"""Build LOD0..LOD3 for every body mesh of a Draft Fever player FBX and export one FBX per player size.

  blender -b --python Tools/Blender/pix_player_lods.py -- [--body Medium[,Small,Large] | --fbx path.fbx] [--out Logs/blender_v6]
        [--ratios 0.5,0.25,0.1] [--sharp-angle 60] [--seam-weight 1.0] [--no-seam-protect] [--exclude REGEX] [--no-decimate REGEX] [--no-verify]

 * --body picks Assets/_Licensed/DraftFever/Characters/Bodies/<Body>Player.fbx (comma list allowed); --out is a folder (or a .fbx path when a
   single body is given). Output name: <Body>Player_LODs.fbx.
 * LOD0 is the untouched original mesh (renamed <Mesh>_LOD0). LOD1..3 = Decimate (collapse) at the given ratios of the ORIGINAL face count,
   then Triangulate, with shape keys baked to their current mix (Head / Eyes blend shapes exist only on LOD0), vertex groups (armature
   weights) carried over, custom split normals dropped and recomputed (smooth shading + sharp edges above --sharp-angle), UV layers kept.
   UV seams: every vertex that sits on a UV island border (or an open mesh border) is put in a vertex group with weight --seam-weight so the
   collapse decimator prefers to leave it alone; the UV error against LOD0 is measured and printed.
 * --no-decimate REGEX (default ^Eyes): those meshes are copied (ratio 1.0, only normals recomputed) into every LOD - decimating an eyeball wrecks the iris.
 * Seam protection is a hard constraint: meshes whose UV borders cover many vertices (Shoes, Hair) stop above the target ratio.
 * Transforms are applied before export: the Draft Fever import has armature scale 0.01 / rot 90 and meshes at -90 (cm data) - the armature
   bone data and the mesh vertices are baked to metres, Z-up, identity object transforms, character facing -Y (pix_common convention).
 * FBX export: -Z forward / Y up, global_scale 1, apply_unit_scale, FBX_SCALE_NONE, add_leaf_bones False, bake_anim False, mesh smoothing
   'EDGE' (sharp edges come from the angle rule), embed_textures False, one shared armature ('root') with all LOD meshes parented to it.
"""
import sys, os, re, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix_player_common import *
import bpy, bmesh
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree


def parse():
    a = script_args()
    o = dict(bodies=["Medium"], fbx=None, out=os.path.join(PROJECT_ROOT, "Logs", "blender_v6"), ratios=[0.5, 0.25, 0.1],
             sharp=60.0, seam_weight=1.0, seam=True, exclude=None, verify=True, keep_full="^Eyes")
    i = 0
    while i < len(a):
        k = a[i]
        if k == "--body": o["bodies"] = a[i + 1].split(","); i += 2
        elif k == "--fbx": o["fbx"] = a[i + 1]; i += 2
        elif k == "--out": o["out"] = a[i + 1]; i += 2
        elif k == "--ratios": o["ratios"] = [float(x) for x in a[i + 1].split(",")]; i += 2
        elif k == "--sharp-angle": o["sharp"] = float(a[i + 1]); i += 2
        elif k == "--seam-weight": o["seam_weight"] = float(a[i + 1]); i += 2
        elif k == "--no-seam-protect": o["seam"] = False; i += 1
        elif k == "--exclude": o["exclude"] = a[i + 1]; i += 2
        elif k == "--no-decimate": o["keep_full"] = a[i + 1]; i += 2
        elif k == "--no-verify": o["verify"] = False; i += 1
        else: raise SystemExit("unknown arg " + k)
    return o


def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


# ------------------------------------------------------------------ apply transforms (cm / 90 deg import -> metres, identity)
def apply_transforms(arm, meshes):
    mw = {m: m.matrix_world.copy() for m in meshes}
    for m in meshes:
        m.data.transform(mw[m], shape_keys=True)
        m.parent = None
        m.matrix_world = Matrix.Identity(4)
    M = arm.matrix_world.copy()
    arm.data.transform(M)
    arm.matrix_world = Matrix.Identity(4)
    for m in meshes:
        m.parent = arm
        m.parent_type = 'OBJECT'
        m.matrix_parent_inverse = Matrix.Identity(4)
        m.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()


# ------------------------------------------------------------------ seam / border vertices
def seam_vertices(me):
    """Vertices on a UV island border or an open mesh border (index set)."""
    uvl = me.uv_layers.active
    if uvl is None:
        return set()
    uv = np.empty(len(me.loops) * 2, np.float32); uvl.data.foreach_get('uv', uv); uv = uv.reshape(-1, 2)
    lv = np.empty(len(me.loops), np.int32); me.loops.foreach_get('vertex_index', lv)
    le = np.empty(len(me.loops), np.int32); me.loops.foreach_get('edge_index', le)
    ls = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('loop_start', ls)
    lt = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('loop_total', lt)
    owners = {}
    for p in range(len(me.polygons)):
        for li in range(ls[p], ls[p] + lt[p]):
            owners.setdefault(int(le[li]), []).append((p, li))
    out = set()
    for e, lst in owners.items():
        ev = me.edges[e].vertices
        if len(lst) != 2:
            out.update(ev); continue
        def uvs(p, li):
            d = {}
            for lj in range(ls[p], ls[p] + lt[p]):
                if le[lj] == e:
                    d[int(lv[lj])] = uv[lj]
                    nx = lj + 1 if lj + 1 < ls[p] + lt[p] else ls[p]
                    d[int(lv[nx])] = uv[nx]
            return d
        a, b = uvs(*lst[0]), uvs(*lst[1])
        if any(np.abs(a[v] - b[v]).max() > 1e-5 for v in ev if v in a and v in b):
            out.update(ev)
    return out


# ------------------------------------------------------------------ LOD generation
def bake_shape_key_mix(obj):
    me = obj.data
    if me.shape_keys is None:
        return
    mix = obj.shape_key_add(name="_mix", from_mix=True)
    co = np.empty(len(me.vertices) * 3, np.float32)
    mix.data.foreach_get('co', co)
    obj.shape_key_clear()
    me.vertices.foreach_set('co', co)
    me.update()


def make_lod(src, ratio, name, arm, opts):
    scene_coll = bpy.context.scene.collection
    tmp = src.copy(); tmp.data = src.data.copy(); scene_coll.objects.link(tmp)
    tmp.parent = None
    for m in list(tmp.modifiers):
        tmp.modifiers.remove(m)
    bake_shape_key_mix(tmp)
    if opts["seam"]:
        sv = seam_vertices(tmp.data)
        if sv:
            vg = tmp.vertex_groups.new(name="_pix_seam")
            vg.add(sorted(sv), opts["seam_weight"], 'REPLACE')
    dm = tmp.modifiers.new("Decimate", 'DECIMATE')
    dm.decimate_type = 'COLLAPSE'
    dm.ratio = ratio
    dm.use_collapse_triangulate = True
    if opts["seam"] and "_pix_seam" in tmp.vertex_groups:
        dm.vertex_group = "_pix_seam"
        dm.vertex_group_factor = 1.0
        dm.invert_vertex_group = True          # the group lists the vertices to PROTECT (Blender: group weight = amount of decimation)
    tm = tmp.modifiers.new("Triangulate", 'TRIANGULATE')
    tm.quad_method = 'BEAUTY'; tm.min_vertices = 4
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    me.name = name
    new = bpy.data.objects.new(name, me)
    scene_coll.objects.link(new)
    # vertex groups of the source (armature weights) in the same order; the helper seam group is not exported
    for vg in src.vertex_groups:
        new.vertex_groups.new(name=vg.name)
    bpy.data.objects.remove(tmp, do_unlink=True)
    # normals: drop custom split normals, smooth shading with sharp edges above the angle
    bpy.context.view_layer.objects.active = new
    for o in bpy.context.selected_objects:
        o.select_set(False)
    new.select_set(True)
    if me.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bm = bmesh.new(); bm.from_mesh(me)
    lim = math.radians(opts["sharp"])
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        e.smooth = not (e.is_manifold and e.calc_face_angle(0.0) > lim)
    bm.to_mesh(me); bm.free()
    new.parent = arm
    new.matrix_parent_inverse = Matrix.Identity(4)
    md = new.modifiers.new("Armature", 'ARMATURE'); md.object = arm
    return new


def groups_ok(obj):
    me = obj.data
    return sum(1 for v in me.vertices if len(v.groups) == 0), len(obj.vertex_groups)


def uv_error(orig, lod, size=4096):
    """Nearest-surface UV comparison LOD vs LOD0 (per face corner). Returns (p50, p95, max) in texels of a 4096 sheet and mean position error (mm)."""
    co, nr, uv, tl, tv, tp = world_arrays(orig)
    bvh = BVHTree.FromPolygons([tuple(v) for v in co], [tuple(t) for t in tv])
    lco, _, luv, ltl, ltv, ltp = world_arrays(lod)
    me = lod.data
    lv = np.empty(len(me.loops), np.int32); me.loops.foreach_get('vertex_index', lv)
    # triangle index in bvh == loop-triangle index in orig
    errs, dists = [], []
    for li in range(len(luv)):
        p = Vector(lco[lv[li]])
        loc, nrm, fi, d = bvh.find_nearest(p)
        if fi is None:
            continue
        # on a UV seam the nearest triangle may belong to the neighbouring island, so take the best match among all triangles within
        # 1.5x the nearest distance + 0.1 mm
        best = 1e9
        for loc2, n2, fj, d2 in bvh.find_nearest_range(p, d * 1.5 + 1e-4):
            a, b, c = co[tv[fj][0]], co[tv[fj][1]], co[tv[fj][2]]
            pa = np.array(loc2)
            v0, v1, v2 = b - a, c - a, pa - a
            d00, d01, d11, d20, d21 = v0 @ v0, v0 @ v1, v1 @ v1, v2 @ v0, v2 @ v1
            den = d00 * d11 - d01 * d01
            if abs(den) < 1e-18:
                continue
            v = (d11 * d20 - d01 * d21) / den; w = (d00 * d21 - d01 * d20) / den; u = 1 - v - w
            ouv = u * uv[tl[fj][0]] + v * uv[tl[fj][1]] + w * uv[tl[fj][2]]
            best = min(best, np.abs(ouv - luv[li]).max() * size)
        if best < 1e8:
            errs.append(best); dists.append(d)
    e = np.array(errs)
    return (float(np.percentile(e, 50)), float(np.percentile(e, 95)), float(e.max()), float(np.mean(dists) * 1000)) if len(e) else (0, 0, 0, 0)


def process(fbx, body, opts):
    objs = import_player(fbx)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    if opts["exclude"]:
        for o in [m for m in meshes if re.search(opts["exclude"], m.name)]:
            bpy.data.objects.remove(o, do_unlink=True)
        meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    apply_transforms(arm, meshes)
    ok = []
    rows = []
    for src in sorted(meshes, key=lambda o: o.name):
        base = src.name
        n0 = len(src.data.vertices); t0 = tri_count(src.data)
        lods = [src]
        for li, r in enumerate(opts["ratios"], start=1):
            full = bool(opts["keep_full"] and re.search(opts["keep_full"], base))
            lods.append(make_lod(src, 1.0 if full else r, "%s_LOD%d" % (base, li), arm, opts))
        # rename LOD0 last (the copies were built from it)
        src.name = base + "_LOD0"; src.data.name = base + "_LOD0"
        for li, o in enumerate(lods):
            nov, ng = groups_ok(o)
            ue = uv_error(src, o) if li > 0 and not re.search(opts["keep_full"] or "$^", base) else (0, 0, 0, 0)   # copies: nothing to measure
            rows.append((base, li, len(o.data.vertices), tri_count(o.data), nov, ue))
    # ---- table
    print("\n%s: LOD table (vertices / triangles; ratios of LOD0 triangles; unweighted verts; UV error vs LOD0 in 4096-texels p50/p95/max; mean pos err mm)" % body)
    print("%-34s %-5s %8s %8s %7s %6s  %s" % ("mesh", "LOD", "verts", "tris", "ratio", "unwgt", "uv p50/p95/max  pos_mm"))
    base_t = {}
    tot = {}
    for base, li, nv, nt, nov, ue in rows:
        if li == 0: base_t[base] = nt
        tot.setdefault(li, [0, 0]); tot[li][0] += nv; tot[li][1] += nt
        print("%-34s LOD%-2d %8d %8d %7.3f %6d  %.1f/%.1f/%.1f  %.2f" % (base, li, nv, nt, nt / max(base_t[base], 1), nov, ue[0], ue[1], ue[2], ue[3]))
    for li in sorted(tot):
        print("%-34s LOD%-2d %8d %8d %7.3f" % ("TOTAL", li, tot[li][0], tot[li][1], tot[li][1] / max(tot[0][1], 1)))
    # ---- export
    out = opts["out_file"]
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in bpy.data.objects:
        if o.type in ('MESH', 'ARMATURE'):
            o.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=out, use_selection=True, global_scale=1.0, apply_unit_scale=True,
                             apply_scale_options='FBX_SCALE_NONE', axis_forward='-Z', axis_up='Y',
                             object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='EDGE',
                             add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False,
                             use_armature_deform_only=False, use_tspace=False)
    print("exported", out, "(%.1f MB)" % (os.path.getsize(out) / 1e6))
    return rows, out


def verify(path):
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=path)
    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    ms = sorted((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: o.name)
    print("re-import check: %d armature(s), %d meshes; armature scale %s" % (len(arms), len(ms), tuple(round(x, 3) for x in arms[0].matrix_world.to_scale())))
    names = [o.name for o in ms]
    for lv in range(4):
        print("  LOD%d meshes: %d" % (lv, sum(1 for n in names if n.endswith("_LOD%d" % lv))))
    zs = []
    for o in ms:
        if o.name.startswith("Legs_GEO_LOD0") or o.name.startswith("Head_") and o.name.endswith("_LOD0"):
            zs += [(o.matrix_world @ Vector(c)).z for c in o.bound_box]
    if zs:
        print("  re-import height span of Legs/Head LOD0: %.3f .. %.3f world units (1 unit = 1 m expected)" % (min(zs), max(zs)))


def main():
    o = parse()
    outs = []
    for body in o["bodies"]:
        fbx = o["fbx"] or os.path.join(BODY_DIR, "%sPlayer.fbx" % body)
        out = o["out"]
        if out.lower().endswith(".fbx") and len(o["bodies"]) == 1:
            o["out_file"] = out
        else:
            o["out_file"] = os.path.join(out, "%sPlayer_LODs.fbx" % body)
        process(fbx, body, o)
        outs.append(o["out_file"])
    if o["verify"]:
        for p in outs:
            verify(p)


if __name__ == "__main__":
    main()
