"""v0.13.0 BODIES-2: shoulder width of R1 and of the 15 field bodies (rest pose, jersey + pads), written as csv.

  blender -b --python Tools/Blender/phr3_shoulders.py -- <meshDir> <out.csv>

R1 rows come from the R1 jersey (the pad bump is in the jersey: no separate pads).  Field rows: Jersey_LOD0 (the draped jersey is the outer surface over the pads).
Columns: width (x extent) of the jersey section at the shoulder-joint height + 0.04 / + 0.0 / - 0.06 m, the chest width (shoulder - 0.17) and the ratio shoulder / chest.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *

SIZES = ["Small", "Medium", "Large"]
BASES = ["Std", "Stocky", "Athletic", "Tall", "Runner"]
OFFS = (0.04, 0.0, -0.06, -0.17)


def row(name, obj, zs):
    out = [name]
    ws = []
    for dz in OFFS:
        m = measure([obj], zs + dz)
        ws.append(m[0] if m else 0.0)
    out += ["%.3f" % w for w in ws]
    out.append("%.3f" % (ws[1] / ws[3] if ws[3] else 0.0))
    return ",".join(out)


def main():
    a = args()
    mdir, outp = a[0], a[1]
    rows = ["body,w_sh+0.04,w_sh,w_sh-0.06,w_chest,sh_over_chest"]
    reset()
    arm, meshes = import_r1()
    zs = arm.data.bones["rig:LeftArm"].head_local[2]
    rows.append(row("R1", meshes["jersey"], zs))
    for b in BASES:
        for s in SIZES:
            p = os.path.join(mdir, "PHR_%s%s.fbx" % (s, "" if b == "Std" else "_" + b))
            if not os.path.exists(p):
                p = os.path.join(mdir, "PHR_%s_%s.fbx" % (s, b))
            if not os.path.exists(p):
                continue
            reset()
            bpy.ops.import_scene.fbx(filepath=p)
            bpy.context.view_layer.update()
            objs = {o.name: o for o in bpy.data.objects if o.type == 'MESH'}
            ar = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
            zz = (ar.matrix_world @ ar.data.bones["rig:LeftArm"].head_local).z
            rows.append(row("%s_%s" % (s, b), objs["Jersey_LOD0"], zz))
    open(outp, "w").write("\n".join(rows) + "\n")
    print("\n".join(rows))


main()
