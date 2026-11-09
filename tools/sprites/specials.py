"""Boden, Bäume und Sondergebäude (Plätze, Parkplätze, Solar, Park, Pool, Biergarten, Türme, Baustelle)."""
import math
import random
from iso import Sprite, rgb, shade, mix, noise
from buildings import box, shadow, house

GRASS = rgb("#5FA34C")
PAVE = rgb("#CFC4AA")


def grass_tile(seed):
    s = Sprite(1, 1, 0)
    n = noise(seed)
    r = random.Random(seed)
    flowers = {(r.randrange(32), r.randrange(32)): r.choice([rgb("#F3E36B"), rgb("#F4F1EA"), rgb("#D97BB0")]) for _ in range(3)}
    def tex(u, v):
        i, j = int(u * 32), int(v * 32)
        if (i, j) in flowers:
            return flowers[(i, j)]
        k = n(i, j)
        c = shade(GRASS, 0.9 + 0.18 * k)
        if k > 0.93:
            c = shade(GRASS, 1.22)
        if k < 0.05:
            c = shade(GRASS, 0.78)
        return c
    s.ground(0, 0, 1, 1, tex)
    return s


def path_tile(seed):
    s = Sprite(1, 1, 0)
    n = noise(seed)
    def tex(u, v):
        i, j = u * 8, v * 8
        row = int(j)
        joint = j % 1 < 0.12 or (i + (row % 2) * 0.5) % 1 < 0.12
        return shade(PAVE, 0.82) if joint else shade(PAVE, 0.94 + 0.12 * n(int(i + row % 2 * 0.5), row))
    s.ground(0, 0, 1, 1, tex)
    return s


def gate_tile():
    s = Sprite(1, 1, 30)
    s.ground(0, 0, 1, 1, lambda u, v: shade(PAVE, 1.08))
    for (x, y) in ((0.15, 0.5), (0.85, 0.5)):
        box(s, x - 0.05, y - 0.05, 0.1, 0.1, 0, 22, rgb("#4A5263"))
    a, b = s.p(0.1, 0.5, 22), s.p(0.9, 0.5, 22)
    s.line(a, b, rgb("#E2B13C"), 3)
    s.line((a[0], a[1] - 2), (b[0], b[1] - 2), rgb("#F5D77A"), 1)
    return s


def tree(kind, seed):
    """Laubbaum, Nadelbaum oder Busch; Fußpunkt liegt am Anker (Sprite 1x1 ohne Boden)."""
    s = Sprite(1, 1, 46)
    r = random.Random(seed)
    cx, cy = s.p(0.5, 0.5)
    for k in range(40):  # Schatten
        a = r.random() * math.tau; d = r.random()
        s.dot(cx + 6 + math.cos(a) * 10 * d, cy + math.sin(a) * 4 * d, (0, 0, 0, 70))
    if kind == "bush":
        blob(s, cx, cy - 5, 8, 6, rgb("#3F8A3A"), r)
        return s
    s.line((cx, cy), (cx, cy - 14), rgb("#6B4A32"), 2)
    if kind == "pine":
        for i in range(5):
            w = 11 - i * 2; y = cy - 10 - i * 6
            for dy in range(7):
                for dx in range(-w + dy, w - dy + 1):
                    c = rgb("#2E6B3A") if dx > 0 else rgb("#3C8447")
                    if r.random() < 0.15: c = shade(c, 1.2)
                    s.dot(cx + dx, y - dy, c)
    else:
        blob(s, cx, cy - 24, 13, 12, rgb("#3B8A3E"), r)
    return s


def blob(s, cx, cy, rx, ry, base, r):
    for y in range(-ry, ry + 1):
        for x in range(-rx, rx + 1):
            d = (x / rx) ** 2 + (y / ry) ** 2
            if d > 1 or (d > 0.75 and r.random() < 0.5):
                continue
            light = 1.25 if x + y < -rx * 0.4 else 0.78 if x + y > rx * 0.6 else 1.0
            c = shade(base, light * (0.88 + 0.24 * r.random()))
            s.dot(cx + x, cy + y, c)


def pitch(w, h, turf=False, seed=1):
    s = Sprite(w, h, 10)
    base = rgb("#2F9078") if turf else rgb("#4C9A50")
    along = w >= h
    L, S = (w, h) if along else (h, w)
    def tex(u, v):
        a, b = (u, v) if along else (v, u)
        c = base if turf or int(a * 10) % 2 else shade(base, 0.9)
        # Linien: Außenlinie, Mittellinie, Mittelkreis, Strafräume
        m = 0.08
        x, y = a * L, b * S
        line = 0.035
        if m <= x <= L - m and m <= y <= S - m:
            if abs(x - m) < line or abs(x - (L - m)) < line or abs(y - m) < line or abs(y - (S - m)) < line:
                return rgb("#EEF4EC")
            if abs(x - L / 2) < line:
                return rgb("#EEF4EC")
            if abs(math.hypot(x - L / 2, (y - S / 2)) - S * 0.18) < line * 1.2:
                return rgb("#EEF4EC")
            bw, bh = L * 0.14, S * 0.5
            for x0 in (m, L - m - bw):
                if x0 <= x <= x0 + bw and abs(abs(y - S / 2) - bh / 2) < line:
                    return rgb("#EEF4EC")
                if abs(y - S / 2) <= bh / 2 and (abs(x - (x0 + bw)) < line if x0 == m else abs(x - x0) < line):
                    return rgb("#EEF4EC")
        return c
    s.ground(0.05, 0.05, w - 0.05, h - 0.05, tex)
    for g in ((0.08, None), (L - 0.08, None)):
        gx = g[0]
        pa = (gx, S / 2 - 0.12); pb = (gx, S / 2 + 0.12)
        if not along: pa, pb = (pa[1], pa[0]), (pb[1], pb[0])
        A, B = s.p(*pa), s.p(*pb)
        for P in (A, B): s.line(P, (P[0], P[1] - 7), rgb("#FFFFFF"))
        s.line((A[0], A[1] - 7), (B[0], B[1] - 7), rgb("#FFFFFF"))
    return s


def court():
    s = pitch(1, 1, turf=True, seed=3)
    for (x0, y0, x1, y1) in ((0.05, 0.95, 0.95, 0.95), (0.95, 0.05, 0.95, 0.95)):
        a0, a1 = s.p(x0, y0, 9), s.p(x1, y1, 9)
        s.line(a0, a1, rgb("#D8DDE3"))
        for k in range(6):
            t = k / 5
            px, py = x0 + (x1 - x0) * t, y0 + (y1 - y0) * t
            s.line(s.p(px, py, 0), s.p(px, py, 9), rgb("#AEB5BE"))
    return s


def track():
    s = Sprite(1, 1, 6)
    def tex(u, v):
        if (v * 6) % 1 < 0.1: return rgb("#F1E9E2")
        return shade(rgb("#C25A40"), 0.95 + 0.1 * ((u * 30) % 1 > 0.5))
    s.ground(0.05, 0.05, 0.95, 0.95, tex)
    return s


CARS = [rgb("#D8403E"), rgb("#EDEFF2"), rgb("#2F6FC2"), rgb("#E9B832"), rgb("#2A2F3A"), rgb("#7FA0B8")]


def parking(w, h, seed=1):
    s = Sprite(w, h, 10)
    n = noise(seed)
    along = w >= h
    L = w if along else h
    bays = max(2, int(L * 4))
    def tex(u, v):
        a, b = (u, v) if along else (v, u)
        c = shade(rgb("#5E6472"), 0.92 + 0.12 * n(int(u * 40), int(v * 40)))
        if (a * bays) % 1 < 0.06 and (b < 0.38 or b > 0.62):
            c = rgb("#E8EAEE")
        return c
    s.ground(0.05, 0.05, w - 0.05, h - 0.05, tex)
    r = random.Random(seed)
    for row in (0, 1):
        for k in range(bays):
            if r.random() > 0.65: continue
            c = r.choice(CARS)
            t = (k + 0.2) / bays * (L - 0.1) + 0.05
            sz = (L - 0.1) / bays * 0.6
            if along:
                x, y, cw, ch = t, (0.1 if row == 0 else h * 0.64), sz, h * 0.26
            else:
                y, x, ch, cw = t, (0.1 if row == 0 else w * 0.64), sz, w * 0.26
            box(s, x, y, cw, ch, 0, 3, c)
            box(s, x + cw * 0.2, y + ch * 0.2, cw * 0.6, ch * 0.6, 3, 5, mix(c, rgb("#9FC1E0"), 0.6))
    return s


def solar():
    s = Sprite(1, 1, 16)
    s.ground(0.05, 0.05, 0.95, 0.95, lambda u, v: shade(rgb("#7FA36E"), 0.95 + 0.1 * ((u * 20 + v * 13) % 1 > 0.5)))
    cell, frame, hi = rgb("#22447F"), rgb("#C9D3DE"), rgb("#6F98D0")
    def panel(u, v):
        if u < 0.04 or u > 0.96 or v < 0.1 or v > 0.9:
            return frame
        if (u * 4) % 1 < 0.06:
            return shade(cell, 0.7)  # Fuge zwischen Modulen
        return hi if v < 0.35 and (u * 4) % 1 > 0.55 else cell  # Lichtreflex
    for r in range(3):
        y0 = 0.14 + r * 0.27
        # Ständer hinten, dann Modulreihe (nach vorn geneigt), dann Kante unten
        for x in (0.15, 0.5, 0.85):
            s.line(s.p(x, y0, 0), s.p(x, y0, 9), rgb("#5E6472"))
        a, b, c = s.p(0.1, y0, 9), s.p(0.9, y0, 9), s.p(0.1, y0 + 0.17, 3)
        s.face(a, (b[0] - a[0], b[1] - a[1]), (c[0] - a[0], c[1] - a[1]), panel)
        s.line(s.p(0.1, y0 + 0.17, 2), s.p(0.9, y0 + 0.17, 2), rgb("#3A3F4A"))
    return s


def park(seed=1):
    s = Sprite(1, 1, 46)
    s.ground(0.03, 0.03, 0.97, 0.97, lambda u, v: rgb("#D8CDB2") if abs(v - (0.7 - 0.3 * math.sin(u * 3))) < 0.06 else shade(rgb("#6CB458"), 0.95 + 0.1 * ((u * 23 + v * 31) % 1 > 0.6)))
    for (x, y, k) in ((0.25, 0.3, "oak"), (0.72, 0.28, "oak"), (0.55, 0.62, "bush")):
        t = tree(k, int(x * 100))
        cx, cy = s.p(x, y)
        s.img.alpha_composite(t.img, (int(cx - t.ax), int(cy - t.ay - 16)))
    return s


def pool(ice=False):
    s = Sprite(1, 1, 8)
    box(s, 0.05, 0.05, 0.9, 0.9, 0, 3, rgb("#D9DEE6"))
    water = rgb("#A9DDF2") if ice else rgb("#3AA6E0")
    s.ground(0.15, 0.15, 0.85, 0.85, lambda u, v: rgb("#FFFFFF") if (v * 3) % 1 < 0.05 else shade(water, 0.95 + 0.15 * ((u * 9 + v * 4) % 1 > 0.7)), z=3)
    return s


def garden(seed=1):
    s = Sprite(1, 1, 24)
    s.ground(0.05, 0.05, 0.95, 0.95, lambda u, v: shade(rgb("#C9B68E"), 0.92 + 0.12 * ((u * 17 + v * 11) % 1 > 0.5)))
    r = random.Random(seed)
    for k in range(4):
        x, y = 0.2 + 0.6 * r.random(), 0.2 + 0.6 * r.random()
        box(s, x - 0.08, y - 0.04, 0.16, 0.08, 0, 4, rgb("#8A6A44"))
        cx, cy = s.p(x, y, 4)
        s.line((cx, cy), (cx, cy - 10), rgb("#5A4632"))
        col = rgb("#E9792E") if k % 2 == 0 else rgb("#F3EFE6")
        for dy in range(-3, 1):
            for dx in range(-9 - dy * 2, 10 + dy * 2):
                s.dot(cx + dx, cy - 11 + dy, shade(col, 1.1 if dx < 0 else 0.85))
    return s


def tower(led=False):
    s = Sprite(1, 1, 64)
    cx, cy = s.p(0.5, 0.5)
    for k in range(30):
        s.dot(cx + 4 + k % 10, cy + (k // 10), (0, 0, 0, 60))
    s.line((cx, cy), (cx, cy - 44), rgb("#5E6472"), 3)
    if led:
        for y in range(16):
            for x in range(-14, 15):
                c = rgb("#141B2B")
                if 2 < y < 13 and abs(x) < 12:
                    c = [rgb("#3DDC97"), rgb("#F5C04E"), rgb("#E0565C")][(y // 4) % 3] if (x + y) % 3 else shade(c, 1.4)
                s.dot(cx + x, cy - 60 + y, c)
    else:
        for y in range(6):
            for x in range(-8, 9):
                s.dot(cx + x, cy - 50 + y, rgb("#FFF3B0") if y in (2, 3) and x % 3 else rgb("#3A4256"))
    return s


def tank(chp=False):
    if chp:
        s = house("energy", 1, 1, 1, seed=5)
        cx, cy = s.p(0.8, 0.2)
        return s
    s = Sprite(1, 1, 30)
    cx, cy = s.p(0.5, 0.5)
    for y in range(22):
        for x in range(-15, 16):
            s.dot(cx + x, cy - y, shade(rgb("#AEB5BE"), 1.15 if x < -4 else 0.8 if x > 6 else 1.0))
    for y in range(-6, 7):
        for x in range(-15, 16):
            if (x / 15) ** 2 + (y / 7.5) ** 2 <= 1:
                s.dot(cx + x, cy - 22 + y, rgb("#D4D9DF"))
    return s


def monument(chapel=False):
    if chapel:
        s = house("", 1, 1, 1, seed=9)
        return s
    s = Sprite(1, 1, 40)
    s.ground(0.1, 0.1, 0.9, 0.9, lambda u, v: shade(PAVE, 1.05))
    box(s, 0.35, 0.35, 0.3, 0.3, 0, 10, rgb("#B9C1CE"))
    cx, cy = s.p(0.5, 0.5, 10)
    for y in range(18):
        for x in range(-3, 4):
            if y < 13 or abs(x) < 2:
                s.dot(cx + x, cy - y, shade(rgb("#B08D3C"), 1.2 if x < 0 else 0.85))
    return s


def construction(w, h):
    s = Sprite(w, h, 56)
    s.ground(0.05, 0.05, w - 0.05, h - 0.05, lambda u, v: rgb("#E2B13C") if (u * w * 6 + v * h * 6) % 1 < 0.35 else rgb("#A08A68"))
    box(s, 0.15, 0.15, w - 0.3, h - 0.3, 0, 10, rgb("#B8B2A6"))
    for (x, y) in ((0.1, h - 0.1), (w - 0.1, h - 0.1), (w - 0.1, 0.1)):
        s.line(s.p(x, y), s.p(x, y, 20), rgb("#E2B13C"))
    s.line(s.p(0.1, h - 0.1, 20), s.p(w - 0.1, h - 0.1, 20), rgb("#E2B13C"))
    s.line(s.p(w - 0.1, h - 0.1, 20), s.p(w - 0.1, 0.1, 20), rgb("#E2B13C"))
    m = s.p(0.25, 0.25)
    s.line(m, (m[0], m[1] - 50), rgb("#E9B832"), 2)
    s.line((m[0] - 12, m[1] - 48), (m[0] + 34, m[1] - 48), rgb("#E9B832"), 2)
    s.line((m[0] + 28, m[1] - 48), (m[0] + 28, m[1] - 26), rgb("#D9DEE6"))
    return s
