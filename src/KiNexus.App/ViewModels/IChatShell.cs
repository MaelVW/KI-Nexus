using KiNexus.Core.Kis;
using KiNexus.Core.Settings;

namespace KiNexus.App.ViewModels;

/// <summary>
/// Was der Chat vom Hauptfenster braucht: KI-Auswahl, Stil wechseln, Modell entladen.
/// Das MainViewModel erfüllt diese Schnittstelle. So kennt der Chat das Hauptfenster nicht direkt.
/// </summary>
public interface IChatShell
{
    IReadOnlyList<KiProfile> Kis { get; }

    KiProfile? SelectedKi { get; set; }

    void SetStyle(ChatStyle style);

    Task UnloadAsync();

    /// <summary>Wählt die KI, wechselt in den Chat-Tab und beginnt einen neuen Chat (z.B. Test-Chat nach "Neue KI").</summary>
    void OpenChatWith(KiProfile ki);
}
