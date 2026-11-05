using System.Windows.Media;
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

    /// <summary>Statt der Schrift-Glyphe eine eigene Form (z.B. die lachende Maske im Fun-Tab). Null = Glyphe.</summary>
    public virtual Geometry? IconGeometry => null;

    public bool HasIconGeometry => IconGeometry is not null;
}

/// <summary>Ein Tab, der erst in einem späteren Meilenstein gebaut wird.</summary>
public sealed class PlaceholderTabViewModel(string id, string title, string icon, int milestone, string description)
    : TabViewModel(id, title, icon)
{
    public PlaceholderInfo Placeholder { get; } = new(title, icon, milestone, description);
}

/// <summary>
/// Der Chat-Tab: Seitenleiste + ein Chat in drei Ansichten (Stile).
/// Terminal und Simpel zeigen denselben ChatSessionViewModel. Der Gesprächsmodus ist im privaten Fun-Tab.
/// </summary>
public sealed partial class ChatTabViewModel : TabViewModel
{
    public ChatTabViewModel(ChatStyle initialStyle, ChatSessionViewModel session, ChatListViewModel chatList, TerminalViewModel terminal)
        : base("chat", "Chat", Glyphs.Chat)
    {
        _style = initialStyle == ChatStyle.Conversation ? ChatStyle.Simple : initialStyle;
        Session = session;
        ChatList = chatList;
        Terminal = terminal;
    }

    public ChatSessionViewModel Session { get; }
    public ChatListViewModel ChatList { get; }
    public TerminalViewModel Terminal { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSimple))]
    [NotifyPropertyChangedFor(nameof(IsTerminal))]
    private ChatStyle _style;

    public bool IsSimple => Style == ChatStyle.Simple;
    public bool IsTerminal => Style == ChatStyle.Terminal;
}
