using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>Consistency checks for a career save (used by the tests and the soak run; cheap enough to call from a debug menu).</summary>
    public static class CareerValidator
    {
        /// <summary>
        /// Checks the league. <paramref name="atSeasonStart"/> demands the strict state of the start of week 1 (53 active, full practice squad,
        /// every team under the cap); the general checks (unique ids, valid contracts, nobody in two places) always run.
        /// </summary>
        public static List<string> Check(CareerSave s, bool atSeasonStart)
        {
            var problems = new List<string>();
            var seen = new HashSet<int>();
            ProLeague pro = s.Pro;
            var names = new HashSet<string>();
            for (int ti = 0; ti < pro.Teams.Count; ti++)
            {
                Team t = pro.Teams[ti];
                string tn = t.Nickname;
                foreach (Player p in CapManager.AllUnderContract(t))
                {
                    if (!seen.Add(p.Id)) problems.Add(tn + ": " + p.Name + " (" + p.Id + ") appears twice in the league");
                    if (!names.Add(p.Name)) problems.Add(tn + ": the name " + p.Name + " is used twice in the league");
                    if (p.Retired) problems.Add(tn + ": retired player " + p.Name + " is on the roster");
                    if (p.Contract.CapHits.Count != p.Contract.Years) problems.Add(tn + ": " + p.Name + " has " + p.Contract.CapHits.Count + " cap hits for " + p.Contract.Years + " years");
                    if (p.Contract.YearsLeft > p.Contract.Years) problems.Add(tn + ": " + p.Name + " has more years left than years");
                    if (p.Contract.YearsLeft < 0) problems.Add(tn + ": " + p.Name + " has negative years left");
                    if (p.Overall < 0 || p.Overall > 99) problems.Add(tn + ": " + p.Name + " overall out of range");
                }
                if (t.Roster.Count > RosterRules.OffseasonLimit) problems.Add(tn + ": roster " + t.Roster.Count + " above the offseason limit");
                if (t.PracticeSquad.Count > RosterRules.PracticeSquadLimit) problems.Add(tn + ": practice squad " + t.PracticeSquad.Count);
                for (int i = 0; i < t.InjuredReserve.Count; i++)
                    if (t.InjuredReserve[i].Injury.IrWeeks < 0) problems.Add(tn + ": IR player " + t.InjuredReserve[i].Name + " has no IR clock");
                for (int i = 0; i < t.PracticeSquad.Count; i++)
                    if (t.PracticeSquad[i].Injury.IsInjured && t.PracticeSquad[i].Injury.WeeksOut > 30) problems.Add(tn + ": long-term injured player on the practice squad");
                if (t.HeadCoach == null || t.OffensiveCoordinator == null || t.DefensiveCoordinator == null)
                {
                    if (ti != s.UserTeamId) problems.Add(tn + ": coaching vacancy");
                }
                if (t.DeadCap < 0 || t.DeadCapNext < 0) problems.Add(tn + ": negative dead cap");
                if (atSeasonStart)
                {
                    if (t.Roster.Count != RosterRules.ActiveLimit) problems.Add(tn + ": " + t.Roster.Count + " active players (need " + RosterRules.ActiveLimit + ")");
                    if (t.PracticeSquad.Count < 10 || t.PracticeSquad.Count > RosterRules.PracticeSquadLimit) problems.Add(tn + ": practice squad of " + t.PracticeSquad.Count);
                    if (t.CapUsed > t.SalaryCap) problems.Add(tn + ": over the cap by " + (t.CapUsed - t.SalaryCap));
                    for (int i = 0; i < PositionBuckets.All.Length; i++)
                    {
                        PositionBucket b = PositionBuckets.All[i];
                        int have = 0;
                        for (int k = 0; k < t.Roster.Count; k++) if (PositionBuckets.Of(t.Roster[k].Position) == b) have++;
                        if (have < RosterRules.Minimum(b)) problems.Add(tn + ": only " + have + " at " + b);
                    }
                    var issues = DepthManager.Issues(t);
                    for (int i = 0; i < issues.Count; i++) if (issues[i].Contains("starters")) problems.Add(tn + ": " + issues[i]);
                }
                // jersey numbers on the active roster are unique
                var jerseys = new HashSet<int>();
                for (int i = 0; i < t.Roster.Count; i++)
                    if (!jerseys.Add(t.Roster[i].Bio.JerseyNumber)) problems.Add(tn + ": duplicate jersey " + t.Roster[i].Bio.JerseyNumber);
            }
            for (int i = 0; i < pro.FreeAgents.Count; i++)
                if (!seen.Add(pro.FreeAgents[i].Id)) problems.Add("free agent " + pro.FreeAgents[i].Name + " is also elsewhere");
            for (int i = 0; i < s.Waivers.Count; i++)
                if (!seen.Add(s.Waivers[i].PlayerId)) problems.Add("waived player " + s.Waivers[i].Player.Name + " is also elsewhere");
            // pick book: every pick has one owner, no duplicates
            var pickKeys = new HashSet<string>();
            for (int i = 0; i < s.Picks.Count; i++)
            {
                DraftPickAsset k = s.Picks[i];
                if (!pickKeys.Add(k.Year + "/" + k.Round + "/" + k.OriginalTeamId)) problems.Add("duplicate draft pick " + k.Year + " r" + k.Round);
                if (s.Pro.GetTeam(k.OwnerTeamId) == null) problems.Add("pick owned by an unknown team");
            }
            return problems;
        }

        /// <summary>Average overall of the depth-chart starters across the league (the "league OVR" the soak test watches).</summary>
        public static float LeagueStarterOverall(CareerSave s) { return Offseason.AverageStarterOverall(s.Pro); }

        public static float LeagueRosterOverall(CareerSave s) { return Offseason.AverageRosterOverall(s.Pro); }

        public static float AverageAge(CareerSave s)
        {
            double sum = 0; int n = 0;
            for (int t = 0; t < s.Pro.Teams.Count; t++)
                for (int i = 0; i < s.Pro.Teams[t].Roster.Count; i++) { sum += s.Pro.Teams[t].Roster[i].Age; n++; }
            return n == 0 ? 0f : (float)(sum / n);
        }
    }
}
