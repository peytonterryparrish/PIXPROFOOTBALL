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

        /// <summary>v0.6 photoreal bodies: the position picks the pool, a clearly heavy or light player moves one size (a 255 lb tight end uses the large body, a 215 lb receiver the medium one). Linemen are always large; quarterbacks and kickers keep their size.</summary>
        public static PlayerSize SizeOf(Position p, int weightLb)
        {
            var s = SizeOf(p);
            if (weightLb <= 0) return s;
            switch (Of(p))
            {
                case PlayerRole.LB: case PlayerRole.TE: case PlayerRole.RB:
                    return weightLb >= 250 ? PlayerSize.Large : weightLb <= 190 ? PlayerSize.Small : PlayerSize.Medium;
                case PlayerRole.WR: case PlayerRole.DB:
                    return weightLb >= 214 ? PlayerSize.Medium : PlayerSize.Small;
                default: return s;
            }
        }

        /// <summary>Hint for PlayerAppearance.FromSeed: 0 skill, 1 quarterback, 2 line, 3 receiver / defensive back.</summary>
        public static int AppearanceRole(Position p)
        {
            switch (Of(p))
            {
                case PlayerRole.QB: return 1;
                case PlayerRole.OL: case PlayerRole.DL: return 2;
                case PlayerRole.WR: case PlayerRole.DB: return 3;
                default: return 0;
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
    public partial struct AnimHints
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
        public PlayKind Kind;                        // scrimmage / kickoff / punt / field goal (kick animation choice)
        public bool Carrying;                        // this player has the ball: the carrier locomotion tree (ball tucked) plays
        // ---- v0.7.0 (Track A): quarterback flow, event timing, ball sync ----
        public bool IsQb;                            // the offensive quarterback of a scrimmage play
        public QbPhase Qb;                           // his simulation state (drop-back, set, scramble, mesh ...)
        public bool PrevScramble;                    // the throw (wind-up) came off a scramble: throw on the run
        public int DropSteps;                        // 0 / 3 / 5 / 7 from the play
        public float DropRemaining;                  // metres left to the drop-back point (distance-matched drop animation)
        public bool Rolling;                         // roll-out / boot: the drop is a run to the side
        public float WindupSeconds;                  // simulation seconds from the start of the throw to the release
        public bool BallReleased;                    // the ball already left the hand when the Throw action began (throw away, pitch)
        public float EventEta;                       // seconds until the ball arrives (catch) / the exchange completes (hand-off); < 0 = unknown
        public float Speed;                          // simulated ground speed, m/s
        public bool Pressure;                        // a rusher is close to the quarterback
        public bool Whistled;                        // the play is over (the player is walking / standing after the whistle)
        // ---- v0.9.0 CONNECT: pairs, blocks, catches ----
        public int PairSeed;                         // identical for both actors of a pair (engagement, tackle, hand-off): they pick the same take
        public bool Live;                            // the ball is live (the snap happened)
        public BlockStyle Block;                     // what kind of block a Block / Engaged action is
        public float Lev;                            // engagement leverage -1 (defender) .. +1 (blockers), from the blocker's point of view
        public bool Winning;                         // this actor is winning the leverage contest
        public bool Dropped;                         // the receiver dropped the catch
        public bool Contested;                       // a defender is within a yard of the catch point
        public bool Diving;                          // the catch is at full stretch: dive / one hand
        public bool NearSideline;                    // the catch is within a metre and a half of the line: toe tap
        public bool ReturnCatch;                     // a kick / punt returner catching a kick
        public bool FairCatch;                       // the returner signals for a fair catch
        public float HeadingDeg;                     // heading of the first move relative to the facing, signed degrees (+ = to the right)
        public float Closing;                        // closing speed of the tackler on the carrier, m/s
        public float CarrierSpeed;                   // carrier ground speed at the tackle, m/s
        public bool Strip;                           // the tackle is a strip attempt (fumble)
    }

    /// <summary>v0.9.0 CONNECT E-163 / E-164: what a Block / Engaged action means for the animation (pass-set footwork, run-block drive, pull, stalk, chip, cut ...).</summary>
    public enum BlockStyle { None, PassSet, RunBlock, Pull, Stalk, Chip, Cut, Double, Kickout }

    public struct ClipPlan
    {
        public AnimationClip Clip;
        public float Start, End, Speed, FadeIn, FadeOut;
        public bool Loop, Hold;
        /// <summary>Keep the legs of the locomotion layer while the player is running (throws, catches, hand-offs made on the move).</summary>
        public bool UpperBody;
        /// <summary>v0.10.0 CHOREO: speed (m/s) above which an upper-body plan gives the legs to locomotion (0 = the default 2 m/s). Engagements and hand-offs use a low one so a pair that moves never treadmills.</summary>
        public float MaskSpeed;
        /// <summary>Initial offset into the window for a looping clip (staggers a line of identical stances without moving the loop point).</summary>
        public float Phase;
        /// <summary>v0.9.0 CONNECT E-286: fade floor of this clip (0 = the default <see cref="ActionAnimator.MinFade"/>); impact takes (tackle, catch, hand-off) ask for a short one so they are fully in at the contact frame.</summary>
        public float FadeFloor;
        public bool Valid { get { return Clip != null; } }
    }

    /// <summary>Maps simulation actions onto ArtLibrary mocap clips (see Docs/ARCHITECTURE.md, v0.2 animation mapping).</summary>
    public static partial class AnimKit
    {
        static ArtLibrary Lib { get { return ArtLibrary.Load(); } }

        static ClipPlan P(AnimationClip c, float start = 0f, float end = 0f, float speed = 1f, float fadeIn = 0.18f, float fadeOut = 0.28f, bool loop = false, bool hold = false)
        {
            return new ClipPlan { Clip = c, Start = start, End = end, Speed = speed, FadeIn = fadeIn, FadeOut = fadeOut, Loop = loop, Hold = hold };
        }

        /// <summary>Upper-body plan: keeps the legs of the locomotion layer while the player runs (QA-648).</summary>
        static ClipPlan Up(ClipPlan p) { p.UpperBody = true; return p; }

        static AnimationClip V(string key, int seed) { var l = Lib; var c = l != null ? l.ClipVariant(key, BagSeed(l, key, seed)) : null; if (c == null && l != null) WarnMissing(key); return c; }

        /// <summary>v0.13 (ANIMLIB-BULK): the seed a variant list is indexed with: the no-repeat bag's index for the key (see <see cref="ClipBag"/>), the plain seed for seed 0 and the quarterback keys.</summary>
        static int BagSeed(ArtLibrary l, string key, int seed)
        {
            if (ClipBag.Skip(key, seed)) return seed;
            return ClipBag.Index(key, seed, l.ClipVariantCount(key));
        }

        /// <summary>Variant of a key whose clip name contains every token (best score); falls back to the seeded variant.</summary>
        static AnimationClip Pick(string key, int seed, params string[] tokens)
        {
            var l = Lib; if (l == null) return null;
            int n = l.ClipVariantCount(key); if (n == 0) return null;
            var ties = new System.Collections.Generic.List<AnimationClip>(); int bs = -1;      // v0.13 (ANIMLIB-BULK): every take with the best token score is a candidate; the bag walks them
            for (int i = 0; i < n; i++)
            {
                var c = l.ClipVariant(key, i);
                int s = 0;
                foreach (string t in tokens) if (c.name.Contains(t)) s++;
                if (s > bs) { bs = s; ties.Clear(); }
                if (s == bs) ties.Add(c);
            }
            if (bs > 0) return ClipBag.Skip(key, seed) ? ties[0] : ClipBag.Draw("pk:" + key, seed, ties);
            return l.ClipVariant(key, BagSeed(l, key, seed));
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
        [System.Obsolete("Superseded by StanceClips / StanceRules (QA-657); kept only for tests and the free-roam fallback")]
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
            return P(c, start, b, 1f, 0.3f, 0.25f, true);
        }

        /// <summary>An offensive lineman / blocker who is not engaged yet: the pass-pro ready footwork (BGS pocket mocap), not an engaged drive-block take (QA-659).</summary>
        public static ClipPlan Block(int seed)
        {
            var pocket = PocketStand(seed, (seed & 1) == 0);
            if (pocket.Valid) return pocket;
            var c = V("ol_pass_set", seed % 5);
            return c == null ? default(ClipPlan) : P(c, 0f, 0f, 1f, 0.25f, 0.25f, true);
        }

        /// <summary>Engaged blockers: the offense plays the A actor, the defender the B actor of the same take (only takes that have their twin: QA-661).</summary>
        public static ClipPlan Engaged(AnimHints h)
        {
            string key = "ol_pass_set";
            switch (h.Rush)
            {
                case RushMove.Bull: key = "dl_bull_rush"; break;
                case RushMove.Swim: case RushMove.Rip: case RushMove.Speed: key = "dl_rush_swim"; break;
                case RushMove.Spin: key = "dl_rush_spin"; break;
            }
            // QA-662: short takes only (a long take cannot loop seamlessly in a live block), played once and held on the last pose
            AnimationClip a, b;
            if (!TryPair(key, h.Seed, 4.2f, out a, out b) && !TryPair("ol_pass_set", h.Seed, 4.2f, out a, out b)) return default(ClipPlan);
            // for the dl_* keys the A actor is the defender; for ol_* keys the A actor is the blocker
            bool pairedKey = key.StartsWith("dl_");
            bool playB = pairedKey ? !h.Defender : h.Defender;
            var c = playB ? b : a;
            return P(c, 0f, Mathf.Min(c.length, 4.2f), 1f, 0.22f, 0.3f, false, true);
        }

        public static ClipPlan Shed(int seed)
        {
            var c = V("dl_shed", seed);
            return c == null ? default(ClipPlan) : P(c, 0.2f, Mathf.Min(c.length, 2.6f), 1.5f, 0.15f, 0.3f);
        }

        public static ClipPlan Throw(AnimHints h)
        {
            if (h.Snapper) { var sn = LongSnapPlan(h); if (sn.Valid) return sn; }      // v0.9.0 (E-254): the long snapper does not throw like a quarterback
            if (h.IsQb && !h.Pitch) { var qp = QbThrow(h); if (qp.Valid) return qp; }     // v0.7.0: the derived takes with a measured release
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
            const float windup = 0.32f;
            float speed = 1.1f;
            float start = Mathf.Max(0f, rel - windup * speed);
            return Up(P(c, start, Mathf.Min(c.length, rel + 0.9f), speed, 0.12f, 0.3f));
        }

        public static ClipPlan Handoff(AnimHints h)
        {
            AnimationClip a, b;
            if (!TryHandoffPick(h, out a, out b) && !TryPair("handoff", h.Seed, 6f, out a, out b)) { a = V("handoff", h.Seed); b = null; if (a == null) return default(ClipPlan); }
            var c = h.Partner && b != null ? b : a;
            float t = ClipTimingTable.Moment(c, 0.4f);
            float rate = GamePace.AnimationRate;      // v0.9.0 CONNECT E-363: the exchange plays at the pace of the game, started early enough that its frame still lands on the simulated exchange
            return Floor(Up(P(c, Mathf.Max(0f, t - Lead(h.EventEta, 0.15f) * rate), Mathf.Min(c.length, t + 0.7f), rate, 0.14f, 0.3f)), ImpactFloor);
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
            var ties = new System.Collections.Generic.List<AnimationClip>();      // v0.13 (ANIMLIB-BULK): the takes tied at the best score go through the no-repeat bag
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
                    if (s > bs) { bs = s; ties.Clear(); }
                    if (s == bs) ties.Add(cl);
                }
            }
            if (bs < 2 && ties.Count > 1) ties.RemoveRange(1, ties.Count - 1);      // nothing matched the ball: the old first take, not a random one
            c = ClipBag.Draw("catch:" + tail, h.Seed, ties);
            if (c == null) return default(ClipPlan);
            float t = ClipTimingTable.Moment(c, 0.35f);
            float rate = GamePace.AnimationRate;      // v0.9.0 CONNECT E-363
            return Up(P(c, Mathf.Max(0f, t - Lead(h.EventEta, 0.1f) * rate), Mathf.Min(c.length, t + 0.85f), rate, 0.12f, 0.3f));
        }

        static readonly System.Collections.Generic.List<MoveTakeTable.Take> _spinTakes = new System.Collections.Generic.List<MoveTakeTable.Take>();

        /// <summary>The ball carrier's spin: a window of the BoNES spin / dodge takes (found by the move take builder), sped up to fit the simulation's spin. Empty plan = use the procedural spin.</summary>
        public static ClipPlan Spin(int seed)
        {
            var t = MoveTakeTable.Load(); var l = Lib;
            if (t == null || l == null) return default(ClipPlan);
            t.Of(ArtKeys.Spin, _spinTakes);
            if (_spinTakes.Count == 0) return default(ClipPlan);
            var take = _spinTakes[ClipBag.Skip("spin", seed) ? ((seed % _spinTakes.Count) + _spinTakes.Count) % _spinTakes.Count : ClipBag.Index("spin", seed, _spinTakes.Count)];      // v0.13 (ANIMLIB-BULK): the no-repeat bag
            var clip = l.ClipByName(take.clip);
            if (clip == null) return default(ClipPlan);
            float dur = Mathf.Max(0.3f, take.t1 - take.t0);
            float speed = Mathf.Clamp(dur / 0.5f, 1f, 1.8f);
            return P(clip, Mathf.Max(0f, take.t0 - 0.18f * speed), Mathf.Min(clip.length, take.t1 + 0.3f * speed), speed, 0.12f, 0.28f);
        }

        public static ClipPlan Jump(int seed)
        {
            var c = V("catch_jump", seed);
            if (c == null) return default(ClipPlan);
            return P(c, 0.1f, Mathf.Min(c.length, 1.3f), 1.15f, 0.1f, 0.28f);
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

        /// <summary>Both actors of a tackle: tackler = A take, carrier (Partner) = the matching B take. Among the best-scoring variants with a twin the pick is seeded (QA-664).</summary>
        public static ClipPlan Tackle(AnimHints h, bool carrier)
        {
            string key = h.Sack ? "qb_sack" : TackleKey(h.Tackle);
            string approach = h.Approach_Back ? "Back" : "Front";
            string tail = approach + h.Side;
            var l = Lib; if (l == null) return default(ClipPlan);
            var best = new System.Collections.Generic.List<AnimationClip>(); int bs = -1;
            for (int i = 0; i < l.ClipVariantCount(key); i++)
            {
                var cl = l.ClipVariant(key, i);
                if (cl.length > 8.5f) continue;
                if (carrier && TwinOf(key + "_b", cl) == null) continue;     // QA-661: never pair with an unrelated take
                int s = 0;
                if (cl.name.Contains(approach)) s += 3;
                if (cl.name.Contains(tail)) s += 2;
                if (key == "tackle_success") s = 1;
                if (s > bs) { bs = s; best.Clear(); }
                if (s == bs) best.Add(cl);
            }
            if (best.Count == 0) { WarnMissing(key); return default(ClipPlan); }
            var a = ClipBag.Draw("tk:" + key + approach, h.Seed, best);      // v0.13 (ANIMLIB-BULK): least used first (both actors ask with the same seed and get the same A take)
            var c = carrier ? TwinOf(key + "_b", a) : a;
            if (c == null) c = a;
            float start = key == "tackle_wrap" || key == "qb_sack" ? 0.55f : (key == "tackle_dive" ? 0.5f : 0f);
            return P(c, start, Mathf.Min(c.length, 5.2f), 1f, 0.1f, 0.35f, false, false);
        }

        public static ClipPlan Fall(int seed)
        {
            var c = Pick("fall", seed, "Stand");
            return c == null ? default(ClipPlan) : P(c, 0f, Mathf.Min(c.length, 2.0f), 1f, 0.12f, 0.4f, false, true);
        }

        /// <summary>v0.5 sideline / end-zone reactions from the broadcast layer: teammates cheer ("cheer", "celebrate_td*"), defenders hang their heads ("frustration").</summary>
        public static ClipPlan Reaction(string key, int seed, bool loop)
        {
            var c = ReactionPool(key, seed) ?? V(key, seed);      // v0.10.0 ANIMLIB: wider pools (AnimKit.Extra2)
            if (c == null || c.length > 14f) return default(ClipPlan);
            return P(c, 0f, 0f, 1f, 0.2f, 0.3f, loop, !loop);
        }

        public static ClipPlan Celebrate(int seed)
        {
            var cv = CelebrateVaried(seed); if (cv.Valid) return cv;      // v0.10.0 ANIMLIB: the whole touchdown set
            var c = (seed & 1) == 0 ? V("celebrate_td_2", seed) : V("celebrate_td_3", seed);
            if (c == null || c.length > 12f) c = V("celebrate_td_2", 0);
            return c == null ? default(ClipPlan) : P(c, 0f, 0f, 1f, 0.2f, 0.3f, c.isLooping, false);
        }
    }
}
