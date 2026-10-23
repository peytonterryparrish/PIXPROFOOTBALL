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
    /// v0.10.0 ANIMLIB batch entry points (all with a watchdog):
    ///   Baseline   - QA of every cleaned clip as the game has it now          -> Logs/animlib/qa_before.csv
    ///   PolishAll  - the QA-driven passes over Clean/*.anim, written in place  -> Logs/animlib/qa_after.csv (+ polish_log.txt, rejects list)
    ///   ImportNew  - Humanoid import settings for the newly re-baked takes, then CleanBatch.RunList on Logs/clean_in.txt
    /// </summary>
    public static class AnimLibBatch
    {
        public const string OutDir = "Logs/animlib";
        public const string RejectFile = "Docs/QA/v0100/animlib_rejects.txt";

        static Dictionary<string, string> LibKeys()
        {
            var d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var lib = ArtLibrary.Load();
            if (lib != null)
                foreach (var e in lib.clips)
                {
                    if (e == null || e.clip == null) continue;
                    List<string> l; if (!d.TryGetValue(e.clip.name, out l)) d[e.clip.name] = l = new List<string>();
                    if (!l.Contains(e.key)) l.Add(e.key);
                }
            return d.ToDictionary(k => k.Key, k => string.Join("+", k.Value), StringComparer.Ordinal);
        }

        static List<string> SkipPrefixes()
        {
            // Logs/animlib/skip.txt: base names of clips that were already cleaned with the polish passes (the new takes): never polished twice
            if (!File.Exists("Logs/animlib/skip.txt")) return new List<string>();
            return File.ReadAllLines("Logs/animlib/skip.txt").Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
        }

        static IEnumerable<AnimationClip> CleanClips(HashSet<string> only = null)
        {
            var skip = SkipPrefixes();
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipCleaner.CleanDir }).OrderBy(x => AssetDatabase.GUIDToAssetPath(x), StringComparer.Ordinal))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.Contains("/_rejected/")) continue;
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
                if (c == null) continue;
                if (only != null && !only.Contains(c.name)) continue;
                if (skip.Count > 0 && skip.Any(sp => c.name == sp || c.name.StartsWith(sp + "_s") || c.name.StartsWith(sp + "_M") || c.name.StartsWith(sp + "_A") || c.name.StartsWith(sp + "_B"))) continue;
                yield return c;
            }
        }

        static HashSet<string> OnlyList()
        {
            if (!File.Exists("Logs/animlib/only.txt")) return null;
            return new HashSet<string>(File.ReadAllLines("Logs/animlib/only.txt").Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
        }

        public static void Done() { if (Application.isBatchMode) EditorApplication.Exit(0); }

        public static void Baseline() { BatchWatchdog.Arm(90); RunQa("qa_before.csv"); Done(); }
        /// <summary>Baseline QA of the current Clean/ library, then the import + cleaning of the new takes (one editor session).</summary>
        public static void BaselineThenImport()
        {
            BatchWatchdog.Arm(150);
            RunQa("qa_before.csv");
            AssetDatabase.Refresh();
            Debug.Log("[AnimLibBatch] import settings = " + AnimationImportSettings.ApplyFolder("Assets/_Licensed/DraftFever/Animations/BoNES_Yan"));
            AssetDatabase.Refresh();
            AnimationImportSettings.ReportAvatars();
            CleanBatch.RunList();
            AssetDatabase.Refresh();
        }

        /// <summary>QA of the untouched pre-polish library (a copy of Clean/ at Assets/AnimBackup/Clean in the private project) with the final metrics: the "before" numbers.</summary>
        public static void QaBackup()
        {
            BatchWatchdog.Arm(90);
            AssetDatabase.Refresh();
            Directory.CreateDirectory(OutDir);
            var keys = LibKeys();
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            var sb = new StringBuilder(QaRow.Header() + "\n");
            int n = 0;
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/AnimBackup/Clean" }).OrderBy(x => AssetDatabase.GUIDToAssetPath(x), StringComparer.Ordinal))
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g)); if (c == null) continue;
                try { string k; keys.TryGetValue(c.name, out k); var r = ClipQa.Analyse(rig, c, k ?? ""); sb.AppendLine(r.Csv()); }
                catch (Exception ex) { Debug.LogWarning("[AnimLibBatch] backup qa " + c.name + ": " + ex.Message); }
                if (++n % 200 == 0) { Debug.Log("[AnimLibBatch] backup qa " + n); File.WriteAllText(OutDir + "/qa_before_v2.csv", sb.ToString()); }
            }
            rig.Dispose();
            File.WriteAllText(OutDir + "/qa_before_v2.csv", sb.ToString());
            Debug.Log("[AnimLibBatch] backup qa rows " + n);
        }

        /// <summary>One editor session: the before numbers, the polish passes over every cleaned clip, then the library rebuild + tags.</summary>
        public static void BackupPolishRebuild()
        {
            BatchWatchdog.Arm(240);
            QaBackup();
            PolishCore();
            Rebuild();
        }

        /// <summary>Contact sheets: Logs/animlib/sheet_new.txt (one clip name per line) as a grid, Logs/animlib/sheet_cmp.txt (name or path:Assets/... lines, 6 poses each) as filmstrips. Output D:/PIXFootball/Renders/animlib_v0100/.</summary>
        public static void Sheets()
        {
            BatchWatchdog.Arm(40);
            string dir = Directory.Exists("D:/") ? "D:/PIXFootball/Renders/animlib_v0100" : "Logs/animlib_sheets";
            Directory.CreateDirectory(dir);
            if (File.Exists("Logs/animlib/sheet_new.txt"))
                StickSheet.RenderGrid(File.ReadAllLines("Logs/animlib/sheet_new.txt").Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList(), dir + "/contact_sheet_24_new_clips.png", 3, 4, 6f);
            if (File.Exists("Logs/animlib/sheet_cmp.txt"))
            {
                var items = File.ReadAllLines("Logs/animlib/sheet_cmp.txt").Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
                StickSheet.RenderList(items, "animlib_cmp", 6, 6);
                // RenderList writes to its own folder: copy the result next to the contact sheet
                string src = "D:/PIXFootball/Renders/animation";
                if (Directory.Exists(src)) foreach (string f in Directory.GetFiles(src, "sheet_animlib_cmp_*.png")) File.Copy(f, dir + "/" + Path.GetFileName(f), true);
            }
            Done();
        }

        /// <summary>Paired takes (the two actors share one timeline): an arms-out lead-in is cut from BOTH by the same amount (the longer one), so the pair stays in sync.</summary>
        static void PairLeadIn()
        {
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            var clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal); foreach (var c0 in CleanClips()) if (!clips.ContainsKey(c0.name)) clips[c0.name] = c0;
            var log = new StringBuilder(); int n = 0;
            foreach (var kv in clips.ToList())
            {
                string a = kv.Key; if (!a.EndsWith("_A")) continue;
                string b = a.Substring(0, a.Length - 1) + "B"; AnimationClip cb;
                if (!clips.TryGetValue(b, out cb) || ClipQa.ArmsOutIsFine(a)) continue;
                var ca = kv.Value;
                if (ca.length > 24f || cb.length > 24f) continue;
                var qa = ClipQa.Analyse(rig, ca); var qb = ClipQa.Analyse(rig, cb);
                float lead = Mathf.Max(qa.armsLead, qb.armsLead);
                if (lead < 0.4f) continue;
                float t0 = lead + 0.07f;
                foreach (var c in new[] { ca, cb })
                {
                    var s = PoseSeq.FromClip(c, 0f, c.length, 30f); s.Name = c.name; s.Loop = c.isLooping; s.InPlace = false;
                    if (c.length - t0 < 0.8f) continue;
                    var tr = s.Trim(t0, s.Duration); tr.Name = c.name; tr.Loop = s.Loop;
                    ClipCleaner.WriteSeq(tr);
                }
                log.AppendLine(a + " lead " + lead.ToString("0.00") + " s cut from both"); n++;
            }
            rig.Dispose();
            AssetDatabase.SaveAssets();
            File.WriteAllText(OutDir + "/pair_leadin.txt", log.ToString());
            Debug.Log("[AnimLibBatch] pair lead-in trims: " + n);
        }

        /// <summary>The arms-out fix: pair lead-ins, then the polish over the clips of Logs/animlib/only.txt, then the library rebuild.</summary>
        public static void ArmsFix()
        {
            BatchWatchdog.Arm(120);
            AssetDatabase.Refresh();
            PairLeadIn();
            PolishCore();
            Rebuild();
        }

        /// <summary>Before numbers with the final metrics, then the library rebuild with the final reject list (no polish).</summary>
        public static void FinalPass()
        {
            BatchWatchdog.Arm(120);
            QaBackup();
            Rebuild();
        }

        /// <summary>The polish over Logs/animlib/only.txt (pops / twist clips, sideline ambient loops), then the library rebuild with the live-key drop.</summary>
        public static void PolishRebuild()
        {
            BatchWatchdog.Arm(120);
            AssetDatabase.Refresh();
            PolishCore();
            Rebuild();
        }

        /// <summary>Rewrites the authored hands of every cleaned clip (pose table re-tuned), then the rebuild.</summary>
        public static void HandsRetune()
        {
            BatchWatchdog.Arm(120);
            AssetDatabase.Refresh();
            Directory.CreateDirectory(OutDir);
            File.WriteAllText("Logs/animlib/hands_only.txt", "1");
            try { PolishCore(); } finally { File.Delete("Logs/animlib/hands_only.txt"); }
            Rebuild();
        }

        /// <summary>Clips still flagged on live keys (names in Logs/animlib/livefix.txt; clips of the library that are not in Clean/, like the referee gestures, are copied into Clean/ under their own name, which replaces the original in the library): the forced limits, then the hands retune and the rebuild.</summary>
        public static void LiveFix()
        {
            BatchWatchdog.Arm(120);
            AssetDatabase.Refresh();
            Directory.CreateDirectory(OutDir);
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            var log = new StringBuilder();
            foreach (string name in File.ReadAllLines("Logs/animlib/livefix.txt").Select(l => l.Trim()).Where(l => l.Length > 0))
            {
                var c = StickSheet.FindClip("clean:" + name) ?? StickSheet.FindClip(name);
                if (c == null || c.length > 24f) { log.AppendLine(name + ": not found"); continue; }
                var seq = PoseSeq.FromClip(c, 0f, c.length, 30f); seq.Name = c.name; seq.Loop = c.isLooping;
                bool inPlace = true; for (int i = 1; i < seq.N; i++) if (Mathf.Abs(seq.RT[i].x - seq.RT[0].x) > 1e-3f || Mathf.Abs(seq.RT[i].z - seq.RT[0].z) > 1e-3f) { inPlace = false; break; }
                seq.InPlace = inPlace;
                string note = ClipPolish.Force(rig, seq);
                ClipCleaner.WriteSeq(seq);
                log.AppendLine(name + ": " + note);
            }
            rig.Dispose();
            AssetDatabase.SaveAssets();
            File.WriteAllText(OutDir + "/livefix_log.txt", log.ToString());
            HandsRetune();      // hands pass over every clip (the new copies included), then the rebuild
        }

        public static void QaAfter() { BatchWatchdog.Arm(90); RunQa("qa_after_check.csv"); Done(); }

        static void RunQa(string file, bool writeRejects = false)
        {
            Directory.CreateDirectory(OutDir);
            var keys = LibKeys(); var only = OnlyList();
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            var sb = new StringBuilder(QaRow.Header() + "\n");
            var badLive = new HashSet<string>(StringComparer.Ordinal);
            var rej = new List<string>(); var rejScore = new Dictionary<string, float>(StringComparer.Ordinal); var allScore = new Dictionary<string, float>(StringComparer.Ordinal);
            int n = 0;
            foreach (var c in CleanClips(only))
            {
                try
                {
                    string k; keys.TryGetValue(c.name, out k);
                    var r = ClipQa.Analyse(rig, c, k ?? "");
                    r.source = AssetDatabase.GetAssetPath(c);
                    sb.AppendLine(r.Csv());
                    allScore[c.name] = r.score; if (r.popFrames >= 2f || r.waistTwist > 85f || r.torsoTwist > 80f) badLive.Add(c.name);
                    if (r.reject) { rej.Add(c.name + "	" + r.score.ToString("0") + "	" + r.faults); rejScore[c.name] = r.score; }
                }
                catch (Exception ex) { Debug.LogWarning("[AnimLibBatch] qa " + c.name + ": " + ex.Message); }
                if (++n % 100 == 0) { Debug.Log("[AnimLibBatch] qa " + n); File.WriteAllText(OutDir + "/" + file, sb.ToString()); }
            }
            rig.Dispose();
            File.WriteAllText(OutDir + "/" + file, sb.ToString());
            if (writeRejects)
            {
                // a key must never lose its last variant: when every clip registered under a key is rejected, the best-scoring one stays (demoted, not removed)
                var byKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var kv in keys) foreach (string k in kv.Value.Split('+')) { List<string> l; if (!byKey.TryGetValue(k, out l)) byKey[k] = l = new List<string>(); l.Add(kv.Key); }
                var keep = new HashSet<string>(StringComparer.Ordinal);
                foreach (var kv in byKey)
                {
                    var vars = kv.Value.Where(v => allScore.ContainsKey(v)).ToList();
                    if (vars.Count == 0 || vars.Any(v => !rejScore.ContainsKey(v))) continue;
                    keep.Add(vars.OrderByDescending(v => allScore[v]).First());
                }
                // live keys (Logs/animlib/live_keys.txt): a clip with 2+ pops / a hips-to-shoulder twist over 85 / a torso twist over 80 that the passes could not fix leaves the key when another variant of it is clean
                if (File.Exists("Logs/animlib/live_keys.txt"))
                {
                    var live = new HashSet<string>(File.ReadAllLines("Logs/animlib/live_keys.txt").Select(l => l.Trim()).Where(l => l.Length > 0), StringComparer.Ordinal);
                    foreach (var kv in byKey)
                    {
                        if (!live.Contains(kv.Key)) continue;
                        var vars = kv.Value.Where(v => allScore.ContainsKey(v)).ToList();
                        Func<string, bool> bad = v => badLive.Contains(v);
                        if (vars.Count == 0 || vars.All(bad)) continue;
                        foreach (string v in vars.Where(bad)) if (!rejScore.ContainsKey(v)) { rejScore[v] = allScore[v]; rej.Add(v + "	live-key" + "	" + "pops/twist, another variant is clean"); }
                    }
                }
                if (keep.Count > 0) { rej = rej.Where(l => !keep.Contains(l.Split('	')[0])).ToList(); Debug.Log("[AnimLibBatch] kept " + keep.Count + " rejected clips that are the last variant of their key"); }
            }
            if (writeRejects) { Directory.CreateDirectory(Path.GetDirectoryName(RejectFile)); File.WriteAllText(RejectFile, "# clips rejected by the ANIMLIB QA (score < 50, T-pose or 8+ bone pops after cleaning): not registered in the ArtLibrary\n" + string.Join("\n", rej) + "\n"); }
            Debug.Log("[AnimLibBatch] qa rows " + n + " -> " + OutDir + "/" + file);
        }

        /// <summary>Runs the polish passes over the cleaned clips and writes the ones that improved (or stayed equal but gained hands / limits).</summary>
        public static void PolishAll() { PolishCore(); Done(); }

        static void PolishCore()
        {
            BatchWatchdog.Arm(150);
            Directory.CreateDirectory(OutDir);
            var keys = LibKeys(); var only = OnlyList();
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            var sb = new StringBuilder("name,scoreBefore,scoreAfter,slideBefore,slideAfter,notes\n");
            var qa = new StringBuilder(QaRow.Header() + "\n");
            var rejects = new List<string>();
            int n = 0, wrote = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var c in CleanClips(only).ToList())
                {
                    try
                    {
                        float dur = Mathf.Min(c.length, 24f);
                        if (c.length > 24f || dur < 0.2f) continue;
                        var seq = PoseSeq.FromClip(c, 0f, c.length, 30f);
                        seq.Name = c.name; seq.Loop = c.isLooping;
                        // a clip with a constant root XZ is an in-place clip: keep it so
                        bool inPlace = true; for (int i = 1; i < seq.N; i++) if (Mathf.Abs(seq.RT[i].x - seq.RT[0].x) > 1e-3f || Mathf.Abs(seq.RT[i].z - seq.RT[0].z) > 1e-3f) { inPlace = false; break; }
                        seq.InPlace = inPlace;
                        if (File.Exists("Logs/animlib/hands_only.txt"))
                        {
                            ClipPolish.HandsOnly(rig, seq); ClipCleaner.WriteSeq(seq); wrote++;
                            if (++n % 100 == 0) Debug.Log("[AnimLibBatch] hands " + n);
                            continue;
                        }
                        var res = ClipPolish.Run(rig, ref seq, inPlace || Paired(c.name));
                        string k; keys.TryGetValue(c.name, out k);
                        res.after.keys = k ?? ""; res.after.source = AssetDatabase.GetAssetPath(c);
                        qa.AppendLine(res.after.Csv());
                        sb.AppendLine(string.Join(",", new[] { "\"" + c.name + "\"", res.before.score.ToString("0.#"), res.after.score.ToString("0.#"), res.before.slidePct.ToString("0"), res.after.slidePct.ToString("0"), "\"" + res.notes.Trim() + "\"" }));
                        if (c.name.Contains("Ambient_coach") || c.name.Contains("Ambient_bench") || System.Text.RegularExpressions.Regex.IsMatch(c.name, @"kickoff_(att_idle_0\d(_closeup)?|def_\d\d_idle)(_M)?$")) { seq.CloseSeam(Mathf.RoundToInt(0.5f * seq.Rate)); seq.Loop = true; res.notes += " loop-closed"; wrote++; ClipCleaner.WriteSeq(seq); }
                        if (res.torso || res.wrist || res.hands || res.feet || res.notes.Contains("floor:") || res.notes.Contains("yaw:")) { ClipCleaner.WriteSeq(seq); wrote++; }
                        if (res.after.reject) rejects.Add(c.name + "\t" + res.after.score.ToString("0") + "\t" + res.after.faults);
                    }
                    catch (Exception ex) { Debug.LogWarning("[AnimLibBatch] polish " + c.name + ": " + ex.Message); }
                    if (++n % 50 == 0) { Debug.Log("[AnimLibBatch] polish " + n + " wrote " + wrote); File.WriteAllText(OutDir + "/polish_log.csv", sb.ToString()); File.WriteAllText(OutDir + "/qa_after.csv", qa.ToString()); }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            rig.Dispose();
            AssetDatabase.SaveAssets();
            File.WriteAllText(OutDir + "/polish_log.csv", sb.ToString());
            File.WriteAllText(OutDir + "/qa_after.csv", qa.ToString());
            File.WriteAllText(OutDir + "/rejects.txt", string.Join("\n", rejects));
            Debug.Log("[AnimLibBatch] polish clips=" + n + " written=" + wrote + " rejected=" + rejects.Count);
        }

        static bool Paired(string n) { return n.EndsWith("_A") || n.EndsWith("_B") || n.EndsWith("_CO") || n.StartsWith("Tackle") || n.StartsWith("Sack") || n.StartsWith("BigHit") || n.StartsWith("Collision"); }

        /// <summary>Humanoid import for the re-baked takes copied into BoNES_Yan, then the cleaner over Logs/clean_in.txt (one raw clip name per line).</summary>
        /// <summary>The library side after the clips changed: ArtLibrary (new keys, rejects), timing / move-take / facing / cycle-speed tables, then the clip tag table + final QA.</summary>
        public static void Rebuild()
        {
            BatchWatchdog.Arm(120);
            RunQa("qa_pre_rebuild.csv", true);      // the reject list the ArtLibrary reads
            AssetDatabase.Refresh();
            ArtLibraryBuilder.Build();
            try { ClipTimingBuilder.Run(); } catch (Exception ex) { Debug.LogError("[AnimLibBatch] ClipTimingBuilder failed: " + ex); }
            try { ContactClipBuilder.BuildMoveTakes(); } catch (Exception ex) { Debug.LogError("[AnimLibBatch] MoveTakes failed: " + ex); }
            try { RuntimeAssetsBuilder.Build(); } catch (Exception ex) { Debug.LogError("[AnimLibBatch] RuntimeAssetsBuilder failed: " + ex); }
            try { ClipFacingAudit.Run(); } catch (Exception ex) { Debug.LogError("[AnimLibBatch] ClipFacingAudit failed: " + ex); }
            CycleSpeeds.Run();
            AnimLibProbe.Inventory();
            AnimLibTags.Run();      // ends with Done()
        }

        public static void ImportAndPolish() { ImportCore(); PolishCore(); Done(); }

        public static void ImportNew() { ImportCore(); Done(); }

        static void ImportCore()
        {
            BatchWatchdog.Arm(90);
            AssetDatabase.Refresh();
            Debug.Log("[AnimLibBatch] import settings = " + AnimationImportSettings.ApplyFolder("Assets/_Licensed/DraftFever/Animations/BoNES_Yan"));
            AssetDatabase.Refresh();
            AnimationImportSettings.ReportAvatars();
            CleanBatch.RunList();
            AssetDatabase.Refresh();
        }
    }
}
