"""PHR: builds one photoreal player body FBX (LOD0..LOD3 for every part, one shared Mixamo-style 'rig:' skeleton, Draft Fever head with blend shapes).

  blender -b --python Tools/Blender/phr_build.py -- <Small|Medium|Large> [--out <dir>] [--no-export] [--lods-only-report]

Pipeline (see Docs/QA/photoreal.md):
  1. import R1 (A-P.glb), bake transforms, centre, delete R1's head / eyes / eyelashes (the real player's face never enters the build)
  2. fit the Draft Fever head (generic sculpt, 28 shapes) into the helmet, weights on Neck / Head, neck thickness per body type
  3. reshape the cloth / skin / accessory meshes for the body type (weight-blended radial scaling, skeleton untouched), shoulder pads
  4. clean normals, one UV set, one material per part, arms+hands in two material slots
  5. LODs by collapse decimation with UV-seam and material-border protection (silhouette checked), static helmet parts bone-parented to the head
  6. FBX export (-Z forward / Y up, 1 unit = 1 m, rig 'rig:*' unchanged so every body type shares the same Humanoid layout)
"""
import sys, os, math, re
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *
import phr_body
import bmesh
import pix_player_lods as pl
import phr_hair as H

HEAD_SHAPES = ["Head_01_SubD01", "Head_02_SubD01", "Head_03_SubD01", "Head_05_SubD01", "Head_07_SubD01", "Head_09_SubD01",
               "Head_11_SubD01", "Head_13_SubD01", "Head_22_SubD01", "Head_26_SubD01", "Head_29_SubD01", "Head_33_SubD01"]
EYE_SHAPE_OF = lambda hn: hn.replace("Head_", "Head_Eyes_").replace("_SubD01", "")

# R1 mesh -> final part name
PARTS = {
    "jersey": "Jersey", "pants": "Pants", "socks": "Socks", "shorts": "Undersock", "arms": "Arms", "cleats": "Cleats",
    "armsleeve": "SleeveR", "innershirt": "SleeveL", "twistband": "Wristband", "wristcoach": "PlayCard", "handwarmer": "Pouch", "towel": "Towel",
    "helmet": "Helmet_Shell", "facemask": "FaceMask", "chinstrap": "Chinstrap", "Padding": "HelmetPad",
}
RIGID = ["Helmet_Shell", "FaceMask", "Chinstrap", "HelmetPad"]          # bone-parented to rig:Head, static meshes
# LOD0 triangle targets (the R1 meshes are heavier than the budget); LOD1..3 are ratios of the LOD0 count
LOD0_TRIS = {"Arms": 9500, "Cleats": 5500, "Helmet_Shell": 9000, "FaceMask": 6500, "Chinstrap": 2500, "HelmetPad": 1600, "SleeveR": 3000,
             "SleeveL": 1500, "Pouch": 1800}
LOD_RATIOS = [0.5, 0.22, 0.09]
HEAD_LOD0_TRIS = 13000
NO_SEAM_PARTS = {"Helmet_Shell", "FaceMask", "Chinstrap", "HelmetPad", "Pouch", "Towel", "Wristband", "PlayCard"}   # uniform / solid materials: UV seams do not matter
LOD_MIN = {"Pants": [1200, 600, 300], "Arms": [3000, 1400, 700], "Cleats": [1800, 800, 450], "Helmet_Shell": [3000, 1300, 600],
           "FaceMask": [2200, 900, 350], "Socks": [500, 280, 220], "Undersock": [320, 200, 160], "Jersey": [1800, 900, 600], "SleeveR": [1500, 800, 450], "SleeveL": [750, 420, 260]}


SCALE_OPT = 'FBX_SCALE_ALL'
FILE_SUFFIX = ''


def log(*a):
    print("PHR", *a)


# ------------------------------------------------------------------------------------------------ helpers
def tri_count(me):
    return sum(len(p.vertices) - 2 for p in me.polygons)


def clear_normals_and_smooth(obj, angle_deg=60.0):
    me = obj.data
    bpy.context.view_layer.objects.active = obj
    for o in bpy.context.selected_objects:
        o.select_set(False)
    obj.select_set(True)
    if me.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bm = bmesh.new()
    bm.from_mesh(me)
    lim = math.radians(angle_deg)
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        e.smooth = not (e.is_manifold and e.calc_face_angle(0.0) > lim)
    bm.to_mesh(me)
    bm.free()


def keep_first_uv(obj):
    me = obj.data
    while len(me.uv_layers) > 1:
        me.uv_layers.remove(me.uv_layers[-1])
    if me.uv_layers:
        me.uv_layers[0].name = "UVMap"


def set_material(obj, names):
    obj.data.materials.clear()
    for n in names:
        m = bpy.data.materials.get(n) or bpy.data.materials.new(n)
        obj.data.materials.append(m)


def decimate_to(obj, target_tris, extra_protect=None, seam=True):
    """In-place collapse decimation to ~target triangles (keeps weights / uv / material indices)."""
    me = obj.data
    t0 = tri_count(me)
    if target_tris >= t0:
        return
    ratio = target_tris / float(t0)
    new = lod_of(obj, ratio, obj.name + "_tmp", extra_protect=extra_protect, seam=seam, parent=obj.parent)
    # swap the data
    old = obj.data
    obj.data = new.data
    bpy.data.objects.remove(new, do_unlink=True)
    bpy.data.meshes.remove(old)


def lod_of(src, ratio, name, extra_protect=None, seam=True, parent=None, rigid_bone=None):
    """Decimated copy (collapse, triangulated, UV seam + material border protected). Returns the new object."""
    scene_coll = bpy.context.scene.collection
    tmp = src.copy()
    tmp.data = src.data.copy()
    scene_coll.objects.link(tmp)
    tmp.parent = None
    had_arm = [m.object for m in tmp.modifiers if m.type == 'ARMATURE']
    for m in list(tmp.modifiers):
        tmp.modifiers.remove(m)
    pl.bake_shape_key_mix(tmp)
    prot = set()
    if seam:
        prot |= pl.seam_vertices(tmp.data)
    if extra_protect:
        prot |= set(extra_protect(tmp))
    if prot:
        vg = tmp.vertex_groups.new(name="_pix_seam")
        vg.add(sorted(prot), 1.0, 'REPLACE')
    dm = tmp.modifiers.new("Decimate", 'DECIMATE')
    dm.decimate_type = 'COLLAPSE'
    dm.ratio = max(0.01, min(1.0, ratio))
    dm.use_collapse_triangulate = True
    if prot:
        dm.vertex_group = "_pix_seam"
        dm.vertex_group_factor = 1.0
        dm.invert_vertex_group = True
    tm = tmp.modifiers.new("Triangulate", 'TRIANGULATE')
    tm.quad_method = 'BEAUTY'
    tm.min_vertices = 4
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    me.name = name
    new = bpy.data.objects.new(name, me)
    scene_coll.objects.link(new)
    for vg in src.vertex_groups:
        if vg.name != "_pix_seam":
            new.vertex_groups.new(name=vg.name)
    bpy.data.objects.remove(tmp, do_unlink=True)
    clear_normals_and_smooth(new)
    if parent is not None:
        new.parent = parent
        new.matrix_parent_inverse = Matrix.Identity(4)
        if had_arm:
            md2 = new.modifiers.new("Armature", 'ARMATURE')
            md2.object = had_arm[0]
    return new


OVERLAY_INFLATE = {   # shells that sit over another mesh: decimation shrinks them, so push them out a little (metres per LOD) to keep the layers from intersecting
    "Jersey": [0.0, 0.0006, 0.0015, 0.0028], "Pants": [0.0, 0.0005, 0.0012, 0.0022], "Socks": [0.0, 0.0004, 0.0010, 0.0016], "Undersock": [0.0, 0.0004, 0.0010, 0.0016],
    "SleeveR": [0.0, 0.0006, 0.0015, 0.0028], "SleeveL": [0.0, 0.0006, 0.0015, 0.0028], "Wristband": [0.0, 0.0004, 0.0010, 0.0016], "PlayCard": [0.0, 0.0004, 0.0010, 0.0016],
    "Chinstrap": [0.0, 0.0003, 0.0008, 0.0014], "HelmetPad": [0.0, 0.0003, 0.0008, 0.0014],
}


def inflate(obj, d):
    if d <= 0:
        return
    me = obj.data
    me.update()
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    nr = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('normal', nr)
    co = co.reshape(-1, 3) + nr.reshape(-1, 3) * d
    me.vertices.foreach_set('co', co.astype(np.float32).ravel())
    me.update()


def material_border_vertices(obj):
    me = obj.data
    if len(me.materials) < 2:
        return set()
    mi = np.empty(len(me.polygons), np.int32)
    me.polygons.foreach_get('material_index', mi)
    vm = {}
    for p in me.polygons:
        for v in p.vertices:
            vm.setdefault(v, set()).add(int(mi[p.index]))
    return {v for v, s in vm.items() if len(s) > 1}


# ------------------------------------------------------------------------------------------------ head
def fit_head(arm, body):
    head, eyes = import_df_head()
    r1_eye_c = Vector((-0.006, -0.061, 1.751))          # R1 eye centre (after centring); the helmet / face mask were authored around it
    emn, emx = bbox([eyes])
    dfc = (emn + emx) / 2
    M = Matrix.Translation(r1_eye_c) @ Matrix.Scale(0.969, 4) @ Matrix.Translation(-dfc)
    for o in (head, eyes):
        o.data.transform(M, shape_keys=True)
    # keep only the Basis + the selected shapes (renamed S01..)
    for o, names in ((head, HEAD_SHAPES), (eyes, [EYE_SHAPE_OF(n) for n in HEAD_SHAPES])):
        sk = o.data.shape_keys
        keep = ["Basis"] + names
        for kb in list(sk.key_blocks):
            if kb.name not in keep:
                o.shape_key_remove(kb)
        for i, n in enumerate(names):
            sk.key_blocks[n].name = "S%02d" % (i + 1)
        for kb in sk.key_blocks:
            kb.value = 0.0
    add_authored_shapes(head)
    return head, eyes


def _sm(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


AUTHORED = ["T_JawWide", "T_ChinLong", "T_NoseBig", "T_CheekFull", "T_BrowHeavy"]


def add_authored_shapes(head):
    """Mild face-proportion blend shapes authored on the cage (jaw width, chin length, nose size, cheek fullness, brow ridge); weights -1..1 at runtime."""
    sk = head.data.shape_keys
    basis = np.array([v.co[:] for v in sk.key_blocks["Basis"].data])
    nrm = np.empty(len(head.data.vertices) * 3, np.float32)
    head.data.vertices.foreach_get('normal', nrm)
    nrm = nrm.reshape(-1, 3)
    cx = -0.006
    x = basis[:, 0] - cx
    ax = np.abs(x)
    y = basis[:, 1]
    z = basis[:, 2]
    sgn = np.where(x >= 0, 1.0, -1.0)
    d = {}
    # jaw width: the lower face outside the mouth corners moves out / in
    w = _sm(1.725, 1.67, z) * _sm(1.575, 1.625, z) * _sm(0.03, 0.07, ax) * _sm(0.045, 0.0, y)
    dd = np.zeros_like(basis)
    dd[:, 0] = sgn * 0.017 * w
    d["T_JawWide"] = dd
    # chin: long / short, slightly forward
    g = np.exp(-(((x) / 0.035) ** 2 + ((z - 1.662) / 0.030) ** 2 + ((y + 0.085) / 0.05) ** 2))
    dd = np.zeros_like(basis)
    dd[:, 2] = -0.022 * g
    dd[:, 1] = -0.006 * g
    d["T_ChinLong"] = dd
    # nose: wider and more prominent
    g = np.exp(-(((x) / 0.026) ** 2 + ((z - 1.712) / 0.026) ** 2 + ((y + 0.100) / 0.030) ** 2))
    dd = np.zeros_like(basis)
    dd[:, 0] = x * 0.30 * g
    dd[:, 1] = -0.012 * g
    d["T_NoseBig"] = dd
    # cheeks: fuller (outward along the normal)
    dd = np.zeros_like(basis)
    for sx in (-1, 1):
        g = np.exp(-((((x) - sx * 0.050) / 0.034) ** 2 + ((z - 1.700) / 0.030) ** 2 + ((y + 0.075) / 0.035) ** 2))
        dd += nrm * (0.011 * g)[:, None]
    d["T_CheekFull"] = dd
    # brow ridge: heavier / lighter
    g = np.exp(-(((z - 1.783) / 0.016) ** 2)) * _sm(0.075, 0.03, ax) * _sm(-0.045, -0.07, y)
    dd = np.zeros_like(basis)
    dd[:, 1] = -0.010 * g
    dd[:, 2] = 0.003 * g
    d["T_BrowHeavy"] = dd
    for nm in AUTHORED:
        kb = head.shape_key_add(name=nm)
        kb.value = 0.0
        kb.data.foreach_set('co', (basis + d[nm]).astype(np.float32).ravel())
    for kb in sk.key_blocks:
        kb.value = 0.0


def neck_thicken(head, body):
    """Radial thickening of the neck below the chin (all shape keys)."""
    f = phr_body.BODY[body]["neck"]
    if abs(f - 1.0) < 1e-3:
        return
    axis_x, axis_y = 0.0, 0.068
    for kb in head.data.shape_keys.key_blocks:
        co = np.empty(len(kb.data) * 3, np.float32)
        kb.data.foreach_get('co', co)
        co = co.reshape(-1, 3)
        w = 1.0 - phr_body_smooth(1.60, 1.68, co[:, 2])
        s = 1.0 + (f - 1.0) * w
        co[:, 0] = axis_x + (co[:, 0] - axis_x) * s
        co[:, 1] = axis_y + (co[:, 1] - axis_y) * s
        kb.data.foreach_set('co', co.ravel())
    head.data.vertices.foreach_set('co', np.array([v.co for v in head.data.shape_keys.key_blocks[0].data], np.float32).ravel())


def phr_body_smooth(a, b, x):
    t = np.clip((x - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def weight_head(head, eyes, arm):
    names = {"head": "rig:Head", "neck": "rig:Neck", "spine": "rig:Spine2"}
    for o in (head, eyes):
        for n in names.values():
            if n not in o.vertex_groups:
                o.vertex_groups.new(name=n)
    co = np.empty(len(head.data.vertices) * 3, np.float32)
    head.data.vertices.foreach_get('co', co)
    z = co.reshape(-1, 3)[:, 2]
    wh = phr_body_smooth(1.600, 1.665, z)
    ws = 1.0 - phr_body_smooth(1.500, 1.565, z)
    wn = np.clip(1.0 - wh - ws, 0, 1)
    tot = wh + wn + ws
    wh, wn, ws = wh / tot, wn / tot, ws / tot
    gh, gn, gs = head.vertex_groups[names["head"]], head.vertex_groups[names["neck"]], head.vertex_groups[names["spine"]]
    for i in range(len(z)):
        if wh[i] > 0.001: gh.add([i], float(wh[i]), 'REPLACE')
        if wn[i] > 0.001: gn.add([i], float(wn[i]), 'REPLACE')
        if ws[i] > 0.001: gs.add([i], float(ws[i]), 'REPLACE')
    ge = eyes.vertex_groups[names["head"]]
    ge.add(list(range(len(eyes.data.vertices))), 1.0, 'REPLACE')
    for o in (head, eyes):
        o.parent = arm
        o.matrix_parent_inverse = Matrix.Identity(4)
        md = o.modifiers.new("Armature", 'ARMATURE')
        md.object = arm


def subdivide_with_shapes(head):
    """Catmull-Clark level 1 of the head cage, evaluated once per shape key so every blend shape keeps its index-for-index counterpart.
    Returns a new object (same name + '_sub') with the same shape-key names."""
    sk = head.data.shape_keys
    names = [kb.name for kb in sk.key_blocks]
    # evaluate each key with only that key at 1
    results = {}
    for nm in names:
        for kb in sk.key_blocks:
            kb.value = 1.0 if (kb.name == nm and nm != "Basis") else 0.0
        bpy.context.view_layer.update()
        tmp = head.copy()
        tmp.data = head.data.copy()
        bpy.context.scene.collection.objects.link(tmp)
        # bake the single key as a plain mesh
        pl.bake_shape_key_mix(tmp)
        for m in list(tmp.modifiers):
            tmp.modifiers.remove(m)
        for vg in list(tmp.vertex_groups):
            pass
        sm = tmp.modifiers.new("sub", 'SUBSURF')
        sm.levels = 1
        sm.render_levels = 1
        sm.subdivision_type = 'CATMULL_CLARK'
        sm.boundary_smooth = 'ALL'
        sm.uv_smooth = 'PRESERVE_BOUNDARIES'
        dg = bpy.context.evaluated_depsgraph_get()
        ev = tmp.evaluated_get(dg)
        me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
        results[nm] = me
        bpy.data.objects.remove(tmp, do_unlink=True)
    for kb in sk.key_blocks:
        kb.value = 0.0
    basis = results["Basis"]
    obj = bpy.data.objects.new(head.name + "_sub", basis)
    bpy.context.scene.collection.objects.link(obj)
    for vg in head.vertex_groups:
        obj.vertex_groups.new(name=vg.name)
    obj.shape_key_add(name="Basis")
    for nm in names[1:]:
        kb = obj.shape_key_add(name=nm)
        kb.value = 0.0
        co = np.empty(len(results[nm].vertices) * 3, np.float32)
        results[nm].vertices.foreach_get('co', co)
        kb.data.foreach_set('co', co)
        bpy.data.meshes.remove(results[nm])
    return obj


def clean_decimated_copy(src, tris, name):
    """Plain (no shape keys, no modifiers) triangulated copy of src's basis mesh decimated to ~tris. Avoids the Blender shape-key bake path."""
    bm = bmesh.new()
    bm.from_mesh(src.data)
    for k in list(bm.verts.layers.shape.keys()):
        bm.verts.layers.shape.remove(bm.verts.layers.shape[k])
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    n = bpy.data.meshes.new(name + "_plain")
    bm.to_mesh(n)
    bm.free()
    o = bpy.data.objects.new(name + "_plain", n)
    bpy.context.scene.collection.objects.link(o)
    dm = o.modifiers.new("d", 'DECIMATE')
    dm.decimate_type = 'COLLAPSE'
    dm.ratio = max(0.02, min(1.0, tris / float(len(n.polygons))))
    dm.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    ev = bpy.data.meshes.new_from_object(o.evaluated_get(dg), depsgraph=dg)
    ev.name = name
    bpy.data.objects.remove(o, do_unlink=True)
    bpy.data.meshes.remove(n)
    res = bpy.data.objects.new(name, ev)
    bpy.context.scene.collection.objects.link(res)
    return res


def head_lod0_from_sub(sub, tris, arm, cage):
    """Decimated subdivided head (neutral) + the blend shapes of `sub` transferred by closest-point barycentric mapping,
    so LOD0 (~13k tris) keeps the exact set of shape keys of the cage and the sculpted detail of the subdivision."""
    from mathutils.bvhtree import BVHTree
    from mathutils.interpolate import poly_3d_calc
    me = sub.data
    me.calc_loop_triangles()
    verts = [v.co.copy() for v in me.vertices]
    tv = [tuple(t.vertices) for t in me.loop_triangles]
    bvh = BVHTree.FromPolygons(verts, tv)
    lo = clean_decimated_copy(sub, tris, "Head_LOD0")
    copy_weights_by_proximity(cage, lo)
    lo.parent = arm
    lo.matrix_parent_inverse = Matrix.Identity(4)
    md = lo.modifiers.new("Armature", 'ARMATURE')
    md.object = arm
    names = [kb.name for kb in sub.data.shape_keys.key_blocks]
    K = {}
    for nm in names:
        a = np.empty(len(me.vertices) * 3, np.float32)
        sub.data.shape_keys.key_blocks[nm].data.foreach_get('co', a)
        K[nm] = a.reshape(-1, 3)
    n = len(lo.data.vertices)
    idx = np.zeros((n, 3), np.int32)
    wts = np.zeros((n, 3), np.float64)
    for i, v in enumerate(lo.data.vertices):
        loc, nor, fi, d = bvh.find_nearest(v.co)
        tri = tv[fi]
        w = poly_3d_calc([verts[tri[0]], verts[tri[1]], verts[tri[2]]], loc)
        idx[i] = tri
        wts[i] = w
    def mapped(arr):
        return (arr[idx] * wts[..., None]).sum(axis=1)
    base_map = mapped(K["Basis"])
    cur = np.empty(n * 3, np.float32)
    lo.data.vertices.foreach_get('co', cur)
    cur = cur.reshape(-1, 3)
    lo.shape_key_add(name="Basis")
    for nm in names[1:]:
        kb = lo.shape_key_add(name=nm)
        kb.value = 0.0
        kb.data.foreach_set('co', (cur + (mapped(K[nm]) - base_map)).astype(np.float32).ravel())
    return lo


def copy_weights_by_proximity(src, dst):
    """Skin weights for dst from the nearest src vertex (the subdivided head from the cage)."""
    from mathutils.kdtree import KDTree
    kd = KDTree(len(src.data.vertices))
    for v in src.data.vertices:
        kd.insert(v.co, v.index)
    kd.balance()
    gnames = {g.index: g.name for g in src.vertex_groups}
    for g in src.vertex_groups:
        if g.name not in dst.vertex_groups:
            dst.vertex_groups.new(name=g.name)
    for v in dst.data.vertices:
        co, idx, d = kd.find(v.co)
        for ge in src.data.vertices[idx].groups:
            dst.vertex_groups[gnames[ge.group]].add([v.index], ge.weight, 'REPLACE')


# ------------------------------------------------------------------------------------------------ optional T-pose rest
def apply_tpose(arm, skinned):
    """Re-poses the rig to a T-pose (upper arms horizontal) and bakes it: every skinned mesh gets the posed vertices (those with shape keys are unaffected by the arms and skipped),
    the armature rest pose becomes the T-pose. Used when the Humanoid avatar needs a T-pose reference (--tpose)."""
    bpy.context.view_layer.objects.active = arm
    for o in bpy.context.selected_objects:
        o.select_set(False)
    arm.select_set(True)
    bpy.ops.object.mode_set(mode='POSE')
    for side, sx in (("Left", 1.0), ("Right", -1.0)):
        pb = arm.pose.bones["rig:%sArm" % side]
        fb = arm.data.bones["rig:%sForeArm" % side]
        hb = arm.data.bones["rig:%sArm" % side]
        d = (fb.head_local - hb.head_local).normalized()
        target = Vector((sx, 0.0, 0.0))
        q = d.rotation_difference(target)
        p = arm.matrix_world @ pb.head
        M = Matrix.Translation(p) @ q.to_matrix().to_4x4() @ Matrix.Translation(-p)
        pb.matrix = M @ pb.matrix
        bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    for o in skinned:
        if o.data.shape_keys:
            continue
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get('co', co)
        ev.to_mesh_clear()
        o.data.vertices.foreach_set('co', co)
        o.data.update()
    bpy.ops.pose.armature_apply(selected=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()


# ------------------------------------------------------------------------------------------------ main build
def build(body, out_dir, export=True, tpose=False):
    reset()
    scene = bpy.context.scene
    arm, meshes = import_r1()
    for n in ("head", "eyes", "eyelashes"):
        bpy.data.objects.remove(meshes.pop(n), do_unlink=True)
    objs = {}
    for k, o in list(meshes.items()):
        o.name = PARTS[k]
        o.data.name = PARTS[k]
        objs[PARTS[k]] = o
        keep_first_uv(o)
    skinned = {n: o for n, o in objs.items() if n not in RIGID}
    phr_body.reshape(arm, skinned, body)
    if tpose:
        apply_tpose(arm, list(skinned.values()))
    # head
    head, eyes = fit_head(arm, body)
    neck_thicken(head, body)
    head.name, eyes.name = "Head", "Eyes"
    head.data.name, eyes.data.name = "Head", "Eyes"
    # subdivided LOD0 head (weights from the cage)
    weight_head(head, eyes, arm)
    sub0 = subdivide_with_shapes(head)
    sub = head_lod0_from_sub(sub0, HEAD_LOD0_TRIS, arm, head)
    bpy.data.objects.remove(sub0, do_unlink=True)
    # materials: one per part
    for n, o in objs.items():
        if n == "Arms":
            set_material(o, ["PHR_Arms", "PHR_Hands"])
        else:
            set_material(o, ["PHR_" + n])
    set_material(head, ["PHR_Head"])
    set_material(sub, ["PHR_Head"])
    set_material(eyes, ["PHR_Eyes"])
    # arms / hands material split by hand-bone weights
    split_hands(objs["Arms"])
    for o in list(objs.values()) + [head, sub, eyes]:
        clear_normals_and_smooth(o)
    # LOD0 decimation of the heavy R1 meshes
    for n, t in LOD0_TRIS.items():
        if n in objs:
            before = tri_count(objs[n].data)
            decimate_to(objs[n], t, extra_protect=material_border_vertices if n == "Arms" else None, seam=n not in NO_SEAM_PARTS)
            log("LOD0", n, before, "->", tri_count(objs[n].data))
    # parts table (LOD0 objects)
    lod0 = dict(objs)
    lod0["Head"] = sub
    lod0["Eyes"] = eyes
    head.name = "Head_cage"
    # LOD sets
    sets = {}
    for n, o in lod0.items():
        sets[n] = [o]
    for n, o in lod0.items():
        base_t = tri_count(o.data)
        for li, r in enumerate(LOD_RATIOS, start=1):
            if n == "Eyes":
                if li > 1:
                    continue
                lo = head_lod1(o, arm, "Eyes_LOD1")          # same shape keys as the head (an eyeball must follow the eye socket)
            elif n == "Head":
                if li == 1:
                    lo = head_lod1(head, arm)
                else:
                    lo = lod_of(head, 0.40 if li == 2 else 0.13, f"{n}_LOD{li}", parent=arm)      # decimated cage, neutral shape (no shape keys)
            else:
                floor = LOD_MIN.get(n, [0, 0, 0])[li - 1]
                tgt = max(int(base_t * r), min(floor, base_t))
                seam = not (n in NO_SEAM_PARTS or n == "Jersey" or (li >= 2 and n in ("Arms", "Cleats", "Undersock")))
                lo = lod_of(o, tgt / float(base_t), f"{n}_LOD{li}", extra_protect=material_border_vertices if (n == "Arms" and li < 2) else None, parent=arm, seam=seam)
            if n == "Jersey":
                import phr_common
                phr_common.unwrap_jersey(lo, arm, layout=dict(phr_common.JERSEY_LAYOUT))   # planar uv is position based: re-project the decimated mesh with LOD0's packing and classes
            if n in OVERLAY_INFLATE:
                inflate(lo, OVERLAY_INFLATE[n][li])
            sets[n].append(lo)
    # rename LOD0, rigid parenting
    for n, lst in sets.items():
        if n.startswith("Head_") and n != "Head_cage":
            continue
        lst[0].name = f"{n}_LOD0"
        lst[0].data.name = f"{n}_LOD0"
        for i, o in enumerate(lst):
            o.data.name = f"{n}_LOD{i}"
    # facial hair / eyebrow / scalp shells (subsets of the cage, same blend shapes)
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
    for n in RIGID + ["Visor"]:
        for o in sets[n]:
            rigid_to_bone(o, arm, "rig:Head")
    # every blend shape starts at weight 0 (Blender creates python-added keys with value 1.0)
    for lst in sets.values():
        for o in lst:
            if o.data.shape_keys:
                for kb in o.data.shape_keys.key_blocks:
                    kb.value = 0.0
    report(sets, body)
    if export:
        export_fbx(arm, sets, out_dir, body)
    return arm, sets


def make_visor(arm):
    """Curved visor shield around the face (inside the face mask, under the shell brim): ~300 tris, double sided shell of 1.5 mm, own UV 0..1."""
    cx, yc = -0.006, 0.012
    nth, nz = 28, 7
    thetas = np.radians(np.linspace(-62, 62, nth))
    zs = np.linspace(1.712, 1.805, nz)
    verts = []
    for iz, z in enumerate(zs):
        t = iz / (nz - 1)
        R = 0.124 - 0.012 * (1 - np.sin(np.pi * t) ** 0.5)                # bows out in the middle, closes at the rim
        for th in thetas:
            r = R + 0.004 * np.cos(th * 2)
            verts.append((cx + r * 0.80 * np.sin(th), yc - r * np.cos(th), z))
    faces = []
    for iz in range(nz - 1):
        for it in range(nth - 1):
            a = iz * nth + it
            faces.append((a, a + 1, a + 1 + nth, a + nth))
    me = bpy.data.meshes.new("Visor")
    me.from_pydata(verts, [], faces)
    me.update()
    uvl = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        for li in poly.loop_indices:
            v = me.loops[li].vertex_index
            iz, it = divmod(v, nth)
            uvl.data[li].uv = (it / (nth - 1), iz / (nz - 1))
    o = bpy.data.objects.new("Visor", me)
    bpy.context.scene.collection.objects.link(o)
    # thickness
    sm = o.modifiers.new("solid", 'SOLIDIFY')
    sm.thickness = 0.0015
    sm.offset = 0.0
    dg = bpy.context.evaluated_depsgraph_get()
    ev = bpy.data.meshes.new_from_object(o.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    o.modifiers.remove(sm)
    o.data = ev
    bpy.data.meshes.remove(me)
    ev.name = "Visor"
    set_material(o, ["PHR_Visor"])
    clear_normals_and_smooth(o)
    return o


def head_lod1(cage, arm, name="Head_LOD1"):
    """LOD1 head = the cage itself with all blend shapes (2.8k verts)."""
    o = cage.copy()
    o.data = cage.data.copy()
    bpy.context.scene.collection.objects.link(o)
    o.name = name
    o.data.name = name
    # armature modifier already copied with the object
    for m in o.modifiers:
        if m.type == 'ARMATURE':
            m.object = arm
    o.parent = arm
    return o


def split_hands(arms):
    """Arms mesh: faces whose vertices mostly belong to the hand bones use material slot 1 (PHR_Hands), the rest slot 0."""
    me = arms.data
    hand_groups = [g.index for g in arms.vertex_groups if "Hand" in g.name and g.name not in ("rig:LeftHand", "rig:RightHand")] + \
                  [g.index for g in arms.vertex_groups if g.name in ("rig:LeftHand", "rig:RightHand")]
    hs = set(hand_groups)
    vh = np.zeros(len(me.vertices))
    for v in me.vertices:
        vh[v.index] = sum(g.weight for g in v.groups if g.group in hs)
    mi = np.zeros(len(me.polygons), np.int32)
    for p in me.polygons:
        mi[p.index] = 1 if np.mean([vh[v] for v in p.vertices]) > 0.5 else 0
    me.polygons.foreach_set('material_index', mi)
    log("hand faces", int(mi.sum()), "of", len(mi))


def rigid_to_bone(o, arm, bone):
    mw = o.matrix_world.copy()
    for m in list(o.modifiers):
        if m.type == 'ARMATURE':
            o.modifiers.remove(m)
    o.vertex_groups.clear()
    o.parent = arm
    o.parent_type = 'BONE'
    o.parent_bone = bone
    o.matrix_world = mw


def report(sets, body):
    rows = {}
    log("=" * 60)
    log("%-14s %9s %9s %9s %9s" % ("part", "LOD0", "LOD1", "LOD2", "LOD3"))
    tot = [0, 0, 0, 0]
    for n, lst in sets.items():
        t = [tri_count(o.data) for o in lst]
        for i in range(len(t)):
            tot[i] += t[i]
        t += [0] * (4 - len(t))
        log("%-14s %9d %9d %9d %9d" % (n, *t))
    log("%-14s %9d %9d %9d %9d" % ("TOTAL", *tot))
    return tot


def export_fbx(arm, sets, out_dir, body):
    ensure(out_dir)
    path = os.path.join(out_dir, f"PHR_{body}{FILE_SUFFIX}.fbx")
    bpy.ops.object.select_all(action='DESELECT')
    for lst in sets.values():
        for o in lst:
            o.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, global_scale=1.0, apply_unit_scale=True,
                             apply_scale_options=SCALE_OPT, axis_forward='-Z', axis_up='Y',
                             object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='EDGE',
                             add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False,
                             use_armature_deform_only=False, use_tspace=True, bake_space_transform=False)
    log("exported", path, "%.1f MB" % (os.path.getsize(path) / 1e6))


if __name__ == "__main__":
    a = args()
    body = a[0]
    out = os.path.join(OUT_ASSET, "Meshes")
    export = True
    tpose = False
    i = 1
    while i < len(a):
        if a[i] == "--out":
            out = a[i + 1]
            i += 2
        elif a[i] == "--no-export":
            export = False
            i += 1
        elif a[i] == "--tpose":
            tpose = True
            i += 1
        elif a[i] == "--scale":
            SCALE_OPT = "FBX_SCALE_" + a[i + 1]
            i += 2
        elif a[i] == "--suffix":
            FILE_SUFFIX = a[i + 1]
            i += 2
        else:
            i += 1
    build(body, out, export, tpose)
