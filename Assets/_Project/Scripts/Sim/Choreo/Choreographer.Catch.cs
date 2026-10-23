using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    public sealed partial class Choreographer
    {
        /// <summary>Seconds before the catch at which the take is booked (the view starts the clip then so its hand-close frame lands on the ball).</summary>
        public const float CatchLead = 0.65f;

        struct CatchForecast { public bool Active; public float Eta; public Vec3 At; public Vec2 AgentAt; }
        readonly CatchForecast[] _cf = new CatchForecast[22];
        readonly ChoreoBooking[] _catchBook = new ChoreoBooking[22];

        public float CatchEta(int agent) { return agent >= 0 && agent < 22 && _cf[agent].Active ? _cf[agent].Eta : -1f; }
        /// <summary>The catch booking of an agent (receiver or defender), null when none is live.</summary>
        public ChoreoBooking CatchBookingOf(int agent) { return agent >= 0 && agent < 22 ? _catchBook[agent] : null; }

        // ===================================================================================================
        // Forecast: where and when the ball meets whom
        // ===================================================================================================
        void ForecastCatches()
        {
            for (int i = 0; i < 22; i++) _cf[i].Active = false;
            BallSim ball = S.Ball;
            if (_lib == null || ball.State != BallState.Pass) { ExpireCatches(false); return; }
            int tgt = S.TargetId;
            if (tgt >= 0 && tgt < 22) PredictCatch(S.Agents[tgt], false);
            // the defender with the best play on the ball: the smallest miss distance among those who can reach it inside the window
            SimAgent best = null; float bestMiss = 2.2f;
            Vec2 dirb = ball.Vel.XY.Norm();
            for (int i = 11; i < 22; i++)
            {
                SimAgent d = S.Agents[i];
                if (d.State == AgentState.Down || d.Body.FallenTime > 0f || d.State == AgentState.Engaged) continue;
                if (d.Assign is RushAssignment || d.Assign is BlitzAssignment) continue;
                Vec2 to = d.Pos - ball.Pos.XY;
                float along = -(to.Dot(dirb));
                if (along <= 0f || along > 14f) continue;
                float perp = Math.Abs(to.X * dirb.Y - to.Y * dirb.X);
                if (perp < bestMiss) { bestMiss = perp; best = d; }
            }
            if (best != null) PredictCatch(best, true);
            ExpireCatches(true);
            SteerCatchers();
        }

        void PredictCatch(SimAgent a, bool defender)
        {
            if (a.Body.FallenTime > 0f || a.State == AgentState.Down) return;
            BallSim ball = S.Ball;
            Vec3 p = ball.Pos, v = ball.Vel;
            Vec2 av = a.Want.Length > 0.5f ? (a.Vel + a.Want) * 0.5f : a.Vel;
            float reachMax = S.Catches.StandReach(a) + S.Catches.Leap(a) + 0.1f;
            float rr = a.Body.Radius + 0.5f + 0.1f;
            int steps = (int)(Horizon / PlaySimulation.Dt) + 6;
            for (int k = 1; k <= steps; k++)
            {
                BallPhysics.Advance(ref p, ref v, PlaySimulation.Dt);
                float tau = k * PlaySimulation.Dt;
                if (p.Z < 0.12f && v.Z < 0f) break;
                Vec2 ap = a.Pos + av * tau;
                if ((p.XY - ap).Length <= rr && p.Z >= 0.15f && p.Z <= reachMax)
                {
                    _cf[a.Id] = new CatchForecast { Active = true, Eta = tau, At = p, AgentAt = ap };
                    Stats.Forecasts++;
                    if (tau <= CatchLead) { if (!defender) ContestForecast(a, _cf[a.Id]); BookCatch(a, defender, _cf[a.Id]); }
                    return;
                }
            }
        }

        void ExpireCatches(bool passFlying)
        {
            for (int i = 0; i < 22; i++)
            {
                var b = _catchBook[i];
                if (b == null) continue;
                bool held = S.Ball.State == BallState.Held;
                bool stale;
                if (passFlying) stale = _t > b.SyncSim + 0.5f;
                else stale = held ? _t > b.SyncSim + 1.5f : _t > b.SyncSim + 0.25f;
                if (stale || b.Phase == ChoreoPhase.Done)
                {
                    b.Phase = ChoreoPhase.Done;
                    if (b.OwnsA && _byAgent[i] == b) _byAgent[i] = null;
                    b.OwnsA = false;
                    _catchBook[i] = null;
                }
            }
        }

        // ===================================================================================================
        // Booking: the take, its placement and the steering that gets the hands to the ball
        // ===================================================================================================
        /// <summary>A catch in traffic: the receiver is nearly stationary, a defender is within a yard of him and the ball is low (the contested-catch pair of the contact director plays it).</summary>
        bool IsDuel(SimAgent r, CatchForecast f)
        {
            if (r.Vel.Length * 0.9144f > 2.0f || f.At.Z > 3.0f || r.Id < 1 || r.Id >= 11) return false;
            for (int i = 11; i < 22; i++)
            {
                SimAgent d = S.Agents[i];
                if (d.State == AgentState.Down || d.State == AgentState.Engaged || d.Body.FallenTime > 0f) continue;
                if ((d.Pos - f.AgentAt).Length < 1.0f) return true;
            }
            return false;
        }

        /// <summary>Lateral component of a vector in a heading's frame: + = to the right of the heading.</summary>
        static float Lateral(Vec2 rel, Vec2 heading) { return rel.X * heading.Y - rel.Y * heading.X; }

        void BookCatch(SimAgent a, bool defender, CatchForecast f)
        {
            if (!defender && (DebugNoDuel ? IsDuel(a, f) : DuelWon(a.Id))) return;      // a duel the receiver wins is the booked contested-catch pair (v0.10.0), the plain take is not booked
            var existing = _catchBook[a.Id];
            Vec2 headingV = a.Vel.Length > 1.7f ? a.Vel.Norm() : a.Facing;
            float vm = a.Vel.Length * 0.9144f;
            Vec2 rel = f.At.XY - f.AgentAt;
            float L = Lateral(rel, headingV) * 0.9144f;
            float F = rel.Dot(headingV) * 0.9144f;
            float H = f.At.Z * 0.9144f;
            if (existing != null && existing.Phase != ChoreoPhase.Done)
            {
                // refine: the time and the point follow the forecast; the take stays unless the ball has ended up far from what it was picked for
                if (Math.Abs(H - existing.ClipA.HandY) < 0.8f) { RefineCatch(existing, a, f, headingV, vm); return; }
                if (existing.OwnsA) return;                      // a body already in a standing take is not swapped
                existing.Phase = ChoreoPhase.Done;
            }
            var list = defender ? _lib.Intercepts : _lib.Catches;
            ChoreoClip best = null; float bs = float.MinValue;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c.Count < 2 || c.ContactT <= 0.05f || c.HandY <= 0.2f || c.Length > 3.5f) continue;
                float score = -Math.Abs(H - c.HandY) * 2.4f;
                score -= Math.Max(0f, Math.Abs(L - c.HandX) - 0.35f) * 1.2f;
                score -= Math.Abs(F - c.HandZ) * 0.4f;
                score -= Math.Abs(vm - c.RootSpeed(c.ContactT)) * 0.35f;
                if (c.MinHips < 0.5f) score -= (Math.Abs(L) > 1.6f || H < 0.55f) ? 0.3f : 3.0f;       // a dive only for a ball that is far out or on the grass
                if (c.HandY > 2.0f) score -= H > 1.9f ? 0f : 1.2f;                                    // a jump only for a high ball
                score += ChoreoMath.Hash01(a.Id * 17 + (defender ? 3 : 1), ChoreoMath.HashString(c.Name), S.PasserId) * 0.4f;
                if (score > bs) { bs = score; best = c; }
            }
            if (best == null) return;
            var b = new ChoreoBooking { Kind = ChoreoKind.Catch, Phase = ChoreoPhase.Playing, A = a.Id, ClipA = best, Rate = GamePace.AnimationRate, Defender = defender };
            b.Eta = f.Eta; b.SyncSim = _t + f.Eta; b.SyncPos = f.At.XY; b.HandPoint = f.At; b.HandCloseT = best.ContactT;
            PlaceCatch(b, a, f, headingV, vm);
            Add(b);
            _catchBook[a.Id] = b;
            Stats.CatchBookings++;
        }

        void PlaceCatch(ChoreoBooking b, SimAgent a, CatchForecast f, Vec2 headingV, float vm)
        {
            ChoreoClip c = b.ClipA;
            float rate = b.Rate;
            // the take starts so that its hand-close frame lands on the predicted catch (when the lead-in is longer than the time left it starts partway in; when shorter it waits)
            float startAhead = f.Eta * rate;
            b.StartT = Math.Max(0f, c.ContactT - startAhead);
            b.StartSim = b.SyncSim - (c.ContactT - b.StartT) / rate;
            b.WarpStart = _t;
            b.ExitTA = Math.Min(c.Length, Math.Min(c.SettleT > 0f ? c.SettleT : c.Length, c.ContactT + 0.2f));      // handed back as soon as the hands are secure: the receiver runs on from there
            bool owns = vm < 0.9f && c.RootSpeed(c.ContactT) < 1.4f;      // only a man who is standing: a walking or running receiver keeps his own legs and his yards after the catch (an owned receiver stops with the take)
            float hCatch = vm > 2.0f ? ChoreoMath.YawOf(headingV) : (S.Ball.Vel.XY.LenSq() < 0.5f ? ChoreoMath.YawOf(a.Facing) : ChoreoMath.YawOf(S.Ball.Vel.XY * -1f));
            Vec2 ho = ChoreoMath.Rotate(new Vec2(c.HandX, c.HandZ) * ChoreoClip.MetersToYards, hCatch);
            Vec2 hipsWorld = f.At.XY - ho;
            b.Align = ChoreoMath.Align(c.Hips(c.ContactT), c.YawAt(c.ContactT), hipsWorld, hCatch);
            if (owns && _byAgent[a.Id] == null && !a.ControlledByUser)
            {
                // a standing catch: the body follows the take. It starts where the take wants him at the clip start; the difference is warped out over the approach (never a snap)
                Vec2 r0 = b.Align.Apply(c.Root(b.StartT));
                b.ResidA = a.Pos - r0; b.SlideA = a.Vel - b.Align.RotateOnly(c.RootVelocity(b.StartT)) * rate;
                b.SlideA = b.SlideA.ClampLen(SlideMaxV);
                b.WarpOut = Math.Max(0.3f, Math.Min(f.Eta + 0.15f, b.ResidA.Length / 2.5f));
                b.OwnsA = true; _byAgent[a.Id] = b;
            }
        }

        // The forecast of the catch moves a little every tick: the clip is re-timed (its speed, within +-25 percent) so its hand-close frame stays on the ball, and a take that does not own the body is re-aimed.
        void RefineCatch(ChoreoBooking b, SimAgent a, CatchForecast f, Vec2 headingV, float vm)
        {
            ChoreoClip c = b.ClipA;
            b.Eta = f.Eta; b.SyncPos = f.At.XY; b.HandPoint = f.At;
            float ctNow = b.ClipTime(_t);
            if (ctNow < c.ContactT - 0.04f && f.Eta > 0.05f && _t >= b.StartSim)
            {
                float need = (c.ContactT - ctNow) / f.Eta;
                float baseRate = GamePace.AnimationRate;
                float want = SimMath.Clamp(need, baseRate * 0.75f, baseRate * 1.3f);
                b.StartT = ctNow; b.StartSim = _t;
                b.Rate = b.Rate + (want - b.Rate) * 0.3f;
                b.SyncSim = _t + (c.ContactT - ctNow) / b.Rate;
            }
            else if (_t < b.StartSim) b.SyncSim = _t + f.Eta;
            if (!b.OwnsA)
            {
                float hCatch = vm > 2.0f ? ChoreoMath.YawOf(headingV) : ChoreoMath.YawOf(a.Facing);
                Vec2 ho = ChoreoMath.Rotate(new Vec2(c.HandX, c.HandZ) * ChoreoClip.MetersToYards, hCatch);
                b.Align = ChoreoMath.Align(c.Hips(c.ContactT), c.YawAt(c.ContactT), f.At.XY - ho, hCatch);
            }
        }

        /// <summary>
        /// Steering of the catcher who keeps running (the take plays over his legs): from the booking to the catch his root is bent a little so the booked hand point meets the ball. Bounded in
        /// speed (never more than 2.2 yd/s of bend) and in distance; a human-controlled player is never steered.
        /// </summary>
        void SteerCatchers()
        {
            if (DebugNoSteer || DebugNoSteerCatch || !SteerCatchersOn) return;      // off by default: bending the receivers toward the ball cost 0.16 yd per pass (9000 passes, paired); the hand-close frame is synced by the clip re-timing
            for (int i = 0; i < 22; i++)
            {
                var b = _catchBook[i];
                if (b == null || b.OwnsA || b.Phase != ChoreoPhase.Playing || !_cf[i].Active) continue;
                SimAgent a = S.Agents[i];
                if (a.ControlledByUser || a.State != AgentState.Normal || a.Move != CarrierMove.None || a.Body.FallenTime > 0f) continue;
                var f = _cf[i];
                if (f.Eta < 0.05f) continue;
                ChoreoClip c = b.ClipA;
                Vec2 headingV = a.Vel.Length > 1.7f ? a.Vel.Norm() : a.Facing;
                float h = ChoreoMath.YawOf(headingV);
                Vec2 ho = ChoreoMath.Rotate(new Vec2(c.HandX, c.HandZ) * ChoreoClip.MetersToYards, h);
                Vec2 rootTarget = f.At.XY - ho;
                Vec2 err = rootTarget - f.AgentAt;
                // only the sideways part of the error is bent: along his line of travel the booking re-times the take instead (a receiver slowed or pushed along the line loses his yards after the catch)
                Vec2 side = new Vec2(headingV.Y, -headingV.X);
                err = side * err.Dot(side);
                float el = err.Length;
                if (el < 0.04f || el > 1.0f) continue;
                float gain = ChoreoMath.Smooth01(1f - f.Eta / CatchLead);
                Vec2 dv = err * (1f / Math.Max(0.12f, f.Eta)) * gain * 0.5f * CatcherSteerScale;
                if (dv.Length > 1.6f * CatcherSteerScale) dv = dv.Norm() * 1.6f * CatcherSteerScale;
                a.Want = a.Want + dv;
                if (dv.Length > Stats.MaxSteerRate) Stats.MaxSteerRate = dv.Length;
                Stats.SteeredTicks++;
            }
        }

        /// <summary>
        /// Where the catcher's hands are (field yards, height in yards) when the booking's take is at its hand-close moment: the simulation completes the catch when the ball is there, not at a generic
        /// arm's length. False when the agent has no catch booking.
        /// </summary>
        public bool TryCatchHand(SimAgent r, float ballZ, out Vec3 hand)
        {
            hand = Vec3.Zero;
            if (DebugNoHand) return false;
            var b = r.Id >= 0 && r.Id < 22 ? _catchBook[r.Id] : null;
            if (b == null || b.ClipA == null || b.Phase == ChoreoPhase.Done) return false;
            ChoreoClip c = b.ClipA;
            Vec2 xy;
            if (b.OwnsA)
            {
                // the hands are where the take's hands are at the hand-close moment
                Vec2 hipsAt = b.Align.Apply(c.Hips(c.ContactT));
                xy = hipsAt + ChoreoMath.Rotate(new Vec2(c.HandX, c.HandZ) * ChoreoClip.MetersToYards, c.YawAt(c.ContactT) + b.Align.Yaw);
            }
            else
            {
                Vec2 headingV = r.Vel.Length > 1.7f ? r.Vel.Norm() : r.Facing;
                xy = r.Pos + ChoreoMath.Rotate(new Vec2(c.HandX, c.HandZ) * ChoreoClip.MetersToYards, ChoreoMath.YawOf(headingV));
            }
            float hz = c.HandY * ChoreoClip.MetersToYards;
            if (Math.Abs(ballZ - hz) > 0.6f) hz = ballZ;                 // a ball that came in much higher or lower than the take was picked for: the hands go to it
            hand = new Vec3(xy, SimMath.Clamp(hz, 0.3f, 3.4f));
            return true;
        }
    }
}
