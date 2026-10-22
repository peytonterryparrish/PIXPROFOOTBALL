import bpy, sys, numpy as np
src = "D:/PIXFootball/Source/PPF_NEW/shoulder_pads/Football_Shoulder_Pads_fbx/Football_Shoulder_Pads.FBX"
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
bpy.context.view_layer.update()
tot=0
for o in bpy.data.objects:
    if o.type=='MESH':
        me=o.data; me.calc_loop_triangles()
        M=np.array(o.matrix_world)
        co=np.array([v.co[:] for v in me.vertices])@M[:3,:3].T+M[:3,3]
        tot+=len(me.loop_triangles)
        print("OBJ",o.name,"v",len(me.vertices),"t",len(me.loop_triangles),"mats",[m.name for m in me.materials],"uv",[u.name for u in me.uv_layers],"bbox",np.round(co.min(0),3).tolist(),np.round(co.max(0),3).tolist(),"scale",tuple(round(x,3) for x in o.scale),"parent",o.parent.name if o.parent else None)
    else:
        print("OBJ",o.name,o.type)
print("TOTAL tris",tot)
for m in bpy.data.materials:
    print("MAT",m.name,[ (n.type, getattr(n,'image',None) and n.image.filepath) for n in m.node_tree.nodes] if m.use_nodes else None)
