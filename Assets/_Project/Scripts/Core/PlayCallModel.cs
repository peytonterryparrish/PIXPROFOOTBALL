using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    public enum CallTab { Playbook, AskCoach, Recent, Special }
    public enum CallStage { Formation, Set, Play }
    public enum CallFilter { All, Run, Pass, PlayAction, Screen, Zone, Man, Blitz }

    /// <summary>What the play-call screen shows about the game.</summary>
    public sealed class CallSituation
    {
        public string Title = "OFFENSE", TeamName = "", DownText = "", SpotText = "", ScoreText = "", ClockText = "", CoachStyle = "";
        public int Timeouts, Quarter;
        public int PlayClockSeconds = 25;
    }

    /// <summary>One row of the second screen: an alignment set of the formation (offense) or a coverage group (defense).</summary>
    public sealed class SetEntry
    {
        public string Label = "", Detail = "";
        public Formation Formation;
        public List<Play> Plays = new List<Play>();
    }

    /// <summary>One row of the first screen.</summary>
    public sealed class FormationEntry
    {
        public string Label = "", Detail = "";
        public Formation Formation;
        public List<SetEntry> Sets = new List<SetEntry>();
        public int PlayCount { get { int n = 0; foreach (SetEntry s in Sets) n += s.Plays.Count; return n; } }
    }

    /// <summary>
    /// The pure navigation model of the full play-call screen (2K5 / NCAA style): Formation -> Set -> Play cards (three per page) for offense,
    /// defense and special teams, plus the Ask Coach, Recent and Special Teams tabs, type filters, play flipping, the timeout request and
    /// the play clock. Driven by D-pad / A / B / LB / RB; the UI only renders what this class says and the director feeds it the input.
    /// </summary>
    public sealed class PlayCallModel
    {
        public const int PageSize = 3;
        public readonly Playbook Book;
        public readonly PlaySide Side;
        public readonly CallSituation Situation = new CallSituation();
        public readonly List<CallTab> Tabs = new List<CallTab>();
        public readonly List<FormationEntry> Formations = new List<FormationEntry>();
        public List<CoachPick> CoachPicks = new List<CoachPick>();
        public List<Play> RecentPlays = new List<Play>();
        public List<Play> SpecialPlays = new List<Play>();

        public CallTab Tab { get; private set; }
        public CallStage Stage { get; private set; }
        public CallFilter Filter { get; private set; }
        public bool Flip { get; private set; }
        public int FormationIndex { get; private set; }
        public int SetIndex { get; private set; }
        public int PlayIndex { get; private set; }
        public int Version { get; private set; }
        public Play Chosen { get; private set; }
        public bool TimeoutRequested { get; private set; }
        public bool TimeoutBlocked { get; private set; }
        /// <summary>Name of the last formation family called (the screen opens on it).</summary>
        public string LastFormation = "";

        readonly List<Play> _plays = new List<Play>();

        public PlayCallModel(Playbook book, PlaySide side, bool includePlaybookTab, bool includeSpecialTab)
        {
            Book = book; Side = side;
            if (includePlaybookTab && side != PlaySide.Special) { Tabs.Add(CallTab.Playbook); Tabs.Add(CallTab.AskCoach); Tabs.Add(CallTab.Recent); }
            if (includeSpecialTab) Tabs.Add(CallTab.Special);
            if (Tabs.Count == 0) Tabs.Add(CallTab.Special);
            if (includePlaybookTab && side != PlaySide.Special) BuildFormations();
        }

        // ================================================================================================ building
        void BuildFormations()
        {
            var families = new List<string>();
            foreach (Formation f in Book.Formations)
            {
                if (f.Side != Side || families.Contains(f.Family)) continue;
                families.Add(f.Family);
            }
            foreach (string fam in families)
            {
                Formation baseF = Book.FindFormation(fam);
                var entry = new FormationEntry { Label = fam.ToUpperInvariant(), Formation = baseF };
                if (Side == PlaySide.Offense)
                {
                    foreach (Formation f in Book.Formations)
                    {
                        if (f.Side != Side || f.Family != fam) continue;
                        var set = new SetEntry { Label = f.SetName == "Base" ? "BASE" : f.SetName.ToUpperInvariant(), Formation = f, Detail = PersonnelText(f) };
                        set.Plays = FilterNonSpecial(Book.PlaysFor(f.Name));
                        if (set.Plays.Count > 0) entry.Sets.Add(set);
                    }
                    entry.Detail = PersonnelText(baseF);
                }
                else
                {
                    List<Play> all = FilterNonSpecial(Book.PlaysFor(fam));
                    AddGroup(entry, baseF, "ZONE COVERAGE", all, PlayCategory.ZoneCoverage);
                    AddGroup(entry, baseF, "MAN COVERAGE", all, PlayCategory.ManCoverage);
                    AddGroup(entry, baseF, "BLITZ", all, PlayCategory.Blitz);
                    var rest = all.FindAll(p => p.Category == PlayCategory.GoalLine || p.Category == PlayCategory.Prevent);
                    if (rest.Count > 0) entry.Sets.Add(new SetEntry { Label = baseF.Name == DefaultPlaybooks.GoalLineD ? "GOAL LINE" : "PREVENT", Formation = baseF, Plays = rest, Detail = rest.Count + " PLAYS" });
                    entry.Detail = baseF.Unit;
                }
                if (entry.Sets.Count > 0) Formations.Add(entry);
            }
        }

        static void AddGroup(FormationEntry entry, Formation f, string label, List<Play> all, PlayCategory cat)
        {
            var list = all.FindAll(p => p.Category == cat);
            if (list.Count > 0) entry.Sets.Add(new SetEntry { Label = label, Formation = f, Plays = list, Detail = list.Count + " PLAYS" });
        }

        static List<Play> FilterNonSpecial(List<Play> plays)
        {
            plays.RemoveAll(p => p.Category == PlayCategory.Kneel || p.Category == PlayCategory.Spike);
            return plays;
        }

        public static string PersonnelText(Formation f)
        {
            int rb = f.Count(Position.RB) + f.Count(Position.FB), te = f.Count(Position.TE), wr = f.Count(Position.WR);
            if (f.Side == PlaySide.Defense) return f.Unit;
            return rb + "RB " + te + "TE " + wr + "WR";
        }

        // ================================================================================================ opening
        /// <summary>Opens the screen: lands on the last formation when known (and on the Special tab when the playbook has nothing to offer).</summary>
        public void Open(CallTab? tab = null)
        {
            Tab = tab ?? Tabs[0];
            if (!Tabs.Contains(Tab)) Tab = Tabs[0];
            Chosen = null; TimeoutRequested = false; Flip = false; Filter = CallFilter.All;
            FormationIndex = 0; SetIndex = 0; PlayIndex = 0;
            for (int i = 0; i < Formations.Count; i++) if (Formations[i].Label == LastFormation.ToUpperInvariant()) FormationIndex = i;
            Stage = Tab == CallTab.Playbook ? CallStage.Formation : CallStage.Play;
            RebuildPlays();
            Touch();
        }

        void Touch() { Version++; }

        // ================================================================================================ the lists
        public FormationEntry CurrentFormation { get { return Formations.Count > 0 ? Formations[Math.Max(0, Math.Min(FormationIndex, Formations.Count - 1))] : null; } }
        public SetEntry CurrentSet { get { var f = CurrentFormation; return f != null && f.Sets.Count > 0 ? f.Sets[Math.Max(0, Math.Min(SetIndex, f.Sets.Count - 1))] : null; } }

        /// <summary>Plays of the current view after the type filter.</summary>
        public IList<Play> Plays { get { return _plays; } }
        public int PageCount { get { return Math.Max(1, (_plays.Count + PageSize - 1) / PageSize); } }
        public int Page { get { return Math.Min(PageCount - 1, PlayIndex / PageSize); } }

        void RebuildPlays()
        {
            _plays.Clear();
            IEnumerable<Play> src = null;
            switch (Tab)
            {
                case CallTab.AskCoach: foreach (CoachPick p in CoachPicks) _plays.Add(p.Play); break;
                case CallTab.Recent: src = RecentPlays; break;
                case CallTab.Special: src = SpecialPlays; break;
                default: { var s = CurrentSet; src = s != null ? s.Plays : new List<Play>(); break; }
            }
            if (src != null) foreach (Play p in src) if (Matches(p)) _plays.Add(p);
            PlayIndex = Math.Max(0, Math.Min(PlayIndex, _plays.Count - 1));
        }

        bool Matches(Play p)
        {
            if (Tab == CallTab.Special || Tab == CallTab.AskCoach) return true;
            switch (Filter)
            {
                case CallFilter.All: return true;
                case CallFilter.Run: return p.IsRun;
                case CallFilter.Pass: return p.Category == PlayCategory.ShortPass || p.Category == PlayCategory.MediumPass || p.Category == PlayCategory.DeepPass;
                case CallFilter.PlayAction: return p.Category == PlayCategory.PlayAction;
                case CallFilter.Screen: return p.Category == PlayCategory.Screen;
                case CallFilter.Zone: return p.Category == PlayCategory.ZoneCoverage;
                case CallFilter.Man: return p.Category == PlayCategory.ManCoverage;
                case CallFilter.Blitz: return p.Category == PlayCategory.Blitz;
            }
            return true;
        }

        public CallFilter[] FiltersFor()
        {
            if (Side == PlaySide.Defense) return new[] { CallFilter.All, CallFilter.Zone, CallFilter.Man, CallFilter.Blitz };
            return new[] { CallFilter.All, CallFilter.Run, CallFilter.Pass, CallFilter.PlayAction, CallFilter.Screen };
        }

        public static string FilterName(CallFilter f)
        {
            switch (f)
            {
                case CallFilter.All: return "ALL"; case CallFilter.Run: return "RUN"; case CallFilter.Pass: return "PASS"; case CallFilter.PlayAction: return "PLAY ACTION";
                case CallFilter.Screen: return "SCREEN"; case CallFilter.Zone: return "ZONE"; case CallFilter.Man: return "MAN"; default: return "BLITZ";
            }
        }

        public static string TabName(CallTab t)
        {
            switch (t) { case CallTab.Playbook: return "PLAYBOOK"; case CallTab.AskCoach: return "ASK COACH"; case CallTab.Recent: return "RECENT"; default: return "SPECIAL TEAMS"; }
        }

        /// <summary>The play as it will be called and drawn (mirrored when Flip is on).</summary>
        public Play Displayed(Play p)
        {
            if (p == null) return null;
            if (Flip && p.Side == PlaySide.Offense && Tab != CallTab.Special && !p.Mirrored) return PlayEditing.Mirror(p);
            return p;
        }

        /// <summary>The (up to three) plays on the current page.</summary>
        public List<Play> PagePlays()
        {
            var l = new List<Play>();
            int start = Page * PageSize;
            for (int i = start; i < start + PageSize && i < _plays.Count; i++) l.Add(_plays[i]);
            return l;
        }

        public int SelectedOnPage { get { return PlayIndex - Page * PageSize; } }
        public Play Highlighted { get { return PlayIndex >= 0 && PlayIndex < _plays.Count ? _plays[PlayIndex] : null; } }
        public string ReasonFor(Play p)
        {
            if (Tab != CallTab.AskCoach) return "";
            foreach (CoachPick c in CoachPicks) if (c.Play == p) return c.Reason;
            return "";
        }

        public string Breadcrumb
        {
            get
            {
                if (Tab != CallTab.Playbook) return TabName(Tab);
                string s = CurrentFormation != null ? CurrentFormation.Label : "";
                if (Stage == CallStage.Formation) return "FORMATION";
                var set = CurrentSet;
                if (Stage == CallStage.Set) return s + "  >  SET";
                return s + (set != null && CurrentFormation.Sets.Count > 1 ? "  >  " + set.Label : "") + "  >  PLAYS";
            }
        }

        public bool CanFlip { get { return Side == PlaySide.Offense && Tab != CallTab.Special && Stage == CallStage.Play; } }

        // ================================================================================================ input
        public void Navigate(int dx, int dy)
        {
            if (Chosen != null) return;
            if (Stage == CallStage.Formation && Tab == CallTab.Playbook)
            {
                if (dy != 0 && Formations.Count > 0) { FormationIndex = Wrap(FormationIndex + (dy < 0 ? 1 : -1), Formations.Count); SetIndex = 0; PlayIndex = 0; Touch(); }
                else if (dx > 0) Confirm();
            }
            else if (Stage == CallStage.Set)
            {
                var f = CurrentFormation;
                if (dy != 0 && f != null) { SetIndex = Wrap(SetIndex + (dy < 0 ? 1 : -1), f.Sets.Count); PlayIndex = 0; Touch(); }
                else if (dx > 0) Confirm();
                else if (dx < 0) Back();
            }
            else
            {
                if (dx != 0 && _plays.Count > 0) { PlayIndex = Math.Max(0, Math.Min(_plays.Count - 1, PlayIndex + dx)); Touch(); }
                if (dy != 0 && Tab != CallTab.AskCoach && Tab != CallTab.Special) CycleFilter(dy < 0 ? 1 : -1);
            }
        }

        public void Confirm()
        {
            if (Chosen != null) return;
            if (Stage == CallStage.Formation && Tab == CallTab.Playbook)
            {
                if (CurrentFormation == null) return;
                LastFormation = CurrentFormation.Label;
                SetIndex = 0;
                Stage = CurrentFormation.Sets.Count > 1 ? CallStage.Set : CallStage.Play;
                PlayIndex = 0; Filter = CallFilter.All; RebuildPlays(); Touch();
            }
            else if (Stage == CallStage.Set)
            {
                Stage = CallStage.Play; PlayIndex = 0; RebuildPlays(); Touch();
            }
            else if (Highlighted != null)
            {
                Chosen = Displayed(Highlighted);
                Touch();
            }
        }

        /// <summary>B: one step up. Returns false when already at the top (nothing to cancel: a play must be called).</summary>
        public bool Back()
        {
            if (Chosen != null) return false;
            if (Tab != CallTab.Playbook && Tabs.Contains(CallTab.Playbook)) { SetTab(CallTab.Playbook); return true; }
            if (Stage == CallStage.Play && Tab == CallTab.Playbook)
            {
                Stage = CurrentFormation != null && CurrentFormation.Sets.Count > 1 ? CallStage.Set : CallStage.Formation;
                Flip = false; Touch(); return true;
            }
            if (Stage == CallStage.Set) { Stage = CallStage.Formation; Touch(); return true; }
            return false;
        }

        /// <summary>LB / RB: page through the play cards.</summary>
        public void PageStep(int d)
        {
            if (Stage != CallStage.Play || _plays.Count == 0) return;
            int page = Math.Max(0, Math.Min(PageCount - 1, Page + d));
            PlayIndex = Math.Min(_plays.Count - 1, page * PageSize + Math.Min(SelectedOnPage, PageSize - 1));
            Touch();
        }

        public void CycleTab(int d)
        {
            if (Chosen != null) return;
            int i = Tabs.IndexOf(Tab);
            SetTab(Tabs[Wrap(i + d, Tabs.Count)]);
        }

        public void SetTab(CallTab t)
        {
            if (!Tabs.Contains(t)) return;
            Tab = t; PlayIndex = 0; Filter = CallFilter.All;
            Stage = t == CallTab.Playbook ? CallStage.Formation : CallStage.Play;
            Flip = false;
            RebuildPlays(); Touch();
        }

        public void CycleFilter(int d)
        {
            if (Stage != CallStage.Play || Tab == CallTab.AskCoach || Tab == CallTab.Special) return;
            CallFilter[] fs = FiltersFor();
            int i = Array.IndexOf(fs, Filter); if (i < 0) i = 0;
            Filter = fs[Wrap(i + d, fs.Length)];
            PlayIndex = 0; RebuildPlays(); Touch();
        }

        public void ToggleFlip() { if (CanFlip) { Flip = !Flip; Touch(); } }

        public void RequestTimeout()
        {
            if (Chosen != null) return;
            if (Situation.Timeouts <= 0) { TimeoutBlocked = true; Touch(); return; }
            TimeoutRequested = true; Touch();
        }

        public void ClearTimeoutRequest() { TimeoutRequested = false; TimeoutBlocked = false; }

        /// <summary>The play clock ran out: the highlighted play is called (or the first suggestion).</summary>
        public Play Expire()
        {
            if (Chosen != null) return Chosen;
            Play p = Highlighted;
            if (p == null)
            {
                if (CoachPicks.Count > 0) p = CoachPicks[0].Play;
                else if (Formations.Count > 0 && Formations[0].Sets.Count > 0 && Formations[0].Sets[0].Plays.Count > 0) p = Formations[0].Sets[0].Plays[0];
                else if (SpecialPlays.Count > 0) p = SpecialPlays[0];
            }
            Chosen = Displayed(p);
            Touch();
            return Chosen;
        }

        static int Wrap(int v, int n) { return n <= 0 ? 0 : ((v % n) + n) % n; }
    }
}
