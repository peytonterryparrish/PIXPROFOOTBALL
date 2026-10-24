using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 ANIMLIB: plans for the owner's mocap that had no caller in v0.10 (quarterback fakes / hail mary / sneak / falling and shovel throws, ball-carrier jukes, stiff arms, cuts and trucks,
    /// reactions, injuries, referee and huddle takes). Same contract as AnimKit.Extra2: an empty plan when the key is not in the library or has no QA-passed clip, so a caller can fall back.
    /// The suggested callers per clip are in Docs/QA/v0110/animlib.md ("clip -> suggested caller"). Pairs (A actor here, B actor in the "_b" key) go through <see cref="AnimKit.TryPair"/>.
    /// </summary>
    public static partial class AnimKit
    {
        /// <summary>Any key through the QA-passed picker. eta greater than 0: the clip's sync moment (release / catch / contact) lands eta seconds from now; otherwise the take plays from its start.</summary>
        public static ClipPlan Mocap(string key, PlayerRole? role, int seed, float maxLen = 6f, float eta = -1f, float tail = 0.9f)
        {
            var c = ClipSelect.Pick(key, role, seed, '?', maxLen);
            if (c == null) return default(ClipPlan);
            if (eta > 0f) return ClipSelect.Synced(c, eta, 0.25f, tail);
            return P(c, 0f, Mathf.Min(c.length, maxLen > 0f ? maxLen : c.length), 1f, 0.2f, 0.3f);
        }

        // ---- quarterback -------------------------------------------------------------------------------------------------------------------------
        /// <summary>Pump fake (play action look, double clutch): release moment tagged.</summary>
        public static ClipPlan QbPumpFake(int seed, float eta = -1f) { return Mocap("qb_pump_fake", PlayerRole.QB, seed, 5f, eta); }
        /// <summary>Fake handoff out of the shotgun / under centre, rolling out (play action); the exchange moment is tagged.</summary>
        public static ClipPlan QbFakeHandoff(int seed, float eta = -1f) { return Mocap("qb_fake_handoff", PlayerRole.QB, seed, 5f, eta); }
        /// <summary>Hail Mary: the long heave off the back foot.</summary>
        public static ClipPlan QbHailMary(int seed, float eta = -1f) { return Mocap("qb_hail_mary", PlayerRole.QB, seed, 6f, eta); }
        /// <summary>Quarterback sneak (A actor; the line push is the B / OL side).</summary>
        public static ClipPlan QbSneak(int seed) { return Mocap("qb_sneak", PlayerRole.QB, seed, 8f); }
        /// <summary>Throw while falling away or being hit (A actor; the B actor is the defender).</summary>
        public static ClipPlan QbFallingThrow(int seed, float eta = -1f) { return Mocap("qb_falling_throw", PlayerRole.QB, seed, 6f, eta); }
        /// <summary>Shovel pass and pitch / toss to the running back.</summary>
        public static ClipPlan QbShovel(int seed, float eta = -1f) { return Mocap("qb_throw_shovel", PlayerRole.QB, seed, 6f, eta); }
        public static ClipPlan QbToss(int seed, float eta = -1f) { return Mocap("qb_toss", PlayerRole.QB, seed, 6f, eta); }
        /// <summary>Throw on the run (rolling out) and the throw as he is going down.</summary>
        public static ClipPlan QbThrowOnRun(int seed, float eta = -1f) { return Mocap("qb_throw_on_run", PlayerRole.QB, seed, 3f, eta); }
        public static ClipPlan QbThrowTackled(int seed, float eta = -1f) { return Mocap("qb_throw_tackled", PlayerRole.QB, seed, 8f, eta); }
        /// <summary>Pocket behaviour: look-around / hesitation / sidestep / ready-to-pass idle, slide and dive for the sideline or the line to gain.</summary>
        public static ClipPlan QbLookAround(int seed) { return Mocap("qb_look_around", PlayerRole.QB, seed, 6f); }
        public static ClipPlan QbHesitation(int seed) { return Mocap("qb_hesitation", PlayerRole.QB, seed, 6f); }
        public static ClipPlan QbSidestep(int seed) { return Mocap("qb_sidestep", PlayerRole.QB, seed, 5f); }
        public static ClipPlan QbReadyPass(int seed) { return Mocap("qb_ready_pass", PlayerRole.QB, seed, 5f); }
        public static ClipPlan QbSlide(int seed) { return Mocap("qb_slide", PlayerRole.QB, seed, 6f); }
        public static ClipPlan QbDive(int seed) { return Mocap("qb_dive", PlayerRole.QB, seed, 6f); }
        /// <summary>Snap catches: under centre (hands up into the centre), shotgun, and the snap into the first dropback steps.</summary>
        public static ClipPlan QbSnapUnder(int seed, float eta = -1f) { return Mocap("qb_snap_under", PlayerRole.QB, seed, 5f, eta); }
        public static ClipPlan QbSnapShotgun(int seed, float eta = -1f) { return Mocap("qb_snap_shotgun", PlayerRole.QB, seed, 6f, eta); }
        /// <summary>Hand signals (finger guns, point) for the huddle break / pre-snap.</summary>
        public static ClipPlan QbHandSignal(int seed) { return Mocap("qb_hand_signal", null, seed, 5f); }

        // ---- ball carrier --------------------------------------------------------------------------------------------------------------------------
        /// <summary>Stiff arm: left / right BGS takes (plant moment tagged) or the studio's HB stiff arm.</summary>
        public static ClipPlan StiffArm(bool left, int seed, float eta = -1f)
        {
            var p = Mocap(left ? "stiff_arm_left" : "stiff_arm_right", null, seed, 4f, eta); if (p.Valid) return p;
            return Mocap("stiff_arm", null, seed, 10f, eta);
        }
        /// <summary>Tempo-change, box-step, rocker-step jukes and the harder plant cuts (long studio takes: the move is a segment of them; use eta to land the plant).</summary>
        public static ClipPlan JukeTempo(int seed, float eta = -1f) { return Mocap("juke_tempo", null, seed, 45f, eta, 1.4f); }
        public static ClipPlan JukeBox(int seed, float eta = -1f) { return Mocap("juke_box", null, seed, 35f, eta, 1.4f); }
        public static ClipPlan RockerStep(int seed, float eta = -1f) { return Mocap("rocker_step", null, seed, 45f, eta, 1.4f); }
        public static ClipPlan CutHard(int seed, float eta = -1f) { return Mocap("cut_hard", null, seed, 4f, eta); }
        public static ClipPlan TruckAlt(int seed, float eta = -1f) { return Mocap("truck_alt", null, seed, 2f, eta); }
        public static ClipPlan Takedown(int seed, float eta = -1f) { return Mocap("takedown", null, seed, 3f, eta); }
        /// <summary>Ankle breaker (juke so hard the defender falls): A actor here, B actor from "ankle_breaker_b".</summary>
        public static bool AnkleBreakerPair(int seed, out AnimationClip a, out AnimationClip b) { return TryPair("ankle_breaker", seed, 6f, out a, out b); }
        public static bool StiffArmPair(int seed, out AnimationClip a, out AnimationClip b) { return TryPair("stiff_arm", seed, 45f, out a, out b); }
        public static bool JukeTempoPair(int seed, out AnimationClip a, out AnimationClip b) { return TryPair("juke_tempo", seed, 45f, out a, out b); }
        public static bool SpinMovePair(int seed, out AnimationClip a, out AnimationClip b) { return TryPair("spin_move", seed, 25f, out a, out b); }
        public static bool WrongArmPair(int seed, out AnimationClip a, out AnimationClip b) { return TryPair("dl_wrong_arm", seed, 25f, out a, out b); }
        public static bool ManCoveragePair(int seed, out AnimationClip a, out AnimationClip b) { return TryPair("man_coverage", seed, 20f, out a, out b); }
        public static bool GangTacklePair(int seed, out AnimationClip a, out AnimationClip b) { return TryPair("tackle_gang", seed, 9f, out a, out b); }
        public static bool StripTacklePair(int seed, out AnimationClip a, out AnimationClip b) { return TryPair("tackle_strip", seed, 60f, out a, out b); }
        public static bool BigHitFailPair(int seed, out AnimationClip a, out AnimationClip b) { return TryPair("tackle_big_hit", seed, 9f, out a, out b); }
        public static bool InjuryPair(int seed, out AnimationClip a, out AnimationClip b) { return TryPair("injury", seed, 12f, out a, out b); }
        public static bool ConversationPair(int seed, out AnimationClip a, out AnimationClip b) { return TryPair("idle_conversation", seed, 30f, out a, out b); }

        // ---- reactions, huddle, officials ---------------------------------------------------------------------------------------------------------
        /// <summary>Quarterback celebration (10 s) and the freestyle dance / salute takes.</summary>
        public static ClipPlan CelebrateQb(int seed) { return Mocap("celebrate_qb", null, seed, 11f); }
        public static ClipPlan Dance(int seed) { return Mocap("dance", null, seed, 5f); }
        public static ClipPlan Salute(int seed) { return Mocap("salute", null, seed, 7f); }
        public static ClipPlan HuddleBreak(int seed) { return Mocap("huddle_break", null, seed, 2f); }
        public static ClipPlan HuddleLeader(int seed) { return Mocap("huddle_leader", null, seed, 4f); }
        public static ClipPlan InjuryIdle(int seed) { return Mocap("injury_idle", null, seed, 60f); }
        public static ClipPlan GetUpSide(int seed) { return Mocap("getup_side", null, seed, 2f); }
        public static ClipPlan RefBallReady(int seed) { return Mocap("ref_ball_ready", null, seed, 2f); }
        public static ClipPlan RefShuffle(bool left, int seed) { return Mocap(left ? "ref_shuffle_left" : "ref_shuffle_right", null, seed, 1f); }
        public static ClipPlan RefWalkBack(int seed) { var p = Mocap("ref_walk_back", null, seed, 2f); p.Loop = true; return p; }

        // ---- v0.11.0 legacy-rig takes (2024 Draft Fever rig, re-baked, single actor) -----------------------------------------------------------------
        /// <summary>Hands-on hold in the trench (OL / DL, looped hold takes) and the win / lose / run-with-the-block variants of an engaged lineman.</summary>
        public static ClipPlan TrenchHold(int seed) { var p = Mocap("trench_hold", null, seed, 4f); p.Loop = true; return p; }
        public static ClipPlan TrenchWin(int seed) { return Mocap("trench_win", null, seed, 3f); }
        public static ClipPlan TrenchLose(int seed) { return Mocap("trench_lose", null, seed, 3f); }
        public static ClipPlan TrenchRun(int seed) { return Mocap("trench_run", null, seed, 2f); }
        /// <summary>Shoulder tackle / jumping tackle (tagger) and the ball carrier going down from it (front / back / side).</summary>
        public static ClipPlan ShoulderTackle(int seed, float eta = -1f) { return Mocap("tackle_shoulder", null, seed, 3f, eta); }
        public static ClipPlan BeTackledShoulder(int seed, float eta = -1f) { return Mocap("be_tackled_shoulder", null, seed, 4f, eta); }
        public static ClipPlan DefStanceFall(int seed) { return Mocap("defstance_fall", null, seed, 2f); }
        public static ClipPlan StiffArmRun(int seed, float eta = -1f) { return Mocap("stiff_arm_run", null, seed, 2f, eta); }

        // ---- special teams, defence --------------------------------------------------------------------------------------------------------------
        /// <summary>Squib kick (low line drive kickoff): kick moment tagged.</summary>
        public static ClipPlan KickSquib(int seed, float eta = -1f) { return Mocap("kick_squib", null, seed, 6f, eta, 1.2f); }
        public static ClipPlan PressJam(int seed, float eta = -1f) { return Mocap("press_jam", null, seed, 4f, eta); }
        public static ClipPlan PushOutOfBounds(int seed, float eta = -1f) { return Mocap("push_oob", null, seed, 4f, eta); }
        public static ClipPlan LbReadStep(int seed) { return Mocap("lb_read_step", PlayerRole.LB, seed, 1f); }
        public static ClipPlan ZoneDrop(int seed) { return Mocap("zone_drop", null, seed, 1f); }
    }
}
