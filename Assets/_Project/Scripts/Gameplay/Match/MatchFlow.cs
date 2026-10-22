using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;
using CoreTeam = PixFootball.Core.Team;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.Gameplay
{
    public enum Difficulty { Rookie = 0, Pro = 1, AllPro = 2 }

    /// <summary>Everything the team-select screen decides.</summary>
    [Serializable]
    public sealed class MatchConfig
    {
        public string HomeId = "buffalo_ironhorns", AwayId = "miami_reefrunners";
        public int QuarterMinutes = 5;
        public bool CollegeRules;
        public Difficulty Difficulty = Difficulty.Pro;
        public CoreTeam UserTeam = CoreTeam.Home;
        /// <summary>0 = random each game.</summary>
        public int Seed;
        /// <summary>Between-play game-clock runoff is multiplied by this ("accelerated clock"): 0.5 1.0 gives roughly 60-70 snaps in 5-minute quarters; lower values accelerate the game further.</summary>
        public float RunoffScale = 1.0f;
        /// <summary>Used by the autotest: both teams are CPU controlled.</summary>
        public bool CpuVsCpu;
        /// <summary>v0.6: what the team-select screen chose for the weather and the time of day (Random is resolved with the game seed when the match starts).</summary>
        public WeatherChoice Weather = WeatherChoice.Clear;
        public TimeChoice Time = TimeChoice.Night;
        /// <summary>Filled in when the match starts: the weather that was actually picked and how slick the ball is (0 dry .. 1 soaked).</summary>
        public WeatherKind ResolvedWeather; public TimeOfDay ResolvedTime; public float BallSlickness;

        public static int[] QuarterChoices = { 2, 3, 5, 8, 15 };

        public MatchConfig Clone() { return (MatchConfig)MemberwiseClone(); }

        /// <summary>CPU rating adjustment per difficulty.</summary>
        public int CpuRatingDelta { get { return Difficulty == Difficulty.Rookie ? -8 : (Difficulty == Difficulty.AllPro ? 6 : 0); } }
    }

    public struct PlayOption
    {
        public string Label;
        /// <summary>RUN, PASS, BLITZ, ZONE, MAN, KICK, PUNT, FG ...</summary>
        public string Tag;
        public Play Play;
    }

    public enum Headline
    {
        None, Touchdown, FieldGoal, FieldGoalMissed, ExtraPoint, TwoPointGood, TwoPointFailed, Safety, Interception, FumbleLost,
        TurnoverOnDowns, FirstDown, Sack, Incomplete, Punt, Kickoff, Penalty, Touchback, Fumble, Kneel, Spike
    }

    /// <summary>What a finished play did to the game (for banners, audio and stats).</summary>
    public sealed class PlayOutcome
    {
        public PlayResult Result;
        public PlayKind Kind;
        public CoreTeam Offense;
        /// <summary>v0.7: why the play ended (tackler, sideline, incomplete, kick result...), straight from the simulation. Null for plays that never ran.</summary>
        public PixFootball.Sim.PlayEndReason EndReason;
        public bool Applied;
        public int QuarterBefore, QuarterAfter;
        public int HomePoints, AwayPoints;
        public Headline Headline;
        public string Text = "";
        public CoreTeam? ScoringTeam;
        public bool PossessionChanged;
        public bool PenaltyAccepted;
        public string PenaltyName = "";
        public PenaltyCall PenaltyCall;
        public bool PeriodChanged { get { return QuarterAfter != QuarterBefore; } }
        public bool GameOver;
        /// <summary>UI-FLOW (QA-761/840): yards the accepted flag actually moved the ball (rule set, spot fouls and half-the-distance applied; 0 is a real value, e.g. at the one yard line); -1 while no flag has been enforced.</summary>
        public int PenaltyYards = -1;
        /// <summary>The accepted flag gave the offense a first down (automatic or by yardage).</summary>
        public bool PenaltyFirstDown;
        /// <summary>The offense earned a first down on this play (line to gain reached, also on a touchdown, or an accepted defensive flag). Set by <see cref="MatchFlow.Finish(PlaySimulation)"/>.</summary>
        public bool FirstDownGained;

        /// <summary>A never-empty tag for logs and play-by-play (QA-279): the headline text, or the play type and yardage for an ordinary run / pass. <see cref="Text"/> stays empty for those on purpose (it drives the banner).</summary>
        public string LogText
        {
            get
            {
                if (!string.IsNullOrEmpty(Text)) return Text;
                if (Result == null) return "PLAY";
                switch (Result.Type)
                {
                    case PlayType.Run: return "RUN " + Result.Yards + " YDS";
                    case PlayType.Pass: return "PASS COMPLETE " + Result.Yards + " YDS";
                    case PlayType.Kneel: return "KNEEL DOWN";
                    default: return Result.Type.ToString().ToUpperInvariant();
                }
            }
        }
    }

    /// <summary>How a <see cref="PlayType.Fumble"/> result is booked in the yardage stats (same rule for the final screen and the broadcast box score).</summary>
    public enum FumbleBooking { None, Rush, Pass }

    public struct PeriodChange
    {
        public bool Changed, Final, Halftime;
        public int From, To;
    }

    /// <summary>Running team stats for the final screen.</summary>
    public sealed class TeamStats
    {
        public int Plays, PassYards, RushYards, FirstDowns, Turnovers, Penalties, PenaltyYards, Sacks;
        /// <summary>UI-FLOW (QA-171): the standard extra rows. Sacks above = sacks MADE by this team's defense; SacksTaken = times this offense was sacked.</summary>
        public int PassAtt, PassComp, RushAtt, SacksTaken, ThirdDownAtt, ThirdDownConv, FourthDownAtt, FourthDownConv;
        public int TotalYards { get { return PassYards + RushYards; } }
    }

    /// <summary>
    /// Engine-free match flow: owns the GameState, picks CPU calls (reusing the PhysicalGameSim coordinator), builds the live
    /// PlaySimulation for each snap and applies the result with the accelerated clock. The MatchDirector drives it from Unity;
    /// tests drive it headless.
    /// </summary>
    public sealed partial class MatchFlow
    {
        public readonly MatchConfig Config;
        public readonly RuleSet Rules;
        public readonly GameState State;
        public readonly Rng Rng;
        public readonly Playbook Book;
        /// <summary>Test hook (autotest contact gallery): adjusts the options of every play simulation this flow creates.</summary>
        public static System.Action<PlaySimOptions> SimOptionsHook;
        public readonly LeagueTeam Home, Away;
        public readonly PhysicalGameSim Cpu;
        public readonly TeamStats HomeStats = new TeamStats(), AwayStats = new TeamStats();
        public readonly int[] HomeByQuarter = new int[8], AwayByQuarter = new int[8];
        public int Snaps;
        public readonly List<string> Log = new List<string>();

        Hash _nextHash = Hash.Middle;
        /// <summary>Game-long fatigue: players carry their energy from snap to snap (engine SIM-024).</summary>
        public readonly FatigueTracker Fatigue = new FatigueTracker();
        int _fatigueQuarter;

        public MatchFlow(MatchConfig cfg, LeagueTeam home, LeagueTeam away, CoreTeam openingKicker, Rng rng, Playbook book = null)
        {
            Config = cfg; Home = home; Away = away; Rng = rng;
            Rules = cfg.CollegeRules ? RuleSet.College() : RuleSet.Pro();
            Rules.QuarterMinutes = Math.Max(1, cfg.QuarterMinutes);
            Rules.OvertimeMinutes = Math.Min(Rules.OvertimeMinutes, Math.Max(2, cfg.QuarterMinutes));
            Book = book ?? DefaultPlaybooks.Create();
            State = new GameState(Rules, openingKicker);
            State.Conditions = MatchConditions.Generate(cfg.Seed != 0 ? cfg.Seed : 1234);   // the game wind (engine SIM-021): the sim, the kick meter and the HUD all read it
            Cpu = new PhysicalGameSim(home, away, State, rng.Fork(), Book);
        }

        // ------------------------------------------------------------------------------------------------ helpers
        public LeagueTeam TeamOf(CoreTeam t) { return t == CoreTeam.Home ? Home : Away; }
        public TeamStats StatsOf(CoreTeam t) { return t == CoreTeam.Home ? HomeStats : AwayStats; }
        public bool IsUser(CoreTeam t) { return !Config.CpuVsCpu && t == Config.UserTeam; }
        public CoreTeam UserTeam { get { return Config.UserTeam; } }

        public PlayFrame CurrentFrame { get { return PlayFrame.For(State); } }

        public string Abbr(CoreTeam t) { var tm = TeamOf(t); return string.IsNullOrEmpty(tm.Abbreviation) ? tm.Nickname : tm.Abbreviation; }

        // ------------------------------------------------------------------------------------------------ clock
        /// <summary>Burns the pending between-play runoff. Reports a period change when the quarter ended during it.</summary>
        public PeriodChange PrepareSnap()
        {
            int q0 = State.Quarter;
            State.RunClockToSnap();
            return new PeriodChange { Changed = State.Quarter != q0 || State.IsFinal, From = q0, To = State.Quarter, Final = State.IsFinal, Halftime = q0 == 2 && State.Quarter == 3 };
        }

        /// <summary>CPU timeout logic: the trailing team stops the clock late in a half.</summary>
        public bool TryCpuTimeout(out CoreTeam team)
        {
            team = CoreTeam.Home;
            if (State.Phase != GamePhase.Scrimmage || !State.ClockRunning || State.PendingRunoff <= 0f) return false;
            if (!(State.Quarter == 2 || State.Quarter == 4) || State.ClockSeconds > 130f) return false;
            foreach (CoreTeam t in new[] { CoreTeam.Home, CoreTeam.Away })
            {
                if (IsUser(t) || State.TimeoutsOf(t) <= 0) continue;
                int diff = State.ScoreOf(t) - State.ScoreOf(t.Opponent());
                bool wantsClock = diff < 0 || (State.Quarter == 2 && diff <= 0 && State.Possession == t);
                if (!wantsClock) continue;
                if (State.Possession == t && State.ClockSeconds > 90f) continue;
                if (!Rng.Chance(0.5f)) continue;
                if (State.CallTimeout(t)) { team = t; return true; }
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------------ play calling
        Play Find(string name, params string[] formations)
        {
            foreach (string f in formations)
                foreach (Play p in Book.AllPlays)
                    if (p.Name == name && p.FormationName == f) return p;
            foreach (Play p in Book.AllPlays) if (p.Name == name) return p;
            return null;
        }

        void Add(List<PlayOption> list, string label, string tag, string name, params string[] formations)
        {
            Play p = Find(name, formations);
            if (p != null) list.Add(new PlayOption { Label = label, Tag = tag, Play = p });
        }

        /// <summary>Four to six offensive calls for the situation: A/B/X/Y/LB/RB in order.</summary>
        public List<PlayOption> OffenseOptions()
        {
            var l = new List<PlayOption>();
            const string SB = DefaultPlaybooks.Singleback, IF = DefaultPlaybooks.IForm, SG = DefaultPlaybooks.ShotgunF, GL = DefaultPlaybooks.GoalLine, TR = DefaultPlaybooks.Trips;
            if (State.Phase == GamePhase.PointAfter)
            {
                if (!State.RequiresTwoPoint) Add(l, "Extra Point", "KICK", "Extra Point", DefaultPlaybooks.FieldGoalF);
                Add(l, "Two-Point Run", "RUN", "Power Right", GL);
                Add(l, "Two-Point Pass", "PASS", "Slants", SG);
                Add(l, "Two-Point Sneak", "RUN", "QB Sneak", GL);
                return l;
            }
            if (State.Phase == GamePhase.Kickoff)
            {
                Add(l, "Deep Kick", "KICK", "Kickoff Deep Middle", DefaultPlaybooks.KickoffF);
                Add(l, "Deep Left", "KICK", "Kickoff Deep Left", DefaultPlaybooks.KickoffF);
                Add(l, "Deep Right", "KICK", "Kickoff Deep Right", DefaultPlaybooks.KickoffF);
                Add(l, "Squib Kick", "KICK", "Kickoff Squib", DefaultPlaybooks.KickoffF);
                Add(l, "Onside Kick", "KICK", "Onside Kick Short", DefaultPlaybooks.OnsideF);
                return l;
            }
            int toGoal = 100 - State.BallSpot;
            bool fgRange = toGoal + 17 <= 58;
            if (State.Down == 4 && toGoal > 3)
            {
                if (toGoal >= 35 || !fgRange) Add(l, "Punt", "PUNT", "Punt Standard", DefaultPlaybooks.PuntF);
                if (fgRange) Add(l, "Field Goal", "FG", "Field Goal", DefaultPlaybooks.FieldGoalF);
                if (toGoal < 35 && !fgRange) Add(l, "Punt", "PUNT", "Punt Standard", DefaultPlaybooks.PuntF);
                if (State.Distance <= 3) { Add(l, "Inside Zone", "RUN", "Inside Zone Right", SB); Add(l, "QB Sneak", "RUN", "QB Sneak", SB); }
                else { Add(l, "Slants", "PASS", "Slants", SG); Add(l, "Y-Cross", "PASS", "Y-Cross", SB); }
                Add(l, "Four Verticals", "PASS", "Four Verticals", SG);
                return l;
            }
            if (toGoal <= 3)
            {
                Add(l, "QB Sneak", "RUN", "QB Sneak", GL);
                Add(l, "Power", "RUN", "Power Right", GL);
                Add(l, "Iso", "RUN", "Iso Left", GL);
                Add(l, "Slants", "PASS", "Slants", SG);
                Add(l, "PA Boot", "PASS", "PA Boot", IF);
                Add(l, "Mesh", "PASS", "Mesh", SG);
                return l;
            }
            Add(l, "Inside Zone", "RUN", "Inside Zone Right", SB);
            Add(l, "Toss Sweep", "RUN", "Toss Sweep Right", IF);
            Add(l, "Slants", "PASS", "Slants", SG);
            Add(l, "Four Verticals", "PASS", "Four Verticals", SG);
            Add(l, "PA Boot", "PASS", "PA Boot", SB);
            Add(l, "Mesh", "PASS", "Mesh", SG);
            bool late = (State.Quarter == 2 || State.Quarter == 4) && State.ClockSeconds <= 75f;
            int diff = State.PossessionScoreDiff;
            if (late && diff <= 0) { l.RemoveAt(l.Count - 1); Add(l, "Spike Ball", "CLOCK", "Spike", SG); }
            else if (State.Quarter == 4 && diff > 0 && State.ClockSeconds <= 150f) { l.RemoveAt(l.Count - 1); Add(l, "Kneel Down", "CLOCK", "Kneel", SB); }
            return l;
        }

        /// <summary>Defensive calls for the situation. Empty when the CPU offense is kicking (the matching special-teams call is automatic).</summary>
        public List<PlayOption> DefenseOptions()
        {
            var l = new List<PlayOption>();
            const string B43 = DefaultPlaybooks.Base43, NK = DefaultPlaybooks.Nickel, GLD = DefaultPlaybooks.GoalLineD;
            int toGoal = 100 - State.BallSpot;
            if (toGoal <= 3)
            {
                Add(l, "Goal Line Plug", "ZONE", "Goal Line Plug Cover 2", GLD);
                Add(l, "Gap Control", "MAN", "Goal Line Gap Control", GLD);
                Add(l, "Goal Line Blitz", "BLITZ", "Goal Line Cover 0", GLD);
                Add(l, "Cover 2", "ZONE", "Cover 2 Zone", B43);
                return l;
            }
            Add(l, "Cover 2", "ZONE", "Cover 2 Zone", B43);
            Add(l, "Cover 3", "ZONE", "Cover 3 Sky", B43);
            Add(l, "Cover 1", "MAN", "Cover 1 Robber", B43);
            Add(l, "Mike Blitz", "BLITZ", "Mike Blitz Cover 1", B43);
            Add(l, "All-Out Blitz", "BLITZ", "Cover 0 Blitz", B43);
            Add(l, "Quarters", "ZONE", "Cover 4 Quarters", NK);
            return l;
        }

        /// <summary>The CPU's pick for the offense and defense in the current situation.</summary>
        public void CpuCalls(out Play off, out Play def) { Cpu.ChoosePlays(out off, out def); }

        public Play DefenseAnswer(Play off) { return Cpu.DefenseResponseTo(off); }

        // ------------------------------------------------------------------------------------------------ the play
        static Dictionary<int, Ratings> Boost(Player[] roster, int delta)
        {
            var saved = new Dictionary<int, Ratings>();
            if (delta == 0) return saved;
            foreach (Player p in roster)
            {
                if (p == null || saved.ContainsKey(p.Id)) continue;
                saved[p.Id] = p.Ratings;
                Ratings r = p.Ratings;
                for (int i = 0; i < Ratings.KindCount; i++)
                {
                    var k = (RatingKind)i;
                    if (k == RatingKind.Injury || k == RatingKind.Stamina) continue;
                    r.Set(k, Math.Max(20, Math.Min(99, r.Get(k) + delta)));
                }
                p.Ratings = r;
            }
            return saved;
        }

        static void Restore(Player[] roster, Dictionary<int, Ratings> saved)
        {
            foreach (Player p in roster) if (p != null && saved.ContainsKey(p.Id)) p.Ratings = saved[p.Id];
        }

        /// <summary>Builds a live play simulation for the current state. AutoSnap is off: the director (or the CPU delay) snaps.</summary>
        public PlaySimulation CreateSim(Play off, Play def, bool autoSnap = false, float autoSnapDelay = 0.6f)
        {
            CoreTeam offTeam = State.Possession;
            LeagueTeam offL = TeamOf(offTeam), defL = TeamOf(offTeam.Opponent());
            Formation of = Book.FindFormation(off.FormationName), df = Book.FindFormation(def.FormationName);
            Player[] ro = SimRosters.ForFormation(offL, of, Fatigue);
            Player[] rd = SimRosters.ForFormation(defL, df, Fatigue);
            int delta = Config.CpuRatingDelta;
            Dictionary<int, Ratings> sro = IsUser(offTeam) ? null : Boost(ro, delta);
            Dictionary<int, Ratings> srd = IsUser(offTeam.Opponent()) ? null : Boost(rd, delta);
            var sim = new PlaySimulation();
            var simOptions = new PlaySimOptions { Playbook = Book, Hash = _nextHash, AutoSnap = autoSnap, AutoSnapDelay = autoSnapDelay, Penalties = true, BallSlickness = Config.BallSlickness, Fatigue = Fatigue, QbWindupSeconds = 0.42f };
            if (State.Conditions != null) simOptions.SetWind(State.Conditions.ForQuarter(State.Quarter), PlayFrame.For(State).TowardsPlusZ);
            if (SimOptionsHook != null) SimOptionsHook(simOptions);
            try { sim.Setup(State, off, def, ro, rd, Rules, Rng.Fork(), simOptions); }
            finally
            {
                // QA-738: the CPU rating boost is always taken back, even when Setup throws
                if (sro != null) Restore(ro, sro);
                if (srd != null) Restore(rd, srd);
            }
            return sim;
        }

        /// <summary>Applies a finished (Dead) play to the game state and works out what happened.</summary>
        public PlayOutcome Finish(PlaySimulation sim) { return Finish(sim, PenaltyDecision.Auto); }

        /// <summary>Applies the finished play. When a flag was thrown, <paramref name="decision"/> is the offended team's choice (Auto = the CPU rule, expected points).</summary>
        public PlayOutcome Finish(PlaySimulation sim, PenaltyDecision decision)
        {
            PlayResult r = sim.Result;
            if (r == null) throw new InvalidOperationException("The play has not ended.");
            var o = new PlayOutcome { Result = r, Kind = sim.Kind, Offense = State.Possession, QuarterBefore = State.Quarter, EndReason = sim.EndInfo };
            int h0 = State.HomeScore, a0 = State.AwayScore, down0 = State.Down, dist0 = State.Distance, spot0 = State.BallSpot;
            CoreTeam poss0 = State.Possession; GamePhase phase0 = State.Phase;
            float ballX = sim.Ball.Pos.X;

            if (r.Penalty != null)
            {
                if (decision != PenaltyDecision.Auto) { r.PenaltyDecision = decision; o.PenaltyAccepted = decision == PenaltyDecision.Accept; }
                else if (r.Type == PlayType.Penalty) o.PenaltyAccepted = r.PenaltyDecision != PenaltyDecision.Decline;
                else
                {
                    o.PenaltyAccepted = PenaltyEnforcer.ShouldAccept(State, r);
                    r.PenaltyDecision = o.PenaltyAccepted ? PenaltyDecision.Accept : PenaltyDecision.Decline;
                }
                o.PenaltyCall = r.Penalty;
                o.PenaltyName = r.Penalty.Type.ToString();
            }

            if (r.Penalty != null && o.PenaltyAccepted && r.OffsettingPenalty == null)
            {
                // the yardage actually enforced (college vs pro yards, spot fouls, half the distance), worked out on a copy of the pre-play state
                try
                {
                    EnforcementResult er = PenaltyEnforcer.Enforce(State.Clone(), r.Penalty, r);
                    o.PenaltyYards = Math.Abs(er.Yards); o.PenaltyFirstDown = er.FirstDown || er.AutomaticFirstDown;
                }
                catch (Exception) { o.PenaltyYards = Math.Abs(r.Penalty.Def.YardsFor(Rules, r.Penalty.FoulSpotYards)); }
            }
            // the energy the players burned carries into the next snap (engine SIM-024); quarter breaks and halftime rest everyone a little
            try
            {
                if (_fatigueQuarter != State.Quarter) { if (_fatigueQuarter > 0) Fatigue.Rest(_fatigueQuarter == 2 ? 600f : 90f); _fatigueQuarter = State.Quarter; }
                var onField = new HashSet<int>();
                for (int i = 0; i < 22; i++) if (sim.Agents[i].Player != null) onField.Add(sim.Agents[i].Player.Id);
                var sitting = new List<Player>();
                foreach (Player p in Home.Roster) if (!onField.Contains(p.Id)) sitting.Add(p);
                foreach (Player p in Away.Roster) if (!onField.Contains(p.Id)) sitting.Add(p);
                Fatigue.AfterPlay(sim, 25f, sitting);
            }
            catch (Exception) { }
            o.Applied = State.Apply(r);
            State.PendingRunoff *= Config.RunoffScale;
            Snaps++;

            o.QuarterAfter = State.Quarter;
            o.HomePoints = State.HomeScore - h0; o.AwayPoints = State.AwayScore - a0;
            o.PossessionChanged = State.Possession != poss0 && State.Phase != GamePhase.Final;
            o.GameOver = State.IsFinal;
            if (o.HomePoints > 0 && o.AwayPoints <= 0) o.ScoringTeam = CoreTeam.Home;
            else if (o.AwayPoints > 0 && o.HomePoints <= 0) o.ScoringTeam = CoreTeam.Away;
            int q = Math.Max(1, Math.Min(o.QuarterBefore, 7));
            HomeByQuarter[q] += o.HomePoints; AwayByQuarter[q] += o.AwayPoints;
            ReconcileLineScore(q);

            // hash for the next snap (field X mirrors when the ball changes hands)
            if (State.Phase == GamePhase.Scrimmage && phase0 == GamePhase.Scrimmage && !(r.Type == PlayType.Punt))
            {
                float x = Math.Max(0f, Math.Min(FieldGeometry.Width, ballX));
                if (State.Possession != poss0) x = FieldGeometry.Width - x;
                _nextHash = FieldGeometry.NearestHash(x, Rules);
            }
            else _nextHash = Hash.Middle;

            if (sim.Kind == PlayKind.Scrimmage || sim.Kind == PlayKind.Kneel || sim.Kind == PlayKind.Spike) Cpu.Remember(poss0, sim.OffPlay);
            Classify(o, r, phase0, poss0, down0, dist0, spot0);
            Stats(o, r, poss0, down0, dist0);
            Log.Add("Q" + o.QuarterBefore + " " + Abbr(poss0) + " " + (o.Text.Length > 0 ? o.Text : r.Type.ToString()) + " " + State.HomeScore + "-" + State.AwayScore);
            return o;
        }

        void Classify(PlayOutcome o, PlayResult r, GamePhase phase0, CoreTeam poss0, int down0, int dist0, int spot0)
        {
            Headline h = Headline.None; string t = "";
            if (r.Penalty != null && o.PenaltyAccepted) { h = Headline.Penalty; t = "FLAG - " + r.Penalty.Def.Name.ToUpperInvariant(); }
            else if (r.Type == PlayType.PAT) { h = r.Good ? Headline.ExtraPoint : Headline.None; t = r.Good ? "EXTRA POINT GOOD" : "EXTRA POINT NO GOOD"; }
            else if (r.Type == PlayType.TwoPoint) { h = r.Good ? Headline.TwoPointGood : Headline.TwoPointFailed; t = r.Good ? "TWO-POINT CONVERSION" : "TWO-POINT TRY FAILED"; }
            else if (r.Safety) { h = Headline.Safety; t = "SAFETY"; }
            else if (r.Touchdown) { h = Headline.Touchdown; t = "TOUCHDOWN"; }
            else if (r.Type == PlayType.FieldGoal) { h = r.Good ? Headline.FieldGoal : Headline.FieldGoalMissed; t = r.Good ? "FIELD GOAL IS GOOD" : "FIELD GOAL NO GOOD"; }
            else if (r.Type == PlayType.Interception) { h = Headline.Interception; t = "INTERCEPTION"; }
            else if (r.Type == PlayType.Fumble && r.Turnover) { h = Headline.FumbleLost; t = "FUMBLE - TURNOVER"; }
            else if (r.Type == PlayType.Fumble) { h = Headline.Fumble; t = "FUMBLE - RECOVERED"; }
            else if (r.Type == PlayType.Sack) { h = Headline.Sack; t = "SACK"; }
            else if (r.Type == PlayType.Incomplete) { h = Headline.Incomplete; t = "INCOMPLETE"; }
            else if (r.Type == PlayType.Punt) { h = Headline.Punt; t = "PUNT"; }
            else if (r.Type == PlayType.Kickoff) { h = Headline.Kickoff; t = "KICKOFF"; }
            else if (r.Type == PlayType.Kneel) { h = Headline.Kneel; t = "KNEEL DOWN"; }
            else if (r.Type == PlayType.Spike) { h = Headline.Spike; t = "SPIKE"; }
            else if (phase0 == GamePhase.Scrimmage && down0 == 4 && State.Possession != poss0 && r.Type != PlayType.Punt && r.Type != PlayType.FieldGoal && !r.Turnover)
            { h = Headline.TurnoverOnDowns; t = "TURNOVER ON DOWNS"; }
            else if (phase0 == GamePhase.Scrimmage && State.Phase == GamePhase.Scrimmage && State.Possession == poss0 && State.Down == 1 && (r.Type == PlayType.Run || r.Type == PlayType.Pass))
            { h = Headline.FirstDown; t = "FIRST DOWN"; }
            if (r.Touchback && h == Headline.Kickoff) { t = "TOUCHBACK"; }
            o.Headline = h; o.Text = t;
        }

        static string PenaltyText(string n)
        {
            var sb = new System.Text.StringBuilder("FLAG - ");
            for (int i = 0; i < n.Length; i++) { if (i > 0 && char.IsUpper(n[i])) sb.Append(' '); sb.Append(char.ToUpperInvariant(n[i])); }
            return sb.ToString();
        }

        /// <summary>
        /// Makes the per-quarter line score add up to the real score after every play: any points the state gained outside the
        /// per-play bookkeeping (QA-107/170) land in the quarter that was current, so the final line score always equals the final score.
        /// </summary>
        void ReconcileLineScore(int q)
        {
            int h = 0, a = 0;
            for (int i = 0; i < HomeByQuarter.Length; i++) { h += HomeByQuarter[i]; a += AwayByQuarter[i]; }
            if (h != State.HomeScore) HomeByQuarter[q] += State.HomeScore - h;
            if (a != State.AwayScore) AwayByQuarter[q] += State.AwayScore - a;
        }

        /// <summary>True when the per-quarter line score of both teams adds up to the live score.</summary>
        public bool LineScoreMatchesScore()
        {
            int h = 0, a = 0;
            for (int i = 0; i < HomeByQuarter.Length; i++) { h += HomeByQuarter[i]; a += AwayByQuarter[i]; }
            return h == State.HomeScore && a == State.AwayScore;
        }

        /// <summary>
        /// Books a fumble: a completed catch (passer and receiver known) counts as a reception, anything else as a carry by the rusher / receiver / quarterback.
        /// <paramref name="carrierId"/> is the fumbling player (0 when the play result does not say).
        /// </summary>
        public static FumbleBooking FumbleBookingOf(PlayResult r, out int carrierId)
        {
            carrierId = 0;
            if (r == null) return FumbleBooking.None;
            if (r.RusherId > 0) { carrierId = r.RusherId; return FumbleBooking.Rush; }
            if (r.ReceiverId > 0 && r.PasserId > 0) { carrierId = r.ReceiverId; return FumbleBooking.Pass; }
            if (r.ReceiverId > 0) { carrierId = r.ReceiverId; return FumbleBooking.Rush; }
            if (r.PasserId > 0) { carrierId = r.PasserId; return FumbleBooking.Rush; }
            return FumbleBooking.None;
        }

        void Stats(PlayOutcome o, PlayResult r, CoreTeam poss0, int down0, int dist0)
        {
            TeamStats s = StatsOf(poss0);
            if (o.PenaltyAccepted)
            {
                // the enforced yardage (set in Finish from the rules in force); the nominal pro yardage only as a last resort
                TeamStats pen = StatsOf(r.Penalty.CommittedBy);
                pen.Penalties++;
                pen.PenaltyYards += o.PenaltyYards >= 0 ? o.PenaltyYards : Math.Abs(r.Penalty.Def.YardsFor(Rules, r.Penalty.FoulSpotYards));
                if (o.PenaltyFirstDown && r.Penalty.CommittedBy != poss0) { s.FirstDowns++; o.FirstDownGained = true; }
                return;
            }
            bool scrimmage = false, keptBall = !r.Turnover && !o.PossessionChanged;
            switch (r.Type)
            {
                case PlayType.Run:
                case PlayType.Kneel:
                    s.RushYards += r.Yards; s.Plays++; s.RushAtt++; scrimmage = true; break;
                case PlayType.Pass:
                    s.PassYards += r.Yards; s.Plays++; s.PassAtt++; s.PassComp++; scrimmage = true; break;
                case PlayType.Sack:
                    s.PassYards += r.Yards; s.Plays++; s.SacksTaken++; StatsOf(poss0.Opponent()).Sacks++; scrimmage = true; break;
                case PlayType.Incomplete:
                    s.Plays++; s.PassAtt++; scrimmage = true; break;
                case PlayType.Interception:
                    s.Plays++; s.PassAtt++; scrimmage = true; break;
                case PlayType.Spike:
                    s.Plays++; scrimmage = true; break;
                case PlayType.Fumble:
                {
                    // a fumble keeps the yardage gained up to the point of the fumble (QA-835)
                    int carrier;
                    FumbleBooking fb = FumbleBookingOf(r, out carrier);
                    if (fb == FumbleBooking.Pass) { s.PassYards += r.Yards; s.PassAtt++; s.PassComp++; }
                    else if (fb == FumbleBooking.Rush) { s.RushYards += r.Yards; s.RushAtt++; }
                    s.Plays++; scrimmage = true; break;
                }
            }
            if (r.Turnover) s.Turnovers++;

            // first downs: the line to gain was reached (also on a touchdown play), never on a turnover
            bool firstDown = false;
            if (scrimmage && keptBall && dist0 > 0 && (r.Type == PlayType.Run || r.Type == PlayType.Pass || r.Type == PlayType.Fumble) && r.Yards >= dist0) firstDown = true;
            else if (o.Headline == Headline.FirstDown && keptBall) firstDown = true;
            if (firstDown) { s.FirstDowns++; o.FirstDownGained = true; }
            if (scrimmage && r.Type != PlayType.Kneel && r.Type != PlayType.Spike)
            {
                bool conv = firstDown || (r.Touchdown && keptBall);
                if (down0 == 3) { s.ThirdDownAtt++; if (conv) s.ThirdDownConv++; }
                else if (down0 == 4) { s.FourthDownAtt++; if (conv) s.FourthDownConv++; }
            }
        }

        /// <summary>The one spot notation used everywhere (QA-282): "BUF 29" on the offense's own side, "MIA 33" past midfield, "50" at midfield. <paramref name="spot"/> is yards from the offense's own goal line.</summary>
        public static string FieldPositionText(int spot, string ownAbbr, string oppAbbr)
        {
            if (spot == 50) return "50";
            return spot < 50 ? ownAbbr + " " + spot : oppAbbr + " " + (100 - spot);
        }

        public string Summary()
        {
            return "Q" + State.Quarter + " " + State.ClockDisplay + "  " + Abbr(CoreTeam.Home) + " " + State.HomeScore + " - " + Abbr(CoreTeam.Away) + " " + State.AwayScore
                   + "  " + Abbr(State.Possession) + " ball " + State.Down + "&" + State.Distance + " at " + FieldPositionText(State.BallSpot, Abbr(State.Possession), Abbr(State.Possession.Opponent()));
        }

        /// <summary>Coin toss: returns true when the user's call (heads when userCallsHeads) won.</summary>
        public static bool TossWon(Rng rng, bool userCallsHeads, out bool resultHeads)
        {
            resultHeads = rng.Chance(0.5f);
            return resultHeads == userCallsHeads;
        }
    }
}


