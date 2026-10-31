using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KiNexus.Core;
using KiNexus.Core.Chats;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KiNexus.App.ViewModels;

/// <summary>Eine gespeicherte Fassung des Systemprompts in der Liste "Versionen".</summary>
public sealed class PromptVersionItem(PromptVersion version, bool isCurrent, string change)
{
    public PromptVersion Version { get; } = version;

    public bool IsCurrent { get; } = isCurrent;

    public string DateText => Version.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
        + (IsCurrent ? " · aktuell" : "");

    /// <summary>Was gegenüber der älteren Fassung neu ist, gekürzt.</summary>
    public string Preview { get; } = change.Length > 70 ? change[..70] + " …" : change;

    /// <summary>Für Screenreader.</summary>
    public override string ToString() => $"Fassung vom {DateText}: {Preview}";
}

/// <summary>Eine Nachricht im kurzen Test-Chat. Wird nicht gespeichert.</summary>
public sealed partial class TestMessageViewModel(bool isUser, string text) : ObservableObject
{
    public bool IsUser { get; } = isUser;

    public string Sender => IsUser ? "Du" : "KI";

    [ObservableProperty] private string _text = text;

    [ObservableProperty] private bool _isError;

    public override string ToString() => $"{Sender}: {Text}";
}

/// <summary>
/// Der System-Prompt-Tab (Bauplan 8.4): Systemprompt bearbeiten, Token schätzen, Vorlagen einfügen,
/// alte Fassungen wiederherstellen und den neuen Prompt kurz testen, ohne ihn zu speichern.
/// </summary>
public sealed partial class SystemPromptViewModel : KiEditorTabViewModel
{
    /// <summary>Für den kurzen Test reicht wenig Verlauf.</summary>
    private const int TestHistoryLimit = 6;

    private readonly SystemPromptService _prompts;
    private readonly EngineManager _engines;
    private CancellationTokenSource? _testCts;
    private string _savedPrompt = "";
    private int _versionsVersion;
    private string? _testKiId;

    public SystemPromptViewModel(KiEditorServices services, SystemPromptService prompts, EngineManager engines, ILogger<SystemPromptViewModel> logger)
        : base("systemPrompt", "System-Prompt", Glyphs.Edit, services, logger)
    {
        _prompts = prompts;
        _engines = engines;
    }

    public IReadOnlyList<PromptTemplate> Templates => KiTemplates.Prompts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenText))]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(DiscardCommand))]
    private string _prompt = "";

    public string TokenText
    {
        get
        {
            var tokens = SystemPromptService.EstimateTokens(Prompt);
            return $"ca. {tokens} Token · {Prompt.Length} Zeichen. Das Modell liest den Systemprompt bei jeder Frage mit.";
        }
    }

    public override bool IsDirty => SelectedKi is not null && Prompt.TrimEnd() != _savedPrompt.TrimEnd();

    /// <summary>Für die Knöpfe (das Toolkit braucht einen eindeutigen Namen).</summary>
    private bool HasChanges => IsDirty;

    public ObservableCollection<PromptVersionItem> Versions { get; } = [];

    [ObservableProperty] private string _versionsHint = "";

    // ----------------------------------------------------------- Laden und Speichern

    protected override void Load(KiProfile? ki)
    {
        // Andere KI: Der Test-Chat gehört zur alten KI.
        if (ki?.Id != _testKiId)
        {
            _testKiId = ki?.Id;
            ClearTest();
            TestStatus = IsTestOpen ? "Test mit dem Prompt aus dem Editor (auch ungespeichert). Ohne Nachdenken, nichts wird gespeichert." : "";
        }

        _savedPrompt = ki?.SystemPrompt ?? "";
        Prompt = _savedPrompt;
        OnPropertyChanged(nameof(IsDirty));
        _ = LoadVersionsAsync(ki?.Id);
    }

    private async Task LoadVersionsAsync(string? kiId)
    {
        var version = ++_versionsVersion;
        IReadOnlyList<PromptVersion> versions = [];
        try
        {
            if (kiId is not null)
            {
                versions = await _prompts.GetVersionsAsync(kiId, CancellationToken.None);
            }
        }
        catch (SqliteException ex)
        {
            Logger.LogError(ex, "Prompt-Versionen von {Id} nicht lesbar.", kiId);
            ShowStatus("Die alten Fassungen konnten nicht gelesen werden: " + ex.Message, isError: true);
        }

        if (version != _versionsVersion)
        {
            return;
        }

        Versions.Clear();
        var currentMarked = false;
        for (var i = 0; i < versions.Count; i++)
        {
            // "aktuell" nur bei der neusten Fassung, die dem gespeicherten Prompt entspricht.
            var isCurrent = !currentMarked && versions[i].Content.TrimEnd() == _savedPrompt.TrimEnd();
            currentMarked |= isCurrent;
            var older = i + 1 < versions.Count ? versions[i + 1].Content : null;
            Versions.Add(new PromptVersionItem(versions[i], isCurrent, SystemPromptService.DescribeChange(older, versions[i].Content)));
        }

        VersionsHint = versions.Count == 0
            ? "Noch keine Fassungen. Beim ersten Speichern merkt sich die App auch den bisherigen Prompt."
            : $"Die letzten {SystemPromptService.MaxVersions} Fassungen bleiben gespeichert.";
    }

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private async Task SaveAsync()
    {
        if (Current is not { } saved)
        {
            return;
        }

        KiProfile updated;
        try
        {
            updated = await _prompts.SaveAsync(saved, Prompt, CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            Logger.LogError(ex, "Systemprompt von {Id} nicht gespeichert.", saved.Id);
            ShowStatus("Speichern fehlgeschlagen: " + ex.Message, isError: true);
            return;
        }

        Reload();
        var (ollamaText, isError) = await SyncOllamaModelAsync(updated);
        ShowStatus(("Gespeichert. Gilt ab der nächsten Frage, auch in alten Chats. " + ollamaText).Trim(), isError);
    }

    [RelayCommand(CanExecute = nameof(HasChanges))]
    private void Discard()
    {
        Prompt = _savedPrompt;
        ShowStatus("Änderungen verworfen.");
    }

    /// <summary>Holt eine alte Fassung in den Editor. Gespeichert wird erst mit "Speichern".</summary>
    [RelayCommand]
    private void RestoreVersion(PromptVersionItem? item)
    {
        if (item is null)
        {
            return;
        }

        Prompt = item.Version.Content;
        ShowStatus($"Fassung vom {item.Version.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm} im Editor. Mit \"Speichern\" übernehmen oder zuerst testen.");
    }

    // ----------------------------------------------------------- Kurzer Test

    [ObservableProperty] private bool _isTestOpen;

    public ObservableCollection<TestMessageViewModel> TestMessages { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendTestCommand))]
    private string _testInput = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendTestCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopTestCommand))]
    private bool _isTesting;

    [ObservableProperty] private string _testStatus = "";

    [RelayCommand]
    private void OpenTest()
    {
        IsTestOpen = true;
        TestStatus = "Test mit dem Prompt aus dem Editor (auch ungespeichert). Ohne Nachdenken, nichts wird gespeichert.";
    }

    [RelayCommand]
    private void CloseTest()
    {
        StopTest();
        IsTestOpen = false;
    }

    [RelayCommand]
    private void ClearTest()
    {
        StopTest();
        TestMessages.Clear();
    }

    private bool CanSendTest => !IsTesting && TestInput.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanSendTest))]
    private async Task SendTestAsync()
    {
        if (Current is not { } saved)
        {
            return;
        }

        // Eine Kopie der KI mit dem Prompt aus dem Editor. Der Prompt ändert nichts am geladenen Modell.
        var ki = JsonDefaults.Clone(saved);
        ki.SystemPrompt = Prompt.TrimEnd();

        var question = TestInput.Trim();
        TestInput = "";
        var messages = new List<ChatMessage>();
        if (ki.SystemPrompt.Length > 0)
        {
            messages.Add(new ChatMessage(ChatRole.System, ki.SystemPrompt));
        }

        messages.AddRange(TestMessages.Where(m => !m.IsError).TakeLast(TestHistoryLimit)
            .Select(m => new ChatMessage(m.IsUser ? ChatRole.User : ChatRole.Assistant, m.Text)));
        messages.Add(new ChatMessage(ChatRole.User, question));

        TestMessages.Add(new TestMessageViewModel(isUser: true, question));
        var answer = new TestMessageViewModel(isUser: false, "");
        TestMessages.Add(answer);

        _testCts = new CancellationTokenSource();
        IsTesting = true;
        TestStatus = $"{ki.DisplayName} antwortet …";
        try
        {
            var progress = new Progress<string>(message => TestStatus = message);
            await foreach (var chunk in _engines.StreamAsync(new ChatRequest(ki, messages, Think: false), progress, _testCts.Token))
            {
                if (chunk.Text.Length > 0)
                {
                    answer.Text += chunk.Text;
                }

                if (chunk.Stats is { } stats)
                {
                    TestStatus = string.Create(CultureInfo.InvariantCulture, $"Fertig: {stats.CompletionTokens} Token, {stats.TokensPerSecond:F1} Token/s.");
                }
            }

            if (answer.Text.Length == 0)
            {
                answer.Text = "(keine Antwort)";
            }
        }
        catch (OperationCanceledException)
        {
            answer.Text = answer.Text.Length == 0 ? "(gestoppt)" : answer.Text + " … (gestoppt)";
            TestStatus = "Gestoppt.";
        }
        catch (Exception ex) when (ex is InferenceException or HttpRequestException or IOException)
        {
            Logger.LogWarning(ex, "Test-Chat für {Id} fehlgeschlagen.", ki.Id);
            answer.Text = ex.Message;
            answer.IsError = true;
            TestStatus = "Fehler beim Test.";
        }
        finally
        {
            IsTesting = false;
            _testCts.Dispose();
            _testCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsTesting))]
    private void StopTest() => _testCts?.Cancel();
}
