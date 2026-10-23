namespace PixFootball.Sim
{
    /// <summary>
    /// v0.10.0 QB: the timing table of the quarterback's choreographed sequence, shared by the simulation (which plans around it: the body does not travel while a segment that has no travel is on) and the presentation (which plays the segment poses for exactly these windows).
    /// The sequence is decided by the simulation before it happens; the view never invents a duration.
    /// </summary>
    public static class QbSeqTiming
    {
        /// <summary>The snap is in the hands: the ball is pulled to the belly and nothing else moves (seconds). Under centre / in the shotgun.</summary>
        public const float SecureUnderCenter = 0.20f, SecureShotgun = 0.10f;
        /// <summary>Under centre: the reverse pivot opens the hips this far from the line (degrees) over <see cref="PivotSeconds"/>; the hips come back to the line over the second half of the drop so he sets square (chest north).</summary>
        public const float OpenDegrees = 72f, PivotSeconds = 0.26f;
        /// <summary>Pocket to scramble: the tuck and the first drive step. The body is held to <see cref="ScrambleFirstSpeed"/> of his speed for the first part and <see cref="ScrambleSecondSpeed"/> up to <see cref="ScrambleStartSeconds"/>.</summary>
        public const float ScrambleStartSeconds = 0.34f, ScrambleFirstSeconds = 0.17f, ScrambleFirstSpeed = 0.30f, ScrambleSecondSpeed = 0.62f;
        /// <summary>Pocket to throw on the move: the set (feet planted over the throw) lasts at least this long after he stops (seconds).</summary>
        public const float SetFeetSeconds = 0.14f;

        /// <summary>Hand-off (under centre, not the toss): the open step toward the runner (the body is held to <see cref="MeshOpenSpeed"/> of his speed for <see cref="MeshOpenSeconds"/>), the arms reach for the runner's pocket for the <see cref="MeshExtendSeconds"/> before the simulated exchange
        /// (the sync frame: the exchange tick, <c>EventEta == 0</c>; the back's pair take is booked on the same tick by CHOREO), and after the ball is gone the empty arms finish the fake for <see cref="MeshFollowSeconds"/>, eyes to the field.</summary>
        public const float MeshOpenSeconds = 0.40f, MeshOpenSpeed = 0.45f, MeshExtendSeconds = 0.45f, MeshFollowSeconds = 0.70f;
        /// <summary>Play-action fake: the ball is carried out toward the runner's pocket (a pocket reach of <see cref="FakeReach"/> of the way) over <see cref="FakeSeconds"/>, the eyes come back to the field after <see cref="FakeEyesBack"/> of it.</summary>
        public const float FakeSeconds = 0.42f, FakeReach = 0.62f, FakeEyesBack = 0.62f;

        public static float Secure(bool shotgun) { return shotgun ? SecureShotgun : SecureUnderCenter; }
    }
}
