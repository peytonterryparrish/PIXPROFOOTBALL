"""Authored (keyframed) locomotion / pre-snap clips on the clean player deform skeleton, exported as PIX_Authored_Anims.fbx.

    blender -b --python Tools/Blender/build_locomotion.py -- <out_dir> [preview_dir] [only_action]

Walk and stroll are cycles built from foot paths (a planted foot is exactly stationary in the world, the pelvis bobs and sways, shoulders counter-rotate,
arms swing opposite to the legs); the huddle stand is a hands-on-knees listening pose. Everything is solved with the 2-bone IK of pix_poses on the FK
bones (character faces -Y, left = +X, metres) so Unity only sees plain bone rotations and retargets them as Humanoid muscles.
Clips (30 fps): PIX_Walk_Cycle, PIX_Stroll_Cycle, PIX_Huddle_Knees, PIX_Huddle_Hips.
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
PB, BN = pp.PB, pp.BN
reset, upd, rest, head, key, new_action, lean, arm_ik, leg_ik, move_root, aim = pp.reset, pp.upd, pp.rest, pp.head, pp.key, pp.new_action, pp.lean, pp.arm_ik, pp.leg_ik, pp.move_root, pp.aim

H0 = rest("root.x").z                        # standing pelvis height
ANKLE = rest("foot.l").z                     # flat-foot ankle height
FOOT_X = rest("foot.l").x                    # lateral foot offset
SHOULDER_Z = rest("arm_stretch.l").z


def twist(names, angles):
    """Rotate the bones about the vertical axis at their heads (degrees, + = counter-clockwise seen from above)."""
    for n, a in zip(names, angles):
        pb = PB[n]
        h = pb.head.copy()
        R = Matrix.Rotation(math.radians(a), 4, 'Z')
        pb.matrix = Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ pb.matrix
    upd()


def foot_pitch(side, toe_up_deg):
    """Pitch the (already flat) foot about its ankle: positive = toes up."""
    s = "l" if side > 0 else "r"
    pb = PB["foot." + s]
    h = pb.head.copy()
    R = Matrix.Rotation(math.radians(-toe_up_deg), 4, 'X')
    pb.matrix = Matrix.Translation(h) @ R @ Matrix.Translation(-h) @ pb.matrix
    upd()


def smooth(x):
    return 0.5 - 0.5 * math.cos(math.pi * max(0.0, min(1.0, x)))


# ------------------------------------------------------------------ walk
def walk_pose(t, step, stance=0.6, lift=0.085, bob=0.024, arm_amp=0.17, lean_deg=3.0, drop=0.05):
    """t in [0,1] over one full cycle (two steps). step = step length (m); the pelvis travels 2*step per cycle along -Y."""
    D = 2.0 * step
    reset()
    zlow = H0 - drop
    z = zlow + bob * (0.5 - 0.5 * math.cos(4 * math.pi * (t - 0.05)))
    sway = 0.022 * math.sin(2 * math.pi * (t - 0.05))
    move_root((sway, -D * t, z - H0))
    # torso: slight forward lean, counter-rotation of the shoulders against the pelvis
    pel_yaw = 4.5 * math.sin(2 * math.pi * t)                      # pelvis yaw follows the forward leg
    twist(["root.x"], [pel_yaw])
    lean(["spine_01.x", "spine_02.x", "spine_03.x"], [lean_deg * 0.4, lean_deg * 0.3, lean_deg * 0.3])
    twist(["spine_01.x", "spine_02.x", "spine_03.x"], [-pel_yaw * 0.9, -pel_yaw * 0.8, -pel_yaw * 0.5])
    lean(["neck.x", "head.x"], [-lean_deg * 0.5, -lean_deg * 0.5])
    # legs: planted foot stays fixed in the world, swing foot arcs forward
    for side in (+1, -1):
        u = (t + (0.0 if side > 0 else 0.5)) % 1.0
        if u < stance:
            f = 0.6 * step - (u / stance) * 1.2 * step
            lz = 0.0
            pitch = (12.0 * (1.0 - u / 0.12) if u < 0.12 else 0.0) - (28.0 * ((u - 0.45) / 0.15) if u > 0.45 else 0.0)   # heel strike toes up, then toe-off pitch
        else:
            w = (u - stance) / (1.0 - stance)
            f = -0.6 * step + 1.2 * step * smooth(w)
            lz = lift * math.sin(math.pi * w) ** 0.8
            pitch = -28.0 * (1.0 - w * 3.0) if w < 0.33 else (10.0 * (w - 0.6) / 0.4 if w > 0.6 else 0.0)
        x = FOOT_X * (1 if side > 0 else -1)
        tgt = Vector((x, -D * t - f, ANKLE + lz))
        leg_ik(side, tgt, (0, -1, 0))
        foot_pitch(side, pitch)
    # arms swing against the legs
    for side in (+1, -1):
        sw = -side * arm_amp * math.cos(2 * math.pi * t)       # left arm back when the left leg is forward (t = 0)
        shx = rest("arm_stretch.l").x * side if side > 0 else rest("arm_stretch.r").x
        tgt = Vector((side * 0.255 + sway * 0.4, -D * t - sw, SHOULDER_Z - 0.50 + 0.012 * math.cos(4 * math.pi * t)))
        arm_ik(side, tgt, Vector((0.35 * side, 1.0, -0.35)))
    lean(["neck.x", "head.x"], [0.0, 0.0])


def build_walk(name, step, stance, lift, arm_amp, lean_deg, frames=32, drop=0.05, bob=0.024):
    act = new_action(name)
    for f in range(frames + 1):
        walk_pose(f / frames, step, stance=stance, lift=lift, arm_amp=arm_amp, lean_deg=lean_deg, drop=drop, bob=bob)
        key(1 + f)
    return act, 1, 1 + frames


# ------------------------------------------------------------------ huddle
def pose_huddle_knees(t):
    reset()
    breathe = math.sin(2 * math.pi * t)
    drop = 0.10
    move_root((0.0, -0.08, -drop))
    lean(["spine_01.x", "spine_02.x", "spine_03.x"], [30 + 1.2 * breathe, 36 + 1.0 * breathe, 38 + 0.8 * breathe])
    lean(["neck.x", "head.x"], [18 + 2.0 * math.sin(2 * math.pi * (t + 0.2)), 8])
    for side in (+1, -1):
        x = (FOOT_X + 0.07) * (1 if side > 0 else -1)
        leg_ik(side, Vector((x, -0.05, ANKLE)), (0, -1, 0))
    for side in (+1, -1):
        s = "l" if side > 0 else "r"
        knee = head("leg_stretch." + s)
        arm_ik(side, knee + Vector((0.0, -0.02, 0.085)), Vector((side * 1.0, 0.35, -0.15)))
    twist(["spine_03.x", "neck.x", "head.x"], [4 * math.sin(2 * math.pi * t), 6 * math.sin(2 * math.pi * (t + 0.3)), 3 * math.sin(2 * math.pi * (t + 0.3))])


def build_huddle_knees():
    act = new_action("PIX_Huddle_Knees")
    n = 96
    for f in range(0, n + 1, 12):
        pose_huddle_knees(f / n)
        key(1 + f)
    return act, 1, 1 + n


def pose_huddle_hips(t):
    reset()
    br = math.sin(2 * math.pi * t)
    move_root((0.005 * br, 0, -0.01))
    lean(["spine_01.x", "spine_02.x", "spine_03.x"], [2 + 0.6 * br, 1.5 + 0.5 * br, 1.5])
    for side in (+1, -1):
        leg_ik(side, Vector((side * (FOOT_X + 0.05), -0.03, ANKLE)), (0, -1, 0))
    pp.hands_on_hips()
    lean(["neck.x", "head.x"], [-2.0 + 1.0 * math.sin(2 * math.pi * (t + 0.25)), 2.0])
    twist(["neck.x", "head.x"], [8 * math.sin(2 * math.pi * t), 10 * math.sin(2 * math.pi * t)])


def build_huddle_hips():
    act = new_action("PIX_Huddle_Hips")
    n = 120
    for f in range(0, n + 1, 15):
        pose_huddle_hips(f / n)
        key(1 + f)
    return act, 1, 1 + n


BUILDERS = {
    "PIX_Walk_Cycle": lambda: build_walk("PIX_Walk_Cycle", 0.74, 0.6, 0.085, 0.17, 3.0),
    "PIX_Stroll_Cycle": lambda: build_walk("PIX_Stroll_Cycle", 0.50, 0.66, 0.06, 0.10, 1.5, frames=36, drop=0.035, bob=0.016),
    "PIX_Huddle_Knees": build_huddle_knees,
    "PIX_Huddle_Hips": build_huddle_hips,
}

actions = {}
for name, fn in BUILDERS.items():
    if only and name != only:
        continue
    act, f0, f1 = fn()
    act.frame_start, act.frame_end = f0, f1
    act.use_frame_range = True
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

if prev_dir:
    os.makedirs(prev_dir, exist_ok=True)
    colors = {b.name: (0.8, 0.6, 0.5, 1) for b in bodies}
    arm.hide_render = True
    for name, (act, f0, f1) in actions.items():
        arm.animation_data.action = act
        files = []
        n = 8
        for i in range(n):
            f = int(f0 + (f1 - f0) * i / n)
            scene.frame_set(f)
            upd()
            files += pc.render_preview(bodies, os.path.join(prev_dir, "loco_%s_%d.png" % (name, i)), views=("front", "side"), size=(240, 380), center=Vector((0, 0, 0.85)), ortho_scale=2.0, colors=colors)
        files = files[0::2] + files[1::2]
        pc.montage(files, os.path.join(prev_dir, "loco_%s.png" % name), n)
        for f in files:
            os.remove(f)
    arm.animation_data.action = None

for b in bodies:
    bpy.data.objects.remove(b)
arm.hide_render = False
reset()
scene.frame_start, scene.frame_end = 1, 130
for act, f0, f1 in actions.values():
    act.use_fake_user = True
fbx = os.path.join(out_dir, "PIX_Authored_Anims.fbx")
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
