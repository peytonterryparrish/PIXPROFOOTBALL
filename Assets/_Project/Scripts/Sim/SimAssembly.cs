using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PixFootball.Tests.EditMode")]

namespace PixFootball.Sim
{
    /// <summary>Marker for the engine-free play simulation assembly (22 agents + ball at a fixed 60 Hz step, yards and seconds).</summary>
    public static class SimAssembly
    {
        public const string Version = "1.0.0";
        /// <summary>Fixed simulation step (seconds).</summary>
        public const float FixedStep = 1f / 60f;
    }

    /// <summary>Goal post geometry shared by the kicking game (identical for pro and college: 18 ft 6 in between the uprights, crossbar at 10 ft).</summary>
    public static class GoalPost
    {
        public const float WidthFeet = 18.5f;
        public const float WidthYards = WidthFeet / 3f;
        public const float CrossbarFeet = 10f;
        public const float CrossbarYards = CrossbarFeet / 3f;
        /// <summary>The posts stand on the end line, 10 yards behind the goal line (Y = 110 in sim coordinates).</summary>
        public const float PlaneY = 110f;
        /// <summary>Top of the uprights above the ground: crossbar 10 ft + 35 ft of upright = 45 ft = 15 yards.</summary>
        public const float UprightTopYards = 15f;

        /// <summary>
        /// Is a ball crossing the goal plane at lateral x and height z (yards) a good kick? Must clear the crossbar and pass between
        /// the uprights (ball radius shaved off both edges).
        /// </summary>
        public static bool IsGood(float x, float z)
        {
            float half = WidthYards * 0.5f - BallPhysics.BallRadius * 0.6f;
            return System.Math.Abs(x - PixFootball.Core.FieldGeometry.CenterX) < half
                   && z > CrossbarYards + BallPhysics.BallRadius * 0.4f && z < UprightTopYards;
        }
    }
}
