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
                for (int p = 0; p < 4; p++)
                {
                    // QA-569: the host alternates by team and by season (not by whole division), so the 17th-game home dates spread over 16 clubs per year
                    bool libHosts = ((i + p + y) % 2 == 0);
                    games.Add(libHosts ? new Matchup(D[0][i][p], D[1][j][p]) : new Matchup(D[1][j][p], D[0][i][p]));
                }
            }
            return games;
        }

        /// <summary>
        /// Places 17 games per team into 18 weeks with exactly one bye (weeks 5-14) each. Returns false if it could not
        /// find a valid arrangement within the attempt budget. When <paramref name="divisionOf"/> (division id per team, -1 = none) is given, week 18
        /// is all divisional games. Several valid arrangements are built and the one with the fewest long home/away streaks and back-to-back rematches wins (QA-568).
        /// </summary>
        public static bool AssignProWeeks(List<Matchup> games, int teamCount, Rng rng, out int[] weekOfGame, out int[] byeWeek, int[] divisionOf = null)
        {
            weekOfGame = new int[games.Count];
            byeWeek = new int[teamCount];
            int[] bestWeeks = null, bestBye = null;
            int bestScore = int.MaxValue, valid = 0;
            for (int attempt = 0; attempt < 400; attempt++)
            {
                int[] wk, bye; int score;
                if (!TryProWeeks(games, teamCount, rng, divisionOf, out wk, out bye, out score)) continue;
                valid++;
                if (score < bestScore) { bestScore = score; bestWeeks = wk; bestBye = bye; }
                if (bestScore == 0 || valid >= 16) break;
            }
            if (bestWeeks == null) return false;
            RepairProStreaks(games, teamCount, bestWeeks);
            weekOfGame = bestWeeks;
            byeWeek = bestBye;
            return true;
        }

        // Swaps the weeks of the two games of a double-meeting pair (division rivals, the finale pair) whenever that shortens a home/away streak of either team.
        // A swap never breaks a week (both games involve the same two teams), so the schedule stays valid.
        private static void RepairProStreaks(List<Matchup> games, int teamCount, int[] wk)
        {
            var byTeam = new List<int>[teamCount];
            for (int t = 0; t < teamCount; t++) byTeam[t] = new List<int>();
            var byPair = new Dictionary<long, List<int>>();
            for (int i = 0; i < games.Count; i++)
            {
                byTeam[games[i].Home].Add(i); byTeam[games[i].Away].Add(i);
                long key = ((long)Math.Min(games[i].Home, games[i].Away) << 32) | (uint)Math.Max(games[i].Home, games[i].Away);
                List<int> l;
                if (!byPair.TryGetValue(key, out l)) { l = new List<int>(); byPair[key] = l; }
                l.Add(i);
            }
            var keys = new List<long>(byPair.Keys);
            keys.Sort();
            for (int pass = 0; pass < 4; pass++)
            {
                bool changed = false;
                for (int k = 0; k < keys.Count; k++)
                {
                    List<int> l = byPair[keys[k]];
                    if (l.Count < 2) continue;
                    int a = games[l[0]].Home, b = games[l[0]].Away;
                    for (int x = 0; x < l.Count; x++)
                        for (int y = x + 1; y < l.Count; y++)
                        {
                            int before = TeamStreakCost(games, byTeam[a], wk, a) + TeamStreakCost(games, byTeam[b], wk, b);
                            int tmp = wk[l[x]]; wk[l[x]] = wk[l[y]]; wk[l[y]] = tmp;
                            int after = TeamStreakCost(games, byTeam[a], wk, a) + TeamStreakCost(games, byTeam[b], wk, b);
                            if (after < before) changed = true;
                            else { tmp = wk[l[x]]; wk[l[x]] = wk[l[y]]; wk[l[y]] = tmp; }
                        }
                }
                if (!changed) break;
            }
        }

        // streak penalty of team t over the whole season (byes do not break a streak)
        private static int TeamStreakCost(List<Matchup> games, List<int> mine, int[] wk, int t)
        {
            var order = new List<int>(mine);
            order.Sort((x, y) => wk[x].CompareTo(wk[y]));
            int cost = 0, streak = 0;
            for (int i = 0; i < order.Count; i++)
            {
                bool home = games[order[i]].Home == t;
                streak = home ? (streak > 0 ? streak + 1 : 1) : (streak < 0 ? streak - 1 : -1);
                if (Math.Abs(streak) > WeekCtx.MaxStreak) cost += StreakPenalty * (Math.Abs(streak) - WeekCtx.MaxStreak);
            }
            return cost;
        }


        /// <summary>Per-week state the matching search uses to prefer pairings that avoid rematches and long streaks.</summary>
        private sealed class WeekCtx
        {
            public List<Matchup> Games;
            public int[] Avoid1, Avoid2;   // opponents a team should not meet this week (last week's, and next week's reserved finale opponent)
            public int[] Streak;           // + consecutive home games, - consecutive away games
            public const int MaxStreak = 3;
        }

        private const int RematchPenalty = 100, StreakPenalty = 10;

        // penalty of putting game gi in this week
        private static int StreakCost(WeekCtx ctx, int gi)
        {
            Matchup m = ctx.Games[gi];
            int h = ctx.Streak[m.Home] > 0 ? ctx.Streak[m.Home] + 1 : 1;
            int a = ctx.Streak[m.Away] < 0 ? -ctx.Streak[m.Away] + 1 : 1;
            return (h > WeekCtx.MaxStreak ? StreakPenalty * (h - WeekCtx.MaxStreak) : 0) + (a > WeekCtx.MaxStreak ? StreakPenalty * (a - WeekCtx.MaxStreak) : 0);
        }

        // cheapest way (over the remaining games of the pair) to schedule teams a and b this week
        private static int PairCost(WeekCtx ctx, List<int>[,] pg, int a, int b)
        {
            int cost = 0;
            if (ctx.Avoid1[a] == b || ctx.Avoid2[a] == b) cost += RematchPenalty;
            List<int> l = pg[Math.Min(a, b), Math.Max(a, b)];
            int best = int.MaxValue;
            for (int i = 0; i < l.Count; i++) best = Math.Min(best, StreakCost(ctx, l[i]));
            return cost + (best == int.MaxValue ? 0 : best);
        }

        private static bool TryProWeeks(List<Matchup> games, int teamCount, Rng rng, int[] divisionOf, out int[] weekOfGame, out int[] byeWeek, out int score)
        {
            const int weeks = 18;
            weekOfGame = new int[games.Count];
            byeWeek = new int[teamCount];
            score = 0;
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
            for (int a = 0; a < teamCount; a++)
                for (int b = 0; b < teamCount; b++)
                    if (pairGames[a, b] == null) pairGames[a, b] = new List<int>();

            var ctx = new WeekCtx { Games = games, Avoid1 = new int[teamCount], Avoid2 = new int[teamCount], Streak = new int[teamCount] };
            for (int t = 0; t < teamCount; t++) { ctx.Avoid1[t] = -1; ctx.Avoid2[t] = -1; }
            var finaleOpp = new int[teamCount];
            for (int t = 0; t < teamCount; t++) finaleOpp[t] = -1;

            int lastWeek = weeks;
            if (divisionOf != null && !AllDivisionsOfFour(divisionOf, teamCount)) divisionOf = null;   // a custom league without four-team divisions has no divisional finale
            if (divisionOf != null)
            {
                // the finale is divisional: reserve one game of two disjoint divisional pairs per division
                var byDiv = new Dictionary<int, List<int>>();
                for (int t = 0; t < teamCount; t++)
                {
                    if (divisionOf[t] < 0) continue;
                    List<int> l;
                    if (!byDiv.TryGetValue(divisionOf[t], out l)) { l = new List<int>(); byDiv[divisionOf[t]] = l; }
                    l.Add(t);
                }
                var divIds = new List<int>(byDiv.Keys);
                divIds.Sort();
                for (int d = 0; d < divIds.Count; d++)
                {
                    List<int> m = byDiv[divIds[d]];

                    var mm = new List<int>(m);
                    rng.Shuffle(mm);
                    for (int k = 0; k < 4; k += 2)
                    {
                        List<int> l = pairGames[Math.Min(mm[k], mm[k + 1]), Math.Max(mm[k], mm[k + 1])];
                        if (l.Count == 0) return false;
                        int pick = rng.Range(0, l.Count);
                        weekOfGame[l[pick]] = lastWeek;
                        l.RemoveAt(pick);
                        finaleOpp[mm[k]] = mm[k + 1]; finaleOpp[mm[k + 1]] = mm[k];
                    }
                }
                // the finale games also set each team's last home/away state, which the loop below does not need to know
            }
            else lastWeek = weeks + 1;   // no reservation: week 18 is matched like every other week

            var lastPartner = new int[teamCount];
            for (int t = 0; t < teamCount; t++) lastPartner[t] = -1;
            for (int w = 1; w < lastWeek; w++)
            {
                var avail = new List<int>();
                for (int t = 0; t < teamCount; t++) if (byeWeek[t] != w) avail.Add(t);
                for (int t = 0; t < teamCount; t++) { ctx.Avoid1[t] = lastPartner[t]; ctx.Avoid2[t] = (w == lastWeek - 1) ? finaleOpp[t] : -1; }
                List<int[]> match = null;
                for (int tries = 0; tries < 25 && match == null; tries++) match = PerfectMatching(avail, pairGames, rng, ctx);
                if (match == null) return false;
                for (int t = 0; t < teamCount; t++) lastPartner[t] = -1;
                for (int m = 0; m < match.Count; m++)
                {
                    int a = Math.Min(match[m][0], match[m][1]), b = Math.Max(match[m][0], match[m][1]);
                    List<int> l = pairGames[a, b];
                    // pick the orientation with the fewest streak violations (ties at random)
                    int bestCost = int.MaxValue; var bestIdx = new List<int>();
                    for (int k = 0; k < l.Count; k++)
                    {
                        int c = StreakCost(ctx, l[k]);
                        if (c < bestCost) { bestCost = c; bestIdx.Clear(); }
                        if (c == bestCost) bestIdx.Add(k);
                    }
                    int pick = bestIdx[rng.Range(0, bestIdx.Count)];
                    int gi = l[pick];
                    weekOfGame[gi] = w;
                    l.RemoveAt(pick);
                    score += bestCost;
                    if (ctx.Avoid1[a] == b || ctx.Avoid2[a] == b) score += RematchPenalty;
                    Matchup mu = games[gi];
                    ctx.Streak[mu.Home] = ctx.Streak[mu.Home] > 0 ? ctx.Streak[mu.Home] + 1 : 1;
                    ctx.Streak[mu.Away] = ctx.Streak[mu.Away] < 0 ? ctx.Streak[mu.Away] - 1 : -1;
                    lastPartner[a] = b; lastPartner[b] = a;
                }
            }
            for (int i = 0; i < weekOfGame.Length; i++) if (weekOfGame[i] == 0) return false;
            return true;
        }

        private static List<int[]> PerfectMatching(List<int> avail, List<int>[,] pairGames, Rng rng, WeekCtx ctx)
        {
            int n = avail.Count;
            var matched = new bool[n];
            var result = new List<int[]>();
            int budget = 40000;
            if (Search(avail, pairGames, matched, result, rng, 0, ref budget, ctx)) return result;
            return null;
        }

        private static int Count(List<int>[,] pg, int a, int b)
        {
            int x = Math.Min(a, b), y = Math.Max(a, b);
            return pg[x, y] == null ? 0 : pg[x, y].Count;
        }

        private static bool Search(List<int> avail, List<int>[,] pg, bool[] matched, List<int[]> result, Rng rng, int done, ref int budget, WeekCtx ctx)
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
            // prefer opponents that still have many games left against us (double meetings first), but pairings that would be a rematch
            // or stretch a home/away streak come last (stable sort keeps the shuffle as the tie-break)
            var cost = new Dictionary<int, int>();
            for (int k = 0; k < cands.Count; k++) cost[cands[k]] = ctx != null ? PairCost(ctx, pg, avail[best], avail[cands[k]]) : 0;
            var sorted = new List<int>(cands);
            int[] ord = new int[sorted.Count];
            for (int k = 0; k < ord.Length; k++) ord[k] = k;
            Array.Sort(ord, (x, y) =>
            {
                int c = cost[sorted[x]].CompareTo(cost[sorted[y]]);
                if (c != 0) return c;
                c = Count(pg, avail[best], avail[sorted[y]]).CompareTo(Count(pg, avail[best], avail[sorted[x]]));
                return c != 0 ? c : x.CompareTo(y);
            });
            for (int k = 0; k < ord.Length; k++)
            {
                int j = sorted[ord[k]];
                matched[best] = matched[j] = true;
                result.Add(new[] { avail[best], avail[j] });
                if (Search(avail, pg, matched, result, rng, done + 1, ref budget, ctx)) return true;
                result.RemoveAt(result.Count - 1);
                matched[best] = matched[j] = false;
                if (budget <= 0) return false;
            }
            return false;
        }

        private static bool AllDivisionsOfFour(int[] divisionOf, int teamCount)
        {
            var count = new Dictionary<int, int>();
            for (int t = 0; t < teamCount; t++)
            {
                if (divisionOf[t] < 0) return false;
                int c;
                count.TryGetValue(divisionOf[t], out c);
                count[divisionOf[t]] = c + 1;
            }
            foreach (var kv in count) if (kv.Value != 4) return false;
            return true;
        }

        // -------------------------------------------------------------- college
        public static List<Matchup> CollegeMatchups(CollegeWorld world, Rng rng)
        {
            var games = new List<Matchup>();
            int n = world.Schools.Count;
            var played = new HashSet<long>();
            var degree = new int[n];

            // QA-570: the membership order is stable (no shuffle), so the same programs are neighbours and rivals year after year; home dates are balanced
            // and flip every other season. Round-robin up to 10 members, otherwise two fixed rival strides, two strides that rotate with the year and the cross-conference "partner".
            bool flip = (world.Year & 1) == 1;
            for (int c = 0; c < world.Conferences.Count; c++)
            {
                List<int> members = new List<int>(world.Conferences[c].TeamIds);
                int m = members.Count;
                if (m <= 10)
                {
                    for (int i = 0; i < m; i++)
                        for (int j = i + 1; j < m; j++)
                        {
                            int d = j - i;
                            bool iHosts = d < m - d ? true : (d > m - d ? false : (i % 2 == 0));
                            AddEdge(games, played, degree, members[i], members[j], iHosts != flip, false);
                        }
                }
                else
                {
                    int half = (m - 1) / 2;
                    int poolSize = half - 2;   // strides 3..half rotate
                    int[] strides = { 1, 2, 3 + ((world.Year % poolSize) + poolSize) % poolSize, 3 + ((world.Year + 1) % poolSize + poolSize) % poolSize };
                    for (int i = 0; i < m; i++)
                        for (int k = 0; k < strides.Length; k++)
                            AddEdge(games, played, degree, members[i], members[(i + strides[k]) % m], !flip, false);
                    for (int i = 0; m % 2 == 0 && i < m / 2; i++)
                        AddEdge(games, played, degree, members[i], members[i + m / 2], ((i % 2) == 0) != flip, false);
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

        /// <summary>
        /// Places college games into weeks (12 games in 14 weeks, 2 byes). Several greedy layouts are built, stragglers are moved into any earlier week where
        /// both teams are idle, and the layout with no unplaced games, the fewest weeks, the evenest weeks and the fewest byeless teams wins (QA-571). Returns the week count.
        /// </summary>
        public static int AssignCollegeWeeks(List<Matchup> games, int teamCount, Rng rng, out int[] weekOfGame)
        {
            int[] best = null; int bestWeeks = 0; double bestScore = double.MaxValue;
            for (int attempt = 0; attempt < 24; attempt++)
            {
                int[] wk;
                int weeks = AssignCollegeWeeksOnce(games, teamCount, rng, out wk);
                weeks = BackfillCollegeWeeks(games, teamCount, wk, weeks);
                double score = CollegeLayoutScore(games, teamCount, wk, weeks);
                if (score < bestScore) { bestScore = score; best = wk; bestWeeks = weeks; }
                if (score < 1.0) break;
            }
            ImproveCollegeWeeks(games, teamCount, best, bestWeeks);
            int max = 0;
            for (int i = 0; i < best.Length; i++) if (best[i] > max) max = best[i];
            bestWeeks = max;
            weekOfGame = best;
            return bestWeeks;
        }

        private const int CollegeTargetWeeks = 14;

        // moves every unplaced game and every game beyond the target week into the earliest week where both teams are idle
        private static int BackfillCollegeWeeks(List<Matchup> games, int teamCount, int[] wk, int weeks)
        {
            int cols = Math.Max(weeks, CollegeTargetWeeks) + 2;
            var busy = new bool[teamCount, cols];
            for (int i = 0; i < games.Count; i++)
                if (wk[i] > 0 && wk[i] <= CollegeTargetWeeks) { busy[games[i].Home, wk[i]] = true; busy[games[i].Away, wk[i]] = true; }
            for (int i = 0; i < games.Count; i++)
            {
                if (wk[i] > 0 && wk[i] <= CollegeTargetWeeks) continue;
                for (int w = 1; w <= CollegeTargetWeeks; w++)
                {
                    if (busy[games[i].Home, w] || busy[games[i].Away, w]) continue;
                    wk[i] = w; busy[games[i].Home, w] = true; busy[games[i].Away, w] = true;
                    break;
                }
            }
            int max = 0;
            for (int i = 0; i < games.Count; i++) if (wk[i] > max) max = wk[i];
            return max;
        }

        // local search: moves single games into weeks where both teams are idle when that evens out the weeks or gives a team a bye inside its span
        private static void ImproveCollegeWeeks(List<Matchup> games, int teamCount, int[] wk, int weeks)
        {
            var busy = new bool[teamCount, weeks + 2];
            var perWeek = new int[weeks + 2];
            int placed = 0;
            for (int i = 0; i < games.Count; i++)
                if (wk[i] > 0) { busy[games[i].Home, wk[i]] = true; busy[games[i].Away, wk[i]] = true; perWeek[wk[i]]++; placed++; }
            double mean = placed / (double)Math.Max(1, weeks);
            for (int pass = 0; pass < 6; pass++)
            {
                bool moved = false;
                for (int i = 0; i < games.Count; i++)
                {
                    int w0 = wk[i];
                    if (w0 <= 0) continue;
                    int h = games[i].Home, a = games[i].Away;
                    double cur = ByelessCost(busy, h, weeks) + ByelessCost(busy, a, weeks);
                    double bestDelta = -1e-9; int bestW = 0;
                    for (int w = 1; w <= weeks; w++)
                    {
                        if (w == w0 || busy[h, w] || busy[a, w]) continue;
                        busy[h, w0] = busy[a, w0] = false; busy[h, w] = busy[a, w] = true;
                        double next = ByelessCost(busy, h, weeks) + ByelessCost(busy, a, weeks);
                        busy[h, w] = busy[a, w] = false; busy[h, w0] = busy[a, w0] = true;
                        double delta = next - cur + WeekSq(perWeek[w0] - 1, mean) + WeekSq(perWeek[w] + 1, mean) - WeekSq(perWeek[w0], mean) - WeekSq(perWeek[w], mean);
                        if (delta < bestDelta) { bestDelta = delta; bestW = w; }
                    }
                    if (bestW == 0) continue;
                    busy[h, w0] = busy[a, w0] = false; busy[h, bestW] = busy[a, bestW] = true;
                    perWeek[w0]--; perWeek[bestW]++; wk[i] = bestW;
                    moved = true;
                }
                if (!moved) break;
            }
        }

        private static double WeekSq(int count, double mean) { return (count - mean) * (count - mean) * 0.2; }

        // 25 when the team has no idle week between its first and last game
        private static double ByelessCost(bool[,] busy, int t, int weeks)
        {
            int first = 0, last = 0, played = 0;
            for (int w = 1; w <= weeks; w++)
                if (busy[t, w]) { if (played == 0) first = w; last = w; played++; }
            return played > 0 && last - first + 1 == played ? 25.0 : 0.0;
        }

        // lower is better: unplaced games dominate, then the number of weeks, the spread of games per week and teams without a bye inside their span
        private static double CollegeLayoutScore(List<Matchup> games, int teamCount, int[] wk, int weeks)
        {
            double score = 0;
            var perWeek = new int[weeks + 2];
            var first = new int[teamCount]; var last = new int[teamCount]; var played = new int[teamCount];
            for (int i = 0; i < games.Count; i++)
            {
                if (wk[i] <= 0) { score += 1000000.0; continue; }
                perWeek[wk[i]]++;
                int[] ts = { games[i].Home, games[i].Away };
                for (int k = 0; k < 2; k++)
                {
                    int t = ts[k];
                    if (played[t] == 0 || wk[i] < first[t]) first[t] = wk[i];
                    if (wk[i] > last[t]) last[t] = wk[i];
                    played[t]++;
                }
            }
            score += weeks * 1000.0;
            double mean = games.Count / (double)Math.Max(1, weeks);
            for (int w = 1; w <= weeks; w++) score += (perWeek[w] - mean) * (perWeek[w] - mean) * 0.2;
            for (int t = 0; t < teamCount; t++) if (played[t] > 0 && last[t] - first[t] + 1 == played[t]) score += 25.0;
            return score;
        }

        /// <summary>Places college games into weeks (12 games in as few weeks as practical, 2+ byes). Returns the week count.</summary>
        private static int AssignCollegeWeeksOnce(List<Matchup> games, int teamCount, Rng rng, out int[] weekOfGame)
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
