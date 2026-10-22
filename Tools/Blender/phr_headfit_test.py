"""PHR: fit a Draft Fever head into R1 (head removed) and render close-ups. blender -b --python phr_headfit_test.py -- <out_prefix> [shape]"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *

a = args()
prefix = a[0]
shape = a[1] if len(a) > 1 else "Head_11_SubD01"
reset()
scene = setup_stage()
arm, meshes = import_r1()
for n in ("head", "eyes", "eyelashes"):
    bpy.data.objects.remove(meshes.pop(n), do_unlink=True)
r1_eye_c = Vector((0.0045 - 0.0105, -0.061, 1.747 + 0.004))
head, eyes = import_df_head()
emn, emx = bbox([eyes])
df_eye_c = (emn + emx) / 2
s = 0.969
print("df eye centre", df_eye_c)
for o in (head, eyes):
    for kb in o.data.shape_keys.key_blocks:
        pass
    M = Matrix.Translation(r1_eye_c) @ Matrix.Scale(s, 4) @ Matrix.Translation(-df_eye_c)
    o.data.transform(M, shape_keys=True)
for o, key in ((head, shape), (eyes, shape.replace("Head_", "Head_Eyes_").replace("_SubD01", ""))):
    for kb in o.data.shape_keys.key_blocks:
        kb.value = 1.0 if kb.name == key else 0.0
# materials
def mat(tex, name):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    t = m.node_tree.nodes.new("ShaderNodeTexImage")
    t.image = load_img(tex)
    m.node_tree.links.new(t.outputs[0], m.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    return m
hm = mat(DF + "/Bodies/mediumPlayer.fbm/T_Head_BC_01.png", "skin")
head.data.materials.clear(); head.data.materials.append(hm)
em = mat(DF + "/Bodies/mediumPlayer.fbm/Eyes_MAT_Base_color.png", "eye")
eyes.data.materials.clear(); eyes.data.materials.append(em)
for o in (head, eyes):
    for p in o.data.polygons:
        p.use_smooth = True
    o.parent = arm
    for m in o.modifiers:
        pass
rig = make_rig(scene)
cam = make_camera(scene)
for name, az, tgt, d in [("front", 0, (0.0, -0.03, 1.72), 1.0), ("3q", 35, (0.0, -0.03, 1.72), 1.0), ("side", 90, (0.0, -0.03, 1.70), 1.0),
                         ("neck", 40, (0.0, 0.0, 1.62), 0.9), ("back", 180, (0.0, 0.0, 1.65), 1.0)]:
    render_view(scene, cam, rig, f"{prefix}_{name}.png", tgt, az, d, size=(800, 800), samples=32)
# cutaway: hide helmet + mask to see the head
for n in ("helmet", "facemask", "chinstrap", "Padding"):
    meshes[n].hide_render = True
for name, az, tgt, d in [("nohelm_front", 0, (0.0, -0.03, 1.68), 0.9), ("nohelm_side", 90, (0.0, -0.03, 1.66), 0.9), ("nohelm_3q", 35, (0.0, -0.03, 1.66), 0.9)]:
    render_view(scene, cam, rig, f"{prefix}_{name}.png", tgt, az, d, size=(800, 800), samples=32)
