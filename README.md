# FMH – Fußball-Manager im Textmodus

> „Ein Klub, eine Saison, noch eine Woche."

Ein Fußball-Manager ohne 3D-Darstellung: Spielgefühl und Humor wie Anstoß 3, Attributsystem und
Taktiktiefe wie Football Manager, der Spieltag läuft als Text-Kommentar oder Ticker. Grundlage ist das
Fachkonzept „Fußball-Manager (Textmodus)".

Dieser Stand setzt **Phase 1 (Prototyp)** der Roadmap um: Matchengine, Spielerattribute, eine
spielbare Liga (plus 2. Liga für Auf- und Abstieg), Kommentarmodus. Das Ergebnis ist eine spielbare
Saison ohne Wirtschaft, die sich über beliebig viele Saisons fortsetzen lässt.

## Schnellstart

Voraussetzung: [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build -c Release
dotnet run -c Release --project src/Fmh.Ui          # das Spiel (Terminal mit mind. 100 x 30 Zeichen)
dotnet test                                          # 45 Tests
dotnet run -c Release --project src/Fmh.Calibrate -- --matches 10000   # Engine-Kalibrierung
```

Spielstände landen unter `%APPDATA%/FMH/saves` bzw. `~/.config/FMH/saves`
(änderbar mit `--saves <pfad>` oder der Umgebungsvariable `FMH_SAVE_DIR`).

## Bedienung

| Taste | Wirkung |
|---|---|
| Leertaste | **Weiter** bis zum nächsten Ereignis (Spieltag, Nachricht, Saisonende) |
| F1–F10, Tab | Bereiche: Posteingang, Kader, Taktik, Training, Spielplan, Transfers, Finanzen, Stadion, Verein, Liga |
| Strg+S | Speichern |
| Esc | Menü (Speichern, Einstellungen, Hauptmenü) |
| Kader | ↑↓ Spieler, Enter Profil, S Sortierspalte, R Reihenfolge |
| Taktik | Enter Spieler wählen, E Aufstellung ↔ Anweisungen, ←→ Wert ändern, A automatisch, H Co-Trainer |
| Spiel | Leertaste/P Pause, W Wechsel, T Taktik, Z Zuruf, M Kommentar/Ticker, +/- Tempo, S sofort beenden |

Alles ist vollständig per Tastatur bedienbar. Die Farbcodierung der Attribute steht zusätzlich als
Symbol daneben (▼ 1–5, · 6–10, ▲ 11–15, ★ 16–20).

## Umfang nach Kapiteln des Fachkonzepts

| Kapitel | Umgesetzt in Phase 1 | Folgt laut Roadmap |
|---|---|---|
| 2 Spielmodi und Spielstart | Karriere mit Neuem Spiel (Name, Werdegang, Verein, Seed, Weltregeln), Managerprofil mit persönlichen Einstellungen (Gehaltsanzeige, Attributanzeige, Spieltag-Darstellung und Tempo, Pausen, Farbthema), Speichern, Autosave vor jedem Spieltag in 3 rotierenden Slots, optionaler Ironman | Schnellsaison, Hot-Seat, Herausforderungen (Phase 4) |
| 3 Spielwelt, Wettbewerbe | Tagesgenauer Kalender mit Stopp-Ereignissen, Ligen als **Daten** (Pyramide, Teams, Runden, Punkte, Tabellenkriterien inkl. direktem Vergleich, Auf-/Abstieg, Winterpause, Kader- und Disziplinregeln), Deutschland mit 1. und 2. Liga | Pokal, Relegation/Play-offs, Europapokal, weitere Länder, Länderkoeffizient |
| 4 Spielerdatenmodell | Alle 44 Attribute (1–20), versteckte Werte (Fähigkeit/Potenzial 1–200, Charakter), Kondition, Form, Moral (7 Stufen), Spielpraxis, Gesamtstärke 1,0–13,0, Positionseignung, Entwicklung zum Saisonende, Karriereende, Nachwuchs, Dauerschäden nach langen Verletzungen | Zufriedenheit, Tutor-System |
| 5 Kader, Taktik | Kaderliste (sortierbar), Spielerprofil, 16 Formationen auf dem 5x7-Raster, Aufstellung und Bank, Kapitän und Standardschützen, Mentalität (7 Stufen) und 11 Mannschaftsanweisungen, Co-Trainer-Feedback | Training, Rollen, Spieleranweisungen, Taktikvertrautheit, freie Positionierung |
| 6 Spieltag und Matchengine | Vorbericht (Gegner, Form, Schlüsselspieler, Wetter, Schiedsrichterstrenge), Aufstellungsprüfung, Kabinenansprache mit 6 Tönen (vor dem Spiel und zur Halbzeit), Kommentar-, Ticker- und Sofortmodus, 5 Tempostufen (ca. 8 Min. bis 20 Sek.), Pause bei Toren/Karten/Verletzungen, Wechsel, Taktikänderungen und Zurufe im Spiel, Nachbericht mit Noten 1,0–6,0, Spieler des Spiels, Statistik inkl. xG und Laufleistung, Text-Heatmap, andere Ergebnisse | Konferenz (Phase 3), Pressekonferenz |
| 7–10 Transfers, Finanzen, Gelände, Umfeld | Platzhalter-Bereiche im Menü | Phasen 2 und 3 |
| 11 Oberfläche | Grundlayout (Kopfzeile in Vereinsfarben, Menü, Hauptbereich, Statuszeile), Farbthemen Klassik/Hell/Terminal, farbcodierte Attribute | Maus, Spaltenlayouts, Tastenbelegung |
| 12 Nicht-funktional | Determinismus (eigener PRNG, gleicher Seed + Eingaben = gleiches Ergebnis), Spielstand-Versionierung mit Migration, alle Texte in Sprachdateien, Datenpakete mit Abhängigkeiten und Überlagerung, Validierung der Daten, Kalibrierungs-CLI | Englisch, Editor, Plugins, Update-Prüfung |

## Kalibrierung der Matchengine

`fmh-calibrate --matches 10000` (Seed 1), Zielwerte aus Kapitel 6:

| Kennzahl | Ist | Ziel |
|---|---|---|
| Tore pro Spiel | 2,82 | 2,8 |
| Unentschieden | 24,1 % | 25 % |
| Heimsiege | 44,0 % | 45 % |
| Gelbe Karten pro Spiel | 3,71 | 3,8 |
| Rote Karten pro Spiel | 0,19 | 0,2 |
| Verletzungen pro Team und Spiel | 0,29 | 0,3 |

Weitere Werte: 13,8 Schüsse pro Team, 9,3 Ecken, 22 Fouls, 4,7 Abseits, 0,31 Elfmeter, rund 111 km
Laufleistung pro Team. Leistung: ca. 2,5 ms pro Spiel; ein kompletter Spieltag beider Ligen braucht
höchstens 0,3–0,5 s (Ziel: unter 5 s).

Die Gewichte stehen in `data/base/rules/engine.json` und können per Datenpaket überschrieben werden.
`fmh-calibrate --engine meine.json` prüft alternative Gewichte, ohne das Paket zu ändern;
`--season --seasons 3` simuliert ganze Saisons und misst die Rechenzeit.

## Aufbau

```
src/
  Fmh.Core/          Kern: Datenmodell, Kalender und Tagesablauf (GameSession), Ligen und Tabellen,
                     Weltgenerator, Aufstellung/KI-Taktik, Entwicklung, Speichern, Datenpakete,
                     Schnittstelle IMatchEngine
  Fmh.MatchEngine/   Text-Matchengine (Phasen, 5x7-Zonen, Duelle) und Kommentargenerator
  Fmh.Ui/            Text-Oberfläche (eigener Zeichenpuffer, ANSI-Ausgabe), Programm "fmh"
  Fmh.Calibrate/     Kommandozeilen-Simulator "fmh-calibrate"
tests/Fmh.Tests/     xUnit-Tests für Kern, Engine, Spielstände, Daten, Oberfläche
data/base/           Datenpaket "base": Länder, Vereine, Namen, Formationen, Regeln, Sprachdateien
```

Die Module sprechen nur über definierte Schnittstellen miteinander: Die Oberfläche kennt die Engine
nur als `IMatchEngine`/`IMatchSimulation`; eine andere Engine lässt sich so einsetzen, ohne Kern
oder Oberfläche anzufassen.

## Datenpakete und Modding

Jedes Unterverzeichnis von `data/` mit einer `package.json` ist ein Paket. Pakete werden nach
`dependsOn` sortiert und übereinandergelegt: Eine Datei mit gleichem relativen Pfad ersetzt die
frühere, Sprachdateien werden schlüsselweise zusammengeführt. Beispiel für eigene Vereinsnamen:

```
data/meine-namen/package.json      { "id": "meine-namen", "version": "1.0.0", "dependsOn": ["base"] }
data/meine-namen/clubs/de.json     (komplette Vereinsliste)
data/meine-namen/lang/de.json      { "menu.squad": "Mannschaft" }
```

| Datei | Inhalt |
|---|---|
| `countries/*.json` | Länderkonfiguration: Pyramide, Ligaformat, Kalender, Kader- und Disziplinregeln |
| `clubs/*.json` | Vereine (fiktiv): Name, Liga, Reputation, Farben, Stadion |
| `names/*.json` | Namenslisten für Zufallsspieler je Land |
| `formations.json` | Formationen auf dem 5x7-Raster mit Position und Aufgabe je Slot |
| `rules/positions.json` | Attributgewichte je Position (Positions- und Gesamtstärke) |
| `rules/injuries.json` | Verletzungsarten mit Dauer und Häufigkeit |
| `rules/engine.json` | Gewichte und Wahrscheinlichkeiten der Matchengine |
| `lang/de.json`, `lang/de.commentary.json` | Texte der Oberfläche und Kommentarbausteine |

Beim Start prüft `DataValidator` die Pakete (z. B. „Liga hat 17 statt 18 Teams") und bricht mit
einer Fehlerliste ab.

## Offene Entscheidungen

- **Lizenz:** Das Fachkonzept schlägt GPL-3.0 für den Code und CC BY-SA 4.0 für Daten und Texte vor.
  Eine Lizenzdatei ist noch nicht enthalten.
- **Datenhaltung:** Spielstände sind derzeit komprimiertes JSON. SQLite (Kapitel 12) lohnt sich, sobald
  mehrere Länder und Transfers die Datenmenge erhöhen.
