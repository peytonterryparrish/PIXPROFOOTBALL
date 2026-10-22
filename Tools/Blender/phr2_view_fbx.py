"""PH2: render a built body FBX (rest pose, LOD n) from several views.  blender -b --python phr2_view_fbx.py -- <fbx> <tag> [lod=0] [views=front,34,side] [hide=Pads]"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *
import phr2_viz as V
a = args()
src, tag = a[0], a[1]
kw = dict(x.split("=") for x in a[2:])
lod = int(kw.get("lod", 0)); views = kw.get("views", "front,34,side").split(","); hide = tuple(h for h in kw.get("hide", "Pads").split(",") if h)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
bpy.context.view_layer.update()
show = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.endswith("_LOD%d" % lod) and not o.name.startswith(("Head_Beard", "Head_Brows", "Head_Scalp", "Visor"))]
mn, mx = bbox(show)
c = ((mn.x + mx.x) / 2, (mn.y + mx.y) / 2, (mn.z + mx.z) / 2)
print("VIEW bbox", mn, mx)
outd = "D:/PIXFootball/Renders/roster_v2/_work/"
for v in views:
    V.render(outd + "fbx_%s_LOD%d_%s.png" % (tag, lod, v), show, c, v, scale=(mx.z - mn.z) * 1.15, size=(700, 1000), hide=hide)
