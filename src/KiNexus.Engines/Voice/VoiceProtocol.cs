using System.Text.Json;
using System.Text.Json.Nodes;

namespace KiNexus.Engines.Voice;

/// <summary>Ein Ereignis vom Sprach-Worker (eine Zeile auf seinem stdout).</summary>
public abstract record VoiceEvent;

public sealed record VoiceHello(int Pid) : VoiceEvent;

/// <summary>Whisper und Piper sind geladen.</summary>
public sealed record VoiceReady(string Device, int SampleRate, int LoadMs) : VoiceEvent;

/// <summary>Das Mikrofon ist offen ("pushToTalk" oder "handsFree").</summary>
public sealed record VoiceListening(string Mode, string? Microphone) : VoiceEvent;

/// <summary>Freihändig: Du hast angefangen zu sprechen.</summary>
public sealed record VoiceSpeechStart : VoiceEvent;

/// <summary>Freihändig: Die Sprechpause ist lang genug, du bist fertig.</summary>
public sealed record VoiceSpeechEnd : VoiceEvent;

public sealed record VoiceTranscribing(int AudioMs) : VoiceEvent;

/// <summary>Die Mitschrift. Leer, wenn nichts verstanden wurde (Reason sagt dann warum).</summary>
public sealed record VoiceTranscript(string Text, int AudioMs, int TookMs, string? Reason) : VoiceEvent;

/// <summary>Lautstärke am Mikrofon, 0 bis 1 (für den Orb).</summary>
public sealed record VoiceLevel(double Value) : VoiceEvent;

/// <summary>Ein Stück Sprachausgabe: PCM, 16 Bit, mono, mit der Abtastrate aus <see cref="VoiceReady"/>.</summary>
public sealed record VoiceAudio(int Id, byte[] Pcm) : VoiceEvent;

public sealed record VoiceSpeakDone(int Id, bool Cancelled) : VoiceEvent;

public sealed record VoiceStopped : VoiceEvent;

public sealed record VoiceMicrophones(IReadOnlyList<string> Items, string? Default) : VoiceEvent;

/// <summary>Ein Fehler. Fatal heisst: Der Worker kann so nicht weiterarbeiten (z.B. Modell fehlt).</summary>
public sealed record VoiceError(string Message, bool Fatal) : VoiceEvent;

/// <summary>Der Worker-Prozess ist beendet (unerwartet oder nach "shutdown").</summary>
public sealed record VoiceExited(int ExitCode) : VoiceEvent;

public sealed record VoiceUnknown(string Type) : VoiceEvent;

/// <summary>Einstellungen für den Worker (Befehl "configure").</summary>
public sealed record VoiceWorkerOptions(string WhisperPath, string VoicePath, string Device, string? Microphone, int SilenceMs, int Threads = 0);

/// <summary>
/// Übersetzt zwischen der App und dem Sprach-Worker (python/voice/worker.py). JSON Lines: eine Nachricht pro Zeile.
/// Das ganze Protokoll steht in docs/05-Sprache.md.
/// </summary>
public static class VoiceProtocol
{
    public static JsonObject Configure(VoiceWorkerOptions options) => new()
    {
        ["type"] = "configure",
        ["whisperPath"] = options.WhisperPath,
        ["voicePath"] = options.VoicePath,
        ["device"] = options.Device,
        ["microphone"] = options.Microphone,
        ["silenceMs"] = options.SilenceMs,
        ["threads"] = options.Threads,
    };

    /// <summary>Mikrofon öffnen. mode: "pushToTalk" (bis stop_listening) oder "handsFree" (VAD erkennt das Ende).</summary>
    public static JsonObject Listen(string mode) => new() { ["type"] = "listen", ["mode"] = mode };

    /// <summary>Mikrofon schliessen. Bei Push-to-Talk mit transcribe=true wird das Gesagte erkannt.</summary>
    public static JsonObject StopListening(bool transcribe) => new() { ["type"] = "stop_listening", ["transcribe"] = transcribe };

    public static JsonObject Pause() => Simple("pause");

    public static JsonObject Resume() => Simple("resume");

    public static JsonObject Speak(int id, string text) => new() { ["type"] = "speak", ["id"] = id, ["text"] = text };

    public static JsonObject StopSpeaking() => Simple("stop_speaking");

    public static JsonObject TranscribeFile(string path) => new() { ["type"] = "transcribe_file", ["path"] = path };

    public static JsonObject Shutdown() => Simple("shutdown");

    public static VoiceEvent? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var type = Text(root, "type") ?? "";
        return type switch
        {
            "hello" => new VoiceHello(Int(root, "pid")),
            "ready" => new VoiceReady(Text(root, "device") ?? "cpu", Int(root, "sampleRate", 22050), Int(root, "loadMs")),
            "listening" => new VoiceListening(Text(root, "mode") ?? "", Text(root, "microphone")),
            "speech_start" => new VoiceSpeechStart(),
            "speech_end" => new VoiceSpeechEnd(),
            "transcribing" => new VoiceTranscribing(Int(root, "audioMs")),
            "transcript" => new VoiceTranscript(Text(root, "text") ?? "", Int(root, "audioMs"), Int(root, "tookMs"), Text(root, "reason")),
            "level" => new VoiceLevel(root.TryGetProperty("value", out var level) ? level.GetDouble() : 0),
            "audio" => new VoiceAudio(Int(root, "id"), Convert.FromBase64String(Text(root, "pcm") ?? "")),
            "speak_done" => new VoiceSpeakDone(Int(root, "id"), root.TryGetProperty("cancelled", out var c) && c.ValueKind == JsonValueKind.True),
            "stopped" => new VoiceStopped(),
            "microphones" => new VoiceMicrophones(
                root.TryGetProperty("items", out var items) ? items.EnumerateArray().Select(i => i.GetString()).OfType<string>().ToList() : [],
                Text(root, "default")),
            "error" => new VoiceError(Text(root, "message") ?? "Unbekannter Fehler", root.TryGetProperty("fatal", out var f) && f.ValueKind == JsonValueKind.True),
            _ => new VoiceUnknown(type),
        };
    }

    private static JsonObject Simple(string type) => new() { ["type"] = type };

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Int(JsonElement root, string name, int fallback = 0) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : fallback;
}
