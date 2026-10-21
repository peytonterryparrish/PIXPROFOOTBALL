namespace PixFootball.EditorTools
{
    public static class BuildAllRun
    {
        public static void Prepare() { SceneBuilder.PrepareEverything(); }
        public static void Scenes() { RuntimeAssetsBuilder.Build(); SceneBuilder.BuildAll(); }
        public static void Crowd() { CrowdBuilder.BuildAll(); }
    }
}
