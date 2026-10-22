using System.Collections.Generic;
using System.IO;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Presentation;
using PixFootball.UI;
using PixFootball.UI.Glass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace PixFootball.EditorTools
{
    /// <summary>QA renders of the in-match glass UI over the lit stadium: scorebugs, banners, toss, final, pause, play call, penalty, pre-snap, TV cards, replay, free roam, kick meter.</summary>
    public static partial class GlassUiShots
    {
        /// <summary>The stadium scene (the same one the menu loads) with a broadcast-style camera and two lines of players.</summary>
        public static Camera OpenStadiumCam(bool players = true)
        {
            if (!File.Exists(GlassSceneBuilder.BackdropScenePath)) GlassSceneBuilder.BuildMenuBackdrop();
            EditorSceneManager.OpenScene(GlassSceneBuilder.BackdropScenePath, OpenSceneMode.Single);
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>(); go.AddComponent<HDAdditionalCameraData>();
            cam.fieldOfView = 24f; cam.allowHDR = true; cam.nearClipPlane = 0.2f; cam.farClipPlane = 1500f;
            go.transform.position = new Vector3(-46f, 16f, -26f); go.transform.LookAt(new Vector3(2f, 0.5f, 0f));
            if (players)
            {
                var root = new GameObject("Players").transform;
                for (int i = 0; i < 11; i++)
                {
                    float x = -5f + i * 1.0f;
                    var a = SimAgentView.Create(root, PlayerSize.Medium, TeamColors.Home, "O" + i); a.transform.position = new Vector3(x, 0f, -1.4f - (i == 5 ? 0.8f : 0f)); a.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                    var b = SimAgentView.Create(root, PlayerSize.Medium, TeamColors.FromHex("#C0172B", "#FFFFFF", "#FFC837"), "D" + i); b.transform.position = new Vector3(x, 0f, 1.4f); b.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                }
            }
            return cam;
        }

        static ScoreSnapshot Snap(string q = "2ND", string clock = "4:12", string dd = "3RD & 7", string spot = "BUF 33")
        {
            return new ScoreSnapshot
            {
                HomeAbbr = "BUF", AwayAbbr = "MIA", HomeCrest = "crest_buffalo_ironhorns", AwayCrest = "crest_miami_reefrunners", HomeColor = new Color(0.05f, 0.24f, 0.7f), AwayColor = new Color(0.98f, 0.8f, 0.1f),
                HomeScore = 14, AwayScore = 10, Quarter = q, Clock = clock, PlayClock = 18, DownDistance = dd, Spot = spot, PossessionHome = true, HomeTimeouts = 2, AwayTimeouts = 3
            };
        }

        static void BannerNow(MatchHud hud, string text, Color c, string sub)
        {
            var m = typeof(MatchHud).GetMethod("BannerRoutine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var e = (System.Collections.IEnumerator)m.Invoke(hud, new object[] { text, c, 99f, sub });
            e.MoveNext(); e.MoveNext();
            var banner = hud.transform.Find("MatchHud/Banner") as RectTransform;
            if (banner == null) return;
            banner.gameObject.SetActive(true);
            var cg = UiKit.GetOrAdd<CanvasGroup>(banner.gameObject); cg.alpha = 1f;
            banner.anchoredPosition = new Vector2(0f, -250f);
        }

        static FinalData FinalSample()
        {
            var d = new FinalData
            {
                HomeName = "Buffalo Ironhorns", AwayName = "Miami Reefrunners", HomeAbbr = "BUF", AwayAbbr = "MIA", HomeCrest = "crest_buffalo_ironhorns", AwayCrest = "crest_miami_reefrunners",
                HomeColor = new Color(0.05f, 0.24f, 0.7f), AwayColor = new Color(0.98f, 0.8f, 0.1f), HomeScore = 27, AwayScore = 24, Headline = "VICTORY", UserWon = true,
                HomeStats = new TeamStats { PassYards = 188, RushYards = 96, FirstDowns = 17, Turnovers = 1, Penalties = 5, PenaltyYards = 41, PassAtt = 25, PassComp = 17, Sacks = 3, ThirdDownAtt = 11, ThirdDownConv = 6 },
                AwayStats = new TeamStats { PassYards = 143, RushYards = 112, FirstDowns = 15, Turnovers = 2, Penalties = 7, PenaltyYards = 55, PassAtt = 30, PassComp = 18, Sacks = 1, ThirdDownAtt = 12, ThirdDownConv = 4 }
            };
            d.HomeByQuarter = new[] { 0, 7, 3, 7, 3, 7, 0, 0 }; d.AwayByQuarter = new[] { 0, 3, 7, 7, 7, 0, 0, 0 };
            return d;
        }

        static PlayCallModel MakeModel(PlaySide side, int confirms)
        {
            var m = new PlayCallModel(DefaultPlaybooks.Create(), side, true, true);
            var s = m.Situation; s.TeamName = "BUFFALO IRONHORNS"; s.Title = side == PlaySide.Offense ? "OFFENSE" : "DEFENSE"; s.DownText = "2ND & 7"; s.SpotText = "BUF 33"; s.ClockText = "Q2  4:12"; s.Timeouts = 2; s.CoachStyle = "BALANCED";
            s.SetScore("MIA", 10, "BUF", 14);
            m.Open();
            for (int i = 0; i < confirms; i++) m.Confirm();
            return m;
        }

        /// <summary>The HUD canvases and cards over the stadium.</summary>
        [MenuItem("PIX Football/Glass UI/Render HUD")]
        public static void Hud()
        {
            BatchWatchdog.Arm(20);
            try
            {
                UiTheme.Force(UiThemeKind.Glass);
                var cam = OpenStadiumCam();
                var go = new GameObject("MatchHud");
                var hud = go.AddComponent<MatchHud>();
                hud.Build(null);
                foreach (var sz in Sizes)
                {
                    string tag = sz[0] + "x" + sz[1];
                    PixSettings.Scorebug = ScorebugStyle.Bottom;
                    hud.SetScore(Snap());
                    InputDeviceTracker.Force(true);
                    hud.SetHint("[A][B][X][Y][LB][RB] THROW (HOLD = BULLET, TAP = LOB)   [RS] LEAD   [LS] SCRAMBLE");
                    Shot("hud_bottom_" + tag, sz[0], sz[1], cam, go.transform);
                    BannerNow(hud, "TOUCHDOWN", new Color(0.95f, 0.78f, 0.1f), "BUFFALO IRONHORNS  -  #12 J. DOE  -  34 YD PASS");
                    hud.ShowToast("TIMEOUT CALLED", 99f);
                    Shot("hud_banner_" + tag, sz[0], sz[1], cam, go.transform);
                    foreach (var nm in new[] { "Banner", "Toast" }) { var tr = go.transform.Find("MatchHud/" + nm); if (tr != null) tr.gameObject.SetActive(false); }
                    var snap = Snap(); snap.FlagText = "FLAG  -  MIA #77  -  OFFENSIVE HOLDING, 10 YARDS";
                    hud.SetScore(snap);
                    Shot("hud_flag_" + tag, sz[0], sz[1], cam, go.transform);
                    hud.SetScore(Snap());
                    PixSettings.Scorebug = ScorebugStyle.Top;
                    hud.SetScore(Snap("OT", "", "1ST & 10", "MIA 25"));
                    Shot("hud_top_" + tag, sz[0], sz[1], cam, go.transform);
                    PixSettings.Scorebug = ScorebugStyle.Bottom;
                    hud.SetScore(Snap());
                    // coin toss over the field
                    hud.SetTossBackdrop(true);
                    hud.ShowCoinTossCall("Buffalo Ironhorns", "Miami Reefrunners", null);
                    hud.SetTossCaption("Reefrunners choose tails.");
                    Shot("toss_" + tag, sz[0], sz[1], cam, go.transform);
                    hud.SetTossCaption("");
                    foreach (var nm in new[] { "CoinToss", "CoinTossResult" }) { var tr = go.transform.Find("MatchHud/" + nm); if (tr != null) Object.DestroyImmediate(tr.gameObject); }
                    var hb = go.transform.Find("MatchHud/HintBar") as RectTransform; if (hb != null) Debug.Log("[GlassUiShots] hintbar " + hb.rect.size + " children " + hb.childCount + " row " + (hb.childCount > 1 ? ((RectTransform)hb.GetChild(1)).rect.size.ToString() + " kids " + hb.GetChild(1).childCount : ""));
                    hud.HidePlayCall();
                    // final
                    hud.ShowFinal(FinalSample(), null);
                    Shot("final_" + tag, sz[0], sz[1], cam, go.transform);
                    var fr = go.transform.Find("MatchHud/Final"); if (fr != null) Object.DestroyImmediate(fr.gameObject);
                    // pause
                    hud.SetPaused(true);
                    Shot("pause_" + tag, sz[0], sz[1], cam, go.transform);
                    var show = typeof(MatchHud).GetMethod("ShowPauseSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    show.Invoke(hud, null);
                    Shot("pause_sound_" + tag, sz[0], sz[1], cam, go.transform);
                    hud.SetPaused(false);
                    // play call: formation, set, play
                    for (int stage = 0; stage < 3; stage++)
                    {
                        var model = MakeModel(PlaySide.Offense, stage);
                        hud.ShowPlayCall(model, 18f);
                        Shot("playcall" + stage + "_" + tag, sz[0], sz[1], cam, go.transform);
                        hud.HidePlayCall();
                    }
                    // penalty
                    var pd = new PenaltyPanelData { Headline = "Offensive holding", Referee = "Offensive holding, number 77, offense (Reefrunners). 10-yard penalty, repeat 3rd down.", Team = "MIAMI REEFRUNNERS", Player = "#77", Yards = "10 YARDS - REPLAY DOWN", AcceptTitle = "ACCEPT", AcceptText = "3RD & 17 at the MIA 23\nthe play is erased", DeclineTitle = "DECLINE", DeclineText = "4TH & 3 at the MIA 37\nthe 14-yard gain stands", Selected = 0, Seconds = 14f, Recommended = true };
                    hud.ShowPenalty(pd, null);
                    Shot("penalty_" + tag, sz[0], sz[1], cam, go.transform);
                    hud.HidePenalty();
                }
                Object.DestroyImmediate(go);
            }
            catch (System.Exception e) { Debug.LogError("[GlassUiShots] " + e); }
        }
    }
}

namespace PixFootball.EditorTools
{
    public static partial class GlassUiShots
    {
        static void AgeCards(TvOverlays ov)
        {
            var cardsF = typeof(TvOverlays).GetField("_cards", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var list = (System.Collections.IList)cardsF.GetValue(ov);
            foreach (var c in list)
            {
                var t = c.GetType();
                var group = (CanvasGroup)t.GetField("Group").GetValue(c); var root = (RectTransform)t.GetField("Root").GetValue(c); var rest = (Vector2)t.GetField("Rest").GetValue(c);
                group.alpha = 1f; root.anchoredPosition = rest;
            }
        }

        /// <summary>TV cards, replay HUD, free roam HUD, kick meter, pre-snap panel, loading screen, top-left scorebug.</summary>
        [MenuItem("PIX Football/Glass UI/Render extras")]
        public static void Extras()
        {
            BatchWatchdog.Arm(20);
            try
            {
                UiTheme.Force(UiThemeKind.Glass);
                var cam = OpenStadiumCam();
                var sz = Sizes[0]; string tag = sz[0] + "x" + sz[1];
                // TV cards
                var root = new GameObject("Overlays");
                var ov = TvOverlays.Create(root.transform, null);
                TeamVis away = new TeamVis("MIA", "Miami Reefrunners", "crest_miami_reefrunners", new Color(0.98f, 0.8f, 0.1f)), home = new TeamVis("BUF", "Buffalo Ironhorns", "crest_buffalo_ironhorns", new Color(0.05f, 0.24f, 0.7f));
                var rows = new List<CompareRow>();
                string[] labels = { "TOTAL YARDS", "PASSING YARDS", "RUSHING YARDS", "FIRST DOWNS", "3RD DOWN", "TURNOVERS", "PENALTIES", "TIME OF POSS." };
                for (int i = 0; i < labels.Length; i++) rows.Add(new CompareRow { Label = labels[i], Away = (100 + i * 7).ToString(), Home = (80 + i * 9).ToString() });
                ov.ShowComparison(rows, away, home, "TEAM STATS");
                ov.ShowThird("t", 5, home, "ROBINSON RETURNS THE INTERCEPTION FOR A TOUCHDOWN", "BIG PLAY   THE LONGEST RETURN OF THE SEASON", 99f);
                ov.ShowNextPlay("2ND & 11", "BUF 29", "1:57");
                ov.ShowTicker("PEARSON RUNS FOR 4 YARDS", home, "1:57", 99f);
                AgeCards(ov);
                Shot("overlays_cards_" + tag, sz[0], sz[1], cam, root.transform);
                ov.ClearAll();
                ov.ShowScoreUpdate(away, 10, home, 14, "Touchdown pass to 82", "4:12", 99f, 1);
                ov.ShowStadium("Buffalo Park", "Buffalo", 24, "Snow, night", 99f, "TONIGHT'S GAME");
                AgeCards(ov);
                Shot("overlays_score_stadium_" + tag, sz[0], sz[1], cam, root.transform);
                ov.ClearAll();
                ov.ShowMatchup(away, "OVR 78", new List<string> { "QB   D. ALDER   65", "RB   U. RICE   86", "DE   J. LANG   88" }, home, "OVR 81", new List<string> { "QB   J. SMITH   78", "WR   S. YANG   79", "CB   R. KING   79" }, "BUFFALO PARK  -  BUFFALO", 99f, "PRO FOOTBALL  -  PRIMETIME");
                AgeCards(ov);
                Shot("overlays_matchup_" + tag, sz[0], sz[1], cam, root.transform);
                Object.DestroyImmediate(root);
                // replay
                var rgo = new GameObject("ReplayRoot");
                var rh = ReplayHud.Create(rgo.transform);
                rh.Show(true, "TOUCHDOWN", "Q2  4:12  -  3RD & 7");
                rh.SetTransport(0.42f, 0.7f, 3.4f, 8.1f, "SLOW MOTION  0.25x", "ORBIT FREE CAMERA", "TARGET  #12 QB J. DOE", true);
                Shot("replay_transport_" + tag, sz[0], sz[1], cam, rgo.transform);
                Object.DestroyImmediate(rgo);
                // free roam HUD and kick meter
                var fgo = new GameObject("FreeRoamHud"); var fh = fgo.AddComponent<FreeRoamHud>(); fh.SendMessage("Start");
                Shot("freeroam_" + tag, sz[0], sz[1], cam, fgo.transform);
                Object.DestroyImmediate(fgo);
                // match HUD: top-left scorebug, kick meter, pre-snap
                var go = new GameObject("MatchHud"); var hud = go.AddComponent<MatchHud>(); hud.Build(null);
                PixSettings.Scorebug = ScorebugStyle.Top;
                typeof(MatchHud).GetMethod("ApplyScorebugLayout", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(hud, null);
                hud.SetScore(Snap("OT", "", "1ST & 10", "MIA 25"));
                var snap = Snap(); snap.FlagText = "FLAG  -  MIA #77  -  OFFENSIVE HOLDING, 10 YARDS"; hud.SetScore(snap);
                hud.SetKickMeter(true, 0.62f, "GOOD", 0.4f, "SLIGHTLY LEFT"); hud.SetWind(70f, 9f);
                Shot("hud_topleft_kick_" + tag, sz[0], sz[1], cam, go.transform);
                PixSettings.Scorebug = ScorebugStyle.Bottom;
                typeof(MatchHud).GetMethod("ApplyScorebugLayout", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(hud, null);
                hud.SetKickMeter(false, 0.5f, "", 0.5f, ""); hud.SetWind(0f, 0f);
                hud.SetScore(Snap());
                var pre = new PreSnapHud { Mode = "OFFENSE", PlayName = "INSIDE ZONE RIGHT", Formation = "SINGLEBACK", OffenseSide = true, Seconds = 18 };
                pre.Hints.Add(new HintPair("glyph_a", "SNAP")); pre.Hints.Add(new HintPair("glyph_x", "AUDIBLE")); pre.Hints.Add(new HintPair("glyph_y", "MOTION")); pre.Hints.Add(new HintPair("glyph_lb", "SLIDE"));
                pre.Adjustments.Add("QB SLIDE PROTECT RIGHT"); pre.Adjustments.Add("HB BLOCK");
                pre.MenuTitle = "AUDIBLE";
                pre.Menu.Add(new MenuRow { Button = Btn.X, Label = "INSIDE ZONE LEFT", Tag = "RUN", Selected = true }); pre.Menu.Add(new MenuRow { Button = Btn.Y, Label = "SLANTS", Tag = "PASS" }); pre.Menu.Add(new MenuRow { Button = Btn.B, Label = "QB SNEAK", Tag = "RUN" });
                hud.SetPreSnap(pre);
                Shot("presnap_" + tag, sz[0], sz[1], cam, go.transform);
                Object.DestroyImmediate(go);
                // loading screen
                var lg = GlassLoading.CreatePreview("Match_Pro");
                Shot("loading_" + tag, sz[0], sz[1], null, lg.transform);
                Object.DestroyImmediate(lg);
            }
            catch (System.Exception e) { Debug.LogError("[GlassUiShots] " + e); }
        }
    }
}
