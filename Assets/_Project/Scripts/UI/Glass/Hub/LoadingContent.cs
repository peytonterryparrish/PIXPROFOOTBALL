using System.Collections.Generic;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// The words of the loading screen (the shell is <see cref="GlassLoading"/>): control tips that are true for the mode the game runs in. v0.11 starts every game as a watch game, so the tips
    /// explain the watch controls (Start pauses, View changes the camera, Y hides the HUD, X the helper bar, RB the speed) instead of the passing controls of the user game. Pure lists; <see cref="Apply"/>
    /// writes them into the shell's tip array (same length), the menu calls it whenever it loads.
    /// </summary>
    public static class LoadingContent
    {
        public const int Count = 6;

        public static readonly string[] WatchTips =
        {
            "Press Start to pause the game and open the pause menu.",
            "Press View to change the camera at any time.",
            "Press Y to hide the HUD for a clean broadcast picture.",
            "Press X to turn the helper bar off and on.",
            "Press RB to switch the game speed between 1x and 2x.",
            "Settings, Gameplay: Controller input turns player control back on."
        };

        public static readonly string[] PlayTips =
        {
            "Tap a receiver button for a bullet pass, hold it for a lob.",
            "Lead your receiver with the right stick before you throw.",
            "Call a timeout with LT before the play clock runs out.",
            "The left stick scrambles the quarterback out of the pocket.",
            "Press View to change the camera at any time.",
            "The coach picks suggest a play for the down and distance."
        };

        /// <summary>The tips for the current mode (watch when controller input is off).</summary>
        public static string[] Tips(bool watch) { return watch ? WatchTips : PlayTips; }

        /// <summary>One tip, chosen by <paramref name="seed"/> (pure).</summary>
        public static string Pick(bool watch, int seed)
        {
            var t = Tips(watch);
            return t[((seed % t.Length) + t.Length) % t.Length];
        }

        /// <summary>The subtitle under the loading title: "WATCH GAME" for a watched match.</summary>
        public static string Subtitle(string scene, bool watch)
        {
            if (scene == GameSession.MatchScene) return watch ? "WATCH GAME" : "KICKOFF";
            if (scene == GameSession.StadiumScene) return "FREE ROAM";
            return "MAIN MENU";
        }

        /// <summary>Writes the tips of the current mode into <see cref="GlassLoading.Tips"/> (the shell picks one at random).</summary>
        public static void Apply()
        {
            var src = Tips(WatchMode.Enabled);
            var dst = GlassLoading.Tips;
            for (int i = 0; i < dst.Length; i++) dst[i] = src[i % src.Length];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { Apply(); }
    }
}
