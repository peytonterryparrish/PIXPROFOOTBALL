using System;

namespace PixFootball.Core
{
    public enum Hash { Left, Middle, Right }

    /// <summary>
    /// Field dimensions in yards. X = sideline to sideline (0..Width), Z = goal-line axis
    /// (0..TotalLength, end zones included). Ball spots (0..100) are measured from the possessing team's own goal line.
    /// </summary>
    public static class FieldGeometry
    {
        public const float Length = 100f;
        public const float EndZoneDepth = 10f;
        public const float TotalLength = 120f;
        public const float Width = 160f / 3f; // 53 1/3 yards
        public const float CenterX = Width / 2f;

        /// <summary>Distance from field center to each hash mark (yards).</summary>
        public static float HashOffsetFromCenter(RuleSet rules)
        {
            return Yards.FeetToYards(rules.HashWidthFeet) * 0.5f;
        }

        public static float HashX(Hash hash, RuleSet rules)
        {
            switch (hash)
            {
                case Hash.Left: return CenterX - HashOffsetFromCenter(rules);
                case Hash.Right: return CenterX + HashOffsetFromCenter(rules);
                default: return CenterX;
            }
        }

        public static Hash NearestHash(float x, RuleSet rules)
        {
            float dl = Math.Abs(x - HashX(Hash.Left, rules));
            float dm = Math.Abs(x - HashX(Hash.Middle, rules));
            float dr = Math.Abs(x - HashX(Hash.Right, rules));
            if (dl <= dm && dl <= dr) return Hash.Left;
            return dr < dm ? Hash.Right : Hash.Middle;
        }

        /// <summary>Converts a 0..100 ball spot to field Z. attackingPositive: team drives toward +Z.</summary>
        public static float SpotToZ(float spot, bool attackingPositive)
        {
            return attackingPositive ? EndZoneDepth + spot : EndZoneDepth + (Length - spot);
        }

        public static float ZToSpot(float z, bool attackingPositive)
        {
            return attackingPositive ? z - EndZoneDepth : Length - (z - EndZoneDepth);
        }

        public static bool IsInBounds(float x, float z)
        {
            return x >= 0f && x <= Width && z >= 0f && z <= TotalLength;
        }

        public static bool IsInEndZone(float z)
        {
            return z < EndZoneDepth || z > EndZoneDepth + Length;
        }

        /// <summary>Distance (yards) from a lateral position to the nearest sideline.</summary>
        public static float DistanceToSideline(float x)
        {
            return Math.Min(x, Width - x);
        }
    }
}
