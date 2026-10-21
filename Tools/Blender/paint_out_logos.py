"""Paint rectangles out of a texture with the mean colour of a ring around each box (removes baked-in logos).
blender -b --python paint_out_logos.py -- <in.png> <out.png> x0,y0,x1,y1 [x0,y0,x1,y1 ...]   (top-left origin, pixels)"""
import bpy, sys
import numpy as np
argv = sys.argv[sys.argv.index("--") + 1:]
src, dst = argv[0], argv[1]
boxes = [tuple(int(v) for v in a.split(",")) for a in argv[2:]]
img = bpy.data.images.load(src)
w, h = img.size
px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)[::-1].copy()  # top-left origin
pad, ring = 16, 12
for x0, y0, x1, y1 in boxes:
    x0 -= pad; y0 -= pad; x1 += pad; y1 += pad
    x0 = max(x0, 0); y0 = max(y0, 0); x1 = min(x1, w); y1 = min(y1, h)
    ox0, oy0, ox1, oy1 = max(x0 - ring, 0), max(y0 - ring, 0), min(x1 + ring, w), min(y1 + ring, h)
    outer = px[oy0:oy1, ox0:ox1].copy()
    m = np.ones(outer.shape[:2], bool)
    m[y0 - oy0:y1 - oy0, x0 - ox0:x1 - ox0] = False
    # ignore bright/saturated ring pixels (other logo parts): use the median of the ring
    col = np.median(outer[m], axis=0)
    px[y0:y1, x0:x1] = col
    print("painted", (x0, y0, x1, y1), "colour", col)
out = bpy.data.images.new("out", w, h, alpha=True)
out.pixels = px[::-1].reshape(-1).tolist()
out.filepath_raw = dst
out.file_format = 'PNG'
out.save()
print("saved", dst)
