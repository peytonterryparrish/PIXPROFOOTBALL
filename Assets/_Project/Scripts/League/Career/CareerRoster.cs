using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    public static class RosterRules
    {
        public const int ActiveLimit = LeagueRules.RosterSize;       // 53
        public const int PracticeSquadLimit = 16;
        public const int OffseasonLimit = 90;
        /// <summary>Weeks a player must stay on injured reserve before he can be activated.</summary>
        public const int MinIrWeeks = 4;
        /// <summary>Shortest injury that may be put on injured reserve.</summary>
        public const int MinIrInjuryWeeks = 4;
        public const int IrLimit = 14;
        /// <summary>A team needs at least this many active players to take the field.</summary>
        public const int MinimumToPlay = 45;
        /// <summary>Seasons of experience above which a player can no longer be on the practice squad.</summary>
        public const int PracticeSquadMaxExperience = 3;

        /// <summary>Lowest healthy-roster count per bucket before a team is considered thin.</summary>
        public static int Minimum(PositionBucket b)
        {
            switch (b)
            {
                case PositionBucket.QB: return 2;
                case PositionBucket.RB: return 3;
                case PositionBucket.FB: return 0;
                case PositionBucket.WR: return 5;
                case PositionBucket.TE: return 2;
                case PositionBucket.OL: return 8;
                case PositionBucket.DL: return 6;
                case PositionBucket.LB: return 4;
                case PositionBucket.CB: return 5;
                case PositionBucket.S: return 3;
                default: return 1;
            }
        }
    }

    public enum RosterStatus { Active, PracticeSquad, InjuredReserve, FreeAgent, Waivers, Unsigned }

    public sealed class RosterIssue
    {
        public string Text;
        public bool Blocking;
        public RosterIssue(string text, bool blocking) { Text = text; Blocking = blocking; }
        public override string ToString() { return (Blocking ? "[must fix] " : "[warning] ") + Text; }
    }

    /// <summary>Creation of new "regen" players with everything the career layer expects (ratings, contract fields, abilities).</summary>
    public static class CareerFactory
    {
        /// <summary>Every "First Last" name in use anywhere in the league (active rosters, practice squads, injured reserve, the pool, waivers, the draft class, staffs and the coach market) so a new player never repeats one.</summary>
        public static HashSet<string> UsedNames(CareerSave s, bool includeDraftClass = true)
        {
            var used = new HashSet<string>();
            for (int i = 0; i < s.Pro.FreeAgents.Count; i++) used.Add(s.Pro.FreeAgents[i].Name);
            if (includeDraftClass)
                for (int i = 0; i < s.Pro.DraftClass.Count; i++) if (s.Pro.DraftClass[i].Player != null) used.Add(s.Pro.DraftClass[i].Player.Name);
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                Team team = s.Pro.Teams[t];
                for (int i = 0; i < team.Roster.Count; i++) used.Add(team.Roster[i].Name);
                for (int i = 0; i < team.Staff.Count; i++) used.Add(team.Staff[i].Name);
                for (int i = 0; i < team.PracticeSquad.Count; i++) used.Add(team.PracticeSquad[i].Name);
                for (int i = 0; i < team.InjuredReserve.Count; i++) used.Add(team.InjuredReserve[i].Name);
            }
            for (int i = 0; i < s.Waivers.Count; i++) if (s.Waivers[i].Player != null) used.Add(s.Waivers[i].Player.Name);
            for (int i = 0; i < s.CoachPool.Count; i++) used.Add(s.CoachPool[i].Coach.Name);
            return used;
        }

        public static Player NewRegen(CareerSave s, Rng rng, HashSet<string> usedNames, Position pos, int peak, int age, bool practiceSquad = false)
        {
            Player p = PlayerFactory.Create(rng, s.Pro, usedNames, pos, peak, age);
            LeagueFactory.FinishProPlayer(p, rng, s.Year, new HashSet<int>(), null);
            p.TeamId = -1;
            p.Contract = practiceSquad ? CapMath.PracticeSquad(s, s.Year + 1) : CapMath.Minimum(s, 1, s.Year + 1);
            p.Contract.Rookie = false;
            AbilityCatalog.AssignInitial(p, rng);
            return p;
        }
    }

    /// <summary>
    /// Roster management for any team (the user's through the UI, CPU teams through <see cref="MaintainCpuTeam"/> / <see cref="FinalCuts"/>):
    /// active roster of 53, practice squad of 16, injured reserve, releases with dead cap, waivers.
    /// </summary>
    public static class RosterManager
    {
        // ---------------------------------------------------------------- queries
        public static RosterStatus StatusOf(CareerSave s, Team t, Player p)
        {
            if (t.Roster.Contains(p)) return RosterStatus.Active;
            if (t.PracticeSquad.Contains(p)) return RosterStatus.PracticeSquad;
            if (t.InjuredReserve.Contains(p)) return RosterStatus.InjuredReserve;
            return RosterStatus.Unsigned;
        }

        public static int ActiveLimitNow(CareerSave s)
        {
            switch (s.Phase)
            {
                case CareerPhase.Preseason:
                case CareerPhase.RegularSeason:
                case CareerPhase.Playoffs:
                case CareerPhase.PixBowl:
                    return s.Phase == CareerPhase.Preseason ? RosterRules.OffseasonLimit : RosterRules.ActiveLimit;
                default:
                    return RosterRules.OffseasonLimit;
            }
        }

        public static bool InSeason(CareerSave s)
        {
            return s.Phase == CareerPhase.RegularSeason || s.Phase == CareerPhase.Playoffs || s.Phase == CareerPhase.PixBowl;
        }

        /// <summary>Problems with a team's roster; blocking ones stop the franchise from advancing past the cut-down.</summary>
        public static List<RosterIssue> Validate(CareerSave s, Team t, bool atSeasonStart)
        {
            var issues = new List<RosterIssue>();
            int limit = atSeasonStart || InSeason(s) ? RosterRules.ActiveLimit : RosterRules.OffseasonLimit;
            if (t.Roster.Count > limit) issues.Add(new RosterIssue("Roster has " + t.Roster.Count + " players; the limit is " + limit + ". Cut " + (t.Roster.Count - limit) + ".", true));
            if (atSeasonStart && t.Roster.Count < RosterRules.ActiveLimit) issues.Add(new RosterIssue("Roster has only " + t.Roster.Count + " of " + RosterRules.ActiveLimit + " players" + (t.Roster.Count < RosterRules.MinimumToPlay ? "; at least " + RosterRules.MinimumToPlay + " are needed to play." : "."), t.Roster.Count < RosterRules.MinimumToPlay));
            if (t.PracticeSquad.Count > RosterRules.PracticeSquadLimit) issues.Add(new RosterIssue("Practice squad has " + t.PracticeSquad.Count + " players; the limit is " + RosterRules.PracticeSquadLimit + ".", true));
            if (t.CapUsed > t.SalaryCap) issues.Add(new RosterIssue("Over the salary cap by " + CareerText.Money(t.CapUsed - t.SalaryCap) + ".", true));
            for (int i = 0; i < PositionBuckets.All.Length; i++)
            {
                PositionBucket b = PositionBuckets.All[i];
                int have = 0;
                for (int k = 0; k < t.Roster.Count; k++) if (PositionBuckets.Of(t.Roster[k].Position) == b) have++;
                if (have < RosterRules.Minimum(b)) issues.Add(new RosterIssue("Too thin at " + b + ": " + have + " (need " + RosterRules.Minimum(b) + ").", false));
            }
            // a roster needs a kicker and a punter (or the sim plays a dummy)
            return issues;
        }

        public static bool HasBlockingIssue(List<RosterIssue> issues)
        {
            for (int i = 0; i < issues.Count; i++) if (issues[i].Blocking) return true;
            return false;
        }

        // ---------------------------------------------------------------- low level
        public static bool RemoveFromTeam(Team t, Player p)
        {
            bool ok = t.Roster.Remove(p) || t.PracticeSquad.Remove(p) || t.InjuredReserve.Remove(p);
            t.InvalidateIndex();
            return ok;
        }

        public static void RememberPrior(CareerSave s, Player p, int teamId)
        {
            for (int i = 0; i < s.PriorTeams.Count; i++)
                if (s.PriorTeams[i].PlayerId == p.Id) { s.PriorTeams[i].TeamId = teamId; return; }
            s.PriorTeams.Add(new PriorTeam { PlayerId = p.Id, TeamId = teamId });
            if (s.PriorTeams.Count > 4000) s.PriorTeams.RemoveRange(0, 1000);
        }

        public static int PriorTeamOf(CareerSave s, int playerId)
        {
            for (int i = 0; i < s.PriorTeams.Count; i++) if (s.PriorTeams[i].PlayerId == playerId) return s.PriorTeams[i].TeamId;
            return -1;
        }

        public static void AfterMove(CareerSave s, Team t)
        {
            DepthManager.Refresh(s, t);
        }

        // ---------------------------------------------------------------- release / waivers
        /// <summary>
        /// Cuts a player. Dead money lands on the cap (half now, half next year when more than a year is left). During the regular season he goes
        /// on the waiver wire (another team may claim him and take over the contract); otherwise straight into the free agent pool.
        /// </summary>
        public static OpResult Release(CareerSave s, Team t, int playerId)
        {
            Player p = t.FindAnyPlayer(playerId);
            if (p == null) return OpResult.Fail("The player is not on this team.");
            long dn, dx;
            CapManager.DeadMoneyPreview(p.Contract, out dn, out dx);
            RemoveFromTeam(t, p);
            Offseason.AddDeadCap(t, p.Contract);
            RememberPrior(s, p, t.Id);
            p.Injury.IrWeeks = -1;
            p.TeamId = -1;
            string where;
            if (s.Phase == CareerPhase.RegularSeason)
            {
                s.Waivers.Add(new WaiverEntry { Player = p, FromTeamId = t.Id, Year = s.Year, Week = s.Season.Week, DeadNow = dn, DeadNext = dx });
                where = "placed on waivers";
                Ledger.Tx(s, TransactionKind.Waived, t.Id, p, p.Contract.CapHit, p.Name + " was waived by the " + t.Nickname + ".");
            }
            else
            {
                p.Contract = new Contract();
                s.Pro.FreeAgents.Add(p);
                where = "released";
                Ledger.Tx(s, TransactionKind.Release, t.Id, p, dn + dx, p.Name + " was released by the " + t.Nickname + ".");
            }
            AfterMove(s, t);
            return OpResult.Success(p.Name + " " + where + (dn + dx > 0 ? " (dead money " + CareerText.Money(dn + dx) + ")" : "") + ".");
        }

        /// <summary>Team ids in waiver claim order: the worst record first.</summary>
        public static List<int> WaiverOrder(CareerSave s)
        {
            var ids = new List<int>();
            Dictionary<int, float> pct = new Dictionary<int, float>();
            if (s.Season != null && !s.Season.IsEmpty)
            {
                var rows = s.Season.Standings();
                for (int i = 0; i < rows.Count; i++) pct[rows[i].TeamId] = rows[i].Games > 0 ? rows[i].Pct : -1f;
            }
            for (int i = 0; i < s.Pro.Teams.Count; i++) ids.Add(s.Pro.Teams[i].Id);
            ids.Sort((a, b) =>
            {
                float pa = pct.ContainsKey(a) && pct[a] >= 0f ? pct[a] : s.Pro.GetTeam(a).TeamOverall() / 100f;
                float pb = pct.ContainsKey(b) && pct[b] >= 0f ? pct[b] : s.Pro.GetTeam(b).TeamOverall() / 100f;
                int c = pa.CompareTo(pb);
                return c != 0 ? c : a.CompareTo(b);
            });
            return ids;
        }

        public static int WaiverPriority(CareerSave s, int teamId)
        {
            return WaiverOrder(s).IndexOf(teamId) + 1;
        }

        /// <summary>The user puts in a claim on a waived player; it is decided (by priority) when the waivers clear before the next week.</summary>
        public static OpResult ClaimWaiver(CareerSave s, int playerId)
        {
            WaiverEntry w = FindWaiver(s, playerId);
            if (w == null) return OpResult.Fail("That player is not on waivers.");
            Team t = s.UserTeam;
            if (w.FromTeamId == t.Id) return OpResult.Fail("You cannot claim your own player.");
            if (t.Roster.Count >= RosterRules.ActiveLimit) return OpResult.Fail("Your active roster is full; cut someone first.");
            if (t.CapUsed + w.Player.Contract.CapHit > t.SalaryCap) return OpResult.Fail("Not enough cap space (" + CareerText.Money(w.Player.Contract.CapHit) + " needed).");
            if (!w.ClaimTeamIds.Contains(t.Id)) w.ClaimTeamIds.Add(t.Id);
            return OpResult.Success("Claim submitted; waiver priority " + WaiverPriority(s, t.Id) + " of " + s.Pro.Teams.Count + ".");
        }

        public static WaiverEntry FindWaiver(CareerSave s, int playerId)
        {
            for (int i = 0; i < s.Waivers.Count; i++) if (s.Waivers[i].PlayerId == playerId) return s.Waivers[i];
            return null;
        }

        /// <summary>Clears the waiver wire: claims are granted in priority order; the rest become free agents.</summary>
        public static int ProcessWaivers(CareerSave s, Rng rng)
        {
            if (s.Waivers.Count == 0) return 0;
            List<int> order = WaiverOrder(s);
            int claimed = 0;
            var entries = new List<WaiverEntry>(s.Waivers);
            s.Waivers.Clear();
            for (int e = 0; e < entries.Count; e++)
            {
                WaiverEntry w = entries[e];
                Player p = w.Player;
                Team winner = null;
                for (int o = 0; o < order.Count && winner == null; o++)
                {
                    Team t = s.Pro.GetTeam(order[o]);
                    if (t.Id == w.FromTeamId) continue;
                    bool wants;
                    if (t.Id == s.UserTeamId) wants = w.ClaimTeamIds.Contains(t.Id) && t.Roster.Count < RosterRules.ActiveLimit && t.CapUsed + p.Contract.CapHit <= t.SalaryCap;
                    else wants = CpuWantsClaim(s, t, p);
                    if (wants) winner = t;
                }
                if (winner != null)
                {
                    Team from = s.Pro.GetTeam(w.FromTeamId);
                    if (from != null)
                    {
                        from.DeadCap = Math.Max(0L, from.DeadCap - w.DeadNow);
                        from.DeadCapNext = Math.Max(0L, from.DeadCapNext - w.DeadNext);
                    }
                    if (winner.Roster.Count >= RosterRules.ActiveLimit) DropWorstBackup(s, winner, p.Position);
                    p.Injury.IrWeeks = -1;
                    Offseason.AssignJersey(winner, p, rng);
                    winner.AddPlayer(p);
                    AfterMove(s, winner);
                    Ledger.Tx(s, TransactionKind.WaiverClaim, winner.Id, p, p.Contract.CapHit, p.Name + " claimed off waivers by the " + winner.Nickname + ".", w.FromTeamId);
                    if (winner.Id == s.UserTeamId || p.Overall >= 78)
                        NewsWriter.Post(s, NewsKind.Signing, winner.Nickname + " claim " + p.Name + " off waivers", CareerText.PlayerLabel(p) + " (" + p.Overall + " OVR) is a " + winner.Nickname + " after clearing waivers.", 2, winner.Id, w.FromTeamId, p.Id);
                    claimed++;
                }
                else
                {
                    p.Contract = new Contract();
                    p.TeamId = -1;
                    s.Pro.FreeAgents.Add(p);
                }
            }
            return claimed;
        }

        private static bool CpuWantsClaim(CareerSave s, Team t, Player p)
        {
            if (t.CapUsed + p.Contract.CapHit > t.SalaryCap - CapMath.MinSalary(s, s.Year) * 2) return false;
            float starter = Offseason.BestOvr(t, p.Position);
            int ovr = p.Ratings.Overall(p.Position);
            if (t.Roster.Count < RosterRules.ActiveLimit) return ovr >= starter - 2 || Offseason.Deficit(t, p.Position) > 0;
            return ovr >= starter + 6 && p.Contract.Aav <= CapMath.MinSalary(s, s.Year) * 3;
        }

        /// <summary>Releases the least useful non-starter (straight to free agency) to make room; prefers the claimed player's position group.</summary>
        internal static void DropWorstBackup(CareerSave s, Team t, Position preferred)
        {
            Player worst = null; float worstV = float.MaxValue;
            for (int i = 0; i < t.Roster.Count; i++)
            {
                Player q = t.Roster[i];
                if (Offseason.IsStarter(t, q)) continue;
                float v = Offseason.KeepValue(q) + Offseason.DeadMoneyOf(q.Contract) / 1000000f * 1.5f;
                if ((int)preferred >= 0 && PositionBuckets.Of(q.Position) == PositionBuckets.Of(preferred)) v -= 3f;
                if (v < worstV) { worstV = v; worst = q; }
            }
            if (worst == null) return;
            t.RemovePlayer(worst);
            Offseason.Release(s.Pro, worst, t);
            Ledger.Tx(s, TransactionKind.Release, t.Id, worst, 0, worst.Name + " was released by the " + t.Nickname + ".");
        }

        // ---------------------------------------------------------------- practice squad / IR
        public static bool EligibleForPracticeSquad(Player p)
        {
            return p.Experience <= RosterRules.PracticeSquadMaxExperience && p.Contract.Aav <= Contract.MinSalary * 2 + 1;
        }

        public static OpResult ToPracticeSquad(CareerSave s, Team t, int playerId)
        {
            Player p = t.GetPlayer(playerId);
            if (p == null) return OpResult.Fail("The player is not on the active roster.");
            if (t.PracticeSquad.Count >= RosterRules.PracticeSquadLimit) return OpResult.Fail("The practice squad is full (" + RosterRules.PracticeSquadLimit + ").");
            if (!EligibleForPracticeSquad(p)) return OpResult.Fail(p.Experience > RosterRules.PracticeSquadMaxExperience ? "Too much experience for the practice squad." : "His contract is too big for the practice squad.");
            if (p.Injury.IsInjured && p.Injury.WeeksOut >= RosterRules.MinIrInjuryWeeks) return OpResult.Fail("Use injured reserve for a long-term injury.");
            t.RemovePlayer(p);
            p.Contract = CapMath.PracticeSquad(s, s.Year + (InSeason(s) ? 0 : 1));
            t.PracticeSquad.Add(p);
            Ledger.Tx(s, TransactionKind.PracticeSquad, t.Id, p, p.Contract.CapHit, p.Name + " moved to the practice squad.");
            AfterMove(s, t);
            return OpResult.Success(p.Name + " is on the practice squad.");
        }

        public static OpResult Promote(CareerSave s, Team t, int playerId)
        {
            Player p = null;
            for (int i = 0; i < t.PracticeSquad.Count; i++) if (t.PracticeSquad[i].Id == playerId) p = t.PracticeSquad[i];
            if (p == null) return OpResult.Fail("The player is not on the practice squad.");
            int limit = InSeason(s) ? RosterRules.ActiveLimit : RosterRules.OffseasonLimit;
            if (t.Roster.Count >= limit) return OpResult.Fail("The active roster is full.");
            long hit = p.Contract.Aav < Contract.MinSalary ? CapMath.MinSalary(s, s.Year) : p.Contract.CapHit;
            if (t.CapUsed - p.Contract.CapHit + hit > t.SalaryCap) return OpResult.Fail("Not enough cap space.");
            t.PracticeSquad.Remove(p);
            if (p.Contract.Aav < Contract.MinSalary) p.Contract = CapMath.Minimum(s, 1, s.Year + (InSeason(s) ? 0 : 1));
            Offseason.AssignJersey(t, p, s.NewRng());
            t.AddPlayer(p);
            Ledger.Tx(s, TransactionKind.Promoted, t.Id, p, p.Contract.CapHit, p.Name + " promoted to the active roster.");
            AfterMove(s, t);
            return OpResult.Success(p.Name + " is on the active roster.");
        }

        public static OpResult PlaceOnIR(CareerSave s, Team t, int playerId)
        {
            Player p = t.GetPlayer(playerId);
            if (p == null) return OpResult.Fail("The player is not on the active roster.");
            if (!p.Injury.IsInjured || p.Injury.WeeksOut < RosterRules.MinIrInjuryWeeks) return OpResult.Fail("Only injuries of " + RosterRules.MinIrInjuryWeeks + "+ weeks qualify for injured reserve.");
            if (t.InjuredReserve.Count >= RosterRules.IrLimit) return OpResult.Fail("Injured reserve is full.");
            t.RemovePlayer(p);
            p.Injury.IrWeeks = 0;
            t.InjuredReserve.Add(p);
            Ledger.Tx(s, TransactionKind.InjuredReserve, t.Id, p, 0, p.Name + " placed on injured reserve (" + p.Injury.Type + ", " + p.Injury.WeeksOut + " weeks).");
            AfterMove(s, t);
            return OpResult.Success(p.Name + " is on injured reserve.");
        }

        public static OpResult ActivateFromIR(CareerSave s, Team t, int playerId)
        {
            Player p = null;
            for (int i = 0; i < t.InjuredReserve.Count; i++) if (t.InjuredReserve[i].Id == playerId) p = t.InjuredReserve[i];
            if (p == null) return OpResult.Fail("The player is not on injured reserve.");
            if (p.Injury.IsInjured) return OpResult.Fail("He is still injured (" + p.Injury.WeeksOut + " more weeks).");
            if (p.Injury.IrWeeks < RosterRules.MinIrWeeks && InSeason(s)) return OpResult.Fail("He has to spend " + RosterRules.MinIrWeeks + " weeks on injured reserve (" + p.Injury.IrWeeks + " so far).");
            int limit = InSeason(s) ? RosterRules.ActiveLimit : RosterRules.OffseasonLimit;
            if (t.Roster.Count >= limit) return OpResult.Fail("The active roster is full; cut or move somebody first.");
            t.InjuredReserve.Remove(p);
            p.Injury.IrWeeks = -1;
            Offseason.AssignJersey(t, p, s.NewRng());
            t.AddPlayer(p);
            Ledger.Tx(s, TransactionKind.ActivatedFromIR, t.Id, p, 0, p.Name + " activated from injured reserve.");
            AfterMove(s, t);
            return OpResult.Success(p.Name + " is back on the active roster.");
        }

        // ---------------------------------------------------------------- weekly upkeep
        /// <summary>Heals players on injured reserve and counts their weeks (the season only heals active rosters).</summary>
        public static void HealReserves(CareerSave s)
        {
            for (int i = 0; i < s.Pro.Teams.Count; i++)
            {
                Team t = s.Pro.Teams[i];
                for (int k = 0; k < t.InjuredReserve.Count; k++)
                {
                    Player p = t.InjuredReserve[k];
                    p.Injury.Heal(1);
                    p.Injury.IrWeeks = Math.Max(0, p.Injury.IrWeeks) + 1;
                }
            }
        }

        /// <summary>CPU roster upkeep after a week: long injuries to IR, healed players back, holes filled from the practice squad / free agency.</summary>
        public static void MaintainCpuTeam(CareerSave s, Team t, Rng rng, HashSet<string> used)
        {
            bool user = t.Id == s.UserTeamId;
            // 1. injured reserve
            for (int i = t.Roster.Count - 1; i >= 0; i--)
            {
                Player p = t.Roster[i];
                if (p.Injury.IsInjured && p.Injury.WeeksOut >= 6 && t.InjuredReserve.Count < RosterRules.IrLimit)
                {
                    t.Roster.RemoveAt(i); t.InvalidateIndex();
                    p.Injury.IrWeeks = 0;
                    t.InjuredReserve.Add(p);
                    Ledger.Tx(s, TransactionKind.InjuredReserve, t.Id, p, 0, p.Name + " placed on injured reserve (" + p.Injury.Type + ").");
                }
            }
            // 2. healed players come back when there is room (or when they beat the worst backup at the position)
            for (int i = t.InjuredReserve.Count - 1; i >= 0; i--)
            {
                Player p = t.InjuredReserve[i];
                if (p.Injury.IsInjured || p.Injury.IrWeeks < RosterRules.MinIrWeeks) continue;
                if (t.Roster.Count >= RosterRules.ActiveLimit)
                {
                    if (p.Overall < Offseason.BestOvr(t, p.Position) - 4f) continue;
                    DropWorstBackup(s, t, p.Position);
                }
                t.InjuredReserve.RemoveAt(i);
                p.Injury.IrWeeks = -1;
                Offseason.AssignJersey(t, p, rng);
                t.AddPlayer(p);
                Ledger.Tx(s, TransactionKind.ActivatedFromIR, t.Id, p, 0, p.Name + " activated from injured reserve.");
            }
            // 3. fill up to 53
            int guard = 0;
            while (t.Roster.Count < RosterRules.ActiveLimit && guard++ < 12)
            {
                if (!FillOne(s, t, rng, used, true)) break;
            }
            DepthManager.Refresh(s, t);
        }

        // ---------------------------------------------------------------- filling
        /// <summary>The position group the roster needs most (deficit against the target slots, then thin starters), Position.QB when none.</summary>
        internal static bool NeediestPosition(Team t, bool healthyOnly, out Position pos)
        {
            float best = 0.5f; pos = Position.QB; bool found = false;
            for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
            {
                Position p = LeagueRules.RosterOrder[i];
                int have = 0; int bestOvr = 0;
                for (int k = 0; k < t.Roster.Count; k++)
                {
                    Player q = t.Roster[k];
                    if (q.Position != p) continue;
                    if (healthyOnly && q.Injury.IsInjured) continue;
                    have++;
                    int o = q.Ratings.Overall(p);
                    if (o > bestOvr) bestOvr = o;
                }
                float score = (LeagueRules.ProSlots(p) - have) * 10f;
                if (have < LeagueRules.Starters(p)) score += 25f;
                if (score > best) { best = score; pos = p; found = true; }
            }
            return found;
        }

        /// <summary>
        /// Adds one player to the active roster: the best practice squad player at the neediest position, otherwise the best affordable free agent,
        /// otherwise a fresh street player. Returns false when nothing could be added.
        /// </summary>
        public static bool FillOne(CareerSave s, Team t, Rng rng, HashSet<string> used, bool inSeason)
        {
            Position need;
            if (!NeediestPosition(t, inSeason, out need))
            {
                // balanced roster: take the best value left on the practice squad / pool
                need = LeagueRules.RosterOrder[rng.Range(0, LeagueRules.RosterOrder.Length)];
            }
            int startYear = s.Year + (inSeason ? 0 : 1);
            long min = CapMath.MinSalary(s, startYear);
            // practice squad first
            Player best = null; float bestV = -1f;
            for (int i = 0; i < t.PracticeSquad.Count; i++)
            {
                Player q = t.PracticeSquad[i];
                if (PositionBuckets.Of(q.Position) != PositionBuckets.Of(need) || q.Injury.IsInjured) continue;
                float v = q.Ratings.Overall(need) + (q.Position == need ? 3f : 0f);
                if (v > bestV) { bestV = v; best = q; }
            }
            if (best != null && t.CapUsed - best.Contract.CapHit + Math.Max(best.Contract.CapHit, min) <= t.SalaryCap)
            {
                Promote(s, t, best.Id);
                return true;
            }
            // free agents asking for little
            best = null; bestV = -1f;
            long room = t.SalaryCap - t.CapUsed;
            if (room < min) return false;
            var pool = s.Pro.FreeAgents;
            for (int i = 0; i < pool.Count; i++)
            {
                Player q = pool[i];
                if (q.Position != need || q.Injury.IsInjured) continue;
                float v = Offseason.Value(q);
                if (v > bestV) { bestV = v; best = q; }
            }
            Player signed;
            if (best != null)
            {
                pool.Remove(best);
                best.Contract = CapMath.Minimum(s, 1, startYear);
                signed = best;
            }
            else
            {
                int peak = (int)Math.Round(rng.Gaussian(60f, 5f));
                signed = CareerFactory.NewRegen(s, rng, used, need, peak, rng.Range(23, 30));
                signed.Contract = CapMath.Minimum(s, 1, startYear);
            }
            if (signed.Contract.CapHit > t.SalaryCap - t.CapUsed) { s.Pro.FreeAgents.Add(signed); return false; }
            Offseason.AssignJersey(t, signed, rng);
            t.AddPlayer(signed);
            Ledger.Tx(s, TransactionKind.Signing, t.Id, signed, signed.Contract.CapHit, signed.Name + " signed by the " + t.Nickname + ".");
            return true;
        }

        // ---------------------------------------------------------------- the cut-down
        /// <summary>
        /// CPU final cuts before week 1: fill position holes, cut to 53 (keeping each position at least at its minimum), build a 16-man practice squad
        /// from the cuts (and street players), fit under the cap, rebuild the depth chart.
        /// </summary>
        public static void FinalCuts(CareerSave s, Team t, Rng rng, HashSet<string> used)
        {
            // fill holes first (a roster missing a kicker still needs one)
            FillDeficits(s, t, rng, used);
            CutDownTo(s, t, RosterRules.ActiveLimit, true);
            int guard = 0;
            while (t.CapUsed > t.SalaryCap && guard++ < 8)
            {
                CapManager.AutoFix(s, t);
                while (t.Roster.Count < RosterRules.ActiveLimit) if (!FillOne(s, t, rng, used, false)) break;
            }
            // top the roster back to 53 if cap cuts left holes, then the squad
            while (t.Roster.Count < RosterRules.ActiveLimit) if (!FillOne(s, t, rng, used, false)) break;
            FillPracticeSquad(s, t, rng, used);
            if (t.CapUsed > t.SalaryCap) CapManager.AutoFix(s, t);
            DepthManager.Refresh(s, t);
        }

        /// <summary>Signs players until every position has its target slots (up to the offseason limit).</summary>
        public static void FillDeficits(CareerSave s, Team t, Rng rng, HashSet<string> used)
        {
            int guard = 0;
            for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
            {
                Position pos = LeagueRules.RosterOrder[i];
                while (Offseason.Deficit(t, pos) > 0 && t.Roster.Count < RosterRules.OffseasonLimit && guard++ < 80)
                {
                    long min = CapMath.MinSalary(s, s.Year + 1);
                    Player best = null; float bestV = -1f;
                    var pool = s.Pro.FreeAgents;
                    for (int k = 0; k < pool.Count; k++)
                    {
                        Player q = pool[k];
                        if (q.Position != pos || q.Injury.IsInjured) continue;
                        float v = Offseason.Value(q);
                        if (v > bestV) { bestV = v; best = q; }
                    }
                    Player pl;
                    if (best != null) { pool.Remove(best); pl = best; }
                    else
                    {
                        int peak = (int)Math.Round(rng.Gaussian(60f, 5f));
                        pl = CareerFactory.NewRegen(s, rng, used, pos, peak, rng.Range(23, 30));
                    }
                    pl.Contract = CapMath.Minimum(s, 1, s.Year + 1);
                    Offseason.AssignJersey(t, pl, rng);
                    t.AddPlayer(pl);
                }
            }
        }

        /// <summary>Cuts the least valuable non-starters until the active roster has <paramref name="target"/> players; useful youngsters go to the practice squad.</summary>
        public static void CutDownTo(CareerSave s, Team t, int target, bool toSquad)
        {
            DepthManager.RebuildAuto(t);
            int guard = 0;
            while (t.Roster.Count > target && guard++ < 80)
            {
                Player worst = null; float worstV = float.MaxValue;
                for (int pass = 0; pass < 2 && worst == null; pass++)
                {
                    for (int i = 0; i < t.Roster.Count; i++)
                    {
                        Player p = t.Roster[i];
                        if (Offseason.IsStarter(t, p)) continue;
                        if (pass == 0 && t.CountAt(p.Position) <= LeagueRules.ProSlots(p.Position)) continue;
                        float v = Offseason.KeepValue(p) + Offseason.DeadMoneyOf(p.Contract) / 1000000f * 1.5f;
                        if (v < worstV) { worstV = v; worst = p; }
                    }
                }
                if (worst == null) break;
                if (toSquad && t.PracticeSquad.Count < RosterRules.PracticeSquadLimit && EligibleForPracticeSquad(worst) && (worst.Potential >= 62 || worst.Experience == 0))
                {
                    t.RemovePlayer(worst);
                    worst.Contract = CapMath.PracticeSquad(s, s.Year + 1);
                    t.PracticeSquad.Add(worst);
                    Ledger.Tx(s, TransactionKind.PracticeSquad, t.Id, worst, worst.Contract.CapHit, worst.Name + " moved to the practice squad.");
                }
                else
                {
                    long now, next; CapManager.DeadMoneyPreview(worst.Contract, out now, out next);
                    t.RemovePlayer(worst);
                    RememberPrior(s, worst, t.Id);
                    Offseason.Release(s.Pro, worst, t);
                    Ledger.Tx(s, TransactionKind.Release, t.Id, worst, now + next, worst.Name + " was released by the " + t.Nickname + ".");
                }
            }
        }

        /// <summary>Fills the practice squad to its limit with undrafted youngsters from the pool or fresh signings.</summary>
        public static void FillPracticeSquad(CareerSave s, Team t, Rng rng, HashSet<string> used)
        {
            // trim an oversized squad first (worst potential out)
            while (t.PracticeSquad.Count > RosterRules.PracticeSquadLimit)
            {
                Player w = null; float wv = float.MaxValue;
                for (int i = 0; i < t.PracticeSquad.Count; i++)
                {
                    float v = t.PracticeSquad[i].Potential + t.PracticeSquad[i].Overall * 0.5f;
                    if (v < wv) { wv = v; w = t.PracticeSquad[i]; }
                }
                t.PracticeSquad.Remove(w);
                w.Contract = new Contract(); w.TeamId = -1; s.Pro.FreeAgents.Add(w);
            }
            int guard = 0;
            while (t.PracticeSquad.Count < RosterRules.PracticeSquadLimit && guard++ < 40)
            {
                // position needs: spread across groups
                Position pos = LeagueRules.RosterOrder[(t.PracticeSquad.Count * 7 + guard) % LeagueRules.RosterOrder.Length];
                if (pos == Position.K || pos == Position.P || pos == Position.FB) pos = Position.WR;
                Player best = null; float bestV = -1f;
                var pool = s.Pro.FreeAgents;
                for (int k = 0; k < pool.Count; k++)
                {
                    Player q = pool[k];
                    if (PositionBuckets.Of(q.Position) != PositionBuckets.Of(pos) || q.Experience > RosterRules.PracticeSquadMaxExperience || q.Bio.Age > 27) continue;
                    float v = q.Potential * 0.6f + q.Overall * 0.4f;
                    if (v > bestV) { bestV = v; best = q; }
                }
                Player pl;
                if (best != null && bestV >= 55f) { pool.Remove(best); pl = best; }
                else
                {
                    int peak = (int)Math.Round(rng.Gaussian(58f, 5f));
                    pl = CareerFactory.NewRegen(s, rng, used, pos, peak, rng.Range(21, 25), true);
                    pl.Experience = Math.Min(pl.Experience, 2);
                    pl.Potential = Math.Max(pl.Potential, pl.Overall + rng.Range(0, 8));
                }
                pl.Contract = CapMath.PracticeSquad(s, s.Year + 1);
                pl.TeamId = t.Id;
                t.PracticeSquad.Add(pl);
            }
        }

        /// <summary>Auto-manages the user's team like a CPU team: cap fix, cuts, squad (used by "auto" settings and the soak test).</summary>
        public static void AutoManageUser(CareerSave s, Rng rng, HashSet<string> used)
        {
            FinalCuts(s, s.UserTeam, rng, used);
        }
    }
}
