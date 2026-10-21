using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>Referee, crowd, helmets (variants), accessories and hair-card prefabs, built with HDRP/Lit materials from the shipped texture sets.</summary>
    public static class ExtraPrefabBuilder
    {
        const string Root = CharacterSetup.Root;
        const string PrefabDir = Root + "/Prefabs";

        static GameObject Load(string path) { return AssetDatabase.LoadAssetAtPath<GameObject>(path); }

        static void Assign(GameObject inst, System.Func<Renderer, Material> pick)
        {
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var m = pick(r);
                if (m == null) continue;
                r.sharedMaterials = Enumerable.Repeat(m, r.sharedMaterials.Length).ToArray();
            }
        }

        static void Save(GameObject inst, string name)
        {
            MaterialFactory.EnsureFolder(PrefabDir);
            PrefabUtility.SaveAsPrefabAsset(inst, PrefabDir + "/" + name + ".prefab");
            Object.DestroyImmediate(inst);
            Debug.Log("[ExtraPrefabBuilder] saved " + name);
        }

        // ---------------------------------------------------------------- referee (static mesh; no referee rig/mocap exists in the source set)
        [MenuItem("PIX Football/Assets/Build Referee Prefab")]
        public static void BuildReferee()
        {
            string tex = Root + "/Characters/Referee/Textures";
            var fbx = Load(Root + "/Characters/Referee/Referee_LOD0.fbx");
            if (fbx == null) { Debug.LogWarning("[ExtraPrefabBuilder] Referee_LOD0.fbx missing"); return; }
            Material Mk(string name, string folder, string bc, string n)
            {
                var b = MaterialFactory.FindTexture(tex + "/" + folder, bc);
                var nm = MaterialFactory.FindTexture(tex + "/" + folder, n);
                return MaterialFactory.CreateLit(MaterialFactory.MatDir + "/Referee/" + name + ".mat", b, null, nm, Color.white, 0f, folder == "Eyes" ? 0.9f : 0.35f);
            }
            var uniform = Mk("PIX_Ref_Uniform", "Uniform", "_bc", "_n");
            var arms = Mk("PIX_Ref_Arms", "Arms", "_bc", "_n");
            var head = Mk("PIX_Ref_Head", "Head", "_bc", "_n");
            var eyes = Mk("PIX_Ref_Eyes", "Eyes", "_bc", "_xx");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            inst.name = "PIX_Referee";
            Assign(inst, r => r.name.Contains("Uniform") ? uniform : r.name.Contains("Arms") ? arms : r.name.Contains("Head") ? head : r.name.Contains("Eyes") ? eyes : null);
            Save(inst, "PIX_Referee");
        }

        // ---------------------------------------------------------------- crowd (colour-mask atlases -> baked recoloured copies)
        static Texture2D Recolor(string srcPath, string dstPath, Color red, Color green, Color blue, Color magenta)
        {
            var ti = AssetImporter.GetAtPath(srcPath) as TextureImporter;
            if (ti == null) return null;
            bool wasReadable = ti.isReadable; int wasMax = ti.maxTextureSize;
            ti.isReadable = true; ti.maxTextureSize = 2048; ti.SaveAndReimport();
            var src = AssetDatabase.LoadAssetAtPath<Texture2D>(srcPath);
            var px = src.GetPixels32();
            var res = new Color32[px.Length];
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
                Color target = Color.clear; bool hit = false;
                // pure mask colours used by the crowd atlas: salmon (217,91,91)=primary, green (51,204,51)=secondary, blue (45,65,155)=tertiary, magenta (204,68,204)=accent
                if (Dist(c, 217, 91, 91) < 40) { target = red; hit = true; }
                else if (Dist(c, 51, 204, 51) < 55) { target = green; hit = true; }
                else if (Dist(c, 45, 65, 155) < 50) { target = blue; hit = true; }
                else if (Dist(c, 204, 68, 204) < 55) { target = magenta; hit = true; }
                if (hit)
                {
                    float lum = 0.75f + 0.5f * (r * 0.3f + g * 0.5f + b * 0.2f);
                    res[i] = new Color(target.r * lum, target.g * lum, target.b * lum, 1f);
                }
                else res[i] = c;
            }
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            dst.SetPixels32(res);
            File.WriteAllBytes(dstPath, dst.EncodeToPNG());
            Object.DestroyImmediate(dst);
            ti.isReadable = wasReadable; ti.maxTextureSize = wasMax; ti.SaveAndReimport();
            AssetDatabase.ImportAsset(dstPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(dstPath);
        }

        static float Dist(Color32 c, int r, int g, int b) { return Mathf.Abs(c.r - r) + Mathf.Abs(c.g - g) + Mathf.Abs(c.b - b); }

        [MenuItem("PIX Football/Assets/Build Crowd Prefabs")]
        public static void BuildCrowd()
        {
            string crowd = Root + "/Characters/Crowd";
            // (prefab name, fbx, mask texture, normal texture)
            var sets = new[]
            {
                new[] { "Male01", crowd + "/Male1/FBX/CrowdMale01.fbx", crowd + "/Male1/Texture/C_male_BaseColorMask.png", crowd + "/Male1/Texture/C_male_Normal.png" },
                new[] { "MaleLarge", crowd + "/Male2/FBX/CrowdMaleLarge.fbx", crowd + "/Male2/Texture/C_maleLarge_BaseColorMask.png", crowd + "/Male2/Texture/C_maleLarge_Normal.png" },
                new[] { "Female01", crowd + "/Girl/FBX/CrowdFemale01.fbx", crowd + "/Girl/Texture/C_female_BaseColorMask.png", crowd + "/Girl/Texture/C_female_Normal.png" },
            };
            var teamA = new Color(0.55f, 0.08f, 0.1f); var teamB = new Color(0.9f, 0.9f, 0.92f);
            foreach (var s in sets)
            {
                var fbx = Load(s[1]);
                if (fbx == null || !File.Exists(s[2])) { Debug.LogWarning("[ExtraPrefabBuilder] crowd set incomplete: " + s[0]); continue; }
                string baked = Path.GetDirectoryName(s[2]).Replace('\\', '/') + "/PIX_" + s[0] + "_Baked.png";
                var tex = Recolor(s[2], baked, teamA, teamB, new Color(0.15f, 0.2f, 0.4f), new Color(0.2f, 0.2f, 0.2f));
                var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(s[3]);
                var mat = MaterialFactory.CreateLit(MaterialFactory.MatDir + "/Crowd/PIX_Crowd_" + s[0] + ".mat", tex, null, nrm, Color.white, 0f, 0.2f);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                inst.name = "PIX_Crowd_" + s[0];
                Assign(inst, r => mat);
                Save(inst, "PIX_Crowd_" + s[0]);
            }
        }

        // ---------------------------------------------------------------- accessories (customisation kit)
        [MenuItem("PIX Football/Assets/Build Accessory Prefabs")]
        public static void BuildAccessories()
        {
            string[] roots = { Root + "/Characters/Customization", Root + "/Characters/Coaches/Accessories" };
            int n = 0;
            foreach (string r in roots)
            {
                if (!AssetDatabase.IsValidFolder(r)) continue;
                foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { r }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    var fbx = Load(p);
                    if (fbx == null) continue;
                    string folder = Path.GetDirectoryName(p).Replace('\\', '/');
                    string bn = Path.GetFileNameWithoutExtension(p);
                    var b = MaterialFactory.FindTextureNear(folder, bn, new[] { "basecolor", "base_color", "basemap", "albedo" });
                    var nm = MaterialFactory.FindTextureNear(folder, bn, new[] { "normal_opengl", "normal" });
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                    string pname = "PIX_Acc_" + bn.Replace(' ', '_');
                    if (File.Exists(PrefabDir + "/" + pname + ".prefab")) { Object.DestroyImmediate(inst); continue; }
                    inst.name = pname;
                    if (b != null)
                    {
                        var mat = MaterialFactory.CreateLit(MaterialFactory.MatDir + "/Accessories/" + pname + ".mat", b, null, nm, Color.white, 0f, 0.3f);
                        Assign(inst, rr => mat);
                    }
                    Save(inst, pname);
                    n++;
                }
            }
            Debug.Log("[ExtraPrefabBuilder] accessory prefabs: " + n);
        }

        // ---------------------------------------------------------------- helmets / face masks / visors as standalone prefabs
        [MenuItem("PIX Football/Assets/Build Helmet Prefabs")]
        public static void BuildHelmets()
        {
            var helmetMat = MaterialFactory.FindMaterial("New_Helmet_MAT");
            Material fm, vis; PlayerPrefabBuilder.EnsureSharedMaterials(out fm, out vis);
            foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { Root + "/Helmets/New" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.Contains("Helmet Elements FBX") || p.Contains("/Face Masks/") && false) continue; // duplicates of the main folders
                string bn = Path.GetFileNameWithoutExtension(p);
                string size = p.Contains("/Small/") ? "S" : p.Contains("/Large/") ? "L" : "M";
                string pname = "PIX_Helmet_" + size + "_" + bn;
                if (File.Exists(PrefabDir + "/" + pname + ".prefab")) continue;
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(Load(p));
                inst.name = pname;
                Material m = bn.ToLower().Contains("visor") ? vis : bn.StartsWith("FM") ? fm : helmetMat;
                Assign(inst, r => m);
                foreach (var lg in inst.GetComponentsInChildren<LODGroup>(true)) lg.enabled = false;
                var rs = inst.GetComponentsInChildren<Renderer>(true).OrderBy(r => r.name).ToArray();
                for (int i = 1; i < rs.Length; i++) rs[i].gameObject.SetActive(false);
                Save(inst, pname);
            }
        }

        public static void BuildAll() { BuildReferee(); BuildCrowd(); BuildHelmets(); BuildAccessories(); }
    }
}
