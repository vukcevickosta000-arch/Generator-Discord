"""
Four-legged creatures (hounds, rats, spirit wolves, the toad hex, wyrms): rig, body builder and clips.

Rig (character faces -Y, all rolls 0):
  root > pelvis > spine > chest > neck > head ; pelvis > tail.1 > tail.2
  chest > upper_front.L/R > lower_front.L/R > paw_front.L/R
  pelvis > thigh_back.L/R > shin_back.L/R > paw_back.L/R
  chest > wing.L/R > wingtip.L/R      (winged creatures)
Rotation conventions (verified in Blender): forward bones (+X tips up), backward bones (+X tips up),
down legs (-X swings forward), side wings (L: +X raises, R: -X raises).
"""
import math

import bf_lib as L
from mathutils import Vector


def mul3(c, k):
    return (min(1, c[0] * k), min(1, c[1] * k), min(1, c[2] * k))


def skeleton(spec):
    h = spec["height"]            # shoulder height
    Ln = spec.get("length", h * 1.4)
    w = spec.get("width", h * 0.22)
    rise = spec.get("neck_rise", h * 0.25)
    head_len = spec.get("head_len", h * 0.32)
    tl = spec.get("tail", h * 0.8)
    s = dict(h=h, L=Ln, w=w, rise=rise, head_len=head_len, tl=tl)
    neck_end = Vector((0, -0.5 * Ln - spec.get("neck_len", 0.0), h + rise))
    s["neck_end"] = neck_end
    bones = [
        ("root", (0, 0, 0), (0, 0, 0.15 * h), None),
        ("pelvis", (0, 0.3 * Ln, h), (0, 0.1 * Ln, h), "root"),
        ("spine", (0, 0.1 * Ln, h), (0, -0.2 * Ln, h * 1.03), "pelvis"),
        ("chest", (0, -0.2 * Ln, h * 1.03), (0, -0.38 * Ln, h * 1.06), "spine"),
        ("neck", (0, -0.38 * Ln, h * 1.06), neck_end[:], "chest"),
        ("head", neck_end[:], (neck_end + Vector((0, -head_len, -0.05 * h)))[:], "neck"),
        ("tail.1", (0, 0.36 * Ln, h * 0.98), (0, 0.36 * Ln + tl * 0.5, h * 0.93), "pelvis"),
        ("tail.2", (0, 0.36 * Ln + tl * 0.5, h * 0.93), (0, 0.36 * Ln + tl, h * 0.8), "tail.1"),
    ]
    for side, sg in (("L", 1), ("R", -1)):
        x = sg * w
        bones += [
            (f"upper_front.{side}", (x, -0.3 * Ln, h * 0.92), (x, -0.3 * Ln, h * 0.48), "chest"),
            (f"lower_front.{side}", (x, -0.3 * Ln, h * 0.48), (x, -0.32 * Ln, h * 0.07), f"upper_front.{side}"),
            (f"paw_front.{side}", (x, -0.32 * Ln, h * 0.07), (x, -0.32 * Ln - 0.12 * h, 0.02 * h), f"lower_front.{side}"),
            (f"thigh_back.{side}", (x, 0.28 * Ln, h * 0.92), (x, 0.2 * Ln, h * 0.52), "pelvis"),
            (f"shin_back.{side}", (x, 0.2 * Ln, h * 0.52), (x, 0.3 * Ln, h * 0.12), f"thigh_back.{side}"),
            (f"paw_back.{side}", (x, 0.3 * Ln, h * 0.12), (x, 0.3 * Ln - 0.12 * h, 0.02 * h), f"shin_back.{side}"),
        ]
        if spec.get("wings"):
            span = spec["wings"]
            root = Vector((x * 0.9, -0.22 * Ln, h * 1.12))
            mid = root + Vector((sg * span * 0.45, 0.06 * Ln, span * 0.12))
            tip = root + Vector((sg * span, 0.18 * Ln, span * 0.02))
            s[f"wing_{side}"] = (root, mid, tip)
            bones += [(f"wing.{side}", root[:], mid[:], "chest"), (f"wingtip.{side}", mid[:], tip[:], f"wing.{side}")]
    return s, bones


def build_body(kit, spec, s):
    h, Ln, w = s["h"], s["L"], s["w"]
    body = spec.get("body", (0.4, 0.38, 0.36))
    belly = spec.get("belly", mul3(body, 1.3))
    accent = spec.get("accent", mul3(body, 0.7))
    glow = spec.get("glow", (1.0, 0.3, 0.2))
    horn = spec.get("horn", (0.85, 0.8, 0.7))
    style = spec.get("head", "wolf")
    bony = spec.get("skeletal", False)
    girth = spec.get("girth", 1.0)
    kit.use(color=body, mat="bf_matte", smooth=True)

    # ---- torso ----------------------------------------------------------------------------------
    if bony:
        kit.use(bone="spine", color=body)
        kit.tube([(0, 0.36 * Ln, h * 0.98), (0, 0.1 * Ln, h * 1.02), (0, -0.2 * Ln, h * 1.05), (0, -0.38 * Ln, h * 1.06)],
                 [0.03 * h, 0.035 * h, 0.035 * h, 0.03 * h], seg=8)
        kit.use(bone="chest")
        for i in range(6):
            y = -0.34 * Ln + i * 0.07 * Ln
            r = 0.2 * h * (1 - abs(i - 2) * 0.12) * girth
            kit.tube([(-w * 1.1, y, h * 1.0), (-w * 1.2, y - 0.02 * Ln, h * 0.8), (0, y - 0.02 * Ln, h * 0.62 + (i * 0.01 * h)),
                      (w * 1.2, y - 0.02 * Ln, h * 0.8), (w * 1.1, y, h * 1.0)], [0.02 * h] * 5, seg=5)
        kit.use(bone="pelvis")
        kit.ellipsoid((0, 0.3 * Ln, h * 0.95), (w * 1.1, 0.12 * Ln, 0.12 * h), seg=12, rings=6)
    else:
        kit.use(bone="pelvis", color=body)
        kit.ellipsoid((0, 0.24 * Ln, h * 0.95), (w * 1.25 * girth, 0.24 * Ln, 0.24 * h * girth), seg=16, rings=9)
        kit.use(bone="spine")
        kit.tube([(0, 0.2 * Ln, h * 0.97), (0, -0.05 * Ln, h * 1.0), (0, -0.26 * Ln, h * 1.03)], [0.22 * h * girth, 0.23 * h * girth, 0.26 * h * girth], seg=16, caps=True)
        kit.use(bone="chest")
        kit.ellipsoid((0, -0.3 * Ln, h * 1.0), (w * 1.35 * girth, 0.2 * Ln, 0.3 * h * girth), seg=16, rings=9)
        kit.use(color=belly)
        kit.ellipsoid((0, -0.28 * Ln, h * 0.82), (w * 0.9 * girth, 0.16 * Ln, 0.16 * h * girth), seg=12, rings=7)
        if spec.get("mane"):
            kit.use(color=spec.get("mane_color", mul3(body, 0.75)))
            for i in range(10):
                a = (i - 4.5) * 0.3
                base = Vector((math.sin(a) * w * 1.1, -0.36 * Ln + abs(a) * 0.05 * Ln, h * 1.1 + math.cos(a) * 0.12 * h))
                kit.cone(base, base + Vector((math.sin(a) * 0.12 * h, 0.14 * h, 0.1 * h)), 0.07 * h, seg=6)

    if spec.get("spikes"):
        kit.use(bone="spine", color=spec.get("spike_color", horn), mat="bf_matte")
        for i in range(7):
            y = 0.3 * Ln - i * 0.1 * Ln
            b = "pelvis" if y > 0.12 * Ln else ("spine" if y > -0.2 * Ln else "chest")
            kit.use(bone=b)
            kit.cone((0, y, h * (1.0 + 0.25 * girth) - (0.02 * h if bony else 0)), (0, y + 0.06 * h, h * (1.0 + 0.25 * girth) + 0.16 * h * (1 - abs(i - 3) * 0.15)), 0.04 * h, seg=5)

    # ---- neck + head ----------------------------------------------------------------------------
    ne = s["neck_end"]
    kit.use(bone="neck", color=body)
    n0 = Vector((0, -0.34 * Ln, h * 1.04))
    kit.tube([n0, n0.lerp(ne, 0.5), ne],
             [0.17 * h * girth * (0.5 if bony else 1), 0.13 * h * (0.5 if bony else 1), 0.11 * h * (0.6 if bony else 1)], seg=12)
    kit.use(bone="head")
    hl = s["head_len"]
    hc = ne + Vector((0, -0.2 * hl, 0.02 * h))
    if style in ("wolf", "hound"):
        kit.ellipsoid(hc, (0.13 * h, 0.17 * h, 0.12 * h), seg=14, rings=8)
        kit.capsule(hc + Vector((0, -0.1 * h, -0.01 * h)), hc + Vector((0, -hl * 0.95, -0.05 * h)), 0.075 * h, 0.045 * h, seg=10)
        kit.use(color=mul3(body, 0.6))
        kit.capsule(hc + Vector((0, -0.08 * h, -0.07 * h)), hc + Vector((0, -hl * 0.8, -0.09 * h)), 0.05 * h, 0.03 * h, seg=8)
        kit.use(color=(0.05, 0.04, 0.04))
        kit.ellipsoid(hc + Vector((0, -hl * 0.98, -0.035 * h)), (0.028 * h,) * 3, seg=8, rings=5)
        kit.use(color=spec.get("ear_color", body))
        for sg in (-1, 1):
            kit.cone(hc + Vector((sg * 0.07 * h, 0.03 * h, 0.07 * h)), hc + Vector((sg * 0.1 * h, 0.08 * h, 0.22 * h)), 0.045 * h, seg=5)
        kit.use(color=(0.95, 0.92, 0.85))
        for sg in (-1, 1):
            kit.cone(hc + Vector((sg * 0.035 * h, -hl * 0.72, -0.07 * h)), hc + Vector((sg * 0.037 * h, -hl * 0.74, -0.13 * h)), 0.012 * h, seg=4)
        if style == "hound" and bony:
            kit.use(color=glow, mat=kit.glow(glow))
            kit.ellipsoid(hc + Vector((0, -0.05 * h, 0.1 * h)), (0.03 * h,) * 3, seg=6, rings=4)
    elif style == "rat":
        kit.ellipsoid(hc, (0.12 * h, 0.15 * h, 0.1 * h), seg=12, rings=8)
        kit.cone(hc + Vector((0, -0.1 * h, 0)), hc + Vector((0, -hl, -0.04 * h)), 0.08 * h, seg=10)
        kit.use(color=spec.get("ear_color", (0.7, 0.45, 0.45)))
        for sg in (-1, 1):
            kit.ellipsoid(hc + Vector((sg * 0.08 * h, 0.03 * h, 0.1 * h)), (0.055 * h, 0.02 * h, 0.06 * h), seg=10, rings=6)
        kit.use(color=(0.9, 0.85, 0.6))
        kit.box(hc + Vector((0, -hl * 0.9, -0.05 * h)), (0.03 * h, 0.01 * h, 0.04 * h), bevel=0.003 * h)
    elif style == "toad":
        kit.ellipsoid(hc + Vector((0, 0.02 * h, -0.02 * h)), (0.3 * h, 0.22 * h, 0.13 * h), seg=16, rings=8)
        kit.use(color=mul3(body, 1.25))
        for sg in (-1, 1):
            kit.ellipsoid(hc + Vector((sg * 0.16 * h, -0.02 * h, 0.1 * h)), (0.08 * h,) * 3, seg=10, rings=6)
        kit.use(color=mul3(body, 0.6))
        kit.lathe([(0.29 * h, -0.004 * h), (0.3 * h, 0.004 * h)], center=hc + Vector((0, -0.08 * h, -0.05 * h)), axis=(0, 0, 1), seg=16, scale=(1.0, 0.6), close_top=False, close_bottom=False)
    elif style == "dragon":
        kit.ellipsoid(hc, (0.12 * h, 0.16 * h, 0.11 * h), seg=14, rings=8)
        kit.capsule(hc + Vector((0, -0.08 * h, 0)), hc + Vector((0, -hl, -0.03 * h)), 0.085 * h, 0.05 * h, seg=10)
        kit.use(color=mul3(body, 0.65))
        kit.capsule(hc + Vector((0, -0.06 * h, -0.08 * h)), hc + Vector((0, -hl * 0.85, -0.1 * h)), 0.06 * h, 0.035 * h, seg=8)
        kit.use(color=horn)
        for sg in (-1, 1):
            b = hc + Vector((sg * 0.07 * h, 0.05 * h, 0.07 * h))
            kit.tube([b, b + Vector((sg * 0.05 * h, 0.15 * h, 0.08 * h)), b + Vector((sg * 0.07 * h, 0.3 * h, 0.05 * h))], [0.035 * h, 0.022 * h, 0.004 * h], seg=6)
    kit.use(color=glow, mat=kit.glow(glow))
    eye_y = -0.1 * h if style != "toad" else -0.04 * h
    eye_z = 0.04 * h if style != "toad" else 0.15 * h
    eye_x = 0.065 * h if style != "toad" else 0.16 * h
    for sg in (-1, 1):
        kit.ellipsoid(hc + Vector((sg * eye_x, eye_y, eye_z)), (0.022 * h, 0.014 * h, 0.018 * h) if style != "toad" else (0.045 * h,) * 3, seg=8, rings=5)

    # ---- legs -----------------------------------------------------------------------------------
    leg_r = spec.get("leg_r", 0.075 * h) * (0.55 if bony else 1)
    for side, sg in (("L", 1), ("R", -1)):
        x = sg * w
        kit.use(bone=f"upper_front.{side}", color=body, mat="bf_matte")
        kit.tube([(x, -0.3 * Ln, h * 1.0), (x, -0.3 * Ln, h * 0.72), (x, -0.3 * Ln, h * 0.48)], [leg_r * 1.5, leg_r * 1.25, leg_r * 0.9], seg=10)
        kit.use(bone=f"lower_front.{side}")
        kit.capsule((x, -0.3 * Ln, h * 0.48), (x, -0.32 * Ln, h * 0.08), leg_r * 0.85, leg_r * 0.65, seg=10)
        kit.use(bone=f"paw_front.{side}", color=spec.get("paw", mul3(body, 0.8)))
        kit.ellipsoid((x, -0.32 * Ln - 0.05 * h, h * 0.045), (leg_r * 1.0, leg_r * 1.4, leg_r * 0.65), seg=10, rings=6)
        if spec.get("claws", True):
            kit.use(color=(0.9, 0.86, 0.78))
            for i in (-1, 0, 1):
                b = Vector((x + i * leg_r * 0.5, -0.32 * Ln - 0.05 * h - leg_r * 1.2, h * 0.04))
                kit.cone(b, b + Vector((0, -leg_r * 0.6, -h * 0.03)), leg_r * 0.2, seg=4)
        kit.use(bone=f"thigh_back.{side}", color=body)
        kit.tube([(x, 0.3 * Ln, h * 1.0), (x, 0.25 * Ln, h * 0.75), (x, 0.2 * Ln, h * 0.52)], [leg_r * 1.9, leg_r * 1.5, leg_r * 0.95], seg=10)
        kit.use(bone=f"shin_back.{side}")
        kit.capsule((x, 0.2 * Ln, h * 0.52), (x, 0.3 * Ln, h * 0.12), leg_r * 0.85, leg_r * 0.6, seg=10)
        kit.use(bone=f"paw_back.{side}", color=spec.get("paw", mul3(body, 0.8)))
        kit.ellipsoid((x, 0.3 * Ln - 0.05 * h, h * 0.045), (leg_r * 1.0, leg_r * 1.4, leg_r * 0.65), seg=10, rings=6)

    # ---- tail -----------------------------------------------------------------------------------
    tl = s["tl"]
    if tl > 0:
        tr = spec.get("tail_r", 0.06 * h) * (0.5 if bony else 1)
        kit.use(bone="tail.1", color=spec.get("tail_color", body), mat="bf_matte")
        p0 = Vector((0, 0.34 * Ln, h * 0.98))
        p1 = Vector((0, 0.36 * Ln + tl * 0.5, h * 0.93))
        kit.tube([p0, p0.lerp(p1, 0.5), p1], [tr, tr * (1.3 if spec.get("bushy") else 0.9), tr * (1.2 if spec.get("bushy") else 0.75)], seg=10)
        kit.use(bone="tail.2")
        p2 = Vector((0, 0.36 * Ln + tl, h * 0.8))
        kit.tube([p1, p1.lerp(p2, 0.5), p2], [tr * (1.2 if spec.get("bushy") else 0.75), tr * (1.0 if spec.get("bushy") else 0.5), tr * 0.15], seg=10)
        if spec.get("tail_blade"):
            kit.use(color=horn)
            kit.blade(p2 - Vector((0, 0.05 * h, 0)), p2 + Vector((0, 0.18 * h, -0.02 * h)), 0.14 * h, 0.02 * h, side=(1, 0, 0))

    # ---- wings ----------------------------------------------------------------------------------
    if spec.get("wings"):
        mem = spec.get("membrane", mul3(body, 0.7))
        for side, sg in (("L", 1), ("R", -1)):
            root, mid, tip = s[f"wing_{side}"]
            kit.use(bone=f"wing.{side}", color=body, mat="bf_matte")
            kit.tube([root, mid], [0.05 * h, 0.035 * h], seg=7)
            kit.use(color=mem)
            kit.sheet([root, mid], [root + Vector((0, 0.3 * s["L"], -0.05 * h)), mid + Vector((0, 0.35 * s["L"], -0.2 * h))], cols=4, rows=3, bulge=0.03 * h, thick=0.012 * h)
            kit.use(bone=f"wingtip.{side}", color=body)
            kit.tube([mid, tip], [0.035 * h, 0.008 * h], seg=6)
            kit.use(color=mem)
            kit.sheet([mid, tip], [mid + Vector((0, 0.35 * s["L"], -0.2 * h)), tip + Vector((0, 0.1 * s["L"], -0.12 * h))], cols=4, rows=3, bulge=0.03 * h, thick=0.012 * h)


def build(spec):
    s, bones = skeleton(spec)
    arm = L.build_armature(spec["key"], bones)
    kit = L.Kit(spec["key"])
    build_body(kit, spec, s)
    mesh = kit.to_object(ao_distance=max(0.2, s["h"] * 0.35))
    L.bind(mesh, arm)
    L.add_empty("projectile_origin", arm, "head", (0, 0, 0))
    s["H"] = s["h"] + s["rise"] + 0.2 * s["h"]
    return arm, mesh, s


def make_clips(arm, spec):
    """Quadruped clip set; winged creatures flap in every clip, flyers hover with legs tucked."""
    h = spec["height"]
    wings = bool(spec.get("wings"))
    flyer = spec.get("flying", False)
    clips = []

    def P(flap=0.0, **over):
        p = {"tail.1": (spec.get("tail_lift", 10), 0, 0), "tail.2": (-8, 0, 0), "neck": (0, 0, 0), "head": (-5, 0, 0)}
        if wings:
            p["wing.L"] = (15 + flap, 0, 0); p["wing.R"] = (-15 - flap, 0, 0)
            p["wingtip.L"] = (-10 + flap * 0.5, 0, 0); p["wingtip.R"] = (10 - flap * 0.5, 0, 0)
        if flyer:
            p["root"] = {"rot": (0, 0, 0), "loc": (0, 0.35 * h, 0)}
            for side in ("L", "R"):
                p[f"upper_front.{side}"] = (-35, 0, 0); p[f"lower_front.{side}"] = (40, 0, 0)
                p[f"thigh_back.{side}"] = (30, 0, 0); p[f"shin_back.{side}"] = (-30, 0, 0)
        for kk, v in over.items():
            p[kk.replace("__", ".")] = v
        return p

    def clip(name, frames, keys, cyclic=False):
        c = L.Clip(arm, name, frames)
        for f, pose in keys:
            c.key(f, pose)
        clips.append(c.done(cyclic=cyclic))

    fl = 45 if wings else 0
    clip("Idle", 40, [(0, P(fl * 0.6 if flyer else 0)), (10, P(-fl * 0.4 if flyer else 0, chest=(2, 0, 0), head=(-8, 0, 6))),
                      (20, P(fl * 0.6 if flyer else 0, **{"tail.1": (14, 0, 12)})), (30, P(-fl * 0.4 if flyer else 0, head=(-4, 0, -6))),
                      (40, P(fl * 0.6 if flyer else 0))], cyclic=True)

    def gallop(phase):
        a = 1 if phase == 0 else -1
        legs = {} if flyer else {
            "upper_front.L": (-40 * a, 0, 0), "upper_front.R": (-30 * a, 0, 0), "lower_front.L": (25 if a < 0 else 5, 0, 0), "lower_front.R": (30 if a < 0 else 5, 0, 0),
            "thigh_back.L": (35 * a, 0, 0), "thigh_back.R": (28 * a, 0, 0), "shin_back.L": (-20 if a > 0 else -5, 0, 0), "shin_back.R": (-25 if a > 0 else -5, 0, 0),
        }
        return P(fl * a, spine=(-6 * a, 0, 0), chest=(4 * a, 0, 0), head=(-5 + 6 * a, 0, 0), **{"tail.1": (15 - 8 * a, 0, 0)}, **{k.replace(".", "__"): v for k, v in legs.items()})

    clip("Run", 16, [(0, gallop(0)), (8, gallop(1)), (16, gallop(0))], cyclic=True)
    clip("Leap", 20, [(0, P(fl, spine=(-10, 0, 0), upper_front__L=(-60, 0, 0), upper_front__R=(-60, 0, 0), thigh_back__L=(50, 0, 0), thigh_back__R=(50, 0, 0))),
                      (10, P(-fl, spine=(5, 0, 0), upper_front__L=(-70, 0, 0), upper_front__R=(-70, 0, 0), thigh_back__L=(40, 0, 0), thigh_back__R=(40, 0, 0))),
                      (20, P(fl, spine=(-10, 0, 0), upper_front__L=(-60, 0, 0), upper_front__R=(-60, 0, 0), thigh_back__L=(50, 0, 0), thigh_back__R=(50, 0, 0)))], cyclic=True)

    def lunge(name, side):
        clip(name, 24, [
            (0, P()),
            (6, P(-fl, spine=(8, 0, 0), neck=(15, 0, 0), head=(10, 0, 0), thigh_back__L=(15, 0, 0), thigh_back__R=(15, 0, 0), **{f"upper_front__{side}": (20, 0, 0)})),
            # Root is an up bone: local +Z points forward (-Y world), local +Y up.
            (10, P(fl, root={"rot": (0, 0, 0), "loc": (0, 0, 0.18 * h)} if not flyer else {"rot": (0, 0, 0), "loc": (0, 0.35 * h, 0.2 * h)},
                   neck=(-20, 0, 0), head=(-18, 0, 0), spine=(-4, 0, 0), **{f"upper_front__{side}": (-65, 0, 0), f"lower_front__{side}": (-20, 0, 0)})),
            (16, P(0, neck=(-8, 0, 0), head=(-8, 0, 0))),
            (24, P()),
        ])

    lunge("Attack1", "L")
    lunge("Attack2", "R")
    howl = P(fl, neck=(35, 0, 0), head=(30, 0, 0), spine=(-8, 0, 0), thigh_back__L=(10, 0, 0), thigh_back__R=(10, 0, 0))
    for name in ("Cast1", "Cast2", "Cast3"):
        clip(name, 30, [(0, P()), (8, P(-fl, neck=(-10, 0, 0), head=(-10, 0, 0))), (12, howl), (22, howl), (30, P())])
    clip("CastUlt", 36, [(0, P()), (10, P(-fl, neck=(-15, 0, 0), spine=(8, 0, 0))), (14, howl), (28, howl), (36, P())])
    clip("Channel", 30, [(0, howl), (15, P(-fl, neck=(30, 0, 0), head=(25, 0, 0))), (30, howl)], cyclic=True)
    clip("Stun", 30, [(0, P(0, neck=(-25, 0, -8), head=(-20, 0, 0))), (15, P(0, neck=(-25, 0, 8), head=(-20, 0, 0))), (30, P(0, neck=(-25, 0, -8), head=(-20, 0, 0)))], cyclic=True)
    down = {"rot": (0, 0, 88), "loc": (0, 0, 0)}   # Z on the up-pointing root rolls the body onto its side
    clip("Death", 40, [(0, P()), (10, P(0, neck=(-30, 0, 0), spine=(10, 0, 0))),
                       (26, P(0, root={"rot": (0, 0, 60), "loc": (0, 0.1 * h, 0)}, neck=(-20, 0, 20), upper_front__L=(-30, 0, 0), thigh_back__L=(30, 0, 0))),
                       (40, P(0, root={**down, "loc": (0, 0.12 * h, 0)}, neck=(-10, 0, 30), head=(0, 0, 20), upper_front__L=(-40, 0, 0), upper_front__R=(-20, 0, 0),
                              thigh_back__L=(40, 0, 0), thigh_back__R=(20, 0, 0), **{"tail.1": (0, 0, 20)}))])
    clip("Spawn", 24, [(0, P(0, root={"rot": (0, 0, 0), "loc": (0, -0.4 * h, 0)} if not flyer else {"rot": (0, 0, 0), "loc": (0, 0.9 * h, 0)},
                             neck=(-30, 0, 0))), (24, P())])
    arm.animation_data.action = clips[0]
    return clips
