using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Sim
{
    /// <summary>
    /// Defensive intelligence: alignment-matched man coverage (press, trail, hip-turn delay), zone drops with pattern matching,
    /// pass-rush lanes and contain, spy, blitz paths, run fits, intercept-solver pursuit and ball reaction.
    /// </summary>
    internal sealed partial class DefenseAI
    {
        private readonly PlaySimulation S;
        private readonly CarrierAI _carrierAI;
        private bool _playIsRun;
        private bool _playIsPlayAction;
        private bool _playIsDraw;
        private SimAgent _intendedRunner, _fakeRunner;
        private float _looseSeenAt = -1f;

        public DefenseAI(PlaySimulation s) { S = s; _carrierAI = new CarrierAI(s); }

        // =====================================================================================
        // Preparation
        // =====================================================================================

        public static float GapOffset(RunGap g)
        {
            switch (g)
            {
                case RunGap.ALeft: return -0.75f;
                case RunGap.ARight: return 0.75f;
                case RunGap.BLeft: return -2.25f;
                case RunGap.BRight: return 2.25f;
                case RunGap.CLeft: return -3.75f;
                case RunGap.CRight: return 3.75f;
                case RunGap.DLeft: return -5.5f;
                case RunGap.DRight: return 5.5f;
                default: return 0f;
            }
        }

        public void Prepare()
        {
            for (int i = 11; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                a.CoverTarget = -1;
                var ca = a.Assign as CoverageAssignment;
                if (ca != null && !ca.IsMan && ca.Zone != null)
                {
                    a.Zone = ca.Zone;
                    a.Landmark = new Vec2(ZoneX(ca.Zone), S.LosY + ca.Zone.CenterY);
                    a.Landmark = new Vec2(SimMath.Clamp(a.Landmark.X, 2f, FieldGeometry.Width - 2f), a.Landmark.Y);   // v0.9.0 (E-201): the deep zones are anchored to the field, not the hash
                }
            }
            if (S.Kind != PlayKind.Scrimmage) return;
            _playIsRun = S.OffPlay.IsRun;
            var qdraw = S.OffPlay.Assignments[0] as QbAssignment;
            _qbDraw = qdraw != null && qdraw.Action == QbAction.Keep && qdraw.DropbackSteps > 0;
            _playIsDraw = S.OffPlay.Category == PlayCategory.Draw || _qbDraw;
            _exchangeAt = _qbDraw ? 1.05f : -1f;
            _playIsPlayAction = S.OffPlay.Category == PlayCategory.PlayAction;
            MatchMen();
            _intendedRunner = null; _fakeRunner = null;
            for (int i = 1; i < 11; i++) { var ca = S.Agents[i].Assign as CarryAssignment; if (ca != null && !ca.IsFake) { _intendedRunner = S.Agents[i]; break; } }
            for (int i = 1; i < 11; i++) { var ca = S.Agents[i].Assign as CarryAssignment; if (ca != null && ca.IsFake) { _fakeRunner = S.Agents[i]; break; } }
        }

        /// <summary>
        /// v0.9.0 (E-201): lateral anchor of a zone. The deep thirds, halves and quarters divide the FIELD (width 53.3 yd from the middle), so with the ball on a hash the boundary third no longer loses
        /// 3 yd and the field third no longer reaches past the sideline; the hook, curl and flat zones stay relative to the ball.
        /// </summary>
        internal float ZoneX(ZoneRegion z)
        {
            return IsDeep(z) ? FieldGeometry.CenterX + z.CenterX : S.BallX0 + z.CenterX;
        }

        private struct MatchPair { public SimAgent D, R; public float Cost; }

        private void MatchMen()
        {
            var men = new List<SimAgent>();
            for (int i = 11; i < 22; i++)
            {
                var ca = S.Agents[i].Assign as CoverageAssignment;
                if (ca != null && ca.IsMan) men.Add(S.Agents[i]);
            }
            if (men.Count == 0) return;
            var rcvs = new List<SimAgent>();
            for (int i = 1; i < 11; i++) if (S.Agents[i].IsReceiverEligible) rcvs.Add(S.Agents[i]);
            var pairs = new List<MatchPair>();
            foreach (SimAgent d in men)
            {
                var ca = (CoverageAssignment)d.Assign;
                foreach (SimAgent r in rcvs)
                {
                    float cost = Math.Abs(d.StartPos.X - r.StartPos.X) + 0.25f * Math.Abs(d.StartPos.Y - r.StartPos.Y);
                    // slot hint breaks ties; position type keeps linebackers on backs and corners on receivers
                    if (ca.TargetSlot == r.Slot) cost -= 0.9f;
                    bool back = r.PlayPos == Position.RB || r.PlayPos == Position.FB || r.PlayPos == Position.TE;
                    if (PositionGroup.IsLinebacker(d.PlayPos)) cost += back ? 0f : 2.5f;
                    else if (PositionGroup.IsSafety(d.PlayPos)) cost += back ? 0.5f : 1.8f;   // a safety takes a tight end or back before a wide receiver
                    else if (d.PlayPos == Position.CB) cost += r.PlayPos == Position.WR ? 0f : 2.0f;
                    pairs.Add(new MatchPair { D = d, R = r, Cost = cost });
                }
            }
            pairs.SortStable((p, q) => p.Cost.CompareTo(q.Cost));
            var usedD = new HashSet<int>(); var usedR = new HashSet<int>();
            foreach (MatchPair p in pairs)
            {
                if (usedD.Contains(p.D.Id) || usedR.Contains(p.R.Id)) continue;
                usedD.Add(p.D.Id); usedR.Add(p.R.Id);
                p.D.CoverTarget = p.R.Id;
            }
            // unmatched men play a short hook zone
            foreach (SimAgent d in men)
            {
                if (d.CoverTarget >= 0) continue;
                d.Zone = ZoneRegion.HookMiddle();
                d.Landmark = S.Rel(new Vec2(0f, 8f));
                d.IsFree = true; d.FreeSince = 0f;   // v0.10.0: a man defender with no man is a free defender (rat / Cover 0 rusher), not a statue at the hook
            }
        }

        public void OnSnap()
        {
            S.Blocks.OnSnap();
            // press jams are resolved when contact is made (see ManUpdate)
            for (int i = 11; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                a.DefReadAt = ReadDelay(a);
            }
        }

        private float ReadDelay(SimAgent a)
        {
            float aw = SimMath.Rating01(a.R.Awareness);
            float d = 0.20f + (1f - aw) * 0.32f;
            if (PositionGroup.IsLinebacker(a.PlayPos)) d += 0.20f;     // a linebacker reads run or pass off the guards (0.40-0.72 s)
            if (PositionGroup.IsDefensiveBack(a.PlayPos)) d += 0.30f;  // a back diagnoses the run through the line, not off the snap (0.50-0.82 s)
            // a man defender keys his receiver first: he needs another quarter second to see the run (he used to charge the line at the snap by accident)
            { var mc = a.Assign as CoverageAssignment; if (mc != null && mc.IsMan) d += 0.28f; }
            // misdirection holds the second level for a beat: the counter step, the pulling line, the fake
            if (a.Side == SimSide.Defense && (!PositionGroup.IsDefensiveLine(a.PlayPos) || MisdirectionDelay >= 0.4f)) d += MisdirectionDelay * (0.7f + 0.6f * (1f - aw));
            return d * 1.12f;   // v0.9.0 (E-318 / E-365): the second level reads the run a little later (+12 percent)
        }

        private float MisdirectionDelay
        {
            get
            {
                for (int i = 1; i < 11; i++)
                {
                    var ca = S.Agents[i].Assign as CarryAssignment;
                    if (ca == null || ca.IsFake) continue;
                    if (ca.Style == CarryStyle.Counter) return 0.52f;
                    if (ca.Style == CarryStyle.Trap) return 0.22f;
                    if (ca.Style == CarryStyle.Option) return 0.12f;
                    if (ca.Style == CarryStyle.Sweep && S.OffAI != null && S.OffAI.QbPlanAction == QbAction.Toss && S.Agents[i].PlayPos == Position.WR) return 0.6f;   // v0.9.0: the jet motion holds the defense for a beat (the backside end does not chase the motion man across the backfield)
                }
                return 0f;
            }
        }

        // =====================================================================================
        // Per-tick update
        // =====================================================================================

        public void Update()
        {
            if (S.Kind != PlayKind.Scrimmage) return;
            bool turnover = S.BallSide != SimSide.Offense;
            if (_exchangeAt < 0f && S.Blocks.HandedOff) _exchangeAt = S.Time;
            for (int i = 11; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.ControlledByUser) continue;
                if (a.State == AgentState.Down || a.Body.FallenTime > 0f) continue;
                if (a.State == AgentState.Engaged) continue;
                if (a.State == AgentState.Tackling) continue;
                if (turnover)
                {
                    if (a.HasBall) _carrierAI.Update(a);
                    else S.Blocks.UpdateEscort(a);
                    continue;
                }
                UpdateDefender(a);
            }
        }

        private bool PerceivesRun(SimAgent a)
        {
            float t = S.Time;
            if (t < a.DefReadAt) return false;
            SimAgent c = S.Carrier;
            bool carrierRuns = c != null && c.Side == SimSide.Offense && (c.Slot != 0 || S.QbHasCrossedLos || ScrambleCommitted(c) || QbDrawRunning(c));
            if (c != null && c.Slot == 0 && c.Side == SimSide.Offense && c.Pos.Y > S.LosY + 0.5f) carrierRuns = true;
            if (_playIsPlayAction)
            {
                // linebackers and safeties bite on the fake until they see the QB keep dropping
                if (PositionGroup.IsDefensiveBack(a.PlayPos) && a.PlayPos == Position.CB) return carrierRuns;
                float aw = SimMath.Rating01(a.R.Awareness);
                float bite = 0.5f + (1f - aw) * 0.45f;
                if (t < bite) return true;
                return carrierRuns;
            }
            if (_playIsDraw)
            {
                float aw = SimMath.Rating01(a.R.Awareness);
                if (t < 0.55f + (1f - aw) * 0.4f) return false;
                return carrierRuns || t > 1.0f && carrierRuns;
            }
            if (_playIsRun)
            {
                // v0.9.0 (E-333): a run is read, not known. The linebackers and backs key the offensive line (run steps, pulls) and the mesh; a defender who misreads (8-15 percent, less for the aware)
                // plays pass until the ball is handed off or he has taken another third of a second to see the blocks
                float awr = SimMath.Rating01(a.R.Awareness);
                bool misread = Hash01(a.Id, 17) < 0.08f + 0.07f * (1f - awr);
                if (misread && !S.Blocks.HandedOff && t < a.DefReadAt + 0.35f) return false;
                return true;
            }
            return carrierRuns;
        }

        /// <summary>A stable pseudo-random number in [0,1) from an id and a salt (no draw from the simulation random stream).</summary>
        internal static float Hash01(int id, int salt)
        {
            unchecked { uint h = (uint)(id * 73856093) ^ (uint)(salt * 19349663); h ^= h >> 13; h *= 1274126177u; h ^= h >> 16; return (h & 0xFFFFFF) / 16777216f; }
        }

        private void UpdateDefender(SimAgent a)
        {
            BallSim ball = S.Ball;
            // 1. a pass in the air: the defenders responsible for it break on the ball
            if (S.PassThrown && !S.PassCompleted && (ball.State == BallState.Pass || (ball.State == BallState.Loose && S.PassTipped)))
            {
                if (BallReaction(a)) return;
            }
            // 2. a live ball on the ground (fumble, bad snap, blocked kick)
            if (ball.State == BallState.Loose && !S.PassTipped && (S.Fumbled || S.FumbleCount > 0))
            {
                // the five nearest players go for the ball at once; everyone else sees it a beat later
                if (_looseSeenAt < 0f) _looseSeenAt = S.Time;
                int closer = 0; float da = (ball.Pos.XY - a.Pos).LenSq();
                for (int j = 11; j < 22; j++) if (j != a.Id && (ball.Pos.XY - S.Agents[j].Pos).LenSq() < da) closer++;
                if (closer < 5 || S.Time - _looseSeenAt > 0.4f) { PursueBall(a); return; }
            }

            SimAgent c = S.Carrier;
            if (c != null && c.Side == SimSide.Offense)
            {
                bool running = c.Slot != 0 || S.QbHasCrossedLos || ScrambleCommitted(c) || QbDrawRunning(c);
                if (running)
                {
                    // the ball is out and moving: everyone fits the run once he has read it
                    if (S.PassCompleted || (S.Time >= a.DefReadAt && !DrawHold(a))) { RunResponse(a, c); return; }
                }
                else if (!S.PassThrown && PerceivesRun(a))
                {
                    // a called run (or a play-action fake) before the exchange: key the back, not the quarterback
                    RunResponse(a, FocusOn(a, c));
                    return;
                }
            }

            // 3. the assignment
            // v0.9.0: the jet motion holds the line for a beat: a rusher reads before he commits (he does not run through the quarterback's spot while the motion man crosses behind it)
            if (a.Assign is RushAssignment && _playIsRun && S.Time < a.DefReadAt && MisdirectionDelay >= 0.4f) { a.Intent = AgentAction.Idle; a.Want = Vec2.Zero; a.HasFace = true; a.FaceDir = new Vec2(0f, -1f); return; }
            if (a.Assign is RushAssignment) { RushUpdate(a, (RushAssignment)a.Assign); return; }
            if (a.Assign is BlitzAssignment) { BlitzUpdate(a, (BlitzAssignment)a.Assign); return; }
            if (a.Assign is SpyAssignment) { SpyUpdate(a); return; }
            var cov = a.Assign as CoverageAssignment;
            if (cov != null && cov.IsMan && (a.CoverTarget >= 0 || a.IsFree)) { ManUpdate(a, cov); return; }
            ZoneUpdate(a);
        }

        // =====================================================================================
        // Pass rush
        // =====================================================================================

        /// <summary>v0.10.0: the gap a down lineman is responsible for: the one his alignment technique gives him (a 3-technique has the B gap, a 1-technique the A gap), except in a stunt, where the stunt's swapped gap rules.</summary>
        internal static RunGap GapOf(SimAgent a, RushAssignment ra)
        {
            if (ra.Stunt != StuntKind.None || !a.HasTechGap) return ra.Gap;
            return a.TechGap;
        }

        private Vec2 GapPoint(SimAgent a, RunGap gap)
        {
            float x = gap == RunGap.None ? a.StartPos.X : S.BallX0 + GapOffset(gap);
            return new Vec2(x, S.LosY - 0.5f);
        }

        private void RushUpdate(SimAgent a, RushAssignment ra)
        {
            a.Intent = AgentAction.Run;
            float vmax = a.Body.EffectiveVMax * (a.ShedBurst > 0f ? 1.03f : 1f);
            if (a.ShedBurst > 0f) a.ShedBurst -= PlaySimulation.Dt;
            Vec2 qb = S.Qb.Pos;
            // a pass is away: rushers near its flight put a hand up, the rest turn to the ball (screens, dump-offs, tipped balls)
            if (S.PassThrown && S.Ball.State == BallState.Pass && !(a.State == AgentState.Engaged))
            {
                Vec2 bp = S.Ball.Pos.XY;
                float db = (bp - a.Pos).Length;
                if (db < 2.6f && S.Ball.Pos.Z > 0.8f && S.Ball.Pos.Z < S.Catches.ReachTop(a) + 0.5f) S.TryJump(a);
                if (db < 9f || bp.Y < S.LosY + 1.5f)
                {
                    a.Want = (bp - a.Pos).Norm() * vmax * 0.85f;
                    a.HasFace = true; a.FaceDir = (bp - a.Pos).LenSq() > 0.2f ? (bp - a.Pos).Norm() : a.Facing;
                    return;
                }
            }
            // screen recognition: the back or receiver turning around behind the line pulls the rush off the quarterback
            SimAgent scr = S.OffAI.ScreenReceiver;
            if (scr != null && !S.PassThrown && S.Time > 0.55f + (1f - SimMath.Rating01(a.R.Awareness)) * 0.5f && scr.Pos.Y < S.LosY + 1.5f && scr.RouteClock > 0.25f)
            {
                PursueTarget(a, scr);
                return;
            }
            Vec2 aim;
            if (S.Time < 0.22f)
            {
                aim = GapPoint(a, GapOf(a, ra));
                if (ra.Stunt != StuntKind.None && S.Time < 0.35f) aim = new Vec2(S.BallX0 + GapOffset(ra.Gap) * 0.8f, S.LosY - 0.2f);
            }
            else if (ra.Stunt != StuntKind.None && ra.StuntPartnerSlot >= 0 && S.Time < 0.85f && !(S.Carrier != null && S.Carrier != S.Qb && S.Carrier.Side == SimSide.Offense))
            {
                // v0.9.0 (E-336): a stunt has two jobs. The penetrator drives hard into his (swapped) gap for 0.45 s to take the blocker with him; the looper holds for a quarter second,
                // then arcs behind the penetrator into the gap he opened
                SimAgent partner = S.Agents[11 + ra.StuntPartnerSlot];
                bool penetrator = a.Slot < ra.StuntPartnerSlot;
                if (penetrator) aim = S.Time < 0.45f ? new Vec2(S.BallX0 + GapOffset(ra.Gap), S.LosY - 0.3f) : S.Qb.Pos;
                else if (S.Time < 0.35f) aim = new Vec2(a.StartPos.X + Math.Sign(partner.StartPos.X - a.StartPos.X) * 0.8f, S.LosY + 0.3f);
                else aim = new Vec2(partner.StartPos.X + (S.BallX0 - partner.StartPos.X) * 0.25f, S.LosY - 0.6f);
            }
            else
            {
                SimAgent qba = S.Qb;
                Vec2 target = qba.Pos;
                if (S.Carrier != null && S.Carrier != qba && S.Carrier.Side == SimSide.Offense) target = S.Carrier.Pos;
                float side = Math.Sign(a.StartPos.X - S.BallX0);
                if (ra.Contain && side != 0f)
                {
                    // keep the outside shoulder and never run past the quarterback's depth
                    float arc = SimMath.Clamp((a.Pos.Y - target.Y) * 0.35f, 0f, 2.4f);
                    aim = new Vec2(target.X + side * (1.4f + arc * 0.6f), Math.Max(target.Y - 0.5f, S.LosY - 7.5f));
                    if (a.Pos.Y < target.Y + 1.2f) aim = target;
                }
                else
                {
                    float lane = (S.BallX0 + GapOffset(GapOf(a, ra)) - target.X) * 0.25f;
                    aim = new Vec2(target.X + lane, target.Y);
                    if ((a.Pos - target).Length < 3f) aim = target;
                }
            }
            Vec2 to = aim - a.Pos;
            // v0.9.0 (E-337): rushers keep 1.5 yd between them (the pocket no longer pinches onto one point), and inside the last 3 yd the finish is a gather, not a full-speed collision (E-209)
            Vec2 spread = Vec2.Zero;
            for (int j = 11; j < 22; j++)
            {
                SimAgent o = S.Agents[j];
                if (o == a || !(o.Assign is RushAssignment || o.Assign is BlitzAssignment) || o.State == AgentState.Down) continue;
                Vec2 rel = a.Pos - o.Pos; float dd = rel.Length;
                if (dd < 1.5f && dd > 0.05f) spread = spread + rel * ((1.5f - dd) / dd);
            }
            if (to.Length > 2.5f) to = to + spread * 1.3f;
            a.Want = to.Norm() * vmax;
            float dq = (S.Qb.Pos - a.Pos).Length;
            if (dq < 3.0f && S.Qb.HasBall && !S.PassThrown) a.Want = a.Want * SimMath.Lerp(0.78f, 1f, SimMath.Clamp01((dq - 1.2f) / 1.8f));
            TryTackleDive(a);
        }

        private void BlitzUpdate(SimAgent a, BlitzAssignment ba)
        {
            a.Intent = AgentAction.Run;
            float vmax = a.Body.EffectiveVMax;
            float delay = ba.Delay;
            if (a.Fx != null) delay *= Math.Max(0.2f, 1f - 2f * a.Fxv(SimHook.BlitzTiming) / 100f);   // v0.8.0 Sniper Blitz: times the snap and arrives sooner
            if (S.Time < delay)
            {
                // the disguise: creep to the gap he will attack (never stand frozen behind his own line)
                // v0.9.0 (E-358): the disguise: he stands at his depth and shows (a drift of a few tenths toward the gap), he does not edge up to the line before the bring
                Vec2 creep = (new Vec2(S.BallX0 + GapOffset(ba.Gap), S.LosY + 0.9f) - a.StartPos) * 0.30f + a.StartPos - a.Pos;
                a.Want = creep.Norm() * Math.Min(vmax * 0.25f, creep.Length * 2.5f);
                a.HasFace = true; a.FaceDir = new Vec2(0f, -1f);
                return;
            }
            Vec2 qb = S.Qb.Pos;
            SimAgent c = S.Carrier;
            Vec2 target = c != null && c.Side == SimSide.Offense ? c.Pos : qb;
            Vec2 aim;
            float gapX = S.BallX0 + GapOffset(ba.Gap);
            if (S.Time < delay + 0.55f && a.Pos.Y > S.LosY + 0.5f)
            {
                aim = new Vec2(gapX, S.LosY - 0.4f);
            }
            else
            {
                aim = target;
                if (ba.Edge && a.Pos.Y > target.Y + 1f)
                {
                    float side = Math.Sign(gapX - S.BallX0);
                    aim = new Vec2(target.X + side * 1.8f, target.Y);
                }
            }
            a.Want = (aim - a.Pos).Norm() * vmax;
            TryTackleDive(a);
        }

        private void SpyUpdate(SimAgent a)
        {
            a.Intent = AgentAction.Run;
            SimAgent qb = S.Qb;
            float vmax = a.Body.EffectiveVMax;
            bool qbRuns = qb.HasBall && (S.QbHasCrossedLos || qb.QbState == QbPhase.Scramble || qb.Pos.Y > S.LosY - 0.5f && qb.Speed > 2.0f);
            if (qbRuns || S.Carrier != null && S.Carrier != qb && !DrawHold(a))   // v0.10.0: the spy bites on the dropback of a draw like everybody else
            {
                PursueTarget(a, S.Carrier != null ? S.Carrier : qb);
                return;
            }
            Vec2 hold = new Vec2(qb.Pos.X, S.LosY + 5.0f);
            Vec2 to = hold - a.Pos;
            a.Want = to.Norm() * Math.Min(vmax * 0.8f, to.Length * 4f);
            a.HasFace = true; a.FaceDir = (qb.Pos - a.Pos).Norm();
        }

        // =====================================================================================
        // Man coverage
        // =====================================================================================

        private void ManUpdate(SimAgent a, CoverageAssignment cov)
        {
            if (a.IsFree || a.CoverTarget < 0) { FreeDefenderUpdate(a, cov); return; }
            SimAgent r = S.Agents[a.CoverTarget];
            // v0.10.0: his man stays in to block: the defender is FREE (see FreeDefenderUpdate). He is released by a rule, not on the first tick: the man must really be blocking (assignment to block, not a route),
            // and the defender has stayed on him for 0.9 s (a back checks for the rush before he releases) or he is already locked up with a rusher
            if (S.OffAI.IsPassPlay && r.Side == SimSide.Offense)
            {
                var rb = r.Assign as BlockAssignment;
                bool blocking = (rb != null && rb.Block != BlockKind.Chip) || (r.Assign is CarryAssignment && r.Intent == AgentAction.Block && S.Time > 0.6f);   // a chip releases into a route: he is still a receiver
                bool tiedUp = r.State == AgentState.Engaged && !(r.Assign is RouteAssignment) && !(rb != null && rb.Block == BlockKind.Chip);
                if (blocking || tiedUp)
                {
                    if (S.Time >= (blocking ? 0.9f : 0.5f))
                    {
                        a.OriginalCoverTarget = a.CoverTarget;
                        a.IsFree = true; a.FreeSince = S.Time; a.CoverTarget = -1; a.RuleReleases++;
                        FreeDefenderUpdate(a, cov);
                    }
                    else HoldOnBlocker(a, r);
                    return;
                }
            }
            a.Intent = AgentAction.Run;
            float vmax = a.Body.EffectiveVMax;
            float aw = SimMath.Rating01(a.R.Awareness);
            float man = SimMath.Rating01(a.R.ManCoverage);
            float react = 0.12f + (1f - aw) * 0.12f + (1f - man) * 0.08f;   // the defender mirrors the stem he saw, not the break that has not happened yet
            float insideSign = r.Pos.X >= S.BallX0 ? -1f : 1f;
            float shadow = a.Fx != null ? a.Fxv(SimHook.ManCoverage) : 0f;   // v0.8.0 Shutdown Shadow (his man-to-man context is already in his mask)
            Vec2 rp = r.PosAgo(react);
            Vec2 rpPrev = r.PosAgo(react + 0.1f);
            Vec2 rv = (rp - rpPrev) * 10f;
            float rsp = rv.Length;
            float t = S.Time;

            // press: jam at the line
            if (cov.Press && t < 1.0f)
            {
                float dist0 = (r.Pos - a.Pos).Length;
                // v0.9.0 (E-199): the jam needs the bodies together (the radii plus a hand), not a roll from 1.9 yd away
                if (!a.PressResolved && dist0 < a.Body.Radius + r.Body.Radius + 0.30f && t > 0.15f) { PressJam(a, r); a.PressResolved = true; }
                if (t < 0.45f && !a.PressResolved)
                {
                    // mirror the release with a one-step shuffle to the receiver's inside shoulder, square, hands up; the jam happens when the receiver comes to him
                    Vec2 aimP = new Vec2(r.Pos.X + insideSign * 0.30f, r.Pos.Y + 0.80f);
                    Vec2 toP = aimP - a.Pos;
                    a.Want = toP.Norm() * Math.Min(vmax * 0.42f, toP.Length * 4f);
                    a.HasFace = true; a.FaceDir = (r.Pos - a.Pos).Norm();
                    return;
                }
            }

            // receiver direction change -> hip-turn delay
            Vec2 rdir = rsp > 2.2f ? rv * (1f / rsp) : new Vec2(0f, 1f);
            if (a.LastTargetDir.LenSq() > 0.25f && rsp > 3.0f)
            {
                float turn = SimMath.AngleBetween(a.LastTargetDir, rdir);
                if (turn > 0.9f && S.Time > a.FrozenUntil + 0.2f)
                {
                    float hip = 0.09f + (1f - man) * 0.20f + (1f - a.Body.Agility01) * 0.08f + (1f - aw) * 0.05f;
                    if (shadow > 0f) hip *= Math.Max(0.4f, 1f - shadow * ShadowHipPerPoint);
                    a.FrozenUntil = S.Time + hip;
                }
            }
            if (rsp > 2.2f) a.LastTargetDir = rdir;
            if (S.Time < a.FrozenUntil)
            {
                a.Want = a.HeldWant * 0.35f;   // v0.9.0 (E-278): the hips turn: he plants and drifts on his old line instead of sprinting on in the old direction
                return;
            }

            float startCushion = Math.Max(0.9f, Math.Abs(a.StartPos.Y - r.StartPos.Y) - 0.3f);
            float cushion;
            if (cov.Press) cushion = 0.9f + SimMath.Clamp01((t - 0.5f) / 1.0f) * 0.6f;
            else
            {
                // v0.9.0 (E-198): the cushion follows the RECEIVER: a vertical threat keeps 0.6 s of his speed in front of him (up to the alignment depth), a receiver who stops or breaks lets him close to 1.5 yd.
                // (It used to collapse from 6+ yd to 1.5 yd between 0.4 s and 1.9 s on a timer, whether the receiver ran a go or a hitch.)
                if (t < 0.4f || a.Cushion <= 0f) a.Cushion = startCushion;
                float vert = Math.Max(0f, rv.Y);
                float wantC = t < 0.4f ? startCushion : SimMath.Clamp(vert * 0.6f, 1.5f, Math.Max(1.5f, startCushion));
                a.Cushion = SimMath.Lerp(a.Cushion, wantC, 0.07f);
                cushion = a.Cushion;
            }
            // The defender plays IN FRONT of the receiver (downfield of him, between him and the end zone) while he is not beaten, holding the
            // cushion and closing it as the receiver commits; once the receiver is past him he trails on his hip. (The old code aimed at a
            // point BEHIND the receiver, so an off corner charged toward the line of scrimmage at the snap.)
            Vec2 rfwd = rsp > 2.2f ? rdir : new Vec2(0f, 1f);
            float ahead = (a.Pos - rp).Dot(rfwd);           // > 0: the defender is in front of the receiver
            Vec2 desired;
            if (ahead > -0.6f) desired = rp + rv * 0.25f + rfwd * cushion;
            else desired = rp + rv * 0.25f - rfwd * 0.9f;   // beaten: trail position
            // inside leverage
            float toMid = FieldGeometry.CenterX - rp.X;
            desired = new Vec2(desired.X + Math.Sign(toMid) * Math.Min(0.5f, Math.Abs(toMid) * 0.05f), desired.Y);
            if (r.Pos.Y < S.LosY + 1.5f && rsp < 2f) desired = rp + new Vec2(0f, 1f) * Math.Max(1.0f, cov.Press ? 1.0f : cushion);

            Vec2 to = desired - a.Pos;
            float dist = to.Length;
            Vec2 want = rv * 0.88f + to * 3.6f;
            float cap = vmax * (1f + shadow * ShadowSpeedPerPoint);
            // face the receiver while backpedaling/shuffling; turn and run when he gets vertical behind the cushion
            bool vertical = rv.Y > 0.55f * r.Body.VMax && dist > 0.5f && desired.Y > a.Pos.Y - 0.5f;
            bool faceReceiver = cushion > 2.0f && !vertical && S.Time < 3.0f;
            if (faceReceiver) { a.HasFace = true; a.FaceDir = (rp - a.Pos).Norm(); }
            if (want.Length > cap) want = want.Norm() * cap;
            a.Want = want;
            a.HeldWant = want;

            // holding / illegal contact when badly beaten before the throw
            if (S.Options.Penalties && !S.PassThrown && S.FoulOpen(SimSide.Defense))
            {
                float sep = (r.Pos - a.Pos).Length;
                bool beaten = sep > 0.4f && sep < 3.5f && rsp > 2.5f && a.Pos.Y < r.Pos.Y - 0.1f;   // SIM-034: the man defender who has been beaten and is trailing his receiver grabs him
                if (beaten && t > 0.9f)
                {
                    float p = 0.0045f * (1.45f - aw) * (1.3f - man);   // v0.9.0: per tick (was 0.010; the beaten-and-trailing state is more common with the slower, later-reacting defenders)
                    if (S.Rng.NextFloat() < p) S.Flag(PenaltyType.DefensiveHolding, a, r.Pos.Y - S.LosY);
                }
                else if (sep < a.Body.Radius + r.Body.Radius + 0.3f && t > 1.1f && t < 3.4f && r.Pos.Y - S.LosY > 6.0f && rsp > 1.5f)   // only on actual body contact
                {
                    float p = 0.0018f * (1.45f - aw) * (1.3f - man);   // v0.9.0: per tick; the closer man coverage (E-198 cushion, E-192) made the old 0.05 25x as common
                    if (S.Rng.NextFloat() < p) S.Flag(PenaltyType.IllegalContact, a, r.Pos.Y - S.LosY);
                }
            }
        }

        /// <summary>v0.8.0 Shutdown Shadow: per point of man-coverage ability, the share of the hip-turn delay removed and of top speed gained.</summary>
        public const float ShadowHipPerPoint = 0.025f, ShadowSpeedPerPoint = 0.002f;

        /// <summary>Yards a zone defender widens his reaction window per point of ZoneRange ability.</summary>
        public const float ZoneRangeYardsPerPoint = 0.22f;

        private void PressJam(SimAgent d, SimAgent r)
        {
            float jam = 0.40f * d.R.Strength + 0.35f * d.R.ManCoverage + 0.25f * d.R.Agility;
            if (d.Fx != null) jam += 0.8f * d.Fxv(SimHook.PressCoverage, PlaySimulation.CtxLineOfScrimmage);   // v0.8.0 Jam Master
            float rel = 0.35f * r.R.Strength + 0.35f * r.R.Agility + 0.30f * r.R.Awareness;
            float p = 1f / (1f + (float)Math.Exp(-(jam - rel) / 10f));
            bool win = S.Rng.NextFloat() < p;
            S.LogEvent(SimEventType.Jam, d.Id, r.Id, TackleType.None, win ? 1f : 0f);
            if (win)
            {
                r.JamTime = 0.30f + 0.35f * S.Rng.NextFloat();
                // v0.9.0 (E-199 / E-328): the jam steers the receiver about 1.2 yd off his line toward the sideline side he came from over its length (a weave with his speed cut to 55 percent),
                // it no longer sets his velocity (Knock + a halved Vel) in one tick
                r.JamPush = r.Pos.X >= d.Pos.X ? 1f : -1f;
            }
            else
            {
                // beaten release: the corner is turned around
                d.FrozenUntil = S.Time + 0.25f + (1f - SimMath.Rating01(d.R.ManCoverage)) * 0.15f;
                d.HeldWant = new Vec2(0f, -1f) * 2f;
            }
        }

        // =====================================================================================
        // Zone coverage
        // =====================================================================================

        private static bool IsDeep(ZoneRegion z)
        {
            return z.Kind == ZoneKind.DeepThird || z.Kind == ZoneKind.DeepHalf || z.Kind == ZoneKind.DeepMiddle || z.Kind == ZoneKind.Quarter;
        }

        private void ZoneUpdate(SimAgent a)
        {
            a.Intent = AgentAction.Run;
            if (a.Zone == null)
            {
                a.Zone = ZoneRegion.HookMiddle();
                a.Landmark = S.Rel(new Vec2(0f, 9f));
            }
            ZoneRegion z = a.Zone;
            float vmax = a.Body.EffectiveVMax;
            Vec2 qb = S.Qb.Pos;
            Vec2 lm = a.Landmark;
            bool deep = IsDeep(z);

            // find the most dangerous receiver working into the zone
            SimAgent tgt = null; float bestScore = float.MinValue;
            float halfW = z.Width * 0.5f + (deep ? 2.5f : 1.0f);   // v0.9.0 (E-192): an underneath zone defender only reacts to a receiver INSIDE his zone (plus a yard)
            float range = a.Fx != null ? Math.Max(0f, a.Fxv(SimHook.ZoneRange)) * ZoneRangeYardsPerPoint : 0f;   // v0.8.0 Sideline To Sideline, Range Rover: a wider reaction radius
            halfW += range;
            float depthLo = deep ? z.CenterY - 4f : z.CenterY - z.Height * 0.5f - 2.5f;
            float depthHi = deep ? 80f : z.CenterY + z.Height * 0.5f + 2.5f;
            for (int i = 1; i < 11; i++)
            {
                SimAgent r = S.Agents[i];
                if (!r.IsReceiverEligible || r.State == AgentState.Down) continue;
                Vec2 rp = r.Pos + r.Vel * 0.25f;
                float relY = rp.Y - S.LosY;
                float relX = rp.X - ZoneX(z);
                if (Math.Abs(relX) > halfW || relY < depthLo || relY > depthHi) continue;
                // do not stick with a receiver who is already covered by a man defender nearby
                float dist = (rp - a.Pos).Length;
                // pattern matching: a receiver a teammate already has in hand is passed off, not doubled by the whole second level
                if (!deep && CoveredByTeammate(a, rp, dist)) continue;
                if (deep && CoveredByDeepTeammate(a, r, rp, dist)) continue;   // v0.9.0 (E-195): a deep defender passes a receiver off to the one who is over the top of him
                float score = -dist + (deep ? relY * 0.3f : 0f) - 0.5f * Math.Abs(relX) + (r.Vel.Y > 2f ? 1.0f : 0f);
                if (r.Id == S.TargetId) score += 3f;
                if (r.Id == a.ZoneTargetId) score += 1.5f;   // hysteresis: stay on the receiver you have unless another is clearly more dangerous
                if (score > bestScore) { bestScore = score; tgt = r; }
            }

            // v0.10.0: quarters pattern-match: the corner and the safety key the receivers who go vertical, wherever they are
            bool matched = false;
            if ((z.Kind == ZoneKind.Quarter || (a.Assign is CoverageAssignment && ((CoverageAssignment)a.Assign).Match)) && S.Time > 0.5f) { SimAgent m = QuartersMatch(a, z); if (m != null) { tgt = m; matched = true; } }
            if (!matched) { SimAgent bm = BracketMatch(a); if (bm != null) { tgt = bm; matched = true; } }

            Vec2 desired;
            a.ZoneTargetId = tgt != null ? tgt.Id : -1;
            float zx = ZoneX(z);
            if (tgt != null && S.Time > 0.5f)
            {
                Vec2 rp = tgt.Pos + tgt.Vel * 0.3f;
                if (deep)
                {
                    float cushion = 2.2f + 0.4f * Math.Max(0f, tgt.Vel.Y);
                    float y = Math.Max(lm.Y, rp.Y + cushion);
                    float x = SimMath.Clamp(rp.X, zx - halfW, zx + halfW);
                    desired = matched ? new Vec2(rp.X, y) : new Vec2(x * 0.7f + rp.X * 0.3f, y);
                }
                else
                {
                    // v0.9.0 (E-192): a zone defender plays the ZONE, not a man: he holds the depth of his landmark, shades toward the receiver laterally and stays 2.8 yd off him underneath,
                    // closing to 1 yd only when the receiver is in the middle of his zone (he used to leave the landmark at 0.25 s and keep a 0.9 yd cushion on whoever came near)
                    Vec2 toQb = (qb - rp).Norm();
                    float dc = (rp - lm).Length;
                    float tight = SimMath.Clamp01((3.4f - dc) / 3.4f);
                    float cushion = SimMath.Lerp(2.8f, 1.0f, tight);
                    desired = rp + toQb * cushion;
                    if (matched) desired = rp + toQb * 2.2f;   // v0.10.0: a bracket / match plays the receiver, not the zone
                    if (!matched) {
                    desired = new Vec2(SimMath.Lerp(lm.X, desired.X, 0.55f + 0.45f * tight), SimMath.Lerp(lm.Y, desired.Y, 0.30f + 0.70f * tight));
                    if (rp.Y > lm.Y + z.Height * 0.5f + 1.5f) desired = new Vec2(desired.X, Math.Max(lm.Y, desired.Y));
                    }
                }
            }
            else
            {
                desired = new Vec2(lm.X + ZoneShade(z, qb), lm.Y);   // v0.10.0: the zone flows with the ball a yard or two (never a still statue at the landmark)
                // v0.9.0 (E-196 / E-197): the deep middle safety cheats toward the most vertical receiver on his side of the field (help over the top), and a quarters safety reads the
                // number two receiver: if he goes vertical within 7 yd he is the safety's man, otherwise the safety helps over the outside receiver
                if (deep && S.Time > 0.7f && (z.Kind == ZoneKind.DeepMiddle || z.Kind == ZoneKind.DeepHalf || z.Kind == ZoneKind.Quarter))
                {
                    SimAgent vr = null; float vbest = 0f;
                    for (int i = 1; i < 11; i++)
                    {
                        SimAgent r = S.Agents[i];
                        if (!r.IsReceiverEligible || r.State == AgentState.Down || r.Vel.Y < 0.6f * r.Body.VMax) continue;
                        float lat = Math.Abs(r.Pos.X - lm.X);
                        if (lat > (z.Kind == ZoneKind.Quarter ? 7f : 12f)) continue;
                        float sc = r.Vel.Y - 0.15f * lat;
                        if (sc > vbest) { vbest = sc; vr = r; }
                    }
                    if (vr != null) desired = new Vec2(lm.X + SimMath.Clamp((vr.Pos.X - lm.X) * 0.45f, -4f, 4f), Math.Max(lm.Y, vr.Pos.Y + 2.5f));
                }
            }

            Vec2 to = desired - a.Pos;
            float dist2 = to.Length;
            float speed = Math.Min(vmax * 0.95f, Math.Max(1.2f, dist2 * 4.5f));
            // v0.9.0 (E-193): the drop has phases: a pedal at 0.55 of top speed facing the quarterback for the first 0.35 s after he reads the snap, then the run to the landmark, braking over the
            // last yards (5 yd/s2) so he settles into the zone instead of running at full tilt and stopping dead at 0.4 yd
            bool pedal = S.Time < a.ReactUntil + 0.35f && S.Time < 1.0f && a.Assign is CoverageAssignment && ((CoverageAssignment)a.Assign).Zone != null;
            if (pedal) speed = Math.Min(speed, vmax * 0.55f);
            speed = Math.Min(speed, (float)Math.Sqrt(2f * 5.5f * Math.Max(0f, dist2 - 0.12f)) + 0.5f);
            if (dist2 < 0.18f) speed = 0f;
            Vec2 dir = to.Norm();
            a.Want = dir * speed;
            // eyes on the quarterback while dropping; turn and run when the landmark is deep (a deep safety turns his hips at 0.5 s)
            bool backpedal = (a.Pos.Y < lm.Y - 0.5f || dist2 < 6f) && !(deep && dist2 > 7f);
            if (deep && S.Time < a.ReactUntil + 0.5f)
            {
                a.HasFace = true;
                a.FaceDir = (qb - a.Pos).LenSq() > 0.5f ? (qb - a.Pos).Norm() : new Vec2(0f, -1f);
            }
            else if (backpedal && tgt == null || tgt != null && !deep)
            {
                a.HasFace = true;
                a.FaceDir = (qb - a.Pos).LenSq() > 0.5f ? (qb - a.Pos).Norm() : new Vec2(0f, -1f);
            }
            else if (deep)
            {
                a.HasFace = dist2 < 4f;
                a.FaceDir = (qb - a.Pos).Norm();
            }
            // QB crosses the line: come up
            SimAgent c = S.Carrier;
            if (c != null && c.Side == SimSide.Offense && c.Slot == 0 && S.QbHasCrossedLos) { PursueTarget(a, c); return; }
        }

        // =====================================================================================
        // Ball reaction
        // =====================================================================================

        public void OnPassThrown()
        {
            Vec3 at; float tt;
            Vec2 T;
            if (BallPhysics.PredictAtHeight(S.Ball.Pos, S.Ball.Vel, 1.6f, out at, out tt)) T = at.XY; else T = at.XY;
            for (int i = 11; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                float aw = SimMath.Rating01(a.R.Awareness);
                float react = 0.26f + (1f - aw) * 0.24f;   // v0.9.0 (E-367): a visual reaction to the ball is 0.25-0.30 s at best (was 0.16 s for an elite defender)
                // trailing man defenders are looking at the receiver, not the quarterback
                bool facesQb = a.Facing.Dot((S.Qb.Pos - a.Pos).Norm()) > 0.3f || (a.Assign is CoverageAssignment && !((CoverageAssignment)a.Assign).IsMan);   // watching the passer, or in zone with eyes on him
                if (!facesQb) react += 0.15f;
                a.BallBreakAt = S.Time + react;
                // only defenders responsible for the area (or his man) break on the ball
                float resp = Responsibility(a, T, S.TargetId);
                bool covering = a.Assign is CoverageAssignment || a.Assign is SpyAssignment;
                a.BreakOnBall = covering && resp >= 0.45f;
                if (covering && resp < 0.45f) a.BallBreakAt = S.Time + react + 0.35f;
            }
        }

        private bool BallReaction(SimAgent a)
        {
            if (S.Time < a.BallBreakAt || !a.BreakOnBall) return false;
            BallSim ball = S.Ball;
            float vA = a.Body.EffectiveVMax;
            // free pass rushers keep after the passer unless they can bat the ball
            bool covering = a.Assign is CoverageAssignment || a.Assign is SpyAssignment;
            Vec2 dBall = ball.Pos.XY - a.Pos;
            if (!covering && dBall.Length > 5.0f)
            {
                // late pass rush finishing the hit? no—rushers just turn to pursue
                return false;
            }
            // choose a point along the flight the defender can reach
            Vec3 p = ball.Pos, v = ball.Vel;
            Vec2 best = ball.Pos.XY; bool found = false; float bestMargin = float.MinValue;
            float reachTop = S.Catches.ReachTop(a) + 0.4f;
            float t = 0f;
            for (int k = 0; k < 60; k++)
            {
                BallPhysics.Advance(ref p, ref v, 0.06f);
                t += 0.06f;
                if (p.Z < 0.2f) break;
                if (p.Z > reachTop) continue;
                float dist = Math.Max(0f, (p.XY - a.Pos).Length - 0.9f);
                float tD = dist / (vA * 0.97f);
                float margin = t - tD;
                if (margin >= -0.05f && !found) { best = p.XY; found = true; bestMargin = margin; break; }
                if (margin > bestMargin) { bestMargin = margin; best = p.XY; }
            }
            // v0.9.0 (E-200): the man defender goes to his RECEIVER'S hands (the inside hip at the catch point), not to the earliest point of the ball flight, and a zone defender breaks on the ball only if he is one
            // of the two nearest to it (the rest hold their lanes), so the whole secondary no longer stacks on one spot
            var covA = a.Assign as CoverageAssignment;
            if (covA != null && covA.IsMan && a.CoverTarget >= 0 && a.CoverTarget == S.TargetId && S.PassThrown)
            {
                Vec3 at0; float tt0;
                SimAgent tr = S.Agents[a.CoverTarget];
                if (BallPhysics.PredictAtHeight(ball.Pos, ball.Vel, 1.6f, out at0, out tt0))
                {
                    Vec2 rp = S.OffAI.PredictPos(tr, tt0);
                    Vec2 inside = (S.Qb.Pos - rp).Norm();
                    best = rp + inside * 0.55f;
                }
            }
            else if (covA != null && !covA.IsMan && S.PassThrown)
            {
                float mine = (best - a.Pos).Length; int closer = 0;
                for (int j = 11; j < 22; j++)
                {
                    SimAgent o = S.Agents[j];
                    if (o == a || !o.BreakOnBall || o.State == AgentState.Down || !(o.Assign is CoverageAssignment) || ((CoverageAssignment)o.Assign).IsMan) continue;
                    if ((best - o.Pos).Length < mine - 0.3f) closer++;
                }
                if (closer >= 2) return false;
            }
            Vec2 to = best - a.Pos;
            float d = to.Length;
            a.Intent = AgentAction.Run;
            if (d < 0.3f) a.Want = Vec2.Zero;
            else a.Want = to.Norm() * vA;
            a.HasFace = true;
            a.FaceDir = (ball.Pos.XY - a.Pos).LenSq() > 0.2f ? (ball.Pos.XY - a.Pos).Norm() : a.Facing;
            // leap for it
            if (ball.Pos.Z > 0f && (ball.Pos.XY - a.Pos).Length < 3.2f)
            {
                float tt;
                Vec3 at;
                if (BallPhysics.PredictAtHeight(ball.Pos, ball.Vel, 2.0f, out at, out tt) && tt < 0.34f && (at.XY - a.Pos).Length < 2.0f && ball.Pos.Z > a.Body.HeightYd * 1.2f)
                    S.TryJump(a);
            }
            return true;
        }

        // =====================================================================================
        // Run defense and pursuit
        // =====================================================================================

        private void RunResponse(SimAgent a, SimAgent carrier)
        {
            a.Intent = AgentAction.Run;
            var cov = a.Assign as CoverageAssignment;
            var ra = a.Assign as RushAssignment;
            // deep zone players hold their depth briefly so the shot over the top is covered
            if (cov != null && !cov.IsMan && cov.Zone != null && IsDeep(cov.Zone) && S.Time < (_playIsRun ? 0.42f : 0.9f) && carrier.Pos.Y < S.LosY + 1f)
            {
                ZoneUpdate(a);
                return;
            }
            // linemen and blitzers attack their gap first
            if ((ra != null || a.Assign is BlitzAssignment) && a.Pos.Y > S.LosY - 0.2f && (carrier.Pos - a.Pos).Length > 3.5f && carrier.Pos.Y > S.LosY - 3f && carrier.Pos.Y < S.LosY + 2.5f)   // v0.10.0: the gap hold ends when the ball is through the line (a lineman did not stand at his gap while a catch went 20 yd)
            {
                RunGap g = ra != null ? GapOf(a, ra) : ((BlitzAssignment)a.Assign).Gap;
                Vec2 gp = GapPoint(a, g);
                // v0.9.0 (E-357): he drives to the gap point and then HOLDS it (squared up, reading the ball), he does not run through it into the backfield; a nose / end with no gap plays two-gap
                float dgp = (gp - a.Pos).Length;
                if (ra != null && dgp < 0.6f) { a.Want = Vec2.Zero; a.HasFace = true; a.FaceDir = (carrier.Pos - a.Pos).LenSq() > 0.3f ? (carrier.Pos - a.Pos).Norm() : new Vec2(0f, -1f); return; }
                a.Want = (gp - a.Pos).Norm() * Math.Min(a.Body.EffectiveVMax, Math.Max(1.5f, dgp * 5f));
                return;
            }
            if (RunFit(a, carrier)) return;
            PursueTarget(a, carrier);
        }

        /// <summary>
        /// v0.9.0 (E-321 / E-335): run fits. Until the ball is through the line, a linebacker fills his gap (a step behind the line, shadowing the ball laterally, never running past the hole), a safety
        /// plays the alley (depth 5, between the ball and his own side) and a corner has force (outside the ball, depth 4); the backside players trail instead of chasing the ball across the field.
        /// Once the carrier crosses the line the fit ends and the defender pursues. Returns true while the fit owns the defender.
        /// </summary>
        private bool RunFit(SimAgent a, SimAgent c)
        {
            if (c == null || c.Side != SimSide.Offense || !(_playIsRun || c.Slot != 0) || S.Kind != PlayKind.Scrimmage) return false;
            bool lb = PositionGroup.IsLinebacker(a.PlayPos), sf = PositionGroup.IsSafety(a.PlayPos), cb = a.PlayPos == Position.CB;
            if (!lb && !sf && !cb) return false;
            if (a.Assign is RushAssignment || a.Assign is BlitzAssignment || a.Assign is SpyAssignment) return false;
            float through = lb ? 1.2f : (sf ? 2.6f : 2.0f);
            if ((c.Pos - a.Pos).Length < 3.4f) return false;   // the ball is on him: he attacks it
            if ((Math.Abs(c.Pos.X - S.BallX0) > 4.5f || Math.Abs(c.Vel.X) > 3.2f) && !(a.PlayPos == Position.CB)) return false;   // a run to the perimeter is pursued, not fitted
            if (c.Pos.Y > S.LosY + through || c.Speed > 0.5f && c.Vel.Y > 0f && c.Pos.Y > S.LosY + through * 0.6f) return false;
            float cx = c.Pos.X;
            float startX = a.StartPos.X;
            float outSide = Math.Sign(startX - S.BallX0);
            Vec2 fit;
            if (lb)
            {
                float x = SimMath.Clamp(startX + (cx - startX) * 0.55f, startX - 3.5f, startX + 3.5f);
                fit = new Vec2(x, S.LosY + 1.5f + 0.4f * Math.Min(1f, Math.Abs(cx - startX) / 4f));
            }
            else if (sf) fit = new Vec2(startX + (cx - startX) * 0.65f, S.LosY + 5.0f);
            else
            {
                // force: outside the ball
                float x = outSide != 0f ? cx + outSide * 1.8f : cx;
                fit = new Vec2(x, S.LosY + 4.0f);
            }
            fit = new Vec2(SimMath.Clamp(fit.X, 1f, FieldGeometry.Width - 1f), fit.Y);
            Vec2 to = fit - a.Pos;
            float vmax = a.Body.EffectiveVMax;
            bool backside = (cx - a.Pos.X) * Math.Sign(cx - S.BallX0 + 0.001f) < 0f && Math.Abs(cx - a.Pos.X) > 4f;
            float cap = vmax * (backside ? 0.78f : 0.92f);
            a.Intent = AgentAction.Run;
            a.Want = to.Norm() * Math.Min(cap, Math.Max(1.2f, to.Length * 3.5f));
            if (to.Length < 0.35f) a.Want = Vec2.Zero;
            a.HasFace = true; a.FaceDir = (c.Pos - a.Pos).LenSq() > 0.3f ? (c.Pos - a.Pos).Norm() : a.Facing;
            return true;
        }

        private void PursueBall(SimAgent a)
        {
            a.Intent = AgentAction.Run;
            Vec2 to = S.Ball.Pos.XY + S.Ball.Vel.XY * 0.2f - a.Pos;
            a.Want = to.Norm() * a.Body.EffectiveVMax;
        }

        /// <summary>Pursuit used by the defense (and by the offense after a turnover).</summary>
        public void PursuitUpdate(SimAgent a)
        {
            SimAgent c = S.Carrier;
            if (c == null)
            {
                if (S.Ball.State == BallState.Loose) { PursueBall(a); return; }
                a.Want = Vec2.Zero;
                return;
            }
            if (c.Side == a.Side)
            {
                S.Blocks.UpdateEscort(a);
                return;
            }
            PursueTarget(a, c);
        }

        public void PursueTarget(SimAgent a, SimAgent c)
        {
            a.Intent = AgentAction.Run;
            float vA = a.Body.EffectiveVMax * 0.95f;
            float aw = SimMath.Rating01(a.R.Awareness);
            float dirY = c.Side == SimSide.Offense ? 1f : -1f;
            // anticipated carrier velocity: current velocity blended toward upfield running
            Vec2 vc = c.Vel;
            Vec2 up = new Vec2(0f, dirY) * (c.Body.EffectiveVMax * 0.8f);
            Vec2 vpred = vc + (up - vc) * 0.25f;
            Vec2 rel = c.Pos - a.Pos;
            // intercept time t with |rel + vpred*t| = vA*t
            float aa = vpred.LenSq() - vA * vA;
            float bb = 2f * rel.Dot(vpred);
            float cc = rel.LenSq();
            float tInt;
            if (Math.Abs(aa) < 1e-4f) tInt = bb < 0f ? -cc / bb : 0f;
            else
            {
                float disc = bb * bb - 4f * aa * cc;
                if (disc < 0f) tInt = 0f;
                else
                {
                    float sq = (float)Math.Sqrt(disc);
                    float t1 = (-bb - sq) / (2f * aa), t2 = (-bb + sq) / (2f * aa);
                    tInt = float.MaxValue;
                    if (t1 > 0f) tInt = Math.Min(tInt, t1);
                    if (t2 > 0f) tInt = Math.Min(tInt, t2);
                    if (tInt == float.MaxValue) tInt = 0f;
                }
            }
            tInt = Math.Min(tInt, 2.2f);
            float lead = 0.30f + 0.65f * aw;
            Vec2 aim = c.Pos + vpred * (tInt * lead);
            // v0.9.0 (E-334): the defender is not heat-seeking. He chases where the carrier WAS 0.2 s ago plus his velocity (decision latency), his aim is off by (1 - awareness) x 0.8 yd, and he needs
            // time to build speed (the intercept used a full-speed body from a standstill)
            {
                // (the decision latency matters at range; inside 4 yd he is looking at the ball carrier, not at where he was)
                float far = SimMath.Clamp01(((c.Pos - a.Pos).Length - 4f) / 6f);
                Vec2 seen = c.PosAgo(0.2f);
                aim = aim + (seen - c.Pos) * (0.5f * far);
                float err = (1f - aw) * 0.8f * far;
                if (err > 0.01f) aim = aim + new Vec2((float)Math.Sin(S.Time * 3.1f + a.Id * 1.7f), (float)Math.Cos(S.Time * 2.3f + a.Id * 2.9f)) * err;
            }
            // contain / force: a defender who is outside the carrier and ahead of him keeps his outside shoulder instead of cutting inside
            {
                float outSide = Math.Sign(c.Pos.X - FieldGeometry.CenterX);
                float relx = a.Pos.X - c.Pos.X;
                bool ahead = c.Side == SimSide.Offense ? a.Pos.Y > c.Pos.Y - 1f : a.Pos.Y < c.Pos.Y + 1f;
                if (outSide != 0f && Math.Abs(relx) > 1.2f && Math.Sign(relx) == outSide && ahead && (c.Pos - a.Pos).Length > 3f) aim = new Vec2(aim.X + outSide * 0.9f, aim.Y);
            }
            // sideline defenders force the carrier inside: keep leverage
            Vec2 to = aim - a.Pos;
            float dist = (c.Pos - a.Pos).Length;
            if (dist < 1.4f) to = c.Pos + c.Vel * 0.1f - a.Pos;
            // pursuit speed: 0.95 of top speed (was 0.985), scaled by how much of it he has built up from a standing start
            float buildUp = 0.90f + 0.10f * SimMath.Clamp01(a.Speed / Math.Max(1f, vA));
            a.Want = to.Norm() * (vA * buildUp);
            // v0.9.0 (E-209): inside 3 yd he breaks down to tackle (a gather: 62 percent of top speed at 1.2 yd) instead of running into the carrier at full speed
            if (dist < 3.0f)
            {
                // a head-on closer (the carrier is running AT him) sets his feet; a defender chasing from behind keeps running (the gather would let a runner walk away from him)
                Vec2 rn = (c.Pos - a.Pos) * (1f / Math.Max(0.05f, dist));
                float head = -c.Vel.Dot(rn);   // > 0: the carrier is coming toward the defender
                if (head > 1.5f) a.Want = a.Want * SimMath.Lerp(0.62f, 1f, SimMath.Clamp01((dist - 1.2f) / 1.8f));
            }
            TryTackleDive(a, c);
        }

        private void TryTackleDive(SimAgent a, SimAgent c = null)
        {
            if (c == null) c = S.Carrier;
            if (c == null || c.Side == a.Side) return;
            if (a.Move != CarrierMove.None || a.MoveCooldown > 0f || a.State != AgentState.Normal) return;
            Vec2 rel = c.Pos - a.Pos;
            float dist = rel.Length;
            if (dist > 2.7f || dist < 1.5f) return;
            if (S.BodyBetween(a, c)) return;   // no diving through a blocker
            float closing = (a.Vel - c.Vel).Dot(rel.Norm());
            // dive when the carrier is about to run away from the angle
            Vec2 cdir = c.Speed > 1.5f ? c.Vel.Norm() : c.Facing;
            float away = cdir.Dot(rel.Norm());
            if (closing > 4.0f && away > -0.8f)   // v0.9.0 (E-243): a dive needs closing speed and a carrier who can cut away from a form tackle (not a head-on runner)
            {
                float aw = SimMath.Rating01(a.R.Awareness);
                float p = 0.010f + 0.012f * (1f - aw);
                if (S.Rng.NextFloat() < p) S.Moves.TryMove(a, CarrierMove.Dive);
            }
        }

        // =====================================================================================
        // User control of defenders
        // =====================================================================================

        public void UserDefenderInput(SimAgent a, ControlInput input)
        {
            if (input.Dive || input.Tackle)
            {
                SimAgent uc = S.Carrier;
                if (uc != null && uc.Side != a.Side)
                {
                    // QA-436: the tackle button is a standing wrap-up when the runner is close; only the dive button (or a tackle from range) leaves his feet
                    if (input.Tackle && !input.Dive && (uc.Pos - a.Pos).Length < 2.0f) a.TackleCooldown = 0f;
                    else S.Moves.TryMove(a, CarrierMove.Dive);
                }
            }
            if (input.Jump) S.TryJump(a);
            if (S.PassThrown && S.Ball.State == BallState.Pass && input.Move.Length < 0.1f && S.Time >= a.BallBreakAt) BallReaction(a);   // QA-435: the assist honours the reaction window
        }
    }
}
namespace PixFootball.Sim
{
    internal sealed partial class DefenseAI { }
}
namespace PixFootball.Sim
{
    internal sealed partial class DefenseAI
    {
        /// <summary>On a called run, defenders key the back (not the quarterback holding the ball) until the exchange.</summary>
        private SimAgent FocusOn(SimAgent a, SimAgent c)
        {
            if (c != null && c.Side == SimSide.Offense && c.Slot == 0 && !S.PassThrown)
            {
                // play action: the fooled defenders chase the fake runner, not the quarterback holding the ball
                if (_playIsPlayAction && _fakeRunner != null && c.QbState != QbPhase.Scramble && !S.QbHasCrossedLos) return _fakeRunner;
                if (_intendedRunner != null && _playIsRun && !S.Blocks.HandedOff && !S.QbHasCrossedLos && c.QbState != QbPhase.Done && c.QbState != QbPhase.Scramble)
                    return _intendedRunner;
            }
            return c;
        }
    }
}
namespace PixFootball.Sim
{
    internal sealed partial class DefenseAI
    {
        /// <summary>
        /// How much this defender is responsible for a pass arriving at point T intended for targetId (0..1): man defenders
        /// own their man, zone defenders own their area; everyone else only helps when the ball comes right at them.
        /// </summary>
        public float Responsibility(SimAgent d, Vec2 T, int targetId)
        {
            if (d.Assign is RushAssignment || d.Assign is BlitzAssignment)
            {
                // a rusher only threatens a lane he is actually standing near
                float rd = (d.Pos - T).Length;
                return rd < 1.5f ? 1f : SimMath.Clamp01(1.2f - rd / 5.5f) * 0.9f + 0.1f;
            }
            float near = (d.Pos - T).Length;
            var cov = d.Assign as CoverageAssignment;
            if (cov != null && cov.IsMan && d.CoverTarget >= 0)
            {
                if (d.CoverTarget == targetId) return 1f;
                return near < 3.0f ? 0.65f : (near < 6f ? 0.3f : 0.12f);
            }
            if (d.Zone != null)
            {
                ZoneRegion z = d.Zone;
                float cx = ZoneX(z), cy = S.LosY + z.CenterY;
                bool deep = IsDeep(z);
                float zr = d.Fx != null ? Math.Max(0f, d.Fxv(SimHook.ZoneRange)) * ZoneRangeYardsPerPoint : 0f;   // v0.10.0: Sideline To Sideline / Range Rover widen the area he owns when the ball is in the air too
                float dx = Math.Abs(T.X - cx) - (z.Width * 0.5f + 3.0f + zr);
                float dy;
                if (deep) dy = (cy - 4f - zr) - T.Y; else dy = Math.Abs(T.Y - cy) - (z.Height * 0.5f + 3.0f + zr);
                float outside = Math.Max(0f, Math.Max(dx, dy));
                if (outside <= 0f) return 1f;
                float w = 1f - outside / 6f;
                return Math.Max(0.15f, Math.Min(1f, w)) * (near < 4f ? 1f : 0.85f);
            }
            return near < 5f ? 0.7f : 0.2f;
        }
    }
}
namespace PixFootball.Sim
{
    internal sealed partial class DefenseAI
    {
        private float _exchangeAt = -1f;
        private bool _qbDraw;

        /// <summary>The draw: defenders who dropped into pass coverage need a beat to see the handoff and turn their hips back downhill.</summary>
        private bool DrawHold(SimAgent a)
        {
            if (!_playIsDraw || _exchangeAt < 0f) return false;
            float aw = SimMath.Rating01(a.R.Awareness);
            float hold = PositionGroup.IsDefensiveLine(a.PlayPos) ? 0.10f : (_qbDraw ? 0.02f + 0.15f * (1f - aw) : 0.30f + 0.30f * (1f - aw) + SimTuning.DrawHoldExtra);
            return S.Time < _exchangeAt + hold;
        }
    }
}
namespace PixFootball.Sim
{
    internal sealed partial class DefenseAI
    {
        private bool QbDrawRunning(SimAgent c)
        {
            return _qbDraw && c.Slot == 0 && c.Side == SimSide.Offense && c.QbState == QbPhase.Done && S.Time > _exchangeAt;
        }
    }
}
namespace PixFootball.Sim
{
    internal sealed partial class DefenseAI
    {
        /// <summary>True when another coverage defender is clearly closer to the receiver and already within reach of him (a pass-off in zone).</summary>
        /// <summary>
        /// v0.9.0 (E-195): deep zones pass off. A receiver is NOT this deep defender's when another deep defender is over the top of him (deeper than the receiver) and clearly closer
        /// (the vertical stack: one over, one under), so two safeties no longer lock onto the same crosser.
        /// </summary>
        private bool CoveredByDeepTeammate(SimAgent me, SimAgent r, Vec2 receiverPos, float myDist)
        {
            for (int j = 11; j < 22; j++)
            {
                SimAgent o = S.Agents[j];
                if (o == me || o.State == AgentState.Down || o.Body.FallenTime > 0f || o.Zone == null || !IsDeep(o.Zone)) continue;
                float d = (o.Pos - receiverPos).Length;
                if (d < 4.2f && d < myDist - 0.8f && o.Pos.Y > receiverPos.Y - 0.5f && o.ZoneTargetId == r.Id) return true;
            }
            return false;
        }

        private bool CoveredByTeammate(SimAgent me, Vec2 receiverPos, float myDist)
        {
            for (int j = 11; j < 22; j++)
            {
                SimAgent o = S.Agents[j];
                if (o == me || o.State == AgentState.Down || o.Body.FallenTime > 0f) continue;
                if (!(o.Assign is CoverageAssignment) && !(o.Assign is SpyAssignment)) continue;
                float d = (o.Pos - receiverPos).Length;
                if (d < 3.4f && d < myDist - 0.35f) return true;
            }
            return false;
        }
    }
}
