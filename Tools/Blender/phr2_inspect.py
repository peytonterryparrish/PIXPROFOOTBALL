"""PH2: inspect one of the PPF_NEW player FBX (or R1 glb): meshes, tris, bbox, armature heads.  blender -b --python phr2_inspect.py -- <file>"""
import bpy, sys, numpy as np
from mathutils import Vector
src = sys.argv[sys.argv.index('--')+1]
bpy.ops.wm.read_factory_settings(use_empty=True)
if src.lower().endswith('.glb'):
    bpy.ops.import_scene.gltf(filepath=src)
else:
    bpy.ops.import_scene.fbx(filepath=src)
bpy.context.view_layer.update()
print("=== OBJECTS")
for o in bpy.data.objects:
    info=''
    if o.type=='MESH':
        me=o.data; me.calc_loop_triangles()
        co=np.array([(o.matrix_world@v.co)[:] for v in me.vertices])
        mn=co.min(0); mx=co.max(0)
        info=f"verts={len(me.vertices)} polys={len(me.polygons)} tris={len(me.loop_triangles)} mats={[m.name if m else None for m in me.materials]} uv={[u.name for u in me.uv_layers]} vg={len(o.vertex_groups)} bbox={np.round(mn,3).tolist()}-{np.round(mx,3).tolist()} sk={len(me.shape_keys.key_blocks) if me.shape_keys else 0}"
    print("OBJ", o.name, o.type, 'parent=',o.parent.name if o.parent else None, 'scale',tuple(round(x,4) for x in o.scale), info)
for a in [o for o in bpy.data.objects if o.type=='ARMATURE']:
    print("=== ARMATURE", a.name, len(a.data.bones), "scale", tuple(a.scale), tuple(round(x,3) for x in a.rotation_euler))
    for b in a.data.bones:
        if b.name in ("rig:Hips","rig:Spine","rig:Spine2","rig:Neck","rig:Head","rig:LeftShoulder","rig:LeftArm","rig:LeftForeArm","rig:LeftHand","rig:LeftUpLeg","rig:LeftLeg","rig:LeftFoot","rig:LeftToeBase"):
            print('BONE', b.name, tuple(round(x,4) for x in (a.matrix_world@b.head_local)))
