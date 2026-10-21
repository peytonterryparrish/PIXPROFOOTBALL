"""Re-bake the owner's BGS referee gesture / locomotion actions onto the clean 95-bone referee deform skeleton.

    blender -b --python Tools/Blender/referee_rebake.py -- <out.fbx>

Why: the shipped Referee.fbx only converts ~6 of its 35 clips to valid Humanoid motion (the rest import with a constant root, scale 1e8
curves). The same actions live intact in 'Studio33 Referee GESTURES.blend' on the full 380-bone Auto-Rig-Pro rig ('Referee Rig.001').
The deform rig used by the skinned mesh (Referee2.fbx, 95 bones, hips lowered to 0.902 m) has the same bone names, so each
frame we transfer the world-space rotation DELTA (pose vs rest) of every deform bone, and the hips displacement, onto the clean rig
(bone rolls / rest lengths may differ, rotation deltas stay valid), key it at 30 fps and export armature + actions as FBX.
"""
import bpy, sys, os, math
from mathutils import Matrix, Vector, Quaternion

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
YAN = os.path.join(ROOT, "_AssetSource", "DraftFever", "3D", "Characters", "Players", "Animations Files", "Yan Files")
GESTURES = os.path.join(YAN, "Blender Files unorganized", "Studio33 Referee GESTURES.blend")
TARGET_FBX = os.path.join(YAN, "FBXs backup", "Referee2.fbx")
out_fbx = sys.argv[sys.argv.index("--") + 1]

bpy.ops.wm.open_mainfile(filepath=GESTURES)
src = bpy.data.objects["Referee Rig.001"]
for o in list(bpy.data.objects):
    if o.type == 'MESH':
        bpy.data.objects.remove(o)
ad = src.animation_data
for t in ad.nla_tracks:
    t.mute = True
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=TARGET_FBX)
new = [o for o in bpy.data.objects if o not in before]
tgt = [o for o in new if o.type == 'ARMATURE'][0]
target_meshes = []
for o in new:
    if o.type == "MESH":
        if "_02_" in o.name or not os.environ.get("CHECK_DIR"):
            bpy.data.objects.remove(o)
        else:
            target_meshes.append(o)
for o in target_meshes:                      # bake the cm-under-0.01-parent transform into the mesh (metres, identity)
    o.data.transform(o.matrix_world)
    o.parent = None
    o.matrix_world = Matrix.Identity(4)
tgt.name = "TargetRig"
tgt.data.transform(tgt.matrix_world)       # cm -> metres, identity object transform
tgt.matrix_world = Matrix.Identity(4)
for o in target_meshes:
    o.parent = tgt
    o.matrix_parent_inverse = Matrix.Identity(4)
bpy.context.view_layer.update()
for pb in tgt.pose.bones:
    pb.rotation_mode = 'QUATERNION'

# bone order (parents first)
order = []
def walk(b):
    order.append(b)
    for c in b.children:
        walk(c)
for b in tgt.data.bones:
    if b.parent is None:
        walk(b)
names = [b.name for b in order]
missing = [n for n in names if n not in src.data.bones]
print("target bones", len(names), "missing in source", missing)

S_rest = {n: src.matrix_world @ src.data.bones[n].matrix_local for n in names if n in src.data.bones}
T_rest = {b.name: b.matrix_local.copy() for b in order}
S_hips_rest = S_rest["root.x"].translation.copy()
T_hips_rest = T_rest["root.x"].translation.copy()
print("hips rest src", S_hips_rest, "tgt", T_hips_rest)

todo = [a for a in bpy.data.actions if a.name.startswith("BGS_Ref_") and "DELETE" not in a.name]
print("actions", len(todo))
made = []
scene = bpy.context.scene
scene.render.fps = 30
tgt.animation_data_create()

def clean(n):
    n = n.replace("#", "").replace("BGS_", "")
    return n.lower()

for act in todo:
    ad.action = act
    try:
        ad.action_slot = act.slots[0]
    except Exception:
        pass
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    new_act = bpy.data.actions.new(clean(act.name))
    tgt.animation_data.action = new_act
    try:
        tgt.animation_data.action_slot = new_act.slots[0]
    except Exception:
        pass
    prev_q = {}
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        bpy.context.view_layer.update()
        sp = {n: src.matrix_world @ src.pose.bones[n].matrix for n in names}
        pose = {}
        for b in order:
            n = b.name
            par = b.parent
            P = pose[par.name] if par else Matrix.Identity(4)
            restoff = (T_rest[par.name].inverted() @ T_rest[n]) if par else T_rest[n]
            natural = P @ restoff
            Rdelta = sp[n].to_3x3().normalized() @ S_rest[n].to_3x3().normalized().inverted()
            Rd = (Rdelta @ T_rest[n].to_3x3()).to_quaternion().to_matrix()
            if n == "root.x":
                head = T_hips_rest + (sp[n].translation - S_hips_rest)
            else:
                head = natural.translation
            Dm = Matrix.Translation(head) @ Rd.to_4x4()
            pose[n] = Dm
            basis = natural.inverted() @ Dm
            loc, rot, _ = basis.decompose()
            pb = tgt.pose.bones[n]
            if n in prev_q:
                rot.make_compatible(prev_q[n])
            prev_q[n] = rot.copy()
            pb.rotation_quaternion = rot
            pb.keyframe_insert("rotation_quaternion", frame=f)
            if n == "root.x":
                pb.location = loc
                pb.keyframe_insert("location", frame=f)
    new_act.use_fake_user = True
    new_act.frame_start, new_act.frame_end = f0, f1
    new_act.use_frame_range = True
    made.append((new_act.name, f0, f1))
    print("BAKED", new_act.name, f0, f1)

CHECK = os.environ.get("CHECK_DIR")
if CHECK:
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    import pix_common as pc
    files = []
    os.makedirs(CHECK, exist_ok=True)
    for name in ("ref_gesture_touchdown", "ref_gesture_firstdown", "ref_gesture_incompletepass", "ref_gesture_timeout", "ref_gesture_whistle_01", "ref_walkf_loop"):
        a = bpy.data.actions[name]
        tgt.animation_data.action = a
        try:
            tgt.animation_data.action_slot = a.slots[0]
        except Exception:
            pass
        f0, f1 = int(a.frame_range[0]), int(a.frame_range[1])
        for i, f in enumerate((f0, (f0 + f1) // 2, f1)):
            scene.frame_set(f); bpy.context.view_layer.update()
            files += pc.render_preview(target_meshes, os.path.join(CHECK, "r_%s_%d.png" % (name, i)), views=("front",), size=(250, 400), center=Vector((0, 0, 0.9)), ortho_scale=2.2)
    pc.montage(files, os.path.join(CHECK, "rebake_check.png"), 6)

tgt.animation_data.action = None
for pb in tgt.pose.bones:
    pb.location = (0, 0, 0)
    pb.rotation_quaternion = (1, 0, 0, 0)
# drop the source rig + every action that is not a re-baked clip so only the clean armature + 34 takes are exported
for a in list(bpy.data.actions):
    if a.name not in {m[0] for m in made}:
        bpy.data.actions.remove(a)
bpy.data.objects.remove(src)
for o in target_meshes:
    bpy.data.objects.remove(o)
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pix_common as pc_dummy
dummy = pc_dummy.add_dummy_mesh(tgt)
bpy.ops.object.select_all(action="DESELECT")
tgt.select_set(True)
dummy.select_set(True)
bpy.context.view_layer.objects.active = tgt
os.makedirs(os.path.dirname(out_fbx), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
                         axis_forward='-Z', axis_up='Y', object_types={"ARMATURE", "MESH"}, add_leaf_bones=False, bake_anim=True,
                         bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                         bake_anim_step=1.0, bake_anim_simplify_factor=0.0, path_mode='STRIP')
print("EXPORTED", out_fbx, len(made))
