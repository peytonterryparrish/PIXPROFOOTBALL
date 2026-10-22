"""Pack PBR texture sets for Unity HDRP Lit.

  blender -b --python Tools/Blender/pix_pack_pbr.py -- --dir <folder> [--name T_Uniform] [--out Logs/blender_v6]
      or explicit: --metallic m.png --roughness r.png [--ao ao.png] [--detail d.png] [--normal n.png] [--basecolor bc.png]
      [--normal-in opengl|directx] [--normal-out opengl|directx] [--size 2048] [--bits 8|16] [--glossiness] [--no-renormalize]

Writes into --out:
  <name>_M.png   HDRP Lit MASK MAP: R = metallic, G = ambient occlusion, B = detail mask, A = smoothness (= 1 - roughness).
                 Linear data (import as sRGB OFF). Missing inputs default to metallic 0 / AO 1 / detail mask 1 (roughness missing -> 0.5).
  <name>_N.png   normal map converted between conventions (flip of the green channel). Unity (HDRP 'NormalMap' import) expects
                 OpenGL / Y+ ; DirectX (Y-) sources (Substance 'DirectX', Unreal) must be flipped: --normal-in directx.
  --dir auto-detects files by name (base colour / metallic / roughness / ao|occlusion / detail / normal); an '_OpenGL' / '_DirectX' tag in
  the normal map file name sets --normal-in unless given. Inputs may have different resolutions (bilinear resize to the largest, or --size).
  --glossiness: the roughness input is already a smoothness/glossiness map (no inversion).
Nothing is written for base colour (it stays an sRGB colour texture imported as-is) apart from being listed in the log.
"""
import sys, os, re, glob
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix_player_common import *
import bpy

PATTERNS = [("basecolor", r"base_?colou?r|albedo|diffuse|_bc\b|_bc\."), ("metallic", r"metal"), ("roughness", r"rough|gloss"),
            ("ao", r"(^|[_\-. ])ao([_\-. ]|$)|ambient|occlusion"), ("detail", r"detail"), ("normal", r"normal|(^|[_\-. ])n([_\-. ]|$)")]


def parse():
    a = script_args()
    o = dict(dir=None, name=None, out=os.path.join(PROJECT_ROOT, "Logs", "blender_v6"), normal_in=None, normal_out="opengl", size=None,
             bits=8, glossiness=False, renorm=True, rough_default=0.5, files={})
    i = 0
    while i < len(a):
        k = a[i]
        if k == "--dir": o["dir"] = a[i + 1]; i += 2
        elif k == "--name": o["name"] = a[i + 1]; i += 2
        elif k == "--out": o["out"] = a[i + 1]; i += 2
        elif k == "--normal-in": o["normal_in"] = a[i + 1].lower(); i += 2
        elif k == "--normal-out": o["normal_out"] = a[i + 1].lower(); i += 2
        elif k == "--size": o["size"] = int(a[i + 1]); i += 2
        elif k == "--bits": o["bits"] = int(a[i + 1]); i += 2
        elif k == "--glossiness": o["glossiness"] = True; i += 1
        elif k == "--no-renormalize": o["renorm"] = False; i += 1
        elif k == "--default-roughness": o["rough_default"] = float(a[i + 1]); i += 2
        elif k in ("--basecolor", "--metallic", "--roughness", "--ao", "--detail", "--normal"):
            o["files"][k[2:]] = a[i + 1]; i += 2
        else: raise SystemExit("unknown arg " + k)
    return o


def discover(folder):
    found = {}
    for f in sorted(glob.glob(os.path.join(folder, "*.png"))):
        b = os.path.basename(f).lower()
        for key, pat in PATTERNS:
            if key in found:
                continue
            if re.search(pat, b):
                found[key] = f
                break
    return found


def resize(a, h, w):
    """Bilinear resize of [H,W,C]."""
    H, W = a.shape[:2]
    if (H, W) == (h, w):
        return a
    ys = (np.arange(h) + 0.5) * H / h - 0.5; xs = (np.arange(w) + 0.5) * W / w - 0.5
    y0 = np.clip(np.floor(ys).astype(int), 0, H - 1); x0 = np.clip(np.floor(xs).astype(int), 0, W - 1)
    y1 = np.clip(y0 + 1, 0, H - 1); x1 = np.clip(x0 + 1, 0, W - 1)
    fy = np.clip(ys - y0, 0, 1)[:, None, None]; fx = np.clip(xs - x0, 0, 1)[None, :, None]
    top = a[y0][:, x0] * (1 - fx) + a[y0][:, x1] * fx
    bot = a[y1][:, x0] * (1 - fx) + a[y1][:, x1] * fx
    return top * (1 - fy) + bot * fy


def gray(img):
    """Single channel from an RGB(A) or grey image: red channel (grey maps have R=G=B)."""
    return img[..., 0]


def main():
    o = parse()
    files = dict(o["files"])
    if o["dir"]:
        for k, v in discover(o["dir"]).items():
            files.setdefault(k, v)
    name = o["name"] or (os.path.splitext(os.path.basename(files.get("metallic") or files.get("roughness") or files.get("normal") or "pbr"))[0]
                         if not o["dir"] else os.path.basename(os.path.normpath(o["dir"])))
    print("inputs:", {k: os.path.basename(v) for k, v in files.items()})
    loaded = {}
    for k, p in files.items():
        if k == "basecolor":
            continue
        loaded[k], _ = load_png(p)
    if not loaded:
        raise SystemExit("no input textures given")
    H = max(a.shape[0] for a in loaded.values()); W = max(a.shape[1] for a in loaded.values())
    if o["size"]:
        H = W = o["size"]
    for k in loaded:
        loaded[k] = resize(loaded[k], H, W)
    os.makedirs(o["out"], exist_ok=True)

    # ---- mask map
    if any(k in loaded for k in ("metallic", "roughness", "ao", "detail")):
        M = np.empty((H, W, 4), np.float32)
        M[..., 0] = gray(loaded["metallic"]) if "metallic" in loaded else 0.0
        M[..., 1] = gray(loaded["ao"]) if "ao" in loaded else 1.0
        M[..., 2] = gray(loaded["detail"]) if "detail" in loaded else 1.0
        if "roughness" in loaded:
            r = gray(loaded["roughness"])
            M[..., 3] = r if o["glossiness"] else 1.0 - r
        else:
            M[..., 3] = 1.0 - o["rough_default"]
        mp = os.path.join(o["out"], name + "_M.png")
        save_png(mp, M, 'Non-Color', o["bits"])
        print("wrote %s  %dx%d  mean R(metal)=%.3f G(ao)=%.3f B(detail)=%.3f A(smooth)=%.3f" % (mp, W, H, M[..., 0].mean(), M[..., 1].mean(), M[..., 2].mean(), M[..., 3].mean()))

    # ---- normal map
    if "normal" in loaded:
        n = loaded["normal"][..., :3].copy()
        src = o["normal_in"] or ("directx" if re.search(r"direct_?x|_dx\b", os.path.basename(files["normal"]).lower())
                                 else "opengl")
        if src != o["normal_out"]:
            n[..., 1] = 1.0 - n[..., 1]
        if o["renorm"]:
            v = n * 2 - 1
            ln = np.maximum(np.linalg.norm(v, axis=2, keepdims=True), 1e-6)
            n = v / ln * 0.5 + 0.5
        npth = os.path.join(o["out"], name + "_N.png")
        save_png(npth, n, 'Non-Color', o["bits"])
        print("wrote %s  %dx%d  source=%s -> %s (green %s)  mean RGB=%s" % (npth, W, H, src, o["normal_out"],
              "flipped" if src != o["normal_out"] else "unchanged", np.round(n.reshape(-1, 3).mean(0), 3)))


main()
