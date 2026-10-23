using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 QBSTANCE plans: the kicker's foot (E-300, E-301, E-306, E-311), the fake hand-off (E-082), the underhand toss (E-143), the kneel / spike timing (E-084).
    /// Probe of the mocap (QbStanceProbe): the kickoff and the field-goal takes kick with the RIGHT foot, the two BGS punt takes (BGS__P_punt_00 / _01) and the onside take with the LEFT. Every kicker kicks with the right foot,
    /// so those takes are swapped for the right-footed Yan punt or for their mirror image.
    /// </summary>
    public static partial class AnimKit
    {
        /// <summary>The kick plan of a kicker / punter: the existing timed plan, with the clip of a left-footed take replaced by a right-footed one.</summary>
        public static ClipPlan KickPlan(in AnimHints h)
        {
            ClipPlan plan;
            var l = Lib;
            if (h.Kind == PlayKind.Kickoff && h.KickKind == (int)KickKind.Onside && l != null && l.HasClip(ArtKeys.KickOnside))
            {
                // the dedicated onside take (a short hop-and-poke), mirrored to the right foot; falls back to the kickoff take when there is no mirror
                var oc = l.Clip(ArtKeys.KickOnside);
                var mir = ClipFacingTable.MirrorOf(oc);
                if (mir != null && oc.length < 9f) { plan = TimedKick(mir, h.EventEta); if (plan.Valid) return plan; }
            }
            plan = Kick(h.Kind, h.Seed, false, h.EventEta > 0f ? h.EventEta + KickEtaBias(h.Kind) : h.EventEta);      // v0.10.0 QB: the measured foot-contact frame of the take, not the table moment
            if (!plan.Valid) return plan;
            return RightFooted(plan);
        }

        /// <summary>The window of a kick take that lands its foot contact <paramref name="eta"/> seconds from now (the same logic as <see cref="Kick(PlayKind,int,bool,float)"/>).</summary>
        static ClipPlan TimedKick(AnimationClip c, float eta)
        {
            if (c == null) return default(ClipPlan);
            float tc = ClipTimingTable.Moment(c, 0.55f);
            if (eta <= 0f) return P(c, Mathf.Max(0f, tc - KickLead), Mathf.Min(c.length, tc + 1.2f), 1f, 0.16f, 0.4f);
            float start, speed = 1f;
            if (tc >= eta) start = tc - eta; else { start = 0f; speed = Mathf.Clamp(tc / eta, 0.5f, 1f); }
            return P(c, start, Mathf.Min(c.length, tc + 1.2f), speed, 0.16f, 0.4f);
        }

        /// <summary>True for the mocap takes that kick with the left foot.</summary>
        public static bool IsLeftFootedKick(AnimationClip c)
        {
            if (c == null) return false;
            string n = c.name;
            return n.StartsWith("BGS__P_punt") || n == "onside_kick_00";
        }

        /// <summary>Swaps a left-footed take for a right-footed one with the same contact moment (the window keeps its lead).</summary>
        static ClipPlan RightFooted(ClipPlan p)
        {
            var l = Lib;
            if (p.Clip == null || l == null || !IsLeftFootedKick(p.Clip)) return p;
            AnimationClip alt = null;
            if (p.Clip.name.StartsWith("BGS__P_punt")) alt = l.ClipByName("YAN__P_punt_00_remap");
            if (alt == null) alt = ClipFacingTable.MirrorOf(p.Clip);
            if (alt == null) return p;
            float tcOld = ClipTimingTable.Moment(p.Clip, 0.55f), tcNew = ClipTimingTable.Moment(alt, 0.55f);
            float lead = Mathf.Max(0.05f, tcOld - p.Start);
            float start = tcNew - lead; float speed = p.Speed;
            if (start < 0f) { speed = Mathf.Clamp(p.Speed * tcNew / (tcNew - start), 0.5f, p.Speed); start = 0f; }
            p.Clip = alt; p.Start = start; p.End = Mathf.Min(alt.length, tcNew + 1.2f); p.Speed = speed;
            return p;
        }
    }
}

namespace PixFootball.Gameplay
{
    public static partial class AnimKit
    {
        /// <summary>The long snapper's snap (E-254): the underhand long-snap take, released at its measured moment; empty when the take is not in the library (the caller keeps the quarterback throw).</summary>
        public static ClipPlan LongSnapPlan(in AnimHints h)
        {
            var l = Lib; if (l == null || !l.HasClip(ArtKeys.Snapper)) return default(ClipPlan);
            var c = l.ClipVariant(ArtKeys.Snapper, h.Seed);
            if (c == null || c.length > 4f) return default(ClipPlan);
            float tr = ClipTimingTable.Moment(c, 0.42f);
            return P(c, Mathf.Max(0f, tr - 0.22f), c.length, 1f, 0.12f, 0.3f);
        }
    }
}

namespace PixFootball.Gameplay
{
    public static partial class AnimKit
    {
        /// <summary>The holder's take with the placing hand on the kicker's side (E-062). The kicker stands to the LEFT of the ball (right-footed); the mocap holder places the ball with his right hand, so the take is the mirror image.</summary>
        public static AnimationClip HolderClip(int seed)
        {
            var l = Lib; if (l == null || l.ClipVariantCount(ArtKeys.KickHolder) == 0) return null;
            var c = l.ClipVariant(ArtKeys.KickHolder, seed);
            var m = ClipFacingTable.MirrorOf(c);
            return m != null ? m : c;
        }
    }
}

namespace PixFootball.Gameplay
{
    public static partial class AnimKit
    {
        /// <summary>v0.10.0 QB (note 7): seconds added to the time-to-contact the kick take is planned with, so the take's measured foot contact (the frame the instep meets the ball: foot speed peak and the ball gap in the audit's frame log) is the simulated contact frame.
        /// The punt take's table moment is 0.042 s later than its real contact (measured 3 frames late); the kickoff and field goal takes land within one frame.</summary>
        public static float KickEtaBias(PlayKind kind) { return kind == PlayKind.Punt ? 0.042f : (kind == PlayKind.Kickoff ? -0.0167f : (kind == PlayKind.FieldGoal ? 0.042f : 0f)); }
    }
}
