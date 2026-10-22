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
        private float _approachAt = -1f;   // sim time at which the kicker's approach (and the kick clip) starts; -1 = not decided yet (a user kicker waits for the press)
        private float _lead;               // approach length: seconds from the start of the approach to ball contact
        private Vec2 _approachFrom;
        private bool _approachLogged;
        private bool _ballInHands;
        private bool _blockResolved;
        private Vec3 _kickPoint;
        private KickKind _kind;
        private float _aimX;
        private bool _catchMade;
        private readonly System.Collections.Generic.Dictionary<int, float> _fieldLock = new System.Collections.Generic.Dictionary<int, float>();

        private readonly CarrierAI _carrierAI;
        public SpecialTeamsAI(PlaySimulation s) { S = s; _carrierAI = new CarrierAI(s); }

        /// <summary>True once the ball has been kicked (punt or kickoff): the kicking team is in coverage, not protection.</summary>
        public bool KickAway { get { return _kicked; } }

        /// <summary>The kicker / punter, the holder (field goals and extra points) and the long snapper of the current kick; null when the play has none.</summary>
        public SimAgent KickerAgent { get { return _kicker; } }
        public SimAgent HolderAgent { get { return _holder; } }
        public SimAgent SnapperAgent { get { return _snapper; } }

        /// <summary>Agent id named in the Snap event of a kick: the kicker (kickoff), the long snapper (punt, field goal).</summary>
        public int SnapAgentId { get { return S.Kind == PlayKind.Kickoff ? (_kicker != null ? _kicker.Id : 0) : (_snapper != null ? _snapper.Id : 1); } }

        /// <summary>True for an onside kick (nobody blocks: both teams go after the ball).</summary>
        public bool IsOnsideKick { get { return S.Kind == PlayKind.Kickoff && _kind == KickKind.Onside; } }

        /// <summary>True once a returner has fielded the kick (kickoff wall blockers hold their rows until then).</summary>
        public bool CatchMade { get { return _catchMade; } }

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
            _kicker = AgentWithRole(KickRole.Kicker, 0);
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
                    SetKickoffMarks();
                    break;
                case PlayKind.Punt:
                    _snapper = AgentWithRole(KickRole.Snapper, 1);
                    S.Blocks.BlockerSideOverride = SimSide.Offense;
                    S.Blocks.ForcePassMode = true;
                    S.Blocks.HasAnchor = true;
                    S.Blocks.AnchorOverride = _kicker.Pos;
                    _returner = S.Agents[11];
                    for (int i = 11; i < 22; i++) if (S.Agents[i].Assign is ReturnerAssignment) { _returner = S.Agents[i]; break; }
                    break;
                case PlayKind.FieldGoal:
                    _holder = AgentWithRole(KickRole.Holder, 1);
                    _snapper = AgentWithRole(KickRole.Snapper, 2);
                    S.Blocks.BlockerSideOverride = SimSide.Offense;
                    S.Blocks.ForcePassMode = true;
                    S.Blocks.HasAnchor = true;
                    S.Blocks.AnchorOverride = _holder.Pos;
                    { Vec2 ks = _holder.Pos + new Vec2(1.8f, -2.6f); _kicker.Body.Pos = ks; _kicker.StartPos = ks; _kicker.Body.Vel = Vec2.Zero; _kicker.ClearHistory(ks); }   // v0.7: room for a 2-3 step approach
                    _returner = null;
                    for (int i = 11; i < 22; i++) if (S.Agents[i].Assign is ReturnerAssignment) _returner = S.Agents[i];
                    break;
            }
            if (_returner != null) _returner.ReturnerRole = true;
            AlignHandsTeam();
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
                _lead = KickTiming.KickoffApproach;
                ScheduleKick(KickTiming.KickoffStand + _lead);
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
                if (S.Kind == PlayKind.Punt) { _lead = KickTiming.PuntApproach; ScheduleKick(S.Time + 0.5f); }
                else { _lead = KickTiming.FieldGoalApproach; ScheduleKick(Math.Max(S.Time + _lead, KickTiming.FieldGoalSnapToKick)); }
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
                if ((a.ControlledByUser && a != _kicker) || a.State == AgentState.Down || a.Body.FallenTime > 0f) continue;
                if (a.State == AgentState.Engaged) continue;
                if (a.HasBall && a.Side == S.BallSide) { _carrierAI.Update(a); continue; }
                if (a.Side == SimSide.Offense)
                {
                    if (a == _kicker) KickerRunUp(a);
                    else if (a.Assign is LaneAssignment && !_kicked) HoldKickoffLine(a);
                    else if (a.Assign is LaneAssignment) { if (_kind == KickKind.Onside && _kicked && S.BallSide == SimSide.Offense && (S.Ball.State == BallState.Kick || S.Ball.State == BallState.Loose)) HandsTeamPursue(a); else CoverageRun(a, _kind == KickKind.Onside ? 0.80f : 0.92f); }
                    else a.Want = Vec2.Zero;
                }
                else
                {
                    if (a.Assign is ReturnerAssignment) ReturnerUpdate(a);
                    else if (_kind == KickKind.Onside && _kicked && S.BallSide == SimSide.Offense && (S.Ball.State == BallState.Kick || S.Ball.State == BallState.Loose)) HandsTeamPursue(a);
                    else ReturnBlocker(a);
                }
            }
            bool kickGo = !_kicked && _approachAt >= 0f && S.Time >= _kickTime && KickGate(_kickTime);
            if (kickGo && (_kicker.Pos - S.Ball.Pos.XY).Length < 2.0f) DoKickoff();
            else if (kickGo && S.Time >= _kickTime + 1.4f) DoKickoff();   // the ball is struck from the tee: he gets there first (no teleporting the ball)
        }

        // ---------------------------------------------------------------- v0.7 kick tempo: approach scheduling and the run-up

        /// <summary>Contact is planned at <paramref name="kickAt"/>; the approach starts <c>_lead</c> seconds earlier. A user kicker waits for the press instead (see <see cref="UserTrigger"/>).</summary>
        private void ScheduleKick(float kickAt)
        {
            _approachLogged = false;
            if (UserControlsKicker) { _approachAt = -1f; _kickTime = float.MaxValue; return; }
            _kickTime = kickAt;
            _approachAt = Math.Max(0f, kickAt - _lead);
        }

        /// <summary>Absolute simulation time of the planned ball contact (-1 while a user kicker has not pressed yet). The kick clip should start at <see cref="SimEventType.KickApproach"/> and make contact here.</summary>
        public float ContactTime { get { return _approachAt >= 0f && !_kicked ? _kickTime : -1f; } }

        /// <summary>A user kicker's press starts the approach (the meter reading is taken at the press); no press within the timeout starts it on a default reading.</summary>
        private void UserTrigger()
        {
            if (!UserControlsKicker || _approachAt >= 0f || _kicked || !AwaitingUserKick) return;
            if (_userReadyAt < 0f) _userReadyAt = S.Time;
            bool timeout = S.Time - _userReadyAt > (S.Kind == PlayKind.Kickoff ? UserKickoffTimeout : UserKickTimeout);
            if (_userKickPress || timeout)
            {
                _approachAt = S.Time;
                _kickTime = S.Time + _lead;
            }
        }

        /// <summary>Starts the Kick action and logs the approach the first time it is due.</summary>
        private void BeginApproach(SimAgent k)
        {
            if (_approachLogged) return;
            _approachLogged = true;
            S.PlayAction(k, AgentAction.Kick, Math.Max(0.1f, _kickTime - S.Time) + 0.6f);
            S.LogEvent(SimEventType.KickApproach, k.Id, -1, new Vec3(k.Pos, 0f), TackleType.None, Math.Max(0f, _kickTime - S.Time));
        }

        /// <summary>Kickoff marks: the kicker stands 6.5 yd behind the tee and off to the side (5-7 steps of run-up), the kicking line 4.5 yd behind the ball so it can run level with it.</summary>
        private void SetKickoffMarks()
        {
            Vec2 tee = new Vec2(S.BallX0, S.LosY);
            Vec2 kp = tee + new Vec2(KickTiming.KickoffStartSide, -KickTiming.KickoffStartBack);
            kp = new Vec2(SimMath.Clamp(kp.X, 2f, FieldGeometry.Width - 2f), kp.Y);
            _kicker.Body.Pos = kp; _kicker.StartPos = kp; _kicker.Body.Vel = Vec2.Zero;
            _kicker.Body.Facing = (tee - kp).Norm();
            _kicker.ClearHistory(kp);
            for (int i = 0; i < 11; i++)
            {
                SimAgent a = S.Agents[i];
                if (a == _kicker || !(a.Assign is LaneAssignment)) continue;
                if (a.Pos.Y < S.LosY - 2.5f) continue;
                Vec2 p = new Vec2(a.Pos.X, S.LosY - KickTiming.CoverageStartBack);
                a.Body.Pos = p; a.StartPos = p; a.ClearHistory(p);
            }
        }

        /// <summary>Kicking line before the ball is struck: waits on its mark, then runs so that it reaches the ball line exactly at contact, at speed.</summary>
        private void HoldKickoffLine(SimAgent a)
        {
            a.Intent = AgentAction.Stance;
            if (_approachAt < 0f) { a.Want = Vec2.Zero; return; }
            float remain = _kickTime - S.Time;
            float tr = KickTiming.CoverageRunLead;
            if (remain > tr) { a.Want = Vec2.Zero; return; }
            // constant acceleration from the mark to the ball line, so the line arrives level with the ball (and at speed) exactly at contact
            a.Intent = AgentAction.Run;
            float d0 = Math.Max(0.5f, S.LosY - 0.2f - a.StartPos.Y);
            float u = SimMath.Clamp01((tr - remain) / tr);
            float targetY = a.StartPos.Y + d0 * u * u;
            float vy = (targetY - a.Pos.Y) * 6f + (remain > 0f ? 2f * d0 * u / tr : 0f);
            a.Want = new Vec2(0f, Math.Max(0f, Math.Min(vy, a.Body.EffectiveVMax * 0.97f)));
        }

        private void KickerRunUp(SimAgent a)
        {
            UserTrigger();
            if (_kicked) { a.Intent = AgentAction.Run; a.Want = Vec2.Zero; return; }
            Vec2 tee = new Vec2(S.BallX0, S.LosY);
            a.HasFace = true;
            if (_approachAt < 0f || S.Time < _approachAt)
            {
                // standing on his mark, eyes on the ball
                a.Intent = AgentAction.Idle;
                a.Want = Vec2.Zero;
                a.FaceDir = (tee - a.Pos).Norm();
                _approachFrom = a.Pos;
                return;
            }
            BeginApproach(a);
            a.Intent = AgentAction.Run;
            // 5-7 steps on a curved line that ends at the plant spot beside the ball at the contact frame
            Vec2 plant = tee + new Vec2(0.55f, -0.8f);
            float u = SimMath.Clamp01((S.Time - _approachAt) / Math.Max(0.1f, _kickTime - _approachAt));
            float shape = 1.1f;
            Vec2 target = _approachFrom + (plant - _approachFrom) * (float)Math.Pow(u, shape);
            Vec2 ff = (plant - _approachFrom) * (shape * (float)Math.Pow(Math.Max(u, 0.02f), shape - 1f) / Math.Max(0.1f, _kickTime - _approachAt));
            Vec2 want = (target - a.Pos) * 6f + ff;
            float cap = a.Body.EffectiveVMax * 0.95f;
            if (want.Length > cap) want = want.Norm() * cap;
            a.Want = want;
            Vec2 face = u > 0.75f ? (tee - a.Pos) : want;
            if (face.LenSq() > 0.04f) a.FaceDir = face.Norm();
        }

        private void DoKickoff()
        {
            _kicked = true;
            SimAgent k = _kicker;
            float kp = SimMath.Rating01(k.R.KickPower), acc = SimMath.Rating01(k.R.KickAccuracy);
            float dist, hang;
            KickMods mods = Mods();
            Vec2 aim = new Vec2(S.BallX0 + _aimX + mods.AimYards, 0f);
            switch (_kind)
            {
                case KickKind.Onside: dist = 11.5f + S.Rng.Gaussian(0f, 1.3f); hang = 1.15f; break;
                case KickKind.Squib: dist = 33f + 8f * kp + S.Rng.Gaussian(0f, 3f); hang = 1.9f; break;
                case KickKind.Rugby: dist = 42f + 10f * kp + S.Rng.Gaussian(0f, 3f); hang = 3.3f; break;
                default: dist = 52f + 22f * kp + S.Rng.Gaussian(0f, 2.5f + 3f * (1f - acc)); hang = 3.65f + 0.65f * kp; break;   // v0.7: about 4.0 s of hang on a typical deep kick (3.65 - 4.3)
            }
            if (_kind != KickKind.Onside) dist *= mods.Power;
            float latErr = S.Rng.Gaussian(0f, (0.8f + 4.5f * (1f - acc)) * mods.Spread);
            if (_kind == KickKind.Onside) latErr *= 0.5f;
            Vec2 target = new Vec2(SimMath.Clamp(aim.X + latErr, 1f, FieldGeometry.Width - 1f), Math.Min(S.LosY + dist, 108.5f));   // never past the back line
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
            // a kicker aims for still air: the wind then moves the ball (see FieldGoal compensation for the placekicker)
            Vec2 w = BallPhysics.Wind; BallPhysics.Wind = Vec2.Zero;
            try { return SolveKickCore(from, target, hang); }
            finally { BallPhysics.Wind = w; }
        }

        private static Vec3 SolveKickCore(Vec3 from, Vec2 target, float hang)
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
                if ((a.ControlledByUser && a != _kicker) || a.State == AgentState.Down || a.Body.FallenTime > 0f) continue;
                if (a.State == AgentState.Engaged) continue;
                if (a.HasBall && a.Side == S.BallSide && a != _kicker) { _carrierAI.Update(a); continue; }
                if (a.Side == SimSide.Offense)
                {
                    if (a == _kicker) PunterUpdate(a);
                    else if (a == _snapper) { if (_kicked) CoverageRun(a, 0.9f); else { a.Want = Vec2.Zero; } }
                    else if (a.Assign is LaneAssignment) { if (S.Time < 0.08f) { a.Want = Vec2.Zero; a.Intent = AgentAction.Stance; } else CoverageRun(a, 0.98f); }
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
            if (_ballInHands && !_kicked && S.Time >= _kickTime && S.Ball.Holder == _kicker.Id && KickGate(_kickTime)) DoPunt();
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
            UserTrigger();
            a.Intent = AgentAction.Idle;
            a.HasFace = true; a.FaceDir = new Vec2(0f, 1f);
            if (_kicked) { a.Want = Vec2.Zero; return; }
            // take two steps forward into the kick (a user punter stands until he presses)
            if (_ballInHands && _approachAt >= 0f && S.Time >= _approachAt) { BeginApproach(a); a.Want = new Vec2(0f, 1f) * 3.0f; }
            else if (_ballInHands) a.Want = Vec2.Zero;
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
                float pBlock = 0.20f + 0.15f * SimMath.Rating01(blocker.R.Jumping) - 0.10f * SimMath.Rating01(k.R.Awareness);   // about one punt in fifty
                if (blocker.Pos.Y < from.Y - 0.4f) pBlock *= 0.3f;   // he has to be in front of the kicker
                if (S.Rng.NextFloat() < pBlock) { BlockKick(k, blocker, from); return; }
            }
            KickMods mods = Mods();
            float flight = (44f + 22f * kp) * (1f - 0.25f * pressure) * mods.Power + S.Rng.Gaussian(0f, (2.5f + 4f * (1f - acc)) * mods.Spread);
            float hang = (3.75f + 1.1f * kp) * (1f - 0.12f * pressure);
            var ka = (KickAssignment)S.OffPlay.Assignments[0];
            float aimX = S.BallX0 + ka.AimX + mods.AimYards;
            if (_kind == KickKind.Rugby) { flight *= 0.78f; hang *= 0.82f; }
            float latErr = S.Rng.Gaussian(0f, (0.8f + 3.2f * (1f - acc)) * mods.Spread);
            Vec2 target = new Vec2(SimMath.Clamp(aimX + latErr, 2f, FieldGeometry.Width - 2f), from.Y + flight);
            Vec3 vel = SolveKick(from, target, hang);
            S.Ball.Launch(BallState.Kick, from, vel, k.Id);
            S.Ball.IsKick = true;
            KickCommon(k, from);
            _kickPoint = from;
            S.KickDistance = flight;
            S.Blocks.ReleaseAll();
            // the return team picks up the coverage while the ball is in the air
            S.Blocks.BlockerSideOverride = SimSide.Defense;
            S.Blocks.ForcePassMode = false;
            S.Blocks.HasAnchor = false;
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
                if ((a.ControlledByUser && a != _kicker) || a.State == AgentState.Down || a.Body.FallenTime > 0f) continue;
                if (a.State == AgentState.Engaged) continue;
                if (a.HasBall && a != _holder && a.Side == S.BallSide) { _carrierAI.Update(a); continue; }
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
            if (_ballInHands && !_kicked && S.Time >= _kickTime && S.Ball.Holder == _holder.Id && KickGate(_kickTime)) DoFieldGoal();
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
            UserTrigger();
            if (_kicked) { a.Intent = AgentAction.Run; a.Want = Vec2.Zero; return; }
            a.HasFace = true;
            Vec2 spot = _holder.Pos + new Vec2(0f, 0.4f);
            if (!_ballInHands || _approachAt < 0f || S.Time < _approachAt)
            {
                // on his mark beside the holder, watching the ball come back
                a.Intent = AgentAction.Idle;
                a.Want = Vec2.Zero;
                a.FaceDir = (spot - a.Pos).LenSq() > 0.04f ? (spot - a.Pos).Norm() : new Vec2(0f, 1f);
                _approachFrom = a.Pos;
                return;
            }
            BeginApproach(a);
            a.Intent = AgentAction.Run;
            // 2-3 steps in, planting beside the ball at the contact frame
            Vec2 plant = _holder.Pos + new Vec2(0.6f, -0.6f);
            float span = Math.Max(0.1f, _kickTime - _approachAt);
            float u = SimMath.Clamp01((S.Time - _approachAt) / span);
            const float shape = 1.15f;
            Vec2 target = _approachFrom + (plant - _approachFrom) * (float)Math.Pow(u, shape);
            Vec2 ff = (plant - _approachFrom) * (shape * (float)Math.Pow(Math.Max(u, 0.02f), shape - 1f) / span);
            Vec2 want = (target - a.Pos) * 7f + ff;
            float cap = a.Body.EffectiveVMax * 0.6f;
            if (want.Length > cap) want = want.Norm() * cap;
            a.Want = want;
            a.FaceDir = u > 0.6f ? (spot - a.Pos).Norm() : (want.LenSq() > 0.04f ? want.Norm() : new Vec2(0f, 1f));
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
                if (S.Rng.NextFloat() < pBlock) { BlockKick(k, blocker, from); return; }   // (BlockKick already counts the loose ball once)
            }
            KickMods mods = Mods();
            float dist0 = S.KickDistance;
            float vmax = (23.5f + 10.5f * kp) * mods.Power;
            vmax *= 1f + S.Rng.Gaussian(0f, 0.012f * (2f - acc));
            // lateral miss angle: gets worse with distance, rush pressure and poor accuracy (and a mistimed user kick)
            float baseSd = SimMath.Lerp(0.094f, 0.036f, acc) * (1f + 0.012f * Math.Max(0f, dist0 - 40f)) * (1f + 0.35f * pressure) * mods.Spread;
            float err = S.Rng.Gaussian(0f, baseSd) + mods.AimAngle;
            float aimX = FieldGeometry.CenterX;
            // trajectory scatter: the ball clears the crossbar by a yard or two on a good kick, barely (or hits it) on a poor one
            float zAim = 4.9f + S.Rng.Gaussian(0f, 0.55f * mods.Spread * (1.5f - acc));
            Vec3 aim = new Vec3(aimX + (float)Math.Tan(err) * dist0, GoalPostY, Math.Max(2.9f, zAim));
            Vec3 vel;
            // a field goal is a steep kick: fix the launch elevation, find the speed that clears the crossbar, and fall short if the leg can't supply it
            float theta = SimMath.Clamp(0.78f - dist0 * 0.0045f, 0.50f, 0.72f);
            float vReq = SolveSpeedAtAngle(from, aim, theta);
            if (!mods.User && (S.Wind.X != 0f || S.Wind.Y != 0f))
            {
                // a CPU kicker allows for the wind: part of the drift is aimed off and part of the range is added or taken away
                float comp = 0.45f + 0.40f * acc;
                float vWind = SolveSpeedAtAngleCore(from, aim, theta);
                float drift = FlightPlaneX(from, aim, theta, vReq) - aim.X;
                aim = new Vec3(aim.X - comp * drift, aim.Y, aim.Z);
                vReq = vReq + comp * (vWind - vReq);
            }
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
            // QA-448: if the designated returner is down or gone, any receiving-team player can field the ball
            bool returnerUp = false;
            for (int i = 11; i < 22; i++) { SimAgent r = S.Agents[i]; if (r.Assign is ReturnerAssignment && r.Body.FallenTime <= 0.3f && r.State != AgentState.Down) { returnerUp = true; break; } }
            for (int i = 11; i < 22 && ball.State == BallState.Kick; i++)
            {
                SimAgent a = S.Agents[i];
                if ((!(a.Assign is ReturnerAssignment) && returnerUp) || a.Body.FallenTime > 0.3f || a.State == AgentState.Down) continue;
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
            var ra = a.Assign as ReturnerAssignment;
            float p = 0.935f + 0.055f * skill - 0.03f * SimMath.Clamp01(a.Speed / Math.Max(1f, a.Body.VMax));
            if (ball.Pos.Z > a.Body.HeightYd * 1.2f || ball.Pos.Z < 0.5f) p -= 0.04f;
            if (S.Rng.NextFloat() > p)
            {
                // muff
                S.LogEvent(SimEventType.Drop, a.Id, -1, ball.Pos);
                ball.MakeLoose(ball.Pos, new Vec3(S.Rng.Gaussian(0f, 2.5f), S.Rng.Gaussian(0f, 2.5f) + 1f, 1.8f));
                ball.IsKick = true;
                S.KickLandY = ball.Pos.Y;
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
            if (S.Kind == PlayKind.Punt)
            {
                float nearest = 99f;
                for (int i = 0; i < 11; i++) { SimAgent o = S.Agents[i]; if (o.State != AgentState.Down) nearest = Math.Min(nearest, (o.Pos - a.Pos).Length); }
                float aw = SimMath.Rating01(a.R.Awareness);
                // a probability from the nearest gunner's distance, the call and his awareness (no deterministic distance trigger)
                float pf = nearest < 8f ? 0.92f : nearest < 14f ? 0.78f : nearest < 20f ? 0.58f : nearest < 28f ? 0.30f : 0.08f;   // calibrated to ~35% fair catches (coverage arrives 5-17 yd away: median 17)
                if (ra != null && !ra.FairCatchAllowed) pf *= 0.50f;
                pf = SimMath.Clamp01(pf + 0.10f * (1f - aw) - 0.05f);
                fair = S.Rng.NextFloat() < pf && ball.Pos.Z > 0f;
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
            var order = new System.Collections.Generic.List<SimAgent>(22);
            for (int i = 0; i < 22; i++) order.Add(S.Agents[i]);
            order.SortStable((p, q) => (p.Pos - ball.Pos.XY).LenSq().CompareTo((q.Pos - ball.Pos.XY).LenSq()));
            for (int oi = 0; oi < order.Count; oi++)
            {
                SimAgent a = order[oi];
                { float fl; if (_fieldLock.TryGetValue(a.Id, out fl) && S.Time < fl) continue; }
                if (a.Body.FallenTime > 0.3f || a.State == AgentState.Down || a.State == AgentState.Engaged) continue;
                float d = (a.Pos - ball.Pos.XY).Length;
                if (d > a.Body.Radius + 0.9f || ball.Pos.Z > 1.6f) continue;
                bool receiving = a.Side == SimSide.Defense;
                if (receiving)
                {
                    float p = 0.62f + 0.30f * SimMath.Rating01(a.R.Catching);
                    if (a.Assign is ReturnerAssignment) p += 0.1f;
                    if (S.Kind == PlayKind.Kickoff) _fieldLock[a.Id] = S.Time + 0.55f;
                    if (ball.Vel.Length > 7f) p -= 0.15f;
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
                    float p = 0.09f + 0.12f * SimMath.Rating01(a.R.Catching) + 0.03f * SimMath.Rating01(a.R.Awareness); // diving on a bouncing ball in traffic: expected onside kicks are recovered about one time in six
                    _fieldLock[a.Id] = S.Time + 0.55f;
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
            Vec2 w = BallPhysics.Wind; BallPhysics.Wind = Vec2.Zero;
            try { return SolveSpeedAtAngleCore(from, aim, theta); }
            finally { BallPhysics.Wind = w; }
        }

        private static float SolveSpeedAtAngleCore(Vec3 from, Vec3 aim, float theta)
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
namespace PixFootball.Sim
{
    internal sealed partial class SpecialTeamsAI
    {
        /// <summary>
        /// Hands team: when the kick is an expected onside kick the receiving team lines up its front row a few yards past the ten-yard
        /// restraining line (a surprise onside keeps the normal return alignment, which is why it is far more likely to be recovered).
        /// </summary>
        private void AlignHandsTeam()
        {
            if (S.Kind != PlayKind.Kickoff || _kind != KickKind.Onside) return;
            bool surprise = S.OffPlay.Name != null && S.OffPlay.Name.IndexOf("Surprise", StringComparison.OrdinalIgnoreCase) >= 0;
            float rowAdd = surprise ? 5.0f : 0f; // a surprise kick catches the front row a few yards deeper
            var rows = new System.Collections.Generic.List<SimAgent>();
            for (int i = 11; i < 22; i++) if (!(S.Agents[i].Assign is ReturnerAssignment)) rows.Add(S.Agents[i]);
            rows.SortStable((p, q) => p.StartPos.X.CompareTo(q.StartPos.X));
            float[] frontX = { -17f, -10.5f, -4f, 4f, 10.5f, 17f };
            for (int k = 0; k < rows.Count; k++)
            {
                SimAgent a = rows[k];
                Vec2 p = k < 6 ? new Vec2(S.BallX0 + frontX[k], S.LosY + 10.8f + rowAdd + (k % 2) * 0.8f)
                               : new Vec2(S.BallX0 + (k - 6 - 1) * 9f, S.LosY + 17f + rowAdd);
                p = new Vec2(SimMath.Clamp(p.X, 1f, FieldGeometry.Width - 1f), p.Y);
                a.Body.Pos = p; a.StartPos = p; a.Body.Vel = Vec2.Zero; a.ClearHistory(p);
            }
        }

        /// <summary>A hands-team man goes after the onside kick: he works to where it will come down and fields it.</summary>
        private void HandsTeamPursue(SimAgent a)
        {
            a.Intent = AgentAction.Run;
            Vec2 tgt = S.Ball.Pos.XY;
            if (S.Ball.State == BallState.Kick)
            {
                Vec3 at; float tt;
                if (BallPhysics.PredictAtHeight(S.Ball.Pos, S.Ball.Vel, 0.8f, out at, out tt)) tgt = at.XY + S.Ball.Vel.XY.Norm() * 2.0f;
            }
            else tgt = tgt + S.Ball.Vel.XY * 0.25f;
            Vec2 to = tgt - a.Pos;
            a.Want = to.Norm() * a.Body.EffectiveVMax * 0.97f;
            if (to.Length < 0.4f) a.Want = Vec2.Zero;
            a.HasFace = true; a.FaceDir = to.LenSq() > 0.2f ? to.Norm() : new Vec2(0f, -1f);
        }
    }
}
namespace PixFootball.Sim
{
    internal sealed partial class SpecialTeamsAI
    {
        /// <summary>The kicking-unit player holding a role (kicker, holder, snapper) by assignment; a formation that orders its slots differently, or a flipped or edited play, still names the right man. Falls back to the classic slot.</summary>
        private SimAgent AgentWithRole(KickRole role, int fallbackSlot)
        {
            for (int i = 0; i < 11; i++)
            {
                var ka = S.Agents[i].Assign as KickAssignment;
                if (ka != null && ka.Role == role) return S.Agents[i];
            }
            return S.Agents[fallbackSlot];
        }
    }
}
