using System;
using System.Collections.Generic;
using PixFootball.Core;
using Side = PixFootball.Core.Team;

namespace PixFootball.League
{
    /// <summary>
    /// Constants that shape QuickSim outcomes. Calibrated against the league generator so that an average game looks like
    /// pro football (~22 pts/team, ~225 pass yds, ~115 rush yds, 2.3 sacks, 0.8 INT) and a +10 overall edge wins ~80%.
    /// </summary>
    public static class SimTuning
    {
        public static float HomeEdge = 1.2f;
        public static float Sens = 0.4f;
        /// <summary>Average team overall the constants were calibrated at; weaker or stronger pairings are normalised to it.</summary>
        public static float ReferenceLevel = 76.7f;
        public static float SensCenter = 68f;
        // reference ratings of an average starter at each composite
        public static float RunoffMin = 22f;
        public static float RunoffMax = 32f;
        public static float PressRef = 0f;
        public static float CoverRefAvg = 70f;
        public static float MatchRef = 0f;
        public static float RunRef = 0f;
        public static float RefPassBlockDiff = 0f;
        public static float RefRunDiff = 0f;
        public static float RefCover = 0f;

        public static float SackBase = 0.061f;
        public static float SackK = 0.040f;
        public static float PressureBase = 0.26f;
        public static float PressureK = 0.034f;

        public static float RunMeanBase = 4.9f;
        public static float RunBlockK = 0.060f;
        public static float RunSkillK = 0.040f;
        public static float StuffBase = 0.14f;
        public static float StuffK = 0.0045f;
        public static float BigRunBase = 0.043f;

        public static float CompBaseCheck = 0.81f;
        public static float CompBaseShort = 0.73f;
        public static float CompBaseMed = 0.575f;
        public static float CompBaseDeep = 0.34f;
        public static float AccK = 0.0042f;
        public static float MatchK = 0.0058f;
        public static float CatchK = 0.0032f;
        public static float PressureCompPenalty = 0.13f;

        public static float IntBase = 0.049f;
        public static float YacMean = 3.65f;

        public static float PenaltyPreSnap = 0.040f;
        public static float PenaltyInPlay = 0.050f;
    }

    internal sealed class SimPlayer
    {
        public Player P;
        public Position Pos;
        public Ratings R;
        public StatLine L = new StatLine();
        public bool Started;
        public bool Dummy;
        // composites
        public float OlPass, OlRun, DlRush, DlStop, LbStop, LbCover, LbRush, CbCover, SCover, SStop;
        public float Open, Catch, Yac, RbRun, Tackle, QbMob, Kick, RunBlockTe;
        public float Share;
        public float Cover; // coverage composite for whatever role the player plays on this snap

        private static Ratings Compress(Ratings r, float shift)
        {
            float s = SimTuning.Sens, c = SimTuning.SensCenter;
            for (int k = 0; k < Ratings.KindCount; k++)
            {
                var kind = (RatingKind)k;
                r.Set(kind, (int)Math.Round(c + (r.Get(kind) + shift - c) * s));
            }
            return r;
        }

        public SimPlayer(Player p, Position pos, float shift)
        {
            P = p; Pos = pos; R = Compress(p.Ratings, shift);
            Ratings r = R;
            OlPass = 0.55f * r.PassBlocking + 0.20f * r.Strength + 0.15f * r.Awareness + 0.10f * r.Agility;
            OlRun = 0.55f * r.RunBlocking + 0.25f * r.Strength + 0.10f * r.Awareness + 0.10f * r.Acceleration;
            DlRush = 0.38f * r.BlockShedding + 0.17f * r.Strength + 0.17f * r.Acceleration + 0.13f * r.Speed + 0.08f * r.Agility + 0.07f * r.Awareness;
            DlStop = 0.35f * r.BlockShedding + 0.30f * r.Strength + 0.25f * r.Tackling + 0.10f * r.Awareness;
            LbStop = 0.40f * r.Tackling + 0.20f * r.BlockShedding + 0.20f * r.Awareness + 0.10f * r.Strength + 0.10f * r.Speed;
            LbCover = 0.40f * r.ZoneCoverage + 0.20f * r.ManCoverage + 0.15f * r.Speed + 0.15f * r.Awareness + 0.10f * r.Agility;
            LbRush = 0.40f * r.BlockShedding + 0.30f * r.Acceleration + 0.30f * r.Speed;
            CbCover = 0.35f * r.ManCoverage + 0.20f * r.ZoneCoverage + 0.20f * r.Speed + 0.10f * r.Agility + 0.10f * r.Awareness + 0.05f * r.Jumping;
            SCover = 0.35f * r.ZoneCoverage + 0.15f * r.ManCoverage + 0.20f * r.Awareness + 0.20f * r.Speed + 0.10f * r.Jumping;
            SStop = 0.60f * r.Tackling + 0.20f * r.Awareness + 0.20f * r.Speed;
            Open = 0.30f * r.Speed + 0.15f * r.Acceleration + 0.17f * r.Agility + 0.15f * r.Awareness + 0.13f * r.Elusiveness + 0.10f * r.Jumping;
            Catch = 0.75f * r.Catching + 0.25f * r.Awareness;
            Yac = 0.30f * r.Elusiveness + 0.25f * r.BreakTackle + 0.25f * r.Speed + 0.20f * r.Agility;
            RbRun = 0.22f * r.Speed + 0.12f * r.Acceleration + 0.20f * r.Elusiveness + 0.20f * r.BreakTackle + 0.12f * r.Agility + 0.14f * r.Carrying;
            Tackle = r.Tackling;
            QbMob = 0.5f * r.Speed + 0.3f * r.Elusiveness + 0.2f * r.Agility;
            Kick = 0.5f * r.KickPower + 0.5f * r.KickAccuracy;
            RunBlockTe = 0.7f * r.RunBlocking + 0.3f * r.Strength;
        }
    }

    internal sealed class SimUnit
    {
        public Team T;
        public int Idx;
        public int TeamId;
        public SimPlayer QB, RB1, RB2, FB, K, P, KR;
        public SimPlayer[] WR = new SimPlayer[4];
        public SimPlayer[] TE = new SimPlayer[2];
        public SimPlayer[] OL = new SimPlayer[5];
        public SimPlayer[] DL = new SimPlayer[4];
        public SimPlayer[] LB = new SimPlayer[3];
        public SimPlayer[] CB = new SimPlayer[3];
        public SimPlayer[] S = new SimPlayer[2];
        public List<SimPlayer> All = new List<SimPlayer>(32);
        public TeamBoxScore Box;

        public float PassBlock, RunBlock, PassRush, RunStop, CovCb, CovLb, CovS;
        public float Edge;           // home advantage in rating points
        public float OffBonus, DefBonus;
        public int Aggression, PassTendency;
        public float RecvTotalW;
        public float[] TargetW = new float[9];
        public SimPlayer[] Targets = new SimPlayer[9];
        public float[] TackleW = new float[9];
        public SimPlayer[] Tacklers = new SimPlayer[9];
        public float[] RushW = new float[7];
        public SimPlayer[] Rushers = new SimPlayer[7];
        public int Timeouts;
    }

    internal enum OutKind { Run, Scramble, PassComp, PassInc, Sack, Int, Punt, Fg, Kneel, Spike, Kickoff, Try, Penalty }

    internal sealed class PlayOutcome
    {
        public OutKind K;
        public PlayResult R;
        public SimPlayer Passer, Target, Rusher, Tackler, Defender, Sacker, Forcer, Returner, Kicker;
        public int Yards, Air, RetYards, Dist;
        public bool Fumble, FumbleLost, Td, Pressure, Good, Oob;
        public bool TwoPoint;
    }

    public static class QuickSim
    {
        public static GameResult SimGame(Team home, Team away, RuleSet rules, Rng rng)
        {
            return SimGame(home, away, rules, rng, null);
        }

        public static GameResult SimGame(Team home, Team away, RuleSet rules, Rng rng, SimOptions options)
        {
            var sim = new GameSim(home, away, rules ?? RuleSet.Pro(), rng, options ?? new SimOptions());
            return sim.Run();
        }
    }

    internal sealed partial class GameSim
    {
        private readonly Rng _rng;
        private readonly RuleSet _rules;
        private readonly SimOptions _opt;
        private readonly SimUnit[] _u = new SimUnit[2];
        private GameState _gs;
        private readonly GameResult _res = new GameResult();
        private DriveRecord _drive;
        private DriveRecord _pendingTd;
        private int _driveStartClock;
        private int _driveYards0;
        private bool _inRedZone;
        private readonly bool _log;
        private readonly float _shift;

        public GameSim(Team home, Team away, RuleSet rules, Rng rng, SimOptions opt)
        {
            _rng = rng; _opt = opt; _log = opt.KeepPlayLog;
            _rules = rules;
            if (opt.ForceWinner)
            {
                _rules = rules.Clone();
                _rules.AllowTies = false;
                _rules.MaxOvertimePeriods = 0;
            }
            float level = (TeamRating.Compute(home) + TeamRating.Compute(away)) * 0.5f;
            _shift = Clamp(SimTuning.ReferenceLevel - level, -8f, 32f);
            _u[0] = BuildUnit(home, 0);
            _u[1] = BuildUnit(away, 1);
            _res.HomeTeamId = home.Id; _res.AwayTeamId = away.Id;
            _res.Neutral = opt.Neutral;
            _u[0].Box = _res.Home; _u[1].Box = _res.Away;
            _res.Home.TeamId = home.Id; _res.Away.TeamId = away.Id;
            if (opt.HomeAdvantage && !opt.Neutral) _u[0].Edge = SimTuning.HomeEdge;
            _u[0].Timeouts = _u[1].Timeouts = _rules.TimeoutsPerHalf;
        }

        // ============================================================ lineup
        private static readonly Player[] NoPlayers = new Player[0];

        private SimPlayer Take(Team t, Position pos, List<Player> used, SimUnit u)
        {
            Player found = null;
            List<int> ids;
            if (t.Depth.TryGet(pos, out ids))
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    Player p = t.GetPlayer(ids[i]);
                    if (p != null && !p.Injury.IsInjured && !p.Retired && !used.Contains(p)) { found = p; break; }
                }
            }
            if (found == null)
            {
                int best = -1;
                PositionBucket b = PositionBuckets.Of(pos);
                for (int i = 0; i < t.Roster.Count; i++)
                {
                    Player p = t.Roster[i];
                    if (p.Injury.IsInjured || p.Retired || used.Contains(p) || p.IsRedshirting) continue;
                    if (PositionBuckets.Of(p.Position) != b) continue;
                    int o = p.Ratings.Overall(pos);
                    if (o > best) { best = o; found = p; }
                }
            }
            if (found == null)
            {
                int best = -1;
                for (int i = 0; i < t.Roster.Count; i++)
                {
                    Player p = t.Roster[i];
                    if (p.Injury.IsInjured || p.Retired || used.Contains(p) || p.IsRedshirting) continue;
                    if (PositionGroup.UnitOf(p.Position) != PositionGroup.UnitOf(pos)) continue;
                    int o = p.Ratings.Overall(pos);
                    if (o > best) { best = o; found = p; }
                }
            }
            SimPlayer sp;
            if (found == null)
            {
                var dummy = new Player { Position = pos, Ratings = Ratings.Uniform(42) };
                dummy.Bio.Id = -1000 - used.Count;
                dummy.Bio.FirstName = "Practice"; dummy.Bio.LastName = "Squad";
                sp = new SimPlayer(dummy, pos, _shift) { Dummy = true };
            }
            else
            {
                used.Add(found);
                sp = new SimPlayer(found, pos, _shift);
            }
            sp.Started = true;
            u.All.Add(sp);
            return sp;
        }

        private SimUnit BuildUnit(Team t, int idx)
        {
            var u = new SimUnit { T = t, Idx = idx, TeamId = t.Id };
            var used = new List<Player>(30);
            u.QB = Take(t, Position.QB, used, u);
            u.RB1 = Take(t, Position.RB, used, u);
            u.RB2 = Take(t, Position.RB, used, u);
            u.FB = Take(t, Position.FB, used, u);
            for (int i = 0; i < 4; i++) u.WR[i] = Take(t, Position.WR, used, u);
            for (int i = 0; i < 2; i++) u.TE[i] = Take(t, Position.TE, used, u);
            u.OL[0] = Take(t, Position.LT, used, u);
            u.OL[1] = Take(t, Position.LG, used, u);
            u.OL[2] = Take(t, Position.C, used, u);
            u.OL[3] = Take(t, Position.RG, used, u);
            u.OL[4] = Take(t, Position.RT, used, u);
            u.DL[0] = Take(t, Position.LE, used, u);
            u.DL[1] = Take(t, Position.DT, used, u);
            u.DL[2] = Take(t, Position.DT, used, u);
            u.DL[3] = Take(t, Position.RE, used, u);
            u.LB[0] = Take(t, Position.LOLB, used, u);
            u.LB[1] = Take(t, Position.MLB, used, u);
            u.LB[2] = Take(t, Position.ROLB, used, u);
            for (int i = 0; i < 3; i++) u.CB[i] = Take(t, Position.CB, used, u);
            u.S[0] = Take(t, Position.FS, used, u);
            u.S[1] = Take(t, Position.SS, used, u);
            u.K = Take(t, Position.K, used, u);
            u.P = Take(t, Position.P, used, u);
            // the second back/slot receiver are not "starters" in the box score sense, but they play
            u.RB2.Started = false; u.FB.Started = false; u.WR[3].Started = false; u.TE[1].Started = false;
            u.KR = u.WR[3];

            float olp = 0, olr = 0;
            for (int i = 0; i < 5; i++) { olp += u.OL[i].OlPass; olr += u.OL[i].OlRun; }
            u.PassBlock = olp / 5f;
            u.RunBlock = 0.8f * (olr / 5f) + 0.1f * u.TE[0].RunBlockTe + 0.1f * u.FB.RunBlockTe;
            float dlr = 0, dls = 0;
            for (int i = 0; i < 4; i++) { dlr += u.DL[i].DlRush; dls += u.DL[i].DlStop; }
            float lbr = 0, lbs = 0, lbc = 0;
            for (int i = 0; i < 3; i++) { lbr += u.LB[i].LbRush; lbs += u.LB[i].LbStop; lbc += u.LB[i].LbCover; }
            float ss = (u.S[0].SStop + u.S[1].SStop) * 0.5f;
            u.PassRush = 0.85f * (dlr / 4f) + 0.15f * (lbr / 3f);
            u.RunStop = 0.5f * (dls / 4f) + 0.35f * (lbs / 3f) + 0.15f * ss;
            u.CovLb = lbc / 3f;
            float cbc = 0;
            for (int i = 0; i < 3; i++) { u.CB[i].Cover = u.CB[i].CbCover; cbc += u.CB[i].CbCover; }
            u.CovCb = cbc / 3f;
            u.CovS = (u.S[0].SCover + u.S[1].SCover) * 0.5f;

            Coach hc = t.HeadCoach, oc = t.OffensiveCoordinator, dc = t.DefensiveCoordinator;
            u.Aggression = hc != null ? hc.Aggression : 50;
            u.PassTendency = oc != null ? oc.PassTendency : (hc != null ? hc.PassTendency : 50);
            u.OffBonus = oc != null ? (oc.OffenseRating - 65) * 0.05f : 0f;
            u.DefBonus = dc != null ? (dc.DefenseRating - 65) * 0.05f : 0f;

            // target distribution: WR1..4, TE1, TE2, RB1, RB2, FB
            SetTarget(u, 0, u.WR[0], 0.215f);
            SetTarget(u, 1, u.WR[1], 0.185f);
            SetTarget(u, 2, u.WR[2], 0.135f);
            SetTarget(u, 3, u.WR[3], 0.04f);
            SetTarget(u, 4, u.TE[0], 0.155f);
            SetTarget(u, 5, u.TE[1], 0.035f);
            SetTarget(u, 6, u.RB1, 0.15f);
            SetTarget(u, 7, u.RB2, 0.07f);
            SetTarget(u, 8, u.FB, 0.015f);

            // tacklers on defense: DL x4, LB x3, S x2 (cbs added by position when covering)
            int ti = 0;
            for (int i = 0; i < 4; i++) { u.Tacklers[ti] = u.DL[i]; u.TackleW[ti] = 0.07f * (0.6f + u.DL[i].R.Tackling / 100f); ti++; }
            u.Tacklers[ti] = u.LB[0]; u.TackleW[ti++] = 0.17f * (0.6f + u.LB[0].R.Tackling / 100f);
            u.Tacklers[ti] = u.LB[1]; u.TackleW[ti++] = 0.24f * (0.6f + u.LB[1].R.Tackling / 100f);
            u.Tacklers[ti] = u.LB[2]; u.TackleW[ti++] = 0.17f * (0.6f + u.LB[2].R.Tackling / 100f);
            u.Tacklers[ti] = u.S[0]; u.TackleW[ti++] = 0.09f * (0.6f + u.S[0].R.Tackling / 100f);
            u.Tacklers[ti] = u.S[1]; u.TackleW[ti++] = 0.12f * (0.6f + u.S[1].R.Tackling / 100f);

            // pass rushers: 4 DL (weighted by rush) + LB blitz
            for (int i = 0; i < 4; i++) { u.Rushers[i] = u.DL[i]; u.RushW[i] = 0.2f * (0.5f + u.DL[i].DlRush / 100f); }
            u.Rushers[4] = u.LB[0]; u.RushW[4] = 0.05f * (0.5f + u.LB[0].LbRush / 100f);
            u.Rushers[5] = u.LB[1]; u.RushW[5] = 0.04f * (0.5f + u.LB[1].LbRush / 100f);
            u.Rushers[6] = u.LB[2]; u.RushW[6] = 0.05f * (0.5f + u.LB[2].LbRush / 100f);
            return u;
        }

        private static void SetTarget(SimUnit u, int i, SimPlayer p, float share)
        {
            u.Targets[i] = p;
            float skill = (p.Open + p.Catch) * 0.5f;
            if (p.Pos == Position.RB || p.Pos == Position.FB) skill = (p.Catch * 0.7f + p.Open * 0.3f);
            u.TargetW[i] = share * (0.65f + 0.5f * (skill / 75f));
        }

        // ============================================================ small helpers
        private static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        private int Pick(float[] w, int n)
        {
            float total = 0f;
            for (int i = 0; i < n; i++) total += w[i];
            float r = _rng.NextFloat() * total;
            for (int i = 0; i < n; i++) { r -= w[i]; if (r < 0f) return i; }
            return n - 1;
        }

        private float Exp(float mean)
        {
            float u = _rng.NextFloat();
            return -(float)Math.Log(1.0 - u) * mean;
        }

        // ============================================================ main loop
        public GameResult Run()
        {
            Side opener = _rng.Chance(0.5f) ? Side.Away : Side.Home;
            _gs = new GameState(_rules, opener);
            _gs.OvertimeReceiverDefault = _rng.Chance(0.5f) ? Side.Home : Side.Away;
            int guard = 0;
            while (!_gs.IsFinal && guard++ < 1600)
            {
                switch (_gs.Phase)
                {
                    case GamePhase.Kickoff: DoKickoff(); break;
                    case GamePhase.PointAfter: DoTry(); break;
                    case GamePhase.Scrimmage: DoScrimmage(); break;
                    case GamePhase.Overtime: _gs.StartOvertime(_gs.OvertimeReceiverDefault); break;
                }
            }
            if (!_gs.IsFinal) { /* safety valve: should never happen */ }
            Finish();
            return _res;
        }

        private void Finish()
        {
            if (_drive != null) CloseDrive(_gs.IsFinal ? "End of Game" : "End of Game");
            _res.HomeScore = _gs.HomeScore; _res.AwayScore = _gs.AwayScore;
            _res.Home.Score = _gs.HomeScore; _res.Away.Score = _gs.AwayScore;
            _res.Overtime = _gs.Quarter > 4;
            _res.OvertimePeriods = Math.Max(0, _gs.Quarter - 4);
            _res.TotalPlays = _gs.PlayCount;
            for (int s = 0; s < 2; s++)
            {
                SimUnit u = _u[s];
                for (int i = 0; i < u.All.Count; i++)
                {
                    SimPlayer sp = u.All[i];
                    if (sp.Dummy) continue;
                    if (!sp.Started && !sp.L.HasActivity) continue;
                    sp.L.Games = 1;
                    sp.L.GamesStarted = sp.Started ? 1 : 0;
                    _res.PlayerLines.Add(new PlayerGameLine
                    {
                        PlayerId = sp.P.Id, Name = sp.P.Name, Pos = sp.Pos, TeamId = u.TeamId, Started = sp.Started, Stats = sp.L
                    });
                }
            }
        }

        // ============================================================ drive / scoring bookkeeping
        private SimUnit Off { get { return _u[(int)_gs.Possession]; } }
        private SimUnit Def { get { return _u[(int)_gs.Possession.Opponent()]; } }

        private void OpenDriveIfNeeded()
        {
            if (_drive != null) return;
            SimUnit o = Off;
            _drive = new DriveRecord
            {
                TeamId = o.TeamId, Quarter = _gs.Quarter, StartClockSeconds = (int)_gs.ClockSeconds, StartSpot = _gs.BallSpot
            };
            _driveStartClock = (int)_gs.ClockSeconds;
            _driveYards0 = _gs.BallSpot;
            o.Box.Drives++;
            _inRedZone = false;
        }

        private void CloseDrive(string result)
        {
            if (_drive == null) return;
            _drive.Result = result;
            _drive.Seconds = Math.Max(0, _driveStartClock - (int)_gs.ClockSeconds);
            if (_drive.Quarter != _gs.Quarter && _drive.Quarter <= 4 && _gs.Quarter <= 4)
                _drive.Seconds = Math.Max(0, _driveStartClock + (_gs.Quarter - _drive.Quarter) * _rules.QuarterSeconds - (int)_gs.ClockSeconds);
            _res.Drives.Add(_drive);
            _drive = null;
        }

        private void AddScoringEvent(int teamIdx, int points, string desc, int quarter, int clock)
        {
            _res.Scoring.Add(new ScoringEvent
            {
                Quarter = quarter, ClockSeconds = clock, TeamId = _u[teamIdx].TeamId, Points = points, Description = desc,
                HomeScore = _gs.HomeScore, AwayScore = _gs.AwayScore
            });
            TeamBoxScore b = _u[teamIdx].Box;
            if (quarter <= 4)
            {
                while (b.ByQuarter.Count < quarter) b.ByQuarter.Add(0);
                b.ByQuarter[quarter - 1] += points;
            }
            else b.OvertimeScore += points;
            if (_drive != null && _drive.TeamId == _u[teamIdx].TeamId) _drive.Points += points;
        }

    }
}
