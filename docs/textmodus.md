# Textmodus (FM2002-Szenenbaum)

Angeschaute Spiele können im Darstellungsmodus **Textmodus** über den Szenenbaum aus dem FM2002-Textmodus-Update III (2. November 2001) laufen. Alle anderen Spiele, einschließlich Sofortergebnis und der Hintergrundpartien, bleiben auf der schnellen Engine. Deren Golden Master (10.000 Spiele) ändert sich dadurch nicht.

Die Originaldateien liegen unter `data/textmode/` (`Textm.sav`, `Textm.txt`, `liesmich.txt`). Sie sind nicht geschützt und dürfen im Repository stehen. `scene-tree.json` ist das daraus erzeugte, versionierte Asset (Format 1). Der Parameterkatalog steht in `data/textmode/parameter-katalog.json`. Die Decoder liegen unter `tools/textmode/`.

```bash
python3 tools/textmode/decode_textm.py data/textmode
```

## Format

16 Byte Kopf: 8 Byte Signatur `CE 5D 50 CF 87 8F AA 94`, dann `u32` Version 2, dann `u32` 0.

Danach 9.906 Knoten in Preorder. Je Knoten: `u32` Id, `u32` n, n Zeilennummern in `Textm.txt` (13.432 Zeilen, CP1252), 44 Byte Parameter, `u32` Kinderzahl. Die Wurzel hat 29 Kinder, das sind die Kategorien (Elfmeter bis Sonstige Texte). Byte 26 eines Knotens ist der Index dieser Kategorie für die Folgeszene.

Mehrere Zeilennummern sind gleichwertige Textvarianten und werden gleichverteilt gezogen. Platzhalter: `1-ST`, `2-GK1` (Suffix = weiterer Spieler derselben Rolle), `REF`, `OFFENDER`, `VICTIM`, `REASON`, `INJURY`.

## Parameter, die der MVP benutzt

| Byte | Bedeutung | Verwendung |
| --- | --- | --- |
| 0–4, 2, 3, 6 | bis zu drei Rollen/Attribut-Paare | Gewicht der Kindauswahl |
| 8, 9 | Stimmung Angriff/Abwehr (Hypothese, neutral 5) | weiche Dämpfung |
| 10 | Schussdistanz in Metern, 255 = kein Schuss | xG-Näherung |
| 11 | Passart, Bit 4 = Abseits | Abseitsstatistik, leichte Taktikgewichtung |
| 12 | Abseitsfalle | harter Filter, nur wenn die Abwehr sie stellt |
| 13 | Standardart | xG bei Elfmeter und direktem Freistoß |
| 14–15 | Ergebnis-Flags | Tor, Schuss, aufs Tor, Eigentor, Pfosten, Latte, Ecke, Foul, vorbei |
| 18 | Schiri-Entscheidung | erkennt Foulknoten für Karten |
| 23 | Torart | Schussart und xG |
| 25 | Blatt (Bit 7) und Seite (Bits 0–1) | Pfadende, Heatmap |
| 26 | Folgekategorie 1–29 | Ecke, Freistoß, Elfmeter |
| 27 | Bedingungen | harter Filter: nass, eisig, Flutlicht, Führung, nicht im Elfmeterschießen |
| 29 | Sondertext | nur wenn die Partie das selten gezogene Extra-Ereignis hat |
| 30 | Aktionsart | Pässe, Zweikämpfe |
| 31 | Schiri-Stil (Hypothese) | kleine Gewichtskorrektur nach Strenge |
| 32 | Basisgewicht | Kindauswahl |
| 33 | handelnde Rolle, Bit 7 = angreifendes Team | Rollenfilter und Namen |
| 35–39 | Täter, Grund, Verletzter, Art | Karten- und Verletzungsszene |
| 40–43 | Noteneffekt, neutral 30 | Spielnote |

Attributindex unter 50 wirkt positiv, ab 50 ist der Index `Byte − 50` und der Beitrag ist `21 − Spielerwert`. Der Spielerwert ist das FMH-Attribut 1–20, leicht verschoben um Moral, Form, Tagesform und Kondition. Index 22 ist die versteckte Fairness. Indizes ohne Textbeleg sind in `AttributeMap` als Annahme markiert und dort austauschbar; unbekannte Indizes zählen neutral 10.

## Ablauf

Eine offene Szene wählt die angreifende Mannschaft (Stärke, Mentalität, Spielstand, kleiner Heimvorteil) und dann eine Startkategorie: Flügel, Mitte, Aufbau, langer Ball, Konter, Steilpass, Abwehrfehler. Darüber liegt eine Ereignisschicht. Sie lost Fouls, Abseits, Ecken, Freistöße, Elfmeter, Einwürfe, Karten, Verletzungen und kurze Abschlüsse mit Raten aus und startet danach die passende Kategorie:

| Ereignis | Kategorie |
| --- | --- |
| Elfmeter | 1 |
| Freistoß links / zentral / rechts | 2, 4, 5 |
| Eckball | 13 oder 14, ein Teil nur als kurze Ansage, `CornerFullShare` spielt den Baum zu Ende |
| Einwurf | 15 oder 16, ein Blatt ohne Tor |
| Karten | 23–28 |
| Verletzung | 22 |
| Kurzer Abschluss | ein Schussblatt aus den Angriffskategorien, meist vorbei oder gehalten |

Die Raten hängen an Zweikampfhärte, Pressing, Aggressivität, Abseitsfalle und Abwehrlinie, Flügelspiel und Flanken, Abschluss und Torwartreflexen, der Schiri-Strenge, dem Spielstand und der Verletzungsanfälligkeit. Unterbrechungen aus den sonstigen Texten verlängern die Nachspielzeit.

Die Kindauswahl im Baum filtert hart: die Rolle muss besetzt sein, Wetter und die belegten Bedingungsbits müssen passen. Danach

```text
w = Grundgewicht + b32 + Attributskala · (bis zu drei Terme)
```

Kinder mit `b32 = 0` und einem Modifikator haben mindestens das Modifikator-Minimum. Eingemessene Defaults: Grundgewicht 1, Attributskala 0,22, Modifikator-Minimum 1, Stimmungsdämpfung 0,06. Ab Attributbyte 50 wirkt der Index umgekehrt (`21 − Spielerwert`). Die Formel steht in `SceneWeights` und ist über `SceneWeightOptions` austauschbar.

Ein Schritt der Oberfläche ist eine Textzeile. Tore, Schüsse, xG, Ecken, Fouls, Abseits, Karten und Verletzungen landen im bestehenden Spielergebnis, inklusive Eigentor.

Flutlicht ist eine Annahme: November bis Februar. Byte 32 (unklar) filtert nicht. Bytes 20, 22 und 28 bleiben unbenutzt.

## Seltene Texte

Kategorie 29 hat 19 Kinder. Gemessen sind 0,28 Ereignisse je Spiel, also etwa eines alle dreieinhalb Spiele (`RarePerMatch = 0,30`). In 1.000 Spielen mit verteilten Monaten kam jedes der 17 gewöhnlichen Ereignisse mindestens einmal vor. Bedingungen:

| Text | Bedingung |
| --- | --- |
| Gegenstände, Flitzer, Feuerwerk | immer, häufiger bei Derby, großem Stadion, hoher Reputation und schlechter Fanstimmung |
| Polizeihund, Elster, Vögel, Tiefflieger, Hubschrauber | immer, selten |
| Megaphon, Riesenbanner | immer; die Geburtstagszeilen dahinter nur mit Easter Eggs |
| Donnergrollen | Regen oder Wind |
| Flutlichtausfall | nur bei Flutlicht |
| Schuhwechsel | Regen oder Schnee |
| zerrissene Hose oder zerrissenes Trikot | immer |

Unterbrechungen (Gegenstände, Hund, Flitzer, Elster, Flutlicht, Feuerwerk, Megaphon, Schuhe, Hose, Trikot) erhöhen die Nachspielzeit. Flutlichtausfall wiegt am schwersten.

Easter Eggs (Karim Saad, Marcel Kruse, Nick Pothmann, Geburtstag von Rolf und Gerald) bleiben aus, solange `SceneWeightOptions.EasterEggs` nicht gesetzt ist.

Mitten in Spielzügen, nicht als eigene Kategorie, und nur wenn das Wetter passt:

- Nasser Rasen, wegrutschender Ball, länger werdender Pass: Bedingung nass, also Regen oder Schnee. Erreichbar.
- Eisiger oder glatter Boden: nur Schnee. Erreichbar.
- Torwart vom Flutlicht geblendet: nur bei Flutlicht, in Freistoß- und Angriffsbäumen. Erreichbar.
- „Kommt frei zum Schuss“ trägt Sonder-Id 17 und ist kein Easter Egg. Der Filter lässt es zu.

## Kalibrierung

`SceneCalibrationTests` spielt 1.000 Partien beider Engines. Stand nach der Ereignisschicht, je Spiel, beide Teams:

| | Textmodus | schnelle Engine |
| --- | ---: | ---: |
| Tore | 3,19 | 3,07 |
| Schüsse | 26,6 | 27,7 |
| davon aufs Tor | 11,0 | 10,4 |
| xG | 3,15 | 3,19 |
| Ecken | 9,9 | 9,0 |
| Fouls | 23,2 | 21,3 |
| Abseits | 7,1 | 6,8 |
| Gelb | 3,6 | 3,6 |
| Rot | 0,17 | 0,16 |
| Verletzungen | 0,72 | 0,68 |

Alle Werte liegen innerhalb von etwa ±10 %. Der Test lässt ±13 % zu, bei Rot und Verletzungen ±20 %, weil die Stichprobe klein ist.

## Offen

- Die exakte FM2002-Gewichtsformel ist nicht belegt. `SceneWeightOptions` ist der Stellhebel.
- Die Ereignisschicht setzt die Häufigkeit von Fouls, Standards, Karten und kurzen Abschlüssen. Der Baum entscheidet den Text und, bei vollen Standards, das Tor.
- Bytes b8/b9 (Stimmung), b20, b22, b28, b31 und b34 sind nur teilweise oder gar nicht angeschlossen. b34 hat keinen Effekt. b29 steuert nur noch die Geburtstagszeilen.
- Attributindizes ohne Katalogname sind Annahmen.
- `REASON`- und `INJURY`-Texte fehlen im Original.
- Rollen wie Libero, Wingback und „ANC“ werden aus der FMH-Formation abgeleitet, nicht aus einem FM2002-Slot.
- Elfmeterschießen benutzt Kategorie 1 und zählt nur Tor oder nicht. Verlängerung folgt der K.-o.-Regel der schnellen Engine.
- Die 2D-Darstellung ist nicht Teil dieses MVP. Der Ereignisstrom (`MatchEvent` inklusive `Narrative`, Minute, Team, Spieler, Ergebnis) ist dafür vorgesehen. `Narrative` ist vom Golden Master ausgenommen.
