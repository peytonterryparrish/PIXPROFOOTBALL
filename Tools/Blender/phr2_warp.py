"""PH2: extra body bases.  Four purchased A-pose bodies share R1's 52-bone `rig:*` skeleton and its base topology / UVs.  Only their SHAPE is used:
a smooth displacement field R1 -> new body is measured from the meshes that exist on both (matched vertex-by-vertex through identical UV coordinates), cached,
and applied as the LAST step of the normal phr_build pipeline (every part, every LOD, the pads), together with the new rest skeleton.
Heads, hair, eyes, faces, textures, names, numbers and all branding of the purchased bodies are never read (only vertex positions of cloth / skin / gear
meshes and bone positions).

  blender -b --python Tools/Blender/phr2_warp.py -- build <Base>|all       writes D:/PIXFootball/PhotorealWork/v2/field_<Base>.npz
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phr_common
from phr_common import *

# neutral base names (nothing named after a person ships): the source folder of the purchased body is only used here
BASES = {
    "Std": None,                       # R1 (the existing S / M / L)
    "Stocky": "derwin_james",          # safety / linebacker: broad shoulders, thick thighs and calves
    "Athletic": "jalen_ramsey",        # corner / safety: lean, near R1
    "Tall": "jordan_love",             # tall QB / TE (+9 cm, long legs)
    "Runner": "justin_jefferson",      # WR / RB: wide hips, strong calves
}
SRC_ROOT = "D:/PIXFootball/Source/PPF_NEW/players/%s/A-P.FBX"
CACHE = os.path.join(WORK, "v2")
# R1 meshes that carry the shape (skinned cloth / skin); rigid head gear is moved with the head bone instead
FIELD_MESHES = ["arms", "jersey", "pants", "socks", "shorts", "cleats", "armsleeve", "innershirt", "handwarmer", "twistband", "wristcoach", "towel"]
HEAD_LIKE = ("Head", "Eyes")
RIGID = ("Helmet_Shell", "FaceMask", "Chinstrap", "HelmetPad", "Visor")
KEY = 4000.0


def field_path(base):
    return os.path.join(CACHE, "field_%s.npz" % base)


def _ukey(u, v):
    return (int(round(u * KEY)), int(round(v * KEY)))


def _world_co(o):
    me = o.data
    M = np.array(o.matrix_world, np.float64)
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    return co.reshape(-1, 3).astype(np.float64) @ M[:3, :3].T + M[:3, 3]


def _bone_table(arm):
    """name -> (head, tail, roll) in world space (armature object transform is identity or already baked)."""
    out = {}
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    M = np.array(arm.matrix_world, np.float64)
    for eb in arm.data.edit_bones:
        h = np.array(eb.head) @ M[:3, :3].T + M[:3, 3]
        t = np.array(eb.tail) @ M[:3, :3].T + M[:3, 3]
        out[eb.name] = (h, t, eb.roll)
    bpy.ops.object.mode_set(mode='OBJECT')
    return out


def build_field(base):
    folder = BASES[base]
    reset()
    orig = phr_common.unwrap_jersey
    phr_common.unwrap_jersey = lambda *a, **k: None       # keep the ORIGINAL R1 jersey UV: it is the match key
    try:
        arm, meshes = phr_common.import_r1()
    finally:
        phr_common.unwrap_jersey = orig
    r1bones = _bone_table(arm)
    for o in list(bpy.data.objects):
        o.name = "R1X_" + o.name
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=SRC_ROOT % folder)
    bpy.context.view_layer.update()
    new = [o for o in bpy.data.objects if o not in before]
    narm = [o for o in new if o.type == 'ARMATURE'][0]
    nmesh = {o.name.lower().replace("\u1ec9", "i"): o for o in new if o.type == 'MESH'}
    # centre like R1: hips on x = 0 (hips x of R1 after its own shift is ~0), soles on z = 0
    hips = np.array(narm.matrix_world @ narm.data.bones["rig:Hips"].head_local)
    zmin = _world_co(nmesh["cleats"])[:, 2].min()
    shift = np.array([-hips[0], 0.0, -zmin])
    print("PH2 base", base, "shift", np.round(shift, 4).tolist())
    # per new mesh: uv key -> world positions
    newdict = {}
    for n, o in nmesh.items():
        me = o.data
        if not me.uv_layers:
            continue
        co = _world_co(o) + shift
        uv = np.empty(len(me.loops) * 2, np.float32)
        me.uv_layers[0].data.foreach_get('uv', uv)
        uv = uv.reshape(-1, 2)
        vi = np.empty(len(me.loops), np.int32)
        me.loops.foreach_get('vertex_index', vi)
        d = {}
        for li in range(len(me.loops)):
            d.setdefault(_ukey(*uv[li]), []).append(co[vi[li]])
        newdict[n] = d
    P_list, D_list = [], []
    for rn in FIELD_MESHES:
        if rn not in meshes:
            continue
        ro = meshes[rn]
        rme = ro.data
        if not rme.uv_layers:
            continue
        rco = _world_co(ro)
        ruv = np.empty(len(rme.loops) * 2, np.float32)
        rme.uv_layers[0].data.foreach_get('uv', ruv)
        ruv = ruv.reshape(-1, 2)
        rvi = np.empty(len(rme.loops), np.int32)
        rme.loops.foreach_get('vertex_index', rvi)
        keys = [_ukey(*ruv[li]) for li in range(len(rme.loops))]
        kset = set(keys)
        cov = sorted(((len(kset & set(d.keys())) / float(len(kset)), n) for n, d in newdict.items()), reverse=True)
        use = [n for c, n in cov if c >= 0.25]
        if not use or cov[0][0] < 0.5:
            print("PH2   %-12s unmatched (best %.2f %s)" % (rn, cov[0][0], cov[0][1]))
            continue
        cand = {}
        for n in use:
            for k, lst in newdict[n].items():
                if k in kset:
                    cand.setdefault(k, []).extend(lst)
        best = {}
        for li, k in enumerate(keys):
            if k not in cand:
                continue
            v = rvi[li]
            p = rco[v]
            c = np.array(cand[k])
            dd = np.linalg.norm(c - p, axis=1)
            j = int(dd.argmin())
            if dd[j] < 0.14 and (v not in best or dd[j] < best[v][1]):
                best[v] = (c[j], dd[j])
        idx = np.array(sorted(best))
        P = rco[idx]
        D = np.array([best[v][0] for v in idx]) - P
        print("PH2   %-12s matched %d / %d vertices via %s (cov %.2f), mean |D| %.4f max %.4f" % (rn, len(idx), len(rco), "+".join(use), cov[0][0], np.linalg.norm(D, axis=1).mean(), np.linalg.norm(D, axis=1).max()))
        P_list.append(P)
        D_list.append(D)
    P = np.concatenate(P_list)
    D = np.concatenate(D_list)
    # outlier filter: a sample whose displacement differs from the median of its neighbours by > 2 cm is a wrong match
    from mathutils.kdtree import KDTree
    kd = KDTree(len(P))
    for i, p in enumerate(P):
        kd.insert(Vector(p), i)
    kd.balance()
    keep = np.ones(len(P), bool)
    for i, p in enumerate(P):
        nb = [j for _, j, _ in kd.find_n(Vector(p), 9)]
        med = np.median(D[nb], axis=0)
        if np.linalg.norm(D[i] - med) > 0.02:
            keep[i] = False
    print("PH2   outliers dropped", int((~keep).sum()), "of", len(P))
    P, D = P[keep], D[keep]
    # bones
    nbones = _bone_table(narm)
    names = sorted(set(r1bones) & set(nbones))
    bh = np.array([nbones[n][0] + shift for n in names])
    bt = np.array([nbones[n][1] + shift for n in names])
    br = np.array([nbones[n][2] for n in names])
    rh = np.array([r1bones[n][0] for n in names])
    ensure(CACHE)
    np.savez(field_path(base), P=P, D=D, names=np.array(names), new_head=bh, new_tail=bt, new_roll=br, r1_head=rh, shift=shift)
    print("PH2 wrote", field_path(base), len(P), "samples,", len(names), "bones; hip z", rh[names.index("rig:Hips")][2], "->", bh[names.index("rig:Hips")][2])


# ------------------------------------------------------------------------------------------------ applying (inside a phr build session)
class Field:
    def __init__(self, base):
        z = np.load(field_path(base))
        self.P, self.D = z["P"], z["D"]
        self.names = [str(n) for n in z["names"]]
        self.new_head, self.new_tail, self.new_roll, self.r1_head = z["new_head"], z["new_tail"], z["new_roll"], z["r1_head"]
        from mathutils.kdtree import KDTree
        self.kd = KDTree(len(self.P))
        for i, p in enumerate(self.P):
            self.kd.insert(Vector(p), i)
        self.kd.balance()
        self.delta = {n: self.new_head[i] - self.r1_head[i] for i, n in enumerate(self.names)}

    def at(self, pts, k=10, soft=0.004):
        out = np.zeros_like(pts)
        for i, p in enumerate(pts):
            res = self.kd.find_n(Vector(p), k)
            d = np.array([r[2] for r in res])
            w = 1.0 / (d + soft) ** 2
            idx = [r[1] for r in res]
            out[i] = (self.D[idx] * w[:, None]).sum(axis=0) / w.sum()
        return out


def _co(me):
    a = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', a)
    return a.reshape(-1, 3).astype(np.float64)


def _bone_offsets(obj, fld):
    """Per-vertex translation = sum of skin weight x bone delta (heads, eyes, hair shells); rigid parts use the head bone delta."""
    me = obj.data
    dh = fld.delta["rig:Head"]
    if not obj.vertex_groups:
        return np.tile(dh, (len(me.vertices), 1))
    gnames = {g.index: g.name for g in obj.vertex_groups}
    off = np.zeros((len(me.vertices), 3))
    wsum = np.zeros(len(me.vertices))
    for v in me.vertices:
        for g in v.groups:
            n = gnames[g.group]
            if n in fld.delta:
                off[v.index] += g.weight * fld.delta[n]
                wsum[v.index] += g.weight
    miss = wsum < 0.999
    off[miss] += (1 - wsum[miss])[:, None] * dh
    return off


def apply_field(fld, objs, arm):
    """objs: iterable of mesh objects (any LOD).  Skinned cloth / skin / pads / gear: displacement field.  Head-like and rigid head parts: bone-delta translation
    (all shape keys alike).  Then the armature takes the new rest pose."""
    for o in objs:
        me = o.data
        base = o.name.split("_LOD")[0]
        if base.startswith(HEAD_LIKE) or base in RIGID:
            off = _bone_offsets(o, fld)
            if me.shape_keys:
                for kb in me.shape_keys.key_blocks:
                    a = np.empty(len(kb.data) * 3, np.float32)
                    kb.data.foreach_get('co', a)
                    kb.data.foreach_set('co', (a.reshape(-1, 3) + off).astype(np.float32).ravel())
            me.vertices.foreach_set('co', (_co(me) + off).astype(np.float32).ravel())
        else:
            co = _co(me)
            me.vertices.foreach_set('co', (co + fld.at(co)).astype(np.float32).ravel())
        me.update()
    # new rest skeleton (positions, orientation) - bone names are identical
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    idx = {n: i for i, n in enumerate(fld.names)}
    for eb in arm.data.edit_bones:
        i = idx.get(eb.name)
        if i is None:
            continue
        eb.head = Vector(fld.new_head[i])
        eb.tail = Vector(fld.new_tail[i])
        eb.roll = float(fld.new_roll[i])
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()


if __name__ == "__main__":
    a = args()
    if a and a[0] == "build":
        for b in (list(BASES) if a[1] == "all" else [a[1]]):
            if BASES[b]:
                build_field(b)
