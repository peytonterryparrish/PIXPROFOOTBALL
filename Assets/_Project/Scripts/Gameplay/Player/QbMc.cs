using System;
using System.Collections.Generic;
using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.13.0 QB-MOCAP: the draw of the quarterback's mocap windows (<see cref="QbClipTable"/>): a no-repeat bag per slot (a clip is not drawn again before every clip of the slot's pool was drawn once; the pool is the rows that pass the caller's filter),
    /// deterministic for a seed. <c>-pixNoQbMocap</c> switches the whole mocap sequence off (the A/B "before" state: the pose layer).
    /// </summary>
    public static class QbMc
    {
        public static bool Off = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoQbMocap") >= 0;
        /// <summary>v0.13.0 QB-2: the fitted mocap drops (catch-and-drop takes, the simulated drop refitted to them) are OFF by default: their planted-foot slide (median 0.40 m/s) is over the gate; <c>-pixQbDropMocap</c> switches them on for QA.</summary>
        public static bool DropMocap = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixQbDropMocap") >= 0;
        /// <summary>Rows below this quality are never drawn (the catalog's verdict: foot slide, facing, hands, seam).</summary>
        public static float MinQuality = 35f;

        static readonly Dictionary<string, HashSet<string>> _used = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        /// <summary>Clips drawn so far in this process (QA / the log of the integration).</summary>
        public static readonly HashSet<string> Drawn = new HashSet<string>(StringComparer.Ordinal);

        public static void ResetBags() { _used.Clear(); Drawn.Clear(); }

        /// <summary>Draws one row of <paramref name="pool"/> that was not drawn before in the slot (the bag refills when it is empty). Null for an empty pool.</summary>
        public static QbClipTable.Entry Pick(string slot, IList<QbClipTable.Entry> pool, int seed)
        {
            if (pool == null || pool.Count == 0) return null;
            HashSet<string> used; if (!_used.TryGetValue(slot, out used)) _used[slot] = used = new HashSet<string>(StringComparer.Ordinal);
            var fresh = new List<QbClipTable.Entry>(pool.Count);
            for (int i = 0; i < pool.Count; i++) if (!used.Contains(pool[i].clip)) fresh.Add(pool[i]);
            if (fresh.Count == 0)
            {
                for (int i = 0; i < pool.Count; i++) used.Remove(pool[i].clip);
                fresh.AddRange(pool);
            }
            var e = fresh[((seed % fresh.Count) + fresh.Count) % fresh.Count];
            used.Add(e.clip); Drawn.Add(e.clip);
            return e;
        }

        /// <summary>Rows of the families that pass <paramref name="ok"/> and the quality floor.</summary>
        public static List<QbClipTable.Entry> Pool(QbClipTable tab, Func<QbClipTable.Entry, bool> ok, params string[] families)
        {
            var res = new List<QbClipTable.Entry>();
            if (tab == null) return res;
            foreach (string f in families)
            {
                var l = tab.Family(f);
                for (int i = 0; i < l.Count; i++) { var e = l[i]; if (e.quality >= MinQuality && (ok == null || ok(e))) res.Add(e); }
            }
            return res;
        }

        /// <summary>The rows whose native facing is within <paramref name="maxDeg"/> of <paramref name="refYaw"/> (consecutive windows that face alike cross-fade without the hips swinging round); the whole pool when none is.</summary>
        public static List<QbClipTable.Entry> Near(List<QbClipTable.Entry> pool, QbClipTable.Entry cur, float maxDeg)
        {
            if (cur == null) return pool;
            var r = pool.FindAll(e => Mathf.Abs(Mathf.DeltaAngle(e.yaw0, cur.yaw0)) <= maxDeg);
            return r.Count > 0 ? r : pool;
        }

        /// <summary>A small deterministic hash of the play and the slot (a draw is the same in a replay of the same play).</summary>
        public static int Seed(int play, int salt) { unchecked { int h = play * 73856093 ^ salt * 19349663; h ^= h >> 13; h *= 1274126177; return h & 0x7fffffff; } }
    }
}
