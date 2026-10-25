using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 LOCOMOTION (LOCO-005): the view side of the gait system. Feeds the simulated velocity (authoritative: no smoothing between the simulation and the feet) to the gait synthesizer,
    /// hands its contact / warp state to the planted-foot solver in BodyMotion and turns the body so the playing cycles travel along the simulated velocity.
    /// v0.10.0 MOTION: the same job for the motion matcher (<see cref="MotionMatcher"/>): the simulated velocity AND its planned path (+0.33 / +0.66 / +1.0 s) go in, the matched frame, the stride warp and the
    /// foot contact tags come out; the gait player stays behind <c>-pixLocoLegacy</c>.
    /// </summary>
    public partial class SimAgentView
    {
        float _legScale = -1f, _gaitBias;
        /// <summary>QA: switch the whole gait system off (the "before" state: animator blend tree only).</summary>
        public static bool DebugNoGait;

        /// <summary>QA: -pixLocoOff on the command line plays the match with the v0.8 locomotion (animator blend tree, v0.4 foot lock) for before / after measurements. -pixLocoLegacy plays the v0.9.0 gait player instead of the motion matcher.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LocoSwitch()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-pixLocoOff") >= 0) { DebugNoGait = true; FootPlant.Enabled = false; ActionAnimator.DisableGait = true; DebugNoActionGround = true; }
            if (System.Array.IndexOf(args, "-pixLocoLegacy") >= 0) MotionMatcher.Wanted = false;
            if (System.Array.IndexOf(args, "-pixLocoNoActionGround") >= 0) DebugNoActionGround = true;
            if (System.Array.IndexOf(args, "-pixLocoNoPlant") >= 0) FootPlant.Enabled = false;
            if (System.Array.IndexOf(args, "-pixLocoNoPolish") >= 0) FootPlant.Polish = false;
            if (System.Array.IndexOf(args, "-pixLocoNoWarp") >= 0) FootPlant.DebugNoWarp = true;
            if (System.Array.IndexOf(args, "-pixLocoNoIK") >= 0) FootPlant.DebugNoIK = true;
            if (System.Array.IndexOf(args, "-pixLocoNoDonor") >= 0) GaitPlayer.DebugNoDonor = true;
            if (System.Array.IndexOf(args, "-pixMmNoInertia") >= 0) MmNoInertia = true;
            if (System.Array.IndexOf(args, "-pixMmNoOrientWarp") >= 0) MmNoOrientWarp = true;
            if (System.Array.IndexOf(args, "-pixMmNoPosture") >= 0) MotionMatcher.NoPosture = true;
            if (System.Array.IndexOf(args, "-pixMmNoTrans") >= 0) MotionMatcher.NoTransitions = true;
            if (System.Array.IndexOf(args, "-pixNoTurnRun") >= 0) NoTurnRun = true;
            if (System.Array.IndexOf(args, "-pixGaitOff") >= 0) GaitLean.Enabled = false;      // v0.12.0 GAIT: the v0.11 start / stop / cut (no lean difference, no hip sink)
            if (System.Array.IndexOf(args, "-pixGaitNoSink") >= 0) GaitLean.NoSink = true;
            { int gj = System.Array.IndexOf(args, "-pixFpHeelBias"); float gw; if (gj >= 0 && gj + 1 < args.Length && float.TryParse(args[gj + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out gw)) FootPlant.HeelBias = gw; }
            { int gi = System.Array.IndexOf(args, "-pixGaitHoldMax"); float gv; if (gi >= 0 && gi + 1 < args.Length && float.TryParse(args[gi + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out gv)) FootPlant.CutHoldMax = gv; }
            if (System.Array.IndexOf(args, "-pixGaitHold") >= 0) FootPlant.CutHoldOn = true;      // v0.12.0 GAIT: measured worse (cut audit 73 -> 68 %), off
            { int ti = System.Array.IndexOf(args, "-pixTurnRate"); float tv; if (ti >= 0 && ti + 1 < args.Length && float.TryParse(args[ti + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out tv)) LocoTurnRate = tv; }
            { int li = System.Array.IndexOf(args, "-pixHipsLeash"); float lv; if (li >= 0 && li + 1 < args.Length && float.TryParse(args[li + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lv)) HipsLeash = lv; }
            if (System.Array.IndexOf(args, "-pixMmNoPlan") >= 0) MmNoPlan = true;
            if (System.Array.IndexOf(args, "-pixMmLog") >= 0) MmLog = true;
            if (System.Array.IndexOf(args, "-pixMmLogQb") >= 0) MmLogQb = true;
            if (System.Array.IndexOf(args, "-pixMmNoLegsBack") >= 0) LegsToLocoOff = true;
            { int qi = System.Array.IndexOf(args, "-pixMmLogQbFrom"); int qv; if (qi >= 0 && qi + 2 < args.Length && int.TryParse(args[qi + 1], out qv)) { MmLogFrom = qv; int qt; if (int.TryParse(args[qi + 2], out qt)) MmLogTo = qt; } }
            if (System.Array.IndexOf(args, "-pixFpHeelFade") >= 0) FootPlant.HeelFade = true;
            if (System.Array.IndexOf(args, "-pixFpNoFadeRel") >= 0) FootPlant.FadeRel = false;
            if (System.Array.IndexOf(args, "-pixFpNoPivot") >= 0) FootPlant.Pivot = false;
            if (System.Array.IndexOf(args, "-pixFpRise") >= 0) FootPlant.RiseOn = true;
            if (System.Array.IndexOf(args, "-pixFpNoYawHold") >= 0) FootPlant.YawHoldOn = false;
            if (System.Array.IndexOf(args, "-pixFpNoMmLift") >= 0) FootPlant.MmLift = false;
            { int hi = System.Array.IndexOf(args, "-pixFpHeelShare"); float hv; if (hi >= 0 && hi + 1 < args.Length && float.TryParse(args[hi + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out hv)) FootPlant.HeelShare = hv; }
            { int bi = System.Array.IndexOf(args, "-pixFpBallRel"); float bv; if (bi >= 0 && bi + 1 < args.Length && float.TryParse(args[bi + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out bv)) FootPlant.BallReleaseH = bv; }
            { int pi2 = System.Array.IndexOf(args, "-pixFpPeak"); float pv; if (pi2 >= 0 && pi2 + 1 < args.Length && float.TryParse(args[pi2 + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pv)) FootPlant.PeakFlatten = pv; }
            { int ci2 = System.Array.IndexOf(args, "-pixMmLead"); float cv; if (ci2 >= 0 && ci2 + 1 < args.Length && float.TryParse(args[ci2 + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out cv)) MotionMatcher.ContactLead = cv; }
            { int fi = System.Array.IndexOf(args, "-pixFpFlightDrop"); float fv; if (fi >= 0 && fi + 1 < args.Length && float.TryParse(args[fi + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out fv)) FootPlant.FlightDrop = fv; }
            { int ei = System.Array.IndexOf(args, "-pixFpEnter"); float ev; if (ei >= 0 && ei + 1 < args.Length && float.TryParse(args[ei + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ev)) FootPlant.EnterHinted = ev; }
        }
        public static bool MmNoInertia, MmNoOrientWarp, MmNoPlan, MmLog, MmLogQb, LegsToLocoOff; static int MmLogFrom = -1, MmLogTo = -1;
        /// <summary>v0.11.0: per-body state of the Final IK bridge (see PixIk); opaque to everyone else.</summary>
        public object PixIkState;
        public float GaitBias { get { return _gaitBias; } }
        public GaitPlayer Gait { get { return Rig != null ? Rig.Gait : null; } }
        /// <summary>The motion matcher of this player (null with -pixLocoLegacy or without the database).</summary>
        public MotionMatcher Mm { get { return Rig != null ? Rig.Mm : null; } }
        /// <summary>The legs belong to the locomotion system (matcher or gait player) this frame.</summary>
        public bool LocoOwns
        {
            get
            {
                if (DebugNoGait) return false;
                var m = Mm; if (m != null && m.Ready) return m.Layer > 0.5f;
                return Gait != null && Gait.Ready && Gait.Layer > 0.5f;
            }
        }
        /// <summary>QA: the speed scale of this body (library native speeds are multiplied by it).</summary>
        public float GaitLegScale { get { if (_legScale < 0f) _legScale = MeasureLegScale(); return _legScale; } }

        static bool GaitAction(AgentAction a)
        {
            switch (a)
            {
                case AgentAction.Run: case AgentAction.Sprint: case AgentAction.Carry: case AgentAction.Route: case AgentAction.Idle: case AgentAction.Backpedal: case AgentAction.Shuffle:
                case AgentAction.Juke: case AgentAction.StiffArm: case AgentAction.Truck: case AgentAction.Hurdle: case AgentAction.Dive: return true;
                default: return false;
            }
        }

        float MeasureLegScale()
        {
            var lib = LocoCycleLibrary.Load();
            var a = Avatar != null ? Avatar.Animator : null;
            if (a == null || !a.isHuman) return 1f;
            Transform u = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg), l = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg), f = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (u == null || l == null || f == null) return 1f;
            float len = Vector3.Distance(u.position, l.position) + Vector3.Distance(l.position, f.position);
            int si = Mathf.Clamp((int)Size, 0, 2);
            if (lib == null)
            {
                var db = MotionDb.Load();
                return Mathf.Clamp(len > 0.2f && db != null ? len / db.refLeg : 1f, 0.6f, 1.6f);
            }
            // the speed scale of this body size was measured on the same takes (build time); a body variant with longer or shorter legs than the reference of its size scales with them
            float sz = lib.sizeScale != null && lib.sizeScale.Length == 3 ? lib.sizeScale[si] : 1f;
            float refLeg = lib.legLength != null && lib.legLength.Length == 3 && lib.legLength[si] > 0.2f ? lib.legLength[si] : lib.refLegLength;
            return Mathf.Clamp(sz * (len > 0.2f ? len / refLeg : 1f), 0.6f, 1.6f);
        }

        void DriveGait(Vector3 velocity, Vector3 facing, AgentAction action, bool carrying, float dt)
        {
            var mm = Mm;
            if (mm != null && mm.Ready && !DebugNoGait) { DriveMotion(mm, velocity, facing, action, carrying, dt); return; }
            var g = Gait;
            var plant = Motion != null ? Motion.Plant : null;
            if (plant != null) plant.GroundY = transform.position.y;
            if (Motion != null) Motion.ArmSwingEnabled = !carrying && g != null && g.Ready && !DebugNoGait;
            if (g == null || !g.Ready || DebugNoGait)
            {
                if (plant != null) { plant.Warp = 1f; plant.HintLeft = plant.HintRight = -1; plant.Speed = new Vector2(velocity.x, velocity.z).magnitude; plant.TravelDir = new Vector2(velocity.x, velocity.z); }
                _gaitBias = Mathf.MoveTowards(_gaitBias, 0f, 120f * dt);
                return;
            }
            if (_legScale < 0f) _legScale = MeasureLegScale();
            Vector3 fwd = new Vector3(facing.x, 0f, facing.z); fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var local = new Vector2(Vector3.Dot(velocity, right), Vector3.Dot(velocity, fwd));
            bool owned = !ContactDriven && !(Ragdoll != null && Ragdoll.Active) && Rig.Weight < 0.9f && GaitAction(action);
            g.SizeKey = (int)Size;
            if (plant != null) g.Feedback(plant, new Vector2(velocity.x, velocity.z).magnitude, dt);
            g.Update(dt, local, carrying, _legScale, owned);
            if (plant != null) g.Learn(plant, new Vector2(velocity.x, velocity.z), new Vector2(velocity.x, velocity.z).magnitude, dt);
            _gaitBias = Mathf.Lerp(_gaitBias, g.Layer > 0.3f ? g.YawBias : 0f, 1f - Mathf.Exp(-dt / 0.08f));
            if (plant != null)
            {
                Vector2 vh = new Vector2(velocity.x, velocity.z);
                plant.Speed = vh.magnitude; plant.TravelDir = vh;
                bool hinted = g.Layer > 0.35f;
                plant.Warp = g.Layer > 0.05f ? g.Warp : 1f;
                plant.HintLeft = hinted ? (g.LeftContact ? 1 : 0) : -1;
                plant.HintRight = hinted ? (g.RightContact ? 1 : 0) : -1;
                plant.GaitLayer = g.Layer;
            }
        }

        // ------------------------------------------------------------------------------------------ v0.10.0 motion matching
        Vector3 _plan1, _plan2, _plan3; bool _hasPlan; float _planAge = 9f;
        Inertializer _inert; Transform _mmHips, _mmSpine, _mmRU, _mmLU;
        readonly float[] _live = new float[10]; Vector3 _livePrevL, _livePrevR; bool _liveHave;

        /// <summary>The simulation's planned path: offsets (Unity world, metres) of the agent's position at +0.33 / +0.66 / +1.0 s from where it is now. Set by the stage every frame.</summary>
        public void SetMotionPlan(Vector3 d1, Vector3 d2, Vector3 d3) { _plan1 = d1; _plan2 = d2; _plan3 = d3; _hasPlan = true; _planAge = 0f; }

        /// <summary>CHOREO: an action takes the legs. Returns the foot phase the locomotion left (which foot is planted, where in the stride). Idempotent.</summary>
        public MotionMatcher.FootPhase LocoRelease() { var m = Mm; _locoHold = true; if (m == null || !m.Ready) return default(MotionMatcher.FootPhase); m.Suspend(); return m.Exit; }
        bool _locoHold;
        /// <summary>CHOREO: the action is over, the locomotion takes the legs back (it searches from the live pose and the planned path).</summary>
        public void LocoTakeBack() { _locoHold = false; var m = Mm; if (m != null && m.Ready) m.Resume(); }

        void DriveMotion(MotionMatcher m, Vector3 velocity, Vector3 facing, AgentAction action, bool carrying, float dt)
        {
            var plant = Motion != null ? Motion.Plant : null;
            if (plant != null) plant.GroundY = transform.position.y;
            _natCarry = carrying;      // v0.12.0 NATURAL
            if (Motion != null) Motion.ArmSwingEnabled = false;       // the matched takes bring their own arms (and the ball carrier's come from the animator tree under the lower-body mask)
            if (_legScale < 0f) _legScale = MeasureLegScale();
            if (_inert == null && Avatar != null && Avatar.Animator != null) { _inert = new Inertializer(Avatar.Animator, transform); }
            m.Inertial = MmNoInertia ? null : _inert;
            Vector3 fwd = new Vector3(facing.x, 0f, facing.z); fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var local = new Vector2(Vector3.Dot(velocity, right), Vector3.Dot(velocity, fwd));
            bool engLoco = ChoreoLocoLegs(action, velocity);
            bool owned = !ContactDriven && !(Ragdoll != null && Ragdoll.Active) && (Rig.Weight < 0.9f || engLoco) && (GaitAction(action) || engLoco) && !_locoHold;      // v0.12.0 CONTACT: ChoreoLocoLegs = an engaged body that moves on (SimAgentView.Choreo.cs)
            if (Motion != null) Motion.GaitScale = owned && Role != PlayerRole.QB ? 1f : 0f;      // v0.12.0 GAIT: start / stop lean for everyone the matcher runs, not the quarterback (authored carriage)
            if (!owned) { if (!m.Suspended) m.Suspend(); }
            else if (m.Suspended) m.Resume();
            m.SizeKey = (int)Size;
            var q = new MotionMatcher.Query { Vel = local, LegScale = _legScale, Enable = owned, Carry = carrying };
            _planAge += dt;
            Vector3 d1, d2, d3;
            if (_hasPlan && _planAge < 0.25f && !MmNoPlan) { d1 = _plan1; d2 = _plan2; d3 = _plan3; }
            else { Vector3 v = new Vector3(velocity.x, 0f, velocity.z); d1 = v * 0.33f * 0.92f; d2 = v * 0.66f * 0.88f; d3 = v * 1.0f * 0.85f; }
            q.P1 = new Vector2(Vector3.Dot(d1, right), Vector3.Dot(d1, fwd)); q.P2 = new Vector2(Vector3.Dot(d2, right), Vector3.Dot(d2, fwd)); q.P3 = new Vector2(Vector3.Dot(d3, right), Vector3.Dot(d3, fwd));
            // a runner who faces his travel turns with his path; a defender who shuffles or backpedals keeps his facing
            if (local.y > 1.2f && Mathf.Abs(local.x) < 0.5f * local.y)
            {
                float a1 = Mathf.Atan2(q.P1.x, q.P1.y) * Mathf.Rad2Deg;
                float a2 = Mathf.Atan2(q.P2.x - q.P1.x, q.P2.y - q.P1.y) * Mathf.Rad2Deg, a3 = Mathf.Atan2(q.P3.x - q.P2.x, q.P3.y - q.P2.y) * Mathf.Rad2Deg;
                q.Yaw1 = Mathf.Clamp(a1, -90f, 90f); q.Yaw2 = Mathf.Clamp(a2, -120f, 120f); q.Yaw3 = Mathf.Clamp(a3, -150f, 150f);
            }
            if (m.Suspended == false && owned && m.Row < 0) q.LiveFeet = LiveFeet(fwd);
            m.StyleRate = NaturalTempo();      // v0.12.0 NATURAL
            bool on = m.Update(dt, q);
            if (Motion != null) { bool legs = on && Rig != null && Rig.Weight > 0.01f && Rig.Weight < 0.98f; if (legs && !LegsToLocoOff && Rig.LegsToLoco()) m.SourceChanged(); Motion.ForceLoco = legs && !LegsToLocoOff; }
            if (plant != null) m.Learn(plant, new Vector2(velocity.x, velocity.z).magnitude, dt);
            if ((MmLog && (AgentId == 18 || AgentId == 3) && Time.frameCount % 20 == 0) || (MmLogQb && AgentId == 0 && (MmLogFrom < 0 ? Time.frameCount % 3 == 0 : (Time.frameCount >= MmLogFrom && Time.frameCount <= MmLogTo)))) Debug.Log("[MM] f " + Time.frameCount + " agent " + AgentId + " " + action + " v " + local.magnitude.ToString("0.00") + " owned " + owned + " susp " + m.Suspended + " on " + on + " layer " + m.Layer.ToString("0.00") + " clip " + m.ClipName + " row " + m.Row + " rigW " + (Rig != null ? Rig.Weight : -1f) + " contact " + ContactDriven + " plan " + _hasPlan + (plant != null ? " pl " + (plant.PlantedLeft ? 1 : 0) + (plant.PlantedRight ? 1 : 0) + " W " + plant.WeightLeft.ToString("0.00") + "/" + plant.WeightRight.ToString("0.00") + " eng " + plant.Engaged + " hint " + plant.HintLeft + plant.HintRight + " ow " + m.OrientationWarp.ToString("0") + " liftL " + plant.LiftLeft.ToString("0.000") + " pressL " + plant.PressLeft.ToString("0.000") + " residL " + plant.ResidLeft.ToString("0.000") + " hipDrop " + plant.HipDrop.ToString("0.000") : ""));
            _gaitBias = Mathf.MoveTowards(_gaitBias, 0f, 240f * dt);
            if (plant != null)
            {
                Vector2 vh = new Vector2(velocity.x, velocity.z);
                plant.Speed = vh.magnitude; plant.TravelDir = vh;
                bool hinted = on && m.Layer > 0.35f;
                plant.Warp = on ? m.Warp : 1f;
                plant.HintLeft = hinted ? (m.LeftContact ? 1 : 0) : -1;
                plant.HintRight = hinted ? (m.RightContact ? 1 : 0) : -1;
                plant.GaitLayer = on ? m.Layer : 0f; plant.HintAuthority = on; plant.BodyLine = on && m.FeetOffTravel; plant.BallFirst = on && m.BackTravel;
            }
        }

        /// <summary>Foot features of the live pose (normalised, weighted; dims 0..9) for the first search after an action: ankle position, height and velocity in the body frame.</summary>
        float[] LiveFeet(Vector3 fwd)
        {
            var db = MotionDb.Load(); var a = Avatar != null ? Avatar.Animator : null; var plant = Motion != null ? Motion.Plant : null;
            if (db == null || a == null || !a.isHuman || plant == null || !plant.Ok) return null;
            Transform lf = a.GetBoneTransform(HumanBodyBones.LeftFoot), rf = a.GetBoneTransform(HumanBodyBones.RightFoot), lt = a.GetBoneTransform(HumanBodyBones.LeftToes), rt = a.GetBoneTransform(HumanBodyBones.RightToes);
            if (lf == null || rf == null) return null;
            Vector3 right = Vector3.Cross(Vector3.up, fwd), org = transform.position; float gy = org.y, inv = 1f / Mathf.Max(0.5f, _legScale);
            Vector3 L = lf.position - org, R = rf.position - org;
            float hl = Mathf.Min(lf.position.y - gy - plant.RestAnkleL, lt != null ? lt.position.y - gy - plant.RestToeL : 9f), hr = Mathf.Min(rf.position.y - gy - plant.RestAnkleR, rt != null ? rt.position.y - gy - plant.RestToeR : 9f);
            Vector3 vl = _liveHave ? (L - _livePrevL) / Mathf.Max(1e-3f, Time.deltaTime) : Vector3.zero, vr = _liveHave ? (R - _livePrevR) / Mathf.Max(1e-3f, Time.deltaTime) : Vector3.zero;
            _livePrevL = L; _livePrevR = R; _liveHave = true;
            var raw = _liveRaw;
            raw[0] = Vector3.Dot(L, right) * inv; raw[1] = Vector3.Dot(L, fwd) * inv; raw[2] = Vector3.Dot(R, right) * inv; raw[3] = Vector3.Dot(R, fwd) * inv; raw[4] = hl * inv; raw[5] = hr * inv;
            raw[6] = Vector3.Dot(vl, right) * inv; raw[7] = Vector3.Dot(vl, fwd) * inv; raw[8] = Vector3.Dot(vr, right) * inv; raw[9] = Vector3.Dot(vr, fwd) * inv;
            for (int i = 0; i < 10; i++) _live[i] = (raw[i] - db.mean[i]) * db.scale[i];
            return _live;
        }
        readonly float[] _liveRaw = new float[10];

        Transform _dbgChest; Quaternion _dbgA, _dbgB; bool _dbgHave; bool _mmWasOwning;
        void ChestDebug(float dt, bool afterStage, float ow, float own)
        {
            if (!MmLog || AgentId != 18 || Avatar == null || Avatar.Animator == null) return;
            if (_dbgChest == null) _dbgChest = Avatar.Animator.GetBoneTransform(HumanBodyBones.Chest);
            if (_dbgChest == null) return;
            var q = _dbgChest.rotation;
            if (!afterStage)
            {
                float da = _dbgHave ? Quaternion.Angle(_dbgA, q) / Mathf.Max(dt, 1e-4f) : 0f, db = _dbgHave ? Quaternion.Angle(_dbgB, q) / Mathf.Max(dt, 1e-4f) : 0f;
                if (da > 1200f || db > 1200f) Debug.Log("[MMCHEST] t " + Time.time.ToString("0.000") + " rawAnim->anim " + da.ToString("0") + " lastOut->anim " + db.ToString("0") + " clip " + (Mm != null ? Mm.ClipName + "@" + Mm.Row : "-") + " ow " + ow.ToString("0.0") + " yaw " + _yaw.ToString("0") + " loopYaw " + _loopYaw.ToString("0") + " action " + _lastAction);
                _dbgA = q; _dbgHave = true;
            }
            else _dbgB = q;
        }

        /// <summary>LateUpdate, first: inertialization of the matcher's jumps and the orientation warp of the legs (before the facing loop, the pop filter and the foot planter see the pose).</summary>
        void MotionLate(float dt)
        {
            var m = Mm;
            if (m == null || !m.Ready || DebugNoGait || Avatar == null || Avatar.Animator == null || !Avatar.Animator.isHuman) return;
            if (_mmHips == null) { _mmHips = Avatar.Animator.GetBoneTransform(HumanBodyBones.Hips); _mmSpine = Avatar.Animator.GetBoneTransform(HumanBodyBones.Spine); }
            if (MmNoOrientWarp) m.NoOrientWarp = true;
            // the takes face the avatar's forward, the body model is turned by ModelYaw: while the matcher owns the pose the hips are turned back (the share of the pose that comes from the matcher, not from an action clip)
            float own = m.Layer > 0.5f ? 1f - Mathf.Clamp01(Rig != null ? Rig.Weight : 0f) : 0f;
            bool owning = m.Layer > 0.5f;
            if (owning && !_mmWasOwning) { _loopYaw = 0f; _loopHeld = 0f; Avatar.SetVisualYaw(_tableYaw); }      // the facing loop chased the old pose: the matcher plays facing the simulated heading, the body model is turned at once and the inertialized pelvis carries the swing
            _mmWasOwning = owning;
            ChestDebug(dt, false, m.OrientationWarp, own);
            if (_mmRU == null) { _mmRU = Avatar.Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg); _mmLU = Avatar.Animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg); }
            Vector3 lat = _mmRU != null && _mmLU != null ? _mmRU.position - _mmLU.position : Vector3.zero; lat.y = 0f;
            float ls = Mathf.Max(0.5f, _legScale);
            m.Late(dt, _mmHips, _mmSpine, Avatar.Animator.transform, (m.LegacyConv ? 0f : -PlayerAvatar.ModelYaw * own), (m.Floor + m.PostureW * m.PostureDropW * MotionMatcher.PostureDrop) * ls * own, lat);
            ChestDebug(dt, true, m.OrientationWarp, own);
        }

        /// <summary>Owns the body heading while the matcher plays: the facing loop (which chases the pose heading) stands down.</summary>
        bool MmOwnsFacing { get { var m = Mm; return m != null && m.Ready && !DebugNoGait && m.Layer > 0.5f; } }


        // ------------------------------------------------------------------------------------------ v0.11.0 B-018 turn and run
        float _turnRun; bool _turnRunOn; public static bool NoTurnRun;
        /// <summary>Degrees per second the visible body heading may chase the simulated facing (was 720).</summary>
        public static float LocoTurnRate = 720f;
        /// <summary>Nobody runs backwards at 7 m/s: when the simulation faces a body more than 100 degrees away from the way it travels at over 4.5 m/s (kickoff and punt coverage runs the lane with the facing left on the ball, a defender chases a receiver
        /// that passed him) the body turns to face its travel over a quarter of a second and runs; it keeps the simulated facing again once the speed falls below 3.5 m/s or the two agree within 60 degrees.</summary>
        Vector3 LocoFacing(Vector3 velocity, Vector3 facing, AgentAction action, float dt)
        {
            if (NoTurnRun) return facing;
            Vector3 v = new Vector3(velocity.x, 0f, velocity.z); float sp = v.magnitude;
            Vector3 f = new Vector3(facing.x, 0f, facing.z);
            float ang = f.sqrMagnitude > 1e-4f && sp > 0.5f ? Vector3.Angle(f, v) : 0f;
            bool actionOk = action != AgentAction.Engaged && action != AgentAction.Block && action != AgentAction.Tackled && action != AgentAction.Fallen && action != AgentAction.Dive;
            if (!_turnRunOn && actionOk && sp > 4.5f && ang > 100f) _turnRunOn = true;
            else if (_turnRunOn && (!actionOk || sp < 3.5f || ang < 60f)) _turnRunOn = false;
            _turnRun = Mathf.MoveTowards(_turnRun, _turnRunOn ? 1f : 0f, dt / 0.25f);
            if (_turnRun < 0.005f) return facing;
            float fy = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg, vy = Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
            float y = fy + Mathf.DeltaAngle(fy, vy) * Mathf.SmoothStep(0f, 1f, _turnRun);
            return new Vector3(Mathf.Sin(y * Mathf.Deg2Rad), 0f, Mathf.Cos(y * Mathf.Deg2Rad));
        }

        // ------------------------------------------------------------------------------------------ v0.11.0 B-020 hips leash
        public static float HipsLeash = 0.30f; Transform _leashHips;
        /// <summary>The skeleton of a body is never drawn further than <see cref="HipsLeash"/> from the point the simulation (and its collider) has him: block, drive and engage takes carry root offsets of up to 1.5 m that the sim does not know about.
        /// The excess is taken off the whole body (the hips and everything under them), on the final pose, before Final IK.</summary>
        void HipsLeashLate(float dt)
        {
            if (HipsLeash <= 0f || Avatar == null || Avatar.Animator == null || !Avatar.Animator.isHuman || (Ragdoll != null && Ragdoll.Active)) return;
            if (_leashHips == null) _leashHips = Avatar.Animator.GetBoneTransform(HumanBodyBones.Hips);
            if (_leashHips == null) return;
            Vector3 off = _leashHips.position - transform.position; off.y = 0f;
            float d = off.magnitude; if (d <= HipsLeash) return;
            _leashHips.position -= off / d * (d - HipsLeash);
        }
        void ResetGait()
        {
            _gaitBias = 0f; _actOff = 0f; _hasPlan = false; _liveHave = false;
            var g = Gait; if (g != null && g.Ready) g.Reset();
            var m = Mm; if (m != null && m.Ready) m.Reset();
            if (_inert != null) _inert.Reset();
        }
    }
}
