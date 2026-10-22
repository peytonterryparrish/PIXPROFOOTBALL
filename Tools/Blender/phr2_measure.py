"""PH2: shoulder width / hull circumferences of the built bodies (rest pose) and LOD silhouette error.

  blender -b --python Tools/Blender/phr2_measure.py -- <out.md>
Before = the live v0.6.0 bodies (Assets/_Licensed/PhotorealPlayer/Meshes/PHR_<Size>.fbx), after = Staging_v07 (PHR_<Size>_<Base>.fbx).
Columns: shoulder width = x extent of jersey LOD0 (the broadest line of the padded jersey), hull circumference of the jersey at the shoulder / chest / waist heights
(z relative to the shoulder joint: -0.02 / -0.17 / -0.36 m), pants at hip / thigh, LOD error = worst relative difference of width / circumference of LOD1..3 vs LOD0
over jersey, pants, arms at 5 - 6 heights.
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *

LIVE = os.path.join(OUT_ASSET, "Meshes")
STG = os.path.join(OUT_ASSET, "Staging_v07", "Meshes")
SIZES = ["Small", "Medium", "Large"]
BASES = ["Std", "Stocky", "Athletic", "Tall", "Runner"]


def load(path):
    reset()
    bpy.ops.import_scene.fbx(filepath=path)
    bpy.context.view_layer.update()
    objs = {o.name: o for o in bpy.data.objects if o.type == 'MESH'}
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    return objs, arm


def bone_z(arm, name):
    M = arm.matrix_world
    return (M @ arm.data.bones[name].head_local).z


def hull(objs, part, lod, z):
    o = objs.get("%s_LOD%d" % (part, lod))
    if o is None:
        return None
    return measure([o], z)


def stats(path):
    objs, arm = load(path)
    zs = bone_z(arm, "rig:LeftArm")
    j0 = objs["Jersey_LOD0"]
    mn, mx = bbox([j0])
    r = dict(shoulder_w=mx.x - mn.x, depth=mx.y - mn.y, top=mx.z, hip=bone_z(arm, "rig:Hips"), height=bbox([o for n, o in objs.items() if n.endswith("_LOD0") and n.startswith(("Helmet_Shell", "Jersey", "Pants", "Cleats"))])[1].z)
    for nm, dz in (("sh", -0.02), ("chest", -0.17), ("waist", -0.36)):
        m = measure([j0], zs + dz)
        r[nm] = m[2] if m else 0
    pz = bone_z(arm, "rig:LeftUpLeg")
    m = measure([objs["Pants_LOD0"]], pz - 0.02)
    r["hips"] = m[2] if m else 0
    m = measure([objs["Pants_LOD0"]], pz - 0.2, 'L')
    r["thigh"] = m[2] if m else 0
    # LOD error
    worst = 0.0
    worst_at = ""
    for part, zl in (("Jersey", [zs - 0.02, zs - 0.12, zs - 0.22, zs - 0.34, zs - 0.40]), ("Pants", [pz - 0.02, pz - 0.15, pz - 0.30, pz - 0.45]), ("Arms", [zs - 0.05, zs - 0.12])):
        for z in zl:
            base = hull(objs, part, 0, z)
            if not base:
                continue
            for lod in (1, 2, 3):
                m = hull(objs, part, lod, z)
                if not m:
                    continue
                for k in (0, 2):
                    e = abs(m[k] - base[k]) / max(base[k], 1e-6)
                    if k == 2 and e > worst:
                        worst, worst_at = e, "%s LOD%d z%.2f" % (part, lod, z)
    r["lod_err"] = worst
    r["lod_at"] = worst_at
    pad = {}
    for lod in range(4):
        o = objs.get("Pads_LOD%d" % lod)
        if o:
            pad[lod] = sum(len(p.vertices) - 2 for p in o.data.polygons)
    r["pad_tris"] = pad
    return r


def main():
    out = args()[0]
    rows = []
    before = {}
    for s in SIZES:
        p = os.path.join(LIVE, "PHR_%s.fbx" % s)
        if os.path.exists(p):
            before[s] = stats(p)
    lines = ["| body | shoulder width m | depth m | shoulder circ | chest circ | waist circ | hips circ | thigh circ | height m | hip z | LOD1-3 worst circ error |", "|---|---|---|---|---|---|---|---|---|---|---|"]

    def row(tag, r):
        lines.append("| %s | %.3f | %.3f | %.2f | %.2f | %.2f | %.2f | %.2f | %.3f | %.3f | %.1f %% (%s) |" % (tag, r["shoulder_w"], r["depth"], r["sh"], r["chest"], r["waist"], r["hips"], r["thigh"], r["height"], r["hip"], 100 * r["lod_err"], r["lod_at"]))
    for s in SIZES:
        if s in before:
            row("BEFORE %s (live v0.6.0)" % s, before[s])
    for b in BASES:
        for s in SIZES:
            p = os.path.join(STG, "PHR_%s_%s.fbx" % (s, b))
            if os.path.exists(p):
                r = stats(p)
                row("AFTER %s %s" % (s, b), r)
                if b == "Std" and s in before:
                    lines.append("| &nbsp;&nbsp;delta vs before | %+.3f (%+.0f %%) | %+.3f | %+.2f | %+.2f | %+.2f | | | | | pad tris %s |" % (r["shoulder_w"] - before[s]["shoulder_w"], 100 * (r["shoulder_w"] / before[s]["shoulder_w"] - 1), r["depth"] - before[s]["depth"], r["sh"] - before[s]["sh"], r["chest"] - before[s]["chest"], r["waist"] - before[s]["waist"], r["pad_tris"]))
    with open(out, "w") as f:
        f.write("\n".join(lines) + "\n")
    print("\n".join(lines))


main()
