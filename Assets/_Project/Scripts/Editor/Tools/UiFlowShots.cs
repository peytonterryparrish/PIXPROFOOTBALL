using System.Collections.Generic;
using System.IO;
using PixFootball.Gameplay;
using PixFootball.Presentation;
using PixFootball.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// UI / flow QA renders (Docs/QA/ui_flow.md): the menu screens, Team Select, the settings pages, dialogs, the match HUD (scorebug, hints, banner, final score, pause), TV overlay cards,
    /// the replay HUD and the free-roam HUD at several aspect ratios. Edit-mode renders of the code-built UI onto a plain backdrop. Output: Logs/uiflow/*.png.
    /// Run: Unity -batchmode -projectPath . -executeMethod PixFootball.EditorTools.UiFlowShots.All -quit  (no -nographics).
    /// </summary>
    public static class UiFlowShots
    {
        const string Out = "Logs/uiflow";
        static Camera _cam;

        static Camera MakeCamera(Color bg)
        {
            var go = new GameObject("UiShotCam");
            var cam = go.AddComponent<Camera>(); go.AddComponent<HDAdditionalCameraData>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = bg; cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f; cam.allowHDR = false;
            go.transform.position = new Vector3(0, 0, -10);
            return cam;
        }

        static void Bind(Transform root, Camera cam)
        {
            foreach (var cv in root.GetComponentsInChildren<Canvas>(true))
                if (cv.isRootCanvas) { cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = cam; cv.planeDistance = 1f; }
        }

        static void Save(string name, int w, int h, params Transform[] roots)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _cam.targetTexture = rt;
            foreach (var r in roots) Bind(r, _cam);
            Canvas.ForceUpdateCanvases();
            foreach (var s in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None)) s.SendMessage("Handle", SendMessageOptions.DontRequireReceiver);
            foreach (var f in Object.FindObjectsByType<FitScale>(FindObjectsInactive.Include, FindObjectsSortMode.None)) f.Apply();
            Canvas.ForceUpdateCanvases();
            // layout groups / content-size fitters settle over a few frames in play; settle them now, innermost first
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var r in roots)
                {
                    var all = r.GetComponentsInChildren<RectTransform>(false);
                    for (int i = all.Length - 1; i >= 0; i--) if (all[i].GetComponent<LayoutGroup>() != null || all[i].GetComponent<ContentSizeFitter>() != null) LayoutRebuilder.ForceRebuildLayoutImmediate(all[i]);
                }
                Canvas.ForceUpdateCanvases();
            }
            for (int i = 0; i < 3; i++) _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; _cam.targetTexture = null; rt.Release();
            Directory.CreateDirectory(Out);
            File.WriteAllBytes(Out + "/" + name + ".png", tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[UiFlowShots] " + name + " " + w + "x" + h);
        }

        static readonly int[][] Sizes = { new[] { 1920, 1080 }, new[] { 1440, 1080 }, new[] { 2560, 1080 } };
        static string Tag(int[] s) { return s[0] == 1920 ? "16x9" : s[0] == 1440 ? "4x3" : "21x9"; }

        [MenuItem("PIX Football/QA/UI flow shots")]
        public static void All()
        {
            try { Menus(); Hud(); Overlays(); Misc(); }
            catch (System.Exception e) { Debug.LogError("[UiFlowShots] " + e); }
        }

        // ============================================================================================ menu, settings, team select
        public static void Menus()
        {
            EditorSceneManager.OpenScene(SceneBuilder.MenuScenePath);
            var mm = Object.FindFirstObjectByType<MainMenuController>();
            mm.SendMessage("Awake");
            _cam = Camera.main != null ? Camera.main : MakeCamera(new Color(0.05f, 0.07f, 0.1f));
            var canvasRoot = mm.transform.Find("MenuCanvas");
            var menuRoot = mm.transform.Find("MenuCanvas/MainMenu");
            var settings = mm.GetComponentInChildren<SettingsPanel>(true);
            var team = mm.GetComponentInChildren<TeamSelectPanel>(true);
            foreach (var sz in Sizes)
            {
                string tag = Tag(sz);
                menuRoot.gameObject.SetActive(true); settings.gameObject.SetActive(false); team.gameObject.SetActive(false);
                Save("menu_" + tag, sz[0], sz[1], mm.transform);
                menuRoot.gameObject.SetActive(false);
                team.gameObject.SetActive(true);
                Save("teamselect_" + tag, sz[0], sz[1], mm.transform);
                team.gameObject.SetActive(false);
                settings.gameObject.SetActive(true);
                if (sz[0] == 1920) for (int p = 0; p < 5; p++) { settings.SetPage(p); Save("settings_page" + p, sz[0], sz[1], mm.transform); }
                else { settings.SetPage(0); Save("settings_" + tag, sz[0], sz[1], mm.transform); }
                settings.gameObject.SetActive(false);
            }
            // a confirm dialog over the menu
            menuRoot.gameObject.SetActive(true);
            var dlg = UiDialog.Show(canvasRoot, "Keep these display settings?", "If the picture is unreadable, do nothing: the previous mode comes back by itself.", "KEEP", "REVERT", null, null, 15f);
            Save("dialog_display", 1920, 1080, mm.transform);
            Object.DestroyImmediate(dlg.gameObject);
        }

        // ============================================================================================ the match HUD
        static ScoreSnapshot Snap(string q = "2ND", string clock = "4:12", string dd = "3RD & 7", string spot = "BUF 33")
        {
            return new ScoreSnapshot
            {
                HomeAbbr = "BUF", AwayAbbr = "MIA", HomeCrest = "", AwayCrest = "", HomeColor = new Color(0.05f, 0.24f, 0.7f), AwayColor = new Color(0.98f, 0.8f, 0.1f),
                HomeScore = 14, AwayScore = 10, Quarter = q, Clock = clock, PlayClock = 18, DownDistance = dd, Spot = spot, PossessionHome = true, HomeTimeouts = 2, AwayTimeouts = 3
            };
        }

        public static void Hud()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _cam = MakeCamera(new Color(0.16f, 0.36f, 0.16f));
            var go = new GameObject("MatchHud");
            var hud = go.AddComponent<MatchHud>();
            hud.Build(null);
            foreach (var sz in Sizes)
            {
                string tag = Tag(sz);
                hud.SetScore(Snap());
                InputDeviceTracker.Force(true);
                hud.SetHint("[A][B][X][Y][LB][RB] THROW (TAP = BULLET, HOLD = LOB)   [RS] LEAD   [LS] SCRAMBLE");
                Save("hud_throw_hint_pad_" + tag, sz[0], sz[1], go.transform);
                if (sz[0] == 1920)
                {
                    InputDeviceTracker.Force(false);
                    hud.SetHint("[A][B][X][Y][LB][RB] THROW (TAP = BULLET, HOLD = LOB)   [RS] LEAD   [LS] SCRAMBLE");
                    Save("hud_throw_hint_keyboard", sz[0], sz[1], go.transform);
                    InputDeviceTracker.Force(true);
                    hud.SetHint("[RT] SPRINT   [A] HURDLE   [B] SPIN   [X] DIVE   [Y] TRUCK   [LB][RB] STIFF-ARM   [RS] JUKE   [LT] PROTECT");
                    Save("hud_carry_hint", sz[0], sz[1], go.transform);
                    hud.SetHint("[LT] TIMEOUT   [VIEW] CAMERA");
                    BannerNow(hud, "BUFFALO IRONHORNS TIMEOUT", new Color(0.18f, 0.35f, 0.74f), "2 REMAINING");
                    Save("hud_banner_long_name", sz[0], sz[1], go.transform);
                    BannerNow(hud, "PENALTY ACCEPTED", new Color(1f, 0.9f, 0f), "Offensive holding, number 77, defense (Reefrunners). 10-yard penalty, repeat 3rd down.");
                    Save("hud_banner_long_sub", sz[0], sz[1], go.transform);
                    PixSettings.Scorebug = ScorebugStyle.Top;
                    hud.SetScore(Snap("OT", "", "1ST & 10", "MIA 25"));
                    Save("hud_scorebug_top_ot", sz[0], sz[1], go.transform);
                    PixSettings.Scorebug = ScorebugStyle.Bottom;
                    hud.SetScore(Snap("2OT", "", "1ST & 10", "MIA 25"));
                    Save("hud_scorebug_bottom_ot", sz[0], sz[1], go.transform);
                }
                var d = new FinalData
                {
                    HomeName = "Buffalo Ironhorns", AwayName = "Miami Reefrunners", HomeAbbr = "BUF", AwayAbbr = "MIA", HomeColor = new Color(0.05f, 0.24f, 0.7f), AwayColor = new Color(0.98f, 0.8f, 0.1f),
                    HomeScore = 27, AwayScore = 24, Headline = "VICTORY", UserWon = true,
                    HomeStats = new TeamStats { PassYards = 188, RushYards = 96, FirstDowns = 17, Turnovers = 1, Penalties = 5, PenaltyYards = 41, PassAtt = 25, PassComp = 17, Sacks = 3, ThirdDownAtt = 11, ThirdDownConv = 6 },
                    AwayStats = new TeamStats { PassYards = 143, RushYards = 112, FirstDowns = 15, Turnovers = 2, Penalties = 7, PenaltyYards = 55, PassAtt = 30, PassComp = 18, Sacks = 1, ThirdDownAtt = 12, ThirdDownConv = 4 }
                };
                d.HomeByQuarter = new[] { 0, 7, 3, 7, 3, 7, 0, 0 }; d.AwayByQuarter = new[] { 0, 3, 7, 7, 7, 0, 0, 0 };
                hud.ShowFinal(d, null);
                Save("hud_final_" + tag, sz[0], sz[1], go.transform);
                var fr = go.transform.Find("MatchHud/Final"); if (fr != null) Object.DestroyImmediate(fr.gameObject);
                hud.SetPaused(true);
                Save("hud_pause_" + tag, sz[0], sz[1], go.transform);
                hud.SetPaused(false);
            }
            // pause page 2 (sound and controls)
            hud.SetPaused(true);
            var show = typeof(MatchHud).GetMethod("ShowPauseSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            show.Invoke(hud, null);
            Save("hud_pause_sound", 1920, 1080, go.transform);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(_cam.gameObject);
        }

        static void BannerNow(MatchHud hud, string text, Color c, string sub)
        {
            var m = typeof(MatchHud).GetMethod("BannerRoutine", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var e = (System.Collections.IEnumerator)m.Invoke(hud, new object[] { text, c, 99f, sub });
            e.MoveNext(); e.MoveNext();   // sets text and colours, adds the canvas group
            RunBanner(hud);
        }

        /// <summary>Banner coroutines do not run in edit mode: put the banner at rest by hand.</summary>
        static void RunBanner(MatchHud hud)
        {
            var banner = hud.transform.Find("MatchHud/Banner") as RectTransform;
            if (banner == null) return;
            banner.gameObject.SetActive(true);
            var cg = banner.GetComponent<CanvasGroup>() ?? banner.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 1f;
            banner.anchoredPosition = new Vector2(0f, -250f);
        }

        // ============================================================================================ TV overlay cards + replay + free roam HUD
        public static void Overlays()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _cam = MakeCamera(new Color(0.16f, 0.36f, 0.16f));
            var root = new GameObject("Overlays");
            var ov = TvOverlays.Create(root.transform, null);
            TeamVis away = new TeamVis("MIA", "Miami Reefrunners", "", new Color(0.98f, 0.8f, 0.1f)), home = new TeamVis("BUF", "Buffalo Ironhorns", "", new Color(0.05f, 0.24f, 0.7f));
            var rows = new List<CompareRow>();
            for (int i = 0; i < 8; i++) rows.Add(new CompareRow { Label = "STAT ROW " + (i + 1), Away = (100 + i * 7).ToString(), Home = (80 + i * 9).ToString() });
            ov.ShowComparison(rows, away, home, "TEAM STATS");
            ov.ShowThird("t", 5, home, "ROBINSON RETURNS THE INTERCEPTION FOR A TOUCHDOWN IN THE FOURTH QUARTER", "BIG PLAY   THE LONGEST RETURN OF THE SEASON FOR THE VISITING TEAM", 99f);
            ov.ShowNextPlay("2ND & 11", "BUF 29", "1:57");
            ov.ShowTicker("PEARSON RUNS FOR 4 YARDS", home, "1:57", 99f);
            Age(ov);
            foreach (var sz in Sizes) Save("overlays_cards_" + Tag(sz), sz[0], sz[1], root.transform);
            ov.ClearAll();
            ov.ShowScoreUpdate(away, 10, home, 14, "Touchdown pass to 82", "4:12", 99f, 1);
            ov.ShowStadium("Buffalo Park", "Buffalo", 24, "Snow, night", 99f, "TONIGHT'S GAME");
            Age(ov);
            Save("overlays_score_stadium", 1920, 1080, root.transform);
            ov.ClearAll();
            ov.ShowMatchup(away, "OVR 78", new List<string> { "QB   D. ALDER   65", "RB   U. RICE   86", "DE   J. LANG   88" }, home, "OVR 81", new List<string> { "QB   J. SMITH   78", "WR   S. YANG   79", "CB   R. KING   79" }, "BUFFALO PARK  -  BUFFALO", 99f, "PRO FOOTBALL  -  PRIMETIME");
            Age(ov);
            Save("overlays_matchup", 1920, 1080, root.transform);
            Object.DestroyImmediate(root);
            // replay transport
            var rgo = new GameObject("ReplayRoot");
            var rh = ReplayHud.Create(rgo.transform);
            rh.Show(true, "TOUCHDOWN", "Q2  4:12  -  3RD & 7");
            rh.SetTransport(0.42f, 0.7f, 3.4f, 8.1f, "SLOW MOTION  0.25x", "ORBIT FREE CAMERA LONG LABEL", "TARGET  #12 QB J. DOE-WILLIAMSON", true);
            foreach (var sz in Sizes) Save("replay_transport_" + Tag(sz), sz[0], sz[1], rgo.transform);
            rh.Show(false, "HALFTIME HIGHLIGHTS 1 OF 4", "Q2", true);
            Save("replay_reel_tag", 1920, 1080, rgo.transform);
            Object.DestroyImmediate(rgo);
            Object.DestroyImmediate(_cam.gameObject);
        }

        /// <summary>Overlay cards animate in over time: put every card at rest.</summary>
        static void Age(TvOverlays ov)
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

        public static void Misc()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _cam = MakeCamera(new Color(0.16f, 0.36f, 0.16f));
            var go = new GameObject("FreeRoamHud");
            var hud = go.AddComponent<FreeRoamHud>();
            hud.SendMessage("Start");
            Save("freeroam_hud", 1920, 1080, go.transform);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(_cam.gameObject);
        }
    }
}
