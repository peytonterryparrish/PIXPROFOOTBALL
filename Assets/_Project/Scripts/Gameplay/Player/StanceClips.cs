using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>Turns a <see cref="StanceSpec"/> into the looping clip window that poses it (v0.6). The selection of the clip keys lives in <see cref="StanceRules.ClipKeys"/>.</summary>
    public static class StanceClips
    {
        public static ClipPlan Plan(StanceSpec spec, int seed, PlayerRole role = PlayerRole.QB)
        {
            var lib = ArtLibrary.Load();
            if (lib == null) return default(ClipPlan);
            string[] keys = StanceRules.ClipKeys(spec.Kind, role);
            AnimationClip c = null;
            for (int k = 0; k < keys.Length && c == null; k++)
                if (lib.ClipVariantCount(keys[k]) > 0) c = Pick(lib, keys[k], spec, seed);
            if (c == null) return default(ClipPlan);
            // the long studio loops start and end in a T-pose calibration (and wander in between): loop the middle of the take, the part the facing / foot / hand audit measured
            float a = 0f, b = 0f, start = 0f;
            float frac = (seed * 0.37f) % 1f; if (frac < 0f) frac += 1f;
            float phase;
            if (c.length > 8f) { a = c.length * 0.40f; b = c.length * 0.60f; start = a; phase = frac * (b - a); }
            else { start = 0f; phase = frac * c.length; }   // v0.8 (QA-655): the loop point stays at the window start, only the first frame is staggered
            return new ClipPlan { Clip = c, Start = start, End = b, Speed = 1f, FadeIn = 0.3f, FadeOut = 0.25f, Loop = true, Hold = false, Phase = phase };
        }

        /// <summary>Name fragments of the takes that really are this stance (the key pools also hold transitions and calibration takes).</summary>
        public static string[] Preferred(StanceKind kind)
        {
            switch (kind)
            {
                case StanceKind.LinebackerTwoPoint: return new[] { "LinebackerBlitzIdle" };
                case StanceKind.QbShotgun: return new[] { "shotgun_ready_to_catch" };
                case StanceKind.QbUnderCenter: return new[] { "undercenter_to_catch_loop" };
                case StanceKind.ReceiverSplit: case StanceKind.ReceiverSlot: case StanceKind.Gunner: case StanceKind.CornerPress: case StanceKind.CornerOff: case StanceKind.SafetyDeep: case StanceKind.KickoffRunner: case StanceKind.Returner: return new[] { "2pstance_safety" };
                case StanceKind.BackTwoPoint: return new[] { "2pstance_halfback" };
                default: return new string[0];
            }
        }

        static AnimationClip Pick(ArtLibrary lib, string key, StanceSpec spec, int seed)
        {
            int n = lib.ClipVariantCount(key);
            var table = ClipFacingTable.Load();
            string[] pref = Preferred(spec.Kind);
            AnimationClip best = null; int bestScore = int.MinValue;
            for (int i = 0; i < n; i++)
            {
                var variant = lib.ClipVariant(key, (seed + i) % n);
                // every authored stance also comes as its mirror image (the Humanoid mirror setting, built by ClipFacingAudit)
                for (int m = 0; m < 2; m++)
                {
                    var c = m == 0 ? variant : ClipFacingTable.MirrorOf(variant);
                    if (c == null) continue;
                    int score = Score(table, c, spec, pref) - (m == 1 ? 0 : 0);
                    if (score > bestScore) { bestScore = score; best = c; }
                }
            }
            return best;
        }

        static int Score(ClipFacingTable table, AnimationClip c, StanceSpec spec, string[] pref)
        {
            int score = 0;
            foreach (string p in pref) if (c.name.IndexOf(p, System.StringComparison.OrdinalIgnoreCase) >= 0) { score += 4; break; }
            ClipFacingTable.Entry e;
            if (table != null && table.TryGet(c.name, out e))
            {
                bool lineStance = spec.Kind == StanceKind.CenterSnap || spec.Kind == StanceKind.ThreePoint || spec.Kind == StanceKind.FourPoint;
                bool receiver = spec.Kind == StanceKind.ReceiverSplit || spec.Kind == StanceKind.ReceiverSlot || spec.Kind == StanceKind.Gunner;
                if (lineStance)
                {
                    // handDown: -1 left hand down, +1 right hand down; the centre keeps the ball hand (right)
                    int want = spec.Kind == StanceKind.CenterSnap ? 1 : (spec.Mirror ? -1 : 1);
                    if (e.handDown == want) score += 2; else if (e.handDown != 0) score -= 1;
                }
                else if (receiver)
                {
                    // footBack: -1 left foot back, +1 right foot back; the outside foot (the sideline side) is the back foot
                    int want = spec.IsLeftOfBall ? -1 : 1;
                    if (e.footBack == want) score += 2; else if (e.footBack != 0) score -= 1;
                }
                // a take that wanders (T-pose calibration, turns) is never a stance
                if (e.spread > ClipFacingTable.MaxSpread) score -= 3;
            }
            return score;
        }
    }
}
