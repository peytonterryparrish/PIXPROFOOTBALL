"""PHR: body-type measurements (LOD0, rest pose) for the log: shoulder / chest / waist (jersey), hip / thigh / calf (pants + socks), arms.
blender -b --python Tools/Blender/phr_body_table.py"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *

rows = []
for body in ("Small", "Medium", "Large"):
    reset()
    bpy.ops.import_scene.fbx(filepath=OUT_ASSET + f"/Meshes/PHR_{body}.fbx")
    ob = {o.name: o for o in bpy.data.objects if o.type == 'MESH'}
    for o in ob.values():
        for m in list(o.modifiers):
            o.modifiers.remove(m)
    j = [ob["Jersey_LOD0"]]
    legs = [ob["Pants_LOD0"], ob["Socks_LOD0"], ob["Undersock_LOD0"]]
    r = {}
    r["shoulders (jersey pads, z1.52) w/d/circ"] = measure(j, 1.52)
    r["chest (jersey, z1.30)"] = measure(j, 1.30)
    r["waist (jersey, z1.15)"] = measure(j, 1.15)
    r["hips (pants, z1.02)"] = measure(legs, 1.02)
    r["thigh (one leg, z0.85)"] = measure(legs, 0.85, 'L')
    r["knee-calf (one leg, z0.45)"] = measure(legs, 0.45, 'L')
    r["calf (one leg, z0.30)"] = measure(legs, 0.30, 'L')
    mn, mx = bbox([ob["Arms_LOD0"], ob["SleeveR_LOD0"], ob["Jersey_LOD0"]])
    r["span x (arms+jersey)"] = (mx.x - mn.x, 0, 0)
    for k, v in r.items():
        print("BT %-8s %-42s w %.3f d %.3f circ %.3f" % (body, k, *(v if v else (0, 0, 0))))
