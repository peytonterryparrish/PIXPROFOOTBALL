using System.Diagnostics;
using PixFootball.Gameplay;
using RootMotion;
using RootMotion.FinalIK;
using UnityEngine;
using FikLimb = RootMotion.FinalIK.LimbIK;      // the game has its own LimbIK (PixFootball.Gameplay)

namespace PixFootball.FinalIkBridge
{
    /// <summary>
    /// v0.11.0 MOTION: Final IK behind <see cref="IPixIk"/>. Lives in Assets/_Licensed/RootMotion/PixBridge (copied there by FinalIkInstaller; the folder, the asset and this assembly are git-ignored and absent from the public repo).
    /// One <c>FullBodyBipedIK</c> per body, never self-updating (the component is disabled; <see cref="Late"/> calls the solver by hand, last, on the final animated pose), solved only while an effector has weight
    /// and only for bodies the camera still shows at LOD 0 / 1. Effectors ramp in over 0.10 s and out over 0.20 s; a limb that is not asked for again fades by itself.
    /// </summary>
    public sealed class FinalIkService : IPixIk
    {
        const int Limbs = 4;
        sealed class State
        {
            public FullBodyBipedIK Ik; public bool Failed;
            public readonly Vector3[] Pos = new Vector3[Limbs]; public readonly Quaternion[] Rot = new Quaternion[Limbs];
            public readonly float[] PosW = new float[Limbs], RotW = new float[Limbs], PosT = new float[Limbs], RotT = new float[Limbs];
            public readonly bool[] Fresh = new bool[Limbs];
            public Vector3 Pull; public float BodyW, BodyT; public bool BodyFresh;
            public FikLimb LegL, LegR; public GrounderIK Gr; public bool GroundTried;
        }

        int _solves; double _ms; double _groundMs; int _groundSolves;
        const int GroundLayer = 30; static GameObject _plane;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { PixIk.Service = new FinalIkService(); }

        static State Get(SimAgentView v, bool create)
        {
            var s = v.PixIkState as State;
            if (s != null || !create) return s;
            s = new State(); v.PixIkState = s;
            var an = v.Avatar != null ? v.Avatar.Animator : null;
            if (an == null || !an.isHuman) { s.Failed = true; return s; }
            try
            {
                var go = an.gameObject;
                var ik = go.GetComponent<FullBodyBipedIK>(); if (ik == null) ik = go.AddComponent<FullBodyBipedIK>();
                ik.fixTransforms = false;
                var refs = new BipedReferences();
                BipedReferences.AutoDetectReferences(ref refs, an.transform, BipedReferences.AutoDetectParams.Default);
                ik.SetReferences(refs, null);
                var sv = ik.solver;
                sv.iterations = 3; sv.IKPositionWeight = 1f;
                ik.enabled = false;      // after the references: OnDisable initiates the solver; from here on only Late() updates it
                if (!ik.solver.initiated) ik.solver.Initiate(an.transform);
                s.Ik = ik; s.Failed = !ik.solver.initiated;
            }
            catch (System.Exception e) { s.Failed = true; UnityEngine.Debug.LogWarning("[PixIk] Final IK setup failed on " + v.name + ": " + e.Message); }
            return s;
        }

        /// <summary>Experiment (-pixGrounder): GrounderIK with a LimbIK per leg, both driven by hand from Late() (their own LateUpdate is off). A flat collider on a private layer stands in for the turf.</summary>
        static void EnsureGrounder(SimAgentView v, State s)
        {
            if (s.GroundTried) return; s.GroundTried = true;
            try
            {
                var an = v.Avatar.Animator; var go = an.gameObject;
                if (_plane == null) { _plane = new GameObject("PixGroundPlane") { layer = GroundLayer }; var bc = _plane.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, -0.5f, 0f); bc.size = new Vector3(1000f, 1f, 1000f); }
                Transform hips = an.GetBoneTransform(HumanBodyBones.Hips);
                s.LegL = go.AddComponent<FikLimb>(); s.LegR = go.AddComponent<FikLimb>();
                s.LegL.solver.SetChain(an.GetBoneTransform(HumanBodyBones.LeftUpperLeg), an.GetBoneTransform(HumanBodyBones.LeftLowerLeg), an.GetBoneTransform(HumanBodyBones.LeftFoot), an.transform);
                s.LegR.solver.SetChain(an.GetBoneTransform(HumanBodyBones.RightUpperLeg), an.GetBoneTransform(HumanBodyBones.RightLowerLeg), an.GetBoneTransform(HumanBodyBones.RightFoot), an.transform);
                s.LegL.solver.goal = AvatarIKGoal.LeftFoot; s.LegR.solver.goal = AvatarIKGoal.RightFoot;
                s.LegL.solver.bendModifier = IKSolverLimb.BendModifier.Animation; s.LegR.solver.bendModifier = IKSolverLimb.BendModifier.Animation;
                s.LegL.fixTransforms = false; s.LegR.fixTransforms = false;
                s.LegL.enabled = false; s.LegR.enabled = false;
                var gr = go.AddComponent<GrounderIK>(); gr.legs = new IK[] { s.LegL, s.LegR }; gr.pelvis = hips; gr.characterRoot = an.transform;
                gr.solver.layers = 1 << GroundLayer; gr.solver.quality = Grounding.Quality.Fastest; gr.solver.maxStep = 0.4f; gr.solver.footSpeed = 4f; gr.solver.pelvisSpeed = 6f; gr.solver.footRadius = 0.1f;
                gr.weight = 1f; s.Gr = gr;
                gr.enabled = true;      // its Update initiates it on the next frame
            }
            catch (System.Exception e) { UnityEngine.Debug.LogWarning("[PixIk] Grounder setup failed on " + v.name + ": " + e.Message); }
        }

        public void Effector(SimAgentView v, PixIk.Limb limb, Vector3 pos, Quaternion rot, float posW, float rotW)
        {
            var s = Get(v, true); if (s.Failed) return;
            int i = (int)limb; s.Pos[i] = pos; s.Rot[i] = rot; s.PosT[i] = Mathf.Clamp01(posW); s.RotT[i] = Mathf.Clamp01(rotW); s.Fresh[i] = true;
        }

        public void Body(SimAgentView v, Vector3 pull, float w)
        {
            var s = Get(v, true); if (s.Failed) return;
            s.Pull = pull; s.BodyT = Mathf.Clamp01(w); s.BodyFresh = true;
        }

        static IKEffector EffectorOf(IKSolverFullBodyBiped sv, int i)
        {
            switch (i) { case 0: return sv.leftHandEffector; case 1: return sv.rightHandEffector; case 2: return sv.leftFootEffector; default: return sv.rightFootEffector; }
        }

        void GroundLate(SimAgentView v)
        {
            var lod = v.Avatar != null ? v.Avatar.Lod : null; if (lod != null && lod.Level >= 2) return;
            var s = Get(v, true); if (s.Failed) return;
            EnsureGrounder(v, s);
            if (s.Gr == null || s.LegL == null || !s.LegL.solver.initiated) return;
            long t0 = Stopwatch.GetTimestamp();
            s.LegL.solver.Update(); s.LegR.solver.Update();
            _groundMs += (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency; _groundSolves++;
        }

        public void Late(SimAgentView v, float dt)
        {
            if (PixIk.Grounder) GroundLate(v);
            var s = v.PixIkState as State; if (s == null || s.Failed || s.Ik == null) return;
            bool any = false;
            for (int i = 0; i < Limbs; i++)
            {
                if (!s.Fresh[i]) { s.PosT[i] = 0f; s.RotT[i] = 0f; }
                s.PosW[i] = Mathf.MoveTowards(s.PosW[i], s.PosT[i], dt / (s.PosT[i] > s.PosW[i] ? 0.10f : 0.20f));
                s.RotW[i] = Mathf.MoveTowards(s.RotW[i], s.RotT[i], dt / (s.RotT[i] > s.RotW[i] ? 0.10f : 0.20f));
                s.Fresh[i] = false;
                if (s.PosW[i] > 0.003f || s.RotW[i] > 0.003f) any = true;
            }
            if (!s.BodyFresh) s.BodyT = 0f;
            s.BodyW = Mathf.MoveTowards(s.BodyW, s.BodyT, dt / (s.BodyT > s.BodyW ? 0.12f : 0.25f)); s.BodyFresh = false;
            if (s.BodyW > 0.003f) any = true;
            if (!any) return;
            // far bodies are not worth it (LOD 2 and beyond: a few pixels of hand)
            var lod = v.Avatar != null ? v.Avatar.Lod : null; if (lod != null && lod.Level >= 2) return;
            var sv = s.Ik.solver;
            for (int i = 0; i < Limbs; i++)
            {
                var e = EffectorOf(sv, i);
                e.position = s.Pos[i]; e.rotation = s.Rot[i]; e.positionWeight = s.PosW[i]; e.rotationWeight = s.RotW[i];
            }
            sv.bodyEffector.positionOffset = s.Pull * s.BodyW;
            long t0 = Stopwatch.GetTimestamp();
            sv.Update();
            _ms += (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency; _solves++;
        }

        public void Release(SimAgentView v)
        {
            var s = v.PixIkState as State; if (s == null) return;
            for (int i = 0; i < Limbs; i++) { s.PosW[i] = s.RotW[i] = s.PosT[i] = s.RotT[i] = 0f; s.Fresh[i] = false; }
            s.BodyW = s.BodyT = 0f; s.BodyFresh = false;
        }

        public void Stats(out int solves, out double ms) { solves = _solves + _groundSolves; ms = _ms + _groundMs; }
        public void ResetStats() { _solves = 0; _ms = 0; _groundSolves = 0; _groundMs = 0; }
    }
}
