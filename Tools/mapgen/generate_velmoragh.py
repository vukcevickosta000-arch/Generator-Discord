#!/usr/bin/env python3
"""
Velmoragh map generator - "Ruins of the Crimson Throne".

Hand-authored layout (lanes, bases, structures, camps, river, pit, shops) + procedural dressing (terrain relief,
forests with juke paths, splat masks, props). The Dawn half is authored; the Dusk half is produced by point
symmetry around the map centre, which keeps the two sides competitively identical.

Outputs
  Shared/Runtime/Resources/GameData/maps/velmoragh.json   gameplay map (grid, lanes, structures, camps ...)
  Client/Assets/Resources/Maps/Velmoragh/height.bytes     uint16 heightfield (render)
  Client/Assets/Resources/Maps/Velmoragh/splat0.png/.png  terrain layer weights (render)
  Client/Assets/Resources/Maps/Velmoragh/dressing.json     prop placements (render)
  Docs/Images/velmoragh_layout.png                         top-down design preview

Requires numpy, scipy, pillow.  Deterministic (fixed seed).
"""
import base64
import json
import math
import os
import struct
import sys

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SIZE = 192.0          # metres
CELL = 0.5            # nav grid cell size
N = int(SIZE / CELL)  # 384 cells
V = N + 1             # height vertices
RNG = np.random.default_rng(1337)

# Height levels (gameplay) and their render heights (metres).
LEVEL_H = {0: -1.4, 1: 0.0, 2: 2.4, 3: 5.0}
H_MIN, H_MAX = -4.0, 12.0


def mirror(p):
    return (SIZE - p[0], SIZE - p[1])


def diag(p):
    """Reflection across y = x (used to author the south jungle from the west jungle)."""
    return (p[1], p[0])


# --------------------------------------------------------------------------------------------- layout (Dawn side)

TOP_LANE = [(15, 40), (15, 70), (14, 110), (14, 148), (17, 164), (25, 174), (40, 178), (70, 179), (110, 178), (148, 177), (152, 177)]
MID_LANE = [(38, 38), (58, 57), (74, 71), (96, 96), (118, 121), (134, 135), (154, 154)]
BOT_LANE = [mirror(p) for p in reversed(TOP_LANE)]
LANES = [("top", TOP_LANE), ("mid", MID_LANE), ("bot", BOT_LANE)]

RIVER = [(12, 184), (22, 172), (40, 152), (58, 136), (78, 115), (96, 96), (114, 77), (134, 56), (152, 40), (170, 20), (180, 12)]

DAWN_BASE = dict(fountain=(9, 9), spawn=(14, 14), shop=(21, 8), core=(25, 25))
PIT = (74.0, 118.0)
PIT_RADIUS = 6.5
SEALS = [(PIT[0] - 12.5, PIT[1]), (PIT[0] + 12.5, PIT[1]), (PIT[0], PIT[1] - 12.5), (PIT[0], PIT[1] + 12.5)]

# Camps on the Dawn side (west jungle authored, south jungle is its diagonal reflection).
WEST_CAMPS = [("small", (31, 78)), ("medium", (45, 97)), ("large", (29, 124))]
ANCIENT_CAMP = ("ancient", (62, 104))
SECRET_SHOP = (50, 134)
SIDE_SHOP = (7, 100)
RUNE_SPOTS = [(58, 134)]
WEST_PATHS = [
    [(15, 77), (31, 78), (45, 97)],
    [(45, 97), (56, 84), (66, 67)],
    [(45, 97), (62, 104), (70, 110)],
    [(15, 123), (29, 124), (40, 134), (50, 134), (57, 135)],
    [(29, 124), (37, 110), (45, 97)],
    [(31, 78), (40, 64), (52, 55)],
    [(62, 104), (60, 118), (54, 130)],
]
WEST_PLATEAUS = [(24, 142), (74, 90)]
WEST_ROCKS = [((22, 96), 3.2), ((52, 118), 2.6), ((36, 60), 2.4), ((66, 90), 2.0)]


def lane_point(lane, s):
    """Point and unit tangent at arc length s along a polyline."""
    acc = 0.0
    for a, b in zip(lane[:-1], lane[1:]):
        seg = math.dist(a, b)
        if acc + seg >= s:
            t = (s - acc) / seg
            p = (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t)
            d = ((b[0] - a[0]) / seg, (b[1] - a[1]) / seg)
            return p, d
        acc += seg
    a, b = lane[-2], lane[-1]
    seg = math.dist(a, b)
    return b, ((b[0] - a[0]) / seg, (b[1] - a[1]) / seg)


def lane_length(lane):
    return sum(math.dist(a, b) for a, b in zip(lane[:-1], lane[1:]))


# Structure arc-length positions from the Dawn end of each lane: (tier1, tier2, tier3, barracks)
LANE_TOWERS = {"top": (80, 44, 7, 0), "mid": (52, 30, 8, 0), "bot": (118, 58, 7, 0)}
TOWER_SIDE_OFFSET = 3.6
BARRACKS_OFFSET = 6.5


def build_structures():
    structs = []
    for lane_name, lane in LANES:
        t1, t2, t3, bs = LANE_TOWERS[lane_name]
        # Bottom lane authored as mirror of top: use the top lane's Dawn offsets but walk the bot lane from its Dawn end.
        for tier, s in ((1, t1), (2, t2), (3, t3)):
            p, d = lane_point(lane, s)
            side = (-d[1], d[0])
            sign = 1 if lane_name != "bot" else -1
            pos = (p[0] + side[0] * TOWER_SIDE_OFFSET * sign, p[1] + side[1] * TOWER_SIDE_OFFSET * sign)
            structs.append(dict(id=f"dawn_{lane_name}_t{tier}", unitId=f"tower_dawn_t{tier}", team="Dawn", position=pos, lane=lane_name, tier=tier))
        p, d = lane_point(lane, bs)
        side = (-d[1], d[0])
        for kind, sign in (("melee", 1), ("ranged", -1)):
            pos = (p[0] + side[0] * BARRACKS_OFFSET * sign, p[1] + side[1] * BARRACKS_OFFSET * sign)
            structs.append(dict(id=f"dawn_{lane_name}_rax_{kind}", unitId=f"barracks_dawn_{kind}", team="Dawn", position=pos, lane=lane_name, tier=3, barracksType=kind))
    structs.append(dict(id="dawn_t4_a", unitId="tower_dawn_t4", team="Dawn", position=(33.5, 24.5), lane="base", tier=4))
    structs.append(dict(id="dawn_t4_b", unitId="tower_dawn_t4", team="Dawn", position=(24.5, 33.5), lane="base", tier=4))
    structs.append(dict(id="dawn_core", unitId="core_dawn", team="Dawn", position=DAWN_BASE["core"], lane="base", tier=5))
    structs.append(dict(id="dawn_fountain", unitId="fountain_dawn", team="Dawn", position=DAWN_BASE["fountain"], lane="base", tier=0))
    # Protection chain.
    for s in structs:
        if s["id"].endswith("_t2"):
            s["protectedBy"] = [s["id"].replace("_t2", "_t1")]
        elif s["id"].endswith("_t3"):
            s["protectedBy"] = [s["id"].replace("_t3", "_t2")]
        elif "_rax_" in s["id"]:
            s["protectedBy"] = [f"dawn_{s['lane']}_t3"]
        elif s["id"].startswith("dawn_t4"):
            s["unlockedByAny"] = ["dawn_top_t3", "dawn_mid_t3", "dawn_bot_t3"]
        elif s["id"] == "dawn_core":
            s["protectedBy"] = ["dawn_t4_a", "dawn_t4_b"]
    # Dusk: point mirror, rename ids / units, facing flipped.
    dusk = []
    for s in structs:
        m = dict(s)
        m["id"] = s["id"].replace("dawn", "dusk")
        m["unitId"] = s["unitId"].replace("dawn", "dusk")
        m["team"] = "Dusk"
        m["position"] = mirror(s["position"])
        if s["lane"] == "top":
            m["lane"] = "bot"
        elif s["lane"] == "bot":
            m["lane"] = "top"
        # Lane-name swaps also rename ids.
        m["id"] = m["id"].replace("_top_", "_TMP_").replace("_bot_", "_top_").replace("_TMP_", "_bot_")
        for key in ("protectedBy", "unlockedByAny"):
            if key in s:
                m[key] = [x.replace("dawn", "dusk").replace("_top_", "_TMP_").replace("_bot_", "_top_").replace("_TMP_", "_bot_") for x in s[key]]
        dusk.append(m)
    for s in structs:
        s["facing"] = 45.0
    for s in dusk:
        s["facing"] = 225.0
    return structs + dusk


# --------------------------------------------------------------------------------------------- rasterisation helpers

xs = (np.arange(N) + 0.5) * CELL
X, Y = np.meshgrid(xs, xs)  # X[y, x]
vx = np.arange(V) * CELL
VX, VY = np.meshgrid(vx, vx)


def dist_to_polyline(px, py, poly):
    d = np.full(px.shape, 1e9, dtype=np.float32)
    for a, b in zip(poly[:-1], poly[1:]):
        ax, ay = a
        bx, by = b
        abx, aby = bx - ax, by - ay
        L = abx * abx + aby * aby
        t = np.clip(((px - ax) * abx + (py - ay) * aby) / max(L, 1e-9), 0, 1)
        cx, cy = ax + abx * t, ay + aby * t
        d = np.minimum(d, np.hypot(px - cx, py - cy))
    return d


def disc(px, py, c, r):
    return np.hypot(px - c[0], py - c[1]) <= r


def fbm(shape, scale, octaves=4, seed=0):
    rng = np.random.default_rng(seed)
    out = np.zeros(shape, dtype=np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        res = max(2, int(shape[0] / (scale / (2 ** o))))
        g = rng.standard_normal((res + 1, res + 1)).astype(np.float32)
        up = ndimage.zoom(g, (shape[0] / res, shape[1] / res), order=3)[: shape[0], : shape[1]]
        out += up * amp
        total += amp
        amp *= 0.5
    return out / total


def both_sides(points):
    return list(points) + [mirror(p) for p in points]


def all_quadrants(points):
    """West jungle authored -> + south jungle (diagonal) -> + Dusk mirror of both."""
    dawn = list(points) + [diag(p) for p in points]
    return dawn + [mirror(p) for p in dawn]


def main():
    structures = build_structures()

    # ---- Semantic masks ------------------------------------------------------------------
    lane_d = np.full(X.shape, 1e9, dtype=np.float32)
    for _, lane in LANES:
        lane_d = np.minimum(lane_d, dist_to_polyline(X, Y, lane))
    river_d = dist_to_polyline(X, Y, RIVER)

    def base_plateau(px, py):
        a = (px <= 50) & (py <= 50) & (px + py <= 82)
        m = (SIZE - px <= 50) & (SIZE - py <= 50) & ((SIZE - px) + (SIZE - py) <= 82)
        return a | m

    plateau = base_plateau(X, Y)
    plateau_v = base_plateau(VX, VY)

    # Ramps: where lanes leave the plateaus.
    ramp_centres = []
    for _, lane in LANES:
        for s in np.linspace(0, lane_length(lane), 600):
            p, _ = lane_point(lane, s)
            if not base_plateau(np.array(p[0]), np.array(p[1])):
                ramp_centres.append(p)
                break
        for s in np.linspace(lane_length(lane), 0, 600):
            p, _ = lane_point(lane, s)
            if not base_plateau(np.array(p[0]), np.array(p[1])):
                ramp_centres.append(p)
                break

    paths = []
    for p in WEST_PATHS:
        paths.append(p)
        paths.append([diag(q) for q in p])
    paths = paths + [[mirror(q) for q in p] for p in paths]
    path_d = np.full(X.shape, 1e9, dtype=np.float32)
    for p in paths:
        path_d = np.minimum(path_d, dist_to_polyline(X, Y, p))

    camps = []
    for kind, pos in WEST_CAMPS:
        camps.append((kind, pos))
        camps.append((kind, diag(pos)))
    camps.append(ANCIENT_CAMP)
    camps_all = camps + [(k, mirror(p)) for k, p in camps]

    plateaus = all_quadrants(WEST_PLATEAUS)
    rocks = []
    for c, r in WEST_ROCKS:
        rocks.append((c, r))
        rocks.append((diag(c), r))
    rocks = rocks + [(mirror(c), r) for c, r in rocks]

    secret_shops = both_sides([SECRET_SHOP])
    side_shops = both_sides([SIDE_SHOP])
    rune_spots = both_sides(RUNE_SPOTS)

    clearing = np.zeros(X.shape, bool)
    for kind, pos in camps_all:
        clearing |= disc(X, Y, pos, 6.5 if kind == "ancient" else 5.5)
    for p in secret_shops + side_shops:
        clearing |= disc(X, Y, p, 5.0)
    for p in plateaus:
        clearing |= disc(X, Y, p, 5.5)
    pit_area = disc(X, Y, PIT, 15.0)
    for s in SEALS:
        clearing |= disc(X, Y, s, 3.5)

    # ---- Levels ---------------------------------------------------------------------------
    level = np.ones(X.shape, np.uint8)
    level[plateau] = 2
    river = river_d < 5.2
    level[river] = 0
    level[disc(X, Y, PIT, PIT_RADIUS + 1.0)] = 0
    for p in plateaus:
        level[disc(X, Y, p, 4.0)] = 2

    walk = np.ones(X.shape, bool)
    rock = np.zeros(X.shape, bool)
    for c, r in rocks:
        rock |= disc(X, Y, c, r)
    # Rocky ring around the pit with two entrances along the river.
    pit_d = np.hypot(X - PIT[0], Y - PIT[1])
    ring = (pit_d > PIT_RADIUS + 1.2) & (pit_d < PIT_RADIUS + 3.2)
    river_dir = np.array([1.0, -1.0]) / math.sqrt(2)
    rel = np.stack([X - PIT[0], Y - PIT[1]], -1) / np.maximum(pit_d[..., None], 1e-6)
    along_river = np.abs(rel @ river_dir) > 0.8
    rock |= ring & ~along_river
    # Map border.
    border = (X < 3) | (Y < 3) | (X > SIZE - 3) | (Y > SIZE - 3)
    rock |= border
    rock &= ~(lane_d < 5.0)
    level[rock] = 3
    walk &= ~rock
    for p in plateaus:
        # Plateau cliff edge except a ramp facing the nearest path.
        d = np.hypot(X - p[0], Y - p[1])
        edge = (d > 4.0) & (d < 5.0)
        best = min(paths, key=lambda pl: float(dist_to_polyline(np.array([p[0]]), np.array([p[1]]), pl)[0]))
        tgt = min(best, key=lambda q: math.dist(q, p))
        ang = math.atan2(tgt[1] - p[1], tgt[0] - p[0])
        a = np.arctan2(Y - p[1], X - p[0])
        ramp_gap = np.abs(np.angle(np.exp(1j * (a - ang)))) < 0.6
        walk &= ~(edge & ~ramp_gap)

    # Plateau cliffs around bases (walkable only through ramps).
    edt_in = ndimage.distance_transform_edt(plateau) * CELL
    edt_out = ndimage.distance_transform_edt(~plateau) * CELL
    near_edge = (edt_in < 1.0) & plateau | (edt_out < 0.6) & ~plateau
    ramp_mask = np.zeros(X.shape, bool)
    for rc in ramp_centres:
        ramp_mask |= disc(X, Y, rc, 7.0)
    walk &= ~(near_edge & ~ramp_mask)
    # Upper half of a ramp counts as high ground.
    ramp_high = ramp_mask & (edt_out < 3.0) & ~plateau
    level[ramp_mask & plateau] = 2

    # Structures block their footprint.
    units_json = {}
    for fn in ("structures.json",):
        with open(os.path.join(ROOT, "Shared/Runtime/Resources/GameData/units", fn)) as f:
            for u in json.load(f)["units"]:
                units_json[u["id"]] = u
    struct_block = np.zeros(X.shape, bool)
    for s in structures:
        r = units_json[s["unitId"]]["collisionRadius"]
        struct_block |= disc(X, Y, s["position"], r * 0.85)
    walk &= ~struct_block
    shop_positions = [DAWN_BASE["shop"], mirror(DAWN_BASE["shop"])]
    for p in shop_positions + side_shops + secret_shops:
        walk &= ~disc(X, Y, p, 0.9)

    # ---- Forests ----------------------------------------------------------------------------
    forest_allowed = (lane_d > 5.8) & (river_d > 7.0) & (path_d > 1.9) & ~clearing & ~pit_area & ~rock & walk
    forest_allowed &= ~(plateau & (edt_in > 2.5))  # keep base interiors open
    fountain_clear = disc(X, Y, DAWN_BASE["fountain"], 12) | disc(X, Y, mirror(DAWN_BASE["fountain"]), 12)
    forest_allowed &= ~fountain_clear
    for s in structures:
        forest_allowed &= ~disc(X, Y, s["position"], 7.0)
    # Density variation: a few glades inside the woods.
    glade = fbm((N, N), 64, 3, seed=7) > 0.68
    forest_allowed &= ~glade

    trees = poisson_trees(forest_allowed, spacing=1.55)
    tree_block = np.zeros(X.shape, bool)
    tree_cells = []
    for (tx, ty, _v) in trees:
        m = disc(X, Y, (tx, ty), 0.72)
        tree_block |= m
    walk_final = walk & ~tree_block

    # Vision blockers: tall ruins in the jungle (props) are handled by rocks (level 3 = blocker).
    vision_block = rock.copy()

    cells = np.zeros(X.shape, np.uint8)
    cells |= walk_final.astype(np.uint8)
    cells |= (np.clip(level, 0, 3).astype(np.uint8) << 1)
    cells |= (tree_block.astype(np.uint8) << 3)
    cells |= (struct_block.astype(np.uint8) << 4)
    cells |= (vision_block.astype(np.uint8) << 5)
    cells |= ((river & walk_final).astype(np.uint8) << 6)
    cells |= ((ramp_mask & walk_final).astype(np.uint8) << 7)

    # ---- Heights (render) --------------------------------------------------------------------
    height = build_heights(plateau_v, ramp_centres, rocks, plateaus)

    # ---- Splat --------------------------------------------------------------------------------
    splat0, splat1 = build_splat(height, lane_d, river_d, path_d, rock, plateau, tree_block)

    # ---- Dressing -------------------------------------------------------------------------------
    dressing = build_dressing(structures, trees, camps_all, secret_shops, side_shops, rune_spots, plateaus, rocks, walk_final)

    # ---- Write gameplay map -----------------------------------------------------------------------
    grid_bytes = cells.flatten()  # row-major [y][x]
    rle = rle_encode(grid_bytes)
    tree_bin = b"".join(struct.pack("<HHB", int(round(t[0] * 10)), int(round(t[1] * 10)), t[2]) for t in trees)
    lanes_out = [dict(name=name, waypoints=[list(map(lambda v: round(v, 2), p)) for p in lane]) for name, lane in LANES]
    bases = [
        dict(team="Dawn", fountain=list(DAWN_BASE["fountain"]), fountainRadius=10, heroSpawn=list(DAWN_BASE["spawn"]), shopPosition=list(DAWN_BASE["shop"])),
        dict(team="Dusk", fountain=list(mirror(DAWN_BASE["fountain"])), fountainRadius=10, heroSpawn=list(mirror(DAWN_BASE["spawn"])), shopPosition=list(mirror(DAWN_BASE["shop"]))),
    ]
    camp_json = []
    for i, (kind, pos) in enumerate(camps_all):
        side = "Dawn" if pos[0] + pos[1] < SIZE else "Dusk"
        camp_json.append(dict(id=f"camp_{side.lower()}_{kind}_{i}", campType=f"camp_{kind}", position=[round(pos[0], 2), round(pos[1], 2)], spawnBoxRadius=3.2, side=side))
    ward_spots = [list(p) for p in plateaus] + [list(p) for p in rune_spots]
    map_def = dict(
        id="map_velmoragh",
        name="Velmoragh",
        subtitle="Ruins of the Crimson Throne",
        description="The drowned capital of the Crimson Court, raised over the prison of Vharoth the Blood Titan. Three roads cross the ruins; a river of corrupted blood divides them.",
        width=SIZE, height=SIZE, cellSize=CELL, gridWidth=N, gridHeight=N,
        grid=rle,
        lanes=lanes_out,
        structures=[dict(s, position=[round(s["position"][0], 2), round(s["position"][1], 2)]) for s in structures],
        bases=bases,
        camps=camp_json,
        secretShops=[list(p) for p in secret_shops],
        sideShops=[list(p) for p in side_shops],
        runeSpots=[list(p) for p in rune_spots],
        wardSpots=ward_spots,
        bossPit=list(PIT),
        vharothSeals=[list(p) for p in SEALS],
        trees=base64.b64encode(tree_bin).decode("ascii"),
        dressingFile="Maps/Velmoragh/dressing",
        heightFile="Maps/Velmoragh/height",
        heightScale=1.0,
    )
    out_map = os.path.join(ROOT, "Shared/Runtime/Resources/GameData/maps/velmoragh.json")
    os.makedirs(os.path.dirname(out_map), exist_ok=True)
    with open(out_map, "w") as f:
        json.dump({"$comment": "Generated by Tools/mapgen/generate_velmoragh.py - edit the generator, not this file.", "map": map_def}, f, separators=(",", ":"))

    render_dir = os.path.join(ROOT, "Client/Assets/Resources/Maps/Velmoragh")
    os.makedirs(render_dir, exist_ok=True)
    hq = np.clip((height - H_MIN) / (H_MAX - H_MIN), 0, 1)
    hq = (hq * 65535).astype("<u2")
    with open(os.path.join(render_dir, "height.bytes"), "wb") as f:
        f.write(struct.pack("<iiff", V, V, H_MIN, H_MAX))
        f.write(hq.tobytes())
    Image.fromarray(splat0, "RGBA").save(os.path.join(render_dir, "splat0.png"))
    Image.fromarray(splat1, "RGBA").save(os.path.join(render_dir, "splat1.png"))
    with open(os.path.join(render_dir, "dressing.json"), "w") as f:
        json.dump(dressing, f, separators=(",", ":"))

    preview(cells, height, structures, camps_all, trees, secret_shops, side_shops, rune_spots, dressing)
    walkable_pct = walk_final.mean() * 100
    print(f"grid {N}x{N}  walkable {walkable_pct:.1f}%  trees {len(trees)}  structures {len(structures)}  camps {len(camps_all)}  props {len(dressing['props'])}")
    print(f"grid rle {len(rle)/1024:.1f} KiB, trees {len(tree_bin)/1024:.1f} KiB")
    validate_connectivity(cells, structures, camps_all)


def poisson_trees(mask, spacing):
    """Bridson-style blue noise restricted to mask (on the 0.5 m grid)."""
    rng = np.random.default_rng(42)
    cell = spacing / math.sqrt(2)
    gw = int(SIZE / cell) + 1
    grid = -np.ones((gw, gw), int)
    pts = []
    candidates = np.argwhere(mask)
    rng.shuffle(candidates)
    for cy, cx in candidates:
        x = (cx + 0.5) * CELL + rng.uniform(-0.2, 0.2)
        y = (cy + 0.5) * CELL + rng.uniform(-0.2, 0.2)
        gx, gy = int(x / cell), int(y / cell)
        ok = True
        for yy in range(max(0, gy - 2), min(gw, gy + 3)):
            for xx in range(max(0, gx - 2), min(gw, gx + 3)):
                j = grid[yy, xx]
                if j >= 0 and (pts[j][0] - x) ** 2 + (pts[j][1] - y) ** 2 < spacing * spacing:
                    ok = False
                    break
            if not ok:
                break
        if ok:
            grid[gy, gx] = len(pts)
            # Variant: 0-2 dead oaks, 3-5 black pines, 6 giant dead tree; more pines near Dusk, more oaks near Dawn.
            dusk_bias = (x + y) / (2 * SIZE)
            r = rng.random()
            v = int(rng.integers(3, 6)) if r < 0.35 + 0.3 * dusk_bias else int(rng.integers(0, 3))
            if rng.random() < 0.02:
                v = 6
            pts.append((x, y, v))
    return pts


def build_heights(plateau_v, ramp_centres, rocks, plateaus):
    h = np.zeros(VX.shape, np.float32)
    edt_in = ndimage.distance_transform_edt(plateau_v) * CELL
    edt_out = ndimage.distance_transform_edt(~plateau_v) * CELL
    signed = np.where(plateau_v, edt_in, -edt_out)
    ramp = np.zeros(VX.shape, np.float32)
    for rc in ramp_centres:
        d = np.hypot(VX - rc[0], VY - rc[1])
        ramp = np.maximum(ramp, np.clip(1 - (d - 4.0) / 5.0, 0, 1))
    width = 0.6 + 9.0 * ramp
    t = np.clip(0.5 + signed / width, 0, 1)
    t = t * t * (3 - 2 * t)
    h += LEVEL_H[2] * t
    # Small plateaus.
    for p in plateaus:
        d = np.hypot(VX - p[0], VY - p[1])
        h += LEVEL_H[2] * np.clip((4.6 - d) / 0.8, 0, 1)
    # River bed.
    rd = dist_to_polyline(VX, VY, RIVER)
    bed = np.clip((7.0 - rd) / 3.0, 0, 1)
    bed = bed * bed * (3 - 2 * bed)
    h = h * (1 - bed) + LEVEL_H[0] * bed
    # Pit.
    pd = np.hypot(VX - PIT[0], VY - PIT[1])
    pit = np.clip((PIT_RADIUS + 1.5 - pd) / 2.0, 0, 1)
    h = h * (1 - pit) + (-2.6) * pit
    # Rocks / cliffs.
    noise_big = fbm(VX.shape, 48, 4, seed=3)
    noise_small = fbm(VX.shape, 12, 3, seed=5)
    rock_h = np.zeros(VX.shape, np.float32)
    for c, r in rocks:
        d = np.hypot(VX - c[0], VY - c[1])
        rock_h = np.maximum(rock_h, np.clip((r + 0.6 - d) / 1.2, 0, 1))
    ring = (pd > PIT_RADIUS + 1.2) & (pd < PIT_RADIUS + 3.2)
    rel_x, rel_y = (VX - PIT[0]) / np.maximum(pd, 1e-6), (VY - PIT[1]) / np.maximum(pd, 1e-6)
    along = np.abs((rel_x - rel_y) / math.sqrt(2)) > 0.8
    rock_h = np.maximum(rock_h, (ring & ~along).astype(np.float32))
    border = np.clip(1 - np.minimum.reduce([VX, VY, SIZE - VX, SIZE - VY]) / 3.5, 0, 1)
    lane_d = np.full(VX.shape, 1e9, np.float32)
    for _, lane in LANES:
        lane_d = np.minimum(lane_d, dist_to_polyline(VX, VY, lane))
    border *= np.clip((lane_d - 4.0) / 2.0, 0, 1)
    rock_h = np.maximum(rock_h, border)
    rock_h = ndimage.gaussian_filter(rock_h, 1.2)
    h += rock_h * (LEVEL_H[3] + 2.5 * noise_big + 1.2 * noise_small)
    # Gentle undulation everywhere (less on lanes so roads stay flat).
    lane_flat = np.clip(lane_d / 6.0, 0.25, 1.0)
    h += (0.35 * noise_big + 0.12 * noise_small) * lane_flat
    return np.clip(h, H_MIN, H_MAX).astype(np.float32)


def build_splat(height, lane_d, river_d, path_d, rock, plateau, tree_block):
    """Two RGBA control maps (512x512).
    splat0: R grass/moss, G dirt/mud, B cobblestone road, A rock/cliff
    splat1: R blood mud (river), G bone/ash (Dusk corruption), B leaf litter (forest floor), A sun-bleached paving (Dawn base)"""
    size = 512
    sx = np.linspace(0, N - 1, size).astype(int)
    idx = np.ix_(sx, sx)
    ld, rd, pd = lane_d[idx], river_d[idx], path_d[idx]
    rk = rock[idx].astype(np.float32)
    pl = plateau[idx]
    tb = ndimage.gaussian_filter(tree_block.astype(np.float32), 3)[idx]
    Xs, Ys = X[idx], Y[idx]
    n1 = fbm((size, size), 40, 4, seed=11)
    n2 = fbm((size, size), 10, 3, seed=12)
    road = np.clip((4.2 - ld) / 1.6 + 0.3 * n2, 0, 1)
    dirt = np.clip((6.5 - ld) / 2.5, 0, 1) * (1 - road) + np.clip((2.4 - pd) / 1.2, 0, 1) + np.clip(n1 * 1.2 - 0.3, 0, 1) * 0.5
    blood = np.clip((6.2 - rd) / 2.0 + 0.2 * n2, 0, 1)
    dusk = (Xs + Ys) / (2 * SIZE)
    ash = np.clip((dusk - 0.55) * 2.2 + 0.4 * n1, 0, 1) * (1 - road) * 0.8
    leaves = np.clip(tb * 1.6 + 0.2 * n2, 0, 1)
    dawn_pl = pl & ((Xs + Ys) < SIZE)
    paving = dawn_pl.astype(np.float32) * np.clip(0.9 + 0.3 * n2, 0, 1) * (1 - road * 0.3)
    dusk_pl = pl & ((Xs + Ys) >= SIZE)
    ash = np.maximum(ash, dusk_pl.astype(np.float32) * 0.9)
    rockw = np.clip(rk * 1.5, 0, 1)
    grass = np.clip(1.0 - (road + dirt + blood + ash + leaves + paving + rockw), 0, 1) + 0.05
    layers = np.stack([grass, dirt, road, rockw, blood, ash, leaves, paving], -1)
    # Priority: road over dirt, rock and blood override.
    layers[..., 1] *= 1 - road
    layers[..., 0] *= 1 - np.maximum(road, blood)
    layers[..., 6] *= 1 - np.maximum(road, blood)
    s = layers.sum(-1, keepdims=True)
    layers = layers / np.maximum(s, 1e-6)
    q = (layers * 255 + 0.5).astype(np.uint8)
    splat0 = np.ascontiguousarray(q[..., 0:4][::-1])  # flip so row 0 = top (north) for PNG
    splat1 = np.ascontiguousarray(q[..., 4:8][::-1])
    return splat0, splat1


def build_dressing(structures, trees, camps, secret_shops, side_shops, rune_spots, plateaus, rocks, walk):
    props = []
    rng = np.random.default_rng(99)

    def add(kind, p, rot=None, scale=1.0, **extra):
        props.append(dict(type=kind, x=round(float(p[0]), 2), y=round(float(p[1]), 2),
                          rot=round(float(rng.uniform(0, 360) if rot is None else rot), 1), scale=round(float(scale), 2), **extra))

    def walkable_at(p):
        cx, cy = int(p[0] / CELL), int(p[1] / CELL)
        return 0 <= cx < N and 0 <= cy < N and walk[cy, cx]

    # Lane dressing: braziers every ~18 m alternating sides, ruined walls / pillars beyond the lane edge.
    for name, lane in LANES:
        L = lane_length(lane)
        s = 10.0
        side = 1
        while s < L - 10:
            p, d = lane_point(lane, s)
            n = (-d[1], d[0])
            q = (p[0] + n[0] * 5.2 * side, p[1] + n[1] * 5.2 * side)
            dawn_side = q[0] + q[1] < SIZE
            add("brazier" if dawn_side else "bone_brazier", q, rot=0, lit=True)
            wall = (p[0] + n[0] * 7.5 * -side, p[1] + n[1] * 7.5 * -side)
            add("ruin_wall" if rng.random() < 0.6 else "ruin_pillar", wall, rot=math.degrees(math.atan2(d[1], d[0])) + rng.uniform(-10, 10), scale=rng.uniform(0.8, 1.3))
            s += 18.0 + rng.uniform(-3, 3)
            side = -side
    # Mid river crossing: ruined bridge + two colossal statues facing each other.
    add("ruined_bridge", (96, 96), rot=45, scale=1.0)
    add("colossus_statue", (86, 106), rot=-45, scale=1.0)
    add("colossus_statue", (106, 86), rot=135, scale=1.0)
    # Pit: chains, sealed door, glowing cracks, seals.
    for i in range(10):
        a = i * math.tau / 10
        add("pit_chain", (PIT[0] + math.cos(a) * (PIT_RADIUS + 0.2), PIT[1] + math.sin(a) * (PIT_RADIUS + 0.2)), rot=math.degrees(a) + 90)
    add("vharoth_seal_door", PIT, rot=45, scale=1.0)
    for sp in SEALS:
        add("vharoth_seal", sp, rot=0)
    for i in range(24):
        a = rng.uniform(0, math.tau)
        r = rng.uniform(PIT_RADIUS + 4, PIT_RADIUS + 16)
        add("blood_crack_decal", (PIT[0] + math.cos(a) * r, PIT[1] + math.sin(a) * r), scale=rng.uniform(1.5, 3.5))
    # Giant creature skeleton on the river bank (south-east of the pit, Dusk mirror too).
    add("titan_skeleton", (116, 64), rot=-30, scale=1.0)
    add("titan_skeleton", mirror((116, 64)), rot=150, scale=1.0)
    # Camps: themed props.
    for kind, pos in camps:
        dawn = pos[0] + pos[1] < SIZE
        if kind == "small":
            for _ in range(5):
                add("gravestone", (pos[0] + rng.uniform(-4, 4), pos[1] + rng.uniform(-4, 4)), scale=rng.uniform(0.8, 1.2))
        elif kind == "medium":
            add("hanging_cage", (pos[0] + 3.2, pos[1] + 2.0))
            add("bone_pile", (pos[0] - 2.5, pos[1] - 1.5))
        elif kind == "large":
            add("gargoyle_perch", (pos[0] + 3.5, pos[1] - 3.0))
            add("ruin_arch", (pos[0] - 3.8, pos[1] + 2.5), scale=1.2)
        else:
            add("crypt_entrance", (pos[0] + 4.2, pos[1] + 3.5), rot=225 if dawn else 45, scale=1.3)
            add("bone_pile", (pos[0] - 3.0, pos[1] - 2.5), scale=1.5)
    # Shops.
    for p in secret_shops:
        add("secret_shop", p, rot=0)
    for p in side_shops:
        add("side_shop", p, rot=0)
    add("shop_dawn", DAWN_BASE["shop"], rot=90)
    add("shop_dusk", mirror(DAWN_BASE["shop"]), rot=270)
    for p in rune_spots:
        add("rune_altar", p, rot=0)
    for p in plateaus:
        add("watch_obelisk", p, rot=0)
    # Burning wagons / abandoned siege engines near lane corners.
    for p in [(22, 160), (160, 22), (58, 44), (134, 148)]:
        add("burning_wagon", p, lit=True)
    for p in [(36, 170), (170, 36)]:
        add("abandoned_trebuchet", p, rot=rng.uniform(0, 360))
    # Gothic backdrops outside the playable area (visible at map edges).
    for p, rot in [((-14, 70), 90), ((-12, 130), 90), ((70, -14), 0), ((130, -12), 0), ((206, 120), 270), ((204, 62), 270), ((120, 206), 180), ((62, 204), 180)]:
        add("castle_backdrop", p, rot=rot, scale=1.6)
    add("cathedral_ruin", (40, 146), rot=-45, scale=1.0)
    add("cathedral_ruin", mirror((40, 146)), rot=135, scale=1.0)
    # Graveyards in the Dusk jungle, fences and shrines near Dawn.
    for c in [(150, 120), (120, 150)]:
        for _ in range(12):
            q = (c[0] + rng.uniform(-6, 6), c[1] + rng.uniform(-6, 6))
            if not walkable_at(q):
                continue
            add("gravestone", q, scale=rng.uniform(0.8, 1.3))
    for c in [(42, 72), (72, 42)]:
        add("sun_shrine", c)
    return dict(props=props, trees=[dict(x=round(t[0], 2), y=round(t[1], 2), v=t[2]) for t in trees])


def rle_encode(arr):
    out = bytearray()
    i, n = 0, len(arr)
    while i < n:
        v = arr[i]
        run = 1
        while i + run < n and arr[i + run] == v and run < 65535:
            run += 1
        out += struct.pack("<HB", run, int(v))
        i += run
    return base64.b64encode(bytes(out)).decode("ascii")


def validate_connectivity(cells, structures, camps):
    walk = (cells & 1).astype(bool)
    labels, n = ndimage.label(walk, structure=np.ones((3, 3)))
    main_label = labels[int(DAWN_BASE["spawn"][1] / CELL), int(DAWN_BASE["spawn"][0] / CELL)]
    problems = []

    def check(name, p, radius=4.0):
        cx, cy = int(p[0] / CELL), int(p[1] / CELL)
        r = int(radius / CELL)
        win = labels[max(0, cy - r): cy + r + 1, max(0, cx - r): cx + r + 1]
        if not (win == main_label).any():
            problems.append(name)

    check("dusk spawn", mirror(DAWN_BASE["spawn"]))
    for s in structures:
        check(s["id"], s["position"], 5.0)
    for k, p in camps:
        check(f"camp {k} {p}", p)
    for _, lane in LANES:
        for p in lane:
            check(f"lane point {p}", p, 2.0)
    check("pit", PIT)
    if problems:
        print("CONNECTIVITY PROBLEMS:", problems)
        sys.exit(1)
    print("connectivity OK (all structures, camps, lanes and the pit reachable)")


def preview(cells, height, structures, camps, trees, secret_shops, side_shops, rune_spots, dressing):
    lvl = (cells >> 1) & 3
    walk = cells & 1
    img = np.zeros((N, N, 3), np.float32)
    base_col = {0: (0.35, 0.06, 0.08), 1: (0.20, 0.23, 0.17), 2: (0.36, 0.33, 0.28), 3: (0.18, 0.17, 0.18)}
    for k, c in base_col.items():
        img[lvl == k] = c
    shade = np.gradient(height[:-1, :-1])[0]
    img *= np.clip(1.0 + shade[..., None] * 1.5, 0.6, 1.4)
    img[(walk == 0) & (lvl != 3)] *= 0.55
    im = Image.fromarray((np.clip(img, 0, 1)[::-1] * 255).astype(np.uint8)).resize((N * 3, N * 3), Image.NEAREST)
    d = ImageDraw.Draw(im)
    S = 3 / CELL

    def P(p):
        return (p[0] * S, (SIZE - p[1]) * S)

    for t in trees:
        x, y = P(t)
        d.ellipse([x - 2, y - 2, x + 2, y + 2], fill=(18, 38, 20) if t[2] < 3 else (12, 28, 26))
    for name, lane in LANES:
        d.line([P(p) for p in lane], fill=(150, 130, 90), width=3)
    d.line([P(p) for p in RIVER], fill=(160, 20, 30), width=2)
    for s in structures:
        x, y = P(s["position"])
        col = (220, 190, 110) if s["team"] == "Dawn" else (200, 40, 50)
        r = 9 if "core" in s["id"] else 6 if "t" in s["id"].split("_")[-1] else 7
        d.rectangle([x - r, y - r, x + r, y + r], outline=col, width=3)
    for k, p in camps:
        x, y = P(p)
        r = {"small": 5, "medium": 7, "large": 9, "ancient": 12}[k]
        d.ellipse([x - r, y - r, x + r, y + r], outline=(180, 120, 255), width=3)
    for p in secret_shops:
        x, y = P(p); d.polygon([(x, y - 9), (x + 9, y), (x, y + 9), (x - 9, y)], outline=(80, 220, 220), width=3)
    for p in side_shops:
        x, y = P(p); d.polygon([(x, y - 7), (x + 7, y), (x, y + 7), (x - 7, y)], outline=(120, 200, 120), width=3)
    for p in rune_spots:
        x, y = P(p); d.ellipse([x - 5, y - 5, x + 5, y + 5], fill=(240, 200, 60))
    x, y = P(PIT)
    d.ellipse([x - PIT_RADIUS * S, y - PIT_RADIUS * S, x + PIT_RADIUS * S, y + PIT_RADIUS * S], outline=(255, 40, 40), width=4)
    for sp in SEALS:
        x, y = P(sp); d.rectangle([x - 5, y - 5, x + 5, y + 5], fill=(255, 60, 60))
    out = os.path.join(ROOT, "Docs/Images/velmoragh_layout.png")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    im.save(out)
    print("preview:", out)


if __name__ == "__main__":
    main()
