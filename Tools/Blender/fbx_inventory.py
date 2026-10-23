"""List the animation takes of FBX files (armature bone count, actions with frame range, scene fps).

    blender -b --python fbx_inventory.py -- <out.json> <file1.fbx> [file2.fbx ...]

ANIMLIB track (v0.10.0): finds out which source FBX files hold takes that the game never imported and which skeleton family they use
(clean 95-bone ARP deform rig, 480-bone ARP control rig, Motus, or something else).
"""
import bpy, sys, os, json, time

argv = sys.argv[sys.argv.index('--') + 1:]
out = argv[0]
files = argv[1:]
res = {}
for p in files:
    t0 = time.time()
    try:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=p, automatic_bone_orientation=False, use_anim=True, ignore_leaf_bones=False)
        arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
        meshes = [o for o in bpy.data.objects if o.type == 'MESH']
        acts = []
        for a in bpy.data.actions:
            fr = a.frame_range
            acts.append({'n': a.name, 's': round(fr[0], 1), 'e': round(fr[1], 1)})
        bones = max([len(o.data.bones) for o in arms]) if arms else 0
        names = []
        if arms:
            big = max(arms, key=lambda o: len(o.data.bones))
            names = [b.name for b in big.data.bones][:6]
        res[p] = {'actions': acts, 'bones': bones, 'arms': len(arms), 'meshes': len(meshes), 'fps': bpy.context.scene.render.fps, 'bone_head': names,
                  'sec': round(time.time() - t0, 1), 'size': os.path.getsize(p)}
    except Exception as ex:
        res[p] = {'error': str(ex)[:200]}
    print('[inv]', os.path.basename(p), res[p].get('bones'), len(res[p].get('actions', [])), res[p].get('sec'), flush=True)
    with open(out, 'w') as f:
        json.dump(res, f)
print('[inv] done')
