using System.Collections.Generic;
using System.IO;
using System.Linq;
using PixFootball.Art;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.7 presentation sets and stadium kit: builds HDRP materials (the PNGs that the Blender prep scripts wrote next to every FBX), prefabs with their screen components, and an idle card for the screens.
    /// Menu: PIX Football/Presentation/Build sets. Batch: PixFootball.EditorTools.PresentationSetBuilder.BuildAll. Sources: Tools/Blender/pres_*.py (Docs/QA/v070/graphics_v070.md).
    /// </summary>
    public static class PresentationSetBuilder
    {
        public const string Root = "Assets/_Licensed/PIX";
        public const string PresDir = Root + "/Presentation", KitDir = Root + "/StadiumKit";
        public const string PrefabDir = Root + "/PresentationPrefabs", MatDir = Root + "/PresentationMaterials";

        [MenuItem("PIX Football/Presentation/Build sets")]
        public static void BuildAll()
        {
            MaterialFactory.EnsureFolder(PrefabDir); MaterialFactory.EnsureFolder(MatDir);
            ImportTextures(PresDir); ImportTextures(KitDir);
            var idle = IdleCard();
            // jumbotrons, tunnel, studio: indoor / neutral materials (HDRP/Lit); stadium kit: PIX/Lit (wet / snow aware)
            foreach (string d in new[] { "Squared", "Hexagon", "Double" }) Build(PresDir + "/Jumbotron/PIX_Jumbotron_" + d + ".fbx", false, 3500f, idle);
            Build(PresDir + "/Tunnel/PIX_Tunnel.fbx", false, 0f, idle);
            Build(PresDir + "/NewsStudio/PIX_NewsStudio.fbx", false, 300f, idle);
            foreach (string m in new[] { "PIX_RoofStraight", "PIX_RoofArc90", "PIX_LightTower", "PIX_RibbonBoard", "PIX_MediaBox", "PIX_TunnelMouth", "PIX_ScoreboardFrame" }) Build(KitDir + "/" + m + ".fbx", true, 3500f, idle);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("[Presentation] sets built in " + PrefabDir);
        }

        /// <summary>Batch helper: builds the sets, then logs the stadium bounds (placement of the kit).</summary>
        public static void BuildAndProbe() { BuildAll(); StadiumProbe.Dump(); }

        // ------------------------------------------------------------------------------------------------ textures
        static void ImportTextures(string dir)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(p) as TextureImporter; if (ti == null) continue;
                string n = Path.GetFileNameWithoutExtension(p);
                bool normal = n.EndsWith("_Normal"), mask = n.EndsWith("_Mask");
                bool dirty = false;
                var type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                if (ti.textureType != type) { ti.textureType = type; dirty = true; }
                bool srgb = !(normal || mask);
                if (ti.sRGBTexture != srgb) { ti.sRGBTexture = srgb; dirty = true; }
                if (ti.maxTextureSize != 2048) { ti.maxTextureSize = 2048; dirty = true; }
                if (ti.textureCompression != TextureImporterCompression.CompressedHQ) { ti.textureCompression = TextureImporterCompression.CompressedHQ; dirty = true; }
                if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }
                if (ti.anisoLevel < 8) { ti.anisoLevel = 8; dirty = true; }
                if (dirty) ti.SaveAndReimport();
            }
        }

        /// <summary>Dark idle card (scan lines, a blue to violet gradient and a vignette, no lettering): the screens show it until the game feeds them.</summary>
        static Texture2D IdleCard()
        {
            string path = MatDir + "/PIX_ScreenIdle.png";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            int w = 512, h = 288;
            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float gx = (float)x / w, gy = (float)y / h;
                    Color c = Color.Lerp(new Color(0.03f, 0.06f, 0.18f), new Color(0.12f, 0.05f, 0.22f), gx * 0.6f + gy * 0.4f);
                    float scan = 0.85f + 0.15f * Mathf.Sin(y * 1.2f);
                    float vig = 1f - 0.5f * Mathf.Pow(Mathf.Abs(gx - 0.5f) * 2f, 2.5f) - 0.3f * Mathf.Pow(Mathf.Abs(gy - 0.5f) * 2f, 2.5f);
                    t.SetPixel(x, y, c * scan * Mathf.Max(0.2f, vig) * 2.2f);
                }
            t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter; if (ti != null) { ti.sRGBTexture = true; ti.mipmapEnabled = false; ti.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Texture2D Tex(string dir, string name) { return AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "/" + name + ".png"); }

        // ------------------------------------------------------------------------------------------------ materials
        static Material MakeMaterial(string module, string dir, string srcName, bool outdoor, float screenNits, Texture idle)
        {
            string path = MatDir + "/" + module + "_" + srcName + ".mat";
            string lower = srcName.ToLowerInvariant();
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = m == null;
            if (create) m = new Material(Shader.Find("HDRP/Lit"));
            else m.shader = Shader.Find("HDRP/Lit");
            bool screen = lower.Contains("screen"), glass = lower.Contains("glass") || lower.Contains("lens");
            bool lamp = lower.Contains("lamps") || lower.EndsWith("_light") || lower.EndsWith("tunnellight") || lower == "pix_news_lamp";
            // texture set: material specific first (PIX_News_Desk_*), else the module's set (PIX_RoofStraight_*)
            Texture2D bc = Tex(dir, srcName + "_BaseColor"), nm = Tex(dir, srcName + "_Normal"), mk = Tex(dir, srcName + "_Mask");
            if (bc == null && !screen && !glass && !lamp) { bc = Tex(dir, module + "_BaseColor"); nm = Tex(dir, module + "_Normal"); mk = Tex(dir, module + "_Mask"); }
            if (srcName.EndsWith("TunnelBlack")) { bc = Tex(dir, "PIX_TunnelBlack_BaseColor"); nm = Tex(dir, "PIX_TunnelBlack_Normal"); }
            if (srcName.EndsWith("TunnelLogo")) bc = Tex(dir, "PIX_TunnelLogo_placeholder");
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_BaseColorMap", bc); m.SetTexture("_NormalMap", nm); m.SetTexture("_MaskMap", mk);
            m.SetFloat("_Metallic", mk != null ? 1f : 0f); m.SetFloat("_Smoothness", mk != null ? 1f : 0.5f);
            if (mk != null) { m.SetFloat("_SmoothnessRemapMin", 0f); m.SetFloat("_SmoothnessRemapMax", 1f); m.SetFloat("_AORemapMin", 0f); m.SetFloat("_AORemapMax", 1f); }
            if (bc == null)
            {
                Color c = new Color(0.55f, 0.57f, 0.6f);
                if (lower.Contains("canvas")) c = new Color(0.82f, 0.83f, 0.86f);
                else if (lower.Contains("frame") || lower.Contains("steel")) c = new Color(0.16f, 0.17f, 0.19f);
                else if (lower.Contains("membrane")) c = new Color(0.8f, 0.82f, 0.86f);
                m.SetColor("_BaseColor", c);
                if (lower.Contains("frame")) { m.SetFloat("_Metallic", 0.9f); m.SetFloat("_Smoothness", 0.4f); }
            }
            m.DisableKeyword("_EMISSIVE_COLOR_MAP");
            m.SetColor("_EmissiveColor", Color.black); m.SetTexture("_EmissiveColorMap", null);
            if (screen)
            {
                float n = Mathf.Max(screenNits, 150f);
                m.SetColor("_BaseColor", Color.black); m.SetTexture("_BaseColorMap", null); m.SetTexture("_MaskMap", null); m.SetFloat("_Metallic", 0f); m.SetFloat("_Smoothness", 0.85f);
                m.SetTexture("_EmissiveColorMap", idle); m.SetColor("_EmissiveColor", Color.white * n); m.EnableKeyword("_EMISSIVE_COLOR_MAP");
                m.SetFloat("_UseEmissiveIntensity", 1f); m.SetFloat("_EmissiveIntensityUnit", 0f); m.SetFloat("_EmissiveIntensity", n); m.SetColor("_EmissiveColorLDR", Color.white);
            }
            else if (lamp)
            {
                float n = outdoor ? 6000f : 900f;
                m.SetColor("_BaseColor", new Color(0.9f, 0.9f, 0.85f));
                m.SetColor("_EmissiveColor", new Color(1f, 0.96f, 0.86f) * n); m.SetFloat("_UseEmissiveIntensity", 1f); m.SetFloat("_EmissiveIntensity", n); m.SetColor("_EmissiveColorLDR", new Color(1f, 0.96f, 0.86f));
            }
            if (glass)
            {
                m.SetFloat("_SurfaceType", 1f); m.SetFloat("_BlendMode", 0f); m.SetFloat("_ZWrite", 0f);
                m.SetColor("_BaseColor", new Color(0.55f, 0.65f, 0.72f, 0.22f)); m.SetFloat("_Smoothness", 0.95f); m.SetFloat("_Metallic", 0f);
                m.renderQueue = 3000;
            }
            HDShaderUtils.ResetMaterialKeywords(m);
            if (create) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            if (outdoor && !screen && !lamp && !glass)
            {
                var pix = V6ArtBuilder.CloneToPix(m, MatDir + "/PIX_" + module + "_" + srcName + ".mat", 0);
                pix.SetFloat("_PixSnowAmount", 0.4f); pix.SetFloat("_PixWetAmount", 0.7f); EditorUtility.SetDirty(pix);
                return pix;
            }
            return m;
        }

        // ------------------------------------------------------------------------------------------------ prefabs
        static void Build(string fbxPath, bool outdoor, float screenNits, Texture idle)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (model == null) { Debug.LogWarning("[Presentation] missing " + fbxPath); return; }
            string module = Path.GetFileNameWithoutExtension(fbxPath), dir = Path.GetDirectoryName(fbxPath).Replace('\\', '/');
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = module;
            var cache = new Dictionary<string, Material>();
            var screens = new List<Renderer>(); Renderer canvas = null; var plates = new List<Renderer>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    string n = mats[i].name;
                    Material to;
                    if (!cache.TryGetValue(n, out to)) cache[n] = to = MakeMaterial(module, dir, n, outdoor, screenNits, idle);
                    mats[i] = to;
                    if (n.Contains("Screen") && !screens.Contains(r)) screens.Add(r);
                }
                r.sharedMaterials = mats;
                if (r.name.StartsWith("Logo_Plate")) plates.Add(r);
                if (r.name == "Tunnel_Canvas") canvas = r;
                r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                r.gameObject.isStatic = true;
                if (module.Contains("Roof")) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // the roof is a fascia over the stands: it must not shadow the pitch
            }
            if (screens.Count > 0)
            {
                var ps = inst.AddComponent<PresentationScreen>(); ps.screens = screens.ToArray(); ps.idle = idle; ps.nits = screenNits > 0f ? screenNits : 2500f;
            }
            if (canvas != null) { var td = inst.AddComponent<TunnelDressing>(); td.canvas = canvas; td.logoPlates = plates.ToArray(); }
            string prefab = PrefabDir + "/" + module + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(inst, prefab);
            Object.DestroyImmediate(inst);
            Debug.Log("[Presentation] " + prefab + " screens=" + screens.Count + " materials=" + cache.Count);
        }
    }
}
