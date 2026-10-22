using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.7.0 Track A plans: the real quarterback (pocket idle, distance-matched drop-backs, throws cut around their detected release, pump fake) and event-timed catches / hand-offs.
    /// The clips are the derived ones of <c>AnimQbBuilder</c> (keys pixqb_*) with their release / exchange moments in the timing table; every plan is empty when its clips are not in the library so callers fall back to the v0.8 plans.
    /// </summary>
    public static partial class AnimKit
    {
        public const string PocketClipName = "BONES_QB_shotgun_ready_to_catch_loop_004";

        static bool Entry(AnimationClip c, out ClipTimingTable.Entry e) { return ClipTimingTable.TryEntry(c, out e); }

        /// <summary>The quarterback standing in the pocket with the ball at his chest: the real loop of the shotgun ready position (hands up, knees bent, small weight shifts). The chest carriage IK keeps the ball high on top of it.</summary>
        public static ClipPlan QbPocket(int seed)
        {
            var l = Lib; if (l == null) return default(ClipPlan);
            var c = l.ClipByName(PocketClipName);
            if (c == null) c = l.ClipVariantCount(ArtKeys.QbStanceShotgun) > 0 ? l.ClipVariant(ArtKeys.QbStanceShotgun, 1) : null;
            if (c == null) return default(ClipPlan);
            float frac = (seed * 0.37f) % 1f; if (frac < 0f) frac += 1f;
            return new ClipPlan { Clip = c, Start = 0f, End = 0f, Speed = 1f, FadeIn = 0.3f, FadeOut = 0.3f, Loop = true, Phase = frac * c.length };
        }

        /// <summary>A drop-back take for the play's number of steps (under centre: the catch-the-snap-and-drop takes; shotgun: the BGS drop-into-stop takes). <paramref name="ta"/>..<paramref name="tb"/> is the part of the clip that travels, <paramref name="distance"/> the metres it covers.</summary>
        public static bool QbDrop(AnimHints h, bool shotgun, out ClipPlan plan, out float ta, out float tb, out float distance)
        {
            plan = default(ClipPlan); ta = tb = distance = 0f;
            var l = Lib; if (l == null) return false;
            string key = h.DropSteps <= 3 ? ArtKeys.QbDrop3 : (h.DropSteps <= 5 ? ArtKeys.QbDrop5 : ArtKeys.QbDrop7);
            int n = l.ClipVariantCount(key); if (n == 0) return false;
            AnimationClip best = null; float bestScore = float.MinValue; ClipTimingTable.Entry be = null;
            for (int i = 0; i < n; i++)
            {
                var c = l.ClipVariant(key, (h.Seed + i) % n);
                ClipTimingTable.Entry e; if (!Entry(c, out e) || e.d < 0.8f) continue;
                bool underClip = c.name.StartsWith("QB_undercenter");
                float score = (underClip == !shotgun ? 10f : 0f) - Mathf.Abs(e.d - h.DropRemaining) * 1.5f;
                if (score > bestScore) { bestScore = score; best = c; be = e; }
            }
            if (best == null) return false;
            ta = be.t; tb = be.t2; distance = be.d;
            plan = P(best, 0f, Mathf.Min(best.length, tb + 0.5f), 1f, 0.2f, 0.3f);
            return true;
        }

        /// <summary>The quarterback's throw: the derived take whose release is at the simulation's release (the wind-up is stretched / squeezed to the simulated wind-up time). Throw-away: the release is now. Off a scramble: the upper body only over the run.</summary>
        public static ClipPlan QbThrow(AnimHints h)
        {
            var l = Lib; if (l == null) return default(ClipPlan);
            string key;
            if (h.BallReleased && !h.Pitch) key = ArtKeys.PixQbThrowAway;
            else if (h.PrevScramble || h.Speed > 3.0f) key = ArtKeys.PixQbThrowRun;
            else if (Mathf.Abs(h.ThrowAngle) > 75f) key = ArtKeys.PixQbThrowOff;
            else key = ArtKeys.PixQbThrowSet;
            int n = l.ClipVariantCount(key);
            if (n == 0 && key == ArtKeys.PixQbThrowOff) { key = ArtKeys.PixQbThrowRun; n = l.ClipVariantCount(key); }      // v0.9.0 (E-106): no off-balance take in the library: the hurried, moving take is the nearest
            if (n == 0 && key != ArtKeys.PixQbThrowSet) { key = ArtKeys.PixQbThrowSet; n = l.ClipVariantCount(key); }
            if (n == 0) return default(ClipPlan);
            string want = key == ArtKeys.PixQbThrowSet ? (h.Lob || h.ThrowDistance > 30f ? "lob" : (h.ThrowDistance > 15f ? "bullet" : "touch")) : "";
            AnimationClip best = null; ClipTimingTable.Entry be = null; int bestScore = -1;
            for (int i = 0; i < n; i++)
            {
                var c = l.ClipVariant(key, h.Seed + i);
                ClipTimingTable.Entry e; if (!Entry(c, out e) || e.t <= 0.2f) continue;
                int score = want.Length > 0 && c.name.ToLowerInvariant().Contains(want) ? 2 : 0;
                if (best == null || score > bestScore) { best = c; be = e; bestScore = score; }
            }
            if (best == null) return default(ClipPlan);
            float tr = be.t, ts = be.t2;
            float start = Mathf.Max(0f, ts - 0.10f), speed = 1f;
            if (h.BallReleased) start = Mathf.Max(0f, tr - 0.26f);      // v0.9.0 (E-114): the take is under way before the ball is gone (the fade-in floor is 0.22 s)
            else
            {
                // v0.9.0 (E-105): the clip speed stays in 0.9 to 1.45 of the take (the lead is cut from the front instead of racing the whole wind-up)
                float w = Mathf.Max(0.15f, h.WindupSeconds);
                speed = Mathf.Clamp((tr - start) / w, 0.9f, 1.45f);
                start = Mathf.Max(start, tr - speed * w);
            }
            if (h.LeftHanded) { var mir = PixFootball.Art.ClipFacingTable.MirrorOf(best); if (mir != null) best = mir; }      // v0.9.0 (E-107): the mirror image for a left-handed passer (same key moments)
            var p = P(best, start, Mathf.Min(best.length, tr + 0.95f), speed, 0.14f, 0.3f);
            if (key == ArtKeys.PixQbThrowRun) p = Up(p);
            return p;
        }

        /// <summary>A pump fake while he holds the ball in the pocket (BGS pump-fake take, shoulders and arms only so the feet stay set).</summary>
        public static ClipPlan QbPump(int seed)
        {
            var l = Lib; if (l == null) return default(ClipPlan);
            var c = l.ClipByName("BGS__QB_Pass_PumpFake");
            if (c == null) return default(ClipPlan);
            return Up(P(c, 0f, Mathf.Min(c.length, 2.5f), 1f, 0.25f, 0.3f));
        }

        /// <summary>The key moment of an event take, plus the lead the plan needs so that it lands on the event (catch / exchange).</summary>
        static float Lead(float eta, float fallback) { return eta >= 0f ? Mathf.Clamp(eta, 0.05f, 0.6f) : fallback; }
    }
}
