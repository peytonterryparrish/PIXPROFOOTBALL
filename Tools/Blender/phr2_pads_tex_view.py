import bpy, os, numpy as np
d="D:/PIXFootball/Source/PPF_NEW/shoulder_pads/Football_Shoulder_Pads_fbx/"
out="D:/PIXFootball/Renders/roster_v2/_work/"
names=sorted(f for f in os.listdir(d) if f.endswith('.png'))
tiles=[]
for n in names:
    im=bpy.data.images.load(d+n)
    print(n, im.size[:], im.channels, im.colorspace_settings.name)
    im.scale(384,384)
    a=np.empty(384*384*im.channels,np.float32); im.pixels.foreach_get(a); a=a.reshape(384,384,im.channels)
    if im.channels==1: a=np.repeat(a,3,axis=2)[:,:,:3]
    tiles.append(a[:,:,:3])
cols=4; rows=(len(tiles)+cols-1)//cols
sheet=np.zeros((rows*384,cols*384,4),np.float32); sheet[:,:,3]=1
for i,t in enumerate(tiles):
    r,c=divmod(i,cols); sheet[r*384:(r+1)*384,c*384:(c+1)*384,:3]=t
im=bpy.data.images.new("s",cols*384,rows*384,alpha=True); im.pixels.foreach_set(sheet.ravel()); im.filepath_raw=out+"pads_tex_sheet.png"; im.file_format='PNG'; im.save()
print("ORDER",names)
