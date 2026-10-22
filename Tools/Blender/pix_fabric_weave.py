"""Procedural, TILEABLE fabric micro-detail textures for the player uniforms (numpy only, no render engine).

  blender -b --python Tools/Blender/pix_fabric_weave.py -- [--out Assets/_Licensed/PIX/V6/Textures] [--size 512] [--seed 7]
        [--strength 1.0] [--debug Logs/blender_v6/fabric_debug.png]
  (also runs with any plain python that has numpy: python pix_fabric_weave.py --out ... )

Writes
  PIX_FabricWeave_Normal.png  512x512 RGB, OpenGL / Y+ tangent-space normal map. Athletic jersey "mesh": a fine plain-weave yarn
                              structure (4 px thread period) plus a diamond lattice of small perforations (16 px pitch) with soft
                              rims and a little fibre noise. Intended as the HDRP detail-map normal (tile ~ 8-12 cm per repeat).
  PIX_FabricWeave_AO.png      512x512 8-bit grey cavity map of the same height field (use as detail AO / multiply on the mask-map AO).
  PIX_Knit_Normal.png         512x512 RGB OpenGL normal: stretch knit / spandex - fine horizontal ribbing (4 px course period, 128
                              courses per tile) with slightly zig-zagged V stitches (8 px wale period) and low fibre noise.
All lattice periods divide the tile size, the noise is generated in the Fourier domain, and every derivative uses periodic differences,
so the textures tile seamlessly. The debug image tiles each 3x3 (half resolution) and shows a lit preview.
"""
import sys, os, zlib, struct
import numpy as np


def script_args():
    if "--" in sys.argv:
        return sys.argv[sys.argv.index("--") + 1:]
    return [] if "--python" in sys.argv else sys.argv[1:]


ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


# ------------------------------------------------------------------ PNG writer (8 bit, grey or RGB)
def write_png(path, a):
    a = np.ascontiguousarray(a)
    h, w = a.shape[:2]
    ch = 1 if a.ndim == 2 else a.shape[2]
    ctype = {1: 0, 3: 2, 4: 6}[ch]
    raw = b"".join(b"\x00" + a[y].tobytes() for y in range(h))
    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, ctype, 0, 0, 0)) + \
          chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "wb") as f:
        f.write(png)


# ------------------------------------------------------------------ helpers (all periodic)
def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def periodic_noise(n, rng, cutoff_lo, cutoff_hi):
    """Band-limited tileable noise in [-1,1] (white noise filtered in Fourier space, radial band cutoff_lo..cutoff_hi in cycles/tile)."""
    f = np.fft.fft2(rng.standard_normal((n, n)))
    fy = np.fft.fftfreq(n) * n; fx = np.fft.fftfreq(n) * n
    r = np.sqrt(fx[None, :] ** 2 + fy[:, None] ** 2)
    filt = smoothstep(cutoff_lo * 0.7, cutoff_lo, r) * (1 - smoothstep(cutoff_hi, cutoff_hi * 1.4, r))
    out = np.real(np.fft.ifft2(f * filt))
    return out / (np.abs(out).max() + 1e-9)


def blur_periodic(a, sigma):
    n = a.shape[0]
    fy = np.fft.fftfreq(n); fx = np.fft.fftfreq(n)
    g = np.exp(-2 * (np.pi ** 2) * sigma ** 2 * (fx[None, :] ** 2 + fy[:, None] ** 2))
    return np.real(np.fft.ifft2(np.fft.fft2(a) * g))


def normal_from_height(h, strength):
    """OpenGL (Y+) tangent-space normal from a periodic height field (image rows go DOWN, V goes UP): n = (-dh/dx, +dh/drow, 1)."""
    dx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    dr = (np.roll(h, -1, 0) - np.roll(h, 1, 0)) * 0.5
    n = np.stack([-dx * strength, dr * strength, np.ones_like(h)], -1)
    n /= np.linalg.norm(n, axis=2, keepdims=True)
    return n


def to_u8(n):
    return np.clip((n * 0.5 + 0.5) * 255.0 + 0.5, 0, 255).astype(np.uint8)


def cavity_ao(h, radius, strength):
    """Cheap tileable ambient occlusion from the height field: how far below the local (blurred) surface a pixel lies."""
    d = np.maximum(blur_periodic(h, radius) - h, 0.0)
    return np.clip(1.0 - strength * np.clip(d / (np.percentile(d, 99.5) + 1e-9), 0.0, 1.0), 0.0, 1.0)


# ------------------------------------------------------------------ fabrics
def weave_height(n, rng):
    y, x = np.mgrid[0:n, 0:n].astype(np.float64)
    p = 4.0                                                   # yarn period in px
    u, v = x / p, y / p
    fu, fv = u - np.floor(u), v - np.floor(v)
    par = ((np.floor(u) + np.floor(v)) % 2)                    # over / under checkerboard of a plain weave
    prof_h = np.sin(np.pi * fv) ** 0.8                         # horizontal thread cross-section (runs along x)
    prof_v = np.sin(np.pi * fu) ** 0.8                         # vertical thread
    # the thread that is "over" shows its full height, the one "under" dips toward the crossing
    over_h = prof_h * (0.55 + 0.45 * np.sin(np.pi * fu) ** 0.5 * 0.0 + 0.45 * (1 - np.abs(2 * fu - 1) ** 2))
    over_v = prof_v * (0.55 + 0.45 * (1 - np.abs(2 * fv - 1) ** 2))
    weave = np.where(par == 0, over_h, over_v * 0.92)
    weave = weave - weave.mean()
    # diamond lattice of perforations (rhombic grid, 16 px pitch, rows every 8 px with a half-pitch offset)
    P = 16.0
    row = np.floor(y / (P / 2) + 0.5)
    # toroidal distance to the nearest of the 2 candidate lattice points on the nearest rows (tile size is a multiple of P)
    best = np.full((n, n), 1e9)
    for dr in (-1, 0, 1):
        rr = row + dr
        cyy = rr * (P / 2)
        ox = (rr % 2) * P / 2
        cxx = np.floor((x - ox) / P + 0.5) * P + ox
        for dc in (-1, 0, 1):
            ccx = cxx + dc * P
            dx = np.abs(x - ccx); dy = np.abs(y - cyy)
            dx = np.minimum(dx, n - dx); dy = np.minimum(dy, n - dy)
            # diamond (L1-ish) metric rounded with L2
            dd = np.sqrt(dx ** 2 * 0.85 + dy ** 2 * 1.5)
            best = np.minimum(best, dd)
    hole = 1.0 - smoothstep(1.2, 3.0, best)                    # 1 inside a perforation
    rim = smoothstep(2.2, 3.6, best) * (1 - smoothstep(3.6, 6.0, best))    # slightly raised fabric around each hole
    fibre = periodic_noise(n, rng, 40, 120)
    h = 0.30 * weave - 0.9 * hole + 0.22 * rim + 0.05 * fibre
    return h


def knit_height(n, rng):
    y, x = np.mgrid[0:n, 0:n].astype(np.float64)
    course = 4.0                                              # horizontal rib period (px) -> 128 ribs per tile
    wale = 8.0
    tri = np.abs(((x / wale) % 1.0) - 0.5) * 2.0              # 0..1 zig-zag along the wale direction
    yy = y + (tri - 0.5) * 1.1                                 # V-shaped stitches: ribs bend slightly per wale
    rib = 0.5 + 0.5 * np.cos(2 * np.pi * yy / course)
    rib = rib ** 1.4
    stitch = 0.78 + 0.22 * np.cos(2 * np.pi * x / wale + np.pi * 0.0)
    fibre = periodic_noise(n, rng, 30, 110)
    low = periodic_noise(n, rng, 2, 7)
    return 0.8 * rib * stitch + 0.07 * fibre + 0.05 * low


# ------------------------------------------------------------------ preview
def lit(n, light=(-0.5, 0.6, 0.62)):
    l = np.array(light) / np.linalg.norm(light)
    d = np.clip((n * l).sum(2), 0, 1)
    return np.clip(0.18 + 0.85 * d, 0, 1)


def main():
    args = script_args()
    out = os.path.join(ROOT, "Assets", "_Licensed", "PIX", "V6", "Textures")
    n = 512; seed = 7; strength = 1.0
    debug = os.path.join(ROOT, "Logs", "blender_v6", "fabric_debug.png")
    i = 0
    while i < len(args):
        if args[i] == "--out": out = args[i + 1]; i += 2
        elif args[i] == "--size": n = int(args[i + 1]); i += 2
        elif args[i] == "--seed": seed = int(args[i + 1]); i += 2
        elif args[i] == "--strength": strength = float(args[i + 1]); i += 2
        elif args[i] == "--debug": debug = args[i + 1]; i += 2
        else: raise SystemExit("unknown arg " + args[i])
    if n % 16:
        raise SystemExit("--size must be a multiple of 16 so the lattices tile")
    rng = np.random.default_rng(seed)
    hw = weave_height(n, rng)
    nw = normal_from_height(hw, 1.35 * strength)
    ao = cavity_ao(hw, 1.6, 0.85)
    hk = knit_height(n, rng)
    nk = normal_from_height(hk, 1.25 * strength)
    write_png(os.path.join(out, "PIX_FabricWeave_Normal.png"), to_u8(nw))
    write_png(os.path.join(out, "PIX_FabricWeave_AO.png"), (ao * 255 + 0.5).astype(np.uint8))
    write_png(os.path.join(out, "PIX_Knit_Normal.png"), to_u8(nk))
    for nm, a in (("weave normal", nw), ("knit normal", nk)):
        ang = np.degrees(np.arccos(np.clip(a[..., 2], -1, 1)))
        print("%-13s mean tilt %.1f deg, p95 %.1f deg, mean RGB %s" % (nm, ang.mean(), np.percentile(ang, 95), np.round((a * 0.5 + 0.5).reshape(-1, 3).mean(0), 3)))
    print("weave AO: mean %.3f min %.3f" % (ao.mean(), ao.min()))
    # tileability: difference across the wrap seam vs. difference between ordinary neighbours
    for nm, a in (("weave normal", nw), ("knit normal", nk)):
        seam = np.abs(a[:, 0] - a[:, -1]).mean(); ordinary = np.abs(a[:, 1:] - a[:, :-1]).mean()
        print("%-13s seam step %.4f vs mean neighbour step %.4f (ratio %.2f)" % (nm, seam, ordinary, seam / ordinary))
    # debug sheet: [lit weave 3x3 | AO 3x3 | lit knit 3x3] at half resolution
    def panel(img):
        t = np.tile(img, (3, 3) + (1,) * (img.ndim - 2)) if img.ndim == 3 else np.tile(img, (3, 3))
        return t[::2, ::2]
    lw = panel(np.repeat(lit(nw)[..., None], 3, 2)); la = panel(np.repeat(ao[..., None], 3, 2)); lk = panel(np.repeat(lit(nk)[..., None], 3, 2))
    nwp = panel(nw * 0.5 + 0.5); nkp = panel(nk * 0.5 + 0.5)
    sheet = np.concatenate([np.concatenate([lw, la, lk], 1), np.concatenate([nwp, np.repeat(np.zeros_like(la[..., :1]) + 0.5, 3, 2), nkp], 1)], 0)
    write_png(debug, (np.clip(sheet, 0, 1) * 255 + 0.5).astype(np.uint8))
    one = np.concatenate([np.repeat(lit(nw)[..., None], 3, 2), np.repeat(ao[..., None], 3, 2), np.repeat(lit(nk)[..., None], 3, 2)], 1)
    write_png(debug.replace(".png", "_1to1.png"), (np.clip(one, 0, 1) * 255 + 0.5).astype(np.uint8))      # lit weave | AO | lit knit at 1:1
    print("wrote", out, "and", debug)


main()
