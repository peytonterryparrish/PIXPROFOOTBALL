"""PHR: re-import a built PHR_<Body>.fbx, print the structure, and render LOD0..3 (+ closeups) with preview materials.
blender -b --python Tools/Blender/phr_verify.py -- <fbx> <out_prefix> [lods=0,1,2,3] [shape=1] [tone=3]"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_dress import *

a = args()
fbx, prefix = a[0], a[1]
lods = [int(x) for x in (a[2] if len(a) > 2 else "0,1,2,3").split(",")]
shape = int(a[3]) if len(a) > 3 else 1
tone = int(a[4]) if len(a) > 4 else 3
reset()
scene = setup_stage()
bpy.ops.import_scene.fbx(filepath=fbx)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
print("ARM", arm.name, "scale", tuple(arm.scale), "rot", tuple(arm.rotation_euler), "bones", len(arm.data.bones))
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
for o in meshes:
    sk = o.data.shape_keys
    print("OBJ", o.name, "tris", sum(len(p.vertices) - 2 for p in o.data.polygons), "parent", o.parent.name if o.parent else None, o.parent_type, o.parent_bone,
          "shapes", len(sk.key_blocks) if sk else 0, "mods", [m.type for m in o.modifiers], "mats", [m.name if m else None for m in o.data.materials])
dress(meshes, tone=tone)
# head shape
for o in meshes:
    if o.data.shape_keys:
        for kb in o.data.shape_keys.key_blocks:
            kb.value = 1.0 if kb.name == "S%02d" % shape else 0.0
rig = make_rig(scene)
cam = make_camera(scene)
for li in lods:
    for o in meshes:
        n = o.name.rsplit("_LOD", 1)
        lv = int(n[1]) if len(n) > 1 and n[1].isdigit() else 0
        if n[0] in ("Head_Brows", "Head_Beard", "Head_Scalp", "Visor"):
            o.hide_render = True
            continue
        o.hide_render = (lv != li)
        if n[0] == "Eyes":
            o.hide_render = (lv != min(li, 1))
    render_view(scene, cam, rig, f"{prefix}_lod{li}_front.png", (0, 0.05, 0.95), 0, 7.0, size=(700, 1100), samples=24)
    if li == 0:
        render_view(scene, cam, rig, f"{prefix}_lod{li}_3q.png", (0, 0.05, 1.0), 40, 5.0, size=(900, 1100), samples=24)
        render_view(scene, cam, rig, f"{prefix}_lod{li}_back.png", (0, 0.05, 0.95), 180, 7.0, size=(700, 1100), samples=24)
        render_view(scene, cam, rig, f"{prefix}_lod{li}_head3q.png", (0, -0.03, 1.72), 30, 1.1, size=(800, 800), samples=32)
        render_view(scene, cam, rig, f"{prefix}_lod{li}_hands.png", (0.62, -0.08, 1.2), 20, 1.0, size=(800, 800), samples=32)
        render_view(scene, cam, rig, f"{prefix}_lod{li}_feet.png", (0.0, 0.03, 0.25), 30, 2.2, size=(900, 700), samples=32)
