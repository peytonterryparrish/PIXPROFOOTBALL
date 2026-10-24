using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.UI.Franchise
{
    /// <summary>Pure draft text (EditMode tested).</summary>
    public static class DraftText
    {
        public static string Estimate(ProspectRow r) { return Mathf.RoundToInt(r.EstOverall) + " +/- " + Mathf.RoundToInt(r.Uncertainty); }

        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        /// <summary>"ROUND 2  PICK 5  (#37)" for a slot.</summary>
        public static string SlotLabel(DraftSlot s) { return s == null ? "" : "ROUND " + s.Round + "  PICK " + s.PickInRound + "  (#" + s.Overall + ")"; }

        /// <summary>Draft stage of the calendar as a short headline.</summary>
        public static string Headline(CareerSave save)
        {
            switch (save.Phase)
            {
                case CareerPhase.Scouting: return "COMBINE AND SCOUTING";
                case CareerPhase.Draft: return save.Draft.Finished ? "DRAFT COMPLETE" : (CareerDraft.UserOnClock(save) ? "YOU ARE ON THE CLOCK" : "THE DRAFT IS UNDER WAY");
                case CareerPhase.RookieSigning: return "SIGN YOUR ROOKIES";
                default: return "THE DRAFT";
            }
        }
    }

    /// <summary>
    /// The draft (simple screen for v0.8, the full Draft House event follows): the big board with the user's scouting estimates, a prospect card with scouting points, the pick clock and the live pick list
    /// with grades, trade-up offers, auto pick and sims, and the rookie signing step. One screen for the Scouting, Draft and RookieSigning phases.
    /// </summary>
    public sealed class DraftPage : FranchisePage
    {
        public const float ClockSeconds = 180f;
        GlassTable _board, _picks, _rookies; PlayerCardPanel _rookieCard; OptionSelector _posSel;
        TextMeshProUGUI _title, _clock, _points, _hint; RectTransform _detail, _bar, _needs;
        readonly List<Button> _bar1 = new List<Button>();
        List<ProspectRow> _rows = new List<ProspectRow>(); List<DraftSlot> _slots = new List<DraftSlot>(); List<RookieRight> _rights = new List<RookieRight>();
        string _sort = "grade"; float _clockLeft = ClockSeconds; int _clockPick = -1;
        ProspectRow Cur { get { return _rows.Count == 0 ? null : _rows[Mathf.Clamp(_board.Cursor, 0, _rows.Count - 1)]; } }
        bool Rookies { get { return Save.Phase == CareerPhase.RookieSigning; } }

        protected override void Build(RectTransform root)
        {
            var t = GlassTheme.T;
            var back = GlassKit.Panel(root, "HeaderBack", 1.2f, false, t.cornerRadius); Lay.Place(back.rectTransform, 0f, 0f, 1800f, 96f);
            _title = Lay.Text(root, "", 44f, t.textPrimary, 24f, 2f, 1100f, 56f, true, TextAlignmentOptions.MidlineLeft, 3f, "Title");
            _hint = Lay.Text(root, "", 22f, t.accent, 24f, 56f, 1300f, 32f, true, TextAlignmentOptions.MidlineLeft, 2f, "Clock");
            _clock = Lay.Text(root, "", 54f, t.textPrimary, 1420f, 0f, 356f, 70f, true, TextAlignmentOptions.MidlineRight, 2f, "ClockTime");
            _points = Lay.Caps(root, "", 1168f, 64f, 608f, 28f, t.textSecondary, 18f, TextAlignmentOptions.MidlineRight);
            _needs = Lay.Group(root, "Needs", 1000f, 0f, 380f, 1f);

            _posSel = GlassKit.Stepper(root, "Position", PosFilter.Names, 0, i => Refresh(), 400f, 52f); Lay.Place((RectTransform)_posSel.transform, 0f, 108f, 400f, 52f);
            var bc = new List<TableColumn>
            {
                new TableColumn("RK", 0.6f, i => _rows[i].ConsensusRank.ToString(), false),
                new TableColumn("PROSPECT", 2.6f, i => _rows[i].Name, false, null, false),
                new TableColumn("POS", 0.7f, i => _rows[i].Pos.ToString(), false, null, false),
                new TableColumn("SCHOOL", 1.7f, i => _rows[i].School ?? "", false, null, false),
                new TableColumn("AGE", 0.6f, i => _rows[i].Age.ToString(), true, null, false),
                new TableColumn("YOUR EST.", 1.5f, i => DraftText.Estimate(_rows[i]), true),
                new TableColumn("     PROJECTED", 1.9f, i => "     " + _rows[i].ProjectedRound, false, null, false),
                new TableColumn("STAR", 0.8f, i => _rows[i].Starred ? "YES" : "", true, i => GlassTheme.T.accent)
            };
            _board = GlassTable.Create(root, "Board", 1060f, 542f, bc, 46f); Lay.Place((RectTransform)_board.transform, 0f, 168f, 1060f, 542f);
            _board.CursorChanged = i => ShowProspect();
            _board.Activated = i => PickCurrent();
            _board.AltAction = i => { var r = Cur; if (r != null) { CareerDraft.SetStarred(Save, r.Id, !r.Starred); Refresh(); } };
            _board.HeaderClicked = SortBy; _board.SortRequested = SortBy;
            _board.EmptyText = "Nobody left on the board.";

            _detail = Lay.Group(root, "Detail", 1076f, 108f, 724f, 290f);
            _picks = GlassTable.Create(root, "Picks", 724f, 300f, new List<TableColumn>
            {
                new TableColumn("#", 0.6f, i => _slots[i].Overall.ToString(), false, null, false),
                new TableColumn("TEAM", 1.2f, i => CareerText.TeamNick(Save, _slots[i].TeamId), false, i => _slots[i].TeamId == Save.UserTeamId ? (Color?)GlassTheme.T.accent : null, false),
                new TableColumn("PLAYER", 2.2f, i => _slots[i].Made ? _slots[i].PlayerName : "", false, null, false),
                new TableColumn("POS", 0.6f, i => _slots[i].Made ? _slots[i].Pos.ToString() : "", false, null, false),
                new TableColumn("GRADE", 0.8f, i => _slots[i].Made ? _slots[i].Grade : "", true, null, false)
            }, 40f);
            Lay.Place((RectTransform)_picks.transform, 1076f, 410f, 724f, 300f);
            _picks.HandleSortKey = false; _picks.EmptyText = "The pick order is set when the draft starts.";

            // rookie signing
            var rc = new List<TableColumn>
            {
                new TableColumn("PICK", 0.8f, i => "#" + _rights[i].Overall, false, null, false),
                new TableColumn("ROOKIE", 2.6f, i => NameOf(_rights[i].PlayerId), false, null, false),
                new TableColumn("POS", 0.7f, i => PosOf(_rights[i].PlayerId), false, null, false),
                new TableColumn("OVR", 0.7f, i => OvrOf(_rights[i].PlayerId), true, null, false),
                new TableColumn("     ROOKIE DEAL", 2.2f, i => { var c = CareerDraft.RookieDeal(Save, _rights[i]); return "     " + Fmt.MoneyShort(c.Aav) + " x " + c.Years; }, false, null, false),
                new TableColumn("STATUS", 1.1f, i => _rights[i].Signed ? "SIGNED" : "UNSIGNED", true, i => _rights[i].Signed ? (Color?)GlassTheme.T.success : GlassTheme.T.warning, false)
            };
            _rookies = GlassTable.Create(root, "Rookies", 1060f, 602f, rc, 50f); Lay.Place((RectTransform)_rookies.transform, 0f, 108f, 1060f, 602f);
            _rookies.HandleSortKey = false; _rookies.EmptyText = "You made no picks.";
            _rookies.CursorChanged = i => ShowRookie();
            _rookies.Activated = i => { var r = CareerDraft.SignRookie(Save, _rights[i].PlayerId); Shell.Toast(r.Message, r.Ok ? "ui_check" : "ui_info"); Ctl.Touch(); };
            _rookieCard = PlayerCardPanel.Create(root, 1076f, 108f, 724f, 602f);

            _bar = Lay.Group(root, "Bar", 0f, 722f, 1800f, 78f);
        }

        string NameOf(int id) { var p = Save.FindPlayer(id); return p != null ? p.Name : "?"; }
        string PosOf(int id) { var p = Save.FindPlayer(id); return p != null ? p.Position.ToString() : ""; }
        string OvrOf(int id) { var p = Save.FindPlayer(id); return p != null ? p.Overall.ToString() : ""; }

        void SortBy(int col)
        {
            _sort = col == 0 ? "consensus" : col == 7 ? "starred" : "grade";
            Refresh();
        }

        public override Selectable Entry { get { return Rookies ? (Selectable)_rookies : _board; } }
        public override PromptItem[] Prompts()
        {
            if (Rookies) return new[] { PromptFooter.Item("glyph_a", "SIGN") };
            return new[] { PromptFooter.Item("glyph_a", CareerDraft.UserOnClock(Save) ? "DRAFT" : "SELECT"), PromptFooter.Item("glyph_x", "STAR"), PromptFooter.Item("glyph_y", "SORT") };
        }

        public override void OnHide() { if (_rookieCard != null) _rookieCard.DisposeStage(); }
        public override bool HandleBack() { return false; }

        public override void OnShow() { _clockPick = -1; Refresh(); }

        public override void Tick(float dt)
        {
            if (!Built || Save.Phase != CareerPhase.Draft || Save.Draft.Finished || !CareerDraft.UserOnClock(Save)) return;
            _clockLeft -= dt;
            if (_clockLeft <= 0f)
            {
                _clockLeft = ClockSeconds;
                Shell.Toast("The clock ran out: your pick was made for you.", "ui_info");
                AutoPick();
                return;
            }
            UpdateClockText();
        }

        void UpdateClockText()
        {
            var t = GlassTheme.T;
            bool onClock = Save.Phase == CareerPhase.Draft && !Save.Draft.Finished && CareerDraft.UserOnClock(Save);
            _clock.text = onClock ? DraftText.Clock(_clockLeft) : "";
            _clock.color = _clockLeft < 30f ? t.danger : t.textPrimary;
        }

        public override void Refresh()
        {
            if (!Built) return;
            var t = GlassTheme.T;
            bool rookies = Rookies;
            _board.gameObject.SetActive(!rookies); _posSel.gameObject.SetActive(!rookies); _detail.gameObject.SetActive(!rookies); _picks.gameObject.SetActive(!rookies);
            _rookies.gameObject.SetActive(rookies); _rookieCard.Root.gameObject.SetActive(rookies);
            _title.text = DraftText.Headline(Save);
            var cur = CareerDraft.Current(Save);
            bool onClock = Save.Phase == CareerPhase.Draft && cur != null && cur.TeamId == Save.UserTeamId;
            if (Save.Phase == CareerPhase.Scouting) _hint.text = "SPEND YOUR POINTS, STAR YOUR TARGETS, THEN START THE DRAFT";
            else if (Save.Phase == CareerPhase.Draft) _hint.text = Save.Draft.Finished ? "YOUR DRAFT GRADE: " + CareerDraft.TeamGrade(Save, Save.UserTeamId) : (cur != null ? DraftText.SlotLabel(cur) + "  -  " + CareerText.TeamName(Save, cur.TeamId).ToUpperInvariant() + " ON THE CLOCK" : "");
            else _hint.text = "YOUR DRAFT GRADE: " + CareerDraft.TeamGrade(Save, Save.UserTeamId) + "   -   ROOKIES NEED CONTRACTS";
            _hint.color = onClock ? t.accent : t.textSecondary;
            _points.text = "SCOUTING POINTS  " + CareerDraft.PointsRemaining(Save);
            if (onClock && _clockPick != cur.Overall) { _clockPick = cur.Overall; _clockLeft = ClockSeconds; }
            UpdateClockText();

            if (rookies)
            {
                _rights = CareerDraft.UnsignedRights(Save, Save.UserTeamId);
                foreach (var r in Save.RookieRights) if (r.TeamId == Save.UserTeamId && r.Signed && !_rights.Contains(r)) _rights.Add(r);
                _rights.Sort((a, b) => a.Overall.CompareTo(b.Overall));
                _rookies.SetCount(_rights.Count);
                ShowRookie();
            }
            else
            {
                int keep = Cur != null ? Cur.Id : -1;
                var all = CareerDraft.Board(Save, _sort, Save.Phase == CareerPhase.Draft);
                _rows = all.FindAll(r => PosFilter.Match(_posSel.Index, r.Pos));
                _board.SetCount(_rows.Count);
                _board.SetSort(_sort == "consensus" ? 0 : _sort == "starred" ? 7 : 5, true);
                int at = _rows.FindIndex(r => r.Id == keep); if (at >= 0) _board.SetCursor(at, false);
                _slots = Save.Draft.Slots;
                _picks.SetCount(_slots.Count);
                if (cur != null) _picks.SetCursor(Mathf.Max(0, cur.Overall - 1), false);
                else if (_slots.Count > 0) _picks.SetCursor(_slots.Count - 1, false);
                ShowProspect();
            }
            BuildBar();
        }

        // ---------------------------------------------------------------- prospect card
        void ShowProspect()
        {
            var t = GlassTheme.T;
            for (int i = _detail.childCount - 1; i >= 0; i--) UiKit.Kill(_detail.GetChild(i).gameObject);
            var card = GlassKit.Panel(_detail, "Card", 1.2f, true, t.cornerRadiusLarge); Lay.Place(card.rectTransform, 0f, 0f, 724f, 290f);
            var r = Cur;
            if (r == null) return;
            var cr = card.rectTransform;
            Lay.Caps(cr, "PROSPECT  -  RANK " + r.ConsensusRank, 26f, 16f, 500f, 26f, t.accent, 17f);
            Lay.Text(cr, r.Name.ToUpperInvariant(), 36f, t.textPrimary, 26f, 40f, 660f, 48f, true, TextAlignmentOptions.MidlineLeft, 2f, "Name");
            Lay.Text(cr, r.Pos + "  -  " + r.Age + " yrs  -  " + (string.IsNullOrEmpty(r.School) ? "unknown school" : r.School) + "  -  " + r.ProjectedRound, 20f, t.textSecondary, 26f, 88f, 660f, 28f, false);
            var sp = StackPanel.Create(cr, "S", 26f, 124f, 400f, 150f);
            sp.Line("Estimated overall", DraftText.Estimate(r), null, 30f, 21f);
            sp.Line("Estimated ceiling", Mathf.RoundToInt(r.EstPotential).ToString(), null, 30f, 21f);
            sp.Bar("Scouted", Mathf.Clamp01(r.PointsSpent / 12f), r.PointsSpent + " pts", r.PointsSpent > 0, 30f);
            sp.Line("Your grade", Mathf.RoundToInt(r.EstGrade).ToString(), t.accent, 30f, 21f);
            var ab = Lay.Group(cr, "Abilities", 444f, 124f, 244f, 150f);
            Lay.Caps(ab, "ABILITY HINTS", 0f, 0f, 240f, 24f, t.textSecondary, 15f);
            if (r.AbilityHints.Count == 0) Lay.Text(ab, "Scout him to learn more.", 19f, t.textMuted, 0f, 30f, 240f, 60f, false, TextAlignmentOptions.TopLeft, 0f, "None", false).textWrappingMode = TextWrappingModes.Normal;
            for (int i = 0; i < r.AbilityHints.Count && i < 4; i++)
            {
                var def = AbilityCatalog.Get(r.AbilityHints[i].Id);
                Lay.Text(ab, AbilityCatalog.Stars(r.AbilityHints[i].Tier) + " " + (def != null ? def.Name : r.AbilityHints[i].Id), 19f, t.textPrimary, 0f, 28f + i * 30f, 244f, 28f, true);
            }
        }

        void ShowRookie()
        {
            if (_rights.Count == 0) { _rookieCard.Fill(Save, null); return; }
            _rookieCard.Show(Save, _rights[Mathf.Clamp(_rookies.Cursor, 0, _rights.Count - 1)].PlayerId);
        }

        // ---------------------------------------------------------------- the action bar
        void BuildBar()
        {
            for (int i = _bar.childCount - 1; i >= 0; i--) UiKit.Kill(_bar.GetChild(i).gameObject);
            _bar1.Clear();
            var t = GlassTheme.T;
            float x = 0f;
            Func<string, Action, GlassButtonStyle, float, bool, Button> add = (label, act, style, w, enabled) =>
            {
                var b = GlassKit.Button(_bar, label, act, style, w, 70f, null, "B_" + label, 22f);
                Lay.Place((RectTransform)b.transform, x, 4f, w, 70f); x += w + 14f; b.interactable = enabled; _bar1.Add(b); return b;
            };
            var ph = Save.Phase;
            bool onClock = ph == CareerPhase.Draft && CareerDraft.UserOnClock(Save);
            bool live = ph == CareerPhase.Draft && !Save.Draft.Finished;
            if (ph == CareerPhase.Scouting)
            {
                add("SCOUT 2 POINTS", () => Scout(2), GlassButtonStyle.Secondary, 260f, Cur != null);
                add("SCOUT 5 POINTS", () => Scout(5), GlassButtonStyle.Secondary, 260f, Cur != null);
                add("START THE DRAFT", () => Go("Starting the draft"), GlassButtonStyle.Primary, 330f, true);
            }
            else if (live)
            {
                add(onClock ? "DRAFT SELECTED PLAYER" : "SIM TO MY PICK", onClock ? (Action)PickCurrent : () => Go("Simulating picks"), GlassButtonStyle.Primary, 380f, onClock ? Cur != null : true);
                add("AUTO PICK", AutoPick, GlassButtonStyle.Secondary, 220f, onClock);
                add("TRADE OFFERS", TradeOffers, GlassButtonStyle.Secondary, 260f, onClock);
                add("SCOUT 2 POINTS", () => Scout(2), GlassButtonStyle.Secondary, 240f, Cur != null);
                add("SIM TO THE END", SimEnd, GlassButtonStyle.Secondary, 240f, true);
            }
            else if (ph == CareerPhase.Draft)
            {
                add("FINISH THE DRAFT", () => Go("Wrapping up the draft"), GlassButtonStyle.Primary, 380f, true);
            }
            else if (ph == CareerPhase.RookieSigning)
            {
                add("SIGN ALL ROOKIES", () => { int n = CareerDraft.SignAllRookies(Save, Save.UserTeamId); Shell.Toast(Fmt.Plural(n, "rookie", "rookies") + " signed."); Ctl.Touch(); }, GlassButtonStyle.Primary, 340f, _rights.Exists(r => !r.Signed));
                add("OPEN FREE AGENCY", () => Go("Opening free agency"), GlassButtonStyle.Secondary, 320f, true);
            }
            add("LEAVE", () => Shell.CloseOverlay(true), GlassButtonStyle.Secondary, 200f, true);
            var tableSel = Rookies ? (Selectable)_rookies : _board;
            for (int i = 0; i < _bar1.Count; i++) NavWire.Set(_bar1[i], tableSel, null, i > 0 ? _bar1[i - 1] : null, i + 1 < _bar1.Count ? _bar1[i + 1] : null);
            NavWire.Set(_board, _posSel, _bar1.Count > 0 ? _bar1[0] : null, null, null);
            NavWire.Set(_posSel, null, _board, null, null);
            NavWire.Set(_rookies, null, _bar1.Count > 0 ? _bar1[0] : null, null, null);
            if (Application.isPlaying) GlassSkin.Apply(_bar);
        }

        void Go(string label)
        {
            Shell.Busy(label, () =>
            {
                var r = Ctl.Advance();
                if (!r.Ok) Shell.Toast(Ctl.Message);
                else if (Save.Phase == CareerPhase.RookieSigning) Shell.Toast("The draft is over. Sign your rookies.");
                else if (Save.Phase == CareerPhase.FreeAgency) Shell.CloseOverlay(true);
            });
        }

        void Scout(int pts)
        {
            var r = Cur; if (r == null) return;
            var res = CareerDraft.Scout(Save, r.Id, pts);
            Shell.Toast(res.Message, res.Ok ? "ui_check" : "ui_info"); Ctl.Touch();
        }

        void PickCurrent()
        {
            if (!CareerDraft.UserOnClock(Save)) { if (Save.Phase == CareerPhase.Scouting) Shell.Toast("The draft has not started."); else Shell.Toast("You are not on the clock."); return; }
            var r = Cur; if (r == null) return;
            Shell.Confirm("Draft " + r.Name + "?", r.Pos + ", estimated " + DraftText.Estimate(r) + ", projected " + r.ProjectedRound + ". Pick #" + CareerDraft.Current(Save).Overall + ".", "DRAFT", () =>
            {
                var res = CareerDraft.UserPick(Save, r.Id);
                Shell.Toast(res.Message, res.Ok ? "ui_check" : "ui_info");
                if (res.Ok) { _clockPick = -1; Ctl.Autosave(); Ctl.Touch(); }
            });
        }

        void AutoPick()
        {
            var rng = Save.NewRng();
            var s = CareerDraft.AutoPick(Save, rng);
            if (s != null) Shell.Toast("Pick #" + s.Overall + ": " + s.PlayerName + " (" + s.Pos + ", " + s.Grade + ")");
            Ctl.Touch();
        }

        void SimEnd()
        {
            Shell.Confirm("Sim the rest of the draft?", "Your remaining picks are made automatically.", "SIM", () => Shell.Busy("Simulating the draft", () =>
            {
                var rng = Save.NewRng();
                int n = 0; while (CareerDraft.UserOnClock(Save) && n++ < 300) CareerDraft.AutoPick(Save, rng);
                CareerDraft.SimToEnd(Save, rng);
                Ctl.Autosave(); Ctl.Touch();
            }));
        }

        void TradeOffers()
        {
            var offers = CareerDraft.TradeUpOffers(Save, Save.NewRng(), 3);
            var items = new List<SheetItem>();
            foreach (var o in offers)
            {
                var offer = o;
                var parts = new List<string>();
                foreach (var a in offer.Proposal.FromA) parts.Add(a.IsPick ? PickName(a.PickId) : NameOf(a.PlayerId));
                items.Add(new SheetItem(CareerText.TeamNick(Save, offer.FromTeamId) + " move up", () => { var r = TradeEngine.AcceptOffer(Save, offer.Id); Shell.Toast(r.Message, r.Ok ? "ui_check" : "ui_info"); Ctl.Touch(); }, string.Join(", ", parts.ToArray())));
            }
            if (items.Count == 0) items.Add(new SheetItem("No offers for this pick", null, "", false));
            ActionSheet.Show(Shell.Root, "Trade offers", "Teams that want to move up to your pick", items);
        }

        string PickName(int pickId) { var k = TradeEngine.FindPick(Save, pickId); return k != null ? TradeEngine.PickLabel(Save, k) : "pick"; }
    }
}
