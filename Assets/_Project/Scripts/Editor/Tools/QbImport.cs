using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.13.0 QB-MOCAP: imports the quarterback takes that were never imported (the retargeted ones: Yan2 under-centre drops and snaps, the Blend sneaks, the pump-fake variants, the shotgun remap) into the private
    /// <c>Assets/_Licensed/DraftFever/Animations/BoNES_QB/Day23</c>, humanoid import, then the cleaner (<see cref="CleanBatch"/>: ground fix, spike removal, segments, the ClipQa pass). The raw two-actor studio captures of day 2 / day 3 (<c>_A</c> / <c>_B</c> / <c>_AB</c>,
    /// 20 to 230 MB, un-retargeted) are not imported here: they need the Blender split + retarget of the studio pipeline.
    /// Input list: <c>D:/PIXFootball/Work/v013/qb/import_list.txt</c> (one project-relative FBX path per line, the first column of <c>never_imported.txt</c>). Batch: <c>-executeMethod PixFootball.EditorTools.QbImport.Run</c>.
    /// </summary>
    public static class QbImport
    {
        public const string Dest = "Assets/_Licensed/DraftFever/Animations/BoNES_QB/Day23";
        public const string ListPath = "D:/PIXFootball/Work/v013/qb/import_list.txt";

        public static void Run()
        {
            BatchWatchdog.Arm(90);
            try
            {
                Directory.CreateDirectory(Dest);
                var names = new List<string>(); var log = new StringBuilder();
                foreach (string line in File.ReadAllLines(ListPath))
                {
                    string src = line.Trim(); if (src.Length == 0 || src.StartsWith("#")) continue;
                    if (!File.Exists(src)) { log.AppendLine("missing " + src); continue; }
                    string dst = Dest + "/" + Path.GetFileName(src);
                    if (!File.Exists(dst)) File.Copy(src, dst);
                    log.AppendLine("copied " + dst);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Debug.Log("[QbImport] humanoid settings changed on " + AnimationImportSettings.ApplyFolder(Dest) + " models");
                AssetDatabase.Refresh();
                foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { Dest }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    foreach (var c in AssetDatabase.LoadAllAssetsAtPath(p).OfType<AnimationClip>())
                    {
                        if (c.name.StartsWith("__preview")) continue;
                        names.Add(c.name); log.AppendLine("take " + c.name + " (" + c.length.ToString("0.0") + " s) in " + Path.GetFileName(p));
                    }
                }
                Directory.CreateDirectory("Logs");
                File.WriteAllText("Logs/clean_in.txt", string.Join("\n", names.ToArray()) + "\n");
                File.WriteAllText(QbClipAudit.OutDir + "/import_log.txt", log.ToString());
                Debug.Log("[QbImport] " + names.Count + " takes -> Logs/clean_in.txt; running the cleaner");
                CleanBatch.RunList();
                AssetDatabase.Refresh();
            }
            catch (Exception ex) { Debug.LogError("[QbImport] " + ex); }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
