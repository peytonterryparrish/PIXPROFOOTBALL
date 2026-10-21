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
            foreach (string p in new[] { "VelX", "VelZ", "Speed", "LocoSpeed" }) ctrl.AddParameter(p, AnimatorControllerParameterType.Float);
            foreach (string p in new[] { "Celebrating", "Protect" }) ctrl.AddParameter(p, AnimatorControllerParameterType.Bool);
            foreach (var s in PlayerMoves.All) if (s.Move != PlayerMove.Celebrate) ctrl.AddParameter(s.Trigger, AnimatorControllerParameterType.Trigger);
            var lc = ctrl.parameters; for (int i = 0; i < lc.Length; i++) if (lc[i].name == "LocoSpeed") { lc[i].defaultFloat = 1f; } ctrl.parameters = lc;

            // ---- locomotion ----
            var idle = lib.Clip(ArtKeys.Idle);
            var jog = FindClipByName("JogCycleJR") ?? lib.Clip(ArtKeys.Jog);
            var run = FindClipByName("RunCycleJR_1") ?? lib.Clip(ArtKeys.Run);
            var sprint = FindClipByName("SprintCycleJR_1") ?? lib.Clip(ArtKeys.Sprint);
            var back = lib.Clip(ArtKeys.Backpedal);
            var sl = lib.Clip(ArtKeys.StrafeLeft);
            var sr = lib.Clip(ArtKeys.StrafeRight);
            float vJog = Natural(jog, 3.0f), vRun = Natural(run, 5.0f), vSprint = Natural(sprint, 8.0f), vBack = Natural(back, 2.0f), vSL = Natural(sl, 2.4f), vSR = Natural(sr, 2.4f);
            // keep the ladder monotonic even if root-motion data is odd
            vRun = Mathf.Max(vRun, vJog + 0.8f); vSprint = Mathf.Max(vSprint, vRun + 0.5f);
            Debug.Log("[PIX] clip natural speeds jog=" + vJog.ToString("0.00") + " run=" + vRun.ToString("0.00") + " sprint=" + vSprint.ToString("0.00") + " back=" + vBack.ToString("0.00") + " strafeL=" + vSL.ToString("0.00") + " strafeR=" + vSR.ToString("0.00"));

            BlendTree tree;
            var locoState = ctrl.CreateBlendTreeInController("Locomotion", out tree, 0);
            tree.blendType = BlendTreeType.FreeformCartesian2D;
            tree.blendParameter = "VelX"; tree.blendParameterY = "VelZ";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, new Vector2(0f, 0f));
            tree.AddChild(jog, new Vector2(0f, vJog * 0.5f));  // walk = slowed jog
            tree.AddChild(jog, new Vector2(0f, vJog));
            tree.AddChild(run, new Vector2(0f, vRun));
            tree.AddChild(sprint, new Vector2(0f, vSprint));
            tree.AddChild(back, new Vector2(0f, -vBack));
            tree.AddChild(sl, new Vector2(-vSL, 0f));
            tree.AddChild(sr, new Vector2(vSR, 0f));
            // diagonal fillers: forward-left / forward-right blend of run with strafes handled by the freeform gradient band
            var ch = tree.children;
            ch[1].timeScale = 0.5f; ch[1].directBlendParameter = "";
            tree.children = ch;
            locoState.speedParameterActive = true; locoState.speedParameter = "LocoSpeed";

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
            var speeds = new[] { vJog, vRun, vSprint, vBack, (vSL + vSR) * 0.5f };
            if (speedsOut != null) for (int i = 0; i < speedsOut.Length && i < speeds.Length; i++) speedsOut[i] = speeds[i];
            LastSpeeds = speeds;
            return ctrl;
        }

        public static float[] LastSpeeds = new[] { 3.2f, 5.2f, 8.2f, 2.2f, 2.6f };
    }
}
