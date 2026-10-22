using System;
using System.Collections.Generic;
using PixFootball.Core;
using Side = PixFootball.Core.Team;

namespace PixFootball.League
{
    /// <summary>Anything that hands out world-unique player ids.</summary>
    public interface IPlayerIdSource
    {
        int NextPlayerId();
    }

    /// <summary>Age curves per position (when growth ends and decline begins).</summary>
    public static class AgeCurve
    {
        /// <summary>Age at which the player reaches his ceiling (Normal development).</summary>
        public static int PeakStart(Position p)
        {
            switch (PositionBuckets.Of(p))
            {
                case PositionBucket.QB: return 28;
                case PositionBucket.RB: return 25;
                case PositionBucket.FB: return 26;
                case PositionBucket.WR: return 26;
                case PositionBucket.TE: return 27;
                case PositionBucket.OL: return 28;
                case PositionBucket.DL: return 27;
                case PositionBucket.LB: return 26;
                case PositionBucket.CB: return 25;
                case PositionBucket.S: return 26;
                default: return 28;
            }
        }

        /// <summary>Age at which decline begins.</summary>
        public static int DeclineStart(Position p)
        {
            switch (PositionBuckets.Of(p))
            {
                case PositionBucket.QB: return 34;
                case PositionBucket.RB: return 28;
                case PositionBucket.FB: return 30;
                case PositionBucket.WR: return 30;
                case PositionBucket.TE: return 31;
                case PositionBucket.OL: return 33;
                case PositionBucket.DL: return 31;
                case PositionBucket.LB: return 30;
                case PositionBucket.CB: return 29;
                case PositionBucket.S: return 30;
                default: return 38;
            }
        }

        public static float DevGrowthFactor(DevTrait d)
        {
            switch (d)
            {
                case DevTrait.Star: return 1.3f;
                case DevTrait.Superstar: return 1.7f;
                case DevTrait.Late: return 0.8f;
                default: return 1f;
            }
        }
    }

    /// <summary>Market value, rookie scale and contract construction.</summary>
    public static class ContractModel
    {
        public static long PositionCeiling(Position p)
        {
            switch (p)
            {
                case Position.QB: return 46000000L;
                case Position.RB: return 13000000L;
                case Position.FB: return 3200000L;
                case Position.WR: return 30000000L;
                case Position.TE: return 15000000L;
                case Position.LT: case Position.RT: return 24000000L;
                case Position.LG: case Position.RG: case Position.C: return 16000000L;
                case Position.LE: case Position.RE: return 28000000L;
                case Position.DT: return 24000000L;
                case Position.LOLB: case Position.ROLB: return 19000000L;
                case Position.MLB: return 14000000L;
                case Position.CB: return 21000000L;
                case Position.FS: case Position.SS: return 12000000L;
                case Position.K: return 5000000L;
                default: return 3500000L;
            }
        }

        public static long MarketAav(Position pos, int ovr, int age)
        {
            float t = (ovr - 55f) / 37f;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            double v = Contract.MinSalary + (PositionCeiling(pos) - Contract.MinSalary) * Math.Pow(t, 2.4);
            if (age > 31) v *= Math.Max(0.4, 1.0 - 0.07 * (age - 31));
            long r = (long)v;
            r = (r / 10000L) * 10000L;
            return Math.Max(Contract.MinSalary, r);
        }

        private static readonly int[] RookiePicks = { 1, 8, 16, 32, 48, 64, 100, 150, 224, 400 };
        private static readonly double[] RookieAav = { 11.5, 7.0, 5.0, 3.6, 2.5, 1.9, 1.4, 1.1, 0.9, 0.85 };

        /// <summary>Rookie scale salary for an overall pick number (UDFA uses a big pick number).</summary>
        public static long RookieAavForPick(int pick)
        {
            if (pick <= 1) return (long)(RookieAav[0] * 1000000.0);
            for (int i = 1; i < RookiePicks.Length; i++)
            {
                if (pick <= RookiePicks[i])
                {
                    double a = Math.Log(RookiePicks[i - 1]), b = Math.Log(RookiePicks[i]);
                    double f = (Math.Log(pick) - a) / (b - a);
                    double m = RookieAav[i - 1] + (RookieAav[i] - RookieAav[i - 1]) * f;
                    return Math.Max(Contract.MinSalary, (long)(m * 1000000.0) / 10000L * 10000L);
                }
            }
            return Contract.MinSalary;
        }

        public static Contract RookieContract(int pick, int year)
        {
            bool udfa = pick > 260;
            int years = udfa ? 3 : 4;
            long aav = RookieAavForPick(pick);
            float bonus = pick <= 32 ? 0.35f : (pick <= 100 ? 0.15f : 0.03f);
            return Contract.Create(years, aav, bonus, pick <= 32 ? 1f : (pick <= 100 ? 0.6f : 0.15f), year, true);
        }

        public static Contract VeteranContract(Position pos, int ovr, int age, int years, int year, Rng rng)
        {
            long aav = MarketAav(pos, ovr, age);
            float bonus = Math.Min(0.45f, 0.1f + (aav - Contract.MinSalary) / 60000000f);
            float guaranteed = Math.Min(0.9f, 0.1f + (aav - Contract.MinSalary) / 40000000f);
            return Contract.Create(years, aav, bonus, guaranteed, year, false);
        }

        /// <summary>Rescales an existing contract's money by a factor (used to fit a roster under the cap).</summary>
        public static Contract Scale(Contract c, double factor)
        {
            if (Math.Abs(factor - 1.0) < 1e-9) return c;
            long aav = Math.Max(Contract.MinSalary, (long)(c.Aav * factor) / 10000L * 10000L);
            float bonusFraction = c.Years > 0 && c.Aav > 0 ? (float)c.SigningBonus / (c.Aav * (float)c.Years) : 0f;
            float gFraction = c.Years > 0 && c.Aav > 0 ? (float)c.Guaranteed / (c.Aav * (float)c.Years) : 0f;
            var n = Contract.Create(c.Years, aav, bonusFraction, gFraction, c.StartYear, c.Rookie);
            n.YearsLeft = c.YearsLeft;
            return n;
        }
    }

    /// <summary>Creates individual players: archetype-driven ratings, bio, development.</summary>
    public static class PlayerFactory
    {
        // physical baselines at a talent of 75:  Speed, Accel, Agility, Strength, Jumping
        private static int[] PhysBase(Position p)
        {
            switch (p)
            {
                case Position.QB: return new[] { 68, 72, 68, 55, 55 };
                case Position.RB: return new[] { 88, 90, 86, 62, 60 };
                case Position.FB: return new[] { 72, 74, 66, 78, 50 };
                case Position.WR: return new[] { 90, 91, 88, 50, 76 };
                case Position.TE: return new[] { 76, 78, 70, 74, 64 };
                case Position.LT: case Position.LG: case Position.C: case Position.RG: case Position.RT:
                    return new[] { 52, 56, 50, 86, 40 };
                case Position.LE: case Position.RE: return new[] { 76, 80, 72, 84, 60 };
                case Position.DT: return new[] { 62, 68, 58, 90, 50 };
                case Position.LOLB: case Position.ROLB: return new[] { 82, 84, 78, 74, 62 };
                case Position.MLB: return new[] { 78, 80, 74, 76, 58 };
                case Position.CB: return new[] { 91, 92, 90, 48, 78 };
                case Position.FS: case Position.SS: return new[] { 87, 88, 82, 58, 74 };
                default: return new[] { 58, 60, 55, 45, 40 };
            }
        }

        private static readonly RatingKind[] PhysKinds =
            { RatingKind.Speed, RatingKind.Acceleration, RatingKind.Agility, RatingKind.Strength, RatingKind.Jumping };

        public static Archetype RandomArchetype(Position pos, Rng rng)
        {
            float r = rng.NextFloat();
            switch (PositionBuckets.Of(pos))
            {
                case PositionBucket.QB: return r < 0.5f ? Archetype.QbPocket : (r < 0.8f ? Archetype.QbScrambler : Archetype.QbGunslinger);
                case PositionBucket.RB: return r < 0.38f ? Archetype.RbPower : (r < 0.76f ? Archetype.RbSpeed : Archetype.RbAllPurpose);
                case PositionBucket.FB: return Archetype.FbBlocker;
                case PositionBucket.WR: return r < 0.38f ? Archetype.WrDeepThreat : (r < 0.72f ? Archetype.WrPossession : Archetype.WrSlot);
                case PositionBucket.TE: return r < 0.5f ? Archetype.TeBlocker : Archetype.TeReceiver;
                case PositionBucket.OL: return r < 0.34f ? Archetype.OlPassProtector : (r < 0.68f ? Archetype.OlRunMauler : Archetype.OlBalanced);
                case PositionBucket.DL: return r < 0.34f ? Archetype.DlRunStuffer : (r < 0.68f ? Archetype.DlPassRusher : Archetype.DlBalanced);
                case PositionBucket.LB: return r < 0.38f ? Archetype.LbRunStopper : (r < 0.7f ? Archetype.LbCoverage : Archetype.LbBlitzer);
                case PositionBucket.CB: return r < 0.42f ? Archetype.CbManLockdown : (r < 0.76f ? Archetype.CbZone : Archetype.CbSlot);
                case PositionBucket.S: return r < 0.5f ? Archetype.SBox : Archetype.SCenterfield;
                case PositionBucket.K: return Archetype.KPlacekicker;
                default: return Archetype.PPunter;
            }
        }

        private static void Adj(ref Ratings r, RatingKind k, int d) { r.Set(k, r.Get(k) + d); }

        private static void ApplyArchetype(ref Ratings r, Archetype a)
        {
            switch (a)
            {
                case Archetype.QbPocket:
                    Adj(ref r, RatingKind.ThrowAccuracy, 6); Adj(ref r, RatingKind.Awareness, 5);
                    Adj(ref r, RatingKind.Speed, -12); Adj(ref r, RatingKind.Acceleration, -8); Adj(ref r, RatingKind.Agility, -8);
                    Adj(ref r, RatingKind.Elusiveness, -10); break;
                case Archetype.QbScrambler:
                    Adj(ref r, RatingKind.Speed, 14); Adj(ref r, RatingKind.Acceleration, 12); Adj(ref r, RatingKind.Agility, 10);
                    Adj(ref r, RatingKind.Elusiveness, 16); Adj(ref r, RatingKind.Carrying, 8);
                    Adj(ref r, RatingKind.ThrowAccuracy, -4); Adj(ref r, RatingKind.Awareness, -5); break;
                case Archetype.QbGunslinger:
                    Adj(ref r, RatingKind.ThrowPower, 10); Adj(ref r, RatingKind.ThrowAccuracy, -3); Adj(ref r, RatingKind.Awareness, -5); break;
                case Archetype.RbPower:
                    Adj(ref r, RatingKind.Strength, 12); Adj(ref r, RatingKind.BreakTackle, 10); Adj(ref r, RatingKind.Carrying, 4);
                    Adj(ref r, RatingKind.Speed, -6); Adj(ref r, RatingKind.Elusiveness, -10); Adj(ref r, RatingKind.Catching, -8); break;
                case Archetype.RbSpeed:
                    Adj(ref r, RatingKind.Speed, 6); Adj(ref r, RatingKind.Acceleration, 6); Adj(ref r, RatingKind.Elusiveness, 10);
                    Adj(ref r, RatingKind.Agility, 6); Adj(ref r, RatingKind.Strength, -10); Adj(ref r, RatingKind.BreakTackle, -10); break;
                case Archetype.RbAllPurpose:
                    Adj(ref r, RatingKind.Catching, 14); Adj(ref r, RatingKind.Awareness, 4); Adj(ref r, RatingKind.Strength, -4);
                    Adj(ref r, RatingKind.BreakTackle, -4); break;
                case Archetype.FbBlocker:
                    Adj(ref r, RatingKind.RunBlocking, 6); Adj(ref r, RatingKind.Strength, 3); break;
                case Archetype.WrDeepThreat:
                    Adj(ref r, RatingKind.Speed, 6); Adj(ref r, RatingKind.Acceleration, 3); Adj(ref r, RatingKind.Jumping, 6);
                    Adj(ref r, RatingKind.Catching, -3); Adj(ref r, RatingKind.Elusiveness, -4); Adj(ref r, RatingKind.Awareness, -4); break;
                case Archetype.WrPossession:
                    Adj(ref r, RatingKind.Catching, 8); Adj(ref r, RatingKind.Awareness, 6); Adj(ref r, RatingKind.Speed, -8);
                    Adj(ref r, RatingKind.Acceleration, -4); Adj(ref r, RatingKind.Jumping, 2); Adj(ref r, RatingKind.Elusiveness, -2); break;
                case Archetype.WrSlot:
                    Adj(ref r, RatingKind.Agility, 10); Adj(ref r, RatingKind.Acceleration, 6); Adj(ref r, RatingKind.Elusiveness, 10);
                    Adj(ref r, RatingKind.Speed, -2); Adj(ref r, RatingKind.Jumping, -8); Adj(ref r, RatingKind.Strength, -4); break;
                case Archetype.TeBlocker:
                    Adj(ref r, RatingKind.RunBlocking, 14); Adj(ref r, RatingKind.PassBlocking, 8); Adj(ref r, RatingKind.Strength, 8);
                    Adj(ref r, RatingKind.Catching, -10); Adj(ref r, RatingKind.Speed, -6); break;
                case Archetype.TeReceiver:
                    Adj(ref r, RatingKind.Catching, 10); Adj(ref r, RatingKind.Speed, 8); Adj(ref r, RatingKind.Jumping, 6);
                    Adj(ref r, RatingKind.RunBlocking, -16); Adj(ref r, RatingKind.PassBlocking, -10); Adj(ref r, RatingKind.Strength, -8); break;
                case Archetype.OlPassProtector:
                    Adj(ref r, RatingKind.PassBlocking, 8); Adj(ref r, RatingKind.RunBlocking, -8); Adj(ref r, RatingKind.Agility, 4); break;
                case Archetype.OlRunMauler:
                    Adj(ref r, RatingKind.RunBlocking, 8); Adj(ref r, RatingKind.Strength, 6); Adj(ref r, RatingKind.PassBlocking, -8);
                    Adj(ref r, RatingKind.Agility, -4); break;
                case Archetype.DlRunStuffer:
                    Adj(ref r, RatingKind.Strength, 6); Adj(ref r, RatingKind.BlockShedding, 4); Adj(ref r, RatingKind.Tackling, 4);
                    Adj(ref r, RatingKind.Speed, -8); Adj(ref r, RatingKind.Acceleration, -6); break;
                case Archetype.DlPassRusher:
                    Adj(ref r, RatingKind.Speed, 6); Adj(ref r, RatingKind.Acceleration, 8); Adj(ref r, RatingKind.Agility, 6);
                    Adj(ref r, RatingKind.BlockShedding, 2); Adj(ref r, RatingKind.Strength, -6); Adj(ref r, RatingKind.Tackling, -4); break;
                case Archetype.LbRunStopper:
                    Adj(ref r, RatingKind.Tackling, 6); Adj(ref r, RatingKind.BlockShedding, 6); Adj(ref r, RatingKind.Strength, 4);
                    Adj(ref r, RatingKind.ZoneCoverage, -10); Adj(ref r, RatingKind.ManCoverage, -10); Adj(ref r, RatingKind.Speed, -4); break;
                case Archetype.LbCoverage:
                    Adj(ref r, RatingKind.ZoneCoverage, 12); Adj(ref r, RatingKind.ManCoverage, 10); Adj(ref r, RatingKind.Speed, 4);
                    Adj(ref r, RatingKind.Tackling, -6); Adj(ref r, RatingKind.BlockShedding, -8); Adj(ref r, RatingKind.Strength, -6); break;
                case Archetype.LbBlitzer:
                    Adj(ref r, RatingKind.Acceleration, 8); Adj(ref r, RatingKind.Speed, 4); Adj(ref r, RatingKind.BlockShedding, 6);
                    Adj(ref r, RatingKind.Strength, 2); Adj(ref r, RatingKind.ZoneCoverage, -10); Adj(ref r, RatingKind.ManCoverage, -10); break;
                case Archetype.CbManLockdown:
                    Adj(ref r, RatingKind.ManCoverage, 8); Adj(ref r, RatingKind.Speed, 3); Adj(ref r, RatingKind.Tackling, -4);
                    Adj(ref r, RatingKind.ZoneCoverage, -4); break;
                case Archetype.CbZone:
                    Adj(ref r, RatingKind.ZoneCoverage, 8); Adj(ref r, RatingKind.Awareness, 5); Adj(ref r, RatingKind.ManCoverage, -4);
                    Adj(ref r, RatingKind.Speed, -3); Adj(ref r, RatingKind.Tackling, 4); break;
                case Archetype.CbSlot:
                    Adj(ref r, RatingKind.Agility, 8); Adj(ref r, RatingKind.Acceleration, 4); Adj(ref r, RatingKind.Speed, -4);
                    Adj(ref r, RatingKind.Tackling, 2); break;
                case Archetype.SBox:
                    Adj(ref r, RatingKind.Tackling, 10); Adj(ref r, RatingKind.Strength, 8); Adj(ref r, RatingKind.BlockShedding, 6);
                    Adj(ref r, RatingKind.Speed, -6); Adj(ref r, RatingKind.ZoneCoverage, -4); Adj(ref r, RatingKind.Jumping, -4); break;
                case Archetype.SCenterfield:
                    Adj(ref r, RatingKind.ZoneCoverage, 8); Adj(ref r, RatingKind.Speed, 4); Adj(ref r, RatingKind.Awareness, 4);
                    Adj(ref r, RatingKind.Catching, 6); Adj(ref r, RatingKind.Tackling, -8); Adj(ref r, RatingKind.Strength, -6); break;
            }
        }

        /// <summary>Builds a rating set whose position overall is (very close to) <paramref name="targetOvr"/>.</summary>
        public static Ratings BuildRatings(Position pos, Archetype arch, int targetOvr, Rng rng)
        {
            RatingWeight[] weights = OverallWeights.For(pos);
            float[] w = new float[Ratings.KindCount];
            for (int i = 0; i < weights.Length; i++) w[(int)weights[i].Kind] += weights[i].Weight;
            int[] phys = PhysBase(pos);
            var r = new Ratings();
            float talentShift = (targetOvr - 75f) * 0.6f;

            for (int k = 0; k < Ratings.KindCount; k++)
            {
                var kind = (RatingKind)k;
                float v;
                int pi = Array.IndexOf(PhysKinds, kind);
                if (kind == RatingKind.Stamina) v = rng.Range(62, 95);
                else if (kind == RatingKind.Injury) v = rng.Range(55, 96);
                else if (pi >= 0) v = phys[pi] + talentShift + rng.Gaussian(0f, 4.5f);
                else if (w[k] > 0f)
                {
                    float off = w[k] >= 18f ? 4f : (w[k] >= 10f ? 2f : (w[k] >= 5f ? -2f : -6f));
                    v = targetOvr + off + rng.Gaussian(0f, 5f);
                }
                else v = rng.Range(18, 46);
                r.Set(kind, (int)Math.Round(v));
            }
            ApplyArchetype(ref r, arch);

            Retarget(ref r, pos, targetOvr);
            return r;
        }

        /// <summary>Uniformly shifts the weighted ratings so the position overall lands on <paramref name="targetOvr"/>.</summary>
        public static void Retarget(ref Ratings r, Position pos, int targetOvr)
        {
            RatingWeight[] weights = OverallWeights.For(pos);
            float total = 0f, stamW = 0f;
            for (int i = 0; i < weights.Length; i++)
            {
                total += weights[i].Weight;
                if (weights[i].Kind == RatingKind.Stamina || weights[i].Kind == RatingKind.Injury) stamW += weights[i].Weight;
            }
            float adjustable = total - stamW;
            if (adjustable <= 0f) adjustable = 1f;
            for (int iter = 0; iter < 8; iter++)
            {
                int diff = targetOvr - r.Overall(pos);
                if (diff == 0) break;
                float scale = total / adjustable;
                int shift = (int)Math.Round(diff * scale);
                if (shift == 0) shift = diff > 0 ? 1 : -1;
                for (int i = 0; i < weights.Length; i++)
                {
                    RatingKind k = weights[i].Kind;
                    if (k == RatingKind.Stamina || k == RatingKind.Injury) continue;
                    r.Set(k, r.Get(k) + shift);
                }
            }
        }

        private static void Measurables(Position pos, Archetype arch, Rng rng, out int heightIn, out int weightLb)
        {
            float h, hs, wt, ws;
            switch (pos)
            {
                case Position.QB: h = 75f; hs = 1.7f; wt = 222f; ws = 12f; break;
                case Position.RB: h = 70f; hs = 1.6f; wt = 212f; ws = 14f; break;
                case Position.FB: h = 72f; hs = 1.5f; wt = 245f; ws = 10f; break;
                case Position.WR: h = 73f; hs = 2.4f; wt = 200f; ws = 15f; break;
                case Position.TE: h = 77f; hs = 1.5f; wt = 252f; ws = 10f; break;
                case Position.LT: case Position.LG: case Position.C: case Position.RG: case Position.RT:
                    h = 77f; hs = 1.6f; wt = 315f; ws = 17f; break;
                case Position.LE: case Position.RE: h = 76.5f; hs = 1.7f; wt = 270f; ws = 14f; break;
                case Position.DT: h = 75f; hs = 1.5f; wt = 308f; ws = 17f; break;
                case Position.LOLB: case Position.ROLB: h = 75f; hs = 1.5f; wt = 245f; ws = 10f; break;
                case Position.MLB: h = 73.5f; hs = 1.4f; wt = 242f; ws = 8f; break;
                case Position.CB: h = 71f; hs = 1.7f; wt = 195f; ws = 10f; break;
                case Position.FS: case Position.SS: h = 72f; hs = 1.6f; wt = 205f; ws = 10f; break;
                case Position.K: h = 72f; hs = 2f; wt = 195f; ws = 10f; break;
                default: h = 74f; hs = 2f; wt = 208f; ws = 10f; break;
            }
            switch (arch)
            {
                case Archetype.RbPower: case Archetype.OlRunMauler: case Archetype.DlRunStuffer: case Archetype.TeBlocker: case Archetype.SBox:
                    wt += 12f; break;
                case Archetype.RbSpeed: case Archetype.WrSlot: case Archetype.CbSlot: case Archetype.QbScrambler:
                    wt -= 8f; h -= 0.8f; break;
                case Archetype.WrDeepThreat: case Archetype.TeReceiver: wt -= 5f; h += 0.6f; break;
            }
            heightIn = (int)Math.Round(h + rng.Gaussian(0f, hs));
            weightLb = (int)Math.Round(wt + rng.Gaussian(0f, ws));
            if (heightIn < 62) heightIn = 62;
            if (weightLb < 150) weightLb = 150;
        }

        public static DevTrait RollDev(int potential, Rng rng)
        {
            float r = rng.NextFloat();
            float starShift = Math.Max(0f, (potential - 70) / 29f); // 0..1
            if (r < 0.015f + 0.05f * starShift) return DevTrait.Superstar;
            if (r < 0.05f + 0.22f * starShift) return DevTrait.Star;
            if (r < 0.12f + 0.15f * starShift) return DevTrait.Late;
            return DevTrait.Normal;
        }

        /// <summary>Current overall expected for a player with the given ceiling at <paramref name="age"/>.</summary>
        public static int CurrentFromPeak(Position pos, int age, int potential, DevTrait dev, Rng rng)
        {
            int peak = AgeCurve.PeakStart(pos);
            if (dev == DevTrait.Late) peak += 2;
            int decl = AgeCurve.DeclineStart(pos);
            float cur = potential;
            if (age < peak)
            {
                float rate = 2.4f / AgeCurve.DevGrowthFactor(dev);
                float gap = (peak - age) * rate + rng.Gaussian(0f, 1.8f);
                if (gap < 0f) gap = 0f;
                cur -= gap;
            }
            else if (age > decl)
            {
                int n = age - decl;
                cur -= (1.4f * n + 0.22f * n * (n + 1) * 0.5f) * (0.75f + 0.5f * rng.NextFloat());
            }
            int c = (int)Math.Round(cur);
            return Math.Max(35, Math.Min(99, c));
        }

        public static Player Create(Rng rng, IPlayerIdSource ids, HashSet<string> usedNames, Position pos, int potential, int age,
            Archetype arch = Archetype.None, DevTrait? devOverride = null, bool collegeStyle = false, int collegeGap = 0)
        {
            potential = Math.Max(40, Math.Min(99, potential));
            if (arch == Archetype.None) arch = RandomArchetype(pos, rng);
            DevTrait dev = devOverride ?? RollDev(potential, rng);
            // college players are rated near their ceiling minus what the class year still has to grow (QA-556), not on the pro age curve whose peak is 25-28
            int cur = collegeStyle ? Math.Max(35, potential - Math.Max(0, collegeGap)) : CurrentFromPeak(pos, age, potential, dev, rng);
            if (cur > potential) cur = potential;
            var p = new Player { Position = pos, Archetype = arch, Potential = potential, Dev = dev };
            p.Ratings = BuildRatings(pos, arch, cur, rng);
            string first, last;
            NameGenerator.PersonName(rng, usedNames, out first, out last);
            int h, wt;
            Measurables(pos, arch, rng, out h, out wt);
            p.Bio = new PlayerBio
            {
                Id = ids.NextPlayerId(), FirstName = first, LastName = last, Age = age, HeightIn = h, WeightLb = wt,
                Hometown = NameGenerator.Hometown(rng), College = "", Hand = rng.Chance(pos == Position.QB ? 0.09f : 0.11f) ? Handedness.Left : Handedness.Right
            };
            p.Morale = rng.Range(55, 90);
            p.Experience = Math.Max(0, age - 22);
            p.Class = ClassYear.Senior;
            return p;
        }
    }

    /// <summary>Weighted team rating from depth chart starters.</summary>
    public static class TeamRating
    {
        private static float Weight(Position p, int slot)
        {
            switch (p)
            {
                case Position.QB: return 0.16f;
                case Position.RB: return 0.04f;
                case Position.FB: return 0.01f;
                case Position.WR: return 0.04f;
                case Position.TE: return 0.035f;
                case Position.LT: case Position.LG: case Position.C: case Position.RG: case Position.RT: return 0.034f;
                case Position.LE: case Position.RE: return 0.04f;
                case Position.DT: return 0.04f;
                case Position.LOLB: case Position.MLB: case Position.ROLB: return 0.033f;
                case Position.CB: return 0.04f;
                case Position.FS: case Position.SS: return 0.035f;
                case Position.K: return 0.02f;
                default: return 0.02f;
            }
        }

        public static int Compute(Team t)
        {
            double sum = 0, tot = 0;
            for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
            {
                Position p = LeagueRules.RosterOrder[i];
                List<int> ids;
                int starters = LeagueRules.Starters(p);
                if (!t.Depth.TryGet(p, out ids)) continue;
                for (int s = 0; s < starters && s < ids.Count; s++)
                {
                    Player pl = t.GetPlayer(ids[s]);
                    if (pl == null) continue;
                    float w = Weight(p, s);
                    if ((s >= 3 && (p == Position.WR || p == Position.CB)) || (s >= 1 && p == Position.TE)) w *= 0.5f;   // QA-577: the 4th WR, 2nd TE and 4th CB count at half weight
                    sum += w * pl.Ratings.Overall(p);
                    tot += w;
                }
            }
            return tot <= 0 ? 0 : (int)Math.Round(sum / tot);
        }
    }

    /// <summary>Builds and maintains depth charts.</summary>
    public static class DepthChartBuilder
    {
        private static Position[] Related(Position p)
        {
            switch (p)
            {
                case Position.QB: return new Position[0];
                case Position.RB: return new[] { Position.FB, Position.WR };
                case Position.FB: return new[] { Position.RB, Position.TE };
                case Position.WR: return new[] { Position.RB, Position.TE, Position.CB };
                case Position.TE: return new[] { Position.FB, Position.WR, Position.RT, Position.LT };
                case Position.LT: return new[] { Position.RT, Position.LG, Position.RG, Position.C };
                case Position.RT: return new[] { Position.LT, Position.RG, Position.LG, Position.C };
                case Position.LG: return new[] { Position.RG, Position.C, Position.LT, Position.RT };
                case Position.RG: return new[] { Position.LG, Position.C, Position.RT, Position.LT };
                case Position.C: return new[] { Position.LG, Position.RG, Position.LT, Position.RT };
                case Position.LE: return new[] { Position.RE, Position.DT, Position.LOLB, Position.ROLB };
                case Position.RE: return new[] { Position.LE, Position.DT, Position.ROLB, Position.LOLB };
                case Position.DT: return new[] { Position.LE, Position.RE };
                case Position.LOLB: return new[] { Position.ROLB, Position.MLB, Position.LE, Position.RE };
                case Position.ROLB: return new[] { Position.LOLB, Position.MLB, Position.RE, Position.LE };
                case Position.MLB: return new[] { Position.LOLB, Position.ROLB, Position.SS };
                case Position.CB: return new[] { Position.FS, Position.SS };
                case Position.FS: return new[] { Position.SS, Position.CB };
                case Position.SS: return new[] { Position.FS, Position.CB, Position.MLB };
                case Position.K: return new[] { Position.P };
                case Position.P: return new[] { Position.K };
            }
            return new Position[0];
        }

        /// <summary>Rebuilds the entire depth chart by overall rating. Injured players sort last.</summary>
        public static void Build(Team team)
        {
            var chart = new DepthChart();
            var used = new HashSet<int>();
            // pass 1: natural position players, best first
            var perPos = new Dictionary<Position, List<Player>>();
            for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
            {
                Position p = LeagueRules.RosterOrder[i];
                var list = new List<Player>();
                for (int j = 0; j < team.Roster.Count; j++) if (team.Roster[j].Position == p && !team.Roster[j].Retired && !team.Roster[j].IsRedshirting) list.Add(team.Roster[j]);
                list.Sort((a, b) => Rank(b, p).CompareTo(Rank(a, p)));
                perPos[p] = list;
            }
            // pass 2: fill holes from related positions (using the best unused backups)
            for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
            {
                Position p = LeagueRules.RosterOrder[i];
                var list = perPos[p];
                int need = LeagueRules.Starters(p);
                if (list.Count >= need) continue;
                var rel = Related(p);
                var cands = new List<Player>();
                for (int r = 0; r < rel.Length; r++)
                {
                    var src = perPos[rel[r]];
                    int keep = LeagueRules.Starters(rel[r]);
                    for (int j = keep; j < src.Count; j++) cands.Add(src[j]);
                }
                cands.Sort((a, b) => Rank(b, p).CompareTo(Rank(a, p)));
                for (int c = 0; c < cands.Count && list.Count < need; c++)
                    if (!list.Contains(cands[c]) && used.Add(cands[c].Id)) list.Add(cands[c]);   // QA-578: a backup fills one hole only
            }
            for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
            {
                Position p = LeagueRules.RosterOrder[i];
                var slot = new DepthSlot { Pos = p };
                var list = perPos[p];
                for (int j = 0; j < list.Count; j++) slot.PlayerIds.Add(list[j].Id);
                chart.Slots.Add(slot);
            }
            team.Depth = chart;
            team.InvalidateIndex();
        }

        private static int Rank(Player pl, Position slot)
        {
            int o = pl.Ratings.Overall(slot);
            return pl.Injury.IsInjured ? o - 200 : o;
        }
    }
}
