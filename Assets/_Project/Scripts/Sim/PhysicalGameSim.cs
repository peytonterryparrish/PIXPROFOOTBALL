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
    public sealed class PhysicalGameSim
    {
        public readonly GameState State;
        public readonly RuleSet Rules;
        public readonly Rng Rng;
        public readonly Playbook Book;
        public readonly LeagueTeam Home, Away;
        public readonly List<PlayRecord> Plays = new List<PlayRecord>();
        public bool KeepLogs = false;

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
            Remember(State.Possession, off);
            rec.OffensePlayName = off.Name; rec.DefensePlayName = def.Name;

            CoreTeam offTeam = State.Possession;
            LeagueTeam offLeague = TeamOf(offTeam), defLeague = TeamOf(offTeam.Opponent());
            Formation of = Book.FindFormation(off.FormationName), df = Book.FindFormation(def.FormationName);
            Player[] ro = SimRosters.ForFormation(offLeague, of);
            Player[] rd = SimRosters.ForFormation(defLeague, df);

            var sim = new PlaySimulation();
            sim.Setup(State, off, def, ro, rd, Rules, Rng.Fork(), new PlaySimOptions { Playbook = Book });
            PlayResult result = sim.RunToCompletion();
            rec.Result = result;
            rec.SimSeconds = sim.Time;
            if (result.Penalty != null)
            {
                if (result.Type == PlayType.Penalty) rec.PenaltyAccepted = result.PenaltyDecision != PenaltyDecision.Decline;
                else
                {
                    rec.PenaltyAccepted = PenaltyEnforcer.ShouldAccept(State, result);
                    result.PenaltyDecision = rec.PenaltyAccepted ? PenaltyDecision.Accept : PenaltyDecision.Decline;
                }
            }
            if (KeepLogs) rec.Log = sim.Log;
            State.Apply(result);
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
                    bool trailing = State.PossessionScoreDiff < 0 && State.PossessionScoreDiff >= -8;
                    bool onside = State.Quarter == 4 && State.ClockSeconds < 200f && trailing && Rng.Chance(0.75f);
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
            if (State.Down == 4)
            {
                switch (Caller.FourthDown(State, oc))
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
            c.Style = oc != null ? (PlayStyle)(int)oc.OffScheme : PlayStyle.Balanced;
            return c;
        }

        public CoachProfile ProfileOf(CoreTeam t) { return ProfileOf(TeamOf(t)); }
        private bool WantsTwoPoint()
        {
            int diff = State.ScoreOf(State.Possession) - State.ScoreOf(State.Possession.Opponent());
            // after the touchdown the score already counts; go for two when it ties/ends a deficit
            if (State.Quarter >= 4 && (diff == -2 || diff == -5 || diff == -1 && State.ClockSeconds < 300f)) return Rng.Chance(0.6f);
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

