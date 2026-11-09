"""Übersichtsbogen: je Gebäudetyp eine Zeile mit allen Ausbaustufen (zur Sichtprüfung des Katalogs)."""
import json
import os
from PIL import Image, ImageDraw
from preview import tint


def render(sprites, path, root, color=(46, 139, 87), scale=2):
    with open(os.path.join(root, "data", "base", "rules", "buildings.json")) as f:
        buildings = json.load(f)
    rows = []
    for b in buildings:
        w, h = b.get("width", 1), b.get("height", 1)
        cells = [sprites.get(f"b_{b['id']}_{w}x{h}_{lv}") for lv in range(1, b.get("maxLevel", 1) + 1)]
        rows.append((b["id"], [c for c in cells if c is not None]))
    cw, ch = 220, 170
    cols = 3
    per_col = (len(rows) + cols - 1) // cols
    img = Image.new("RGBA", (cols * (130 + 5 * cw), per_col * ch), (28, 36, 52, 255))
    d = ImageDraw.Draw(img)
    for i, (name, cells) in enumerate(rows):
        ox, oy = (i // per_col) * (130 + 5 * cw), (i % per_col) * ch
        d.text((ox + 4, oy + ch // 2), name, fill=(230, 230, 230, 255))
        for k, s in enumerate(cells):
            im = tint(s.img, color)
            x = ox + 130 + k * cw + (cw - im.size[0]) // 2
            y = oy + ch - 6 - im.size[1]
            img.alpha_composite(im, (max(0, x), max(0, y)))
    img = img.resize((img.size[0] * scale // 2, img.size[1] * scale // 2), Image.NEAREST)
    img.save(path)
    print("Bogen", path, img.size)
