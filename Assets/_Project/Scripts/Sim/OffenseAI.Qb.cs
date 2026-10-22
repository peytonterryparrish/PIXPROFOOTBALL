using System;
using System.Collections.Generic;
using PixFootball.Core;

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
                case QbPhase.Mesh: QbMesh(q); return;
                case QbPhase.Fake: QbFake(q); return;
                case QbPhase.Dropback: QbDropback(q); return;
                case QbPhase.Set: QbSet(q); return;
                case QbPhase.Windup: QbWindup(q); return;
                case QbPhase.Scramble: UpdateQbScramble(); return;
                case QbPhase.Done:
                default:
                    if (q.HasBall && _qa != null && _qa.Action == QbAction.Keep && _qa.DropbackSteps > 0 && S.Time < 0.85f)
                    {
                        // the QB draw: show a pass drop, then take off
                        q.HasFace = true; q.FaceDir = new Vec2(0f, 1f);
                        q.Want = new Vec2(0f, -1f) * 1.5f;
                        return;
                    }
                    if (q.HasBall) { _carrierAI.Update(q); }
                    else
                    {
                        // after the handoff: ride the fake, then settle
                        q.HasFace = true; q.FaceDir = new Vec2(0f, 1f);
                        q.Want = Vec2.Zero;
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
                q.Want = Vec2.Zero;
                if ((S.Time >= 0.28f && dist < 7.5f && dist > 2.0f && Math.Abs(tgt.Vel.X) > 1.5f) || (S.Time >= 0.8f && dist < 9.5f && dist > 1.5f)) { S.PitchBall(q, tgt); q.QbState = QbPhase.Done; }   // a deadline: the pitch goes even if the back is slow to the edge
                else if (S.Time > 1.1f) q.QbState = QbPhase.Done;
                return;
            }

            // handoff: shuffle toward the mesh point
            Vec2 meshPt = tgt.Pos + tgt.Vel * 0.18f;
            Vec2 toMesh = meshPt - q.Pos;
            Vec2 start = new Vec2(S.BallX0 + S.OffForm.Slots[0].X, S.LosY + S.OffForm.Slots[0].Y);
            if ((q.Pos - start).Length > 5.6f) toMesh = Vec2.Zero;
            q.FaceDir = toMesh.LenSq() > 0.04f ? toMesh.Norm() : new Vec2(0f, 1f);
            q.Want = toMesh.Norm() * Math.Min(5.2f, toMesh.Length * 5f);

            bool closestPassed = q.MeshPrevDist > 0f && dist > q.MeshPrevDist + 0.01f && q.MeshPrevDist < 2.8f;
            q.MeshPrevDist = dist;
            float minHandoff = 0.22f;
            var hca = tgt.Assign as CarryAssignment;
            if (hca != null && S.OffPlay.Category == PlayCategory.Draw)
            {
                // the draw: drop as if to pass, then give it late
                minHandoff = hca.Delay + 0.05f;
                if (S.Time < minHandoff - 0.15f) { q.Want = new Vec2(0f, -1f) * 1.8f; q.FaceDir = new Vec2(0f, 1f); }
            }
            if (S.Time >= minHandoff && (dist < 1.6f || closestPassed))
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
            bool crashed = key.Vel.X * dir < -0.7f || (key.Pos.X - q.Pos.X) * dir < 2.8f;
            bool keep = crashed; // end crashes down on the back: pull it and run outside; end stays home: give
            if (keep && S.Rng.NextFloat() < 0.55f * (1f - SimMath.Rating01(q.R.Speed))) keep = false; // a slow quarterback hands it off anyway
            if (S.Rng.NextFloat() < (1f - SimMath.Rating01(q.R.Awareness)) * 0.15f) keep = !keep;
            return !keep;
        }

        private void QbFake(SimAgent q)
        {
            q.Intent = AgentAction.Handoff;
            q.HasFace = true;
            Vec2 toward = _fakeTarget != null ? _fakeTarget.Pos - q.Pos : new Vec2(0f, 1f);   // QA-372: a fake with no runner to sell to faces downfield, never his own end zone
            q.FaceDir = toward.LenSq() > 0.04f ? toward.Norm() : new Vec2(0f, 1f);
            q.Want = Vec2.Zero;
            S.PlayAction(q, AgentAction.Handoff, 0.2f);
            float fakeTime = _shotgun ? 0.32f : 0.45f;
            if (S.Time >= fakeTime) { q.QbState = QbPhase.Dropback; }
        }

        // ---------------------------------------------------------------- dropback

        private void QbDropback(SimAgent q)
        {
            if (!q.HasBall) { q.QbState = QbPhase.Done; return; }
            q.Intent = AgentAction.Run;
            Vec2 to = _dropPoint - q.Pos;
            float dist = to.Length;
            if (_qa.DropbackSteps <= 0 && !_rolling) { EnterSet(q); return; }
            if (dist < 0.55f || q.QbClock > 3.2f) { EnterSet(q); return; }
            Vec2 dir = to.Norm();
            float v = q.Body.EffectiveVMax * (_rolling ? 0.97f : 0.92f);
            if (!_rolling) { q.HasFace = true; q.FaceDir = new Vec2(0f, 1f); }
            q.Want = dir * Math.Min(v, Math.Max(2.0f, dist * 4.5f));
            // quick game may throw on the drop
            PressureCheck(q);
            if (_rolling && q.QbClock > 0.7f) HandleReads(q, false);
        }

        private void EnterSet(SimAgent q)
        {
            q.QbState = QbPhase.Set;
            q.QbSetAt = S.Time;
            if (!_rolling || q.ReadIdx == 0) q.ReadIdx = 0;   // a rolling quarterback keeps the progression he already worked through
            q.ReadTimer = 0.05f;
            q.QbLastAdvance = S.Time;
        }

        // ---------------------------------------------------------------- in the pocket

        private void QbSet(SimAgent q)
        {
            if (!q.HasBall) { q.QbState = QbPhase.Done; return; }
            q.Intent = AgentAction.Idle;
            q.HasFace = true;
            q.FaceDir = new Vec2(0f, 1f);
            if (_rolling) { q.Want = Vec2.Zero; q.HasFace = false; }
            // drift to a clean spot / step up away from edge pressure
            Vec2 drift = PocketDrift(q);
            q.Want = drift;
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
            if (_qa.Action == QbAction.ScreenPass) return 0.9f;
            var ra = r.Assign as RouteAssignment;
            if (ra == null || ra.Waypoints.Count == 0) return 0.6f;
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
            if (tCut < 0f) tCut = Math.Min(1.9f, ra.Waypoints[ra.Waypoints.Count - 1].Time * 0.30f);
            return SimMath.Clamp(tCut * 0.9f - 0.1f, 0.55f, 2.6f);
        }

        /// <summary>How long (sim seconds from the snap) the quarterback will hold the ball before taking what is there: longer drops hold longer.</summary>
        private float HoldMax(SimAgent q, bool hot)
        {
            float aw = SimMath.Rating01(q.R.Awareness);
            int steps = _qa != null ? _qa.DropbackSteps : 3;
            float baseT = steps <= 3 ? 2.5f : (steps <= 5 ? 2.95f : 3.3f);
            if (_qa != null && _qa.Action == QbAction.PlayActionFake) baseT -= 0.8f; // the fake already burned half a second
            float h = (hot ? Math.Min(baseT - 0.25f, 2.45f) : baseT) + 0.4f * (aw - 0.5f) + (_rolling ? 0.5f : 0f);
            return Math.Max(h, q.QbSetAt + (hot ? 0.6f : (_qa != null && _qa.Action == QbAction.PlayActionFake ? 0.3f : 0.85f)));
        }

        private float Threshold(SimAgent q, int idx, float press)
        {
            float aw = SimMath.Rating01(q.R.Awareness);
            float t0 = 0.70f - 0.12f * aw;
            float hm = HoldMax(q, _hotMode);
            float decay = SimMath.Clamp01((S.Time - 1.5f) / Math.Max(0.5f, hm - 1.5f));
            float th = t0 - 0.30f * decay - 0.30f * press - 0.02f * idx;
            return Math.Max(0.45f, th);
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
                if (S.Time >= 2.8f || press > 0.7f)
                {
                    SimAgent best = null; float bd = -1f;
                    for (int i = 0; i < _reads.Count; i++)
                    {
                        SimAgent r = S.Agents[_reads[i]];
                        if (r.Pos.Y > bd) { bd = r.Pos.Y; best = r; }
                    }
                    if (best != null) BeginThrow(q, best, ThrowStyle.Lob, Vec2.Zero);
                }
                return;
            }

            if (_qa.Action == QbAction.ScreenPass && ScreenReceiver != null)
            {
                // the screen is a timed throw: the line has released and the receiver has turned around
                SimAgent sr = ScreenReceiver;
                bool ready = S.Time >= 0.95f || (press > 0.45f && S.Time > 0.7f && sr.RouteClock > 0.3f);
                if (ready && sr.State != AgentState.Down) { q.PendingOpen = 0.7f; BeginThrow(q, sr, ThrowStyle.Touch, Vec2.Zero); }
                return;
            }


            bool hot = _hotMode;
            int upTo = (hot || S.Time > 1.8f) ? _reads.Count - 1 : Math.Min(q.ReadIdx, _reads.Count - 1);
            int pick = -1; ThrowStyle pickStyle = ThrowStyle.Auto; float pickOpen = -1f; float pickScore = -9f;
            float bestOpen = -1f; int bestIdx = -1; ThrowStyle bestStyle = ThrowStyle.Auto;
            for (int i = 0; i < _reads.Count; i++)
            {
                SimAgent r = S.Agents[_reads[i]];
                bool hotRead = IsHotRead(r);   // v0.4: the receiver the user hot-routed is read from the snap on, whatever the progression says
                if (i > upTo && !hotRead) continue;
                if (r.State == AgentState.Down) continue;
                float earliest = EarliestThrow(r);
                if (hot) earliest = Math.Min(earliest, 0.85f);
                if (S.Time < earliest && press < 0.55f) continue;
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
                bool bail = press >= 0.55f || S.Time > holdMax;
                if (!bail) return;
                // 1) take what is there when the clock or the rush says so
                if (bestIdx >= 0 && bestOpen > 0.05f && (S.Time > holdMax || press > 0.55f))
                {
                    q.PendingOpen = bestOpen;
                    BeginThrow(q, S.Agents[_reads[bestIdx]], bestStyle, Vec2.Zero);
                    return;
                }
                // 2) leave the pocket
                if (!hot && (press < 0.55f || q.R.Speed >= 80) && TryStartScramble(q, press)) return;
                // 3) live to play another down
                if (aw > 0.35f && S.State.Down < 4 && (press >= 0.50f || S.Time > holdMax + 0.05f)) { ThrowAway(q); return; }   // on fourth down nobody throws it away
                if (bestIdx >= 0 && bestOpen > 0.2f && S.Time > holdMax + 0.6f) BeginThrow(q, S.Agents[_reads[bestIdx]], bestStyle, Vec2.Zero);
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
            Vec3 from = new Vec3(q.Pos, 1.9f);
            float windup = 0.26f - 0.06f * SimMath.Rating01(q.R.Awareness);   // the same constant BeginThrow uses
            float t = 0.9f;
            Vec2 T = r.Pos;
            aim = new Vec3(T, 1.6f);
            tf = 0.9f;
            style = ThrowStyle.Auto;
            int power = q.R.ThrowPower;
            bool feasible = true;
            Vec3 lastVel = Vec3.Zero;
            // pick the style once from where the receiver is now, so deep throws do not spiral into ever-longer lobs
            Vec2 T0 = ClampField(PredictPos(r, 1.0f + windup));
            style = ChooseStyle((T0 - q.Pos).Length, power);
            for (int it = 0; it < 3; it++)
            {
                T = ClampField(PredictPos(r, t + windup));
                float d = (T - q.Pos).Length;
                // style fixed above
                bool high;
                float speed = StyleSpeed(style, d, power, out high);
                aim = new Vec3(T, high ? 1.9f : 1.6f);
                Vec3 vel;
                feasible = BallPhysics.SolveLaunch(from, aim, speed, high, out vel, out tf);
                lastVel = vel;
                t = tf;
            }
            if (!feasible || tf > 3.4f) return -1f;
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
                threat *= S.DefAI.Responsibility(d, T, r.Id);
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

        /// <summary>Launch speed (yd/s) for a pass style over horizontal distance d.</summary>
        public static float StyleSpeed(ThrowStyle style, float d, int power, out bool high)
        {
            float vmax = BallPhysics.MaxThrowSpeed(power);
            float flat = SimMath.Clamp(14.0f + 0.45f * d, 16.5f, vmax); // ~37 mph on a 10-yard out, 52 mph on a 30-yard dig: real flight times (0.55 s at 10 yd, 1.0 s at 25 yd)
            high = false;
            float minV = (float)Math.Sqrt(SimMath.Gravity * Math.Max(1f, d)) * 1.12f;
            switch (style)
            {
                case ThrowStyle.Bullet: return Math.Max(flat, Math.Min(vmax, minV));
                case ThrowStyle.Lob:
                {
                    if (d < 9f) return Math.Max(flat * 0.8f, Math.Min(vmax, minV));
                    high = true;
                    float v = (float)Math.Sqrt(SimMath.Gravity * d / 0.94f) * 1.10f;
                    return SimMath.Clamp(v, 14f, vmax);
                }
                case ThrowStyle.Touch:
                default:
                    return Math.Max(flat * 0.83f, Math.Min(vmax, minV));
            }
        }

        // ---------------------------------------------------------------- pressure

        public float PocketPressure(SimAgent q)
        {
            float p = 0f;
            // the passer feels pressure a beat late: less aware quarterbacks see where the rusher was a few ticks ago
            float delay = 0.10f + (1f - SimMath.Rating01(q.R.Awareness)) * 0.28f;
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
                p = Math.Max(p, SimMath.Clamp01(1f - tTo / 0.50f));
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

        private void UpdateQbScramble()
        {
            SimAgent q = _qb;
            if (!q.HasBall) { q.QbState = QbPhase.Done; return; }
            if (S.PassThrown) { q.QbState = QbPhase.Done; return; }
            if (q.Pos.Y > S.LosY + 0.3f) S.QbHasCrossedLos = true;
            _carrierAI.Update(q);
            q.Intent = AgentAction.Run;
            if (!S.QbHasCrossedLos && !q.ControlledByUser) HandleReads(q, true);
        }

        public void ThrowAway(SimAgent q)
        {
            if (!q.HasBall || S.PassThrown) return;
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
            Vec3 from = new Vec3(q.Pos, 1.9f);
            Vec3 aim = new Vec3(sideX, ty, 1.5f);
            bool grounding = false;
            // intentional grounding: in the pocket with no receiver anywhere near the throw
            if (Math.Abs(q.Pos.X - S.BallX0) < 7.5f && q.Pos.Y < S.LosY && q.Body.Speed < 3f)
            {
                float nearest = 99f;
                for (int i = 1; i < 11; i++) if (S.Agents[i].IsReceiverEligible) nearest = Math.Min(nearest, (S.Agents[i].Pos - aim.XY).Length);
                grounding = nearest > 11f && S.Rng.Chance(0.5f);
            }
            float d = (aim.XY - q.Pos).Length;
            bool high;
            float v = StyleSpeed(d > 22f ? ThrowStyle.Touch : ThrowStyle.Bullet, d, q.R.ThrowPower, out high);
            Vec3 vel; float tf;
            BallPhysics.SolveLaunch(from, aim, v, false, out vel, out tf);
            S.Ball.Launch(BallState.Pass, from, vel, q.Id);
            S.OnPassReleased(q, null, true);
            q.HasBall = false;
            q.QbState = QbPhase.Done;
            S.PlayAction(q, AgentAction.Throw, 0.5f);
            S.LogEvent(SimEventType.Throwaway, q.Id, -1);
            if (grounding) S.Flag(PenaltyType.IntentionalGrounding, q, q.Pos.Y - S.LosY);
        }

        // ---------------------------------------------------------------- throwing

        public void BeginThrow(SimAgent q, SimAgent target, ThrowStyle style, Vec2 nudge)
        {
            if (!q.HasBall || S.PassThrown || q.QbState == QbPhase.Windup) return;
            q.ThrowTarget = target.Id;
            q.PendingStyle = style;
            q.PendingNudge = nudge;
            q.WindupLeft = 0.26f - 0.06f * SimMath.Rating01(q.R.Awareness);
            if (S.Options.QbWindupSeconds > 0f) q.WindupLeft = S.Options.QbWindupSeconds * (q.QbState == QbPhase.Scramble ? 0.8f : 1f) * (1.04f - 0.08f * SimMath.Rating01(q.R.Awareness));      // v0.7.0: animation-synced wind-up
            q.PrevQbState = q.QbState;
            q.QbState = QbPhase.Windup;
            q.HasFace = true;
            q.FaceDir = (target.Pos - q.Pos).Norm();
            S.PlayAction(q, AgentAction.Throw, 0.55f);
        }

        private void QbWindup(SimAgent q)
        {
            q.WindupLeft -= PlaySimulation.Dt;
            q.HasFace = true;
            SimAgent t = S.Agents[q.ThrowTarget];
            q.FaceDir = (t.Pos - q.Pos).Norm();
            // keep moving at reduced speed when the throw came off a scramble
            if (q.PrevQbState == QbPhase.Scramble) q.Want = q.Vel * 0.9f; else q.Want = Vec2.Zero;
            if (!q.HasBall) { q.QbState = QbPhase.Done; return; }
            if (q.WindupLeft <= 0f) ReleaseThrow(q);
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
            for (int it = 0; it < 3; it++)
            {
                Vec2 T = ClampField(PredictPos(t, t_)) + q.PendingNudge;
                float d = (T - q.Pos).Length;
                if (style == ThrowStyle.Auto) style = ChooseStyle(d, power);
                float speed = StyleSpeed(style, d, power, out high);
                float z = high ? 1.95f : 1.6f;
                aim = new Vec3(T, z);
                BallPhysics.SolveLaunch(from, aim, speed, high, out vel, out tf);
                t_ = tf;
            }

            // accuracy: error cone grows with distance, pressure, throwing on the run, and drops with ThrowAccuracy
            float acc01 = SimMath.Rating01(q.R.ThrowAccuracy);
            float sd = 0.125f - 0.095f * acc01;
            float press = PocketPressure(q);
            float dist = (aim.XY - q.Pos).Length;
            float mult = 1f + 0.9f * press;
            mult *= 1f + 0.55f * SimMath.Clamp01(q.Speed / Math.Max(1f, q.Body.VMax));
            if (style == ThrowStyle.Lob) mult *= 0.85f; else if (style == ThrowStyle.Touch) mult *= 0.92f;
            if (dist > 30f) mult *= 1.12f;
            if (q.Body.StumbleTime > 0f) mult *= 1.4f;
            float err = sd * mult;
            Vec2 dirH = (aim.XY - q.Pos).Norm();
            Vec2 lat = dirH.Perp();
            float eLat = S.Rng.Gaussian(0f, err * dist * 0.9f);
            float eDep = S.Rng.Gaussian(0f, err * dist * 0.55f);
            float eZ = S.Rng.Gaussian(0f, err * dist * 0.30f);
            Vec3 aimErr = new Vec3(aim.X + lat.X * eLat + dirH.X * eDep, aim.Y + lat.Y * eLat + dirH.Y * eDep, Math.Max(0.3f, aim.Z + eZ));
            float d2 = (aimErr.XY - q.Pos).Length;
            float sp2 = StyleSpeed(style, d2, power, out high);
            BallPhysics.SolveLaunch(from, aimErr, sp2, high, out vel, out tf);

            BallPhysics.WindScale = windScale0;
            S.Ball.Launch(BallState.Pass, from, vel, q.Id);
            S.OnPassReleased(q, t, false);
            q.HasBall = false;
            q.QbState = QbPhase.Done;
        }

        // ---------------------------------------------------------------- user quarterback

        public void UserQbInput(SimAgent q, ControlInput input)
        {
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
                if (q.QbState == QbPhase.Set && mag > 0.4f) q.QbState = QbPhase.Scramble;
            }
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
            BeginThrow(q, t, input.ThrowStyle, input.LeadNudge);
        }
    }
}
