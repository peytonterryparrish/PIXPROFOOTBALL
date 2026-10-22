using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>Runs ClipCleaner over the library (and the BoNES_Yan folder). Output: Animations/Clean/*.anim + Logs/anim_audit/clean_report.csv.</summary>
    public static class CleanBatch
    {
        static readonly Regex MirrorNames = new Regex(@"(stance|avoid|stiff|transition|trans_|to_sprint|to_shuffle|_90|_180|to_stand|dodge|juke|turn|bgs_cb_|olm_step|olm_shuffle|cir_loop|handoff)", RegexOptions.IgnoreCase);

        [MenuItem("PIX Football/Animation/Clean All Clips")]
        public static void RunAll() { Run(null); }

        public static void RunList()
        {
            BatchWatchdog.Arm(60);
            var names = new HashSet<string>(File.ReadAllLines("Logs/clean_in.txt").Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            Run(names);
        }

        static void Run(HashSet<string> only)
        {
            BatchWatchdog.Arm(60);
            var lib = ArtLibrary.Load();
            if (lib == null) { Debug.LogError("[CleanBatch] no library"); return; }
            var clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            var keys = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            // v0.8: ALWAYS clean from the raw FBX takes (the library may already point at Clean/ clips); the library only supplies the semantic keys for planning
            var libKeys = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var e in lib.clips)
            {
                if (e == null || e.clip == null) continue;
                List<string> l; if (!libKeys.TryGetValue(e.clip.name, out l)) libKeys[e.clip.name] = l = new List<string>();
                if (!l.Contains(e.key)) l.Add(e.key);
            }
            foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Licensed/DraftFever/Animations" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (path.Contains("/Clean/") || path.EndsWith("Motus_Football_Player_A_01.fbx")) continue;
                foreach (var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (c.name.StartsWith("__preview") || clips.ContainsKey(c.name)) continue;
                    clips[c.name] = c;
                    List<string> kl; keys[c.name] = libKeys.TryGetValue(c.name, out kl) ? kl : new List<string> { "(unreg)" };
                }
            }

            AllNames = new HashSet<string>(clips.Keys);
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            var sb = new StringBuilder("name,out,keys,lenIn,lenOut,t0,t1,spikes,clamped,heading,spread,groundFix,note\n");
            int n = 0, ok = 0, fail = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var kv in clips.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    if (only != null && !only.Contains(kv.Key)) continue;
                    n++;
                    try
                    {
                        var plan = PlanFor(kv.Key, kv.Value, keys[kv.Key]);
                        if (plan.KeepRootFrame && kv.Value.length > 20f) continue;       // raw 3-minute pair takes (celebrations, conversations): no gameplay use, not worth the disk
                        foreach (var r in ClipCleaner.CleanClip(rig, kv.Value, plan))
                        {
                            sb.AppendLine(string.Join(",", new[] { Q(kv.Key), Q(Path.GetFileName(r.path)), Q(string.Join("+", keys[kv.Key])), F(r.lenIn), F(r.lenOut), F(r.t0), F(r.t1), r.spikes.ToString(), r.clamped.ToString(), F(r.heading), F(r.headingSpread), F(r.groundFix), Q(r.note) }));
                            ok++;
                        }
                    }
                    catch (Exception e) { fail++; Debug.LogWarning("[CleanBatch] " + kv.Key + ": " + e.Message); }
                    if (n % 100 == 0) Debug.Log("[CleanBatch] " + n + "/" + clips.Count);
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            rig.Dispose();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/anim_audit");
            File.WriteAllText("Logs/anim_audit/clean_report.csv", sb.ToString());
            Debug.Log("[CleanBatch] clips=" + n + " written=" + ok + " failed=" + fail);
        }

        public static HashSet<string> AllNames;
        public static CleanPlan PlanFor(string name, AnimationClip clip, List<string> keys)
        {
            bool loopLike = AnimationImportSettings.IsLoopName(name) || clip.isLooping;
            var p = ClipCleaner.PlanFor(name, clip.length, loopLike);
            string n = name.ToLowerInvariant();
            // QA-924: a clip is a two-actor take only when its twin exists (name_A + name_B), not by its suffix alone
            bool twin = false;
            if (name.EndsWith("_A") || name.EndsWith("_B") || name.EndsWith("_a") || name.EndsWith("_b"))
            {
                string sw = name.Substring(0, name.Length - 1) + (name.EndsWith("A") ? "B" : name.EndsWith("B") ? "A" : name.EndsWith("a") ? "b" : "a");
                twin = AllNames != null && AllNames.Contains(sw);
            }
            if (p.KeepRootFrame && !twin && !name.StartsWith("Tackle") && !name.StartsWith("Sack") && !name.StartsWith("BigHit") && !name.StartsWith("Collision")) { p.KeepRootFrame = false; p.AutoTrim = !p.Loop; p.FaceForward = true; p.Note = ""; }
            bool paired = p.KeepRootFrame;
            // very long raw takes (several repetitions + lead-in / lead-out): cut the action out as segments
            if (!paired && clip.length > 14f && !(p.InPlace && loopLike) && !n.Contains("loop") && !n.Contains("cycle"))
            {
                p.Segment = true; p.Loop = false; p.FindLoop = false;
                if (n.Contains("qb_") || n.Contains("bones_qb")) p.InPlace = false;
            }
            if (!paired && MirrorNames.IsMatch(n) && !n.Contains("_ab")) p.AlsoMirror = true;
            if (n.Contains("stance")) { p.GroundFix = true; }
            return p;
        }

        static string Q(string s) { return "\"" + (s ?? "").Replace("\"", "'") + "\""; }
        static string F(float v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }
    }
}
