using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    public static partial class AnimKit
    {
        /// <summary>The hand-off pair both actors play: the A take chosen by hints (see <see cref="AnimHints.HandoffPick"/>), with its twin.</summary>
        public static bool TryHandoffPick(AnimHints h, out AnimationClip a, out AnimationClip b)
        {
            a = b = null;
            if (string.IsNullOrEmpty(h.HandoffPick)) return false;
            var l = Lib; if (l == null) return false;
            a = l.ClipByName(h.HandoffPick); if (a == null) return false;
            b = TwinOf("handoff_b", a);
            return b != null;
        }

        /// <summary>The hand-off pair whose two takes travel the way the two bodies really move (the quarterback barely, the back at a run) over the window that is played around the exchange.</summary>
        public static string PickHandoffPair(float qbSpeed, float backSpeed, int seed)
        {
            var l = Lib; if (l == null) return null;
            int n = l.ClipVariantCount("handoff"); string best = null; float bs = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var a = l.ClipVariant("handoff", i); if (a == null || a.length > 6f) continue;
                var b = TwinOf("handoff_b", a); if (b == null) continue;
                float ta = ClipTimingTable.Moment(a, 0.4f), tb = ClipTimingTable.Moment(b, 0.4f);
                float sa = RootSpeedOf(a, Mathf.Max(0f, ta - 0.15f), Mathf.Min(a.length, ta + 0.7f)), sb = RootSpeedOf(b, Mathf.Max(0f, tb - 0.15f), Mathf.Min(b.length, tb + 0.7f));
                float score = Mathf.Abs(sa - qbSpeed) + Mathf.Abs(sb - backSpeed) * 0.6f + (((seed * 73856093) ^ (i * 19349663)) & 0xFFFF) / 65535f * 0.25f;
                if (score < bs) { bs = score; best = a.name; }
            }
            return best;
        }

        /// <summary>Mean ground speed (m/s) a take wants over a window of its own time (from the measured root path; 0 when the clip is not measured).</summary>
        public static float RootSpeedOf(AnimationClip c, float t0, float t1)
        {
            var table = ChoreoTable.Load(); if (table == null || c == null || t1 <= t0) return 0f;
            return table.RootDelta(c.name, t0, t1).magnitude / (t1 - t0);
        }

        /// <summary>
        /// v0.10.0 CHOREO: the jump (a jump ball, a leap at the line) chosen by the speed the body really has: the take whose own root speed over its leaping window is nearest the simulated speed, so a standing jumper does not run in place and a running one does not stand.
        /// </summary>
        public static ClipPlan JumpMatched(AnimHints h)
        {
            var l = Lib; if (l == null) return default(ClipPlan);
            int n = l.ClipVariantCount("catch_jump"); if (n == 0) return default(ClipPlan);
            AnimationClip best = null; float bs = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var c = l.ClipVariant("catch_jump", i); if (c == null || c.length > 3f) continue;
                float sp = RootSpeedOf(c, 0.1f, Mathf.Min(1.3f, c.length));
                float score = Mathf.Abs(sp - h.Speed) + (((h.Seed * 73856093) ^ (i * 19349663)) & 0xFFFF) / 65535f * 0.3f;
                if (score < bs) { bs = score; best = c; }
            }
            if (best == null) return default(ClipPlan);
            return P(best, 0.1f, Mathf.Min(best.length, 1.3f), 1.15f, 0.1f, 0.28f);
        }

        /// <summary>
        /// v0.10.0 CHOREO: the catch (or interception / swat) the simulation's choreographer booked. The clip is the booked one, started at the clip time the booking is at (the hand-close frame lands on
        /// the ball) and played at the booking's rate; it owns the whole body only when the booking owns the body, otherwise it is the upper-body take over the runner's legs.
        /// </summary>
        public static ClipPlan CatchBooked(in AnimHints h)
        {
            var bk = h.Booked;
            if (bk == null || bk.ClipA == null) return default(ClipPlan);
            var l = Lib; var c = l != null ? l.ClipByName(bk.ClipA.Name) : null;
            if (c == null) return default(ClipPlan);
            float contact = bk.ClipA.ContactT;
            float start = Mathf.Clamp(h.BookedClipTime, 0f, Mathf.Max(0f, contact - 0.03f));
            float end = Mathf.Min(c.length, bk.ExitTA + 0.4f);
            var plan = P(c, start, Mathf.Max(end, start + 0.2f), bk.Rate, 0.10f, 0.3f);
            plan.UpperBody = !bk.OwnsA;
            return Floor(plan, ImpactFloor);
        }
    }
}
