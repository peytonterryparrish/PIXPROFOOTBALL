using PixFootball.Art;

namespace PixFootball.EditorTools
{
    public static partial class ArtLibraryBuilder
    {
        /// <summary>v0.13 (QB-MOCAP): the quarterback rules. Processed before <see cref="NewClipRules"/> and <see cref="ClipRules"/>, so a key defined here
        /// (with its own cap) claims its clips first. The v0.12 caps (6 per key) left 176 of the owner's quarterback takes behind full keys and 37 with no rule: every
        /// quarterback key takes up to <see cref="QbCap"/> clips now, and the leftovers (random pocket, the Yan / BoNES re-cuts, the day-2 / day-3 imports, the derived <c>pixqb_*</c> throws) have a key.
        /// The runtime picks through the QB clip catalog (<c>PixQbClips</c>, built by <c>QbClipCatalog</c>), not through the key order, so a key is a pool, not a ranking.</summary>
        const int QbCap = 120;

        static readonly Rule[] QbRules =
        {
            // ---- stance, cadence, snap
            new Rule(ArtKeys.QbStanceShotgun, @"shotgun_ready_to_catch_loop|^qb_shotgun_to_ready|ready_to_pass_loop|^quarterback_shotgun_00\d", null, QbCap),
            new Rule(ArtKeys.PixStanceQbUnder, @"^pixstance_qb_under", null, QbCap),
            new Rule(ArtKeys.QbStanceUnder, @"undercenter_to_catch_loop|^qb_stance_transition", null, QbCap),
            new Rule("qb_cadence_under", @"^scrim_qbundrctr_cadence|^qb_cadence_\d+_(a|b|ab)$", null, QbCap),
            new Rule("qb_cadence_shotgun", @"^scrim_qbshotgun_cadence", null, QbCap),
            new Rule(ArtKeys.QbCadenceClap, @"^bgs_qb_cadence_03_clap$", null, QbCap),
            new Rule(ArtKeys.QbCadenceRaise, @"^bgs_qb_cadence04_raise$", null, QbCap),
            new Rule(ArtKeys.QbCadenceStomp, @"^bgs_qb_cadence_0[12]_[a-z]*stomp$", null, QbCap),
            new Rule("qb_snap_dropback", @"^qb_snaptodropback_transition$|^qb_snap_1stepdropback$|^qb_shotgun_snap_catch_00$", null, QbCap),
            new Rule("qb_snap_hands", @"^qb_undercenter_snap_00", null, QbCap),
            new Rule("qb_snap_drop_under", @"^qb_undercen?er_catch_to_", null, QbCap),
            new Rule(ArtKeys.QbSnapUnder, @"undercenter_snap|undercenter_to_catch_bad", null, QbCap),
            new Rule(ArtKeys.QbSnapShotgun, @"shotgun_to_catch_(center|high|low|side)|^quarterback_shotgun|^qb_shotgun_00[4-6]|shotgun_snap", null, QbCap),
            // ---- drops
            new Rule("qb_drop_walk", @"^qb_[357]stepdropback_walk$", null, QbCap),
            new Rule("qb_drop_settle", @"^qb_[357]stepdropback_settle$", null, QbCap),
            new Rule(ArtKeys.QbDrop3, @"^qb_3stepdropback|3_step_dropback", null, QbCap),
            new Rule(ArtKeys.QbDrop5, @"^qb_5stepdropback|5_step_dropback", null, QbCap),
            new Rule(ArtKeys.QbDrop7, @"^qb_7stepdropback|7_step_dropback", null, QbCap),
            // ---- throws
            new Rule("qb_throw_on_run", @"^run_fthrow|(^|bones_)qb_(run_(left|right|forward)_pass|runfoward_to_jump_pass)", null, QbCap),
            new Rule("qb_throw_tackled", @"^qb_offbalanace_tackle|frustrationqbbadthrow", null, QbCap),
            new Rule(ArtKeys.QbThrowBullet, @"pass_(quick_|strong_)?bullet|^quarterbackpass(long|short)bullet|^qb_pass_(short|long)bullet", null, QbCap),
            new Rule(ArtKeys.QbThrowLobLong, @"pass_long_lob|^quarterbackpasslonglob", null, QbCap),
            new Rule(ArtKeys.QbThrowLobMedium, @"pass_medium_lob", null, QbCap),
            new Rule(ArtKeys.QbThrowLobShort, @"pass_short_lob|pass_shortlob|^quarterbackpassshortlob", null, QbCap),
            new Rule(ArtKeys.QbThrowTouch, @"touch_pass", null, QbCap),
            new Rule(ArtKeys.QbThrowJump, @"jump_pass|pass_jump", null, QbCap),
            new Rule(ArtKeys.QbThrowOffbalance, @"offbalance_pass|offbalance_throw|ready_pass_to_offbalance", null, QbCap),
            new Rule(ArtKeys.QbThrowSidearm, @"sidearm_pass|siderman_pass|pass_sidearm", null, QbCap),
            new Rule(ArtKeys.QbThrowShovel, @"shovel_pass|pass_shovel|passshovel|pass_underhand", null, QbCap),
            new Rule(ArtKeys.QbThrowAway, @"throwaway", null, QbCap),
            new Rule(ArtKeys.QbPumpFake, @"pumpfake", null, QbCap),
            new Rule(ArtKeys.QbFakeHandoff, @"fakehandoff", null, QbCap),
            new Rule(ArtKeys.QbHailMary, @"hail_mary", null, QbCap),
            new Rule(ArtKeys.QbSack, @"falling_throw.*sack|^sack(wrap|broken)", null, QbCap),
            new Rule(ArtKeys.QbFallingThrow, @"falling_throw", null, QbCap),
            new Rule("qb_backpedal_pass", @"backpedal_backwards_pass", null, QbCap),
            // ---- on the move
            new Rule(ArtKeys.QbScramble, @"scramble|ready_to_run_forward|ready_to_run_00[2-5]|transitionrunfwd", null, QbCap),
            new Rule(ArtKeys.QbRunLeft, @"ready_to_run_left", null, QbCap),
            new Rule(ArtKeys.QbRunRight, @"ready_to_run_right", null, QbCap),
            new Rule(ArtKeys.QbSidestep, @"sidestep|ready_to_step", null, QbCap),
            new Rule("qb_shuffle", @"^quarterbackshuffle", null, QbCap),
            // ---- pocket, ready
            new Rule(ArtKeys.QbHesitation, @"hesitation", null, QbCap),
            new Rule(ArtKeys.QbLookAround, @"lookaround", null, QbCap),
            new Rule(ArtKeys.QbReadyPass, @"ready_pass_to_pass|ready_to_confident", null, QbCap),
            new Rule("qb_pocket", @"random_pocket|(^|bones_)qb_neutral", null, QbCap),
            // ---- the rest of the sequence
            new Rule(ArtKeys.QbSneak, @"qb_sneak", null, QbCap),
            new Rule(ArtKeys.QbKneel, @"qb_kneel", null, QbCap),
            new Rule(ArtKeys.QbSlide, @"qb_slide", null, QbCap),
            new Rule(ArtKeys.QbSpike, @"qb_spike", null, QbCap),
            new Rule(ArtKeys.QbToss, @"qb_toss", null, QbCap),
            new Rule(ArtKeys.QbDive, @"qb_dive", null, QbCap),
            new Rule(ArtKeys.CelebrateQb, @"qb_celebration", null, QbCap),
            new Rule(ArtKeys.QbCadence, @"qb_cadence", null, QbCap),
            new Rule(ArtKeys.PixQbThrowSet, @"^pixqb_throw_set", null, QbCap),
            new Rule(ArtKeys.PixQbThrowRun, @"^pixqb_throw_run", null, QbCap),
            new Rule(ArtKeys.PixQbThrowOff, @"^pixqb_throw_off", null, QbCap),
            new Rule(ArtKeys.PixQbThrowAway, @"^pixqb_throw_away", null, QbCap),
            new Rule(ArtKeys.PixQbPump, @"^pixqb_pump", null, QbCap),
            new Rule(ArtKeys.QbThrow, @"^(bones_)?qb_shotgun_pass|^qb_ready_pass|^fb_quarterback_pass", null, QbCap),
        };

        /// <summary>The key the quarterback rules give a clip name (the name as the clip is called, any pack prefix), or null when no QB rule claims it. QA / tests.</summary>
        public static string QbKeyOf(string clipName)
        {
            string n = NormalizeClip(clipName);
            foreach (var r in QbRules)
            {
                if (!r.inc.IsMatch(n)) continue;
                if (r.exc != null && r.exc.IsMatch(n)) continue;
                return r.key;
            }
            return null;
        }
    }
}
