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
    /// <summary>Sorting of the roster table (pure, tested): column ids 0 jersey, 1 name, 2 position, 3 overall, 4 age, 5 cap hit, 6 morale, 7 status.</summary>
    public static class RosterSort
    {
        public static readonly string[] Names = { "#", "NAME", "POS", "OVR", "AGE", "CONTRACT", "MORALE", "STATUS" };

        public static int PosIndex(Position p) { return Array.IndexOf(LeagueRules.RosterOrder, p); }

        public static void Sort(List<RosterRow> rows, int col, bool descending)
        {
            Comparison<RosterRow> cmp;
            switch (col)
            {
                case 0: cmp = (a, b) => a.Jersey.CompareTo(b.Jersey); break;
                case 1: cmp = (a, b) => string.CompareOrdinal(a.Name, b.Name); break;
                case 2: cmp = (a, b) => { int c = PosIndex(a.Pos).CompareTo(PosIndex(b.Pos)); return c != 0 ? c : b.Ovr.CompareTo(a.Ovr); }; break;
                case 3: cmp = (a, b) => a.Ovr.CompareTo(b.Ovr); break;
                case 4: cmp = (a, b) => a.Age.CompareTo(b.Age); break;
                case 5: cmp = (a, b) => a.CapHit.CompareTo(b.CapHit); break;
                case 6: cmp = (a, b) => a.Morale.CompareTo(b.Morale); break;
                case 7: cmp = (a, b) => { int c = ((int)a.Status).CompareTo((int)b.Status); return c != 0 ? c : (b.InjuryWeeks).CompareTo(a.InjuryWeeks); }; break;
                default: cmp = (a, b) => { int c = ((int)a.Status).CompareTo((int)b.Status); if (c != 0) return c; c = PosIndex(a.Pos).CompareTo(PosIndex(b.Pos)); return c != 0 ? c : b.Ovr.CompareTo(a.Ovr); }; break;
            }
            // stable: ties keep the engine's own order (position, then overall)
            var idx = new List<KeyValuePair<int, RosterRow>>();
            for (int i = 0; i < rows.Count; i++) idx.Add(new KeyValuePair<int, RosterRow>(i, rows[i]));
            idx.Sort((x, y) => { int c = cmp(x.Value, y.Value); if (descending && col >= 0) c = -c; return c != 0 ? c : x.Key.CompareTo(y.Key); });
            for (int i = 0; i < rows.Count; i++) rows[i] = idx[i].Value;
        }

        /// <summary>The first sort direction of a column: numbers high to low, names and positions A to Z.</summary>
        public static bool DefaultDescending(int col) { return col == 3 || col == 4 || col == 5 || col == 6 || col == 7; }
    }

    // ====================================================================================================== ROSTER
    /// <summary>TEAM &gt; ROSTER: every player under contract (active, practice squad, injured reserve) in a sortable list with the player card beside it; A opens the actions of the player.</summary>
    public sealed class RosterPage : FranchisePage
    {
        GlassTable _table; PlayerCardPanel _card; OptionSelector _posSel, _showSel; TextMeshProUGUI _count;
        List<RosterRow> _rows = new List<RosterRow>();
        int _sortCol = -1; bool _desc;

        protected override void Build(RectTransform root)
        {
            var t = GlassTheme.T;
            _posSel = GlassKit.Stepper(root, "Position", PosFilter.Names, 0, i => Refresh(), 380f, 56f); Lay.Place((RectTransform)_posSel.transform, 0f, 0f, 380f, 56f);
            _showSel = GlassKit.Stepper(root, "Show", new[] { "Everyone", "Active", "Starters" }, 0, i => Refresh(), 380f, 56f); Lay.Place((RectTransform)_showSel.transform, 396f, 0f, 380f, 56f);
            _count = Lay.Text(root, "", 22f, t.textPrimary, 800f, 0f, 300f, 56f, false, TextAlignmentOptions.MidlineRight, 0f, "Count"); Lay.Under(_count, 10f, 0f);
            var cols = new List<TableColumn>
            {
                new TableColumn("#", 0.55f, i => _rows[i].Jersey.ToString(), false),
                new TableColumn("NAME", 2.7f, i => _rows[i].Name),
                new TableColumn("POS", 0.8f, i => _rows[i].Pos.ToString()),
                new TableColumn("OVR", 0.8f, i => _rows[i].Ovr.ToString(), true, i => Fmt.OvrColor(_rows[i].Ovr)),
                new TableColumn("AGE", 0.65f, i => _rows[i].Age.ToString(), true),
                new TableColumn("CONTRACT", 1.9f, i => _rows[i].YearsLeft + "y  " + Fmt.MoneyShort(_rows[i].CapHit), true, i => _rows[i].YearsLeft <= 1 ? (Color?)GlassTheme.T.warning : null),
                new TableColumn("MOR", 0.7f, i => _rows[i].Morale.ToString(), true),
                new TableColumn("STATUS", 1.35f, i => StatusText(_rows[i]), true)
            };
            _table = GlassTable.Create(root, "RosterTable", 1100f, Lay.H - 68f, cols);
            Lay.Place((RectTransform)_table.transform, 0f, 68f, 1100f, Lay.H - 68f);
            _table.RowTint = i => _rows[i].Injured ? (Color?)t.danger : _rows[i].Status != RosterStatus.Active ? (Color?)t.textSecondary : null;
            _table.CursorChanged = i => ShowCard();
            _table.Activated = i => OpenActions(_rows[i]);
            _table.HeaderClicked = SortBy; _table.SortRequested = SortBy;
            _card = PlayerCardPanel.Create(root, 1116f, 0f, Lay.W - 1116f, Lay.H);
        }

        static string StatusText(RosterRow r)
        {
            if (r.Injured) return (r.Status == RosterStatus.InjuredReserve ? "IR " : "INJ ") + r.InjuryWeeks + "w";
            return r.Status == RosterStatus.Active ? (r.Starter ? "STARTER" : "") : Fmt.Status(r.Status);
        }

        void SortBy(int col)
        {
            if (col == _sortCol) _desc = !_desc; else { _sortCol = col; _desc = RosterSort.DefaultDescending(col); }
            Refresh();
        }

        public override Selectable Entry { get { return _table; } }

        public override PromptItem[] Prompts() { return new[] { PromptFooter.Item("glyph_a", "ACTIONS"), PromptFooter.Item("glyph_y", "SORT") }; }

        public override void OnHide() { if (_card != null) _card.DisposeStage(); }

        public override void Refresh()
        {
            if (!Built) return;
            int keep = _rows.Count > 0 && _table.Cursor < _rows.Count ? _rows[_table.Cursor].PlayerId : -1;
            var all = CareerViews.RosterTable(Save, Ctl.User, true);
            _rows = new List<RosterRow>();
            int show = _showSel.Index;
            foreach (var r in all)
            {
                if (!PosFilter.Match(_posSel.Index, r.Pos)) continue;
                if (show >= 1 && r.Status != RosterStatus.Active) continue;
                if (show == 2 && !r.Starter) continue;
                _rows.Add(r);
            }
            RosterSort.Sort(_rows, _sortCol, _desc);
            _table.SetCount(_rows.Count);
            _table.SetSort(_sortCol, _desc);
            int at = _rows.FindIndex(r => r.PlayerId == keep);
            _table.SetCursor(at >= 0 ? at : Mathf.Min(_table.Cursor, Mathf.Max(0, _rows.Count - 1)), false);
            var rep = CapManager.Report(Save, Ctl.User);
            _count.text = "Active " + rep.ActiveCount + "/" + RosterRules.ActiveLimit + "   Squad " + rep.PracticeSquadCount + "   IR " + rep.InjuredReserveCount;
            ShowCard();
        }

        void ShowCard()
        {
            if (_rows.Count == 0) { _card.Fill(Save, null); return; }
            _card.Show(Save, _rows[Mathf.Clamp(_table.Cursor, 0, _rows.Count - 1)].PlayerId);
        }

        // ---------------------------------------------------------------- actions
        void OpenActions(RosterRow r)
        {
            var p = Save.FindPlayer(r.PlayerId); if (p == null) return;
            var u = Ctl.User;
            var items = new List<SheetItem>();
            bool expiring = r.YearsLeft <= 1;
            items.Add(new SheetItem(expiring ? "Extend contract" : "Contract talks", () => Shell.ShowTab(FranchiseNav.Office, FranchiseNav.ReSign), expiring ? "his deal ends this year" : "only final-year deals", expiring));
            bool active = r.Status == RosterStatus.Active;
            items.Add(new SheetItem("Move up the depth chart", () => { DepthPage.Wanted = p.Position; Shell.ShowTab(FranchiseNav.Team, FranchiseNav.Depth); }, "set the order at " + p.Position, active));
            items.Add(new SheetItem("Offer in a trade", () => { TradesPage.Wanted = p.Id; Shell.ShowTab(FranchiseNav.Office, FranchiseNav.Trades); }, TradeEngine.IsOpen(Save) ? "build a deal" : "trade window is closed", active && TradeEngine.IsOpen(Save)));
            if (r.Status == RosterStatus.PracticeSquad) items.Add(new SheetItem("Promote to the active roster", () => Do(RosterManager.Promote(Save, u, p.Id))));
            else if (active)
            {
                bool eligible = RosterManager.EligibleForPracticeSquad(p);
                items.Add(new SheetItem("Move to the practice squad", () => Do(RosterManager.ToPracticeSquad(Save, u, p.Id)), eligible ? "" : "not eligible", eligible));
                bool ir = p.Injury.IsInjured && p.Injury.WeeksOut >= RosterRules.MinIrInjuryWeeks;
                items.Add(new SheetItem("Place on injured reserve", () => Do(RosterManager.PlaceOnIR(Save, u, p.Id)), ir ? "" : "needs a " + RosterRules.MinIrInjuryWeeks + "+ week injury", ir));
            }
            else if (r.Status == RosterStatus.InjuredReserve) items.Add(new SheetItem("Activate from injured reserve", () => Do(RosterManager.ActivateFromIR(Save, u, p.Id))));
            items.Add(new SheetItem("Release", () => ConfirmRelease(p), "see the dead money first", true, true));
            ActionSheet.Show(Shell.Root, p.Name, p.Position + "  -  " + p.Overall + " OVR  -  " + Fmt.Years(p.Contract.YearsLeft) + " at " + Fmt.MoneyShort(p.Contract.Aav), items);
        }

        void Do(OpResult r)
        {
            Shell.Toast(r.Message, r.Ok ? "ui_check" : "ui_info");
            Ctl.Touch();
        }

        void ConfirmRelease(Player p)
        {
            long now, next; CapManager.DeadMoneyPreview(p.Contract, out now, out next);
            long saves = CapManager.SavingsIfReleased(p);
            string msg = "Dead money: " + Fmt.MoneyShort(now) + " this year" + (next > 0 ? ", " + Fmt.MoneyShort(next) + " next year" : "") + ". Cap space gained now: " + Fmt.MoneyShort(saves) + "."
                         + (RosterManager.InSeason(Save) ? " He goes on waivers." : "");
            Shell.Confirm("Release " + p.Name + "?", msg, "RELEASE", () => Do(RosterManager.Release(Save, Ctl.User, p.Id)));
        }
    }

    // ====================================================================================================== DEPTH CHART
    /// <summary>TEAM &gt; DEPTH CHART: pick a position on the left, then A picks a player up and up / down places him; X opens the options (make starter, add a player, remove, auto sort). Eligibility comes from <see cref="DepthManager"/>.</summary>
    public sealed class DepthPage : FranchisePage
    {
        public static Position? Wanted;
        GlassTable _pos, _depth; PlayerCardPanel _card; TextMeshProUGUI _issues;
        Button _autoPos, _autoAll;
        List<DepthEntry> _entries = new List<DepthEntry>();
        bool _grab; int _grabId;
        Position Pos { get { return LeagueRules.RosterOrder[Mathf.Clamp(_pos.Cursor, 0, LeagueRules.RosterOrder.Length - 1)]; } }

        protected override void Build(RectTransform root)
        {
            var t = GlassTheme.T;
            _issues = Lay.Text(root, "", 22f, t.textSecondary, 0f, 0f, 1000f, 52f, true, TextAlignmentOptions.MidlineLeft, 0f, "Issues"); Lay.Under(_issues, 16f, 0f);
            _autoPos = GlassKit.Button(root, "AUTO SORT POSITION", () => { DepthManager.AutoSort(Save, Ctl.User, Pos); Ctl.Touch(); Shell.Toast("Automatic order for " + Pos + "."); }, GlassButtonStyle.Secondary, 330f, 52f, null, "AutoPos", 20f);
            Lay.Place((RectTransform)_autoPos.transform, Lay.W - 330f * 2f - 16f, 0f, 330f, 52f);
            _autoAll = GlassKit.Button(root, "AUTO SORT ALL", () => { DepthManager.AutoSortAll(Save, Ctl.User); Ctl.Touch(); Shell.Toast("Every position is back on automatic."); }, GlassButtonStyle.Secondary, 330f, 52f, null, "AutoAll", 20f);
            Lay.Place((RectTransform)_autoAll.transform, Lay.W - 330f, 0f, 330f, 52f);

            var pcols = new List<TableColumn>
            {
                new TableColumn("POS", 0.9f, i => LeagueRules.RosterOrder[i].ToString(), false, null, false),
                new TableColumn("STARTER", 2.6f, i => StarterName(i), false, i => IsIssue(i) ? (Color?)GlassTheme.T.warning : null, false),
                new TableColumn("OVR", 0.8f, i => StarterOvr(i), true, null, false)
            };
            _pos = GlassTable.Create(root, "PosTable", 360f, Lay.H - 60f, pcols, 44f);
            Lay.Place((RectTransform)_pos.transform, 0f, 60f, 360f, Lay.H - 60f);
            _pos.SetCount(LeagueRules.RosterOrder.Length);
            _pos.CursorChanged = i => { _grab = false; RefreshDepth(true); };
            _pos.Activated = i => Shell.Focus(_depth);

            var dcols = new List<TableColumn>
            {
                new TableColumn("#", 0.5f, i => (i + 1).ToString(), false, null, false),
                new TableColumn("NAME", 2.6f, i => _entries[i].Name, false, null, false),
                new TableColumn("POS", 0.8f, i => _entries[i].NaturalPos.ToString(), false, i => _entries[i].OutOfPosition ? (Color?)GlassTheme.T.warning : null, false),
                new TableColumn("OVR", 0.8f, i => _entries[i].OverallAtSlot.ToString(), true, i => Fmt.OvrColor(_entries[i].OverallAtSlot), false),
                new TableColumn("ROLE", 1.6f, i => RoleText(i), true, null, false)
            };
            _depth = GlassTable.Create(root, "DepthTable", 740f, Lay.H - 60f, dcols);
            Lay.Place((RectTransform)_depth.transform, 376f, 60f, 740f, Lay.H - 60f);
            _depth.RowTint = i => _grab && i < _entries.Count && _entries[i].PlayerId == _grabId ? (Color?)GlassTheme.T.accent : (_entries[i].Injured ? (Color?)GlassTheme.T.danger : null);
            _depth.CursorChanged = i => ShowCard();
            _depth.Activated = i => ToggleGrab();
            _depth.AltAction = i => OpenOptions();
            _depth.MoveHook = OnDepthMove;
            _depth.HandleSortKey = false;
            _depth.EmptyText = "Nobody is listed here. Press X to add a player.";
            _card = PlayerCardPanel.Create(root, 1132f, 60f, Lay.W - 1132f, Lay.H - 60f, false);
            NavWire.Set(_pos, null, null, null, _depth); NavWire.Set(_depth, null, null, _pos, null);
        }

        string StarterName(int i)
        {
            var e = DepthManager.Entries(Save, Ctl.User, LeagueRules.RosterOrder[i]);
            return e.Count == 0 ? "-" : e[0].Name + (e.Count > 0 && LeagueRules.Starters(LeagueRules.RosterOrder[i]) > 1 ? "  +" + (LeagueRules.Starters(LeagueRules.RosterOrder[i]) - 1) : "");
        }

        string StarterOvr(int i) { var e = DepthManager.Entries(Save, Ctl.User, LeagueRules.RosterOrder[i]); return e.Count == 0 ? "" : e[0].OverallAtSlot.ToString(); }

        bool IsIssue(int i)
        {
            var e = DepthManager.Entries(Save, Ctl.User, LeagueRules.RosterOrder[i]);
            int need = LeagueRules.Starters(LeagueRules.RosterOrder[i]);
            if (e.Count < need) return true;
            for (int k = 0; k < need && k < e.Count; k++) if (e[k].Injured) return true;
            return false;
        }

        string RoleText(int i)
        {
            var e = _entries[i];
            string r = e.Starter ? "STARTER" : (i == LeagueRules.Starters(Pos) ? "NEXT UP" : "");
            if (e.Injured) r = "INJ " + e.InjuryWeeks + "w";
            if (_grab && e.PlayerId == _grabId) r = "MOVING";
            return r;
        }

        public override Selectable Entry { get { return _pos; } }

        public override PromptItem[] Prompts()
        {
            return _grab ? new[] { PromptFooter.Item("glyph_a", "DROP"), PromptFooter.Item("glyph_dpad_up", "MOVE") } : new[] { PromptFooter.Item("glyph_a", "PICK UP"), PromptFooter.Item("glyph_x", "OPTIONS") };
        }

        public override void OnShow()
        {
            if (Wanted.HasValue) { _pos.SetCursor(Mathf.Max(0, Array.IndexOf(LeagueRules.RosterOrder, Wanted.Value)), false); Wanted = null; }
            Refresh();
        }

        public override void OnHide() { _grab = false; if (_card != null) _card.DisposeStage(); }

        public override bool HandleBack()
        {
            if (_grab) { _grab = false; RefreshDepth(false); Shell.UpdateFooter(); return true; }
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _depth.gameObject) { Shell.Focus(_pos); return true; }
            return false;
        }

        public override void Refresh()
        {
            if (!Built) return;
            _pos.Refresh();
            RefreshDepth(false);
            var issues = DepthManager.Issues(Ctl.User);
            var t = GlassTheme.T;
            _issues.text = issues.Count == 0 ? "The depth chart is complete." : issues[0] + (issues.Count > 1 ? "   (+" + (issues.Count - 1) + " more)" : "");
            _issues.color = issues.Count == 0 ? t.success : t.warning;
            _autoPos.GetComponentInChildren<TextMeshProUGUI>().text = "AUTO SORT " + Pos;
        }

        void RefreshDepth(bool resetCursor)
        {
            int keep = !resetCursor && _entries.Count > 0 && _depth.Cursor < _entries.Count ? _entries[_depth.Cursor].PlayerId : -1;
            _entries = DepthManager.Entries(Save, Ctl.User, Pos);
            _depth.SetCount(_entries.Count, !resetCursor);
            if (keep > 0) { int at = _entries.FindIndex(e => e.PlayerId == keep); if (at >= 0) _depth.SetCursor(at, false); }
            _depth.Refresh();
            ShowCard();
        }

        void ShowCard()
        {
            if (_entries.Count == 0) { _card.Fill(Save, null); return; }
            _card.Show(Save, _entries[Mathf.Clamp(_depth.Cursor, 0, _entries.Count - 1)].PlayerId);
        }

        // ---------------------------------------------------------------- pick up and place
        void ToggleGrab()
        {
            if (_entries.Count == 0) return;
            _grab = !_grab;
            _grabId = _grab ? _entries[_depth.Cursor].PlayerId : 0;
            _depth.Refresh(); Shell.UpdateFooter();
        }

        bool OnDepthMove(MoveDirection dir)
        {
            if (!_grab || _entries.Count == 0) return false;
            int d = dir == MoveDirection.Up ? -1 : dir == MoveDirection.Down ? 1 : 0;
            if (d == 0) return true;
            int from = _depth.Cursor, to = from + d;
            if (to < 0 || to >= _entries.Count) return true;
            var r = DepthManager.Swap(Save, Ctl.User, Pos, from, to);
            if (!r.Ok) { Shell.Toast(r.Message); return true; }
            _entries = DepthManager.Entries(Save, Ctl.User, Pos);
            _depth.SetCount(_entries.Count); _depth.SetCursor(to, false);
            _pos.Refresh();
            return true;
        }

        void OpenOptions()
        {
            var p = Pos;
            var items = new List<SheetItem>();
            if (_entries.Count > 0)
            {
                var e = _entries[_depth.Cursor]; int i = _depth.Cursor;
                items.Add(new SheetItem("Make him the starter", () => Edit(DepthManager.Insert(Save, Ctl.User, p, e.PlayerId, 0), 0), "", i > 0));
                items.Add(new SheetItem("Move up one", () => Edit(DepthManager.Swap(Save, Ctl.User, p, i, i - 1), i - 1), "", i > 0));
                items.Add(new SheetItem("Move down one", () => Edit(DepthManager.Swap(Save, Ctl.User, p, i, i + 1), i + 1), "", i + 1 < _entries.Count));
                items.Add(new SheetItem("Remove from " + p, () => Edit(DepthManager.Remove(Save, Ctl.User, p, e.PlayerId), Mathf.Max(0, i - 1)), "", true, true));
            }
            items.Add(new SheetItem("Add a player to " + p, OpenCandidates));
            items.Add(new SheetItem("Automatic order for " + p, () => { DepthManager.AutoSort(Save, Ctl.User, p); Ctl.Touch(); }));
            ActionSheet.Show(Shell.Root, p + " depth chart", _entries.Count + " listed, " + LeagueRules.Starters(p) + " start", items);
        }

        void OpenCandidates()
        {
            var p = Pos;
            var c = DepthManager.Candidates(Ctl.User, p);
            var items = new List<SheetItem>();
            for (int i = 0; i < c.Count && i < 9; i++)
            {
                var pl = c[i];
                items.Add(new SheetItem(pl.Name + "  (" + pl.Position + ")", () => Edit(DepthManager.Insert(Save, Ctl.User, p, pl.Id, _entries.Count), _entries.Count), DepthManager.OverallAt(pl, p) + " OVR here"));
            }
            if (items.Count == 0) items.Add(new SheetItem("Nobody else can play " + p, null, "", false));
            ActionSheet.Show(Shell.Root, "Add to " + p, "Eligible players, best first", items);
        }

        void Edit(OpResult r, int newCursor)
        {
            if (!r.Ok) { Shell.Toast(r.Message); return; }
            Ctl.Touch();
            _depth.SetCursor(newCursor, false);
        }
    }

    // ====================================================================================================== INJURIES AND SQUAD
    /// <summary>TEAM &gt; INJURIES &amp; SQUAD: the injury report, the practice squad, injured reserve and the waiver wire in one list with a view selector; A opens the actions.</summary>
    public sealed class SquadPage : FranchisePage
    {
        sealed class Row { public int PlayerId; public string Name, Pos, A, B, C, D; public int Ovr, Age; public Color? Tint; public bool Starter; public Player P; public WaiverEntry W; }
        readonly List<Row>[] _data = { new List<Row>(), new List<Row>(), new List<Row>(), new List<Row>() };
        readonly GlassTable[] _tables = new GlassTable[4];
        OptionSelector _view; PlayerCardPanel _card; TextMeshProUGUI _info;
        int V { get { return _view.Index; } }

        protected override void Build(RectTransform root)
        {
            var t = GlassTheme.T;
            _view = GlassKit.Stepper(root, "View", new[] { "Injury report", "Practice squad", "Injured reserve", "Waiver wire" }, 0, i => { SwitchView(); }, 560f, 56f); Lay.Place((RectTransform)_view.transform, 0f, 0f, 560f, 56f);
            _info = Lay.Text(root, "", 21f, t.textPrimary, 576f, 0f, 500f, 56f, false, TextAlignmentOptions.MidlineRight, 0f, "Info"); Lay.Under(_info, 10f, 0f);
            var specs = new[]
            {
                new[] { "NAME", "POS", "INJURY", "OUT", "ROLE", "OVR" },
                new[] { "NAME", "POS", "OVR", "AGE", "EXP", "SALARY" },
                new[] { "NAME", "POS", "INJURY", "OUT", "ON IR", "OVR" },
                new[] { "NAME", "POS", "OVR", "AGE", "FROM", "CAP HIT" }
            };
            for (int v = 0; v < 4; v++)
            {
                int vi = v;
                var h = specs[v];
                var cols = new List<TableColumn>
                {
                    new TableColumn(h[0], 2.6f, i => _data[vi][i].Name, false, null, false),
                    new TableColumn(h[1], 0.8f, i => _data[vi][i].Pos, false, null, false),
                    new TableColumn(h[2], 1.6f, i => vi == 0 || vi == 2 ? _data[vi][i].A : _data[vi][i].Ovr.ToString(), vi == 1 || vi == 3, i => vi == 1 || vi == 3 ? (Color?)Fmt.OvrColor(_data[vi][i].Ovr) : null, false),
                    new TableColumn(h[3], 0.8f, i => vi == 0 || vi == 2 ? _data[vi][i].B : _data[vi][i].Age.ToString(), true, null, false),
                    new TableColumn(h[4], 1.3f, i => _data[vi][i].C, true, null, false),
                    new TableColumn(h[5], 1.2f, i => vi == 0 || vi == 2 ? _data[vi][i].Ovr.ToString() : _data[vi][i].D, true, i => vi == 0 || vi == 2 ? (Color?)Fmt.OvrColor(_data[vi][i].Ovr) : null, false)
                };
                var tb = GlassTable.Create(root, "SquadTable" + v, 1100f, Lay.H - 68f, cols);
                Lay.Place((RectTransform)tb.transform, 0f, 68f, 1100f, Lay.H - 68f);
                tb.EmptyText = v == 0 ? "Nobody is hurt." : v == 1 ? "The practice squad is empty." : v == 2 ? "Injured reserve is empty." : (RosterManager.InSeason(Save) ? "Nobody is on waivers." : "The waiver wire only runs during the season.");
                tb.RowTint = i => _data[vi][i].Tint;
                tb.CursorChanged = i => ShowCard();
                tb.Activated = i => OpenActions(_data[vi][i]);
                tb.HandleSortKey = false;
                _tables[v] = tb;
            }
            _card = PlayerCardPanel.Create(root, 1116f, 0f, Lay.W - 1116f, Lay.H);
        }

        public override Selectable Entry { get { return _tables[V]; } }
        public override PromptItem[] Prompts() { return new[] { PromptFooter.Item("glyph_a", "ACTIONS") }; }
        public override void OnHide() { if (_card != null) _card.DisposeStage(); }

        void SwitchView()
        {
            for (int i = 0; i < 4; i++) _tables[i].gameObject.SetActive(i == V);
            Refresh();
        }

        public override void Refresh()
        {
            if (!Built) return;
            var u = Ctl.User; var t = GlassTheme.T;
            foreach (var l in _data) l.Clear();
            // 0 injury report
            foreach (var ev in CareerViews.InjuryReport(Save, u.Id))
            {
                var p = Save.FindPlayer(ev.PlayerId); if (p == null) continue;
                _data[0].Add(new Row { PlayerId = p.Id, P = p, Name = p.Name, Pos = p.Position.ToString(), A = ev.Type, B = ev.Weeks + "w", C = ev.PlacedOnIr ? "IR" : (ev.Starter ? "STARTER" : "BACKUP"), Ovr = p.Overall, Tint = ev.Weeks >= 6 ? (Color?)t.danger : (Color?)t.warning });
            }
            // 1 practice squad
            foreach (var p in u.PracticeSquad) _data[1].Add(new Row { PlayerId = p.Id, P = p, Name = p.Name, Pos = p.Position.ToString(), Ovr = p.Overall, Age = p.Age, C = p.Experience + " yr", D = Fmt.MoneyShort(p.Contract.CapHit) });
            // 2 injured reserve
            foreach (var p in u.InjuredReserve) _data[2].Add(new Row { PlayerId = p.Id, P = p, Name = p.Name, Pos = p.Position.ToString(), A = p.Injury.Type, B = p.Injury.WeeksOut + "w", C = Mathf.Max(0, p.Injury.IrWeeks) + " wk", Ovr = p.Overall, Tint = p.Injury.IsInjured ? (Color?)t.danger : (Color?)t.success });
            // 3 waivers
            foreach (var w in Save.Waivers)
            {
                if (w.Player == null) continue;
                _data[3].Add(new Row { PlayerId = w.PlayerId, P = w.Player, W = w, Name = w.Player.Name, Pos = w.Player.Position.ToString(), Ovr = w.Player.Overall, Age = w.Player.Age, C = TeamUi.Abbr(Save.Pro.GetTeam(w.FromTeamId)), D = Fmt.MoneyShort(w.Player.Contract.CapHit), Tint = w.ClaimTeamIds.Contains(u.Id) ? (Color?)t.accent : null });
            }
            for (int i = 0; i < 4; i++)
            {
                _tables[i].gameObject.SetActive(i == V);
                _tables[i].SetCount(_data[i].Count);
            }
            var rep = CapManager.Report(Save, u);
            string[] info =
            {
                _data[0].Count + " injured, " + (_data[0].FindAll(r => r.C == "STARTER").Count) + " starters out",
                rep.PracticeSquadCount + " of " + RosterRules.PracticeSquadLimit + " spots used",
                rep.InjuredReserveCount + " of " + RosterRules.IrLimit + " spots used",
                "Your priority: " + RosterManager.WaiverPriority(Save, u.Id) + " of " + Save.Pro.Teams.Count
            };
            _info.text = info[V];
            ShowCard();
        }

        void ShowCard()
        {
            var l = _data[V];
            if (l.Count == 0) { _card.Fill(Save, null); return; }
            _card.Show(Save, l[Mathf.Clamp(_tables[V].Cursor, 0, l.Count - 1)].PlayerId);
        }

        void OpenActions(Row r)
        {
            var u = Ctl.User; var p = r.P;
            var items = new List<SheetItem>();
            switch (V)
            {
                case 0:
                    {
                        bool ir = p.Injury.WeeksOut >= RosterRules.MinIrInjuryWeeks && u.Roster.Contains(p);
                        items.Add(new SheetItem("Place on injured reserve", () => Do(RosterManager.PlaceOnIR(Save, u, p.Id)), ir ? "" : "needs a " + RosterRules.MinIrInjuryWeeks + "+ week injury on the active roster", ir));
                        items.Add(new SheetItem("Set the depth chart", () => { DepthPage.Wanted = p.Position; Shell.ShowTab(FranchiseNav.Team, FranchiseNav.Depth); }));
                        break;
                    }
                case 1:
                    items.Add(new SheetItem("Promote to the active roster", () => Do(RosterManager.Promote(Save, u, p.Id))));
                    items.Add(new SheetItem("Release", () => Do(RosterManager.Release(Save, u, p.Id)), "", true, true));
                    break;
                case 2:
                    {
                        bool can = !p.Injury.IsInjured;
                        items.Add(new SheetItem("Activate to the active roster", () => Do(RosterManager.ActivateFromIR(Save, u, p.Id)), can ? "" : "still injured", can));
                        items.Add(new SheetItem("Release", () => Do(RosterManager.Release(Save, u, p.Id)), "", true, true));
                        break;
                    }
                default:
                    {
                        bool claimed = r.W != null && r.W.ClaimTeamIds.Contains(u.Id);
                        items.Add(new SheetItem(claimed ? "Claim submitted" : "Put in a waiver claim", () => Do(RosterManager.ClaimWaiver(Save, p.Id)), claimed ? "decided when the week advances" : "", !claimed));
                        break;
                    }
            }
            ActionSheet.Show(Shell.Root, p.Name, p.Position + "  -  " + p.Overall + " OVR  -  " + p.Age + " yrs", items);
        }

        void Do(OpResult r) { Shell.Toast(r.Message, r.Ok ? "ui_check" : "ui_info"); Ctl.Touch(); }
    }
}
