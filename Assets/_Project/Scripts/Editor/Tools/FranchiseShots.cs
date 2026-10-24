using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.UI;
using PixFootball.UI.Franchise;
using PixFootball.UI.Glass;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11 UI-WP4 franchise renders (edit mode, same composite as the glass UI shots): every franchise tab, sub page, modal and the draft at each size of <see cref="Sizes"/> in the active theme
    /// (Tile when the theme exists, else Glass; <c>-pixUiTheme</c> wins). Output: D:\PIXFootball\Renders\v0110_franchise\ (fr_&lt;page&gt;_&lt;size&gt;.png and navaudit.txt).
    /// Run: -executeMethod PixFootball.EditorTools.FranchiseShots.Render   (no -nographics). <c>-pixShots home,roster</c> limits the pages, <c>-pixSizes 1080,219</c> the sizes.
    /// </summary>
    public static class FranchiseShots
    {
        public const string OutDir = @"D:\PIXFootball\Renders\v0110_franchise";
        static readonly Dictionary<string, Vector2Int> Sizes = new Dictionary<string, Vector2Int>
        {
            { "1080", new Vector2Int(1920, 1080) }, { "219", new Vector2Int(3440, 1440) }, { "1440", new Vector2Int(2560, 1440) }, { "43", new Vector2Int(1440, 1080) }, { "4k", new Vector2Int(3840, 2160) }
        };

        static T Field<T>(object o, string name) where T : class
        {
            var f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            return f != null ? f.GetValue(o) as T : null;
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            var env = Environment.GetEnvironmentVariable("PIX_FR_" + name.Substring(4).ToUpperInvariant());   // -pixShots -> PIX_FR_SHOTS, -pixSizes -> PIX_FR_SIZES (a launcher that cannot quote arguments)
            if (!string.IsNullOrEmpty(env)) return env;
            return null;
        }

        static HashSet<string> _only;
        static bool Want(string n) { return _only == null || _only.Contains(n); }

        [MenuItem("PIX Football/Career/Render franchise (v0.11 UI)")]
        public static void Render()
        {
            BatchWatchdog.Arm(60);
            string keep = GlassUiShots.OutDir;
            var only = Arg("-pixShots");
            _only = string.IsNullOrEmpty(only) ? null : new HashSet<string>(only.Split(','));
            var sizeArg = Arg("-pixSizes");
            var sizes = new List<string>((string.IsNullOrEmpty(sizeArg) ? "1080,219" : sizeArg).Split(','));
            try
            {
                GlassUiShots.OutDir = OutDir;
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(Path.Combine(OutDir, "navaudit.txt"), "");
                QualityTiers.Override(3, -1f);
                UiThemeKind kind;
                if (!Enum.TryParse("Tile", out kind)) kind = UiThemeKind.Glass;
                var themeArg = Arg("-pixUiTheme");
                if (!string.IsNullOrEmpty(themeArg) && Enum.TryParse(themeArg, true, out kind)) { }
                UiTheme.Force(kind);
                Debug.Log("[FranchiseShots] theme " + kind);
                CareerSession.DirOverride = Path.Combine(Path.GetTempPath(), "pix_franchise_shots");
                if (Directory.Exists(CareerSession.DirOverride)) Directory.Delete(CareerSession.DirOverride, true);
                var cam = GlassUiShots.OpenMenuStage();
                var mm = UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
                mm.SendMessage("Awake");
                if (GlassTheme.T.flat) { var cr = Field<Transform>(mm, "_canvasRoot"); if (cr != null) GlassKit.TileBackdrop(cr).SetAsFirstSibling(); }   // WP2 builds the same backdrop into the menu canvas

                var save = CareerUiShots.MakeCareer();
                CareerSession.Open(save, new CareerPrefs { Difficulty = 1, QuarterMinutes = 5 }, 1);
                var menuRoot = mm.transform.Find("MenuCanvas/MainMenu");
                if (menuRoot != null) menuRoot.gameObject.SetActive(false);
                var open = typeof(MainMenuController).GetMethod("OpenFranchise", BindingFlags.NonPublic | BindingFlags.Instance);
                FranchiseUi ui = null;
                Action reopen = () => { open.Invoke(mm, new object[] { FranchiseController.FromSession() }); ui = Field<FranchiseUi>(mm, "_franchise"); };
                Action<string, Action> snap = (name, prep) =>
                {
                    if (!Want(name)) return;
                    try
                    {
                        if (prep != null) prep();
                        foreach (var t in ui.GetComponentsInChildren<GlassTable>(false)) t.ShowFocused(true);
                        PlayerCardPanel.RenderAll();
                        foreach (var sz in sizes)
                        {
                            Vector2Int s; if (!Sizes.TryGetValue(sz, out s)) continue;
                            GlassUiShots.Shot("fr_" + name + "_" + sz, s.x, s.y, cam, mm.transform);
                        }
                    }
                    catch (Exception e) { Debug.LogError("[FranchiseShots] " + name + ": " + e); }
                };
                Action<int, int> tab = (t, s) => ui.ShowTab(t, s);
                var ctl = FranchiseController.FromSession();

                reopen();
                snap("home", () => tab(FranchiseNav.Home, 0));
                snap("news", () => tab(FranchiseNav.News, 0));
                snap("cap", () => tab(FranchiseNav.Actions, FranchiseNav.Cap));
                snap("resign", () => tab(FranchiseNav.Actions, FranchiseNav.ReSign));
                snap("freeagency", () => tab(FranchiseNav.Actions, FranchiseNav.FreeAgency));
                snap("trades", () => tab(FranchiseNav.Actions, FranchiseNav.Trades));
                snap("staff", () => tab(FranchiseNav.Actions, FranchiseNav.Staff));
                snap("owner", () => tab(FranchiseNav.Owner, 0));
                snap("roster", () => tab(FranchiseNav.Team, FranchiseNav.Roster));
                snap("depth", () => tab(FranchiseNav.Team, FranchiseNav.Depth));
                snap("squad", () => tab(FranchiseNav.Team, FranchiseNav.Squad));
                snap("schedule", () => tab(FranchiseNav.League, FranchiseNav.Schedule));
                snap("standings", () => tab(FranchiseNav.League, FranchiseNav.Standings));
                snap("leaders", () => tab(FranchiseNav.League, FranchiseNav.Leaders));
                snap("sheet", () => { tab(FranchiseNav.Team, FranchiseNav.Roster); var rp = (RosterPage)ui.PageOf(FranchiseNav.Team, FranchiseNav.Roster); typeof(RosterPage).GetMethod("OpenActions", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rp, new object[] { CareerViews.RosterTable(ctl.Save, ctl.User, true)[0] }); });
                CloseModals(ui);
                snap("boxscore", () => { var r = ctl.SimUserGameNow(); BoxScoreView.Show(ui, ctl.Save, r, "Simulated game", "CONTINUE", null); });
                CloseModals(ui);
                ctl.Advance();

                // ---- the end of the season
                ctl.SimToOffseason();
                reopen();
                snap("home_awards", () => tab(FranchiseNav.Home, 0));
                snap("awards", () => tab(FranchiseNav.League, FranchiseNav.Awards));
                snap("records", () => tab(FranchiseNav.League, FranchiseNav.Records));
                snap("trophies", () => tab(FranchiseNav.League, FranchiseNav.Trophies));
                snap("news_final", () => tab(FranchiseNav.News, 0));

                // ---- re-sign window, scouting, the draft, rookies, free agency
                CareerFlow.AdvanceUntil(ctl.Save, CareerPhase.ReSign);
                reopen();
                snap("home_resign", () => tab(FranchiseNav.Home, 0));
                snap("resign_window", () => tab(FranchiseNav.Actions, FranchiseNav.ReSign));
                CareerFlow.AdvanceUntil(ctl.Save, CareerPhase.Scouting);
                reopen();
                snap("draft_scouting", () => ui.ShowOverlay(new DraftPage()));
                ui.CloseOverlay(false);
                CareerFlow.Advance(ctl.Save);
                CareerFlow.Advance(ctl.Save);   // CPU teams pick until the user is on the clock
                reopen();
                snap("draft_onclock", () => ui.ShowOverlay(new DraftPage()));
                ui.CloseOverlay(false);
                ctl.AutoDraft();
                reopen();
                snap("draft_rookies", () => ui.ShowOverlay(new DraftPage()));
                ui.CloseOverlay(false);
                CareerDraft.SignAllRookies(ctl.Save, ctl.Save.UserTeamId);
                ctl.Advance();
                for (int d = 0; d < 3; d++) ctl.Advance();
                reopen();
                snap("freeagency_bidding", () => tab(FranchiseNav.Actions, FranchiseNav.FreeAgency));
                snap("fa_offer", () => { var pool = FreeAgency.Pool(ctl.Save, false, PositionBucket.QB, 70); if (pool.Count > 0) FaOfferSheet.Show(ui, ctl.Save, pool[0], null); });
                CloseModals(ui);
                snap("home_fa", () => tab(FranchiseNav.Home, 0));
                Debug.Log("[FranchiseShots] done");
            }
            catch (Exception e) { Debug.LogError("[FranchiseShots] " + e); }
            finally { GlassUiShots.OutDir = keep; QualityTiers.ClearOverride(); CareerSession.Close(); UiTheme.Force(null); }
        }

        static void CloseModals(FranchiseUi ui)
        {
            foreach (var m in ui.GetComponentsInChildren<ModalView>(true)) UnityEngine.Object.DestroyImmediate(m.gameObject);
            foreach (var m in ui.GetComponentsInChildren<ActionSheet>(true)) UnityEngine.Object.DestroyImmediate(m.gameObject);
            foreach (var m in ui.GetComponentsInChildren<BoxScoreView>(true)) UnityEngine.Object.DestroyImmediate(m.gameObject);
            ActionSheet.AnyOpen = false;
        }
    }
}
