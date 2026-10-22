"""PH2: shoulder pad textures (1K, from the 2K / 4K store maps; no logos in them).
Shell (plastic): gray x team colour (PIX/Lit uniform mode, empty mask = primary colour), normal from the bump map.
Trim (strap + fabric): one 2048 x 1024 atlas (left = strap map, right = fabric map; phr2_pads remaps the UVs), neutral colours, normal atlas.
  blender -b --python Tools/Blender/phr2_pad_textures.py
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *
import phr_tex as T

SRC = "D:/PIXFootball/Source/PPF_NEW/shoulder_pads/Football_Shoulder_Pads_fbx/football_shoulder_pads_"
OUT = os.path.join(OUT_ASSET, "Staging_v07", "Textures", "Pads")
ensure(OUT)


def load(name, size, srgb=True):
    im = load_img(SRC + name + ".png", srgb)
    im.scale(size, size)
    a = img_to_np(im)
    bpy.data.images.remove(im)
    return a[..., :3]


def lum(a):
    return a[..., 0] * 0.2126 + a[..., 1] * 0.7152 + a[..., 2] * 0.0722


def save(name, arr, srgb):
    np_to_png(os.path.join(OUT, name), arr.astype(np.float32), srgb=srgb)
    print("PH2 wrote", name, arr.shape)


# shell
diff = load("plastic_diff_blak", 1024)
L = lum(diff)
cov = L > 0.02
g = np.where(cov, np.clip(L / max(float(L[cov].mean()), 1e-3) * 0.78, 0.05, 1.0), 0.5)
save("PadShell_Gray.png", np.stack([g] * 3, -1), True)
bump = load("plastic_bump", 1024, False)
h = lum(bump)
save("PadShell_N.png", T.height_to_normal(h, 2.5), False)
save("PadShell_Team.png", np.zeros((16, 16, 3), np.float32), False)
save("PadShell_Regions.png", np.zeros((16, 16, 4), np.float32), False)
# trim atlas
s_d = load("strap_diffuse", 1024)
f_d = load("fabric_diffuse", 1024)
atlas = np.zeros((1024, 2048, 3), np.float32)
atlas[:, :1024] = s_d
atlas[:, 1024:] = f_d
save("PadTrim_Albedo.png", np.clip(atlas, 0, 1), True)
s_b = lum(load("strap_bump", 1024, False))
sn = T.height_to_normal(s_b, 2.0)
f_n = load("fabric_normal", 1024, False)
natlas = np.zeros((1024, 2048, 3), np.float32)
natlas[:, :1024] = sn
natlas[:, 1024:] = f_n
save("PadTrim_N.png", natlas, False)
