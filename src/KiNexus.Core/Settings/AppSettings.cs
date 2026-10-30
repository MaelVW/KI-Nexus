namespace KiNexus.Core.Settings;

/// <summary>
/// Alle App-Einstellungen. Wird als data/settings.json gespeichert.
/// Jeder Wert hat einen Standard. So fehlt nie etwas, auch wenn die Datei alt ist.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Version des Formats. Steigt, wenn sich die Struktur ändert.</summary>
    public int SchemaVersion { get; set; } = 1;

    public PathSettings Paths { get; set; } = new();
    public PortSettings Ports { get; set; } = new();
    public EngineOrder EngineOrder { get; set; } = EngineOrder.LlamaCppFirst;

    /// <summary>Id der Standard-KI (Dateiname in kis/ ohne .json). Null = noch keine.</summary>
    public string? DefaultKiId { get; set; }

    public ChatStyle DefaultChatStyle { get; set; } = ChatStyle.Simple;

    /// <summary>Akzentfarbe als Hex-Wert, z.B. "#00E5FF".</summary>
    public string AccentColor { get; set; } = HexColor.DefaultAccent.ToString();

    public VoiceSettings Voice { get; set; } = new();
    public BackgroundTaskSettings BackgroundTasks { get; set; } = new();
    public OpenWebUiSettings OpenWebUi { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
}

/// <summary>Ordner. Relative Pfade gelten ab dem Projektordner.</summary>
public sealed class PathSettings
{
    public string Models { get; set; } = "models";
    public string Engines { get; set; } = "engines";
    public string OpenWebUiData { get; set; } = @"%USERPROFILE%\ki\open-webui";
}

public sealed class PortSettings
{
    public const int DefaultLlamaServer = 8090;
    public const int DefaultOllama = 11434;
    public const int DefaultOpenWebUi = 8080;

    public int LlamaServer { get; set; } = DefaultLlamaServer;
    public int Ollama { get; set; } = DefaultOllama;
    public int OpenWebUi { get; set; } = DefaultOpenWebUi;
}

public sealed class VoiceSettings
{
    /// <summary>Name des Mikrofons. Null = Windows-Standard.</summary>
    public string? Microphone { get; set; }

    /// <summary>Name der Piper-Stimme. Null = Standardstimme.</summary>
    public string? Voice { get; set; }

    public ComputeDevice SpeechRecognitionDevice { get; set; } = ComputeDevice.Cpu;
    public string PushToTalkKey { get; set; } = "Space";
}

public sealed class BackgroundTaskSettings
{
    /// <summary>Chat-Titel nach der ersten Antwort vom Modell erzeugen lassen.</summary>
    public bool GenerateChatTitles { get; set; } = true;
}

public sealed class OpenWebUiSettings
{
    public bool StopOnExit { get; set; } = true;

    /// <summary>Feste Version, z.B. "0.6.30". Null oder leer = immer die neueste (@latest).</summary>
    public string? PinnedVersion { get; set; }
}

/// <summary>Grösse und Lage des Hauptfensters, dazu der zuletzt offene Tab.</summary>
public sealed class WindowSettings
{
    public const double MinWidth = 1100;
    public const double MinHeight = 700;

    /// <summary>Null = Fenster in der Bildschirmmitte öffnen.</summary>
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 800;
    public bool IsMaximized { get; set; }
    public string LastTab { get; set; } = "chat";

    /// <summary>
    /// Prüft, ob die Titelleiste des Fensters auf dem Bildschirm sichtbar wäre.
    /// Sonst könnte das Fenster nach dem Abstecken eines Monitors unsichtbar sein.
    /// </summary>
    public bool IsOnScreen(double screenLeft, double screenTop, double screenWidth, double screenHeight)
    {
        if (Left is not double left || Top is not double top)
        {
            return false;
        }

        const double minVisible = 100;
        var visibleWidth = Math.Min(left + Width, screenLeft + screenWidth) - Math.Max(left, screenLeft);
        var titleBarInside = top >= screenTop && top <= screenTop + screenHeight - minVisible;
        return visibleWidth >= minVisible && titleBarInside;
    }
}

public enum ChatStyle
{
    Terminal,
    Simple,
    Conversation,
}

public enum EngineOrder
{
    LlamaCppFirst,
    OllamaFirst,
}

public enum ComputeDevice
{
    Cpu,
    Gpu,
}
