using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>One game before it is placed in a week.</summary>
    public struct Matchup
    {
        public int Home, Away;
        public bool NonConference;
        public Matchup(int h, int a, bool nonConf = false) { Home = h; Away = a; NonConference = nonConf; }
    }

    /// <summary>
    /// Pro: 17 games = 6 division + 4 rotating same-conference division + 4 rotating other-conference division
    /// + 2 same-place games + 1 extra same-place interconference game, spread over 18 weeks with one bye each.
    /// College: 12 games = 9 conference (circulant graph) + 3 non-conference (independents play 12 non-conference).
    /// </summary>
    public static class ScheduleGenerator
    {
        // -------------------------------------------------------------- pro
        public static List<Matchup> ProMatchups(ProLeague league, int year, int[] placeOf)
        {
            var games = new List<Matchup>();
            // D[c][d][p] = team id
            int[][][] D = new int[2][][];
            for (int c = 0; c < 2; c++)
            {
                D[c] = new int[4][];
                for (int d = 0; d < 4; d++)
                {
                    D[c][d] = new int[4];
                    Division div = league.Conferences[c].Divisions[d];
                    for (int k = 0; k < div.TeamIds.Count; k++) D[c][d][placeOf[div.TeamIds[k]]] = div.TeamIds[k];
                }
            }
            int y = year;
            // 1. division games (home and home)
            for (int c = 0; c < 2; c++)
                for (int d = 0; d < 4; d++)
                    for (int a = 0; a < 4; a++)
                        for (int b = a + 1; b < 4; b++)
                        {
                            games.Add(new Matchup(D[c][d][a], D[c][d][b]));
                            games.Add(new Matchup(D[c][d][b], D[c][d][a]));
                        }
            // 2. intra-conference rotation
            int[][] pairings = { new[] { 0, 1, 2, 3 }, new[] { 0, 2, 1, 3 }, new[] { 0, 3, 1, 2 } };
            int[] pr = pairings[((y % 3) + 3) % 3];
            for (int c = 0; c < 2; c++)
            {
                for (int pi = 0; pi < 2; pi++)
                {
                    int di = pr[pi * 2], dj = pr[pi * 2 + 1];
                    for (int p = 0; p < 4; p++)
                        for (int q = 0; q < 4; q++)
                        {
                            bool iHosts = ((p + q + y) % 2 == 0);
                            games.Add(iHosts ? new Matchup(D[c][di][p], D[c][dj][q]) : new Matchup(D[c][dj][q], D[c][di][p]));
                        }
                }
            }
            // 3. inter-conference rotation (Liberty division i vs Frontier division pi(i))
            for (int i = 0; i < 4; i++)
            {
                int j = (i + (((y % 4) + 4) % 4)) % 4;
                for (int p = 0; p < 4; p++)
                    for (int q = 0; q < 4; q++)
                    {
                        bool libHosts = ((p + q + y) % 2 == 0);
                        games.Add(libHosts ? new Matchup(D[0][i][p], D[1][j][q]) : new Matchup(D[1][j][q], D[0][i][p]));
                    }
            }
            // 4. same-place games against the two divisions outside the rotation pair (4-cycle orientation)
            for (int c = 0; c < 2; c++)
            {
                for (int pi = 0; pi < 2; pi++)
                {
                    int i = pr[pi * 2], m = pr[pi * 2 + 1];
                    int k = pr[(1 - pi) * 2], l = pr[(1 - pi) * 2 + 1];
                    if (pi == 1) continue; // each cross pair is created once (from pairing 0 perspective)
                    for (int p = 0; p < 4; p++)
                    {
                        bool flip = ((y + p) % 2 == 1);
                        int[] cycle = { D[c][i][p], D[c][k][p], D[c][m][p], D[c][l][p] };
                        for (int e = 0; e < 4; e++)
                        {
                            int a = cycle[e], b = cycle[(e + 1) % 4];
                            games.Add(flip ? new Matchup(b, a) : new Matchup(a, b));
                        }
                    }
                }
            }
            // 5. 17th game: Liberty division i vs Frontier division sigma(i), same place
            for (int i = 0; i < 4; i++)
            {
                int j = (i + (((y % 4) + 4) % 4) + 1) % 4;
                bool libHosts = ((i + y) % 2 == 0);
                for (int p = 0; p < 4; p++)
                    games.Add(libHosts ? new Matchup(D[0][i][p], D[1][j][p]) : new Matchup(D[1][j][p], D[0][i][p]));
            }
            return games;
        }

        /// <summary>
        /// Places 17 games per team into 18 weeks with exactly one bye (weeks 5-14) each. Returns false if it could not
        /// find a valid arrangement within the attempt budget.
        /// </summary>
        public static bool AssignProWeeks(List<Matchup> games, int teamCount, Rng rng, out int[] weekOfGame, out int[] byeWeek)
        {
            const int weeks = 18;
            weekOfGame = new int[games.Count];
            byeWeek = new int[teamCount];
            for (int attempt = 0; attempt < 400; attempt++)
            {
                // bye weeks: 8 of the 10 weeks in 5..14, four teams each
                var byeWeeks = new List<int>();
                for (int w = 5; w <= 14; w++) byeWeeks.Add(w);
                rng.Shuffle(byeWeeks);
                var order = new List<int>();
                for (int t = 0; t < teamCount; t++) order.Add(t);
                rng.Shuffle(order);
                for (int i = 0; i < teamCount; i++) byeWeek[order[i]] = byeWeeks[i / 4];

                var pairGames = new List<int>[teamCount, teamCount];
                for (int i = 0; i < games.Count; i++)
                {
                    int a = Math.Min(games[i].Home, games[i].Away), b = Math.Max(games[i].Home, games[i].Away);
                    if (pairGames[a, b] == null) pairGames[a, b] = new List<int>();
                    pairGames[a, b].Add(i);
                }
                for (int i = 0; i < weekOfGame.Length; i++) weekOfGame[i] = 0;

                bool ok = true;
                for (int w = 1; w <= weeks && ok; w++)
                {
                    var avail = new List<int>();
                    for (int t = 0; t < teamCount; t++) if (byeWeek[t] != w) avail.Add(t);
                    List<int[]> match = null;
                    for (int tries = 0; tries < 25 && match == null; tries++) match = PerfectMatching(avail, pairGames, rng);
                    if (match == null) { ok = false; break; }
                    for (int m = 0; m < match.Count; m++)
                    {
                        int a = Math.Min(match[m][0], match[m][1]), b = Math.Max(match[m][0], match[m][1]);
                        List<int> l = pairGames[a, b];
                        int pick = rng.Range(0, l.Count);
                        weekOfGame[l[pick]] = w;
                        l.RemoveAt(pick);
                    }
                }
                if (ok)
                {
                    bool all = true;
                    for (int i = 0; i < weekOfGame.Length; i++) if (weekOfGame[i] == 0) all = false;
                    if (all) return true;
                }
            }
            return false;
        }

        private static List<int[]> PerfectMatching(List<int> avail, List<int>[,] pairGames, Rng rng)
        {
            int n = avail.Count;
            var matched = new bool[n];
            var result = new List<int[]>();
            int budget = 40000;
            if (Search(avail, pairGames, matched, result, rng, 0, ref budget)) return result;
            return null;
        }

        private static int Count(List<int>[,] pg, int a, int b)
        {
            int x = Math.Min(a, b), y = Math.Max(a, b);
            return pg[x, y] == null ? 0 : pg[x, y].Count;
        }

        private static bool Search(List<int> avail, List<int>[,] pg, bool[] matched, List<int[]> result, Rng rng, int done, ref int budget)
        {
            int n = avail.Count;
            if (done == n / 2) return true;
            if (budget-- <= 0) return false;
            // most constrained unmatched team
            int best = -1, bestCnt = int.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (matched[i]) continue;
                int cnt = 0;
                for (int j = 0; j < n; j++) if (!matched[j] && j != i && Count(pg, avail[i], avail[j]) > 0) cnt++;
                if (cnt == 0) return false;
                if (cnt < bestCnt || (cnt == bestCnt && rng.Chance(0.3f))) { best = i; bestCnt = cnt; }
            }
            var cands = new List<int>();
            for (int j = 0; j < n; j++) if (!matched[j] && j != best && Count(pg, avail[best], avail[j]) > 0) cands.Add(j);
            rng.Shuffle(cands);
            // prefer opponents that still have many games left against us (double meetings first)
            cands.Sort((x, y) => Count(pg, avail[best], avail[y]).CompareTo(Count(pg, avail[best], avail[x])));
            for (int k = 0; k < cands.Count; k++)
            {
                int j = cands[k];
                matched[best] = matched[j] = true;
                result.Add(new[] { avail[best], avail[j] });
                if (Search(avail, pg, matched, result, rng, done + 1, ref budget)) return true;
                result.RemoveAt(result.Count - 1);
                matched[best] = matched[j] = false;
                if (budget <= 0) return false;
            }
            return false;
        }

        // -------------------------------------------------------------- college
        public static List<Matchup> CollegeMatchups(CollegeWorld world, Rng rng)
        {
            var games = new List<Matchup>();
            int n = world.Schools.Count;
            var played = new HashSet<long>();
            var degree = new int[n];

            for (int c = 0; c < world.Conferences.Count; c++)
            {
                List<int> members = new List<int>(world.Conferences[c].TeamIds);
                rng.Shuffle(members);
                int m = members.Count;
                if (m <= 10)
                {
                    for (int i = 0; i < m; i++)
                        for (int j = i + 1; j < m; j++)
                            AddEdge(games, played, degree, members[i], members[j], rng.Chance(0.5f), false);
                }
                else
                {
                    for (int i = 0; i < m; i++)
                        for (int k = 1; k <= 4; k++)
                            AddEdge(games, played, degree, members[i], members[(i + k) % m], true, false);
                    for (int i = 0; m % 2 == 0 && i < m / 2; i++)
                        AddEdge(games, played, degree, members[i], members[i + m / 2], (i % 2) == 0, false);
                }
            }

            // non-conference games
            var homeCount = new int[n];
            for (int i = 0; i < games.Count; i++) homeCount[games[i].Home]++;
            var need = new int[n];
            for (int i = 0; i < n; i++) need[i] = LeagueRules.CollegeGames - degree[i];

            for (int attempt = 0; attempt < 400; attempt++)
            {
                var rem = (int[])need.Clone();
                var trial = new List<Matchup>();
                var trialPlayed = new HashSet<long>(played);
                bool failed = false;
                bool relax = attempt > 150;
                while (true)
                {
                    // team with the most remaining games
                    int a = -1, ar = 0;
                    for (int i = 0; i < n; i++) if (rem[i] > ar || (rem[i] == ar && rem[i] > 0 && rng.Chance(0.2f))) { a = i; ar = rem[i]; }
                    if (a < 0 || ar == 0) break;
                    var cands = new List<int>();
                    for (int b = 0; b < n; b++)
                    {
                        if (b == a || rem[b] <= 0) continue;
                        long key = Key(a, b);
                        if (trialPlayed.Contains(key)) continue;
                        bool sameConf = world.Schools[a].ConferenceId >= 0 && world.Schools[a].ConferenceId == world.Schools[b].ConferenceId;
                        if (sameConf && !relax) continue;
                        cands.Add(b);
                    }
                    if (cands.Count == 0) { failed = true; break; }
                    // prefer partners with many remaining games to avoid stranding teams
                    cands.Sort((x, y) => rem[y].CompareTo(rem[x]));
                    int span = Math.Min(cands.Count, 8);
                    int bpick = cands[rng.Range(0, span)];
                    trialPlayed.Add(Key(a, bpick));
                    rem[a]--; rem[bpick]--;
                    // home for the team with fewer home dates so far
                    int ha = homeCount[a] + CountHome(trial, a), hb = homeCount[bpick] + CountHome(trial, bpick);
                    bool aHome = ha < hb || (ha == hb && rng.Chance(0.5f));
                    trial.Add(aHome ? new Matchup(a, bpick, true) : new Matchup(bpick, a, true));
                }
                if (!failed)
                {
                    games.AddRange(trial);
                    return games;
                }
            }
            return games; // extremely unlikely: a few teams may play fewer games
        }

        private static int CountHome(List<Matchup> l, int t)
        {
            int c = 0;
            for (int i = 0; i < l.Count; i++) if (l[i].Home == t) c++;
            return c;
        }

        private static long Key(int a, int b) { int x = Math.Min(a, b), y = Math.Max(a, b); return ((long)x << 32) | (uint)y; }

        private static void AddEdge(List<Matchup> games, HashSet<long> played, int[] degree, int home, int away, bool homeFirst, bool nonConf)
        {
            if (!played.Add(Key(home, away))) return;
            games.Add(homeFirst ? new Matchup(home, away, nonConf) : new Matchup(away, home, nonConf));
            degree[home]++; degree[away]++;
        }

        /// <summary>Places college games into weeks (12 games in as few weeks as practical, 2+ byes). Returns the week count.</summary>
        public static int AssignCollegeWeeks(List<Matchup> games, int teamCount, Rng rng, out int[] weekOfGame)
        {
            weekOfGame = new int[games.Count];
            var remaining = new int[teamCount];
            for (int i = 0; i < games.Count; i++) { remaining[games[i].Home]++; remaining[games[i].Away]++; }
            var left = new List<int>();
            for (int i = 0; i < games.Count; i++) left.Add(i);
            const int targetWeeks = 14;
            int week = 0;
            while (left.Count > 0 && week < 20)
            {
                week++;
                int weeksLeft = Math.Max(1, targetWeeks - week + 1);
                var busy = new bool[teamCount];
                // order: games between urgent teams first, non-conference first in the early weeks
                var order = new List<int>(left);
                rng.Shuffle(order);
                bool early = week <= 4;
                order.Sort((x, y) =>
                {
                    float sx = Urgency(games[x], remaining, weeksLeft) + (early == games[x].NonConference ? 0.35f : 0f);
                    float sy = Urgency(games[y], remaining, weeksLeft) + (early == games[y].NonConference ? 0.35f : 0f);
                    return sy.CompareTo(sx);
                });
                var placed = new List<int>();
                for (int k = 0; k < order.Count; k++)
                {
                    int gi = order[k];
                    Matchup m = games[gi];
                    if (busy[m.Home] || busy[m.Away]) continue;
                    // skip optionally to leave a bye if both teams are comfortably ahead of schedule
                    bool slackH = remaining[m.Home] < weeksLeft - 1, slackA = remaining[m.Away] < weeksLeft - 1;
                    if (slackH && slackA && week > 1 && rng.Chance(0.1f)) continue;
                    busy[m.Home] = busy[m.Away] = true;
                    weekOfGame[gi] = week;
                    placed.Add(gi);
                }
                for (int p = 0; p < placed.Count; p++)
                {
                    left.Remove(placed[p]);
                    remaining[games[placed[p]].Home]--; remaining[games[placed[p]].Away]--;
                }
            }
            return week;
        }

        private static float Urgency(Matchup m, int[] remaining, int weeksLeft)
        {
            return (float)(remaining[m.Home] + remaining[m.Away]) / (2f * weeksLeft);
        }
    }
}
