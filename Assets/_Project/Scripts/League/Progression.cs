using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>
    /// Offseason ratings development: growth to the position's peak age, plateau, then decline.
    /// Dev trait speeds growth, Potential caps it, coaching nudges it.
    /// </summary>
    public static class Progression
    {
        private static bool IsPhysical(RatingKind k)
        {
            switch (k)
            {
                case RatingKind.Speed: case RatingKind.Acceleration: case RatingKind.Agility: case RatingKind.Jumping:
                case RatingKind.Elusiveness: case RatingKind.BreakTackle: case RatingKind.Strength:
                    return true;
            }
            return false;
        }

        /// <summary>Expected yearly change in overall for a player at the given (new) age, before noise.</summary>
        public static float ExpectedChange(Position pos, int age, DevTrait dev)
        {
            int peak = AgeCurve.PeakStart(pos);
            int decl = AgeCurve.DeclineStart(pos);
            if (dev == DevTrait.Late) { peak += 2; decl += 1; }
            if (dev == DevTrait.Superstar) decl += 1;
            float g = AgeCurve.DevGrowthFactor(dev);
            if (age <= peak)
            {
                float rate = 2.4f * g;
                if (dev == DevTrait.Late) rate = age <= 24 ? 1.4f : 3.6f;
                return rate;
            }
            if (age <= decl) return -0.15f;
            int n = age - decl;
            return -(1.3f + 0.55f * n);
        }

        /// <summary>
        /// Ages the player one year (call once per offseason) and moves the ratings.
        /// <paramref name="coachDevelopment"/> is the head coach's 40-99 development rating (use 65 for neutral).
        /// </summary>
        public static void Advance(Player p, Rng rng, int coachDevelopment = 65)
        {
            p.Bio.Age++;
            p.Experience++;
            int cur = p.Ratings.Overall(p.Position);
            float exp = ExpectedChange(p.Position, p.Bio.Age, p.Dev);
            int peak = AgeCurve.PeakStart(p.Position) + (p.Dev == DevTrait.Late ? 2 : 0);
            bool growing = p.Bio.Age <= peak;
            float coach = (coachDevelopment - 65) * 0.045f;
            float change = growing ? exp + coach + rng.Gaussian(0f, 1.5f) : exp * (0.75f + 0.5f * rng.NextFloat()) + coach * 0.6f + rng.Gaussian(0f, 0.9f);

            float target = cur + change;
            if (growing)
            {
                if (target > p.Potential) target = p.Potential + (cur >= p.Potential ? rng.Gaussian(0f, 0.6f) : 0f);
            }
            else
            {
                // late career: hold position within the potential ceiling
                if (target > p.Potential) target = p.Potential;
            }
            // injuries leave a mark
            if (p.Injury.Wear > 0 && !growing) target -= p.Injury.Wear * 0.05f;
            int newOvr = Math.Max(30, Math.Min(99, (int)Math.Round(target)));
            int delta = newOvr - cur;

            Ratings r = p.Ratings;
            for (int k = 0; k < Ratings.KindCount; k++)
            {
                var kind = (RatingKind)k;
                if (kind == RatingKind.Injury) continue;
                int d;
                float jitter = rng.Gaussian(0f, 0.9f);
                bool phys = IsPhysical(kind);
                if (delta >= 0) d = (int)Math.Round(delta * (phys ? 0.85f : 1.1f) + jitter);
                else d = (int)Math.Round(delta * (phys ? 1.45f : 0.6f) + jitter);
                // do not let non-weighted background ratings run away
                r.Set(kind, r.Get(kind) + (kind == RatingKind.Stamina ? d / 2 : d));
            }
            PlayerFactory.Retarget(ref r, p.Position, newOvr);
            p.Ratings = r;

            // durability fades slowly with age and wear
            int injuryRating = r.Injury;
            if (p.Bio.Age >= 30) injuryRating -= rng.Range(0, 3);
            if (p.Injury.Wear > 3) injuryRating -= 1;
            p.Ratings.Injury = Ratings.Clamp(injuryRating);
        }

        /// <summary>College development: faster growth, coached by the school's head coach.</summary>
        public static void AdvanceCollege(Player p, Rng rng, int coachDevelopment)
        {
            Advance(p, rng, coachDevelopment);
        }
    }

    public static class Retirement
    {
        private static int PositionShift(Position pos)
        {
            switch (PositionBuckets.Of(pos))
            {
                case PositionBucket.K: case PositionBucket.P: return 5;
                case PositionBucket.QB: return 4;
                case PositionBucket.OL: return 2;
                case PositionBucket.RB: return -2;
                case PositionBucket.CB: return -1;
                case PositionBucket.WR: case PositionBucket.S: return -1;
                default: return 0;
            }
        }

        /// <summary>Probability that the player retires after this season.</summary>
        public static float Probability(Player p, bool unsigned)
        {
            int age = p.Bio.Age - PositionShift(p.Position);
            float baseP;
            if (age < 30) baseP = 0.004f;
            else if (age <= 31) baseP = 0.02f;
            else if (age == 32) baseP = 0.04f;
            else if (age == 33) baseP = 0.07f;
            else if (age == 34) baseP = 0.12f;
            else if (age == 35) baseP = 0.2f;
            else if (age == 36) baseP = 0.32f;
            else if (age == 37) baseP = 0.45f;
            else if (age == 38) baseP = 0.6f;
            else baseP = 0.8f;
            int ovr = p.Ratings.Overall(p.Position);
            if (ovr < 55 && p.Bio.Age >= 27) baseP = baseP * 2.2f + 0.02f;
            else if (ovr >= 82) baseP *= 0.45f;
            else if (ovr >= 72) baseP *= 0.75f;
            baseP *= 1f + p.Injury.Wear * 0.08f;
            if (unsigned) baseP = baseP * 1.7f + (p.Bio.Age >= 29 ? 0.04f : 0.01f);
            if (p.Bio.Age >= 41) return 1f;
            return baseP > 0.97f ? 0.97f : baseP;
        }

        public static bool Check(Player p, Rng rng, bool unsigned)
        {
            return rng.Chance(Probability(p, unsigned));
        }
    }
}
