"""Coach texture prep (inside Blender, numpy):
  1. PIX_Coach_HairCards.png  = hair card colour atlas (T_HairCards_BC) with the alpha atlas (T_HairCards_A) packed into A
  2. prints the average colour of the head skin texture and of the 15 arm skin tones, to pick matching pairs
    blender -b --python make_coach_textures.py -- <project_root>
"""
import bpy, sys, os
import numpy as np
root = sys.argv[sys.argv.index("--") + 1]
src = os.path.join(root, "_AssetSource", "DraftFever", "3D", "Characters", "Coaches", "Hair", "Textures")
out = os.path.join(root, "Assets", "_Licensed", "DraftFever", "Characters", "Coaches", "Rigged", "Textures")
os.makedirs(out, exist_ok=True)


def load(path):
    im = bpy.data.images.load(path)
    w, h = im.size
    a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
    bpy.data.images.remove(im)
    return a


bc = load(os.path.join(src, "T_HairCards_BC.png"))
al = load(os.path.join(src, "T_HairCards_A.png"))
res = bc.copy()
res[..., :3] = np.clip(bc[..., :3] * 1.6 + 0.12, 0, 1)   # lift the grey strands so a dark tint still reads as hair
res[..., 3] = al[..., 0]
im = bpy.data.images.new("hair", res.shape[1], res.shape[0], alpha=True)
im.pixels = res.reshape(-1).tolist()
im.filepath_raw = os.path.join(out, "PIX_Coach_HairCards.png")
im.file_format = 'PNG'
im.save()
print("saved hair atlas")

skin_dir = os.path.join(root, "Assets", "_Licensed", "DraftFever", "Characters", "SkinTextures", "Arms")
head = load(os.path.join(root, "Assets", "_Licensed", "DraftFever", "Characters", "Bodies", "mediumPlayer.fbm", "T_Head_BC_01.png"))
m = head[::8, ::8, :3].reshape(-1, 3)
m = m[m.sum(1) > 0.15]
print("HEAD mean", m.mean(0))
for i in range(1, 16):
    p = os.path.join(skin_dir, "T_Arms_Skin_%02d.png" % i)
    if not os.path.exists(p):
        continue
    a = load(p)
    m = a[::16, ::16, :3].reshape(-1, 3)
    m = m[m.sum(1) > 0.15]
    print("ARMS %02d mean" % i, m.mean(0))

# lighter head variant (the shipped head atlas is a single medium-brown skin): scale towards the arm tone T_Arms_Skin_06
hm = np.array([0.4640069, 0.3003983, 0.21235323])
target = np.array([0.5927463, 0.43051904, 0.35574436])
ratio = target / hm
lh = head.copy()
lh[..., :3] = np.clip(head[..., :3] * ratio, 0, 1)
im = bpy.data.images.new("headlight", lh.shape[1], lh.shape[0], alpha=True)
im.pixels = lh.reshape(-1).tolist()
im.filepath_raw = os.path.join(out, "PIX_Coach_Head_Light.png")
im.file_format = 'PNG'
im.save()
print("saved light head")
