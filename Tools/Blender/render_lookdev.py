"""Consistent look-dev renders for player model comparison.

blender -b --python Tools/Blender/render_lookdev.py -- <model.glb|fbx> <out_prefix> [height_m=1.88] [engine=EEVEE]

Writes <out_prefix>_{front,threequarter,back}.png (1024x1536) and <out_prefix>_stats.json.
Identical rig for every model: 3-point studio lights + sky-colour world light, 85 mm lens, model scaled so
its bounding-box height is height_m and standing on z=0. Models are assumed to face -Y (glTF +Z).
"""
import bpy, sys, os, json, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
model, prefix = argv[0], argv[1]
HEIGHT = float(argv[2]) if len(argv) > 2 else 1.88
ENGINE = argv[3] if len(argv) > 3 else "EEVEE"
ROT = float(argv[4]) if len(argv) > 4 else -90.0  # degrees about Z; Tripo GLBs face +X in Blender after import
W, H = 1024, 1536

bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene

ext = os.path.splitext(model)[1].lower()
if ext in (".glb", ".gltf"):
    bpy.ops.import_scene.gltf(filepath=model)
elif ext == ".fbx":
    bpy.ops.import_scene.fbx(filepath=model)
else:
    raise SystemExit("unsupported " + ext)

for _o in [o for o in bpy.context.scene.objects if o.type == "MESH" and len(o.data.materials) == 0]:
    bpy.data.objects.remove(_o, do_unlink=True)  # stray helper meshes (e.g. R1 icosphere)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
arms = [o for o in bpy.context.scene.objects if o.type == 'ARMATURE']
dg = bpy.context.evaluated_depsgraph_get()


def world_bbox():
    mn = Vector((1e9, 1e9, 1e9)); mx = Vector((-1e9, -1e9, -1e9))
    for o in meshes:
        eo = o.evaluated_get(dg)
        for c in eo.bound_box:
            v = eo.matrix_world @ Vector(c)
            for i in range(3):
                mn[i] = min(mn[i], v[i]); mx[i] = max(mx[i], v[i])
    return mn, mx


mn, mx = world_bbox()
raw_h = mx.z - mn.z
# stats gathered before scaling
tris = 0; verts = 0
for o in meshes:
    eo = o.evaluated_get(dg)
    me = eo.to_mesh()
    me.calc_loop_triangles()
    tris += len(me.loop_triangles); verts += len(me.vertices)
    eo.to_mesh_clear()
tex = {}
for img in bpy.data.images:
    if img.size[0] > 0:
        tex[img.name] = [img.size[0], img.size[1]]
stats = {
    "model": os.path.basename(model), "meshes": len(meshes), "tris": tris, "verts": verts,
    "materials": len(bpy.data.materials), "textures": tex,
    "armatures": [{"name": a.name, "bones": len(a.data.bones),
                   "bone_names": [b.name for b in a.data.bones]} for a in arms],
    "raw_bbox_size": [round(mx.x - mn.x, 3), round(mx.y - mn.y, 3), round(mx.z - mn.z, 3)],
}

# Normalise: parent everything under an empty, scale to HEIGHT, feet on z=0, centred
root = bpy.data.objects.new("root", None)
scene.collection.objects.link(root)
for o in list(scene.objects):
    if o is not root and o.parent is None:
        o.parent = root
s = HEIGHT / raw_h if HEIGHT > 0 else 1.0  # HEIGHT<=0: keep native metre scale (user-supplied posed refs)
root.scale = (s, s, s)
root.rotation_euler = (0, 0, math.radians(ROT))
bpy.context.view_layer.update()
dg = bpy.context.evaluated_depsgraph_get()
mn, mx = world_bbox()
root.location = Vector((-(mn.x + mx.x) / 2, -(mn.y + mx.y) / 2, -mn.z))
bpy.context.view_layer.update()
stats["scale_applied"] = round(s, 4)

# Studio floor (neutral grey, matte) -- same for every model
bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, 0))
floor = bpy.context.object
fm = bpy.data.materials.new("floor"); fm.use_nodes = True
fm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.10, 0.15, 0.10, 1)
fm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.9
floor.data.materials.append(fm)

# World: soft sky gradient (outdoor stadium-ish sky light)
world = bpy.data.worlds.new("sky"); scene.world = world; world.use_nodes = True
nt = world.node_tree; nt.nodes.clear()
out = nt.nodes.new("ShaderNodeOutputWorld")
bg = nt.nodes.new("ShaderNodeBackground")
tc = nt.nodes.new("ShaderNodeTexCoord")
sep = nt.nodes.new("ShaderNodeSeparateXYZ")
ramp = nt.nodes.new("ShaderNodeValToRGB")
ramp.color_ramp.elements[0].position = 0.35; ramp.color_ramp.elements[0].color = (0.10, 0.12, 0.10, 1)  # ground bounce
ramp.color_ramp.elements[1].position = 0.75; ramp.color_ramp.elements[1].color = (0.55, 0.72, 1.0, 1)   # sky
mapr = nt.nodes.new("ShaderNodeMapRange")
mapr.inputs[1].default_value = -1; mapr.inputs[2].default_value = 1
nt.links.new(tc.outputs["Object"], sep.inputs[0])
nt.links.new(sep.outputs["Z"], mapr.inputs[0])
nt.links.new(mapr.outputs[0], ramp.inputs[0])
nt.links.new(ramp.outputs[0], bg.inputs["Color"])
bg.inputs["Strength"].default_value = 0.9
nt.links.new(bg.outputs[0], out.inputs[0])


def add_light(kind, loc, energy, size, color=(1, 1, 1), target=Vector((0, 0, 1.0))):
    ld = bpy.data.lights.new(kind + "_l", 'AREA')
    ld.energy = energy; ld.size = size; ld.color = color
    lo = bpy.data.objects.new(kind, ld)
    scene.collection.objects.link(lo)
    lo.location = loc
    d = target - Vector(loc)
    lo.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    return lo


# 3-point studio rig, defined for the FRONT view and rotated with the camera so every view is lit the same way
rig = bpy.data.objects.new("rig", None); scene.collection.objects.link(rig)
for nm, loc, e, sz, col in [
    ("key",  (-3.2, -4.0, 3.6), 900, 3.5, (1.0, 0.96, 0.9)),
    ("fill", (3.8, -3.2, 1.8), 260, 4.5, (0.85, 0.92, 1.0)),
    ("rim",  (0.5, 4.2, 3.4), 650, 3.0, (1.0, 1.0, 1.0)),
]:
    l = add_light(nm, loc, e, sz, col); l.parent = rig

# Camera: 85 mm, same framing for every model
cam_d = bpy.data.cameras.new("cam"); cam_d.lens = 85; cam_d.sensor_width = 36; cam_d.sensor_fit = 'VERTICAL'
cam_d.sensor_height = 36
cam = bpy.data.objects.new("cam", cam_d); scene.collection.objects.link(cam)
scene.camera = cam
CAM_DIST, CAM_Z, TGT_Z = 6.8, 1.1, 0.95

scene.render.resolution_x = W; scene.render.resolution_y = H; scene.render.resolution_percentage = 100
scene.render.film_transparent = False
if ENGINE == "CYCLES":
    scene.render.engine = 'CYCLES'; scene.cycles.samples = 64; scene.cycles.use_denoising = True
else:
    scene.render.engine = 'BLENDER_EEVEE'
    try:
        scene.eevee.taa_render_samples = 64
    except Exception:
        pass
scene.view_settings.view_transform = 'AgX' if 'AgX' in bpy.types.ColorManagedViewSettings.bl_rna.properties['view_transform'].enum_items.keys() else 'Standard'
try:
    scene.view_settings.look = 'None'
except Exception:
    pass
scene.render.image_settings.file_format = 'PNG'
scene.render.image_settings.color_mode = 'RGB'

views = {"front": 0.0, "threequarter": 40.0, "back": 180.0}
for name, az in views.items():
    a = math.radians(az)
    # azimuth 0 = camera at -Y looking +Y (front of model)
    cam.location = (math.sin(a) * CAM_DIST, -math.cos(a) * CAM_DIST, CAM_Z)
    d = Vector((0, 0, TGT_Z)) - cam.location
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    rig.rotation_euler = (0, 0, a)
    scene.render.filepath = f"{prefix}_{name}.png"
    bpy.ops.render.render(write_still=True)

with open(prefix + "_stats.json", "w") as f:
    json.dump(stats, f, indent=1)
print("STATS", json.dumps({k: v for k, v in stats.items() if k != "armatures"}), "BONES",
      [(a["name"], a["bones"]) for a in stats["armatures"]])
