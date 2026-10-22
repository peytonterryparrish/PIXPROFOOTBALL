"""Re-bake EVERY action of one or more Auto-Rig-Pro FBX files onto the clean deform skeleton and export one FBX per action.

    blender -b --python retarget_actions.py -- <clean_skeleton.fbx> <out_dir> <prefix> [--only <regex>] [--skip <regex>] [--force] <src1.fbx> [src2.fbx ...]

* The clean skeleton is the Auto-Rig-Pro deform skeleton of StiffArm_14.fbx (the same one the player bodies use).
* The source character is scaled so the hip height matches the clean skeleton (0.907 m), 'copy world loc/rot' constraints drive the clean
  bones, the result is baked at the source frame rate, and the take is named <prefix>__<action name> (duplicate 'Key|' / 'Legacy Slot' variants
  of the same action are written once).
"""
import bpy, sys, os, re, time

argv = sys.argv[sys.argv.index('--') + 1:]
clean_fbx, out_dir, prefix = argv[0], argv[1], argv[2]
rest = argv[3:]
only = skip = None
force = False                      # QA-929: --force re-bakes and overwrites outputs that already exist
files = []
i = 0
while i < len(rest):
    if rest[i] == '--only': only = re.compile(rest[i + 1], re.I); i += 2
    elif rest[i] == '--skip': skip = re.compile(rest[i + 1], re.I); i += 2
    elif rest[i] == '--force': force = True; i += 1
    else: files.append(rest[i]); i += 1
os.makedirs(out_dir, exist_ok=True)


def import_fbx(p):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=p, automatic_bone_orientation=False, use_anim=True, ignore_leaf_bones=False)
    return [o for o in bpy.data.objects if o not in before]


def purge(objs):
    for o in objs:
        try: bpy.data.objects.remove(o, do_unlink=True)
        except Exception: pass


def clean_name(n):
    n = n.split('|')[-1]
    n = re.sub(r'_Legacy Slot$', '', n)
    n = re.sub(r'[^A-Za-z0-9_\.\-]+', '_', n)
    return n


def candidates(n):
    c = [n]
    if n.startswith('c_'): c.append(n[2:])
    return c


bpy.ops.wm.read_factory_settings(use_empty=True)
objs = import_fbx(clean_fbx)
B = [o for o in objs if o.type == 'ARMATURE'][0]
purge([o for o in objs if o.type != 'ARMATURE'])
B.animation_data_clear()
for a in list(bpy.data.actions): bpy.data.actions.remove(a)


def sname(o):
    """map of un-prefixed bone name -> actual bone name (Maya-style imports prefix every bone with "1:")"""
    return {re.sub(r"^[0-9]+:", "", b.name): b.name for b in o.data.bones}


def ground_z(o):
    cb = o.data.bones.get(sname(o).get("c_traj", "c_traj"))
    return (o.matrix_world @ cb.matrix_local).translation.z if cb else 0.0


total = 0
for src in files:
    t_src = time.time()
    try:
        before_actions = set(bpy.data.actions)
        objs = import_fbx(src)
        arms = [o for o in objs if o.type == 'ARMATURE']
        if not arms or "root.x" not in sname(arms[0]):
            print("SKIP (not ARP)", src); purge(objs); continue
        A = arms[0]
        fps = bpy.context.scene.render.fps
        new_actions = [a for a in bpy.data.actions if a not in before_actions]
        purge([o for o in objs if o.type != 'ARMATURE'])
        SA = sname(A)
        hipA = (A.matrix_world @ A.data.bones[SA["root.x"]].matrix_local).translation.z - ground_z(A)
        hipB = (B.matrix_world @ B.data.bones["root.x"].matrix_local).translation.z - ground_z(B)
        s = hipB / hipA
        A.scale = (A.scale[0] * s, A.scale[1] * s, A.scale[2] * s)
        bpy.context.view_layer.update()
        print("SRC", os.path.basename(src), "fps", fps, "hip %.3f -> %.3f x%.3f" % (hipA, hipB, s), "actions", len(new_actions))
        # constraints on B (once)
        bpy.ops.object.select_all(action='DESELECT')
        bpy.context.view_layer.objects.active = B
        B.select_set(True)
        bpy.ops.object.mode_set(mode='POSE')
        for pb in B.pose.bones:
            for c in list(pb.constraints): pb.constraints.remove(c)
            for cand in candidates(pb.name):
                if cand in SA:
                    for ctype in ("COPY_LOCATION", "COPY_ROTATION"):
                        con = pb.constraints.new(ctype)
                        con.target = A; con.subtarget = SA[cand]
                        con.target_space = "WORLD"; con.owner_space = "WORLD"
                    break
        bpy.ops.object.mode_set(mode='OBJECT')
        seen = set()
        if A.animation_data is None: A.animation_data_create()
        # QA-930: the take name is the cleaned action name; only a file with exactly ONE distinct cleaned action name (Key| duplicates and Legacy Slot copies count as the same
        # action) takes the file name (keeps the _A / _B suffix of the studio takes)
        distinct = {clean_name(a.name) for a in new_actions}
        single = len(distinct) == 1
        for act in sorted(new_actions, key=lambda a: (('Key' in a.name.split('|')[0]), a.name)):
            nm = clean_name(act.name)
            if single:
                nm = clean_name(os.path.splitext(os.path.basename(src))[0])
            if nm in seen: continue
            seen.add(nm)
            if only and not only.search(nm): continue
            if skip and skip.search(nm): continue
            f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
            if f1 - f0 < 2: continue
            out_name = prefix + "__" + nm
            out_path = os.path.join(out_dir, out_name + ".fbx")
            if os.path.exists(out_path) and not force: continue
            t0 = time.time()
            A.animation_data.action = act
            bpy.context.scene.frame_start, bpy.context.scene.frame_end = f0, f1
            bpy.ops.object.select_all(action='DESELECT')
            bpy.context.view_layer.objects.active = B
            B.select_set(True)
            bpy.ops.object.mode_set(mode='POSE')
            bpy.ops.pose.select_all(action='SELECT')
            bpy.ops.nla.bake(frame_start=f0, frame_end=f1, step=1, only_selected=False, visual_keying=True,
                             clear_constraints=False, clear_parents=False, use_current_action=False, bake_types={'POSE'})
            baked = B.animation_data.action
            baked.name = out_name
            bpy.ops.object.mode_set(mode='OBJECT')
            bpy.context.scene.name = out_name
            bpy.ops.object.select_all(action='DESELECT')
            B.select_set(True)
            bpy.context.view_layer.objects.active = B
            bpy.ops.export_scene.fbx(
                filepath=out_path, use_selection=True, object_types={'ARMATURE'},
                global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
                bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                bake_anim_use_all_bones=True, bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
                add_leaf_bones=False, primary_bone_axis='Y', secondary_bone_axis='X', armature_nodetype='NULL',
                axis_forward='-Z', axis_up='Y', use_armature_deform_only=False)
            B.animation_data_clear()
            bpy.data.actions.remove(baked)
            print("OK", out_name, f0, f1, "fps", fps, "%.1fs" % (time.time() - t0))
            total += 1
        # cleanup source
        bpy.ops.object.mode_set(mode='POSE')
        for pb in B.pose.bones:
            for c in list(pb.constraints): pb.constraints.remove(c)
            pb.location = (0, 0, 0); pb.rotation_quaternion = (1, 0, 0, 0); pb.rotation_euler = (0, 0, 0)
        bpy.ops.object.mode_set(mode='OBJECT')
        purge(objs)
        for a in list(bpy.data.actions):
            if a.users == 0 or a in new_actions:
                try: bpy.data.actions.remove(a)
                except Exception: pass
        print("SRC DONE", os.path.basename(src), "%.1fs" % (time.time() - t_src))
    except Exception as e:
        import traceback; traceback.print_exc()
        print("FAIL", src, e)
        try: bpy.ops.object.mode_set(mode='OBJECT')
        except Exception: pass
print("=====DONE", total)
