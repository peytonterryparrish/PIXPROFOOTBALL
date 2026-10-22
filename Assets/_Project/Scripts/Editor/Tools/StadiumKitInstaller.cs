using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.7 stadium kit-bash: parts of the modular stadium kit (Tools/Blender/pres_stadiumkit.py, prefabs built by PresentationSetBuilder) placed into the existing Draft Fever bowl at scene build time.
    /// Installed: a centre-hung jumbotron over the middle of the pitch (live screens through <see cref="PresentationScreen"/>). The roof, ribbon board, media box, light tower, tunnel mouth and
    /// scoreboard frame prefabs are ready in PresentationPrefabs but the bowl already has its own roof skeleton, fascia boards and boxes, so they are not placed (see graphics_v070.md, D-006).
    /// </summary>
    public static class StadiumKitInstaller
    {
        /// <summary>Hanging point (top of the unit) of the centre-hung jumbotron: under the roof skeleton, above every broadcast camera.</summary>
        public static Vector3 JumbotronTop = new Vector3(0f, 66f, 0f);

        /// <summary>Batch entry: rebuilds the match and free-roam stadium scenes with the kit installed.</summary>
        public static void RebuildScenes() { SceneBuilder.BuildMatchScene(); SceneBuilder.BuildStadiumScene(); }

        public static void Install(GameObject stadium)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PresentationSetBuilder.PrefabDir + "/PIX_Jumbotron_Hexagon.prefab");
            if (prefab == null) { Debug.LogWarning("[StadiumKit] PIX_Jumbotron_Hexagon prefab missing (run PIX Football/Presentation/Build sets): jumbotron not installed"); return; }
            var old = stadium.transform.Find("KitJumbotron"); if (old != null) Object.DestroyImmediate(old.gameObject);
            var j = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            j.name = "KitJumbotron";
            j.transform.SetParent(stadium.transform, true);
            j.transform.position = JumbotronTop; j.transform.rotation = Quaternion.identity;
            foreach (var r in j.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // 22 players + a 12 m box in the shadow cascades is not worth it
            Debug.Log("[StadiumKit] jumbotron installed at " + JumbotronTop);
        }
    }
}
