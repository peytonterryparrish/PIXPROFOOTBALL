using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering.HighDefinition;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>Creates HDRP/Lit materials from texture sets and repairs pink materials.</summary>
    public static class MaterialFactory
    {
        public const string Root = "Assets/_Licensed/DraftFever";
        public const string MatDir = Root + "/Materials";

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        public static Material FindMaterial(string name)
        {
            foreach (string g in AssetDatabase.FindAssets(name + " t:Material", new[] { Root }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<Material>(p);
            }
            return null;
        }

        public static Material CreateLit(string assetPath, Texture baseMap, Texture maskMap, Texture normalMap, Color? tint = null,
                                         float metallic = 0f, float smoothness = 0.4f, Vector2? tiling = null)
        {
            EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            var m = existing != null ? existing : new Material(Shader.Find("HDRP/Lit"));
            m.shader = Shader.Find("HDRP/Lit");
            if (baseMap != null) m.SetTexture("_BaseColorMap", baseMap);
            if (maskMap != null) { m.SetTexture("_MaskMap", maskMap); m.SetFloat("_Metallic", 1f); m.SetFloat("_Smoothness", 1f); m.SetFloat("_SmoothnessRemapMin", 0f); m.SetFloat("_SmoothnessRemapMax", 1f); }
            else { m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", smoothness); }
            if (normalMap != null) { m.SetTexture("_NormalMap", normalMap); m.SetFloat("_NormalScale", 1f); }
            m.SetColor("_BaseColor", tint ?? Color.white);
            if (tiling.HasValue) { m.SetTextureScale("_BaseColorMap", tiling.Value); m.SetTextureScale("_NormalMap", tiling.Value); m.SetTextureScale("_MaskMap", tiling.Value); }
            HDShaderUtils.ResetMaterialKeywords(m);
            if (existing == null) AssetDatabase.CreateAsset(m, assetPath); else EditorUtility.SetDirty(m);
            return m;
        }

        public static Material CreateSolid(string assetPath, Color c, float metallic, float smoothness, bool transparent = false)
        {
            EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            var m = existing != null ? existing : new Material(Shader.Find("HDRP/Lit"));
            m.shader = Shader.Find("HDRP/Lit");
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            if (transparent)
            {
                m.SetFloat("_SurfaceType", 1f);
                m.SetFloat("_BlendMode", 0f);
                m.SetFloat("_ZWrite", 0f);
                m.SetFloat("_AlphaCutoffEnable", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
            }
            HDShaderUtils.ResetMaterialKeywords(m);
            if (existing == null) AssetDatabase.CreateAsset(m, assetPath); else EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Finds texture assets in a folder tree by loose name patterns.</summary>
        public static Texture2D FindTexture(string folder, params string[] containsAny)
        {
            foreach (string g in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                string n = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                if (containsAny.Any(c => n.Contains(c.ToLowerInvariant()))) return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            return null;
        }

        /// <summary>Find a texture whose name contains any pattern AND shares the given prefix with the model name (best effort).</summary>
        public static Texture2D FindTextureNear(string folder, string modelBase, string[] patterns)
        {
            Texture2D best = null; int bestScore = -1;
            foreach (string g in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                string n = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                if (!patterns.Any(c => n.Contains(c))) continue;
                int score = 0;
                string mb = modelBase.ToLowerInvariant();
                for (int i = 1; i <= mb.Length && i <= n.Length; i++) { if (n[i - 1] == mb[i - 1]) score++; else break; }
                if (score > bestScore) { bestScore = score; best = AssetDatabase.LoadAssetAtPath<Texture2D>(p); }
            }
            return best;
        }

        public static int FixPinkMaterials()
        {
            int fixedCount = 0;
            foreach (string g in AssetDatabase.FindAssets("t:Material", new[] { Root }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) continue;
                bool bad = m.shader == null || m.shader.name.Contains("InternalError") || !m.shader.isSupported;
                if (!bad) continue;
                // remap: keep common textures
                Texture baseTex = null;
                foreach (string pn in new[] { "_BaseColorMap", "_BaseMap", "_MainTex", "_Albedo", "_BaseColorMask", "_Base_Color" })
                    if (m.HasProperty(pn) && m.GetTexture(pn) != null) { baseTex = m.GetTexture(pn); break; }
                Texture nrm = null;
                foreach (string pn in new[] { "_NormalMap", "_BumpMap", "_Normal" })
                    if (m.HasProperty(pn) && m.GetTexture(pn) != null) { nrm = m.GetTexture(pn); break; }
                m.shader = Shader.Find("HDRP/Lit");
                if (baseTex != null) m.SetTexture("_BaseColorMap", baseTex);
                if (nrm != null) m.SetTexture("_NormalMap", nrm);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Smoothness", 0.25f);
                HDShaderUtils.ResetMaterialKeywords(m);
                EditorUtility.SetDirty(m);
                fixedCount++;
            }
            AssetDatabase.SaveAssets();
            return fixedCount;
        }
    }
}
