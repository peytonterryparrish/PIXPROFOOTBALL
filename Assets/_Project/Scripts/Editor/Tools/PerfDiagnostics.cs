using System.IO;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>v0.4 performance diagnostics: what one match player costs (renderers, vertices, skinning, materials) and the HDRP asset settings of every quality level.</summary>
    public static class PerfDiagnostics
    {
        public static void Players()
        {
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            var sb = new StringBuilder();
            foreach (string key in new[] { "player_small", "player_medium", "player_large", "referee", "coach_a", "crowd_male01" })
            {
                var prefab = lib.Prefab(key);
                if (prefab == null) { sb.AppendLine("NO PREFAB " + key); continue; }
                long verts = 0, tris = 0; int smr = 0, mr = 0, mats = 0, bones = 0;
                sb.AppendLine("=== " + key);
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    Mesh m = null;
                    var s = r as SkinnedMeshRenderer;
                    if (s != null) { m = s.sharedMesh; smr++; bones += s.bones.Length; } else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; mr++; }
                    int v = m != null ? m.vertexCount : 0, t = m != null ? (int)(m.GetIndexCount(0) / 3) : 0;
                    if (m != null) { verts += v; for (int i = 0; i < m.subMeshCount; i++) tris += m.GetIndexCount(i) / 3; }
                    mats += r.sharedMaterials.Length;
                    sb.AppendLine("  " + r.name + " active=" + r.gameObject.activeSelf + " enabled=" + r.enabled + (s != null ? " SKINNED bones=" + s.bones.Length + " quality=" + s.quality + " offscreen=" + s.updateWhenOffscreen : " static") + " verts=" + v + " subMeshes=" + (m != null ? m.subMeshCount : 0)
                                  + " blend=" + (m != null ? m.blendShapeCount : 0) + " mats=" + r.sharedMaterials.Length + " shader=" + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "null") + " shadows=" + r.shadowCastingMode + " lod=" + (r.GetComponentInParent<LODGroup>() != null));
                }
                sb.AppendLine("  TOTAL skinned=" + smr + " static=" + mr + " materials=" + mats + " verts=" + verts + " tris=" + tris + " bones=" + bones);
                var anim = prefab.GetComponent<Animator>();
                if (anim != null) sb.AppendLine("  animator culling=" + anim.cullingMode + " applyRoot=" + anim.applyRootMotion);
            }
            sb.AppendLine("QUALITY levels: " + string.Join(", ", QualitySettings.names) + " current=" + QualitySettings.GetQualityLevel());
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                sb.AppendLine("  [" + i + "] " + QualitySettings.names[i] + " pipeline=" + (QualitySettings.renderPipeline != null ? AssetDatabase.GetAssetPath(QualitySettings.renderPipeline) : "default") + " lodBias=" + QualitySettings.lodBias + " maxLod=" + QualitySettings.maximumLODLevel + " skinWeights=" + QualitySettings.skinWeights + " shadowDist=" + QualitySettings.shadowDistance + " vsync=" + QualitySettings.vSyncCount);
            }
            sb.AppendLine("default pipeline: " + (UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != null ? AssetDatabase.GetAssetPath(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline) : "none"));
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/perf_players.txt", sb.ToString());
            Debug.Log("[Perf] wrote Logs/perf_players.txt");
        }
    }
}
