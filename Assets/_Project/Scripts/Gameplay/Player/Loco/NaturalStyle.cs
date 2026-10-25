using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.12.0 NATURAL (owner note 1: nobody moves like a robot): the personal movement style of one player. Seeded ONCE from the player's identity, his body size and his role (never per frame, never random at run time),
    /// so the same man runs, stands and looks the same way every play, and two neighbours of the same position never share a tempo, an arm swing or a breathing rhythm. Pure data + math, unit tested (<c>NaturalTests</c>).
    /// </summary>
    public struct NaturalStyle
    {
        /// <summary>Playback-rate factor of his running cycles (his own step rate; the stride warp takes the difference back so his speed is the simulation's).</summary>
        public float Tempo;
        /// <summary>Slow wander of the tempo (amplitude as a factor, frequency in Hz, phase in cycles): a runner is never at exactly one cadence.</summary>
        public float WanderAmp, WanderHz, WanderPhase;
        /// <summary>Arm-swing amplitude factor (1 = what the take brings), shoulder counter-rotation amplitude factor (>= 1), the share of the trunk's yaw/roll wobble the head cancels (head stabilization).</summary>
        public float ArmSwing, Counter, HeadStab;
        /// <summary>Forward trunk offset in degrees while running (+ = more over his feet).</summary>
        public float Posture;
        /// <summary>Idle: breath rate (Hz) and chest amplitude (deg), weight-shift period (s) / sway amplitude (deg) / phase (cycles), mean gap between glances (s).</summary>
        public float BreathHz, BreathAmp, ShiftPeriod, ShiftAmp, ShiftPhase, GlanceGap;
        /// <summary>0..1 personal offset (used to start his own breath and glance clocks apart).</summary>
        public float Phase0;

        public static NaturalStyle Neutral { get { return new NaturalStyle { Tempo = 1f, ArmSwing = 1f, Counter = 1f, HeadStab = 0f, BreathHz = 0.25f, ShiftPeriod = 8f, GlanceGap = 4f }; } }

        /// <summary>Deterministic 0..1 value from a seed and a salt (integer hash, no state).</summary>
        public static float U(int seed, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u ^ (uint)(salt + 1) * 2246822519u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        /// <summary>The style of player <paramref name="seed"/> (his id) with his body size and role. Bigger bodies run with a slower step rate and less arm; small fast bodies with more.</summary>
        public static NaturalStyle For(int seed, PlayerSize size, PlayerRole role)
        {
            float sz = size == PlayerSize.Large ? -1f : size == PlayerSize.Small ? 1f : 0f;
            var s = new NaturalStyle();
            s.Tempo = 1f + (U(seed, 1) - 0.5f) * 0.14f + sz * 0.015f;                       // 0.93 .. 1.07 (+- 1.5 percent by body)
            s.WanderAmp = 0.012f + U(seed, 2) * 0.018f;
            s.WanderHz = 0.07f + U(seed, 3) * 0.11f;
            s.WanderPhase = U(seed, 4);
            s.ArmSwing = (0.88f + U(seed, 5) * 0.34f) * (1f + sz * 0.05f);                  // 0.84 .. 1.28
            s.Counter = 1f + U(seed, 6) * 0.55f * (size == PlayerSize.Large ? 0.7f : 1f);      // never less than the take brings (the lineman's trunk twists less)
            s.HeadStab = 0.50f + U(seed, 7) * 0.35f;
            s.Posture = (U(seed, 8) - 0.45f) * 6.5f + (role == PlayerRole.DB || role == PlayerRole.WR ? -0.6f : 0f) + (role == PlayerRole.OL || role == PlayerRole.DL ? 1.2f : 0f);
            s.BreathHz = 0.20f + U(seed, 9) * 0.14f;
            s.BreathAmp = 0.5f + U(seed, 10) * 0.6f + (size == PlayerSize.Large ? 0.25f : 0f);
            s.ShiftPeriod = 5.5f + U(seed, 11) * 5.5f;
            s.ShiftAmp = 1.3f + U(seed, 12) * 1.7f;
            s.ShiftPhase = U(seed, 13);
            s.GlanceGap = 2.5f + U(seed, 14) * 3.5f;
            s.Phase0 = U(seed, 15);
            return s;
        }

        /// <summary>Tempo factor at time <paramref name="t"/>: his own cadence with a slow wander.</summary>
        public float TempoAt(float t) { return Tempo * (1f + WanderAmp * Mathf.Sin(2f * Mathf.PI * (WanderHz * t + WanderPhase))); }
    }

    /// <summary>
    /// v0.12.0 NATURAL: how a look is shared between the neck, the chest and the pelvis (owner note 3: heads and hips turned too far). Pure math, unit tested.
    /// A gaze direction (degrees from the way the body faces) is taken first by the neck and head (up to <see cref="NeckMax"/>), then by the chest (<see cref="ChestMax"/>), then by a little pelvis (<see cref="PelvisMax"/>);
    /// the rest of the gaze is not reached (a man whose head, chest and pelvis are at their limits turns his feet instead, which the simulation's facing does).
    /// </summary>
    public static class NaturalLook
    {
        /// <summary>Limits in degrees: neck and head together, chest over the pelvis, pelvis over the way the body faces / travels.</summary>
        public const float NeckMax = 70f, ChestMax = 35f, PelvisMax = 25f;
        /// <summary>Rate limits in degrees per second of the relative twist: head over chest, chest over pelvis, pelvis over the facing.</summary>
        public const float NeckRate = 650f, ChestRate = 540f, PelvisRate = 380f;

        /// <summary>Soft limit: linear up to <paramref name="knee"/> x the limit, then compressed asymptotically to the limit (no hard stop, no kink in the motion). Sign kept.</summary>
        public static float SoftLimit(float angle, float limit, float knee = 0.8f)
        {
            float a = Mathf.Abs(angle), k = limit * knee;
            if (a <= k) return angle;
            float room = limit - k;
            float r = k + room * (1f - Mathf.Exp(-(a - k) / Mathf.Max(1e-3f, room)));
            return Mathf.Sign(angle) * r;
        }

        /// <summary>Which share of a gaze angle each segment takes: x = pelvis, y = chest, z = neck + head (sums to the reachable angle, sign of the gaze). A look of 30 degrees is mostly neck; 90 degrees takes the neck's 70 and the rest from the chest.</summary>
        public static Vector3 Share(float gaze)
        {
            float a = Mathf.Abs(gaze), s = Mathf.Sign(gaze);
            // the neck leads (60 percent of the look up to its limit), the chest follows (30 percent, up to 35), the pelvis a little (10 percent, up to 25)
            float neck = Mathf.Min(NeckMax, a * 0.60f), chest = Mathf.Min(ChestMax, a * 0.30f), pelvis = Mathf.Min(PelvisMax, a * 0.10f);
            float left = a - neck - chest - pelvis;
            if (left > 0f)
            {
                float t = Mathf.Min(left, NeckMax - neck); neck += t; left -= t;
                t = Mathf.Min(left, ChestMax - chest); chest += t; left -= t;
                t = Mathf.Min(left, PelvisMax - pelvis); pelvis += t;
            }
            return new Vector3(pelvis * s, chest * s, neck * s);
        }

        /// <summary>One step of a rate-limited follower: <paramref name="cur"/> moves toward <paramref name="target"/> by at most <paramref name="rate"/> degrees per second.</summary>
        public static float Follow(float cur, float target, float rate, float dt) { return Mathf.MoveTowards(cur, target, rate * dt); }

        /// <summary>One step of an eased turn (pure): the heading moves toward <paramref name="target"/> with a rate that follows the error (time constant 0.09 s) up to <paramref name="maxRate"/> deg/s, and the rate itself changes by at most <paramref name="accel"/> deg/s per second, so a turn builds up and settles. Returns the new heading, updates <paramref name="vel"/>.</summary>
        public static float TurnStep(float cur, float target, ref float vel, float maxRate, float accel, float dt)
        {
            float err = Mathf.DeltaAngle(cur, target);
            float want = Mathf.Clamp(err / 0.09f, -maxRate, maxRate);
            vel = Mathf.MoveTowards(vel, want, accel * dt);
            float step = vel * dt;
            if (Mathf.Abs(step) >= Mathf.Abs(err) && (step >= 0f) == (err >= 0f)) { vel = 0f; return target; }
            return cur + step;
        }
    }
}
