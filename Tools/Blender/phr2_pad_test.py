"""PH2 pad fit experiment on the R1 frame: blender -b --python phr2_pad_test.py -- Small|Medium|Large [key=value fit params]"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *
import phr_body, phr2_viz as V, phr2_pads as PD

a = args()
body = a[0]
fit = {}
for kv in a[1:]:
    k, v = kv.split("=")
    fit[k] = float(v)
ext = fit.pop("ext", 0.05)
outd = "D:/PIXFootball/Renders/roster_v2/_work/"


def shoulder_report(tag, jersey, extra=()):
    co, tv = mesh_world(jersey)
    res = []
    for z in (1.40, 1.46, 1.52, 1.56):
        m = measure([jersey] + list(extra), z)
        res.append("z%.2f w %.3f d %.3f c %.3f" % ((z,) + tuple(m)) if m else "z%.2f none" % z)
    mn, mx = bbox([jersey])
    print("SHOULDER", tag, "| jersey bbox x %.3f..%.3f (w %.3f) y %.3f..%.3f z %.3f..%.3f" % (mn.x, mx.x, mx.x - mn.x, mn.y, mx.y, mn.z, mx.z), "|", " | ".join(res))


def setup():
    reset()
    arm, meshes = import_r1()
    for n in ("head", "eyes", "eyelashes"):
        bpy.data.objects.remove(meshes.pop(n), do_unlink=True)
    return arm, meshes


# BEFORE: existing build (bump pads)
arm, meshes = setup()
sk = {k: o for k, o in meshes.items() if k not in ("helmet", "facemask", "chinstrap", "Padding")}
phr_body.reshape(arm, sk, body)
shoulder_report("before " + body, meshes["jersey"], [meshes["armsleeve"], meshes["innershirt"]])
c = (0, 0.05, 1.45)
for v in ("front", "34"):
    V.render(outd + "pads_before_%s_%s.png" % (body, v), [meshes["jersey"], meshes["arms"]], c, v, scale=1.1)

# AFTER
arm, meshes = setup()
P = phr_body.BODY[body]
P["pad"] = 0.0
P["pad_up"] = 0.0
pads = PD.make_pads(arm, fit or None)
sk = {k: o for k, o in meshes.items() if k not in ("helmet", "facemask", "chinstrap", "Padding")}
sk["Pads"] = pads
phr_body.reshape(arm, sk, body)
j = meshes["jersey"]
PD.extend_sleeves(j, arm, ext)
fin = PD.conform_to_pads(j, pads, arm)
print("PUSH max %.3f mean %.4f nonzero %d of %d" % (fin.max(), fin.mean(), (fin > 0).sum(), len(fin)))
shoulder_report("after " + body, j, [meshes["armsleeve"], meshes["innershirt"]])
for v in ("front", "34", "side", "back", "top"):
    V.render(outd + "pads_after_%s_%s.png" % (body, v), [j, meshes["arms"]], c, v, scale=1.1)
V.render(outd + "pads_under_%s_34.png" % body, [pads, meshes["arms"], j], c, "34", scale=1.1, hide=("jersey",))
