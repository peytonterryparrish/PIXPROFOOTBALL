"""v0.13.0 BODIES-2: cloth folds, seams, panels and fabric detail for the uniform maps (jersey, pants, compression sleeve).

The folds are authored ("sculpted") as a HEIGHT FIELD in metres on the R1-frame uniform meshes: every texel of the UV layout knows its 3D rest position and normal (phr_tex.bake_world),
and the folds are functions of that position and of the skeleton landmarks (armpit, shoulder joint and arm axis, waist, hip, knee, elbow, wrist).  The 15 bodies are the R1 mesh reshaped
(phr_body / phr2_warp), the UV is position based, so one map set lands on every body at the right anatomical place.
Outputs per part: a tangent-space normal from the physical gradient of the height (metres per texel from the 3D position map, so a 2 mm fold is 2 mm on every island),
an ambient-occlusion / cavity multiplier baked into the grey albedo (folds, armpit, waist tuck, seams), seam groove + stitch dashes, mesh / knit weave.
Nothing is copied from R1's own jersey maps (they carry the real marks).  numpy only; runs inside Blender (imported by phr_textures.py).
"""
import numpy as np
from phr_tex import *

_C = {}


def noise(size, lo, hi, seed, aniso=(1.0, 1.0)):
    k = (size, lo, hi, seed, aniso)
    if k not in _C:
        _C[k] = fft_noise(size, lo, hi, aniso, seed)
    return _C[k]


def ridge(ph, sharp=1.7):
    """folded-cloth profile in -1..1: rounded crests, narrow creases."""
    c = 0.5 + 0.5 * np.cos(ph)
    return 1.0 - 2.0 * (1.0 - c) ** sharp


def sstep(a, b, x):
    return smoothstep(a, b, x)


def texel_metric(pos, cov):
    """metres per texel along u (columns) and v (rows) from the 3D position map; holes and island jumps take the median."""
    def d(p, axis):
        a = np.roll(p, -1, axis); b = np.roll(p, 1, axis)
        ca = np.roll(cov, -1, axis); cb = np.roll(cov, 1, axis)
        L = np.linalg.norm(a - b, axis=-1) * 0.5
        ok = cov & ca & cb & (L < 0.004) & (L > 1e-6)
        return L, ok
    mu, oku = d(pos, 1)
    mv, okv = d(pos, 0)
    med = float(np.median(np.concatenate([mu[oku], mv[okv]]))) if oku.any() else 0.0006
    return np.where(oku, mu, med), np.where(okv, mv, med), med


def phys_normal(h, pos, cov, gain=1.0):
    """tangent-space (OpenGL, +Y = up in the image) normal of the height field h (metres); h outside the coverage is filled from the nearest covered texel first."""
    hf = pushpull(h[..., None].astype(np.float32), cov)[..., 0].astype(np.float64)
    mu, mv, med = texel_metric(pos, cov)
    gx = (np.roll(hf, -1, 1) - np.roll(hf, 1, 1)) * 0.5 / mu
    gy = (np.roll(hf, 1, 0) - np.roll(hf, -1, 0)) * 0.5 / mv
    n = np.stack([-gx * gain, -gy * gain, np.ones_like(hf)], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def cavity(h, r_px, scale, cov=None):
    """0..1 in the valleys: how far the height sits under its own blur (holes of the UV layout are filled from the nearest covered texel first, so island borders give no ring)."""
    if cov is not None:
        h = pushpull(h[..., None].astype(np.float32), cov)[..., 0].astype(np.float64)
    return np.clip((gblur(h, r_px) - h) / scale, 0, 1)


def groove(d, w):
    return np.exp(-(d / w) ** 2)


def mesh_diamond(size, period):
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    return np.cos(2 * np.pi * (xx + yy) / period) * np.cos(2 * np.pi * (xx - yy) / period)


def knit(size, period):
    """small braided knit: diagonal ridges that alternate."""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    return 0.5 * np.sin(2 * np.pi * (xx + yy) / period) + 0.5 * np.sin(2 * np.pi * (xx - yy) / (period * 1.5))


def landmarks(arm):
    B = arm.data.bones
    g = lambda n: np.array(B[n].head_local)
    J = g("rig:LeftArm"); E = g("rig:LeftForeArm"); W = g("rig:LeftHand")
    a = (E - J) / np.linalg.norm(E - J)
    up = np.array([0, 0, 1.0])
    e1 = up - up.dot(a) * a
    e1 /= np.linalg.norm(e1)
    e2 = np.cross(a, e1)
    return dict(J=J, E=E, W=W, a=a, e1=e1, e2=e2, H=g("rig:LeftUpLeg"), K=g("rig:LeftLeg"), A=g("rig:LeftFoot"))


def vertex_boundary_dist(obj):
    """per-vertex euclidean distance to the open boundary of the mesh (hem, sleeve ends), as an attribute for the UV rasteriser."""
    import bmesh
    from mathutils.kdtree import KDTree
    bm = bmesh.new(); bm.from_mesh(obj.data)
    bpts = [v.co.copy() for v in bm.verts if any(e.is_boundary for e in v.link_edges)]
    n = len(bm.verts)
    bm.free()
    d = np.full(n, 1.0)
    if not bpts:
        return d
    kd = KDTree(len(bpts))
    for i, p in enumerate(bpts):
        kd.insert(p, i)
    kd.balance()
    co = obj.data.vertices
    for i in range(n):
        d[i] = kd.find(co[i].co)[2]
    return d


def bake_with_boundary(obj, size):
    uv, tl, attr = uv_attrs(obj, extra_vertex_attrs=vertex_boundary_dist(obj)[:, None])
    out, cov = ppc.rasterise(uv, tl, attr, size)
    return out[..., 0:3], out[..., 3:6], out[..., 6], cov


# ------------------------------------------------------------------------------------------------ jersey
def jersey_fields(arm, pos, nrm, bd, cov, size):
    """returns dict(h = height in metres, ao = multiplier 0..1, panel = side mesh panel mask, seam = seam groove 0..1)."""
    L = landmarks(arm)
    x, y, z = pos[..., 0], pos[..., 1], pos[..., 2]
    ax = np.abs(x)
    ny = nrm[..., 1]
    N = lambda s, lo=1, hi=6, an=(1.0, 1.0): noise(size, lo, hi, s, an)
    H = np.zeros((size, size))
    ao = np.ones((size, size))
    # arm frame (left arm, mirrored)
    q = np.stack([ax, y, z], -1) - L["J"]
    s = q @ L["a"]
    rv = q - s[..., None] * L["a"]
    ra = np.linalg.norm(rv, axis=-1)
    tha = np.arctan2(rv @ L["e2"], rv @ L["e1"])
    sleeve = sstep(0.015, 0.05, s) * (1 - sstep(0.095, 0.125, ra))
    surf_fb = sstep(0.25, 0.6, np.abs(ny))                       # front / back faces, not the flank
    front = sstep(0.1, 0.5, -ny)
    back = sstep(0.1, 0.5, ny)

    # 1 armpit tension folds: rays from the armpit over the chest / the back (the fabric pulled by the raised arm)
    Ap = np.array([0.19, 0.075, 1.455])
    dx, dz = Ap[0] - ax, z - Ap[2]
    r = np.hypot(dx, dz)
    th = np.arctan2(dz, dx)
    env = sstep(0.03, 0.06, r) * (1 - sstep(0.13, 0.27, r)) * sstep(1.45, 0.5, np.abs(th + 0.30)) * surf_fb * (1 - sleeve)
    gate = sstep(-0.35, 0.40, N(11, 1, 5))
    F1 = 0.0021 * ridge(24.0 * th + 6.0 * N(12, 1, 4)) * env * gate
    # a second, finer set that fans a little higher (the pull over the pad edge)
    env2 = sstep(0.05, 0.08, r) * (1 - sstep(0.10, 0.17, r)) * sstep(0.9, 0.2, np.abs(th - 0.65)) * surf_fb * (1 - sleeve)
    F1b = 0.0014 * ridge(34.0 * th + 5.0 * N(13, 1, 4)) * env2 * sstep(-0.2, 0.5, N(14, 1, 5))
    # 2 waist tuck (v0.13 review: the even horizontal bands over the stomach read as a quilted jacket): only a short, irregular gather where the jersey tucks at the belt, fading out over the stomach,
    # amplitude -70 %, spacing and direction varied by noise, broken into patches, nothing above the lower ribs
    phi = np.arctan2(y - 0.07, x)
    zz = z + 0.014 * N(21, 1, 4) + 0.010 * np.sin(2 * phi + 1.0) + 0.012 * N(24, 1, 3) * (ax / 0.2)
    spacing = 0.020 + 0.012 * (0.5 + 0.5 * N(25, 1, 3))
    env = sstep(1.095, 1.115, z) * (1 - sstep(1.135, 1.19, z)) * (1 - sleeve)
    amp = 0.00036 * (0.35 + 0.65 * np.abs(np.cos(phi)) + 0.25 * back)
    F2 = amp * ridge(2 * np.pi * zz / spacing + 2.0 * N(23, 1, 4)) * env * sstep(0.0, 0.5, N(22, 1, 5))
    # 3 long vertical hang folds on the back (and a few on the belly): loose cloth falling from the shoulder blades
    zb = sstep(1.13, 1.22, z) * (1 - sstep(1.45, 1.55, z))
    xb = x + 0.012 * N(31, 1, 4)
    F3 = 0.0007 * ridge(2 * np.pi * xb / 0.052 + 2.0 * N(32, 1, 5)) * back * zb * (1 - sleeve) * (ax < 0.2) * sstep(-0.3, 0.4, N(33, 1, 6))
    # 4 shoulder: the jersey drapes over the pad dome and runs down the arm in tension folds along the arm; a soft groove where it turns off the dome
    upper = sstep(-0.2, 0.5, np.cos(tha))
    envs = sstep(0.0, 0.03, s) * (1 - sstep(0.075, 0.17, s)) * sstep(0.05, 0.075, ra) * (1 - sstep(0.115, 0.15, ra))
    F4 = 0.00065 * ridge(2 * np.pi * tha * 0.09 / 0.030 + 3.0 * N(41, 1, 4)) * envs * (0.4 + 0.6 * upper) * sstep(-0.3, 0.4, N(42, 1, 5))
    F4 -= 0.0003 * groove(s - 0.085, 0.010) * sstep(0.05, 0.075, ra) * (1 - sstep(0.115, 0.15, ra)) * sleeve
    # 5 trapezius / neck drape: rays from the collar over the top of the shoulders
    rho = np.hypot(x, y - 0.062)
    thn = np.arctan2(y - 0.062, x)
    envn = sstep(0.135, 0.16, rho) * (1 - sstep(0.19, 0.27, rho)) * sstep(1.50, 1.54, z)
    F5 = 0.0008 * ridge(26.0 * thn + 4.0 * N(51, 1, 4)) * envn * sstep(-0.3, 0.4, N(52, 1, 5))
    # 6 hems: waist and sleeve ends: ripple, folded-over edge, stitch row with dashes
    arc_w = phi * 0.20
    arc_s = tha * 0.075
    is_sl = sstep(0.1, 0.5, sleeve + (ax > 0.215) * 0.5)
    arc = np.where(is_sl > 0.5, arc_s, arc_w)
    hem_env = 1 - sstep(0.0, 0.055, bd)
    F6 = 0.0005 * ridge(2 * np.pi * arc / 0.045 + 3.0 * N(61, 1, 4)) * hem_env
    F6 += 0.0006 * sstep(0.014, 0.011, bd) * (bd > 0.0005)
    stitch = groove(bd - 0.0165, 0.0011)
    dash = sstep(-0.2, 0.3, np.sin(2 * np.pi * arc / 0.0036))
    F6 -= 0.0006 * stitch * (0.5 + 0.5 * dash)
    hem_ao = 1 - 0.25 * groove(bd - 0.0165, 0.004)
    # 7 seams: side seam (flat-felled), armscye ring, shoulder-top seam; stitch dashes along each
    zs_ = np.linspace(1.09, 1.60, 52)
    xs_ = np.zeros_like(zs_); ys_ = np.zeros_like(zs_)
    # side line of the torso: per height, the y of the most lateral torso texels
    tor = cov & (ax < 0.215) & (np.abs(ny) < 0.6)
    zi = np.clip(((z - 1.09) / (1.60 - 1.09) * 51).astype(int), 0, 51)
    for k in range(52):
        m = tor & (zi == k)
        if m.sum() > 30:
            vals = ax[m]
            sel = m & (ax >= np.quantile(vals, 0.93))
            xs_[k] = ax[sel].mean(); ys_[k] = y[sel].mean()
        else:
            xs_[k] = np.nan
    good = ~np.isnan(xs_)
    ys_i = np.interp(z, zs_[good], ys_[good])
    t = y - ys_i
    side_zone = (z > 1.085) * (1 - sstep(1.50, 1.56, z)) * (ax < 0.215) * (1 - sleeve)
    seam_side = groove(t, 0.0030) * side_zone
    ridge_side = 0.5 * (groove(t - 0.008, 0.0035) + groove(t + 0.008, 0.0035)) * side_zone
    dash_side = sstep(-0.2, 0.3, np.sin(2 * np.pi * z / 0.0038))
    F7 = -0.0010 * seam_side * (0.55 + 0.45 * dash_side) + 0.0005 * ridge_side
    # armscye ring: on the plane through the shoulder joint, around the arm root
    ring_zone = sstep(0.04, 0.065, ra) * (1 - sstep(0.115, 0.14, ra))
    seam_arm = groove(s - 0.0, 0.0028) * ring_zone
    dash_arm = sstep(-0.2, 0.3, np.sin(2 * np.pi * tha * ra / 0.0038))
    F7 += -0.0006 * seam_arm * (0.55 + 0.45 * dash_arm) + 0.0003 * 0.5 * (groove(s - 0.009, 0.0035) + groove(s + 0.009, 0.0035)) * ring_zone
    seam = np.clip(seam_side + seam_arm, 0, 1)
    # 8 side mesh panels (ventilated knit under the arms, as on real game jerseys): bigger diamond mesh, a touch darker
    panel = sstep(0.115, 0.075, np.abs(t)) * sstep(1.10, 1.16, z) * (1 - sstep(1.36, 1.44, z)) * (ax < 0.215) * (1 - sleeve)
    mesh_big = mesh_diamond(size, 10.0)
    # 9 fabric: fine knit everywhere, crumple, a little large-scale unevenness
    wk = knit(size, 6.0)
    crumple = N(71, 14, 90, (1.0, 0.8))
    Fw = 0.00007 * wk * (1 - panel) + 0.00032 * mesh_big * panel + 0.00028 * crumple + 0.0006 * N(72, 4, 22)
    H = F1 + F1b + F2 + F3 + F4 + F5 + F6 + F7 + Fw
    # ---- ambient occlusion / cavity
    cav = cavity(H, 5, 0.0016, cov)
    ao *= 1 - 0.55 * cav
    r3 = np.linalg.norm(np.stack([ax, y, z], -1) - Ap, axis=-1)
    ao *= 1 - 0.30 * np.exp(-(r3 / 0.055) ** 2) * (1 - 0.3 * sleeve)         # armpit pocket
    ao *= 1 - 0.07 * env_band(z, 1.095, 1.14)                                  # waist tuck
    ao *= 1 - 0.10 * panel * (0.5 + 0.5 * (mesh_big < -0.2))                   # holes of the mesh panel
    ao *= 1 - 0.10 * groove(s - 0.0, 0.006) * ring_zone * 0.6                  # armscye
    ao *= hem_ao
    return dict(h=H, ao=np.clip(ao, 0.45, 1.0), panel=panel, seam=seam)


def env_band(z, a, b):
    return 1 - sstep(a, b, z)


# ------------------------------------------------------------------------------------------------ pants
def pants_fields(arm, pos, nrm, bd, cov, size):
    L = landmarks(arm)
    x, y, z = pos[..., 0], pos[..., 1], pos[..., 2]
    ax = np.abs(x)
    nx = nrm[..., 0] * np.where(x > 0, 1.0, -1.0)
    ny = nrm[..., 1]
    N = lambda s, lo=1, hi=6, an=(1.0, 1.0): noise(size, lo, hi, s, an)
    front = sstep(0.05, 0.5, -ny)
    back = sstep(0.05, 0.5, ny)
    Kz = L["K"][2]
    # 1 knee: creases across the front above the kneecap, deep V creases behind the knee
    zk = z - (Kz + 0.005)
    envf = sstep(-0.03, 0.01, zk) * (1 - sstep(0.07, 0.14, zk))
    F1 = 0.0017 * ridge(2 * np.pi * (zk + 0.010 * N(11, 1, 4)) / 0.026) * envf * front * sstep(-0.3, 0.4, N(12, 1, 5))
    envb = sstep(-0.06, -0.01, zk) * (1 - sstep(0.04, 0.09, zk))
    xv = np.abs(ax - 0.127)
    F1 += 0.0022 * ridge(2 * np.pi * (zk + 0.45 * xv + 0.008 * N(13, 1, 4)) / 0.024) * envb * back * sstep(-0.3, 0.4, N(14, 1, 5))
    # 2 hip / groin: fan of creases radiating from the crotch over the front of the thigh, and under the seat
    cz = 0.985
    dx, dz = ax - 0.02, z - cz
    r = np.hypot(dx, dz)
    th = np.arctan2(dz, dx)
    envg = sstep(0.03, 0.07, r) * (1 - sstep(0.12, 0.24, r)) * sstep(1.5, 0.5, np.abs(th - 0.35)) * front
    F2 = 0.0022 * ridge(22.0 * th + 5.0 * N(21, 1, 4)) * envg * sstep(-0.3, 0.4, N(22, 1, 5))
    envs = sstep(0.88, 0.95, z) * (1 - sstep(1.03, 1.08, z)) * back
    F2 += 0.0016 * ridge(2 * np.pi * (z + 0.012 * N(23, 1, 4) + 0.02 * np.abs(x)) / 0.029) * envs * sstep(-0.3, 0.4, N(24, 1, 5))
    # 3 thigh: soft tension folds running down the front of the thigh
    envt = sstep(0.62, 0.72, z) * (1 - sstep(0.93, 1.0, z)) * front
    F3 = 0.0009 * ridge(2 * np.pi * (ax + 0.02 * N(31, 1, 4) + 0.15 * (z - 0.8)) / 0.055) * envt * sstep(-0.2, 0.5, N(32, 1, 6))
    # 4 waistband: elastic ribs, two stitch rows, the pants gathered under the band
    top = Lz = 1.1445
    band = sstep(Lz - 0.040, Lz - 0.035, z)
    F4 = 0.00030 * ridge(2 * np.pi * z / 0.0042) * band
    F4 -= 0.0007 * groove(z - (Lz - 0.040), 0.0012) * (0.5 + 0.5 * sstep(-0.2, 0.3, np.sin(2 * np.pi * (np.arctan2(y - 0.07, x)) * 0.18 / 0.0036)))
    F4 -= 0.0007 * groove(z - (Lz - 0.012), 0.0012)
    gath = sstep(1.04, 1.075, z) * (1 - band)
    F4 += 0.0013 * ridge(2 * np.pi * (z + 0.010 * N(41, 1, 4) + 0.02 * np.sin(3 * np.arctan2(y - 0.07, x))) / 0.022) * gath * sstep(-0.35, 0.35, N(42, 1, 5))
    # 5 hem at the knee: stitch row, ripple
    zh = z - 0.531
    F5 = 0.0010 * ridge(2 * np.pi * (np.arctan2(y - 0.07, x) * 0.10) / 0.04 + 2.5 * N(51, 1, 4)) * (1 - sstep(0.0, 0.05, zh))
    F5 -= 0.0007 * groove(zh - 0.017, 0.0012)
    F5 += 0.0005 * sstep(0.017, 0.012, zh) * (zh > 0.0005)
    # 6 seams: inseam, and the sewn edges of the side stripe insert (same iso-line as the colour: nx = 0.82)
    xc = np.interp(z, [L["K"][2], L["H"][2]], [L["K"][0], L["H"][0]])
    yc = np.interp(z, [L["K"][2], L["H"][2]], [L["K"][1], L["H"][1]])
    psi = np.arctan2(y - yc, ax - xc)
    inner = np.pi - np.abs(psi)
    leg_zone = sstep(0.54, 0.58, z) * (1 - sstep(0.98, 1.04, z))
    seam_in = groove(inner * 0.10, 0.0030) * leg_zone * (ax < 0.18)
    seam_out = groove((nx - 0.82) / 5.7, 0.0030) * leg_zone
    dash = sstep(-0.2, 0.3, np.sin(2 * np.pi * z / 0.0038))
    F6 = -0.0011 * (seam_in + seam_out) * (0.55 + 0.45 * dash)
    F6 += 0.0004 * 0.5 * (groove(inner * 0.10 - 0.008, 0.0035) + groove(inner * 0.10 + 0.008, 0.0035)) * leg_zone * (ax < 0.18)
    seam = np.clip(seam_in + seam_out, 0, 1)
    # 7 fabric: twill + crumple
    tw = np.sin(2 * np.pi * (np.mgrid[0:size, 0:size][1] + np.mgrid[0:size, 0:size][0]) / 5.0)
    Fw = 0.00010 * tw + 0.00030 * N(61, 14, 90, (1.0, 0.8)) + 0.0006 * N(62, 4, 22)
    H = F1 + F2 + F3 + F4 + F5 + F6 + Fw
    ao = np.ones((size, size))
    cav = cavity(H, 5, 0.0016, cov)
    ao *= 1 - 0.55 * cav
    ao *= 1 - 0.30 * np.exp(-(np.hypot(ax - 0.02, z - cz) / 0.05) ** 2) * front                 # groin
    ao *= 1 - 0.22 * np.exp(-((z - Kz) / 0.035) ** 2) * back                                    # back of the knee
    ao *= 1 - 0.14 * sstep(0.0, 0.05, 0.05 - np.abs(z - 1.07)) * 0.0
    ao *= 1 - 0.10 * (1 - sstep(0.0, 0.04, zh))
    ao *= 1 - 0.18 * groove(z - (Lz - 0.040), 0.004)
    return dict(h=H, ao=np.clip(ao, 0.45, 1.0), seam=seam)


# ------------------------------------------------------------------------------------------------ compression sleeve
def sleeve_fields(arm, pos, nrm, cov, size):
    L = landmarks(arm)
    x, y, z = pos[..., 0], pos[..., 1], pos[..., 2]
    ax = np.abs(x)
    ny = nrm[..., 1]
    N = lambda s, lo=1, hi=6, an=(1.0, 1.0): noise(size, lo, hi, s, an)
    p = np.stack([ax, y, z], -1)
    a1 = (L["E"] - L["J"]); a1 /= np.linalg.norm(a1)
    a2 = (L["W"] - L["E"]); a2 /= np.linalg.norm(a2)
    d1 = (p - L["E"]) @ a1
    d2 = (p - L["E"]) @ a2
    de = np.where(d2 > 0, d2, d1)                 # signed distance along the arm bones from the elbow
    crook = sstep(-0.25, 0.65, -ny)               # the inside of the elbow faces the front
    env = (1 - sstep(0.05, 0.11, np.abs(de)))
    F1 = 0.0016 * ridge(2 * np.pi * (de + 0.006 * N(11, 1, 4)) / 0.0145 + 1.2 * np.abs(ny)) * env * (0.25 + 0.75 * crook) * sstep(-0.4, 0.3, N(12, 1, 5))
    # wrist: the compression sleeve pushed down into rings
    dw = (p - L["W"]) @ a2
    F2 = 0.0013 * ridge(2 * np.pi * (dw + 0.004 * N(21, 1, 4)) / 0.0125) * (1 - sstep(0.06, 0.12, np.abs(dw))) * (dw < 0.03)
    # biceps / shoulder: long soft tension folds
    ds = (p - L["J"]) @ a1
    F3 = 0.0009 * ridge(2 * np.pi * (p[..., 1] * 1.0 + 0.5 * p[..., 2] + 0.02 * N(31, 1, 4)) / 0.040) * sstep(0.02, 0.06, ds) * (1 - sstep(0.12, 0.2, ds)) * sstep(-0.3, 0.4, N(32, 1, 5))
    H = F1 + F2 + F3 + 0.00020 * N(41, 14, 90) + 0.0004 * N(42, 4, 22)
    ao = np.ones((size, size))
    ao *= 1 - 0.55 * cavity(H, 5, 0.0014, cov)
    ao *= 1 - 0.16 * np.exp(-(de / 0.03) ** 2) * crook
    return dict(h=H, ao=np.clip(ao, 0.5, 1.0))


def combine_normals(base_rgb, fold_rgb):
    """adds the slopes of two tangent-space normal maps (0..1 encoded, OpenGL)."""
    b = base_rgb * 2 - 1
    f = fold_rgb * 2 - 1
    n = np.stack([b[..., 0] / np.maximum(b[..., 2], 0.3) + f[..., 0] / np.maximum(f[..., 2], 0.3),
                  b[..., 1] / np.maximum(b[..., 2], 0.3) + f[..., 1] / np.maximum(f[..., 2], 0.3), np.ones(b.shape[:2])], -1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5
