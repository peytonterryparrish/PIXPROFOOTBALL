using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    public enum OfferVerdict { Accepted, Counter, Declined, Refuses, WalkedAway, Rejected }

    /// <summary>What a player wants for a new deal.</summary>
    public sealed class ContractAsk
    {
        public long Aav;
        public int Years;
        public float BonusPct;
        /// <summary>False when he will not talk (he wants to test the market).</summary>
        public bool WillNegotiate = true;
        /// <summary>0-100 interest in staying.</summary>
        public int Interest;
        public string Mood = "";
        public List<string> Notes = new List<string>();
    }

    public sealed class OfferResult
    {
        public OfferVerdict Verdict;
        public long CounterAav;
        public int CounterYears;
        public string Message = "";
        public Contract Contract;
        public bool Accepted { get { return Verdict == OfferVerdict.Accepted; } }
    }

    /// <summary>
    /// Re-signing and extensions. The asking price comes from overall (and ceiling for the young), age, position and the cap-scaled market,
    /// then moves with morale, team success and his role. An offer is accepted, countered, declined or the player walks away after three rounds.
    /// </summary>
    public static class Negotiator
    {
        public const int MaxRounds = 3;

        /// <summary>Stable per-player value in [0,1) so a player's ask does not change every time the screen opens.</summary>
        public static double Jitter(int playerId, int salt)
        {
            unchecked
            {
                uint h = (uint)(playerId * 2654435761u) ^ (uint)(salt * 40503u);
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (h % 10000u) / 10000.0;
            }
        }

        public static int YearsFor(int age)
        {
            return age <= 25 ? 5 : (age <= 28 ? 4 : (age <= 30 ? 3 : (age <= 33 ? 2 : 1)));
        }

        public static int StartYearNow(CareerSave s)
        {
            switch (s.Phase)
            {
                case CareerPhase.Preseason: case CareerPhase.RegularSeason: case CareerPhase.Playoffs: case CareerPhase.PixBowl: return s.Year;
                default: return s.Year + 1;
            }
        }

        /// <summary>Players whose contract ran out and who can be re-signed during the window.</summary>
        public static List<Player> ExpiringPlayers(CareerSave s, Team t)
        {
            var l = new List<Player>();
            foreach (Player p in CapManager.AllUnderContract(t)) if (p.Contract.YearsLeft <= 0 && !t.PracticeSquad.Contains(p)) l.Add(p);
            l.Sort((a, b) => b.Overall.CompareTo(a.Overall));
            return l;
        }

        public static Negotiation StateOf(CareerSave s, int teamId, int playerId, bool create)
        {
            for (int i = 0; i < s.Negotiations.Count; i++)
                if (s.Negotiations[i].TeamId == teamId && s.Negotiations[i].PlayerId == playerId && s.Negotiations[i].Year == s.Year) return s.Negotiations[i];
            if (!create) return null;
            var n = new Negotiation { TeamId = teamId, PlayerId = playerId, Year = s.Year };
            s.Negotiations.Add(n);
            return n;
        }

        /// <summary>The player's asking terms with the factors behind them (shown on the negotiation screen).</summary>
        public static ContractAsk Ask(CareerSave s, Team t, Player p)
        {
            int startYear = StartYearNow(s);
            var ask = new ContractAsk();
            long market = CapMath.Market(s, p, startYear);
            double f = 0.96 + 0.10 * Jitter(p.Id, startYear);
            f *= 1.0 + (60 - p.Morale) * 0.0025;
            if (p.Morale >= 80) ask.Notes.Add("Happy here: hometown discount.");
            else if (p.Morale < 45) ask.Notes.Add("Unhappy: wants a premium to stay.");
            float win = MoraleEngine.WinPct(s, t.Id);
            f *= 1.0 + (0.5 - win) * 0.12;
            if (win >= 0.65f) ask.Notes.Add("Likes winning with this team.");
            else if (win <= 0.35f) ask.Notes.Add("Losing makes him pricey.");
            bool starter = Offseason.IsStarter(t, p);
            if (!starter && p.Overall >= 68 && p.Overall >= Offseason.BestOvr(t, p.Position) - 2f) { f *= 1.03; ask.Notes.Add("Wants a bigger role."); }
            if (p.Contract.Rookie && p.Bio.Age <= 25 && p.Overall >= 75) { f *= 1.06; ask.Notes.Add("Coming off a rookie deal: leverage."); }
            ask.Aav = Math.Max(CapMath.MinSalary(s, startYear), CapMath.Round(market * f));
            ask.Years = YearsFor(p.Bio.Age);
            ask.BonusPct = CapMath.DefaultBonusPct(s, ask.Aav, startYear);
            PlayerMoraleInfo mi = MoraleEngine.Breakdown(s, t, p);
            ask.Mood = mi.Mood;
            ask.Interest = Math.Max(0, Math.Min(100, p.Morale + (int)((win - 0.5f) * 40f) + (starter ? 8 : -4)));
            if (p.Morale < 30 && win < 0.45f && p.Overall >= 68) { ask.WillNegotiate = false; ask.Notes.Add("Refuses to negotiate: wants to test free agency."); }
            Negotiation n = StateOf(s, t.Id, p.Id, false);
            if (n != null && (n.State == NegotiationState.Walked || n.State == NegotiationState.Declined) && n.Rounds >= MaxRounds) { ask.WillNegotiate = false; ask.Notes.Add("Talks broke down."); }
            return ask;
        }

        /// <summary>
        /// The user (or a script) offers a deal. Accepted offers are applied at once (re-sign when the contract expired, extension otherwise)
        /// as long as the cap allows it. A counter-offer keeps the talks open for up to three rounds.
        /// </summary>
        public static OfferResult Offer(CareerSave s, Team t, int playerId, long aav, int years, float bonusPct)
        {
            var r = new OfferResult();
            Player p = t.FindAnyPlayer(playerId);
            if (p == null) { r.Verdict = OfferVerdict.Rejected; r.Message = "The player is not on this team."; return r; }
            bool expired = p.Contract.YearsLeft <= 0;
            bool windowOpen = s.Phase == CareerPhase.ReSign;
            bool inSeasonExtension = !expired && (s.Phase == CareerPhase.RegularSeason || s.Phase == CareerPhase.Preseason || s.Phase == CareerPhase.TrainingCamp) && p.Contract.YearsLeft <= 1;
            if (!(expired && windowOpen) && !inSeasonExtension)
            { r.Verdict = OfferVerdict.Rejected; r.Message = expired ? "Contracts can only be renewed during the re-sign window." : "He is under contract; extensions are only possible in the final year."; return r; }
            if (years < 1 || years > 6) { r.Verdict = OfferVerdict.Rejected; r.Message = "Contracts run 1 to 6 years."; return r; }

            ContractAsk ask = Ask(s, t, p);
            Negotiation n = StateOf(s, t.Id, p.Id, true);
            if (n.State == NegotiationState.Signed) { r.Verdict = OfferVerdict.Rejected; r.Message = "Already signed."; return r; }
            if (!ask.WillNegotiate)
            {
                n.State = NegotiationState.Declined;
                r.Verdict = OfferVerdict.Refuses; r.Message = p.Name + " will not negotiate: he wants to test the market.";
                return r;
            }
            // value of the offer against the ask: money, with a small premium for guaranteed bonus money
            double ratio = (double)aav / Math.Max(1L, ask.Aav);
            ratio *= 1.0 + (bonusPct - ask.BonusPct) * 0.25;
            if (p.Bio.Age >= 31 && years > ask.Years) ratio *= 1.0 + 0.03 * (years - ask.Years);   // old players like long security
            double accept = 1.0 - Math.Max(0, (p.Morale - 75)) * 0.002;   // a happy player gives up to 5 percent
            n.Rounds++;
            if (ratio >= accept)
            {
                int startYear = StartYearNow(s);
                Contract c = CapMath.Veteran(s, aav, years, bonusPct, startYear);
                OpResult applied = Apply(s, t, p, c, expired);
                if (!applied.Ok) { r.Verdict = OfferVerdict.Rejected; r.Message = applied.Message; n.Rounds--; return r; }
                n.State = NegotiationState.Signed;
                r.Verdict = OfferVerdict.Accepted; r.Contract = p.Contract; r.Message = p.Name + " agrees to " + years + " years at " + CareerText.Money(aav) + " per year.";
                return r;
            }
            if (n.Rounds >= MaxRounds)
            {
                n.State = NegotiationState.Walked;
                r.Verdict = OfferVerdict.WalkedAway; r.Message = p.Name + " has had enough and ends the talks.";
                return r;
            }
            if (ratio >= 0.88)
            {
                r.Verdict = OfferVerdict.Counter; r.CounterAav = ask.Aav; r.CounterYears = ask.Years;
                n.LastCounterAav = ask.Aav; n.LastCounterYears = ask.Years;
                r.Message = p.Name + " counters: " + ask.Years + " years at " + CareerText.Money(ask.Aav) + " per year.";
                return r;
            }
            r.Verdict = OfferVerdict.Declined; r.CounterAav = ask.Aav; r.CounterYears = ask.Years;
            r.Message = "Not even close. He is looking for about " + CareerText.Money(ask.Aav) + " per year.";
            return r;
        }

        /// <summary>Puts a new contract on a player: replaces an expired deal or extends a deal that is in its last year.</summary>
        internal static OpResult Apply(CareerSave s, Team t, Player p, Contract c, bool expired)
        {
            if (expired)
            {
                if (t.CapUsed + c.CapHit > t.SalaryCap) return OpResult.Fail("Not enough cap space: the first-year cap hit is " + CareerText.Money(c.CapHit) + ", you have " + CareerText.Money(t.SalaryCap - t.CapUsed) + ".");
                p.Contract = c;
                Ledger.Tx(s, TransactionKind.ReSigning, t.Id, p, c.Aav, p.Name + " re-signed: " + c.Years + " years, " + CareerText.Money(c.Aav) + " per year.");
                NewsWriter.Signing(s, t, p, c.Aav, c.Years, 80, true);
                return OpResult.Success();
            }
            Contract old = p.Contract;
            int idx = old.YearIndex;
            var merged = old.Clone();
            // keep the years already played (and the current one), then the new deal's years
            while (merged.CapHits.Count > idx + 1) merged.CapHits.RemoveAt(merged.CapHits.Count - 1);
            long hitBefore = old.CapHit;
            for (int i = 0; i < c.CapHits.Count; i++) merged.CapHits.Add(c.CapHits[i]);
            merged.Years = merged.CapHits.Count;
            merged.YearsLeft = 1 + c.Years;
            merged.Aav = c.Aav;
            merged.SigningBonus = old.SigningBonus + c.SigningBonus;
            merged.Guaranteed = old.Guaranteed + c.Guaranteed;
            merged.Rookie = false;
            p.Contract = merged;
            Ledger.Tx(s, TransactionKind.Extension, t.Id, p, c.Aav, p.Name + " extended: " + c.Years + " more years at " + CareerText.Money(c.Aav) + ".");
            NewsWriter.Signing(s, t, p, c.Aav, c.Years, 80, true);
            return OpResult.Success();
        }

        // ---------------------------------------------------------------- CPU decisions
        /// <summary>The chance a CPU team keeps one of its expiring players (before cap checks).</summary>
        public static float CpuKeepProbability(CareerSave s, Team t, Player p)
        {
            int ovr = p.Overall;
            float prob = ovr < 60 ? 0.2f : Math.Min(0.95f, 0.4f + (ovr - 60) * 0.025f);
            if (Offseason.IsStarter(t, p)) prob = Math.Min(0.97f, prob + 0.2f);
            if (p.Bio.Age >= 33) prob *= 0.6f;
            return Math.Max(0.02f, Math.Min(0.99f, prob + (p.Morale - 70) * 0.002f));
        }

        /// <summary>
        /// Closes the re-sign window for the CPU teams: each decides on its expiring players (best first) within the cap, may use the franchise tag
        /// on a star it cannot keep, and lets the rest go to free agency. The user's expiring players are handled by <see cref="ReleaseUnsigned"/>.
        /// </summary>
        public static void CpuReSign(CareerSave s, Rng rng)
        {
            for (int ti = 0; ti < s.Pro.Teams.Count; ti++)
            {
                Team t = s.Pro.Teams[ti];
                if (t.Id == s.UserTeamId) continue;
                ReSignTeam(s, t, rng);
            }
        }

        /// <summary>One team's re-sign decisions (what the CPU does at the end of the window; the auto-pilot uses it for the user too).</summary>
        public static void ReSignTeam(CareerSave s, Team t, Rng rng)
        {
            int startYear = s.Year + 1;
            {
                var exp = ExpiringPlayers(s, t);
                exp.Sort((a, b) => Offseason.Value(b).CompareTo(Offseason.Value(a)));
                bool tagged = false;
                for (int i = 0; i < exp.Count; i++)
                {
                    Player p = exp[i];
                    float prob = CpuKeepProbability(s, t, p);
                    bool keep = rng.Chance(prob);
                    ContractAsk ask = Ask(s, t, p);
                    Contract c = CapMath.Veteran(s, ask.Aav, Math.Min(ask.Years, p.Bio.Age >= 31 ? 2 : ask.Years), ask.BonusPct, startYear);
                    if (keep && t.CapUsed + c.CapHit <= t.SalaryCap)
                    {
                        p.Contract = c;
                        Ledger.Tx(s, TransactionKind.ReSigning, t.Id, p, c.Aav, p.Name + " re-signed.");
                        NewsWriter.Signing(s, t, p, c.Aav, c.Years, 84, true);
                        continue;
                    }
                    if (!tagged && p.Overall >= 84 && CapManager.TagAvailable(s, t.Id) && rng.Chance(0.5f))
                    {
                        long cost = CapManager.TagCost(s, p);
                        var tc = Contract.Create(1, cost, 0f, 1f, startYear);
                        if (t.CapUsed + tc.CapHit <= t.SalaryCap)
                        {
                            CapManager.ApplyTag(s, t, p, cost);
                            tagged = true;
                            continue;
                        }
                    }
                    ReleaseToMarket(s, t, p);
                }
            }
        }

        /// <summary>Everybody on the user's team with an expired contract goes to the free agent pool.</summary>
        public static int ReleaseUnsigned(CareerSave s, Team t)
        {
            var exp = ExpiringPlayers(s, t);
            for (int i = 0; i < exp.Count; i++) ReleaseToMarket(s, t, exp[i]);
            return exp.Count;
        }

        /// <summary>An expired contract: he leaves for free agency without dead money.</summary>
        internal static void ReleaseToMarket(CareerSave s, Team t, Player p)
        {
            RosterManager.RemoveFromTeam(t, p);
            RosterManager.RememberPrior(s, p, t.Id);
            p.TeamId = -1;
            p.Contract = new Contract();
            p.Injury.IrWeeks = -1;
            s.Pro.FreeAgents.Add(p);
            CapManager.CloseNegotiation(s, t.Id, p.Id, NegotiationState.Walked);
        }
    }
}
