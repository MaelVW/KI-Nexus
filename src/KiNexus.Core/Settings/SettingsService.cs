using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KiNexus.Core.Settings;

/// <summary>
/// Lädt und speichert data/settings.json.
/// Fehlt die Datei, gelten die Standards. Ist sie kaputt, wird sie gesichert und ersetzt.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly string _filePath;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public SettingsService(string filePath, ILogger<SettingsService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
        _logger = logger;
    }

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? Saved;

    public async Task<AppSettings> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            _logger.LogInformation("Keine Einstellungen gefunden, nutze Standardwerte: {Path}", _filePath);
            Current = new AppSettings();
            return Current;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonDefaults.Options, ct).ConfigureAwait(false);
            Current = loaded ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Einstellungen sind beschädigt. Die Datei wird gesichert, es gelten die Standardwerte.");
            BackupBrokenFile();
            Current = new AppSettings();
            return Current;
        }

        foreach (var fix in SettingsValidator.Normalize(Current))
        {
            _logger.LogWarning("Einstellung korrigiert: {Fix}", fix);
        }

        _logger.LogInformation("Einstellungen geladen: {Path}", _filePath);
        return Current;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SettingsValidator.Normalize(settings);

        await _saveLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WriteAtomicallyAsync(settings, ct).ConfigureAwait(false);
            Current = settings;
        }
        finally
        {
            _saveLock.Release();
        }

        _logger.LogInformation("Einstellungen gespeichert.");
        Saved?.Invoke(this, settings);
    }

    /// <summary>
    /// Schreibt zuerst in eine Temp-Datei und ersetzt dann die echte Datei.
    /// So ist settings.json nie halb geschrieben, auch wenn der Strom ausfällt.
    /// </summary>
    private async Task WriteAtomicallyAsync(AppSettings settings, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var tempPath = _filePath + ".tmp";

        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonDefaults.Options, ct).ConfigureAwait(false);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    private void BackupBrokenFile()
    {
        var backupPath = $"{_filePath}.kaputt-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            File.Move(_filePath, backupPath, overwrite: true);
            _logger.LogWarning("Beschädigte Einstellungen gesichert als {Backup}", backupPath);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Beschädigte Einstellungen konnten nicht gesichert werden.");
        }
    }
}
