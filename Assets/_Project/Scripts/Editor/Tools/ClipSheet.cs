using System.Collections.Generic;
using System.IO;
using System.Linq;
using PixFootball.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>Renders a row of posed players over a clip's timeline (used to pick trim points). Input: Logs/clipsheet_in.txt lines "clipName|t0|t1|frames" (or key:idle|...).</summary>
    public static class ClipSheet
    {
        public const float ModelYaw = -90f;
        public static void Run()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
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
            var hd = lg.AddComponent<HDAdditionalLightData>(); sun.lightUnit = LightUnit.Lux; sun.intensity = 90000f;
            lg.transform.rotation = Quaternion.Euler(42f, 250f, 0f);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.localScale = new Vector3(1.5f, 1f, 3f);
            floor.GetComponent<Renderer>().sharedMaterial = MaterialFactory.CreateSolid(MaterialFactory.MatDir + "/PIX_ShotFloor.mat", new Color(0.25f, 0.28f, 0.25f), 0f, 0.1f);

            var lib = ArtLibrary.Load();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.Root + "/Prefabs/PIX_Player_Medium.prefab");
            string[] lines = File.ReadAllLines("Logs/clipsheet_in.txt");
            foreach (string raw in lines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var parts = raw.Split('|');
                AnimationClip clip = parts[0].StartsWith("key:") ? lib.Clip(parts[0].Substring(4)) : AnimatorControllerBuilder.FindClipByName(parts[0]);
                if (clip == null) { Debug.LogWarning("[ClipSheet] no clip " + parts[0]); continue; }
                float t0 = float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), t1 = float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
                int n = int.Parse(parts[3]);
                bool mirror = parts.Length > 4 && parts[4] == "m";
                if (t1 <= 0f) t1 = clip.length;
                var made = new List<GameObject>();
                for (int i = 0; i < n; i++)
                {
                    var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    g.transform.position = new Vector3(0f, 0f, (i - (n - 1) * 0.5f) * 1.7f);
                    g.transform.rotation = Quaternion.Euler(0f, ModelYaw, 0f);
                    float t = Mathf.Lerp(t0, t1, n == 1 ? 0f : i / (float)(n - 1));
                    Pose(g, clip, t);
                    made.Add(g);
                }
                float width = n * 1.7f;
                var cam = new GameObject("Cam").AddComponent<Camera>();
                cam.gameObject.AddComponent<HDAdditionalCameraData>();
                cam.fieldOfView = 24f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
                float h = 2.4f; float aspect = (n * 1.7f) / h;
                int W = 2400, H = Mathf.Clamp(Mathf.RoundToInt(W / aspect), 260, 700);
                cam.aspect = W / (float)H;
                float dist = (h * 0.5f) / Mathf.Tan(12f * Mathf.Deg2Rad) * Mathf.Max(1f, (width / h) / ((float)W / H));
                cam.transform.position = new Vector3(dist + 1f, 1.0f, 0f); cam.transform.rotation = Quaternion.Euler(0f, -90f, 0f);
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
                var tex = Render(cam, W, H);
                Directory.CreateDirectory("Docs/Screens");
                string outName = "Docs/Screens/clipsheet_" + parts[1] + "_" + parts[0].Replace(':', '_') + ".png";
                File.WriteAllBytes(outName, tex.EncodeToPNG());
                Debug.Log("[ClipSheet] " + outName + " len=" + clip.length.ToString("0.00") + " t=" + t0 + ".." + t1 + " avgSpeed=" + clip.averageSpeed);
                Object.DestroyImmediate(tex); Object.DestroyImmediate(cam.gameObject);
                foreach (var g in made) Object.DestroyImmediate(g);
            }
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
            PixFootball.EditorTools.AnimPlay.PlayClip(anim, clip, out graph);
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
