"""Bake the body-region mask of the Draft Fever player uniform UV layout (Torso_GEO + Legs_GEO share one 4096 'Uniform' sheet).

  blender -b --python Tools/Blender/bake_player_regions.py -- [--body Medium] [--size 2048] [--pad 12]
        [--out Assets/_Licensed/PIX/V6/Textures/PIX_Uniform_Regions.png] [--debug Logs/blender_v6/regions_debug.png]
        [--layout Logs/blender_v6/uniform_layout.json] [--no-render] [--compare]

Output channels (RGBA8, Non-Color / linear, edge-padded):
   R = knees + shins (legs mesh below mid-thigh, front/side; knee caps strongest, calf front)
   G = seat + thighs (back/side of the legs from mid-thigh to the waist, hips, glutes; light carry-over on the jersey hem back)
   B = chest / belly / front of the torso (front facing torso incl. front hem; strongest on lower chest and belly)
   A = back of the torso + shoulders + sleeves (sleeve ends strongest)
"Front" is the character front = -Y in Blender (character's left = +X). The mask is computed per pixel from the bind-pose geometry
(world-space position and smooth vertex normal interpolated in UV space with a numpy rasteriser), so it does not depend on any render
engine. It also analyses the four jersey-number decals baked into the BaseColor sheet and writes uniform_layout.json.
"""
import sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix_player_common import *
import bpy
from mathutils import Vector

BASE_TEX = os.path.join(PROJECT_ROOT, "Assets", "_Licensed", "DraftFever", "Packages", "MainPlayer", "Mid_Size_Main_Player",
                        "Textures", "Uniform", "Mid_Size_Main_Player_Low_Texturing_Uniform_MAT_BaseColor.png")


def parse():
    a = script_args()
    o = dict(body="Medium", size=2048, pad=12, render=True, compare=False,
             out=os.path.join(PROJECT_ROOT, "Assets", "_Licensed", "PIX", "V6", "Textures", "PIX_Uniform_Regions.png"),
             debug=os.path.join(PROJECT_ROOT, "Logs", "blender_v6", "regions_debug.png"),
             layout=os.path.join(PROJECT_ROOT, "Logs", "blender_v6", "uniform_layout.json"))
    i = 0
    while i < len(a):
        k = a[i]
        if k == "--body": o["body"] = a[i + 1]; i += 2
        elif k == "--size": o["size"] = int(a[i + 1]); i += 2
        elif k == "--pad": o["pad"] = int(a[i + 1]); i += 2
        elif k == "--out": o["out"] = a[i + 1]; i += 2
        elif k == "--debug": o["debug"] = a[i + 1]; i += 2
        elif k == "--layout": o["layout"] = a[i + 1]; i += 2
        elif k == "--no-render": o["render"] = False; i += 1
        elif k == "--compare": o["compare"] = True; i += 1
        else: raise SystemExit("unknown arg " + k)
    return o


# ------------------------------------------------------------------ smooth helpers
def sstep(x, a, b):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def curve(z, pts):
    """Smooth (cosine) interpolation through (z, value) keypoints sorted by z."""
    zs = np.array([p[0] for p in pts]); vs = np.array([p[1] for p in pts])
    out = np.interp(z, zs, vs)
    for i in range(len(pts) - 1):
        m = (z >= zs[i]) & (z <= zs[i + 1])
        t = (z[m] - zs[i]) / max(zs[i + 1] - zs[i], 1e-9)
        out[m] = vs[i] + (vs[i + 1] - vs[i]) * (0.5 - 0.5 * np.cos(np.pi * t))
    return out


def blur_masked(img, cov, sigma):
    """Normalised Gaussian blur restricted to covered pixels (so unrelated UV islands/black gaps do not bleed in)."""
    r = int(math.ceil(sigma * 3))
    k = np.exp(-0.5 * (np.arange(-r, r + 1) / sigma) ** 2); k /= k.sum()
    def sep(a):
        t = np.zeros_like(a)
        for i, w in enumerate(k):
            t += w * np.roll(a, i - r, 0)
        u = np.zeros_like(t)
        for i, w in enumerate(k):
            u += w * np.roll(t, i - r, 1)
        return u
    c = cov.astype(np.float64)
    num = sep(img * c[..., None]); den = sep(c)[..., None]
    out = np.where(den > 1e-4, num / np.maximum(den, 1e-4), img)
    return np.where(cov[..., None], out, img)


# ------------------------------------------------------------------ landmarks from the rig (world metres)
def landmarks(objs):
    arm = objs["root"]
    def hz(b): return (arm.matrix_world @ arm.data.bones[b].head_local)
    L = dict(hip=hz("hip").z, thigh=hz("thigh_stretch_l").z, knee=hz("leg_stretch_l").z, ankle=hz("foot_l").z,
             spine1=hz("spine_01").z, spine2=hz("spine_02").z, spine3=hz("spine_03").z, shoulder=hz("arm_stretch_l").z,
             neck=hz("neck").z, shoulder_x=abs(hz("arm_stretch_l").x))
    L["midthigh"] = 0.5 * (L["thigh"] + L["knee"])
    L["head_top"] = max((o.matrix_world @ Vector(c)).z for o in objs.values() if o.type == 'MESH' and o.name.startswith("Head")
                        for c in o.bound_box)
    L["feet"] = min((o.matrix_world @ Vector(c)).z for o in objs.values() if o.type == 'MESH' and o.name.startswith("Shoes")
                    for c in o.bound_box)
    return L


# ------------------------------------------------------------------ region functions (per pixel: p = world pos [N,3], n = unit normal)
def torso_regions(p, n, L):
    x, y, z = p[:, 0], p[:, 1], p[:, 2]
    ax = np.abs(x)
    front = sstep(-n[:, 1], -0.05, 0.75)
    back = sstep(n[:, 1], -0.05, 0.75)
    side = sstep(np.abs(n[:, 0]), 0.35, 0.9)
    sleeve = sstep(ax, L["shoulder_x"] - 0.02, L["shoulder_x"] + 0.09)       # 0 on the body, 1 on the sleeve ends
    body = 1.0 - sleeve
    # B: chest/belly front. Strong from the hem up to the lower chest, fading out toward the collar bones
    zB = curve(z, [(L["hip"] - 0.2, 0.85), (L["hip"], 0.85), (L["hip"] + 0.10, 1.0), (L["spine2"] + 0.05, 1.0),
                   (L["spine3"], 0.45), (L["shoulder"] + 0.0, 0.0), (9, 0.0)])
    B = front * zB * body
    # A: back + upper shoulders + sleeves
    zA = curve(z, [(L["hip"] - 0.2, 0.9), (L["hip"] + 0.05, 1.0), (9, 1.0)])
    shoulders = sstep(z, L["spine3"] + 0.02, L["shoulder"] + 0.06) * np.clip(0.55 + 0.45 * sstep(n[:, 2], 0.0, 0.6), 0, 1)
    A = np.maximum.reduce([back * zA * body, shoulders, sleeve])
    # G (light): lower back / hip side of the hem continues the seat gradient of the pants
    zG = 1.0 - sstep(z, L["hip"] + 0.0, L["hip"] + 0.16)
    G = 0.6 * zG * np.maximum(back, 0.8 * side) * body
    return np.zeros_like(z), G, B, A


def legs_regions(p, n, L):
    z = p[:, 2]
    front = sstep(-n[:, 1], -0.25, 0.65)
    back = sstep(n[:, 1], -0.25, 0.65)
    side = sstep(np.abs(n[:, 0]), 0.35, 0.9)
    kn, an, mt = L["knee"], L["ankle"], L["midthigh"]
    # R: knees + shins
    zR = curve(z, [(an - 0.05, 0.0), (an + 0.08, 0.25), (kn - 0.20, 0.45), (kn - 0.07, 0.7), (kn, 1.0), (kn + 0.06, 0.95),
                   (kn + 0.14, 0.55), (mt, 0.0), (9, 0.0)])
    fR = np.maximum(front, 0.45 * side)
    R = zR * fR
    # G: seat + thighs (back/side, mid-thigh -> waist)
    zG = curve(z, [(mt - 0.05, 0.0), (mt + 0.08, 0.55), (L["hip"] + 0.02, 1.0), (9, 1.0)])
    fG = np.maximum.reduce([back, 0.85 * side, 0.22 * np.ones_like(z)])
    G = zG * fG
    return R, G, np.zeros_like(z), np.zeros_like(z)


# ------------------------------------------------------------------ UV layout / number analysis
def tri_geometry(obj):
    co, nr, uv, tl, tv, tp = world_arrays(obj)
    return co, nr, uv, tl, tv, tp


def region_pixels(mask_img):
    return mask_img


NUMERALS = [
    # name, search window (x0,y0,x1,y1) in 4096 px (origin top-left), UV direction of digit "up" and of the reading direction
    # (u right, v up in Unity/Blender UV space), mesh, and the axis used to decide "outward"
    dict(name="back_number",  win=(620, 2880, 1300, 3660), up=(+1, 0), read=(0, -1), mesh="Torso_GEO"),
    dict(name="front_number", win=(2280, 2930, 2820, 3560), up=(-1, 0), read=(0, +1), mesh="Torso_GEO"),
    dict(name="sleeve_number_R", win=(340, 1900, 680, 2240), up=(0, +1), read=(+1, 0), mesh="Torso_GEO"),
    dict(name="sleeve_number_L", win=(1200, 1840, 1560, 2160), up=(0, +1), read=(+1, 0), mesh="Torso_GEO"),
]


def detect_numeral_rect(grey, win):
    x0, y0, x1, y1 = win
    c = grey[y0:y1, x0:x1].astype(np.float64)
    # 5x5 box blur removes the dotted mesh pattern
    k = np.ones(5) / 5
    c = np.apply_along_axis(lambda r: np.convolve(r, k, 'same'), 1, c)
    c = np.apply_along_axis(lambda r: np.convolve(r, k, 'same'), 0, c)
    bg = np.median(c)
    m = c > bg + 0.07
    m[:6] = False; m[-6:] = False; m[:, :6] = False; m[:, -6:] = False
    ys, xs = np.where(m)
    return (x0 + int(xs.min()), y0 + int(ys.min()), x0 + int(xs.max()) + 1, y0 + int(ys.max()) + 1)


def analyse_numerals(objs, L):
    """For every baked-in numeral: refined pixel rect, the 3D place it lands on, and the orientation of the digits.
    Orientation = per-triangle UV Jacobian (dP/du, dP/dv). 'Outward' is the radial direction away from the body axis (torso: the
    vertical axis through the torso, sleeves: the arm axis from the rig); only triangles whose vertex normal points outward are used,
    because the sleeve caps contain a second inner lining layer that overlaps the same UV area (inverted handedness, ignored)."""
    grey, _ = load_png(BASE_TEX)
    g = grey[..., 0]
    out = {}
    arm = objs["root"]
    co, nr, uv, tl, tv, tp = tri_geometry(objs["Torso_GEO"])
    pix = uv[tl].copy(); pix[:, :, 0] *= 4096; pix[:, :, 1] = (1 - pix[:, :, 1]) * 4096
    cen = pix.mean(1)
    P3 = co[tv]; N3 = nr[tv].mean(1)
    N3 /= np.linalg.norm(N3, axis=1, keepdims=True)
    axis_y = float(np.average(co[:, 1]))
    for nd in NUMERALS:
        x0, y0, x1, y1 = detect_numeral_rect(g, nd["win"])
        inside = (cen[:, 0] >= x0) & (cen[:, 0] <= x1) & (cen[:, 1] >= y0) & (cen[:, 1] <= y1)
        idx_all = np.where(inside)[0]
        if nd["name"].startswith("sleeve"):
            s = "l" if nd["name"].endswith("L") else "r"
            a0 = np.array(arm.matrix_world @ arm.data.bones["arm_stretch_" + s].head_local)
            a1 = np.array(arm.matrix_world @ arm.data.bones["forearm_stretch_" + s].head_local)
            ax = (a1 - a0) / np.linalg.norm(a1 - a0)
            def radial(c):
                r = c - a0; r = r - ax * np.dot(r, ax); return r / np.linalg.norm(r)
        else:
            def radial(c):
                r = c - np.array([0, axis_y, c[2]]); r[2] = 0; return r / np.linalg.norm(r)
        Pu = np.zeros(3); Pv = np.zeros(3); pos = np.zeros(3); nrm = np.zeros(3); wsum = 0.0
        pos_handed = 0; used = 0
        for t in idx_all:
            c = P3[t].mean(0)
            if np.dot(N3[t], radial(c)) <= 0:
                continue                                                  # inner lining layer
            e = uv[tl[t]][1:] - uv[tl[t]][0]
            E = P3[t][1:] - P3[t][0]
            det = e[0, 0] * e[1, 1] - e[0, 1] * e[1, 0]
            if abs(det) < 1e-14:
                continue
            J = np.linalg.inv(e) @ E                                      # rows: dP/du, dP/dv
            if np.linalg.norm(J[0]) < 1e-9 or np.linalg.norm(J[1]) < 1e-9:
                continue
            w = abs(det) * 0.5
            Pu += w * J[0] / np.linalg.norm(J[0]); Pv += w * J[1] / np.linalg.norm(J[1])
            pos += w * c; nrm += w * N3[t]; wsum += w; used += 1
            cr = np.cross(J[0], J[1])
            pos_handed += 1 if np.dot(cr, radial(c)) > 0 else 0
        Pu /= np.linalg.norm(Pu); Pv /= np.linalg.norm(Pv); pos /= wsum; nrm /= np.linalg.norm(nrm)
        up3 = nd["up"][0] * Pu + nd["up"][1] * Pv; up3 /= np.linalg.norm(up3)
        rd3 = nd["read"][0] * Pu + nd["read"][1] * Pv; rd3 /= np.linalg.norm(rd3)
        frac = pos_handed / max(used, 1)
        print("  %-16s rect %s  tris under rect %d, outer-shell tris %d, unmirrored fraction %.2f" % (nd["name"], (x0, y0, x1, y1), len(idx_all), used, frac))
        out[nd["name"]] = dict(
            rect_px=[x0, y0, x1, y1], rect_size_px=[x1 - x0, y1 - y0],
            rect_uv_unity=[round(x0 / 4096, 5), round(1 - y1 / 4096, 5), round(x1 / 4096, 5), round(1 - y0 / 4096, 5)],  # umin, vmin, umax, vmax (origin bottom-left)
            digit_up_uv=list(nd["up"]), reading_dir_uv=list(nd["read"]),
            n_tris_under_rect=int(len(idx_all)), n_tris_outer_shell=int(used),
            mean_normal=[round(float(v), 3) for v in nrm], mean_pos=[round(float(v), 3) for v in pos],
            digit_up_3d=[round(float(v), 3) for v in up3], reading_dir_3d=[round(float(v), 3) for v in rd3],
            facing=("front(-Y)" if nrm[1] < -0.3 else "back(+Y)" if nrm[1] > 0.3 else "side/top"),
            side=("character LEFT(+X)" if pos[0] > 0.1 else "character RIGHT(-X)" if pos[0] < -0.1 else "centre"),
            unmirrored_fraction=round(frac, 3), mirrored_when_viewed_from_outside=bool(frac < 0.5))
    return out


def island_table(objs):
    rows = []
    for name in ("Torso_GEO", "Legs_GEO"):
        o = objs[name]
        isl = uv_islands(o)
        co, nr, uv, tl, tv, tp = tri_geometry(o)
        for i in np.unique(isl):
            tris = np.where(isl[tp] == i)[0]
            if len(tris) < 20:
                continue
            u = uv[tl[tris]].reshape(-1, 2)
            P = co[tv[tris]].reshape(-1, 3)
            rows.append(dict(mesh=name, faces=int((isl == i).sum()),
                             rect_px=[int(u[:, 0].min() * 4096), int((1 - u[:, 1].max()) * 4096), int(u[:, 0].max() * 4096) + 1,
                                      int((1 - u[:, 1].min()) * 4096) + 1],
                             z_range=[round(float(P[:, 2].min()), 3), round(float(P[:, 2].max()), 3)],
                             x_range=[round(float(P[:, 0].min()), 3), round(float(P[:, 0].max()), 3)],
                             y_range=[round(float(P[:, 1].min()), 3), round(float(P[:, 1].max()), 3)]))
    return rows


# ------------------------------------------------------------------ bake
def bake(objs, size, pad, L):
    chans = np.zeros((size, size, 4), np.float64)
    cov = np.zeros((size, size), bool)
    for name, fn in (("Torso_GEO", torso_regions), ("Legs_GEO", legs_regions)):
        o = objs[name]
        co, nr, uv, tl, tv, tp = tri_geometry(o)
        attr = np.concatenate([co, nr], axis=1)[tv]                  # [T,3,6]
        a, c = rasterise(uv, tl, attr, size)
        p = a[c][:, :3]; n = a[c][:, 3:6]
        n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-9)
        r, g, b, al = fn(p, n, L)
        v = np.stack([r, g, b, al], 1)
        chans[c] = np.maximum(chans[c], v) if cov[c].any() else v
        cov |= c
    chans = blur_masked(chans, cov, sigma=max(1.5, size / 1024.0))
    chans, full = dilate(chans, cov, pad)
    return np.clip(chans, 0, 1), cov, full


def compare_sizes(objs_m, L):
    """Compare the UV layout of the Small/Large bodies with the Medium one. NOTE: re-imports (the scene is replaced)."""
    S = 1024
    def cover(objs):
        c = np.zeros((S, S), bool)
        for n in ("Torso_GEO", "Legs_GEO"):
            co, nr, uv, tl, tv, tp = tri_geometry(objs[n])
            _, cc = rasterise(uv, tl, np.zeros((len(tl), 3, 1)), S)
            c |= cc
        return c
    def uvs(o):
        a = o.data; u = np.empty(len(a.loops) * 2, np.float32); a.uv_layers.active.data.foreach_get('uv', u); return u
    base = cover(objs_m)
    med = {n: uvs(objs_m[n]) for n in ("Torso_GEO", "Legs_GEO")}
    res = {}
    for sz in ("Small", "Large"):
        o2 = import_player(os.path.join(BODY_DIR, "%sPlayer.fbx" % sz))
        c2 = cover(o2)
        row = dict(coverage_iou_1024=round(float((base & c2).sum() / (base | c2).sum()), 4))
        for n in ("Torso_GEO", "Legs_GEO"):
            u2 = uvs(o2[n])
            row[n + "_uv_floats"] = [len(med[n]), len(u2)]
            if len(med[n]) == len(u2):
                row[n + "_max_uv_diff_px4096"] = round(float(np.abs(med[n] - u2).max() * 4096), 2)
        row["islands"] = island_table(o2)
        res[sz] = row
    return res


# ------------------------------------------------------------------ debug composite
def render_views(objs, rgb_path, a_path, out_dir, size=(512, 896)):
    from mathutils import Matrix
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.render.image_settings.media_type = "IMAGE"
    sc.render.image_settings.file_format = "PNG"
    sc.view_settings.view_transform = 'Standard'
    sc.display.shading.light = 'FLAT'
    sc.display.shading.color_type = 'TEXTURE'
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.render.film_transparent = False
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.12, 0.13, 0.16)
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'; cam.data.ortho_scale = 2.0; cam.data.clip_end = 50
    sc.camera = cam
    for o in objs.values():
        if o.type != 'MESH': continue
        for m in list(o.data.materials): pass
    def paint(tex_path):
        im = bpy.data.images.load(tex_path, check_existing=False)
        im.colorspace_settings.name = 'Non-Color'; im.alpha_mode = 'CHANNEL_PACKED'
        for n in ("Torso_GEO", "Legs_GEO"):
            o = objs[n]
            mat = bpy.data.materials.new("m_" + n); mat.use_nodes = True
            nt = mat.node_tree
            tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = im; tn.interpolation = 'Linear'
            nt.nodes.active = tn
            o.data.materials.clear(); o.data.materials.append(mat)
        for o in objs.values():
            if o.type == 'MESH' and o.name not in ("Torso_GEO", "Legs_GEO"):
                m = bpy.data.materials.new("g_" + o.name); m.diffuse_color = (0.30, 0.30, 0.32, 1)
                o.data.materials.clear(); o.data.materials.append(m)
        return im
    views = {"front": Vector((0, -1, 0)), "back": Vector((0, 1, 0)), "q": Vector((0.8, -0.6, 0.0)), "side": Vector((1, 0, 0))}
    center = Vector((0, 0.05, 0.93))
    res = {}
    for tag, path in (("rgb", rgb_path), ("a", a_path)):
        paint(path)
        for v, d in views.items():
            d = d.normalized()
            cam.location = center + d * 6
            cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()
            p = os.path.join(out_dir, "_view_%s_%s.png" % (tag, v))
            sc.render.filepath = p
            bpy.ops.render.render(write_still=True)
            res[(tag, v)] = p
    return res


def compose_debug(path, chans, cov, base_grey, views, panel=(512, 896)):
    W = 2048
    S = 512
    # row 1: per channel overlay on dimmed base colour (downsampled 4096 -> 512)
    g = base_grey.reshape(512, 8, 512, 8).mean((1, 3)) if base_grey.shape[0] == 4096 else base_grey
    cs = chans.shape[0] // S
    cd = chans.reshape(S, cs, S, cs, 4).mean((1, 3))
    row1 = []
    tint = [(1, 0.15, 0.15), (0.2, 1, 0.2), (0.3, 0.45, 1), (1, 1, 0.2)]
    for i in range(4):
        base = np.repeat(g[..., None] * 0.35, 3, 2)
        a = cd[..., i:i + 1]
        img = base * (1 - a) + np.array(tint[i]) * a
        row1.append(img)
    row1 = np.concatenate(row1, axis=1)
    pw, ph = panel
    def load(p):
        a, _ = load_png(p, 'sRGB')
        return a[..., :3]
    row2 = np.concatenate([load(views[("rgb", v)]) for v in ("front", "back", "q", "side")], axis=1)
    row3 = np.concatenate([load(views[("a", v)]) for v in ("front", "back", "q", "side")], axis=1)
    sheet = np.concatenate([row1, row2, row3], axis=0)
    save_png(path, sheet, color_space='sRGB')


def main():
    o = parse()
    fbx = os.path.join(BODY_DIR, "%sPlayer.fbx" % o["body"])
    objs = import_player(fbx)
    L = landmarks(objs)
    print("landmarks (m):", {k: round(float(v), 3) for k, v in L.items()})
    chans, cov, full = bake(objs, o["size"], o["pad"], L)
    save_png(o["out"], chans, color_space='Non-Color')
    print("wrote", o["out"], chans.shape, "covered %.1f%% of sheet" % (100 * cov.mean()))
    for i, n in enumerate("RGBA"):
        m = chans[..., i][cov]
        print("  channel %s: mean %.3f  p95 %.3f  max %.3f  coverage>0.5: %.1f%% of UV area" % (n, m.mean(), np.percentile(m, 95), m.max(), 100 * (m > 0.5).mean()))
    layout = dict(body=o["body"], landmarks={k: round(float(v), 4) for k, v in L.items()},
                  islands=island_table(objs), numerals=analyse_numerals(objs, L))
    if o["compare"]:
        layout["size_compare"] = compare_sizes(objs, L)
        objs = import_player(fbx)       # compare_sizes replaced the scene
    os.makedirs(os.path.dirname(os.path.abspath(o["layout"])), exist_ok=True)
    with open(o["layout"], "w") as f:
        json.dump(layout, f, indent=1)
    print("wrote", o["layout"])
    for k, v in layout["numerals"].items():
        print(k, json.dumps(v))
    if o["render"]:
        d = os.path.dirname(os.path.abspath(o["debug"]))
        rgb = chans.copy(); a = np.repeat(chans[..., 3:4], 3, 2)
        rgb[..., 3] = 1.0
        a = np.concatenate([a, np.ones_like(a[..., :1])], 2)
        save_png(os.path.join(d, "_rgb_only.png"), rgb, 'Non-Color')
        save_png(os.path.join(d, "_a_only.png"), a, 'Non-Color')
        views = render_views(objs, os.path.join(d, "_rgb_only.png"), os.path.join(d, "_a_only.png"), d)
        grey, _ = load_png(BASE_TEX)
        compose_debug(o["debug"], chans, cov, grey[..., 0], views)
        print("wrote", o["debug"])
        for p in list(views.values()) + [os.path.join(d, "_rgb_only.png"), os.path.join(d, "_a_only.png")]:
            try: os.remove(p)
            except OSError: pass


if __name__ == "__main__":
    main()
