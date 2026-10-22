using System.IO;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>Batch entry points of the animation track (import of the new mocap, audit, clean, register).</summary>
    public static class AnimTools
    {
        /// <summary>Imports the re-baked Yan/BGS folder as Humanoid, fixes invalid avatars, then audits every clip.</summary>
        public static void ImportYanAndAudit()
        {
            AssetDatabase.Refresh();
            int n = AnimationImportSettings.ApplyFolder("Assets/_Licensed/DraftFever/Animations/BoNES_Yan");
            Debug.Log("[AnimTools] yan reimported=" + n);
            AssetDatabase.Refresh();
            AnimationImportSettings.ReportAvatars();
            AnimAudit.RunAll();
        }

        public static void AuditOnly() { AnimAudit.RunAll(); }

        /// <summary>Clean every clip, import the result and audit the Clean folder.</summary>
        public static void CleanAndAudit()
        {
            AssetDatabase.Refresh();
            foreach (string f in new[] { "Assets/_Licensed/DraftFever/Animations/BoNES_Yan", "Assets/_Licensed/DraftFever/Animations/PIX_Authored" })
                if (AssetDatabase.IsValidFolder(f)) Debug.Log("[AnimTools] reimported " + f + " = " + AnimationImportSettings.ApplyFolder(f));
            AssetDatabase.Refresh();
            AnimationImportSettings.ApplyAll();     // QA-907: loop / root flags of the raw FBX clips follow the corrected name rules (only changed models re-import)
            AssetDatabase.Refresh();
            CleanBatch.RunAll();
            AssetDatabase.Refresh();
            DerivedClips.Run();
            AssetDatabase.Refresh();
            AnimAudit.RunFolder(ClipCleaner.CleanDir, "clean");
        }
    }
}

namespace PixFootball.EditorTools
{
    /// <summary>The v0.8 two-stage rebuild: stage 1 imports the new mocap, cleans every clip and audits the result; stage 2 rebuilds the ArtLibrary (Clean/ replaces the raw takes), the timing / facing / move tables and the player controller.</summary>
    public static class AnimRebuild
    {
        public static void Stage1()
        {
            BatchWatchdog.Arm(60);
            AssetDatabase.Refresh();
            foreach (string f in new[] { "Assets/_Licensed/DraftFever/Animations/BoNES_Yan", "Assets/_Licensed/DraftFever/Animations/PIX_Authored", "Assets/_Licensed/DraftFever/Animations/Motus_Extra" })
                if (AssetDatabase.IsValidFolder(f)) Debug.Log("[AnimRebuild] import settings " + f + " = " + AnimationImportSettings.ApplyFolder(f));
            AssetDatabase.Refresh();
            AnimationImportSettings.ApplyAll();     // QA-907: loop / root flags of the raw FBX clips follow the corrected name rules (only changed models re-import)
            AssetDatabase.Refresh();
            CleanBatch.RunAll();
            AssetDatabase.Refresh();
            DerivedClips.Run();
            AssetDatabase.Refresh();
            AnimAudit.RunFolder(ClipCleaner.CleanDir, "clean");
        }

        public static void Stage2()
        {
            BatchWatchdog.Arm(60);
            AssetDatabase.Refresh();
            ArtLibraryBuilder.Build();
            try { ClipTimingBuilder.Run(); } catch (System.Exception ex) { Debug.LogError("[AnimRebuild] ClipTimingBuilder failed: " + ex); }
            try { ContactClipBuilder.BuildMoveTakes(); } catch (System.Exception ex) { Debug.LogError("[AnimRebuild] MoveTakes failed: " + ex); }
            try { RuntimeAssetsBuilder.Build(); } catch (System.Exception ex) { Debug.LogError("[AnimRebuild] RuntimeAssetsBuilder failed: " + ex); }
            try { ClipFacingAudit.Run(); } catch (System.Exception ex) { Debug.LogError("[AnimRebuild] ClipFacingAudit failed: " + ex); }
            CycleSpeeds.Run();
            GroundBySize.Run();
            AnimAudit.RunAll();     // audit of the library as the game now sees it (Clean/ clips)
            Debug.Log("[AnimRebuild] stage 2 done");
        }
    }
}

namespace PixFootball.EditorTools
{
    /// <summary>QA-910: the same clips on the Small / Medium / Large bodies - how far the lowest foot point sits from the turf per body size (Logs/anim_audit/ground_by_size.txt).</summary>
    public static class GroundBySize
    {
        public static void Run()
        {
            BatchWatchdog.Arm(60);
            var lib = PixFootball.Art.ArtLibrary.Load(); if (lib == null) return;
            var sb = new System.Text.StringBuilder("clip,size,hipMed,groundP10,footMin\n");
            string[] sizes = { "player_small", "player_medium", "player_large" };
            string[] clips = { "Player_Standing_Idle", "JogCycleJR", "RunCycleJR_1", "3pstance_aggressive_001", "DlineIdle3ptR", "BGS__BGS_CB_Idle_M2M", "2pstance_safety_001" };
            foreach (string sz in sizes)
            {
                var prefab = lib.Prefab(sz); if (prefab == null) continue;
                var rig = AnimRig.Create(prefab);
                var idle = lib.ClipByName("Player_Standing_Idle");
                var ip = idle != null ? rig.PoseAt(idle, 0.5f) : default(AnimRig.Frame);
                foreach (string cn in clips)
                {
                    var c = lib.ClipByName(cn); if (c == null) continue;
                    var r = AnimAudit.Analyse(rig, c, ip);
                    sb.AppendLine(cn + "," + sz + "," + r.hipMed.ToString("0.000") + "," + r.groundP10.ToString("0.000") + "," + r.footMinY.ToString("0.000"));
                }
                rig.Dispose();
            }
            System.IO.Directory.CreateDirectory("Logs/anim_audit");
            System.IO.File.WriteAllText("Logs/anim_audit/ground_by_size.txt", sb.ToString());
            UnityEngine.Debug.Log("[GroundBySize] " + sb);
        }
    }
}

namespace PixFootball.EditorTools
{
    public static class AnimRebuildAll
    {
        /// <summary>Stage 1 + stage 2 in one launch (shared-project etiquette: one long editor session instead of two).</summary>
        public static void Run()
        {
            BatchWatchdog.Arm(100);
            AnimRebuild.Stage1();
            AssetDatabase.Refresh();
            AnimRebuild.Stage2();
        }
    }
}

namespace PixFootball.EditorTools
{
    public static class AnimRebuildStage3
    {
        /// <summary>The tail of stage 2 (facing table, cycle speeds, ground by body size, audit of the library as the game sees it).</summary>
        public static void Run()
        {
            BatchWatchdog.Arm(30);
            AssetDatabase.Refresh();
            try { ClipFacingAudit.Run(); } catch (System.Exception ex) { Debug.LogError("[AnimRebuild] ClipFacingAudit failed: " + ex); }
            CycleSpeeds.Run();
            GroundBySize.Run();
            AnimAudit.RunAll();
            Debug.Log("[AnimRebuild] stage 3 done");
        }
    }
}
