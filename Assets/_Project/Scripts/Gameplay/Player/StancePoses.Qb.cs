using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.9.0 QBSTANCE: pose blending and the quarterback's throwing poses (closed front shoulder, hips before shoulders, a long stable stride, follow-through).</summary>
    public static partial class StancePoses
    {
        static float L(float a, float b, float t) { return a + (b - a) * t; }

        /// <summary>Blend of two poses: numbers are interpolated, hand modes switch at the half way point.</summary>
        public static StancePose Lerp(in StancePose a, in StancePose b, float t)
        {
            if (!a.Valid) return b;
            if (!b.Valid) return a;
            t = Mathf.Clamp01(t);
            var r = t < 0.5f ? a : b;
            r.Valid = true;
            r.Hips = L(a.Hips, b.Hips, t); r.HipsZ = L(a.HipsZ, b.HipsZ, t); r.HipsX = L(a.HipsX, b.HipsX, t);
            r.Lean = L(a.Lean, b.Lean, t); r.Bend = L(a.Bend, b.Bend, t); r.Neck = L(a.Neck, b.Neck, t); r.HeadPitch = L(a.HeadPitch, b.HeadPitch, t);
            r.RX = L(a.RX, b.RX, t); r.RZ = L(a.RZ, b.RZ, t); r.LX = L(a.LX, b.LX, t); r.LZ = L(a.LZ, b.LZ, t); r.RHeel = L(a.RHeel, b.RHeel, t); r.LHeel = L(a.LHeel, b.LHeel, t);
            r.ToeOut = L(a.ToeOut, b.ToeOut, t);
            r.RPos = Vector3.Lerp(a.RPos, b.RPos, t); r.LPos = Vector3.Lerp(a.LPos, b.LPos, t); r.RY = L(a.RY, b.RY, t); r.LY = L(a.LY, b.LY, t);
            r.Tilt = L(a.Tilt, b.Tilt, t); r.HeadYaw = L(a.HeadYaw, b.HeadYaw, t); r.MaxLean = L(a.MaxLean, b.MaxLean, t); r.UpperYaw = L(a.UpperYaw, b.UpperYaw, t);
            return r;
        }

        /// <summary>Keyed throwing pose of a right-handed passer facing his target. <paramref name="u"/>: 0 = the wind-up starts, 1 = the ball leaves the hand, above 1 the follow-through (clamped at 1.5).
        /// The shoulders start CLOSED (the front shoulder points at the target: positive upper yaw), the hips and the stride open first, the shoulders unwind through the release and finish open across the body; the front foot steps toward the target
        /// and the front knee stays over the foot (image 73); at release the trunk is near vertical and the drive leg angled (image 82).</summary>
        public static StancePose QbThrow(float u, bool onTheRun)
        {
            u = Mathf.Clamp(u, 0f, 1.5f);
            var ready = For(new StanceSpec { Kind = StanceKind.QbPocket }, PlayerRole.QB);
            // key poses (hands stay free: the throw take owns the arms)
            StancePose k0 = ready; k0.UpperYaw = 12f; k0.Lean = 12f;
            StancePose k1 = ready; k1.RHand = HandMode.Cock; k1.LHand = HandMode.Chest;
            k1.RPos = new Vector3(0.17f, 0.30f, 0.02f); k1.LPos = new Vector3(-0.22f, 0.10f, 0.52f);      // loaded: the ball by the right ear, the front arm out pointing at the target
            k1.Hips = 0.85f; k1.Lean = 7f; k1.UpperYaw = 40f; k1.HeadPitch = 3f;                  // loaded: weight back, shoulders closed
            k1.LZ = 0.30f; k1.LX = -0.17f; k1.RZ = -0.36f; k1.RX = 0.22f; k1.ToeOut = 0f;          // the stride is planted toward the target
            StancePose k2 = k1;
            k2.Hips = 0.86f; k2.Lean = 17f; k2.UpperYaw = 0f; k2.HipsZ = 0.04f;                    // release: spine near vertical over the front leg, shoulders square
            k2.RHeel = 0.10f;
            k2.RPos = new Vector3(0.10f, 0.40f, 0.50f); k2.LPos = new Vector3(-0.16f, -0.05f, 0.26f);        // the release: the throwing arm high and forward, the front arm pulled in
            StancePose k3 = k2;
            k3.Hips = 0.88f; k3.Lean = 26f; k3.UpperYaw = -24f; k3.HipsZ = 0.10f; k3.RHeel = 0.12f; k3.RZ = -0.20f;
            k3.RPos = new Vector3(-0.12f, -0.14f, 0.40f); k3.LPos = new Vector3(-0.14f, -0.05f, 0.20f);   // follow-through: the hand finishes across the body near the opposite hip // follow-through: the shoulders finish across the body
            StancePose p;
            if (u < 0.55f) p = Lerp(k0, k1, Ease(u / 0.55f));
            else if (u < 1f) p = Lerp(k1, k2, Ease((u - 0.55f) / 0.45f));
            else p = Lerp(k2, k3, Ease((u - 1f) / 0.5f));
            p.Valid = true;
            if (onTheRun) { p.LZ = Mathf.Min(p.LZ, 0.12f); p.RZ = Mathf.Max(p.RZ, -0.20f); p.Hips = Mathf.Max(p.Hips, 0.90f); }
            return p;
        }


        /// <summary>The underhand pitch (E-143): the ball swings back low by the hip and is flipped out to the runner with the throwing hand, the front hand lets go and the shoulders stay square.
        /// <paramref name="u"/>: 1 = the ball leaves the hand.</summary>
        public static StancePose QbToss(float u)
        {
            u = Mathf.Clamp(u, 0f, 1.6f);
            var ready = For(new StanceSpec { Kind = StanceKind.QbPocket }, PlayerRole.QB);
            StancePose k0 = ready;
            StancePose k1 = ready; k1.RPos = new Vector3(0.13f, -0.28f, 0.16f); k1.LPos = new Vector3(-0.10f, -0.22f, 0.18f); k1.Lean = 20f; k1.Hips = 0.86f; k1.LZ = 0.16f;      // the swing back, knees bent
            StancePose k2 = ready; k2.RPos = new Vector3(0.14f, -0.06f, 0.56f); k2.LPos = new Vector3(-0.22f, -0.02f, 0.34f); k2.Lean = 17f; k2.Hips = 0.87f; k2.LZ = 0.22f;      // the release: the hand forward and a little up, the front hand opens away
            StancePose k3 = k2; k3.RPos = new Vector3(0.12f, 0.08f, 0.64f); k3.LPos = new Vector3(-0.18f, -0.08f, 0.22f); k3.Lean = 14f;
            StancePose p;
            if (u < 0.5f) p = Lerp(k0, k1, Ease(u / 0.5f));
            else if (u < 1f) p = Lerp(k1, k2, Ease((u - 0.5f) / 0.5f));
            else p = Lerp(k2, k3, Ease((u - 1f) / 0.6f));
            p.Valid = true;
            return p;
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
    }
}

namespace PixFootball.Gameplay
{
    public static partial class StancePoses
    {
        /// <summary>The kneel-down (E-084): the quarterback drops onto his right knee with the ball tucked at the belly (u 0..1 over about half a second). The mocap kneel takes never leave the standing position.</summary>
        public static StancePose QbKneel(float u)
        {
            var a = For(new StanceSpec { Kind = StanceKind.QbPocket }, PlayerRole.QB);
            var b = a; b.Hips = 0.52f; b.HipsZ = -0.10f; b.Lean = 30f; b.Neck = 14f; b.HeadPitch = 0f;
            b.RX = 0.14f; b.RZ = -0.44f; b.RHeel = 0.20f; b.LX = -0.20f; b.LZ = 0.30f; b.LHeel = 0f; b.ToeOut = 2f;      // right knee on the turf, the right foot behind it, the left foot flat out front
            b.RPos = new Vector3(0.10f, -0.06f, 0.20f); b.LPos = new Vector3(-0.10f, -0.06f, 0.20f);
            var p = Lerp(a, b, u * u * (3f - 2f * u)); p.Valid = true; return p;
        }

        /// <summary>The spike (E-084): both hands up with the ball, then the right arm slams it down toward the turf at u = 1 (u 0..1.4).</summary>
        public static StancePose QbSpike(float u)
        {
            u = Mathf.Clamp(u, 0f, 1.5f);
            var a = For(new StanceSpec { Kind = StanceKind.QbPocket }, PlayerRole.QB);
            var up = a; up.RHand = HandMode.Cock; up.RPos = new Vector3(0.12f, 0.42f, 0.14f); up.LPos = new Vector3(-0.08f, 0.30f, 0.14f); up.Lean = 8f;
            var down = a; down.RHand = HandMode.Cock; down.RPos = new Vector3(0.14f, -0.46f, 0.38f); down.LPos = new Vector3(-0.12f, -0.05f, 0.20f); down.Lean = 28f; down.Hips = 0.82f;
            StancePose p = u < 0.6f ? Lerp(a, up, Ease(u / 0.6f)) : (u < 1f ? Lerp(up, down, Ease((u - 0.6f) / 0.4f)) : down);
            p.Valid = true; return p;
        }
    }
}
