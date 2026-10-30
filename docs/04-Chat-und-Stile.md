# 04 – Chat und Stile

Ein Chat hat drei Ansichten: Terminal, Simpel und Gespräch.
Du kannst mitten im Chat den Stil wechseln, der Verlauf bleibt gleich.
Diese Datei wird in **M2** (Terminal, Simpel) und **M4** (Gespräch) gefüllt.

## Stand M1

- Der Stil-Umschalter **Terminal | Simpel | Gespräch** ist in der Kopfleiste. Er ist nur im Chat-Tab sichtbar.
- In den Stilen Terminal und Simpel zeigt der Chat-Tab vorerst das **Test-Feld**: eine Frage, ohne Verlauf, mit gestreamter Antwort, Thinking an/aus, Gedanken zeigen/verstecken, Stopp-Knopf und Statistik. M2 ersetzt es durch den richtigen Chat.
- Der Stil Gespräch zeigt „Kommt in Meilenstein 4“.
- Mit welchem Stil die App startet, stellst du unter Zahnrad → Darstellung → Standard-Stil ein.

Die Statistik im Test-Feld zeigt zum Beispiel:
`46 Token · 38.0 Token/s · Frage gelesen: 108 Token (226 Token/s) · 1.8 s · llama.cpp`
„Frage gelesen“ enthält den Systemprompt. Es geht nur Systemprompt und Frage ans Modell, nichts Verstecktes.

## Plan

| Stil | Meilenstein | Kurz |
|---|---|---|
| Simpel | M2 | Blasen wie ChatGPT, Markdown, Code mit Farben, Bilder anhängen |
| Terminal | M2 | Sieht aus wie ein Terminal, Befehle wie `/help`. Keine echten Shell-Befehle. |
| Gespräch | M4 | Sprechen und Zuhören, mit Orb und Mitschrift |

### Terminal-Befehle (M2)

| Befehl | Wirkung |
|---|---|
| `/help` | Alle Befehle anzeigen |
| `/think on` / `/think off` | Nachdenken ein- oder ausschalten |
| `/think show` / `/think hide` | Gedanken anzeigen oder verstecken |
| `/ki <name>` | Andere KI wählen |
| `/style simpel` / `/style gespraech` | Stil wechseln |
| `/new` | Neuer Chat |
| `/clear` | Bildschirm leeren (Verlauf bleibt) |
| `/stop` | Antwort abbrechen (auch `Strg + C`) |
| `/stats` | Token, Tempo und Kontext anzeigen |
| `/unload` | Modell entladen |

### Chat-Datenbank (`data/chats.db`, M2)

Tabellen `chats`, `messages` und `system_prompt_versions` (siehe Bauplan, Kapitel 9). Mit Versionsnummer für spätere Migrationen.
