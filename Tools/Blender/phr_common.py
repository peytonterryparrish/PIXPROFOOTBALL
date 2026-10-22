"""Shared helpers for the photoreal-player (PHR) Blender scripts.  Run inside Blender 5.2 (blender -b --python ...).

Conventions: metres, Z-up, character faces -Y (character's left = +X), feet on Z=0 (same as pix_common.py / ArtPipeline.md).
"""
import bpy, sys, os, math
import numpy as np
from mathutils import Vector, Matrix

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SRC_DIR = os.path.join(ROOT, "_AssetSource", "UserModels", "a-pose-rigged-joe-burrow-cincinnati-bengals-nfl-3d-model")
R1_GLB = os.path.join(SRC_DIR, "A-P.glb")
R1_TEX = os.path.join(SRC_DIR, "textures", "textures")
DF = os.path.join(ROOT, "Assets", "_Licensed", "DraftFever", "Characters")
OUT_SRC = os.path.join(ROOT, "_AssetSource", "PhotorealPlayer")
OUT_ASSET = os.path.join(ROOT, "Assets", "_Licensed", "PhotorealPlayer")
QA = "D:/PIXFootball/Renders/photoreal" if os.path.isdir("D:/PIXFootball") else os.path.join(ROOT, "Docs", "QA", "photoreal")   # big renders live on D: (lead instruction)
WORK = "D:/PIXFootball/PhotorealWork" if os.path.isdir("D:/PIXFootball") else os.path.join(OUT_SRC, "work")


def args():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def ensure(p):
    os.makedirs(p, exist_ok=True)
    return p


# ----------------------------------------------------------------------------- rendering
def setup_stage(floor=True, sky=0.9):
    scene = bpy.context.scene
    if floor:
        bpy.ops.mesh.primitive_plane_add(size=60, location=(0, 0, 0))
        f = bpy.context.object
        f.name = "stage_floor"
        fm = bpy.data.materials.new("stage_floor")
        fm.use_nodes = True
        b = fm.node_tree.nodes["Principled BSDF"]
        b.inputs["Base Color"].default_value = (0.10, 0.16, 0.10, 1)
        b.inputs["Roughness"].default_value = 0.9
        f.data.materials.append(fm)
    world = bpy.data.worlds.new("sky")
    scene.world = world
    world.use_nodes = True
    nt = world.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputWorld")
    bg = nt.nodes.new("ShaderNodeBackground")
    tc = nt.nodes.new("ShaderNodeTexCoord")
    sep = nt.nodes.new("ShaderNodeSeparateXYZ")
    ramp = nt.nodes.new("ShaderNodeValToRGB")
    mapr = nt.nodes.new("ShaderNodeMapRange")
    ramp.color_ramp.elements[0].position = 0.35
    ramp.color_ramp.elements[0].color = (0.10, 0.12, 0.10, 1)
    ramp.color_ramp.elements[1].position = 0.75
    ramp.color_ramp.elements[1].color = (0.55, 0.72, 1.0, 1)
    mapr.inputs[1].default_value = -1
    mapr.inputs[2].default_value = 1
    nt.links.new(tc.outputs["Object"], sep.inputs[0])
    nt.links.new(sep.outputs["Z"], mapr.inputs[0])
    nt.links.new(mapr.outputs[0], ramp.inputs[0])
    nt.links.new(ramp.outputs[0], bg.inputs["Color"])
    bg.inputs["Strength"].default_value = sky
    nt.links.new(bg.outputs[0], out.inputs[0])
    return scene


def _light(scene, name, loc, energy, size, color, parent, target=(0, 0, 1.0)):
    ld = bpy.data.lights.new(name, 'AREA')
    ld.energy = energy
    ld.size = size
    ld.color = color
    lo = bpy.data.objects.new(name, ld)
    scene.collection.objects.link(lo)
    lo.location = loc
    lo.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat('-Z', 'Y').to_euler()
    lo.parent = parent
    return lo


def make_rig(scene, target=(0, 0, 1.0), scale=1.0):
    rig = bpy.data.objects.new("lightrig", None)
    scene.collection.objects.link(rig)
    s = scale
    _light(scene, "key", (-3.2 * s, -4.0 * s, 3.6 * s), 900 * s * s, 3.5 * s, (1.0, 0.96, 0.9), rig, target)
    _light(scene, "fill", (3.8 * s, -3.2 * s, 1.8 * s), 260 * s * s, 4.5 * s, (0.85, 0.92, 1.0), rig, target)
    _light(scene, "rim", (0.5 * s, 4.2 * s, 3.4 * s), 650 * s * s, 3.0 * s, (1, 1, 1), rig, target)
    return rig


def make_camera(scene, lens=85):
    cd = bpy.data.cameras.new("cam")
    cd.lens = lens
    cd.sensor_fit = 'VERTICAL'
    cd.sensor_height = 36
    cam = bpy.data.objects.new("cam", cd)
    scene.collection.objects.link(cam)
    scene.camera = cam
    return cam


def render_view(scene, cam, rig, path, target, azimuth_deg, dist, elev_deg=0.0, size=(900, 1100), lens=85, samples=48, engine='EEVEE'):
    """Camera orbits `target`; azimuth 0 = in front of the character (looking toward +Y), 90 = character's left, 180 = back."""
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.resolution_percentage = 100
    scene.render.engine = 'BLENDER_EEVEE' if engine == 'EEVEE' else 'CYCLES'
    if engine == 'EEVEE':
        try:
            scene.eevee.taa_render_samples = samples
        except Exception:
            pass
    else:
        scene.cycles.samples = samples
        scene.cycles.use_denoising = True
    cam.data.lens = lens
    a = math.radians(azimuth_deg)
    e = math.radians(elev_deg)
    t = Vector(target)
    cam.location = t + Vector((math.sin(a) * math.cos(e), -math.cos(a) * math.cos(e), math.sin(e))) * dist
    cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler()
    rig.rotation_euler = (0, 0, a)
    bpy.context.view_layer.update()
    scene.render.filepath = path
    scene.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)


# ----------------------------------------------------------------------------- images
def load_img(path, srgb=True):
    im = bpy.data.images.load(path, check_existing=False)
    if not srgb:
        im.colorspace_settings.name = 'Non-Color'
    return im


def img_to_np(im):
    w, h = im.size
    a = np.empty(w * h * im.channels, np.float32)
    im.pixels.foreach_get(a)
    a = a.reshape(h, w, im.channels)
    if im.channels == 3:
        a = np.concatenate([a, np.ones((h, w, 1), np.float32)], axis=2)
    elif im.channels == 1:
        a = np.concatenate([a, a, a, np.ones((h, w, 1), np.float32)], axis=2)
    return a[::-1]  # top-left origin


def np_to_png(path, arr, srgb=True):
    """arr: HxWx(3|4) float 0..1 with TOP-left origin."""
    h, w = arr.shape[:2]
    if arr.shape[2] == 3:
        arr = np.concatenate([arr, np.ones((h, w, 1), arr.dtype)], axis=2)
    im = bpy.data.images.new("_tmp_" + os.path.basename(path), w, h, alpha=True)
    im.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
    im.pixels.foreach_set(np.ascontiguousarray(arr[::-1].astype(np.float32)).ravel())
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    bpy.data.images.remove(im)


def montage(files, out, cols, tile=None):
    imgs = []
    for f in files:
        im = bpy.data.images.load(f)
        if tile:
            im.scale(*tile)
        imgs.append(img_to_np(im))
    th = max(a.shape[0] for a in imgs)
    tw = max(a.shape[1] for a in imgs)
    rows = (len(imgs) + cols - 1) // cols
    sheet = np.ones((rows * th, cols * tw, 4), np.float32)
    for i, a in enumerate(imgs):
        r, c = divmod(i, cols)
        h, w = a.shape[:2]
        sheet[r * th:r * th + h, c * tw:c * tw + w] = a
    np_to_png(out, sheet, srgb=True)


# ----------------------------------------------------------------------------- R1 import
R1_SHIFT = Vector((-0.0105, 0.0, 0.004))   # centre R1 on x = 0 (hips at x = 0.0105 in the source) and put the cleat soles on z = 0


def shift_all(objs, arm, delta=R1_SHIFT):
    for o in objs:
        o.data.transform(Matrix.Translation(delta))
    arm.data.transform(Matrix.Translation(delta))
    bpy.context.view_layer.update()


def import_r1():
    """Imports R1 (A-P.glb), bakes the armature scale (0.001) and object transforms into the data so everything has an
    identity transform, drops the stray Icosphere and the *_End empties, renames the one mesh with a non-ASCII name,
    centres the character (x = 0, soles on z = 0). Returns (armature, {mesh name: obj})."""
    bpy.ops.import_scene.gltf(filepath=R1_GLB)
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    for o in list(bpy.data.objects):
        if o.type == 'EMPTY' or o.name == 'Icosphere':
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.context.view_layer.update()
    meshes = {}
    for o in bpy.data.objects:
        if o.type == 'MESH':
            o.name = "".join(ch if ord(ch) < 128 else "i" for ch in o.name)
            if o.name == "innershit":
                o.name = "innershirt"
            meshes[o.name] = o
    mw = {n: o.matrix_world.copy() for n, o in meshes.items()}
    amw = arm.matrix_world.copy()
    for n, o in meshes.items():
        o.parent = None
        o.data.transform(mw[n])
        o.matrix_world = Matrix.Identity(4)
    arm.data.transform(amw)
    arm.matrix_world = Matrix.Identity(4)
    arm.scale = (1, 1, 1)
    for n, o in meshes.items():
        o.parent = arm
        o.matrix_parent_inverse = Matrix.Identity(4)
        for m in o.modifiers:
            if m.type == 'ARMATURE':
                m.object = arm
        if not [m for m in o.modifiers if m.type == 'ARMATURE']:
            md = o.modifiers.new("Armature", 'ARMATURE')
            md.object = arm
    arm.data.pose_position = 'POSE'
    for pb in arm.pose.bones:
        pb.location = (0, 0, 0)
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.rotation_euler = (0, 0, 0)
        pb.scale = (1, 1, 1)
    bpy.context.view_layer.update()
    shift_all(list(meshes.values()), arm)
    unwrap_jersey(meshes["jersey"], arm)
    return arm, meshes


JERSEY_LAYOUT = {}


def unwrap_jersey(obj, arm=None, pad=0.006, layout=None):
    """Replaces R1's jersey UV (stretched ~2.2x along v, which would squash the shader's jersey digits) by a deterministic, isotropic layout:
    planar front / back / flank / shoulder islands and cylindrical sleeve islands, packed on a shelf with one uniform scale (1 uv unit ~ constant metres).
    One uv layer named UVMap."""
    me = obj.data
    while len(me.uv_layers) > 0:
        me.uv_layers.remove(me.uv_layers[0])
    uvl = me.uv_layers.new(name="UVMap")
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3).astype(np.float64)
    classes = []
    bones = {}
    if arm is not None:
        for sd, nm in ((1, "rig:LeftArm"), (-1, "rig:RightArm")):
            b = arm.data.bones[nm]
            f = arm.data.bones[nm.replace("Arm", "ForeArm")]
            a = np.array(f.head_local) - np.array(b.head_local)
            bones[sd] = (np.array(b.head_local), a / np.linalg.norm(a))
    else:
        bones = {1: (np.array([0.2115, 0.0717, 1.5475]), np.array([0.7207, -0.0830, -0.6893])),
                 -1: (np.array([-0.2325, 0.0717, 1.5475]), np.array([-0.7207, -0.0830, -0.6893]))}
    islands = {}
    loop_uv = {}
    ref_bvh = None
    if layout and "ref_verts" in layout:
        from mathutils.bvhtree import BVHTree
        ref_bvh = BVHTree.FromPolygons(layout["ref_verts"], layout["ref_polys"])
    for poly in me.polygons:
        n = np.array(poly.normal[:])
        c = np.array(poly.center[:])
        if ref_bvh is not None:
            _, _, fi, _ = ref_bvh.find_nearest(Vector(poly.center))
            key = layout["ref_class"][fi]
        elif abs(c[0]) > 0.20:
            key = "sleeveL" if c[0] > 0 else "sleeveR"
        elif n[2] > 0.6:
            key = "top"
        elif n[1] < -0.5:
            key = "front"
        elif n[1] > 0.5:
            key = "back"
        else:
            key = "flankL" if c[0] > 0 else "flankR"
        classes.append(key)
        pts = []
        if key.startswith("sleeve"):
            J, a = bones[1 if key == "sleeveL" else -1]
            up = np.array([0, 0, 1.0])
            e1 = up - up.dot(a) * a
            e1 /= np.linalg.norm(e1)
            e2 = np.cross(a, e1)
            R = 0.075
            ph = []
            for li in poly.loop_indices:
                q = co[me.loops[li].vertex_index] - J
                v = q.dot(a)
                r = q - v * a
                ph.append((math.atan2(r.dot(e2), r.dot(e1)), v))
            ang = [p[0] for p in ph]
            if max(ang) - min(ang) > math.pi:
                ph = [(p[0] + 2 * math.pi if p[0] < 0 else p[0], p[1]) for p in ph]
            pts = [(p[0] * R, p[1]) for p in ph]
        else:
            for li in poly.loop_indices:
                p = co[me.loops[li].vertex_index]
                if key == "front":
                    pts.append((p[0], p[2]))
                elif key == "back":
                    pts.append((-p[0], p[2]))
                elif key == "top":
                    pts.append((p[0], p[1]))
                elif key == "flankL":
                    pts.append((p[1], p[2]))
                else:
                    pts.append((-p[1], p[2]))
        islands.setdefault(key, []).append((poly, pts))
    if layout:
        # LOD meshes: reuse the packing of LOD0 so the texture lines up exactly (the mapping is position based inside every island)
        scale = layout["scale"]
        for k, lst in islands.items():
            if k not in layout["place"]:
                continue
            ox, oy = layout["place"][k]
            b = layout["boxes"][k]
            for poly, pts in lst:
                for li, q in zip(poly.loop_indices, pts):
                    qx = min(max(q[0], b[0]), b[2])      # clamp into the island of LOD0 (a coarse face can reach slightly beyond it)
                    qy = min(max(q[1], b[1]), b[3])
                    uvl.data[li].uv = (ox + (qx - b[0]) * scale + pad * 0.5, oy + (qy - b[1]) * scale + pad * 0.5)
        me.update()
        return layout
    # island bounding boxes (metres)
    boxes = {}
    for k, lst in islands.items():
        xs = [q[0] for _, pts in lst for q in pts]
        ys = [q[1] for _, pts in lst for q in pts]
        boxes[k] = (min(xs), min(ys), max(xs), max(ys))
    order = sorted(boxes, key=lambda k: -(boxes[k][3] - boxes[k][1]))

    def pack(scale):
        x = y = 0.0
        rowh = 0.0
        out = {}
        for k in order:
            b = boxes[k]
            w = (b[2] - b[0]) * scale + pad
            h = (b[3] - b[1]) * scale + pad
            if x + w > 1.0:
                x = 0.0
                y += rowh
                rowh = 0.0
            out[k] = (x, y)
            x += w
            rowh = max(rowh, h)
        return out, y + rowh

    lo, hi = 0.2, 3.0
    for _ in range(30):
        mid = (lo + hi) / 2
        _, used = pack(mid)
        if used <= 1.0:
            lo = mid
        else:
            hi = mid
    scale = lo
    place, _ = pack(scale)
    for k, lst in islands.items():
        ox, oy = place[k]
        b = boxes[k]
        for poly, pts in lst:
            for li, q in zip(poly.loop_indices, pts):
                uvl.data[li].uv = (ox + (q[0] - b[0]) * scale + pad * 0.5, oy + (q[1] - b[1]) * scale + pad * 0.5)
    me.update()
    JERSEY_LAYOUT.clear()
    JERSEY_LAYOUT.update(dict(scale=scale, place=place, boxes=boxes, ref_verts=[v.co.copy() for v in me.vertices], ref_polys=[tuple(p.vertices) for p in me.polygons], ref_class=classes))
    return JERSEY_LAYOUT


def bbox(objs, world=True):
    mn = Vector((1e9,) * 3)
    mx = Vector((-1e9,) * 3)
    for o in objs:
        M = o.matrix_world if world else Matrix.Identity(4)
        co = np.empty(len(o.data.vertices) * 3, np.float32)
        o.data.vertices.foreach_get('co', co)
        co = co.reshape(-1, 3)
        P = co @ np.array(M.to_3x3()).T + np.array(M.translation)
        for i in range(3):
            mn[i] = min(mn[i], float(P[:, i].min()))
            mx[i] = max(mx[i], float(P[:, i].max()))
    return mn, mx


# ----------------------------------------------------------------------------- Draft Fever head import (shared topology, 28 shape keys)
def import_df_head(fbx=None):
    """Imports the Draft Fever Medium body FBX, keeps the blend-shape head + eyes (world space, metres, rest pose, modifiers dropped)
    and removes everything else. Returns (head, eyes)."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=fbx or (DF + "/Bodies/MediumPlayer.fbx"))
    new = [o for o in bpy.data.objects if o not in before]
    head = [o for o in new if o.name.startswith("Head_BlendShape")][0]
    eyes = [o for o in new if o.name.startswith("Eyes_BlendShape")][0]
    bpy.context.view_layer.update()
    mats = {o: o.matrix_world.copy() for o in (head, eyes)}
    for o in new:
        if o not in (head, eyes):
            bpy.data.objects.remove(o, do_unlink=True)
    for o in (head, eyes):
        for m in list(o.modifiers):
            o.modifiers.remove(m)
        o.parent = None
        # bake the object matrix (the importer leaves a 0.01 / 90 deg transform) into the vertex data and every shape key
        o.data.transform(mats[o], shape_keys=True)
        o.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    return head, eyes


# ----------------------------------------------------------------------------- measuring
def mesh_world(obj, evaluated=False):
    """(co[N,3] world, tri_verts[T,3]) of the object (optionally with modifiers/shape keys evaluated)."""
    if evaluated:
        dg = bpy.context.evaluated_depsgraph_get()
        eo = obj.evaluated_get(dg)
        me = eo.to_mesh()
    else:
        me = obj.data
    me.calc_loop_triangles()
    co = np.empty(len(me.vertices) * 3, np.float32)
    me.vertices.foreach_get('co', co)
    co = co.reshape(-1, 3).astype(np.float64)
    M = np.array(obj.matrix_world, np.float64)
    co = co @ M[:3, :3].T + M[:3, 3]
    lt = me.loop_triangles
    tv = np.empty(len(lt) * 3, np.int32)
    lt.foreach_get('vertices', tv)
    tv = tv.reshape(-1, 3)
    if evaluated:
        eo.to_mesh_clear()
    return co, tv


def _hull_perimeter(pts):
    p = sorted(set((round(float(x), 5), round(float(y), 5)) for x, y in pts))
    if len(p) < 3:
        return 0.0

    def cross(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])
    lo = []
    for q in p:
        while len(lo) >= 2 and cross(lo[-2], lo[-1], q) <= 0:
            lo.pop()
        lo.append(q)
    up = []
    for q in reversed(p):
        while len(up) >= 2 and cross(up[-2], up[-1], q) <= 0:
            up.pop()
        up.append(q)
    h = lo[:-1] + up[:-1]
    return sum(math.hypot(h[i][0] - h[(i + 1) % len(h)][0], h[i][1] - h[(i + 1) % len(h)][1]) for i in range(len(h)))


def z_section(co, tv, z):
    a, b, c = co[tv[:, 0]], co[tv[:, 1]], co[tv[:, 2]]
    pts = []
    for p, q in ((a, b), (b, c), (c, a)):
        dz = q[:, 2] - p[:, 2]
        m = ((p[:, 2] - z) * (q[:, 2] - z) < 0)
        if not m.any():
            continue
        t = (z - p[m, 2]) / dz[m]
        pts.append(p[m] + (q[m] - p[m]) * t[:, None])
    return np.concatenate(pts) if pts else np.zeros((0, 3))


def measure(objs, z, side=None):
    """width (x), depth (y), hull circumference at height z over the union of objs (side: 'L' x>0 / 'R' x<0 / None)."""
    allp = []
    for o in objs:
        co, tv = mesh_world(o)
        s = z_section(co, tv, z)
        if len(s):
            allp.append(s)
    if not allp:
        return None
    s = np.concatenate(allp)
    if side == 'L':
        s = s[s[:, 0] > 0.0]
    elif side == 'R':
        s = s[s[:, 0] < 0.0]
    if len(s) < 3:
        return None
    w = float(s[:, 0].max() - s[:, 0].min())
    d = float(s[:, 1].max() - s[:, 1].min())
    return w, d, _hull_perimeter(s[:, :2])
