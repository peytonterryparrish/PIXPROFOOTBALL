"""PHR: generates the ORIGINAL (team-colourable) uniform texture set for the photoreal player on the R1 UV layouts.

No pixel of R1's own jersey / pants / sock / helmet / towel / pouch albedo is used (those carry Bengals / NFL / Nike marks and the real player's name).
Everything is built from the mesh geometry (world position / normal baked into UV space) plus procedural cloth noise, so the masks follow the body.

Outputs (Assets/_Licensed/PhotorealPlayer/Textures/Uniform):  <part>_Gray.png (shading, sRGB), <part>_Team.png (R unused / G = secondary / B = tertiary, linear),
<part>_N.png (OpenGL normal, linear), <part>_Regions.png (dirt regions R knees G seat B front A back/shoulders, linear)
blender -b --python Tools/Blender/phr_textures.py -- [parts,comma,separated]"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_tex import *
import bmesh

OUT = os.environ.get("PHR_TEX_OUT") or os.path.join(OUT_ASSET, "Textures", "Uniform")   # v0.12.0 BODIES: PHR_TEX_OUT writes to a private stage folder
SLEEVE_BANDS = os.environ.get("PHR_SLEEVE_BANDS", "0") == "1"   # v0.12.0 BODIES: the sleeve-end bands land on the pad dome as black / white slabs (the jersey sleeve ends at the cap): off
SZ = {"jersey": 2048, "pants": 2048, "socks": 1024}


def island_edge(cov, width=3):
    """thin band along the UV island borders (= the real panel seams of the garment) as a soft 0..1 mask."""
    f = box_blur(cov.astype(np.float64), width)
    edge = cov & (f < 0.985)
    return gblur(edge.astype(np.float64), 1.2).clip(0, 1)


def mesh_pattern(size, period):
    """diamond lattice (athletic mesh / dimple) in -1..1"""
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    return np.cos(2 * np.pi * (xx + yy) / period) * np.cos(2 * np.pi * (xx - yy) / period)


def twill(size, period):
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float64)
    return np.sin(2 * np.pi * (xx + yy) / period)


def boundary_points(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    pts = np.array([[v.co.x, v.co.y, v.co.z] for v in bm.verts if any(e.is_boundary for e in v.link_edges)])
    bm.free()
    return pts


def min_dist(P, Q, chunk=200000):
    """distance from each row of P[N,3] to the closest row of Q[M,3]."""
    out = np.empty(len(P))
    for i in range(0, len(P), chunk):
        d = P[i:i + chunk, None, :] - Q[None, :, :]
        out[i:i + chunk] = np.sqrt((d * d).sum(-1)).min(axis=1)
    return out


def finish(name, gray, team_g, team_b, normal, regions, cov, size):
    """Writes the four maps with edge dilation."""
    g = np.stack([gray, gray, gray], -1)
    g = fill_dilate(g, cov, 24)
    save(f"{OUT}/{name}_Gray.png", g, True)
    t = np.stack([np.zeros_like(team_g), team_g, team_b], -1)
    t = fill_dilate(t, cov, 24)
    save(f"{OUT}/{name}_Team.png", t, False)
    n = fill_dilate(normal, cov, 24)
    save(f"{OUT}/{name}_N.png", n, False)
    r = fill_dilate(regions, cov, 24)
    save(f"{OUT}/{name}_Regions.png", r, False)
    print("wrote", name, size)


def jersey(arm, meshes):
    size = SZ["jersey"]
    o = meshes["jersey"]
    pos, nrm, cov = bake_world(o, size)
    x, y, z = pos[..., 0], pos[..., 1], pos[..., 2]
    ax = np.abs(x)
    # --- design masks
    # sleeve end bands (distance along the arm axis from the shoulder joint)
    ptsL = np.array(arm.data.bones["rig:LeftArm"].head_local)
    fL = np.array(arm.data.bones["rig:LeftForeArm"].head_local)
    d = (fL - ptsL) / np.linalg.norm(fL - ptsL)
    ptsR = np.array(arm.data.bones["rig:RightArm"].head_local)
    fR = np.array(arm.data.bones["rig:RightForeArm"].head_local)
    dR = (fR - ptsR) / np.linalg.norm(fR - ptsR)
    sL = (pos - ptsL) @ d
    sR = (pos - ptsR) @ dR
    s = np.where(x > 0, sL, sR)
    sleeve = (ax > 0.17) & (np.where(x > 0, 1, 0) >= 0)
    sec = np.zeros((size, size))
    ter = np.zeros((size, size))
    band2 = smoothstep(0.014, 0.017, s) * (1 - smoothstep(0.030, 0.033, s))      # wide secondary band
    band3 = smoothstep(0.036, 0.038, s) * (1 - smoothstep(0.046, 0.048, s))      # thin tertiary band
    if SLEEVE_BANDS:
        sec = np.maximum(sec, band2 * sleeve)
        ter = np.maximum(ter, band3 * sleeve)
    # collar trim: the neck opening is not an open boundary on R1's jersey (closed collar fold), so use the distance to the neck axis
    rho = np.sqrt(x ** 2 + (y - 0.062) ** 2)
    collar = (1 - smoothstep(0.108, 0.112, rho)) * (z > 1.49)
    collar2 = (1 - smoothstep(0.122, 0.126, rho)) * smoothstep(0.114, 0.118, rho) * (z > 1.49)
    sec = np.maximum(sec, collar2)
    ter = np.maximum(ter, collar)
    # side panels: thin tertiary lines along both sides of the torso
    side = (np.abs(nrm[..., 0]) > 0.93) & (z < 1.42) & (ax < 0.26) & (ax > 0.10)
    side_line = smoothstep(0.93, 0.95, np.abs(nrm[..., 0])) * (1 - smoothstep(0.985, 0.995, np.abs(nrm[..., 0])))
    ter = np.maximum(ter, side_line * (z < 1.40) * (z > 1.12) * 0.0)
    sec = gblur(sec, 0.8)
    ter = gblur(ter, 0.8)
    # --- shading: soft folds + athletic mesh over the torso, ribbed collar, seam grooves along the island borders
    h = cloth_height(size, 11, 0.6)
    mesh = mesh_pattern(size, 13.0)
    mesh_zone = smoothstep(1.10, 1.14, z) * (1 - smoothstep(1.40, 1.44, z)) * (1 - collar) * (1 - collar2) * (ax < 0.22)
    rib = np.sin(np.clip(rho, 0, 1) * 2 * np.pi / 0.0045)
    rib_zone = smoothstep(0.100, 0.108, rho) * (1 - smoothstep(0.126, 0.134, rho)) * (z > 1.49)
    seam = island_edge(cov, 3)
    gray = 0.80 + 0.05 * fft_noise(size, 1, 6, (1, 1), 21) + 0.04 * h
    gray *= 1.0 - 0.10 * smoothstep(0.30, 0.0, z - 1.12)
    gray *= 1.0 - 0.07 * np.maximum(-mesh, 0) * mesh_zone          # dark holes of the mesh
    gray *= 1.0 - 0.0 * seam                                       # (island borders are not real seams on this garment: no darkening)
    gray *= 1.0 - 0.06 * rib * rib_zone
    nA = height_to_normal(h, 9.0)
    nB = height_to_normal(0.35 * mesh * mesh_zone + 0.5 * rib * rib_zone, 1.2)
    normal = np.clip(nA + (nB - 0.5), 0, 1)
    # --- dirt regions: B = front, A = back + shoulders
    front = smoothstep(0.35, 0.8, -nrm[..., 1]) * smoothstep(1.08, 1.16, z) * (1 - smoothstep(1.34, 1.46, z))
    back = smoothstep(0.35, 0.8, nrm[..., 1]) * smoothstep(1.08, 1.16, z)
    shoulders = smoothstep(1.40, 1.48, z) * 1.0
    regions = np.zeros((size, size, 4))
    regions[..., 2] = front
    regions[..., 3] = np.maximum(back, shoulders)
    regions = regions[..., :4]
    save_regions(f"{OUT}/jersey_Regions.png", regions, cov)
    finish_noreg("jersey", gray, sec, ter, normal, cov, size)


def save_regions(path, reg, cov):
    r = fill_dilate(reg, cov, 24)
    save(path, r, False)


def finish_noreg(name, gray, team_g, team_b, normal, cov, size):
    g = np.stack([gray, gray, gray], -1)
    g = fill_dilate(g, cov, 24)
    save(f"{OUT}/{name}_Gray.png", g, True)
    t = np.stack([np.zeros_like(team_g), team_g, team_b], -1)
    t = fill_dilate(t, cov, 24)
    save(f"{OUT}/{name}_Team.png", t, False)
    n = fill_dilate(normal, cov, 24)
    save(f"{OUT}/{name}_N.png", n, False)
    print("wrote", name, size)


def pants(arm, meshes):
    size = SZ["pants"]
    o = meshes["pants"]
    pos, nrm, cov = bake_world(o, size)
    x, y, z = pos[..., 0], pos[..., 1], pos[..., 2]
    side = np.where(x > 0, 1.0, -1.0)
    outer = nrm[..., 0] * side            # +1 = pointing away from the body centre
    sec = np.zeros((size, size))
    ter = np.zeros((size, size))
    # wide outer-leg stripe + thin border lines
    core = smoothstep(0.80, 0.84, outer)
    border = smoothstep(0.62, 0.66, outer) * (1 - smoothstep(0.76, 0.80, outer))
    on_leg = (z < 1.05) * (z > 0.545)
    sec = np.maximum(sec, core * on_leg)
    ter = np.maximum(ter, border * on_leg)
    # hem trim at the bottom of the knee
    hem = 1 - smoothstep(0.540, 0.548, z)
    ter = np.maximum(ter, hem)
    sec = gblur(sec, 0.8)
    ter = gblur(ter, 0.8)
    h = cloth_height(size, 31, 0.5)
    seam = island_edge(cov, 3)
    tw = twill(size, 5.0)
    gray = 0.82 + 0.04 * fft_noise(size, 1, 6, (1, 1), 41) + 0.035 * h
    gray *= 1.0
    gray *= 1.0 + 0.012 * tw
    nA = height_to_normal(h, 8.0)
    nB = height_to_normal(0.25 * tw, 1.0)
    normal = np.clip(nA + (nB - 0.5), 0, 1)
    reg = np.zeros((size, size, 4))
    knees = smoothstep(0.80, 0.68, z) * smoothstep(0.1, 0.45, -nrm[..., 1])
    reg[..., 0] = knees
    reg[..., 1] = smoothstep(0.80, 0.95, z) * smoothstep(-0.2, 0.3, nrm[..., 1]) + smoothstep(0.80, 0.95, z) * 0.3
    reg[..., 1] = np.clip(reg[..., 1], 0, 1)
    save_regions(f"{OUT}/pants_Regions.png", reg, cov)
    finish_noreg("pants", gray, sec, ter, normal, cov, size)


def socks(arm, meshes):
    size = SZ["socks"]
    o = meshes["socks"]
    pos, nrm, cov = bake_world(o, size)
    z = pos[..., 2]
    sec = np.zeros((size, size))
    ter = np.zeros((size, size))
    b1 = smoothstep(0.388, 0.392, z) * (1 - smoothstep(0.414, 0.418, z))
    b2 = smoothstep(0.428, 0.431, z) * (1 - smoothstep(0.442, 0.445, z))
    sec = np.maximum(sec, b1)
    ter = np.maximum(ter, b2)
    sec = gblur(sec, 0.6)
    ter = gblur(ter, 0.6)
    h = cloth_height(size, 51, 0.55)
    # ribbing: fine vertical lines (along the sock axis = world z => UV direction unknown, use both small amplitude)
    rib = np.sin(np.arange(size)[None, :] * (2 * np.pi * 96 / size)) * 0.5
    gray = 0.80 + 0.03 * fft_noise(size, 1, 6, (1, 1), 61) + 0.04 * h + 0.012 * rib
    normal = height_to_normal(h + 0.12 * rib, 13.0)
    reg = np.zeros((size, size, 4))
    save_regions(f"{OUT}/socks_Regions.png", reg, cov)
    finish_noreg("socks", gray, sec, ter, normal, cov, size)


if __name__ == "__main__":
    a = args()
    which = a[0].split(",") if a else ["jersey", "pants", "socks"]
    reset()
    arm, meshes = import_r1()
    for w in which:
        globals()[w](arm, meshes)
