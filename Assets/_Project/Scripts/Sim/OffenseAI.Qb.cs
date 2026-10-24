using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Sim
{
    internal sealed partial class OffenseAI
    {
        // =====================================================================================
        // Quarterback state machine
        // =====================================================================================

        private void UpdateQb()
        {
            SimAgent q = _qb;
            if (S.Phase != PlayPhase.Live || q.State == AgentState.Down) return;
            QbSeqWatch(q);      // v0.10.0 QB: secure, scramble transition
            q.QbClock += PlaySimulation.Dt;
            q.Intent = AgentAction.Idle;

            if (q.QbState == QbPhase.WaitSnap)
            {
                q.HasFace = true; q.FaceDir = new Vec2(0f, 1f);
                if (S.Ball.Holder == q.Id) q.QbState = q.QbPendingState;
                else if (S.Ball.State != BallState.Snap && !q.HasBall)
                {
                    // a bad snap: he goes after the loose ball (and the loose-ball logic lets him fall on it)
                    Vec2 toBall = S.Ball.Pos.XY - q.Pos;
                    q.Want = toBall.Norm() * q.Body.EffectiveVMax * 0.9f;
                    q.HasFace = true; q.FaceDir = toBall.LenSq() > 0.2f ? toBall.Norm() : new Vec2(0f, 1f);
                    q.Intent = AgentAction.Run;
                }
                return;
            }

            switch (q.QbState)
            {
                case QbPhase.Kneel:
                    if (q.HasBall && q.QbClock > 0.9f) KneelOrSpike(true);
                    q.Want = Vec2.Zero;
                    return;
                case QbPhase.Spike:
                    if (q.HasBall && q.QbClock > 0.7f) KneelOrSpike(false);
                    q.Want = Vec2.Zero;
                    return;
                case QbPhase.Mesh: QbMesh(q); QbSeqMeshFace(q); return;      // v0.10.0 QB: he opens to the runner, he never spins his back to the field
                case QbPhase.Fake: QbFake(q); return;
                case QbPhase.Dropback: QbDropback(q); return;
                case QbPhase.Set: QbSet(q); return;
                case QbPhase.Windup: QbWindup(q); return;
                case QbPhase.Scramble: UpdateQbScramble(); return;
                case QbPhase.Done:
                default:
                    if (q.HasBall && _qa != null && _qa.Action == QbAction.Keep && _qa.DropbackSteps > 0 && S.Time < 1.05f)
                    {
                        // the QB draw: a real three-step drop to the drop point, a plant, then take off (v0.9.0 E-097: it was a 1.5 yd/s shuffle for 0.85 s, about 0.8 yd)
                        Vec2 toDrop = _dropPoint - q.Pos;
                        q.HasFace = true; q.FaceDir = new Vec2(0f, 1f);
                        float vd = Math.Min(q.Body.EffectiveVMax * 0.9f, Math.Max(0.8f, toDrop.Length * 4f));
                        q.Want = toDrop.LenSq() > 0.09f ? toDrop.Norm() * vd : Vec2.Zero;
                        return;
                    }
                    if (q.HasBall) { _carrierAI.Update(q); }
                    else
                    {
                        // after the handoff: ride the fake, then settle
                        q.HasFace = true; q.FaceDir = new Vec2(0f, 1f);
                        q.Want = QbFollowThrough(q);
                    }
                    return;
            }
        }

        private void KneelOrSpike(bool kneel)
        {
            if (kneel)
            {
                S.LogEvent(SimEventType.Kneel, _qb.Id, -1);
                S.PlayAction(_qb, AgentAction.Fallen, 0.8f);
                S.EndPlay(EndReason.Kneel);
            }
            else
            {
                S.LogEvent(SimEventType.Spike, _qb.Id, -1);
                S.Ball.Launch(BallState.Pass, new Vec3(_qb.Pos, 1.0f), new Vec3(0f, 1.5f, -6f), _qb.Id);
                _qb.HasBall = false;
                S.EndPlay(EndReason.Spike);
            }
        }

        // ---------------------------------------------------------------- handoffs

        private void QbMesh(SimAgent q)
        {
            var tgt = _handoffTarget;
            if (!q.HasBall) { q.QbState = QbPhase.Done; return; }
            if (tgt == null) { q.QbState = QbPhase.Done; return; }
            q.HasFace = true;
            float dist = (tgt.Pos - q.Pos).Length;
            q.Intent = AgentAction.Handoff;

            if (_qa.Action == QbAction.Toss)
            {
                // step toward the runner, pitch once he is on the edge
                q.FaceDir = (tgt.Pos - q.Pos).Norm();
                // v0.9.0 (E-156): an open step (two steps toward the runner's side) before the pitch, the pitch at 0.7-0.9 s, not the quarterback standing still and pitching at 0.28 s
                float openSign = Math.Sign(tgt.Pos.X - q.Pos.X);
                q.Want = S.Time < 0.55f && tgt.PlayPos != Position.WR ? new Vec2(openSign * 1.0f, -0.45f).Norm() * 2.4f : Vec2.Zero;
                if ((S.Time >= (tgt.PlayPos == Position.WR ? 0.40f : 0.62f) && dist < (tgt.PlayPos == Position.WR ? 10.5f : 7.5f) && dist > 2.0f && Math.Abs(tgt.Vel.X) > 1.5f) || (S.Time >= 1.0f && dist < 9.5f && dist > 1.5f)) { S.PitchBall(q, tgt); q.QbState = QbPhase.Done; }   // a deadline: the pitch goes even if the back is slow to the edge
                else if (S.Time > 1.3f) q.QbState = QbPhase.Done;
                return;
            }

            // handoff: shuffle toward the mesh point
            Vec2 meshPt = tgt.Pos + tgt.Vel * 0.18f;
            Vec2 toMesh = meshPt - q.Pos;
            Vec2 start = new Vec2(S.BallX0 + S.OffForm.Slots[0].X, S.LosY + S.OffForm.Slots[0].Y);
            // v0.9.0 (E-155): the mesh is at the runner's designed spot; the quarterback only steps and extends the arm (he no longer walks 5 yd toward a runner at 5.2 yd/s)
            if ((q.Pos - start).Length > 5.6f) toMesh = Vec2.Zero;
            q.FaceDir = toMesh.LenSq() > 0.04f ? toMesh.Norm() : new Vec2(0f, 1f);
            q.Want = toMesh.Norm() * Math.Min(5.2f, toMesh.Length * 5f);

            bool closestPassed = q.MeshPrevDist > 0f && dist > q.MeshPrevDist + 0.01f && q.MeshPrevDist < 2.5f;   // (CONNECT E-247 tightened this to 1.7: the outside runs then missed the exchange 25 percent of the time)   // v0.9.0 CONNECT E-247: the exchange needs the runner close (was 2.8 yd)
            q.MeshPrevDist = dist;
            // v0.9.0 (E-141): the exchange needs the runner at the mesh: 0.62 s from a shotgun (the back arcs in), 0.5 s from under centre (it was 0.22 s, with the back nearly standing)
            float minHandoff = _shotgun ? 0.78f : 0.70f;
            if (_qa.Action == QbAction.ReadOption) minHandoff = Math.Max(minHandoff, 0.58f);   // E-154: the quarterback rides the mesh and reads the end
            var hca = tgt.Assign as CarryAssignment;
            if (hca != null && hca.Style == CarryStyle.OutsideZone) minHandoff = _shotgun ? SimTuning.OutsideZoneHandoffSg : SimTuning.OutsideZoneHandoffUc;   // v0.10.0: the outside-zone exchange is a quick flip (the back must beat the force defender to the edge)
            if (hca != null && S.OffPlay.Category == PlayCategory.Draw)
            {
                // the draw: drop as if to pass, then give it late
                minHandoff = hca.Delay + 0.05f;
                if (S.Time < minHandoff - 0.15f) { q.Want = new Vec2(0f, -1f) * 1.8f; q.FaceDir = new Vec2(0f, 1f); }
            }
            if (S.Time >= minHandoff && (dist < 1.3f || closestPassed))
            {
                bool give = true;
                if (_qa.Action == QbAction.ReadOption) give = OptionReadGive(q);
                if (give)
                {
                    S.Handoff(q, tgt);
                    q.QbState = QbPhase.Done;
                    return;
                }
                // keep: run the option path
                q.QbState = QbPhase.Done;
                S.LogEvent(SimEventType.CarrierMove, q.Id, -1, TackleType.None, 0f, "OptionKeep");
                return;
            }
            if (S.Time > 1.6f) q.QbState = QbPhase.Done; // broken mesh: the QB keeps it
        }

        private bool OptionReadGive(SimAgent q)
        {
            int dir = _qa.RollDirection >= 0 ? 1 : -1;
            // the unblocked end man on the option side
            SimAgent key = null; float bestScore = float.MinValue;
            for (int j = 11; j < 22; j++)
            {
                SimAgent d = S.Agents[j];
                if (!(d.IsDefensiveLineman || PositionGroup.IsLinebacker(d.PlayPos))) continue;
                float side = (d.Pos.X - q.Pos.X) * dir;
                if (side < 2f) continue;
                float score = side + (d.State == AgentState.Engaged ? -6f : 0f) - Math.Abs(d.Pos.Y - S.LosY) * 0.3f;
                if (score > bestScore && side < 14f) { bestScore = score; key = d; }
            }
            if (key == null) return true;
            // v0.9.0 (E-154): the read is the end man's displacement over the last 0.3 s (does he squeeze the back or sit), not one frame of his velocity
            Vec2 disp = key.Pos - key.PosAgo(0.3f);
            bool crashed = disp.X * dir < -0.30f || (key.Pos.X - q.Pos.X) * dir < 2.4f;
            bool keep = crashed; // end crashes down on the back: pull it and run outside; end stays home: give
            // v0.10.0: the quarterback keeps only when the edge is really open (the lane outside the crashed end), not into a flowing linebacker and a corner with force
            if (keep) { float lopen, lval; LaneFinder.Best(S, q, new Vec2(dir * 0.8f, 0.6f), 2.0f, 9f, Vec2.Zero, out lopen, out lval); if (lopen * (q.Body.VMax / 10.5f) < SimTuning.OptionKeepOpen) keep = false; }
            if (keep && S.Rng.NextFloat() < 0.55f * (1f - SimMath.Rating01(q.R.Speed))) keep = false; // a slow quarterback hands it off anyway
            if (S.Rng.NextFloat() < (1f - SimMath.Rating01(q.R.Awareness)) * 0.06f) keep = !keep;
            return !keep;
        }

        private void QbFake(SimAgent q)
        {
            q.Intent = AgentAction.Handoff;
            q.HasFace = true;
            Vec2 toward = _fakeTarget != null ? _fakeTarget.Pos - q.Pos : new Vec2(0f, 1f);   // QA-372: a fake with no runner to sell to faces downfield, never his own end zone
            q.FaceDir = toward.LenSq() > 0.04f ? toward.Norm() : new Vec2(0f, 1f);
            q.Want = toward.LenSq() > 0.04f ? toward.Norm() * 1.4f : Vec2.Zero;      // v0.9.0 QBSTANCE: a step toward the runner he fakes to
            S.PlayAction(q, AgentAction.Handoff, 0.2f);
            float fakeTime = _shotgun ? 0.32f : 0.45f;
            if (S.Time >= fakeTime) { q.QbState = QbPhase.Dropback; }
        }

        // ---------------------------------------------------------------- dropback

        private void QbDropback(SimAgent q)
        {
            if (!q.HasBall) { q.QbState = QbPhase.Done; return; }
            q.Intent = AgentAction.Run;
            if (_dropT0 < 0f) { _dropT0 = S.Time; _dropFrom = q.Pos; }
            Vec2 to = _dropPoint - q.Pos;
            float dist = to.Length;
            if (_qa.DropbackSteps <= 0 && !_rolling) { EnterSet(q); return; }
            // v0.9.0 (E-075 / E-087 / E-095): the drop is TIME-parametrised: he arrives at the drop point after the planned duration (3 steps 1.2 s, 5 steps 1.75 s, 7 steps 2.3 s under centre;
            // a shotgun quarterback takes a gather step and a short retreat), not whenever his legs get him there
            float el = S.Time - _dropT0;
            float remaining = _dropDur - el;
            if ((remaining <= 0f && dist < 1.3f) || (dist < 0.40f && el >= _dropDur * 0.9f) || q.QbClock > 3.8f) { EnterSet(q); return; }
            Vec2 dir = to.Norm();
            float cap = q.Body.EffectiveVMax * (_rolling ? 0.97f : 0.92f);
            // trapezoid speed profile along the drop: 0.25 s to get going, cruise, and a 0.3 s settle onto the drop point (the plant)
            float total = Math.Max(0.5f, (_dropPoint - _dropFrom).Length);
            float cruise = total / Math.Max(0.35f, _dropDur - 0.2f);
            float vUp = cruise * SimMath.Clamp01(0.35f + el / 0.18f);
            float vDown = Math.Max(0.8f, dist * 4.2f);
            float v = Math.Min(cap, Math.Min(cruise * 1.1f, Math.Min(vUp, vDown)));
            if (remaining < 0.3f && dist > 1.2f) v = Math.Min(cap, Math.Max(v, dist / Math.Max(0.12f, remaining)));   // late: catch up
            if (!_rolling) QbDropFacing(q, dir);      // v0.9.0 QBSTANCE: reverse pivot under centre
            q.Want = dir * v;
            // quick game may throw on the drop
            PressureCheck(q);
            if (_rolling && q.QbClock > 0.7f) HandleReads(q, false);
        }

        private void EnterSet(SimAgent q)
        {
            q.QbState = QbPhase.Set;
            q.QbSetAt = S.Time;
            if (!_rolling || q.ReadIdx == 0) q.ReadIdx = 0;   // a rolling quarterback keeps the progression he already worked through
            q.ReadTimer = 0.05f + (_rolling || _hotMode ? 0f : QbHitchSeconds);   // v0.9.0 (E-072): the hitch at the top of the drop
            q.QbLastAdvance = S.Time;
        }

        /// <summary>The plant / hitch at the top of the drop before the first read can become a throw (seconds).</summary>
        public const float QbHitchSeconds = 0.12f;


        // ---------------------------------------------------------------- in the pocket

        private void QbSet(SimAgent q)
        {
            if (!q.HasBall) { q.QbState = QbPhase.Done; return; }
            q.Intent = AgentAction.Idle;
            q.HasFace = true;
            q.FaceDir = QbSetFacing(q);      // v0.9.0 QBSTANCE: shoulders follow the read
            // drift to a clean spot / step up away from edge pressure
            Vec2 drift = PocketDrift(q);
            // v0.9.0 (E-092): real pocket evasion. When a rusher closes (pressure above 0.6) the passer steps: a side-step away from the nearest closing man (or a step up if both edges are home),
            // 0.35 s at 3.5 yd/s with a 1.2 s cooldown; the 2.6 yd/s drift cannot get away from a rusher at 8 yd/s.
            if (!_rolling && !S.Options.QbHoldsBall && S.Time >= _evadeCool && S.Time >= _evadeUntil && q.Body.StumbleTime <= 0f && PocketPressure(q) > 0.60f)
            {
                SimAgent nr = null; float nd = 5.5f;
                for (int j = 11; j < 22; j++)
                {
                    SimAgent d = S.Agents[j];
                    if (d.State == AgentState.Down || d.Body.FallenTime > 0f || (d.State == AgentState.Engaged && (d.Engagement == null || d.Engagement.Lev > -0.5f))) continue;
                    float dd = (d.Pos - q.Pos).Length;
                    if (dd < nd) { nd = dd; nr = d; }
                }
                if (nr != null)
                {
                    Vec2 away = (q.Pos - nr.Pos).Norm();
                    Vec2 perp = away.Perp();
                    float sgn = perp.X >= 0f ? 1f : -1f;
                    if ((sgn > 0f && q.Pos.X > FieldGeometry.Width - 5f) || (sgn < 0f && q.Pos.X < 5f)) sgn = -sgn;
                    Vec2 dir = nd < 2.6f && Math.Abs(nr.Pos.X - q.Pos.X) < 1.6f ? new Vec2(sgn * 0.6f, 0.8f).Norm() : new Vec2(sgn, 0.25f).Norm();   // a man on top of him: step up and out; at an angle: slide off
                    if (q.Pos.Y < S.LosY - 8.5f) dir = new Vec2(sgn, 0f);
                    _evadeDir = dir; _evadeUntil = S.Time + 0.35f; _evadeCool = S.Time + 1.2f;
                    S.LogEvent(SimEventType.CarrierMove, q.Id, nr.Id, TackleType.None, 0f, "Sidestep");
                }
            }
            if (S.Time < _evadeUntil) drift = _evadeDir * 3.5f;
            q.Want = QbSetMotion(q, drift);
            HandleReads(q, false);
        }

        private Vec2 PocketDrift(SimAgent q)
        {
            // slide laterally away from nearby edge rushers; step up when they have gone wide
            Vec2 push = Vec2.Zero;
            for (int j = 11; j < 22; j++)
            {
                SimAgent d = S.Agents[j];
                if (d.State == AgentState.Down || d.Body.FallenTime > 0f) continue;
                Vec2 rel = q.Pos - d.Pos;
                float dd = rel.Length;
                if (dd < 4.5f && dd > 0.1f && (d.State != AgentState.Engaged || (d.Engagement != null && d.Engagement.Lev < -0.5f)))
                    push = push + rel.Norm() * ((4.5f - dd) / 4.5f);
            }
            // climb the pocket: rushers are winning around the outside while the middle is still clean, so the passer steps up (never past 2.5 yd behind the line)
            bool edgeThreat = false, interiorThreat = false;
            for (int j = 11; j < 22; j++)
            {
                SimAgent d = S.Agents[j];
                if (d.State == AgentState.Down || d.Body.FallenTime > 0f) continue;
                float dx = Math.Abs(d.Pos.X - q.Pos.X), dyAhead = d.Pos.Y - q.Pos.Y;
                bool losing = d.State != AgentState.Engaged || (d.Engagement != null && d.Engagement.Lev < -0.3f);
                if (dx > 2.2f && dx < 7f && dyAhead > -3.5f && dyAhead < 4.5f && (d.Pos - q.Pos).Length < 6.8f && losing) edgeThreat = true;
                else if (dx <= 2.2f && dyAhead > 0f && dyAhead < 3.8f && losing) interiorThreat = true;
            }
            if (edgeThreat && !interiorThreat && !_rolling && q.Pos.Y < S.LosY - 3.2f && q.QbClock > 1.0f)
                push = push * 0.5f + new Vec2(0f, 1.1f);
            // sliding away from a rusher is mostly lateral: a rusher in front no longer drives him straight back into the safeties
            if (push.Y < 0f) push = new Vec2(push.X, push.Y * 0.25f);
            if (q.Pos.Y < S.LosY - 9f && push.Y < 0f) push = new Vec2(push.X, 0f);
            if (push.LenSq() < 0.01f) return Vec2.Zero;
            float mag = Math.Min(1f, push.Length);
            return push.Norm() * (2.6f * mag * (0.5f + 0.5f * q.Body.Agility01));
        }

        private void PressureCheck(SimAgent q)
        {
            // a QB on the drop who is about to be sacked bails out of the plan
            float p = PocketPressure(q);
            if (p > 0.92f && q.QbClock > 0.5f && !_rolling) { EnterSet(q); }
        }

        // ---------------------------------------------------------------- reads and decisions

        private float EarliestThrow(SimAgent r)
        {
            SimAgent q = _qb;
            if (_qa.Action == QbAction.ScreenPass) return 0.9f;
            var ra = r.Assign as RouteAssignment;
            if (ra == null || ra.Waypoints.Count == 0) return 0.6f;
            // v0.9.0 (E-100 / E-101): the earliest decision is the later of (a) the receiver's break, (b) a floor after the plant at the top of the drop and (c) an absolute floor
            // for the kind of drop (a shotgun slant used to leave the hand at 0.75 s, a vertical at 1.5 s)
            int steps = _qa.DropbackSteps;
            float tCut = -1f;
            Vec2 prev = new Vec2(S.BallX0 + S.OffForm.Slots[r.Slot].X, S.LosY + S.OffForm.Slots[r.Slot].Y);
            for (int i = 0; i + 1 < ra.Waypoints.Count; i++)
            {
                Vec2 a = S.RouteRel(ra.Waypoints[i].Point);
                Vec2 b = S.RouteRel(ra.Waypoints[i + 1].Point);
                float ang = SimMath.AngleBetween((a - prev).Norm(), (b - a).Norm());
                if (ang > 0.6f) { tCut = ra.Waypoints[i].Time; break; }
                prev = a;
            }
            Vec2 lastPt = S.RouteRel(ra.Waypoints[ra.Waypoints.Count - 1].Point);
            bool deep = lastPt.Y - S.LosY >= 20f;
            float baseT;
            if (tCut < 0f) baseT = Math.Min(2.4f, ra.Waypoints[ra.Waypoints.Count - 1].Time * 0.30f);
            else baseT = tCut;
            float floor = _shotgun ? 1.05f : 1.25f;                       // absolute floor: quick game
            if (deep) floor = Math.Max(floor, steps <= 3 ? 1.75f : (steps <= 5 ? 2.10f : 2.40f));   // a vertical shot waits for the drop and the route
            float afterSet = q.QbSetAt > 0f ? q.QbSetAt + QbHitchSeconds : 0f;
            float t = Math.Max(Math.Max(baseT * 1.0f - 0.05f, floor), afterSet);
            return SimMath.Clamp(t, 0.55f, 3.4f);
        }

        /// <summary>How long (sim seconds from the snap) the quarterback will hold the ball before taking what is there: longer drops hold longer.</summary>
        private float HoldMax(SimAgent q, bool hot)
        {
            float aw = SimMath.Rating01(q.R.Awareness);
            int steps = _qa != null ? _qa.DropbackSteps : 3;
            float baseT = steps <= 3 ? 2.15f : (steps <= 5 ? 2.65f : 3.05f);   // v0.9.0 (E-354): longer drops hold longer, but the quick game no longer sits for 2.5 s
            if (_qa != null && _qa.Action == QbAction.PlayActionFake) baseT -= 0.8f; // the fake already burned half a second
            if (_shotgun) baseT += 0.30f;   // v0.9.0: the shotgun passer stands 5-6 yd deep and the rush needs a second longer to reach him, so he can afford to hold the ball to the same 2.5-3.0 s a seven-step passer does
            float h = (hot ? Math.Min(baseT - 0.25f, 2.45f) : baseT) + 0.4f * (aw - 0.5f) + (_rolling ? 0.5f : 0f);
            return Math.Max(h, q.QbSetAt + (hot ? 0.6f : (_qa != null && _qa.Action == QbAction.PlayActionFake ? 0.3f : 0.85f)));
        }

        private float Threshold(SimAgent q, int idx, float press)
        {
            float aw = SimMath.Rating01(q.R.Awareness);
            float t0 = 0.70f - 0.12f * aw + SimTuning.QbPatience;
            float hm = HoldMax(q, _hotMode);
            float decay = SimMath.Clamp01((S.Time - 1.2f) / Math.Max(0.5f, hm - 1.2f));
            float th = t0 - 0.30f * decay - 0.30f * press - 0.02f * idx;
            return Math.Max(SimTuning.QbMinThreshold, th);
        }

        /// <summary>How much easier (openness threshold) and how much more attractive (ranking) a hot-routed receiver is to the quarterback.</summary>
        public const float HotReadEdge = 0.22f, HotReadBonus = 0.34f;

        /// <summary>True for a receiver the user gave a hot route at the line.</summary>
        public static bool IsHotRead(SimAgent r)
        {
            var ra = r != null ? r.Assign as RouteAssignment : null;
            return ra != null && ra.IsHot;
        }

        private void HandleReads(SimAgent q, bool onRun)
        {
            q.ReadTimer -= PlaySimulation.Dt;
            if (S.Options.QbHoldsBall) return;
            if (q.ReadTimer > 0f) return;
            q.ReadTimer = onRun ? 0.16f : 0.10f;
            if (_reads.Count == 0) return;
            float press = PocketPressure(q);
            float aw = SimMath.Rating01(q.R.Awareness);

            if (_qa.Action == QbAction.HailMary)
            {
                // v0.9.0 (E-353): the Hail Mary is thrown at 3.2-3.6 s (the passer buys time in the pocket), into the middle of the group of receivers (the one nearest their centroid), not to the deepest man at a fixed 2.8 s
                float hold = 3.2f + 0.4f * DefenseAI.Hash01(q.Id, 5);
                if (S.Time >= hold || press > 0.75f)
                {
                    Vec2 cen = Vec2.Zero; int n = 0;
                    for (int i = 0; i < _reads.Count; i++) { cen = cen + S.Agents[_reads[i]].Pos; n++; }
                    if (n > 0) cen = cen * (1f / n);
                    SimAgent best = null; float bd = float.MaxValue;
                    for (int i = 0; i < _reads.Count; i++)
                    {
                        SimAgent r = S.Agents[_reads[i]];
                        float dd = (r.Pos - cen).Length - 0.05f * r.Pos.Y;
                        if (dd < bd) { bd = dd; best = r; }
                    }
                    if (best != null) BeginThrow(q, best, ThrowStyle.Lob, Vec2.Zero);
                }
                return;
            }

            if (_qa.Action == QbAction.ScreenPass && ScreenReceiver != null)
            {
                // the screen is a timed throw: the line has released and the receiver has turned around
                SimAgent sr = ScreenReceiver;
                // v0.9.0 (E-352): the screen is thrown when the back has turned and is within 3.5 yd of his catch point and the line has released, not on a fixed clock
                var sra = sr.Assign as RouteAssignment;
                Vec2 catchPt = sra != null && sra.Waypoints.Count > 0 ? S.RouteRel(sra.Waypoints[sra.Waypoints.Count - 1].Point) : sr.Pos;
                bool backReady = (sr.Pos - catchPt).Length < 3.5f && sr.RouteClock > 0.35f;
                bool ready = (S.Time >= 0.95f && backReady) || S.Time >= 1.9f || (press > 0.45f && S.Time > 0.7f && sr.RouteClock > 0.3f);
                if (ready && sr.State != AgentState.Down) { q.PendingOpen = 0.7f; BeginThrow(q, sr, ThrowStyle.Touch, Vec2.Zero); }
                return;
            }


            bool hot = _hotMode;
            // v0.9.0 (E-325): the passer works his progression one read at a time; he only scans the whole field when the rush is hot, the pocket is breaking, or he has worked through every read
            bool exhaustedReads = q.ReadIdx >= _reads.Count - 1;
            int upTo = (hot || exhaustedReads || press > SimTuning.ScanPressure || S.Time > HoldMax(q, hot) - 0.35f) ? _reads.Count - 1 : Math.Min(q.ReadIdx, _reads.Count - 1);   // (the checkdowns come into view when the rush or the clock closes in)
            int pick = -1; ThrowStyle pickStyle = ThrowStyle.Auto; float pickOpen = -1f; float pickScore = -9f;
            float bestOpen = -1f; int bestIdx = -1; ThrowStyle bestStyle = ThrowStyle.Auto;
            for (int i = 0; i < _reads.Count; i++)
            {
                SimAgent r = S.Agents[_reads[i]];
                bool hotRead = IsHotRead(r);   // v0.4: the receiver the user hot-routed is read from the snap on, whatever the progression says
                if (i > upTo && !hotRead) continue;
                if (r.State == AgentState.Down) continue;
                if (r.Slot >= 0 && r.Slot < _blockedUntil.Length && S.Time < _blockedUntil[r.Slot]) continue;   // he just pumped off this man
                float earliest = EarliestThrow(r);
                if (hot) earliest = Math.Min(earliest, 0.85f);
                if (S.Time < earliest && press < 0.42f) continue;
                Vec3 aim; float tf; ThrowStyle st;
                float open = EvalReceiver(q, r, out aim, out tf, out st);
                if (open < 0f) continue;
                // hot throws must be quick: the ball can't take forever to arrive
                if (hot && tf > 1.25f) open -= 0.25f;
                // misjudgement noise for low-awareness quarterbacks
                open += S.Rng.Gaussian(0f, 0.16f * (1f - aw));
                if (open > bestOpen) { bestOpen = open; bestIdx = i; bestStyle = st; }
                float th = Threshold(q, i, press) - (hot ? 0.10f : 0f) - (hotRead ? HotReadEdge : 0f);
                float rank = open - 0.05f * i + (hotRead ? HotReadBonus : 0f);
                if (open >= th && (pick < 0 || rank > pickScore)) { pick = i; pickStyle = st; pickOpen = open; pickScore = rank; }
            }
            if (pick >= 0)
            {
                q.PendingOpen = pickOpen;
                BeginThrow(q, S.Agents[_reads[pick]], pickStyle, Vec2.Zero);
                return;
            }

            // advance the progression
            float dwell = 0.32f + 0.26f * (1f - aw);
            if (IsHotRead(S.Agents[_reads[Math.Min(q.ReadIdx, _reads.Count - 1)]]) && press < 0.5f) dwell += 0.45f;   // stay on the hot read a beat longer
            float curEarliest = EarliestThrow(S.Agents[_reads[Math.Min(q.ReadIdx, _reads.Count - 1)]]);
            if (q.ReadIdx < _reads.Count - 1 && S.Time - Math.Max(q.QbLastAdvance, curEarliest) >= dwell)
            {
                q.ReadIdx++;
                q.QbLastAdvance = S.Time;
            }
            bool exhausted = q.ReadIdx >= _reads.Count - 1;

            float holdMax = HoldMax(q, hot);
            if (!onRun)
            {
                // v0.10.0: a covered quarterback holds the ball. He takes a window that is really there (0.16-0.26 open, by awareness); with nobody open he sits on it for a beat longer (0.20-0.75 s, the sack the
                // offense gave up by hanging on) before he scrambles, throws it away or takes the sack. Under a hit he still dumps it to anything that is not dead covered.
                float takeMin = SimTuning.TakeMin + 0.10f * aw;
                bool shot = bestIdx >= 0 && S.Agents[_reads[bestIdx]].Pos.Y - S.LosY >= SimTuning.ShotDepth;   // a vertical route: he takes the shot into leverage (a jump ball), he does not throw the deep ball away
                bool haveTake = bestIdx >= 0 && bestOpen > (shot ? SimTuning.ShotMin : takeMin);
                float coverHold = hot ? 0f : SimTuning.CoverHold + 0.55f * (1f - aw);
                bool timeUp = S.Time > holdMax + (haveTake ? 0f : coverHold);
                bool bail = press >= 0.50f || timeUp;
                if (!bail) return;
                // 1) take what is there when the clock or the rush says so
                if (bestIdx >= 0 && ((haveTake && (S.Time > holdMax || press > 0.50f)) || (bestOpen > 0.04f && press > 0.85f)))
                {
                    q.PendingOpen = bestOpen;
                    BeginThrow(q, S.Agents[_reads[bestIdx]], bestStyle, Vec2.Zero);
                    return;
                }
                // 2) leave the pocket
                if (!hot && (press < 0.55f || q.R.Speed >= 80) && TryStartScramble(q, press)) return;
                // 3) live to play another down
                if (aw > 0.35f && S.State.Down < 4 && (press >= 0.50f || timeUp)) { ThrowAway(q); return; }   // on fourth down nobody throws it away
                if (bestIdx >= 0 && bestOpen > 0.2f && S.Time > holdMax + coverHold + 0.6f) BeginThrow(q, S.Agents[_reads[bestIdx]], bestStyle, Vec2.Zero);
            }
            else
            {
                if (q.Pos.Y > S.LosY - 0.3f) return;
                if (bestIdx >= 0 && bestOpen > 0.45f && S.Time > holdMax + 0.5f) BeginThrow(q, S.Agents[_reads[bestIdx]], bestStyle, Vec2.Zero);
            }
        }

        /// <summary>Scores a throw to receiver r (0 = covered, 1 = wide open) and plans its trajectory.</summary>
        public float EvalReceiver(SimAgent q, SimAgent r, out Vec3 aim, out float tf, out ThrowStyle style)
        {
            float o = EvalReceiver(q, r, false, out aim, out tf, out style);
            if (r.Slot >= 0 && r.Slot < _loftFor.Length) _loftFor[r.Slot] = false;
            // v0.9.0 (E-103): a covered intermediate target is tried again with a touch pass over the underneath defender (a real arc, slower ball, higher apex)
            Vec2 dq = r.Pos - q.Pos;
            if (o < 0.62f && style == ThrowStyle.Bullet && dq.Length > 12f && dq.Length < 32f)
            {
                Vec3 aim2; float tf2; ThrowStyle st2;
                float o2 = EvalReceiver(q, r, true, out aim2, out tf2, out st2);
                if (o2 > o + 0.08f) { aim = aim2; tf = tf2; style = ThrowStyle.Touch; if (r.Slot >= 0 && r.Slot < _loftFor.Length) _loftFor[r.Slot] = true; return o2; }
            }
            return o;
        }

        private float _hurried;
        private float EvalReceiver(SimAgent q, SimAgent r, bool loft, out Vec3 aim, out float tf, out ThrowStyle style)
        {
            Vec3 from = new Vec3(q.Pos, 1.9f);
            float windup = 0.26f - 0.06f * SimMath.Rating01(q.R.Awareness);   // the same constant BeginThrow uses
            float t = 0.9f;
            Vec2 T = r.Pos;
            aim = new Vec3(T, 1.6f);
            tf = 0.9f;
            style = ThrowStyle.Auto;
            float vBoost = ThrowBoost(q);
            int power = q.R.ThrowPower;
            bool feasible = true;
            Vec3 lastVel = Vec3.Zero;
            // pick the style once from where the receiver is now, so deep throws do not spiral into ever-longer lobs
            Vec2 T0 = ClampField(PredictPos(r, 1.0f + windup));
            style = loft ? ThrowStyle.Touch : ChooseStyle((T0 - q.Pos).Length, power);
            for (int it = 0; it < 3; it++)
            {
                T = ClampField(PredictPos(r, t + windup));
                float d = (T - q.Pos).Length;
                // style fixed above
                bool high;
                float speed = StyleSpeed(style, d, power, out high, vBoost, loft);
                T = AimOffset(q, r, T);
                aim = new Vec3(T, AimHeight(r, d, high));
                Vec3 vel;
                feasible = BallPhysics.SolveLaunch(from, aim, speed, high, out vel, out tf);
                lastVel = vel;
                t = tf;
            }
            if (!feasible || tf > 3.9f) return -1f;
            // sampled height of the planned flight at each fraction of its horizontal run (for lane checks)
            for (int k = 0; k < _zAt.Length; k++) _zAt[k] = 0f;
            {
                Vec3 bp = from; Vec3 bv = lastVel;
                float total = Math.Max(0.01f, (aim.XY - from.XY).Length);
                for (int step = 0; step < 220; step++)
                {
                    BallPhysics.Advance(ref bp, ref bv, 0.02f);
                    float fr = (bp.XY - from.XY).Length / total;
                    int idx = (int)(fr * (_zAt.Length - 1));
                    if (idx >= _zAt.Length) break;
                    if (idx >= 0) _zAt[idx] = bp.Z;
                    if (fr > 1.02f) break;
                }
                for (int k = 1; k < _zAt.Length; k++) if (_zAt[k] <= 0f) _zAt[k] = _zAt[k - 1];
            }
            float tArrive = tf + windup;
            Vec2 qpos = q.Pos;
            float maxThreat = 0f;
            for (int j = 11; j < 22; j++)
            {
                SimAgent d = S.Agents[j];
                if (d.State == AgentState.Down || d.Body.FallenTime > 0.6f) continue;
                Vec2 dp;
                if (d.CoverTarget == r.Id) dp = T + (d.Pos - r.Pos) * 0.85f;
                else dp = d.Pos + d.Vel * (Math.Min(tArrive, 0.6f) * 0.6f);
                float reachD = 0.95f + d.Body.HeightYd * 0.45f;
                float dDist = Math.Max(0f, (dp - T).Length - reachD);
                float react = 0.22f + (1f - SimMath.Rating01(d.R.Awareness)) * 0.26f + (d.State == AgentState.Engaged ? 0.9f : 0f);
                float tD = dDist / (d.Body.VMax * 0.88f) + react;
                float rDist = Math.Max(0f, (T - r.Pos).Length - 1.3f);
                float tR = Math.Min(tArrive, rDist / Math.Max(3f, r.Body.VMax * 0.9f));
                float delta = tD - Math.Max(tR, tArrive - 0.12f);
                float threat = SimMath.Clamp01((0.22f - delta) / 0.55f);
                // where the defender is relative to the ball's line: between the passer and the catch point is dangerous, trailing is not
                Vec2 uBall = (qpos - T).Norm();
                float front = SimMath.Clamp((dp - T).Dot(uBall), -1.5f, 1.5f) / 1.5f;
                float posW = 0.34f + 0.66f * SimMath.Clamp01((front + 0.45f) / 1.2f);
                threat *= posW;
                float sepNow = (dp - T).Length;
                float proximity = SimMath.Clamp01((2.4f - sepNow) / 1.8f) * (0.25f + 0.75f * SimMath.Clamp01(front * 0.5f + 0.5f)) * (d.State == AgentState.Engaged ? 0.4f : 1f);
                threat = Math.Max(threat, 0.95f * proximity);
                threat *= PerceivedResponsibility(q, d, T, r.Id);
                // lane threat: defenders that can step into the throwing lane
                Vec2 laneP = SimMath.ClosestOnSegment(qpos, T, dp, out float frac);
                float laneD = (laneP - dp).Length;
                if (frac > 0.12f && frac < 0.98f && laneD < 2.2f)
                {
                    float tBall = windup + tf * frac;
                    float tLane = d.State == AgentState.Engaged ? Math.Max(0f, laneD - 1.2f) / 2.0f + 0.1f : Math.Max(0f, laneD - 0.9f) / (d.Body.VMax * 0.85f) + react * 0.8f;
                    float ballZ = _zAt[Math.Min(_zAt.Length - 1, (int)(frac * (_zAt.Length - 1)))];
                    if (ballZ < d.Body.HeightYd * 1.5f + 0.8f)
                    {
                        float lane = SimMath.Clamp01((tBall + 0.12f - tLane) / 0.5f) * SimMath.Clamp01((2.2f - laneD) / 1.4f);
                        if (d.State == AgentState.Engaged) lane *= 0.38f; // linemen locked in blocks only occasionally bat a pass
                        if (lane > threat) threat = lane * 0.9f;
                    }
                }
                if (threat > maxThreat) maxThreat = threat;
            }
            float open = 1f - maxThreat;
            // targets near a sideline are worth a little less
            float toLine = FieldGeometry.DistanceToSideline(T.X);
            if (toLine < 1.5f) open -= 0.12f;
            // pass distance past the passer's comfortable range
            if ((T - q.Pos).Length > BallPhysics.MaxThrowSpeed(power) * 2.1f) open -= 0.25f;
            return open;
        }

        /// <summary>
        /// v0.9.0 (E-325): what the passer reads of a defender's job from behind the line. A rusher he sees for what he is; a coverage man's assignment is a read that costs film study (Awareness):
        /// the sharp passer sees the true responsibility, the green one only sees who is standing near the throw.
        /// </summary>
        private float PerceivedResponsibility(SimAgent q, SimAgent d, Vec2 T, int rid)
        {
            float actual = S.DefAI.Responsibility(d, T, rid);
            if (d.Assign is RushAssignment || d.Assign is BlitzAssignment) return actual;
            float guess = (d.Pos - T).Length < 4.5f ? 0.85f : 0.30f;
            float k = 0.40f + 0.60f * SimMath.Rating01(q.R.Awareness);
            return SimMath.Lerp(guess, actual, k);
        }

        private static Vec2 ClampField(Vec2 p)
        {
            return new Vec2(SimMath.Clamp(p.X, 0.9f, FieldGeometry.Width - 0.9f), SimMath.Clamp(p.Y, -3f, 112f));
        }

        public static ThrowStyle ChooseStyle(float d, int power)
        {
            if (d < 9f) return ThrowStyle.Touch;
            if (d < 27f) return ThrowStyle.Bullet;
            if (d < 58f) return ThrowStyle.Touch;
            return ThrowStyle.Lob;
        }

        /// <summary>v0.8.0 Rocket Arm: extra launch velocity (yd/s) for this passer.</summary>
        private static float ThrowBoost(SimAgent q) { return q.Fx == null ? 0f : q.Fxv(SimHook.ThrowPower) * PlaySimulation.ThrowVelocityPerPoint; }

        /// <summary>
        /// Launch speed (yd/s) for a pass style over horizontal distance d. v0.9.0 (E-102 / E-103 / E-113 / E-117): ONE monotone curve fitted to NFL flight times
        /// (10 yd 0.7 s, 20 yd 1.25 s, 30 yd 1.85 s, 40 yd 2.35 s, 50 yd 2.9 s) with a real arc from 20 yd (40 yd: 34 degrees, 27 ft apex); a Bullet is about 12 percent faster
        /// and flatter and blends into the Touch arc over 21-27 yd, so the ball speed never steps backwards as the distance grows.
        /// </summary>
        public static float StyleSpeed(ThrowStyle style, float d, int power, out bool high, float boost = 0f, bool loft = false)
        {
            float vmax = BallPhysics.MaxThrowSpeed(power);
            float std = SimMath.Clamp(12.2f + 0.30f * d, 12.5f, 40f);
            if (loft && style == ThrowStyle.Touch) std *= 0.86f;   // the touch pass over the linebacker: 35-40 degrees, 1.5 s at 18 yd        // the Touch pass: about 31 mph at 10 yd, 48 mph at 40 yd
            float minV = (float)Math.Sqrt(SimMath.Gravity * Math.Max(1f, d)) * 1.12f;
            high = false;
            switch (style)
            {
                case ThrowStyle.Bullet:
                {
                    float f = SimMath.Lerp(1.12f, 0.96f, SimMath.SmoothStep(21f, 27f, d));
                    return Math.Min(vmax, Math.Max(std * f, minV)) + boost;   // v0.8.0: boost = Rocket Arm velocity (flat and touch throws only; a lob's arc is set by gravity)
                }
                case ThrowStyle.Lob:
                {
                    if (d < 9f) return Math.Max(std * 0.85f, Math.Min(vmax, minV));
                    high = true;
                    float v = (float)Math.Sqrt(SimMath.Gravity * d / 0.94f) * 1.10f;
                    return SimMath.Clamp(v, 14f, vmax);
                }
                case ThrowStyle.Touch:
                default:
                {
                    float f = d < 14f ? 1.0f : SimMath.Lerp(1.0f, 0.96f, SimMath.SmoothStep(14f, 24f, d));   // over the linebacker: a little more arc from the mid range on
                    return Math.Min(vmax, Math.Max(std * f, minV)) + boost;
                }
            }
        }

        // ---------------------------------------------------------------- pressure

        /// <summary>A seven-step passer has stood in the pocket for 2.4 s and has seen the edge come (he feels it 0.55 x as late), a quick-game passer is looking at a short window and feels it 1.15 x as late.</summary>
        private float SenseScale()
        {
            int steps = _qa != null ? _qa.DropbackSteps : 3;
            return steps <= 3 ? SimTuning.SenseQuick : (steps <= 5 ? SimTuning.SenseFive : SimTuning.SenseSeven);
        }

        public float PocketPressure(SimAgent q)
        {
            float p = 0f;
            // the passer feels pressure a beat late: less aware quarterbacks see where the rusher was a few ticks ago
            float delay = 0.08f + SimTuning.SenseDelay * SenseScale() + Math.Max(0f, 0.6f - SimMath.Rating01(q.R.Awareness)) * 0.22f;   // v0.9.0 (E-324): only the green passer sees the rush late
            for (int j = 11; j < 22; j++)
            {
                SimAgent d = S.Agents[j];
                if (d.State == AgentState.Down || d.Body.FallenTime > 0f) continue;
                Vec2 seen = d.PosAgo(delay);
                Vec2 rel = seen - q.Pos;
                float dist = rel.Length;
                if (dist > 9f) continue;
                if (d.State == AgentState.Engaged)
                {
                    float lev = d.Engagement != null ? d.Engagement.Lev : 0f;
                    if (lev < -0.35f && dist < 3.6f) p = Math.Max(p, 0.35f + (-lev - 0.35f) * 0.6f);
                    continue;
                }
                float closing = Math.Max(0f, -d.Vel.Dot(rel.Norm()));
                float tTo = Math.Max(0f, dist - 1.0f) / Math.Max(2.5f, closing + 1.0f);
                p = Math.Max(p, SimMath.Clamp01(1f - tTo / 1.00f));   // v0.9.0 (E-324): a rusher 0.9 s from contact is felt (was 0.5 s, shorter than the wind-up)
            }
            return p;
        }

        // ---------------------------------------------------------------- scramble / throwaway

        private bool TryStartScramble(SimAgent q, float press)
        {
            if (q.Pos.Y > S.LosY + 0.5f) return false;
            float open, val;
            Vec2 d = LaneFinder.Best(S, q, Vec2.Zero, 0f, 9f, Vec2.Zero, out open, out val);
            float speedF = q.Body.VMax / 10.5f;
            bool ok = (open * speedF >= 7.0f && d.Y > 0.25f) || (press > 0.65f && open >= 4.5f && d.Y > 0.05f);
            if (!ok) return false;
            // quarterbacks that can't run don't leave a clean pocket
            if (press < 0.45f && (q.R.Speed < 78 || S.Time < 3.6f)) return false;
            q.QbState = QbPhase.Scramble;
            q.LastTargetDir = d;
            q.ReactionClock = 0f;
            S.LogEvent(SimEventType.Scramble, q.Id, -1);
            return true;
        }

        private float _scrambleStart = -1f;
        private float _evadeUntil = -1f, _evadeCool = -1f;
        private Vec2 _evadeDir;

        private void UpdateQbScramble()
        {
            SimAgent q = _qb;
            if (!q.HasBall) { q.QbState = QbPhase.Done; return; }
            if (S.PassThrown) { q.QbState = QbPhase.Done; return; }
            if (_scrambleStart < 0f) _scrambleStart = S.Time;
            if (q.Pos.Y > S.LosY + 0.3f) S.QbHasCrossedLos = true;
            float since = S.Time - _scrambleStart;
            // v0.9.0 (E-094): a computer quarterback who leaves the pocket works the line first: he runs laterally in a band 0.8-3.5 yd behind the line looking for a receiver (the receivers
            // are doing the scramble drill) for up to about 2 s, and only commits to the run when nobody comes open; he used to be a runner within a second and could only throw behind the line.
            bool hold = !q.ControlledByUser && !S.QbHasCrossedLos && S.Time < 3.6f && since < 2.1f && q.Pos.Y < S.LosY - 0.6f && _qb.R.Speed < 92;
            _scrambleHolding = hold; _scrambleHoldKnown = true;      // v0.11.0 QB: the escape work is the stance; the end of it is the takeoff
            if (hold)
            {
                float vmax = q.Body.EffectiveVMax;
                float room = Math.Min(q.Pos.X - 2.5f, FieldGeometry.Width - 2.5f - q.Pos.X);
                float side = Math.Abs(q.Vel.X) > 0.8f ? Math.Sign(q.Vel.X) : (q.Pos.X < FieldGeometry.CenterX ? -1f : 1f);
                // run away from the nearest closing rusher; never into the sideline
                Vec2 away = Vec2.Zero; float nd = 6f;
                for (int j = 11; j < 22; j++)
                {
                    SimAgent d = S.Agents[j];
                    if (d.State == AgentState.Down || d.Body.FallenTime > 0f) continue;
                    float dd = (d.Pos - q.Pos).Length;
                    if (dd < nd) { nd = dd; away = (q.Pos - d.Pos).Norm(); }
                }
                if (away.LenSq() > 0.25f && Math.Abs(away.X) > 0.25f) side = Math.Sign(away.X);
                if ((side < 0f && q.Pos.X < 6f) || (side > 0f && q.Pos.X > FieldGeometry.Width - 6f)) side = -side;
                float targetY = SimMath.Clamp(q.Pos.Y, S.LosY - 3.5f, S.LosY - 1.0f);
                if (away.Y > 0.3f) targetY = S.LosY - 3.5f;   // a rusher in front: give ground
                Vec2 want = new Vec2(side * vmax * 0.86f, SimMath.Clamp((targetY - q.Pos.Y) * 2.5f, -2.5f, 2.5f));
                q.Want = want;
                q.HasFace = true; q.FaceDir = QbStanceMoveFacing(new Vec2(side, 0f), 0.45f, 0.5f, 0.87f);   // v0.11.0 QB: square stance while he works across the line
                q.Intent = AgentAction.Run;
                HandleReads(q, true);
                // out of the box near a sideline with nobody open and a rusher on him: he throws it away (instead of running out of bounds)
                if (q.QbState == QbPhase.Scramble && since > 0.7f && room < 11f && nd < 3.6f && !_awayPending) ThrowAway(q);
                return;
            }
            _carrierAI.Update(q);
            q.Intent = AgentAction.Run;
            if (!S.QbHasCrossedLos && !q.ControlledByUser) HandleReads(q, true);
        }

        private Vec3 _awayAim;
        private bool _awayPending, _awayGrounding;
        private bool _pumpChecked;
        private float _windupTotal;
        private readonly float[] _blockedUntil = new float[11];
        private readonly bool[] _loftFor = new bool[11];

        /// <summary>
        /// The quarterback throws the ball away. v0.9.0 (E-104): it goes through the normal wind-up (the ball leaves at the release, not on the decision tick), so the throw-away
        /// animation and the sim release agree.
        /// </summary>
        public void ThrowAway(SimAgent q)
        {
            if (!q.HasBall || S.PassThrown || q.QbState == QbPhase.Windup) return;
            // pick the sideline nearest a receiver on the passer's side and put the ball out of bounds past him
            SimAgent near = null; float bd = float.MaxValue;
            for (int i = 1; i < 11; i++)
            {
                SimAgent r = S.Agents[i];
                if (!r.IsReceiverEligible) continue;
                float dd = FieldGeometry.DistanceToSideline(r.Pos.X) + Math.Abs(r.Pos.Y - (q.Pos.Y + 6f)) * 0.25f + Math.Abs(r.Pos.X - q.Pos.X) * 0.05f;
                if (dd < bd) { bd = dd; near = r; }
            }
            float sideX = q.Pos.X < FieldGeometry.CenterX ? -2.0f : FieldGeometry.Width + 2.0f;
            float ty = Math.Max(q.Pos.Y + 4f, S.LosY + 1.0f);
            if (near != null)
            {
                sideX = near.Pos.X < FieldGeometry.CenterX ? -1.5f : FieldGeometry.Width + 1.5f;
                ty = Math.Max(near.Pos.Y, S.LosY + 1.0f);
            }
            // do not throw it into the rusher: if a free defender stands on the line to that sideline, use the other one
            for (int j = 11; j < 22; j++)
            {
                SimAgent dj = S.Agents[j];
                if (dj.State == AgentState.Engaged || dj.State == AgentState.Down) continue;
                float tp;
                Vec2 cp = SimMath.ClosestOnSegment(q.Pos, new Vec2(sideX, ty), dj.Pos, out tp);
                if (tp > 0.05f && tp < 0.7f && (cp - dj.Pos).Length < 1.5f) { sideX = sideX < 0f ? FieldGeometry.Width + 1.5f : -1.5f; break; }
            }
            _awayAim = new Vec3(sideX, ty, 1.5f);
            _awayGrounding = false;
            // intentional grounding: in the pocket with no receiver anywhere near the throw
            if (Math.Abs(q.Pos.X - S.BallX0) < 7.5f && q.Pos.Y < S.LosY && q.Body.Speed < 3f)
            {
                float nearest = 99f;
                for (int i = 1; i < 11; i++) if (S.Agents[i].IsReceiverEligible) nearest = Math.Min(nearest, (S.Agents[i].Pos - _awayAim.XY).Length);
                _awayGrounding = nearest > 11f && S.Rng.Chance(0.35f);
            }
            _awayPending = true;
            q.ThrowTarget = -1;
            q.PendingStyle = ThrowStyle.Auto; q.PendingNudge = Vec2.Zero;
            StartWindup(q, (_awayAim.XY - q.Pos).Norm());
        }

        private void StartWindup(SimAgent q, Vec2 faceDir, SimAgent target = null)
        {
            q.WindupLeft = 0.26f - 0.06f * SimMath.Rating01(q.R.Awareness);
            if (S.Options.QbWindupSeconds > 0f) q.WindupLeft = S.Options.QbWindupSeconds * (q.QbState == QbPhase.Scramble ? 0.8f : 1f) * (1.04f - 0.08f * SimMath.Rating01(q.R.Awareness));      // v0.7.0: animation-synced wind-up
            _windupTotal = q.WindupLeft + (target != null ? QbWindupExtra(q, target, q.WindupLeft) : 0f);
            _pumpChecked = false;
            if (target != null) q.WindupLeft += QbWindupExtra(q, target, q.WindupLeft);      // v0.9.0 QBSTANCE: time to turn the body to the target
            q.PrevQbState = q.QbState;
            q.QbState = QbPhase.Windup;
            q.HasFace = true;
            q.FaceDir = faceDir;
            S.PlayAction(q, AgentAction.Throw, 0.55f);
        }

        private void LaunchThrowaway(SimAgent q)
        {
            Vec3 from = new Vec3(q.Pos + q.Facing * 0.25f, 1.9f);
            Vec3 aim = _awayAim;
            float d = (aim.XY - q.Pos).Length;
            bool high;
            float v = StyleSpeed(d > 22f ? ThrowStyle.Touch : ThrowStyle.Bullet, d, q.R.ThrowPower, out high, ThrowBoost(q));
            Vec3 vel; float tf;
            BallPhysics.SolveLaunch(from, aim, v, false, out vel, out tf);
            S.Ball.Launch(BallState.Pass, from, vel, q.Id);
            S.OnPassReleased(q, null, true);
            q.HasBall = false;
            q.QbState = QbPhase.Done;
            _awayPending = false;
            S.PlayAction(q, AgentAction.Throw, 0.5f);
            S.LogEvent(SimEventType.Throwaway, q.Id, -1);
            if (_awayGrounding) S.Flag(PenaltyType.IntentionalGrounding, q, q.Pos.Y - S.LosY);
        }

        // ---------------------------------------------------------------- throwing

        public void BeginThrow(SimAgent q, SimAgent target, ThrowStyle style, Vec2 nudge, bool user = false)
        {
            if (!q.HasBall || S.PassThrown || q.QbState == QbPhase.Windup) return;
            q.ThrowTarget = target.Id;
            q.PendingStyle = style;
            q.PendingNudge = nudge;
            _awayPending = false;
            _userThrow = user;
            _pendingLoft = !user && target.Slot >= 0 && target.Slot < _loftFor.Length && _loftFor[target.Slot];
            StartWindup(q, (target.Pos - q.Pos).Norm(), target);
        }

        private bool _userThrow, _pendingLoft;

        private void QbWindup(SimAgent q)
        {
            q.WindupLeft -= PlaySimulation.Dt;
            q.HasFace = true;
            if (_awayPending) q.FaceDir = (_awayAim.XY - q.Pos).Norm();
            else { SimAgent t0 = S.Agents[q.ThrowTarget]; q.FaceDir = (t0.Pos - q.Pos).Norm(); }
            // keep moving at reduced speed when the throw came off a scramble
            if (q.PrevQbState == QbPhase.Scramble || _rolling) q.Want = q.Vel * 0.9f;
            else
            {
                // v0.9.0 (E-108): the step into the throw. The throwing clips step the front foot forward about 0.3 m; the body now takes that step (about 1 yd/s in the last 60 percent of the wind-up) instead of
                // standing while the clip slides
                float stepFrac = _windupTotal > 0.05f ? 1f - q.WindupLeft / _windupTotal : 1f;
                q.Want = stepFrac > 0.4f && q.FaceDir.Y > -0.2f ? q.FaceDir * (0.30f / Math.Max(0.2f, _windupTotal * 0.6f)) : Vec2.Zero;
            }
            if (!q.HasBall) { q.QbState = QbPhase.Done; _awayPending = false; return; }
            // v0.9.0 (E-121): a committed (computer) throw is re-checked 60 percent of the way through the wind-up: if the window has closed he pumps instead of throwing it into coverage
            if (!_awayPending && !_userThrow && !_pumpChecked && q.WindupLeft <= _windupTotal * 0.40f && q.WindupLeft > 0.03f)
            {
                _pumpChecked = true;
                SimAgent t = S.Agents[q.ThrowTarget];
                Vec3 aim2; float tf2; ThrowStyle st2;
                float open = EvalReceiver(q, t, _pendingLoft, out aim2, out tf2, out st2);
                if (open >= 0f && open < 0.12f && q.Body.StumbleTime <= 0f && PocketPressure(q) < 0.35f)   // (no pump with a rusher on him: he throws it)
                {
                    _blockedUntil[t.Slot] = S.Time + 0.7f;
                    q.QbState = q.PrevQbState == QbPhase.Scramble ? QbPhase.Scramble : QbPhase.Set;
                    q.ReadTimer = 0.18f;
                    q.OverrideLeft = Math.Min(q.OverrideLeft, 0.25f);
                    S.LogEvent(SimEventType.CarrierMove, q.Id, t.Id, TackleType.None, 0f, "PumpFake");
                    return;
                }
            }
            if (q.WindupLeft <= 0f && (_awayPending || QbAligned(q) || q.WindupLeft < -0.2f))      // v0.9.0 QBSTANCE: the release is gated by the body
            {
                if (_awayPending) LaunchThrowaway(q); else ReleaseThrow(q);
            }
        }

        private float AimHeight(SimAgent r, float d, bool high)
        {
            if (high) return 1.95f;
            var ra = r.Assign as RouteAssignment;
            if (ra == null) return 1.6f;
            switch (ra.Route)
            {
                // v0.9.0 (E-120): a crosser gets it low and in stride, a vertical shot is thrown to the high point, a flat route gets it at the numbers
                case RouteKind.Cross: case RouteKind.Drag: case RouteKind.Shallow: case RouteKind.Texas: case RouteKind.Dig: case RouteKind.In: case RouteKind.Slant: return 1.4f;
                case RouteKind.Go: case RouteKind.Fade: case RouteKind.Seam: case RouteKind.Corner: case RouteKind.Post: case RouteKind.Corner2: case RouteKind.OutAndUp: case RouteKind.Wheel: return d > 26f ? 1.8f : 1.65f;
                case RouteKind.Flat: case RouteKind.Swing: case RouteKind.Bubble: case RouteKind.ScreenRelease: return 1.3f;
            }
            return 1.6f;
        }

        /// <summary>0.4 yd away from the nearest defender, perpendicular to the line of the throw (the ball goes to the receiver's far shoulder).</summary>
        private Vec2 AimOffset(SimAgent q, SimAgent r, Vec2 T)
        {
            SimAgent nd = null; float bd = 3.2f;
            for (int j = 11; j < 22; j++)
            {
                SimAgent d = S.Agents[j];
                if (d.State == AgentState.Down || d.Assign is RushAssignment) continue;
                float dd = (d.Pos - T).Length;
                if (dd < bd) { bd = dd; nd = d; }
            }
            if (nd == null) return T;
            Vec2 line = (T - q.Pos).Norm();
            if (line.LenSq() < 0.25f) return T;
            Vec2 perp = line.Perp();
            float side = (T - nd.Pos).Dot(perp) >= 0f ? 1f : -1f;
            return ClampField(T + perp * (side * 0.4f));
        }

        private void ReleaseThrow(SimAgent q)
        {
            float windScale0 = BallPhysics.WindScale; BallPhysics.WindScale = 0.55f; // the passer allows for about half of the wind
            SimAgent t = S.Agents[q.ThrowTarget];
            Vec3 from = new Vec3(q.Pos + q.Facing * 0.25f, 1.95f);
            float t_ = 0.9f;
            Vec3 aim = new Vec3(t.Pos, 1.6f), vel = Vec3.Zero;
            float tf = 0.9f;
            bool high = false;
            ThrowStyle style = q.PendingStyle;
            int power = q.R.ThrowPower;
            float vBoost = ThrowBoost(q);
            bool loft = _pendingLoft && !_userThrow;
            for (int it = 0; it < 3; it++)
            {
                Vec2 T = ClampField(PredictPos(t, t_));
                float d = (T - q.Pos).Length;
                if (style == ThrowStyle.Auto) style = ChooseStyle(d, power);
                float speed = StyleSpeed(style, d, power, out high, vBoost, loft);
                T = AimOffset(q, t, T) + q.PendingNudge;
                float z = AimHeight(t, d, high);
                aim = new Vec3(T, z);
                BallPhysics.SolveLaunch(from, aim, speed, high, out vel, out tf);
                t_ = tf;
            }

            // accuracy: error cone grows with distance, pressure, throwing on the run, and drops with ThrowAccuracy
            float press = PocketPressure(q);
            float dist = (aim.XY - q.Pos).Length;
            float accPts = 0f;
            if (q.Fx != null)
            {
                // v0.8.0: Cool Under Fire (a collapsing pocket) and Deep Touch (a throw 18+ yards downfield) on top of the accuracy already baked for the down and clock
                int dyn = 0;
                if (press >= PlaySimulation.UnderPressureAt) dyn |= PlaySimulation.CtxUnderPressure;
                if (aim.Y - q.Pos.Y >= 18f) dyn |= PlaySimulation.CtxDeep;
                accPts = q.FxOnly(SimHook.ThrowAccuracy, dyn & ~q.CtxMask) + q.Fxv(SimHook.DeepAccuracy, dyn);
            }
            float acc01 = SimMath.Rating01(q.R.ThrowAccuracy + (int)Math.Round(accPts, MidpointRounding.AwayFromZero));
            float sd = 0.125f - 0.095f * acc01;
            float mult = 1f + 0.9f * press;
            mult *= 1f + 0.55f * SimMath.Clamp01(q.Speed / Math.Max(1f, q.Body.VMax));
            if (style == ThrowStyle.Lob) mult *= 0.85f; else if (style == ThrowStyle.Touch) mult *= 0.92f;
            if (dist > 30f) mult *= 1.12f;
            if (q.Body.StumbleTime > 0f) mult *= 1.4f;
            if (_hurried > 0f) mult *= 1.25f;   // v0.9.0 (E-234): a quarterback who was hit or hurried before the throw is less accurate
            float err = sd * mult;
            Vec2 dirH = (aim.XY - q.Pos).Norm();
            Vec2 lat = dirH.Perp();
            float eLat = S.Rng.Gaussian(0f, err * dist * 0.9f);
            float eDep = S.Rng.Gaussian(0f, err * dist * 0.55f);
            float eZ = S.Rng.Gaussian(0f, err * dist * 0.24f);
            Vec3 aimErr = new Vec3(aim.X + lat.X * eLat + dirH.X * eDep, aim.Y + lat.Y * eLat + dirH.Y * eDep, Math.Max(0.3f, aim.Z + eZ));
            float d2 = (aimErr.XY - q.Pos).Length;
            float sp2 = StyleSpeed(style, d2, power, out high, vBoost, loft);
            BallPhysics.SolveLaunch(from, aimErr, sp2, high, out vel, out tf);
            S.NoteThrow((aimErr.XY - aim.XY).Length, dist, press, vel.Length);
            S.LastThrowStyle = style;

            BallPhysics.WindScale = windScale0;
            S.Ball.Launch(BallState.Pass, from, vel, q.Id);
            S.OnPassReleased(q, t, false);
            q.HasBall = false;
            q.QbState = QbPhase.Done;
        }

        // ---------------------------------------------------------------- user quarterback

        public void UserQbInput(SimAgent q, ControlInput input)
        {
            QbSeqWatch(q);      // v0.10.0 QB
            if (!q.HasBall) return;
            // the dropback is automatic; a firm stick push after the exchange takes the quarterback off the script
            if (q.QbState == QbPhase.Dropback && input.Move.Length > 0.6f && S.Time > 1.0f && (_dropPoint - q.Pos).Length < 2.5f) q.QbState = QbPhase.Scramble;   // after he has dropped, not mid-drop
            bool manual = q.QbState == QbPhase.Set || q.QbState == QbPhase.Scramble;
            if (q.QbState == QbPhase.Windup)
            {
                UpdateQb();
                return;
            }
            if (!manual)
            {
                UpdateQb();
                if (input.ThrowToSlot > 0 && q.QbState == QbPhase.Dropback && S.Time > 0.5f && q.HasBall)
                    TryUserThrow(q, input);
                if (q.QbState == QbPhase.Mesh || q.QbState == QbPhase.Fake) return;
                if (q.QbState != QbPhase.Dropback) return;
                return;
            }
            float maxSp = q.Body.EffectiveVMax * (input.Sprint ? 1f : 0.82f);
            float mag = Math.Min(1f, input.Move.Length);
            if (mag > 0.08f)
            {
                q.Want = input.Move.Norm() * (maxSp * mag * (q.QbState == QbPhase.Set ? 0.7f : 1f));
                q.Exertion = input.Sprint ? 1f : 0f;
                if (q.QbState == QbPhase.Set)
                {
                    // v0.9.0 (E-320): a nudge of the stick in the pocket is a step in the pocket, not a scramble (every receiver abandoned his route on a mag-0.4 push). The scramble starts when the stick has been
                    // held for 0.4 s at a real speed, or when he has left the pocket.
                    if (mag > 0.4f) q.StickHeld += PlaySimulation.Dt; else q.StickHeld = Math.Max(0f, q.StickHeld - 2f * PlaySimulation.Dt);
                    bool outOfPocket = Math.Abs(q.Pos.X - _dropPoint.X) > 3.6f || q.Pos.Y > _dropPoint.Y + 3.4f || q.Pos.Y < _dropPoint.Y - 3.0f;
                    if ((q.StickHeld >= 0.4f && q.Speed > 3f) || outOfPocket) q.QbState = QbPhase.Scramble;
                }
            }
            else if (q.QbState == QbPhase.Set) q.StickHeld = Math.Max(0f, q.StickHeld - 2f * PlaySimulation.Dt);
            q.Intent = AgentAction.Run;
            if (q.Pos.Y > S.LosY + 0.3f) S.QbHasCrossedLos = true;
            if (input.Throwaway) { ThrowAway(q); return; }
            if (input.ThrowToSlot > 0 && !S.QbHasCrossedLos) TryUserThrow(q, input);
            if (q.QbState == QbPhase.Scramble) S.Moves.UserMoves(q, input);
        }

        private void TryUserThrow(SimAgent q, ControlInput input)
        {
            int slot = input.ThrowToSlot;
            if (slot < 1 || slot > 10) return;
            SimAgent t = S.Agents[slot];
            if (!t.IsReceiverEligible) return;
            BeginThrow(q, t, input.ThrowStyle, input.LeadNudge, true);
        }
    }
}
