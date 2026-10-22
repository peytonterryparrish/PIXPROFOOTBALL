"""PHR: deformation check on the built FBX: rotates bones in armature space and renders. blender -b --python phr_pose_test.py -- <fbx> <out_prefix>"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_dress import *

fbx, prefix = args()[0], args()[1]
reset()
scene = setup_stage()
bpy.ops.import_scene.fbx(filepath=fbx)
arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
meshes = [o for o in bpy.data.objects if o.type == 'MESH' and o.name != "stage_floor"]
dress(meshes)
for o in meshes:
    n = o.name.rsplit("_LOD", 1)
    lv = int(n[1]) if len(n) > 1 and n[1].isdigit() else 0
    o.hide_render = (lv != 0)
    if o.data.shape_keys:
        for kb in o.data.shape_keys.key_blocks:
            kb.value = 0.0
rig = make_rig(scene)
cam = make_camera(scene)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode='POSE')


def rot(name, axis, deg):
    pb = arm.pose.bones[name]
    p = arm.matrix_world @ pb.head
    R = Matrix.Rotation(math.radians(deg), 4, Vector(axis))
    M = Matrix.Translation(p) @ R @ Matrix.Translation(-p)
    pb.matrix = M @ pb.matrix
    bpy.context.view_layer.update()


def reset_pose():
    for pb in arm.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


poses = {
    "armsup": [("rig:LeftArm", (0, 1, 0), -100), ("rig:RightArm", (0, 1, 0), 100), ("rig:LeftForeArm", (1, 0, 0), 30), ("rig:RightForeArm", (1, 0, 0), 30)],
    "reach": [("rig:LeftArm", (1, 0, 0), -80), ("rig:RightArm", (1, 0, 0), -80), ("rig:Spine", (1, 0, 0), -10), ("rig:Spine1", (1, 0, 0), -10), ("rig:Spine2", (1, 0, 0), -10)],
    "stance": [("rig:Hips", (1, 0, 0), -30), ("rig:Spine", (1, 0, 0), -22), ("rig:Spine1", (1, 0, 0), -22), ("rig:Spine2", (1, 0, 0), -16), ("rig:LeftUpLeg", (1, 0, 0), -75), ("rig:RightUpLeg", (1, 0, 0), -75),
               ("rig:LeftLeg", (1, 0, 0), 85), ("rig:RightLeg", (1, 0, 0), 85), ("rig:LeftArm", (1, 0, 0), -40), ("rig:RightArm", (1, 0, 0), -40)],
    "twist": [("rig:Spine", (0, 0, 1), 20), ("rig:Spine1", (0, 0, 1), 20), ("rig:Spine2", (0, 0, 1), 20), ("rig:Head", (0, 0, 1), 40), ("rig:LeftArm", (1, 0, 0), -70), ("rig:LeftForeArm", (0, 0, 1), -60)],
}
for name, ops in poses.items():
    reset_pose()
    for b, ax, d in ops:
        rot(b, ax, d)
    for view, az, tgt, dist, size in (("f", 0, (0, 0.0, 1.05), 5.5, (700, 900)), ("s", 90, (0, 0.0, 1.05), 5.5, (700, 900)), ("3q", 40, (0, 0.0, 1.05), 5.5, (700, 900))):
        render_view(scene, cam, rig, f"{prefix}_{name}_{view}.png", tgt, az, dist, size=size, samples=16)
