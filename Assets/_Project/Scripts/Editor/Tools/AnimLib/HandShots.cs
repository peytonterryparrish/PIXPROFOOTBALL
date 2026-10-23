using System.Collections.Generic;
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
    /// <summary>v0.10.0 ANIMLIB: close-up renders of the authored hands (catch, carry grip, tackle wrap, stance hand on the turf, celebration, throw) on the real player, one PNG. Output D:/PIXFootball/Renders/animlib_v0100/hands_closeup.png.</summary>
    public static class HandShots
    {
        // clip, seconds offset (added to the tag sync time when "sync" is true), label
        static readonly object[][] Cases =
        {
            new object[] { "CatchNormalBackCHigh", -0.14f, true, "catch reach (open)" },
            new object[] { "CatchNormalBackCHigh", 0.12f, true, "catch (grip)" },
            new object[] { "BGS__BGS_Run07_F_Loop_BC", 0.20f, false, "carry grip" },
            new object[] { "TackleWrapBackCHighDmo_A", -0.20f, true, "tackle reach (open)" },
            new object[] { "TackleWrapBackCHighDmo_A", 0.25f, true, "tackle wrap" },
            new object[] { "DlineIdle3ptR", 1.0f, false, "hand on turf" },
            new object[] { "CelebrationTouchdownBreakdance", 1.2f, false, "celebration" },
            new object[] { "BONES_QB_pass_quick_bullet_001_s01", -0.30f, true, "throw hand" },
        };

        public static void Run()
        {
            BatchWatchdog.Arm(25);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var volGo = new GameObject("ShotVolume");
            var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var exp = prof.Add<Exposure>(true); exp.mode.Override(ExposureMode.Fixed); exp.fixedExposure.Override(12.5f);
            var env = prof.Add<VisualEnvironment>(true); env.skyType.Override((int)SkyType.Gradient); env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var sky = prof.Add<GradientSky>(true);
            sky.top.Override(new Color(0.35f, 0.55f, 0.95f)); sky.middle.Override(new Color(0.75f, 0.82f, 0.9f)); sky.bottom.Override(new Color(0.45f, 0.45f, 0.42f));
            var dpl = prof.Add<DiffusionProfileList>(true); dpl.diffusionProfiles.overrideState = true; dpl.diffusionProfiles.value = PlayerPrefabBuilder.FindDiffusionProfiles();
            vol.sharedProfile = prof;
            var lg = new GameObject("Sun"); var sun = lg.AddComponent<Light>(); sun.type = LightType.Directional;
            lg.AddComponent<HDAdditionalLightData>(); sun.lightUnit = LightUnit.Lux; sun.intensity = 90000f;
            lg.transform.rotation = Quaternion.Euler(42f, 250f, 0f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = new Vector3(3f, 1f, 3f);
            floor.GetComponent<Renderer>().sharedMaterial = MaterialFactory.CreateSolid(MaterialFactory.MatDir + "/PIX_ShotFloor.mat", new Color(0.25f, 0.28f, 0.25f), 0f, 0.1f);

            var tags = ClipTagTable.Load();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.Root + "/Prefabs/PIX_Player_Medium.prefab");
            var cells = new List<Texture2D>();
            const int S = 640;
            foreach (var cs in Cases)
            {
                string cn = (string)cs[0];
                var clip = StickSheet.FindClip(cn); if (clip == null) { Debug.LogWarning("[HandShots] missing " + cn); continue; }
                float t = (float)cs[1];
                if ((bool)cs[2])
                {
                    ClipTagTable.Entry e; float st = tags != null && tags.TryGet(clip.name, out e) && e.syncT > 0f ? e.syncT : clip.length * 0.4f;
                    t = st + t;
                }
                t = Mathf.Clamp(t, 0f, clip.length - 0.01f);
                var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                g.transform.position = Vector3.zero; g.transform.rotation = Quaternion.Euler(0f, ClipSheet.ModelYaw, 0f);
                Pose(g, clip, t);
                var an = g.GetComponent<Animator>();
                Vector3 a = an.GetBoneTransform(HumanBodyBones.LeftHand).position, b = an.GetBoneTransform(HumanBodyBones.RightHand).position;
                Vector3 mid = 0.5f * (a + b);
                float span = Mathf.Max(0.55f, Vector3.Distance(a, b) + 0.55f);
                var cam = new GameObject("Cam").AddComponent<Camera>(); cam.gameObject.AddComponent<HDAdditionalCameraData>();
                cam.fieldOfView = 24f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f); cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f;
                float dist = span * 0.5f / Mathf.Tan(12f * Mathf.Deg2Rad) * 1.2f;
                Vector3 chest = an.GetBoneTransform(HumanBodyBones.Chest).position; Vector3 out3 = mid - chest; out3.y = 0f;
                Vector3 fwd = g.transform.forward; if (out3.sqrMagnitude < 0.01f) out3 = fwd;
                // look at the hands from in front of the player and slightly above
                cam.transform.position = mid + (fwd * 0.9f + Vector3.up * 0.35f + Vector3.Cross(Vector3.up, fwd) * 0.25f).normalized * dist;
                cam.transform.LookAt(mid);
                cam.aspect = 1f;
                cells.Add(Render(cam, S, S));
                Object.DestroyImmediate(cam.gameObject); Object.DestroyImmediate(g);
                Debug.Log("[HandShots] " + cs[3] + " " + clip.name + " t=" + t.ToString("0.00"));
            }
            if (cells.Count > 0)
            {
                int cols = 4, rows = (cells.Count + cols - 1) / cols;
                var sheet = new Texture2D(cols * S, rows * S, TextureFormat.RGB24, false);
                for (int i = 0; i < cells.Count; i++) { int r = i / cols, c = i % cols; sheet.SetPixels(c * S, (rows - 1 - r) * S, S, S, cells[i].GetPixels()); Object.DestroyImmediate(cells[i]); }
                string dir = Directory.Exists("D:/") ? "D:/PIXFootball/Renders/animlib_v0100" : "Logs/animlib_sheets";
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(dir + "/hands_closeup.png", sheet.EncodeToPNG());
                Debug.Log("[HandShots] wrote " + dir + "/hands_closeup.png");
            }
            AnimLibBatch.Done();
        }

        static Texture2D Render(Camera cam, int w, int h)
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

        static void Pose(GameObject go, AnimationClip clip, float t)
        {
            var anim = go.GetComponent<Animator>();
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
