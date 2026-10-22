using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.UI.Franchise
{
    /// <summary>Pure text of the league screens (EditMode tested).</summary>
    public static class LeagueText
    {
        public static readonly string[] CategoryNames = { "Passing yards", "Passing touchdowns", "Interceptions thrown", "Passer rating", "Rushing yards", "Rushing touchdowns", "Yards per carry", "Receptions", "Receiving yards", "Receiving touchdowns", "Sacks", "Interceptions", "Tackles", "Field goals", "Points", "Punts" };

        public static string LeaderValue(StatCategory c, float v)
        {
            return c == StatCategory.PasserRating || c == StatCategory.RushAvg ? v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : Mathf.RoundToInt(v).ToString();
        }

        public static string Result(ScheduleRow r)
        {
            if (r.Bye) return "BYE";
            if (!r.Played) return "-";
            return (r.Tie ? "T " : r.Won ? "W " : "L ") + r.TeamScore + "-" + r.OpponentScore;
        }

        public static string Opponent(ScheduleRow r) { return r.Bye ? "" : (r.Home ? "vs " : "at ") + r.Opponent; }

        public static string AccoladeName(AccoladeKind k)
        {
            switch (k)
            {
                case AccoladeKind.Mvp: return "Most Valuable Player"; case AccoladeKind.Opoy: return "Offensive Player of the Year"; case AccoladeKind.Dpoy: return "Defensive Player of the Year";
                case AccoladeKind.Roy: return "Rookie of the Year"; case AccoladeKind.PixBowlMvp: return "PIX Bowl MVP"; case AccoladeKind.AllPro1: return "All-Pro first team"; case AccoladeKind.AllPro2: return "All-Pro second team";
                default: return "Champion";
            }
        }

        public static string TrophyName(TrophyKind k)
        {
            switch (k) { case TrophyKind.PixBowl: return "PIX Bowl champions"; case TrophyKind.ConferenceTitle: return "Conference champions"; case TrophyKind.DivisionTitle: return "Division champions"; default: return "Perfect regular season"; }
        }

        public static string KindName(NewsKind k) { return k == NewsKind.ReSigning ? "RE-SIGN" : k == NewsKind.HallOfFame ? "HOF" : k.ToString().ToUpperInvariant(); }

        public static string When(NewsItem n) { return n.Year + " " + (n.Week > 0 ? "wk " + n.Week : n.Phase.ToString()); }
    }

    // ====================================================================================================== SCHEDULE
    /// <summary>LEAGUE &gt; SCHEDULE: your schedule and results on the left, every game of the highlighted week on the right; A opens the box score when the game was played in this session.</summary>
    public sealed class SchedulePage : FranchisePage
    {
        GlassTable _mine, _week; List<ScheduleRow> _rows = new List<ScheduleRow>(); List<ScheduledGame> _games = new List<ScheduledGame>();
        TextMeshProUGUI _weekTitle;

        protected override void Build(RectTransform root)
        {
            var t = GlassTheme.T;
            var cols = new List<TableColumn>
            {
                new TableColumn("WK", 0.8f, i => _rows[i].Bye ? _rows[i].Week.ToString() : (_rows[i].Label.StartsWith("Week") ? _rows[i].Week.ToString() : ShortLabel(_rows[i].Label)), false, null, false),
                new TableColumn("OPPONENT", 3.2f, i => _rows[i].Bye ? "BYE WEEK" : LeagueText.Opponent(_rows[i]), false, null, false),
                new TableColumn("RESULT", 1.5f, i => LeagueText.Result(_rows[i]), true, null, false)
            };
            _mine = GlassTable.Create(root, "MySchedule", 760f, 666f, cols, 46f);
            Lay.Place((RectTransform)_mine.transform, 0f, 0f, 760f, 666f);
            _mine.RowTint = i => !_rows[i].Played ? (Color?)null : _rows[i].Tie ? (Color?)GlassTheme.T.textSecondary : _rows[i].Won ? (Color?)GlassTheme.T.success : (Color?)GlassTheme.T.danger;
            _mine.CursorChanged = i => FillWeek();
            _mine.Activated = i => OpenBox(GameOfRow(_rows[i]));
            _mine.HandleSortKey = false;
            _weekTitle = Lay.Caps(root, "", 788f, 0f, 900f, 36f, t.accent, 19f);
            var wc = new List<TableColumn>
            {
                new TableColumn("AWAY", 2.4f, i => TeamName(_games[i].AwayId), false, i => _games[i].Involves(Save.UserTeamId) ? (Color?)GlassTheme.T.accent : null, false),
                new TableColumn("SCORE", 1.3f, i => _games[i].Played ? _games[i].AwayScore + " - " + _games[i].HomeScore + (_games[i].Overtime ? " OT" : "") : "-", true, null, false),
                new TableColumn("HOME", 2.4f, i => "     " + TeamName(_games[i].HomeId), false, i => _games[i].Involves(Save.UserTeamId) ? (Color?)GlassTheme.T.accent : null, false),
                new TableColumn("", 1.1f, i => _games[i].Played ? (Ctl.BoxOf(_games[i]) != null ? "BOX SCORE" : "FINAL") : "", true, null, false)
            };
            _week = GlassTable.Create(root, "WeekGames", 940f, 624f, wc, 46f);
            Lay.Place((RectTransform)_week.transform, 788f, 42f, 940f, 624f);
            _week.EmptyText = "No games this week.";
            _week.Activated = i => OpenBox(_games[i]);
            _week.HandleSortKey = false;
            NavWire.Set(_mine, null, null, null, _week); NavWire.Set(_week, null, null, _mine, null);
        }

        static string ShortLabel(string l) { return l.StartsWith("Wild") ? "WC" : l.StartsWith("Div") ? "DIV" : l.StartsWith("Conf") ? "CONF" : l.Contains("Bowl") ? "BOWL" : l; }

        string TeamName(int id) { var t = Save.Pro.GetTeam(id); return t != null ? t.City + " " + t.Nickname : "Team " + id; }

        ScheduledGame GameOfRow(ScheduleRow r)
        {
            if (r.Bye || Save.Season == null) return null;
            foreach (var g in Save.Season.GamesOfWeek(r.Week)) if (g.Involves(Save.UserTeamId)) return g;
            return null;
        }

        void OpenBox(ScheduledGame g)
        {
            if (g == null) return;
            if (!g.Played) { Shell.Toast("That game has not been played yet."); return; }
            var box = Ctl.BoxOf(g);
            if (box == null) { Shell.Toast("Final score " + CareerText.TeamNick(Save, g.AwayId) + " " + g.AwayScore + ", " + CareerText.TeamNick(Save, g.HomeId) + " " + g.HomeScore + ". Box scores are kept for games of this session."); return; }
            BoxScoreView.Show(Shell, Save, box, "Week " + g.Week, "CLOSE", null);
        }

        public override Selectable Entry { get { return _mine; } }
        public override PromptItem[] Prompts() { return new[] { PromptFooter.Item("glyph_a", "BOX SCORE") }; }

        public override void OnShow()
        {
            Refresh();
            // open on the current week
            int at = _rows.FindIndex(r => !r.Played && !r.Bye); if (at < 0) at = Mathf.Max(0, _rows.Count - 1);
            _mine.SetCursor(at, false); FillWeek();
        }

        public override void Refresh()
        {
            if (!Built) return;
            _rows = CareerViews.Schedule(Save, Save.UserTeamId);
            _mine.SetCount(_rows.Count);
            FillWeek();
        }

        void FillWeek()
        {
            if (_rows.Count == 0 || Save.Season == null) { _games.Clear(); _week.SetCount(0); return; }
            var r = _rows[Mathf.Clamp(_mine.Cursor, 0, _rows.Count - 1)];
            _games = Save.Season.GamesOfWeek(r.Week);
            _games.Sort((a, b) => (b.Involves(Save.UserTeamId) ? 1 : 0).CompareTo(a.Involves(Save.UserTeamId) ? 1 : 0));
            _week.SetCount(_games.Count, false);
            _weekTitle.text = (r.Label.StartsWith("Week") || r.Bye ? "WEEK " + r.Week : r.Label.ToUpperInvariant()) + "  -  LEAGUE RESULTS";
        }
    }

    // ====================================================================================================== STANDINGS
    /// <summary>LEAGUE &gt; STANDINGS: division, conference, league, power rankings and the playoff picture (the seven conference seeds with the cut line).</summary>
    public sealed class StandingsPage : FranchisePage
    {
        sealed class Row { public int Rank, TeamId; public string Name, Rec, Pct, PF, PA, Diff, Streak, Note; public bool Cut; }
        OptionSelector _view, _group; GlassTable _table; List<Row> _rows = new List<Row>();
        readonly List<Division> _divs = new List<Division>(); readonly List<Conference> _confs = new List<Conference>();

        protected override void Build(RectTransform root)
        {
            _view = GlassKit.Stepper(root, "View", new[] { "Division", "Conference", "Playoff picture", "League", "Power rankings" }, 0, i => Refresh(), 560f, 56f); Lay.Place((RectTransform)_view.transform, 0f, 0f, 560f, 56f);
            _group = GlassKit.Stepper(root, "Group", new[] { "-" }, 0, i => Refresh(), 640f, 56f); Lay.Place((RectTransform)_group.transform, 576f, 0f, 640f, 56f);
            var cols = new List<TableColumn>
            {
                new TableColumn("#", 0.5f, i => _rows[i].Rank.ToString(), false, null, false),
                new TableColumn("TEAM", 3f, i => _rows[i].Name, false, null, false),
                new TableColumn("REC", 1.1f, i => _rows[i].Rec, true, null, false),
                new TableColumn("PCT", 1f, i => _rows[i].Pct, true, null, false),
                new TableColumn("PF", 0.9f, i => _rows[i].PF, true, null, false),
                new TableColumn("PA", 0.9f, i => _rows[i].PA, true, null, false),
                new TableColumn("DIFF", 0.9f, i => _rows[i].Diff, true, null, false),
                new TableColumn("STRK", 0.8f, i => _rows[i].Streak, true, null, false),
                new TableColumn("", 1.6f, i => _rows[i].Note, true, null, false)
            };
            _table = GlassTable.Create(root, "StandTable", 1728f, 598f, cols);
            Lay.Place((RectTransform)_table.transform, 0f, 68f, 1728f, 598f);
            _table.RowTint = i => _rows[i].TeamId == Save.UserTeamId ? (Color?)GlassTheme.T.accent : (_rows[i].Cut ? (Color?)GlassTheme.T.textMuted : null);
            _table.HandleSortKey = false;
        }

        public override Selectable Entry { get { return _table; } }

        public override void OnShow()
        {
            _divs.Clear(); _confs.Clear();
            foreach (var c in Save.Pro.Conferences) { _confs.Add(c); foreach (var d in c.Divisions) _divs.Add(d); }
            Refresh();
        }

        public override void Refresh()
        {
            if (!Built || Save.Season == null || Save.Season.IsEmpty) return;
            var u = Ctl.User;
            int v = _view.Index;
            // the group stepper follows the view
            if (v == 0) { var n = new string[_divs.Count]; for (int i = 0; i < n.Length; i++) n[i] = DivName(_divs[i]); int idx = _group.Options.Length == n.Length ? _group.Index : Mathf.Max(0, _divs.FindIndex(d => d.Id == u.DivisionId)); _group.SetOptions(n, idx); }
            else if (v == 1 || v == 2) { var n = new string[_confs.Count]; for (int i = 0; i < n.Length; i++) n[i] = _confs[i].Name; int idx = _group.Options.Length == n.Length ? _group.Index : Mathf.Max(0, _confs.FindIndex(c => c.Id == ConfOf(u))); _group.SetOptions(n, idx); }
            else _group.SetOptions(new[] { "All 32 teams" }, 0);
            _group.gameObject.SetActive(v <= 2);
            _rows.Clear();
            if (v == 4)
            {
                foreach (var r in CareerViews.PowerRankings(Save)) _rows.Add(new Row { Rank = r.Rank, TeamId = r.TeamId, Name = r.Name, Rec = r.Record, Pct = r.Ovr + " OVR", PF = "", PA = "", Diff = "", Streak = "", Note = "" });
            }
            else
            {
                List<StandingsRow> src;
                if (v == 0 && _divs.Count > 0) src = Save.Season.DivisionStandings(_divs[Mathf.Clamp(_group.Index, 0, _divs.Count - 1)].Id);
                else if ((v == 1 || v == 2) && _confs.Count > 0) src = Save.Season.ConferenceStandings(_confs[Mathf.Clamp(_group.Index, 0, _confs.Count - 1)].Id);
                else { src = Save.Season.Standings(); src.Sort((a, b) => { int c = b.Pct.CompareTo(a.Pct); return c != 0 ? c : (b.PF - b.PA).CompareTo(a.PF - a.PA); }); }
                for (int i = 0; i < src.Count; i++)
                {
                    var s = src[i];
                    int diff = s.PF - s.PA;
                    var row = new Row { Rank = i + 1, TeamId = s.TeamId, Name = s.Name, Rec = s.Record, Pct = s.Pct.ToString(".000", System.Globalization.CultureInfo.InvariantCulture), PF = s.PF.ToString(), PA = s.PA.ToString(), Diff = (diff > 0 ? "+" : "") + diff, Streak = s.Streak > 0 ? "W" + s.Streak : s.Streak < 0 ? "L" + (-s.Streak) : "-" };
                    if (v == 2) { row.Note = i < 7 ? "SEED " + (i + 1) : "OUT"; row.Cut = i >= 7; }
                    _rows.Add(row);
                }
            }
            for (int i = 0; i < _rows.Count; i++) { var tm = Save.Pro.GetTeam(_rows[i].TeamId); if (tm != null) _rows[i].Name = tm.City + " " + tm.Nickname; }
            _table.SetCount(_rows.Count, false);
            int at = _rows.FindIndex(r => r.TeamId == u.Id); if (at >= 0) _table.SetCursor(at, false);
        }

        int ConfOf(LeagueTeam t) { foreach (var c in Save.Pro.Conferences) foreach (var d in c.Divisions) if (d.Id == t.DivisionId) return c.Id; return 0; }
        string DivName(Division d) { foreach (var c in Save.Pro.Conferences) if (c.Id == d.ConferenceId) { string pre = string.IsNullOrEmpty(c.Abbreviation) ? c.Name : c.Abbreviation; return d.Name.StartsWith(pre) ? d.Name : pre + " " + d.Name; } return d.Name; }
    }

    // ====================================================================================================== LEADERS
    /// <summary>LEAGUE &gt; LEADERS: the top 25 of every statistical category of the season, with the player card of the highlighted leader.</summary>
    public sealed class LeadersPage : FranchisePage
    {
        OptionSelector _cat; GlassTable _table; PlayerCardPanel _card; List<LeaderEntry> _rows = new List<LeaderEntry>();
        StatCategory Cat { get { return (StatCategory)_cat.Index; } }

        protected override void Build(RectTransform root)
        {
            _cat = GlassKit.Stepper(root, "Category", LeagueText.CategoryNames, 0, i => Refresh(), 640f, 56f); Lay.Place((RectTransform)_cat.transform, 0f, 0f, 640f, 56f);
            var cols = new List<TableColumn>
            {
                new TableColumn("#", 0.5f, i => _rows[i].Rank.ToString(), false, null, false),
                new TableColumn("PLAYER", 2.6f, i => _rows[i].Name, false, null, false),
                new TableColumn("TEAM", 1.8f, i => CareerText.TeamNick(Save, _rows[i].TeamId), false, null, false),
                new TableColumn("POS", 0.7f, i => _rows[i].Pos.ToString(), false, null, false),
                new TableColumn("GP", 0.6f, i => _rows[i].Games.ToString(), true, null, false),
                new TableColumn("VALUE", 1.1f, i => LeagueText.LeaderValue(Cat, _rows[i].Value), true, null, false)
            };
            _table = GlassTable.Create(root, "LeaderTable", 1080f, 598f, cols);
            Lay.Place((RectTransform)_table.transform, 0f, 68f, 1080f, 598f);
            _table.RowTint = i => _rows[i].TeamId == Save.UserTeamId ? (Color?)GlassTheme.T.accent : null;
            _table.CursorChanged = i => ShowCard(); _table.HandleSortKey = false;
            _table.EmptyText = "No qualifying players yet.";
            _card = PlayerCardPanel.Create(root, 1096f, 68f, 632f, 598f);
        }

        public override Selectable Entry { get { return _table; } }
        public override void OnHide() { if (_card != null) _card.DisposeStage(); }

        public override void Refresh()
        {
            if (!Built || Save.Season == null || Save.Season.IsEmpty) return;
            _rows = LeagueLeaders.Top(Save.Season, Cat, 25);
            _table.SetCount(_rows.Count, false);
            ShowCard();
        }

        void ShowCard()
        {
            if (_rows.Count == 0) { _card.Fill(Save, null); return; }
            _card.Show(Save, _rows[Mathf.Clamp(_table.Cursor, 0, _rows.Count - 1)].PlayerId);
        }
    }

    // ====================================================================================================== AWARDS
    /// <summary>LEAGUE &gt; AWARDS: the winners of a season (MVP, offensive and defensive player of the year, rookie of the year, PIX Bowl MVP) and the All-Pro teams.</summary>
    public sealed class AwardsPage : FranchisePage
    {
        sealed class Row { public string A, B, C, D; public int TeamId; }
        OptionSelector _year, _view; GlassTable _table; List<Row> _rows = new List<Row>(); List<int> _years = new List<int>();

        protected override void Build(RectTransform root)
        {
            _year = GlassKit.Stepper(root, "Season", new[] { "-" }, 0, i => Refresh(), 380f, 56f); Lay.Place((RectTransform)_year.transform, 0f, 0f, 380f, 56f);
            _view = GlassKit.Stepper(root, "Show", new[] { "Season awards", "All-Pro first team", "All-Pro second team" }, 0, i => Refresh(), 640f, 56f); Lay.Place((RectTransform)_view.transform, 396f, 0f, 640f, 56f);
            var cols = new List<TableColumn>
            {
                new TableColumn("AWARD / POSITION", 2.8f, i => _rows[i].A, false, null, false),
                new TableColumn("PLAYER", 2.6f, i => _rows[i].B, false, null, false),
                new TableColumn("TEAM", 2f, i => _rows[i].C, false, null, false),
                new TableColumn("", 1f, i => _rows[i].D, true, null, false)
            };
            _table = GlassTable.Create(root, "AwardTable", 1728f, 598f, cols);
            Lay.Place((RectTransform)_table.transform, 0f, 68f, 1728f, 598f);
            _table.RowTint = i => _rows[i].TeamId == Save.UserTeamId ? (Color?)GlassTheme.T.accent : null;
            _table.HandleSortKey = false; _table.EmptyText = "No awards yet: they are handed out when the season ends.";
        }

        public override Selectable Entry { get { return _table; } }

        public override void OnShow()
        {
            _years.Clear();
            foreach (var a in Save.Accolades) if (!_years.Contains(a.Year)) _years.Add(a.Year);
            if (Save.Season != null && Save.Season.Awards.Count > 0 && !_years.Contains(Save.Year)) _years.Add(Save.Year);
            _years.Sort((a, b) => b.CompareTo(a));
            if (_years.Count == 0) _years.Add(Save.Year);
            var n = new string[_years.Count]; for (int i = 0; i < n.Length; i++) n[i] = _years[i].ToString();
            _year.SetOptions(n, 0);
            Refresh();
        }

        public override void Refresh()
        {
            if (!Built) return;
            _rows.Clear();
            int year = _years.Count > 0 ? _years[Mathf.Clamp(_year.Index, 0, _years.Count - 1)] : Save.Year;
            if (_view.Index == 0)
            {
                AccoladeKind[] kinds = { AccoladeKind.Mvp, AccoladeKind.Opoy, AccoladeKind.Dpoy, AccoladeKind.Roy, AccoladeKind.PixBowlMvp };
                foreach (var k in kinds)
                {
                    var a = Save.Accolades.Find(x => x.Year == year && x.Kind == k);
                    if (a == null) continue;
                    var p = Save.FindPlayer(a.PlayerId);
                    _rows.Add(new Row { A = LeagueText.AccoladeName(k), B = p != null ? p.Name : "Player " + a.PlayerId, C = CareerText.TeamName(Save, a.TeamId), D = p != null ? p.Position.ToString() : "", TeamId = a.TeamId });
                }
                if (_rows.Count == 0 && year == Save.Year && Save.Season != null)
                    foreach (var w in Save.Season.Awards) _rows.Add(new Row { A = w.Award, B = w.PlayerName, C = CareerText.TeamName(Save, w.TeamId), D = w.Pos.ToString(), TeamId = w.TeamId });
            }
            else
            {
                var ap = HistoryEngine.GetAllPro(Save, year, _view.Index);
                if (ap != null) foreach (var s in ap.Players) _rows.Add(new Row { A = s.Pos.ToString(), B = s.Name, C = CareerText.TeamName(Save, s.TeamId), D = Mathf.RoundToInt(s.Score).ToString(), TeamId = s.TeamId });
            }
            _table.SetCount(_rows.Count, false);
        }
    }

    // ====================================================================================================== RECORDS
    /// <summary>LEAGUE &gt; RECORDS: the record book (single game, single season, career) and the team records.</summary>
    public sealed class RecordsPage : FranchisePage
    {
        sealed class Row { public string A, B, C, D, E; public bool Mine; }
        OptionSelector _book; GlassTable _table; List<Row> _rows = new List<Row>();

        protected override void Build(RectTransform root)
        {
            _book = GlassKit.Stepper(root, "Record book", new[] { "Single game", "Single season", "Career", "Team records" }, 0, i => Refresh(), 640f, 56f); Lay.Place((RectTransform)_book.transform, 0f, 0f, 640f, 56f);
            var cols = new List<TableColumn>
            {
                new TableColumn("RECORD", 2.4f, i => _rows[i].A, false, null, false),
                new TableColumn("VALUE", 1f, i => _rows[i].B, true, null, false),
                new TableColumn("HOLDER", 2.6f, i => _rows[i].C, false, null, false),
                new TableColumn("TEAM", 2.2f, i => _rows[i].D, false, null, false),
                new TableColumn("YEAR", 0.8f, i => _rows[i].E, true, null, false)
            };
            _table = GlassTable.Create(root, "RecordTable", 1728f, 598f, cols);
            Lay.Place((RectTransform)_table.transform, 0f, 68f, 1728f, 598f);
            _table.HandleSortKey = false; _table.EmptyText = "No records yet.";
        }

        public override Selectable Entry { get { return _table; } }

        public override void Refresh()
        {
            if (!Built) return;
            _rows.Clear();
            if (_book.Index == 3)
            {
                foreach (var r in Save.TeamRecords) _rows.Add(new Row { A = r.Category, B = r.Value.ToString(), C = r.TeamName, D = "", E = r.Year.ToString() });
            }
            else
            {
                RecordScope scope = _book.Index == 0 ? RecordScope.SingleGame : _book.Index == 1 ? RecordScope.SingleSeason : RecordScope.Career;
                foreach (var r in Save.Pro.Records.Entries)
                    if (r.Scope == scope) _rows.Add(new Row { A = Cap(HistoryEngine.Pretty(r.Category)), B = r.Value.ToString(), C = r.PlayerName, D = r.TeamName, E = r.Year > 0 ? r.Year.ToString() : "" });
            }
            _table.SetCount(_rows.Count, false);
        }

        static string Cap(string s) { return string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }
    }

    // ====================================================================================================== TROPHIES, HALL OF FAME, LEGACY
    /// <summary>LEAGUE &gt; TROPHIES &amp; HOF: your trophy case, the Hall of Fame and the legacy score with its itemised lines.</summary>
    public sealed class TrophyPage : FranchisePage
    {
        OptionSelector _view; GlassTable _trophies, _hof; RectTransform _side;
        List<TrophyEntry> _t = new List<TrophyEntry>(); List<HofEntry> _h = new List<HofEntry>();

        protected override void Build(RectTransform root)
        {
            _view = GlassKit.Stepper(root, "View", new[] { "Trophy case", "Hall of Fame", "Legacy" }, 0, i => SwitchView(), 640f, 56f); Lay.Place((RectTransform)_view.transform, 0f, 0f, 640f, 56f);
            var tc = new List<TableColumn>
            {
                new TableColumn("YEAR", 0.8f, i => _t[i].Year.ToString(), false, null, false),
                new TableColumn("TROPHY", 2.4f, i => LeagueText.TrophyName(_t[i].Kind), false, null, false),
                new TableColumn("DETAIL", 3.2f, i => _t[i].Kind == TrophyKind.PixBowl ? "beat " + _t[i].Opponent + " " + _t[i].Score : _t[i].Kind == TrophyKind.ConferenceTitle ? _t[i].Score : "", false, null, false),
                new TableColumn("MVP", 2f, i => _t[i].BowlMvp, true, null, false)
            };
            _trophies = GlassTable.Create(root, "TrophyTable", 1728f, 598f, tc); Lay.Place((RectTransform)_trophies.transform, 0f, 68f, 1728f, 598f);
            _trophies.EmptyText = "The trophy case is empty. Win a division title to start it."; _trophies.HandleSortKey = false;
            var hc = new List<TableColumn>
            {
                new TableColumn("NAME", 2.6f, i => _h[i].Name, false, null, false),
                new TableColumn("POS", 0.7f, i => _h[i].Pos.ToString(), false, null, false),
                new TableColumn("INDUCTED", 1f, i => _h[i].InductionYear.ToString(), true, null, false),
                new TableColumn("SEASONS", 0.9f, i => _h[i].Seasons.ToString(), true, null, false),
                new TableColumn("TEAMS", 2.4f, i => _h[i].Teams, false, null, false)
            };
            _hof = GlassTable.Create(root, "HofTable", 1060f, 598f, hc); Lay.Place((RectTransform)_hof.transform, 0f, 68f, 1060f, 598f);
            _hof.EmptyText = "Nobody is in the Hall of Fame yet. Retired stars are voted in after two years."; _hof.HandleSortKey = false;
            _hof.CursorChanged = i => FillSide();
            _side = Lay.Group(root, "Side", 1076f, 68f, 652f, 598f);
        }

        public override Selectable Entry { get { return _view; } }

        void SwitchView() { Refresh(); }

        public override void Refresh()
        {
            if (!Built) return;
            int v = _view.Index;
            _trophies.gameObject.SetActive(v == 0); _hof.gameObject.SetActive(v <= 2 && v != 0); _side.gameObject.SetActive(v != 0);
            _t = HistoryEngine.TrophyCase(Save, Save.UserTeamId); _trophies.SetCount(_t.Count, false);
            _h = new List<HofEntry>(Save.HallOfFame); _h.Sort((a, b) => b.InductionYear.CompareTo(a.InductionYear));
            _hof.gameObject.SetActive(v == 1); _hof.SetCount(_h.Count, false);
            FillSide();
        }

        void FillSide()
        {
            var t = GlassTheme.T;
            for (int i = _side.childCount - 1; i >= 0; i--) UiKit.Kill(_side.GetChild(i).gameObject);
            if (_view.Index == 0) return;
            var card = GlassKit.Panel(_side, "Card", 1.2f, true, t.cornerRadiusLarge); Lay.Place(card.rectTransform, 0f, 0f, 652f, 598f);
            var sp = StackPanel.Create(card.rectTransform, "S", 28f, 24f, 596f, 550f);
            if (_view.Index == 1)
            {
                if (_h.Count == 0) { sp.Heading("HALL OF FAME", t.textPrimary, 30f); sp.Para("Players are voted in two years after they retire when their career was big enough. At most four a year.", 22f); return; }
                var h = _h[Mathf.Clamp(_hof.Cursor, 0, _h.Count - 1)];
                sp.Heading(h.Name.ToUpperInvariant(), t.textPrimary, 32f);
                sp.Line("Position", h.Pos.ToString()); sp.Line("Inducted", h.InductionYear.ToString()); sp.Line("Career", h.DraftYear + " to " + h.RetiredYear + "  (" + h.Seasons + " seasons)");
                sp.Line("Teams", Fmt.Trim(h.Teams, 40)); sp.Rule();
                sp.Para(h.Summary, 21f, t.textSecondary, 150f); sp.Gap(4f);
                sp.Para(h.Accolades, 20f, t.textMuted, 120f);
            }
            else
            {
                var leg = HistoryEngine.Legacy(Save, Save.UserTeamId);
                Lay.Text(card.rectTransform, leg.Score.ToString(), 80f, t.accent, 28f, 20f, 240f, 96f, true, TextAlignmentOptions.MidlineLeft, 0f, "Score");
                Lay.Text(card.rectTransform, leg.Tier.ToUpperInvariant(), 30f, t.textPrimary, 280f, 32f, 340f, 50f, true, TextAlignmentOptions.MidlineLeft, 4f, "Tier");
                var s2 = StackPanel.Create(card.rectTransform, "L", 28f, 130f, 596f, 440f);
                foreach (var kv in leg.Lines) if (kv.Value != 0 && s2.Fits(34f)) s2.Line(kv.Key, (kv.Value > 0 ? "+" : "") + kv.Value, kv.Value > 0 ? t.textPrimary : t.danger, 34f, 21f);
            }
        }
    }

    // ====================================================================================================== NEWS
    /// <summary>LEAGUE &gt; NEWS: the league wire as a list with filters; the story of the highlighted headline on the right.</summary>
    public sealed class NewsPage : FranchisePage
    {
        OptionSelector _filter; GlassTable _table; RectTransform _story; List<NewsItem> _rows = new List<NewsItem>();
        static readonly string[] Filters = { "Everything", "My team", "Trades", "Signings", "Injuries", "Awards and records", "Draft", "Coaches" };

        protected override void Build(RectTransform root)
        {
            _filter = GlassKit.Stepper(root, "Filter", Filters, 0, i => Refresh(), 560f, 56f); Lay.Place((RectTransform)_filter.transform, 0f, 0f, 560f, 56f);
            var cols = new List<TableColumn>
            {
                new TableColumn("WHEN", 1.2f, i => LeagueText.When(_rows[i]), false, null, false),
                new TableColumn("TYPE", 1.1f, i => LeagueText.KindName(_rows[i].Kind), false, null, false),
                new TableColumn("HEADLINE", 5f, i => _rows[i].Headline, false, null, false)
            };
            _table = GlassTable.Create(root, "NewsTable", 1120f, 598f, cols, 52f);
            Lay.Place((RectTransform)_table.transform, 0f, 68f, 1120f, 598f);
            _table.RowTint = i => _rows[i].Mine ? (Color?)GlassTheme.T.accent : null;
            _table.CursorChanged = i => ShowStory(); _table.HandleSortKey = false; _table.EmptyText = "Quiet so far.";
            _story = Lay.Group(root, "Story", 1136f, 68f, 592f, 598f);
        }

        public override Selectable Entry { get { return _table; } }

        public override void Refresh()
        {
            if (!Built) return;
            int f = _filter.Index;
            var all = NewsWriter.Feed(Save, 120, f == 1);
            _rows = all.FindAll(n =>
            {
                switch (f)
                {
                    case 2: return n.Kind == NewsKind.Trade;
                    case 3: return n.Kind == NewsKind.Signing || n.Kind == NewsKind.ReSigning;
                    case 4: return n.Kind == NewsKind.Injury;
                    case 5: return n.Kind == NewsKind.Award || n.Kind == NewsKind.Record || n.Kind == NewsKind.Milestone || n.Kind == NewsKind.HallOfFame || n.Kind == NewsKind.Title;
                    case 6: return n.Kind == NewsKind.Draft;
                    case 7: return n.Kind == NewsKind.Coach;
                }
                return true;
            });
            _table.SetCount(_rows.Count, false);
            ShowStory();
        }

        void ShowStory()
        {
            var t = GlassTheme.T;
            for (int i = _story.childCount - 1; i >= 0; i--) UiKit.Kill(_story.GetChild(i).gameObject);
            var card = GlassKit.Panel(_story, "Card", 1.2f, true, t.cornerRadiusLarge); Lay.Place(card.rectTransform, 0f, 0f, 592f, 598f);
            if (_rows.Count == 0) return;
            var n = _rows[Mathf.Clamp(_table.Cursor, 0, _rows.Count - 1)];
            var sp = StackPanel.Create(card.rectTransform, "S", 28f, 26f, 536f, 546f);
            sp.Caps(LeagueText.KindName(n.Kind) + "  -  " + LeagueText.When(n), t.accent);
            sp.Para(n.Headline, 28f, t.textPrimary, 150f);
            sp.Gap(8f); sp.Rule();
            sp.Para(n.Body, 22f, t.textSecondary, 320f);
        }
    }
}
