"""
Structures and objects: towers (per team and tier), barracks, cores, fountains, wards, Vharoth's seal crystal, Morwen's
cauldron, and the siege carts (rigged: wheels and throwing arm).

Design language (Docs/ART_DIRECTION.md): Dawn = ivory stone, gold, warm light; Dusk = obsidian, crimson, bone, red glow.
Static models get a single "root" bone so Unity treats every model the same way.
"""
import math

import bf_lib as L
from mathutils import Euler, Vector

DAWN = dict(stone=(0.8, 0.76, 0.66), stone2=(0.66, 0.62, 0.54), trim=(0.86, 0.66, 0.26), roof=(0.3, 0.36, 0.46),
            glow=(1.0, 0.8, 0.4), cloth=(0.9, 0.84, 0.66), wood=(0.45, 0.33, 0.2))
DUSK = dict(stone=(0.2, 0.16, 0.18), stone2=(0.28, 0.22, 0.24), trim=(0.58, 0.07, 0.1), roof=(0.14, 0.1, 0.12),
            glow=(1.0, 0.14, 0.12), cloth=(0.45, 0.04, 0.07), wood=(0.24, 0.18, 0.16), bone=(0.82, 0.77, 0.66))


def team_palette(key):
    return DAWN if "_dawn" in key else DUSK


def static_rig(key):
    return L.build_armature(key, [("root", (0, 0, 0), (0, 0, 1.0), None)])


# ================================================================================================ towers

def tower(kit, key):
    P = team_palette(key)
    tier = int(key[-1])
    dawn = P is DAWN
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
    dawn = P is DAWN
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
    dawn = P is DAWN
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
    dawn = P is DAWN
    kit.use(bone="root", color=P["stone2"], mat="bf_matte", smooth=False)
    kit.lathe([(4.2, 0), (4.3, 0.6), (3.9, 0.7), (3.8, 0.3), (0.5, 0.3)], seg=20, close_top=True)
    water = (0.3, 0.6, 0.95) if dawn else (0.55, 0.02, 0.07)
    kit.use(color=water, mat=kit.glow(water))
    kit.lathe([(3.8, 0.45), (0.1, 0.45)], seg=20, close_top=True, close_bottom=False)
    kit.use(color=P["stone"], mat="bf_matte")
    kit.lathe([(0.9, 0.3), (0.6, 1.6), (1.5, 1.9), (1.4, 2.1), (0.45, 2.2), (0.35, 3.4)], seg=12, close_top=True)
    if dawn:
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


STATIC = {
    "tower_": tower, "barracks_": barracks, "core_": core, "fountain_": fountain, "ward_": ward,
    "vharoth_seal_active": seal_crystal, "summon_cauldron": cauldron,
}

STATIC_KEYS = ([f"tower_{t}_t{i}" for t in ("dawn", "dusk") for i in (1, 2, 3, 4)]
               + [f"barracks_{t}_{k}" for t in ("dawn", "dusk") for k in ("melee", "ranged")]
               + ["core_dawn", "core_dusk", "fountain_dawn", "fountain_dusk", "ward_watcher", "ward_sentry",
                  "vharoth_seal_active", "summon_cauldron"])


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
    if not (P is DAWN):
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
