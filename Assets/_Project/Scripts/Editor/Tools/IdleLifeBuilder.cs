using System;
using System.Collections.Generic;
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
    /// v0.13.0 NATURAL-2: bakes the micro-motion of the MoCap Online Mobility Pro standing takes (Idle, Idle_v2, Fgt_v1..v4) into <see cref="IdleLifeData"/>: every take is sampled on the match body at 30 fps and every bone
    /// above the pelvis is stored as the turn of its local rotation against its own mean over the take (rotation vector, degrees). Run: -executeMethod PixFootball.EditorTools.IdleLifeBuilder.Build  (writes
    /// Assets/_Licensed/Resources/PixIdleLife.asset and Logs/motion/idle_life.txt). -pixIdleTakes a,b overrides the take list.
    /// </summary>
    public static class IdleLifeBuilder
    {
        static readonly string[] DefaultTakes = { "MOB1_Stand_Relaxed_Idle", "MOB1_Stand_Relaxed_Idle_v2", "MOB1_Stand_Relaxed_Fgt_v1", "MOB1_Stand_Relaxed_Fgt_v2", "MOB1_Stand_Relaxed_Fgt_v3", "MOB1_Stand_Relaxed_Fgt_v4" };
        static readonly HumanBodyBones[] Bs = { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand };

        public static void Build()
        {
            BatchWatchdog.Arm(10);
            Directory.CreateDirectory("Logs/motion");
            AssetDatabase.Refresh();
            var rig = AvRig.Create(PlayerSize.Medium); if (rig == null) { Debug.LogError("[IdleLife] no rig"); return; }
            string only = LocoCycleScan.CmdArg("-pixIdleTakes", "");
            var names = only.Length > 0 ? only.Split(',') : DefaultTakes;
            var data = ScriptableObject.CreateInstance<IdleLifeData>(); var takes = new List<IdleLifeData.Take>();
            var rep = new StringBuilder("take,frames,peak deg,rms deg;  per bone peak: spine chest head lSh rSh lUA rUA lLA rLA lHand rHand\n");
            foreach (var n in names)
            {
                var clip = MixamoImport.LoadClip(MobilityImport.Dir + "/" + n + ".fbx");
                if (clip == null) { Debug.LogWarning("[IdleLife] missing " + n); continue; }
                var rows = new List<Quaternion[]>();
                MixamoScan.Sample(rig, clip, (i, cnt, f) => { var q = new Quaternion[Bs.Length]; for (int b = 0; b < Bs.Length; b++) { var t = rig.A.GetBoneTransform(Bs[b]); q[b] = t != null ? t.localRotation : Quaternion.identity; } rows.Add(q); }, 30f, 0f);
                int F = rows.Count; if (F < 8) continue;
                var mean = new Quaternion[Bs.Length];
                for (int b = 0; b < Bs.Length; b++)
                {
                    Vector4 s = Vector4.zero; Quaternion r0 = rows[0][b];
                    for (int i = 0; i < F; i++) { var q = rows[i][b]; if (Quaternion.Dot(q, r0) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); s += new Vector4(q.x, q.y, q.z, q.w); }
                    float m = s.magnitude; mean[b] = m > 1e-6f ? new Quaternion(s.x / m, s.y / m, s.z / m, s.w / m) : r0;
                }
                var tk = new IdleLifeData.Take { name = n, fps = 30f, frames = F, delta = new float[F * Bs.Length * 3] };
                var peakB = new float[Bs.Length]; double sq = 0; float peak = 0f;
                for (int i = 0; i < F; i++)
                    for (int b = 0; b < Bs.Length; b++)
                    {
                        Quaternion d = Quaternion.Inverse(mean[b]) * rows[i][b]; float ang; Vector3 ax; d.ToAngleAxis(out ang, out ax);
                        if (ang > 180f) ang -= 360f; if (float.IsNaN(ax.x) || ax.sqrMagnitude < 1e-8f) { ax = Vector3.right; ang = 0f; }
                        Vector3 v = ax.normalized * ang; int o = (i * Bs.Length + b) * 3;
                        tk.delta[o] = v.x; tk.delta[o + 1] = v.y; tk.delta[o + 2] = v.z;
                        float mg = v.magnitude; if (mg > peakB[b]) peakB[b] = mg; if (mg > peak) peak = mg; sq += mg * mg;
                    }
                tk.peak = peak; tk.rms = (float)Math.Sqrt(sq / (F * Bs.Length)); takes.Add(tk);
                rep.AppendLine(n + "," + F + "," + peak.ToString("0.0") + "," + Math.Sqrt(sq / (F * Bs.Length)).ToString("0.00") + ";  " + string.Join(" ", peakB.Select(x => x.ToString("0.0"))));
            }
            data.takes = takes.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(IdleLifeData.EditorPath));
            AssetDatabase.DeleteAsset(IdleLifeData.EditorPath);
            AssetDatabase.CreateAsset(data, IdleLifeData.EditorPath);
            AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/motion/idle_life.txt", rep.ToString());
            rig.Dispose();
            Debug.Log("[IdleLife] baked " + takes.Count + " takes");
        }
    }
}
