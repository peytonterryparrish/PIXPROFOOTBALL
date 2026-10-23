using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 CHOREO: the legs of a body that the simulation pushes slowly while its upper body is in a hand fight (pass rush, run block, jam, shed). Below about 1.7 m/s the locomotion has no gait of its own and the
    /// animated feet slid over the turf (0.8 m/s on average, engaged linemen skating). Here every foot is pinned to a world position (a stance) and, when the body has moved the animated foot more than
    /// <see cref="Drift"/> away from it, that foot steps: it swings to the animated foot's position a little ahead along the push over <see cref="Dur"/> s with a small lift, while the other foot stays planted.
    /// The result is short choppy steps driven by the push and feet that do not move while they are on the turf. It runs on the final pose through two-bone IK (ankle only, the foot keeps its world rotation),
    /// weighted in and out over 0.1 / 0.15 s. No random numbers.
    /// </summary>
    public sealed class EngageStepper
    {
        public const float Drift = 0.20f, Dur = 0.17f, Lead = 0.08f, LiftH = 0.06f, ReachFrac = 0.97f;

        sealed class F { public Transform U, L, E, Toe; public bool Have, Swing; public Vector3 Anchor, From; public float T; }
        readonly F _l = new F(), _r = new F();
        readonly Animator _a; readonly bool _ok;
        float _w;
        Vector3 _outL, _outR; bool _haveOut;
        /// <summary>The feet are never held further apart (m, on the turf) than the animation has them plus this slack, nor more than <see cref="MinSpread"/> where the animation is narrower; and a foot never reaches past this share of the leg.</summary>
        public const float AirHeight = 0.22f;
        public const float MinSpread = 0.62f, SpreadSlack = 0.08f, ReachShare = 0.92f, TriggerShare = 0.90f;
        /// <summary>QA: steps taken (swings completed).</summary>
        public int Steps;
        public float Weight { get { return _w; } }

        public EngageStepper(Animator a)
        {
            _a = a;
            if (a == null || !a.isHuman) return;
            _l.U = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg); _l.L = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg); _l.E = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            _r.U = a.GetBoneTransform(HumanBodyBones.RightUpperLeg); _r.L = a.GetBoneTransform(HumanBodyBones.RightLowerLeg); _r.E = a.GetBoneTransform(HumanBodyBones.RightFoot);
            _ok = _l.U != null && _l.L != null && _l.E != null && _r.U != null && _r.L != null && _r.E != null;
        }

        public void Reset() { _haveOut = false; _w = 0f; _l.Have = _r.Have = false; _l.Swing = _r.Swing = false; }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Late, on the final pose. <paramref name="vel"/> = the body's horizontal velocity (m/s), <paramref name="fwd"/> = its facing.</summary>
        public void Late(float dt, bool active, Vector3 vel, Vector3 fwd, float groundY = float.NaN)
        {
            if (!_ok || dt <= 1e-5f) return;
            if (active) _w = Mathf.MoveTowards(_w, 1f, dt / 0.10f); else _w = Mathf.MoveTowards(_w, 0f, dt / 0.15f);
            if (_w <= 0.001f) { _l.Have = _r.Have = false; _l.Swing = _r.Swing = false; return; }
            Vector3 al = _l.E.position, ar = _r.E.position;      // the animated ankles of this frame
            // a foot that is up in the air (a jump, a stumble's kick) is not pinned: it follows the animation, its anchor re-set where it comes down
            if (!float.IsNaN(groundY)) { if (al.y - groundY > AirHeight) { _l.Anchor = al; _l.Swing = false; _l.Have = true; } if (ar.y - groundY > AirHeight) { _r.Anchor = ar; _r.Swing = false; _r.Have = true; } }
            if (!_l.Have) { _l.Anchor = al; _l.Have = true; _l.Swing = false; }
            if (!_r.Have) { _r.Anchor = ar; _r.Have = true; _r.Swing = false; }
            if (active)
            {
                float dl = Flat(al - _l.Anchor).magnitude, dr = Flat(ar - _r.Anchor).magnitude;
                float rl = Vector3.Distance(_l.U.position, new Vector3(_l.Anchor.x, al.y, _l.Anchor.z)), rr = Vector3.Distance(_r.U.position, new Vector3(_r.Anchor.x, ar.y, _r.Anchor.z));
                float reachL = LimbIK.Reach(_l.U.position, _l.L.position, _l.E.position) * ReachFrac, reachR = LimbIK.Reach(_r.U.position, _r.L.position, _r.E.position) * ReachFrac;
                bool needL = !_l.Swing && (dl > Drift || rl > reachL * TriggerShare / ReachFrac), needR = !_r.Swing && (dr > Drift || rr > reachR * TriggerShare / ReachFrac);
                // one foot at a time: the one that is further from where the body wants it
                if (needL && needR) { if (dl >= dr) needR = false; else needL = false; }
                if (needL && !_r.Swing) { _l.Swing = true; _l.From = _l.Anchor; _l.T = 0f; }
                if (needR && !_l.Swing) { _r.Swing = true; _r.From = _r.Anchor; _r.T = 0f; }
            }
            Vector3 dir = Flat(vel).sqrMagnitude > 0.04f ? Flat(vel).normalized : Flat(fwd).normalized;
            Vector3 pole = Flat(fwd).sqrMagnitude > 1e-4f ? Flat(fwd).normalized : Vector3.forward;
            float limit = Mathf.Max(MinSpread, Flat(al - ar).magnitude + SpreadSlack);
            Step(_l, al, dir, pole, dt, _haveOut ? _outR : ar, limit, out _outL); Step(_r, ar, dir, pole, dt, _outL, limit, out _outR); _haveOut = true;
        }

        /// <summary>A landing foot is brought inside the spread of the other foot and inside the leg's reach (a wide lunge read as legs reaching for the next yard). A pinned foot is never moved by this: it would slide.</summary>
        static Vector3 ClampLanding(F f, Vector3 land, Vector3 other, float spread)
        {
            Vector3 sd = Flat(land - other); if (sd.magnitude > spread) { Vector3 c = other + sd.normalized * spread; land = new Vector3(c.x, land.y, c.z); }
            float legReach = LimbIK.Reach(f.U.position, f.L.position, f.E.position) * ReachShare, dy = f.U.position.y - land.y;
            float hmax = Mathf.Sqrt(Mathf.Max(0.01f, legReach * legReach - dy * dy)); Vector3 hd = Flat(land - f.U.position);
            if (hd.magnitude > hmax) { Vector3 c = f.U.position + hd.normalized * hmax; land = new Vector3(c.x, land.y, c.z); }
            return land;
        }

        void Step(F f, Vector3 anim, Vector3 dir, Vector3 pole, float dt, Vector3 other, float spread, out Vector3 placed)
        {
            Vector3 tgt;
            if (f.Swing)
            {
                f.T += dt; float u = Mathf.Clamp01(f.T / Dur);
                Vector3 landing = ClampLanding(f, anim + dir * Lead, other, spread);
                float s = u * u * (3f - 2f * u);
                Vector3 p = Vector3.Lerp(f.From, landing, s);
                tgt = new Vector3(p.x, anim.y + LiftH * Mathf.Sin(Mathf.PI * u), p.z);
                if (u >= 1f) { f.Anchor = new Vector3(landing.x, f.Anchor.y, landing.z); f.Swing = false; Steps++; }
            }
            else tgt = new Vector3(f.Anchor.x, anim.y, f.Anchor.z);
            tgt = Vector3.Lerp(anim, tgt, _w);
            placed = tgt;
            LimbIK.Apply(f.U, f.L, f.E, tgt, pole, 1f);
        }
    }
}
