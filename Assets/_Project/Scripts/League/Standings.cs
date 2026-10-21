using System;
using System.Collections.Generic;

namespace PixFootball.League
{
    [Serializable]
    public class StandingsRow
    {
        public int TeamId;
        public int ConferenceId, DivisionId;
        public string Name = "";
        public int W, L, T;
        public int PF, PA;
        public int DivW, DivL, DivT;
        public int ConfW, ConfL, ConfT;
        /// <summary>Rank inside the division (1 = leader), or inside the conference when there are no divisions.</summary>
        public int GroupRank;
        /// <summary>Rank within the conference after tiebreakers.</summary>
        public int ConferenceRank;
        public int Streak;

        public int Games { get { return W + L + T; } }
        public float Pct { get { int g = W + L + T; return g == 0 ? 0f : (W + 0.5f * T) / g; } }
        public float DivPct { get { int g = DivW + DivL + DivT; return g == 0 ? 0f : (DivW + 0.5f * DivT) / g; } }
        public float ConfPct { get { int g = ConfW + ConfL + ConfT; return g == 0 ? 0f : (ConfW + 0.5f * ConfT) / g; } }
        public string Record { get { return W + "-" + L + (T > 0 ? "-" + T : ""); } }
    }

    /// <summary>
    /// Builds standings from played regular-season games. Tiebreakers (in order): win percentage, head-to-head among the
    /// tied teams, division record (same division), conference record, point differential, team id.
    /// </summary>
    public static class StandingsCalculator
    {
        public static List<StandingsRow> Compute(Season s)
        {
            var map = new Dictionary<int, StandingsRow>();
            var rows = new List<StandingsRow>();
            for (int i = 0; i < s.Teams.Count; i++)
            {
                SeasonTeam t = s.Teams[i];
                var r = new StandingsRow { TeamId = t.TeamId, ConferenceId = t.ConferenceId, DivisionId = t.DivisionId, Name = t.Name };
                map[t.TeamId] = r; rows.Add(r);
            }
            // results by team in game order for streaks
            var lastResult = new Dictionary<int, int>();
            var games = new List<ScheduledGame>();
            for (int i = 0; i < s.Games.Count; i++) if (s.Games[i].Played && s.Games[i].Kind == GameKind.Regular) games.Add(s.Games[i]);
            games.Sort((a, b) => a.Week.CompareTo(b.Week));
            for (int i = 0; i < games.Count; i++)
            {
                ScheduledGame g = games[i];
                StandingsRow h = map[g.HomeId], a = map[g.AwayId];
                h.PF += g.HomeScore; h.PA += g.AwayScore;
                a.PF += g.AwayScore; a.PA += g.HomeScore;
                bool sameDiv = h.DivisionId >= 0 && h.DivisionId == a.DivisionId;
                bool sameConf = h.ConferenceId >= 0 && h.ConferenceId == a.ConferenceId;
                if (g.HomeScore > g.AwayScore)
                {
                    h.W++; a.L++;
                    if (sameDiv) { h.DivW++; a.DivL++; }
                    if (sameConf) { h.ConfW++; a.ConfL++; }
                    h.Streak = h.Streak > 0 ? h.Streak + 1 : 1;
                    a.Streak = a.Streak < 0 ? a.Streak - 1 : -1;
                }
                else if (g.AwayScore > g.HomeScore)
                {
                    a.W++; h.L++;
                    if (sameDiv) { a.DivW++; h.DivL++; }
                    if (sameConf) { a.ConfW++; h.ConfL++; }
                    a.Streak = a.Streak > 0 ? a.Streak + 1 : 1;
                    h.Streak = h.Streak < 0 ? h.Streak - 1 : -1;
                }
                else
                {
                    h.T++; a.T++;
                    if (sameDiv) { h.DivT++; a.DivT++; }
                    if (sameConf) { h.ConfT++; a.ConfT++; }
                    h.Streak = 0; a.Streak = 0;
                }
            }

            var h2h = new Dictionary<long, float[]>();
            for (int i = 0; i < games.Count; i++)
            {
                ScheduledGame g = games[i];
                Add(h2h, g.HomeId, g.AwayId, g.HomeScore > g.AwayScore ? 1f : (g.HomeScore == g.AwayScore ? 0.5f : 0f));
                Add(h2h, g.AwayId, g.HomeId, g.AwayScore > g.HomeScore ? 1f : (g.HomeScore == g.AwayScore ? 0.5f : 0f));
            }

            // rank inside each division (or conference if none) and inside each conference
            var byGroup = new Dictionary<int, List<StandingsRow>>();
            for (int i = 0; i < rows.Count; i++)
            {
                int key = rows[i].DivisionId >= 0 ? rows[i].DivisionId : 1000 + rows[i].ConferenceId;
                List<StandingsRow> l;
                if (!byGroup.TryGetValue(key, out l)) { l = new List<StandingsRow>(); byGroup[key] = l; }
                l.Add(rows[i]);
            }
            foreach (var kv in byGroup)
            {
                var sorted = Sort(kv.Value, h2h, s.IsCollege);
                for (int i = 0; i < sorted.Count; i++) sorted[i].GroupRank = i + 1;
            }
            var byConf = new Dictionary<int, List<StandingsRow>>();
            for (int i = 0; i < rows.Count; i++)
            {
                List<StandingsRow> l;
                if (!byConf.TryGetValue(rows[i].ConferenceId, out l)) { l = new List<StandingsRow>(); byConf[rows[i].ConferenceId] = l; }
                l.Add(rows[i]);
            }
            foreach (var kv in byConf)
            {
                var sorted = Sort(kv.Value, h2h, s.IsCollege);
                for (int i = 0; i < sorted.Count; i++) sorted[i].ConferenceRank = i + 1;
            }
            return rows;
        }

        private static void Add(Dictionary<long, float[]> d, int a, int b, float v)
        {
            long key = ((long)a << 32) | (uint)b;
            float[] arr;
            if (!d.TryGetValue(key, out arr)) { arr = new float[2]; d[key] = arr; }
            arr[0] += v; arr[1] += 1f;
        }

        private static bool H2h(Dictionary<long, float[]> d, int a, int b, out float wins, out float games)
        {
            float[] arr;
            if (d.TryGetValue(((long)a << 32) | (uint)b, out arr)) { wins = arr[0]; games = arr[1]; return true; }
            wins = 0; games = 0; return false;
        }

        /// <summary>Sorts rows best-first applying the tiebreak chain. Returns a new list.</summary>
        public static List<StandingsRow> Sort(IList<StandingsRow> input, Dictionary<long, float[]> h2h, bool byConference = false)
        {
            var list = new List<StandingsRow>(input);
            list.Sort((a, b) =>
            {
                int c = Key(b, byConference).CompareTo(Key(a, byConference));
                return c != 0 ? c : a.TeamId.CompareTo(b.TeamId);
            });
            var result = new List<StandingsRow>(list.Count);
            int i = 0;
            while (i < list.Count)
            {
                int j = i + 1;
                while (j < list.Count && Math.Abs(Key(list[j], byConference) - Key(list[i], byConference)) < 1e-6f) j++;
                if (j - i == 1) result.Add(list[i]);
                else result.AddRange(BreakTie(list.GetRange(i, j - i), h2h, byConference));
                i = j;
            }
            return result;
        }

        private static float Key(StandingsRow r, bool byConf) { return byConf ? r.ConfPct : r.Pct; }

        private static List<StandingsRow> BreakTie(List<StandingsRow> group, Dictionary<long, float[]> h2h, bool byConf)
        {
            int n = group.Count;
            // head-to-head among the group, valid only if every pair met at least once
            bool allMet = true;
            var h2hPct = new float[n];
            for (int a = 0; a < n && allMet; a++)
                for (int b = a + 1; b < n; b++)
                {
                    float w, g;
                    if (!H2h(h2h, group[a].TeamId, group[b].TeamId, out w, out g) || g <= 0f) { allMet = false; break; }
                }
            if (allMet)
            {
                for (int a = 0; a < n; a++)
                {
                    float wins = 0, games = 0;
                    for (int b = 0; b < n; b++)
                    {
                        if (a == b) continue;
                        float w, g;
                        if (H2h(h2h, group[a].TeamId, group[b].TeamId, out w, out g)) { wins += w; games += g; }
                    }
                    h2hPct[a] = games > 0f ? wins / games : 0.5f;
                }
            }
            bool sameDiv = true;
            for (int a = 1; a < n; a++) if (group[a].DivisionId != group[0].DivisionId || group[a].DivisionId < 0) sameDiv = false;
            var idx = new List<int>();
            for (int a = 0; a < n; a++) idx.Add(a);
            idx.Sort((x, y) =>
            {
                int c = h2hPct[y].CompareTo(h2hPct[x]);
                if (c != 0) return c;
                if (sameDiv) { c = group[y].DivPct.CompareTo(group[x].DivPct); if (c != 0) return c; }
                c = byConf ? group[y].Pct.CompareTo(group[x].Pct) : group[y].ConfPct.CompareTo(group[x].ConfPct);
                if (c != 0) return c;
                c = (group[y].PF - group[y].PA).CompareTo(group[x].PF - group[x].PA);
                if (c != 0) return c;
                return group[x].TeamId.CompareTo(group[y].TeamId);
            });
            var res = new List<StandingsRow>(n);
            for (int k = 0; k < n; k++) res.Add(group[idx[k]]);
            return res;
        }

        /// <summary>Builds the head-to-head table (exposed for other modules / tests).</summary>
        public static Dictionary<long, float[]> BuildHeadToHead(Season s)
        {
            var h2h = new Dictionary<long, float[]>();
            for (int i = 0; i < s.Games.Count; i++)
            {
                ScheduledGame g = s.Games[i];
                if (!g.Played || g.Kind != GameKind.Regular) continue;
                Add(h2h, g.HomeId, g.AwayId, g.HomeScore > g.AwayScore ? 1f : (g.HomeScore == g.AwayScore ? 0.5f : 0f));
                Add(h2h, g.AwayId, g.HomeId, g.AwayScore > g.HomeScore ? 1f : (g.HomeScore == g.AwayScore ? 0.5f : 0f));
            }
            return h2h;
        }
    }
}
