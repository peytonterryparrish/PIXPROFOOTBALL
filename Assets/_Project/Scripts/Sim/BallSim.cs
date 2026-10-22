using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    public enum BallState { Held, Snap, Pass, Pitch, Kick, Loose, Dead }

    /// <summary>Stateless ballistic helpers (gravity + quadratic drag) shared by the live ball and by trajectory prediction.</summary>
    public static class BallPhysics
    {
        public const float DragCoeff = 0.0045f;
        public const float BallRadius = 0.13f;
        public const float Dt = 1f / 60f;

        [ThreadStatic] private static float _windX, _windY, _windScaleDelta;
        /// <summary>Gameplay gain on the wind (real air is gentler than a football flight needs to look: 10 mph moves a punt a few yards).</summary>
        public const float WindGain = 0.95f;

        /// <summary>Wind velocity (X lateral, Y downfield, yards per second) acting on the ball in flight; (0,0) = still air. Set by the play simulation for the length of every step.</summary>
        public static Vec2 Wind
        {
            get { return new Vec2(_windX, _windY); }
            set { _windX = value.X; _windY = value.Y; }
        }

        /// <summary>Fraction of the wind a planner (quarterback, kicker) allows for when aiming (1 = perfect compensation, 0 = none).</summary>
        public static float WindScale
        {
            get { return 1f + _windScaleDelta; }
            set { _windScaleDelta = value - 1f; }
        }

        public static void Advance(ref Vec3 pos, ref Vec3 vel, float dt)
        {
            if (_windX == 0f && _windY == 0f)
            {
                float sp0 = vel.Length;
                Vec3 acc0 = new Vec3(-DragCoeff * sp0 * vel.X, -DragCoeff * sp0 * vel.Y, -SimMath.Gravity - DragCoeff * sp0 * vel.Z);
                vel = vel + acc0 * dt;
                pos = pos + vel * dt;
                return;
            }
            float wg = WindGain * (1f + _windScaleDelta);
            float rx = vel.X - _windX * wg, ry = vel.Y - _windY * wg;
            float sp = (float)Math.Sqrt(rx * rx + ry * ry + vel.Z * vel.Z);
            Vec3 acc = new Vec3(-DragCoeff * sp * rx, -DragCoeff * sp * ry, -SimMath.Gravity - DragCoeff * sp * vel.Z);
            vel = vel + acc * dt;
            pos = pos + vel * dt;
        }

        /// <summary>
        /// Integrates a free flight until the ball is descending through height zTarget (or lands). Returns false when the ball
        /// lands before ever reaching zTarget on the way down.
        /// </summary>
        public static bool PredictAtHeight(Vec3 pos, Vec3 vel, float zTarget, out Vec3 at, out float time)
        {
            time = 0f;
            at = pos;
            for (int i = 0; i < 900; i++)
            {
                Vec3 pPrev = pos;
                Advance(ref pos, ref vel, Dt);
                time += Dt;
                if (vel.Z < 0f && pos.Z <= zTarget && pPrev.Z >= zTarget - 1e-4f)
                {
                    float f = (pPrev.Z - zTarget) / Math.Max(1e-5f, pPrev.Z - pos.Z);
                    at = pPrev + (pos - pPrev) * f;
                    time -= Dt * (1f - f);
                    return true;
                }
                if (pos.Z <= BallRadius && vel.Z < 0f) { at = pos; return false; }
            }
            return false;
        }

        /// <summary>Position of the ball after t seconds of free flight (no ground).</summary>
        public static Vec3 PositionAfter(Vec3 pos, Vec3 vel, float t)
        {
            int n = (int)(t / Dt);
            for (int i = 0; i < n; i++) Advance(ref pos, ref vel, Dt);
            float rem = t - n * Dt;
            if (rem > 1e-5f) Advance(ref pos, ref vel, rem);
            return pos;
        }

        /// <summary>
        /// Solves a launch velocity so a ball released at <paramref name="from"/> with the given speed passes through <paramref name="to"/>.
        /// highArc picks the lob solution. Returns false if the speed cannot reach (velocity then points at the best-effort max-range angle).
        /// </summary>
        public static bool SolveLaunch(Vec3 from, Vec3 to, float speed, bool highArc, out Vec3 vel, out float flightTime)
        {
            Vec3 d = to - from;
            float dh = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y);
            float hx = dh > 1e-4f ? d.X / dh : 0f, hy = dh > 1e-4f ? d.Y / dh : 1f;
            flightTime = 0f;
            if (dh < 0.05f)
            {
                float th0 = highArc ? 1.5f : 0.3f;
                vel = new Vec3(0f, 0f, speed * (float)Math.Sin(th0));
                flightTime = 2f * vel.Z / SimMath.Gravity;
                return true;
            }
            // the launch angle is found by bisection on the simulated height at the target distance: the height rises with the angle
            // on the flat branch (up to about 45 degrees) and falls with it on the lob branch, drag included
            const float split = 0.80f;
            float lo = highArc ? split : 0.02f, hi = highArc ? 1.55f : split;
            float zLo, zHi, tt;
            SimulateToDistance(from, Launch(hx, hy, speed, lo), dh, hx, hy, out zLo, out tt);
            SimulateToDistance(from, Launch(hx, hy, speed, hi), dh, hx, hy, out zHi, out tt);
            bool reachable = highArc ? (zLo >= to.Z) : (zHi >= to.Z);
            if (!reachable)
            {
                // out of range for this speed: best effort at maximum-range elevation
                float thBest = 0.62f;
                vel = Launch(hx, hy, speed, thBest);
                float zb;
                SimulateToDistance(from, vel, dh, hx, hy, out zb, out flightTime);
                return false;
            }
            if (!highArc && zLo > to.Z) { hi = lo; }
            if (highArc && zHi > to.Z) { lo = hi; }
            for (int it = 0; it < 16 && hi - lo > 0.0004f; it++)
            {
                float mid = 0.5f * (lo + hi);
                float z;
                SimulateToDistance(from, Launch(hx, hy, speed, mid), dh, hx, hy, out z, out tt);
                if (highArc) { if (z > to.Z) lo = mid; else hi = mid; }
                else { if (z < to.Z) lo = mid; else hi = mid; }
            }
            float theta = highArc ? hi : lo;
            vel = Launch(hx, hy, speed, theta);
            float zEnd;
            SimulateToDistance(from, vel, dh, hx, hy, out zEnd, out flightTime);
            return true;
        }

        private static Vec3 Launch(float hx, float hy, float speed, float theta)
        {
            float c = (float)Math.Cos(theta), s = (float)Math.Sin(theta);
            return new Vec3(hx * speed * c, hy * speed * c, speed * s);
        }

        private static void SimulateToDistance(Vec3 from, Vec3 vel, float dh, float hx, float hy, out float zAt, out float t)
        {
            Vec3 p = from;
            t = 0f;
            for (int i = 0; i < 1200; i++)
            {
                Vec3 pp = p;
                Advance(ref p, ref vel, Dt);
                t += Dt;
                float prog = (p.X - from.X) * hx + (p.Y - from.Y) * hy;
                if (prog >= dh)
                {
                    float prevProg = (pp.X - from.X) * hx + (pp.Y - from.Y) * hy;
                    float f = (dh - prevProg) / Math.Max(1e-5f, prog - prevProg);
                    zAt = pp.Z + (p.Z - pp.Z) * f;
                    t -= Dt * (1f - f);
                    return;
                }
                if (p.Z < -6f) break;
            }
            zAt = p.Z;
        }

        /// <summary>Max throwing speed (yd/s) for a ThrowPower rating.</summary>
        public static float MaxThrowSpeed(int throwPower) { return 22f + 12f * SimMath.Rating01(throwPower); }
    }

    /// <summary>
    /// The football: held, snapped, thrown, pitched, kicked, or loose. Handles bounce physics with irregular football bounces.
    /// </summary>
    public sealed class BallSim
    {
        public Vec3 Pos;
        public Vec3 Vel;
        public Vec3 PrevPos;
        public BallState State = BallState.Held;
        /// <summary>Agent id currently holding the ball (-1 when in flight / loose).</summary>
        public int Holder = -1;
        public int LastHolder = -1;
        public int Thrower = -1;
        public int Intended = -1;
        public bool GroundContactThisStep;
        public int Bounces;
        public float FlightTime;
        /// <summary>Set once a loose ball has stopped moving.</summary>
        public bool AtRest;
        /// <summary>True for kicked balls (punt/kickoff/FG).</summary>
        public bool IsKick;

        private readonly Rng _rng;

        public BallSim(Rng rng) { _rng = rng; }

        public bool InFlight { get { return State == BallState.Pass || State == BallState.Kick || State == BallState.Pitch || State == BallState.Snap; } }
        public bool IsLoose { get { return State == BallState.Loose; } }

        public void Hold(int agentId, Vec3 pos)
        {
            State = BallState.Held;
            Holder = agentId;
            LastHolder = agentId;
            Pos = pos;
            PrevPos = pos;
            Vel = Vec3.Zero;
            AtRest = false;
            Bounces = 0;
        }

        public void Launch(BallState state, Vec3 pos, Vec3 vel, int thrower)
        {
            State = state;
            Holder = -1;
            Thrower = thrower;
            Pos = pos;
            PrevPos = pos;
            Vel = vel;
            FlightTime = 0f;
            Bounces = 0;
            AtRest = false;
            GroundContactThisStep = false;
        }

        public void MakeLoose(Vec3 pos, Vec3 vel)
        {
            State = BallState.Loose;
            Holder = -1;
            Pos = pos;
            PrevPos = pos;
            Vel = vel;
            AtRest = false;
            Bounces = 0;
        }

        /// <summary>Deflection: perturbs the velocity (tipped pass, blocked kick) and leaves the ball loose in the air.</summary>
        public void Deflect(Vec3 newVel)
        {
            Vel = newVel;
            if (State == BallState.Pass || State == BallState.Kick || State == BallState.Pitch || State == BallState.Snap) State = BallState.Loose;
            Holder = -1;
        }

        public void Step(float dt)
        {
            GroundContactThisStep = false;
            PrevPos = Pos;
            if (State == BallState.Held || State == BallState.Dead) return;
            FlightTime += dt;
            if (AtRest) return;
            // two sub-steps keep fast balls accurate
            float h = dt * 0.5f;
            for (int i = 0; i < 2; i++)
            {
                bool onGround = Pos.Z <= BallPhysics.BallRadius + 0.005f && Math.Abs(Vel.Z) < 0.6f;
                if (onGround)
                {
                    // rolling
                    Vec2 hv = Vel.XY;
                    float sp = hv.Length;
                    if (sp < 0.35f) { Vel = Vec3.Zero; Pos = new Vec3(Pos.X, Pos.Y, BallPhysics.BallRadius); AtRest = true; GroundContactThisStep = true; return; }
                    float ns = Math.Max(0f, sp - 5.0f * h);
                    hv = hv * (ns / sp);
                    Vel = new Vec3(hv, 0f);
                    Pos = new Vec3(Pos.X + Vel.X * h, Pos.Y + Vel.Y * h, BallPhysics.BallRadius);
                    continue;
                }
                BallPhysics.Advance(ref Pos, ref Vel, h);
                if (Pos.Z <= BallPhysics.BallRadius && Vel.Z < 0f) Bounce();
            }
        }

        private void Bounce()
        {
            GroundContactThisStep = true;
            Bounces++;
            float vz = -Vel.Z;
            Vec2 hv = Vel.XY;
            float rest = SimMath.Clamp(0.46f + _rng.Gaussian(0f, 0.09f), 0.2f, 0.7f);
            float newVz = vz * rest;
            // irregular football bounce: yaw the horizontal travel and scale it
            float yaw = _rng.Gaussian(0f, 0.35f) * SimMath.Clamp01(vz / 8f + 0.25f);
            float scale = SimMath.Clamp(0.74f + _rng.Gaussian(0f, 0.12f), 0.35f, 1.1f);
            hv = hv.Rotate(yaw) * scale;
            Pos = new Vec3(Pos.X, Pos.Y, BallPhysics.BallRadius);
            Vel = new Vec3(hv, newVz < 0.9f ? 0f : newVz);
        }

        public void Kill()
        {
            State = BallState.Dead;
            Vel = Vec3.Zero;
            Holder = -1;
        }
    }
}
