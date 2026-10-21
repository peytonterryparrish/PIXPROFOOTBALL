using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Builds prop prefabs (goal post, pylon, yard markers, ball, benches...) and the two stadium prefabs.
    /// Field geometry: NFL field 120 yd x 53.33 yd incl. end zones, 1 unit = 1 m, centred at origin, long axis along Z,
    /// goal lines at Z = +/-45.72 m, end lines at Z = +/-54.864 m, sidelines at X = +/-24.384 m.
    /// </summary>
    public static class StadiumPrefabBuilder
    {
        const string Root = CharacterSetup.Root;
        const string PrefabDir = Root + "/Prefabs";
        public const float GoalLineZ = 45.72f;
        public const float EndLineZ = 54.864f;
        public const float HalfWidthX = 24.384f;

        static string Clean(string n)
        {
            n = Path.GetFileNameWithoutExtension(n);
            foreach (string s in new[] { "_GEO_LODs", "_LOD_GEO", "_GEO", "_LODs" }) n = n.Replace(s, "");
            return n;
        }

        [MenuItem("PIX Football/Assets/Build Prop Prefabs")]
        public static void BuildProps()
        {
            MaterialFactory.EnsureFolder(PrefabDir);
            string propRoot = Root + "/Props/StadiumProps";
            var done = new HashSet<string>();
            foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { propRoot }).OrderBy(AssetDatabase.GUIDToAssetPath))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                string name = Clean(path);
                if (path.Contains("/footballBall/delivery/downMarker")) continue; // duplicate of the down marker
                if (!done.Add(name)) continue;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null) continue;
                string topFolder = propRoot + "/" + path.Substring(propRoot.Length + 1).Split('/')[0];
                string[] patBase = { "basemap", "basecolor", "base_color", "albedo" };
                string[] patMask = { "maskmap", "mask_map" };
                string[] patNormal = { "normal" };
                string bn = Path.GetFileNameWithoutExtension(path).Replace("_GEO", "").Replace("_LOD", "");
                var tb = MaterialFactory.FindTextureNear(topFolder, bn, patBase);
                var tm = MaterialFactory.FindTextureNear(topFolder, bn, patMask);
                var tn = MaterialFactory.FindTextureNear(topFolder, bn, patNormal);
                Material mat = null;
                if (tb != null) mat = MaterialFactory.CreateLit(MaterialFactory.MatDir + "/Props/PIX_Prop_" + name + ".mat", tb, tm, tn);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                inst.name = "PIX_Prop_" + name;
                if (mat != null)
                    foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                    {
                        var arr = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < arr.Length; i++) arr[i] = mat;
                        r.sharedMaterials = arr;
                    }
                // LOD fbx: keep LOD0 only visible
                var lods = inst.GetComponentsInChildren<Renderer>(true).Where(r => r.name.ToLower().Contains("lod")).OrderBy(r => r.name).ToArray();
                foreach (var lg in inst.GetComponentsInChildren<LODGroup>(true)) lg.enabled = false;
                for (int i = 1; i < lods.Length; i++) lods[i].gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(inst, PrefabDir + "/PIX_Prop_" + name + ".prefab");
                Object.DestroyImmediate(inst);
            }
            AssetDatabase.SaveAssets();
        }

        static GameObject Prop(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/PIX_Prop_" + name + ".prefab");
        }

        static GameObject Place(GameObject parent, string propName, Vector3 pos, float yaw, string instName = null)
        {
            var p = Prop(propName);
            if (p == null) { Debug.LogWarning("[StadiumPrefabBuilder] missing prop " + propName); return null; }
            var g = (GameObject)PrefabUtility.InstantiatePrefab(p);
            g.name = instName ?? propName;
            g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            return g;
        }

        static void SetTransparent(Material m)
        {
            m.SetFloat("_SurfaceType", 1f);
            m.SetFloat("_BlendMode", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_AlphaCutoffEnable", 0f);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetOverrideTag("RenderType", "Transparent");
            HDShaderUtils.ResetMaterialKeywords(m);
            EditorUtility.SetDirty(m);
        }

        static GameObject BuildField(Transform parent)
        {
            var fieldAsset = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Props/Field/footballField_GEO.fbx");
            var fieldRoot = new GameObject("Field");
            fieldRoot.transform.SetParent(parent, false);

            string grassFolder = Root + "/Props/Field/Lucca Grass Texture";
            var albedo = MaterialFactory.FindTexture(grassFolder, "albedo");
            var normal = MaterialFactory.FindTexture(grassFolder, "normal");
            var grass = MaterialFactory.CreateLit(MaterialFactory.MatDir + "/PIX_FieldGrass.mat", albedo, null, normal, new Color(0.78f, 0.9f, 0.7f), 0f, 0.18f, new Vector2(22f, 12.5f));

            var marks = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Props/Field/grassMarkings_NEW.png");
            var markMat = MaterialFactory.CreateLit(MaterialFactory.MatDir + "/PIX_FieldMarkings.mat", marks, null, null, Color.white, 0f, 0.1f);
            SetTransparent(markMat);

            var turf = (GameObject)PrefabUtility.InstantiatePrefab(fieldAsset);
            turf.name = "Turf";
            turf.transform.SetParent(fieldRoot.transform, false);
            foreach (var r in turf.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = Enumerable.Repeat(grass, r.sharedMaterials.Length).ToArray();

            var lines = (GameObject)PrefabUtility.InstantiatePrefab(fieldAsset);
            lines.name = "Markings";
            lines.transform.SetParent(fieldRoot.transform, false);
            lines.transform.localPosition = new Vector3(0, 0.012f, 0);
            foreach (var r in lines.GetComponentsInChildren<Renderer>(true))
            {
                r.sharedMaterials = Enumerable.Repeat(markMat, r.sharedMaterials.Length).ToArray();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return fieldRoot;
        }

        static void AddFurniture(GameObject root)
        {
            var props = new GameObject("FieldProps");
            props.transform.SetParent(root.transform, false);
            float[] zs = { GoalLineZ, -GoalLineZ };
            // goal posts on the end line, facing the field
            Place(props, "goalPost", new Vector3(0, 0, EndLineZ), 180f, "GoalPost_North");
            Place(props, "goalPost", new Vector3(0, 0, -EndLineZ), 0f, "GoalPost_South");
            // pylons: goal line + end line corners
            foreach (float sx in new[] { -1f, 1f })
                foreach (float z in new[] { GoalLineZ, -GoalLineZ, EndLineZ, -EndLineZ })
                    Place(props, "endZonePylon", new Vector3(sx * HalfWidthX, 0, z), 0f, "Pylon");
            // sideline yard markers (every 10 yd)
            string[] marker = { "yardMarker_10", "yardMarker_20", "yardMarker_30", "yardMarker_40", "yardMarker_50" };
            for (int k = 0; k < 5; k++)
            {
                float z = GoalLineZ - (k + 1) * 9.144f;
                foreach (float sx in new[] { -1f, 1f })
                {
                    Place(props, marker[k], new Vector3(sx * (HalfWidthX + 1.2f), 0, z), sx > 0 ? 90f : -90f);
                    if (k < 4) Place(props, marker[k], new Vector3(sx * (HalfWidthX + 1.2f), 0, -z), sx > 0 ? 90f : -90f);
                }
            }
            foreach (float sx in new[] { -1f, 1f })
                foreach (float z in zs) Place(props, "yardMarker_G", new Vector3(sx * (HalfWidthX + 1.2f), 0, z), sx > 0 ? 90f : -90f);
            // sideline furniture
            Place(props, "stadiumBench", new Vector3(HalfWidthX + 6f, 0, 8f), -90f, "Bench_Home");
            Place(props, "stadiumBench", new Vector3(HalfWidthX + 6f, 0, -8f), -90f, "Bench_Home");
            Place(props, "stadiumBench", new Vector3(-HalfWidthX - 6f, 0, 8f), 90f, "Bench_Away");
            Place(props, "stadiumBench", new Vector3(-HalfWidthX - 6f, 0, -8f), 90f, "Bench_Away");
            Place(props, "hydrationStation", new Vector3(HalfWidthX + 6f, 0, 18f), -90f);
            Place(props, "hydrationStation", new Vector3(-HalfWidthX - 6f, 0, 18f), 90f);
            Place(props, "downMarker", new Vector3(HalfWidthX + 2.5f, 0, 0f), 0f);
            Place(props, "playClock", new Vector3(0f, 0f, EndLineZ + 9f), 180f);
            Place(props, "playClock", new Vector3(0f, 0f, -EndLineZ - 9f), 0f);
            Place(props, "footballBall", new Vector3(0f, 0.11f, 0f), 0f, "Football");
        }

        /// <summary>Builds a stadium prefab. bowlFbx is the authored stadium mesh whose long axis is X (field authored along X).</summary>
        static void BuildStadium(string name, string bowlFbx, Vector3 bowlOffset)
        {
            var root = new GameObject(name);
            var fieldRot = new GameObject("Pitch_Rotated"); // authoring space has the pitch along X; rotate so it runs along Z
            fieldRot.transform.SetParent(root.transform, false);
            fieldRot.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            BuildField(fieldRot.transform);

            var bowlAsset = AssetDatabase.LoadAssetAtPath<GameObject>(bowlFbx);
            var bowl = (GameObject)PrefabUtility.InstantiatePrefab(bowlAsset);
            bowl.name = "Bowl";
            bowl.transform.SetParent(fieldRot.transform, false);
            bowl.transform.localPosition = bowlOffset;

            AddFurniture(root);
            string path = PrefabDir + "/" + name + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            Debug.Log("[StadiumPrefabBuilder] saved " + path);
        }

        [MenuItem("PIX Football/Assets/Build Stadium Prefabs")]
        public static void BuildStadiums()
        {
            MaterialFactory.EnsureFolder(PrefabDir);
            BuildProps();
            BuildStadium("PIX_Stadium_Pro", Root + "/Packages/Stadium_Regular/_Project/3D/Stadium_Regular/Meshes/Regular.fbx", Vector3.zero);
            BuildStadium("PIX_Stadium_College", Root + "/Packages/Stadium_College/_Project/3D/Stadium_College/Meshes/Stadium_College.fbx", Vector3.zero);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
