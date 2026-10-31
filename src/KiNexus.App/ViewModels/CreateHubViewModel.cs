using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core.Inference;
using KiNexus.Core.IO;
using KiNexus.Core.Kis;
using KiNexus.Core.Models;
using KiNexus.Core.Paths;
using KiNexus.Core.Resources;
using KiNexus.Engines.HuggingFace;
using KiNexus.Engines.Ollama;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

public enum CreateHubSection
{
    Overview,
    NewKi,
    Import,
    HuggingFace,
}

/// <summary>
/// Der Create-Hub-Tab (Bauplan 8.3): Übersicht, neue KI, Importieren, Hugging Face.
/// Hält die gemeinsamen Dinge: Meldungs-Balken, Ladetest, Ampel-Berechnung.
/// </summary>
public sealed partial class CreateHubViewModel : TabViewModel
{
    private readonly IKiRegistry _kis;
    private readonly ModelLibrary _library;
    private readonly EngineManager _engines;
    private readonly IResourceMonitor _resources;
    private readonly AppPaths _paths;
    private readonly ILogger<CreateHubViewModel> _logger;
    private readonly SynchronizationContext _ui;
    private Func<Task>? _noticeAction;

    public CreateHubViewModel(
        IKiRegistry kis,
        ModelLibrary library,
        EngineManager engines,
        IResourceMonitor resources,
        AppPaths paths,
        IDialogService dialogs,
        IShellService shell,
        OllamaBackend ollama,
        OllamaModelStore ollamaStore,
        OllamaImporter ollamaImporter,
        ModelImporter importer,
        HuggingFaceClient huggingFace,
        ModelDownloader downloader,
        SecretStore tokenStore,
        ILoggerFactory loggers)
        : base("createHub", "Create-Hub", Glyphs.Library)
    {
        _kis = kis;
        _library = library;
        _engines = engines;
        _resources = resources;
        _paths = paths;
        _logger = loggers.CreateLogger<CreateHubViewModel>();
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        Overview = new CreateHubOverviewViewModel(this, kis, library, engines, dialogs, shell, ollama, loggers.CreateLogger<CreateHubOverviewViewModel>());
        Wizard = new NewKiWizardViewModel(this, kis, ollama);
        Import = new ImportViewModel(this, importer, ollamaStore, ollamaImporter, library, dialogs, loggers.CreateLogger<ImportViewModel>());
        HuggingFace = new HuggingFaceViewModel(this, huggingFace, downloader, tokenStore, shell, dialogs, loggers.CreateLogger<HuggingFaceViewModel>());
    }

    public CreateHubOverviewViewModel Overview { get; }
    public NewKiWizardViewModel Wizard { get; }
    public ImportViewModel Import { get; }
    public HuggingFaceViewModel HuggingFace { get; }

    /// <summary>Das Hauptfenster: zum Chat wechseln, KI auswählen. Setzt das MainViewModel.</summary>
    public IChatShell? Shell { get; set; }

    /// <summary>Der gewählte Bereich. Die Segment-Knöpfe oben sind daran gebunden.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    private CreateHubSection _section;

    public object CurrentPage => Section switch
    {
        CreateHubSection.NewKi => Wizard,
        CreateHubSection.Import => Import,
        CreateHubSection.HuggingFace => HuggingFace,
        _ => Overview,
    };

    /// <summary>Beim Wechsel per Knopf die Listen des Bereichs neu lesen.</summary>
    partial void OnSectionChanged(CreateHubSection value)
    {
        if (!_switchingInCode)
        {
            _ = RefreshPageAsync();
        }
    }

    private bool _switchingInCode;

    private void SwitchTo(CreateHubSection section)
    {
        _switchingInCode = true;
        try
        {
            Section = section;
        }
        finally
        {
            _switchingInCode = false;
        }
    }

    // ----------------------------------------------------------- Meldungs-Balken

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    private string _notice = "";

    [ObservableProperty] private bool _noticeIsError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoticeAction))]
    private string _noticeActionText = "";

    public bool HasNotice => Notice.Length > 0;

    public bool HasNoticeAction => NoticeActionText.Length > 0;

    /// <summary>Zeigt eine Meldung oben im Create-Hub, optional mit einem Knopf (z.B. "Neue KI daraus").</summary>
    public void ShowNotice(string text, bool isError, string? actionText = null, Func<Task>? action = null)
    {
        Notice = text;
        NoticeIsError = isError;
        NoticeActionText = action is null ? "" : actionText ?? "";
        _noticeAction = action;
    }

    [RelayCommand]
    private void DismissNotice() => ShowNotice("", false);

    [RelayCommand]
    private async Task RunNoticeActionAsync()
    {
        var action = _noticeAction;
        DismissNotice();
        if (action is not null)
        {
            await action();
        }
    }

    // ----------------------------------------------------------- Beschäftigt-Anzeige (Ladetest)

    [ObservableProperty] private bool _isBusy;

    [ObservableProperty] private string _busyText = "";

    // ----------------------------------------------------------- Navigation

    [RelayCommand]
    private void ShowSection(CreateHubSection section) => Section = section;

    /// <summary>Öffnet den Assistenten, wenn gewünscht mit einem schon gewählten Modell.</summary>
    public async Task StartWizardAsync(LocalModel? model = null, string? ollamaName = null)
    {
        SwitchTo(CreateHubSection.NewKi);
        await Wizard.PrepareAsync(model, ollamaName);
    }

    /// <summary>Dateien wurden irgendwo in den Create-Hub gezogen (Drag &amp; Drop): zum Import wechseln.</summary>
    public async Task ImportDroppedFilesAsync(IReadOnlyList<string> files)
    {
        SwitchTo(CreateHubSection.Import);
        await Import.RefreshAsync();
        await Import.ImportFilesAsync(files);
    }

    public void SearchHuggingFace(string query)
    {
        SwitchTo(CreateHubSection.HuggingFace);
        HuggingFace.Query = query;
        HuggingFace.SearchCommand.Execute(null);
    }

    /// <summary>Beim Öffnen des Tabs (und nach Änderungen) die Listen neu lesen.</summary>
    public Task RefreshPageAsync() => CurrentPage switch
    {
        NewKiWizardViewModel wizard => wizard.PrepareAsync(null, null, keepSelection: true),
        ImportViewModel import => import.RefreshAsync(),
        HuggingFaceViewModel huggingFace => huggingFace.RefreshAsync(),
        _ => Overview.RefreshAsync(),
    };

    /// <summary>Für Meldungen aus Hintergrund-Threads (z.B. Download fertig).</summary>
    public void Post(Action action) => _ui.Post(_ => action(), null);

    // ----------------------------------------------------------- Gemeinsame Werkzeuge

    public IReadOnlyList<LocalModel> LibraryScan() => _library.Scan();

    /// <summary>Die Ampel für eine Modellgrösse, gemessen an diesem Laptop.</summary>
    public FitEstimate Estimate(long modelBytes, long extraBytes, int contextLength)
    {
        var snapshot = _resources.Read();
        return FitEstimator.Estimate(modelBytes, extraBytes, contextLength, snapshot.VramTotal, snapshot.RamTotal);
    }

    public FitEstimate Estimate(LocalModel model, int contextLength = 8192)
    {
        var projector = model.MmprojPath is { } path && File.Exists(path) ? new FileInfo(path).Length : 0;
        var total = model.Manifest.SizeBytes > 0 ? model.Manifest.SizeBytes : SafeLength(model.ModelPath) + projector;
        return Estimate(total - projector, projector, contextLength);
    }

    /// <summary>Pfad, wie er in kis/*.json steht: relativ zum Projektordner, wenn möglich.</summary>
    public string ToKiPath(string fullPath)
    {
        var relative = Path.GetRelativePath(_paths.Root, fullPath);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? fullPath : relative.Replace('\\', '/');
    }

    /// <summary>Welche KIs nutzen dieses Grundmodell?</summary>
    public IReadOnlyList<KiProfile> KisUsing(LocalModel model) =>
        _kis.All.Where(ki => ki.Engine == EngineKind.LlamaCpp && ki.BaseModel.Path is { Length: > 0 } path
                && string.Equals(Path.GetFullPath(_paths.Resolve(path)), Path.GetFullPath(model.ModelPath), StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>
    /// Ladetest (Bauplan 8.3): Kann llama.cpp das Modell laden? Lädt es mit kleinem Kontext und entlädt es wieder.
    /// </summary>
    public async Task<bool> TestLoadAsync(LocalModel model)
    {
        if (_engines.State == EngineManagerState.Answering)
        {
            ShowNotice("Gerade antwortet eine KI. Warte kurz: Der Ladetest entlädt das aktuelle Modell.", isError: true);
            return false;
        }

        var name = model.Manifest.Name.Length > 0 ? model.Manifest.Name : model.FolderName;
        var probe = new KiProfile
        {
            Id = "ladetest",
            DisplayName = "Ladetest " + name,
            Engine = EngineKind.LlamaCpp,
            BaseModel = { Path = ToKiPath(model.ModelPath), MmprojPath = model.MmprojPath is { } mmproj ? ToKiPath(mmproj) : null },
            Parameters = { ContextLength = 2048 },
            EngineOptions = { KeepAliveMinutes = 0 },
        };

        IsBusy = true;
        BusyText = $"Ladetest: {name} wird geladen …";
        try
        {
            var progress = new Progress<string>(message => BusyText = "Ladetest: " + message);
            await _engines.TestLoadAsync(probe, progress, CancellationToken.None);
            ShowNotice($"Ladetest bestanden: llama.cpp kann {name} laden.", isError: false,
                "Neue KI daraus", () => StartWizardAsync(model));
            return true;
        }
        catch (Exception ex) when (ex is InferenceException or IOException or HttpRequestException or OperationCanceledException)
        {
            _logger.LogWarning(ex, "Ladetest für {Model} fehlgeschlagen.", model.FolderName);
            ShowNotice($"Ladetest fehlgeschlagen: llama.cpp kann {name} nicht laden. {ex.Message} "
                + "Tipp: Lade dasselbe Modell als GGUF von Hugging Face, diese Dateien sind für llama.cpp gemacht.", isError: true,
                "Auf Hugging Face suchen", () => { SearchHuggingFace(SearchTermFor(name)); return Task.CompletedTask; });
            return false;
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    /// <summary>"gemma4:26b" → "gemma4 26b". Gute Suchbegriffe für Hugging Face.</summary>
    public static string SearchTermFor(string modelName) =>
        modelName.Replace(":latest", "", StringComparison.OrdinalIgnoreCase).Replace(':', ' ').Replace('-', ' ').Trim();

    private static long SafeLength(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
}
