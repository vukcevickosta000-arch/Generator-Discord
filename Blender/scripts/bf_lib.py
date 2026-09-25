"""
Bloodfall Blender pipeline: shared geometry kit, materials, rigging, export and preview rendering.

Conventions (Docs/ART_DIRECTION.md §5):
  * 1 Blender unit = 1 m, Z up, characters face -Y (exported so they face +Z in Unity with bakeAxisConversion).
  * Pivot at the feet.
  * Colours live in the "Col" colour attribute (sRGB authored, stored linear, exported sRGB) like the procedural
    stand-ins; materials only say *how* a surface shades. Unity maps them by name (ModelFactory.ConvertMaterials):
      bf_matte, bf_metal, bf_team (tinted to the team colour), bf_glow_RRGGBB (emissive).
  * One skinned mesh per character, rigid-bound per part (every vertex belongs to one bone).

Everything is built with bmesh (no viewport operators) so it runs headless through the `bpy` module.
"""
import math
import os

import bpy  # noqa: I001  (bpy must be imported before bmesh/mathutils when running as a Python module)
import bmesh
from mathutils import Matrix, Vector

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MODELS_OUT = os.path.join(ROOT, "Client", "Assets", "Resources", "Models")
PREVIEW_OUT = os.path.join(ROOT, "Blender", "previews")


# ------------------------------------------------------------------------------------------------ colours

def srgb_to_linear(c):
    def f(x):
        return x / 12.92 if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4
    return (f(c[0]), f(c[1]), f(c[2]), 1.0)


def mul(c, k):
    return (min(1.0, c[0] * k), min(1.0, c[1] * k), min(1.0, c[2] * k))


def mix(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


# ------------------------------------------------------------------------------------------------ scene

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    for coll in (bpy.data.meshes, bpy.data.materials, bpy.data.armatures, bpy.data.actions, bpy.data.objects):
        for block in list(coll):
            coll.remove(block)


def link(obj):
    bpy.context.scene.collection.objects.link(obj)
    return obj


# ------------------------------------------------------------------------------------------------ materials

MATERIAL_KINDS = ("bf_matte", "bf_metal", "bf_team")


def material(name, glow=None):
    """Named material with a preview node tree driven by the Col attribute."""
    if name in bpy.data.materials:
        return bpy.data.materials[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    attr = nt.nodes.new("ShaderNodeAttribute")
    attr.attribute_name = "Col"
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])
    if name == "bf_team":
        # Previews show the Dusk accent; Unity replaces it with the team colour.
        bsdf.inputs["Base Color"].default_value = srgb_to_linear((0.55, 0.06, 0.09))
        bsdf.inputs["Roughness"].default_value = 0.6
    else:
        nt.links.new(attr.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.35 if name == "bf_metal" else 0.75
        bsdf.inputs["Metallic"].default_value = 0.75 if name == "bf_metal" else 0.0
    if glow is not None:
        bsdf.inputs["Emission Color"].default_value = srgb_to_linear(glow)
        bsdf.inputs["Emission Strength"].default_value = 3.0
    m.diffuse_color = (1, 1, 1, 1)
    return m


def glow_material(c):
    hexs = "".join(f"{int(max(0, min(1, v)) * 255):02X}" for v in c[:3])
    return material(f"bf_glow_{hexs}", glow=c)


# ------------------------------------------------------------------------------------------------ geometry kit

class Kit:
    """Accumulates parts into one bmesh with per-part bone, colour and material."""

    def __init__(self, name):
        self.name = name
        self.bm = bmesh.new()
        self.col = self.bm.loops.layers.color.new("Col")
        self.deform = self.bm.verts.layers.deform.verify()
        self.materials = []          # material objects in slot order
        self.bones = []              # bone names in vertex-group order
        self.current_bone = "root"
        self.current_color = (0.5, 0.5, 0.5)
        self.current_mat = "bf_matte"
        self.current_smooth = True
        self.xform = Matrix.Identity(4)

    # -- state -----------------------------------------------------------------------------------
    def use(self, bone=None, color=None, mat=None, smooth=None):
        if bone is not None:
            self.current_bone = bone
        if color is not None:
            self.current_color = color
        if mat is not None:
            self.current_mat = mat
        if smooth is not None:
            self.current_smooth = smooth
        return self

    def _mat_index(self, mat):
        m = glow_material(mat[1]) if isinstance(mat, tuple) else material(mat)
        if m not in self.materials:
            self.materials.append(m)
        return self.materials.index(m)

    def _bone_index(self, bone):
        if bone not in self.bones:
            self.bones.append(bone)
        return self.bones.index(bone)

    def _finish(self, verts, faces, color=None, mat=None, bone=None, smooth=None):
        color = color or self.current_color
        mi = self._mat_index(mat or self.current_mat)
        bi = self._bone_index(bone or self.current_bone)
        lin = srgb_to_linear(color)
        sm = self.current_smooth if smooth is None else smooth
        for v in verts:
            v.co = self.xform @ v.co
            v[self.deform][bi] = 1.0
        for f in faces:
            f.material_index = mi
            f.smooth = sm
            for loop in f.loops:
                loop[self.col] = lin
        return verts, faces

    def glow(self, c):
        return ("glow", c)

    # -- primitives ------------------------------------------------------------------------------
    def ring_verts(self, center, axis, radius, seg, ref=None, scale=(1.0, 1.0), phase=0.0):
        axis = Vector(axis).normalized()
        ref = Vector(ref) if ref is not None else (Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((0, 1, 0)))
        u = axis.cross(ref).normalized()
        w = axis.cross(u).normalized()
        out = []
        for i in range(seg):
            a = phase + i * 2 * math.pi / seg
            p = Vector(center) + (u * math.cos(a) * scale[0] + w * math.sin(a) * scale[1]) * radius
            out.append(self.bm.verts.new(p))
        return out

    def _bridge(self, r0, r1, faces):
        n = len(r0)
        for i in range(n):
            j = (i + 1) % n
            faces.append(self.bm.faces.new((r0[i], r0[j], r1[j], r1[i])))

    def _cap(self, ring, faces, flip=False, apex=None):
        if apex is not None:
            n = len(ring)
            for i in range(n):
                j = (i + 1) % n
                tri = (ring[j], ring[i], apex) if not flip else (ring[i], ring[j], apex)
                faces.append(self.bm.faces.new(tri))
        else:
            faces.append(self.bm.faces.new(list(reversed(ring)) if not flip else ring))

    def tube(self, points, radii, seg=10, caps=True, scale=(1.0, 1.0), ref=None, **kw):
        """Sweeps rings along a polyline. radii: one per point. Rounded caps when caps=True."""
        pts = [Vector(p) for p in points]
        rings, verts, faces = [], [], []
        for i, p in enumerate(pts):
            if i == 0:
                d = pts[1] - pts[0]
            elif i == len(pts) - 1:
                d = pts[-1] - pts[-2]
            else:
                d = (pts[i + 1] - pts[i - 1])
            r = self.ring_verts(p, d, radii[i], seg, ref=ref, scale=scale)
            rings.append(r)
            verts += r
        for a, b in zip(rings, rings[1:]):
            self._bridge(a, b, faces)
        if caps:
            d0 = (pts[0] - pts[1]).normalized()
            d1 = (pts[-1] - pts[-2]).normalized()
            a0 = self.bm.verts.new(pts[0] + d0 * radii[0] * 0.6)
            a1 = self.bm.verts.new(pts[-1] + d1 * radii[-1] * 0.6)
            verts += [a0, a1]
            self._cap(rings[0], faces, flip=True, apex=a0)
            self._cap(rings[-1], faces, flip=False, apex=a1)
        return self._finish(verts, faces, **kw)

    def capsule(self, a, b, r0, r1=None, seg=12, **kw):
        r1 = r0 if r1 is None else r1
        a, b = Vector(a), Vector(b)
        d = (b - a)
        L = d.length
        dn = d.normalized()
        # Hemispherical caps approximated by 2 extra rings each side.
        pts = [a - dn * r0 * 0.7, a - dn * r0 * 0.35, a, a + d * 0.5, b, b + dn * r1 * 0.35, b + dn * r1 * 0.7]
        rad = [r0 * 0.45, r0 * 0.85, r0, (r0 + r1) * 0.5, r1, r1 * 0.85, r1 * 0.45]
        return self.tube(pts, rad, seg=seg, caps=True, **kw)

    def ellipsoid(self, center, radii, seg=16, rings=10, rot=None, **kw):
        c = Vector(center)
        R = rot.to_matrix() if rot is not None else Matrix.Identity(3)
        verts, faces = [], []
        top = self.bm.verts.new(c + R @ Vector((0, 0, radii[2])))
        bot = self.bm.verts.new(c + R @ Vector((0, 0, -radii[2])))
        rows = []
        for j in range(1, rings):
            phi = math.pi * j / rings
            row = []
            for i in range(seg):
                th = 2 * math.pi * i / seg
                p = Vector((math.sin(phi) * math.cos(th) * radii[0], math.sin(phi) * math.sin(th) * radii[1], math.cos(phi) * radii[2]))
                row.append(self.bm.verts.new(c + R @ p))
            rows.append(row)
        verts = [top, bot] + [v for r in rows for v in r]
        for i in range(seg):
            k = (i + 1) % seg
            faces.append(self.bm.faces.new((rows[0][i], rows[0][k], top)))
            faces.append(self.bm.faces.new((rows[-1][k], rows[-1][i], bot)))
        for a, b in zip(rows, rows[1:]):
            for i in range(seg):
                k = (i + 1) % seg
                faces.append(self.bm.faces.new((a[i], b[i], b[k], a[k])))
        return self._finish(verts, faces, **kw)

    def box(self, center, size, rot=None, bevel=0.0, taper=1.0, **kw):
        """Box with optional top taper (scale of the +Z face) and bevel."""
        c = Vector(center)
        R = rot.to_matrix() if rot is not None else Matrix.Identity(3)
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        corners = []
        for z in (-1, 1):
            k = taper if z > 0 else 1.0
            for x, y in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
                corners.append(c + R @ Vector((x * hx * k, y * hy * k, z * hz)))
        vs = [self.bm.verts.new(p) for p in corners]
        idx = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
        faces = [self.bm.faces.new([vs[i] for i in f]) for f in idx]
        verts = list(vs)
        if bevel > 0:
            edges = list({e for f in faces for e in f.edges})
            res = bmesh.ops.bevel(self.bm, geom=verts + edges, offset=bevel, segments=2, affect='EDGES', profile=0.5)
            # Collect the whole beveled island: every face touching the new or surviving vertices.
            seed = {v for v in list(res["verts"]) + verts if v.is_valid}
            faces = list({f for v in seed for f in v.link_faces})
            verts = list({v for f in faces for v in f.verts})
        kw.setdefault("smooth", False)
        return self._finish(verts, faces, **kw)

    def lathe(self, profile, center=(0, 0, 0), axis=(0, 0, 1), seg=16, scale=(1.0, 1.0), close_top=True, close_bottom=True, ref=None, **kw):
        """Revolves [(radius, height), ...] around axis through center (heights measured along the axis)."""
        c, ax = Vector(center), Vector(axis).normalized()
        rings, verts, faces = [], [], []
        for r, h in profile:
            ring = self.ring_verts(c + ax * h, ax, max(r, 1e-4), seg, ref=ref, scale=scale)
            rings.append(ring)
            verts += ring
        for a, b in zip(rings, rings[1:]):
            self._bridge(a, b, faces)
        if close_bottom:
            ap = self.bm.verts.new(c + ax * profile[0][1]); verts.append(ap)
            self._cap(rings[0], faces, flip=True, apex=ap)
        if close_top:
            ap = self.bm.verts.new(c + ax * profile[-1][1]); verts.append(ap)
            self._cap(rings[-1], faces, flip=False, apex=ap)
        return self._finish(verts, faces, **kw)

    def cone(self, base, tip, r, seg=8, **kw):
        base, tip = Vector(base), Vector(tip)
        ring = self.ring_verts(base, tip - base, r, seg)
        apex = self.bm.verts.new(tip)
        faces = []
        self._cap(ring, faces, flip=False, apex=apex)
        centre = self.bm.verts.new(base)
        self._cap(ring, faces, flip=True, apex=centre)
        return self._finish(ring + [apex, centre], faces, **kw)

    def blade(self, base, tip, width, thick, side=(1, 0, 0), guard=0.0, **kw):
        """Double-edged blade: diamond cross-section tapering to the tip."""
        base, tip = Vector(base), Vector(tip)
        d = (tip - base)
        s = Vector(side).normalized()
        n = d.normalized().cross(s).normalized()
        L = d.length
        sections = [(0.0, 1.0), (0.75, 0.85), (1.0, 0.0)]
        rings, verts, faces = [], [], []
        for t, wk in sections:
            p = base + d * t
            w = max(width * 0.5 * wk, 0.002)
            th = max(thick * 0.5 * wk, 0.001)
            ring = [self.bm.verts.new(p + s * w), self.bm.verts.new(p + n * th), self.bm.verts.new(p - s * w), self.bm.verts.new(p - n * th)]
            rings.append(ring)
            verts += ring
        for a, b in zip(rings, rings[1:]):
            self._bridge(a, b, faces)
        self._cap(rings[0], faces, flip=True)
        faces.append(self.bm.faces.new(rings[-1]))
        kw.setdefault("smooth", False)
        return self._finish(verts, faces, **kw)

    def sheet(self, corners_top, corners_bottom, cols=6, rows=6, bulge=0.0, wave=0.0, thick=0.02, **kw):
        """Cloth-like panel between a top edge (2 points) and bottom edge (2 points), bent outward by bulge."""
        tl, tr = Vector(corners_top[0]), Vector(corners_top[1])
        bl, br = Vector(corners_bottom[0]), Vector(corners_bottom[1])
        normal = (tr - tl).cross(bl - tl).normalized()
        grid_front, grid_back, verts, faces = [], [], [], []
        for j in range(rows + 1):
            v = j / rows
            rowf, rowb = [], []
            for i in range(cols + 1):
                u = i / cols
                p = (tl * (1 - u) + tr * u) * (1 - v) + (bl * (1 - u) + br * u) * v
                p = p + normal * (math.sin(u * math.pi) * bulge * (0.4 + v)) + normal * (math.sin(u * math.pi * 3 + v * 2) * wave * v)
                rowf.append(self.bm.verts.new(p + normal * thick * 0.5))
                rowb.append(self.bm.verts.new(p - normal * thick * 0.5))
            grid_front.append(rowf); grid_back.append(rowb)
            verts += rowf + rowb
        for j in range(rows):
            for i in range(cols):
                faces.append(self.bm.faces.new((grid_front[j][i], grid_front[j + 1][i], grid_front[j + 1][i + 1], grid_front[j][i + 1])))
                faces.append(self.bm.faces.new((grid_back[j][i + 1], grid_back[j + 1][i + 1], grid_back[j + 1][i], grid_back[j][i])))
        # Edges.
        for j in range(rows):
            for col in (0, cols):
                a, b = (grid_front[j][col], grid_front[j + 1][col])
                c, d = (grid_back[j][col], grid_back[j + 1][col])
                faces.append(self.bm.faces.new((a, c, d, b) if col == 0 else (b, d, c, a)))
        for i in range(cols):
            for row in (0, rows):
                a, b = grid_front[row][i], grid_front[row][i + 1]
                c, d = grid_back[row][i], grid_back[row][i + 1]
                faces.append(self.bm.faces.new((a, b, d, c) if row == 0 else (c, d, b, a)))
        return self._finish(verts, faces, **kw)

    def spikes(self, center, axis, radius, count, length, r=0.03, phase=0.0, spread=0.0, **kw):
        """A crown of cones around a ring."""
        axis = Vector(axis).normalized()
        ref = Vector((0, 0, 1)) if abs(axis.z) < 0.9 else Vector((0, 1, 0))
        u = axis.cross(ref).normalized(); w = axis.cross(u).normalized()
        for i in range(count):
            a = phase + i * 2 * math.pi / count
            radial = u * math.cos(a) + w * math.sin(a)
            base = Vector(center) + radial * radius
            ln = length[i % len(length)] if isinstance(length, (list, tuple)) else length
            tip = base + (axis + radial * spread).normalized() * ln
            self.cone(base, tip, r, seg=6, **kw)

    # -- finish ----------------------------------------------------------------------------------
    def bake_ao(self, samples=24, distance=0.35, strength=0.75, ground=True):
        """Multiplies vertex colours by a ray-traced ambient occlusion term (crevices, armpits, under plates)."""
        from mathutils.bvhtree import BVHTree
        import random
        rnd = random.Random(7)
        self.bm.normal_update()
        tree = BVHTree.FromBMesh(self.bm)
        dirs = []
        while len(dirs) < samples:
            d = Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1)))
            if 0.05 < d.length <= 1:
                dirs.append(d.normalized())
        occ = {}
        for v in self.bm.verts:
            n = v.normal
            origin = v.co + n * 0.004
            hits = total = 0
            for d in dirs:
                if d.dot(n) < 0:
                    d = -d
                w = d.dot(n)
                total += w
                if tree.ray_cast(origin, d, distance)[0] is not None or (ground and d.z < 0 and origin.z + d.z * distance < 0):
                    hits += w
            occ[v] = 1.0 - strength * (hits / total if total else 0.0)
        for f in self.bm.faces:
            for loop in f.loops:
                c = loop[self.col]
                k = occ[loop.vert]
                loop[self.col] = (c[0] * k, c[1] * k, c[2] * k, 1.0)

    def to_object(self, subdivide=0, ao=True, ao_distance=None):
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        if ao:
            self.bake_ao(distance=ao_distance or 0.35)
        mesh = bpy.data.meshes.new(self.name)
        self.bm.to_mesh(mesh)
        self.bm.free()
        obj = link(bpy.data.objects.new(self.name, mesh))
        for m in self.materials:
            mesh.materials.append(m)
        for b in self.bones:
            obj.vertex_groups.new(name=b)
        if subdivide > 0:
            mod = obj.modifiers.new("subd", "SUBSURF")
            mod.levels = subdivide
            mod.render_levels = subdivide
            mod.boundary_smooth = 'PRESERVE_CORNERS'
        return obj


def apply_modifiers(obj):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg)
    mesh = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    old = obj.data
    obj.modifiers.clear()
    obj.data = mesh
    bpy.data.meshes.remove(old)


def triangle_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


# ------------------------------------------------------------------------------------------------ armature

def build_armature(name, bones):
    """bones: [(name, head, tail, parent or None)], roll 0 for all."""
    arm = bpy.data.armatures.new(name + "_rig")
    obj = link(bpy.data.objects.new("Armature", arm))
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    eb = {}
    for bname, head, tail, parent in bones:
        b = arm.edit_bones.new(bname)
        b.head, b.tail, b.roll = head, tail, 0.0
        if parent:
            b.parent = eb[parent]
        b.use_deform = True
        eb[bname] = b
    bpy.ops.object.mode_set(mode='OBJECT')
    return obj


def bind(mesh_obj, arm_obj):
    mesh_obj.parent = arm_obj
    mod = mesh_obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm_obj


def add_empty(name, arm_obj, bone, offset=(0, 0, 0)):
    e = link(bpy.data.objects.new(name, None))
    e.empty_display_size = 0.1
    e.parent = arm_obj
    e.parent_type = 'BONE'
    e.parent_bone = bone
    # Bone-parented empties sit at the bone tail; offset is in bone space.
    e.location = offset
    return e


# ------------------------------------------------------------------------------------------------ animation

class Clip:
    """Keyframes pose-bone Euler rotations (degrees) and root offsets for one action."""

    def __init__(self, arm_obj, name, frames, fps=30):
        self.arm = arm_obj
        self.name = name
        self.frames = frames
        self.action = bpy.data.actions.new(name)
        self.action.use_fake_user = True
        arm_obj.animation_data_create()
        arm_obj.animation_data.action = self.action
        for pb in arm_obj.pose.bones:
            pb.rotation_mode = 'XYZ'

    def key(self, frame, pose):
        """pose: {bone: (rx, ry, rz)} degrees or {bone: {'rot': (..), 'loc': (..)}}; unlisted bones return to rest.

        Held (non-rest) rotations get a ±0.1° alternating jitter: the FBX exporter drops channels that never change,
        which would lose a pose held for a whole clip (e.g. a bent elbow in Idle). Rest channels may be dropped safely.
        """
        self._n = getattr(self, "_n", 0) + 1
        jitter = 0.1 if self._n % 2 else -0.1
        for pb in self.arm.pose.bones:
            v = pose.get(pb.name)
            rot, loc = (0, 0, 0), (0, 0, 0)
            if isinstance(v, dict):
                rot = v.get("rot", (0, 0, 0)); loc = v.get("loc", (0, 0, 0))
            elif v is not None:
                rot = v
            rot = tuple(a + jitter if abs(a) > 1e-3 else a for a in rot)
            pb.rotation_euler = tuple(math.radians(a) for a in rot)
            pb.location = loc
            pb.keyframe_insert("rotation_euler", frame=frame)
            pb.keyframe_insert("location", frame=frame)

    def done(self, cyclic=False):
        self.action.frame_range = (0, self.frames)
        self.action.use_frame_range = True
        if cyclic:
            self.action.use_cyclic = True
        return self.action


def reset_pose(arm_obj):
    for pb in arm_obj.pose.bones:
        pb.rotation_euler = (0, 0, 0)
        pb.location = (0, 0, 0)


# ------------------------------------------------------------------------------------------------ export

def export_fbx(path, objects, animated=True):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    kwargs = dict(
        filepath=path, use_selection=True, object_types={'ARMATURE', 'MESH', 'EMPTY'},
        apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=False,
        use_mesh_modifiers=True, mesh_smooth_type='FACE', colors_type='SRGB', add_leaf_bones=False,
        primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL', path_mode='AUTO',
        bake_anim=animated, bake_anim_use_all_actions=animated, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=False, bake_anim_simplify_factor=1.0, bake_anim_step=1.0,
    )
    bpy.ops.export_scene.fbx(**kwargs)


# ------------------------------------------------------------------------------------------------ preview

def render_preview(path, focus_height, distance=None, size=512, samples=24, yaw=-35.0, pitch=32.0, frame=None, extra_light=1.0):
    """Cycles CPU render of the scene from a three-quarter front view (characters face -Y)."""
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.device = 'CPU'
    scene.cycles.samples = samples
    scene.cycles.use_denoising = True
    scene.render.resolution_x = size
    scene.render.resolution_y = size
    scene.render.film_transparent = False
    scene.view_settings.view_transform = 'AgX' if 'AgX' in [v.identifier for v in bpy.types.ColorManagedViewSettings.bl_rna.properties['view_transform'].enum_items] else 'Filmic'
    world = bpy.data.worlds.new("preview") if not scene.world else scene.world
    scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.09, 0.085, 0.09, 1)
        bg.inputs[1].default_value = 1.0
    h = focus_height
    dist = distance or max(3.0, h * 2.3)
    cam_data = bpy.data.cameras.new("cam")
    cam_data.lens = 50
    cam = link(bpy.data.objects.new("cam", cam_data))
    yr, pr = math.radians(yaw), math.radians(pitch)
    target = Vector((0, 0, h * 0.5))
    pos = target + Vector((math.sin(yr) * math.cos(pr), -math.cos(yr) * math.cos(pr), math.sin(pr))) * dist
    cam.location = pos
    cam.rotation_euler = (target - pos).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    for i, (loc, energy, col) in enumerate((((3, -4, 5), 1600, (1, 0.93, 0.85)), ((-4, -1, 3), 700, (0.6, 0.7, 1.0)), ((0, 5, 4), 700, (1, 0.35, 0.3)))):
        ld = bpy.data.lights.new(f"l{i}", 'AREA')
        ld.energy = energy * extra_light * max(1.0, h / 2.0) ** 2
        ld.size = 3
        ld.color = col
        lo = link(bpy.data.objects.new(f"l{i}", ld))
        lo.location = Vector(loc) * max(1.0, h / 2.0)
        lo.rotation_euler = (Vector((0, 0, h * 0.4)) - lo.location).to_track_quat('-Z', 'Y').to_euler()
    ground = bpy.data.meshes.new("ground")
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=max(4.0, h * 3))
    bm.to_mesh(ground); bm.free()
    g = link(bpy.data.objects.new("ground", ground))
    gm = bpy.data.materials.new("ground_mat"); gm.use_nodes = True
    gm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.08, 0.07, 0.07, 1)
    g.data.materials.append(gm)
    if frame is not None:
        scene.frame_set(frame)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    for o in (cam, g):
        bpy.data.objects.remove(o)
    for o in [o for o in bpy.data.objects if o.name.startswith("l") and o.type == 'LIGHT']:
        bpy.data.objects.remove(o)
