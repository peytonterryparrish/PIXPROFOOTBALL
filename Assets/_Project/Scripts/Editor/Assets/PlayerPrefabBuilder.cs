using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Builds PIX_Player_{Small,Medium,Large}.prefab: skinned body (humanoid avatar) + HDRP shader-graph materials
    /// (team-colour uniform, skin, eyes, shoes) + helmet shell + face mask (+ inactive visor).
    /// </summary>
    public static class PlayerPrefabBuilder
    {
        const string Root = CharacterSetup.Root;
        const string PrefabDir = Root + "/Prefabs";

        class HelmetSet { public string shell, mask, visor; }

        static readonly Dictionary<string, HelmetSet> Helmets = new Dictionary<string, HelmetSet>
        {
            { "Small", new HelmetSet { shell = Root + "/Helmets/New/Small/Basic Helmet 1/Basic_Helmet_01_Small.fbx", mask = Root + "/Helmets/New/Small/Basic Helmet 1/FM_01_Small.fbx", visor = Root + "/Helmets/New/Small/Basic Helmet 1/visor_01_Small.fbx" } },
            { "Medium", new HelmetSet { shell = Root + "/Helmets/New/Medium/Basic Helmet 1/basicHelmet_01_GEO.fbx", mask = Root + "/Helmets/New/Medium/Face Masks/FM_01_GEO.fbx", visor = Root + "/Helmets/New/Medium/Visors/visor_01_GEO.fbx" } },
            { "Large", new HelmetSet { shell = Root + "/Helmets/New/Large/Basic_Helmet_01_Large.fbx", mask = Root + "/Helmets/New/Large/FM_01_Large.fbx", visor = Root + "/Helmets/New/Large/visor_01_Large.fbx" } },
        };

        static Material M(string name)
        {
            var m = MaterialFactory.FindMaterial(name);
            if (m == null) Debug.LogWarning("[PlayerPrefabBuilder] material not found: " + name);
            return m;
        }

        public static void EnsureSharedMaterials(out Material faceMask, out Material visor)
        {
            faceMask = MaterialFactory.CreateSolid(MaterialFactory.MatDir + "/PIX_FaceMask.mat", new Color(0.09f, 0.09f, 0.1f, 1f), 0.9f, 0.7f);
            visor = MaterialFactory.CreateSolid(MaterialFactory.MatDir + "/PIX_Visor.mat", new Color(0.05f, 0.07f, 0.1f, 0.45f), 0f, 0.95f, true);
        }

        public static DiffusionProfileSettings[] FindDiffusionProfiles()
        {
            var list = new List<DiffusionProfileSettings>();
            foreach (string g in AssetDatabase.FindAssets("t:DiffusionProfileSettings", new[] { Root }))
                list.Add(AssetDatabase.LoadAssetAtPath<DiffusionProfileSettings>(AssetDatabase.GUIDToAssetPath(g)));
            return list.ToArray();
        }

        /// <summary>Registers the skin diffusion profiles in HDRP Global Settings (otherwise skin renders magenta).</summary>
        public static void RegisterDiffusionProfiles()
        {
            var gsType = System.Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings, Unity.RenderPipelines.HighDefinition.Runtime");
            var inst = gsType == null ? null : gsType.GetProperty("instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var gs = inst == null ? null : inst.GetValue(null) as ScriptableObject;
            if (gs == null) { Debug.LogWarning("[PlayerPrefabBuilder] no HDRP global settings"); return; }
            var so = new SerializedObject(gs);
            var prop = so.FindProperty("diffusionProfileSettingsList");
            if (prop == null) { Debug.LogWarning("[PlayerPrefabBuilder] diffusionProfileSettingsList property not found"); return; }
            var have = new HashSet<Object>();
            for (int i = 0; i < prop.arraySize; i++) have.Add(prop.GetArrayElementAtIndex(i).objectReferenceValue);
            foreach (var p in FindDiffusionProfiles())
            {
                if (have.Contains(p)) continue;
                prop.arraySize++;
                prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = p;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(gs);
            AssetDatabase.SaveAssets();
        }

        static void SetMats(Renderer r, Material m)
        {
            if (m == null) return;
            var arr = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < arr.Length; i++) arr[i] = m;
            r.sharedMaterials = arr;
        }

        static Bounds Combined(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        [MenuItem("PIX Football/Assets/Build Player Prefabs")]
        public static void BuildAll()
        {
            RegisterDiffusionProfiles();
            MaterialFactory.EnsureFolder(PrefabDir);
            MaterialFactory.EnsureFolder(MaterialFactory.MatDir);
            Material faceMask, visor;
            EnsureSharedMaterials(out faceMask, out visor);

            var uniform = M("Mid_Size_Player_Uniform_MAT");
            var arms = M("Mid_Size_Player_Arms_MAT");
            var head = M("Mid_Size_Player_Head_MAT");
            var eyes = M("Mid_Size_Player_Eyes_MAT");
            var shoes = M("Mid_Size_Player_Gloves_Shoes_MAT");
            var helmetMat = M("New_Helmet_MAT");

            string[] sizes = { "Small", "Medium", "Large" };
            for (int i = 0; i < 3; i++)
            {
                string size = sizes[i];
                var bodyAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSetup.BodyPaths[i]);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(bodyAsset);
                inst.name = "PIX_Player_" + size;
                inst.transform.position = Vector3.zero;
                inst.transform.rotation = Quaternion.identity;

                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    string n = r.name;
                    if (n.StartsWith("Hair Mesh")) { r.gameObject.SetActive(false); continue; }
                    if (n.StartsWith("Torso") || n.StartsWith("Legs")) SetMats(r, uniform);
                    else if (n.StartsWith("Arms") || n.StartsWith("Hands")) SetMats(r, arms);
                    else if (n.StartsWith("Head")) SetMats(r, head);
                    else if (n.StartsWith("Eyes")) SetMats(r, eyes);
                    else if (n.StartsWith("Shoes")) SetMats(r, shoes);
                }

                var animator = inst.GetComponent<Animator>();
                if (animator == null) animator = inst.AddComponent<Animator>();
                animator.applyRootMotion = false; // gameplay layers root motion over a physics capsule (use animator.deltaPosition)
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // helmet
                var headBone = animator.GetBoneTransform(HumanBodyBones.Head);
                var headRenderer = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name.StartsWith("Head"));
                if (headBone != null)
                {
                    var set = Helmets[size];
                    var holder = new GameObject("Helmet");
                    holder.transform.SetParent(headBone, false);
                    GameObject shell = AttachGear(inst, holder.transform, set.shell, "Helmet_Shell", helmetMat);
                    GameObject mask = AttachGear(inst, holder.transform, set.mask, "FaceMask", faceMask);
                    GameObject vis = AttachGear(inst, holder.transform, set.visor, "Visor", visor);
                    if (vis != null) vis.SetActive(false);
                    if (shell != null && headRenderer != null)
                    {
                        // Small/Large helmets are authored in body space; the Medium one is centred on its own pivot.
                        Vector3 headCenter = headRenderer.bounds.center;
                        Vector3 target = headCenter + new Vector3(0f, 0.065f, -0.012f);
                        Vector3 shellCenter = Combined(shell).center;
                        bool bodySpace = size != "Medium";
                        if (!bodySpace)
                        {
                            Vector3 delta = target - shellCenter;
                            foreach (var g in new[] { shell, mask, vis }) if (g != null) g.transform.position += delta;
                        }
                    }
                }

                string path = PrefabDir + "/PIX_Player_" + size + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(inst, path);
                Object.DestroyImmediate(inst);
                Debug.Log("[PlayerPrefabBuilder] saved " + path);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static GameObject AttachGear(GameObject body, Transform parent, string fbxPath, string name, Material mat)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (asset == null) { Debug.LogWarning("[PlayerPrefabBuilder] missing gear " + fbxPath); return null; }
            var g = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            g.name = name;
            // keep authored world position first, then re-parent so it follows the head bone
            g.transform.SetParent(parent, true);
            foreach (var lg in g.GetComponentsInChildren<LODGroup>(true)) lg.enabled = false; // LODGroup would cull the shell at distance once LOD1/2 are hidden
            // keep only the highest LOD renderer: others are disabled
            var rs = g.GetComponentsInChildren<Renderer>(true).OrderBy(r => r.name).ToArray();
            for (int i = 0; i < rs.Length; i++)
            {
                if (i > 0 && rs.Length > 1) rs[i].gameObject.SetActive(false);
                SetMats(rs[i], mat);
            }
            return g;
        }
    }
}
