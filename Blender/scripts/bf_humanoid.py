"""
Humanoid characters: rig, body builder and the standard clip set.

A character is described by a spec dict (see bf_characters.py). The builder produces one rigid-skinned mesh on a
generic armature:

  root > hips > spine > chest > neck > head
                         chest > upper_arm.L/R > forearm.L/R > hand.L/R (> weapon_r)
                         chest > cape
        hips > thigh.L/R > shin.L/R > foot.L/R

Bone conventions (all rolls 0, character faces -Y):
  * limbs point down: rotating -X swings them forward, +X backward; on .L -Z raises outward, on .R +Z does.
  * spine bones point up: +X leans forward, Y twists, Z bends sideways.
  * shins: +X bends the knee (heel back). forearms: -X bends the elbow (hand forward/up).
Clips put their impact frame at 40% (ClipAnimator.ImpactFraction).
"""
import math

import bf_lib as L
from mathutils import Euler, Vector

SKIN = (0.72, 0.58, 0.5)


def skeleton(spec):
    H = spec["height"]
    k = spec.get("bulk", 1.0)
    hipY, chestY, neckY, headY = 0.52 * H, 0.70 * H, 0.83 * H, 0.865 * H
    shoulderY = 0.805 * H
    sx = 0.13 * H * (0.85 + 0.15 * k)
    upper, fore, hand = 0.175 * H, 0.155 * H, 0.065 * H
    hx = 0.058 * H * (0.9 + 0.1 * k)
    kneeY, ankleY = 0.27 * H, 0.045 * H
    elbow = Vector((sx + 0.018 * H, 0.0, shoulderY - upper))
    wrist = elbow + Vector((0.004 * H, -0.02 * H, -fore))
    s = dict(H=H, k=k, hipY=hipY, chestY=chestY, neckY=neckY, headY=headY, shoulderY=shoulderY, sx=sx, hx=hx,
             kneeY=kneeY, ankleY=ankleY, elbow=elbow, wrist=wrist, hand=hand, upper=upper, fore=fore)
    bones = [
        ("root", (0, 0, 0), (0, 0, 0.12 * H), None),
        ("hips", (0, 0, hipY), (0, 0, hipY + 0.06 * H), "root"),
        ("spine", (0, 0, hipY + 0.06 * H), (0, 0, chestY), "hips"),
        ("chest", (0, 0, chestY), (0, 0, neckY), "spine"),
        ("neck", (0, 0, neckY), (0, 0, headY), "chest"),
        ("head", (0, 0, headY), (0, 0, H), "neck"),
        ("cape", (0, 0.07 * H * k, shoulderY), (0, 0.1 * H * k, hipY), "chest"),
    ]
    for side, sgn in (("L", 1), ("R", -1)):
        sh = Vector((sgn * sx, 0, shoulderY))
        el = Vector((sgn * elbow.x, elbow.y, elbow.z))
        wr = Vector((sgn * wrist.x, wrist.y, wrist.z))
        bones += [
            (f"upper_arm.{side}", sh[:], el[:], "chest"),
            (f"forearm.{side}", el[:], wr[:], f"upper_arm.{side}"),
            (f"hand.{side}", wr[:], (wr + Vector((0, -0.01 * H, -hand)))[:], f"forearm.{side}"),
            (f"thigh.{side}", (sgn * hx, 0, hipY), (sgn * hx, 0, kneeY), "hips"),
            (f"shin.{side}", (sgn * hx, 0, kneeY), (sgn * hx, 0.01 * H, ankleY), f"thigh.{side}"),
            (f"foot.{side}", (sgn * hx, 0.01 * H, ankleY), (sgn * hx, -0.09 * H, 0.01 * H), f"shin.{side}"),
        ]
    bones.append(("weapon_r", (-wrist.x, wrist.y, wrist.z - hand * 0.6), (-wrist.x, wrist.y - 0.12 * H, wrist.z - hand * 0.6), "hand.R"))
    return s, bones


# ================================================================================================ body

def build_body(kit, spec, s):
    H, k = s["H"], s["k"]
    skin = spec.get("skin", SKIN)
    armor = spec.get("armor", (0.35, 0.34, 0.36))
    cloth = spec.get("cloth", (0.3, 0.1, 0.12))
    accent = spec.get("accent", (0.6, 0.5, 0.3))
    glow = spec.get("glow", (1.0, 0.2, 0.2))
    bone_body = spec.get("skeletal", False)
    metal = spec.get("armor_mat", "bf_metal")
    R = lambda f: f * H

    # ---- pelvis / abdomen / chest --------------------------------------------------------------
    hipY, chestY, shoulderY, neckY, headY = s["hipY"], s["chestY"], s["shoulderY"], s["neckY"], s["headY"]
    chestW, chestD = R(0.105) * k, R(0.07) * k
    if bone_body:
        bonec = skin
        kit.use(bone="spine", color=bonec, mat="bf_matte", smooth=True)
        kit.tube([(0, R(0.01), hipY), (0, R(0.02), chestY), (0, R(0.015), neckY)], [R(0.018), R(0.02), R(0.016)], seg=8)
        kit.use(bone="hips")
        kit.ellipsoid((0, 0, hipY + R(0.01)), (R(0.085) * k, R(0.05), R(0.04)), seg=12, rings=6)
        kit.use(bone="chest")
        for i in range(5):
            z = chestY - R(0.03) + i * R(0.028)
            w = chestW * (0.8 + 0.2 * math.sin(i / 4 * math.pi))
            kit.tube([(-w, R(0.01), z), (-w * 0.7, -chestD * 0.9, z - R(0.01)), (0, -chestD * 1.05, z - R(0.015)), (w * 0.7, -chestD * 0.9, z - R(0.01)), (w, R(0.01), z)],
                     [R(0.008)] * 5, seg=6, caps=True)
        kit.tube([(-chestW * 1.05, 0, shoulderY - R(0.005)), (chestW * 1.05, 0, shoulderY - R(0.005))], [R(0.012), R(0.012)], seg=6)
    else:
        kit.use(bone="hips", color=spec.get("belt", mul3(cloth, 0.8)), mat="bf_matte", smooth=True)
        kit.ellipsoid((0, R(0.005), hipY + R(0.01)), (R(0.1) * k, R(0.07) * k, R(0.06)), seg=16, rings=8)
        kit.use(bone="spine", color=cloth)
        kit.capsule((0, R(0.005), hipY + R(0.04)), (0, 0, chestY - R(0.02)), R(0.085) * k, R(0.095) * k, seg=16)
        kit.use(bone="chest", color=spec.get("torso", armor if not spec.get("robe") else cloth),
                mat=metal if spec.get("cuirass", not spec.get("robe")) else "bf_matte")
        # Chest: a lathe with a proud breastplate.
        kit.lathe([(chestW * 0.9, chestY - R(0.04)), (chestW * 1.08, chestY + R(0.02)), (chestW * 1.18, chestY + R(0.07)),
                   (chestW * 1.1, shoulderY - R(0.01)), (chestW * 0.55, shoulderY + R(0.025))],
                  center=(0, 0, 0), seg=18, scale=(1.0, chestD / chestW))
        if spec.get("cuirass", not spec.get("robe")):
            kit.use(color=accent, mat=metal)
            kit.box((0, -chestD * 1.08, chestY + R(0.06)), (chestW * 0.9, R(0.012), R(0.1)), bevel=R(0.004), taper=0.7)
            kit.use(color=glow, mat=kit.glow(glow))
            kit.ellipsoid((0, -chestD * 1.15, chestY + R(0.07)), (R(0.012),) * 3, seg=8, rings=5)
        # Belt.
        kit.use(bone="hips", color=spec.get("belt", mul3(armor, 0.7)), mat=metal)
        kit.lathe([(R(0.098) * k, hipY + R(0.03)), (R(0.1) * k, hipY + R(0.055))], seg=18, scale=(1.0, 0.75))
        kit.use(color=accent)
        kit.box((0, -R(0.075) * k, hipY + R(0.043)), (R(0.04), R(0.012), R(0.03)), bevel=R(0.003))
        if spec.get("tabard"):
            tab = spec["tabard"]
            kit.use(bone="hips", color=tab, mat="bf_matte", smooth=True)
            for yb, sgn in ((-R(0.082) * k, -1), (R(0.075) * k, 1)):
                kit.sheet([(-R(0.05) * k, yb, hipY + R(0.03)), (R(0.05) * k, yb, hipY + R(0.03))],
                          [(-R(0.055) * k, yb + sgn * R(0.02), hipY - R(0.2)), (R(0.055) * k, yb + sgn * R(0.02), hipY - R(0.2))],
                          cols=3, rows=4, bulge=sgn * R(0.004), thick=R(0.005))
            kit.use(color=spec.get("trim", accent), mat=metal)
            kit.box((0, -R(0.094) * k, hipY - R(0.2)), (R(0.115) * k, R(0.008), R(0.012)), bevel=R(0.002))

    # ---- skirts / robes ------------------------------------------------------------------------
    if spec.get("robe"):
        kit.use(bone="hips", color=cloth, mat="bf_matte", smooth=True)
        kr = k ** 0.5
        kit.lathe([(R(0.1) * k, hipY + R(0.04)), (R(0.115) * kr, hipY - R(0.08)), (R(0.14) * kr, R(0.2)), (R(0.165) * kr, R(0.03)),
                   (R(0.16) * kr, R(0.01))], seg=20, scale=(1.0, 0.85), close_top=False)
        kit.use(color=accent)
        kit.lathe([(R(0.165) * kr, R(0.03)), (R(0.167) * kr, R(0.05))], seg=20, scale=(1.0, 0.85), close_top=False, close_bottom=False)
    elif spec.get("skirt", True) and not bone_body:
        kit.use(bone="hips", color=spec.get("skirt_color", mul3(cloth, 0.9)), mat="bf_matte", smooth=True)
        for side in (-1, 1):
            kit.sheet([(side * R(0.02), -R(0.078) * k, hipY + R(0.02)), (side * R(0.1) * k, -R(0.06) * k, hipY + R(0.02))],
                      [(side * R(0.025), -R(0.1) * k, hipY - R(0.17)), (side * R(0.13) * k, -R(0.07) * k, hipY - R(0.15))],
                      cols=3, rows=3, bulge=R(0.01), thick=R(0.006))
        kit.sheet([(-R(0.09) * k, R(0.07) * k, hipY + R(0.02)), (R(0.09) * k, R(0.07) * k, hipY + R(0.02))],
                  [(-R(0.11) * k, R(0.1) * k, hipY - R(0.18)), (R(0.11) * k, R(0.1) * k, hipY - R(0.18))],
                  cols=4, rows=3, bulge=-R(0.01), thick=R(0.006))

    # ---- cape ----------------------------------------------------------------------------------
    if spec.get("cape"):
        kit.use(bone="cape", color=spec.get("cape_color", cloth), mat="bf_matte", smooth=True)
        kit.sheet([(-chestW * 0.95, chestD * 0.9, shoulderY), (chestW * 0.95, chestD * 0.9, shoulderY)],
                  [(-chestW * 1.5, chestD * 1.6 + R(0.03), R(0.1)), (chestW * 1.5, chestD * 1.6 + R(0.03), R(0.1))],
                  cols=8, rows=8, bulge=-R(0.025), wave=R(0.012), thick=R(0.008))

    # ---- neck / head ---------------------------------------------------------------------------
    hr = R(0.052) * (1 + (k - 1) * 0.25) * spec.get("head_scale", 1.0)
    hc = Vector((0, -R(0.005), headY + hr * 1.05))
    kit.use(bone="neck", color=skin, mat="bf_matte", smooth=True)
    kit.capsule((0, 0, neckY - R(0.01)), (0, -R(0.004), headY + R(0.01)), R(0.024) * k, R(0.022) * k, seg=10)
    kit.use(bone="head", color=skin)
    if spec.get("muzzle"):
        kit.ellipsoid(hc, (hr * 0.95, hr * 1.0, hr * 0.95), seg=16, rings=10)
        kit.capsule(hc + Vector((0, -hr * 0.6, -hr * 0.1)), hc + Vector((0, -hr * 2.0, -hr * 0.35)), hr * 0.5, hr * 0.28, seg=10)
        kit.use(color=mul3(skin, 0.6))
        kit.capsule(hc + Vector((0, -hr * 0.6, -hr * 0.55)), hc + Vector((0, -hr * 1.7, -hr * 0.6)), hr * 0.3, hr * 0.18, seg=8)
        kit.use(color=(0.05, 0.04, 0.04))
        kit.ellipsoid(hc + Vector((0, -hr * 2.05, -hr * 0.3)), (hr * 0.13,) * 3, seg=8, rings=5)
        kit.use(color=(0.95, 0.92, 0.85))
        for sgn in (-1, 1):
            kit.cone(hc + Vector((sgn * hr * 0.2, -hr * 1.6, -hr * 0.45)), hc + Vector((sgn * hr * 0.22, -hr * 1.62, -hr * 0.8)), hr * 0.06, seg=5)
    else:
        kit.ellipsoid(hc, (hr * 0.86, hr * 0.95, hr * 1.08), seg=16, rings=10)
        kit.ellipsoid(hc + Vector((0, -hr * 0.35, -hr * 0.55)), (hr * 0.6, hr * 0.62, hr * 0.5), seg=12, rings=7)  # jaw
        kit.cone(hc + Vector((0, -hr * 0.82, -hr * 0.05)), hc + Vector((0, -hr * 1.04, -hr * 0.2)), hr * 0.12, seg=6)  # nose
        kit.use(color=mul3(skin, 0.8))
        kit.box(hc + Vector((0, -hr * 0.78, hr * 0.22)), (hr * 1.1, hr * 0.14, hr * 0.14), bevel=hr * 0.04, smooth=True)  # brow
    kit.use(color=glow, mat=kit.glow(glow))
    for sgn in (-1, 1):
        kit.ellipsoid(hc + Vector((sgn * hr * 0.32, -hr * (0.78 if not spec.get("muzzle") else 0.75), hr * 0.08)), (hr * 0.1, hr * 0.06, hr * 0.07), seg=8, rings=5)
    if spec.get("hair"):
        kit.use(color=spec["hair"], mat="bf_matte")
        kit.ellipsoid(hc + Vector((0, hr * 0.12, hr * 0.12)), (hr * 0.93, hr * 0.95, hr * 1.02), seg=16, rings=9)
        if spec.get("long_hair"):
            kit.sheet([(-hr * 0.8, hr * 0.5, hc.z + hr * 0.3), (hr * 0.8, hr * 0.5, hc.z + hr * 0.3)],
                      [(-hr * 1.1, hr * 0.9, hc.z - hr * 2.4), (hr * 1.1, hr * 0.9, hc.z - hr * 2.4)], cols=5, rows=5, bulge=hr * 0.3, thick=hr * 0.1)
    head_features(kit, spec, hc, hr, H, armor, accent, glow, cloth, skin)

    # ---- arms ----------------------------------------------------------------------------------
    for side, sgn in (("L", 1), ("R", -1)):
        sh = Vector((sgn * s["sx"], 0, shoulderY))
        el = Vector((sgn * s["elbow"].x, s["elbow"].y, s["elbow"].z))
        wr = Vector((sgn * s["wrist"].x, s["wrist"].y, s["wrist"].z))
        arm_r = R(0.03) * k
        sleeve = skin if bone_body else (cloth if spec.get("robe") else spec.get("sleeve", skin))
        kit.use(bone=f"upper_arm.{side}", color=sleeve, mat="bf_matte", smooth=True)
        if bone_body:
            kit.capsule(sh, el, arm_r * 0.55, arm_r * 0.5, seg=8)
        else:
            kit.tube([sh + (sh - el).normalized() * arm_r * 0.5, sh, sh.lerp(el, 0.4), el], [arm_r * 0.7, arm_r * 1.1, arm_r * 1.12, arm_r * 0.82], seg=12)
        kit.use(bone=f"forearm.{side}")
        if bone_body:
            kit.capsule(el, wr, arm_r * 0.45, arm_r * 0.4, seg=8)
        else:
            kit.tube([el, el.lerp(wr, 0.3), wr, wr + (wr - el).normalized() * arm_r * 0.3], [arm_r * 0.85, arm_r * 0.95, arm_r * 0.68, arm_r * 0.55], seg=12)
        if not bone_body and not spec.get("robe"):
            kit.use(color=spec.get("bracer", armor), mat=metal)
            kit.lathe([(arm_r * 0.78, -arm_r * 0.2), (arm_r * 0.9, 0), (arm_r * 1.02, (el - wr).length * 0.45), (arm_r * 1.22, (el - wr).length * 0.62)],
                      center=wr, axis=(el - wr), seg=12, close_top=False, close_bottom=False)
        if spec.get("robe"):
            kit.use(color=cloth, mat="bf_matte")
            kit.lathe([(arm_r * 1.8, 0), (arm_r * 1.1, (el - wr).length * 0.9)], center=wr + (wr - el).normalized() * R(0.01), axis=(el - wr), seg=12, close_top=False, close_bottom=False)
        kit.use(bone=f"hand.{side}", color=skin if not spec.get("gauntlets") else armor, mat="bf_matte" if not spec.get("gauntlets") else metal)
        kit.ellipsoid(wr + Vector((0, -R(0.004), -s["hand"] * 0.5)), (arm_r * 0.75, arm_r * 0.6, s["hand"] * 0.55), seg=10, rings=6)
        if spec.get("claws_hands"):
            kit.use(color=(0.92, 0.88, 0.8), mat="bf_matte")
            for i in (-1, 0, 1):
                base = wr + Vector((i * arm_r * 0.35, -arm_r * 0.3, -s["hand"] * 0.9))
                kit.cone(base, base + Vector((i * arm_r * 0.1, -arm_r * 0.6, -s["hand"] * 0.7)), arm_r * 0.14, seg=5)
        # Team armband (tinted to the team colour in Unity).
        if spec.get("team_band") and side == "L":
            kit.use(bone=f"upper_arm.{side}", color=(1, 1, 1), mat="bf_team")
            band = sh.lerp(el, 0.55)
            kit.lathe([(arm_r * 1.14, -R(0.012)), (arm_r * 1.16, R(0.012))], center=band, axis=(el - sh), seg=12, close_top=False, close_bottom=False)
        # Pauldrons.
        if spec.get("pauldrons", not spec.get("robe") and not bone_body):
            kit.use(bone=f"upper_arm.{side}", color=spec.get("pauldron", armor), mat=metal)
            pc = sh + Vector((sgn * R(0.012), 0, R(0.005)))
            tilt = Euler((0, math.radians(sgn * 25), 0))
            kit.ellipsoid(pc, (R(0.052) * k, R(0.052) * k, R(0.036) * k), seg=16, rings=8, rot=tilt)
            kit.ellipsoid(pc + Vector((sgn * R(0.018) * k, 0, -R(0.028) * k)), (R(0.046) * k, R(0.048) * k, R(0.022) * k), seg=16, rings=6, rot=tilt)
            kit.use(color=spec.get("trim", accent))
            kit.lathe([(R(0.047) * k, -R(0.003)), (R(0.05) * k, R(0.003))], center=pc + Vector((sgn * R(0.024) * k, 0, -R(0.036) * k)),
                      axis=tilt.to_matrix() @ Vector((0, 0, 1)), seg=16, close_top=False, close_bottom=False)
            if spec.get("spiked"):
                kit.use(color=accent)
                kit.spikes(sh + Vector((sgn * R(0.02), 0, R(0.03))), (sgn * 0.4, 0, 1), R(0.025), 3, R(0.05), r=R(0.008))

    # ---- legs ----------------------------------------------------------------------------------
    for side, sgn in (("L", 1), ("R", -1)):
        hip = Vector((sgn * s["hx"], 0, hipY))
        kn = Vector((sgn * s["hx"], 0, s["kneeY"]))
        an = Vector((sgn * s["hx"], R(0.01), s["ankleY"]))
        leg_r = R(0.043) * k
        legc = skin if bone_body else spec.get("legs", mul3(cloth, 0.75))
        kit.use(bone=f"thigh.{side}", color=legc, mat="bf_matte", smooth=True)
        if bone_body:
            kit.capsule(hip, kn, leg_r * 0.42, leg_r * 0.38, seg=8)
        else:
            kit.tube([hip + Vector((0, 0, leg_r * 0.6)), hip, hip.lerp(kn, 0.45), kn], [leg_r * 0.9, leg_r * 1.18, leg_r * 1.02, leg_r * 0.76], seg=14)
        kit.use(bone=f"shin.{side}")
        if bone_body:
            kit.capsule(kn, an, leg_r * 0.36, leg_r * 0.32, seg=8)
        else:
            kit.tube([kn, kn.lerp(an, 0.3), an], [leg_r * 0.74, leg_r * 0.8, leg_r * 0.5], seg=14)
        if not bone_body:
            kit.use(color=spec.get("boots", mul3(armor, 0.8)), mat=metal if spec.get("greaves", True) else "bf_matte")
            ln = (kn - an).length
            kit.lathe([(leg_r * 0.6, -R(0.01)), (leg_r * 0.72, ln * 0.2), (leg_r * 0.86, ln * 0.55), (leg_r * 0.95, ln * 0.7), (leg_r * 1.05, ln * 0.74)],
                      center=an, axis=(kn - an), seg=14, close_top=False, close_bottom=False, scale=(1.0, 1.08))
            kit.use(color=spec.get("knee", armor), mat=metal)
            kit.ellipsoid(kn + Vector((0, -leg_r * 0.55, 0)), (leg_r * 0.6, leg_r * 0.4, leg_r * 0.55), seg=10, rings=6)
        kit.use(bone=f"foot.{side}", color=spec.get("boots", mul3(armor, 0.8)) if not bone_body else skin, mat="bf_matte", smooth=True)
        foot = Vector((sgn * s["hx"], -R(0.03), R(0.03)))
        kit.ellipsoid(foot, (leg_r * 0.62, R(0.075), R(0.035)), seg=12, rings=6)
        kit.use(color=mul3(spec.get("boots", mul3(armor, 0.8)), 0.55))
        kit.box(foot + Vector((0, 0, -R(0.022))), (leg_r * 1.2, R(0.15), R(0.014)), bevel=R(0.005), smooth=True)

    # ---- beast belly / bark / leaves ------------------------------------------------------------
    if spec.get("belly"):
        kit.use(bone="chest", color=spec["belly"], mat="bf_matte", smooth=True)
        kit.ellipsoid((0, -chestD * 0.75, chestY + R(0.03)), (chestW * 0.75, chestD * 0.45, R(0.1)), seg=14, rings=8)
        kit.use(bone="spine")
        kit.ellipsoid((0, -R(0.055) * k, hipY + R(0.08)), (R(0.07) * k, R(0.04) * k, R(0.08)), seg=12, rings=7)
    if spec.get("bark_ridges"):
        kit.use(bone="chest", color=mul3(skin, 0.7), mat="bf_matte", smooth=True)
        for i in range(7):
            a = (i / 7) * 2 * math.pi
            x, y = math.cos(a) * chestW * 1.02, math.sin(a) * chestD * 1.02
            kit.tube([(x, y, chestY - R(0.05)), (x * 1.08, y * 1.08, chestY + R(0.05)), (x, y, shoulderY)], [R(0.012), R(0.016), R(0.01)], seg=5)
    if spec.get("leaves"):
        leaf = spec["leaves"]
        kit.use(bone="chest", color=leaf, mat="bf_matte", smooth=True)
        for sgn in (-1, 1):
            for j, (dx, dy, dz, r) in enumerate(((1.0, 0.2, 0.02, 0.06), (0.7, 0.5, 0.05, 0.05), (1.2, -0.2, -0.01, 0.045))):
                kit.ellipsoid((sgn * chestW * dx, chestD * dy, shoulderY + R(dz)), (R(r) * k, R(r) * k * 0.9, R(r) * k * 0.8), seg=10, rings=6)
        kit.use(bone="head")
        for j in range(5):
            a = j * 2 * math.pi / 5
            kit.ellipsoid(hc + Vector((math.cos(a) * hr * 0.9, math.sin(a) * hr * 0.9 + hr * 0.3, hr * 1.0)), (hr * 0.55,) * 3, seg=10, rings=6)
        kit.use(color=mul3(leaf, 1.4))
        for j in range(4):
            a = j * 1.7
            kit.ellipsoid((math.cos(a) * chestW * 0.6, chestD * 0.9 + math.sin(a) * R(0.02), chestY + R(0.02) + j * R(0.025)), (R(0.03),) * 3, seg=8, rings=5)

    # ---- mantle / collar / halo on the torso ----------------------------------------------------
    if spec.get("mane"):
        kit.use(bone="chest", color=spec.get("fur", cloth), mat="bf_matte", smooth=True)
        # A fur mantle hugging the shoulders and upper back, open at the front so the head stays clear.
        kit.ellipsoid((0, chestD * 0.55, shoulderY - R(0.01)), (chestW * 1.18, chestD * 1.05, R(0.045)), seg=18, rings=8)
        for i in range(9):
            a = (i - 4) * 0.36
            base = Vector((math.sin(a) * chestW * 1.05, chestD * 0.55 + math.cos(a) * chestD * 0.8, shoulderY - R(0.02)))
            kit.cone(base, base + Vector((math.sin(a) * R(0.025), math.cos(a) * R(0.04) + R(0.015), -R(0.09))), R(0.022), seg=5)
    if spec.get("collar"):
        kit.use(bone="chest", color=accent, mat=metal)
        for i in range(7):
            a = (i - 3) * 0.35
            base = Vector((math.sin(a) * chestW * 0.75, chestD * 0.7, shoulderY + R(0.01)))
            kit.cone(base, base + Vector((math.sin(a) * R(0.05), R(0.025), R(0.1 if abs(i - 3) < 2 else 0.07))), R(0.014), seg=4)
    if spec.get("halo"):
        kit.use(bone="chest", color=glow, mat=kit.glow(glow))
        c = Vector((0, chestD * 1.5, shoulderY + R(0.07)))
        kit.lathe([(R(0.11), -R(0.004)), (R(0.12), 0), (R(0.11), R(0.004))], center=c, axis=(0, 1, 0), seg=24, close_top=False, close_bottom=False)
        kit.spikes(c, (0, 1, 0), R(0.12), 12, [R(0.01)], r=R(0.008), spread=0)
        for i in range(12):
            a = i * math.pi / 6
            base = c + Vector((math.cos(a), 0, math.sin(a))) * R(0.12)
            kit.cone(base, c + Vector((math.cos(a), 0, math.sin(a))) * (R(0.22) if i % 2 == 0 else R(0.17)), R(0.012), seg=4)
    if spec.get("wings"):
        kit.use(bone="chest", color=spec.get("wing_color", mul3(skin, 0.6)), mat="bf_matte", smooth=True)
        for sgn in (-1, 1):
            root = Vector((sgn * chestW * 0.5, chestD * 1.0, shoulderY - R(0.02)))
            mid = root + Vector((sgn * R(0.28), R(0.1), R(0.14)))
            tip = root + Vector((sgn * R(0.55), R(0.12), R(0.02)))
            kit.tube([root, mid, tip], [R(0.012), R(0.009), R(0.003)], seg=6)
            kit.sheet([root, mid], [root + Vector((sgn * R(0.05), R(0.08), -R(0.25))), tip + Vector((0, 0, -R(0.08)))], cols=4, rows=4, bulge=R(0.02), thick=R(0.004))


def head_features(kit, spec, hc, hr, H, armor, accent, glow, cloth, skin):
    R = lambda f: f * H
    top = hc.z + hr * 1.08
    if spec.get("helmet"):
        kit.use(bone="head", color=spec.get("helm_color", armor), mat="bf_metal", smooth=True)
        kit.ellipsoid(hc + Vector((0, hr * 0.02, hr * 0.12)), (hr * 1.02, hr * 1.08, hr * 1.12), seg=16, rings=9)
        kit.use(color=mul3(armor, 0.4))
        kit.box(hc + Vector((0, -hr * 0.95, hr * 0.05)), (hr * 1.2, hr * 0.12, hr * 0.12), bevel=hr * 0.03)
        kit.use(color=spec.get("trim", accent))
        kit.lathe([(hr * 1.04, -hr * 0.04), (hr * 1.06, hr * 0.06)], center=hc + Vector((0, hr * 0.02, -hr * 0.28)), seg=16, scale=(1.0, 1.05), close_top=False, close_bottom=False)
        kit.box(hc + Vector((0, -hr * 0.98, hr * 0.5)), (hr * 0.14, hr * 0.12, hr * 0.8), bevel=hr * 0.03, taper=0.5)
        if spec.get("crest"):
            kit.use(color=accent)
            kit.box(hc + Vector((0, hr * 0.1, hr * 1.2)), (hr * 0.12, hr * 1.6, hr * 0.5), bevel=hr * 0.03, taper=0.6)
    if spec.get("horns"):
        kit.use(bone="head", color=spec.get("horn_color", (0.86, 0.8, 0.7)), mat="bf_matte", smooth=True)
        for sgn in (-1, 1):
            b = hc + Vector((sgn * hr * 0.7, 0, hr * 0.55))
            kit.tube([b, b + Vector((sgn * hr * 0.7, hr * 0.2, hr * 0.6)), b + Vector((sgn * hr * 1.1, hr * 0.45, hr * 1.4)), b + Vector((sgn * hr * 0.9, hr * 0.3, hr * 2.0))],
                     [hr * 0.24, hr * 0.18, hr * 0.1, hr * 0.02], seg=8)
    if spec.get("crown"):
        kit.use(bone="head", color=spec.get("crown_color", accent), mat="bf_metal", smooth=False)
        kit.lathe([(hr * 0.86, 0), (hr * 0.9, hr * 0.18)], center=(0, 0, top - hr * 0.42), seg=16, close_top=False, close_bottom=False)
        kit.spikes((0, 0, top - hr * 0.26), (0, 0, 1), hr * 0.88, 7, [hr * 0.55, hr * 0.35], r=hr * 0.08)
        kit.use(color=glow, mat=kit.glow(glow))
        kit.ellipsoid((0, -hr * 0.9, top - hr * 0.33), (hr * 0.1,) * 3, seg=8, rings=5)
    if spec.get("hood"):
        kit.use(bone="head", color=mul3(cloth, 0.85), mat="bf_matte", smooth=True)
        kit.lathe([(hr * 1.25, -hr * 1.3), (hr * 1.2, -hr * 0.2), (hr * 1.0, hr * 0.8), (hr * 0.3, hr * 1.7), (0.001, hr * 1.9)],
                  center=hc + Vector((0, hr * 0.2, 0)), seg=16, scale=(1.0, 1.05), close_bottom=False)
    if spec.get("hat"):
        kit.use(bone="head", color=mul3(cloth, 0.75), mat="bf_matte", smooth=True)
        kit.lathe([(hr * 2.5, 0), (hr * 2.4, hr * 0.12), (hr * 1.05, hr * 0.2)], center=(0, 0, top - hr * 0.35), seg=24, close_top=False)
        kit.tube([(0, 0, top - hr * 0.2), (0, hr * 0.1, top + hr * 1.0), (hr * 0.15, hr * 0.5, top + hr * 1.9), (hr * 0.5, hr * 1.0, top + hr * 2.3)],
                 [hr * 1.02, hr * 0.7, hr * 0.35, hr * 0.05], seg=14, caps=True)
        kit.use(color=accent, mat="bf_metal")
        kit.lathe([(hr * 1.04, 0), (hr * 1.0, hr * 0.22)], center=(0, 0, top - hr * 0.2), seg=18, close_top=False, close_bottom=False)
    if spec.get("ears"):
        kit.use(bone="head", color=skin, mat="bf_matte", smooth=True)
        for sgn in (-1, 1):
            kit.cone(hc + Vector((sgn * hr * 0.55, hr * 0.1, hr * 0.8)), hc + Vector((sgn * hr * 0.8, hr * 0.2, hr * 1.9)), hr * 0.28, seg=6)
    if spec.get("antlers"):
        kit.use(bone="head", color=spec.get("antler_color", mul3(skin, 0.8)), mat="bf_matte", smooth=True)
        for sgn in (-1, 1):
            r0 = hc + Vector((sgn * hr * 0.55, 0, hr * 0.75))
            r1 = r0 + Vector((sgn * hr * 1.4, hr * 0.1, hr * 1.6))
            r2 = r1 + Vector((sgn * hr * 1.0, hr * 0.05, hr * 1.5))
            kit.tube([r0, r1, r2], [hr * 0.17, hr * 0.12, hr * 0.04], seg=7)
            kit.tube([r1, r1 + Vector((sgn * hr * 0.2, -hr * 0.4, hr * 1.2))], [hr * 0.1, hr * 0.03], seg=6)
            mid = r0.lerp(r1, 0.5)
            kit.tube([mid, mid + Vector((-sgn * hr * 0.1, -hr * 0.3, hr * 1.0))], [hr * 0.09, hr * 0.03], seg=6)
            kit.use(color=glow, mat=kit.glow(glow))
            kit.ellipsoid(r2, (hr * 0.1,) * 3, seg=8, rings=5)
            kit.use(color=spec.get("antler_color", mul3(skin, 0.8)), mat="bf_matte")
    if spec.get("tiara"):
        kit.use(bone="head", color=spec.get("crown_color", accent), mat="bf_metal", smooth=False)
        kit.spikes((0, -hr * 0.2, top - hr * 0.1), (0, -0.3, 1), hr * 0.6, 5, [hr * 0.35, hr * 0.5, hr * 0.8, hr * 0.5, hr * 0.35], r=hr * 0.07, phase=math.radians(-150))


def mul3(c, k):
    return (min(1, c[0] * k), min(1, c[1] * k), min(1, c[2] * k))


# ================================================================================================ weapons

def build_weapon(kit, spec, s):
    w = spec.get("weapon")
    if not w:
        return
    H = s["H"]
    R = lambda f: f * H
    grip = Vector((-s["wrist"].x, s["wrist"].y - R(0.005), s["wrist"].z - s["hand"] * 0.55))
    steel = spec.get("steel", (0.66, 0.66, 0.7))
    accent = spec.get("accent", (0.6, 0.5, 0.3))
    glow = spec.get("glow", (1, 0.2, 0.2))
    wood = spec.get("wood", (0.28, 0.18, 0.12))
    kit.use(bone="weapon_r")
    fwd = Vector((0, -1, 0))
    if w in ("sword", "greatsword", "rapier"):
        L_ = {"sword": 0.42, "greatsword": 0.72, "rapier": 0.55}[w] * H
        width = {"sword": 0.035, "greatsword": 0.06, "rapier": 0.012}[w] * H
        kit.use(color=wood, mat="bf_matte", smooth=True)
        kit.capsule(grip + fwd * R(-0.05), grip + fwd * R(0.03), R(0.011), seg=8)
        kit.use(color=accent, mat="bf_metal")
        if w == "rapier":
            kit.ellipsoid(grip + fwd * R(0.045), (R(0.03), R(0.018), R(0.03)), seg=12, rings=6)
        else:
            kit.box(grip + fwd * R(0.04), (width * 3.2, R(0.012), R(0.014)), bevel=R(0.003))
        kit.use(color=steel, mat="bf_metal")
        kit.blade(grip + fwd * R(0.045), grip + fwd * (R(0.045) + L_), width, width * 0.25, side=(1, 0, 0))
        if w == "greatsword":
            kit.use(color=glow, mat=kit.glow(glow))
            kit.blade(grip + fwd * R(0.06) + Vector((0, 0, width * 0.13)), grip + fwd * (R(0.02) + L_ * 0.9) + Vector((0, 0, width * 0.13)), width * 0.18, width * 0.03)
    elif w in ("hammer", "mace", "club"):
        shaft = {"hammer": 0.5, "mace": 0.36, "club": 0.5}[w] * H
        kit.use(color=wood, mat="bf_matte", smooth=True)
        r0, r1 = (R(0.012), R(0.012)) if w != "club" else (R(0.016), R(0.035))
        kit.capsule(grip + fwd * R(-0.06), grip + fwd * shaft, r0, r1, seg=8)
        head = grip + fwd * shaft
        if w == "hammer":
            kit.use(color=accent, mat="bf_metal")
            kit.box(head, (R(0.16), R(0.07), R(0.08)), bevel=R(0.008))
        elif w == "mace":
            kit.use(color=accent, mat="bf_metal")
            kit.ellipsoid(head, (R(0.045),) * 3, seg=12, rings=8)
            kit.spikes(head, (0, -1, 0), R(0.04), 6, R(0.035), r=R(0.01), spread=1.2)
            kit.use(color=glow, mat=kit.glow(glow))
            kit.ellipsoid(head + fwd * R(0.045), (R(0.015),) * 3, seg=8, rings=5)
        else:
            kit.ellipsoid(head, (R(0.055), R(0.07), R(0.055)), seg=12, rings=8)
            kit.use(color=mul3(wood, 1.3))
            kit.tube([head + Vector((R(0.03), 0, R(0.02))), head + Vector((R(0.09), -R(0.02), R(0.1)))], [R(0.01), R(0.003)], seg=5)
            kit.use(color=glow, mat=kit.glow(glow))
            kit.ellipsoid(head + Vector((R(0.09), -R(0.02), R(0.11))), (R(0.015),) * 3, seg=8, rings=5)
    elif w in ("staff", "scythe"):
        up = Vector((0, 0, 1))
        kit.use(color=wood if w == "staff" else (0.75, 0.7, 0.6), mat="bf_matte", smooth=True)
        kit.tube([grip - up * R(0.42), grip, grip + up * R(0.3) + Vector((R(0.01), 0, 0)), grip + up * R(0.46)], [R(0.012), R(0.012), R(0.011), R(0.01)], seg=8)
        top = grip + up * R(0.47)
        if w == "staff":
            kit.use(color=accent, mat="bf_metal")
            for i in range(3):
                a = i * 2 * math.pi / 3
                kit.tube([top - up * R(0.02), top + Vector((math.cos(a) * R(0.04), math.sin(a) * R(0.04), R(0.07)))], [R(0.006), R(0.002)], seg=5)
            kit.use(color=glow, mat=kit.glow(glow))
            kit.ellipsoid(top + up * R(0.045), (R(0.028),) * 3, seg=12, rings=8)
        else:
            kit.use(color=(0.85, 0.8, 0.7), mat="bf_matte")
            kit.ellipsoid(top, (R(0.03), R(0.032), R(0.034)), seg=12, rings=8)
            kit.use(color=steel, mat="bf_metal")
            pts = [top + Vector((0, -R(0.02) - t * R(0.22), -math.sin(t * 1.3) * R(0.1))) for t in (0, 0.33, 0.66, 1.0)]
            kit.tube(pts, [R(0.018), R(0.014), R(0.009), R(0.002)], seg=4, scale=(1.0, 0.25))
            kit.use(color=glow, mat=kit.glow(glow))
            for sgn in (-1, 1):
                kit.ellipsoid(top + Vector((sgn * R(0.011), -R(0.026), R(0.004))), (R(0.006),) * 3, seg=6, rings=4)
    elif w in ("bow", "crossbow"):
        kit.use(color=wood, mat="bf_matte", smooth=True)
        if w == "bow":
            pts = [grip + Vector((0, math.cos(a) * R(0.08) - R(0.08), math.sin(a) * R(0.28))) for a in (-1.2, -0.6, 0, 0.6, 1.2)]
            kit.tube(pts, [R(0.004), R(0.009), R(0.011), R(0.009), R(0.004)], seg=6)
        else:
            kit.box(grip + fwd * R(0.12), (R(0.03), R(0.28), R(0.03)), bevel=R(0.005))
            kit.use(color=steel, mat="bf_metal")
            kit.tube([grip + Vector((-R(0.16), -R(0.24), 0)), grip + fwd * R(0.26), grip + Vector((R(0.16), -R(0.24), 0))], [R(0.006), R(0.009), R(0.006)], seg=6)
    elif w == "shovel":
        kit.use(color=wood, mat="bf_matte", smooth=True)
        kit.capsule(grip + fwd * R(-0.12), grip + fwd * R(0.45), R(0.012), seg=8)
        kit.use(color=(0.42, 0.4, 0.38), mat="bf_metal")
        kit.box(grip + fwd * R(0.52), (R(0.13), R(0.16), R(0.012)), bevel=R(0.004), taper=0.8)
    if spec.get("shield"):
        kit.use(bone="forearm.L", color=spec.get("shield_color", accent), mat="bf_metal", smooth=True)
        c = Vector((s["elbow"].x + R(0.035), -R(0.04), s["elbow"].z - s["fore"] * 0.45))
        kit.lathe([(R(0.13), 0), (R(0.13), R(0.012)), (R(0.1), R(0.025)), (0.001, R(0.035))], center=c, axis=(1, 0, 0), seg=20, scale=(1.0, 1.25))
        kit.use(color=glow, mat=kit.glow(glow))
        kit.ellipsoid(c + Vector((R(0.038), 0, 0)), (R(0.012), R(0.02), R(0.02)), seg=8, rings=5)


# ================================================================================================ build + clips

def build(spec):
    """Returns (armature, mesh) for a humanoid spec."""
    s, bones = skeleton(spec)
    arm = L.build_armature(spec["key"], bones)
    kit = L.Kit(spec["key"])
    build_body(kit, spec, s)
    build_weapon(kit, spec, s)
    extra = spec.get("extra")
    if extra:
        extra(kit, spec, s)
    mesh = kit.to_object(subdivide=spec.get("subdivide", 0))
    if spec.get("subdivide", 0):
        L.apply_modifiers(mesh)
    L.bind(mesh, arm)
    grip_bone = "weapon_r" if spec.get("weapon") else "hand.R"
    L.add_empty("projectile_origin", arm, grip_bone, (0, 0.05 * s["H"], 0))
    return arm, mesh, s


def make_clips(arm, spec):
    """The standard humanoid clip set. Character flavour: 'stance' ('heavy', 'caster', 'agile', 'beast')."""
    stance = spec.get("stance", "heavy")
    two_hand = spec.get("weapon") in ("greatsword", "hammer", "club", "scythe", "shovel")
    hunch = spec.get("hunch", 0)
    relax = {"upper_arm.L": (0, 0, -8), "upper_arm.R": (0, 0, 8), "forearm.L": (-12, 0, 0), "forearm.R": (-18, 0, 0),
             "spine": (hunch, 0, 0), "chest": (hunch * 0.5, 0, 0), "neck": (-hunch * 0.8, 0, 0)}
    if two_hand:
        relax.update({"upper_arm.R": (-25, 0, 10), "forearm.R": (-45, 0, 0), "upper_arm.L": (-20, 0, -5), "forearm.L": (-60, 0, 20)})
    if stance == "caster":
        relax.update({"upper_arm.R": (-15, 0, 6), "forearm.R": (-55, 0, 0)})

    def P(**over):
        p = dict(relax)
        for kk, v in over.items():
            p[kk.replace("__", ".")] = v
        return p

    def add(p, bone, rot):
        a = p.get(bone, (0, 0, 0))
        p[bone] = (a[0] + rot[0], a[1] + rot[1], a[2] + rot[2]) if not isinstance(a, dict) else a
        return p

    clips = []
    # Idle: breathing and a slow sway.
    c = L.Clip(arm, "Idle", 60)
    c.key(0, P()); c.key(30, add(add(P(), "chest", (2.5, 0, 0)), "head", (-2, 3, 0))); c.key(60, P())
    clips.append(c.done(cyclic=True))
    # Run: 20 frames per stride.
    lean = 10 + hunch * 0.3
    arm_swing = 38 if stance != "caster" else 22

    Hs = spec["height"]

    def stride(sgn, down):
        p = P(spine=(lean, 0, 0), chest=(2, -sgn * 6, 0), hips={"rot": (0, sgn * 6, 0), "loc": (0, (-0.02 if down else 0.01) * Hs, 0)})
        p["thigh.L"] = (-38 * sgn, 0, 0); p["thigh.R"] = (38 * sgn, 0, 0)
        p["shin.L"] = (8 if sgn > 0 else 70, 0, 0); p["shin.R"] = (70 if sgn > 0 else 8, 0, 0)
        p["foot.L"] = (10 * sgn, 0, 0); p["foot.R"] = (-10 * sgn, 0, 0)
        if not two_hand:
            p["upper_arm.L"] = (arm_swing * sgn, 0, -10); p["upper_arm.R"] = (-arm_swing * sgn, 0, 10)
            p["forearm.L"] = (-45, 0, 0); p["forearm.R"] = (-45, 0, 0)
        return p

    def passing(sgn):
        p = P(spine=(lean, 0, 0), hips={"rot": (0, 0, 0), "loc": (0, 0.015 * Hs, 0)})
        p["thigh.L"] = (-5 * sgn, 0, 0); p["thigh.R"] = (5 * sgn, 0, 0)
        p["shin.L"] = (15 if sgn > 0 else 85, 0, 0); p["shin.R"] = (85 if sgn > 0 else 15, 0, 0)
        return p

    c = L.Clip(arm, "Run", 20)
    c.key(0, stride(1, True)); c.key(5, passing(1)); c.key(10, stride(-1, True)); c.key(15, passing(-1)); c.key(20, stride(1, True))
    clips.append(c.done(cyclic=True))

    # Attacks (impact at 40%).
    def attack(name, windup, strike, follow, frames=30):
        c = L.Clip(arm, name, frames)
        c.key(0, P()); c.key(int(frames * 0.25), windup); c.key(int(frames * 0.4), strike); c.key(int(frames * 0.65), follow); c.key(frames, P())
        clips.append(c.done())

    if stance == "caster":
        attack("Attack1",
               P(upper_arm__R=(-40, 0, 20), forearm__R=(-90, 0, 0), chest=(0, 20, 0)),
               P(upper_arm__R=(-95, 0, 5), forearm__R=(-10, 0, 0), chest=(5, -15, 0), spine=(5, 0, 0)),
               P(upper_arm__R=(-70, 0, 5), forearm__R=(-20, 0, 0)))
        attack("Attack2",
               P(upper_arm__L=(-40, 0, -20), forearm__L=(-90, 0, 0), chest=(0, -20, 0)),
               P(upper_arm__L=(-95, 0, -5), forearm__L=(-10, 0, 0), chest=(5, 15, 0), spine=(5, 0, 0)),
               P(upper_arm__L=(-70, 0, -5), forearm__L=(-20, 0, 0)))
    elif spec.get("weapon") in ("bow", "crossbow"):
        attack("Attack1",
               P(upper_arm__R=(-85, 0, 0), forearm__R=(-10, 0, 0), upper_arm__L=(-80, 0, -30), forearm__L=(-110, 0, 0), chest=(0, 30, 0)),
               P(upper_arm__R=(-88, 0, 0), forearm__R=(-5, 0, 0), upper_arm__L=(-85, 0, -40), forearm__L=(-80, 0, 0), chest=(0, 30, 0)),
               P(upper_arm__R=(-70, 0, 0), chest=(0, 15, 0)))
        attack("Attack2",
               P(upper_arm__R=(-85, 0, 0), forearm__R=(-10, 0, 0), upper_arm__L=(-80, 0, -30), forearm__L=(-110, 0, 0), chest=(0, 30, 0)),
               P(upper_arm__R=(-88, 0, 0), upper_arm__L=(-85, 0, -40), forearm__L=(-75, 0, 0), chest=(0, 32, 0)),
               P(upper_arm__R=(-70, 0, 0), chest=(0, 15, 0)))
    else:
        big = 1.2 if two_hand else 1.0
        attack("Attack1",
               P(upper_arm__R=(60 * big, 0, 35), forearm__R=(-80, 0, 0), upper_arm__L=(-30, 0, -10) if not two_hand else (40, 0, -30), chest=(-5, 30, 0), spine=(-5, 0, 0)),
               P(upper_arm__R=(-80, 0, 10), forearm__R=(-10, 0, 0), upper_arm__L=(10, 0, -15) if not two_hand else (-70, 0, -25), chest=(12, -25, 0), spine=(12, 0, 0)),
               P(upper_arm__R=(-35, 0, 15), forearm__R=(-20, 0, 0), chest=(8, -30, 0), spine=(8, 0, 0)))
        attack("Attack2",
               P(upper_arm__R=(-40, 0, 85), forearm__R=(-30, 0, 0), chest=(0, 45, 0), upper_arm__L=(-20, 0, -30)),
               P(upper_arm__R=(-80, 0, -20), forearm__R=(-15, 0, 0), chest=(5, -45, 0), spine=(5, -10, 0), upper_arm__L=(20, 0, -20)),
               P(upper_arm__R=(-50, 0, -40), forearm__R=(-25, 0, 0), chest=(5, -50, 0)))

    # Casts.
    attack("Cast1",
           P(upper_arm__R=(20, 0, 30), forearm__R=(-90, 0, 0), chest=(-4, 20, 0)),
           P(upper_arm__R=(-90, 0, 5), forearm__R=(-5, 0, 0), chest=(6, -15, 0), spine=(6, 0, 0)),
           P(upper_arm__R=(-70, 0, 5), forearm__R=(-15, 0, 0)))
    attack("Cast2",
           P(upper_arm__R=(-150, 0, 20), upper_arm__L=(-150, 0, -20), forearm__R=(-20, 0, 0), forearm__L=(-20, 0, 0), spine=(-8, 0, 0), head=(-10, 0, 0)),
           P(upper_arm__R=(-80, 0, 10), upper_arm__L=(-80, 0, -10), forearm__R=(0, 0, 0), forearm__L=(0, 0, 0), spine=(10, 0, 0)),
           P(upper_arm__R=(-60, 0, 10), upper_arm__L=(-60, 0, -10)))
    attack("Cast3",
           P(upper_arm__R=(-160, 0, 10), upper_arm__L=(-160, 0, -10), forearm__R=(-30, 0, 0), forearm__L=(-30, 0, 0), spine=(-10, 0, 0), thigh__L=(-10, 0, 0), shin__L=(15, 0, 0)),
           P(upper_arm__R=(-40, 0, 20), upper_arm__L=(-40, 0, -20), forearm__R=(-10, 0, 0), forearm__L=(-10, 0, 0), spine=(25, 0, 0), chest=(10, 0, 0),
             thigh__L=(-40, 0, 0), shin__L=(50, 0, 0), thigh__R=(20, 0, 0), shin__R=(40, 0, 0), hips={"rot": (0, 0, 0), "loc": (0, -0.12 * spec["height"] * 0.3, 0)}),
           P(upper_arm__R=(-30, 0, 20), upper_arm__L=(-30, 0, -20), spine=(15, 0, 0)))
    c = L.Clip(arm, "CastUlt", 36)
    c.key(0, P())
    c.key(10, P(upper_arm__R=(-170, 0, 25), upper_arm__L=(-170, 0, -25), forearm__R=(-10, 0, 0), forearm__L=(-10, 0, 0), spine=(-12, 0, 0), head=(-15, 0, 0),
                thigh__L=(-25, 0, 0), shin__L=(40, 0, 0), thigh__R=(15, 0, 0), shin__R=(35, 0, 0), hips={"rot": (0, 0, 0), "loc": (0, -0.05 * spec["height"], 0)}))
    c.key(14, P(upper_arm__R=(-60, 0, 45), upper_arm__L=(-60, 0, -45), forearm__R=(-5, 0, 0), forearm__L=(-5, 0, 0), spine=(22, 0, 0), chest=(8, 0, 0),
                thigh__L=(-45, 0, 0), shin__L=(60, 0, 0), thigh__R=(25, 0, 0), shin__R=(50, 0, 0), hips={"rot": (0, 0, 0), "loc": (0, -0.1 * spec["height"], 0)}))
    c.key(24, P(upper_arm__R=(-40, 0, 40), upper_arm__L=(-40, 0, -40), spine=(15, 0, 0)))
    c.key(36, P())
    clips.append(c.done())
    # Channel: arms forward, pulsing.
    c = L.Clip(arm, "Channel", 30)
    ch = lambda d: P(upper_arm__R=(-70 - d, 0, -8), upper_arm__L=(-70 - d, 0, 8), forearm__R=(-20, 0, 0), forearm__L=(-20, 0, 0), spine=(4, 0, 0), head=(4, 0, 0))
    c.key(0, ch(0)); c.key(15, ch(8)); c.key(30, ch(0))
    clips.append(c.done(cyclic=True))
    # Stun: slumped, swaying.
    c = L.Clip(arm, "Stun", 30)
    st = lambda z: P(head=(25, 0, z), neck=(10, 0, 0), spine=(12, 0, z * 0.5), upper_arm__L=(10, 0, -5), upper_arm__R=(10, 0, 5), forearm__L=(-5, 0, 0), forearm__R=(-5, 0, 0),
                     shin__L=(15, 0, 0), shin__R=(15, 0, 0), thigh__L=(-8, 0, 0), thigh__R=(-8, 0, 0))
    c.key(0, st(-6)); c.key(15, st(6)); c.key(30, st(-6))
    clips.append(c.done(cyclic=True))
    # Death: stagger, knees buckle, fall backwards.
    Hh = spec["height"]
    c = L.Clip(arm, "Death", 45)
    c.key(0, P())
    c.key(8, P(spine=(-15, 0, 0), head=(-20, 0, 0), upper_arm__L=(-30, 0, -40), upper_arm__R=(-30, 0, 40)))
    c.key(20, P(root={"rot": (-35, 0, 0), "loc": (0, 0, 0)}, hips={"rot": (0, 0, 0), "loc": (0, -0.12 * Hh, 0)}, thigh__L=(-50, 0, 0), thigh__R=(-40, 0, 0),
                shin__L=(80, 0, 0), shin__R=(70, 0, 0), spine=(-10, 0, 0), upper_arm__L=(-60, 0, -50), upper_arm__R=(-60, 0, 50)))
    c.key(34, P(root={"rot": (-88, 0, 0), "loc": (0, 0.05 * Hh, 0)}, hips={"rot": (0, 0, 0), "loc": (0, -0.05 * Hh, 0)}, thigh__L=(-20, 0, 0), thigh__R=(-10, 0, 0),
                shin__L=(30, 0, 0), shin__R=(20, 0, 0), upper_arm__L=(-150, 0, -30), upper_arm__R=(-120, 0, 40), head=(-15, 0, 10)))
    c.key(45, P(root={"rot": (-90, 0, 0), "loc": (0, 0.06 * Hh, 0)}, hips={"rot": (0, 0, 0), "loc": (0, -0.05 * Hh, 0)}, thigh__L=(-15, 0, 0), thigh__R=(-5, 0, 0),
                shin__L=(20, 0, 0), shin__R=(10, 0, 0), upper_arm__L=(-160, 0, -35), upper_arm__R=(-130, 0, 45), head=(-10, 0, 20)))
    clips.append(c.done())
    # Spawn: rise from a crouch.
    c = L.Clip(arm, "Spawn", 24)
    c.key(0, P(hips={"rot": (0, 0, 0), "loc": (0, -0.22 * Hh, 0)}, thigh__L=(-80, 0, 0), thigh__R=(-70, 0, 0), shin__L=(120, 0, 0), shin__R=(110, 0, 0), spine=(30, 0, 0), head=(20, 0, 0)))
    c.key(14, P(hips={"rot": (0, 0, 0), "loc": (0, -0.05 * Hh, 0)}, thigh__L=(-20, 0, 0), thigh__R=(-15, 0, 0), shin__L=(30, 0, 0), shin__R=(25, 0, 0), spine=(10, 0, 0)))
    c.key(24, P())
    clips.append(c.done())
    arm.animation_data.action = clips[0]
    return clips
