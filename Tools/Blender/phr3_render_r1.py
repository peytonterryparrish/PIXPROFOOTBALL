"""v0.13.0 BODIES-2: R1 (untouched, QA only, never shipped) rendered in the framing of the BodyShots cells (front, 3/4, shoulder close-up, side) for the R1-vs-field contact sheets.
blender -b --python Tools/Blender/phr3_render_r1.py -- <out_prefix> [scale]   -> <prefix>_{c0_front,c1_3q,c3_shoulder,c7_side}.png  (cell 360 x 520 x scale)"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *

a = args()
prefix = a[0]
sc = int(a[1]) if len(a) > 1 else 2
reset()
scene = setup_stage()
bpy.ops.import_scene.gltf(filepath=R1_GLB)
for o in list(bpy.data.objects):
    if o.type == 'EMPTY' or o.name == 'Icosphere':
        bpy.data.objects.remove(o, do_unlink=True)
bpy.context.view_layer.update()
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
arm.location = arm.location + Vector((-0.0105, 0.0, 0.004))
bpy.context.view_layer.update()
rig = make_rig(scene)
cam = make_camera(scene)
W, H = 360 * sc, 520 * sc
L22 = 18.0 / math.tan(math.radians(11.0))
L24 = 18.0 / math.tan(math.radians(12.0))
render_view(scene, cam, rig, f"{prefix}_c0_front.png", (0, 0.05, 0.95), 0, 7.5, size=(W, H), lens=L22, samples=32)
render_view(scene, cam, rig, f"{prefix}_c1_3q.png", (0, 0.05, 0.95), 38, 7.5, size=(W, H), lens=L22, samples=32)
render_view(scene, cam, rig, f"{prefix}_c3_shoulder.png", (0, 0.05, 1.43), 32, 2.1, elev_deg=2.5, size=(W, H), lens=L24, samples=32)
render_view(scene, cam, rig, f"{prefix}_c7_side.png", (0, 0.05, 0.95), 90, 7.5, size=(W, H), lens=L22, samples=32)
