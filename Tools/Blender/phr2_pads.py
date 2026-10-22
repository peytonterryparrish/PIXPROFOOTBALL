"""PH2: shoulder pads (47k-tri store model, 14 meshes, no logos) -> one skinned game mesh 'Pads' (3 material slots: Shell / Trim / Metal),
fitted to the R1 rest body, rigid-ish skin weights, and the jersey conform (the jersey is pushed out so it drapes OVER the pads).

Frame conventions as phr_common (metres, Z up, faces -Y, character centred x = 0, soles z = 0).  The pads are fitted in the R1 frame (like every other
part) BEFORE phr_body.reshape / the base warp, so they scale and move with the body exactly like the jersey.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *
import bmesh

PADSRC = "D:/PIXFootball/Source/PPF_NEW/shoulder_pads/Football_Shoulder_Pads_fbx/Football_Shoulder_Pads.FBX"
MATS = ["PHR_PadShell", "PHR_PadTrim", "PHR_PadMetal"]
# source material -> (material slot, atlas tile) ; trim atlas: u 0..0.5 = strap map, 0.5..1 = fabric map
SRC_MAP = {"plastic": (0, 0), "strap": (1, 0), "fabric": (1, 1), "hrome": (2, 0)}

# fit parameters (pad frame -> R1 frame)
FIT = dict(taper_x=0.78, taper_y=0.80, sx=1.03, sy=0.86, sz=0.88, dy=0.055, dz=1.05, cap_scale=0.45, cap_anchor=(0.30, 0.0, 0.58))
CLEAR = 0.007           # jersey clearance over the pad surface (m)


def _kind(o):
    """Source mesh -> material kind from its source material name."""
    mn = o.data.materials[0].name.lower() if len(o.data.materials) and o.data.materials[0] else o.name.lower()
    if "hrome" in mn: return "hrome"
    if "plastic" in mn: return "plastic"
    if "strap" in mn: return "strap"
    return "fabric"


def make_pads(arm, fit=None):
    f = dict(FIT)
    if fit:
        f.update(fit)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=PADSRC)
    new = [o for o in bpy.data.objects if o not in before]
    bpy.context.view_layer.update()
    src = [o for o in new if o.type == 'MESH']
    mats = [bpy.data.materials.get(n) or bpy.data.materials.new(n) for n in MATS]
    anchor = np.array(f["cap_anchor"])
    parts = []
    for o in src:
        me = o.data
        kind = _kind(o)
        M = o.matrix_world.copy()
        me.transform(M)
        o.parent = None
        o.matrix_world = Matrix.Identity(4)
        is_cap = "shoulder_strap" in o.name
        co = np.empty(len(me.vertices) * 3, np.float32)
        me.vertices.foreach_get('co', co)
        co = co.reshape(-1, 3).astype(np.float64)
        if is_cap:
            sgn = 1.0 if co[:, 0].mean() > 0 else -1.0
            a = anchor * np.array([sgn, 1, 1])
            s = f["cap_scale"]
            co = a + (co - a) * np.array([s, 0.85, s])
        else:
            # vest taper: full width / depth over the shoulders and upper chest, narrower at the lower ribs (a barrel chest under the jersey otherwise)
            t = _sm(0.26, 0.58, co[:, 2])
            co[:, 0] *= f["taper_x"] + (1.0 - f["taper_x"]) * t
            co[:, 1] *= f["taper_y"] + (1.0 - f["taper_y"]) * t
        co[:, 0] *= f["sx"]
        co[:, 1] = co[:, 1] * f["sy"] + f["dy"]
        co[:, 2] = co[:, 2] * f["sz"] + f["dz"]
        me.vertices.foreach_set('co', co.astype(np.float32).ravel())
        slot, tile = SRC_MAP[kind]
        me.materials.clear()
        for m in mats:
            me.materials.append(m)
        me.polygons.foreach_set('material_index', np.full(len(me.polygons), slot, np.int32))
        if not me.uv_layers:
            me.uv_layers.new(name="UVMap")
        uvl = me.uv_layers[0]
        uvl.name = "UVMap"
        if slot == 1:
            for l in uvl.data:
                l.uv = (tile * 0.5 + l.uv[0] * 0.5, l.uv[1])
        while len(me.uv_layers) > 1:
            me.uv_layers.remove(me.uv_layers[-1])
        me.update()
        parts.append(o)
    for o in new:
        if o.type != 'MESH':
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in parts:
        o.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    pads = bpy.context.view_layer.objects.active
    pads.name = "Pads"
    pads.data.name = "Pads"
    pads.parent = arm
    pads.matrix_parent_inverse = Matrix.Identity(4)
    md = pads.modifiers.new("Armature", 'ARMATURE')
    md.object = arm
    weight_pads(pads, arm)
    return pads


def _sm(a, b, x):
    t = np.clip((x - a) / (b - a + 1e-12), 0, 1)
    return t * t * (3 - 2 * t)


def weight_pads(pads, arm):
    """Rigid-ish: chest / back plates on Spine2 (Spine1 below the ribs), clavicle on the outer shoulder, caps follow the upper arm up to ~60 percent at the tip.
    Normalised, smooth gradients (no candy-wrap), <= 4 influences per vertex (Spine2, Spine1, clavicle, upper arm)."""
    me = pads.data
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3).astype(np.float64)
    ax = np.abs(co[:, 0])
    sg = np.sign(co[:, 0])
    jx = abs(np.array(arm.data.bones["rig:LeftArm"].head_local)[0])
    spine1 = np.array(arm.data.bones["rig:Spine1"].head_local)[2]
    t_arm = _sm(jx + 0.02, jx + 0.14, ax)
    w_arm = 0.62 * t_arm
    w_clav = (0.30 * _sm(0.10, jx, ax)) * (1.0 - t_arm * 0.5)
    w_s1 = 0.75 * (1.0 - _sm(spine1 - 0.12, spine1 + 0.06, co[:, 2])) * (1 - t_arm)
    w_s2 = np.maximum(1.0 - w_arm - w_clav - w_s1, 0.0)
    W = {"rig:Spine2": w_s2, "rig:Spine1": w_s1}
    for s, side in (("Left", 1), ("Right", -1)):
        m = (sg == side).astype(float)
        W["rig:%sArm" % s] = w_arm * m
        W["rig:%sShoulder" % s] = w_clav * m
    tot = sum(W.values())
    for name, w in W.items():
        w = w / np.maximum(tot, 1e-6)
        g = pads.vertex_groups.new(name=name)
        for i in np.nonzero(w > 0.004)[0]:
            g.add([int(i)], float(w[i]), 'REPLACE')


# ------------------------------------------------------------------------------------------------ jersey conform
def mesh_neighbours(me):
    nb = [set() for _ in me.vertices]
    for e in me.edges:
        a, b = e.vertices
        nb[a].add(b)
        nb[b].add(a)
    return [np.array(sorted(s), np.int32) for s in nb]


def vertex_normals(obj):
    me = obj.data
    me.update()
    nr = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('normal', nr)
    return nr.reshape(-1, 3).astype(np.float64)


def extend_sleeves(jersey, arm, ext=0.05, s_start=0.03):
    """Stretches the (very short) jersey sleeve along the upper arm so it can cover the pad epaulets (smoothstep in the outer part of the sleeve)."""
    me = jersey.data
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3).astype(np.float64)
    for s, g in (("Left", 1), ("Right", -1)):
        a0 = np.array(arm.data.bones["rig:%sArm" % s].head_local)
        a1 = np.array(arm.data.bones["rig:%sForeArm" % s].head_local)
        d = a1 - a0
        d /= np.linalg.norm(d)
        rel = co - a0
        sc = rel @ d
        rad = np.linalg.norm(rel - sc[:, None] * d[None, :], axis=1)
        side = (co[:, 0] * g > 0.17) & (sc > 0) & (rad < 0.11) & (co[:, 2] > a0[2] - 0.20)
        if not side.any():
            continue
        smax = sc[side].max()
        dl = ext * _sm(s_start, smax, sc) * side
        co += dl[:, None] * d[None, :]
    me.vertices.foreach_set('co', co.astype(np.float32).ravel())
    me.update()


def conform_to_pads(jersey, pads, arm, clear=CLEAR, back=0.04, reach=0.12, iters=14, dil=2, blend_weights=True):
    """Pushes every jersey vertex outward along its normal until it clears the pad surface by `clear` (rays from `back` inside the jersey through all pad layers),
    smooths the push over the mesh (skirt), and blends the jersey skin weights towards the pad weights where the push is large so the jersey follows the pads."""
    from mathutils.bvhtree import BVHTree
    from mathutils.kdtree import KDTree
    pco, ptv = mesh_world(pads)
    pme = pads.data
    pme.calc_loop_triangles()
    tmi = np.empty(len(pme.loop_triangles), np.int32)
    pme.loop_triangles.foreach_get('material_index', tmi)
    jxa = abs(arm.data.bones["rig:LeftArm"].head_local[0])
    cx = np.abs(pco[ptv][:, :, 0]).mean(axis=1)
    ptv = ptv[(tmi == 0) | (tmi == 2) | ((tmi == 1) & (cx > jxa - 0.03))]      # envelope = plastic shell + metal + the strap / fabric of the epaulets (trim between the body plates stays out: belt wings made slivers)
    verts = [Vector(c) for c in pco]
    bvh = BVHTree.FromPolygons(verts, [tuple(int(i) for i in t) for t in ptv])
    me = jersey.data
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3).astype(np.float64)
    nr = vertex_normals(jersey)
    push = np.zeros(len(co))
    for i in range(len(co)):
        n = Vector(nr[i])
        o = Vector(co[i]) - n * back
        start = o.copy()
        far = None
        for _ in range(6):
            hit = bvh.ray_cast(start, n, reach + back)
            if hit[0] is None:
                break
            d = (hit[0] - o).length
            if d > reach + back:
                break
            far = d
            start = hit[0] + n * 1e-4
        if far is not None and far + clear > back:
            push[i] = far + clear - back
    nb = mesh_neighbours(me)
    # local pad detail (rivets, plate edges) must not ripple the cloth: dilate (max over `dil` rings) so every covered vertex stays covered, then blur
    dl = push.copy()
    for _ in range(dil):
        nd = dl.copy()
        for i in range(len(dl)):
            if len(nb[i]):
                nd[i] = max(dl[i], dl[nb[i]].max())
        dl = nd
    sm = dl
    for _ in range(iters):
        nsm = sm.copy()
        for i in range(len(sm)):
            if len(nb[i]):
                nsm[i] = 0.5 * sm[i] + 0.5 * sm[nb[i]].mean()
        sm = nsm
    final = np.maximum(sm, push)
    zmin = co[:, 2].min()
    final *= _sm(zmin + 0.03, zmin + 0.24, co[:, 2])          # the pads end at the lower ribs: no push at the hem, the jersey tapers to the waist
    final *= _sm(-0.97, -0.55, nr[:, 2])                      # under-arm / downward facing cloth is not pushed (spikes in the armpit)
    final[final < 0.0006] = 0.0
    co += nr * final[:, None]
    me.vertices.foreach_set('co', co.astype(np.float32).ravel())
    me.update()
    if blend_weights:
        kd = KDTree(len(pco))
        for i, c in enumerate(pco):
            kd.insert(Vector(c), i)
        kd.balance()
        pg = {g.index: g.name for g in pads.vertex_groups}
        for v in me.vertices:
            c = min(1.0, final[v.index] / 0.025)
            if c <= 0.0:
                continue
            cur = {jersey.vertex_groups[g.group].name: g.weight for g in v.groups}
            _, pi, _ = kd.find(Vector(co[v.index]))
            pw = {pg[g.group]: g.weight for g in pads.data.vertices[pi].groups}
            tot = {}
            for k, w in cur.items():
                tot[k] = tot.get(k, 0) + (1 - c) * w
            for k, w in pw.items():
                tot[k] = tot.get(k, 0) + c * w
            top = sorted(tot.items(), key=lambda kv: -kv[1])[:4]
            s = sum(w for _, w in top)
            for k in cur:
                jersey.vertex_groups[k].remove([v.index])
            for k, w in top:
                vg = jersey.vertex_groups.get(k) or jersey.vertex_groups.new(name=k)
                vg.add([v.index], w / s, 'REPLACE')
    return final


# ------------------------------------------------------------------------------------------------ LODs of the jersey follow LOD0
def _co(me):
    a = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', a)
    return a.reshape(-1, 3).astype(np.float64)


def drape_jersey(lods, arm, pads, ext):
    """lods[0] = jersey LOD0: sleeve extension + conform over the pad envelope.  lods[1:]: the same displacement and skin weights (inverse distance from the nearest LOD0
    vertices), so every LOD has the same padded silhouette (an independent conform of a 600-triangle mesh would not)."""
    from mathutils.kdtree import KDTree
    j0 = lods[0]
    p0 = _co(j0.data)
    extend_sleeves(j0, arm, ext)
    conform_to_pads(j0, pads, arm)
    p1 = _co(j0.data)
    D = p1 - p0
    kd = KDTree(len(p0))
    for i, p in enumerate(p0):
        kd.insert(Vector(p), i)
    kd.balance()
    gname = {g.index: g.name for g in j0.vertex_groups}
    w0 = [{gname[g.group]: g.weight for g in v.groups} for v in j0.data.vertices]
    for lo in lods[1:]:
        co = _co(lo.data)
        new = co.copy()
        newW = []
        for i, p in enumerate(co):
            res = kd.find_n(Vector(p), 4)
            d = np.array([r[2] for r in res])
            w = 1.0 / (d + 0.003) ** 2
            w /= w.sum()
            idx = [r[1] for r in res]
            new[i] += (D[idx] * w[:, None]).sum(axis=0)
            acc = {}
            for wi, j in zip(w, idx):
                for k, x in w0[j].items():
                    acc[k] = acc.get(k, 0.0) + wi * x
            top = sorted(acc.items(), key=lambda kv: -kv[1])[:4]
            s = sum(x for _, x in top)
            newW.append([(k, x / s) for k, x in top])
        lo.data.vertices.foreach_set('co', new.astype(np.float32).ravel())
        lo.data.update()
        for g in list(lo.vertex_groups):
            lo.vertex_groups.remove(g)
        vgs = {}
        for i, lst in enumerate(newW):
            for k, x in lst:
                vg = vgs.get(k) or lo.vertex_groups.new(name=k)
                vgs[k] = vg
                vg.add([i], x, 'REPLACE')


def poke_report(jersey, pads, tol=0.002):
    """Share of pad vertices that lie outside the jersey surface (nearest jersey point + its face normal), per material slot; (outside_count, total, by_slot dict, worst distance)."""
    from mathutils.bvhtree import BVHTree
    jco, jtv = mesh_world(jersey)
    bvh = BVHTree.FromPolygons([Vector(c) for c in jco], [tuple(int(i) for i in t) for t in jtv])
    pme = pads.data
    pme.calc_loop_triangles()
    pco = _co(pme)
    slot = np.zeros(len(pco), np.int32)
    for t in pme.loop_triangles:
        for v in t.vertices:
            slot[v] = t.material_index
    out = 0
    by = {0: 0, 1: 0, 2: 0}
    worst = 0.0
    for i, p in enumerate(pco):
        loc, nor, fi, d = bvh.find_nearest(Vector(p))
        if loc is None:
            continue
        s = (Vector(p) - loc).dot(nor)
        if s > tol:
            out += 1
            by[int(slot[i])] += 1
            worst = max(worst, s)
    return out, len(pco), by, worst


def tuck_pads(pads, jersey, depth=0.008):
    """Pad vertices that still lie outside the draped jersey (epaulet rims under the sleeve end, the neck roll at the collar) are moved just under the jersey surface:
    the pads are an under-layer, nothing of them may show through or beside the cloth."""
    from mathutils.bvhtree import BVHTree
    jco, jtv = mesh_world(jersey)
    bvh = BVHTree.FromPolygons([Vector(c) for c in jco], [tuple(int(i) for i in t) for t in jtv])
    me = pads.data
    co = _co(me)
    moved = 0
    for i, p in enumerate(co):
        loc, nor, fi, d = bvh.find_nearest(Vector(p))
        if loc is None:
            continue
        if (Vector(p) - loc).dot(nor) > -depth:
            q = loc - nor * depth
            co[i] = (q.x, q.y, q.z)
            moved += 1
    me.vertices.foreach_set('co', co.astype(np.float32).ravel())
    me.update()
    return moved
