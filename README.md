# FMH – Fußball-Manager im Textmodus

> „Ein Klub, eine Saison, noch eine Woche."

Ein Fußball-Manager ohne 3D-Darstellung: Spielgefühl und Humor wie Anstoß 3, Attributsystem und
Taktiktiefe wie Football Manager, der Spieltag läuft als Text-Kommentar oder Ticker. Grundlage ist das
Fachkonzept „Fußball-Manager (Textmodus)".

Dieser Stand umfasst **Phase 1 (Prototyp)** und **Phase 2 (Karriere)** der Roadmap:

- Phase 1: Matchengine, Attribute, zwei Ligen, Kommentarmodus.
- Phase 2: Training, vollständige Taktik mit Rollen, Transfers, Verträge, Scouting, Pokal,
  Relegation und Jugend.

Die Karriere läuft über beliebig viele Saisons, gespielt wird in einer Desktop-Oberfläche mit Maus und
Tastatur. Spielstände sind SQLite-Datenbanken.

## Schnellstart

Voraussetzung: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build -c Release
dotnet run -c Release --project src/Fmh.Desktop     # das Spiel (Fenster, Maus und Tastatur)
dotnet test                                          # 65 Tests, davon 10 Oberflächentests ohne Bildschirm
dotnet run -c Release --project src/Fmh.Calibrate -- --matches 10000           # Engine-Kalibrierung
dotnet run -c Release --project src/Fmh.Calibrate -- --season --seasons 8      # Karriere-Simulation
```

Spielstände (`*.fmh`, SQLite) landen unter `%APPDATA%/FMH/saves` bzw. `~/.config/FMH/saves`,
änderbar mit `--saves <pfad>` oder der Umgebungsvariable `FMH_SAVE_DIR`. Eigene Datenpakete lädt
`--data <pfad>`.

## Bedienung

Alles ist mit der Maus bedienbar: Navigation links, Spalten zum Sortieren anklicken, Doppelklick öffnet
Spielerprofile, Spieler werden auf dem Taktikfeld per Drag & Drop oder Klick-Klick aufgestellt.
Zusätzlich gibt es Tastenkürzel:

| Taste | Wirkung |
|---|---|
| Leertaste | **Weiter** bis zum nächsten Ereignis (Spieltag, Nachricht, Entscheidung, Saisonende) |
| F1–F10 | Posteingang, Kader, Taktik, Training, Spielplan, Transfers, Finanzen, Stadion, Verein, Liga |
| Strg+S | Speichern |
| Kader / Transfers | Enter oder Doppelklick: Spielerprofil, ←→ im Profil: voriger/nächster Spieler |
| Taktik | A automatisch aufstellen, Esc Auswahl aufheben, Umschalt+Pfeile gewählten Platz verschieben |
| Liga | T Tabelle ↔ Torjäger |
| Spiel | Leertaste/P Pause, W Wechsel, T Taktik, Z Zuruf, M Kommentar/Ticker |

Die Farbcodierung der Attribute steht zusätzlich als Symbol daneben (▼ 1–5, · 6–10, ▲ 11–15, ★ 16–20).
Farbthemen: Klassik, Hell, Terminal.

## Karriere (Phase 2)

- **Training:** Wochenplan mit Vor- und Nachmittag (9 Bausteine), drei Intensitäten mit
  Verletzungsrisiko und Wochenbelastung. Dazu Individualtraining (Schwerpunkt-Attribut),
  Positionsumschulung über mehrere Monate und Trainingslager (Erholung, Teambuilding, Fitness).
  Der Fortschritt hängt von Alter, Potenzial, Professionalität, Moral und Trainerteam ab.
- **Taktik:**
  - 25 Formationen und 24 Rollen mit Aufgabe (Verteidigen, Unterstützen, Angreifen).
  - Vier Einzelanweisungen: öfter schießen, eng decken, früh flanken, Position halten.
  - Freie Positionierung auf dem 5x7-Raster und drei gespeicherte Taktikvarianten.
  - Ecken- und Freistoßvarianten.
  - Taktikvertrautheit: Sie wächst durch Taktiktraining und Spiele und wirkt in der Matchengine.
- **Transfers und Verträge:**
  - Suche mit Filtern, Beobachtungsliste und Transferfenster.
  - Verhandlung in zwei Stufen mit höchstens drei Runden, danach 14 Tage Sperre:
    - mit dem Verein: Ablöse, Raten, Weiterverkaufsbeteiligung, Tauschspieler;
    - mit dem Spieler: Laufzeit, Gehalt, Handgeld, Prämien, Ausstiegs- und Abstiegsklausel,
      jährliche Erhöhung, Kaderstatus.
  - Leihen mit Gehaltsanteil, Kaufoption oder -pflicht und Rückholklausel.
  - Vorverträge, ablösefreie Spieler, Vertragsverlängerungen und Beraterhonorar.
  - Verweigerungsgründe: Reputation, Liga, Konkurrenz, Vereinstreue.
  - Die KI kauft, verleiht, sichert sich Vorverträge und bietet für eigene Spieler; solche Angebote
    sind Pflicht-Entscheidungen im Posteingang.
- **Scouting:** Kenntnisstand je Spieler (0–100 %). Fremde Spieler zeigen Attribute als Spanne.
  Scouts beobachten Spieler, durchsuchen Ligen nach Kriterien (Berichte A–D mit Sternen für Stärke
  und Potenzial) oder analysieren den nächsten Gegner.
- **Pokal und Relegation:**
  - Pokal mit 64 Teams einschließlich 28 Amateurvereinen; Freilose und Heimrecht für
    Unterklassige; Verlängerung und Elfmeterschießen; neutrales Finale.
  - Relegation 16. gegen 3. in Hin- und Rückspiel.
  - Ansicht mit allen Runden und Duellen.
- **Jugend:** Jahrgang im März, ein Anfangsjahrgang bei Spielstart und 2 % Chance auf eine
  „goldene Generation". Profiverträge ab 16, Freigabe mit 19; die KI übernimmt Talente selbst.
- **Budgets:** Kontostand, Transfer- und Gehaltsbudget je Saison aus der Reputation. Die volle
  Finanzsimulation folgt in Phase 3.

Die Karriere-Simulation (8 Saisons) zeigt eine stabile Welt:

- Kader bleiben bei durchschnittlich 23–25 Spielern.
- Das Durchschnittsalter liegt bei 26 Jahren.
- Pro Saison gibt es rund 60 Transfers mit Ablöse und 25–70 Leihen.
- Die Spielstärke der Ligen steigt nur leicht, um etwa 0,07 Punkte pro Saison.

## Umfang nach Kapiteln des Fachkonzepts

| Kapitel | Umgesetzt | Folgt laut Roadmap |
|---|---|---|
| 2 Spielmodi und Spielstart | Karriere mit neuem Spiel (Name, Werdegang, Verein, Seed, Weltregeln), Managerprofil, Speichern, Autosave vor jedem Spieltag in 3 rotierenden Slots, optional Ironman | Schnellsaison, Hot-Seat, Herausforderungen (Phase 4) |
| 3 Spielwelt, Wettbewerbe | Tagesgenauer Kalender, Ligen als Daten, Deutschland mit 1. und 2. Liga und Amateurbereich, **Pokal**, **Relegation**, Transferfenster, Pflicht-Entscheidungen | Europapokal, weitere Länder (Phase 3) |
| 4 Spielerdatenmodell | 44 Attribute, versteckte Werte, Kondition, Form, Moral, Spielpraxis, Positionseignung, Entwicklung durch **Training** und Saisonwechsel, Karriereende, Dauerschäden | Zufriedenheit, Tutor-System |
| 5 Kader, Training, Taktik | Kader, Profil, **Training**, **Trainingslager**, 25 Formationen, **Rollen und Aufgaben**, **Spieleranweisungen**, **freie Positionierung**, **Taktikvertrautheit**, **3 Taktikvarianten**, Standardvarianten, Co-Trainer | – |
| 6 Spieltag und Matchengine | Vorbericht, Kabinenansprache, Kommentar/Ticker/Sofort, Wechsel, Taktik und Zurufe live, Nachbericht mit Noten und Statistik, **Verlängerung und Elfmeterschießen**, Hin- und Rückspiel | Konferenz, Pressekonferenz (Phase 3) |
| 7 Transfers, Verträge, Scouting | **vollständig** (siehe oben) | – |
| 8–10 Finanzen, Gelände, Umfeld | Budgets und Buchungen (Ablösen, Raten, Handgeld, Trainingslager) | Phase 3 |
| 11 Oberfläche | Desktop-Oberfläche (Avalonia) mit Maus, Drag & Drop, sortierbaren Tabellen, Diagrammen (Radar, Formkurve, xG, Heatmap), Wappen, Farbthemen | weitere Sprachen, Tastenbelegung |
| 12 Nicht-funktional | Determinismus, **SQLite-Spielstände** mit Schemamigration, Texte in Sprachdateien, Datenpakete mit Überlagerung, Datenvalidierung, Kalibrierungs-CLI | Editor, Plugins |

## Spielstände (SQLite)

Ein Spielstand ist eine normale SQLite-Datenbank. Spieler-Attribute, Fixtures, Tore, Karten,
Transfers, Buchungen, Scouts und Kenntnisstand liegen in eigenen Tabellen und lassen sich direkt per
SQL auswerten, zum Beispiel:

```sql
SELECT first_name, last_name, a_Finishing, a_Pace FROM players WHERE club_id = 6 ORDER BY a_Finishing DESC;
SELECT date, player_id, fee FROM transfers WHERE to_club = 6;
```

Verschachtelte Konfigurationen (Ligaformat, Taktik, Pokalrunden, Verhandlungen) liegen als JSON-Spalten
daneben. Die Tabelle `meta` enthält die Schemaversion. Ältere Spielstände (Version 1 aus Phase 1)
werden beim Laden per Migration angehoben und mit Karrieredaten nachgerüstet: Budgets, Scouts, Pokal
und Amateurvereine.

## Kalibrierung der Matchengine

`fmh-calibrate --matches 10000` (Seed 1), Zielwerte aus Kapitel 6:

| Kennzahl | Ist | Ziel |
|---|---|---|
| Tore pro Spiel | 2,82 | 2,8 |
| Unentschieden | 23,7 % | 25 % |
| Heimsiege | 44,5 % | 45 % |
| Gelbe Karten pro Spiel | 3,76 | 3,8 |
| Rote Karten pro Spiel | 0,20 | 0,2 |
| Verletzungen pro Team und Spiel | 0,30 | 0,3 |

Leistung: ca. 2,5 ms pro Spiel; der langsamste Spieltag (alle Ligen und Pokal) braucht unter 1 s
(Ziel: unter 5 s). Die Gewichte stehen in `data/base/rules/engine.json`.

## Aufbau

```
src/
  Fmh.Core/          Kern: Datenmodell, Kalender und Tagesablauf (GameSession, aufgeteilt in Saison,
                     Pokal, Transfers, Training, Scouting, Jugend), Ligen und Tabellen, Weltgenerator,
                     Aufstellung/KI-Taktik, Verhandlungslogik, Marktwerte, SQLite-Speicher, Datenpakete,
                     Schnittstelle IMatchEngine
  Fmh.MatchEngine/   Text-Matchengine (Phasen, 5x7-Zonen, Duelle, Rollen, Verlängerung, Elfmeterschießen)
                     und Kommentargenerator
  Fmh.Desktop/       Desktop-Oberfläche (Avalonia, MVVM), Programm "fmh"
  Fmh.Calibrate/     Kommandozeilen-Simulator "fmh-calibrate"
tests/Fmh.Tests/     xUnit-Tests für Kern, Engine, Karriere, Spielstände, Daten und Oberfläche
data/base/           Datenpaket "base": Länder, Vereine, Namen, Formationen, Rollen, Regeln, Sprachdateien
```

Oberflächentests laufen ohne Bildschirm (Avalonia Headless). Mit `FMH_SHOTS=<ordner> dotnet test`
speichern sie Bildschirmfotos aller Seiten.

## Datenpakete und Modding

Jedes Unterverzeichnis von `data/` mit einer `package.json` ist ein Paket. Pakete werden nach
`dependsOn` sortiert und übereinandergelegt: Eine Datei mit gleichem relativen Pfad ersetzt die
frühere, Sprachdateien werden schlüsselweise zusammengeführt.

| Datei | Inhalt |
|---|---|
| `countries/*.json` | Pyramide, Ligaformat, Kalender, Kaderregeln, Amateur-Pools, Pokal, Relegation, Transferfenster, Jugend |
| `clubs/*.json` | Vereine (fiktiv): Name, Liga oder Pool, Reputation, Farben, Stadion |
| `names/*.json` | Namenslisten für Zufallsspieler je Land |
| `formations.json` | Formationen auf dem 5x7-Raster |
| `rules/roles.json` | Rollen mit erlaubten Positionen, Aufgaben und Wirkung in der Engine |
| `rules/positions.json`, `rules/injuries.json`, `rules/engine.json` | Attributgewichte, Verletzungen, Engine-Gewichte |
| `lang/de.json`, `lang/de.commentary.json` | Texte der Oberfläche und Kommentarbausteine |

Beim Start prüft `DataValidator` die Pakete und bricht mit einer Fehlerliste ab. Geprüft werden unter
anderem Teamzahlen, Pools, Pokaltermine und Relegationsplätze.

## Offene Entscheidungen

- **Lizenz:** Das Fachkonzept schlägt GPL-3.0 für den Code und CC BY-SA 4.0 für Daten und Texte vor.
  Eine Lizenzdatei ist noch nicht enthalten.
- **Spielstärke-Drift:** Über viele Saisons steigt die Ligastärke leicht, um etwa 0,07 pro Saison.
  Mit Finanzen und Gehältern in Phase 3 sollte das neu kalibriert werden.
