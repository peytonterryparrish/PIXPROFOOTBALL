import bpy, sys, math
from mathutils import Vector
src = sys.argv[sys.argv.index('--')+1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
print("=== OBJECTS")
for o in bpy.data.objects:
    d = o.type
    info = ''
    if o.type=='MESH':
        me=o.data
        me.calc_loop_triangles()
        mn=Vector((1e9,)*3); mx=Vector((-1e9,)*3)
        for v in me.vertices:
            w=o.matrix_world@v.co
            mn=Vector(map(min,mn,w)); mx=Vector(map(max,mx,w))
        info=f"verts={len(me.vertices)} tris={len(me.loop_triangles)} mats={[m.name if m else None for m in me.materials]} uv={[u.name for u in me.uv_layers]} vgroups={len(o.vertex_groups)} bbox=({mn.x:.3f},{mn.y:.3f},{mn.z:.3f})-({mx.x:.3f},{mx.y:.3f},{mx.z:.3f}) shapekeys={len(me.shape_keys.key_blocks) if me.shape_keys else 0} mods={[m.type for m in o.modifiers]}"
    print(o.name, d, 'parent=',o.parent.name if o.parent else None, 'scale',tuple(round(x,3) for x in o.scale), info)
arm=[o for o in bpy.data.objects if o.type=='ARMATURE']
for a in arm:
    print("=== ARMATURE", a.name, len(a.data.bones), "scale", tuple(a.scale), tuple(a.rotation_euler))
    for b in a.data.bones:
        print(' ', b.name, 'parent=', b.parent.name if b.parent else None, 'head', tuple(round(x,3) for x in (a.matrix_world@b.head_local)))
