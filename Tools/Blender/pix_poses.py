"""Pose / clip authoring helpers shared by build_anims.py and build_coach.py (inside Blender).
Armature space = world (the clean rig sits at the origin), character faces -Y, its left side is +X, units are metres."""
import bpy, math
from mathutils import Vector, Matrix, Quaternion

arm = None
PB = None
BN = None
KEYED = []


def init(a):
    global arm, PB, BN, KEYED
    arm = a
    PB = a.pose.bones
    BN = a.data.bones
    for pb in PB:
        pb.rotation_mode = 'QUATERNION'
    KEYED = [b.name for b in BN if not b.name.startswith(("cc_",)) and b.name != "c_traj"]


def upd():
    bpy.context.view_layer.update()


def rest(name):
    return Vector(BN[name].head_local)


def reset():
    for pb in PB:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.scale = (1, 1, 1)
    upd()


def head(name):
    return Vector(PB[name].head)


def tail(name):
    return Vector(PB[name].tail)


def aim(name, direction):
    pb = PB[name]
    cur = (pb.tail - pb.head).normalized()
    d = Vector(direction).normalized()
    q = cur.rotation_difference(d)
    h = pb.head.copy()
    pb.matrix = Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ pb.matrix
    upd()


def move_root(delta):
    pb = PB["root.x"]
    pb.matrix = Matrix.Translation(Vector(delta)) @ pb.matrix
    upd()


def lean(chain, angles):
    """Rotate each bone of chain about the lateral axis relative to its rest direction (positive = forward, i.e. -Y)."""
    for name, a in zip(chain, angles):
        rd = (Vector(BN[name].tail_local) - Vector(BN[name].head_local)).normalized()
        c, s = math.cos(math.radians(a)), math.sin(math.radians(a))
        d = Vector((rd.x, rd.y * c - rd.z * s, rd.y * s + rd.z * c))
        aim(name, d)


def rest_hinge(upper, lower, end):
    """Axis the Unity Humanoid elbow really flexes about for this rig in the rest pose (measured with HumanPoseHandler on PIX_Coach_A:
    Left Forearm Stretch bends about Unity (0.70,-0.29,0.65) = Blender (-0.70,-0.65,-0.29); mirrored for the right arm). It is NOT the
    normal of the rest arm plane (55 degrees off)."""
    if upper.endswith(".l"):
        return Vector((0.70, 0.65, 0.29)).normalized()      # right-handed axis of a POSITIVE (flexing) Forearm Stretch
    if upper.endswith(".r"):
        return Vector((0.70, -0.65, -0.29)).normalized()
    u = (rest(lower) - rest(upper))
    f = (rest(end) - rest(lower))
    n = u.cross(f)
    return n.normalized() if n.length > 1e-5 else None


def roll_to_hinge(upper, lower, end, upper_dir, fore_dir):
    """Humanoid forearms only flex about a hinge fixed in the upper arm frame (and only in the flexing direction): twist the upper arm
    about its own axis so that the target forearm direction lies in the plane perpendicular to that hinge and the flexion angle is positive."""
    H0 = rest_hinge(upper, lower, end)
    if H0 is None:
        return
    pb = PB[upper]
    u = upper_dir.normalized()
    f1 = fore_dir.normalized()
    R = pb.matrix.to_3x3() @ BN[upper].matrix_local.to_3x3().inverted()
    H = R @ H0
    F0 = R @ (rest(end) - rest(lower)).normalized()
    a = f1.dot(H - u * u.dot(H))
    b = f1.dot(u.cross(H))
    c = f1.dot(u) * u.dot(H)
    r = math.hypot(a, b)
    cands = []
    if r > 1e-6:
        base = math.atan2(b, a)
        k = max(-1.0, min(1.0, -c / r))
        d = math.acos(k)
        cands = [base + d, base - d]
    best = None
    for phi in cands:
        Rp = Matrix.Rotation(phi, 3, u)
        Hp, Fp = Rp @ H, Rp @ F0
        theta = math.atan2(Hp.dot(Fp.cross(f1)), Fp.dot(f1))
        score = (0 if 0 <= theta <= math.radians(150) else 10) + abs(((phi + math.pi) % (2 * math.pi)) - math.pi) * 0.1
        if best is None or score < best[0]:
            best = (score, phi)
    if best is None:
        return
    hd = pb.head.copy()
    pb.matrix = Matrix.Translation(hd) @ Matrix.Rotation(best[1], 4, u) @ Matrix.Translation(-hd) @ pb.matrix
    upd()


def two_bone(upper, lower, end, target, pole, end_dir=None, hinge=False):
    """Solve a 2-bone chain so that bone `end`'s head reaches target; elbow/knee bends toward pole (a direction)."""
    S = head(upper)
    a = (rest(lower) - rest(upper)).length
    b = (rest(end) - rest(lower)).length
    T = Vector(target)
    d = (T - S)
    dist = min(max(d.length, abs(a - b) + 1e-3), a + b - 1e-3)
    dirn = d.normalized()
    x = (a * a - b * b + dist * dist) / (2 * dist)
    h = math.sqrt(max(a * a - x * x, 0.0))
    p = Vector(pole)
    p = (p - dirn * p.dot(dirn))
    p = p.normalized() if p.length > 1e-6 else Vector((0, 0, 1))
    elbow = S + dirn * x + p * h
    aim(upper, elbow - S)
    if hinge:
        roll_to_hinge(upper, lower, end, elbow - S, (S + dirn * dist) - elbow)
    aim(lower, S + dirn * dist - head(lower))
    if end_dir is not None:
        aim(end, end_dir)


def arm_ik(side, target, pole, hand_dir=None):
    s = "l" if side > 0 else "r"
    two_bone("arm_stretch." + s, "forearm_stretch." + s, "hand." + s, target, pole, hand_dir, hinge=True)


def leg_ik(side, target=None, pole=(0, -1, 0)):
    s = "l" if side > 0 else "r"
    t = target if target is not None else rest("foot." + s)
    two_bone("thigh_stretch." + s, "leg_stretch." + s, "foot." + s, t, pole)
    # keep the foot flat in its rest orientation
    PB["foot." + s].matrix = BN["foot." + s].matrix_local.copy()
    upd()


TARGETS = {}      # action name -> [{frame, bone: [x, y, z] (Blender world metres)}], consumed by the Unity arm fitter (ArmFit.cs)
CURRENT = [None]
ARM_BONES = ["arm_stretch.l", "forearm_stretch.l", "hand.l", "c_index1.l", "c_middle1.l", "c_pinky1.l", "arm_stretch.r", "forearm_stretch.r", "hand.r", "c_index1.r", "c_middle1.r", "c_pinky1.r"]


def key(frame):
    rec = {"frame": frame}
    for n in ARM_BONES:
        rec[n] = [round(v, 4) for v in PB[n].head]
    TARGETS.setdefault(CURRENT[0], []).append(rec)
    for n in KEYED:
        pb = PB[n]
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        if n in ("root.x",):
            pb.keyframe_insert("location", frame=frame)


def new_action(name):
    act = bpy.data.actions.new(name)
    CURRENT[0] = name
    arm.animation_data_create()
    arm.animation_data.action = act
    reset()
    return act


def sway(t, amp=1.0):
    return math.sin(t * 2 * math.pi) * amp


# ------------------------------------------------------------------ standing helpers
HIP_L = Vector((0.23, 0.0, 1.03))      # wrist target for hands-on-hips (left); mirror x for right
POLE_OUT = Vector((1.0, 0.45, 0.0))


def hands_on_hips(sx=0.0):
    arm_ik(+1, HIP_L + Vector((sx, 0, 0)), POLE_OUT)
    arm_ik(-1, Vector((-HIP_L.x + sx, HIP_L.y, HIP_L.z)), Vector((-1.0, 0.45, 0.0)))


def pose_ref_idle(t):
    reset()
    lean(["spine_01.x", "spine_02.x", "spine_03.x"], [1.0 + 1.0 * sway(t), 0.5 + 0.8 * sway(t), 0.5 + 0.6 * sway(t)])
    lean(["neck.x", "head.x"], [-1.0, -0.5 + 0.8 * sway(t)])
    hands_on_hips()
    # slow look-around
    PB["head.x"].rotation_quaternion = PB["head.x"].rotation_quaternion  # (head stays; turn applied via spine twist below)
    upd()


def build_ref_idle():
    act = new_action("ref_idle")
    n = 90
    for f in (0, 30, 60, 90):
        pose_ref_idle(f / n)
        key(1 + f)
    return act, 1, 91


def pose_headset(t):
    reset()
    sh = 0.012 * sway(t)
    move_root((sh, 0, 0))
    lean(["spine_01.x", "spine_02.x", "spine_03.x"], [1.0, 0.5, 0.5 + 0.5 * sway(t)])
    leg_ik(+1); leg_ik(-1)
    # right hand on the headset (the left arm keeps holding the clipboard of coach A, same arm pose as coach_clipboard_idle)
    ear = Vector((-0.16, 0.0, 1.60))
    arm_ik(-1, ear + Vector((0, 0, 0.01 * sway(t))), Vector((-0.6, -0.5, -1.0)), Vector((0.1, 0.25, 1.0)))
    arm_ik(+1, Vector((0.10, -0.22, 1.22)), Vector((0.7, 0.2, -1.0)))
    lean(["neck.x", "head.x"], [-1.0, -1.0 + 1.5 * sway(t)])


def build_headset_idle():
    act = new_action("coach_headset_idle")
    n = 120
    for f in (0, 30, 60, 90, 120):
        pose_headset(f / n)
        key(1 + f)
    return act, 1, 121


def pose_clipboard(t, tick=0.0):
    reset()
    move_root((0.01 * sway(t), 0, 0))
    lean(["spine_01.x", "spine_02.x", "spine_03.x"], [3.0, 2.0, 3.0 + 0.5 * sway(t)])
    leg_ik(+1); leg_ik(-1)
    # left forearm holds the clipboard across the belly, right hand "writes" on it
    arm_ik(+1, Vector((0.10, -0.22, 1.22)), Vector((0.7, 0.2, -1.0)))
    arm_ik(-1, Vector((-0.08 + 0.03 * tick, -0.22, 1.24 + 0.02 * sway(t * 4))), Vector((-0.7, 0.2, -1.0)))
    lean(["neck.x", "head.x"], [8.0, 4.0 + 1.5 * sway(t)])


def build_clipboard_idle():
    act = new_action("coach_clipboard_idle")
    n = 120
    for f in range(0, 121, 15):
        pose_clipboard(f / n, tick=math.sin(f / 15.0 * math.pi))
        key(1 + f)
    return act, 1, 121


def pose_clap(t, together):
    reset()
    bounce = 0.015 * together
    move_root((0, 0, -bounce))
    lean(["spine_01.x", "spine_02.x", "spine_03.x"], [2.0, 2.0, 2.0])
    leg_ik(+1); leg_ik(-1)
    sep = 0.07 + 0.24 * (1 - together)
    y = -0.22 - 0.04 * (1 - together)
    z = 1.32 + 0.07 * (1 - together)
    arm_ik(+1, Vector((sep, y, z)), Vector((0.5, 0.5, -1.0)))
    arm_ik(-1, Vector((-sep, y, z)), Vector((-0.5, 0.5, -1.0)))
    lean(["neck.x", "head.x"], [-2.0, -4.0])


def build_clap():
    act = new_action("coach_clap")
    # 6 claps in 2.4 s (72 frames): hands apart -> together every 12 frames
    f = 0
    for i in range(7):
        pose_clap(0, 0.0 if i % 2 == 0 else 1.0)
        key(1 + i * 12)
    # make the last pose equal the first for looping: apart
    return act, 1, 1 + 72


def pose_spot(k):
    """k: 0 stand .. 1 deep crouch with the right hand on the ball spot."""
    reset()
    drop = 0.38 * k
    move_root((0, 0.0, -drop))
    lean(["spine_01.x", "spine_02.x", "spine_03.x"], [35 * k, 20 * k, 10 * k])
    lean(["neck.x", "head.x"], [-20 * k, -10 * k])
    # feet fixed, knees forward
    for s in (+1, -1):
        leg_ik(s, None, (0, -1, 0))
    ball = Vector((-0.12, -0.50, 0.10))
    rhand = Vector((-0.23, 0.0, 1.03)).lerp(ball + Vector((0, 0, 0.06)), k)
    arm_ik(-1, rhand, Vector((-1.0, 0.5, -0.4)))
    lhand = Vector((0.23, 0.0, 1.03)).lerp(Vector((0.20, -0.18, 0.62)), k)
    arm_ik(+1, lhand, Vector((1.0, 0.5, -0.4)))


def build_spot_ball():
    act = new_action("ref_spot_ball")
    seq = [(0, 0.0), (14, 0.6), (26, 1.0), (50, 1.0), (62, 0.55), (76, 0.0), (80, 0.0)]
    for f, k in seq:
        pose_spot(k)
        key(1 + f)
    return act, 1, 81


def pose_flag(phase):
    """phase 0..1: wind-up behind the shoulder (0), release overhead-forward (0.5), follow-through down (1)."""
    reset()
    lean(["spine_01.x", "spine_02.x", "spine_03.x"], [0, 0, 0])
    leg_ik(+1); leg_ik(-1)
    sh = head("arm_stretch.r")
    if phase < 0.5:
        k = phase / 0.5
        tgt = Vector((-0.23, 0.0, 1.03)).lerp(Vector((-0.28, 0.25, 1.78)), k)     # hand behind and above the shoulder
        pole = Vector((-0.2, 1.0, 0.0))
    else:
        k = (phase - 0.5) / 0.5
        tgt = Vector((-0.28, 0.25, 1.78)).lerp(Vector((-0.40, -0.45, 1.25)), min(k * 2, 1)) if k < 0.5 else Vector((-0.40, -0.45, 1.25)).lerp(Vector((-0.30, -0.25, 0.95)), (k - 0.5) * 2)
        pole = Vector((-0.6, 0.6, -0.4))
    arm_ik(-1, tgt, pole)
    arm_ik(+1, Vector((0.23, 0.0, 1.03)), POLE_OUT)
    twist = -18 * math.sin(phase * math.pi)
    lean(["spine_01.x", "spine_02.x", "spine_03.x"], [-4 * (1 - phase) + 8 * phase, -3 * (1 - phase) + 6 * phase, 0])
    lean(["neck.x", "head.x"], [0, 0])


def build_flag():
    act = new_action("ref_penalty_flag")
    for f, ph in [(0, 0.0), (10, 0.25), (18, 0.5), (24, 0.65), (36, 0.8), (50, 1.0), (60, 1.0)]:
        pose_flag(ph)
        key(1 + f)
    return act, 1, 61


BUILDERS = {
    "ref_idle": build_ref_idle,
    "coach_headset_idle": build_headset_idle,
    "coach_clipboard_idle": build_clipboard_idle,
    "coach_clap": build_clap,
    "ref_spot_ball": build_spot_ball,
    "ref_penalty_flag": build_flag,
}

