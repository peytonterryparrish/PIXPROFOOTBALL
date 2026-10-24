using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using PixFootball.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11.0 ANIMLIB: hand pose lab. One pose of a real take, the finger muscles overridden by a list of presets, a close-up of one hand from two directions per preset (real HDRP player), one PNG.
    /// Presets come from Logs/animlib/handlab.txt: "label|clip|seconds|side|p0 p1 p2 p3 m0..m3 d0..d3 spread t1 t2 t3 tsp" (17 numbers: proximal / middle / distal "Stretched" of index..little, index spread, thumb 1-3 stretched, thumb spread). Output D:/PIXFootball/Renders/animlib_v0110/<-pixTag>.png
    /// </summary>
    public static class HandLab
    {
        static readonly string[] Fingers = { "Index", "Middle", "Ring", "Little" };

        public static void ApplyHand(PoseSeq seq, string side, float[] p)
        {
            for (int fi = 0; fi < 4; fi++)
                for (int j = 0; j < 3; j++)
                {
                    int mi = ClipQa.MuscleIndex(side + " " + Fingers[fi] + " " + (j + 1) + " Stretched"); if (mi < 0) continue;
                    for (int i = 0; i < seq.N; i++) seq.M[mi][i] = p[j * 4 + fi];
                }
            string[] spreadNames = { "Index Spread", "Middle Spread", "Ring Spread", "Little Spread" };
            float[] spreadK = { 1f, 0.2f, -0.5f, -0.9f };
            for (int fi = 0; fi < 4; fi++) { int ms = ClipQa.MuscleIndex(side + " " + spreadNames[fi]); if (ms < 0) continue; for (int i = 0; i < seq.N; i++) seq.M[ms][i] = p[12] * spreadK[fi]; }
            string[] th = { "Thumb 1 Stretched", "Thumb 2 Stretched", "Thumb 3 Stretched", "Thumb Spread" };
            for (int t = 0; t < 4; t++) { int mt = ClipQa.MuscleIndex(side + " " + th[t]); if (mt < 0) continue; for (int i = 0; i < seq.N; i++) seq.M[mt][i] = p[13 + t]; }
        }

        public static void Run()
        {
            BatchWatchdog.Arm(25);
            string tag = "handlab";
            var a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-pixTag") tag = a[i + 1];
            var lines = AnimLibV11.Names("Logs/animlib/" + tag + ".txt");
            Setup();
            var tags = ClipTagTable.Load();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.Root + "/Prefabs/PIX_Player_Medium.prefab");
            var cells = new List<Texture2D>();
            const int S = 512;
            foreach (string ln in lines)
            {
                var f = ln.Split('|'); if (f.Length < 5) continue;
                var clip = StickSheet.FindClip(f[1]); if (clip == null) { Debug.LogWarning("[HandLab] missing " + f[1]); continue; }
                float t = float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture);
                if (f[2].StartsWith("s")) { }
                string side = f[3] == "L" ? "Left" : "Right";
                var seq = PoseSeq.FromClip(clip, 0f, clip.length, 30f); seq.Name = clip.name;
                var vals = f[4].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); var p = new float[17];
                for (int i = 0; i < 17 && i < vals.Length; i++) p[i] = float.Parse(vals[i], System.Globalization.CultureInfo.InvariantCulture);
                if (vals.Length >= 17) { ApplyHand(seq, "Left", p); ApplyHand(seq, "Right", p); }
                var c2 = seq.ToClip("lab");
                for (int view = 0; view < 2; view++)
                {
                    var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    g.transform.position = Vector3.zero; g.transform.rotation = Quaternion.Euler(0f, ClipSheet.ModelYaw, 0f);
                    Pose(g, c2, Mathf.Clamp(t, 0f, clip.length - 0.01f));
                    var an = g.GetComponent<Animator>();
                    Vector3 hand = an.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand).position;
                    Vector3 tip = an.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal).position;
                    Vector3 centre = Vector3.Lerp(hand, tip, 0.55f); if (view == 0) Debug.Log("[HandLab] " + f[0] + " tip-hand " + Vector3.Distance(hand, tip).ToString("0.000") + " thumbtip " + Vector3.Distance(hand, an.GetBoneTransform(side == "Left" ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal).position).ToString("0.000") + " muscle R index1 in clip " + seq.M[ClipQa.MuscleIndex("Right Index 1 Stretched")][5].ToString("0.00"));
                    var cam = new GameObject("Cam").AddComponent<Camera>(); cam.gameObject.AddComponent<HDAdditionalCameraData>();
                    cam.fieldOfView = 22f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f); cam.nearClipPlane = 0.03f; cam.farClipPlane = 30f;
                    Vector3 fwd = g.transform.forward; Vector3 right = Vector3.Cross(Vector3.up, fwd);
                    Vector3 dir = view == 0 ? (fwd * 0.9f + Vector3.up * 0.35f + right * 0.25f) : (Vector3.up * 0.8f + fwd * 0.3f + (side == "Left" ? -right : right) * 0.5f);
                    cam.transform.position = centre + dir.normalized * 0.62f; cam.transform.LookAt(centre); cam.aspect = 1f;
                    cells.Add(Render(cam, S, S));
                    UnityEngine.Object.DestroyImmediate(cam.gameObject); UnityEngine.Object.DestroyImmediate(g);
                }
                UnityEngine.Object.DestroyImmediate(c2);
                Debug.Log("[HandLab] " + f[0]);
            }
            if (cells.Count > 0)
            {
                int cols = Mathf.Min(cells.Count / 2, 6) * 2 / 2; cols = Mathf.Max(1, cols); int pairs = cells.Count / 2; int pcols = Mathf.Min(pairs, 4); int rows = ((pairs + pcols - 1) / pcols) * 2;
                var sheet = new Texture2D(pcols * S, rows * S, TextureFormat.RGB24, false);
                for (int i = 0; i < pairs; i++)
                {
                    int pr = i / pcols, pc = i % pcols;
                    for (int v = 0; v < 2; v++) { int row = pr * 2 + v; sheet.SetPixels(pc * S, (rows - 1 - row) * S, S, S, cells[i * 2 + v].GetPixels()); }
                }
                foreach (var c in cells) UnityEngine.Object.DestroyImmediate(c);
                string dir2 = Directory.Exists("D:/") ? "D:/PIXFootball/Renders/animlib_v0110" : "Logs/animlib_sheets";
                Directory.CreateDirectory(dir2);
                File.WriteAllBytes(dir2 + "/" + tag + ".png", sheet.EncodeToPNG());
                Debug.Log("[HandLab] wrote " + dir2 + "/" + tag + ".png");
            }
            AnimLibBatch.Done();
        }

        public static void Diag()
        {
            BatchWatchdog.Arm(10);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.Root + "/Prefabs/PIX_Player_Medium.prefab");
            var clip = StickSheet.FindClip("CatchNormalBackCHigh");
            var seq = PoseSeq.FromClip(clip, 0f, clip.length, 30f);
            foreach (float curl in new[] { 0.4f, -1f })
            {
                var s = seq.Clone(); float[] p = new float[17]; for (int i = 0; i < 12; i++) p[i] = curl; HandLab.ApplyHand(s, "Left", p); HandLab.ApplyHand(s, "Right", p);
                var c2 = s.ToClip("d");
                var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab); var an = g.GetComponent<Animator>();
                Debug.Log("[Diag] isHuman " + an.isHuman + " avatar valid " + (an.avatar != null && an.avatar.isValid) + " controller " + (an.runtimeAnimatorController != null) + " hasTransformHierarchy " + an.hasTransformHierarchy);
                var h = an.GetBoneTransform(HumanBodyBones.RightHand); var tip = an.GetBoneTransform(HumanBodyBones.RightMiddleDistal);
                Debug.Log("[Diag] bones: hand " + (h != null) + " tip " + (tip != null) + " bind dist " + Vector3.Distance(h.position, tip.position).ToString("0.000"));
                PlayableGraph graph; AnimPlay.PlayClip(an, c2, out graph); graph.Evaluate(0f); graph.Evaluate(0.5f);
                Debug.Log("[Diag] graph curl " + curl + " dist " + Vector3.Distance(h.position, tip.position).ToString("0.000"));
                graph.Destroy();
                // handler path
                var hp = new HumanPoseHandler(an.avatar, an.transform); var pose = new HumanPose(); hp.GetHumanPose(ref pose);
                for (int m = 0; m < pose.muscles.Length; m++) pose.muscles[m] = s.M[m][15];
                pose.bodyPosition = new Vector3(0, 1f, 0); pose.bodyRotation = Quaternion.identity; hp.SetHumanPose(ref pose);
                Debug.Log("[Diag] handler curl " + curl + " dist " + Vector3.Distance(h.position, tip.position).ToString("0.000") + " muscle index1 " + pose.muscles[ClipQa.MuscleIndex("Right Index 1 Stretched")]);
                UnityEngine.Object.DestroyImmediate(g); UnityEngine.Object.DestroyImmediate(c2);
            }
            AnimLibBatch.Done();
        }

        /// <summary>Do the raw takes carry finger data? Per animation folder: clips sampled, clips with finger curves, clips whose finger curves actually move (range over 0.05).</summary>
        public static void FingerProbe()
        {
            BatchWatchdog.Arm(20);
            var byFolder = new Dictionary<string, int[]>();
            int k = 0;
            foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Licensed/DraftFever/Animations", "Assets/_Licensed/Mixamo" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g); if (path.Contains("/Clean/") || path.EndsWith("Player_A_01.fbx")) continue;
                if ((k++ % 6) != 0) continue;
                string folder = Path.GetDirectoryName(path).Replace("\\", "/"); folder = folder.Substring(folder.IndexOf("Animations") >= 0 ? folder.IndexOf("Animations") : 0);
                int[] a; if (!byFolder.TryGetValue(folder, out a)) byFolder[folder] = a = new int[3];
                foreach (var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (c.name.StartsWith("__preview")) continue;
                    a[0]++; bool has = false, moves = false;
                    foreach (var b in AnimationUtility.GetCurveBindings(c))
                    {
                        if (b.type != typeof(Animator) || !b.propertyName.Contains("Hand.")) continue;
                        has = true; var cv = AnimationUtility.GetEditorCurve(c, b); float mn = 9f, mx = -9f; foreach (var kf in cv.keys) { mn = Mathf.Min(mn, kf.value); mx = Mathf.Max(mx, kf.value); }
                        if (mx - mn > 0.05f) { moves = true; break; }
                    }
                    if (has) a[1]++; if (moves) a[2]++;
                    break;      // one clip per file is enough for a folder statistic
                }
            }
            foreach (var kv in byFolder.OrderBy(x => x.Key)) Debug.Log("[FingerProbe] " + kv.Key + " files " + kv.Value[0] + " withFingerCurves " + kv.Value[1] + " moving " + kv.Value[2]);
            AnimLibBatch.Done();
        }

        public static void Setup()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var volGo = new GameObject("ShotVolume");
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var exp = prof.Add<Exposure>(true); exp.mode.Override(ExposureMode.Fixed); exp.fixedExposure.Override(12.5f);
            var env = prof.Add<VisualEnvironment>(true); env.skyType.Override((int)SkyType.Gradient); env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var sky = prof.Add<GradientSky>(true);
            sky.top.Override(new Color(0.5f, 0.65f, 0.95f)); sky.middle.Override(new Color(0.85f, 0.88f, 0.92f)); sky.bottom.Override(new Color(0.6f, 0.6f, 0.58f));
            var dpl = prof.Add<DiffusionProfileList>(true); dpl.diffusionProfiles.overrideState = true; dpl.diffusionProfiles.value = PlayerPrefabBuilder.FindDiffusionProfiles();
            vol.sharedProfile = prof;
            var lg = new GameObject("Sun"); var sun = lg.AddComponent<Light>(); sun.type = LightType.Directional;
            lg.AddComponent<HDAdditionalLightData>(); sun.lightUnit = LightUnit.Lux; sun.intensity = 90000f;
            lg.transform.rotation = Quaternion.Euler(55f, 215f, 0f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = new Vector3(3f, 1f, 3f);
            floor.GetComponent<Renderer>().sharedMaterial = MaterialFactory.CreateSolid(MaterialFactory.MatDir + "/PIX_ShotFloor.mat", new Color(0.25f, 0.28f, 0.25f), 0f, 0.1f);
        }

        public static Texture2D Render(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            for (int i = 0; i < 3; i++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null; rt.Release();
            return tex;
        }

        public static void Pose(GameObject go, AnimationClip clip, float t)
        {
            var anim = go.GetComponent<Animator>(); anim.applyRootMotion = true; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            PlayableGraph graph;
            AnimPlay.PlayClip(anim, clip, out graph);
            graph.Evaluate(0f); graph.Evaluate(t);
            graph.Destroy();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!smr.enabled) continue;
                var baked = new Mesh(); smr.BakeMesh(baked, false);
                var child = new GameObject("baked_" + smr.name);
                child.transform.SetParent(smr.transform, false);
                child.AddComponent<MeshFilter>().sharedMesh = baked;
                child.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                smr.enabled = false;
            }
        }
    }
}
