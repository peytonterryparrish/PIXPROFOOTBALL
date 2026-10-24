using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;
using CoreTeam = PixFootball.Core.Team;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.Sim
{
    /// <summary>One executed play of a <see cref="PhysicalGameSim"/> game.</summary>
    public sealed class PlayRecord
    {
        public int Index;
        public int Quarter;
        public float ClockBefore;
        public int DownBefore, DistanceBefore, SpotBefore;
        public CoreTeam Offense;
        public string OffensePlayName = "", DefensePlayName = "";
        public PlayResult Result;
        public PlayEventLog Log;
        public float SimSeconds;
        public int HomeScore, AwayScore;
        public bool Note;
        /// <summary>True when a flag on this play was accepted (the play itself was replaced by the foul).</summary>
        public bool PenaltyAccepted;
        public override string ToString()
        {
            return "Q" + Quarter + " " + DownBefore + "&" + DistanceBefore + " @" + SpotBefore + " " + OffensePlayName + " -> "
                   + (Result != null ? Result.Type + " " + Result.Yards : "-") + "  " + HomeScore + "-" + AwayScore;
        }
    }

    /// <summary>
    /// Headless CPU-vs-CPU game on the full physical play simulation: picks plays like a basic coordinator, runs each play through
    /// <see cref="PlaySimulation"/> and feeds the results to <see cref="GameState"/>. Usable for "watch a game" modes and for simming a
    /// full play-by-play with physical outcomes.
    /// </summary>
    public sealed partial class PhysicalGameSim
    {
        public readonly GameState State;
        public readonly RuleSet Rules;
        public readonly Rng Rng;
        public readonly Playbook Book;
        public readonly LeagueTeam Home, Away;
        public readonly List<PlayRecord> Plays = new List<PlayRecord>();
        /// <summary>QA: changes the options of every play (the match flow uses a 0.42 s windup, penalties and the choreographer).</summary>
        public System.Action<PlaySimOptions> OptionsHook;
        public bool KeepLogs = false;
        /// <summary>Per-player fatigue carried from snap to snap (set to null for the old always-fresh behavior).</summary>
        public FatigueTracker Fatigue = new FatigueTracker();
        /// <summary>CPU coaches use their timeouts in the two-minute drill (set false for the old behavior).</summary>
        public bool CpuTimeouts = true;
        /// <summary>v0.8.0: apply abilities, morale and leadership to the players of every play (see <see cref="PlaySimOptions.PlayerModifiers"/>); false = the pre-0.8.0 ratings.</summary>
        public bool PlayerModifiers = true;
        /// <summary>v0.8.0: apply the coaching staff's own play-calling rates (blitz, man coverage, deep shots, no-huddle tempo) on top of the coach profile; false = the pre-0.8.0 calling.</summary>
        public bool StaffTendencies = true;
        private int _lastQuarter;

        private readonly List<Play> _runs = new List<Play>(), _passes = new List<Play>(), _screens = new List<Play>(),
            _zoneDefs = new List<Play>(), _manDefs = new List<Play>(), _blitzDefs = new List<Play>(), _goalLineDefs = new List<Play>(),
            _preventDefs = new List<Play>(), _kickoffs = new List<Play>(), _onsides = new List<Play>(), _kickReturns = new List<Play>(),
            _puntReturns = new List<Play>(), _fgBlocks = new List<Play>(), _goalLineOff = new List<Play>();
        private Play _punt, _fieldGoal, _extraPoint, _kneel, _sneak, _spike;

        public PhysicalGameSim(LeagueTeam home, LeagueTeam away, RuleSet rules, Rng rng, Playbook book = null, CoreTeam openingKicker = CoreTeam.Away)
        {
            Home = home; Away = away; Rules = rules ?? RuleSet.Pro(); Rng = rng;
            Book = book ?? DefaultPlaybooks.Create();
            State = new GameState(Rules, openingKicker);
            State.OvertimeCoin = Rng.Fork();   // QA-479: the overtime coin toss
            Index();
        }

        /// <summary>Wraps an externally driven game (the live match director owns the state and runs the plays itself; this object only supplies CPU play calling).</summary>
        public PhysicalGameSim(LeagueTeam home, LeagueTeam away, GameState state, Rng rng, Playbook book = null)
        {
            Home = home; Away = away; State = state; Rules = state.Rules; Rng = rng;
            Book = book ?? DefaultPlaybooks.Create();
            Index();
        }

        private void Index()
        {
            foreach (Play p in Book.AllPlays)
            {
                switch (p.Category)
                {
                    case PlayCategory.InsideRun: case PlayCategory.OutsideRun: case PlayCategory.Draw: case PlayCategory.QbRun:
                        if (p.Name == "QB Sneak") _sneak = p; else _runs.Add(p);
                        if (p.FormationName == DefaultPlaybooks.GoalLine) _goalLineOff.Add(p);
                        break;
                    case PlayCategory.ShortPass: case PlayCategory.MediumPass: case PlayCategory.DeepPass: case PlayCategory.PlayAction:
                        if (p.Name != "Hail Mary") _passes.Add(p);
                        break;
                    case PlayCategory.Screen: _screens.Add(p); break;
                    case PlayCategory.Kneel: if (_kneel == null) _kneel = p; break;
                    case PlayCategory.Spike: if (_spike == null) _spike = p; break;
                    case PlayCategory.ZoneCoverage: _zoneDefs.Add(p); break;
                    case PlayCategory.ManCoverage: _manDefs.Add(p); break;
                    case PlayCategory.Blitz: _blitzDefs.Add(p); break;
                    case PlayCategory.GoalLine: _goalLineDefs.Add(p); break;
                    case PlayCategory.Prevent: _preventDefs.Add(p); break;
                    case PlayCategory.Kickoff: _kickoffs.Add(p); break;
                    case PlayCategory.Onside: _onsides.Add(p); break;
                    case PlayCategory.KickReturn: _kickReturns.Add(p); break;
                    case PlayCategory.PuntReturn: _puntReturns.Add(p); break;
                    case PlayCategory.FieldGoalBlock: _fgBlocks.Add(p); break;
                    case PlayCategory.Punt: if (p.Name == "Punt Standard") _punt = p; break;
                    case PlayCategory.FieldGoal: _fieldGoal = p; break;
                    case PlayCategory.PointAfter: _extraPoint = p; break;
                }
            }
            if (_punt == null) foreach (Play p in Book.PlaysByCategory(PlayCategory.Punt)) { _punt = p; break; }
        }

        public LeagueTeam TeamOf(CoreTeam t) { return t == CoreTeam.Home ? Home : Away; }

        /// <summary>v0.8.0: multiplier on a player's chance of being hurt in a game or play (Iron Man: x0.85 / x0.75 / x0.65). League.Season.RollInjuries applies the same scale to every game line; an in-match injury model should use this too.</summary>
        public static float InjuryScale(Player p)
        {
            if (p == null || p.Abilities == null || p.Abilities.Count == 0) return 1f;
            return Math.Max(0.3f, 1f + AbilityCatalog.Modifier(p, SimHook.InjuryResist) / 100f);
        }

        // ===================================================================================================

        /// <summary>Runs every play until the game ends (or maxPlays safety stop). Returns the number of plays executed.</summary>
        public int Run(int maxPlays = 450)
        {
            int n = 0;
            while (!State.IsFinal && n < maxPlays)
            {
                PlayRecord r = PlayNext();
                if (r == null) break;
                if (!r.Note) n++;
            }
            return n;
        }

        /// <summary>Executes the next snap. Returns a Note record when only the clock moved (period ended), or null when the game is over.</summary>
        public PlayRecord PlayNext()
        {
            if (State.IsFinal) return null;
            if (CpuTimeouts && State.Phase == GamePhase.Scrimmage)
            {
                foreach (CoreTeam tt in new[] { CoreTeam.Home, CoreTeam.Away })
                    if (PlayCaller.WantsTimeout(State, tt, Rng)) { State.CallTimeout(tt); break; }
            }
            if (State.Phase == GamePhase.Scrimmage && State.RunClockToSnap())
                return new PlayRecord { Note = true, Quarter = State.Quarter, HomeScore = State.HomeScore, AwayScore = State.AwayScore };
            if (State.IsFinal) return null;

            var rec = new PlayRecord
            {
                Index = Plays.Count, Quarter = State.Quarter, ClockBefore = State.ClockSeconds, DownBefore = State.Down,
                DistanceBefore = State.Distance, SpotBefore = State.BallSpot, Offense = State.Possession
            };
            Play off, def;
            ChoosePlays(out off, out def);
            if (off == null || def == null) throw new InvalidOperationException("The playbook has no play for " + State.Phase + " (offense " + (off != null) + ", defense " + (def != null) + "): a trimmed custom playbook must keep at least one kickoff, return, punt, field goal, block and extra-point play.");
            Remember(State.Possession, off);
            rec.OffensePlayName = off.Name; rec.DefensePlayName = def.Name;

            CoreTeam offTeam = State.Possession;
            LeagueTeam offLeague = TeamOf(offTeam), defLeague = TeamOf(offTeam.Opponent());
            Formation of = Book.FindFormation(off.FormationName), df = Book.FindFormation(def.FormationName);
            Player[] ro = SimRosters.ForFormation(offLeague, of, Fatigue);
            Player[] rd = SimRosters.ForFormation(defLeague, df, Fatigue);

            var sim = new PlaySimulation();
            // game conditions: the wind of this quarter in the offense's frame, and the fatigue the players carry into the snap
            bool attacksPositiveZ = (State.Possession == CoreTeam.Home) == (State.Quarter % 2 == 1);
            var simOptions = new PlaySimOptions { Playbook = Book, Fatigue = Fatigue, PlayerModifiers = PlayerModifiers };
            if (State.Conditions != null) simOptions.SetWind(State.Conditions.ForQuarter(State.Quarter), attacksPositiveZ);
            if (rec.Quarter != _lastQuarter) { if (_lastQuarter > 0 && Fatigue != null) Fatigue.Rest(_lastQuarter == 2 ? 600f : 90f); _lastQuarter = rec.Quarter; }
            sim.Setup(State, off, def, ro, rd, Rules, Rng.Fork(), simOptions);
            PlayResult result = sim.RunToCompletion();
            rec.Result = result;
            rec.SimSeconds = sim.Time;
            if (Fatigue != null)
            {
                var onField = new HashSet<int>();
                for (int i = 0; i < 22; i++) if (sim.Agents[i].Player != null) onField.Add(sim.Agents[i].Player.Id);
                var sitting = new List<Player>();
                foreach (Player p in offLeague.Roster) if (!onField.Contains(p.Id)) sitting.Add(p);
                foreach (Player p in defLeague.Roster) if (!onField.Contains(p.Id)) sitting.Add(p);
                Fatigue.AfterPlay(sim, 25f, sitting);
            }
            if (result.Penalty != null)
            {
                if (result.Type == PlayType.Penalty) rec.PenaltyAccepted = result.PenaltyDecision != PenaltyDecision.Decline;
                else
                {
                    rec.PenaltyAccepted = result.OffsettingPenalty == null && PenaltyEnforcer.ShouldAccept(State, result);
                    result.PenaltyDecision = rec.PenaltyAccepted ? PenaltyDecision.Accept : PenaltyDecision.Decline;
                }
            }
            if (KeepLogs) rec.Log = sim.Log;
            State.Apply(result);
            ApplyTempo(State.Possession);
            rec.HomeScore = State.HomeScore; rec.AwayScore = State.AwayScore;
            Plays.Add(rec);
            return rec;
        }

        // ===================================================================================================
        // Play calling
        // ===================================================================================================

        private Play Pick(List<Play> list)
        {
            return list.Count == 0 ? null : list[Rng.Range(0, list.Count)];
        }

        /// <summary>Deep kicks almost always; the squib is a rare change of pace.</summary>
        private Play PickKickoff()
        {
            var deep = new List<Play>();
            foreach (Play p in _kickoffs) if (p.Name.IndexOf("Squib") < 0) deep.Add(p);
            if (deep.Count == 0 || Rng.Chance(0.06f)) return Pick(_kickoffs);
            return Pick(deep);
        }

        private Play PickByFormation(List<Play> list, string formationOrNull)
        {
            if (formationOrNull == null) return Pick(list);
            var sub = new List<Play>();
            foreach (Play p in list) if (p.FormationName == formationOrNull) sub.Add(p);
            return sub.Count > 0 ? Pick(sub) : Pick(list);
        }

        public void ChoosePlays(out Play off, out Play def)
        {
            switch (State.Phase)
            {
                case GamePhase.Kickoff:
                {
                    // the kicking team (possession) trails when its diff is negative
                    bool trailing = State.PossessionScoreDiff < 0 && State.PossessionScoreDiff >= -16;   // down two scores still onsides
                    bool onside = State.Quarter == 4 && State.ClockSeconds < 210f && trailing && Rng.Chance(0.75f);
                    off = onside ? Pick(_onsides) : PickKickoff();
                    def = Pick(_kickReturns);
                    return;
                }
                case GamePhase.PointAfter:
                {
                    bool two = State.RequiresTwoPoint || WantsTwoPoint();
                    if (two)
                    {
                        off = Rng.Chance(0.5f) && _goalLineOff.Count > 0 ? Pick(_goalLineOff) : PickByFormation(_passes, DefaultPlaybooks.ShotgunF);
                        def = _goalLineDefs.Count > 0 ? Pick(_goalLineDefs) : Pick(_zoneDefs);
                    }
                    else { off = _extraPoint; def = Pick(_fgBlocks); }
                    return;
                }
            }

            // scrimmage downs: situational calling from the whole playbook (down, distance, field zone, score, clock, coach tendencies)
            CoreTeam team = State.Possession;
            CoachProfile oc = ProfileOf(team), dc = ProfileOf(team.Opponent());
            if (PlayCaller.ShouldKneel(State) && _kneel != null) { off = _kneel; def = DefenseResponseTo(off); return; }
            if (_spike != null && PlayCaller.ShouldSpike(State)) { off = _spike; def = DefenseResponseTo(off); return; }
            if (PlayCaller.ShouldHailMary(State)) { Play hm = null; foreach (Play hp in Book.AllPlays) if (hp.Name == "Hail Mary" && hp.Side == PlaySide.Offense) { hm = hp; break; } if (hm != null) { off = hm; def = DefenseResponseTo(off); return; } }
            if (State.Down == 4)
            {
                switch (Caller.FourthDown(State, oc, KickerRange(team)))
                {
                    case PlayCaller.FourthCall.FieldGoal: off = _fieldGoal; def = Pick(_fgBlocks); return;
                    case PlayCaller.FourthCall.Punt: off = _punt; def = Pick(_puntReturns); return;
                }
            }
            off = Caller.CallOffense(State, oc, Recent(team));
            def = Caller.CallDefense(State, dc, Book.FindFormation(off.FormationName));
        }

        // ---- coaches ----
        private PlayCaller _caller;
        private readonly List<Play>[] _recent = { new List<Play>(), new List<Play>() };
        public PlayCaller Caller { get { return _caller ?? (_caller = new PlayCaller(Book, Rng.Fork())); } }

        /// <summary>The recent offensive calls of a team (newest last), used to avoid repeating.</summary>
        public List<Play> Recent(CoreTeam t) { return _recent[(int)t]; }

        public void Remember(CoreTeam t, Play p)
        {
            var l = _recent[(int)t];
            l.Add(p);
            while (l.Count > 5) l.RemoveAt(0);
        }

        /// <summary>The CPU coach's tendencies for a team (head coach, offensive coordinator and scheme).</summary>
        public static CoachProfile ProfileOf(LeagueTeam t)
        {
            var c = CoachProfile.Neutral;
            if (t == null) return c;
            Coach hc = t.HeadCoach, oc = t.OffensiveCoordinator, dcoord = t.DefensiveCoordinator;
            c.PassTendency = oc != null ? oc.PassTendency : (hc != null ? hc.PassTendency : 50);
            c.Aggression = hc != null ? hc.Aggression : 50;
            if (dcoord != null) c.Aggression = (c.Aggression + dcoord.Aggression) / 2 + (dcoord.DefScheme == DefenseScheme.Aggressive ? 12 : 0);
            c.Aggression = Math.Max(0, Math.Min(100, c.Aggression));
            c.Style = oc != null ? StyleOf(oc.OffScheme) : PlayStyle.Balanced;
            if (dcoord != null)
            {
                // QA-621: the coordinator's scheme steers the base front and the nickel share
                c.FrontBias = dcoord.DefScheme == DefenseScheme.Base34 ? 1f : (dcoord.DefScheme == DefenseScheme.Base43 ? -1f : 0f);
                c.NickelBias = dcoord.DefScheme == DefenseScheme.Nickel ? 1f : 0f;
            }
            // v0.8.0: the staff's own play-calling rates (StaffManager.Tendencies), relative to the league-average staff (blitz 0.25, man 0.40, deep shots 0.148, no-huddle 0.21), so the league as a whole plays as before while a Zone or Air Raid staff differs
            PlaycallTendencies tend = StaffManager.Tendencies(t);
            float dcAgg = dcoord != null ? dcoord.Aggression : 50;
            c.BlitzBias = (tend.BlitzRate - Math.Max(0.10f, Math.Min(0.45f, 0.25f + (dcAgg - 50f) * 0.002f))) * 0.5f;   // the coordinator's aggression is already in c.Aggression; this is the scheme part
            c.ManBias = (tend.ManCoverageRate - 0.40f) * 0.8f;
            c.DeepShotBias = (tend.DeepShotRate - 0.148f) * 5f;
            c.NoHuddleBias = tend.NoHuddleRate - 0.21f;
            return c;
        }

        public CoachProfile ProfileOf(CoreTeam t)
        {
            CoachProfile p = ProfileOf(TeamOf(t));
            if (!StaffTendencies) { p.BlitzBias = 0f; p.ManBias = 0f; p.DeepShotBias = 0f; p.NoHuddleBias = 0f; }
            return p;
        }

        /// <summary>v0.8.0 no-huddle: an offense whose staff runs more snaps without a huddle burns less clock between snaps (about 3.5 seconds for a 0.18 higher rate; a slow, power-run staff burns a little more). The headless game calls it after every play; a live match that keeps its own clock can call it after <c>State.Apply</c> with the team that has the ball next.</summary>
        public void ApplyTempo(CoreTeam offense)
        {
            if (State.Phase != GamePhase.Scrimmage || !State.ClockRunning || State.PendingRunoff <= 0f) return;
            float bias = ProfileOf(offense).NoHuddleBias;
            if (bias == 0f) return;
            State.PendingRunoff = Math.Max(6f, State.PendingRunoff - bias * 20f);
        }

        /// <summary>QA-622: explicit scheme mapping (an enum cast silently changed every coach if either enum was reordered).</summary>
        public static PlayStyle StyleOf(OffenseScheme s)
        {
            switch (s)
            {
                case OffenseScheme.AirRaid: return PlayStyle.AirRaid;
                case OffenseScheme.WestCoast: return PlayStyle.WestCoast;
                case OffenseScheme.PowerRun: return PlayStyle.PowerRun;
                case OffenseScheme.SpreadOption: return PlayStyle.SpreadOption;
                default: return PlayStyle.Balanced;
            }
        }
        private bool WantsTwoPoint()
        {
            int diff = State.ScoreOf(State.Possession) - State.ScoreOf(State.Possession.Opponent());
            // after the touchdown the score already counts; go for two when it ties/ends a deficit
            if (State.Quarter >= 4 && (diff == -2 || diff == -5 || diff == -1 && State.ClockSeconds < 300f)) return Rng.Chance(0.6f);
            // the standard late-game chart also goes for two when leading by 5 or 12 after the touchdown (to make it a two-score margin)
            if (State.Quarter == 4 && State.ClockSeconds < 420f && (diff == 5 || diff == 12 || diff == -8 && State.ClockSeconds < 240f)) return Rng.Chance(0.5f);
            // QA-627: the rest of the chart (the conversion changes which scores the trailing team needs): trailing by 10, 13 or 14 after the touchdown
            if (State.Quarter == 4 && State.ClockSeconds < 480f && (diff == -10 || diff == -13 || diff == -14)) return Rng.Chance(0.5f);
            return Rng.Chance(0.02f);
        }

        /// <summary>CPU defensive call that answers a chosen offensive/special-teams play (used when the human picks the offense): personnel match the offense's formation.</summary>
        public Play DefenseResponseTo(Play off)
        {
            switch (off.Category)
            {
                case PlayCategory.Kickoff: case PlayCategory.Onside: return Pick(_kickReturns);
                case PlayCategory.Punt: return Pick(_puntReturns);
                case PlayCategory.FieldGoal: case PlayCategory.PointAfter: return Pick(_fgBlocks);
            }
            if (State.Phase == GamePhase.PointAfter) return _goalLineDefs.Count > 0 ? Pick(_goalLineDefs) : Pick(_zoneDefs);
            return Caller.CallDefense(State, ProfileOf(State.Possession.Opponent()), Book.FindFormation(off.FormationName));
        }
    }
}

namespace PixFootball.Sim
{
    public sealed partial class PhysicalGameSim
    {
        /// <summary>Longest field goal (yards, counting the snap) the coach trusts his kicker with: about 46 for a poor leg, 54 for an average one, 62 for an elite one.</summary>
        public int KickerRange(CoreTeam t)
        {
            LeagueTeam team = TeamOf(t);
            Player best = null; int bo = -1;
            if (team != null)
                for (int i = 0; i < team.Roster.Count; i++) { Player p = team.Roster[i]; if (p.Position == Position.K && p.IsAvailable && p.Overall > bo) { bo = p.Overall; best = p; } }
            if (best == null) return 0;
            return (int)Math.Round(30f + 0.22f * best.Ratings.KickPower + 0.10f * best.Ratings.KickAccuracy);
        }
    }
}
