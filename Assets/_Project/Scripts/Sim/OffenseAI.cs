using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// Offensive intelligence: quarterback (dropback, read progression, pressure, scramble, throw), route running with
    /// coverage-aware settling and scramble drill, runner mesh, plus hooks for blockers and the ball carrier.
    /// </summary>
    internal sealed partial class OffenseAI
    {
        private readonly PlaySimulation S;
        private readonly CarrierAI _carrierAI;
        private QbAssignment _qa;
        private SimAgent _qb;
        private bool _shotgun;
        private bool _isPass;
        private readonly List<int> _reads = new List<int>();
        private SimAgent _handoffTarget;
        private SimAgent _fakeTarget;
        private Vec2 _dropPoint;
        private bool _rolling;
        private bool _hotMode;
        private readonly float[] _zAt = new float[21];

        public OffenseAI(PlaySimulation s) { S = s; _carrierAI = new CarrierAI(s); }

        public bool IsPassPlay { get { return _isPass; } }
        public SimAgent ScreenReceiver { get; private set; }

        // =====================================================================================
        // Preparation
        // =====================================================================================

        public void Prepare()
        {
            _qb = S.Agents[0];
            _qa = S.OffPlay.Assignments[0] as QbAssignment;
            _isPass = false;
            _reads.Clear();
            _handoffTarget = null; _fakeTarget = null;
            if (S.Kind != PlayKind.Scrimmage && S.Kind != PlayKind.Kneel && S.Kind != PlayKind.Spike) return;
            if (_qa == null) return;
            _shotgun = _qa.Shotgun || S.OffForm.Slots[0].Y <= -3f;
            switch (_qa.Action)
            {
                case QbAction.DropbackPass: case QbAction.QuickPass: case QbAction.PlayActionFake: case QbAction.Rollout:
                case QbAction.Boot: case QbAction.ScreenPass: case QbAction.HailMary:
                    _isPass = true; break;
            }
            for (int i = 0; i < _qa.ReadProgression.Count; i++)
            {
                int slot = _qa.ReadProgression[i];
                if (slot > 0 && slot < 11 && S.Agents[slot].IsReceiverEligible && !_reads.Contains(slot)) _reads.Add(slot);
            }
            // checkdown options: any other eligible running a route, deepest-first order reversed so the hot outlet is last
            var rest = new List<int>();
            for (int i = 1; i < 11; i++)
            {
                SimAgent a = S.Agents[i];
                if (!a.IsReceiverEligible || _reads.Contains(i)) continue;
                if (a.Assign is RouteAssignment) rest.Add(i);
            }
            rest.SortStable((p, q) => S.Agents[q].Pos.Y.CompareTo(S.Agents[p].Pos.Y));
            _reads.AddRange(rest);

            ScreenReceiver = (_qa.Action == QbAction.ScreenPass && _reads.Count > 0) ? S.Agents[_reads[0]] : null;
            if (_qa.HandoffSlot >= 0 && _qa.HandoffSlot < 11) _handoffTarget = S.Agents[_qa.HandoffSlot];
            for (int i = 1; i < 11; i++)
            {
                var ca = S.Agents[i].Assign as CarryAssignment;
                if (ca == null) continue;
                if (ca.IsFake) _fakeTarget = S.Agents[i];
                else if (_handoffTarget == null) _handoffTarget = S.Agents[i];
            }
            int protectors = 0;
            for (int i = 0; i < 11; i++) { var pb = S.Agents[i].Assign as BlockAssignment; if (pb != null && (pb.Block == BlockKind.PassPro || pb.Block == BlockKind.Chip)) protectors++; }
            _hotMode = _isPass && _qa.Action != QbAction.HailMary && _qa.Action != QbAction.ScreenPass && S.DefPlay.PassRushers >= 5 && S.DefPlay.PassRushers > protectors - 1;   // the blitz is hot when the rushers outnumber (or match) the blockers
            ComputeDropPoint();
        }

        private void ComputeDropPoint()
        {
            float x0 = S.BallX0 + S.OffForm.Slots[0].X;
            float y0 = S.LosY + S.OffForm.Slots[0].Y;
            int steps = _qa.DropbackSteps;
            float depthBelow; // yards behind the LOS
            if (_shotgun) depthBelow = steps <= 3 ? 5.0f : (steps <= 5 ? 6.0f : 7.5f);
            else depthBelow = steps <= 0 ? 1.5f : (steps <= 3 ? 4.2f : (steps <= 5 ? 6.4f : 8.6f));
            if (_hotMode) depthBelow = Math.Min(depthBelow, _shotgun ? 5.0f : 4.4f); // the blitz is on: a short drop and a quick throw
            _rolling = _qa.Action == QbAction.Rollout || _qa.Action == QbAction.Boot;
            float x = x0;
            if (_rolling)
            {
                x = x0 + Math.Sign(_qa.RollDirection == 0 ? 1 : _qa.RollDirection) * 6.0f;
                depthBelow = Math.Min(depthBelow, 4.5f);
            }
            _dropPoint = new Vec2(SimMath.Clamp(x, 3f, FieldGeometry.Width - 3f), S.LosY - depthBelow);
        }

        // =====================================================================================
        // Snap
        // =====================================================================================

        public void OnSnap()
        {
            if (S.Kind == PlayKind.Kickoff || S.Kind == PlayKind.Punt || S.Kind == PlayKind.FieldGoal) return;
            SimAgent center = S.Snapper;
            _qb.QbClock = 0f;
            if (_qa == null)
            {
                // a malformed play without a quarterback assignment: the ball goes to the first real ball carrier (or the quarterback) so the play cannot hang
                SimAgent alt = null;
                for (int i = 1; i < 11 && alt == null; i++) { var cc = S.Agents[i].Assign as CarryAssignment; if (cc != null && !cc.IsFake) alt = S.Agents[i]; }
                S.GiveBall(alt ?? _qb);
                return;
            }
            switch (_qa.Action)
            {
                case QbAction.Kneel: _qb.QbState = QbPhase.Kneel; break;
                case QbAction.Spike: _qb.QbState = QbPhase.Spike; break;
                case QbAction.Handoff: case QbAction.Toss: case QbAction.ReadOption: _qb.QbState = QbPhase.Mesh; break;
                case QbAction.Keep: case QbAction.Sneak: _qb.QbState = QbPhase.Done; break;
                case QbAction.PlayActionFake: case QbAction.Boot: _qb.QbState = QbPhase.Fake; break;
                default: _qb.QbState = QbPhase.Dropback; break;
            }
            Vec3 from = new Vec3(center.Pos, 0.9f);
            if (_shotgun)
            {
                Vec3 to = new Vec3(_qb.Pos + new Vec2(0f, 0.15f), 1.15f);
                Vec3 vel; float tf;
                BallPhysics.SolveLaunch(from, to, 17f, false, out vel, out tf);
                S.Ball.Launch(BallState.Snap, from, vel, center.Id);
                _qb.QbPendingState = _qb.QbState;
                _qb.QbState = QbPhase.WaitSnap;
            }
            else
            {
                // under center: a short snap (the ball travels the yard from the center's hands into the quarterback's, about a tenth of a second)
                Vec3 to = new Vec3(_qb.Pos + new Vec2(0f, 0.1f), 1.0f);
                Vec3 vel; float tf;
                BallPhysics.SolveLaunch(from, to, 7.5f, false, out vel, out tf);
                S.Ball.Launch(BallState.Snap, from, vel, center.Id);
                _qb.QbPendingState = _qb.QbState;
                _qb.QbState = QbPhase.WaitSnap;
            }
        }

        // =====================================================================================
        // Per-tick update
        // =====================================================================================

        public void Update()
        {
            if (S.Kind == PlayKind.Kickoff || S.Kind == PlayKind.Punt || S.Kind == PlayKind.FieldGoal) return;
            for (int i = 0; i < 11; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.ControlledByUser) continue;
                if (a.State == AgentState.Down || a.Body.FallenTime > 0f) continue;
                if (S.BallSide != SimSide.Offense)
                {
                    // possession flipped: the offense now pursues and tackles
                    if (a.State != AgentState.Engaged) S.DefAI.PursuitUpdate(a);
                    continue;
                }
                if (a.HasBall)
                {
                    if (i == 0 && _qb.QbState != QbPhase.Done && !S.PassThrown) { UpdateQb(); continue; }
                    _carrierAI.Update(a);
                    continue;
                }
                if (a.State == AgentState.Engaged) continue;
                if (i == 0) { UpdateQb(); continue; }
                if (a.Assign is BlockAssignment) { S.Blocks.UpdateBlocker(a); continue; }
                if (a.Assign is RouteAssignment) { RunRoute(a); continue; }
                if (a.Assign is CarryAssignment) { RunnerPreBall(a); continue; }
                a.Intent = AgentAction.Idle;
            }
        }

        // =====================================================================================
        // Prediction helpers
        // =====================================================================================

        /// <summary>Where agent a will be t seconds from now (route-aware for receivers).</summary>
        public Vec2 PredictPos(SimAgent a, float t)
        {
            var ra = a.Assign as RouteAssignment;
            if (ra != null && a.Side == SimSide.Offense && !a.HasBall && !a.RouteDone && S.BallSide == SimSide.Offense)
            {
                float v = Math.Max(a.Speed, a.Body.VMax * 0.7f);
                v = Math.Min(v, a.Body.VMax * 0.98f);
                float budget = v * t;
                Vec2 p = a.Pos;
                int idx = a.PathIdx;
                var wps = ra.Waypoints;
                while (idx < wps.Count && budget > 0f)
                {
                    Vec2 w = S.RouteRel(wps[idx].Point);
                    float seg = (w - p).Length;
                    if (seg <= budget) { budget -= seg; p = w; idx++; }
                    else { p = p + (w - p).Norm() * budget; budget = 0f; }
                }
                if (budget > 0f && wps.Count > 0)
                {
                    Vec2 last = S.RouteRel(wps[wps.Count - 1].Point);
                    Vec2 prev = wps.Count > 1 ? S.RouteRel(wps[wps.Count - 2].Point) : a.Pos;
                    Vec2 dir = (last - prev).Norm();
                    if (!ContinuesDownfield(ra.Route)) budget *= 0.2f;
                    p = p + dir * budget;
                }
                return p;
            }
            Vec2 vel = a.Vel;
            float tt = Math.Min(t, 1.4f);
            return a.Pos + vel * tt * (a.HasBall ? 0.95f : 0.8f);
        }

        public static bool ContinuesDownfield(RouteKind k)
        {
            switch (k)
            {
                case RouteKind.Go: case RouteKind.Fade: case RouteKind.Seam: case RouteKind.Post: case RouteKind.Corner:
                case RouteKind.Wheel: case RouteKind.Corner2: case RouteKind.OutAndUp: return true;
                default: return false;
            }
        }

        // =====================================================================================
        // Route running
        // =====================================================================================

        private void RunRoute(SimAgent a)
        {
            var ra = (RouteAssignment)a.Assign;
            a.Intent = AgentAction.Route;
            if (S.PassThrown && S.TargetId == a.Id && S.Ball.State == BallState.Pass) { TrackBall(a); return; }
            if (S.Phase != PlayPhase.Live) return;
            var wps = ra.Waypoints;
            float vmax = a.Body.EffectiveVMax;

            // jammed at the line: the route keeps running (waypoints still advance) but at a third of the pace for the jam time
            float jamScale = a.JamTime > 0f ? 0.38f : 1f;
            // the Hail Mary: the receivers converge on the landing zone in front of the goal line instead of running five parallel go routes
            if (_qa != null && _qa.Action == QbAction.HailMary && S.Time > 1.3f)
            {
                Vec2 zone = new Vec2(S.BallX0 + SimMath.Clamp((a.Pos.X - S.BallX0) * 0.3f, -5f, 5f), Math.Min(Math.Max(a.Pos.Y + 4f, S.LosY + 55f), 98f));
                Vec2 toZone = zone - a.Pos;
                a.Want = toZone.Norm() * Math.Min(vmax * 0.97f, Math.Max(0f, toZone.Length * 3f));
                a.HasFace = true; a.FaceDir = (_qb.Pos - a.Pos).Norm();
                return;
            }

            if (!a.RouteDone && a.PathIdx >= wps.Count) a.RouteDone = true;

            if (a.RouteDone || wps.Count == 0)
            {
                FinishRoute(a, ra);
                return;
            }

            // scramble drill: abandon the route if the quarterback is on the move outside the pocket
            if (ScrambleDrillActive() && S.Time > 1.8f && a.PathIdx >= Math.Max(1, wps.Count - 1))
            {
                ScrambleDrillMove(a);
                return;
            }

            Vec2 target = S.RouteRel(wps[a.PathIdx].Point);
            Vec2 prevPt = a.PathIdx == 0 ? S.Rel(Vec2.Zero) + new Vec2(S.OffForm.Slots[a.Slot].X, S.OffForm.Slots[a.Slot].Y) : S.RouteRel(wps[a.PathIdx - 1].Point);
            float dist = (target - a.Pos).Length;

            // corner angle at this waypoint
            float cornerAng = 0f;
            if (a.PathIdx + 1 < wps.Count)
            {
                Vec2 d1 = (target - prevPt).Norm();
                Vec2 d2 = (S.RouteRel(wps[a.PathIdx + 1].Point) - target).Norm();
                cornerAng = SimMath.AngleBetween(d1, d2);
            }
            // real route runners plant AT the break depth: sharp breaks are reached to within a third of a yard, gentle bends round off
            float arrive = SimMath.Lerp(1.3f, 0.35f, SimMath.Clamp01((cornerAng - 0.5f) / 0.9f));
            // vertical stems get a longer leash so fast receivers do not loop back
            if (dist < arrive)
            {
                a.PathIdx++;
                if (a.Fx != null && cornerAng > 0.8f) a.BreakBurstUntil = S.Time + PlaySimulation.BreakBurstSeconds;   // v0.8.0 Crisp Routes: a hard plant is followed by a burst
                if (a.PathIdx >= wps.Count) { a.RouteDone = true; FinishRoute(a, ra); return; }
                target = S.RouteRel(wps[a.PathIdx].Point);
                dist = (target - a.Pos).Length;
            }

            float segTime = wps[a.PathIdx].Time - (a.PathIdx > 0 ? wps[a.PathIdx - 1].Time : 0f);
            float segLen = (target - prevPt).Length;
            float vRoute = SimMath.Clamp(segLen / Math.Max(0.3f, segTime) * 1.32f, vmax * 0.55f, vmax * 0.985f);

            // brake into sharp cuts based on agility
            if (a.PathIdx < wps.Count - 1 && cornerAng > 0.6f)
            {
                float sharp = SimMath.Clamp01((cornerAng - 0.6f) / 1.6f);
                float entry = vmax * SimMath.Lerp(0.8f, 0.30f, sharp) * (0.85f + 0.15f * a.Body.Agility01 + (a.R.Awareness > 70 ? 0.03f : 0f));
                float decel = a.Body.DecelBase * 0.75f;
                float allowed = (float)Math.Sqrt(entry * entry + 2f * decel * Math.Max(0f, dist - 0.5f));
                vRoute = Math.Min(vRoute, allowed);
            }
            Vec2 dir = (target - a.Pos).Norm();
            // press release / defender contact: gentle weave around a trailing/leveraging defender is skipped; keep the stem
            a.Want = dir * (vRoute * jamScale);
            a.RouteClock += PlaySimulation.Dt;
        }

        private void FinishRoute(SimAgent a, RouteAssignment ra)
        {
            a.RouteDone = true;
            a.Intent = AgentAction.Route;
            float vmax = a.Body.EffectiveVMax;
            var wps = ra.Waypoints;
            Vec2 last = wps.Count > 0 ? S.RouteRel(wps[wps.Count - 1].Point) : a.Pos;
            Vec2 prev = wps.Count > 1 ? S.RouteRel(wps[wps.Count - 2].Point) : a.Pos;
            if (ScrambleDrillActive()) { ScrambleDrillMove(a); return; }
            if (ContinuesDownfield(ra.Route))
            {
                Vec2 dir = (last - prev).Norm();
                if (dir.LenSq() < 0.25f) dir = new Vec2(0f, 1f);
                a.Want = dir * (vmax * 0.97f);
                return;
            }
            // find a soft spot / settle against the coverage and turn to the quarterback
            Vec2 spot = FindSoftSpot(a, last);
            Vec2 to = spot - a.Pos;
            float d = to.Length;
            a.HasFace = true;
            a.FaceDir = (_qb.Pos - a.Pos).Norm();
            if (d > 0.4f) a.Want = to.Norm() * Math.Min(vmax * 0.6f, d * 3.5f);
            else a.Want = Vec2.Zero;
        }

        private Vec2 FindSoftSpot(SimAgent a, Vec2 anchor)
        {
            // only re-evaluate a few times a second; cache on the agent
            if (a.ReactionClock > 0f) { a.ReactionClock -= PlaySimulation.Dt; return a.Landmark; }
            a.ReactionClock = 0.2f;
            Vec2 best = a.Pos; float bestScore = float.MinValue;
            for (int ix = -2; ix <= 2; ix++)
            {
                for (int iy = -1; iy <= 1; iy++)
                {
                    Vec2 c = new Vec2(anchor.X + ix * 1.6f, anchor.Y + iy * 1.8f);
                    c = new Vec2(SimMath.Clamp(c.X, 1.5f, FieldGeometry.Width - 1.5f), Math.Max(S.LosY - 1f, c.Y));
                    float minD = 99f;
                    for (int j = 11; j < 22; j++)
                    {
                        SimAgent d = S.Agents[j];
                        if (d.State == AgentState.Down) continue;
                        float dd = (d.Pos - c).Length;
                        if (dd < minD) minD = dd;
                    }
                    float move = (c - a.Pos).Length;
                    float score = Math.Min(minD, 7f) - 0.18f * move - 0.2f * Math.Abs(iy);
                    if (score > bestScore) { bestScore = score; best = c; }
                }
            }
            a.Landmark = best;
            return best;
        }

        private bool ScrambleDrillActive()
        {
            if (S.PassThrown || _qb.QbState != QbPhase.Scramble) return false;
            return _qb.HasBall;
        }

        private void ScrambleDrillMove(SimAgent a)
        {
            float qbx = _qb.Pos.X;
            float side = _qb.Vel.X != 0f && Math.Abs(_qb.Vel.X) > 0.5f ? Math.Sign(_qb.Vel.X) : (qbx < FieldGeometry.CenterX ? -1f : 1f);
            // receivers work to the QB's side, open grass beyond the nearest defender
            float tx = SimMath.Clamp(_qb.Pos.X + side * (6f + 2f * (a.Slot % 3)), 3f, FieldGeometry.Width - 3f);
            float ty = Math.Max(a.Pos.Y, S.LosY + 8f + (a.Slot % 3) * 2f);
            Vec2 target = new Vec2(tx, ty);
            Vec2 to = target - a.Pos;
            a.Want = to.Norm() * Math.Min(a.Body.EffectiveVMax * 0.9f, Math.Max(0f, to.Length * 3f));
            a.HasFace = true;
            a.FaceDir = (_qb.Pos - a.Pos).Norm();
            a.Intent = AgentAction.Route;
        }

        // =====================================================================================
        // Tracking a thrown ball
        // =====================================================================================

        private void TrackBall(SimAgent a)
        {
            BallSim ball = S.Ball;
            Vec3 at; float tt;
            Vec2 pt;
            float catchZ = 1.6f;
            if (BallPhysics.PredictAtHeight(ball.Pos, ball.Vel, catchZ, out at, out tt)) pt = at.XY;
            else { pt = at.XY; tt = 0.3f; }
            float vmax = a.Body.EffectiveVMax;
            float targetZ = catchZ;
            bool leap = false;
            // an overthrown or lofted ball: work to the point on its flight where he can actually get a hand on it, leaping if he has to
            {
                float stand = S.Catches.StandReach(a);
                float maxReach = S.Catches.JumpReach(a) * 0.96f;
                Vec3 p = ball.Pos, v = ball.Vel;
                float t = 0f, bestCost = float.MaxValue;
                bool nominalOk = false;
                {
                    float dn = Math.Max(0f, (pt - a.Pos).Length - 0.7f);
                    nominalOk = dn / Math.Max(1f, vmax * 0.98f) <= tt + 0.05f && catchZ <= maxReach;
                }
                if (!nominalOk || (ball.Pos.Z > stand * 1.05f && (ball.Pos.XY - a.Pos).Length < 7f))
                {
                    for (int k = 0; k < 70; k++)
                    {
                        BallPhysics.Advance(ref p, ref v, 0.05f);
                        t += 0.05f;
                        if (p.Z < 0.5f) break;
                        if (p.Z > maxReach) continue;
                        float dist = Math.Max(0f, (p.XY - a.Pos).Length - 0.7f);
                        float need = dist / Math.Max(1f, vmax * 0.98f);
                        if (need > t + 0.04f) continue;
                        float cost = Math.Abs(p.Z - 1.7f) + 0.03f * t;
                        if (cost < bestCost) { bestCost = cost; pt = p.XY; tt = t; targetZ = p.Z; }
                    }
                }
                leap = targetZ > stand * 0.93f;
            }
            Vec2 to = pt - a.Pos;
            float dist2 = to.Length;
            float need2 = dist2 / Math.Max(0.2f, tt);
            float speed = Math.Min(vmax, Math.Max(need2 * 1.05f, 2.0f));
            if (dist2 < 0.35f) speed = 0f;
            a.Want = to.Norm() * speed;
            a.HasFace = true;
            // run to the spot with the shoulders square to the run; only turn to the ball once it is nearly there (looking back costs no speed)
            Vec2 toBall = ball.Pos.XY - a.Pos;
            if (dist2 > 2.0f && tt > 0.55f && to.Dot(toBall) < 0.6f * to.Length * toBall.Length) a.FaceDir = to.Norm();
            else a.FaceDir = toBall.Length > 0.3f ? toBall.Norm() : a.Facing;
            a.Intent = AgentAction.Route;
            // leap for balls above standing reach, timed so the top of the jump meets the ball
            if (leap && tt < 0.36f && tt > 0.05f && dist2 < 2.4f) S.TryJump(a);
        }

        // =====================================================================================
        // Runner before the handoff
        // =====================================================================================

        private void RunnerPreBall(SimAgent a)
        {
            var ca = (CarryAssignment)a.Assign;
            a.Intent = AgentAction.Carry;
            float vmax = a.Body.EffectiveVMax;
            if (S.Time < ca.Delay)
            {
                // draw/delay: look like pass protection
                a.Want = Vec2.Zero;
                a.HasFace = true; a.FaceDir = new Vec2(0f, 1f);
                return;
            }
            if (ca.IsFake && ca.Path.Count > 0 && a.PathIdx >= ca.Path.Count - 1 && (S.Rel(ca.Path[ca.Path.Count - 1]) - a.Pos).Length < 1.6f && S.PassThrown == false)
            {
                S.Blocks.FakeCarrierBlock(a);
                return;
            }
            if (ca.Path.Count == 0) return;
            while (a.PathIdx < ca.Path.Count - 1 && (S.Rel(ca.Path[a.PathIdx]) - a.Pos).Length < 1.0f) a.PathIdx++;
            Vec2 wp = S.Rel(ca.Path[Math.Min(a.PathIdx, ca.Path.Count - 1)]);
            Vec2 to = wp - a.Pos;
            // before getting the ball keep the pace the mesh needs; a fake carries on at full speed
            float v = ca.IsFake ? vmax * 0.95f : vmax * 0.82f;
            if (!ca.IsFake && _shotgun && !S.Blocks.HandedOff && (_qb.QbState == QbPhase.WaitSnap || _qb.QbState == QbPhase.Mesh) && (a.Pos - _qb.Pos).Length < 7.5f)
                v = Math.Min(v, _qb.QbState == QbPhase.WaitSnap ? 1.6f : 3.4f);
            if (!ca.IsFake && a.PathIdx >= ca.Path.Count - 1 && to.Length < 1.0f) { a.Want = Vec2.Zero; a.HasFace = true; a.FaceDir = new Vec2(0f, 1f); return; }   // he waits at the mesh point instead of walking into the quarterback
            a.Want = to.Norm() * Math.Min(v, Math.Max(2.5f, to.Length * 5f));
        }

        // =====================================================================================
        // User input helpers (QB / receivers / blockers)
        // =====================================================================================

        public void UserEngagedInput(SimAgent a, ControlInput input)
        {
            if (a.Engagement != null) a.Engagement.UserHint(a, input);
        }

        public void UserReceiverInput(SimAgent a, ControlInput input)
        {
            if (input.Jump || input.Dive) S.TryJump(a);
            if (S.PassThrown && S.Ball.State == BallState.Pass && input.Move.Length < 0.1f) TrackBall(a);
        }
    }
}
