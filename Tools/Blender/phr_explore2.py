import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_tex import *
reset()
arm, meshes = import_r1()
j = meshes["jersey"]
co, tv = mesh_world(j)
aL = np.array(arm.data.bones["rig:LeftArm"].head_local)
fL = np.array(arm.data.bones["rig:LeftForeArm"].head_local)
print("LeftArm head", aL, "forearm head", fL)
d = (fL - aL); d /= np.linalg.norm(d)
s = (co - aL) @ d
m = co[:, 0] > 0.18
print("jersey sleeve s range", s[m].min(), s[m].max(), "n", m.sum())
# neckline: open boundary
me = j.data
import bmesh
bm = bmesh.new(); bm.from_mesh(me)
bd = [v.co.copy() for v in bm.verts if any(e.is_boundary for e in v.link_edges)]
bd = np.array([[v.x, v.y, v.z] for v in bd])
print("boundary verts", len(bd))
top = bd[bd[:, 2] > 1.45]
print("neck boundary", len(top), "z range", top[:, 2].min() if len(top) else None, top[:, 2].max() if len(top) else None, "x", top[:, 0].min() if len(top) else None, top[:, 0].max() if len(top) else None)
hem = bd[bd[:, 2] < 1.25]
print("hem boundary", len(hem), "z range", hem[:, 2].min(), hem[:, 2].max())
sl = bd[(np.abs(bd[:, 0]) > 0.2)]
print("sleeve-end boundary", len(sl))
# pants
p = meshes["pants"]
co, tv = mesh_world(p)
print("pants z", co[:, 2].min(), co[:, 2].max(), "x", co[:, 0].min(), co[:, 0].max())
bm2 = bmesh.new(); bm2.from_mesh(p.data)
bd2 = np.array([[v.co.x, v.co.y, v.co.z] for v in bm2.verts if any(e.is_boundary for e in v.link_edges)])
print("pants boundary z range", bd2[:, 2].min(), bd2[:, 2].max(), len(bd2))
print("--- neck boundary samples")
for q in top[np.argsort(top[:,0])][::6]:
    print(np.round(q,3))
