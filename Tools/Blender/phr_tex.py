"""PHR texture helpers: UV-space baking of mesh attributes, procedural cloth normals, colour-mask previews, PNG IO (numpy, origin TOP-left).
Run inside Blender (imported by phr_textures.py / phr_build.py)."""
import os
import numpy as np
import bpy
from phr_common import *
import pix_player_common as ppc


# ----------------------------------------------------------------------------- UV bake
def uv_attrs(obj, extra_vertex_attrs=None):
    """Per-triangle-corner attributes of the active UV layer for rasterise(): world position (3) + world vertex normal (3)
    [+ extra per-vertex arrays]. Returns (uv[L,2], tri_loops[T,3], attr[T,3,C])."""
    co, nr, uv, tl, tv, tp = ppc.world_arrays(obj)
    cols = [co, nr]
    if extra_vertex_attrs is not None:
        cols.append(extra_vertex_attrs)
    A = np.concatenate(cols, axis=1)
    return uv, tl, A[tv]


def bake_world(obj, size):
    """Rasterise world position / normal into UV space. Returns pos[H,W,3], nrm[H,W,3], covered[H,W]."""
    uv, tl, attr = uv_attrs(obj)
    out, cov = ppc.rasterise(uv, tl, attr, size)
    return out[..., 0:3], out[..., 3:6], cov


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a + 1e-12), 0.0, 1.0)
    return t * t * (3 - 2 * t)


# ----------------------------------------------------------------------------- filters (numpy only)
def box_blur(a, r):
    """separable box blur with radius r (pixels), wraps at the borders (fine for masks that are dilated afterwards)."""
    if r <= 0:
        return a
    k = 2 * r + 1
    out = a.astype(np.float64)
    for axis in (0, 1):
        c = np.cumsum(np.concatenate([np.take(out, np.arange(-r, 0), axis=axis, mode='wrap'), out, np.take(out, np.arange(0, r), axis=axis, mode='wrap')], axis=axis), axis=axis)
        n = out.shape[axis]
        hi = np.take(c, np.arange(k - 1, k - 1 + n), axis=axis)
        lo = np.concatenate([np.zeros_like(np.take(c, [0], axis=axis)), np.take(c, np.arange(0, n - 1), axis=axis)], axis=axis)
        out = (hi - lo) / k
    return out


def gblur(a, sigma):
    r = max(1, int(sigma * 1.6))
    out = a
    for _ in range(3):
        out = box_blur(out, max(1, r // 2 + 1))
    return out


def fft_noise(size, lo, hi, aniso=(1.0, 1.0), seed=0, tileable=True):
    """Band-limited tileable noise in [-1, 1] (lo / hi = frequency band in cycles per texture, aniso scales the x / y frequencies)."""
    rng = np.random.RandomState(seed)
    n = rng.randn(size, size)
    F = np.fft.rfft2(n)
    fy = np.fft.fftfreq(size)[:, None] * size / aniso[1]
    fx = np.fft.rfftfreq(size)[None, :] * size / aniso[0]
    f = np.sqrt(fx * fx + fy * fy)
    band = np.exp(-(f / hi) ** 2) * (1.0 - np.exp(-(f / max(lo, 1e-3)) ** 2))
    out = np.fft.irfft2(F * band, s=(size, size))
    out /= max(np.abs(out).max(), 1e-9)
    return out


def height_to_normal(h, strength, wrap=True):
    """OpenGL tangent-space normal (+Y = up in the image, i.e. towards smaller row index) from a height field (top-left origin)."""
    gx = (np.roll(h, -1, 1) - np.roll(h, 1, 1)) * 0.5
    gy = (np.roll(h, 1, 0) - np.roll(h, -1, 0)) * 0.5     # up = -row
    n = np.stack([-gx * strength, -gy * strength, np.ones_like(h)], axis=-1)
    n /= np.linalg.norm(n, axis=-1, keepdims=True)
    return n * 0.5 + 0.5


def cloth_height(size, seed, fold=0.5, weave=0.0):
    h = fft_noise(size, 2, 22, (1.0, 0.45), seed) * fold                # long soft folds
    h += fft_noise(size, 10, 60, (1.0, 0.7), seed + 1) * 0.25 * fold    # crumple
    h += fft_noise(size, 60, 200, (1.0, 1.0), seed + 2) * 0.05          # fine grain
    return h


# ----------------------------------------------------------------------------- IO
def save(path, arr, srgb):
    ensure(os.path.dirname(path))
    ppc.save_png(path, np.clip(arr, 0, 1), color_space='sRGB' if srgb else 'Non-Color')


def load(path, srgb=True):
    a, nch = ppc.load_png(path, color_space='sRGB' if srgb else 'Non-Color')
    return a


def flat_normal(size):
    n = np.zeros((size, size, 3), np.float32)
    n[..., 0] = 0.5
    n[..., 1] = 0.5
    n[..., 2] = 1.0
    return n


def pushpull(img, cov, levels=None):
    """Push-pull hole filling: uncovered pixels take the (coverage weighted) colour of the coarser pyramid levels; covered pixels are kept.
    img [H,W,C] float, cov [H,W] bool.  Fast replacement for iterative dilation (edge padding for mip-safe UV textures)."""
    h, w = cov.shape
    wgt = cov.astype(np.float64)
    pyr = [(img.astype(np.float64) * wgt[..., None], wgt)]
    n = 0
    while min(pyr[-1][1].shape) > 1 and (levels is None or n < levels):
        a, b = pyr[-1]
        hh, ww = a.shape[0] // 2 * 2, a.shape[1] // 2 * 2
        a2 = a[:hh, :ww].reshape(hh // 2, 2, ww // 2, 2, -1).sum(axis=(1, 3))
        b2 = b[:hh, :ww].reshape(hh // 2, 2, ww // 2, 2).sum(axis=(1, 3))
        pyr.append((a2, b2))
        n += 1
    # pull back down: fill pixels with no weight from the coarser level
    fill = None
    for a, b in reversed(pyr):
        col = a / np.maximum(b, 1e-9)[..., None]
        if fill is not None:
            up = np.repeat(np.repeat(fill, 2, axis=0), 2, axis=1)
            up = np.pad(up, ((0, a.shape[0] - up.shape[0]), (0, a.shape[1] - up.shape[1]), (0, 0)), mode='edge')
            col = np.where((b > 1e-9)[..., None], col, up)
        fill = col
    out = np.where(cov[..., None], img, fill)
    return out.astype(np.float32)


def fill_dilate(img, cov, it=16):
    return pushpull(img, cov)


# ----------------------------------------------------------------------------- team-colour preview (mirrors PixLitHooks mode 2)
def compose_uniform(gray, mask, primary, secondary, tertiary):
    """gray [H,W] 0..1, mask [H,W,3] -> linear RGB preview of PixLitHooks uniform mode (without numbers / dirt).  Colours are linear rgb."""
    m = np.clip((mask - 0.5) * 3.3 + 0.5, 0, 1)
    team = np.array(primary)[None, None, :] * (1 - m[..., 1:2]) + np.array(secondary)[None, None, :] * m[..., 1:2]
    team = team * (1 - m[..., 2:3]) + np.array(tertiary)[None, None, :] * m[..., 2:3]
    return np.clip(gray[..., None] * team * 1.12, 0, 1)


def srgb_to_lin(c):
    c = np.asarray(c, np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def lin_to_srgb(c):
    c = np.clip(np.asarray(c, np.float64), 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * np.power(c, 1 / 2.4) - 0.055)
