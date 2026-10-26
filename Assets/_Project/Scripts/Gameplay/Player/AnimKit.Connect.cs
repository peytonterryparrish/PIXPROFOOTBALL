using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 CONNECT plan builders: role-specific blocks (E-163/164/165), matched engagement pairs (E-162/185), get-offs chosen by the first move (E-251/165), the catch family (E-127/128/272/283/308),
    /// stumble / fall / get-up (E-211/212), the spin by direction (E-142/145) and the pair seed for tackles and hand-offs (E-139/210/213). Every plan is empty when its clips are not in the library, so callers fall back.
    /// </summary>
    public static partial class AnimKit
    {
        /// <summary>Fade floor of impact takes: the take must be fully in at the contact frame (E-286 / E-376).</summary>
        public const float ImpactFloor = 0.08f;

        static ClipPlan Floor(ClipPlan p, float f) { p.FadeFloor = f; return p; }

        /// <summary>Variant of a key whose name contains every token, preferring the mirror image (name ends with _M) when asked; null when nothing matches.</summary>
        static AnimationClip PickTokens(string key, int seed, bool mirror, params string[] tokens)
        {
            var l = Lib; if (l == null) return null;
            int n = l.ClipVariantCount(key); if (n == 0) return null;
            var ties = new List<AnimationClip>(); int bs = -1;      // v0.13 (ANIMLIB-BULK): every take with the best score is a candidate, the no-repeat bag walks them
            for (int k = 0; k < n; k++)
            {
                var c = l.ClipVariant(key, k);
                int s = 0; bool all = true;
                foreach (string t in tokens) { if (c.name.Contains(t)) s += 2; else all = false; }
                if (!all) continue;
                bool isM = c.name.EndsWith("_M");
                if (isM == mirror) s += 1;
                if (s > bs) { bs = s; ties.Clear(); }
                if (s == bs) ties.Add(c);
            }
            if (ties.Count == 0) return null;
            return ClipBag.Skip(key, seed) ? ties[0] : ClipBag.Draw("pt:" + key + (mirror ? "M" : "") + string.Join("|", tokens), seed, ties);
        }

        // =========================================================================================================== blocks
        /// <summary>
        /// A blocker who is not locked with anybody yet (E-163 / E-164): linemen set (pass pro: stand / kick-slide step / shuffle chosen by their simulated velocity against their facing),
        /// fire out (run block) and receivers, backs and defenders show hands up over their own legs instead of playing the offensive-line pocket loop.
        /// </summary>
        public static ClipPlan BlockPlan(AnimHints h, PlayerRole role, Vector2 localVel)
        {
            float sp = localVel.magnitude;
            bool lineman = role == PlayerRole.OL || role == PlayerRole.TE;
            if (lineman && (h.Block == BlockStyle.PassSet || h.Block == BlockStyle.None || h.Block == BlockStyle.Chip))
            {
                if (sp < 0.45f) return PocketStand(h.Seed, (h.Seed & 1) == 0);
                bool leftStance = (h.Seed & 1) == 0;
                string stance = leftStance ? "LStance" : "RStance";
                AnimationClip c;
                if (localVel.y < -Mathf.Abs(localVel.x) * 0.8f) c = PickTokens(ArtKeys.OlPocketStep, h.Seed, false, stance, "_B_Loop");
                else if (localVel.y > Mathf.Abs(localVel.x) * 0.8f) c = PickTokens(ArtKeys.OlPocketStep, h.Seed, false, stance, "_F_Loop");
                else c = PickTokens(ArtKeys.OlPocketShuffle, h.Seed, false, "CStance", localVel.x < 0f ? "_L_Loop" : "_R_Loop");
                if (c == null) return PocketStand(h.Seed, leftStance);
                return P(c, 0f, 0f, Mathf.Clamp(sp / 1.6f, 0.8f, 1.5f), 0.25f, 0.25f, true);
            }
            if (lineman && (h.Block == BlockStyle.RunBlock || h.Block == BlockStyle.Double || h.Block == BlockStyle.Kickout || h.Block == BlockStyle.Cut || h.Block == BlockStyle.Pull))
            {
                // the run-block footwork out of the stance (the first 1.4 s: drive steps, hands up), then the engagement takes over
                var c = PickTokens(ArtKeys.OlRunBlock, h.Seed, (h.Seed & 1) == 1, "s01");
                if (c != null) return P(c, 0f, Mathf.Min(c.length, 1.4f), 1f, 0.2f, 0.3f);
                return PocketStand(h.Seed, (h.Seed & 1) == 0);
            }
            // receivers, backs, defenders and tight ends in space: hands up over the legs of the locomotion layer (upper body only), not the lineman's pocket loop
            return HandsUp(h.Seed, role == PlayerRole.WR || role == PlayerRole.DB);
        }

        /// <summary>Hands out in front of the chest (the stalk / chip block pose): the punch of a stale hit take, held and breathing, over the locomotion legs.</summary>
        public static ClipPlan HandsUp(int seed, bool stalk)
        {
            var c = PickTokens(ArtKeys.OlPassSet, seed, false, stalk ? "BlockHitRStale" : "BlockHitCStale");
            if (c == null) c = V(ArtKeys.OlPassSet, seed);
            if (c == null) return default(ClipPlan);
            float a = Mathf.Min(0.42f, c.length * 0.3f), b = Mathf.Min(c.length, a + 0.9f);
            return Up(P(c, a, b, 1f, 0.2f, 0.3f, true));
        }

        // =========================================================================================================== engagements
        static bool NameHas(AnimationClip c, string tok) { return c.name.IndexOf(tok, System.StringComparison.OrdinalIgnoreCase) >= 0; }

        /// <summary>A twin pair of a key chosen from the SHARED pair seed (both blockers of an engagement call this with the same seed and get the same A / B take). <paramref name="has"/> / <paramref name="not"/> filter the A clip names.</summary>
        static bool PairFiltered(string key, int pairSeed, float maxLength, string has, string not, out AnimationClip a, out AnimationClip b)
        {
            a = b = null;
            var l = Lib; if (l == null) return false;
            int n = l.ClipVariantCount(key); if (n == 0) return false;
            int start = ClipBag.Skip(key, pairSeed) ? ((pairSeed % n) + n) % n : ClipBag.Index(key, pairSeed, n);      // v0.13 (ANIMLIB-BULK): the no-repeat bag's index
            for (int k = 0; k < n; k++)
            {
                var ca = l.ClipVariant(key, (start + k) % n);
                if (ca.length > maxLength) continue;
                if (has != null && !NameHas(ca, has)) continue;
                if (not != null && NameHas(ca, not)) continue;
                var cb = TwinOf(key + "_b", ca);
                if (cb == null) continue;
                a = ca; b = cb; return true;
            }
            return false;
        }

        /// <summary>
        /// The two actors of a block engagement (E-162 / E-173 / E-185): the pair is chosen by the pass-rush move or, in the run game, by who is winning the leverage contest (drive when the blocker wins, a failing
        /// block when he loses, the stale hand fight in between), from one seed shared by both actors so they always play the matching A / B take. For the dl_* keys the A actor is the rusher, for the ol_* keys the blocker.
        /// </summary>
        public static ClipPlan EngagedPair(AnimHints h, out float holdFrom)
        {
            holdFrom = 0f;
            AnimationClip a = null, b = null; bool aIsBlocker;
            int seed = h.PairSeed != 0 ? h.PairSeed : h.Seed;
            bool run = h.Block == BlockStyle.RunBlock || h.Block == BlockStyle.Double || h.Block == BlockStyle.Kickout || h.Block == BlockStyle.Cut || h.Block == BlockStyle.Pull;
            bool ok = false; aIsBlocker = false;
            if (!run)
            {
                switch (h.Rush)
                {
                    case RushMove.Bull: ok = PairFiltered("dl_bull_rush", seed, 4.2f, null, null, out a, out b); break;
                    case RushMove.Swim: case RushMove.Speed: ok = PairFiltered("dl_rush_swim", seed, 4.2f, null, null, out a, out b); break;
                    case RushMove.Spin: ok = PairFiltered("dl_rush_spin", seed, 4.2f, null, null, out a, out b); break;
                    case RushMove.Rip: ok = PairFiltered("ol_block_fail", seed, 4.5f, "Escape", null, out a, out b); aIsBlocker = ok; break;
                    case RushMove.Disengage: ok = PairFiltered("dl_shed", seed, 4.2f, null, null, out a, out b); break;
                }
                if (!ok) { ok = PairFiltered("ol_pass_set", seed, 3.2f, null, null, out a, out b); aIsBlocker = ok; }
                else if (h.Rush != RushMove.Rip) aIsBlocker = false;
            }
            else
            {
                aIsBlocker = true;
                if (h.Block == BlockStyle.Cut) ok = PairFiltered(ArtKeys.OlBlockCut, seed, 4.7f, h.Lev > 0.2f ? "Pass" : "Stale", null, out a, out b);        // the cut block by assignment
                else if (h.Block == BlockStyle.Pull || h.Block == BlockStyle.Kickout) ok = PairFiltered(ArtKeys.OlPulling, seed, 3f, h.Lev > 0.2f ? "Pass" : "Stale", null, out a, out b);   // the kick-out after a pull
                if (ok) { }
                else if (h.Lev > 0.35f) ok = PairFiltered("ol_block_push", seed, 3.2f, null, null, out a, out b);
                else if (h.Lev < -0.25f) ok = PairFiltered("ol_block_fail", seed, 4.5f, null, "Escape", out a, out b);
                else ok = PairFiltered("ol_block_engage", seed, 4f, null, null, out a, out b);
                if (!ok) ok = PairFiltered("ol_pass_set", seed, 3.2f, null, null, out a, out b);
            }
            if (!ok || a == null) return default(ClipPlan);
            // dl_* keys: A = defender, ol_* keys: A = blocker
            bool playA = aIsBlocker ? !h.Defender : h.Defender;
            var c = playA ? a : (b != null ? b : a);
            float end = Mathf.Min(c.length, 3.0f);
            holdFrom = Mathf.Min(0.45f * end, 1.0f);          // the hand-fight loop after the take plays this window
            return P(c, 0f, end, 1f, 0.22f, 0.3f);
        }

        /// <summary>The hand-fight loop an engaged player settles into after his pair take played through (E-185: loop the engaged hand fight instead of freezing the last pose).</summary>
        public static ClipPlan EngageLoop(AnimationClip clip, float from)
        {
            if (clip == null) return default(ClipPlan);
            float end = Mathf.Min(clip.length, 3.0f);
            if (end - from < 0.4f) from = Mathf.Max(0f, end - 0.6f);
            return P(clip, from, end, 1f, 0.2f, 0.3f, true);
        }

        // =========================================================================================================== the snap
        /// <summary>
        /// The explosion out of a stance chosen by the FIRST simulated action and its heading (E-251 / E-165): linemen fire out forward or at an angle, a pass-protecting lineman steps into his set, a defensive back who
        /// backpedals gets the back-turned break (not a forward sprint), a corner who shuffles gets the lateral one, everybody else the forward transition. Mirror images for left-hand headings.
        /// </summary>
        public static ClipPlan GetOffFor(PlayerRole role, int seed, AgentAction first, float headingDeg, BlockStyle block)
        {
            bool left = headingDeg < 0f; float ah = Mathf.Abs(headingDeg);
            AnimationClip c = null;
            switch (role)
            {
                case PlayerRole.OL: case PlayerRole.TE:
                    if (first == AgentAction.Block || first == AgentAction.Engaged)
                    {
                        if (block == BlockStyle.RunBlock || block == BlockStyle.Double || block == BlockStyle.Kickout || block == BlockStyle.Cut || block == BlockStyle.Pull) return BlockPlan(new AnimHints { Seed = seed, Block = block }, role, Vector2.up);
                        return default(ClipPlan);       // pass set: the pocket footwork plan owns the first step
                    }
                    c = ah > 28f ? PickTokens(ArtKeys.OlStance, seed, !left, "Left45deg") : PickTokens(ArtKeys.OlStance, seed, false, "Fwd0deg");
                    if (c == null) c = PickTokens(ArtKeys.OlStance, seed, false, "Fwd0deg");
                    break;
                case PlayerRole.DL:
                    c = ah > 28f ? PickTokens(ArtKeys.DlMove, seed, left, "Right45deg") : PickTokens(ArtKeys.DlMove, seed, false, "Fwd0deg");
                    if (c == null) c = PickTokens(ArtKeys.DlMove, seed, false, "Fwd0deg");
                    break;
                case PlayerRole.DB: case PlayerRole.LB: case PlayerRole.WR: case PlayerRole.RB:
                    if (first == AgentAction.Backpedal) c = ah > 35f ? PickTokens(ArtKeys.CbBreak, seed, left, "BackRight") : PickTokens(ArtKeys.CbBreak, seed, false, "IdleToSprint_Back");
                    else if (first == AgentAction.Shuffle || ah > 60f)
                    {
                        if ((seed & 1) == 1) c = PickTokens(ArtKeys.CbShuffle, seed, !left, ah > 60f ? "Shuffle_Left" : "ShuffleFoLeft");      // v0.13 (ANIMLIB-BULK): cb_shuffle was never asked for (the left take, its mirror for a right heading)
                        if (c == null) c = PickTokens(ArtKeys.CbBreak, seed, left, "Right");
                    }
                    else c = PickTokens(ArtKeys.CbBreak, seed, false, "Forward_Transition");
                    if (c == null) c = PickTokens(ArtKeys.CbBreak, seed, false, "Forward");
                    break;
                default: return default(ClipPlan);
            }
            if (c == null || c.length > 3f) return default(ClipPlan);
            return P(c, 0f, Mathf.Min(c.length, 1.0f), 1f, 0.12f, 0.4f);
        }

        // =========================================================================================================== catching
        /// <summary>
        /// The catch family (E-127 / E-128 / E-272 / E-283 / E-308): fair catch and return catch for returners, a drop that never closes the hands, the dive and one-hand takes at full stretch, the toe tap at the sideline,
        /// the interception for defenders and the high / low / side / jump catches by ball height and side otherwise. Hands are finished with the reach IK to the real ball (CatchReach).
        /// </summary>
        public static ClipPlan CatchFamily(AnimHints h)
        {
            var l = Lib; if (l == null) return default(ClipPlan);
            AnimationClip c = null; string key = null;
            if (h.ReturnCatch)
            {
                if (h.FairCatch) { c = PickTokens(ArtKeys.FairCatch, h.Seed, false, "s02"); if (c == null) c = PickTokens(ArtKeys.FairCatch, h.Seed, false, "fair_catch"); key = ArtKeys.FairCatch; }
                else { c = PickTokens(ArtKeys.KickReturnCatch, h.Seed, false, "kickoff_catch"); key = ArtKeys.KickReturnCatch; }
                if (c != null && c.length < 9f)
                {
                    float tm = ClipTimingTable.Moment(c, 0.35f);
                    return Floor(Up(P(c, Mathf.Max(0f, tm - Lead(h.EventEta, 0.25f)), Mathf.Min(c.length, tm + 1.0f), 1f, 0.12f, 0.3f)), ImpactFloor);
                }
            }
            if (!h.Defender && h.Diving) key = (h.Seed & 1) == 0 ? ArtKeys.CatchDive : ArtKeys.CatchOneHand;
            else if (!h.Defender && h.NearSideline && !h.Airborne) key = ArtKeys.CatchToeTap;
            if (key != null && l.ClipVariantCount(key) > 0)
            {
                c = l.ClipVariant(key, h.Seed);
                if (c != null && c.length <= 3f)
                {
                    float tm = ClipTimingTable.Moment(c, 0.4f);
                    return Floor(Up(P(c, Mathf.Max(0f, tm - Lead(h.EventEta, 0.2f)), Mathf.Min(c.length, tm + 0.8f), 1f, 0.12f, 0.3f)), ImpactFloor);
                }
            }
            if (h.Dropped && (h.Defender || (h.Seed & 3) < 3))      // v0.13 (ANIMLIB-BULK): a receiver who drops it plays one of the failed-catch takes (catch_fail was never asked for); wave 2: 3 of 4 drops, and the defender who drops the interception plays the B takes
            {
                var cf = h.Defender ? ClipSelect.Pick("catch_fail_b", null, h.Seed, '?', 3.2f) : PickEither("catch_fail", h.Seed, 3.2f);
                if (cf != null) { float tf = ClipTimingTable.Moment(cf, 0.4f); return Floor(Up(P(cf, Mathf.Max(0f, tf - Lead(h.EventEta, 0.2f)), Mathf.Min(cf.length, tf + 0.8f), 1f, 0.12f, 0.3f)), ImpactFloor); }
            }
            var plan = Catch(h);
            if (!plan.Valid) return plan;
            if (h.Dropped)
            {
                // the hands come together but never close: the take is cut before its hands-secure frame and the arms fall away
                float tm = ClipTimingTable.Moment(plan.Clip, 0.35f);
                plan.End = Mathf.Min(plan.End > 0f ? plan.End : plan.Clip.length, tm + 0.04f); plan.FadeOut = 0.3f; plan.Speed = 1.15f;
            }
            return Floor(plan, ImpactFloor);
        }

        /// <summary>A defender's swat of the ball (E-189): the swat takes by ball height, started ahead of the contact point like a receiver's catch.</summary>
        public static ClipPlan SwatPlan(AnimHints h)
        {
            string ht = h.Airborne ? "Jump" : (h.BallHeight >= 1.75f ? "High" : (h.BallHeight <= 0.9f ? "Low" : "Mid"));
            var c = PickTokens(ArtKeys.Swat, h.Seed, h.BallLocalDir.x < 0f, ht);
            if (c == null) c = V(ArtKeys.Swat, h.Seed);
            if (c == null || c.length > 3f) return default(ClipPlan);
            float tm = ClipTimingTable.Moment(c, 0.4f);
            return Floor(Up(P(c, Mathf.Max(0f, tm - Lead(h.EventEta, 0.2f)), Mathf.Min(c.length, tm + 0.6f), 1.05f, 0.1f, 0.3f)), ImpactFloor);
        }

        // =========================================================================================================== stumble, fall, get-up
        /// <summary>A stumble (E-211): the collision-and-stagger takes, played over the legs of the locomotion layer once he is moving, ahead of the arm windmill.</summary>
        public static ClipPlan Stumble(int seed, float speed)
        {
            var c = PickTokens("fall", seed, false, speed > 2.5f ? "Run0deg" : "Stand0deg");
            if (c == null) c = V("fall", seed);
            if (c == null) return default(ClipPlan);
            var p = P(c, 0.05f, Mathf.Min(c.length, 0.65f), 1.05f, 0.1f, 0.3f);
            return speed > 2.5f ? Up(p) : p;
        }

        /// <summary>The get-up after a fall (E-212): face up / face down / on the side takes, in place.</summary>
        public static ClipPlan GetUp(bool faceUp, int seed)
        {
            var l = Lib; if (l == null) return default(ClipPlan);
            string key = faceUp ? ArtKeys.GetUpFaceUp : ArtKeys.GetUpFaceDown;
            if (l.ClipVariantCount(key) == 0) key = ArtKeys.GetUp;
            var c = l.ClipVariantCount(key) > 0 ? l.ClipVariant(key, seed) : null;
            if (c == null || c.length > 3f) return default(ClipPlan);
            return P(c, 0f, c.length, 1f, 0.2f, 0.35f);
        }

        /// <summary>The fall held for the sim's fall time and then released into the get-up (not held forever).</summary>
        public static ClipPlan FallThenRise(int seed)
        {
            var c = Pick("fall", seed, "Stand");
            return c == null ? default(ClipPlan) : P(c, 0f, Mathf.Min(c.length, 1.6f), 1f, 0.1f, 0.3f, false, false);
        }

        // =========================================================================================================== carrier moves
        static readonly List<MoveTakeTable.Take> _spinTakes2 = new List<MoveTakeTable.Take>();

        /// <summary>The spin turned the way the simulation turned the carrier (E-145) and played over the locomotion legs (E-142): the take's own turn sign (clockwise seen from above = a turn to the right) is matched to the simulated spin direction.</summary>
        public static ClipPlan SpinDir(int seed, float lateral)
        {
            var t = MoveTakeTable.Load(); var l = Lib;
            if (t == null || l == null) return default(ClipPlan);
            t.Of(ArtKeys.Spin, _spinTakes2);
            if (_spinTakes2.Count == 0) return default(ClipPlan);
            MoveTakeTable.Take pick = null; int n = _spinTakes2.Count;
            for (int k = 0; k < n; k++)
            {
                var take = _spinTakes2[((ClipBag.Skip("spin", seed) ? ((seed % n) + n) : ClipBag.Index("spin", seed, n)) + k) % n];      // v0.13 (ANIMLIB-BULK): the no-repeat bag's index
                if (Mathf.Abs(take.turnDegrees) < 90f || Mathf.Sign(take.turnDegrees) == (lateral >= 0f ? 1f : -1f)) { pick = take; break; }
            }
            if (pick == null) pick = _spinTakes2[((seed % n) + n) % n];
            var clip = l.ClipByName(pick.clip);
            if (clip == null) return default(ClipPlan);
            float dur = Mathf.Max(0.3f, pick.t1 - pick.t0);
            float speed = Mathf.Clamp(dur / 0.5f, 1f, 1.8f);
            return Up(P(clip, Mathf.Max(0f, pick.t0 - 0.18f * speed), Mathf.Min(clip.length, pick.t1 + 0.3f * speed), speed, 0.12f, 0.28f));
        }

        /// <summary>The receiver in pre-snap motion: the ready-idle-to-motion transition (wr_release), left or right by the heading (E-249 / E-267).</summary>
        public static ClipPlan MotionStart(int seed, float headingDeg)
        {
            var c = PickTokens(ArtKeys.WrRelease, seed, false, headingDeg < 0f ? "MotionL" : "MotionR");
            if (c == null || c.length > 7f) return default(ClipPlan);
            return P(c, 0f, Mathf.Min(c.length, 1.3f), 1f, 0.2f, 0.4f);
        }

        /// <summary>The hip flip of a defender who stops backpedalling and runs (E-191): the 135 / 180 degree jog-to-jog turn takes (mirror image for a turn to the left).</summary>
        public static ClipPlan TurnAndRun(int seed, float turnDeg)
        {
            float a = Mathf.Abs(turnDeg);
            if (a < 55f) return default(ClipPlan);
            var c = PickTokens(ArtKeys.ManTurnRun, seed, turnDeg < 0f, a > 150f ? "180" : "135");
            if (c == null || c.length > 3f) return default(ClipPlan);
            return P(c, 0f, Mathf.Min(c.length, 0.95f), 1.1f, 0.1f, 0.35f);
        }

        // =========================================================================================================== huddle
        /// <summary>The huddle stand (hands on knees / hips), looped with a staggered phase (E-252).</summary>
        public static ClipPlan HuddlePlan(int seed)
        {
            var c = HuddleClip(seed);      // v0.10.0 ANIMLIB: the authored huddle stand mostly, fidgets / look-arounds for variety
            if (c == null) c = V(ArtKeys.HuddleIdle, seed);
            if (c == null) c = V(ArtKeys.IdleLively, seed);
            if (c == null) return default(ClipPlan);
            float frac = (seed * 0.37f) % 1f; if (frac < 0f) frac += 1f;
            var p = P(c, 0f, 0f, 1f, 0.3f, 0.3f, true);
            p.Phase = frac * c.length;
            return p;
        }

        // =========================================================================================================== tackles: shared seed, take starts at its impact
        /// <summary>
        /// Both actors of a tackle (E-210 / E-213): the SAME pair seed picks the take, and the take starts so its impact frame lands at the simulated contact (the measured moment when the timing table has one).
        /// </summary>
        public static ClipPlan TacklePair(AnimHints h, bool carrier)
        {
            string key = h.Sack ? "qb_sack" : TackleKey(h.Tackle);
            string approach = h.Approach_Back ? "Back" : "Front";
            string tail = approach + h.Side;
            var l = Lib; if (l == null) return default(ClipPlan);
            var best = new List<AnimationClip>(); int bs = -1;
            for (int i = 0; i < l.ClipVariantCount(key); i++)
            {
                var cl = l.ClipVariant(key, i);
                if (cl.length > 8.5f) continue;
                if (TwinOf(key + "_b", cl) == null) continue;     // QA-661: never pair with an unrelated take
                int s = 0;
                if (cl.name.Contains(approach)) s += 3;
                if (cl.name.Contains(tail)) s += 2;
                if (key == "tackle_success") s = 1;
                if (s > bs) { bs = s; best.Clear(); }
                if (s == bs) best.Add(cl);
            }
            if (best.Count == 0) { WarnMissing(key); return default(ClipPlan); }
            int seed = h.PairSeed != 0 ? h.PairSeed : h.Seed;
            var a = ClipBag.Draw("tk:" + key + approach, seed, best);      // v0.13 (ANIMLIB-BULK): least used first; the shared pair seed gives both actors the same A take
            var c = carrier ? TwinOf(key + "_b", a) : a;
            if (c == null) c = a;
            float start;
            ClipTimingTable.Entry e;
            if (ClipTimingTable.TryEntry(a, out e) && e.t > 0.3f) start = Mathf.Max(0f, e.t - 0.25f);
            else start = key == "tackle_wrap" || key == "qb_sack" ? 0.55f : (key == "tackle_dive" ? 0.5f : 0f);
            return Floor(P(c, start, Mathf.Min(c.length, 5.2f), 1f, 0.1f, 0.35f, false, false), ImpactFloor);
        }
    }
}
