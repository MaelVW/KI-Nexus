namespace KiNexus.Core.Chats;

public enum TerminalCommandKind
{
    /// <summary>Kein Befehl, sondern eine normale Frage an die KI.</summary>
    None,
    Help,
    ThinkOn,
    ThinkOff,
    ThinkShow,
    ThinkHide,
    SelectKi,
    Style,
    NewChat,
    Clear,
    Stop,
    Stats,
    Unload,

    /// <summary>Beginnt mit "/", ist aber kein bekannter Befehl oder hat falsche Angaben.</summary>
    Invalid,
}

/// <summary>Ein erkannter Befehl. Argument ist z.B. der KI-Name bei /ki oder der Stil bei /style.</summary>
public sealed record TerminalCommand(TerminalCommandKind Kind, string Argument = "", string Error = "");

/// <summary>
/// Die Befehle des Terminal-Stils (Bauplan, Kapitel 8.2).
/// Es werden nie echte Shell-Befehle ausgeführt. Alles, was nicht mit "/" beginnt, geht als Frage an die KI.
/// </summary>
public static class TerminalCommands
{
    public static IReadOnlyList<(string Usage, string Description)> Help { get; } =
    [
        ("/help", "Alle Befehle anzeigen"),
        ("/think on", "Nachdenken einschalten"),
        ("/think off", "Nachdenken ausschalten"),
        ("/think show", "Gedanken anzeigen"),
        ("/think hide", "Gedanken verstecken"),
        ("/ki <name>", "Andere KI wählen"),
        ("/style simpel", "Zum Simpel-Stil wechseln"),
        ("/style gespraech", "Zum Gespräch-Stil wechseln"),
        ("/new", "Neuer Chat"),
        ("/clear", "Bildschirm leeren (der Verlauf bleibt)"),
        ("/stop", "Antwort abbrechen (auch Strg + C)"),
        ("/stats", "Token, Tempo und Kontext anzeigen"),
        ("/unload", "Modell entladen"),
    ];

    /// <summary>Alle Wörter, die die Tab-Taste vervollständigen kann.</summary>
    private static readonly string[] Completions =
    [
        "/help", "/think on", "/think off", "/think show", "/think hide", "/ki ",
        "/style simpel", "/style gespraech", "/new", "/clear", "/stop", "/stats", "/unload",
    ];

    public static TerminalCommand Parse(string? input)
    {
        var text = input?.Trim() ?? "";
        if (!text.StartsWith('/'))
        {
            return new TerminalCommand(TerminalCommandKind.None);
        }

        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var name = parts[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1] : "";

        return name switch
        {
            "/help" or "/?" => new(TerminalCommandKind.Help),
            "/think" => ParseThink(argument),
            "/ki" => argument.Length > 0
                ? new(TerminalCommandKind.SelectKi, argument)
                : Invalid("Welche KI? Beispiel: /ki Mael-KI"),
            "/style" => ParseStyle(argument),
            "/new" => new(TerminalCommandKind.NewChat),
            "/clear" or "/cls" => new(TerminalCommandKind.Clear),
            "/stop" => new(TerminalCommandKind.Stop),
            "/stats" => new(TerminalCommandKind.Stats),
            "/unload" => new(TerminalCommandKind.Unload),
            _ => Invalid($"Unbekannter Befehl: {parts[0]}. Mit /help siehst du alle Befehle."),
        };
    }

    /// <summary>Alle Befehle, die mit dem bisher Getippten beginnen (für die Tab-Taste).</summary>
    public static IReadOnlyList<string> CompletionsFor(string? input)
    {
        var text = input ?? "";
        if (!text.StartsWith('/'))
        {
            return [];
        }

        return Completions.Where(c => c.StartsWith(text, StringComparison.OrdinalIgnoreCase) && c.Length > text.Length).ToList();
    }

    private static TerminalCommand ParseThink(string argument) => argument.ToLowerInvariant() switch
    {
        "on" or "an" => new(TerminalCommandKind.ThinkOn),
        "off" or "aus" => new(TerminalCommandKind.ThinkOff),
        "show" or "zeigen" => new(TerminalCommandKind.ThinkShow),
        "hide" or "verstecken" => new(TerminalCommandKind.ThinkHide),
        _ => Invalid("Erlaubt sind: /think on, /think off, /think show, /think hide"),
    };

    private static TerminalCommand ParseStyle(string argument) => argument.ToLowerInvariant() switch
    {
        "simpel" or "simple" => new(TerminalCommandKind.Style, "simple"),
        "gespraech" or "gespräch" or "conversation" => new(TerminalCommandKind.Style, "conversation"),
        "terminal" => new(TerminalCommandKind.Style, "terminal"),
        _ => Invalid("Erlaubt sind: /style simpel, /style gespraech"),
    };

    private static TerminalCommand Invalid(string error) => new(TerminalCommandKind.Invalid, Error: error);
}
