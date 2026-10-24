using System;
using System.Collections;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CoreTeam = PixFootball.Core.Team;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.Presentation
{
    public sealed partial class BroadcastDirector
    {
        Canvas _showCanvas; RectTransform _showRoot;
        float _showClock;
        bool _skipShow, _showStageHidden;
        public string CurrentShow { get; private set; } = "";
        public string CurrentPage { get; private set; } = "";
        public event Action<string> PageShown;

        MatchInputFrame PollInput()
        {
            return MatchInput.Instance.Poll(Time.unscaledDeltaTime);
        }

        void EnsureShowCanvas()
        {
            if (_showCanvas != null) return;
            _showCanvas = UiKit.CreateCanvas("ShowCanvas", 48, transform);
            _showRoot = (RectTransform)_showCanvas.transform;
            _showCanvas.GetComponent<GraphicRaycaster>().enabled = true;
        }

        void BeginShow(string name)
        {
            EnsureShowCanvas();
            _showActive = true; CurrentShow = name; _skipShow = false; _showClock = 0f;
            AbortShots(); _ov.ClearAll();
            _dir.Ui?.SetReplayMode(true);
            if (_dir.Stage != null) { _dir.Stage.gameObject.SetActive(false); _showStageHidden = true; }
            if (_dir.Officials != null) _dir.Officials.SetCrewVisible(false);
            if (_dir.Markers != null) _dir.Markers.HideAll();
            _cam.Begin();
            _ov.Enabled = false;
            _dir.Ui?.SetHudVisible(false);
        }

        void EndShow()
        {
            if (_showRoot != null) for (int i = _showRoot.childCount - 1; i >= 0; i--) Destroy(_showRoot.GetChild(i).gameObject);
            _cam.End();
            if (_showStageHidden && _dir.Stage != null) _dir.Stage.gameObject.SetActive(true);
            _showStageHidden = false;
            if (_dir.Officials != null) _dir.Officials.SetCrewVisible(true);
            _dir.Ui?.SetHudVisible(true);
            _dir.Ui?.SetReplayMode(false);
            _ov.Enabled = true;
            _showActive = false; CurrentShow = ""; CurrentPage = "";
        }

        void ShowCamera()
        {
            _showClock += Time.unscaledDeltaTime;
            var c = new ShotContext { Variant = 0 };
            var pose = ShotLibrary.Evaluate(ShotKind.StadiumWide, Mathf.PingPong(_showClock / 26f, 1f), c);
            _cam.Set(pose.Pos, pose.Look, pose.Fov, -1f);
        }

        // ============================================================================================ page runner
        struct PageDef { public string Name; public Func<RectTransform> Build; public float Secs; public PageDef(string name, Func<RectTransform> build, float secs) { Name = name; Build = build; Secs = secs; } }
        bool _pageBack;

        /// <summary>Runs the pages in order: A goes on, B steps back one page, Menu skips the rest of the show (QA-876).</summary>
        IEnumerator RunPages(params PageDef[] pages)
        {
            int i = 0;
            while (i < pages.Length && !_skipShow)
            {
                _pageBack = false;
                yield return RunPage(pages[i].Name, pages[i].Build, pages[i].Secs);
                if (_skipShow) yield break;
                if (_pageBack) i = Mathf.Max(0, i - 1); else i++;
            }
        }

        IEnumerator RunPage(string name, Func<RectTransform> build, float autoSeconds, bool waitForInput = true)
        {
            var page = build();
            PixFootball.UI.Glass.GlassSkin.Apply(page);
            var cg = page.gameObject.AddComponent<CanvasGroup>(); cg.alpha = 0f;
            CurrentPage = name; if (PageShown != null) PageShown(name);
            float t = 0f;
            while (true)
            {
                float dt = Time.unscaledDeltaTime;
                t += dt;
                ShowCamera();
                cg.alpha = Mathf.Clamp01(t / 0.35f);
                var inp = _dir.AutoRun ? null : PollInput();
                if (inp != null)
                {
                    if (t > 0.5f && (inp.Down[(int)Btn.A] || inp.Confirm)) break;
                    if (inp.Down[(int)Btn.B] || inp.Cancel) { _pageBack = true; break; }
                    if (inp.Menu) { _skipShow = true; break; }
                }
                float limit = _dir.AutoRun ? Mathf.Min(autoSeconds, 4.2f) : autoSeconds;
                if (t > limit) break;
                yield return null;
            }
            float f = 0f;
            while (f < 0.25f) { f += Time.unscaledDeltaTime; cg.alpha = 1f - f / 0.25f; ShowCamera(); yield return null; }
            Destroy(page.gameObject);
        }

        // ============================================================================================ game intro
        public IEnumerator GameIntro()
        {
            if (!_attached || PixSettings.Cinematics >= 2) yield break;
            EnsureTracker();
            bool minimal = PixSettings.Cinematics == 1;
            _introActive = true; CurrentShow = "intro"; _skipShow = false;
            _dir.Ui?.SetHudVisible(false);   // the scorebug is not drawn over the establishing shot or the matchup card (QA-112 / 117)
            _cam.Begin();
            if (_dir.Stage != null && _dir.Stage.Ball != null) _dir.Stage.Ball.SetVisible(false);
            var home = Flow.Home; var away = Flow.Away;
            var ctx = new ShotContext { Los = new Vector3(0f, 0f, -12f), LiveSide = 1f, Downfield = 1f };
            float matchupAt = 0.7f, stadiumAt = minimal ? 99f : 7.4f, total = minimal ? 6.2f : 13.2f;
            bool shownMatchup = false, shownStadium = false;
            float t = 0f;
            while (t < total)
            {
                float dt = Time.unscaledDeltaTime; t += dt;
                if (!_dir.AutoRun) { var inp = PollInput(); if (t > 0.8f && (inp.Down[(int)Btn.A] || inp.Confirm || inp.Down[(int)Btn.B] || inp.Menu)) break; }
                if (!shownMatchup && t >= matchupAt)
                {
                    shownMatchup = true;
                    string venue = (string.IsNullOrEmpty(home.StadiumName) ? "PIX PRO STADIUM" : home.StadiumName.ToUpperInvariant()) + "  -  " + home.City.ToUpperInvariant();
                    _ov.ShowMatchup(VisOf(CoreTeam.Away), "OVR " + away.TeamOverall(), KeyPlayers(away), VisOf(CoreTeam.Home), "OVR " + home.TeamOverall(), KeyPlayers(home), venue, minimal ? 5f : 6.4f, MatchupHeader());
                    CurrentPage = "matchup"; if (PageShown != null) PageShown("matchup");
                }
                if (!shownStadium && t >= stadiumAt)
                {
                    shownStadium = true;
                    // the real weather and time of day of this game (not the Settings value, and not a fixed 'clear night')
                    var w = _dir.Weather;
                    bool hasW = !string.IsNullOrEmpty(w.Label);
                    bool night = hasW ? w.Time == PixFootball.Core.TimeOfDay.Night : PixSettings.LastMatchLighting == LightingPreset.Night;
                    int seed = _dir.Config.Seed != 0 ? _dir.Config.Seed : StableHash(home.City + away.City);
                    int temp = AdjustTemperature(OverlayText.KickoffTemperature(seed, night, home.City), hasW ? w.Kind : PixFootball.Core.WeatherKind.Clear);
                    string cond = hasW ? w.Label : (night ? "Clear night" : "Sunny");
                    string header = !hasW ? (night ? "TONIGHT'S GAME" : "TODAY'S GAME") : (w.Time == PixFootball.Core.TimeOfDay.Night ? "TONIGHT'S GAME" : (w.Time == PixFootball.Core.TimeOfDay.Dusk ? "THIS EVENING" : "TODAY'S GAME"));
                    _ov.ShowStadium(string.IsNullOrEmpty(home.StadiumName) ? "PIX PRO STADIUM" : home.StadiumName, home.City, temp, cond, 5.4f, header);
                    CurrentPage = "stadium"; if (PageShown != null) PageShown("stadium");
                }
                ShotPose pose;
                if (t < stadiumAt) pose = ShotLibrary.Evaluate(ShotKind.StadiumWide, t / Mathf.Min(stadiumAt, total), ctx);
                else pose = ShotLibrary.Evaluate(ShotKind.SkySwoop, Mathf.Clamp01((t - stadiumAt) / (total - stadiumAt)), ctx);
                _cam.Set(pose.Pos, pose.Look, pose.Fov, t < 0.7f ? 60f : -1f, 32f);   // the first frames run the depth-of-field pass once (a nearly blur-free f/32) so the first replay does not hitch compiling it
                yield return null;
            }
            _dir.Ui?.SetHudVisible(true);
            _ov.ClearAll();
            _cam.End();
            if (_dir.Stage != null && _dir.Stage.Ball != null) _dir.Stage.Ball.SetVisible(true);
            _introActive = false; CurrentShow = ""; CurrentPage = "";
        }

        /// <summary>The same three lines for both teams, in the same order: the quarterback, the best skill player (RB / WR / TE) and the best defender (QA-114).</summary>
        List<string> KeyPlayers(LeagueTeam t)
        {
            var l = new List<string>();
            var qb = t.GetPlayer(t.Depth.StarterId(Position.QB));
            if (qb != null) l.Add(OverlayText.PositionLabel(Position.QB) + "   " + OverlayText.ShortName(qb.Name) + "   " + qb.Overall);
            Player skill = null;
            foreach (var pos in new[] { Position.RB, Position.WR, Position.TE })
            {
                var p = t.GetPlayer(t.Depth.StarterId(pos));
                if (p != null && (skill == null || p.Overall > skill.Overall)) skill = p;
            }
            if (skill != null) l.Add(OverlayText.PositionLabel(skill.Position) + "   " + OverlayText.ShortName(skill.Name) + "   " + skill.Overall);
            Player best = null;
            foreach (var pos in new[] { Position.LE, Position.RE, Position.MLB, Position.CB, Position.FS })
            {
                var p = t.GetPlayer(t.Depth.StarterId(pos));
                if (p != null && (best == null || p.Overall > best.Overall)) best = p;
            }
            if (best != null) l.Add(OverlayText.PositionLabel(best.Position) + "   " + OverlayText.ShortName(best.Name) + "   " + best.Overall);
            return l;
        }

        /// <summary>"PRO FOOTBALL  -  PRIMETIME": the rule set and the time of day of this game.</summary>
        string MatchupHeader()
        {
            var w = _dir.Weather;
            string slot = string.IsNullOrEmpty(w.Label) ? "PRIMETIME" : (w.Time == PixFootball.Core.TimeOfDay.Night ? "PRIMETIME" : (w.Time == PixFootball.Core.TimeOfDay.Dusk ? "EVENING KICKOFF" : "AFTERNOON KICKOFF"));
            return (_dir.Config.CollegeRules ? "COLLEGE FOOTBALL" : "PRO FOOTBALL") + "  -  " + slot;
        }

        static int StableHash(string s) { int h = 17; foreach (char c in s ?? "") h = h * 31 + c; return h & 0x7fffffff; }

        /// <summary>Kickoff temperature (F) adjusted for the weather of the game: snow is cold, rain and fog are cool.</summary>
        public static int AdjustTemperature(int baseF, PixFootball.Core.WeatherKind kind)
        {
            switch (kind)
            {
                case PixFootball.Core.WeatherKind.Snow: return Mathf.Clamp(baseF - 30, 12, 32);
                case PixFootball.Core.WeatherKind.Rain: return Mathf.Clamp(baseF - 9, 38, 80);
                case PixFootball.Core.WeatherKind.Fog: return Mathf.Clamp(baseF - 6, 36, 80);
                case PixFootball.Core.WeatherKind.Overcast: return baseF - 4;
                default: return baseF;
            }
        }

        // ============================================================================================ halftime
        public IEnumerator Halftime()
        {
            if (!_attached || PixSettings.Cinematics >= 2 || _trk == null) yield break;
            BeginShow("halftime");
            yield return RunPages(new PageDef("halftime stats", () => BuildStatsPage("HALFTIME", "FIRST HALF TEAM STATS", false), 14f), new PageDef("halftime performers", () => BuildLeadersPage("HALFTIME", "TOP PERFORMERS"), 14f));
            if (!_skipShow) yield return HighlightsReel("HALFTIME HIGHLIGHTS", 1, 2, 4);
            EndShow();
        }

        IEnumerator HighlightsReel(string heading, int firstQ, int lastQ, int count)
        {
            var clips = _lib.Top(count, firstQ, lastQ, 20f);
            if (clips.Count == 0) clips = _lib.Top(count, firstQ, lastQ, 0f);
            if (clips.Count == 0) yield break;
            CurrentPage = "highlights"; if (PageShown != null) PageShown("highlights");
            _ov.Enabled = true;
            _dir.Ui?.SetHudVisible(true);
            yield return _rc.PlayReel(clips, () =>
            {
                if (_dir.AutoRun) return false;
                var inp = PollInput();
                return inp.Down[(int)Btn.B] || inp.Menu || inp.Down[(int)Btn.A];
            }, (clip, n) =>
            {
                var team = VisOfIdx(clip.Possession);
                _ov.Clear(OverlaySlot.Third);
                _ov.ShowThird("reel" + n, 5, team, heading + "  " + (n + 1) + " OF " + clips.Count, clip.Title + "   " + clip.Situation, 6f);
            });
            _dir.Ui?.SetHudVisible(false);
            _ov.Enabled = false;
            // the controller restored the stage and gave the camera back: take both again for the next page
            if (_dir.Stage != null) _dir.Stage.gameObject.SetActive(false);
            if (_dir.Officials != null) _dir.Officials.SetCrewVisible(false);
            _cam.Begin();
        }

        // ============================================================================================ postgame
        public IEnumerator Postgame()
        {
            PostgameShown = false;
            if (!_attached || PixSettings.Cinematics >= 2 || _trk == null) yield break;
            BeginShow("postgame");
            // a clean slate for the last play: nothing of the live game is on screen behind the show
            if (_pendingClip) CommitClip();
            var s = State;
            CoreTeam? winner = s.Winner;
            int widx = winner.HasValue ? TeamIdxOf(winner.Value) : -1;
            yield return RunPages(new PageDef("final", () => BuildFinalPage(widx), 16f), new PageDef("box score", () => BuildStatsPage("FINAL", "TEAM STATS", true), 14f), new PageDef("leaders", () => BuildLeadersPage("FINAL", "TOP PERFORMERS"), 14f));
            if (!_skipShow) yield return HighlightsReel("GAME HIGHLIGHTS", 1, 99, 5);
            yield return RunReturnPage(() => { });
            EndShow();
            PostgameShown = true;
        }

        IEnumerator RunReturnPage(Action done)
        {
            bool pressed = false;
            var page = BuildReturnPage(() => pressed = true);
            PixFootball.UI.Glass.GlassSkin.Apply(page);
            var cg = page.gameObject.AddComponent<CanvasGroup>(); cg.alpha = 0f;
            CurrentPage = "return"; if (PageShown != null) PageShown("return");
            float t = 0f;
            while (!pressed)
            {
                t += Time.unscaledDeltaTime; ShowCamera(); cg.alpha = Mathf.Clamp01(t / 0.35f);
                if (_dir.AutoRun) { if (t > 3.2f) break; }
                else
                {
                    var inp = PollInput();
                    if (t > 0.6f && (inp.Down[(int)Btn.A] || inp.Confirm || inp.Down[(int)Btn.B] || inp.Cancel || inp.Menu)) break;
                }
                yield return null;
            }
            done();
        }

        // ============================================================================================ page builders
        RectTransform PageBase(string title, string subtitle, bool scoreStrip)
        {
            var root = UiKit.Rect(_showRoot, "Page_" + title); UiKit.Stretch(root);
            var scrim = UiKit.Panel(root, "Scrim", UiTheme.IsGlass ? new Color(0.02f, 0.025f, 0.035f, 0.52f) : new Color(0.02f, 0.03f, 0.06f, 0.84f)); UiKit.Stretch(scrim.rectTransform);
            var wash = TvUi.Fade() != null ? UiKit.Panel(root, "Wash", new Color(0.176f, 0.353f, 0.737f, 0.22f), TvUi.Fade()) : null;
            if (wash != null && UiTheme.IsGlass) wash.enabled = false;
            if (wash != null) { wash.type = Image.Type.Sliced; UiKit.Anchor(wash.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(1920, 420)); wash.rectTransform.anchorMax = new Vector2(1, 0); wash.rectTransform.sizeDelta = new Vector2(0, 420); }
            TvUi.Bar(root, "Accent", TvUi.Gold, new Vector2(0, 1), new Vector2(0, 1), new Vector2(110, -190), new Vector2(240, 8));
            TvUi.Text(root, title, 118, Color.white, true, TextAlignmentOptions.BottomLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(104, -48), new Vector2(900, 140), 6f);
            TvUi.Text(root, subtitle, 40, TvUi.Gold, true, TextAlignmentOptions.TopLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(110, -210), new Vector2(900, 56), 6f);
            if (scoreStrip) ScoreStrip(root);
            var foot = UiKit.Rect(root, "Footer"); UiKit.Anchor(foot, new Vector2(0, 0), new Vector2(0, 0), new Vector2(110, 26), new Vector2(1100, 52));
            var h = foot.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 40; h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
            PromptFooter.Attach(foot, 46, 26, 40).Set(PromptFooter.Item("glyph_a", "NEXT"), PromptFooter.Item("glyph_b", "BACK"), PromptFooter.Item("glyph_menu", "SKIP"));
            return root;
        }

        void ScoreStrip(RectTransform root)
        {
            var s = State;
            for (int i = 0; i < 2; i++)
            {
                var t = i == 0 ? VisOf(CoreTeam.Away) : VisOf(CoreTeam.Home);
                int sc = i == 0 ? s.AwayScore : s.HomeScore;
                var blk = TvUi.Strip(root, "ScoreBlock" + i, t.Color, 96, 22, true, true); UiKit.Anchor(blk.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-110 - (1 - i) * 440, -64), new Vector2(420, 96));
                TvUi.Crest(blk.transform, t.Crest, 72f, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(36, 0));
                TvUi.Text(blk.transform, t.Abbr, 46, TvUi.Readable(t.Color), true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(122, 2), new Vector2(160, 70), 3f);
                TvUi.Text(blk.transform, sc.ToString(), 72, TvUi.Readable(t.Color), true, TextAlignmentOptions.MidlineRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-40, 2), new Vector2(150, 84));
            }
        }

        RectTransform BuildStatsPage(string title, string subtitle, bool final)
        {
            var page = PageBase(title, subtitle, true);
            var away = VisOf(CoreTeam.Away); var home = VisOf(CoreTeam.Home);
            var rows = OverlayText.Compare(_trk.HomeBox, _trk.AwayBox);
            rows.AddRange(OverlayText.CompareExtra(_trk.HomeBox, _trk.AwayBox));   // 4th down, comp/att, sacked, red zone (QA-138)
            float w = 1300f, top = -310f;
            float canvasH = _showRoot != null && _showRoot.rect.height > 200f ? _showRoot.rect.height : 1080f;
            float pitch = Mathf.Clamp((canvasH - (-top) - 96f - 100f) / Mathf.Max(1, rows.Count), 38f, 66f);   // the last row stays clear of the footer on short canvases (QA-877)
            // header: crests and names above each value column
            var hb = TvUi.Strip(page, "Head", TvUi.Blue, 84, 24, true, true); UiKit.Anchor(hb.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, top), new Vector2(w, 84));
            TvUi.Crest(hb.transform, away.Crest, 64f, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(44, 0));
            TvUi.Text(hb.transform, away.Name.ToUpperInvariant(), 40, Color.white, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(124, 2), new Vector2(480, 70), 2f);
            TvUi.Crest(hb.transform, home.Crest, 64f, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-44, 0));
            TvUi.Text(hb.transform, home.Name.ToUpperInvariant(), 40, Color.white, true, TextAlignmentOptions.MidlineRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-124, 2), new Vector2(480, 70), 2f);
            for (int i = 0; i < rows.Count; i++)
            {
                var bg = TvUi.Bar(page, "Row" + i, i % 2 == 0 ? TvUi.Row : TvUi.RowAlt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, top - 96 - i * pitch), new Vector2(w, pitch - 4f));
                TvUi.Text(bg.transform, rows[i].Away, 42, Color.white, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(44, 0), new Vector2(300, 58));
                TvUi.Text(bg.transform, rows[i].Label, 30, TvUi.Muted, true, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 58), 3f);
                TvUi.Text(bg.transform, rows[i].Home, 42, Color.white, true, TextAlignmentOptions.MidlineRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-44, 0), new Vector2(300, 58));
            }
            return page;
        }

        RectTransform BuildLeadersPage(string title, string subtitle)
        {
            var page = PageBase(title, subtitle, true);
            string[] cats = { "pass", "rush", "rec", "def" }; string[] names = { "PASSING", "RUSHING", "RECEIVING", "DEFENSE" }; int[] counts = { 1, 2, 2, 2 };   // two defenders like the other categories (QA-139)
            for (int col = 0; col < 2; col++)
            {
                int teamIdx = col == 0 ? 1 : 0;   // away on the left like the scorebug
                var t = VisOfIdx(teamIdx);
                float cx = col == 0 ? -450f : 450f;
                var head = TvUi.Strip(page, "TeamHead" + col, t.Color, 76, 22, true, true); UiKit.Anchor(head.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(cx, -320), new Vector2(840, 76));
                TvUi.Crest(head.transform, t.Crest, 58f, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(38, 0));
                TvUi.Text(head.transform, t.Name.ToUpperInvariant(), 40, TvUi.Readable(t.Color), true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(112, 2), new Vector2(680, 66), 2f);
                float y = -414f;
                for (int c = 0; c < cats.Length; c++)
                {
                    TvUi.Text(page, names[c], 28, TvUi.Gold, true, TextAlignmentOptions.MidlineLeft, new Vector2(0.5f, 1), new Vector2(0f, 1), new Vector2(cx - 414f, y), new Vector2(400, 34), 5f);
                    y -= 36f;
                    var leaders = _trk.Leaders(teamIdx, cats[c], counts[c]);
                    if (leaders.Count == 0)
                    {
                        var bg = TvUi.Bar(page, "None", TvUi.RowAlt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(cx, y), new Vector2(840, 54));
                        TvUi.Text(bg.transform, "-", 32, TvUi.Muted, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(300, 50));
                        y -= 58f; continue;
                    }
                    foreach (var l in leaders)
                    {
                        var bg = TvUi.Bar(page, "Leader", TvUi.Row, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(cx, y), new Vector2(840, 54));
                        TvUi.Bar(bg.transform, "Edge", t.Color, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(8, 54));
                        TvUi.Text(bg.transform, OverlayText.LeaderLine(l, cats[c]), 32, Color.white, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(28, 0), new Vector2(800, 50), 1f);
                        y -= 58f;
                    }
                }
            }
            return page;
        }

        RectTransform BuildFinalPage(int winnerIdx)
        {
            var s = State; var f = Flow;
            var user = _dir.Config.UserTeam; bool tie = s.HomeScore == s.AwayScore;
            bool userWon = !tie && ((s.HomeScore > s.AwayScore) == (user == CoreTeam.Home));
            string title = tie ? "FINAL" : (_dir.Config.CpuVsCpu ? "FINAL" : (userWon ? "VICTORY" : "DEFEAT"));
            var page = PageBase(title, tie ? "TIE GAME" : "FINAL SCORE", false);
            // line score rows
            string[] qn = { "1", "2", "3", "4", "OT" };
            for (int i = 0; i < 5; i++) TvUi.Text(page, qn[i], 28, TvUi.Muted, true, TextAlignmentOptions.Center, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(150f + i * 96f, -300), new Vector2(90, 34));
            for (int i = 0; i < 2; i++)
            {
                var t = i == 0 ? VisOf(CoreTeam.Away) : VisOf(CoreTeam.Home);
                int[] byQ = i == 0 ? f.AwayByQuarter : f.HomeByQuarter; int total = i == 0 ? s.AwayScore : s.HomeScore;
                bool win = (i == 0 ? 1 : 0) == winnerIdx || (winnerIdx == (i == 0 ? 1 : 0));
                win = winnerIdx == (i == 0 ? 1 : 0);
                var row = TvUi.Strip(page, "TeamRow" + i, t.Color, 118, 36, true, true); UiKit.Anchor(row.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -350 - i * 132), new Vector2(1560, 118));
                TvUi.Crest(row.transform, t.Crest, 92f, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(64, 0));
                TvUi.Text(row.transform, t.Name.ToUpperInvariant(), 52, TvUi.Readable(t.Color), true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(180, 2), new Vector2(680, 80), 2f);
                int[] vals = MatchHud.LineScoreCells(byQ);
                for (int k = 0; k < 5; k++) TvUi.Text(row.transform, vals[k].ToString(), 42, TvUi.Readable(t.Color), true, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(150f + k * 96f, 0), new Vector2(90, 70));
                TvUi.Text(row.transform, total.ToString(), 88, win ? TvUi.Gold : TvUi.Readable(t.Color), true, TextAlignmentOptions.MidlineRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-50, 2), new Vector2(190, 100));
            }
            // player of the game
            var potg = _trk.PlayerOfTheGame(winnerIdx);
            if (potg != null)
            {
                var p = _trk.PlayerById(potg.PlayerId); var tv = VisOfIdx(_trk.TeamIdxOfPlayer(p));
                var card = TvUi.Strip(page, "Potg", TvUi.NavyDeep, 220, 36, true, true); UiKit.Anchor(card.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 110), new Vector2(1560, 220));
                var tag = TvUi.Strip(card.transform, "Tag", TvUi.Gold, 220, 36, true, true); UiKit.Anchor(tag.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(420, 220));
                TvUi.Text(tag.transform, "PLAYER OF\nTHE GAME", 44, new Color(0.06f, 0.07f, 0.1f), true, TextAlignmentOptions.Center, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(420, 220), 4f);
                TvUi.Crest(card.transform, tv.Crest, 110f, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(470, 0));
                TvUi.Text(card.transform, OverlayText.Heading(p), 74, Color.white, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(610, -14), new Vector2(900, 90), 2f);
                TvUi.Text(card.transform, OverlayText.PotgSubLine(p, tv.Name, potg.Stats), 36, TvUi.Gold, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(610, 28), new Vector2(920, 56), 2f);
            }
            return page;
        }

        /// <summary>Joins the non-empty parts (a player with no stat line no longer ends in a dangling separator).</summary>
        public static string JoinNonEmpty(string sep, params string[] parts)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var p in parts) { if (string.IsNullOrEmpty(p)) continue; if (sb.Length > 0) sb.Append(sep); sb.Append(p); }
            return sb.ToString();
        }

        RectTransform BuildReturnPage(Action onContinue)
        {
            var s = State;
            var root = UiKit.Rect(_showRoot, "Page_Return"); UiKit.Stretch(root);
            var scrim = UiKit.Panel(root, "Scrim", UiTheme.IsGlass ? new Color(0.02f, 0.025f, 0.035f, 0.55f) : new Color(0.02f, 0.03f, 0.06f, 0.9f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            TvUi.Text(root, "THANKS FOR WATCHING", 96, Color.white, true, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(1500, 130), 8f);
            TvUi.Bar(root, "Rule", TvUi.Gold, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 170), new Vector2(240, 8));
            TvUi.Text(root, Flow.Abbr(CoreTeam.Away) + " " + s.AwayScore + "   -   " + Flow.Abbr(CoreTeam.Home) + " " + s.HomeScore, 64, TvUi.Gold, true, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 80), new Vector2(1500, 90), 6f);
            var col = UiKit.Rect(root, "Buttons"); UiKit.Anchor(col, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -140), new Vector2(560, 100));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            var btn = UiKit.MenuButton(col, "RETURN TO MENU", () => { if (onContinue != null) onContinue(); }, 560, 90, 40);
            UiKit.EnsureEventSystem();
            StartCoroutine(SelectLater(btn.gameObject));
            return root;
        }

        IEnumerator SelectLater(GameObject g)
        {
            yield return null;
            if (g != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(g);
        }
    }
}
