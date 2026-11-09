"""Gebäudekatalog: je Gebäudetyp aus data/base/rules/buildings.json ein eigenes Sprite je Ausbaustufe.

Flächen werden relativ zur Grundfläche angegeben (0..1, lange Seite = x) und bei gedrehter Grundfläche gespiegelt;
so passt jedes Sprite zu jeder Größe und Drehung. Die Stufe bestimmt Epoche (alt → modern) und Größe des Baukörpers.
"""
import math
from iso import Sprite, rgb, shade, mix, noise
from buildings import box, chimney, CATEGORIES
import kit as K
from kit import tier_of
from stadium import mask
import specials as X


class Ctx:
    def __init__(self, w, h, level, max_level, cat, seed):
        self.w, self.h, self.level, self.max = w, h, level, max_level
        self.tier = tier_of(level, max_level)
        self.cat = cat
        self.seed = seed
        self.along = w >= h
        _, self.wall, self.roof = CATEGORIES.get(cat, CATEGORIES[""])

    def r(self, fx0, fy0, fx1, fy1):
        """Relative Fläche (lange Seite = x) in Rasterkoordinaten der echten Grundfläche."""
        if not self.along:
            fx0, fy0, fx1, fy1 = fy0, fx0, fy1, fx1
        return fx0 * self.w, fy0 * self.h, fx1 * self.w, fy1 * self.h

    def pt(self, fx, fy):
        if not self.along:
            fx, fy = fy, fx
        return fx * self.w, fy * self.h

    def grow(self, base=1, per=2):
        """Stockwerke: Grundzahl plus eins je `per` Stufen."""
        return base + (self.level - 1) // per


def block(c, s, rect, floors, glyph=None, roof=None, wall=None, roof_hex=None, floor_h=12):
    """Baukörper auf relativer Fläche mit Dach nach Epoche, Schild und (modern) Vordach. Rückgabe: Oberkante."""
    x0, y0, x1, y1 = c.r(*rect)
    z = floors * floor_h + 2
    K.walls(s, x0, y0, x1, y1, z, c.tier, wall or c.wall, c.seed, floors)
    roof = roof or ("gable" if c.tier < 3 else "flat")
    if glyph:
        sx0 = x0 + (x1 - x0) * 0.25; sx1 = x0 + (x1 - x0) * 0.75
        K.sign(s, sx0, sx1, y1, z - 9, glyph, c.tier, led=c.tier == 3)
    if c.tier == 3:
        K.canopy(s, x0 + (x1 - x0) * 0.55, x0 + (x1 - x0) * 0.9, y1, 9)
    if roof == "gable":
        top = K.gable(s, x0, y0, x1, y1, z, roof_hex or c.roof, c.tier, c.seed)
        if c.tier == 1:
            chimney(s, x0 + (x1 - x0) * 0.25, y0 + 0.1, z + 4, None)
        return top
    if roof == "barrel":
        return K.barrel(s, x0, y0, x1, y1, z, roof_hex or "#B8C2C9", c.tier)
    return K.flat(s, x0, y0, x1, y1, z, c.tier, roof_hex or c.roof, c.seed)


def new(c, height, ground="grass"):
    s = Sprite(c.w, c.h, height)
    K.lot(s, c.w, c.h, c.tier, ground, c.seed)
    return s


# ------------------------------------------------------------------ Plätze

def pitch_area(c, s, rect, turf=False, lines=True, small=False):
    x0, y0, x1, y1 = c.r(*rect)
    base = rgb("#2F9078") if turf else rgb("#4C9A50")
    if c.tier == 1 and not turf:
        base = mix(base, rgb("#8A8A4E"), 0.3)
    n = noise(c.seed + 7)
    L, S = (x1 - x0, y1 - y0) if c.along else (y1 - y0, x1 - x0)
    def tex(u, v):
        a, b = (u, v) if c.along else (v, u)
        col = base if (turf or int(a * 10) % 2) else shade(base, 0.9)
        if c.tier == 1 and n(int(u * 30), int(v * 30)) > 0.9:
            col = rgb("#9C8A5E")
        if lines and (c.tier > 1 or c.level >= 2 or small):
            x, y = a * L, b * S
            m, ln = 0.06 * S, 0.03
            if m <= x <= L - m and m <= y <= S - m:
                if min(abs(x - m), abs(x - (L - m)), abs(y - m), abs(y - (S - m)), abs(x - L / 2)) < ln:
                    return rgb("#EEF4EC")
                if not small and abs(math.hypot(x - L / 2, y - S / 2) - S * 0.17) < ln * 1.2:
                    return rgb("#EEF4EC")
        return col
    s.ground(x0, y0, x1, y1, tex)
    K.goals(s, x0 + 0.04, y0, x1 - 0.04, y1, c.along, 5 if small else 7)
    return x0, y0, x1, y1


def p_pitch(c, turf=False):
    s = new(c, 40)
    x0, y0, x1, y1 = pitch_area(c, s, (0.06, 0.12, 0.94, 0.88), turf)
    if c.tier >= 2:
        bx, by = c.pt(0.5, 0.94)
        box(s, bx - 0.15, by - 0.04, 0.3, 0.07, 0, 5, rgb("#5E6A7A"))  # Trainerbank
    if c.level == c.max and c.max >= 4:
        for fx in (0.38, 0.62):  # Spielerkabinen
            bx, by = c.pt(fx, 0.95)
            box(s, bx - 0.08, by - 0.05, 0.16, 0.06, 0, 7, rgb("#C9D1DB"))
            s.ground(bx - 0.08, by - 0.05, bx + 0.08, by + 0.01, lambda u, v: (205, 220, 235, 200), z=7)
    if c.tier == 3:
        K.fence(s, 0.04, 0.04, c.w - 0.04, c.h - 0.04, 3, 8)
        for (fx, fy) in ((0.03, 0.06), (0.97, 0.06), (0.03, 0.94), (0.97, 0.94)):
            px, py = c.pt(fx, fy)
            a = s.p(px, py)
            s.line(a, (a[0], a[1] - 30), rgb("#8E98A8"))
            for dx in range(-3, 4):
                s.dot(a[0] + dx, a[1] - 31, rgb("#FFF6C2"))
    return s


def p_youth_pitches(c):
    s = new(c, 30)
    for rect in ((0.04, 0.08, 0.48, 0.92), (0.52, 0.08, 0.96, 0.92)):
        pitch_area(c, s, rect, turf=c.tier == 3, small=True)
    if c.tier == 3:
        K.fence(s, 0.03, 0.03, c.w - 0.03, c.h - 0.03, 3)
    return s


def p_goalkeeper(c):
    s = new(c, 30)
    pitch_area(c, s, (0.1, 0.15, 0.9, 0.85), turf=c.tier >= 2, small=True)
    if c.tier >= 2:  # Ballmaschine
        x, y = c.pt(0.7, 0.5)
        box(s, x - 0.06, y - 0.06, 0.12, 0.12, 0, 6, rgb("#E2B13C"))
    if c.tier == 3:
        K.fence(s, 0.05, 0.05, c.w - 0.05, c.h - 0.05, 3, 10)
    return s


def p_setpiece(c):
    s = new(c, 30)
    pitch_area(c, s, (0.06, 0.1, 0.94, 0.9), turf=c.tier == 2, small=True)
    for k in range(3 + c.level):  # Freistoßmauer aus Dummys
        x, y = c.pt(0.45, 0.3 + k * 0.08)
        box(s, x, y, 0.05, 0.05, 0, 9, rgb("#E2B13C") if c.tier > 1 else rgb("#C9CED6"))
    return s


def p_court(c):
    s = new(c, 30, "asphalt" if c.tier == 1 else "grass")
    pitch_area(c, s, (0.08, 0.08, 0.92, 0.92), turf=True, small=True)
    K.fence(s, 0.06, 0.06, c.w - 0.06, c.h - 0.06, max(2, c.tier), 10)
    return s


def p_track(c):
    s = new(c, 20)
    x0, y0, x1, y1 = c.r(0.04, 0.2, 0.96, 0.8)
    col = rgb("#A0644A") if c.tier == 1 else rgb("#C25A40") if c.tier == 2 else rgb("#3E6FB0")
    def tex(u, v):
        a, b = (u, v) if c.along else (v, u)
        if (b * 5) % 1 < 0.08:
            return rgb("#F1E9E2")
        return shade(col, 0.95 + 0.1 * ((a * 40) % 1 > 0.5))
    s.ground(x0, y0, x1, y1, tex)
    if c.tier >= 2:
        for k in range(3):
            px, py = c.pt(0.3 + k * 0.2, 0.5)
            s.line(s.p(px, py - 0.2 if c.along else py, 4), s.p(px, py + 0.2 if c.along else py, 4), WHITE)
    return s


# ------------------------------------------------------------------ Gebäude (Standard)

def scaled(c, rect):
    """Baukörper wächst mit der Stufe: von 80 % (Stufe 1) bis 100 % (Höchststufe), vorne links verankert."""
    k = 0.8 + 0.2 * ((c.level - 1) / max(1, c.max - 1))
    x0, y0, x1, y1 = rect
    return (x0, y0 + (y1 - y0) * (1 - k), x0 + (x1 - x0) * k, y1)


def b_std(glyph, rect=(0.12, 0.15, 0.88, 0.8), base=1, per=2, roof=None, ground="grass", extras=(), pre=(), wall=None):
    """Standardgebäude: Grundstück, optional Vorarbeiten (pre), Baukörper mit Schild, danach Zubehör (extras)."""
    def make(c):
        floors = c.grow(base, per)
        s = new(c, floors * 12 + 70, ground)
        for f in pre:
            f(c, s)
        top = block(c, s, scaled(c, rect), floors, glyph, roof, wall=wall)
        for f in extras:
            f(c, s, top)
        return s
    return make


WHITE = K.WHITE


def x_dish(c, s, top):
    x, y = c.r(0.3, 0.25, 0.4, 0.35)[:2]
    K.dish(s, x, y, top)


def x_antenna(c, s, top):
    x, y = c.r(0.7, 0.3, 0.7, 0.3)[:2]
    K.antenna(s, x, y, top, 18 + 4 * c.level)
    K.dish(s, *c.r(0.3, 0.3, 0.3, 0.3)[:2], top, 5)


def x_tanks(c, s, top):
    for k in range(min(3, c.level + 1)):
        x, y = c.pt(0.08 + k * 0.08, 0.9)
        box(s, x, y - 0.06, 0.06, 0.06, 0, 14, rgb("#C9CED6"))


def x_garden(c, s):
    x0, y0, x1, y1 = c.r(0.05, 0.84, 0.95, 0.97)
    K.hedge(s, x0, y0, x1, y1)


def x_tables(c, s, top):
    x0, y0, x1, y1 = c.r(0.15, 0.82, 0.85, 0.96)
    K.tables(s, x0, y0, x1, y1, 2 + c.level, c.seed, umbrellas=c.tier > 1)


def x_awning(c, s, top):
    x0, y0, x1, y1 = c.r(0.12, 0.15, 0.88, 0.8)
    K.awning(s, x0 + 0.05, x1 - 0.05, y1, 12)


def x_flags(c, s, top):
    pts = [c.pt(0.3 + k * 0.15, 0.92) for k in range(min(4, c.level + 1))]
    K.flagpoles(s, pts)


def x_van(c, s, top):
    x, y = c.pt(0.15, 0.88)
    K.vehicle(s, x, y - 0.1, c.along, 0.35, rgb("#EDEFF2"), 8)
    K.dish(s, x + 0.1, y - 0.05, 9, 3)


def x_steam(c, s, top):
    x, y = c.r(0.25, 0.25, 0.25, 0.25)[:2]
    box(s, x, y, 0.1, 0.1, top - 4, top + 14, rgb("#8C5A44"))
    K.steam(s, x + 0.05, y + 0.05, top + 14)


def x_playground(c, s, top):
    K.playground(s, *c.r(0.55, 0.82, 0.95, 0.98))


def x_ambulance(c, s, top):
    x, y = c.pt(0.15, 0.88)
    K.vehicle(s, x, y - 0.1, c.along, 0.32, rgb("#F4F2EC"), 8)


def x_bikes(c, s, top):
    x0, y0, x1, y1 = c.r(0.15, 0.85, 0.85, 0.95)
    for k in range(6):
        t = x0 + (x1 - x0) * k / 6
        s.line(s.p(t, y0, 0), s.p(t, y0, 4), rgb("#3A3F4A"))


def x_columns(c, s, top):
    x0, y0, x1, y1 = c.r(0.12, 0.15, 0.88, 0.8)
    for k in range(5):
        x = x0 + 0.08 + (x1 - x0 - 0.16) * k / 4
        box(s, x, y1 + 0.02, 0.05, 0.05, 0, 18, rgb("#E8E2D4"))
    s.wall_left(x0, x1, y1 + 0.07, 18, 22, lambda u, v: rgb("#DCD4C2"))


def x_graffiti(c, s, top):
    x0, y0, x1, y1 = c.r(0.12, 0.15, 0.88, 0.8)
    cols = [rgb("#E53935"), rgb("#F5C04E"), rgb("#1F9AAD"), rgb("#7A45B8")]
    s.wall_left(x0 + 0.05, x1 - 0.4, y1 + 0.005, 2, 10, lambda u, v: cols[int(u * 7 + v * 3) % 4])


def x_clock(c, s, top):
    x0, y0, x1, y1 = c.r(0.12, 0.15, 0.88, 0.8)
    box(s, x0 + 0.05, y0 + 0.05, 0.16, 0.16, top - 6, top + 16, rgb("#D9CDB4"))
    cx, cy = s.p(x0 + 0.13, y0 + 0.21, top + 9)
    for dx in range(-2, 3):
        for dy in range(-2, 3):
            if dx * dx + dy * dy <= 5:
                s.dot(cx + dx, cy + dy, WHITE)


def x_billboard(c, s, top):
    x, y = c.pt(0.9, 0.9)
    s.line(s.p(x, y, 0), s.p(x, y, 14), rgb("#5E6472"))
    s.wall_left(x - 0.25, x + 0.05, y, 14, 24, lambda u, v: mask(0.9) if 0.1 < v < 0.9 else WHITE)


def x_pitchlet(c, s, top):
    pitch_area(c, s, (0.05, 0.82, 0.6, 0.98), turf=True, small=True)


def x_queue(c, s, top):
    x0, y0, x1, y1 = c.r(0.12, 0.15, 0.88, 0.8)
    for k in range(3):
        y = y1 + 0.08 + k * 0.05
        s.line(s.p(x0 + 0.1, y, 4), s.p(x1 - 0.1, y, 4), rgb("#C9CED6"))


