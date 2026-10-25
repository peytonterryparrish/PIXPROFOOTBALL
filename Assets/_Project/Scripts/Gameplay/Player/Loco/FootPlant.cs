using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 LOCOMOTION (LOCO-004): stride warping, foot planting and pelvis compensation, run in LateUpdate after the animation.
    ///  1. Stride warp: the feet's offset from the body along the direction of travel is scaled by the factor the gait player could not absorb with its rate band, so the planted foot moves backwards under the body at exactly the ground speed.
    ///  2. Plant: from the gait phase markers (or, without a gait, from the pose) the foot that is on the ground is held at the world position where it landed (the middle of the foot, so the roll-over of the foot stays the animation's)
    ///     until lift-off; its sole is pressed to the turf (the mocap hovers or sinks by 2 - 10 cm), and the lock is released smoothly.
    ///  3. Pelvis: the hips are lowered by whatever the planted leg is short of reaching (legs never over-extend) plus a short landing compression on every plant that scales with the running speed (weight).
    /// Two-bone IK keeps each knee bending the way the animation bends it. Nothing here knows the game pace: it works on the speed and the contact state it is given.
    /// </summary>
    public sealed class FootPlant
    {
        public static bool Enabled = true;
        /// <summary>QA switches (batch runs): no stride warp, no leg IK (the plant still computes).</summary>
        public static bool DebugNoWarp, DebugNoIK;
        /// <summary>v0.9.0 LOCOMOTION round 3 (LOCO-016): leg polish (knee pole along the line of travel, foot yaw and roll limits). QA switch: -pixLocoNoPolish.</summary>
        public static bool Polish = true;
        /// <summary>How much of the leg polish ran in the last Late (0..1): the old toe-out clamp of BodyMotion stands down while it does.</summary>
        public float PolishWeight;
        public const float FootYawLimit = 10f, FootRollLimit = 8f, FootToeOut = 4f, KneeSlack = 6f, KneeMax = 55f, FootYawRate = 900f, ShinTwistLimit = 10f, FootTwistLimit = 10f;
        /// <summary>Lowest point of a planted sole above the turf (m).</summary>
        public const float SoleGoal = -0.004f;

        sealed class Foot
        {
            public Transform Upper, Lower, End, Toe; public bool Left;
            public bool Planted; public float W, Sole, PrevSole; public bool Have; public Pt H, B;
            public float RestAnkle, RestToe, Reach, Away, Corr, Resid, WHeel, WBall, HoldAbs; public Vector3 ToeHold; public bool YawHold;
            public Vector3 LocalLat = Vector3.right, LocalFwd = Vector3.forward; public bool HaveAxes;
            public Vector3 ShinAxis = Vector3.up, FootAxis = Vector3.up; public Quaternion ShinBind = Quaternion.identity, FootBind = Quaternion.identity; public bool HaveTwist; public float YawOut; public bool YawHave;
            public Vector3 Target; public float Press; public Vector2 CorrVec; public float Age; public float Slip; public Vector2 PrevH, PrevB, PrevWH, PrevWB; public float Lift; public float HoldT;
        }

        struct Pt { public bool Planted; public Vector2 Anchor; public float Ramp, Away, H0; public bool Spent; }
        readonly Foot _l = new Foot { Left = true }, _r = new Foot();
        readonly Animator _anim; Transform _hips;
        float _hipDrop, _dip, _prevSpeed, _flightDrop, _hipsMean; bool _meanHave;
        bool _ok;
        public float HipDrop { get { return _hipDrop; } }
        /// <summary>v0.12.0 GAIT: extra pelvis sink (m) the start / stop / cut layer asks for; the planted feet stay, the legs bend (set by BodyMotion each frame).</summary>
        public float ExtraDrop;
        /// <summary>v0.12.0 GAIT: 0..1, the body is in a cut. A foot whose contact tag ends while the other foot is not down yet is held planted for up to <see cref="CutHoldMax"/> s (no flight in the middle of a plant-and-cut).</summary>
        /// <summary>v0.12.0 GAIT: weight of the heel anchor against the ball anchor in the correction of a planted foot (1 = the v0.11 average, the default; below 1 leans on the ball of the foot: measured no gain on the cut audit with the final tree, 77 vs 76 %, and a little more sprint slide). QA: -pixFpHeelBias.</summary>
        public float CutHold; public static float HeelBias = 1f; public static float CutHoldMax = 0.08f; public static bool CutHoldOn = false;
        /// <summary>QA: bind-pose heights of the ankle / toe bones above the turf (the zero of the sole height).</summary>
        public float RestAnkleL { get { return _l.RestAnkle; } }
        public float RestToeL { get { return _l.RestToe; } }
        public float RestAnkleR { get { return _r.RestAnkle; } }
        public float RestToeR { get { return _r.RestToe; } }
        public bool PlantedLeft { get { return _l.Planted; } }
        public bool PlantedRight { get { return _r.Planted; } }
        public float WeightLeft { get { return _l.W; } }
        public float WeightRight { get { return _r.W; } }
        /// <summary>QA: horizontal distance (m) the planted foot had to be moved back to its anchor this frame (left / right).</summary>
        public float CorrectionLeft { get { return _l.Corr; } }
        public float CorrectionRight { get { return _r.Corr; } }
        /// <summary>How far the animation had pulled the planted foot from its anchor (world xz, anchor minus animated point) and for how long it has been planted: the gait player learns the real native speed of a cycle from it.</summary>
        public Vector2 CorrVecLeft { get { return _l.CorrVec; } }
        public Vector2 CorrVecRight { get { return _r.CorrVec; } }
        public float AgeLeft { get { return _l.Age; } }
        /// <summary>Forward world speed (m/s, along the travel direction) of the animated planted point before the lock: how fast the take slides over the turf. Used by the gait player to speed the stance up or slow it down.</summary>
        public float SlipLeft { get { return _l.Slip; } }
        public float SlipRight { get { return _r.Slip; } }
        public float LiftLeft { get { return _l.Lift; } }
        public float PressLeft { get { return _l.Press; } }
        public float ResidLeft { get { return _l.Resid; } }
        public float ResidRight { get { return _r.Resid; } }
        public float AgeRight { get { return _r.Age; } }

        // inputs, set by the view each frame
        public Vector2 TravelDir = Vector2.up;     // world xz, unit
        public float Speed, Warp = 1f, GaitLayer;
        public int HintLeft = -1, HintRight = -1;  // -1 no gait information (judge from the pose), 0 swing, 1 contact
        public float Strength = 1f;
        /// <summary>Action clips (throws, kicks, hand-offs ...): the planted-foot lock runs on the final pose from the pose alone (slow low points plant, a 10 cm drift or a rise releases), without stride warp, landing dip or toe-off lift. Set by the view around its own call.</summary>
        public bool ActionMode;
        /// <summary>The action ground pass owns the legs this frame: BodyMotion does not run the locomotion plant or the old lock.</summary>
        public bool ActionClaim;
        /// <summary>The contact hints are frame-accurate (motion matching tags every source frame): a planted point holds until its hint ends instead of letting go when the animated foot rises or runs ahead of its anchor; only the heel still lets go when it lifts.</summary>
        public bool HintAuthority;
        /// <summary>QA / tuning (batch): height below which a hinted point may plant, heel fade on, lift of the matcher's swing feet on.</summary>
        public static float EnterHinted = 0.13f, BallReleaseH = 9f, FlightDrop = 0.04f, PeakFlatten = 0.45f; public static bool HeelFade = false, MmLift = true, FadeRel = true, Pivot = true, YawHoldOn = true, RiseOn = false; public static float HeelShare = 1f;
        /// <summary>The legs of the playing take point along the pelvis heading whatever way the body travels (strafes, back-pedals, the orientation warp): the knee / foot polish aims at the pelvis heading instead of the travel direction.</summary>
        public bool BodyLine;
        /// <summary>Toe landings (back-pedal): the ball of the foot is held first, the heel only follows.</summary>
        public bool BallFirst;
        /// <summary>Height of the turf under the player (the view root); NaN = the animator transform.</summary>
        public float GroundY = float.NaN;

        public FootPlant(Animator a)
        {
            _anim = a;
            if (a == null || !a.isHuman) return;
            _hips = a.GetBoneTransform(HumanBodyBones.Hips);
            Init(_l, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
            Init(_r, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
            _ok = _hips != null && _l.End != null && _r.End != null && _l.Upper != null && _r.Upper != null;
            if (_ok) CaptureAxes(_l); if (_ok) CaptureAxes(_r);
        }

        void CaptureAxes(Foot f)
        {
            // the bind pose is flat: the foot bone's own long and lateral axes, from the pelvis facing (the two hip joints)
            Vector3 bf = Vector3.Cross(_r.Upper.position - _l.Upper.position, Vector3.up); bf.y = 0f;
            if (bf.sqrMagnitude < 1e-4f) { bf = _anim.transform.forward; bf.y = 0f; }
            if (bf.sqrMagnitude < 1e-4f) bf = Vector3.forward;
            bf.Normalize(); Quaternion inv = Quaternion.Inverse(f.End.rotation);
            f.LocalLat = inv * Vector3.Cross(Vector3.up, bf); f.LocalFwd = inv * bf; f.HaveAxes = true;
            if (f.Upper != null && f.Lower != null)
            {
                // the twist references of the shin (against the thigh) and of the foot (against the shin), from the bind pose: the bone axes in each bone's own frame and the bind rotations relative to the parent
                Vector3 shin = (f.End.position - f.Lower.position).normalized;
                f.ShinAxis = Quaternion.Inverse(f.Lower.rotation) * shin; f.FootAxis = Quaternion.Inverse(f.End.rotation) * shin;
                f.ShinBind = Quaternion.Inverse(f.Upper.rotation) * f.Lower.rotation; f.FootBind = Quaternion.Inverse(f.Lower.rotation) * f.End.rotation; f.HaveTwist = true;
            }
        }

        void Init(Foot f, HumanBodyBones u, HumanBodyBones lo, HumanBodyBones e, HumanBodyBones t)
        {
            f.Upper = _anim.GetBoneTransform(u); f.Lower = _anim.GetBoneTransform(lo); f.End = _anim.GetBoneTransform(e); f.Toe = _anim.GetBoneTransform(t);
            float gy = float.IsNaN(GroundY) ? _anim.transform.position.y : GroundY;
            if (f.End != null) f.RestAnkle = f.End.position.y - gy;
            if (f.Toe != null) f.RestToe = f.Toe.position.y - gy;
            if (f.Upper != null && f.Lower != null && f.End != null) f.Reach = Vector3.Distance(f.Upper.position, f.Lower.position) + Vector3.Distance(f.Lower.position, f.End.position);
        }

        public void Cancel()
        {
            _l.Planted = _r.Planted = false; _l.H = _l.B = _r.H = _r.B = default(Pt); _l.W = _r.W = 0f; _l.Have = _r.Have = false; _hipDrop = _dip = 0f; _l.Corr = _r.Corr = 0f;
        }

        float SoleHeight(Foot f, float gy)
        {
            float a = f.End.position.y - gy - f.RestAnkle;
            if (f.Toe == null) return a;
            return Mathf.Min(a, f.Toe.position.y - gy - f.RestToe);
        }

        /// <summary>LateUpdate after the animation. <paramref name="active"/> = the locomotion layer owns the legs.</summary>
        public void Late(float dt, bool active)
        {
            _engaged = false; PolishWeight = 0f;
            if (!_ok || !Enabled || dt <= 1e-5f) return;
            bool moving = ActionMode || (active && Speed > 0.6f);
            if (!moving)
            {
                // let go smoothly; nothing is bent while the legs are not ours
                _l.Planted = _r.Planted = false; _l.H.Planted = _l.B.Planted = _r.H.Planted = _r.B.Planted = false; _l.H.Ramp = _l.B.Ramp = _r.H.Ramp = _r.B.Ramp = 0f; _l.W = Mathf.MoveTowards(_l.W, 0f, dt / 0.05f); _r.W = Mathf.MoveTowards(_r.W, 0f, dt / 0.05f);
                _hipDrop = Mathf.MoveTowards(_hipDrop, 0f, dt / 0.15f); _dip = 0f; _flightDrop = 0f; _meanHave = false; _l.Have = _r.Have = false; _l.Corr = _r.Corr = 0f;
                _prevSpeed = Speed;
                if (_l.W <= 0.001f && _r.W <= 0.001f && _hipDrop <= 0.001f) return;
            }
            _engaged = true;
            {
            }
            float gy = float.IsNaN(GroundY) ? _anim.transform.position.y : GroundY;
            Vector3 rootPos = _anim.transform.position;
            Vector2 root = new Vector2(rootPos.x, rootPos.z);
            Vector2 u = TravelDir; if (u.sqrMagnitude < 0.01f) u = Vector2.up; u.Normalize();
            float k = moving && !ActionMode && !DebugNoWarp ? Warp : 1f;
            // landing compression grows with speed, with the body's mass through the ground fit of the view
            int hl = HintLeft, hr = HintRight;
            if (HintAuthority && CutHold > 0.25f && moving && !ActionMode) { int ol = hl, or2 = hr; hl = HoldHint(_l, ol, or2, dt); hr = HoldHint(_r, or2, ol, dt); } else { _l.HoldT = _r.HoldT = 0f; }
            Step(_l, hl, dt, gy, root, u, k, moving);
            Step(_r, hr, dt, gy, root, u, k, moving);

            // ---- pelvis: legs must reach their targets, plus the landing compression
            float need = Mathf.Clamp(Mathf.Max(Need(_l), Need(_r)), 0f, 0.07f);
            _hipDrop = need > _hipDrop ? Mathf.MoveTowards(_hipDrop, need, dt / 0.025f * 0.09f) : Mathf.MoveTowards(_hipDrop, need, dt / 0.14f * 0.09f);
            _dip -= _dip * Mathf.Min(1f, dt / 0.085f);
            // v0.11.0 B-016: the stride is stretched to the simulated speed and the bounce of the take comes out a little higher than the body needs: the peaks of the pelvis above its own running average are shaved (never the troughs), so both feet are clear of the turf for less of the stride
            float hy = _hips.position.y - gy;
            if (!_meanHave) { _hipsMean = hy; _meanHave = true; }
            _hipsMean = Mathf.Lerp(_hipsMean, hy, 1f - Mathf.Exp(-dt / 0.25f));
            _flightDrop = (HintAuthority && moving && !ActionMode && Speed > 3f) ? Mathf.Clamp((hy - _hipsMean) * PeakFlatten * Mathf.Clamp01((Speed - 3f) / 2f), 0f, FlightDrop) : 0f;
            float drop = Mathf.Clamp((_hipDrop + _dip + _flightDrop + (moving ? ExtraDrop : 0f)) * (moving ? 1f : 0.5f), 0f, 0.11f + ExtraDrop);
            if (drop > 1e-4f) _hips.position -= Vector3.up * drop;
            // a pelvis that sits low (the first frames after a clip gave the legs back: its crouch is still blended in) leaves the feet in the turf: lift it to where the lowest foot stands on it
            if (HintAuthority && moving && !ActionMode && RiseOn) { float low = Mathf.Min(_l.Sole, _r.Sole); if (low < -0.015f) _hips.position += Vector3.up * Mathf.Min(0.15f, -low - 0.005f); }

            // ---- legs
            float wp = (Polish && moving && !ActionMode && !DebugNoIK) ? Ramp01(0.5f, 1.5f, Speed) : 0f;
            PolishWeight = wp;
            Vector3 line = LineOf(u);

            Leg(_l, wp, line); Leg(_r, wp, line);
            _l.Resid = (_l.Target - _l.End.position).magnitude; _r.Resid = (_r.Target - _r.End.position).magnitude;
            if (wp > 0.001f) { FootFix(_l, line, wp, dt); FootFix(_r, line, wp, dt); if (Pivot && HintAuthority) { KeepToe(_l); KeepToe(_r); } } else { _l.YawHave = _r.YawHave = false; }

            _prevSpeed = Speed;
        }

        int HoldHint(Foot f, int hint, int other, float dt)
        {
            if (hint == 1) { f.HoldT = 0f; return 1; }
            if (hint == 0 && f.Planted && other != 1 && f.HoldT < CutHoldMax) { f.HoldT += dt; return 1; }
            f.HoldT = 0f; return hint;
        }

        // The foot touches the ground with two points, the heel (the ankle bone, the foot is flat or heel-first) and the ball (the toe bone). Each has its own anchor and plants / lifts on its own,
        // so the roll-over of the foot stays the animation's while the point that is on the turf stands still.
        void Step(Foot f, int hint, float dt, float gy, Vector2 root, Vector2 u, float k, bool moving)
        {
            Vector3 A = f.End.position, T = f.Toe != null ? f.Toe.position : A;
            float hH = A.y - gy - f.RestAnkle, hT = f.Toe != null ? T.y - gy - f.RestToe : hH + 1f;
            float sole = Mathf.Min(hH, hT);
            f.PrevSole = sole; f.Have = true; f.Sole = sole;
            Vector2 pH = new Vector2(A.x, A.z), pT = new Vector2(T.x, T.z);
            Vector2 shiftH = Vector2.zero, shiftT = Vector2.zero;
            if (moving && Mathf.Abs(k - 1f) > 0.004f)
            {
                shiftH = u * ((k - 1f) * Vector2.Dot(pH - root, u));
                shiftT = u * ((k - 1f) * Vector2.Dot(pT - root, u));
            }
            bool hinted = hint >= 0;
            bool contact = moving && (hinted ? hint == 1 : sole < 0.045f);
            float enterH = hinted ? EnterHinted : 0.035f;
            bool cH = contact, cB = contact;
            if (ActionMode)
            {
                float sH = f.PrevB != Vector2.zero && dt > 1e-4f ? (pH - f.PrevH).magnitude / dt : 99f, sB = f.PrevB != Vector2.zero && dt > 1e-4f ? (pT - f.PrevB).magnitude / dt : 99f;
                cH = contact && sH < (f.H.Planted ? 1.6f : 0.6f); cB = contact && sB < (f.B.Planted ? 1.6f : 0.6f);
            }
            StepPoint(ref f.H, hH, pH + shiftH, cH, enterH, dt, hinted, u, true);
            StepPoint(ref f.B, hT, pT + shiftT, cB, enterH, dt, hinted, u, false);
            bool was = f.Planted;

            f.Planted = f.H.Planted || f.B.Planted;
            f.Age = f.Planted ? f.Age + dt : 0f;
            if (was && !(f.H.Planted || f.B.Planted) && Speed > 2f && !ActionMode) f.Lift = Mathf.Max(f.Lift, 0.04f);      // toe-off: the foot leaves the turf instead of skidding along it
            if (f.Planted && !was && Speed > 1.5f) _dip = Mathf.Max(_dip, Mathf.Clamp(0.008f + 0.0030f * Speed, 0f, 0.035f) * Strength);
            // weights fade with the point's height above the turf (a heel that rises hands the foot to the ball)
            // with frame-accurate hints the fade counts from the height the point had when it touched down (the pose floats a few cm above the turf before the lock presses it down), so a planted point keeps its full weight
            float fadeH = HintAuthority && FadeRel && f.H.Planted ? hH - f.H.H0 : hH, fadeB = HintAuthority && FadeRel && f.B.Planted ? hT - f.B.H0 : hT;
            float wH = f.H.Ramp * (HintAuthority && FadeRel ? 1f - Ramp01(0.03f, 0.075f, fadeH) : 1f - Mathf.SmoothStep(0.03f, 0.075f, fadeH)), wB = f.B.Ramp * (HintAuthority && FadeRel ? 1f - Ramp01(0.03f, 0.075f, fadeB) : 1f - Mathf.SmoothStep(0.03f, 0.075f, fadeB));      // (the legacy fade is Mathf.SmoothStep(from, to, t), which never went below 0.92: kept for the old gait player)
            if (HeelFade && HintAuthority && f.H.Planted) wH *= 1f - Ramp01(0.010f, 0.035f, hH - f.H.H0);
            if (HintAuthority && (BallFirst || HeelShare < 0.999f)) wH *= BallFirst ? 0.1f : HeelShare;      // the ball of the foot is the contact: it is held first, the heel only follows
            f.WHeel = wH; f.WBall = wB;
            float wt = wH + wB, wmax = Mathf.Max(wH, wB);
            Vector2 corr = Vector2.zero;
            if (wt > 1e-4f) { float wHc = wH * HeelBias, wc = wHc + wB; if (wc > 1e-4f) corr = ((f.H.Anchor - (pH + shiftH)) * wHc + (f.B.Anchor - (pT + shiftT)) * wB) / wc; else corr = ((f.H.Anchor - (pH + shiftH)) * wH + (f.B.Anchor - (pT + shiftT)) * wB) / wt; }
            f.CorrVec = corr;
            if (corr.magnitude > 0.30f) corr = corr.normalized * 0.30f;
            f.W = wmax;
            float w = wmax * Strength;
            f.Corr = corr.magnitude * w;
            // the unplanted part of the foot still follows the stride warp
            Vector2 shift = (shiftH + shiftT) * 0.5f;
            Vector2 xz = shift * (1f - w) + (shift + corr) * w;
            float press = w > 0.001f ? Mathf.Clamp(SoleGoal - sole, -0.12f, 0.12f) * w : 0f;
            // a foot that is not planted but skims the turf faster than the body walks is dragged: the mocap toe-off skids along the turf for a few frames. Lift it clear of the ground in proportion to how fast it moves over it.
            float vSole = 0f;
            if (moving && dt > 1e-4f && f.PrevB != Vector2.zero) { Vector2 dH = (pH - f.PrevH) / dt, dB = (pT - f.PrevB) / dt; vSole = hT < hH ? dB.magnitude : dH.magnitude; }
            float slipW = wH + wB;
            if (moving && dt > 1e-4f && f.PrevB != Vector2.zero && slipW > 0.05f) { float sH = Vector2.Dot((pH + shiftH - f.PrevWH) / dt, u), sB = Vector2.Dot((pT + shiftT - f.PrevWB) / dt, u); f.Slip = (sH * wH + sB * wB) / slipW; } else f.Slip = 0f;
            f.PrevH = pH; f.PrevB = pT; f.PrevWH = pH + shiftH; f.PrevWB = pT + shiftT;
            float need = moving && !ActionMode ? (HintAuthority && MmLift ? Mathf.Clamp((vSole - 0.4f) * 0.07f, 0f, 0.085f) : Mathf.Clamp((vSole - 0.5f) * 0.05f, 0f, 0.06f)) : 0f;      // a swing foot that skims the turf is lifted clear in proportion to how fast it moves over it (more with the matcher: its swing feet pass 2-4 cm up)
            float wantLift = Mathf.Max(0f, need - Mathf.Max(0f, sole)) * (1f - w);
            if (HintAuthority && moving && !ActionMode && sole < 0f) wantLift = Mathf.Max(wantLift, (-sole + 0.004f) * (1f - w));      // a swing foot never sinks into the turf (the pose of the first frames after a hand-back sits low)
            f.Lift = Mathf.MoveTowards(f.Lift, wantLift, (wantLift > f.Lift ? (HintAuthority && MmLift ? 6.0f : 3.0f) : 0.35f) * dt);
            f.Target = new Vector3(A.x + xz.x, A.y + press + f.Lift, A.z + xz.y);
            f.Press = press;
        }

        void StepPoint(ref Pt p, float h, Vector2 pw, bool contact, float enterH, float dt, bool hinted, Vector2 u, bool heel)
        {
            if (!p.Planted)
            {
                if (!contact) p.Spent = false;
                if (contact && !p.Spent && h < enterH) { p.Planted = true; p.Anchor = pw; p.Away = 0f; p.H0 = h; }
            }
            else
            {
                p.Away = (p.Anchor - pw).magnitude;
                // the animation starts the swing: the point rises, or it runs ahead of its anchor (the hint of the phase markers only says where the take puts the lift-off)
                float ahead = Vector2.Dot(pw - p.Anchor, u);
                bool auth = HintAuthority && hinted && !ActionMode;
                bool release = !contact || (auth ? ((heel && h > p.H0 + 0.035f) || (!heel && h > p.H0 + BallReleaseH)) : (h > p.H0 + 0.035f || (ActionMode ? p.Away > 0.10f : ahead > 0.05f))) || p.Away > 0.32f;
                if (release) { p.Planted = false; p.Spent = contact; }
            }
            p.Ramp = Mathf.MoveTowards(p.Ramp, p.Planted ? 1f : 0f, dt / (p.Planted ? 0.03f : 0.06f));
        }

        /// <summary>How far the hips must drop for this leg to reach its target (0 = it reaches).</summary>
        static float Need(Foot f)
        {
            Vector3 H = f.Upper.position, T = f.Target;
            float h = new Vector2(T.x - H.x, T.z - H.z).magnitude, dv = H.y - T.y;
            float reach = f.Reach * 0.985f;
            float vmax = Mathf.Sqrt(Mathf.Max(0f, reach * reach - h * h));
            return dv - vmax;
        }

        /// <summary>True while the planted-foot solver owns the legs (moving, or still letting go).</summary>
        public bool Engaged { get { return _engaged; } }
        bool _engaged;
        public bool Ok { get { return _ok; } }

        static float Ramp01(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>QA: the foot bone's own up / forward axes (bind pose, flat foot) for measuring the tilt of the sole.</summary>
        public bool SoleAxes(bool left, out Vector3 localLat, out Vector3 localFwd) { var f = left ? _l : _r; localLat = f.LocalLat; localFwd = f.LocalFwd; return f.HaveAxes; }

        /// <summary>Yaw of a foot against a line (degrees, + = toes to the right), from the foot's lateral axis, which stays level however steeply the foot pitches (the toe-bone direction flips when the foot hangs toe-down).</summary>
        public static bool YawOf(Quaternion rot, Vector3 localLat, Vector3 line, out float yaw)
        {
            Vector3 lat = rot * localLat; lat.y = 0f; yaw = 0f;
            if (lat.sqrMagnitude < 0.09f) return false;
            Vector3 fwd = Vector3.Cross(lat.normalized, Vector3.up);
            yaw = Vector3.SignedAngle(line, fwd, Vector3.up);
            return true;
        }

        /// <summary>Roll of a foot about its long axis (degrees, 0 = the foot axis pair is level); false when the foot points too steeply up or down for a roll to mean anything.</summary>
        public static bool RollOf(Quaternion rot, Vector3 localLat, Vector3 localFwd, out float roll, out Vector3 fwdW)
        {
            fwdW = rot * localFwd; Vector3 lat = rot * localLat; roll = 0f;
            Vector3 lat0 = Vector3.Cross(Vector3.up, fwdW);
            if (lat0.sqrMagnitude < 0.25f) return false;       // pitched more than 60 degrees
            roll = Vector3.SignedAngle(lat0.normalized, lat, fwdW);
            return true;
        }

        /// <summary>The line a square runner's legs swing along (horizontal unit): the travel direction going forward, its reverse backpedalling, the body's own forward in between (shuffles).</summary>
        Vector3 LineOf(Vector2 travel)
        {
            Vector3 body = Vector3.Cross(_r.Upper.position - _l.Upper.position, Vector3.up); body.y = 0f;       // the pelvis facing from the two hip joints (the animator transform is not the body heading)
            if (body.sqrMagnitude < 1e-4f) { body = _anim.transform.forward; body.y = 0f; }
            body = body.sqrMagnitude < 1e-4f ? Vector3.forward : body.normalized;
            if (BodyLine) return body;
            Vector3 t = new Vector3(travel.x, 0f, travel.y);
            float d = Vector3.Dot(t, body);
            float w = Ramp01(0.3f, 0.7f, Mathf.Abs(d));
            if (w < 0.001f) return body;
            Vector3 to = d >= 0f ? t : -t;
            Vector3 l = Vector3.Slerp(body, to, w);
            return l.sqrMagnitude < 1e-6f ? body : l.normalized;
        }

        /// <summary>The polish turns the foot about the ankle (yaw toward the line, roll, twist limits), which swings the ball of the foot off the point the lock holds. The ankle follows by the same amount the other way, so
        /// the ball of a planted foot stays where it was put (the share of the ball in the lock decides how much).</summary>
        void KeepToe(Foot f)
        {
            if (f.Toe == null || !f.Planted) return;
            float wt = f.WHeel + f.WBall; if (wt < 0.05f) return;
            float k = Mathf.Clamp01(f.WBall / wt) * Mathf.Clamp01(f.W);
            Vector3 d = f.ToeHold - f.Toe.position; d.y = 0f;
            if (d.sqrMagnitude < 1e-8f || k < 0.01f) return;
            if (d.magnitude > 0.08f) d = d.normalized * 0.08f;
            Vector3 pole = f.Lower.position - (f.Upper.position + f.End.position) * 0.5f;
            if (pole.sqrMagnitude < 1e-8f) pole = Vector3.forward;
            LimbIK.Apply(f.Upper, f.Lower, f.End, f.End.position + d * k, pole, 1f);
        }

        void Leg(Foot f, float wp, Vector3 line)
        {
            if (DebugNoIK) return;
            bool move = (f.Target - f.End.position).sqrMagnitude >= 1e-7f;
            if (!move && wp <= 0.001f) return;
            if (move)
            {
                Vector3 pole = f.Lower.position - (f.Upper.position + f.End.position) * 0.5f;
                if (pole.sqrMagnitude < 1e-8f) pole = Vector3.forward;
                LimbIK.Apply(f.Upper, f.Lower, f.End, f.Target, pole, 1f);
            }
            f.ToeHold = f.Toe != null ? f.Toe.position : f.End.position;
            if (wp <= 0.001f) return;
            // Knee along the line of travel. The knee is turned around the hip-ankle axis and the WHOLE leg turns with it (thigh, shin, foot as one rigid piece: the ankle stays where it is, the knee hinge stays square to
            // the thigh), instead of swinging the knee out of the take's bend plane, which leaves the thigh twisted against its own knee. The take's own variation (+-6 degrees) is left alone.
            Vector3 H = f.Upper.position, K = f.Lower.position, A = f.End.position;
            Vector3 axis = A - H;
            if (axis.sqrMagnitude > 1e-6f)
            {
                axis.Normalize();
                Vector3 want = Quaternion.AngleAxis(f.Left ? -3f : 3f, Vector3.up) * line;
                Vector3 pw = want - axis * Vector3.Dot(want, axis), pa = (K - H) - axis * Vector3.Dot(K - H, axis);
                if (pw.sqrMagnitude >= 0.05f && pa.sqrMagnitude >= 1e-5f)      // else the leg lies along the line / is straight: nothing to turn
                {
                    float phi = Vector3.SignedAngle(pa, pw, axis);
                    phi -= Mathf.Clamp(phi, -KneeSlack, KneeSlack);
                    phi = Mathf.Clamp(phi, -KneeMax, KneeMax);
                    if (Mathf.Abs(phi) > 0.01f) f.Upper.rotation = Quaternion.AngleAxis(phi * wp, axis) * f.Upper.rotation;
                }
            }
            // The takes twist the shin and the foot about their own axes far beyond what a runner does (the humanoid twist muscles sit at their limits: the foot points anywhere): hold each to its limit around the bind pose.
            // Both turns are about an axis that runs through the ankle, so nothing moves but the foot's heading.
            if (f.HaveTwist)
            {
                LimitTwist(f.Lower, f.Upper, f.ShinAxis, f.ShinBind, ShinTwistLimit, wp);
                LimitTwist(f.End, f.Lower, f.FootAxis, f.FootBind, FootTwistLimit, wp);
            }
        }

        /// <summary>Twist of a bone about its own long axis against its parent, relative to the bind pose (degrees): the swing-twist split of the rotation.</summary>
        public static float TwistOf(Quaternion parentWorld, Quaternion childWorld, Vector3 axisLocal, Quaternion bindRel)
        {
            Quaternion qd = Quaternion.Inverse(bindRel) * (Quaternion.Inverse(parentWorld) * childWorld);
            float p = qd.x * axisLocal.x + qd.y * axisLocal.y + qd.z * axisLocal.z;
            return Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(p, qd.w) * Mathf.Rad2Deg);
        }

        static void LimitTwist(Transform child, Transform parent, Vector3 axisLocal, Quaternion bindRel, float limit, float w)
        {
            float tw = TwistOf(parent.rotation, child.rotation, axisLocal, bindRel);
            float want = Mathf.Clamp(tw, -limit, limit);
            float d = (want - tw) * w;
            if (Mathf.Abs(d) > 0.01f) child.localRotation = child.localRotation * Quaternion.AngleAxis(d, axisLocal);
        }

        /// <summary>Foot after the leg IK: yaw pulled toward the line of travel (never more than FootYawLimit off it, a slight toe-out), roll about the foot's long axis limited (flat in stance), pitch left to the animation (heel strike / toe-off).</summary>
        void FootFix(Foot f, Vector3 line, float wp, float dt)
        {
            if (f.Toe == null || !f.HaveAxes) return;
            float yaw;
            if (YawOf(f.End.rotation, f.LocalLat, line, out yaw))
            {
                float outSign = f.Left ? -1f : 1f, bias = FootToeOut * outSign;
                float rel = Mathf.DeltaAngle(bias, yaw);       // + = toes to the right of the line
                float newRel = Mathf.Clamp(rel * (1f - 0.7f * wp), -FootYawLimit, FootYawLimit);
                float target = bias + newRel;
                // a planted foot does not turn on the turf: the heading it had when it came down is kept (in the world, so a turning line does not drag it) until it lifts
                if (HintAuthority && YawHoldOn)
                {
                    float lineYaw = Mathf.Atan2(line.x, line.z) * Mathf.Rad2Deg;
                    if (f.Planted && f.W > 0.5f)
                    {
                        if (!f.YawHold) { f.YawHold = true; f.HoldAbs = lineYaw + target; }
                        target = Mathf.Clamp(Mathf.DeltaAngle(lineYaw, f.HoldAbs) - bias, -FootYawLimit * 2f, FootYawLimit * 2f) + bias;
                    }
                    else f.YawHold = false;
                }
                if (f.YawHave) target = Mathf.MoveTowardsAngle(f.YawOut, target, FootYawRate * dt);     // no foot flick: the heading follows its target at a limited turn rate
                float delta = Mathf.DeltaAngle(yaw, target); f.YawOut = target; f.YawHave = true;

                if (Mathf.Abs(delta) > 0.01f) f.End.rotation = Quaternion.AngleAxis(delta, Vector3.up) * f.End.rotation;
            }
            float tilt; Vector3 fwdW;
            if (RollOf(f.End.rotation, f.LocalLat, f.LocalFwd, out tilt, out fwdW))
            {
                float lim = Mathf.Lerp(FootRollLimit, 2f, f.W * wp);
                float wr = wp;
                float want = Mathf.Clamp(tilt, -lim, lim);
                if (Mathf.Abs(want - tilt) > 0.01f)
                {
                    Quaternion before = f.End.rotation;
                    float d = (want - tilt) * wr;
                    f.End.rotation = Quaternion.AngleAxis(d, fwdW) * before;
                    float t2; Vector3 f2;
                    if (RollOf(f.End.rotation, f.LocalLat, f.LocalFwd, out t2, out f2) && Mathf.Abs(t2 - want) > Mathf.Abs(tilt - want)) f.End.rotation = Quaternion.AngleAxis(-d, fwdW) * before;     // the sign convention of the angle helper is not trusted: never make it worse
                }
            }
        }
    }
}
