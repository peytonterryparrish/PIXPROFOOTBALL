"""PH2: pads silhouette before / after (live v0.6.0 body vs Staging_v07 Std body, rest pose, LOD0..3).
  blender -b --python Tools/Blender/phr2_silhouette.py
Writes D:/PIXFootball/Renders/roster_v2/pads_silhouette_{front,side}.png : left = before (live), middle = after (staged, pads), right = overlay (red = before only, blue = after only, white = both).
Also LOD silhouettes of the staged Medium body (LOD0..3 front, overlay of LOD1..3 on LOD0) -> pads_lod_silhouette.png
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *
import phr2_viz as V

LIVE = os.path.join(OUT_ASSET, "Meshes")
STG = os.path.join(OUT_ASSET, "Staging_v07", "Meshes")
OUT = "D:/PIXFootball/Renders/roster_v2/"
TMP = OUT + "_work/"
os.makedirs(TMP, exist_ok=True)
W, H = 520, 760


def mask(path, lod, view, scale=2.05, center=(0, 0.06, 0.98)):
    reset()
    bpy.ops.import_scene.fbx(filepath=path)
    bpy.context.view_layer.update()
    show = [o for o in bpy.data.objects if o.type == 'MESH' and o.name.endswith("_LOD%d" % lod) and not o.name.startswith(("Pads", "Visor"))]
    for o in show:
        o.color = (1, 1, 1, 1)
    sc = V.setup(W, H)
    sc.render.film_transparent = True
    sc.display.shading.light = 'FLAT'
    sc.display.shading.color_type = 'OBJECT'
    for o in bpy.data.objects:
        if o.type == 'MESH':
            o.hide_render = o not in show
    cd = bpy.data.cameras.new('c')
    cam = bpy.data.objects.new('c', cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    cd.type = 'ORTHO'
    cd.ortho_scale = scale
    d = Vector({'front': (0, -1, 0), 'side': (1, 0, 0)}[view])
    cam.location = Vector(center) + d * 5
    cam.rotation_euler = (Vector(center) - cam.location).to_track_quat('-Z', 'Y').to_euler()
    p = TMP + "_m.png"
    sc.render.filepath = p
    sc.render.image_settings.color_mode = 'RGBA'
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(p, check_existing=False)
    a = img_to_np(im)[..., 3] > 0.5
    bpy.data.images.remove(im)
    return a


def compose(b, a):
    img = np.zeros(b.shape + (4,), np.float32)
    img[..., 3] = 1
    img[..., :3] = 0.12
    img[b & ~a] = (0.95, 0.25, 0.2, 1)
    img[a & ~b] = (0.25, 0.5, 1.0, 1)
    img[a & b] = (0.92, 0.92, 0.92, 1)
    return img


def tile(a, col):
    img = np.zeros(a.shape + (4,), np.float32)
    img[..., 3] = 1
    img[..., :3] = 0.12
    img[a] = col
    return img


for view in ("front", "side"):
    rows = []
    for size in ("Small", "Medium", "Large"):
        b = mask(os.path.join(LIVE, "PHR_%s.fbx" % size), 0, view)
        a = mask(os.path.join(STG, "PHR_%s_Std.fbx" % size), 0, view)
        sep = np.ones((H, 6, 4), np.float32)
        rows.append(np.concatenate([tile(b, (0.95, 0.4, 0.35, 1)), sep, tile(a, (0.4, 0.65, 1.0, 1)), sep, compose(b, a)], axis=1))
        print("PH2 silhouette %s %s: before area %d after area %d (+%.0f %%)" % (size, view, b.sum(), a.sum(), 100 * (a.sum() / b.sum() - 1)))
    sheet = np.concatenate(rows, axis=1) if False else np.concatenate(rows, axis=0)
    np_to_png(OUT + "pads_silhouette_%s.png" % view, sheet)

# LOD silhouettes (Medium Std): LOD0..3 and overlay with LOD0
m0 = mask(os.path.join(STG, "PHR_Medium_Std.fbx"), 0, "front")
tiles = [tile(m0, (0.9, 0.9, 0.9, 1))]
for lod in (1, 2, 3):
    ml = mask(os.path.join(STG, "PHR_Medium_Std.fbx"), lod, "front")
    inter = (ml & m0).sum()
    union = (ml | m0).sum()
    print("PH2 LOD%d silhouette IoU vs LOD0 %.4f (area %+.2f %%)" % (lod, inter / union, 100 * (ml.sum() / m0.sum() - 1)))
    tiles.append(compose(m0, ml))
np_to_png(OUT + "pads_lod_silhouette.png", np.concatenate(tiles, axis=1))
