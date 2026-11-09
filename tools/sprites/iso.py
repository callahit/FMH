"""Pixel-Rasterung für isometrische Sprites (2:1, Kachel 64x32) im Stil von SimCity 2000 / RollerCoaster Tycoon.

Ein Sprite für eine Grundfläche w x h (Kacheln) mit Höhe H Pixel ist (w+h)*32 breit und (w+h)*16 + H hoch.
Rasterpunkt (gx, gy, z) liegt bei px = h*32 + (gx-gy)*32, py = H + (gx+gy)*16 - z; der Rasterpunkt (0,0,0)
ist der Anker (obere Ecke der Grundfläche). Flächen sind Parallelogramme P(u,v) = A + u*U + v*V; jedes Pixel
bekommt seine Farbe aus einer Texturfunktion f(u, v) mit u, v in [0, 1].
"""
import random
from PIL import Image, ImageDraw

HW, HH = 32, 16


class Sprite:
    def __init__(self, w, h, height):
        self.w, self.h, self.height = w, h, height
        self.img = Image.new("RGBA", ((w + h) * HW + 1, (w + h) * HH + height + 1), (0, 0, 0, 0))
        self.px = self.img.load()
        self.ax, self.ay = h * HW, height

    def p(self, gx, gy, z=0):
        return (self.ax + (gx - gy) * HW, self.ay + (gx + gy) * HH - z)

    def face(self, a, u, v, tex):
        """Parallelogramm mit Ecke a und Kanten u, v (Bildschirmvektoren); tex(u, v) -> RGBA oder None."""
        pts = [a, (a[0] + u[0], a[1] + u[1]), (a[0] + u[0] + v[0], a[1] + u[1] + v[1]), (a[0] + v[0], a[1] + v[1])]
        mask = Image.new("L", self.img.size, 0)
        ImageDraw.Draw(mask).polygon(pts, fill=255)
        m = mask.load()
        det = u[0] * v[1] - u[1] * v[0]
        if abs(det) < 1e-9:
            return
        xs = [q[0] for q in pts]; ys = [q[1] for q in pts]
        for y in range(max(0, int(min(ys))), min(self.img.size[1], int(max(ys)) + 1)):
            for x in range(max(0, int(min(xs))), min(self.img.size[0], int(max(xs)) + 1)):
                if not m[x, y]:
                    continue
                dx, dy = x + 0.5 - a[0], y + 0.5 - a[1]
                uu = (dx * v[1] - dy * v[0]) / det
                vv = (u[0] * dy - u[1] * dx) / det
                c = tex(min(max(uu, 0), 0.9999), min(max(vv, 0), 0.9999))
                if c is not None:
                    self.px[x, y] = c

    # Flächen in Rasterkoordinaten
    def ground(self, x0, y0, x1, y1, tex, z=0):
        """Waagrechte Fläche von (x0,y0) bis (x1,y1); tex(u, v) mit u entlang x, v entlang y."""
        a = self.p(x0, y0, z); b = self.p(x1, y0, z); c = self.p(x0, y1, z)
        self.face(a, (b[0] - a[0], b[1] - a[1]), (c[0] - a[0], c[1] - a[1]), tex)

    def wall_left(self, x0, x1, y, z0, z1, tex):
        """Vorderseite links (bei y, entlang x): u entlang x, v von oben (z1) nach unten (z0)."""
        a = self.p(x0, y, z1); b = self.p(x1, y, z1); c = self.p(x0, y, z0)
        self.face(a, (b[0] - a[0], b[1] - a[1]), (c[0] - a[0], c[1] - a[1]), tex)

    def wall_right(self, x, y0, y1, z0, z1, tex):
        """Vorderseite rechts (bei x, entlang y von y1 nach y0): u von vorn nach hinten, v von oben nach unten."""
        a = self.p(x, y1, z1); b = self.p(x, y0, z1); c = self.p(x, y1, z0)
        self.face(a, (b[0] - a[0], b[1] - a[1]), (c[0] - a[0], c[1] - a[1]), tex)

    def poly(self, pts, color):
        ImageDraw.Draw(self.img).polygon(pts, fill=color)

    def line(self, a, b, color, width=1):
        ImageDraw.Draw(self.img).line([a, b], fill=color, width=width)

    def dot(self, x, y, color):
        x, y = int(x), int(y)
        if 0 <= x < self.img.size[0] and 0 <= y < self.img.size[1]:
            self.px[x, y] = color


def rgb(h, a=255):
    h = h.lstrip("#")
    return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16), a)


def shade(c, f):
    return (max(0, min(255, int(c[0] * f))), max(0, min(255, int(c[1] * f))), max(0, min(255, int(c[2] * f))), c[3])


def mix(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3)) + (255,)


def noise(seed):
    r = random.Random(seed)
    cache = {}
    def n(i, j):
        k = (i, j)
        if k not in cache:
            cache[k] = r.random()
        return cache[k]
    return n
