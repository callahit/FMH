"""Baukasten für Gebäude-Sprites je Typ und Ausbaustufe.

Drei Ausbau-Epochen (tier): 1 = alt und einfach (Holz/Backstein, Satteldach, kleine Fenster, Gebrauchsspuren),
2 = solide (Putz, größere Fenster, gepflegtes Dach, Schild), 3 = modern (Glas und Paneele, Flachdach mit Begrünung
oder Solar, Vordach, Leuchtband in Vereinsfarbe). Jedes Gebäude steht auf einem Grundstück, das seine ganze
Grundfläche ausfüllt; Baukörper und Zubehör teilen sich diese Fläche.
"""
import random
from iso import Sprite, rgb, shade, mix, noise
from buildings import wall_texture, roof_texture, box, shadow, GLASS, GLASS_HI, FRAME
from stadium import mask

GRASS = rgb("#5FA34C")
PAVE = rgb("#CFC4AA")
WHITE = rgb("#F4F2EC")

# 5x5-Symbole für Schilder (x = Pixel gesetzt)
GLYPHS = {
    "cross": ["..x..", "..x..", "xxxxx", "..x..", "..x.."],
    "ball": [".xxx.", "x.x.x", "xxxxx", "x.x.x", ".xxx."],
    "dumbbell": ["x...x", "x...x", "xxxxx", "x...x", "x...x"],
    "shirt": ["xx.xx", "xxxxx", ".xxx.", ".xxx.", ".xxx."],
    "cup": ["x...x", "xxxxx", ".xxx.", "..x..", ".xxx."],
    "star": ["..x..", ".xxx.", "xxxxx", ".x.x.", "x...x"],
    "camera": [".xx..", "xxxxx", "xx.xx", "xxxxx", "....."],
    "bed": [".....", "x....", "xxxxx", "xxxxx", "x...x"],
    "book": ["xx.xx", "xx.xx", "xx.xx", "xx.xx", "....."],
    "fork": ["x.x.x", "x.x.x", "xxxxx", "..x..", "..x.."],
    "beer": ["xxxx.", "xxxxx", "xxxx.", "xxxx.", "xxxx."],
    "ticket": ["xxxxx", "x.x.x", "xxxxx", "x.x.x", "xxxxx"],
    "euro": [".xxx.", "x....", "xxxx.", "x....", ".xxx."],
    "heart": [".x.x.", "xxxxx", "xxxxx", ".xxx.", "..x.."],
    "letter": ["xxxxx", "xx.xx", "x.x.x", "x...x", "xxxxx"],
    "eye": [".....", ".xxx.", "xx.xx", ".xxx.", "....."],
    "wrench": ["x..x.", "xxx..", ".xx..", "..xx.", "...xx"],
    "shield": ["xxxxx", "x.x.x", "xxxxx", ".xxx.", "..x.."],
    "p": ["xxxx.", "x...x", "xxxx.", "x....", "x...."],
    "bolt": ["...x.", "..x..", ".xxx.", "..x..", ".x..."],
    "kid": ["..x..", ".xxx.", "..x..", ".x.x.", "x...x"],
    "game": [".....", "xxxxx", "x.x.x", "xxxxx", "....."],
    "drop": ["..x..", ".xxx.", "xxxxx", "xxxxx", ".xxx."],
    "mic": [".xx..", ".xx..", ".xx..", "xxxx.", ".x..."],
    "bus": ["xxxxx", "x.x.x", "xxxxx", "xxxxx", ".x.x."],
}


def tier_of(level, max_level):
    """Ausbau-Epoche aus Stufe und Höchststufe (eine Stufe: solide; zwei: alt und modern)."""
    if max_level <= 1:
        return 2
    t = (level - 1) / (max_level - 1)
    return 1 if t < 0.34 else 2 if t < 0.67 else 3


def lot(s, w, h, tier, kind="grass", seed=1):
    """Grundstück über die ganze Fläche: Rasen (alt fleckig), Pflaster oder Terrasse; modern mit Kante."""
    n = noise(seed + 101)
    def tex(u, v):
        k = n(int(u * w * 24), int(v * h * 24))
        if kind == "pave":
            c = shade(PAVE, 0.92 + 0.12 * k)
            if (u * w * 6) % 1 < 0.08 or (v * h * 6) % 1 < 0.08:
                c = shade(PAVE, 0.84)
        elif kind == "asphalt":
            c = shade(rgb("#5E6472"), 0.9 + 0.14 * k)
        else:
            c = shade(GRASS, 0.88 + 0.2 * k)
            if tier == 1 and k > 0.86:
                c = rgb("#9C8A5E")  # kahle Stellen
        if tier == 3 and (min(u * w, (1 - u) * w, v * h, (1 - v) * h) < 0.05):
            c = rgb("#B9B2A2")  # Einfassung
        return c
    s.ground(0.03, 0.03, w - 0.03, h - 0.03, tex)


def palette(tier, wall_hex, seed):
    """Wandmaterial und -farbe je Epoche."""
    if tier == 1:
        return ("brick", shade(rgb("#9A5A44"), 0.95)) if seed % 2 else ("wood", rgb("#A8865E"))
    if tier == 2:
        return ("render", rgb(wall_hex))
    return ("glass", rgb("#9DB8CF")) if seed % 3 == 0 else ("panel", mix(rgb(wall_hex), WHITE, 0.4))


def walls(s, x0, y0, x1, y1, z, tier, wall_hex, seed, floors=1, door=True):
    """Baukörper mit Fenstern; Holz bekommt Bretter, Glas Pfosten-Riegel, alt dunklere Fenster."""
    material, color = palette(tier, wall_hex, seed)
    fh = max(9, (z - 2) / max(1, floors))
    lw, rw = (x1 - x0) * 36, (y1 - y0) * 36
    if material == "wood":
        def wood(light):
            def t(u, v):
                c = shade(color, 0.92 + 0.12 * ((int(u * lw / 3)) % 2))
                if v * z % fh > fh * 0.35 and v * z % fh < fh * 0.7 and (u * lw) % 14 > 5 and (u * lw) % 14 < 11:
                    c = rgb("#3D4A5C")
                return shade(c, light)
            return t
        s.wall_left(x0, x1, y1, 0, z, wood(1.0))
        s.wall_right(x1, y0, y1, 0, z, wood(0.76))
    else:
        s.wall_left(x0, x1, y1, 0, z, wall_texture(material, color, lw, z, floors, fh, seed, 1.0))
        s.wall_right(x1, y0, y1, 0, z, wall_texture(material, color, rw, z, floors, fh, seed + 7, 0.76,
                                                    door=(0.4, 0.6) if door else None))
    return material, color


def gable(s, x0, y0, x1, y1, z, roof_hex, tier, seed):
    """Satteldach (First entlang der längeren Seite); alt verblasst."""
    roof = rgb(roof_hex) if tier > 1 else mix(rgb(roof_hex), rgb("#7A6E62"), 0.55)
    rise = 7 + 5 * min(x1 - x0, y1 - y0)
    e = 0.06
    if (x1 - x0) >= (y1 - y0):
        ym = (y0 + y1) / 2
        a, b, c = s.p(x0 - e, y0 - e, z), s.p(x1 + 0.02, y0 - e, z), s.p(x0 - e, ym, z + rise)
        s.face(c, (b[0] - a[0], b[1] - a[1]), (a[0] - c[0], a[1] - c[1]), roof_texture(roof, (x1 - x0) * 36, 30, 1.08, seed))
        s.poly([s.p(x1, y0, z), s.p(x1, y1, z), s.p(x1, ym, z + rise)], shade(roof, 0.55))
        a, b, c = s.p(x0 - e, ym, z + rise), s.p(x1 + 0.02, ym, z + rise), s.p(x0 - e, y1 + e, z)
        s.face(a, (b[0] - a[0], b[1] - a[1]), (c[0] - a[0], c[1] - a[1]), roof_texture(roof, (x1 - x0) * 36, 30, 0.92, seed + 3))
    else:
        xm = (x0 + x1) / 2
        a, b, c = s.p(xm, y0 - e, z + rise), s.p(xm, y1 + 0.02, z + rise), s.p(x0 - e, y0 - e, z)
        s.face(a, (b[0] - a[0], b[1] - a[1]), (c[0] - a[0], c[1] - a[1]), roof_texture(roof, (y1 - y0) * 36, 30, 1.08, seed))
        s.poly([s.p(x0, y1, z), s.p(x1, y1, z), s.p(xm, y1, z + rise)], shade(roof, 0.6))
        a, b, c = s.p(xm, y0 - e, z + rise), s.p(xm, y1 + 0.02, z + rise), s.p(x1 + e, y0 - e, z)
        s.face(a, (b[0] - a[0], b[1] - a[1]), (c[0] - a[0], c[1] - a[1]), roof_texture(roof, (y1 - y0) * 36, 30, 0.8, seed + 3))
    return z + rise


def flat(s, x0, y0, x1, y1, z, tier, edge_hex, seed, green=None):
    """Flachdach mit Attika: solide Kies, modern begrünt oder mit Solarmodulen."""
    edge = rgb(edge_hex)
    s.wall_left(x0, x1, y1, z, z + 3, lambda u, v: shade(edge, 0.95))
    s.wall_right(x1, y0, y1, z, z + 3, lambda u, v: shade(edge, 0.75))
    n = noise(seed + 33)
    green = (tier == 3 and seed % 2 == 0) if green is None else green
    def tex(u, v):
        k = n(int(u * 50), int(v * 50))
        if tier == 3 and green:
            return shade(rgb("#6FA858"), 0.85 + 0.25 * k)
        if tier == 3:
            if 0.12 < u < 0.88 and 0.12 < v < 0.88:
                return rgb("#A9C3E8") if (u * 10) % 1 < 0.1 or (v * 6) % 1 < 0.12 else rgb("#22447F")
        return shade(rgb("#9DA3A8"), 0.88 + 0.2 * k)
    s.ground(x0, y0, x1, y1, tex, z=z + 3)
    s.ground(x0, y0, x1, y0 + 0.06, lambda u, v: edge, z=z + 3)
    s.ground(x0, y0, x0 + 0.06, y1, lambda u, v: edge, z=z + 3)
    return z + 3


def barrel(s, x0, y0, x1, y1, z, color_hex, tier):
    """Tonnendach (Halle) entlang der längeren Seite, aus Streifen angenähert."""
    c = rgb(color_hex) if tier > 1 else mix(rgb(color_hex), rgb("#8C8478"), 0.5)
    rise = 10 + 4 * min(x1 - x0, y1 - y0)
    steps = 10
    along = (x1 - x0) >= (y1 - y0)
    import math
    for i in range(steps):
        t0, t1 = i / steps, (i + 1) / steps
        z0 = z + math.sin(t0 * math.pi) * rise
        z1 = z + math.sin(t1 * math.pi) * rise
        light = 1.1 - 0.35 * t0
        col = shade(c, light)
        if along:
            ya, yb = y0 + (y1 - y0) * t0, y0 + (y1 - y0) * t1
            a, b, d = s.p(x0, ya, z0), s.p(x1, ya, z0), s.p(x0, yb, z1)
        else:
            xa, xb = x0 + (x1 - x0) * t0, x0 + (x1 - x0) * t1
            a, b, d = s.p(xa, y1, z0), s.p(xa, y0, z0), s.p(xb, y1, z1)
        rib = shade(col, 0.85)
        s.face(a, (b[0] - a[0], b[1] - a[1]), (d[0] - a[0], d[1] - a[1]),
               lambda u, v, col=col, rib=rib: rib if (u * 12) % 1 < 0.08 else col)
    # Giebelfläche vorn
    if along:
        pts = [s.p(x1, y0 + (y1 - y0) * i / steps, z + math.sin(i / steps * math.pi) * rise) for i in range(steps + 1)]
        s.poly([s.p(x1, y0, z), s.p(x1, y1, z)] + pts[::-1], shade(rgb("#C9D0DB"), 0.72))
    else:
        pts = [s.p(x0 + (x1 - x0) * i / steps, y1, z + math.sin(i / steps * math.pi) * rise) for i in range(steps + 1)]
        s.poly([s.p(x1, y1, z), s.p(x0, y1, z)] + pts, shade(rgb("#C9D0DB"), 0.9))
    return z + rise


def sign(s, x0, x1, y, z, glyph, tier, led=False):
    """Schild an der linken Vorderwand: Vereinsfarbe (Maske) mit weißem Symbol; modern als Leuchtband."""
    g = GLYPHS.get(glyph)
    hpx = 7
    def tex(u, v):
        if u < 0.04 or u > 0.96 or v < 0.1 or v > 0.9:
            return rgb("#F1EEE6") if tier < 3 else mask(0.6)
        if g:
            gx, gy = int((u - 0.08) / 0.84 * 9) - 2, int((v - 0.1) / 0.8 * 5)
            if 0 <= gx < 5 and 0 <= gy < 5 and g[gy][gx] == "x":
                return rgb("#FFFFFF")
        return mask(1.0 if led else 0.9)
    s.wall_left(x0, x1, y + 0.01, z, z + hpx, tex)


def awning(s, x0, x1, y, z, depth=0.18):
    """Markise in Vereinsfarbe mit Streifen vor der linken Vorderwand."""
    a, b, c = s.p(x0, y, z), s.p(x1, y, z), s.p(x0, y + depth, z - 5)
    s.face(a, (b[0] - a[0], b[1] - a[1]), (c[0] - a[0], c[1] - a[1]),
           lambda u, v: mask(1.0) if int(u * 10) % 2 else WHITE)


def canopy(s, x0, x1, y, z, depth=0.2):
    """Modernes Vordach (Glas/Stahl) vor der linken Vorderwand."""
    s.ground(x0, y, x1, y + depth, lambda u, v: (205, 220, 235, 200) if v < 0.85 else rgb("#7D8796"), z=z)
    for x in (x0 + 0.03, x1 - 0.03):
        s.line(s.p(x, y + depth, 0), s.p(x, y + depth, z), rgb("#7D8796"))


def dish(s, x, y, z, r=4):
    cx, cy = s.p(x, y, z)
    for dy in range(-r, r + 1):
        for dx in range(-r, r + 1):
            if dx * dx + dy * dy * 2 <= r * r:
                s.dot(cx + dx, cy + dy - r, rgb("#E9EDF2") if dx < 0 else rgb("#BFC7D2"))
    s.line((cx, cy), (cx, cy + 2), rgb("#5E6472"))


def antenna(s, x, y, z, hgt=22):
    a = s.p(x, y, z)
    s.line(a, (a[0], a[1] - hgt), rgb("#5E6472"))
    for k in range(3, hgt, 5):
        s.line((a[0] - 2, a[1] - k), (a[0] + 2, a[1] - k), rgb("#8E98A8"))
    s.dot(a[0], a[1] - hgt, rgb("#E53935"))


def flagpoles(s, pts, z=0):
    for (x, y) in pts:
        a = s.p(x, y, z)
        s.line(a, (a[0], a[1] - 24), rgb("#C9CED6"))
        for dy in range(4):
            for dx in range(1, 7):
                s.dot(a[0] + dx, a[1] - 24 + dy, mask(0.95) if dy < 2 else WHITE)


def tables(s, x0, y0, x1, y1, n, seed, umbrellas=True):
    """Tische mit Sonnenschirmen (Vereinsfarbe) auf einer Fläche."""
    r = random.Random(seed)
    for _ in range(n):
        x, y = r.uniform(x0, x1), r.uniform(y0, y1)
        box(s, x - 0.05, y - 0.04, 0.1, 0.08, 0, 3, rgb("#9A6A3E"))
        if umbrellas:
            cx, cy = s.p(x, y, 10)
            s.line((cx, cy), (cx, cy + 7), rgb("#5E6472"))
            for dx in range(-5, 6):
                for dy in range(-2, 2):
                    if abs(dx) + abs(dy) * 2 <= 5:
                        s.dot(cx + dx, cy + dy, mask(0.95) if (dx // 2) % 2 else WHITE)


def vehicle(s, x, y, along, length, color, height=6):
    """Fahrzeug (Auto, Transporter, Bus) als Kasten mit Fensterband."""
    w = 0.16 if length < 0.4 else 0.2
    if along:
        box(s, x, y, length, w, 0, height, color)
        s.wall_left(x + 0.05, x + length - 0.05, y + w + 0.002, height - 4, height - 1, lambda u, v: rgb("#3D4A5C"))
    else:
        box(s, x, y, w, length, 0, height, color)
        s.wall_right(x + w + 0.002, y + 0.05, y + length - 0.05, height - 4, height - 1, lambda u, v: rgb("#3D4A5C"))


def fence(s, x0, y0, x1, y1, tier, hgt=6):
    """Zaun: alt Holz, sonst Stabgitter; nur die beiden vorderen Seiten sichtbar gezeichnet."""
    col = rgb("#8A6A44") if tier == 1 else rgb("#5E6A5A")
    step = 0.12
    for (ax, ay, bx, by) in ((x0, y1, x1, y1), (x1, y0, x1, y1)):
        L = max(abs(bx - ax), abs(by - ay))
        k = int(L / step)
        for i in range(k + 1):
            t = i / max(1, k)
            px, py = ax + (bx - ax) * t, ay + (by - ay) * t
            s.line(s.p(px, py, 0), s.p(px, py, hgt), col)
        s.line(s.p(ax, ay, hgt), s.p(bx, by, hgt), col)
        s.line(s.p(ax, ay, hgt * 0.5), s.p(bx, by, hgt * 0.5), col)


def goals(s, x0, y0, x1, y1, along, gh=6):
    """Zwei Tore an den Stirnseiten einer Fläche."""
    if along:
        ym = (y0 + y1) / 2; d = (y1 - y0) * 0.14
        ends = [((x0, ym - d), (x0, ym + d)), ((x1, ym - d), (x1, ym + d))]
    else:
        xm = (x0 + x1) / 2; d = (x1 - x0) * 0.14
        ends = [((xm - d, y0), (xm + d, y0)), ((xm - d, y1), (xm + d, y1))]
    for (pa, pb) in ends:
        A, B = s.p(*pa), s.p(*pb)
        for P in (A, B):
            s.line(P, (P[0], P[1] - gh), WHITE)
        s.line((A[0], A[1] - gh), (B[0], B[1] - gh), WHITE)


def playground(s, x0, y0, x1, y1):
    """Sandfläche mit Rutsche, Schaukel und Klettergerüst."""
    s.ground(x0, y0, x1, y1, lambda u, v: shade(rgb("#E3CF96"), 0.95 + 0.1 * ((u * 13 + v * 7) % 1 > 0.5)))
    mx, my = (x0 + x1) / 2, (y0 + y1) / 2
    box(s, x0 + 0.05, y0 + 0.05, 0.12, 0.12, 0, 10, rgb("#E2B13C"))
    s.line(s.p(x0 + 0.17, y0 + 0.11, 10), s.p(x0 + 0.4, y0 + 0.11, 1), rgb("#D8403E"), 2)
    for x in (mx, mx + 0.18):
        s.line(s.p(x, my + 0.1, 0), s.p(x, my + 0.1, 12), rgb("#2F6FC2"))
    s.line(s.p(mx, my + 0.1, 12), s.p(mx + 0.18, my + 0.1, 12), rgb("#2F6FC2"))


def steam(s, x, y, z, n=3):
    cx, cy = s.p(x, y, z)
    for k in range(n):
        for dx in range(-2 - k, 3 + k):
            for dy in range(-2, 2):
                if dx * dx + dy * dy * 3 <= (3 + k) ** 2:
                    s.dot(cx + dx + k * 2, cy - 4 - k * 6 + dy, (240, 242, 245, 150 - k * 35))


def hedge(s, x0, y0, x1, y1):
    box(s, x0, y0, x1 - x0, y1 - y0, 0, 5, rgb("#3F7F3A"))
