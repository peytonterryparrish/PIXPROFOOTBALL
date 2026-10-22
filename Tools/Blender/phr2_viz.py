"""PH2 viewing helpers (Workbench renders, orthographic / perspective, coloured by part name)."""
import bpy, math, os
from mathutils import Vector
COL = {'jersey': (0.1, 0.2, 0.55), 'pads': (0.75, 0.12, 0.1), 'pad': (0.75, 0.12, 0.1), 'arms': (0.75, 0.55, 0.42), 'pants': (0.9, 0.9, 0.9), 'socks': (0.9, 0.9, 0.9),
       'helmet': (0.1, 0.35, 0.2), 'head': (0.7, 0.5, 0.4), 'sleeve': (0.2, 0.2, 0.25), 'armsleeve': (0.2, 0.2, 0.25), 'innershirt': (0.2, 0.2, 0.25)}


def color_for(name):
    n = name.lower()
    for k, c in COL.items():
        if n.startswith(k):
            return c
    return (0.6, 0.6, 0.6)


def setup(w=900, h=1000):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_WORKBENCH'
    sc.display.shading.light = 'STUDIO'
    sc.display.shading.color_type = 'OBJECT'
    sc.display.shading.show_cavity = True
    sc.display.shading.cavity_type = 'BOTH'
    sc.render.resolution_x, sc.render.resolution_y = w, h
    sc.render.film_transparent = False
    sc.world = bpy.data.worlds.new("w") if sc.world is None else sc.world
    sc.world.color = (0.85, 0.87, 0.9)
    return sc


def render(path, objs, center, view, scale=1.2, persp=False, size=None, hide=()):
    sc = setup(*(size or (900, 1000)))
    for o in bpy.data.objects:
        if o.type == 'MESH':
            o.color = (*color_for(o.name), 1)
            o.hide_render = (o not in objs) or any(o.name.startswith(h) for h in hide)
    if 'vizcam' in bpy.data.objects:
        cam = bpy.data.objects['vizcam']
    else:
        cd = bpy.data.cameras.new('vizcam')
        cam = bpy.data.objects.new('vizcam', cd)
        sc.collection.objects.link(cam)
    sc.camera = cam
    cam.data.type = 'PERSP' if persp else 'ORTHO'
    cam.data.ortho_scale = scale
    cam.data.lens = 85
    dirs = {'front': (0, -1, 0), 'back': (0, 1, 0), 'side': (1, 0, 0), 'top': (0, 0.001, 1), '34': (0.6, -0.8, 0.45), '34b': (-0.6, -0.8, 0.45), '34r': (0.6, 0.8, 0.45)}
    d = Vector(dirs[view]).normalized()
    c = Vector(center)
    cam.location = c + d * (4 if not persp else scale)
    cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
