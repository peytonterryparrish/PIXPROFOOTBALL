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
        public struct FootState { public Vector3 Pos; public Vector3 From, To; public bool Swing; public float T, Dur, Yaw, FromYaw, ToYaw, Arc; public Vector3 Off; }
        public FootState R, L;
        /// <summary>Height of the ankle above the turf when the foot is flat (m), and the arc height of a swing (m).</summary>
        public float Ankle = 0.09f, Lift = 0.10f;
        /// <summary>Largest step length (m) and the share of the rest width kept when moving sideways.</summary>
        public float MaxStep = 0.95f;
        /// <summary>v0.10.0 QB: stride multiplier (1 = the speed-based stride). An under-centre drop of N steps takes about N long strides, not the dozen short ones of a plain walk.</summary>
        public float StepScale = 1f;
        /// <summary>v0.10.0 QB: how far (m) a planted foot may trail the body along the motion before it must step (the leg cannot reach further; a clamped ankle would drag across the turf). 0 = off.</summary>
        public float ReachH = 0f;
        /// <summary>v0.10.0 QB: the stride count follows the reach (a step only when the planted foot trails by <see cref="ReachH"/>), with a long unhurried swing of <see cref="SwingOverride"/> seconds: an N-step drop is N landings.</summary>
        public bool ReachOnly; public float SwingOverride; public static bool RetargetOn = true;
        int _moveSteps;
        public bool Valid { get; private set; }
        /// <summary>Number of landings so far (QA).</summary>
        public int Steps;
        Vector3 _prevRoot; bool _havePrev;
        float _swingBlock;

        public void Reset(Vector3 root, float yawDeg, Vector3 restR, Vector3 restL, float ground)
        {
            Vector3 fwd = Fwd(yawDeg), right = Vector3.Cross(Vector3.up, fwd);
            R = Plant(_hasSeed ? _seedR : root + right * restR.x + fwd * restR.z, yawDeg, ground);
            L = Plant(_hasSeed ? _seedL : root + right * restL.x + fwd * restL.z, yawDeg, ground);
            _hasSeed = false;
            Valid = true; Steps = 0; _havePrev = false; _swingBlock = 0f;
        }

        bool _hasSeed; Vector3 _seedR, _seedL;
        /// <summary>v0.10.0 QB: the next <see cref="Reset"/> plants the feet where the animation has them (the walk into the survey), not at the stance rest spots.</summary>
        public void Seed(Vector3 rightAnkle, Vector3 leftAnkle) { _seedR = rightAnkle; _seedL = leftAnkle; _hasSeed = true; }

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
            float stepLen = Mathf.Clamp((0.30f + 0.15f * speed) * StepScale, 0.30f, MaxStep * Mathf.Max(1f, StepScale));
            float swingDur = speed > 0.3f ? Mathf.Clamp(0.46f * stepLen / speed, 0.13f, 0.30f) : 0.20f;
            if (ReachOnly && ReachH > 0f && speed > 0.25f) { stepLen = 2f * ReachH * 0.92f; if (SwingOverride > 0f) swingDur = SwingOverride; }
            if (speed < 0.2f) _moveSteps = 0;
            Vector3 restRW = root + right * restR.x + fwd * restR.z, restLW = root + right * restL.x + fwd * restL.z;
            // v0.10.0 QB: a landing spot rides with the body (offset from where the body will be when the foot lands): a body that speeds up mid-stride no longer outruns the spot, which used to drag the descending foot along the turf
            if (ReachH > 0f && RetargetOn) { RetargetSwing(ref R, root, vel, ground); RetargetSwing(ref L, root, vel, ground); }
            // a swing in progress
            AdvanceSwing(ref R, dt, ground);
            AdvanceSwing(ref L, dt, ground);
            // v0.10.0 QB: a swinging foot never goes where the leg cannot follow (a stretched leg cannot lift the ankle: the foot then drags and the toes dig): pulled in to the reach
            if (ReachH > 0f) { ClampReach(ref R, root); ClampReach(ref L, root); }
            // decide a new swing: only one foot is ever in the air, and only when the other one is planted
            if (!R.Swing && !L.Swing)
            {
                // how far each planted foot is from where it ought to be: its rest spot pushed out along the motion (the foot that is farther behind steps first)
                float lead = Mathf.Min(stepLen * 0.5f, speed * swingDur * 0.5f + stepLen * 0.25f);
                Vector3 idealR = restRW + (speed > 0.25f ? vdir * lead : Vector3.zero), idealL = restLW + (speed > 0.25f ? vdir * lead : Vector3.zero);
                float yawErrR = Mathf.Abs(Mathf.DeltaAngle(R.Yaw, yawDeg)), yawErrL = Mathf.Abs(Mathf.DeltaAngle(L.Yaw, yawDeg));
                // 'stale' is the distance behind the ideal spot along the motion, or the plain distance when he stands (settling), or the turn of the foot against the body
                float staleR = Stale(R.Pos, idealR, vdir, speed, yawErrR), staleL = Stale(L.Pos, idealL, vdir, speed, yawErrL);
                if (ReachH > 0f && speed > 0.25f)
                {
                    float trR = -Vector3.Dot(R.Pos - root, vdir) - ReachH, trL = -Vector3.Dot(L.Pos - root, vdir) - ReachH;      // the foot that trails farther steps first (a tie went to the right foot every time: it re-stepped while the left stayed metres behind)
                    if (trR > 0f) staleR = Mathf.Max(staleR, 9f + trR);
                    if (trL > 0f) staleL = Mathf.Max(staleL, 9f + trL);
                }
                float thresh = speed > 0.25f ? (ReachOnly && ReachH > 0f ? 8f : stepLen * 0.5f) : 0.10f;
                if (ReachOnly && ReachH > 0f && speed > 0.4f && _moveSteps == 0) { staleR = Mathf.Max(staleR, 9f); staleL = Mathf.Max(staleL, 9f); }      // the first (drop) step goes at once
                bool wantR = staleR > thresh, wantL = staleL > thresh;
                if (wantR || wantL)
                {
                    bool goR = wantR && (!wantL || staleR >= staleL);
                    // v0.10.0 QB: a sideways slide is a shuffle: the foot on the side he moves toward steps first, the other closes up (the feet never cross)
                    if (wantR && wantL && speed > 0.25f && Mathf.Abs(Vector3.Dot(vdir, right)) > 0.8f) goR = Vector3.Dot(vdir, right) > 0f;
                    if (goR) BeginSwing(ref R, L.Pos, 1f, root, vel, yawDeg, restR, right, fwd, vdir, speed, stepLen, swingDur, ground);
                    else BeginSwing(ref L, R.Pos, -1f, root, vel, yawDeg, restL, right, fwd, vdir, speed, stepLen, swingDur, ground);
                }
            }
            // a planted foot keeps its spot; it only rotates on the ball of the foot toward the body (the pivot of a reverse pivot)
            if (R.Swing) R.ToYaw = yawDeg; if (L.Swing) L.ToYaw = yawDeg;      // a foot in the air turns with the body (it lands square to him: no pivot right after the landing)
            // a planted foot turns on the ball of the foot only once the body has turned past PivotSlack (a foot a few degrees off the body is natural; every small pivot is a visible toe slide)
            if (!R.Swing) PivotOnBall(ref R, TowardWithin(R.Yaw, yawDeg), 540f * dt);
            if (!L.Swing) PivotOnBall(ref L, TowardWithin(L.Yaw, yawDeg), 540f * dt);
            _prevRoot = root; _havePrev = true;
        }

        /// <summary>The planted foot turns on the ball of the foot (v0.10.0 QB): the toes stay where they are, the heel swings round (a reverse pivot no longer sweeps the toes across the turf).</summary>
        public float BallOfFoot = 0.17f;
        public float PivotSlack = 0f;
        float TowardWithin(float cur, float want) { float dlt = Mathf.DeltaAngle(cur, want); return Mathf.Abs(dlt) <= PivotSlack ? cur : want - Mathf.Sign(dlt) * PivotSlack; }

        void PivotOnBall(ref FootState f, float yawDeg, float maxStep)
        {
            float ny = Mathf.MoveTowardsAngle(f.Yaw, yawDeg, maxStep);
            if (Mathf.Abs(Mathf.DeltaAngle(f.Yaw, ny)) < 1e-4f) return;
            Vector3 toe = f.Pos + Fwd(f.Yaw) * BallOfFoot;
            f.Pos = toe - Fwd(ny) * BallOfFoot; f.Pos.y = f.To.y;
            f.Yaw = ny; f.To = f.Pos;
        }

        static float Stale(Vector3 pos, Vector3 ideal, Vector3 vdir, float speed, float yawErr)
        {
            Vector3 d = ideal - pos; d.y = 0f;
            float s = speed > 0.25f ? Vector3.Dot(d, vdir) : d.magnitude;
            if (yawErr > 60f) s = Mathf.Max(s, 0.5f);
            return s;
        }

        void BeginSwing(ref FootState f, Vector3 other, float side, Vector3 root, Vector3 vel, float yawDeg, Vector3 rest, Vector3 right, Vector3 fwd, Vector3 vdir, float speed, float stepLen, float dur, float ground)
        {
            // landing spot: where the body will be when the foot lands, its rest spot there, pushed ahead along the motion by half a step
            Vector3 body = root + vel * dur;
            Vector3 to = body + right * rest.x + fwd * rest.z;
            if (speed > 0.25f) to += vdir * (stepLen * 0.5f);
            // never land across the other foot: keep the lateral order (right foot right of the left foot)
            // v0.10.0 QB: the feet never cross (a scissored drop): the landing keeps at least 14 cm of clear lateral gap from the planted foot (right foot to the right of the left, and the other way round)
            { float gap = Vector3.Dot(to - other, right) * side; if (gap < 0.14f) to += right * ((0.14f - gap) * side); }
            if (ReachH > 0f) { Vector3 hh = to - body; hh.y = 0f; if (hh.magnitude > ReachH) { Vector3 c = body + hh.normalized * ReachH; to = new Vector3(c.x, to.y, c.z); } }      // the landing spot is within the leg's reach of where the body will be
            f.Off = to - body; f.Off.y = 0f;
            to.y = ground + Ankle;
            f.From = f.Pos; f.To = to; f.FromYaw = f.Yaw; f.ToYaw = yawDeg; f.T = 0f; f.Dur = dur; f.Swing = true;
            f.Arc = Mathf.Clamp(0.05f + 0.13f * Mathf.Min(stepLen, 1.8f), Lift, 0.27f);      // v0.10.0 QB: a long stride lifts the foot well clear of the turf (the old 10 cm arc skated the toes along it)
        }

        void RetargetSwing(ref FootState f, Vector3 root, Vector3 vel, float ground)
        {
            if (!f.Swing || f.T > 0.35f * f.Dur) return;      // the spot is fixed for the last 65 percent of the stride (the foot comes down on a spot that no longer moves)
            Vector3 t = root + vel * Mathf.Max(0f, f.Dur - f.T) + f.Off; t.y = ground + Ankle; f.To = t;
        }

        void ClampReach(ref FootState f, Vector3 root)
        {
            if (!f.Swing) return;
            Vector3 h = f.Pos - root; h.y = 0f; float lim = ReachH * 1.12f;
            if (h.magnitude > lim) { Vector3 p = root + h.normalized * lim; f.Pos = new Vector3(p.x, f.Pos.y, p.z); }
        }

        void AdvanceSwing(ref FootState f, float dt, float ground)
        {
            if (!f.Swing) return;
            f.T += dt;
            float u = Mathf.Clamp01(f.T / Mathf.Max(0.05f, f.Dur));
            // v0.10.0 QB: the foot leaves the turf before it travels (the first 12 percent lifts, the last 10 percent lands): no toe scuffing along the ground at the lift-off
            float uh = Mathf.Clamp01((u - 0.12f) / 0.78f);      // a drop stride is square on the turf for its last fifth (the foot comes down already still)
            float e = uh * uh * (3f - 2f * uh);
            Vector3 p = Vector3.Lerp(f.From, f.To, e);
            float arc = (f.Arc > 0.001f ? f.Arc : Lift) * Mathf.Sin(Mathf.PI * u);
            p.y = ground + Ankle + arc;
            f.Pos = p; f.Yaw = Mathf.LerpAngle(f.FromYaw, f.ToYaw, e);
            if (u >= 1f)
            {
                f.Swing = false; f.Pos = new Vector3(f.To.x, ground + Ankle, f.To.z); f.Yaw = f.ToYaw; Steps++; _moveSteps++;
            }
        }
    }
}
