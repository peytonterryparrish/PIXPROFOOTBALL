namespace PixFootball.EditorTools
{
    public static class BuildAllRun
    {
        public static void Prepare() { SceneBuilder.PrepareEverything(); }
        public static void Scenes() { RuntimeAssetsBuilder.Build(); SceneBuilder.BuildAll(); }
        public static void RuntimeAssets() { RuntimeAssetsBuilder.Build(); }
        public static void Crowd() { CrowdBuilder.BuildAll(); }
        public static void CrowdMeshes() { CrowdBuilder.BuildMeshesOnly(); }
    }
}


namespace PixFootball.EditorTools
{
    /// <summary>v0.4 batch entry points: rebuild the ArtLibrary (contact keys), measure the contact table, render contact sheets.</summary>
    public static class ContactRun
    {
        public static void LibraryAndTable() { ArtLibraryBuilder.Build(); ContactClipBuilder.Run(); }
        public static void Table() { ContactClipBuilder.Run(); }
        public static void Restore() { ContactClipBuilder.SetOriginalPositions(false); }
        public static void MoveTakes() { ContactClipBuilder.BuildMoveTakes(); }
        public static void Sheet() { ContactClipBuilder.SetOriginalPositions(true); try { ContactClipBuilder.RunSheet(); } finally { ContactClipBuilder.SetOriginalPositions(false); } }
    }
}

namespace PixFootball.EditorTools
{
    public static class PerfRun
    {
        public static void Players() { PerfDiagnostics.Players(); }
    }
}

namespace PixFootball.EditorTools
{
    public static class MeshLodRun
    {
        public static void Build() { PlayerMeshLodBuilder.Run(); }
    }
}

namespace PixFootball.EditorTools
{
    public static class HdrpRun
    {
        public static void DynamicResolution() { HdrpTuning.EnableDynamicResolution(); }
    }
}
