"""PHR texture set 2: cleats (marks painted out), skin tones (arms), sleeves, small accessories, helmet / flat parts.
blender -b --python Tools/Blender/phr_textures2.py -- [cleats,skin,sleeves,small,helmet]
R1's cleat / arm / sleeve / wristband / towel / pouch maps are used only as shading and normal detail AFTER every logo (swooshes, hexagon, NFL shield, tiger head, wordmarks)
has been painted out or replaced; jersey / pants / socks / helmet albedo are not used at all (see phr_textures.py)."""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_tex import *

OUT = os.path.join(OUT_ASSET, "Textures", "Uniform")
SKIN_OUT = os.path.join(OUT_ASSET, "Textures", "Skin")
R1T = R1_TEX


def poly_mask(h, w, poly, scale):
    """boolean mask [h,w] of a polygon given in 1400-px coordinates of a texture that is w x h."""
    pts = np.array(poly, np.float64) * scale
    xs = np.arange(w) + 0.5
    ys = np.arange(h) + 0.5
    x0, x1 = int(max(pts[:, 0].min() - 2, 0)), int(min(pts[:, 0].max() + 2, w))
    y0, y1 = int(max(pts[:, 1].min() - 2, 0)), int(min(pts[:, 1].max() + 2, h))
    X, Y = np.meshgrid(xs[x0:x1], ys[y0:y1])
    inside = np.zeros(X.shape, bool)
    n = len(pts)
    j = n - 1
    for i in range(n):
        xi, yi = pts[i]
        xj, yj = pts[j]
        cond = ((yi > Y) != (yj > Y)) & (X < (xj - xi) * (Y - yi) / (yj - yi + 1e-12) + xi)
        inside ^= cond
        j = i
    m = np.zeros((h, w), bool)
    m[y0:y1, x0:x1] = inside
    return m


def patch_fill(img, mask, src_box, feather=6, seed=0):
    """Replace img[mask] with a mirrored tiling of img[src_box] (x0,y0,x1,y1 px), feathered."""
    h, w = mask.shape
    x0, y0, x1, y1 = src_box
    src = img[y0:y1, x0:x1]
    sh, sw = src.shape[:2]
    big = np.concatenate([src, src[:, ::-1]], axis=1)
    big = np.concatenate([big, big[::-1]], axis=0)
    reps_y = h // big.shape[0] + 2
    reps_x = w // big.shape[1] + 2
    tiled = np.tile(big, (reps_y, reps_x) + (1,) * (img.ndim - 2))[:h, :w]
    f = box_blur(mask.astype(np.float64), feather)
    f = np.clip(f * 1.6, 0, 1)
    f = np.maximum(f, mask)
    if img.ndim == 3:
        f = f[..., None]
    return img * (1 - f) + tiled * f


# polygons in 1400-px coordinates of Cleat.jpg (2048 px source): the three Nike swooshes, the sole swoosh, the hexagon tag and the bottom swoosh
CLEAT_MARKS = {
    "swoosh_left": [(360, 1022), (520, 1028), (690, 1036), (780, 1004), (832, 1085), (806, 1182), (640, 1172), (480, 1112)],
    "swoosh_right": [(1026, 655), (1086, 698), (1172, 790), (1242, 900), (1266, 1010), (1254, 1096), (1182, 1148), (1104, 1124), (1100, 1062), (1140, 1002), (1100, 902), (1052, 790)],
    "swoosh_bottom": [(530, 1330), (720, 1330), (730, 1410), (530, 1410)],
    "swoosh_sole": [(250, 610), (350, 610), (350, 690), (250, 690)],
    "hex_tag": [(100, 95), (205, 95), (205, 190), (100, 190)],
}


def cleats():
    size = 2048
    alb = load(f"{R1T}/Cleat.jpg", True)[..., :3]
    nrm = load(f"{R1T}/Cleat_nrm.jpg", False)[..., :3]
    h, w = alb.shape[:2]
    scale = w / 1400.0
    clean_box = (int(300 * scale), int(870 * scale), int(470 * scale), int(960 * scale))   # plain white knit of the upper
    for name, poly in CLEAT_MARKS.items():
        m = poly_mask(h, w, poly, scale)
        if name in ("swoosh_bottom", "swoosh_sole"):
            # these sit on orange / honeycomb: fill from the surrounding strip instead
            src = (int(220 * scale), int(1250 * scale), int(420 * scale), int(1330 * scale)) if name == "swoosh_bottom" else (int(240 * scale), int(700 * scale), int(340 * scale), int(780 * scale))
            alb = patch_fill(alb, m, src, 4)
            nrm = patch_fill(nrm, m, src, 4)
        else:
            alb = patch_fill(alb, m, clean_box, 8)
            nrm = patch_fill(nrm, m, clean_box, 8)
    # classes: orange accents -> secondary (G), dark background / sole rubber -> tertiary (B), white -> primary
    r, g, b = alb[..., 0], alb[..., 1], alb[..., 2]
    mx = alb.max(-1)
    mn = alb.min(-1)
    sat = (mx - mn) / np.maximum(mx, 1e-4)
    orange = smoothstep(0.35, 0.55, sat) * smoothstep(0.35, 0.5, r)
    dark = (1 - smoothstep(0.08, 0.16, mx)) * (1 - orange)
    lum = 0.2126 * r + 0.7152 * g + 0.0722 * b
    # shading: normalise per class so the knit / stitching detail survives, flat 0.8 average
    base = np.where(orange > 0.5, 0.42, np.where(dark > 0.5, 0.12, 0.88))
    gray = np.clip(lum / np.maximum(base, 1e-3), 0.0, 1.25) * 0.80
    gray = np.clip(0.80 + (gray - 0.80) * 0.6, 0.0, 1.0)
    mask = np.stack([np.zeros_like(orange), orange, dark], -1)
    cov = np.ones((h, w), bool)
    for tex, srgb in ((gray, True), (mask, False)):
        pass
    G = np.stack([gray] * 3, -1)
    import pix_player_common as ppc
    def rs(a, s):
        im = bpy.data.images.new("t", a.shape[1], a.shape[0], alpha=True)
        arr = np.concatenate([a, np.ones(a.shape[:2] + (1,), a.dtype)], -1) if a.shape[2] == 3 else a
        im.pixels.foreach_set(np.ascontiguousarray(arr[::-1].astype(np.float32)).ravel())
        im.scale(s, s)
        out = np.empty(s * s * 4, np.float32)
        im.pixels.foreach_get(out)
        bpy.data.images.remove(im)
        return out.reshape(s, s, 4)[::-1][..., :3]
    save(f"{OUT}/cleats_Gray.png", rs(G, size), True)
    save(f"{OUT}/cleats_Team.png", rs(mask, size), False)
    save(f"{OUT}/cleats_N.png", rs(nrm, size), False)
    save(f"{OUT}/cleats_Regions.png", np.zeros((64, 64, 4)), False)
    print("wrote cleats")


SKIN_TONES = [(235, 200, 182), (222, 182, 152), (206, 160, 124), (186, 138, 102), (160, 112, 78), (131, 88, 60), (102, 67, 46), (76, 48, 34)]


def skin_mean(a):
    """robust mean (linear) of the skin pixels of an albedo: central luminance band."""
    lin = srgb_to_lin(a[..., :3])
    lum = lin @ np.array([0.2126, 0.7152, 0.0722])
    lo, hi = np.percentile(lum, [30, 85])
    m = (lum >= lo) & (lum <= hi)
    return lin[m].mean(axis=0)


def retint(a, target_srgb255, mean_lin):
    lin = srgb_to_lin(a[..., :3])
    tgt = srgb_to_lin(np.array(target_srgb255) / 255.0)
    out = lin * (tgt / mean_lin)
    return lin_to_srgb(np.clip(out, 0, 1)).astype(np.float32)


def skin():
    arm = load(f"{R1T}/arm.jpg", True)
    mean = skin_mean(arm)
    print("arm mean (lin)", mean, "srgb", lin_to_srgb(mean))
    for i, t in enumerate(SKIN_TONES):
        save(f"{SKIN_OUT}/PHR_Arms_T{i}.png", retint(arm, t, mean), True)
    # arm / body normal as is (skin pores, veins, finger creases: no marks)
    nrm = load(f"{R1T}/arm_nrm.jpg", False)
    save(f"{SKIN_OUT}/PHR_Arms_N.png", nrm[..., :3], False)
    head = load(DF + "/Heads/Rodrigo/textures/head12-baseMap.png", True)
    hm = skin_mean(head)
    print("head mean (lin)", hm, "srgb", lin_to_srgb(hm))
    hs = bpy.data.images.new("hs", head.shape[1], head.shape[0], alpha=True)
    def down(a, s):
        im = bpy.data.images.new("d", a.shape[1], a.shape[0], alpha=True)
        arr = a if a.shape[2] == 4 else np.concatenate([a, np.ones(a.shape[:2] + (1,), a.dtype)], -1)
        im.pixels.foreach_set(np.ascontiguousarray(arr[::-1].astype(np.float32)).ravel())
        im.scale(s, s)
        out = np.empty(s * s * 4, np.float32)
        im.pixels.foreach_get(out)
        bpy.data.images.remove(im)
        return out.reshape(s, s, 4)[::-1][..., :3]
    for i, t in enumerate(SKIN_TONES):
        save(f"{SKIN_OUT}/PHR_Head_T{i}.png", down(retint(head, t, hm), 2048), True)
    hn = load(DF + "/Heads/Rodrigo/textures/head12-normal.tif", False)[..., :3]
    save(f"{SKIN_OUT}/PHR_Head_N.png", hn, False)
    rough = np.full((16, 16, 3), 0.78, np.float32)
    save(f"{SKIN_OUT}/PHR_Skin_Rough.png", rough, False)
    print("wrote skin")


def sleeves():
    for src, name in (("armsleeve", "armsleeve"), ("innershirt", "innershirt")):
        a = load(f"{R1T}/{src}.jpg", True)[..., :3]
        mx, mn = a.max(-1), a.min(-1)
        white = 1 - smoothstep(0.10, 0.22, (mx - mn) / np.maximum(mx, 1e-3))
        white *= smoothstep(0.55, 0.75, mx)
        lum = a @ np.array([0.2126, 0.7152, 0.0722], np.float32)
        gray = np.where(white > 0.5, np.clip(lum / 0.93, 0, 1.2) * 0.80, 0.80)
        gray = 0.80 + (gray - 0.80) * 0.8
        h, w = gray.shape
        G = np.stack([gray] * 3, -1).astype(np.float32)
        save(f"{OUT}/{name}_Gray.png", G, True)
        save(f"{OUT}/{name}_Team.png", np.zeros((16, 16, 3), np.float32), False)
        save(f"{OUT}/{name}_Regions.png", np.zeros((16, 16, 4), np.float32), False)
        nrm = load(f"{R1T}/arm_nrm.jpg", False)[..., :3]
        save(f"{OUT}/{name}_N.png", nrm, False)
    print("wrote sleeves")


def small():
    for src, name, base, nsrc in (("bag", "pouch", 0.55, "bag_nrm"), ("towel", "towel", 0.85, "towel_nrm"), ("wristcoach", "wristband", 0.85, "wristcoach_nrm")):
        n = load(f"{R1T}/{nsrc}.jpg", False)[..., :3]
        s = n.shape[0]
        g = base + 0.04 * fft_noise(s, 30, 200, (1, 1), 77)
        save(f"{OUT}/{name}_Gray.png", np.stack([g] * 3, -1).astype(np.float32), True)
        save(f"{OUT}/{name}_Team.png", np.zeros((16, 16, 3), np.float32), False)
        save(f"{OUT}/{name}_Regions.png", np.zeros((16, 16, 4), np.float32), False)
        save(f"{OUT}/{name}_N.png", n, False)
    print("wrote small")


def gloves():
    """Glove texture set on the R1 arm UV layout (only the hand islands are used by the PHR_Hands material): back = primary, palm = secondary (grip), cuff = tertiary."""
    from phr_textures import min_dist
    size = 2048
    arm, meshes = import_r1()
    o = meshes["arms"]
    me = o.data
    hand_idx = {g.index for g in o.vertex_groups if "Hand" in g.name}
    hv = np.zeros(len(me.vertices))
    for v in me.vertices:
        hv[v.index] = sum(g.weight for g in v.groups if g.group in hand_idx)
    uv, tl, attr = uv_attrs(o, extra_vertex_attrs=hv[:, None])
    import pix_player_common as ppc
    out, cov = ppc.rasterise(uv, tl, attr, size)
    pos, nrm, hand = out[..., 0:3], out[..., 3:6], out[..., 6]
    isl = hand > 0.5
    x = pos[..., 0]
    toward = -np.sign(x) * nrm[..., 0]
    palm = smoothstep(0.15, 0.4, toward) * isl
    # cuff: close to the wrist bones
    wl = np.array(arm.data.bones["rig:LeftHand"].head_local)
    wr = np.array(arm.data.bones["rig:RightHand"].head_local)
    dwl = np.linalg.norm(pos - wl, axis=-1)
    dwr = np.linalg.norm(pos - wr, axis=-1)
    dw = np.where(x > 0, dwl, dwr)
    cuff = (1 - smoothstep(0.045, 0.055, dw)) * isl
    gray = 0.78 + 0.05 * fft_noise(size, 200, 900, (1, 1), 91) + 0.03 * fft_noise(size, 4, 30, (1, 1), 92)
    # grip dots on the palm
    yy, xx = np.mgrid[0:size, 0:size]
    dots = (np.sin(xx * 2 * np.pi / 9.0) * np.sin(yy * 2 * np.pi / 9.0))
    gray = gray - 0.06 * smoothstep(0.55, 0.8, dots) * palm
    h = 0.5 * fft_noise(size, 150, 700, (1, 1), 93) + 0.8 * smoothstep(0.55, 0.8, dots) * palm
    normal = height_to_normal(h, 1.2)
    team_g = palm
    team_b = cuff * (1 - palm)
    cov2 = cov
    for tex, nm, srgb in ((np.stack([gray] * 3, -1), "Gray", True), (np.stack([np.zeros_like(team_g), team_g, team_b], -1), "Team", False), (normal, "N", False)):
        save(f"{OUT}/gloves_{nm}.png", fill_dilate(tex.astype(np.float32), cov2, 24), srgb)
    save(f"{OUT}/gloves_Regions.png", np.zeros((16, 16, 4), np.float32), False)
    print("wrote gloves")


def helmet():
    g = np.full((16, 16, 3), 0.86, np.float32)
    save(f"{OUT}/helmet_Gray.png", g, True)
    m = np.zeros((16, 16, 3), np.float32)
    m[..., 0] = 1.0
    save(f"{OUT}/helmet_Team.png", m, False)
    save(f"{OUT}/helmet_N.png", flat_normal(16), False)
    print("wrote helmet")


if __name__ == "__main__":
    a = args()
    which = a[0].split(",") if a else ["cleats", "skin", "sleeves", "small", "helmet"]
    reset()
    for w in which:
        globals()[w]()
