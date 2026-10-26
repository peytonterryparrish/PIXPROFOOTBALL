using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace PixFootball.EditorTools
{
    public sealed partial class MatchWatchRunner
    {
        // ---------------------------------------------------------------------------------------------------------- stats helper
        sealed class Stat
        {
            public readonly List<float> V = new List<float>();
            public void Add(float x) { if (V.Count < 400000) V.Add(x); }
            public string Describe()
            {
                if (V.Count == 0) return "n=0";
                var l = V.OrderBy(x => x).ToList();
                return "n=" + l.Count + " mean " + (l.Sum() / l.Count).ToString("0.###", CI) + " p5 " + l[(int)(l.Count * 0.05)].ToString("0.###", CI) + " p50 " + l[l.Count / 2].ToString("0.###", CI) + " p95 " + l[Math.Min(l.Count - 1, (int)(l.Count * 0.95))].ToString("0.###", CI) + " max " + l[l.Count - 1].ToString("0.###", CI);
            }
        }
        readonly SortedDictionary<string, Stat> _stats = new SortedDictionary<string, Stat>();
        void S(string k, float v) { Stat s; if (!_stats.TryGetValue(k, out s)) _stats[k] = s = new Stat(); s.Add(v); }
        readonly SortedDictionary<string, int> _hist = new SortedDictionary<string, int>();
        void H(string k, int n = 1) { int c; _hist.TryGetValue(k, out c); _hist[k] = c + n; }

        static string Grp(Position p)
        {
            if (PositionGroup.IsOffensiveLine(p)) return "OL";
            if (PositionGroup.IsDefensiveLine(p)) return "DL";
            if (PositionGroup.IsLinebacker(p)) return "LB";
            if (p == Position.CB) return "CB";
            if (p == Position.FS || p == Position.SS) return "S";
            return p.ToString();
        }

        // ---------------------------------------------------------------------------------------------------------- per view tracking
        sealed class VT
        {
            public SimAgentView V; public Animator An;
            public Transform Hips, Spine, Chest, Head, LF, RF, LT, RT, LH, RH, LUL, RUL, LTE, RTE;
            public Renderer[] Rs; public SkinnedMeshRenderer[] Smrs; public PhotoPlayer Pp; public int RsPlay = -1; public int RsChildCount;
            public Vector3 PrevLT, PrevRT, PrevLF, PrevRF, PrevHipsLocal; public Quaternion PrevHipsRot, PrevChestRot; public bool Have;
            public bool PlantL, PlantR;
            public float PrevHoc, PrevViewYaw; public bool HocHave;      // v0.13.0 STANCE-HEAD: head over chest of the last frame (turn rate)
            public int FloatRun, AirRun, SunkRun, LagRun, FaceRun, TravelRun, BackRun, NoDrawRun, BoundsRun, OffHandRun, LvlChanges, PrevLvl = -1, LvlPrevChangeFrame = -99, MissRun;
            public string PrevClip = ""; public int ClipSwitches; public float Alive; public int LastClipSwitchFrame;
            public int LoopHips; public bool WasStale; public int StaleFrames; public int BuriedRun;
            public Vector3 PrevRoot; public bool HaveRoot; public float ViewSpeed; public int FloatRunOld, LagRunOld, FaceRunOld, FaceRunV012, HudRun;   // v0.12: horizontal speed of the body on the stage (the pre-snap glide moves bodies that the simulation still lists as Stance at 0 m/s)
        }
        readonly Dictionary<SimAgentView, VT> _vt = new Dictionary<SimAgentView, VT>();
        readonly Vector3[] _hudC = new Vector3[2]; readonly int[] _hudN = new int[2];

        VT GetVT(SimAgentView v)
        {
            VT t;
            if (_vt.TryGetValue(v, out t) && t.Hips != null) return t;
            if (v.Avatar == null || v.Avatar.Animator == null) return null;
            var an = v.Avatar.Animator; if (!an.isHuman) return null;
            t = new VT { V = v, An = an };
            t.Hips = an.GetBoneTransform(HumanBodyBones.Hips); t.Spine = an.GetBoneTransform(HumanBodyBones.Spine); t.Chest = an.GetBoneTransform(HumanBodyBones.Chest) ?? an.GetBoneTransform(HumanBodyBones.UpperChest);
            t.Head = an.GetBoneTransform(HumanBodyBones.Head);
            t.LF = an.GetBoneTransform(HumanBodyBones.LeftFoot); t.RF = an.GetBoneTransform(HumanBodyBones.RightFoot);
            t.LT = an.GetBoneTransform(HumanBodyBones.LeftToes); t.RT = an.GetBoneTransform(HumanBodyBones.RightToes);
            t.LTE = t.LT != null && t.LT.childCount > 0 ? t.LT.GetChild(0) : null; t.RTE = t.RT != null && t.RT.childCount > 0 ? t.RT.GetChild(0) : null;
            t.LH = an.GetBoneTransform(HumanBodyBones.LeftHand); t.RH = an.GetBoneTransform(HumanBodyBones.RightHand);
            t.LUL = an.GetBoneTransform(HumanBodyBones.LeftUpperLeg); t.RUL = an.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (t.Hips == null || t.LF == null || t.RF == null) return null;
            _vt[v] = t;
            return t;
        }

        void ResetVTs() { foreach (var t in _vt.Values) { t.Have = false; t.HocHave = false; t.FaceRunV012 = t.HudRun = 0; t.FloatRun = t.AirRun = t.SunkRun = t.LagRun = t.LagRunOld = t.FaceRun = t.FaceRunOld = t.TravelRun = t.BackRun = t.NoDrawRun = t.BoundsRun = t.OffHandRun = t.MissRun = 0; t.RsPlay = -1; t.ClipSwitches = 0; t.Alive = 0f; t.PlantL = t.PlantR = false; } Array.Clear(_overlapRun, 0, _overlapRun.Length); Array.Clear(_overlapRunOld, 0, _overlapRunOld.Length); }

        static bool Drawn(Renderer r, bool allowShadowOnly = false)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return false;
            if (!allowShadowOnly && r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) return false;
            var smr = r as SkinnedMeshRenderer; if (smr != null) return smr.sharedMesh != null;
            var mf = r.GetComponent<MeshFilter>(); return mf != null && mf.sharedMesh != null;
        }

        // ---------------------------------------------------------------------------------------------------------- hooks
        SimStateSnap _prevSimPhase; PlayPhase _lastSimPhaseSeen = PlayPhase.Dead;
        struct SimStateSnap { public PlayPhase Phase; public PlayKind Kind; }
        readonly int[,] _overlapRunDummy = new int[1, 1];
        int[] _overlapRun = new int[22 * 22], _overlapRunOld = new int[22 * 22];      // (v0.12: _overlapRunOld = the v0.11 rule, any body speed; _overlapRun = both bodies still on the stage)
        int _snapAtPlay = -1;

        void OnPlayStarted(PlaySimulation sim)
        {
            ResetVTs();
            _prevSimPhase = new SimStateSnap { Phase = PlayPhase.PreSnap, Kind = sim.Kind };
            _ballOffRun = 0; _ballDesyncRun = 0; _camOff = 0; _carrierOff = 0;
            _flightPlay.Clear();
            _simStartFrame = _frame; _preFrames = 0;
            if (_playCount.ContainsKey(sim.Kind)) _playCount[sim.Kind]++; else _playCount[sim.Kind] = 1;
            RequestStripsOnPlayStart(sim);
        }
        int _simStartFrame; int _preFrames;
        readonly Dictionary<PlayKind, int> _playCount = new Dictionary<PlayKind, int>();
        readonly Dictionary<int, int> _flightPlay = new Dictionary<int, int>();

        void OnPhaseChanged(MatchPhase p) { RequestStripsOnPhase(p); }

        // ---------------------------------------------------------------------------------------------------------- stance sampling
        struct StanceRow { public bool Ok; public string Pos, Kind; public float HipsY, HeadY, HandL, HandR, LowToe, Lean, YawErr, Stagger, Width, HeadBelowHips, PoseW, SecSet; public bool Mirror; public float SimX, SimY; public string Group; public string Stance; }
        readonly StanceRow[] _stanceNow = new StanceRow[22];
        readonly int[] _stSince = Enumerable.Repeat(-1, 22).ToArray();
        bool _stanceFresh;
        readonly StringBuilder _stanceCsv = new StringBuilder("play,kind,agent,pos,group,stance,hipsY,headY,handL_y,handR_y,lowToeY,trunkLeanDeg,yawErrDeg,staggerM,widthM,headMinusHips,simRelX,simRelY,poseW,secSet\n");
        int _stanceFrameMod;

        void SampleStance(PlaySimulation sim, PlayStage st)
        {
            var frame = st.Frame;
            for (int i = 0; i < 22; i++)
            {
                var v = st.Views[i]; var a = sim.Agents[i]; _stanceNow[i].Ok = false;
                if (v == null) continue; var t = GetVT(v); if (t == null || t.Head == null) continue;
                float gy = v.transform.position.y;
                var r = new StanceRow { Ok = true, Pos = a.PlayPos.ToString(), Group = Grp(a.PlayPos), Stance = v.Stance.Kind.ToString() };
                r.HipsY = t.Hips.position.y - gy; r.HeadY = t.Head.position.y - gy;
                r.HandL = t.LH != null ? t.LH.position.y - gy : 9f; r.HandR = t.RH != null ? t.RH.position.y - gy : 9f;
                float lt = (t.LT != null ? t.LT.position.y : t.LF.position.y - 0.09f) - gy, rt = (t.RT != null ? t.RT.position.y : t.RF.position.y - 0.09f) - gy;
                r.LowToe = Mathf.Min(lt, rt);
                Vector3 up = t.Head.position - t.Hips.position; r.Lean = Vector3.Angle(up, Vector3.up); r.HeadBelowHips = r.HeadY - r.HipsY;
                var pose = BodyHeading.Measure(t.An);
                Vector3 fd = frame.DirToUnity(a.Facing);
                float simYaw = Mathf.Atan2(fd.x, fd.z) * Mathf.Rad2Deg;
                r.YawErr = pose.Valid ? Mathf.Abs(StanceRules.YawDelta(pose.Yaw, simYaw)) : 0f;
                Vector3 fwd = new Vector3(fd.x, 0f, fd.z).normalized;
                Vector3 lp = (t.LT != null ? t.LT.position : t.LF.position), rp = (t.RT != null ? t.RT.position : t.RF.position);
                r.Stagger = Vector3.Dot(rp - lp, fwd); r.Width = Mathf.Abs(Vector3.Dot(rp - lp, Vector3.Cross(Vector3.up, fwd)));
                r.SimX = a.StartPos.X - sim.BallX0; r.SimY = a.StartPos.Y - sim.LosY;
                { bool inSt = a.Action == AgentAction.Stance; if (!inSt) _stSince[i] = -1; else if (_stSince[i] < 0) _stSince[i] = _frame; r.SecSet = inSt ? (_frame - _stSince[i]) / 60f : 0f; r.PoseW = v.StanceLayer != null ? v.StanceLayer.Weight : -1f; }      // v0.13.0: how long the body has been in his stance, and the weight of the stance pose layer
                _stanceNow[i] = r;
            }
            _stanceFresh = true;
        }

        void CommitStance(PlaySimulation sim)
        {
            if (!_stanceFresh) return;
            for (int i = 0; i < 22; i++)
            {
                var r = _stanceNow[i]; if (!r.Ok) continue;
                _stanceCsv.AppendLine(string.Join(",", new[] { _dir.PlayNumber.ToString(), sim.Kind.ToString(), i.ToString(), r.Pos, r.Group, r.Stance, F(r.HipsY), F(r.HeadY), F(r.HandL), F(r.HandR), F(r.LowToe), F(r.Lean), F(r.YawErr), F(r.Stagger), F(r.Width), F(r.HeadBelowHips), F(r.SimX), F(r.SimY), F(r.PoseW), F(r.SecSet) }));
                string k = r.Group + "/" + r.Stance;
                S("stance hipsY " + k, r.HipsY); S("stance headY " + k, r.HeadY); S("stance trunkLean " + k, r.Lean); S("stance lowToe " + k, r.LowToe); S("stance yawErr " + k, r.YawErr);
                S("stance minHand " + k, Mathf.Min(r.HandL, r.HandR));
                string ctx = r.Pos + " " + r.Stance + " in " + sim.Kind + " hips " + F(r.HipsY) + " head " + F(r.HeadY) + " lean " + F(r.Lean) + " hands " + F(r.HandL) + "/" + F(r.HandR) + " yawErr " + F(r.YawErr);
                bool lineStance = r.Stance == "ThreePoint" || r.Stance == "FourPoint" || r.Stance == "CenterSnap";
                if (lineStance)
                {
                    if (Mathf.Min(r.HandL, r.HandR) > 0.18f) Issue("STANCE_LINEMAN_NO_HAND_ON_TURF_" + r.Group, ctx, Mathf.Min(r.HandL, r.HandR), i, true);
                    if (r.Stance == "FourPoint" && Mathf.Max(r.HandL, r.HandR) > 0.25f) Issue("STANCE_4PT_ONE_HAND_UP_" + r.Group, ctx, Mathf.Max(r.HandL, r.HandR), i);
                    if (r.Lean < 25f) Issue("STANCE_LINEMAN_TOO_UPRIGHT_" + r.Group, ctx, r.Lean, i, true);
                    if (r.HeadBelowHips < -0.05f) Issue("STANCE_LINEMAN_HIPS_ABOVE_HEAD_" + r.Group, ctx, r.HeadBelowHips, i);
                    if (r.HipsY > 1.0f) Issue("STANCE_LINEMAN_HIPS_TOO_HIGH_" + r.Group, ctx, r.HipsY, i);
                }
                else
                {
                    if (r.Lean > 38f) Issue("STANCE_UPRIGHT_TOO_BENT_" + r.Group, ctx, r.Lean, i, true);
                    if (r.HipsY < 0.62f && r.Stance != "Upright") Issue("STANCE_TWO_POINT_SQUAT_TOO_LOW_" + r.Group, ctx, r.HipsY, i);
                    if (r.HeadBelowHips < 0.25f) Issue("STANCE_UPRIGHT_HEAD_NOT_ABOVE_HIPS_" + r.Group, ctx, r.HeadBelowHips, i, true);
                }
                if (r.LowToe > 0.07f) Issue("STANCE_FEET_OFF_TURF_" + r.Group, ctx, r.LowToe, i, true);
                if (r.LowToe < -0.06f) Issue("STANCE_FEET_IN_TURF_" + r.Group, ctx, r.LowToe, i);
                if (r.YawErr > 40f) Issue("STANCE_FACING_OFF_SIM_" + r.Group, ctx, r.YawErr, i, true);
            }
            for (int i = 0; i < 22; i++) _stSince[i] = -1;      // v0.13.0: the next pre-snap starts counting again
            _stanceFresh = false;
        }
        static string F(float x) { return x.ToString("0.00", CI); }

        // ---------------------------------------------------------------------------------------------------------- the per-frame body of the audit
        Plane[] _planes = new Plane[6];
        Vector3 _prevCamPos; bool _haveCam; int _camOff, _carrierOff, _ballOffRun, _ballDesyncRun;
        float _prevClock;

        void PerFrameChecks()
        {
            var st = _dir.Stage; if (st == null) return;
            var sim = st.Sim; var cam = _dir.MainCamera;
            // camera continuity (every frame, also between plays)
            if (cam != null)
            {
                Vector3 cp = cam.transform.position;
                if (_haveCam)
                {
                    float dcam = Vector3.Distance(cp, _prevCamPos);
                    S("camera move m/frame", dcam);
                    if (dcam > 6f) { Issue("CAM_JUMP_OVER_6M_IN_ONE_FRAME", "phase " + _dir.Phase + " from " + _prevCamPos + " to " + cp, dcam); }
                }
                _prevCamPos = cp; _haveCam = true;
                if (cp.y < 0.8f) Issue("CAM_BELOW_0.8M", "camera y " + cp.y.ToString("0.00", CI) + " phase " + _dir.Phase, cp.y);
                S("camera fov", cam.fieldOfView); S("camera height m", cp.y);
            }
            if (sim == null || st.Views[0] == null && st.Views[11] == null) { if (_dir.Phase == MatchPhase.Live || _dir.Phase == MatchPhase.PreSnap) H("frames with Live/PreSnap but no sim on the stage"); return; }
            H("frames audited (" + _dir.Phase + ")");
            var frame = st.Frame;
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            bool live = sim.Phase == PlayPhase.Live;
            bool pre = sim.Phase == PlayPhase.PreSnap;
            // sim phase edge: stance snapshot at the moment the ball is snapped / kicked
            if (_lastSimPhaseSeen == PlayPhase.PreSnap && sim.Phase != PlayPhase.PreSnap) { CommitStance(sim); if (sim.Kind == PlayKind.Scrimmage) SideShotsForGroups("snap", sim, 6); }
            _lastSimPhaseSeen = sim.Phase;
            if (pre && (_frame % 6) == 0) SampleStance(sim, st);
            if (pre) { _preFrames++; if (_preFrames == 180 && sim.Kind == PlayKind.Scrimmage) SideShotsForGroups("early", sim, 6); }

            int[] teamNoBody = new int[2], teamInactive = new int[2], teamMissingParts = new int[2], teamOutOfFrustum = new int[2], teamOnField = new int[2], teamOffField = new int[2];
            Vector3[] hipP = new Vector3[22]; bool[] hipOk = new bool[22]; float[] hipSpd = new float[22];
            Vector3 ballU = st.Ball != null ? st.Ball.Position : Vector3.zero;
            // v0.13.0 STANCE-HEAD: where each team's huddle ring is (the mean of the bodies the view has in the huddle)
            _hudC[0] = _hudC[1] = Vector3.zero; _hudN[0] = _hudN[1] = 0;
            for (int hi = 0; hi < 22; hi++) { var hv = st.Views[hi]; if (hv != null && hv.Huddle && hv.gameObject.activeInHierarchy) { int ht = sim.Agents[hi].Side == SimSide.Offense ? 0 : 1; _hudC[ht] += hv.transform.position; _hudN[ht]++; } }
            for (int ht = 0; ht < 2; ht++) if (_hudN[ht] > 0) _hudC[ht] /= _hudN[ht];
            for (int i = 0; i < 22; i++)
            {
                var v = st.Views[i]; var a = sim.Agents[i]; int team = a.Side == SimSide.Offense ? 0 : 1;
                if (v == null) { Issue("VIS_VIEW_NULL", "agent " + i + " " + a.PlayPos, 0, i); teamInactive[team]++; continue; }
                if (!v.gameObject.activeInHierarchy) { Issue("VIS_VIEW_INACTIVE_" + sim.Kind, "agent " + i + " " + a.PlayPos + " " + a.Side, 0, i); teamInactive[team]++; continue; }
                var t = GetVT(v); if (t == null) { Issue("VIS_NO_ANIMATOR_OR_BONES", "agent " + i, 0, i); continue; }
                string g = Grp(a.PlayPos);
                float gy = v.transform.position.y;
                Vector3 vp = v.transform.position;
                if (float.IsNaN(vp.x) || float.IsNaN(vp.y) || float.IsNaN(vp.z)) { Issue("VIS_POSITION_NAN", "agent " + i, 0, i); continue; }
                { float inst = t.HaveRoot ? new Vector2(vp.x - t.PrevRoot.x, vp.z - t.PrevRoot.z).magnitude * 60f : 0f; t.ViewSpeed = t.HaveRoot ? Mathf.Lerp(t.ViewSpeed, inst, 0.25f) : 0f; t.PrevRoot = vp; t.HaveRoot = true; }
                bool contact = v.ContactDriven;
                // ---- off-screen bodies: v0.10.0 sets AnimatorCullingMode.CullUpdateTransforms, so the pose is frozen while procedural layers keep moving the hips (the body sinks); measure that separately and keep it out of every other check
                {
                    bool bigF = GeometryUtility.TestPlanesAABB(_planes, new Bounds(vp + Vector3.up * 0.95f, new Vector3(3.5f, 3.5f, 3.5f)));
                    bool stale = t.An.cullingMode != AnimatorCullingMode.AlwaysAnimate && !bigF;
                    float hyRel = t.Hips.position.y - vp.y;
                    if (stale)
                    {
                        t.WasStale = true; t.StaleFrames++; t.Have = false; H("frames: body outside the view with a culled animator (" + t.An.cullingMode + ")");
                        S("off-screen culled body hips height m", hyRel);
                        if (hyRel < -0.3f) { H("frames: culled off-screen body hips more than 30 cm below the turf"); if (_frame % 30 == 0) Issue("CULL_OFFSCREEN_BODY_SINKS_UNDER_TURF_" + g, "agent " + i + " " + a.PlayPos + " act " + a.Action + " simPhase " + sim.Phase + " kind " + sim.Kind + " hips " + hyRel.ToString("0.00", CI) + " m, stale for " + (t.StaleFrames / 60f).ToString("0.0", CI) + " s", hyRel, i, true); }
                        if (hyRel < -0.3f) teamNoBody[team] += 0;
                        continue;
                    }
                    if (t.WasStale) { t.WasStale = false; t.StaleFrames = 0; S("hips height on the first frame back in view m", hyRel); if (hyRel < 0.35f && !a.IsDown) Issue("CULL_BODY_STILL_SUNK_WHEN_RETURNING_TO_VIEW_" + g, "agent " + i + " " + a.PlayPos + " hips " + hyRel.ToString("0.00", CI) + " m on the first frame back in the frustum, act " + a.Action, hyRel, i, true); }
                }
                // ---- renderers
                if (t.RsPlay != _dir.PlayNumber || t.Rs == null) { t.Rs = v.GetComponentsInChildren<Renderer>(true); t.Smrs = v.GetComponentsInChildren<SkinnedMeshRenderer>(true); t.Pp = v.GetComponentInChildren<PhotoPlayer>(true); t.RsPlay = _dir.PlayNumber; }
                int drawn = 0, drawnSmr = 0;
                foreach (var r in t.Rs) if (Drawn(r)) { drawn++; if (r is SkinnedMeshRenderer) drawnSmr++; }
                if (drawnSmr == 0) { t.NoDrawRun++; if (t.NoDrawRun == 3) { teamNoBody[team]++; Issue("VIS_NO_DRAWN_SKINNED_RENDERER", "agent " + i + " " + a.PlayPos + " drawn=" + drawn + " kind " + sim.Kind + " simPhase " + sim.Phase, drawn, i, true); } else if (t.NoDrawRun > 3) teamNoBody[team]++; }
                else t.NoDrawRun = 0;
                S("drawn renderers per player", drawn);
                if (t.Pp != null)
                {
                    int lvl = t.Pp.Level;
                    if (t.PrevLvl >= 0 && lvl != t.PrevLvl) { t.LvlChanges++; if (_frame - t.LvlPrevChangeFrame <= 12) { H("LOD level flip-flop (changed twice within 0.2 s)"); } t.LvlPrevChangeFrame = _frame; H("PhotoPlayer LOD level changes"); }
                    t.PrevLvl = lvl;
                    string missing = null;
                    foreach (var p in t.Pp.parts)
                    {
                        if (p == null || p.lods == null || p.lods.Length == 0 || !p.visible) continue;
                        bool any = false; foreach (var r in p.lods) if (r != null && Drawn(r, true)) { any = true; break; }
                        if (!any && !(p.hideBeyond && lvl >= p.lods.Length)) missing = (missing == null ? "" : missing + ",") + p.key;
                    }
                    if (missing != null)
                    {
                        t.MissRun++;
                        if (t.MissRun == 4) { teamMissingParts[team]++; Issue("VIS_BODY_PART_HAS_NO_ACTIVE_LOD_" + missing.Split(',')[0], "agent " + i + " " + a.PlayPos + " parts=" + missing + " level " + lvl, 0, i, true); }
                    }
                    else t.MissRun = 0;
                    S("lod level in play", lvl);
                }
                // skinned mesh bounds that do not contain the bones (frustum culling would cut limbs off)
                int badB = 0; string badName = null; float worst = 0f;
                foreach (var smr in t.Smrs)
                {
                    if (!Drawn(smr)) continue;
                    var b = smr.bounds; b.Expand(0.02f);
                    if (float.IsNaN(b.size.x) || b.size.sqrMagnitude < 1e-6f) { badB++; badName = smr.name + " (empty bounds)"; continue; }
                    foreach (var bone in new[] { t.LH, t.RH, t.LF, t.RF, t.Head })
                    {
                        if (bone == null) continue;
                        if (!b.Contains(bone.position)) { }
                    }
                }
                // frustum culling uses the union of the part bounds: test the 5 extremities against the union of all drawn skinned bounds
                {
                    Bounds u = default(Bounds); bool have = false;
                    foreach (var smr in t.Smrs) { if (!Drawn(smr)) continue; if (!have) { u = smr.bounds; have = true; } else u.Encapsulate(smr.bounds); }
                    if (have)
                    {
                        u.Expand(0.02f);
                        foreach (var bone in new[] { t.LH, t.RH, t.LF, t.RF, t.Head })
                        {
                            if (bone == null || u.Contains(bone.position)) continue;
                            float d = Mathf.Sqrt(u.SqrDistance(bone.position)); badB++; if (d > worst) { worst = d; badName = bone.name; }
                        }
                    }
                }
                if (badB > 0) { t.BoundsRun++; if (worst > 0.15f || badName != null && badName.Contains("empty")) { H("frames where a limb is outside the skinned bounds by >15 cm (culling risk)"); if (t.BoundsRun == 6) Issue("VIS_LIMB_OUTSIDE_RENDERER_BOUNDS_" + g, "agent " + i + " " + a.PlayPos + " bone " + badName + " outside bounds by " + worst.ToString("0.00", CI) + " m, action " + a.Action, worst, i, true); } }
                else t.BoundsRun = 0;
                // inside frustum / on the field
                var bbox = new Bounds(vp + Vector3.up * 0.95f, new Vector3(1f, 1.9f, 1f));
                bool inF = GeometryUtility.TestPlanesAABB(_planes, bbox);
                { float hy2 = t.Hips.position.y - vp.y; if (inF && hy2 < -0.5f && !a.IsDown) { H("frames: body buried (hips more than 0.5 m under the turf) with his root inside the broadcast frustum"); t.BuriedRun++; if (t.BuriedRun == 30) Issue("VIS_BURIED_BODY_ROOT_IN_VIEW_OVER_0.5S_" + g, "agent " + i + " " + a.PlayPos + " act " + a.Action + " hips " + hy2.ToString("0.00", CI) + " m under, root inside the camera view for 0.5 s, kind " + sim.Kind, hy2, i, true); } else t.BuriedRun = 0; }
                if (!inF) teamOutOfFrustum[team]++;
                bool onField = Mathf.Abs(vp.x) < 34f && Mathf.Abs(vp.z) < 70f;
                if (onField) teamOnField[team]++; else { teamOffField[team]++; }
                // ---- position vs simulation
                Vector3 sp = frame.ToUnity(a.Pos); sp.y = vp.y;
                float lag = Vector3.Distance(new Vector3(vp.x, 0, vp.z), new Vector3(sp.x, 0, sp.z));
                S("view-vs-sim position error m", lag);
                // v0.12.0 CONTACT: a body that is walking to his place (view speed 0.35 m/s or more: the pre-snap walk-in, the huddle break, a post-whistle walk) is meant to lag the simulation; the v0.11 rule stays as _V011_RULE for the like-for-like count
                if (lag > 1.0f) { t.LagRunOld++; if (t.LagRunOld == 12) Issue("SYNC_VIEW_FAR_FROM_SIM_1M_V011_RULE" + (t.ViewSpeed < 0.35f ? "_STILL" : "_MOVING"), "agent " + i + " " + a.PlayPos + " action " + a.Action + " err " + lag.ToString("0.00", CI) + " m, view speed " + t.ViewSpeed.ToString("0.00", CI), lag, i, false); } else t.LagRunOld = 0;
                if (lag > 1.0f && t.ViewSpeed < 0.35f) { t.LagRun++; if (t.LagRun == 12) Issue("SYNC_VIEW_FAR_FROM_SIM_1M", "agent " + i + " " + a.PlayPos + " action " + a.Action + " err " + lag.ToString("0.00", CI) + " m", lag, i); } else t.LagRun = 0;
                // hips away from the root (skeleton detached from its collider)
                Vector3 hp = t.Hips.position; hipP[i] = hp; hipOk[i] = true; hipSpd[i] = t.ViewSpeed;
                float hoff = Vector3.Distance(new Vector3(hp.x, 0, hp.z), new Vector3(vp.x, 0, vp.z));
                S("hips offset from root m " + g, hoff);
                if (hoff > 1.2f && !contact && !a.IsDown) Issue("SYNC_HIPS_FAR_FROM_ROOT_1.2M_" + g, "agent " + i + " " + a.PlayPos + " action " + a.Action + " clip " + (v.Rig != null ? v.Rig.CurrentClipName : ""), hoff, i);
                // ---- feet
                float lth = (t.LT != null ? t.LT.position.y : t.LF.position.y - 0.09f) - gy, rth = (t.RT != null ? t.RT.position.y : t.RF.position.y - 0.09f) - gy;
                float lah = t.LF.position.y - gy - 0.09f, rah = t.RF.position.y - gy - 0.09f;
                float lowest = Mathf.Min(Mathf.Min(lth, rth), Mathf.Min(lah, rah));
                float lowToe = Mathf.Min(lth, rth);
                float hipsY = hp.y - gy;
                float speedMs = a.Speed * 0.9144f;
                if (pre && _preFrames < 520 && sim.Kind == PlayKind.Scrimmage && _dir.PlayNumber >= 2 && _dir.PlayNumber <= 5 && (i == 2 || i == 8 || i == 12)) ProbeRow("dense", i, v, t, a, sim, gy, speedMs, hipsY);
                var act = a.Action;
                bool groundAct = a.IsDown || act == AgentAction.Tackled || act == AgentAction.Fallen || act == AgentAction.GetUp || act == AgentAction.Slide || act == AgentAction.Celebrate && hipsY < 0.6f;
                bool airOk = act == AgentAction.Jump || act == AgentAction.Hurdle || act == AgentAction.Dive || act == AgentAction.TackleDive || act == AgentAction.Catch || act == AgentAction.Kick || act == AgentAction.Celebrate || act == AgentAction.Stumble || act == AgentAction.TackleBigHit || act == AgentAction.TackleGang || act == AgentAction.Spin || act == AgentAction.Juke || act == AgentAction.Truck || act == AgentAction.StiffArm || act == AgentAction.Throw || act == AgentAction.Shed;
                bool standingAct = (act == AgentAction.Stance || act == AgentAction.Idle || act == AgentAction.Block || act == AgentAction.Engaged || act == AgentAction.Shuffle) && !contact;
                if (!contact && !groundAct)
                {
                    bool simStanding = standingAct && speedMs < 1.2f, viewStill = t.ViewSpeed < 0.35f;      // v0.12: the pre-snap glide moves bodies at 0.5-5 m/s while the simulation still lists them as Stance / 0 m/s: those are walking, not standing
                    if (simStanding && lowToe > 0.09f && lth > 0.09f && rth > 0.09f) { t.FloatRunOld++; if (t.FloatRunOld == 8) Issue("FLOAT_STANDING_V011_RULE_" + g + (viewStill ? "_STILL" : "_MOVING"), "agent " + i + " " + a.PlayPos + " act " + act + " lowest toe " + lowToe.ToString("0.00", CI) + " m, view speed " + t.ViewSpeed.ToString("0.00", CI) + " m/s, sim speed " + speedMs.ToString("0.00", CI), lowToe, i, false); } else t.FloatRunOld = 0;      // the v0.11 rule unchanged (same 8-frame run), for the like-for-like count; split by whether the body is moving on the stage
                    if (simStanding && !viewStill)
                    {
                        H("standing by the simulation but moving on the stage (pre-snap glide / walk-in), frames");
                        if (lowToe > 0.09f && lth > 0.09f && rth > 0.09f) H("v0.11 FLOAT_STANDING rule met by a body that is moving on the stage (walking, not a stance), frames");
                        if (_frame % 12 == 0) ProbeRow("glide", i, v, t, a, sim, gy, speedMs, hipsY);
                        t.FloatRun = 0;
                    }
                    else if (simStanding)
                    {
                        S("standing: lowest foot above turf m " + g, lowest);
                        if (_frame % 12 == 0) ProbeRow("stand", i, v, t, a, sim, gy, speedMs, hipsY);
                        if (lowToe > 0.09f && lth > 0.09f && rth > 0.09f) { t.FloatRun++; if (t.FloatRun == 8) SideShot("float", i, 3); if (t.FloatRun == 8) Issue("FLOAT_STANDING_BOTH_FEET_UP_" + g, "agent " + i + " " + a.PlayPos + " act " + act + " lowest toe " + lowToe.ToString("0.00", CI) + " m, hips " + hipsY.ToString("0.00", CI) + " clip " + (v.Rig != null ? v.Rig.CurrentClipName : ""), lowToe, i, true); }
                        else t.FloatRun = 0;
                    }
                    else t.FloatRun = 0;
                    bool loco = (act == AgentAction.Run || act == AgentAction.Sprint || act == AgentAction.Carry || act == AgentAction.Route || act == AgentAction.Backpedal) && speedMs > 3.0f;
                    if (loco)
                    {
                        H("loco frames (speed>3 m/s) " + g); S("loco hips height " + g, hipsY); if (_frame % 24 == 0) ProbeRow("loco", i, v, t, a, sim, gy, speedMs, hipsY);
                        if (lth > 0.10f && rth > 0.10f) { t.AirRun++; H("loco flight frames (both toes >10 cm) " + g); if (t.AirRun == 14) Issue("FLOAT_RUN_AIRBORNE_OVER_0.23S_" + g, "agent " + i + " " + a.PlayPos + " act " + act + " speed " + speedMs.ToString("0.0", CI) + " m/s, toes " + lowToe.ToString("0.00", CI) + " m", lowToe, i, true); }
                        else t.AirRun = 0;
                    }
                    else t.AirRun = 0;
                    if (hipsY < 0.35f && (standingAct || act == AgentAction.Run || act == AgentAction.Sprint || act == AgentAction.Carry || act == AgentAction.Route) && !a.IsDown) { t.SunkRun++; if (t.SunkRun == 10) Issue("SUNK_HIPS_BELOW_0.35M_" + g, "agent " + i + " " + a.PlayPos + " act " + act + " hips " + hipsY.ToString("0.00", CI), hipsY, i, true); } else t.SunkRun = 0;
                }
                else { t.FloatRun = 0; t.AirRun = 0; t.SunkRun = 0; }
                if (groundAct && !contact && lowest > 0.25f && hipsY > 0.7f) Issue("FLOAT_GROUND_ACTION_STANDING_HEIGHT_" + g, "agent " + i + " " + a.PlayPos + " act " + act + " hips " + hipsY.ToString("0.00", CI), hipsY, i);
                if (lowest < -0.07f) { H("frames with a foot more than 7 cm below the turf " + g); if (_frame % 20 == 0) Issue("FEET_BELOW_TURF_7CM_" + g, "agent " + i + " " + a.PlayPos + " act " + act + " lowest " + lowest.ToString("0.00", CI) + " rootY " + gy.ToString("0.00", CI) + " hipsWorldY " + hp.y.ToString("0.00", CI) + " contact=" + contact + " ragdoll=" + (v.Ragdoll != null) + " clip " + (v.Rig != null ? v.Rig.CurrentClipName : "") + " animEnabled=" + t.An.enabled + " rootMotion=" + t.An.applyRootMotion + " localY " + v.transform.InverseTransformPoint(hp).y.ToString("0.00", CI), lowest, i, true); }
                FeetProbe(i, v, t, a, sim, gy, speedMs, hipsY, lowest, contact);      // v0.13.0 STANCE-HEAD: bone vs drawn sole at every sunk-foot sample and at a control sample of the blocking bodies
                foreach (var bn in new[] { t.LH, t.RH, t.Head })
                { if (bn != null && bn.position.y - gy < -0.08f && !a.IsDown) { H("frames with hand/head below turf " + g); if (_frame % 20 == 0) Issue("HAND_OR_HEAD_BELOW_TURF_" + g, "agent " + i + " " + a.PlayPos + " bone " + bn.name + " " + (bn.position.y - gy).ToString("0.00", CI) + " act " + act, bn.position.y - gy, i); } }
                // ---- foot slide / cadence
                bool plL = t.PlantL ? Mathf.Min(lth, lah) < 0.07f : Mathf.Min(lth, lah) < 0.04f, plR = t.PlantR ? Mathf.Min(rth, rah) < 0.07f : Mathf.Min(rth, rah) < 0.04f;
                Vector3 ltp = t.LT != null ? t.LT.position : t.LF.position, rtp = t.RT != null ? t.RT.position : t.RF.position;
                const float Dt = 1f / 60f;
                if (t.Have && !contact && !groundAct)
                {
                    Vector3 dl = ltp - t.PrevLT; dl.y = 0; Vector3 dr = rtp - t.PrevRT; dr.y = 0;
                    if (plL && t.PlantL && speedMs > 1.5f) S("planted-foot slide m/s (speed>1.5) " + g, dl.magnitude / Dt);
                    if (plR && t.PlantR && speedMs > 1.5f) S("planted-foot slide m/s (speed>1.5) " + g, dr.magnitude / Dt);
                    float hipsRot = Quaternion.Angle(t.Hips.rotation, t.PrevHipsRot) / Dt;
                    float chestRot = t.Chest != null ? Quaternion.Angle(t.Chest.rotation, t.PrevChestRot) / Dt : 0f;
                    if (hipsRot > 900f) { H("hips rotation pop >900 deg/s " + g); if (_frame % 15 == 0) Issue("POP_HIPS_ROT_OVER_900DEGS_" + g, "agent " + i + " " + a.PlayPos + " act " + act + " clip " + (v.Rig != null ? v.Rig.CurrentClipName : ""), hipsRot, i); }
                    if (chestRot > 1400f) { H("chest rotation pop >1400 deg/s " + g); if (_frame % 15 == 0) Issue("POP_CHEST_ROT_OVER_1400DEGS_" + g, "agent " + i + " " + a.PlayPos + " act " + act + " clip " + (v.Rig != null ? v.Rig.CurrentClipName : ""), chestRot, i); }
                    Vector3 hl = v.transform.InverseTransformPoint(hp);
                    if ((hl - t.PrevHipsLocal).magnitude > 0.10f) { H("hips jump >10 cm in one frame " + g); if (_frame % 10 == 0) Issue("POP_HIPS_JUMP_OVER_10CM_" + g, "agent " + i + " " + a.PlayPos + " act " + act + " clip " + (v.Rig != null ? v.Rig.CurrentClipName : ""), (hl - t.PrevHipsLocal).magnitude, i); }
                    t.PrevHipsLocal = hl;
                }
                else t.PrevHipsLocal = v.transform.InverseTransformPoint(hp);
                t.PrevLT = ltp; t.PrevRT = rtp; t.PrevLF = t.LF.position; t.PrevRF = t.RF.position; t.PrevHipsRot = t.Hips.rotation; t.PrevChestRot = t.Chest != null ? t.Chest.rotation : Quaternion.identity; t.PlantL = plL; t.PlantR = plR; t.Have = true;
                // ---- facing
                var pose = BodyHeading.Measure(t.An);
                if (pose.Valid && !contact && !groundAct)
                {
                    Vector3 fd = frame.DirToUnity(a.Facing); float simYaw = Mathf.Atan2(fd.x, fd.z) * Mathf.Rad2Deg;
                    float errF = Mathf.Abs(StanceRules.YawDelta(pose.Yaw, simYaw));
                    S("body yaw vs sim facing deg " + g, errF);
                    // v0.12.0 CONTACT: the same view-speed guard as FLOAT_STANDING (a body that walks to his place turns toward where he walks); the v0.11 rule stays as _V011_RULE
                    bool faceStill = t.ViewSpeed < 0.35f;
                    if (speedMs < 1.0f && errF > 70f && act != AgentAction.Block && act != AgentAction.Engaged) { t.FaceRunOld++; if (t.FaceRunOld == 20) Issue("FACE_BODY_70DEG_OFF_SIM_FACING_STANDING_V011_RULE_" + g + (faceStill ? "_STILL" : "_MOVING"), "agent " + i + " " + a.PlayPos + " act " + act + " err " + errF.ToString("0") + " view speed " + t.ViewSpeed.ToString("0.00", CI), errF, i, false); } else t.FaceRunOld = 0;
                    // v0.13.0 STANCE-HEAD: the FACE rule counted the huddle: a body in the ring stands (the simulation already has him in Stance at 0 m/s, the view does not move him) facing the middle of the ring, 70 to 180 degrees off the line he will face at the snap (168 of the 172 counted runs of a 40-play game, 9 s each).
                    // The rule now needs a body that is not in the huddle; the v0.12 rule stays as _V012_RULE (split _HUDDLE / _SET) and the ring itself is checked on its own (HUDDLE_FACING_OFF_RING_CENTRE_60DEG).
                    bool faceCond = speedMs < 1.0f && errF > 70f && act != AgentAction.Block && act != AgentAction.Engaged && faceStill, hud = v.Huddle;
                    if (faceCond) { t.FaceRunV012++; if (t.FaceRunV012 == 20) Issue("FACE_BODY_70DEG_OFF_SIM_FACING_STANDING_V012_RULE_" + g + (hud ? "_HUDDLE" : "_SET"), "agent " + i + " " + a.PlayPos + " act " + act + " err " + errF.ToString("0"), errF, i, false); } else t.FaceRunV012 = 0;
                    if (faceCond && !hud) { t.FaceRun++; if (t.FaceRun == 20) { Issue("FACE_BODY_70DEG_OFF_SIM_FACING_STANDING_" + g, "agent " + i + " " + a.PlayPos + " act " + act + " err " + errF.ToString("0"), errF, i); FaceProbe(v, t, a, sim, pose, simYaw, speedMs, errF, 20, false); } } else { if (t.FaceRun >= 20) FaceProbe(v, t, a, sim, pose, simYaw, speedMs, errF, t.FaceRun, true); t.FaceRun = 0; }
                    if (hud && _hudN[team] > 3)
                    {
                        Vector3 toC = _hudC[team] - vp; toC.y = 0f;
                        if (toC.magnitude > 0.7f)
                        {
                            float eRing = Mathf.Abs(StanceRules.YawDelta(pose.Yaw, Mathf.Atan2(toC.x, toC.z) * Mathf.Rad2Deg));
                            if (eRing > 60f) { t.HudRun++; if (t.HudRun == 20) { Issue("HUDDLE_FACING_OFF_RING_CENTRE_60DEG_" + g, "agent " + i + " " + a.PlayPos + " err " + eRing.ToString("0") + " ring of " + _hudN[team], eRing, i, false); FaceProbe(v, t, a, sim, pose, simYaw, speedMs, eRing, 20, false); } } else t.HudRun = 0;
                        }
                        else t.HudRun = 0;
                    }
                    else t.HudRun = 0;

                    if (speedMs > 3.0f && (act == AgentAction.Run || act == AgentAction.Sprint || act == AgentAction.Carry || act == AgentAction.Route))
                    {
                        Vector3 vd = frame.DirToUnity(a.Vel); float vy = Mathf.Atan2(vd.x, vd.z) * Mathf.Rad2Deg; float errV = Mathf.Abs(StanceRules.YawDelta(pose.Yaw, vy));
                        S("body yaw vs velocity deg (speed>3) " + g, errV);
                        if (errV > 55f) { t.TravelRun++; if (t.TravelRun == 20) Issue("FACE_RUNNING_55DEG_OFF_TRAVEL_" + g, "agent " + i + " " + a.PlayPos + " act " + act + " err " + errV.ToString("0") + " speed " + speedMs.ToString("0.0", CI), errV, i, true); } else t.TravelRun = 0;
                        if (errV > 120f && act != AgentAction.Backpedal) { t.BackRun++; if (t.BackRun == 12) Issue("FACE_RUNNING_BACKWARDS_" + g, "agent " + i + " " + a.PlayPos + " act " + act + " err " + errV.ToString("0"), errV, i, true); } else t.BackRun = 0;
                    }
                    else { t.TravelRun = 0; t.BackRun = 0; }
                }
                HeadSample(v, t, a, contact);      // v0.13.0 STANCE-HEAD
                // ---- clip switching
                string clip = v.Rig != null ? v.Rig.CurrentClipName : "";
                if (t.Have && clip != t.PrevClip) { t.ClipSwitches++; if (_frame - t.LastClipSwitchFrame < 8) H("clip switches <0.13 s apart " + g); t.LastClipSwitchFrame = _frame; }
                t.PrevClip = clip; t.Alive += Dt;
                // ---- speed discontinuity between sim and view root
                if (a.Action == AgentAction.Idle && speedMs > 1.0f && !contact) H("sim moving >1 m/s while action=Idle " + g);
                if ((a.Action == AgentAction.Run || a.Action == AgentAction.Sprint) && speedMs < 0.4f && !contact) H("action Run/Sprint while sim speed <0.4 m/s " + g);
            }
            // ---- team level visibility
            for (int tm = 0; tm < 2; tm++)
            {
                if (teamNoBody[tm] >= 2 || teamInactive[tm] >= 2) { Issue("VIS_TEAM_MISSING_" + (tm == 0 ? "OFFENSE" : "DEFENSE") + "_" + sim.Kind, "noBody=" + teamNoBody[tm] + " inactive=" + teamInactive[tm] + " simPhase " + sim.Phase, teamNoBody[tm] + teamInactive[tm], -1, true); }
                if (teamOffField[tm] >= 3 && (_dir.Phase == MatchPhase.Live || _dir.Phase == MatchPhase.PreSnap)) Issue("VIS_TEAM_PLAYERS_OFF_FIELD_" + (tm == 0 ? "OFFENSE" : "DEFENSE") + "_" + sim.Kind, "offField=" + teamOffField[tm] + " (not inside the pitch rectangle) simPhase " + sim.Phase, teamOffField[tm], -1, true);
            }
            if (live && teamOutOfFrustum[0] + teamOutOfFrustum[1] > 14) H("frames with >14 of 22 players outside the broadcast frustum (live)");
            S("players inside broadcast frustum (live)", live ? 22 - teamOutOfFrustum[0] - teamOutOfFrustum[1] : 22);
            // ---- overlaps
            for (int i = 0; i < 22; i++)
            {
                if (!hipOk[i]) continue;
                for (int j = i + 1; j < 22; j++)
                {
                    if (!hipOk[j]) continue;
                    float dx = hipP[i].x - hipP[j].x, dz = hipP[i].z - hipP[j].z; float d2 = dx * dx + dz * dz;
                    int k = i * 22 + j;
                    if (d2 < 0.26f * 0.26f && Mathf.Abs(hipP[i].y - hipP[j].y) < 0.6f)
                    {
                        // v0.12.0 CONTACT: two bodies that are walking to their places (view speed 0.35 m/s or more) pass each other; the v0.11 rule (_overlapRunOld) is kept as _V011_RULE
                        bool still2 = hipSpd[i] < 0.35f && hipSpd[j] < 0.35f;
                        _overlapRunOld[k]++; _overlapRun[k] = still2 ? _overlapRun[k] + 1 : 0;
                        var ai = sim.Agents[i]; var aj = sim.Agents[j];
                        bool pile = ai.IsDown || aj.IsDown || ai.Action.ToString().StartsWith("Tackle") || aj.Action.ToString().StartsWith("Tackle") || ai.Action == AgentAction.Tackled || aj.Action == AgentAction.Tackled;
                        string oinfo = i + " " + ai.PlayPos + "/" + ai.Action + " vs " + j + " " + aj.PlayPos + "/" + aj.Action + " hips " + Mathf.Sqrt(d2).ToString("0.00", CI) + " m apart";
                        if (_overlapRunOld[k] == 10 && !pile) Issue("OVERLAP_BODIES_STANDING_OR_BLOCKING_V011_RULE" + (still2 ? "_STILL" : "_MOVING"), oinfo + ", view speed " + hipSpd[i].ToString("0.00", CI) + " / " + hipSpd[j].ToString("0.00", CI), Mathf.Sqrt(d2), i, false);
                        if (pile ? _overlapRunOld[k] == 10 : _overlapRun[k] == 10) Issue(pile ? "OVERLAP_BODIES_IN_TACKLE_OR_PILE" : "OVERLAP_BODIES_STANDING_OR_BLOCKING", oinfo, Mathf.Sqrt(d2), i, !pile);
                    }
                    else { _overlapRun[k] = 0; _overlapRunOld[k] = 0; }
                }
            }
            // ---- ball
            if (st.Ball != null)
            {
                bool ballActive = st.Ball.gameObject.activeInHierarchy;
                if (!ballActive && (sim.Phase != PlayPhase.Dead)) Issue("BALL_HIDDEN_DURING_PLAY", "kind " + sim.Kind + " simPhase " + sim.Phase, 0, -1, true);
                if (ballU.y < -0.05f) Issue("BALL_BELOW_TURF", "ball y " + ballU.y.ToString("0.00", CI) + " state " + sim.Ball.State, ballU.y);
                if (float.IsNaN(ballU.x) || float.IsNaN(ballU.y)) Issue("BALL_POSITION_NAN", "");
                int hid = sim.Ball.Holder;
                if (sim.Ball.State == BallState.Held && hid >= 0 && st.Views[hid] != null)
                {
                    var t = GetVT(st.Views[hid]);
                    if (t != null && t.LH != null && t.RH != null)
                    {
                        float dd = Mathf.Min(Vector3.Distance(ballU, t.LH.position), Vector3.Distance(ballU, t.RH.position));
                        var ha = sim.Agents[hid];
                        S("ball to nearest hand m (held) " + Grp(ha.PlayPos), dd);
                        if (dd > 0.40f && !ha.IsDown) { _ballOffRun++; if (_ballOffRun == 10) Issue("BALL_OFF_BOTH_HANDS_40CM_WHILE_HELD", "holder " + hid + " " + ha.PlayPos + " act " + ha.Action + " dist " + dd.ToString("0.00", CI) + " m parent " + (st.Ball.transform.parent != null ? st.Ball.transform.parent.name : "none") + " mode " + st.Ball.ModeName, dd, hid, true); } else _ballOffRun = 0;
                        H("ball mode while Held: " + st.Ball.ModeName + " parent=" + (st.Ball.transform.parent != null ? st.Ball.transform.parent.name : "none"));
                    }
                }
                else _ballOffRun = 0;
                if (sim.Ball.State != BallState.Held)
                {
                    Vector3 exp = frame.ToUnity(sim.Ball.Pos);
                    float d = Vector3.Distance(exp, ballU);
                    S("ball render vs sim position m (not held) " + sim.Ball.State, d);
                    if (d > 1.5f) { _ballDesyncRun++; if (_ballDesyncRun == 6) Issue("BALL_RENDER_FAR_FROM_SIM_1.5M_" + sim.Ball.State, "rendered ball is " + d.ToString("0.0", CI) + " m from the simulated ball", d, -1, true); } else _ballDesyncRun = 0;
                }
                else _ballDesyncRun = 0;
                // camera framing
                if (cam != null && live)
                {
                    Vector3 vpB = cam.WorldToViewportPoint(ballU);
                    bool off = vpB.z <= 0f || vpB.x < 0.03f || vpB.x > 0.97f || vpB.y < 0.03f || vpB.y > 0.97f;
                    S("ball viewport x (live)", vpB.x); S("ball viewport y (live)", vpB.y);
                    if (off) { _camOff++; if (_camOff == 30) Issue("CAM_BALL_OFFSCREEN_OVER_0.5S", "ball viewport " + vpB.x.ToString("0.00", CI) + "," + vpB.y.ToString("0.00", CI) + " z " + vpB.z.ToString("0.0", CI) + " state " + sim.Ball.State, vpB.x, -1, true); } else _camOff = 0;
                    var car = sim.Carrier;
                    if (car != null && st.Views[car.Id] != null)
                    {
                        Vector3 cv = cam.WorldToViewportPoint(st.Views[car.Id].transform.position + Vector3.up);
                        bool coff = cv.z <= 0f || cv.x < 0.02f || cv.x > 0.98f || cv.y < 0.02f || cv.y > 0.98f;
                        if (coff) { _carrierOff++; if (_carrierOff == 30) Issue("CAM_CARRIER_OFFSCREEN_OVER_0.5S", "carrier " + car.PlayPos + " viewport " + cv.x.ToString("0.00", CI) + "," + cv.y.ToString("0.00", CI), cv.x, car.Id, true); } else _carrierOff = 0;
                    }
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------------- report body
        string ExtraReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== plays by kind =="); foreach (var kv in _playCount) sb.AppendLine(kv.Key + " " + kv.Value);
            sb.AppendLine();
            sb.AppendLine("== counters ==");
            foreach (var kv in _hist) sb.AppendLine(kv.Key + " : " + kv.Value);
            sb.AppendLine();
            sb.AppendLine("== distributions ==");
            foreach (var kv in _stats) sb.AppendLine(kv.Key + " : " + kv.Value.Describe());
            System.IO.File.WriteAllText(_out + "/stance_snapshots.csv", _stanceCsv.ToString());
            return sb.ToString();
        }
    }
}
