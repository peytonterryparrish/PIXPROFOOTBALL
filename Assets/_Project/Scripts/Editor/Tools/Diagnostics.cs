using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    public static class Diagnostics
    {
        [MenuItem("PIX Football/Diagnostics/Dump Stadium Materials")]
        public static void DumpStadium()
        {
            var sb = new StringBuilder();
            foreach (string pn in new[] { "PIX_Stadium_Pro", "PIX_Stadium_College" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Licensed/DraftFever/Prefabs/" + pn + ".prefab");
                if (prefab == null) { sb.AppendLine("NO PREFAB " + pn); continue; }
                sb.AppendLine("=== " + pn);
                var mats = new Dictionary<Material, List<string>>();
                int rendererCount = 0; long tris = 0;
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    rendererCount++;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        if (!mats.TryGetValue(m, out var l)) mats[m] = l = new List<string>();
                        if (l.Count < 3) l.Add(r.name);
                    }
                }
                sb.AppendLine("renderers=" + rendererCount + " tris=" + tris);
                foreach (var kv in mats)
                {
                    var m = kv.Key;
                    sb.AppendLine("MAT " + AssetDatabase.GetAssetPath(m) + " shader=" + (m.shader != null ? m.shader.name : "null") + " users=" + string.Join(",", kv.Value));
                    var so = m.shader;
                    int pc = so.GetPropertyCount();
                    for (int i = 0; i < pc; i++)
                        if (so.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                        {
                            var t = m.GetTexture(so.GetPropertyName(i));
                            if (t != null) sb.AppendLine("    " + so.GetPropertyName(i) + " = " + AssetDatabase.GetAssetPath(t) + " " + t.width + "x" + t.height);
                        }
                }
                // hierarchy summary
                foreach (Transform t in prefab.transform) Walk(t, 0, sb, 3);
            }
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/stadium_dump.txt", sb.ToString());
            Debug.Log("[Diag] wrote Logs/stadium_dump.txt");
        }

        static void Walk(Transform t, int depth, StringBuilder sb, int max)
        {
            var b = new Bounds();
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length > 0) { b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); }
            sb.AppendLine(new string(' ', depth * 2) + t.name + " children=" + t.childCount + " renderers=" + rs.Length + " center=" + b.center + " size=" + b.size);
            if (depth + 1 >= max) return;
            if (t.childCount > 40) return;
            foreach (Transform c in t) Walk(c, depth + 1, sb, max);
        }

        [MenuItem("PIX Football/Diagnostics/Dump Clips")]
        public static void DumpClips()
        {
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            var sb = new StringBuilder();
            if (lib == null) { File.WriteAllText("Logs/clips_dump.txt", "NO LIB"); return; }
            sb.AppendLine("avatar=" + lib.playerAvatar + " ctrl=" + lib.playerController);
            foreach (var key in new[] { "idle", "idle_alt", "jog", "run", "sprint", "backpedal", "strafe_left", "strafe_right", "shuffle_left", "shuffle_right", "shuffle_slow", "juke_right", "spin_move", "stiff_arm", "stiff_arm_left", "stiff_arm_right", "hurdle", "dive_forward", "truck", "celebrate_td", "celebrate_td_2", "cut_left", "cut_right", "stance_2pt", "slowdown", "be_tackled", "tackle_wrap" })
            {
                int n = lib.ClipVariantCount(key);
                for (int i = 0; i < n; i++)
                {
                    var c = lib.ClipVariant(key, i);
                    string p = AssetDatabase.GetAssetPath(c);
                    // estimate root speed
                    sb.AppendLine(key + "[" + i + "] " + c.name + " len=" + c.length.ToString("0.00") + " loop=" + c.isLooping + " fps=" + c.frameRate + " human=" + c.isHumanMotion + " path=" + p);
                }
            }
            File.WriteAllText("Logs/clips_dump.txt", sb.ToString());
            Debug.Log("[Diag] wrote Logs/clips_dump.txt");
        }

        [MenuItem("PIX Football/Diagnostics/Dump Diffusion")]
        public static void DumpDiffusion()
        {
            var sb = new StringBuilder();
            var gsType = System.Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings, Unity.RenderPipelines.HighDefinition.Runtime");
            var inst = gsType.GetProperty("instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            var gs = inst.GetValue(null) as ScriptableObject;
            sb.AppendLine("gs=" + (gs != null ? AssetDatabase.GetAssetPath(gs) : "null"));
            if (gs != null)
            {
                var so = new SerializedObject(gs);
                var prop = so.FindProperty("diffusionProfileSettingsList");
                for (int i = 0; prop != null && i < prop.arraySize; i++)
                    sb.AppendLine("dp[" + i + "]=" + prop.GetArrayElementAtIndex(i).objectReferenceValue);
            }
            File.WriteAllText("Logs/diffusion_dump.txt", sb.ToString());
        }

        public static void RunAll() { DumpStadium(); DumpClips(); DumpDiffusion(); }
    }
}
namespace PixFootball.EditorTools
{
    public static class GoalPostDiag
    {
        public static void Run()
        {
            var sb = new System.Text.StringBuilder();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Licensed/DraftFever/Prefabs/PIX_Prop_goalPost.prefab");
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var rs = inst.GetComponentsInChildren<Renderer>(false);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            sb.AppendLine("bounds center=" + b.center + " size=" + b.size + " renderers=" + rs.Length);
            foreach (var r in rs)
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null) continue;
                var m = mf.sharedMesh; var vs = m.vertices; var mtx = r.transform.localToWorldMatrix;
                // slice profile along Y
                float minY = b.min.y, maxY = b.max.y; int n = 30;
                var minX = new float[n]; var maxX = new float[n]; var minZ = new float[n]; var maxZ = new float[n];
                for (int i = 0; i < n; i++) { minX[i] = minZ[i] = 1e9f; maxX[i] = maxZ[i] = -1e9f; }
                foreach (var v in vs) { var w = mtx.MultiplyPoint3x4(v); int k = Mathf.Clamp((int)((w.y - minY) / (maxY - minY) * n), 0, n - 1); minX[k] = Mathf.Min(minX[k], w.x); maxX[k] = Mathf.Max(maxX[k], w.x); minZ[k] = Mathf.Min(minZ[k], w.z); maxZ[k] = Mathf.Max(maxZ[k], w.z); }
                sb.AppendLine(r.name + " verts=" + vs.Length);
                for (int i = 0; i < n; i++) if (minX[i] < 1e8f) sb.AppendLine("  y=" + (minY + (maxY - minY) * i / n).ToString("0.00") + " x:[" + minX[i].ToString("0.00") + "," + maxX[i].ToString("0.00") + "] z:[" + minZ[i].ToString("0.00") + "," + maxZ[i].ToString("0.00") + "]");
            }
            Object.DestroyImmediate(inst);
            File.WriteAllText("Logs/goalpost_dump.txt", sb.ToString());
        }
    }
}
