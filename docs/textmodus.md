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

Eine offene Szene wählt zuerst die angreifende Mannschaft (Stärke, Mentalität, Spielstand, kleiner Heimvorteil) und dann eine Startkategorie: Flügel, Mitte, Aufbau, langer Ball, Konter, Steilpass, Abwehrfehler, selten Einwurf oder ein sonstiger Text. Standards, Karten und Verletzungen kommen nur als Folgeszene.

Die Kindauswahl filtert hart: die Rolle muss in der Formation besetzt sein (Spezialrollen Elfmeter, Freistoß und Ecke fallen auf den besten Mann zurück), Wetter und die belegten Bedingungsbits müssen passen. Danach

```text
w = Grundgewicht + b32 + Attributskala · (bis zu drei Terme)
```

Kinder mit `b32 = 0` und einem Modifikator haben mindestens das Modifikator-Minimum, damit kein Zweig tot ist. Die Defaults stehen in `SceneWeightOptions` und sind das Ergebnis der Kalibrierung. Die Formel ist austauschbar.

Ein Schritt der Oberfläche ist eine Textzeile. Tore, Schüsse, xG, Ecken, Fouls, Abseits, Karten und Verletzungen werden auf das bestehende Spielergebnis geschrieben, inklusive Eigentor (`GoalRecord.OwnGoal`). Karten- und Verletzungstexte kommen aus den Kategorien 22–28; die Originalzeilen für `REASON` und `INJURY` fehlen, die Zuordnung steht in `CardCatalog`.

Flutlicht ist eine Annahme: November bis Februar. Byte 32 (unklar) filtert nicht. Bytes 20, 22 und 28 bleiben unbenutzt.

## Kalibrierung

`SceneCalibrationTests` spielt 1.000 Partien beider Engines auf denselben Aufstellungen. `MinutesPerScene = 6.4` ist so gewählt, dass die Tore bei der schnellen Engine landen. Stand des Laufs:

| je Spiel, beide Teams | Textmodus | schnelle Engine |
| --- | ---: | ---: |
| Tore | 3,11 | 3,07 |
| Schüsse | 7,5 | 27,7 |
| davon aufs Tor | 5,6 | 10,4 |
| xG | 3,01 | 3,19 |
| Ecken | 0,5 | 9,0 |
| Fouls | 0,5 | 21,3 |
| Abseits | 0,01 | 6,8 |
| Gelb | 0,17 | 3,6 |
| Rot | 0,01 | 0,16 |
| Verletzungen | 0,01 | — |

Tore und xG passen. Schüsse aufs Tor liegen in derselben Größenordnung. Fouls, Ecken, Abseits und Karten bleiben deutlich seltener: auf den wahrscheinlichen Pfaden trägt nur etwa jeder fünfzigste Angriff ein Foul-Flag und nur etwa jeder dreißigste eine Ecke. Die Gewichtsformel verschiebt das um wenige Prozent, nicht um den Faktor 30. Diese Raten sind im Test als Band festgehalten und nicht an die schnelle Engine gezwungen. Wer sie anhebt, erfindet Ereignisse, die der Baum nicht ausgewählt hat.

## Offen

- Die exakte FM2002-Gewichtsformel ist nicht belegt. `SceneWeightOptions` ist der Stellhebel.
- Startkategorie, Minuten je Szene, Karten- und Verletzungswahrscheinlichkeit sind eigene Logik.
- Bytes b8/b9 (Stimmung), b20, b22, b28, b29, b31 und b34 sind nur teilweise oder gar nicht angeschlossen. b34 (Schiri-Strenge auf dem Knoten) hat keinen Effekt.
- Attributindizes ohne Katalogname sind Annahmen.
- `REASON`- und `INJURY`-Texte fehlen im Original.
- Rollen wie Libero, Wingback und „ANC“ werden aus der FMH-Formation abgeleitet, nicht aus einem FM2002-Slot.
- Elfmeterschießen benutzt Kategorie 1 und zählt nur Tor oder nicht. Verlängerung folgt der K.-o.-Regel der schnellen Engine.
- Die 2D-Darstellung ist nicht Teil dieses MVP. Der Ereignisstrom (`MatchEvent` inklusive `Narrative`, Minute, Team, Spieler, Ergebnis) ist dafür vorgesehen. `Narrative` ist vom Golden Master ausgenommen.
