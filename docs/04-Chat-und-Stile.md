# 04 – Chat und Stile

Ein Chat hat drei Ansichten: Terminal, Simpel und Gespräch.
Du kannst mitten im Chat den Stil wechseln, der Verlauf bleibt gleich. Beide Stile zeigen nämlich denselben Chat.
Alle Chats liegen in `data/chats.db`. Gespräch (mit Stimme) kommt in Meilenstein 4.

## Für alle Stile gleich

| Was | Wie |
|---|---|
| Stil wechseln | Kopfleiste: Terminal / Simpel / Gespräch. Im Terminal auch `/style simpel`. |
| Chats | Seitenleiste links: neu (`Strg + N`), umbenennen (Stift oder `F2`), löschen (Papierkorb oder `Entf`, mit Rückfrage), suchen. Einklappbar. |
| Suche | findet den Titel und den Text aller Nachrichten |
| Thinking | pro Chat ein- und ausschaltbar (Schalter unten, im Terminal `/think on` / `/think off`) |
| Gedanken anzeigen | für alle Chats (Schalter unten, im Terminal `/think show` / `/think hide`) |
| Abbrechen | Stopp-Knopf oder `Esc` (Simpel), `Strg + C` oder `/stop` (Terminal). Der fertige Teil der Antwort bleibt gespeichert. |
| Statistik | Info-Knopf unter jeder Antwort (Simpel) oder `/stats` (Terminal): Token, Tempo, Dauer, Engine, KI |
| Kontext-Anzeige | unten rechts, z.B. `243 / 8192 Token` |
| Bewertung | Daumen hoch oder runter unter einer Antwort. Nochmals klicken nimmt sie zurück. Gespeichert für den Training-Tab (M8). |
| Chat-Titel | erzeugt die App nach der ersten Antwort (siehe unten) |
| Beim Start | öffnet die App den zuletzt benutzten Chat. Öffnest du einen Chat, wechselt die KI-Auswahl zur KI dieses Chats. |

### Was ans Modell geht

Nur Systemprompt, Verlauf und deine neue Nachricht. Nichts Verstecktes (Lektion 5 im Bauplan).
Die Gedanken früherer Antworten gehen **nicht** mit, nur der eigentliche Text.

### Kontext (Gedächtnis des Modells)

- Die Anzeige zeigt nach jeder Antwort die echte Zahl der Engine: alles, was danach im Kontext steht.
- Vor dem Senden schätzt die App, ob alles passt. Für die Antwort hält sie Platz frei (die maximale Antwortlänge, höchstens den halben Kontext).
- Passt der Verlauf nicht mehr, fallen die **ältesten** Nachrichten weg. Der Systemprompt und die neue Frage bleiben immer drin. Ein gelber Hinweis sagt, wie viele Nachrichten weggelassen wurden. In der Datenbank bleiben sie erhalten.
- Die Schätzung: ca. 3.2 Zeichen pro Token, 8 Token pro Nachricht, 600 Token pro Bild. Nach jeder Antwort lernt die App aus der echten Zahl, wie viele Zeichen ein Token in diesem Chat hat.

### Chat-Titel

- Nach der ersten Antwort fragt die App das Modell nach einem Titel mit höchstens 5 Wörtern. Ohne Thinking, ohne Systemprompt, höchstens 24 Token.
- Niedrige Priorität: Stellst du währenddessen eine neue Frage, bricht die App die Titel-Erzeugung ab (Lektion 6). Der Titel kommt dann nach der nächsten Antwort, wieder aus der **ersten** Frage.
- Aus „ß“ wird „ss“.
- Ausschalten: Zahnrad → Hintergrund-Aufgaben. Dann nimmt die App die ersten 6 Wörter deiner Frage.

## Simpel-Stil

- Deine Nachrichten stehen rechts in einer Blase, die Antworten links mit Markdown: fett, kursiv, Listen, Tabellen, Zitate, Links (öffnen den Browser) und Codeblöcke.
- Codeblöcke haben Syntax-Farben (AvalonEdit) und einen Kopier-Knopf. Während eine Antwort noch entsteht, sind Codeblöcke einfach umrahmt, damit das Streamen flüssig bleibt.
- Gedanken stehen über der Antwort als einklappbarer Block „Nachgedacht für X Sekunden“. Solange die KI denkt, ist er offen. Sobald die Antwort beginnt, klappt er zu.
- Knöpfe pro Antwort: kopieren, neu erzeugen (nur die letzte), Daumen hoch, Daumen runter, Info.
- Deine letzte Nachricht kannst du mit dem Stift bearbeiten. „Senden“ ersetzt dann die alte Frage und ihre Antwort. Angehängte Bilder bleiben dabei.
- `Enter` sendet, `Shift + Enter` macht eine neue Zeile, `Esc` bricht ab.

### Bilder anhängen

- Mit dem +-Knopf (Datei-Dialog) oder per Drag & Drop auf den Chat. Mehrere Bilder gehen.
- Formate: png, jpg, jpeg, gif, bmp, höchstens 20 MB pro Bild.
- Versteht die KI keine Bilder, ist der +-Knopf grau. Der Hinweis beim Darüberfahren sagt warum.
  - llama.cpp-KI: Bilder gehen, wenn ein Projektor (`mmprojPath`) da ist.
  - Ollama-KI: Die App fragt Ollama (`/api/show`), ob das Modell die Fähigkeit „vision“ hat.
- Die App kopiert jedes Bild nach `data/attachments/<chat-id>/`. Der Chat funktioniert also auch, wenn das Original weg ist. Beim Löschen des Chats werden die Bilder mit gelöscht.
- Bilder früherer Nachrichten gehen bei jeder Frage wieder mit (das Modell braucht sie für Folgefragen). Das kostet Kontext.

Getestet: Mael-KI (gemma4) beschreibt ein Testbild richtig (rotes Haus, braunes Dach, Baum, Sonne) und liest den Text darin vor.

## Terminal-Stil

- Sieht aus wie ein Terminal: dunkel, Cascadia Mono, Prompt `mael@nexus ›`, blinkender Cursor.
- **Es werden nie echte Shell-Befehle ausgeführt.** Alles, was nicht mit `/` beginnt, geht als Frage an die KI.
- Farben: deine Eingabe im Akzent (Cyan), Antwort hell, Gedanken grau und einklappbar, Systemmeldungen gelb, Fehler rot.
- Die Antwort erscheint Zeichen für Zeichen. Der Schreibmaschinen-Effekt folgt dem echten Streaming: Kommt viel Text auf einmal, tippt er schneller (er holt den Rückstand in ca. 60 ms auf).
- Markdown wird einfach dargestellt: fett, Listen, Codeblöcke mit Rahmen.
- Die Statuszeile unten zeigt KI, Engine, Thinking, Kontext und „antwortet …“.
- Tasten: `Enter` ausführen, `Pfeil hoch` / `Pfeil runter` frühere Eingaben, `Tab` vervollständigt Befehle (mehrmals Tab geht durch alle Treffer), `Strg + C` bricht ab (ist Text markiert, kopiert es wie gewohnt).

### Befehle

| Befehl | Wirkung |
|---|---|
| `/help` | Alle Befehle anzeigen |
| `/think on` / `/think off` | Nachdenken ein- oder ausschalten (für diesen Chat) |
| `/think show` / `/think hide` | Gedanken anzeigen oder verstecken |
| `/ki <name>` | Andere KI wählen. Der Anfang des Namens reicht, wenn er eindeutig ist. |
| `/style simpel` / `/style gespraech` | Stil wechseln |
| `/new` | Neuer Chat |
| `/clear` | Bildschirm leeren (der Verlauf bleibt) |
| `/stop` | Antwort abbrechen (auch `Strg + C`) |
| `/stats` | Token, Tempo und Kontext anzeigen |
| `/unload` | Modell entladen |

## Chat-Datenbank (`data/chats.db`)

SQLite, eine Datei. Die Version steht in der Datenbank (`PRAGMA user_version`, zurzeit 1). Neue App-Versionen bringen die Datenbank mit Migrationen auf den neuesten Stand, ohne Daten zu löschen.

| Tabelle | Spalten |
|---|---|
| `chats` | id, title, ki_id, created_at, updated_at, think_enabled, voice_mode |
| `messages` | id, chat_id, role, content, thinking, thinking_ms, ki_id, engine, attachment_paths, prompt_tokens, completion_tokens, tokens_per_second, duration_ms, context_tokens, rating, created_at |
| `system_prompt_versions` | id, ki_id, content, created_at (wird ab M6 genutzt) |

Gegenüber dem Bauplan dazugekommen: `thinking_ms` (für „Nachgedacht für X Sekunden“) und `context_tokens` (für die Kontext-Anzeige beim Öffnen eines Chats).

- Zeiten stehen in UTC. So stimmt die Sortierung auch beim Wechsel auf Sommer- oder Winterzeit.
- `attachment_paths` ist eine JSON-Liste mit vollen Pfaden.
- Löscht man einen Chat, löscht SQLite seine Nachrichten mit (`ON DELETE CASCADE`).
- Die Suche (`LIKE`) unterscheidet bei Umlauten Gross- und Kleinschreibung („Ä“ findet kein „ä“). Bei anderen Buchstaben nicht.
