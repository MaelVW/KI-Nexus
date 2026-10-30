# Changelog

Alle wichtigen Änderungen an KI-Nexus stehen hier.
Format nach [Keep a Changelog](https://keepachangelog.com/de/1.1.0/), Versionen nach [Semantic Versioning](https://semver.org/lang/de/).
Version 1.0.0 = Meilensteine M0 bis M4.

## [Unveröffentlicht]

## [0.1.0] – 2026-10-30 – Meilenstein 0: Gerüst

### Neu

- Solution `KI-Nexus.sln` mit den Projekten `KiNexus.App` (WPF), `KiNexus.Core`, `KiNexus.Engines` und zwei Testprojekten.
- Ordnerstruktur nach Bauplan, `.gitignore` (ohne `models/`, `data/`, `engines/`, `python/.venv/`).
- Theme dunkel mit Cyan: Farben, Schriften, Stile für Knöpfe, Felder, Auswahllisten, Schalter, Tabs, Bildlaufleisten und Hinweise.
- Hauptfenster mit eigener, dunkler Titelleiste und den Tabs Chat, Feineinstellungen, Training, System-Prompt und Create-Hub. Jeder Tab zeigt „Kommt in Meilenstein …“.
- Kopfleiste mit KI-Auswahl, Stil-Umschalter (nur im Chat-Tab), Platz für die Ressourcen-Anzeige, Knopf „Entladen“ und Zahnrad. KI-Auswahl, Ressourcen und „Entladen“ sind noch ohne Funktion (M1).
- Einstellungs-Dialog: Akzentfarbe (wirkt sofort), Standard-Stil, Engine-Reihenfolge, Ordner, Ports, Titel-Erzeugung, Open-WebUI-Optionen. Mit Prüfung der Eingaben.
- Einstellungen in `data/settings.json`: sicheres Speichern, Standardwerte, Korrektur ungültiger Werte, Sicherung einer kaputten Datei.
- Fenstergrösse, Lage und letzter Tab bleiben nach dem Neustart erhalten.
- Logging mit Serilog nach `data/logs/` (eine Datei pro Tag, 14 Tage).
- Tastenkürzel `Strg + 1` bis `5` (Tabs) und `Strg + ,` (Einstellungen).
- `Start-KI-Nexus.bat`: baut bei Bedarf und startet die App per Doppelklick.
- App-Icon.
- Doku-Gerüst in `docs/` (00 bis 09).
- 39 Unit-Tests für Pfade, Einstellungen, Farben, Fensterlage und Ports.
