using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    /// <summary>The classic tabs come first (their numeric values are stored by older code); v0.7 added the call-sheet tabs (Formation, Concept, PlayType, Personnel, Favorites). AskCoach is shown as "SUGGESTED" on the sheet.</summary>
    public enum CallTab { Playbook, AskCoach, Recent, Special, Formation, Concept, PlayType, Personnel, Favorites }
    public enum CallStage { Formation, Set, Play }
    public enum CallFilter { All, Run, Pass, PlayAction, Screen, Zone, Man, Blitz }

    /// <summary>What the play-call screen shows about the game.</summary>
    public sealed class CallSituation
    {
        public string Title = "OFFENSE", TeamName = "", DownText = "", SpotText = "", ScoreText = "", ClockText = "", CoachStyle = "";
        public int Timeouts, Quarter;
        /// <summary>Length of the play clock the screen counts down from (QA-506: follows the rules' live-ball clock until the caller sets the real one).</summary>
        public int PlayClockSeconds = new RuleSet().PlayClockLive;
        /// <summary>True when <see cref="ScoreText"/> is written HOME first (the legacy contract). The scorebug lists the AWAY team first, so <see cref="DisplayScore"/> swaps the sides (QA-158).</summary>
        public bool ScoreIsHomeFirst = true;

        /// <summary>Sets <see cref="ScoreText"/> in scorebug order (away team first).</summary>
        public void SetScore(string awayAbbr, int awayScore, string homeAbbr, int homeScore)
        {
            ScoreText = awayAbbr + " " + awayScore + "  -  " + homeAbbr + " " + homeScore;
            ScoreIsHomeFirst = false;
        }

        /// <summary>The score as the call screen prints it: away team first like the scorebug, single spaces around the dash.</summary>
        public string DisplayScore
        {
            get
            {
                if (string.IsNullOrEmpty(ScoreText)) return "";
                string t = ScoreText.Replace("  -  ", " - ");
                if (!ScoreIsHomeFirst) return t;
                int i = t.IndexOf(" - ", StringComparison.Ordinal);
                return i < 0 ? t : t.Substring(i + 3) + " - " + t.Substring(0, i);
            }
        }
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

    /// <summary>One group of the call sheet (v0.7): a formation family, a concept, a play type or a personnel package, with the plays it holds.</summary>
    public sealed class CallGroup
    {
        public string Label = "", Detail = "";
        public Formation Formation;
        public List<Play> Plays = new List<Play>();
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
        /// <summary>The plays the user calls most (set by the match flow from the stored call counts); the sheet's FAVORITES tab.</summary>
        public List<Play> FavoritePlays = new List<Play>();
        /// <summary>v0.7 call sheet: always on the play stage, tabs SUGGESTED / FORMATION / CONCEPT / PLAY TYPE / PERSONNEL / RECENT / FAVORITES (+ SPECIAL TEAMS), a group list beside the cards, quick search.</summary>
        public bool SheetMode { get; private set; }
        public readonly List<CallGroup> Groups = new List<CallGroup>();
        public int GroupIndex { get; private set; }
        public string SearchText { get; private set; } = "";
        /// <summary>True when <see cref="SearchText"/> is a single initial letter chosen with the pad (matches the start of a name) instead of typed text (matches anywhere).</summary>
        public bool SearchIsLetter { get; private set; }

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

        public PlayCallModel(Playbook book, PlaySide side, bool includePlaybookTab, bool includeSpecialTab) : this(book, side, includePlaybookTab, includeSpecialTab, false) { }

        public PlayCallModel(Playbook book, PlaySide side, bool includePlaybookTab, bool includeSpecialTab, bool sheet)
        {
            Book = book; Side = side;
            SheetMode = sheet && includePlaybookTab && side != PlaySide.Special;
            if (SheetMode) { Tabs.Add(CallTab.AskCoach); Tabs.Add(CallTab.Formation); Tabs.Add(CallTab.Concept); Tabs.Add(CallTab.PlayType); Tabs.Add(CallTab.Personnel); Tabs.Add(CallTab.Recent); Tabs.Add(CallTab.Favorites); }
            else if (includePlaybookTab && side != PlaySide.Special) { Tabs.Add(CallTab.Playbook); Tabs.Add(CallTab.AskCoach); Tabs.Add(CallTab.Recent); }
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
                    // QA-507: one set per category that is present (no category is silently dropped), labelled from the category
                    List<Play> all = FilterNonSpecial(Book.PlaysFor(fam));
                    foreach (PlayCategory cat in DefenseSetOrder)
                        AddGroup(entry, baseF, DefenseSetLabel(cat), all, cat);
                    foreach (PlayCategory cat in DistinctCategories(all))
                        if (Array.IndexOf(DefenseSetOrder, cat) < 0) AddGroup(entry, baseF, DefenseSetLabel(cat), all, cat);
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

        /// <summary>Order of the defensive sets on the second screen.</summary>
        static readonly PlayCategory[] DefenseSetOrder = { PlayCategory.ZoneCoverage, PlayCategory.ManCoverage, PlayCategory.Blitz, PlayCategory.GoalLine, PlayCategory.Prevent };

        static List<PlayCategory> DistinctCategories(List<Play> plays)
        {
            var l = new List<PlayCategory>();
            foreach (Play p in plays) if (!l.Contains(p.Category)) l.Add(p.Category);
            return l;
        }

        /// <summary>Label of a defensive set row: the coverage categories read "ZONE COVERAGE" / "MAN COVERAGE", everything else its category name.</summary>
        public static string DefenseSetLabel(PlayCategory cat)
        {
            switch (cat)
            {
                case PlayCategory.ZoneCoverage: return "ZONE COVERAGE";
                case PlayCategory.ManCoverage: return "MAN COVERAGE";
                default: return CategoryName(cat);
            }
        }

        /// <summary>Player-facing name of a play category ("INSIDE RUN", "QUICK PASS", "FIELD GOAL"...). Never a raw enum identifier (QA-508).</summary>
        public static string CategoryName(PlayCategory c)
        {
            switch (c)
            {
                case PlayCategory.InsideRun: return "INSIDE RUN"; case PlayCategory.OutsideRun: return "OUTSIDE RUN"; case PlayCategory.Draw: return "DRAW"; case PlayCategory.QbRun: return "QB RUN";
                case PlayCategory.ShortPass: return "QUICK PASS"; case PlayCategory.MediumPass: return "INTERMEDIATE"; case PlayCategory.DeepPass: return "DEEP PASS"; case PlayCategory.PlayAction: return "PLAY ACTION";
                case PlayCategory.Screen: return "SCREEN"; case PlayCategory.Kneel: return "KNEEL"; case PlayCategory.Spike: return "SPIKE";
                case PlayCategory.ZoneCoverage: return "ZONE"; case PlayCategory.ManCoverage: return "MAN"; case PlayCategory.Blitz: return "BLITZ";
                case PlayCategory.GoalLine: return "GOAL LINE"; case PlayCategory.Prevent: return "PREVENT"; case PlayCategory.Kickoff: return "KICKOFF"; case PlayCategory.Onside: return "ONSIDE KICK";
                case PlayCategory.Punt: return "PUNT"; case PlayCategory.FieldGoal: return "FIELD GOAL"; case PlayCategory.PointAfter: return "EXTRA POINT"; case PlayCategory.KickReturn: return "KICK RETURN";
                case PlayCategory.PuntReturn: return "PUNT RETURN"; case PlayCategory.FieldGoalBlock: return "KICK BLOCK";
                default: return SpacedUpper(c.ToString());
            }
        }

        /// <summary>Name of a playbook set ("Offense - Inside Run"): side plus the title-cased category name, for lists that show set names (QA-508).</summary>
        public static string SetNameFor(PlaySide side, PlayCategory cat)
        {
            string name = CategoryName(cat).ToLowerInvariant();
            var sb = new System.Text.StringBuilder(side.ToString());
            sb.Append(" - ");
            bool up = true;
            for (int i = 0; i < name.Length; i++) { char ch = name[i]; sb.Append(up ? char.ToUpperInvariant(ch) : ch); up = ch == ' '; }
            return sb.ToString();
        }

        /// <summary>"InsideZone" -> "INSIDE ZONE", "QbKeep" -> "QB KEEP" (an enum identifier turned into player-facing caps text).</summary>
        public static string SpacedUpper(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(s[i]));
            }
            return sb.ToString();
        }

        /// <summary>"COVER 3", "COVER 2 MAN", "TAMPA 2"...: the shell name for cards and reasons.</summary>
        public static string SchemeName(CoverageScheme s)
        {
            switch (s)
            {
                case CoverageScheme.Cover0: return "COVER 0"; case CoverageScheme.Cover1: return "COVER 1"; case CoverageScheme.Cover2: return "COVER 2";
                case CoverageScheme.Cover2Man: return "COVER 2 MAN"; case CoverageScheme.Cover3: return "COVER 3"; case CoverageScheme.Cover4: return "COVER 4";
                case CoverageScheme.Cover6: return "COVER 6"; case CoverageScheme.Tampa2: return "TAMPA 2"; case CoverageScheme.Prevent: return "PREVENT";
                default: return "";
            }
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
            Chosen = null; TimeoutRequested = false; Flip = false; Filter = CallFilter.All; SearchText = ""; SearchIsLetter = false;
            FormationIndex = 0; SetIndex = 0; PlayIndex = 0; GroupIndex = 0;
            for (int i = 0; i < Formations.Count; i++) if (Formations[i].Label == LastFormation.ToUpperInvariant()) FormationIndex = i;
            Stage = Tab == CallTab.Playbook ? CallStage.Formation : CallStage.Play;
            if (SheetMode) { Stage = CallStage.Play; RebuildGroups(); }
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
            if (SheetMode && SearchText.Length > 0 && Tab != CallTab.Special)
            {
                foreach (Play p in AllSheetPlays()) if (SearchMatches(p) && Matches(p)) _plays.Add(p);
                PlayIndex = Math.Max(0, Math.Min(PlayIndex, _plays.Count - 1));
                return;
            }
            switch (Tab)
            {
                case CallTab.AskCoach: foreach (CoachPick p in CoachPicks) _plays.Add(p.Play); break;
                case CallTab.Recent: src = RecentPlays; break;
                case CallTab.Favorites: src = FavoritePlays; break;
                case CallTab.Special: src = InterleaveByCategory(SpecialPlays); break;
                case CallTab.Formation: case CallTab.Concept: case CallTab.PlayType: case CallTab.Personnel: src = CurrentGroupPlays(); break;
                default: src = SetPlays(); break;
            }
            if (src != null) foreach (Play p in src) if (Matches(p)) _plays.Add(p);
            PlayIndex = Math.Max(0, Math.Min(PlayIndex, _plays.Count - 1));
        }

        /// <summary>The unfiltered plays of the Playbook-tab set the cursor is in.</summary>
        IList<Play> SetPlays() { var s = CurrentSet; return s != null ? (IList<Play>)s.Plays : new List<Play>(); }

        /// <summary>
        /// Special teams plays in round-robin order of their category (punt, field goal, kickoff... each category shows its first play before any category
        /// shows a second one), so the first page of the Special Teams tab is not nine punt variants (QA-64). Stable inside a category.
        /// </summary>
        public static List<Play> InterleaveByCategory(IList<Play> plays)
        {
            var res = new List<Play>();
            if (plays == null) return res;
            var cats = new List<PlayCategory>(); var lists = new List<List<Play>>();
            foreach (Play p in plays)
            {
                int k = cats.IndexOf(p.Category);
                if (k < 0) { cats.Add(p.Category); lists.Add(new List<Play>()); k = cats.Count - 1; }
                lists[k].Add(p);
            }
            for (int round = 0; res.Count < plays.Count; round++)
                foreach (List<Play> l in lists) if (round < l.Count) res.Add(l[round]);
            return res;
        }

        static bool FilterMatches(CallFilter f, Play p)
        {
            switch (f)
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

        bool Matches(Play p)
        {
            if (Tab == CallTab.Special || Tab == CallTab.AskCoach) return true;
            return FilterMatches(Filter, p);
        }

        // ================================================================================================ the call sheet (v0.7)
        /// <summary>The unfiltered plays behind the current tab (what the type filters look at); null for the tabs that have no filters.</summary>
        IEnumerable<Play> ViewPlays()
        {
            if (SheetMode && SearchText.Length > 0 && Tab != CallTab.Special) return AllSheetPlays();
            switch (Tab)
            {
                case CallTab.Recent: return RecentPlays;
                case CallTab.Favorites: return FavoritePlays;
                case CallTab.Formation: case CallTab.Concept: case CallTab.PlayType: case CallTab.Personnel: return CurrentGroupPlays();
                case CallTab.Playbook: return SetPlays();
                default: return null;
            }
        }

        List<Play> CurrentGroupPlays()
        {
            return Groups.Count > 0 ? Groups[Math.Max(0, Math.Min(GroupIndex, Groups.Count - 1))].Plays : new List<Play>();
        }

        public CallGroup CurrentGroup { get { return Groups.Count > 0 ? Groups[Math.Max(0, Math.Min(GroupIndex, Groups.Count - 1))] : null; } }

        /// <summary>Every callable play of the playbook for this side (what the quick search looks through): the formations' plays in playbook order.</summary>
        public List<Play> AllSheetPlays()
        {
            var l = new List<Play>();
            foreach (FormationEntry fe in Formations) foreach (SetEntry se in fe.Sets) l.AddRange(se.Plays);
            return l;
        }

        /// <summary>Whether the tab shows a group list beside the cards (formation families, concepts, play types, personnel packages).</summary>
        public bool TabHasGroups { get { return SheetMode && (Tab == CallTab.Formation || Tab == CallTab.Concept || Tab == CallTab.PlayType || Tab == CallTab.Personnel) && SearchText.Length == 0; } }

        void RebuildGroups()
        {
            Groups.Clear();
            if (!SheetMode) return;
            switch (Tab)
            {
                case CallTab.Formation:
                    foreach (FormationEntry fe in Formations)
                    {
                        var g = new CallGroup { Label = fe.Label, Detail = fe.Detail, Formation = fe.Formation };
                        foreach (SetEntry se in fe.Sets) g.Plays.AddRange(se.Plays);
                        if (g.Plays.Count > 0) Groups.Add(g);
                    }
                    break;
                case CallTab.Concept:
                {
                    var byName = new Dictionary<string, CallGroup>();
                    foreach (Play p in AllSheetPlays())
                    {
                        string key = p.Name.ToUpperInvariant(); CallGroup g;
                        if (!byName.TryGetValue(key, out g)) { g = new CallGroup { Label = key, Formation = Book.FindFormation(p.FormationName) }; byName[key] = g; Groups.Add(g); }
                        g.Plays.Add(p);
                    }
                    Groups.Sort((a, b) => string.CompareOrdinal(a.Label, b.Label));
                    foreach (CallGroup g in Groups) g.Detail = g.Plays.Count + (g.Plays.Count == 1 ? " FORMATION" : " FORMATIONS");
                    break;
                }
                case CallTab.PlayType:
                {
                    var order = new List<PlayCategory>(); var map = new Dictionary<PlayCategory, CallGroup>();
                    foreach (Play p in AllSheetPlays())
                    {
                        CallGroup g;
                        if (!map.TryGetValue(p.Category, out g)) { g = new CallGroup { Label = CategoryName(p.Category), Formation = Book.FindFormation(p.FormationName) }; map[p.Category] = g; order.Add(p.Category); }
                        g.Plays.Add(p);
                    }
                    order.Sort((a, b) => ((int)a).CompareTo((int)b));
                    foreach (PlayCategory c in order) { map[c].Detail = map[c].Plays.Count + " PLAYS"; Groups.Add(map[c]); }
                    break;
                }
                case CallTab.Personnel:
                {
                    var map = new Dictionary<string, CallGroup>();
                    foreach (Play p in AllSheetPlays())
                    {
                        Formation f = Book.FindFormation(p.FormationName); string key = f != null ? PersonnelText(f) : "OTHER"; CallGroup g;
                        if (!map.TryGetValue(key, out g)) { g = new CallGroup { Label = key.ToUpperInvariant(), Formation = f }; map[key] = g; Groups.Add(g); }
                        g.Plays.Add(p);
                    }
                    Groups.Sort((a, b) => b.Plays.Count.CompareTo(a.Plays.Count));
                    foreach (CallGroup g in Groups) g.Detail = g.Plays.Count + " PLAYS";
                    break;
                }
            }
            GroupIndex = 0;
            if (Tab == CallTab.Formation) for (int i = 0; i < Groups.Count; i++) if (Groups[i].Label == (LastFormation ?? "").ToUpperInvariant()) GroupIndex = i;
        }

        /// <summary>Sheet: next / previous group (D-pad down / up).</summary>
        public void StepGroup(int d)
        {
            if (!TabHasGroups || Groups.Count == 0 || Chosen != null) return;
            GroupIndex = Wrap(GroupIndex + d, Groups.Count); PlayIndex = 0; Filter = CallFilter.All; RebuildPlays(); Touch();
        }

        // ---- quick search by play name
        bool SearchMatches(Play p)
        {
            if (SearchText.Length == 0) return true;
            string n = p.Name ?? "";
            if (SearchIsLetter) return n.StartsWith(SearchText, StringComparison.OrdinalIgnoreCase);
            return n.IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0 || (p.FormationName ?? "").IndexOf(SearchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void SetSearch(string text, bool letter = false)
        {
            if (!SheetMode || Chosen != null) return;
            text = (text ?? "").Trim();
            if (text == SearchText && letter == SearchIsLetter) return;
            SearchText = text; SearchIsLetter = letter && text.Length == 1; PlayIndex = 0; Filter = CallFilter.All; RebuildPlays(); Touch();
        }

        public void AppendSearch(char c) { SetSearch(SearchText + c, false); }
        public void BackspaceSearch() { if (SearchText.Length > 0) SetSearch(SearchText.Substring(0, SearchText.Length - 1), false); }
        public void ClearSearch() { SetSearch("", false); }

        /// <summary>Pad quick find: RS left / right steps the initial letter (A..Z, then off). Only letters that start a play are offered.</summary>
        public void CycleLetter(int d)
        {
            if (!SheetMode || Chosen != null) return;
            var letters = new List<char>();
            foreach (Play p in AllSheetPlays()) { if (string.IsNullOrEmpty(p.Name)) continue; char c = char.ToUpperInvariant(p.Name[0]); if (!letters.Contains(c)) letters.Add(c); }
            letters.Sort();
            if (letters.Count == 0) return;
            int cur = SearchText.Length == 1 && SearchIsLetter ? letters.IndexOf(char.ToUpperInvariant(SearchText[0])) : -1;
            int next = cur + d;
            if (cur < 0 && d < 0) next = letters.Count - 1;
            if (next < 0 || next >= letters.Count) { ClearSearch(); return; }
            SetSearch(letters[next].ToString(), true);
        }

        /// <summary>
        /// The type filters worth offering: ALL plus every filter that matches at least one play of the unfiltered view (QA-616: no filter that opens an empty page).
        /// The current filter is always included so the row never loses the selection.
        /// </summary>
        public CallFilter[] FiltersFor()
        {
            CallFilter[] candidates = Side == PlaySide.Defense
                ? new[] { CallFilter.All, CallFilter.Zone, CallFilter.Man, CallFilter.Blitz }
                : new[] { CallFilter.All, CallFilter.Run, CallFilter.Pass, CallFilter.PlayAction, CallFilter.Screen };
            IEnumerable<Play> src = ViewPlays();
            if (src == null) return candidates;
            var res = new List<CallFilter>();
            foreach (CallFilter f in candidates)
            {
                bool any = f == CallFilter.All || f == Filter;
                if (!any) foreach (Play p in src) if (FilterMatches(f, p)) { any = true; break; }
                if (any) res.Add(f);
            }
            return res.ToArray();
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
            switch (t)
            {
                case CallTab.Playbook: return "PLAYBOOK"; case CallTab.AskCoach: return "ASK COACH"; case CallTab.Recent: return "RECENT";
                case CallTab.Formation: return "FORMATION"; case CallTab.Concept: return "CONCEPT"; case CallTab.PlayType: return "PLAY TYPE"; case CallTab.Personnel: return "PERSONNEL"; case CallTab.Favorites: return "FAVORITES";
                default: return "SPECIAL TEAMS";
            }
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
            int shared = 0; string raw = null;
            foreach (CoachPick c in CoachPicks) if (c.Play == p) { raw = c.Reason; break; }
            if (raw == null) return "";
            foreach (CoachPick c in CoachPicks) if (c.Reason == raw) shared++;
            return RefineReason(p, raw, shared > 1);
        }

        /// <summary>
        /// Turns the coach's situational line into one that fits this card (QA-66): a run is never "set up the pass", and when several picks carry the same
        /// line each gets its own concept in front of it so the three cards read differently.
        /// </summary>
        public static string RefineReason(Play p, string reason, bool shared)
        {
            if (p == null) return reason ?? "";
            reason = reason ?? "";
            if (p.IsRun && (reason == "1ST DOWN: SET UP THE PASS" || reason == "KEEP THE CHAINS MOVING")) reason = "STAY AHEAD OF THE CHAINS";
            else if (p.IsPass && reason == "MOVE THE CHAINS") reason = "MOVE THE CHAINS THROUGH THE AIR";
            else if (!shared) return reason;
            string concept = ConceptLabel(p);
            if (concept.Length == 0 || reason.StartsWith(concept, StringComparison.Ordinal)) return reason;
            return concept + ": " + reason;
        }

        /// <summary>Short play-specific label for a reason line: the carry style of a run, the category of a pass, the shell of a defense.</summary>
        public static string ConceptLabel(Play p)
        {
            if (p == null) return "";
            if (p.Side == PlaySide.Defense) { string s = SchemeName(p.Scheme); return s.Length > 0 ? s : CategoryName(p.Category); }
            if (p.IsRun)
            {
                foreach (Assignment a in p.Assignments)
                {
                    var c = a as CarryAssignment;
                    if (c != null && !c.IsFake) return SpacedUpper(c.Style.ToString());
                }
            }
            return CategoryName(p.Category);
        }

        public string Breadcrumb
        {
            get
            {
                if (SheetMode)
                {
                    if (SearchText.Length > 0) return "SEARCH  >  " + SearchText.ToUpperInvariant();
                    var g = CurrentGroup;
                    return TabName(Tab) + (TabHasGroups && g != null ? "  >  " + g.Label : "");
                }
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
            if (SheetMode)
            {
                if (dx != 0 && _plays.Count > 0) { PlayIndex = Math.Max(0, Math.Min(_plays.Count - 1, PlayIndex + dx)); Touch(); }
                else if (dy != 0) StepGroup(dy < 0 ? 1 : -1);
                return;
            }
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
            if (SheetMode)
            {
                if (Highlighted == null) return;
                if (Tab == CallTab.Formation && CurrentGroup != null) LastFormation = CurrentGroup.Label;
                Chosen = Displayed(Highlighted); Touch(); return;
            }
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
            if (SheetMode)
            {
                // B: clear a search first, then jump back to the coach's suggestions (one press from anywhere)
                if (SearchText.Length > 0) { ClearSearch(); return true; }
                if (Tab != CallTab.AskCoach && Tabs.Contains(CallTab.AskCoach)) { SetTab(CallTab.AskCoach); return true; }
                return false;
            }
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
            Tab = t; PlayIndex = 0; Filter = CallFilter.All; SearchText = ""; SearchIsLetter = false;
            Stage = t == CallTab.Playbook ? CallStage.Formation : CallStage.Play;
            Flip = false;
            if (SheetMode) { Stage = CallStage.Play; RebuildGroups(); }
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
                else p = FirstBookPlay();
            }
            Chosen = Displayed(p);
            Touch();
            return Chosen;
        }

        /// <summary>The first callable play of the book for this model's side (the last resort when the clock expires on an empty view, QA-505).</summary>
        Play FirstBookPlay()
        {
            foreach (Play q in Book.AllPlays)
                if (q.Side == Side && q.Category != PlayCategory.Kneel && q.Category != PlayCategory.Spike) return q;
            foreach (Play q in Book.AllPlays) if (q.Side == Side) return q;
            return null;
        }

        static int Wrap(int v, int n) { return n <= 0 ? 0 : ((v % n) + n) % n; }
    }
}
