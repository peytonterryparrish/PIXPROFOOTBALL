using System.Reflection;
using PixFootball.Gameplay;
using PixFootball.UI;
using PixFootball.UI.Glass;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.7 track C menu renders: the tile hub (four pages), the pre-game matchup card, team select, the settings pages and the quit dialog, at 2560x1440 and, for the hub and the matchup card, also
    /// 1920x1080, 3440x1440 (21:9), 2560x1600 (16:10) and 1440x1080 (4:3). Output: D:\PIXFootball\Renders\v070_c\menus\.
    /// Run: Tools/unity.ps1 -Method PixFootball.EditorTools.GlassUiShots.HubMenus -Log c_menu.log
    /// </summary>
    public static partial class GlassUiShots
    {
        public const string HubOutDir = @"D:\PIXFootball\Renders\v070_c\menus";

        static T Field<T>(object o, string name) where T : class
        {
            var f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(o) as T : null;
        }

        [MenuItem("PIX Football/Glass UI/Render hub menus")]
        public static void HubMenus()
        {
            BatchWatchdog.Arm(30);
            string keep = OutDir;
            try
            {
                OutDir = HubOutDir;
                QualityTiers.Override(3, -1f);
                UiTheme.Force(UiThemeKind.Glass);
                var cam = OpenMenuStage();
                var mm = Object.FindFirstObjectByType<MainMenuController>();
                mm.SendMessage("Awake");
                var canvasRoot = mm.transform.Find("MenuCanvas");
                var menuRoot = mm.transform.Find("MenuCanvas/MainMenu");
                var settings = mm.GetComponentInChildren<SettingsPanel>(true);
                var team = mm.GetComponentInChildren<TeamSelectPanel>(true);
                var hub = Field<MainMenuHub>(mm, "_hub");
                var sizes = new[] { new[] { 2560, 1440 }, new[] { 1920, 1080 }, new[] { 3440, 1440 }, new[] { 2560, 1600 }, new[] { 1440, 1080 } };
                foreach (var sz in sizes)
                {
                    string tag = sz[0] + "x" + sz[1];
                    bool main = sz[0] == 2560 && sz[1] == 1440;
                    // ---- hub
                    team.gameObject.SetActive(false); settings.gameObject.SetActive(false); menuRoot.gameObject.SetActive(true);
                    mm.SendMessage("GlassSection", "MAIN MENU");
                    for (int p = 0; p < (main ? 4 : 1); p++)
                    {
                        hub.SetPage(p);
                        FocusHero(hub, p);
                        Shot("hub" + p + "_" + tag, sz[0], sz[1], cam, mm.transform);
                    }
                    hub.SetPage(0);
                    // ---- team select
                    menuRoot.gameObject.SetActive(false); team.gameObject.SetActive(true);
                    mm.SendMessage("GlassSection", "TEAM SELECT");
                    Shot("teamselect_" + tag, sz[0], sz[1], cam, mm.transform);
                    // ---- matchup card
                    team.SendMessage("ShowMatchup");
                    Shot("matchup_" + tag, sz[0], sz[1], cam, mm.transform);
                    team.SendMessage("HideMatchup");
                    if (!main) continue;
                    // ---- settings pages and the dialog
                    team.gameObject.SetActive(false); settings.gameObject.SetActive(true);
                    mm.SendMessage("GlassSection", "SETTINGS");
                    for (int p = 0; p < 5; p++) { settings.SetPage(p); Shot("settings_page" + p + "_" + tag, sz[0], sz[1], cam, mm.transform); }
                    settings.SetPage(0);
                    settings.gameObject.SetActive(false); menuRoot.gameObject.SetActive(true);
                    mm.SendMessage("GlassSection", "MAIN MENU");
                    var dlg = UiDialog.Show(canvasRoot, "Quit the game?", "Return to the desktop. Settings are already saved.", "QUIT", "STAY", null, null);
                    Shot("dialog_" + tag, sz[0], sz[1], cam, mm.transform);
                    Object.DestroyImmediate(dlg.gameObject);
                }
                // the match options tab of team select (a second page of the same panel)
                {
                    menuRoot.gameObject.SetActive(false); team.gameObject.SetActive(true);
                    mm.SendMessage("GlassSection", "TEAM SELECT");
                    var tabs = team.GetComponentInChildren<GlassTabs>(true);
                    if (tabs != null) tabs.SetIndex(1, true);
                    Shot("teamselect_options_2560x1440", 2560, 1440, cam, mm.transform);
                }
            }
            catch (System.Exception e) { Debug.LogError("[GlassUiShots] " + e); }
            finally { OutDir = keep; QualityTiers.ClearOverride(); }
        }

        /// <summary>Edit mode does not run the focus animation: show the hero tile of the page focused (orange edge and glow) as it is when the pad lands on it.</summary>
        static void FocusHero(MainMenuHub hub, int page)
        {
            foreach (var tl in hub.Tiles(page))
                if (tl.Button != null) { tl.Surface.SetFocus(1f); tl.Surface.transform.localScale = new Vector3(1.02f, 1.02f, 1f); break; }
        }
    }
}
