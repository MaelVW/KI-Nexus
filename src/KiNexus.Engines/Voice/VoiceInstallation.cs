using KiNexus.Core.Paths;
using KiNexus.Core.Settings;

namespace KiNexus.Engines.Voice;

/// <summary>
/// Wo die Teile der Sprache liegen: Python-Umgebung, Worker-Skript, Whisper-Modelle und Piper-Stimmen.
/// Eingerichtet wird alles mit scripts\setup-voice.ps1.
/// </summary>
public sealed record VoiceInstallation(string PythonExe, string WorkerScript, string VoiceModelsDirectory)
{
    public const string DefaultVoice = "de_DE-thorsten-high";

    /// <summary>Weibliche Stimme, jung und ruhig (gemessen: ca. 205 Hz, sauberstes Signal der deutschen Frauenstimmen).</summary>
    public const string FemaleVoice = "de_DE-ramona-low";

    /// <summary>
    /// Stimmen mit vielen Sprechern: nur ausgewählte Sprecherinnen anbieten. Ausgesucht nach Tonhöhe (jung),
    /// ruhiger Sprechmelodie und sauberem Klang (siehe docs/05-Sprache.md).
    /// </summary>
    private static readonly Dictionary<string, int[]> ChosenSpeakers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de_DE-mls-medium"] = [110, 120],
    };

    /// <summary>Namen für die Auswahl. Unbekannte Stimmen zeigen ihren Dateinamen.</summary>
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["de_DE-thorsten-high"] = "Thorsten (männlich, beste Qualität)",
        ["de_DE-thorsten-medium"] = "Thorsten (männlich, mittel)",
        ["de_DE-ramona-low"] = "Ramona (weiblich, jung)",
        ["de_DE-kerstin-low"] = "Kerstin (weiblich, tiefer)",
        ["de_DE-mls-medium#110"] = "Sprecherin 110 (weiblich, jung, ruhig)",
        ["de_DE-mls-medium#120"] = "Sprecherin 120 (weiblich, ruhig)",
    };

    public static VoiceInstallation For(AppPaths paths, string modelsDirectory) => new(
        Path.Combine(paths.Root, "python", ".venv", "Scripts", "python.exe"),
        Path.Combine(paths.Root, "python", "voice", "worker.py"),
        Path.Combine(modelsDirectory, "voice"));

    public string WhisperPath(string model) => Path.Combine(VoiceModelsDirectory, "whisper-" + model);

    /// <summary>Datei der Stimme. "de_DE-mls-medium#110" (Sprecherin 110) liegt in de_DE-mls-medium.onnx.</summary>
    public string VoicePath(string voice) => Path.Combine(VoiceModelsDirectory, "piper", FileName(voice) + ".onnx");

    /// <summary>Sprecher-Nummer bei Stimmen mit vielen Sprechern ("…#110"), sonst null.</summary>
    public static int? SpeakerId(string voice) =>
        voice.IndexOf('#') is var hash and > 0 && int.TryParse(voice[(hash + 1)..], out var id) ? id : null;

    public static string DisplayName(string voice) => Names.TryGetValue(voice, out var name) ? name : voice;

    private static string FileName(string voice) => voice.IndexOf('#') is var hash and > 0 ? voice[..hash] : voice;

    /// <summary>Eingerichtete Whisper-Modelle, z.B. "large-v3-turbo" und "small".</summary>
    public IReadOnlyList<string> WhisperModels() =>
        Directory.Exists(VoiceModelsDirectory)
            ? Directory.EnumerateDirectories(VoiceModelsDirectory, "whisper-*")
                .Where(d => File.Exists(Path.Combine(d, "model.bin")))
                .Select(d => Path.GetFileName(d)["whisper-".Length..])
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];

    /// <summary>Eingerichtete Piper-Stimmen (ONNX-Datei mit passender .onnx.json).</summary>
    public IReadOnlyList<string> Voices()
    {
        var folder = Path.Combine(VoiceModelsDirectory, "piper");
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.onnx")
                .Where(f => File.Exists(f + ".json"))
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .SelectMany(name => ChosenSpeakers.TryGetValue(name, out var speakers) ? speakers.Select(s => $"{name}#{s}") : [name])
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
    }

    /// <summary>Die Stimme aus den Einstellungen, sonst Thorsten, sonst die erste vorhandene.</summary>
    public string ChooseVoice(string? configured)
    {
        var voices = Voices();
        return configured is { Length: > 0 } && voices.Contains(configured) ? configured
            : voices.Contains(DefaultVoice) ? DefaultVoice
            : voices.FirstOrDefault() ?? configured ?? DefaultVoice;
    }

    /// <summary>Was fehlt noch? Null, wenn alles da ist. Sonst ein Satz für die Oberfläche.</summary>
    public string? Problem(VoiceSettings settings)
    {
        if (!File.Exists(PythonExe) || !File.Exists(WorkerScript))
        {
            return @"Die Sprache ist noch nicht eingerichtet. Führe einmal scripts\setup-voice.ps1 aus (lädt ca. 1.8 GB: Python-Pakete, Whisper und die Stimme).";
        }

        if (!File.Exists(Path.Combine(WhisperPath(settings.WhisperModel), "model.bin")))
        {
            return $@"Das Whisper-Modell ""{settings.WhisperModel}"" fehlt. Lade es mit: scripts\setup-voice.ps1 -Whisper {settings.WhisperModel}";
        }

        var voice = ChooseVoice(settings.Voice);
        return File.Exists(VoicePath(voice)) ? null
            : $@"Die Stimme ""{voice}"" fehlt. Lade sie mit: scripts\setup-voice.ps1 -Voice {voice}";
    }
}
