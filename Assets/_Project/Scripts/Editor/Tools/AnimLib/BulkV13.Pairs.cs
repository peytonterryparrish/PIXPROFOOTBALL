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
    /// <summary>
    /// v0.13 wave 2 (BULK-2): the two-actor studio takes (BoNES day 2 / day 3 re-baked by Tools/Blender/retarget_actions.py --split).
    ///   ImportPairs -pixFolder Assets/_Licensed/DraftFever/Animations/BoNES_Day23B -pixList names.txt
    ///     Humanoid import of every file of the list (single take = file name). Two-actor takes (name_A with name_B next to it, not a quarterback take) are imported with the authored layout
    ///     (Root Transform Position XZ = Original), so both actors share one frame; they are then cut into the SAME windows around their interaction (<see cref="PairWindows"/>: the contact
    ///     moment of a hit / strip / contested catch, the first engagement of a block / rush / coverage take; at most 8 s, 4.1 s for the engagement families the engine plays through
    ///     AnimKit.EngagedPair) and written as name_s01_A / name_s01_B ... (the old whole-take clips of the same names are removed from Clean/). Everything else (single actor, quarterback takes)
    ///     goes through the normal plan (segments of long takes, mirrors by name).
    /// </summary>
    public static partial class BulkV13
    {
        static readonly Regex QbName = new Regex(@"(^|_)qb_|bones_qb", RegexOptions.IgnoreCase);
        // families whose pair is booked at a contact moment (the distance between the hips closes to a minimum) versus engagements that last
        static readonly Regex ContactFamily = new Regex(@"bighit|striptackle|contestedcatch|endzone_save|stiff_arm|tackle|sack|truck|collision|draft_night", RegexOptions.IgnoreCase);

        static bool IsPairName(string n, ICollection<string> all)
        {
            if (QbName.IsMatch(n)) return false;
            if (n.EndsWith("_A")) return all.Contains(n.Substring(0, n.Length - 1) + "B");
            if (n.EndsWith("_B")) return all.Contains(n.Substring(0, n.Length - 1) + "A");
            return false;
        }

        /// <summary>Windows (seconds) of a pair take: both actors sampled in the shared frame at 10 Hz; contact families get [contact - 1.0 s, contact + tail], engagement families [first close - 0.3 s, + 2.8 s].</summary>
        public static List<Vector2> PairWindows(AnimRig rig, AnimationClip a, AnimationClip b, string name, out string note)
        {
            note = "";
            float len = Mathf.Min(Mathf.Min(a.length, b.length), 90f);
            float te1, tb1, te2, tb2;
            ClipCleaner.TposeEdges(rig, a, out te1, out tb1); ClipCleaner.TposeEdges(rig, b, out te2, out tb2);
            float tStart = Mathf.Max(te1, te2) > 0.05f ? Mathf.Max(te1, te2) + 0.08f : 0f;
            float tEnd = Mathf.Min(len, Mathf.Min(tb1, tb2) < len ? Mathf.Min(tb1, tb2) - 0.05f : len);
            var fa = rig.Sample(a, 0f, len, 10f); var fb = rig.Sample(b, 0f, len, 10f);
            int n = Mathf.Min(fa.Count, fb.Count);
            var d = new float[n];
            for (int i = 0; i < n; i++) { Vector3 q = fa[i].P[AnimRig.Hips] - fb[i].P[AnimRig.Hips]; q.y = 0f; d[i] = q.magnitude; }
            var sm = new float[n];
            for (int i = 0; i < n; i++) { float s = 0f; int c = 0; for (int k = -1; k <= 1; k++) { int j = i + k; if (j < 0 || j >= n) continue; s += d[j]; c++; } sm[i] = s / c; }
            var wins = new List<Vector2>();
            bool contact = ContactFamily.IsMatch(name);
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (contact)
            {
                bool catches0 = Regex.IsMatch(name, "contestedcatch|endzone_save", RegexOptions.IgnoreCase);
                // local minima below 1.25 m, at least 4 s apart (the lower one wins)
                var mins = new List<int>();
                for (int i = 1; i < n - 1; i++)
                {
                    float t = i / 10f; if (t < tStart || t > tEnd) continue;
                    if (sm[i] < (catches0 ? 2.0f : 1.25f) && sm[i] <= sm[i - 1] && sm[i] < sm[i + 1])
                    {
                        if (mins.Count > 0 && (i - mins[mins.Count - 1]) < 40) { if (sm[i] < sm[mins[mins.Count - 1]]) mins[mins.Count - 1] = i; }
                        else mins.Add(i);
                    }
                }
                bool catches = Regex.IsMatch(name, "contestedcatch|endzone_save", RegexOptions.IgnoreCase);
                float tail = catches ? 3.6f : (Regex.IsMatch(name, "stiff_arm", RegexOptions.IgnoreCase) ? 4.0f : 6.6f);
                for (int m = 0; m < mins.Count && wins.Count < 3; m++)
                {
                    float tc = mins[m] / 10f;
                    float t0 = Mathf.Max(tStart, tc - 1.0f), t1 = Mathf.Min(tEnd, tc + tail);
                    if (m + 1 < mins.Count) t1 = Mathf.Min(t1, mins[m + 1] / 10f - 1.2f);
                    if (t1 - t0 < 2.2f) continue;
                    wins.Add(new Vector2(t0, t1));
                    note += string.Format(ci, " c{0:0.0}({1:0.00}m)", tc, sm[mins[m]]);
                }
            }
            else
            {
                float thr = 1.7f; int lastFar = -100;
                for (int i = 0; i < n && wins.Count < 3; i++)
                {
                    float t = i / 10f; if (t < tStart || t > tEnd) continue;
                    if (sm[i] >= thr) { lastFar = i; continue; }
                    if (i - lastFar >= 8)
                    {
                        float t0 = Mathf.Max(tStart, t - 0.3f), t1 = Mathf.Min(tEnd, t0 + 3.1f);
                        if (t1 - t0 >= 2.0f) { wins.Add(new Vector2(t0, t1)); note += string.Format(ci, " e{0:0.0}", t); }
                        lastFar = i + 40;      // the next engagement is at least 4 s later
                    }
                }
            }
            if (wins.Count == 0) note += " no-window(minD=" + (n > 0 ? sm.Min().ToString("0.00") : "-") + ")";
            return wins;
        }

        /// <summary>Humanoid import + clean of the takes of a list (see the class comment). Writes Logs/animlib/pairs_clean.csv.</summary>
        public static void ImportPairs()
        {
            BatchWatchdog.Arm(120);
            string folder = Arg("-pixFolder", "Assets/_Licensed/DraftFever/Animations/BoNES_Day23B");
            var names = Names(Arg("-pixList", ""));
            AssetDatabase.Refresh();
            var models = AssetDatabase.FindAssets("t:Model", folder.Split(';')).Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => names.Count == 0 || names.Contains(Path.GetFileNameWithoutExtension(p))).OrderBy(p => p, StringComparer.Ordinal).ToList();
            var fileNames = new HashSet<string>(models.Select(Path.GetFileNameWithoutExtension), StringComparer.Ordinal);
            var allNames = new HashSet<string>(fileNames, StringComparer.Ordinal);
            int changed = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string path in models)
                {
                    var mi = AssetImporter.GetAtPath(path) as ModelImporter; if (mi == null) continue;
                    AnimationImportSettings.ConfigureHumanoid(mi);
                    var dd = mi.defaultClipAnimations; string nm1 = Path.GetFileNameWithoutExtension(path);
                    var l1 = new List<ModelImporterClipAnimation>();
                    for (int i = 0; i < dd.Length; i++) { var c1 = dd[i]; c1.name = dd.Length == 1 ? nm1 : nm1 + "_" + i; l1.Add(c1); }
                    mi.clipAnimations = l1.ToArray(); mi.SaveAndReimport();
                    AnimationImportSettings.ConfigureClips(mi);
                    if (IsPairName(nm1, allNames))
                    {
                        var cl = mi.clipAnimations;
                        for (int i = 0; i < cl.Length; i++) { cl[i].keepOriginalPositionXZ = true; cl[i].lockRootPositionXZ = false; }
                        mi.clipAnimations = cl;
                    }
                    mi.SaveAndReimport(); changed++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
            Debug.Log("[BulkV13] pair importers set: " + changed);

            var clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (string path in models)
                foreach (var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                    if (!c.name.StartsWith("__preview")) clips[c.name] = c;
            CleanBatch.AllNames = new HashSet<string>(allNames);
            var rig = AnimRig.CreateMedium(); if (rig == null) { AnimLibBatch.Done(); return; }
            var log = new StringBuilder("name,out,lenIn,lenOut,t0,t1,spikes,clamped,note\n");
            int okN = 0, fail = 0, noWin = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var kv in clips.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    string name = kv.Key; var clip = kv.Value;
                    try
                    {
                        var plan = CleanBatch.PlanFor(name, clip, new List<string> { "(unreg)" });
                        bool pair = IsPairName(name, clips.Keys.ToList());
                        if (QbName.IsMatch(name)) { plan.KeepRootFrame = false; plan.AutoTrim = false; plan.FaceForward = true; plan.InPlace = false; plan.Note = ""; plan.Loop = false; plan.FindLoop = false; plan.Segment = clip.length > 14f; }
                        else if (pair)
                        {
                            if (name.EndsWith("_B")) continue;      // written together with its A
                            string bn = name.Substring(0, name.Length - 1) + "B";
                            AnimationClip cb; if (!clips.TryGetValue(bn, out cb)) continue;
                            string note = "";
                            var wins = clip.length > 9f || cb.length > 9f ? PairWindows(rig, clip, cb, name, out note) : null;
                            if (wins != null)
                            {
                                if (wins.Count == 0) { noWin++; log.AppendLine(Q2(name) + ",,,,,,,," + Q2("no window:" + note)); continue; }
                                plan.Windows = wins; plan.Segment = true; plan.AutoTrim = false; plan.Loop = false; plan.FindLoop = false; plan.AlsoMirror = false;
                                // the whole-take clips of the same names are replaced by the segments
                                string stem = name.Substring(0, name.Length - 2);
                                foreach (string old in new[] { name, bn }) AssetDatabase.DeleteAsset(ClipCleaner.PathOf(old));
                                foreach (string g in AssetDatabase.FindAssets(stem + "_s t:AnimationClip", new[] { ClipCleaner.CleanDir }))
                                { string pp = AssetDatabase.GUIDToAssetPath(g); if (Regex.IsMatch(Path.GetFileNameWithoutExtension(pp), "^" + Regex.Escape(stem) + @"_s\d\d(_M)?_[AB]$")) AssetDatabase.DeleteAsset(pp); }
                            }
                            foreach (var c2 in new[] { clip, cb })
                                foreach (var r in ClipCleaner.CleanClip(rig, c2, plan))
                                { log.AppendLine(string.Join(",", new[] { Q2(c2.name), Q2(Path.GetFileName(r.path)), r.lenIn.ToString("0.##"), r.lenOut.ToString("0.##"), r.t0.ToString("0.##"), r.t1.ToString("0.##"), r.spikes.ToString(), r.clamped.ToString(), Q2((r.note ?? "") + note) })); okN++; }
                            continue;
                        }
                        foreach (var r in ClipCleaner.CleanClip(rig, clip, plan))
                        { log.AppendLine(string.Join(",", new[] { Q2(name), Q2(Path.GetFileName(r.path)), r.lenIn.ToString("0.##"), r.lenOut.ToString("0.##"), r.t0.ToString("0.##"), r.t1.ToString("0.##"), r.spikes.ToString(), r.clamped.ToString(), Q2(r.note) })); okN++; }
                    }
                    catch (Exception ex) { fail++; Debug.LogWarning("[BulkV13] pairs clean " + name + ": " + ex.Message); }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            rig.Dispose();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/animlib");
            File.WriteAllText("Logs/animlib/pairs_clean.csv", log.ToString());
            Debug.Log("[BulkV13] pairs cleaned: written " + okN + ", failed " + fail + ", no window " + noWin);
            AnimLibBatch.Done();
        }

        static string Q2(string s) { return "\"" + (s ?? "").Replace("\"", "'") + "\""; }
    }
}
