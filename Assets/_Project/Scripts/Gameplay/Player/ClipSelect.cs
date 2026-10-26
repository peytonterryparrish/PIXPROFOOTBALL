using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 ANIMLIB: the clip selection table for CHOREO and QB. Every query returns only clips whose QA score passed (rejected clips are never returned) and carries the tag row
    /// (sync moment, entry / exit foot, root speed and direction, positions). Empty results mean "key or table missing": callers fall back to their old pick.
    /// <code>
    /// var c = ClipSelect.Pick("qb_cadence_under", PlayerRole.QB, seed);                       // best clip of a key for a position
    /// var plan = ClipSelect.Synced(c, eta: 0.6f, lead: 0.25f);                              // the clip's sync moment (release / catch / contact) lands eta seconds from now
    /// var list = ClipSelect.Query("tackle_wrap", null, entryFoot: 'L', minScore: 60f);       // all candidates, filtered
    /// </code>
    /// </summary>
    public static class ClipSelect
    {
        public const float MinScore = 50f;

        static ArtLibrary Lib { get { return ArtLibrary.Load(); } }

        public static bool Has(string key) { var l = Lib; return l != null && l.ClipVariantCount(key) > 0; }

        /// <summary>The tag row of a clip, or null.</summary>
        public static ClipTagTable.Entry Tag(AnimationClip c)
        {
            var t = ClipTagTable.Load(); ClipTagTable.Entry e;
            return t != null && c != null && t.TryGet(c.name, out e) ? e : null;
        }

        static bool RoleOk(ClipTagTable.Entry e, string role)
        {
            if (role == null || string.IsNullOrEmpty(e.roles)) return true;
            return e.roles.Contains("ALL") || e.roles.Contains(role);
        }

        public static string RoleName(PlayerRole r) { return r == PlayerRole.K ? "K" : r.ToString(); }

        /// <summary>Candidates of a key: filtered by position (null = any), planted entry foot ('L' / 'R', '?' = any), maximum length (0 = any) and QA score.</summary>
        public static List<AnimationClip> Query(string key, PlayerRole? role = null, char entryFoot = '?', float maxLen = 0f, float minScore = MinScore)
        {
            var res = new List<AnimationClip>(); var l = Lib; if (l == null) return res;
            var tags = ClipTagTable.Load();
            int n = l.ClipVariantCount(key);
            for (int i = 0; i < n; i++)
            {
                var c = l.ClipVariant(key, i); if (c == null) continue;
                if (maxLen > 0f && c.length > maxLen) continue;
                ClipTagTable.Entry e;
                if (tags != null && tags.TryGet(c.name, out e))
                {
                    if (e.reject || e.score < minScore) continue;
                    if (role.HasValue && !RoleOk(e, RoleName(role.Value))) continue;
                    if (entryFoot != '?' && e.entryFoot != entryFoot && e.entryFoot != 'B') continue;
                }
                res.Add(c);
            }
            return res;
        }

        /// <summary>v0.13 (ANIMLIB-BULK): how far under the best QA score of a key a take may be and still be drawn (was 8). Every candidate already passed <see cref="Query"/> (not rejected, score >= <see cref="MinScore"/>).</summary>
        public static float Window = 20f;

        /// <summary>Pick among the best candidates (QA score within <see cref="Window"/> points of the best), least used first (<see cref="ClipBag"/>), ties by the seed. Null when the key has nothing usable.</summary>
        public static AnimationClip Pick(string key, PlayerRole? role, int seed, char entryFoot = '?', float maxLen = 0f)
        {
            var q = Query(key, role, entryFoot, maxLen);
            if (q.Count == 0 && entryFoot != '?') q = Query(key, role, '?', maxLen);
            if (q.Count == 0) return null;
            var tags = ClipTagTable.Load(); float best = 0f;
            var sc = new float[q.Count];
            for (int i = 0; i < q.Count; i++) { var e = Tag(q[i]); sc[i] = e != null ? e.score : 70f; best = Mathf.Max(best, sc[i]); }
            var pool = new List<AnimationClip>();
            bool stance = ClipBag.IsStancePool(key);      // v0.13 wave 2 (BULK-2): the pre-snap stance pools keep the old window (8) and the seed pick
            for (int i = 0; i < q.Count; i++) if (sc[i] >= best - (stance ? 8f : Window)) pool.Add(q[i]);
            if (stance) return pool[((seed % pool.Count) + pool.Count) % pool.Count];
            return ClipBag.Draw("cs:" + key + (role.HasValue ? RoleName(role.Value) : ""), seed, pool);
        }

        /// <summary>Best candidate whose root travel direction (degrees from the facing, + = right) is closest to dirDeg and whose speed is closest to speed (m/s).</summary>
        public static AnimationClip PickMoving(string key, PlayerRole? role, float dirDeg, float speed, int seed)
        {
            var q = Query(key, role); AnimationClip best = null; float bs = float.MaxValue;
            for (int i = 0; i < q.Count; i++)
            {
                var e = Tag(q[i]); if (e == null) continue;
                float s = Mathf.Abs(Mathf.DeltaAngle(e.rootDir, dirDeg)) * 0.02f + Mathf.Abs(e.rootSpeed - speed) * 0.5f + ((i * 7 + seed) % 5) * 0.01f;
                if (s < bs) { bs = s; best = q[i]; }
            }
            return best ?? Pick(key, role, seed);
        }

        /// <summary>The moment (seconds into the clip) the action of the clip happens: release / catch / contact / kick / snap. Falls back to a fraction of the length.</summary>
        public static float SyncTime(AnimationClip c, float fallbackFraction = 0.5f)
        {
            var e = Tag(c);
            if (e != null && e.syncT > 0.02f) return e.syncT;
            return ClipTimingTable.Moment(c, fallbackFraction);
        }

        /// <summary>A plan that starts the clip so its sync moment is <paramref name="eta"/> seconds from now (starts mid-clip when eta is short, slows down when it is long and the clip has no lead-in), keeps playing <paramref name="tail"/> seconds after it.</summary>
        public static ClipPlan Synced(AnimationClip c, float eta, float lead = 0.3f, float tail = 0.9f, float fadeIn = 0.14f, float fadeOut = 0.3f)
        {
            if (c == null) return default(ClipPlan);
            float t = SyncTime(c);
            float rate = GamePace.AnimationRate;
            float start = Mathf.Max(0f, t - Mathf.Max(eta, 0f) * rate);
            float speed = rate;
            if (t < eta * rate && eta > 0.05f) speed = Mathf.Clamp(t / eta, 0.6f, rate);     // no lead-in left: slow the clip so the moment still lands at eta
            return new ClipPlan { Clip = c, Start = start, End = Mathf.Min(c.length, t + tail), Speed = speed, FadeIn = fadeIn, FadeOut = fadeOut };
        }
    }
}
