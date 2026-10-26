using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.8 animation pass: kicking mocap (Yan "QB-Kickers-Rework"), pre-snap clips, stops / cuts / turns, huddle and sideline idles. All plans are empty when the clip is not in the library, so callers fall back.</summary>
    public static partial class AnimKit
    {
        /// <summary>Foot-contact moment minus this is where the kick window starts: last plant step + swing.</summary>
        public const float KickLead = 0.5f;

        /// <summary>The kick of the kicker / punter. The window is trimmed around the foot contact (ClipTimingTable moment measured by ClipTimingBuilder).</summary>
        /// <summary>Contact frame (seconds into the clip) of the photoreal biped place-kick take (`kick_off_biped`, measured 1.27 s: the kicking foot peaks at 14 m/s). The simulation's kickoff approach is 1.27 / GamePace.AnimationRate long, so the clip played at that rate lands its contact on the ball.</summary>
        public const float BipedKickContact = 1.27f;

        /// <summary>v0.7.0 (note 30): the kick clip starts at the simulation's approach and makes contact <paramref name="eta"/> seconds later (PlaySimulation.KickContactTime - Time): the clip is slowed (contact later than the clip's own lead) or started part-way (earlier) so its foot contact is the ball launch.</summary>
        public static ClipPlan Kick(PlayKind kind, int seed, bool fast, float eta)
        {
            if (eta <= 0f) return Kick(kind, seed, fast);
            var l = Lib; AnimationClip c = null; float tc = 0f;
            if (kind == PlayKind.Kickoff && l != null && l.HasClip("kick_off_biped")) { c = l.Clip("kick_off_biped"); tc = BipedKickContact; }
            if (c == null)
            {
                string key = kind == PlayKind.Punt ? (fast && l != null && l.HasClip(ArtKeys.PuntFast) ? ArtKeys.PuntFast : ArtKeys.Punt) : kind == PlayKind.FieldGoal ? ArtKeys.KickFieldGoal : ArtKeys.KickOff;
                c = V(key, seed);
                if (c == null || c.length > 9f) return default(ClipPlan);
                tc = ClipTimingTable.Moment(c, 0.55f);
                WarnUnmeasured(c);      // v0.9.0 CONNECT E-388
            }
            float start, speed = 1f;
            if (tc >= eta) start = tc - eta; else { start = 0f; speed = Mathf.Clamp(tc / eta, 0.5f, 1f); }
            return P(c, start, Mathf.Min(c.length, tc + 1.2f), speed, 0.16f, 0.4f);
        }

        public static ClipPlan Kick(PlayKind kind, int seed, bool fast = false)
        {
            string key = kind == PlayKind.Punt ? (fast && Lib != null && Lib.HasClip(ArtKeys.PuntFast) ? ArtKeys.PuntFast : ArtKeys.Punt)
                : kind == PlayKind.FieldGoal ? ArtKeys.KickFieldGoal : ArtKeys.KickOff;
            var c = V(key, seed);
            if (c == null || c.length > 9f) return default(ClipPlan);
            float t = ClipTimingTable.Moment(c, 0.55f);
            WarnUnmeasured(c);      // v0.9.0 CONNECT E-388
            return P(c, Mathf.Max(0f, t - KickLead), Mathf.Min(c.length, t + 1.2f), 1f, 0.16f, 0.4f);
        }

        /// <summary>The holder of a field goal / PAT: receives the snap and places the ball.</summary>
        public static ClipPlan Holder(int seed)
        {
            var c = HolderClip(seed);
            return c == null ? default(ClipPlan) : P(c, 0.12f, Mathf.Min(c.length, 3.2f), 1f, 0.2f, 0.35f);     // v0.7.0: starts 0.12 s in, so the hands-up catch (0.6 s into the take) is the snap arriving 0.45 s after the snap; kneels on through the kick
        }

        /// <summary>Kick returner catching the kick (kickoff catch / touchback).</summary>
        public static ClipPlan ReturnCatch(int seed)
        {
            var c = V(ArtKeys.KickReturnCatch, seed);
            return c == null ? default(ClipPlan) : P(c, 0f, Mathf.Min(c.length, 3.5f), 1f, 0.15f, 0.35f);
        }

        /// <summary>Quarterback cadence at the line (stomp / clap / raise the hand): looped over the take when it is short.</summary>
        public static ClipPlan Cadence(int seed)
        {
            string[] keys = { ArtKeys.QbCadenceStomp, ArtKeys.QbCadenceClap, ArtKeys.QbCadenceRaise, ArtKeys.QbCadence };
            var c = V(keys[((seed % 4) + 4) % 4], seed);
            if (c == null) c = V(ArtKeys.QbCadence, seed);
            return c == null || c.length > 8f ? default(ClipPlan) : P(c, 0f, 0f, 1f, 0.3f, 0.3f);
        }

        /// <summary>Pass-protection ready stance / footwork loop of an offensive lineman (Linemen Pocket Movement mocap).</summary>
        public static ClipPlan PocketStand(int seed, bool leftStance)
        {
            var c = Pick(ArtKeys.OlPocketStand, seed, leftStance ? "LStance" : "RStance", "StepStand");
            return c == null ? default(ClipPlan) : P(c, 0f, 0f, 1f, 0.3f, 0.3f, true);
        }

        /// <summary>Man-coverage corner stance (BGS corner behaviour mocap).</summary>
        public static ClipPlan CornerIdle(int seed)
        {
            var c = Pick(ArtKeys.CbIdle, seed, "Idle_M2M");
            if (c == null) return default(ClipPlan);
            float a = c.length > 6f ? 0.6f : 0f;
            return P(c, a, c.length > 6f ? c.length - 0.4f : 0f, 1f, 0.3f, 0.3f, true);
        }

        /// <summary>Huddle / sideline idles: relaxed standing variants, picked by seed.</summary>
        public static ClipPlan IdleVariant(int seed)
        {
            var wide = IdleVariantWide(seed); if (wide.Valid) return wide;      // v0.10.0 ANIMLIB: lively / look / helmet / generic pool
            var c = V(ArtKeys.IdleLively, seed);
            if (c == null) c = V(ArtKeys.IdleAlt, seed);
            return c == null ? default(ClipPlan) : P(c, 0f, 0f, 1f, 0.4f, 0.4f, c.isLooping);
        }

        /// <summary>Stop from a run: a ball carrier stops with the left or right foot planted (BGS "To_Stand_Relaxed" takes), others with the sprint stops.</summary>
        public static ClipPlan Stop(float speed, bool carrier, bool leftFoot, int seed)
        {
            string key = carrier ? ArtKeys.BcStop : ArtKeys.StopSprint;
            string gait = speed > 6.2f ? "Sprint" : (speed > 4.2f ? "Run" : "Jog");
            var c = Pick(key, seed, gait, leftFoot ? "_LU" : "_RU", "_F_");
            return c == null ? default(ClipPlan) : P(c, 0f, Mathf.Min(c.length, 2.2f), 1f, 0.12f, 0.35f);
        }

        /// <summary>Plant-and-cut / turn at speed: 90 or 180 degrees to the left or right.</summary>
        public static ClipPlan Cut(float signedAngle, float speed, bool carrier, int seed)
        {
            bool left = signedAngle < 0f;
            bool big = Mathf.Abs(signedAngle) > 135f;
            string key = carrier ? (big ? ArtKeys.BcTurn180 : ArtKeys.BcTurn90) : ArtKeys.SprintTurn;
            string dir = left ? (carrier ? "_L_" : "Left") : (carrier ? "_R_" : "Right");
            var c = Pick(key, seed, dir, big ? "180" : "90", speed > 5.5f ? "Sprint" : "Jog");
            return c == null ? default(ClipPlan) : P(c, 0f, Mathf.Min(c.length, 2.6f), 1f, 0.1f, 0.3f);
        }

        /// <summary>Defensive back reading the receiver: break on the ball out of the backpedal / shuffle (BGS corner idle-to-sprint takes).</summary>
        public static ClipPlan BreakOnBall(float angle, int seed)
        {
            string tok = angle > 45f ? "Right" : (angle < -45f ? "Back" : "Forward");
            var c = Pick(ArtKeys.CbBreak, seed, tok);
            return c == null ? default(ClipPlan) : P(c, 0f, Mathf.Min(c.length, 1.4f), 1f, 0.1f, 0.3f);
        }
    }
}

namespace PixFootball.Gameplay
{
    public static partial class AnimKit
    {
        static readonly System.Collections.Generic.HashSet<string> _warned = new System.Collections.Generic.HashSet<string>();
        /// <summary>QA-668: a key without clips is logged once so missing animations are visible in testing instead of silently falling back to locomotion.</summary>
        /// <summary>v0.9.0 CONNECT E-388: a kick clip whose contact moment was never measured falls back to 55 percent of its length; say so once (rebuild the timing table with ClipTimingBuilder).</summary>
        public static void WarnUnmeasured(AnimationClip c)
        {
            ClipTimingTable.Entry e;
            if (c != null && !ClipTimingTable.TryEntry(c, out e) && _warned.Add("timing:" + c.name)) Debug.LogWarning("[AnimKit] kick clip '" + c.name + "' has no measured contact moment (guessing 55% of its length): run ClipTimingBuilder");
        }

        public static void WarnMissing(string key)
        {
            if (_warned.Add(key)) Debug.LogWarning("[AnimKit] no clip registered for key '" + key + "' (falls back to locomotion)");
        }

        /// <summary>The snap: the explosion out of a stance into the first steps (Motus get-off takes, BGS corner break), forward only (no heading change inside the take).</summary>
        public static ClipPlan GetOff(PlayerRole role, int seed)
        {
            AnimationClip c = null;
            switch (role)
            {
                case PlayerRole.OL: case PlayerRole.TE: c = Pick(ArtKeys.OlStance, seed, "ToRunFwd0deg"); break;
                case PlayerRole.DL: c = Pick(ArtKeys.DlMove, seed, "ToRunFwd0deg"); break;
                case PlayerRole.RB: c = Pick(ArtKeys.StanceToSprint, seed, "Run_0_deg_fwd"); break;
                case PlayerRole.WR: c = Pick(ArtKeys.StanceToSprint, seed, "Run_0_deg_fwd"); break;
                case PlayerRole.DB: c = Pick(ArtKeys.CbBreak, seed, "Forward"); break;
                case PlayerRole.LB: c = Pick(ArtKeys.StanceToSprint, seed, "Run_0_deg_fwd"); break;
                default: return default(ClipPlan);
            }
            if (c == null || c.length > 3f || !(c.name.Contains("0deg") || c.name.Contains("0_deg") || c.name.Contains("Forward"))) return default(ClipPlan);
            return P(c, 0f, Mathf.Min(c.length, 1.5f), 1f, 0.12f, 0.4f);
        }

        /// <summary>Picks a seeded variant of a key that HAS its twin in key + "_b" (two-actor takes): returns false when none exists.</summary>
        public static bool TryPair(string key, int seed, float maxLength, out AnimationClip a, out AnimationClip b)
        {
            a = b = null;
            var l = Lib; if (l == null) return false;
            int n = l.ClipVariantCount(key); if (n == 0) { WarnMissing(key); return false; }
            int s0 = ClipBag.Skip(key, seed) ? seed : ClipBag.Index(key, seed, n);      // v0.13 (ANIMLIB-BULK): the no-repeat bag's index
            for (int i = 0; i < n; i++)
            {
                var ca = l.ClipVariant(key, s0 + i);
                if (ca.length > maxLength) continue;
                var cb = TwinOf(key + "_b", ca);
                if (cb == null) continue;
                a = ca; b = cb; return true;
            }
            return false;
        }

        /// <summary>The exact _B / _Def twin of an A clip in the second-actor list, or null (no unrelated fallback: QA-661).</summary>
        public static AnimationClip TwinOf(string keyB, AnimationClip a)
        {
            var l = Lib; if (l == null || a == null) return null;
            string n = a.name; string want = null;
            if (n.EndsWith("_A")) want = n.Substring(0, n.Length - 2) + "_B";
            else if (n.EndsWith("_a")) want = n.Substring(0, n.Length - 2) + "_b";
            else if (n.EndsWith("_BC")) want = n.Substring(0, n.Length - 3) + "_Def";
            if (want == null) return null;
            int cnt = l.ClipVariantCount(keyB);
            for (int i = 0; i < cnt; i++) { var c = l.ClipVariant(keyB, i); if (c.name == want) return c; }
            return null;
        }
    }
}

namespace PixFootball.Gameplay
{
    public static partial class AnimKit
    {
        /// <summary>The quarterback's kneel-down and spike (victory formation / clock play), from the BoNES QB takes.</summary>
        public static ClipPlan QbSpecial(PlayKind kind, int seed)
        {
            string key = kind == PlayKind.Kneel ? ArtKeys.QbKneel : (kind == PlayKind.Spike ? ArtKeys.QbSpike : null);
            if (key == null) return default(ClipPlan);
            var c = V(key, seed);
            return c == null || c.length > 9f ? default(ClipPlan) : P(c, 0f, Mathf.Min(c.length, 4f), 1f, 0.2f, 0.35f, false, true);
        }
    }
}
