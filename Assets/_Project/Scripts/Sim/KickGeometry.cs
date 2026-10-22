namespace PixFootball.Sim
{
    /// <summary>
    /// v0.9.0 CONNECT (E-253, E-255, E-256, E-298, E-387): the heights and offsets of the football around a kick, shared by the simulation (where the ball is launched from) and the view
    /// (where it is drawn), so the ball never pops between the two.
    /// </summary>
    public static class KickGeometry
    {
        /// <summary>Kickoff tee: height of the ball centre above the turf (yards). The view draws the ball here until the kick is struck.</summary>
        public const float TeeHeightYd = 0.30f;
        /// <summary>Holder's spot: ball centre height above the turf while the holder steadies it (yards).</summary>
        public const float HolderSpotHeightYd = 0.22f;
        /// <summary>Punt: the ball leaves the punter's hands this long before the foot meets it (seconds); it falls with a slight forward lean.</summary>
        public const float PuntDropSeconds = 0.34f;
        /// <summary>Punt contact point relative to the punter: yards ahead and ball centre height (yards).</summary>
        public const float PuntContactAheadYd = 0.55f, PuntContactHeightYd = 0.33f;
        /// <summary>Long snap: the ball starts between the snapper's legs (yards behind his position, height).</summary>
        public const float SnapBehindYd = 0.25f, SnapStartHeightYd = 0.30f;
    }
}
