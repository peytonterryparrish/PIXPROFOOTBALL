"""PHR: body-type reshaping of the R1 clothing / skin meshes (Small / Medium / Large) by weight-blended radial scaling.

The skeleton is NOT touched (same rig for every body type, so one set of animation retargets fits all): every vertex is moved by
   v' = sum_g W_g(v) * ( C_g(v) + S_g(v) * (v - C_g(v)) )
where g are body groups (torso, left/right leg, left/right arm, left/right hand, neck, left/right foot), W_g(v) the skin weight of the vertex
on the bones of that group, C_g(v) the closest point on the group's bone axis and S_g(v) a per-axis scale that depends on height (torso, legs)
or on the position along the limb (arms).  Because the weights are the same ones the animation uses, thick limbs deform exactly like thin ones.
Shoulder pads are a smooth outward bump on the jersey / sleeve vertices around the shoulder joints.
"""
import numpy as np
from mathutils import Vector

# ----------------------------------------------------------------------------- body types
# torso: (z, width scale x, depth scale y) control points; legs: (z, radial scale); arms: (t along arm 0..1, radial scale)
BODY = {
    "Small": dict(
        torso=[(0.95, 1.00, 1.00), (1.10, 1.00, 1.02), (1.25, 1.00, 1.05), (1.40, 1.02, 1.07), (1.60, 1.00, 1.00)],
        leg=[(1.05, 1.00), (0.85, 1.04), (0.60, 1.00), (0.40, 1.00), (0.12, 1.00)],
        arm=[(0.0, 1.00), (0.3, 1.03), (0.6, 1.00), (1.0, 1.00)],
        neck=1.00, hand=1.00, foot=1.00, pad=0.040, pad_up=0.024,
    ),
    "Medium": dict(
        torso=[(0.95, 1.03, 1.05), (1.10, 1.03, 1.05), (1.25, 1.11, 1.16), (1.40, 1.18, 1.22), (1.60, 1.04, 1.06)],
        leg=[(1.05, 1.18), (0.85, 1.20), (0.60, 1.10), (0.40, 1.09), (0.12, 1.02)],
        arm=[(0.0, 1.12), (0.3, 1.16), (0.6, 1.10), (1.0, 1.03)],
        neck=1.08, hand=1.03, foot=1.03, pad=0.068, pad_up=0.036,
    ),
    "Large": dict(
        torso=[(0.95, 1.26, 1.28), (1.10, 1.26, 1.30), (1.25, 1.36, 1.40), (1.40, 1.36, 1.40), (1.60, 1.10, 1.14)],
        leg=[(1.05, 1.44), (0.85, 1.50), (0.60, 1.30), (0.40, 1.24), (0.12, 1.06)],
        arm=[(0.0, 1.34), (0.3, 1.40), (0.6, 1.26), (1.0, 1.10)],
        neck=1.26, hand=1.08, foot=1.07, pad=0.100, pad_up=0.054,
    ),
}

GROUPS = {
    "torso": ["rig:Hips", "rig:Spine", "rig:Spine1", "rig:Spine2", "rig:LeftShoulder", "rig:RightShoulder"],
    "neck": ["rig:Neck", "rig:Head"],
    "armL": ["rig:LeftArm", "rig:LeftForeArm"], "armR": ["rig:RightArm", "rig:RightForeArm"],
    "handL": ["rig:LeftHand"] + ["rig:LeftHand%s%d" % (f, i) for f in ("Thumb", "Index", "Middle", "Ring", "Pinky") for i in (1, 2, 3)],
    "handR": ["rig:RightHand"] + ["rig:RightHand%s%d" % (f, i) for f in ("Thumb", "Index", "Middle", "Ring", "Pinky") for i in (1, 2, 3)],
    "legL": ["rig:LeftUpLeg", "rig:LeftLeg"], "legR": ["rig:RightUpLeg", "rig:RightLeg"],
    "footL": ["rig:LeftFoot", "rig:LeftToeBase"], "footR": ["rig:RightFoot", "rig:RightToeBase"],
}


def weights_matrix(obj, bone_names):
    """Dense [N, B] skin weight matrix from the vertex groups (columns follow bone_names)."""
    idx = {}
    for g in obj.vertex_groups:
        idx[g.index] = g.name
    col = {n: i for i, n in enumerate(bone_names)}
    W = np.zeros((len(obj.data.vertices), len(bone_names)), np.float32)
    for v in obj.data.vertices:
        for g in v.groups:
            n = idx.get(g.group)
            if n in col:
                W[v.index, col[n]] = g.weight
    return W


def interp(points, x):
    xs = np.array([p[0] for p in points], np.float64)
    order = np.argsort(xs)
    xs = xs[order]
    out = []
    for k in range(1, len(points[0])):
        ys = np.array([p[k] for p in points], np.float64)[order]
        out.append(np.interp(x, xs, ys))
    return out


def closest_on_polyline(P, pts):
    """closest point (and parameter t in 0..1 over the total length) on the polyline pts for each row of P."""
    P = np.asarray(P, np.float64)
    best = np.full(len(P), 1e18)
    cp = np.zeros_like(P)
    tt = np.zeros(len(P))
    lens = [np.linalg.norm(pts[i + 1] - pts[i]) for i in range(len(pts) - 1)]
    total = sum(lens)
    acc = 0.0
    for i in range(len(pts) - 1):
        a, b = pts[i], pts[i + 1]
        ab = b - a
        t = np.clip(((P - a) @ ab) / max(ab @ ab, 1e-12), 0.0, 1.0)
        c = a + t[:, None] * ab
        d = np.sum((P - c) ** 2, axis=1)
        m = d < best
        best[m] = d[m]
        cp[m] = c[m]
        tt[m] = (acc + t[m] * lens[i]) / total
        acc += lens[i]
    return cp, tt


def head_of(arm, name):
    return np.array(arm.data.bones[name].head_local, np.float64)


def tail_of(arm, name):
    return np.array(arm.data.bones[name].tail_local, np.float64)


def reshape(arm, objs, body, pad_objs=("jersey", "armsleeve", "innershirt")):
    """Applies the body type to every mesh in objs (dict name -> object, rest pose, world space == local space)."""
    P = BODY[body]
    bone_names = [b.name for b in arm.data.bones]
    bcol = {n: i for i, n in enumerate(bone_names)}
    # axes
    hips = head_of(arm, "rig:Hips")
    spine_pts = [head_of(arm, "rig:Hips"), head_of(arm, "rig:Spine"), head_of(arm, "rig:Spine1"), head_of(arm, "rig:Spine2"), head_of(arm, "rig:Neck")]
    spine_z = np.array([p[2] for p in spine_pts])
    spine_y = np.array([p[1] for p in spine_pts])
    leg_axis = {s: [head_of(arm, "rig:%sUpLeg" % s), head_of(arm, "rig:%sLeg" % s), head_of(arm, "rig:%sFoot" % s)] for s in ("Left", "Right")}
    arm_axis = {s: [head_of(arm, "rig:%sArm" % s), head_of(arm, "rig:%sForeArm" % s), head_of(arm, "rig:%sHand" % s)] for s in ("Left", "Right")}
    neck_axis = [head_of(arm, "rig:Neck"), head_of(arm, "rig:Head")]
    foot_axis = {s: [head_of(arm, "rig:%sFoot" % s), head_of(arm, "rig:%sToeBase" % s)] for s in ("Left", "Right")}
    shoulder_c = {"Left": np.array([0.115, hips[1] + 0.02, 1.47]) , "Right": np.array([-0.115, hips[1] + 0.02, 1.47])}
    shoulder_j = {s: head_of(arm, "rig:%sArm" % s) for s in ("Left", "Right")}

    for name, o in objs.items():
        me = o.data
        N = len(me.vertices)
        co = np.empty(N * 3, np.float32)
        me.vertices.foreach_get('co', co)
        co = co.reshape(-1, 3).astype(np.float64)
        W = weights_matrix(o, bone_names)
        wsum = np.maximum(W.sum(axis=1), 1e-6)[:, None]
        W = W / wsum
        gw = {g: W[:, [bcol[b] for b in bl if b in bcol]].sum(axis=1) for g, bl in GROUPS.items()}
        out = np.zeros_like(co)
        used = np.zeros(N)
        # torso
        z = co[:, 2]
        sx, sy = interp([(t[0], t[1], t[2]) for t in P["torso"]], z)
        yc = np.interp(z, spine_z, spine_y)
        Tt = co.copy()
        Tt[:, 0] = (co[:, 0] - hips[0]) * sx + hips[0]
        Tt[:, 1] = (co[:, 1] - yc) * sy + yc
        out += gw["torso"][:, None] * Tt
        used += gw["torso"]
        # neck / head (only radial around the neck axis, bounded by the neck factor)
        cp, _ = closest_on_polyline(co, neck_axis)
        Tn = cp + (co - cp) * P["neck"]
        out += gw["neck"][:, None] * Tn
        used += gw["neck"]
        for s, side in (("L", "Left"), ("R", "Right")):
            # legs
            cp, _ = closest_on_polyline(co, leg_axis[side])
            fs, = interp(P["leg"], z)
            Tl = cp + (co - cp) * fs[:, None]
            out += gw["leg" + s][:, None] * Tl
            used += gw["leg" + s]
            # feet
            cp, _ = closest_on_polyline(co, foot_axis[side])
            Tf = cp + (co - cp) * P["foot"]
            out += gw["foot" + s][:, None] * Tf
            used += gw["foot" + s]
            # arms
            cp, t = closest_on_polyline(co, arm_axis[side])
            fa, = interp(P["arm"], t)
            Ta = cp + (co - cp) * fa[:, None]
            out += gw["arm" + s][:, None] * Ta
            used += gw["arm" + s]
            # hands
            cp, _ = closest_on_polyline(co, [head_of(arm, "rig:%sHand" % side), head_of(arm, "rig:%sHandMiddle3" % side)])
            Th = cp + (co - cp) * P["hand"]
            out += gw["hand" + s][:, None] * Th
            used += gw["hand" + s]
        rest = np.clip(1.0 - used, 0.0, 1.0)
        out += rest[:, None] * co
        # shoulder pads: outward / upward bump around each shoulder joint on the cloth that covers it
        if name in pad_objs and (P["pad"] > 0):
            for side in ("Left", "Right"):
                c = shoulder_c[side]
                j = shoulder_j[side]
                d = out - c
                r = np.linalg.norm(d, axis=1)
                R = 0.24
                fall = np.exp(-(np.maximum(r - 0.05, 0) / (R * 0.55)) ** 2) * (r < R * 1.6)
                dirv = d / np.maximum(r, 1e-6)[:, None]
                # mostly outward and up; only the part above the arm-pit line and on the shoulder side
                sidef = np.clip((np.abs(out[:, 0]) - 0.06) / 0.1, 0, 1) * (np.sign(out[:, 0]) == (1 if side == "Left" else -1))
                upf = np.clip((out[:, 2] - 1.36) / 0.12, 0, 1)
                bump = fall * sidef * upf
                out += (dirv * P["pad"] + np.array([0, 0, P["pad_up"]])) * bump[:, None]
        me.vertices.foreach_set('co', out.astype(np.float32).ravel())
        me.update()
