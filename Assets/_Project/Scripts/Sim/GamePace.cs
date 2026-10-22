using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.7 game pace (playtest note 8, "gameplay a touch too fast"; All-Pro 2K8 reference: "realistic speed of game, calculated and clean").
    /// ONE place for the pace of the match. <see cref="Scale"/> is the headline: how fast bodies move relative to the v0.6 engine
    /// (0.88 = 12 percent slower). It is baked into the simulation's world units, so a simulated velocity is already the real on-screen velocity
    /// (yards per real second): locomotion animation can keep driving off <c>SimAgent.Vel</c> unchanged.
    /// Animation that is NOT driven by velocity (throw, tackle, catch, kick, celebration) should play at <see cref="AnimationRate"/> so the
    /// clip's motion keeps pace with the heavier body: <c>animator.speed = clipSpeed * GamePace.AnimationRate</c>.
    /// </summary>
    public static class GamePace
    {
        /// <summary>Overall movement speed relative to v0.6 (1 = old pace). Receivers, backs and quarterbacks use this value; see <see cref="For"/> for the other positions. Averaged over the field the slow-down is about 10 percent; a WR runs the 40 in pads in 5.3 s instead of 4.7 s, top speed 18.7 mph instead of 21.2.</summary>
        public const float Scale = 0.88f;

        /// <summary>Offensive linemen work in a few yards, so their short-yardage quickness is slowed less (the line battle keeps its timing).</summary>
        public const float LineScale = 0.93f;

        /// <summary>Defensive linemen: the pass rush (and the sack rate) is calibrated against the quarterback timing, so they give up less of their speed than the skill players.</summary>
        public const float RushScale = 1.0f;
        /// <summary>Defensive backs and linebackers keep a little more of their speed than the offense: slowing every body by the same amount handed the ball to the offense (28.9 points a team, 7.9 yards an attempt) because the thrown ball did not slow with them. Calibrated with 600-game runs (docs: B-001).</summary>
        public const float DbScale = 0.90f, LbScale = 0.89f;

        /// <summary>Playback rate for animation that is not velocity-driven (non-locomotion clips): equal to <see cref="Scale"/> so the action and the body move at the same tempo.</summary>
        public const float AnimationRate = Scale;

        /// <summary>The body takes a little longer to wind up to top speed than the pure speed scaling alone gives (weight, not just a lower number).</summary>
        public const float AccelerationStretch = 1.06f;

        // ---- commit windows (v0.7, playtest notes 7/25): no instant state flips; animation can rely on these minimum durations (simulation seconds) ----

        /// <summary>A catch holds the Catch action at least this long, and the receiver is capped at <see cref="CatchSpeedFrac"/> of his top speed while he secures the ball.</summary>
        public const float CatchSeconds = 0.40f, CatchSpeedFrac = 0.78f;
        /// <summary>First contact of a tackle to the whistle (the carrier is already Tackled/Down on screen during this window).</summary>
        public const float TackleWrapSeconds = 0.24f;
        /// <summary>A block engagement cannot be won or lost before it has lasted this long (no lock-up-and-pancake in a few frames).</summary>
        public const float MinEngageSeconds = 0.45f;
        /// <summary>A diving tackler stays in the air up to this long before the dive counts as a whiff.</summary>
        public const float DiveCommitSeconds = 0.5f;

        /// <summary>Pace multiplier for a position (the value handed to <see cref="AgentBody"/>).</summary>
        public static float For(Position p)
        {
            if (PositionGroup.IsOffensiveLine(p)) return LineScale;
            if (PositionGroup.IsDefensiveLine(p)) return RushScale;
            if (PositionGroup.IsDefensiveBack(p)) return DbScale;
            if (PositionGroup.IsLinebacker(p)) return LbScale;
            return Scale;
        }

    }
}

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.7 kick tempo (playtest note 28, "field goals and kickoffs too fast and not convincing"). All values are simulation seconds, which are real
    /// seconds on screen. The animation hooks tell the kick clip WHEN to start so that its foot-contact frame lands exactly on the ball launch:
    /// the simulation logs <see cref="SimEventType.KickApproach"/> when the approach (and the kick clip) must start, its Value is the seconds until
    /// contact, and <see cref="PlaySimulation.KickContactTime"/> is the absolute contact time. <c>SimAgent.Action == Kick</c> from the approach on.
    /// </summary>
    public static class KickTiming
    {
        /// <summary>Foot-contact frame of the staged kickoff clip (<c>kick_off_biped</c>), in clip seconds.</summary>
        public const float KickoffClipContact = 1.27f;
        /// <summary>The approach (clip start to ball contact) in simulation seconds: the clip plays at <see cref="GamePace.AnimationRate"/>, so its contact frame arrives later by that factor.</summary>
        public const float KickoffApproach = KickoffClipContact / GamePace.AnimationRate;
        /// <summary>Whistle to the first step of a CPU kickoff (the kicker stands at his mark, the line settles).</summary>
        public const float KickoffStand = 0.55f;
        /// <summary>The kicking line starts its run this long before the ball is struck, from 3.5 yd back, so it is level with the ball at contact and at speed.</summary>
        public const float CoverageRunLead = 1.30f;
        public const float CoverageStartBack = 3.5f;
        /// <summary>Kickoff kicker's mark: yards behind the tee and to the side. 5-7 steps of approach.</summary>
        public const float KickoffStartBack = 5.0f, KickoffStartSide = 1.8f;

        /// <summary>Field goal / extra point: snap to ball contact (snap about 0.45 s, catch and place, then the kick).</summary>
        public const float FieldGoalSnapToKick = 1.30f;
        /// <summary>Field goal kicker's approach (2-3 steps) in simulation seconds; also the user kicker's press-to-contact delay.</summary>
        public const float FieldGoalApproach = 0.62f;
        /// <summary>Punter step into the kick (a CPU punt starts it at the catch; a user punt at the press).</summary>
        public const float PuntApproach = 0.5f;
    }
}
