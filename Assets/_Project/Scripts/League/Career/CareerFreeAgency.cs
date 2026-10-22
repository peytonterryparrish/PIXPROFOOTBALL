using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    public enum FaPriority { Money, Contender, Role, Loyalty }

    /// <summary>How a free agent judged one offer: each component 0-1 and the weighted score against the day's threshold.</summary>
    public sealed class OfferEvaluation
    {
        public float Score, Threshold;
        public float Money, Contender, Role, Mood;
        public FaPriority Priority;
        public bool WillSign;
        public long Ask;
        public string Reason = "";
    }

    public sealed class FaDayReport
    {
        public int Day;
        public List<FaSigning> Signings = new List<FaSigning>();
        /// <summary>One line per user offer that was decided on this day.</summary>
        public List<string> UserNotes = new List<string>();
    }

    /// <summary>
    /// Free agency: a multi-day bidding period. Every day the CPU teams put offers on the players they need (by position need and best player
    /// available, within their cap); each player then picks the offer that scores best on money, contender status, role and mood, and signs if
    /// it clears a threshold that drops day by day. The user places offers through <see cref="PlaceOffer"/>.
    /// </summary>
    public static class FreeAgency
    {
        public const int MaxLog = 700;

        // ---------------------------------------------------------------- asks
        public static bool IsUndrafted(CareerSave s, Player p)
        {
            return p.Experience == 0 && p.DraftRound == 0 && p.DraftYear >= s.Year;
        }

        public static double DayFactor(int day) { return Math.Max(0.80, 1.0 - 0.012 * day); }

        /// <summary>The market ask of a free agent on the given day (undrafted youngsters ask the minimum).</summary>
        public static ContractAsk Ask(CareerSave s, Player p, int day = -1)
        {
            if (day < 0) day = s.Phase == CareerPhase.FreeAgency ? s.FaDay : s.Settings.FreeAgencyDays;
            int startYear = Negotiator.StartYearNow(s);
            var ask = new ContractAsk { Years = Negotiator.YearsFor(p.Bio.Age) };
            if (IsUndrafted(s, p)) { ask.Aav = CapMath.MinSalary(s, startYear); ask.Years = 3; ask.BonusPct = 0f; ask.Notes.Add("Undrafted: minimum deal."); ask.Interest = 70; return ask; }
            double f = 0.97 + 0.10 * Negotiator.Jitter(p.Id, startYear + 7);
            long market = CapMath.Market(s, p, startYear);
            ask.Aav = Math.Max(CapMath.MinSalary(s, startYear), CapMath.Round(market * f * DayFactor(day)));
            ask.BonusPct = CapMath.DefaultBonusPct(s, ask.Aav, startYear);
            ask.Interest = 50;
            return ask;
        }

        /// <summary>Cap-scaled price used by CPU teams that grab bargains after the bidding is over.</summary>
        public static long StreetPrice(CareerSave s, Player p, int startYear)
        {
            return Math.Max(CapMath.MinSalary(s, startYear), CapMath.Round(CapMath.Market(s, p, startYear) * 0.8));
        }

        // ---------------------------------------------------------------- the player's decision
        public static FaPriority PriorityOf(Player p)
        {
            return (FaPriority)(int)(Negotiator.Jitter(p.Id, 77) * 4);
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        public static float Threshold(int day) { return Math.Max(0.30f, 0.55f - 0.025f * day); }

        /// <summary>Contender appeal 0-1: how strong the team is and how well it did last year.</summary>
        public static float ContenderScore(CareerSave s, Team t)
        {
            float strength = Clamp01((t.TeamOverall() - 62f) / 26f);
            return 0.5f * strength + 0.5f * MoraleEngine.WinPct(s, t.Id);
        }

        /// <summary>Role appeal 0-1: would he start for this team?</summary>
        public static float RoleScore(Team t, Player p)
        {
            Position pos = p.Position;
            int ovr = p.Ratings.Overall(pos);
            int starters = LeagueRules.Starters(pos);
            int better = 0;
            for (int i = 0; i < t.Roster.Count; i++)
                if (t.Roster[i].Position == pos && t.Roster[i].Ratings.Overall(pos) > ovr) better++;
            if (better < starters) return 1f;
            if (better == starters) return 0.6f;
            return better == starters + 1 ? 0.3f : 0.12f;
        }

        public static OfferEvaluation Evaluate(CareerSave s, Player p, int teamId, long aav, int years, float bonusPct, int day = -1)
        {
            if (day < 0) day = s.FaDay;
            Team t = s.Pro.GetTeam(teamId);
            ContractAsk ask = Ask(s, p, day);
            var ev = new OfferEvaluation { Ask = ask.Aav, Priority = PriorityOf(p), Threshold = Threshold(day) };
            double ratio = (double)aav / Math.Max(1L, ask.Aav);
            ratio *= 1.0 + (bonusPct - ask.BonusPct) * 0.2;
            if (years < ask.Years - 1 && p.Bio.Age <= 28) ratio *= 0.97;   // young players want security
            ev.Money = Clamp01((float)((ratio - 0.80) / 0.40));
            ev.Contender = ContenderScore(s, t);
            ev.Role = RoleScore(t, p);
            int prior = RosterManager.PriorTeamOf(s, p.Id);
            ev.Mood = 0.5f * (prior == teamId ? 1f : 0.4f) + 0.5f * (MoraleEngine.TeamMorale(s, teamId) / 100f);
            float w0 = 0.40f, w1 = 0.20f, w2 = 0.20f, w3 = 0.20f;
            switch (ev.Priority)
            {
                case FaPriority.Money: w0 = 0.60f; w1 = w2 = w3 = 0.1333f; break;
                case FaPriority.Contender: w1 = 0.45f; w0 = 0.30f; w2 = 0.125f; w3 = 0.125f; break;
                case FaPriority.Role: w2 = 0.45f; w0 = 0.30f; w1 = 0.125f; w3 = 0.125f; break;
                case FaPriority.Loyalty: w3 = 0.45f; w0 = 0.30f; w1 = 0.125f; w2 = 0.125f; break;
            }
            ev.Score = w0 * ev.Money + w1 * ev.Contender + w2 * ev.Role + w3 * ev.Mood;
            ev.WillSign = ev.Score >= ev.Threshold;
            if (!ev.WillSign)
            {
                if (ev.Money < 0.4f) ev.Reason = "Wants more money.";
                else if (ev.Priority == FaPriority.Contender && ev.Contender < 0.5f) ev.Reason = "Wants to play for a contender.";
                else if (ev.Priority == FaPriority.Role && ev.Role < 0.6f) ev.Reason = "Wants a starting role.";
                else if (ev.Priority == FaPriority.Loyalty) ev.Reason = "Prefers a team he trusts.";
                else ev.Reason = "Not convinced yet.";
            }
            else ev.Reason = "Interested.";
            return ev;
        }

        // ---------------------------------------------------------------- user offers
        public static FaOffer OfferOf(CareerSave s, int teamId, int playerId)
        {
            for (int i = 0; i < s.FaOffers.Count; i++) if (s.FaOffers[i].TeamId == teamId && s.FaOffers[i].PlayerId == playerId) return s.FaOffers[i];
            return null;
        }

        /// <summary>The user bids on a free agent (or raises a standing bid). The bid is decided when the day is advanced.</summary>
        public static OpResult PlaceOffer(CareerSave s, int playerId, long aav, int years, float bonusPct)
        {
            if (s.Phase != CareerPhase.FreeAgency) return OpResult.Fail("Free agency is not open.");
            Player p = FindInPool(s, playerId);
            if (p == null) return OpResult.Fail("That player is no longer a free agent.");
            if (years < 1 || years > 6) return OpResult.Fail("Contracts run 1 to 6 years.");
            Team t = s.UserTeam;
            Contract c = CapMath.Veteran(s, aav, years, bonusPct, s.Year + 1);
            if (t.CapUsed + c.CapHit > t.SalaryCap) return OpResult.Fail("Not enough cap space (" + CareerText.Money(c.CapHit) + " first-year hit, " + CareerText.Money(t.SalaryCap - t.CapUsed) + " available).");
            if (t.Roster.Count >= RosterRules.OffseasonLimit) return OpResult.Fail("Your roster is full.");
            FaOffer o = OfferOf(s, t.Id, playerId);
            if (o == null) { o = new FaOffer { Id = s.NextId(), PlayerId = playerId, TeamId = t.Id }; s.FaOffers.Add(o); }
            o.Aav = aav; o.Years = years; o.BonusPct = bonusPct; o.DayPlaced = s.FaDay;
            OfferEvaluation ev = Evaluate(s, p, t.Id, aav, years, bonusPct);
            return OpResult.Success(ev.WillSign ? "Offer placed: he looks interested." : "Offer placed: " + ev.Reason);
        }

        public static OpResult WithdrawOffer(CareerSave s, int playerId)
        {
            FaOffer o = OfferOf(s, s.UserTeamId, playerId);
            if (o == null) return OpResult.Fail("No offer to withdraw.");
            s.FaOffers.Remove(o);
            return OpResult.Success("Offer withdrawn.");
        }

        public static Player FindInPool(CareerSave s, int playerId)
        {
            var pool = s.Pro.FreeAgents;
            for (int i = 0; i < pool.Count; i++) if (pool[i].Id == playerId) return pool[i];
            return null;
        }

        /// <summary>The free agent list for the screen: best first, optionally one position bucket.</summary>
        public static List<Player> Pool(CareerSave s, bool filterBucket = false, PositionBucket bucket = PositionBucket.QB, int minOvr = 0)
        {
            var l = new List<Player>();
            var pool = s.Pro.FreeAgents;
            for (int i = 0; i < pool.Count; i++)
            {
                Player p = pool[i];
                if (filterBucket && PositionBuckets.Of(p.Position) != bucket) continue;
                if (p.Overall < minOvr) continue;
                l.Add(p);
            }
            l.Sort((a, b) => Offseason.Value(b).CompareTo(Offseason.Value(a)));
            return l;
        }

        // ---------------------------------------------------------------- phase control
        /// <summary>Called when free agency opens: stale offers are cleared and the day counter starts.</summary>
        public static void Open(CareerSave s)
        {
            s.FaOffers.Clear();
            s.FaDay = 0;
            s.FaOpen = true;
            if (s.FaLog.Count > MaxLog) s.FaLog.RemoveRange(0, s.FaLog.Count - MaxLog);
        }

        public static bool IsDone(CareerSave s) { return s.FaDay >= s.Settings.FreeAgencyDays; }

        public static void FinishAll(CareerSave s, Rng rng)
        {
            int guard = 0;
            while (!IsDone(s) && guard++ < 60) AdvanceDay(s, rng);
        }

        // ---------------------------------------------------------------- the day
        public static FaDayReport AdvanceDay(CareerSave s, Rng rng)
        {
            var report = new FaDayReport { Day = s.FaDay + 1 };
            int day = s.FaDay;   // day factor and threshold for the day being resolved
            long min = CapMath.MinSalary(s, s.Year + 1);

            // 1. CPU offers
            var cpuOffers = new List<FaOffer>();
            var teams = new List<Team>(s.Pro.Teams);
            rng.Shuffle(teams);
            int perTeam = day == 0 ? 6 : 3;
            for (int ti = 0; ti < teams.Count; ti++)
            {
                Team t = teams[ti];
                if (t.Id == s.UserTeamId) continue;
                MakeCpuOffers(s, t, day, perTeam, rng, cpuOffers);
            }

            // 2. group offers by player
            var byPlayer = new Dictionary<int, List<FaOffer>>();
            var order = new List<int>();
            Action<FaOffer> add = o =>
            {
                List<FaOffer> l;
                if (!byPlayer.TryGetValue(o.PlayerId, out l)) { l = new List<FaOffer>(); byPlayer[o.PlayerId] = l; order.Add(o.PlayerId); }
                l.Add(o);
            };
            for (int i = 0; i < s.FaOffers.Count; i++) add(s.FaOffers[i]);
            for (int i = 0; i < cpuOffers.Count; i++) add(cpuOffers[i]);

            // best players decide first
            var cand = new List<Player>();
            for (int i = 0; i < order.Count; i++) { Player p = FindInPool(s, order[i]); if (p != null) cand.Add(p); }
            cand.Sort((a, b) => { int c = Offseason.Value(b).CompareTo(Offseason.Value(a)); return c != 0 ? c : a.Id.CompareTo(b.Id); });

            bool lastDay = day + 1 >= s.Settings.FreeAgencyDays;
            for (int ci = 0; ci < cand.Count; ci++)
            {
                Player p = cand[ci];
                var offers = byPlayer[p.Id];
                FaOffer best = null; OfferEvaluation bestEv = null;
                for (int k = 0; k < offers.Count; k++)
                {
                    FaOffer o = offers[k];
                    Team t = s.Pro.GetTeam(o.TeamId);
                    Contract c = CapMath.Veteran(s, o.Aav, o.Years, o.BonusPct, s.Year + 1);
                    if (IsUndrafted(s, p)) c = CapMath.Rookie(s, 400, s.Year + 1);
                    if (t.CapUsed + c.CapHit > t.SalaryCap) continue;
                    if (t.Roster.Count >= RosterRules.OffseasonLimit) continue;
                    OfferEvaluation ev = Evaluate(s, p, o.TeamId, o.Aav, o.Years, o.BonusPct, day);
                    ev.Score += (float)(rng.NextFloat() * 0.02);
                    if (bestEv == null || ev.Score > bestEv.Score) { best = o; bestEv = ev; }
                }
                bool sign = best != null && (bestEv.WillSign || (lastDay && bestEv.Score >= bestEv.Threshold - 0.12f));
                for (int k = 0; k < offers.Count; k++)
                {
                    FaOffer o = offers[k];
                    if (o.TeamId != s.UserTeamId) continue;
                    if (sign && o == best) report.UserNotes.Add(p.Name + " signed with you (" + CareerText.Money(o.Aav) + " x " + o.Years + ").");
                    else if (sign) report.UserNotes.Add(p.Name + " chose the " + CareerText.TeamNick(s, best.TeamId) + " over your offer.");
                    else
                    {
                        Team ut = s.UserTeam;
                        Contract uc = CapMath.Veteran(s, o.Aav, o.Years, o.BonusPct, s.Year + 1);
                        if (ut.CapUsed + uc.CapHit > ut.SalaryCap) { report.UserNotes.Add("Your offer to " + p.Name + " was voided: no cap space."); s.FaOffers.Remove(o); }
                        else report.UserNotes.Add(p.Name + " is still deciding on your offer: " + Evaluate(s, p, o.TeamId, o.Aav, o.Years, o.BonusPct, day).Reason);
                    }
                }
                if (!sign) continue;
                Team winner = s.Pro.GetTeam(best.TeamId);
                FaSigning sg = SignPlayer(s, winner, p, best, day + 1, rng);
                report.Signings.Add(sg);
                // all bids for him are done
                for (int k = s.FaOffers.Count - 1; k >= 0; k--) if (s.FaOffers[k].PlayerId == p.Id) s.FaOffers.RemoveAt(k);
            }

            s.FaDay++;
            if (s.FaLog.Count > MaxLog) s.FaLog.RemoveRange(0, s.FaLog.Count - MaxLog);
            return report;
        }

        private static FaSigning SignPlayer(CareerSave s, Team t, Player p, FaOffer o, int day, Rng rng)
        {
            s.Pro.FreeAgents.Remove(p);
            int fromTeam = RosterManager.PriorTeamOf(s, p.Id);
            int startYear = s.Year + 1;
            Contract c = IsUndrafted(s, p) ? CapMath.Rookie(s, 400, startYear) : CapMath.Veteran(s, o.Aav, o.Years, o.BonusPct, startYear);
            p.Contract = c;
            p.Morale = Math.Max(p.Morale, 72);
            Offseason.AssignJersey(t, p, rng);
            t.AddPlayer(p);
            var sg = new FaSigning
            {
                Year = s.Year, Day = day, PlayerId = p.Id, PlayerName = p.Name, Pos = p.Position, Ovr = p.Overall, Age = p.Age, TeamId = t.Id, FromTeamId = fromTeam,
                Aav = c.Aav, Years = c.Years
            };
            s.FaLog.Add(sg);
            Ledger.Tx(s, TransactionKind.Signing, t.Id, p, c.Aav, p.Name + " signed with the " + t.Nickname + ": " + c.Years + " years, " + CareerText.Money(c.Aav) + " per year.", fromTeam);
            NewsWriter.Signing(s, t, p, c.Aav, c.Years, 80, false);
            return sg;
        }

        // ---------------------------------------------------------------- CPU shopping
        private sealed class Target
        {
            public Player P;
            public float Score;
            public long Aav;
        }

        private static void MakeCpuOffers(CareerSave s, Team t, int day, int count, Rng rng, List<FaOffer> output)
        {
            long min = CapMath.MinSalary(s, s.Year + 1);
            int active = t.Roster.Count;
            if (active >= RosterRules.OffseasonLimit - 2) return;

            // best current ovr per position (sorted), used for improvement
            var perPos = new Dictionary<Position, List<int>>();
            for (int i = 0; i < LeagueRules.RosterOrder.Length; i++) perPos[LeagueRules.RosterOrder[i]] = new List<int>();
            for (int i = 0; i < t.Roster.Count; i++) perPos[t.Roster[i].Position].Add(t.Roster[i].Ratings.Overall(t.Roster[i].Position));
            foreach (var kv in perPos) kv.Value.Sort((a, b) => b.CompareTo(a));
            long reserve = Math.Max(0, 53 - active - 1) * min;
            long space = t.SalaryCap - t.CapUsed - reserve;

            var pool = s.Pro.FreeAgents;
            var targets = new List<Target>();
            for (int i = 0; i < pool.Count; i++)
            {
                Player p = pool[i];
                if (p.Injury.IsInjured && p.Injury.WeeksOut > 8) continue;
                int ovr = p.Ratings.Overall(p.Position);
                float value = Offseason.Value(p);
                if (value < 50f) continue;
                ContractAsk ask = Ask(s, p, day);
                if (ask.Aav > space) continue;
                List<int> mine = perPos[p.Position];
                int starters = LeagueRules.Starters(p.Position);
                int slots = LeagueRules.ProSlots(p.Position);
                float starterOvr = mine.Count >= starters ? mine[starters - 1] : 0f;
                float improve = ovr - starterOvr;
                float deficit = (slots - mine.Count) * 7f;
                if (mine.Count < starters) deficit += 20f;
                float score = improve * 0.9f + deficit;
                if (improve < 2f && deficit <= 0f) score = (value - 74f) * 0.5f;   // best player available
                if (mine.Count >= slots + 1) score -= 14f;
                score -= ask.Aav / 1000000f * 0.45f;
                score += rng.Gaussian(0f, 1.5f);
                if (score <= 2f) continue;
                targets.Add(new Target { P = p, Score = score, Aav = ask.Aav });
            }
            targets.Sort((a, b) => { int c = b.Score.CompareTo(a.Score); return c != 0 ? c : a.P.Id.CompareTo(b.P.Id); });
            long committed = 0;
            int made = 0;
            for (int i = 0; i < targets.Count && made < count; i++)
            {
                Target tg = targets[i];
                if (committed + tg.Aav > space) continue;
                // bid around the ask; a hungry team pays a bit more
                double over = 1.0 + rng.Range(-0.02f, 0.10f) + (day >= 6 ? 0.0 : 0.02);
                long aav = Math.Max(min, CapMath.Round(tg.Aav * over));
                if (aav > space - committed) aav = space - committed;
                if (aav < min) continue;
                ContractAsk a = Ask(s, tg.P, day);
                int years = a.Years;
                output.Add(new FaOffer { Id = 0, PlayerId = tg.P.Id, TeamId = t.Id, Aav = aav, Years = years, BonusPct = CapMath.DefaultBonusPct(s, aav, s.Year + 1), DayPlaced = day });
                committed += aav;
                made++;
            }
        }

        /// <summary>Puts the offers a CPU team would make on behalf of a team (the auto-pilot and "let the staff handle it" for the user).</summary>
        public static int AutoBid(CareerSave s, Team t, Rng rng)
        {
            var offers = new List<FaOffer>();
            MakeCpuOffers(s, t, s.FaDay, s.FaDay == 0 ? 6 : 3, rng, offers);
            int n = 0;
            for (int i = 0; i < offers.Count; i++)
            {
                if (OfferOf(s, t.Id, offers[i].PlayerId) != null) continue;
                offers[i].Id = s.NextId();
                s.FaOffers.Add(offers[i]);
                n++;
            }
            return n;
        }

        // ---------------------------------------------------------------- outside the bidding window
        /// <summary>Signs a free agent right away (preseason / in-season street signings): he accepts the lowered ask.</summary>
        public static OpResult SignNow(CareerSave s, Team t, int playerId, long aav, int years, bool practiceSquad = false)
        {
            if (s.Phase == CareerPhase.FreeAgency) return OpResult.Fail("Use offers while the bidding is open.");
            Player p = FindInPool(s, playerId);
            if (p == null) return OpResult.Fail("That player is not a free agent.");
            int startYear = Negotiator.StartYearNow(s);
            ContractAsk ask = Ask(s, p);
            Contract c;
            if (practiceSquad)
            {
                if (t.PracticeSquad.Count >= RosterRules.PracticeSquadLimit) return OpResult.Fail("The practice squad is full.");
                if (!RosterManager.EligibleForPracticeSquad(p) && p.Experience > RosterRules.PracticeSquadMaxExperience) return OpResult.Fail("Too much experience for the practice squad.");
                c = CapMath.PracticeSquad(s, startYear);
                if (t.CapUsed + c.CapHit > t.SalaryCap) return OpResult.Fail("Not enough cap space.");
                s.Pro.FreeAgents.Remove(p);
                p.Contract = c;
                t.PracticeSquad.Add(p);
                p.TeamId = t.Id;
                Ledger.Tx(s, TransactionKind.PracticeSquad, t.Id, p, c.CapHit, p.Name + " signed to the practice squad.");
                return OpResult.Success(p.Name + " joined the practice squad.");
            }
            int limit = RosterManager.InSeason(s) ? RosterRules.ActiveLimit : RosterRules.OffseasonLimit;
            if (t.Roster.Count >= limit) return OpResult.Fail("The active roster is full.");
            if (aav < ask.Aav * 0.95) return OpResult.Fail(p.Name + " wants at least " + CareerText.Money((long)(ask.Aav * 0.95)) + " per year.");
            c = IsUndrafted(s, p) ? CapMath.Rookie(s, 400, startYear) : CapMath.Veteran(s, aav, years, CapMath.DefaultBonusPct(s, aav, startYear), startYear);
            if (t.CapUsed + c.CapHit > t.SalaryCap) return OpResult.Fail("Not enough cap space (" + CareerText.Money(c.CapHit) + " needed).");
            s.Pro.FreeAgents.Remove(p);
            p.Contract = c;
            Offseason.AssignJersey(t, p, s.NewRng());
            t.AddPlayer(p);
            Ledger.Tx(s, TransactionKind.Signing, t.Id, p, c.Aav, p.Name + " signed with the " + t.Nickname + ".");
            NewsWriter.Signing(s, t, p, c.Aav, c.Years, 80, false);
            RosterManager.AfterMove(s, t);
            return OpResult.Success(p.Name + " signed.");
        }
    }
}
