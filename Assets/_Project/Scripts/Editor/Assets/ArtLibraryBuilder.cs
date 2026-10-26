using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Populates Assets/_Licensed/ArtLibrary.asset (+ a copy in Assets/_Licensed/Resources) from the imported Draft Fever assets
    /// using name heuristics, then prints which semantic keys were found / missing (also written to Docs/ArtLibraryReport.md).
    /// </summary>
    public static partial class ArtLibraryBuilder
    {
        const string Root = "Assets/_Licensed/DraftFever";
        const string LibPath = ArtLibrary.EditorAssetPath;
        const string ResPath = "Assets/_Licensed/Resources/ArtLibrary.asset";

        class Rule
        {
            public string key; public Regex inc; public Regex exc; public int max; public bool Raw;
            public Rule(string k, string inc, string exc = null, int max = 6)
            { key = k; this.inc = new Regex(inc, RegexOptions.IgnoreCase); this.exc = exc == null ? null : new Regex(exc, RegexOptions.IgnoreCase); this.max = max; }
        }

        /// <summary>v0.13 (ANIMLIB-BULK): the cap of a rule. 656 football takes sat behind keys that were full at 6 (the owner: "so much motion capture is missing"); every non-QB key now takes up to 4x its old cap
        /// (at least 24). Pairs stay aligned (the A and B lists of a key fill in the same name order and are matched by name), the QA reject list still filters, the runtime draws through the no-repeat bag.
        /// Quarterback keys keep their caps here (QB-MOCAP owns them in ArtLibraryBuilder.Qb.cs). <c>-pixOldCaps</c> restores the old caps.</summary>
        static int Cap(Rule r)
        {
            if (OldCaps || r.max >= 400 || r.key.StartsWith("qb_") || r.key.StartsWith("pix_qb")) return r.max;
            // the pre-snap stance pools keep their caps: a wider pool put stances with another facing / hand / foot into the line (FACE_* standing +35 % for WR / S / CB in the 40-play probe), and variety there does not need more takes
            if ((r.key.StartsWith("stance_") && r.key != "stance_to_sprint") || r.key == "idle" || r.key == "idle_alt" || r.key == "dl_stance" || r.key == "ol_stance" || r.key.StartsWith("cb_idle")) return r.max;
            return System.Math.Max(r.max * 4, 24);
        }
        /// <summary>v0.13 wave 2 (BULK-2): single-actor studio takes whose file name ends in _B (the day-3 OLINE_MAN_3 footwork) are not second actors: they register under their own key.</summary>
        static readonly Regex SingleActorB = new Regex(@"^oline_man_3_stance_to_block_run_foot_work_left_\d+(_s\d+)?(_m)?_b$", RegexOptions.IgnoreCase);
        static readonly bool OldCaps = Environment.GetCommandLineArgs().Contains("-pixOldCaps");

        // Matched against the normalised clip name (lowercase, "root|" and "<pack>__" prefixes stripped, "import_" stripped).
        // Order inside the table matters: the first rule that claims a clip wins for single-claim rules; paired (_a/_b) clips register
        // the _A actor under <key> and the _B actor under <key>_b.
        // v0.8: the BGS / Yan mocap (BoNES_Yan folder) and the cleaned / derived clips. Processed BEFORE ClipRules (the first rule that claims a clip wins).
        static readonly Rule[] NewClipRules =
        {
            // ---- v0.10.0 ANIMLIB: the unimported Draft Fever takes (kickoff line idles, real centre / long-snapper mocap, 4-point DL, QB cadences / snaps, crowd, draft night ...). String keys: no ArtKeys constant needed.
            new Rule(ArtKeys.Snapper, @"^ls_snap_00$", null, 4),                       // the real long-snap mocap (the derived long_snap_00 stays as an extra variant)
            new Rule(ArtKeys.StanceSnapper, @"^ls_idle_00$", null, 6),
            new Rule(ArtKeys.StanceCenter, @"^c_idle_00$", null, 6),
            new Rule("center_snap", @"^c_undercentersnap_00$", null, 2),
            new Rule(ArtKeys.StanceDl4pt, @"^dline4pt_idle_00$", null, 6),
            new Rule("dl_getoff_4pt", @"^dlinetransition4ptrtorunfwd0deg", null, 2),
            new Rule("qb_drop_walk", @"^qb_[357]stepdropback_walk$", null, 6),
            new Rule("qb_drop_settle", @"^qb_[357]stepdropback_settle$", null, 6),
            new Rule("qb_snap_dropback", @"^qb_snaptodropback_transition$|^qb_snap_1stepdropback$|^qb_shotgun_snap_catch_00$", null, 4),
            new Rule("qb_cadence_under", @"^scrim_qbundrctr_cadence", null, 8),
            new Rule("qb_cadence_shotgun", @"^scrim_qbshotgun_cadence", null, 8),
            new Rule("qb_snap_hands", @"^qb_undercenter_snap_00", null, 6),
            new Rule("qb_snap_drop_under", @"^qb_undercener_catch_to_", null, 8),
            new Rule("qb_throw_on_run", @"^run_fthrow", null, 2),
            new Rule("qb_throw_tackled", @"^qb_offbalanace_tackle", null, 2),
            new Rule("kickoff_att_idle", @"^kickoff_att_idle_0\d(_closeup)?$", null, 12),      // the loopable stands (the stance layer loops them)
            new Rule("kickoff_att_fidget", @"^kickoff_att_idle", null, 16),                        // helmet fix, stretches, head turn, pointing: one-shots

            new Rule("kickoff_def_run", @"^kickoff_def_\d\d_idletorun", null, 4),
            new Rule("kickoff_def_idle", @"^kickoff_def_\d\d_idle$", null, 6),
            new Rule("kickoff_def_fidget", @"^kickoff_def_\d\d_", null, 12),
            new Rule("trench_atk", @"^trench_00_atk", null, 6),
            new Rule("trench_def", @"^trench_00_def", null, 6),
            // v0.11.0 ANIMLIB: the 2024 legacy-rig takes re-baked with Blender (LEG__ prefix, single actor, QA-passed only: the two hip-roll takes, run_dive and trench_push_lose were dropped)
            new Rule("trench_hold", @"^trench_hold_0\d", null, 8),
            new Rule("trench_win", @"^trench_(win_00|push_win_00|run_00_(left|right)_win)", null, 8),
            new Rule("trench_lose", @"^trench_(lose_00|run_00_(left|right)_lose)", null, 8),
            new Rule("trench_run", @"^trench_run_00_(left|right)(_m)?$", null, 6),
            new Rule("tackle_shoulder", @"^tackle_(shoulder_00|jump_00)", null, 4),
            new Rule("be_tackled_shoulder", @"^get_tackled_(back|front|left_side|right_side)_00", null, 8),
            new Rule("defstance_fall", @"^defstance_fall_0", null, 6),
            new Rule("stiff_arm_run", @"^run_stiffarm_00", null, 4),
            new Rule("takedown", @"^get_takedown", null, 2),
            new Rule("truck_alt", @"^truck_(a_)?0\d", null, 6),
            new Rule("ankle_breaker", @"ankle_breaker_right_success", null, 4),
            new Rule("crowd_stand_happy", @"^mc_stand_happy", null, 6),
            new Rule("crowd_stand_sad", @"^mc_stand_sad", null, 6),
            new Rule("crowd_sit_happy", @"^mc_sit_happy", null, 6),
            new Rule("crowd_sit_sad", @"^mc_sit_sad", null, 6),
            new Rule("crowd_posture", @"^mc_(sit_to_stand|stand_to_sit)", null, 2),
            new Rule("draft_night", @"^draft_night_\d+(_s\d+)?_(a|b)$", null, 24),
            new Rule("endzone_save", @"^endzone_save_\d+(_s\d+)?_(a|b)$", null, 24),
            // kicking (Yan "QB-Kickers-Rework")
            new Rule(ArtKeys.KickReturnCatch, @"^kickoff_catch|^kr_touchback", null, 4),
            new Rule(ArtKeys.KickOff, @"^k_kickoff_kick", null, 4),
            new Rule(ArtKeys.KickFieldGoal, @"^k_fieldgoal", null, 4),
            new Rule(ArtKeys.KickHolder, @"^h_fieldgoal", null, 4),
            new Rule(ArtKeys.PuntFast, @"^p_punt_00_faster", null, 4),
            new Rule(ArtKeys.Punt, @"^p_punt_0[01](_remap)?$", null, 6),
            new Rule(ArtKeys.KickOnside, @"^onside_kick", null, 4),
            new Rule(ArtKeys.KickSquib, @"^squib_kick", null, 4),
            // derived get-ups (GetUpBuilder) and specials
            new Rule(ArtKeys.GetUpFaceUp, @"^getup_face_up", null, 12),
            new Rule(ArtKeys.GetUpFaceDown, @"^getup_face_down", null, 12),
            new Rule(ArtKeys.GetUpSide, @"^getup_side", null, 12),
            new Rule(ArtKeys.PressJam, @"^press_jam", null, 6),
            new Rule(ArtKeys.CatchToeTap, @"^catch_toe_tap", null, 6),
            new Rule(ArtKeys.CatchOneHand, @"^catch_one_hand", null, 6),
            new Rule(ArtKeys.TackleStrip, @"^tackle_strip", null, 6),
            new Rule(ArtKeys.PushOob, @"^push_oob", null, 6),
            new Rule(ArtKeys.DlRushRip, @"^dl_rip", null, 6),
            new Rule(ArtKeys.WrRelease, @"^wr_release|^receivertransitionreadyidlertomotion", null, 6),
            // RAW day-2 / day-3 BoNES takes re-baked by retarget_actions.py (sideline, strips, helmet)
            new Rule(ArtKeys.TackleStrip, @"^bc_striptackle_(critical)?success", null, 8),
            new Rule(ArtKeys.BenchSit, @"^ambient_bench", null, 6),
            new Rule(ArtKeys.CoachAmbient, @"^ambient_coach", null, 6),
            new Rule(ArtKeys.HuddleLeader, @"^leader_idle", null, 6),
            new Rule(ArtKeys.IdleHelmet, @"^helmet_interactions", null, 6),
            new Rule(ArtKeys.Frustration, @"^loosing_frustration", null, 6),
            new Rule(ArtKeys.Swat, @"^swatnormal|^swat_", null, 8),
            new Rule(ArtKeys.HuddleIdle, @"^huddle_idle|^pix_huddle", null, 6),
            new Rule(ArtKeys.HuddleBreak, @"^huddle_break", null, 6),
            new Rule(ArtKeys.BenchSit, @"^bench_sit", null, 6),
            new Rule(ArtKeys.Walk, @"^walk_cycle|^pix_walk|^pix_stroll", null, 6),
            new Rule(ArtKeys.WalkStart, @"^walk_start", null, 6),
            new Rule(ArtKeys.WalkStop, @"^walk_stop", null, 6),
            // ball-carrier locomotion
            new Rule(ArtKeys.BcBackpedal, @"^mob1_jog_(b|bl|br|l|r)_(bkpd_)?loop(_bc|_remap)", @"mob1_jog_(l|r)_loop_bc", 12),
            new Rule(ArtKeys.BcCircle, @"cir_loop_bc$", null, 16),
            new Rule(ArtKeys.BcStop, @"to_stand_relaxed(_lu|_ru)?(\.\d+)?_bc$|to_stand_relaxed_bc(\.\d+)?$", null, 40),
            new Rule(ArtKeys.BcTurn90, @"^mob1_(jog|run)_(l|r)_90_bc|^bgs_sprint\d*_f_90_deg_turn_(left|right)_bc", null, 16),
            new Rule(ArtKeys.BcTurn180, @"^mob1_(jog|run)_(l|r)_180_bc|^bgs_sprint\d*_f_180_deg_turn_(left|right)_bc", null, 16),
            new Rule(ArtKeys.BcJog, @"^mob1_jog_(f|fl|fr|l|r)_loop_bc", null, 12),
            new Rule(ArtKeys.BcRun, @"^mob1_run_(f|fl|fr|l|r)_loop_bc|^bgs_run\d*_(f|fl|fr)_loop_bc(_fix)?$", null, 16),
            new Rule(ArtKeys.BcSprint, @"^bgs_sprint\d*_(f|fl|fr)_loop_bc", null, 16),
            // non-carrier sprint set
            new Rule(ArtKeys.SprintTurn, @"^bgs_sprint\d*_f_(90|180)_deg_turn_(left|right)(\.\d+)?$", null, 16),
            new Rule(ArtKeys.StopSprint, @"^bgs_(sprint|run)\d*_f_to_stand_relaxed(_lu|_ru)?(\.\d+)?$", null, 24),
            new Rule(ArtKeys.SprintAlt, @"^bgs_(sprint|run)\d*_(f|fl|fr|l_cir|r_cir)_loop$|^bgs_run\d*_f_loop$|^bgs_run_f_loop", null, 24),
            // pass-pro footwork / cornerback behaviour
            new Rule(ArtKeys.OlPocketStand, @"^bgs_olm_(shufflestand|stepstand)", null, 8),
            new Rule(ArtKeys.OlPocketShuffle, @"^bgs_olm_shuffle_", null, 16),
            new Rule(ArtKeys.OlPocketStep, @"^bgs_olm_step_", null, 16),
            new Rule(ArtKeys.CbBreak, @"^bgs_cb_idletosprint", null, 12),
            new Rule(ArtKeys.CbIdle, @"^bgs_cb_(idle|gettingintoidle)", null, 6),
            new Rule(ArtKeys.CbShuffle, @"^bgs_cb_shuffle", null, 12),
            new Rule(ArtKeys.CbSprint, @"^bgs_cb_sprint", null, 6),
            new Rule(ArtKeys.CbRotate, @"^bgs_cb_stance_rotate", null, 6),
            // idles
            new Rule(ArtKeys.IdleLook, @"^bgs_fix_mcu_am_.*lookaround", null, 6),
            new Rule(ArtKeys.IdleLively, @"^bgs_fix_", null, 16),
            // carrier moves
            new Rule(ArtKeys.JukeLeft, @"^bgs_(dodge|juke)_frontl_", null, 10),
            new Rule(ArtKeys.JukeRight, @"^bgs_(dodge|juke)_frontr_", null, 10),
            new Rule(ArtKeys.StiffArmLeft, @"^bgs_stiffarm_frontl", null, 8),
            new Rule(ArtKeys.StiffArmRight, @"^bgs_stiffarm_frontr", null, 8),
            new Rule(ArtKeys.Truck, @"^truck_front_|^truck_a_00", null, 8),
            new Rule(ArtKeys.FumbleDive, @"^bgs_fumble_recovery_dive", null, 4),
            new Rule(ArtKeys.DiveForward, @"^bgs_dive_ball", null, 4),
            new Rule(ArtKeys.Handoff, @"^bgs_handoffishotgunspeedoption_", null, 6),
            // QB
            new Rule(ArtKeys.QbCadenceClap, @"^bgs_qb_cadence_03_clap$", null, 4),
            new Rule(ArtKeys.QbCadenceRaise, @"^bgs_qb_cadence04_raise$", null, 4),
            new Rule(ArtKeys.QbCadenceStomp, @"^bgs_qb_cadence_0[12]_[a-z]*stomp$", null, 4),
            new Rule(ArtKeys.QbDrop3, @"^qb_3stepdropback", null, 4),
            new Rule(ArtKeys.QbDrop5, @"^qb_5stepdropback", null, 4),
            new Rule(ArtKeys.QbDrop7, @"^qb_7stepdropback", null, 4),
        };

        static readonly Rule[] ClipRules =
        {
            // ---- locomotion (Motus cycles / BoNES studio)
            new Rule(ArtKeys.Idle, @"^(player_)?standing_idle"),
            new Rule(ArtKeys.IdleAlt, @"^(receivertransitionreadyidle|linebackerblitzidle|2pstance_halfback)"),
            new Rule(ArtKeys.Walk, @"(^|_)walk(cycle)?(_|$)", @"transition|collision|idle"),
            new Rule(ArtKeys.Jog, @"^jogcycle"),
            new Rule(ArtKeys.Run, @"^runcycle"),
            new Rule(ArtKeys.Sprint, @"^sprintcycle"),
            new Rule(ArtKeys.Backpedal, @"^quarterbackshufflebkwd|^bones_backpedal_to_shuffle"),
            new Rule(ArtKeys.StrafeLeft, @"^quarterbackshuffleleft|^slow_shuffle_loop_270"),
            new Rule(ArtKeys.StrafeRight, @"^quarterbackshuffleright|^slow_shuffle_loop_090"),
            new Rule(ArtKeys.ShuffleLeft, @"^(2point|3point|4point)_to_shuffle_270|^slow_shuffle_loop_315"),
            new Rule(ArtKeys.ShuffleRight, @"^(2point|3point|4point)_to_shuffle_090|^slow_shuffle_loop_045"),
            new Rule(ArtKeys.ShuffleSlow, @"^slow_shuffle_loop"),
            new Rule(ArtKeys.TurnRight, @"^trans(ition)?_stand_idle_to_stand_idle_\d+_deg_right|^transition_walk_to_walk|^transitionwalkidle"),
            // QA-918: left turns exist only as mirrored right turns (see the "_m" block in BuildClips); the jog turns are man-turn-run material

            new Rule(ArtKeys.Slowdown, @"slowdown|^trans_run_to_ready_idle"),
            new Rule(ArtKeys.StanceToSprint, @"stance_to_sprint|^trans(ition)?_stand_idle_to_run|^(runningback|linebacker)transition"),
            new Rule(ArtKeys.BackpedalToSprint, @"stance_to_backpedal"),
            new Rule(ArtKeys.ShuffleToSprint, @"^shuffle_to_sprint"),
            new Rule(ArtKeys.CutLeft, @"^avoid45degr"),
            new Rule(ArtKeys.CutRight, @"^avoid90degr"),
            new Rule(ArtKeys.CutHard, @"^avoid(135|180)degr"),
            // ---- stances
            new Rule(ArtKeys.Stance2pt, @"^2pstance_"),
            new Rule(ArtKeys.Stance3pt, @"^3pstance_aggressive|^3pstance_halfback"),
            new Rule(ArtKeys.Stance4pt, @"^dlineidle3ptrv"),
            new Rule(ArtKeys.StanceHalfback, @"^(2|3)pstance_halfback"),
            new Rule(ArtKeys.StanceSafety, @"^2pstance_safety"),
            new Rule(ArtKeys.StanceDefLine, @"^dlineidle3ptr$|^dlinecreep"),
            new Rule(ArtKeys.StanceWideDefense, @"^3pstance_wide_defence"),
            // ---- QB
            new Rule(ArtKeys.QbStanceUnder, @"undercenter_to_catch_loop|^qb_stance_transition"),
            new Rule(ArtKeys.QbStanceShotgun, @"shotgun_ready_to_catch_loop|^qb_shotgun_to_ready|ready_to_pass_loop"),
            new Rule(ArtKeys.QbSnapUnder, @"undercenter_snap|undercenter_to_catch_bad"),
            new Rule(ArtKeys.QbSnapShotgun, @"shotgun_to_catch_(center|high|low|side)|^quarterback_shotgun|^qb_shotgun_00[4-6]|shotgun_snap"),
            new Rule(ArtKeys.QbDrop3, @"3_step_dropback"),
            new Rule(ArtKeys.QbDrop5, @"5_step_dropback"),
            new Rule(ArtKeys.QbDrop7, @"7_step_dropback"),
            new Rule(ArtKeys.QbThrowBullet, @"pass_(quick_|strong_)?bullet|^quarterbackpass(long|short)bullet"),
            new Rule(ArtKeys.QbThrowLobLong, @"pass_long_lob|^quarterbackpasslonglob"),
            new Rule(ArtKeys.QbThrowLobMedium, @"pass_medium_lob"),
            new Rule(ArtKeys.QbThrowLobShort, @"pass_short_lob|^quarterbackpassshortlob"),
            new Rule(ArtKeys.QbThrowTouch, @"touch_pass"),
            new Rule(ArtKeys.QbThrowJump, @"jump_pass"),
            new Rule(ArtKeys.QbThrowOffbalance, @"offbalance_pass"),
            new Rule(ArtKeys.QbThrowSidearm, @"sidearm_pass|siderman_pass"),
            new Rule(ArtKeys.QbThrowShovel, @"shovel_pass"),
            new Rule(ArtKeys.QbThrowAway, @"throwaway"),
            new Rule(ArtKeys.QbPumpFake, @"pumpfake"),
            new Rule(ArtKeys.QbFakeHandoff, @"fakehandoff"),
            new Rule(ArtKeys.QbHailMary, @"hail_mary"),
            new Rule(ArtKeys.QbSack, @"falling_throw.*sack|^sack(wrap|broken)"),
            new Rule(ArtKeys.QbFallingThrow, @"falling_throw"),
            new Rule(ArtKeys.QbScramble, @"scramble|ready_to_run_forward|run_forward_pass|ready_to_run_00[2-5]"),
            new Rule(ArtKeys.QbRunLeft, @"run_left_pass|ready_to_run_left"),
            new Rule(ArtKeys.QbRunRight, @"run_right_pass|ready_to_run_right"),
            new Rule(ArtKeys.QbSidestep, @"sidestep"),
            new Rule(ArtKeys.QbHesitation, @"hesitation"),
            new Rule(ArtKeys.QbLookAround, @"lookaround"),
            new Rule(ArtKeys.QbSneak, @"qb_sneak"),
            new Rule(ArtKeys.QbKneel, @"qb_kneel"),
            new Rule(ArtKeys.QbSlide, @"qb_slide"),
            new Rule(ArtKeys.QbSpike, @"qb_spike"),
            new Rule(ArtKeys.QbToss, @"qb_toss"),
            new Rule(ArtKeys.QbDive, @"qb_dive"),
            new Rule(ArtKeys.QbCadence, @"qb_cadence"),
            new Rule(ArtKeys.QbReadyPass, @"ready_pass_to_pass|ready_to_confident"),
            new Rule(ArtKeys.QbThrow, @"^(bones_)?qb_shotgun_pass|^qb_ready_pass"),
            // ---- OL / DL
            new Rule(ArtKeys.OlRunBlock, @"oline_man_3_stance_to_block_run_foot_work"),
            new Rule(ArtKeys.OlPassSet, @"^block(hit|drive)(c|r)stale|^ot_vs_lb"),
            new Rule(ArtKeys.OlStance, @"^3pstance_aggressive|^olinecenterpoints|^olinetransition3ptr"),
            new Rule(ArtKeys.OlBlockEngage, @"^blockhit(c|r)pass"),
            new Rule(ArtKeys.OlBlockPush, @"^block(push|drive|shove)(c|r)pass"),
            new Rule(ArtKeys.OlBlockCut, @"^blockcut"),
            new Rule(ArtKeys.OlBlockFail, @"^block.*fail"),
            new Rule(ArtKeys.OlPulling, @"^blocksiderun"),
            new Rule(ArtKeys.DlRushSwim, @"dline_swim|^blockswim"),
            new Rule(ArtKeys.DlRushSpin, @"^blockspin"),
            new Rule(ArtKeys.DlBullRush, @"dline_bullrush|^blockbull"),
            new Rule(ArtKeys.DlShed, @"reachblockshed"),
            new Rule(ArtKeys.DlWrongArm, @"wrong_arm"),
            new Rule(ArtKeys.DlStance, @"^dlineidle3pt"),
            new Rule(ArtKeys.DlMove, @"^dline(shift|transition)"),
            // ---- catches / coverage
            new Rule(ArtKeys.CatchContested, @"contestedcatch.*success"),
            new Rule(ArtKeys.CatchFail, @"contestedcatch.*fail|^fail_catch"),
            new Rule(ArtKeys.CatchJump, @"^catchnormal.*jump"),
            new Rule(ArtKeys.CatchHigh, @"^catchnormal.*high"),
            new Rule(ArtKeys.CatchLow, @"^catchnormal.*low"),
            new Rule(ArtKeys.CatchSide, @"^catchnormalright"),
            new Rule(ArtKeys.CatchRun, @"^catch_run|^catchsnap|^catchnormal.*mid"),
            new Rule(ArtKeys.FairCatch, @"fair_?catch|^catchfair"),      // v0.13 (ANIMLIB-BULK): + the Motus CatchFairFrontCMid take
            new Rule(ArtKeys.Interception, @"^intnormal"),
            new Rule(ArtKeys.ManCoverage, @"man_coverage|^cornerbackshuffle"),
            new Rule(ArtKeys.ManTurnRun, @"jog_to_jog"),
            new Rule(ArtKeys.RouteCut, @"^receivertransition"),
            // ---- ball carrier
            new Rule(ArtKeys.JukeBox, @"boxstep"),
            new Rule(ArtKeys.RockerStep, @"rockerstep"),
            new Rule(ArtKeys.JukeTempo, @"juke_tempo"),
            new Rule(ArtKeys.Spin, @"spindodge|^avoid(135|180)"),
            new Rule(ArtKeys.JukeLeft, @"juke.*left"),
            new Rule(ArtKeys.JukeRight, @"^avoid(45|90)degr|juke.*right"),
            new Rule(ArtKeys.StiffArmRight, @"^ballcarrierstiffarmr$"),
            new Rule(ArtKeys.StiffArmLeft, @"^ballcarrierstiffarmrswitch"),
            new Rule(ArtKeys.StiffArm, @"stiff_?arm"),
            new Rule(ArtKeys.Hurdle, @"hurdle"),
            new Rule(ArtKeys.DiveForward, @"^dive(short|stretch)withball|avoid0degdive"),
            new Rule(ArtKeys.Truck, @"tacklebroken.*omo"),
            new Rule(ArtKeys.Handoff, @"^handoff"),
            // ---- tackles
            new Rule(ArtKeys.TackleBigHit, @"bighit"),
            new Rule(ArtKeys.TackleDive, @"^tacklewrap.*low.*dmo"),
            new Rule(ArtKeys.TackleWrap, @"^tacklewrap.*dmo"),
            new Rule(ArtKeys.BeTackled, @"^tacklewrap.*omo"),
            new Rule(ArtKeys.TackleArm, @"^tacklebroken.*dmo"),
            new Rule(ArtKeys.TackleStrip, @"striptackle"),
            new Rule(ArtKeys.Gang, @"^sackwrap"),
            new Rule(ArtKeys.Fall, @"^collision"),
            new Rule(ArtKeys.Injury, @"ambient_injury"),
            // ---- celebrations
            new Rule(ArtKeys.CelebrateTd, @"celebrationtouchdown|touchdown_celebrations"),
            new Rule(ArtKeys.CelebrateQb, @"qb_celebration"),
            new Rule(ArtKeys.Cheer, @"cheer|^celebrationbig"),
            new Rule(ArtKeys.Spike, @"spike"),
            new Rule(ArtKeys.Frustration, @"frustrat"),
            new Rule(ArtKeys.IdleConversation, @"idles_conversations"),
            new Rule(ArtKeys.Dance, @"breakdance|freestyle"),
            new Rule(ArtKeys.Salute, @"soldierboy"),
            // ---- kicking
            new Rule(ArtKeys.Punt, @"^punt"),     // QA-192: a celebration "downpunt" is not a punt
            new Rule(ArtKeys.KickOff, @"^kickoff"),
            new Rule(ArtKeys.KickFieldGoal, @"^fieldgoal|^kick_fg"),
            // ---- refs: real BGS referee gestures / locomotion (Characters/Referee/Rigged/Referee_Gestures.fbx) + keyframed ones (Coach_Anims.fbx)
            new Rule(ArtKeys.RefSignalTd, @"^ref_.*touchdown"),
            new Rule(ArtKeys.RefSignalFirstDown, @"^ref_.*first"),
            new Rule(ArtKeys.RefSignalPenalty, @"^ref_gesture_(personalfoul|illegalcontact|falsestart|delayofgame|lossofdown)|^ref_.*penalty"),
            new Rule(ArtKeys.RefSignalIncomplete, @"^ref_.*incomplete"),
            new Rule(ArtKeys.RefSignalTimeout, @"^ref_.*timeout"),
            new Rule(ArtKeys.RefSignalSafety, @"^ref_gesture_safety"),
            new Rule(ArtKeys.RefSignalDeadBall, @"^ref_gesture_deadball"),
            new Rule(ArtKeys.RefBallReady, @"^ref_gesture_ballready"),
            new Rule(ArtKeys.RefWhistle, @"^ref_.*whistle"),
            new Rule(ArtKeys.RefSpotBall, @"^ref_.*spot"),
            new Rule(ArtKeys.RefIdle, @"^ref_idle"),
            new Rule(ArtKeys.RefRun, @"^ref_jog_forward|^ref_runf_loop"),
            new Rule(ArtKeys.RefBackpedal, @"^ref_jog_backward"),
            new Rule(ArtKeys.RefWalk, @"^ref_walkf_loop"),
            new Rule(ArtKeys.RefWalkBack, @"^ref_walkb_loop"),
            new Rule(ArtKeys.RefShuffleLeft, @"^ref_shuffle_left"),
            new Rule(ArtKeys.RefShuffleRight, @"^ref_shuffle_right"),
            // ---- sideline coaches (keyframed in Blender)
            new Rule(ArtKeys.CoachClap, @"^coach_clap"),
            new Rule(ArtKeys.CoachHeadsetIdle, @"^coach_headset_idle"),
            new Rule(ArtKeys.CoachClipboardIdle, @"^coach_clipboard_idle"),
        };

        // Secondary keys: a clip that already won a primary rule can also be offered under these (gameplay-facing synonyms).
        static readonly Rule[] AliasRules =
        {
            // v0.10.0 ANIMLIB: wider pools for the variety slots (additional keys; the clips keep their original key as well)
            new Rule("celebrate_big", @"^celebration(big|fumble|interception|swat|blockkick|downpunt)(?!.*remap)", null, 24),
            new Rule("celebrate_td_all", @"^celebrationtouchdown(?!.*remap)", null, 24),
            new Rule("react_frustration", @"^frustration|^loosing_frustration", null, 16),
            // v0.4 contact pairs: all authored two-actor takes (the A actor under the key, the B actor under <key>_b)
            new Rule(ArtKeys.ContactTackle, @"^tackle(wrap|broken)", null, 400),
            new Rule(ArtKeys.ContactSack, @"^sack(wrap|broken)", null, 400),
            new Rule(ArtKeys.ContactBigHit, @"^bighit", null, 100),
            new Rule(ArtKeys.ContactCollision, @"^collision.*_(a|b)$", null, 60),
            new Rule(ArtKeys.JukeRight, @"^avoid(45|90)degr|boxstepright|rockerstepright", null, 6),
            new Rule(ArtKeys.CatchSide, @"^catchnormalright", null, 6),
            new Rule(ArtKeys.TackleSuccess, @"bighit1_(critical)?success", null, 6),
            new Rule(ArtKeys.TackleFail, @"bighit1_(critical)?failure", null, 6),
            new Rule(ArtKeys.BeTruck, @"^tacklebroken.*dmo", null, 6),
            new Rule(ArtKeys.CelebrateTd2, @"touchdown_celebrations_02|celebrationtouchdownfreestyle", null, 4),
            new Rule(ArtKeys.CelebrateTd3, @"touchdown_celebrations_03|celebrationtouchdownheismanpose|celebrationtouchdownhushcrowd", null, 4),
            new Rule(ArtKeys.Spike, @"spike", null, 3),
            new Rule(ArtKeys.Salute, @"soldierboy|kneelreverently", null, 3),
            new Rule(ArtKeys.StanceHalfback, @"^(2|3)pstance_halfback", null, 3),
            new Rule(ArtKeys.StanceSafety, @"^2pstance_safety", null, 3),
            new Rule(ArtKeys.DlStance, @"^dlineidle", null, 6),
            new Rule(ArtKeys.RouteCut, @"^receivertransition|^avoid(45|90)degr", null, 6),
            new Rule(ArtKeys.RouteBreak, @"^avoid(135|180)degr|^trans_jog_to_jog", null, 6),
            new Rule(ArtKeys.ManTurnRun, @"jog_to_jog|^transition_stand_idle_to_run", null, 6),
            new Rule(ArtKeys.Fumble, @"^bgs_fumble_recovery_dive", null, 3),     // QA-194/916: fumble = the recovery dive (celebrations stay celebrations)
            new Rule(ArtKeys.ShuffleSlow, @"^slow_shuffle_loop", null, 4),
            new Rule(ArtKeys.Snapper, @"^long_snap", null, 4),     // QA-190/917: the centre snap (derived), not a QB fake handoff
            new Rule(ArtKeys.CatchDive, @"^bgs_dive_ball|^dive(short|stretch)withball|^avoid0degdive", null, 4),     // QA-915: a diving catch lays out, it is not a vertical jump
            new Rule(ArtKeys.InjuredIdle, @"ambient_injury_00[12]_a", null, 2),
            new Rule(ArtKeys.DlRushRip, @"^blockescape(c|r)(stale|fail)_(a|b)$", null, 6),     // rip = the escape moves (v0.13 ANIMLIB-BULK: the _B actors too: they register under dl_rush_rip_b)
            new Rule(ArtKeys.PressJam, @"^blockhit(c|r)stale_(a|b)$", null, 4),                  // press jam = the first punch of the hit-block take
            new Rule(ArtKeys.PushOob, @"^block(shove|push)(c|r)(stale|pass)_(a|b)$", null, 4),             // push out of bounds = the shove take
            // v0.8 distinct per-position stances (QA-899..901). The mirrored copy ("_m") of a hand-down stance is the other-handed variant.
            new Rule(ArtKeys.StanceOl3ptR, @"^3pstance_aggressive_00[12](_m)?$", null, 4) { Raw = true },
            new Rule(ArtKeys.StanceOl3ptL, @"^3pstance_aggressive_00[12]_m$", null, 4) { Raw = true },
            new Rule(ArtKeys.StanceCenter, @"^3pstance_aggressive_002(_m)?$", null, 2) { Raw = true },
            new Rule(ArtKeys.StanceSnapper, @"^3pstance_aggressive_001(_m)?$", null, 2) { Raw = true },
            new Rule(ArtKeys.StanceTe, @"^3pstance_halfback_001(_m)?$|^3pstance_aggressive_001(_m)?$", null, 4) { Raw = true },
            new Rule(ArtKeys.StanceDl3pt, @"^dlineidle3ptr(v2)?(_m)?$", null, 6) { Raw = true },
            new Rule(ArtKeys.StanceDl4pt, @"^3pstance_wide_defence_001(_m)?$|^dlineidle3ptrv2(_m)?$", null, 4) { Raw = true },
            new Rule(ArtKeys.StanceWide9, @"^3pstance_wide_defence_001(_m)?$", null, 2) { Raw = true },
            new Rule(ArtKeys.StanceLb, @"^linebackerblitzidleone$|^2pstance_safety_001(_m)?$", null, 4) { Raw = true },
            new Rule(ArtKeys.StanceCbPress, @"^bgs_cb_idle_m2m(_m)?$", null, 2) { Raw = true },
            new Rule(ArtKeys.StanceCbOff, @"^bgs_cb_idle_m2m(_m)?$|^2pstance_safety_001(_m)?$", null, 4) { Raw = true },
            new Rule(ArtKeys.StanceS, @"^2pstance_safety_001(_m)?$", null, 2) { Raw = true },
            new Rule(ArtKeys.StanceWr, @"^bgs_fix_mcu_am_ready_idle_01$|^bgs_fix_mcu_am_ready_idle_03_lookaround$|^2pstance_halfback_001(_m)?$", null, 4) { Raw = true },
            new Rule(ArtKeys.StanceRb, @"^2pstance_halfback_001(_m)?$|^3pstance_halfback_001(_m)?$", null, 4) { Raw = true },
            new Rule(ArtKeys.StanceKicker, @"^player_standing_idle(_2)?$", null, 2) { Raw = true },
            new Rule(ArtKeys.StanceHolder, @"^player_standing_idle(_2)?$", null, 2) { Raw = true },
            new Rule(ArtKeys.ZoneDrop, @"^bgs_cb_shuffleback(left|right)?$|^quarterbackshufflebkwd", null, 6),     // QA-914: a zone drop is a backpedal / shuffle, not a crowd-pump gesture
            new Rule(ArtKeys.Gang, @"^sackwrap(back|front|left|right)", null, 8),                                   // gang tackle = the wrap-up takes
            new Rule(ArtKeys.LbReadStep, @"^bgs_cb_stance_rotate_front23", null, 2),
            new Rule(ArtKeys.Backpedal45, @"^bgs_cb_shuffleback(left|right)$|^mob1_jog_b(l|r)_bkpd_loop", null, 8),
            new Rule(ArtKeys.QbHandSignal, @"^bgs_fix_mcu_am_stand_idle_(11_point|09_fingerguns)", null, 4),
            new Rule(ArtKeys.HuddleBreak, @"^bgs_qb_cadence_03_clap$", null, 2),
            new Rule(ArtKeys.GetUp, @"^getup_(face_up|face_down|side)", null, 12),
            // QA-191/916: no get_up registration from running stops / injuries; get-ups are played by ContactDirector from the pair takes (contact table windows)
        };

        static readonly string[] AllClipKeys = typeof(ArtKeys).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()).ToArray();

        static string Snake(string s)
        {
            s = Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
            return s;
        }

        static string NormalizeClip(string name)
        {
            string n = name;
            int bar = n.LastIndexOf('|'); if (bar >= 0) n = n.Substring(bar + 1);
            int dd = n.IndexOf("__"); if (dd >= 0) n = n.Substring(dd + 2);
            n = n.ToLowerInvariant().Replace("import_", "").Trim();
            return n;
        }

        static int SourcePriority(string path)
        {
            // Motus first for cycles, BoNES clean next
            if (path.Contains("/Animations/Motus/")) return 0;
            if (path.Contains("/Animations/BoNES_QB")) return 1;
            if (path.Contains("/Animations/BoNES_Studio")) return 2;
            if (path.Contains("/Animations/BoNES_Retargets")) return 3;
            if (path.Contains("/Animations/BoNES_Yan")) return 4;
            if (path.Contains("/Animations/Clean/")) return 0;
            return 4;
        }

        [MenuItem("PIX Football/Assets/Build ArtLibrary")]
        public static void Build()
        {
            Directory.CreateDirectory("Assets/_Licensed/Resources");
            var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(LibPath);
            if (lib == null) { lib = ScriptableObject.CreateInstance<ArtLibrary>(); AssetDatabase.CreateAsset(lib, LibPath); }
            lib.clips.Clear(); lib.prefabs.Clear(); lib.materials.Clear(); lib.sprites.Clear(); lib.audio.Clear(); lib.textures.Clear(); lib.fonts.Clear();

            BuildClips(lib);
            BuildPrefabs(lib);
            BuildMaterials(lib);
            BuildSprites(lib);
            BuildAudio(lib);
            BuildFonts(lib);
            PhotoPlayerBuilder.AddArtLibraryKeys(lib);   // photo_player_small / medium / large (Assets/_Licensed/PhotorealPlayer, additive only)

            // shared avatar + controller
            // the shared avatar is the Medium one (kept for tools); every player prefab animates with the Animator avatar of its OWN body FBX (QA-952)
            var body = AssetDatabase.LoadAllAssetsAtPath(CharacterSetup.BodyPaths[1]).OfType<Avatar>().FirstOrDefault();
            if (body == null) Debug.LogError("[ArtLibraryBuilder] Medium body avatar missing: lib.playerAvatar stays null");
            foreach (string bp in CharacterSetup.BodyPaths) if (!HumanoidMapper.IsGoodAvatar(bp)) Debug.LogError("[ArtLibraryBuilder] body avatar invalid: " + bp);
            lib.playerAvatar = body;
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();

            // copy into Resources for the runtime fallback
            if (AssetDatabase.LoadAssetAtPath<ArtLibrary>(ResPath) != null) AssetDatabase.DeleteAsset(ResPath);
            AssetDatabase.CopyAsset(LibPath, ResPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Report(lib);
        }

        static void BuildClips(ArtLibrary lib)
        {
            var candidates = new List<KeyValuePair<string, AnimationClip>>();
            var clipRoots = new List<string> { Root + "/Animations" };
            foreach (string extra in new[] { Root + "/Characters/Referee/Rigged", Root + "/Characters/Coaches/Rigged" })
                if (AssetDatabase.IsValidFolder(extra)) clipRoots.Add(extra);
            foreach (string g in AssetDatabase.FindAssets("t:Model", clipRoots.ToArray()))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (path.EndsWith("Motus_Football_Player_A_01.fbx")) continue;
                foreach (var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (c.name.StartsWith("__preview__")) continue;
                    candidates.Add(new KeyValuePair<string, AnimationClip>(path, c));
                }
            }
            // standalone arm-fitted clips (ArmFit) replace the same-named FBX clips
            string fittedDir = Root + "/Characters/Coaches/Rigged/Clips";
            if (AssetDatabase.IsValidFolder(fittedDir))
            {
                var fitted = new List<KeyValuePair<string, AnimationClip>>();
                foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { fittedDir }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
                    if (c != null) fitted.Add(new KeyValuePair<string, AnimationClip>(p, c));
                }
                var names = new HashSet<string>(fitted.Select(f => f.Value.name));
                candidates.RemoveAll(k => k.Key.Contains("/Coaches/Rigged/") && names.Contains(k.Value.name));
                candidates.AddRange(fitted);
            }
            // v0.8: cleaned clips (ClipCleaner / derived builders) replace the same-named originals; clips that only exist as .anim are added
            string cleanDir = Root + "/Animations/Clean";
            if (AssetDatabase.IsValidFolder(cleanDir))
            {
                var cleanMap = new Dictionary<string, KeyValuePair<string, AnimationClip>>();
                foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { cleanDir }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
                    if (c != null) cleanMap[c.name] = new KeyValuePair<string, AnimationClip>(p, c);
                }
                var used = new HashSet<string>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    KeyValuePair<string, AnimationClip> cl;
                    if (cleanMap.TryGetValue(candidates[i].Value.name, out cl)) { candidates[i] = cl; used.Add(cl.Value.name); }
                }
                // a long raw take that was cut into segments is replaced by them
                candidates.RemoveAll(k => !k.Key.Contains("/Clean/") && cleanMap.ContainsKey(ClipCleaner.TaggedName(k.Value.name, "_s01")));
                foreach (var kv in cleanMap) if (!used.Contains(kv.Key)) candidates.Add(kv.Value);
                // v0.13 (BULK-2): a whole-take pair clip (RAW__*_A / _B) whose interaction windows exist (name_s01_A ...) is replaced by them: a 20 s pair never books and would fill the key
                candidates.RemoveAll(k => k.Key.Contains("/Clean/") && k.Value.name.StartsWith("RAW__") && Regex.IsMatch(k.Value.name, "_[AB]$") && !Regex.IsMatch(k.Value.name, @"_s\d\d(_M)?_[AB]$") && cleanMap.ContainsKey(ClipCleaner.TaggedName(k.Value.name, "_s01")));
            }
            // v0.10.0 ANIMLIB: clips the QA score rejected (Docs/QA/v0100/animlib_rejects.txt) are not registered
            var rejects = LoadRejects();
            if (rejects.Count > 0) candidates.RemoveAll(k => rejects.Contains(k.Value.name));
            // order: source priority, then name
            candidates = candidates.OrderBy(k => SourcePriority(k.Key)).ThenBy(k => NormalizeClip(k.Value.name), StringComparer.Ordinal).ToList();
            var count = new Dictionary<string, int>();
            var seenNames = new HashSet<string>();
            foreach (var kv in candidates)
            {
                string n = NormalizeClip(kv.Value.name); string an = n;     // an keeps the mirrored "_m" tag for the stance alias rules
                string pathLow = kv.Key;
                // skip duplicate normalised names coming from several packs, and namespaced paired "_co" combined clips
                string dedupe = n + "|" + (pathLow.Contains("/Motus/") ? "m" : "b");
                if (!seenNames.Add(dedupe)) continue;
                if (n.EndsWith("_co") || n.Contains("_dude") || n.StartsWith("mediumplayer_rig")) continue;
                // mirrored copies (ClipCleaner "_M") register like their source clip; the left-hand cuts / jukes get their own keys
                if (n.EndsWith("_m"))
                {
                    string mn = n.Substring(0, n.Length - 2);
                    if (Regex.IsMatch(mn, @"^avoid(45|90)degr$"))
                    {
                        foreach (string mk in new[] { ArtKeys.CutLeft, ArtKeys.JukeLeft })
                        {
                            int mc; count.TryGetValue(mk, out mc);
                            if (mc >= 24) continue;      // v0.13 (ANIMLIB-BULK): was 6
                            count[mk] = mc + 1;
                            lib.clips.Add(new ArtLibrary.ClipEntry { key = mk, clip = kv.Value, source = pathLow.Substring(Root.Length + 1) + "#" + kv.Value.name, length = kv.Value.length, loop = kv.Value.isLooping });
                        }
                        continue;
                    }
                    if (Regex.IsMatch(mn, @"^trans(ition)?_stand_idle_to_stand_idle_\d+_deg_right|^transition_walk_to_walk|^transitionwalkidle"))
                    {
                        int tc; count.TryGetValue(ArtKeys.TurnLeft, out tc);
                        if (tc < 16) { count[ArtKeys.TurnLeft] = tc + 1; lib.clips.Add(new ArtLibrary.ClipEntry { key = ArtKeys.TurnLeft, clip = kv.Value, source = pathLow.Substring(Root.Length + 1) + "#" + kv.Value.name, length = kv.Value.length, loop = kv.Value.isLooping }); }
                        continue;
                    }
                    n = mn;
                }
                foreach (var r in BulkRules.Concat(QbRules).Concat(NewClipRules).Concat(ClipRules))      // v0.13: the ANIMLIB-BULK rules (ArtLibraryBuilder.Bulk.cs: the MC idles, the look-arounds the QB pattern would take) and the QB-MOCAP rules (ArtLibraryBuilder.Qb.cs) claim first
                {
                    if (!r.inc.IsMatch(n)) continue;
                    if (r.exc != null && r.exc.IsMatch(n)) continue;
                    string key = r.key;
                    if (Regex.IsMatch(n, @"(_b|_def)$") && !n.EndsWith("_ab") && !SingleActorB.IsMatch(n)) key += "_b";
                    int c; count.TryGetValue(key, out c);
                    if (c >= Cap(r)) { break; }
                    count[key] = c + 1;
                    lib.clips.Add(new ArtLibrary.ClipEntry { key = key, clip = kv.Value, source = pathLow.Substring(Root.Length + 1) + "#" + kv.Value.name, length = kv.Value.length, loop = kv.Value.isLooping });
                    break;
                }
                foreach (var ar in AliasRules)
                {
                    if (!ar.inc.IsMatch(ar.Raw ? an : n)) continue;
                    string akey = ar.key;
                    if (Regex.IsMatch(n, @"(_b|_def)$") && !n.EndsWith("_ab") && !SingleActorB.IsMatch(n)) akey += "_b";
                    int ac; count.TryGetValue(akey, out ac);
                    if (ac >= Cap(ar)) continue;
                    count[akey] = ac + 1;
                    lib.clips.Add(new ArtLibrary.ClipEntry { key = akey, clip = kv.Value, source = pathLow.Substring(Root.Length + 1) + "#" + kv.Value.name, length = kv.Value.length, loop = kv.Value.isLooping });
                }
            }
        }

        static HashSet<string> LoadRejects()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            const string path = "Docs/QA/v0100/animlib_rejects.txt";
            if (File.Exists(path))
                foreach (string l in File.ReadAllLines(path)) { string t = l.Split('	')[0].Trim(); if (t.Length > 0 && !t.StartsWith("#")) set.Add(t); }
            // v0.13 wave 2 (BULK-2): the engagement takes that sink the hips / put a hand under the turf / float both feet (EngageProbe)
            if (File.Exists("Docs/QA/v0130/bulk2_engage_rejects.txt"))
                foreach (string l in File.ReadAllLines("Docs/QA/v0130/bulk2_engage_rejects.txt")) { string t = l.Split('	')[0].Trim(); if (t.Length > 0 && !t.StartsWith("#")) set.Add(t); }
            return set;
        }

        static void BuildPrefabs(ArtLibrary lib)
        {
            string dir = Root + "/Prefabs";
            if (!AssetDatabase.IsValidFolder(dir)) return;
            foreach (string g in AssetDatabase.FindAssets("t:Prefab", new[] { dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go == null) continue;
                string k = Snake(go.name);
                if (k.StartsWith("pix_")) k = k.Substring(4);
                // the rigged humanoid referee owns the `referee` key; the old static T-pose mesh is kept as `referee_static`
                if (k == "referee") k = "referee_static";
                else if (k == "referee_rigged") k = "referee";
                lib.prefabs.Add(new ArtLibrary.PrefabEntry { key = k, prefab = go });
            }
        }

        static void BuildMaterials(ArtLibrary lib)
        {
            string dir = Root + "/Materials";
            if (!AssetDatabase.IsValidFolder(dir)) return;
            foreach (string g in AssetDatabase.FindAssets("t:Material", new[] { dir }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null) continue;
                string k = Snake(m.name); if (k.StartsWith("pix_")) k = k.Substring(4);
                lib.materials.Add(new ArtLibrary.MaterialEntry { key = k, material = m });
            }
        }

        static void BuildSprites(ArtLibrary lib)
        {
            AddSprites(lib, Root + "/UI/InGame", p => "ui_" + Snake(Path.GetFileNameWithoutExtension(p) + "_" + Path.GetFileName(Path.GetDirectoryName(p))));
            AddSprites(lib, Root + "/UI/RankBadges", p => "badge_" + Snake(Path.GetFileNameWithoutExtension(p)));
            AddSprites(lib, Root + "/UI/Logos/Pro", p => "logo_pro_" + Snake(Path.GetFileNameWithoutExtension(p)));
            AddSprites(lib, Root + "/UI/Logos/College", p => "logo_college_" + Snake(Path.GetFileNameWithoutExtension(p)));
            // atlases stay textures (single-sprite import); expose them as textures + sprites
            foreach (string g in AssetDatabase.FindAssets("t:Texture2D", new[] { Root + "/UI/Atlases" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (t != null) lib.textures.Add(new ArtLibrary.TextureEntry { key = "atlas_" + Snake(Path.GetFileNameWithoutExtension(p)), texture = t });
            }
        }

        static void AddSprites(ArtLibrary lib, string folder, Func<string, string> keyOf)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return;
            var used = new HashSet<string>();
            foreach (string g in AssetDatabase.FindAssets("t:Sprite", new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var s = AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>().FirstOrDefault();
                if (s == null) continue;
                string k = keyOf(p);
                if (!used.Add(k)) continue;
                lib.sprites.Add(new ArtLibrary.SpriteEntry { key = k, sprite = s });
            }
        }

        static readonly KeyValuePair<string, string>[] AudioMap =
        {
            new KeyValuePair<string, string>("/SFX/Footsteps/Grass", "footstep_grass"),
            new KeyValuePair<string, string>("/SFX/Footsteps/Wet", "footstep_wet"),
            new KeyValuePair<string, string>("/SFX/Footsteps/Dirt", "footstep_dirt"),
            new KeyValuePair<string, string>("/SFX/Footsteps/Snow", "footstep_snow"),
            new KeyValuePair<string, string>("/SFX/Footsteps/Concrete", "footstep_concrete"),
            new KeyValuePair<string, string>("/SFX/Footsteps/Wood", "footstep_wood"),
            new KeyValuePair<string, string>("/SFX/Footsteps/Carpet", "footstep_carpet"),
            new KeyValuePair<string, string>("/SFX/Ball Bounce/Ball_Bounce_wet_grass", "ball_bounce_wet"),
            new KeyValuePair<string, string>("/SFX/Ball Bounce/Ball_Bounce_snow_grass", "ball_bounce_snow"),
            new KeyValuePair<string, string>("/SFX/Ball Bounce", "ball_bounce"),
            new KeyValuePair<string, string>("/SFX/Ball hit", "ball_hit"),
            new KeyValuePair<string, string>("/SFX/Cath ball", "ball_catch"),
            new KeyValuePair<string, string>("Client_SFX_Catch_ball", "ball_catch"),
            new KeyValuePair<string, string>("/SFX/Hits/Hard_hitting_tackles", "tackle_hit_hard"),
            new KeyValuePair<string, string>("/SFX/Hits", "tackle_hit"),
            new KeyValuePair<string, string>("Client_SFX_Tackle_single", "tackle_hit"),
            new KeyValuePair<string, string>("Client_SFX_Tackle", "tackle_hit_hard"),
            new KeyValuePair<string, string>("Client_Tackle_Grunt", "tackle_grunt"),
            new KeyValuePair<string, string>("Client_SFX_Whoosh", "whoosh"),
            new KeyValuePair<string, string>("/SFX/Kicks", "kick"),
            new KeyValuePair<string, string>("/SFX/Umpire whistle", "whistle"),
            new KeyValuePair<string, string>("/SFX/Scout_booster", "ui_booster"),
            new KeyValuePair<string, string>("/SFX/Team screen", "ui_card_flip"),
            new KeyValuePair<string, string>("/SFX/UI/Notifications", "ui_notification"),
            new KeyValuePair<string, string>("/SFX/UI/Play clock", "ui_play_clock"),
            new KeyValuePair<string, string>("/SFX/UI/Popups sfx", "ui_popup"),
            new KeyValuePair<string, string>("/SFX/UI/Purchase", "ui_purchase"),
            new KeyValuePair<string, string>("/SFX/UI/Zoom", "ui_zoom"),
            new KeyValuePair<string, string>("/SFX/UI", "ui_click"),
            new KeyValuePair<string, string>("/SFX/ClientAssets", "sfx_client_misc"),
            new KeyValuePair<string, string>("/Ambience/Crowds/Crowd Cheer", "crowd_cheer"),
            new KeyValuePair<string, string>("/Ambience/Crowds", "crowd_ambience"),
            new KeyValuePair<string, string>("Ambience_crowd_loading", "crowd_loading"),
            new KeyValuePair<string, string>("/Ambience/Rain", "ambience_rain"),
            new KeyValuePair<string, string>("/Ambience/Winds", "ambience_wind"),
            new KeyValuePair<string, string>("/Ambience/Locker Room", "ambience_locker_room"),
            new KeyValuePair<string, string>("/Music/DraftFeverBowlOST", "music_stadium_anthem"),
            new KeyValuePair<string, string>("/Music/DraftHouseOST", "music_menu"),
            new KeyValuePair<string, string>("/Music/PeytonParrish", "music_pp"),
        };

        static void BuildAudio(ArtLibrary lib)
        {
            foreach (string g in AssetDatabase.FindAssets("t:AudioClip", new[] { Root + "/Audio" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
                if (clip == null) continue;
                string key = null;
                foreach (var m in AudioMap) if (p.Contains(m.Key)) { key = m.Value; break; }
                if (key == null) key = "sfx_" + Snake(Path.GetFileName(Path.GetDirectoryName(p)));
                if (key == "music_pp") key = "music_pp_" + Snake(Path.GetFileNameWithoutExtension(p));
                lib.audio.Add(new ArtLibrary.AudioEntry { key = key, clip = clip });
            }
        }

        static void BuildFonts(ArtLibrary lib)
        {
            foreach (string g in AssetDatabase.FindAssets("t:Font", new[] { Root + "/Fonts" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                var f = AssetDatabase.LoadAssetAtPath<Font>(p);
                if (f != null) lib.fonts.Add(new ArtLibrary.FontEntry { key = "font_" + Snake(Path.GetFileNameWithoutExtension(p)), font = f });
            }
        }

        static void Report(ArtLibrary lib)
        {
            lib.BuildMaps();
            var sb = new StringBuilder();
            sb.AppendLine("# ArtLibrary key report");
            sb.AppendLine();
            sb.AppendLine("Generated by `PIX Football/Assets/Build ArtLibrary`. Counts are variants per key. Paired (two-actor) clips register the second actor as `<key>_b`.");
            sb.AppendLine();
            sb.AppendLine("clips=" + lib.clips.Count + " prefabs=" + lib.prefabs.Count + " materials=" + lib.materials.Count + " sprites=" + lib.sprites.Count + " audioClips=" + lib.audio.Count + " textures=" + lib.textures.Count + " fonts=" + lib.fonts.Count);
            sb.AppendLine();
            sb.AppendLine("| animation key | variants | example clip |");
            sb.AppendLine("|---|---|---|");
            var missing = new List<string>();
            int found = 0;
            foreach (string k in AllClipKeys)
            {
                int n = lib.ClipVariantCount(k);
                if (n == 0) { missing.Add(k); continue; }
                found++;
                var ex = lib.clips.First(c => c.key == k);
                sb.AppendLine("| " + k + " | " + n + " | " + ex.clip.name + " (" + ex.source.Split('#')[0] + ") |");
            }
            var extra = lib.clips.Select(c => c.key).Where(k => !AllClipKeys.Contains(k)).Distinct().OrderBy(k => k).ToList();
            sb.AppendLine();
            sb.AppendLine("**Animation keys found: " + found + " / " + AllClipKeys.Length + "**");
            sb.AppendLine();
            sb.AppendLine("Second-actor keys (`_b`): " + string.Join(", ", extra.ToArray()));
            sb.AppendLine();
            sb.AppendLine("**MISSING animation keys (" + missing.Count + "):** " + string.Join(", ", missing.ToArray()));
            sb.AppendLine();
            sb.AppendLine("Prefab keys: " + string.Join(", ", lib.PrefabKeys.OrderBy(k => k).ToArray()));
            sb.AppendLine();
            var ak = lib.AudioKeys.OrderBy(k => k).Select(k => k + "(" + lib.AudioVariantCount(k) + ")");
            sb.AppendLine("Audio keys: " + string.Join(", ", ak.ToArray()));
            sb.AppendLine();
            sb.AppendLine("Material keys: " + string.Join(", ", lib.MaterialKeys.OrderBy(k => k).ToArray()));
            sb.AppendLine();
            sb.AppendLine("Sprite keys: " + lib.sprites.Count + " (ui_*, badge_*, logo_pro_*, logo_college_*)");
            File.WriteAllText("Docs/ArtLibraryReport.md", sb.ToString());
            Debug.Log("[ArtLibrary] animation keys found " + found + "/" + AllClipKeys.Length + ", missing: " + string.Join(", ", missing.ToArray()));
        }
    }
}
