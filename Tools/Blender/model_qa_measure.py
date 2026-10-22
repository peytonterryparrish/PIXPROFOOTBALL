"""MODELS QA: measure the S/M/L player bodies (and optionally referee / coach FBX or a LOD export) in bind pose.

  blender -b --python Tools/Blender/model_qa_measure.py -- [--fbx path.fbx[,path2.fbx]] [--out Logs/model_qa/blender_measure.txt]

Prints, per file: armature bone lengths (hip height, thigh, shin, spine, shoulder width, ...), per mesh bounds and the cross-section
width / depth / hull circumference at fixed heights (m), measured on the world-space mesh (metres, Z-up, character faces -Y, left = +X).
Leg sections are split per leg (x < 0 / x > 0).
"""
import sys, os, re, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix_player_common import *
import bpy
import numpy as np
from mathutils import Vector

HEIGHTS = [0.10, 0.30, 0.50, 0.65, 0.80, 0.907, 1.00, 1.10, 1.20, 1.30, 1.40, 1.50]
BONES = ["hip", "root.x", "spine_01", "spine_02", "spine_03", "neck", "head", "shoulder_l", "shoulder.l", "arm_stretch_l", "arm_stretch.l", "forearm_stretch_l",
         "forearm_stretch.l", "hand_l", "hand.l", "thigh_stretch_l", "thigh_stretch.l", "leg_stretch_l", "leg_stretch.l", "foot_l", "foot.l", "toes_01_l", "toes_01.l"]


def hull_perimeter(pts):
    p = sorted(set((round(float(x), 5), round(float(y), 5)) for x, y in pts))
    if len(p) < 3:
        return 0.0
    def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo = []
    for q in p:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], q) <= 0: lo.pop()
        lo.append(q)
    up = []
    for q in reversed(p):
        while len(up) >= 2 and cross(up[-2], up[-1], q) <= 0: up.pop()
        up.append(q)
    h = lo[:-1] + up[:-1]
    return sum(math.hypot(h[i][0] - h[(i + 1) % len(h)][0], h[i][1] - h[(i + 1) % len(h)][1]) for i in range(len(h)))


def section(co, tv, z):
    """Intersection points of the triangle edges with the plane Z=z -> Nx3 array."""
    a, b, c = co[tv[:, 0]], co[tv[:, 1]], co[tv[:, 2]]
    pts = []
    for p, q in ((a, b), (b, c), (c, a)):
        dz = q[:, 2] - p[:, 2]
        m = ((p[:, 2] - z) * (q[:, 2] - z) < 0)
        if not m.any():
            continue
        t = (z - p[m, 2]) / dz[m]
        pts.append(p[m] + (q[m] - p[m]) * t[:, None])
    return np.concatenate(pts) if pts else np.zeros((0, 3))


def main():
    a = script_args()
    fbxs = [os.path.join(BODY_DIR, "%sPlayer.fbx" % b) for b in ("Small", "Medium", "Large")]
    out = os.path.join(PROJECT_ROOT, "Logs", "model_qa", "blender_measure.txt")
    i = 0
    while i < len(a):
        if a[i] == "--fbx": fbxs = a[i + 1].split(","); i += 2
        elif a[i] == "--out": out = a[i + 1]; i += 2
        else: i += 1
    os.makedirs(os.path.dirname(out), exist_ok=True)
    lines = []
    def P(s=""):
        print(s); lines.append(s)
    for f in fbxs:
        objs = import_player(f)
        arm = next((o for o in bpy.data.objects if o.type == 'ARMATURE'), None)
        P("=" * 100); P(os.path.basename(f))
        if arm:
            M = arm.matrix_world
            bones = {b.name: (M @ b.head_local, M @ b.tail_local) for b in arm.data.bones}
            P("armature world scale %s, %d bones" % (tuple(round(x, 3) for x in M.to_scale()), len(bones)))
            for n in BONES:
                if n in bones:
                    h, t = bones[n]
                    P("  bone %-18s head (%.3f %.3f %.3f) tail (%.3f %.3f %.3f) len %.3f" % (n, h.x, h.y, h.z, t.x, t.y, t.z, (t - h).length))
            def L(a_, b_):
                return (bones[a_][0] - bones[b_][0]).length if a_ in bones and b_ in bones else float('nan')
            for side in ("l", "r"):
                th = next((n for n in bones if n.startswith("thigh_stretch") and n.endswith(side)), None)
                lg = next((n for n in bones if n.startswith("leg_stretch") and n.endswith(side)), None)
                ft = next((n for n in bones if n.startswith("foot") and n.endswith(side) and "tgt" not in n.lower() and n.startswith("foot") and len(n) <= 8), None)
                if th and lg and ft:
                    P("  leg %s: thigh %.3f  shin %.3f  foot-head z %.3f  thigh-head x %.3f" % (side, L(th, lg), L(lg, ft), bones[ft][0].z, bones[th][0].x))
        for o in sorted((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: o.name):
            co, nr, uv, tl, tv, tp = world_arrays(o)
            mn, mx = co.min(0), co.max(0)
            P("  mesh %-36s verts %6d tris %6d  bounds x %.3f..%.3f y %.3f..%.3f z %.3f..%.3f" % (o.name, len(co), len(tv), mn[0], mx[0], mn[1], mx[1], mn[2], mx[2]))
            if not re.match(r"^(Torso|Legs)", o.name):
                continue
            for z in HEIGHTS:
                if z < mn[2] or z > mx[2]:
                    continue
                s = section(co, tv, z)
                if len(s) < 3:
                    continue
                if o.name.startswith("Legs") and z < 0.85:
                    for nm, mk in (("L", s[:, 0] > 0), ("R", s[:, 0] < 0)):
                        q = s[mk]
                        if len(q) >= 3:
                            P("    z=%.3f %s  width %.3f depth %.3f circ %.3f" % (z, nm, np.ptp(q[:, 0]), np.ptp(q[:, 1]), hull_perimeter(q[:, :2])))
                else:
                    P("    z=%.3f all width %.3f depth %.3f circ %.3f" % (z, np.ptp(s[:, 0]), np.ptp(s[:, 1]), hull_perimeter(s[:, :2])))
    with open(out, "w") as fh:
        fh.write("\n".join(lines))


if __name__ == "__main__":
    main()
