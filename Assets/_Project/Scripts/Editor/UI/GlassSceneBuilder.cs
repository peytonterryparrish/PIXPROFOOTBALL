using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.UI.Glass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Builds MenuBackdrop_Glass.unity: the stadium at night (prefab, crowd, reflection probes, moon light, floodlights, volumetric fog) that the glass menus load additively behind the menu stage, so the
    /// frosted glass always has a real, lit 3D scene to refract (floodlight bokeh, stands, the field). It contains no camera, no player and no UI; the menu scene keeps those.
    /// Menu: PIX Football/Glass UI/Build menu backdrop scene.
    /// </summary>
    public static class GlassSceneBuilder
    {
        public const string BackdropScenePath = SceneBuilder.ScenesDir + "/" + GlassStage.BackdropSceneName + ".unity";

        [MenuItem("PIX Football/Glass UI/Build menu backdrop scene")]
        public static void BuildMenuBackdrop()
        {
            MaterialFactory.EnsureFolder(SceneBuilder.ScenesDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.Root + "/Prefabs/PIX_Stadium_Pro.prefab");
            if (prefab == null) { Debug.LogError("[GlassScene] stadium prefab missing: build the licensed assets first"); return; }
            var stadium = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            stadium.name = "Stadium"; stadium.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            BrandingCleaner.ApplyCleanMaterials(stadium);
            SceneBuilder.FixGoalPosts(stadium);
            V6Look.UpgradeStadium(stadium);
            foreach (var r in stadium.GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("Skeleton")) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            SceneBuilder.BuildCrowdObjects(null);
            V6Look.AddReflectionProbes();
            Light moon;
            var night = SceneBuilder.BuildNight(out moon);
            var flood = SceneBuilder.BuildFlood();
            var lighting = new GameObject("Lighting");
            night.transform.SetParent(lighting.transform, false); flood.transform.SetParent(lighting.transform, false);
            EditorSceneManager.SaveScene(scene, BackdropScenePath);
            Debug.Log("[GlassScene] saved " + BackdropScenePath);
            AddToBuildSettings();
        }

        /// <summary>Registers the backdrop scene as the last Build Settings scene (the menu loads it by name).</summary>
        public static void AddToBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in list) if (s.path == BackdropScenePath) return;
            list.Add(new EditorBuildSettingsScene(BackdropScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
