using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 QBSTANCE (E-069, E-074, E-076, E-086, E-090): a footfall planner for a body that is moved by the simulation while it must keep its feet on the turf: the quarterback's drop, the pocket slide, the step-up, the set feet.
    /// The mocap drop clips (and the back-pedal locomotion) hop: 51 to 63 percent of the frames had both feet in the air. This planner can never do that: a foot only lifts while the other is planted (a landing hands the next step to the other foot in the same frame),
    /// a planted foot never moves, a swing is a smoothstep between its lift-off and its landing spot with a low arc. Steps go where the body goes (forward, backward or sideways), their length grows with the speed, a stopped body settles both feet to the stance in two short steps.
    /// Pure math (EditMode tested); the view feeds it the simulated root, velocity and facing every frame.
    /// </summary>
    public sealed class ProcGait
    {
        public struct FootState { public Vector3 Pos; public Vector3 From, To; public bool Swing; public float T, Dur, Yaw, FromYaw, ToYaw; }
        public FootState R, L;
        /// <summary>Height of the ankle above the turf when the foot is flat (m), and the arc height of a swing (m).</summary>
        public float Ankle = 0.09f, Lift = 0.10f;
        /// <summary>Largest step length (m) and the share of the rest width kept when moving sideways.</summary>
        public float MaxStep = 0.95f;
        public bool Valid { get; private set; }
        /// <summary>Number of landings so far (QA).</summary>
        public int Steps;
        Vector3 _prevRoot; bool _havePrev;
        float _swingBlock;

        public void Reset(Vector3 root, float yawDeg, Vector3 restR, Vector3 restL, float ground)
        {
            Vector3 fwd = Fwd(yawDeg), right = Vector3.Cross(Vector3.up, fwd);
            R = Plant(root + right * restR.x + fwd * restR.z, yawDeg, ground);
            L = Plant(root + right * restL.x + fwd * restL.z, yawDeg, ground);
            Valid = true; Steps = 0; _havePrev = false; _swingBlock = 0f;
        }

        public void Cancel() { Valid = false; }

        static Vector3 Fwd(float yawDeg) { float a = yawDeg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); }

        FootState Plant(Vector3 p, float yaw, float ground) { p.y = ground + Ankle; return new FootState { Pos = p, From = p, To = p, Yaw = yaw, FromYaw = yaw, ToYaw = yaw }; }

        /// <summary>Planted flag: true while the foot is on the turf (not in a swing).</summary>
        public bool RPlanted { get { return !R.Swing; } }
        public bool LPlanted { get { return !L.Swing; } }
        /// <summary>How far above the turf the foot is lifted (0 = planted), metres.</summary>
        public float RLift(float ground) { return Mathf.Max(0f, R.Pos.y - ground - Ankle); }
        public float LLift(float ground) { return Mathf.Max(0f, L.Pos.y - ground - Ankle); }

        /// <summary>Advances the planner. <paramref name="restR"/> / <paramref name="restL"/> are the stance offsets of the right and left ankle in the body frame (x right, z forward).</summary>
        public void Update(float dt, Vector3 root, Vector3 vel, float yawDeg, Vector3 restR, Vector3 restL, float ground)
        {
            if (!Valid) { Reset(root, yawDeg, restR, restL, ground); return; }
            dt = Mathf.Clamp(dt, 0f, 0.1f);
            vel.y = 0f;
            float speed = vel.magnitude;
            Vector3 fwd = Fwd(yawDeg), right = Vector3.Cross(Vector3.up, fwd);
            Vector3 vdir = speed > 0.25f ? vel / speed : Vector3.zero;
            // step length: short steps when slow, long when fast (cadence about 3 to 5 steps per second)
            float stepLen = Mathf.Clamp(0.30f + 0.15f * speed, 0.30f, MaxStep);
            float swingDur = speed > 0.3f ? Mathf.Clamp(0.46f * stepLen / speed, 0.13f, 0.30f) : 0.20f;
            Vector3 restRW = root + right * restR.x + fwd * restR.z, restLW = root + right * restL.x + fwd * restL.z;
            // a swing in progress
            AdvanceSwing(ref R, dt, ground);
            AdvanceSwing(ref L, dt, ground);
            // decide a new swing: only one foot is ever in the air, and only when the other one is planted
            if (!R.Swing && !L.Swing)
            {
                // how far each planted foot is from where it ought to be: its rest spot pushed out along the motion (the foot that is farther behind steps first)
                float lead = Mathf.Min(stepLen * 0.5f, speed * swingDur * 0.5f + stepLen * 0.25f);
                Vector3 idealR = restRW + (speed > 0.25f ? vdir * lead : Vector3.zero), idealL = restLW + (speed > 0.25f ? vdir * lead : Vector3.zero);
                float yawErrR = Mathf.Abs(Mathf.DeltaAngle(R.Yaw, yawDeg)), yawErrL = Mathf.Abs(Mathf.DeltaAngle(L.Yaw, yawDeg));
                // 'stale' is the distance behind the ideal spot along the motion, or the plain distance when he stands (settling), or the turn of the foot against the body
                float staleR = Stale(R.Pos, idealR, vdir, speed, yawErrR), staleL = Stale(L.Pos, idealL, vdir, speed, yawErrL);
                float thresh = speed > 0.25f ? stepLen * 0.5f : 0.10f;
                bool wantR = staleR > thresh, wantL = staleL > thresh;
                if (wantR || wantL)
                {
                    bool goR = wantR && (!wantL || staleR >= staleL);
                    if (goR) BeginSwing(ref R, root, vel, yawDeg, restR, right, fwd, vdir, speed, stepLen, swingDur, ground);
                    else BeginSwing(ref L, root, vel, yawDeg, restL, right, fwd, vdir, speed, stepLen, swingDur, ground);
                }
            }
            // a planted foot keeps its spot; it only rotates on the ball of the foot toward the body (the pivot of a reverse pivot)
            if (!R.Swing) R.Yaw = Mathf.MoveTowardsAngle(R.Yaw, yawDeg, 540f * dt);
            if (!L.Swing) L.Yaw = Mathf.MoveTowardsAngle(L.Yaw, yawDeg, 540f * dt);
            _prevRoot = root; _havePrev = true;
        }

        static float Stale(Vector3 pos, Vector3 ideal, Vector3 vdir, float speed, float yawErr)
        {
            Vector3 d = ideal - pos; d.y = 0f;
            float s = speed > 0.25f ? Vector3.Dot(d, vdir) : d.magnitude;
            if (yawErr > 60f) s = Mathf.Max(s, 0.5f);
            return s;
        }

        void BeginSwing(ref FootState f, Vector3 root, Vector3 vel, float yawDeg, Vector3 rest, Vector3 right, Vector3 fwd, Vector3 vdir, float speed, float stepLen, float dur, float ground)
        {
            // landing spot: where the body will be when the foot lands, its rest spot there, pushed ahead along the motion by half a step
            Vector3 body = root + vel * dur;
            Vector3 to = body + right * rest.x + fwd * rest.z;
            if (speed > 0.25f) to += vdir * (stepLen * 0.5f);
            // never land across the other foot: keep the lateral order (right foot right of the left foot)
            to.y = ground + Ankle;
            f.From = f.Pos; f.To = to; f.FromYaw = f.Yaw; f.ToYaw = yawDeg; f.T = 0f; f.Dur = dur; f.Swing = true;
        }

        void AdvanceSwing(ref FootState f, float dt, float ground)
        {
            if (!f.Swing) return;
            f.T += dt;
            float u = Mathf.Clamp01(f.T / Mathf.Max(0.05f, f.Dur));
            float e = u * u * (3f - 2f * u);
            Vector3 p = Vector3.Lerp(f.From, f.To, e);
            float arc = Lift * Mathf.Sin(Mathf.PI * u);
            p.y = ground + Ankle + arc;
            f.Pos = p; f.Yaw = Mathf.LerpAngle(f.FromYaw, f.ToYaw, e);
            if (u >= 1f)
            {
                f.Swing = false; f.Pos = new Vector3(f.To.x, ground + Ankle, f.To.z); f.Yaw = f.ToYaw; Steps++;
            }
        }
    }
}
