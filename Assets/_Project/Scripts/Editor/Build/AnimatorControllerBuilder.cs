using System.IO;
using System.Linq;
using PixFootball.Art;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Builds PIX_PlayerLocomotion.controller from the ArtLibrary clips: a 2D freeform blend tree (idle / walk / jog / run / sprint / strafe / backpedal)
    /// on local velocity in m/s plus an override action layer with trimmed ball-carrier moves.
    /// </summary>
    public static class AnimatorControllerBuilder
    {
        public const string Dir = "Assets/_Licensed/PIX/Animation";
        public const string ControllerPath = Dir + "/PIX_PlayerLocomotion.controller";
        public const string AnimRoot = "Assets/_Licensed/DraftFever/Animations";

        public static AnimationClip FindClipByName(string clipName)
        {
            // v0.8: a cleaned copy (Animations/Clean, same clip name) wins over the raw mocap
            var cleaned = AssetDatabase.LoadAssetAtPath<AnimationClip>(AnimRoot + "/Clean/" + ClipCleaner.SafeName(clipName) + ".anim");
            if (cleaned != null) return cleaned;
            foreach (string g in AssetDatabase.FindAssets(clipName + " t:Model", new[] { AnimRoot }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(p))
                {
                    var c = o as AnimationClip;
                    if (c != null && c.name == clipName && !c.name.StartsWith("__preview")) return c;
                }
            }
            return null;
        }

        static float Natural(AnimationClip c, float fallback)
        {
            if (c == null) return fallback;
            float s = c.averageSpeed.magnitude;
            return s > 0.3f ? s : fallback;
        }

        /// <summary>Adds the idle and every named cycle to a freeform 2D tree at (right, forward) = stride speed x travel direction (measured on the Medium rig).</summary>
        static void FillTree(BlendTree tree, AnimRig rig, AnimRig.Frame idlePose, AnimationClip idle, string[] names, ArtLibrary lib, float[] speedsOut, System.Text.StringBuilder log)
        {
            tree.blendType = BlendTreeType.FreeformCartesian2D;
            tree.blendParameter = "VelX"; tree.blendParameterY = "VelZ";
            tree.useAutomaticThresholds = false;
            if (idle != null) tree.AddChild(idle, new Vector2(0f, 0f));
            float jog = 0f, run = 0f, sprint = 0f, back = 0f, side = 0f; int nSide = 0;
            var used = new System.Collections.Generic.List<Vector2>();
            foreach (string n in names)
            {
                var clip = FindClipByName(n);
                if (clip == null) { Debug.LogWarning("[PIX] locomotion clip missing: " + n); continue; }
                float spd, ang;
                if (rig != null)
                {
                    var r = AnimAudit.Analyse(rig, clip, idlePose);
                    spd = r.stanceSpeed > 0.3f ? r.stanceSpeed * 1.08f : r.speed;
                    ang = -r.travelAngle;
                }
                else { spd = Natural(clip, 3f); ang = 0f; }
                if (spd < 0.4f) { Debug.LogWarning("[PIX] locomotion clip " + n + " does not travel (" + spd.ToString("0.00") + " m/s): skipped"); continue; }
                float rad = ang * Mathf.Deg2Rad;
                var pos = new Vector2(Mathf.Sin(rad) * spd, Mathf.Cos(rad) * spd);
                bool dup = false; foreach (var u in used) if ((u - pos).sqrMagnitude < 0.04f) dup = true;
                if (dup) continue;
                used.Add(pos);
                tree.AddChild(clip, pos);
                float a = Mathf.Abs(ang);
                if (a < 25f) { if (spd < 2.3f) { } else if (spd < 4.4f) jog = Mathf.Max(jog, spd); else if (spd < 6.8f) run = Mathf.Max(run, spd); else sprint = Mathf.Max(sprint, spd); }
                else if (a > 150f) back = Mathf.Max(back, spd);
                else if (a > 70f && a < 110f) { side += spd; nSide++; }
                log.Append(n).Append("=").Append(spd.ToString("0.00")).Append("@").Append(ang.ToString("0")).Append(" ");
            }
            if (jog <= 0f) jog = 3.0f; if (run <= jog) run = jog + 1.6f; if (sprint <= run) sprint = run + 1.8f;
            speedsOut[0] = jog; speedsOut[1] = run; speedsOut[2] = sprint; speedsOut[3] = back > 0f ? back : 2.0f; speedsOut[4] = nSide > 0 ? side / nSide : 2.4f;
            log.Append("| ladder jog=").Append(jog.ToString("0.0")).Append(" run=").Append(run.ToString("0.0")).Append(" sprint=").Append(sprint.ToString("0.0")).Append(" back=").Append(speedsOut[3].ToString("0.0")).Append(" side=").Append(speedsOut[4].ToString("0.0")).Append("; ");
        }

        public static AnimationClip ResolveClip(MoveSpec spec, ArtLibrary lib)
        {
            if (spec.ClipNames == null || spec.ClipNames.Length == 0) return null;
            foreach (string n in spec.ClipNames) { var c = FindClipByName(n); if (c != null) return c; }
            return lib != null ? lib.Clip(spec.ClipKey) : null;
        }

        [MenuItem("PIX Football/Assets/Build Player Animator")]
        public static void Build() { BuildAndReport(); }

        public static AnimatorController BuildAndReport(float[] speedsOut = null)
        {
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            if (lib == null) { Debug.LogError("[PIX] ArtLibrary missing"); return null; }
            MaterialFactory.EnsureFolder(Dir);
            if (File.Exists(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            foreach (string p in new[] { "VelX", "VelZ", "Speed", "LocoSpeed", "CycleOffset" }) ctrl.AddParameter(p, AnimatorControllerParameterType.Float);
            foreach (string p in new[] { "Celebrating", "Protect" }) ctrl.AddParameter(p, AnimatorControllerParameterType.Bool);
            foreach (var s in PlayerMoves.All) if (s.Move != PlayerMove.Celebrate) ctrl.AddParameter(s.Trigger, AnimatorControllerParameterType.Trigger);
            var lc = ctrl.parameters; for (int i = 0; i < lc.Length; i++) if (lc[i].name == "LocoSpeed") { lc[i].defaultFloat = 1f; } ctrl.parameters = lc;

            // ---- locomotion (v0.8: data driven) ----
            // Every cycle is placed in the 2D velocity space by its MEASURED stride: the speed the planted foot really moves (AnimAudit stance speed), not the root-motion speed
            // (which overstates it by 10-50 % on several Motus takes and made the feet skate), in the direction the take really travels. Two trees: normal running and ball carrier.
            var rig = AnimRig.CreateMedium();
            AnimRig.Frame idlePose = default(AnimRig.Frame);
            var idleClip = FindClipByName("Player_Standing_Idle") ?? lib.Clip(ArtKeys.Idle);
            if (rig != null && idleClip != null) idlePose = rig.PoseAt(idleClip, 0.5f);
            var tag = new System.Text.StringBuilder();

            BlendTree tree;
            var locoState = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
            float[] speeds = new[] { 3.0f, 5.0f, 8.0f, 2.0f, 2.4f };
            FillTree(tree, rig, idlePose, idleClip, new[]
            {
                "PIX_Walk_Cycle", "JogCycleJR", "RunCycleJR_1", "SprintCycleJR_1",
                "BGS__BGS_CB_ShuffleBack", "BGS__BGS_CB_ShuffleBackLeft", "BGS__BGS_CB_ShuffleBackRight",
                "BGS__BGS_CB_ShuffleLeft", "BGS__BGS_CB_ShuffleRight", "BGS__BGS_CB_ShuffleFoLeft",
                "BGS__BGS_Sprint08_FL_Loop", "BGS__BGS_Sprint08_FR_Loop", "BGS__BGS_Run07_FL_Loop_BC", "BGS__BGS_Run07_FR_Loop_BC"
            }, lib, speeds, tag);
            locoState.speedParameterActive = true; locoState.speedParameter = "LocoSpeed"; locoState.cycleOffsetParameterActive = true; locoState.cycleOffsetParameter = "CycleOffset";     // per-player phase: no synchronised hops

            BlendTree carry;
            var carryState = ctrl.CreateBlendTreeInController("Carry", out carry, 0);
            float[] carrySpeeds = new[] { 3.0f, 5.0f, 8.0f, 2.0f, 2.4f };
            FillTree(carry, rig, idlePose, idleClip, new[]
            {
                "BGS__MOB1_Jog_F_Loop_BC", "BGS__MOB1_Run_F_Loop_BC", "BGS__BGS_Sprint08_F_Loop_BC",
                "BGS__MOB1_Jog_FL_Loop_BC", "BGS__MOB1_Jog_FR_Loop_BC", "BGS__MOB1_Run_FL_Loop_BC", "BGS__MOB1_Run_FR_Loop_BC",
                "BGS__BGS_Sprint08_FL_Loop_BC", "BGS__BGS_Sprint08_FR_Loop_BC",
                "BGS__MOB1_Jog_L_Loop_BC", "BGS__MOB1_Jog_R_Loop_BC", "BGS__MOB1_Run_L_Loop_BC", "BGS__MOB1_Run_R_Loop_BC",
                "BGS__MOB1_Jog_B_Loop_BC", "BGS__MOB1_Jog_BL_BkPd_Loop_BC", "BGS__MOB1_Jog_BR_BkPd_Loop_BC"
            }, lib, carrySpeeds, tag);
            carryState.speedParameterActive = true; carryState.speedParameter = "LocoSpeed"; carryState.cycleOffsetParameterActive = true; carryState.cycleOffsetParameter = "CycleOffset";
            var toCarry = locoState.AddTransition(carryState); toCarry.hasExitTime = false; toCarry.hasFixedDuration = true; toCarry.duration = 0.3f; toCarry.AddCondition(AnimatorConditionMode.If, 0f, "Protect");
            var toLoco = carryState.AddTransition(locoState); toLoco.hasExitTime = false; toLoco.hasFixedDuration = true; toLoco.duration = 0.3f; toLoco.AddCondition(AnimatorConditionMode.IfNot, 0f, "Protect");
            ctrl.layers[0].stateMachine.defaultState = locoState;
            if (rig != null) rig.Dispose();
            Debug.Log("[PIX] locomotion trees (stride speeds m/s): " + tag);

            // ---- action layer ----
            var layerSm = new AnimatorStateMachine { name = "Actions", hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(layerSm, ctrl);
            var layer = new AnimatorControllerLayer { name = "Actions", stateMachine = layerSm, defaultWeight = 1f, blendingMode = AnimatorLayerBlendingMode.Override };
            ctrl.AddLayer(layer);
            var none = layerSm.AddState("None");
            layerSm.defaultState = none;
            foreach (var spec in PlayerMoves.All)
            {
                var clip = ResolveClip(spec, lib);
                if (clip == null) { if (spec.Move != PlayerMove.Spin) Debug.LogWarning("[PIX] no clip for move " + spec.Move); continue; }
                var st = layerSm.AddState(spec.Move.ToString());
                st.motion = clip; st.mirror = spec.Mirror; st.speed = spec.PlaySpeed; st.cycleOffset = spec.Start01;
                if (spec.Move == PlayerMove.Celebrate)
                {
                    var t1 = none.AddTransition(st); t1.hasExitTime = false; t1.duration = 0.25f; t1.AddCondition(AnimatorConditionMode.If, 0f, "Celebrating");
                    var t2 = st.AddTransition(none); t2.hasExitTime = false; t2.duration = 0.25f; t2.AddCondition(AnimatorConditionMode.IfNot, 0f, "Celebrating");
                    continue;
                }
                var tin = none.AddTransition(st); tin.hasExitTime = false; tin.hasFixedDuration = true; tin.duration = 0.08f; tin.AddCondition(AnimatorConditionMode.If, 0f, spec.Trigger);
                tin.interruptionSource = TransitionInterruptionSource.None;
                var tout = st.AddTransition(none); tout.hasExitTime = true; tout.exitTime = spec.End01; tout.hasFixedDuration = true; tout.duration = 0.18f;
                Debug.Log("[PIX] move " + spec.Move + " clip=" + clip.name + " len=" + clip.length.ToString("0.00") + " play=" + ((spec.End01 - spec.Start01) * clip.length / spec.PlaySpeed).ToString("0.00") + "s");
            }
            // correct layer weight (AddLayer copies the struct before we set the sub asset)
            var layers = ctrl.layers; layers[1].defaultWeight = 1f; ctrl.layers = layers;
            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();

            if (speedsOut != null) for (int i = 0; i < speedsOut.Length && i < speeds.Length; i++) speedsOut[i] = speeds[i];
            LastSpeeds = speeds;
            return ctrl;
        }

        public static float[] LastSpeeds = new[] { 3.2f, 5.2f, 8.2f, 2.2f, 2.6f };
    }
}
