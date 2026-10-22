import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import phr2_build as B2
import phr2_pads as PD
from phr_common import *
a = args()
for kv in [x for x in a[2:] if "=" in x]:
    k, v = kv.split("=")
    if k == "ext": B2.PAD_SLEEVE_EXT = float(v)
    else: PD.FIT[k] = float(v)
arm, sets = B2.build2(a[0], a[1], "D:/PIXFootball/PhotorealWork/v2/test", export=("export" in a))
for lod in range(4):
    r = PD.poke_report(sets["Jersey"][lod], sets["Pads"][0])
    print("POKE LOD%d pad vertices outside jersey: %d / %d by slot %s worst %.3f m" % (lod, r[0], r[1], r[2], r[3]))
