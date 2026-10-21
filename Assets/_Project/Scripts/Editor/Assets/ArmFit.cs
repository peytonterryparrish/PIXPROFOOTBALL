using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// The keyframed coach / referee clips are authored in Blender with 2-bone IK, but a Humanoid forearm only has stretch + twist DoFs with its own
    /// axis conventions, so the arm part does not survive the FBX -> Humanoid conversion (hands end up 20 cm off or the forearms point up).
    /// This fits the 10 arm muscles per key to the wrist / elbow positions exported by build_anims.py (Coach_ArmTargets.json) inside Unity itself
    /// (HumanPoseHandler on the real avatar), and writes standalone .anim clips (all other muscles / root curves copied from the FBX clip).
    /// </summary>
    public static class ArmFit
    {
        [Serializable] class Key { public int frame; public float[] l; public float[] r; }
        [Serializable] class ClipTargets { public string name; public Key[] keys; }
        [Serializable] class TargetFile { public ClipTargets[] clips; }

        public const string OutDir = RefCoachBuilder.CoachDir + "/Clips";
        static readonly string[] Suffix = { "Arm Down-Up", "Arm Front-Back", "Arm Twist In-Out", "Forearm Stretch", "Forearm Twist In-Out", "Hand Down-Up", "Hand In-Out" };
        const int N = 7;

        static Vector3 ToUnity(float x, float y, float z) { return new Vector3(-x, z, -y); }

        static Vector3[] Targets(float[] a)
        {
            var r = new Vector3[6];
            for (int i = 0; i < 6; i++) r[i] = ToUnity(a[i * 3], a[i * 3 + 1], a[i * 3 + 2]);
            return r;
        }

        static int MuscleIndex(string name)
        {
            for (int i = 0; i < HumanTrait.MuscleCount; i++) if (HumanTrait.MuscleName[i] == name) return i;
            throw new Exception("muscle not found: " + name);
        }

        class Solver
        {
            public Animator anim; public HumanPoseHandler handler; public HumanPose pose;
            public Transform upper, lower, hand, index, middle, little;
            public int[] idx = new int[N];

            public float Cost(float[] x, Vector3[] t)
            {
                for (int i = 0; i < N; i++) pose.muscles[idx[i]] = x[i];
                handler.SetHumanPose(ref pose);
                Vector3 sh = upper.position, el = lower.position, wr = hand.position;
                Vector3 ew = (wr - sh) - (t[2] - t[0]), ee = (el - sh) - (t[1] - t[0]);
                float c = ew.sqrMagnitude * 4f + ee.sqrMagnitude;
                // hand orientation: knuckle points relative to the wrist
                c += ((index.position - wr) - (t[3] - t[2])).sqrMagnitude * 0f;
                c += ((middle.position - wr) - (t[4] - t[2])).sqrMagnitude * 0f;
                c += ((little.position - wr) - (t[5] - t[2])).sqrMagnitude * 0f;
                return c + 0.002f * (x[2] * x[2] + x[4] * x[4]) + 0.05f * (x[5] * x[5] + x[6] * x[6]);
            }

            public float[] Solve(Vector3[] t, float[] start)
            {
                var best = (float[])start.Clone();
                float bestCost = Cost(best, t);
                var rng = new System.Random(7);
                for (int restart = 0; restart < 14; restart++)
                {
                    var x = (float[])best.Clone();
                    if (restart > 0) for (int i = 0; i < N; i++) x[i] = Mathf.Clamp(x[i] + (float)(rng.NextDouble() - 0.5) * (restart < 7 ? 1.2f : 2.4f), -1.6f, 1.6f);
                    float c = Cost(x, t);
                    float step = 0.4f;
                    while (step > 0.0015f)
                    {
                        bool improved = false;
                        for (int i = 0; i < N; i++)
                            foreach (float s in new[] { step, -step })
                            {
                                float old = x[i]; x[i] = Mathf.Clamp(old + s, -1.6f, 1.6f);
                                float c2 = Cost(x, t);
                                if (c2 < c) { c = c2; improved = true; } else x[i] = old;
                            }
                        if (!improved) step *= 0.5f;
                    }
                    if (c < bestCost) { bestCost = c; best = (float[])x.Clone(); }
                }
                Debug.Log("[ArmFit]   fit cost " + bestCost.ToString("F5") + " muscles " + string.Join(",", best.Select(v => v.ToString("F2")).ToArray()));
                return best;
            }
        }

        static Solver MakeSolver(Animator anim, string side)
        {
            var s = new Solver { anim = anim };
            s.handler = new HumanPoseHandler(anim.avatar, anim.transform);
            s.handler.GetHumanPose(ref s.pose);
            bool left = side == "Left";
            s.upper = anim.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            s.lower = anim.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            s.hand = anim.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            s.index = anim.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            s.middle = anim.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            s.little = anim.GetBoneTransform(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            for (int i = 0; i < N; i++) s.idx[i] = MuscleIndex(side + " " + Suffix[i]);
            return s;
        }

        [MenuItem("PIX Football/Assets/Coaches/Fit Arm Clips")]
        public static void BuildFittedClips()
        {
            string json = RefCoachBuilder.CoachDir + "/Coach_ArmTargets.json";
            if (!File.Exists(json)) { Debug.LogWarning("[ArmFit] missing " + json); return; }
            var file = JsonUtility.FromJson<TargetFile>(File.ReadAllText(json));
            MaterialFactory.EnsureFolder(OutDir);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.Root + "/Prefabs/PIX_Coach_A.prefab");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var anim = inst.GetComponent<Animator>();
            var left = MakeSolver(anim, "Left");
            var right = MakeSolver(anim, "Right");
            var bindMuscles = (float[])left.pose.muscles.Clone();
            string fbx = RefCoachBuilder.CoachDir + "/Coach_Anims.fbx";
            var all = AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().ToList();
            var armMuscles = new HashSet<string>();
            foreach (string side in new[] { "Left", "Right" }) foreach (string s in Suffix) armMuscles.Add(side + " " + s);

            foreach (var ct in file.clips)
            {
                if (ct.name == "ref_spot_ball" || ct.name == "ref_penalty_flag") continue;   // large torso lean / throw: the FBX version is fine
                var src = all.FirstOrDefault(c => c.name == ct.name);
                if (src == null) { Debug.LogWarning("[ArmFit] no source clip " + ct.name); continue; }
                Debug.Log("[ArmFit] " + ct.name);
                var nc = new AnimationClip { name = ct.name, frameRate = 30f };
                foreach (var b in AnimationUtility.GetCurveBindings(src))
                    if (!armMuscles.Contains(b.propertyName)) AnimationUtility.SetEditorCurve(nc, b, AnimationUtility.GetEditorCurve(src, b));
                var curves = new Dictionary<string, AnimationCurve>();
                foreach (var n in armMuscles) curves[n] = new AnimationCurve();
                float[] prevL = new float[N], prevR = new float[N];
                for (int i = 0; i < N; i++) { prevL[i] = bindMuscles[left.idx[i]]; prevR[i] = bindMuscles[right.idx[i]]; }
                foreach (var k in ct.keys)
                {
                    float t = (k.frame - 1) / 30f;
                    // body muscles of this instant from the FBX clip (spine / head sway), so the arm fit sees the same torso
                    for (int m = 0; m < HumanTrait.MuscleCount; m++)
                    {
                        var cv = AnimationUtility.GetEditorCurve(src, EditorCurveBinding.FloatCurve("", typeof(Animator), HumanTrait.MuscleName[m]));
                        float v = cv != null ? cv.Evaluate(t) : bindMuscles[m];
                        left.pose.muscles[m] = v; right.pose.muscles[m] = v;
                    }
                    var xl = left.Solve(Targets(k.l), prevL);
                    for (int i = 0; i < N; i++) right.pose.muscles[right.idx[i]] = prevR[i];
                    var xr = right.Solve(Targets(k.r), prevR);
                    prevL = xl; prevR = xr;
                    for (int i = 0; i < N; i++) { curves["Left " + Suffix[i]].AddKey(t, xl[i]); curves["Right " + Suffix[i]].AddKey(t, xr[i]); }
                }
                bool loop = ct.name.Contains("idle") || ct.name.Contains("clap");
                foreach (var kv in curves)
                {
                    if (loop && kv.Value.length > 1)
                    {
                        var first = kv.Value.keys[0]; var last = kv.Value.keys[kv.Value.length - 1];
                        kv.Value.MoveKey(kv.Value.length - 1, new Keyframe(last.time, first.value));
                    }
                    for (int i = 0; i < kv.Value.length; i++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(kv.Value, i, AnimationUtility.TangentMode.ClampedAuto);
                        AnimationUtility.SetKeyRightTangentMode(kv.Value, i, AnimationUtility.TangentMode.ClampedAuto);
                    }
                    AnimationUtility.SetEditorCurve(nc, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), kv.Value);
                }
                var st = AnimationUtility.GetAnimationClipSettings(nc);
                st.loopTime = loop; st.loopBlend = loop;
                AnimationUtility.SetAnimationClipSettings(nc, st);
                string path = OutDir + "/" + ct.name + ".anim";
                if (AssetDatabase.LoadAssetAtPath<AnimationClip>(path) != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(nc, path);
            }
            UnityEngine.Object.DestroyImmediate(inst);
            AssetDatabase.SaveAssets();
        }
    }
}
