"""PHR preview materials for Blender renders (Principled approximation of the Unity PIX/Lit uniform mode).  Imported by test / render scripts."""
import os
from phr_tex import *

TEXDIR = os.path.join(OUT_ASSET, "Textures", "Uniform")
_cache = {}


def _img_from_np(name, arr, srgb):
    h, w = arr.shape[:2]
    if arr.shape[2] == 3:
        arr = np.concatenate([arr, np.ones((h, w, 1), np.float32)], axis=2)
    im = bpy.data.images.new(name, w, h, alpha=False)
    im.colorspace_settings.name = 'sRGB' if srgb else 'Non-Color'
    im.pixels.foreach_set(np.ascontiguousarray(arr[::-1].astype(np.float32)).ravel())
    im.pack()
    return im


def uniform_material(part, primary_srgb, secondary_srgb, tertiary_srgb, rough=0.55, normal_strength=0.6, tex=None):
    """Principled material with the composed albedo of <part>_Gray/_Team (colours given as sRGB 0..1 tuples)."""
    tex = tex or part
    key = (tex, tuple(primary_srgb), tuple(secondary_srgb), tuple(tertiary_srgb))
    if key in _cache:
        return _cache[key]
    gray = load(f"{TEXDIR}/{tex}_Gray.png", True)[..., 0]
    mask = load(f"{TEXDIR}/{tex}_Team.png", False)[..., :3]
    lin = compose_uniform(gray, mask, srgb_to_lin(primary_srgb), srgb_to_lin(secondary_srgb), srgb_to_lin(tertiary_srgb))
    alb = _img_from_np(f"pv_{part}_alb", lin_to_srgb(lin).astype(np.float32), True)
    nimg = None
    npath = f"{TEXDIR}/{tex}_N.png"
    if os.path.exists(npath):
        nimg = bpy.data.images.load(npath)
        nimg.colorspace_settings.name = 'Non-Color'
    m = bpy.data.materials.new(f"pv_{part}")
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = alb
    nt.links.new(t.outputs[0], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = rough
    if nimg is not None:
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.inputs["Strength"].default_value = normal_strength
        nt2 = nt.nodes.new("ShaderNodeTexImage")
        nt2.image = nimg
        nt.links.new(nt2.outputs[0], nm.inputs["Color"])
        nt.links.new(nm.outputs[0], b.inputs["Normal"])
    _cache[key] = m
    return m


def flat_material(name, srgb, rough=0.5, metallic=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*srgb_to_lin(srgb), 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metallic
    return m


def assign(obj, mat):
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    for p in obj.data.polygons:
        p.use_smooth = True
