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
/// In M1 zeigen Terminal und Simpel das Test-Feld, Gespräch kommt in M4.
/// </summary>
public sealed partial class ChatTabViewModel : TabViewModel
{
    private static readonly PlaceholderInfo ConversationPlaceholder = new("Gespräch-Stil", Glyphs.Microphone, 4,
        "Du sprichst Hochdeutsch, die KI antwortet mit Stimme. Mit Push-to-Talk oder freihändig.");

    public ChatTabViewModel(ChatStyle initialStyle, EngineTestViewModel engineTest) : base("chat", "Chat", Glyphs.Chat)
    {
        _style = initialStyle;
        EngineTest = engineTest;
    }

    public EngineTestViewModel EngineTest { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Content))]
    private ChatStyle _style;

    /// <summary>Was im Tab angezeigt wird. WPF wählt die Vorlage passend zum Typ (Views/DataTemplates.xaml).</summary>
    public object Content => Style == ChatStyle.Conversation ? ConversationPlaceholder : EngineTest;
}
