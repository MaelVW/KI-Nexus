using System.Globalization;

namespace KiNexus.Engines.Video;

/// <summary>Wo ComfyUI liegt: engines/comfyui mit eigener Python-Umgebung und dem Zusatz ComfyUI-GGUF.</summary>
public sealed record ComfyInstallation(string Directory, string PythonExe)
{
    public static ComfyInstallation For(string enginesDirectory)
    {
        var directory = Path.Combine(enginesDirectory, "comfyui");
        return new ComfyInstallation(directory, Path.Combine(directory, ".venv", "Scripts", "python.exe"));
    }

    public string MainScript => Path.Combine(Directory, "main.py");

    /// <summary>Sagt ComfyUI, wo die Modelle liegen. Schreibt die App bei jedem Start neu.</summary>
    public string PathsFile => Path.Combine(Directory, "kinexus-paths.yaml");

    /// <summary>Was fehlt noch? Null, wenn alles da ist.</summary>
    public string? Problem()
    {
        if (!File.Exists(MainScript) || !File.Exists(PythonExe))
        {
            return @"Video ist noch nicht eingerichtet. Führe einmal scripts\setup-video.ps1 aus (ComfyUI mit Python-Umgebung, ca. 1 GB).";
        }

        return File.Exists(Path.Combine(Directory, "custom_nodes", "ComfyUI-GGUF", "nodes.py"))
            ? null
            : @"Der Zusatz ComfyUI-GGUF fehlt. Führe scripts\setup-video.ps1 nochmals aus.";
    }
}

/// <param name="VideoModelsDirectory">models/video mit den Unterordnern diffusion_models, text_encoders und vae.</param>
/// <param name="OutputDirectory">Hier speichert ComfyUI die fertigen Videos (data/videos/roh).</param>
/// <param name="WorkDirectory">Eingabe-, Temp- und Benutzer-Ordner von ComfyUI (data/comfyui).</param>
/// <param name="LogFile">Ausgabe von ComfyUI (data/logs/video.log).</param>
/// <param name="CacheNone">
/// ComfyUI hält nichts zwischen den Schritten fest. So gibt es den Text-Encoder frei, bevor das Videomodell lädt.
/// In V0 gemessen: weniger Auslagerung (3 statt 9 GB) und schneller, aber jedes Video lädt die Modelle neu.
/// </param>
public sealed record ComfyOptions(
    string VideoModelsDirectory,
    string OutputDirectory,
    string WorkDirectory,
    string LogFile,
    int Port = 8188,
    bool CacheNone = true);

/// <summary>Baut den Startbefehl für ComfyUI. Eigene Klasse, damit man sie ohne Prozess testen kann.</summary>
public static class ComfyCommand
{
    /// <summary>
    /// Nur auf 127.0.0.1, ohne Browser. --offline sperrt die Cloud-Knoten (bezahlte Dienste) und das Internet der Oberfläche.
    /// -s: keine Python-Pakete aus dem Benutzerordner, nur die eigene Umgebung.
    /// </summary>
    public static IReadOnlyList<string> Arguments(ComfyInstallation installation, ComfyOptions options)
    {
        List<string> arguments =
        [
            "-s", installation.MainScript,
            "--listen", "127.0.0.1",
            "--port", options.Port.ToString(CultureInfo.InvariantCulture),
            "--disable-auto-launch",
            "--offline",
            "--extra-model-paths-config", installation.PathsFile,
            "--output-directory", options.OutputDirectory,
            "--input-directory", Path.Combine(options.WorkDirectory, "input"),
            "--temp-directory", Path.Combine(options.WorkDirectory, "temp"),
            "--user-directory", Path.Combine(options.WorkDirectory, "user"),
        ];
        if (options.CacheNone)
        {
            arguments.Add("--cache-none");
        }

        return arguments;
    }

    /// <summary>
    /// Inhalt von kinexus-paths.yaml: Die Modelle bleiben in models/video, ComfyUI bekommt nur die Pfade.
    /// Der Loader für die Audio-VAE von LTX sucht im Ordner "checkpoints", darum zeigt der auch auf vae.
    /// </summary>
    public static string PathsYaml(string videoModelsDirectory) => $"""
        # Von KI-Nexus geschrieben (bei jedem Start von ComfyUI). Hier sucht ComfyUI die Modelle.
        kinexus:
          base_path: "{videoModelsDirectory.Replace('\\', '/')}"
          is_default: true
          diffusion_models: diffusion_models
          text_encoders: text_encoders
          vae: vae
          checkpoints: vae
          latent_upscale_models: latent_upscale_models
          loras: loras

        """;
}
