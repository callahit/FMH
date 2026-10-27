# FMH – Fußball-Manager im Textmodus

> „Ein Klub, eine Saison, noch eine Woche."

Ein Fußball-Manager ohne 3D-Darstellung: Spielgefühl und Humor wie Anstoß 3, Attributsystem und
Taktiktiefe wie Football Manager, der Spieltag läuft als Text-Kommentar oder Ticker. Grundlage ist das
Fachkonzept „Fußball-Manager (Textmodus)".

Dieser Stand umfasst **Phase 1 (Prototyp)**, **Phase 2 (Karriere)** und **Phase 3 (Beta)** der Roadmap:

- Phase 1: Matchengine, Attribute, zwei Ligen, Kommentarmodus.
- Phase 2: Training, vollständige Taktik mit Rollen, Transfers, Verträge, Scouting, Pokal,
  Relegation und Jugend.
- Phase 3: Finanzen, Stadion, Vereinsgelände, Personal, Sponsoren, Vorstand, Fans, Presse,
  Zufallsereignisse, Managerkarriere, Konferenz und Mehrsprachigkeit (siehe [Umfeld](#umfeld-phase-3)
  und [Sprachen](#sprachen)).

Die Karriere läuft über beliebig viele Saisons, gespielt wird in einer Desktop-Oberfläche mit Maus und
Tastatur. Spielstände sind SQLite-Datenbanken.

Die Weiterentwicklung zu einem modularen, europaweiten Manager folgt dem
[Umsetzungskonzept](docs/umsetzungskonzept.md): Arbeitspakete (AP 0–15) mit je einem Branch und einem
Pull Request, Abnahmekriterien und Definition of Done.

**Lizenz:** Open Source; die konkrete Lizenz folgt.

## Schnellstart

Voraussetzung: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build -c Release
dotnet run -c Release --project src/Fmh.Desktop     # das Spiel (Fenster, Maus und Tastatur)
dotnet test                                          # 74 Tests, davon 11 Oberflächentests ohne Bildschirm
dotnet run -c Release --project src/Fmh.Calibrate -- --matches 10000           # Engine-Kalibrierung
dotnet run -c Release --project src/Fmh.Calibrate -- --season --seasons 8      # Karriere-Simulation
```

Spielstände (`*.fmh`, SQLite) landen unter `%APPDATA%/FMH/saves` bzw. `~/.config/FMH/saves`,
änderbar mit `--saves <pfad>` oder der Umgebungsvariable `FMH_SAVE_DIR`. Eigene Datenpakete lädt
`--data <pfad>`, eine bestimmte Sprache `--lang en`.

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
  - Pokal mit 64 Teams: alle Vereine der 1. bis 3. Liga und die acht Regionalligisten mit dem höchsten
    Ruf; Freilose und Heimrecht für Unterklassige; Verlängerung und Elfmeterschießen; neutrales Finale.
  - Relegation 16. gegen 3. in Hin- und Rückspiel (1./2. und 2./3. Liga), Aufstiegsspiele der beiden
    Regionalliga-Meister ohne Direktplatz (jährlich wechselnd).
  - Ansicht mit allen Runden und Duellen.
- **Jugend:** Nachwuchsleistungszentrum mit drei Altersstufen, siehe
  [Nachwuchsleistungszentrum](#nachwuchsleistungszentrum).
- **Budgets:** Kontostand, Transfer- und Gehaltsbudget je Saison; seit Phase 3 aus den Einnahmen
  des Vereins berechnet (siehe unten).

Die Karriere-Simulation (8 Saisons) zeigt eine stabile Welt:

- Kader bleiben bei durchschnittlich 23–25 Spielern.
- Das Durchschnittsalter liegt bei 26 Jahren.
- Pro Saison gibt es rund 60 Transfers mit Ablöse und 25–70 Leihen.
- Die Spielstärke der Ligen steigt nur leicht, um etwa 0,07 Punkte pro Saison.

## Nachwuchsleistungszentrum

- **Altersstufen:**
  - U15 (14–15 Jahre), U17 (16–17) und U19 (18).
  - Jede Stufe hat eine begrenzte Zahl an Plätzen, die mit der Infrastruktur wächst.
  - Jugendtrainer aus dem Personal betreuen je eine Altersstufe.
- **Jährlicher Jahrgang:**
  - Am 15. März kommen 14-Jährige in die U15.
  - Ihre Anzahl und ihr Talent hängen von Sichtung, Ruf der Akademie und Vereinsgröße ab; mit 2 %
    Chance gibt es eine „goldene Generation".
  - Bei guter Sichtung kommen Quereinsteiger mit 15 oder 16 Jahren dazu.
- **Vier Qualitätskriterien (1–20):** Sie folgen ihrem Zielwert über mehrere Monate.
  - Trainerarbeit: Budgetanteil und Jugendtrainer.
  - Infrastruktur: Leistungszentrum, Jugendplätze, Internat, Kabinen, Kantine.
  - Sichtung: Budgetanteil, Fußballschule, Internat, Vereins- und Akademieruf.
  - Schule und Betreuung: Budgetanteil, Partnerschule, Internat.
- **Jahresbudget:**
  - Das Budget wird monatlich gebucht und auf Trainer, Sichtung und Schule verteilt.
  - Zum Saisonstart kommt ein Vorschlag in den Posteingang.
  - Ohne Geld sinkt die Qualität; die KI richtet ihr Budget nach Finanzlage und Lizenzvorgabe.
- **Zertifizierung:** 0–3 Sterne aus dem Gesamtwert. Sie ist Lizenzvoraussetzung (1. Liga 2 Sterne,
  2. Liga 1 Stern, Europa 1 Stern).
- **Wirkung:** Die Talente entwickeln sich je nach Trainern, Infrastruktur und Alter, ältere lernen
  schneller.
- **Saisonstart:**
  - Volle Altersstufen werden ausgedünnt.
  - Schwache Schule führt zu mehr Abbrechern.
  - Mit 19 endet die Jugendzeit. Wer nicht übernommen wird, geht meist in den Amateurfußball.
- **Absolventen:** Profiverträge ab 16. Absolventen, die sich durchsetzen, steigern den Ruf der
  Akademie.
- **Seite:** Verein → Nachwuchs mit Kriterien und Trend, Budget und Aufteilung, Altersstufen mit
  Trainerwahl und Spielerlisten.

## Umfeld (Phase 3)

- **Finanzen** (Kapitel 8):
  - Einnahmen: Zuschauer nach Preis, Stärke, Wetter, Derby und Fanstimmung; Dauerkarten bis Ende Juli;
    Fernsehgelder in zehn Raten plus Leistungsanteil; Sponsoren; Catering, Fanartikel und Gelände;
    Pokal- und Relegationsprämien.
  - Ausgaben: Gehälter, Personal, Unterhalt, Reisen, Prämien, Zinsen und Betriebskosten (Verwaltung,
    Spielbetrieb, Personal außerhalb des Kaders) als Anteil der laufenden Einnahmen (`operatingCostShare`).
  - Gehälter (siehe „Wirtschaft“ unten): Marktgehalt nach Stärke, angepasst an das Gehaltsniveau der
    Spielklasse (`wageLevel`) und begrenzt durch ihr Spitzengehalt (`maxAnnualWage`). Verträge sind
    ligaabhängig: Nach Auf- oder Abstieg ändern sich die Gehälter im Verhältnis der Gehaltsniveaus.
  - Bank: Kredite (3–12 % Zins je nach Verschuldung), Festgeld, Umschichten zwischen Transfer- und
    Gehaltsbudget (52:1), Eintrittspreise mit Fanreaktion.
  - Lizenzierung mit Stichtagen, jedes Jahr für die nächste Saison:
    - Antrag am 15. März mit Checkliste.
    - Entscheidung am 30. April: erteilt, mit Auflagen, mit Punktabzug oder verweigert.
      Verweigert bedeutet Zwangsabstieg; in der untersten Liga gibt es stattdessen neun Punkte Abzug.
    - Nachweis der Auflagen bis 31. Mai, sonst drei Punkte Abzug und eine Geldstrafe.
    - Geprüft werden Eigenkapital, Flutlicht und die Zertifizierung des Nachwuchsleistungszentrums.
    - Aufstiegskandidaten werden zusätzlich nach den Kriterien der höheren Liga geprüft.
      Fehlt das Geld, ist kein Aufstieg möglich; fehlende Infrastruktur kostet eine Geldstrafe.
  - Insolvenz nach 30 Tagen unter dem Kreditrahmen: KI-Vereine bekommen neun Punkte Abzug, der
    Mensch wird entlassen.
  - **Financial Fair Play:**
    - Prüfung zum Saisonstart für die Vereine der 1. Liga und alle Europapokalteilnehmer.
    - Regel 1: Das Break-even-Ergebnis über drei Spielzeiten darf höchstens 5 Mio. im Minus liegen.
      Ausgaben für Stadion, Gelände und Nachwuchs zählen dabei nicht.
    - Regel 2: Die Kaderkostenquote darf höchstens 70 % betragen.
    - Sanktionen: Verwarnung, im Wiederholungsfall Geldstrafe, ab 30 Mio. Defizit Ausschluss
      aus Europa.
  - **Europa-Lizenz** (zum 30. April):
    - Voraussetzungen sind eine einwandfreie nationale Lizenz, kein FFP-Ausschluss, mindestens
      8.000 Sitzplätze, Flutlicht Stufe 3, ein zertifiziertes Leistungszentrum und keine
      überfälligen Verbindlichkeiten.
  - **Europapokalplätze** zum Saisonende:
    - Platz 1–4 der 1. Liga spielen im Champions-Pokal, Platz 5 und der Pokalsieger im
      Europa-Pokal, Platz 6 im Conference-Pokal.
    - Ohne Zulassung rückt der nächste Verein nach. Ist der Pokalsieger schon qualifiziert oder ohne
      Lizenz, geht sein Platz an den nächsten Ligaplatz.
    - Die Wettbewerbe laufen vereinfacht ohne eigene Spiele: An fünf Terminen entscheidet die
      Mannschaftsstärke gegen das Teilnehmerfeld über das Weiterkommen. Es gibt Start- und
      Rundenprämien, Fanstimmung und Vereinsruf steigen.
  - Ansicht unter Finanzen → Lizenz und Europa.
  - Saisonbilanz, Monatsverlauf als Diagramm.
- **Stadion:** vier Tribünen mit Steh- und Sitzplätzen, Dach und Logen; Flutlicht, Rasenheizung,
  Anzeigetafel, Rasenzustand. Bauprojekte brauchen ab 2 Mio. die Freigabe des Vorstands und ab
  1 Mio. eine Baugenehmigung. Drei Baufirmen (billig und riskant, normal, teuer und schnell),
  Verzögerungen und Pleiten, Finanzierung über Kredit.
  - **Prognose je Ausbau:** zusätzliche Zuschauer pro Heimspiel, Mehreinnahmen und Unterhalt pro
    Saison und Amortisationsdauer. Berechnet wird mit dem Zuschauermodell, dem aktuellen Spielplan,
    den Preisen und der Fanstimmung. Mehr Plätze bringen nur etwas, wenn die Nachfrage das Stadion
    füllt.
  - **Stadionneubau an anderer Stelle** (Sonderthema):
    - Nur möglich, wenn die Stadt per Zufallsereignis ein Grundstück anbietet. Das Angebot gilt zwei
      Jahre, die Größe des Grundstücks begrenzt die Kapazität.
    - Planung von Kapazität, Stehplatzanteil, Logen, Dach und Name mit Kosten- und
      Einnahmenprognose.
    - Bauzeit etwa zwei Jahre, Stadionkredit über bis zu 20 Jahre. Während des Baus wird im alten
      Stadion gespielt.
- **Vereinsgelände** (Kapitel 9): 83 Gebäude in neun Bereichen (Training, Medizin, Jugend, Fans,
  Kommerz, Verwaltung, Infrastruktur, Energie, Mannschaften).
  - Bis zu fünf Ausbaustufen, Zustand, Unterhalt, Voraussetzungen, Lagebonus und Synergien.
  - Gebäude lassen sich verpachten oder abreißen, Bauland ist zukaufbar.
  - Anklickbares Raster mit Baumenü.
- **Personal:** neun Rollen (Co-Trainer, Fitness-, Torwart- und Jugendtrainer, Physiotherapeut,
  Arzt, Sportpsychologe, Pressesprecher, Platzwart) mit Arbeitsmarkt, Einstellung, Entlassung mit
  Abfindung. Das Personal wirkt auf Training, Verletzungen, Jugend, Moral, Presse und Rasen.
- **Sponsoren:** Trikot, Ärmel, Ausrüster, zehn Banden und Stadionname. Angebote kommen in den
  Posteingang, Erfolgsprämie beim Saisonziel. Es gibt 70 fiktive Firmen.
- **Vorstand:** Saisonziel zum Saisonstart, Vertrauen 0–100, Mitglieder mit Eigenschaften, Bitten
  (Budget, Vertrag, Jugendzuschuss), Ultimatum, Entlassung, Jahreshauptversammlung mit Abstimmung
  über Bauprojekte.
- **Fans:** Stimmung, Mitglieder, Fanclubs, Publikumslieblinge, Choreos vor Derbys und Topspielen,
  Proteste. Die Stimmung wirkt auf Zuschauer, Fanartikel und Dauerkarten.
- **Presse:**
  - Pressekonferenzen vor und nach dem Spiel mit 44 Fragen und vier Antworttönen; Wirkung auf
    Moral, Gegner, Vorstand, Medienansehen, Fehden und Strafen. Pressekonferenzen lassen sich auch
    an den Pressesprecher delegieren.
  - Schlagzeilen, Gerüchte und Spielerinterviews in drei Tonfällen (satirisch, ausgewogen, seriös).
- **Zufallsereignisse** (Kapitel 10): 166 Ereignisse als Daten (`data/base/events/*.json`) in den
  Bereichen Spieler, Verein, Fans, Medien, Finanzen, Stadion, Sport und Kurioses.
  - Bedingungen: Spieler, Monat, Ruf, Sieg oder Niederlage, Stimmung, Kontostand.
  - Antworten mit Erfolgschance; Häufigkeit und Tonfall sind einstellbar.
- **Managerkarriere:** Trainerlizenzen C bis Pro mit Lehrgängen im Sommer, Stationen mit Bilanz, 25
  Erfolge in der Ruhmeshalle, Jobangebote, Rücktritt, Entlassung und Vereinswechsel. Während der
  Vereinslosigkeit spielt ein Interimstrainer weiter.
- **Konferenz:** Anzeigemodus, in dem die anderen Spiele des Spieltags minutengenau mitlaufen, mit
  Torblitz im Ticker und Live-Tabelle.
- **Einstellungen:** Schwierigkeit (Leicht, Normal, Schwer, Realistisch) beim Spielstart;
  Tonfall, Ereignishäufigkeit und Pressedelegation jederzeit in den Optionen.
- **Statuszeile:** Kontostand, Vorstand, Fans, Moral und nächstes Spiel.

### Wirtschaft

**Währung:** Gerechnet wird überall in Euro (Leitwährung); alle Datenpakete geben Beträge in Euro an (der Validator
prüft `currency: "EUR"`). Pfund und US-Dollar gibt es nur als Anzeige: Unter Optionen wählt der Manager die
Anzeigewährung, die Standardkurse stehen in `rules/currencies.json` (EZB-Referenzkurse) und lassen sich je Profil
überschreiben. Geldeingaben (Verhandlungen, Preise, Kredite) rechnen die Anzeigewährung zurück in Euro.

Gehälter und Einnahmen haben reale Größenordnungen. Referenz für Deutschland sind der
[DFL-Wirtschaftsreport 24/25](https://report.bundesliga.de/2425/de/_assets/downloads/key-economic-figures-dfl-er25.pdf)
(Bundesliga je Klub rund 284 Mio. Umsatz, 96 Mio. Personalaufwand Spielbetrieb; 2. Bundesliga 67 bzw.
16,5 Mio.), der [DFB-Saisonreport 3. Liga](https://www.dfb.de/news/wirtschaftlich-stabil-und-talentschmiede-3-liga-schreibt-rekordzahlen)
(rund 16 Mio. Erträge, 5,5 Mio. Personal) und MDR-Recherchen zur Regionalliga (Spieler im Schnitt rund 1.000 €
im Monat, Spitze bis 6.500 €).

- **Marktgehalt:** 9.000 € pro Woche bei Stärke 9,4, Faktor 1,85 je Stärkepunkt; Marktwert 9 Mio. bei Stärke 12
  mit demselben Faktor. Gehaltsforderungen gehen vom Marktgehalt aus, das bisherige Gehalt hebt sie höchstens
  auf 120 % (Verlängerung) bzw. 140 % (Wechsel).
- **Marktniveau je Spielklasse:** Zum Spielstart und jedem Saisonstart wird die Ø-Stärke der besten elf je
  Verein gemessen. Gehälter und Marktwerte richten sich nach dem Abstand dazu, damit stärker werdende Kader
  das Gehaltsgefüge nicht aufblähen (Spielstand: `market_levels`/`market_bases` in `meta`).
- **Daten je Spielklasse** (`finance.divisions`): TV-Topf, Eintrittspreise, `wageLevel`, `maxAnnualWage`,
  `sponsorLevel`, `operatingCostShare` und `targetAnnualWage` als Kalibrierungsziel.
- **Kalibrierung:** `fmh-calibrate --season --seasons 5 --strict` prüft je Spielklasse, dass das Ø-Jahresgehalt
  im Korridor ±30 % um das Ziel liegt; erlaubt ist eine Lohnentwicklung von 4 % je Saison (real etwa 4 % im
  Jahr laut DFL 2019/20–2024/25).

Die Finanzsimulation über fünf Saisons ergibt folgende Werte je Verein:

| Liga | Ø Jahresgehalt (Ziel) | Kadergehälter | Einnahmen | Saldo pro Saison |
|---|---|---|---|---|
| 1. Liga | 2,9–3,6 Mio. (2,9 Mio.) | 69–81 Mio. | 200–250 Mio. | +28 bis +38 Mio. |
| 2. Liga | 420–550 Tsd. (420 Tsd.) | 10–12 Mio. | 37–45 Mio. | +4 bis +8 Mio. |
| 3. Liga | 150–180 Tsd. (165 Tsd.) | 3,8–4 Mio. | 9–10 Mio. | ±0 bis +1,8 Mio. |
| Regionalliga | 26–34 Tsd. (25 Tsd.) | 0,6–0,8 Mio. | 2,5–3,9 Mio. | ±0 bis +0,4 Mio. |

Darin sind die Budgets der Nachwuchsleistungszentren und die Europapokalprämien bereits enthalten.
Absteiger geraten mit ihren Gehältern ins Minus. Es gibt keine Kettenpleiten. In der 2. Liga bekommen
einige Vereine im Frühjahr Lizenzauflagen, Punktabzüge sind selten. Die Zahl der vereinslosen
Spieler bleibt bei 100–130: Wer nach der Jugend nicht übernommen wird, geht meist in den
Amateurfußball, und Vereine mit dünnem Kader verpflichten unter der Saison nach. Die Kennzahlen zeigt
`fmh-calibrate --season --seasons 5` je Liga und Buchungsart an.

## Umfang nach Kapiteln des Fachkonzepts

| Kapitel | Umgesetzt | Folgt laut Roadmap |
|---|---|---|
| 2 Spielmodi und Spielstart | Karriere mit neuem Spiel (Name, Werdegang, Verein, Seed, Weltregeln), Managerprofil, Speichern, Autosave vor jedem Spieltag in 3 rotierenden Slots, optional Ironman | Schnellsaison, Hot-Seat, Herausforderungen (Phase 4) |
| 3 Spielwelt, Wettbewerbe | Tagesgenauer Kalender, Ligen als Daten, Deutschland bis zur 4. Liga (Regionalliga in fünf Staffeln) mit Oberliga als Pool, England mit vier Profiligen und National League als Pool, **Pokal**, **Relegation**, Transferfenster, Pflicht-Entscheidungen, **Ligapyramiden mit Staffeln, Bewegungsregeln, Aufstiegsrunden, zweiten Mannschaften und Formatversionen** | Europapokal, weitere Länder (Phase 3) |
| 4 Spielerdatenmodell | 44 Attribute, versteckte Werte, Kondition, Form, Moral, Spielpraxis, Positionseignung, Entwicklung durch **Training** und Saisonwechsel, Karriereende, Dauerschäden | Zufriedenheit, Tutor-System |
| 5 Kader, Training, Taktik | Kader, Profil, **Training**, **Trainingslager**, 25 Formationen, **Rollen und Aufgaben**, **Spieleranweisungen**, **freie Positionierung**, **Taktikvertrautheit**, **3 Taktikvarianten**, Standardvarianten, Co-Trainer | – |
| 6 Spieltag und Matchengine | Vorbericht, Kabinenansprache, Kommentar/Ticker/Sofort/**Konferenz**, Wechsel, Taktik und Zurufe live, Nachbericht mit Noten und Statistik, **Verlängerung und Elfmeterschießen**, Hin- und Rückspiel, **Pressekonferenzen** | – |
| 7 Transfers, Verträge, Scouting | **vollständig** (siehe oben) | – |
| 8 Finanzen, Stadion, Personal | **vollständig**: Einnahmen und Ausgaben, Bank, Lizenz, Insolvenz, Stadionausbau, Personal, Sponsoren | – |
| 9 Vereinsgelände | **83 Gebäude** mit Stufen, Lage und Synergien, Baufirmen, Genehmigungen | – |
| 10 Umfeld, Karriere | **Vorstand, Fans, Presse, 166 Zufallsereignisse, Managerkarriere** mit Lizenzen und Erfolgen | Nationalmannschaft, Medienhaus |
| 11 Oberfläche | Desktop-Oberfläche (Avalonia) mit Maus, Drag & Drop, sortierbaren Tabellen, Diagrammen (Radar, Formkurve, xG, Heatmap), Wappen, Farbthemen | Tastenbelegung |
| 12 Nicht-funktional | Determinismus, **SQLite-Spielstände** mit Schemamigration, **Mehrsprachigkeit (Deutsch, Englisch, erweiterbar)**, Datenpakete mit Überlagerung, Datenvalidierung, Kalibrierungs-CLI mit Toleranzprüfung, **CI auf Linux und Windows**, Warnungen als Fehler, **Modulaufbau mit Golden Master und Architekturtest** | Editor (AP 6), Mod-Verwaltung (AP 14) |

## Sprachen

Die Sprache wird im Hauptmenü (unten rechts) oder im Spiel unter **Einstellungen → Sprache** gewählt.
Die Oberfläche baut sich sofort neu auf, das Spiel läuft auf derselben Seite weiter. Die Wahl gilt für
das ganze Gerät und steht in `settings.json` neben dem Spielstandordner. Beim ersten Start wird die
Sprache aus der Systemumgebung erkannt (`LANG`), sonst gilt Deutsch. `--lang <code>` überschreibt beides.

Übersetzt werden:

- alle Texte der Oberfläche, Spielmeldungen und der Spielkommentar;
- Zahlen-, Datums- und Geldformate, z. B. „4,21 Mio. EUR · 01.07.2026“ bzw. „EUR 4.21m · 01/07/2026“;
- Namen von Wettbewerben, Positionen, Rollen und Verletzungen.

Spielstände sind sprachneutral. Nachrichten, Buchungen, Scoutberichte und Verhandlungsantworten werden
als Textschlüssel mit Argumenten gespeichert und bei jeder Anzeige in der gewählten Sprache aufgebaut.
Ein Sprachwechsel übersetzt deshalb auch den bisherigen Posteingang. Nachrichten aus Spielständen vor
dieser Version bleiben wörtlich erhalten.

**Eine Sprache hinzufügen:** Eine Datei `lang/<code>.json` in einem Datenpaket anlegen (zum Beispiel
`data/base/lang/fr.json` oder in einem eigenen Paket). Optional kommt `lang/<code>.commentary.json`
für den Spielkommentar dazu. Die Sprache erscheint danach automatisch in der Auswahl.

- Pflicht ist `"lang.name"` (Anzeigename in der Sprache selbst, z. B. „Français“).
- Die Formate stehen in den Schlüsseln `fmt.*` (Dezimal- und Tausendertrennzeichen, Datumsmuster,
  Wochentage, Geldmuster).
- Fehlende Texte und Kommentarbausteine kommen aus der Standardsprache des Grundpakets
  (`"defaultLanguage": "de"` in `package.json`).
- Pakete können auch einzelne Texte einer Sprache überschreiben, zum Beispiel `"competition.DE1": "Premier League"`.
- Für Sprachen im Repository prüft ein Test, dass alle Schlüssel vorhanden sind, dieselben Platzhalter
  `{0}`, `{1}` … verwendet werden und der Kommentar übersetzt ist.

## Spielstände (SQLite)

Ein Spielstand ist eine normale SQLite-Datenbank. Spieler-Attribute, Fixtures, Tore, Karten,
Transfers, Buchungen, Scouts und Kenntnisstand liegen in eigenen Tabellen und lassen sich direkt per
SQL auswerten, zum Beispiel:

```sql
SELECT first_name, last_name, a_Finishing, a_Pace FROM players WHERE club_id = 6 ORDER BY a_Finishing DESC;
SELECT date, player_id, fee FROM transfers WHERE to_club = 6;
```

Verschachtelte Konfigurationen (Ligaformat, Taktik, Pokalrunden, Verhandlungen) liegen als JSON-Spalten
daneben. Die Tabellen gehören Speicherabschnitten je Modul; jeder führt seine eigene Version in der
Tabelle `meta` (`schema.world`, `schema.finance` …). Die frühere globale Schemaversion 5 ist die
Ausgangsversion aller Abschnitte. Ältere Spielstände werden beim Laden zuerst global bis Version 5 und
danach je Abschnitt angehoben:

- Version 1 (Phase 1) bekommt die Karrieredaten: Budgets, Scouts, Pokal und Amateurvereine.
- Version 2 bekommt die Spalten für sprachneutrale Texte.
- Version 3 bekommt das Umfeld: Spalten für Finanzen, Stadion, Gelände, Sponsoren, Vorstand, Fans
  und Rivalen; Karriere des Managers; Punktabzüge; Tabellen `staff`, `events` und `event_history`.
  Fehlende Daten werden beim Laden erzeugt.
- Version 4 bekommt das Nachwuchsleistungszentrum, die Lizenzakte (Auflagen, Europa-Lizenz, FFP)
  und die Tabelle `european_entries`.
- Abschnitt `world` Version 6 bekommt die Tabelle `teams`: jeder Verein erhält seine erste Mannschaft.
- Abschnitt `world` Version 7 bekommt die Vereinsspalten `reserve_of`, `region`, `latitude` und `longitude`.

Ein Spielstand mit einem neueren oder unbekannten Abschnitt wird abgelehnt.

## Kalibrierung der Matchengine

`fmh-calibrate --matches 10000 --strict` (Seed 1), Zielwerte aus Kapitel 6, Toleranzen nach Abschnitt 8.1
des Umsetzungskonzepts:

| Kennzahl | Ist | Ziel | Toleranz |
|---|---|---|---|
| Tore pro Spiel | 2,79 | 2,8 | ± 0,15 |
| Unentschieden | 23,6 % | 25 % | ± 2,5 Pp. |
| Heimsiege | 44,8 % | 45 % | ± 2,5 Pp. |
| Gelbe Karten pro Spiel | 3,76 | 3,8 | ± 0,3 |
| Rote Karten pro Spiel | 0,19 | 0,2 | ± 0,05 |
| Verletzungen pro Team und Spiel | 0,30 | 0,3 | ± 0,05 |

Mit `--strict` endet das Programm mit Exitcode 1, wenn eine Kennzahl außerhalb liegt; die CI nutzt das.
Weitere Prüfungen:

- `--max-level 4` bezieht die unteren Ebenen in die Engine-Kalibrierung ein (Standard: Ebene 1–2, für die die
  Toleranzen gelten).
- `--max-ms 3` prüft zusätzlich die Rechenzeit je Spiel. Sie hängt von der Hardware ab und läuft
  deshalb nicht in der CI (gemessen: 2,4 ms in der Cloud-Umgebung, davon etwa 0,4 ms Aufstellung und
  KI-Taktik, der Rest Engine).
- `--season --seasons 5 --strict` prüft den Karrierelauf: Der langsamste Spieltag muss unter 5 s
  liegen und alle Saisons müssen durchlaufen.
- `--max-drift 0.05` prüft zusätzlich die Ligastärke-Drift.

Die Gewichte stehen in `data/base/rules/engine.json`.

## Entwicklung

- Arbeit auf Feature-Branches, ein Arbeitspaket je Pull Request (siehe Umsetzungskonzept, Abschnitt 1).
- CI (`.github/workflows/ci.yml`): Build und Tests auf Linux und Windows, Warnungen gelten als Fehler,
  dazu ein Kalibrierungsjob mit Toleranzprüfung.
- Keine Länder- oder Wettbewerbscodes im C#-Code; Regeln, Zahlen und Texte gehören in Datenpakete.

## Aufbau

Aufbau nach Umsetzungskonzept, Abschnitt 4: `GameSession` ist eine dünne Fassade über Spielmodulen,
die nur über Ereignisse und Dienst-Schnittstellen miteinander sprechen.

```
src/
  Fmh.Domain/        Reine Datentypen (Model, Match-Modelle), Rng, Formatierung, Texte, Konfigurationstypen,
                     Aufstellungs- und Bewertungsregeln, die die Engine braucht; referenziert nichts
  Fmh.Data/          Datenpakete laden, überlagern und validieren
  Fmh.Simulation.Abstractions/
                     Modulvertrag (IGameModule, ModuleContext, DayContext), Ereignisbus mit Ereignissen
                     (DayStarted, MatchPlayed), Dienst-Schnittstellen der Module, gemeinsamer Kern
                     (Spielstand, Zufall, Posteingang) und Regelbibliothek (Tabellen, Weltgenerator, Marktwerte …)
  Fmh.Modules/       Je Fachgebiet ein Modul: Spielbetrieb, Saison, Pokale, Transfers, Training, Scouting,
                     Jugend, Finanzen, Wirtschaft, Gelände, Stadionpläne, Personal, Sponsoren, Vorstand,
                     Presse, Ereignisse, Lizenz
  Fmh.Simulation/    Fassade GameSession: Tagesschleife, Modul-Registry (Reihenfolge aus DependsOn)
  Fmh.Persistence/   SQLite-Spielstand: Speicherabschnitte je Modul mit eigener Version, Altmigrationen bis Version 5
  Fmh.MatchEngine/   Text-Matchengine (Phasen, 5x7-Zonen, Duelle, Rollen, Verlängerung, Elfmeterschießen)
                     und Kommentargenerator; MatchSimulation ist nach Konzept 4.4 aufgeteilt in Zustandsmodell,
                     Phasensteuerung (Flow), Aktionen, Standards, Disziplin, Spielerwahl, Werte und Wechsel-KI
  Fmh.Desktop/       Desktop-Oberfläche (Avalonia, MVVM), Programm "fmh"
  Fmh.Calibrate/     Kommandozeilen-Simulator "fmh-calibrate"
tests/Fmh.Tests/     xUnit-Tests für Kern, Engine, Karriere, Spielstände, Daten, Oberfläche, Architektur
                     und Golden Master
data/base/           Datenpaket "base": Deutschland, Formationen, Rollen, Regeln, Sprachdateien
data/england/        Länderpaket England (dependsOn base): Ligen, Vereine, Namen, Sponsoren, Wettbewerbsnamen
```

Regeln, die ein Architekturtest prüft: erlaubte Projektverweise (Konzept 4.1), keine Datei in
`Fmh.Simulation`, `Fmh.Modules` oder `Fmh.MatchEngine` über 400 Zeilen, kein Modul verwendet die Klasse eines anderen.
Ein Verein (`Club`) besitzt Mannschaften (`Team`, je eine Welt `men`/`women`); vereinsweite Daten liegen
am Verein, Kader, Taktik, Training und Liga an der Mannschaft. Bis alle Module direkt mit Mannschaften
arbeiten, leiten die gleichnamigen Vereinseigenschaften an die erste Mannschaft weiter.

**Golden Master** (Konzept 4.5): `GoldenMasterTests` simuliert eine Welt mit Seed 4242 über drei Saisons
und 10.000 Engine-Spiele und vergleicht Hashes je Bereich mit `tests/Fmh.Tests/GoldenMaster/golden.json`
(ein Eintrag je Laufzeitumgebung). Neu gesetzt wird er nur in PRs, die eine Regeländerung ankündigen:
`FMH_GOLDEN_UPDATE=1 dotnet test --filter GoldenMaster`.

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
| `rules/buildings.json` | 83 Gebäude: Größe, Stufen, Kosten, Unterhalt, Wirkungen, Voraussetzungen, Lage, Synergien |
| `rules/press.json` | Pressefragen mit Zeitpunkt, Spielsituation, Kategorie und Tonfall |
| `events/*.json` | Zufallsereignisse mit Bedingungen, Antworten, Wirkungen und Erfolgschance |
| `sponsors/*.json` | Fiktive Sponsorenfirmen je Land mit Größe und Werbeplätzen |
| `lang/<code>.json`, `lang/<code>.commentary.json` | Texte der Oberfläche und Kommentarbausteine je Sprache (`de`, `en`) |

Beim Start prüft `DataValidator` die Pakete und bricht mit einer Fehlerliste ab. Geprüft werden unter
anderem Teamzahlen, Pools, Pokaltermine und Relegationsplätze.

### Ligapyramiden (AP 3)

Ein Land beschreibt seine Pyramide als Ebenen mit Divisionen. Formate ändern sich als reines Daten-Update:

- **Staffeln:** `groups` teilt eine Division in Staffeln (je Staffel `teams` Vereine), `assignment` bestimmt die
  Zuteilung: `Fixed` (Vereine behalten ihre Staffel, Neulinge kommen in die Staffel ihrer `region` bzw. die
  nächstgelegene), `Geographic` (jede Saison neu nach `longitude`/`latitude`) oder `Rotating` (ausgelost).
  Vereine stehen in `clubs/*.json` mit der Staffel oder nur der Division als `division`.
- **Bewegungsregeln:** `movements: [{ "positions": "1-2", "to": "Up" }, { "positions": "17-18", "to": "Down" }]`,
  optional `onlySeason` für Übergangsregeln. Die älteren Felder `promotion`/`relegation` werden beim Laden
  darauf abgebildet. Wer keine Lizenz hat oder als zweite Mannschaft nicht aufsteigen darf, gibt seinen Platz an
  den Nächsten ab; Zwangsabsteiger ersetzt der bestplatzierte sportliche Absteiger bzw. ein Nachrücker. Zu- und
  Abgänge je Ebene gleichen sich immer aus.
- **Eigene Regeln je Staffel:** `groups[].movements` ersetzt die Regeln der Division für diese Staffel, z. B. steigen
  in der Regionalliga nur drei Meister direkt auf, die übrigen zwei spielen den vierten Platz aus.
- **Rotation:** `cycle`/`cycleStart` an Bewegungsregeln und Relegationen lassen sie nur jede n-te Saison gelten. So
  wechselt der dritte Direktaufstieg der Regionalliga wie in der Realität jährlich zwischen Nordost (2026/27), Süd
  (Bayern, 2027/28) und Nord (2028/29); West und Südwest steigen immer direkt auf, die beiden anderen Meister spielen
  den vierten Aufsteiger aus. Der Validator prüft jede Saison eines vollen Zyklus.
- **Pool unter der untersten Ebene:** Ein Pool mit `feeds: "<Division>"` nimmt deren Absteiger auf; dafür steigen
  ebenso viele Poolvereine mit Lizenz auf (nach Ruf mit Zufallsanteil, bevorzugt aus der Region des Absteigers).
  Der Pool spielt keine Liga. Ohne `feeds` nimmt er nur am Pokal teil.
- **Pokalplätze:** Haben die Teilnehmerligen mehr Vereine als `teams`, kommen die höheren Ebenen zuerst, innerhalb
  einer Ebene die Vereine mit dem höchsten Ruf.
- **Relegation und Aufstiegsrunden:** `playoffs` mit `entrants` (Liga oder Division und Tabellenplatz; bei einer
  Division aus jeder Staffel) und `places`. Zwei Teilnehmer spielen ein Duell, mehr ein K.-o.-Turnier.
- **Saisonphasen:** `phases` mit einer zweiten Phase nach dem Grunddurchgang, z. B. Meister- und Abstiegsrunde:
  `groups` nach Tabellenplätzen, `rounds`, `pointsCarry` (z. B. 0,5) und `rounding` (`Down`, `Up`, `Nearest`). Die
  Endtabelle reiht die Gruppen hintereinander; Bewegungsregeln und Relegation gelten für sie.
- **Zweite Mannschaften:** Vereine mit `reserveOf` (Schlüssel der ersten Mannschaft). `reserves` im Land regelt den
  Mindestabstand in Ebenen (`minLevelGap`), die Pokalteilnahme und das Spielrecht (`ageLimit`, `overAgePlayers`).
- **Gültigkeit:** `validFrom`/`validTo` je Formatversion; beim Saisonwechsel gilt die passende Version, Ligen und
  Staffeln werden umgebaut. Der Validator prüft für jede Version, dass Zu- und Abgänge je Ebene aufgehen, und
  beim Wechsel, dass die Teamzahlen nach den Übergangsregeln zur neuen Version passen.

- **Englische Wochen:** `season.lastMatchday` (MM-TT) begrenzt die regulären Spieltage; passen sie nicht wöchentlich
  davor, kommen Spieltage am `season.midweekDay` (Standard Dienstag) gleichmäßig verteilt hinzu.
- **Play-offs wie in England:** `seeded` paart die Teilnehmer in der Reihenfolge der `entrants` (3. gegen 6., 4.
  gegen 5.; Rückspiel beim Besserplatzierten), `finalLegs` gibt die Spiele im Finale vor (z. B. 1).

Ein Testland mit vier Ebenen, geteilter 4. Liga (drei Staffeln), Relegation, Aufstiegsrunde und zweiten
Mannschaften liegt unter `tests/Fmh.Tests/Packages/testland` (nur für Tests).

### Länderpakete (AP 4)

Jedes Land ist ein eigenes Paket `data/<land>/` mit `dependsOn: ["base"]` und nur fiktiven Namen. Deutschland
liegt noch im Grundpaket. Die meisten Tests laden nur `base`, die Länder-Tests und die Kalibrierung alle Pakete.

| Land | Ebene 1 | Ebene 2 | Ebene 3 | Ebene 4 | Unterbau | Besonderheiten |
|---|---|---|---|---|---|---|
| Deutschland | 18 | 18 | 20 | Regionalliga 5 × 18 | Oberliga (Pool) | Relegationen, rotierender Regionalliga-Aufstieg, zweite Mannschaften |
| England | 20 | 24 | 24 | 24 | National Non-League (Pool) | 46 Spieltage mit Englischen Wochen, Play-offs 3–6 bzw. 4–7, keine Winterpause |

England (Stand 2025/26): Premier Division ohne Relegation (18–20 steigen ab); First, Second und Third Division
steigen mit 2, 2 bzw. 3 Vereinen direkt auf, dazu je ein Play-off-Sieger; Absteiger 3, 4 und 2 (in den Pool).
Gehälter und Einnahmen nach der Größenordnung des Deloitte Annual Review of Football Finance (Premier League je
Klub rund 315 Mio. £ Umsatz, rund 4 Mrd. £ Lohnkosten insgesamt), umgerechnet in Euro.

## Offene Entscheidungen

- **Lizenz:** Open Source ist gesetzt, die konkrete Lizenz klärt der Projektinhaber später (Vorschlag
  GPL-3.0 für Code, CC BY-SA 4.0 für Daten). Bis dahin gibt es keine Lizenzdatei.
- **Spielstärke-Drift:** Die Stärke der 1. Liga (Durchschnitt der besten elf je Verein) steigt im
  5-Saison-Lauf um etwa 0,14 je Saison; Ziel ist ≤ 0,05. Die CI prüft das noch nicht.
- **Europapokal:** Die europäischen Wettbewerbe laufen vereinfacht ohne eigene Spiele und ohne
  ausländische Vereine. Echte Partien brauchen weitere Länder als Datenpakete.
- **Saisonphasen:** Umgesetzt ist eine zweite Phase je Saison; mehrere Folgephasen (z. B. Play-offs um den Titel nach
  der Meisterrunde) folgen, wenn ein Land sie braucht.
- **Deutschland:** Pokalplätze für Amateure gehen nach Ruf statt über Landespokale. Die fiktiven Ortsnamen sind
  zusammengesetzt; Übereinstimmungen mit kleinen realen Orten sind möglich, bekannte Vereinsorte sind ausgeschlossen.
- **Wirtschaft:** Die 1. Liga erwirtschaftet im Schnitt rund +30 Mio. je Saison (real etwa +15 Mio.), weil die
  KI Überschüsse nur teilweise ausgibt. Bauprojekte, Prämien des Vorstands und Strafen sind noch nicht an die
  neuen Größenordnungen angepasst. Ältere Spielstände behalten ihre gespeicherte Länderkonfiguration (ohne
  Betriebskosten, Sponsorenniveau 1,0) und ihre Gehälter, rechnen aber Sponsoren, Marktwerte und neue Verträge
  nach den neuen Formeln und werden dadurch reicher; eine Migration der Länderdaten im Spielstand ist offen.
- **England:** Ein Pokal (National Cup mit 64 Plätzen ab der 3. Runde); der Ligapokal fehlt. Beträge stehen in
  Euro.
  Lizenzierung läuft wie in Deutschland mit großzügigen Eigenkapitalgrenzen, statt der englischen PSR-Regeln.
  Die fiktiven Ortsnamen sind zusammengesetzt; Übereinstimmungen mit kleinen realen Orten sind möglich.
- **Karten in unteren Ligen:** Über alle vier Ebenen zeigt die Engine 3,2 Gelbe und 0,13 Rote Karten je Spiel
  (Ziel 3,8 bzw. 0,2), weil schwächere Spieler seltener in Zweikämpfe kommen. Die Kalibrierung prüft deshalb
  Ebene 1–2 (`--max-level`); ob untere Ligen eigene Zielwerte bekommen, ist offen.
- **Zweite Mannschaften als eigene Vereine:** Eine zweite Mannschaft ist bisher ein eigener Verein mit Verweis
  `reserveOf` (eigener Kader, eigene Finanzen). Die Mannschaft eines Vereins (`Team`) übernimmt das, sobald
  Spiele Mannschaften statt Vereine referenzieren.
