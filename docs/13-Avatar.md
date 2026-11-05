# 13 – Avatar (Erweiterung E3)

Stand: 5. November 2026. Plan: `KI-Nexus-Erweiterung-Avatar.md` (im Ordner `KI-Nexus-Avatar-Testpanel`). Die Anleitung nennt die Datei `docs/12-Avatar.md`, die Nummer 12 war schon an „Updates“ vergeben, darum 13.

| Phase | Inhalt | Status |
|---|---|---|
| E3.0 | Testpanel übernehmen (`src/KiNexus.App/Avatar/web/`), in WebView2 mit `?nurAvatar` | ✅ gebaut |
| E3.1 | WebView2-Feld, lokale Dateien, Sicherheit, VRM laden, Leerlauf (Blinzeln, Atmen aus dem Testpanel) | ✅ gebaut, Abnahme in der App offen |
| E3.2 | Lippen und Zustände aus dem Gesprächsmodus | ✅ gebaut, Abnahme mit echter Stimme offen |
| E3.3–E3.7 | Gefühle, Create-Hub, Live2D, Gesten, Webcam | offen |

## Aufbau

- **Web-Code:** unverändert aus dem Avatar-Testpanel 0.2 (`index.html`, `css`, `js` (fertige Bündel), `vendor` (MediaPipe), `src` (Quellen zum Neubündeln mit `build.cjs`)). Neu ist nur eine strenge **Content-Security-Policy** in `index.html`.
- **Live2D Cubism Core** ist **nicht** im Repository (Lizenz von Live2D, das Repository ist öffentlich). `src/KiNexus.App/Avatar/web/live2d/live2dcubismcore*.js` steht in `.gitignore`. Thema von E3.5.
- **`AvatarView`** (`src/KiNexus.App/Avatar/AvatarView.cs`): WPF-Steuerelement mit WebView2 (`Microsoft.Web.WebView2` 1.0.4258.31).
  - `https://avatar.kinexus/` → die Web-Dateien neben der App, `https://modelle.kinexus/` → der Ordner des gewählten Modells (`SetVirtualHostNameToFolderMapping`).
  - **Sicherheit:** Jede Anfrage an eine andere Adresse wird mit 403 blockiert (`WebResourceRequested`), Navigation nur innerhalb von `avatar.kinexus`, keine neuen Fenster, keine DevTools, kein Kontextmenü, alle Berechtigungen (Kamera, Mikrofon) abgelehnt (die Webcam kommt erst mit E3.7 und einem Schalter).
  - Befehle als JSON (`PostWebMessageAsJson`), genau wie im Testpanel: `loadUrl`, `setState`, `setLipSyncMode` (`aus`), `setMouth`, `unload`.
- **KI-Profil:** neues Feld `avatarPath` (VRM, relativ zum Projektordner oder voll). Im Gesprächsmodus: „Avatar wählen …“ und „Ohne Avatar“.
- **Gesprächsmodus:** Hat die KI einen Avatar, steht er gross in der Mitte, der Orb bleibt klein darunter. Grund: Über ein WebView2 kann WPF nichts legen („Airspace“), also kann man nicht auf den Avatar klicken. Der kleine Orb funktioniert wie bisher (Klick, Push-to-Talk).

## Zustände und Lippen

| Gesprächsmodus | Avatar |
|---|---|
| Zuhören, Hören | `listening` |
| Verstehen, Denken | `thinking` |
| Sprechen | `speaking` |
| Aus, Starten, Bereit, Fehler | `idle` |

- **Mund:** Beim Sprechen sendet die App höchstens 20 Mal pro Sekunde `setMouth` aus der Lautstärke der Wiedergabe (dieselbe, die den Orb wachsen lässt): unter 0.03 zu, ab ca. 0.5 ganz offen. Nur bei einer Änderung ab 0.03. Danach und in allen anderen Zuständen: Mund zu.
- Genauere Mundformen aus den Lauten von Piper (Bauplan-Frage 6) sind noch nicht eingebaut.

## Test-Modell

`models\avatar\Seed-san.vrm` (VRM-Konsortium / VirtualCast, VRM Public License 1.0, 10.9 MB), nur zum Testen und nicht im Repository.

## Noch offen / nicht geprüft

- Abnahme in der App: VRM erscheint im Gesprächsmodus und blinzelt, ohne Internet.
- Mund beim echten Sprechen und die Verzögerung zwischen Stimme und Mund.
- VRAM und CPU zusammen mit gemma4:26b.
- E3.3 (Gefühle), E3.4 (Create-Hub: Avatare), E3.5 (Live2D, braucht dein Okay zur Lizenz der Cubism Core), E3.6, E3.7 (Webcam).
