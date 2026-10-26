using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.13.0 QB-2 QA probe: dumps the kinematics of the quarterback clips whose name matches <c>-pixQbFitRe</c> (regex) as a text table every <c>-pixQbFitStep</c> seconds: the hips path (m, from the first frame, authored frame), speed,
    /// hip / chest yaw, hips height, the hands' mid point in the hips frame (fwd / left / up), hand distance, and whether each foot is planted. Used to decide where a take's catch, drop, plant and exchange are before the catalog rules are written.
    /// Batch: <c>-executeMethod PixFootball.EditorTools.QbFitProbe.Run -pixQbFitRe "regex"</c>. Output: <c>Logs/qb/fit_probe.txt</c>.
    /// </summary>
    public static class QbFitProbe
    {
        static string Arg(string n, string d) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i + 1 < a.Length; i++) if (a[i] == n) return a[i + 1]; return d; }
        static float YawOf(Vector3 v) { return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; }

        public static void Run()
        {
            BatchWatchdog.Arm(30);
            try
            {
                var re = new Regex(Arg("-pixQbFitRe", "dropback"), RegexOptions.IgnoreCase);
                float step = float.Parse(Arg("-pixQbFitStep", "0.2"), CultureInfo.InvariantCulture);
                var rig = AnimRig.CreateMedium(); if (rig == null) return;
                var sb = new StringBuilder();
                Directory.CreateDirectory(QbClipAudit.OutDir);
                foreach (var c in QbClipAudit.QbClips())
                {
                    if (!re.IsMatch(c.name)) continue;
                    var fr = rig.Sample(c, 0f, Mathf.Min(c.length, 12f), 30f);
                    int N = fr.Count; if (N < 3) continue;
                    float ground = float.MaxValue; for (int i = 0; i < N; i++) ground = Mathf.Min(ground, Mathf.Min(fr[i].P[AnimRig.LT].y, fr[i].P[AnimRig.RT].y));
                    sb.AppendLine("== " + c.name + " len " + c.length.ToString("0.00") + " frames " + N);
                    sb.AppendLine("t,hx,hz,speed,hipYaw,chestYaw,hipsY,handF,handL,handUp,handD,pR,pL,rfF,rfL,lfF,lfL");
                    Vector3 h0 = fr[0].P[AnimRig.Hips];
                    int every = Mathf.Max(1, Mathf.RoundToInt(step * 30f));
                    for (int i = 0; i < N; i += every)
                    {
                        var p = fr[i].P; Vector3 h = p[AnimRig.Hips];
                        int a = Mathf.Max(0, i - 3), b = Mathf.Min(N - 1, i + 3);
                        Vector3 v = fr[b].P[AnimRig.Hips] - fr[a].P[AnimRig.Hips]; v.y = 0f; float sp = v.magnitude / ((b - a) / 30f);
                        Vector3 fwd = AnimRig.Forward(fr[i]); fwd.y = 0f; fwd.Normalize(); Vector3 left = Vector3.Cross(Vector3.up, fwd) * -1f;
                        Vector3 hm = (p[AnimRig.LH] + p[AnimRig.RH]) * 0.5f - h;
                        float pr = p[AnimRig.RT].y - 0.023f - ground, pl = p[AnimRig.LT].y - 0.023f - ground;
                        sb.AppendLine(string.Join(",", new[] { (i / 30f).ToString("0.00"), (h.x - h0.x).ToString("0.00"), (h.z - h0.z).ToString("0.00"), sp.ToString("0.0"), YawOf(AnimRig.Forward(fr[i])).ToString("0"), YawOf(AnimRig.ShoulderForward(fr[i])).ToString("0"), (h.y - ground).ToString("0.00"),
                            Vector3.Dot(hm, fwd).ToString("0.00"), Vector3.Dot(hm, left).ToString("0.00"), hm.y.ToString("0.00"), Vector3.Distance(p[AnimRig.LH], p[AnimRig.RH]).ToString("0.00"), pr < 0.04f ? "1" : "0", pl < 0.04f ? "1" : "0", Vector3.Dot(p[AnimRig.RT] - h, fwd).ToString("0.00"), Vector3.Dot(p[AnimRig.RT] - h, left).ToString("0.00"), Vector3.Dot(p[AnimRig.LT] - h, fwd).ToString("0.00"), Vector3.Dot(p[AnimRig.LT] - h, left).ToString("0.00") }));
                    }
                }
                rig.Dispose();
                File.WriteAllText(QbClipAudit.OutDir + "/fit_probe.txt", sb.ToString());
                Debug.Log("[QbFitProbe] done " + sb.Length);
            }
            catch (Exception ex) { Debug.LogError("[QbFitProbe] " + ex); }
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
