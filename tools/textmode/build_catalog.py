#!/usr/bin/env python3
"""Baut parameter-katalog.json aus work/nodes.json (erzeugt von decode_textm.py).
Statistiken werden aus den Daten berechnet; Bedeutungen sind manuell aus Textkorrelation abgeleitet."""
import json, collections, os, re
HERE=os.path.dirname(os.path.abspath(__file__))
D=json.load(open(os.path.join(HERE,'work','nodes.json')))
N=D['nodes']; L={int(k):v for k,v in D['lines'].items()}
DEF=[0]*44; DEF[8]=DEF[9]=5; DEF[10]=255; DEF[21]=20; DEF[23]=7; DEF[34]=6; DEF[42]=DEF[43]=30
RN={1:'GK',2:'RB',3:'LB',4:'CD',5:'SW',6:'RWB',7:'LWB',8:'ANC',9:'DM',10:'RM',11:'LM',12:'CM',13:'RW',14:'LW',15:'AM',16:'CF',17:'ST',18:'PEN',19:'FKC',20:'FKR',21:'FKL',22:'CKR',23:'CKL'}
ROLEB={0,1,4,19,33,35,36,38,40,41}
def role(v): return None if v==0 else ('1-' if v&128 else '2-')+RN.get(v&127,'?%d'%(v&127))
def stat(o):
    c=collections.Counter(n['blk'][o] for n in N)
    nd=sum(v for k,v in c.items() if k!=DEF[o])
    top=[[ (role(k) if o in ROLEB else k), v] for k,v in c.most_common(12)]
    return dict(default=DEF[o], nonDefaultNodes=nd, distinct=len(c), min=min(c), max=max(c), top=top)
H=lambda s:s
B=[  # offset, name, meaning, confidence, evidence, csharp, fmh, entry
(0,'modA.role','Rolle des Spielers, dessen Attribut die Knotengewichtung beeinflusst (Paar A mit b2)','hoch (Schema), mittel (Formel)','b0==b33 in 2441 Knoten; z.B. "kann unbedrängt flanken" 0=1-LM 2=10','RoleRef ModA.Role','Player der Rolle in aktueller Aufstellung','Kindauswahl: Gewichtsterm'),
(1,'modB.role','Rolle zu Attributpaar B (mit b3), oft Gegenspieler/Vorlagengeber','hoch (Schema)','"Kopfball 1-ST!" 1=1-LM 3=10 (Flanke des Vorlagengebers)','RoleRef ModB.Role','Player','Kindauswahl: Gewichtsterm'),
(2,'modA.attr','Attributindex zu b0; <50 positiv, >=50 = (Wert-50) invertiert','hoch (Schema), Index-Namen Hypothese','Siehe attributeIndex; z.B. 70 = Nerven invertiert bei "am Tor vorbei"','AttrRef ModA.Attr (+bool Inverted)','PlayerAttributes','Kindauswahl'),
(3,'modB.attr','Attributindex zu b1, gleiche Kodierung','hoch (Schema)','"2-GK hat den Ball sicher" 2=13 3=60 (GK Strafraum + invertierte Flanke 1-LM)','AttrRef ModB.Attr','PlayerAttributes','Kindauswahl'),
(4,'modC.role','Drittes Modifikator-Paar (selten, 477 Knoten)','mittel','Foulknoten: 4=2-CD 6=11','RoleRef ModC.Role','Player','Kindauswahl'),
(5,'reserved5','immer 0','bestätigt','alle 9906 Knoten 0','-','-','-'),
(6,'modC.attr','Attributindex zu b4 (Werte 1-13)','mittel','477 Knoten','AttrRef ModC.Attr','PlayerAttributes','Kindauswahl'),
(7,'reserved7','immer 0','bestätigt','alle 0','-','-','-'),
(8,'gate.attTeam','Team-Stimmung/Einsatz angreifendes Team, Skala 1-10, neutral 5','Hypothese','2-4 bei "lassen Einsatz vermissen", 6-7 bei "wütend/erbost"','byte MoodAtt','Moral/Form/Spielstand-Druck des Teams','Gate/Gewicht vor Kindauswahl'),
(9,'gate.defTeam','Einsatz verteidigendes Team, 1-10, neutral 5','Hypothese','6-11 bei "letzter Einsatz/wirft sich dazwischen", 3 bei "halbherzig"','byte MoodDef','Moral/Kondition Verteidiger','Gate/Gewicht'),
(10,'shotDistance','Schussdistanz in Metern, 255 = kein Schuss','hoch','"aus 30 Metern"=28/30, Elfmeter=10, Nachschuss 3-7, Kopfball ~4-9','byte? ShotDistanceM','xG-Modell (Distanz)','Ergebnis: Shot-Event + xG'),
(11,'passType','Pass-/Ballart-Bitfeld: 1 ?, 2 Ballgewinn/Lauf, 4 Abseits, 8 hoch/Flanke, 16 kurz/flach, 32 weit, 64 Tribüne, 128 Steil/in den Lauf','mittel (4=Abseits hoch)','40 von 41 b11=4 Knoten sind Blätter mit "Abseits"','[Flags] PassKind','Pässe/Abseits-Statistik, Taktik Passspiel','Ergebnis + Gewicht (Taktik)'),
(12,'offsideTrap','1 = Abwehr rückt heraus/Abseitsfalle','mittel','39 Knoten, alle Abseitsfallen-Texte','bool OffsideTrap','Taktik Abseitsfalle des verteidigenden Teams','Gate: nur wählbar bei aktiver Abseitsfalle (Hypothese)'),
(13,'setPieceKind','Standard-Kontext: 1 Ecke/Kopfball, 2 direkter FS, 4 FS-Kopfball/indirekt, 8 Elfmeter, 15 ?','mittel','Elfmeter-Knoten 13=8','[Flags] SetPieceKind','Statistik Tore nach Standards','Ergebnis-Annotation'),
(14,'result.lo','Ergebnis-Flags (u16 mit b15), Low-Byte','bestätigt','siehe resultFlags','[Flags] SceneResult','Tore/Schüsse/aufs Tor/Eigentore/Pfosten','Ergebnis'),
(15,'result.hi','Ergebnis-Flags High-Byte (0x0100 Latte ... 0x1000 Abstoß)','bestätigt','siehe resultFlags','[Flags] SceneResult','Ecken/Fouls/Abstöße','Ergebnis'),
(16,'reserved16','immer 0','bestätigt','alle 0','-','-','-'),
(17,'reserved17','immer 0','bestätigt','alle 0','-','-','-'),
(18,'refDecision','Schiri-Entscheidung Bitfeld: 1 Elfmeter, 16 Freistoß, 32 nichts gesehen/weiterlaufen, 64 kein Foul/fair/Tor zählt, 2/4/8 Zusatz (unklar)','mittel-hoch','(1,b26=1)x102, (32)x62, (64)x50, (16,b26=4)x37, 65=nach Zögern Elfmeter','[Flags] RefDecision','Schiri-Strenge, Fouls, Elfmeter','Ergebnis + Folgekategorie'),
(19,'foul.victim','Rolle des Gefoulten','hoch','"senst 2-DM einfach um" -> 19=2-DM','RoleRef Victim','Player (Verletzungsrisiko)','Ergebnis Foul / VICTIM'),
(20,'foul.victimVal','1-9 (Hypothese: Schwere/Verletzungsgefahr) bzw. Rollencode des Täters, wenn Angreifer foult (>=128)','Hypothese','Werte 1-9 bei 1-xx Opfern; 144/145 (=1-CF/1-ST) wenn 2-GK/2-DM Opfer','byte FoulValue','Verletzung/Täter','Ergebnis'),
(21,'fkDistance','Distanz des Freistoßes/Foulortes in m, Default 20','mittel','18 bei b26=4 (FS zentral), 30 bei weit, 16 bei "aus 16 Metern"','byte FoulDistanceM','Folgeszene Freistoß (b10-Start)','Folgekategorie-Parameter'),
(22,'fkZone','0 Default, 2-5 bei Freistoßentscheidungen','unklar','nur 41 Knoten, keine klare Korrelation zu b26','byte FkZone','-','offen'),
(23,'goalType','Torart: 0 Kopfball, 1 Distanzschuss, 2 Volley/Direktabnahme, 3 direkter Freistoß, 4 Elfmeter, 5 Eigentor, 6 Schuss/Abstauber, 7 = kein Tor (Default)','hoch (0,3,4,5) / mittel (1,2,6)','Kopfball im Pfad 72 % bei 0; Volley 99 % bei 2','enum GoalType','Torstatistik, xG-Typ','Ergebnis'),
(24,'goalPlacement','Torplatzierung: 0 unbestimmt, 3 hoch/unter die Latte, 4 Winkel, 7 flach in die Ecke, 8 durch die Beine/leeres Tor; 1,2,5,6 unklar','mittel','4 = Winkel 68/68','enum GoalPlacement','2D-Ballziel','2D-Choreograf'),
(25,'nodeFlags','Bit 0x80 = Blatt (Szenenende); Bits 0-1 = Seite/Zone (1 rechts, 2 links, 3 zentral); Bits 2-6 immer 0','bestätigt','Blattkorrelation exakt (6884/3022); "über links"-Tore 226x Zone 2, "über rechts" 226x Zone 1','[Flags] NodeFlags','Ballposition','Ablaufsteuerung + 2D'),
(26,'followUp','Folgekategorie-Index (1-29): 1 Elfmeter, 2-6 Freistöße, 11 Alleingang, 13/14 Ecke links/rechts','bestätigt','Foul->1/2-6, Ecke-Flags->13/14','int? FollowUpCategory','Nächste Szene','Szenenverkettung'),
(27,'conditions','Bedingungen: 1 nass, 2 eisig, 4 Flutlicht/Nacht, 32 ?, 64 nur mit laufendem Spiel (nicht im Elfmeterschießen?), 128 klare Führung?','bestätigt (1,2,4), Hypothese (32,64,128)','"nasser Rasen" alle 1; "eisig/glatt" alle 2; "Flutlicht/Nachthimmel" alle 4; 64 nur Nachschuss/Abpraller beim Elfmeter','[Flags] Conditions','Wetter, Anstoßzeit, Spielstand','Gate (harte Bedingung)'),
(28,'mood','8/6/1/2 selten; Hypothese Stimmung (6 frustriert, 1 lässig, 2 resigniert)','unklar','<40 Knoten','byte Mood28','Moral','offen'),
(29,'specialId','Sondertexte (2,4,17) in "Sonstige Texte"','unklar','Geburtstagsbanner/Megaphon','byte SpecialId','-','Easter-Egg-Gate'),
(30,'actionType','Aktionsart-Bitfeld: 1 Schuss, 2 Pass/Flanke, 4 Dribbling/Zweikampf, 8 Befreiungsschlag ins Aus, 16 Grätsche, 64 Unsportlichkeit, 128 Foulpfiff mit Täter','mittel','b30=1 205x Schusstexte, 128 66x Schiri pfeift','[Flags] ActionKind','Statistik (Schüsse, Zweikämpfe, Fouls), 2D-Animation','Ergebnis + 2D'),
(31,'refStyle','Schiri-Verhaltensklasse: 1 pfeift sofort, 2 zögert, 4 sieht nichts/fair, 8 Freistoß','Hypothese','nur auf Schiri-Knoten, exklusiv zu b30','byte RefStyle','Schiedsrichter-Strenge','Gate/Gewicht der Schiri-Kinder'),
(32,'baseWeight','Basisgewicht 0-22 für die Kindauswahl','hoch (Gewicht), Formel Hypothese','2344 Knoten !=0; Elfmeter "Tor!" 11 vs Glanzparade 0','byte BaseWeight','-','Kindauswahl'),
(33,'actor','handelnde Rolle (Ballführender/Protagonist), Teambit 0x80 = Team 1 (angreifend)','bestätigt','Rolle erscheint in 84 % im eigenen Text; alle 23 Codes','RoleRef Actor','Player-Auswahl nach Aufstellung','Gate (Rolle muss besetzt sein) + Ballbesitz'),
(34,'refSeverity','Default 6; 0-5 auf Schiri-Knoten (Hypothese Strengestufe/Kartenschwelle)','unklar','nur Schiri-Kontext','byte RefSeverity','Schiri-Strenge','Gate'),
(35,'offender.play','Täter für Karte "für Spielaktionen" (Foul/Hand/Schwalbe)','hoch','"2-LB kann ihn nur mit einem Foul stoppen" -> 35=2-LB','RoleRef OffenderPlay','Karten, Sperren','Folgeszene Karten (Kat. 23/25/27)'),
(36,'offender.conduct','Täter für Karte "für Unbeherrschtheiten"','mittel-hoch','"1-CF ist darüber so erbost" -> 36=1-CF 37=25','RoleRef OffenderConduct','Karten','Folgeszene Karten (Kat. 24/26/28)'),
(37,'reason','Kartengrund-Code (REASON): 4/5 Foul, 6/10 Handspiel, 7 Halten, 9 taktisches Foul, 2 brutales Treten, 26 Stoßen, 25 Nachtreten, 29 Foul am Torwart, 30/37 Meckern/Schwalbe','mittel','Hand-Knoten 6/10; "gehalten/umgerissen" 7; Schwalbe 37','byte ReasonCode -> enum CardReason','Kartenstatistik, Text REASON','Karten-Folgeszene'),
(38,'injury.player','verletzter Spieler (Rolle)','hoch','"wälzt sich auf dem Boden", "verletzt zu haben"','RoleRef Injured','Verletzungen','Folgeszene Verletzung (Kat. 22)'),
(39,'injury.type','Verletzungsart-Code (INJURY), Geschwister mit identischem Text unterscheiden sich nur hier','mittel','gleicher Text, b39 = 30/18/16/23/14','byte InjuryType -> InjuryCatalog','Verletzungsdauer','Folgeszene Verletzung'),
(40,'rating.roleA','Rolle mit Noteneffekt A','mittel-hoch','Glanzparade 40=2-GK 42=50','RoleRef RatingA','Spielernote','Nach Knotenauswahl'),
(41,'rating.roleB','Rolle mit Noteneffekt B','mittel-hoch','Glanzparade 41=1-PEN 43=21','RoleRef RatingB','Spielernote','Nach Knotenauswahl'),
(42,'rating.valA','Noteneffekt A, neutral 30 (<30 schlechter, >30 besser)','mittel-hoch','vorbei 15, Glanzparade 50','sbyte RatingDeltaA = v-30','PlayerMatchRating','Nach Knotenauswahl'),
(43,'rating.valB','Noteneffekt B, neutral 30','mittel-hoch','gehaltener Elfmeter: Schütze 19-21','sbyte RatingDeltaB = v-30','PlayerMatchRating','Nach Knotenauswahl'),
]
cats=[]
for c in N[0]['kids']:
    n=N[c]; sub=[x for x in N if x['cat']==n['idx']]
    cats.append(dict(index=len(cats)+1,nodeIdx=c,id=n['id'],name=L[n['refs'][0]] if n['refs'] else '',nodes=len(sub)+1,
        maxDepth=max([x['depth'] for x in sub]+[1]),goalLeaves=sum(1 for x in sub if (x['blk'][14]|x['blk'][15]<<8)&1),
        leaves=sum(1 for x in sub if x['blk'][25]&128)))
flags={'0x0001':['Tor','bestätigt','1668 Knoten, 97 % mit "Tor!"'],'0x0002':['Schuss','bestätigt',''],'0x0004':['aufs Tor','bestätigt','0x0006 = Schuss aufs Tor'],
 '0x0008':['Eigentor','bestätigt','0x000E'],'0x0010':['unklar (Elfmeter-"Schuss...")','unklar',''],'0x0020':['unklar','unklar','selten'],
 '0x0040':['Torwart/Abwehr hat Ball (pariert/geklärt)','hoch',''],'0x0080':['Pfosten','bestätigt',''],'0x0100':['Latte','bestätigt',''],
 '0x0200':['Ecke (Kontext links)','hoch','b26=13'],'0x0400':['Ecke (Kontext rechts)','hoch','b26=14, gemischt'],'0x0800':['Foul/Pfiff','bestätigt','b26 = 1 oder 2-6'],
 '0x1000':['vorbei/drüber -> Abstoß','bestätigt','1132 Knoten']}
fc=collections.Counter()
for n in N:
    f=n['blk'][14]|n['blk'][15]<<8
    for b in range(16):
        if f>>b&1: fc['0x%04x'%(1<<b)]+=1
attr={1:'Passspiel',2:'Kopfball',3:'Schusskraft',4:'Schussgenauigkeit/Abschluss',5:'Antritt/Reaktion',6:'Schnelligkeit (Laufduell)',7:'Zweikampf defensiv/Einsatz',8:'Zweikampf (Grätsche/Ausspielen)',9:'Technik (Volley, Fallrückzieher, Dribbling)',10:'Flanken',11:'Torwart Reflexe',12:'Torwart Halten/Fangen',13:'Torwart Strafraumbeherrschung',14:'Torwart Herauslaufen/1-gegen-1',17:'Stellungsspiel',18:'Einsatz/Dazwischengehen',20:'Nerven',22:'Fairness',24:'Teamgeist/Abspiel',26:'Torwart Flugparaden',27:'Torwart Fangsicherheit',30:'Elfmeter',37:'Torinstinkt',38:'Abstauber/Nachsetzen',48:'Spielübersicht (Libero)'}
ac=collections.Counter(n['blk'][o] for n in N for o in (2,3,6) if n['blk'][o])
tok=collections.Counter(); R=re.compile(r'\b([12])-([A-Z]{2,4})(\d?)\b'); suf=collections.Counter()
for v in L.values():
    for m in R.finditer(v): tok[m.group(1)+'-'+m.group(2)]+=1; suf[m.group(3) or '-']+=1
    for t in ('REF','OFFENDER','VICTIM','REASON','INJURY'): tok[t]+=len(re.findall(r'\b%s\b'%t,v))
cat=dict(
 meta=dict(source='Textm.sav (Baum) + Textm.txt (13432 Zeilen, CP1252)',generator='decode_textm.py + build_catalog.py',created='2026-10-31',
   legend='Konfidenz: bestätigt = exakte/nahezu exakte Korrelation; hoch/mittel = starke Textkorrelation; Hypothese/unklar = nicht belegt'),
 header=dict(size=16,fields=[dict(offset=0,len=8,value='ce 5d 50 cf 87 8f aa 94',meaning='Magic/Signatur',confidence='bestätigt'),
   dict(offset=8,type='u32',value=2,meaning='Version (Hypothese)',confidence='Hypothese'),dict(offset=12,type='u32',value=0,meaning='reserviert/Flags',confidence='Hypothese')]),
 record=dict(layout='u32 id, u32 n, n*u32 Zeilennummer (Varianten), 44 Byte Parameterblock, u32 Kindanzahl; Preorder-Reihenfolge',confidence='bestätigt (Parse endet exakt am EOF)',
   nodes=len(N),leaves=sum(1 for n in N if n['blk'][25]&128),variantsPerNode=dict(collections.Counter(len(n['refs']) for n in N)),duplicateIds='2764, nur in gespiegelten Kategorien'),
 categories=cats,
 bytes=[dict(offset=o,name=nm,meaning=m,confidence=cf,evidence=ev,csharp=cs,fmh=fm,decisionEntry=en,stats=stat(o)) for (o,nm,m,cf,ev,cs,fm,en) in B],
 resultFlags={k:dict(meaning=v[0],confidence=v[1],evidence=v[2],count=fc.get(k,0)) for k,v in flags.items()},
 roles=dict(encoding='Bit 0x80 = Team 1 (in der Szene angreifend/ballbesitzend), sonst Team 2; untere 7 Bit = Rollencode',codes={str(k):v for k,v in RN.items()},
   textCounts=dict(tok.most_common()),suffix='Ziffer 1-3 hinter der Rolle (1-ST1) = weiterer, anderer Spieler derselben Rolle',suffixCounts=dict(suf)),
 placeholders=dict(REF='Name Schiedsrichter',OFFENDER='Täter (b35/b36)',VICTIM='Verletzter/Gefoulter (b19/b38)',REASON='Kartengrund (b37)',INJURY='Verletzungsart (b39)',
   note='Keine Platzhalter für Minute, Spielstand, Team- oder Stadionnamen; Zahlen wie "30 Metern" sind Literaltext'),
 attributeIndex=dict(rule='Wert<50: Attribut wirkt positiv; Wert>=50: Attribut (Wert-50) wirkt invertiert',confidence='Schema hoch; Namen Hypothese aus Textkorrelation',
   names={str(k):v for k,v in attr.items()},usage={str(k):v for k,v in sorted(ac.items())}),
 conditions={'1':'nass (bestätigt)','2':'eisig/glatt (bestätigt)','4':'Flutlicht/Nacht (bestätigt)','32':'unklar (Elfmeter-Klärung, Sondertexte)','64':'Hypothese: nur bei weiterlaufendem Spiel, nicht im Elfmeterschießen','128':'Hypothese: klare Führung ("heute nicht so schlimm")'},
 childSelection=dict(
   facts=['3022 Elternknoten, 320 mit genau einem Kind','1668 Eltern, deren Kinder alle b32=0 haben (617 davon ganz ohne Modifikatoren)','2859 Kinder mit b32=0 und ohne Modifikatoren, oft neben gewichteten Geschwistern','Geschwister unterscheiden sich häufig nur in der Rolle b33 (Kopfball 1-ST / 1-CF / 1-AM)'],
   tests=[dict(h='gleichverteilt',penaltyGoal=0.37),dict(h='nur b32',penaltyGoal=0.91,refuted='Indirekter FS zentral wird toter Pfad (P=0)'),dict(h='b32+1',penaltyGoal=0.66),dict(h='b32 oder 1 bei Modifikator',penaltyGoal=0.78)],
   model='Hypothese: w = Basis + b32 + Summe(Attributterme aus b0/b2, b1/b3, b4/b6) * Gates(b27 Bedingungen, b33 Rolle besetzt, b12 Taktik, b8/b9 Stimmung, b31/b34 Schiri); Auswahl per gewichtetem Zufall',
   confidence='Hypothese, ohne Executable nicht verifizierbar; Kalibrierung gegen reale Statistik nötig'),
 variants='Mehrere Zeilennummern pro Knoten = gleichwertige Textvarianten, vermutlich gleichverteilt gezogen (7935x1, 1005x2, 649x3, max 10)')
json.dump(cat,open(os.path.join(HERE,'parameter-katalog.json'),'w'),ensure_ascii=False,indent=1)
print('ok',len(cat['bytes']),'bytes',len(cats),'cats')
