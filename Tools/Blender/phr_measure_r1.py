import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from phr_common import *
reset()
arm, meshes = import_r1()
for z in (0.15, 0.30, 0.45, 0.60, 0.75, 0.90, 1.02, 1.10, 1.20, 1.30, 1.40, 1.48, 1.54):
    pants = measure([meshes['pants'], meshes['socks']], z, None)
    legL = measure([meshes['pants'], meshes['socks']], z, 'L')
    jers = measure([meshes['jersey']], z)
    print("Z %.2f  legL %s  both %s  jersey %s" % (z, tuple(round(x, 3) for x in legL) if legL else None, tuple(round(x, 3) for x in pants) if pants else None, tuple(round(x, 3) for x in jers) if jers else None))
for n, o in meshes.items():
    mn, mx = bbox([o])
    print("PART %-12s x %.3f..%.3f  y %.3f..%.3f  z %.3f..%.3f" % (n, mn.x, mx.x, mn.y, mx.y, mn.z, mx.z))
