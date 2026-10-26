using PixFootball.Art;

namespace PixFootball.EditorTools
{
    public static partial class ArtLibraryBuilder
    {
        /// <summary>v0.13 (ANIMLIB-BULK): the non-quarterback rules that must claim BEFORE <see cref="QbRules"/> (the QB rules use unanchored patterns such as <c>lookaround</c>, which also match the studio idle
        /// look-arounds and the MoCap Central idles). Same cap rules as the others (<see cref="Cap"/>).</summary>
        static readonly Rule[] BulkRules =
        {
            new Rule(ArtKeys.IdleLook, @"^bgs_fix_mcu_am_.*lookaround", null, 6),
            // the day-3 China-route juke finishes (BoNES, re-baked): carrier moves with a tempo change, pooled with juke_tempo
            new Rule("juke_tempo", @"^ball_carrier_juke_china_route", null, 24),
            // v0.13 wave 2: the contested-catch pairs, cut into their interaction windows (name_s01_A ...): up to 3 windows of 22 pairs
            new Rule(ArtKeys.CatchContested, @"contestedcatch.*success", null, 80),
            // the MoCap Central idle pack (MC__ prefix, DraftFever/Animations/MC_Idles): sideline, huddle, warm-up and cheer ambient for the stage scenes
            new Rule("idle_mc_look", @"^stand_idle_waiting_0[4-8]|^(armscrossed|armscrossedb|handhipl|handhipr|handsclaspfront|handship)_idle_\d\d_lookaround|^stand_idle_shift[lr]_loop_\d\d_look", null, 32),
            new Rule("idle_mc_stand", @"^(armscrossed|armscrossedb|handhipl|handhipr|handsclaspfront|handship)_idle_\d\d|^stand_idle_shift[lr]_loop", null, 64),
            new Rule("idle_mc_tired", @"^stand_idle_breathe|^stand_tired_0[1256]|^stand_bodyache", null, 32),
            new Rule("idle_mc_fidget", @"^stand_(fidget_0[1-7]|brushoff|greet_0[2456])", null, 32),
            new Rule("warmup_stretch", @"^standwide_stretch", null, 16),
            // v0.13 wave 2: the day-3 OLINE_MAN_3 stance-to-block footwork takes (24 more, single actor): the run-block opening pool
            new Rule(ArtKeys.OlRunBlock, @"oline_man_3_stance_to_block_run_foot_work", null, 64),
            new Rule("celebrate_td_mc", @"^mc__celebrate_(dancebasic|stand_dance)|^celebrate_(dancebasic|stand_dance)", null, 16),
            new Rule("cheer_mc", @"^stand_crowd_(cheer|clap[ab]_01|fistshake|shout)", null, 24),
        };
    }
}
