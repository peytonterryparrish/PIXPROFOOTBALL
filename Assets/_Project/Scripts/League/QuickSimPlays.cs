using System;
using System.Collections.Generic;
using PixFootball.Core;
using Side = PixFootball.Core.Team;

namespace PixFootball.League
{
    internal enum Call { Run, Pass, Punt, FieldGoal, Kneel, Spike }

    internal sealed partial class GameSim
    {
        // ============================================================ apply + bookkeeping
        private void ApplyOutcome(PlayOutcome o)
        {
            SimUnit off = Off, def = Def;
            GamePhase phaseBefore = _gs.Phase;
            if (phaseBefore == GamePhase.Scrimmage) OpenDriveIfNeeded();
            int qBefore = _gs.Quarter;
            float clockBefore = _gs.ClockSeconds;
            int hs = _gs.HomeScore, aws = _gs.AwayScore;
            int downB = _gs.Down, distB = _gs.Distance, spotB = _gs.BallSpot;
            Side possB = _gs.Possession;

            bool penaltyAccepted = false;
            if (o.R.Penalty != null)
            {
                penaltyAccepted = o.R.Type == PlayType.Penalty || PenaltyEnforcer.ShouldAccept(_gs, o.R);
                o.R.PenaltyDecision = penaltyAccepted ? PenaltyDecision.Accept : PenaltyDecision.Decline;
            }

            float before = _gs.ClockSeconds;
            bool applied = _gs.Apply(o.R);
            float burned = before - _gs.ClockSeconds;
            if (burned > 0f && _gs.Quarter == qBefore) off.Box.TimeOfPossessionSeconds += (int)burned;

            if (!applied)
            {
                // period ended during the pre-snap runoff: nothing happened, but the clock moved
                if (_drive != null && _gs.Quarter != qBefore && qBefore == 2) CloseDrive("End of Half");
                return;
            }

            bool gotFirst = false;
            if (!penaltyAccepted)
            {
                gotFirst = o.Td || ((o.K == OutKind.Run || o.K == OutKind.Scramble || o.K == OutKind.PassComp)
                                    && _gs.Phase == GamePhase.Scrimmage && _gs.Possession == possB && _gs.Down == 1);
            }

            if (penaltyAccepted)
            {
                SimUnit by = o.R.Penalty.CommittedBy == possB ? off : def;
                by.Box.Penalties++;
                int py = o.R.Penalty.Def.YardsFor(_rules, o.R.Penalty.FoulSpotYards);
                by.Box.PenaltyYds += py;
                SimPlayer pp = by == off ? off.OL[_rng.Range(0, 5)] : def.DL[_rng.Range(0, 4)];
                pp.L.Penalties++; pp.L.PenaltyYds += py;
                if (_log) _res.PlayLog.Add("Q" + qBefore + " " + ClockText(clockBefore) + " Penalty: " + o.R.Penalty.Def.Name + " on " + by.T.Abbreviation);
            }
            else
            {
                CommitStats(o, off, def, downB, distB, spotB, possB, gotFirst);
            if (_log && !penaltyAccepted) _res.PlayLog.Add("Q" + qBefore + " " + ClockText(clockBefore) + " " + off.T.Abbreviation + " " + DownText(downB, distB, spotB) + ": " + PlayText(o));
            }

            int dh = _gs.HomeScore - hs, da = _gs.AwayScore - aws;
            if (o.K == OutKind.Try) { int tp = possB == Side.Home ? dh : da; if (tp > 0 && _pendingTd != null) _pendingTd.Points += tp; _pendingTd = null; }
            else _pendingTd = null;
            if (dh > 0) AddScoringEvent(0, dh, Describe(o, dh, penaltyAccepted), qBefore, (int)clockBefore);
            if (da > 0) AddScoringEvent(1, da, Describe(o, da, penaltyAccepted), qBefore, (int)clockBefore);

            if (phaseBefore == GamePhase.Scrimmage && _drive != null)
            {
                if (!penaltyAccepted || o.R.Type != PlayType.Penalty) _drive.Plays += (o.R.Type == PlayType.Penalty ? 0 : 1);
                if (_gs.Possession == possB && _gs.Phase == GamePhase.Scrimmage) _drive.Yards = _gs.BallSpot - _driveYards0;
                string end = null;
                bool offScored = (possB == Side.Home ? dh : da) > 0;
                bool defScored = (possB == Side.Home ? da : dh) > 0;
                if (_gs.Phase == GamePhase.PointAfter && offScored) { end = "Touchdown"; _drive.Yards = Math.Max(0, 100 - _driveYards0); }
                else if (!penaltyAccepted && o.R.Type == PlayType.FieldGoal && o.Good) end = "Field Goal";
                else if (!penaltyAccepted && o.R.Type == PlayType.FieldGoal) end = "Missed FG";
                else if (defScored && (possB == Side.Home ? da : dh) == 2) end = "Safety";
                else if (!penaltyAccepted && o.R.Type == PlayType.Punt && !o.R.RecoveredByKickingTeam) end = "Punt";
                else if (!penaltyAccepted && o.K == OutKind.Int) end = "Interception";
                else if (!penaltyAccepted && o.FumbleLost) end = "Fumble";
                else if (offScored && _gs.Phase != GamePhase.Scrimmage) end = "Touchdown";
                else if (_gs.Phase == GamePhase.Scrimmage && _gs.Possession != possB) end = "Downs";
                else if (qBefore == 2 && _gs.Quarter == 3) end = "End of Half";
                else if (qBefore == 4 && _gs.Quarter == 5) end = "End of Half";
                else if (_gs.IsFinal) end = "End of Game";
                if (end != null)
                {
                    if (end == "Touchdown" && _inRedZone) off.Box.RedZoneTds++;
                    DriveRecord closing = _drive;
                    CloseDrive(end);
                    if (end == "Touchdown") _pendingTd = closing;
                }
            }
        }

        private static string ClockText(float s) { int c = (int)s; return (c / 60) + ":" + (c % 60).ToString("00"); }

        private static string DownText(int down, int dist, int spot)
        {
            string[] ord = { "", "1st", "2nd", "3rd", "4th" };
            return ord[Math.Max(1, Math.Min(4, down))] + " & " + dist + " at " + (spot <= 50 ? "own " + spot : "opp " + (100 - spot));
        }

        private string PlayText(PlayOutcome o)
        {
            switch (o.K)
            {
                case OutKind.Run: return Name(o.Rusher) + " rush for " + o.Yards + " yds" + (o.Td ? " TD" : "") + (o.FumbleLost ? " (fumble lost)" : "");
                case OutKind.Scramble: return Name(o.Rusher) + " scramble for " + o.Yards + " yds" + (o.Td ? " TD" : "");
                case OutKind.PassComp: return Name(o.Passer) + " pass to " + Name(o.Target) + " for " + o.Yards + " yds" + (o.Td ? " TD" : "") + (o.FumbleLost ? " (fumble lost)" : "");
                case OutKind.PassInc: return Name(o.Passer) + " pass incomplete to " + Name(o.Target);
                case OutKind.Sack: return Name(o.Passer) + " sacked by " + Name(o.Sacker) + " for " + (-o.Yards) + " yds";
                case OutKind.Int: return Name(o.Passer) + " intercepted by " + Name(o.Defender) + (o.Td ? " (returned for TD)" : "");
                case OutKind.Punt: return Name(o.Kicker) + " punt " + o.Dist + " yds";
                case OutKind.Fg: return Name(o.Kicker) + " " + o.Dist + " yd field goal " + (o.Good ? "GOOD" : "NO GOOD");
                case OutKind.Kneel: return "QB kneel";
                case OutKind.Spike: return "QB spike";
                case OutKind.Kickoff: return "Kickoff" + (o.RetYards > 0 ? ", return " + o.RetYards : ", touchback");
                case OutKind.Try: return o.TwoPoint ? "Two-point try " + (o.Good ? "GOOD" : "FAILED") : "Extra point " + (o.Good ? "GOOD" : "NO GOOD");
            }
            return o.K.ToString();
        }

        private string Describe(PlayOutcome o, int points, bool penaltyAccepted)
        {
            switch (o.R.Type)
            {
                case PlayType.FieldGoal: return Name(o.Kicker) + " " + o.Dist + " yd field goal";
                case PlayType.PAT: return Name(o.Kicker) + " extra point";
                case PlayType.TwoPoint: return "Two-point conversion";
                case PlayType.Pass: return Name(o.Target) + " " + Math.Max(0, o.Yards) + " yd pass from " + Name(o.Passer);
                case PlayType.Run: return Name(o.Rusher) + " " + Math.Max(0, o.Yards) + " yd run";
                case PlayType.Interception: return Name(o.Defender) + " interception return";
                case PlayType.Punt: return Name(o.Returner) + " punt return";
                case PlayType.Kickoff: return Name(o.Returner) + " kickoff return";
                case PlayType.Fumble: return "Fumble return";
                case PlayType.Sack: return "Safety";
            }
            return points == 2 ? "Safety" : "Score";
        }

        private static string Name(SimPlayer p) { return p == null ? "Team" : p.P.Name; }

        private void CommitStats(PlayOutcome o, SimUnit off, SimUnit def, int downB, int distB, int spotB, Side possB, bool gotFirst)
        {
            TeamBoxScore ob = off.Box, db = def.Box;
            bool scrim = o.K == OutKind.Run || o.K == OutKind.Scramble || o.K == OutKind.PassComp || o.K == OutKind.PassInc
                         || o.K == OutKind.Sack || o.K == OutKind.Int || o.K == OutKind.Kneel || o.K == OutKind.Spike;
            if (scrim)
            {
                ob.Plays++;
                if (spotB >= 80 && !_inRedZone && _drive != null) { _inRedZone = true; ob.RedZoneTrips++; }
                if (downB == 3 && o.K != OutKind.Kneel && o.K != OutKind.Spike) { ob.ThirdDownAtt++; if (gotFirst) ob.ThirdDownConv++; }
                if (downB == 4 && o.K != OutKind.Kneel) { ob.FourthDownAtt++; if (gotFirst) ob.FourthDownConv++; }
                if (gotFirst) ob.FirstDowns++;
            }
            switch (o.K)
            {
                case OutKind.Run:
                case OutKind.Scramble:
                {
                    SimPlayer r = o.Rusher;
                    ob.RushAtt++; ob.RushYds += o.Yards;
                    r.L.RushAtt++; r.L.RushYds += o.Yards;
                    if (o.Yards > r.L.RushLong) r.L.RushLong = o.Yards;
                    if (o.Td) { ob.RushTd++; r.L.RushTd++; }
                    if (o.Tackler != null && !o.Td) { o.Tackler.L.Tackles++; if (o.Yards < 0) o.Tackler.L.TacklesForLoss++; }
                    if (o.Fumble) CommitFumble(o, r, ob, db);
                    break;
                }
                case OutKind.Kneel:
                {
                    ob.Kneels++; ob.RushAtt++; ob.RushYds -= 1;
                    o.Rusher.L.RushAtt++; o.Rusher.L.RushYds -= 1;
                    break;
                }
                case OutKind.Spike:
                    ob.PassAtt++; o.Passer.L.PassAtt++;
                    break;
                case OutKind.PassComp:
                {
                    ob.PassAtt++; ob.PassCmp++; ob.PassYds += o.Yards;
                    SimPlayer q = o.Passer, t = o.Target;
                    q.L.PassAtt++; q.L.PassCmp++; q.L.PassYds += o.Yards;
                    if (o.Yards > q.L.PassLong) q.L.PassLong = o.Yards;
                    t.L.Targets++; t.L.Rec++; t.L.RecYds += o.Yards;
                    if (o.Yards > t.L.RecLong) t.L.RecLong = o.Yards;
                    if (o.Td) { ob.PassTd++; q.L.PassTd++; t.L.RecTd++; }
                    if (o.Tackler != null && !o.Td) o.Tackler.L.Tackles++;
                    if (o.Fumble) CommitFumble(o, t, ob, db);
                    break;
                }
                case OutKind.PassInc:
                {
                    ob.PassAtt++;
                    o.Passer.L.PassAtt++;
                    if (o.Target != null) o.Target.L.Targets++;
                    if (o.Defender != null) o.Defender.L.PassesDefended++;
                    break;
                }
                case OutKind.Sack:
                {
                    ob.Sacked++; ob.SackYdsLost += -o.Yards;
                    o.Passer.L.Sacked++; o.Passer.L.SackYds += -o.Yards;
                    db.SacksMade++;
                    if (o.Sacker != null) { o.Sacker.L.Sacks++; o.Sacker.L.Tackles++; }
                    if (o.Fumble) CommitFumble(o, o.Passer, ob, db);
                    break;
                }
                case OutKind.Int:
                {
                    ob.PassAtt++; ob.PassInt++; ob.Giveaways++;
                    db.InterceptionsMade++; db.Takeaways++;
                    o.Passer.L.PassAtt++; o.Passer.L.PassInt++;
                    if (o.Target != null) o.Target.L.Targets++;
                    o.Defender.L.Interceptions++;
                    o.Defender.L.IntYds += o.RetYards;
                    if (o.Td) o.Defender.L.IntTd++;
                    break;
                }
                case OutKind.Punt:
                {
                    ob.Punts++; ob.PuntYds += o.Dist;
                    o.Kicker.L.Punts++; o.Kicker.L.PuntYds += o.Dist;
                    if (spotB + o.Dist >= 80 && spotB + o.Dist < 100) o.Kicker.L.PuntIn20++;
                    if (o.Returner != null && o.RetYards > 0 || (o.Returner != null && o.R.ReturnYards == 0 && !o.R.FairCatch && !o.R.Touchback))
                    {
                        db.PuntReturnYds += o.RetYards;
                        o.Returner.L.PrAtt++; o.Returner.L.PrYds += o.RetYards;
                        if (o.Td) o.Returner.L.RetTd++;
                    }
                    break;
                }
                case OutKind.Fg:
                {
                    ob.FgAtt++; o.Kicker.L.FgAtt++;
                    if (o.Good) { ob.FgMade++; o.Kicker.L.FgMade++; if (o.Dist > o.Kicker.L.FgLong) o.Kicker.L.FgLong = o.Dist; }
                    break;
                }
                case OutKind.Kickoff:
                {
                    if (o.Returner != null && o.RetYards > 0)
                    {
                        db.KickoffReturnYds += o.RetYards;
                        o.Returner.L.KrAtt++; o.Returner.L.KrYds += o.RetYards;
                        if (o.Td) o.Returner.L.RetTd++;
                    }
                    break;
                }
                case OutKind.Try:
                {
                    if (o.TwoPoint) { ob.TwoPointAtt++; if (o.Good) ob.TwoPointMade++; }
                    else { ob.XpAtt++; o.Kicker.L.XpAtt++; if (o.Good) { ob.XpMade++; o.Kicker.L.XpMade++; } }
                    break;
                }
            }
        }

        private void CommitFumble(PlayOutcome o, SimPlayer carrier, TeamBoxScore ob, TeamBoxScore db)
        {
            carrier.L.Fumbles++;
            if (o.Forcer != null) { o.Forcer.L.ForcedFumbles++; db.FumblesForced++; }
            if (o.FumbleLost)
            {
                carrier.L.FumblesLost++;
                ob.FumblesLost++; ob.Giveaways++; db.Takeaways++;
                if (o.Tackler != null) o.Tackler.L.FumbleRecoveries++;
            }
        }

        // ============================================================ picking helpers
        private SimPlayer PickTackler(SimUnit def, bool deep)
        {
            if (deep)
            {
                float a = def.LB[0].Cover + 0f;
                // deep gains: mostly DBs and LBs
                float r = _rng.NextFloat();
                if (r < 0.28f) return def.CB[_rng.Range(0, 3)];
                if (r < 0.58f) return def.S[_rng.Range(0, 2)];
                return def.LB[_rng.Range(0, 3)];
            }
            int i = Pick(def.TackleW, 9);
            return def.Tacklers[i];
        }

        private SimPlayer PickRusher(SimUnit def, bool sackPlay)
        {
            int i = Pick(def.RushW, 7);
            return def.Rushers[i];
        }

        // ============================================================ kickoff / tries
        private void DoKickoff()
        {
            SimUnit k = Off;
            SimUnit rcv = Def;
            var o = new PlayOutcome { K = OutKind.Kickoff, Kicker = k.K };
            int diff = _gs.ScoreOf(_gs.Possession) - _gs.ScoreOf(_gs.Possession.Opponent());
            bool onside = false;
            if (!_gs.FreeKick && _gs.Quarter == 4 && diff < 0 && diff >= -16)
            {
                float c = _gs.ClockSeconds;
                float p = c <= 120f ? 0.85f : (c <= 300f ? 0.35f : 0f);
                if (diff < -8) p *= 0.6f;
                onside = _rng.Chance(p);
            }
            if (onside)
            {
                bool rec = _rng.Chance(0.14f);
                o.R = PlayResult.Onside(12, rec);
                o.R.KickerId = k.K.P.Id;
                ApplyOutcome(o);
                return;
            }

            if (_gs.FreeKick)
            {
                int dist = (int)Math.Round(_rng.Gaussian(52f + (k.P.R.KickPower - 75) * 0.2f, 5f));
                bool fc = _rng.Chance(0.3f);
                int ret = fc ? 0 : (int)Math.Round(Exp(9f));
                o.Returner = rcv.KR; o.RetYards = ret;
                o.R = PlayResult.Kickoff(dist, ret, false, false, false);
                ApplyOutcome(o);
                return;
            }
            float power = k.K.R.KickPower;
            int d = (int)Math.Round(_rng.Gaussian(65.5f + (power - 75f) * 0.12f, 4.5f));
            d = Clamp(d, 48, 82);
            int land = _gs.KickSpot + d;
            if (land >= 100)
            {
                o.R = PlayResult.Kickoff(d, 0, false, true);
                o.R.KickerId = k.K.P.Id;
            }
            else
            {
                bool oob = _rng.Chance(0.025f);
                int ret = oob ? 0 : (int)Math.Round(_rng.Gaussian(24f, 7f));
                if (ret < 0) ret = 0;
                bool td = !oob && _rng.Chance(0.004f);
                if (td) ret = land;
                o.Returner = rcv.KR; o.RetYards = ret; o.Td = td;
                o.R = PlayResult.Kickoff(d, ret, td, false, oob);
                o.R.KickerId = k.K.P.Id; o.R.ReturnerId = rcv.KR.P.Id;
            }
            ApplyOutcome(o);
        }

        private bool ShouldGoForTwo()
        {
            int d = _gs.PossessionScoreDiff; // before the try, scoring team's perspective
            if (_gs.Quarter < 3) return false;
            if (_gs.Quarter == 3 && _gs.ClockSeconds > 300f) return false;
            if (_gs.Quarter > 4) return false;
            switch (d)
            {
                case -2: case -5: case -8: case -10: return true;
                case 5: return _gs.Quarter == 4;
                case 1: case -1: return false;
            }
            if (d == 12 && _gs.Quarter == 4) return _rng.Chance(0.5f);
            return false;
        }

        private void DoTry()
        {
            SimUnit u = Off;
            bool two = _gs.RequiresTwoPoint || ShouldGoForTwo();
            var o = new PlayOutcome { K = OutKind.Try, Kicker = u.K, TwoPoint = two };
            if (two)
            {
                float p = 0.475f + (u.OffBonus - Def.DefBonus) * 0.01f + (u.RunBlock - Def.RunStop) * 0.002f;
                o.Good = _rng.Chance(Clamp(p, 0.3f, 0.65f));
                o.R = PlayResult.TwoPointTry(o.Good);
            }
            else
            {
                float basep = _rules.CollegeStyle ? 0.945f : 0.945f;
                float p = basep + (u.K.Kick - 75f) * 0.0016f;
                o.Good = _rng.Chance(Clamp(p, 0.84f, 0.995f));
                o.R = PlayResult.Extra(o.Good);
                o.R.KickerId = u.K.P.Id;
            }
            ApplyOutcome(o);
        }

        // ============================================================ the scrimmage decision
        private float FgMakeProb(SimPlayer k, int dist)
        {
            float kv = 0.55f * k.R.KickAccuracy + 0.45f * k.R.KickPower;
            float d50 = 49f + (kv - 50f) * 0.30f;
            float p = 1f / (1f + (float)Math.Exp((dist - d50) / 6.2f));
            if (dist <= 32 && p < 0.93f) p = 0.93f + (kv - 70f) * 0.0006f;
            return Clamp(p, 0.02f, 0.995f);
        }

        private bool ShouldKneel(SimUnit off, SimUnit def)
        {
            if (_gs.Quarter != 4 || _gs.IsOvertime) return false;
            if (_gs.PossessionScoreDiff <= 0) return false;
            if (_gs.BallSpot < 4) return false;
            int kneels = 4 - _gs.Down + 1;
            int oppTo = _gs.TimeoutsOf(_gs.Possession.Opponent());
            float burn = Math.Max(0, kneels - oppTo) * 40f;
            if (_gs.Down == 4) burn = Math.Max(0f, (1 - oppTo) * 40f);
            return _gs.ClockSeconds <= burn - 1f;
        }

        private bool MaybeCallTimeout(SimUnit off, SimUnit def)
        {
            if (!_gs.ClockRunning || _gs.PendingRunoff <= 0f) return false;
            if (_gs.Quarter != 2 && _gs.Quarter != 4) return false;
            if (_gs.IsOvertime) return false;
            float c = _gs.ClockSeconds;
            int d = _gs.PossessionScoreDiff;
            Side offSide = _gs.Possession, defSide = _gs.Defense;
            // defense (trailing) stops the clock after the offense runs it down
            if (d > 0 && _gs.TimeoutsOf(defSide) > 0)
            {
                if (_gs.Quarter == 4 && c <= 215f && d <= 16) return _gs.CallTimeout(defSide);
                if (_gs.Quarter == 2 && c <= 75f && d <= 3) return _gs.CallTimeout(defSide);
            }
            else if (d == 0 && _gs.Quarter == 2 && c <= 50f && _gs.TimeoutsOf(defSide) > 0 && _rng.Chance(0.5f))
            {
                return _gs.CallTimeout(defSide);
            }
            // offense (trailing or driving at the end of a half) saves the clock
            if (_gs.TimeoutsOf(offSide) > 0)
            {
                if (_gs.Quarter == 4 && d < 0 && c <= 130f && d >= -16) return _gs.CallTimeout(offSide);
                if (_gs.Quarter == 4 && d == 0 && c <= 70f) return _gs.CallTimeout(offSide);
                if (_gs.Quarter == 2 && c <= 55f && d <= 7 && _gs.BallSpot >= 25 && _gs.YardsToGoal > 3) return _gs.CallTimeout(offSide);
            }
            return false;
        }

        private void DoScrimmage()
        {
            SimUnit off = Off, def = Def;
            OpenDriveIfNeeded();
            if (MaybeCallTimeout(off, def)) return;
            if (!_gs.IsOvertime || !_gs.IsCollegeOvertime)
            {
                if (_rng.NextFloat() < SimTuning.PenaltyPreSnap * (_gs.Down == 4 && _gs.Distance > 8 ? 0.7f : 1f)) { ApplyOutcome(PreSnapPenalty(off, def)); return; }
            }

            int d = _gs.PossessionScoreDiff;
            float c = _gs.ClockSeconds;
            int q = _gs.Quarter;
            bool clockedPeriod = !_gs.IsCollegeOvertime;
            bool hurry = clockedPeriod && !_gs.IsOvertime && ((q == 2 && c <= 120f && d <= 7) || (q == 4 && d < 0 && c <= (d <= -9 ? 480f : 300f)) || (q == 4 && d == 0 && c <= 60f));
            bool milk = clockedPeriod && q == 4 && d > 0 && c <= 360f;
            bool endHalfFg = clockedPeriod && !_gs.IsOvertime && (q == 2 || q == 4) && c <= 14f;

            Call call;
            bool goForIt = false;
            if (ShouldKneel(off, def)) call = Call.Kneel;
            else if (WantsSpike(off, hurry)) call = Call.Spike;
            else call = ChooseCall(off, def, hurry, milk, endHalfFg, out goForIt);

            PlayOutcome o;
            switch (call)
            {
                case Call.Kneel:
                    o = new PlayOutcome { K = OutKind.Kneel, Rusher = off.QB, Passer = off.QB, R = PlayResult.KneelDown(2f) };
                    o.R.RunoffSeconds = 40f;
                    break;
                case Call.Spike:
                    o = new PlayOutcome { K = OutKind.Spike, Passer = off.QB, R = PlayResult.SpikeBall(1f) };
                    break;
                case Call.Punt: o = DoPunt(off, def); break;
                case Call.FieldGoal: o = DoFieldGoal(off, def); break;
                case Call.Pass: o = DoPass(off, def, hurry, milk); break;
                default: o = DoRun(off, def, hurry, milk); break;
            }
            if ((call == Call.Pass || call == Call.Run) && _rng.NextFloat() < SimTuning.PenaltyInPlay) AttachPenalty(o, off, def);
            ApplyOutcome(o);
        }

        private bool WantsSpike(SimUnit off, bool hurry)
        {
            if (!hurry) return false;
            if (!_gs.ClockRunning || _gs.PendingRunoff <= 0f) return false;
            if (_gs.TimeoutsOf(_gs.Possession) > 0) return false;
            if (_gs.Down >= 4) return false;
            float c = _gs.ClockSeconds;
            if (c > 40f || c < 3f) return false;
            if (_gs.Quarter == 4 && _gs.PossessionScoreDiff > 0) return false;
            return _gs.YardsToGoal <= 55;
        }

        private Call ChooseCall(SimUnit off, SimUnit def, bool hurry, bool milk, bool endHalfFg, out bool goForIt)
        {
            goForIt = false;
            int ytg = _gs.YardsToGoal;
            int fgDist = ytg + 17;
            float fgP = FgMakeProb(off.K, fgDist);
            bool fgRange = fgP >= 0.40f && fgDist <= 66;
            int d = _gs.PossessionScoreDiff;
            float c = _gs.ClockSeconds;
            int q = _gs.Quarter;

            if (endHalfFg && fgRange)
            {
                if (q == 2 || (q == 4 && d >= -3 && d <= 0)) return Call.FieldGoal;
            }
            if (_gs.IsCollegeOvertime)
            {
                if (_gs.Down == 4) return fgRange && !(ytg > 40 && d < 0) ? Call.FieldGoal : GoFor(off, def, hurry, milk);
                if (_gs.Down == 3 && fgRange && fgP > 0.8f && _rng.Chance(0.1f)) return Call.FieldGoal;
                return PassOrRun(off, def, hurry, milk);
            }

            if (_gs.Down == 4)
            {
                int dist = _gs.Distance;
                double a = off.Aggression / 100.0;
                bool desperate = q == 4 && d < 0 && (c <= 150f || (c <= 300f && d <= -9));
                if (_gs.IsOvertime && d == 0 && !fgRange) desperate = false;
                if (desperate)
                {
                    bool needsTd = d < -3;
                    if (!needsTd && fgRange && c > 8f) return Call.FieldGoal;
                    if (needsTd || !fgRange) return GoFor(off, def, true, false);
                    return Call.FieldGoal;
                }
                // pro overtime / late tied game: take the points
                if (fgRange && ((q >= 4 && d >= -3 && d <= 0 && c <= 120f))) return Call.FieldGoal;
                double go = 0.0;
                if (_gs.BallSpot >= 50)
                {
                    if (dist <= 1) go = 0.40 + 0.45 * a;
                    else if (dist == 2) go = 0.15 + 0.45 * a;
                    else if (dist <= 4) go = 0.02 + 0.35 * a;
                    else if (dist <= 6) go = 0.02 + 0.12 * a;
                    else go = 0.0;
                    if (fgRange && fgP > 0.78f && ytg > 3) go *= 0.35;
                    if (ytg <= 2 && dist <= 1) go = Math.Max(go, 0.5 + 0.3 * a);
                    if (d <= -9 && q >= 3) go += 0.2;
                }
                else if (_gs.BallSpot >= 35)
                {
                    if (dist <= 1) go = 0.10 + 0.40 * a;
                    else if (dist == 2) go = 0.03 + 0.15 * a;
                }
                else if (dist <= 1 && a > 0.75) go = 0.12;
                if (_rng.NextDouble() < go) return GoFor(off, def, hurry, milk);
                if (fgRange && !(ytg > 45 && fgP < 0.5f)) return Call.FieldGoal;
                return Call.Punt;
            }
            return PassOrRun(off, def, hurry, milk);
        }

        private Call GoFor(SimUnit off, SimUnit def, bool hurry, bool milk)
        {
            return PassOrRun(off, def, hurry, milk);
        }

        private Call PassOrRun(SimUnit off, SimUnit def, bool hurry, bool milk)
        {
            float p = 0.585f + (off.PassTendency - 50) * 0.004f;
            int down = _gs.Down, dist = _gs.Distance, ytg = _gs.YardsToGoal;
            float qbEdge = (off.QB.R.ThrowAccuracy + off.QB.R.ThrowPower) * 0.5f - 72f;
            float rbEdge = off.RB1.RbRun - 72f;
            p += (qbEdge - rbEdge) * 0.0012f;
            if (down == 1) p -= 0.065f;
            else if (down == 2) p += dist >= 8 ? 0.15f : (dist <= 3 ? -0.2f : 0.0f);
            else if (down == 3) p = dist >= 8 ? 0.9f : (dist >= 4 ? 0.8f : (dist >= 2 ? 0.52f : 0.36f));
            else if (down == 4) p = dist >= 5 ? 0.88f : (dist >= 3 ? 0.62f : 0.38f);
            if (ytg <= 4 && dist <= ytg) p = Math.Min(p, down >= 3 ? 0.55f : 0.4f);
            else if (ytg <= 10) p -= 0.04f;
            int d = _gs.PossessionScoreDiff;
            if (_gs.Quarter >= 3 && d <= -8) p += 0.1f;
            if (_gs.Quarter >= 3 && d >= 10) p -= 0.1f;
            if (hurry) p = Math.Max(p, 0.86f);
            if (milk) p = Math.Min(p, down == 3 && dist >= 7 ? 0.7f : 0.22f);
            if (_gs.IsCollegeOvertime && ytg > 25) p += 0.0f;
            p = Clamp(p, 0.08f, 0.96f);
            return _rng.NextFloat() < p ? Call.Pass : Call.Run;
        }

        // ============================================================ special teams plays
        private PlayOutcome DoPunt(SimUnit off, SimUnit def)
        {
            var o = new PlayOutcome { K = OutKind.Punt, Kicker = off.P };
            float power = off.P.R.KickPower, acc = off.P.R.KickAccuracy;
            int dist = (int)Math.Round(_rng.Gaussian(44f + (power - 75f) * 0.22f, 5.5f));
            dist = Clamp(dist, 22, 76);
            int spot = _gs.BallSpot;
            if (spot >= 55)
            {
                // directional / pin the opponent
                int maxIn = 100 - spot - 1 - (int)Math.Round(Exp(4f * (1.4f - acc / 100f)));
                if (maxIn < 14) maxIn = 14;
                if (dist > maxIn) dist = maxIn;
            }
            int land = spot + dist;
            if (land >= 100)
            {
                o.R = PlayResult.Punt(dist, 0, false, true);
                o.R.KickerId = off.P.P.Id;
                return TimeAndRunoff(o, 8f, false, false);
            }
            bool oob = _rng.Chance(0.07f);
            bool fair = !oob && _rng.Chance(0.36f);
            SimPlayer returner = def.KR;
            int ret = 0;
            bool td = false;
            if (!oob && !fair)
            {
                ret = (int)Math.Round(Exp(8.8f) * (0.75f + (returner.Yac - 55f) * 0.012f));
                ret = Clamp(ret, 0, 70);
                td = _rng.Chance(0.006f);
                if (td) ret = land;
            }
            o.Returner = returner; o.RetYards = ret; o.Td = td;
            o.R = PlayResult.Punt(dist, ret, td, false, oob, fair);
            o.R.KickerId = off.P.P.Id; o.R.ReturnerId = returner.P.Id;
            o.Dist = dist;
            return TimeAndRunoff(o, 8f, false, false);
        }

        private PlayOutcome DoFieldGoal(SimUnit off, SimUnit def)
        {
            var o = new PlayOutcome { K = OutKind.Fg, Kicker = off.K };
            int dist = _gs.YardsToGoal + 17;
            bool good = _rng.NextFloat() < FgMakeProb(off.K, dist);
            // a pinch of block risk is folded into the miss rate
            o.Good = good; o.Dist = dist;
            o.R = PlayResult.FieldGoal(good, dist, 5f);
            o.R.KickerId = off.K.P.Id;
            return o;
        }

        private PlayOutcome TimeAndRunoff(PlayOutcome o, float time, bool hurry, bool milk)
        {
            o.R.TimeElapsed = time;
            return o;
        }

        // ============================================================ penalties
        private PlayOutcome PreSnapPenalty(SimUnit off, SimUnit def)
        {
            float r = _rng.NextFloat();
            PenaltyCall call;
            if (r < 0.56f)
            {
                float t = _rng.NextFloat();
                PenaltyType type = t < 0.70f ? PenaltyType.FalseStart : (t < 0.90f ? PenaltyType.DelayOfGame : PenaltyType.IllegalFormation);
                call = PenaltyCall.For(type, _gs, 0f, off.OL[_rng.Range(0, 5)].P.Id);
                if (_gs.IsOvertime && _gs.IsCollegeOvertime && type == PenaltyType.DelayOfGame) call = PenaltyCall.For(PenaltyType.FalseStart, _gs, 0f);
            }
            else
            {
                float t = _rng.NextFloat();
                if (t < 0.68f) call = PenaltyCall.For(PenaltyType.Offside, _gs, 0f, def.DL[_rng.Range(0, 4)].P.Id);
                else if (t < 0.88f) call = PenaltyCall.For(PenaltyType.Encroachment, _gs, 0f, def.DL[_rng.Range(0, 4)].P.Id);
                else call = new PenaltyCall(PenaltyType.TooManyMen, _rng.Chance(0.5f) ? _gs.Possession : _gs.Defense);
            }
            var o = new PlayOutcome { K = OutKind.Penalty, R = PlayResult.PenaltyOnly(call, 0f) };
            return o;
        }

        private void AttachPenalty(PlayOutcome o, SimUnit off, SimUnit def)
        {
            float r = _rng.NextFloat();
            PenaltyCall call = null;
            float pressureMod = o.Pressure ? 1f : 0.5f;
            if (o.R.Type == PlayType.Run)
            {
                if (r < 0.50f) call = PenaltyCall.For(PenaltyType.OffensiveHolding, _gs, o.Yards > 0 ? _rng.Range(0, o.Yards + 1) : -1f, off.OL[_rng.Range(0, 5)].P.Id);
                else if (r < 0.64f) call = PenaltyCall.For(PenaltyType.DefensiveHolding, _gs, 0f, def.DL[_rng.Range(0, 4)].P.Id);
                else if (r < 0.80f) call = PenaltyCall.For(PenaltyType.IllegalBlockInBack, _gs, Math.Max(1f, o.Yards * 0.6f), off.OL[_rng.Range(0, 5)].P.Id);
                else if (r < 0.90f) call = new PenaltyCall(PenaltyType.FaceMask, _rng.Chance(0.8f) ? _gs.Defense : _gs.Possession, o.Yards, def.LB[_rng.Range(0, 3)].P.Id);
                else call = new PenaltyCall(PenaltyType.UnnecessaryRoughness, _rng.Chance(0.5f) ? _gs.Defense : _gs.Possession, o.Yards, 0);
            }
            else
            {
                float air = Math.Max(1f, o.Air);
                if (r < 0.30f) call = PenaltyCall.For(PenaltyType.OffensiveHolding, _gs, -3f, off.OL[_rng.Range(0, 5)].P.Id);
                else if (r < 0.37f) call = PenaltyCall.For(PenaltyType.OffensivePassInterference, _gs, air, o.Target != null ? o.Target.P.Id : 0);
                else if (r < 0.50f) call = PenaltyCall.For(PenaltyType.DefensiveHolding, _gs, 0f, def.CB[_rng.Range(0, 3)].P.Id);
                else if (r < 0.77f) call = PenaltyCall.For(PenaltyType.DefensivePassInterference, _gs, air, def.CB[_rng.Range(0, 3)].P.Id);
                else if (r < 0.85f && o.Pressure) call = PenaltyCall.For(PenaltyType.RoughingThePasser, _gs, 0f, def.DL[_rng.Range(0, 4)].P.Id);
                else if (r < 0.92f) call = PenaltyCall.For(PenaltyType.IllegalContact, _gs, 0f, def.CB[_rng.Range(0, 3)].P.Id);
                else if (r < 0.96f) call = new PenaltyCall(PenaltyType.FaceMask, _rng.Chance(0.85f) ? _gs.Defense : _gs.Possession, 0f, 0);
                else call = PenaltyCall.For(PenaltyType.OffensiveHolding, _gs, -3f, off.OL[_rng.Range(0, 5)].P.Id);
            }
            if (call != null) o.R.WithPenalty(call);
        }
    }
}
