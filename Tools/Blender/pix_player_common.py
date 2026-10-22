"""Shared helpers for the V6 player-art scripts (run inside Blender: blender -b --python ...).

Conventions (same as pix_common.py): metres, Z-up, character faces -Y (character's left = +X), feet on Z=0.
The Draft Fever player FBXs import with an armature scaled 0.01 / rotated 90 deg X and meshes rotated -90 deg X (cm data), so every
helper here works on *world-space* arrays (obj.matrix_world applied) which come out in metres / Z-up.
PNG helpers use numpy arrays with the origin at the TOP-left (like image viewers) so pixel rectangles in docs match.
"""
import bpy, os, sys
import numpy as np

BODY_DIR = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..",
                                         "Assets", "_Licensed", "DraftFever", "Characters", "Bodies"))
PROJECT_ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def script_args():
    """Arguments after the `--` separator."""
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_player(fbx):
    reset_scene()
    bpy.ops.import_scene.fbx(filepath=fbx)
    return {o.name: o for o in bpy.data.objects}


# ------------------------------------------------------------------ numpy views of a mesh
def world_arrays(obj):
    """(co[N,3] world metres, vertex_normals[N,3] world unit, uv[L,2] of the active UV layer (loop order),
    tri_loops[T,3], tri_verts[T,3], tri_poly[T])."""
    me = obj.data
    me.calc_loop_triangles()
    M = np.array(obj.matrix_world, dtype=np.float64)
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3).astype(np.float64) @ M[:3, :3].T + M[:3, 3]
    nr = np.empty(len(me.vertices) * 3, np.float32)
    me.vertex_normals.foreach_get('vector', nr)
    nr = nr.reshape(-1, 3).astype(np.float64) @ M[:3, :3].T
    nr /= np.maximum(np.linalg.norm(nr, axis=1, keepdims=True), 1e-9)
    uvl = me.uv_layers.active
    uv = np.empty(len(me.loops) * 2, np.float32)
    uvl.data.foreach_get('uv', uv)
    uv = uv.reshape(-1, 2).astype(np.float64)
    lt = me.loop_triangles
    tl = np.empty(len(lt) * 3, np.int32); lt.foreach_get('loops', tl)
    tv = np.empty(len(lt) * 3, np.int32); lt.foreach_get('vertices', tv)
    tp = np.empty(len(lt), np.int32); lt.foreach_get('polygon_index', tp)
    return co, nr, uv, tl.reshape(-1, 3), tv.reshape(-1, 3), tp


def uv_islands(obj):
    """Island id per polygon (faces joined when the shared edge has identical UVs at both ends)."""
    me = obj.data
    uvd = me.uv_layers.active.data
    uv = np.empty(len(me.loops) * 2, np.float32); uvd.foreach_get('uv', uv); uv = uv.reshape(-1, 2)
    lv = np.empty(len(me.loops), np.int32); me.loops.foreach_get('vertex_index', lv)
    le = np.empty(len(me.loops), np.int32); me.loops.foreach_get('edge_index', le)
    ls = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('loop_start', ls)
    lt = np.empty(len(me.polygons), np.int32); me.polygons.foreach_get('loop_total', lt)
    parent = list(range(len(me.polygons)))

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]; a = parent[a]
        return a
    owners = {}
    for p in range(len(me.polygons)):
        for li in range(ls[p], ls[p] + lt[p]):
            owners.setdefault(le[li], []).append((p, li))
    for e, lst in owners.items():
        if len(lst) != 2:
            continue
        (p1, l1), (p2, l2) = lst
        v = {}
        for p, li in ((p1, l1), (p2, l2)):
            # the loop of poly p that starts at each end vertex of the edge
            for lj in range(ls[p], ls[p] + lt[p]):
                if le[lj] == e:
                    v.setdefault(p, {})[lv[lj]] = uv[lj]
                    nxt = lj + 1 if lj + 1 < ls[p] + lt[p] else ls[p]
                    v[p][lv[nxt]] = uv[nxt]
        a, b = v[p1], v[p2]
        if all(np.abs(a[k] - b[k]).max() < 1e-5 for k in a if k in b) and len(set(a) & set(b)) == 2:
            parent[find(p1)] = find(p2)
    return np.array([find(i) for i in range(len(me.polygons))])


# ------------------------------------------------------------------ UV-space rasteriser
def rasterise(uv, tri_loops, tri_attr, size, eps=2e-3):
    """Rasterise UV triangles into a size x size buffer (origin TOP-left, v flipped) and barycentric-interpolate per-vertex
    attributes. tri_attr: float array [T, 3, C] (attribute per triangle corner). Returns (attr[size,size,C], covered[size,size] bool)."""
    C = tri_attr.shape[2]
    out = np.zeros((size, size, C), np.float64)
    cov = np.zeros((size, size), bool)
    P = uv[tri_loops].copy()
    P[:, :, 0] *= size
    P[:, :, 1] = (1.0 - P[:, :, 1]) * size
    for t in range(len(P)):
        a, b, c = P[t]
        x0 = int(max(np.floor(min(a[0], b[0], c[0])), 0)); x1 = int(min(np.ceil(max(a[0], b[0], c[0])), size - 1))
        y0 = int(max(np.floor(min(a[1], b[1], c[1])), 0)); y1 = int(min(np.ceil(max(a[1], b[1], c[1])), size - 1))
        if x1 < x0 or y1 < y0:
            continue
        d = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1])
        if abs(d) < 1e-12:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        w0 = ((b[1] - c[1]) * (xs - c[0]) + (c[0] - b[0]) * (ys - c[1])) / d
        w1 = ((c[1] - a[1]) * (xs - c[0]) + (a[0] - c[0]) * (ys - c[1])) / d
        w2 = 1.0 - w0 - w1
        m = (w0 >= -eps) & (w1 >= -eps) & (w2 >= -eps)
        if not m.any():
            continue
        at = tri_attr[t]
        val = w0[m][:, None] * at[0] + w1[m][:, None] * at[1] + w2[m][:, None] * at[2]
        out[y0:y1 + 1, x0:x1 + 1][m] = val
        cov[y0:y1 + 1, x0:x1 + 1][m] = True
    return out, cov


def dilate(img, cov, iterations):
    """Edge padding: repeatedly copy the average of covered 8-neighbours into uncovered pixels. img [H,W,C], cov [H,W] bool."""
    img = img.copy(); cov = cov.copy()
    for _ in range(iterations):
        acc = np.zeros_like(img); cnt = np.zeros(cov.shape, np.float64)
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                if dx == 0 and dy == 0:
                    continue
                sc = np.roll(np.roll(cov, dy, 0), dx, 1)
                si = np.roll(np.roll(img, dy, 0), dx, 1)
                acc += si * sc[..., None]; cnt += sc
        new = (~cov) & (cnt > 0)
        if not new.any():
            break
        img[new] = acc[new] / cnt[new][:, None]
        cov |= new
    return img, cov


# ------------------------------------------------------------------ PNG IO (numpy, origin top-left, floats 0..1)
def save_png(path, arr, color_space='Non-Color', bit_depth=8):
    """arr: [H,W,4] (or [H,W,3]) float 0..1, origin top-left. PNG, straight alpha, no colour transform when Non-Color.
    bit_depth 16 goes through save_render with the 'Raw' view transform so values are written untouched."""
    if arr.shape[2] == 3:
        arr = np.concatenate([arr, np.ones(arr.shape[:2] + (1,), arr.dtype)], axis=2)
    h, w = arr.shape[:2]
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    im = bpy.data.images.new("_out_" + os.path.basename(path), w, h, alpha=True, float_buffer=(bit_depth == 16))
    im.colorspace_settings.name = color_space
    im.alpha_mode = 'CHANNEL_PACKED'
    im.pixels.foreach_set(np.ascontiguousarray(np.flipud(np.clip(arr, 0, 1))).astype(np.float32).reshape(-1))
    if bit_depth == 16:
        sc = bpy.context.scene
        s = sc.render.image_settings
        s.media_type = 'IMAGE'; s.file_format = 'PNG'; s.color_depth = '16'; s.color_mode = 'RGBA'
        sc.view_settings.view_transform = 'Raw'
        im.save_render(os.path.abspath(path), scene=sc)
    else:
        im.filepath_raw = os.path.abspath(path)
        im.file_format = 'PNG'
        im.save()
    bpy.data.images.remove(im)


def load_png(path, color_space='Non-Color'):
    """Returns (float32 array [H,W,4] origin top-left with the raw file values (no colour transform when Non-Color), number of channels of the file)."""
    im = bpy.data.images.load(os.path.abspath(path), check_existing=False)
    im.colorspace_settings.name = color_space
    im.alpha_mode = 'CHANNEL_PACKED'
    w, h = im.size
    a = np.empty(w * h * 4, np.float32)
    im.pixels.foreach_get(a)
    nch = im.channels
    bpy.data.images.remove(im)
    return np.flipud(a.reshape(h, w, 4)), nch
