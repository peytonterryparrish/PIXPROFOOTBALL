using System;

namespace PixFootball.Core
{
    /// <summary>
    /// Playing conditions of a game that the simulation and the HUD share: the wind (speed and compass direction on the field), per quarter.
    /// Deterministic: <see cref="Generate"/> draws from a seed and <see cref="ForQuarter"/> shifts the wind a little each period.
    /// World frame: 0 degrees = blowing toward the +Z end zone, 90 = toward the +X sideline, 180 = toward -Z, 270 = toward -X.
    /// </summary>
    public sealed class MatchConditions
    {
        public const float YardsPerSecondPerMph = 0.48889f;

        /// <summary>Wind speed in miles per hour (0 = still air).</summary>
        public float WindMph;
        /// <summary>Direction the wind blows TOWARD, in degrees on the field (see class summary).</summary>
        public float WindToDegrees;
        /// <summary>Seed the conditions were generated from (quarter variation derives from it).</summary>
        public int Seed;

        public static MatchConditions Calm { get { return new MatchConditions(); } }

        public MatchConditions() { }

        public MatchConditions(float mph, float toDegrees, int seed = 0)
        {
            WindMph = Math.Max(0f, mph);
            WindToDegrees = Normalize(toDegrees);
            Seed = seed;
        }

        private static float Normalize(float deg)
        {
            deg %= 360f;
            if (deg < 0f) deg += 360f;
            return deg;
        }

        /// <summary>A reproducible game wind: a quarter of games are calm, most are light, some breezy, a few properly windy.</summary>
        public static MatchConditions Generate(int seed)
        {
            var rng = new Rng((ulong)unchecked((uint)seed * 2654435761u) ^ 0x57494E44UL);
            float u = rng.NextFloat();
            float mph;
            if (u < 0.22f) mph = rng.Range(0f, 3f);
            else if (u < 0.66f) mph = rng.Range(3f, 9f);
            else if (u < 0.92f) mph = rng.Range(9f, 16f);
            else mph = rng.Range(16f, 24f);
            return new MatchConditions(mph, rng.Range(0f, 360f), seed);
        }

        /// <summary>The wind in the given quarter (1-4, overtime = 5): the same weather front, drifting a little in speed and direction.</summary>
        public MatchConditions ForQuarter(int quarter)
        {
            if (quarter <= 1) return new MatchConditions(WindMph, WindToDegrees, Seed);
            var rng = new Rng(unchecked(Seed * 40503 + quarter * 9176 + 17));
            float speed = WindMph * rng.Range(0.8f, 1.2f);
            float dir = WindToDegrees + rng.Gaussian(0f, 18f);
            return new MatchConditions(speed, dir, Seed);
        }

        /// <summary>Component blowing toward the +Z end zone (mph, negative = toward -Z).</summary>
        public float AlongMph { get { return WindMph * (float)Math.Cos(WindToDegrees * Math.PI / 180.0); } }
        /// <summary>Component blowing toward the +X sideline (mph).</summary>
        public float AcrossMph { get { return WindMph * (float)Math.Sin(WindToDegrees * Math.PI / 180.0); } }

        /// <summary>
        /// The wind as the play simulation sees it (X lateral, Y downfield in the offense's frame), in yards per second. The simulation
        /// always has the offense attacking +Y; when the offense attacks the -Z end zone the frame is rotated 180 degrees.
        /// </summary>
        public Vec2 ToSimFrame(bool offenseAttacksPositiveZ)
        {
            float along = AlongMph * YardsPerSecondPerMph, across = AcrossMph * YardsPerSecondPerMph;
            return offenseAttacksPositiveZ ? new Vec2(across, along) : new Vec2(-across, -along);
        }

        /// <summary>Tailwind (positive) or headwind (negative) for a team driving toward the given end zone, in mph.</summary>
        public float TailwindMph(bool offenseAttacksPositiveZ) { return offenseAttacksPositiveZ ? AlongMph : -AlongMph; }

        /// <summary>Short HUD label: "CALM", "LIGHT", "BREEZY" or "WINDY".</summary>
        public string Label
        {
            get
            {
                if (WindMph < 3f) return "CALM";
                if (WindMph < 9f) return "LIGHT";
                if (WindMph < 16f) return "BREEZY";
                return "WINDY";
            }
        }

        /// <summary>HUD text such as "WIND 12 MPH".</summary>
        public string HudText { get { return WindMph < 1f ? "WIND CALM" : "WIND " + Math.Round(WindMph) + " MPH"; } }

        public MatchConditions Clone() { return new MatchConditions(WindMph, WindToDegrees, Seed); }
    }
}
