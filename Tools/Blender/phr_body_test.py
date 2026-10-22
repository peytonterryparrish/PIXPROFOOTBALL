"""PHR: reshape R1 to Small/Medium/Large and render front/side/back + print cross-section measurements.
blender -b --python phr_body_test.py -- <out_prefix> <Small|Medium|Large>"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *
import phr_body

a = args()
prefix, body = a[0], a[1]
reset()
scene = setup_stage()
arm, meshes = import_r1()
for n in ("head", "eyes", "eyelashes"):
    bpy.data.objects.remove(meshes.pop(n), do_unlink=True)
head, eyes = import_df_head()
r1_eye_c = Vector((-0.006, -0.061, 1.751))
emn, emx = bbox([eyes])
dfc = (emn + emx) / 2
for o in (head, eyes):
    o.data.transform(Matrix.Translation(r1_eye_c) @ Matrix.Scale(0.969, 4) @ Matrix.Translation(-dfc), shape_keys=True)
    for kb in o.data.shape_keys.key_blocks:
        kb.value = 0.0
shape = "Head_11_SubD01"
for kb in head.data.shape_keys.key_blocks:
    kb.value = 1.0 if kb.name == shape else 0.0
for kb in eyes.data.shape_keys.key_blocks:
    kb.value = 1.0 if kb.name == "Head_Eyes_11" else 0.0
for o in (head, eyes):
    o.parent = arm
    md = o.modifiers.new("Armature", 'ARMATURE')
    md.object = arm
# head weights: all to the head bone (test only)
for o in (head, eyes):
    g = o.vertex_groups.new(name="rig:Head")
    g.add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
def mat(tex, name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    t = m.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = load_img(tex)
    m.node_tree.links.new(t.outputs[0], m.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    return m
head.data.materials.clear(); head.data.materials.append(mat(DF + "/Bodies/mediumPlayer.fbm/T_Head_BC_01.png", "skin"))
eyes.data.materials.clear(); eyes.data.materials.append(mat(DF + "/Bodies/mediumPlayer.fbm/Eyes_MAT_Base_color.png", "eye"))
for o in (head, eyes):
    for p in o.data.polygons:
        p.use_smooth = True

skinned = {n: o for n, o in meshes.items() if n not in ("helmet", "facemask", "chinstrap", "Padding")}
phr_body.reshape(arm, skinned, body)
bpy.context.view_layer.update()
for z in (0.30, 0.60, 0.75, 0.90, 1.02, 1.15, 1.25, 1.35, 1.45, 1.52):
    legs = measure([meshes['pants'], meshes['socks']], z)
    legl = measure([meshes['pants'], meshes['socks']], z, 'L')
    jers = measure([meshes['jersey'], meshes['armsleeve'], meshes['innershirt'], meshes['arms']], z)
    print("MEAS %s z=%.2f legL %s | jersey+arms %s | pants %s" % (body, z, tuple(round(x, 3) for x in legl) if legl else None, tuple(round(x, 3) for x in jers) if jers else None, tuple(round(x, 3) for x in legs) if legs else None))
rig = make_rig(scene)
cam = make_camera(scene)
for name, az, tgt, d in [("front", 0, (0, 0.05, 0.95), 7.0), ("side", 90, (0, 0.05, 0.95), 7.0), ("back", 180, (0, 0.05, 0.95), 7.0),
                         ("torso3q", 35, (0, 0.05, 1.3), 3.0)]:
    size = (900, 1300) if name != "torso3q" else (900, 900)
    render_view(scene, cam, rig, f"{prefix}_{body}_{name}.png", tgt, az, d, size=size, samples=24)
