using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.13 wave 2 (BULK-2): the engagement pools (AnimKit.EngagedPair / HandsUp / PocketStand play the first 3 s of these takes on a body that stands, blocks or rushes) measured clip by clip, in the
    /// MatchWatchAudit's terms: hips under 0.35 m (SUNK_HIPS), a hand or the head more than 8 cm under the turf (HAND_OR_HEAD_BELOW_TURF), both toes more than 9 cm over the turf (FLOAT_STANDING).
    /// The wave-1 game doubled the first and raised the others because the wider pools + the no-repeat bag drew the takes that do this (every sample of those issues was an Engaged OL / DL body).
    /// Writes Docs/QA/v0130/bulk2_engage_rejects.txt (clip, reason; ArtLibraryBuilder.LoadRejects reads it, so the take is not registered) and Logs/animlib/engage_probe.csv.
    /// Run: -executeMethod PixFootball.EditorTools.EngageProbe.Run (after AnimLibBatch.Rebuild, before the final ArtLibrary build; rejects only look at the 3 s window that is played).
    /// </summary>
    public static class EngageProbe
    {
        public const string RejectFile = "Docs/QA/v0130/bulk2_engage_rejects.txt";
        public const float Window = 3.0f, RunBlockWindow = 1.4f;
        static readonly string[] Keys =
        {
            "ol_pass_set", "ol_block_engage", "ol_block_push", "ol_block_fail", "ol_block_cut", "ol_pulling",
            "dl_bull_rush", "dl_rush_swim", "dl_rush_spin", "dl_shed", "dl_wrong_arm", "ol_run_block"
        };

        public struct Res { public float minHips, minHand, minHead, floatRun, sunkRun; public string reason; }

        public static Res Measure(AnimRig rig, AnimationClip c, bool floatCheck = true)
        {
            var r = new Res { minHips = 9f, minHand = 9f, minHead = 9f, reason = "" };
            float t1 = Mathf.Min(c.length, c.name.IndexOf("oline_man_3", StringComparison.OrdinalIgnoreCase) >= 0 ? RunBlockWindow : Window);      // the run-block footwork plays its first 1.4 s
            if (t1 < 0.3f) return r;
            var fr = rig.Sample(c, 0f, t1, 30f);
            int fl = 0, sk = 0, bestFl = 0, bestSk = 0, hand = 0, bestHand = 0;
            foreach (var f in fr)
            {
                float hips = f.P[AnimRig.Hips].y;
                float toe = Mathf.Min(f.P[AnimRig.LT].y, f.P[AnimRig.RT].y);
                float hh = Mathf.Min(f.P[AnimRig.LH].y, f.P[AnimRig.RH].y), hd = f.P[AnimRig.Head].y;
                r.minHips = Mathf.Min(r.minHips, hips); r.minHand = Mathf.Min(r.minHand, hh); r.minHead = Mathf.Min(r.minHead, hd);
                fl = toe > 0.09f ? fl + 1 : 0; bestFl = Mathf.Max(bestFl, fl);
                sk = hips < 0.35f ? sk + 1 : 0; bestSk = Mathf.Max(bestSk, sk);
                hand = (hh < -0.08f || hd < -0.08f) ? hand + 1 : 0; bestHand = Mathf.Max(bestHand, hand);
            }
            r.floatRun = bestFl / 30f; r.sunkRun = bestSk / 30f;
            if (bestHand >= 2) r.reason += "hand_or_head_below_turf ";
            if (bestSk >= 5) r.reason += "sunk_hips ";
            if (floatCheck && bestFl >= 15) r.reason += "both_toes_up ";
            return r;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(60);
            // measure the whole pools: forget the previous run's rejects and register everything first
            if (File.Exists(RejectFile)) File.Delete(RejectFile);
            AssetDatabase.Refresh();
            ArtLibraryBuilder.Build();
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            var rig = AnimRig.CreateMedium();
            if (lib == null || rig == null) { Debug.LogError("[EngageProbe] no library / rig"); AnimLibBatch.Done(); return; }
            var rows = new StringBuilder("key,clip,length,minHips,minHand,minHead,floatS,sunkS,reason\n");
            var bad = new Dictionary<string, string>(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int n = 0;
            foreach (string key in Keys)
                foreach (string k in new[] { key, key + "_b" })
                    foreach (var c in lib.ClipVariants(k))
                    {
                        if (c == null || !seen.Add(c.name)) continue;
                        n++;
                        var r = Measure(rig, c, key != "ol_run_block");      // the run-block footwork steps: no FLOAT_STANDING sample in the game came from it
                        rows.AppendLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0},{1},{2:0.0},{3:0.00},{4:0.00},{5:0.00},{6:0.00},{7:0.00},{8}", k, c.name, c.length, r.minHips, r.minHand, r.minHead, r.floatRun, r.sunkRun, r.reason.Trim()));
                        if (r.reason.Length > 0) bad[c.name] = r.reason.Trim();
                    }
            rig.Dispose();
            // a pair stays aligned: the twin of a rejected take goes with it
            foreach (var name in bad.Keys.ToList())
            {
                string tw = null;
                if (name.EndsWith("_A")) tw = name.Substring(0, name.Length - 1) + "B"; else if (name.EndsWith("_B")) tw = name.Substring(0, name.Length - 1) + "A";
                if (tw != null && !bad.ContainsKey(tw) && seen.Contains(tw)) bad[tw] = "twin of " + name;
            }
            Directory.CreateDirectory("Logs/animlib"); Directory.CreateDirectory(Path.GetDirectoryName(RejectFile));
            File.WriteAllText("Logs/animlib/engage_probe.csv", rows.ToString());
            File.WriteAllLines(RejectFile, bad.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "\t" + k.Value).ToArray());
            Debug.Log("[EngageProbe] " + n + " engagement takes measured, " + bad.Count + " rejected -> " + RejectFile);
            // the library again, without the rejected takes (the other tables of the rebuild do not depend on them)
            AssetDatabase.Refresh();
            ArtLibraryBuilder.Build();
            try { RuntimeAssetsBuilder.Build(); } catch (Exception ex) { Debug.LogError("[EngageProbe] RuntimeAssetsBuilder failed: " + ex); }
            AnimLibBatch.Done();
        }
    }
}
