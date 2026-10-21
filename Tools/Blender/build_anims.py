"""Keyframed (procedural) Humanoid clips on the clean player deform skeleton, exported as Coach_Anims.fbx.

    blender -b --python Tools/Blender/build_anims.py -- <out_dir> [preview_dir] [only_action]

Poses are authored with 2-bone IK solves (hand / foot targets in metres, armature space, character faces -Y, left = +X) and
keyed on the FK bones, so the FBX only carries plain bone rotations (Unity re-targets them as Humanoid muscles).
Clips (30 fps): ref_idle, ref_spot_ball, ref_penalty_flag, coach_headset_idle, coach_clipboard_idle, coach_clap.
(The real referee signal clips come from Referee_Gestures.fbx.)
"""
import bpy, sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pix_common as pc
import coach_variants as cv
from mathutils import Vector, Matrix, Quaternion

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
argv = sys.argv[sys.argv.index("--") + 1:]
out_dir = argv[0]
prev_dir = argv[1] if len(argv) > 1 and argv[1] else None
only = argv[2] if len(argv) > 2 else None
os.makedirs(out_dir, exist_ok=True)

import pix_poses as pp
arm, bodies = pc.load_clean_coach_rig(os.path.join(ROOT, cv.SOURCE_BLEND))
scene = bpy.context.scene
scene.render.fps = 30
pp.init(arm)
upd, reset, BUILDERS = pp.upd, pp.reset, pp.BUILDERS

actions = {}
for name, fn in BUILDERS.items():
    if only and name != only:
        continue
    act, f0, f1 = fn()
    act.frame_start, act.frame_end = f0, f1
    act.use_frame_range = True
    # smooth interpolation
    try:
        for layer in act.layers:
            for strip in layer.strips:
                for cb in strip.channelbags:
                    for fc in cb.fcurves:
                        for kp in fc.keyframe_points:
                            kp.interpolation = 'BEZIER'
                            kp.handle_left_type = kp.handle_right_type = 'AUTO_CLAMPED'
    except Exception as e:
        print("interp warn", e)
    act.use_fake_user = True
    actions[name] = (act, f0, f1)
    print("ACTION", name, f0, f1)

arm.animation_data.action = None

# ------------------------------------------------------------------ preview contact sheets
if prev_dir:
    os.makedirs(prev_dir, exist_ok=True)
    colors = {b.name: (0.8, 0.6, 0.5, 1) for b in bodies}
    arm.hide_render = True
    for name, (act, f0, f1) in actions.items():
        arm.animation_data.action = act
        files = []
        n = 6
        for i in range(n):
            f = int(f0 + (f1 - f0) * i / (n - 1))
            scene.frame_set(f)
            upd()
            files += pc.render_preview(bodies, os.path.join(prev_dir, "anim_%s_%d.png" % (name, i)), views=("front", "side"), size=(300, 420), center=Vector((0, 0, 0.85)), ortho_scale=2.0, colors=colors)
        files = files[0::2] + files[1::2]
        pc.montage(files, os.path.join(prev_dir, "anim_%s.png" % name), 6)
        for f in files:
            os.remove(f)
    arm.animation_data.action = None

# ------------------------------------------------------------------ export (armature + actions only)
for b in bodies:
    bpy.data.objects.remove(b)
arm.hide_render = False
reset()
scene.frame_start, scene.frame_end = 1, 130
for act, f0, f1 in actions.values():
    act.use_fake_user = True
fbx = os.path.join(out_dir, "Coach_Anims.fbx")
dummy = pc.add_dummy_mesh(arm)
bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
dummy.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, global_scale=1.0, apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE",
                         axis_forward="-Z", axis_up="Y", object_types={"ARMATURE", "MESH"}, add_leaf_bones=False, bake_anim=True,
                         bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                         bake_anim_step=1.0, bake_anim_simplify_factor=0.0, path_mode='STRIP')
print("EXPORTED", fbx)
import json
clips = []
for n, recs in pp.TARGETS.items():
    clips.append({"name": n, "keys": [{"frame": r["frame"], "l": sum([r[n] for n in ("arm_stretch.l", "forearm_stretch.l", "hand.l", "c_index1.l", "c_middle1.l", "c_pinky1.l")], []),
                                       "r": sum([r[n] for n in ("arm_stretch.r", "forearm_stretch.r", "hand.r", "c_index1.r", "c_middle1.r", "c_pinky1.r")], [])} for r in recs]})
with open(os.path.join(out_dir, "Coach_ArmTargets.json"), "w") as fh:
    json.dump({"clips": clips}, fh)
