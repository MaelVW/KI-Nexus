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

    public static VoiceInstallation For(AppPaths paths, string modelsDirectory) => new(
        Path.Combine(paths.Root, "python", ".venv", "Scripts", "python.exe"),
        Path.Combine(paths.Root, "python", "voice", "worker.py"),
        Path.Combine(modelsDirectory, "voice"));

    public string WhisperPath(string model) => Path.Combine(VoiceModelsDirectory, "whisper-" + model);

    public string VoicePath(string voice) => Path.Combine(VoiceModelsDirectory, "piper", voice + ".onnx");

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
