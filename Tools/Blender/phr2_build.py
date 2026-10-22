"""PH2: photoreal body build v2 = phr_build.build + shoulder pads + extra body base.

  blender -b --python Tools/Blender/phr2_build.py -- <Small|Medium|Large> <Std|Stocky|Athletic|Tall|Runner> [--out <dir>] [--no-export] [--no-pads]

Differences from phr_build.build (which stays untouched and keeps producing the live v0.6.0 bodies):
  * the shoulder pads are fitted + skinned in the R1 frame and reshaped with the body (phr2_pads), the old pad "bump" of phr_body is switched off
  * LODs for the pads (the jersey LODs come from the R1-frame jersey as before)
  * the base warp (phr2_warp.Field: displacement field R1 -> purchased body, new rest skeleton) is applied to every part / LOD
  * jersey (every LOD): sleeve extension + conform over the pad envelope, skin weights blended towards the pad weights
  * FBX name PHR_<Size>_<Base>.fbx
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phr_build as B
from phr_build import *
import phr2_pads as PD
import phr2_warp as W

STAGING = os.path.join(OUT_ASSET, "Staging_v07", "Meshes")
B.LOD0_TRIS["Pads"] = 4000          # hidden under the jersey (only the neck roll can show): the 47k-tri store pads need no more
B.LOD_MIN["Pads"] = [2000, 900, 400]
B.NO_SEAM_PARTS.add("Pads")
PAD_SLEEVE_EXT = 0.0


def build2(body, base, out_dir, export=True, pads=True):
    reset()
    scene = bpy.context.scene
    arm, meshes = import_r1()
    for n in ("head", "eyes", "eyelashes"):
        bpy.data.objects.remove(meshes.pop(n), do_unlink=True)
    objs = {}
    for k, o in list(meshes.items()):
        o.name = B.PARTS[k]
        o.data.name = B.PARTS[k]
        objs[B.PARTS[k]] = o
        keep_first_uv(o)
    body_key = body
    if base == "Athletic":
        # PH2-009: the purchased Athletic body is almost R1 (<= 1.4 cm), so the base gets its own proportions on top of the field: V-taper (broad lats, narrow waist),
        # sprinter legs (lean thigh, full calf) and a little more arm.
        import copy
        A = copy.deepcopy(phr_body.BODY[body])
        mod = {0.95: (0.93, 0.92), 1.10: (0.90, 0.90), 1.25: (1.00, 0.97), 1.40: (1.06, 1.00), 1.60: (1.00, 1.00)}
        A["torso"] = [(z, sx * mod[z][0], sy * mod[z][1]) for z, sx, sy in A["torso"]]
        lmod = {1.05: 0.92, 0.85: 0.93, 0.60: 0.97, 0.40: 1.08, 0.12: 1.0}
        A["leg"] = [(z, f * lmod[z]) for z, f in A["leg"]]
        A["arm"] = [(t, f * (1.05 if 0.1 < t < 0.7 else 1.0)) for t, f in A["arm"]]
        body_key = body + "_Ath"
        phr_body.BODY[body_key] = A
    P = phr_body.BODY[body_key]
    saved_pad = (P["pad"], P["pad_up"])
    if pads:
        P["pad"] = 0.0
        P["pad_up"] = 0.0
        padobj = PD.make_pads(arm)
        objs["Pads"] = padobj
    skinned = {n: o for n, o in objs.items() if n not in B.RIGID}
    phr_body.reshape(arm, skinned, body_key)
    P["pad"], P["pad_up"] = saved_pad
    head, eyes = fit_head(arm, body)
    neck_thicken(head, body)
    head.name, eyes.name = "Head", "Eyes"
    head.data.name, eyes.data.name = "Head", "Eyes"
    weight_head(head, eyes, arm)
    sub0 = subdivide_with_shapes(head)
    sub = head_lod0_from_sub(sub0, B.HEAD_LOD0_TRIS, arm, head)
    bpy.data.objects.remove(sub0, do_unlink=True)
    for n, o in objs.items():
        if n == "Pads":
            continue
        if n == "Arms":
            set_material(o, ["PHR_Arms", "PHR_Hands"])
        else:
            set_material(o, ["PHR_" + n])
    set_material(head, ["PHR_Head"])
    set_material(sub, ["PHR_Head"])
    set_material(eyes, ["PHR_Eyes"])
    split_hands(objs["Arms"])
    for o in list(objs.values()) + [head, sub, eyes]:
        clear_normals_and_smooth(o)
    for n, t in B.LOD0_TRIS.items():
        if n in objs:
            before = tri_count(objs[n].data)
            decimate_to(objs[n], t, extra_protect=material_border_vertices if n in ("Arms", "Pads") else None, seam=n not in B.NO_SEAM_PARTS)
            log("LOD0", n, before, "->", tri_count(objs[n].data))
    lod0 = dict(objs)
    lod0["Head"] = sub
    lod0["Eyes"] = eyes
    head.name = "Head_cage"
    sets = {}
    for n, o in lod0.items():
        sets[n] = [o]
    for n, o in lod0.items():
        base_t = tri_count(o.data)
        for li, r in enumerate(B.LOD_RATIOS, start=1):
            if n == "Eyes":
                if li > 1:
                    continue
                lo = head_lod1(o, arm, "Eyes_LOD1")
            elif n == "Head":
                if li == 1:
                    lo = head_lod1(head, arm)
                else:
                    lo = lod_of(head, 0.40 if li == 2 else 0.13, f"{n}_LOD{li}", parent=arm)
            else:
                floor = B.LOD_MIN.get(n, [0, 0, 0])[li - 1]
                tgt = max(int(base_t * r), min(floor, base_t))
                seam = not (n in B.NO_SEAM_PARTS or n == "Jersey" or (li >= 2 and n in ("Arms", "Cleats", "Undersock")))
                lo = lod_of(o, tgt / float(base_t), f"{n}_LOD{li}", extra_protect=material_border_vertices if (n in ("Arms", "Pads") and li < 2) else None, parent=arm, seam=seam)
            if n == "Jersey":
                import phr_common
                phr_common.unwrap_jersey(lo, arm, layout=dict(phr_common.JERSEY_LAYOUT))
            if n in B.OVERLAY_INFLATE:
                inflate(lo, B.OVERLAY_INFLATE[n][li])
            sets[n].append(lo)
    for n, lst in sets.items():
        if n.startswith("Head_") and n != "Head_cage":
            continue
        lst[0].name = f"{n}_LOD0"
        lst[0].data.name = f"{n}_LOD0"
        for i, o in enumerate(lst):
            o.data.name = f"{n}_LOD{i}"
    for sn in H.SHELLS:
        sh = H.make_shell(head, sn, *H.SHELLS[sn])
        sh.name = f"Head_{sn}_LOD0"
        sh.data.name = sh.name
        sh.parent = arm
        sh.matrix_parent_inverse = Matrix.Identity(4)
        md = sh.modifiers.new("Armature", 'ARMATURE')
        md.object = arm
        set_material(sh, ["PHR_Hair" + sn])
        clear_normals_and_smooth(sh)
        sets["Head_" + sn] = [sh]
    visor = make_visor(arm)
    visor.name = "Visor_LOD0"
    visor.data.name = "Visor_LOD0"
    sets["Visor"] = [visor, lod_of(visor, 0.5, "Visor_LOD1", parent=None, seam=False)]
    sets["Visor"][1].name = "Visor_LOD1"
    bpy.data.objects.remove(head, do_unlink=True)
    # ---- base warp: every part / LOD + the rest skeleton
    if base != "Std":
        fld = W.Field(base)
        allobjs = [o for lst in sets.values() for o in lst]
        W.apply_field(fld, allobjs, arm)
        log("base warp", base, "applied to", len(allobjs), "meshes")
    # ---- jersey over the pads (every LOD)
    if pads:
        pad0 = sets["Pads"][0]
        PD.drape_jersey(sets["Jersey"], arm, pad0, PAD_SLEEVE_EXT)
        for po in sets["Pads"]:
            log("pads tucked under the jersey:", po.name, PD.tuck_pads(po, sets["Jersey"][0]), "of", len(po.data.vertices), "vertices")
    for n in B.RIGID + ["Visor"]:
        for o in sets[n]:
            rigid_to_bone(o, arm, "rig:Head")
    for lst in sets.values():
        for o in lst:
            if o.data.shape_keys:
                for kb in o.data.shape_keys.key_blocks:
                    kb.value = 0.0
    report(sets, body)
    if export:
        B.FILE_SUFFIX = "_" + base
        export_fbx(arm, sets, out_dir, body)
    return arm, sets


if __name__ == "__main__":
    a = args()
    body, base = a[0], a[1]
    out = STAGING
    export = True
    pads = True
    i = 2
    while i < len(a):
        if a[i] == "--out":
            out = a[i + 1]
            i += 2
        elif a[i] == "--no-export":
            export = False
            i += 1
        elif a[i] == "--no-pads":
            pads = False
            i += 1
        else:
            i += 1
    build2(body, base, out, export, pads)
