"""Print the armatures of the .blend files given after `--` (name, bone count, first bones, active action, scene fps)."""
import bpy, sys, os

for p in sys.argv[sys.argv.index('--') + 1:]:
    bpy.ops.wm.open_mainfile(filepath=p)
    print('=== ', os.path.basename(p), 'fps', bpy.context.scene.render.fps, 'frames', bpy.context.scene.frame_start, bpy.context.scene.frame_end)
    for o in bpy.data.objects:
        if o.type == 'ARMATURE':
            bn = [b.name for b in o.data.bones]
            act = o.animation_data.action.name if o.animation_data and o.animation_data.action else None
            sc = tuple(round(x, 3) for x in o.scale)
            print('  ARM', o.name, 'bones', len(bn), 'scale', sc, 'act', act, 'head', bn[:4])
    print('  actions', len(bpy.data.actions), 'meshes', sum(1 for o in bpy.data.objects if o.type == 'MESH'))
