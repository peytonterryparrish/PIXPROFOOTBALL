using PixFootball.Sim;

namespace PixFootball.Gameplay
{
    /// <summary>v0.9.0 QBSTANCE (E-116): the spin of a passed ball follows the throw: a bullet spirals tight (about 3000 deg/s), a touch pass about 2400, a lob about 1800. Set by <see cref="PlayStage"/> from the quarterback's throw style; read by the ball in the air.</summary>
    public static class BallSpinHint
    {
        public const float Bullet = 3000f, Touch = 2400f, Lob = 1800f;
        /// <summary>Degrees per second of the ball in the air after a pass.</summary>
        public static float PassSpin = Bullet;
        /// <summary>v0.11.0 QB: the wobble (degrees of nose precession) of the ball in the air after a pass: a clean set-feet throw is nearly perfect, a hurried one, a throw on the run, a hit arm wobbles. Set by the stage at the release.</summary>
        public static float Wobble = 1.2f;

        public static float For(ThrowStyle style)
        {
            switch (style)
            {
                case ThrowStyle.Lob: return Lob;
                case ThrowStyle.Touch: return Touch;
                case ThrowStyle.Bullet: return Bullet;
                default: return 2800f;
            }
        }
    }
}
