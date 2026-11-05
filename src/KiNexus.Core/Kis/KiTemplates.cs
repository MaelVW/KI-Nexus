namespace KiNexus.Core.Kis;

/// <summary>Eine Vorlage für den Systemprompt (Assistent "Neue KI", Schritt 3, und System-Prompt-Tab).</summary>
/// <param name="IsCustom">Eine eigene Vorlage (data/prompt-vorlagen.json), keine eingebaute. Nur eigene kann man löschen.</param>
public sealed record PromptTemplate(string Name, string Text, bool IsCustom = false)
{
    public override string ToString() => Name;
}

/// <summary>Eine Vorlage für die Parameter (Assistent "Neue KI", Schritt 4).</summary>
public sealed record ParameterPreset(string Name, string Description, double? Temperature, double? TopP, int? TopK)
{
    /// <summary>Der Name, z.B. für Screenreader in der Auswahlliste.</summary>
    public override string ToString() => Name;
}

/// <summary>Vorlagen für neue KIs. Alle antworten auf Deutsch mit Schweizer Rechtschreibung.</summary>
public static class KiTemplates
{
    private const string SwissRules =
        "- Antworte immer auf Deutsch mit Schweizer Rechtschreibung. Schreibe nie \"ß\", sondern immer \"ss\".\n" +
        "- Wenn du dir bei einem Fakt nicht sicher bist, sag das klar.\n" +
        "- Erfinde keine Namen, Zahlen oder Quellen.";

    public static IReadOnlyList<PromptTemplate> Prompts { get; } =
    [
        new("Allgemein", "Du bist ein hilfsbereiter Assistent.\n- Schreibe einfache, kurze Sätze.\n" + SwissRules),
        new("Schule",
            "Du bist ein geduldiger Lernbegleiter für einen Informatik-Lernenden an der Berufsfachschule.\n" +
            "- Erkläre Schritt für Schritt und mit einfachen Beispielen.\n" +
            "- Fachbegriffe erklärst du kurz in Klammern.\n" +
            "- Bei Aufträgen gibst du Hinweise und Fragen, keine fertige Lösung.\n" + SwissRules),
        new("Programmieren",
            "Du bist ein erfahrener C#- und .NET-Entwickler.\n" +
            "- Zeige Code in Codeblöcken mit Sprache (z.B. ```csharp).\n" +
            "- Code-Namen auf Englisch, Kommentare und Erklärungen auf Deutsch.\n" +
            "- Erkläre kurz, warum der Code so funktioniert.\n" + SwissRules),
        new("Kurz und knapp",
            "Du antwortest so kurz wie möglich.\n" +
            "- Höchstens drei Sätze, ausser ich frage ausdrücklich nach mehr.\n" +
            "- Keine Einleitung, keine Wiederholung der Frage.\n" + SwissRules),
    ];

    /// <summary>"Standard" übernimmt die Werte des Herstellers (aus der GGUF-Datei). Null = Standard der Engine.</summary>
    public static IReadOnlyList<ParameterPreset> Parameters { get; } =
    [
        new("Standard", "Empfehlung des Modell-Herstellers (aus der GGUF-Datei).", null, null, null),
        new("Genau", "Wenig Zufall. Gut für Fakten, Mathe und Code.", 0.3, 0.9, 40),
        new("Kreativ", "Mehr Abwechslung. Gut für Geschichten und Ideen.", 1.1, 0.95, 64),
    ];

    /// <summary>Kontextlängen zur Auswahl (Gedächtnis des Modells, mehr braucht mehr Speicher).</summary>
    public static IReadOnlyList<int> ContextLengths { get; } = [4096, 8192, 16384, 32768];
}
