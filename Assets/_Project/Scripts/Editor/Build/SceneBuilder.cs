using System.Collections.Generic;
using System.IO;
using System.Linq;
using PixFootball.Art;
using PixFootball.Gameplay;
using PixFootball.UI;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>Builds Stadium_Pro.unity and MainMenu.unity (headless-capable) and registers them in Build Settings. Menu: PIX Football/Scenes/Build All.</summary>
    public static class SceneBuilder
    {
        public const string ScenesDir = "Assets/_Project/Scenes";
        public const string StadiumScenePath = ScenesDir + "/Stadium_Pro.unity";
        public const string MenuScenePath = ScenesDir + "/MainMenu.unity";
        public const string VolumesDir = "Assets/_Project/Settings/Volumes";
        const string PrefabDir = CharacterSetup.Root + "/Prefabs";

        // NFL goal post: crossbar 10 ft (3.048 m), uprights reach 35 ft (10.668 m) above the crossbar
        public const float CrossbarHeight = 3.048f, UprightHeight = 10.668f;

        public static readonly string[] ExpectedStadiumObjects = { "Stadium", "Crowd", "Lighting", "Lighting_Night", "Lighting_Day", "Main Camera", "FreeRoam", "FreeRoamUI" };
        public static readonly string[] ExpectedMenuObjects = { "Main Camera", "MainMenu", "MenuStage" };

        // ---------------------------------------------------------------------------------------------- helpers
        static void Ensure(string dir) { if (!AssetDatabase.IsValidFolder(dir)) MaterialFactory.EnsureFolder(dir); }

        static T AddOverride<T>(VolumeProfile p) where T : VolumeComponent
        {
            var c = p.Add<T>(true);
            AssetDatabase.AddObjectToAsset(c, p);
            return c;
        }

        static VolumeProfile NewProfile(string name)
        {
            Ensure(VolumesDir);
            string path = VolumesDir + "/" + name + ".asset";
            AssetDatabase.DeleteAsset(path);
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(p, path);
            return p;
        }

        static void CommonPost(VolumeProfile p, bool night)
        {
            var tm = AddOverride<Tonemapping>(p); tm.mode.Override(TonemappingMode.ACES);
            var bloom = AddOverride<Bloom>(p); bloom.intensity.Override(night ? 0.18f : 0.1f); bloom.threshold.Override(0.9f); bloom.scatter.Override(0.6f);
            var ca = AddOverride<ColorAdjustments>(p); ca.contrast.Override(10f); ca.saturation.Override(8f); ca.colorFilter.Override(new Color(1f, 0.965f, 0.915f));
            var wb = AddOverride<WhiteBalance>(p); wb.temperature.Override(9f);
            var vg = AddOverride<Vignette>(p); vg.intensity.Override(0.2f); vg.smoothness.Override(0.55f);
            var fg = AddOverride<FilmGrain>(p); fg.type.Override(FilmGrainLookup.Thin1); fg.intensity.Override(0.1f); fg.response.Override(0.8f);
            var mb = AddOverride<MotionBlur>(p); mb.intensity.Override(0.12f);
            var ao = AddOverride<ScreenSpaceAmbientOcclusion>(p); ao.intensity.Override(0.7f); ao.radius.Override(0.7f);
            var ssr = AddOverride<ScreenSpaceReflection>(p); ssr.enabled.Override(true); ssr.minSmoothness = 0.92f;
            var cs = AddOverride<ContactShadows>(p); cs.enable.Override(true); cs.length.Override(0.25f); cs.opacity.Override(1f);
            var sh = AddOverride<HDShadowSettings>(p); sh.maxShadowDistance.Override(220f); sh.cascadeShadowSplitCount.Override(4);
            var dpl = AddOverride<DiffusionProfileList>(p); dpl.diffusionProfiles.overrideState = true; dpl.diffusionProfiles.value = PlayerPrefabBuilder.FindDiffusionProfiles();
        }

        static Light MakeLight(Transform parent, string name, LightType type, Vector3 pos, Quaternion rot, LightUnit unit, float intensity, Color color, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var l = go.AddComponent<Light>();
            go.AddComponent<HDAdditionalLightData>();
            l.type = type; l.lightUnit = unit; l.intensity = intensity; l.color = color;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            return l;
        }

        // ---------------------------------------------------------------------------------------------- lighting presets
        static GameObject BuildNight(out Light moon)
        {
            var root = new GameObject("Lighting_Night");
            var prof = NewProfile("Night_Stadium");
            var exp = AddOverride<Exposure>(prof); exp.mode.Override(ExposureMode.Fixed); exp.fixedExposure.Override(9.3f);
            var env = AddOverride<VisualEnvironment>(prof); env.skyType.Override((int)SkyType.Gradient); env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            // night sky: dim blue gradient (gives the stands and roof underside a soft ambient fill)
            var sky = AddOverride<GradientSky>(prof);
            sky.top.Override(new Color(0.10f, 0.14f, 0.30f)); sky.middle.Override(new Color(0.08f, 0.10f, 0.20f)); sky.bottom.Override(new Color(0.04f, 0.045f, 0.07f)); sky.gradientDiffusion.Override(2f); sky.multiplier.Override(120f);
            var fog = AddOverride<Fog>(prof); fog.enabled.Override(true); fog.enableVolumetricFog.Override(true); fog.meanFreePath.Override(520f); fog.albedo.Override(new Color(0.62f, 0.7f, 0.85f)); fog.anisotropy.Override(0.55f);
            fog.baseHeight.Override(0f); fog.maximumHeight.Override(140f);
            var ind = AddOverride<IndirectLightingController>(prof); ind.indirectDiffuseLightingMultiplier.Override(5f);
            CommonPost(prof, true);
            var vol = root.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10; vol.sharedProfile = prof;

            // moon / fill: soft blue directional with shadows
            moon = MakeLight(root.transform, "Moon", LightType.Directional, new Vector3(0, 60, 0), Quaternion.Euler(52f, -35f, 0f), LightUnit.Lux, 220f, new Color(0.55f, 0.66f, 1f), true);

            // stadium floodlights: spot lights on the roof edges, aimed at the pitch (candela, roughly 1500 lux on the grass)
            var rig = new GameObject("Floodlights").transform; rig.SetParent(root.transform, false);
            Color warm = new Color(1f, 0.95f, 0.88f);
            int n = 0;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float z in new[] { -54f, -30f, -10f, 10f, 30f, 54f })
                {
                    Vector3 pos = new Vector3(sx * 46f, 46f, z);
                    Vector3 aim = new Vector3(-sx * 4f, 0f, z * 0.45f);
                    var l = MakeLight(rig, "Flood_" + (sx < 0 ? "W" : "E") + (++n), LightType.Spot, pos, Quaternion.LookRotation(aim - pos), LightUnit.Candela, 4200000f, warm, n % 3 == 0);
                    l.spotAngle = 74f; l.range = 260f;
                    l.GetComponent<HDAdditionalLightData>().innerSpotPercent = 75f;
                }
            foreach (float sz in new[] { -1f, 1f })
                foreach (float x in new[] { -14f, 14f })
                {
                    Vector3 pos = new Vector3(x, 44f, sz * 84f);
                    Vector3 aim = new Vector3(-x * 0.3f, 0f, sz * 28f);
                    var l = MakeLight(rig, "FloodEnd_" + (++n), LightType.Spot, pos, Quaternion.LookRotation(aim - pos), LightUnit.Candela, 5000000f, warm, false);
                    l.spotAngle = 66f; l.range = 280f;
                }
            return root;
        }

        static GameObject BuildDay(out Light sun)
        {
            var root = new GameObject("Lighting_Day");
            var prof = NewProfile("Day_Stadium");
            var exp = AddOverride<Exposure>(prof); exp.mode.Override(ExposureMode.Fixed); exp.fixedExposure.Override(13.7f);
            var env = AddOverride<VisualEnvironment>(prof); env.skyType.Override((int)SkyType.PhysicallyBased); env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
            var pbs = AddOverride<PhysicallyBasedSky>(prof);
            var fog = AddOverride<Fog>(prof); fog.enabled.Override(true); fog.enableVolumetricFog.Override(false); fog.meanFreePath.Override(4000f); fog.albedo.Override(new Color(0.75f, 0.8f, 0.9f));
            CommonPost(prof, false);
            var vol = root.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10; vol.sharedProfile = prof;
            sun = MakeLight(root.transform, "Sun", LightType.Directional, new Vector3(0, 60, 0), Quaternion.Euler(62f, 38f, 0f), LightUnit.Lux, 105000f, new Color(1f, 0.96f, 0.9f), true);
            return root;
        }

        // ---------------------------------------------------------------------------------------------- stadium
        static Mesh _goalPostMesh;

        static Mesh NflGoalPostMesh(Mesh src)
        {
            if (_goalPostMesh != null) return _goalPostMesh;
            Ensure(BrandingCleaner.PixDir + "/Meshes");
            string path = BrandingCleaner.PixDir + "/Meshes/PIX_goalPost_NFL.asset";
            AssetDatabase.DeleteAsset(path);
            var m = Object.Instantiate(src); m.name = "PIX_goalPost_NFL";
            var v = m.vertices;
            float modelTop = 12.75f;
            float k = UprightHeight / (modelTop - CrossbarHeight);   // stretch only the uprights above the crossbar
            for (int i = 0; i < v.Length; i++) if (v[i].y > CrossbarHeight + 0.15f) v[i].y = CrossbarHeight + 0.15f + (v[i].y - CrossbarHeight - 0.15f) * k;
            m.vertices = v; m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return _goalPostMesh = m;
        }

        static void FixGoalPosts(GameObject stadium)
        {
            foreach (var t in stadium.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("GoalPost")) continue;
                foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    // collect the LOD0 mesh only
                    if (mf.name.Contains("LOD01") || mf.name.Contains("LOD02")) { mf.gameObject.SetActive(false); continue; }
                    mf.sharedMesh = NflGoalPostMesh(mf.sharedMesh);
                }
                t.localScale = Vector3.one;
            }
        }

        static void BuildCrowdObjects(Transform parent)
        {
            var root = new GameObject("Crowd"); root.transform.SetParent(parent, false);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(CrowdBuilder.BillboardMaterialPath);
            if (!File.Exists(Path.GetFullPath(CrowdBuilder.MeshListPath))) { Debug.LogWarning("[PIX] crowd meshes not built (PIX Football/Assets/Build Crowd Billboards)"); return; }
            int fans = 0;
            foreach (string p in File.ReadAllLines(Path.GetFullPath(CrowdBuilder.MeshListPath)))
            {
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(p.Trim());
                if (mesh == null) continue;
                var go = new GameObject(mesh.name);
                go.transform.SetParent(root.transform, false);
                go.isStatic = true;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true; mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                fans += mesh.vertexCount / 4;
            }
            var info = root.AddComponent<CrowdInfo>(); info.FanCount = fans;
            Debug.Log("[PIX] crowd instantiated: " + fans + " fans");
        }

        static Camera MakeMainCamera(Vector3 pos, Quaternion rot, float fov, bool menu)
        {
            var go = new GameObject("Main Camera"); go.tag = "MainCamera";
            go.transform.SetPositionAndRotation(pos, rot);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov; cam.nearClipPlane = 0.2f; cam.farClipPlane = 1500f; cam.allowHDR = true;
            go.AddComponent<AudioListener>();
            var hd = go.AddComponent<HDAdditionalCameraData>();
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            hd.taaSharpenStrength = 0.4f;
            if (menu) { hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color; hd.backgroundColorHDR = new Color(0.012f, 0.018f, 0.04f, 1f); }
            else hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
            if (!menu) go.AddComponent<CinemachineBrain>();
            return cam;
        }

        [MenuItem("PIX Football/Scenes/Build Stadium Pro")]
        public static void BuildStadiumScene()
        {
            Ensure(ScenesDir);
            PlayerPrefabBuilder.RegisterDiffusionProfiles();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/PIX_Stadium_Pro.prefab");
            var stadium = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            stadium.name = "Stadium"; stadium.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            int swapped = BrandingCleaner.ApplyCleanMaterials(stadium);
            Debug.Log("[PIX] clean materials applied to stadium: " + swapped);
            FixGoalPosts(stadium);
            // the roof lattice would cast a grid of shadows over the pitch from the sun and the floodlights
            foreach (var r in stadium.GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("Skeleton")) r.shadowCastingMode = ShadowCastingMode.Off;

            BuildCrowdObjects(null);

            Light moon, sun;
            var night = BuildNight(out moon);
            var day = BuildDay(out sun);
            var lighting = new GameObject("Lighting");
            night.transform.SetParent(lighting.transform, false); day.transform.SetParent(lighting.transform, false);
            var lc = lighting.AddComponent<LightingController>();
            lc.NightRoot = night; lc.DayRoot = day; lc.NightSun = moon; lc.DaySun = sun;
            day.SetActive(false);

            var cam = MakeMainCamera(new Vector3(-50f, 19f, -30f), Quaternion.LookRotation(new Vector3(50f, -17f, 0f)), 26f, false);

            var game = new GameObject("FreeRoam");
            var fr = game.AddComponent<FreeRoamGame>();
            fr.MainCamera = cam; fr.Lighting = lc;
            fr.SpawnPosition = new Vector3(0f, 0f, -30f);

            var ui = new GameObject("FreeRoamUI");
            ui.AddComponent<FreeRoamHud>(); ui.AddComponent<PauseMenu>();

            EditorSceneManager.SaveScene(scene, StadiumScenePath);
            Debug.Log("[PIX] saved " + StadiumScenePath);
        }

        // ---------------------------------------------------------------------------------------------- main menu
        [MenuItem("PIX Football/Scenes/Build Main Menu")]
        public static void BuildMenuScene()
        {
            Ensure(ScenesDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var prof = NewProfile("Menu_Stage");
            var exp = AddOverride<Exposure>(prof); exp.mode.Override(ExposureMode.Fixed); exp.fixedExposure.Override(8.4f);
            var env = AddOverride<VisualEnvironment>(prof); env.skyType.Override(0);
            var fog = AddOverride<Fog>(prof); fog.enabled.Override(true); fog.enableVolumetricFog.Override(true); fog.meanFreePath.Override(40f); fog.albedo.Override(new Color(0.3f, 0.4f, 0.6f)); fog.anisotropy.Override(0.6f);
            CommonPost(prof, true);
            var volGo = new GameObject("MenuVolume"); var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.sharedProfile = prof;

            var cam = MakeMainCamera(new Vector3(-0.5f, 1.4f, 4.9f), Quaternion.identity, 32f, true);
            cam.transform.LookAt(new Vector3(1.2f, 1.0f, 0f));

            var stage = new GameObject("MenuStage");
            var anchor = new GameObject("PlayerAnchor"); anchor.transform.SetParent(stage.transform, false);
            anchor.transform.localPosition = Vector3.zero; anchor.transform.localRotation = Quaternion.Euler(0f, 22f, 0f);

            // pitch turf as the stage floor (licensed grass material) + key / rim lights
            var grass = MaterialFactory.FindMaterial("PIX_FieldGrass");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.name = "StageFloor"; floor.transform.SetParent(stage.transform, false);
            floor.transform.localScale = new Vector3(6f, 1f, 6f);
            if (grass != null) floor.GetComponent<Renderer>().sharedMaterial = grass;
            Object.DestroyImmediate(floor.GetComponent<Collider>());
            MakeLight(stage.transform, "Key", LightType.Spot, new Vector3(2.6f, 3.6f, 4.2f), Quaternion.LookRotation(new Vector3(-2.6f, -2.4f, -4.2f)), LightUnit.Candela, 48000f, new Color(1f, 0.94f, 0.85f), true).spotAngle = 40f;
            MakeLight(stage.transform, "Rim", LightType.Spot, new Vector3(-2.2f, 3.2f, -3.8f), Quaternion.LookRotation(new Vector3(2.2f, -2.2f, 3.8f)), LightUnit.Candela, 34000f, new Color(0.55f, 0.7f, 1f), true).spotAngle = 45f;
            MakeLight(stage.transform, "Fill", LightType.Directional, new Vector3(0, 5, 0), Quaternion.Euler(35f, 160f, 0f), LightUnit.Lux, 900f, new Color(0.6f, 0.7f, 1f), false);

            var menu = new GameObject("MainMenu");
            var mc = menu.AddComponent<MainMenuController>();
            mc.StageAnchor = anchor.transform; mc.StageCamera = cam;

            EditorSceneManager.SaveScene(scene, MenuScenePath);
            Debug.Log("[PIX] saved " + MenuScenePath);
        }

        public static void SetBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(StadiumScenePath, true),
            };
        }

        [MenuItem("PIX Football/Scenes/Build All")]
        public static void BuildAll()
        {
            AssetDatabase.Refresh();
            BuildStadiumScene();
            BuildMenuScene();
            SetBuildSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[PIX] scenes built; build settings: " + string.Join(", ", EditorBuildSettings.scenes.Select(s => s.path)));
        }

        /// <summary>Everything from the licensed sources: input map, branding-free textures, crowd, runtime assets, scenes.</summary>
        [MenuItem("PIX Football/Build/Prepare Everything")]
        public static void PrepareEverything()
        {
            InputMapBuilder.Generate();
            BrandingCleaner.CleanAll();
            CrowdBuilder.BuildAll();
            RuntimeAssetsBuilder.Build();
            BuildAll();
        }
    }
}
