"""PHR facial hair / eyebrows / scalp hair: procedural alpha-tested shell textures in the head UV space + the shell meshes (subsets of the head cage).

Why shells: the Draft Fever head is one skin texture; painting beards into it would need one head texture per beard x skin tone x hair colour.  Instead the
head stays clean and each hair type is a thin (~1 mm) alpha-cutout shell that follows the head blend shapes (same vertices, same shape keys),
tinted by the hair colour at runtime (PIX/Lit alpha clip, _BaseColor tint).  Styles are different texture variants.

Coordinates: head local frame of the fitted head: x centre CX = -0.006, face looks to -Y, z up (see phr_build.fit_head).
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_tex import *
import bmesh

CX = -0.006
HAIR_OUT = os.path.join(OUT_ASSET, "Textures", "Hair")

BEARD_STYLES = ["stubble", "goatee", "full", "moustache"]
BROW_STYLES = ["thin", "normal", "thick"]
SCALP_STYLES = ["buzz", "short"]


def _sm(a, b, x):
    return smoothstep(a, b, x)


def fields(P, N):
    """Per-point (P[...,3] head-local position, N[...,3] normal) density fields in 0..1 for every style. Returns dict name -> array."""
    x = P[..., 0] - CX
    ax = np.abs(x)
    y = P[..., 1]
    z = P[..., 2]
    front = _sm(-0.05, -0.075, y)                                   # facing the camera side of the head
    f = {}
    # ---- eyebrows: arch above the eye, from x 0.011 to 0.06
    zb = 1.768 + 0.10 * ax - 0.9 * np.maximum(ax - 0.044, 0.0)
    arch = np.exp(-((z - zb) / 0.0042) ** 2)
    span = _sm(0.010, 0.016, ax) * (1 - _sm(0.050, 0.062, ax))
    f["brow_thin"] = arch * span * front * 0.95
    arch2 = np.exp(-((z - (zb + 0.0008)) / 0.0058) ** 2)
    f["brow_normal"] = arch2 * span * front
    arch3 = np.exp(-((z - (zb + 0.0015)) / 0.0078) ** 2)
    span3 = _sm(0.006, 0.012, ax) * (1 - _sm(0.054, 0.066, ax))
    f["brow_thick"] = arch3 * span3 * front
    # ---- facial hair
    zmax_c = 1.700 + 0.026 * _sm(0.030, 0.065, ax)                      # beard top edge: under the nose in the middle, up the cheeks at the sides
    below_cheek = (1 - _sm(zmax_c, zmax_c + 0.012, z)) * _sm(1.575, 1.62, z)   # lower face, down onto the upper neck
    lips = _sm(0.020, 0.012, ax) * np.exp(-((z - 1.694) / 0.0085) ** 2) * front
    nostril = np.exp(-(((ax) / 0.02) ** 2 + ((z - 1.712) / 0.006) ** 2)) * 0.0
    side = 1 - _sm(0.058, 0.09, ax) * (1 - _sm(1.745, 1.76, z))            # sideburns reach higher near the ears
    full_area = np.where(ax > 0.070, _sm(1.60, 1.64, z) * (1 - _sm(1.755, 1.775, z)), below_cheek)
    full_area = np.clip(full_area, 0, 1)
    cheek_edge = 1 - _sm(0.075, 0.095, ax) * 0.0
    f["beard_stubble_area"] = np.clip(below_cheek * (1 - lips), 0, 1) * (y < 0.05)
    f["beard_full_area"] = np.clip(full_area * (1 - lips * 0.85), 0, 1) * (y < 0.05)
    # goatee = chin patch + under lip + moustache
    chin = np.exp(-(((ax) / 0.030) ** 2 + ((z - 1.668) / 0.026) ** 2)) * front
    must = np.exp(-(((ax) / 0.034) ** 2 + ((z - 1.708) / 0.0085) ** 2)) * front * (1 - _sm(0.010, 0.004, ax) * 0.0)
    f["goatee_area"] = np.clip(chin + must * 1.0, 0, 1) * (1 - lips * 0.9)
    f["must_area"] = np.clip(must * 1.15, 0, 1) * (1 - lips * 0.9)
    # ---- scalp
    hair_line = np.where(y < -0.07, 1.800, np.interp(y, [-0.07, 0.03, 0.08], [1.800, 1.745, 1.70]))
    temple = _sm(0.082, 0.062, ax) * 1.0
    scalp_edge = _sm(0.0, 0.012, z - hair_line)
    ear = _sm(0.080, 0.088, ax) * (1 - _sm(1.755, 1.775, z)) * (y > -0.02)
    f["scalp"] = np.clip(scalp_edge * (1 - ear), 0, 1)
    f["scalp_buzz"] = f["scalp"]
    return f


def strands(size, seed, aniso, lo, hi, coverage, tone_lo=0.55):
    """Binary-ish strand cutout from anisotropic noise, coverage [H,W] 0..1 -> (alpha, tone)."""
    n = 0.6 * fft_noise(size, lo, hi, aniso, seed) + 0.4 * fft_noise(size, lo * 2, hi * 2, (1.0, 1.0), seed + 100)
    n = (n - n.min()) / (n.max() - n.min() + 1e-9)
    # rank-normalise so that coverage == density
    srt = np.sort(n.ravel())
    r = np.searchsorted(srt, n.ravel()).reshape(n.shape) / float(n.size)
    a = (r > (1.0 - np.clip(coverage, 0, 1))).astype(np.float64)
    a = np.clip(box_blur(a, 1) * 1.6 - 0.3, 0, 1)
    t = tone_lo + (1.0 - tone_lo) * (0.5 + 0.5 * fft_noise(size, 30, 400, (1, 1), seed + 5))
    return a, t


def bake_head_pos(head_obj, size):
    pos, nrm, cov = bake_world(head_obj, size)
    return pos, nrm, cov


def write_textures(head_obj, size=2048):
    pos, nrm, cov = bake_head_pos(head_obj, size)
    F = fields(pos, nrm)
    ensure(HAIR_OUT)

    def write(name, alpha, tone, srgb_tint=(1, 1, 1)):
        img = np.concatenate([np.stack([tone, tone, tone], -1), alpha[..., None]], -1)
        # edge padding so UV seams / mips do not open gaps (push-pull from the covered texels)
        img = pushpull(img, cov)
        save_rgba(f"{HAIR_OUT}/{name}.png", img)

    # beard styles
    a, t = strands(size, 11, (1.0, 0.30), 40, 420, F["beard_stubble_area"] * 0.55)
    write("PHR_Beard_stubble", a, t)
    a, t = strands(size, 12, (1.0, 0.22), 30, 380, np.clip(F["goatee_area"] * 0.88, 0, 1))
    write("PHR_Beard_goatee", a, t)
    a, t = strands(size, 13, (1.0, 0.18), 30, 360, np.clip(F["beard_full_area"] * 0.86, 0, 1))
    write("PHR_Beard_full", a, t)
    a, t = strands(size, 14, (1.0, 0.25), 40, 420, np.clip(F["must_area"] * 0.90, 0, 1))
    write("PHR_Beard_moustache", a, t)
    # eyebrows (1024 would do; kept 2048 for the same UV scale)
    for nm, key, seed in (("thin", "brow_thin", 21), ("normal", "brow_normal", 22), ("thick", "brow_thick", 23)):
        a, t = strands(size, seed, (0.35, 1.0), 40, 500, np.clip(F[key] * 1.05, 0, 1), 0.6)
        write(f"PHR_Brow_{nm}", a, t)
    # scalp
    a, t = strands(size, 31, (1.0, 1.0), 60, 520, np.clip(F["scalp"] * 0.74, 0, 1), 0.5)
    write("PHR_Scalp_buzz", a, t)
    a, t = strands(size, 32, (1.0, 1.0), 40, 420, np.clip(F["scalp"] * 0.88, 0, 1), 0.5)
    write("PHR_Scalp_short", a, t)
    print("wrote hair textures")
    return F


def save_rgba(path, arr):
    h, w = arr.shape[:2]
    im = bpy.data.images.new("_h_" + os.path.basename(path), w, h, alpha=True)
    im.colorspace_settings.name = 'sRGB'
    im.alpha_mode = 'STRAIGHT'
    im.pixels.foreach_set(np.ascontiguousarray(arr[::-1].astype(np.float32)).ravel())
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    bpy.data.images.remove(im)


# ---------------------------------------------------------------------------------------------- shell meshes
SHELLS = {
    # name: (field keys that define the region, vertex threshold, offset in metres)
    "Brows": (["brow_thick", "brow_normal"], 0.05, 0.0010),
    "Beard": (["beard_full_area", "goatee_area", "must_area"], 0.05, 0.0009),
    "Scalp": (["scalp"], 0.05, 0.0008),
}


def make_shell(cage, name, keys, thr, offset):
    """Subset of the cage head faces whose vertices have region density > thr (any key), inflated by `offset` along the normals, with the cage shape keys."""
    me = cage.data
    sk = me.shape_keys
    basis = np.array([v.co[:] for v in sk.key_blocks[0].data])
    nrm = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('normal', nrm)
    nrm = nrm.reshape(-1, 3)
    F = fields(basis, nrm)
    d = np.zeros(len(basis))
    for k in keys:
        d = np.maximum(d, F[k])
    keep_v = d > thr
    polys = [p for p in me.polygons if any(keep_v[v] for v in p.vertices)]
    used = sorted({v for p in polys for v in p.vertices})
    remap = {v: i for i, v in enumerate(used)}
    uvl = me.uv_layers.active
    # build the new mesh with bmesh from the cage to keep the UV layer
    bm = bmesh.new()
    bm.from_mesh(me)
    for k in list(bm.verts.layers.shape.keys()):
        bm.verts.layers.shape.remove(bm.verts.layers.shape[k])
    keep_faces = {p.index for p in polys}
    bm.faces.ensure_lookup_table()
    to_del = [f for f in bm.faces if f.index not in keep_faces]
    bmesh.ops.delete(bm, geom=to_del, context='FACES')
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context='VERTS')
    bm.verts.ensure_lookup_table()
    # remaining verts keep their order -> index mapping to the cage = sorted used list (bmesh keeps order)
    n = bpy.data.meshes.new(name)
    bm.to_mesh(n)
    bm.free()
    o = bpy.data.objects.new(name, n)
    bpy.context.scene.collection.objects.link(o)
    assert len(n.vertices) == len(used), (len(n.vertices), len(used))
    cur = np.array([basis[i] for i in used])
    nn = np.array([nrm[i] for i in used])
    base = cur + nn * offset
    n.vertices.foreach_set('co', base.astype(np.float32).ravel())
    o.shape_key_add(name="Basis").value = 0.0
    for kb in sk.key_blocks[1:]:
        arr = np.array([kb.data[i].co[:] for i in used])
        # shape key delta from the cage basis, applied to the inflated base
        k2 = o.shape_key_add(name=kb.name)
        k2.value = 0.0
        k2.data.foreach_set('co', (base + (arr - cur)).astype(np.float32).ravel())
    # vertex groups (weights) from the cage
    gn = {g.index: g.name for g in cage.vertex_groups}
    for g in cage.vertex_groups:
        o.vertex_groups.new(name=g.name)
    for i, v in enumerate(used):
        for ge in me.vertices[v].groups:
            o.vertex_groups[gn[ge.group]].add([i], ge.weight, 'REPLACE')
    return o
