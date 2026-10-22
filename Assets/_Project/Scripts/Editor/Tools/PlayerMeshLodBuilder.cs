using UnityEditor;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.5.x: the old vertex-clustering LODs (which thinned waists and legs and tore the UVs, MDL-001..) are gone. The LOD1..LOD3 meshes now come from Blender
    /// (Tools/Blender/pix_player_fix.py: quadric decimation with UV-border protection, bulky LOD0 bodies) and are imported by <see cref="BodyMeshImporter"/>.
    /// This entry point is kept for BuildAllRun and the menu.
    /// </summary>
    public static class PlayerMeshLodBuilder
    {
        [MenuItem("PIX Football/Assets/Build Player Mesh LODs")]
        public static void Run() { BodyMeshImporter.Run(); }
    }
}
