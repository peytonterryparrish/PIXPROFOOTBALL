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
        private Play _punt, _fieldGoal, _extraPoint, _kneel, _sneak;

        public PhysicalGameSim(LeagueTeam home, LeagueTeam away, RuleSet rules, Rng rng, Playbook book = null, CoreTeam openingKicker = CoreTeam.Away)
        {
            Home = home; Away = away; Rules = rules ?? RuleSet.Pro(); Rng = rng;
            Book = book ?? DefaultPlaybooks.Create();
            State = new GameState(Rules, openingKicker);
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

        private Play PickByFormation(List<Play> list, string formationOrNull)
        {
            if (formationOrNull == null) return Pick(list);
            var sub = new List<Play>();
            foreach (Play p in list) if (p.FormationName == formationOrNull) sub.Add(p);
            return sub.Count > 0 ? Pick(sub) : Pick(list);
        }

        private void ChoosePlays(out Play off, out Play def)
        {
            switch (State.Phase)
            {
                case GamePhase.Kickoff:
                {
                    // the kicking team (possession) trails when its diff is negative
                    bool trailing = State.PossessionScoreDiff < 0 && State.PossessionScoreDiff >= -8;
                    bool onside = State.Quarter == 4 && State.ClockSeconds < 200f && trailing && Rng.Chance(0.75f);
                    off = onside ? Pick(_onsides) : Pick(_kickoffs);
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

            // scrimmage downs
            int toGoal = 100 - State.BallSpot;
            int scoreDiff = State.PossessionScoreDiff;
            bool late = State.Quarter == 4 && State.ClockSeconds < 240f;
            bool closing = State.Quarter == 4 && State.ClockSeconds < 130f && scoreDiff > 0 && State.Down <= 3 && State.TimeoutsOf(State.Possession.Opponent()) == 0;
            if (closing && _kneel != null) { off = _kneel; def = DefenseFor(false); return; }

            if (State.Down == 4)
            {
                int fgDist = toGoal + 17;
                bool fgRange = fgDist <= (Rules.CollegeStyle ? 50 : 54);
                bool desperate = late && scoreDiff < 0;
                if (State.Distance <= 2 && toGoal <= 45 && (Rng.Chance(0.45f) || desperate && toGoal > 20)) { PickScrimmageCall(out off, out def); return; }
                if (desperate && State.Distance > 3 && State.BallSpot > 35 && !fgRange) { PickScrimmageCall(out off, out def); return; }
                if (fgRange && !(desperate && scoreDiff < -3)) { off = _fieldGoal; def = Pick(_fgBlocks); return; }
                if (State.BallSpot >= 35 || !desperate) { off = _punt; def = Pick(_puntReturns); return; }
            }
            PickScrimmageCall(out off, out def);
        }

        private bool WantsTwoPoint()
        {
            int diff = State.ScoreOf(State.Possession) - State.ScoreOf(State.Possession.Opponent());
            // after the touchdown the score already counts; go for two when it ties/ends a deficit
            if (State.Quarter >= 4 && (diff == -2 || diff == -5 || diff == -1 && State.ClockSeconds < 300f)) return Rng.Chance(0.6f);
            return Rng.Chance(0.02f);
        }

        private void PickScrimmageCall(out Play off, out Play def)
        {
            int toGoal = 100 - State.BallSpot;
            bool shortYardage = State.Distance <= 2;
            bool goalLine = toGoal <= 3;
            float pPass;
            if (State.Down == 1) pPass = 0.48f;
            else if (State.Distance >= 8) pPass = 0.78f;
            else if (State.Distance >= 4) pPass = 0.58f;
            else pPass = 0.34f;
            int scoreDiff = State.PossessionScoreDiff;
            if (State.Quarter == 4 && scoreDiff < -7 && State.ClockSeconds < 600f) pPass = Math.Max(pPass, 0.8f);
            if (State.Quarter == 4 && scoreDiff > 7 && State.ClockSeconds < 600f) pPass = Math.Min(pPass, 0.28f);

            if (goalLine && shortYardage && _sneak != null && Rng.Chance(0.25f)) { off = _sneak; def = DefenseFor(true); return; }
            bool pass = Rng.Chance(pPass);
            if (goalLine && !pass && _goalLineOff.Count > 0) { off = Pick(_goalLineOff); def = DefenseFor(true); return; }
            if (pass)
            {
                float r = Rng.NextFloat();
                if (r < 0.07f && _screens.Count > 0) off = Pick(_screens);
                else
                {
                    var pool = new List<Play>();
                    foreach (Play p in _passes)
                    {
                        // shape the mix by distance
                        if (State.Distance <= 4 && (p.Category == PlayCategory.DeepPass) && !goalLine) continue;
                        if (State.Distance >= 10 && p.Category == PlayCategory.ShortPass && Rng.Chance(0.4f)) continue;
                        pool.Add(p);
                    }
                    off = pool.Count > 0 ? Pick(pool) : Pick(_passes);
                }
            }
            else off = Pick(_runs);
            def = DefenseFor(goalLine);
        }

        private Play DefenseFor(bool goalLineCall)
        {
            if (goalLineCall && _goalLineDefs.Count > 0 && Rng.Chance(0.7f)) return Pick(_goalLineDefs);
            bool late = State.Quarter == 4 && State.ClockSeconds < 100f;
            int scoreDiff = State.PossessionScoreDiff; // offense perspective
            if (late && scoreDiff < 0 && State.BallSpot < 70 && _preventDefs.Count > 0 && Rng.Chance(0.4f)) return Pick(_preventDefs);
            float r = Rng.NextFloat();
            float pBlitz = State.Distance >= 7 ? 0.20f : 0.12f;
            if (r < pBlitz) return Pick(_blitzDefs);
            if (r < pBlitz + 0.28f) return Pick(_manDefs);
            return Pick(_zoneDefs);
        }
    }
}
