using System.Collections.Generic;
using System.IO;
using System.Text;
using PixFootball.Art;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.9.0 QBSTANCE (E-107, E-306, E-311): mirror-image twins (the Humanoid "Mirror" clip setting, the same mechanism as the stance mirrors of <see cref="ClipFacingAudit"/>) of the takes a left-handed passer and the right-footed kickers need:
    /// the derived throw takes (pixqb_*), the onside kick (left-footed in the mocap) and the holder take. Additive: the existing <see cref="ClipFacingTable"/> keeps every entry, the twins and their <c>mirror</c> links are added.
    /// Run: Unity -batchmode -quit -executeMethod PixFootball.EditorTools.QbMirrorBuilder.Run
    /// </summary>
    public static class QbMirrorBuilder
    {
        static readonly string[] Keys = { "kick_onside", "kick_holder", "pixqb_throw_set", "pixqb_throw_run", "pixqb_throw_away", "qb_fake_handoff", "qb_toss", "qb_drop_3", "qb_drop_5", "qb_drop_7" };

        public static void Run()
        {
            ArtLibrary.ResetCache(); ClipFacingTable.ResetCache();
            var lib = ArtLibrary.Load();
            var table = AssetDatabase.LoadAssetAtPath<ClipFacingTable>(ClipFacingTable.EditorPath);
            if (lib == null || table == null) { Debug.LogError("[QbMirror] no library / facing table"); return; }
            var av = PlayerAvatar.Spawn(null, PlayerSize.Medium, TeamColors.Home, "MirrorProbe");
            var sb = new StringBuilder();
            int made = 0;
            MaterialFactory.EnsureFolder(ClipFacingAudit.MirrorDir);
            foreach (string key in Keys)
            {
                int n = lib.ClipVariantCount(key);
                for (int i = 0; i < n; i++)
                {
                    var clip = lib.ClipVariant(key, i);
                    if (clip == null || clip.name.EndsWith("_M") || clip.length > 20f) continue;
                    ClipFacingTable.Entry src = null;
                    foreach (var en in table.entries) if (en.clip == clip.name) { src = en; break; }
                    if (src == null) continue;
                    if (src.mirror != null) continue;
                    string path = ClipFacingAudit.MirrorDir + "/" + ClipCleaner.SafeName(clip.name) + "_M.anim";
                    var twin = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (twin == null) { twin = Object.Instantiate(clip); twin.name = clip.name + "_M"; AssetDatabase.CreateAsset(twin, path); }
                    var set = AnimationUtility.GetAnimationClipSettings(twin); set.mirror = true; set.loopTime = clip.isLooping; AnimationUtility.SetAnimationClipSettings(twin, set);
                    EditorUtility.SetDirty(twin);
                    var m = ClipFacingAudit.Measure(av.Animator, twin);
                    bool have = false; foreach (var en in table.entries) if (en.clip == twin.name) { have = true; break; }
                    if (!have) table.entries.Add(new ClipFacingTable.Entry { clip = twin.name, yaw = m.Mean, spread = m.Spread, handDown = m.Hand, footBack = m.Foot, upright = m.HeadY, length = clip.length });
                    src.mirror = twin;
                    sb.AppendLine(key + " : " + clip.name + " -> " + twin.name); made++;
                }
            }
            // the twins have the same key moments (release, exchange) as their sources: copy the timing entries
            var timing = AssetDatabase.LoadAssetAtPath<ClipTimingTable>(ClipTimingTable.EditorPath);
            int timed = 0;
            if (timing != null)
            {
                var add = new List<ClipTimingTable.Entry>();
                foreach (var en in table.entries)
                {
                    if (en.mirror == null) continue;
                    ClipTimingTable.Entry te = null, have = null;
                    foreach (var x in timing.entries) { if (x.clip == en.clip) te = x; if (x.clip == en.mirror.name) have = x; }
                    if (te != null && have == null) { add.Add(new ClipTimingTable.Entry { clip = en.mirror.name, t = te.t, t2 = te.t2, d = te.d }); timed++; }
                }
                timing.entries.AddRange(add); EditorUtility.SetDirty(timing); ClipTimingTable.ResetCache();
            }
            sb.AppendLine("timing entries added: " + timed);
            EditorUtility.SetDirty(table); AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Object.DestroyImmediate(av.gameObject);
            string outp = "D:/PIXFootball/Renders/qbstance/mirrors.txt"; Directory.CreateDirectory(Path.GetDirectoryName(outp)); File.WriteAllText(outp, sb.ToString());
            Debug.Log("[QbMirror] made " + made + " mirror twins");
        }
    }
}
