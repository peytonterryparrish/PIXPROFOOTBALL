using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    public sealed partial class PlaySimulation
    {
        // ===================================================================================================
        // Possession mechanics
        // ===================================================================================================

        internal void GiveBall(SimAgent a)
        {
            for (int i = 0; i < 22; i++) Agents[i].HasBall = false;
            a.HasBall = true;
            Ball.Hold(a.Id, BallHandPos(a));
            a.Protecting = false;
            if (a.Side == SimSide.Offense) LastOffenseCarrier = a.Id;
        }

        internal Vec3 BallHandPos(SimAgent h)
        {
            float z = 1.05f;
            Vec2 p = h.Pos + h.Facing * 0.28f;
            if (h.Side == SimSide.Offense && h.Slot == 0 && !PassThrown && (h.QbState == QbPhase.Set || h.QbState == QbPhase.Windup || h.QbState == QbPhase.Scramble || h.QbState == QbPhase.Dropback))
                z = 1.75f;
            if (h.Move == CarrierMove.Dive && h.MoveTime > 0f) p = p + h.MoveDir * 0.85f;
            if (h.Body.FallenTime > 0f) z = 0.4f;
            return new Vec3(p, z);
        }

        private void UpdateHeldBall()
        {
            if (Ball.Holder < 0) return;
            SimAgent h = Agents[Ball.Holder];
            Vec3 p = BallHandPos(h);
            Ball.PrevPos = Ball.Pos;
            Ball.Pos = p;
            Ball.Vel = new Vec3(h.Vel, 0f);
        }

        internal void Handoff(SimAgent from, SimAgent to)
        {
            GiveBall(to);
            RusherId = to.Id;
            Blocks.HandedOff = true;
            PlayAction(from, AgentAction.Handoff, 0.35f);
            PlayAction(to, AgentAction.Handoff, 0.3f);
            LogEvent(SimEventType.Handoff, from.Id, to.Id);
        }

        internal void PitchBall(SimAgent from, SimAgent to)
        {
            Vec3 a = new Vec3(from.Pos, 1.2f);
            float dist = (to.Pos - from.Pos).Length;
            float t = Math.Max(0.38f, dist / 15f);
            Vec2 target = to.Pos + to.Vel * t;
            Vec3 tgt = new Vec3(target, 1.3f);
            Vec3 d = tgt - a;
            Vec3 vel = new Vec3(d.X / t, d.Y / t, (d.Z + 0.5f * SimMath.Gravity * t * t) / t);
            for (int i = 0; i < 22; i++) Agents[i].HasBall = false;
            Ball.Launch(BallState.Pitch, a, vel, from.Id);
            Blocks.HandedOff = true;
            Ball.Intended = to.Id;
            PlayAction(from, AgentAction.Throw, 0.3f);
            LogEvent(SimEventType.Pitch, from.Id, to.Id);
        }

        internal void OnPassReleased(SimAgent q, SimAgent target, bool throwaway)
        {
            PassThrown = true;
            Catches.ResetPass();
            PasserId = q.Id;
            TargetId = target != null ? target.Id : -1;
            PassThrowY = q.Pos.Y;
            Ball.Intended = TargetId;
            q.HasBall = false;
            PlayAction(q, AgentAction.Throw, 0.5f);
            LogEvent(SimEventType.Throw, q.Id, TargetId, new Vec3(Ball.Pos.X, Ball.Pos.Y, Ball.Pos.Z), TackleType.None, Ball.Vel.Length, throwaway ? "away" : null);
            DefAI.OnPassThrown();
        }

        internal void TryJump(SimAgent a)
        {
            if (a.JumpTime > 0f || a.Body.FallenTime > 0f) return;
            a.JumpTime = 0.62f;
            PlayAction(a, AgentAction.Jump, 0.55f);
        }

        // ===================================================================================================
        // Fumbles and loose balls
        // ===================================================================================================

        internal void Fumble(SimAgent carrier, SimAgent forcer, Vec2 impulse)
        {
            if (Ball.Holder != carrier.Id) return;
            carrier.HasBall = false;
            Fumbled = true;
            FumbleCount++;
            Vec3 pos = Ball.Pos;
            Vec2 v = carrier.Vel * 0.45f + impulse + new Vec2(Rng.Gaussian(0f, 1.6f), Rng.Gaussian(0f, 1.6f));
            Ball.MakeLoose(new Vec3(pos.X, pos.Y, Math.Max(0.5f, pos.Z)), new Vec3(v, Rng.Range(1.5f, 4.5f)));
            FumbleSpotY = pos.Y;
            LogEvent(SimEventType.Fumble, carrier.Id, forcer != null ? forcer.Id : -1);
            if (carrier.State == AgentState.Down) carrier.State = AgentState.Normal;
        }

        private void UpdateLooseBall()
        {
            // airborne tipped passes are handled as catches; ground fumbles are scooped
            if (PassThrown && PassTipped && !PassCompleted && !Fumbled)
            {
                Catches.CheckTipped();
                if (Phase != PlayPhase.Live) return;
                if (Ball.GroundContactThisStep || Ball.AtRest) { Ball.Kill(); EndPlay(EndReason.Incomplete); }
                return;
            }
            if (Ball.State != BallState.Loose) return;
            // out of bounds?
            if (Ball.Pos.X < 0f || Ball.Pos.X > FieldGeometry.Width)
            {
                OutOfBounds = true;
                Ball.Kill();
                EndPlay(EndReason.OutOfBounds);
                return;
            }
            if (Ball.Pos.Y < -10f) { Ball.Kill(); EndPlay(EndReason.OutOfBounds); return; }
            if (Ball.Pos.Y > 110f) { Ball.Kill(); EndPlay(EndReason.OutOfBounds); return; }

            if (Ball.Pos.Z > 1.6f) return;
            // contest for the loose ball
            SimAgent best = null; float bestScore = 0f;
            float speed = Ball.Vel.Length;
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Agents[i];
                if (a.Body.FallenTime > 0.45f || a.State == AgentState.Down) continue;
                float d = (a.Pos - Ball.Pos.XY).Length;
                float reach = a.Body.Radius + 0.85f + (a.Body.Speed > 4f ? 0.2f : 0.35f);
                if (d > reach) continue;
                float skill = (a.R.Awareness * 0.25f + a.R.Catching * 0.30f + a.R.Carrying * 0.25f + a.R.Agility * 0.20f) / 99f;
                float prox = 1f - d / reach;
                float eng = a.State == AgentState.Engaged ? 0.25f : 1f;
                float score = (0.35f + 0.65f * skill) * (0.4f + 0.6f * prox) * eng * (a.Body.StumbleTime > 0f ? 0.7f : 1f) + Rng.NextFloat() * 0.35f;
                if (score > bestScore) { bestScore = score; best = a; }
            }
            if (best == null) return;
            if (speed > 7f && Rng.NextFloat() < 0.55f) return; // too hot to handle this tick
            Recover(best);
        }

        private void Recover(SimAgent a)
        {
            bool turnover = a.Side != BallSide;
            GiveBall(a);
            LogEvent(SimEventType.Recovery, a.Id, -1, TackleType.None, turnover ? 1f : 0f);
            PlayAction(a, AgentAction.Catch, 0.3f);
            if (turnover)
            {
                BallSide = a.Side;
                Turnover = true;
                TurnoverFumble = true;
                TurnoverSpotY = a.Pos.Y;
                Blocks.ReleaseAll();
            }
            // fall on it unless there is open field (scoop and score)
            bool opponentsNear = false;
            for (int i = 0; i < 22; i++)
            {
                SimAgent o = Agents[i];
                if (o.Side == a.Side || o.State == AgentState.Down) continue;
                if ((o.Pos - a.Pos).Length < 3.8f) { opponentsNear = true; break; }
            }
            bool scoop = !opponentsNear && Rng.NextFloat() < 0.55f;
            if (!scoop && (opponentsNear || Rng.NextFloat() < 0.6f))
            {
                a.State = AgentState.Down;
                a.Body.Fall(0.9f);
                TacklerId = -1;
                EndPlay(turnover ? EndReason.FumbleLost : EndReason.Tackle);
            }
        }

        // ===================================================================================================
        // Per-tick ball update
        // ===================================================================================================

        private void UpdateBall()
        {
            if (Ball.State == BallState.Held) { UpdateHeldBall(); return; }
            if (Ball.State == BallState.Dead) return;
            Ball.Step(Dt);

            switch (Ball.State)
            {
                case BallState.Snap: UpdateSnap(); break;
                case BallState.Pass:
                    Catches.CheckPass();
                    if (Phase != PlayPhase.Live) return;
                    if (Ball.State == BallState.Pass) CheckPassGround();
                    break;
                case BallState.Pitch: Catches.CheckPitch(); if (Phase == PlayPhase.Live && Ball.GroundContactThisStep && Ball.State == BallState.Pitch) { Ball.State = BallState.Loose; LogEvent(SimEventType.Fumble, Ball.Thrower, -1); Fumbled = true; FumbleCount++; FumbleSpotY = Ball.Pos.Y; } break;
                case BallState.Kick: Specials.UpdateKickBall(); break;
                case BallState.Loose:
                    if (IsKickLoose) Specials.UpdateLooseKick(); else UpdateLooseBall();
                    break;
            }
        }

        internal bool IsKickLoose { get { return Ball.IsKick; } }

        private void UpdateSnap()
        {
            SimAgent q = Agents[0];
            if (Kind == PlayKind.Punt || Kind == PlayKind.FieldGoal) { Specials.UpdateSnap(); return; }
            float d = (q.Pos - Ball.Pos.XY).Length;
            if (d < 1.0f && Ball.Pos.Z < 2.4f)
            {
                // a bad snap is rare and tied to the center's awareness
                SimAgent c = Agents[4];
                float pBad = 0.0018f * (1.6f - SimMath.Rating01(c.R.Awareness));
                if (Rng.Chance(pBad)) { Ball.MakeLoose(Ball.Pos, new Vec3(Ball.Vel.XY * 0.3f, 1f)); Fumbled = true; FumbleCount++; FumbleSpotY = Ball.Pos.Y; LogEvent(SimEventType.Fumble, c.Id, -1, TackleType.None, 0f, "snap"); return; }
                GiveBall(q);
                return;
            }
            if (Ball.GroundContactThisStep) { Ball.State = BallState.Loose; Fumbled = true; FumbleCount++; FumbleSpotY = Ball.Pos.Y; LogEvent(SimEventType.Fumble, Agents[4].Id, -1, TackleType.None, 0f, "snap"); }
        }

        private void CheckPassGround()
        {
            if (!Ball.GroundContactThisStep && Ball.Pos.Z > BallPhysics.BallRadius + 0.02f)
            {
                // sideline / endline while airborne
                return;
            }
            if (Ball.GroundContactThisStep)
            {
                bool oob = Ball.Pos.X < 0f || Ball.Pos.X > FieldGeometry.Width || Ball.Pos.Y > 110f || Ball.Pos.Y < -10f;
                LogEvent(SimEventType.Incomplete, PasserId, TargetId, new Vec3(Ball.Pos.X, Ball.Pos.Y, 0f), TackleType.None, oob ? 1f : 0f);
                Ball.Kill();
                EndPlay(EndReason.Incomplete);
            }
        }

        // ===================================================================================================
        // Boundaries and scoring
        // ===================================================================================================

        private void CheckBoundsAndScoring()
        {
            SimAgent c = Carrier;
            if (c == null) return;
            float dirY = c.Side == SimSide.Offense ? 1f : -1f;
            float ballY = c.Pos.Y + dirY * (c.Move == CarrierMove.Dive && c.MoveTime > 0f ? 0.8f : 0.25f);

            // out of bounds laterally
            if (c.Pos.X < 0f || c.Pos.X > FieldGeometry.Width)
            {
                OutOfBounds = true;
                LogEvent(SimEventType.OutOfBounds, c.Id, -1);
                EndPlay(EndReason.OutOfBounds);
                return;
            }
            if (c.Side == SimSide.Offense)
            {
                if (ballY >= 100f)
                {
                    Touchdown = true;
                    LogEvent(SimEventType.Touchdown, c.Id, -1);
                    PlayAction(c, AgentAction.Celebrate, 3f);
                    EndPlay(EndReason.Touchdown);
                    return;
                }
                if (c.Pos.Y < -10.5f) { SafetyScored = true; LogEvent(SimEventType.Safety, c.Id, -1); EndPlay(EndReason.Safety); return; }
            }
            else
            {
                if (ballY <= 0f)
                {
                    Touchdown = true; DefensiveTd = true;
                    LogEvent(SimEventType.Touchdown, c.Id, -1);
                    PlayAction(c, AgentAction.Celebrate, 3f);
                    EndPlay(EndReason.Touchdown);
                    return;
                }
                if (c.Pos.Y > 110.5f) { LogEvent(SimEventType.OutOfBounds, c.Id, -1); EndPlay(EndReason.OutOfBounds); return; }
            }
        }

        private void CheckFirstDown()
        {
            if (FirstDownCrossed || BallSide != SimSide.Offense) return;
            SimAgent c = Carrier;
            float y;
            if (c != null) y = c.Pos.Y + 0.25f; else if (Ball.State == BallState.Pass) return; else return;
            if (State.Phase == GamePhase.Scrimmage && y >= LineToGainY && LineToGainY > LosY + 0.5f)
            {
                FirstDownCrossed = true;
                LogEvent(SimEventType.FirstDownCrossed, c.Id, -1);
            }
        }
    }
}
