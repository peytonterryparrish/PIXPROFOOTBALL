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
    /// <summary>Sorting of the cap ledger (pure, tested): 0 name, 1 position, 2 overall, 3 age, 4 years left, 5 average value, 6 cap hit, 7 next year, 8 guaranteed, 9 dead money if cut.</summary>
    public static class CapSort
    {
        public static void Sort(List<CapRow> rows, int col, bool desc)
        {
            Comparison<CapRow> c;
            switch (col)
            {
                case 0: c = (a, b) => string.CompareOrdinal(a.Name, b.Name); break;
                case 1: c = (a, b) => RosterSort.PosIndex(a.Pos).CompareTo(RosterSort.PosIndex(b.Pos)); break;
                case 2: c = (a, b) => a.Ovr.CompareTo(b.Ovr); break;
                case 3: c = (a, b) => a.Age.CompareTo(b.Age); break;
                case 4: c = (a, b) => a.YearsLeft.CompareTo(b.YearsLeft); break;
                case 5: c = (a, b) => a.Aav.CompareTo(b.Aav); break;
                case 7: c = (a, b) => a.CapHitNextYear.CompareTo(b.CapHitNextYear); break;
                case 8: c = (a, b) => a.Guaranteed.CompareTo(b.Guaranteed); break;
                case 9: c = (a, b) => a.DeadIfReleased.CompareTo(b.DeadIfReleased); break;
                default: c = (a, b) => a.CapHitThisYear.CompareTo(b.CapHitThisYear); break;
            }
            var idx = new List<KeyValuePair<int, CapRow>>();
            for (int i = 0; i < rows.Count; i++) idx.Add(new KeyValuePair<int, CapRow>(i, rows[i]));
            idx.Sort((x, y) => { int r = c(x.Value, y.Value); if (desc) r = -r; return r != 0 ? r : x.Key.CompareTo(y.Key); });
            for (int i = 0; i < rows.Count; i++) rows[i] = idx[i].Value;
        }
    }

    // ====================================================================================================== CAP & CONTRACTS
    /// <summary>OFFICE &gt; CAP &amp; CONTRACTS: the cap sheet (cap, used, space, dead money, next year), every contract with what a release would cost, restructure, franchise tag and auto-fix.</summary>
    public sealed class CapPage : FranchisePage
    {
        GlassTable _table; RectTransform _tiles; TextMeshProUGUI _banner; Button _autoFix;
        List<CapRow> _rows = new List<CapRow>();
        int _sortCol = 6; bool _desc = true;

        protected override void Build(RectTransform root)
        {
            _tiles = Lay.Group(root, "Tiles", 0f, 0f, Lay.W, 96f);
            _banner = Lay.Text(root, "", 22f, GlassTheme.T.textSecondary, 0f, 108f, 1250f, 48f, true, TextAlignmentOptions.MidlineLeft, 0f, "Banner"); Lay.Under(_banner, 16f, 0f);
            _autoFix = GlassKit.Button(root, "AUTO-FIX THE CAP", () => Shell.Busy("Fixing the cap", () => { int n = CapManager.AutoFix(Save, Ctl.User); Shell.Toast(n + " moves made to get under the cap."); Ctl.Touch(); }), GlassButtonStyle.Secondary, 380f, 52f, null, "AutoFix", 22f);
            Lay.Place((RectTransform)_autoFix.transform, Lay.W - 380f, 106f, 380f, 52f);
            var cols = new List<TableColumn>
            {
                new TableColumn("NAME", 2.5f, i => _rows[i].Name),
                new TableColumn("POS", 0.7f, i => _rows[i].Pos.ToString()),
                new TableColumn("OVR", 0.7f, i => _rows[i].Ovr.ToString(), true, i => Fmt.OvrColor(_rows[i].Ovr)),
                new TableColumn("AGE", 0.6f, i => _rows[i].Age.ToString(), true),
                new TableColumn("YRS", 0.6f, i => _rows[i].YearsLeft.ToString(), true, i => _rows[i].YearsLeft <= 1 ? (Color?)GlassTheme.T.warning : null),
                new TableColumn("PER YEAR", 1.1f, i => Fmt.MoneyShort(_rows[i].Aav), true),
                new TableColumn("CAP HIT", 1.1f, i => Fmt.MoneyShort(_rows[i].CapHitThisYear), true),
                new TableColumn("NEXT YEAR", 1.1f, i => Fmt.MoneyShort(_rows[i].CapHitNextYear), true),
                new TableColumn("GUARANTEED", 1.2f, i => Fmt.MoneyShort(_rows[i].Guaranteed), true),
                new TableColumn("DEAD IF CUT", 1.2f, i => Fmt.MoneyShort(_rows[i].DeadIfReleased), true),
                new TableColumn("STATUS", 1.3f, i => _rows[i].Status, true, null, false)
            };
            _table = GlassTable.Create(root, "CapTable", Lay.W, Lay.H - 162f, cols);
            Lay.Place((RectTransform)_table.transform, 0f, 162f, Lay.W, Lay.H - 162f);
            _table.Activated = i => OpenActions(_rows[i]);
            _table.HeaderClicked = SortBy; _table.SortRequested = SortBy;
        }

        public override Selectable Entry { get { return _table; } }
        public override PromptItem[] Prompts() { return new[] { PromptFooter.Item("glyph_a", "ACTIONS"), PromptFooter.Item("glyph_y", "SORT") }; }

        void SortBy(int col)
        {
            if (col == _sortCol) _desc = !_desc; else { _sortCol = col; _desc = col != 0 && col != 1; }
            Refresh();
        }

        public override void Refresh()
        {
            if (!Built) return;
            var t = GlassTheme.T; var u = Ctl.User;
            var rep = CapManager.Report(Save, u);
            for (int i = _tiles.childCount - 1; i >= 0; i--) UiKit.Kill(_tiles.GetChild(i).gameObject);
            string[] vals = { Fmt.MoneyShort(rep.Cap), Fmt.MoneyShort(rep.Used), Fmt.MoneyShort(rep.Space), Fmt.MoneyShort(rep.Dead), Fmt.MoneyShort(rep.CommittedNextYear) };
            string[] caps = { "SALARY CAP", "CAP USED", "CAP SPACE", "DEAD MONEY", "NEXT YEAR" };
            float w = (Lay.W - 4f * 14f) / 5f;
            for (int i = 0; i < 5; i++)
            {
                var tile = GlassWidgets.StatTile(_tiles, vals[i], caps[i], w, 96f, i == 2);
                Lay.Unlayout(tile.gameObject); Lay.Place(tile, i * (w + 14f), 0f, w, 96f);
                if (i == 2 && rep.Space < 0) { var v = tile.Find("Value"); if (v != null) v.GetComponent<TextMeshProUGUI>().color = t.danger; }
            }
            int keep = _rows.Count > 0 && _table.Cursor < _rows.Count ? _rows[_table.Cursor].PlayerId : -1;
            _rows = CapManager.LedgerRows(u);
            CapSort.Sort(_rows, _sortCol, _desc);
            _table.SetCount(_rows.Count); _table.SetSort(_sortCol, _desc);
            int at = _rows.FindIndex(r => r.PlayerId == keep); if (at >= 0) _table.SetCursor(at, false);
            _autoFix.interactable = !rep.Compliant;
            string tag = Save.Phase == CareerPhase.ReSign ? (CapManager.TagAvailable(Save, u.Id) ? "   Franchise tag: available." : "   Franchise tag: used this year.") : "";
            _banner.text = (rep.Compliant ? "You are " + Fmt.MoneyShort(rep.Space) + " under the cap." : "You are " + Fmt.MoneyShort(-rep.Space) + " OVER the cap.") + tag;
            _banner.color = rep.Compliant ? t.textSecondary : t.danger;
        }

        void OpenActions(CapRow r)
        {
            var p = Save.FindPlayer(r.PlayerId); if (p == null) return;
            var u = Ctl.User;
            var items = new List<SheetItem>();
            items.Add(new SheetItem("Restructure the contract", () => Do(CapManager.Restructure(Save, u, p.Id)), "half of this year's base becomes bonus", r.YearsLeft >= 2));
            bool tag = Save.Phase == CareerPhase.ReSign && p.Contract.YearsLeft <= 1 && CapManager.TagAvailable(Save, u.Id);
            items.Add(new SheetItem("Franchise tag", () => Do(CapManager.FranchiseTag(Save, u, p.Id)), tag ? Fmt.MoneyShort(CapManager.TagCost(Save, p)) + " for one year" : "re-sign window, once a year", tag));
            items.Add(new SheetItem("Contract talks", () => Shell.ShowTab(FranchiseNav.Office, FranchiseNav.ReSign), "", r.YearsLeft <= 1));
            long now, next; CapManager.DeadMoneyPreview(p.Contract, out now, out next);
            items.Add(new SheetItem("Release", () => Shell.Confirm("Release " + p.Name + "?", "Dead money " + Fmt.MoneyShort(now) + " now" + (next > 0 ? ", " + Fmt.MoneyShort(next) + " next year" : "") + ". Space gained now: " + Fmt.MoneyShort(CapManager.SavingsIfReleased(p)) + ".", "RELEASE", () => Do(RosterManager.Release(Save, u, p.Id))), "dead money " + Fmt.MoneyShort(now) + " now", true, true));
            ActionSheet.Show(Shell.Root, p.Name, p.Position + "  -  cap hit " + Fmt.MoneyShort(r.CapHitThisYear) + "  -  " + Fmt.Years(r.YearsLeft) + " left", items);
        }

        void Do(OpResult r) { Shell.Toast(r.Message, r.Ok ? "ui_check" : "ui_info"); Ctl.Touch(); }
    }

    // ====================================================================================================== RE-SIGN
    /// <summary>OFFICE &gt; RE-SIGN: expiring contracts (extensions in the final year during the season). Pick a player, read what he wants, set years / money / bonus and submit: accepted, counter, declined or he walks away; the franchise tag is here too.</summary>
    public sealed class ReSignPage : FranchisePage
    {
        public static int Wanted;
        GlassTable _table; OfferEditor _editor; Button _submit, _tag;
        RectTransform _info; TextMeshProUGUI _resp, _header, _sub;
        List<Player> _players = new List<Player>();
        ContractAsk _ask; string _verdict = ""; Color _verdictColor;
        Player Cur { get { return _players.Count == 0 ? null : _players[Mathf.Clamp(_table.Cursor, 0, _players.Count - 1)]; } }

        protected override void Build(RectTransform root)
        {
            var t = GlassTheme.T;
            var cols = new List<TableColumn>
            {
                new TableColumn("NAME", 2.6f, i => _players[i].Name, false, null, false),
                new TableColumn("POS", 0.8f, i => _players[i].Position.ToString(), false, null, false),
                new TableColumn("OVR", 0.8f, i => _players[i].Overall.ToString(), true, i => Fmt.OvrColor(_players[i].Overall), false),
                new TableColumn("AGE", 0.6f, i => _players[i].Age.ToString(), true, null, false),
                new TableColumn("MOR", 0.7f, i => _players[i].Morale.ToString(), true, null, false),
                new TableColumn("STATUS", 1.5f, i => StatusOf(_players[i]), true, i => StatusTint(_players[i]), false)
            };
            _table = GlassTable.Create(root, "ReSignTable", 840f, Lay.H, cols);
            Lay.Place((RectTransform)_table.transform, 0f, 0f, 840f, Lay.H);
            _table.EmptyText = "Nobody's contract is up.";
            _table.CursorChanged = i => SelectCurrent();
            _table.HandleSortKey = false;

            var card = GlassKit.Panel(root, "OfferCard", 1.2f, true, t.cornerRadiusLarge); Lay.Place(card.rectTransform, 856f, 0f, Lay.W - 856f, Lay.H);
            var cr = card.rectTransform;
            _header = Lay.Text(cr, "", 38f, t.textPrimary, 30f, 20f, Lay.W - 916f, 52f, true, TextAlignmentOptions.MidlineLeft, 2f, "Header");
            _sub = Lay.Text(cr, "", 22f, t.textSecondary, 30f, 70f, Lay.W - 916f, 32f, false, TextAlignmentOptions.MidlineLeft, 0f, "Sub");
            _info = Lay.Group(cr, "Info", 30f, 112f, Lay.W - 916f, 196f);
            Lay.Caps(cr, "YOUR OFFER", 30f, 318f, 400f, 26f, t.textSecondary, 17f);
            _editor = OfferEditor.Build(cr, 30f, 348f, Lay.W - 916f, 54f);
            _editor.Changed = () => { };
            _resp = Lay.Text(cr, "", 22f, t.textPrimary, 30f, Lay.H - 190f, Lay.W - 916f, 54f, true, TextAlignmentOptions.TopLeft, 0f, "Response", false);
            _resp.textWrappingMode = TextWrappingModes.Normal;
            _submit = GlassKit.Button(cr, "SUBMIT OFFER", Submit, GlassButtonStyle.Primary, 400f, 66f, "ui_arrow_right", "Submit", 26f); Lay.Place((RectTransform)_submit.transform, 30f, Lay.H - 82f, 400f, 66f);
            _tag = GlassKit.Button(cr, "FRANCHISE TAG", UseTag, GlassButtonStyle.Secondary, 400f, 66f, null, "Tag", 24f); Lay.Place((RectTransform)_tag.transform, 450f, Lay.H - 82f, 400f, 66f);
        }

        string StatusOf(Player p)
        {
            var n = Negotiator.StateOf(Save, Ctl.User.Id, p.Id, false);
            if (n != null && n.State == NegotiationState.Walked) return "WALKED AWAY";
            if (n != null && n.State == NegotiationState.Declined) return "DECLINED";
            var ask = Negotiator.Ask(Save, Ctl.User, p);
            if (!ask.WillNegotiate) return "TESTING THE MARKET";
            return n != null && n.Rounds > 0 ? "ROUND " + n.Rounds + " OF " + Negotiator.MaxRounds : Fmt.MoneyShort(ask.Aav) + " x " + ask.Years;
        }

        Color? StatusTint(Player p)
        {
            var n = Negotiator.StateOf(Save, Ctl.User.Id, p.Id, false);
            var t = GlassTheme.T;
            if (n != null && (n.State == NegotiationState.Walked || n.State == NegotiationState.Declined)) return t.danger;
            return null;
        }

        public override Selectable Entry { get { return _table; } }
        public override PromptItem[] Prompts() { return new PromptItem[0]; }

        public override void OnShow()
        {
            Refresh();
            if (Wanted > 0) { int at = _players.FindIndex(p => p.Id == Wanted); if (at >= 0) _table.SetCursor(at); Wanted = 0; }
        }

        public override void Refresh()
        {
            if (!Built) return;
            int keep = Cur != null ? Cur.Id : -1;
            _players = Negotiator.ExpiringPlayers(Save, Ctl.User);
            _players.Sort((a, b) => b.Overall.CompareTo(a.Overall));
            _table.SetCount(_players.Count);
            int at = _players.FindIndex(p => p.Id == keep); if (at >= 0) _table.SetCursor(at, false);
            SelectCurrent();
        }

        void SelectCurrent()
        {
            var t = GlassTheme.T;
            for (int i = _info.childCount - 1; i >= 0; i--) UiKit.Kill(_info.GetChild(i).gameObject);
            var p = Cur;
            bool window = Save.Phase == CareerPhase.ReSign;
            _submit.interactable = p != null; _tag.interactable = p != null && window && CapManager.TagAvailable(Save, Ctl.User.Id);
            if (p == null)
            {
                _header.text = "NO EXPIRING CONTRACTS"; _sub.text = window ? "Everybody is signed for next year." : "Extensions open for players in the last year of their deal.";
                _resp.text = ""; return;
            }
            _ask = Negotiator.Ask(Save, Ctl.User, p);
            _header.text = p.Name.ToUpperInvariant();
            _sub.text = p.Position + "  -  " + p.Overall + " OVR  -  " + p.Age + " yrs  -  " + (window ? "re-sign window" : "extension (final contract year)");
            var sp = StackPanel.Create(_info, "Stack", 0f, 0f, Lay.W - 916f, 196f);
            sp.Line("He wants", Fmt.MoneyShort(_ask.Aav) + " per year, " + Fmt.Years(_ask.Years), t.textPrimary, 32f, 22f);
            sp.Bar("Interest in staying", _ask.Interest / 100f, _ask.Interest.ToString(), _ask.Interest >= 60, 32f);
            sp.Line("Mood", _ask.Mood, _ask.WillNegotiate ? t.textPrimary : t.danger, 32f, 21f);
            for (int i = 0; i < _ask.Notes.Count && i < 2; i++) sp.Para(_ask.Notes[i], 19f, t.textMuted, 26f);
            _editor.Set(_ask.Aav, _ask.Years, _ask.BonusPct, CapMath.MinSalary(Save, Save.Year));
            _resp.text = _verdict; _resp.color = _verdictColor;
            if (_tag.interactable) _tag.GetComponentInChildren<TextMeshProUGUI>().text = "FRANCHISE TAG  " + Fmt.MoneyShort(CapManager.TagCost(Save, p));
            else _tag.GetComponentInChildren<TextMeshProUGUI>().text = "FRANCHISE TAG";
            _verdict = "";
        }

        void Submit()
        {
            var p = Cur; if (p == null) return;
            var t = GlassTheme.T;
            var r = Negotiator.Offer(Save, Ctl.User, p.Id, _editor.Aav, _editor.Years, _editor.BonusPct);
            _verdict = r.Message;
            _verdictColor = r.Verdict == OfferVerdict.Accepted ? t.success : r.Verdict == OfferVerdict.Counter ? t.warning : t.danger;
            Shell.Toast(r.Message, r.Accepted ? "ui_check" : "ui_info");
            Ctl.Touch();
            // a counter offer: put it in the editor so one more press accepts it
            if (r.Verdict == OfferVerdict.Counter && Cur != null && Cur.Id == p.Id)
            {
                _editor.Set(r.CounterAav, r.CounterYears, _ask != null ? _ask.BonusPct : 0.2f, CapMath.MinSalary(Save, Save.Year));
                _resp.text = r.Message; _resp.color = _verdictColor;
            }
        }

        void UseTag()
        {
            var p = Cur; if (p == null) return;
            var r = CapManager.FranchiseTag(Save, Ctl.User, p.Id);
            Shell.Toast(r.Message, r.Ok ? "ui_check" : "ui_info"); Ctl.Touch();
        }
    }

    // ====================================================================================================== STAFF
    /// <summary>OFFICE &gt; STAFF: the head coach and both coordinators with their ratings, schemes and contracts, fire and hire from the market. A vacancy blocks the end of camp.</summary>
    public sealed class StaffPage : FranchisePage
    {
        static float CardW { get { return (Lay.W - 48f) / 3f; } }
        RectTransform _cards; GlassTable _table; OptionSelector _roleSel; TextMeshProUGUI _pay;
        List<CoachCandidate> _cands = new List<CoachCandidate>();
        readonly List<Button> _fireButtons = new List<Button>();

        protected override void Build(RectTransform root)
        {
            _cards = Lay.Group(root, "Cards", 0f, 0f, Lay.W, 262f);
            _roleSel = GlassKit.Stepper(root, "Market", new[] { "All roles", "Head coach", "Offensive coordinator", "Defensive coordinator" }, 0, i => Refresh(), 640f, 52f); Lay.Place((RectTransform)_roleSel.transform, 0f, 274f, 640f, 52f);
            _pay = Lay.Text(root, "", 21f, GlassTheme.T.textPrimary, 700f, 274f, Lay.W - 700f, 52f, true, TextAlignmentOptions.MidlineRight, 0f, "Payroll"); Lay.Under(_pay, 14f, 0f);
            var cols = new List<TableColumn>
            {
                new TableColumn("NAME", 2.4f, i => _cands[i].Coach.Name, false, null, false),
                new TableColumn("ROLE", 1.8f, i => StaffManager.RoleName(_cands[i].Coach.Role).ToUpperInvariant(), false, null, false),
                new TableColumn("AGE", 0.6f, i => _cands[i].Coach.Age.ToString(), true, null, false),
                new TableColumn("OFF", 0.6f, i => _cands[i].Coach.OffenseRating.ToString(), true, null, false),
                new TableColumn("DEF", 0.6f, i => _cands[i].Coach.DefenseRating.ToString(), true, null, false),
                new TableColumn("DEV", 0.6f, i => _cands[i].Coach.Development.ToString(), true, null, false),
                new TableColumn("SCHEMES", 2.4f, i => _cands[i].Coach.OffScheme + " / " + _cands[i].Coach.DefScheme, true, null, false),
                new TableColumn("ASKS", 1.6f, i => Fmt.MoneyShort(_cands[i].AskSalary) + " x " + _cands[i].Years, true, null, false)
            };
            _table = GlassTable.Create(root, "CoachTable", Lay.W, Lay.H - 338f, cols, 48f);
            Lay.Place((RectTransform)_table.transform, 0f, 338f, Lay.W, Lay.H - 338f);
            _table.EmptyText = "Nobody is on the market.";
            _table.Activated = i => ConfirmHire(_cands[i]);
            _table.HandleSortKey = false;
        }

        public override Selectable Entry { get { return _table; } }
        public override PromptItem[] Prompts() { return new[] { PromptFooter.Item("glyph_a", "HIRE") }; }

        public override void Refresh()
        {
            if (!Built) return;
            var t = GlassTheme.T; var u = Ctl.User;
            for (int i = _cards.childCount - 1; i >= 0; i--) UiKit.Kill(_cards.GetChild(i).gameObject);
            _fireButtons.Clear();
            CoachRole[] roles = { CoachRole.HeadCoach, CoachRole.OffensiveCoordinator, CoachRole.DefensiveCoordinator };
            for (int k = 0; k < 3; k++)
            {
                var role = roles[k]; var c = u.FindCoach(role);
                var card = GlassKit.Panel(_cards, "Card" + k, 1.15f, true, t.cornerRadiusLarge); Lay.Place(card.rectTransform, k * (CardW + 24f), 0f, CardW, 262f);
                var cr = card.rectTransform;
                Lay.Caps(cr, StaffManager.RoleName(role), 26f, 16f, 500f, 26f, t.accent, 17f);
                if (c == null)
                {
                    Lay.Text(cr, "VACANT", 40f, t.warning, 26f, 56f, 500f, 54f, true, TextAlignmentOptions.MidlineLeft, 3f, "Vacant");
                    Lay.Text(cr, "Hire somebody from the market below before camp ends.", 21f, t.textSecondary, 26f, 118f, 510f, 60f, false, TextAlignmentOptions.TopLeft, 0f, "Hint", false).textWrappingMode = TextWrappingModes.Normal;
                    continue;
                }
                var sc = StaffManager.ContractOf(Save, u.Id, c.Id);
                Lay.Text(cr, c.Name, 32f, t.textPrimary, 26f, 42f, 508f, 44f, true, TextAlignmentOptions.MidlineLeft, 1f, "Name");
                Lay.Text(cr, c.Age + " yrs   " + c.OffScheme + " offense / " + c.DefScheme + " defense", 19f, t.textSecondary, 26f, 86f, 510f, 28f, false, TextAlignmentOptions.MidlineLeft, 0f, "Scheme");
                var sp = StackPanel.Create(cr, "Bars", 26f, 118f, 508f, 100f);
                sp.Bar("Offense", c.OffenseRating / 99f, c.OffenseRating.ToString(), role != CoachRole.DefensiveCoordinator, 28f);
                sp.Bar("Defense", c.DefenseRating / 99f, c.DefenseRating.ToString(), role == CoachRole.DefensiveCoordinator, 28f);
                sp.Bar("Develop", c.Development / 99f, c.Development.ToString(), false, 28f);
                Lay.Text(cr, sc != null ? Fmt.MoneyShort(sc.Salary) + " for " + Fmt.Years(sc.YearsLeft) : "", 20f, t.textSecondary, 26f, 222f, 300f, 28f, false, TextAlignmentOptions.MidlineLeft, 0f, "Pay");
                int kk = k;
                var fire = GlassKit.Button(cr, "FIRE", () => Shell.Confirm("Fire " + c.Name + "?", "The rest of his contract is paid out. The job stays open until you hire somebody.", "FIRE", () => { var r = StaffManager.Fire(Save, u, role); Shell.Toast(r.Message); Ctl.Touch(); }), GlassButtonStyle.Secondary, 150f, 44f, null, "Fire", 20f);
                Lay.Place((RectTransform)fire.transform, CardW - 26f - 150f, 262f - 56f, 150f, 44f); _fireButtons.Add(fire);
            }
            long pay = StaffManager.Payroll(Save, u), bud = StaffManager.Budget(Save, u);
            int offFit = StaffManager.TeamSchemeFit(u, true), defFit = StaffManager.TeamSchemeFit(u, false);
            _pay.text = "Payroll " + Fmt.MoneyShort(pay) + " of " + Fmt.MoneyShort(bud) + "   -   scheme fit: offense " + offFit + ", defense " + defFit;
            _cands.Clear();
            foreach (var c in Save.CoachPool)
            {
                if (_roleSel.Index == 1 && c.Coach.Role != CoachRole.HeadCoach) continue;
                if (_roleSel.Index == 2 && c.Coach.Role != CoachRole.OffensiveCoordinator) continue;
                if (_roleSel.Index == 3 && c.Coach.Role != CoachRole.DefensiveCoordinator) continue;
                _cands.Add(c);
            }
            _cands.Sort((a, b) => StaffManager.CandidateValue(b.Coach).CompareTo(StaffManager.CandidateValue(a.Coach)));
            _table.SetCount(_cands.Count);
        }

        void ConfirmHire(CoachCandidate cand)
        {
            var u = Ctl.User; var existing = u.FindCoach(cand.Coach.Role);
            string msg = cand.Coach.Name + " asks " + Fmt.MoneyShort(cand.AskSalary) + " for " + Fmt.Years(cand.Years) + "." + (existing != null ? " " + existing.Name + " is let go." : "");
            Shell.Confirm("Hire " + cand.Coach.Name + "?", msg, "HIRE", () => { var r = StaffManager.Hire(Save, u, cand.Coach.Id); Shell.Toast(r.Message, r.Ok ? "ui_check" : "ui_info"); Ctl.Touch(); });
        }
    }

    // ====================================================================================================== OWNER
    /// <summary>OFFICE &gt; OWNER: the owner's trust and goals, job security, past seasons and the legacy score with its itemised lines.</summary>
    public sealed class OwnerPage : FranchisePage
    {
        RectTransform _left, _right;

        protected override void Build(RectTransform root)
        {
            _left = Lay.Group(root, "Left", 0f, 0f, 840f, Lay.H);
            _right = Lay.Group(root, "Right", 856f, 0f, Lay.W - 856f, Lay.H);
        }

        public override void Refresh()
        {
            if (!Built) return;
            var t = GlassTheme.T; var u = Ctl.User; var home = Ctl.Home();
            for (int i = _left.childCount - 1; i >= 0; i--) UiKit.Kill(_left.GetChild(i).gameObject);
            for (int i = _right.childCount - 1; i >= 0; i--) UiKit.Kill(_right.GetChild(i).gameObject);
            var c1 = GlassWidgets.Card(_left, "THE OWNER", 840f, Lay.H, "OwnerCard"); Lay.Unlayout(c1.Root.gameObject); Lay.Place(c1.Root, 0f, 0f, 840f, Lay.H);
            var sp = StackPanel.Create(c1.Body, "Stack", 0f, 0f, 784f, Lay.H - 106f);
            sp.Bar("Trust", home.OwnerSatisfaction / 100f, home.OwnerSatisfaction.ToString(), true, 50f);
            sp.Line("Job security", home.JobSecurity, home.OwnerSatisfaction < 35 ? t.danger : t.textPrimary, 50f, 28f);
            sp.Rule();
            sp.Caps("GOALS FOR " + Save.Year);
            if (home.Goals.Count == 0) sp.Para("The owner sets goals when the season starts.", 22f);
            foreach (var g in home.Goals)
            {
                string mark = g.Resolved ? (g.Met ? "MET" : "MISSED") : (g.Target > 0 ? g.Actual + " / " + g.Target : "open");
                sp.Line(g.Text, mark, g.Resolved ? (g.Met ? t.success : t.danger) : t.textSecondary, 46f, 26f);
            }
            sp.Gap(8f); sp.Rule();
            sp.Caps("PAST SEASONS");
            int shown = 0;
            for (int i = Save.TeamSeasons.Count - 1; i >= 0 && shown < 5; i--)
            {
                var r = Save.TeamSeasons[i]; if (r.TeamId != u.Id) continue;
                sp.Line(r.Year + "   " + r.Wins + "-" + r.Losses + (r.Ties > 0 ? "-" + r.Ties : ""), r.Result == PlayoffResult.Champion ? "PIX BOWL CHAMPIONS" : r.Result.ToString().ToUpperInvariant(), r.Result == PlayoffResult.Champion ? t.accent : t.textSecondary, 44f, 26f);
                shown++;
            }
            if (shown == 0) sp.Para("No seasons played yet.", 22f, t.textMuted);

            var leg = HistoryEngine.Legacy(Save, u.Id);
            var c2 = GlassWidgets.Card(_right, "LEGACY", Lay.W - 856f, Lay.H, "LegacyCard"); Lay.Unlayout(c2.Root.gameObject); Lay.Place(c2.Root, 0f, 0f, Lay.W - 856f, Lay.H);
            Lay.Text(c2.Body, leg.Score.ToString(), 90f, t.accent, 0f, 0f, 300f, 110f, true, TextAlignmentOptions.MidlineLeft, 0f, "Score");
            Lay.Text(c2.Body, leg.Tier.ToUpperInvariant(), 32f, t.textPrimary, 300f, 18f, 500f, 50f, true, TextAlignmentOptions.MidlineLeft, 4f, "Tier");
            Lay.Text(c2.Body, "Titles, playoff runs, wins, awards and records of " + u.Nickname + ".", 20f, t.textSecondary, 300f, 66f, 500f, 36f, false, TextAlignmentOptions.MidlineLeft, 0f, "Blurb");
            var s2 = StackPanel.Create(c2.Body, "Lines", 0f, 128f, Lay.W - 856f - 56f, Lay.H - 226f);
            foreach (var kv in leg.Lines) if (kv.Value != 0 && s2.Fits(36f)) s2.Line(kv.Key, (kv.Value > 0 ? "+" : "") + kv.Value, kv.Value > 0 ? t.textPrimary : t.danger, 34f, 21f);
        }
    }
}
