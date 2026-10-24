using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PixFootball.Gameplay;
using PixFootball.UI;
using PixFootball.UI.Franchise;
using PixFootball.UI.Glass;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11 UI-WP2 renders (edit mode, the same composite as the glass UI shots): hub (3 pages), career start (3 pages), team select (teams, options), matchup card, settings (5 pages), dialog and loading, at the
    /// sizes given with <c>-pixShotSizes 1920x1080,3440x1440</c> (default 1920x1080 and 3440x1440) in the theme of <c>-pixShotTheme Tile</c> (default Tile when it exists, else Glass). A <c>-pixShots hub,settings</c> filter limits the screens.
    /// Output: D:\PIXFootball\Renders\v0110_ui_wp2\. Run: -executeMethod PixFootball.EditorTools.UiWp2Shots.Render (no -nographics).
    /// </summary>
    public static class UiWp2Shots
    {
        public const string OutDir = @"D:\PIXFootball\Renders\v0110_ui_wp2";

        static T Field<T>(object o, string name) where T : class
        {
            var f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(o) as T : null;
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static HashSet<string> _only;
        static bool Want(string n) { return _only == null || _only.Contains(n); }

        static UiThemeKind ThemeFromArgs()
        {
            string n = Arg("-pixShotTheme");
            if (!string.IsNullOrEmpty(n)) { try { return (UiThemeKind)Enum.Parse(typeof(UiThemeKind), n, true); } catch { } }
            try { return (UiThemeKind)Enum.Parse(typeof(UiThemeKind), "Tile", true); } catch { return UiThemeKind.Glass; }
        }

        static List<int[]> SizesFromArgs()
        {
            var l = new List<int[]>();
            string s = Arg("-pixShotSizes");
            if (string.IsNullOrEmpty(s)) s = "1920x1080,3440x1440";
            foreach (var p in s.Split(',')) { var wh = p.Split('x'); if (wh.Length == 2) l.Add(new[] { int.Parse(wh[0]), int.Parse(wh[1]) }); }
            return l;
        }

        /// <summary>Edit mode does not run the tab plate slide: snap every tab bar to its selected tab, then render.</summary>
        static Texture2D S(string name, int w, int h, Camera cam, params Transform[] roots)
        {
            foreach (var r in roots) foreach (var tb in r.GetComponentsInChildren<GlassTabs>(true)) if (tb.Tile) tb.SnapPlate();
            return GlassUiShots.Shot(name, w, h, cam, roots);
        }

        static void FocusFirst(MainMenuHub hub, int page)
        {
            foreach (var tl in hub.Tiles(page))
                if (tl.Button != null) { tl.Surface.SetFocus(1f); tl.Surface.transform.localScale = new Vector3(1.02f, 1.02f, 1f); break; }
        }


        /// <summary>The private copy cannot deserialize PixRuntimeAssets.asset (a package / import difference); the renders only need the crests, so a transient registry is filled from the crest PNGs.</summary>
        static void EnsureRuntimeAssets()
        {
            if (PixFootball.Art.PixRuntimeAssets.Load() != null) return;
            var inst = ScriptableObject.CreateInstance<PixFootball.Art.PixRuntimeAssets>(); inst.hideFlags = HideFlags.HideAndDontSave;
            foreach (var t in TeamCatalog.Teams)
            {
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Licensed/UI/Logos/ProV6/" + t.Id + ".png");
                if (sp != null) inst.sprites.Add(new PixFootball.Art.PixRuntimeAssets.SpriteEntry { key = "crest_" + t.Id, sprite = sp });
            }
            typeof(PixFootball.Art.PixRuntimeAssets).GetField("_inst", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, inst);
            Debug.Log("[UiWp2Shots] transient runtime assets with " + inst.sprites.Count + " crests");
        }
        [MenuItem("PIX Football/UI WP2/Render menu screens")]
        public static void Render()
        {
            BatchWatchdog.Arm(40);
            string keep = GlassUiShots.OutDir;
            var only = Arg("-pixShots");
            _only = string.IsNullOrEmpty(only) ? null : new HashSet<string>(only.Split(','));
            try
            {
                GlassUiShots.OutDir = OutDir;
                Directory.CreateDirectory(OutDir);
                QualityTiers.Override(3, -1f);
                var theme = ThemeFromArgs();
                UiTheme.Force(theme);
                GlassTheme.ResetCache();
                Debug.Log("[UiWp2Shots] theme " + theme);
                EnsureRuntimeAssets();
                CareerSession.DirOverride = Path.Combine(Path.GetTempPath(), "pix_wp2_shots");
                if (Directory.Exists(CareerSession.DirOverride)) Directory.Delete(CareerSession.DirOverride, true);
                // a stored last setup so QUICK REMATCH is live and a save so FRANCHISE shows RESUME
                PixSettings.StoreMatchConfig(TeamSelectPanel.InitialConfig(TeamCatalog.Teams, null, null, TeamCatalog.SavedTeamId));
                var save = CareerUiShots.MakeCareer();
                CareerSession.Open(save, new CareerPrefs { Difficulty = 1, QuarterMinutes = 5 }, 1);
                CareerSession.SaveNow();

                var cam = GlassUiShots.OpenMenuStage(); EnsureRuntimeAssets();
                var mm = UnityEngine.Object.FindAnyObjectByType<MainMenuController>();
                mm.SendMessage("Awake");
                var canvasRoot = mm.transform.Find("MenuCanvas");
                var menuRoot = mm.transform.Find("MenuCanvas/MainMenu");
                var settings = mm.GetComponentInChildren<SettingsPanel>(true);
                var team = mm.GetComponentInChildren<TeamSelectPanel>(true);
                var start = Field<CareerStartPanel>(mm, "_careerStart");
                var hub = Field<MainMenuHub>(mm, "_hub");

                foreach (var sz in SizesFromArgs())
                {
                    string tag = sz[0] + "x" + sz[1];
                    int W = sz[0], H = sz[1];
                    Action<string> hide = _ => { };
                    Action none = () => { team.gameObject.SetActive(false); settings.gameObject.SetActive(false); start.gameObject.SetActive(false); menuRoot.gameObject.SetActive(false); };

                    // ---- hub: three pages
                    for (int p = 0; p < 3; p++)
                        if (Want("hub"))
                        {
                            none(); menuRoot.gameObject.SetActive(true);
                            mm.SendMessage("GlassSection", "MAIN MENU");
                            hub.SetPage(p); FocusFirst(hub, p);
                            S("hub" + p + "_" + tag, W, H, cam, mm.transform);
                        }
                    hub.SetPage(0);

                    // ---- career start
                    if (Want("career"))
                    {
                        none(); start.gameObject.SetActive(true);
                        for (int p = 0; p < 3; p++) { start.Show(p); S("career" + p + "_" + tag, W, H, cam, mm.transform); }
                        start.Show(0);
                    }

                    // ---- team select, options, matchup
                    if (Want("team"))
                    {
                        none(); team.gameObject.SetActive(true);
                        mm.SendMessage("GlassSection", "TEAM SELECT");
                        S("teamselect_" + tag, W, H, cam, mm.transform);
                        var tabs = team.GetComponentInChildren<GlassTabs>(true);
                        if (tabs != null) { tabs.SetIndex(1, true); S("teamoptions_" + tag, W, H, cam, mm.transform); tabs.SetIndex(0, true); }
                        team.SendMessage("ShowMatchup");
                        S("matchup_" + tag, W, H, cam, mm.transform);
                        team.SendMessage("HideMatchup");
                    }

                    // ---- settings pages
                    if (Want("settings"))
                    {
                        none(); settings.gameObject.SetActive(true);
                        mm.SendMessage("GlassSection", "SETTINGS");
                        for (int p = 0; p < 5; p++) { settings.SetPage(p); S("settings" + p + "_" + tag, W, H, cam, mm.transform); }
                        settings.SetPage(0);
                    }

                    // ---- dialog over the hub, loading
                    if (Want("dialog"))
                    {
                        none(); menuRoot.gameObject.SetActive(true); mm.SendMessage("GlassSection", "MAIN MENU"); hub.SetPage(0);
                        var dlg = UiDialog.Show(canvasRoot, "Quit the game?", "Return to the desktop. Settings are already saved.", "QUIT", "STAY", null, null);
                        S("dialog_" + tag, W, H, cam, mm.transform);
                        UnityEngine.Object.DestroyImmediate(dlg.gameObject);
                    }
                    if (Want("loading"))
                    {
                        none();
                        MatchData.Current = TeamSelectPanel.InitialConfig(TeamCatalog.Teams, PixSettings.LoadMatchConfig(), MatchData.Current, TeamCatalog.SavedTeamId);
                        var ld = GlassLoading.CreatePreview(GameSession.MatchScene);
                        S("loading_" + tag, W, H, cam, ld.transform);
                        UnityEngine.Object.DestroyImmediate(ld);
                    }
                }
            }
            catch (Exception e) { Debug.LogError("[UiWp2Shots] " + e); }
            finally { GlassUiShots.OutDir = keep; QualityTiers.ClearOverride(); UiTheme.Force(null); }
        }
    }
}
