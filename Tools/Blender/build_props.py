"""Export the hand-held coach props as plain FBX meshes.   blender -b --python build_props.py -- <out_dir>"""
import bpy, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import pix_props, pix_common as pc
out = sys.argv[sys.argv.index("--") + 1]
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
for fn, name in ((pix_props.clipboard, "Coach_Clipboard"), (pix_props.tablet, "Coach_Tablet")):
    ob = fn(name)
    pc.export_fbx(os.path.join(out, name + ".fbx"), [ob])
    bpy.data.objects.remove(ob)
    print("EXPORTED", name)
