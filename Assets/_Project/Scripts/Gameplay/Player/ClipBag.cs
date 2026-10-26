using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.13 (ANIMLIB-BULK): the no-repeat draw. Every clip pick of the game used to be "seed modulo pool" or "first best score", so a pool of 24 takes showed the same six of them all game
    /// (the owner counts the clips he sees: 364 distinct in a 168-play game, 9 of 461 quarterback takes). The bag hands out the LEAST USED candidate of a pool first and breaks the tie with the
    /// seed, so the whole pool is walked before any take comes back, and the same (pool, seed) always returns the same take again (both actors of a pair, every frame of a re-issued plan).
    /// Pure and deterministic for a given call order (the same game seed draws the same takes). <c>-pixNoClipBag</c> switches it off (the old seed pick).
    /// </summary>
    public static class ClipBag
    {
        public static bool Enabled = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoClipBag") < 0;

        static readonly Dictionary<string, int> _uses = new Dictionary<string, int>(StringComparer.Ordinal);
        static readonly Dictionary<long, string> _memo = new Dictionary<long, string>();
        static readonly Dictionary<long, int> _memoIdx = new Dictionary<long, int>();
        static readonly Dictionary<string, int> _idxUses = new Dictionary<string, int>(StringComparer.Ordinal);
        const int MemoCap = 40000;

        /// <summary>Forget the usage and the per-seed answers (a new match).</summary>
        public static void Reset() { _uses.Clear(); _memo.Clear(); _memoIdx.Clear(); _idxUses.Clear(); }

        /// <summary>Times a clip was handed out (tests, audits).</summary>
        public static int Uses(string clip) { int n; return clip != null && _uses.TryGetValue(clip, out n) ? n : 0; }

        static int Hash(string s)
        {
            unchecked { uint h = 2166136261u; for (int i = 0; i < s.Length; i++) h = (h ^ s[i]) * 16777619u; return (int)h; }
        }

        static long Memo(string pool, int seed) { return ((long)Hash(pool) << 32) ^ (uint)seed; }

        /// <summary>"x_b" is the second actor of "x": both share one pool so a pair keeps its A / B index.</summary>
        static string Base(string key) { return key != null && key.EndsWith("_b", StringComparison.Ordinal) ? key.Substring(0, key.Length - 2) : key; }

        static int Mix(int seed, int salt)
        {
            unchecked { uint h = (uint)(seed * 73856093) ^ (uint)(salt * 19349663); h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; return (int)(h & 0x7FFFFFFF); }
        }

        /// <summary>The take of <paramref name="cands"/> this (pool, seed) gets: the least used one, ties by the seed. Null for an empty list.</summary>
        public static AnimationClip Draw(string pool, int seed, IList<AnimationClip> cands)
        {
            if (cands == null || cands.Count == 0) return null;
            if (cands.Count == 1) return cands[0];
            if (!Enabled) return cands[((seed % cands.Count) + cands.Count) % cands.Count];
            long m = Memo(pool, seed); string name;
            if (_memo.TryGetValue(m, out name))
                for (int i = 0; i < cands.Count; i++) if (cands[i] != null && cands[i].name == name) return cands[i];
            int best = -1, bestUse = int.MaxValue, bestTie = int.MaxValue;
            for (int i = 0; i < cands.Count; i++)
            {
                var c = cands[i]; if (c == null) continue;
                int u; _uses.TryGetValue(c.name, out u);
                int tie = Mix(seed, Hash(c.name));
                if (u < bestUse || (u == bestUse && tie < bestTie)) { best = i; bestUse = u; bestTie = tie; }
            }
            if (best < 0) return null;
            var pick = cands[best];
            if (_memo.Count > MemoCap) _memo.Clear();
            _memo[m] = pick.name; _uses[pick.name] = bestUse + 1;
            return pick;
        }

        /// <summary>Index into a pool of <paramref name="n"/> variants of a library key (or any ordered list): the least used index, ties by the seed. "key_b" shares the pool and the answer of "key",
        /// so the A and B actors of a pair get the same index.</summary>
        public static int Index(string key, int seed, int n)
        {
            if (n <= 1) return 0;
            if (!Enabled) return ((seed % n) + n) % n;
            string pool = Base(key); long m = Memo(pool, seed); int idx;
            if (_memoIdx.TryGetValue(m, out idx)) return idx % n;
            int best = 0, bestUse = int.MaxValue, bestTie = int.MaxValue;
            for (int i = 0; i < n; i++)
            {
                int u; _idxUses.TryGetValue(pool + "#" + i, out u);
                int tie = Mix(seed, i + 1);
                if (u < bestUse || (u == bestUse && tie < bestTie)) { best = i; bestUse = u; bestTie = tie; }
            }
            if (_memoIdx.Count > MemoCap) _memoIdx.Clear();
            _memoIdx[m] = best; _idxUses[pool + "#" + best] = bestUse + 1;
            return best;
        }

        /// <summary>Keys that must keep their old seed pick: a seed of 0 means "the canonical take" in the callers, and the quarterback keys belong to the QB sequence.</summary>
        public static bool Skip(string key, int seed) { return seed == 0 || (key != null && (key.StartsWith("qb_", StringComparison.Ordinal) || key.StartsWith("pix_qb", StringComparison.Ordinal) || IsStancePool(key))); }

        /// <summary>v0.13 wave 2 (BULK-2): the pre-snap stance pools keep the seed pick. The no-repeat walk through them put stances with another facing / hand / foot into the line (standing FACE +12 %, CB +42 % in the wave-1 game); variety there comes from the pool itself, not from a rotation.</summary>
        public static bool IsStancePool(string key)
        {
            string k = Base(key);
            return k.StartsWith("stance_", StringComparison.Ordinal) && k != "stance_to_sprint" || k == "idle" || k == "idle_alt" || k == "dl_stance" || k == "ol_stance" || k.StartsWith("cb_idle", StringComparison.Ordinal);
        }
    }
}
