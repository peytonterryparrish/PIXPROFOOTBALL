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

        /// <summary>Set by the glass UI: handles a scene change itself (loading screen) and returns true; null or false = the plain synchronous load.</summary>
        public static System.Func<string, bool> LoadOverride;
        static void Load(string scene) { if (LoadOverride != null && LoadOverride(scene)) return; SceneManager.LoadScene(scene); }

        public static void StartMatch(MatchConfig cfg) { if (cfg != null) MatchData.Current = cfg; ResetTime(); Load(MatchScene); }
        public static void StartFreeRoam() { ResetTime(); Load(StadiumScene); }
        public static void QuitToMenu() { ResetTime(); Load(MenuScene); }

        static float _scaleBeforePause = 1f;
        /// <summary>The frame a menu closed itself with Esc / B, so the gameplay Pause action on that same key press does not pause again (QA-797).</summary>
        public static int PauseConsumedFrame = -1;

        /// <summary>Scene start / teardown: not paused, normal time, audio on.</summary>
        public static void ResetTime() { Paused = false; Time.timeScale = 1f; AudioListener.pause = false; _scaleBeforePause = 1f; }

        /// <summary>
        /// Pauses / resumes the game. Resuming from a real pause restores the time scale that was running before it (replay slow motion, hit-stop, the autotest speed) instead of forcing 1;
        /// a call that is not a pause transition (scene start / teardown) resets the scale to 1. Pausing also pauses the crowd, whistles and effects (UI sounds ignore the pause).
        /// </summary>
        public static void SetPaused(bool paused)
        {
            if (paused)
            {
                if (!Paused) _scaleBeforePause = Time.timeScale > 0.001f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
            else Time.timeScale = Paused ? _scaleBeforePause : 1f;
            Paused = paused;
            AudioListener.pause = paused;
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
