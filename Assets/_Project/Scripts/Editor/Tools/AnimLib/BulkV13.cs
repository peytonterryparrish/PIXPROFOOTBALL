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
    /// v0.13 (ANIMLIB-BULK) batch entry points for importing a folder of new humanoid takes into the clip library pipeline:
    ///   ImportFolder -pixFolder Assets/_Licensed/DraftFever/Animations/MC_Idles -pixList D:/.../names.txt
    ///     Humanoid import (avatar from the file, a single-take FBX is named after its file, loop flag by name: "loop" / the standing idles only), then CleanBatch.RunList over the names
    ///     (Logs/clean_in.txt is written from the list). Next steps of the recipe: AnimLibV11.Pipeline (-pixList), AnimLibBatch.Rebuild (QA + rejects + ArtLibrary + tables + tags).
    ///   QaNew -pixList names.txt -pixOut file.csv : ClipQa over the Clean/ clips of the list (and their _sNN / _M twins), one CSV row each.
    /// </summary>
    public static partial class BulkV13
    {
        static string Arg(string key, string def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
            return def;
        }

        static List<string> Names(string file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return new List<string>();
            return File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).Distinct().ToList();
        }

        /// <summary>Loop flag for the MoCap Central idles: the standing idles and the "Loop" takes loop, the one-shots (fidgets, brush-offs, stretches, body aches, claps) do not.</summary>
        public static bool IsLoopTake(string n)
        {
            string s = n.ToLowerInvariant();
            if (s.Contains("loop") || s.Contains("cheer")) return true;
            if (s.Contains("_idle_0") && (s.Contains("armscrossed") || s.Contains("handhip") || s.Contains("handsclasp") || s.Contains("handship"))) return true;
            if (s.Contains("waiting_0") || s.Contains("breathe_0")) return true;
            return false;
        }

        public static void ImportFolder()
        {
            BatchWatchdog.Arm(120);
            string folder = Arg("-pixFolder", "Assets/_Licensed/DraftFever/Animations/MC_Idles");
            var names = Names(Arg("-pixList", ""));
            bool takes = Arg("-pixMode", "ambient") == "takes";      // takes: travelling studio takes (name-driven loop / root flags, AnimationImportSettings.ConfigureClips); ambient: standing in-place idles
            AssetDatabase.Refresh();
            if (names.Count == 0) foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { folder })) names.Add(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g)));
            int changed = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(g);
                    var mi = AssetImporter.GetAtPath(path) as ModelImporter; if (mi == null) continue;
                    AnimationImportSettings.ConfigureHumanoid(mi);
                    if (takes)
                    {
                        var dd = mi.defaultClipAnimations; string nm1 = Path.GetFileNameWithoutExtension(path);
                        var l1 = new List<ModelImporterClipAnimation>();
                        for (int i = 0; i < dd.Length; i++) { var c1 = dd[i]; c1.name = dd.Length == 1 ? nm1 : nm1 + "_" + i; l1.Add(c1); }
                        mi.clipAnimations = l1.ToArray(); mi.SaveAndReimport();
                        AnimationImportSettings.ConfigureClips(mi); mi.SaveAndReimport(); changed++;
                        continue;
                    }
                    var defs = mi.defaultClipAnimations;
                    string fn = Path.GetFileNameWithoutExtension(path);
                    var list = new List<ModelImporterClipAnimation>();
                    for (int i = 0; i < defs.Length; i++)
                    {
                        var c = defs[i];
                        c.name = defs.Length == 1 ? fn : fn + "_" + i;
                        bool loop = IsLoopTake(c.name);
                        c.loopTime = loop; c.loopPose = loop;
                        c.lockRootRotation = true; c.lockRootHeightY = true; c.lockRootPositionXZ = true;      // standing ambient: in place
                        c.keepOriginalOrientation = true; c.keepOriginalPositionY = true; c.keepOriginalPositionXZ = false; c.heightFromFeet = false; c.cycleOffset = 0f;
                        list.Add(c);
                    }
                    mi.clipAnimations = list.ToArray();
                    mi.SaveAndReimport(); changed++;
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
            Debug.Log("[BulkV13] importers set: " + changed);
            CleanFolder(folder, names);
            AssetDatabase.Refresh();
            // which clips came out
            int ok = 0, miss = 0;
            foreach (string nm in names) { if (AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipCleaner.PathOf(nm)) != null) ok++; else miss++; }
            Debug.Log("[BulkV13] cleaned clips present: " + ok + ", missing: " + miss);
            AnimLibBatch.Done();
        }

        /// <summary>CleanBatch for one folder only (CleanBatch.RunList loads every FBX of the library: too much memory next to three other editors): the same plan and the same cleaner per clip.</summary>
        static void CleanFolder(string folder, List<string> names)
        {
            var want = new HashSet<string>(names, StringComparer.Ordinal);
            CleanBatch.AllNames = new HashSet<string>(want);
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            var log = new StringBuilder("name,out,lenIn,lenOut,t0,t1,spikes,clamped,note\n");
            int n = 0, ok = 0, fail = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { folder }).OrderBy(x => AssetDatabase.GUIDToAssetPath(x), StringComparer.Ordinal))
                {
                    string path = AssetDatabase.GUIDToAssetPath(g);
                    foreach (var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                    {
                        if (c.name.StartsWith("__preview") || (want.Count > 0 && !want.Contains(c.name))) continue;
                        n++;
                        try
                        {
                            var plan = CleanBatch.PlanFor(c.name, c, new List<string> { "(unreg)" });
                            foreach (var r in ClipCleaner.CleanClip(rig, c, plan))
                            {
                                log.AppendLine(string.Join(",", new[] { c.name, Path.GetFileName(r.path), r.lenIn.ToString("0.##"), r.lenOut.ToString("0.##"), r.t0.ToString("0.##"), r.t1.ToString("0.##"), r.spikes.ToString(), r.clamped.ToString(), (r.note ?? "").Replace(",", ";") }));
                                ok++;
                            }
                        }
                        catch (Exception ex) { fail++; Debug.LogWarning("[BulkV13] clean " + c.name + ": " + ex.Message); }
                    }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            rig.Dispose();
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs/animlib");
            File.WriteAllText("Logs/animlib/bulk_clean.csv", log.ToString());
            Debug.Log("[BulkV13] cleaned: clips " + n + ", written " + ok + ", failed " + fail);
        }

        /// <summary>ClipQa over the Clean clips whose name starts with a name of the list.</summary>
        public static void QaNew()
        {
            BatchWatchdog.Arm(60);
            AssetDatabase.Refresh();
            var names = Names(Arg("-pixList", ""));
            string outFile = Arg("-pixOut", "Logs/animlib/bulk_qa.csv");
            var rig = AnimRig.CreateMedium(); if (rig == null) { AnimLibBatch.Done(); return; }
            var sb = new StringBuilder(QaRow.Header() + "\n"); int n = 0;
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipCleaner.CleanDir }).OrderBy(x => AssetDatabase.GUIDToAssetPath(x), StringComparer.Ordinal))
            {
                string p = AssetDatabase.GUIDToAssetPath(g); if (p.Contains("/_rejected/")) continue;
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p); if (c == null) continue;
                if (!names.Any(x => c.name == x || c.name.StartsWith(x + "_"))) continue;
                try { var r = ClipQa.Analyse(rig, c, ""); r.source = p; sb.AppendLine(r.Csv()); n++; }
                catch (Exception ex) { Debug.LogWarning("[BulkV13] qa " + c.name + ": " + ex.Message); }
            }
            rig.Dispose();
            Directory.CreateDirectory(Path.GetDirectoryName(outFile));
            File.WriteAllText(outFile, sb.ToString());
            Debug.Log("[BulkV13] qa rows " + n + " -> " + outFile);
            AnimLibBatch.Done();
        }
    }
}
