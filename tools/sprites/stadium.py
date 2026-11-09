"""Stadion-Bausteine auf einer 4x3-Grundfläche: Sockel mit Spielfeld, je Tribüne drei Größen mit und ohne Dach,
Flutlichtmasten. Sitze sind in Magenta-Tönen gezeichnet (Maske); das Spiel färbt sie in die Vereinsfarbe um."""
import math
import random
from iso import Sprite, rgb, shade, mix, noise
from buildings import box

W, H = 4, 3
PITCH = (0.85, 0.75, 3.15, 2.25)
OUT = 0.06
HEIGHTS = {1: 14, 2: 24, 3: 34}
CONCRETE = rgb("#8E98A8")
CROWD = [rgb("#F3E9DC"), rgb("#2A2F3A"), rgb("#D9B26B"), rgb("#7A8AA0"), rgb("#B04A3C")]


def mask(f):
    """Sitzfarbe als Maske: Helligkeit f in Rot und Blau, Grün = 0 (wird im Spiel ersetzt)."""
    v = max(0, min(255, int(200 * f)))
    return (v, 0, v, 255)


def base(seed=1):
    s = Sprite(W, H, 70)
    n = noise(seed)
    s.ground(0, 0, W, H, lambda u, v: shade(CONCRETE, 0.9 + 0.12 * n(int(u * 70), int(v * 50))))
    x0, y0, x1, y1 = PITCH
    L, S = x1 - x0, y1 - y0
    def grass(u, v):
        x, y = u * L, v * S
        c = rgb("#4C9A50") if int(u * 12) % 2 else rgb("#45904A")
        m, line = 0.1, 0.03
        if m <= x <= L - m and m <= y <= S - m:
            if min(abs(x - m), abs(x - (L - m)), abs(y - m), abs(y - (S - m)), abs(x - L / 2)) < line:
                return rgb("#EEF4EC")
            if abs(math.hypot(x - L / 2, y - S / 2) - S * 0.17) < line * 1.3:
                return rgb("#EEF4EC")
            bw, bh = L * 0.14, S * 0.52
            for xa, xb in ((m, m + bw), (L - m - bw, L - m)):
                if xa <= x <= xb and abs(abs(y - S / 2) - bh / 2) < line: return rgb("#EEF4EC")
            if abs(y - S / 2) <= bh / 2 and (abs(x - (m + bw)) < line or abs(x - (L - m - bw)) < line): return rgb("#EEF4EC")
        return c
    s.ground(x0, y0, x1, y1, grass, z=1)
    # Tore
    for gx in (x0 + 0.1, x1 - 0.1):
        a, b = s.p(gx, (y0 + y1) / 2 - 0.1, 1), s.p(gx, (y0 + y1) / 2 + 0.1, 1)
        for P in (a, b): s.line(P, (P[0], P[1] - 6), rgb("#FFFFFF"))
        s.line((a[0], a[1] - 6), (b[0], b[1] - 6), rgb("#FFFFFF"))
    # Ecktürme (Treppenhäuser)
    for (cx, cy) in ((OUT, OUT), (W - 0.75, OUT), (OUT, H - 0.69), (W - 0.75, H - 0.69)):
        box(s, cx + 0.12, cy + 0.12, 0.45, 0.4, 0, 20, rgb("#B7BFCB"))
    return s


def stand(side, level, roof, seed=1):
    s = Sprite(W, H, 80)
    r = random.Random(seed + level * 7 + len(side))
    hi = HEIGHTS[level]
    lo = 3
    x0, y0, x1, y1 = PITCH
    def seats(u, v):
        # v: 0 = Spielfeldkante (unten), 1 = Außenkante (oben); Ränge als Streifen, Treppen alle 1/6
        row = int(v * hi / 2.4)
        if (u * 6) % 1 < 0.05:
            return shade(CONCRETE, 1.1)
        if (v * hi / 2.4) % 1 < 0.25:
            return shade(CONCRETE, 0.7)
        if r.random() < 0.55:
            return mask(0.75 + 0.35 * r.random()) if r.random() < 0.7 else r.choice(CROWD)
        return mask(0.95 if row % 2 else 0.85)
    if side == "North":
        a, b, c = s.p(x0, y0, lo), s.p(x1, y0, lo), s.p(x0, OUT, hi)
        s.face(a, (b[0] - a[0], b[1] - a[1]), (c[0] - a[0], c[1] - a[1]), seats)
        s.wall_right(x1, OUT, y0, 0, hi, lambda u, v: shade(CONCRETE, 0.8) if v > (1 - u) * 0.0 else None)
        end = [s.p(x1, OUT, 0), s.p(x1, y0, 0), s.p(x1, y0, lo), s.p(x1, OUT, hi)]
        s.poly(end, shade(CONCRETE, 0.78))
        region = (x0, OUT, x1, y0)
    elif side == "West":
        a, b, c = s.p(x0, y1, lo), s.p(x0, y0, lo), s.p(OUT, y1, hi)
        s.face(a, (b[0] - a[0], b[1] - a[1]), (c[0] - a[0], c[1] - a[1]), seats)
        end = [s.p(OUT, y1, 0), s.p(x0, y1, 0), s.p(x0, y1, lo), s.p(OUT, y1, hi)]
        s.poly(end, shade(CONCRETE, 0.95))
        region = (OUT, y0, x0, y1)
    elif side == "South":
        facade(s, x0, H - OUT, x1, hi, left=True, seed=seed)
        end = [s.p(x1, y1, 0), s.p(x1, H - OUT, 0), s.p(x1, H - OUT, hi), s.p(x1, y1, lo)]
        s.poly(end, shade(CONCRETE, 0.78))
        region = (x0, y1, x1, H - OUT)
    else:
        facade(s, W - OUT, y0, y1, hi, left=False, seed=seed)
        end = [s.p(x1, y1, 0), s.p(W - OUT, y1, 0), s.p(W - OUT, y1, hi), s.p(x1, y1, lo)]
        s.poly(end, shade(CONCRETE, 0.95))
        region = (x1, y0, W - OUT, y1)
    if roof:
        rx0, ry0, rx1, ry1 = region
        z = hi + 9
        for (px, py) in ((rx0 + 0.05, ry0 + 0.05), (rx1 - 0.05, ry0 + 0.05), (rx0 + 0.05, ry1 - 0.05), (rx1 - 0.05, ry1 - 0.05)):
            s.line(s.p(px, py, hi * 0.5), s.p(px, py, z), rgb("#DDE3EA"))
        def glass(u, v):
            if (u * 10) % 1 < 0.12 or (v * 3) % 1 < 0.12:
                return rgb("#E9EEF4")
            return (220, 232, 245, 110)
        s.ground(rx0 - 0.05, ry0 - 0.05, rx1 + 0.05, ry1 + 0.05, glass, z=z)
    return s


def facade(s, a, b, c, hi, left, seed):
    """Außenfassade einer vorderen Tribüne: Beton mit Pfeilern, Werbeband in Vereinsfarbe, Eingänge."""
    def tex(u, v):
        zf = (1 - v) * hi
        if (u * 12) % 1 < 0.1:
            return shade(CONCRETE, 0.85 if left else 0.7)
        if hi * 0.55 < zf < hi * 0.75:
            return mask(1.0)  # Band in Vereinsfarbe
        if zf < 9 and (u * 6) % 1 > 0.6:
            return rgb("#3A3F4A")  # Eingänge
        return shade(rgb("#C9D0DB"), 1.0 if left else 0.78)
    if left:
        s.wall_left(a, c, b, 0, hi, tex)
    else:
        s.wall_right(a, b, c, 0, hi, tex)


def mast(level):
    s = Sprite(1, 1, 110)
    cx, cy = s.p(0.5, 0.5)
    hgt = 52 + 10 * level
    for k in range(-1, 2):
        s.line((cx + k, cy), (cx + k, cy - hgt), rgb("#5E6472") if k else rgb("#8E98A8"))
    for y in range(8):
        for x in range(-10, 11):
            c = rgb("#3A4256")
            if 1 < y < 7 and abs(x) < 9 and x % 3:
                c = rgb("#FFF6C2")
            s.dot(cx + x, cy - hgt - y, c)
    return s
