"""Gebäude-Sprites: Häuser mit Satteldach und Hallen mit Flachdach, Material und Dachfarbe je Bereich."""
import random
from iso import Sprite, rgb, shade, mix, noise

# Bereich: Wandmaterial, Wandfarbe, Dachfarbe
CATEGORIES = {
    "training": ("panel", "#C9CED6", "#2F6FC2"),
    "medical": ("render", "#F2F0EA", "#C8383F"),
    "youth": ("render", "#EFE2B8", "#7A45B8"),
    "fans": ("brick", "#B5533C", "#D8702A"),
    "commerce": ("glass", "#8FB6D8", "#D9A421"),
    "admin": ("stone", "#D9CDB4", "#55606E"),
    "infrastructure": ("brick", "#8C5A44", "#6E523F"),
    "energy": ("panel", "#B8C2C9", "#1F9AAD"),
    "teams": ("brick", "#A85A45", "#3E9A52"),
    "": ("render", "#E4DED2", "#7A8496"),
}

GLASS = rgb("#5D86B5")
GLASS_HI = rgb("#A9CBEA")
LIT = rgb("#F5D77A")
FRAME = rgb("#3A3F4A")


def wall_texture(material, base, length_px, height_px, floors, floor_h, seed, light, door=None):
    """Textur einer Wand: Material, Fensterreihen je Stockwerk, Sockel, optional Tür (u-Bereich)."""
    n = noise(seed)
    rnd = random.Random(seed)
    windows = max(1, int(length_px / 13))
    lit = {(f, k): rnd.random() < 0.18 for f in range(floors) for k in range(windows)}

    def tex(u, v):
        x = u * length_px
        y = v * height_px
        zf = height_px - y  # Höhe über dem Boden
        c = base
        if material == "brick":
            row = int(y / 3)
            mortar = y % 3 < 0.9 or (x + (row % 2) * 3) % 6 < 0.9
            c = shade(base, 1.18) if mortar else shade(base, 0.92 + 0.16 * n(int(x / 6), row))
        elif material == "render":
            c = shade(base, 0.97 + 0.06 * n(int(x / 2), int(y / 2)))
        elif material == "stone":
            row = int(y / 4)
            joint = y % 4 < 0.8 or (x + (row % 2) * 5) % 10 < 0.8
            c = shade(base, 0.85) if joint else shade(base, 0.95 + 0.1 * n(int(x / 10), row))
        elif material == "panel":
            c = shade(base, 0.86) if x % 8 < 0.9 else shade(base, 0.98 + 0.04 * n(int(x / 8), 0))
        elif material == "glass":
            c = GLASS_HI if (x + y * 0.5) % 9 < 1.2 else mix(base, GLASS, 0.35)
            if x % 10 < 1 or zf % floor_h < 1:
                c = shade(base, 0.6)
        # Sockel
        if zf < 2:
            c = shade(base, 0.62)
        # Fenster
        f = int((zf - 2) // floor_h)
        if material != "glass" and 0 <= f < floors and zf > 2:
            fz = (zf - 2) % floor_h
            k = int(u * windows)
            fu = (u * windows) % 1
            if door is not None and f == 0 and door[0] <= u <= door[1]:
                pass
            elif 0.22 < fu < 0.78 and floor_h * 0.28 < fz < floor_h * 0.82:
                edge = fu < 0.3 or fu > 0.7 or fz < floor_h * 0.36 or fz > floor_h * 0.74
                if edge:
                    c = FRAME
                else:
                    c = LIT if lit.get((f, k)) else (GLASS_HI if fz > floor_h * 0.62 and fu < 0.45 else GLASS)
        # Tür mit Vordach
        if door is not None and door[0] <= u <= door[1] and zf < floor_h * 0.8:
            du = (u - door[0]) / (door[1] - door[0])
            c = FRAME if du < 0.15 or du > 0.85 or zf > floor_h * 0.7 else rgb("#6B4A32")
        return shade(c, light)
    return tex


def roof_texture(color, length_px, slope_px, light, seed):
    n = noise(seed)
    def tex(u, v):
        x, y = u * length_px, v * slope_px
        row = int(y / 3)
        c = color
        if y % 3 < 0.9:
            c = shade(color, 0.72)
        elif (x + (row % 2) * 2) % 4 < 0.8:
            c = shade(color, 0.85)
        else:
            c = shade(color, 0.95 + 0.12 * n(int(x / 4), row))
        if v < 0.06:
            c = shade(color, 1.25)  # First
        return shade(c, light)
    return tex


def house(cat, w, h, floors, seed=1):
    material, wall_hex, roof_hex = CATEGORIES.get(cat, CATEGORIES[""])
    wall, roof = rgb(wall_hex), rgb(roof_hex)
    floor_h = 12
    zw = floors * floor_h + 2
    i = 0.12
    x0, y0, x1, y1 = i, i, w - i, h - i
    rise = 8 + 5 * min(w, h)
    s = Sprite(w, h, zw + rise + 22)
    shadow(s, x0, y0, x1, y1)
    lw = (x1 - x0) * 36; rw = (y1 - y0) * 36
    s.wall_left(x0, x1, y1, 0, zw, wall_texture(material, wall, lw, zw, floors, floor_h, seed, 1.0))
    s.wall_right(x1, y0, y1, 0, zw, wall_texture(material, wall, rw, zw, floors, floor_h, seed + 7, 0.76, door=(0.42, 0.58)))
    e = 0.08
    if w >= h:
        ym = (y0 + y1) / 2
        # hintere Dachfläche, Giebel rechts, vordere Dachfläche
        a, b, c = s.p(x0 - e, y0 - e, zw), s.p(x1 + 0.02, y0 - e, zw), s.p(x0 - e, ym, zw + rise)
        s.face(c, (b[0] - a[0], b[1] - a[1]), (a[0] - c[0], a[1] - c[1]), roof_texture(roof, lw, 30, 1.08, seed))
        gable = [s.p(x1, y0, zw), s.p(x1, y1, zw), s.p(x1, ym, zw + rise)]
        s.poly(gable, shade(wall, 0.7))
        a, b, c = s.p(x0 - e, ym, zw + rise), s.p(x1 + 0.02, ym, zw + rise), s.p(x0 - e, y1 + e, zw)
        s.face(a, (b[0] - a[0], b[1] - a[1]), (c[0] - a[0], c[1] - a[1]), roof_texture(roof, lw, 30, 0.92, seed + 3))
        chimney(s, x0 + (x1 - x0) * 0.25, y0 + 0.15, zw + rise * 0.55, wall)
    else:
        xm = (x0 + x1) / 2
        a, b, c = s.p(xm, y0 - e, zw + rise), s.p(xm, y1 + 0.02, zw + rise), s.p(x0 - e, y0 - e, zw)
        s.face(a, (b[0] - a[0], b[1] - a[1]), (c[0] - a[0], c[1] - a[1]), roof_texture(roof, rw, 30, 1.08, seed))
        gable = [s.p(x0, y1, zw), s.p(x1, y1, zw), s.p(xm, y1, zw + rise)]
        s.poly(gable, shade(wall, 0.92))
        a, b, c = s.p(xm, y0 - e, zw + rise), s.p(xm, y1 + 0.02, zw + rise), s.p(x1 + e, y0 - e, zw)
        s.face(a, (b[0] - a[0], b[1] - a[1]), (c[0] - a[0], c[1] - a[1]), roof_texture(roof, rw, 30, 0.8, seed + 3))
        chimney(s, x0 + 0.15, y0 + (y1 - y0) * 0.3, zw + rise * 0.55, wall)
    return s


def hall(cat, w, h, floors, seed=1):
    material, wall_hex, roof_hex = CATEGORIES.get(cat, CATEGORIES[""])
    wall, roof = rgb(wall_hex), rgb(roof_hex)
    material = "panel" if material in ("render", "stone") else material
    zw = 18 + floors * 8
    i = 0.1
    x0, y0, x1, y1 = i, i, w - i, h - i
    s = Sprite(w, h, zw + 26)
    shadow(s, x0, y0, x1, y1)
    lw = (x1 - x0) * 36; rw = (y1 - y0) * 36
    s.wall_left(x0, x1, y1, 0, zw, wall_texture(material, wall, lw, zw, 1, zw * 0.9, seed, 1.0))
    s.wall_right(x1, y0, y1, 0, zw, wall_texture(material, wall, rw, zw, 1, zw * 0.9, seed + 5, 0.76, door=(0.35, 0.65)))
    # Attika in Dachfarbe, Kiesdach, Oberlichter, Lüftungsgeräte
    s.wall_left(x0, x1, y1, zw, zw + 3, lambda u, v: shade(roof, 0.95))
    s.wall_right(x1, y0, y1, zw, zw + 3, lambda u, v: shade(roof, 0.75))
    n = noise(seed + 11)
    def gravel(u, v):
        c = rgb("#9DA3A8")
        c = shade(c, 0.88 + 0.2 * n(int(u * 60), int(v * 60)))
        if (u * w * 4) % 1 < 0.18 and 0.15 < v < 0.85:
            c = rgb("#BFD9EE") if (v * 20) % 2 < 1 else rgb("#8FB1CF")  # Oberlichtband
        return c
    s.ground(x0, y0, x1, y1, gravel, z=zw + 3)
    s.ground(x0, y0, x1, y0 + 0.08, lambda u, v: roof, z=zw + 3)
    s.ground(x0, y0, x0 + 0.08, y1, lambda u, v: roof, z=zw + 3)
    for k in range(min(3, w * h)):
        bx, by = x0 + 0.3 + 0.5 * k % (x1 - x0 - 0.5), y0 + 0.35 + 0.4 * (k % 2)
        box(s, bx, by, 0.25, 0.2, zw + 3, zw + 9, rgb("#C9CED6"))
    return s


def box(s, x, y, w, h, z0, z1, color):
    s.wall_left(x, x + w, y + h, z0, z1, lambda u, v: color)
    s.wall_right(x + w, y, y + h, z0, z1, lambda u, v: shade(color, 0.75))
    s.ground(x, y, x + w, y + h, lambda u, v: shade(color, 1.15), z=z1)


def chimney(s, x, y, z, wall):
    box(s, x, y, 0.14, 0.14, z, z + 12, shade(rgb("#9B4A36"), 1.0))
    box(s, x - 0.01, y - 0.01, 0.16, 0.16, z + 12, z + 14, rgb("#5E5F66"))


def shadow(s, x0, y0, x1, y1, d=0.35):
    s.ground(x0 + 0.1, y0 + 0.1, x1 + d, y1 + d * 0.6, lambda u, v: (0, 0, 0, 60))
