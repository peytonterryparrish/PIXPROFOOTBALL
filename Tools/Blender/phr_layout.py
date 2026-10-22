"""PHR: where do the jersey number / name / wordmark go on the R1 jersey UV?  Ray-casts onto the jersey mesh at 3D anchor points and converts
the surface frame (reading direction, digit up, size in metres) into the PIX/Lit placement convention (cx, cy, height, rotation) used by PixLitHooks.
Writes Assets/_Licensed/PhotorealPlayer/jersey_layout.json.   blender -b --python Tools/Blender/phr_layout.py"""
import sys, os, json, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *
from mathutils.bvhtree import BVHTree
from mathutils.interpolate import poly_3d_calc

reset()
arm, meshes = import_r1()
jersey = meshes["jersey"]
me = jersey.data
me.calc_loop_triangles()
verts = [v.co.copy() for v in me.vertices]
tris = [tuple(t.vertices) for t in me.loop_triangles]
bvh = BVHTree.FromPolygons(verts, tris)
uvl = me.uv_layers.active.data
UP = Vector((0, 0, 1))


def place(origin, direction, height_m, label):
    loc, nor, fi, d = bvh.ray_cast(Vector(origin), Vector(direction).normalized())
    if loc is None:
        raise RuntimeError("no hit for " + label)
    lt = me.loop_triangles[fi]
    p = [verts[i] for i in lt.vertices]
    uv = [uvl[li].uv.copy() for li in lt.loops]
    w = poly_3d_calc(p, loc)
    cuv = w[0] * uv[0] + w[1] * uv[1] + w[2] * uv[2]
    n = (p[1] - p[0]).cross(p[2] - p[0]).normalized()
    if n.dot(Vector(direction)) > 0:
        n = -n                                    # outward = towards the ray origin
    # tangent frame in world
    r_w = (-n).cross(UP)
    if r_w.length < 1e-4:
        r_w = Vector((1, 0, 0))
    r_w.normalize()
    up_w = n.cross(r_w).normalized()
    # world -> uv Jacobian of this triangle
    E = np.array([(p[1] - p[0])[:], (p[2] - p[0])[:]]).T            # 3x2
    U = np.array([[uv[1].x - uv[0].x, uv[2].x - uv[0].x], [uv[1].y - uv[0].y, uv[2].y - uv[0].y]])   # 2x2
    J = E @ np.linalg.inv(U)                                       # d world / d uv (3x2)
    Jp = np.linalg.pinv(J)                                         # world tangent vector -> uv vector
    r_uv = Jp @ np.array(r_w[:])
    u_uv = Jp @ np.array(up_w[:])
    mirrored = (r_uv[0] * u_uv[1] - r_uv[1] * u_uv[0]) < 0
    if mirrored:
        theta = math.atan2(-r_uv[1], -r_uv[0])
    else:
        theta = math.atan2(r_uv[1], r_uv[0])
    h = height_m * float(np.linalg.norm(u_uv))
    if mirrored:
        h = -h
    per_m = float(np.linalg.norm(r_uv))
    print("PLACE %-10s hit (%.3f %.3f %.3f) n (%.2f %.2f %.2f) uv (%.4f %.4f) h %.4f rot %.3f mirrored %s  uv/m reading %.4f up %.4f" % (
        label, loc.x, loc.y, loc.z, n.x, n.y, n.z, cuv.x, cuv.y, h, theta, mirrored, per_m, float(np.linalg.norm(u_uv))))
    return dict(cx=cuv.x, cy=cuv.y, h=h, rot=theta, per_m=per_m, mirrored=bool(mirrored))


lay = {}
lay["front"] = place((0.0, -1.0, 1.27), (0, 1, 0), 0.165, "front")
lay["back"] = place((0.0, 1.0, 1.27), (0, -1, 0), 0.215, "back")
# shoulder numbers on the top / outer side of the sleeve caps (rays from outside / above towards the shoulder joint)
for side, sx in (("L", 1.0), ("R", -1.0)):
    j = np.array(arm.data.bones["rig:%sArm" % ("Left" if sx > 0 else "Right")].head_local)
    origin = (j[0] + sx * 0.30, j[1], j[2] + 0.30)
    d = (j[0] + sx * 0.02 - origin[0], 0.0, j[2] + 0.0 - origin[2])
    lay["shoulder" + side] = place(origin, d, 0.07, "shoulder" + side)
lay["name"] = place((0.0, 1.0, 1.455), (0, -1, 0), 0.042, "name")
lay["word"] = place((0.0, -1.0, 1.405), (0, 1, 0), 0.028, "word")
lay["nameMaxW_uv"] = 0.30 * lay["name"]["per_m"]
lay["wordMaxW_uv"] = 0.19 * lay["word"]["per_m"]
out = os.path.join(OUT_ASSET, "jersey_layout.json")
ensure(os.path.dirname(out))
json.dump(lay, open(out, "w"), indent=2)
print("wrote", out)
