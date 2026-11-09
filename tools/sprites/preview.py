"""Vorschau: setzt eine Beispiel-Szene aus den Sprites zusammen (wie die Karte im Spiel), dreifach vergrößert."""
from PIL import Image

HW, HH = 32, 16


def tint(img, color):
    """Ersetzt Maskenpixel (Rot = Blau, Grün = 0) durch die Vereinsfarbe in gleicher Helligkeit."""
    out = img.copy()
    px = out.load()
    for y in range(out.size[1]):
        for x in range(out.size[0]):
            r, g, b, a = px[x, y]
            if a and g == 0 and r == b and r > 0:
                f = r / 200
                px[x, y] = (min(255, int(color[0] * f)), min(255, int(color[1] * f)), min(255, int(color[2] * f)), a)
    return out


def render(sprites, path):
    gw, gh = 14, 10
    W, H = (gw + gh) * HW + 200, (gw + gh) * HH + 260
    img = Image.new("RGBA", (W, H), (20, 28, 46, 255))
    ox, oy = gh * HW + 100, 160

    def place(name, gx, gy, color=None):
        s = sprites[name]
        im = tint(s.img, color) if color else s.img
        img.alpha_composite(im, (int(ox + (gx - gy) * HW - s.ax), int(oy + (gx + gy) * HH - s.ay)))

    paths = {(x, 3) for x in range(gw)} | {(6, y) for y in range(3, gh)} | {(x, 7) for x in range(2, 12)}
    for y in range(gh):
        for x in range(gw):
            place(f"path_{(x + y) % 2}" if (x, y) in paths else f"grass_{(x * 7 + y * 3) % 4}", x, y)
    place("gate", 6, 3)
    objs = [
        ("house_training_2x1_2", 0, 0), ("pitch_2x1", 0, 1), ("hall_youth_2x2_2", 2, 1),
        ("stadium_base", 4, 0), ("house_medical_2x1_2", 8, 0), ("parking_2x1", 10, 0), ("house_admin_1x1_3", 12, 0),
        ("turf_2x1", 8, 1), ("house_fans_1x1_1", 10, 1), ("house_commerce_1x2_1", 11, 1), ("solar_1x1", 13, 1),
        ("house_fans_1x1_2", 0, 4), ("garden_1x1", 1, 4), ("house_fans_1x1_3", 2, 4), ("pool_1x1", 3, 4),
        ("construction_1x1", 4, 4), ("house_teams_2x1_1", 7, 4), ("ledtower_1x1", 9, 4), ("park_1x1", 10, 4),
        ("house_energy_1x1_1", 11, 4), ("tank_1x1", 12, 4), ("statue_1x1", 4, 5), ("court_1x1", 7, 5),
        ("house_infrastructure_1x2_2", 8, 5), ("track_1x1", 0, 8), ("parking_1x2", 13, 5),
    ]
    stadium_parts = [("stand_North_3_1", 4, 0), ("stand_West_3_1", 4, 0), ("stand_South_2_1", 4, 0), ("stand_East_3_1", 4, 0)]
    order = sorted(objs, key=lambda o: o[1] + o[2])
    color = (46, 139, 87)
    for name, x, y in order:
        place(name, x, y)
        if name == "stadium_base":
            for part, px, py in stadium_parts:
                place(part, px, py, color)
            for (mx, my) in ((4, 0), (7, 0), (4, 2), (7, 2)):
                place("mast_3", mx, my)
    import random
    r = random.Random(3)
    for i in range(34):
        x, y = r.choice([(-1, r.uniform(0, gh)), (r.uniform(0, gw), -1), (gw, r.uniform(0, gh)), (r.uniform(0, gw), gh)])
        place(r.choice(["tree_oak_0", "tree_oak_1", "tree_oak_2", "tree_pine_0", "tree_pine_1", "tree_bush_0"]), x - 0.5, y - 0.5)
    img = img.resize((img.size[0] * 2, img.size[1] * 2), Image.NEAREST)
    img.save(path)
    print("Vorschau", path, img.size)
