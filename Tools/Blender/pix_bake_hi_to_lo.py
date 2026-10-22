"""Generic high-poly -> low-poly bake (Cycles, selected-to-active): tangent-space normal, ambient occlusion and curvature.

  blender -b --python Tools/Blender/pix_bake_hi_to_lo.py -- --hi hi.fbx[,hi2.obj] --lo low.fbx --out Logs/blender_v6/bake [--name Asset]
        [--size 2048] [--samples 32] [--margin 16] [--cage-extrusion 0.02] [--max-ray 0.1] [--ao-distance 0.3] [--curv-gain 4]
        [--maps normal,ao,curvature] [--normal-y opengl|directx]
  blender -b --python Tools/Blender/pix_bake_hi_to_lo.py -- --selftest --out Logs/blender_v6/bake_selftest

 * --hi / --lo: FBX, OBJ, PLY, STL or GLB/GLTF (metres). The low mesh MUST have a (non overlapping) UV layout; every hi mesh is
   baked onto it. Units: --cage-extrusion and --max-ray are scene units (metres); the cage is the low mesh pushed out along its normals
   by that distance, rays travel up to --max-ray from it. Pick extrusion ~ the largest height difference between hi and lo.
 * Outputs <name>_N.png (OpenGL / Y+ tangent space, which is what Unity expects; --normal-y directx flips green), <name>_AO.png,
   <name>_CURV.png (0.5 = flat, >0.5 convex edges, <0.5 cavities; from Cycles 'Pointiness' of the hi mesh, mapped with --curv-gain).
   All 8-bit PNG, Non-Color. They are baked with Cycles on the CPU (device forced), --samples per pixel.
 * --selftest builds a noise-displaced, subdivided icosphere + a 320 face low icosphere (smart UV project), bakes it and prints image stats.
"""
import sys, os, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix_player_common import *
import bpy, bmesh
from mathutils import Vector, noise


def parse():
    a = script_args()
    o = dict(hi=[], lo=None, out=os.path.join(PROJECT_ROOT, "Logs", "blender_v6", "bake"), name=None, size=1024, samples=16, margin=16,
             cage=0.02, ray=0.1, ao_dist=0.3, gain=4.0, maps=["normal", "ao", "curvature"], ny="opengl", selftest=False)
    i = 0
    while i < len(a):
        k = a[i]
        if k == "--hi": o["hi"] = a[i + 1].split(","); i += 2
        elif k == "--lo": o["lo"] = a[i + 1]; i += 2
        elif k == "--out": o["out"] = a[i + 1]; i += 2
        elif k == "--name": o["name"] = a[i + 1]; i += 2
        elif k == "--size": o["size"] = int(a[i + 1]); i += 2
        elif k == "--samples": o["samples"] = int(a[i + 1]); i += 2
        elif k == "--margin": o["margin"] = int(a[i + 1]); i += 2
        elif k == "--cage-extrusion": o["cage"] = float(a[i + 1]); i += 2
        elif k == "--max-ray": o["ray"] = float(a[i + 1]); i += 2
        elif k == "--ao-distance": o["ao_dist"] = float(a[i + 1]); i += 2
        elif k == "--curv-gain": o["gain"] = float(a[i + 1]); i += 2
        elif k == "--maps": o["maps"] = a[i + 1].split(","); i += 2
        elif k == "--normal-y": o["ny"] = a[i + 1].lower(); i += 2
        elif k == "--selftest": o["selftest"] = True; i += 1
        else: raise SystemExit("unknown arg " + k)
    return o


def import_any(path):
    before = set(bpy.data.objects)
    ext = os.path.splitext(path)[1].lower()
    if ext == ".fbx": bpy.ops.import_scene.fbx(filepath=path)
    elif ext == ".obj": bpy.ops.wm.obj_import(filepath=path)
    elif ext == ".ply": bpy.ops.wm.ply_import(filepath=path)
    elif ext == ".stl": bpy.ops.wm.stl_import(filepath=path)
    elif ext in (".glb", ".gltf"): bpy.ops.import_scene.gltf(filepath=path)
    else: raise SystemExit("unsupported mesh format " + ext)
    return [o for o in bpy.data.objects if o not in before and o.type == 'MESH']


def selftest_scene():
    """Hi: icosphere (subdiv 6, ~82k tris) displaced with noise (bumps + a ridge). Lo: icosphere subdiv 2 (320 tris) with smart UVs."""
    reset_scene()
    hi_me = bpy.data.meshes.new("hi")
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=6, radius=0.5)
    for v in bm.verts:
        p = v.co.normalized()
        d = 0.045 * noise.noise(p * 3.0) + 0.012 * noise.noise(p * 14.0)
        d += 0.03 * max(0.0, 1.0 - abs(p.z) * 18.0)                   # an equatorial ridge
        v.co = p * (0.5 + d)
    bm.to_mesh(hi_me); bm.free()
    for p in hi_me.polygons: p.use_smooth = True
    hi = bpy.data.objects.new("HI_sphere", hi_me); bpy.context.scene.collection.objects.link(hi)
    lo_me = bpy.data.meshes.new("lo")
    bm = bmesh.new(); bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.5); bm.to_mesh(lo_me); bm.free()
    for p in lo_me.polygons: p.use_smooth = True
    lo = bpy.data.objects.new("LO_sphere", lo_me); bpy.context.scene.collection.objects.link(lo)
    bpy.context.view_layer.objects.active = lo
    for ob in bpy.data.objects: ob.select_set(ob == lo)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.02)
    bpy.ops.object.mode_set(mode='OBJECT')
    return [hi], lo


def make_curvature_material(hi_objs, gain):
    for ob in hi_objs:
        mat = bpy.data.materials.new("PIX_curv_" + ob.name); mat.use_nodes = True
        nt = mat.node_tree; nt.nodes.clear()
        geo = nt.nodes.new("ShaderNodeNewGeometry")
        sub = nt.nodes.new("ShaderNodeMath"); sub.operation = 'SUBTRACT'; sub.inputs[1].default_value = 0.5
        mul = nt.nodes.new("ShaderNodeMath"); mul.operation = 'MULTIPLY'; mul.inputs[1].default_value = gain
        add = nt.nodes.new("ShaderNodeMath"); add.operation = 'ADD'; add.inputs[1].default_value = 0.5; add.use_clamp = True
        em = nt.nodes.new("ShaderNodeEmission")
        out = nt.nodes.new("ShaderNodeOutputMaterial")
        nt.links.new(geo.outputs["Pointiness"], sub.inputs[0])
        nt.links.new(sub.outputs[0], mul.inputs[0]); nt.links.new(mul.outputs[0], add.inputs[0])
        nt.links.new(add.outputs[0], em.inputs["Color"]); nt.links.new(em.outputs[0], out.inputs["Surface"])
        ob.data.materials.clear(); ob.data.materials.append(mat)


def bake_one(lo, hi_objs, bake_type, size, out_path, o, flip_green=False):
    sc = bpy.context.scene
    img = bpy.data.images.new("bake_" + bake_type, size, size, alpha=False, float_buffer=False)
    img.colorspace_settings.name = 'Non-Color'
    # low mesh material with the target image node active
    lo.data.materials.clear()
    mat = bpy.data.materials.new("PIX_bake_target"); mat.use_nodes = True
    lo.data.materials.append(mat)
    tn = mat.node_tree.nodes.new("ShaderNodeTexImage"); tn.image = img
    mat.node_tree.nodes.active = tn
    for ob in bpy.data.objects: ob.select_set(False)
    for ob in hi_objs: ob.select_set(True)
    lo.select_set(True)
    bpy.context.view_layer.objects.active = lo
    b = sc.render.bake
    b.use_selected_to_active = True
    b.cage_extrusion = o["cage"]
    b.max_ray_distance = o["ray"]
    b.margin = o["margin"]
    b.use_clear = True
    b.target = 'IMAGE_TEXTURES'
    if bake_type == 'NORMAL':
        b.normal_space = 'TANGENT'
        b.normal_r, b.normal_g, b.normal_b = 'POS_X', 'POS_Y', 'POS_Z'
    bpy.ops.object.bake(type=bake_type)
    px = np.empty(size * size * 4, np.float32); img.pixels.foreach_get(px)
    a = np.flipud(px.reshape(size, size, 4))
    if flip_green:
        a = a.copy(); a[..., 1] = 1.0 - a[..., 1]
    save_png(out_path, a[..., :3].copy(), 'Non-Color')
    bpy.data.images.remove(img)
    return a[..., :3]


def main():
    o = parse()
    if o["selftest"]:
        hi_objs, lo = selftest_scene()
        o["name"] = o["name"] or "selftest"
        o["cage"], o["ray"], o["ao_dist"] = 0.08, 0.2, 0.3
    else:
        if not o["hi"] or not o["lo"]:
            raise SystemExit("--hi and --lo (or --selftest) required")
        reset_scene()
        hi_objs = []
        for p in o["hi"]:
            hi_objs += import_any(p)
        los = import_any(o["lo"])
        if not los: raise SystemExit("no mesh in --lo")
        lo = los[0]
        o["name"] = o["name"] or os.path.splitext(os.path.basename(o["lo"]))[0]
    sc = bpy.context.scene
    sc.render.engine = 'CYCLES'
    sc.cycles.device = 'CPU'
    sc.cycles.samples = o["samples"]
    sc.cycles.use_denoising = False
    try:
        sc.cycles.use_adaptive_sampling = False
    except Exception:
        pass
    if sc.world is None:
        sc.world = bpy.data.worlds.new("W")
    try:
        sc.world.light_settings.distance = o["ao_dist"]
    except Exception as e:
        print("could not set AO distance:", e)
    os.makedirs(o["out"], exist_ok=True)
    print("bake: hi=%s lo=%s size=%d samples=%d cage=%.4f ray=%.4f" % ([h.name for h in hi_objs], lo.name, o["size"], o["samples"], o["cage"], o["ray"]))
    hi_mats = {h: list(h.data.materials) for h in hi_objs}
    results = {}
    if "normal" in o["maps"]:
        results["N"] = bake_one(lo, hi_objs, 'NORMAL', o["size"], os.path.join(o["out"], o["name"] + "_N.png"), o, flip_green=(o["ny"] == "directx"))
    if "ao" in o["maps"]:
        results["AO"] = bake_one(lo, hi_objs, 'AO', o["size"], os.path.join(o["out"], o["name"] + "_AO.png"), o)
    if "curvature" in o["maps"]:
        make_curvature_material(hi_objs, o["gain"])
        results["CURV"] = bake_one(lo, hi_objs, 'EMIT', o["size"], os.path.join(o["out"], o["name"] + "_CURV.png"), o)
    for k, a in results.items():
        cov = a.reshape(-1, 3)
        nz = (cov.max(1) > 0.001)
        print("%-5s written; non-empty pixels %.1f%%  mean RGB %s  std %s" % (k, 100 * nz.mean(), np.round(cov[nz].mean(0), 3), np.round(cov[nz].std(0), 3)))
    print("done ->", o["out"])


main()
