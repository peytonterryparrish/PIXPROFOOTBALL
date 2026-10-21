namespace PixFootball.Core
{
    /// <summary>Unit conversion. Core works in yards; Gameplay works in meters (1 Unity unit = 1 m).</summary>
    public static class Yards
    {
        public const float ToMeters = 0.9144f;
        public const float FromMeters = 1f / 0.9144f;
        public static float ToMetersF(float yards) { return yards * ToMeters; }
        public static float MetersToYards(float meters) { return meters * FromMeters; }
        public static float FeetToYards(float feet) { return feet / 3f; }
        public static float YardsToFeet(float yards) { return yards * 3f; }
        /// <summary>1 mph = 1.4667 ft/s = 0.4889 yd/s.</summary>
        public static float MphToYardsPerSecond(float mph) { return mph * 0.488889f; }
        public static float YardsPerSecondToMph(float yps) { return yps / 0.488889f; }
    }
}
