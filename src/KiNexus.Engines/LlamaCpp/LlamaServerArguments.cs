using System.Globalization;
using KiNexus.Core.Inference;
using KiNexus.Core.Kis;
using KiNexus.Core.Paths;

namespace KiNexus.Engines.LlamaCpp;

/// <summary>
/// Baut die Befehlszeile für llama-server aus einem KI-Profil.
/// Werte und Messungen dazu: docs/02-Engines.md.
/// </summary>
public static class LlamaServerArguments
{
    public static IReadOnlyList<string> Build(KiProfile ki, AppPaths paths, int port, string logFile)
    {
        if (ki.Engine != EngineKind.LlamaCpp)
        {
            throw new ArgumentException("Nur für llama.cpp-KIs.", nameof(ki));
        }

        if (string.IsNullOrWhiteSpace(ki.BaseModel.Path))
        {
            throw new InferenceException($"Bei der KI \"{ki.DisplayName}\" fehlt der Pfad zum Modell (baseModel.path).");
        }

        var args = new List<string>
        {
            "-m", paths.Resolve(ki.BaseModel.Path),
            // Nur auf diesem Laptop erreichbar (Bauplan, Kapitel 5).
            "--host", "127.0.0.1",
            "--port", port.ToString(CultureInfo.InvariantCulture),
            "-c", ki.Parameters.ContextLength.ToString(CultureInfo.InvariantCulture),
            // Ein einziger Platz für Anfragen: Wir stellen nie zwei Fragen gleichzeitig.
            "-np", "1",
            // Modell ganz einlesen statt einblenden (mmap): weniger RAM, schneller (siehe Messungen).
            "-lm", "none",
            // Keine eigene Web-Oberfläche und kein Internet.
            "--no-webui",
            "--offline",
            "-a", ki.Id,
            "--log-file", logFile,
        };

        if (!string.IsNullOrWhiteSpace(ki.BaseModel.MmprojPath))
        {
            args.AddRange(["--mmproj", paths.Resolve(ki.BaseModel.MmprojPath)]);
        }

        // "auto": llama.cpp verteilt das Modell selbst auf VRAM und RAM (--fit).
        if (!string.Equals(ki.EngineOptions.GpuLayers, "auto", StringComparison.OrdinalIgnoreCase))
        {
            args.AddRange(["-ngl", ki.EngineOptions.GpuLayers]);
        }

        if (ki.EngineOptions.CpuMoeLayers is int cpuMoe)
        {
            args.AddRange(["--n-cpu-moe", cpuMoe.ToString(CultureInfo.InvariantCulture)]);
        }

        // Draft (spekulatives Dekodieren): Messung in docs/02-Engines.md. Braucht ca. 0.5 GB mehr VRAM.
        if (ki.EngineOptions.UseDraftModel && !string.IsNullOrWhiteSpace(ki.BaseModel.DraftPath))
        {
            args.AddRange(["-md", paths.Resolve(ki.BaseModel.DraftPath), "--spec-type", "draft-mtp"]);
        }

        if (!string.IsNullOrWhiteSpace(ki.LoraAdapterPath))
        {
            args.AddRange(["--lora", paths.Resolve(ki.LoraAdapterPath)]);
        }

        return args;
    }
}
