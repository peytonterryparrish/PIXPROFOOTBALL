using UnityEditor;
using UnityEditor.SceneManagement;

namespace PixFootball.EditorTools
{
    /// <summary>Runs the autotest inside the editor (fast iteration without a player build). Launch without -batchmode and pass -pixAutotest.</summary>
    public static class PlayModeRunner
    {
        public static void RunAutoTest()
        {
            EditorSceneManager.OpenScene(SceneBuilder.MenuScenePath);
            EditorApplication.isPlaying = true;
        }
    }
}
