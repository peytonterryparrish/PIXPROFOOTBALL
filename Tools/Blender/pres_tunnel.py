"""
pres_tunnel.py - build the PIX team walk-out inflatable tunnel for Unity.

Run (headless, Blender 5.2):
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python pres_tunnel.py

Source  : D:/PIXFootball/Source/PPF_NEW/tunnel/RETRACTABLE_TUNNEL.obj (user-owned, Cinema4D export, cm)
Output  : Assets/_Licensed/PIX/Presentation/Tunnel/PIX_Tunnel.fbx (+ PIX_TunnelBlack_BaseColor/_Normal.png,
          PIX_TunnelLogo_placeholder.png) and previews in D:/PIXFootball/Renders/v070_d/tunnel_*.png

What it does
  * imports the OBJ, drops the stacked duplicate meshes (the export has ~3x copies of everything at the same
    place: 424 objects -> 62 distinct), DELETES every brand carrier (LOGOS*, TUNNEL_END_LOGO*, Cap_2 overlay).
  * scale 0.01 -> metres, Z-up, tunnel along +Y, field mouth faces -Y at y=0, origin = floor centre of the mouth.
  * decimates the frame / wheels / canvas, merges to 7 meshes with 5 materials, makes UVs, sharp-edge smoothing.
  * adds Logo_Plate_Left/Right/End (flat quads, material PIX_TunnelLogo) and a ceiling light strip.
  * exports the FBX (axis_forward -Z, axis_up Y, apply_unit_scale, bake_space_transform), re-imports it to verify,
    renders previews, and scans the output for brand strings.
"""
import bpy, bmesh, hashlib, math, os, re, sys
import numpy as np
from mathutils import Vector, Matrix

# ----------------------------------------------------------------------------------------------------------
# config
# ----------------------------------------------------------------------------------------------------------
SRC_OBJ = r"D:\PIXFootball\Source\PPF_NEW\tunnel\RETRACTABLE_TUNNEL.obj"
SRC_TEX = r"D:\PIXFootball\Source\PPF_NEW\tunnel\RETRACTABLE_TUNNEL\tex"
OUT_DIR = r"C:\Users\peyto\Desktop\PeytonParrishGames\PIX PRO FOOTBALL\Assets\_Licensed\PIX\Presentation\Tunnel"
RENDER_DIR = r"D:\PIXFootball\Renders\v070_d"
FBX_NAME = "PIX_Tunnel.fbx"

MODEL_SCALE = 0.01          # source is centimetres -> metres. (Real inflatables are smaller; change here to shrink.)
RATIO_HOOP = 0.40           # 5980-tri hoops
RATIO_TUBE = 0.50           # scissor lattice bars / pins (444..1024 tris)
WHEEL_TRIS = 360            # per wheel (source 2880)
CANVAS_DISSOLVE_DEG = 0.05
SOLIDIFY_T = 0.02           # canvas skin thickness (m) -> inner surface so it also renders from inside
SHARP_DEG = 38.0
LINTEL_MATERIAL = "PIX_TunnelCanvas"   # source calls these 'Cap'; they are the skin header panels at the end.

BANNED = re.compile(r"adidas|sheffield|united|nike|nfl|reebok|puma|umbro|lotto|kappa|fila|joma|macron|errea|"
                    r"hummel|under.?armour|new.?balance|mizuno|canterbury|\bfc\b|logos?_copy", re.I)

os.makedirs(OUT_DIR, exist_ok=True)
os.makedirs(RENDER_DIR, exist_ok=True)


def log(*a):
    print("[tunnel]", *a)
    sys.stdout.flush()


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


# ----------------------------------------------------------------------------------------------------------
# 1. import + dedupe + delete brand carriers
# ----------------------------------------------------------------------------------------------------------
bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene
sc.unit_settings.system = 'METRIC'
sc.unit_settings.scale_length = 1.0
bpy.ops.wm.obj_import(filepath=SRC_OBJ)
log("imported", len(bpy.data.objects), "objects")
bpy.context.view_layer.update()

meshes = [o for o in bpy.data.objects if o.type == 'MESH']


def raw_key(o):
    n = len(o.data.vertices)
    a = np.empty(n * 3, np.float32)
    o.data.vertices.foreach_get('co', a)
    return hashlib.md5(np.round(a, 1).tobytes() + str(len(o.data.polygons)).encode()).hexdigest()


def world_bbox(o):
    pts = np.array([(o.matrix_world @ Vector(c))[:] for c in o.bound_box])
    return pts.min(0), pts.max(0)


seen = {}
kept = []
dropped_dupes = 0
removed_brand = []
for o in sorted(meshes, key=lambda x: x.name):
    nm = o.name
    if re.match(r"^(LOGOS|TUNNEL_END_LOGO)", nm):
        removed_brand.append(nm)
        bpy.data.objects.remove(o, do_unlink=True)
        continue
    k = raw_key(o)
    if k in seen:
        dropped_dupes += 1
        bpy.data.objects.remove(o, do_unlink=True)
        continue
    seen[k] = o
    kept.append(o)
log("brand carriers removed:", len(removed_brand), "stacked duplicates dropped:", dropped_dupes, "distinct kept:", len(kept))

# Cap pieces by geometry (world y, cm): 3127 = inner panel (faces -Y), 3128 = outer panel (faces +Y),
# 3129 = logo overlay (3 pieces, brand carrier) -> delete.
final = []
for o in kept:
    if o.name.startswith("Cap"):
        mn, mx = world_bbox(o)
        yc = (mn[1] + mx[1]) / 2
        if yc > 3128.5:  # overlay at 3129
            removed_brand.append(o.name)
            bpy.data.objects.remove(o, do_unlink=True)
            continue
    final.append(o)
kept = final
log("kept after cap filtering:", len(kept))

# ----------------------------------------------------------------------------------------------------------
# 2. classify + reference frame
# ----------------------------------------------------------------------------------------------------------
def classify(o):
    n = o.name
    if n.startswith("CANVAS"):
        return "canvas"
    if n.startswith("WHEEL"):
        return "wheel"
    if n.startswith("Cap"):
        return "lintel"
    if tri_count(o) >= 5000:
        return "hoop"
    return "tube"  # scissor lattice bars, rails, pins ('1','1.1','Cylinder.2' ...)


cls = {o: classify(o) for o in kept}
from collections import Counter
log("classes", dict(Counter(cls.values())))

# reference: mouth = min y of the hoops, floor = min z of wheels, x centre = canvas centre
hoop_y = min(world_bbox(o)[0][1] for o in kept if cls[o] == "hoop")
floor_z = min(world_bbox(o)[0][2] for o in kept if cls[o] == "wheel")
cmn, cmx = [world_bbox(o) for o in kept if cls[o] == "canvas"][0]
x_c = (cmn[0] + cmx[0]) / 2
log("ref mouth_y(cm)=%.1f floor_z(cm)=%.1f x_c=%.2f" % (hoop_y, floor_z, x_c))
XFORM = Matrix.Scale(MODEL_SCALE, 4) @ Matrix.Translation((-x_c, -hoop_y, -floor_z))


def evaluated_copy(o, modifiers):
    """add modifiers [(type, dict)], return new mesh datablock evaluated in object space"""
    for typ, props in modifiers:
        m = o.modifiers.new(typ.title(), typ)
        for k, v in props.items():
            setattr(m, k, v)
    dg = bpy.context.evaluated_depsgraph_get()
    eo = o.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(eo)
    for m in list(o.modifiers):
        o.modifiers.remove(m)
    return me


def to_bm(me, matrix=None):
    bm = bmesh.new()
    bm.from_mesh(me)
    if matrix is not None:
        bmesh.ops.transform(bm, matrix=matrix, verts=bm.verts)
    return bm


# ----------------------------------------------------------------------------------------------------------
# 3. per-object cleaning, orientation, decimation -> per-group bmesh (in metre / final space)
# ----------------------------------------------------------------------------------------------------------
G = {"canvas": bmesh.new(), "frame": bmesh.new(), "black": bmesh.new()}
stats = Counter()


def append_bm(dst, src_me):
    dst_me_tmp = src_me
    dst.from_mesh(dst_me_tmp)  # bmesh.from_mesh appends to existing content


def clean_closed(bm):
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)


for o in kept:
    c = cls[o]
    src_tris = tri_count(o)
    # object space -> world (cm, still rotated Z-up) -> bake into mesh so modifiers act in final space
    bm = to_bm(o.data, XFORM @ o.matrix_world)
    if c in ("hoop", "tube", "wheel"):
        clean_closed(bm)
    me = bpy.data.meshes.new("tmp_" + o.name)
    bm.to_mesh(me)
    bm.free()
    for uvl in list(me.uv_layers):
        me.uv_layers.remove(uvl)
    tmp = bpy.data.objects.new("tmp_" + o.name, me)
    sc.collection.objects.link(tmp)

    if c == "hoop":
        out = evaluated_copy(tmp, [("DECIMATE", dict(decimate_type='COLLAPSE', ratio=RATIO_HOOP))])
        dst = "frame"
    elif c == "tube":
        out = evaluated_copy(tmp, [("DECIMATE", dict(decimate_type='COLLAPSE', ratio=RATIO_TUBE))])
        dst = "frame"
    elif c == "wheel":
        out = evaluated_copy(tmp, [("DECIMATE", dict(decimate_type='COLLAPSE', ratio=min(1.0, WHEEL_TRIS / max(1, src_tris))))])
        dst = "black"
    elif c == "canvas":
        # planar dissolve of the long flat/cylindrical strips, keep the rounded shoulders
        out = evaluated_copy(tmp, [("DECIMATE", dict(decimate_type='DISSOLVE', angle_limit=math.radians(CANVAS_DISSOLVE_DEG),
                                                       use_dissolve_boundaries=False))])
        dst = "canvas"
    else:  # lintel panels, keep as is (single ngons -> triangulate below)
        out = me
        dst = "canvas" if LINTEL_MATERIAL == "PIX_TunnelCanvas" else "black"
    if c in ("hoop", "tube", "wheel"):
        vb = bmesh.new()
        vb.from_mesh(out)
        bmesh.ops.triangulate(vb, faces=vb.faces[:])
        vol = sum(f.verts[0].co.dot(f.verts[1].co.cross(f.verts[2].co)) for f in vb.faces) / 6.0
        vb.free()
        stats["negvol"] += 1 if vol < 0 else 0
    G[dst].from_mesh(out)
    stats[c] += len(out.polygons) if out is me else sum(len(p.vertices) - 2 for p in out.polygons)
    stats[c + "_src"] += src_tris
    bpy.data.objects.remove(tmp, do_unlink=True)
log("pieces with inverted (negative-volume) normals:", stats["negvol"])
log("decimation stats (tris after / before)", {k: (stats[k], stats[k + "_src"]) for k in ["hoop", "tube", "wheel", "canvas", "lintel"]})

# --- canvas: triangulate, orient outward (radial from the tunnel axis), then solidify inward for double side
canvas_bm = G["canvas"]
bmesh.ops.triangulate(canvas_bm, faces=canvas_bm.faces[:])
# lintel pieces must face +/-Y, canvas sheet must face away from axis (x=0, z~3.3)
axis_c = Vector((0.0, 0.0, 3.3))
flip = []
canvas_bm.normal_update()
_ly = [f.calc_center_median().y for f in canvas_bm.faces if abs(f.normal.y) > 0.99]
LINTEL_MID = (min(_ly) + max(_ly)) / 2
for f in canvas_bm.faces:
    n = f.normal
    cen = f.calc_center_median()
    if abs(n.y) > 0.99:  # lintel plane: inner panel (smaller y) must face -Y, outer (larger y) +Y
        if cen.y < LINTEL_MID and n.y > 0:
            flip.append(f)
        elif cen.y >= LINTEL_MID and n.y < 0:
            flip.append(f)
    else:
        r = Vector((cen.x, 0.0, cen.z)) - axis_c
        if n.dot(r) < 0:
            flip.append(f)
bmesh.ops.reverse_faces(canvas_bm, faces=flip)
canvas_bm.faces.index_update()
log("canvas faces flipped:", len(flip), "lintel mid y", round(LINTEL_MID, 3))

# split canvas sheet vs lintel before solidify
sheet_bm = bmesh.new()
lintel_bm = bmesh.new()
tmp_all = bpy.data.meshes.new("canvas_all")
canvas_bm.to_mesh(tmp_all)
sheet_faces = [p.index for p in tmp_all.polygons if abs(p.normal.y) <= 0.99]
lintel_faces = [p.index for p in tmp_all.polygons if abs(p.normal.y) > 0.99]
bmesh.ops.delete(canvas_bm, geom=[f for f in canvas_bm.faces if f.index in set(lintel_faces)], context='FACES')
canvas_bm.faces.index_update()
sheet_me = bpy.data.meshes.new("canvas_sheet")
canvas_bm.to_mesh(sheet_me)
so = bpy.data.objects.new("canvas_sheet", sheet_me)
sc.collection.objects.link(so)
solid_me = evaluated_copy(so, [("SOLIDIFY", dict(thickness=SOLIDIFY_T, offset=-1.0, use_rim=True, use_even_offset=True,
                                                  use_quality_normals=True))])
bpy.data.objects.remove(so, do_unlink=True)
canvas_final = bmesh.new()
canvas_final.from_mesh(solid_me)
# lintel panels (two faces, one each direction)
lint_me = bpy.data.meshes.new("lintel")
l_bm = bmesh.new()
l_bm.from_mesh(tmp_all)
bmesh.ops.delete(l_bm, geom=[f for f in l_bm.faces if f.index not in set(lintel_faces)], context='FACES')
l_bm.to_mesh(lint_me)
canvas_final.from_mesh(lint_me)
l_bm.free()
G["canvas"] = canvas_final
log("canvas tris (sheet+shell+lintel):", len(canvas_final.faces))

# --- black: floor mat (one quad, black leather)
bm_b = G["black"]
mat_x, mat_y0, mat_y1, mat_z = 2.40, 0.25, 13.40, 0.012
vs = [bm_b.verts.new(p) for p in [(-mat_x, mat_y0, mat_z), (mat_x, mat_y0, mat_z), (mat_x, mat_y1, mat_z), (-mat_x, mat_y1, mat_z)]]
bm_b.faces.new(vs)  # CCW seen from +Z -> normal up
bm_b.verts.ensure_lookup_table()

# --- frame: nothing more. Light strip + plates are separate objects (below)

# ----------------------------------------------------------------------------------------------------------
# 4. shading, UVs
# ----------------------------------------------------------------------------------------------------------
def box_uv(bm, scale=1.0):
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for l in f.loops:
            p = l.vert.co
            if ax == 0:
                u, v = p.y, p.z
            elif ax == 1:
                u, v = p.x, p.z
            else:
                u, v = p.x, p.y
            l[uv].uv = (u * scale, v * scale)


def canvas_uv(bm):
    """cylindrical: u = angle around the tunnel axis, v = distance along the tunnel (normalised 0..1)"""
    uv = bm.loops.layers.uv.verify()
    ys = [v.co.y for v in bm.verts]
    y0, y1 = min(ys), max(ys)
    for f in bm.faces:
        n = f.normal
        for l in f.loops:
            p = l.vert.co
            if abs(n.y) > 0.99:  # lintel
                u = (p.x + 2.75) / 5.5
                v = (p.z - 5.74) / 0.95
                l[uv].uv = (u, 0.9 + 0.1 * v)
            else:
                a = math.atan2(p.x, p.z - 3.3)
                l[uv].uv = ((a + math.pi) / (2 * math.pi), (p.y - y0) / (y1 - y0))


def finish(bm, name, uvfn, mat):
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bm.normal_update()
    # sharp edges by angle, smooth faces
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) == 2:
            ang = e.calc_face_angle(0.0)
            e.smooth = ang < math.radians(SHARP_DEG)
        else:
            e.smooth = False
    uvfn(bm)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    sc.collection.objects.link(ob)
    # custom split normals come from sharp edges in 4.1+/5.x automatically
    return ob


# ----------------------------------------------------------------------------------------------------------
# 5. images + materials
# ----------------------------------------------------------------------------------------------------------
def load_resized(src, dst_name, size=1024, colorspace='sRGB'):
    """load a jpg, resample to size x size (bilinear, wraps so the seamless tile stays seamless), save PNG"""
    s = bpy.data.images.load(src, check_existing=False)
    w, h = s.size
    a = np.empty(w * h * 4, np.float32)
    s.pixels.foreach_get(a)
    a = a.reshape(h, w, 4)
    xs = np.arange(size) * (w / size)
    ys = np.arange(size) * (h / size)
    x0 = np.floor(xs).astype(int) % w
    x1 = (x0 + 1) % w
    fx = (xs - np.floor(xs))[None, :, None]
    y0 = np.floor(ys).astype(int) % h
    y1 = (y0 + 1) % h
    fy = (ys - np.floor(ys))[:, None, None]
    top = a[y0][:, x0] * (1 - fx) + a[y0][:, x1] * fx
    bot = a[y1][:, x0] * (1 - fx) + a[y1][:, x1] * fx
    out = (top * (1 - fy) + bot * fy).astype(np.float32)
    out[..., 3] = 1.0
    bpy.data.images.remove(s)
    img = bpy.data.images.new(dst_name, size, size, alpha=False)
    img.colorspace_settings.name = colorspace   # must be set BEFORE the pixels (changing it later clears the buffer)
    img.pixels.foreach_set(out.ravel())
    img.filepath_raw = os.path.join(OUT_DIR, dst_name)
    img.file_format = 'PNG'
    img.save()
    return img


def draw_placeholder(path):
    W, H = 1024, 512
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    img = np.zeros((H, W, 4), np.float32)
    bg = np.array([0.075, 0.085, 0.10, 1.0], np.float32)
    img[:] = bg
    # border
    m = 14
    border = (xx < m) | (xx > W - 1 - m) | (yy < m) | (yy > H - 1 - m)
    img[border] = [0.85, 0.85, 0.88, 1.0]
    inner = 30
    border2 = ((xx > inner) & (xx < inner + 4) | (xx < W - 1 - inner) & (xx > W - 5 - inner)) & (yy > inner) & (yy < H - inner)
    img[border2] = [0.30, 0.34, 0.40, 1.0]

    def seg(ax, ay, bx, by):
        px, py = xx - ax, yy - ay
        dx, dy = bx - ax, by - ay
        t = np.clip((px * dx + py * dy) / (dx * dx + dy * dy + 1e-9), 0, 1)
        return np.hypot(px - t * dx, py - t * dy)

    r = 30.0
    d = np.full((H, W), 1e9, np.float32)
    top, bot = 380.0, 132.0
    # P
    px0 = 215.0
    d = np.minimum(d, seg(px0, bot, px0, top))
    d = np.minimum(d, seg(px0, top, px0 + 70, top))
    d = np.minimum(d, seg(px0, 256, px0 + 70, 256))
    cx, cy, rad = px0 + 70, (top + 256) / 2, (top - 256) / 2
    pts = [(cx + rad * math.sin(a), cy + rad * math.cos(a)) for a in np.linspace(0, math.pi, 17)]
    for a, b in zip(pts[:-1], pts[1:]):
        d = np.minimum(d, seg(*a, *b))
    # I
    ix = 512.0
    d = np.minimum(d, seg(ix, bot, ix, top))
    # X
    x0, x1 = 640.0, 830.0
    d = np.minimum(d, seg(x0, bot, x1, top))
    d = np.minimum(d, seg(x0, top, x1, bot))
    a = np.clip(r - d + 0.5, 0, 1)[..., None]
    img[...] = img * (1 - a) + np.array([0.96, 0.96, 0.98, 1.0], np.float32) * a
    img[..., 3] = 1.0
    im = bpy.data.images.new("PIX_TunnelLogo_placeholder.png", W, H, alpha=False)
    im.colorspace_settings.name = 'sRGB'
    im.pixels.foreach_set(img.ravel())
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    return im


img_base = load_resized(os.path.join(SRC_TEX, "Seamless_black_shiny_fake_leather_texture_DIFFUSE.jpg"), "PIX_TunnelBlack_BaseColor.png")
img_norm = load_resized(os.path.join(SRC_TEX, "Seamless_black_shiny_fake_leather_texture_NORMAL.jpg"), "PIX_TunnelBlack_Normal.png", colorspace='Non-Color')
img_logo = draw_placeholder(os.path.join(OUT_DIR, "PIX_TunnelLogo_placeholder.png"))
log("textures written")


def new_mat(name, base, metallic=0.0, rough=0.5, diffuse=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (*base, 1.0)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*(diffuse or base), 1.0)
    return m, bsdf


def attach_image(m, bsdf, img, socket="Base Color"):
    nt = m.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    nt.links.new(tex.outputs["Color"], bsdf.inputs[socket])
    return tex


mat_canvas, b = new_mat("PIX_TunnelCanvas", (0.88, 0.88, 0.88), 0.0, 0.8)
mat_frame, b = new_mat("PIX_TunnelFrame", (0.07, 0.07, 0.08), 0.9, 0.38)
mat_black, b = new_mat("PIX_TunnelBlack", (0.04, 0.04, 0.045), 0.0, 0.5)
attach_image(mat_black, b, img_base)
ntex = mat_black.node_tree.nodes.new("ShaderNodeTexImage")
ntex.image = img_norm
nmap = mat_black.node_tree.nodes.new("ShaderNodeNormalMap")
mat_black.node_tree.links.new(ntex.outputs["Color"], nmap.inputs["Color"])
mat_black.node_tree.links.new(nmap.outputs["Normal"], b.inputs["Normal"])
mat_light, b = new_mat("PIX_TunnelLight", (1.0, 1.0, 1.0), 0.0, 0.5, diffuse=(1.0, 0.98, 0.9))
b.inputs["Emission Color"].default_value = (1.0, 0.97, 0.88, 1.0)
b.inputs["Emission Strength"].default_value = 2.0
mat_logo, b = new_mat("PIX_TunnelLogo", (1.0, 1.0, 1.0), 0.0, 0.55, diffuse=(0.3, 0.3, 0.33))
attach_image(mat_logo, b, img_logo)

# ----------------------------------------------------------------------------------------------------------
# 6. build final objects
# ----------------------------------------------------------------------------------------------------------
objs = []
objs.append(finish(G["canvas"], "Tunnel_Canvas", canvas_uv, mat_canvas))
objs.append(finish(G["frame"], "Tunnel_Frame", lambda bm: box_uv(bm, 1.0), mat_frame))
objs.append(finish(G["black"], "Tunnel_Black", lambda bm: box_uv(bm, 1.0), mat_black))


def quad_obj(name, corners, mat):
    """corners CCW seen from the facing side; UV 0..1 (bottom-left, bottom-right, top-right, top-left)"""
    bm = bmesh.new()
    vs = [bm.verts.new(c) for c in corners]
    f = bm.faces.new(vs)
    uv = bm.loops.layers.uv.verify()
    for l, uvv in zip(f.loops, [(0, 0), (1, 0), (1, 1), (0, 1)]):
        l[uv].uv = uvv
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    me.materials.append(mat)
    ob = bpy.data.objects.new(name, me)
    sc.collection.objects.link(ob)
    return ob


# measured dims for plates / light (final space)
tmpd = bpy.data.objects["Tunnel_Canvas"]
cmn = np.min([v.co[:] for v in tmpd.data.vertices], 0)
cmx = np.max([v.co[:] for v in tmpd.data.vertices], 0)
L = cmx[1]
HALF_W = cmx[0]
log("canvas bbox (m) min", np.round(cmn, 3), "max", np.round(cmx, 3))
SIDE_W, SIDE_H, OFF = 3.0, 1.5, 0.05
xr, xl = HALF_W + 0.04 + OFF * 0, -(HALF_W + 0.04)
xr = HALF_W + 0.04  # clears the 3 cm hoop protrusion
yc, zc = 6.9, 2.6
y0, y1, z0, z1 = yc - SIDE_W / 2, yc + SIDE_W / 2, zc - SIDE_H / 2, zc + SIDE_H / 2
# Right plate (+X face): viewer at +X looking -X has +Y on their right -> u runs y0 -> y1, normal +X
objs.append(quad_obj("Logo_Plate_Right", [(xr, y0, z0), (xr, y1, z0), (xr, y1, z1), (xr, y0, z1)], mat_logo))
# Left plate (-X face): viewer at -X looking +X has -Y on their right -> u runs y1 -> y0, normal -X
objs.append(quad_obj("Logo_Plate_Left", [(xl, y1, z0), (xl, y0, z0), (xl, y0, z1), (xl, y1, z1)], mat_logo))
# End: on the +Y lintel face, viewer at +Y looking -Y, right-hand side is -X
END_W, END_H = 2.7, 0.88
ye = L + 0.03
ze0 = 5.74 + (0.95 - END_H) / 2
objs.append(quad_obj("Logo_Plate_End", [(END_W / 2, ye, ze0), (-END_W / 2, ye, ze0), (-END_W / 2, ye, ze0 + END_H), (END_W / 2, ye, ze0 + END_H)], mat_logo))

# ceiling light strip: two thin boxes
bm = bmesh.new()
for sx in (-1.0, 1.0):
    cx = sx * 1.0
    bmesh.ops.create_cube(bm, size=1.0, matrix=Matrix.Translation((cx, (0.7 + L - 0.5) / 2, 6.56)) @
                          Matrix.Diagonal((0.22, (L - 0.5) - 0.7, 0.03, 1.0)))
objs.append(finish(bm, "Tunnel_Light", lambda b_: box_uv(b_, 0.25), mat_light))

total_tris = 0
for ob in objs:
    t = tri_count(ob)
    total_tris += t
    log("  %-18s tris %6d verts %6d mats %s" % (ob.name, t, len(ob.data.vertices), [m.name for m in ob.data.materials]))
log("TOTAL TRIS", total_tris, "objects", len(objs))
for ob in objs:
    if ob.name.startswith("Logo"):
        cs = np.array([v.co[:] for v in ob.data.vertices])
        log("  plate %-17s normal %s  center %s  size %s" % (ob.name, tuple(round(x, 2) for x in ob.data.polygons[0].normal),
                                                           np.round(cs.mean(0), 3), np.round(cs.max(0) - cs.min(0), 3)))

# ----------------------------------------------------------------------------------------------------------
# 7. delete everything imported (so no stray datablocks), checks, export
# ----------------------------------------------------------------------------------------------------------
for o in list(bpy.data.objects):
    if o not in objs:
        bpy.data.objects.remove(o, do_unlink=True)
for me in list(bpy.data.meshes):
    if me.users == 0:
        bpy.data.meshes.remove(me)
for m in list(bpy.data.materials):
    if m.users == 0:
        bpy.data.materials.remove(m)
for im in list(bpy.data.images):
    if im.users == 0 and im.name not in ("PIX_TunnelBlack_BaseColor.png", "PIX_TunnelBlack_Normal.png", "PIX_TunnelLogo_placeholder.png"):
        bpy.data.images.remove(im)

# dims
allv = np.concatenate([np.array([(o.matrix_world @ v.co)[:] for v in o.data.vertices]) for o in objs if not o.name.startswith("Logo")])
dims = allv.max(0) - allv.min(0)
log("DIMS  X(width)=%.2f Y(length)=%.2f Z(height)=%.2f  min=%s max=%s" % (dims[0], dims[1], dims[2], np.round(allv.min(0), 3), np.round(allv.max(0), 3)))

# brand scan on datablocks
bad = []
for coll in (bpy.data.objects, bpy.data.meshes, bpy.data.materials, bpy.data.images, bpy.data.node_groups):
    for d in coll:
        if BANNED.search(d.name) or (hasattr(d, "filepath") and BANNED.search(d.filepath or "")):
            bad.append(d.name)
for m in bpy.data.materials:
    for n in m.node_tree.nodes:
        if BANNED.search(n.name) or BANNED.search(n.label or ""):
            bad.append(m.name + ":" + n.name)
log("brand-name hits in datablocks:", bad)

for o in bpy.data.objects:
    o.select_set(False)
for o in objs:
    o.select_set(True)
bpy.context.view_layer.objects.active = objs[0]
fbx_path = os.path.join(OUT_DIR, FBX_NAME)
bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True, object_types={'MESH'},
                         axis_forward='-Z', axis_up='Y', global_scale=1.0, apply_unit_scale=True,
                         apply_scale_options='FBX_SCALE_UNITS', bake_space_transform=True,
                         use_mesh_modifiers=True, mesh_smooth_type='OFF', use_tspace=False,
                         add_leaf_bones=False, bake_anim=False, path_mode='AUTO', embed_textures=False,
                         use_custom_props=False)
log("exported", fbx_path, os.path.getsize(fbx_path), "bytes")

# FBX binary brand scan
data = open(fbx_path, 'rb').read()
BANNED_FBX = re.compile(BANNED.pattern.replace("|" + "\\" + "bfc" + "\\" + "b", ""), re.I)   # 'fc' is noise in binary data
fbx_hits = [m.group(0) for m in BANNED_FBX.finditer(data.decode('latin-1'))]
log("brand strings in FBX:", fbx_hits)
words = sorted(set(w.decode() for w in re.findall(rb"[A-Za-z][A-Za-z0-9_.\-]{5,}", data) if w.startswith((b"PIX", b"Tunnel", b"Logo", b"Model", b"Material", b"Texture"))))
log("FBX names:", words)
log("output folder files:", sorted(f for f in os.listdir(OUT_DIR) if not f.endswith('.meta')),
    "| brand file names:", [f for f in os.listdir(OUT_DIR) if BANNED.search(f)])

# ----------------------------------------------------------------------------------------------------------
# 8. re-import the exported FBX to verify what Unity receives, then render previews
# ----------------------------------------------------------------------------------------------------------
for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)
bpy.ops.import_scene.fbx(filepath=fbx_path, axis_forward='-Z', axis_up='Y')
imp = [o for o in bpy.data.objects if o.type == 'MESH']
for o in imp:
    # restore preview colours (viewport) from node values
    for sl in o.material_slots:
        m = sl.material
        if m.name.startswith("PIX_TunnelCanvas"):
            m.diffuse_color = (0.85, 0.35, 0.30, 1)   # team-tint preview
        elif m.name.startswith("PIX_TunnelFrame"):
            m.diffuse_color = (0.12, 0.12, 0.13, 1)
        elif m.name.startswith("PIX_TunnelBlack"):
            m.diffuse_color = (0.03, 0.03, 0.035, 1)
        elif m.name.startswith("PIX_TunnelLight"):
            m.diffuse_color = (1, 1, 0.85, 1)
        elif m.name.startswith("PIX_TunnelLogo"):
            m.diffuse_color = (0.25, 0.3, 0.9, 1)
vmin = np.min([np.array([(o.matrix_world @ v.co)[:] for v in o.data.vertices]).min(0) for o in imp], 0)
vmax = np.max([np.array([(o.matrix_world @ v.co)[:] for v in o.data.vertices]).max(0) for o in imp], 0)
log("REIMPORT bbox min", np.round(vmin, 3), "max", np.round(vmax, 3), "tris", sum(tri_count(o) for o in imp),
    "objs", [o.name for o in imp])

# preview-only props: ground + 1.9 m person reference
bpy.ops.mesh.primitive_plane_add(size=120, location=(0, 10, -0.02))
ground = bpy.context.object
gm = bpy.data.materials.new("prev_ground")
gm.diffuse_color = (0.22, 0.36, 0.2, 1)
ground.data.materials.append(gm)
refm = bpy.data.materials.new("prev_ref")
refm.diffuse_color = (1.0, 0.75, 0.1, 1)
bpy.ops.mesh.primitive_cylinder_add(radius=0.22, depth=1.9, location=(-3.6, -1.2, 0.95))
person = bpy.context.object
person.data.materials.append(refm)

sc.render.engine = 'BLENDER_WORKBENCH'
sh = sc.display.shading
sh.light = 'STUDIO'
sh.color_type = 'MATERIAL'
sh.show_shadows = True
sh.show_cavity = False
sc.render.resolution_x, sc.render.resolution_y = 1400, 800
sc.render.image_settings.file_format = 'PNG'
sc.render.film_transparent = False
world = bpy.data.worlds.new("w")
world.color = (0.62, 0.70, 0.80)
sc.world = world
bpy.ops.object.camera_add()
cam = bpy.context.object
sc.camera = cam
cam.data.clip_end = 500


def shoot(name, loc, target, lens=28):
    cam.data.lens = lens
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    sc.render.filepath = os.path.join(RENDER_DIR, name + ".png")
    bpy.ops.render.render(write_still=True)


shoot("tunnel_3q_outside", (-17, -15, 7.5), (0, 7.5, 2.8), 30)
shoot("tunnel_mouth_view", (0.0, -12, 1.7), (0, 8, 3.0), 24)
shoot("tunnel_inside_out", (0.0, 12.6, 1.7), (0, -4, 2.2), 24)
shoot("tunnel_side", (30, 7, 3.0), (0, 7, 3.0), 40)
shoot("tunnel_back_end", (9, 24, 5), (0, 13.8, 3.4), 30)
shoot("tunnel_lattice_close", (-1.2, 5.5, 1.6), (-2.5, 7.5, 2.6), 24)
shoot("tunnel_top", (0, 7, 40), (0, 7, 0), 35)
# textured close-ups of the three logo plates: the placeholder wordmark must read correctly (not mirrored) from outside
sh.color_type = 'TEXTURE'
sc.render.resolution_x, sc.render.resolution_y = 1200, 700
shoot("tunnel_plate_left", (-12.0, 6.9, 2.6), (-2.8, 6.9, 2.6), 35)
shoot("tunnel_plate_right", (12.0, 6.9, 2.6), (2.8, 6.9, 2.6), 35)
shoot("tunnel_plate_end", (0.0, 22.0, 6.2), (0.0, 13.8, 6.2), 35)
log("DONE")
