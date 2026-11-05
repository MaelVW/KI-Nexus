using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using KiNexus.Core.Chats;
using KiNexus.Core.Inference;

namespace KiNexus.App.ViewModels;

/// <summary>Eine Nachricht im Chat, so wie die Oberfläche sie braucht. Simpel- und Terminal-Stil zeigen dieselben Objekte.</summary>
public sealed partial class MessageViewModel : ObservableObject
{
    public MessageViewModel(MessageRecord record, string? kiName)
    {
        Role = record.Role;
        KiName = kiName;
        Images = record.AttachmentPaths;
        ApplySaved(record);
    }

    /// <summary>Platzhalter für eine Antwort, die gerade entsteht.</summary>
    public MessageViewModel(string? kiName)
    {
        Role = ChatRole.Assistant;
        KiName = kiName;
        Images = [];
        _isStreaming = true;
    }

    public long Id { get; private set; }

    /// <summary>Belegter Kontext nach dieser Antwort (von der Engine gemeldet).</summary>
    public int? ContextTokens { get; private set; }

    public ChatRole Role { get; }
    public bool IsUser => Role == ChatRole.User;
    public bool IsAssistant => Role == ChatRole.Assistant;
    public string? KiName { get; }
    public IReadOnlyList<string> Images { get; }
    public bool HasImages => Images.Count > 0;

    [ObservableProperty] private string _content = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThinking))]
    private string _thinking = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThinkingHeader))]
    private bool _isStreaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThinkingHeader))]
    private long? _thinkingMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLiked))]
    [NotifyPropertyChangedFor(nameof(IsDisliked))]
    private int? _rating;

    [ObservableProperty] private string _statsText = "";
    [ObservableProperty] private bool _showStats;
    [ObservableProperty] private bool _isThinkingExpanded;
    [ObservableProperty] private bool _isLastUser;
    [ObservableProperty] private bool _isLastAssistant;

    public bool HasThinking => Thinking.Length > 0;
    public bool IsLiked => Rating == 1;
    public bool IsDisliked => Rating == -1;

    /// <summary>"Nachgedacht für 12 Sekunden" (Bauplan 8.2) oder "Denkt nach …" während des Streamens.</summary>
    public string ThinkingHeader => IsStreaming && Content.Length == 0
        ? "Denkt nach …"
        : ThinkingMs is long ms ? $"Nachgedacht für {Math.Max(1, Math.Round(ms / 1000.0))} Sekunden" : "Gedanken";

    /// <summary>Der Text ohne Gefühls-Markierungen ([[gefuehl:…]], Avatar). So wird er angezeigt.</summary>
    public string DisplayContent => KiNexus.Core.Chats.EmotionTags.Strip(Content);

    partial void OnContentChanged(string value)
    {
        OnPropertyChanged(nameof(ThinkingHeader));
        OnPropertyChanged(nameof(DisplayContent));
    }

    /// <summary>Übernimmt die gespeicherten Werte (Id, Statistik) nach dem Streamen.</summary>
    public void ApplySaved(MessageRecord record)
    {
        Id = record.Id;
        ContextTokens = record.ContextTokens;
        Content = record.Content;
        Thinking = record.Thinking;
        ThinkingMs = record.ThinkingMs;
        Rating = record.Rating;
        StatsText = FormatStats(record, KiName);
        IsStreaming = false;
    }

    public static string FormatStats(MessageRecord r, string? kiName)
    {
        if (r.CompletionTokens is not int tokens)
        {
            return "Keine Statistik (abgebrochen).";
        }

        var engine = r.Engine == "ollama" ? "Ollama" : "llama.cpp";
        var seconds = (r.DurationMs ?? 0) / 1000.0;
        var parts = new List<string>
        {
            $"{tokens} Token",
            string.Create(CultureInfo.InvariantCulture, $"{r.TokensPerSecond ?? 0:F1} Token/s"),
            string.Create(CultureInfo.InvariantCulture, $"{seconds:F1} s"),
            engine,
        };
        if (!string.IsNullOrEmpty(kiName))
        {
            parts.Add(kiName);
        }

        return string.Join(" · ", parts);
    }
}
