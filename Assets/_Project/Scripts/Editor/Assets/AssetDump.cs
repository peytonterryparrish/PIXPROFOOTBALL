using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>Diagnostic dumps used while curating the import (clip names, model hierarchies, material shaders).</summary>
    public static class AssetDump
    {
        const string Root = "Assets/_Licensed/DraftFever";

        static void Write(string name, string text)
        {
            Directory.CreateDirectory("Docs/_dumps");
            File.WriteAllText("Docs/_dumps/" + name, text);
        }

        [MenuItem("PIX Football/Assets/Dump/Clips")]
        public static void DumpClips()
        {
            var sb = new StringBuilder();
            string[] guids = AssetDatabase.FindAssets("t:Model", new[] { Root + "/Animations" });
            int files = 0, clips = 0;
            foreach (string g in guids.OrderBy(AssetDatabase.GUIDToAssetPath))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                files++;
                var all = AssetDatabase.LoadAllAssetsAtPath(path);
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                Avatar av = all.OfType<Avatar>().FirstOrDefault();
                sb.AppendLine("# " + path.Substring(Root.Length + 1) + " type=" + (mi != null ? mi.animationType.ToString() : "?") + " avatar=" + (av != null ? (av.isValid ? "valid" : "INVALID") + (av.isHuman ? "/human" : "/generic") : "none"));
                foreach (var c in all.OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")))
                {
                    clips++;
                    sb.AppendLine(string.Format("  {0}\t{1:0.00}s\tloop={2}\tfps={3}", c.name, c.length, c.isLooping, c.frameRate));
                }
            }
            sb.Insert(0, "files=" + files + " clips=" + clips + "\n");
            Write("clips.txt", sb.ToString());
            Debug.Log("[AssetDump] clips dumped: files=" + files + " clips=" + clips);
        }

        static void DumpTransform(StringBuilder sb, Transform t, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            var comps = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name);
            sb.AppendLine(new string(' ', depth * 2) + t.name + "  [" + string.Join(",", comps) + "]  pos=" + t.localPosition.ToString("F3") + " scl=" + t.localScale.ToString("F3"));
            foreach (Transform c in t) DumpTransform(sb, c, depth + 1, maxDepth);
        }

        [MenuItem("PIX Football/Assets/Dump/Models")]
        public static void DumpModels()
        {
            var sb = new StringBuilder();
            string[] roots = { "Characters/Bodies", "Characters/MainPlayerDelivery", "Characters/FacialRig", "Characters/Heads", "Packages/MainPlayer", "Helmets", "Characters/Customization", "Characters/Coaches", "Characters/Referee", "Characters/Crowd", "Props", "Packages/Stadium_Regular", "Packages/Stadium_College", "Packages/StadiumKit", "Packages/HairSystem" };
            foreach (string r in roots)
            {
                string folder = Root + "/" + r;
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                string[] guids = AssetDatabase.FindAssets("t:Model", new[] { folder });
                sb.AppendLine("==== " + r + " (" + guids.Length + " models)");
                foreach (string g in guids.OrderBy(AssetDatabase.GUIDToAssetPath).Take(80))
                {
                    string path = AssetDatabase.GUIDToAssetPath(g);
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go == null) continue;
                    var rends = go.GetComponentsInChildren<Renderer>(true);
                    Bounds b = new Bounds();
                    bool first = true;
                    foreach (var rd in rends) { if (first) { b = rd.bounds; first = false; } else b.Encapsulate(rd.bounds); }
                    int bones = go.GetComponentsInChildren<Transform>(true).Length;
                    sb.AppendLine("-- " + path.Substring(Root.Length + 1) + "  size=" + b.size.ToString("F2") + " center=" + b.center.ToString("F2") + " transforms=" + bones + " renderers=" + rends.Length);
                    foreach (var rd in rends.Take(30))
                    {
                        var mats = rd.sharedMaterials.Select(m => m == null ? "null" : m.name + "<" + (m.shader != null ? m.shader.name : "noshader") + ">");
                        sb.AppendLine("     R " + rd.name + " " + rd.GetType().Name + " mats=" + string.Join("; ", mats));
                    }
                }
            }
            Write("models.txt", sb.ToString());
            Debug.Log("[AssetDump] models dumped");
        }

        [MenuItem("PIX Football/Assets/Dump/Materials")]
        public static void DumpMaterials()
        {
            var sb = new StringBuilder();
            var byShader = new Dictionary<string, int>();
            foreach (string g in AssetDatabase.FindAssets("t:Material", new[] { Root }).OrderBy(AssetDatabase.GUIDToAssetPath))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                string sh = m.shader != null ? m.shader.name : "NULL";
                bool bad = m.shader == null || sh.Contains("InternalError") || !m.shader.isSupported;
                int c; byShader.TryGetValue(sh, out c); byShader[sh] = c + 1;
                sb.AppendLine((bad ? "PINK " : "ok   ") + path.Substring(Root.Length + 1) + "  <" + sh + ">");
            }
            sb.AppendLine("---- shader counts");
            foreach (var kv in byShader.OrderByDescending(k => k.Value)) sb.AppendLine(kv.Value + "	" + kv.Key);
            Write("materials.txt", sb.ToString());
            Debug.Log("[AssetDump] materials dumped");
        }

        public static void DumpAll() { DumpClips(); DumpModels(); DumpMaterials(); }

        [MenuItem("PIX Football/Assets/Dump/Hierarchy of selection")]
        public static void DumpSelection()
        {
            var sb = new StringBuilder();
            foreach (var o in Selection.gameObjects) DumpTransform(sb, o.transform, 0, 40);
            Write("selection.txt", sb.ToString());
        }

        public static void DumpBonePathsFromArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            string path = "";
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-dumpPaths") path = args[i + 1];
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var sb = new StringBuilder();
            string[] want = { "root.x", "spine_01.x", "spine_02.x", "spine_03.x", "neck.x", "head.x", "shoulder.l", "arm.l", "arm_stretch.l", "forearm.l", "forearm_stretch.l", "hand.l", "hand_ref.l", "thigh.l", "thigh_stretch.l", "leg.l", "leg_stretch.l", "foot.l", "toes_01.l", "index1.l", "thumb1.l", "c_index1.l" };
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (!want.Contains(t.name)) continue;
                var p = new List<string>(); for (var x = t; x != null; x = x.parent) p.Add(x.name); p.Reverse();
                sb.AppendLine(t.name + "  <=  " + string.Join("/", p.ToArray()));
            }
            Write("bonepaths.txt", sb.ToString());
        }

        /// <summary>CLI: -executeMethod PixFootball.EditorTools.AssetDump.DumpHierarchyFromArgs -dumpPaths a;b</summary>
        public static void DumpHierarchyFromArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            string paths = "";
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-dumpPaths") paths = args[i + 1];
            var sb = new StringBuilder();
            foreach (string p in paths.Split(';'))
            {
                if (string.IsNullOrEmpty(p)) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                sb.AppendLine("##### " + p);
                if (go == null) { sb.AppendLine("  (null)"); continue; }
                DumpTransform(sb, go.transform, 0, 40);
                var av = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Avatar>().FirstOrDefault();
                sb.AppendLine("  avatar=" + (av ? av.name + " valid=" + av.isValid + " human=" + av.isHuman : "none"));
            }
            Write("hier.txt", sb.ToString());
        }
    }
}
