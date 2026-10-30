namespace KiNexus.Core.Settings;

public interface ISettingsService
{
    /// <summary>Die aktiven Einstellungen. Nie null, nach dem Start sind es mindestens die Standards.</summary>
    AppSettings Current { get; }

    /// <summary>Wird ausgelöst, nachdem neue Einstellungen gespeichert wurden.</summary>
    event EventHandler<AppSettings>? Saved;

    Task<AppSettings> LoadAsync(CancellationToken ct);

    /// <summary>Übernimmt die Einstellungen als aktiv und speichert sie.</summary>
    Task SaveAsync(AppSettings settings, CancellationToken ct);
}
