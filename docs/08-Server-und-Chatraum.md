# 08 – Server und Chatraum

Zwei Pläne für später: ein Schalter **Lokal | Server** (M9) und ein **Chatraum** mit mehreren KIs (M10).
Beide hängen von stärkerer Hardware ab. Die Architektur ist aber schon darauf vorbereitet.

## Server (M9)

- Der Server ist einfach ein weiteres Backend (`RemoteBackend`) mit einer anderen Adresse.
- Er spricht die Ollama-API oder die OpenAI-kompatible API.
- Verbindungstest mit klarer Meldung, z.B. „Server nicht erreichbar. Ist das VPN an?“
- Der aktuelle Unraid-Server (AMD Opteron X3216, 2 Kerne) ist zu schwach, um selbst Modelle zu rechnen.
- In M0 ist der Schalter unsichtbar.

## Multi-KI-Chatraum (M10)

- Zwei oder mehr KIs mit Rollen (z.B. „Lösungsfinder“ und „Kritiker“) arbeiten Runde für Runde.
- Abbruch: maximale Rundenzahl, Einigung oder Stopp-Knopf. Du kannst jederzeit eingreifen.
- Eigene Komponente `Orchestrator`, redet nur über `IInferenceBackend` mit den Modellen.
- Tipp: Zwei KIs mit demselben Grundmodell brauchen kein Neuladen.
