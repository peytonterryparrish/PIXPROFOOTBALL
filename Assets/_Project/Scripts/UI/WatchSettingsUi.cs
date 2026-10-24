using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.UI
{
    /// <summary>v0.11 watch mode rows for the Settings "Gameplay" page and the in-match pause menu.</summary>
    public static class WatchSettingsUi
    {
        public static readonly string[] ControllerNames = { "Off (watch)", "On (play)" };

        /// <summary>Settings page rows: controller input (watch / play), the helper bar and the game speed of a watched game.</summary>
        public static void AddSettingsRows(Transform page, float width = 760f, float height = 50f)
        {
            UiControls.Selector(page, "Controller input", ControllerNames, WatchMode.UserControl ? 1 : 0, i => { WatchMode.UserControl = i == 1; }, width, height);
            UiControls.Selector(page, "Helper bar", WatchMode.HelperNames, (int)WatchMode.Helper, i => { WatchMode.SetHelper((HelperBarMode)i); }, width, height);
            UiControls.Selector(page, "Game speed (watch)", new[] { "1x", "2x" }, WatchMode.SpeedIndex, i => { WatchMode.SpeedIndex = i; }, width, height);
        }

        /// <summary>The pause menu row of a match: the helper bar can be switched here as well.</summary>
        public static OptionSelector AddPauseHelperRow(Transform col, float width, float height)
        {
            return UiControls.Selector(col, "Helper bar", WatchMode.HelperNames, (int)WatchMode.Helper, i => { WatchMode.SetHelper((HelperBarMode)i); }, width, height);
        }
    }
}
