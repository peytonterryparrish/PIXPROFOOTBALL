"""Assemble + rig the sideline coach variants from the modular Draft Fever coach kit (Coach_Final.blend).

    blender -b --python Tools/Blender/build_coach.py -- <out_dir> [variants=A,B,C] [preview_dir]

For every variant it bakes the chosen kit pieces (modifiers + shape keys applied) into metre-space meshes, binds them to the clean
player deform skeleton (the 'root' armature stored in the same .blend, 66 bones, hips at 0.907 m) and exports
<out_dir>/Coach_<V>.fbx (skinned, Y-up, 1 unit = 1 m, character faces +Z in Unity).

Skinning: clothes / shoes / watch / skin: nearest-surface weight transfer from the player body meshes (Torso/Arms/Legs/Shoes/Head), smoothed;
head gear, hair, eyes, eyebrows, glasses, headset: rigid to head.x. Long-sleeve jackets: the arm skin hidden under the sleeve is
trimmed (it poked through the cloth).
Meshes are named Coach_<V>_<Slot> (Top, Pants, Shoes, Cap, Hair, Headset, Watch, Glasses, Head, Arms, Eyes): the Unity builder
assigns materials by that slot name.
"""
import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pix_common as pc
import coach_variants as cv
from mathutils import Vector, Matrix

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
argv = sys.argv[sys.argv.index("--") + 1:]
out_dir = argv[0]
variants = argv[1].split(",") if len(argv) > 1 and argv[1] else list(cv.VARIANTS)
prev_dir = argv[2] if len(argv) > 2 else None
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.open_mainfile(filepath=os.path.join(ROOT, cv.SOURCE_BLEND))
scene = bpy.context.scene

# ------------------------------------------------------------------ skeleton (metres, origin, identity)
src_arm = bpy.data.objects["root"]
src_arm.data.pose_position = 'REST'
src_arm.location.x = 0.0                      # the player rig was parked at x = -1.497 next to the coach row
bpy.context.view_layer.update()

src_names = ["Torso_GEO", "Arms_GEO", "Legs_GEO", "Shoes_GEO", "Head_GEO"]
src_copies = [pc.baked_copy(bpy.data.objects[n], "SRC_" + n) for n in src_names]
source = pc.build_source(src_copies)
for c in src_copies:
    bpy.data.objects.remove(c)

# clean armature object: data baked to world metres
arm_data = src_arm.data.copy()
arm = bpy.data.objects.new("Armature", arm_data)
scene.collection.objects.link(arm)
arm.matrix_world = src_arm.matrix_world.copy()
bpy.context.view_layer.update()
pc.bake_armature_transform(arm)
arm.data.name = "CoachArmature"
arm.show_in_front = True
bones = {b.name for b in arm.data.bones}
print("armature bones", len(bones), "hips z", arm.data.bones["root.x"].head_local.z)

def bake_part(name, slot, skin_override=None):
    o = bpy.data.objects.get(name)
    if o is None:
        print("MISSING kit object", name)
        return None
    for m in o.modifiers:
        if m.type == "SUBSURF":
            m.levels = 0 if slot in ("hair", "glasses", "headset", "watch") else max(m.levels, 1)
    c = pc.baked_copy(o, "tmp_" + name)
    c["slot"] = slot
    return c


def trim_arm_under_sleeves(arms):
    """Delete arm skin hidden by long sleeves: everything above the wrist stays only for the last ~40% of the forearm."""
    import bmesh
    me = arms.data
    names = {i: g.name for i, g in enumerate(arms.vertex_groups)}
    bm = bmesh.new(); bm.from_mesh(me)
    deform = bm.verts.layers.deform.verify()
    kill = []
    elbow = {s: Vector(arm.data.bones["forearm_stretch." + s].head_local) for s in "lr"}
    wrist = {s: Vector(arm.data.bones["hand." + s].head_local) for s in "lr"}
    for v in bm.verts:
        w = v[deform]
        if not w:
            continue
        dom = names[max(w.items(), key=lambda kv: kv[1])[0]]
        low = dom.lower()
        side = "l" if (low.endswith(".l") or low.endswith("_l")) else "r"
        if low.startswith(("hand", "c_index", "c_middle", "c_ring", "c_pinky", "c_thumb", "cc_", "index", "middle", "ring", "pinky", "thumb")):
            continue
        if low.startswith(("forearm", "leg", "foot")):
            e, wr = elbow[side], wrist[side]
            seg = wr - e
            s = (v.co - e).dot(seg) / max(seg.dot(seg), 1e-6)
            if s > 0.6:
                continue
        kill.append(v)
    bmesh.ops.delete(bm, geom=kill, context='VERTS')
    bm.to_mesh(me); bm.free()


def decimate(obj, ratio):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    m = obj.modifiers.new("dec", "DECIMATE")
    m.ratio = ratio
    bpy.ops.object.modifier_apply(modifier=m.name)
    print("decimated", obj.name, "->", len(obj.data.vertices))


SLOT_TO_NAME = {"head": "Head", "arms": "Arms", "eyes": "Eyes", "top": "Top", "pants": "Pants", "shoes": "Shoes", "cap": "Cap", "hair": "Hair",
                "headset": "Headset", "watch": "Watch", "glasses": "Glasses"}
RIGID_HEAD = {"cap", "hair", "headset", "glasses", "eyes"}
BODY_TRANSFER = {"top", "pants", "shoes", "watch"}
all_made = {}

for vk in variants:
    v = cv.VARIANTS[vk]
    n = "%02d" % v["head"]
    # remove any earlier variant objects
    parts = [("Coach_%s_Head_SubD01" % n, "head"), ("Coach_%s_Eyes" % n, "eyes"), ("Coach_Arms_Full", "arms")]
    parts += [(p.replace("{n}", n), s) for p, s in v["parts"]]
    long_sleeves = any(s == "top" and p not in ("Coach_Polo_Shirt", "Coach_TShirt") for p, s in v["parts"])
    baked = []
    for name, slot in parts:
        c = bake_part(name, slot)
        if c is None:
            continue
        if slot == "arms" and long_sleeves:
            trim_arm_under_sleeves(c)
        if len(c.data.vertices) > 25000:
            decimate(c, 25000.0 / len(c.data.vertices))
        baked.append(c)

    # ---- weights
    for c in baked:
        slot = c["slot"]
        if slot in RIGID_HEAD:
            pc.bind_rigid(c, "head.x")
        elif slot == "head":
            w = pc.transfer_weights(c, source, restrict=bones)
            pc.apply_weights(c, w, 4, 1, 0.4)
        else:
            w = pc.transfer_weights(c, source, restrict=bones)
            smooth = 3 if slot in ("top", "pants") else 1
            pc.apply_weights(c, w, 4, smooth, 0.5)

    # ---- join per slot, name, parent to a fresh armature copy
    by_slot = {}
    for c in baked:
        by_slot.setdefault(c["slot"], []).append(c)
    var_arm = arm.copy()
    var_arm.data = arm.data.copy()
    var_arm.name = "Coach_%s_Rig" % vk
    scene.collection.objects.link(var_arm)
    joined = []
    for slot, objs in by_slot.items():
        bpy.ops.object.select_all(action='DESELECT')
        for o in objs:
            o.select_set(True)
        bpy.context.view_layer.objects.active = objs[0]
        if len(objs) > 1:
            bpy.ops.object.join()
        j = bpy.context.view_layer.objects.active
        j.name = "Coach_%s_%s" % (vk, SLOT_TO_NAME[slot])
        j.data.name = j.name
        # materials: one slot named after the part, UVs preserved
        j.data.materials.clear()
        m = bpy.data.materials.new("PIX_Coach_" + SLOT_TO_NAME[slot])
        j.data.materials.append(m)
        for p in j.data.polygons:
            p.material_index = 0
        pc.add_armature_modifier(j, var_arm)
        joined.append(j)
    all_made[vk] = (var_arm, joined)

    # ---- export
    for o in bpy.data.objects:
        o.select_set(False)
    objs = [var_arm] + joined
    fbx = os.path.join(out_dir, "PIX_Coach_%s.fbx" % vk)
    pc.export_fbx(fbx, objs)
    print("EXPORTED", fbx, "verts", sum(len(j.data.vertices) for j in joined))

    # ---- preview: bind pose + a raised-arm pose test (deformation check)
    if prev_dir:
        os.makedirs(prev_dir, exist_ok=True)
        for o in bpy.data.objects:
            if o.type == "MESH" and o not in joined:
                o.hide_render = True
        var_arm.hide_render = True
        for j in joined:
            j.hide_render = False
        pc.render_preview(joined, os.path.join(prev_dir, "coach_%s_bind.png" % vk), views=("front", "three_quarter"), size=(700, 900), center=Vector((0, 0, 0.95)), ortho_scale=2.2,
                          colors={j.name: {"Top": (0.1, 0.3, 0.8, 1), "Pants": (0.15, 0.15, 0.2, 1), "Shoes": (0.9, 0.9, 0.9, 1), "Cap": (0.8, 0.2, 0.2, 1), "Hair": (0.25, 0.15, 0.08, 1),
                                           "Headset": (0.05, 0.05, 0.05, 1), "Watch": (0.7, 0.7, 0.2, 1), "Glasses": (0.1, 0.1, 0.1, 1), "Head": (0.85, 0.6, 0.45, 1), "Arms": (0.85, 0.6, 0.45, 1), "Eyes": (1, 1, 1, 1)}.get(j.name.split("_", 2)[2], (0.45, 0.3, 0.15, 1)) for j in joined})
        for o in bpy.data.objects:
            if o.type == 'MESH':
                o.hide_render = True
    # park this variant before the next one (kit objects must stay visible so their modifiers keep evaluating)
    var_arm.hide_viewport = True; var_arm.hide_render = True
    for j in joined:
        j.hide_viewport = True; j.hide_render = True
