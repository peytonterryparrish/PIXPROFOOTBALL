"""Hand-held coach props (clipboard, tablet): rigid, unskinned meshes modelled around their own centre
(x = width, y = length, z = thickness, the face towards the coach is -z). The Unity builder (RefCoachBuilder.AttachProps) parents them to
the coach's left hand and calibrates the local transform on the fitted coach_clipboard_idle pose, using the constants below."""
import bpy, bmesh
from mathutils import Vector

# where the board sits in the coach_clipboard_idle pose (Blender world, metres, character faces -Y) and its axes there
BOARD_CENTER = {"clipboard": (0.0, -0.30, 1.22), "tablet": (0.03, -0.30, 1.22)}
AXIS_X = (1.0, 0.0, 0.0)
AXIS_Y = tuple(Vector((0, -0.87, 0.5)).normalized())           # length direction (tilted forward / up)
AXIS_Z = tuple(Vector(AXIS_X).cross(Vector(AXIS_Y)).normalized())


def _mesh(name, boxes, mats):
    bm = bmesh.new()
    for (c, s, mi) in boxes:
        r = bmesh.ops.create_cube(bm, size=1.0, calc_uvs=True)
        for v in r['verts']:
            v.co = Vector((v.co.x * s[0], v.co.y * s[1], v.co.z * s[2])) + Vector(c)
        for f in {f for v in r['verts'] for f in v.link_faces}:
            f.material_index = mi
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in mats:
        me.materials.append(bpy.data.materials.new(m))
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def clipboard(name="Coach_Clipboard"):
    return _mesh(name, [((0, 0, 0), (0.23, 0.32, 0.012), 0), ((0, -0.01, -0.0075), (0.195, 0.27, 0.003), 1), ((0, 0.15, 0.0), (0.09, 0.035, 0.018), 2)],
                 ["PIX_Coach_Clipboard_Board", "PIX_Coach_Clipboard_Paper", "PIX_Coach_Clipboard_Clip"])


def tablet(name="Coach_Tablet"):
    return _mesh(name, [((0, 0, 0), (0.17, 0.24, 0.009), 0), ((0, 0, -0.0045), (0.15, 0.22, 0.0016), 1)],
                 ["PIX_Coach_Tablet_Body", "PIX_Coach_Tablet_Screen"])
