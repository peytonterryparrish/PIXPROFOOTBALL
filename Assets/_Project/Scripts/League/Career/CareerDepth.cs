using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>One entry the depth chart screen shows: a player with his rating at the slot and flags.</summary>
    public sealed class DepthEntry
    {
        public int PlayerId;
        public string Name;
        public Position NaturalPos;
        public int OverallAtSlot;
        public bool Injured;
        public int InjuryWeeks;
        public bool Starter;
        public bool OutOfPosition;
    }

    /// <summary>
    /// Depth chart editing with position eligibility. The chart is rebuilt automatically after roster moves; positions the user has edited
    /// by hand (<see cref="CareerSave.ManualDepth"/>) keep their order.
    /// </summary>
    public static class DepthManager
    {
        /// <summary>The player's overall when he lines up at the given slot (his ratings re-weighted for that position).</summary>
        public static int OverallAt(Player p, Position slot) { return p.Ratings.Overall(slot); }

        /// <summary>A player can be listed at his own position and at the neighbouring ones (a guard at tackle, a corner at safety, ...).</summary>
        public static bool IsEligible(Player p, Position slot)
        {
            if (p.Position == slot) return true;
            Position[] rel = DepthChartBuilder.Related(slot);
            for (int i = 0; i < rel.Length; i++) if (rel[i] == p.Position) return true;
            return false;
        }

        public static bool IsManual(CareerSave s, int teamId, Position pos)
        {
            for (int i = 0; i < s.ManualDepth.Count; i++) if (s.ManualDepth[i].TeamId == teamId && s.ManualDepth[i].Pos == pos) return true;
            return false;
        }

        private static void SetManual(CareerSave s, int teamId, Position pos, bool manual)
        {
            for (int i = s.ManualDepth.Count - 1; i >= 0; i--)
                if (s.ManualDepth[i].TeamId == teamId && s.ManualDepth[i].Pos == pos) s.ManualDepth.RemoveAt(i);
            if (manual) s.ManualDepth.Add(new ManualDepth { TeamId = teamId, Pos = pos });
        }

        /// <summary>Plain rebuild by rating (CPU teams, and the first pass of <see cref="Refresh"/>).</summary>
        public static void RebuildAuto(Team t) { DepthChartBuilder.Build(t); }

        /// <summary>
        /// Rebuilds a team's depth chart after a roster change. CPU teams (and a user with AutoDepthChart on) get a fresh auto chart with
        /// manually edited positions preserved; otherwise the old chart is only cleaned of players who left and new players are appended.
        /// </summary>
        public static void Refresh(CareerSave s, Team t)
        {
            bool user = t.Id == s.UserTeamId;
            var old = t.Depth;
            if (user && !s.Settings.AutoDepthChart)
            {
                Prune(t, old);
                Append(t, old);
                return;
            }
            var manual = new List<KeyValuePair<Position, List<int>>>();
            if (user)
            {
                for (int i = 0; i < s.ManualDepth.Count; i++)
                {
                    if (s.ManualDepth[i].TeamId != t.Id) continue;
                    List<int> ids;
                    if (old.TryGet(s.ManualDepth[i].Pos, out ids)) manual.Add(new KeyValuePair<Position, List<int>>(s.ManualDepth[i].Pos, new List<int>(ids)));
                }
            }
            DepthChartBuilder.Build(t);
            for (int i = 0; i < manual.Count; i++)
            {
                Position pos = manual[i].Key;
                var keep = new List<int>();
                for (int k = 0; k < manual[i].Value.Count; k++)
                {
                    Player p = t.GetPlayer(manual[i].Value[k]);
                    if (p != null && !keep.Contains(p.Id) && IsEligible(p, pos)) keep.Add(p.Id);
                }
                var auto = t.Depth.Get(pos);
                for (int k = 0; k < auto.Count; k++) if (!keep.Contains(auto[k])) keep.Add(auto[k]);
                auto.Clear(); auto.AddRange(keep);
            }
        }

        private static void Prune(Team t, DepthChart chart)
        {
            for (int i = 0; i < chart.Slots.Count; i++)
                chart.Slots[i].PlayerIds.RemoveAll(id => t.GetPlayer(id) == null);
            t.InvalidateIndex();
        }

        private static void Append(Team t, DepthChart chart)
        {
            for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
            {
                Position pos = LeagueRules.RosterOrder[i];
                var ids = chart.Get(pos);
                var add = new List<Player>();
                for (int k = 0; k < t.Roster.Count; k++)
                    if (t.Roster[k].Position == pos && !ids.Contains(t.Roster[k].Id)) add.Add(t.Roster[k]);
                add.Sort((a, b) => b.Ratings.Overall(pos).CompareTo(a.Ratings.Overall(pos)));
                for (int k = 0; k < add.Count; k++) ids.Add(add[k].Id);
            }
        }

        // ---------------------------------------------------------------- queries for the screen
        public static List<DepthEntry> Entries(CareerSave s, Team t, Position pos)
        {
            var l = new List<DepthEntry>();
            List<int> ids;
            if (!t.Depth.TryGet(pos, out ids)) return l;
            int starters = LeagueRules.Starters(pos);
            for (int i = 0; i < ids.Count; i++)
            {
                Player p = t.GetPlayer(ids[i]);
                if (p == null) continue;
                l.Add(new DepthEntry
                {
                    PlayerId = p.Id, Name = p.Name, NaturalPos = p.Position, OverallAtSlot = OverallAt(p, pos), Injured = p.Injury.IsInjured,
                    InjuryWeeks = p.Injury.WeeksOut, Starter = i < starters, OutOfPosition = p.Position != pos
                });
            }
            return l;
        }

        /// <summary>Players that could be added to a position's chart (eligible, not already listed), best at the slot first.</summary>
        public static List<Player> Candidates(Team t, Position pos)
        {
            var l = new List<Player>();
            List<int> ids;
            t.Depth.TryGet(pos, out ids);
            for (int i = 0; i < t.Roster.Count; i++)
            {
                Player p = t.Roster[i];
                if (!IsEligible(p, pos)) continue;
                if (ids != null && ids.Contains(p.Id)) continue;
                l.Add(p);
            }
            l.Sort((a, b) => OverallAt(b, pos).CompareTo(OverallAt(a, pos)));
            return l;
        }

        /// <summary>Problems on the chart: empty starter slots, injured or retired starters, ineligible players.</summary>
        public static List<string> Issues(Team t)
        {
            var issues = new List<string>();
            for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
            {
                Position pos = LeagueRules.RosterOrder[i];
                List<int> ids;
                int need = LeagueRules.Starters(pos);
                int filled = 0;
                if (t.Depth.TryGet(pos, out ids))
                {
                    for (int k = 0; k < ids.Count && k < need; k++)
                    {
                        Player p = t.GetPlayer(ids[k]);
                        if (p == null) continue;
                        filled++;
                        if (p.Injury.IsInjured) issues.Add(pos + " starter " + p.Name + " is injured (" + p.Injury.WeeksOut + " wk).");
                        if (!IsEligible(p, pos)) issues.Add(p.Name + " cannot play " + pos + ".");
                    }
                }
                if (filled < need) issues.Add(pos + " has " + filled + " of " + need + " starters.");
            }
            return issues;
        }

        // ---------------------------------------------------------------- edits (all mark the position as manual)
        private static OpResult Check(Team t, Position pos, int playerId, out Player p)
        {
            p = t.GetPlayer(playerId);
            if (p == null) return OpResult.Fail("The player is not on the active roster.");
            if (!IsEligible(p, pos)) return OpResult.Fail(p.Name + " (" + p.Position + ") cannot play " + pos + ".");
            return null;
        }

        /// <summary>Replaces the whole order of a position (every id must be an eligible, active player).</summary>
        public static OpResult SetOrder(CareerSave s, Team t, Position pos, IList<int> playerIds)
        {
            var seen = new HashSet<int>();
            var list = new List<int>();
            for (int i = 0; i < playerIds.Count; i++)
            {
                Player p;
                OpResult bad = Check(t, pos, playerIds[i], out p);
                if (bad != null) return bad;
                if (!seen.Add(p.Id)) return OpResult.Fail(p.Name + " is listed twice.");
                list.Add(p.Id);
            }
            var slot = t.Depth.Get(pos);
            slot.Clear(); slot.AddRange(list);
            SetManual(s, t.Id, pos, true);
            return OpResult.Success();
        }

        public static OpResult Swap(CareerSave s, Team t, Position pos, int indexA, int indexB)
        {
            var ids = t.Depth.Get(pos);
            if (indexA < 0 || indexB < 0 || indexA >= ids.Count || indexB >= ids.Count) return OpResult.Fail("No such depth slot.");
            int tmp = ids[indexA]; ids[indexA] = ids[indexB]; ids[indexB] = tmp;
            SetManual(s, t.Id, pos, true);
            return OpResult.Success();
        }

        /// <summary>Puts a player at a depth index of a position (adds him when he is not listed, moves him when he is).</summary>
        public static OpResult Insert(CareerSave s, Team t, Position pos, int playerId, int index)
        {
            Player p;
            OpResult bad = Check(t, pos, playerId, out p);
            if (bad != null) return bad;
            var ids = t.Depth.Get(pos);
            ids.Remove(playerId);
            index = Math.Max(0, Math.Min(index, ids.Count));
            ids.Insert(index, playerId);
            SetManual(s, t.Id, pos, true);
            return OpResult.Success();
        }

        public static OpResult Remove(CareerSave s, Team t, Position pos, int playerId)
        {
            var ids = t.Depth.Get(pos);
            if (!ids.Remove(playerId)) return OpResult.Fail("He is not on that depth chart.");
            SetManual(s, t.Id, pos, true);
            return OpResult.Success();
        }

        /// <summary>Back to the automatic order for one position.</summary>
        public static void AutoSort(CareerSave s, Team t, Position pos)
        {
            SetManual(s, t.Id, pos, false);
            Refresh(s, t);
        }

        public static void AutoSortAll(CareerSave s, Team t)
        {
            for (int i = s.ManualDepth.Count - 1; i >= 0; i--) if (s.ManualDepth[i].TeamId == t.Id) s.ManualDepth.RemoveAt(i);
            Refresh(s, t);
        }
    }
}
