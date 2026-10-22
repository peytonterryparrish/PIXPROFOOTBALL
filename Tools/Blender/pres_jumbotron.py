"""PIX Football - stadium jumbotron prep (re-runnable).

Usage:
  "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --python pres_jumbotron.py -- [Squared Hexagon Double] [--no-render]

Source: Jumbotron_scoreboard_pack.fbx (3 meshes, cm, Y-up raw) + UDIM tiles in Vray/.
Output (Assets/_Licensed/PIX/Presentation/Jumbotron/):
  PIX_Jumbotron_<Design>.fbx            root empty + Frame + Screen_0..N (meters, origin = top centre / hang point)
  PIX_Jumbotron_<Design>_BaseColor.png  UDIM collapsed to one 0..1 atlas (2048x2048 or 4096x2048)
  PIX_Jumbotron_<Design>_Normal.png
  PIX_Jumbotron_<Design>_Mask.png       HDRP mask: R=metallic G=AO(1) B=detail(0) A=smoothness(1-rough)
Previews: D:/PIXFootball/Renders/v070_d/jumbotron_<design>.png (+ _uvtest, _back)

Pipeline per design
  1. import pack, keep one mesh, apply transform -> metres, Z-up, centred XY, Z origin at the TOP of the unit.
  2. classify faces by UDIM tile (UV centroid). Tiles whose BaseColor is black are "dark" tiles.
  3. screens = dark-tile, planar quads, area >= SCREEN_MIN_AREA, near-vertical, facing outward.
     Each gets its own object Screen_N, origin at its centre, UV 0..1 (upright + unmirrored seen from outside),
     material PIX_JumbotronScreen (shared).
  4. frame = everything else; UVs remapped from UDIM into an atlas: one slot per non-dark tile, plus a small
     constant "dark metal" swatch strip (bottom 128 px) that the remaining dark-tile faces (ticker bands, ring
     skirts) are squeezed into (UVs kept non-degenerate so tangents stay valid).
  5. export FBX: global_scale=1, apply_unit_scale, axis_forward='-Z', axis_up='Y', bake_space_transform=True.
  6. reimport the FBX to verify upright/scale, then render previews (Workbench).
"""
import bpy, bmesh, sys, os, glob, math, json, struct
import numpy as np
from mathutils import Vector, Matrix

# ----------------------------------------------------------------------------- config
SRC_FBX = r"D:\PIXFootball\Source\PPF_NEW\jumbotron\Jumbotron_scoreboard_pack.fbx"
SRC_TEX = r"D:\PIXFootball\Source\PPF_NEW\jumbotron\Vray"
OUT_DIR = r"C:\Users\peyto\Desktop\PeytonParrishGames\PIX PRO FOOTBALL\Assets\_Licensed\PIX\Presentation\Jumbotron"
RENDER_DIR = r"D:\PIXFootball\Renders\v070_d"
DESIGNS = {
    "Squared": "Jumbotron_scoreboard_squared",
    "Hexagon": "Jumbotron_scoreboard_hexagon",
    "Double": "Jumbotron_scoreboard_double",
}
SCREEN_MIN_AREA = 5.0      # m^2
SCREEN_MAX_NZ = 0.35       # near vertical: |n.z| <= this
DARK_MEAN = 0.02           # BaseColor mean below this = "dark" (screen / black) tile
ATLAS_H = 2048
STRIP_H = 128              # bottom rows of the atlas hold the constant dark swatch
SWATCH_BASE = 12.0 / 255.0
SWATCH_METAL = 0.44        # taken from the source black tiles
SWATCH_ROUGH = 0.608


# ----------------------------------------------------------------------------- image helpers
def load_arr(path):
    im = bpy.data.images.load(path)
    im.colorspace_settings.name = 'Non-Color'
    w, h = im.size
    c = im.channels
    a = np.empty(w * h * c, np.float32)
    im.pixels.foreach_get(a)
    bpy.data.images.remove(im)
    return a.reshape(h, w, c)


def _area_axis0(a, n_out):
    """Exact box (area) resample along axis 0 via cumulative sums (Blender's numpy has no usable BLAS)."""
    n = a.shape[0]
    cs = np.zeros((n + 1,) + a.shape[1:], np.float64)
    np.cumsum(a, axis=0, out=cs[1:])
    x = np.arange(n_out + 1) * (n / n_out)
    i0 = np.minimum(np.floor(x).astype(np.int64), n - 1)
    fr = (x - i0).reshape((-1,) + (1,) * (a.ndim - 1))
    F = cs[i0] + fr * a[i0]
    return ((F[1:] - F[:-1]) / (n / n_out)).astype(np.float32)


def area_resize(a, oh, ow):
    t = _area_axis0(a, oh)                                     # oh,w,c
    t = _area_axis0(np.ascontiguousarray(t.transpose(1, 0, 2)), ow)   # ow,oh,c
    return np.ascontiguousarray(t.transpose(1, 0, 2))


def save_png(path, arr, alpha):
    h, w, c = arr.shape
    im = bpy.data.images.new("tmp_save", w, h, alpha=alpha, float_buffer=False)
    im.colorspace_settings.name = 'Non-Color'
    if alpha:
        im.alpha_mode = 'CHANNEL_PACKED'
    full = np.ones((h, w, 4), np.float32)
    full[..., :c] = np.clip(arr[..., :c], 0, 1)
    im.pixels.foreach_set(full.ravel())
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    bpy.data.images.remove(im)


def png_info(path):
    with open(path, 'rb') as f:
        d = f.read(33)
    w, h, bd, ct = struct.unpack(">IIBB", d[16:26])
    return w, h, bd, {0: 'L', 2: 'RGB', 3: 'P', 4: 'LA', 6: 'RGBA'}.get(ct, ct)


def tile_file(key, kind, tile):
    g = glob.glob(os.path.join(SRC_TEX, "*scoreboard_%s_%s_.%d.png" % (key, kind, 1001 + tile)))
    return g[0] if g else None


# ----------------------------------------------------------------------------- geometry helpers
def unity_vec(v):
    """Blender (x,y,z) -> Unity coords for axis_forward='-Z', axis_up='Y' export: (-x, z, -y)."""
    return Vector((-v.x, v.z, -v.y))


def new_material(name, base, metallic, rough):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*base, 1)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = rough
    m.diffuse_color = (*base, 1)
    return m


def tex_node(mat, path, srgb, label):
    n = mat.node_tree.nodes.new("ShaderNodeTexImage")
    im = bpy.data.images.load(path, check_existing=False)
    im.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
    n.image = im
    n.label = label
    return n


def wire_frame_material(mat, base_p, normal_p, mask_p):
    nt = mat.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    nb = tex_node(mat, base_p, True, "BaseColor")
    nn = tex_node(mat, normal_p, False, "Normal")
    nm = tex_node(mat, mask_p, False, "Mask")
    nmap = nt.nodes.new("ShaderNodeNormalMap")
    sep = nt.nodes.new("ShaderNodeSeparateColor")
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = 'SUBTRACT'
    inv.inputs[0].default_value = 1.0
    nt.links.new(nb.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(nn.outputs["Color"], nmap.inputs["Color"])
    nt.links.new(nmap.outputs["Normal"], bsdf.inputs["Normal"])
    nt.links.new(nm.outputs["Color"], sep.inputs["Color"])
    nt.links.new(sep.outputs["Red"], bsdf.inputs["Metallic"])
    nt.links.new(nm.outputs["Alpha"], inv.inputs[1])
    nt.links.new(inv.outputs["Value"], bsdf.inputs["Roughness"])
    nt.nodes.active = nb          # Workbench TEXTURE mode shows the active image node


def fbx_global_settings(path):
    d = open(path, 'rb').read()
    out = {}
    for key in (b'UpAxis', b'UpAxisSign', b'FrontAxis', b'FrontAxisSign', b'CoordAxis', b'CoordAxisSign',
                b'UnitScaleFactor', b'OriginalUnitScaleFactor'):
        i = d.find(struct.pack('<I', len(key)) + key)
        if i < 0:
            continue
        p = i + 4 + len(key)
        for _ in range(3):  # type, subtype, flags strings: 'S' + len + bytes
            p += 1
            ln = struct.unpack('<I', d[p:p + 4])[0]
            p += 4 + ln
        t = chr(d[p])
        if t == 'I':
            out[key.decode()] = struct.unpack('<i', d[p + 1:p + 5])[0]
        elif t == 'D':
            out[key.decode()] = struct.unpack('<d', d[p + 1:p + 9])[0]
    return out


# ----------------------------------------------------------------------------- main build
def world_bbox(objs):
    lo = np.full(3, 1e9)
    hi = np.full(3, -1e9)
    for o in objs:
        c = np.empty(len(o.data.vertices) * 3, np.float32)
        o.data.vertices.foreach_get("co", c)
        c = c.reshape(-1, 3)
        M = np.array(o.matrix_world)
        w = c @ M[:3, :3].T + M[:3, 3]
        lo = np.minimum(lo, w.min(0))
        hi = np.maximum(hi, w.max(0))
    return Vector(lo), Vector(hi)


def fbx_models(path):
    """Minimal binary-FBX reader: returns {model name: {Lcl Translation/Rotation/Scaling, Pre/PostRotation}}."""
    d = open(path, 'rb').read()
    sz = {'Y': 2, 'C': 1, 'I': 4, 'F': 4, 'D': 8, 'L': 8}
    asz = {'f': 4, 'd': 8, 'l': 8, 'i': 4, 'b': 1}

    def nodes(pos, end):
        out = []
        while pos < end:
            eo, npr, pl, nl = struct.unpack_from('<IIIB', d, pos)
            if eo == 0:
                break
            name = d[pos + 13:pos + 13 + nl].decode()
            p = pos + 13 + nl
            props = []
            for _ in range(npr):
                t = chr(d[p]); p += 1
                if t in sz:
                    props.append(struct.unpack_from('<' + {'Y': 'h', 'C': 'b', 'I': 'i', 'F': 'f', 'D': 'd', 'L': 'q'}[t], d, p)[0]); p += sz[t]
                elif t in asz:
                    n, enc, cl = struct.unpack_from('<III', d, p); p += 12
                    p += cl if enc else n * asz[t]
                    props.append(None)
                elif t in 'SR':
                    ln = struct.unpack_from('<I', d, p)[0]; p += 4
                    props.append(d[p:p + ln].decode('latin1') if t == 'S' else None); p += ln
            kids = nodes(p, eo) if eo > p else []
            out.append((name, props, kids))
            pos = eo
        return out

    top = nodes(27, len(d))
    res = {}
    for n, _, kids in top:
        if n != 'Objects':
            continue
        for m, props, mk in kids:
            if m != 'Model':
                continue
            rec = {}
            for k, kp, kk in mk:
                if k == 'Properties70':
                    for pn, pp, _ in kk:
                        if pp and pp[0] in ('Lcl Translation', 'Lcl Rotation', 'Lcl Scaling', 'PreRotation', 'PostRotation'):
                            rec[pp[0]] = tuple(round(x, 4) for x in pp[4:7])
            res[props[1].split(chr(0))[0]] = (props[2], rec)
    return res


def build(design, key, do_render):
    print("\n==================== %s ====================" % design)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=SRC_FBX)
    src = bpy.data.objects["Jumbotron_scoreboard_" + key.split("_")[-1]]
    for o in list(bpy.data.objects):
        if o != src:
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.objects.active = src
    src.select_set(True)
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    me = src.data
    # centre XY on bbox centre, Z origin at the TOP of the unit (hang point)
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    mn, mx = co.min(0), co.max(0)
    shift = Vector((-(mn[0] + mx[0]) / 2, -(mn[1] + mx[1]) / 2, -mx[2]))
    me.transform(Matrix.Translation(shift))
    me.update()
    src.name = "Frame"
    me.name = "Frame_mesh"

    # ---- tiles: which are dark
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.normal_update()
    uvl = bm.loops.layers.uv.active

    def face_tile(f):
        cu = sum(l[uvl].uv.x for l in f.loops) / len(f.loops)
        return int(math.floor(cu))

    tiles_used = sorted({face_tile(f) for f in bm.faces})
    dark = set()
    tile_mean = {}
    for t in tiles_used:
        a = load_arr(tile_file(key.split("_")[-1], "BaseColor", t))
        tile_mean[t] = float(a[::16, ::16, :3].mean())
        if tile_mean[t] < DARK_MEAN:
            dark.add(t)
        del a
    print("tiles used", [1001 + t for t in tiles_used], "dark", [1001 + t for t in sorted(dark)],
          "means", {1001 + t: round(m, 3) for t, m in tile_mean.items()})

    # ---- screens
    screens = []
    for f in bm.faces:
        if len(f.verts) != 4 or face_tile(f) not in dark:
            continue
        n = f.normal.copy()
        area = f.calc_area()
        if area < SCREEN_MIN_AREA or abs(n.z) > SCREEN_MAX_NZ:
            continue
        cen = f.calc_center_median()
        if n.x * cen.x + n.y * cen.y <= 0:
            continue
        vs = [v.co.copy() for v in f.verts]
        d = (vs[3] - vs[0]).dot(n)
        if max(abs((v - vs[0]).dot(n)) for v in vs) > 0.02:     # planar
            continue
        up = (Vector((0, 0, 1)) - n * n.z).normalized()
        right = (-n).cross(up)
        us = [(v - cen).dot(right) for v in vs]
        ws = [(v - cen).dot(up) for v in vs]
        W, H = max(us) - min(us), max(ws) - min(ws)
        if min(W, H) < 0.8:
            continue
        # recentre on the true rectangle centre
        cen2 = cen + right * ((max(us) + min(us)) / 2) + up * ((max(ws) + min(ws)) / 2)
        screens.append(dict(face=f, n=n, cen=cen2, W=W, H=H, area=area, up=up, right=right, vs=vs))

    # order: big first, then by yaw (Unity convention, 0 = +Z, clockwise seen from above)
    for s in screens:
        u = unity_vec(s["n"])
        s["yaw"] = math.degrees(math.atan2(u.x, u.z)) % 360
        s["pitch"] = math.degrees(math.asin(max(-1, min(1, u.y))))     # <0 = faces down
    screens.sort(key=lambda s: (-round(s["area"], 1), round(s["yaw"], 1)))

    # ---- screen objects
    mat_screen = new_material("PIX_JumbotronScreen", (0.01, 0.01, 0.01), 0.0, 0.15)
    root = bpy.data.objects.new("PIX_Jumbotron_" + design, None)
    bpy.context.scene.collection.objects.link(root)
    root.empty_display_type = 'PLAIN_AXES'
    screen_objs = []
    for i, s in enumerate(screens):
        sbm = bmesh.new()
        sl = sbm.loops.layers.uv.new("UVMap")
        verts = [sbm.verts.new(v - s["cen"]) for v in s["vs"]]
        face = sbm.faces.new(verts)
        face.smooth = False
        for l, v in zip(face.loops, s["vs"]):
            r = v - s["cen"]
            l[sl].uv = (r.dot(s["right"]) / s["W"] + 0.5, r.dot(s["up"]) / s["H"] + 0.5)
        sbm.normal_update()
        assert face.normal.dot(s["n"]) > 0.999, "screen winding flipped"
        sme = bpy.data.meshes.new("Screen_%d_mesh" % i)
        sbm.to_mesh(sme)
        sbm.free()
        sme.materials.append(mat_screen)
        so = bpy.data.objects.new("Screen_%d" % i, sme)
        bpy.context.scene.collection.objects.link(so)
        so.location = s["cen"]
        so.parent = root
        screen_objs.append(so)
    bmesh.ops.delete(bm, geom=[s["face"] for s in screens], context='FACES')
    ng = [f for f in bm.faces if len(f.verts) > 4]
    if ng:
        bmesh.ops.triangulate(bm, faces=ng)
    print("screens:", len(screens), "ngons triangulated:", len(ng))

    # ---- atlas
    grey = [t for t in tiles_used if t not in dark]
    N = max(1, len(grey))
    AW = 2048 if N == 1 else 4096
    slots = {}
    for k, t in enumerate(grey):
        slots[t] = (round(k * AW / N), round((k + 1) * AW / N))
    CH = ATLAS_H - STRIP_H
    base_a = np.empty((ATLAS_H, AW, 3), np.float32)
    base_a[:] = SWATCH_BASE
    norm_a = np.empty((ATLAS_H, AW, 3), np.float32)
    norm_a[:] = (128 / 255, 128 / 255, 1.0)
    mask_a = np.empty((ATLAS_H, AW, 4), np.float32)
    mask_a[:] = (SWATCH_METAL, 1.0, 0.0, 1.0 - SWATCH_ROUGH)
    short = key.split("_")[-1]
    for t, (x0, x1) in slots.items():
        w = x1 - x0
        base_a[STRIP_H:, x0:x1] = area_resize(load_arr(tile_file(short, "BaseColor", t))[..., :3], CH, w)
        nrm = area_resize(load_arr(tile_file(short, "Normal", t))[..., :3], CH, w)
        v = nrm * 2 - 1
        v /= np.maximum(np.linalg.norm(v, axis=2, keepdims=True), 1e-6)
        norm_a[STRIP_H:, x0:x1] = v * 0.5 + 0.5
        met = area_resize(load_arr(tile_file(short, "Metallic", t))[..., :1], CH, w)
        rou = area_resize(load_arr(tile_file(short, "Roughness", t))[..., :1], CH, w)
        mask_a[STRIP_H:, x0:x1, 0] = met[..., 0]
        mask_a[STRIP_H:, x0:x1, 1] = 1.0
        mask_a[STRIP_H:, x0:x1, 2] = 0.0
        mask_a[STRIP_H:, x0:x1, 3] = 1.0 - rou[..., 0]
        print("  slot tile %d -> x %d..%d  (%dx%d)" % (1001 + t, x0, x1, w, CH))
    os.makedirs(OUT_DIR, exist_ok=True)
    pre = os.path.join(OUT_DIR, "PIX_Jumbotron_%s" % design)
    save_png(pre + "_BaseColor.png", base_a, False)
    save_png(pre + "_Normal.png", norm_a, False)
    save_png(pre + "_Mask.png", mask_a, True)
    emi = tile_file(short, "Emissive", 0)
    emi_note = "none"
    if emi:
        mx_e = max(float(load_arr(tile_file(short, "Emissive", t))[::8, ::8, :3].max())
                   for t in range(4) if tile_file(short, "Emissive", t))
        emi_note = "source emissive present but max value %.3f (all black) -> not written" % mx_e
        if mx_e > 0.01:
            ea = np.zeros((ATLAS_H, AW, 3), np.float32)
            for t, (x0, x1) in slots.items():
                ea[STRIP_H:, x0:x1] = area_resize(load_arr(tile_file(short, "Emissive", t))[..., :3], CH, x1 - x0)
            save_png(pre + "_Emissive.png", ea, False)
            emi_note = "written"
    del base_a, norm_a, mask_a
    print("textures:", {k: png_info(pre + "_%s.png" % k) for k in ("BaseColor", "Normal", "Mask")}, "emissive:", emi_note)

    # ---- frame UV remap into the atlas
    straddle = 0
    for f in bm.faces:
        us = [l[uvl].uv.x for l in f.loops]
        t = int(math.floor(sum(us) / len(us)))
        if max(us) - min(us) > 1.0 or min(us) < t - 0.01 or max(us) > t + 1.01:
            straddle += 1
        for l in f.loops:
            u = min(1.0, max(0.0, l[uvl].uv.x - t))
            v = min(1.0, max(0.0, l[uvl].uv.y))
            if t in slots:
                x0, x1 = slots[t]
                l[uvl].uv = ((x0 + u * (x1 - x0)) / AW, (STRIP_H + v * CH) / ATLAS_H)
            else:  # dark tile -> squeeze into the constant swatch strip (non-degenerate)
                l[uvl].uv = (0.1 + 0.8 * u, (32 + 64 * v) / ATLAS_H)
        f.material_index = 0
    print("faces straddling tiles:", straddle)
    bm.to_mesh(me)
    bm.free()
    me.uv_layers[0].name = "UVMap"
    me.materials.clear()
    mat_frame = new_material("PIX_JumbotronFrame", (0.5, 0.5, 0.5), 0.8, 0.3)
    wire_frame_material(mat_frame, pre + "_BaseColor.png", pre + "_Normal.png", pre + "_Mask.png")
    me.materials.append(mat_frame)
    frame = src
    bpy.context.scene.collection.objects.link(frame) if frame.name not in bpy.context.scene.collection.objects else None
    frame.parent = root

    # frame normals sanity
    cn = me.corner_normals
    dev = max(math.degrees(math.acos(max(-1, min(1, cn[li].vector.dot(p.normal)))))
              for p in me.polygons for li in p.loop_indices)
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    ngon = sum(len(p.vertices) > 4 for p in me.polygons)
    stris = sum(len(o.data.polygons) * 2 for o in screen_objs)
    bpy.context.view_layer.update()
    bmin, bmax = world_bbox([frame] + screen_objs)
    size = bmax - bmin
    print("frame tris %d ngons %d custom-normals %s max corner-vs-face dev %.1f deg" % (tris, ngon, me.has_custom_normals, dev))
    print("screen tris %d, total tris %d" % (stris, tris + stris))
    print("bbox min", [round(x, 2) for x in bmin], "max", [round(x, 2) for x in bmax], "size", [round(x, 2) for x in size])

    # ---- export
    fbx = pre + ".fbx"
    bpy.ops.object.select_all(action='DESELECT')
    for o in [root, frame] + screen_objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = root
    bpy.ops.export_scene.fbx(
        filepath=fbx, use_selection=True, object_types={'EMPTY', 'MESH'},
        global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True,
        use_mesh_modifiers=False, mesh_smooth_type='OFF', use_tspace=False, use_triangles=False,
        add_leaf_bones=False, bake_anim=False, path_mode='STRIP', embed_textures=False, use_custom_props=False)
    print("FBX written", fbx, os.path.getsize(fbx), "bytes", fbx_global_settings(fbx))

    rep = dict(design=design, fbx=fbx, tris_frame=tris, tris_screens=stris, tris_total=tris + stris,
               size=[round(x, 3) for x in size], bbox_min=[round(x, 3) for x in bmin], bbox_max=[round(x, 3) for x in bmax],
               atlas=[AW, ATLAS_H], slots={1001 + t: v for t, v in slots.items()}, dark_tiles=[1001 + t for t in sorted(dark)],
               screens=[dict(name="Screen_%d" % i, w=round(s["W"], 3), h=round(s["H"], 3), area=round(s["area"], 2),
                             yaw_unity=round(s["yaw"], 1), pitch_deg=round(s["pitch"], 1),
                             centre_unity=[round(x, 3) for x in unity_vec(s["cen"])],
                             normal_unity=[round(x, 3) for x in unity_vec(s["n"])])
                        for i, s in enumerate(screens)], emissive=emi_note)

    # ---- verify by reimport
    verify(fbx, rep)
    if do_render:
        render(design, root, frame, screen_objs, mat_screen, bmin, bmax)
    return rep


def verify(fbx, rep):
    keep = {}
    # snapshot to restore the built scene? we simply import additively into a temp collection and delete after
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=fbx)
    new = [o for o in bpy.data.objects if o not in before]
    for k, v in fbx_models(fbx).items():
        print("   FILE model", k, v)
    print("-- reimport check (Blender default Y-up/-Z import)")
    allb = []
    stris = 0
    for o in new:
        rot = tuple(round(math.degrees(a), 2) for a in o.rotation_euler)
        print("  ", o.name, o.type, "parent", o.parent.name if o.parent else None, "rot", rot, "scale", tuple(round(x, 3) for x in o.scale),
              "mats", [m.name for m in o.data.materials] if o.type == 'MESH' else "")
        if o.type == 'MESH':
            for c in o.bound_box:
                allb.append(o.matrix_world @ Vector(c))
    bmin = Vector([min(v[i] for v in allb) for i in range(3)])
    bmax = Vector([max(v[i] for v in allb) for i in range(3)])
    print("   reimport bbox min", [round(x, 2) for x in bmin], "max", [round(x, 2) for x in bmax])
    ok = True
    for o in new:
        if o.name.startswith("Screen_"):
            n = (o.matrix_world.to_3x3() @ o.data.polygons[0].normal).normalized()
            # compare to the unity-agnostic expectation: horizontal outward, z tilt keeps sign
            cen = o.matrix_world.translation
            out = n.x * cen.x + n.y * cen.y
            if out <= 0:
                ok = False
    print("   screens outward after roundtrip:", ok, "| top z (should be ~0):", round(bmax.z, 3), "bottom z:", round(bmin.z, 3))
    for o in new:
        bpy.data.objects.remove(o, do_unlink=True)


def render(design, root, frame, screen_objs, mat_screen, bmin, bmax):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sh = sc.display.shading
    sh.light = 'STUDIO'
    sh.color_type = 'TEXTURE'
    sh.show_object_outline = False
    sc.display.render_aa = '8'
    sc.render.resolution_x, sc.render.resolution_y = 1280, 960
    sc.render.image_settings.file_format = 'PNG'
    w = bpy.data.worlds.new("w")
    sc.world = w
    w.color = (0.32, 0.36, 0.42)
    cen = (bmin + bmax) / 2
    R = (bmax - bmin).length / 2
    cd = bpy.data.cameras.new("cam")
    cd.lens = 40
    cd.clip_end = 1000
    cam = bpy.data.objects.new("cam", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    os.makedirs(RENDER_DIR, exist_ok=True)
    lname = design.lower()

    def shoot(az, el, tag, dist=2.55):
        d = Vector((math.sin(math.radians(az)) * math.cos(math.radians(el)),
                    -math.cos(math.radians(az)) * math.cos(math.radians(el)),
                    math.sin(math.radians(el))))
        cam.location = cen + d * R * dist
        cam.rotation_euler = (cen - cam.location).to_track_quat('-Z', 'Y').to_euler()
        sc.render.filepath = os.path.join(RENDER_DIR, "jumbotron_%s%s.png" % (lname, tag))
        bpy.ops.render.render(write_still=True)

    # cyan screens
    mat_screen.diffuse_color = (0.0, 0.95, 1.0, 1.0)
    shoot(35, -22, "")
    shoot(215, -22, "_back")
    # upright / mirror test texture on the screens: a lit "F", red grows with u, green with v
    n = 256
    img = np.zeros((n, n, 4), np.float32)
    uu, vv = np.meshgrid(np.linspace(0, 1, n), np.linspace(0, 1, n))
    img[..., 0] = uu
    img[..., 1] = vv
    img[..., 2] = 0.25
    img[..., 3] = 1
    def box(x0, x1, y0, y1):
        img[int(y0 * n):int(y1 * n), int(x0 * n):int(x1 * n)] = (1, 1, 1, 1)
    box(0.2, 0.32, 0.15, 0.85)
    box(0.2, 0.75, 0.73, 0.85)
    box(0.2, 0.6, 0.45, 0.57)
    im = bpy.data.images.new("uvtest", n, n)
    im.pixels.foreach_set(img.ravel())
    nt = mat_screen.node_tree
    tn = nt.nodes.new("ShaderNodeTexImage")
    tn.image = im
    tn.interpolation = 'Closest'
    nt.nodes.active = tn
    shoot(35, -22, "_uvtest")
    shoot(125, -22, "_uvtest_side")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    do_render = "--no-render" not in argv
    names = [a for a in argv if not a.startswith("--")] or list(DESIGNS)
    reps = []
    for d in names:
        reps.append(build(d, DESIGNS[d], do_render))
    print("REPORT_JSON " + json.dumps(reps))


main()
