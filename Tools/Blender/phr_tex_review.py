"""PHR: contact sheet of the final uniform texture set (composed albedo in team colours + normals) for visual review (no logos / marks / numbers allowed).
blender -b --python Tools/Blender/phr_tex_review.py -- <out.png>"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_preview import *

out = args()[0]
TEX = os.path.join(OUT_ASSET, "Textures", "Uniform")
P, S, T = (0.05, 0.16, 0.46), (0.96, 0.72, 0.10), (0.95, 0.95, 0.95)
tiles = []
tmp = os.path.join(os.path.dirname(out), "_phr_tex_tiles")
ensure(tmp)


def comp(name, cols):
    gray = load(f"{TEX}/{name}_Gray.png", True)[..., 0]
    mask = load(f"{TEX}/{name}_Team.png", False)[..., :3]
    if mask.shape[0] != gray.shape[0]:
        mask = np.zeros(gray.shape + (3,), np.float32)
    lin = compose_uniform(gray, mask, srgb_to_lin(cols[0]), srgb_to_lin(cols[1]), srgb_to_lin(cols[2]))
    return lin_to_srgb(lin).astype(np.float32)


items = [("jersey", (P, S, T)), ("pants", (T, P, S)), ("socks", (P, S, T)), ("cleats", (T, S, (0.04, 0.04, 0.04))),
         ("gloves", ((0.1, 0.1, 0.1), S, T)), ("armsleeve", (T, T, T)), ("pouch", ((0.05, 0.05, 0.05),) * 3), ("towel", (T, T, T)), ("wristband", (T, T, T))]
for n, cols in items:
    img = comp(n, cols)
    p = f"{tmp}/{n}_albedo.png"
    np_to_png(p, img, True)
    tiles.append(p)
for n in ("jersey", "cleats", "pouch", "towel"):
    tiles.append(f"{TEX}/{n}_N.png")
montage(tiles, out, 4, (512, 512))
print("wrote", out)
