# pres_stadiumkit.py - build the PIX StadiumKit modules from OlegWER "Modular American Football Stadium".
# Run:  blender -b --python pres_stadiumkit.py -- <stage> [args]
# Stages (re-runnable, intermediate files in D:\PIXFootball\Work\stadiumkit):
#   scan    : stream-parse the 3 GB FBX (no full import), dump node table (name,geom,bbox,mats,world xform) -> nodes.tsv + scan.pkl
#   mats    : parse Unity .mat files -> mats.json (material name -> textures/colours)
#   survey <list> <png> [cols] / view <list> <png> mode : contact sheets / world views of FBX nodes (read straight from the 3 GB FBX, no full import)
#   build_all : roof (straight+arc90), light tower, ribbon board, media box, tunnel mouth, scoreboard frame -> StadiumKit*.fbx + textures
#   previews [filter], roofring, texsheet, validate : QA renders (re-import of the exported FBX), assembly check, texture contact sheet, stats
# Full rebuild: scan, mats, build_all, previews, texsheet, validate (scan/mats only needed once; ~15 s each)
import sys, os, struct, zlib, pickle, time, json, re, math

WORK = r"D:\PIXFootball\Work\stadiumkit"
PKG = r"D:\PIXFootball\Source\PPF_NEW\stadium_pkg"
FBX = os.path.join(PKG, "b634ed166ce474f43a834daa050c01df", "asset")
os.makedirs(WORK, exist_ok=True)

# ----------------------------------------------------------------------------------------------
# lazy binary FBX reader (v7400, 32-bit offsets). Arrays are NOT read, only their file offset.
# ----------------------------------------------------------------------------------------------
class Arr:
    __slots__ = ("t", "n", "enc", "clen", "off")
    def __init__(self, t, n, enc, clen, off):
        self.t, self.n, self.enc, self.clen, self.off = t, n, enc, clen, off
    def load(self, f):
        import numpy as np
        f.seek(self.off)
        raw = f.read(self.clen if self.enc else self.n * ELSZ[self.t])
        if self.enc:
            raw = zlib.decompress(raw)
        return np.frombuffer(raw, dtype=NPT[self.t])

ELSZ = {"f": 4, "d": 8, "l": 8, "i": 4, "b": 1}
NPT = {"f": "<f4", "d": "<f8", "l": "<i8", "i": "<i4", "b": "<u1"}

def read_props(f, nprops):
    out = []
    for _ in range(nprops):
        t = f.read(1).decode("latin1")
        if t == "Y": out.append(struct.unpack("<h", f.read(2))[0])
        elif t == "C": out.append(f.read(1)[0])
        elif t == "I": out.append(struct.unpack("<i", f.read(4))[0])
        elif t == "F": out.append(struct.unpack("<f", f.read(4))[0])
        elif t == "D": out.append(struct.unpack("<d", f.read(8))[0])
        elif t == "L": out.append(struct.unpack("<q", f.read(8))[0])
        elif t in "fdlib":
            n, enc, clen = struct.unpack("<III", f.read(12))
            off = f.tell()
            out.append(Arr(t, n, enc, clen, off))
            f.seek(off + (clen if enc else n * ELSZ[t]))
        elif t == "S":
            n = struct.unpack("<I", f.read(4))[0]
            out.append(f.read(n).decode("utf-8", "replace"))
        elif t == "R":
            n = struct.unpack("<I", f.read(4))[0]
            out.append(f.read(n))
        else:
            raise ValueError("bad prop type %r at %d" % (t, f.tell()))
    return out

def read_node(f):
    """returns (name, props, children) or None for null record"""
    hdr = f.read(13)
    end, nprops, plen, nlen = struct.unpack("<IIIB", hdr)
    if end == 0:
        return None
    name = f.read(nlen).decode("latin1")
    props = read_props(f, nprops)
    children = []
    if f.tell() < end:
        while f.tell() < end - 13:
            c = read_node(f)
            if c is None:
                break
            children.append(c)
        f.seek(end)
    return (name, props, children)

def read_fbx(path):
    f = open(path, "rb")
    assert f.read(20) == b"Kaydara FBX Binary  "; f.read(1)
    f.read(2)
    ver = struct.unpack("<I", f.read(4))[0]
    assert ver < 7500, ver
    top = []
    size = os.path.getsize(path)
    while f.tell() < size - 200:
        n = read_node(f)
        if n is None:
            break
        top.append(n)
    return f, top

def child(node, name):
    for c in node[2]:
        if c[0] == name:
            return c
    return None

# ----------------------------------------------------------------------------------------------
def euler_xyz_matrix(rx, ry, rz):
    import numpy as np
    rx, ry, rz = map(math.radians, (rx, ry, rz))
    cx, sx, cy, sy, cz, sz = math.cos(rx), math.sin(rx), math.cos(ry), math.sin(ry), math.cos(rz), math.sin(rz)
    Rx = np.array([[1, 0, 0], [0, cx, -sx], [0, sx, cx]])
    Ry = np.array([[cy, 0, sy], [0, 1, 0], [-sy, 0, cy]])
    Rz = np.array([[cz, -sz, 0], [sz, cz, 0], [0, 0, 1]])
    return Rz @ Ry @ Rx   # FBX default eEulerXYZ: R = Rz*Ry*Rx

def model_local(model):
    import numpy as np
    p70 = child(model, "Properties70")
    d = {"Lcl Translation": (0, 0, 0), "Lcl Rotation": (0, 0, 0), "Lcl Scaling": (1, 1, 1),
         "PreRotation": (0, 0, 0), "PostRotation": (0, 0, 0)}
    other = {}
    if p70:
        for p in p70[2]:
            pr = p[1]
            if pr and pr[0] in d and len(pr) >= 7:
                d[pr[0]] = tuple(pr[4:7])
            elif pr and pr[0] in ("RotationOffset", "RotationPivot", "ScalingOffset", "ScalingPivot", "RotationOrder", "InheritType") and len(pr) >= 5:
                other[pr[0]] = tuple(pr[4:])
    M = np.eye(4)
    R = euler_xyz_matrix(*d["Lcl Rotation"])
    Rpre = euler_xyz_matrix(*d["PreRotation"])
    Rpost = euler_xyz_matrix(*d["PostRotation"])
    S = np.diag(d["Lcl Scaling"])
    L = Rpre @ R @ np.linalg.inv(Rpost) @ S
    M[:3, :3] = L
    M[:3, 3] = d["Lcl Translation"]
    return M, other

def stage_scan():
    import numpy as np
    t0 = time.time()
    f, top = read_fbx(FBX)
    print("top nodes", [t[0] for t in top], "%.1fs" % (time.time() - t0))
    objs = child_of(top, "Objects")
    conns = child_of(top, "Connections")
    geoms, models, mats = {}, {}, {}
    for n in objs[2]:
        if n[0] == "Geometry":
            geoms[n[1][0]] = n
        elif n[0] == "Model":
            models[n[1][0]] = n
        elif n[0] == "Material":
            mats[n[1][0]] = n
    print("geoms", len(geoms), "models", len(models), "mats", len(mats))
    parent, mgeom, mmats = {}, {}, {}
    for c in conns[2]:
        if c[0] != "C" or c[1][0] != "OO":
            continue
        a, b = c[1][1], c[1][2]   # a child -> b parent
        if a in models and b in models:
            parent[a] = b
        elif a in geoms and b in models:
            mgeom[b] = a
        elif a in mats and b in models:
            mmats.setdefault(b, []).append(a)
    # geometry stats
    gstat = {}
    t0 = time.time()
    for i, (gid, g) in enumerate(geoms.items()):
        v = child(g, "Vertices")[1][0].load(f).reshape(-1, 3)
        pi = child(g, "PolygonVertexIndex")[1][0].load(f)
        npoly = int((pi < 0).sum())
        ntri = int(np.sum(np.diff(np.concatenate([[-1], np.nonzero(pi < 0)[0]])) - 2))
        gstat[gid] = dict(nv=len(v), npoly=npoly, ntri=ntri, bmin=v.min(0), bmax=v.max(0))
        if i % 100 == 0:
            print(i, "%.0fs" % (time.time() - t0), flush=True)
    # world transforms
    local = {mid: model_local(m) for mid, m in models.items()}
    world = {}
    def getw(mid):
        if mid in world:
            return world[mid]
        M = local[mid][0]
        if mid in parent:
            M = getw(parent[mid]) @ M
        world[mid] = M
        return M
    rows = []
    for mid, m in models.items():
        W = getw(mid)
        name = m[1][1].split("\x00")[0]
        gid = mgeom.get(mid)
        row = dict(id=mid, name=name, parent=(models[parent[mid]][1][1].split("\x00")[0] if mid in parent else ""),
                   geom=gid, mats=[mats[x][1][1].split("\x00")[0] for x in mmats.get(mid, [])], world=W)
        if gid:
            gs = gstat[gid]
            lo, hi = gs["bmin"], gs["bmax"]
            corners = np.array([[x, y, z, 1] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
            wc = (W @ corners.T).T[:, :3]
            row.update(nv=gs["nv"], ntri=gs["ntri"], wmin=wc.min(0), wmax=wc.max(0))
        rows.append(row)
    with open(os.path.join(WORK, "scan.pkl"), "wb") as fh:
        pickle.dump(dict(rows=rows, gstat=gstat), fh)
    with open(os.path.join(WORK, "nodes.tsv"), "w") as fh:
        fh.write("name\tgeom\tnv\tntri\twmin\twmax\tsize\tmats\tparent\n")
        for r in rows:
            if "wmin" in r:
                sz = r["wmax"] - r["wmin"]
                fh.write("%s\t%s\t%d\t%d\t%s\t%s\t%s\t%s\t%s\n" % (
                    r["name"], r["geom"], r["nv"], r["ntri"],
                    ",".join("%.1f" % x for x in r["wmin"]), ",".join("%.1f" % x for x in r["wmax"]),
                    ",".join("%.1f" % x for x in sz), "|".join(r["mats"]), r["parent"]))
            else:
                fh.write("%s\t-\t0\t0\t\t\t\t\t%s\n" % (r["name"], r["parent"]))
    print("scan done")

def child_of(top, name):
    for t in top:
        if t[0] == name:
            return t

def load_index():
    idx = {}
    with open(r"D:\PIXFootball\Source\PPF_NEW\stadium_index.tsv", encoding="utf-8") as fh:
        for line in fh:
            p = line.rstrip("\n").split("\t")
            if len(p) >= 2:
                idx[p[0]] = p[1]
    return idx

def stage_mats():
    idx = load_index()
    out = {}
    for g, path in idx.items():
        if not path.endswith(".mat"):
            continue
        txt = open(os.path.join(PKG, g, "asset"), encoding="utf-8", errors="replace").read()
        name = re.search(r"m_Name: (.*)", txt).group(1).strip()
        tex = {}
        for m in re.finditer(r"- (_\w+):\n\s+m_Texture: \{fileID: (\d+)(?:, guid: (\w+))?", txt):
            if m.group(3):
                tex[m.group(1)] = os.path.basename(idx.get(m.group(3), "?" + m.group(3)))
        col = re.search(r"- _Color: \{r: ([\d.e-]+), g: ([\d.e-]+), b: ([\d.e-]+), a: ([\d.e-]+)", txt)
        ecol = re.search(r"- _EmissionColor: \{r: ([\d.e-]+), g: ([\d.e-]+), b: ([\d.e-]+)", txt)
        mode = re.search(r"- _Mode: ([\d.]+)", txt)
        out[name] = dict(guid=g, tex=tex, color=[float(x) for x in col.groups()] if col else None,
                         emis=[float(x) for x in ecol.groups()] if ecol else None, mode=float(mode.group(1)) if mode else 0)
    # texture list (basename -> guid path)
    texdir = {}
    for g, path in idx.items():
        if "/Textures/" in path:
            texdir[os.path.basename(path)] = os.path.join(PKG, g, "asset")
    json.dump(dict(mats=out, tex=texdir), open(os.path.join(WORK, "mats.json"), "w"), indent=0)
    print(len(out), "mats", len(texdir), "textures")

# ----------------------------------------------------------------------------------------------
# ----------------------------------------------------------------------------------------------
# geometry loading into Blender (world transform baked, cm/Y-up -> m/Z-up)
# ----------------------------------------------------------------------------------------------
_CTX = {}

def ctx():
    if not _CTX:
        f, top = read_fbx(FBX)
        objs = child_of(top, "Objects")
        geoms = {n[1][0]: n for n in objs[2] if n[0] == "Geometry"}
        d = pickle.load(open(os.path.join(WORK, "scan.pkl"), "rb"))
        rows = {r["name"]: r for r in d["rows"]}
        mj = json.load(open(os.path.join(WORK, "mats.json")))
        _CTX.update(f=f, geoms=geoms, rows=rows, mats=mj["mats"], tex=mj["tex"], gstat=d["gstat"])
    return _CTX

def _C3():
    import numpy as np
    return np.array([[1, 0, 0], [0, 0, -1], [0, 1, 0]], dtype=np.float64) * 0.01

def geom_arrays(gid):
    """dict(v (n,3 cm Y-up), loops, ls, lt, uv (nl,2) or None, matidx (npoly,))"""
    import numpy as np
    c = ctx(); f = c["f"]; g = c["geoms"][gid]
    v = child(g, "Vertices")[1][0].load(f).reshape(-1, 3)
    pi = child(g, "PolygonVertexIndex")[1][0].load(f).astype(np.int64)
    ends = np.nonzero(pi < 0)[0]
    loops = np.where(pi < 0, ~pi, pi)
    starts = np.concatenate([[0], ends[:-1] + 1])
    totals = ends - starts + 1
    uv = None
    for ch in g[2]:
        if ch[0] == "LayerElementUV":
            uvd = child(ch, "UV")[1][0].load(f).reshape(-1, 2)
            mp = child(ch, "MappingInformationType")[1][0]
            uidx = child(ch, "UVIndex")
            if uidx is not None:
                ui = uidx[1][0].load(f)
                uv = uvd[ui] if mp == "ByPolygonVertex" else uvd[ui][loops]
            elif mp == "ByPolygonVertex":
                uv = uvd
            else:
                uv = uvd[loops]
            break
    matidx = np.zeros(len(starts), dtype=np.int32)
    for ch in g[2]:
        if ch[0] == "LayerElementMaterial":
            mp = child(ch, "MappingInformationType")[1][0]
            ma = child(ch, "Materials")[1][0].load(f)
            if mp == "ByPolygon" and len(ma) == len(starts):
                matidx = ma.astype(np.int32)
            elif len(ma):
                matidx[:] = ma[0]
            break
    return dict(v=v, loops=loops, ls=starts, lt=totals, uv=uv, matidx=matidx)

def get_bl_material(name):
    import bpy
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    col = (ctx()["mats"].get(name) or {}).get("color") or [0.6, 0.6, 0.6, 1]
    m.diffuse_color = (col[0], col[1], col[2], 1.0)
    return m

def make_object(nodename, collection=None, with_mats=True):
    """Blender object from FBX node; vertices in world metres, Blender axes (Z-up)."""
    import bpy, numpy as np
    c = ctx(); r = c["rows"][nodename]
    a = geom_arrays(r["geom"])
    W = r["world"]
    v = a["v"] @ W[:3, :3].T + W[:3, 3]
    v = v @ _C3().T
    flip = np.linalg.det(W[:3, :3]) < 0
    loops, ls, lt = a["loops"], a["ls"], a["lt"]
    if flip:   # mirrored instance: reverse winding per polygon
        newidx = np.empty(len(loops), dtype=np.int64)
        for s, t in zip(ls, lt):
            newidx[s:s + t] = np.arange(s + t - 1, s - 1, -1)
        loops = loops[newidx]
        if a["uv"] is not None:
            a["uv"] = a["uv"][newidx]
    me = bpy.data.meshes.new(nodename)
    me.vertices.add(len(v))
    me.vertices.foreach_set("co", v.astype(np.float32).ravel())
    me.loops.add(len(loops))
    me.loops.foreach_set("vertex_index", loops.astype(np.int32))
    me.polygons.add(len(ls))
    me.polygons.foreach_set("loop_start", ls.astype(np.int32))
    me.update(calc_edges=True)
    if a["uv"] is not None:
        uvl = me.uv_layers.new(name="UVMap")
        uvl.data.foreach_set("uv", a["uv"].astype(np.float32).ravel())
    if with_mats:
        for mn in r["mats"]:
            me.materials.append(get_bl_material(mn))
        me.polygons.foreach_set("material_index", np.clip(a["matidx"], 0, max(len(r["mats"]) - 1, 0)).astype(np.int32))
    me.validate()
    ob = bpy.data.objects.new(nodename, me)
    (collection or bpy.context.scene.collection).objects.link(ob)
    return ob

def reset_scene():
    import bpy
    bpy.ops.wm.read_factory_settings(use_empty=True)

def setup_workbench(res=(1600, 1000)):
    import bpy
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.film_transparent = False
    sh = sc.display.shading
    sh.light = "STUDIO"; sh.color_type = "MATERIAL"; sh.show_cavity = False
    sh.background_type = "VIEWPORT"; sh.background_color = (0.45, 0.5, 0.58)
    sc.view_settings.view_transform = "Standard"
    sc.render.image_settings.file_format = "PNG"

def add_camera(loc, target, ortho=None, lens=35):
    import bpy, mathutils
    cam = bpy.data.cameras.new("cam")
    if ortho:
        cam.type = "ORTHO"; cam.ortho_scale = ortho
    else:
        cam.lens = lens
    cam.clip_start = 0.05; cam.clip_end = 5000
    co = bpy.data.objects.new("cam", cam)
    bpy.context.scene.collection.objects.link(co)
    co.location = loc
    d = mathutils.Vector(target) - mathutils.Vector(loc)
    co.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.camera = co
    return co

def add_label(text, loc, size=0.35):
    import bpy
    cu = bpy.data.curves.new("t", "FONT")
    cu.body = text; cu.size = size; cu.align_x = "CENTER"
    ob = bpy.data.objects.new("t", cu)
    bpy.context.scene.collection.objects.link(ob)
    ob.location = loc
    ob.rotation_euler = (math.radians(90), 0, 0)
    m = bpy.data.materials.get("_lab") or bpy.data.materials.new("_lab")
    m.diffuse_color = (1, 1, 1, 1)
    ob.data.materials.append(m)
    return ob

def stage_survey(listfile, outpng, cols="6"):
    """contact sheet of FBX nodes listed (one name per line), each normalised to a 3 m cell, labelled"""
    import bpy, mathutils
    reset_scene(); setup_workbench((2400, 1800))
    names = [l.strip() for l in open(listfile) if l.strip() and ctx()["rows"][l.strip()].get("geom")]
    cols = int(cols)
    rows_n = (len(names) + cols - 1) // cols
    cell = 4.0
    for i, n in enumerate(names):
        ob = make_object(n)
        bb = [mathutils.Vector(c) for c in ob.bound_box]
        mn = [min(b[k] for b in bb) for k in range(3)]; mx = [max(b[k] for b in bb) for k in range(3)]
        ext = max(mx[k] - mn[k] for k in range(3))
        s = 3.0 / max(ext, 1e-6)
        cx, cy = (mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2
        col, row = i % cols, i // cols
        ob.scale = (s, s, s)
        ob.location = (col * cell - cx * s, -cy * s, -(row * cell) - mn[2] * s - 1.5)
        add_label(n.replace("SM_asset_", "") + " %.1fm" % ext, (col * cell, 0, -(row * cell) - 2.0), 0.3)
    W = cols * cell; H = rows_n * cell
    cxm = W / 2 - cell / 2; czm = -(H / 2) + cell / 2
    add_camera((cxm, -80, czm), (cxm, 0, czm), ortho=W * 1.02)
    sc = bpy.context.scene
    sc.render.resolution_x = 2400
    sc.render.resolution_y = int(2400 * H / W)
    sc.render.filepath = outpng
    bpy.ops.render.render(write_still=True)
    print("survey ->", outpng)

def fit_scene_bbox(objs):
    import mathutils
    mn = [1e9] * 3; mx = [-1e9] * 3
    for ob in objs:
        for c in ob.bound_box:
            w = ob.matrix_world @ mathutils.Vector(c)
            for k in range(3):
                mn[k] = min(mn[k], w[k]); mx[k] = max(mx[k], w[k])
    return mn, mx

def stage_view(listfile, outpng, mode="persp", res="1800x1100"):
    """render FBX nodes (names in listfile) in world position: mode top|side|front|persp"""
    import bpy, mathutils
    reset_scene(); rx, ry = map(int, res.split("x")); setup_workbench((rx, ry))
    names = [l.strip() for l in open(listfile) if l.strip() and ctx()["rows"][l.strip()].get("geom")]
    obs = [make_object(n) for n in names]
    mn, mx = fit_scene_bbox(obs)
    cx, cy, cz = [(mn[k] + mx[k]) / 2 for k in range(3)]
    ext = [mx[k] - mn[k] for k in range(3)]
    R = max(ext)
    if mode == "top":
        add_camera((cx, cy, mx[2] + R * 2), (cx, cy, cz), ortho=max(ext[0], ext[1] * rx / ry) * 1.05)
    elif mode == "side":
        add_camera((cx, mn[1] - R * 2, cz), (cx, cy, cz), ortho=max(ext[0], ext[2] * rx / ry) * 1.05)
    elif mode == "front":
        add_camera((mn[0] - R * 2, cy, cz), (cx, cy, cz), ortho=max(ext[1], ext[2] * rx / ry) * 1.05)
    else:
        add_camera((cx + R * 0.9, cy - R * 1.3, cz + R * 0.8), (cx, cy, cz), lens=35)
    sc = bpy.context.scene
    sc.render.filepath = outpng
    bpy.ops.render.render(write_still=True)
    print("view ->", outpng, "bbox", mn, mx)

# ==============================================================================================
# KIT BUILDING: common helpers
# ==============================================================================================
OUT = r"C:\Users\peyto\Desktop\PeytonParrishGames\PIX PRO FOOTBALL\Assets\_Licensed\PIX\StadiumKit"
RENDERS = r"D:\PIXFootball\Renders\v070_d"
TEXW = os.path.join(WORK, "tex")           # textures are authored here then copied to OUT
os.makedirs(TEXW, exist_ok=True)

def write_png(path, arr):
    """arr: HxWxC uint8, row 0 = TOP of image. C in (3,4)."""
    import numpy as np
    h, w, c = arr.shape
    raw = np.concatenate([np.zeros((h, 1), np.uint8), arr.reshape(h, w * c)], axis=1).tobytes()
    def chunk(t, d):
        cc = struct.pack(">I", len(d)) + t + d
        return cc + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2 if c == 3 else 6, 0, 0, 0)) \
        + chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, "wb").write(png)

def tile_noise(h, w, beta=2.0, seed=0, aniso=(1.0, 1.0)):
    """periodic (tileable) noise in [0,1] via FFT filtering; aniso scales the frequency axes"""
    import numpy as np
    rng = np.random.default_rng(seed)
    f = rng.standard_normal((h, w))
    F = np.fft.fft2(f)
    fy = np.fft.fftfreq(h)[:, None] * h * aniso[0]
    fx = np.fft.fftfreq(w)[None, :] * w * aniso[1]
    r = np.sqrt(fx ** 2 + fy ** 2); r[0, 0] = 1
    F = F / (r ** (beta / 2.0)); F[0, 0] = 0
    n = np.real(np.fft.ifft2(F))
    n = (n - n.min()) / (n.max() - n.min() + 1e-9)
    return n

class Atlas:
    """texture atlas, v-up arrays [row=v, col=u]; regions are UV rectangles (u0,v0,u1,v1)"""
    def __init__(self, size=1024):
        import numpy as np
        self.S = size
        self.base = np.full((size, size, 3), 0.5, np.float32)
        self.height = np.zeros((size, size), np.float32)
        self.metal = np.zeros((size, size), np.float32)
        self.smooth = np.full((size, size), 0.5, np.float32)
        self.rects = {}
    def region(self, name, u0, v0, u1, v1):
        self.rects[name] = (u0, v0, u1, v1)
        S = self.S
        return slice(int(round(v0 * S)), int(round(v1 * S))), slice(int(round(u0 * S)), int(round(u1 * S)))
    def paint(self, name, base, height=None, metal=0.0, smooth=0.5):
        import numpy as np
        sl = self.region(name, *self.rects[name]) if name in self.rects else None
        h = sl[0].stop - sl[0].start; w = sl[1].stop - sl[1].start
        b = np.asarray(base, np.float32)
        self.base[sl] = np.broadcast_to(b, (h, w, 3)) if b.ndim <= 1 else b
        self.height[sl] = 0 if height is None else height
        self.metal[sl] = metal
        self.smooth[sl] = smooth
        return sl
    def size_of(self, name):
        u0, v0, u1, v1 = self.rects[name]
        return int(round((v1 - v0) * self.S)), int(round((u1 - u0) * self.S))
    def save(self, prefix, normal_strength=2.0, outdir=None):
        import numpy as np
        outdir = outdir or TEXW
        S = self.S
        H = self.height
        dx = (np.roll(H, -1, 1) - np.roll(H, 1, 1)) * 0.5 * S * normal_strength / 100.0
        dy = (np.roll(H, -1, 0) - np.roll(H, 1, 0)) * 0.5 * S * normal_strength / 100.0
        n = np.stack([-dx, -dy, np.ones_like(dx)], -1)
        n /= np.linalg.norm(n, axis=-1, keepdims=True)
        nrm = ((n * 0.5 + 0.5) * 255 + 0.5).astype(np.uint8)
        base = (np.clip(self.base, 0, 1) * 255 + 0.5).astype(np.uint8)
        mask = np.stack([self.metal, np.ones_like(self.metal), np.zeros_like(self.metal), self.smooth], -1)
        mask = (np.clip(mask, 0, 1) * 255 + 0.5).astype(np.uint8)
        paths = {}
        for tag, arr in (("BaseColor", base), ("Normal", nrm), ("Mask", mask)):
            p = os.path.join(outdir, "%s_%s.png" % (prefix, tag))
            write_png(p, arr[::-1].copy())     # flip: file row0 = top = v 1
            paths[tag] = p
        return paths
    def uvmap(self, name, u, v, inset=2):
        """local (u,v) in 0..1 -> atlas uv inside region, inset by `inset` px"""
        u0, v0, u1, v1 = self.rects[name]
        e = inset / self.S
        return (u0 + e + u * (u1 - u0 - 2 * e), v0 + e + v * (v1 - v0 - 2 * e))

# ---- materials -------------------------------------------------------------------------------
def make_material(name, color=(0.5, 0.5, 0.5, 1.0), emission=None, emission_strength=1.0, alpha=1.0, metallic=0.0, roughness=0.5):
    import bpy
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Alpha"].default_value = alpha
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (emission[0], emission[1], emission[2], 1.0)
        bsdf.inputs["Emission Strength"].default_value = emission_strength
    m.diffuse_color = color
    return m

# ---- mesh builder ----------------------------------------------------------------------------
class Builder:
    """accumulates parts into one bmesh. each part: material slot + atlas region + UV mapping rule"""
    def __init__(self, name, mats, atlas=None):
        import bmesh
        self.name = name; self.bm = bmesh.new(); self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.mats = list(mats); self.atlas = atlas
    def mi(self, matname):
        return self.mats.index(matname)

    def _faces(self, verts):
        fs = set()
        for v in verts:
            for f in v.link_faces:
                fs.add(f)
        return list(fs)

    def _uv_box(self, faces, region, spin=False):
        import numpy as np
        pts = np.array([[*l.vert.co] for f in faces for l in f.loops])
        lo, hi = pts.min(0), pts.max(0)
        ext = np.maximum(hi - lo, 1e-6)
        for f in faces:
            f.normal_update()
            n = f.normal
            ax = max(range(3), key=lambda k: abs(n[k]))
            a, b = [k for k in range(3) if k != ax]
            if abs(n[a]) > 0.7 and False:
                pass
            for l in f.loops:
                u = (l.vert.co[a] - lo[a]) / ext[a]; v = (l.vert.co[b] - lo[b]) / ext[b]
                if spin: u, v = v, u
                l[self.uv].uv = self.atlas.uvmap(region, u, v) if self.atlas else (u, v)

    def _uv_cyl(self, faces, region, axis=2):
        import numpy as np, math
        pts = np.array([[*l.vert.co] for f in faces for l in f.loops])
        lo, hi = pts.min(0), pts.max(0); c = (lo + hi) / 2
        ext = max(hi[axis] - lo[axis], 1e-6)
        a, b = [k for k in range(3) if k != axis]
        for f in faces:
            f.normal_update()
            if abs(f.normal[axis]) > 0.7:    # caps: planar
                self._uv_box([f], region)
                continue
            ang = [math.atan2(l.vert.co[b] - c[b], l.vert.co[a] - c[a]) for l in f.loops]
            ref = ang[0]
            ang = [((x - ref + math.pi) % (2 * math.pi)) - math.pi for x in ang]
            amin, amax = min(ang), max(ang)
            w = max(amax - amin, 1e-6)
            for l, x in zip(f.loops, ang):
                u = (x - amin) / w
                v = (l.vert.co[axis] - lo[axis]) / ext
                l[self.uv].uv = self.atlas.uvmap(region, u, v) if self.atlas else (u, v)

    def finish(self, faces, mat, region=None, mode="box", smooth=False, axis=2, spin=False):
        for f in faces:
            f.material_index = self.mi(mat)
            f.smooth = smooth
        if region and self.atlas:
            if mode == "box": self._uv_box(faces, region, spin)
            else: self._uv_cyl(faces, region, axis)

    def box(self, center, size, mat, region=None, rot=None):
        """axis aligned (or rotated by 3x3 `rot`) box; size = full dims"""
        import bmesh, mathutils
        M = mathutils.Matrix.Translation(center)
        if rot is not None:
            R = mathutils.Matrix.Identity(4)
            for i in range(3):
                for j in range(3):
                    R[i][j] = rot[i][j]
            M = M @ R
        M = M @ mathutils.Matrix.Diagonal((size[0], size[1], size[2], 1.0))
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=M)
        fs = self._faces(r["verts"])
        self.finish(fs, mat, region, "box")
        return fs

    def frustum(self, center, r1, r2, depth, segs, mat, region=None, caps=True, rot=None, smooth=True, axis=2):
        """z-aligned cone/cylinder frustum; r1 = bottom radius, r2 = top radius"""
        import bmesh, mathutils
        M = mathutils.Matrix.Translation(center)
        if rot is not None:
            R = mathutils.Matrix.Identity(4)
            for i in range(3):
                for j in range(3):
                    R[i][j] = rot[i][j]
            M = M @ R
        r = bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=segs, radius1=r1, radius2=r2, depth=depth, matrix=M)
        fs = self._faces(r["verts"])
        self.finish(fs, mat, region, "cyl", smooth=smooth, axis=2)
        return fs

    def beam(self, p0, p1, w, d, mat, region=None, up=(0, 0, 1)):
        """box between 3d points p0->p1 with cross-section w (across) x d (depth)"""
        import mathutils
        a = mathutils.Vector(p0); b = mathutils.Vector(p1)
        L = (b - a).length
        y = (b - a).normalized()
        u = mathutils.Vector(up)
        if abs(y.dot(u)) > 0.98: u = mathutils.Vector((1, 0, 0))
        x = y.cross(u).normalized(); z = x.cross(y).normalized()
        rot = [[x[0], y[0], z[0]], [x[1], y[1], z[1]], [x[2], y[2], z[2]]]
        return self.box((a + b) / 2, (w, L, d), mat, region, rot=rot)

    def quad(self, pts, mat, region=None, uvs=None, smooth=False, normal=None):
        bm = self.bm
        vs = [bm.verts.new(p) for p in pts]
        f = bm.faces.new(vs)
        if normal is not None:
            self.locked = getattr(self, "locked", []) + [(f, normal)]
        f.material_index = self.mi(mat); f.smooth = smooth
        if uvs:
            for l, uv in zip(f.loops, uvs):
                l[self.uv].uv = uv
        elif region and self.atlas:
            self._uv_box([f], region)
        return f

    def to_object(self, collection=None):
        import bpy, bmesh
        bm = self.bm
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        for f, n in getattr(self, "locked", []):
            if f.is_valid:
                f.normal_update()
                if f.normal.dot(n) < 0: f.normal_flip()
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        for mn in self.mats:
            me.materials.append(bpy.data.materials[mn])
        ob = bpy.data.objects.new(self.name, me)
        (collection or bpy.context.scene.collection).objects.link(ob)
        return ob

def _lathe(self, origin, axis, profile, segs, mat, region=None, smooth=True, cap_back=True, cap_front=None):
    """revolve profile [(axial d, radius r), ...] around `axis` starting at origin; returns faces"""
    import mathutils
    bm = self.bm
    o = mathutils.Vector(origin); d = mathutils.Vector(axis).normalized()
    t = mathutils.Vector((0, 0, 1)) if abs(d.z) < 0.9 else mathutils.Vector((1, 0, 0))
    a = d.cross(t).normalized(); b = d.cross(a).normalized()
    rings = []
    for (ax, r) in profile:
        ring = []
        for i in range(segs):
            ang = 2 * math.pi * i / segs
            ring.append(bm.verts.new(o + d * ax + (a * math.cos(ang) + b * math.sin(ang)) * r))
        rings.append(ring)
    faces = []
    for j in range(len(rings) - 1):
        for i in range(segs):
            i2 = (i + 1) % segs
            try:
                f = bm.faces.new([rings[j][i], rings[j][i2], rings[j + 1][i2], rings[j + 1][i]])
            except ValueError:
                continue
            faces.append(f)
    if cap_back:
        try:
            faces.append(bm.faces.new(rings[0][::-1]))
        except ValueError:
            pass
    lens = None
    if cap_front:
        try:
            lens = bm.faces.new(rings[-1])
            lens.material_index = self.mi(cap_front); lens.smooth = False
            for l in lens.loops:   # planar 0..1 (screen-like) uv
                l[self.uv].uv = (0.5 + 0.5 * (l.vert.co - o - d * profile[-1][0]).dot(a) / profile[-1][1],
                                 0.5 + 0.5 * (l.vert.co - o - d * profile[-1][0]).dot(b) / profile[-1][1])
        except ValueError:
            pass
    for f in faces:
        f.material_index = self.mi(mat); f.smooth = smooth
    if region and self.atlas:
        # facet-wise: u across the facet, v = axial fraction
        n = len(profile)
        for f in faces:
            vs = [l.vert.co for l in f.loops]
            if len(f.loops) == 4:
                axs = [(v - o).dot(d) for v in vs]
                amin, amax = min(axs), max(axs)
                for l, ax_ in zip(f.loops, axs):
                    pass
                # loops order: r_j,i  r_j,i2  r_j+1,i2  r_j+1,i
                us = [0, 1, 1, 0]
                lo = min(axs); hi = max(axs)
                tot = max(max(p_[0] for p_ in profile) - min(p_[0] for p_ in profile), 1e-6)
                for l, uu, ax_ in zip(f.loops, us, axs):
                    l[self.uv].uv = self.atlas.uvmap(region, uu, (ax_ - min(p_[0] for p_ in profile)) / tot)
            else:
                for l in f.loops:
                    l[self.uv].uv = self.atlas.uvmap(region, 0.5 + 0.5 * (l.vert.co - o).dot(a) / max(profile[0][1], 1e-6) * 0.5, 0.5 + 0.5 * (l.vert.co - o).dot(b) / max(profile[0][1], 1e-6) * 0.5)
    return faces

Builder.lathe = _lathe

def smooth_by_angle(ob, deg=35):
    import bpy
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    try:
        bpy.ops.object.shade_smooth_by_angle(angle=math.radians(deg))
    except Exception as e:
        print("smooth_by_angle failed", e)
        bpy.ops.object.shade_smooth()

def tri_count(ob):
    import bpy
    dg = bpy.context.evaluated_depsgraph_get()
    me = ob.evaluated_get(dg).to_mesh()
    me.calc_loop_triangles()
    n = len(me.loop_triangles)
    ob.evaluated_get(dg).to_mesh_clear()
    return n

def dims_of(ob):
    import mathutils
    pts = [ob.matrix_world @ mathutils.Vector(c) for c in ob.bound_box]
    mn = [min(p[k] for p in pts) for k in range(3)]; mx = [max(p[k] for p in pts) for k in range(3)]
    return mn, mx

def decimate_copy(ob, ratio, name=None):
    """decimated evaluated copy of an object (keeps UV/materials), returns new object"""
    import bpy
    md = ob.modifiers.new("dec", "DECIMATE"); md.ratio = ratio; md.use_collapse_triangulate = True
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg))
    ob.modifiers.remove(md)
    o2 = bpy.data.objects.new(name or ob.name + "_dec", me)
    bpy.context.scene.collection.objects.link(o2)
    return o2

def export_fbx(ob_list, path):
    import bpy
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    for o in ob_list:
        o.select_set(True)
    bpy.context.view_layer.objects.active = ob_list[0]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, global_scale=1.0, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_NONE", axis_forward="-Z", axis_up="Y",
                             bake_space_transform=True, object_types={"MESH"}, add_leaf_bones=False,
                             mesh_smooth_type="OFF", use_mesh_modifiers=True, path_mode="STRIP",
                             bake_anim=False)
    print("EXPORTED", path)

def copy_tex(paths, outdir=None):
    import shutil
    outdir = outdir or OUT
    os.makedirs(outdir, exist_ok=True)
    for p in paths.values():
        shutil.copy(p, os.path.join(outdir, os.path.basename(p)))

# ==============================================================================================
# MODULE 1: ROOF (profile measured from the source roof skin SM_asset_1052, swept straight / 90-degree arc)
# ==============================================================================================
# Source measurement (cantilever skin, short side): inner lip 5.4-6.5 m above the fascia bottom, crown 11 m,
# 25.8 m span.  Profile coordinates: u = outward from inner edge (m), z = up from fascia bottom (m).
ROOF_TOP = [(0, 6.5), (2, 7.0), (5, 8.6), (9, 10.2), (13, 11.0), (17, 10.7), (21, 9.4), (24, 7.6), (25.8, 6.2)]
ROOF_SOF = [(24.0, 5.1), (20, 7.2), (14, 8.6), (8, 8.0), (3, 5.9), (0, 5.4)]     # outer -> inner
ROOF_SPAN = 25.8

def catmull(pts, n):
    import numpy as np
    P = np.array(pts, float)
    ext = np.vstack([2 * P[0] - P[1], P, 2 * P[-1] - P[-2]])
    out = []
    segs = len(P) - 1
    per = max(1, n // segs)
    for i in range(segs):
        p0, p1, p2, p3 = ext[i], ext[i + 1], ext[i + 2], ext[i + 3]
        for k in range(per):
            t = k / per
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(P[-1])
    return [tuple(p) for p in out]

def roof_profile():
    top = catmull(ROOF_TOP, 16)
    sof = catmull(ROOF_SOF, 12)
    groups = [
        ("top", top),
        ("fo", [top[-1], (ROOF_SPAN, 0.0)]),               # outer fascia face
        ("fb", [(ROOF_SPAN, 0.0), (ROOF_SPAN - 0.45, 0.0)]),   # fascia bottom
        ("fi", [(ROOF_SPAN - 0.45, 0.0), (ROOF_SPAN - 0.55, 4.6), sof[0]]),   # fascia inner face
        ("sof", sof),
        ("lip", [sof[-1], top[0]]),
    ]
    return groups

GROUP_MAT = {"top": ("PIX_RoofMembrane", "top"), "sof": ("PIX_RoofMembrane", "soffit"),
             "fo": ("PIX_RoofSteel", "steel"), "fb": ("PIX_RoofSteel", "steel"),
             "fi": ("PIX_RoofSteel", "steel"), "lip": ("PIX_RoofSteel", "steel")}

def roof_pos(kind, s, u, z, L=20.0, Rin=25.0):
    if kind == "straight":
        return (-L / 2 + s * L, u, z)
    th = s * math.pi / 2
    r = Rin + u
    return (r * math.cos(th), r * math.sin(th), z)

def roof_frame(kind, s, Rin=25.0):
    """(tangent, outward) unit vectors at station s"""
    if kind == "straight":
        return (1, 0, 0), (0, 1, 0)
    th = s * math.pi / 2
    return (-math.sin(th), math.cos(th), 0), (math.cos(th), math.sin(th), 0)

def build_roof(kind):
    import bpy, numpy as np, mathutils
    reset_scene()
    NP = 5 if kind == "straight" else 6
    K = 5 if kind == "straight" else 18        # stations
    L, Rin = 20.0, 25.0
    at = Atlas(2048)
    at.region("top", 0, 0.5, 1, 1.0); at.region("soffit", 0, 0.25, 1, 0.5); at.region("steel", 0, 0, 1, 0.25)
    # ---------------- textures ----------------
    S = 2048
    h, w = at.size_of("top")
    nz = tile_noise(h, w, 2.2, 1); fine = tile_noise(h, w, 1.0, 2)
    weave = 0.5 + 0.5 * np.sin(np.arange(w)[None, :] * 2 * np.pi / 6) * np.sin(np.arange(h)[:, None] * 2 * np.pi / 6)
    seam = np.zeros((h, w), np.float32)
    for k in range(NP + 1):
        x = int(k * w / NP) % w
        for dx in range(-5, 6):
            seam[:, (x + dx) % w] = np.maximum(seam[:, (x + dx) % w], 1 - abs(dx) / 6.0)
    for k in range(1, 4):
        y = int(k * h / 4)
        seam[max(0, y - 3):y + 4, :] = np.maximum(seam[max(0, y - 3):y + 4, :], 0.5)
    col = np.array([0.6, 0.615, 0.63])[None, None, :] * (0.93 + 0.1 * nz[..., None]) * (1 - 0.35 * seam[..., None]) * (0.97 + 0.03 * weave[..., None])
    at.paint("top", col, height=(0.02 * weave + 0.01 * fine - 0.05 * seam), metal=0.0, smooth=0.42)
    h, w = at.size_of("soffit")
    nz = tile_noise(h, w, 2.0, 3)
    perf = np.zeros((h, w), np.float32)
    yy, xx = np.mgrid[0:h, 0:w]
    perf = (((xx % 16) - 8) ** 2 + ((yy % 16) - 8) ** 2 < 10).astype(np.float32)
    seam2 = np.zeros((h, w), np.float32)
    for k in range(NP + 1):
        x = int(k * w / NP) % w
        for dx in range(-4, 5):
            seam2[:, (x + dx) % w] = np.maximum(seam2[:, (x + dx) % w], 1 - abs(dx) / 5.0)
    col = np.array([0.58, 0.6, 0.62])[None, None, :] * (0.92 + 0.12 * nz[..., None]) * (1 - 0.5 * perf[..., None] * 0.5) * (1 - 0.4 * seam2[..., None])
    at.paint("soffit", col, height=(-0.04 * perf - 0.05 * seam2), metal=0.0, smooth=0.3)
    h, w = at.size_of("steel")
    br = tile_noise(h, w, 1.6, 4, aniso=(6.0, 1.0)); dirt = tile_noise(h, w, 2.5, 5)
    col = np.array([0.17, 0.18, 0.19])[None, None, :] * (0.8 + 0.4 * br[..., None]) * (0.85 + 0.3 * dirt[..., None])
    at.paint("steel", col, height=0.03 * br, metal=0.85, smooth=0.5 + 0.12 * dirt)
    paths = at.save("PIX_Roof" + ("Straight" if kind == "straight" else "Arc90"), normal_strength=1.5)
    # ---------------- materials/geometry ----------------
    make_material("PIX_RoofMembrane", (0.74, 0.755, 0.77, 1), roughness=0.55)
    make_material("PIX_RoofSteel", (0.17, 0.18, 0.19, 1), metallic=0.85, roughness=0.45)
    B = Builder("PIX_Roof" + ("Straight" if kind == "straight" else "Arc90"), ["PIX_RoofMembrane", "PIX_RoofSteel"], at)
    bm = B.bm
    groups = roof_profile()
    # ring vertices per station per profile point (dedupe joints)
    poly = []
    for gi, (tag, pl) in enumerate(groups):
        for pi, p in enumerate(pl):
            if gi > 0 and pi == 0:
                continue
            poly.append((tag, gi, pi, p))
    # arc length per group
    def alen(pl):
        a = [0.0]
        for i in range(1, len(pl)):
            a.append(a[-1] + math.hypot(pl[i][0] - pl[i - 1][0], pl[i][1] - pl[i - 1][1]))
        return a
    glen = {gi: alen(pl) for gi, (tag, pl) in enumerate(groups)}
    rings = []
    for k in range(K + 1):
        s = k / K
        rings.append([bm.verts.new(roof_pos(kind, s, p[0], p[1], L, Rin)) for (_, _, _, p) in poly])
    n = len(poly)
    # map (gi,pi) -> index in poly
    idx = {(g, p): i for i, (_, g, p, _) in enumerate(poly)}
    def vi(g, p):
        if p == 0 and g > 0:
            return idx[(g - 1, len(groups[g - 1][1]) - 1)]
        if g == len(groups) - 1 and p == len(groups[g][1]) - 1:
            return 0
        return idx[(g, p)]
    for gi, (tag, pl) in enumerate(groups):
        mat, reg = GROUP_MAT[tag]
        tot = glen[gi][-1]
        for k in range(K):
            for pi in range(len(pl) - 1):
                a, b = vi(gi, pi), vi(gi, pi + 1)
                vs = [rings[k][a], rings[k][b], rings[k + 1][b], rings[k + 1][a]]
                try:
                    f = bm.faces.new(vs)
                except ValueError:
                    continue
                f.material_index = B.mi(mat)
                u0, u1 = k / K, (k + 1) / K
                v0, v1 = glen[gi][pi] / tot, glen[gi][pi + 1] / tot
                if reg == "steel":
                    uvs = [at.uvmap(reg, u0, v0), at.uvmap(reg, u0, v1), at.uvmap(reg, u1, v1), at.uvmap(reg, u1, v0)]
                else:
                    uvs = [at.uvmap(reg, u0, v0), at.uvmap(reg, u0, v1), at.uvmap(reg, u1, v1), at.uvmap(reg, u1, v0)]
                for l, uv in zip(f.loops, uvs):
                    l[B.uv].uv = uv
    # end caps
    for k, flip in ((0, True), (K, False)):
        vs = list(rings[k])
        if flip: vs = vs[::-1]
        try:
            f = bm.faces.new(vs)
        except ValueError:
            continue
        f.material_index = B.mi("PIX_RoofSteel")
        for l in f.loops:
            p = l.vert.co
            l[B.uv].uv = at.uvmap("steel", (p[0] if kind == "straight" else p[0]) % 1.0, (p[2] / 12.0))
    # ---------------- trusses under the soffit ----------------
    sofn = catmull([(0, 5.4), (3, 5.9), (8, 8.0), (14, 8.6), (20, 7.2), (24, 5.1)], 8)   # nodes
    sofn = [(u, z - 0.18) for (u, z) in sofn]
    chord = lambda u: 5.0 - (u / 25.2) * 4.6
    nodes = [(u, z) for (u, z) in sofn]
    def at3(s, u, z, tt=0.0):
        p = roof_pos(kind, s, u, z, L, Rin)
        tan, out = roof_frame(kind, s)
        return (p[0] + tan[0] * tt, p[1] + tan[1] * tt, p[2])
    nT = 4 if kind == "straight" else 6
    for ti in range(nT):
        s = (ti + 0.5) / nT
        def mem(u0, z0, u1, z1, wd=0.22):
            B.beam(at3(s, u0, z0), at3(s, u1, z1), wd, 0.22, "PIX_RoofSteel", "steel", up=roof_frame(kind, s)[0])
        # chords
        for i in range(len(nodes) - 1):
            mem(nodes[i][0], nodes[i][1], nodes[i + 1][0], nodes[i + 1][1], 0.3)
        ub = [n_[0] for n_ in nodes]
        mem(ub[0], chord(ub[0]), ub[-1], chord(ub[-1]), 0.3)
        for i, (u, z) in enumerate(nodes):
            if z - chord(u) > 0.5:
                mem(u, z, u, chord(u), 0.18)
        for i in range(len(nodes) - 1):
            ua, za = nodes[i]; ub_, zb = nodes[i + 1]
            if i % 2 == 0: mem(ua, chord(ua), ub_, zb, 0.15)
            else: mem(ua, za, ub_, chord(ub_), 0.15)
    ob = B.to_object()
    smooth_by_angle(ob, 38)
    return ob, at, paths

def stage_roof():
    import bpy
    results = {}
    for kind in ("straight", "arc"):
        ob, at, paths = build_roof(kind)
        name = ob.name
        mn, mx = dims_of(ob)
        t = tri_count(ob)
        print(name, "tris", t, "bbox", [round(x, 2) for x in mn], [round(x, 2) for x in mx])
        export_fbx([ob], os.path.join(OUT, name + ".fbx"))
        copy_tex(paths)
        results[name] = dict(tris=t, mn=mn, mx=mx)
    json.dump(results, open(os.path.join(WORK, "roof_result.json"), "w"), indent=1)

# ==============================================================================================
# PREVIEW: re-import the exported FBX (validates axes/units) and render with the kit PBR maps
# ==============================================================================================
FLAT_MATS = {   # materials that are NOT in the texture atlas: name -> (colour, emission(rgb, strength), alpha)
    "PIX_LightTowerLamps": ((1, 1, 1, 1), ((1.0, 0.97, 0.9), 12.0), 1.0),
    "PIX_RibbonScreen": ((0.012, 0.014, 0.02, 1), None, 1.0),
    "PIX_ScoreboardScreen": ((0.012, 0.014, 0.02, 1), None, 1.0),
    "PIX_BoxGlass": ((0.55, 0.72, 0.8, 1), None, 0.22),
}

def _img_node(nt, path, cs):
    import bpy
    n = nt.nodes.new("ShaderNodeTexImage")
    n.image = bpy.data.images.load(path)
    n.image.colorspace_settings.name = cs
    return n

def preview_pbr_material(m, prefix):
    import bpy
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    base = os.path.join(OUT, prefix + "_BaseColor.png")
    if m.name in FLAT_MATS:
        col, em, al = FLAT_MATS[m.name]
        bsdf.inputs["Base Color"].default_value = col
        bsdf.inputs["Alpha"].default_value = al
        if m.name == "PIX_BoxGlass":
            bsdf.inputs["Roughness"].default_value = 0.05
            bsdf.inputs["Metallic"].default_value = 0.0
            try: bsdf.inputs["IOR"].default_value = 1.45
            except Exception: pass
        if em:
            bsdf.inputs["Emission Color"].default_value = (*em[0], 1)
            bsdf.inputs["Emission Strength"].default_value = em[1]
        if os.environ.get("PIX_UVCHECK") and "Screen" in m.name:
            import numpy as np
            hh, ww = 256, 1024
            u = np.linspace(0, 1, ww)[None, :].repeat(hh, 0); v = np.linspace(0, 1, hh)[:, None].repeat(ww, 1)
            chk = (((u * 32).astype(int) + (v * 8).astype(int)) % 2) * 0.25
            arr = (np.stack([u, v, chk, np.ones_like(u)], -1) * 255).astype(np.uint8)
            pth = os.path.join(WORK, "uvcheck.png"); write_png(pth, arr[::-1].copy())
            t = _img_node(nt, pth, "sRGB"); nt.links.new(t.outputs["Color"], bsdf.inputs["Emission Color"]); bsdf.inputs["Emission Strength"].default_value = 1.0
            nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
        return
    if not os.path.exists(base):
        return
    tb = _img_node(nt, base, "sRGB"); nt.links.new(tb.outputs["Color"], bsdf.inputs["Base Color"])
    tn = _img_node(nt, os.path.join(OUT, prefix + "_Normal.png"), "Non-Color")
    nm = nt.nodes.new("ShaderNodeNormalMap"); nm.inputs["Strength"].default_value = 1.0
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    tm = _img_node(nt, os.path.join(OUT, prefix + "_Mask.png"), "Non-Color")
    sp = nt.nodes.new("ShaderNodeSeparateColor"); nt.links.new(tm.outputs["Color"], sp.inputs["Color"])
    nt.links.new(sp.outputs["Red"], bsdf.inputs["Metallic"])
    inv = nt.nodes.new("ShaderNodeMath"); inv.operation = "SUBTRACT"; inv.inputs[0].default_value = 1.0
    nt.links.new(tm.outputs["Alpha"], inv.inputs[1]); nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])

def stage_preview(fbxname, outname=None, az="35", elev="22", dist="1.0", cube="0,0", prefix=None, res="1600x1000", extra=""):
    """render <OUT>/<fbxname>.fbx  ->  RENDERS/stadiumkit_<outname>.png"""
    import bpy, mathutils
    reset_scene()
    path = os.path.join(OUT, fbxname + ".fbx")
    bpy.ops.import_scene.fbx(filepath=path, axis_forward="-Z", axis_up="Y")
    obs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    prefix = prefix or fbxname
    for o in obs:
        for slot in o.material_slots:
            if slot.material:
                preview_pbr_material(slot.material, prefix)
    mn, mx = fit_scene_bbox(obs)
    c = [(mn[k] + mx[k]) / 2 for k in range(3)]
    ext = [mx[k] - mn[k] for k in range(3)]
    R = max(ext) * 0.5
    if "cz=" in extra:
        c[2] = float(extra.split("cz=")[1].split(";")[0])
    # ground
    bpy.ops.mesh.primitive_plane_add(size=max(ext) * 6, location=(c[0], c[1], mn[2] - 0.02 if False else min(0, mn[2]) - 0.01))
    g = bpy.context.object
    gm = make_material("_ground", (0.28, 0.3, 0.28, 1), roughness=0.9); g.data.materials.append(gm)
    # reference cube 1.9 m
    cx, cy = [float(x) for x in cube.split(",")]
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(cx, cy, min(0, mn[2]) + 0.95))
    rc = bpy.context.object; rc.scale = (0.5, 0.5, 1.9)
    rm = make_material("_refcube", (0.95, 0.4, 0.05, 1), roughness=0.6); rc.data.materials.append(rm)
    # sun + world
    sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 3.5; sun.angle = math.radians(5)
    so = bpy.data.objects.new("sun", sun); bpy.context.scene.collection.objects.link(so)
    so.rotation_euler = (math.radians(50), math.radians(10), math.radians(25))
    w = bpy.data.worlds.new("w"); bpy.context.scene.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.66, 0.82, 1)
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.1
    sc = bpy.context.scene
    try:
        sc.render.engine = "BLENDER_EEVEE"
    except Exception:
        sc.render.engine = "BLENDER_EEVEE_NEXT"
    rx, ry = map(int, res.split("x")); sc.render.resolution_x, sc.render.resolution_y = rx, ry
    sc.view_settings.view_transform = "Standard"
    a = math.radians(float(az)); e = math.radians(float(elev))
    Rs = 0.5 * math.sqrt(sum(x * x for x in ext)); fov = 2 * math.atan(18.0 * min(1.0, ry / rx) / 40.0)
    d = Rs / math.sin(fov / 2) * 1.05 * float(dist)
    loc = (c[0] + d * math.sin(a) * math.cos(e), c[1] - d * math.cos(a) * math.cos(e), c[2] + d * math.sin(e))
    add_camera(loc, c, lens=40)
    sc.render.filepath = os.path.join(RENDERS, "stadiumkit_%s.png" % (outname or fbxname))
    os.makedirs(RENDERS, exist_ok=True)
    bpy.ops.render.render(write_still=True)
    print("preview ->", sc.render.filepath, "dims", [round(x, 2) for x in ext])

# ----------------------------------------------------------------------------------------------
# shared procedural surface painters (atlas regions)
# ----------------------------------------------------------------------------------------------
def paint_steel(at, name, color=(0.6, 0.62, 0.64), seed=1, metal=0.85, smooth=0.55, grad_dirt=True, brushed=True):
    import numpy as np
    h, w = at.size_of(name)
    br = tile_noise(h, w, 1.6, seed, aniso=(5.0, 1.0) if brushed else (1.0, 1.0))
    dirt = tile_noise(h, w, 2.5, seed + 7)
    col = np.array(color)[None, None, :] * (0.82 + 0.3 * br[..., None]) * (0.88 + 0.22 * dirt[..., None])
    if grad_dirt:
        g = np.linspace(0.62, 1.0, h)[:, None, None]
        g = np.minimum(1.0, g * 1.0) ** 0.7
        col = col * g
    at.paint(name, col, height=0.03 * br + 0.01 * dirt, metal=metal, smooth=smooth - 0.1 * (1 - dirt))

def paint_painted(at, name, color=(0.1, 0.1, 0.11), seed=2, metal=0.4, smooth=0.5):
    import numpy as np
    h, w = at.size_of(name)
    n = tile_noise(h, w, 2.0, seed); f = tile_noise(h, w, 0.8, seed + 3)
    col = np.array(color)[None, None, :] * (0.88 + 0.24 * n[..., None]) * (0.96 + 0.08 * f[..., None])
    at.paint(name, col, height=0.015 * f + 0.01 * n, metal=metal, smooth=smooth)

def paint_concrete(at, name, color=(0.55, 0.55, 0.53), seed=3):
    import numpy as np
    h, w = at.size_of(name)
    n = tile_noise(h, w, 2.4, seed); f = tile_noise(h, w, 1.0, seed + 5); p = tile_noise(h, w, 3.0, seed + 9)
    col = np.array(color)[None, None, :] * (0.78 + 0.35 * n[..., None]) * (0.92 + 0.14 * f[..., None])
    col = col * (1 - 0.25 * (p[..., None] > 0.92))
    at.paint(name, col, height=0.05 * f + 0.03 * n - 0.04 * (p > 0.92), metal=0.0, smooth=0.12 + 0.05 * f)

def paint_grating(at, name, color=(0.2, 0.21, 0.22), seed=4, cell=16):
    import numpy as np
    h, w = at.size_of(name)
    yy, xx = np.mgrid[0:h, 0:w]
    bars = (((xx % cell) < 3) | ((yy % (cell * 2)) < 3)).astype(np.float32)
    n = tile_noise(h, w, 2.0, seed)
    col = np.array(color)[None, None, :] * (0.7 + 0.5 * bars[..., None]) * (0.85 + 0.3 * n[..., None])
    at.paint(name, col, height=0.06 * bars, metal=0.8, smooth=0.5)

# ==============================================================================================
# MODULE 2: LIGHT TOWER (floodlight mast; lamp panels are rebuilt from the measured source panel SM_asset_1495:
# 2.3 m x 2.1 m plate, 5 x 3 bullet pods ~0.37 m dia x 0.48 m long, lens = emissive)
# ==============================================================================================
def build_lamp_panel(B, cx, cy, cz, W=2.3, H=2.1):
    """lamp panel centred (cx,cz), plate back face at y=cy, lamps facing +Y. returns nothing"""
    B.box((cx, cy + 0.05, cz), (W, 0.1, H), "PIX_LightTower", "housing")
    # 5 x 3 pods
    cols, rows = 5, 3
    px = W / cols; pz = H / rows
    prof = [(0.0, 0.0), (0.0, 0.07), (0.07, 0.12), (0.19, 0.165), (0.38, 0.178), (0.455, 0.178), (0.48, 0.17), (0.48, 0.145), (0.43, 0.145)]
    for i in range(cols):
        for j in range(rows):
            x = cx - W / 2 + px * (i + 0.5); z = cz - H / 2 + pz * (j + 0.5)
            B.lathe((x, cy + 0.1, z), (0, 1, 0), prof, 8, "PIX_LightTower", "housing", smooth=True, cap_back=False, cap_front="PIX_LightTowerLamps")

def build_tower():
    import bpy, numpy as np
    reset_scene()
    at = Atlas(1024)
    at.region("steel", 0, 0.5, 0.5, 1); at.region("housing", 0.5, 0.5, 1, 1); at.region("concrete", 0, 0, 0.5, 0.5); at.region("grating", 0.5, 0, 1, 0.5)
    paint_steel(at, "steel", (0.66, 0.68, 0.7), 11); paint_painted(at, "housing", (0.12, 0.125, 0.135), 12, metal=0.55, smooth=0.45)
    paint_concrete(at, "concrete", (0.5, 0.5, 0.48), 13); paint_grating(at, "grating", (0.24, 0.25, 0.26), 14)
    paths = at.save("PIX_LightTower", normal_strength=1.5)
    make_material("PIX_LightTower", (0.55, 0.56, 0.58, 1), metallic=0.6, roughness=0.5)
    make_material("PIX_LightTowerLamps", (1, 1, 1, 1), emission=(1.0, 0.97, 0.9), emission_strength=12.0)
    B = Builder("PIX_LightTower", ["PIX_LightTower", "PIX_LightTowerLamps"], at)
    M = "PIX_LightTower"
    # foundation + mast
    B.box((0, 0, 0.3), (1.8, 1.8, 0.6), M, "concrete")
    B.frustum((0, 0, 0.66), 0.7, 0.7, 0.12, 12, M, "steel")           # base flange
    for sx in (-1, 1):
        B.box((sx * 0.46, 0, 1.3), (0.06, 0.8, 1.2), M, "steel")        # gussets
        B.box((0, sx * 0.46, 1.3), (0.8, 0.06, 1.2), M, "steel")
    z0 = 0.72
    secs = [(z0, 9.6, 0.50, 0.44), (9.6, 19.2, 0.44, 0.36), (19.2, 31.2, 0.36, 0.29)]
    for (za, zb, ra, rb) in secs:
        B.frustum((0, 0, (za + zb) / 2), ra, rb, zb - za, 12, M, "steel", caps=False)
    for z, r in ((9.6, 0.44), (19.2, 0.36)):
        B.frustum((0, 0, z), r + 0.06, r + 0.06, 0.16, 12, M, "steel", caps=False)
    B.frustum((0, 0, 31.25), 0.34, 0.34, 0.1, 12, M, "steel")           # top cap
    # ladder on the back (-Y)
    for sx in (-0.2, 0.2):
        B.box((sx, -0.78, 14.3), (0.05, 0.05, 25.2), M, "steel")
    zz = 1.4
    while zz < 26.4:
        B.box((0, -0.78, zz), (0.4, 0.03, 0.03), M, "steel")
        zz += 0.6
    for zb in np.arange(3.0, 26.0, 4.0):
        B.box((0, -0.55, zb), (0.45, 0.5, 0.05), M, "steel")
    # service platform
    B.frustum((0, 0, 26.6), 1.55, 1.55, 0.08, 16, M, "grating", smooth=False)
    for k in range(8):
        a = 2 * math.pi * k / 8
        B.box((1.5 * math.cos(a), 1.5 * math.sin(a), 27.1), (0.05, 0.05, 1.0), M, "steel")
    for k in range(16):
        a0 = 2 * math.pi * k / 16; a1 = 2 * math.pi * (k + 1) / 16
        B.beam((1.5 * math.cos(a0), 1.5 * math.sin(a0), 27.6), (1.5 * math.cos(a1), 1.5 * math.sin(a1), 27.6), 0.05, 0.05, M, "steel")
    # head frame + lamp panels
    zc = 29.1; yf = 1.0
    xs = (-2.45, 0.0, 2.45); zs = (zc - 1.125, zc + 1.125)
    for x in xs:
        for z in zs:
            build_lamp_panel(B, x, yf + 0.1, z)
    for px_ in (-3.675, -1.225, 1.225, 3.675):
        B.box((px_, yf, zc), (0.16, 0.16, 4.8), M, "housing")
    for dz in (-2.3, 0.0, 2.3):
        B.box((0, yf, zc + dz), (7.6, 0.16, 0.18), M, "housing")
    for bx in (-2.45, 0.0, 2.45):             # back diagonals
        B.beam((bx - 1.2, yf - 0.1, zc - 2.3), (bx + 1.2, yf - 0.1, zc), 0.1, 0.1, M, "housing")
        B.beam((bx + 1.2, yf - 0.1, zc - 2.3), (bx - 1.2, yf - 0.1, zc), 0.1, 0.1, M, "housing")
    for dz in (-2.3, 0.0, 2.3):               # arms from mast to frame
        B.beam((0, 0.2, zc + dz), (0, yf, zc + dz), 0.14, 0.18, M, "steel")
    B.beam((0, 0.3, zc - 2.3), (0, 0.3, 26.7), 0.1, 0.1, M, "steel")
    ob = B.to_object()
    smooth_by_angle(ob, 35)
    return ob, at, paths

def stage_tower():
    ob, at, paths = build_tower()
    mn, mx = dims_of(ob); t = tri_count(ob)
    print("PIX_LightTower tris", t, "bbox", [round(x, 2) for x in mn], [round(x, 2) for x in mx])
    export_fbx([ob], os.path.join(OUT, "PIX_LightTower.fbx")); copy_tex(paths)
    json.dump(dict(tris=t, mn=mn, mx=mx), open(os.path.join(WORK, "tower_result.json"), "w"))

def paint_cabinets(at, name, color=(0.1, 0.105, 0.115), cols=16, rows=1, seed=21, metal=0.55, smooth=0.45):
    """dark painted LED-cabinet / panel skin with seam lines every 1/cols (u) and 1/rows (v) + vents"""
    import numpy as np
    h, w = at.size_of(name)
    n = tile_noise(h, w, 2.0, seed)
    seam = np.zeros((h, w), np.float32)
    for k in range(cols + 1):
        x = int(k * w / cols) % w
        for dx in range(-2, 3):
            seam[:, (x + dx) % w] = np.maximum(seam[:, (x + dx) % w], 1 - abs(dx) / 3.0)
    for k in range(rows + 1):
        y = int(k * h / rows) % h
        for dy in range(-2, 3):
            seam[(y + dy) % h, :] = np.maximum(seam[(y + dy) % h, :], 1 - abs(dy) / 3.0)
    yy, xx = np.mgrid[0:h, 0:w]
    vent = (((xx * cols // w) >= 0) & ((xx % max(w // cols, 1)) > w // cols * 0.2) & ((xx % max(w // cols, 1)) < w // cols * 0.8) & ((yy % 8) < 2) & (yy > h * 0.3) & (yy < h * 0.7)).astype(np.float32)
    col = np.array(color)[None, None, :] * (0.88 + 0.24 * n[..., None]) * (1 - 0.6 * seam[..., None]) * (1 - 0.5 * vent[..., None])
    at.paint(name, col, height=-0.05 * seam - 0.03 * vent + 0.01 * n, metal=metal, smooth=smooth)

def paint_wood(at, name, color=(0.62, 0.45, 0.28), seed=31):
    import numpy as np
    h, w = at.size_of(name)
    n = tile_noise(h, w, 1.8, seed, aniso=(8.0, 1.0)); m = tile_noise(h, w, 2.5, seed + 1)
    col = np.array(color)[None, None, :] * (0.78 + 0.4 * n[..., None]) * (0.92 + 0.16 * m[..., None])
    at.paint(name, col, height=0.03 * n, metal=0.0, smooth=0.4)

def paint_fabric(at, name, color=(0.1, 0.12, 0.16), seed=41):
    import numpy as np
    h, w = at.size_of(name)
    yy, xx = np.mgrid[0:h, 0:w]
    weave = 0.5 + 0.5 * np.sin(xx * 2 * np.pi / 4) * np.sin(yy * 2 * np.pi / 4)
    n = tile_noise(h, w, 2.0, seed)
    col = np.array(color)[None, None, :] * (0.8 + 0.3 * weave[..., None]) * (0.9 + 0.2 * n[..., None])
    at.paint(name, col, height=0.03 * weave, metal=0.0, smooth=0.25)

def paint_white_panel(at, name, color=(0.78, 0.79, 0.8), cols=8, rows=2, seed=51):
    import numpy as np
    h, w = at.size_of(name)
    n = tile_noise(h, w, 2.2, seed)
    seam = np.zeros((h, w), np.float32)
    for k in range(cols + 1):
        x = int(k * w / cols) % w
        for dx in range(-2, 3):
            seam[:, (x + dx) % w] = np.maximum(seam[:, (x + dx) % w], 1 - abs(dx) / 3.0)
    for k in range(rows + 1):
        y = int(k * h / rows) % h
        for dy in range(-2, 3):
            seam[(y + dy) % h, :] = np.maximum(seam[(y + dy) % h, :], 1 - abs(dy) / 3.0)
    col = np.array(color)[None, None, :] * (0.9 + 0.15 * n[..., None]) * (1 - 0.45 * seam[..., None])
    at.paint(name, col, height=-0.05 * seam + 0.01 * n, metal=0.0, smooth=0.35)

# ==============================================================================================
# MODULE 3: RIBBON BOARD (16 m fascia LED strip). Source only holds a curved plane + 12-tri frame (SM_asset_1546/1547,
# 58 x 8.7 m), so the housing is built here around a clean UV 0..1 screen strip.
# ==============================================================================================
def build_ribbon(L=16.0, H=1.2):
    reset_scene()
    at = Atlas(1024)
    at.region("frame", 0, 0.5, 1, 1.0); at.region("steel", 0, 0.25, 0.5, 0.5); at.region("trim", 0.5, 0.25, 1, 0.5)
    paint_cabinets(at, "frame", (0.085, 0.09, 0.1), cols=int(L), rows=1, seed=22)
    paint_steel(at, "steel", (0.35, 0.36, 0.38), 23, metal=0.8, smooth=0.5, brushed=True)
    paint_steel(at, "trim", (0.72, 0.74, 0.76), 24, metal=0.95, smooth=0.6, grad_dirt=False)
    paths = at.save("PIX_RibbonBoard", normal_strength=1.5)
    make_material("PIX_RibbonBoard", (0.1, 0.1, 0.11, 1), metallic=0.5, roughness=0.5)
    make_material("PIX_RibbonScreen", (0.012, 0.014, 0.02, 1), roughness=0.25)
    B = Builder("PIX_RibbonBoard", ["PIX_RibbonBoard", "PIX_RibbonScreen"], at)
    M = "PIX_RibbonBoard"; D = 0.3
    B.box((0, D / 2, H / 2), (L, D, H), M, "frame")                          # cabinet housing
    t = 0.09
    B.box((0, D + 0.03, H - t / 2), (L, 0.06, t), M, "trim")                 # bezel top
    B.box((0, D + 0.03, t / 2), (L, 0.06, t), M, "trim")                     # bezel bottom
    for sx in (-1, 1):
        B.box((sx * (L / 2 - t / 2), D + 0.03, H / 2), (t, 0.06, H - 2 * t), M, "trim")   # bezel ends
    B.box((0, D + 0.065, H + 0.02), (L + 0.1, 0.5, 0.04), M, "steel")        # sun-hood cap
    # screen quad: UV 0..1 over the whole strip (u runs toward -X so it reads left-to-right from the front, +Y side)
    x0, x1 = L / 2 - t, -(L / 2 - t); z0, z1 = t, H - t; y = D + 0.012
    B.quad([(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)], "PIX_RibbonScreen", uvs=[(0, 0), (1, 0), (1, 1), (0, 1)], normal=(0, 1, 0))
    # hanger brackets behind
    for xb in np_linspace(-L / 2 + 1.0, L / 2 - 1.0, 5):
        for zb in (0.2, H - 0.2):
            B.box((xb, -0.2, zb), (0.12, 0.4, 0.12), M, "steel")
        B.box((xb, -0.38, H / 2), (0.12, 0.08, H - 0.1), M, "steel")
    ob = B.to_object()
    smooth_by_angle(ob, 35)
    return ob, at, paths

def np_linspace(a, b, n):
    return [a + (b - a) * i / (n - 1) for i in range(n)]

def stage_ribbon():
    ob, at, paths = build_ribbon()
    mn, mx = dims_of(ob); t = tri_count(ob)
    print("PIX_RibbonBoard tris", t, "bbox", [round(x, 2) for x in mn], [round(x, 2) for x in mx])
    export_fbx([ob], os.path.join(OUT, "PIX_RibbonBoard.fbx")); copy_tex(paths)
    json.dump(dict(tris=t, mn=mn, mx=mx), open(os.path.join(WORK, "ribbon_result.json"), "w"))

# ==============================================================================================
# MODULE 5b: SCOREBOARD FRAME (source SM_asset_1548/1549/1550: 19.2 x 10.9 m screen plane + thin frame)
# ==============================================================================================
def build_scoreboard(W=19.2, H=10.9):
    reset_scene()
    at = Atlas(1024)
    at.region("frame", 0, 0.5, 1, 1.0); at.region("steel", 0, 0, 0.5, 0.5); at.region("trim", 0.5, 0, 1, 0.5)
    paint_cabinets(at, "frame", (0.085, 0.09, 0.1), cols=12, rows=6, seed=61)
    paint_steel(at, "steel", (0.42, 0.43, 0.45), 62, metal=0.8, smooth=0.5)
    paint_steel(at, "trim", (0.7, 0.72, 0.74), 63, metal=0.95, smooth=0.6, grad_dirt=False)
    paths = at.save("PIX_ScoreboardFrame", normal_strength=1.5)
    make_material("PIX_ScoreboardFrame", (0.1, 0.1, 0.11, 1), metallic=0.5, roughness=0.5)
    make_material("PIX_ScoreboardScreen", (0.012, 0.014, 0.02, 1), roughness=0.25)
    B = Builder("PIX_ScoreboardFrame", ["PIX_ScoreboardFrame", "PIX_ScoreboardScreen"], at)
    M = "PIX_ScoreboardFrame"
    zb = 3.0; D = 0.9; bz = 0.4      # board bottom height, housing depth, bezel
    zc = zb + H / 2 + bz
    TW, TH = W + 2 * bz, H + 2 * bz
    B.box((0, D / 2, zc), (TW, D, TH), M, "frame")
    B.box((0, D + 0.04, zc + TH / 2 - bz / 2), (TW, 0.08, bz), M, "trim")
    B.box((0, D + 0.04, zc - TH / 2 + bz / 2), (TW, 0.08, bz), M, "trim")
    for sx in (-1, 1):
        B.box((sx * (TW / 2 - bz / 2), D + 0.04, zc), (bz, 0.08, TH - 2 * bz), M, "trim")
    y = D + 0.015
    x0, x1 = W / 2, -W / 2; z0, z1 = zc - H / 2, zc + H / 2
    B.quad([(x0, y, z0), (x1, y, z0), (x1, y, z1), (x0, y, z1)], "PIX_ScoreboardScreen", uvs=[(0, 0), (1, 0), (1, 1), (0, 1)], normal=(0, 1, 0))
    B.box((0, D / 2, zc + TH / 2 + 0.2), (TW, D + 0.2, 0.4), M, "steel")     # top cap / lamp rail housing
    for sx in (-1, 1):                                                       # support columns + bracing
        B.box((sx * (W / 2 - 2.0), -0.5, (zb + 0.2) / 2), (0.9, 0.9, zb + 0.2), M, "steel")
        B.box((sx * (W / 2 - 2.0), -0.5, 0.15), (1.5, 1.5, 0.3), M, "steel")
        B.beam((sx * (W / 2 - 2.0), -0.1, 0.4), (sx * (W / 2 - 2.0), -0.05, zb + 0.5), 0.5, 0.5, M, "steel")
    B.beam((-W / 2 + 2.0, -0.5, 0.5), (W / 2 - 2.0, -0.5, zb + 0.4), 0.18, 0.18, M, "steel")
    B.beam((W / 2 - 2.0, -0.5, 0.5), (-W / 2 + 2.0, -0.5, zb + 0.4), 0.18, 0.18, M, "steel")
    ob = B.to_object(); smooth_by_angle(ob, 35)
    return ob, at, paths

def stage_scoreboard():
    ob, at, paths = build_scoreboard()
    mn, mx = dims_of(ob); t = tri_count(ob)
    print("PIX_ScoreboardFrame tris", t, "bbox", [round(x, 2) for x in mn], [round(x, 2) for x in mx])
    export_fbx([ob], os.path.join(OUT, "PIX_ScoreboardFrame.fbx")); copy_tex(paths)
    json.dump(dict(tris=t, mn=mn, mx=mx), open(os.path.join(WORK, "scoreboard_result.json"), "w"))

# ==============================================================================================
# MODULE 4: MEDIA / PRESS BOX (source shell+glazing SM_asset_1522/1523: 21.8 x 3.1 x 3.6 m, 12 bays with a transom;
# the source VIP boxes (SM_asset_22 etc.) are 680k-poly seat blocks and were rejected; interior is rebuilt simply)
# ==============================================================================================
def build_chair(B, x, y, z0=0.0):
    M = "PIX_MediaBox"
    B.box((x, y, z0 + 0.46), (0.5, 0.5, 0.08), M, "fabric")                 # seat
    B.box((x, y - 0.26, z0 + 0.78), (0.48, 0.07, 0.5), M, "fabric")         # back (chair faces +Y, toward the glass)
    B.frustum((x, y, z0 + 0.25), 0.03, 0.03, 0.4, 6, M, "metal", caps=False)
    for k in range(5):
        a = 2 * math.pi * k / 5
        B.beam((x, y, z0 + 0.08), (x + 0.28 * math.cos(a), y + 0.28 * math.sin(a), z0 + 0.06), 0.045, 0.03, M, "metal")

def build_mediabox(L=21.8, Dp=3.1, Hh=3.6):
    import numpy as np
    reset_scene()
    at = Atlas(1024)
    at.region("wall", 0, 0.5, 0.5, 1); at.region("frame", 0.5, 0.5, 1, 1); at.region("desk", 0, 0.25, 0.5, 0.5)
    at.region("fabric", 0.5, 0.25, 1, 0.5); at.region("metal", 0, 0, 0.5, 0.25); at.region("floor", 0.5, 0, 1, 0.25)
    paint_white_panel(at, "wall", (0.74, 0.75, 0.76), cols=11, rows=2, seed=71)
    paint_steel(at, "frame", (0.2, 0.21, 0.22), 72, metal=0.85, smooth=0.5, grad_dirt=False)
    paint_wood(at, "desk", (0.55, 0.4, 0.26), 73); paint_fabric(at, "fabric", (0.09, 0.11, 0.15), 74)
    paint_steel(at, "metal", (0.5, 0.52, 0.54), 75, metal=0.9, smooth=0.5, grad_dirt=False)
    paint_concrete(at, "floor", (0.32, 0.32, 0.31), 76)
    paths = at.save("PIX_MediaBox", normal_strength=1.5)
    make_material("PIX_MediaBox", (0.6, 0.6, 0.6, 1), roughness=0.5)
    make_material("PIX_BoxGlass", (0.55, 0.72, 0.8, 1), alpha=0.22, roughness=0.05)
    B = Builder("PIX_MediaBox", ["PIX_MediaBox", "PIX_BoxGlass"], at)
    M = "PIX_MediaBox"; G = "PIX_BoxGlass"
    sl = 0.2; wt = 0.2
    B.box((0, -Dp / 2, sl / 2), (L, Dp, sl), M, "floor")                                    # floor slab
    B.box((0, -Dp / 2, Hh - 0.15), (L + 0.4, Dp + 0.4, 0.3), M, "wall")                     # roof slab with overhang
    B.box((0, -Dp + wt / 2, Hh / 2), (L, wt, Hh - 0.3), M, "wall")                           # back wall
    for sx in (-1, 1):
        B.box((sx * (L / 2 - wt / 2), -Dp / 2, Hh / 2), (wt, Dp, Hh - 0.3), M, "wall")      # end walls
    # front glazing: 12 bays, transom at 1.15 m
    nb = 12; bw = L / nb; zt = 1.15
    for i in range(nb + 1):
        B.box((-L / 2 + i * bw, 0.0, (sl + Hh - 0.3) / 2), (0.1, 0.12, Hh - 0.3 - sl), M, "frame")
    B.box((0, 0.0, zt), (L, 0.12, 0.1), M, "frame")
    B.box((0, 0.0, sl + 0.05), (L, 0.12, 0.1), M, "frame")
    B.box((0, 0.0, Hh - 0.35), (L, 0.12, 0.1), M, "frame")
    for i in range(nb):
        xc = -L / 2 + (i + 0.5) * bw
        for (za, zb) in ((sl + 0.1, zt - 0.05), (zt + 0.05, Hh - 0.4)):
            B.box((xc, 0.0, (za + zb) / 2), (bw - 0.1, 0.02, zb - za), G)
    # interior: desks (facing the glass) with monitors and chairs, sized for 10 seats
    seats = 9; pitch = (L - 1.6) / seats
    for i in range(seats):
        x = -L / 2 + 0.8 + (i + 0.5) * pitch
        B.box((x, -0.55, 0.74), (pitch - 0.35, 0.65, 0.05), M, "desk")                       # desk top
        for sx in (-1, 1):
            B.box((x + sx * (pitch - 0.45) / 2, -0.55, 0.37), (0.05, 0.6, 0.72), M, "metal")  # legs
        B.box((x, -0.55, 0.5), (pitch - 0.4, 0.03, 0.5), M, "metal")                           # modesty panel
        B.box((x, -0.62, 1.05), (0.55, 0.04, 0.34), M, "frame")                              # monitor
        B.box((x, -0.62, 0.84), (0.1, 0.12, 0.14), M, "metal")                               # stand
        build_chair(B, x, -1.35, sl)
    B.box((L / 2 - 1.2, -Dp + wt + 0.05, 1.05), (1.1, 0.06, 2.1), M, "frame")               # back door
    B.box((0, -Dp / 2 + 0.2, Hh - 0.33), (L - 1.0, 0.5, 0.05), M, "metal")                   # ceiling light tray
    ob = B.to_object(); smooth_by_angle(ob, 35)
    return ob, at, paths

def stage_mediabox():
    ob, at, paths = build_mediabox()
    mn, mx = dims_of(ob); t = tri_count(ob)
    print("PIX_MediaBox tris", t, "bbox", [round(x, 2) for x in mn], [round(x, 2) for x in mx])
    export_fbx([ob], os.path.join(OUT, "PIX_MediaBox.fbx")); copy_tex(paths)
    json.dump(dict(tris=t, mn=mn, mx=mx), open(os.path.join(WORK, "mediabox_result.json"), "w"))

# ==============================================================================================
# MODULE 5a: TUNNEL MOUTH (player tunnel portal; rebuilt, nothing in the source matches a pitch-level portal)
# ==============================================================================================
def build_tunnel(Wo=4.4, Ho=3.2, Dep=7.0):
    reset_scene()
    at = Atlas(1024)
    at.region("wall", 0, 0.5, 0.5, 1); at.region("pad", 0.5, 0.5, 1, 1); at.region("floor", 0, 0, 0.5, 0.5); at.region("frame", 0.5, 0, 1, 0.5)
    paint_concrete(at, "wall", (0.36, 0.36, 0.35), 81)
    paint_cabinets(at, "pad", (0.12, 0.13, 0.15), cols=4, rows=6, seed=82, metal=0.0, smooth=0.3)
    paint_concrete(at, "floor", (0.22, 0.22, 0.22), 83)
    paint_steel(at, "frame", (0.22, 0.23, 0.25), 84, metal=0.85, smooth=0.5, grad_dirt=False)
    paths = at.save("PIX_TunnelMouth", normal_strength=1.5)
    make_material("PIX_TunnelMouth", (0.4, 0.4, 0.4, 1), roughness=0.6)
    B = Builder("PIX_TunnelMouth", ["PIX_TunnelMouth"], at)
    M = "PIX_TunnelMouth"; wt = 0.5
    # portal: front facade slab with opening, tube behind (-Y), floor ramp
    fw, fh = Wo + 2.2, Ho + 1.6
    B.box((0, 0.0, Ho + 0.8 - 0.4 + 0.4), (fw, 0.5, 1.2), M, "wall")                          # lintel (above opening)
    for sx in (-1, 1):
        B.box((sx * (Wo / 2 + 0.55), 0.0, Ho / 2 + 0.2), (1.1, 0.5, Ho + 0.4), M, "wall")        # jambs
    B.box((0, 0.0, Ho + 1.5), (fw + 0.8, 0.9, 0.25), M, "frame")                            # canopy cap
    for sx in (-1, 1):                                                                      # flared wing walls
        p0 = (sx * (fw / 2 - 0.2), 0.0, 1.4)
        p1 = (p0[0] + sx * math.cos(math.radians(28)) * 3.4, p0[1] + math.sin(math.radians(28)) * 3.4, 1.4)
        B.beam(p0, p1, 0.4, 2.8, M, "wall")
        B.beam((p0[0], p0[1], 2.85), (p1[0], p1[1], 2.85), 0.5, 0.1, M, "frame")
    # padded jambs (inner reveal)
    for sx in (-1, 1):
        B.box((sx * (Wo / 2 - 0.1), -(Dep + 0.25) / 2, Ho / 2 - 0.1), (0.2, Dep - 0.25, Ho - 0.2), M, "pad")     # side padding runs in the tube
    B.box((0, -(Dep + 0.25) / 2, Ho + 0.1), (Wo, Dep - 0.25, 0.2), M, "wall")                               # tube ceiling
    B.box((0, -Dep / 2, -0.1), (Wo + 2.0, Dep + 0.6, 0.2), M, "floor")                        # floor
    for sx in (-1, 1):
        B.box((sx * (Wo / 2 + 0.55), -(Dep + 0.25) / 2, Ho / 2 + 0.2), (1.1, Dep - 0.25, Ho + 0.4), M, "wall")  # tube side walls (outer)
    B.box((0, -Dep, Ho / 2), (Wo + 2.2, 0.2, Ho + 0.4), M, "floor")                         # dark far end cap
    # ceiling light strips (flat boxes)
    for k in range(4):
        B.box((0, -1.0 - k * 1.6, Ho - 0.05), (0.5, 0.9, 0.05), M, "frame")
    ob = B.to_object(); smooth_by_angle(ob, 35)
    return ob, at, paths

def stage_tunnel():
    ob, at, paths = build_tunnel()
    mn, mx = dims_of(ob); t = tri_count(ob)
    print("PIX_TunnelMouth tris", t, "bbox", [round(x, 2) for x in mn], [round(x, 2) for x in mx])
    export_fbx([ob], os.path.join(OUT, "PIX_TunnelMouth.fbx")); copy_tex(paths)
    json.dump(dict(tris=t, mn=mn, mx=mx), open(os.path.join(WORK, "tunnel_result.json"), "w"))

def _env(sc, sun_rot=(50, 10, 25)):
    import bpy
    sun = bpy.data.lights.new("sun", "SUN"); sun.energy = 3.5; sun.angle = math.radians(5)
    so = bpy.data.objects.new("sun", sun); bpy.context.scene.collection.objects.link(so)
    so.rotation_euler = tuple(math.radians(x) for x in sun_rot)
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.55, 0.66, 0.82, 1)
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 1.1
    try: sc.render.engine = "BLENDER_EEVEE"
    except Exception: sc.render.engine = "BLENDER_EEVEE_NEXT"
    sc.view_settings.view_transform = "Standard"

def stage_roofring(outname="PIX_RoofRing_assembly", a="45.0", b="35.0", nx="2", ny="1"):
    """assemble 4 arcs + straights into a rounded-rect ring (a,b = half size of the INNER opening) -> assembly check render"""
    import bpy, mathutils
    reset_scene()
    for nm in ("PIX_RoofStraight", "PIX_RoofArc90"):
        bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, nm + ".fbx"), axis_forward="-Z", axis_up="Y")
    st = bpy.data.objects["PIX_RoofStraight"]; ar = bpy.data.objects["PIX_RoofArc90"]
    for o in (st, ar):
        for slot in o.material_slots:
            preview_pbr_material(slot.material, o.name if o.name in ("PIX_RoofStraight", "PIX_RoofArc90") else o.name)
    a = float(a); b = float(b); R = 25.0; nx = int(nx); ny = int(ny); L = 20.0
    def inst(src, loc, rotz):
        o = bpy.data.objects.new(src.name + "_i", src.data)
        bpy.context.scene.collection.objects.link(o)
        o.matrix_world = mathutils.Matrix.Translation(loc) @ mathutils.Matrix.Rotation(math.radians(rotz), 4, "Z") @ src.matrix_world
        return o
    # arcs: quadrant I base; centres at (+-(a-R), +-(b-R))
    for sx, sy, rot in ((1, 1, 0), (-1, 1, 90), (-1, -1, 180), (1, -1, 270)):
        inst(ar, (sx * (a - R), sy * (b - R), 0), rot)
    # +Y / -Y straights run along X, length centred, count nx
    for i in range(nx):
        x = (i - (nx - 1) / 2) * L
        inst(st, (x, b, 0), 0)
        inst(st, (-x, -b, 0), 180)
    for i in range(ny):
        y = (i - (ny - 1) / 2) * L
        inst(st, (a, y, 0), -90)
        inst(st, (-a, -y, 0), 90)
    st.hide_render = True; ar.hide_render = True
    sc = bpy.context.scene; _env(sc)
    bpy.ops.mesh.primitive_plane_add(size=500, location=(0, 0, -0.02)); g = bpy.context.object
    g.data.materials.append(make_material("_ground", (0.25, 0.3, 0.25, 1), roughness=0.9))
    bpy.ops.mesh.primitive_cube_add(size=1.0, location=(0, 0, 0.95)); rc = bpy.context.object; rc.scale = (0.5, 0.5, 1.9)
    rc.data.materials.append(make_material("_refcube", (0.95, 0.4, 0.05, 1)))
    sc.render.resolution_x, sc.render.resolution_y = 1800, 1100
    add_camera((95, -150, 120), (0, 0, 5), lens=34)
    sc.render.filepath = os.path.join(RENDERS, "stadiumkit_%s.png" % outname)
    bpy.ops.render.render(write_still=True)
    print("ring ->", sc.render.filepath)

# ==============================================================================================
# driver
# ==============================================================================================
PREVIEWS = [   # fbx, outname, az, elev, dist, cube xy, res
    ("PIX_RoofStraight", "PIX_RoofStraight", "30", "22", "0.95", "-2,-4", "1600x1000"),
    ("PIX_RoofStraight", "PIX_RoofStraight_underside", "20", "-2", "0.55", "-2,-4", "1600x1000", "cz=6"),
    ("PIX_RoofArc90", "PIX_RoofArc90", "20", "30", "0.95", "-2,-4", "1600x1100"),
    ("PIX_LightTower", "PIX_LightTower", "215", "12", "1.0", "3,3", "900x1400"),
    ("PIX_LightTower", "PIX_LightTower_head", "200", "6", "0.30", "3,3", "1400x1000", "cz=29.1"),
    ("PIX_RibbonBoard", "PIX_RibbonBoard", "205", "14", "0.85", "-9,3", "1600x800"),
    ("PIX_MediaBox", "PIX_MediaBox", "205", "15", "0.9", "-12.5,3", "1600x900"),
    ("PIX_TunnelMouth", "PIX_TunnelMouth", "200", "15", "0.9", "5.5,2", "1400x1000"),
    ("PIX_ScoreboardFrame", "PIX_ScoreboardFrame", "205", "12", "0.9", "-12,3", "1400x1100"),
]

def stage_build_all():
    for fn in (stage_roof, stage_tower, stage_ribbon, stage_mediabox, stage_tunnel, stage_scoreboard):
        fn()

def stage_previews(only=""):
    for (fbx, out, az, el, dist, cube, res, *ex) in PREVIEWS:
        if only and only not in out:
            continue
        stage_preview(fbx, out, az, el, dist, cube, fbx, res, ex[0] if ex else "")
    os.environ["PIX_UVCHECK"] = "1"
    stage_preview("PIX_RibbonBoard", "PIX_RibbonBoard_uvcheck", "160", "8", "0.85", "-9,3", "PIX_RibbonBoard", "1600x800")
    stage_preview("PIX_ScoreboardFrame", "PIX_ScoreboardFrame_uvcheck", "160", "8", "0.9", "-12,3", "PIX_ScoreboardFrame", "1400x1100")
    os.environ.pop("PIX_UVCHECK")
    stage_roofring()

def stage_texsheet(outpng=None):
    """contact sheet of every delivered texture (visual brand-free check)"""
    import bpy, numpy as np
    outpng = outpng or os.path.join(RENDERS, "stadiumkit_textures_contact.png")
    sets = sorted(set(f.rsplit("_", 1)[0] for f in os.listdir(OUT) if f.endswith(".png")))
    T = 340
    cols = 6
    rows = (len(sets) * 3 + cols - 1) // cols
    sheet = np.zeros((rows * T, cols * T, 3), np.uint8)
    i = 0
    for s in sets:
        for tag in ("BaseColor", "Normal", "Mask"):
            img = bpy.data.images.load(os.path.join(OUT, "%s_%s.png" % (s, tag)))
            w, h = img.size
            a = np.array(img.pixels[:], np.float32).reshape(h, w, 4)[..., :3]
            step = max(1, w // T)
            a = a[::step, ::step][:T, :T]
            a8 = (np.clip(a, 0, 1) ** (1 / 2.2) * 255).astype(np.uint8) if tag == "BaseColor" else (np.clip(a, 0, 1) * 255).astype(np.uint8)
            r, c = divmod(i, cols)
            sheet[r * T:r * T + a8.shape[0], c * T:c * T + a8.shape[1]] = a8[::-1]
            i += 1
            bpy.data.images.remove(img)
    write_png(outpng, sheet)
    print("texsheet ->", outpng, sets)

def stage_validate():
    """re-import every kit FBX and print tris / dims / materials / uv range"""
    import bpy, numpy as np
    out = {}
    for f in sorted(os.listdir(OUT)):
        if not f.endswith(".fbx"):
            continue
        reset_scene()
        bpy.ops.import_scene.fbx(filepath=os.path.join(OUT, f), axis_forward="-Z", axis_up="Y")
        obs = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        tris = sum(tri_count(o) for o in obs)
        mn, mx = fit_scene_bbox(obs)
        mats = sorted(set(s.material.name for o in obs for s in o.material_slots if s.material))
        uvs = []
        for o in obs:
            me = o.data
            if me.uv_layers:
                u = np.empty(len(me.loops) * 2, np.float32); me.uv_layers[0].data.foreach_get("uv", u)
                uvs.append(u.reshape(-1, 2))
        uv = np.concatenate(uvs)
        vol = 0.0; npoly_flipped = 0
        for o in obs:
            me = o.data; me.calc_loop_triangles()
            co = np.empty(len(me.vertices) * 3, np.float32); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
            tv = np.empty(len(me.loop_triangles) * 3, np.int32); me.loop_triangles.foreach_get("vertices", tv); tv = tv.reshape(-1, 3)
            a, b, c_ = co[tv[:, 0]], co[tv[:, 1]], co[tv[:, 2]]
            vol += float(np.einsum("ij,ij->i", a, np.cross(b, c_)).sum() / 6.0)
        out[f] = dict(objs=[o.name for o in obs], tris=tris, size=[round(mx[k] - mn[k], 2) for k in range(3)],
                      mn=[round(x, 2) for x in mn], mx=[round(x, 2) for x in mx], mats=mats,
                      uv_min=uv.min(0).round(3).tolist(), uv_max=uv.max(0).round(3).tolist(), nuv=len(me.uv_layers), signed_volume=round(vol, 2))
        print(f, json.dumps(out[f]))
    json.dump(out, open(os.path.join(WORK, "validate.json"), "w"), indent=1)

if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    stage = argv[0] if argv else "scan"
    g = globals()
    g["stage_" + stage](*argv[1:])
