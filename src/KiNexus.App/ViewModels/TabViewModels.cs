using CommunityToolkit.Mvvm.ComponentModel;
using KiNexus.Core.Settings;

namespace KiNexus.App.ViewModels;

/// <summary>Was ein noch nicht gebauter Bereich anzeigt: "Kommt in Meilenstein …".</summary>
public sealed record PlaceholderInfo(string Title, string Icon, int Milestone, string Description)
{
    public string MilestoneText => $"Kommt in Meilenstein {Milestone}";
}

/// <summary>Grundklasse für alle Tabs im Hauptfenster.</summary>
public abstract class TabViewModel(string id, string title, string icon) : ObservableObject
{
    /// <summary>Kurzer englischer Name, wird in settings.json als "lastTab" gespeichert.</summary>
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Icon { get; } = icon;
}

/// <summary>Ein Tab, der erst in einem späteren Meilenstein gebaut wird.</summary>
public sealed class PlaceholderTabViewModel(string id, string title, string icon, int milestone, string description)
    : TabViewModel(id, title, icon)
{
    public PlaceholderInfo Placeholder { get; } = new(title, icon, milestone, description);
}

/// <summary>
/// Der Chat-Tab. Ein Chat, drei Ansichten (Stile).
/// In M0 zeigt jeder Stil nur, in welchem Meilenstein er kommt.
/// </summary>
public sealed partial class ChatTabViewModel : TabViewModel
{
    public ChatTabViewModel(ChatStyle initialStyle) : base("chat", "Chat", Glyphs.Chat)
    {
        _style = initialStyle;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    private ChatStyle _style;

    public PlaceholderInfo Placeholder => Style switch
    {
        ChatStyle.Terminal => new("Terminal-Stil", Glyphs.Terminal, 2,
            "Sieht aus wie ein Windows-Terminal, mit Befehlen wie /help, /think und /stats. Es laufen keine echten Shell-Befehle."),
        ChatStyle.Conversation => new("Gespräch-Stil", Glyphs.Microphone, 4,
            "Du sprichst Hochdeutsch, die KI antwortet mit Stimme. Mit Push-to-Talk oder freihändig."),
        _ => new("Simpel-Stil", Glyphs.Bubbles, 2,
            "Nachrichten als Blasen, mit Markdown, farbigem Code und angehängten Bildern."),
    };
}
