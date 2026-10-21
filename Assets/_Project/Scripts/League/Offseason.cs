using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    public sealed class OffseasonReport
    {
        public int Year;
        public int Retired, Released, ReSigned, Drafted, Undrafted, FreeAgentSignings, EmergencySignings, Cuts, Restructures;
        public float StarterOvrBefore, StarterOvrAfter;
        public float RosterOvrAfter;
        public List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Everything between two seasons: aging/progression, retirement, contract expiry and re-signing,
    /// the draft, free agency, roster cut-down and cap compliance. Runs forever on its own.
    /// </summary>
    public static class Offseason
    {
        /// <summary>Generates the upcoming draft class and each team's baseline scouting reports (call any time before <see cref="Run"/>).</summary>
        public static void PrepareDraftClass(ProLeague league, CollegeWorld college, Rng rng)
        {
            List<Prospect> cls = college != null
                ? ProspectGenerator.FromCollege(college, league.Year, rng)
                : ProspectGenerator.Standalone(league, league.Year, rng, null);
            league.DraftClass = cls;
            league.ScoutingReports = DraftEngine.BuildScouting(league, cls, rng, league.Year);
        }

        public static float AverageStarterOverall(ProLeague league)
        {
            double sum = 0; int n = 0;
            for (int t = 0; t < league.Teams.Count; t++)
            {
                Team team = league.Teams[t];
                for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
                {
                    Position pos = LeagueRules.RosterOrder[i];
                    List<int> ids;
                    if (!team.Depth.TryGet(pos, out ids)) continue;
                    for (int s = 0; s < LeagueRules.Starters(pos) && s < ids.Count; s++)
                    {
                        Player p = team.GetPlayer(ids[s]);
                        if (p == null) continue;
                        sum += p.Ratings.Overall(pos); n++;
                    }
                }
            }
            return n == 0 ? 0f : (float)(sum / n);
        }

        public static float AverageRosterOverall(ProLeague league)
        {
            double sum = 0; int n = 0;
            for (int t = 0; t < league.Teams.Count; t++)
                for (int i = 0; i < league.Teams[t].Roster.Count; i++) { sum += league.Teams[t].Roster[i].Overall; n++; }
            return n == 0 ? 0f : (float)(sum / n);
        }

        /// <summary>Runs the whole offseason and rolls the league (and college world, if given) to the next season.</summary>
        public static OffseasonReport Run(ProLeague league, CollegeWorld college, Rng rng)
        {
            var rep = new OffseasonReport { Year = league.Year };
            rep.StarterOvrBefore = AverageStarterOverall(league);

            // ---- draft class (before anyone ages)
            if (league.DraftClass == null || league.DraftClass.Count == 0 || league.ScoutingReports == null || league.ScoutingReports.Count != league.Teams.Count)
                PrepareDraftClass(league, college, rng);
            List<Prospect> cls = league.DraftClass;

            // team prestige drifts with results (it feeds progression, free agency and recruiting)
            if (league.Season != null && league.Season.Games.Count > 0 && league.Season.IsComplete)
            {
                var rows = league.Season.Standings();
                for (int i = 0; i < rows.Count; i++)
                {
                    Team tm = league.Teams[rows[i].TeamId];
                    float target = 1f + 4f * rows[i].Pct;
                    tm.Prestige = Math.Max(1, Math.Min(5, (int)Math.Round(0.65f * tm.Prestige + 0.35f * target)));
                }
            }

            // ---- 1. aging, progression, healing, contract clock
            for (int t = 0; t < league.Teams.Count; t++)
            {
                Team team = league.Teams[t];
                Coach hc = team.HeadCoach;
                int dev = (hc != null ? hc.Development : 65) + (team.Prestige - 3) * 7;
                for (int i = 0; i < team.Roster.Count; i++)
                {
                    Player p = team.Roster[i];
                    Progression.Advance(p, rng, dev);
                    p.Injury.WeeksOut = 0; p.Injury.Type = "";
                    p.Contract.AdvanceYear();
                    p.Morale = Math.Max(30, Math.Min(100, p.Morale + rng.Range(-8, 10)));
                }
                team.DeadCap = 0;
            }
            for (int i = 0; i < league.FreeAgents.Count; i++)
            {
                Player p = league.FreeAgents[i];
                Progression.Advance(p, rng, 60);
                p.Injury.WeeksOut = 0; p.Injury.Type = "";
            }

            // ---- 2. retirement
            for (int t = 0; t < league.Teams.Count; t++)
            {
                Team team = league.Teams[t];
                for (int i = team.Roster.Count - 1; i >= 0; i--)
                {
                    Player p = team.Roster[i];
                    if (Retirement.Check(p, rng, false))
                    {
                        Retire(league, p);
                        team.Roster.RemoveAt(i);
                        rep.Retired++;
                    }
                }
                team.InvalidateIndex();
            }
            for (int i = league.FreeAgents.Count - 1; i >= 0; i--)
            {
                Player p = league.FreeAgents[i];
                bool junk = p.Ratings.Overall(p.Position) < 46 || (p.Bio.Age >= 34 && p.Ratings.Overall(p.Position) < 66);
                if (junk || Retirement.Check(p, rng, true))
                {
                    Retire(league, p);
                    league.FreeAgents.RemoveAt(i);
                    rep.Retired++;
                }
            }

            // ---- 3. contract expiry
            for (int t = 0; t < league.Teams.Count; t++)
            {
                Team team = league.Teams[t];
                for (int i = team.Roster.Count - 1; i >= 0; i--)
                {
                    Player p = team.Roster[i];
                    if (p.Contract.YearsLeft > 0) continue;
                    int ovr = p.Ratings.Overall(p.Position);
                    float prob = ovr < 60 ? 0.2f : Math.Min(0.95f, 0.4f + (ovr - 60) * 0.025f);
                    if (IsStarter(team, p)) prob = Math.Min(0.97f, prob + 0.2f);
                    if (p.Bio.Age >= 33) prob *= 0.6f;
                    long ask = ContractModel.MarketAav(p.Position, ovr, p.Bio.Age);
                    bool afford = team.CapSpace - ask > -20000000L; // restructure fixes the rest later
                    if (rng.Chance(prob) && afford)
                    {
                        int years = p.Bio.Age >= 31 ? rng.Range(1, 3) : rng.Range(2, 6);
                        p.Contract = ContractModel.VeteranContract(p.Position, ovr, p.Bio.Age, years, league.Year + 1, rng);
                        rep.ReSigned++;
                    }
                    else
                    {
                        team.Roster.RemoveAt(i);
                        Release(league, p);
                        rep.Released++;
                    }
                }
                team.InvalidateIndex();
            }

            // ---- 4. the draft
            var reports = league.ScoutingReports;
            List<DraftPickRecord> picks = DraftEngine.Run(league, cls, reports, rng, league.Year);
            league.LastDraft = picks;
            rep.Drafted = picks.Count;

            // ---- 5. undrafted rookies hit the market
            var removedFromCollege = new HashSet<int>();
            for (int i = 0; i < cls.Count; i++)
            {
                Prospect pr = cls[i];
                removedFromCollege.Add(pr.Id);
                if (pr.Drafted) continue;
                Player p = pr.Player;
                int grade = (int)pr.TrueGrade;
                if (grade < 52) continue;
                p.DraftYear = league.Year; p.DraftRound = 0; p.DraftPick = 0; p.Experience = 0;
                p.TeamId = -1; p.Contract = new Contract();
                p.Retired = false; p.IsRedshirting = false;
                league.FreeAgents.Add(p);
                rep.Undrafted++;
            }

            // ---- 6. free agency & roster building
            SignFreeAgents(league, rng, rep);
            for (int t = 0; t < league.Teams.Count; t++)
            {
                Team team = league.Teams[t];
                CutDown(league, team, rep);
                FillDeficits(league, team, rng, rep);
                EnforceCap(team, rep);
                DepthChartBuilder.Build(team);
            }
            // anyone who got squeezed out of the pool by the cut-down and is useless leaves
            if (league.FreeAgents.Count > 400)
            {
                league.FreeAgents.Sort((a, b) => b.Ratings.Overall(b.Position).CompareTo(a.Ratings.Overall(a.Position)));
                for (int i = league.FreeAgents.Count - 1; i >= 300; i--) { Retire(league, league.FreeAgents[i]); league.FreeAgents.RemoveAt(i); }
            }

            // ---- 7. coaching staff
            for (int t = 0; t < league.Teams.Count; t++) EvolveStaff(league.Teams[t], rng);

            // ---- 8. roll the year
            league.DraftClass = new List<Prospect>();
            league.ScoutingReports = new List<ScoutingReport>();
            league.Year++;
            if (college != null)
            {
                if (college.Season != null && college.Season.Games.Count > 0) CollegeFactory.UpdatePrestige(college, college.Season);
                CollegeFactory.AdvanceYear(college, rng, removedFromCollege);
            }
            league.Season = Season.CreatePro(league, league.Year, rng.Fork());
            league.Bind();
            league.SaveRng(rng);

            rep.StarterOvrAfter = AverageStarterOverall(league);
            rep.RosterOvrAfter = AverageRosterOverall(league);
            return rep;
        }

        private static void Retire(ProLeague league, Player p)
        {
            p.Retired = true;
            p.TeamId = -1;
            league.RetiredPlayers.Add(p);
            if (league.RetiredPlayers.Count > 1500) league.RetiredPlayers.RemoveRange(0, league.RetiredPlayers.Count - 1500);
        }

        private static void Release(ProLeague league, Player p)
        {
            p.TeamId = -1;
            p.Contract = new Contract();
            league.FreeAgents.Add(p);
        }

        private static bool IsStarter(Team team, Player p)
        {
            List<int> ids;
            if (!team.Depth.TryGet(p.Position, out ids)) return false;
            int n = LeagueRules.Starters(p.Position);
            for (int i = 0; i < n && i < ids.Count; i++) if (ids[i] == p.Id) return true;
            return false;
        }

        // -------------------------------------------------------------- free agency
        private static float Value(Player p)
        {
            return p.Ratings.Overall(p.Position) + (p.Potential - p.Ratings.Overall(p.Position)) * 0.3f
                   - (p.Bio.Age >= 32 ? (p.Bio.Age - 31) * 1.5f : 0f);
        }

        private static int Deficit(Team t, Position pos)
        {
            return LeagueRules.ProSlots(pos) - t.CountAt(pos);
        }

        private static void SignFreeAgents(ProLeague league, Rng rng, OffseasonReport rep)
        {
            var pool = league.FreeAgents;
            for (int round = 0; round < 8; round++)
            {
                var order = new List<Team>(league.Teams);
                var noise = new Dictionary<int, float>();
                foreach (Team tt in order) noise[tt.Id] = tt.Prestige * 2.5f + tt.TeamOverall() * 0.5f + rng.Gaussian(0f, 4f);
                order.Sort((a, b) => noise[b.Id].CompareTo(noise[a.Id]));
                bool any = false;
                for (int ti = 0; ti < order.Count; ti++)
                {
                    Team team = order[ti];
                    if (team.Roster.Count >= LeagueRules.RosterSize + 2 && round > 0) continue;
                    // evaluate needs: deficits first, then weak starters
                    Position needPos = Position.QB;
                    float needScore = 0f;
                    for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
                    {
                        Position pos = LeagueRules.RosterOrder[i];
                        float score = Deficit(team, pos) * 10f;
                        float starterOvr = BestOvr(team, pos);
                        if (starterOvr < 72f) score += (72f - starterOvr) * 0.4f;
                        if (score > needScore) { needScore = score; needPos = pos; }
                    }
                    if (needScore <= 1f) continue;
                    bool deficit = Deficit(team, needPos) > 0;

                    Player best = null; float bestV = -1f;
                    for (int i = 0; i < pool.Count; i++)
                    {
                        Player p = pool[i];
                        if (PositionBuckets.Of(p.Position) != PositionBuckets.Of(needPos)) continue;
                        int ovrAtNeed = p.Ratings.Overall(needPos);
                        if (!deficit && ovrAtNeed < BestOvr(team, needPos) + 3f) continue;
                        float v = Value(p);
                        long price = Price(p);
                        long reserve = Math.Max(0, Deficit(team, needPos) - 1) * Contract.MinSalary;
                        if (price > team.CapSpace - reserve) { price = Math.Max(Contract.MinSalary, team.CapSpace - reserve); if (price > team.CapSpace || price < Contract.MinSalary || Value(p) > 64f) continue; }
                        if (v > bestV) { bestV = v; best = p; }
                    }
                    if (best == null) continue;
                    pool.Remove(best);
                    Sign(league, team, best, rng);
                    rep.FreeAgentSignings++;
                    any = true;
                }
                if (!any && round > 2) break;
            }
        }

        private static long Price(Player p)
        {
            if (p.DraftRound == 0 && p.Experience == 0) return Contract.MinSalary;
            return ContractModel.MarketAav(p.Position, p.Ratings.Overall(p.Position), p.Bio.Age);
        }

        private static float BestOvr(Team t, Position pos)
        {
            float best = 0f;
            int starters = LeagueRules.Starters(pos);
            // return the weakest starter's overall at that slot for multi-starter positions
            var list = new List<int>();
            for (int i = 0; i < t.Roster.Count; i++)
                if (PositionBuckets.Of(t.Roster[i].Position) == PositionBuckets.Of(pos) && t.Roster[i].Position == pos)
                    list.Add(t.Roster[i].Ratings.Overall(pos));
            if (list.Count == 0) return 0f;
            list.Sort((a, b) => b.CompareTo(a));
            best = list[Math.Min(starters, list.Count) - 1];
            return best;
        }

        private static void Sign(ProLeague league, Team team, Player p, Rng rng)
        {
            int ovr = p.Ratings.Overall(p.Position);
            if (p.Experience == 0 && p.DraftRound == 0 && p.DraftYear == league.Year)
            {
                p.Contract = ContractModel.RookieContract(400, league.Year + 1);
            }
            else
            {
                long market = ContractModel.MarketAav(p.Position, ovr, p.Bio.Age);
                int years = p.Bio.Age >= 31 ? rng.Range(1, 3) : rng.Range(1, 5);
                var c = ContractModel.VeteranContract(p.Position, ovr, p.Bio.Age, years, league.Year + 1, rng);
                long room = team.CapSpace;
                if (c.Aav > room && room >= Contract.MinSalary)
                    c = Contract.Create(years, room, 0.05f, 0.1f, league.Year + 1);
                p.Contract = c;
            }
            var jerseys = new HashSet<int>();
            for (int i = 0; i < team.Roster.Count; i++) jerseys.Add(team.Roster[i].Bio.JerseyNumber);
            if (jerseys.Contains(p.Bio.JerseyNumber)) p.Bio.JerseyNumber = LeagueFactory.PickJersey(p.Position, rng, jerseys);
            team.AddPlayer(p);
        }

        // -------------------------------------------------------------- roster management
        private static float KeepValue(Player p)
        {
            int ovr = p.Ratings.Overall(p.Position);
            return ovr + (p.Potential - ovr) * 0.35f + (p.Bio.Age <= 24 ? 2f : 0f) - (p.Bio.Age >= 33 ? 3f : 0f);
        }

        private static void CutDown(ProLeague league, Team team, OffseasonReport rep)
        {
            DepthChartBuilder.Build(team);
            int guard = 0;
            while (team.Roster.Count > LeagueRules.RosterSize && guard++ < 60)
            {
                Player worst = null; float worstV = float.MaxValue;
                for (int i = 0; i < team.Roster.Count; i++)
                {
                    Player p = team.Roster[i];
                    if (team.CountAt(p.Position) <= LeagueRules.ProSlots(p.Position)) continue; // at or under its target
                    if (IsStarter(team, p)) continue;
                    float v = KeepValue(p);
                    if (v < worstV) { worstV = v; worst = p; }
                }
                if (worst == null)
                {
                    for (int i = 0; i < team.Roster.Count; i++)
                    {
                        Player p = team.Roster[i];
                        if (IsStarter(team, p)) continue;
                        float v = KeepValue(p);
                        if (v < worstV) { worstV = v; worst = p; }
                    }
                }
                if (worst == null) break;
                team.RemovePlayer(worst);
                Release(league, worst);
                rep.Cuts++;
            }
        }

        private static void FillDeficits(ProLeague league, Team team, Rng rng, OffseasonReport rep)
        {
            for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
            {
                Position pos = LeagueRules.RosterOrder[i];
                while (Deficit(team, pos) > 0 && team.Roster.Count < LeagueRules.RosterSize + 20)
                {
                    // best available in the pool at this position, otherwise a fresh street free agent
                    Player best = null; float bestV = -1f;
                    for (int k = 0; k < league.FreeAgents.Count; k++)
                    {
                        Player p = league.FreeAgents[k];
                        if (p.Position != pos) continue;
                        float v = Value(p);
                        if (v > bestV) { bestV = v; best = p; }
                    }
                    if (best != null)
                    {
                        league.FreeAgents.Remove(best);
                        best.Contract = ContractModel.VeteranContract(best.Position, best.Ratings.Overall(pos), best.Bio.Age, 1, league.Year + 1, rng);
                        if (best.Contract.Aav > Contract.MinSalary * 3) best.Contract = Contract.Create(1, Contract.MinSalary * 2, 0f, 0f, league.Year + 1);
                        team.AddPlayer(best);
                    }
                    else
                    {
                        var used = new HashSet<string>();
                        int peak = (int)Math.Round(rng.Gaussian(60f, 5f));
                        Player p = PlayerFactory.Create(rng, league, used, pos, peak, rng.Range(23, 30));
                        LeagueFactory.FinishProPlayer(p, rng, league.Year, new HashSet<int>(), null);
                        p.Contract = Contract.Create(1, Contract.MinSalary, 0f, 0f, league.Year + 1);
                        p.Contract.Rookie = false;
                        team.AddPlayer(p);
                        rep.EmergencySignings++;
                    }
                }
            }
            // trim anything beyond the limit created by deficit filling
            if (team.Roster.Count > LeagueRules.RosterSize) CutDown(league, team, rep);
        }

        /// <summary>Gets a team under the cap by restructuring big contracts (and, as a last resort, cutting expensive backups).</summary>
        public static void EnforceCap(Team team, OffseasonReport rep = null)
        {
            int guard = 0;
            while (team.CapUsed > team.SalaryCap && guard++ < 80)
            {
                bool changed = false;
                for (int i = 0; i < team.Roster.Count; i++)
                {
                    Player p = team.Roster[i];
                    if (p.Contract.Aav > 3000000L && p.Contract.CapHit > 0)
                    {
                        p.Contract = ContractModel.Scale(p.Contract, 0.94);
                        changed = true;
                        if (rep != null) rep.Restructures++;
                    }
                }
                if (!changed) break;
            }
        }

        // -------------------------------------------------------------- coaching staff
        private static void EvolveStaff(Team team, Rng rng)
        {
            var used = new HashSet<string>();
            for (int i = 0; i < team.Staff.Count; i++)
            {
                Coach c = team.Staff[i];
                c.Age++;
                if (c.Age >= 68 && rng.Chance(0.5f))
                {
                    team.Staff[i] = LeagueFactory.CreateCoach(rng, used, c.Role, c.Id, 0f);
                    continue;
                }
                c.OffenseRating = Ratings.Clamp(c.OffenseRating + rng.Range(-2, 3));
                c.DefenseRating = Ratings.Clamp(c.DefenseRating + rng.Range(-2, 3));
                c.Development = Ratings.Clamp(c.Development + rng.Range(-2, 3));
                c.Scouting = Ratings.Clamp(c.Scouting + rng.Range(-2, 3));
            }
        }
    }
}
