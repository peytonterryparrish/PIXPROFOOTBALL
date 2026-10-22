import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *
import phr_body, phr2_viz as V
a = args()
body = a[0] if a else "Small"
reset()
arm, meshes = import_r1()
for n in ("head", "eyes", "eyelashes"):
    bpy.data.objects.remove(meshes.pop(n), do_unlink=True)
sk = {k: o for k, o in meshes.items() if k not in ("helmet", "facemask", "chinstrap", "Padding")}
P = phr_body.BODY[body]; P0 = (P['pad'], P['pad_up']); P['pad'] = 0.0; P['pad_up'] = 0.0
phr_body.reshape(arm, sk, body)
# pads
PADSRC = "D:/PIXFootball/Source/PPF_NEW/shoulder_pads/Football_Shoulder_Pads_fbx/Football_Shoulder_Pads.FBX"
before = set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=PADSRC)
pads = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
bpy.context.view_layer.update()
sx, sy, sz, dy, dz = [float(x) for x in a[1:6]]
for o in pads:
    M = Matrix.Translation((0, dy, dz)) @ Matrix.Diagonal((sx, sy, sz, 1.0)) @ o.matrix_world
    o.parent = None
    o.data.transform(M)
    o.matrix_world = Matrix.Identity(4)
    o.name = "pads_" + o.name
for o in list(bpy.data.objects):
    if o.type == 'EMPTY' and o not in (arm,) and not o.name.startswith('rig:'):
        bpy.data.objects.remove(o, do_unlink=True)
bpy.context.view_layer.update()
tag = "_".join(a[:6]).replace('.', 'p')
outd = "D:/PIXFootball/Renders/roster_v2/_work/"
show = [meshes['jersey'], meshes['arms']] + pads
c = (0, 0.05, 1.45)
for v in ('front', 'side', '34'):
    V.render(outd + f"padtry_{tag}_{v}.png", show, c, v, scale=1.0, hide=())
V.render(outd + f"padtry_{tag}_x_front.png", pads + [meshes['arms']], c, 'front', scale=1.0)
