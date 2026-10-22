import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_tex import *

a = args()
out = a[0]
parts = a[1].split(",")
size = 768
reset()
arm, meshes = import_r1()
tiles = []
for n in parts:
    o = meshes[n]
    me = o.data
    print("PART", n, "uv layers", [u.name for u in me.uv_layers], "active", me.uv_layers.active.name)
    pos, nrm, cov = bake_world(o, size)
    img = np.zeros((size, size, 3), np.float32)
    img[..., 0] = (pos[..., 0] + 0.35) / 0.7
    img[..., 1] = (pos[..., 2] - 0.0) / 1.7
    img[..., 2] = np.clip(nrm[..., 1] * 0.5 + 0.5, 0, 1)
    img[~cov] = 0.1
    save(f"{out}/uvx_{n}.png", img, True)
print("done")
