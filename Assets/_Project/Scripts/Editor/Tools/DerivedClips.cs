using System.Collections.Generic;
using System.Linq;
using PixFootball.Art;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Animations that did not exist as mocap and are derived from similar takes (trim / mirror / retime / layer / blend), written to Animations/Clean:
    /// onside and squib kicks (kickoff take mirrored / retimed), one-hand catches (a two-hand catch with the off arm relaxed), toe-tap catches (the sideline reach),
    /// walk start / stop (idle blended into the authored walk cycle) and face-up / face-down / side get-ups cut from the pair takes of the contact table.
    /// </summary>
    public static class DerivedClips
    {
        static AnimationClip Src(ArtLibrary lib, string name)
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipCleaner.PathOf(name));
            if (c != null) return c;
            c = lib != null ? lib.ClipByName(name) : null;
            return c ?? StickSheet.FindClip(name);
        }

        static PoseSeq Seq(AnimationClip c, float maxLen = 30f) { return PoseSeq.FromClip(c, 0f, Mathf.Min(c.length, maxLen), 30f); }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        public static void Run()
        {
            BatchWatchdog.Arm(20);
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            int n = 0;
            var log = new System.Text.StringBuilder();

            // ---- kicks
            var kick = Src(lib, "YAN__K_kickoff_kick_00_Rework");
            if (kick != null)
            {
                var k = Seq(kick); k.Loop = false;
                var on = k.MirrorAbout(-PlayerAvatar.ModelYaw).Retime(1.12f); on.Name = "onside_kick_00"; ClipCleaner.WriteSeq(on); n++; log.AppendLine("onside_kick_00 <- kickoff mirrored (left foot), 1.12x");
                var sq = k.Retime(0.92f); sq.Name = "squib_kick_00"; ClipCleaner.WriteSeq(sq); n++; log.AppendLine("squib_kick_00 <- kickoff 0.92x");
            }
            // ---- one-hand / toe-tap catches
            var idle = Src(lib, "Player_Standing_Idle");
            string[] leftArm = { "Left Shoulder", "Left Arm", "Left Forearm", "Left Hand", "LeftHand." };
            string[] catchSrc = { "CatchNormalBackRHigh", "CatchNormalFrontRHigh", "CatchNormalRightRHigh" };
            for (int i = 0; i < catchSrc.Length; i++)
            {
                var c = Src(lib, catchSrc[i]); if (c == null || idle == null) continue;
                var s = Seq(c); s.Loop = false;
                s.Layer(Seq(idle, 4f), leftArm, 0.92f);
                s.Name = "catch_one_hand_0" + i; ClipCleaner.WriteSeq(s); n++; log.AppendLine(s.Name + " <- " + catchSrc[i] + " with the left arm relaxed");
            }
            string[] tap = { "CatchNormalRightRHigh", "CatchNormalRightLHigh", "CatchNormalRightRLow" };
            for (int i = 0; i < tap.Length; i++)
            {
                var c = Src(lib, tap[i]); if (c == null) continue;
                var s = Seq(c); s.Loop = false; s.Name = "catch_toe_tap_0" + i; ClipCleaner.WriteSeq(s); n++; log.AppendLine(s.Name + " <- sideline reach " + tap[i]);
            }
            // ---- long snap: the centre's 3-point stance with the snapping arm swung back between the legs (underhand pass arm layered on the stance)
            var cStance = Src(lib, "3pstance_aggressive_002"); var under = Src(lib, "BGS__QB_Pass_Underhand");
            if (cStance != null && under != null)
            {
                var cs = Seq(cStance, 3f); cs = cs.Trim(0f, Mathf.Min(1.8f, cs.Duration)); cs.Loop = false;
                var us = Seq(under, 4f);
                cs.Layer(us, new[] { "Right Shoulder", "Right Arm", "Right Forearm", "Right Hand", "RightHand." }, 0.9f);
                cs.Name = "long_snap_00"; ClipCleaner.WriteSeq(cs); n++; log.AppendLine("long_snap_00 <- centre stance with the underhand-pass arm swing");
            }
            // ---- walk start / stop
            var walk = Src(lib, "PIX_Walk_Cycle");
            if (walk != null && idle != null)
            {
                var w = Seq(walk); var id = Seq(idle, 4f);
                foreach (bool start in new[] { true, false })
                {
                    int frames = start ? 33 : 27;
                    var o = PoseSeq.Alloc(frames, 30f, start ? "walk_start_00" : "walk_stop_00");
                    float ramp = start ? 0.75f : 0.8f;
                    Vector3 pos = Vector3.zero;
                    for (int i = 0; i < frames; i++)
                    {
                        float t = i / (float)(frames - 1);
                        float e = start ? Smooth(t / ramp) : 1f - Smooth(t / ramp);
                        int wi = i % w.N, wp = (i + w.N - 1) % w.N;
                        for (int m = 0; m < o.M.Length; m++) o.M[m][i] = Mathf.Lerp(id.M[m][Mathf.Min(i, id.N - 1)], w.M[m][wi], e);
                        o.RQ[i] = Quaternion.Slerp(id.RQ[0], w.RQ[wi], e);
                        Vector3 d = i > 0 && wi > 0 ? (w.RT[wi] - w.RT[wp]) * e : Vector3.zero; d.y = 0f;
                        pos += d;
                        o.RT[i] = new Vector3(pos.x, Mathf.Lerp(id.RT[0].y, w.RT[wi].y, e), pos.z);
                    }
                    o.Loop = false; ClipCleaner.WriteSeq(o); n++; log.AppendLine(o.Name + " <- idle blended " + (start ? "into" : "out of") + " the walk cycle");
                }
            }
            // ---- get-ups from the contact table (the pair takes contain the get-up of the tackled / blocked player)
            var table = ContactTable.Load();
            if (table != null)
            {
                int up = 0, down = 0, side = 0;
                foreach (var p in table.pairs)
                {
                    foreach (int actor in new[] { 0, 1 })
                    {
                        string cn = actor == 0 ? p.clipA : p.clipB; float gu = actor == 0 ? p.getUpA : p.getUpB, fu = actor == 0 ? p.faceUpA : p.faceUpB;
                        if (gu < 0.3f) continue;
                        string cat = fu > 0.4f ? "face_up" : (fu < -0.4f ? "face_down" : "side");
                        int cnt = cat == "face_up" ? up : (cat == "face_down" ? down : side);
                        if (cnt >= 8) continue;
                        var c = lib != null ? lib.ClipByName(cn) : null; if (c == null) continue;
                        float t0 = Mathf.Max(0f, gu - 0.45f), t1 = Mathf.Min(c.length, gu + 1.4f);
                        if (t1 - t0 < 1f) continue;
                        var s = PoseSeq.FromClip(c, t0, t1, 30f); s.Loop = false; s.RebaseRoot(true);
                        s.Name = "getup_" + cat + "_" + cnt.ToString("00");
                        ClipCleaner.WriteSeq(s); n++;
                        if (cat == "face_up") up++; else if (cat == "face_down") down++; else side++;
                        log.AppendLine(s.Name + " <- " + cn + " [" + t0.ToString("0.00") + ".." + t1.ToString("0.00") + "]");
                    }
                }
            }
            AssetDatabase.SaveAssets();
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/derived_clips.txt", log.ToString());
            Debug.Log("[DerivedClips] wrote " + n + " clips\n" + log);
        }
    }
}
