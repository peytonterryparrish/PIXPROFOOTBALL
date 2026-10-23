using Stopwatch = System.Diagnostics.Stopwatch;
using System.IO;
using System.Text;
using PixFootball.Core;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>v0.10.0 MOTION: CPU cost of the matcher (editor, mono, no burst): database search per query and the inertializer per frame. Run: -executeMethod PixFootball.EditorTools.MotionBench.Run  (Logs/motion/bench.txt)</summary>
    public static class MotionBench
    {
        public static void Run()
        {
            BatchWatchdog.Arm(10);
            Directory.CreateDirectory("Logs/motion");
            var sb = new StringBuilder();
            var db = MotionDb.Load(); if (db == null) { Debug.LogError("[MotionBench] no db"); return; }
            var rnd = new System.Random(7);
            var q = new float[MotionDb.Dim];
            // queries: copies of random rows plus noise (the realistic case: the best row is close, the early out bites)
            const int N = 20000;
            var rows = new int[N]; for (int i = 0; i < N; i++) rows[i] = rnd.Next(db.Rows);
            float best; int sink = 0;
            for (int w = 0; w < 500; w++) { for (int i = 0; i < MotionDb.Dim; i++) q[i] = db.feat[rows[w] * MotionDb.Dim + i]; sink += db.Search(q, 4f, 0.15f, 6f, 0, 0, out best); }
            var sw = Stopwatch.StartNew();
            for (int n = 0; n < N; n++)
            {
                int r = rows[n];
                for (int i = 0; i < MotionDb.Dim; i++) q[i] = db.feat[r * MotionDb.Dim + i] + (float)(rnd.NextDouble() - 0.5) * 0.4f;
                sink += db.Search(q, 4f, 0.15f, 6f, 0, 0, out best);
            }
            sw.Stop();
            double perSearchMs = sw.Elapsed.TotalMilliseconds / N;
            sb.AppendLine("rows " + db.Rows + " clips " + db.clips.Count + " dims " + MotionDb.Dim);
            sb.AppendLine("search: " + (perSearchMs * 1000.0).ToString("0.0") + " us per query (every " + MotionMatcher.SearchInterval + " s: " + (perSearchMs * 0.1 / 0.0167 * 1.0).ToString("0.0000") + " ms per player per 60 Hz frame averaged)");
            sb.AppendLine("worst case (no early out): " + WorstMs(db, q).ToString("0.000") + " ms per query");
            // inertializer
            var rig = AvRig.Create(PlayerSize.Medium);
            if (rig != null)
            {
                var inz = new Inertializer(rig.A, rig.A.transform.parent);
                inz.Apply(0.016f, false);
                var sw2 = Stopwatch.StartNew(); const int M = 20000;
                for (int i = 0; i < M; i++) inz.Apply(0.016f, i % 20 == 0);
                sw2.Stop();
                sb.AppendLine("inertializer: " + (sw2.Elapsed.TotalMilliseconds / M * 1000.0).ToString("0.0") + " us per frame");
                rig.Dispose();
            }
            sb.AppendLine("sink " + sink);
            File.WriteAllText("Logs/motion/bench.txt", sb.ToString());
            Debug.Log("[MotionBench] " + sb);
        }

        static double WorstMs(MotionDb db, float[] q)
        {
            for (int i = 0; i < MotionDb.Dim; i++) q[i] = 50f;      // nothing is near: every row runs the whole dot product
            var sw = Stopwatch.StartNew(); const int N = 2000; float best; int s = 0;
            for (int n = 0; n < N; n++) s += db.Search(q, 4f, 0.15f, 6f, 0, 0, out best);
            sw.Stop(); if (s == 12345) Debug.Log("x");
            return sw.Elapsed.TotalMilliseconds / N;
        }
    }
}
