"""Assemble R1 (head removed) + Draft Fever head + generated uniform textures and render. blender -b --python phr_scene_test.py -- <prefix> <Body> [views]"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_preview import *
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
        kb.value = 1.0 if kb.name in ("Head_11_SubD01", "Head_Eyes_11") else 0.0
for o in (head, eyes):
    o.parent = arm
    md = o.modifiers.new("Armature", 'ARMATURE')
    md.object = arm
    g = o.vertex_groups.new(name="rig:Head")
    g.add(list(range(len(o.data.vertices))), 1.0, 'REPLACE')
skinned = {n: o for n, o in meshes.items() if n not in ("helmet", "facemask", "chinstrap", "Padding")}
phr_body.reshape(arm, skinned, body)

P = (0.05, 0.16, 0.46)
S = (0.96, 0.72, 0.10)
T = (0.95, 0.95, 0.95)
assign(meshes["jersey"], uniform_material("jersey", P, S, T))
assign(meshes["pants"], uniform_material("pants", T, P, S))
assign(meshes["socks"], uniform_material("socks", P, S, T))
assign(meshes["armsleeve"], flat_material("sleeve", T, 0.6))
assign(meshes["innershirt"], flat_material("sleeve2", T, 0.6))
assign(meshes["helmet"], flat_material("helmet", P, 0.25))
assign(meshes["facemask"], flat_material("mask", (0.05, 0.05, 0.05), 0.4, 0.5))
assign(meshes["chinstrap"], flat_material("chin", T, 0.5))
assign(meshes["Padding"], flat_material("pad", T, 0.7))
for n in ("handwarmer", "towel", "twistband", "wristcoach", "shorts"):
    assign(meshes[n], flat_material(n, (0.05, 0.05, 0.05) if n == "handwarmer" else T, 0.6))
assign(meshes["cleats"], flat_material("cleats", T, 0.5))
skin = flat_material("skin", (0.62, 0.45, 0.34), 0.5)
assign(meshes["arms"], skin)
hm = bpy.data.materials.new("hskin")
hm.use_nodes = True
t = hm.node_tree.nodes.new("ShaderNodeTexImage")
t.image = load_img(DF + "/Bodies/mediumPlayer.fbm/T_Head_BC_01.png")
hm.node_tree.links.new(t.outputs[0], hm.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
assign(head, hm)
assign(eyes, flat_material("eye", (0.3, 0.2, 0.1), 0.2))
bpy.data.objects.remove(meshes["shorts"], do_unlink=True)
rig = make_rig(scene)
cam = make_camera(scene)
views = [("front", 0, (0, 0.05, 0.95), 7.0, (700, 1100)), ("back", 180, (0, 0.05, 0.95), 7.0, (700, 1100)),
         ("torso", 30, (0, 0.05, 1.3), 2.8, (900, 900)), ("legs", 25, (0, 0.05, 0.55), 2.8, (900, 900))]
for name, az, tgt, d, size in views:
    render_view(scene, cam, rig, f"{prefix}_{body}_{name}.png", tgt, az, d, size=size, samples=32)
