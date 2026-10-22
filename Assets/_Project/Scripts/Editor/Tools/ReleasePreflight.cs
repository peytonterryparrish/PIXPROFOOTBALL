using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>The generated-data preflight of Docs/RELEASING.md in one editor session: ArtLibrary + tables + controller + clip facing (AnimRebuild.Stage2), player mesh LODs, V6 art, scenes.</summary>
    public static class ReleasePreflight
    {
        public static void Run() { RunStages(true, true, true, true); }
        public static void DataOnly() { RunStages(true, true, true, false); }
        public static void ScenesOnly() { RunStages(false, false, false, true); }

        /// <summary>Opens the main menu and enters play mode (batch): the player-side autotests (-pixAutotest ...) then run inside the editor, e.g. the look gallery for exposure tuning.</summary>
        public static void PlayMenu()
        {
            BatchWatchdog.Arm(25);
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(SceneBuilder.MenuScenePath);
            EditorApplication.isPlaying = true;
        }

        static void RunStages(bool anim, bool meshes, bool v6, bool scenes)
        {
            BatchWatchdog.Arm(90);
            AssetDatabase.Refresh();
            if (anim) { Debug.Log("[Preflight] stage anim"); AnimRebuild.Stage2(); AssetDatabase.Refresh(); }
            if (meshes) { Debug.Log("[Preflight] stage meshes"); BodyMeshImporter.Run(); AssetDatabase.Refresh(); }
            if (v6) { Debug.Log("[Preflight] stage v6"); V6ArtBuilder.BuildAll(); AssetDatabase.Refresh(); }
            if (scenes) { Debug.Log("[Preflight] stage scenes"); SceneBuilder.BuildAll(); AssetDatabase.SaveAssets(); }
            Debug.Log("[Preflight] done");
        }
    }
}
