using System;
using System.Collections.Generic;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.Presentation
{
    /// <summary>What a card needs to draw a team.</summary>
    public struct TeamVis
    {
        public string Abbr, Name, Crest; public Color Color;
        public TeamVis(string abbr, string name, string crest, Color color) { Abbr = abbr; Name = name; Crest = crest; Color = color; }
    }

    public enum OverlaySlot { Third, Side, BottomRight, Center }

    /// <summary>
    /// TV overlays in the UI kit style, animated in and out: player stat lower-third, penalty lower-third, play-by-play ticker, drive summary, team stat comparison, score update,
    /// two-minute warning, "next play" down and distance card, stadium / weather card and the matchup card. Cards live in four screen slots (lower left, right side, lower right,
    /// centre) so two cards never overlap; a more important card replaces the one on screen, an equal one waits its turn (and is dropped when stale).
    /// </summary>
    public sealed partial class TvOverlays : MonoBehaviour
    {
        sealed class Card
        {
            public RectTransform Root; public CanvasGroup Group; public OverlaySlot Slot; public int Priority;
            public float Age, Hold; public bool Leaving; public float LeaveAge; public Vector2 Rest, Hidden; public string Id;
            public float InTime = 0.3f, OutTime = 0.22f;
        }

        sealed class Pending { public OverlaySlot Slot; public int Priority; public float Hold; public Func<Card> Build; public float Age; public string Id; }

        Canvas _canvas; RectTransform _root;
        readonly List<Card> _cards = new List<Card>();
        readonly List<Pending> _pending = new List<Pending>();
        MatchDirector _dir;
        public bool Enabled = true;
        public bool CardsWaiting { get { return _pending.Count > 0; } }
        public int ActiveCount { get { return _cards.Count; } }
        public string LastShown { get; private set; }
        public event Action<string> CardShown;

        public static TvOverlays Create(Transform parent, MatchDirector dir)
        {
            var go = new GameObject("TvOverlays");
            go.transform.SetParent(parent, false);
            var o = go.AddComponent<TvOverlays>();
            o._dir = dir;
            o._canvas = UiKit.CreateCanvas("TvOverlayCanvas", 25, go.transform);
            o._root = (RectTransform)o._canvas.transform;
            TvUi.Prewarm();
            o._canvas.GetComponent<GraphicRaycaster>().enabled = false;
            return o;
        }

        public Transform Root { get { return _root; } }

        // ============================================================================================ queueing
        bool Visible
        {
            get
            {
                if (!Enabled || !PixSettings.TvOverlays) return false;
                if (_dir != null && (_dir.CallModel != null || (_dir.Ui != null && _dir.Ui.PauseOpen))) return false;
                // QA-850: never draw over the coin toss, the final screen, the penalty panel or the play-call screen
                var hud = _dir != null ? _dir.Ui as MatchHud : null;
                if (hud != null && hud.ModalOpen) return false;
                // QA-129: the first-person view keeps its pre-snap clear
                if (_dir != null && _dir.Cams != null && _dir.Cams.Mode == CameraMode.FirstPerson && (_dir.Phase == MatchPhase.PreSnap || _dir.Phase == MatchPhase.PlayCall)) return false;
                return true;
            }
        }

        void Request(OverlaySlot slot, int priority, float hold, string id, Func<Card> build)
        {
            if (!Enabled || !PixSettings.TvOverlays) return;
            var cur = SlotCard(slot);
            if (cur == null) { Launch(build(), slot, priority, hold, id); return; }
            if (priority > cur.Priority) { Dismiss(cur, 0.12f); _pending.Insert(0, new Pending { Slot = slot, Priority = priority, Hold = hold, Build = build, Age = 0f, Id = id }); return; }
            // an equal or lower card waits (a ticker line is dropped when something is already waiting)
            if (priority <= 1) { int n = 0; foreach (var p in _pending) if (p.Slot == slot) n++; if (n >= 1) return; }
            _pending.Add(new Pending { Slot = slot, Priority = priority, Hold = hold, Build = build, Age = 0f, Id = id });
        }

        Card SlotCard(OverlaySlot s) { foreach (var c in _cards) if (c.Slot == s && !(c.Leaving && c.LeaveAge > c.OutTime * 0.6f)) return c; return null; }

        void Launch(Card c, OverlaySlot slot, int priority, float hold, string id)
        {
            c.Slot = slot; c.Priority = priority; c.Hold = hold; c.Id = id; c.Age = 0f;
            if (slot != OverlaySlot.Center && c.Root != null)
            {
                c.Root.localScale = Vector3.one * HudMetrics.CardScale;
                if (c.Root.anchorMin.y < 0.5f) { float lift = HudMetrics.BarLift - 24f + 14f; c.Rest.y += lift; c.Hidden.y += lift; }   // clear of the (lifted) score bar
            }   // v0.11: one scale for every lower third, side card and next-play card
            c.Group = c.Root.gameObject.AddComponent<CanvasGroup>(); c.Group.alpha = 0f;
            c.Root.anchoredPosition = c.Hidden;
            _cards.Add(c);
            if (UiTheme.IsGlass) PixFootball.UI.Glass.GlassSkin.Apply(c.Root);
            LastShown = id;
            if (CardShown != null) CardShown(id);
        }

        void Dismiss(Card c, float fast = 0f) { if (c.Leaving) return; c.Leaving = true; c.LeaveAge = 0f; if (fast > 0f) c.OutTime = fast; }

        public void ClearAll()
        {
            foreach (var c in _cards) if (c.Root != null) Destroy(c.Root.gameObject);
            _cards.Clear(); _pending.Clear();
        }

        /// <summary>
        /// Drops what belongs to the play that just ended (stat lower-thirds, the penalty strip, the ticker and the score card) when the next play is set up or snapped; the drive summary and the
        /// team stats card are meant to appear on the next pre-snap and stay (QA-096 / 228 / 236 / 240 / 264 / 214).
        /// </summary>
        public void ClearStale()
        {
            foreach (var c in _cards) if (IsPlayResult(c.Id)) Dismiss(c, 0.1f);
            _pending.RemoveAll(p => IsPlayResult(p.Id));
        }

        static bool IsPlayResult(string id) { return id != null && (id == "ticker" || id == "penalty" || id == "score" || id.StartsWith("stat:")); }

        /// <summary>Drops cards of a slot (e.g. the ticker and stat cards when the next snap starts).</summary>
        public void Clear(OverlaySlot slot) { foreach (var c in _cards) if (c.Slot == slot) Dismiss(c, 0.1f); _pending.RemoveAll(p => p.Slot == slot); }

        void Update()
        {
            bool vis = Visible;
            _canvas.enabled = vis;
            if (!vis) return;   // cards and the queue only age while they can be seen (a card must not expire behind the play-call screen)
            float dt = UnityEngine.Time.unscaledDeltaTime;
            var hud = _dir != null ? _dir.Ui as MatchHud : null;
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                var c = _cards[i];
                if (c.Root == null) { _cards.RemoveAt(i); continue; }
                c.Age += dt;
                if (!c.Leaving && c.Age > c.InTime + c.Hold) Dismiss(c);
                float k;
                if (c.Leaving) { c.LeaveAge += dt; k = 1f - TvUi.EaseIn(c.LeaveAge / c.OutTime); if (c.LeaveAge >= c.OutTime) { Destroy(c.Root.gameObject); _cards.RemoveAt(i); continue; } }
                else k = TvUi.EaseOut(c.Age / c.InTime);
                c.Root.anchoredPosition = Vector2.LerpUnclamped(c.Hidden, c.Rest, k);
                c.Group.alpha = Mathf.Clamp01(k * 1.4f);
            }
            for (int i = 0; i < _pending.Count; i++)
            {
                var p = _pending[i];
                p.Age += dt;   // QA-853: queued cards age only while visible
                if (p.Age > 7f) { _pending.RemoveAt(i--); continue; }
                if (hud != null && hud.BannerVisible && p.Priority < 5) continue;   // the banner has the screen first; the card follows it (QA-135 / 140 / 250)
                if (SlotCard(p.Slot) == null) { _pending.RemoveAt(i); Launch(p.Build(), p.Slot, p.Priority, p.Hold, p.Id); break; }
            }
        }

        // ============================================================================================ building blocks
        static RectTransform NewCard(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 rest, Vector2 size, Vector2 hiddenOffset, out Vector2 hidden)
        {
            var r = UiKit.Rect(parent, name);
            UiKit.Anchor(r, anchor, pivot, rest, size);
            hidden = rest + hiddenOffset;
            return r;
        }

        Card MakeThird(string name, Color teamColor, string crest, string abbr, string line1, string line2, Color accent, Color line2Color, int height = 124, bool compact = false, string rightText = null)
        {
            Vector2 hidden;
            var root = NewCard(_root, name, new Vector2(0, 0), new Vector2(0, 0), new Vector2(80, 118), new Vector2(1080, height), new Vector2(-1200, 0), out hidden);
            TvUi.Strip(root, "Body", TvUi.Navy, height, 26, true, true).rectTransform.sizeDelta = new Vector2(1080, height);
            var body = root.GetChild(0).GetComponent<RectTransform>(); UiKit.Anchor(body, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(1080, height));
            float blockW = compact ? 118f : 232f;
            var blk = TvUi.Strip(root, "Team", teamColor, height, 26, true, true); UiKit.Anchor(blk.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(blockW, height));
            if (!compact && !string.IsNullOrEmpty(crest)) TvUi.Crest(root, crest, 84f, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(66, 8));
            if (!compact) TvUi.Text(root, abbr, 28, TvUi.Readable(teamColor), true, TextAlignmentOptions.Center, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 8), new Vector2(130, 34), 2f);
            float tx = blockW + 26f;
            float rightW = string.IsNullOrEmpty(rightText) ? 0f : 150f;   // the game clock at the right end of the ticker strip (QA-854)
            UiKit.Fit(TvUi.Text(root, line1, compact ? 38f : 48f, Color.white, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(tx, compact ? -6 : -10), new Vector2(1080 - tx - 40 - rightW, 60), 1.5f), compact ? 24f : 28f);   // long lines shrink, then truncate (QA-852)
            if (!string.IsNullOrEmpty(line2)) UiKit.Fit(TvUi.Text(root, line2, compact ? 30f : 40f, line2Color, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(tx, compact ? 6 : 14), new Vector2(1080 - tx - 40, 52), 1.5f), 22f);
            if (rightW > 0f) TvUi.Text(root, rightText, 28, TvUi.Muted, true, TextAlignmentOptions.MidlineRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-34, 0), new Vector2(rightW, 44), 1f);
            var bar = TvUi.Bar(root, "Accent", accent, new Vector2(0, 0), new Vector2(0, 0), new Vector2(blockW - 6, 0), new Vector2(1080 - blockW - 30, 5));
            return new Card { Root = root, Rest = root.anchoredPosition, Hidden = hidden };
        }

        // ============================================================================================ lower thirds
        /// <summary>"#12 J. DOE" / "QB - 18/24, 231 YDS, 2 TD" with the team block.</summary>
        public void ShowPlayerStat(Player p, StatLine s, TeamVis team, string tag, float hold = 5.5f)
        {
            // 2K8-style slim table strip (# NAME TEAM + the stat columns for what the player did); see TvOverlays.StatTable.cs
            var table = StatTable.For(s, p.Position);
            if (table == null) return;
            Request(OverlaySlot.Third, 3, hold, "stat:" + p.Name, () => MakeStatTable(p, table, team, tag));
        }

        public void ShowPenalty(string headline, string detail, TeamVis team, float hold = 4.5f)
        {
            Request(OverlaySlot.Third, 4, hold, "penalty", () => MakeThird("PenaltyThird", new Color(1f, 0.918f, 0f), team.Crest, "FLAG", headline, detail, new Color(1f, 0.918f, 0f), new Color(1f, 0.918f, 0f)));
        }

        /// <summary>A generic lower-third (highlight reel tags, replay labels): team block, two lines of text.</summary>
        public void ShowThird(string id, int priority, TeamVis team, string line1, string line2, float hold = 4f)
        {
            Request(OverlaySlot.Third, priority, hold, id, () => MakeThird("Third_" + id, team.Color, team.Crest, team.Abbr, line1, line2, TvUi.Gold, TvUi.Gold));
        }

        /// <summary>One line of play-by-play (the ticker).</summary>
        public void ShowTicker(string text, TeamVis team, string clock, float hold = 3.6f)
        {
            if (string.IsNullOrEmpty(text)) return;
            Request(OverlaySlot.Third, 1, hold, "ticker", () => MakeThird("Ticker", team.Color, "", team.Abbr, text, null, TvUi.Blue, Color.white, 70, true, clock));
        }

        // ============================================================================================ side cards
        /// <summary>Top offset of a side card: -470 normally, higher up for tall cards so the bottom stays above the bottom bar on short canvases (QA-851).</summary>
        float SideTop(float height)
        {
            float ch = _root != null ? _root.rect.height : 1080f; if (ch < 200f) ch = 1080f;
            return -Mathf.Clamp(Mathf.Min(470f, ch - height - 110f), 150f, 470f);
        }

        Card MakeSide(string name, float height, Color headerColor, string headerText, string crestA, string crestB, int rows = 0)
        {
            Vector2 hidden;
            var root = NewCard(_root, name, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-80, SideTop(height)), new Vector2(600, height), new Vector2(760, 0), out hidden);
            var back = TvUi.Strip(root, "Body", TvUi.Navy, (int)height, 22, true, false); UiKit.Anchor(back.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(600, height));
            var head = TvUi.Strip(root, "Header", headerColor, 76, 22, true, false); UiKit.Anchor(head.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(600, 76));
            Color hc = TvUi.Readable(headerColor);
            float tx = 36f;
            if (!string.IsNullOrEmpty(crestA)) { TvUi.Crest(root, crestA, 56f, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -10)); tx = 112f; }
            TvUi.Text(root, headerText, 38, hc, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(tx, -8), new Vector2(600 - tx - 20, 60), 2f);
            if (!string.IsNullOrEmpty(crestB)) TvUi.Crest(root, crestB, 56f, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-24, -10));
            TvUi.Bar(root, "Accent", TvUi.Gold, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 0), new Vector2(560, 5));
            return new Card { Root = root, Rest = root.anchoredPosition, Hidden = hidden };
        }

        public void ShowDriveSummary(DriveRecord d, TeamVis team, string startText, float hold = 7f)
        {
            if (d == null) return;
            Request(OverlaySlot.Side, 3, hold, "drive", () =>
            {
                var c = MakeSide("DriveSummary", 336, team.Color, team.Abbr + " DRIVE SUMMARY", team.Crest, null);
                Color rc = ResultColor(d.Result);
                TvUi.Text(c.Root, OverlayText.DriveResult(d), 56, rc, true, TextAlignmentOptions.Center, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -82), new Vector2(560, 70), 3f);
                string[] vals = { d.Plays.ToString(), d.Yards.ToString(), OverlayText.Clock(d.Seconds) };
                string[] labs = { d.Plays == 1 ? "PLAY" : "PLAYS", Math.Abs(d.Yards) == 1 ? "YARD" : "YARDS", "TIME OF POSS." };
                for (int i = 0; i < 3; i++)
                {
                    float x = 36f + i * 176f;
                    TvUi.Bar(c.Root, "Col" + i, TvUi.Row, new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, 56), new Vector2(164, 124));
                    TvUi.Text(c.Root, vals[i], 60, (i == 1 && d.Yards < 0) ? new Color(1f, 0.33f, 0.33f) : Color.white, true, TextAlignmentOptions.Center, new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, 100), new Vector2(164, 76));
                    TvUi.Text(c.Root, labs[i], 20, TvUi.Muted, true, TextAlignmentOptions.Center, new Vector2(0, 0), new Vector2(0, 0), new Vector2(x, 64), new Vector2(164, 30), 2f);
                }
                if (!string.IsNullOrEmpty(startText)) TvUi.Text(c.Root, startText, 24, TvUi.Muted, false, TextAlignmentOptions.Center, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(560, 34), 1f);
                return c;
            });
        }

        static Color ResultColor(string result)
        {
            switch (result)
            {
                case "Touchdown": return TvUi.Gold;
                case "Field Goal": return new Color(0.067f, 0.94f, 0.333f);
                case "Interception": case "Fumble": case "Downs": case "Safety": return new Color(1f, 0.33f, 0.33f);
                default: return Color.white;
            }
        }

        public void ShowComparison(List<CompareRow> rows, TeamVis away, TeamVis home, string title, float hold = 7f)
        {
            Request(OverlaySlot.Side, 2, hold, "compare", () =>
            {
                if (rows.Count > 6) rows = rows.GetRange(0, 6);   // the in-game card shows six rows at most (the postgame page has its own table)
                float h = 112f + rows.Count * 64f;
                var c = MakeSide("TeamStats", h, TvUi.Blue, title, away.Crest, home.Crest);
                for (int i = 0; i < rows.Count; i++)
                {
                    float y = -92f - i * 64f;
                    var bg = TvUi.Bar(c.Root, "Row" + i, i % 2 == 0 ? TvUi.Row : TvUi.RowAlt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, y), new Vector2(552, 58));
                    TvUi.Text(bg.transform, rows[i].Away, 36, Color.white, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(170, 52));
                    TvUi.Text(bg.transform, rows[i].Label, 24, TvUi.Muted, true, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(230, 52), 1.5f);
                    TvUi.Text(bg.transform, rows[i].Home, 36, Color.white, true, TextAlignmentOptions.MidlineRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(170, 52));
                }
                return c;
            });
        }

        public void ShowScoreUpdate(TeamVis away, int awayScore, TeamVis home, int homeScore, string play, string clock, float hold = 5.5f, int scorerIdx = -1)
        {
            Request(OverlaySlot.Side, 4, hold, "score", () =>
            {
                var c = MakeSide("ScoreUpdate", 268, TvUi.Gold, "SCORE UPDATE", null, null);
                for (int i = 0; i < 2; i++)
                {
                    var t = i == 0 ? away : home; int sc = i == 0 ? awayScore : homeScore;
                    var row = TvUi.Strip(c.Root, "TeamRow" + i, t.Color, 64, 18, true, true); UiKit.Anchor(row.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -92 - i * 70), new Vector2(552, 64));
                    TvUi.Crest(row.transform, t.Crest, 52f, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(34, 0));
                    TvUi.Text(row.transform, t.Abbr, 40, TvUi.Readable(t.Color), true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(104, 2), new Vector2(200, 60), 2f);
                    TvUi.Text(row.transform, sc.ToString(), 56, TvUi.Readable(t.Color), true, TextAlignmentOptions.MidlineRight, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-30, 2), new Vector2(140, 60));
                    if (scorerIdx == i) TvUi.Bar(row.transform, "Scorer", TvUi.Gold, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 5)).rectTransform.anchorMax = new Vector2(1, 0);   // the team that just scored gets the gold rule
                }
                UiKit.Fit(TvUi.Text(c.Root, (scorerIdx >= 0 ? (scorerIdx == 0 ? away.Abbr : home.Abbr) + ":  " : "") + play, 24, TvUi.Muted, true, TextAlignmentOptions.Center, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 14), new Vector2(560, 32), 1.5f), 16);
                c.InTime = 0.34f;
                return c;
            });
        }

        public void ShowTwoMinute(string quarterText, float hold = 4.5f)
        {
            Request(OverlaySlot.Side, 3, hold, "twominute", () =>
            {
                var c = MakeSide("TwoMinute", 190, new Color(1f, 0.565f, 0f), "TWO-MINUTE WARNING", null, null);
                TvUi.Text(c.Root, "2:00", 84, new Color(1f, 0.565f, 0f), true, TextAlignmentOptions.Center, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-120, 22), new Vector2(240, 96), 3f);
                TvUi.Text(c.Root, quarterText, 34, Color.white, true, TextAlignmentOptions.MidlineLeft, new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(10, 44), new Vector2(260, 48), 2f);
                return c;
            });
        }

        // ============================================================================================ bottom right
        public void ShowNextPlay(string downDistance, string spot, string clock, float hold = 3.4f)
        {
            Request(OverlaySlot.BottomRight, 2, hold, "nextplay", () =>
            {
                Vector2 hidden;
                var root = NewCard(_root, "NextPlay", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-80, 118), new Vector2(560, 104), new Vector2(700, 0), out hidden);
                var back = TvUi.Strip(root, "Body", TvUi.Navy, 104, 22, true, false); UiKit.Anchor(back.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(560, 104));
                var tag = TvUi.Strip(root, "Tag", TvUi.Blue, 104, 22, true, false); UiKit.Anchor(tag.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(176, 104));
                TvUi.Text(root, "NEXT\nPLAY", 30, Color.white, true, TextAlignmentOptions.Center, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0), new Vector2(166, 104), 2f);
                TvUi.Text(root, downDistance, 54, TvUi.Gold, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(206, -6), new Vector2(340, 64), 2f);
                TvUi.Text(root, "BALL ON " + spot + (string.IsNullOrEmpty(clock) ? "" : "   " + clock), 26, Color.white, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(206, 10), new Vector2(340, 36), 1.5f);
                TvUi.Bar(root, "Accent", TvUi.Gold, new Vector2(0, 0), new Vector2(0, 0), new Vector2(176, 0), new Vector2(360, 4));
                return new Card { Root = root, Rest = root.anchoredPosition, Hidden = hidden };
            });
        }

        // ============================================================================================ centre cards
        /// <summary>Stadium and weather card shown at kickoff: stadium name, city and temperature.</summary>
        public void ShowStadium(string stadium, string city, int tempF, string conditions, float hold = 6f, string header = "TONIGHT'S GAME")
        {
            Request(OverlaySlot.Center, 5, hold, "stadium", () =>
            {
                Vector2 hidden;
                var root = NewCard(_root, "StadiumCard", new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(1320, 160), new Vector2(0, -260), out hidden);
                var back = TvUi.Strip(root, "Body", TvUi.Navy, 160, 30, true, true); UiKit.Anchor(back.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(1320, 160));
                var tmp = TvUi.Strip(root, "Temp", TvUi.Blue, 160, 30, true, true); UiKit.Anchor(tmp.rectTransform, new Vector2(1, 0), new Vector2(1, 0), Vector2.zero, new Vector2(330, 160));
                TvUi.Text(root, header, 26, TvUi.Gold, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(70, -14), new Vector2(600, 34), 4f);
                TvUi.Text(root, stadium.ToUpperInvariant(), 62, Color.white, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(70, 2), new Vector2(940, 78), 2f);
                TvUi.Text(root, city.ToUpperInvariant() + "     " + conditions.ToUpperInvariant(), 30, TvUi.Muted, true, TextAlignmentOptions.MidlineLeft, new Vector2(0, 0), new Vector2(0, 0), new Vector2(70, 14), new Vector2(940, 40), 2f);
                TvUi.Text(root, tempF + "<sup>o</sup>F", 92, Color.white, true, TextAlignmentOptions.Center, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(UiTheme.IsGlass ? -165 : -96, 12), new Vector2(UiTheme.IsGlass ? 270 : 240, 110));
                TvUi.Text(root, "KICKOFF TEMP", 22, new Color(1f, 1f, 1f, 0.8f), true, TextAlignmentOptions.Center, new Vector2(1, 0), new Vector2(1, 0), new Vector2(UiTheme.IsGlass ? -165 : -96, 14), new Vector2(UiTheme.IsGlass ? 270 : 240, 30), 2f);
                TvUi.Bar(root, "Accent", TvUi.Gold, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 0), new Vector2(940, 5));
                return new Card { Root = root, Rest = root.anchoredPosition, Hidden = hidden, InTime = 0.4f };
            });
        }

        /// <summary>Game intro matchup card: both crests, records and key players.</summary>
        public void ShowMatchup(TeamVis away, string awayRecord, List<string> awayKeys, TeamVis home, string homeRecord, List<string> homeKeys, string venueLine, float hold = 8f, string header = "EXHIBITION  -  PRIMETIME")
        {
            Request(OverlaySlot.Center, 6, hold, "matchup", () =>
            {
                Vector2 hidden;
                var root = NewCard(_root, "MatchupCard", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(1640, 650), new Vector2(0, -420), out hidden);
                var back = UiKit.Panel(root, "Body", TvUi.NavyDeep); UiKit.Stretch(back.rectTransform);
                var head = TvUi.Strip(root, "Head", TvUi.Gold, 76, 28, true, true); UiKit.Anchor(head.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, 0), new Vector2(1640, 76));
                TvUi.Text(root, header, 44, new Color(0.06f, 0.07f, 0.1f), true, TextAlignmentOptions.Center, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -8), new Vector2(1400, 60), 6f);
                for (int i = 0; i < 2; i++)
                {
                    var t = i == 0 ? away : home; string rec = i == 0 ? awayRecord : homeRecord; var keys = i == 0 ? awayKeys : homeKeys;
                    float cx = i == 0 ? -520f : 520f;
                    var wash = TvUi.Strip(root, "Wash" + i, new Color(t.Color.r, t.Color.g, t.Color.b, 0.38f), 510, 40, true, true); UiKit.Anchor(wash.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(cx, 50), new Vector2(640, 510));
                    TvUi.Crest(root, t.Crest, 196f, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(cx, -92));
                    TvUi.Text(root, t.Name.ToUpperInvariant(), 52, Color.white, true, TextAlignmentOptions.Center, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(cx, -294), new Vector2(620, 66), 2f);
                    TvUi.Text(root, rec, 40, TvUi.Gold, true, TextAlignmentOptions.Center, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(cx, -358), new Vector2(620, 50), 4f);
                    TvUi.Text(root, "KEY PLAYERS", 22, TvUi.Muted, true, TextAlignmentOptions.Center, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(cx, -424), new Vector2(620, 28), 4f);
                    for (int k = 0; k < keys.Count && k < 3; k++)
                        TvUi.Text(root, keys[k], 28, Color.white, true, TextAlignmentOptions.Center, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(cx, -458 - k * 40), new Vector2(620, 38), 1.5f);
                }
                TvUi.Text(root, "VS", 96, Color.white, true, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(240, 120), 4f);
                TvUi.Text(root, venueLine, 28, TvUi.Muted, true, TextAlignmentOptions.Center, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(1400, 36), 3f);
                return new Card { Root = root, Rest = root.anchoredPosition, Hidden = hidden, InTime = 0.5f, OutTime = 0.4f };
            });
        }
    }
}
