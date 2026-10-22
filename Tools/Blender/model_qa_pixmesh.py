"""MODELS QA: read a .pixmesh file back and print, per mesh / LOD, the bones that carry the weight (proves the weights survived the export).

  python-free: blender -b --python Tools/Blender/model_qa_pixmesh.py -- <file.pixmesh> [more files]
"""
import sys, struct
import numpy as np

def read7(f):
    n = 0; shift = 0
    while True:
        b = f.read(1)[0]; n |= (b & 0x7F) << shift; shift += 7
        if not b & 0x80: break
    return f.read(n).decode("utf-8")

def main():
    files = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    for path in files:
        with open(path, "rb") as f:
            assert f.read(4) == b"PIXM"
            ver, count = struct.unpack("<ii", f.read(8)); nb = struct.unpack("<i", f.read(4))[0]
            bones = [read7(f) for _ in range(nb)]
            print(path, "version", ver, "meshes", count, "bones", nb)
            for _ in range(count):
                name = read7(f); lod, nv, nt, ns = struct.unpack("<iiii", f.read(16))
                f.seek(nv * 12 * 2 + nv * 8, 1)
                bi = np.frombuffer(f.read(nv * 16), "<i4").reshape(nv, 4); bw = np.frombuffer(f.read(nv * 16), "<f4").reshape(nv, 4)
                f.seek(nt * 12 + nt * 4, 1)
                tot = {}
                for k in range(4):
                    m = (bi[:, k] >= 0) & (bw[:, k] > 0)
                    for b, w in zip(bi[m, k], bw[m, k]): tot[bones[b]] = tot.get(bones[b], 0.0) + float(w)
                top = sorted(tot.items(), key=lambda kv: -kv[1])[:6]
                if lod in (0, 1) and name[:4] in ("Legs", "Torso", "Arms"):
                    print("  %-12s LOD%d verts %5d wsum/vert %.3f  top: %s" % (name[:12], lod, nv, float(bw.sum() / nv), ", ".join("%s %.0f" % kv for kv in top)))

main()
