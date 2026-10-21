"""Shared helpers for the PIX Football character tooling (run inside Blender: blender -b --python ...).

All character data ends up in metres, Z-up (Blender) with the character facing -Y, feet on Z=0, armature at the origin with
unit scale. FBX export uses Y-up / -Z forward so Unity sees +Z forward, 1 unit = 1 m.
"""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix, Quaternion
from mathutils.bvhtree import BVHTree


# ------------------------------------------------------------------ mesh baking
def baked_copy(obj, name=None, apply_modifiers=True, collection=None):
    """Return a new mesh object holding the evaluated (modifiers + shape keys applied) world-space geometry of obj.
    The copy has no parent, identity transform, and keeps vertex groups / materials / UVs."""
    dg = bpy.context.evaluated_depsgraph_get()
    ev = obj.evaluated_get(dg) if apply_modifiers else obj
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    me.transform(obj.matrix_world)
    if obj.matrix_world.determinant() < 0:
        me.flip_normals()
    new = bpy.data.objects.new(name or obj.name, me)
    for vg in obj.vertex_groups:
        new.vertex_groups.new(name=vg.name)
    # vertex groups survive new_from_object only through the evaluated deform layer; copy when counts match
    if len(me.vertices) == len(obj.data.vertices):
        for v in obj.data.vertices:
            for g in v.groups:
                new.vertex_groups[obj.vertex_groups[g.group].name].add([v.index], g.weight, 'REPLACE')
    (collection or bpy.context.scene.collection).objects.link(new)
    return new


def clear_modifiers(obj):
    for m in list(obj.modifiers):
        obj.modifiers.remove(m)


# ------------------------------------------------------------------ armature helpers
def bake_armature_transform(arm):
    """Apply the object transform of an armature to its bone data so the object has identity transform and metre units."""
    M = arm.matrix_world.copy()
    arm.data.transform(M)
    for b in arm.data.bones:
        pass
    arm.matrix_world = Matrix.Identity(4)


def head_of(arm, name):
    return arm.matrix_world @ arm.data.bones[name].head_local


# ------------------------------------------------------------------ weight transfer
def build_source(objs):
    """Triangulated world-space source geometry + per-vertex weights for nearest-surface transfer.
    Returns (bvh, tris(list of (i0,i1,i2)), verts(list Vector), weights(list of {group_name: w}))."""
    verts, tris, weights = [], [], []
    for o in objs:
        base = len(verts)
        names = [g.name for g in o.vertex_groups]
        me = o.data
        for v in me.vertices:
            verts.append(o.matrix_world @ v.co)
            weights.append({names[g.group]: g.weight for g in v.groups if g.weight > 1e-4})
        bm = bmesh.new(); bm.from_mesh(me); bmesh.ops.triangulate(bm, faces=bm.faces[:])
        for f in bm.faces:
            tris.append(tuple(base + v.index for v in f.verts))
        bm.free()
    bvh = BVHTree.FromPolygons(verts, tris, epsilon=0.0)
    return bvh, tris, verts, weights


def _bary(p, a, b, c):
    v0, v1, v2 = b - a, c - a, p - a
    d00, d01, d11 = v0.dot(v0), v0.dot(v1), v1.dot(v1)
    d20, d21 = v2.dot(v0), v2.dot(v1)
    den = d00 * d11 - d01 * d01
    if abs(den) < 1e-14:
        return 1.0, 0.0, 0.0
    v = (d11 * d20 - d01 * d21) / den
    w = (d00 * d21 - d01 * d20) / den
    u = 1.0 - v - w
    u, v, w = max(u, 0.0), max(v, 0.0), max(w, 0.0)
    s = u + v + w
    return u / s, v / s, w / s


def transfer_weights(target, source, restrict=None):
    """Nearest-surface weight transfer from source = build_source(...) onto target (world-space mesh object).
    restrict: optional set of group names allowed on the target."""
    bvh, tris, verts, weights = source
    me = target.data
    gnames = {}
    for v in me.vertices:
        loc, nor, fi, dist = bvh.find_nearest(target.matrix_world @ v.co)
        if fi is None:
            continue
        t = tris[fi]
        u, vv, w = _bary(loc, verts[t[0]], verts[t[1]], verts[t[2]])
        acc = {}
        for wt, vi in ((u, t[0]), (vv, t[1]), (w, t[2])):
            for n, x in weights[vi].items():
                acc[n] = acc.get(n, 0.0) + wt * x
        if restrict is not None:
            acc = {n: x for n, x in acc.items() if n in restrict}
        gnames[v.index] = acc
    return gnames


def apply_weights(target, vweights, max_influences=4, smooth_iters=0, smooth_factor=0.5):
    """Write {vertex index: {group: w}} onto target with optional Laplacian smoothing, influence limit and normalisation."""
    me = target.data
    n = len(me.vertices)
    cur = [dict(vweights.get(i, {})) for i in range(n)]
    adj = [[] for _ in range(n)]
    for e in me.edges:
        a, b = e.vertices
        adj[a].append(b); adj[b].append(a)
    for _ in range(smooth_iters):
        nxt = []
        for i in range(n):
            if not adj[i]:
                nxt.append(cur[i]); continue
            acc = {}
            for k, x in cur[i].items():
                acc[k] = acc.get(k, 0.0) + x * (1 - smooth_factor)
            for j in adj[i]:
                for k, x in cur[j].items():
                    acc[k] = acc.get(k, 0.0) + x * smooth_factor / len(adj[i])
            nxt.append(acc)
        cur = nxt
    for g in list(target.vertex_groups):
        target.vertex_groups.remove(g)
    groups = {}
    for i in range(n):
        d = cur[i]
        items = sorted(d.items(), key=lambda kv: -kv[1])[:max_influences]
        tot = sum(x for _, x in items)
        if tot <= 1e-6:
            continue
        for name, x in items:
            if name not in groups:
                groups[name] = target.vertex_groups.new(name=name)
            groups[name].add([i], x / tot, 'REPLACE')


def bind_rigid(target, bone_name):
    for g in list(target.vertex_groups):
        target.vertex_groups.remove(g)
    vg = target.vertex_groups.new(name=bone_name)
    vg.add(list(range(len(target.data.vertices))), 1.0, 'REPLACE')


def add_armature_modifier(obj, arm):
    for m in list(obj.modifiers):
        if m.type == 'ARMATURE':
            obj.modifiers.remove(m)
    m = obj.modifiers.new("Armature", 'ARMATURE')
    m.object = arm
    obj.parent = arm
    obj.matrix_parent_inverse = Matrix.Identity(4)


# ------------------------------------------------------------------ render preview (workbench, flat colours)
def _mat(color):
    m = bpy.data.materials.new("prev")
    m.diffuse_color = color
    return m


def render_preview(objs, path, views=("front",), size=(900, 1100), center=None, dist=5.0, ortho_scale=2.2, colors=None):
    """Render flat-shaded previews. views: front/side/back/three_quarter. colors: {obj.name: (r,g,b,a)}"""
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.image_settings.media_type = "IMAGE"
    sc.render.image_settings.file_format = "PNG"
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'OBJECT'
    sc.render.resolution_x, sc.render.resolution_y = size
    sc.render.film_transparent = False
    sc.world = sc.world or bpy.data.worlds.new("w")
    sc.world.color = (0.18, 0.2, 0.24)
    for o in objs:
        o.color = (colors or {}).get(o.name, (0.75, 0.75, 0.78, 1))
    if center is None:
        center = Vector((0, 0, 0.9))
    cam = bpy.data.objects.get("PreviewCam")
    if cam is None:
        cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
        sc.collection.objects.link(cam)
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = ortho_scale
    sc.camera = cam
    dirs = {"front": Vector((0, -1, 0)), "back": Vector((0, 1, 0)), "side": Vector((1, 0, 0)),
            "three_quarter": Vector((0.7, -0.7, 0.15)).normalized()}
    outs = []
    for v in views:
        d = dirs[v]
        cam.location = center + d * dist
        cam.rotation_euler = (center - cam.location).to_track_quat('-Z', 'Y').to_euler()
        p = path.replace(".png", "_%s.png" % v)
        sc.render.filepath = p
        bpy.ops.render.render(write_still=True)
        outs.append(p)
    return outs


# ------------------------------------------------------------------ FBX export
def export_fbx(path, objs, armature_only=False, bake_anim=False, add_leaf_bones=False, all_actions=False, bake_step=1.0):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    kw = dict(filepath=path, use_selection=True, global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_NONE',
              axis_forward='-Z', axis_up='Y', object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=True,
              mesh_smooth_type='FACE', add_leaf_bones=add_leaf_bones, bake_anim=bake_anim,
              bake_anim_use_all_actions=all_actions, bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
              bake_anim_step=bake_step, bake_anim_simplify_factor=0.0, path_mode='STRIP', embed_textures=False)
    bpy.ops.export_scene.fbx(**kw)


# ------------------------------------------------------------------ clean rig from the coach kit blend + preview montage
def load_clean_coach_rig(blend_path):
    """Open the coach kit blend, return (armature in metres at the origin, [baked bare player body meshes with groups])."""
    bpy.ops.wm.open_mainfile(filepath=blend_path)
    src_arm = bpy.data.objects["root"]
    src_arm.data.pose_position = 'REST'
    src_arm.location.x = 0.0
    bpy.context.view_layer.update()
    bodies = [baked_copy(bpy.data.objects[n], "BODY_" + n) for n in ("Torso_GEO", "Arms_GEO", "Legs_GEO", "Shoes_GEO", "Head_GEO")]
    data = src_arm.data.copy()
    arm = bpy.data.objects.new("Armature", data)
    bpy.context.scene.collection.objects.link(arm)
    arm.matrix_world = src_arm.matrix_world.copy()
    bpy.context.view_layer.update()
    bake_armature_transform(arm)
    arm.data.pose_position = 'POSE'
    for b in bodies:
        add_armature_modifier(b, arm)
    for o in bpy.data.objects:
        if o is not arm and o not in bodies:
            o.hide_render = True
    return arm, bodies


def montage(files, out_path, cols):
    import numpy as np
    imgs = []
    for f in files:
        im = bpy.data.images.load(f)
        w, h = im.size
        imgs.append(np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4))
        bpy.data.images.remove(im)
    h, w = imgs[0].shape[:2]
    rows = (len(imgs) + cols - 1) // cols
    sheet = np.zeros((rows * h, cols * w, 4), np.float32)
    sheet[..., 3] = 1
    for i, im in enumerate(imgs):
        r, c = divmod(i, cols)
        sheet[(rows - 1 - r) * h:(rows - r) * h, c * w:(c + 1) * w] = im
    out = bpy.data.images.new("sheet", cols * w, rows * h, alpha=True)
    out.pixels = sheet.reshape(-1).tolist()
    out.filepath_raw = out_path
    out.file_format = 'PNG'
    out.save()


def add_dummy_mesh(arm, bone="root.x"):
    """A 1 mm triangle skinned to `bone`. Armature-only FBX exports come out in Unity with a 100x / -90 degree root node (the clip would be
    sampled in a rotated frame); with a skinned mesh in the file the exporter behaves exactly like the character FBXs (identity root)."""
    me = bpy.data.meshes.new("AnimDummy")
    me.from_pydata([(0, 0, 0.9), (0.001, 0, 0.9), (0, 0.001, 0.9)], [], [(0, 1, 2)])
    ob = bpy.data.objects.new("AnimDummy", me)
    bpy.context.scene.collection.objects.link(ob)
    bind_rigid(ob, bone)
    add_armature_modifier(ob, arm)
    return ob
