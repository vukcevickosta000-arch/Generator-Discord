"""
Builds the Bloodfall character and structure models and exports them as FBX for Unity.

    python3 Blender/scripts/build_models.py                 # everything
    python3 Blender/scripts/build_models.py hero_vorak      # selected keys
    python3 Blender/scripts/build_models.py --preview ...   # also render Blender/previews/<key>.png (Cycles, CPU)

Output: Client/Assets/Resources/Models/<key>.fbx (ModelFactory loads these instead of the procedural stand-ins).
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bf_lib as L          # noqa: E402
import bf_humanoid as HU    # noqa: E402
from bf_characters import SPECS  # noqa: E402


def build_character(key, preview=False):
    spec = dict(SPECS[key]); spec["key"] = key
    L.reset_scene()
    arm, mesh, s = HU.build(spec)
    clips = HU.make_clips(arm, spec)
    tris = L.triangle_count(mesh)
    path = os.path.join(L.MODELS_OUT, key + ".fbx")
    objs = [arm, mesh] + [o for o in arm.children if o.type == 'EMPTY']
    L.export_fbx(path, objs)
    info = f"{key}: {tris} tris, {len(clips)} clips, {os.path.getsize(path) / 1024:.0f} KiB"
    if preview:
        arm.animation_data.action = next(c for c in clips if c.name == "Idle")
        L.render_preview(os.path.join(L.PREVIEW_OUT, key + ".png"), s["H"] * 1.15, frame=0)
        for clip_name, frame in (("Attack1", 12), ("Run", 0)):
            arm.animation_data.action = next(c for c in clips if c.name == clip_name)
            L.render_preview(os.path.join(L.PREVIEW_OUT, f"{key}_{clip_name.lower()}.png"), s["H"] * 1.15, frame=frame, size=320, samples=12)
    return info


def contact_sheet(keys, path, cell=256, cols=6):
    """Joins the idle previews into one image for review (needs Pillow)."""
    from PIL import Image
    ims = [(k, os.path.join(L.PREVIEW_OUT, k + ".png")) for k in keys]
    ims = [(k, Image.open(p).convert("RGB").resize((cell, cell))) for k, p in ims if os.path.exists(p)]
    rows = (len(ims) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * cell), (20, 18, 20))
    for i, (_, im) in enumerate(ims):
        sheet.paste(im, ((i % cols) * cell, (i // cols) * cell))
    sheet.save(path)


def main(argv):
    preview = "--preview" in argv
    keys = [a for a in argv if not a.startswith("--")] or list(SPECS)
    built = []
    for key in keys:
        if key in SPECS:
            print(build_character(key, preview), flush=True)
            built.append(key)
        else:
            print(f"unknown key {key}", flush=True)
    if preview and built:
        contact_sheet(built, os.path.join(L.PREVIEW_OUT, "_contact_sheet.png"))


if __name__ == "__main__":
    main(sys.argv[1:])
