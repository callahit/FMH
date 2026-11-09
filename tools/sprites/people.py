"""Kleine Pixel-Figuren für das belebte Vereinsgelände: Besucher, Fans und Spieler.

Jede Figur steht mittig auf einer 1×1-Kachel (Fußpunkt = Kachelmitte). Vier Blickrichtungen (vorn/hinten ×
links/rechts gespiegelt) und zwei Schrittbilder. Namen: person_<variante>_<f|b><l|r>_<0|1>.
Vereinsfarbe als Maske (Rot = Blau, Grün = 0), wird zur Laufzeit umgefärbt.
"""
from PIL import Image
from iso import Sprite, rgb, shade
from stadium import mask

SKIN = [rgb("#F1C9A5"), rgb("#C98E63"), rgb("#8A5A3C")]
HAIR = [rgb("#3A2A1E"), rgb("#D9B26B"), rgb("#1E1E24"), rgb("#8C4A2F")]

# Variante: (Oberteil, Hose); None = Vereinsfarbe
VARIANTS = {
    "fan": (None, rgb("#2E3A55")),
    "red": (rgb("#C8383F"), rgb("#2E3A55")),
    "blue": (rgb("#2F6FC2"), rgb("#4A4F5C")),
    "white": (rgb("#EDEFF2"), rgb("#5E6472")),
    "yellow": (rgb("#E9B832"), rgb("#2E3A55")),
    "staff": (rgb("#3A4256"), rgb("#2A2F3A")),
    "player": (None, rgb("#F4F2EC")),
}


def figure(variant, front, right, frame, seed):
    """Figur 3–4 px breit, 10 px hoch, mit Schatten; Schrittbild bewegt die Beine."""
    s = Sprite(1, 1, 16)
    cx, cy = s.p(0.5, 0.5)
    cx, cy = int(cx), int(cy)
    shirt, pants = VARIANTS[variant]
    skin = SKIN[seed % len(SKIN)]
    hair = HAIR[(seed // 3) % len(HAIR)]
    px = s.img.load()
    def dot(x, y, c):
        xx = cx + (x if right else -x - 1)
        if 0 <= xx < s.img.size[0] and 0 <= cy + y < s.img.size[1]:
            px[xx, cy + y] = c
    for x in range(-2, 2):  # Schatten
        dot(x, 0, (0, 0, 0, 70))
    # Beine: Schrittbild 0 im Schritt (Füße auseinander), 1 geschlossen
    feet = (-2, 1) if frame == 0 else (-1, 0)
    for x in feet:
        dot(x, -1, pants)
    for y in (-2, -3):
        for x in (-1, 0):
            dot(x, y, pants)
    # Körper
    for y in (-4, -5, -6):
        for x in (-1, 0):
            c = mask(0.95 if x == 0 else 0.8) if shirt is None else (shirt if x == 0 else shade(shirt, 0.8))
            dot(x, y, c)
    # Arme
    arm_y = -5 + (frame if front else 1 - frame)
    dot(-2, arm_y, skin)
    dot(1, -4 - (1 - frame), skin)
    # Kopf: vorn Gesicht, hinten Haar
    for x in (-1, 0):
        dot(x, -7, skin if front else hair)
        dot(x, -8, hair)
    if variant == "player" and front:
        dot(0, -5, rgb("#FFFFFF"))  # Rückennummer bzw. Brustlogo
    return s


def people():
    out = {}
    for i, variant in enumerate(VARIANTS):
        for front in (True, False):
            for right in (True, False):
                for frame in (0, 1):
                    name = f"person_{variant}_{'f' if front else 'b'}{'r' if right else 'l'}_{frame}"
                    out[name] = figure(variant, front, right, frame, seed=i * 5 + 1)
    return out
