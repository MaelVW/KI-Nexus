using System.Diagnostics;
using System.Text.Json;

namespace KiNexus.Engines.Video;

/// <summary>
/// Setzt die Clips eines Szenen-Projekts zu einem Video zusammen (python/video/szenen_zusammen.py mit PyAV aus der Umgebung von ComfyUI).
/// </summary>
public static class SceneComposer
{
    /// <summary>Gibt die Länge in Sekunden zurück. Wirft <see cref="InvalidOperationException"/> mit dem Grund, wenn es nicht geht.</summary>
    public static async Task<double> ComposeAsync(ComfyInstallation installation, string script, IReadOnlyList<string> clips, string output, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(installation);
        var info = new ProcessStartInfo(installation.PythonExe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-s", script, "--out", output }.Concat(clips))
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["PYTHONIOENCODING"] = "utf-8";
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Python liess sich nicht starten.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var last = (await stdout.ConfigureAwait(false)).Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        try
        {
            using var json = JsonDocument.Parse(last);
            if (json.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                return json.RootElement.GetProperty("seconds").GetDouble();
            }

            throw new InvalidOperationException("Zusammensetzen fehlgeschlagen: " + json.RootElement.GetProperty("message").GetString());
        }
        catch (JsonException)
        {
            var error = (await stderr.ConfigureAwait(false)).Trim();
            throw new InvalidOperationException("Zusammensetzen fehlgeschlagen: " + (error.Length > 300 ? error[^300..] : error));
        }
    }
}
