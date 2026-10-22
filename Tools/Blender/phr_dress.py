"""PHR: dress the built player objects (any LOD) with preview materials for Blender renders."""
import os
from phr_preview import *

SKIN = os.path.join(OUT_ASSET, "Textures", "Skin")
BLUE = (0.05, 0.16, 0.46)
GOLD = (0.96, 0.72, 0.10)
WHITE = (0.93, 0.93, 0.93)


def tex_material(name, albedo, normal=None, rough=0.5, nstrength=0.7, sss=0.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    b = nt.nodes["Principled BSDF"]
    t = nt.nodes.new("ShaderNodeTexImage")
    t.image = load_img(albedo)
    nt.links.new(t.outputs[0], b.inputs["Base Color"])
    b.inputs["Roughness"].default_value = rough
    try:
        b.inputs["Subsurface Weight"].default_value = sss
        b.inputs["Subsurface Radius"].default_value = (1.0, 0.3, 0.2)
        b.inputs["Subsurface Scale"].default_value = 0.01
    except Exception:
        pass
    if normal:
        nm = nt.nodes.new("ShaderNodeNormalMap")
        nm.inputs["Strength"].default_value = nstrength
        tn = nt.nodes.new("ShaderNodeTexImage")
        tn.image = load_img(normal, False)
        nt.links.new(tn.outputs[0], nm.inputs["Color"])
        nt.links.new(nm.outputs[0], b.inputs["Normal"])
    return m


def dress(objs, primary=BLUE, secondary=GOLD, tertiary=WHITE, tone=3, glove=None):
    """objs: iterable of mesh objects named <Part>_LOD<n>."""
    cache = {}
    def once(key, fn):
        if key not in cache:
            cache[key] = fn()
        return cache[key]
    for o in objs:
        base = o.name.rsplit("_LOD", 1)[0]
        if base in ("Head_Brows", "Head_Beard", "Head_Scalp", "Visor"):
            o.hide_render = True
            continue
        if base == "Jersey":
            m = once(base, lambda: uniform_material("jersey", primary, secondary, tertiary, 0.6, 0.7))
        elif base == "Pants":
            m = once(base, lambda: uniform_material("pants", tertiary, primary, secondary, 0.6, 0.7))
        elif base in ("Socks", "Undersock"):
            m = once("socks", lambda: uniform_material("socks", primary, secondary, tertiary, 0.7, 0.6, tex="socks"))
        elif base == "Cleats":
            m = once(base, lambda: uniform_material("cleats", tertiary, secondary, (0.04, 0.04, 0.04), 0.45, 0.7))
        elif base in ("SleeveR", "SleeveL"):
            m = once("sleeve", lambda: flat_material("sleeve", tertiary, 0.6))
        elif base in ("Wristband", "PlayCard", "Towel"):
            m = once("band", lambda: flat_material("band", WHITE, 0.7))
        elif base == "Pouch":
            m = once(base, lambda: flat_material("pouch", (0.04, 0.04, 0.04), 0.6))
        elif base == "Helmet_Shell":
            m = once(base, lambda: flat_material("helmet", primary, 0.22))
        elif base == "FaceMask":
            m = once(base, lambda: flat_material("mask", (0.06, 0.06, 0.07), 0.35, 0.6))
        elif base in ("Chinstrap", "HelmetPad"):
            m = once(base, lambda: flat_material("padding", WHITE, 0.7))
        elif base == "Eyes":
            m = once(base, lambda: tex_material("eyes", DF + "/Bodies/mediumPlayer.fbm/Eyes_MAT_Base_color.png", None, 0.15))
        elif base == "Head":
            m = once(base, lambda: tex_material("head", f"{SKIN}/PHR_Head_T{tone}.png", f"{SKIN}/PHR_Head_N.png", 0.45, 0.8))
        elif base == "Arms":
            arms_m = once("arms", lambda: tex_material("arms", f"{SKIN}/PHR_Arms_T{tone}.png", f"{SKIN}/PHR_Arms_N.png", 0.5, 0.8))
            hands_m = arms_m if glove is None else once("glove", lambda: flat_material("glove", glove, 0.8))
            o.data.materials.clear()
            o.data.materials.append(arms_m)
            o.data.materials.append(hands_m)
            continue
        else:
            m = once(base, lambda: flat_material(base, WHITE, 0.6))
        assign(o, m)
