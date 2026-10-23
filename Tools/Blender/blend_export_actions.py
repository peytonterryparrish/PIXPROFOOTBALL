"""Export single actions of a .blend as one FBX per action (armature only), the way the studio's own FBX exports were made.

    blender -b --python blend_export_actions.py -- <out_dir> <blend> [--obj <regex>] [--only <regex>] [--fps N]

The exported FBX files then go through retarget_actions.py (the proven FBX route). v0.10.0 ANIMLIB: baking straight from a library-loaded armature collapsed the pose
(the armature object carries a 90 degree rotation, an offset location and a 0.01 scale), the FBX route does not.
"""
import bpy, sys, os, re

argv = sys.argv[sys.argv.index('--') + 1:]
out_dir, blend = argv[0], argv[1]
objre, only, fps_override = re.compile('^root', re.I), None, 0
i = 2
while i < len(argv):
    if argv[i] == '--obj': objre = re.compile(argv[i + 1], re.I); i += 2
    elif argv[i] == '--only': only = re.compile(argv[i + 1], re.I); i += 2
    elif argv[i] == '--fps': fps_override = int(argv[i + 1]); i += 2
    else: i += 1
os.makedirs(out_dir, exist_ok=True)


def clean_name(n):
    n = n.split('|')[-1]
    return re.sub(r'[^A-Za-z0-9_\.\-]+', '_', n)


bpy.ops.wm.read_factory_settings(use_empty=True)
with bpy.data.libraries.load(blend, link=False) as (df, dt):
    dt.objects = [n for n in df.objects if objre.search(n)]
    dt.actions = list(df.actions)
    dt.scenes = [df.scenes[0]] if df.scenes else []
sc0 = dt.scenes[0] if dt.scenes else None
fps = int(fps_override or (sc0.render.fps if sc0 else 30))
if sc0 is not None: bpy.data.scenes.remove(sc0)
bpy.context.scene.render.fps = fps
for o in dt.objects:
    if o is not None: bpy.context.scene.collection.objects.link(o)
arms = [o for o in dt.objects if o is not None and o.type == 'ARMATURE' and 'root.x' in {re.sub(r'^[0-9]+:', '', b.name) for b in o.data.bones}]
A = max(arms, key=lambda o: len(o.data.bones))
for o in list(bpy.data.objects):
    if o is not A: bpy.data.objects.remove(o, do_unlink=True)
bones = {b.name for b in A.data.bones}


def action_bones(act):
    names = set()
    rx = re.compile(r'pose\.bones\["([^"]+)"\]')
    try:
        for layer in act.layers:
            for strip in layer.strips:
                for bag in strip.channelbags:
                    for fc in bag.fcurves:
                        m = rx.match(fc.data_path)
                        if m: names.add(m.group(1))
    except Exception:
        pass
    return names


A.animation_data_create()
seen = set()
n = 0
for act in sorted(dt.actions, key=lambda a: ('Key' in a.name.split('|')[0], a.name)):
    if act is None: continue
    nm = clean_name(act.name)
    if nm in seen: continue
    if only and not only.search(nm): continue
    if len(action_bones(act) & bones) < 8: continue
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    if f1 - f0 < 2: continue
    seen.add(nm)
    out = os.path.join(out_dir, nm + '.fbx')
    A.animation_data.action = act
    try:
        if len(act.slots) > 0: A.animation_data.action_slot = act.slots[0]
    except Exception:
        pass
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = f0, f1
    bpy.ops.object.select_all(action='DESELECT')
    A.select_set(True); bpy.context.view_layer.objects.active = A
    bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE'}, bake_anim=True, bake_anim_use_all_actions=False,
                             bake_anim_use_nla_strips=False, bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                             add_leaf_bones=False, axis_forward='-Z', axis_up='Y')
    n += 1
    print('EXPORTED', nm, f0, f1, 'fps', fps)
print('=====DONE', n)
