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
dotnet test                                          # 199 Tests, davon 14 Oberflächentests ohne Bildschirm
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
    - mit dem Spieler: Laufzeit, Gehalt, Handgeld, Prämien, Ausstiegsklausel, Auf- und Abstiegsklausel
      (Gehalt −% bei Abstieg, +% bei Aufstieg), jährliche Erhöhung, Kaderstatus.
  - Leihen mit Gehaltsanteil, Kaufoption oder -pflicht und Rückholklausel.
  - Vorverträge, ablösefreie Spieler, Vertragsverlängerungen und Beraterhonorar.
  - Verweigerungsgründe: Reputation, Liga, Konkurrenz, Vereinstreue.
  - Die KI kauft, verleiht, sichert sich Vorverträge und bietet für eigene Spieler; solche Angebote
    sind Pflicht-Entscheidungen im Posteingang.
- **Scouting:** Kenntnisstand je Spieler (0–100 %). Fremde Spieler zeigen Attribute als Spanne.
  Scouts beobachten Spieler, durchsuchen Ligen nach Kriterien (Berichte A–D mit Sternen für Stärke
  und Potenzial) oder analysieren den nächsten Gegner.
- **Pokal und Relegation:**
  - Mehrere Pokale je Land, siehe [Pokale](#pokale): in Deutschland Supercup, Pokal und fünf Verbandspokale;
    in England Supercup, National Cup, League Cup und League Trophy; in Spanien Supercopa und Copa Nacional; in Italien Supercoppa und Coppa Nazionale; in Frankreich Trophée des Champions und Coupe Nationale. Freilose und Heimrecht für Unterklassige;
    Verlängerung und Elfmeterschießen; neutrales Finale.
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
    Fernsehgelder in zehn Monatsraten (August–Mai), Grundbetrag und Leistungsanteil nach dem aktuellen Tabellenplatz;
    Sponsoren; Catering, Fanartikel und Gelände;
    Pokal- und Relegationsprämien.
  - Ausgaben: Gehälter, Personal (Marktgehalt mal `wageLevel` der Spielklasse, wie bei Spielern), Unterhalt, Reisen, Prämien, Zinsen und Betriebskosten (Verwaltung,
    Spielbetrieb, Personal außerhalb des Kaders) als Anteil der laufenden Einnahmen (`operatingCostShare`).
  - Gehälter (siehe „Wirtschaft“ unten): Marktgehalt nach Stärke, angepasst an das Gehaltsniveau der
    Spielklasse (`wageLevel`) und begrenzt durch ihr Spitzengehalt (`maxAnnualWage`).
  - **Auf- und Abstiegsklauseln** in allen Verträgen. Wechselt der Verein die Spielklasse, gilt je Ebene:
    - Spieler: Gehalt nach der Klausel im Vertrag. Sie ist verhandelbar, ein höherer Abschlag als üblich kostet je
      Prozentpunkt 0,3 % mehr Grundgehalt.
    - Personal und Manager: übliche Klausel der bisherigen Spielklasse.
    - Sponsoren: Zahlung und Bonus nach der Klausel im Sponsorenvertrag (Deutschland −40/+25 %, England −40/+30 %, Spanien, Italien und Frankreich −40/+25 %).
    - Übliche Werte stehen je Spielklasse in `relegationWagePercent`/`promotionWagePercent`, zum Beispiel
      Deutschland: 1. Liga −40 %, 2. Liga −50/+40 %, 3. Liga −70/+80 %, Regionalliga −50/+100 %, Oberliga +60 %.
    - Verträge aus älteren Spielständen ohne Klausel folgen wie bisher dem Gehaltsniveau der neuen Spielklasse.
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
    Mensch wird entlassen. Vorher nimmt die KI zum Monatsersten einen Kredit, sobald der halbe Kreditrahmen verbraucht
    ist und die Kreditlinie (1,5 Jahreseinnahmen abzüglich laufender Kredite) reicht.
  - KI und Gehaltsbudget: Vereinslose verpflichtet die KI nur im Budget, außer der Spieltagskader ist nicht mehr voll.
    Liegen die Gehälter mehr als 10 % über dem Budget, setzt sie den bestbezahlten Spieler außerhalb der ersten Elf auf
    die Transferliste.
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
      Europa-Pokal, Platz 6 im Conference-Pokal. In England geht der Conference-Platz an den Sieger des
      League Cup statt an den Sechsten (`europe.cupPlaces`). Spanien, Italien und Frankreich: Platz 1–4, 5 und 6 wie in Deutschland.
    - Ohne Zulassung rückt der nächste Verein nach. Ist ein Pokalsieger schon qualifiziert oder ohne
      Lizenz, geht sein Platz an den nächsten Ligaplatz.
    - Die Wettbewerbe (Champions-, Europa-, Conference-Pokal) laufen mit echten Spielen (AP 5):
      Qualifikationsrunden (Hin und Rück), Ligaphase und K.-o. bis zum Finale. Format in
      `data/base/competitions/europe.json`. Teilnehmer aus allen geladenen Ländern; Einstieg nach
      Länderkoeffizient. Termine Di–Do, ohne Kollision mit Ligaspielen. Start- und Rundenprämien,
      Fanstimmung und Vereinsruf steigen.
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

### Pokale

Jedes Land hat beliebig viele Pokale (`cups` in `countries/*.json`). Ein Pokal nimmt Vereine über Startplätze
(`entries`) auf und füllt freie Plätze bis `teams` aus `participants` auf (höhere Ebenen und höherer Ruf zuerst):

- **Startplätze** (`entries`, in dieser Reihenfolge, ohne Doppelte):
  - Ligen nach der Abschlusstabelle der Vorsaison (`previousSeason`, `count`) oder nach aktueller Zugehörigkeit.
  - Bestplatzierte eines anderen Pokals der Vorsaison (`qualifiersOf`): Sieger, Finalist, dann je Runde die
    Ausgeschiedenen (höhere Ebene, höherer Ruf zuerst). Ist ein Verein schon dabei, rückt der nächste nach.
  - Nur europäisch spielende Vereine oder nur die übrigen (`europe`).
  - Einstiegsrunde (`round`). Der Rundenplan rechnet vom Finale rückwärts und gibt Freilose an die Stärksten.
- **Weitere Regeln:** `region` (Verbandspokal), `roundLegs` (Spiele je Runde), `fixedPairing` mit `homeEntry`
  (Supercup ohne Auslosung) und `prizes`/`winnerPrize` (sonst die Prämien des Landes).

| Land | Pokal | Teilnehmer |
|---|---|---|
| Deutschland | Supercup | Pokalsieger gegen Meister der Vorsaison (Doublesieger: gegen den Zweiten), beim Meister |
| | Pokal (64) | 36 Vereine der 1. und 2. Liga der Vorsaison, die ersten vier der 3. Liga, 24 über die Verbandspokale |
| | Verbandspokal Nord, Nordost, West, Südwest, Süd | Vereine der 3. Liga, Regionalliga und Oberliga der Region; 5, 6, 4, 7 bzw. 2 Plätze im Pokal der nächsten Saison (wie die realen Landesverbände je Regionalliga-Region) |
| England | Super Cup | Sieger des National Cup gegen den Meister, neutral |
| | National Cup | Third, Second Division und National League ab Runde 1, Premier und First Division ab Runde 3 |
| | League Cup | First bis Third Division ab Runde 1, Premier Division ab Runde 2, Europapokalteilnehmer ab Runde 3; Halbfinale mit Hin- und Rückspiel; Sieger in den Conference-Pokal |
| | League Trophy | Second und Third Division |
| Spanien | Supercopa (4) | Finalisten der Copa Nacional und die ersten zwei der Primera División (bei Überschneidung der Nächste), Halbfinale und Finale neutral; das Finale eine Woche nach dem Halbfinale (real am Wochenende danach) |
| | Copa Nacional | 112 Vereine ab Runde 1 (Primera División bis Tercera Federación, ohne Filiales, Heimrecht für Unterklassige), die ersten vier der Primera División der Vorsaison ab Runde 3 (Runde der letzten 32, wie die Supercopa-Teilnehmer); Halbfinale mit Hin- und Rückspiel |
| Italien | Supercoppa (4) | Die Finalisten der Coppa und die ersten zwei der Serie A (bei Überschneidung der nächste der Serie A), neutral |
| | Coppa Nazionale (44) | 4 Vereine der Serie B und 4 der Serie C in Runde 1, 12 der Serie A und 16 der Serie B ab Runde 2, die besten acht der Serie A ab dem Achtelfinale; Heimrecht für den höher eingestuften Verein, Halbfinale mit Hin- und Rückspiel |
| Frankreich | Trophée des Champions | Meister gegen Sieger der Coupe (Doublesieger: gegen den Zweiten), neutral |
| | Coupe Nationale | Ligue 3, National 1 und 2 ab Runde 1 (ohne zweite Mannschaften, Heimrecht für Unterklassige), Ligue 2 ab Runde 2, Ligue 1 ab der Runde der letzten 64 |

Spielstände: Neue Felder haben Standardwerte, ältere Spielstände spielen mit ihrer gespeicherten Pokalkonfiguration
weiter (ein Pokal je Land).

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
  `sponsorLevel`, `merchLevel`, `operatingCostShare`, `academyBudgetFactor`, `squadSize` und `targetAnnualWage` als
  Kalibrierungsziel. Je Land gelten `cateringPerSpectator` und `merchPerSpectator`; `merchLevel` (Standard 1,0) senkt
  die Fanartikel je Zuschauer einer Spielklasse, z. B. in der Segunda División. KI-Vereine übernehmen nach Auf- und Abstieg die
  Eintrittspreise ihrer neuen Spielklasse.
- **Ligazahlen** (`finance.divisions.*.reference`): Erträge und Aufwand je Verein ohne Transfers, in den Kategorien
  des DFL-Wirtschaftsreports, jeweils mit Quelle und Kennzeichen „amtlich“. Für 1. und 2. Liga stammen die Werte
  aus dem DFL-Wirtschaftsreport 24/25, für die 3. Liga aus dem DFB-Saisonreport 2023/24 (nur Summe Erträge und
  Personal). Für die Premier Division gibt es keinen amtlichen Ligabericht. Hier dienen die Zahlen aus Deloittes
  [Annual Review of Football Finance 2026](https://www.deloitte.com/uk/en/services/consulting/research/annual-review-of-football-finance-premier-league-clubs.html)
  als Ziel (2024/25: 6,8 Mrd. £ Umsatz, davon 3,4 Mrd. £ TV; umgerechnet zu 0,87 £ je Euro), ausdrücklich als nicht
  amtlich gekennzeichnet. Derselbe Bericht liefert die Football-League-Zahlen 2024/25: Championship je Klub 39 Mio. £
  Umsatz (Spieltag 8,7, TV einschließlich Fallschirmzahlungen 19,2), League One 11,7 Mio. £, League Two 6,6 Mio. £.
  Die Lohnkosten (Premier League 4,4 Mrd. £, Championship 903 Mio. £ = 96 % des Umsatzes, League One 94 %, League Two
  74 %) umfassen bei Deloitte alle Beschäftigten und sind deshalb nicht direkt mit dem Kaderaufwand vergleichbar; sie
  werden nicht geprüft.
  Zuordnung der Buchungen: Spielertrag = Tickets und Dauerkarten; Werbung = Sponsoren; mediale Verwertung = TV und
  Europapokal; Merchandising = Fanartikel; Sonstiges = Catering, Mitglieder, Gelände und Prämien; Personal
  Spielbetrieb = Gehälter, Prämien und Trainerstab; Verwaltung, Spielbetrieb und Sonstiges = Betriebskosten,
  Instandhaltung und Reisen; Nachwuchs = Budget des Leistungszentrums (bei der DFL inklusive Amateure und Frauen).
  Spanien: amtliche Zahlen aus dem [LALIGA Financial Report 2024/25](https://assets.laliga.com/assets/2026/04/13/originals/dd7aec50d2ee10d78a8f409e609c34a5.pdf)
  (Tabellen 20 ff.), je Klub: Primera División (LALIGA EA Sports) Betriebserträge 206,1 Mio. €, davon Netto-Umsatz
  199,4 (Spieltag 40,8, TV 64,4 plus UEFA 20,3, Sponsoring und Werbung 44,1, Fanartikel 15,8), Kaderaufwand (Spieler
  und Trainerstab der ersten und der übrigen Mannschaften) 106,2 Mio. €; Segunda División (LALIGA HYPERMOTION)
  Betriebserträge 18,1 Mio. €, davon Netto-Umsatz 15,3 (Spieltag 3,9, TV 6,5, Sponsoring und Werbung 3,6, Fanartikel
  1,0), Kaderaufwand 10,8 Mio. €. Verglichen wird mit den Betriebserträgen, weil auch die DFL-Summe sonstige Erträge
  enthält. Die Fanartikel der Segunda prüft die Kalibrierung nur in der Summe, weil der Fanartikelerlös je Zuschauer
  für das ganze Land gilt und die Segunda real weit unter diesem Wert liegt. Italien: Für die Serie A gibt es keinen amtlichen
  Ligabericht; Ziel sind Deloittes Zahlen 2024/25 (3,0 Mrd. € Umsatz, davon 0,5 Spieltag und 1,5 TV einschließlich UEFA),
  als nicht amtlich gekennzeichnet. Serie B und C nach dem ReportCalcio der FIGC: Serie B 2023/24 Wert der Produktion
  483 Mio. € und Personalkosten 394 Mio. € (20 Klubs); ohne Spielerverkäufe (2022/23 rund 15 % des Werts) geschätzt
  20,8 Mio. € je Klub. Serie C 2022/23 je Klub 3,6 Mio. €, Gehälter rund 90 % davon (im Spiel ohne Zuschüsse der Eigentümer niedriger,
  Gehaltsziel 80 Tsd. statt real rund 120 Tsd.). Frankreich: amtliche Zahlen der DNCG
  für 2024/25 (laut Pressezusammenfassungen des Jahresberichts): Ligue 1 Erträge ohne Transfers 2.161 Mio. € für 18
  Klubs (120,1 je Klub), davon Ticketing 252 Mio. € (14,0 je Klub); Lohnsumme 51,7 % von 3.350 Mio. € Aufwand (96,2 je
  Klub). Ligue 2 Erträge 202 Mio. € (11,2 je Klub). Für die Federación-Ligen gibt es keinen Ligabericht.
- **Lizenz und TV-Geld:** Der Leistungsanteil der TV-Gelder fließt in zehn Monatsraten nach dem aktuellen
  Tabellenplatz (bis Oktober 2026 erst zum Saisonende; die Lücke trieb vor allem in Spanien Vereine in die Insolvenz,
  Review F-01). Bei der Lizenzprüfung zählen die noch ausstehenden Raten als Forderung zum Eigenkapital.
- **Kalibrierung:** `fmh-calibrate --season --seasons 5 --strict` prüft je Spielklasse, dass das Ø-Jahresgehalt
  im Korridor ±30 % um das Ziel liegt; erlaubt ist eine Lohnentwicklung von 4 % je Saison (real etwa 4 % im
  Jahr laut DFL 2019/20–2024/25). Außerdem müssen die Ligazahlen im Durchschnitt aller ausgewerteten
  Saisons passen: Summen ±10 %, einzelne Kategorien ±20 % (Review F-03). Abweichende Grenzen stehen je Liga in
  `reference.tolerances` und brauchen eine Begründung in `reference.toleranceNote`. Die Werte jeder Saison werden mit
  ausgegeben; ein einzelner reicher Absteiger verzerrt sonst die zweite Liga einer Saison.
- **Insolvenzen und Lizenz-Punktabzüge:** Der Saisonlauf zählt beides je Land und Ebene. Im Mittel über alle Saisons
  sind höchstens 2 % Insolvenzen je Saison (bezogen auf die Vereinszahl der Ebene) und höchstens ein Lizenz-Punktabzug
  je Ebene und Saison erlaubt.

Die Finanzsimulation über fünf Saisons ergibt folgende Werte je Verein:

| Liga | Ø Jahresgehalt (Ziel) | Kadergehälter | Einnahmen | Saldo pro Saison |
|---|---|---|---|---|
| 1. Liga | 2,8–3,2 Mio. (2,9 Mio.) | 78–84 Mio. | 218–241 Mio. | +7 bis +23 Mio. |
| 2. Liga | 420–540 Tsd. (480 Tsd.) | 12–14 Mio. | 57–61 Mio. | ±0 bis +4 Mio. |
| 3. Liga | 150–185 Tsd. (165 Tsd.) | 3,9–4,5 Mio. | 15–18 Mio. | +0,4 bis +2,7 Mio. |
| Regionalliga | 24–35 Tsd. (25 Tsd.) | 0,6–0,8 Mio. | 2,7–2,9 Mio. | ±0 bis +0,7 Mio. |

Im Vergleich mit dem DFL-Bericht (Mittel über fünf Saisons, Spiel/Bericht in Mio. € je Klub):

| Kategorie | 1. Liga | 2. Liga |
|---|---|---|
| Spielertrag | 29/33 | 15/15 |
| Werbung | 65/64 | 13/12 |
| Mediale Verwertung | 91/95 | 16/16 |
| Merchandising | 14/16 | 5/5 |
| Sonstiges | 28/29 | 9/10 |
| Erträge gesamt | 227/236 | 57/58 |
| Personal Spielbetrieb (mit zweiter Mannschaft) | 86/96 | 14/17 |
| Verwaltung, Spielbetrieb, Sonstiges | 112/108 | 37/39 |
| Nachwuchs | 16/14 | 5/4 |
| Aufwand gesamt | 214/217 | 56/59 |

Darin sind die Budgets der Nachwuchsleistungszentren und die Europapokalprämien bereits enthalten.
Absteiger geraten mit ihren Gehältern ins Minus. Es gibt keine Kettenpleiten. In der 2. Liga bekommen
einige Vereine im Frühjahr Lizenzauflagen, Punktabzüge sind selten. Zum Spielstart gibt es je Verein
rund einen Vereinslosen; mit allen fünf Ländern liegen am Saisonende vor den Karriereenden 500–2.000 Spieler ohne
Vertrag (höchstens rund 10 % der Profis): Wer nach der Jugend nicht übernommen wird, geht meist in den
Amateurfußball, und Vereine mit dünnem Kader verpflichten unter der Saison nach. Die Kennzahlen zeigt
`fmh-calibrate --season --seasons 5` je Liga und Buchungsart an.

**Größe der Spielwelt** (alle Länder, Seed 1, 10 Saisons): Die aktiven Spieler (Profis, Jugend, Vereinslose) wachsen
von 43.400 auf 45.900 (+5,7 %); Amateurvereine nehmen ihren Jugendjahrgang mit 16 auf, die Startjahrgänge entsprechen
dem laufenden Spiel. Spieler im Ruhestand bleiben mit ihrer Laufbahn im Spielstand (nach 10 Saisons rund 54.000); der
Spielstand wächst von 50 auf 112 MB (Ziel laut Konzept 6.2: unter 300 MB). Die Kalibrierung prüft je Saisonende:
aktive Spieler höchstens +10 % gegenüber dem ersten Saisonende, Vereinslose höchstens 12 % der Profis.

## Umfang nach Kapiteln des Fachkonzepts

| Kapitel | Umgesetzt | Folgt laut Roadmap |
|---|---|---|
| 2 Spielmodi und Spielstart | Karriere mit neuem Spiel (Name, Werdegang, Verein, Seed, Weltregeln), Managerprofil, Speichern, Autosave vor jedem Spieltag in 3 rotierenden Slots, optional Ironman | Schnellsaison, Hot-Seat, Herausforderungen (Phase 4) |
| 3 Spielwelt, Wettbewerbe | Tagesgenauer Kalender, Ligen als Daten, DE/EN/ES/IT/FR bis zur 4. Liga, **nationale Pokale und Relegation**, **echte Europapokale (UCL/UEL/UECL) mit Qualifikation, Ligaphase und K.-o.**, Transferfenster, Pflicht-Entscheidungen, **Ligapyramiden** | Hintergrundländer / EuropeOnly (Welle 3), weitere Länder |
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
| Tore pro Spiel | 2,78 | 2,8 | ± 0,15 |
| Unentschieden | 23,6 % | 25 % | ± 2,5 Pp. |
| Heimsiege | 44,7 % | 45 % | ± 2,5 Pp. |
| Gelbe Karten pro Spiel | 3,94 | 3,8 | ± 0,3 |
| Rote Karten pro Spiel | 0,20 | 0,2 | ± 0,05 |
| Verletzungen pro Team und Spiel | 0,30 | 0,3 | ± 0,05 |

Mit `--strict` endet das Programm mit Exitcode 1, wenn eine Kennzahl außerhalb liegt; die CI nutzt das.
Weitere Prüfungen:

- `--max-level 4` bezieht die unteren Ebenen in die Engine-Kalibrierung ein (Standard: Ebene 1–2, für die die
  Toleranzen gelten).
- `--max-ms 3` prüft zusätzlich die Rechenzeit je Spiel. Sie hängt von der Hardware ab und läuft
  deshalb nicht in der CI (gemessen: 2,4 ms in der Cloud-Umgebung, davon etwa 0,4 ms Aufstellung und
  KI-Taktik, der Rest Engine).
- `--season --seasons 5 --strict` prüft den Karrierelauf: Der langsamste Spieltag muss unter 5 s
  liegen, alle Saisons müssen durchlaufen, Gehälter und Ligazahlen müssen passen und Insolvenzen sowie
  Lizenz-Punktabzüge unter den Grenzen bleiben (siehe Finanzen).
- `--max-drift 0.05` prüft zusätzlich die Ligastärke-Drift (Durchschnitt der besten elf je Verein der 1. Liga);
  die CI ruft den Karrierelauf damit auf. Gemessen: +0,024 je Saison. Dafür holen junge Spieler im Mittel nur ihren
  Altersabschlag auf, Akademietalente erreichen etwa die Stärke ihres Vereins, und der Abbau ab 30 nimmt mit dem
  Alter zu.

Die Gewichte stehen in `data/base/rules/engine.json`.

## Entwicklung

- Arbeit auf Feature-Branches, ein Arbeitspaket je Pull Request (siehe Umsetzungskonzept, Abschnitt 1).
- CI (`.github/workflows/ci.yml`): Build und Tests auf Linux und Windows, Warnungen gelten als Fehler,
  dazu ein Kalibrierungsjob mit Toleranzprüfung (Engine über 10.000 Spiele, Karriere über fünf Saisons mit
  Ligazahlen, Insolvenzen, Mindestkader, Größe der Spielwelt und `--max-drift 0.05`).
- Offene Punkte aus dem Code-Review vom 28.10.2026 stehen mit Status in `docs/review-2026-10-28.md`.
- Keine Länder- oder Wettbewerbscodes im C#-Code; Regeln, Zahlen und Texte gehören in Datenpakete.

## Aufbau

Aufbau nach Umsetzungskonzept, Abschnitt 4: `GameSession` ist eine dünne Fassade über Spielmodulen,
die nur über Ereignisse und Dienst-Schnittstellen miteinander sprechen.

```
src/
  Fmh.Domain/        Reine Datentypen (Model, Match-Modelle), Rng, Formatierung, Texte, Konfigurationstypen,
                     Aufstellungs- und Bewertungsregeln, die die Engine braucht; referenziert nichts
  Fmh.Data/          Datenpakete laden, überlagern und validieren
  Fmh.Rules/         Regelbibliothek ohne Modulzustand: Tabellen, Weltgenerator, Spielergenerator, Entwicklung,
                     Marktwerte, Verhandlungen, Pyramide, Pokale, Spielplan, Meldelisten …
  Fmh.Simulation.Abstractions/
                     Modulvertrag (IGameModule, ModuleContext, DayContext), Ereignisbus mit Ereignissen
                     (DayStarted, MatchPlayed), Dienst-Schnittstellen der Module und gemeinsamer Kern
                     (Spielstand, Zufall, Posteingang); unter 600 Zeilen
  Fmh.Modules/       Je Fachgebiet ein Modul: Spielbetrieb, Saison, Pokale, Europapokal, Transfers, Training,
                     Scouting, Jugend, Finanzen, Wirtschaft, Gelände, Stadionpläne, Personal, Sponsoren,
                     Vorstand, Presse, Ereignisse, Lizenz
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
data/spain/          Länderpaket Spanien (dependsOn base): Ligen mit Gruppen, Filiales, Vereine, Namen, Sponsoren
data/italy/          Länderpaket Italien (dependsOn base): Serie A–C, Serie D als Pool, Under-23-Teams, Namen, Sponsoren
data/france/         Länderpaket Frankreich (dependsOn base): Ligue 1–3, National 1 in Gruppen, National 2 als Pool
```

Regeln, die ein Architekturtest prüft: erlaubte Projektverweise (Konzept 4.1; `Fmh.Rules` nur auf `Fmh.Domain`),
keine Datei in `Fmh.Simulation`, `Fmh.Modules`, `Fmh.MatchEngine` oder `Fmh.Rules` über 400 Zeilen,
`Fmh.Simulation.Abstractions` ohne Spiellogik und unter 600 Zeilen, kein Modul verwendet die Klasse eines anderen.
Ein Verein (`Club`) besitzt Mannschaften (`Team`, je eine Welt `men`/`women`); vereinsweite Daten liegen
am Verein, Kader, Taktik, Training und Liga an der Mannschaft. Bis alle Module direkt mit Mannschaften
arbeiten, leiten die gleichnamigen Vereinseigenschaften an die erste Mannschaft weiter.

**Golden Master** (Konzept 4.5): `GoldenMasterTests` simuliert die Grundwelt (Deutschland) mit Seed 4242 über drei
Saisons, alle ausgelieferten Länderpakete über zwei Saisons und 10.000 Engine-Spiele und vergleicht Hashes je Bereich
mit `tests/Fmh.Tests/GoldenMaster/golden.json` (ein Eintrag je Laufzeitumgebung). Gehasht werden alle Felder der Welt
einschließlich der Länderdaten; ein Test prüft, dass neue Felder der Welt in den Fingerabdruck aufgenommen werden.
Neu gesetzt wird er nur in PRs, die eine Regeländerung ankündigen und im PR-Text begründen:
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
  einer Ebene die Vereine mit dem höchsten Ruf. Startplätze, späterer Einstieg und Qualifikation stehen unter
  [Pokale](#pokale).
- **Relegation und Aufstiegsrunden:** `playoffs` mit `entrants` (Liga oder Division und Tabellenplatz; bei einer
  Division aus jeder Staffel) und `places`. Zwei Teilnehmer spielen ein Duell, mehr ein K.-o.-Turnier.
- **Saisonphasen:** `phases` mit einer zweiten Phase nach dem Grunddurchgang, z. B. Meister- und Abstiegsrunde:
  `groups` nach Tabellenplätzen, `rounds`, `pointsCarry` (z. B. 0,5) und `rounding` (`Down`, `Up`, `Nearest`). Die
  Endtabelle reiht die Gruppen hintereinander; Bewegungsregeln und Relegation gelten für sie.
- **Zweite Mannschaften:** Vereine mit `reserveOf` (Schlüssel der ersten Mannschaft). `reserves` im Land regelt den
  Mindestabstand in Ebenen (`minLevelGap`), die Pokalteilnahme und das Spielrecht (`ageLimit`, `overAgePlayers`).
  Sie haben keine eigene Kasse: Gehälter und Einnahmen bucht der Hauptverein, gekennzeichnet mit dem Namen der
  zweiten Mannschaft. So zählen ihre Spieler zur Gehaltsliste und zum Personalaufwand des Vereins. Eine zweite
  Mannschaft hat kein Transferbudget und ergänzt ihren Kader nur ablösefrei im Rahmen ihrer laufenden Gehälter. Muss
  sie wegen des Abstands zur ersten Mannschaft absteigen, gilt das auch, wenn kein Verein aufsteigen darf; dann rückt
  der bestplatzierte Verein der Ebene darunter ohne Aufstiegslizenz nach.
- **Meldelisten** (`registration` je Liga bzw. Division): Wer nicht gemeldet ist, darf in Liga und Pokal nicht spielen.
  - Ausbildung: Jeder Spieler sammelt zwischen 15 und 21 je Saison eine Ausbildungsspielzeit bei seinem Verein. Ab drei
    Spielzeiten bei Vereinen des Landes gilt er als lokal ausgebildet (Local Player, Homegrown), ab drei beim eigenen
    Verein als im Verein ausgebildet. Spieler, die zu Spielbeginn erzeugt werden, verteilen sich nach `formative`
    (Deutschland und England: 15 % im eigenen Verein, 45 % bei einem anderen Verein des Landes, der Rest im Ausland;
    Spanien 20 % und 50 %, Italien 15 % und 45 %, Frankreich 15 % und 50 %).
  - 1. und 2. Liga (DFL): Liste bis zur Kadergrenze (32), mindestens 8 Local Player, davon 4 aus dem eigenen Verein,
    und 12 Spieler mit deutscher Staatsangehörigkeit.
  - Premier Division: 25 Plätze für Spieler ab 21, höchstens 17 ohne Homegrown-Status; Jüngere ohne Meldeplatz.
  - First Division: wie die Premier Division, Leihspieler brauchen aber immer einen Meldeplatz (Pressequellen).
  - Primera und Segunda División: 25 Plätze; Spieler unter 23 brauchen keinen Platz (in der Realität mit Lizenz der
    Filial-Mannschaft und Rückennummer ab 26).
  - Serie A: 25 Plätze für Spieler ab 22, mindestens acht in Italien ausgebildet, davon vier im eigenen Verein.
  - 3. Liga: mindestens vier U23-Spieler im Spieltagskader (`matchdayMinYoung`); die KI und der Assistent setzen sie auf
    die Bank.
  - Meldungen ändern sich nur bei offenem Transferfenster. Zu Saisonbeginn und im Fenster melden die Vereine nach
    Spielstärke nach. Zum Meldeschluss meldet der Assistent freie Plätze nach und schickt eine Nachricht, wer nicht
    spielberechtigt ist. In der Kaderansicht gibt es die Spalte „Meldung“ und die Schaltfläche „Melden / abmelden“.
  - Spielstand: Abschnitt `players` Version 6 (Spalte `registration`). Ältere Spielstände laden mit allen Spielern
    gemeldet; die Ausbildungsjahre werden beim Öffnen nach den Anteilen der Daten ergänzt.
- **Kadergröße:** `squadSize` je Spielklasse (Standard 25): Deutschland 1. Liga 29, 2. Liga 28, 3. Liga 26; England
  Premier Division 30, First Division 28, Third Division 24; Spanien Primera División 27, Segunda División 26; Italien Serie A 28, Serie B 27. Danach richten sich Kaderaufbau, Nachverpflichtungen der
  KI (bis drei unter der Kadergröße) und Vertragsverlängerungen.
- **Mindestkader:** `minSquad` je Spielklasse (Standard 22, mindestens ein Spieltagskader aus Elf und Bank). Fällt ein
  KI-Verein darunter, verpflichtet er am nächsten Montag bis zum Mindestkader nach (Vereinslose, sonst Talente oder
  Amateure). Die Kalibrierung zählt Vereine unter dem Mindestkader; im Autopiloten hält der eigene Verein ihn mit
  Vereinslosen.
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
| Spanien | 20 | 22 | Primera Federación 2 × 20 | Segunda Federación 5 × 18 | Tercera Federación (Pool, 50) | Direkter Vergleich vor Tordifferenz, Play-offs je Gruppe, Filiales ab Ebene 3, kurze Weihnachtspause |
| Italien | 20 | 20 | Serie C 3 × 20 | – | Serie D (Pool, 50) | Play-off und Play-out der Serie B, Play-off der Serie C, Under-23-Teams in der Serie C |
| Frankreich | 18 | 18 | 18 (Ligue 3) | National 1, 3 × 16 | National 2 (Pool, 50) | Barrages 16. gegen 3. zwischen Ligue 1/2 und Ligue 2/3, zweite Mannschaften in der National 1, keine Meldeliste |

England (Stand 2025/26): Premier Division ohne Relegation (18–20 steigen ab); First, Second und Third Division
steigen mit 2, 2 bzw. 3 Vereinen direkt auf, dazu je ein Play-off-Sieger; Absteiger 3, 4 und 2 (in den Pool).
Gehälter und Einnahmen nach dem Deloitte Annual Review of Football Finance 2026 (siehe Ligazahlen), umgerechnet in
Euro. **Fallschirmzahlungen:** Absteiger aus der Premier Division erhalten drei Saisons lang 55 %, 45 % und 20 % des
TV-Grundbetrags eines Premier-Division-Vereins, nach nur einer Saison in der Premier Division zwei Saisons lang
(`parachuteShares`, `parachuteShortTenure`).

Spanien (Stand 2025/26): Primera División mit drei Absteigern; Segunda División mit 42 Spieltagen (Englische Wochen am
Dienstag, Pokalspiele liegen mittwochs), zwei Direktaufsteigern und Play-off 3–6 mit Hin- und Rückspiel auch im
Finale, vier Absteiger. Primera Federación in zwei Gruppen: Gruppensieger steigen auf, Platz 2–5 spielen je Gruppe
einen Aufstiegsplatz aus, 16–20 steigen ab. Segunda Federación in fünf Gruppen nach Regionen: Sieger direkt, Platz 2–5
je Gruppe im Play-off mit Einzelspielen beim Besserplatzierten, 14–18 in die Tercera Federación. Filiales (zweite
Mannschaften) spielen mindestens eine Ebene unter der ersten Mannschaft und nicht im Pokal. Tabellen: direkter
Vergleich vor Tordifferenz.

Italien (Stand 2025/26): Serie A mit drei Absteigern. Serie B mit zwei Direktaufsteigern, Play-off 3–6 mit Hin- und
Rückspiel (real 3–8 mit Vorrunde), drei Direktabsteigern und Play-out 16. gegen 17. (der Verlierer steigt ab). Serie C
in drei Gruppen nach Regionen (Nord, Mitte, Süd): Gruppensieger direkt, ein vierter Platz im Play-off der drei
Gruppenzweiten und eines Gruppendritten (die Gruppe wechselt jährlich, real 28 Teilnehmer), 18–20 in die Serie D. Die
Under-23-Mannschaften der drei größten Vereine spielen in der Serie C, mindestens zwei Ebenen unter der ersten
Mannschaft. Fallschirmgeld: Absteiger aus der Serie A erhalten eine Saison lang 50 % des TV-Grundbetrags (real 10 bis
25 Mio. € je nach Zahl der Jahre in der Serie A). Tabellen: direkter Vergleich vor Tordifferenz.

Frankreich (Stand 2026/27, erste Saison der Ligue 3): Ligue 1 mit zwei Absteigern und Barrage des 16. gegen den
Dritten der Ligue 2 (real der Sieger eines Play-offs 3–5). Ligue 2 mit zwei Direktaufsteigern, zwei Absteigern und
Barrage des 16. gegen den Dritten der Ligue 3. Ligue 3 mit zwei Direktaufsteigern und drei Absteigern. National 1 (bis
2025 National 2) in drei Gruppen nach Regionen: Sieger steigen auf, 13–16 in die National 2. Zweite Mannschaften der
drei größten Vereine spielen höchstens in der National 1 und nicht im Pokal. Tabellen: Tordifferenz vor direktem
Vergleich.

## Offene Entscheidungen

- **Lizenz:** Open Source ist gesetzt, die konkrete Lizenz klärt der Projektinhaber später (Vorschlag
  GPL-3.0 für Code, CC BY-SA 4.0 für Daten). Bis dahin gibt es keine Lizenzdatei.
- **Europapokal:** Echte Spiele zwischen geladenen Ländern sind umgesetzt (AP 5). Offene Punkte:
  Hintergrundvereine für volle 36er-Ligaphasen, Koeffizienten-Bonusplätze und UEFA-Drop-Ins
  (UCL-Quali-Verlierer → UEL) feiner kalibrieren.
- **Saisonphasen:** Umgesetzt ist eine zweite Phase je Saison; mehrere Folgephasen (z. B. Play-offs um den Titel nach
  der Meisterrunde) folgen, wenn ein Land sie braucht.
- **Deutschland:** Es gibt fünf Verbandspokale (je Regionalliga-Region) statt der 21 Landespokale. In der ersten
  Saison fehlen Vorjahresergebnisse, deshalb gehen die Amateurplätze im Pokal dann nach Ruf. Die fiktiven Ortsnamen sind
  zusammengesetzt; Übereinstimmungen mit kleinen realen Orten sind möglich, bekannte Vereinsorte sind ausgeschlossen.
- **Wirtschaft:** Die 1. Liga erwirtschaftet im Schnitt +7 bis +23 Mio. je Saison (laut DFL ohne Transfers etwa
  +19 Mio.). KI-Vereine stecken ein Viertel ihrer Rücklagen über 25 % der Jahreseinnahmen in die Gehälter;
  der eigene Verein spart im Autopiloten der Kalibrierung weiter an, weil dort niemand Transfers tätigt. Der Personalaufwand liegt mit größeren Kadern und
  den zweiten Mannschaften noch rund 10 % unter dem DFL-Wert. Die Stadien der fiktiven Vereine wurden auf reale
  Größenordnungen gebracht (2. Liga Ø rund 30.000 Plätze). Bauprojekte, Prämien des Vorstands und Strafen sind noch nicht an die
  neuen Größenordnungen angepasst. Beim Laden hebt die Sitzung die Finanz-, Kader- und Nachwuchsregeln
  eines älteren Spielstands auf den aktuellen Datenstand (`CountryDataMigration`); Ligaaufbau, Pokale und Play-offs
  bleiben wie gespeichert. Gehälter laufender Verträge bleiben, bis sie verlängert werden.
- **England:** Der National Cup kennt keine Qualifikationsrunden; die National League steigt mit ihren 24 Vereinen
  in Runde 1 ein. Die League Trophy läuft ohne Gruppenphase und ohne U21-Teams. Beträge stehen in Euro.
  Lizenzierung läuft wie in Deutschland mit großzügigen Eigenkapitalgrenzen, statt der englischen PSR-Regeln.
  Die fiktiven Ortsnamen sind zusammengesetzt; Übereinstimmungen mit kleinen realen Orten sind möglich.
- **Spanien:** Die Play-offs der Federación-Ligen laufen je Gruppe statt gruppenübergreifend ausgelost, die Tercera
  Federación ist ein Pool statt 18 Gruppen. Die Copa Nacional lässt die ersten vier der Vorsaison statt der
  Supercopa-Teilnehmer später einsteigen. Offen sind die Grenze für Spieler ohne EU-Pass (in der Primera drei je
  Kader), die Kostengrenze der LaLiga (Límite de Coste de Plantilla), Ausgleichszahlungen für Absteiger und das
  Hochziehen von Filial-Spielern in die erste Mannschaft. Die fiktiven Ortsnamen sind zusammengesetzt.
- **Italien:** Das Play-off der Serie B hat vier statt sechs Teilnehmer, das der Serie C vier statt 28; Entscheidungsspiele
  bei Punktgleichheit um Titel und Abstieg fehlen. Die Serie B prüft die Kalibrierung nur in Summe und Personalkosten,
  die Serie C ebenso (keine Kategorien im ReportCalcio-Auszug). Offen sind die Liste der Serie B, die Spielzeitregeln
  für junge Spieler in der Serie C und die Zahl älterer Spieler in den Under-23-Mannschaften (angenommen vier).
- **Frankreich:** Die Barrage zwischen Ligue 1 und 2 trägt der Dritte der Ligue 2 statt des Siegers eines Play-offs
  3–5 aus. Es gibt keine Meldeliste (real höchstens vier Spieler ohne EU-Pass) und keine Hilfen für Absteiger. Die DNCG-Zahlen
  stammen aus Pressezusammenfassungen; geprüft werden Summe, Ticketing und Lohnsumme der Ligue 1 sowie die Summe der Ligue 2.
- **Karten in unteren Ligen:** Aggressivität und Dribbling wirken bei Fouls relativ zum eigenen Niveau des Spielers,
  sodass Unterklassige nicht seltener foulen. Über alle vier Ebenen liegen 3,58 Gelbe und 0,17 Rote Karten je Spiel im
  Zielbereich; die dritte und vierte Ebene haben etwas weniger Zweikämpfe und damit weiter etwas weniger Fouls. Tore
  über alle vier Ebenen 2,96 je Spiel (untere Ligen torreicher). Die Toleranzen gelten für Ebene 1–2.
- **Zweite Mannschaften als eigene Vereine:** Eine zweite Mannschaft ist bisher ein eigener Verein mit Verweis
  `reserveOf` (eigener Kader, eigene Finanzen). Die Mannschaft eines Vereins (`Team`) übernimmt das, sobald
  Spiele Mannschaften statt Vereine referenzieren.
