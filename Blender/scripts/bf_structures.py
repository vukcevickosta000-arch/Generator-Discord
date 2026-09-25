"""
Structures and objects: towers (per team and tier), barracks, cores, fountains, wards, Vharoth's seal crystal, Morwen's
cauldron, and the siege carts (rigged: wheels and throwing arm).

Design language (Docs/ART_DIRECTION.md): Dawn = ivory stone, gold, warm light; Dusk = obsidian, crimson, bone, red glow.
RTS factions reuse these shapes with their own palettes (Crimson Court: the noble Dawn shapes in blood marble and gold)
or get their own (Wild Covenant: living wood, moss, standing stones and moonlight).
Static models get a single "root" bone so Unity treats every model the same way.
"""
import math

import bf_lib as L
from mathutils import Euler, Vector

DAWN = dict(style="noble", water=(0.3, 0.6, 0.95), stone=(0.8, 0.76, 0.66), stone2=(0.66, 0.62, 0.54), trim=(0.86, 0.66, 0.26), roof=(0.3, 0.36, 0.46),
            glow=(1.0, 0.8, 0.4), cloth=(0.9, 0.84, 0.66), wood=(0.45, 0.33, 0.2))
DUSK = dict(style="grim", water=(0.55, 0.02, 0.07), stone=(0.2, 0.16, 0.18), stone2=(0.28, 0.22, 0.24), trim=(0.58, 0.07, 0.1), roof=(0.14, 0.1, 0.12),
            glow=(1.0, 0.14, 0.12), cloth=(0.45, 0.04, 0.07), wood=(0.24, 0.18, 0.16), bone=(0.82, 0.77, 0.66))


CRIMSON = dict(style="noble", stone=(0.42, 0.13, 0.15), stone2=(0.25, 0.08, 0.1), trim=(0.82, 0.62, 0.28), roof=(0.09, 0.05, 0.07),
               glow=(1.0, 0.12, 0.18), cloth=(0.55, 0.04, 0.09), wood=(0.26, 0.13, 0.11), bone=(0.82, 0.77, 0.66), water=(0.6, 0.02, 0.08))
WILD = dict(style="wild", stone=(0.44, 0.43, 0.38), stone2=(0.33, 0.33, 0.29), bark=(0.33, 0.24, 0.16), bark2=(0.24, 0.17, 0.11),
            leaf=(0.2, 0.37, 0.15), leaf2=(0.3, 0.46, 0.2), moss=(0.27, 0.4, 0.18), trim=(0.62, 0.68, 0.76), roof=(0.36, 0.33, 0.2),
            glow=(0.6, 0.85, 1.0), cloth=(0.45, 0.36, 0.25), wood=(0.36, 0.26, 0.17), bone=(0.84, 0.8, 0.7), water=(0.5, 0.78, 1.0))


def team_palette(key):
    if "_crimson" in key or key.startswith("rts_cc_"):
        return CRIMSON
    if "_wild" in key or key.startswith("rts_wc_"):
        return WILD
    return DAWN if "_dawn" in key else DUSK


def static_rig(key):
    return L.build_armature(key, [("root", (0, 0, 0), (0, 0, 1.0), None)])


# ================================================================================================ towers

def tower(kit, key):
    P = team_palette(key)
    tier = int(key[-1])
    if P["style"] == "wild":
        return wild_den(kit, P) if tier == 4 else wild_totem(kit, P)
    dawn = P["style"] == "noble"
    H = 6.8 + tier * 0.8
    kit.use(bone="root", color=P["stone2"], mat="bf_matte", smooth=False)
    # Stepped octagonal plinth.
    kit.lathe([(1.9, 0), (1.9, 0.35), (1.65, 0.35), (1.65, 0.7), (1.45, 0.7)], seg=8, close_top=True)
    kit.use(color=P["stone"])
    # Tapered shaft with bands.
    kit.lathe([(1.25, 0.7), (1.05, H * 0.55), (1.12, H * 0.62), (1.35, H * 0.72)], seg=8, close_bottom=False, close_top=False)
    kit.use(color=P["trim"], mat="bf_metal")
    for z in (1.4, H * 0.35, H * 0.55):
        kit.lathe([(1.2 - z * 0.02, z - 0.08), (1.24 - z * 0.02, z + 0.08)], seg=8, close_top=False, close_bottom=False)
    # Buttresses.
    kit.use(color=P["stone2"], mat="bf_matte")
    for i in range(4):
        a = math.pi / 4 + i * math.pi / 2
        d = Vector((math.cos(a), math.sin(a), 0))
        kit.box((d * 1.45) + Vector((0, 0, 1.2)), (0.45, 0.6, 2.4), rot=Euler((0, 0, a)), taper=0.4, bevel=0.04)
    # Crown.
    top = H * 0.72
    kit.use(color=P["stone"])
    kit.lathe([(1.35, top), (1.6, top + 0.2), (1.6, top + 0.55), (1.3, top + 0.55)], seg=8, close_bottom=True, close_top=True)
    if dawn:
        for i in range(8):
            a = i * math.pi / 4 + math.pi / 8
            kit.box((math.cos(a) * 1.45, math.sin(a) * 1.45, top + 0.8), (0.45, 0.35, 0.5), rot=Euler((0, 0, a)), bevel=0.03)
        kit.use(color=P["roof"])
        kit.lathe([(0.9, top + 0.55), (0.6, top + 1.2), (0.05, top + 2.6 + tier * 0.2)], seg=8, close_bottom=True, close_top=True)
        kit.use(color=P["trim"], mat="bf_metal")
        kit.spikes((0, 0, top + 2.6 + tier * 0.2), (0, 0, 1), 0.01, 1, 0.5, r=0.06)
        # Sunstone cradle.
        kit.lathe([(0.7, -0.05), (0.75, 0.05)], center=(0, 0, top + 1.35), axis=(0, 1, 0), seg=16, close_top=False, close_bottom=False)
        kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
        kit.ellipsoid((0, 0, top + 1.35), (0.42,) * 3, seg=14, rings=10)
        for i in range(tier):
            a = i * 2 * math.pi / max(1, tier)
            kit.ellipsoid((math.cos(a) * 1.0, math.sin(a) * 1.0, top + 0.95), (0.12,) * 3, seg=8, rings=5)
    else:
        kit.use(color=P["bone"], mat="bf_matte")
        kit.spikes((0, 0, top + 0.5), (0, 0, 1), 1.35, 8, [0.9, 0.6], r=0.14, spread=0.35)
        for sgn in (-1, 1):
            b = Vector((sgn * 1.1, 0, top + 0.4))
            kit.tube([b, b + Vector((sgn * 0.8, 0, 0.6)), b + Vector((sgn * 0.7, 0, 1.6)), b + Vector((sgn * 0.3, 0, 2.2))], [0.2, 0.16, 0.1, 0.02], seg=8)
        kit.use(color=P["stone2"])
        kit.lathe([(0.6, top + 0.55), (0.35, top + 1.0)], seg=8, close_top=True)
        kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
        kit.ellipsoid((0, 0, top + 1.55), (0.3, 0.3, 0.55), seg=8, rings=6)
        for i in range(2 + tier):
            a = i * 2 * math.pi / (2 + tier)
            kit.ellipsoid((math.cos(a) * 0.8, math.sin(a) * 0.8, top + 1.4 + (i % 2) * 0.3), (0.08, 0.08, 0.2), seg=6, rings=4)
    # Arrow slits / glowing windows.
    kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
    for i in range(4):
        a = i * math.pi / 2
        n = Vector((math.cos(a), math.sin(a), 0))
        kit.box(n * 1.12 + Vector((0, 0, H * 0.45)), (0.12, 0.12, 0.7), rot=Euler((0, 0, a)))
    return H * 0.72 + 1.35


# ================================================================================================ barracks

def barracks(kit, key):
    P = team_palette(key)
    ranged = "ranged" in key
    if P["style"] == "wild":
        return wild_stone_circle(kit, P) if ranged else wild_lodge(kit, P)
    dawn = P["style"] == "noble"
    w, d = (4.2, 3.6) if ranged else (5.0, 4.0)
    kit.use(bone="root", color=P["stone2"], mat="bf_matte", smooth=False)
    kit.box((0, 0, 0.2), (w + 0.6, d + 0.6, 0.4), bevel=0.05)
    kit.use(color=P["stone"])
    kit.box((0, 0, 1.9), (w, d, 3.0), bevel=0.06)
    # Corner pillars.
    kit.use(color=P["stone2"])
    for sx in (-1, 1):
        for sy in (-1, 1):
            kit.box((sx * w / 2, sy * d / 2, 2.1), (0.6, 0.6, 3.6), bevel=0.05)
            kit.use(color=P["trim"], mat="bf_metal")
            if dawn:
                kit.cone((sx * w / 2, sy * d / 2, 3.9), (sx * w / 2, sy * d / 2, 4.6), 0.35, seg=4)
            else:
                kit.cone((sx * w / 2, sy * d / 2, 3.9), (sx * w / 2 * 1.08, sy * d / 2 * 1.08, 5.0), 0.2, seg=5)
            kit.use(color=P["stone2"], mat="bf_matte")
    # Roof.
    kit.use(color=P["roof"])
    if ranged:
        kit.lathe([(w * 0.55, 3.4), (0.05, 5.6)], seg=4, scale=(1.0, d / w), close_top=True)
    else:
        kit.box((0, 0, 3.9), (w + 0.3, d + 0.3, 1.0), taper=0.55, bevel=0.04)
    # Door (front -Y) and emblem.
    kit.use(color=P["wood"])
    kit.box((0, -d / 2 - 0.02, 1.3), (1.3, 0.15, 2.0), bevel=0.04)
    kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
    kit.box((0, -d / 2 - 0.1, 2.75), (0.5, 0.06, 0.5), rot=Euler((0, math.pi / 4, 0)))
    # Banners.
    kit.use(color=P["cloth"], mat="bf_matte", smooth=True)
    for sx in (-1, 1):
        kit.sheet([(sx * w * 0.3 - 0.35, -d / 2 - 0.1, 3.2), (sx * w * 0.3 + 0.35, -d / 2 - 0.1, 3.2)],
                  [(sx * w * 0.3 - 0.35, -d / 2 - 0.2, 1.3), (sx * w * 0.3 + 0.35, -d / 2 - 0.2, 1.3)], cols=3, rows=5, wave=0.05, thick=0.03)
    kit.use(color=P["trim"], mat="bf_metal")
    if ranged:
        kit.tube([(-0.9, -d / 2 - 0.15, 3.4), (0, -d / 2 - 0.35, 3.0), (0.9, -d / 2 - 0.15, 3.4)], [0.05, 0.08, 0.05], seg=6)
    else:
        kit.blade((0, -d / 2 - 0.2, 2.2), (0, -d / 2 - 0.2, 3.5), 0.25, 0.05, side=(1, 0, 0))
    return 5.0


# ================================================================================================ cores

def core(kit, key):
    P = team_palette(key)
    if P["style"] == "wild":
        return wild_heart_tree(kit, P)
    dawn = P["style"] == "noble"
    kit.use(bone="root", color=P["stone2"], mat="bf_matte", smooth=False)
    kit.lathe([(4.2, 0), (4.2, 0.4), (3.6, 0.4), (3.6, 0.9), (3.0, 0.9)], seg=12, close_top=True)
    if dawn:
        kit.use(color=P["stone"])
        for i in range(6):
            a = i * math.pi / 3
            kit.box((math.cos(a) * 2.4, math.sin(a) * 2.4, 3.2), (0.7, 0.7, 5.0), rot=Euler((0, 0, a)), taper=0.7, bevel=0.05)
            kit.use(color=P["trim"], mat="bf_metal")
            kit.cone((math.cos(a) * 2.4, math.sin(a) * 2.4, 5.7), (math.cos(a) * 2.2, math.sin(a) * 2.2, 7.0), 0.35, seg=4)
            kit.use(color=P["stone"], mat="bf_matte")
        kit.lathe([(2.2, 0.9), (1.6, 6.0), (1.9, 6.4), (0.9, 8.5), (0.05, 12.0)], seg=12, close_top=True, close_bottom=False)
        kit.use(color=P["trim"], mat="bf_metal")
        kit.lathe([(3.1, -0.08), (3.2, 0.08)], center=(0, 0, 8.0), axis=(0, 1, 0), seg=24, close_top=False, close_bottom=False)
        kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
        kit.ellipsoid((0, 0, 8.0), (1.6, 0.4, 1.6), seg=20, rings=10)
        kit.spikes((0, 0, 8.0), (0, 1, 0), 1.7, 16, [1.0, 0.6], r=0.12, spread=0)
    else:
        kit.use(color=P["stone"])
        for i in range(5):
            a = i * 2 * math.pi / 5
            kit.tube([(math.cos(a) * 3.0, math.sin(a) * 3.0, 0.5), (math.cos(a) * 2.6, math.sin(a) * 2.6, 4.0), (math.cos(a) * 1.4, math.sin(a) * 1.4, 8.5)],
                     [0.6, 0.45, 0.1], seg=6)
        kit.use(color=P["bone"])
        for i in range(5):
            a = i * 2 * math.pi / 5 + math.pi / 5
            kit.tube([(math.cos(a) * 3.3, math.sin(a) * 3.3, 0.5), (math.cos(a) * 3.5, math.sin(a) * 3.5, 3.0), (math.cos(a) * 2.2, math.sin(a) * 2.2, 5.5)],
                     [0.3, 0.25, 0.04], seg=6)
        kit.use(color=P["stone2"])
        kit.lathe([(2.0, 0.9), (1.4, 4.0), (0.9, 6.0)], seg=10, close_top=True, close_bottom=False)
        kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
        kit.ellipsoid((0, 0, 7.5), (1.3, 1.3, 1.9), seg=10, rings=7)
        for i in range(6):
            a = i * math.pi / 3
            kit.ellipsoid((math.cos(a) * 2.2, math.sin(a) * 2.2, 7.0 + (i % 2) * 0.8), (0.18, 0.18, 0.45), seg=6, rings=4)
    return 10.0


# ================================================================================================ fountains

def fountain(kit, key):
    P = team_palette(key)
    dawn = P["style"] == "noble"
    kit.use(bone="root", color=P["stone2"], mat="bf_matte", smooth=False)
    kit.lathe([(4.2, 0), (4.3, 0.6), (3.9, 0.7), (3.8, 0.3), (0.5, 0.3)], seg=20, close_top=True)
    water = P["water"]
    kit.use(color=water, mat=kit.glow(water))
    kit.lathe([(3.8, 0.45), (0.1, 0.45)], seg=20, close_top=True, close_bottom=False)
    kit.use(color=P["stone"], mat="bf_matte")
    kit.lathe([(0.9, 0.3), (0.6, 1.6), (1.5, 1.9), (1.4, 2.1), (0.45, 2.2), (0.35, 3.4)], seg=12, close_top=True)
    if P["style"] == "wild":
        # Moonwell: mossy stones around the pool and a moon-silver crescent over the spout.
        kit.use(color=P["moss"])
        kit.lathe([(1.5, 1.9), (1.2, 2.25), (0.4, 2.3)], seg=12, close_top=True, close_bottom=False)
        kit.use(color=P["stone"])
        for i in range(6):
            a = i * math.pi / 3 + 0.3
            kit.box((math.cos(a) * 4.2, math.sin(a) * 4.2, 0.9), (0.7, 0.5, 1.8), rot=Euler((0, 0, a)), taper=0.6, bevel=0.06)
        kit.use(color=P["trim"], mat="bf_metal")
        kit.tube([(math.cos(t) * 0.8, 0, 3.4 + math.sin(t) * 0.8) for t in (0.4, 1.2, 2.0, 2.8)], [0.05, 0.12, 0.12, 0.05], seg=8)
        kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
        kit.ellipsoid((0, 0, 3.7), (0.3,) * 3, seg=10, rings=7)
    elif dawn:
        kit.use(color=P["trim"], mat="bf_metal")
        kit.spikes((0, 0, 3.4), (0, 0, 1), 0.3, 8, [0.8, 0.5], r=0.1, spread=0.6)
        kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
        kit.ellipsoid((0, 0, 3.7), (0.35,) * 3, seg=10, rings=7)
    else:
        kit.use(color=P["bone"])
        kit.ellipsoid((0, 0, 3.8), (0.45, 0.5, 0.5), seg=10, rings=7)
        kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
        for sgn in (-1, 1):
            kit.ellipsoid((sgn * 0.16, -0.42, 3.85), (0.08,) * 3, seg=6, rings=4)
    return 4.0


def mix_c(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


# ================================================================================================ Wild Covenant (organic)

def wild_heart_tree(kit, P):
    """Heart Grove: a vast living oak on a mossy mound, a ring of standing stones and a moonstone in its hollow."""
    kit.use(bone="root", color=P["moss"], mat="bf_matte", smooth=True)
    kit.lathe([(4.3, 0), (4.1, 0.35), (3.2, 0.8), (2.4, 1.0)], seg=16, close_top=True)
    kit.use(color=P["stone"], smooth=False)
    for i in range(7):
        a = i * 2 * math.pi / 7 + 0.2
        kit.box((math.cos(a) * 3.8, math.sin(a) * 3.8, 1.0), (0.75, 0.55, 2.0 + (i % 3) * 0.35), rot=Euler((0, 0, a)), taper=0.55, bevel=0.07)
    kit.use(color=P["bark"], smooth=True)
    kit.lathe([(2.3, 0.6), (1.75, 2.6), (1.35, 5.0), (1.45, 6.6), (1.1, 7.6)], seg=12, close_top=True, close_bottom=False)
    for i in range(7):
        a = i * 2 * math.pi / 7
        c, s = math.cos(a), math.sin(a)
        kit.tube([(c * 1.4, s * 1.4, 1.8), (c * 2.4, s * 2.4, 0.9), (c * 3.3, s * 3.3, 0.35), (c * 3.9, s * 3.9, 0.05)], [0.55, 0.42, 0.25, 0.08], seg=8)
    kit.use(color=P["bark2"])
    for i in range(6):
        a = i * math.pi / 3 + 0.4
        c, s = math.cos(a), math.sin(a)
        kit.tube([(c * 0.8, s * 0.8, 6.8), (c * 2.0, s * 2.0, 8.0), (c * 3.2, s * 3.2, 8.9), (c * 3.8, s * 3.8, 9.6)], [0.45, 0.32, 0.2, 0.08], seg=8)
    kit.use(color=P["leaf"], mat="bf_matte")
    kit.ellipsoid((0, 0, 10.6), (3.6, 3.6, 2.1), seg=16, rings=9)
    kit.use(color=P["leaf2"])
    for i in range(6):
        a = i * math.pi / 3 + 0.4
        kit.ellipsoid((math.cos(a) * 3.0, math.sin(a) * 3.0, 9.5 + (i % 2) * 0.5), (1.7, 1.7, 1.2), seg=12, rings=7)
    kit.use(color=(0.05, 0.04, 0.03))
    kit.ellipsoid((0, -1.55, 2.1), (0.62, 0.35, 0.95), seg=12, rings=7)
    kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
    kit.ellipsoid((0, -1.7, 2.1), (0.32, 0.22, 0.5), seg=10, rings=6)
    for i in range(9):
        a = i * 2 * math.pi / 9
        kit.ellipsoid((math.cos(a) * (2.2 + (i % 3) * 0.6), math.sin(a) * (2.2 + (i % 3) * 0.6), 8.2 + (i % 4) * 0.5), (0.1,) * 3, seg=6, rings=4)
    return 12.5


def wild_totem(kit, P):
    """Thorn Totem: a twisted living trunk bristling with thorns, crowned with antlers around a moon eye."""
    H = 6.2
    kit.use(bone="root", color=P["moss"], mat="bf_matte", smooth=True)
    kit.lathe([(1.9, 0), (1.7, 0.3), (1.1, 0.55)], seg=10, close_top=True)
    kit.use(color=P["bark"])
    for i in range(5):
        a = i * 2 * math.pi / 5
        c, s = math.cos(a), math.sin(a)
        kit.tube([(c * 0.4, s * 0.4, 1.0), (c * 1.2, s * 1.2, 0.45), (c * 1.8, s * 1.8, 0.05)], [0.3, 0.2, 0.07], seg=6)
    pts, radii = [], []
    for j in range(8):
        z = 0.3 + j * (H - 0.3) / 7
        off = 0.28 * math.sin(j * 1.4)
        pts.append((off * math.cos(j * 1.1), off * math.sin(j * 1.1), z))
        radii.append(0.72 - j * 0.06)
    kit.tube(pts, radii, seg=9)
    kit.use(color=P["bark2"])
    for j in (2, 3, 4, 5, 6):
        kit.spikes(pts[j], (0, 0, 1), radii[j] * 0.9, 6, [0.55, 0.35], r=0.07, phase=j * 0.5, spread=2.5)
    kit.use(color=P["bone"])
    for sgn in (-1, 1):
        b = Vector((sgn * 0.3, 0, H))
        kit.tube([b, b + Vector((sgn * 0.7, 0, 0.5)), b + Vector((sgn * 0.9, 0, 1.3)), b + Vector((sgn * 0.6, 0, 1.9))], [0.12, 0.1, 0.07, 0.02], seg=6)
        kit.tube([b + Vector((sgn * 0.75, 0, 0.6)), b + Vector((sgn * 1.4, 0, 0.9)), b + Vector((sgn * 1.6, 0, 1.4))], [0.07, 0.05, 0.02], seg=6)
    kit.use(color=P["leaf2"])
    for i in range(4):
        a = i * math.pi / 2 + 0.6
        kit.ellipsoid((math.cos(a) * 0.55, math.sin(a) * 0.55, H - 0.4), (0.45, 0.45, 0.3), seg=8, rings=5)
    kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
    kit.ellipsoid((0, 0, H + 0.45), (0.38,) * 3, seg=12, rings=8)
    for i in range(3):
        a = i * 2 * math.pi / 3
        kit.ellipsoid((math.cos(a) * 0.9, math.sin(a) * 0.9, H - 1.2 - i * 0.6), (0.1, 0.1, 0.16), seg=6, rings=4)
    return H + 1.2


def wild_den(kit, P):
    """Den of Claws: a mossy boulder mound with a dark cave mouth, glowing claw marks and old bones."""
    kit.use(bone="root", color=P["stone2"], mat="bf_matte", smooth=True)
    kit.ellipsoid((0, 0.4, 1.6), (3.3, 2.8, 2.4), seg=14, rings=8)
    kit.use(color=P["stone"])
    for x, y, r in ((-2.2, -0.6, 1.5), (2.3, -0.4, 1.4), (-1.2, 1.9, 1.3), (1.6, 1.8, 1.2)):
        kit.ellipsoid((x, y, r * 0.8), (r, r * 0.9, r), seg=10, rings=6)
    kit.use(color=P["moss"])
    kit.ellipsoid((0, 0.5, 3.6), (2.4, 2.0, 0.7), seg=12, rings=6)
    kit.use(color=(0.04, 0.03, 0.03))
    kit.ellipsoid((0, -2.25, 1.1), (1.1, 0.6, 1.1), seg=12, rings=7)
    kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
    for sx in (-1.9, 1.9):
        for k in range(3):
            kit.box((sx + k * 0.22 * (1 if sx < 0 else -1), -2.2 + abs(sx) * 0.12, 1.9 - k * 0.1), (0.08, 0.06, 1.0), rot=Euler((0, 0.35, 0)))
    kit.use(color=P["bone"], mat="bf_matte")
    for x, a in ((-0.9, 0.4), (0.8, -0.3), (1.3, 1.2)):
        kit.capsule((x, -2.9, 0.1), (x + math.cos(a) * 0.7, -2.9 + math.sin(a) * 0.4, 0.15), 0.08, seg=6)
    kit.ellipsoid((-0.2, -3.0, 0.2), (0.22, 0.26, 0.2), seg=8, rings=5)
    kit.use(color=P["bark"])
    kit.lathe([(0.35, 3.5), (0.25, 5.2)], center=(0.6, 0.8, 0), seg=8, close_top=False)
    kit.use(color=P["leaf"])
    kit.ellipsoid((0.6, 0.8, 5.8), (1.2, 1.2, 1.0), seg=10, rings=6)
    return 6.5


def wild_lodge(kit, P):
    """Hunting Lodge: a log hall with a leaf-thatch roof, antlers over the door and stretched hides."""
    w, d = 5.0, 4.0
    kit.use(bone="root", color=P["stone2"], mat="bf_matte", smooth=False)
    kit.box((0, 0, 0.2), (w + 0.6, d + 0.6, 0.4), bevel=0.05)
    kit.use(color=P["wood"], smooth=True)
    for row in range(5):
        z = 0.65 + row * 0.55
        for y in (-d / 2, d / 2):
            kit.tube([(-w / 2 - 0.2, y, z), (w / 2 + 0.2, y, z)], [0.28, 0.28], seg=8)
        for x in (-w / 2, w / 2):
            kit.tube([(x, -d / 2 - 0.2, z + 0.27), (x, d / 2 + 0.2, z + 0.27)], [0.26, 0.26], seg=8)
    kit.use(color=P["roof"], smooth=False)
    kit.box((0, 0, 4.0), (w + 0.9, d + 0.9, 1.4), taper=0.35, bevel=0.05)
    kit.use(color=P["leaf"], mat="bf_matte", smooth=True)
    for i in range(5):
        kit.ellipsoid((-w / 2 + 0.5 + i * (w - 1.0) / 4, 0, 4.75), (0.7, 0.9, 0.35), seg=8, rings=5)
    kit.use(color=(0.06, 0.05, 0.04), smooth=False)
    kit.box((0, -d / 2 - 0.1, 1.3), (1.3, 0.2, 2.0), bevel=0.04)
    kit.use(color=P["bone"], smooth=True)
    for sgn in (-1, 1):
        b = Vector((sgn * 0.25, -d / 2 - 0.35, 2.75))
        kit.tube([b, b + Vector((sgn * 0.6, 0, 0.35)), b + Vector((sgn * 0.9, 0, 0.95)), b + Vector((sgn * 0.7, 0, 1.4))], [0.08, 0.07, 0.05, 0.02], seg=6)
        kit.tube([b + Vector((sgn * 0.65, 0, 0.45)), b + Vector((sgn * 1.2, 0, 0.7))], [0.05, 0.02], seg=6)
    kit.use(color=P["cloth"], mat="bf_matte")
    for sx in (-1, 1):
        kit.sheet([(sx * (w / 2 + 0.35), -1.0, 3.0), (sx * (w / 2 + 0.35), 1.0, 3.0)],
                  [(sx * (w / 2 + 0.4), -0.8, 1.0), (sx * (w / 2 + 0.4), 0.8, 1.0)], cols=3, rows=4, bulge=0.1, thick=0.03)
    kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
    for sx in (-1, 1):
        kit.ellipsoid((sx * 1.1, -d / 2 - 0.35, 2.3), (0.14, 0.14, 0.2), seg=8, rings=5)
    return 5.2


def wild_stone_circle(kit, P):
    """Grove of Ancients: a ring of rune-cut standing stones around a young ancient oak."""
    kit.use(bone="root", color=P["moss"], mat="bf_matte", smooth=True)
    kit.lathe([(3.4, 0), (3.3, 0.25), (2.6, 0.35)], seg=16, close_top=True)
    for i in range(8):
        a = i * math.pi / 4
        h = 2.6 + (i % 2) * 0.6
        kit.use(color=P["stone"], smooth=False)
        kit.box((math.cos(a) * 2.9, math.sin(a) * 2.9, h / 2), (0.7, 0.45, h), rot=Euler((0, 0, a)), taper=0.7, bevel=0.06)
        kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
        kit.box((math.cos(a) * 2.62, math.sin(a) * 2.62, h * 0.6), (0.06, 0.28, 0.5), rot=Euler((0, 0, a)))
        kit.use(color=P["moss"], mat="bf_matte")
        kit.ellipsoid((math.cos(a) * 2.9, math.sin(a) * 2.9, h + 0.05), (0.4, 0.3, 0.14), seg=8, rings=4)
    kit.use(color=P["bark"], smooth=True)
    kit.lathe([(0.7, 0.3), (0.5, 2.0), (0.4, 3.4)], seg=10, close_top=True, close_bottom=False)
    for i in range(4):
        a = i * math.pi / 2 + 0.4
        kit.tube([(math.cos(a) * 0.3, math.sin(a) * 0.3, 3.0), (math.cos(a) * 1.2, math.sin(a) * 1.2, 3.9)], [0.2, 0.06], seg=6)
    kit.use(color=P["leaf2"], mat="bf_matte")
    kit.ellipsoid((0, 0, 4.4), (1.9, 1.9, 1.3), seg=12, rings=7)
    kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
    kit.ellipsoid((0, -0.55, 1.4), (0.18, 0.12, 0.3), seg=8, rings=5)
    return 5.7


# ================================================================================================ small objects

def ward(kit, key):
    sentry = "sentry" in key
    kit.use(bone="root", color=(0.3, 0.24, 0.18), mat="bf_matte", smooth=True)
    kit.tube([(0, 0, 0), (0.02, 0, 0.8), (0, 0, 1.4)], [0.05, 0.04, 0.035], seg=6)
    if sentry:
        kit.use(color=(0.4, 0.38, 0.34), mat="bf_metal")
        kit.lathe([(0.12, 1.35), (0.16, 1.45), (0.14, 1.75), (0.05, 1.9)], seg=8, close_top=True)
        kit.use(color=(0.5, 0.8, 1.0), mat=kit.glow((0.5, 0.8, 1.0)))
        kit.ellipsoid((0, 0, 1.6), (0.1, 0.1, 0.13), seg=8, rings=6)
    else:
        kit.use(color=(0.82, 0.78, 0.7))
        kit.ellipsoid((0, 0, 1.65), (0.22,) * 3, seg=12, rings=8)
        kit.use(color=(1.0, 0.8, 0.3), mat=kit.glow((1.0, 0.8, 0.3)))
        kit.ellipsoid((0, -0.17, 1.66), (0.1, 0.08, 0.1), seg=8, rings=6)
        kit.use(color=(0.05, 0.03, 0.02), mat="bf_matte")
        kit.ellipsoid((0, -0.24, 1.66), (0.035, 0.02, 0.06), seg=6, rings=4)
    return 1.9


def seal_crystal(kit, key):
    red = (1.0, 0.15, 0.2)
    kit.use(bone="root", color=red, mat=kit.glow(red), smooth=False)
    kit.lathe([(0.001, 1.8), (0.32, 2.4), (0.22, 3.3), (0.001, 3.6)], seg=6, close_top=False, close_bottom=False)
    for i in range(3):
        a = i * 2 * math.pi / 3
        c = Vector((math.cos(a) * 0.8, math.sin(a) * 0.8, 2.6 + (i % 2) * 0.3))
        kit.lathe([(0.001, -0.2), (0.1, 0), (0.001, 0.25)], center=c, seg=4, close_top=False, close_bottom=False)
    return 3.6


def cauldron(kit, key):
    iron, brew = (0.14, 0.13, 0.13), (0.55, 1.0, 0.3)
    kit.use(bone="root", color=iron, mat="bf_metal", smooth=True)
    for i in range(3):
        a = i * 2 * math.pi / 3
        kit.tube([(math.cos(a) * 0.38, math.sin(a) * 0.38, 0.45), (math.cos(a) * 0.52, math.sin(a) * 0.52, 0.0)], [0.045, 0.035], seg=6)
    kit.lathe([(0.3, 0.22), (0.52, 0.4), (0.58, 0.62), (0.5, 0.88), (0.54, 0.98), (0.47, 0.98)], seg=18, close_bottom=True, close_top=False)
    kit.use(color=brew, mat=kit.glow(brew))
    kit.lathe([(0.47, 0.93), (0.001, 0.95)], seg=18, close_top=False, close_bottom=False)
    for i in range(5):
        a = i * 1.3
        kit.ellipsoid((math.cos(a) * 0.22, math.sin(a) * 0.2, 0.98), (0.05 + (i % 2) * 0.03,) * 3, seg=8, rings=5)
    kit.use(color=(1.0, 0.45, 0.15), mat=kit.glow((1.0, 0.45, 0.15)))
    kit.ellipsoid((0, 0, 0.07), (0.35, 0.35, 0.06), seg=10, rings=4)
    kit.use(color=(0.25, 0.2, 0.18), mat="bf_matte")
    for i in range(6):
        a = i * math.pi / 3
        kit.capsule((math.cos(a) * 0.3, math.sin(a) * 0.3, 0.05), (math.cos(a + 2.5) * 0.3, math.sin(a + 2.5) * 0.3, 0.08), 0.04, seg=6)
    return 1.2


def bloodiron_vein(kit, key):
    """RTS resource node: a scorched rock mound split by glowing blood-iron seams, with a miner's timber frame."""
    rock, rock2, rust = (0.17, 0.14, 0.15), (0.24, 0.2, 0.2), (0.42, 0.17, 0.1)
    kit.use(bone="root", color=rock, mat="bf_matte", smooth=False)
    kit.ellipsoid((0, 0, 0.35), (1.75, 1.55, 0.75), seg=9, rings=5)
    kit.use(color=rock2)
    for i, (a, r, s, h) in enumerate([(0.3, 0.9, 0.8, 0.9), (2.2, 1.0, 0.7, 0.75), (4.0, 0.8, 0.85, 1.0), (5.2, 1.1, 0.6, 0.6)]):
        kit.ellipsoid((math.cos(a) * r, math.sin(a) * r, 0.55 + 0.2 * (i % 2)), (s, s * 0.8, h), seg=7, rings=4,
                      rot=Euler((0.2 * (i - 1.5), 0.15 * i, a)))
    kit.use(color=rust)
    for a in (1.0, 3.1, 4.6):
        kit.ellipsoid((math.cos(a) * 1.25, math.sin(a) * 1.1, 0.45), (0.45, 0.3, 0.2), seg=6, rings=4, rot=Euler((0, 0.3, a)))
    # Blood-iron crystals thrusting out of the rock at different angles.
    red = (1.0, 0.12, 0.14)
    kit.use(color=red, mat=kit.glow(red))
    shards = [((0.1, 0.1, 1.0), (0.0, 0.1, 1.0), 0.32, 1.4), ((0.7, -0.3, 0.8), (0.6, -0.2, 0.8), 0.22, 0.9),
              ((-0.6, 0.5, 0.8), (-0.5, 0.4, 0.8), 0.24, 1.0), ((-0.3, -0.7, 0.6), (-0.2, -0.8, 0.6), 0.18, 0.7),
              ((0.9, 0.6, 0.5), (0.7, 0.6, 0.5), 0.16, 0.6), ((-1.1, -0.2, 0.4), (-0.9, -0.1, 0.5), 0.15, 0.55)]
    for c, axis, r, length in shards:
        kit.lathe([(0.001, -0.1), (r, 0.15), (r * 0.7, length * 0.75), (0.001, length)], center=c, axis=axis, seg=5,
                  close_top=False, close_bottom=False)
    # Timber frame of the mine mouth on the -Y (front) side, with a hanging lantern.
    wood = (0.3, 0.21, 0.14)
    kit.use(color=wood, mat="bf_matte", smooth=False)
    for sx in (-0.75, 0.75):
        kit.box((sx, -1.45, 0.75), (0.16, 0.16, 1.5), bevel=0.02)
    kit.box((0, -1.45, 1.55), (1.8, 0.2, 0.18), bevel=0.02)
    kit.use(color=(0.05, 0.03, 0.03))
    kit.ellipsoid((0, -1.32, 0.6), (0.55, 0.12, 0.6), seg=8, rings=5)
    kit.use(color=(1.0, 0.6, 0.25), mat=kit.glow((1.0, 0.6, 0.25)))
    kit.ellipsoid((0.55, -1.62, 1.3), (0.09, 0.09, 0.12), seg=6, rings=4)
    return 2.4


STATIC = {
    "resource_bloodiron_vein": bloodiron_vein,
    "tower_": tower, "barracks_": barracks, "core_": core, "fountain_": fountain, "ward_": ward,
    "vharoth_seal_active": seal_crystal, "summon_cauldron": cauldron,
}

STATIC_KEYS = ([f"tower_{t}_t{i}" for t in ("dawn", "dusk") for i in (1, 2, 3, 4)]
               + [f"barracks_{t}_{k}" for t in ("dawn", "dusk") for k in ("melee", "ranged")]
               # RTS factions: tier 1 is the defensive tower and tier 4 the elite building.
               + [f"tower_{t}_t{i}" for t in ("crimson", "wild") for i in (1, 4)]
               + [f"barracks_{t}_{k}" for t in ("crimson", "wild") for k in ("melee", "ranged")]
               + ["core_crimson", "core_wild", "fountain_crimson", "fountain_wild"]
               + ["core_dawn", "core_dusk", "fountain_dawn", "fountain_dusk", "ward_watcher", "ward_sentry",
                  "vharoth_seal_active", "summon_cauldron", "resource_bloodiron_vein"])


def build_static(key):
    fn = next(f for prefix, f in STATIC.items() if key.startswith(prefix))
    arm = static_rig(key)
    kit = L.Kit(key)
    top = fn(kit, key)
    mesh = kit.to_object(ao_distance=1.2 if key.startswith(("tower", "core", "barracks", "fountain")) else 0.3)
    L.bind(mesh, arm)
    if key.startswith("tower_"):
        e = L.add_empty("projectile_origin", arm, "root", (0, top - 1.0, 0))  # bone tail is at z=1; offset along the bone (Y)
    return arm, mesh, top


# ================================================================================================ siege (rigged)

def build_siege(key):
    catapult = "catapult" in key
    P = team_palette(key)
    bones = [("root", (0, 0, 0), (0, 0, 0.5), None), ("cart", (0, 0, 0.55), (0, -0.4, 0.55), "root"),
             ("arm", (0, 0.3, 0.9), (0, 0.3, 1.9) if catapult else (0, -0.9, 1.0), "cart")]
    for n, x, y in (("wheel.FL", 0.62, -0.6), ("wheel.FR", -0.62, -0.6), ("wheel.BL", 0.62, 0.6), ("wheel.BR", -0.62, 0.6)):
        bones.append((n, (x, y, 0.35), (x + (0.2 if x > 0 else -0.2), y, 0.35), "cart"))
    arm = L.build_armature(key, bones)
    kit = L.Kit(key)
    wood = P["wood"]
    kit.use(bone="cart", color=wood, mat="bf_matte", smooth=False)
    kit.box((0, 0, 0.6), (1.1, 1.8, 0.28), bevel=0.03)
    kit.use(color=P["trim"], mat="bf_metal")
    for y in (-0.88, 0.88):
        kit.box((0, y, 0.78), (1.15, 0.08, 0.1), bevel=0.01)
    if P["style"] == "grim":
        kit.use(color=P["bone"], mat="bf_matte")
        for i in range(4):
            kit.cone((-0.45 + i * 0.3, 0.9, 0.72), (-0.5 + i * 0.33, 1.1, 1.2), 0.05, seg=5)
    for n, x, y in (("wheel.FL", 0.62, -0.6), ("wheel.FR", -0.62, -0.6), ("wheel.BL", 0.62, 0.6), ("wheel.BR", -0.62, 0.6)):
        kit.use(bone=n, color=L.mul(wood, 0.7), mat="bf_matte")
        kit.lathe([(0.35, -0.06), (0.35, 0.06)], center=(x, y, 0.35), axis=(1, 0, 0), seg=12, close_top=True, close_bottom=True)
        kit.use(color=P["trim"], mat="bf_metal")
        kit.lathe([(0.36, -0.065), (0.37, 0.065)], center=(x, y, 0.35), axis=(1, 0, 0), seg=12, close_top=False, close_bottom=False)
    kit.use(bone="arm", color=wood, mat="bf_matte")
    if catapult:
        kit.box((0, 0.3, 1.4), (0.14, 0.14, 1.1), bevel=0.02)
        kit.lathe([(0.26, -0.1), (0.3, 0.08), (0.001, 0.12)], center=(0, 0.3, 1.95), seg=10, close_bottom=True)
        kit.use(color=P.get("bone", (0.8, 0.75, 0.64)))
        kit.ellipsoid((0, 0.3, 2.05), (0.17,) * 3, seg=10, rings=6)
        kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
        kit.ellipsoid((0, 0.2, 2.1), (0.06,) * 3, seg=6, rings=4)
    else:
        kit.box((0, -0.3, 1.0), (0.16, 1.5, 0.16), bevel=0.02)
        kit.use(color=P["trim"], mat="bf_metal")
        kit.tube([(-0.75, -0.6, 1.0), (0, -0.95, 1.05), (0.75, -0.6, 1.0)], [0.035, 0.06, 0.035], seg=6)
        kit.use(color=P["glow"], mat=kit.glow(P["glow"]))
        kit.box((0, -0.5, 1.12), (0.05, 1.0, 0.05))
    mesh = kit.to_object(ao_distance=0.4)
    L.bind(mesh, arm)
    L.add_empty("projectile_origin", arm, "arm", (0, 0, 0))
    clips = siege_clips(arm, catapult)
    return arm, mesh, 2.2, clips


def siege_clips(arm, catapult):
    clips = []

    def clip(name, frames, keys, cyclic=False):
        c = L.Clip(arm, name, frames)
        for f, pose in keys:
            c.key(f, pose)
        clips.append(c.done(cyclic=cyclic))

    wheels = ("wheel.FL", "wheel.FR", "wheel.BL", "wheel.BR")
    clip("Idle", 30, [(0, {}), (30, {})], cyclic=True)
    # Wheel bones point sideways (+X for L, -X for R): Y rotates around the axle.
    clip("Run", 24, [(0, {w: (0, 0, 0) for w in wheels}), (12, {w: (0, 180 if w.endswith("L") else -180, 0) for w in wheels}),
                     (24, {w: (0, 359 if w.endswith("L") else -359, 0) for w in wheels})], cyclic=True)
    fire_back = {"arm": (-70, 0, 0)} if catapult else {"cart": (4, 0, 0)}
    fire = {"arm": (35, 0, 0)} if catapult else {"cart": (-6, 0, 0), "arm": (-4, 0, 0)}
    for name in ("Attack1", "Attack2"):
        clip(name, 30, [(0, {}), (8, fire_back), (12, fire), (22, {}), (30, {})])
    clip("Death", 40, [(0, {}), (20, {"cart": (0, 25, 10), "wheel.FL": (0, 0, 30)}), (40, {"cart": (0, 40, 20), "wheel.FL": (0, 0, 60), "arm": (40, 0, 0)})])
    arm.animation_data.action = clips[0]
    return clips
