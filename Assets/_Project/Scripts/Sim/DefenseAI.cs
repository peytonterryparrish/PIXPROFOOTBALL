using System;
using System.Collections.Generic;
using PixFootball.Core;

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
                    a.Landmark = S.Rel(new Vec2(ca.Zone.CenterX, ca.Zone.CenterY));
                    a.Landmark = new Vec2(SimMath.Clamp(a.Landmark.X, 3f, FieldGeometry.Width - 3f), a.Landmark.Y);
                }
            }
            if (S.Kind != PlayKind.Scrimmage) return;
            _playIsRun = S.OffPlay.IsRun;
            _playIsDraw = S.OffPlay.Category == PlayCategory.Draw;
            _playIsPlayAction = S.OffPlay.Category == PlayCategory.PlayAction;
            MatchMen();
            _intendedRunner = null; _fakeRunner = null;
            for (int i = 1; i < 11; i++) { var ca = S.Agents[i].Assign as CarryAssignment; if (ca != null && !ca.IsFake) { _intendedRunner = S.Agents[i]; break; } }
            for (int i = 1; i < 11; i++) { var ca = S.Agents[i].Assign as CarryAssignment; if (ca != null && ca.IsFake) { _fakeRunner = S.Agents[i]; break; } }
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
                    else if (d.PlayPos == Position.CB) cost += r.PlayPos == Position.WR ? 0f : 2.0f;
                    pairs.Add(new MatchPair { D = d, R = r, Cost = cost });
                }
            }
            pairs.Sort((p, q) => p.Cost.CompareTo(q.Cost));
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
            if (PositionGroup.IsLinebacker(a.PlayPos)) d += 0.10f;
            if (PositionGroup.IsDefensiveBack(a.PlayPos)) d += 0.20f;
            return d;
        }

        // =====================================================================================
        // Per-tick update
        // =====================================================================================

        public void Update()
        {
            if (S.Kind != PlayKind.Scrimmage) return;
            bool turnover = S.BallSide != SimSide.Offense;
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
            bool carrierRuns = c != null && c.Side == SimSide.Offense && (c.Slot != 0 || S.QbHasCrossedLos || c.QbState == QbPhase.Scramble);
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
            if (_playIsRun) return true;
            return carrierRuns;
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
            if (ball.State == BallState.Loose && !S.PassTipped && (S.Fumbled || S.FumbleCount > 0)) { PursueBall(a); return; }

            SimAgent c = S.Carrier;
            if (c != null && c.Side == SimSide.Offense)
            {
                bool running = c.Slot != 0 || S.QbHasCrossedLos || c.QbState == QbPhase.Scramble;
                if (running)
                {
                    // the ball is out and moving: everyone fits the run once he has read it
                    if (S.PassCompleted || S.Time >= a.DefReadAt) { RunResponse(a, c); return; }
                }
                else if (!S.PassThrown && PerceivesRun(a))
                {
                    // a called run (or a play-action fake) before the exchange: key the back, not the quarterback
                    RunResponse(a, FocusOn(a, c));
                    return;
                }
            }

            // 3. the assignment
            if (a.Assign is RushAssignment) { RushUpdate(a, (RushAssignment)a.Assign); return; }
            if (a.Assign is BlitzAssignment) { BlitzUpdate(a, (BlitzAssignment)a.Assign); return; }
            if (a.Assign is SpyAssignment) { SpyUpdate(a); return; }
            var cov = a.Assign as CoverageAssignment;
            if (cov != null && cov.IsMan && a.CoverTarget >= 0) { ManUpdate(a, cov); return; }
            ZoneUpdate(a);
        }

        // =====================================================================================
        // Pass rush
        // =====================================================================================

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
            Vec2 aim;
            if (S.Time < 0.22f)
            {
                aim = GapPoint(a, ra.Gap);
                if (ra.Stunt != StuntKind.None && S.Time < 0.35f) aim = new Vec2(S.BallX0 + GapOffset(ra.Gap) * 0.8f, S.LosY - 0.2f);
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
                    float lane = (S.BallX0 + GapOffset(ra.Gap) - target.X) * 0.25f;
                    aim = new Vec2(target.X + lane, target.Y);
                    if ((a.Pos - target).Length < 3f) aim = target;
                }
            }
            Vec2 to = aim - a.Pos;
            a.Want = to.Norm() * vmax;
            TryTackleDive(a);
        }

        private void BlitzUpdate(SimAgent a, BlitzAssignment ba)
        {
            a.Intent = AgentAction.Run;
            float vmax = a.Body.EffectiveVMax;
            if (S.Time < ba.Delay) { a.Want = Vec2.Zero; a.HasFace = true; a.FaceDir = new Vec2(0f, -1f); return; }
            Vec2 qb = S.Qb.Pos;
            SimAgent c = S.Carrier;
            Vec2 target = c != null && c.Side == SimSide.Offense ? c.Pos : qb;
            Vec2 aim;
            float gapX = S.BallX0 + GapOffset(ba.Gap);
            if (S.Time < ba.Delay + 0.55f && a.Pos.Y > S.LosY + 0.5f)
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
            bool qbRuns = qb.HasBall && (S.QbHasCrossedLos || qb.QbState == QbPhase.Scramble || qb.Pos.Y > S.LosY - 0.5f && qb.Speed > 3f);
            if (qbRuns || S.Carrier != null && S.Carrier != qb)
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
            SimAgent r = S.Agents[a.CoverTarget];
            a.Intent = AgentAction.Run;
            float vmax = a.Body.EffectiveVMax;
            float aw = SimMath.Rating01(a.R.Awareness);
            float man = SimMath.Rating01(a.R.ManCoverage);
            float react = 0.07f + (1f - aw) * 0.12f + (1f - man) * 0.06f;
            Vec2 rp = r.PosAgo(react);
            Vec2 rpPrev = r.PosAgo(react + 0.1f);
            Vec2 rv = (rp - rpPrev) * 10f;
            float rsp = rv.Length;
            float t = S.Time;

            // press: jam at the line
            if (cov.Press && t < 1.0f)
            {
                float dist0 = (r.Pos - a.Pos).Length;
                if (!a.MoveResolved && dist0 < 1.9f && t > 0.12f) { PressJam(a, r); a.MoveResolved = true; }
                if (t < 0.32f && !a.MoveResolved)
                {
                    a.Want = Vec2.Zero;
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
                    a.FrozenUntil = S.Time + hip;
                }
            }
            if (rsp > 2.2f) a.LastTargetDir = rdir;
            if (S.Time < a.FrozenUntil)
            {
                a.Want = a.HeldWant;
                return;
            }

            float startCushion = Math.Max(0.9f, Math.Abs(a.StartPos.Y - r.StartPos.Y) - 0.3f);
            float cushion;
            if (cov.Press) cushion = 0.9f + SimMath.Clamp01((t - 0.5f) / 1.0f) * 0.6f;
            else cushion = SimMath.Lerp(startCushion, 1.5f, SimMath.Clamp01((t - 0.4f) / 1.5f));
            // against short/horizontal routes keep the cushion
            Vec2 desired = rp + rv * 0.25f - rdir * cushion;
            // inside leverage
            float toMid = FieldGeometry.CenterX - rp.X;
            desired = new Vec2(desired.X + Math.Sign(toMid) * Math.Min(0.5f, Math.Abs(toMid) * 0.05f), desired.Y);
            if (r.Pos.Y < S.LosY + 1.5f && rsp < 2f) desired = rp + (rdir * -1f) * 1.2f;

            Vec2 to = desired - a.Pos;
            float dist = to.Length;
            Vec2 want = rv * 0.97f + to * 3.6f;
            float cap = vmax;
            // face the receiver while backpedaling/shuffling; turn and run when he gets vertical behind the cushion
            bool vertical = rv.Y > 0.55f * r.Body.VMax && dist > 0.5f && desired.Y > a.Pos.Y - 0.5f;
            bool faceReceiver = cushion > 2.0f && !vertical && S.Time < 3.0f;
            if (faceReceiver) { a.HasFace = true; a.FaceDir = (rp - a.Pos).Norm(); }
            if (want.Length > cap) want = want.Norm() * cap;
            a.Want = want;
            a.HeldWant = want;

            // holding / illegal contact when badly beaten before the throw
            if (S.Options.Penalties && !S.PassThrown && S.PendingPenalty == null)
            {
                float sep = (r.Pos - a.Pos).Length;
                bool beaten = sep > 0.8f && sep < 2.3f && rv.Y > 1.5f && a.Pos.Y < r.Pos.Y - 0.3f;
                if (beaten && t > 0.9f)
                {
                    float p = 0.00028f * (1.35f - aw);
                    if (S.Rng.NextFloat() < p)
                        S.Flag(r.Pos.Y - S.LosY > 5f && S.Rules.CollegeStyle == false ? PenaltyType.DefensiveHolding : PenaltyType.DefensiveHolding, a, r.Pos.Y - S.LosY);
                }
            }
        }

        private void PressJam(SimAgent d, SimAgent r)
        {
            float jam = 0.40f * d.R.Strength + 0.35f * d.R.ManCoverage + 0.25f * d.R.Agility;
            float rel = 0.35f * r.R.Strength + 0.35f * r.R.Agility + 0.30f * r.R.Awareness;
            float p = 1f / (1f + (float)Math.Exp(-(jam - rel) / 10f));
            bool win = S.Rng.NextFloat() < p;
            S.LogEvent(SimEventType.Jam, d.Id, r.Id, TackleType.None, win ? 1f : 0f);
            if (win)
            {
                r.JamTime = 0.30f + 0.35f * S.Rng.NextFloat();
                r.Body.Vel = r.Body.Vel * 0.5f;
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
            float halfW = z.Width * 0.5f + 2.5f;
            float depthLo = deep ? z.CenterY - 4f : z.CenterY - z.Height * 0.5f - 2.5f;
            float depthHi = deep ? 80f : z.CenterY + z.Height * 0.5f + 2.5f;
            for (int i = 1; i < 11; i++)
            {
                SimAgent r = S.Agents[i];
                if (!r.IsReceiverEligible || r.State == AgentState.Down) continue;
                Vec2 rp = r.Pos + r.Vel * 0.25f;
                float relY = rp.Y - S.LosY;
                float relX = rp.X - (S.BallX0 + z.CenterX);
                if (Math.Abs(relX) > halfW || relY < depthLo || relY > depthHi) continue;
                // do not stick with a receiver who is already covered by a man defender nearby
                float dist = (rp - a.Pos).Length;
                float score = -dist + (deep ? relY * 0.3f : 0f) - 0.5f * Math.Abs(relX) + (r.Vel.Y > 2f ? 1.0f : 0f);
                if (r.Id == S.TargetId) score += 3f;
                if (score > bestScore) { bestScore = score; tgt = r; }
            }

            Vec2 desired;
            if (tgt != null && S.Time > 0.45f)
            {
                Vec2 rp = tgt.Pos + tgt.Vel * 0.3f;
                if (deep)
                {
                    float cushion = 2.2f + 0.4f * Math.Max(0f, tgt.Vel.Y);
                    float y = Math.Max(lm.Y, rp.Y + cushion);
                    float x = SimMath.Clamp(rp.X, S.BallX0 + z.CenterX - halfW, S.BallX0 + z.CenterX + halfW);
                    desired = new Vec2(x * 0.7f + rp.X * 0.3f, y);
                }
                else
                {
                    Vec2 toQb = (qb - rp).Norm();
                    float cushion = 0.9f + 0.5f * Math.Max(0f, (rp - a.Pos).Length - 2.5f) * 0.1f;
                    desired = rp + toQb * cushion;
                    if (rp.Y > lm.Y + z.Height * 0.5f + 1.5f) desired = new Vec2(desired.X, Math.Max(lm.Y, desired.Y));
                }
            }
            else desired = lm;

            Vec2 to = desired - a.Pos;
            float dist2 = to.Length;
            float speed = Math.Min(vmax * 0.95f, Math.Max(1.2f, dist2 * 4.5f));
            if (dist2 < 0.4f) speed = 0f;
            Vec2 dir = to.Norm();
            a.Want = dir * speed;
            // eyes on the quarterback while dropping; turn and run when the landmark is deep
            bool backpedal = (a.Pos.Y < lm.Y - 0.5f || dist2 < 6f) && !(deep && dist2 > 7f);
            if (backpedal && tgt == null || tgt != null && !deep)
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
                float react = 0.16f + (1f - aw) * 0.30f;
                // trailing man defenders are looking at the receiver, not the quarterback
                bool facesQb = a.Facing.Dot(new Vec2(0f, -1f)) > 0.3f || a.Assign is CoverageAssignment && !((CoverageAssignment)a.Assign).IsMan;
                if (!facesQb) react += 0.12f;
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
            if (cov != null && !cov.IsMan && cov.Zone != null && IsDeep(cov.Zone) && S.Time < 0.9f && carrier.Pos.Y < S.LosY + 1f)
            {
                ZoneUpdate(a);
                return;
            }
            // linemen and blitzers attack their gap first
            if ((ra != null || a.Assign is BlitzAssignment) && a.Pos.Y > S.LosY - 0.2f && (carrier.Pos - a.Pos).Length > 3.5f && carrier.Pos.Y > S.LosY - 3f)
            {
                RunGap g = ra != null ? ra.Gap : ((BlitzAssignment)a.Assign).Gap;
                Vec2 gp = GapPoint(a, g);
                a.Want = (gp - a.Pos).Norm() * a.Body.EffectiveVMax;
                return;
            }
            PursueTarget(a, carrier);
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
            float vA = a.Body.EffectiveVMax * 0.985f;
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
            float lead = 0.30f + 0.70f * aw;
            Vec2 aim = c.Pos + vpred * (tInt * lead);
            // sideline defenders force the carrier inside: keep leverage
            Vec2 to = aim - a.Pos;
            float dist = (c.Pos - a.Pos).Length;
            if (dist < 1.4f) to = c.Pos + c.Vel * 0.1f - a.Pos;
            a.Want = to.Norm() * vA;
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
            float closing = (a.Vel - c.Vel).Dot(rel.Norm());
            // dive when the carrier is about to run away from the angle
            Vec2 cdir = c.Speed > 1.5f ? c.Vel.Norm() : c.Facing;
            float away = cdir.Dot(rel.Norm());
            if (closing > 2.0f || away > 0.2f)
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
                if (S.Carrier != null && S.Carrier.Side != a.Side) S.Moves.TryMove(a, CarrierMove.Dive);
            }
            if (input.Jump) S.TryJump(a);
            if (S.PassThrown && S.Ball.State == BallState.Pass && input.Move.Length < 0.1f) BallReaction(a);
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
            if (d.Assign is RushAssignment || d.Assign is BlitzAssignment) return 1f;
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
                float cx = S.BallX0 + z.CenterX, cy = S.LosY + z.CenterY;
                bool deep = IsDeep(z);
                float dx = Math.Abs(T.X - cx) - (z.Width * 0.5f + 3.0f);
                float dy;
                if (deep) dy = (cy - 4f) - T.Y; else dy = Math.Abs(T.Y - cy) - (z.Height * 0.5f + 3.0f);
                float outside = Math.Max(0f, Math.Max(dx, dy));
                if (outside <= 0f) return 1f;
                float w = 1f - outside / 6f;
                return Math.Max(0.15f, Math.Min(1f, w)) * (near < 4f ? 1f : 0.85f);
            }
            return near < 5f ? 0.7f : 0.2f;
        }
    }
}
