using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 MOTION: the game's door to Final IK (RootMotion, owner-licensed Asset Store asset, installed under Assets/_Licensed/RootMotion, git-ignored; never in the public repo).
    /// This file is always compiled and has no Final IK types in it: without the asset <see cref="Service"/> stays null and every call is a no-op, so the public repo builds and plays exactly as before.
    /// With the asset installed (see Docs/QA/v0110/motion.md "Final IK in PIX") the bridge assembly <c>PixFootball.FinalIkBridge</c> (source: Editor/Tools/FinalIk/Bridge~) registers itself here at startup
    /// and drives a <c>FullBodyBipedIK</c> per body: hands and feet reach world targets with the body (shoulders, spine, pelvis) following, on top of the final animated pose.
    /// Usage from any track: call <see cref="Hand"/> / <see cref="Foot"/> EVERY FRAME while the contact exists (before the end of LateUpdate; the stage's Present or a view's LateUpdate hook is fine);
    /// stop calling and the effector fades out by itself (0.2 s). <see cref="Late"/> is called once per view by <c>SimAgentView.LateUpdate</c> after every procedural pass, and solves only while an effector has weight.
    /// </summary>
    public interface IPixIk
    {
        /// <summary>Wants a limb effector at a world pose this frame. posW / rotW 0..1 (1 = the effector is where asked, the body follows).</summary>
        void Effector(SimAgentView v, PixIk.Limb limb, Vector3 pos, Quaternion rot, float posW, float rotW);
        /// <summary>Pulls the whole body by <paramref name="pull"/> (world offset of the body effector, m) with weight w: a tackler driven into the ball carrier, a blocker leaning into the rusher.</summary>
        void Body(SimAgentView v, Vector3 pull, float w);
        /// <summary>Solves the effectors that have weight; no-op otherwise. Called from SimAgentView.LateUpdate.</summary>
        void Late(SimAgentView v, float dt);
        /// <summary>The body goes back to the pool / is destroyed.</summary>
        void Release(SimAgentView v);
        /// <summary>Solves done and milliseconds spent since the last <see cref="ResetStats"/> (QA).</summary>
        void Stats(out int solves, out double ms);
        void ResetStats();
    }

    public static class PixIk
    {
        public enum Limb { LeftHand, RightHand, LeftFoot, RightFoot }

        /// <summary>Set by the Final IK bridge assembly at startup; null in the public repo.</summary>
        public static IPixIk Service;
        public static bool Available { get { return Service != null; } }
        /// <summary>QA switch -pixIkOff: Final IK present but not used (A/B against the code-only bodies).</summary>
        public static bool Off;
        /// <summary>QA switch -pixGrounder: Final IK's GrounderIK (two LimbIK legs) on top of the final pose of every body the camera shows (experiment, see Docs/QA/v0110/motion.md). Off by default: FootPlant already locks and presses the feet.</summary>
        public static bool Grounder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Switches() { { var a = System.Environment.GetCommandLineArgs(); if (System.Array.IndexOf(a, "-pixIkOff") >= 0) Off = true; if (System.Array.IndexOf(a, "-pixGrounder") >= 0) Grounder = true; } }

        public static void Hand(SimAgentView v, bool left, Vector3 pos, Quaternion rot, float weight = 1f, float rotWeight = 0f)
        {
            if (Service != null && !Off && v != null) Service.Effector(v, left ? Limb.LeftHand : Limb.RightHand, pos, rot, weight, rotWeight);
        }

        public static void Foot(SimAgentView v, bool left, Vector3 pos, Quaternion rot, float weight = 1f, float rotWeight = 0f)
        {
            if (Service != null && !Off && v != null) Service.Effector(v, left ? Limb.LeftFoot : Limb.RightFoot, pos, rot, weight, rotWeight);
        }

        public static void Body(SimAgentView v, Vector3 pull, float weight = 1f)
        {
            if (Service != null && !Off && v != null) Service.Body(v, pull, weight);
        }

        public static void Late(SimAgentView v, float dt)
        {
            if (Service != null && !Off && v != null) Service.Late(v, dt);
        }

        public static void Release(SimAgentView v)
        {
            if (Service != null && v != null) Service.Release(v);
        }
    }
}
