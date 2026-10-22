using System.Collections.Generic;
using System.IO;
using System.Linq;
using PixFootball.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace PixFootball.EditorTools
{
    /// <summary>Renders prefabs to PNG (Docs/Screens) from an editor script; run WITHOUT -nographics.</summary>
    public static class AssetScreenshots
    {
        const string OutDir = "Docs/Screens";

        internal static Scene NewStudio(float exposure, out Light sun)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // volume: fixed exposure + gradient sky
            var volGo = new GameObject("ShotVolume");
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            var exp = prof.Add<Exposure>(true);
            exp.mode.Override(ExposureMode.Fixed);
            exp.fixedExposure.Override(exposure);
            var env = prof.Add<VisualEnvironment>(true);
            env.skyType.Override((int)SkyType.Gradient);
            env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var sky = prof.Add<GradientSky>(true);
            sky.top.Override(new Color(0.35f, 0.55f, 0.95f));
            sky.middle.Override(new Color(0.75f, 0.82f, 0.9f));
            sky.bottom.Override(new Color(0.45f, 0.45f, 0.42f));
            sky.gradientDiffusion.Override(2f);
            sky.multiplier.Override(1f);
            var dpl = prof.Add<DiffusionProfileList>(true);
            dpl.diffusionProfiles.overrideState = true;
            dpl.diffusionProfiles.value = PlayerPrefabBuilder.FindDiffusionProfiles();
            vol.sharedProfile = prof;

            var lg = new GameObject("Sun");
            sun = lg.AddComponent<Light>();
            sun.type = LightType.Directional;
            var hd = lg.AddComponent<HDAdditionalLightData>();
            hd.SetIntensity(90000f, LightUnit.Lux);
            lg.transform.rotation = Quaternion.Euler(48f, 150f, 0f);
            return scene;
        }

        internal static Texture2D Render(Camera cam, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            for (int i = 0; i < 3; i++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            rt.Release();
            return tex;
        }

        internal static Camera MakeCamera(Vector3 pos, Vector3 lookAt, float fov = 30f)
        {
            var go = new GameObject("ShotCam");
            var cam = go.AddComponent<Camera>();
            go.AddComponent<HDAdditionalCameraData>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1500f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
            go.transform.position = pos;
            go.transform.LookAt(lookAt);
            return cam;
        }

        internal static void Save(Texture2D t, string name)
        {
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(OutDir + "/" + name + ".png", t.EncodeToPNG());
            Object.DestroyImmediate(t);
            Debug.Log("[AssetScreenshots] wrote " + OutDir + "/" + name + ".png");
        }

        internal static void Pose(GameObject go, AnimationClip clip, float t)
        {
            var anim = go.GetComponent<Animator>();
            if (anim == null || clip == null) return;
            PlayableGraph graph;
            PixFootball.EditorTools.AnimPlay.PlayClip(anim, clip, out graph);
            graph.Evaluate(0f);
            graph.Evaluate(t);
            graph.Destroy();
            // edit-mode skinning does not refresh during manual renders: bake the posed meshes and show those instead
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!smr.enabled) continue;
                var baked = new Mesh();
                smr.BakeMesh(baked, false);
                var child = new GameObject("baked_" + smr.name);
                child.transform.SetParent(smr.transform, false);
                // Blender-exported mesh nodes carry a 100x scale (and -90 degree rotation) that BakeMesh output expects to inherit only partly: cancel the scale
                var ls = smr.transform.lossyScale; child.transform.localScale = new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
                child.AddComponent<MeshFilter>().sharedMesh = baked;
                child.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                smr.enabled = false;
            }
        }

        [MenuItem("PIX Football/Assets/Screenshots/Players")]
        public static void ShootPlayers()
        {
            Light sun;
            NewStudio(12.5f, out sun);
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            string[] sizes = { "Small", "Medium", "Large" };
            var instances = new List<GameObject>();
            // floor
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(2, 1, 1);
            floor.GetComponent<Renderer>().sharedMaterial = MaterialFactory.CreateSolid(MaterialFactory.MatDir + "/PIX_ShotFloor.mat", new Color(0.25f, 0.28f, 0.25f), 0f, 0.1f);
            for (int i = 0; i < 3; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.Root + "/Prefabs/PIX_Player_" + sizes[i] + ".prefab");
                if (prefab == null) { Debug.LogWarning("no prefab " + sizes[i]); continue; }
                var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                g.transform.position = new Vector3((i - 1) * 1.6f, 0, 0);
                instances.Add(g);
            }
            // T-pose shot (bind pose)
            var cam = MakeCamera(new Vector3(0, 1.1f, 7.5f), new Vector3(0, 0.95f, 0), 28f);
            Save(Render(cam, 1600, 900), "players_bind_pose");
            // posed shot: idle clip from the library (proves humanoid retargeting)
            if (lib != null)
            {
                var idle = lib.Clip(ArtKeys.Idle) ?? lib.Clip(ArtKeys.QbStanceShotgun);
                var run = lib.Clip(ArtKeys.Sprint);
                var catchHigh = lib.Clip(ArtKeys.CatchHigh);
                var clips = new[] { idle, run, catchHigh };
                for (int i = 0; i < instances.Count; i++) Pose(instances[i], clips[i] ?? idle, (clips[i] != null ? Mathf.Min(0.35f, clips[i].length * 0.5f) : 0f));
                cam.transform.position = new Vector3(0, 1.1f, 6.0f); cam.transform.LookAt(new Vector3(0, 0.95f, 0));
                Save(Render(cam, 1600, 900), "players_posed");
            }
            // close-up of the medium player (helmet fit)
            if (instances.Count > 1)
            {
                var head = instances[1].GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
                cam.fieldOfView = 20f;
                cam.transform.position = head.position + new Vector3(0.6f, 0.15f, 1.5f);
                cam.transform.LookAt(head.position);
                Save(Render(cam, 1280, 720), "player_medium_helmet_closeup");
            }
            Object.DestroyImmediate(cam.gameObject);
        }

        public static void ShootBonesClips()
        {
            Light sun;
            NewStudio(12.5f, out sun);
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            if (lib == null) return;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(3, 1, 1);
            floor.GetComponent<Renderer>().sharedMaterial = MaterialFactory.CreateSolid(MaterialFactory.MatDir + "/PIX_ShotFloor.mat", new Color(0.25f, 0.28f, 0.25f), 0f, 0.1f);
            string[][] sets = { new[] { ArtKeys.QbDrop5, ArtKeys.QbThrowTouch, ArtKeys.OlRunBlock }, new[] { ArtKeys.QbSnapShotgun, ArtKeys.ShuffleToSprint, ArtKeys.StiffArm }, new[] { ArtKeys.TackleWrap, ArtKeys.DlRushSwim, ArtKeys.CelebrateTd } };
            for (int s = 0; s < sets.Length; s++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.Root + "/Prefabs/PIX_Player_Medium.prefab");
                var made = new List<GameObject>();
                var cam = MakeCamera(new Vector3(0, 1.2f, 7.5f), new Vector3(0, 1.0f, 0), 30f);
                for (int i = 0; i < 3; i++)
                {
                    var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    g.transform.position = new Vector3((i - 1) * 2.0f, 0, 0);
                    var clip = lib.Clip(sets[s][i]);
                    Pose(g, clip, clip != null ? clip.length * 0.5f : 0f);
                    made.Add(g);
                    Debug.Log("[AssetScreenshots] bones pose " + sets[s][i] + " -> " + (clip != null ? clip.name + " len " + clip.length : "MISSING"));
                }
                Save(Render(cam, 1600, 900), "bones_clips_" + (s + 1));
                foreach (var g in made) Object.DestroyImmediate(g);
                Object.DestroyImmediate(cam.gameObject);
            }
        }

        public static void ShootExtras()
        {
            Light sun;
            NewStudio(12.5f, out sun);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(3, 1, 1);
            floor.GetComponent<Renderer>().sharedMaterial = MaterialFactory.CreateSolid(MaterialFactory.MatDir + "/PIX_ShotFloor.mat", new Color(0.25f, 0.28f, 0.25f), 0f, 0.1f);
            string[] names = { "PIX_Referee", "PIX_Crowd_Male01", "PIX_Crowd_Female01", "PIX_Crowd_MaleLarge", "PIX_Prop_goalPost", "PIX_Prop_footballBall", "PIX_Prop_stadiumBench", "PIX_Prop_endZonePylon", "PIX_Prop_hydrationStation" };
            float x = -7f;
            var keep = new List<GameObject>();
            foreach (string n in names)
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.Root + "/Prefabs/" + n + ".prefab");
                if (p == null) { Debug.LogWarning("missing " + n); continue; }
                var g = (GameObject)PrefabUtility.InstantiatePrefab(p);
                float scale = n.Contains("goalPost") ? 0.25f : n.Contains("football") ? 3f : n.Contains("Pylon") ? 2.5f : 1f;
                g.transform.localScale = Vector3.one * scale;
                g.transform.position = new Vector3(x, n.Contains("football") ? 0.3f : 0f, 0);
                x += 1.7f;
                keep.Add(g);
            }
            var cam = MakeCamera(new Vector3(0, 1.6f, 11f), new Vector3(0, 0.9f, 0), 38f);
            Save(Render(cam, 1800, 700), "extras_referee_crowd_props");
            Object.DestroyImmediate(cam.gameObject);
        }

        [MenuItem("PIX Football/Assets/Screenshots/Stadiums")]
        public static void ShootStadiums()
        {
            foreach (string n in new[] { "PIX_Stadium_Pro", "PIX_Stadium_College" })
            {
                Light sun;
                NewStudio(13f, out sun);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.Root + "/Prefabs/" + n + ".prefab");
                if (prefab == null) { Debug.LogWarning("no prefab " + n); continue; }
                var g = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                var cam = MakeCamera(new Vector3(0, 420, -0.01f), Vector3.zero, 30f);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                cam.transform.position = new Vector3(0, 380f, 0f);
                cam.fieldOfView = 40f;
                Save(Render(cam, 1600, 1600), n + "_top");
                cam.transform.position = new Vector3(-30, 14, -70);
                cam.transform.LookAt(new Vector3(0, 1, 5));
                cam.fieldOfView = 55f;
                Save(Render(cam, 1600, 900), n + "_sideline");
                cam.transform.position = new Vector3(0, 40, -170);
                cam.transform.LookAt(new Vector3(0, 10, 0));
                cam.fieldOfView = 50f;
                Save(Render(cam, 1600, 900), n + "_broadcast");
                // goal-line closeup to confirm positions
                cam.transform.position = new Vector3(-12, 3, StadiumPrefabBuilder.GoalLineZ - 10f);
                cam.transform.LookAt(new Vector3(0, 2, StadiumPrefabBuilder.GoalLineZ + 6f));
                cam.fieldOfView = 55f;
                Save(Render(cam, 1600, 900), n + "_goalline");
                Object.DestroyImmediate(cam.gameObject);
                Object.DestroyImmediate(g);
            }
        }

        public static void RebuildAndShoot()
        {
            DraftFeverImporter.PostImportPrefabs();
            ExtraPrefabBuilder.BuildHelmets();
            AssetDatabase.Refresh();
            ArtLibraryBuilder.Build();
            ArtLibrary.ResetCache();
            ShootAll();
        }

        public static void LibraryAndShots() { ArtLibraryBuilder.Build(); ArtLibrary.ResetCache(); ShootAll(); }

        public static void ShootAll() { ShootPlayers(); ShootStadiums(); }
    }
}
