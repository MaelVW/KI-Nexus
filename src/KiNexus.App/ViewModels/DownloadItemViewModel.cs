using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.Engines.HuggingFace;

namespace KiNexus.App.ViewModels;

/// <summary>Ein Download in der Liste: Fortschritt, Tempo, Pause/Fortsetzen/Abbrechen.</summary>
public sealed partial class DownloadItemViewModel : ObservableObject
{
    private readonly ModelDownloader _downloader;
    private readonly CreateHubViewModel _hub;
    private DownloadState _lastState;

    public DownloadItemViewModel(DownloadJob job, ModelDownloader downloader, CreateHubViewModel hub)
    {
        Job = job;
        _downloader = downloader;
        _hub = hub;
        _lastState = job.State;
        job.Changed += OnJobChanged;
        Update();
    }

    public DownloadJob Job { get; }

    public string Title => $"{Job.Plan.Repo} · {Job.Plan.Label}" + (Job.Plan.Projector is null ? "" : " + Bild-Projektor");

    [ObservableProperty] private double _fraction;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _stateText = "";
    [ObservableProperty] private bool _isError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseCommand), nameof(ResumeCommand), nameof(CancelCommand), nameof(DismissCommand), nameof(CreateKiCommand))]
    private DownloadState _state;

    public bool IsCompleted => State == DownloadState.Completed;

    public void Detach() => Job.Changed -= OnJobChanged;

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause() => _downloader.Pause(Job);

    private bool CanPause => State == DownloadState.Running;

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume() => _downloader.Resume(Job);

    private bool CanResume => State is DownloadState.Paused or DownloadState.Failed;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private Task CancelAsync() => _downloader.CancelAsync(Job);

    private bool CanCancel => State is DownloadState.Running or DownloadState.Paused or DownloadState.Failed;

    [RelayCommand(CanExecute = nameof(IsCompleted))]
    private void Dismiss() => _downloader.Dismiss(Job);

    [RelayCommand(CanExecute = nameof(IsCompleted))]
    private Task CreateKi() => Job.Result is { } model ? _hub.StartWizardAsync(model) : Task.CompletedTask;

    private void OnJobChanged(object? sender, EventArgs e) => _hub.Post(Update);

    private void Update()
    {
        State = Job.State;
        OnPropertyChanged(nameof(IsCompleted));
        Fraction = Job.TotalBytes > 0 ? Math.Min(1, Job.BytesDone / (double)Job.TotalBytes) : 0;
        var amount = $"{DisplayFormat.Bytes(Job.BytesDone)} / {DisplayFormat.Bytes(Job.TotalBytes)}";
        IsError = State == DownloadState.Failed;
        (StateText, ProgressText) = State switch
        {
            DownloadState.Running when Job.BytesPerSecond > 0 => ("Lädt …",
                $"{amount} · {DisplayFormat.Speed(Job.BytesPerSecond)} · {DisplayFormat.Remaining(Job.TotalBytes - Job.BytesDone, Job.BytesPerSecond)}"),
            DownloadState.Running => ("Verbinde …", amount),
            DownloadState.Paused => ("Pausiert", amount + " · \"Fortsetzen\" macht dort weiter"),
            DownloadState.Verifying => ("Prüfe die Prüfsumme (SHA-256) …", amount),
            DownloadState.Completed => ("Fertig und geprüft", $"{DisplayFormat.Bytes(Job.TotalBytes)} · liegt in models/{Job.Result?.FolderName}"),
            DownloadState.Failed => ("Fehler", Job.Error ?? "Unbekannter Fehler."),
            _ => ("Abgebrochen", ""),
        };

        if (State != _lastState && State == DownloadState.Completed && Job.Result is { } model)
        {
            _hub.ShowNotice($"Download fertig: {model.FolderName} ist jetzt ein Grundmodell.", isError: false,
                "Neue KI daraus", () => _hub.StartWizardAsync(model));
        }

        _lastState = State;
    }
}
