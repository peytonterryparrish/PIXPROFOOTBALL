"""MODELS QA: skin-weight statistics of the player body FBXs (influences per vertex, weight lost by a 4-bone truncation, unweighted vertices,
weight share on bones that are not Humanoid-mapped (twist / helper bones) and the deform bone hierarchy.

  blender -b --python Tools/Blender/model_qa_weights.py -- [--fbx a.fbx,b.fbx] [--out Logs/model_qa/blender_weights.txt]
"""
import sys, os, re
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pix_player_common import *
import bpy
import numpy as np


def main():
    a = script_args()
    fbxs = [os.path.join(BODY_DIR, "%sPlayer.fbx" % b) for b in ("Small", "Medium", "Large")]
    out = os.path.join(PROJECT_ROOT, "Logs", "model_qa", "blender_weights.txt")
    i = 0
    while i < len(a):
        if a[i] == "--fbx": fbxs = a[i + 1].split(","); i += 2
        elif a[i] == "--out": out = a[i + 1]; i += 2
        else: i += 1
    lines = []
    def P(s=""):
        print(s); lines.append(s)
    for f in fbxs:
        import_player(f)
        arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
        P("=" * 90); P(os.path.basename(f))
        if f == fbxs[0]:
            P("deform bone hierarchy:")
            def walk(b, d):
                if d < 9:
                    P("  " * d + b.name + ("" if b.use_deform else "  [no deform]"))
                for c in b.children: walk(c, d + 1)
            for b in arm.data.bones:
                if b.parent is None: walk(b, 0)
        for o in sorted((o for o in bpy.data.objects if o.type == 'MESH'), key=lambda o: o.name):
            me = o.data
            names = {vg.index: vg.name for vg in o.vertex_groups}
            n = len(me.vertices)
            cnt = np.zeros(n, np.int32); lost = np.zeros(n); unw = 0
            share = {}
            for v in me.vertices:
                ws = sorted(((g.weight, names.get(g.group, "?")) for g in v.groups if g.weight > 1e-4), reverse=True)
                cnt[v.index] = len(ws)
                tot = sum(w for w, _ in ws)
                if tot < 1e-4: unw += 1; continue
                lost[v.index] = sum(w for w, _ in ws[4:]) / tot
                for w, nm in ws:
                    share[nm] = share.get(nm, 0.0) + w / tot
            P("  mesh %-34s verts %5d  >4 influences: %5d (%.1f%%)  max %d  mean lost-weight on those %.3f  max lost %.3f  unweighted %d  groups %d" %
              (o.name, n, int((cnt > 4).sum()), 100.0 * (cnt > 4).sum() / max(n, 1), int(cnt.max()), float(lost[cnt > 4].mean()) if (cnt > 4).any() else 0.0, float(lost.max()), unw, len(names)))
            if re.match(r"^(Torso|Legs|Arms)", o.name):
                top = sorted(share.items(), key=lambda kv: -kv[1])[:14]
                P("      top bones (summed normalised weight): " + ", ".join("%s %.0f" % (k, v) for k, v in top))
    os.makedirs(os.path.dirname(out), exist_ok=True)
    open(out, "w").write("\n".join(lines))


if __name__ == "__main__":
    main()
