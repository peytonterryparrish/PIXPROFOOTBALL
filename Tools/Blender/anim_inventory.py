"""List the actions (animation takes) inside .blend files without opening the scenes.

    blender -b --python anim_inventory.py -- <out.json> <file1.blend> [file2.blend ...]

For every .blend: the action names with frame range, fps and the number of F-curves (loaded as data blocks only: no objects, fast even for multi-GB files).
Used by the ANIMLIB track (v0.10.0) to find takes that were never exported to FBX / imported into the game.
"""
import bpy, sys, os, json, time

argv = sys.argv[sys.argv.index('--') + 1:]
out = argv[0]
files = argv[1:]
res = {}
for p in files:
    t0 = time.time()
    try:
        with bpy.data.libraries.load(p) as (df, dt):
            names = list(df.actions)
            dt.actions = names
        acts = []
        for a in dt.actions:
            if a is None:
                continue
            try:
                fr = a.frame_range
                nf = len(a.fcurves) if hasattr(a, 'fcurves') else 0
                acts.append({'n': a.name, 's': round(fr[0], 1), 'e': round(fr[1], 1), 'f': nf})
            except Exception as ex:
                acts.append({'n': a.name, 's': 0, 'e': 0, 'f': -1})
        fps = None
        res[p] = {'actions': acts, 'sec': round(time.time() - t0, 1), 'size': os.path.getsize(p)}
        for a in list(bpy.data.actions):
            bpy.data.actions.remove(a)
    except Exception as ex:
        res[p] = {'error': str(ex)}
    print('[inv]', os.path.basename(p), len(res[p].get('actions', [])), res[p].get('sec'), flush=True)
    with open(out, 'w') as f:
        json.dump(res, f)
print('[inv] done')
