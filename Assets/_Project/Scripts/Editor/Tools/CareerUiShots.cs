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
using UnityEngine.UI;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.8 career UI renders (edit mode, same composite as the glass UI shots): the career start screens and every franchise tab / sub page at 2560x1440 (the screens of the filter in <c>-pixShots</c>, default all).
    /// Output: D:\PIXFootball\Renders\v080_career\. Run: Tools/unity.ps1 -Method PixFootball.EditorTools.CareerUiShots.Render -Log career_shots.log   (no -nographics).
    /// </summary>
    public static class CareerUiShots
    {
        public const string OutDir = @"D:\PIXFootball\Renders\v080_career";

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
        static bool Want(string n) { return _only == null || _only.Contains(n.Split('_')[0]) || _only.Contains(n); }

        /// <summary>A career advanced to week 5 of the regular season (some games simulated so the screens have standings, stats and news).</summary>
        public static CareerSave MakeCareer(int seed = 11, int team = 5, int weeks = 4)
        {
            var o = new CareerOptions { Seed = seed, UserTeamId = team };
            var defs = MatchData.Defs; if (defs != null && defs.Count == 32) o.Teams = defs;
            var s = CareerFlow.NewCareer(o);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            for (int i = 0; i < weeks; i++) CareerFlow.Advance(s);
            return s;
        }

        [MenuItem("PIX Football/Career/Render career screens")]
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
                UiTheme.Force(UiThemeKind.Glass);
                CareerSession.DirOverride = Path.Combine(Path.GetTempPath(), "pix_career_shots");
                if (Directory.Exists(CareerSession.DirOverride)) Directory.Delete(CareerSession.DirOverride, true);
                var cam = GlassUiShots.OpenMenuStage();
                var mm = UnityEngine.Object.FindFirstObjectByType<MainMenuController>();
                mm.SendMessage("Awake");
                const int W = 2560, H = 1440;

                // ---- hub with the CAREER tile, start screens
                var save = MakeCareer();
                var prefs = new CareerPrefs { Difficulty = 1, QuarterMinutes = 5 };
                CareerSession.Open(save, prefs, 1);
                CareerSession.SaveNow();
                var start = Field<CareerStartPanel>(mm, "_careerStart");
                var hub = Field<MainMenuHub>(mm, "_hub");
                var menuRoot = mm.transform.Find("MenuCanvas/MainMenu");
                if (Want("hub")) { mm.SendMessage("GlassSection", "MAIN MENU"); GlassUiShots.Shot("hub_career", W, H, cam, mm.transform); }
                menuRoot.gameObject.SetActive(false); start.gameObject.SetActive(true);
                if (Want("start")) { start.Show(0); GlassUiShots.Shot("start_landing", W, H, cam, mm.transform); start.Show(1); GlassUiShots.Shot("start_new", W, H, cam, mm.transform); start.Show(2); GlassUiShots.Shot("start_load", W, H, cam, mm.transform); }
                start.gameObject.SetActive(false);

                // ---- the franchise, one calendar stage after the other (the same save advances)
                var open = typeof(MainMenuController).GetMethod("OpenFranchise", BindingFlags.NonPublic | BindingFlags.Instance);
                FranchiseUi ui = null;
                Action reopen = () => { open.Invoke(mm, new object[] { FranchiseController.FromSession() }); ui = Field<FranchiseUi>(mm, "_franchise"); };
                Action<string, Action> snap = (name, prep) =>
                {
                    if (!Want(name)) return;
                    try
                    {
                        if (prep != null) prep();
                        FocusLook(ui); PlayerCardPanel.RenderAll();
                        GlassUiShots.Shot(name, W, H, cam, mm.transform);
                    }
                    catch (Exception e) { Debug.LogError("[CareerUiShots] " + name + ": " + e); }
                };
                Action<int, int> tab = (t, s) => ui.ShowTab(t, s);
                var ctl = FranchiseController.FromSession();

                reopen();
                snap("home", () => tab(0, 0));
                snap("roster", () => tab(1, 0));
                snap("depth", () => tab(1, 1));
                snap("squad", () => tab(1, 2));
                snap("schedule", () => tab(2, 0));
                snap("standings", () => tab(2, 1));
                snap("leaders", () => tab(2, 2));
                snap("trades", () => tab(3, 3));
                snap("cap", () => tab(3, 0));
                snap("staff", () => tab(3, 4));
                snap("owner", () => tab(3, 5));
                snap("news", () => tab(2, 6));
                snap("freeagency_street", () => tab(3, 2));
                snap("resign_inseason", () => tab(3, 1));
                snap("sheet_roster", () => { tab(1, 0); var rp = (RosterPage)ui.PageOf(1, 0); typeof(RosterPage).GetMethod("OpenActions", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rp, new object[] { CareerViews.RosterTable(ctl.Save, ctl.User, true)[0] }); });
                CloseModals(ui);
                snap("boxscore", () => { var r = ctl.SimUserGameNow(); BoxScoreView.Show(ui, ctl.Save, r, "Simulated game", "CONTINUE", null); });
                CloseModals(ui);
                ctl.Advance();

                // ---- the end of the season
                ctl.SimToOffseason();
                reopen();
                snap("home_awards", () => tab(0, 0));
                snap("awards", () => tab(2, 3));
                snap("records", () => tab(2, 4));
                snap("trophies", () => tab(2, 5));
                snap("leaders_final", () => tab(2, 2));
                snap("standings_final", () => tab(2, 1));
                snap("schedule_final", () => tab(2, 0));

                // ---- re-sign window, scouting, the draft, rookies, free agency
                CareerFlow.AdvanceUntil(ctl.Save, CareerPhase.ReSign);
                reopen();
                snap("home_resign", () => tab(0, 0));
                snap("resign", () => tab(3, 1));
                snap("cap_resign", () => tab(3, 0));
                CareerFlow.AdvanceUntil(ctl.Save, CareerPhase.Scouting);
                reopen();
                snap("draft_scouting", () => ui.ShowOverlay(new DraftPage()));
                ui.CloseOverlay(false);
                CareerFlow.Advance(ctl.Save);   // the draft starts: CPU teams pick until the user is on the clock
                reopen();
                snap("draft_clock", () => ui.ShowOverlay(new DraftPage()));
                ui.CloseOverlay(false);
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
                snap("freeagency", () => tab(3, 2));
                snap("fa_offer", () => { var fp = ui.PageOf(3, 2); var pool = FreeAgency.Pool(ctl.Save, false, PositionBucket.QB, 70); if (pool.Count > 0) FaOfferSheet.Show(ui, ctl.Save, pool[0], null); });
                CloseModals(ui);
                snap("home_fa", () => tab(0, 0));
                snap("staff_camp", () => tab(3, 4));
                Debug.Log("[CareerUiShots] done");
            }
            catch (Exception e) { Debug.LogError("[CareerUiShots] " + e); }
            finally { GlassUiShots.OutDir = keep; QualityTiers.ClearOverride(); CareerSession.Close(); }
        }

        static void CloseModals(FranchiseUi ui)
        {
            foreach (var m in ui.GetComponentsInChildren<ModalView>(true)) UnityEngine.Object.DestroyImmediate(m.gameObject);
            foreach (var m in ui.GetComponentsInChildren<ActionSheet>(true)) UnityEngine.Object.DestroyImmediate(m.gameObject);
            foreach (var m in ui.GetComponentsInChildren<BoxScoreView>(true)) UnityEngine.Object.DestroyImmediate(m.gameObject);
            ActionSheet.AnyOpen = false;
        }

        /// <summary>Edit mode runs no focus animation: show the tables as focused (orange cursor) like they are when the pad is on them.</summary>
        static void FocusLook(FranchiseUi ui)
        {
            foreach (var t in ui.GetComponentsInChildren<GlassTable>(false)) t.ShowFocused(true);
        }
    }
}
