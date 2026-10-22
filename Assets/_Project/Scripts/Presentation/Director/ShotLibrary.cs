using System;
using UnityEngine;

namespace PixFootball.Presentation
{
    public struct ShotPose
    {
        public Vector3 Pos, Look;
        public float Fov;
        /// <summary>Distance to focus the lens on (depth of field); &lt;= 0 means no depth of field.</summary>
        public float Focus;
        public float Aperture;
    }

    /// <summary>Everything a shot needs from the live scene, filled in when the shot starts (the delegates are read every frame so the shot follows the action).</summary>
    public sealed class ShotContext
    {
        public Func<Vector3> Subject = () => Vector3.zero;
        public Func<Vector3> Second = () => Vector3.zero;
        public Func<Vector3> Head = null;
        /// <summary>Direction the subject faces (field frame, unit, flat).</summary>
        public Func<Vector3> Facing = () => Vector3.forward;
        public Vector3 Los;
        /// <summary>+1 when the offense attacks +Z, -1 for -Z.</summary>
        public float Downfield = 1f;
        /// <summary>Sign of the sideline the live broadcast camera is on (+1 = +X).</summary>
        public float LiveSide = 1f;
        public int Variant;
        // ---- v0.9.0 CONNECT E-394: smoothing state of the tight shots (the director sets Dt every frame; 0 = first evaluation, which just seeds the state)
        public float Dt;
        public bool SmoothInit; public Vector3 SmoothMid; public float SmoothYaw;
    }

    /// <summary>The camera moves of the broadcast director: each shot is a function of its progress u (0..1) and the live scene.</summary>
    public static class ShotLibrary
    {
        static float Ease(float u) { return u * u * (3f - 2f * u); }

        /// <summary>
        /// v0.9.0 CONNECT E-394: the tight shots used to be rigidly locked to the moving player. The subject point follows with a 0.25 s time constant and the facing direction turns with the same
        /// lag and at most 90 degrees per second, so a juke or a spin no longer whips the camera. Only advances when <see cref="ShotContext.Dt"/> &gt; 0 (a second evaluation in the same frame is free).
        /// </summary>
        static void SmoothSubject(ShotContext c, ref Vector3 mid, ref Vector3 face)
        {
            float yawT = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;
            if (!c.SmoothInit) { c.SmoothInit = true; c.SmoothMid = mid; c.SmoothYaw = yawT; }
            else if (c.Dt > 0f)
            {
                float a = 1f - Mathf.Exp(-c.Dt / 0.25f);
                c.SmoothMid = Vector3.Lerp(c.SmoothMid, mid, a);
                float want = Mathf.DeltaAngle(c.SmoothYaw, yawT) * a, lim = 90f * c.Dt;
                c.SmoothYaw += Mathf.Clamp(want, -lim, lim);
            }
            else { c.SmoothMid = mid; c.SmoothYaw = yawT; }   // Dt = 0 (tools, tests, the first frame): pass the raw values through
            mid = c.SmoothMid;
            face = Quaternion.Euler(0f, c.SmoothYaw, 0f) * Vector3.forward;
        }

        public static ShotPose Evaluate(ShotKind kind, float u, ShotContext c)
        {
            u = Mathf.Clamp01(u);
            switch (kind)
            {
                case ShotKind.SkySwoop: return Sky(u, c);
                case ShotKind.SidelineQb: return SidelineQb(u, c);
                case ShotKind.EndZone: return EndZone(u, c);
                case ShotKind.StadiumWide: return StadiumWide(u, c);
                case ShotKind.TightCarrier: return TightCarrier(u, c);
                case ShotKind.Celebration: return Celebration(u, c);
                case ShotKind.CoachReaction: return Coach(u, c);
                case ShotKind.CrowdShot: return Crowd(u, c);
            }
            return new ShotPose { Pos = new Vector3(c.LiveSide * 44f, 17f, c.Los.z), Look = c.Los, Fov = 27f, Focus = -1f };
        }

        /// <summary>Sky-cam swoop: starts high above the end of the field behind the offense and dives toward the line of scrimmage.</summary>
        static ShotPose Sky(float u, ShotContext c)
        {
            float e = Ease(u);
            Vector3 target = c.Los;
            Vector3 from = target + new Vector3(-c.LiveSide * 26f, 52f, -c.Downfield * 52f);
            Vector3 to = target + new Vector3(-c.LiveSide * 17f, 9.5f, -c.Downfield * 27f);
            Vector3 pos = Vector3.Lerp(from, to, e);
            Vector3 look = target + Vector3.up * Mathf.Lerp(0f, 1.2f, e) + new Vector3(0f, 0f, c.Downfield * Mathf.Lerp(6f, 3f, e));
            return new ShotPose { Pos = pos, Look = look, Fov = Mathf.Lerp(38f, 30f, e), Focus = -1f };
        }

        /// <summary>Low angle on the quarterback from the sideline, a slow push, the crowd blurred behind him.</summary>
        static ShotPose SidelineQb(float u, ShotContext c)
        {
            Vector3 qb = c.Subject();
            float side = -c.LiveSide;
            Vector3 pos = new Vector3(qb.x + side * Mathf.Lerp(11f, 8.2f, Ease(u)), 1.25f, qb.z - c.Downfield * Mathf.Lerp(1.5f, 3.2f, u));
            Vector3 look = qb + Vector3.up * 1.35f + new Vector3(0f, 0f, c.Downfield * 1.2f);
            return new ShotPose { Pos = pos, Look = look, Fov = 24f, Focus = Vector3.Distance(pos, qb + Vector3.up * 1.4f), Aperture = 1.6f };
        }

        /// <summary>High behind the offense looking down the field at the formation, pushing in.</summary>
        static ShotPose EndZone(float u, ShotContext c)
        {
            Vector3 pos = new Vector3(c.Los.x * 0.4f, Mathf.Lerp(8.5f, 6.2f, Ease(u)), c.Los.z - c.Downfield * Mathf.Lerp(36f, 30f, Ease(u)));
            Vector3 look = c.Los + new Vector3(0f, 1f, c.Downfield * 5f);
            return new ShotPose { Pos = pos, Look = look, Fov = Mathf.Lerp(36f, 31f, u), Focus = -1f };
        }

        /// <summary>Slow orbit over the stadium for the game intro.</summary>
        static ShotPose StadiumWide(float u, ShotContext c)
        {
            float ang = Mathf.Lerp(-35f, 28f, u) * Mathf.Deg2Rad;
            float r = Mathf.Lerp(88f, 74f, u);
            Vector3 pos = new Vector3(Mathf.Sin(ang) * r * 0.55f - 20f * (c.Variant % 3), Mathf.Lerp(46f, 30f, Ease(u)), -Mathf.Cos(ang) * r);
            return new ShotPose { Pos = pos, Look = new Vector3(0f, 1f, 0f), Fov = Mathf.Lerp(40f, 34f, u), Focus = -1f };
        }

        /// <summary>Tight shot on the ball carrier / tackler pile after the whistle, from a three-quarter front angle with a shallow depth of field.</summary>
        static ShotPose TightCarrier(float u, ShotContext c)
        {
            Vector3 s = c.Subject(), s2 = c.Second();
            Vector3 mid = s2 != Vector3.zero ? Vector3.Lerp(s, s2, 0.4f) : s;
            Vector3 face = c.Facing(); face.y = 0f; if (face.sqrMagnitude < 0.01f) face = Vector3.forward * c.Downfield; face.Normalize();
            SmoothSubject(c, ref mid, ref face);   // v0.9.0 CONNECT E-394
            Vector3 right = Vector3.Cross(Vector3.up, face);
            float dist = Mathf.Lerp(7.4f, 5.4f, Ease(u));
            Vector3 pos = mid + face * dist * 0.8f + right * dist * 0.55f * (c.Variant % 2 == 0 ? 1f : -1f);
            pos.y = Mathf.Lerp(1.45f, 1.7f, u);
            Vector3 look = mid + Vector3.up * 1.15f;
            return new ShotPose { Pos = pos, Look = look, Fov = 26f, Focus = Vector3.Distance(pos, look), Aperture = 1.7f };
        }

        /// <summary>Touchdown close-up: in front of the scorer, orbiting a few degrees, teammates piling in behind him.</summary>
        static ShotPose Celebration(float u, ShotContext c)
        {
            Vector3 s = c.Subject();
            Vector3 face = c.Facing(); face.y = 0f; if (face.sqrMagnitude < 0.01f) face = Vector3.forward * c.Downfield; face.Normalize();
            SmoothSubject(c, ref s, ref face);   // v0.9.0 CONNECT E-394
            float ang = Mathf.Lerp(-22f, 26f, Ease(u));
            float dist = Mathf.Lerp(7.6f, 5.6f, Ease(u));
            // in front of the scorer (he celebrates facing the camera side); the end-zone wall is not an option
            Vector3 front = face;
            if (Mathf.Abs((s + front * dist).z) > 54f) front = (Vector3.Cross(Vector3.up, face) * (s.x > 0f ? -1f : 1f) * 0.92f - face * 0.38f).normalized;
            Vector3 dir = Quaternion.Euler(0f, ang, 0f) * front;
            Vector3 pos = s + dir * dist; pos.y = Mathf.Lerp(1.5f, 1.35f, u);
            Vector3 look = s + Vector3.up * Mathf.Lerp(1.55f, 1.35f, u);
            return new ShotPose { Pos = pos, Look = look, Fov = 27f, Focus = Vector3.Distance(pos, look), Aperture = 1.8f };
        }

        /// <summary>Sideline reaction: the head coach from a few metres in front, the bench blurred behind.</summary>
        static ShotPose Coach(float u, ShotContext c)
        {
            Vector3 s = c.Subject();
            float side = Mathf.Sign(s.x == 0f ? 1f : s.x);
            Vector3 pos = new Vector3(s.x - side * Mathf.Lerp(7.2f, 5.6f, Ease(u)), 1.55f, s.z + Mathf.Lerp(-1.5f, 1.2f, u));
            Vector3 look = s + Vector3.up * 1.55f;
            return new ShotPose { Pos = pos, Look = look, Fov = 24f, Focus = Vector3.Distance(pos, look), Aperture = 1.6f };
        }

        /// <summary>The home crowd: from the sideline toward the stands, a slow tilt up.</summary>
        static ShotPose Crowd(float u, ShotContext c)
        {
            Vector3 s = c.Subject();
            float side = c.LiveSide;
            Vector3 pos = new Vector3(side * 20f, 1.7f, Mathf.Clamp(s.z, -34f, 34f));
            Vector3 look = new Vector3(side * 52f, Mathf.Lerp(7f, 12.5f, Ease(u)), pos.z + Mathf.Lerp(-6f, 6f, u));
            return new ShotPose { Pos = pos, Look = look, Fov = 30f, Focus = -1f };
        }
    }
}
