"""List coloured (saturated) blobs of a texture on a coarse grid, to find baked-in logos.
blender -b --python scan_logos.py -- <png> [cell=64]"""
import bpy, sys
import numpy as np
argv = sys.argv[sys.argv.index("--") + 1:]
img = bpy.data.images.load(argv[0])
cell = int(argv[1]) if len(argv) > 1 else 64
w, h = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[:, ::-1 if False else 1]
px = px[::-1]  # top-left origin
rgb = px[..., :3]
mx = rgb.max(-1); mn = rgb.min(-1)
sat = (mx - mn) / np.maximum(mx, 1e-4)
mask = (sat > 0.35) & (mx > 0.25)
print("size", w, h)
gh, gw = h // cell, w // cell
cells = mask[:gh * cell, :gw * cell].reshape(gh, cell, gw, cell).mean(axis=(1, 3))
hits = np.argwhere(cells > 0.02)
seen = set(); blobs = []
for y, x in map(tuple, hits):
    if (y, x) in seen: continue
    stack = [(y, x)]; seen.add((y, x)); cs = []
    while stack:
        cy, cx = stack.pop(); cs.append((cy, cx))
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                n = (cy + dy, cx + dx)
                if n in seen or not (0 <= n[0] < gh and 0 <= n[1] < gw): continue
                if cells[n] > 0.02: seen.add(n); stack.append(n)
    ys = [c[0] for c in cs]; xs = [c[1] for c in cs]
    blobs.append((min(xs) * cell, min(ys) * cell, (max(xs) + 1) * cell, (max(ys) + 1) * cell, len(cs)))
for b in sorted(blobs, key=lambda b: -b[4]):
    print("blob x0=%d y0=%d x1=%d y1=%d cells=%d" % b)
