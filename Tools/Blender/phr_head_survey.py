"""PHR: contact sheet of every Draft Fever head shape key (MediumPlayer.fbx, 28 shapes, shared topology + UV).
blender -b --python Tools/Blender/phr_head_survey.py -- <out_dir>
Writes <out_dir>/shape_<NN>_{front,side}.png and prints the world bbox / eye centre of the basis so the fit onto R1 can be derived."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *

out = args()[0]
ensure(out)
reset()
scene = setup_stage(floor=False)
bpy.ops.import_scene.fbx(filepath=DF + "/Bodies/MediumPlayer.fbx")
objs = {o.name: o for o in bpy.data.objects if o.type == 'MESH'}
head = objs["Head_BlendShape_Medium_Player"]
eyes = objs["Eyes_BlendShape_Medium_Player"]
for n, o in list(objs.items()):
    if o not in (head, eyes):
        bpy.data.objects.remove(o, do_unlink=True)
# kill armature modifiers (rest pose is what we want, evaluate in world space)
for o in (head, eyes):
    for m in list(o.modifiers):
        o.modifiers.remove(m)
bpy.context.view_layer.update()
mn, mx = bbox([head])
print("HEAD world bbox", mn, mx)
emn, emx = bbox([eyes])
print("EYES world bbox", emn, emx, "centre", (emn + emx) / 2)

# neutral albedo for the survey
tex = DF + "/Bodies/mediumPlayer.fbm/T_Head_BC_01.png"
m = bpy.data.materials.new("skin")
m.use_nodes = True
t = m.node_tree.nodes.new("ShaderNodeTexImage")
t.image = load_img(tex)
m.node_tree.links.new(t.outputs[0], m.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
head.data.materials.clear()
head.data.materials.append(m)
for p in head.data.polygons:
    p.use_smooth = True
em = bpy.data.materials.new("eye")
em.use_nodes = True
et = em.node_tree.nodes.new("ShaderNodeTexImage")
et.image = load_img(DF + "/Bodies/mediumPlayer.fbm/Eyes_MAT_Base_color.png")
em.node_tree.links.new(et.outputs[0], em.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
eyes.data.materials.clear()
eyes.data.materials.append(em)

rig = make_rig(scene, target=(0, 0, 1.7))
cam = make_camera(scene)
c = (mn + mx) / 2
names = [k.name for k in head.data.shape_keys.key_blocks]
ekeys = eyes.data.shape_keys.key_blocks
for i, nm in enumerate(names):
    for kb in head.data.shape_keys.key_blocks:
        kb.value = 1.0 if kb.name == nm and nm != "Basis" else 0.0
    for kb in ekeys:
        kb.value = 1.0 if kb.name.replace("Eyes_", "") .replace("_SubD01", "") == nm.replace("_SubD01", "").replace("._", "_") and nm != "Basis" else 0.0
    tag = "%02d_%s" % (i, nm.replace("Head_", "").replace("_SubD01", "").replace(".", ""))
    for az, nme in ((0, "front"), (90, "side")):
        render_view(scene, cam, rig, f"{out}/shape_{tag}_{nme}.png", (c.x, c.y, c.z), az, 1.0, size=(400, 480), samples=8)
print("DONE")
