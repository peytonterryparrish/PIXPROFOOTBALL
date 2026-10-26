using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.11.0 SIM-FLOW: clip plans for the dead-ball flow between plays (huddle leader, break clap, first-down point, frustration, sideline standing, injury). Empty plan = key missing.</summary>
    public static partial class AnimKit
    {
        /// <summary>The quarterback calling the play in the huddle (looped).</summary>
        public static ClipPlan HuddleLeaderPlan(int seed)
        {
            var c = ClipSelect.Pick("huddle_leader", null, seed);
            if (c == null) return HuddlePlan(seed);
            return P(c, 0f, 0f, 1f, 0.3f, 0.3f, true);
        }

        /// <summary>The unison clap at the break (one shot, upper body so the legs can start walking).</summary>
        public static ClipPlan HuddleBreakClap(int seed)
        {
            var c = ClipSelect.Pick("huddle_break", null, seed);
            if (c == null) return default(ClipPlan);
            var p = P(c, 0f, Mathf.Min(c.length, 1.4f), 1.15f, 0.12f, 0.25f);
            p.UpperBody = true; p.MaskSpeed = 1.2f;
            return p;
        }

        /// <summary>The first-down point: the referee's arm-forward signal played on a player's upper body.</summary>
        public static ClipPlan FirstDownPoint(int seed)
        {
            var l = Lib; if (l == null) return default(ClipPlan);
            int n = l.ClipVariantCount("ref_signal_first_down"); if (n == 0) return default(ClipPlan);
            var c = l.ClipVariant("ref_signal_first_down", seed);
            if (c == null) return default(ClipPlan);
            var p = P(c, 0f, Mathf.Min(c.length, 2.0f), 1.0f, 0.2f, 0.3f);
            p.UpperBody = true; p.MaskSpeed = 1.5f;
            return p;
        }

        /// <summary>A player lying injured (loops while the trainers come), and the stand-up after it.</summary>
        public static ClipPlan InjuryDown(int seed)
        {
            var c = ClipSelect.Pick("injury", null, seed);
            if (c == null) return default(ClipPlan);
            return P(c, 0f, Mathf.Min(c.length, 4.5f), 0.9f, 0.25f, 0.4f, false, true);
        }

        /// <summary>Standing on the sideline (idles, helmet, fidgets).</summary>
        public static ClipPlan SidelineStand(int seed) { return SidelineIdle(seed, false); }

        /// <summary>A short conversation idle for two players.</summary>
        public static ClipPlan ConversationIdle(int seed)
        {
            var c = PickEither("idle_conversation", seed, 0f);      // v0.13 wave 2: + the second speakers
            return c == null ? default(ClipPlan) : P(c, 0f, 0f, 1f, 0.3f, 0.3f, true);
        }
    }
}
