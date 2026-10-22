using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>Rating to physical-trait mapping shared by every agent.</summary>
    public static class Physique
    {
        public const float RefTau = 0.80f;
        public const float RefAccelCap = 12.5f;
        /// <summary>Game-speed factor on every body's top speed: the 40-yard table is a combine-in-shorts time; in pads on turf the same player is about 4.5 percent slower (elite peak about 22 mph, never 26).</summary>
        public const float PadsFactor = 0.955f;
        /// <summary>Hard ceiling on any body's speed relative to its own top speed (dive lunges and collision knocks can only briefly exceed it).</summary>
        public const float OverspeedCap = 1.05f;
        public const float Dt = 1f / 60f;

        /// <summary>Acceleration-derived time constant (seconds): rating 99 -> 0.80, 40 -> 1.50.</summary>
        public static float TauFor(int accel) { return RefTau + (1f - SimMath.Rating01(Math.Max(20, accel))) * 0.95f; }
        public static float AccelCapFor(int accel) { return 8.5f + 4f * SimMath.Rating01(Math.Max(20, accel)); }

        private static readonly object _vmaxLock = new object();
        private static float[] _vmaxTable;

        /// <summary>Target 40-yard dash time (seconds) for a Speed rating: 99 -> 4.30, 40 -> 5.30 (linear), extended below 40.</summary>
        public static float Target40(int speed)
        {
            speed = SimMath.Clamp(speed, 0, 99);
            return 4.30f + (99 - speed) * (1.0f / 59f);
        }

        /// <summary>Straight-line time from rest to dist yards using the same integration as AgentBody.Step.</summary>
        public static float TimeToDistance(float vmax, float tau, float cap, float dist)
        {
            float v = 0f, x = 0f, t = 0f;
            while (x < dist && t < 30f)
            {
                float a = Math.Min(cap, (vmax - v) / tau);
                if (a < 0f) a = 0f;
                v += a * Dt;
                x += v * Dt;
                t += Dt;
            }
            return t;
        }

        /// <summary>Builds the speed table now (thread-safe, once): call at load so the first snap does not pay for the calibration.</summary>
        public static void Warm() { VMaxForSpeed(50); }

        /// <summary>Top speed (yd/s) such that a player with reference acceleration runs the 40 in Target40(speed).</summary>
        public static float VMaxForSpeed(int speed)
        {
            if (_vmaxTable == null) lock (_vmaxLock) if (_vmaxTable == null)
            {
                var table = new float[100];
                for (int s = 0; s < 100; s++)
                {
                    float target = Target40(s);
                    // calibrate against a typical acceleration for that speed class (halfway between the rating and elite)
                    int accRef = (int)Math.Round(99 - (99 - s) * 0.5f);
                    float tau = TauFor(accRef), cap = AccelCapFor(accRef);
                    float lo = 4f, hi = 16f;
                    for (int i = 0; i < 28; i++)
                    {
                        float mid = 0.5f * (lo + hi);
                        if (TimeToDistance(mid, tau, cap, 40f) > target) lo = mid; else hi = mid;
                    }
                    table[s] = 0.5f * (lo + hi);
                }
                _vmaxTable = table;
            }
            return _vmaxTable[SimMath.Clamp(speed, 0, 99)];
        }
    }

    /// <summary>
    /// Momentum-based locomotion. Desired velocity in, actual velocity out: forward acceleration follows an exponential
    /// approach (strong burst, then taper), braking and lateral acceleration are bounded (friction circle), turning
    /// slows the body, and facing is decoupled from travel so backpedal and shuffle are slower than forward running.
    /// </summary>
    public sealed class AgentBody
    {
        public Vec2 Pos;
        public Vec2 Vel;
        public Vec2 Facing = new Vec2(0f, 1f);
        public float Radius, Mass, HeightYd;
        /// <summary>1 = fresh, 0 = gassed.</summary>
        public float Energy = 1f;
        public float StumbleTime;
        public float FallenTime;
        /// <summary>External speed multiplier (ball security, jog, injuries, etc.).</summary>
        public float SpeedMultiplier = 1f;
        /// <summary>v0.8.0 ability multipliers (1 = no ability): top speed, acceleration time constant (below 1 = quicker), the speed lost in a plant-and-cut and the energy drained while running. Set per tick by the simulation from the player's abilities; a player without any keeps 1.</summary>
        public float AbilityVMax = 1f, AbilityTau = 1f, CutBleed = 1f, DrainScale = 1f;

        // traits
        public float VMax, Tau, AccelCap, LatBase, DecelBase, TurnRate, BackpedalFactor, ShuffleFactor;
        public float Agility01, Stamina01, Strength01;
        /// <summary>Game-pace multiplier this body was built with (see <see cref="GamePace"/>; 1 = the v0.6 engine, used by free roam and the body tests).</summary>
        public readonly float Pace;

        public AgentBody(Ratings r, int weightLb, int heightIn) : this(r, weightLb, heightIn, 1f) { }

        public AgentBody(Ratings r, int weightLb, int heightIn, float pace)
        {
            Pace = pace;
            Mass = Math.Max(60f, weightLb * 0.45359f);
            HeightYd = Math.Max(1.6f, heightIn / 36f);
            Radius = SimMath.Clamp(0.34f + 0.0017f * (Mass - 70f), 0.34f, 0.46f);
            Agility01 = SimMath.Rating01(r.Agility);
            Stamina01 = SimMath.Rating01(r.Stamina);
            Strength01 = SimMath.Rating01(r.Strength);
            float acc01 = SimMath.Rating01(Math.Max(20, r.Acceleration));
            VMax = Physique.VMaxForSpeed(r.Speed) * Physique.PadsFactor;
            Tau = Physique.TauFor(r.Acceleration);
            AccelCap = Physique.AccelCapFor(r.Acceleration);
            float massN = SimMath.Clamp((Mass - 90f) / 50f, -0.5f, 1f);
            LatBase = (8.5f + 5.5f * Agility01) * (1f - 0.16f * massN);
            DecelBase = (9f + 4.5f * Agility01) * (1f - 0.08f * massN);
            TurnRate = (5.0f + 4.5f * Agility01) * (1f - 0.2f * massN);
            BackpedalFactor = 0.46f + 0.10f * Agility01;   // about 5-6 yd/s backward
            ShuffleFactor = 0.60f + 0.11f * Agility01;      // about 6-7 yd/s sideways
            if (pace != 1f)
            {
                // v0.7 game pace: slower top speed, a lower burst, and slower cutting / braking / turning, so the body reads heavier rather than just time-stretched
                VMax *= pace; AccelCap *= pace;
                Tau *= 1f + (GamePace.AccelerationStretch - 1f) * (1f - pace) / (1f - GamePace.Scale);
                LatBase *= pace; DecelBase *= pace; TurnRate *= pace;
            }
        }

        public float Speed { get { return Vel.Length; } }
        public bool IsDown { get { return FallenTime > 0f; } }
        public float Fatigue { get { return 0.88f + 0.12f * Energy; } }

        public float EffectiveVMax
        {
            get
            {
                float v = VMax * Fatigue * SpeedMultiplier * AbilityVMax;
                if (StumbleTime > 0f) v *= 0.55f;
                return v;
            }
        }

        /// <summary>Speed cap multiplier for moving at angle radians off the facing direction.</summary>
        public float DirectionFactor(float angle)
        {
            float a = Math.Abs(angle);
            const float a1 = 0.35f, a2 = 1.5708f, a3 = 3.1416f;
            if (a <= a1) return 1f;
            if (a <= a2) return SimMath.Lerp(1f, ShuffleFactor, (a - a1) / (a2 - a1));
            return SimMath.Lerp(ShuffleFactor, BackpedalFactor, (a - a2) / (a3 - a2));
        }

        /// <summary>Maximum speed this body can hold moving along dir given its current facing.</summary>
        public float SpeedCapAlong(Vec2 dir)
        {
            return EffectiveVMax * DirectionFactor(SimMath.AngleBetween(dir, Facing));
        }

        /// <summary>Time (s) to run from rest to the given distance in a straight line for this body.</summary>
        public float StraightLineTime(float dist)
        {
            return Physique.TimeToDistance(VMax, Tau, AccelCap, dist);
        }

        public void Knock(Vec2 deltaV) { Vel = Vel + deltaV; }

        /// <summary>Instant ceiling applied after every impulse (tackles, dives, collisions): 1.08 x this body's own top speed, so no player is ever clocked above about 24 mph.</summary>
        public void LimitSpeed()
        {
            float cap = VMax * Fatigue * SpeedMultiplier * AbilityVMax * (Mass > 105f ? 1.03f : 1.08f);   // v0.7: big men (linemen) never exceed 1.03 x their top speed, shed bursts and lunges included
            float sp = Vel.Length;
            if (sp > cap) Vel = Vel * (cap / sp);
        }

        public void Stumble(float seconds) { StumbleTime = Math.Max(StumbleTime, seconds); }

        public void Fall(float seconds) { FallenTime = Math.Max(FallenTime, seconds); }

        /// <summary>
        /// Advances one fixed step. wantVel is the desired velocity; faceDir an optional explicit facing target
        /// (otherwise facing follows travel). exertion in 0..1 scales fatigue drain (sprint button).
        /// </summary>
        public void Step(float dt, Vec2 wantVel, bool hasFace, Vec2 faceDir, float exertion)
        {
            if (FallenTime > 0f)
            {
                FallenTime -= dt;
                float sp = Vel.Length;
                if (sp > 0f)
                {
                    float ns = Math.Max(0f, sp - 14f * dt);   // a tackled player slides a yard or two, not three
                    Vel = Vel * (ns / sp);
                }
                Pos = Pos + Vel * dt;
                Energy = Math.Min(1f, Energy + dt / 90f);
                return;
            }
            if (StumbleTime > 0f) StumbleTime = Math.Max(0f, StumbleTime - dt);

            float vEff = EffectiveVMax;
            float speed = Vel.Length;
            float stumbleF = StumbleTime > 0f ? 0.5f : 1f;

            // ---- facing ----
            Vec2 wantDir = wantVel.Norm();
            float wantSpd = wantVel.Length;
            Vec2 faceTarget;
            if (hasFace && faceDir.LenSq() > 0.25f) faceTarget = faceDir.Norm();
            else if (wantSpd > 0.4f) faceTarget = wantDir;
            else if (speed > 1.0f) faceTarget = Vel.Norm();
            else faceTarget = Facing;
            float turn = TurnRate * (1f - 0.45f * SimMath.Clamp01(speed / Math.Max(1f, VMax))) * stumbleF;
            Facing = SimMath.RotateToward(Facing, faceTarget, turn * dt);

            // ---- direction-dependent speed cap ----
            if (wantSpd > 0.001f)
            {
                float cap = vEff * DirectionFactor(SimMath.AngleBetween(wantDir, Facing));
                if (wantSpd > cap) { wantVel = wantDir * cap; wantSpd = cap; }
            }

            // ---- friction-circle acceleration ----
            Vec2 h;
            if (speed > 0.3f) h = Vel * (1f / speed);
            else if (wantSpd > 0.01f) h = wantDir;
            else h = Facing;
            Vec2 hp = h.Perp();
            Vec2 dv = wantVel - Vel;
            float at = dv.Dot(h) / dt;
            float al = dv.Dot(hp) / dt;

            float fwdCap = Math.Min(AccelCap * (StumbleTime > 0f ? 0.6f : 1f), Math.Max(0f, (vEff - speed) / (Tau * AbilityTau)));
            float decelCap = DecelBase * (StumbleTime > 0f ? 0.7f : 1f);
            float latCap = LatBase * (1f - 0.40f * SimMath.Clamp01(speed / Math.Max(1f, VMax))) * stumbleF;
            if (speed < 2.5f) latCap = Math.Max(latCap, AccelCap);

            if (at > fwdCap) at = fwdCap;
            if (at < -decelCap) at = -decelCap;
            if (al > latCap) al = latCap;
            if (al < -latCap) al = -latCap;

            // plant-and-cut cost: sharp direction changes bleed speed (more for low agility, more at speed)
            if (wantSpd > 0.5f && speed > 3.5f)
            {
                float ang = SimMath.AngleBetween(h, wantDir);
                if (ang > 1.0472f)
                {
                    float sev = SimMath.SmoothStep(1.0472f, 2.6f, ang);
                    float bleed = CutBleed * 9.5f * speed * sev * (1f - 0.55f * Agility01) / Math.Max(1f, VMax);
                    at -= bleed;
                }
            }

            // overall traction limit
            float tractionLimit = (AccelCap + 1.5f + 2f * Agility01) * (StumbleTime > 0f ? 0.7f : 1f);
            float mag = (float)Math.Sqrt(at * at + al * al);
            if (mag > tractionLimit)
            {
                // the friction circle holds for braking too: a hard stop plus a hard cut cannot both happen at full strength
                if (at > 0f) { float k = tractionLimit / mag; at *= k; al *= k; }
                else { float alMax = (float)Math.Sqrt(Math.Max(0f, tractionLimit * tractionLimit - at * at)); al = SimMath.Clamp(al, -alMax, alMax); }
            }

            Vel = Vel + (h * at + hp * al) * dt;
            float newSpeed = Vel.Length;
            if (newSpeed > vEff * 1.02f && wantSpd <= vEff)
            {
                // bled by hits or fatigue: relax toward the cap
                float ns = Math.Max(vEff, newSpeed - DecelBase * dt);
                Vel = Vel * (ns / newSpeed);
                newSpeed = ns;
            }
            // nobody exceeds his own top speed by more than a few percent (dive lunges and knocks used to reach 28 mph)
            float hardCap = vEff * (Mass > 105f ? 1.03f : Physique.OverspeedCap);
            if (newSpeed > hardCap)
            {
                float ns2 = Math.Max(hardCap, newSpeed - 30f * dt);
                Vel = Vel * (ns2 / newSpeed);
            }
            Pos = Pos + Vel * dt;

            // ---- fatigue ----
            float frac = Vel.Length / Math.Max(1f, VMax);
            if (frac > 0.78f || exertion > 0.01f)
            {
                float drain = (frac > 0.78f ? 1f : 0.25f) * dt / (45f + 0.6f * 99f * Stamina01);
                drain += exertion * dt / (10f + 0.18f * 99f * Stamina01);
                drain *= DrainScale;
                Energy = Math.Max(0f, Energy - drain);
            }
            else if (frac < 0.5f) Energy = Math.Min(1f, Energy + dt / 60f);
        }
    }

    /// <summary>Circle-circle body separation with mass-weighted push.</summary>
    public static class BodyCollision
    {
        /// <summary>A body lying on the grass is an obstacle: the standing body is pushed around it (the fallen one never moves, nobody walks through a pile).</summary>
        public static bool ResolveAroundFallen(AgentBody standing, AgentBody fallen, float fallenRadiusScale)
        {
            Vec2 d = standing.Pos - fallen.Pos;
            float minD = standing.Radius * 0.9f + fallen.Radius * fallenRadiusScale;
            float dsq = d.LenSq();
            if (dsq >= minD * minD) return false;
            float dist = (float)Math.Sqrt(dsq);
            Vec2 n = dist > 1e-4f ? d * (1f / dist) : new Vec2(0f, 1f);
            standing.Pos = standing.Pos + n * (minD - dist);
            float into = standing.Vel.Dot(n);
            if (into < 0f) standing.Vel = standing.Vel - n * into;
            return true;
        }

        public static bool Resolve(AgentBody a, AgentBody b, float restitution)
        {
            Vec2 d = b.Pos - a.Pos;
            float minD = a.Radius + b.Radius;
            float dsq = d.LenSq();
            if (dsq >= minD * minD) return false;
            float dist = (float)Math.Sqrt(dsq);
            Vec2 n = dist > 1e-4f ? d * (1f / dist) : new Vec2(1f, 0f);
            float pen = minD - dist;
            float ma = a.Mass, mb = b.Mass, tot = ma + mb;
            a.Pos = a.Pos - n * (pen * mb / tot);
            b.Pos = b.Pos + n * (pen * ma / tot);
            float vrel = (a.Vel - b.Vel).Dot(n);
            if (vrel > 0f)
            {
                float j = (1f + restitution) * vrel / (1f / ma + 1f / mb);
                a.Vel = a.Vel - n * (j / ma);
                b.Vel = b.Vel + n * (j / mb);
            }
            return true;
        }
    }
}
