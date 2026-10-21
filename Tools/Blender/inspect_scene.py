"""Dump objects / armatures / actions of a .blend or .fbx (compact). Usage:
blender -b --python inspect_scene.py -- <file> [out.txt]
env: MAXO (max objects listed, 80), MAXA (max actions, 60), BONES=1 (list bones of armatures)
"""
import bpy, sys, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
path = argv[0]
out = argv[1] if len(argv) > 1 else None
lines = []
def P(*a):
    lines.append(" ".join(str(x) for x in a))

if path.lower().endswith(".fbx"):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
else:
    bpy.ops.wm.open_mainfile(filepath=path)

P("FILE", path)
P("scene fps", bpy.context.scene.render.fps, "frames", bpy.context.scene.frame_start, bpy.context.scene.frame_end)
P("collections:", [(c.name, len(c.objects)) for c in bpy.data.collections][:40])
MAXO = int(os.environ.get("MAXO", "80"))
for idx, o in enumerate(bpy.data.objects):
    if idx >= MAXO:
        P("... (%d objects total)" % len(bpy.data.objects))
        break
    d = ""
    if o.type == 'MESH':
        d = "verts=%d mats=%s mods=%s vg=%d" % (len(o.data.vertices), [m.name if m else None for m in o.data.materials][:3], [m.type for m in o.modifiers], len(o.vertex_groups))
        bb = [o.matrix_world @ Vector(c) for c in o.bound_box]
        d += " z=(%.2f..%.2f) x=(%.2f..%.2f)" % (min(v.z for v in bb), max(v.z for v in bb), min(v.x for v in bb), max(v.x for v in bb))
    elif o.type == 'ARMATURE':
        d = "bones=%d" % len(o.data.bones)
    P("OBJ", o.type, repr(o.name), "parent=", o.parent.name if o.parent else None, "scale=", tuple(round(x, 3) for x in o.scale), "loc=", tuple(round(x, 2) for x in o.location), d)
MAXA = int(os.environ.get("MAXA", "60"))
P("actions (%d total):" % len(bpy.data.actions), [(a.name, tuple(int(x) for x in a.frame_range)) for a in bpy.data.actions][:MAXA])
for o in bpy.data.objects:
    if o.type == 'ARMATURE':
        P("ARMATURE", o.name, "action=", o.animation_data.action.name if o.animation_data and o.animation_data.action else None)
        if os.environ.get("BONES", "0") == "1":
            for b in o.data.bones:
                P("  bone", b.name, "parent=", b.parent.name if b.parent else None, "head=(%.3f %.3f %.3f)" % tuple(o.matrix_world @ b.head_local))
txt = "\n".join(lines)
if out:
    open(out, "w", encoding="utf-8").write(txt)
print(txt)
