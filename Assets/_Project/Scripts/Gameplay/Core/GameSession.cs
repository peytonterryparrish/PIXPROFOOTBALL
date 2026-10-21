using UnityEngine;
using UnityEngine.SceneManagement;

namespace PixFootball.Gameplay
{
    /// <summary>Scene flow and global game state (v0.1: Free Roam only).</summary>
    public static class GameSession
    {
        public const string MenuScene = "MainMenu";
        public const string StadiumScene = "Stadium_Pro";
        public const string MatchScene = "Match_Pro";

        public static bool Paused { get; private set; }

        public static void StartMatch(MatchConfig cfg) { if (cfg != null) MatchData.Current = cfg; SetPaused(false); SceneManager.LoadScene(MatchScene); }
        public static void StartFreeRoam() { SetPaused(false); SceneManager.LoadScene(StadiumScene); }
        public static void QuitToMenu() { SetPaused(false); SceneManager.LoadScene(MenuScene); }

        public static void SetPaused(bool paused)
        {
            Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            AudioListener.pause = false;
        }

        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
