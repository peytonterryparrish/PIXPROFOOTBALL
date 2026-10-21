using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    public enum PlayerRole { QB, RB, WR, TE, OL, DL, LB, DB, K }

    public static class RoleUtil
    {
        public static PlayerRole Of(Position p)
        {
            switch (p)
            {
                case Position.QB: return PlayerRole.QB;
                case Position.RB: case Position.FB: return PlayerRole.RB;
                case Position.WR: return PlayerRole.WR;
                case Position.TE: return PlayerRole.TE;
                case Position.LT: case Position.LG: case Position.C: case Position.RG: case Position.RT: return PlayerRole.OL;
                case Position.LE: case Position.DT: case Position.RE: return PlayerRole.DL;
                case Position.LOLB: case Position.MLB: case Position.ROLB: return PlayerRole.LB;
                case Position.CB: case Position.FS: case Position.SS: return PlayerRole.DB;
                default: return PlayerRole.K;
            }
        }

        /// <summary>Body mesh by position: linemen large, backers / tight ends / backs / quarterbacks medium, everyone else small.</summary>
        public static PlayerSize SizeOf(Position p)
        {
            switch (Of(p))
            {
                case PlayerRole.OL: case PlayerRole.DL: return PlayerSize.Large;
                case PlayerRole.LB: case PlayerRole.TE: case PlayerRole.RB: case PlayerRole.QB: return PlayerSize.Medium;
                default: return PlayerSize.Small;
            }
        }
    }

    /// <summary>Situational facts the Unity layer derives from the simulation for picking animations.</summary>
    public struct AnimHints
    {
        public int Seed;
        public float ThrowDistance, ThrowAngle;      // yards, degrees (+ = to the passer's right)
        public bool Lob;
        public float BallHeight;                     // metres
        public Vector3 BallLocalDir;                 // incoming ball direction in the receiver's local frame (x right, z forward)
        public bool Airborne;
        public TackleType Tackle;
        public bool Approach_Back;                   // tackler comes from behind the carrier
        public char Side;                            // 'C', 'L' or 'R' (tackler side seen from the carrier)
        public bool Sack;
        public bool Defender;
        public RushMove Rush;
        public bool Partner;                         // this player is the second actor of a paired clip
        public bool ShotgunQb;
        public bool Pitch;
    }

    public struct ClipPlan
    {
        public AnimationClip Clip;
        public float Start, End, Speed, FadeIn, FadeOut;
        public bool Loop, Hold;
        public bool Valid { get { return Clip != null; } }
    }

    /// <summary>Maps simulation actions onto ArtLibrary mocap clips (see Docs/ARCHITECTURE.md, v0.2 animation mapping).</summary>
    public static class AnimKit
    {
        static ArtLibrary Lib { get { return ArtLibrary.Load(); } }

        static ClipPlan P(AnimationClip c, float start = 0f, float end = 0f, float speed = 1f, float fadeIn = 0.12f, float fadeOut = 0.18f, bool loop = false, bool hold = false)
        {
            return new ClipPlan { Clip = c, Start = start, End = end, Speed = speed, FadeIn = fadeIn, FadeOut = fadeOut, Loop = loop, Hold = hold };
        }

        static AnimationClip V(string key, int seed) { var l = Lib; return l != null ? l.ClipVariant(key, seed) : null; }

        /// <summary>Variant of a key whose clip name contains every token (best score); falls back to the seeded variant.</summary>
        static AnimationClip Pick(string key, int seed, params string[] tokens)
        {
            var l = Lib; if (l == null) return null;
            int n = l.ClipVariantCount(key); if (n == 0) return null;
            AnimationClip best = null; int bs = -1;
            for (int i = 0; i < n; i++)
            {
                var c = l.ClipVariant(key, i);
                int s = 0;
                foreach (string t in tokens) if (c.name.Contains(t)) s++;
                if (s > bs) { bs = s; best = c; }
            }
            return bs > 0 ? best : l.ClipVariant(key, seed);
        }

        static AnimationClip Partner(string key, AnimationClip a, int seed)
        {
            var l = Lib; if (l == null || a == null) return null;
            string want = a.name.EndsWith("_A") ? a.name.Substring(0, a.name.Length - 2) + "_B" : a.name;
            int n = l.ClipVariantCount(key);
            for (int i = 0; i < n; i++) { var c = l.ClipVariant(key, i); if (c.name == want) return c; }
            return l.ClipVariant(key, seed);
        }

        // =========================================================================================================
        public static ClipPlan Stance(PlayerRole role, bool shotgunQb, int seed, bool specialTeams)
        {
            if (specialTeams) return default(ClipPlan);
            AnimationClip c = null;
            switch (role)
            {
                case PlayerRole.QB: c = shotgunQb ? V("qb_stance_shotgun", 0) : V("qb_stance_under", 0); break;
                case PlayerRole.OL: c = V("dl_stance", seed); break;      // Motus 3-point stance loop (the BoNES 3pstance takes are mostly T-pose calibration)
                case PlayerRole.TE: c = V("dl_stance", seed + 1); break;
                case PlayerRole.DL: c = V("dl_stance", seed); break;
                case PlayerRole.LB: c = V("idle_alt", seed); break;
                case PlayerRole.DB: c = V("stance_safety", 0); break;
                case PlayerRole.RB: c = V("stance_halfback", 0); break;
                case PlayerRole.WR: c = V("stance_2pt", 0); break;
                default: return default(ClipPlan);
            }
            if (c == null) return default(ClipPlan);
            // the long studio loops start and end in a T-pose calibration: loop only the middle of the take
            float a = c.length > 8f ? 2.8f : 0f, b = c.length > 8f ? c.length - 2.8f : 0f;
            // stagger the loops so the line does not breathe in unison
            float start = a + ((seed * 0.37f) % 1f) * Mathf.Min((c.length - 2f * a) * 0.5f, 8f);
            return P(c, start, b, 1f, 0.2f, 0.12f, true);
        }

        public static ClipPlan Block(int seed)
        {
            var c = V("ol_pass_set", seed % 5);
            return c == null ? default(ClipPlan) : P(c, 0f, 0f, 1f, 0.15f, 0.15f, true);
        }

        /// <summary>Engaged blockers: the offense plays the A actor, the defender the B actor of the same take.</summary>
        public static ClipPlan Engaged(AnimHints h)
        {
            string key = "ol_pass_set";
            if (h.Defender)
            {
                switch (h.Rush)
                {
                    case RushMove.Bull: key = "dl_bull_rush"; break;
                    case RushMove.Swim: case RushMove.Rip: case RushMove.Speed: key = "dl_rush_swim"; break;
                    case RushMove.Spin: key = "dl_rush_spin"; break;
                }
            }
            else
            {
                switch (h.Rush)
                {
                    case RushMove.Bull: key = "dl_bull_rush"; break;
                    case RushMove.Swim: case RushMove.Rip: case RushMove.Speed: key = "dl_rush_swim"; break;
                    case RushMove.Spin: key = "dl_rush_spin"; break;
                }
            }
            var main = V(key, h.Seed);
            if (main == null) return default(ClipPlan);
            // short Motus takes only: the long BoNES packs are far too slow for a live block
            if (main.length > 4f) main = V(key, 0);
            bool pairedKey = key.StartsWith("dl_");
            // for the dl_* keys the A actor is the defender; for ol_* keys the A actor is the blocker
            bool playB = pairedKey ? !h.Defender : h.Defender;
            AnimationClip c = playB ? Partner(key + "_b", main, h.Seed) : main;
            if (c == null) c = main;
            return P(c, 0f, Mathf.Min(c.length, 3.2f), 1f, 0.15f, 0.2f, true);
        }

        public static ClipPlan Shed(int seed)
        {
            var c = V("dl_shed", seed);
            return c == null ? default(ClipPlan) : P(c, 0.2f, Mathf.Min(c.length, 2.6f), 1.5f, 0.1f, 0.25f);
        }

        public static ClipPlan Throw(AnimHints h)
        {
            string key; AnimationClip c;
            float d = h.ThrowDistance;
            string ang = h.ThrowAngle > 50f ? "90degR" : (h.ThrowAngle < -50f ? "90degL" : "0deg");
            if (h.Pitch) { c = Pick("qb_throw_lob_short", h.Seed, ang); }
            else if (h.Lob || d > 30f) { c = Pick("qb_throw_lob_long", h.Seed, ang, "QuarterbackPassLong"); }
            else if (d > 14f) { c = Pick("qb_throw_bullet", h.Seed, ang, "Long"); }
            else { c = Pick("qb_throw_bullet", h.Seed, ang, "Short"); }
            key = null;
            if (c == null || c.length > 6f) c = V("qb_throw_bullet", 0);
            if (c == null) return default(ClipPlan);
            float rel = ClipTimingTable.Moment(c, 0.45f);
            const float windup = 0.26f;
            float speed = 1.1f;
            float start = Mathf.Max(0f, rel - windup * speed);
            return P(c, start, Mathf.Min(c.length, rel + 0.9f), speed, 0.07f, 0.25f);
        }

        public static ClipPlan Handoff(AnimHints h)
        {
            var a = V("handoff", h.Seed);
            if (a == null) return default(ClipPlan);
            var c = h.Partner ? Partner("handoff_b", a, h.Seed) : a;
            if (c == null) c = a;
            float t = ClipTimingTable.Moment(c, 0.4f);
            return P(c, Mathf.Max(0f, t - 0.3f), Mathf.Min(c.length, t + 0.7f), 1f, 0.08f, 0.25f);
        }

        public static ClipPlan Catch(AnimHints h)
        {
            string dir = h.BallLocalDir.z > 0.35f ? "Back" : (h.BallLocalDir.z < -0.35f ? "Front" : "Right");
            // "Back" clips: the ball arrives from behind the receiver (he is running away from the passer)
            string side = Mathf.Abs(h.BallLocalDir.x) < 0.3f ? "C" : (h.BallLocalDir.x > 0f ? "R" : "L");
            string height = h.Airborne ? "Jump" : (h.BallHeight >= 1.75f ? "High" : (h.BallHeight <= 0.9f ? "Low" : "Mid"));
            string tail = dir + side + height;
            AnimationClip c = null;
            string[] keys = h.Defender ? new[] { "interception" } : new[] { "catch_high", "catch_run", "catch_low", "catch_jump", "catch_side" };
            int bs = -1;
            foreach (string k in keys)
            {
                var l = Lib; if (l == null) break;
                for (int i = 0; i < l.ClipVariantCount(k); i++)
                {
                    var cl = l.ClipVariant(k, i);
                    if (cl.length > 3f) continue;
                    int s = 0;
                    if (cl.name.EndsWith(tail)) s += 6;
                    if (cl.name.Contains(dir)) s += 3;
                    if (cl.name.EndsWith(height)) s += 2;
                    if (cl.name.Contains(dir + side)) s += 1;
                    if (s > bs) { bs = s; c = cl; }
                }
            }
            if (c == null) return default(ClipPlan);
            float t = ClipTimingTable.Moment(c, 0.35f);
            return P(c, Mathf.Max(0f, t - 0.14f), Mathf.Min(c.length, t + 0.85f), 1f, 0.06f, 0.25f);
        }

        static readonly System.Collections.Generic.List<MoveTakeTable.Take> _spinTakes = new System.Collections.Generic.List<MoveTakeTable.Take>();

        /// <summary>The ball carrier's spin: a window of the BoNES spin / dodge takes (found by the move take builder), sped up to fit the simulation's spin. Empty plan = use the procedural spin.</summary>
        public static ClipPlan Spin(int seed)
        {
            var t = MoveTakeTable.Load(); var l = Lib;
            if (t == null || l == null) return default(ClipPlan);
            t.Of(ArtKeys.Spin, _spinTakes);
            if (_spinTakes.Count == 0) return default(ClipPlan);
            var take = _spinTakes[((seed % _spinTakes.Count) + _spinTakes.Count) % _spinTakes.Count];
            var clip = l.ClipByName(take.clip);
            if (clip == null) return default(ClipPlan);
            float dur = Mathf.Max(0.3f, take.t1 - take.t0);
            float speed = Mathf.Clamp(dur / 0.5f, 1f, 1.8f);
            return P(clip, Mathf.Max(0f, take.t0 - 0.18f * speed), Mathf.Min(clip.length, take.t1 + 0.3f * speed), speed, 0.06f, 0.2f);
        }

        public static ClipPlan Jump(int seed)
        {
            var c = V("catch_jump", seed);
            if (c == null) return default(ClipPlan);
            return P(c, 0.1f, Mathf.Min(c.length, 1.3f), 1.15f, 0.06f, 0.2f);
        }

        public static string TackleKey(TackleType t)
        {
            switch (t)
            {
                case TackleType.BigHit: return "tackle_success";
                case TackleType.DiveTackle: case TackleType.Shoestring: return "tackle_dive";
                case TackleType.BrokenTackle: return "tackle_arm";
                default: return "tackle_wrap";
            }
        }

        /// <summary>Both actors of a tackle: tackler = A take, carrier (Partner) = the matching B take, aligned by variant name.</summary>
        public static ClipPlan Tackle(AnimHints h, bool carrier)
        {
            string key = h.Sack ? "qb_sack" : TackleKey(h.Tackle);
            string approach = h.Approach_Back ? "Back" : "Front";
            string tail = approach + h.Side;
            var l = Lib; if (l == null) return default(ClipPlan);
            AnimationClip a = null; int bs = -1;
            for (int i = 0; i < l.ClipVariantCount(key); i++)
            {
                var cl = l.ClipVariant(key, i);
                if (cl.length > 8.5f) continue;
                int s = 0;
                if (cl.name.Contains(approach)) s += 3;
                if (cl.name.Contains(tail)) s += 2;
                if (key == "tackle_success") s = 1;
                // keep variants stable per seed among equal scores
                if (s > bs || (s == bs && (h.Seed & 1) == 1)) { bs = s; a = cl; }
            }
            if (a == null) return default(ClipPlan);
            var c = carrier ? Partner(key + "_b", a, h.Seed) : a;
            if (c == null) c = a;
            bool big = key == "tackle_success";
            float start = key == "tackle_wrap" || key == "qb_sack" ? 0.55f : (key == "tackle_dive" ? 0.5f : 0f);
            return P(c, start, Mathf.Min(c.length, 5.2f), 1f, 0.06f, 0.3f, false, false);
        }

        public static ClipPlan Fall(int seed)
        {
            var c = Pick("fall", seed, "Stand");
            return c == null ? default(ClipPlan) : P(c, 0f, Mathf.Min(c.length, 2.0f), 1f, 0.08f, 0.3f, false, true);
        }

        public static ClipPlan Celebrate(int seed)
        {
            var c = (seed & 1) == 0 ? V("celebrate_td_2", seed) : V("celebrate_td_3", seed);
            if (c == null || c.length > 12f) c = V("celebrate_td_2", 0);
            return c == null ? default(ClipPlan) : P(c, 0f, 0f, 1f, 0.2f, 0.3f, c.isLooping, false);
        }
    }
}
