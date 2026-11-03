using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core;
using KiNexus.Core.Chats;
using KiNexus.Core.Inference;
using KiNexus.Core.IO;
using KiNexus.Core.Kis;
using KiNexus.Core.Models;
using KiNexus.Core.Paths;
using KiNexus.Core.Settings;
using KiNexus.Core.Training;
using KiNexus.Engines.Training;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>Eine Wahl in den Auswahllisten des Trainings (Wert + Text).</summary>
public sealed record TrainingChoice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Der Training-Tab (Bauplan 8.6): Datensatz, Grundmodell, Training mit Fortschritt und Loss-Kurve, Ergebnis als neue KI.
/// Trainiert wird ein LoRA-Adapter mit PEFT im Python-Worker. llama.cpp lädt ihn danach mit --lora.
/// </summary>
public sealed partial class TrainingViewModel : TabViewModel
{
    private const double ChartWidth = 600;
    private const double ChartHeight = 140;

    private readonly AppPaths _paths;
    private readonly ISettingsService _settings;
    private readonly IChatRepository _chats;
    private readonly IKiRegistry _kis;
    private readonly ModelLibrary _library;
    private readonly EngineManager _engines;
    private readonly TrainingWorker _worker;
    private readonly SecretStore _tokens;
    private readonly IDialogService _dialogs;
    private readonly ILogger<TrainingViewModel> _logger;
    private readonly SynchronizationContext _ui;
    private readonly List<double> _losses = [];

    /// <summary>Loss der früheren Runden (Weitertrainieren), für die Kurve über alle Runden.</summary>
    private readonly List<double> _previousLosses = [];
    private TrainingPaths? _run;
    private TrainingBaseModel? _runModel;

    /// <summary>Die laufende Runde: Name, Nummer und Ausgangsversion (null = neu begonnen).</summary>
    private (string Name, int Number, AdapterVersion? Parent, int Rank, TrainingOptions Options, int Examples)? _runVersion;
    private IReadOnlyList<AdapterLineage> _lineages = [];
    private bool _finished;
    private bool _checked;

    public TrainingViewModel(AppPaths paths, ISettingsService settings, IChatRepository chats, IKiRegistry kis, ModelLibrary library,
        EngineManager engines, TrainingWorker worker, SecretStore tokens, IDialogService dialogs, ILogger<TrainingViewModel> logger)
        : base("training", "Training", Glyphs.Training)
    {
        _paths = paths;
        _settings = settings;
        _chats = chats;
        _kis = kis;
        _library = library;
        _engines = engines;
        _worker = worker;
        _tokens = tokens;
        _dialogs = dialogs;
        _logger = logger;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _worker.EventReceived += trainingEvent => _ui.Post(_ => OnTrainingEvent(trainingEvent), null);

        _selectedBaseModel = BaseModels[0];
        _epochs = EpochChoices[2];
        _learningRate = LearningRateChoices[1];
        _loraRank = RankChoices[1];
        _runName = "mein-adapter";
        _selectedStart = StartChoices[0];
    }

    /// <summary>Das Hauptfenster: zum Chat mit der neuen KI wechseln. Setzt das MainViewModel.</summary>
    public IChatShell? Shell { get; set; }

    private string ModelsDirectory => _paths.Resolve(_settings.Current.Paths.Models);

    private TrainingInstallation Installation => TrainingInstallation.For(_paths, _paths.Resolve(_settings.Current.Paths.Engines));

    private string DatasetFile => Path.Combine(_paths.DataDirectory, "training", "datensatz.jsonl");

    private string RunsDirectory => Path.Combine(_paths.DataDirectory, "training", "runs");

    // ----------------------------------------------------------- Einrichtung

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSetupProblem))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _setupProblem = "";

    [ObservableProperty] private string _checkText = "";

    public bool HasSetupProblem => SetupProblem.Length > 0;

    /// <summary>Bei jedem Öffnen: frühere Trainings suchen. Beim ersten Mal auch Datensatz laden und prüfen, ob das Training eingerichtet ist.</summary>
    public async Task ActivateAsync()
    {
        if (!IsTraining)
        {
            RefreshLineages();
        }

        if (_checked)
        {
            return;
        }

        _checked = true;
        try
        {
            foreach (var example in await TrainingDataset.LoadAsync(DatasetFile, CancellationToken.None))
            {
                Examples.Add(example);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus("Der Datensatz konnte nicht gelesen werden: " + ex.Message, isError: true);
        }

        OnExamplesChanged(save: false);
        await CheckSetupAsync();
    }

    [RelayCommand]
    private async Task CheckSetupAsync()
    {
        SetupProblem = Installation.Problem() ?? "";
        UpdateBaseModelInfo();
        if (HasSetupProblem)
        {
            CheckText = "";
            return;
        }

        CheckText = "Prüfe PyTorch und die Grafikkarte …";
        try
        {
            var check = await TrainingWorker.CheckAsync(Installation, CancellationToken.None);
            CheckText = check.Ok
                ? string.Create(CultureInfo.InvariantCulture, $"Bereit: {check.Gpu} ({check.VramGb:0.#} GB VRAM), PyTorch {check.Torch}.")
                : "PyTorch findet die Grafikkarte nicht: " + check.Message;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Prüfung des Trainings fehlgeschlagen.");
            CheckText = "Prüfung fehlgeschlagen: " + ex.Message;
        }
    }

    // ----------------------------------------------------------- 1. Datensatz

    public ObservableCollection<TrainingExample> Examples { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UpdateExampleCommand), nameof(DeleteExampleCommand))]
    private TrainingExample? _selectedExample;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddExampleCommand), nameof(UpdateExampleCommand))]
    private string _editQuestion = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddExampleCommand), nameof(UpdateExampleCommand))]
    private string _editAnswer = "";

    [ObservableProperty] private string _datasetInfo = "";

    partial void OnSelectedExampleChanged(TrainingExample? value)
    {
        if (value is not null)
        {
            EditQuestion = value.Question;
            EditAnswer = value.Answer;
        }
    }

    private bool CanAdd => EditQuestion.Trim().Length > 0 && EditAnswer.Trim().Length > 0;

    private bool CanUpdate => SelectedExample is not null && CanAdd;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddExample()
    {
        var example = new TrainingExample(EditQuestion.Trim(), EditAnswer.Trim());
        Examples.Add(example);
        SelectedExample = null;
        EditQuestion = "";
        EditAnswer = "";
        OnExamplesChanged();
    }

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private void UpdateExample()
    {
        if (SelectedExample is not { } old)
        {
            return;
        }

        var index = Examples.IndexOf(old);
        var updated = old with { Question = EditQuestion.Trim(), Answer = EditAnswer.Trim() };
        Examples[index] = updated;
        SelectedExample = updated;
        OnExamplesChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteExample()
    {
        if (SelectedExample is { } example)
        {
            Examples.Remove(example);
            SelectedExample = null;
            EditQuestion = "";
            EditAnswer = "";
            OnExamplesChanged();
        }
    }

    private bool HasSelection => SelectedExample is not null;

    [RelayCommand]
    private async Task ImportFileAsync()
    {
        if (_dialogs.PickDatasetFile() is not { } file)
        {
            return;
        }

        try
        {
            var import = TrainingDataset.Parse(await File.ReadAllLinesAsync(file));
            AddNew(import.Examples, out var added);
            ShowStatus($"{added} Beispiele aus {Path.GetFileName(file)} übernommen."
                + (import.Skipped > 0 ? $" {import.Skipped} Zeilen passten nicht (keine Frage und Antwort)." : "")
                + (import.Examples.Count > added ? $" {import.Examples.Count - added} waren schon da." : ""));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus("Import fehlgeschlagen: " + ex.Message, isError: true);
        }
    }

    /// <summary>Alle Antworten mit Daumen hoch, je mit der Frage davor (Bauplan 8.2 und 8.6).</summary>
    [RelayCommand]
    private async Task ImportLikedAsync()
    {
        var liked = await _chats.GetLikedExamplesAsync(CancellationToken.None);
        AddNew(liked, out var added);
        ShowStatus(liked.Count == 0
            ? "Keine Antworten mit Daumen hoch gefunden. Bewerte im Chat gute Antworten mit 👍, dann kannst du sie hier übernehmen."
            : $"{added} Antworten mit Daumen hoch übernommen." + (liked.Count > added ? $" {liked.Count - added} waren schon da." : ""));
    }

    [RelayCommand]
    private void ClearExamples()
    {
        if (Examples.Count > 0 && _dialogs.Confirm("Datensatz leeren", $"Alle {Examples.Count} Beispiele löschen?", "Löschen"))
        {
            Examples.Clear();
            OnExamplesChanged();
        }
    }

    private void AddNew(IEnumerable<TrainingExample> examples, out int added)
    {
        added = 0;
        foreach (var example in examples)
        {
            if (!Examples.Any(e => e.Question == example.Question && e.Answer == example.Answer))
            {
                Examples.Add(example);
                added++;
            }
        }

        OnExamplesChanged();
    }

    /// <summary>Der Datensatz wird bei jeder Änderung gespeichert (data/training/datensatz.jsonl).</summary>
    private void OnExamplesChanged(bool save = true)
    {
        DatasetInfo = Examples.Count switch
        {
            0 => "Noch keine Beispiele. Für einen ersten Test reichen 10–30 gute Beispiele, für einen deutlichen Stil eher 100 und mehr.",
            < 10 => $"{Examples.Count} Beispiele. Etwas wenig: Nimm mindestens 10.",
            _ => $"{Examples.Count} Beispiele.",
        };
        StartCommand.NotifyCanExecuteChanged();
        if (save)
        {
            _ = SaveDatasetAsync();
        }
    }

    private async Task SaveDatasetAsync()
    {
        try
        {
            await TrainingDataset.SaveAsync(DatasetFile, [.. Examples], CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Datensatz nicht gespeichert.");
            ShowStatus("Der Datensatz konnte nicht gespeichert werden: " + ex.Message, isError: true);
        }
    }

    // ----------------------------------------------------------- 2. Grundmodell

    public IReadOnlyList<TrainingBaseModel> BaseModels => TrainingModels.Recommended;

    [ObservableProperty] private TrainingBaseModel _selectedBaseModel;

    [ObservableProperty] private string _baseModelInfo = "";

    partial void OnSelectedBaseModelChanged(TrainingBaseModel value) => UpdateBaseModelInfo();

    private void UpdateBaseModelInfo()
    {
        var model = SelectedBaseModel;
        var vram = TrainingModels.EstimateVramGb(model.ParametersB, model.Use4Bit);
        var downloaded = File.Exists(Path.Combine(ModelsDirectory, "hf", model.Slug, "config.json"));
        BaseModelInfo = string.Create(CultureInfo.InvariantCulture,
            $"{model.Note} Braucht beim Training ca. {vram:0.#} GB VRAM. ")
            + (downloaded ? "Das Originalmodell ist schon da." : $"Das Originalmodell ({model.DownloadGb.ToString("0.#", CultureInfo.InvariantCulture)} GB) lädt die App beim Start von Hugging Face.");
    }

    // ----------------------------------------------------------- Weitertrainieren (Training 2, T1)

    /// <summary>"Neu beginnen" und je ein Eintrag pro früherem Adapter, dessen Grundmodell die App trainieren kann.</summary>
    public ObservableCollection<TrainingChoice<AdapterLineage?>> StartChoices { get; } = [new(null, "Neu beginnen")];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResuming), nameof(IsNewStart), nameof(ResumeInfo))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private TrainingChoice<AdapterLineage?> _selectedStart;

    public bool IsResuming => SelectedStart.Value is not null;

    /// <summary>Grundmodell, Adapter-Grösse und Name sind beim Weitertrainieren fest.</summary>
    public bool IsNewStart => !IsResuming;

    public string ResumeInfo => SelectedStart.Value is not { } lineage
        ? "Ein neuer, leerer Adapter. Der Name darf noch nicht vergeben sein."
        : $"Trainiert v{lineage.Latest.Number} von \"{lineage.Name}\" weiter und speichert das Ergebnis als v{lineage.Versions.Max(v => v.Number) + 1}. "
          + "Grundmodell, Adapter-Grösse und Name bleiben gleich. Neue Beispiele und mehr Durchgänge gehen. "
          + "Das Lerntempo ist vorsichtiger eingestellt, damit die KI das Bisherige nicht vergisst.";

    partial void OnSelectedStartChanged(TrainingChoice<AdapterLineage?> value)
    {
        if (value.Value is { } lineage)
        {
            if (BaseModels.FirstOrDefault(m => string.Equals(m.Repo, lineage.BaseRepo, StringComparison.OrdinalIgnoreCase)) is { } model)
            {
                SelectedBaseModel = model;
            }

            RunName = lineage.Name;
            LoraRank = RankChoices.FirstOrDefault(r => r.Value == lineage.Latest.LoraRank)
                ?? new TrainingChoice<int>(lineage.Latest.LoraRank, $"Rang {lineage.Latest.LoraRank} (wie bisher)");
            LearningRate = LearningRateChoices[0];
        }
        else
        {
            LearningRate = LearningRateChoices[1];
        }

        ShowVersions(value.Value);
        _previousLosses.Clear();
        if (!IsTraining)
        {
            _losses.Clear();
            foreach (var version in value.Value?.Chain() ?? [])
            {
                _previousLosses.AddRange(version.Losses);
            }

            LossPoints = Chart(_previousLosses);
            LossText = value.Value is { } shown && _previousLosses.Count > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Bisherige Runden von \"{shown.Name}\": Loss {_previousLosses[0]:0.00} → {_previousLosses[^1]:0.00}.")
                : "";
        }
    }

    /// <summary>Frühere Trainings neu suchen (z.B. nach einer Runde). Die Wahl bleibt, wenn es den Adapter noch gibt.</summary>
    private void RefreshLineages()
    {
        try
        {
            _lineages = TrainingHistory.Scan(RunsDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Frühere Trainings nicht lesbar.");
            _lineages = [];
        }

        var keep = SelectedStart.Value?.Name;
        while (StartChoices.Count > 1)
        {
            StartChoices.RemoveAt(1);
        }

        foreach (var lineage in _lineages.Where(l => BaseModels.Any(m => string.Equals(m.Repo, l.BaseRepo, StringComparison.OrdinalIgnoreCase))))
        {
            var model = BaseModels.First(m => string.Equals(m.Repo, lineage.BaseRepo, StringComparison.OrdinalIgnoreCase));
            StartChoices.Add(new(lineage, $"Weitertrainieren: {lineage.Name} (v{lineage.Latest.Number}, {model.Name})"));
        }

        SelectedStart = StartChoices.FirstOrDefault(c => c.Value?.Name == keep) ?? StartChoices[0];
        OnSelectedStartChanged(SelectedStart);
    }

    // ----------------------------------------------------------- Versionen

    /// <summary>Die Versionen des gewählten Adapters, die neueste zuerst.</summary>
    public ObservableCollection<AdapterVersion> Versions { get; } = [];

    public bool HasVersions => Versions.Count > 0;

    /// <summary>KIs, die eine Version dieses Adapters nutzen. Für sie kann man die Version wechseln.</summary>
    public ObservableCollection<KiProfile> VersionKis { get; } = [];

    public bool HasVersionKis => VersionKis.Count > 0;

    public bool HasNoVersionKis => !HasVersionKis;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VersionKiInfo))]
    private KiProfile? _selectedVersionKi;

    public string VersionKiInfo => SelectedVersionKi is { } ki && _lineageForVersions is { } lineage
        ? $"{ki.DisplayName} nutzt jetzt {(CurrentVersionOf(ki, lineage) is { } used ? $"v{used.Number}" : "keine dieser Versionen")}."
        : "";

    private AdapterLineage? _lineageForVersions;

    private void ShowVersions(AdapterLineage? lineage)
    {
        _lineageForVersions = lineage;
        Versions.Clear();
        foreach (var version in (lineage?.Versions ?? []).Reverse())
        {
            Versions.Add(version);
        }

        var keep = SelectedVersionKi?.Id;
        VersionKis.Clear();
        foreach (var ki in _kis.All.Where(k => lineage is not null && CurrentVersionOf(k, lineage) is not null))
        {
            VersionKis.Add(ki);
        }

        SelectedVersionKi = VersionKis.FirstOrDefault(k => k.Id == keep) ?? VersionKis.FirstOrDefault();
        OnPropertyChanged(nameof(HasVersions));
        OnPropertyChanged(nameof(HasVersionKis));
        OnPropertyChanged(nameof(HasNoVersionKis));
        OnPropertyChanged(nameof(VersionKiInfo));
    }

    /// <summary>Welche Version dieses Adapters nutzt die KI? Null = keine.</summary>
    private AdapterVersion? CurrentVersionOf(KiProfile ki, AdapterLineage lineage) =>
        ki.LoraAdapterPath is { Length: > 0 } path
            ? lineage.Versions.FirstOrDefault(v => SamePath(_paths.Resolve(path), v.AdapterGguf))
            : null;

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Die gewählte KI nutzt ab jetzt diese Version (auch zurück zu einer älteren). Ist sie geladen, wird sie entladen,
    /// damit llama.cpp beim nächsten Chat den neuen Adapter lädt.
    /// </summary>
    [RelayCommand]
    private async Task UseVersionAsync(AdapterVersion version)
    {
        if (SelectedVersionKi is not { } chosen || _kis.Find(chosen.Id) is not { } current)
        {
            ShowStatus("Wähle zuerst die KI, die diese Version nutzen soll.", isError: true);
            return;
        }

        var updated = JsonDefaults.Clone(current);
        updated.LoraAdapterPath = ToKiPath(version.AdapterGguf);
        try
        {
            if (_engines.LoadedKi?.Id == current.Id)
            {
                await _engines.UnloadAsync(CancellationToken.None);
            }

            await _kis.SaveAsync(updated, CancellationToken.None);
            ShowStatus($"{updated.DisplayName} nutzt jetzt v{version.Number} von \"{version.Name}\".");
            ShowVersions(_lineageForVersions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowStatus("Umstellen fehlgeschlagen: " + ex.Message, isError: true);
        }
    }

    // ----------------------------------------------------------- 3. Training

    public IReadOnlyList<TrainingChoice<int>> EpochChoices { get; } =
    [
        new(1, "1 Durchgang"),
        new(2, "2 Durchgänge"),
        new(3, "3 Durchgänge (Standard)"),
        new(5, "5 Durchgänge"),
        new(8, "8 Durchgänge (kleiner Datensatz)"),
    ];

    public IReadOnlyList<TrainingChoice<double>> LearningRateChoices { get; } =
    [
        new(1e-4, "Vorsichtig (0.0001)"),
        new(2e-4, "Standard (0.0002)"),
        new(5e-4, "Schnell (0.0005)"),
    ];

    public IReadOnlyList<TrainingChoice<int>> RankChoices { get; } =
    [
        new(8, "Klein (Rang 8)"),
        new(16, "Mittel (Rang 16, Standard)"),
        new(32, "Gross (Rang 32)"),
    ];

    [ObservableProperty] private TrainingChoice<int> _epochs;

    [ObservableProperty] private TrainingChoice<double> _learningRate;

    [ObservableProperty] private TrainingChoice<int> _loraRank;

    /// <summary>Name des Adapters, wird zum Dateinamen (models/&lt;modell&gt;/lora/&lt;name&gt;.gguf).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _runName;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isTraining;

    public bool IsIdle => !IsTraining;

    [ObservableProperty] private string _stageText = "";

    [ObservableProperty] private double _progressValue;

    [ObservableProperty] private string _progressText = "";

    [ObservableProperty] private PointCollection _lossPoints = [];

    [ObservableProperty] private string _lossText = "";

    private bool CanStart => !IsTraining && !HasSetupProblem && Examples.Count >= 2 && KiIds.Slug(RunName).Length > 0;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var model = SelectedBaseModel;
        var resume = SelectedStart.Value;
        var name = resume?.Name ?? KiIds.Slug(RunName);
        if (resume is null && _lineages.FirstOrDefault(l => l.Name == name) is { } existing)
        {
            ShowStatus($"Einen Adapter \"{name}\" gibt es schon (bis v{existing.Latest.Number}). Wähle oben „Weitertrainieren: {name}“ oder einen anderen Namen.",
                isError: true);
            return;
        }

        if (resume is not null && !string.Equals(resume.BaseRepo, model.Repo, StringComparison.OrdinalIgnoreCase))
        {
            ShowStatus($"\"{name}\" wurde mit {resume.BaseRepo} trainiert. Weitertrainieren geht nur mit demselben Grundmodell.", isError: true);
            return;
        }

        // Jede Runde bekommt einen eigenen Ordner und einen eigenen Adapter. Nichts Fertiges wird überschrieben.
        var (number, runDir) = TrainingHistory.Next(RunsDirectory, name, resume);
        var run = TrainingPaths.ForVersion(ModelsDirectory, model, name, number, runDir);

        // Für das Training braucht die Grafikkarte fast den ganzen Speicher.
        if (_engines.LoadedKi is { } loaded)
        {
            if (!_dialogs.Confirm("Modell entladen", $"{loaded.DisplayName} ist geladen und belegt Grafikspeicher. Für das Training wird es entladen.", "Entladen und starten"))
            {
                return;
            }

            await _engines.UnloadAsync(CancellationToken.None);
        }

        try
        {
            // Ein abgebrochener Versuch derselben Nummer (ohne fertigen Adapter) darf weg.
            if (Directory.Exists(run.RunDir))
            {
                Directory.Delete(run.RunDir, recursive: true);
            }

            Directory.CreateDirectory(run.RunDir);
            await TrainingDataset.SaveAsync(run.DatasetFile, [.. Examples], CancellationToken.None);
            var rank = resume?.Latest.LoraRank ?? LoraRank.Value;
            var options = new TrainingOptions(Epochs.Value, LearningRate.Value, rank);
            var download = !File.Exists(Path.Combine(run.BaseModelDir, "config.json"));
            var config = TrainingProtocol.BuildConfig(model, options, run, Installation.ConvertScriptsDir, download, resume?.Latest.AdapterDir);
            await File.WriteAllTextAsync(run.ConfigFile, config.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            _run = run;
            _runModel = model;
            _runVersion = (name, number, resume?.Latest, rank, options, Examples.Count);
            _finished = false;
            _losses.Clear();
            LossPoints = Chart(_previousLosses);
            LossText = "";
            ProgressValue = 0;
            ProgressText = "";
            HasResult = false;
            SavedKi = null;
            StageText = "Startet …";
            ShowStatus("");
            IsTraining = true;
            _worker.Start(Installation, run.ConfigFile, run.StopFile, _tokens.Load());
            _logger.LogInformation("Training {Name} v{Number} mit {Repo} gestartet ({Count} Beispiele, weiter von {Parent}).", name, number, model.Repo,
                Examples.Count, resume is null ? "nichts" : $"v{resume.Latest.Number}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogError(ex, "Training konnte nicht starten.");
            IsTraining = false;
            ShowStatus("Das Training konnte nicht starten: " + ex.Message, isError: true);
        }
    }

    [RelayCommand(CanExecute = nameof(IsTraining))]
    private async Task StopAsync()
    {
        StageText = "Wird abgebrochen (nach dem laufenden Schritt) …";
        await _worker.StopAsync();
    }

    private void OnTrainingEvent(TrainingEvent trainingEvent)
    {
        switch (trainingEvent)
        {
            case TrainingStage stage:
                StageText = stage.Text;
                break;
            case TrainingDownload download when download.Total > 0:
                ProgressValue = 100.0 * download.Done / download.Total;
                ProgressText = $"Download: {DisplayFormat.Bytes(download.Done)} von {DisplayFormat.Bytes(download.Total)}";
                break;
            case TrainingProgress progress:
                OnProgress(progress);
                break;
            case TrainingDone done:
                _finished = true;
                IsTraining = false;
                _ = OnDoneAsync(done);
                break;
            case TrainingFailed failed:
                _finished = true;
                IsTraining = false;
                StageText = "Fehler.";
                ShowStatus("Training fehlgeschlagen: " + failed.Message + @" Details: data\logs\training.log", isError: true);
                break;
            case TrainingCancelled:
                _finished = true;
                IsTraining = false;
                StageText = "Abgebrochen. Es wurde nichts gespeichert.";
                break;
            case TrainingExited exited:
                if (!_finished)
                {
                    IsTraining = false;
                    StageText = "Fehler.";
                    ShowStatus($@"Der Trainings-Worker wurde unerwartet beendet (Code {exited.ExitCode}). Details: data\logs\training.log", isError: true);
                }

                _finished = true;
                break;
        }
    }

    private void OnProgress(TrainingProgress progress)
    {
        ProgressValue = 100.0 * progress.Step / Math.Max(1, progress.TotalSteps);
        ProgressText = string.Create(CultureInfo.InvariantCulture,
            $"Schritt {progress.Step} von {progress.TotalSteps} · Durchgang {progress.Epoch}/{progress.Epochs} · noch ca. {Duration(progress.EtaSeconds)} · VRAM {progress.VramGb:0.0} GB");
        _losses.Add(progress.Loss);
        LossText = string.Create(CultureInfo.InvariantCulture,
            $"Loss: Start {_losses[0]:0.00} → jetzt {progress.Loss:0.00}. Kleiner ist besser: Das Modell liegt seltener daneben.")
            + (_previousLosses.Count > 0 ? " Die Kurve zeigt auch die früheren Runden (links)." : "");
        LossPoints = Chart([.. _previousLosses, .. _losses]);
    }

    /// <summary>Die Loss-Kurve als Punkte in einer Fläche von 600 × 140. Oben = grösster Wert.</summary>
    public static PointCollection Chart(IReadOnlyList<double> values)
    {
        var points = new PointCollection();
        if (values.Count == 0)
        {
            return points;
        }

        var max = Math.Max(values.Max(), 0.01);
        for (var i = 0; i < values.Count; i++)
        {
            var x = values.Count == 1 ? 0 : i * ChartWidth / (values.Count - 1);
            var y = ChartHeight - (values[i] / max * (ChartHeight - 6)) - 3;
            points.Add(new Point(Math.Round(x, 1), Math.Round(y, 1)));
        }

        points.Freeze();
        return points;
    }

    private static string Duration(double seconds) =>
        seconds >= 60 ? $"{(int)(seconds / 60)} min {(int)(seconds % 60):00} s" : $"{Math.Max(0, (int)seconds)} s";

    // ----------------------------------------------------------- 4. Ergebnis

    [ObservableProperty] private bool _hasResult;

    [ObservableProperty] private string _resultText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAsKiCommand))]
    private string _newKiName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSaved))]
    [NotifyCanExecuteChangedFor(nameof(OpenChatCommand))]
    private KiProfile? _savedKi;

    public bool IsSaved => SavedKi is not null;

    private async Task OnDoneAsync(TrainingDone done)
    {
        ProgressValue = 100;
        await SaveVersionAsync(done);
        StageText = string.Create(CultureInfo.InvariantCulture, $"Fertig in {Duration(done.Seconds)}.");
        try
        {
            // Das Grundmodell als GGUF ist jetzt ein normales Grundmodell (sichtbar im Create-Hub).
            var folder = Path.GetDirectoryName(done.BaseGguf)!;
            if (!File.Exists(Path.Combine(folder, ModelLibrary.ManifestFileName)) && _runModel is { } model)
            {
                _library.WriteManifestFor(folder, Path.GetFileName(done.BaseGguf), null, "huggingface:" + model.Repo, model.License.ToLowerInvariant());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _logger.LogWarning(ex, "model.json für das trainierte Grundmodell nicht geschrieben.");
        }

        HasResult = true;
        NewKiName = RunName.Trim();
        var number = _runVersion?.Number ?? 1;
        ResultText = string.Create(CultureInfo.InvariantCulture,
            $"Version v{number}. Der Adapter liegt in {ToKiPath(done.AdapterGguf)} ({DisplayFormat.Bytes(new FileInfo(done.AdapterGguf).Length)}). "
            + $"Grundmodell: {ToKiPath(done.BaseGguf)}. Loss am Schluss: {done.FinalLoss:0.00}.")
            + (HasVersionKis ? " Unten bei „Versionen“ kannst du eine bestehende KI auf diese Version umstellen." : "");

        // Ehrliche Grenze (Bauplan Training 2): Fast null Loss heisst auswendig gelernt.
        if (TrainingHistory.LooksMemorized(done.FinalLoss))
        {
            ShowStatus(string.Create(CultureInfo.InvariantCulture,
                $"Achtung: Der Loss ist fast null ({done.FinalLoss:0.000}). Die KI kennt die Beispiele wohl auswendig. Sie wiederholt sich dann leicht oder vergisst Allgemeines. ")
                + "Besser als mehr Durchgänge: neue oder abwechslungsreichere Beispiele.", isError: true);
        }
    }

    /// <summary>Schreibt version.json für die fertige Runde und zeigt die Versionen des Adapters.</summary>
    private async Task SaveVersionAsync(TrainingDone done)
    {
        if (_run is not { } run || _runModel is not { } model || _runVersion is not { } info)
        {
            return;
        }

        var version = new AdapterVersion
        {
            Name = info.Name,
            Number = info.Number,
            Parent = info.Parent?.Number,
            BaseRepo = model.Repo,
            LoraRank = info.Rank,
            Epochs = info.Options.Epochs,
            LearningRate = info.Options.LearningRate,
            ExampleCount = info.Examples,
            FinalLoss = done.FinalLoss,
            Losses = [.. _losses],
            CreatedAt = DateTimeOffset.Now,
            AdapterGguf = done.AdapterGguf,
            RunDir = run.RunDir,
        };
        try
        {
            await TrainingHistory.WriteAsync(version, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "version.json nicht geschrieben.");
        }

        RefreshLineages();
        var lineage = _lineages.FirstOrDefault(l => l.Name == info.Name);
        SelectedStart = StartChoices.FirstOrDefault(c => c.Value?.Name == info.Name) ?? SelectedStart;
        ShowVersions(lineage);
    }

    private bool CanSaveAsKi => HasResult && NewKiName.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSaveAsKi))]
    private async Task SaveAsKiAsync()
    {
        if (_run is not { } run || _runModel is not { } model)
        {
            return;
        }

        // Der Systemprompt wie im Training: So antwortet die KI am ehesten wie gelernt.
        var systems = Examples.Select(e => e.System ?? "").Distinct().ToList();
        var ki = new KiProfile
        {
            Id = KiIds.FromName(NewKiName, _kis.All.Select(k => k.Id)),
            DisplayName = NewKiName.Trim(),
            Description = $"Trainiert auf {model.Name} mit {Examples.Count} Beispielen (LoRA, v{_runVersion?.Number ?? 1})",
            Engine = EngineKind.LlamaCpp,
            BaseModel = { Path = ToKiPath(run.BaseGguf) },
            LoraAdapterPath = ToKiPath(run.AdapterGguf),
            SystemPrompt = systems.Count == 1 ? systems[0] : "",
            // Empfehlung von Qwen3 ohne Thinking: temperature 0.7, top_p 0.8, top_k 20.
            Parameters = { Temperature = 0.7, TopP = 0.8, TopK = 20, ContextLength = 4096, MaxTokens = 1024 },
        };
        try
        {
            await _kis.SaveAsync(ki, CancellationToken.None);
            SavedKi = ki;
            ShowVersions(_lineageForVersions);
            ShowStatus($"Neue KI \"{ki.DisplayName}\" gespeichert. Sie steht jetzt in der KI-Auswahl.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowStatus("Speichern fehlgeschlagen: " + ex.Message, isError: true);
        }
    }

    [RelayCommand(CanExecute = nameof(IsSaved))]
    private void OpenChat()
    {
        if (SavedKi is { } ki && _kis.Find(ki.Id) is { } current)
        {
            Shell?.OpenChatWith(current);
        }
    }

    private string ToKiPath(string fullPath)
    {
        var relative = Path.GetRelativePath(_paths.Root, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? fullPath : relative.Replace('\\', '/');
    }

    // ----------------------------------------------------------- Meldung

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "";

    [ObservableProperty] private bool _statusIsError;

    public bool HasStatus => Status.Length > 0;

    private void ShowStatus(string text, bool isError = false)
    {
        Status = text;
        StatusIsError = isError;
    }

    [RelayCommand]
    private void DismissStatus() => ShowStatus("");

    /// <summary>Beim Schliessen der App: ein laufendes Training beenden.</summary>
    public ValueTask DisposeAsync() => _worker.DisposeAsync();
}
