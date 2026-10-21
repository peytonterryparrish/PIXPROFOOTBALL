using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    [Serializable]
    public class PollEntry
    {
        public int Rank;
        public int TeamId;
        public string Name = "";
        public int Points;
        public int FirstPlaceVotes;
        public int PrevRank;
        public string Record = "";
        public float Resume;
    }

    [Serializable]
    public class PollSnapshot
    {
        /// <summary>0 = preseason, otherwise the week just played.</summary>
        public int Week;
        public string Label = "";
        public List<PollEntry> Top25 = new List<PollEntry>();

        public PollEntry Find(int teamId)
        {
            for (int i = 0; i < Top25.Count; i++) if (Top25[i].TeamId == teamId) return Top25[i];
            return null;
        }
    }

    /// <summary>
    /// College polls: each of 62 voters ranks a ballot from the team's résumé (rating built from margins and strength of
    /// schedule, win percentage, quality wins) plus personal biases (brand, region) and noise; ballots are tallied 25..1.
    /// The playoff committee uses the same résumé without voter noise.
    /// </summary>
    public static class Rankings
    {
        public const int Voters = 62;

        public sealed class TeamResume
        {
            public int TeamId;
            public int W, L, T;
            public float Rating;
            public float Sos;
            public float Score;
            public int QualityWins;
            public int Games { get { return W + L + T; } }
        }

        private static float Prior(Season s, SeasonTeam st)
        {
            Team t = s.GetTeam(st.TeamId);
            float overall = t != null ? t.TeamOverall() : 65f;
            return (overall - 62f) * 1.2f + (st.Prestige - 3.5f) * 2.2f;
        }

        public static Dictionary<int, TeamResume> ComputeResumes(Season s, int throughWeek)
        {
            var res = new Dictionary<int, TeamResume>();
            var prior = new Dictionary<int, float>();
            for (int i = 0; i < s.Teams.Count; i++)
            {
                res[s.Teams[i].TeamId] = new TeamResume { TeamId = s.Teams[i].TeamId };
                prior[s.Teams[i].TeamId] = Prior(s, s.Teams[i]);
                res[s.Teams[i].TeamId].Rating = prior[s.Teams[i].TeamId];
            }
            var games = new List<ScheduledGame>();
            for (int i = 0; i < s.Games.Count; i++)
                if (s.Games[i].Played && s.Games[i].Week <= throughWeek) games.Add(s.Games[i]);

            for (int i = 0; i < games.Count; i++)
            {
                ScheduledGame g = games[i];
                TeamResume h = res[g.HomeId], a = res[g.AwayId];
                if (g.HomeScore > g.AwayScore) { h.W++; a.L++; }
                else if (g.AwayScore > g.HomeScore) { a.W++; h.L++; }
                else { h.T++; a.T++; }
            }
            // Massey-style iteration: rating = mean(opponent rating + capped margin), shrunk toward the prior
            var next = new Dictionary<int, float>();
            for (int iter = 0; iter < 12; iter++)
            {
                var sum = new Dictionary<int, float>();
                var cnt = new Dictionary<int, int>();
                for (int i = 0; i < games.Count; i++)
                {
                    ScheduledGame g = games[i];
                    float margin = Math.Max(-24, Math.Min(24, g.HomeScore - g.AwayScore)) * 0.55f;
                    float hadv = g.Neutral ? 0f : 1.2f;
                    Acc(sum, cnt, g.HomeId, res[g.AwayId].Rating + margin - hadv);
                    Acc(sum, cnt, g.AwayId, res[g.HomeId].Rating - margin + hadv);
                }
                next.Clear();
                foreach (var kv in res)
                {
                    int n = cnt.ContainsKey(kv.Key) ? cnt[kv.Key] : 0;
                    float total = sum.ContainsKey(kv.Key) ? sum[kv.Key] : 0f;
                    float pw = 2.2f;
                    next[kv.Key] = (pw * prior[kv.Key] + total) / (pw + n);
                }
                foreach (var kv in next) res[kv.Key].Rating = kv.Value;
            }
            // strength of schedule and quality wins
            float median = Median(res);
            foreach (var kv in res) { kv.Value.Sos = 0f; }
            var oppCount = new Dictionary<int, int>();
            for (int i = 0; i < games.Count; i++)
            {
                ScheduledGame g = games[i];
                res[g.HomeId].Sos += res[g.AwayId].Rating; res[g.AwayId].Sos += res[g.HomeId].Rating;
                Inc(oppCount, g.HomeId); Inc(oppCount, g.AwayId);
                if (g.HomeScore > g.AwayScore && res[g.AwayId].Rating > median + 3f) res[g.HomeId].QualityWins++;
                if (g.AwayScore > g.HomeScore && res[g.HomeId].Rating > median + 3f) res[g.AwayId].QualityWins++;
            }
            foreach (var kv in res)
            {
                TeamResume r = kv.Value;
                int n = oppCount.ContainsKey(kv.Key) ? oppCount[kv.Key] : 0;
                r.Sos = n > 0 ? r.Sos / n : 0f;
                float g = r.Games;
                float winPct = g > 0 ? (r.W + 0.5f * r.T) / g : 0.5f;
                float conf = g / (g + 3f); // résumés count for more as the season goes on
                r.Score = 0.5f * r.Rating + conf * (52f * (winPct - 0.5f) + 0.25f * (r.Sos - median) + 1.1f * r.QualityWins);
            }
            return res;
        }

        private static void Acc(Dictionary<int, float> sum, Dictionary<int, int> cnt, int id, float v)
        {
            if (!sum.ContainsKey(id)) { sum[id] = 0f; cnt[id] = 0; }
            sum[id] += v; cnt[id]++;
        }

        private static void Inc(Dictionary<int, int> d, int id) { d[id] = (d.ContainsKey(id) ? d[id] : 0) + 1; }

        private static float Median(Dictionary<int, TeamResume> res)
        {
            var l = new List<float>();
            foreach (var kv in res) l.Add(kv.Value.Rating);
            l.Sort();
            return l.Count == 0 ? 0f : l[l.Count / 2];
        }

        /// <summary>Selection-committee style ranking: résumé score, no voter noise. Best first.</summary>
        public static List<int> CommitteeRanking(Season s, int throughWeek = 999)
        {
            var res = ComputeResumes(s, throughWeek);
            var list = new List<TeamResume>(res.Values);
            list.Sort((a, b) =>
            {
                int c = b.Score.CompareTo(a.Score);
                return c != 0 ? c : a.TeamId.CompareTo(b.TeamId);
            });
            var ids = new List<int>(list.Count);
            for (int i = 0; i < list.Count; i++) ids.Add(list[i].TeamId);
            return ids;
        }

        public static PollSnapshot Poll(Season s, int week, string label, Rng rng, PollSnapshot previous)
        {
            var res = ComputeResumes(s, week);
            var teams = new List<TeamResume>(res.Values);
            teams.Sort((a, b) => a.TeamId.CompareTo(b.TeamId));
            var points = new Dictionary<int, int>();
            var firsts = new Dictionary<int, int>();
            // ballots
            var score = new float[teams.Count];
            var order = new int[teams.Count];
            for (int v = 0; v < Voters; v++)
            {
                Rng vr = rng.Fork();
                float brand = Math.Max(0f, vr.Gaussian(0.6f, 0.45f));
                int favRegion = vr.Range(0, 8);
                float regionBias = vr.Range(0.5f, 2.2f);
                float sd = 1.6f + 1.6f * vr.NextFloat();
                for (int i = 0; i < teams.Count; i++)
                {
                    SeasonTeam st = s.Info(teams[i].TeamId);
                    float sc = teams[i].Score + brand * (st.Prestige - 3.5f) + (st.Region == favRegion ? regionBias : 0f) + vr.Gaussian(0f, sd);
                    // voters love unbeaten teams and punish losses a little extra
                    if (teams[i].L == 0 && teams[i].Games >= 3) sc += 1.5f;
                    score[i] = sc; order[i] = i;
                }
                Array.Sort(order, (x, y) => score[y].CompareTo(score[x]));
                for (int r = 0; r < 25; r++)
                {
                    int id = teams[order[r]].TeamId;
                    points[id] = (points.ContainsKey(id) ? points[id] : 0) + (25 - r);
                    if (r == 0) firsts[id] = (firsts.ContainsKey(id) ? firsts[id] : 0) + 1;
                }
            }
            var entries = new List<PollEntry>();
            foreach (var kv in points)
            {
                TeamResume r = res[kv.Key];
                entries.Add(new PollEntry
                {
                    TeamId = kv.Key, Name = s.NameOf(kv.Key), Points = kv.Value,
                    FirstPlaceVotes = firsts.ContainsKey(kv.Key) ? firsts[kv.Key] : 0,
                    Record = r.W + "-" + r.L + (r.T > 0 ? "-" + r.T : ""), Resume = r.Score
                });
            }
            entries.Sort((a, b) =>
            {
                int c = b.Points.CompareTo(a.Points);
                if (c != 0) return c;
                c = b.Resume.CompareTo(a.Resume);
                return c != 0 ? c : a.TeamId.CompareTo(b.TeamId);
            });
            var snap = new PollSnapshot { Week = week, Label = label };
            for (int i = 0; i < entries.Count && i < 25; i++)
            {
                entries[i].Rank = i + 1;
                PollEntry prev = previous != null ? previous.Find(entries[i].TeamId) : null;
                entries[i].PrevRank = prev != null ? prev.Rank : 0;
                snap.Top25.Add(entries[i]);
            }
            return snap;
        }
    }
}
