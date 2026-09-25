"""Renders a strip of key poses for one character (animation review): python3 pose_strip.py <key>"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bf_lib as L          # noqa: E402
import bf_humanoid as HU    # noqa: E402
from bf_characters import SPECS  # noqa: E402

POSES = [("Idle", 0), ("Run", 0), ("Run", 5), ("Attack1", 7), ("Attack1", 12), ("Attack2", 12), ("Cast1", 12),
         ("Cast2", 7), ("Cast3", 12), ("CastUlt", 10), ("CastUlt", 14), ("Channel", 0), ("Stun", 0), ("Death", 20), ("Death", 45), ("Spawn", 0)]


def main(key):
    spec = dict(SPECS[key]); spec["key"] = key
    L.reset_scene()
    arm, mesh, s = HU.build(spec)
    clips = {c.name: c for c in HU.make_clips(arm, spec)}
    from PIL import Image
    out = []
    for name, frame in POSES:
        arm.animation_data.action = clips[name]
        p = os.path.join(L.PREVIEW_OUT, "_strip", f"{key}_{name}_{frame}.png")
        L.render_preview(p, s["H"] * 1.2, frame=frame, size=256, samples=8, yaw=-60)
        out.append(Image.open(p).convert("RGB"))
    sheet = Image.new("RGB", (256 * 8, 256 * 2))
    for i, im in enumerate(out):
        sheet.paste(im, ((i % 8) * 256, (i // 8) * 256))
    sheet.save(os.path.join(L.PREVIEW_OUT, f"_poses_{key}.png"))


if __name__ == "__main__":
    main(sys.argv[1])
