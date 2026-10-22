"""Render the original R1 (A-P.glb, untouched textures) from the same views / lighting as phr_verify.py so the two can be compared.
blender -b --python Tools/Blender/phr_render_r1.py -- <out_prefix>   (writes <prefix>_{front,3q,back,head3q,hands,feet}.png)
R1 is only rendered for the QA comparison; none of its renders are used by the game."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *

a = args()
prefix = a[0]
reset()
scene = setup_stage()
bpy.ops.import_scene.gltf(filepath=R1_GLB)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
for o in list(bpy.data.objects):
    if o.type == 'EMPTY' or o.name == 'Icosphere':
        bpy.data.objects.remove(o, do_unlink=True)
# the glTF import is already in metres through the armature scale; centre it like import_r1() does
bpy.context.view_layer.update()
rig = make_rig(scene)
cam = make_camera(scene)
shift = Vector((-0.0105, 0.0, 0.004))
arm.location = arm.location + shift
bpy.context.view_layer.update()
render_view(scene, cam, rig, f"{prefix}_front.png", (0, 0.05, 0.95), 0, 7.0, size=(700, 1100), samples=24)
render_view(scene, cam, rig, f"{prefix}_3q.png", (0, 0.05, 1.0), 40, 5.0, size=(900, 1100), samples=24)
render_view(scene, cam, rig, f"{prefix}_back.png", (0, 0.05, 0.95), 180, 7.0, size=(700, 1100), samples=24)
render_view(scene, cam, rig, f"{prefix}_head3q.png", (0, -0.03, 1.72), 30, 1.1, size=(800, 800), samples=32)
render_view(scene, cam, rig, f"{prefix}_hands.png", (0.62, -0.08, 1.2), 20, 1.0, size=(800, 800), samples=32)
render_view(scene, cam, rig, f"{prefix}_feet.png", (0.0, 0.03, 0.25), 30, 2.2, size=(900, 700), samples=32)
