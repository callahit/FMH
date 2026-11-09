# Abnahme Textmodus

Stand: aktueller `master` plus diese Änderungen. Geprüft wurden die Texte, die seltenen Ereignisse, der Statistikvergleich gegen die schnelle Engine (1.000 Spiele, feste Seeds), der Gleichlauf von Ereignisstrom und Ergebnis und ein beobachtetes Karrierespiel über die App-Schicht. Ein Beispielprotokoll liegt in `docs/textmodus-beispiel.txt` (SV Nordheide gegen VfL Moorbach, Seed der Testkarriere 4711).

## Ergebnis

Der Textmodus ist abnahmefähig. Die fünf Prüfpunkte sind bestanden. Die schnelle Engine und ihre Golden-Master-Hashes sind unverändert. Spielstände bleiben kompatibel: es gibt keine Schemaänderung.

| Prüfpunkt | Status |
| --- | --- |
| 1 Texte: Platzhalter, Kodierung, kein Widerspruch zum Spielstand | bestanden |
| 2 Seltene Ereignisse: erreichbar, Bedingungen, Nachspielzeit, Insider aus | bestanden |
| 3 Statistik gegen die schnelle Engine, 1.000 Spiele, etwa ±10 % | bestanden |
| 4 Konsistenz Ereignisstrom und Determinismus | bestanden |
| 5 Beobachtetes Spiel über die App-Schicht | bestanden |

## Gefundene und behobene Fehler

| Fehler | Wirkung | Behebung |
| --- | --- | --- |
| „Tor!“ stand auf Elternknoten, bevor Schiri, Abseits oder die Linie den Treffer zurücknahmen. Die Zeile wurde als normales Angriffsereignis gezeigt, der Spielstand blieb gleich. | Torjubel ohne Tor | Die Zeile wird gehalten und nur an ein echtes Tor-Ereignis gehängt. Fällt kein Tor, erscheint sie nicht. |
| Pfosten und Latte zählten als Schuss aufs Tor. Knoten mit zugleich „aufs Tor“ und „vorbei“ ebenso. | Statistik und Ereignis widersprachen sich und der schnellen Engine | Aufs Tor ist nur noch ein Tor oder eine gehaltene Chance, passend zum `ShotOutcome` der Zeile. |
| Volle Ecken erhöhten die Eckenstatistik, ohne immer ein Eck-Ereignis zu schreiben. Foultexte im Baum erzeugten ein zusätzliches Foul-Ereignis. | Ereignisstrom und Endstatistik drifteten | Ecken und Fouls zählt nur die Ereignisschicht, genau einmal je Vorgang. Der Baum liefert den Text. |
| Platzverweis und Verletzungsauswechslung liefen vor dem Text. Die Zeile nannte danach einen anderen oder gar keinen Spieler, das Ereignis den bereits vom Platz gestellten. | Falscher oder fehlender Name bei Karte und Verletzung | Der Spieler bleibt bis nach dem Text auf dem Platz, dann erst Sperre oder Wechsel. |
| `1-ST1` bei nur einem Stürmer wiederholte denselben Nachnamen. | Spieler, den es in der Szene nicht gibt | Fehlt der weitere Spieler, steht „ein weiterer Spieler“. |
| Wetter- und Flutlichtzeilen kamen bei Sonne vor. Der Rollen-Fallback und die kurzen Abschlüsse ignorierten Byte 27. | „Nachthimmel“ ohne Flutlicht, nasser Rasen bei Sonne | Wetter und Flutlicht bleiben ein harter Filter, auch im Fallback und bei Auszügen aus der Bibliothek. |
| Nachspielzeit wurde festgeschrieben, bevor seltene Unterbrechungen im letzten Zeitabschnitt gezählt waren, und der Aufschlag blieb in der nächsten Halbzeit hängen. | Flutlichtausfall verlängerte die Nachspielzeit nicht zuverlässig | Die Ansage enthält alle geplanten Unterbrechungen dieser Halbzeit. Die Halbzeit endet erst, nachdem fällige Texte gelaufen sind. |
| Megaphon endete bei ausgeschalteten Easter Eggs mit „und singt:“. Fortsetzungen (Kairo-Fahne, Geburtstagslied, Gerald) waren auch bei eingeschalteten Easter Eggs unerreichbar. | Kaputte Zeile, Insider-Ketten tot | Die Kette wird ausgegeben. Ohne Easter Eggs wird der hängende Doppelpunkt zu einem vollständigen Satz. |
| Tippfehler „Flutlich“ in `scene-tree.json`, obwohl `Textm.txt` schon „Flutlicht“ sagt. | Kaputtes Wort | Asset an die Quelle angeglichen. |
| „Gelbe Karte für OFFENDER REASON!“ wurde zu „… Hahn Foul!“. | Kaputte Kartenzeile | Die Gründe lauten jetzt „wegen eines Fouls“, „wegen Handspiels“ und so weiter. |

Nicht angefasst: die schnelle Engine, `golden.json`, Persistenzschema.

## 1 Texte

CP1252 aus `Textm.txt` und `scene-tree.json` stimmen überein, inklusive ä, ö, ü, ß. Zeile 8618 lautet „Das Flutlicht wird schwächer …“. Es gibt keine Ersetzungszeichen und keine leere Textzeile im Asset.

Platzhalter `1-ST`, `2-GK1`, `REF`, `OFFENDER`, `VICTIM`, `REASON`, `INJURY` werden ersetzt. Vereinsnamen sind im Original keine Platzhalter; die Partie nennt die beiden Vereine nur in der Protokollüberschrift. Der Schiedsrichtername kommt aus der Partie (`Ali Stein` im Beispiel).

Über die Stichprobe (Sonne, Bewölkung, Regen, Schnee, Wind, mit und ohne Flutlicht) gilt:

- keine offene Platzhalter-Zeichenkette
- keine Zeile, die mit einem Doppelpunkt endet, solange Easter Eggs aus sind
- Torjubel nur auf Tor-Ereignissen, und dort steigt der Spielstand
- genannte Nachnamen gehören zu Spielern, die in diesem Moment auf dem Platz stehen, oder zum gerade ausgeführten Wechsel
- Torschütze ist die angreifende Mannschaft, beim Eigentor die andere
- kein Easter-Egg-Name

Gelegentlich steht „der Stürmer“ oder „ein weiterer Spieler“, wenn die Formation die Rolle nicht hergibt. Das ist ein Ersatzname, kein falscher Kaderspieler.

## 2 Seltene Ereignisse

`SceneWeightOptions.EasterEggs` ist standardmäßig aus. In den Läufen ohne diese Option fehlen Karim Saad, Marcel Kruse, Nick Pothmann, Rolf Langenberg, Jörg Rohrer, Noam Salomon, das Geburtstagslied und Geralds Banner.

Alle 17 gewöhnlichen Schlüssel kommen in einem Regen-Flutlicht-Lauf mit erhöhter Rate vor: Gegenstände, Hund, Flitzer, Donner, Vögel, Elster, Tiefflieger, Flutlicht, Feuerwerk, Megaphon, Banner, beide Hubschrauber, Schuhwechsel Stürmer und Innenverteidiger, Hose, Trikot.

| Text | Bedingung im Lauf | Ergebnis |
| --- | --- | --- |
| Donner, Gewitter | nur Regen oder Wind | eingehalten |
| Fußballschuhe | nur Regen oder Schnee | eingehalten |
| Flutlichtausfall und Blendung, Nachthimmel | nur bei Flutlicht | eingehalten |
| Eis, glatter Boden | nur Schnee | eingehalten |
| Nasser Rasen, glitschiger Ball | nur Regen oder Schnee | eingehalten |

Ein Flutlichtausfall hebt die Nachspielzeit dieser Halbzeit auf mindestens 6 Minuten. Ohne seltene Texte bleibt die Ansage bei 1 bis 3 Minuten und springt nicht in die nächste Halbzeit. Mit Easter Eggs sind Geburtstagslied, Kairo-Fahne und „Alles Gute zum Geburtstag, Gerald“ erreichbar; das Megaphon darf dann mit „singt:“ auf die nächste Zeile zeigen.

## 3 Statistik

1.000 Ligapartien, gleiche Paarungen und Seeds, Textmodus gegen die schnelle Engine. Summe beider Teams je Spiel. Der Test `OneThousandMatchesStayNearTheFastEngine` bricht außerhalb von ±10 % ab.

| Kennzahl | Textmodus | schnelle Engine | Abweichung |
| --- | ---: | ---: | ---: |
| Tore | 3,14 | 3,07 | +2,3 % |
| Schüsse | 26,81 | 27,70 | −3,2 % |
| davon aufs Tor | 10,53 | 10,39 | +1,3 % |
| xG | 3,11 | 3,19 | −2,5 % |
| Ecken | 9,44 | 9,04 | +4,4 % |
| Fouls | 22,30 | 21,27 | +4,8 % |
| Abseits | 7,19 | 6,76 | +6,4 % |
| Gelb | 3,56 | 3,57 | −0,3 % |
| Rot | 0,16 | 0,16 | 0 % |
| Verletzungen | 0,71 | 0,68 | +4,4 % |

Die größte Abweichung ist Abseits mit +6,4 %. Stellschrauben nur im Szenenmodus: `FoulsPerMatch` 28,8, `CornersPerMatch` 14,6, `RedPerFoul` 0,0063. Die Golden-Master-Referenz der schnellen Engine wurde nicht neu gesetzt.

## 4 Konsistenz und Determinismus

Für jedes Spiel der Stichprobe gilt:

- Endstand gleich der Zahl der Tor-Ereignisse, je Mannschaft
- Spielertore plus Eigentore gleich dem Endstand
- Schüsse, aufs Tor, xG, Ecken, Fouls, Abseits, Gelb, Rot und Verletzungen gleich den zugehörigen Ereignissen
- der im Ereignis mitgeführte Spielstand folgt den Toren und springt sonst nicht
- zwei Läufe mit demselben `MatchSetup` erzeugen denselben Ereignisstrom einschließlich Text, Spieler, Spielstand und xG

## 5 App-Schicht

`WatchedCareerMatchUsesTheTextEngine` startet eine Karriere, stellt den Darstellungsmodus auf Textmodus, spielt das anstehende Spiel des Menschen über `MatchViewModel` (Anpfiff, Kabinenansprache, ein paar Live-Takte, dann zu Ende) und prüft:

- die Simulation ist ein `SceneSimulation`, nicht die schnelle Engine
- das gespeicherte Ergebnis trägt dieselben Tore
- der Ticker enthält die Szenenzeugnisse, ohne offene Platzhalter, Easter Eggs oder hängende Doppelpunkte

Das Beispielprotokoll ist ein zweiter, kopfloser Lauf: Grunddatenpaket, Seed 4711, nächstes Spiel von Nordheide, Textmodus. Es ist nicht derselbe Text wie der Oberflächentest, der alle Datenpakete lädt. Anfang und zwei Tore:

```text
SV Nordheide – VfL Moorbach
Schiedsrichter: Ali Stein
Wetter: Rain, Flutlicht: nein

1' | Petersen spielt den Ball von rechts in die Spitze...
39' | Tor!
52' | Tor!
66' | Gelbe Karte für Hahn wegen eines Fouls!
```

Endstand in der Datei: 0:2. Er wird mit dem Ereignisstrom geprüft, nicht aus dem Text geraten.

## Tests

Lokal `dotnet test` Release: **283 bestanden, 0 fehlgeschlagen, 0 übersprungen** (10 min 23 s). Darin:

| Suite | Inhalt |
| --- | --- |
| `TextmodusAbnahmeTests` | Kodierung, Platzhalter, Wetterstichprobe, Konsistenz, Determinismus, seltene Texte, Nachspielzeit, Easter Eggs, Beispielprotokoll |
| `SceneCalibrationTests` | 1.000 Spiele, Band ±10 % |
| `SceneRareTests`, `SceneTreeTests` | Import, Gewichte, Erreichbarkeit |
| `WatchedCareerMatchUsesTheTextEngine` | ein beobachtetes Karrierespiel |
| Golden Master | Referenz unverändert, nicht neu gesetzt |

Ubuntu- und Windows-CI laufen auf dem Pull Request.

## Offen

- Die FM2002-Gewichtsformel bleibt eine Kalibrierung (`SceneWeightOptions`), kein belegtes Original.
- Bytes b8/b9, b20, b22, b28, b31 und b34 sind nur teilweise oder gar nicht angeschlossen.
- `REASON`- und `INJURY`-Texte fehlen im Original. Die Formulierungen sind die Zuordnung aus dem Katalog.
- Libero, Wingback und „ANC“ werden aus der FMH-Formation abgeleitet.
- Flutlicht ist weiterhin die Monatsannahme November bis Februar, jetzt aber wirklich eine harte Bedingung.
- Fehlt eine Rolle, erscheint ein Gattungsname statt eines Kaderspielers. „ein weiterer Spieler“ wird nicht dekliniert.
- 2D-Choreografie und ein Motorwechsel mitten im Spiel sind nicht Teil dieses MVP.
- Der Golden Master der schnellen Engine beweist die Bitgleichheit der Hintergrundspiele. Einen eigenen Szenen-Golden-Master über 1.000 Spiele gibt es nicht; Determinismus ist je Seed geprüft.
