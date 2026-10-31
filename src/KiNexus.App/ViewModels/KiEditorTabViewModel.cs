using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.App.Services;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Settings;
using KiNexus.Engines.Ollama;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>Was die zwei Editor-Tabs brauchen. Ein Paket, damit die Konstruktoren kurz bleiben.</summary>
public sealed record KiEditorServices(IKiRegistry Kis, ISettingsService Settings, IDialogService Dialogs, OllamaBackend Ollama);

/// <summary>
/// Gemeinsame Grundlage für die Tabs "System-Prompt" und "Feineinstellungen":
/// links die Liste der KIs, rechts ein Editor. Ungespeicherte Änderungen gehen nicht stillschweigend verloren.
/// </summary>
public abstract partial class KiEditorTabViewModel : TabViewModel
{
    private readonly KiEditorServices _services;
    private readonly SynchronizationContext _ui;
    private bool _ignoreSelection;

    /// <summary>Die KI, deren Werte gerade im Editor stehen.</summary>
    private string? _editingId;

    protected KiEditorTabViewModel(string id, string title, string icon, KiEditorServices services, ILogger logger)
        : base(id, title, icon)
    {
        _services = services;
        Logger = logger;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        Kis = new ObservableCollection<KiProfile>(Sorted());

        // Neue, geänderte oder gelöschte KIs sofort zeigen (auch Änderungen aus dem anderen Tab).
        services.Kis.Changed += (_, _) => _ui.Post(_ => RefreshKis(), null);
    }

    protected ILogger Logger { get; }

    protected ISettingsService SettingsService => _services.Settings;

    public ObservableCollection<KiProfile> Kis { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKi))]
    [NotifyPropertyChangedFor(nameof(IsOllamaKi))]
    private KiProfile? _selectedKi;

    public bool HasKi => SelectedKi is not null;

    public bool IsOllamaKi => SelectedKi?.Engine == EngineKind.Ollama;

    /// <summary>Gibt es Änderungen, die noch nicht gespeichert sind?</summary>
    public abstract bool IsDirty { get; }

    // ----------------------------------------------------------- Meldung unten

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = "";

    [ObservableProperty] private bool _statusIsError;

    public bool HasStatus => Status.Length > 0;

    protected void ShowStatus(string text, bool isError = false)
    {
        Status = text;
        StatusIsError = isError;
    }

    [RelayCommand]
    private void DismissStatus() => ShowStatus("");

    // ----------------------------------------------------------- KI wechseln

    /// <summary>Beim Öffnen des Tabs: ist noch keine KI gewählt, die aus der Kopfleiste nehmen.</summary>
    public void Activate(KiProfile? preferred)
    {
        if (SelectedKi is null)
        {
            SelectedKi = Kis.FirstOrDefault(k => k.Id == preferred?.Id) ?? Kis.FirstOrDefault();
        }
    }

    partial void OnSelectedKiChanged(KiProfile? oldValue, KiProfile? newValue)
    {
        if (_ignoreSelection)
        {
            return;
        }

        if (newValue?.Id == _editingId)
        {
            return;
        }

        if (IsDirty && Registry.Find(_editingId) is { } editing && !_services.Dialogs.Confirm("Nicht gespeichert",
                $"Deine Änderungen an \"{editing.DisplayName}\" sind noch nicht gespeichert. Verwerfen und zur anderen KI wechseln?", "Verwerfen"))
        {
            // Zurück zur alten KI. Erst nach dem Klick, sonst zeigt die Liste die falsche Auswahl.
            _ui.Post(_ => SelectQuietly(Kis.FirstOrDefault(k => k.Id == _editingId) ?? oldValue), null);
            return;
        }

        ShowStatus("");
        LoadEditor(newValue);
    }

    /// <summary>Die Felder mit den Werten der KI füllen. Null = keine KI gewählt.</summary>
    protected abstract void Load(KiProfile? ki);

    protected IKiRegistry Registry => _services.Kis;

    /// <summary>Die aktuelle, gespeicherte Fassung der KI (ein anderer Tab hat sie vielleicht geändert).</summary>
    protected KiProfile? Current => _editingId is null ? null : Registry.Find(_editingId);

    /// <summary>
    /// Bei Ollama-KIs das eigene Ollama-Modell neu erstellen, z.B. "mael-ki-ollama" (Bauplan 8.4).
    /// Gibt einen Satz für die Meldung zurück. Läuft Ollama nicht, bleibt die KI trotzdem gespeichert.
    /// </summary>
    protected async Task<(string Text, bool IsError)> SyncOllamaModelAsync(KiProfile ki)
    {
        if (ki.Engine != EngineKind.Ollama)
        {
            return ("", false);
        }

        try
        {
            var name = await _services.Ollama.CreateModelAsync(ki, CancellationToken.None);
            return ($"Das Ollama-Modell \"{name}\" ist aktualisiert.", false);
        }
        catch (InferenceException ex)
        {
            Logger.LogWarning(ex, "Ollama-Modell für {Id} nicht erstellt.", ki.Id);
            return ($"Das Ollama-Modell \"{OllamaProtocol.OwnModelName(ki)}\" konnte nicht erstellt werden: {ex.Message}", true);
        }
    }

    /// <summary>Knopf "Ollama-Modell aktualisieren": z.B. wenn Ollama beim Speichern nicht lief.</summary>
    [RelayCommand]
    private async Task UpdateOllamaModelAsync()
    {
        if (Current is not { Engine: EngineKind.Ollama } ki)
        {
            return;
        }

        ShowStatus($"Ollama-Modell \"{OllamaProtocol.OwnModelName(ki)}\" wird erstellt …");
        var (text, isError) = await SyncOllamaModelAsync(ki);
        ShowStatus(text, isError);
    }

    /// <summary>Nach dem Speichern: Editor neu füllen, damit er zeigt, was jetzt in der Datei steht.</summary>
    protected void Reload() => LoadEditor(Current);

    private void LoadEditor(KiProfile? ki)
    {
        _editingId = ki?.Id;
        Load(ki);
    }

    private void SelectQuietly(KiProfile? ki)
    {
        _ignoreSelection = true;
        try
        {
            SelectedKi = ki;
        }
        finally
        {
            _ignoreSelection = false;
        }
    }

    private IReadOnlyList<KiProfile> Sorted() => KiRegistry.SortForDisplay(Registry.All, _services.Settings.Current.EngineOrder);

    /// <summary>Liste neu aufbauen. Ungespeicherte Änderungen der gewählten KI bleiben stehen.</summary>
    private void RefreshKis()
    {
        _ignoreSelection = true;
        try
        {
            Kis.Clear();
            foreach (var ki in Sorted())
            {
                Kis.Add(ki);
            }

            SelectedKi = Kis.FirstOrDefault(k => k.Id == _editingId);
        }
        finally
        {
            _ignoreSelection = false;
        }

        if (SelectedKi is null && _editingId is not null)
        {
            // Die KI wurde gelöscht.
            LoadEditor(null);
        }
        else if (!IsDirty && SelectedKi is not null)
        {
            Load(SelectedKi);
        }
    }
}
