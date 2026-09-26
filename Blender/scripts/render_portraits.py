"""
Renders hero portraits (256x320) from the Blender models into Client/Assets/Resources/Textures/Icons/Portraits.

    python3 Blender/scripts/render_portraits.py [hero_key ...]

Head-and-shoulders framing, faction-coloured backdrop and rim light, Cycles CPU. Replaces the silhouettes drawn by
Tools/art/generate_icons.py (which only draws a portrait when none exists).
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy                  # noqa: E402
import bf_humanoid as HU    # noqa: E402
import bf_lib as L          # noqa: E402
from bf_characters import SPECS  # noqa: E402
from mathutils import Vector  # noqa: E402

OUT = os.path.join(L.ROOT, "Client", "Assets", "Resources", "Textures", "Icons", "Portraits")
DATA = os.path.join(L.ROOT, "Shared", "Runtime", "Resources", "GameData", "heroes")
FACTION = {  # backdrop, rim light
    "CrimsonCourt": ((0.35, 0.02, 0.04), (1.0, 0.25, 0.2)),
    "AshenLegion": ((0.06, 0.14, 0.07), (0.55, 1.0, 0.5)),
    "WildCovenant": ((0.04, 0.1, 0.2), (0.6, 0.8, 1.0)),
    "Dawnguard": ((0.3, 0.2, 0.05), (1.0, 0.85, 0.5)),
}


def hero_faction(key):
    name = key.replace("hero_", "").split("_")[0]
    path = os.path.join(DATA, name + ".json")
    return json.load(open(path))["hero"]["faction"] if os.path.exists(path) else "CrimsonCourt"


def render(key):
    spec = dict(SPECS[key]); spec["key"] = key
    L.reset_scene()
    arm, mesh, s = HU.build(spec)
    clips = HU.make_clips(arm, spec)
    arm.animation_data.action = clips[0]
    scene = bpy.context.scene
    scene.frame_set(0)
    H = s["H"]
    back, rim = FACTION[hero_faction(key)]
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = 48
    scene.cycles.use_denoising = True
    scene.render.resolution_x, scene.render.resolution_y = 256, 320
    scene.view_settings.view_transform = 'Standard'
    world = bpy.data.worlds.new("w"); scene.world = world;     world.node_tree.nodes["Background"].inputs[0].default_value = (*L.srgb_to_linear(back)[:3], 1)
    world.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    # Backdrop card with a radial glow behind the head.
    head = Vector((0, -0.01 * H, s["headY"] + 0.06 * H))
    cam_d = bpy.data.cameras.new("cam"); cam_d.lens = 85
    cam = L.link(bpy.data.objects.new("cam", cam_d))
    # From the character's left (weapons are held in the right hand), looking slightly up at the face.
    yaw = math.radians(24)
    dist = H * 1.18   # heroes have bigger heads (bf_humanoid.HEROIC): frame a little wider
    aim = head - Vector((0, 0, 0.05 * H))
    cam.location = aim + Vector((math.sin(yaw) * dist, -math.cos(yaw) * dist, -0.02 * H))
    cam.rotation_euler = (aim - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    for i, (loc, energy, col, size) in enumerate(((((1.5, -2.5, 1.2)), 380, (1, 0.95, 0.9), 1.5),
                                                  (((-2.0, 1.5, 0.8)), 700, rim, 0.8),
                                                  (((0.0, 2.0, 1.5)), 450, rim, 1.5))):
        ld = bpy.data.lights.new(f"l{i}", 'AREA'); ld.energy = energy * 0.65 * (H / 2) ** 2; ld.size = size; ld.color = col
        lo = L.link(bpy.data.objects.new(f"l{i}", ld))
        lo.location = head + Vector(loc) * (H / 2)
        lo.rotation_euler = (head - lo.location).to_track_quat('-Z', 'Y').to_euler()
    card = bpy.data.meshes.new("card")
    import bmesh
    bm = bmesh.new(); bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=H * 2); bm.to_mesh(card); bm.free()
    co = L.link(bpy.data.objects.new("card", card))
    co.location = head + Vector((0, H * 0.9, 0)); co.rotation_euler = (math.radians(90), 0, 0)
    m = bpy.data.materials.new("card"); m.use_nodes = True
    nt = m.node_tree; bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*L.srgb_to_linear(back)[:3], 1)
    bsdf.inputs["Emission Color"].default_value = (*L.srgb_to_linear(rim)[:3], 1)
    grad = nt.nodes.new("ShaderNodeTexGradient"); grad.gradient_type = 'SPHERICAL'
    mapping = nt.nodes.new("ShaderNodeMapping"); tc = nt.nodes.new("ShaderNodeTexCoord")
    mapping.inputs["Scale"].default_value = (1.4, 1.4, 1.4)
    nt.links.new(tc.outputs["Object"], mapping.inputs["Vector"]); nt.links.new(mapping.outputs["Vector"], grad.inputs["Vector"])
    ramp = nt.nodes.new("ShaderNodeMath"); ramp.operation = 'POWER'; ramp.inputs[1].default_value = 2.5
    nt.links.new(grad.outputs["Fac"], ramp.inputs[0]); nt.links.new(ramp.outputs[0], bsdf.inputs["Emission Strength"])
    co.data.materials.append(m)
    os.makedirs(OUT, exist_ok=True)
    name = key.replace("hero_", "")
    scene.render.filepath = os.path.join(OUT, f"portrait_{name}.png")
    bpy.ops.render.render(write_still=True)
    return scene.render.filepath


if __name__ == "__main__":
    keys = sys.argv[1:] or [k for k in SPECS if k.startswith("hero_") and k != "hero_fenrax_moonfang"]
    for k in keys:
        print("portrait", render(k), flush=True)
