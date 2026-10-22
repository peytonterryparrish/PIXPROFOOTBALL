"""MODELS fix: bulky padded football bodies + proper LOD0..LOD3 for the three Draft Fever player bodies, exported as a .pixmesh data file for Unity.

  blender -b --python Tools/Blender/pix_player_fix.py -- [--body Small,Medium,Large] [--out Assets/_Licensed/PIX/Meshes/Bodies] [--no-bulk] [--preview Logs/model_qa]
                                                          [--report Logs/model_qa/fix_report.txt]

What it does (per body, source = Assets/_Licensed/DraftFever/Characters/Bodies/<Body>Player.fbx, never modified):
 1. imports the FBX, bakes the cm / 90 degree import transforms to metres, Z-up, identity (same helpers as pix_player_lods.py), drops the hair meshes.
 2. "bulk" morph of Torso_GEO / Legs_GEO (skipped with --no-bulk, used to prove the exporter reproduces the original mesh): the v0.5 bodies have thin
    thighs / calves and narrow shoulders for football gear.  Targets follow the All-Pro 2K8 reference (big shoulder pads, thick padded thighs, wide waists, linemen
    clearly wider than skill players).  The skeleton and the bind pose do NOT change (same bone positions), only the skin surface moves, so every Humanoid
    clip still fits.  Fields (all smooth, all vertex-group / height driven):
      - shoulder pads: radial scale of the shoulder cap vertices about the arm axis (weights on shoulder / arm bones) + lateral widening (vertical profile)
      - vertical profile (Torso + Legs top): hips, waist, chest and shoulder scale about the body centre line (x and depth)
      - thighs: radial scale about each thigh axis (anisotropic, grows outward so the thighs do not collide), fades out at the knee and above the hip joint
      - calves: isotropic radial scale about the shin axis
 3. skin weights: at most 4 influences per vertex, renormalised (what Unity keeps with SkinQuality.Bone4), unweighted vertices get the nearest weighted neighbour.
 4. LOD1..LOD3 by Decimate (collapse) with UV-border protection (pix_player_lods.make_lod), per-part ratios (Hands / Shoes are micro detail and go lower).
 5. writes <out>/<Body>.pixmesh (binary, Unity world bind-pose frame: x_u = -x_b, y_u = z_b, z_u = -y_b, winding flipped) read by PixFootball.EditorTools.BodyMeshImporter,
    plus a table of verts / tris and the cross-section circumferences before / after in the report.
"""
import sys, os, re, math, struct
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix_player_common import *
import pix_player_lods as PL
import bpy, bmesh
import numpy as np
from mathutils import Matrix, Vector

RATIOS = {"default": [0.5, 0.25, 0.18], "Hands": [0.30, 0.12, 0.05], "Shoes": [0.40, 0.20, 0.10]}

# per-size bulk parameters.  kx / ky = scale across / along the body depth (1.0 = unchanged)
BULK = {
    "Small":  dict(ks=1.08, pad=1.09, chest=(1.03, 1.03), waist=(1.05, 1.04), hip=(1.04, 1.04), thigh=(1.12, 1.14), calf=1.06, r0=0.092),
    "Medium": dict(ks=1.11, pad=1.14, chest=(1.05, 1.04), waist=(1.08, 1.05), hip=(1.06, 1.05), thigh=(1.18, 1.20), calf=1.09, r0=0.092),
    "Large":  dict(ks=1.14, pad=1.17, chest=(1.09, 1.08), waist=(1.12, 1.09), hip=(1.08, 1.07), thigh=(1.22, 1.24), calf=1.11, r0=0.105),
}


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def norm_name(n):
    return n.lower().replace(".", "_")


def parse():
    a = script_args()
    o = dict(bodies=["Small", "Medium", "Large"], out=os.path.join(PROJECT_ROOT, "Assets", "_Licensed", "PIX", "Meshes", "Bodies"), bulk=True,
             preview=None, report=os.path.join(PROJECT_ROOT, "Logs", "model_qa", "fix_report.txt"), params=None)
    i = 0
    while i < len(a):
        k = a[i]
        if k == "--body": o["bodies"] = a[i + 1].split(","); i += 2
        elif k == "--out": o["out"] = a[i + 1]; i += 2
        elif k == "--no-bulk": o["bulk"] = False; i += 1
        elif k == "--preview": o["preview"] = a[i + 1]; i += 2
        elif k == "--report": o["report"] = a[i + 1]; i += 2
        else: raise SystemExit("unknown arg " + k)
    return o


# ------------------------------------------------------------------ geometry helpers
def get_co(obj):
    co = np.empty(len(obj.data.vertices) * 3, np.float32)
    obj.data.vertices.foreach_get('co', co)
    return co.reshape(-1, 3).astype(np.float64)


def set_co(obj, co):
    obj.data.vertices.foreach_set('co', np.ascontiguousarray(co, dtype=np.float32).reshape(-1))
    obj.data.update()


def group_weights(obj):
    """dict normalised bone name -> float array [N] of the vertex group weights."""
    n = len(obj.data.vertices)
    names = {vg.index: norm_name(vg.name) for vg in obj.vertex_groups}
    out = {}
    for v in obj.data.vertices:
        for g in v.groups:
            arr = out.setdefault(names[g.group], np.zeros(n))
            arr[v.index] += g.weight
    return out


def hull_perimeter(pts):
    p = sorted(set((round(float(x), 5), round(float(y), 5)) for x, y in pts))
    if len(p) < 3: return 0.0
    def cross(o, a, b): return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo = []
    for q in p:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], q) <= 0: lo.pop()
        lo.append(q)
    up = []
    for q in reversed(p):
        while len(up) >= 2 and cross(up[-2], up[-1], q) <= 0: up.pop()
        up.append(q)
    h = lo[:-1] + up[:-1]
    return sum(math.hypot(h[i][0] - h[(i + 1) % len(h)][0], h[i][1] - h[(i + 1) % len(h)][1]) for i in range(len(h)))


def section_pts(co, tv, z):
    a, b, c = co[tv[:, 0]], co[tv[:, 1]], co[tv[:, 2]]
    pts = []
    for p, q in ((a, b), (b, c), (c, a)):
        m = ((p[:, 2] - z) * (q[:, 2] - z) < 0)
        if not m.any(): continue
        t = (z - p[m, 2]) / (q[m, 2] - p[m, 2])
        pts.append(p[m] + (q[m] - p[m]) * t[:, None])
    return np.concatenate(pts) if pts else np.zeros((0, 3))


def circ_at(obj, z, side=None):
    co, nr, uv, tl, tv, tp = world_arrays(obj)
    s = section_pts(co, tv, z)
    if side is not None and len(s):
        s = s[s[:, 0] > 0] if side > 0 else s[s[:, 0] < 0]
    return hull_perimeter(s[:, :2]) if len(s) >= 3 else 0.0, (float(np.ptp(s[:, 0])) if len(s) else 0.0), (float(np.ptp(s[:, 1])) if len(s) else 0.0)


# ------------------------------------------------------------------ landmarks
def landmarks(arm):
    b = {norm_name(x.name): x for x in arm.data.bones}
    def head(*names):
        for n in names:
            if n in b: return np.array(b[n].head_local)
        raise KeyError(names)
    def tail(*names):
        for n in names:
            if n in b: return np.array(b[n].tail_local)
        raise KeyError(names)
    lm = dict(
        hips=head("root_x", "hip"), thigh=head("thigh_stretch_l"), knee=head("leg_stretch_l"), ankle=head("foot_l"),
        shoulder=head("arm_stretch_l"), arm_tail=tail("arm_stretch_l"), neck=head("neck_x", "neck"),
        sp1=head("spine_01_x", "spine_01"), sp2=head("spine_02_x", "spine_02"), sp3=head("spine_03_x", "spine_03"))
    for nm in ("thigh_stretch", "leg_stretch", "foot", "arm_stretch"):   # QA-943: the right side must mirror the left (the fields use sign(x))
        l, r = b.get(nm + "_l"), b.get(nm + "_r")
        if l is None or r is None:
            raise SystemExit("pix_player_fix.py: bone '%s_l' or '%s_r' not found (rig: %s)" % (nm, nm, sorted(b)[:12]))
        if abs(l.head_local[0] + r.head_local[0]) > 0.02 or abs(l.head_local[2] - r.head_local[2]) > 0.02:
            raise SystemExit("pix_player_fix.py: %s is not symmetric: left %s right %s" % (nm, tuple(l.head_local), tuple(r.head_local)))
    lm["yc"] = float(np.mean([lm["hips"][1], lm["sp1"][1], lm["sp2"][1], lm["sp3"][1]]))
    return lm


# ------------------------------------------------------------------ the bulk morph
def bulk_torso(co, W, lm, P):
    H, S, N = lm["thigh"][2], lm["shoulder"][2], lm["neck"][2]
    out = co.copy()
    # shoulder pads: radial scale about the arm axis, driven by the arm / shoulder weights
    w = np.zeros(len(co))
    for k, arr in W.items():
        if k.startswith("shoulder") or k.startswith("arm_stretch") or k.startswith("arm_twist"): w += arr
    w = np.clip(w, 0, 1)
    side = np.where(out[:, 0] >= 0, 1.0, -1.0)
    A = np.stack([lm["shoulder"][0] * side, np.full(len(co), lm["shoulder"][1]), np.full(len(co), lm["shoulder"][2])], axis=1)
    d = np.array([lm["arm_tail"][0] - lm["shoulder"][0], lm["arm_tail"][1] - lm["shoulder"][1], lm["arm_tail"][2] - lm["shoulder"][2]])
    d = d / np.linalg.norm(d)
    dd = d[None, :] * np.stack([side, np.ones(len(co)), np.ones(len(co))], axis=1)
    rel = out - A
    along = np.sum(rel * dd, axis=1, keepdims=True)
    radial = rel - along * dd
    # a pad only on the part of the shoulder that is outside the neck
    outside = smooth(0.06, 0.20, np.abs(out[:, 0]))
    f = 1.0 + (P["pad"] - 1.0) * w * outside
    out = A + along * dd + radial * f[:, None]
    return vertical_profile(out, lm, P)


def vertical_profile(out, lm, P):
    H, S, N = lm["thigh"][2], lm["shoulder"][2], lm["neck"][2]
    mid = (H + S) * 0.5
    zs = [H - 0.18, H - 0.02, H + 0.10, mid, S - 0.14, S - 0.03, N + 0.02]
    kx = [1.0, P["hip"][0], P["hip"][0], P["waist"][0], P["chest"][0], P["ks"], 1.0]
    ky = [1.0, P["hip"][1], P["hip"][1], P["waist"][1], P["chest"][1], P["chest"][1], 1.0]
    z = out[:, 2]
    sx = np.interp(z, zs, kx); sy = np.interp(z, zs, ky)
    out[:, 0] = out[:, 0] * sx
    out[:, 1] = lm["yc"] + (out[:, 1] - lm["yc"]) * sy
    return out


def bulk_legs(co, W, lm, P):
    H, K, A_z = lm["thigh"][2], lm["knee"][2], lm["ankle"][2]
    out = co.copy()
    side = np.where(out[:, 0] >= 0, 1.0, -1.0)
    z = out[:, 2]
    # thigh axis (hip joint -> knee), per side
    hj = np.stack([lm["thigh"][0] * side, np.full(len(co), lm["thigh"][1]), np.full(len(co), H)], axis=1)
    kn = np.stack([lm["knee"][0] * side, np.full(len(co), lm["knee"][1]), np.full(len(co), K)], axis=1)
    t = ((H - z) / (H - K))[:, None]
    c = hj + (kn - hj) * t
    u = (z - K) / (H - K)
    p = smooth(0.0, 0.55, u) * (1.0 - smooth(0.55, 0.90, u))
    kx_, ky_ = P["thigh"]
    ex = 1.0 + (kx_ - 1.0) * p; ey = 1.0 + (ky_ - 1.0) * p
    shift = side * 0.65 * (kx_ - 1.0) * P["r0"] * p
    dx = out[:, 0] - c[:, 0]; dy = out[:, 1] - c[:, 1]
    out[:, 0] = c[:, 0] + shift + dx * ex
    out[:, 1] = c[:, 1] + dy * ey
    # calves: isotropic radial scale about the shin axis (knee -> ankle)
    an = np.stack([lm["ankle"][0] * side, np.full(len(co), lm["ankle"][1]), np.full(len(co), A_z)], axis=1)
    t2 = ((K - z) / (K - A_z))[:, None]
    c2 = kn + (an - kn) * t2
    pc = smooth(A_z + 0.04, A_z + 0.20, z) * (1.0 - smooth(K - 0.10, K + 0.06, z))
    fc = 1.0 + (P["calf"] - 1.0) * pc
    out[:, 0] = c2[:, 0] + (out[:, 0] - c2[:, 0]) * fc
    out[:, 1] = c2[:, 1] + (out[:, 1] - c2[:, 1]) * fc
    return vertical_profile(out, lm, P)


# ------------------------------------------------------------------ weights
def limit_weights(obj, max_inf=4):
    """At most max_inf influences per vertex, renormalised; vertices without weights copy the nearest weighted vertex."""
    me = obj.data
    names = {vg.index: vg.name for vg in obj.vertex_groups}
    # twist bones (thigh_twist / leg_twist / arm_twist / forearm_twist) are children of the stretch bones and not Humanoid-mapped, so in Unity they always rotate with
    # their parent: fold their weights into the stretch bone (same motion, fewer influences, no 4-bone truncation loss; QA-944)
    fold = {}
    for gi, nm in names.items():
        if "_twist" in norm_name(nm):
            tgt = nm.replace("_twist", "_stretch")
            tg = obj.vertex_groups.get(tgt) or obj.vertex_groups.new(name=tgt)
            fold[gi] = tg.index
    names = {vg.index: vg.name for vg in obj.vertex_groups}
    new = []
    empty = []
    for v in me.vertices:
        acc = {}
        for g in v.groups:
            if g.weight > 1e-4:
                k = fold.get(g.group, g.group); acc[k] = acc.get(k, 0.0) + g.weight
        ws = sorted(((w, k) for k, w in acc.items()), reverse=True)[:max_inf]
        tot = sum(w for w, _ in ws)
        if tot < 1e-6: empty.append(v.index); new.append([]); continue
        new.append([(g, w / tot) for w, g in ws])
    if empty:
        co = get_co(obj); good = np.array([i for i in range(len(new)) if new[i]])
        for e in empty:
            j = good[np.argmin(np.linalg.norm(co[good] - co[e], axis=1))]
            new[e] = list(new[j])
    for vg in obj.vertex_groups:
        vg.remove(list(range(len(me.vertices))))
    for vi, lst in enumerate(new):
        for g, w in lst:
            obj.vertex_groups[g].add([vi], w, 'REPLACE')
    return len(empty)


# ------------------------------------------------------------------ export
def write_str(f, s):
    b = s.encode("utf-8"); n = len(b); out = bytearray()
    while True:
        byte = n & 0x7F; n >>= 7
        out.append(byte | (0x80 if n else 0))
        if not n: break
    f.write(bytes(out)); f.write(b)


def mesh_export_data(obj, bone_index):
    """Unity-ready arrays: positions / normals in the Unity world bind frame, uv0, weights (4 x (bone idx, w)), triangles (flipped), submesh per triangle."""
    me = obj.data
    me.calc_loop_triangles()
    M = np.array(obj.matrix_world, dtype=np.float64)
    nv = len(me.vertices)
    co = np.empty(nv * 3, np.float32); me.vertices.foreach_get('co', co); co = co.reshape(-1, 3).astype(np.float64) @ M[:3, :3].T + M[:3, 3]
    nl = len(me.loops)
    lv = np.empty(nl, np.int32); me.loops.foreach_get('vertex_index', lv)
    uvl = me.uv_layers.active
    uv = np.zeros((nl, 2), np.float32)
    if uvl is not None:
        t = np.empty(nl * 2, np.float32); uvl.data.foreach_get('uv', t); uv = t.reshape(-1, 2)
    cn = np.empty(nl * 3, np.float32); me.corner_normals.foreach_get('vector', cn); cn = cn.reshape(-1, 3).astype(np.float64) @ M[:3, :3].T
    cn /= np.maximum(np.linalg.norm(cn, axis=1, keepdims=True), 1e-9)
    lt = me.loop_triangles
    nt = len(lt)
    tl = np.empty(nt * 3, np.int32); lt.foreach_get('loops', tl); tl = tl.reshape(-1, 3)
    tpoly = np.empty(nt, np.int32); lt.foreach_get('polygon_index', tpoly)
    pm = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('material_index', pm)
    sub = pm[tpoly]
    # unique (vertex, uv, normal) corners
    keyarr = np.concatenate([lv[:, None].astype(np.float64), np.round(uv.astype(np.float64) * 1e5), np.round(cn * 1e3)], axis=1)
    uniq, first, inverse = np.unique(keyarr, axis=0, return_index=True, return_inverse=True)
    inverse = inverse.reshape(-1)
    vidx = lv[first]
    P = co[vidx]; Nn = cn[first]; U = uv[first]
    # world -> Unity
    Pu = np.stack([-P[:, 0], P[:, 2], -P[:, 1]], axis=1); Nu = np.stack([-Nn[:, 0], Nn[:, 2], -Nn[:, 1]], axis=1)
    tris = inverse[tl]
    tris = tris[:, [0, 2, 1]]
    # weights per original vertex -> per unique corner
    names = {vg.index: vg.name for vg in obj.vertex_groups}
    bw = np.full((nv, 4), -1, np.int32); ww = np.zeros((nv, 4), np.float32)
    for v in me.vertices:
        gs = sorted(((g.weight, g.group) for g in v.groups if g.weight > 1e-4 and names[g.group] in bone_index), reverse=True)[:4]
        tot = sum(w for w, _ in gs) or 1.0
        for k, (w, g) in enumerate(gs):
            bw[v.index, k] = bone_index[names[g]]; ww[v.index, k] = w / tot
    return Pu.astype(np.float32), Nu.astype(np.float32), U.astype(np.float32), bw[vidx], ww[vidx], tris.astype(np.int32), sub.astype(np.int32)


def write_pixmesh(path, entries, bone_names):
    """entries: list of (name, lod, obj)."""
    bone_index = {n: i for i, n in enumerate(bone_names)}
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "wb") as f:
        f.write(b"PIXM"); f.write(struct.pack("<ii", 1, len(entries)))
        f.write(struct.pack("<i", len(bone_names)))
        for n in bone_names: write_str(f, n)
        for name, lod, obj in entries:
            Pu, Nu, U, bw, ww, tris, sub = mesh_export_data(obj, bone_index)
            write_str(f, name); f.write(struct.pack("<iiii", lod, len(Pu), len(tris), int(sub.max()) + 1 if len(sub) else 1))
            f.write(Pu.tobytes()); f.write(Nu.tobytes()); f.write(U.tobytes())
            f.write(bw.astype('<i4').tobytes()); f.write(ww.astype('<f4').tobytes())
            f.write(tris.astype('<i4').tobytes()); f.write(sub.astype('<i4').tobytes())


# ------------------------------------------------------------------ preview (workbench, ortho)
def preview(copies, path_prefix, tag):
    """Workbench ortho renders, front and side; `copies` (the untouched bodies) stand next to the live meshes at 0.8 m."""
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'; sc.display.shading.color_type = 'OBJECT'
    sc.render.film_transparent = False
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.45, 0.47, 0.5)
    cam_d = bpy.data.cameras.new("c"); cam_d.type = 'ORTHO'; cam_d.ortho_scale = 3.2
    cam = bpy.data.objects.new("c", cam_d); sc.collection.objects.link(cam); sc.camera = cam
    sc.render.resolution_x = 1500; sc.render.resolution_y = 1000
    for view in ("front", "side"):
        for c in copies:
            c.location = (-0.8, 0, 0) if view == "front" else (0, 0.8, 0)
        if view == "front":
            cam.location = (-0.4, -8, 0.95); cam.rotation_euler = (math.radians(90), 0, 0)
        else:
            cam.location = (8, 0.4, 0.95); cam.rotation_euler = (math.radians(90), 0, math.radians(90))
        sc.render.filepath = "%s_%s_%s.png" % (path_prefix, tag, view)
        bpy.ops.render.render(write_still=True)


def colour_for(name):
    if name.startswith("Torso"): return (0.15, 0.4, 0.8, 1)
    if name.startswith("Legs"): return (0.9, 0.9, 0.92, 1)
    if name.startswith("Arms") or name.startswith("Hands"): return (0.8, 0.55, 0.42, 1)
    if name.startswith("Shoes"): return (0.1, 0.1, 0.12, 1)
    return (0.8, 0.6, 0.5, 1)


# ------------------------------------------------------------------ main per body
def process(body, o, rep):
    fbx = os.path.join(BODY_DIR, "%sPlayer.fbx" % body)
    import_player(fbx)
    arm = next(x for x in bpy.data.objects if x.type == 'ARMATURE')
    for m in [x for x in bpy.data.objects if x.type == 'MESH' and x.name.startswith("Hair")]:
        bpy.data.objects.remove(m, do_unlink=True)
    meshes = [x for x in bpy.data.objects if x.type == 'MESH']
    PL.apply_transforms(arm, meshes)
    lm = landmarks(arm)
    P = BULK[body]
    rep("=" * 100); rep("%s  landmarks: hip joint z %.3f knee z %.3f ankle z %.3f shoulder z %.3f neck z %.3f  yc %.3f" % (body, lm["thigh"][2], lm["knee"][2], lm["ankle"][2], lm["shoulder"][2], lm["neck"][2], lm["yc"]))
    by = {re.sub(r"_GEO.*$", "", m.name): m for m in meshes}
    torso = next(m for m in meshes if m.name.startswith("Torso")); legs = next(m for m in meshes if m.name.startswith("Legs"))
    # circumference table before
    hips_z = lm["thigh"][2]
    probe = [("waist", (lm["thigh"][2] + lm["shoulder"][2]) * 0.5 - 0.08, torso, None), ("chest", lm["shoulder"][2] - 0.18, torso, None),
             ("hip", lm["thigh"][2] + 0.02, legs, None), ("thigh_hi", lm["thigh"][2] - 0.12, legs, 1), ("thigh_mid", (lm["thigh"][2] + lm["knee"][2]) * 0.5, legs, 1),
             ("knee+.1", lm["knee"][2] + 0.10, legs, 1), ("calf", (lm["knee"][2] + lm["ankle"][2]) * 0.5, legs, 1)]
    before = {n: circ_at(ob, z, s) for n, z, ob, s in probe}
    pre = []
    if o["preview"]:
        for m in meshes:
            if re.match(r"^(Torso|Legs|Arms|Hands|Shoes)", m.name): m.color = colour_for(m.name)
        pre = []
        for m in meshes:
            if re.match(r"^(Torso|Legs|Arms|Hands|Shoes|Head)", m.name):
                c = m.copy(); c.data = m.data.copy(); c.parent = None; c.matrix_world = Matrix.Identity(4)
                c.color = colour_for(m.name); bpy.context.scene.collection.objects.link(c); pre.append(c)
    # bulk
    if o["bulk"]:
        for ob, fn in ((torso, bulk_torso), (legs, bulk_legs)):
            W = group_weights(ob)
            set_co(ob, fn(get_co(ob), W, lm, P))
    after = {n: circ_at(ob, z, s) for n, z, ob, s in probe}
    rep("section circumference (m) before -> after (hull perimeter; legs per leg)")
    for n, z, ob, s in probe:
        rep("  %-10s z %.3f  %.3f -> %.3f  (x%.2f)   width %.3f -> %.3f  depth %.3f -> %.3f" % (n, z, before[n][0], after[n][0], after[n][0] / max(before[n][0], 1e-6), before[n][1], after[n][1], before[n][2], after[n][2]))
    # bounds
    for ob in (torso, legs):
        co = get_co(ob); rep("  %s bounds x %.3f..%.3f y %.3f..%.3f z %.3f..%.3f" % (ob.name, co[:, 0].min(), co[:, 0].max(), co[:, 1].min(), co[:, 1].max(), co[:, 2].min(), co[:, 2].max()))
    # weights
    for m in meshes:
        if re.match(r"^(Torso|Legs|Arms|Hands|Shoes|Head)", m.name):
            ne = limit_weights(m)
            if ne: rep("  %s: %d unweighted vertices fixed" % (m.name, ne))
    if o["preview"]:
        for m in meshes:
            m.color = colour_for(m.name)
        o["preview"] = os.path.abspath(o["preview"]); os.makedirs(o["preview"], exist_ok=True)
        for m in list(meshes):
            if m.name.startswith("Eyes"): m.hide_render = True
        preview(pre, os.path.join(o["preview"], "blender_preview_" + body), "bulk" if o["bulk"] else "orig")
    # LODs
    opts = dict(seam=True, seam_weight=1.0, sharp=60.0)
    entries = []
    bone_set = {b.name for b in arm.data.bones}   # the decimator leaves its helper group (_pix_seam) in the LOD meshes: only real bones may carry weight
    def used_groups(ob):
        nm = {vg.index: vg.name for vg in ob.vertex_groups}
        return {nm[g.group] for v in ob.data.vertices for g in v.groups if g.weight > 1e-4 and nm.get(g.group) in bone_set}
    rep("LOD table (verts / tris)")
    for src in sorted([m for m in meshes if re.match(r"^(Torso|Legs|Arms|Hands|Shoes|Head|Eyes)", m.name)], key=lambda m: m.name):
        base = src.name
        kind = next((k for k in RATIOS if k != "default" and base.startswith(k)), "default")
        ratios = RATIOS[kind]
        row = [(len(src.data.vertices), PL.tri_count(src.data))]
        lods = [src]
        for li, r in enumerate(ratios, start=1):
            full = base.startswith("Eyes")
            o_l = dict(opts, seam=(opts["seam"] and not (kind in ("Shoes", "Hands") and li >= 2)))   # shoes: UV-border protection pins ~28 percent of the vertices; at 26 m+ a slightly distorted sole texture is invisible (QA-951)
            lod = PL.make_lod(src, 1.0 if full else r, "%s_LOD%d" % (base, li), arm, o_l)
            lods.append(lod)
            row.append((len(lod.data.vertices), PL.tri_count(lod.data)))
        for li, lo in enumerate(lods):
            entries.append((base, li, lo))
        rep("  %-34s " % base + "  ".join("LOD%d %5d/%5d" % (i, v, t) for i, (v, t) in enumerate(row)))
    rep("LOD silhouette vs LOD0 (hull circumference ratio at the probe heights)")
    for n, z, ob, s in probe:
        base = ob.name.replace("_LOD0", "")
        ls = [e[2] for e in entries if e[0] == base]
        c0 = circ_at(ls[0], z, s)[0]
        rep("  %-10s " % n + "  ".join("LOD%d x%.3f" % (i, circ_at(l, z, s)[0] / max(c0, 1e-6)) for i, l in enumerate(ls) if i > 0))
    out = os.path.join(o["out"], body + ".pixmesh")
    bone_names = sorted({n for _, _, lo in entries for n in used_groups(lo)})   # only bones that carry weights (QA-945); must be computed AFTER the LODs exist
    unw = 0
    for _, _, lo in entries:
        if lo.name.startswith("Eyes"): continue
        nm = {vg.index: vg.name for vg in lo.vertex_groups}
        unw += sum(1 for v in lo.data.vertices if not any(g.weight > 1e-4 and nm.get(g.group) in bone_set for g in v.groups))
    if not bone_names or unw:
        raise SystemExit("pix_player_fix.py: export would carry no or incomplete skin weights (bones %d, unweighted vertices %d)" % (len(bone_names), unw))
    write_pixmesh(out, entries, bone_names)
    rep("wrote %s (%.1f MB)" % (out, os.path.getsize(out) / 1e6))


def main():
    if bpy.app.version < (4, 1, 0):
        raise SystemExit("pix_player_fix.py needs Blender 4.1+ (mesh.corner_normals); running %s" % (bpy.app.version_string,))
    o = parse()
    lines = []
    def rep(s):
        print(s); lines.append(s)
    for body in o["bodies"]:
        process(body, o, rep)
    os.makedirs(os.path.dirname(o["report"]), exist_ok=True)
    open(o["report"], "w").write("\n".join(lines))


if __name__ == "__main__":
    main()
