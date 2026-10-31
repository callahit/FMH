# FMH – Merchandising: Genre-Vergleich und Designvorschlag

Stand: 31.10.2026 · Ergänzung zu `docs/umsetzungskonzept.md` und der Konzeptanalyse (Transfers/Ligen).
Interaktive Fassung: Canvas „FMH Merchandising“.

## 1. Ausgangslage in FMH

Merchandising ist wirtschaftlich kalibriert (DFL: 1. Liga ~14/16 Mio. Fanartikel je Klub), aber **nicht spielbar**:

| Baustein | Ist | Lücke |
|---|---|---|
| Matchday-Merch | `Zuschauer × merchPerSpectator × MerchLevel × (1 + Fanshop×0,2) × Stimmung` in `FinanceModule.MatchdayFinances` | Kein Sortiment, keine Preise, keine Partnerwahl |
| Gebäude | `fanshop` → `income.merch`; `cityShop` / `onlineShop` → `income.weekly` | Kanäle nicht an Katalog gekoppelt |
| Ausrüster | `SponsorSlot.Kit` (16 % Sponsorwert), Firmen in `sponsors/*.json` mit `industry: kit` | Nur Jahreszahlung; kein Einfluss auf Merch-Marge oder Katalog |
| Ereignisse | `merchRecord`, `merchFlop` (Gartenzwerg) | Hooks ohne Sortimentslogik |

Kapitel 8 gilt laut README als „vollständig“ (Sponsoren, Finanzen). Für Anstoß-näheres Merchandising fehlt ein eigenes Arbeitspaket.

## 2. Vergleich mit Genre-Spielen

| Spiel | Merch-Modell | Ausrüster / Kit | Partner vs. Eigenbetrieb | Hebel |
|---|---|---|---|---|
| **FMH heute** | Formel je Zuschauer | Kit-Slot = Sponsoring | Nein | Liga-`merchLevel`, Fanshop, Stimmung |
| **Anstoß 3** | Agentur **oder** Eigenbetrieb: Sortiment, Preise, Lager | getrennt vom Fanshop | **Ja** (Agentur / Einkaufsleiter + Produktideen) | Kuratiertes Sortiment; Flops (z. B. Senf) meiden |
| **Football Manager** | Commercial/Merch als Aggregat, Shirt Sales | Kit-Deal über Vorstand, kaum verhandelbar | Nein (indirekt) | Ruf, Superstars, Asien-/US-Touren, Affiliate-Märkte |
| **EA FC 26 Karriere** | kein Club-Merch-System | keine verhandelbaren Sponsoren | Nein | Matchday/Stadion; Sponsoren Community-Wunsch |
| **Hattrick** | kein Club-Fanshop | saisonale Sponsorenangebote | Nein | realer HT-Store außerhalb der Simulation |

**Fazit:** Anstoß 3 ist das passende Vorbild für Flexibilität (Partner vs. selbst). FM liefert indirekte Umsatzhebel (Ruf, Stars, Touren), die FMH später ergänzen kann. EA FC und Hattrick bieten im Club-Betrieb fast keine Merch-Tiefe.

## 3. Offizielle Fanshops – Kategorien und Produkte

Ausgewertet: öffentlicher Kategoriebaum von **FC Bayern Store**, **BVB Fanshop**, **Liverpool FC Store**.

| Kategorie | Typische Produkte |
|---|---|
| Matchday-Kit | Heim-/Auswärts-/Third-/Torwart-Trikot, Shorts, Stutzen; Authentic vs. Replica |
| Training & Travel | Trainingsanzug, Warm-up, Travel-Jacke, Trainingsshirt |
| Lifestyle-Bekleidung | Hoodie, T-Shirt, Polo, Jacke, Hose, Loungewear |
| Kopf & Hals | Schal, Cap, Beanie, New-Era-Kollektion |
| Kinder & Baby | Mini-Kit, Babybody, Kinderschal |
| Accessoires | Schlüsselanhänger, Aufkleber, Taschen, Plüsch/Maskottchen |
| Zuhause & Lifestyle | Tasse, Bettwäsche, Küche, Garten, Tierbedarf |
| Saisonales / Events | Weihnachten, Halloween, CL-Begegnungsschal, Jubiläum |
| Personalisierung | Rückennummer, Name, Sonderedition |

Diese neun Kategorien sind die Vorlage für den datengetriebenen Produktkatalog (Abschnitt 5).

## 4. Designziel: Partnerfirma oder selbst

Drei Betriebsmodi für den menschlichen Verein (KI bleibt bei vereinfachter Formel, skaliert nach Modus):

### 4.1 Partner (Lizenz)

- Ausrüster oder Merch-Partner führt Kernsortiment und Lager.
- Verein erhält **Fixum + Umsatzbeteiligung**.
- Wenig Mikro-Steuerung; geeignet für Amateur bis 3. Liga oder Spieler, die nicht mikromanagen wollen.
- Angebote: Premium-Marke (höheres Fixum, engere Marge), Mittelklasse, Regionalhersteller, No-Name/Eigenlabel.

### 4.2 Hybrid (Standard ab 2./1. Liga)

- Partner liefert Matchday-Kit ± Training.
- Lifestyle und Accessoires steuert der Verein selbst (Kategorien freischalten, Preise, Lager).
- Mittlere Marge, mittlere Komplexität.

### 4.3 Eigenbetrieb (DIY, Anstoß-Pfad)

- Verein wählt Kategorien/Produkte, Preise, Bestellmengen.
- Personal: **Einkaufsleiter** (Nachbestellung) und **Produktentwicklung** (neue SKUs vorschlagen) – analog Anstoß.
- Höchste Marge, Lager- und Flop-Risiko; braucht `fanshop` (und optional `onlineShop`).
- Novelty-Artikel (Gartenzwerg o. Ä.) mit hoher `flopChance` – Satire optional.

### 4.4 Ausrüstervertrag (Erweiterung von `SponsorSlot.Kit`)

Neben der Jahreszahlung:

| Klausel | Wirkung |
|---|---|
| Qualitätsstufe | Einfluss auf Fan-Stimmung und Replica-Nachfrage |
| Katalogrechte | welche Kategorien der Partner exklusiv führt |
| Mindestabnahme | Fixkosten bei DIY/Hybrid |
| Exklusivität | sperrt parallele Lifestyle-Partner |
| Auf-/Abstieg, Europa-Bonus | wie bestehende Sponsor-Klauseln |
| Merch-Multiplikator | ersetzt oder ergänzt den reinen Slot-Anteil am Sponsorwert |

Mehrere Angebote gleichzeitig; Ablehnen/Annehmen wie heute bei Sponsoren.

## 5. Datenmodell (Vorschlag)

```text
MerchState (je Club)
  mode: Partner | Hybrid | Diy
  partnerDeal?: MerchPartnerDeal   // Firma, Fixum, royalty%, Rechte, UntilSeason
  lines: List<MerchLine>           // productId, price, stock, active
  staff: buyingManager?, productDev?

MerchPartnerDeal
  company, quality 1–5, kitRights, trainingRights, lifestyleRights
  perSeason, royaltyPercent, minOrder, UntilSeason
  relegationPercent, promotionPercent

ProductDefinition (Datenpaket)
  id, category, baseDemand, margin, flopChance
  requiresPartnerQuality?, requiresBuilding?, seasonalMonths?
```

Kanäle:

- `fanshop` → Matchday-Absatzkapazität
- `cityShop` → Wochenumsatz aus Lifestyle
- `onlineShop` → reichweitenabhängiger Wochenumsatz (Ruf, Stars später)

Bestehende Formel bleibt Fallback für KI und für Partner-Modus (skaliert mit Royalty).

## 6. Produktkatalog (Startmenge)

| Id | Kategorie | Nachfrage | Hinweis |
|---|---|---|---|
| `kit.home` / `kit.away` | Matchday-Kit | hoch | Ausrüster oder Eigenproduktion |
| `kit.third` | Matchday-Kit | mittel | ab Ruf ≥ 8 oder Europa |
| `training.set` | Training | mittel | |
| `lifestyle.hoodie` / `lifestyle.tee` | Lifestyle | hoch | Volumen |
| `fan.scarf` / `fan.cap` / `fan.flag` / `fan.bag` | Accessoires | mittel–hoch | Derby-/Europa-Boost für Schal |
| `home.mug` / `home.bedding` | Zuhause | niedrig | Bedding: Flop-Risiko |
| `novelty.gnome` | Novelty | sehr niedrig | Anstoß-Satire |
| `mascot.plush` | Accessoires | mittel | wenn Maskottchen-Gebäude |
| `seasonal.xmas` | Saisonal | mittel | Nov–Dez |

Erweiterbar über `data/*/merch/products.json` ohne Codeänderung.

## 7. Umsetzungsschritte (Arbeitspakete)

| ID | Inhalt | Abhängigkeit |
|---|---|---|
| **M-01** | Domäne: `MerchState`, Modi, PartnerDeal, Stock; Save-Abschnitt + Migration | – |
| **M-02** | Ausrüster: Kit-Angebote mit Qualitäts-/Katalogklauseln; Merch-Multiplikator | M-01 |
| **M-03** | Produktkatalog als Daten; DIY/Hybrid-Sortiment und Preise | M-01 |
| **M-04** | Kanäle `fanshop` / `cityShop` / `onlineShop` an Absatz koppeln | M-03 |
| **M-05** | UI unter Finanzen: Modus, Partner wählen, Sortiment | M-02, M-03 |
| **M-06** | Kalibrierung: Partner flach, DIY-Peak höher; DFL ±20 % halten | M-04 |

Empfohlene Reihenfolge: **M-01 → M-02 → M-03**, dann UI und Feintuning.

## 8. Abgrenzung zur bestehenden Roadmap

- Kein Ersatz für AP 5 (Europapokale) oder AP 6 (Editor).
- Passt fachlich zu Kapitel 8 (Finanzen/Sponsoren) und Anstoß-Tiefe (Gelände, Ereignisse).
- Indirekte FM-Hebel (Shirt Sales durch Stars, Preseason-Touren) bewusst **nicht** in M-01–M-06; eigener Folgeschritt nach Nationalteams/Touren.

## 9. Quellen

- FMH: `FinanceModule.cs`, `SponsorsModule.cs`, `data/base/rules/buildings.json`, `data/base/sponsors/de.json`, README Wirtschaft
- Anstoß 3 Fanartikel: [anstoss-zone Forum](http://forum.anstoss-zone.de/viewtopic.php?t=8794); Bauwerke Fanshop: [anstoss-zone Bauwerke](http://www.anstoss-zone.de/specials/bauwerke.php)
- FM Commercial: Community-/Guide-Berichte zu Shirt Sales, Ruf, Touren (kein verhandelbares Sortiment)
- EA FC 26: Pitch Notes Career Mode; Community-Forderung nach Sponsoren
- Fanshops: [fcbayern.com/store](https://fcbayern.com/store/de-de/c/bekleidung), [shop.bvb.de](https://shop.bvb.de/), [store.liverpoolfc.com](https://store.liverpoolfc.com/)
