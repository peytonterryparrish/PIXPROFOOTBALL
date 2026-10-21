using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>Kickoffs, punts, field goals and extra points: kick physics, coverage lanes, returns, blocks and results.</summary>
    internal sealed partial class SpecialTeamsAI
    {
        public const float GoalPostY = 110f;
        public const float UprightHalfWidth = 3.0833f; // 18.5 ft between the uprights, both pro and college
        public const float CrossbarHeight = 3.3333f;   // 10 ft

        private readonly PlaySimulation S;
        private SimAgent _kicker, _holder, _snapper;
        private SimAgent _returner;
        private bool _kicked;
        private float _kickTime;       // sim time at which the kick happens
        private float _catchTime;      // sim time the kicker has the ball in hand
        private bool _ballInHands;
        private bool _blockResolved;
        private Vec3 _kickPoint;
        private bool _landed;
        private bool _firstTouchDone;
        private KickKind _kind;
        private float _aimX;
        private bool _catchMade;

        public SpecialTeamsAI(PlaySimulation s) { S = s; }

        public bool IsKickerOrHolder(SimAgent a)
        {
            if (S.Kind == PlayKind.Scrimmage) return false;
            return a == _kicker || a == _holder || a == _snapper;
        }

        // =====================================================================================
        // Preparation
        // =====================================================================================

        public void Prepare()
        {
            if (S.Kind == PlayKind.Scrimmage || S.Kind == PlayKind.Kneel || S.Kind == PlayKind.Spike) return;
            _kicker = S.Agents[0];
            var ka = S.OffPlay.Assignments[0] as KickAssignment;
            _kind = ka != null ? ka.KickType : KickKind.Deep;
            _aimX = ka != null ? ka.AimX : 0f;
            switch (S.Kind)
            {
                case PlayKind.Kickoff:
                    S.Blocks.BlockerSideOverride = SimSide.Defense;
                    S.Ball.State = BallState.Dead;
                    S.Ball.Pos = new Vec3(S.BallX0, S.LosY, BallPhysics.BallRadius);
                    S.Ball.PrevPos = S.Ball.Pos;
                    _returner = ChooseReturner();
                    break;
                case PlayKind.Punt:
                    _snapper = S.Agents[1];
                    S.Blocks.BlockerSideOverride = SimSide.Offense;
                    S.Blocks.ForcePassMode = true;
                    S.Blocks.HasAnchor = true;
                    S.Blocks.AnchorOverride = _kicker.Pos;
                    _returner = S.Agents[11];
                    break;
                case PlayKind.FieldGoal:
                    _holder = S.Agents[1];
                    _snapper = S.Agents[2];
                    S.Blocks.BlockerSideOverride = SimSide.Offense;
                    S.Blocks.ForcePassMode = true;
                    S.Blocks.HasAnchor = true;
                    S.Blocks.AnchorOverride = _holder.Pos;
                    _returner = null;
                    for (int i = 11; i < 22; i++) if (S.Agents[i].Assign is ReturnerAssignment) _returner = S.Agents[i];
                    break;
            }
            if (_returner != null) _returner.ReturnerRole = true;
        }

        private SimAgent ChooseReturner()
        {
            SimAgent best = null;
            for (int i = 11; i < 22; i++)
            {
                var ra = S.Agents[i].Assign as ReturnerAssignment;
                if (ra == null) continue;
                if (best == null) best = S.Agents[i];
            }
            return best ?? S.Agents[11];
        }

        // =====================================================================================
        // Snap
        // =====================================================================================

        public void OnSnap()
        {
            if (S.Kind == PlayKind.Scrimmage || S.Kind == PlayKind.Kneel || S.Kind == PlayKind.Spike) return;
            _kicked = false;
            if (S.Kind == PlayKind.Punt || S.Kind == PlayKind.FieldGoal)
            {
                SimAgent c = _snapper;
                SimAgent to = S.Kind == PlayKind.Punt ? _kicker : _holder;
                Vec3 from = new Vec3(c.Pos, 0.6f);
                Vec3 tgt = new Vec3(to.Pos + new Vec2(0f, 0.3f), 1.0f);
                Vec3 vel; float tf;
                BallPhysics.SolveLaunch(from, tgt, S.Kind == PlayKind.Punt ? 21f : 19f, false, out vel, out tf);
                S.Ball.Launch(BallState.Snap, from, vel, c.Id);
                S.PlayAction(c, AgentAction.Throw, 0.4f);
            }
            else
            {
                _kickTime = 1.05f;
                _ballInHands = true;
            }
        }

        public void UpdateSnap()
        {
            // snap in flight to punter/holder
            SimAgent to = S.Kind == PlayKind.Punt ? _kicker : _holder;
            float d = (to.Pos - S.Ball.Pos.XY).Length;
            if (d < 1.1f && S.Ball.Pos.Z < 2.4f)
            {
                S.GiveBall(to);
                _ballInHands = true;
                _catchTime = S.Time;
                _kickTime = S.Time + (S.Kind == PlayKind.Punt ? 0.5f : 0.35f);
                return;
            }
            if (S.Ball.GroundContactThisStep)
            {
                S.Ball.State = BallState.Loose;
                S.Ball.IsKick = false;
                S.Fumbled = true; S.FumbleCount++; S.FumbleSpotY = S.Ball.Pos.Y;
                S.LogEvent(SimEventType.Fumble, _snapper.Id, -1, TackleType.None, 0f, "snap");
            }
        }

        // =====================================================================================
        // Per-tick behavior
        // =====================================================================================

        public void Update()
        {
            if (S.Kind == PlayKind.Scrimmage || S.Kind == PlayKind.Kneel || S.Kind == PlayKind.Spike) return;
            if (S.Phase != PlayPhase.Live) return;
            if (S.Kind == PlayKind.Kickoff) UpdateKickoff();
            else if (S.Kind == PlayKind.Punt) UpdatePunt();
            else UpdateFieldGoal();
        }

        private bool RetPhase { get { return S.BallSide == SimSide.Defense && S.Kind != PlayKind.FieldGoal || S.Turnover; } }

        // ---------------------------------------------------------------- common agent helpers

        private void RunLane(SimAgent a, LaneAssignment lane, float speedFrac)
        {
            float vmax = a.Body.EffectiveVMax;
            a.Intent = AgentAction.Run;
            if (lane == null || lane.Path.Count == 0) { a.Want = new Vec2(0f, 1f) * vmax * speedFrac; return; }
            while (a.PathIdx < lane.Path.Count - 1 && (S.Rel(lane.Path[a.PathIdx]) - a.Pos).Length < 1.5f) a.PathIdx++;
            Vec2 wp = S.Rel(lane.Path[a.PathIdx]);
            Vec2 to = wp - a.Pos;
            a.Want = to.Norm() * vmax * speedFrac;
        }

        private void CoverageRun(SimAgent a, float speedFrac)
        {
            a.Intent = AgentAction.Run;
            SimAgent c = S.Carrier;
            if (c != null && c.Side != a.Side)
            {
                S.DefAI.PursuitUpdate(a);
                return;
            }
            if (S.Ball.State == BallState.Kick || S.Ball.State == BallState.Loose)
            {
                // converge on the landing zone while holding lanes
                var lane = a.Assign as LaneAssignment;
                if (lane != null && lane.Gunner || S.Kind == PlayKind.Punt)
                {
                    Vec3 at; float tt;
                    Vec2 tgt = S.Ball.Pos.XY;
                    if (BallPhysics.PredictAtHeight(S.Ball.Pos, S.Ball.Vel, 1.5f, out at, out tt)) tgt = at.XY;
                    Vec2 to2 = tgt - a.Pos;
                    a.Want = to2.Norm() * a.Body.EffectiveVMax * 0.98f;
                    return;
                }
                RunLane(a, a.Assign as LaneAssignment, speedFrac);
                return;
            }
            RunLane(a, a.Assign as LaneAssignment, speedFrac);
        }

        private void ReturnBlocker(SimAgent a)
        {
            SimAgent c = S.Carrier;
            if (c != null && c.Side == a.Side) { S.Blocks.UpdateEscort(a); return; }
            S.Blocks.UpdateReturnBlocker(a);
        }

        // ---------------------------------------------------------------- kickoff

        private void UpdateKickoff()
        {
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.ControlledByUser || a.State == AgentState.Down || a.Body.FallenTime > 0f) continue;
                if (a.State == AgentState.Engaged) continue;
                if (a.HasBall && a.Side == S.BallSide) { new CarrierAI(S).Update(a); continue; }
                if (a.Side == SimSide.Offense)
                {
                    if (a == _kicker) KickerRunUp(a);
                    else if (a.Assign is LaneAssignment) CoverageRun(a, 0.92f);
                    else a.Want = Vec2.Zero;
                }
                else
                {
                    if (a.Assign is ReturnerAssignment) ReturnerUpdate(a);
                    else ReturnBlocker(a);
                }
            }
            if (!_kicked && S.Time >= _kickTime && (_kicker.Pos - S.Ball.Pos.XY).Length < 1.6f) DoKickoff();
            else if (!_kicked && S.Time >= _kickTime + 0.6f) DoKickoff();
        }

        private void KickerRunUp(SimAgent a)
        {
            a.Intent = AgentAction.Run;
            if (_kicked) { a.Want = Vec2.Zero; return; }
            // a few steps in a curved run-up to the tee
            Vec2 ball = new Vec2(S.BallX0, S.LosY);
            Vec2 dir = (ball - a.Pos).Norm();
            float speed = Math.Min(a.Body.EffectiveVMax * 0.7f, 5f + S.Time * 4f);
            a.Want = dir * speed;
            a.HasFace = true; a.FaceDir = dir;
        }

        private void DoKickoff()
        {
            _kicked = true;
            SimAgent k = _kicker;
            float kp = SimMath.Rating01(k.R.KickPower), acc = SimMath.Rating01(k.R.KickAccuracy);
            float dist, hang;
            Vec2 aim = new Vec2(S.BallX0 + _aimX, 0f);
            switch (_kind)
            {
                case KickKind.Onside: dist = 11.5f + S.Rng.Gaussian(0f, 1.3f); hang = 1.15f; break;
                case KickKind.Squib: dist = 33f + 8f * kp + S.Rng.Gaussian(0f, 3f); hang = 1.9f; break;
                case KickKind.Rugby: dist = 42f + 10f * kp + S.Rng.Gaussian(0f, 3f); hang = 3.3f; break;
                default: dist = 52f + 22f * kp + S.Rng.Gaussian(0f, 2.5f + 3f * (1f - acc)); hang = 3.55f + 1.0f * kp; break;
            }
            float latErr = S.Rng.Gaussian(0f, 0.8f + 4.5f * (1f - acc));
            if (_kind == KickKind.Onside) latErr *= 0.5f;
            Vec2 target = new Vec2(SimMath.Clamp(aim.X + latErr, 1f, FieldGeometry.Width - 1f), S.LosY + dist);
            Vec3 from = new Vec3(S.BallX0, S.LosY, 0.3f);
            Vec3 vel = SolveKick(from, target, hang);
            S.Ball.Launch(BallState.Kick, from, vel, k.Id);
            S.Ball.IsKick = true;
            KickCommon(k, from);
            _kickPoint = from;
            S.KickDistance = dist;
        }

        private void KickCommon(SimAgent k, Vec3 from)
        {
            S.KickerId = k.Id;
            S.PlayAction(k, AgentAction.Kick, 0.6f);
            S.LogEvent(SimEventType.Kick, k.Id, -1, from, TackleType.None, S.Ball.Vel.Length);
            _kicked = true;
            _ballInHands = false;
        }

        /// <summary>Launch velocity so a kicked ball lands near target with the requested hang time (drag-corrected).</summary>
        public static Vec3 SolveKick(Vec3 from, Vec2 target, float hang)
        {
            Vec2 d = target - from.XY;
            float dist = d.Length;
            Vec2 dir = dist > 1e-3f ? d * (1f / dist) : new Vec2(0f, 1f);
            float vh = dist / hang;
            float vz = 0.5f * SimMath.Gravity * hang + (0.3f - from.Z) / hang;
            for (int it = 0; it < 6; it++)
            {
                Vec3 p = from; Vec3 v = new Vec3(dir.X * vh, dir.Y * vh, vz);
                float t = 0f;
                for (int i = 0; i < 1200; i++)
                {
                    BallPhysics.Advance(ref p, ref v, BallPhysics.Dt);
                    t += BallPhysics.Dt;
                    if (p.Z <= 0.12f && v.Z < 0f) break;
                }
                float landed = (p.XY - from.XY).Length;
                if (landed < 0.5f) break;
                vh *= dist / landed;
                vz *= hang / Math.Max(0.5f, t);
            }
            return new Vec3(dir.X * vh, dir.Y * vh, vz);
        }

        // ---------------------------------------------------------------- returners

        private void ReturnerUpdate(SimAgent a)
        {
            a.Intent = AgentAction.Run;
            float vmax = a.Body.EffectiveVMax;
            bool inFlight = S.Ball.State == BallState.Kick;
            bool bouncing = S.Ball.State == BallState.Loose && S.Ball.IsKick && !S.Ball.AtRest;
            if (inFlight || bouncing)
            {
                if (a != _returner && !bouncing)
                {
                    // the backup returner holds his spot
                    a.Want = Vec2.Zero;
                    return;
                }
                Vec2 target = inFlight ? PredictCatch().XY : S.Ball.Pos.XY;
                Vec2 to = target - a.Pos;
                float need = to.Length / Math.Max(0.25f, inFlight ? PredictCatchTime() : 0.3f);
                float spd = Math.Min(vmax, Math.Max(2f, need * 1.05f));
                if (to.Length < 0.4f) spd = 0f;
                a.Want = to.Norm() * spd;
                a.HasFace = true;
                a.FaceDir = (S.Ball.Pos.XY - a.Pos).LenSq() > 0.2f ? (S.Ball.Pos.XY - a.Pos).Norm() : new Vec2(0f, 1f);
                return;
            }
            a.Want = Vec2.Zero;
        }

        private Vec3 PredictCatch()
        {
            Vec3 at; float tt;
            if (BallPhysics.PredictAtHeight(S.Ball.Pos, S.Ball.Vel, 1.5f, out at, out tt)) return at;
            return S.Ball.Pos;
        }

        private float PredictCatchTime()
        {
            Vec3 at; float tt;
            if (BallPhysics.PredictAtHeight(S.Ball.Pos, S.Ball.Vel, 1.5f, out at, out tt)) return tt;
            return 0.3f;
        }

        // ---------------------------------------------------------------- punts

        private void UpdatePunt()
        {
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.ControlledByUser || a.State == AgentState.Down || a.Body.FallenTime > 0f) continue;
                if (a.State == AgentState.Engaged) continue;
                if (a.HasBall && a.Side == S.BallSide && a != _kicker) { new CarrierAI(S).Update(a); continue; }
                if (a.Side == SimSide.Offense)
                {
                    if (a == _kicker) PunterUpdate(a);
                    else if (a == _snapper) { if (_kicked) CoverageRun(a, 0.9f); else { a.Want = Vec2.Zero; } }
                    else if (a.Assign is LaneAssignment) { if (S.Time < 0.35f) { a.Want = Vec2.Zero; a.Intent = AgentAction.Stance; } else CoverageRun(a, 0.98f); }
                    else if (a.Assign is BlockAssignment)
                    {
                        if (_kicked && S.Ball.State == BallState.Kick) CoverageRun(a, 0.85f);
                        else if (_kicked) S.DefAI.PursuitUpdate(a);
                        else S.Blocks.UpdateBlocker(a);
                    }
                    else a.Want = Vec2.Zero;
                }
                else
                {
                    if (a.Assign is ReturnerAssignment) ReturnerUpdate(a);
                    else if (a.Assign is RushAssignment || a.Assign is BlitzAssignment) { if (!_kicked) RushPunter(a); else ReturnBlocker(a); }
                    else if (a.Assign is LaneAssignment) { if (!_kicked) RushPunter(a); else ReturnBlocker(a); }
                    else ReturnBlocker(a);
                }
            }
            S.Blocks.AnchorOverride = _kicker.Pos;
            if (_ballInHands && !_kicked && S.Time >= _kickTime && S.Ball.Holder == _kicker.Id) DoPunt();
        }

        private void RushPunter(SimAgent a)
        {
            a.Intent = AgentAction.Run;
            float delay = 0f;
            var ba = a.Assign as BlitzAssignment;
            if (ba != null) delay = ba.Delay;
            if (S.Time < delay) { a.Want = Vec2.Zero; return; }
            Vec2 to = _kicker.Pos - a.Pos;
            a.Want = to.Norm() * a.Body.EffectiveVMax;
        }

        private void PunterUpdate(SimAgent a)
        {
            a.Intent = AgentAction.Idle;
            a.HasFace = true; a.FaceDir = new Vec2(0f, 1f);
            if (_kicked) { a.Want = Vec2.Zero; return; }
            // take two steps forward into the kick
            if (_ballInHands) a.Want = new Vec2(0f, 1f) * 3.0f;
        }

        private void DoPunt()
        {
            _kicked = true;
            SimAgent k = _kicker;
            float kp = SimMath.Rating01(k.R.KickPower), acc = SimMath.Rating01(k.R.KickAccuracy);
            Vec3 from = new Vec3(k.Pos + new Vec2(0f, 0.8f), 0.8f);
            // rush pressure shortens the punt and can block it
            float pressure = 0f;
            SimAgent blocker = null; float bd = 99f;
            for (int i = 11; i < 22; i++)
            {
                SimAgent d = S.Agents[i];
                if (d.State == AgentState.Down || d.Body.FallenTime > 0f) continue;
                float dist = (d.Pos - from.XY).Length;
                if (dist < 5f) pressure = Math.Max(pressure, 1f - dist / 5f);
                if (dist < bd && !(d.Assign is ReturnerAssignment)) { bd = dist; blocker = d; }
            }
            if (blocker != null && bd < 1.35f)
            {
                float pBlock = 0.55f + 0.3f * SimMath.Rating01(blocker.R.Jumping) - 0.2f * SimMath.Rating01(k.R.Awareness);
                if (S.Rng.NextFloat() < pBlock) { BlockKick(k, blocker, from); return; }
            }
            float flight = (50f + 20f * kp) * (1f - 0.25f * pressure) + S.Rng.Gaussian(0f, 2.5f + 4f * (1f - acc));
            float hang = (3.9f + 1.2f * kp) * (1f - 0.12f * pressure);
            var ka = (KickAssignment)S.OffPlay.Assignments[0];
            float aimX = S.BallX0 + ka.AimX;
            if (_kind == KickKind.Rugby) { flight *= 0.78f; hang *= 0.82f; }
            float latErr = S.Rng.Gaussian(0f, 0.8f + 3.2f * (1f - acc));
            Vec2 target = new Vec2(SimMath.Clamp(aimX + latErr, 2f, FieldGeometry.Width - 2f), from.Y + flight);
            Vec3 vel = SolveKick(from, target, hang);
            S.Ball.Launch(BallState.Kick, from, vel, k.Id);
            S.Ball.IsKick = true;
            KickCommon(k, from);
            _kickPoint = from;
            S.KickDistance = flight;
            S.Blocks.ReleaseAll();
        }

        private void BlockKick(SimAgent k, SimAgent blocker, Vec3 from)
        {
            _kicked = true;
            S.KickBlocked = true;
            S.KickerId = k.Id;
            S.LogEvent(SimEventType.KickBlocked, blocker.Id, k.Id, from);
            S.PlayAction(blocker, AgentAction.Jump, 0.5f);
            Vec3 v = new Vec3(blocker.Facing.X * -1.5f + S.Rng.Gaussian(0f, 3.5f), S.Rng.Gaussian(0f, 3.5f) - 2f, 1.5f + S.Rng.NextFloat() * 2f);
            S.Ball.MakeLoose(from, v);
            S.Ball.IsKick = false;
            S.Fumbled = true; S.FumbleCount++; S.FumbleSpotY = from.Y;
            S.Blocks.ReleaseAll();
        }

        // ---------------------------------------------------------------- field goals

        private void UpdateFieldGoal()
        {
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.ControlledByUser || a.State == AgentState.Down || a.Body.FallenTime > 0f) continue;
                if (a.State == AgentState.Engaged) continue;
                if (a.HasBall && a != _holder && a.Side == S.BallSide) { new CarrierAI(S).Update(a); continue; }
                if (a.Side == SimSide.Offense)
                {
                    if (a == _kicker) FgKickerUpdate(a);
                    else if (a == _holder || a == _snapper) { a.Want = Vec2.Zero; a.HasFace = true; a.FaceDir = new Vec2(0f, 1f); a.Intent = AgentAction.Stance; }
                    else if (_kicked) S.DefAI.PursuitUpdate(a);
                    else S.Blocks.UpdateBlocker(a);
                }
                else
                {
                    if (a.Assign is ReturnerAssignment)
                    {
                        a.Want = Vec2.Zero;
                        if (_kicked && S.Ball.State == BallState.Loose) PursueLoose(a);
                    }
                    else if (_kicked) { PursueLoose(a); }
                    else FgRush(a);
                }
            }
            S.Blocks.AnchorOverride = _holder != null ? _holder.Pos : _kicker.Pos;
            if (_ballInHands && !_kicked && S.Time >= _kickTime && S.Ball.Holder == _holder.Id) DoFieldGoal();
        }

        private void PursueLoose(SimAgent a)
        {
            if (S.Ball.State == BallState.Loose)
            {
                Vec2 to = S.Ball.Pos.XY - a.Pos;
                a.Want = to.Norm() * a.Body.EffectiveVMax;
            }
            else a.Want = Vec2.Zero;
        }

        private void FgKickerUpdate(SimAgent a)
        {
            a.Intent = AgentAction.Run;
            if (_kicked) { a.Want = Vec2.Zero; return; }
            // approach the ball placed at the holder
            if (!_ballInHands) { a.Want = Vec2.Zero; return; }
            Vec2 spot = _holder.Pos + new Vec2(0f, 0.2f);
            Vec2 toBall = spot - a.Pos;
            a.Want = toBall.Norm() * Math.Min(a.Body.EffectiveVMax * 0.55f, toBall.Length * 3.5f);
            a.HasFace = true; a.FaceDir = new Vec2(0f, 1f);
        }

        private void FgRush(SimAgent a)
        {
            a.Intent = AgentAction.Run;
            float vmax = a.Body.EffectiveVMax;
            float delay = 0f;
            RunGap gap = RunGap.None;
            var ra = a.Assign as RushAssignment;
            var ba = a.Assign as BlitzAssignment;
            if (ba != null) { delay = ba.Delay; gap = ba.Gap; }
            if (ra != null) gap = ra.Gap;
            if (S.Time < delay) { a.Want = Vec2.Zero; return; }
            if (ra == null && ba == null)
            {
                // lane players stay home on the line
                a.Want = Vec2.Zero;
                return;
            }
            Vec2 target = new Vec2(S.BallX0 + DefenseAI.GapOffset(gap) * 0.8f, _holder.Pos.Y + 0.5f);
            if (S.Time > 0.45f) target = _holder.Pos + new Vec2(0f, 0.4f);
            if (ba != null && ba.Edge) target = new Vec2(S.BallX0 + DefenseAI.GapOffset(gap) * 0.7f, S.LosY - 1f);
            Vec2 to = target - a.Pos;
            a.Want = to.Norm() * vmax;
        }

        private void DoFieldGoal()
        {
            _kicked = true;
            SimAgent k = _kicker;
            float kp = SimMath.Rating01(k.R.KickPower), acc = SimMath.Rating01(k.R.KickAccuracy);
            Vec3 from = new Vec3(_holder.Pos.X, _holder.Pos.Y + 0.4f, 0.4f);
            S.KickDistance = GoalPostY - from.Y + 0f;
            // pressure from the rush
            float pressure = 0f; SimAgent blocker = null; float bd = 99f;
            for (int i = 11; i < 22; i++)
            {
                SimAgent d = S.Agents[i];
                if (d.State == AgentState.Down || d.Body.FallenTime > 0f) continue;
                float dist = (d.Pos - from.XY).Length;
                if (dist < 6f) pressure = Math.Max(pressure, 1f - dist / 6f);
                if (dist < bd) { bd = dist; blocker = d; }
            }
            // a defender who gets a hand on it at the line blocks the kick
            if (blocker != null && bd < 2.4f)
            {
                float pBlock = 0.16f + 0.28f * SimMath.Rating01(blocker.R.Jumping) + 0.2f * (blocker.IsDefensiveLineman ? 1f : 0f) - 0.08f * (1f - SimMath.Rating01(blocker.R.BlockShedding));
                pBlock *= SimMath.Clamp01(1.2f - (bd - 0.6f) / 1.8f);
                if (S.Rng.NextFloat() < pBlock) { BlockKick(k, blocker, from); S.Fumbled = true; return; }
            }
            float dist0 = S.KickDistance;
            float vmax = 23.5f + 10.5f * kp;
            vmax *= 1f + S.Rng.Gaussian(0f, 0.012f * (2f - acc));
            // lateral miss angle: gets worse with distance, rush pressure and poor accuracy
            float baseSd = SimMath.Lerp(0.075f, 0.036f, acc) * (1f + 0.012f * Math.Max(0f, dist0 - 40f)) * (1f + 0.35f * pressure);
            float err = S.Rng.Gaussian(0f, baseSd);
            float aimX = FieldGeometry.CenterX;
            Vec3 aim = new Vec3(aimX + (float)Math.Tan(err) * dist0, GoalPostY, 4.6f);
            Vec3 vel;
            // a field goal is a steep kick: fix the launch elevation, find the speed that clears the crossbar, and fall short if the leg can't supply it
            float theta = SimMath.Clamp(0.78f - dist0 * 0.0045f, 0.50f, 0.72f);
            aim = new Vec3(aim.X, GoalPostY, 5.0f);
            float vReq = SolveSpeedAtAngle(from, aim, theta);
            float vUse = Math.Min(vReq, vmax);
            vel = LaunchAtAngle(from, aim, theta, vUse);
            S.Ball.Launch(BallState.Kick, from, vel, k.Id);
            S.Ball.IsKick = true;
            KickCommon(k, from);
            _kickPoint = from;
            S.Blocks.ReleaseAll();
        }

        // =====================================================================================
        // Kick flight, catches, bounces
        // =====================================================================================

        public void UpdateKickBall()
        {
            BallSim ball = S.Ball;
            if (S.Kind == PlayKind.FieldGoal) { UpdateFieldGoalFlight(); return; }
            // a returner (or any receiving player at hand) fields the ball
            for (int i = 11; i < 22 && ball.State == BallState.Kick; i++)
            {
                SimAgent a = S.Agents[i];
                if (!(a.Assign is ReturnerAssignment) || a.Body.FallenTime > 0.3f || a.State == AgentState.Down) continue;
                float closest;
                if (S.Catches.Touches(a, ball.PrevPos, ball.Pos, out closest))
                {
                    AttemptCatch(a);
                    return;
                }
            }
            if (ball.GroundContactThisStep && ball.State == BallState.Kick)
            {
                ball.State = BallState.Loose;
                ball.IsKick = true;
                S.KickLandY = ball.Pos.Y;
                _landed = true;
                S.LogEvent(SimEventType.KickLanded, -1, -1, ball.Pos);
            }
            if (ball.Pos.X < -0.5f || ball.Pos.X > FieldGeometry.Width + 0.5f)
            {
                if (ball.Pos.Z < 3f || ball.GroundContactThisStep || ball.State == BallState.Loose)
                {
                    S.KickOob = true;
                    S.KickLandY = ball.Pos.Y;
                    ball.Kill();
                    S.EndPlay(EndReason.KickResult);
                }
            }
        }

        private void AttemptCatch(SimAgent a)
        {
            BallSim ball = S.Ball;
            float skill = SimMath.Rating01(a.R.Catching);
            var ra = (ReturnerAssignment)a.Assign;
            float p = 0.90f + 0.09f * skill - 0.04f * SimMath.Clamp01(a.Speed / Math.Max(1f, a.Body.VMax));
            if (ball.Pos.Z > a.Body.HeightYd * 1.2f || ball.Pos.Z < 0.5f) p -= 0.04f;
            if (S.Rng.NextFloat() > p)
            {
                // muff
                S.LogEvent(SimEventType.Drop, a.Id, -1, ball.Pos);
                ball.MakeLoose(ball.Pos, new Vec3(S.Rng.Gaussian(0f, 2.5f), S.Rng.Gaussian(0f, 2.5f) + 1f, 1.8f));
                ball.IsKick = true;
                S.KickLandY = ball.Pos.Y;
                _landed = true;
                return;
            }
            float y = a.Pos.Y;
            S.KickReturnStartY = y;
            S.KickLandY = y;
            // end zone catches: kneel unless close to the goal line
            if (y >= 100f)
            {
                bool kneel = y - 100f > 3.2f || S.Kind == PlayKind.Punt;
                S.GiveBall(a);
                S.LogEvent(SimEventType.Catch, a.Id, -1, new Vec3(a.Pos, 1f));
                if (kneel)
                {
                    S.KickTouchback = true;
                    S.LogEvent(SimEventType.Touchback, a.Id, -1);
                    S.EndPlay(EndReason.KickResult);
                    return;
                }
            }
            // fair catch (punts): the coverage is on top of him
            bool fair = false;
            if (S.Kind == PlayKind.Punt && ra.FairCatchAllowed)
            {
                float nearest = 99f;
                for (int i = 0; i < 11; i++) { SimAgent o = S.Agents[i]; if (o.State != AgentState.Down) nearest = Math.Min(nearest, (o.Pos - a.Pos).Length); }
                float aw = SimMath.Rating01(a.R.Awareness);
                fair = nearest < 4.2f + 2.0f * (1f - aw);
            }
            S.GiveBall(a);
            S.BallSide = a.Side;
            S.LogEvent(SimEventType.Catch, a.Id, -1, new Vec3(a.Pos, 1f));
            S.PlayAction(a, AgentAction.Catch, 0.35f);
            _catchMade = true;
            if (fair)
            {
                S.KickFairCatch = true;
                S.LogEvent(SimEventType.FairCatch, a.Id, -1);
                a.Want = Vec2.Zero;
                S.EndPlay(EndReason.KickResult);
                return;
            }
            S.Blocks.BlockerSideOverride = SimSide.Defense;
        }

        public void UpdateLooseKick()
        {
            BallSim ball = S.Ball;
            if (S.Kind == PlayKind.FieldGoal && !S.Fumbled) { UpdateFieldGoalFlight(); return; }
            if (S.KickBlocked || (S.Kind == PlayKind.FieldGoal) || !ball.IsKick)
            {
                // blocked kick: a live ball, recovered by whoever gets there
                if (ball.Pos.X < 0f || ball.Pos.X > FieldGeometry.Width) { S.KickOob = true; ball.Kill(); S.EndPlay(EndReason.KickResult); return; }
                RecoverBlocked();
                return;
            }
            // ball on the ground after a kick
            if (ball.Pos.X < 0f || ball.Pos.X > FieldGeometry.Width)
            {
                S.KickOob = true; S.KickLandY = ball.Pos.Y;
                ball.Kill(); S.EndPlay(EndReason.KickResult); return;
            }
            if (ball.Pos.Y >= 100f && (ball.GroundContactThisStep || ball.AtRest || ball.Pos.Y > 109f))
            {
                // untouched in the end zone: touchback
                S.KickTouchback = true;
                if (S.KickLandY < 100f) S.KickLandY = Math.Min(100f, ball.Pos.Y);
                ball.Kill();
                S.EndPlay(EndReason.KickResult);
                return;
            }
            // fielding the bouncing ball
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.Body.FallenTime > 0.3f || a.State == AgentState.Down || a.State == AgentState.Engaged) continue;
                float d = (a.Pos - ball.Pos.XY).Length;
                if (d > a.Body.Radius + 0.9f || ball.Pos.Z > 1.6f) continue;
                bool receiving = a.Side == SimSide.Defense;
                if (receiving)
                {
                    float p = 0.55f + 0.35f * SimMath.Rating01(a.R.Catching);
                    if (a.Assign is ReturnerAssignment) p += 0.1f;
                    if (ball.Vel.Length > 7f) p -= 0.25f;
                    if (S.Rng.NextFloat() < p)
                    {
                        S.KickReturnStartY = a.Pos.Y;
                        if (S.KickLandY <= 0f) S.KickLandY = a.Pos.Y;
                        S.GiveBall(a);
                        S.BallSide = SimSide.Defense;
                        S.LogEvent(SimEventType.Recovery, a.Id, -1, new Vec3(a.Pos, 0.3f), TackleType.None, 1f);
                        S.Blocks.BlockerSideOverride = SimSide.Defense;
                        return;
                    }
                }
                else
                {
                    // the kicking team: onside recoveries (10+ yards) or downing a punt
                    bool far = ball.Pos.Y - S.LosY >= (S.Kind == PlayKind.Kickoff ? 9.5f : 1f);
                    if (!far) continue;
                    if (S.Kind == PlayKind.Punt)
                    {
                        // downed by the coverage team (no return)
                        if (S.Rng.NextFloat() < 0.5f)
                        {
                            S.KickLandY = ball.Pos.Y;
                            S.KickRecoveredByKicker = false;
                            S.LogEvent(SimEventType.Whistle, a.Id, -1, ball.Pos, TackleType.None, 0f, "downed");
                            ball.Kill();
                            S.EndPlay(EndReason.KickResult);
                            return;
                        }
                        continue;
                    }
                    float p = 0.35f + 0.3f * SimMath.Rating01(a.R.Catching) + 0.1f * SimMath.Rating01(a.R.Awareness);
                    if (S.Rng.NextFloat() < p)
                    {
                        S.KickRecoveredByKicker = true;
                        S.KickLandY = ball.Pos.Y;
                        S.GiveBall(a);
                        S.LogEvent(SimEventType.Recovery, a.Id, -1, new Vec3(a.Pos, 0.3f), TackleType.None, 0f);
                        S.EndPlay(EndReason.KickResult);
                        return;
                    }
                }
            }
            if (ball.AtRest)
            {
                S.KickLandY = ball.Pos.Y;
                ball.Kill();
                S.EndPlay(EndReason.KickResult);
            }
        }

        private void RecoverBlocked()
        {
            BallSim ball = S.Ball;
            if (ball.Pos.Z > 1.5f) return;
            SimAgent best = null; float bs = 0f;
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = S.Agents[i];
                if (a.Body.FallenTime > 0.4f || a.State == AgentState.Down) continue;
                float d = (a.Pos - ball.Pos.XY).Length;
                if (d > a.Body.Radius + 0.95f) continue;
                float sc = (0.4f + 0.6f * SimMath.Rating01(a.R.Awareness)) * (1f - d / 2f) + S.Rng.NextFloat() * 0.4f;
                if (sc > bs) { bs = sc; best = a; }
            }
            if (best == null) { if (ball.AtRest) { ball.Kill(); S.KickLandY = ball.Pos.Y; S.EndPlay(EndReason.KickResult); } return; }
            S.KickLandY = best.Pos.Y;
            S.KickReturnStartY = best.Pos.Y;
            S.GiveBall(best);
            bool receiving = best.Side == SimSide.Defense;
            S.LogEvent(SimEventType.Recovery, best.Id, -1, new Vec3(best.Pos, 0.3f), TackleType.None, receiving ? 1f : 0f);
            if (receiving) { S.BallSide = SimSide.Defense; S.Blocks.BlockerSideOverride = SimSide.Defense; }
            else { S.KickRecoveredByKicker = true; S.EndPlay(EndReason.KickResult); return; }
            // scoop and score in the open, otherwise go down
            bool opponentsNear = false;
            for (int i = 0; i < 11; i++) if ((S.Agents[i].Pos - best.Pos).Length < 3.5f && S.Agents[i].State != AgentState.Down) opponentsNear = true;
            if (opponentsNear) { best.State = AgentState.Down; best.Body.Fall(0.9f); S.EndPlay(EndReason.KickResult); }
        }

        // ---------------------------------------------------------------- field goal flight

        private void UpdateFieldGoalFlight()
        {
            BallSim ball = S.Ball;
            if (ball.State != BallState.Kick) return;
            Vec3 prev = ball.PrevPos, cur = ball.Pos;
            // goalpost plane
            if (prev.Y < GoalPostY && cur.Y >= GoalPostY)
            {
                float f = (GoalPostY - prev.Y) / Math.Max(1e-5f, cur.Y - prev.Y);
                float x = prev.X + (cur.X - prev.X) * f;
                float z = prev.Z + (cur.Z - prev.Z) * f;
                bool good = GoalPost.IsGood(x, z);
                // doink: the ball clips an upright or the crossbar
                float edge = Math.Abs(Math.Abs(x - FieldGeometry.CenterX) - UprightHalfWidth);
                float barEdge = Math.Abs(z - CrossbarHeight);
                if (z < 12f && (edge < 0.12f && z > CrossbarHeight || barEdge < 0.12f && Math.Abs(x - FieldGeometry.CenterX) < UprightHalfWidth))
                {
                    good = S.Rng.NextFloat() < 0.4f;
                    S.LogEvent(SimEventType.CarrierMove, -1, -1, new Vec3(x, GoalPostY, z), TackleType.None, 0f, "doink");
                }
                S.KickGood = good;
                S.LogEvent(good ? SimEventType.FieldGoalGood : SimEventType.FieldGoalMissed, _kicker.Id, -1, new Vec3(x, GoalPostY, z));
                ball.Kill();
                S.EndPlay(EndReason.KickResult);
                return;
            }
            // lands short / wide
            if (ball.GroundContactThisStep || ball.Pos.Z <= 0.2f && ball.Vel.Z < 0f || Math.Abs(ball.Pos.X - FieldGeometry.CenterX) > 12f && cur.Y > 100f)
            {
                S.KickGood = false;
                S.LogEvent(SimEventType.FieldGoalMissed, _kicker.Id, -1, ball.Pos);
                ball.Kill();
                S.EndPlay(EndReason.KickResult);
                return;
            }
            // a defender at the line can swat it early
            if (!_blockResolved && ball.FlightTime < 0.35f)
            {
                for (int i = 11; i < 22; i++)
                {
                    SimAgent d = S.Agents[i];
                    if (d.State == AgentState.Down || d.Body.FallenTime > 0f) continue;
                    float dist = (d.Pos - ball.Pos.XY).Length;
                    float top = S.Catches.ReachTop(d) + 0.2f;
                    if (dist < 1.1f && ball.Pos.Z < top && ball.Pos.Z > 0.5f)
                    {
                        _blockResolved = true;
                        if (S.Rng.NextFloat() < 0.65f)
                        {
                            S.KickBlocked = true;
                            S.LogEvent(SimEventType.KickBlocked, d.Id, _kicker.Id, ball.Pos);
                            ball.MakeLoose(ball.Pos, new Vec3(S.Rng.Gaussian(0f, 3f), -2f + S.Rng.Gaussian(0f, 2.5f), 0.8f));
                            ball.IsKick = false;
                            S.Fumbled = true; S.FumbleCount++; S.FumbleSpotY = ball.Pos.Y;
                            S.PlayAction(d, AgentAction.Jump, 0.5f);
                            return;
                        }
                    }
                }
            }
        }

        // =====================================================================================
        // Results
        // =====================================================================================

        public PlayResult BuildKickResult(EndReason reason, float time)
        {
            float endY = S.EndY;
            switch (S.Kind)
            {
                case PlayKind.FieldGoal: return BuildFieldGoalResult(reason, time);
                case PlayKind.Punt: return BuildPuntResult(reason, time);
                default: return BuildKickoffResult(reason, time);
            }
        }

        private PlayResult BuildFieldGoalResult(EndReason reason, float time)
        {
            if (S.IsExtraPoint)
            {
                var pat = PlayResult.Extra(S.KickGood);
                pat.KickerId = _kicker != null ? _kicker.PlayerId : 0;
                return pat;
            }
            int dist = (int)Math.Round(S.KickDistance);
            var r = PlayResult.FieldGoal(S.KickGood, dist, time);
            r.KickerId = _kicker != null ? _kicker.PlayerId : 0;
            if (!S.KickGood && S.Turnover && reason == EndReason.Touchdown) r.Touchdown = true;
            if (!S.KickGood && S.BallSide == SimSide.Defense)
            {
                r.ReturnYards = Math.Max(0, (int)Math.Round(S.KickReturnStartY - S.EndY));
            }
            return r;
        }

        private PlayResult BuildPuntResult(EndReason reason, float time)
        {
            float recvY = S.KickLandY;
            if (S.KickBlocked && S.KickLandY <= 0f) recvY = S.KickReturnStartY;
            int gross = (int)Math.Round(recvY - S.LosY);
            int ret = 0;
            bool td = false;
            if (S.BallSide == SimSide.Defense && !S.KickFairCatch && !S.KickTouchback)
            {
                ret = Math.Max(0, (int)Math.Round(S.KickReturnStartY - S.EndY));
                if (S.Touchdown) { td = true; }
            }
            if (S.KickRecoveredByKicker)
            {
                var rk = PlayResult.Punt(gross, 0, false, false, false, false, time);
                rk.RecoveredByKickingTeam = true;
                rk.KickerId = _kicker.PlayerId;
                return rk;
            }
            var r = PlayResult.Punt(gross, ret, td, S.KickTouchback || recvY >= 100f && !(S.BallSide == SimSide.Defense && ret > 0), S.KickOob, S.KickFairCatch, time);
            if (td) r.Touchdown = true;
            r.KickerId = _kicker.PlayerId;
            return r;
        }

        private PlayResult BuildKickoffResult(EndReason reason, float time)
        {
            float recvY = S.KickLandY;
            if (recvY <= 0f) recvY = S.Ball.Pos.Y;
            int gross = (int)Math.Round(recvY - S.LosY);
            if (S.KickRecoveredByKicker)
            {
                var ro = PlayResult.Onside(gross, true);
                ro.TimeElapsed = time;
                ro.ReturnYards = 0;
                return ro;
            }
            if (_kind == KickKind.Onside && !S.KickRecoveredByKicker && S.BallSide == SimSide.Defense && S.EndY > 0)
            {
                var rl = PlayResult.Onside(gross, false);
                rl.ReturnYards = Math.Max(0, (int)Math.Round(S.KickReturnStartY - S.EndY));
                rl.TimeElapsed = time;
                return rl;
            }
            int ret = 0;
            bool td = S.Touchdown && S.DefensiveTd;
            if (S.BallSide == SimSide.Defense && !S.KickTouchback) ret = Math.Max(0, (int)Math.Round(S.KickReturnStartY - S.EndY));
            bool touchback = S.KickTouchback || (recvY >= 100f && S.BallSide != SimSide.Defense);
            var r = PlayResult.Kickoff(gross, ret, td, touchback, S.KickOob, time);
            r.KickerId = _kicker.PlayerId;
            return r;
        }
    }
}
namespace PixFootball.Sim
{
    internal sealed partial class SpecialTeamsAI
    {
        public static Vec3 LaunchAtAngle(Vec3 from, Vec3 aim, float theta, float speed)
        {
            Vec2 d = aim.XY - from.XY;
            float dh = d.Length;
            Vec2 dir = dh > 1e-4f ? d * (1f / dh) : new Vec2(0f, 1f);
            float ch = (float)Math.Cos(theta), sh = (float)Math.Sin(theta);
            return new Vec3(dir.X * speed * ch, dir.Y * speed * ch, speed * sh);
        }

        private static float HeightAtRun(Vec3 from, Vec3 aim, float theta, float speed)
        {
            Vec2 d = aim.XY - from.XY;
            float dh = d.Length;
            Vec3 v = LaunchAtAngle(from, aim, theta, speed);
            Vec3 p = from;
            Vec2 dir = dh > 1e-4f ? d * (1f / dh) : new Vec2(0f, 1f);
            for (int i = 0; i < 1500; i++)
            {
                Vec3 pp = p;
                BallPhysics.Advance(ref p, ref v, BallPhysics.Dt);
                float prog = (p.X - from.X) * dir.X + (p.Y - from.Y) * dir.Y;
                if (prog >= dh)
                {
                    float prev = (pp.X - from.X) * dir.X + (pp.Y - from.Y) * dir.Y;
                    float f = (dh - prev) / Math.Max(1e-5f, prog - prev);
                    return pp.Z + (p.Z - pp.Z) * f;
                }
                if (p.Z < -3f) return p.Z - 100f;
            }
            return -100f;
        }

        /// <summary>Launch speed that puts the ball through the aim point at a fixed elevation (bisection on speed).</summary>
        public static float SolveSpeedAtAngle(Vec3 from, Vec3 aim, float theta)
        {
            float lo = 8f, hi = 70f;
            for (int i = 0; i < 24; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (HeightAtRun(from, aim, theta, mid) < aim.Z) lo = mid; else hi = mid;
            }
            return 0.5f * (lo + hi);
        }
    }
}
