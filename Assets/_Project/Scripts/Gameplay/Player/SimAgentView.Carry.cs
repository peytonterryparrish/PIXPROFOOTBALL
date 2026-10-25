using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 RB track (owner note 7: "mirror how running backs run with the football in real life"). The ball-carrier style layer of the view. The simulation decides how the carrier runs
    /// (<see cref="CarryFeed"/>: carry arm, press, crouch, cut phase, planned move); this layer draws it on top of the motion matcher's legs, in LateUpdate after the arm IK:
    ///  - the ball arm holds the ball high and tight (five points of pressure: fingertips over the point, forearm, biceps, ribs, chest), the other arm pumps naturally against the legs;
    ///  - the ball crosses the chest when the sim changes the carry arm (the free hand comes to the ball, the ball changes arms, the old arm releases);
    ///  - forward lean over the feet, lowered centre of gravity (the hips sink with the feet kept where they are) in traffic and at the plant of a cut;
    ///  - two hands on the ball when a defender is about to hit him;
    ///  - the free arm is the stiff-arm: it reaches into the defender's chest for the length of the move.
    /// Real-life basis: see <c>Docs/QA/v0110/rb.md</c>.
    /// </summary>
    public partial class SimAgentView
    {
        /// <summary>What the simulation decided about the carrier this frame (filled by PlayStage.CarryHook).</summary>
        public struct CarryFeed
        {
            public bool Has, Two, Tight, Protecting;
            public int Arm;                      // +1 right, -1 left
            public float Press, Crouch;
            public int CutPhase, CutKind, CutPlant;      // CarryCutPhase / CarryCutKind / foot that plants (+1 right)
            public float CutAngle, CutEta, CutT;
            public int Plan, Move;                    // CarrierMove
            public float PlanEta, MoveT, MoveTotal;
            public bool HasThreat; public Vector3 ThreatPos; public int ThreatSide;
            public float SimSpeed;                    // m/s
        }

        /// <summary>QA: -pixCarryOff plays the v0.10 carrier (animator carry tree, old tuck IK, view-side carry arm).</summary>
        public static bool DebugNoCarry;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void CarrySwitch()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-pixCarryOff") >= 0) { DebugNoCarry = true; CarryStyleConfig.Enabled = false; }
        }

        CarryFeed _cf; bool _cfHave;
        public void SetCarryFeed(in CarryFeed f) { _cf = f; _cfHave = true; }
        /// <summary>The arm the ball is drawn on (the BallFollower reads it): changes half way through a switch. 0 before the first carry frame.</summary>
        public int CarryArmShown { get { return _armShown; } }
        /// <summary>Weight of the carry layer (QA / audit).</summary>
        public float CarryWeight { get { return _cw; } }
        public bool CarrySwitching { get { return _swT >= 0f; } }

        // ---- state
        float _cw;                         // layer weight
        int _armShown, _armWant;           // arm on which the ball is drawn, arm the sim wants
        float _swT = -1f;                  // seconds into an arm switch
        readonly float[] _grip = new float[2];       // 0 = left arm, 1 = right arm: 0 = free pump, 1 = on the ball
        readonly float[] _tuckB = new float[2];      // 1 = the arm holds the ball (tuck pose), 0 = covers it (second hand)
        readonly float[] _ext = new float[2];        // stiff-arm extension
        float _lean, _sink, _crouchW, _plantW, _pumpPh;
        Transform _cHips, _cSpine, _cChest, _cHead, _cLUA, _cLLA, _cLH, _cRUA, _cRLA, _cRH, _cLUL, _cLLL, _cLF, _cRUL, _cRLL, _cRF; bool _cBones;
        float _prevSpeed; bool _haveSpeed; float _accSm;
        // QA read-outs
        public float CarryLeanDeg { get { return _lean; } }
        public float CarrySinkM { get { return _sink; } }

        void FindCarryBones()
        {
            if (_cBones || Avatar == null || Avatar.Animator == null || !Avatar.Animator.isHuman) return;
            var a = Avatar.Animator;
            _cHips = a.GetBoneTransform(HumanBodyBones.Hips); _cSpine = a.GetBoneTransform(HumanBodyBones.Spine); _cChest = a.GetBoneTransform(HumanBodyBones.Chest) ?? a.GetBoneTransform(HumanBodyBones.UpperChest);
            _cHead = a.GetBoneTransform(HumanBodyBones.Head);
            _cLUA = a.GetBoneTransform(HumanBodyBones.LeftUpperArm); _cLLA = a.GetBoneTransform(HumanBodyBones.LeftLowerArm); _cLH = a.GetBoneTransform(HumanBodyBones.LeftHand);
            _cRUA = a.GetBoneTransform(HumanBodyBones.RightUpperArm); _cRLA = a.GetBoneTransform(HumanBodyBones.RightLowerArm); _cRH = a.GetBoneTransform(HumanBodyBones.RightHand);
            _cLUL = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg); _cLLL = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg); _cLF = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            _cRUL = a.GetBoneTransform(HumanBodyBones.RightUpperLeg); _cRLL = a.GetBoneTransform(HumanBodyBones.RightLowerLeg); _cRF = a.GetBoneTransform(HumanBodyBones.RightFoot);
            _cBones = _cHips != null && _cSpine != null && _cChest != null && _cHead != null && _cLUA != null && _cLLA != null && _cLH != null && _cRUA != null && _cRLA != null && _cRH != null
                      && _cLUL != null && _cLLL != null && _cLF != null && _cRUL != null && _cRLL != null && _cRF != null;
        }

        static bool CarryActionOk(AgentAction a)
        {
            switch (a)
            {
                case AgentAction.Carry: case AgentAction.Run: case AgentAction.Sprint: case AgentAction.Idle: case AgentAction.Juke: case AgentAction.StiffArm: case AgentAction.Shuffle: case AgentAction.Backpedal: case AgentAction.Stumble:
                    return true;
            }
            return false;
        }

        /// <summary>LateUpdate, after the arm IK: the carrier's style.</summary>
        void CarryLate(float dt)
        {
            if (dt <= 1e-5f) return;
            bool on = !DebugNoCarry && CarryStyleConfig.Enabled && _cfHave && _cf.Has && !ContactDriven && !Huddle && !(Ragdoll != null && Ragdoll.Active) && CarryActionOk(_lastAction) && Avatar != null && Avatar.Animator != null && Avatar.Animator.isHuman;
            if (on && Rig != null && Rig.Valid && Rig.Weight > 0.25f && !Rig.UpperBodyActive) on = false;      // a full-body take (spin, hurdle, truck, dive, tackle, catch) owns the pose
            _cw = Mathf.MoveTowards(_cw, on ? 1f : 0f, dt / (on ? 0.14f : 0.10f));
            if (_cw <= 0.01f)
            {
                if (!on) { _armShown = 0; _swT = -1f; _grip[0] = _grip[1] = 0f; _ext[0] = _ext[1] = 0f; _lean = 0f; _sink = 0f; _haveSpeed = false; }
                return;
            }
            FindCarryBones(); if (!_cBones) return;
            var f = _cf;
            Vector3 fwd = Forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float speed = f.SimSpeed;

            // ---- arm switch (the sim changed the carry arm): the free hand comes to the ball, the ball changes arms, the old arm releases
            if (_armShown == 0) { _armShown = f.Arm; _armWant = f.Arm; _tuckB[ArmIdx(f.Arm)] = 1f; _grip[ArmIdx(f.Arm)] = 1f; }
            else if (f.Arm != _armWant) { _armWant = f.Arm; _swT = 0f; }
            if (_swT >= 0f)
            {
                _swT += dt;
                if (_armShown != _armWant && _swT >= 0.085f) _armShown = _armWant;
                if (_swT >= 0.30f) _swT = -1f;
            }
            int shown = ArmIdx(_armShown), other = 1 - shown;
            bool switching = _swT >= 0f;

            // ---- grips: the ball arm is always on the ball; the other arm covers the ball while switching, in traffic (two hands) or pumps
            bool two = f.Two || switching || f.Move == (int)CarrierMove.Truck;
            float upT = 1f / 0.09f, dnT = 1f / 0.16f;
            _grip[shown] = Mathf.MoveTowards(_grip[shown], 1f, dt * upT);
            _grip[other] = Mathf.MoveTowards(_grip[other], two ? 1f : 0f, dt * (two ? upT : dnT));
            _tuckB[shown] = Mathf.MoveTowards(_tuckB[shown], 1f, dt / 0.12f);
            _tuckB[other] = Mathf.MoveTowards(_tuckB[other], 0f, dt / 0.10f);

            // ---- stiff arm: the free arm (on the defender's side) reaches into his chest for the length of the move
            float stiffE = 0f;
            bool stiff = f.Move == (int)CarrierMove.StiffArm && f.HasThreat;
            if (stiff) stiffE = LeanMath.Envelope(f.MoveT, f.MoveTotal, 0.10f, 0.12f);
            _ext[other] = Mathf.MoveTowards(_ext[other], stiffE, dt / 0.05f);
            _ext[shown] = Mathf.MoveTowards(_ext[shown], 0f, dt / 0.05f);

            // ---- acceleration (for the lean) from the simulated speed
            if (!_haveSpeed) { _prevSpeed = speed; _haveSpeed = true; }
            float acc = (speed - _prevSpeed) / dt; _prevSpeed = speed;
            _accSm = Mathf.Lerp(_accSm, Mathf.Clamp(acc, -12f, 12f), 1f - Mathf.Exp(-dt / 0.25f));

            // ---- posture: lean (spine + chest about the body's right axis, the head stays up) and the sink (hips down, feet kept)
            float speedFrac = Mathf.Clamp01((speed - 1.5f) / 6f);
            float curLean = Mathf.Atan2(Vector3.Dot(_cChest.position - _cHips.position, fwd), (_cChest.position - _cHips.position).y) * Mathf.Rad2Deg;      // what the animation and BodyMotion already lean
            float leanWant = (8.0f + 6.0f * speedFrac) + Mathf.Clamp(_accSm, -3f, 6f) * 1.0f + 10f * f.Crouch + 4f * f.Press;
            float plant = f.CutPhase == (int)CarryCutPhase.Plant ? 1f : f.CutPhase == (int)CarryCutPhase.Drive ? Mathf.Clamp01(1f - f.CutT / 0.5f) * 0.55f : f.CutPhase == (int)CarryCutPhase.Approach ? 0.25f : 0f;
            _plantW = Mathf.Lerp(_plantW, plant, 1f - Mathf.Exp(-dt / (plant > _plantW ? 0.05f : 0.14f)));
            leanWant += 6f * _plantW;
            float leanT = Mathf.Clamp(Mathf.Clamp(Motion != null ? Motion.GaitWant(leanWant) : leanWant, -14f, 30f) - curLean, -12f, 14f);      // v0.12.0 GAIT hook: the start / stop lean of the layer (one line, floor -4 -> -14)      // only the difference is added
            _lean = Mathf.Lerp(_lean, leanT, 1f - Mathf.Exp(-dt / 0.12f));
            float sinkT = DebugNoCarrySink ? 0f : 0.045f * f.Crouch + 0.03f * f.Press + 0.05f * _plantW;
            if (f.Tight) sinkT *= 0.5f;
            _sink = Mathf.Lerp(_sink, sinkT, 1f - Mathf.Exp(-dt / (sinkT > _sink ? 0.07f : 0.16f)));
            // juke: the shoulders and head sell the fake (set-up the other way, then through the cut), the hips sink into the plant; the legs stay the matcher's (foot-locked)
            float jYaw = 0f, jBank = 0f, jSink = 0f; bool juke = f.Plan == (int)CarrierMove.JukeLeft || f.Plan == (int)CarrierMove.JukeRight || f.Move == (int)CarrierMove.JukeLeft || f.Move == (int)CarrierMove.JukeRight;
            if (juke)
            {
                int jm = f.Move != 0 ? f.Move : f.Plan; float js = jm == (int)CarrierMove.JukeRight ? 1f : -1f;
                float tj = f.Move != 0 ? f.MoveT : -f.PlanEta;
                float bA = Bump(tj, -0.30f, -0.12f, 0.04f), bB = Bump(tj, 0.0f, 0.17f, 0.40f);
                jYaw = js * (-15f * bA + 14f * bB); jBank = js * (-6f * bA + 10f * bB); jSink = 0.045f * bB;
            }
            _jYaw = Mathf.Lerp(_jYaw, jYaw, 1f - Mathf.Exp(-dt / 0.04f)); _jBank = Mathf.Lerp(_jBank, jBank, 1f - Mathf.Exp(-dt / 0.05f));
            _sink = Mathf.Max(_sink, jSink);
            ApplyLeanAndSink(fwd, right, _lean * _cw, _sink * _cw);
            if (Mathf.Abs(_jYaw) > 0.1f || Mathf.Abs(_jBank) > 0.1f)
            {
                float w = _cw; Quaternion q = Quaternion.AngleAxis(_jYaw * w, Vector3.up) * Quaternion.AngleAxis(-_jBank * w, fwd);
                _cSpine.rotation = Quaternion.Slerp(Quaternion.identity, q, 0.5f) * _cSpine.rotation; _cChest.rotation = Quaternion.Slerp(Quaternion.identity, q, 0.5f) * _cChest.rotation;
                _cHead.rotation = Quaternion.AngleAxis(-_jYaw * 0.6f * w, Vector3.up) * Quaternion.AngleAxis(_jBank * 0.8f * w, fwd) * _cHead.rotation;
            }

            // ---- arms
            DriveCarryArm(0, fwd, right, f, shown, dt);
            DriveCarryArm(1, fwd, right, f, shown, dt);
        }

        float _jYaw, _jBank;
        static float Bump(float t, float a, float peak, float b) { if (t <= a || t >= b) return 0f; float u = t < peak ? (t - a) / (peak - a) : 1f - (t - peak) / (b - peak); return u * u * (3f - 2f * u); }

        static int ArmIdx(int side) { return side >= 0 ? 1 : 0; }      // 1 = right

        void ApplyLeanAndSink(Vector3 fwd, Vector3 right, float leanDeg, float sink)
        {
            // hips down with the ankles where the animation put them (knees bend: the centre of gravity drops, the feet do not move)
            if (sink > 0.002f)
            {
                Vector3 lf = _cLF.position, rf = _cRF.position;
                _cHips.position -= Vector3.up * sink;
                SolveLeg(_cLUL, _cLLL, _cLF, lf, fwd); SolveLeg(_cRUL, _cRLL, _cRF, rf, fwd);
            }
            if (Mathf.Abs(leanDeg) > 0.1f)
            {
                Quaternion q = Quaternion.AngleAxis(leanDeg, right);
                _cSpine.rotation = Quaternion.Slerp(Quaternion.identity, q, 0.5f) * _cSpine.rotation;
                _cChest.rotation = Quaternion.Slerp(Quaternion.identity, q, 0.5f) * _cChest.rotation;
                _cHead.rotation = Quaternion.AngleAxis(-leanDeg * 0.85f, right) * _cHead.rotation;      // eyes up
            }
        }

        static void SolveLeg(Transform up, Transform lo, Transform foot, Vector3 target, Vector3 fwd)
        {
            Vector3 pole = lo.position - (up.position + foot.position) * 0.5f;
            if (pole.sqrMagnitude < 1e-6f) pole = fwd;
            LimbIK.Apply(up, lo, foot, target, pole * 4f + fwd * 0.05f, 1f);
        }

        // ------------------------------------------------------------------------------------------ arms
        void DriveCarryArm(int idx, Vector3 fwd, Vector3 right, in CarryFeed f, int shown, float dt)
        {
            Transform ua = idx == 1 ? _cRUA : _cLUA, la = idx == 1 ? _cRLA : _cLLA, hand = idx == 1 ? _cRH : _cLH;
            float s = idx == 1 ? 1f : -1f;                       // lateral sign of this arm's shoulder
            float g = _grip[idx], tb = _tuckB[idx], ex = _ext[idx];
            if (g < 0.01f && ex < 0.01f && _cw < 0.01f) return;
            Vector3 sh = ua.position;
            float l1 = Vector3.Distance(ua.position, la.position), l2 = Vector3.Distance(la.position, hand.position);

            // free pump (the arm swings against the leg on its own side; the amplitude follows the speed)
            Transform thigh = idx == 1 ? _cRUL : _cLUL, shin = idx == 1 ? _cRLL : _cLLL;
            float legA = Mathf.Atan2(Vector3.Dot(shin.position - thigh.position, fwd), -(shin.position - thigh.position).y) * Mathf.Rad2Deg;      // + = knee forward of the hip
            float spd = Mathf.Clamp01((f.SimSpeed - 2f) / 6f);
            float gain = Mathf.Lerp(0.9f, 1.35f, spd);
            float th = Mathf.Clamp(-legA * gain, -62f, 62f) * Mathf.Deg2Rad;
            float flex = Mathf.Lerp(92f, 78f, spd) + 14f * Mathf.Clamp01(th * Mathf.Rad2Deg / 50f);
            float abd = 0.10f + 0.28f * _plantW + (f.Tight && idx == (f.Arm >= 0 ? 0 : 1) ? 0.15f : 0f);
            Vector3 u = (-Vector3.up * Mathf.Cos(th) + fwd * Mathf.Sin(th) + right * (s * abd)).normalized;
            float ph = th + flex * Mathf.Deg2Rad;
            Vector3 fa = (-Vector3.up * Mathf.Cos(ph) + fwd * Mathf.Sin(ph) + right * (s * abd * 0.5f)).normalized;
            Vector3 pumpE = sh + u * l1, pumpH = pumpE + fa * l2;
            Vector3 pumpPole = (pumpE - (sh + pumpH) * 0.5f);

            // the ball arm: elbow at the ribs, forearm across the front of the chest, hand over the point of the ball
            Vector3 chest = _cChest.position;
            float inward = -s;
            Vector3 tuckH = sh + (-Vector3.up) * 0.21f + fwd * 0.24f + right * (inward * 0.11f);
            Vector3 tuckPole = right * (s * 0.8f) + Vector3.down * 0.55f - fwd * 0.35f;
            // the second hand: on top of the ball's point from the other side
            int shownIdx = shown;
            Vector3 shownSh = (shownIdx == 1 ? _cRUA : _cLUA).position;
            float ss = shownIdx == 1 ? 1f : -1f;
            Vector3 ballTip = shownSh + Vector3.down * 0.17f + fwd * 0.25f + right * (-ss * 0.085f);
            Vector3 coverH = ballTip + right * (-ss * 0.07f) + Vector3.up * 0.05f;
            Vector3 coverPole = right * (s * 0.7f) + Vector3.down * 0.7f - fwd * 0.2f;

            Vector3 holdH = Vector3.Lerp(coverH, tuckH, tb), holdPole = Vector3.Lerp(coverPole, tuckPole, tb);
            Vector3 H = Vector3.Lerp(pumpH, holdH, g), pole = Vector3.Lerp(pumpPole.sqrMagnitude > 1e-5f ? pumpPole.normalized : right * s, holdPole.normalized, g);

            // stiff arm: straight arm into the defender's chest
            if (ex > 0.005f && f.HasThreat)
            {
                Vector3 tgt = f.ThreatPos + Vector3.up * 1.32f;
                Vector3 d = tgt - sh; float dl = d.magnitude;
                Vector3 extH = sh + d / Mathf.Max(0.05f, dl) * Mathf.Min(dl - 0.05f, (l1 + l2) * 0.985f);
                H = Vector3.Lerp(H, extH, ex); pole = Vector3.Lerp(pole, (right * s + Vector3.down * 0.9f).normalized, ex);
            }
            LimbIK.Apply(ua, la, hand, H, pole, Mathf.Clamp01(_cw));
        }
    }
}
