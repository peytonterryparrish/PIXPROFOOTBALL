using System.Collections.Generic;
using System.Linq;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.UI;
using PixFootball.UI.Glass;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.EditorTools
{
    /// <summary>v0.7 QA renders (Docs/QA/v070/ui_controls.md): the play-call sheet on every tab, offense and defense, the field widgets (route badges, user marker, whistle) and the HUD at the resolution set.</summary>
    public static partial class GlassUiShots
    {
        public const string V7Dir = @"D:\PIXFootball\Renders\v070_c\ui";

        static PlayCallModel MakeSheetModel(PlaySide side, bool special = false)
        {
            var book = DefaultPlaybooks.Create();
            var m = new PlayCallModel(book, side, true, special, true);
            var s = m.Situation; s.TeamName = "BUFFALO IRONHORNS"; s.Title = side == PlaySide.Offense ? "OFFENSE" : "DEFENSE"; s.DownText = "2ND & 7"; s.SpotText = "BALL ON BUF 33"; s.ClockText = "Q2 4:12"; s.Timeouts = 2; s.CoachStyle = "BALANCED";
            s.SetScore("MIA", 10, "BUF", 14);
            var plays = book.AllPlays.Where(p => p.Side == side).ToList();
            Play Cat(PlayCategory c, int skip = 0) { return plays.Where(p => p.Category == c).Skip(skip).FirstOrDefault() ?? plays[0]; }
            if (side == PlaySide.Offense)
                m.CoachPicks = new List<CoachPick> { new CoachPick { Play = Cat(PlayCategory.ShortPass), Reason = "3RD & MEDIUM: MOVE THE CHAINS", Score = 2f }, new CoachPick { Play = Cat(PlayCategory.InsideRun), Reason = "STAY AHEAD OF THE CHAINS", Score = 1.5f }, new CoachPick { Play = Cat(PlayCategory.PlayAction), Reason = "THEY ARE STACKING THE BOX", Score = 1f } };
            else
                m.CoachPicks = new List<CoachPick> { new CoachPick { Play = Cat(PlayCategory.ZoneCoverage), Reason = "THEY LIKE TO THROW ON 2ND AND LONG", Score = 2f }, new CoachPick { Play = Cat(PlayCategory.Blitz), Reason = "BRING PRESSURE", Score = 1.5f }, new CoachPick { Play = Cat(PlayCategory.ManCoverage), Reason = "TIGHT COVERAGE", Score = 1f } };
            m.RecentPlays = plays.Take(5).ToList(); m.FavoritePlays = plays.Skip(7).Take(4).ToList();
            if (special) m.SpecialPlays = book.PlaysFor(DefaultPlaybooks.PuntF).ToList();
            m.LastFormation = "";
            m.Open(CallTab.AskCoach);
            return m;
        }

        static void SheetShot(MatchHud hud, GameObject go, Camera cam, PlayCallModel m, string name, int w, int h)
        {
            hud.ShowPlayCall(m, 19f);
            Shot(name, w, h, cam, go.transform);
            hud.HidePlayCall();
        }

        /// <summary>The call sheet: every tab on offense and defense at 2560x1440, then the suggestions and the formation tab at every aspect.</summary>
        [MenuItem("PIX Football/Glass UI/Render call sheet")]
        public static void CallSheet()
        {
            BatchWatchdog.Arm(25);
            string prevOut = OutDir; OutDir = V7Dir;
            QualityTiers.Override(3, -1f);
            try
            {
                UiTheme.Force(UiThemeKind.Glass);
                var cam = OpenStadiumCam();
                var go = new GameObject("MatchHud");
                var hud = go.AddComponent<MatchHud>();
                hud.Build(null);
                InputDeviceTracker.Force(true);
                const int W = 2560, H = 1440;
                // offense, every tab
                var m = MakeSheetModel(PlaySide.Offense, true);
                SheetShot(hud, go, cam, m, "sheet_off_suggested_2560x1440", W, H);
                m.SetTab(CallTab.Formation); m.StepGroup(1); m.StepGroup(1); m.Navigate(1, 0);
                SheetShot(hud, go, cam, m, "sheet_off_formation_2560x1440", W, H);
                m.SetTab(CallTab.Concept); m.StepGroup(1); m.Navigate(1, 0);
                SheetShot(hud, go, cam, m, "sheet_off_concept_2560x1440", W, H);
                m.SetTab(CallTab.PlayType); m.StepGroup(2);
                SheetShot(hud, go, cam, m, "sheet_off_playtype_2560x1440", W, H);
                m.SetTab(CallTab.Personnel);
                m.ToggleFlip();
                SheetShot(hud, go, cam, m, "sheet_off_personnel_flipped_2560x1440", W, H);
                m.SetTab(CallTab.Formation); m.SetSearch("slant");
                SheetShot(hud, go, cam, m, "sheet_off_search_2560x1440", W, H);
                m.ClearSearch(); m.SetTab(CallTab.Favorites);
                SheetShot(hud, go, cam, m, "sheet_off_favorites_2560x1440", W, H);
                m.SetTab(CallTab.Special);
                SheetShot(hud, go, cam, m, "sheet_off_special_2560x1440", W, H);
                // defense
                var d = MakeSheetModel(PlaySide.Defense);
                SheetShot(hud, go, cam, d, "sheet_def_suggested_2560x1440", W, H);
                d.SetTab(CallTab.Formation); d.StepGroup(1);
                SheetShot(hud, go, cam, d, "sheet_def_formation_2560x1440", W, H);
                // aspects and resolutions
                foreach (var sz in new[] { new[] { 1920, 1080 }, new[] { 3440, 1440 }, new[] { 1920, 1200 }, new[] { 1440, 1080 }, new[] { 3840, 2160 }, new[] { 1366, 768 } })
                {
                    string tag = sz[0] + "x" + sz[1];
                    var a = MakeSheetModel(PlaySide.Offense, true);
                    SheetShot(hud, go, cam, a, "sheet_off_suggested_" + tag, sz[0], sz[1]);
                    a.SetTab(CallTab.Formation); a.StepGroup(1);
                    SheetShot(hud, go, cam, a, "sheet_off_formation_" + tag, sz[0], sz[1]);
                }
                Object.DestroyImmediate(go);
            }
            catch (System.Exception e) { Debug.LogError("[GlassUiShots] " + e); }
            finally { OutDir = prevOut; QualityTiers.ClearOverride(); }
        }

        /// <summary>The field widgets over the stadium: read badges, the marker over the controlled player, the whistle banner, plus the HUD bottom bar at the resolution set (cut-off check).</summary>
        [MenuItem("PIX Football/Glass UI/Render field widgets")]
        public static void FieldWidgets()
        {
            BatchWatchdog.Arm(20);
            string prevOut = OutDir; OutDir = V7Dir;
            QualityTiers.Override(3, -1f);
            try
            {
                UiTheme.Force(UiThemeKind.Glass);
                var cam = OpenStadiumCam();
                var go = new GameObject("MatchHud");
                var hud = go.AddComponent<MatchHud>();
                hud.Build(null);
                InputDeviceTracker.Force(true);
                var tr = typeof(MatchHud);
                foreach (var sz in new[] { new[] { 2560, 1440 }, new[] { 1920, 1080 }, new[] { 3440, 1440 }, new[] { 1920, 1200 }, new[] { 1440, 1080 }, new[] { 3840, 2160 } })
                {
                    string tag = sz[0] + "x" + sz[1];
                    PixSettings.Scorebug = ScorebugStyle.Bottom;
                    hud.SetScore(Snap());
                    hud.SetHint("[A][B][X][Y][LB][RB] THROW (HOLD = BULLET, TAP = LOB)   [RS] LEAD   [LT] ROUTES   [LS] SCRAMBLE");
                    var badges = new List<RouteOverlay.Badge>
                    {
                        new RouteOverlay.Badge { World = new Vector3(-6f, 0f, 3f), Read = 1, Text = "1", Primary = true },
                        new RouteOverlay.Badge { World = new Vector3(-1f, 0f, 5f), Read = 2, Text = "2" },
                        new RouteOverlay.Badge { World = new Vector3(4f, 0f, 6f), Read = 3, Text = "3", Hot = true },
                        new RouteOverlay.Badge { World = new Vector3(1.5f, 0f, -3.5f), Text = "RUN", Run = true, Primary = true },
                    };
                    hud.SetRouteBadges(badges, cam);
                    hud.SetUserMarker(true, new Vector3(0f, 2.4f, -2.2f), cam, "#12 QB");
                    // whistle (the coroutine does not run in edit mode: set the widget by hand)
                    tr.GetMethod("BuildWhistle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(hud, null);
                    var wt = (TMPro.TextMeshProUGUI)tr.GetField("_whistleText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(hud); wt.text = "TACKLED BY #54  -  2 YARD GAIN";
                    var wr = (RectTransform)tr.GetField("_whistle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(hud); wr.gameObject.SetActive(true);
                    ((CanvasGroup)tr.GetField("_whistleGroup", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(hud)).alpha = 1f;
                    Shot("field_widgets_" + tag, sz[0], sz[1], cam, go.transform);
                    hud.SetRouteBadges(null, cam); hud.SetUserMarker(false, Vector3.zero, cam, ""); wr.gameObject.SetActive(false);
                }
                Object.DestroyImmediate(go);
            }
            catch (System.Exception e) { Debug.LogError("[GlassUiShots] " + e); }
            finally { OutDir = prevOut; QualityTiers.ClearOverride(); }
        }

        public static void V7All() { BatchWatchdog.Arm(30); CallSheet(); FieldWidgets(); }
    }
}
