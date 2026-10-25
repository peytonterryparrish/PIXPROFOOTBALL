using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Core;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.12.0 MOBILITY: MoCap Online "Mobility 27B Pro" (licensed, the owner's, never committed). The package is staged under Assets/_MocapOnlineStage/ in the private copy while working and moves to
    /// Assets/_Licensed/MocapOnline/ (git-ignored) at the end, keeping the GUIDs of its .meta files. 224 Humanoid takes (MotusMan avatar, shared), each in a Root_Motion and an in-place IPC version; the
    /// Root_Motion set is the source: its "Root" reference node (always under the body, always facing forward) is the root-motion node, so the root path is smooth and a strafe keeps facing forward.
    /// Run: -executeMethod PixFootball.EditorTools.MobilityImport.Run  (importer settings + inventory: Logs/motion/mobility_inventory.csv / .md). Folder override: -pixMobDir.
    /// </summary>
    public static class MobilityImport
    {
        public static string Root { get { return LocoCycleScan.CmdArg("-pixMobDir", ResolveRoot()); } }
        public static string Dir { get { return Root + "/Animation/Root_Motion"; } }
        /// <summary>The folder that holds the package folder (the stage while working, Assets/_Licensed/MocapOnline afterwards).</summary>
        public static string StageRoot { get { return Path.GetDirectoryName(Root).Replace((char)92, (char)47); } }
        /// <summary>Where the cleaned and mirrored copies of the takes live (inside the package folder: they move with it).</summary>
        public static string OutDir { get { return Root + "/Clean"; } }

        static string ResolveRoot()
        {
            // working: the stage; after the move to the shared tree: Assets/_Licensed/MocapOnline
            foreach (var r in new[] { "Assets/_MocapOnlineStage/Mobility_Pro", "Assets/_Licensed/MocapOnline/Mobility_Pro" }) if (AssetDatabase.IsValidFolder(r)) return r;
            return "Assets/_Licensed/MocapOnline/Mobility_Pro";
        }

        /// <summary>The importer of one take: Humanoid, avatar copied from MotusMan (as shipped), root motion node = Root, single clip named after the file, no loop flag (the database cuts its own loops).</summary>
        static bool Configure(ModelImporter mi, string path)
        {
            bool changed = false;
            if (mi.animationType != ModelImporterAnimationType.Human) { mi.animationType = ModelImporterAnimationType.Human; changed = true; }
            if (mi.motionNodeName != "Root") { mi.motionNodeName = "Root"; changed = true; }
            if (mi.animationCompression != ModelImporterAnimationCompression.Off) { mi.animationCompression = ModelImporterAnimationCompression.Off; changed = true; }
            if (!mi.resampleCurves) { mi.resampleCurves = true; changed = true; }
            if (mi.importBlendShapes || mi.importCameras || mi.importLights) { mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false; changed = true; }
            var clips = mi.clipAnimations; if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
            string nm = Path.GetFileNameWithoutExtension(path);
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i].loopTime || clips[i].name != (clips.Length == 1 ? nm : nm + "_" + i)) changed = true;
                clips[i].name = clips.Length == 1 ? nm : nm + "_" + i; clips[i].loopTime = false;
            }
            mi.clipAnimations = clips;
            return changed;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(50);
            Directory.CreateDirectory("Logs/motion");
            AssetDatabase.Refresh();
            string filter = LocoCycleScan.CmdArg("-pixMmFilter", "");
            bool analyse = LocoCycleScan.CmdArg("-pixMobNoScan", "0") != "1";
            var files = Directory.GetFiles(Dir, "*.fbx").Select(f => f.Replace('\\', '/')).OrderBy(x => x).Where(f => filter.Length == 0 || Path.GetFileName(f).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            int reimported = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var f in files)
                {
                    var mi = AssetImporter.GetAtPath(f) as ModelImporter; if (mi == null) { Debug.LogWarning("[Mobility] no importer " + f); continue; }
                    if (Configure(mi, f)) { mi.SaveAndReimport(); reimported++; }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
            Debug.Log("[Mobility] importer settings applied to " + reimported + " of " + files.Count);
            var rig = AvRig.Create(PlayerSize.Medium); if (rig == null) { Debug.LogError("[Mobility] no rig"); return; }
            var plant = new FootPlant(rig.A);
            var rows = new List<MixamoScan.Row>(); var meta = new StringBuilder("file,animType,avatarFrom,takeFps,clipFps,takeSeconds,clipSeconds,rootMotionNode,humanoidCurves\n");
            foreach (var f in files)
            {
                var mi = AssetImporter.GetAtPath(f) as ModelImporter; var clip = MixamoImport.LoadClip(f);
                if (mi == null || clip == null) { Debug.LogWarning("[Mobility] no clip " + f); continue; }
                var ti = mi.importedTakeInfos != null && mi.importedTakeInfos.Length > 0 ? mi.importedTakeInfos[0] : default(TakeInfo);
                meta.AppendLine(string.Join(",", new[] { Path.GetFileName(f), mi.animationType.ToString(), mi.sourceAvatar != null ? mi.sourceAvatar.name : (mi.avatarSetup.ToString()), ti.sampleRate.ToString("0.#", CultureInfo.InvariantCulture), clip.frameRate.ToString("0.#", CultureInfo.InvariantCulture), (ti.stopTime - ti.startTime).ToString("0.000", CultureInfo.InvariantCulture), clip.length.ToString("0.000", CultureInfo.InvariantCulture), mi.motionNodeName, clip.humanMotion.ToString() }));
                if (!analyse) continue;
                try { rows.Add(MixamoScan.Analyze(rig, plant, clip, true, null, 0f)); }
                catch (Exception e) { Debug.LogWarning("[Mobility] " + clip.name + ": " + e.Message); }
            }
            File.WriteAllText("Logs/motion/mobility_import.csv", meta.ToString());
            if (analyse) MixamoScan.WriteReport(rows, "Logs/motion/mobility_inventory");
            rig.Dispose();
            Debug.Log("[Mobility] inventory " + rows.Count + " clips");
        }
    }
}
