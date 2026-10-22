using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>Salary cap growth and cap-scaled contract money (the league constants in <see cref="ContractModel"/> are for the first year).</summary>
    public static class CapMath
    {
        public const long PracticeSquadBase = 400000L;

        public static double Scale(CareerSave s, int year)
        {
            return Math.Pow(1.0 + s.CapGrowth, Math.Max(0, year - s.StartYear));
        }

        public static long Round(double v) { return ((long)v / 10000L) * 10000L; }

        /// <summary>The salary cap of a season year (255M in the first year, +5 percent a year).</summary>
        public static long CapFor(CareerSave s, int year) { return Round(s.CapBase * Scale(s, year)); }

        public static long Scaled(CareerSave s, long baseDollars, int year) { return Round(baseDollars * Scale(s, year)); }

        public static long MinSalary(CareerSave s, int year) { return Math.Max(Contract.MinSalary, Round(Contract.MinSalary * Scale(s, year))); }

        public static long PracticeSquadSalary(CareerSave s, int year) { return Math.Max(PracticeSquadBase, Round(PracticeSquadBase * Scale(s, year))); }

        /// <summary>Overall used for pricing: young players are priced partly on their ceiling.</summary>
        public static float PricingOverall(Player p)
        {
            float ovr = p.Ratings.Overall(p.Position);
            if (p.Bio.Age <= 26 && p.Potential > ovr) ovr += (p.Potential - ovr) * (p.Bio.Age <= 23 ? 0.35f : 0.2f);
            return ovr;
        }

        /// <summary>Market average annual value of a player in a season year, scaled with the cap.</summary>
        public static long Market(CareerSave s, Position pos, float ovr, int age, int year)
        {
            long baseAav = ContractModel.MarketAav(pos, (int)Math.Round(ovr), age);
            return Math.Max(MinSalary(s, year), Scaled(s, baseAav, year));
        }

        public static long Market(CareerSave s, Player p, int year) { return Market(s, p.Position, PricingOverall(p), p.Bio.Age, year); }

        /// <summary>A veteran contract: bonus and guarantee shares grow with the money.</summary>
        public static Contract Veteran(CareerSave s, long aav, int years, float bonusPct, int startYear)
        {
            aav = Math.Max(MinSalary(s, startYear), Round(aav));
            float guaranteed = Math.Min(0.85f, 0.1f + (aav - MinSalary(s, startYear)) / (40000000f * (float)Scale(s, startYear)));
            return Contract.Create(years, aav, Math.Max(0f, Math.Min(0.45f, bonusPct)), guaranteed, startYear, false);
        }

        public static float DefaultBonusPct(CareerSave s, long aav, int year)
        {
            return Math.Min(0.45f, 0.1f + (aav - MinSalary(s, year)) / (60000000f * (float)Scale(s, year)));
        }

        /// <summary>Rookie scale contract for a draft slot (pick 1-224; anything above 260 is an undrafted deal), scaled with the cap.</summary>
        public static Contract Rookie(CareerSave s, int overallPick, int startYear)
        {
            Contract c = ContractModel.RookieContract(overallPick, startYear);
            double f = Scale(s, startYear);
            if (f > 1.0001) c = ContractModel.Scale(c, f);
            long minimum = MinSalary(s, startYear);
            if (c.Aav < minimum) c = ContractModel.Scale(c, (double)minimum / Math.Max(1L, c.Aav));
            return c;
        }

        /// <summary>A 1-year practice squad deal (below the minimum salary).</summary>
        public static Contract PracticeSquad(CareerSave s, int startYear)
        {
            long hit = PracticeSquadSalary(s, startYear);
            var c = new Contract { Years = 1, YearsLeft = 1, Aav = hit, SigningBonus = 0, Guaranteed = 0, StartYear = startYear };
            c.CapHits.Add(hit);
            return c;
        }

        /// <summary>A 1-year minimum-salary deal.</summary>
        public static Contract Minimum(CareerSave s, int years, int startYear)
        {
            return Contract.Create(years, MinSalary(s, startYear), 0f, 0f, startYear);
        }
    }

    /// <summary>One line of the cap ledger screen.</summary>
    public sealed class CapRow
    {
        public int PlayerId;
        public string Name;
        public Position Pos;
        public int Ovr, Age;
        public long CapHitThisYear, CapHitNextYear;
        public int YearsLeft;
        public long Aav, Guaranteed;
        public long DeadIfReleased;
        public string Status;
    }

    public sealed class CapReport
    {
        public int TeamId;
        public long Cap, Used, Space, Dead, DeadNext;
        public long CommittedNextYear;
        public int ActiveCount, PracticeSquadCount, InjuredReserveCount;
        public bool Compliant;
    }

    /// <summary>Salary cap bookkeeping: ledger rows, restructures, releases with dead money, compliance and the franchise tag.</summary>
    public static class CapManager
    {
        public static CapReport Report(CareerSave s, Team t)
        {
            var r = new CapReport
            {
                TeamId = t.Id, Cap = t.SalaryCap, Used = t.CapUsed, Dead = t.DeadCap, DeadNext = t.DeadCapNext,
                ActiveCount = t.Roster.Count, PracticeSquadCount = t.PracticeSquad.Count, InjuredReserveCount = t.InjuredReserve.Count
            };
            r.Space = r.Cap - r.Used;
            r.Compliant = r.Used <= r.Cap;
            long next = t.DeadCapNext;
            foreach (Player p in AllUnderContract(t)) next += CapHitNext(p.Contract);
            r.CommittedNextYear = next;
            return r;
        }

        public static IEnumerable<Player> AllUnderContract(Team t)
        {
            for (int i = 0; i < t.Roster.Count; i++) yield return t.Roster[i];
            for (int i = 0; i < t.PracticeSquad.Count; i++) yield return t.PracticeSquad[i];
            for (int i = 0; i < t.InjuredReserve.Count; i++) yield return t.InjuredReserve[i];
        }

        public static long CapHitNext(Contract c)
        {
            if (c.YearsLeft <= 1 || c.CapHits.Count == 0) return 0L;
            int idx = c.YearIndex + 1;
            return idx < c.CapHits.Count ? c.CapHits[idx] : 0L;
        }

        /// <summary>The cap ledger of a team, biggest cap hit first.</summary>
        public static List<CapRow> LedgerRows(Team t)
        {
            var rows = new List<CapRow>();
            foreach (Player p in AllUnderContract(t))
            {
                string status = t.Roster.Contains(p) ? "Active" : (t.PracticeSquad.Contains(p) ? "Practice squad" : "Injured reserve");
                rows.Add(new CapRow
                {
                    PlayerId = p.Id, Name = p.Name, Pos = p.Position, Ovr = p.Overall, Age = p.Age,
                    CapHitThisYear = p.Contract.CapHit, CapHitNextYear = CapHitNext(p.Contract), YearsLeft = p.Contract.YearsLeft,
                    Aav = p.Contract.Aav, Guaranteed = p.Contract.Guaranteed, DeadIfReleased = Offseason.DeadMoneyOf(p.Contract), Status = status
                });
            }
            rows.Sort((a, b) => b.CapHitThisYear.CompareTo(a.CapHitThisYear));
            return rows;
        }

        /// <summary>What a release would put on the books this year and next (the UI shows it before the user confirms).</summary>
        public static void DeadMoneyPreview(Contract c, out long now, out long next)
        {
            long dead = Offseason.DeadMoneyOf(c);
            now = c.YearsLeft > 1 ? dead / 2 : dead;
            next = dead - now;
        }

        /// <summary>Cap space freed by releasing a player (his cap hit minus the dead money that lands this year).</summary>
        public static long SavingsIfReleased(Player p)
        {
            long now, next;
            DeadMoneyPreview(p.Contract, out now, out next);
            return p.Contract.CapHit - now;
        }

        public static bool IsCompliant(Team t) { return t.CapUsed <= t.SalaryCap; }

        /// <summary>Restructure: half of this year's base salary becomes a prorated bonus (needs two or more contract years left).</summary>
        public static OpResult Restructure(CareerSave s, Team t, int playerId)
        {
            Player p = t.FindAnyPlayer(playerId);
            if (p == null) return OpResult.Fail("The player is not on this team.");
            long room = Offseason.RestructureRoom(p.Contract);
            if (room <= 0) return OpResult.Fail("This contract cannot be restructured (needs 2+ years left and a base salary above $2M).");
            long before = p.Contract.CapHit;
            Offseason.Restructure(p.Contract);
            Ledger_(s, TransactionKind.Restructure, t, p, before - p.Contract.CapHit, "Restructured: saved " + CareerText.Money(before - p.Contract.CapHit) + " of cap this year.");
            return OpResult.Success("Saved " + CareerText.Money(before - p.Contract.CapHit) + " this year.");
        }

        private static void Ledger_(CareerSave s, TransactionKind k, Team t, Player p, long amount, string text)
        {
            PixFootball.League.Ledger.Tx(s, k, t.Id, p, amount, text);
        }

        /// <summary>
        /// Gets a team under the cap: restructures, then releases expensive backups, then trims the biggest deal (never lets the active roster fall
        /// below the minimum of a legal one: callers fill the roster afterwards). Returns the number of moves.
        /// </summary>
        public static int AutoFix(CareerSave s, Team t)
        {
            if (IsCompliant(t)) return 0;
            var rep = new OffseasonReport();
            Offseason.EnforceCap(t, rep, s.Pro, true);
            return rep.Restructures + rep.Cuts;
        }

        // ---------------------------------------------------------------- franchise tag
        public static bool TagAvailable(CareerSave s, int teamId)
        {
            for (int i = 0; i < s.TagUses.Count; i++) if (s.TagUses[i].TeamId == teamId && s.TagUses[i].Year == s.Year) return false;
            return true;
        }

        /// <summary>Franchise tag salary: average of the five biggest contracts at the position bucket, and at least 120 percent of his last salary.</summary>
        public static long TagCost(CareerSave s, Player p)
        {
            PositionBucket b = PositionBuckets.Of(p.Position);
            var top = new List<long>();
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                Team tm = s.Pro.Teams[t];
                foreach (Player q in AllUnderContract(tm))
                    if (q.Contract.YearsLeft > 0 && PositionBuckets.Of(q.Position) == b) top.Add(q.Contract.Aav);
            }
            top.Sort((a, c) => c.CompareTo(a));
            long sum = 0; int n = Math.Min(5, top.Count);
            for (int i = 0; i < n; i++) sum += top[i];
            long avg = n > 0 ? sum / n : CapMath.MinSalary(s, s.Year + 1);
            long floor = (long)(Math.Max(p.Contract.Aav, 1L) * 1.2);
            return CapMath.Round(Math.Max(avg, floor));
        }

        public static OpResult FranchiseTag(CareerSave s, Team t, int playerId)
        {
            if (s.Phase != CareerPhase.ReSign) return OpResult.Fail("The franchise tag can only be used in the re-sign window.");
            Player p = t.FindAnyPlayer(playerId);
            if (p == null) return OpResult.Fail("The player is not on this team.");
            if (p.Contract.YearsLeft > 0) return OpResult.Fail("Only players with an expiring contract can be tagged.");
            if (!TagAvailable(s, t.Id)) return OpResult.Fail("The franchise tag was already used this offseason.");
            long cost = TagCost(s, p);
            return ApplyTag(s, t, p, cost);
        }

        internal static OpResult ApplyTag(CareerSave s, Team t, Player p, long cost)
        {
            var c = Contract.Create(1, cost, 0f, 1f, s.Year + 1);
            long other = t.CapUsed;
            if (other + c.CapHit > t.SalaryCap) return OpResult.Fail("Not enough cap space for the tag (" + CareerText.Money(cost) + ").");
            p.Contract = c;
            s.TagUses.Add(new TagUse { TeamId = t.Id, Year = s.Year, PlayerId = p.Id });
            CloseNegotiation(s, t.Id, p.Id, NegotiationState.Signed);
            Ledger_(s, TransactionKind.FranchiseTag, t, p, cost, p.Name + " franchise tagged at " + CareerText.Money(cost) + ".");
            NewsWriter.Post(s, NewsKind.ReSigning, t.Nickname + " place franchise tag on " + p.Name, CareerText.PlayerLabel(p) + " will earn " + CareerText.Money(cost) + " on a one-year tag.", p.Overall >= 85 ? 4 : 3, t.Id, -1, p.Id);
            return OpResult.Success("Tagged for " + CareerText.Money(cost) + ".");
        }

        internal static void CloseNegotiation(CareerSave s, int teamId, int playerId, NegotiationState state)
        {
            for (int i = 0; i < s.Negotiations.Count; i++)
                if (s.Negotiations[i].TeamId == teamId && s.Negotiations[i].PlayerId == playerId && s.Negotiations[i].Year == s.Year)
                { s.Negotiations[i].State = state; return; }
        }

        // ---------------------------------------------------------------- compliance
        /// <summary>Teams that break the cap (empty when the league is compliant). Used by the deadlines and by the soak test.</summary>
        public static List<int> NonCompliantTeams(CareerSave s)
        {
            var l = new List<int>();
            for (int i = 0; i < s.Pro.Teams.Count; i++) if (!IsCompliant(s.Pro.Teams[i])) l.Add(s.Pro.Teams[i].Id);
            return l;
        }

        /// <summary>Updates every team's cap to the cap of a season year.</summary>
        public static void ApplyCap(CareerSave s, int year)
        {
            long cap = CapMath.CapFor(s, year);
            for (int i = 0; i < s.Pro.Teams.Count; i++) s.Pro.Teams[i].SalaryCap = cap;
        }
    }
}
