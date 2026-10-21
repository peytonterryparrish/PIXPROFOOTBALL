"""Sanity render: apply the re-baked referee clips (Referee_Gestures.fbx) to the skinned referee (Referee_Rigged.fbx) in Blender.
blender -b --python check_referee_baked.py -- <gestures.fbx> <rigged.fbx> <out_dir> [action1,action2,...]"""
import bpy, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pix_common as pc
from mathutils import Vector, Matrix
argv = sys.argv[sys.argv.index("--") + 1:]
ges, rig, out = argv[0], argv[1], argv[2]
want = argv[3].split(",") if len(argv) > 3 else ["ref_gesture_touchdown", "ref_gesture_firstdown", "ref_gesture_incompletepass", "ref_gesture_timeout", "ref_gesture_whistle_01", "ref_walkf_loop"]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=rig)
body = [o for o in bpy.data.objects if o.type == 'MESH']
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
for o in body:
    if "_02_" in o.name: bpy.data.objects.remove(o)
body = [o for o in bpy.data.objects if o.type == 'MESH']
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=ges)
garm = [o for o in bpy.data.objects if o not in before and o.type == 'ARMATURE'][0]
print("actions", [a.name for a in bpy.data.actions][:10])
print("rig arm scale", tuple(arm.scale), "ges arm scale", tuple(garm.scale), "rot", tuple(garm.rotation_euler))
arm.animation_data_create()
files = []
os.makedirs(out, exist_ok=True)
for name in want:
    act = [a for a in bpy.data.actions if a.name.endswith(name)][0]
    arm.animation_data.action = act
    try:
        arm.animation_data.action_slot = act.slots[0]
    except Exception as e:
        print("slot", e)
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    for i, f in enumerate((f0, (f0 + f1) // 2, f1)):
        bpy.context.scene.frame_set(f); bpy.context.view_layer.update()
        files += pc.render_preview(body, os.path.join(out, "chk_%s_%d.png" % (name, i)), views=("front",), size=(250, 400), center=Vector((0, 0, 0.9)), ortho_scale=2.2)
pc.montage(files, os.path.join(out, "chk_sheet.png"), 6)
