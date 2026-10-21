using System.Collections.Generic;
using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Rigid transform in the ground plane: world = T + R(Yaw) * authored. Yaw is in degrees and follows the Unity convention (positive turns +Z toward +X), so a view whose
    /// transform is rotated by Yaw plays an authored take (body facing the authored frame) exactly in that world orientation.
    /// </summary>
    public struct Rigid2
    {
        public Vector2 T;
        public float Yaw;

        public static Rigid2 Identity { get { return new Rigid2 { T = Vector2.zero, Yaw = 0f }; } }
        public Vector2 Apply(Vector2 p) { return T + ContactMath.Rotate(p, Yaw); }
        public Vector2 RotateOnly(Vector2 v) { return ContactMath.Rotate(v, Yaw); }
        public Vector2 Inverse(Vector2 world) { return ContactMath.Rotate(world - T, -Yaw); }
    }

    /// <summary>Pure math for aligning authored two-actor clips to the simulation (unit tested).</summary>
    public static class ContactMath
    {
        /// <summary>Rotates a ground vector (x, z) about +Y by yawDegrees (0 leaves it, +90 sends +Z to +X).</summary>
        public static Vector2 Rotate(Vector2 v, float yawDegrees)
        {
            float r = yawDegrees * Mathf.Deg2Rad, s = Mathf.Sin(r), c = Mathf.Cos(r);
            return new Vector2(v.x * c + v.y * s, -v.x * s + v.y * c);
        }

        /// <summary>Heading of a ground vector in degrees (+Z = 0, +X = 90).</summary>
        public static float YawOf(Vector2 v) { return Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg; }

        /// <summary>Transform that puts an authored point (with authored heading) at a world point with a world heading.</summary>
        public static Rigid2 Align(Vector2 authoredPoint, float authoredYaw, Vector2 worldPoint, float worldYaw)
        {
            float yaw = Mathf.DeltaAngle(0f, worldYaw - authoredYaw);
            return new Rigid2 { Yaw = yaw, T = worldPoint - Rotate(authoredPoint, yaw) };
        }

        /// <summary>
        /// Aligns an authored pair to the simulated pair at contact. The carrier B is anchored (position exact, simulation stays authoritative for him); the yaw is a blend of
        /// "carrier heading matches" and "the A to B axis matches" (axisWeight 0..1), so both bodies keep their authored relative layout and the tackler comes from where he really came from.
        /// </summary>
        public static Rigid2 AlignPair(Vector2 authA, Vector2 authB, float authYawB, Vector2 worldA, Vector2 worldB, float worldYawB, float axisWeight)
        {
            float facingYaw = Mathf.DeltaAngle(0f, worldYawB - authYawB);
            Vector2 ab = authA - authB, wb = worldA - worldB;
            float yaw = facingYaw;
            if (ab.sqrMagnitude > 1e-4f && wb.sqrMagnitude > 1e-4f)
            {
                float axisYaw = Mathf.DeltaAngle(0f, YawOf(wb) - YawOf(ab));
                yaw = facingYaw + Mathf.DeltaAngle(facingYaw, axisYaw) * Mathf.Clamp01(axisWeight);
            }
            return new Rigid2 { Yaw = yaw, T = worldB - Rotate(authB, yaw) };
        }

        /// <summary>Where a subject is seen from an observer who faces observerYaw: 0 = straight ahead, +90 = to the observer's right, +/-180 = behind him.</summary>
        public static float BearingDeg(Vector2 subject, Vector2 observer, float observerYaw)
        {
            Vector2 d = subject - observer;
            if (d.sqrMagnitude < 1e-6f) return 0f;
            return Mathf.DeltaAngle(observerYaw, YawOf(d));
        }

        public static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Warp-in weight: 0 at the start of the take, 1 at the contact moment (the view steps from the simulation onto the animation root motion).</summary>
        public static float WarpIn(float t, float tStart, float tContact)
        {
            float span = Mathf.Max(0.04f, tContact - tStart);
            return Smooth01((t - tStart) / span);
        }

        /// <summary>
        /// Weight of the simulation-authority correction: 0 while the animation drives the contact, 1 once the take is over, so the pair ends where the simulation says the
        /// carrier is (forward progress stays authoritative) without sliding the bodies mid-hit.
        /// </summary>
        public static float Authority(float t, float tContact, float length, float hold = 0.3f, float ramp = 0.9f)
        {
            float t0 = tContact + hold;
            float t1 = Mathf.Max(t0 + 0.2f, Mathf.Min(length, t0 + ramp));
            return Smooth01((t - t0) / (t1 - t0));
        }

        /// <summary>Linear sample of an interleaved array at time t (seconds); channel picks the float inside the stride.</summary>
        public static float Sample(float[] data, int stride, int channel, float step, float t)
        {
            int n = data.Length / stride;
            if (n == 0) return 0f;
            float f = Mathf.Max(0f, t / step);
            int i = Mathf.Min((int)f, n - 1), j = Mathf.Min(i + 1, n - 1);
            float u = f - i;
            return Mathf.Lerp(data[i * stride + channel], data[j * stride + channel], u);
        }

        public static Vector2 SampleXZ(float[] data, int stride, int channel, float step, float t)
        {
            return new Vector2(Sample(data, stride, channel, step, t), Sample(data, stride, channel + 1, step, t));
        }

        public static float SampleYaw(float[] data, int stride, int channel, float step, float t)
        {
            int n = data.Length / stride;
            if (n == 0) return 0f;
            float f = Mathf.Max(0f, t / step);
            int i = Mathf.Min((int)f, n - 1), j = Mathf.Min(i + 1, n - 1);
            float u = f - i;
            float a = data[i * stride + channel], b = data[j * stride + channel];
            return a + Mathf.DeltaAngle(a, b) * u;
        }
    }
}
