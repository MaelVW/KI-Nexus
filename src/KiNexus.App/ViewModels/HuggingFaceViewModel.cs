using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core.IO;
using KiNexus.Core.Models;
using KiNexus.Core.Video;
using KiNexus.Engines.HuggingFace;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>Ein Treffer der Suche, aufbereitet für die Liste.</summary>
public sealed record HfResultRow(HfModel Model)
{
    public string Name => Model.Name;
    public string Author => Model.Author;
    public string DownloadsText => DisplayFormat.Count(Model.Downloads);
    public string LikesText => DisplayFormat.Count(Model.Likes);
    public string DateText => DisplayFormat.Date(Model.LastModified);
    public string LicenseText => string.IsNullOrWhiteSpace(Model.License) ? "Lizenz unbekannt" : Model.License!;
    public bool IsGated => Model.Gated;

    /// <summary>
    /// Embedding-, Sprach-, Bild- oder Video-Modelle taugen nicht zum Chatten. Die Suche findet sie trotzdem.
    /// Geprüft werden die Art (pipeline_tag), die Schlagwörter und die Architektur im Kopf der GGUF-Datei.
    /// </summary>
    public bool IsNotChat => NotChatReason is not null;

    /// <summary>z.B. "ein Videomodell, Architektur ltxv" oder "feature-extraction". Null = Chat-Modell.</summary>
    public string? NotChatReason
    {
        get
        {
            if (ModelKinds.NonTextKind(Model.GgufArchitecture) is { } kind)
            {
                return $"{kind}, Architektur {Model.GgufArchitecture}";
            }

            if (Model.PipelineTag is { Length: > 0 } tag && tag is not ("text-generation" or "image-text-to-text" or "conversational" or "any-to-any"))
            {
                return tag;
            }

            return ModelKinds.NonTextTag(Model.Tags);
        }
    }

    /// <summary>
    /// Ein LTX-Videomodell: nicht zum Chatten, aber vielleicht für den Video-Tab (geprüft wird nach dem Download im GGUF-Kopf).
    /// Erkannt an der Architektur oder am Schlagwort "ltx-video".
    /// </summary>
    public bool IsLtxVideo => string.Equals(Model.GgufArchitecture, VideoModelCatalog.LtxArchitecture, StringComparison.OrdinalIgnoreCase)
                              || (Model.Tags ?? []).Any(tag => string.Equals(tag, "ltx-video", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Ein Bildmodell fürs Studio (FLUX.2 klein oder Z-Image): erkannt an der Architektur (flux, lumina2).
    /// Welches genau, prüft KI-Nexus nach dem Download in der Datei.
    /// </summary>
    public bool IsStudioImage => ImageModelCatalog.IsImageArchitecture(Model.GgufArchitecture);

    /// <summary>Kurzer Text für das Etikett in der Liste.</summary>
    public string KindBadge => IsLtxVideo ? "Videomodell (LTX)" : IsStudioImage ? "Bildmodell (FLUX / Z-Image)" : "kein Chat-Modell";

    public string KindTooltip => IsLtxVideo
        ? "Videomodell: nutzbar im Studio, wenn es LTX-2.3 ist. KI-Nexus prüft das nach dem Download."
        : IsStudioImage
            ? "Bildmodell: nutzbar im Studio, wenn es FLUX.2 klein oder Z-Image ist. KI-Nexus prüft das nach dem Download."
            : $"Art: {NotChatReason}. Zum Chatten nicht geeignet.";

    /// <summary>Für Screenreader: Die Liste liest diesen Text vor.</summary>
    public override string ToString() => Model.Id;
}

/// <summary>Eine Quantisierung zum Herunterladen, mit Ampel.</summary>
public sealed record HfOptionRow(HfQuantOption Option, FitEstimate Fit)
{
    public string Label => Option.Label;
    public string SizeText => DisplayFormat.Bytes(Option.TotalBytes);
    public string PartsText => Option.IsSplit ? $"{Option.Files.Count} Teile" : "";
    public bool IsRecommended => Option.IsRecommended;

    public override string ToString() => Label;
}

/// <summary>
/// Hugging Face (Bauplan 8.3): GGUF-Modelle suchen, Quantisierung mit Ampel wählen, herunterladen
/// (Pause, Fortsetzen, Prüfsumme). Optional ein Token für gesperrte Modelle, verschlüsselt mit DPAPI.
/// </summary>
public sealed partial class HuggingFaceViewModel : ObservableObject
{
    private const int ResultLimit = 30;

    private readonly CreateHubViewModel _hub;
    private readonly HuggingFaceClient _client;
    private readonly ModelDownloader _downloader;
    private readonly SecretStore _tokenStore;
    private readonly IShellService _shell;
    private readonly ILogger<HuggingFaceViewModel> _logger;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _filesRequest;

    public HuggingFaceViewModel(CreateHubViewModel hub, HuggingFaceClient client, ModelDownloader downloader, SecretStore tokenStore,
        IShellService shell, IDialogService dialogs, ILogger<HuggingFaceViewModel> logger)
    {
        _hub = hub;
        _dialogs = dialogs;
        _client = client;
        _downloader = downloader;
        _tokenStore = tokenStore;
        _shell = shell;
        _logger = logger;
        _hasToken = tokenStore.HasSecret;

        downloader.JobsChanged += (_, _) => hub.Post(SyncDownloads);
        downloader.RestorePending();
        SyncDownloads();
    }

    // ----------------------------------------------------------- Suche

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _query = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private bool _isSearching;

    [ObservableProperty] private string _searchInfo = "Suche nach einem Modell, z.B. \"qwen3\", \"gemma 3\" oder \"llama 3.2\". Es werden nur GGUF-Modelle gezeigt.";

    public ObservableCollection<HfResultRow> Results { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private HfResultRow? _selectedResult;

    public bool HasSelection => SelectedResult is not null;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        IsSearching = true;
        SearchInfo = "Suche läuft …";
        try
        {
            var models = await _client.SearchAsync(Query, ResultLimit, CancellationToken.None);
            Results.Clear();
            foreach (var model in models)
            {
                Results.Add(new HfResultRow(model));
            }

            SearchInfo = models.Count == 0
                ? "Nichts gefunden. Versuch es mit einem kürzeren Begriff."
                : $"{models.Count} Modelle, die beliebtesten zuerst. Klick eines an, um seine Dateien zu sehen.";
            SelectedResult = null;
        }
        catch (HuggingFaceException ex)
        {
            SearchInfo = ex.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    private bool CanSearch => !IsSearching;

    // ----------------------------------------------------------- Dateien des gewählten Modells

    public ObservableCollection<HfOptionRow> Options { get; } = [];

    [ObservableProperty] private bool _isLoadingFiles;
    [ObservableProperty] private string _filesInfo = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjector), nameof(ProjectorText))]
    private HfFile? _projector;

    /// <summary>Den Bild-Projektor mitladen? Ohne ihn versteht die KI keine Bilder.</summary>
    [ObservableProperty] private bool _includeProjector = true;

    public bool HasProjector => Projector is not null;

    public string ProjectorText => Projector is null ? "" : $"Bild-Projektor mitladen ({Projector.FileName}, {DisplayFormat.Bytes(Projector.Size)}): Damit versteht die KI Bilder.";

    partial void OnSelectedResultChanged(HfResultRow? value) => _ = LoadFilesAsync(value);

    private async Task LoadFilesAsync(HfResultRow? row)
    {
        _filesRequest?.Cancel();
        Options.Clear();
        Projector = null;
        FilesInfo = "";
        if (row is null)
        {
            return;
        }

        using var request = _filesRequest = new CancellationTokenSource();
        IsLoadingFiles = true;
        try
        {
            var files = await _client.GetFilesAsync(row.Model.Id, request.Token);
            if (request.IsCancellationRequested)
            {
                return;
            }

            Projector = files.Projectors.FirstOrDefault();
            var projectorBytes = Projector?.Size ?? 0;
            foreach (var option in files.Options)
            {
                Options.Add(new HfOptionRow(option, _hub.Estimate(option.TotalBytes, projectorBytes, 8192)));
            }

            var recommended = files.Options.FirstOrDefault(o => o.IsRecommended)?.Label;
            FilesInfo = files.Options.Count == 0
                ? "In diesem Repository gibt es keine GGUF-Modelle (vielleicht nur Projektoren)."
                : $"Lizenz: {row.LicenseText}. Q4_K_M ist meist ein guter Standard: klein und kaum schlechter als das Original."
                  + (recommended is not null && recommended != HuggingFaceProtocol.RecommendedQuant ? $" Das gibt es hier nicht, darum ist {recommended} empfohlen." : "")
                  + " Die Ampel ist eine Schätzung für 8192 Token Kontext.";
            if (row.IsLtxVideo)
            {
                FilesInfo = $"Lizenz: {row.LicenseText}. Videomodell (LTX): Nach dem Download prüft KI-Nexus, ob es LTX-2.3 ist, und bietet es im Studio an. "
                    + "distilled (8 Schritte) ist schnell, dev (20 Schritte) genauer. Q4_K_M ist ein guter Standard, Q2_K/Q3_K sind kleiner und etwas ungenauer. "
                    + "Die Ampel unten gilt nur für Sprachmodelle.";
            }
            else if (row.IsStudioImage)
            {
                FilesInfo = $"Lizenz: {row.LicenseText}. Bildmodell: Nach dem Download prüft KI-Nexus, ob es FLUX.2 klein (4B oder 9B) oder Z-Image ist, "
                    + "und bietet es im Studio an. Text-Encoder und VAE kommen aus models\\image (setup-video.ps1 -Images). "
                    + "Q8_0 ist fast verlustfrei, Q4_K_M kleiner. Die Ampel unten gilt nur für Sprachmodelle.";
            }
            else if (row.IsNotChat)
            {
                FilesInfo += $" Achtung: Das ist kein Chat-Modell ({row.NotChatReason}). KI-Nexus kann damit nicht chatten.";
            }
            if (row.IsGated && !HasToken)
            {
                FilesInfo += " Dieses Modell ist gesperrt (gated): Akzeptiere die Bedingungen auf Hugging Face und hinterlege unten einen Token.";
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            // Ein anderes Modell wurde gewählt.
        }
        catch (HuggingFaceException ex)
        {
            FilesInfo = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_filesRequest, request))
            {
                IsLoadingFiles = false;
                _filesRequest = null;
            }
        }
    }

    /// <summary>Öffnet das gewählte Modell (oder die GGUF-Liste) im Browser. Die Datei kann man danach per Drag &amp; Drop importieren.</summary>
    [RelayCommand]
    private void OpenOnHuggingFace()
    {
        var uri = SelectedResult is { } row
            ? _client.WebPage(row.Model.Id)
            : new Uri(HuggingFaceClient.DefaultBaseUri, "models?library=gguf&sort=downloads");
        _shell.OpenUrl(uri);
    }

    [RelayCommand]
    private void Download(HfOptionRow row)
    {
        if (SelectedResult is not { } result)
        {
            return;
        }

        // Bildmodell fürs Studio: kurz nachfragen, es ist kein Chat-Modell.
        if (result.IsStudioImage)
        {
            if (!_dialogs.Confirm("Bildmodell herunterladen",
                    $"{result.Model.Id} ({row.Label}) ist ein Bildmodell fürs Studio, kein Chat-Modell. Der Download braucht {row.SizeText}. "
                    + "Dazu braucht es Text-Encoder und VAE (einmal, setup-video.ps1 -Images). Herunterladen?", "Herunterladen"))
            {
                return;
            }
        }

        // Videomodell (LTX): für den Video-Tab. Trotzdem nachfragen, es sind viele Gigabyte.
        else if (result.IsLtxVideo)
        {
            if (!_dialogs.Confirm("Videomodell herunterladen",
                    $"{result.Model.Id} ({row.Label}) ist ein Videomodell für den Video-Tab, kein Chat-Modell. Der Download braucht {row.SizeText}. "
                    + "Für Videos braucht es zusätzlich Text-Encoder und VAEs (ca. 11 GB, einmal, über den Video-Tab). Herunterladen?", "Herunterladen"))
            {
                return;
            }
        }

        // Kein Chat-Modell (z.B. ein Bildmodell): lieber nachfragen, bevor viele Gigabyte geladen werden.
        else if (result.NotChatReason is { } reason && !_dialogs.Confirm("Kein Chat-Modell",
                $"{result.Model.Id} ist kein Chat-Modell ({reason}). KI-Nexus kann damit wahrscheinlich nicht chatten. "
                + $"Der Download braucht {row.SizeText}. Trotzdem herunterladen?", "Trotzdem laden"))
        {
            return;
        }

        var projector = IncludeProjector ? Projector : null;
        var job = _downloader.Start(new DownloadPlan(result.Model.Id, row.Label, row.Option.Files, projector, result.Model.License));
        _logger.LogInformation("Download {Id} gestartet ({Repo}, {Label}).", job.Id, result.Model.Id, row.Label);
    }

    // ----------------------------------------------------------- Downloads

    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = [];

    public bool HasDownloads => Downloads.Count > 0;

    private void SyncDownloads()
    {
        var jobs = _downloader.Jobs;
        foreach (var item in Downloads.Where(d => !jobs.Contains(d.Job)).ToList())
        {
            item.Detach();
            Downloads.Remove(item);
        }

        foreach (var job in jobs.Where(j => Downloads.All(d => d.Job != j)))
        {
            Downloads.Insert(0, new DownloadItemViewModel(job, _downloader, _hub));
        }

        OnPropertyChanged(nameof(HasDownloads));
    }

    // ----------------------------------------------------------- Token

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenStatus))]
    private bool _hasToken;

    public string TokenStatus => HasToken
        ? "Token gespeichert, verschlüsselt mit Windows (DPAPI). Nur dein Windows-Konto kann ihn lesen."
        : "Kein Token gespeichert. Nur nötig für gesperrte Modelle (z.B. von Meta oder Google).";

    /// <summary>Der Token kommt aus einem PasswordBox (nicht gebunden, damit er nicht im Speicher herumliegt).</summary>
    public void SaveToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        _tokenStore.Save(token);
        HasToken = true;
        _hub.ShowNotice("Token gespeichert (verschlüsselt).", isError: false);
    }

    [RelayCommand]
    private void DeleteToken()
    {
        _tokenStore.Delete();
        HasToken = false;
        _hub.ShowNotice("Token gelöscht.", isError: false);
    }

    public Task RefreshAsync()
    {
        HasToken = _tokenStore.HasSecret;
        return Task.CompletedTask;
    }
}
