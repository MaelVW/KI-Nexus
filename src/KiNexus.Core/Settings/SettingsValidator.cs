namespace KiNexus.Core.Settings;

/// <summary>
/// Prüft Einstellungen und ersetzt ungültige Werte durch den Standard.
/// So startet die App auch mit einer kaputten oder von Hand geänderten Datei.
/// </summary>
public static class SettingsValidator
{
    public const int MinPort = 1024;
    public const int MaxPort = 65535;

    public static bool IsValidPort(int port) => port is >= MinPort and <= MaxPort;

    /// <summary>
    /// Korrigiert ungültige Werte direkt im Objekt.
    /// Gibt eine Liste zurück, was korrigiert wurde (für das Log).
    /// </summary>
    public static IReadOnlyList<string> Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var fixes = new List<string>();

        FixMissingSections(settings, fixes);
        FixPorts(settings.Ports, fixes);
        FixPaths(settings.Paths, fixes);
        FixAccentColor(settings, fixes);
        FixEnums(settings, fixes);
        FixWindow(settings.Window, fixes);

        return fixes;
    }

    private static void FixMissingSections(AppSettings settings, List<string> fixes)
    {
        // In JSON kann "paths": null stehen. Dann ist die Eigenschaft trotz Standardwert null.
        // Fehlt ein ganzer Abschnitt (z.B. "chat" in einer Datei von M1), setzt der JSON-Leser den Standard selbst.
        if (settings.Paths is null) { settings.Paths = new(); fixes.Add("paths fehlte"); }
        if (settings.Ports is null) { settings.Ports = new(); fixes.Add("ports fehlte"); }
        if (settings.Chat is null) { settings.Chat = new(); fixes.Add("chat fehlte"); }
        if (settings.Voice is null) { settings.Voice = new(); fixes.Add("voice fehlte"); }
        if (settings.BackgroundTasks is null) { settings.BackgroundTasks = new(); fixes.Add("backgroundTasks fehlte"); }
        if (settings.OpenWebUi is null) { settings.OpenWebUi = new(); fixes.Add("openWebUi fehlte"); }
        if (settings.Window is null) { settings.Window = new(); fixes.Add("window fehlte"); }
    }

    private static void FixPorts(PortSettings ports, List<string> fixes)
    {
        if (!IsValidPort(ports.LlamaServer)) { fixes.Add($"Port llamaServer {ports.LlamaServer} ungültig"); ports.LlamaServer = PortSettings.DefaultLlamaServer; }
        if (!IsValidPort(ports.Ollama)) { fixes.Add($"Port ollama {ports.Ollama} ungültig"); ports.Ollama = PortSettings.DefaultOllama; }
        if (!IsValidPort(ports.OpenWebUi)) { fixes.Add($"Port openWebUi {ports.OpenWebUi} ungültig"); ports.OpenWebUi = PortSettings.DefaultOpenWebUi; }
    }

    private static void FixPaths(PathSettings paths, List<string> fixes)
    {
        var defaults = new PathSettings();
        if (string.IsNullOrWhiteSpace(paths.Models)) { paths.Models = defaults.Models; fixes.Add("Pfad models leer"); }
        if (string.IsNullOrWhiteSpace(paths.Engines)) { paths.Engines = defaults.Engines; fixes.Add("Pfad engines leer"); }
        if (string.IsNullOrWhiteSpace(paths.OpenWebUiData)) { paths.OpenWebUiData = defaults.OpenWebUiData; fixes.Add("Pfad openWebUiData leer"); }
    }

    private static void FixAccentColor(AppSettings settings, List<string> fixes)
    {
        if (HexColor.TryParse(settings.AccentColor, out var color))
        {
            settings.AccentColor = color.ToString();
            return;
        }

        fixes.Add($"Akzentfarbe '{settings.AccentColor}' ungültig");
        settings.AccentColor = HexColor.DefaultAccent.ToString();
    }

    private static void FixEnums(AppSettings settings, List<string> fixes)
    {
        if (!Enum.IsDefined(settings.DefaultChatStyle)) { fixes.Add("defaultChatStyle ungültig"); settings.DefaultChatStyle = ChatStyle.Simple; }
        if (!Enum.IsDefined(settings.EngineOrder)) { fixes.Add("engineOrder ungültig"); settings.EngineOrder = EngineOrder.LlamaCppFirst; }
        if (!Enum.IsDefined(settings.Voice.SpeechRecognitionDevice)) { fixes.Add("speechRecognitionDevice ungültig"); settings.Voice.SpeechRecognitionDevice = ComputeDevice.Cpu; }
        FixVoice(settings.Voice, fixes);
    }

    private static void FixVoice(VoiceSettings voice, List<string> fixes)
    {
        var defaults = new VoiceSettings();
        if (string.IsNullOrWhiteSpace(voice.WhisperModel)) { voice.WhisperModel = defaults.WhisperModel; fixes.Add("whisperModel fehlte"); }
        if (string.IsNullOrWhiteSpace(voice.PushToTalkKey)) { voice.PushToTalkKey = defaults.PushToTalkKey; fixes.Add("pushToTalkKey fehlte"); }
        if (voice.SilenceMs is < 300 or > 3000) { voice.SilenceMs = defaults.SilenceMs; fixes.Add("silenceMs ausserhalb 300–3000"); }
    }

    private static void FixWindow(WindowSettings window, List<string> fixes)
    {
        var defaults = new WindowSettings();
        if (!double.IsFinite(window.Width) || window.Width < WindowSettings.MinWidth) { window.Width = defaults.Width; fixes.Add("Fensterbreite ungültig"); }
        if (!double.IsFinite(window.Height) || window.Height < WindowSettings.MinHeight) { window.Height = defaults.Height; fixes.Add("Fensterhöhe ungültig"); }
        if (string.IsNullOrWhiteSpace(window.LastTab)) { window.LastTab = defaults.LastTab; }
    }
}
