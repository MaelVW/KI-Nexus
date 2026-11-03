using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core;
using KiNexus.Core.Inference;
using KiNexus.Core.Models;
using KiNexus.Core.Paths;
using KiNexus.Core.Resources;
using KiNexus.Core.Settings;
using KiNexus.Core.Video;
using KiNexus.Engines.Training;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>Ein Bildformat zur Auswahl, z.B. "Quer 768×512".</summary>
public sealed record VideoFormat(int Width, int Height, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Ein Ablauf zur Auswahl: distilled (8 Schritte, CFG 1) oder dev (20 Schritte, CFG 4).</summary>
public sealed record VideoProcess(bool IsDev, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Was das Studio gerade erzeugt (Bauplan Bild, B2).</summary>
public enum StudioMode
{
    Video,
    Image,
}

/// <summary>Welche Einträge die Galerie zeigt.</summary>
public enum GalleryFilter
{
    All,
    Videos,
    Images,
}

/// <summary>Ein Auftrag in der Warteschlange: ein Video oder Bilder. Beide laufen nacheinander über ComfyUI.</summary>
public abstract record StudioJob
{
    public abstract string Label { get; }

    /// <summary>Schritte beim Entrauschen, für die Schätzung der Restzeit.</summary>
    public abstract int Steps { get; }

    public sealed override string ToString() => Label;

    protected static string Short(string text) => text.Length <= 70 ? text : text[..67] + "…";
}

/// <summary>Ein Video-Auftrag: was, mit welchem Modell und Ablauf.</summary>
public sealed record VideoJob(VideoRequest Request, VideoModelSet Models) : StudioJob
{
    /// <summary>z.B. "768×512 · 4 s · LTX-2.3 distilled (Q4_K_M) · A golden retriever …".</summary>
    public override string Label => string.Create(CultureInfo.InvariantCulture,
        $"Video · {Request.Width}×{Request.Height} · {Request.Seconds:0} s{(Request.StartImagePath is null ? "" : " · mit Startbild")} · {Models.Name} · {Short(Request.Prompt)}");

    public override int Steps => Models.Steps;
}

/// <summary>Ein Bild-Auftrag: 1 bis 4 Bilder mit demselben Prompt.</summary>
public sealed record ImageJob(ImageRequest Request, ImageModelSet Models) : StudioJob
{
    /// <summary>z.B. "2 Bilder · 1024×1024 · FLUX.2 klein 4B (Q8_0) · A golden retriever …".</summary>
    public override string Label => string.Create(CultureInfo.InvariantCulture,
        $"{(Request.Count == 1 ? "1 Bild" : $"{Request.Count} Bilder")} · {Request.Width}×{Request.Height}{References}{(Request.SourceImage is null ? "" : $" · Vorlage {Request.Strength:0.00}")} · {Models.Name} · {Short(Request.Prompt)}");

    private string References => Request.ReferenceImages?.Count switch
    {
        null or 0 => "",
        1 => " · 1 Referenz",
        var n => $" · {n} Referenzen",
    };

    /// <summary>Mit Vorlage rechnet FLUX.2 nur einen Teil der Stufen (z.B. 3 von 4 bei Stärke 0.8), Z-Image immer alle.</summary>
    public override int Steps => Request.SourceImage is not null && Models.Family == ImageModelFamily.Flux2Klein
        ? Math.Max(1, (int)Math.Round(Models.Steps * Request.Strength, MidpointRounding.ToEven))
        : Models.Steps;
}

/// <summary>Eine Länge zur Auswahl, z.B. "4 Sekunden".</summary>
public sealed record VideoLength(double Seconds, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Der Video-Tab (Bauplan Video, Kapitel 7 und 8): Auftrag mit Speicher-Ampel, Fortschritt mit Abbrechen,
/// Galerie und Player. Erzeugt wird über <see cref="IVideoBackend"/> (ComfyUI). Während ein Video entsteht,
/// ist der Chat gesperrt: Der Grafikspeicher reicht nicht für beides.
/// </summary>
public sealed partial class VideoViewModel : TabViewModel, IAsyncDisposable
{
    private const double FrameRate = 25;
    private static readonly TimeSpan FitInterval = TimeSpan.FromSeconds(5);

    private readonly IVideoBackend _backend;
    private readonly VideoLibrary _library;
    private readonly ImageLibrary _images;
    private readonly ModelLibrary _models;
    private readonly AppPaths _paths;
    private readonly EngineManager _engines;
    private readonly TrainingWorker _training;
    private readonly ISettingsService _settings;
    private readonly IResourceMonitor _resources;
    private readonly IDialogService _dialogs;
    private readonly IShellService _shell;
    private readonly ILogger<VideoViewModel> _logger;
    private readonly SynchronizationContext _ui;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Stopwatch _clock = new();
    private readonly List<double> _stepTimes = [];
    private CancellationTokenSource? _job;
    private CancellationTokenSource? _idleStop;
    private bool _activated;
    private bool _queueRunning;
    private string? _promptBeforeImprove;
    private bool _disposed;

    /// <summary>Der Prompt der anderen Art (Video oder Bild). Beim Umschalten tauschen die beiden.</summary>
    private string _otherPrompt = "";

    /// <summary>Alle Einträge der Galerie, ungefiltert. <see cref="Videos"/> zeigt davon, was der Filter durchlässt.</summary>
    private readonly List<VideoEntry> _allEntries = [];

    /// <summary>Der laufende Auftrag (für Restzeit und Texte).</summary>
    private StudioJob? _current;

    public VideoViewModel(IVideoBackend backend, VideoLibrary library, ImageLibrary images, ModelLibrary models, AppPaths paths, EngineManager engines,
        TrainingWorker training, ISettingsService settings, IResourceMonitor resources, IDialogService dialogs, IShellService shell, ILogger<VideoViewModel> logger)
        : base("video", "Studio", Glyphs.Video)
    {
        _backend = backend;
        _library = library;
        _images = images;
        _models = models;
        _paths = paths;
        _engines = engines;
        _training = training;
        _settings = settings;
        _resources = resources;
        _dialogs = dialogs;
        _shell = shell;
        _logger = logger;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _selectedFormat = Formats[0];
        _selectedLength = Lengths[2];
        _selectedImageFormat = ImageFormats[0];
        _backend.Changed += (_, _) => _ui.Post(_ => BackendStatus = Describe(_backend), null);
        _backendStatus = Describe(_backend);
    }

    /// <summary>Die Dateien des gewählten Videomodells. Ohne Wahl: das offizielle LTX-2.3 aus V0.</summary>
    public VideoModelSet Models => SelectedModel?.ToModelSet(SelectedProcess?.IsDev) ?? VideoModelSet.Ltx23Distilled;

    public IReadOnlyList<VideoProcess> Processes { get; } =
    [
        new(false, "Schnell: distilled (8 Schritte)"),
        new(true, "Genau: dev (20 Schritte mit CFG 4, ca. 2- bis 3-mal langsamer)"),
    ];

    /// <summary>Muss zum Modell passen: Ein dev-Modell mit dem distilled-Ablauf gibt ein verrauschtes Video.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDevProcess))]
    private VideoProcess? _selectedProcess;

    public bool IsDevProcess => SelectedProcess?.IsDev == true;

    /// <summary>Was nicht vorkommen soll. Wirkt nur beim dev-Ablauf. Leer = Standard von Lightricks.</summary>
    [ObservableProperty] private string _negativePrompt = "";

    public string NegativePromptHint => "Leer = Standard: " + VideoModelSet.DevNegativePrompt;

    private string VideoModelsDirectory => Path.Combine(_paths.Resolve(_settings.Current.Paths.Models), "video");

    /// <summary>Bei jedem Öffnen: Videomodelle neu suchen (z.B. nach einem Download). Beim ersten Mal auch Galerie und Ampel.</summary>
    public async Task ActivateAsync()
    {
        UpdateFit();
        await RefreshImageModelsAsync();
        await RefreshModelsAsync();
        if (_activated)
        {
            return;
        }

        _activated = true;
        await LoadGalleryAsync();
        _ = RunFitLoopAsync(_lifetime.Token);
    }

    // ----------------------------------------------------------- Umschalter Video | Bild

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsImageMode), nameof(IsVideoMode), nameof(GenerateText), nameof(PromptHint), nameof(PromptName))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(EnqueueCommand), nameof(ImprovePromptCommand), nameof(UndoPromptCommand))]
    private StudioMode _mode;

    public bool IsImageMode => Mode == StudioMode.Image;

    public bool IsVideoMode => Mode == StudioMode.Video;

    /// <summary>Ehrliche Grenzen für Bilder (gemessen in B0, docs/11-Bild.md).</summary>
    public string ImageLimits =>
        "Bilder aus einer Beschreibung, mit FLUX.2 klein (oder Z-Image) über ComfyUI. Ein Bild in 1024×1024 dauert ca. 10 s, mit Z-Image ca. 25 s. "
        + "Beim ersten Auftrag startet zuerst ComfyUI (ca. 15 s). Während Bilder entstehen, geht der Chat nicht.";

    public string GenerateText => IsImageMode ? "Bilder erzeugen" : "Video erzeugen";

    public string PromptName => IsImageMode ? "Beschreibung des Bilds" : "Beschreibung des Videos";

    public string PromptHint => IsImageMode
        ? "Beschreib Motiv, Umgebung, Licht und Stil. Auf Englisch klappt es am besten, z.B. „A red fox in a snowy forest at sunrise, soft light, photo.“ Text im Bild in Anführungszeichen."
        : "Beschreib Szene, Bewegung, Kamera und Ton. Auf Englisch klappt es am besten, z.B. „A red fox jumps over a snowy log, crunching snow, soft wind, handheld camera.“";

    partial void OnModeChanged(StudioMode value)
    {
        // Jede Art behält ihren eigenen Prompt: Ein Video-Prompt beschreibt Bewegung und Ton, ein Bild-Prompt nicht.
        (Prompt, _otherPrompt) = (_otherPrompt, Prompt);
        _promptBeforeImprove = null;
        CanUndoPrompt = false;
        CheckSetup();
        UpdateFit();
    }

    // ----------------------------------------------------------- Bild-Auftrag

    /// <summary>
    /// Die Bildmodelle: die eingebauten in models/image und passende Grundmodelle aus models/ (z.B. von Hugging Face),
    /// erkannt im GGUF-Kopf (<see cref="ImageModelCatalog"/>).
    /// </summary>
    public ObservableCollection<ImageModelChoice> ImageModels { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedImageModel), nameof(ImageModelNote))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(EnqueueCommand))]
    private ImageModelChoice? _selectedImageChoice;

    /// <summary>Dateien und Einstellungen des gewählten Bildmodells. Ohne Wahl: FLUX.2 klein 4B (dann hilft die Einrichtung).</summary>
    public ImageModelSet SelectedImageModel => SelectedImageChoice?.Set ?? ImageModelSet.Flux2Klein4B;

    public string ImageModelNote => SelectedImageChoice?.Note
        ?? "Noch kein Bildmodell gefunden. Richte Bilder mit scripts\\setup-video.ps1 -Images ein oder lade FLUX.2 klein oder Z-Image im Create-Hub.";

    partial void OnSelectedImageChoiceChanged(ImageModelChoice? value)
    {
        CheckSetup();
        OnPropertyChanged(nameof(ReferencesSupported));
        OnPropertyChanged(nameof(ReferenceHint));
        AddReferenceCommand.NotifyCanExecuteChanged();
    }

    // ----------------------------------------------------------- Vorlage: Bild zu Bild (nur Bild)

    /// <summary>Bild zu Bild: Ausgangspunkt für das neue Bild. Null = aus dem Nichts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSource))]
    private string? _sourceImagePath;

    public bool HasSource => SourceImagePath is not null;

    /// <summary>0.2 = kaum verändert, 1.0 = ganz neu.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrengthText))]
    private double _strength = ImageRequest.DefaultStrength;

    public string StrengthText => string.Create(CultureInfo.InvariantCulture, $"Stärke {Strength:0.00}") + Strength switch
    {
        < 0.5 => ": kleine Änderungen",
        < 0.75 => ": Motiv und Umgebung bleiben, Details ändern sich",
        < 0.9 => ": Pose und Aufbau bleiben, Farben meist auch",
        _ => ": grosse Änderungen (z.B. andere Jahreszeit), nur der grobe Aufbau bleibt",
    };

    public string SourceHint =>
        "Das Bild wird auf das Format zugeschnitten (Mitte) und ist der Ausgangspunkt. Beschreib im Prompt das ganze neue Bild, nicht nur die Änderung. "
        + "Anders als Referenzbilder geht das auch mit Z-Image.";

    [RelayCommand]
    private void PickSource()
    {
        if (_dialogs.PickImages("Vorlage für das Bild wählen").FirstOrDefault() is { } image)
        {
            SourceImagePath = image;
        }
    }

    [RelayCommand]
    private void ClearSource() => SourceImagePath = null;

    /// <summary>Das gewählte Bild wird Vorlage (Bild zu Bild). Das Studio wechselt auf Bild.</summary>
    [RelayCommand(CanExecute = nameof(HasSelectedImage))]
    private void UseAsSource()
    {
        if (SelectedVideo is not { IsImage: true } image)
        {
            return;
        }

        Mode = StudioMode.Image;
        SourceImagePath = image.VideoPath;
        ShowStatus("Das Bild ist jetzt die Vorlage. Beschreib links das neue Bild, wähle die Stärke und klick auf „Bilder erzeugen“.");
    }

    // ----------------------------------------------------------- Referenzbilder (nur Bild)

    /// <summary>Bilder, an denen sich das Bildmodell orientiert (höchstens <see cref="ImageRequest.MaxReferences"/>).</summary>
    public ObservableCollection<string> ReferenceImages { get; } = [];

    public bool HasReferences => ReferenceImages.Count > 0;

    public bool ReferencesSupported => SelectedImageModel.SupportsReferences;

    public string ReferenceHint => ReferencesSupported
        ? $"Bis {ImageRequest.MaxReferences} Bilder, an denen sich das Modell orientiert (Person, Tier, Ort, Stil). Sag im Prompt, was damit passieren soll, "
          + "z.B. „The dog from the first image in front of the hut from the second image“. Jede Referenz kostet ca. 5 s. "
          + "Bilder von anderen Personen nur mit deren Einverständnis."
        : $"{SelectedImageModel.Name} kann keine Referenzbilder nutzen. Dafür FLUX.2 klein wählen.";

    private bool CanAddReference => ReferencesSupported && ReferenceImages.Count < ImageRequest.MaxReferences;

    [RelayCommand(CanExecute = nameof(CanAddReference))]
    private void AddReference()
    {
        foreach (var image in _dialogs.PickImages("Referenzbilder wählen").Take(ImageRequest.MaxReferences - ReferenceImages.Count))
        {
            AddReferencePath(image);
        }
    }

    [RelayCommand]
    private void RemoveReference(string path)
    {
        ReferenceImages.Remove(path);
        OnReferencesChanged();
    }

    [RelayCommand]
    private void ClearReferences()
    {
        ReferenceImages.Clear();
        OnReferencesChanged();
    }

    private void AddReferencePath(string path)
    {
        if (ReferenceImages.Count >= ImageRequest.MaxReferences || ReferenceImages.Any(r => SamePath(r, path)))
        {
            return;
        }

        ReferenceImages.Add(path);
        OnReferencesChanged();
    }

    private void OnReferencesChanged()
    {
        OnPropertyChanged(nameof(HasReferences));
        AddReferenceCommand.NotifyCanExecuteChanged();
        UseAsReferenceCommand.NotifyCanExecuteChanged();
    }

    public IReadOnlyList<VideoFormat> ImageFormats { get; } = [.. ImageRequest.Formats.Select(f => new VideoFormat(f.Width, f.Height, f.Name))];

    [ObservableProperty] private VideoFormat _selectedImageFormat;

    public IReadOnlyList<int> ImageCounts { get; } = [.. Enumerable.Range(1, ImageRequest.MaxCount)];

    [ObservableProperty] private int _imageCount = 1;

    private string ImageModelsDirectory => Path.Combine(_paths.Resolve(_settings.Current.Paths.Models), "image");

    private async Task RefreshImageModelsAsync()
    {
        var directory = ImageModelsDirectory;
        IReadOnlyList<ImageModelChoice> found;
        try
        {
            found = await Task.Run(() => ImageModelCatalog.Find(directory, _models.Scan()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Bildmodelle konnten nicht gesucht werden.");
            found = [];
        }

        var keep = SelectedImageChoice?.ModelPath;
        ImageModels.Clear();
        foreach (var choice in found)
        {
            ImageModels.Add(choice);
        }

        SelectedImageChoice = ImageModels.FirstOrDefault(m => keep is not null && SamePath(m.ModelPath, keep))
            ?? ImageModels.FirstOrDefault(m => m.IsUsable)
            ?? ImageModels.FirstOrDefault();
        CheckSetup();
    }

    // ----------------------------------------------------------- Videomodell

    public ObservableCollection<VideoModelChoice> VideoModels { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelNote))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(RepeatSameSeedCommand), nameof(RepeatNewSeedCommand), nameof(EnqueueCommand))]
    private VideoModelChoice? _selectedModel;

    /// <summary>Ein Satz zum gewählten Modell, z.B. "LTX-2.3 distilled, mit Ton. Nutzbar im Video-Tab."</summary>
    public string ModelNote => SelectedModel?.Note ?? "Noch kein Videomodell gefunden. Richte Video mit scripts\\setup-video.ps1 -Models ein oder lade eines im Create-Hub.";

    partial void OnSelectedModelChanged(VideoModelChoice? value)
    {
        // Ablauf: wie zuletzt für dieses Modell gewählt, sonst aus dem Dateinamen (unbekannt = distilled).
        var dev = value is not null && _settings.Current.Video.DevByModel.TryGetValue(value.ModelPath, out var chosen)
            ? chosen
            : value?.Variant == VideoModelVariant.Dev;
        SelectedProcess = Processes.First(p => p.IsDev == dev);
        CheckSetup();
        UpdateFit();
    }

    /// <summary>Aus dem Create-Hub: ins Studio wechseln und dieses Modell wählen (Video- oder Bildmodell).</summary>
    public async Task SelectModelAsync(string modelPath)
    {
        await RefreshImageModelsAsync();
        if (ImageModels.FirstOrDefault(m => SamePath(m.ModelPath, modelPath)) is { } image)
        {
            Mode = StudioMode.Image;
            SelectedImageChoice = image;
            return;
        }

        await RefreshModelsAsync();
        if (VideoModels.FirstOrDefault(m => SamePath(m.ModelPath, modelPath)) is { } choice)
        {
            Mode = StudioMode.Video;
            SelectedModel = choice;
        }
    }

    private async Task RefreshModelsAsync()
    {
        var directory = VideoModelsDirectory;
        IReadOnlyList<VideoModelChoice> found;
        try
        {
            found = await Task.Run(() => VideoModelCatalog.Find(directory, _models.Scan()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Videomodelle konnten nicht gesucht werden.");
            found = [];
        }

        var keep = SelectedModel?.ModelPath ?? _settings.Current.Video.ModelPath;
        VideoModels.Clear();
        foreach (var choice in found)
        {
            VideoModels.Add(choice);
        }

        SelectedModel = VideoModels.FirstOrDefault(m => keep is not null && SamePath(m.ModelPath, keep))
            ?? VideoModels.FirstOrDefault(m => m.IsUsable)
            ?? VideoModels.FirstOrDefault();
        CheckSetup();
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Merkt sich Modell und Ablauf für den nächsten Start der App.</summary>
    private async Task RememberModelAsync()
    {
        var path = SelectedModel?.ModelPath;
        var dev = IsDevProcess;
        var current = _settings.Current.Video;
        if (path == current.ModelPath && (path is null || (current.DevByModel.TryGetValue(path, out var known) && known == dev)))
        {
            return;
        }

        var settings = JsonDefaults.Clone(_settings.Current);
        settings.Video.ModelPath = path;
        if (path is not null)
        {
            settings.Video.DevByModel[path] = dev;
        }

        await _settings.SaveAsync(settings, CancellationToken.None);
    }

    // ----------------------------------------------------------- Einrichtung

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSetupProblem))]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(RepeatSameSeedCommand), nameof(RepeatNewSeedCommand), nameof(EnqueueCommand))]
    private string _setupProblem = "";

    public bool HasSetupProblem => SetupProblem.Length > 0;

    [ObservableProperty] private string _backendStatus;

    [RelayCommand]
    private void CheckSetup() => SetupProblem = IsImageMode
        ? SelectedImageChoice is { IsUsable: false } unusableImage
            ? unusableImage.Note + " Wähle oben ein anderes Bildmodell."
            : _backend.SetupProblem(SelectedImageModel) ?? ""
        : SelectedModel is { IsUsable: false } unusable
            ? unusable.Note + " Wähle oben ein anderes Videomodell."
            : _backend.SetupProblem(Models) ?? "";

    /// <summary>
    /// Startet scripts\setup-video.ps1 in einem eigenen Fenster: Es richtet ComfyUI ein und lädt fehlende Dateien mit Prüfsumme.
    /// Fehlt nur der Rest (Text-Encoder, VAEs), lädt es nur diesen (-Companions).
    /// </summary>
    [RelayCommand]
    private void RunSetup()
    {
        var mainModelMissing = SelectedModel is null or { IsBuiltIn: true } && !File.Exists(Models.Files(VideoModelsDirectory)[0]);
        var arguments = IsImageMode ? "-Images" : mainModelMissing ? "-Models" : "-Companions";
        var size = IsImageMode ? "ca. 12 GB (Bildmodell, Text-Encoder, VAE)"
            : mainModelMissing ? "ca. 24 GB (Videomodell, Text-Encoder, VAEs)" : "ca. 11 GB (Text-Encoder, Projektion, VAEs)";
        if (!_dialogs.Confirm(IsImageMode ? "Bilder einrichten" : "Video einrichten",
                $"Das startet scripts\\setup-video.ps1 {arguments} in einem eigenen Fenster. Es richtet ComfyUI ein (falls nötig) und lädt die fehlenden Dateien "
                + $"von Hugging Face, höchstens {size}, mit Prüfsumme. Ist es fertig, klick hier auf „Nochmals prüfen“.", "Skript starten"))
        {
            return;
        }

        try
        {
            _shell.RunScriptInWindow(Path.Combine(_paths.Root, "scripts", "setup-video.ps1"), arguments);
        }
        catch (Exception ex) when (ex is FileNotFoundException or System.ComponentModel.Win32Exception)
        {
            ShowStatus("Das Skript liess sich nicht starten: " + ex.Message, isError: true);
        }
    }

    /// <summary>ComfyUI beenden, z.B. vor dem Gamen. Gibt RAM und VRAM frei.</summary>
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task StopBackendAsync()
    {
        _idleStop?.Cancel();
        await _backend.StopAsync();
    }

    private static string Describe(IVideoBackend backend) => backend.State switch
    {
        VideoBackendState.Stopped => "ComfyUI: aus (startet beim ersten Auftrag, ca. 15 s)",
        VideoBackendState.Ready => "ComfyUI: läuft und wartet",
        _ => "ComfyUI: " + backend.StatusText,
    };

    // ----------------------------------------------------------- Auftrag

    public IReadOnlyList<VideoFormat> Formats { get; } =
    [
        new(768, 512, "Quer 768×512"),
        new(512, 768, "Hochkant 512×768"),
        new(512, 320, "Klein 512×320 (schnell, zum Testen)"),
    ];

    public IReadOnlyList<VideoLength> Lengths { get; } =
    [
        new(2, "2 Sekunden"),
        new(3, "3 Sekunden"),
        new(4, "4 Sekunden"),
        new(5, "5 Sekunden"),
        new(6, "6 Sekunden"),
        new(8, "8 Sekunden"),
        new(10, "10 Sekunden"),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(EnqueueCommand), nameof(ImprovePromptCommand))]
    private string _prompt = "";

    [ObservableProperty] private VideoFormat _selectedFormat;

    [ObservableProperty] private VideoLength _selectedLength;

    [ObservableProperty] private bool _withAudio = true;

    [ObservableProperty] private string _seedText = "42";

    /// <summary>Aus: Jedes Video bekommt einen neuen Zufalls-Seed. Ein: Der Seed im Feld bleibt (gleicher Prompt = gleiches Video).</summary>
    [ObservableProperty] private bool _keepSeed;

    [ObservableProperty] private VideoFit? _fit;

    partial void OnSelectedFormatChanged(VideoFormat value) => UpdateFit();

    partial void OnSelectedLengthChanged(VideoLength value) => UpdateFit();

    [RelayCommand]
    private void RandomSeed()
    {
        SeedText = NewSeed().ToString(CultureInfo.InvariantCulture);
        KeepSeed = true;
    }

    private static long NewSeed() => Random.Shared.NextInt64(1, 1L << 40);

    private VideoRequest BuildRequest(long seed) => new(
        Prompt.Trim(),
        SelectedFormat.Width,
        SelectedFormat.Height,
        VideoRequest.FramesFor(SelectedLength.Seconds, FrameRate),
        FrameRate,
        seed,
        WithAudio,
        IsDevProcess && NegativePrompt.Trim().Length > 0 ? NegativePrompt.Trim() : null,
        StartImagePath);

    private void UpdateFit()
    {
        if (IsImageMode)
        {
            // Die Ampel rechnet mit dem Videomodell. Bilder brauchen viel weniger Speicher (docs/11-Bild.md).
            Fit = null;
            return;
        }

        var snapshot = _resources.Read();
        Fit = VideoFitEstimator.Estimate(BuildRequest(0), snapshot.RamTotal, snapshot.RamUsed, _engines.LoadedKi is not null, PowerStatus.IsOnBattery());
    }

    private async Task RunFitLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(FitInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (!IsGenerating)
                {
                    UpdateFit();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // App wird geschlossen.
        }
    }

    // ----------------------------------------------------------- Bild zu Video

    /// <summary>Bild zu Video: das erste Bild des Videos. Null = Text zu Video.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStartImage))]
    private string? _startImagePath;

    public bool HasStartImage => StartImagePath is not null;

    /// <summary>Nur eigene Bilder oder mit Einverständnis der abgebildeten Person (Bauplan Video, Kapitel 9).</summary>
    public string StartImageHint =>
        "Das Bild wird auf das Format zugeschnitten (Mitte). Nimm eigene Bilder. Bilder von anderen Personen nur mit deren Einverständnis.";

    [RelayCommand]
    private void PickStartImage()
    {
        if (_dialogs.PickImages("Startbild für das Video wählen").FirstOrDefault() is { } image)
        {
            StartImagePath = image;
        }
    }

    [RelayCommand]
    private void ClearStartImage() => StartImagePath = null;

    // ----------------------------------------------------------- Prompt verbessern

    /// <summary>Das Hauptfenster: Welche KI ist in der Kopfleiste gewählt? Setzt das MainViewModel.</summary>
    public IChatShell? Shell { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImprovePromptCommand))]
    private bool _isImproving;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoPromptCommand))]
    private bool _canUndoPrompt;

    private bool CanImprove => !IsImproving && !IsGenerating && Prompt.Trim().Length > 0;

    /// <summary>
    /// Die KI aus der Kopfleiste schreibt aus der Idee einen ausführlichen englischen Prompt. Hat erst das Verbessern
    /// das Chat-Modell geladen, wird es danach wieder entladen: Video und Bild brauchen den Grafikspeicher.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanImprove))]
    private async Task ImprovePromptAsync()
    {
        if (Shell?.SelectedKi is not { } ki)
        {
            ShowStatus("Wähle oben in der Kopfleiste eine KI. Sie verbessert dann den Prompt.", isError: true);
            return;
        }

        var loadedBefore = _engines.LoadedKi is not null;
        IsImproving = true;
        ShowStatus($"{ki.DisplayName} verbessert den Prompt …");
        var answer = new System.Text.StringBuilder();
        try
        {
            var messages = IsImageMode
                ? VideoPromptWriter.ImageMessages(Prompt, SelectedImageFormat.Width, SelectedImageFormat.Height)
                : VideoPromptWriter.Messages(Prompt, SelectedLength.Seconds, SelectedFormat.Width, SelectedFormat.Height, HasStartImage);
            await foreach (var chunk in _engines.StreamAsync(new ChatRequest(ki, messages, Think: false, MaxTokens: 400), null, CancellationToken.None))
            {
                answer.Append(chunk.Text);
            }

            var improved = VideoPromptWriter.Clean(answer.ToString());
            if (improved.Length < 20)
            {
                ShowStatus($"{ki.DisplayName} hat keinen brauchbaren Prompt geliefert. Versuch es nochmals oder mit einer anderen KI.", isError: true);
                return;
            }

            _promptBeforeImprove = Prompt;
            Prompt = improved;
            CanUndoPrompt = true;
            ShowStatus($"{ki.DisplayName} hat den Prompt verbessert. Gefällt er dir nicht: „Rückgängig“.");
        }
        catch (Exception ex) when (ex is InferenceException or HttpRequestException or IOException)
        {
            _logger.LogWarning(ex, "Prompt verbessern fehlgeschlagen.");
            ShowStatus("Prompt verbessern hat nicht geklappt: " + ex.Message, isError: true);
        }
        finally
        {
            IsImproving = false;
            if (!loadedBefore && _engines.LoadedKi is not null)
            {
                await _engines.UnloadAsync(CancellationToken.None);
            }

            UpdateFit();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndoPrompt))]
    private void UndoPrompt()
    {
        if (_promptBeforeImprove is { } before)
        {
            Prompt = before;
        }

        _promptBeforeImprove = null;
        CanUndoPrompt = false;
    }

    // ----------------------------------------------------------- Erzeugen

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GenerateCommand), nameof(CancelCommand), nameof(StopBackendCommand), nameof(ImprovePromptCommand))]
    [NotifyCanExecuteChangedFor(nameof(RepeatSameSeedCommand), nameof(RepeatNewSeedCommand), nameof(DeleteVideoCommand))]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isGenerating;

    public bool IsIdle => !IsGenerating;

    [ObservableProperty] private string _stageText = "Noch kein Auftrag in Arbeit.";

    [ObservableProperty] private double _progressValue;

    [ObservableProperty] private string _progressText = "";

    private bool CanGenerate => !IsGenerating && !_queueRunning && CanCreateJob;

    private bool CanCreateJob => !HasSetupProblem && Prompt.Trim().Length > 0
        && (IsImageMode ? SelectedImageChoice is not { IsUsable: false } : SelectedModel is not { IsUsable: false });

    /// <summary>Sofort erzeugen: Der Auftrag kommt an den Anfang der Warteschlange.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        if (CreateJob() is not { } job)
        {
            return;
        }

        Queue.Insert(0, job);
        OnQueueChanged();
        await RunQueueAsync();
    }

    /// <summary>Für später, z.B. über Nacht: Läuft schon ein Auftrag, kommt dieser danach dran.</summary>
    [RelayCommand(CanExecute = nameof(CanCreateJob))]
    private async Task EnqueueAsync()
    {
        if (CreateJob() is not { } job)
        {
            return;
        }

        Queue.Add(job);
        OnQueueChanged();
        if (_queueRunning)
        {
            ShowStatus(Queue.Count == 1 ? "In der Warteschlange: 1 Auftrag wartet." : $"In der Warteschlange: {Queue.Count} Aufträge warten.");
            return;
        }

        await RunQueueAsync();
    }

    /// <summary>Baut den Auftrag aus dem Formular. Ohne "Seed behalten" bekommt jeder Auftrag einen neuen Seed.</summary>
    private StudioJob? CreateJob()
    {
        if (!KeepSeed || !long.TryParse(SeedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed) || seed < 0)
        {
            seed = NewSeed();
            SeedText = seed.ToString(CultureInfo.InvariantCulture);
        }

        if (IsImageMode)
        {
            var references = ReferencesSupported && ReferenceImages.Count > 0 ? ReferenceImages.ToList() : null;
            var image = new ImageRequest(Prompt.Trim(), SelectedImageFormat.Width, SelectedImageFormat.Height, ImageCount, seed, references,
                SourceImagePath, Math.Round(Strength, 2));
            if (image.Problem() is { } imageProblem)
            {
                ShowStatus(imageProblem, isError: true);
                return null;
            }

            return new ImageJob(image, SelectedImageModel);
        }

        var request = BuildRequest(seed);
        if (request.Problem() is { } problem)
        {
            ShowStatus(problem, isError: true);
            return null;
        }

        _ = RememberModelAsync();
        return new VideoJob(request, Models);
    }

    /// <summary>Arbeitet die Warteschlange ab, ein Auftrag nach dem anderen. Abbrechen stoppt nur den laufenden.</summary>
    private async Task RunQueueAsync()
    {
        if (_queueRunning || Queue.Count == 0)
        {
            return;
        }

        if (_training.IsRunning)
        {
            ShowStatus("Gerade läuft ein Training. Es braucht die Grafikkarte. Starte den Auftrag, wenn das Training fertig ist.", isError: true);
            return;
        }

        // Der Grafikspeicher reicht nicht für Chat und Video (oder Bild) gleichzeitig.
        if (_engines.LoadedKi is { } loaded)
        {
            if (!_dialogs.Confirm("Chat-Modell entladen", $"{loaded.DisplayName} ist geladen und belegt Grafikspeicher. Für den Auftrag wird es entladen.",
                    "Entladen und starten"))
            {
                return;
            }

            await _engines.UnloadAsync(CancellationToken.None);
        }

        _queueRunning = true;
        GenerateCommand.NotifyCanExecuteChanged();
        try
        {
            while (Queue.Count > 0)
            {
                var job = Queue[0];
                Queue.RemoveAt(0);
                OnQueueChanged();
                await RunAsync(job);
            }
        }
        finally
        {
            _queueRunning = false;
            GenerateCommand.NotifyCanExecuteChanged();
        }
    }

    public ObservableCollection<StudioJob> Queue { get; } = [];

    public bool HasQueue => Queue.Count > 0;

    public string QueueText => Queue.Count == 1 ? "1 Auftrag wartet:" : $"{Queue.Count} Aufträge warten:";

    private void OnQueueChanged()
    {
        OnPropertyChanged(nameof(HasQueue));
        OnPropertyChanged(nameof(QueueText));
    }

    [RelayCommand]
    private void RemoveQueued(StudioJob job)
    {
        Queue.Remove(job);
        OnQueueChanged();
    }

    [RelayCommand]
    private void ClearQueue()
    {
        Queue.Clear();
        OnQueueChanged();
    }

    [RelayCommand(CanExecute = nameof(IsGenerating))]
    private void Cancel()
    {
        StageText = "Wird abgebrochen …";
        _job?.Cancel();
    }

    private async Task RunAsync(StudioJob studioJob)
    {
        _idleStop?.Cancel();
        var what = studioJob is ImageJob ? "Gerade entstehen Bilder" : "Gerade entsteht ein Video";
        using var block = _engines.Block($"{what}. Der Grafikspeicher ist voll. Chatten geht wieder, wenn es fertig ist.");
        using var job = new CancellationTokenSource();
        _job = job;
        IsGenerating = true;
        ShowStatus("");
        _stepTimes.Clear();
        _clock.Restart();
        ProgressValue = 0;
        ProgressText = "";
        StageText = "Startet …";
        _current = studioJob;
        _ = RunClockAsync(job.Token);
        _logger.LogInformation("Auftrag gestartet: {Job}", studioJob.Label);
        try
        {
            var progresses = studioJob switch
            {
                ImageJob image => _backend.GenerateImageAsync(image.Request, image.Models, job.Token),
                VideoJob video => _backend.GenerateAsync(video.Request, video.Models, job.Token),
                _ => throw new InvalidOperationException("Unbekannter Auftrag."),
            };
            await foreach (var progress in progresses)
            {
                await OnProgressAsync(progress, studioJob);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Auftrag fehlgeschlagen.");
            StageText = "Fehler.";
            ShowStatus("Der Auftrag ist fehlgeschlagen: " + ex.Message, isError: true);
        }
        finally
        {
            _clock.Stop();
            _job = null;
            _current = null;
            IsGenerating = false;
            await FreeMemoryAsync();
            ScheduleIdleStop();
            UpdateFit();
        }
    }

    private async Task OnProgressAsync(VideoProgress progress, StudioJob job)
    {
        switch (progress)
        {
            case VideoStage stage:
                StageText = stage.Text;
                ProgressValue = Math.Max(ProgressValue, StageProgress(stage.Phase));
                break;
            case VideoStep step:
                // Eigene Uhr: Die Zeit vom Backend zählt erst ab dem Auftrag, nicht ab dem Start von ComfyUI.
                _stepTimes.Add(_clock.Elapsed.TotalSeconds);
                ProgressValue = 40 + 50.0 * step.Step / Math.Max(1, step.TotalSteps);
                StageText = string.Create(CultureInfo.InvariantCulture, $"Entrauschen: Schritt {step.Step} von {step.TotalSteps}");
                break;
            case VideoDone done when job is VideoJob videoJob:
                ProgressValue = 100;
                StageText = "Fertig in " + Duration(_clock.Elapsed.TotalSeconds) + ".";
                ProgressText = "";
                // In der Galerie steht dieselbe Dauer wie hier: alles, was du gewartet hast (auch der Start von ComfyUI).
                await AddToGalleryAsync(done with { Seconds = Math.Round(_clock.Elapsed.TotalSeconds, 1) }, videoJob);
                break;
            case ImageDone done when job is ImageJob imageJob:
                ProgressValue = 100;
                StageText = "Fertig in " + Duration(_clock.Elapsed.TotalSeconds) + ".";
                ProgressText = "";
                await AddImagesToGalleryAsync(done with { Seconds = Math.Round(_clock.Elapsed.TotalSeconds, 1) }, imageJob);
                break;
            case VideoFailed failed:
                StageText = "Fehler.";
                ProgressText = "";
                ShowStatus(failed.Message, isError: true);
                break;
            case VideoCancelled:
                StageText = "Abgebrochen. Es wurde nichts gespeichert.";
                ProgressText = "";
                ProgressValue = 0;
                break;
        }
    }

    /// <summary>Grober Fortschritt pro Abschnitt. Das Entrauschen (40–90 %) zählt die Schritte.</summary>
    private static double StageProgress(VideoPhase phase) => phase switch
    {
        VideoPhase.Starting => 2,
        VideoPhase.LoadingTextEncoder => 8,
        VideoPhase.ReadingPrompt => 18,
        VideoPhase.LoadingModel or VideoPhase.Denoising => 30,
        VideoPhase.DecodingAudio => 92,
        VideoPhase.DecodingVideo => 94,
        _ => 98,
    };

    /// <summary>Jede Sekunde: verstrichene Zeit und Schätzung der Restzeit.</summary>
    private async Task RunClockAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(ct) && IsGenerating)
            {
                ProgressText = "Läuft seit " + Duration(_clock.Elapsed.TotalSeconds) + RemainingText();
            }
        }
        catch (OperationCanceledException)
        {
            // Video fertig oder abgebrochen.
        }
    }

    /// <summary>Ab dem zweiten Schritt: Dauer pro Schritt mal fehlende Schritte, plus ca. 10 s fürs Dekodieren.</summary>
    private string RemainingText()
    {
        if (_stepTimes.Count < 2)
        {
            return _stepTimes.Count == 0 ? (_current is ImageJob ? " · lädt die Modelle" : " · lädt die Modelle (ca. 1 Minute)") : "";
        }

        var perStep = (_stepTimes[^1] - _stepTimes[0]) / (_stepTimes.Count - 1);
        var decode = _current is ImageJob ? 3 : 10;
        var left = perStep * ((_current?.Steps ?? Models.Steps) - _stepTimes.Count) + decode - (_clock.Elapsed.TotalSeconds - _stepTimes[^1]);
        return " · noch ca. " + Duration(Math.Max(5, left));
    }

    private async Task FreeMemoryAsync()
    {
        try
        {
            await _backend.FreeMemoryAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Speicher freigeben hat nicht geklappt.");
        }
    }

    /// <summary>ComfyUI nach der eingestellten Zeit ohne Auftrag beenden (gibt RAM und VRAM frei).</summary>
    private void ScheduleIdleStop()
    {
        _idleStop?.Cancel();
        _idleStop?.Dispose();
        _idleStop = null;
        var minutes = _settings.Current.Video.IdleStopMinutes;
        if (minutes <= 0)
        {
            return;
        }

        _idleStop = new CancellationTokenSource();
        _ = StopWhenIdleAsync(TimeSpan.FromMinutes(minutes), _idleStop.Token);
    }

    private async Task StopWhenIdleAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
            if (!IsGenerating)
            {
                _logger.LogInformation("Kein Auftrag seit {Minutes} Minuten: ComfyUI wird beendet.", delay.TotalMinutes);
                await _backend.StopAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Ein neues Video hat begonnen.
        }
    }

    private static string Duration(double seconds) =>
        seconds >= 60 ? $"{(int)(seconds / 60)} min {(int)(seconds % 60):00} s" : $"{Math.Max(0, (int)seconds)} s";

    // ----------------------------------------------------------- Galerie und Player

    public ObservableCollection<VideoEntry> Videos { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(SelectedInfo), nameof(HasSelectedImage), nameof(HasSelectedVideo))]
    [NotifyCanExecuteChangedFor(nameof(ReusePromptCommand), nameof(RepeatSameSeedCommand), nameof(RepeatNewSeedCommand), nameof(DeleteVideoCommand))]
    [NotifyCanExecuteChangedFor(nameof(UseAsStartImageCommand), nameof(UseAsReferenceCommand), nameof(UseAsSourceCommand))]
    private VideoEntry? _selectedVideo;

    public bool HasSelection => SelectedVideo is not null;

    /// <summary>Ein Bild ist gewählt: Rechts steht das Bild statt des Players.</summary>
    public bool HasSelectedImage => SelectedVideo is { IsImage: true };

    public bool HasSelectedVideo => SelectedVideo is { IsImage: false };

    public bool HasNoVideos => Videos.Count == 0;

    [ObservableProperty] private GalleryFilter _filter;

    partial void OnFilterChanged(GalleryFilter value) => ApplyFilter();

    private bool Matches(VideoEntry entry) => Filter switch
    {
        GalleryFilter.Videos => !entry.IsImage,
        GalleryFilter.Images => entry.IsImage,
        _ => true,
    };

    /// <summary>Zeigt aus allen Einträgen die, die zum Filter passen. Die Auswahl bleibt, wenn sie noch sichtbar ist.</summary>
    private void ApplyFilter()
    {
        var keep = SelectedVideo;
        Videos.Clear();
        foreach (var entry in _allEntries.Where(Matches))
        {
            Videos.Add(entry);
        }

        SelectedVideo = keep is not null && Videos.Contains(keep) ? keep : Videos.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoVideos));
    }

    /// <summary>Neue Einträge vorne einfügen. Passt der Filter nicht, wird er auf "Alle" gestellt, damit man sie sieht.</summary>
    private void AddEntries(IReadOnlyList<VideoEntry> entries)
    {
        _allEntries.InsertRange(0, entries);
        if (!entries.All(Matches))
        {
            Filter = GalleryFilter.All;
        }

        for (var i = entries.Count - 1; i >= 0; i--)
        {
            Videos.Insert(0, entries[i]);
        }

        SelectedVideo = entries[0];
        OnPropertyChanged(nameof(HasNoVideos));
    }

    /// <summary>z.B. "768×512 · 3.9 s · mit Ton · Seed 42 · erzeugt in 2 min 20 s · 02.11.2026 15:30".</summary>
    public string SelectedInfo => SelectedVideo switch
    {
        null => "",
        { IsImage: true } v => string.Create(CultureInfo.InvariantCulture,
            $"Bild · {v.Width}×{v.Height}{(v.BatchCount > 1 ? $" · {v.BatchIndex + 1} von {v.BatchCount}" : "")}{(v.ReferenceImages.Count > 0 ? $" · {v.ReferenceImages.Count} Ref." : "")}{(v.SourceImage is null ? "" : $" · Vorlage {v.Strength:0.00}")} · Seed {v.Seed} · {v.Model} · erzeugt in {Duration(v.GenerationSeconds)} · {v.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}"),
        var v => string.Create(CultureInfo.InvariantCulture,
            $"{v.Width}×{v.Height} · {v.Seconds:0.0} s · {(v.WithAudio ? "mit Ton" : "ohne Ton")} · Seed {v.Seed} · erzeugt in {Duration(v.GenerationSeconds)} · {v.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm}"),
    };

    private bool CanRepeat => HasSelection && !IsGenerating;

    private bool CanDelete => HasSelection && !IsGenerating;

    private async Task LoadGalleryAsync()
    {
        try
        {
            var videos = await _library.LoadAllAsync(_lifetime.Token);
            var images = await _images.LoadAllAsync(_lifetime.Token);
            _allEntries.Clear();
            _allEntries.AddRange(videos.Concat(images).OrderByDescending(e => e.CreatedAt));
            ApplyFilter();
        }
        catch (OperationCanceledException)
        {
            // App wird geschlossen.
        }
    }

    private async Task AddToGalleryAsync(VideoDone done, VideoJob job)
    {
        try
        {
            var entry = await _library.AddAsync(done, job.Request, job.Models, CancellationToken.None);
            AddEntries([entry]);
            ShowStatus($"Das Video ist fertig und läuft rechts im Player. Datei: data\\videos\\{entry.Id}.mp4");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Video nicht in die Galerie übernommen.");
            ShowStatus($"Das Video ist fertig ({done.VideoPath}), konnte aber nicht in die Galerie: {ex.Message}", isError: true);
        }
    }

    private async Task AddImagesToGalleryAsync(ImageDone done, ImageJob job)
    {
        try
        {
            var entries = await _images.AddAsync(done, job.Request, job.Models, CancellationToken.None);
            AddEntries(entries);
            ShowStatus(entries.Count == 1
                ? $"Das Bild ist fertig. Datei: data\\images\\{entries[0].Id}.png"
                : $"{entries.Count} Bilder sind fertig, in der Galerie und in data\\images.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Bilder nicht in die Galerie übernommen.");
            ShowStatus($"Die Bilder sind fertig ({string.Join(", ", done.ImagePaths)}), konnten aber nicht in die Galerie: {ex.Message}", isError: true);
        }
    }

    /// <summary>Prompt, Format, Länge, Ton und Seed des gewählten Videos (oder Bilds) ins Formular übernehmen.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ReusePrompt()
    {
        if (SelectedVideo is not { } video)
        {
            return;
        }

        if (video.IsImage)
        {
            Mode = StudioMode.Image;
            Prompt = video.Prompt;
            SelectedImageFormat = ImageFormats.FirstOrDefault(f => f.Width == video.Width && f.Height == video.Height) ?? SelectedImageFormat;
            ImageCount = Math.Clamp(video.BatchCount, 1, ImageRequest.MaxCount);
            SelectedImageChoice = ImageModels.FirstOrDefault(m => m.IsUsable && m.Name == video.Model) ?? SelectedImageChoice;
            SourceImagePath = video.SourceImagePath;
            Strength = video.SourceImagePath is null ? Strength : video.Strength;
            ReferenceImages.Clear();
            foreach (var reference in video.ReferenceImagePaths)
            {
                ReferenceImages.Add(reference);
            }

            OnReferencesChanged();
            SeedText = video.Seed.ToString(CultureInfo.InvariantCulture);
            return;
        }

        Mode = StudioMode.Video;
        Prompt = video.Prompt;
        SelectedFormat = Formats.FirstOrDefault(f => f.Width == video.Width && f.Height == video.Height) ?? SelectedFormat;
        SelectedLength = Lengths.MinBy(l => Math.Abs(l.Seconds - video.Seconds)) ?? SelectedLength;
        WithAudio = video.WithAudio;
        SeedText = video.Seed.ToString(CultureInfo.InvariantCulture);
        StartImagePath = video.StartImagePath;
    }

    [RelayCommand(CanExecute = nameof(CanRepeat))]
    private async Task RepeatSameSeedAsync()
    {
        ReusePrompt();
        KeepSeed = true;
        await GenerateIfPossibleAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRepeat))]
    private async Task RepeatNewSeedAsync()
    {
        ReusePrompt();
        KeepSeed = false;
        await GenerateIfPossibleAsync();
    }

    /// <summary>"Nochmal": Das Formular ist ausgefüllt. Fehlt etwas (z.B. die Einrichtung), steht unten der Grund.</summary>
    private async Task GenerateIfPossibleAsync()
    {
        if (!CanGenerate)
        {
            ShowStatus(HasSetupProblem ? SetupProblem : "Das geht gerade nicht. Prüf links den Auftrag.", isError: true);
            return;
        }

        await GenerateAsync();
    }

    /// <summary>
    /// Aus einem Bild wird ein Video (Bauplan Bild, B2): Das Bild wird Startbild, das Studio wechselt auf Video.
    /// Ein Hochkant-Bild gibt ein Hochkant-Video, sonst bleibt das Format.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelectedImage))]
    private void UseAsStartImage()
    {
        if (SelectedVideo is not { IsImage: true } image)
        {
            return;
        }

        Mode = StudioMode.Video;
        StartImagePath = image.VideoPath;
        if (image.Height > image.Width)
        {
            SelectedFormat = Formats.First(f => f.Height > f.Width);
        }
        else if (SelectedFormat.Height > SelectedFormat.Width)
        {
            SelectedFormat = Formats[0];
        }

        ShowStatus("Das Bild ist jetzt das Startbild. Beschreib links, wie es sich bewegen soll, und klick auf „Video erzeugen“.");
    }

    private bool CanUseAsReference => HasSelectedImage && ReferenceImages.Count < ImageRequest.MaxReferences;

    /// <summary>Das gewählte Bild wird Referenz für das nächste Bild. Das Studio wechselt auf Bild und, falls nötig, auf FLUX.2 klein.</summary>
    [RelayCommand(CanExecute = nameof(CanUseAsReference))]
    private void UseAsReference()
    {
        if (SelectedVideo is not { IsImage: true } image)
        {
            return;
        }

        Mode = StudioMode.Image;
        if (!SelectedImageModel.SupportsReferences && ImageModels.FirstOrDefault(m => m.Set?.SupportsReferences == true) is { } flux)
        {
            SelectedImageChoice = flux;
        }

        AddReferencePath(image.VideoPath);
        ShowStatus($"Das Bild ist jetzt Referenz {ReferenceImages.Count}. Beschreib links, was damit passieren soll, und klick auf „Bilder erzeugen“.");
    }

    [RelayCommand]
    private void OpenFolder() => _shell.OpenFolder(SelectedVideo is { IsImage: true } ? _images.Directory : _library.Directory);

    /// <summary>In den Papierkorb, nicht endgültig gelöscht. Der Player gibt die Datei vorher frei.</summary>
    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void DeleteVideo()
    {
        var what = SelectedVideo is { IsImage: true } ? "Bild" : "Video";
        if (SelectedVideo is not { } video
            || !_dialogs.Confirm($"{what} löschen", $"Das {what} \"{Shorten(video.Prompt)}\" in den Papierkorb verschieben?", "In den Papierkorb"))
        {
            return;
        }

        var index = Videos.IndexOf(video);
        SelectedVideo = null;
        if (!_shell.MoveToRecycleBin(video.IsImage ? _images.FilesOf(video) : _library.FilesOf(video)))
        {
            SelectedVideo = video;
            ShowStatus($"Das {what} liess sich nicht löschen. Ist es noch in einem anderen Programm offen?", isError: true);
            return;
        }

        if (video.IsImage && SamePathOrNull(StartImagePath, video.VideoPath))
        {
            StartImagePath = null;
        }

        if (video.IsImage && SamePathOrNull(SourceImagePath, video.VideoPath))
        {
            SourceImagePath = null;
        }

        if (video.IsImage && ReferenceImages.FirstOrDefault(r => SamePath(r, video.VideoPath)) is { } reference)
        {
            ReferenceImages.Remove(reference);
            OnReferencesChanged();
        }

        _allEntries.Remove(video);
        Videos.Remove(video);
        SelectedVideo = Videos.Count == 0 ? null : Videos[Math.Min(index, Videos.Count - 1)];
        OnPropertyChanged(nameof(HasNoVideos));
        ShowStatus($"Das {what} liegt jetzt im Papierkorb.");
    }

    private static bool SamePathOrNull(string? a, string b) => a is not null && SamePath(a, b);

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..57] + "…";

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

    /// <summary>Beim Schliessen der App: laufenden Auftrag abbrechen und ComfyUI beenden.</summary>
    public async ValueTask DisposeAsync()
    {
        // Wird zweimal gerufen: vom Hauptfenster beim Schliessen und vom DI-Container beim Beenden.
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifetime.CancelAsync();
        _job?.Cancel();
        _idleStop?.Cancel();
        await _backend.StopAsync();
        _lifetime.Dispose();
    }
}
