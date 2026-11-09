"""Zuordnung Gebäudetyp → Sprite-Bauplan sowie Sonderbauten (Hallen, Parkflächen, Energie, Kleinstadion …)."""
import random
from iso import Sprite, rgb, shade, mix
from buildings import box
import kit as K
from stadium import mask
import specials as X
from catalog import (Ctx, new, block, b_std, pitch_area, p_pitch, p_youth_pitches, p_goalkeeper, p_setpiece, p_court,
                     p_track, x_dish, x_antenna, x_tanks, x_garden, x_tables, x_awning, x_flags, x_van, x_steam,
                     x_playground, x_ambulance, x_bikes, x_columns, x_graffiti, x_clock, x_billboard, x_pitchlet,
                     x_queue)

CARS = X.CARS


def s_hall(c):
    """Trainingshalle: alt Tonnendach in Blech, modern Flachdach mit Glasfront."""
    s = new(c, 90, "pave" if c.tier == 3 else "grass")
    roof = "barrel" if c.tier < 3 else "flat"
    block(c, s, (0.06, 0.1, 0.94, 0.86), 2 if c.tier < 3 else 3, "ball", roof, floor_h=11)
    return s


def s_floodlight(c):
    s = new(c, 90)
    n = 2 + 2 * c.level
    for k in range(n):
        x, y = c.pt(0.15 + 0.7 * (k % 2), 0.15 + 0.7 * (k // 2) / max(1, (n - 1) // 2))
        a = s.p(x, y)
        hgt = 40 + 12 * c.level
        s.line(a, (a[0], a[1] - hgt), rgb("#8E98A8"))
        for dx in range(-4, 5):
            for dy in range(3):
                s.dot(a[0] + dx, a[1] - hgt - dy, rgb("#FFF6C2") if c.level > 1 and dy else rgb("#3A4256"))
    return s


def s_heating(c):
    s = new(c, 80)
    top = block(c, s, (0.2, 0.25, 0.7, 0.7), 1, None, "flat" if c.tier > 1 else "gable")
    x_steam(c, s, top)
    x0, y0, x1, y1 = c.r(0.7, 0.4, 0.95, 0.45)
    box(s, x0, y0, x1 - x0, 0.05, 2, 5, rgb("#B87333"))  # Rohrleitung
    return s


def s_greenkeeper(c):
    s = new(c, 70)
    block(c, s, (0.1, 0.15, 0.6, 0.6), 1, "wrench", "gable" if c.tier < 3 else "flat")
    x, y = c.pt(0.7, 0.7)
    K.vehicle(s, x, y, c.along, 0.18, rgb("#3E9A52"), 5)
    if c.level >= 2:
        x, y = c.pt(0.2, 0.8)
        box(s, x, y, 0.15, 0.1, 0, 4, rgb("#6B4A32"))  # Erdhaufen/Material
    return s


def s_pool(c, ice=False):
    s = new(c, 60, "pave")
    x0, y0, x1, y1 = c.r(0.12, 0.15, 0.88, 0.85)
    box(s, x0, y0, x1 - x0, y1 - y0, 0, 3, rgb("#D9DEE6"))
    water = rgb("#A9DDF2") if ice else rgb("#3AA6E0")
    s.ground(x0 + 0.08, y0 + 0.08, x1 - 0.08, y1 - 0.08,
             lambda u, v: rgb("#FFFFFF") if (v * 4) % 1 < 0.05 else shade(water, 0.95 + 0.15 * ((u * 9 + v * 4) % 1 > 0.7)), z=3)
    if c.tier == 3:  # Glasüberdachung
        s.ground(x0, y0, x1, y1, lambda u, v: (215, 230, 245, 90) if (u * 8) % 1 > 0.08 else rgb("#C9D1DB"), z=22)
        for (px, py) in ((x0, y1), (x1, y1), (x1, y0)):
            s.line(s.p(px, py, 3), s.p(px, py, 22), rgb("#C9D1DB"))
    return s


def s_sauna(c):
    s = new(c, 70)
    top = block(c, s, (0.2, 0.2, 0.75, 0.7), 1, None, "gable", wall="#A8865E", roof_hex="#6B4A32")
    x_steam(c, s, top)
    if c.level >= 2:
        X_pond = c.r(0.1, 0.78, 0.4, 0.95)
        s.ground(*X_pond, lambda u, v: rgb("#7FC4E0"))
    return s


def s_kiosk(glyph):
    def make(c):
        s = new(c, 60, "pave")
        top = block(c, s, (0.25, 0.25, 0.75, 0.65), 1, glyph, "flat")
        x0, y0, x1, y1 = c.r(0.25, 0.25, 0.75, 0.65)
        K.awning(s, x0 + 0.03, x1 - 0.03, y1, 10)
        K.tables(s, *c.r(0.15, 0.75, 0.85, 0.95), 1 + c.level, c.seed)
        return s
    return make


def s_beergarden(c):
    s = new(c, 60, "pave" if c.tier > 1 else "grass")
    block(c, s, (0.1, 0.1, 0.45, 0.4), 1, "beer", "gable", wall="#A8865E")
    K.tables(s, *c.r(0.15, 0.5, 0.9, 0.92), 3 + 2 * c.level, c.seed)
    if c.tier > 1:
        for k in range(4):  # Lichterkette
            a = s.p(*c.pt(0.15 + k * 0.25, 0.45), 14)
            s.dot(a[0], a[1], rgb("#FFE58A"))
    return s


def s_walk(c):
    s = new(c, 30, "pave")
    x0, y0, x1, y1 = c.r(0.08, 0.3, 0.92, 0.7)
    s.ground(x0, y0, x1, y1, lambda u, v: mask(0.5) if (u * 6) % 1 < 0.5 and 0.3 < v < 0.7 else rgb("#E8E2D4"))
    for k in range(5):
        a = s.p(*c.pt(0.15 + k * 0.17, 0.5), 1)
        for dx, dy in ((0, -1), (-1, 0), (0, 0), (1, 0), (0, 1)):
            s.dot(a[0] + dx, a[1] + dy, rgb("#F5C04E"))
    return s


def tree_at(s, x, y, kind="oak", seed=31):
    """Baum mit Stammfuß auf Rasterpunkt (x, y)."""
    t = X.tree(kind, seed)
    ax, ay = s.p(x, y)
    s.img.alpha_composite(t.img, (int(ax - t.ax), int(ay - t.ay - 16)))


def s_parking(c, bus=False, ev=False):
    """Parkfläche: alt Schotter mit wenigen Autos, sonst markierter Asphalt; modern mit Bäumen, E-Ladesäulen."""
    if c.tier >= 2 and not bus:
        s = X.parking(c.w, c.h, seed=c.seed)
    else:
        s = new(c, 40, "asphalt" if bus else "grass")
        x0, y0, x1, y1 = c.r(0.06, 0.08, 0.94, 0.92)
        if not bus:
            s.ground(x0, y0, x1, y1, lambda u, v: shade(rgb("#A39C8C"), 0.9 + 0.15 * ((u * 31 + v * 17) % 1 > 0.6)))
        r = random.Random(c.seed)
        L = max(c.w, c.h)
        n = max(1, int(L * (1.5 if bus else 3)))
        fill = 0.35 if c.level <= 1 else 0.75  # Stufe 2: deutlich mehr Stellplätze belegt
        for k in range(n):
            if not bus and r.random() > fill:
                continue
            fx = 0.08 + 0.84 * k / n
            x, y = c.pt(fx + 0.02, 0.2)
            col = rgb("#E2B13C") if bus else r.choice(CARS)
            K.vehicle(s, x, y, not c.along, 0.55 if bus else 0.3, col, 9 if bus else 5)
    if ev or (c.stage == 4 and not bus):
        for k in range(3):
            a = s.p(*c.pt(0.2 + 0.3 * k, 0.5))
            s.line(a, (a[0], a[1] - 9), rgb("#1F9AAD"))
            s.dot(a[0], a[1] - 9, rgb("#7FE0F0"))
    if c.tier == 3 and not bus:
        tree_at(s, *c.pt(0.96, 0.75))
    return s


def s_garage(c):
    """Parkhaus: Decks nach Stufe."""
    decks = 1 + c.level
    s = new(c, decks * 10 + 50, "asphalt")
    x0, y0, x1, y1 = c.r(0.06, 0.08, 0.94, 0.92)
    concrete = rgb("#B8BEC8")
    for d in range(decks):
        z0, z1 = d * 10, d * 10 + 10
        s.wall_left(x0, x1, y1, z0, z1, lambda u, v: shade(concrete, 1.05) if v < 0.3 else rgb("#3D4A5C") if (u * 10) % 1 > 0.1 else concrete)
        s.wall_right(x1, y0, y1, z0, z1, lambda u, v: shade(concrete, 0.8) if v < 0.3 else rgb("#2E3846") if (u * 10) % 1 > 0.1 else shade(concrete, 0.8))
    K.flat(s, x0, y0, x1, y1, decks * 10, c.tier, "#8C98A8", c.seed, solar=c.stage == 4)
    K.sign(s, x0 + 0.1, x0 + 0.35, y1, decks * 10 - 8, "p", c.tier)
    return s


def s_shuttle(c):
    s = new(c, 50, "pave")
    x0, y0, x1, y1 = c.r(0.15, 0.2, 0.75, 0.35)
    box(s, x0, y0, x1 - x0, y1 - y0, 0, 2, rgb("#B9B2A2"))
    s.ground(x0, y0, x1, y1, lambda u, v: (200, 220, 240, 160), z=14)  # Wartehäuschen
    x, y = c.pt(0.1, 0.5)
    K.vehicle(s, x, y, c.along, 0.8, mask(0.9) if c.tier > 1 else rgb("#E2B13C"), 9)
    return s


def s_bikes(c):
    s = new(c, 40, "pave")
    for row in (0.3, 0.65):
        x0, y0, x1, _ = c.r(0.12, row, 0.88, row)
        for k in range(4 + 2 * c.level):
            t = x0 + (x1 - x0) * k / (4 + 2 * c.level)
            p = s.p(t, y0, 0) if c.along else s.p(x0, t, 0)
            s.line(p, (p[0] + 3, p[1] - 4), random.Random(k).choice(CARS))
    if c.tier == 3:
        s.ground(*c.r(0.08, 0.2, 0.92, 0.8), lambda u, v: (205, 220, 235, 120), z=14)
    return s


def s_road(c):
    s = Sprite(c.w, c.h, 30)
    along = c.along
    def tex(u, v):
        a, b = (u, v) if along else (v, u)
        col = shade(rgb("#4F5563"), 0.95 + 0.1 * ((a * 37 + b * 11) % 1 > 0.5))
        if abs(b - 0.5) < 0.03 and (a * 8) % 1 < 0.55:
            col = rgb("#F1F1EC")
        if b < 0.08 or b > 0.92:
            col = rgb("#B9B2A2")
        return col
    s.ground(0, 0, c.w, c.h, tex)
    if c.level >= 2:
        for k in range(3):
            x, y = c.pt(0.15 + k * 0.35, 0.04)
            a = s.p(x, y)
            s.line(a, (a[0], a[1] - 22), rgb("#3A3F4A"))
            s.dot(a[0] + 2, a[1] - 22, rgb("#FFF3B0"))
    return s


def s_security(c):
    s = new(c, 60, "pave")
    top = block(c, s, (0.15, 0.2, 0.5, 0.55), 1, "shield", "flat")
    x0, y0, x1, y1 = c.r(0.55, 0.7, 0.95, 0.72)
    s.line(s.p(x0, y0, 5), s.p(x1, y1, 5), rgb("#E53935"), 2)  # Schranke
    for k in range(c.level):
        a = s.p(*c.pt(0.2 + 0.2 * k, 0.15), top)
        s.dot(a[0], a[1] - 2, rgb("#2A2F3A"))
    return s


def s_solar(c):
    s = new(c, 30)
    rows = 1 + c.level
    for r_ in range(rows):
        fy = 0.1 + r_ * 0.8 / rows
        x0, y0, x1, y1 = c.r(0.08, fy, 0.92, fy + 0.55 / rows)
        a, b, d = s.p(x0, y0, 9), s.p(x1, y0, 9), s.p(x0, y1, 3)
        if not c.along:
            a, b, d = s.p(x0, y0, 9), s.p(x0, y1, 9), s.p(x1, y0, 3)
        s.face(a, (b[0] - a[0], b[1] - a[1]), (d[0] - a[0], d[1] - a[1]),
               lambda u, v: rgb("#C9D3DE") if u < 0.03 or u > 0.97 or v < 0.1 or v > 0.9 else
               (rgb("#6F98D0") if v < 0.35 and (u * 4) % 1 > 0.55 else rgb("#22447F")))
    return s


def s_tank(c, chp=False):
    s = new(c, 70)
    x, y = c.pt(0.45, 0.45)
    r = 0.22 + 0.04 * c.level
    box(s, x - r, y - r, 2 * r, 2 * r, 0, 22 + 6 * c.level, rgb("#C9CED6") if not chp else rgb("#B8C2C9"))
    if chp:
        block(c, s, (0.6, 0.55, 0.95, 0.9), 1, "bolt", "flat")
        x_steam(c, s, 28)
    return s


def s_park(c):
    s = new(c, 50)
    s.ground(*c.r(0.05, 0.4, 0.95, 0.55), lambda u, v: rgb("#D8CDB2"))
    r = random.Random(c.seed)
    for k in range(2 + c.level):
        x, y = c.pt(r.uniform(0.1, 0.9), r.choice([r.uniform(0.1, 0.35), r.uniform(0.6, 0.9)]))
        tree_at(s, x, y, r.choice(["oak", "pine", "bush"]), k + 30)
    return s


def s_small_stadium(c):
    s = new(c, 70, "pave")
    x0, y0, x1, y1 = pitch_area(c, s, (0.18, 0.22, 0.82, 0.78))
    hi = 6 + 5 * c.level
    stand = lambda u, v: mask(0.8 + 0.2 * ((u * 30) % 1 > 0.5)) if (v * 5) % 1 > 0.2 else shade(rgb("#8E98A8"), 0.8)
    ax0, ay0, ax1, ay1 = c.r(0.18, 0.04, 0.82, 0.22)
    a, b, d = s.p(ax0, ay1, 1), s.p(ax1, ay1, 1), s.p(ax0, ay0, hi)
    if c.along:
        s.face(a, (b[0] - a[0], b[1] - a[1]), (d[0] - a[0], d[1] - a[1]), stand)
    if c.level >= 2:
        bx0, by0, bx1, by1 = c.r(0.02, 0.22, 0.18, 0.78)
        a, b, d = s.p(bx1, by1, 1), s.p(bx1, by0, 1), s.p(bx0, by1, hi)
        s.face(a, (b[0] - a[0], b[1] - a[1]), (d[0] - a[0], d[1] - a[1]), stand)
    if c.level >= 3:
        s.ground(ax0, ay0, ax1, ay0 + (ay1 - ay0) * 0.6, lambda u, v: rgb("#C9D1DB") if v < 0.85 else mask(0.9), z=hi + 6)
    return s


def s_bus_garage(c):
    s = new(c, 60, "asphalt")
    top = block(c, s, (0.1, 0.1, 0.9, 0.6), 1, "bus", "flat" if c.tier > 1 else "gable")
    x, y = c.pt(0.15, 0.72)
    K.vehicle(s, x, y, c.along, 0.7, mask(0.85), 9)
    return s


def s_land(c):
    s = new(c, 20)
    K.fence(s, 0.05, 0.05, c.w - 0.05, c.h - 0.05, 1, 5)
    return s


def s_sports_complex(c):
    s = new(c, 80, "pave")
    for k in range(1 + (c.level >= 2)):  # Tennisplätze
        x0, y0, x1, y1 = c.r(0.05 + k * 0.25, 0.55, 0.27 + k * 0.25, 0.95)
        s.ground(x0, y0, x1, y1, lambda u, v: WHITE if (u * 6) % 1 < 0.06 or (v * 2) % 1 < 0.04 else rgb("#C25A40"))
    block(c, s, (0.1, 0.08, 0.9, 0.5), 2, "ball", "barrel" if c.tier < 3 else "flat", floor_h=11)
    return s


def s_warehouse(c):
    s = new(c, 70, "asphalt")
    block(c, s, (0.08, 0.1, 0.75, 0.65), 1 + (c.level >= 3), "shirt", "flat", floor_h=14)
    for k in range(c.level):
        x, y = c.pt(0.15 + 0.22 * k, 0.75)
        K.vehicle(s, x, y, not c.along, 0.3, rgb("#EDEFF2"), 8)
    return s


def s_tower(c, led):
    s = X.tower(led=led)
    return s if c.w == 1 and c.h == 1 else None


WHITE = K.WHITE


def mon(chapel):
    return lambda c: X.monument(chapel=chapel)


TYPES = {
    # Training
    "trainingPitch": lambda c: p_pitch(c),
    "artificialPitch": lambda c: p_pitch(c, turf=True),
    "reservePitch": lambda c: p_pitch(c),
    "trainingFloodlight": s_floodlight,
    "trainingHeating": s_heating,
    "soccerCourt": p_court,
    "goalkeeperArea": p_goalkeeper,
    "setPieceArea": p_setpiece,
    "sprintTrack": p_track,
    "trainingHall": s_hall,
    "gym": b_std("dumbbell", extras=(x_bikes,)),
    "dressingRooms": b_std("shirt", rect=(0.08, 0.2, 0.92, 0.75), per=3),
    "videoRoom": b_std("camera", extras=(x_dish,)),
    "analysisCentre": b_std("eye", extras=(x_antenna,)),
    "altitudeChamber": b_std("drop", extras=(x_tanks,)),
    "greenkeeper": s_greenkeeper,
    # Medizin
    "physio": b_std("cross"),
    "clinic": b_std("cross", base=2, extras=(x_ambulance,)),
    "rehab": b_std("cross", pre=(x_garden,)),
    "pool": s_pool,
    "sauna": s_sauna,
    "icePool": lambda c: s_pool(c, ice=True),
    "restRooms": b_std("bed", pre=(x_garden,)),
    "nutrition": b_std("fork", pre=(x_garden,)),
    "psychologyRoom": b_std("heart", rect=(0.2, 0.2, 0.8, 0.7), pre=(x_garden,)),
    "diagnostics": b_std("cross", extras=(x_tanks,)),
    # Jugend
    "youthCentre": b_std("star", rect=(0.08, 0.08, 0.92, 0.7), base=2, extras=(x_pitchlet, x_flags)),
    "youthPitches": p_youth_pitches,
    "boardingSchool": b_std("bed", base=2, extras=(x_bikes,)),
    "partnerSchool": b_std("book", extras=(x_clock,)),
    "youthDressing": b_std("shirt", rect=(0.15, 0.25, 0.85, 0.7)),
    "youthCanteen": b_std("fork", extras=(x_tables,)),
    "footballSchool": b_std("ball", rect=(0.1, 0.1, 0.9, 0.6), extras=(x_pitchlet,)),
    "womensCentre": b_std("star", rect=(0.08, 0.1, 0.92, 0.65), extras=(x_pitchlet,)),
    # Verwaltung
    "office": b_std("shield", base=2, extras=(x_flags,)),
    "coachOffices": b_std("letter"),
    "scoutingDept": b_std("eye", extras=(x_dish,)),
    "legalDept": b_std("book", extras=(x_columns,)),
    "marketing": b_std("euro", extras=(x_billboard,)),
    "pressRoom": b_std("mic", extras=(x_dish, x_van)),
    "tvStudio": b_std("camera", extras=(x_antenna, x_van)),
    "staffCanteen": b_std("fork", extras=(x_tables,)),
    "daycare": b_std("kid", rect=(0.1, 0.12, 0.6, 0.62), extras=(x_playground,)),
    # Fans
    "fanshop": b_std("shirt", ground="pave", extras=(x_awning,)),
    "cityShop": b_std("shirt", ground="pave", base=2, extras=(x_awning,)),
    "onlineShop": s_warehouse,
    "ticketCentre": b_std("ticket", rect=(0.15, 0.15, 0.85, 0.6), ground="pave", extras=(x_queue,)),
    "snackBar": s_kiosk("fork"),
    "beerGarden": s_beergarden,
    "fanPub": b_std("beer", extras=(x_tables,)),
    "clubhouse": b_std("star", extras=(x_tables,)),
    "restaurant": b_std("fork", ground="pave", extras=(x_tables,)),
    "fanProject": b_std("heart", extras=(x_graffiti,)),
    "museum": b_std("cup", extras=(x_columns,)),
    "stadiumTours": b_std("camera", rect=(0.12, 0.12, 0.6, 0.55), ground="pave", extras=(x_van,)),
    "kidsClub": b_std("kid", rect=(0.1, 0.12, 0.6, 0.62), extras=(x_playground,)),
    "walkOfFame": s_walk,
    "statue": mon(False),
    "chapel": mon(True),
    # Kommerz
    "hotel": b_std("bed", base=3, per=1, ground="pave", extras=(x_flags,)),
    "conferenceCentre": b_std("mic", ground="pave", base=2),
    "businessClub": b_std("euro", ground="pave", extras=(x_flags,)),
    "offices": b_std("letter", base=2, per=1),
    "membersGym": b_std("dumbbell", extras=(x_bikes,)),
    "sportsComplex": s_sports_complex,
    "esports": b_std("game"),
    "ledTower": lambda c: s_tower(c, True),
    # Infrastruktur
    "parking": s_parking,
    "parkingGarage": s_garage,
    "busParking": lambda c: s_parking(c, bus=True),
    "shuttle": s_shuttle,
    "bikeRacks": s_bikes,
    "accessRoad": s_road,
    "workshop": b_std("wrench", ground="asphalt"),
    "security": s_security,
    # Energie
    "solar": s_solar,
    "chp": lambda c: s_tank(c, chp=True),
    "cistern": s_tank,
    "evCharging": lambda c: s_parking(c, ev=True),
    "park": s_park,
    # Mannschaften
    "smallStadium": s_small_stadium,
    "busGarage": s_bus_garage,
    "land": s_land,
}


def make(building, w, h, level):
    """Sprite für einen Gebäudetyp in Grundfläche w×h und Stufe level (None, wenn kein Bauplan existiert)."""
    f = TYPES.get(building["id"])
    if f is None:
        return None
    c = Ctx(w, h, level, building.get("maxLevel", 1), building.get("category", ""), seed=sum(map(ord, building["id"])) + level)
    return f(c)
