using System.Collections.Generic;
using System.IO;
using System.Text;
using PixFootball.Art;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace PixFootball.EditorTools
{
    /// <summary>v0.9.0 QBSTANCE: offline clip probe (QA only). Samples the mocap clips of the kick / QB keys on a match avatar: which foot kicks (peak ankle speed), how long each take is, how much of it is flight phase (both toes off the turf), where the hands travel.
    /// Run: Unity -batchmode -executeMethod PixFootball.EditorTools.QbStanceProbe.Run -pixProbeOut file</summary>
    public static class QbStanceProbe
    {
        static readonly string[] Keys = { "kick_off", "kick_field_goal", "punt", "punt_fast", "kick_onside", "kick_squib", "kick_holder", "long_snap", "kick_return_catch",
            "qb_drop_3", "qb_drop_5", "qb_drop_7", "qb_snap_under", "qb_snap_shotgun", "qb_scramble", "qb_run_left", "qb_run_right", "qb_fake_handoff", "qb_toss", "qb_cadence", "qb_cadence_stomp", "qb_cadence_clap", "qb_cadence_raise",
            "qb_kneel", "qb_spike", "qb_sneak", "qb_throw_offbalance", "qb_throw_sidearm", "qb_throw_jump", "qb_falling_throw", "qb_sidestep", "qb_look_around", "qb_ready_pass", "qb_hail_mary", "qb_slide",
            "pixqb_throw_set", "pixqb_throw_run", "pixqb_throw_off", "pixqb_throw_away", "pixqb_pump" };

        public static void Run()
        {
            string outPath = "D:/PIXFootball/Renders/qbstance/probe.txt";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-pixProbeOut") outPath = args[i + 1];
            ArtLibrary.ResetCache(); ClipFacingTable.ResetCache(); ClipTimingTable.ResetCache();
            var lib = ArtLibrary.Load();
            var sb = new StringBuilder();
            if (lib == null) { File.WriteAllText(outPath, "no ArtLibrary"); return; }
            var av = PlayerAvatar.Spawn(null, PlayerSize.Medium, TeamColors.Home, "Probe");
            var an = av.Animator;
            var lf = an.GetBoneTransform(HumanBodyBones.LeftFoot); var rf = an.GetBoneTransform(HumanBodyBones.RightFoot);
            var lt = an.GetBoneTransform(HumanBodyBones.LeftToes); var rt = an.GetBoneTransform(HumanBodyBones.RightToes);
            var lh = an.GetBoneTransform(HumanBodyBones.LeftHand); var rh = an.GetBoneTransform(HumanBodyBones.RightHand);
            var hips = an.GetBoneTransform(HumanBodyBones.Hips);
            sb.AppendLine("key | clip | length | peakFootSpeed L/R (m/s) at t | flight fraction (both toes > 8cm) | hips y min/max | hand fwd max L/R (m, body frame) | timing t/t2/d");
            foreach (string key in Keys)
            {
                int n = lib.ClipVariantCount(key);
                if (n == 0) { sb.AppendLine(key + " | (none)"); continue; }
                for (int v = 0; v < Mathf.Min(n, (key == "qb_kneel" || key == "qb_spike") ? 3 : 5); v++)
                {
                    var clip = lib.ClipVariant(key, v); if (clip == null) continue;
                    float len = clip.length; float dt = 1f / 24f;
                    if (len > 12f && key != "qb_kneel" && key != "qb_spike") { sb.AppendLine(key + " | " + clip.name + " | " + len.ToString("0.0") + " | (long take, skipped)"); continue; }
                    Vector3 pl = Vector3.zero, pr = Vector3.zero; float pkL = 0, pkR = 0, tpL = 0, tpR = 0; int flight = 0, frames = 0; float hyMin = 9, hyMax = -9, hfL = -9, hfR = -9; bool have = false;
                    PlayableGraph g; AnimPlay.PlayClip(an, clip, out g);
                    var detail = new StringBuilder();
                    for (float t = 0; t <= len; t += dt)
                    {
                        g.GetOutput(0).GetSourcePlayable().SetTime(t); g.Evaluate(0f);
                        float gy = an.transform.position.y;
                        Vector3 L = lf.position, R = rf.position;
                        if (have) { float sl = (L - pl).magnitude / dt, sr = (R - pr).magnitude / dt; if (sl > pkL) { pkL = sl; tpL = t; } if (sr > pkR) { pkR = sr; tpR = t; } }
                        pl = L; pr = R; have = true;
                        float th = Mathf.Min(lt != null ? lt.position.y : lf.position.y, rt != null ? rt.position.y : rf.position.y);
                        float lth = (lt != null ? lt.position.y : lf.position.y) - gy, rth = (rt != null ? rt.position.y : rf.position.y) - gy;
                        if (lth > 0.08f && rth > 0.08f) flight++; frames++;
                        float hy = hips.position.y - gy; hyMin = Mathf.Min(hyMin, hy); hyMax = Mathf.Max(hyMax, hy);
                        Vector3 across = rf.position - lf.position; across.y = 0f;
                        var pose = BodyHeading.Measure(an); float yaw = pose.Yaw * Mathf.Deg2Rad; Vector3 fwd = new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw));
                        hfL = Mathf.Max(hfL, Vector3.Dot(lh.position - hips.position, fwd)); hfR = Mathf.Max(hfR, Vector3.Dot(rh.position - hips.position, fwd));
                        if ((key == "kick_holder" || key == "long_snap" || key == "qb_kneel" || key == "qb_spike") && frames % 6 == 0) detail.AppendLine("     t=" + t.ToString("0.00") + " hips y " + (hips.position.y - gy).ToString("0.00") + " L hand y " + (lh.position.y - gy).ToString("0.00") + " fwd " + Vector3.Dot(lh.position - hips.position, fwd).ToString("0.00") + " side " + Vector3.Dot(lh.position - hips.position, Vector3.Cross(Vector3.up, fwd)).ToString("0.00") + " | R hand y " + (rh.position.y - gy).ToString("0.00") + " fwd " + Vector3.Dot(rh.position - hips.position, fwd).ToString("0.00") + " side " + Vector3.Dot(rh.position - hips.position, Vector3.Cross(Vector3.up, fwd)).ToString("0.00"));
                    }
                    g.Destroy();
                    if (detail.Length > 0) sb.Append(detail.ToString());
                    ClipTimingTable.Entry e; string tm = ClipTimingTable.TryEntry(clip, out e) ? e.t.ToString("0.00") + "/" + e.t2.ToString("0.00") + "/" + e.d.ToString("0.0") : "-";
                    sb.AppendLine(key + " | " + clip.name + " | " + len.ToString("0.00") + " | L " + pkL.ToString("0.0") + "@" + tpL.ToString("0.00") + " R " + pkR.ToString("0.0") + "@" + tpR.ToString("0.00") + " => " + (pkR >= pkL ? "RIGHT" : "LEFT") + " | " + (frames > 0 ? (flight / (float)frames).ToString("0.00") : "-") + " | " + hyMin.ToString("0.00") + "/" + hyMax.ToString("0.00") + " | " + hfL.ToString("0.00") + "/" + hfR.ToString("0.00") + " | " + tm);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, sb.ToString());
            Object.DestroyImmediate(av.gameObject);
            Debug.Log("[QbStanceProbe] wrote " + outPath);
        }
    }
}
