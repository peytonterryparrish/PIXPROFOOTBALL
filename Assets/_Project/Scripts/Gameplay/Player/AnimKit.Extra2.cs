using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 ANIMLIB: wider clip pools for the variety slots (celebrations, reactions, idles, sideline) and plans for the newly imported Draft Fever takes (kickoff line idles,
    /// under-centre / shotgun cadences, the real centre snap, crowd, draft night, end-zone save). Every method returns an empty plan when the key is not in the library, so the
    /// callers fall back to what they did before. Selection goes through <see cref="ClipSelect"/> (QA-passed clips only).
    /// </summary>
    public static partial class AnimKit
    {
        static AnimationClip PoolPick(string[] keys, int seed, float maxLen)
        {
            // one key per call, rotated by the seed: each key is a pool of its own, so all of them get played
            int k0 = ((seed % keys.Length) + keys.Length) % keys.Length;
            for (int j = 0; j < keys.Length; j++)
            {
                var c = ClipSelect.Pick(keys[(k0 + j) % keys.Length], null, seed / keys.Length, '?', maxLen);
                if (c != null) return c;
            }
            return null;
        }

        static readonly string[] CelebrateKeys = { "celebrate_td", "celebrate_td_2", "celebrate_td_all", "celebrate_td_3" };
        static readonly string[] CheerKeys = { "cheer", "celebrate_big" };
        static readonly string[] FrustrationKeys = { "frustration", "react_frustration" };

        /// <summary>Touchdown celebration with the whole Motus + BoNES set (about 30 clips instead of 8).</summary>
        public static ClipPlan CelebrateVaried(int seed)
        {
            var c = PoolPick(CelebrateKeys, seed, 12f);
            return c == null ? default(ClipPlan) : P(c, 0f, 0f, 1f, 0.2f, 0.3f, c.isLooping, false);
        }

        /// <summary>Pool of a reaction key: "cheer" adds the big-play celebrations, "frustration" the Motus frustrations, "celebrate_td_3" the whole touchdown set. Null = no wider pool.</summary>
        static AnimationClip ReactionPool(string key, int seed)
        {
            string[] keys = null;
            if (key == "cheer") keys = CheerKeys; else if (key == "frustration") keys = FrustrationKeys; else if (key == "celebrate_td_3" || key == "celebrate_td_2") keys = CelebrateKeys;
            return keys == null ? null : PoolPick(keys, seed, 14f);
        }

        /// <summary>Sideline / huddle standing idles: lively fidgets, looking around, fixing the helmet, the generic idles.</summary>
        public static ClipPlan IdleVariantWide(int seed)
        {
            var c = PoolPick(new[] { "idle_lively", "idle_look", "idle_helmet", "idle_alt" }, seed, 0f);
            return c == null ? default(ClipPlan) : P(c, 0f, 0f, 1f, 0.4f, 0.4f, c.isLooping);
        }

        /// <summary>Kickoff line-up idles (stretching, helmet fix, head turn, pointing, taunt, look back ...): the kicking team waits on its line, the receiving team in its own.</summary>
        public static ClipPlan KickoffLineIdle(bool kickingTeam, int seed)
        {
            var c = ClipSelect.Pick(kickingTeam ? "kickoff_att_idle" : "kickoff_def_idle", null, seed);
            return c == null ? default(ClipPlan) : P(c, 0f, 0f, 1f, 0.3f, 0.3f, c.isLooping);
        }

        /// <summary>The run-up out of the line-up idle (receiving team): idle to run.</summary>
        public static ClipPlan KickoffRunUp(int seed)
        {
            var c = ClipSelect.Pick("kickoff_def_run", null, seed);
            return c == null ? default(ClipPlan) : P(c, 0f, Mathf.Min(c.length, 1.6f), 1f, 0.2f, 0.4f);
        }

        /// <summary>Quarterback cadence by formation: the under-centre and shotgun cadence takes of the studio (scrim_QBUndrCtr / scrim_QBShotgun), falls back to <see cref="Cadence(int)"/>.</summary>
        public static ClipPlan Cadence(int seed, bool underCenter)
        {
            var c = ClipSelect.Pick(underCenter ? "qb_cadence_under" : "qb_cadence_shotgun", PlayerRole.QB, seed, '?', 9f);
            if (c == null) return Cadence(seed);
            return P(c, 0f, 0f, 1f, 0.3f, 0.3f);
        }

        /// <summary>The centre's snap out of his down stance (C_underCenterSnap_00 mocap).</summary>
        public static ClipPlan CenterSnap(int seed)
        {
            var c = ClipSelect.Pick("center_snap", null, seed, '?', 4f);
            return c == null ? default(ClipPlan) : P(c, 0f, 0f, 1f, 0.12f, 0.3f);
        }

        /// <summary>Four-point defensive line get-off.</summary>
        public static ClipPlan FourPointGetOff(int seed)
        {
            var c = ClipSelect.Pick("dl_getoff_4pt", PlayerRole.DL, seed, '?', 3f);
            return c == null ? default(ClipPlan) : P(c, 0f, Mathf.Min(c.length, 1.5f), 1f, 0.12f, 0.4f);
        }

        /// <summary>Quarterback pocket movement after the drop: the walk back and the settle (3 / 5 / 7 step takes); steps picks the matching one when present.</summary>
        public static ClipPlan QbDropSettle(int steps, int seed)
        {
            var q = ClipSelect.Query("qb_drop_settle", PlayerRole.QB, '?', 4f);
            AnimationClip pick = null;
            foreach (var c in q) if (c.name.Contains("_" + steps + "Step")) { pick = c; break; }
            if (pick == null && q.Count > 0) pick = q[((seed % q.Count) + q.Count) % q.Count];
            return pick == null ? default(ClipPlan) : P(pick, 0f, 0f, 1f, 0.2f, 0.3f);
        }

        /// <summary>The huddle stand: the authored huddle take for two of three players, the lively / look-around idles for the rest (seeded per player).</summary>
        public static AnimationClip HuddleClip(int seed)
        {
            if (((seed % 3) + 3) % 3 != 2) return ClipSelect.Pick("huddle_idle", null, seed);
            return PoolPick(new[] { "idle_lively", "idle_look" }, seed, 0f);
        }

        /// <summary>Sideline coach idle: headset and clipboard stay; the third coach of every team plays the studio's coach ambient takes (loop-closed segments), a different one per team.</summary>
        public static AnimationClip CoachIdle(string role, int team, AnimationClip fallback)
        {
            if (role != "C2") return fallback;
            var c = ClipSelect.Pick("coach_ambient", null, team * 3 + 1, '?', 12f);
            return c != null && c.isLooping ? c : fallback;
        }

        /// <summary>Sideline player idle: seated on the bench, or standing (generic idles, helmet, fidgets).</summary>
        public static ClipPlan SidelineIdle(int seed, bool seated)
        {
            if (seated)
            {
                var b = ClipSelect.Pick("bench_sit", null, seed);
                if (b != null) return P(b, 0f, 0f, 1f, 0.4f, 0.4f, true);
            }
            return IdleVariantWide(seed);
        }

        /// <summary>Crowd member (Male Crowd rig clips): standing or seated, happy or sad.</summary>
        public static ClipPlan CrowdIdle(int seed, bool seated, bool happy)
        {
            var c = ClipSelect.Pick("crowd_" + (seated ? "sit_" : "stand_") + (happy ? "happy" : "sad"), null, seed);
            return c == null ? default(ClipPlan) : P(c, 0f, 0f, 1f, 0.4f, 0.4f, true);
        }

        /// <summary>Draft night (two-actor takes: stage handshake / hug) and the end-zone save pair: A actor in <paramref name="a"/>, B actor in <paramref name="b"/>. False when there is none.</summary>
        public static bool DraftNightPair(int seed, out AnimationClip a, out AnimationClip b) { return TryPair("draft_night", seed, 40f, out a, out b); }
        public static bool EndzoneSavePair(int seed, out AnimationClip a, out AnimationClip b) { return TryPair("endzone_save", seed, 40f, out a, out b); }
    }
}
