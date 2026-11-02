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

    public ChatSettings Chat { get; set; } = new();
    public VoiceSettings Voice { get; set; } = new();
    public BackgroundTaskSettings BackgroundTasks { get; set; } = new();
    public OpenWebUiSettings OpenWebUi { get; set; } = new();
    public VideoSettings Video { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
}

/// <summary>Video-Tab (Bauplan Video).</summary>
public sealed class VideoSettings
{
    public const int DefaultIdleStopMinutes = 10;

    /// <summary>ComfyUI nach so vielen Minuten ohne Video beenden (gibt RAM und VRAM frei). 0 = erst beim Schliessen der App.</summary>
    public int IdleStopMinutes { get; set; } = DefaultIdleStopMinutes;
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
    public const int DefaultComfyUi = 8188;

    public int LlamaServer { get; set; } = DefaultLlamaServer;
    public int Ollama { get; set; } = DefaultOllama;
    public int OpenWebUi { get; set; } = DefaultOpenWebUi;

    /// <summary>ComfyUI für Videomodelle (Bauplan Video).</summary>
    public int ComfyUi { get; set; } = DefaultComfyUi;
}

/// <summary>Einstellungen der Chat-Ansicht, die sich die App merkt.</summary>
public sealed class ChatSettings
{
    /// <summary>Gedanken (Thinking) anzeigen oder verstecken.</summary>
    public bool ShowThinking { get; set; } = true;

    /// <summary>Seitenleiste mit den Chats offen?</summary>
    public bool IsSidebarOpen { get; set; } = true;

    /// <summary>Dieser Chat wird beim Start wieder geöffnet.</summary>
    public long? LastChatId { get; set; }
}

public sealed class VoiceSettings
{
    /// <summary>Name des Mikrofons. Null = Windows-Standard.</summary>
    public string? Microphone { get; set; }

    /// <summary>Name der Piper-Stimme. Null = Standardstimme.</summary>
    public string? Voice { get; set; }

    public ComputeDevice SpeechRecognitionDevice { get; set; } = ComputeDevice.Cpu;
    public string PushToTalkKey { get; set; } = "Space";

    /// <summary>Whisper-Modell in models/voice/whisper-&lt;name&gt;/. "large-v3-turbo" erkennt am besten, "small" ist schneller.</summary>
    public string WhisperModel { get; set; } = "large-v3-turbo";

    /// <summary>Freihändig: So lange Stille, bis die App annimmt, dass du fertig bist (Millisekunden).</summary>
    public int SilenceMs { get; set; } = 800;

    /// <summary>Im Gespräch "Antworte kurz und so, wie man spricht." an den Systemprompt hängen.</summary>
    public bool ShortSpokenAnswers { get; set; } = true;

    /// <summary>Sprechtempo für Piper: 1 = normal, grösser = ruhiger und langsamer. Gilt für KIs ohne eigenes Tempo.</summary>
    public double LengthScale { get; set; } = 1.0;

    /// <summary>Freihändig: Sprichst du, während die KI antwortet, hört sie auf (Unterbrechen). Am besten mit Kopfhörern.</summary>
    public bool BargeIn { get; set; } = true;

    /// <summary>Freihändig: Beim Öffnen des Gesprächs gleich zuhören (sonst erst nach einem Klick auf den Orb).</summary>
    public bool AutoListen { get; set; } = true;
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
