"""
pres_newsstudio.py - build the Unity-ready "PIX_NewsStudio" FBX (halftime / post-game show set).

Run (Blender 5.2, headless, re-runnable):
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python pres_newsstudio.py

Source (user-owned, royalty free purchase "Sports News Studio TV Broadcast Set"):
  D:\\PIXFootball\\Source\\PPF_NEW\\_news_blend\\Sport News v1.blend   (textures packed inside)
  fallback: D:\\PIXFootball\\Source\\PPF_NEW\\news_studio\\Sport News v1.fbx (no textures -> flat colours)

What it does
  * deletes the dummy room box SM_Box, adds a plain floor (the source set has no floor plane)
  * decimates the 17 lamps (5.4k tris each) to ~650 tris and joins them
  * re-classifies every face of the 56 source materials into 11 PIX_News_* materials
    (gold strip textures -> flat PIX_News_Trim; brand art on the desk screen and ball photo on the
    video wall are DROPPED; both screens get 0..1 UVs and a flat dark placeholder)
  * writes PIX_News_<name>_BaseColor/_Normal/_Mask.png (<=1024 px, Mask = R metallic, G AO, B 0, A smoothness)
  * moves the origin to the floor at the host spot behind the desk, set faces -Y (camera side)
  * exports PIX_NewsStudio.fbx (axis_forward='-Z', axis_up='Y', 1 unit = 1 m) next to the textures
  * renders Workbench previews + texture contact sheets to D:\\PIXFootball\\Renders\\v070_d\\news_*.png
"""
import bpy, bmesh, numpy as np, os, math, sys
from mathutils import Vector, Matrix

BLEND = r"D:\PIXFootball\Source\PPF_NEW\_news_blend\Sport News v1.blend"
FBXSRC = r"D:\PIXFootball\Source\PPF_NEW\news_studio\Sport News v1.fbx"
OUT = r"C:\Users\peyto\Desktop\PeytonParrishGames\PIX PRO FOOTBALL\Assets\_Licensed\PIX\Presentation\NewsStudio"
RENDERS = r"D:\PIXFootball\Renders\v070_d"
FBX_OUT = os.path.join(OUT, "PIX_NewsStudio.fbx")
os.makedirs(OUT, exist_ok=True)
os.makedirs(RENDERS, exist_ok=True)

LAMP_TRIS = 650
HOST_ORIG = (0.5, 0.0)          # host standing spot in the SOURCE frame (behind desk, on podium)
RES = {"w": 1280, "h": 720}


def log(*a):
    print("NEWS>", *a, flush=True)


# ----------------------------------------------------------------------------------------------
# helpers
# ----------------------------------------------------------------------------------------------
def img_px(img):
    w, h = img.size
    a = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(a)
    return a.reshape(h, w, 4)


def downscale(a, size):
    h, w = a.shape[:2]
    fy, fx = max(1, h // size), max(1, w // size)
    if fy > 1 or fx > 1:
        a = a[: (h // fy) * fy, : (w // fx) * fx].reshape(h // fy, fy, w // fx, fx, a.shape[2]).mean((1, 3))
    return a


def save_png(name, arr, colorspace="sRGB"):
    h, w = arr.shape[:2]
    path = os.path.join(OUT, name)
    if name in bpy.data.images:
        bpy.data.images.remove(bpy.data.images[name])
    im = bpy.data.images.new(name, w, h, alpha=True)
    im.pixels.foreach_set(np.clip(arr, 0, 1).astype(np.float32).ravel())
    im.filepath_raw = path
    im.file_format = "PNG"
    im.save()
    im.colorspace_settings.name = colorspace
    return path


def normal_from_height(hgt, strength):
    dx = (np.roll(hgt, -1, 1) - np.roll(hgt, 1, 1)) * 0.5
    dy = (np.roll(hgt, -1, 0) - np.roll(hgt, 1, 0)) * 0.5
    n = np.dstack([-dx * strength, -dy * strength, np.ones_like(hgt)])
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    out = np.ones(hgt.shape + (4,), np.float32)
    out[..., :3] = n * 0.5 + 0.5
    return out


# ----------------------------------------------------------------------------------------------
# 1. load source
# ----------------------------------------------------------------------------------------------
have_tex = True
try:
    bpy.ops.wm.open_mainfile(filepath=BLEND)
    assert "SM_Table" in bpy.data.objects
    log("loaded blend")
except Exception as e:
    log("blend load failed, importing FBX (no textures):", e)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=FBXSRC)
    have_tex = False
    for o in bpy.data.objects:  # FBX importer may bring cm scale
        pass

sc = bpy.context.scene
for o in list(bpy.data.objects):
    if o.type != "MESH":
        bpy.data.objects.remove(o)
if "SM_Box" in bpy.data.objects:
    bpy.data.objects.remove(bpy.data.objects["SM_Box"])
    log("deleted SM_Box")
meshes = {o.name: o for o in bpy.data.objects if o.type == "MESH"}

# ----------------------------------------------------------------------------------------------
# 2. lamps: decimate one, copy to the other 16
# ----------------------------------------------------------------------------------------------
lamps = sorted([o for n, o in meshes.items() if n.startswith("SM_Lamp_")], key=lambda o: o.name)
if lamps:
    src = lamps[0]
    tris0 = sum(len(p.vertices) - 2 for p in src.data.polygons)
    bpy.context.view_layer.objects.active = src
    md = src.modifiers.new("dec", "DECIMATE")
    md.decimate_type = "COLLAPSE"
    md.ratio = min(1.0, LAMP_TRIS / float(tris0))
    with bpy.context.temp_override(object=src, active_object=src):
        bpy.ops.object.modifier_apply(modifier="dec")
    t1 = sum(len(p.vertices) - 2 for p in src.data.polygons)
    log("lamp tris", tris0, "->", t1)
    base_mesh = src.data
    for o in lamps[1:]:
        old = o.data
        o.data = base_mesh.copy()
        o.data.name = o.name + "_m"
        bpy.data.meshes.remove(old)

# ----------------------------------------------------------------------------------------------
# 3. face classification -> PIX material classes
# ----------------------------------------------------------------------------------------------
_img_cache = {}


def base_image_of(matname):
    """the base-colour image (as RGBA array, raw/encoded) of a source material, or None"""
    if not have_tex:
        return None
    if matname in _img_cache:
        return _img_cache[matname]
    m = bpy.data.materials.get(matname)
    res = None
    if m and m.node_tree:
        for n in m.node_tree.nodes:
            if n.type == "TEX_IMAGE" and n.image:
                nm = n.image.name.lower()
                if "rough" in nm or "alpha" in nm:
                    continue
                res = img_px(n.image)
                break
    _img_cache[matname] = res
    return res


def sample(a, u, v):
    h, w = a.shape[:2]
    ix = int((u % 1.0) * w) % w
    iy = int((v % 1.0) * h) % h
    return a[iy, ix, :3]


def is_gold(c):
    return c[0] > 0.30 and c[0] > 1.3 * c[2]


gold_pts = []

# per source object: (default class, {source material: rule})
#   rule 'X'      -> always class X
#   rule ('G',X,Y)-> sampled gold -> X, else Y
RULES = {
    "SM_Stage": {"M_Glossy_Plastic": "Floor", "M_Light_08": ("G", "Trim", "Floor")},
    "SM_Base": {"M_Glossy_Plastic": "Desk", "M_Light_01": ("G", "Trim", "Desk")},
    "SM_Table": {"M_Plastic_01": "Desk", "M_Light_03.001": ("G", "Trim", "Desk")},
    "SM_Sill": {"M_Plastic_01": "Desk", "M_Light_09": ("G", "Trim", "Desk")},
    "SM_Strips": {"M_Light_10": "Wall"},
    "SM_Cap": {"M_Light_03": ("G", "Trim", "Wall")},
    "SM_Tube": {"M_Alluminium": "Truss"},
    "SM_Clamps": {"M_Alluminium": "Truss"},
    "SM_Wall_01": {"M_Grey_Plastic": "Wall"},
    "SM_Wall_02": {"M_Glossy_Plastic": "Backdrop"},
    "SM_Glass": {"Glass Clear": "DeskGlass"},
    "SM_Screen": {"M_Light_05": "ScreenWall"},
    "SM_Table_Screen": {"M_News": "ScreenDesk"},
}
for i in range(1, 7):
    RULES["SM_Pylon_%02d" % i] = {"M_Glossy_Plastic": "Desk"}
for i in range(1, 18):
    RULES["SM_Lamp_%02d" % i] = {"Lamp": "LampLens", "M_Plastic_01": "Lamp", "M_Light_07": "LampLens"}
PYLON_DEFAULT_TRIM = ("G", "Trim", "Desk")

mw_cache = {}
face_class = {}   # obj name -> np array of class names (object dtype)
for name, o in meshes.items():
    me = o.data
    nf = len(me.polygons)
    mi = np.empty(nf, np.int32)
    me.polygons.foreach_get("material_index", mi)
    slots = [s.material.name if s.material else "" for s in o.material_slots]
    uvl = me.uv_layers.active
    ls = np.empty(nf, np.int32)
    lt = np.empty(nf, np.int32)
    me.polygons.foreach_get("loop_start", ls)
    me.polygons.foreach_get("loop_total", lt)
    uv = np.empty(len(me.loops) * 2, np.float32)
    uvl.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    rules = RULES.get(name, {})
    cls = np.empty(nf, dtype=object)
    for f in range(nf):
        sm = slots[mi[f]] if mi[f] < len(slots) else ""
        rule = rules.get(sm)
        if rule is None:
            rule = PYLON_DEFAULT_TRIM if (name.startswith("SM_Pylon") and sm.startswith("M_Light")) else "Desk"
        if isinstance(rule, tuple):
            a = base_image_of(sm)
            pts = uv[ls[f]: ls[f] + lt[f]]
            if a is None:
                cls[f] = rule[1] if "Light" in sm else rule[2]
                continue
            cen = pts.mean(0)
            samp = [sample(a, cen[0], cen[1])] + [sample(a, *(cen + 0.6 * (p - cen))) for p in pts]
            col = np.mean(samp, 0)
            if is_gold(col):
                cls[f] = rule[1]
                gold_pts.extend([s for s in samp if is_gold(s) and s[0] > 0.6])
            else:
                cls[f] = rule[2]
        else:
            cls[f] = rule
    face_class[name] = cls
    log(name, {c: int((cls == c).sum()) for c in set(cls)})

# front-facing test for the desk screen (SM_Table_Screen): only +X (camera side) faces are the screen
o = meshes["SM_Table_Screen"]
cls = face_class["SM_Table_Screen"]
mw3 = o.matrix_world.to_3x3()
for f, p in enumerate(o.data.polygons):
    if (mw3 @ p.normal).normalized().x < 0.35:
        cls[f] = "Desk"
log("desk screen faces:", int((cls == "ScreenDesk").sum()), "of", len(cls))

# ----------------------------------------------------------------------------------------------
# 4. assign classes, fix UVs
# ----------------------------------------------------------------------------------------------
CLASS_ORDER = ["Floor", "Desk", "DeskGlass", "Wall", "Backdrop", "Truss", "Lamp", "LampLens", "ScreenWall", "ScreenDesk", "Trim"]
CLASS_MATS = {}   # filled later


def screen_uv_fix(o, faces_mask, view_dir_world):
    """normalise UVs of the masked faces to 0..1 and orient them for a viewer looking along view_dir"""
    me = o.data
    nf = len(me.polygons)
    ls = np.empty(nf, np.int32)
    lt = np.empty(nf, np.int32)
    me.polygons.foreach_get("loop_start", ls)
    me.polygons.foreach_get("loop_total", lt)
    uvl = me.uv_layers.active
    uv = np.empty(len(me.loops) * 2, np.float32)
    uvl.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    vidx = np.empty(len(me.loops), np.int32)
    me.loops.foreach_get("vertex_index", vidx)
    idx = np.concatenate([np.arange(ls[f], ls[f] + lt[f]) for f in range(nf) if faces_mask[f]])
    sub = uv[idx]
    mn, mx = sub.min(0), sub.max(0)
    span = np.maximum(mx - mn, 1e-6)
    newuv = (sub - mn) / span
    # orientation: right vector for the viewer, up = +Z
    mw = o.matrix_world
    co = np.array([(mw @ me.vertices[v].co)[:] for v in vidx[idx]])
    right = np.cross(np.array(view_dir_world), np.array([0, 0, 1.0]))
    right /= np.linalg.norm(right)
    cu = np.corrcoef(newuv[:, 0], co @ right)[0, 1]
    cv = np.corrcoef(newuv[:, 1], co[:, 2])[0, 1]
    if cu < 0:
        newuv[:, 0] = 1 - newuv[:, 0]
    if cv < 0:
        newuv[:, 1] = 1 - newuv[:, 1]
    log(o.name, "screen uv corr right/up", round(float(cu), 2), round(float(cv), 2), "flip", cu < 0, cv < 0)
    uv[idx] = np.clip(newuv, 0, 1)
    uvl.data.foreach_set("uv", uv.ravel())


VIEW_DIR = (-1.0, 0.0, 0.0)   # source frame: the camera sits at +X and looks towards -X
staged = {}
for name, o in meshes.items():
    me = o.data
    cls = face_class[name]
    # trim faces: collapse UV to the centre of the flat gold texture
    nf = len(me.polygons)
    ls = np.empty(nf, np.int32)
    lt = np.empty(nf, np.int32)
    me.polygons.foreach_get("loop_start", ls)
    me.polygons.foreach_get("loop_total", lt)
    uvl = me.uv_layers.active
    uv = np.empty(len(me.loops) * 2, np.float32)
    uvl.data.foreach_get("uv", uv)
    uv = uv.reshape(-1, 2)
    for f in range(nf):
        if cls[f] in ("Trim", "LampLens"):
            if cls[f] == "Trim":
                uv[ls[f]: ls[f] + lt[f]] = 0.5
    uvl.data.foreach_set("uv", uv.ravel())
    for scr in ("ScreenWall", "ScreenDesk"):
        m = cls == scr
        if m.any():
            screen_uv_fix(o, m, VIEW_DIR)
    used = [c for c in CLASS_ORDER if (cls == c).any()]
    staged[name] = (used, cls)

# ----------------------------------------------------------------------------------------------
# 5. textures + materials
# ----------------------------------------------------------------------------------------------
def src_img(name):
    im = bpy.data.images.get(name)
    return img_px(im) if (im and have_tex and im.size[0] > 0) else None


# class -> (base src, rough src, metallic, base gain rgb, base lift, rough const, normal strength, size)
GOLD = None
if gold_pts:
    g = np.array(gold_pts)
    GOLD = np.median(g, 0)
else:
    GOLD = np.array([0.95, 0.78, 0.30])
log("gold trim colour", GOLD)

SPEC = {
    "Floor": dict(base="Map #18", rough="GSG_TechProducts_Glossy_Plastic_01_roughness.jpg", metal=0.0, gain=(1.6, 1.6, 1.7), nstr=1.5, size=1024),
    "Desk": dict(base="Map #15", rough="GSG_TechProducts_Plastic_01_roughness.jpg", metal=0.0, gain=(1.5, 1.5, 1.55), nstr=1.5, size=1024),
    "Wall": dict(base="Map #24", rough="D_GreyPlasticScratched_ROUGH.jpg", metal=0.0, gain=(0.75, 0.76, 0.8), nstr=2.0, size=1024),
    "Backdrop": dict(base="Map #18", rough="GSG_TechProducts_Glossy_Plastic_01_roughness.jpg", metal=0.0, gain=(1.9, 2.0, 2.4), nstr=1.0, size=1024),
    "Truss": dict(base="Map #21", rough=None, rconst=0.35, metal=0.9, gain=(0.8, 0.8, 0.82), nstr=0.5, size=1024),
    "Lamp": dict(base="Map #15", rough="GSG_TechProducts_Plastic_01_roughness.jpg", metal=0.2, gain=(1.2, 1.2, 1.25), nstr=0.5, size=512),
    "LampLens": dict(base="Light 07.jpg", rough=None, rconst=0.15, metal=0.0, gain=(1, 1, 1), nstr=0.0, size=256),
    "DeskGlass": dict(flat=(0.10, 0.14, 0.17), rconst=0.05, metal=0.0, alpha=0.30),
    "ScreenWall": dict(flat=(0.02, 0.02, 0.03), rconst=0.12, metal=0.0),
    "ScreenDesk": dict(flat=(0.02, 0.02, 0.03), rconst=0.12, metal=0.0),
    "Trim": dict(flat=tuple(GOLD), rconst=0.30, metal=0.8),
}
mats = {}
for cname in CLASS_ORDER:
    sp = SPEC[cname]
    name = "PIX_News_" + cname
    if "flat" in sp:
        S = 64
        base = np.ones((S, S, 4), np.float32)
        base[..., :3] = np.array(sp["flat"], np.float32)
        base[..., 3] = sp.get("alpha", 1.0)
        nrm = normal_from_height(np.zeros((S, S), np.float32), 0)
        smooth = 1.0 - sp["rconst"]
        mask = np.zeros((S, S, 4), np.float32)
        mask[..., 0] = sp["metal"]
        mask[..., 1] = 1.0
        mask[..., 3] = smooth
    else:
        b = src_img(sp["base"])
        if b is None:
            b = np.full((64, 64, 4), 0.2, np.float32)
        b = downscale(b, sp["size"]).copy()
        b[..., :3] = np.clip(b[..., :3] * np.array(sp["gain"], np.float32), 0, 1)
        b[..., 3] = 1.0
        base = b
        S = base.shape[0]
        lum = base[..., :3].mean(2)
        nrm = normal_from_height(lum, sp["nstr"] * 4.0) if sp["nstr"] > 0 else normal_from_height(np.zeros(lum.shape, np.float32), 0)
        mask = np.zeros(base.shape, np.float32)
        mask[..., 0] = sp["metal"]
        mask[..., 1] = 1.0
        r = src_img(sp["rough"]) if sp.get("rough") else None
        if r is not None:
            r = downscale(r, sp["size"])
            rr = r[..., 0]
            if rr.shape != lum.shape:
                rr = np.resize(rr, lum.shape)
            mask[..., 3] = 1.0 - rr
        else:
            mask[..., 3] = 1.0 - sp.get("rconst", 0.5)
    save_png(name + "_BaseColor.png", base, "sRGB")
    save_png(name + "_Normal.png", nrm, "Non-Color")
    save_png(name + "_Mask.png", mask, "Non-Color")

    # Blender material (Principled + the three PNGs) so FBX links BaseColor/Normal and previews match
    if name in bpy.data.materials:
        bpy.data.materials.remove(bpy.data.materials[name])
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    out.location = (700, 0)
    bsdf = nt.nodes.new("ShaderNodeBsdfPrincipled")
    bsdf.location = (400, 0)
    nt.links.new(bsdf.outputs["BSDF"], out.inputs["Surface"])

    def tex(fname, cs, loc):
        n = nt.nodes.new("ShaderNodeTexImage")
        n.image = bpy.data.images[fname]
        n.image.colorspace_settings.name = cs
        n.location = loc
        return n

    tb = tex(name + "_BaseColor.png", "sRGB", (-300, 300))
    tn = tex(name + "_Normal.png", "Non-Color", (-300, 0))
    tm = tex(name + "_Mask.png", "Non-Color", (-300, -300))
    nt.links.new(tb.outputs["Color"], bsdf.inputs["Base Color"])
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nm.location = (100, 0)
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"])
    nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    sep.location = (-100, -300)
    nt.links.new(tm.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = "SUBTRACT"
    inv.inputs[0].default_value = 1.0
    inv.location = (100, -300)
    nt.links.new(tm.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])
    if "alpha" in sp:
        bsdf.inputs["Alpha"].default_value = sp["alpha"]
    if cname.startswith("Screen"):
        bsdf.inputs["Emission Color"].default_value = (0.02, 0.02, 0.03, 1)
        bsdf.inputs["Emission Strength"].default_value = 0.0
    nt.nodes.active = tb
    m.diffuse_color = (*[float(x) for x in (sp.get("flat") or (0.3, 0.3, 0.3))], 1.0)
    mats[cname] = m

# assign classes to meshes
for name, o in meshes.items():
    used, cls = staged[name]
    me = o.data
    me.materials.clear()
    for c in used:
        me.materials.append(mats[c])
    mi = np.array([used.index(c) for c in cls], np.int32)
    me.polygons.foreach_set("material_index", mi)
    me.update()

# ----------------------------------------------------------------------------------------------
# 6. bake transforms into the meshes, build the floor, join, rename
# ----------------------------------------------------------------------------------------------
wall = meshes["SM_Wall_01"]
bot = sorted([(wall.matrix_world @ v.co) for v in wall.data.vertices if abs((wall.matrix_world @ v.co).z) < 0.01], key=lambda v: v.y)

for name, o in meshes.items():
    mw = o.matrix_world.copy()
    o.data.transform(mw)
    if mw.determinant() < 0:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
        bm.to_mesh(o.data)
        bm.free()
    o.matrix_world = Matrix.Identity(4)
    o.parent = None
    o.data.update()

# floor polygon (source frame): back = wall base curve, front = open towards the camera
fl_pts = [(v.x, v.y) for v in bot] + [(11.5, bot[-1].y), (11.5, bot[0].y)]
bm = bmesh.new()
vs = [bm.verts.new((x, y, -0.002)) for x, y in fl_pts]
bm.faces.new(vs)
bmesh.ops.triangulate(bm, faces=bm.faces[:], quad_method="BEAUTY", ngon_method="EAR_CLIP")
for f in bm.faces:
    if f.normal.z < 0:
        f.normal_flip()
uvl = bm.loops.layers.uv.new("UVMap")
for f in bm.faces:
    for l in f.loops:
        l[uvl].uv = (l.vert.co.x / 1.5, l.vert.co.y / 1.5)
fme = bpy.data.meshes.new("PIX_News_Floor")
bm.to_mesh(fme)
bm.free()
fme.materials.append(mats["Floor"])
floor = bpy.data.objects.new("PIX_News_Floor", fme)
sc.collection.objects.link(floor)

NAMES = {
    "SM_Stage": "PIX_News_StageCurb", "SM_Base": "PIX_News_Podium", "SM_Table": "PIX_News_DeskBody",
    "SM_Table_Screen": "PIX_News_DeskScreen", "SM_Glass": "PIX_News_DeskGlass", "SM_Clamps": "PIX_News_DeskClamps",
    "SM_Screen": "PIX_News_VideoWall", "SM_Wall_01": "PIX_News_WallInner", "SM_Wall_02": "PIX_News_Backdrop",
    "SM_Sill": "PIX_News_Sill", "SM_Tube": "PIX_News_TrussRing", "SM_Cap": "PIX_News_TrussCap", "SM_Strips": "PIX_News_TrussStrips",
}
for k, v in NAMES.items():
    meshes[k].name = v
    meshes[k].data.name = v


def join(objs, newname):
    objs = [o for o in objs if o]
    act = objs[0]
    with bpy.context.temp_override(object=act, active_object=act, selected_objects=objs, selected_editable_objects=objs):
        bpy.ops.object.join()
    act.name = newname
    act.data.name = newname
    return act


join([meshes["SM_Pylon_%02d" % i] for i in range(1, 7)], "PIX_News_Pylons")
join(lamps, "PIX_News_Lamps")

# ----------------------------------------------------------------------------------------------
# 7. origin at the host spot (floor), set faces -Y
# ----------------------------------------------------------------------------------------------
M = Matrix.Rotation(math.radians(-90), 4, "Z") @ Matrix.Translation((-HOST_ORIG[0], -HOST_ORIG[1], 0.0))
final_objs = [o for o in bpy.data.objects if o.type == "MESH"]
for o in final_objs:
    o.data.transform(M)
    o.data.update()
    # simple smooth shading by angle would need edits; leave source shading, recompute nothing

# ----------------------------------------------------------------------------------------------
# 8. stats
# ----------------------------------------------------------------------------------------------
tot = 0
per = {}
for o in final_objs:
    t = sum(len(p.vertices) - 2 for p in o.data.polygons)
    per[o.name] = t
    tot += t
log("TRIS", tot, per)
bb_min = Vector((1e9,) * 3)
bb_max = Vector((-1e9,) * 3)
for o in final_objs:
    for v in o.data.vertices:
        for i in range(3):
            bb_min[i] = min(bb_min[i], v.co[i])
            bb_max[i] = max(bb_max[i], v.co[i])
log("BBOX min", tuple(round(x, 2) for x in bb_min), "max", tuple(round(x, 2) for x in bb_max))


def desk_info():
    for nme in ("PIX_News_DeskBody", "PIX_News_DeskScreen", "PIX_News_DeskGlass", "PIX_News_Podium"):
        o = bpy.data.objects[nme]
        vs = np.array([v.co[:] for v in o.data.vertices])
        log("BB", nme, np.round(vs.min(0), 2), np.round(vs.max(0), 2), "centre", np.round((vs.min(0) + vs.max(0)) / 2, 2))


desk_info()

# ----------------------------------------------------------------------------------------------
# 9. export FBX
# ----------------------------------------------------------------------------------------------
for o in bpy.data.objects:
    o.select_set(o.type == "MESH")
bpy.ops.export_scene.fbx(
    filepath=FBX_OUT, use_selection=True, object_types={"MESH"},
    axis_forward="-Z", axis_up="Y", apply_unit_scale=True, apply_scale_options="FBX_SCALE_NONE",
    bake_space_transform=True, global_scale=1.0, path_mode="STRIP", embed_textures=False,
    mesh_smooth_type="FACE", use_mesh_modifiers=True, add_leaf_bones=False, use_tspace=False,
    bake_anim=False, use_custom_props=False)
log("exported", FBX_OUT, os.path.getsize(FBX_OUT))

# ----------------------------------------------------------------------------------------------
# 10. previews
# ----------------------------------------------------------------------------------------------
sc.render.engine = "BLENDER_WORKBENCH"
sc.render.resolution_x, sc.render.resolution_y = RES["w"], RES["h"]
sc.render.film_transparent = False
sh = sc.display.shading
sh.light = "STUDIO"
sh.color_type = "TEXTURE"
sh.show_backface_culling = True
sh.background_type = "VIEWPORT"
sh.background_color = (0.30, 0.32, 0.36)
sc.display.render_aa = "8"
sh.show_cavity = False
sh.show_shadows = False

cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
sc.collection.objects.link(cam)
sc.camera = cam
cam.data.clip_start = 0.05
cam.data.clip_end = 100


def shot(fname, loc, tgt, lens):
    cam.location = loc
    cam.rotation_euler = (Vector(tgt) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = lens
    sc.render.filepath = os.path.join(RENDERS, fname)
    bpy.ops.render.render(write_still=True)


# camera positions in the BLENDER frame of the exported set (host at origin, set faces -Y)
CAMS = {
    "front_wide": dict(loc=(0.0, -9.0, 1.65), tgt=(0.0, 2.0, 1.45), lens=24),
    "three_quarter": dict(loc=(3.4, -6.2, 1.7), tgt=(-0.3, 0.0, 1.35), lens=35),
}
for k, c in CAMS.items():
    shot("news_%s.png" % k, c["loc"], c["tgt"], c["lens"])
shot("news_overview_high.png", (6.5, -11.0, 5.5), (0.0, 0.5, 1.3), 28)
shot("news_desk_close.png", (1.2, -4.2, 1.5), (0.0, -1.5, 1.0), 40)
shot("news_screen_wall.png", (0.0, -4.5, 1.7), (0.0, 3.0, 1.9), 35)
shot("news_back_view.png", (-3.0, 8.5, 3.5), (0.0, -1.0, 1.0), 26)

# map from Blender frame to Unity frame: Unity = (-x, z, -y)
for k, c in CAMS.items():
    x, y, z = c["loc"]
    tx, ty, tz = c["tgt"]
    log("CAM", k, "blender", c["loc"], "-> unity", (round(-x, 2), round(z, 2), round(-y, 2)),
        "look-at unity", (round(-tx, 2), round(tz, 2), round(-ty, 2)), "lens", c["lens"])

# ----------------------------------------------------------------------------------------------
# 11. contact sheets (sources + final)
# ----------------------------------------------------------------------------------------------
def contact(images, path, cell=420, cols=4, gamma=1.0):
    rows = (len(images) + cols - 1) // cols
    sheet = np.full((rows * cell, cols * cell, 4), 0.25, np.float32)
    sheet[..., 3] = 1
    for k, a in enumerate(images):
        a = a.copy()
        a[..., :3] = np.clip(a[..., :3], 0, 1) ** gamma
        h, w = a.shape[:2]
        s = min((cell - 8) / w, (cell - 8) / h)
        nw, nh = max(1, int(w * s)), max(1, int(h * s))
        ys = (np.arange(nh) / s).astype(int).clip(0, h - 1)
        xs = (np.arange(nw) / s).astype(int).clip(0, w - 1)
        r, c = divmod(k, cols)
        r = rows - 1 - r
        sheet[r * cell + 4: r * cell + 4 + nh, c * cell + 4: c * cell + 4 + nw, 3] = 1
        sheet[r * cell + 4: r * cell + 4 + nh, c * cell + 4: c * cell + 4 + nw, :3] = a[ys][:, xs][..., :3]
    save_png(os.path.basename(path), sheet, "sRGB")
    # save_png writes into OUT; move to renders
    import shutil
    shutil.move(os.path.join(OUT, os.path.basename(path)), path)


final = []
for cname in CLASS_ORDER:
    for suf in ("BaseColor", "Normal", "Mask"):
        im = bpy.data.images["PIX_News_%s_%s.png" % (cname, suf)]
        a = img_px(im)
        if suf == "Mask":
            a = a.copy()
            a[..., 3] = 1
        final.append(a)
contact(final, os.path.join(RENDERS, "news_textures_final_contact.png"), cell=300, cols=6)
if have_tex:
    srcs = [img_px(i) for i in bpy.data.images if i.packed_file and i.name not in
            [n for n in bpy.data.images.keys() if n.startswith("PIX_News")]]
    contact(srcs, os.path.join(RENDERS, "news_textures_source_contact.png"), cell=420, cols=5, gamma=0.5)
# the temp sheet images do not belong in the output folder
for f in os.listdir(OUT):
    if f.startswith("news_textures"):
        os.remove(os.path.join(OUT, f))
log("DONE")
