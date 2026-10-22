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
        public const float Scale = 0.85f;   // v0.9.0 (playtest note 10): 0.88 -> 0.85, three percent slower again

        /// <summary>v0.9.0 (audit E-016 / E-317): linemen are heavy. Offensive linemen run 14.5-16.5 mph in pads in the real game, a long way below a receiver; the line battle keeps its timing through the get-off, not through free speed.</summary>
        public const float LineScale = 0.805f;

        /// <summary>v0.9.0 (E-015 / E-317): defensive ends 18-19.5 mph in pads (they were at pace 1.0 and outran the receivers), defensive tackles 16.5-18. The sack rate is re-fitted with the get-off delay and the pocket timing, not with raw speed.</summary>
        public const float EdgeScale = 0.91f, TackleScale = 0.835f;
        /// <summary>Kept for callers that used the old single defensive-line value: the edge rushers.</summary>
        public const float RushScale = EdgeScale;
        /// <summary>v0.9.0 (E-317): tight ends and quarterbacks were a little too slow for what they are (18-19.5 mph / 17.5-19.5 mph in pads).</summary>
        public const float TightEndScale = 0.89f, QbScale = 0.89f;
        /// <summary>Defensive backs and linebackers keep a little more of their speed than the offense: slowing every body by the same amount handed the ball to the offense (28.9 points a team, 7.9 yards an attempt) because the thrown ball did not slow with them. Calibrated with 600-game runs (docs: B-001).</summary>
        public const float DbScale = 0.87f, LbScale = 0.86f;

        /// <summary>Playback rate for animation that is not velocity-driven (non-locomotion clips): equal to <see cref="Scale"/> so the action and the body move at the same tempo.</summary>
        public const float AnimationRate = Scale;

        /// <summary>The body takes a little longer to wind up to top speed than the pure speed scaling alone gives (weight, not just a lower number).</summary>
        public const float AccelerationStretch = 1.06f;

        // ---- commit windows (v0.7, playtest notes 7/25): no instant state flips; animation can rely on these minimum durations (simulation seconds) ----

        /// <summary>A catch holds the Catch action at least this long, and the receiver is capped at <see cref="CatchSpeedFrac"/> of his top speed while he secures the ball.</summary>
        public const float CatchSeconds = 0.45f, CatchSpeedFrac = 0.72f;
        /// <summary>First contact of a tackle to the whistle (the carrier is already Tackled/Down on screen during this window).</summary>
        public const float TackleWrapSeconds = 0.90f;
        /// <summary>v0.9.0 (E-369): a big hit, a gang tackle or a dive tackle takes longer from contact to the whistle (the pile, the carry-through).</summary>
        public const float TackleWrapBigSeconds = 1.20f;
        /// <summary>A block engagement cannot be won or lost before it has lasted this long (no lock-up-and-pancake in a few frames).</summary>
        public const float MinEngageSeconds = 0.60f;   // v0.9.0 (E-385): 0.6 s in run mode; pass mode uses MinEngagePassSeconds
        /// <summary>v0.9.0 (E-385): a pass-protection lock-up lasts at least this long before it can be won or lost (a rusher cannot beat his man in the first 0.9 s).</summary>
        public const float MinEngagePassSeconds = 0.90f;
        /// <summary>A diving tackler stays in the air up to this long before the dive counts as a whiff.</summary>
        public const float DiveCommitSeconds = 0.5f;

        /// <summary>Pace multiplier for a position (the value handed to <see cref="AgentBody"/>).</summary>
        public static float For(Position p)
        {
            if (PositionGroup.IsOffensiveLine(p)) return LineScale;
            if (p == Position.DT) return TackleScale;
            if (PositionGroup.IsDefensiveLine(p)) return EdgeScale;
            if (p == Position.TE) return TightEndScale;
            if (p == Position.QB) return QbScale;
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
        public const float KickoffStartBack = 5.2f, KickoffStartSide = -3.6f;   // v0.9.0 (E-300/302): the (right-footed) kicker stands LEFT of the ball seen from behind and runs in at an angle (about 35 degrees)

        /// <summary>Field goal / extra point: snap to ball contact (snap about 0.45 s, catch and place, then the kick).</summary>
        public const float FieldGoalSnapToKick = 1.30f;
        /// <summary>Field goal kicker's approach (2-3 steps) in simulation seconds; also the user kicker's press-to-contact delay.</summary>
        public const float FieldGoalApproach = 0.62f;
        /// <summary>Punter step into the kick (a CPU punt starts it at the catch; a user punt at the press).</summary>
        public const float PuntApproach = 1.10f;
        /// <summary>v0.9.0 (E-370): the punter holds the ball this long after the catch before the kick (snap to kick is about 2.0 s; it was about 1.2 s).</summary>
        public const float PuntHold = 1.10f;
        /// <summary>Extra seconds the punt rush waits (on top of each rusher's own delay) so the longer operation does not turn into more blocks.</summary>
        public const float PuntRushDelay = 0.45f;
    }
}
