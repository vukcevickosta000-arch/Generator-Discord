#!/usr/bin/env python3
"""
Ashfields map generator - the first map of War of the Ancients (RTS, 1v1).

A burned plain between two ruined strongholds. Each player starts on a raised plateau with a hall site, a blood-iron
vein and forest to cut; two natural expansions per side are guarded by neutral camps, and two contested expansions
sit on the line of equal distance between the bases. The Dawn (south-west) half is authored; the south-east quarter
is its reflection across y = x and the Dusk half is the point mirror of both, so both sides are identical.

Outputs
  Shared/Runtime/Resources/GameData/maps/ashfields.json   gameplay map (grid, starts, veins, camps, trees)
  Client/Assets/Resources/Maps/Ashfields/height.bytes     uint16 heightfield (render)
  Client/Assets/Resources/Maps/Ashfields/splat0/1.png     terrain layer weights (render)
  Client/Assets/Resources/Maps/Ashfields/dressing.json    prop placements (render)
  Docs/Images/ashfields_layout.png                        top-down design preview

Requires numpy, scipy, pillow. Deterministic (fixed seeds).
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
SIZE = 144.0
CELL = 0.5
N = int(SIZE / CELL)   # 288 cells
V = N + 1
LEVEL_H = {0: -1.2, 1: 0.0, 2: 2.4, 3: 5.0}
H_MIN, H_MAX = -4.0, 12.0

xs = (np.arange(N) + 0.5) * CELL
X, Y = np.meshgrid(xs, xs)      # X[y, x]
vx = np.arange(V) * CELL
VX, VY = np.meshgrid(vx, vx)


def mirror(p):
    return (SIZE - p[0], SIZE - p[1])


def diag(p):
    return (p[1], p[0])


def dawn_and_dusk(points):
    return list(points) + [mirror(p) for p in points]


def quadrants(points):
    """Authored west points -> + their diagonal reflection (south) -> + the Dusk mirror of both."""
    dawn = list(points) + [diag(p) for p in points]
    return dawn + [mirror(p) for p in dawn]


# --------------------------------------------------------------------------------------------- layout (Dawn side)

START = (24.0, 24.0)                  # hall centre
START_FACING = 45.0
MAIN_VEIN = (15.0, 32.5)              # ~11.5 m from the hall
PLATEAU_R = 21.0                      # base plateau radius around the start
RAMP = (40.5, 40.5)                   # plateau exit toward the centre
NATURAL_VEINS_WEST = [(15.0, 68.0)]   # + diagonal -> (68, 15)
NATURAL_CAMPS_WEST = [("camp_medium", (24.0, 62.0))]
CONTESTED_VEINS = [(38.0, 106.0)]     # on x + y = 144: equidistant from both starts (+ mirror)
CONTESTED_CAMPS = [("camp_large", (46.0, 100.0))]
CENTRE_CAMP = ("camp_ancient", (72.0, 72.0))
ROADS_WEST = [
    [RAMP, (52, 52), (72, 72)],                         # main road to the centre
    [(34, 44), (26, 56), (20, 66)],                      # to the natural
    [(52, 52), (46, 76), (42, 100)],                     # to the contested expansion
    [(20, 66), (28, 84), (42, 100)],                     # natural -> contested
]
ROCKS_WEST = [((34.0, 78.0), 3.0), ((56.0, 88.0), 2.6), ((60.0, 64.0), 2.2), ((10.0, 88.0), 3.4)]
RUINS_WEST = [(30.0, 92.0), (58.0, 76.0), (48.0, 60.0)]


def build_features():
    veins = []
    for i, p in enumerate([MAIN_VEIN]):
        veins.append((f"vein_dawn_main", p, 12500))
        veins.append((f"vein_dusk_main", mirror(p), 12500))
    nat = NATURAL_VEINS_WEST + [diag(p) for p in NATURAL_VEINS_WEST]
    for i, p in enumerate(nat):
        veins.append((f"vein_dawn_natural_{i}", p, 10000))
        veins.append((f"vein_dusk_natural_{i}", mirror(p), 10000))
    for i, p in enumerate(CONTESTED_VEINS):
        veins.append((f"vein_contested_{2 * i}", p, 12500))
        veins.append((f"vein_contested_{2 * i + 1}", mirror(p), 12500))
    camps = []
    for kind, p in NATURAL_CAMPS_WEST:
        camps += [(kind, q) for q in quadrants([p])]
    for kind, p in CONTESTED_CAMPS:
        camps += [(kind, q) for q in dawn_and_dusk([p])]
    camps.append(CENTRE_CAMP)
    roads = []
    for r in ROADS_WEST:
        roads.append(r)
        roads.append([diag(q) for q in r])
    roads = roads + [[mirror(q) for q in r] for r in roads]
    rocks = []
    for c, r in ROCKS_WEST:
        rocks += [(q, r) for q in quadrants([c])]
    ruins = quadrants(RUINS_WEST)
    return veins, camps, roads, rocks, ruins


# --------------------------------------------------------------------------------------------- helpers

def dist_to_polyline(px, py, poly):
    d = np.full(px.shape, 1e9, dtype=np.float32)
    for a, b in zip(poly[:-1], poly[1:]):
        ax, ay = a
        bx, by = b
        abx, aby = bx - ax, by - ay
        L = abx * abx + aby * aby
        t = np.clip(((px - ax) * abx + (py - ay) * aby) / max(L, 1e-9), 0, 1)
        d = np.minimum(d, np.hypot(px - (ax + abx * t), py - (ay + aby * t)))
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


def poisson_trees(mask, spacing, seed=42):
    """Bridson-style blue noise restricted to mask (0.5 m grid). Variants: 0-2 dead oaks, 3-5 black pines, 6 giant."""
    rng = np.random.default_rng(seed)
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
            # Burned land: mostly dead oaks, black pines in the cooler north-west / south-east.
            r = rng.random()
            v = int(rng.integers(3, 6)) if r < 0.35 else int(rng.integers(0, 3))
            if rng.random() < 0.015:
                v = 6
            pts.append((x, y, v))
    return pts


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


# --------------------------------------------------------------------------------------------- main

def main():
    veins, camps, roads, rocks, ruins = build_features()
    starts = [("Dawn", START, START_FACING), ("Dusk", mirror(START), START_FACING + 180.0)]

    road_d = np.full(X.shape, 1e9, dtype=np.float32)
    for r in roads:
        road_d = np.minimum(road_d, dist_to_polyline(X, Y, r))

    def plateau_mask(px, py):
        return (np.hypot(px - START[0], py - START[1]) <= PLATEAU_R) | (np.hypot(px - (SIZE - START[0]), py - (SIZE - START[1])) <= PLATEAU_R)

    plateau = plateau_mask(X, Y)
    plateau_v = plateau_mask(VX, VY)
    ramps = [RAMP, mirror(RAMP)]

    level = np.ones(X.shape, np.uint8)
    level[plateau] = 2

    walk = np.ones(X.shape, bool)
    rock = np.zeros(X.shape, bool)
    for c, r in rocks:
        rock |= disc(X, Y, c, r)
    border = (X < 2.5) | (Y < 2.5) | (X > SIZE - 2.5) | (Y > SIZE - 2.5)
    rock |= border
    rock &= ~(road_d < 3.5)
    level[rock] = 3
    walk &= ~rock

    # Plateau cliffs: walkable only through the ramp.
    edt_in = ndimage.distance_transform_edt(plateau) * CELL
    edt_out = ndimage.distance_transform_edt(~plateau) * CELL
    near_edge = ((edt_in < 1.0) & plateau) | ((edt_out < 0.6) & ~plateau)
    ramp_mask = np.zeros(X.shape, bool)
    for rc in ramps:
        ramp_mask |= disc(X, Y, rc, 5.5)
    walk &= ~(near_edge & ~ramp_mask)
    level[ramp_mask & plateau] = 2

    # Clearings: hall sites, veins, camps and ruins stay free of trees.
    clearing = np.zeros(X.shape, bool)
    for _, p, _ in starts:
        clearing |= disc(X, Y, p, 7.5)
    for _, p, _ in veins:
        clearing |= disc(X, Y, p, 5.0)
    # Room for an expansion hall next to every natural / contested vein (toward the road).
    for vid, p, _ in veins:
        if "main" in vid:
            continue
        clearing |= disc(X, Y, p, 11.0)
    for kind, p in camps:
        clearing |= disc(X, Y, p, 7.0 if kind == "camp_ancient" else 5.5)
    for p in ruins:
        clearing |= disc(X, Y, p, 4.0)
    for rc in ramps:
        clearing |= disc(X, Y, rc, 6.0)
    # Mining lane: open ground between each hall and its main vein.
    for (_, sp, _), vp in zip(starts, [MAIN_VEIN, mirror(MAIN_VEIN)]):
        clearing |= dist_to_polyline(X, Y, [sp, vp]) < 4.5

    # ---- Forests: a lumber line behind each base, walls along the edges, groves in between.
    edge_band = (np.minimum.reduce([X, Y, SIZE - X, SIZE - Y]) < 12.0)
    open_field = fbm((N, N), 40, 4, seed=21)
    groves = open_field > 0.35
    base_back = np.zeros(X.shape, bool)
    for _, p, _ in starts:
        d = np.hypot(X - p[0], Y - p[1])
        base_back |= (d > 13.0) & (d < PLATEAU_R - 1.5)
    # Keep the side of the plateau facing the ramp open (the base's front yard).
    front = np.zeros(X.shape, bool)
    for (_, p, _), rc in zip(starts, ramps):
        dirx, diry = rc[0] - p[0], rc[1] - p[1]
        L = math.hypot(dirx, diry)
        proj = ((X - p[0]) * dirx + (Y - p[1]) * diry) / L
        front |= (proj > 2.0) & (np.hypot(X - p[0], Y - p[1]) < PLATEAU_R + 4)
    forest = (edge_band | groves | base_back) & ~front
    forest &= (road_d > 4.5) & ~clearing & ~rock & walk
    forest &= ~(np.hypot(X - SIZE / 2, Y - SIZE / 2) < 16.0)   # the burned centre is open ground
    # Trees are sampled on the Dawn half and point-mirrored, so both sides get exactly the same forests.
    # Keeping 1.2 m off the dividing diagonal leaves mirrored trunks at least the Poisson spacing apart.
    half = forest & (X + Y < SIZE - 1.2)
    half_trees = poisson_trees(half, spacing=1.55)
    trees = half_trees + [(SIZE - x, SIZE - y, v) for x, y, v in half_trees]
    tree_block = np.zeros(X.shape, bool)
    for (tx, ty, _v) in trees:
        tree_block |= disc(X, Y, (tx, ty), 0.72)
    walk_final = walk & ~tree_block

    vision_block = rock.copy()
    cells = np.zeros(X.shape, np.uint8)
    cells |= walk_final.astype(np.uint8)
    cells |= (np.clip(level, 0, 3).astype(np.uint8) << 1)
    cells |= (tree_block.astype(np.uint8) << 3)
    cells |= (vision_block.astype(np.uint8) << 5)
    cells |= ((ramp_mask & walk_final).astype(np.uint8) << 7)

    height = build_heights(plateau_v, ramps, rocks)
    splat0, splat1 = build_splat(road_d, rock, plateau, tree_block)
    dressing = build_dressing(trees, camps, ruins, veins, walk_final)

    tree_bin = b"".join(struct.pack("<HHB", int(round(t[0] * 10)), int(round(t[1] * 10)), t[2]) for t in trees)
    camp_json = []
    for i, (kind, pos) in enumerate(camps):
        side = "Dawn" if pos[0] + pos[1] < SIZE - 0.1 else "Dusk" if pos[0] + pos[1] > SIZE + 0.1 else "Neutral"
        # Expansion camps guard their veins; the centre camp only fights when attacked, so armies can meet beside it.
        camp_json.append(dict(id=f"camp_{side.lower()}_{kind[5:]}_{i}", campType=kind, position=[round(pos[0], 2), round(pos[1], 2)], spawnBoxRadius=3.2, side=side,
                              guards=kind != "camp_ancient"))
    map_def = dict(
        id="map_rts_ashfields",
        name="Ashfields",
        subtitle="Where the Ancients Burned",
        description="A plain of ash and dead oaks where the first war of the Ancients ended in fire. Two strongholds face each other across the cinders; blood-iron still seeps from the scorched earth.",
        width=SIZE, height=SIZE, cellSize=CELL, gridWidth=N, gridHeight=N,
        grid=rle_encode(cells.flatten()),
        startLocations=[dict(team=t, position=[round(p[0], 2), round(p[1], 2)], facing=f) for t, p, f in starts],
        resourceNodes=[dict(id=vid, unitId="rts_bloodiron_vein", position=[round(p[0], 2), round(p[1], 2)], amount=amt) for vid, p, amt in veins],
        camps=camp_json,
        trees=base64.b64encode(tree_bin).decode("ascii"),
        dressingFile="Maps/Ashfields/dressing",
        heightFile="Maps/Ashfields/height",
        heightScale=1.0,
    )
    out_map = os.path.join(ROOT, "Shared/Runtime/Resources/GameData/maps/ashfields.json")
    with open(out_map, "w") as f:
        json.dump({"$comment": "Generated by Tools/mapgen/generate_ashfields.py - edit the generator, not this file.", "map": map_def}, f, separators=(",", ":"))

    render_dir = os.path.join(ROOT, "Client/Assets/Resources/Maps/Ashfields")
    os.makedirs(render_dir, exist_ok=True)
    hq = (np.clip((height - H_MIN) / (H_MAX - H_MIN), 0, 1) * 65535).astype("<u2")
    with open(os.path.join(render_dir, "height.bytes"), "wb") as f:
        f.write(struct.pack("<iiff", V, V, H_MIN, H_MAX))
        f.write(hq.tobytes())
    Image.fromarray(splat0, "RGBA").save(os.path.join(render_dir, "splat0.png"))
    Image.fromarray(splat1, "RGBA").save(os.path.join(render_dir, "splat1.png"))
    with open(os.path.join(render_dir, "dressing.json"), "w") as f:
        json.dump(dressing, f, separators=(",", ":"))

    preview(cells, height, starts, veins, camps, trees, roads)
    print(f"grid {N}x{N}  walkable {walk_final.mean() * 100:.1f}%  trees {len(trees)}  veins {len(veins)}  camps {len(camps)}  props {len(dressing['props'])}")
    validate(cells, starts, veins, camps)


def build_heights(plateau_v, ramps, rocks):
    h = np.zeros(VX.shape, np.float32)
    edt_in = ndimage.distance_transform_edt(plateau_v) * CELL
    edt_out = ndimage.distance_transform_edt(~plateau_v) * CELL
    signed = np.where(plateau_v, edt_in, -edt_out)
    ramp = np.zeros(VX.shape, np.float32)
    for rc in ramps:
        d = np.hypot(VX - rc[0], VY - rc[1])
        ramp = np.maximum(ramp, np.clip(1 - (d - 3.5) / 4.0, 0, 1))
    width = 0.6 + 8.0 * ramp
    t = np.clip(0.5 + signed / width, 0, 1)
    t = t * t * (3 - 2 * t)
    h += LEVEL_H[2] * t
    noise_big = fbm(VX.shape, 36, 4, seed=3)
    noise_small = fbm(VX.shape, 10, 3, seed=5)
    rock_h = np.zeros(VX.shape, np.float32)
    for c, r in rocks:
        d = np.hypot(VX - c[0], VY - c[1])
        rock_h = np.maximum(rock_h, np.clip((r + 0.6 - d) / 1.2, 0, 1))
    border = np.clip(1 - np.minimum.reduce([VX, VY, SIZE - VX, SIZE - VY]) / 3.0, 0, 1)
    rock_h = np.maximum(rock_h, border)
    rock_h = ndimage.gaussian_filter(rock_h, 1.2)
    h += rock_h * (LEVEL_H[3] + 2.5 * noise_big + 1.2 * noise_small)
    # Shallow burn craters across the plain.
    h += 0.4 * noise_big + 0.12 * noise_small
    return np.clip(h, H_MIN, H_MAX).astype(np.float32)


def build_splat(road_d, rock, plateau, tree_block):
    """splat0: R grass, G dirt, B road, A rock.  splat1: R blood mud, G ash, B leaf litter, A paving (base floors)."""
    size = 512
    sx = np.linspace(0, N - 1, size).astype(int)
    idx = np.ix_(sx, sx)
    rd = road_d[idx]
    rk = rock[idx].astype(np.float32)
    pl = plateau[idx]
    tb = ndimage.gaussian_filter(tree_block.astype(np.float32), 3)[idx]
    n1 = fbm((size, size), 40, 4, seed=11)
    n2 = fbm((size, size), 10, 3, seed=12)
    road = np.clip((3.2 - rd) / 1.4 + 0.3 * n2, 0, 1)
    dirt = np.clip((5.5 - rd) / 2.0, 0, 1) * (1 - road) + np.clip(n1 * 1.2 - 0.2, 0, 1) * 0.4
    ash = np.clip(0.75 + 0.5 * n1, 0, 1) * (1 - road)      # the plain is mostly ash
    blood = np.clip(n2 * 2.0 - 0.9, 0, 1) * 0.6             # blood-iron seepage
    leaves = np.clip(tb * 1.4 + 0.2 * n2, 0, 1) * 0.7
    paving = pl.astype(np.float32) * np.clip(0.8 + 0.3 * n2, 0, 1) * (1 - road * 0.3)
    rockw = np.clip(rk * 1.5, 0, 1)
    grass = np.clip(0.25 - ash * 0.2 + 0.2 * n2, 0, 1)
    layers = np.stack([grass, dirt, road, rockw, blood, ash, leaves, paving], -1)
    layers[..., 1] *= 1 - road
    layers[..., 5] *= 1 - paving
    s = layers.sum(-1, keepdims=True)
    layers = layers / np.maximum(s, 1e-6)
    q = (layers * 255 + 0.5).astype(np.uint8)
    return np.ascontiguousarray(q[..., 0:4][::-1]), np.ascontiguousarray(q[..., 4:8][::-1])


def build_dressing(trees, camps, ruins, veins, walk):
    props = []
    rng = np.random.default_rng(77)

    def add(kind, p, rot=None, scale=1.0, **extra):
        props.append(dict(type=kind, x=round(float(p[0]), 2), y=round(float(p[1]), 2),
                          rot=round(float(rng.uniform(0, 360) if rot is None else rot), 1), scale=round(float(scale), 2), **extra))

    def walkable_at(p):
        cx, cy = int(p[0] / CELL), int(p[1] / CELL)
        return 0 <= cx < N and 0 <= cy < N and walk[cy, cx]

    for p in ruins:
        add("ruin_arch" if rng.random() < 0.4 else "ruin_wall", p, scale=rng.uniform(0.9, 1.3))
        for _ in range(2):
            q = (p[0] + rng.uniform(-3.5, 3.5), p[1] + rng.uniform(-3.5, 3.5))
            add("ruin_pillar", q, scale=rng.uniform(0.7, 1.1))
    for kind, p in camps:
        if kind == "camp_medium":
            add("bone_pile", (p[0] - 2.5, p[1] - 1.5))
            add("hanging_cage", (p[0] + 3.0, p[1] + 2.0))
        elif kind == "camp_large":
            add("gargoyle_perch", (p[0] + 3.5, p[1] - 3.0))
            add("ruin_arch", (p[0] - 3.8, p[1] + 2.5), scale=1.2)
        else:
            add("titan_skeleton", (p[0] + 6.0, p[1] - 6.0), rot=-45)
            add("crypt_entrance", (p[0] - 5.0, p[1] + 5.0), rot=45, scale=1.3)
    # Burned wagons and abandoned siege engines strewn over the plain.
    for p in [(56, 40), (40, 56), (88, 104), (104, 88)]:
        add("burning_wagon", p, lit=True)
    for p in [(62, 30), (30, 62)]:
        add("abandoned_trebuchet", p)
        add("abandoned_trebuchet", mirror(p))
    # Braziers along the plateau ramps; gravestones scattered through the ash.
    for rc, lit in [((40.5, 40.5), "brazier"), (mirror((40.5, 40.5)), "bone_brazier")]:
        add(lit, (rc[0] + 3.0, rc[1] - 3.0), rot=0, lit=True)
        add(lit, (rc[0] - 3.0, rc[1] + 3.0), rot=0, lit=True)
    for _ in range(60):
        q = (rng.uniform(8, SIZE - 8), rng.uniform(8, SIZE - 8))
        if walkable_at(q) and all(math.dist(q, v[1]) > 6 for v in veins):
            add("gravestone", q, scale=rng.uniform(0.7, 1.2))
    for p, rot in [((-12, 50), 90), ((-10, 100), 90), ((50, -12), 0), ((100, -10), 0), ((156, 94), 270), ((154, 44), 270), ((94, 156), 180), ((44, 154), 180)]:
        add("castle_backdrop", p, rot=rot, scale=1.5)
    return dict(props=props, trees=[dict(x=round(t[0], 2), y=round(t[1], 2), v=t[2]) for t in trees])


def validate(cells, starts, veins, camps):
    walk = (cells & 1).astype(bool)
    labels, _ = ndimage.label(walk, structure=np.ones((3, 3)))
    sx, sy = starts[0][1]
    # The hall site itself is walkable in the static grid; the simulation blocks its footprint at spawn.
    main = labels[int(sy / CELL), int(sx / CELL)]
    problems = []

    def reachable(p, r):
        cx, cy = int(p[0] / CELL), int(p[1] / CELL)
        k = int(r / CELL)
        return (labels[max(0, cy - k): cy + k + 1, max(0, cx - k): cx + k + 1] == main).any()

    for t, p, _ in starts:
        if not reachable(p, 1.0):
            problems.append(f"start {t}")
    for vid, p, _ in veins:
        if not reachable(p, 2.6):
            problems.append(vid)
        if math.dist(p, starts[0][1]) < 7.5 or math.dist(p, starts[1][1]) < 7.5:
            problems.append(f"{vid} too close to a start")
    for kind, p in camps:
        if not reachable(p, 3.0):
            problems.append(f"{kind} {p}")
    if problems:
        print("MAP PROBLEMS:", problems)
        sys.exit(1)
    print("validation OK (starts, veins and camps connected)")


def preview(cells, height, starts, veins, camps, trees, roads):
    lvl = (cells >> 1) & 3
    walk = cells & 1
    img = np.zeros((N, N, 3), np.float32)
    for k, c in {0: (0.35, 0.06, 0.08), 1: (0.26, 0.24, 0.22), 2: (0.40, 0.36, 0.30), 3: (0.16, 0.15, 0.16)}.items():
        img[lvl == k] = c
    shade = np.gradient(height[:-1, :-1])[0]
    img *= np.clip(1.0 + shade[..., None] * 1.5, 0.6, 1.4)
    img[(walk == 0) & (lvl != 3)] *= 0.55
    scale = 4
    im = Image.fromarray((np.clip(img, 0, 1)[::-1] * 255).astype(np.uint8)).resize((N * scale, N * scale), Image.NEAREST)
    d = ImageDraw.Draw(im)
    S = scale / CELL

    def P(p):
        return (p[0] * S, (SIZE - p[1]) * S)

    for t in trees:
        x, y = P(t)
        d.ellipse([x - 2.5, y - 2.5, x + 2.5, y + 2.5], fill=(26, 30, 18) if t[2] < 3 else (14, 26, 22))
    for r in roads:
        d.line([P(p) for p in r], fill=(150, 130, 90), width=3)
    for t, p, _ in starts:
        x, y = P(p)
        col = (220, 190, 110) if t == "Dawn" else (200, 40, 50)
        r = 2.6 * S
        d.rectangle([x - r, y - r, x + r, y + r], outline=col, width=4)
    for vid, p, _ in veins:
        x, y = P(p)
        r = 1.7 * S
        d.ellipse([x - r, y - r, x + r, y + r], fill=(190, 30, 40), outline=(255, 120, 120), width=2)
    for kind, p in camps:
        x, y = P(p)
        r = {"camp_small": 5, "camp_medium": 7, "camp_large": 9, "camp_ancient": 12}[kind] * scale / 3
        d.ellipse([x - r, y - r, x + r, y + r], outline=(180, 120, 255), width=3)
    out = os.path.join(ROOT, "Docs/Images/ashfields_layout.png")
    im.save(out)
    print("preview:", out)


if __name__ == "__main__":
    main()
