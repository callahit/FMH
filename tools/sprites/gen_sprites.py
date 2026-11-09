"""Erzeugt den Sprite-Atlas der Vereinsgelände-Karte (Pixel-Art im Stil von SimCity 2000 / RollerCoaster Tycoon).

    python3 tools/sprites/gen_sprites.py [--preview datei.png]

Schreibt src/Fmh.Desktop/Assets/grounds.png und grounds.json (Name -> [x, y, Breite, Höhe, AnkerX, AnkerY]).
Der Anker ist die obere Ecke der Grundfläche (Rasterpunkt x, y des Gebäudes). Ergebnis ist deterministisch.
"""
import json
import zlib
import os
import sys
from PIL import Image
from iso import Sprite, rgb, shade, noise
import buildings as B
import specials as X
import stadium as St
import catalog_types as CT
import people as PE

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "src", "Fmh.Desktop", "Assets")


def road(w, h):
    s = Sprite(w, h, 4)
    n = noise(5)
    along = w >= h
    def tex(u, v):
        a, b = (u, v) if along else (v, u)
        c = shade(rgb("#4F5563"), 0.92 + 0.12 * n(int(u * 40), int(v * 40)))
        if abs(b - 0.5) < 0.03 and (a * 8) % 1 < 0.55:
            c = rgb("#F1F1EC")
        if b < 0.06 or b > 0.94:
            c = rgb("#B9B2A2")
        return c
    s.ground(0, 0, w, h, tex)
    return s


def sprites():
    out = {}
    for i in range(4): out[f"grass_{i}"] = X.grass_tile(10 + i)
    for i in range(2): out[f"path_{i}"] = X.path_tile(20 + i)
    out["gate"] = X.gate_tile()
    for i in range(2): out[f"plaza_{i}"] = X.plaza_tile(70 + i)
    out.update(PE.people())
    out["deco_lamp"] = X.lamp()
    out["deco_bench"] = X.bench()
    out["deco_bin"] = X.bin_()
    for i in range(2): out[f"deco_planter_{i}"] = X.planter(60 + i)
    for i in range(3): out[f"tree_oak_{i}"] = X.tree("oak", 30 + i)
    for i in range(2): out[f"tree_pine_{i}"] = X.tree("pine", 40 + i)
    out["tree_bush_0"] = X.tree("bush", 50)
    for cat in B.CATEGORIES:
        key = cat or "none"
        for (w, h) in ((1, 1), (2, 1), (1, 2)):
            for f in (1, 2, 3):
                out[f"house_{key}_{w}x{h}_{f}"] = B.house(cat, w, h, f, seed=zlib.crc32(f"{key}{w}{h}{f}".encode()) % 1000)
        for f in (1, 2, 3):
            out[f"hall_{key}_2x2_{f}"] = B.hall(cat, 2, 2, f, seed=len(key) * 13 + f)
    for (w, h) in ((1, 1), (2, 1), (1, 2), (2, 2)):
        out[f"pitch_{w}x{h}"] = X.pitch(w, h)
        out[f"turf_{w}x{h}"] = X.pitch(w, h, turf=True)
        out[f"parking_{w}x{h}"] = X.parking(w, h, seed=w * 3 + h)
        out[f"road_{w}x{h}"] = road(w, h)
        out[f"construction_{w}x{h}"] = X.construction(w, h)
    out["court_1x1"] = X.court()
    out["track_1x1"] = X.track()
    out["solar_1x1"] = X.solar()
    out["park_1x1"] = X.park()
    out["pool_1x1"] = X.pool()
    out["icepool_1x1"] = X.pool(ice=True)
    out["garden_1x1"] = X.garden()
    out["ledtower_1x1"] = X.tower(led=True)
    out["floodlight_1x1"] = X.tower()
    out["tank_1x1"] = X.tank()
    out["chp_1x1"] = X.tank(chp=True)
    out["statue_1x1"] = X.monument()
    out["chapel_1x1"] = X.monument(chapel=True)
    # Baustellen für jede Grundfläche aus den Daten (auch gedreht)
    with open(os.path.join(ROOT, "data", "base", "rules", "buildings.json")) as f:
        for b in json.load(f):
            for (sw, sh) in {(b.get("width", 1), b.get("height", 1)), (b.get("height", 1), b.get("width", 1))}:
                out.setdefault(f"construction_{sw}x{sh}", X.construction(sw, sh))
    # Eigene Sprites je Gebäudetyp, Grundfläche (auch gedreht) und Stufe: b_<typ>_<B>x<H>_<stufe>
    with open(os.path.join(ROOT, "data", "base", "rules", "buildings.json")) as f:
        for b in json.load(f):
            w, h = b.get("width", 1), b.get("height", 1)
            for (sw, sh) in {(w, h), (h, w)}:
                for level in range(1, b.get("maxLevel", 1) + 1):
                    sp = CT.make(b, sw, sh, level)
                    if sp is not None:
                        out[f"b_{b['id']}_{sw}x{sh}_{level}"] = sp
    out["stadium_base"] = St.base()
    for side in ("North", "South", "West", "East"):
        for level in (1, 2, 3):
            for roof in (0, 1):
                out[f"stand_{side}_{level}_{roof}"] = St.stand(side, level, bool(roof))
    for level in (1, 2, 3, 4):
        out[f"mast_{level}"] = St.mast(level)
    return out


def trim(sprites):
    """Schneidet jedes Sprite auf seinen sichtbaren Bereich zu und verschiebt den Anker entsprechend."""
    for s in sprites.values():
        box_ = s.img.getbbox()
        if box_ is None:
            continue
        x0, y0, x1, y1 = box_
        s.img = s.img.crop((x0, y0, x1, y1))
        s.ax -= x0
        s.ay -= y0


def pack(sprites, width=2048):
    trim(sprites)
    items = sorted(sprites.items(), key=lambda kv: -kv[1].img.size[1])
    x = y = shelf = 0
    places = {}
    for name, s in items:
        w, h = s.img.size
        if x + w > width:
            x, y, shelf = 0, y + shelf + 1, 0
        places[name] = (x, y)
        x += w + 1
        shelf = max(shelf, h)
    atlas = Image.new("RGBA", (width, y + shelf + 1), (0, 0, 0, 0))
    manifest = {}
    for name, s in sprites.items():
        px, py = places[name]
        atlas.alpha_composite(s.img, (px, py))
        manifest[name] = [px, py, s.img.size[0], s.img.size[1], s.ax, s.ay]
    return atlas, manifest


def main():
    all_sprites = sprites()
    atlas, manifest = pack(all_sprites)
    os.makedirs(OUT, exist_ok=True)
    atlas.save(os.path.join(OUT, "grounds.png"), optimize=True)
    with open(os.path.join(OUT, "grounds.json"), "w") as f:
        json.dump(manifest, f, separators=(",", ":"), sort_keys=True)
    print(f"{len(manifest)} Sprites, Atlas {atlas.size[0]}x{atlas.size[1]}")
    if "--sheet" in sys.argv:
        import sheet
        sheet.render(all_sprites, sys.argv[sys.argv.index("--sheet") + 1], ROOT)
    if "--preview" in sys.argv:
        import preview
        preview.render(all_sprites, sys.argv[sys.argv.index("--preview") + 1])


if __name__ == "__main__":
    main()
