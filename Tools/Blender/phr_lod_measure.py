"""PHR: silhouette check per LOD. Re-imports PHR_<Body>.fbx and measures width (x) / depth (y) / hull circumference of the body parts at fixed heights for every LOD,
reporting the deviation from LOD0 (target: within 3 %).  blender -b --python phr_lod_measure.py -- <fbx> [out.txt]"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *

fbx = args()[0]
out = args()[1] if len(args()) > 1 else None
reset()
bpy.ops.import_scene.fbx(filepath=fbx)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
for o in meshes:
    for m in list(o.modifiers):
        o.modifiers.remove(m)          # rest pose, no armature evaluation


def lod(o):
    n = o.name.rsplit("_LOD", 1)
    return int(n[1]) if len(n) > 1 and n[1].isdigit() else 0


def base(o):
    return o.name.rsplit("_LOD", 1)[0]


GROUPS = {
    "torso(jersey+sleeves+arms)": ["Jersey", "SleeveL", "SleeveR", "Arms"],
    "legs(pants+socks+undersock)": ["Pants", "Socks", "Undersock"],
    "arms": ["Arms"],
    "head": ["Head"],
    "cleats": ["Cleats"],
}
HEIGHTS = {"torso(jersey+sleeves+arms)": [1.15, 1.25, 1.35, 1.45, 1.52], "legs(pants+socks+undersock)": [0.25, 0.40, 0.60, 0.80, 0.95, 1.05],
           "arms": [1.30, 1.40], "head": [1.62, 1.70, 1.78], "cleats": [0.03, 0.08]}
lines = []
worst = 0.0
for g, bases in GROUPS.items():
    for z in HEIGHTS[g]:
        row = {}
        for li in range(4):
            objs = [o for o in meshes if base(o) in bases and lod(o) == li]
            if not objs:
                continue
            # head LOD1 has shape keys: evaluate basis only (rest)
            m = measure(objs, z, None if g.startswith("legs") is False else None)
            if m:
                row[li] = m
        if 0 not in row:
            continue
        w0, d0, c0 = row[0]
        s = "%-30s z=%.2f LOD0 w %.3f d %.3f c %.3f" % (g, z, w0, d0, c0)
        for li in (1, 2, 3):
            if li in row:
                w, d, c = row[li]
                dw, dd, dc = (w / w0 - 1) * 100, (d / d0 - 1) * 100, (c / c0 - 1) * 100
                worst = max(worst, abs(dw), abs(dc))
                s += " | LOD%d w %+.1f%% d %+.1f%% c %+.1f%%" % (li, dw, dd, dc)
        lines.append(s)
        print("MEAS", s)
print("MEAS worst |dw|,|dc| over all groups: %.2f%%" % worst)
if out:
    open(out, "w").write("\n".join(lines) + "\nworst %.2f%%\n" % worst)
