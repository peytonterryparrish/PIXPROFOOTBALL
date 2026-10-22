"""PH2: compare an extra body FBX against R1 (face-index correspondence + UV identity).  blender -b --python phr2_cmp.py -- <fbx>"""
import bpy, sys, numpy as np
src = sys.argv[sys.argv.index('--')+1]
r1 = "C:/Users/peyto/Desktop/PeytonParrishGames/PIX PRO FOOTBALL/_AssetSource/UserModels/a-pose-rigged-joe-burrow-cincinnati-bengals-nfl-3d-model/A-P.glb"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=r1)
R1 = {}
for o in list(bpy.data.objects):
    if o.type=='MESH':
        key=o.name.replace('\u1ec9','i')
        R1[key]=o
    o.name="R1X_"+o.name
before=set(bpy.data.objects)
bpy.ops.import_scene.fbx(filepath=src)
NEW = {o.name.lower():o for o in bpy.data.objects if o.type=='MESH' and o not in before}
bpy.context.view_layer.update()
def polys_world(o):
    me=o.data
    M=np.array(o.matrix_world)
    co=np.array([v.co[:] for v in me.vertices]); co=co@M[:3,:3].T+M[:3,3]
    return me,co
print("NEWKEYS",sorted(NEW))
pairs={'arms':'arms','jersey':'jersey','pants':'pants','socks':'socks','shorts':'shorts','cleats':'cleats','helmet':'helmet','facemask':'facemask','chinstrap':'chinstrap','Padding':'padding','handwarmer':'bag','innershit':'innershit','armsleeve':'armsleever','head':'head','wristcoach':'wristcoach','eyes':'eyes'}
for rk,nk in pairs.items():
    if rk not in R1 or nk not in NEW: print("skip",rk,nk); continue
    ro=R1[rk]; no=NEW[nk]
    rme,rco=polys_world(ro); nme,nco=polys_world(no)
    rc=np.array([rco[list(p.vertices)].mean(0) for p in rme.polygons])
    nc=np.array([nco[list(p.vertices)].mean(0) for p in nme.polygons]); nt=np.array([len(p.vertices)-2 for p in nme.polygons])
    if len(rc)!=nt.sum(): print(rk,nk,"tri count mismatch",len(rc),nt.sum()); continue
    ne=np.repeat(nc,nt,axis=0)
    d=np.linalg.norm(rc-ne,axis=1)
    ru=np.array([u.uv[:] for u in rme.uv_layers[0].data]); nu=np.array([u.uv[:] for u in nme.uv_layers[0].data]) if len(nme.uv_layers) else np.zeros((0,2))
    rs=set(map(tuple,np.round(ru,4))); ns=set(map(tuple,np.round(nu,4)))
    print(f"{rk:12s}-{nk:10s} faceidx dist mean {d.mean():.4f} max {d.max():.4f} p99 {np.percentile(d,99):.4f} | uv R1 {len(rs)} new {len(ns)} common {len(rs&ns)}")
