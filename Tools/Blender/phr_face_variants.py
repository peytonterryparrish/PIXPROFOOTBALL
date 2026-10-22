"""PHR: render the head blend shapes (12 sculpted faces, 5 authored tweaks at -1 / +1) from the built FBX: proof that every shape key survived the export and reads as a different face.
blender -b --python phr_face_variants.py -- <fbx> <out_dir>  -> <out_dir>/face_S01..S12.png, face_T_*_{neg,pos}.png"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_dress import *

fbx, out = args()[0], args()[1]
ensure(out)
reset()
scene = setup_stage(floor=False)
bpy.ops.import_scene.fbx(filepath=fbx)
meshes = [o for o in bpy.data.objects if o.type == 'MESH']
for o in meshes:
    n = o.name
    keep = n in ("Head_LOD0", "Eyes_LOD0")
    o.hide_render = not keep
dress([o for o in meshes if o.name in ("Head_LOD0", "Eyes_LOD0")], tone=3)
head = bpy.data.objects["Head_LOD0"]
eyes = bpy.data.objects["Eyes_LOD0"]
rig = make_rig(scene, target=(0, 0, 1.7))
cam = make_camera(scene)
names = [k.name for k in head.data.shape_keys.key_blocks]
print("KEYS", names)


def setkeys(**vals):
    for o in (head, eyes):
        for kb in o.data.shape_keys.key_blocks:
            kb.value = vals.get(kb.name, 0.0)


files = []
for i in range(1, 13):
    setkeys(**{"S%02d" % i: 1.0})
    f = f"{out}/face_S{i:02d}.png"
    render_view(scene, cam, rig, f, (0, -0.03, 1.72), 20, 0.75, size=(420, 480), samples=16)
    files.append(f)
for t in ("T_JawWide", "T_ChinLong", "T_NoseBig", "T_CheekFull", "T_BrowHeavy"):
    for sgn, tag in ((-1.0, "neg"), (1.0, "pos")):
        setkeys(S07=1.0, **{t: sgn})
        f = f"{out}/face_{t}_{tag}.png"
        render_view(scene, cam, rig, f, (0, -0.03, 1.72), 20, 0.75, size=(420, 480), samples=16)
        files.append(f)
montage(files[:12], f"{out}/faces_sculpted.png", 6)
montage(files[12:], f"{out}/faces_tweaks.png", 6)
