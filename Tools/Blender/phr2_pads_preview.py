import bpy, sys, math, numpy as np
from mathutils import Vector
src = "D:/PIXFootball/Source/PPF_NEW/shoulder_pads/Football_Shoulder_Pads_fbx/Football_Shoulder_Pads.FBX"
out = "D:/PIXFootball/Renders/roster_v2/_work/"
import os; os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
cols={'football_s_p_fabric':(0.12,0.12,0.14,1),'football_s_p_hrome':(0.8,0.8,0.85,1),'football_s_p_plastic_blak':(0.75,0.12,0.1,1),'football_s_p_strap':(0.45,0.45,0.5,1)}
for m in bpy.data.materials:
    if m.name in cols: m.diffuse_color=cols[m.name]
sc=bpy.context.scene
sc.render.engine='BLENDER_WORKBENCH'
sc.display.shading.light='STUDIO'; sc.display.shading.color_type='MATERIAL'
sc.display.shading.show_cavity=True
sc.render.resolution_x=900; sc.render.resolution_y=700
cd=bpy.data.cameras.new('c'); cd.type='ORTHO'; cd.ortho_scale=1.2
cam=bpy.data.objects.new('c',cd); sc.collection.objects.link(cam); sc.camera=cam
c=Vector((0,0,0.45))
for name,d in (('front',(0,-1,0)),('side',(1,0,0)),('top',(0,0,1)),('34',(0.6,-0.8,0.5))):
    d=Vector(d).normalized()
    cam.location=c+d*5
    cam.rotation_euler=(c-cam.location).to_track_quat('-Z','Y' if name!='top' else 'Y').to_euler()
    if name=='top': cam.rotation_euler=(0,0,0)
    sc.render.filepath=out+'pads_'+name+'.png'
    bpy.ops.render.render(write_still=True)
