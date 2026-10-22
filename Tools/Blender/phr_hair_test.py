"""blender -b --python phr_hair_test.py -- <out_prefix> [--textures]"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_dress import *
import phr_build as B
import phr_hair as H

a = args()
prefix = a[0]
reset()
scene = setup_stage(floor=False)
arm, meshes = import_r1()
head, eyes = B.fit_head(arm, "Medium")
B.weight_head(head, eyes, arm)
if "--textures" in a:
    H.write_textures(head)
shells = {n: H.make_shell(head, n, *H.SHELLS[n]) for n in H.SHELLS}
for n, o in shells.items():
    print("SHELL", n, "verts", len(o.data.vertices), "tris", B.tri_count(o.data))
for o in meshes.values():
    o.hide_render = True


def hair_mat(tex, rgb):
    m = bpy.data.materials.new("hair")
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = load_img(f"{H.HAIR_OUT}/{tex}.png")
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = 'RGBA'
    mix.blend_type = 'MULTIPLY'
    mix.inputs[0].default_value = 1.0
    mix.inputs[7].default_value = (*srgb_to_lin(rgb), 1)
    nt.links.new(t.outputs["Color"], mix.inputs[6])
    nt.links.new(mix.outputs[2], b.inputs["Base Color"])
    nt.links.new(t.outputs["Alpha"], b.inputs["Alpha"])
    b.inputs["Roughness"].default_value = 0.6
    return m


skin = tex_material("head", f"{SKIN}/PHR_Head_T3.png", f"{SKIN}/PHR_Head_N.png", 0.45, 0.8)
assign(head, skin)
assign(eyes, tex_material("eyes", DF + "/Bodies/mediumPlayer.fbm/Eyes_MAT_Base_color.png", None, 0.15))
rig = make_rig(scene, target=(0, 0, 1.7))
cam = make_camera(scene)
combos = [("stubble", "normal", "buzz", (0.07, 0.045, 0.03), 3), ("full", "thick", "short", (0.03, 0.02, 0.015), 6),
          ("goatee", "thin", "buzz", (0.25, 0.16, 0.08), 1), ("moustache", "normal", "short", (0.45, 0.30, 0.15), 0)]
for i, (beard, brow, scalp, col, tone) in enumerate(combos):
    head.data.materials.clear()
    head.data.materials.append(tex_material("head%d" % i, f"{SKIN}/PHR_Head_T{tone}.png", f"{SKIN}/PHR_Head_N.png", 0.45, 0.8))
    assign(shells["Beard"], hair_mat(f"PHR_Beard_{beard}", col))
    assign(shells["Brows"], hair_mat(f"PHR_Brow_{brow}", col))
    assign(shells["Scalp"], hair_mat(f"PHR_Scalp_{scalp}", col))
    render_view(scene, cam, rig, f"{prefix}_{i}_front.png", (0, -0.03, 1.72), 0, 0.8, size=(600, 700), samples=24)
    render_view(scene, cam, rig, f"{prefix}_{i}_3q.png", (0, -0.03, 1.72), 40, 0.8, size=(600, 700), samples=24)
