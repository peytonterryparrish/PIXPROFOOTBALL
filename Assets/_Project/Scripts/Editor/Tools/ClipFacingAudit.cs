using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PixFootball.Art;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.6 QA / fix: measures, for every clip of the ArtLibrary, the heading the body ends up with when the clip is played on a match avatar (the avatar root already carries <see cref="PlayerAvatar.ModelYaw"/>),
    /// writes the audit table (Docs/QA/stance_gallery/clip_facing.csv) and the runtime <see cref="ClipFacingTable"/> that corrects every clip that faces the wrong way. It also builds mirror-image copies of the
    /// stance clips (left hand down / other foot back) so left and right sides of the ball get the right-handed stance.
    /// Menu: PIX Football/Assets/Build Clip Facing. Batch: Unity -batchmode -projectPath ... -executeMethod PixFootball.EditorTools.ClipFacingAudit.Run -quit
    /// </summary>
    public static class ClipFacingAudit
    {
        public const string OutPath = "Docs/QA/stance_gallery/clip_facing.csv";
        public const string MirrorDir = "Assets/_Licensed/Resources/MirroredClips";
        /// <summary>Stance keys whose clips get a mirrored twin.</summary>
        static readonly string[] MirrorKeys = { "dl_stance", "stance_2pt", "stance_halfback", "stance_safety", "stance_3pt", "qb_stance_under", "qb_stance_shotgun", "idle_alt" };

        struct Measured { public float Mean, Spread, HeadY; public int Hand, Foot; public float[] Hips, Shoulders; }

        static Measured Measure(Animator animator, AnimationClip clip)
        {
            float len = Mathf.Max(0.05f, clip.length);
            float[] fr = len > 8f ? new[] { 0.5f, 0.35f, 0.65f } : new[] { 0.15f, 0.5f, 0.85f };
            var poses = new BodyPose[3];
            for (int k = 0; k < 3; k++)
            {
                PlayableGraph g;
                PixFootball.EditorTools.AnimPlay.PlayClip(animator, clip, out g);
                g.GetOutput(0).GetSourcePlayable().SetTime(len * fr[k]);
                g.Evaluate(0f);
                poses[k] = BodyHeading.Measure(animator);
                g.Destroy();
            }
            float[] comb = new float[3];
            for (int k = 0; k < 3; k++)
            {
                // the hips carry the facing; the shoulders swing with the arms / twist in stances
                float a = poses[k].HipsYaw * Mathf.Deg2Rad, b = poses[k].ShouldersYaw * Mathf.Deg2Rad;
                comb[k] = Mathf.Atan2(0.7f * Mathf.Sin(a) + 0.3f * Mathf.Sin(b), 0.7f * Mathf.Cos(a) + 0.3f * Mathf.Cos(b)) * Mathf.Rad2Deg;
            }
            var m = new Measured();
            m.Mean = ClipFacingTable.MeanAngle(comb[0], comb[1], comb[2]);
            m.Spread = Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(m.Mean, comb[0])), Mathf.Max(Mathf.Abs(Mathf.DeltaAngle(m.Mean, comb[1])), Mathf.Abs(Mathf.DeltaAngle(m.Mean, comb[2]))));
            var mid = poses[1];
            m.Hand = Mathf.Abs(mid.LeftHandY - mid.RightHandY) > 0.25f ? (mid.LeftHandY < mid.RightHandY ? -1 : 1) : 0;
            float lr = mid.LeftFootFwd - mid.RightFootFwd;
            m.Foot = Mathf.Abs(lr) > 0.12f ? (lr < 0f ? -1 : 1) : 0;
            m.HeadY = mid.HeadHeight;
            m.Hips = new[] { poses[0].HipsYaw, poses[1].HipsYaw, poses[2].HipsYaw };
            m.Shoulders = new[] { poses[0].ShouldersYaw, poses[1].ShouldersYaw, poses[2].ShouldersYaw };
            return m;
        }

        [MenuItem("PIX Football/Assets/Build Clip Facing")]
        public static void Run()
        {
            ArtLibrary.ResetCache(); ClipFacingTable.ResetCache();
            var lib = ArtLibrary.Load();
            if (lib == null) { Debug.LogError("[ClipFacing] no ArtLibrary"); return; }
            var av = PlayerAvatar.Spawn(null, PlayerSize.Medium, TeamColors.Home, "FacingProbe");
            var animator = av.Animator;
            var table = AssetDatabase.LoadAssetAtPath<ClipFacingTable>(ClipFacingTable.EditorPath);
            if (table == null) { table = ScriptableObject.CreateInstance<ClipFacingTable>(); MaterialFactory.EnsureFolder("Assets/_Licensed/Resources"); AssetDatabase.CreateAsset(table, ClipFacingTable.EditorPath); }
            table.entries.Clear();
            var sb = new StringBuilder();
            sb.AppendLine("key,clip,source,length,loop,hips0,shoulders0,hips1,shoulders1,hips2,shoulders2,headY,meanYaw,spread,offset,handDown,footBack");
            int n = 0, corrected = 0;
            var seen = new HashSet<string>();
            var mirrorSources = new List<AnimationClip>();
            foreach (var e in lib.clips)
            {
                if (e == null || e.clip == null || string.IsNullOrEmpty(e.key)) continue;
                var clip = e.clip;
                bool firstOfName = seen.Add(clip.name);
                var m = Measure(animator, clip);
                float off = ClipFacingTable.OffsetOf(m.Mean, m.Spread);
                if (firstOfName)
                {
                    table.entries.Add(new ClipFacingTable.Entry { clip = clip.name, yaw = m.Mean, spread = m.Spread, handDown = m.Hand, footBack = m.Foot, upright = m.HeadY, length = clip.length });
                    if (Mathf.Abs(off) > 0.1f) corrected++;
                }
                if ((Array.IndexOf(MirrorKeys, e.key) >= 0 || e.key.StartsWith("stance_")) && !mirrorSources.Contains(clip) && !clip.name.EndsWith("_M") && clip.length < 90f) mirrorSources.Add(clip);
                string src = e.source ?? ""; int slash = src.LastIndexOf('/'); string pack = slash > 0 ? src.Substring(0, slash) : src;
                sb.AppendLine(e.key + "," + clip.name + "," + pack + "," + clip.length.ToString("0.00") + "," + (clip.isLooping ? 1 : 0) + "," + m.Hips[0].ToString("0") + "," + m.Shoulders[0].ToString("0") + "," + m.Hips[1].ToString("0") + "," + m.Shoulders[1].ToString("0") + "," + m.Hips[2].ToString("0") + "," + m.Shoulders[2].ToString("0") + "," + m.HeadY.ToString("0.00") + "," + m.Mean.ToString("0") + "," + m.Spread.ToString("0") + "," + off.ToString("0") + "," + m.Hand + "," + m.Foot);
                n++;
            }

            // ---- mirror-image twins of the stance clips (the Humanoid "Mirror" clip setting): left hand down / the other foot back
            int mirrored = 0;
            MaterialFactory.EnsureFolder(MirrorDir);
            foreach (var clip in mirrorSources)
            {
                string path = MirrorDir + "/" + ClipCleaner.SafeName(clip.name) + "_M.anim";     // v0.8: clip names like root|X contain a pipe: not a valid file name
                var twin = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (twin == null)
                {
                    twin = UnityEngine.Object.Instantiate(clip); twin.name = clip.name + "_M";
                    AssetDatabase.CreateAsset(twin, path);
                }
                var set = AnimationUtility.GetAnimationClipSettings(twin);
                set.mirror = true; set.loopTime = clip.isLooping;
                AnimationUtility.SetAnimationClipSettings(twin, set);
                EditorUtility.SetDirty(twin);
                var m = Measure(animator, twin);
                table.entries.Add(new ClipFacingTable.Entry { clip = twin.name, yaw = m.Mean, spread = m.Spread, handDown = m.Hand, footBack = m.Foot, upright = m.HeadY, length = clip.length });
                foreach (var en in table.entries) if (en.clip == clip.name) en.mirror = twin;
                sb.AppendLine("mirror," + twin.name + ",MirroredClips," + clip.length.ToString("0.00") + "," + (clip.isLooping ? 1 : 0) + ",,,,,,," + m.HeadY.ToString("0.00") + "," + m.Mean.ToString("0") + "," + m.Spread.ToString("0") + "," + ClipFacingTable.OffsetOf(m.Mean, m.Spread).ToString("0") + "," + m.Hand + "," + m.Foot);
                mirrored++;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            File.WriteAllText(OutPath, sb.ToString());
            EditorUtility.SetDirty(table); AssetDatabase.SaveAssets();
            UnityEngine.Object.DestroyImmediate(av.gameObject);
            Debug.Log("[ClipFacing] measured " + n + " clips, " + corrected + " need a facing correction, " + mirrored + " mirrored twins; table " + ClipFacingTable.EditorPath + ", csv " + OutPath);
        }
    }
}

namespace PixFootball.EditorTools
{
    /// <summary>Batch entry points for the release pipeline: the facing table must be rebuilt after every ArtLibrary build (clips re-imported / retargeted / cleaned change their facing).</summary>
    public static class ClipFacingRun
    {
        public static void LibraryAndFacing() { ArtLibraryBuilder.Build(); ClipFacingAudit.Run(); }
        public static void Facing() { ClipFacingAudit.Run(); }
    }
}
