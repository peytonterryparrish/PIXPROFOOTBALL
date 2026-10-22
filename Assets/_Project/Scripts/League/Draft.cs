using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    [Serializable]
    public class DraftPickRecord
    {
        public int Year;
        public int Overall;
        public int Round;
        public int PickInRound;
        public int TeamId;
        public int PlayerId;
        public string PlayerName;
        public Position Position;
        public string College;
        /// <summary>Rank on the public board at the time of the pick (1 = best).</summary>
        public int ConsensusRank;
    }

    /// <summary>Produces each year's draft class.</summary>
    public static class ProspectGenerator
    {
        public const int DefaultClassSize = 340;

        /// <summary>
        /// Builds the class from college seniors plus juniors who declare. The players are the same objects as on the college
        /// rosters (they are removed from their schools by <see cref="CollegeFactory.AdvanceYear"/>).
        /// </summary>
        public static List<Prospect> FromCollege(CollegeWorld world, int year, Rng rng, int classSize = DefaultClassSize)
        {
            var pool = new List<Prospect>();
            var seniorGrades = new List<float>();
            for (int s = 0; s < world.Schools.Count; s++)
            {
                var ro = world.Schools[s].Team.Roster;
                for (int i = 0; i < ro.Count; i++)
                    if (ro[i].Class == ClassYear.Senior && !ro[i].IsRedshirting && !ro[i].Retired)
                        seniorGrades.Add(ScoutingReport.Grade(ro[i].Ratings.Overall(ro[i].Position), ro[i].Potential, ro[i].Bio.Age));
            }
            seniorGrades.Sort((a, b) => b.CompareTo(a));
            float declareThreshold = seniorGrades.Count > 0 ? seniorGrades[Math.Min(seniorGrades.Count - 1, (int)(classSize * 0.7f))] : 70f;
            for (int s = 0; s < world.Schools.Count; s++)
            {
                School school = world.Schools[s];
                var roster = school.Team.Roster;
                for (int i = 0; i < roster.Count; i++)
                {
                    Player p = roster[i];
                    if (p.IsRedshirting || p.Retired) continue;
                    bool senior = p.Class == ClassYear.Senior;
                    bool junior = p.Class == ClassYear.Junior;
                    if (!senior && !junior) continue;
                    float g = ScoutingReport.Grade(p.Ratings.Overall(p.Position), p.Potential, p.Bio.Age);
                    if (junior)
                    {
                        // only the best juniors declare
                        if (g < declareThreshold || !rng.Chance(0.75f)) continue;
                    }
                    pool.Add(new Prospect
                    {
                        Player = p, SchoolId = school.Id, SchoolName = school.ShortName, ClassYear = p.Class, DeclaredEarly = junior
                    });
                }
            }
            return Finish(pool, rng, classSize);
        }

        /// <summary>Synthetic class for leagues without a college world (talent curve shaped like a real class).</summary>
        public static List<Prospect> Standalone(IPlayerIdSource ids, int year, Rng rng, IList<string> colleges, int classSize = DefaultClassSize, HashSet<string> usedNames = null)
        {
            var used = usedNames ?? new HashSet<string>();   // QA-612: pass the league-wide names so a prospect never shares a name with a pro
            var pool = new List<Prospect>();
            for (int r = 0; r < classSize; r++)
            {
                float q = (float)r / classSize;
                int pot = (int)Math.Round(94f - 44f * (float)Math.Pow(q, 0.55) + rng.Gaussian(0f, 3.5f));   // QA-580: the top of a class reaches the mid-90s so stars get replaced (the old curve topped out near 86)
                Position pos = LeagueFactory.RandomRosterPosition(rng);
                int age = rng.Range(21, 24);
                Player p = PlayerFactory.Create(rng, ids, used, pos, pot, age);
                p.Experience = 0;
                string college = colleges != null && colleges.Count > 0 ? colleges[rng.Range(0, colleges.Count)] : "";
                p.Bio.College = college;
                p.Class = ClassYear.Senior;
                p.Stars = CollegeFactory.StarsFromPotential(pot, rng);
                pool.Add(new Prospect { Player = p, SchoolName = college, ClassYear = age <= 21 ? ClassYear.Junior : ClassYear.Senior, DeclaredEarly = age <= 21 });
            }
            return Finish(pool, rng, classSize);
        }

        private static List<Prospect> Finish(List<Prospect> pool, Rng rng, int classSize)
        {
            // best N by true grade; position-balance guard so every position group is represented
            var scored = new List<KeyValuePair<float, Prospect>>(pool.Count);
            for (int i = 0; i < pool.Count; i++) scored.Add(new KeyValuePair<float, Prospect>(pool[i].TrueGrade + rng.Gaussian(0f, 0.2f), pool[i]));
            scored.Sort((a, b) => b.Key.CompareTo(a.Key));
            var cls = new List<Prospect>();
            // QA-581: reserve a share of the class for every position group (best of each group first), then fill the rest by grade, so a class
            // is never 7 quarterbacks and 49 corners
            var inClass = new HashSet<Prospect>();
            var taken = new Dictionary<PositionBucket, int>();
            for (int i = 0; i < scored.Count && cls.Count < classSize; i++)
            {
                Prospect pr = scored[i].Value;
                PositionBucket b = PositionBuckets.Of(pr.Player.Position);
                int have; taken.TryGetValue(b, out have);
                if (have >= (int)(PositionBuckets.ProCount(b) * classSize / 53f * 0.7f)) continue;
                taken[b] = have + 1; cls.Add(pr); inClass.Add(pr);
            }
            for (int i = 0; i < scored.Count && cls.Count < classSize; i++)
                if (!inClass.Contains(scored[i].Value)) { cls.Add(scored[i].Value); inClass.Add(scored[i].Value); }

            // kickers and punters are rare in the top of the pool; make sure a handful exist
            EnsureSpecialists(pool, cls, Position.K, 6);
            EnsureSpecialists(pool, cls, Position.P, 6);

            // public consensus board: true grade plus public-opinion noise
            var consensus = new List<KeyValuePair<float, Prospect>>(cls.Count);
            for (int i = 0; i < cls.Count; i++)
                consensus.Add(new KeyValuePair<float, Prospect>(cls[i].TrueGrade + rng.Gaussian(0f, 4.5f), cls[i]));
            consensus.Sort((a, b) => b.Key.CompareTo(a.Key));
            for (int i = 0; i < consensus.Count; i++) consensus[i].Value.ConsensusRank = i + 1;
            cls.Sort((a, b) => a.ConsensusRank.CompareTo(b.ConsensusRank));
            return cls;
        }

        private static void EnsureSpecialists(List<Prospect> pool, List<Prospect> cls, Position pos, int min)
        {
            int have = 0;
            for (int i = 0; i < cls.Count; i++) if (cls[i].Player.Position == pos) have++;
            if (have >= min) return;
            var extra = new List<Prospect>();
            for (int i = 0; i < pool.Count; i++) if (pool[i].Player.Position == pos && !cls.Contains(pool[i])) extra.Add(pool[i]);
            extra.Sort((a, b) => b.TrueGrade.CompareTo(a.TrueGrade));
            for (int i = 0; i < extra.Count && have < min; i++) { cls.Add(extra[i]); have++; }
        }
    }

    /// <summary>Runs a seven-round draft with per-team scouting reports.</summary>
    public static class DraftEngine
    {
        private static readonly int[] CompPicks = { 0, 0, 0, 3, 5, 7, 9, 12 }; // index = round (1-based)

        public static int TotalPicks
        {
            get
            {
                int n = 32 * LeagueRules.DraftRounds;
                for (int r = 1; r <= LeagueRules.DraftRounds; r++) n += CompPicks[r];
                return n;
            }
        }

        public static List<ScoutingReport> BuildScouting(ProLeague league, IList<Prospect> cls, Rng rng, int year)
        {
            var reports = new List<ScoutingReport>();
            for (int t = 0; t < league.Teams.Count; t++)
            {
                Team team = league.Teams[t];
                Coach hc = team.HeadCoach;
                int budget = team.ScoutingPoints + (hc != null ? (hc.Scouting - 60) * 2 : 0);
                budget = Math.Max(80, budget);
                var rep = new ScoutingReport(team.Id, year, unchecked((long)rng.NextULong()), budget);
                // spend on the consensus top of the board: heavier on the very top
                int top = Math.Min(cls.Count, 120);
                float totalWeight = 0f;
                for (int i = 0; i < top; i++) totalWeight += 1.6f - 1.2f * i / top;
                for (int i = 0; i < top; i++)
                {
                    float w = (1.6f - 1.2f * i / top) / totalWeight;
                    rep.Reveal(cls[i], Math.Max(1, (int)Math.Round(budget * w)));
                }
                reports.Add(rep);
            }
            return reports;
        }

        /// <summary>Team order for the draft: worst record first, champion last.</summary>
        public static List<int> DraftOrder(ProLeague league)
        {
            if (league.Season != null && league.Season.Games.Count > 0) return league.Season.DraftOrder();
            var list = new List<Team>(league.Teams);
            list.Sort((a, b) => a.TeamOverall().CompareTo(b.TeamOverall()));
            var ids = new List<int>();
            for (int i = 0; i < list.Count; i++) ids.Add(list[i].Id);
            return ids;
        }

        private static float NeedBonus(Team team, Position pos)
        {
            PositionBucket b = PositionBuckets.Of(pos);
            int have = 0;
            float best = 0f;
            for (int i = 0; i < team.Roster.Count; i++)
            {
                Player p = team.Roster[i];
                if (PositionBuckets.Of(p.Position) != b) continue;
                have++;
                int o = p.Ratings.Overall(p.Position);
                if (o > best) best = o;
            }
            int target = PositionBuckets.ProCount(b);
            float bonus = (target - have) * 2.5f;
            if (bonus > 7f) bonus = 7f; else if (bonus < -7f) bonus = -7f;
            if (best > 0f && best < 72f) bonus += (72f - best) * 0.15f;
            return bonus;
        }

        public static List<DraftPickRecord> Run(ProLeague league, IList<Prospect> cls, IList<ScoutingReport> reports, Rng rng, int year)
        {
            var picks = new List<DraftPickRecord>();
            List<int> order = DraftOrder(league);
            var boards = new List<List<Prospect>>();
            for (int t = 0; t < league.Teams.Count; t++) boards.Add(reports[t].BigBoard(cls));
            var taken = new HashSet<int>();
            var jerseys = new List<HashSet<int>>();
            for (int t = 0; t < league.Teams.Count; t++)
            {
                var set = new HashSet<int>();
                for (int i = 0; i < league.Teams[t].Roster.Count; i++) set.Add(league.Teams[t].Roster[i].Bio.JerseyNumber);
                jerseys.Add(set);
            }

            int overall = 0;
            for (int round = 1; round <= LeagueRules.DraftRounds; round++)
            {
                var roundOrder = new List<int>(order);
                // compensatory picks at the end of the round go to a random subset of teams
                int comp = CompPicks[round];
                if (comp > 0)
                {
                    var ids = new List<int>(order);
                    rng.Shuffle(ids);
                    for (int i = 0; i < comp; i++) roundOrder.Add(ids[i]);
                }
                for (int pi = 0; pi < roundOrder.Count; pi++)
                {
                    overall++;
                    int teamId = roundOrder[pi];
                    Team team = league.Teams[teamId];
                    Prospect choice = ChoosePick(team, boards[teamId], taken, round, rng);
                    if (choice == null) break;
                    taken.Add(choice.Id);
                    choice.Drafted = true; choice.DraftedByTeamId = teamId; choice.OverallPick = overall;
                    Player p = choice.Player;
                    p.DraftYear = year; p.DraftRound = round; p.DraftPick = overall; p.Experience = 0;
                    if (choice.SchoolId >= 0) CollegeFactory.ApplyProTransition(p);   // a college prospect turns pro: ratings move back toward the pro age curve
                    p.Contract = ContractModel.RookieContract(overall, year);
                    p.Morale = 80;
                    p.Bio.JerseyNumber = LeagueFactory.PickJersey(p.Position, rng, jerseys[teamId]);
                    p.Class = ClassYear.Senior;
                    p.IsRedshirting = false;
                    p.Retired = false;
                    team.AddPlayer(p);
                    picks.Add(new DraftPickRecord
                    {
                        Year = year, Overall = overall, Round = round, PickInRound = pi + 1, TeamId = teamId, PlayerId = p.Id,
                        PlayerName = p.Name, Position = p.Position, College = p.Bio.College, ConsensusRank = choice.ConsensusRank
                    });
                }
            }
            return picks;
        }

        internal static Prospect ChoosePick(Team team, List<Prospect> board, HashSet<int> taken, int round, Rng rng)
        {
            Prospect best = null;
            float bestScore = float.MinValue;
            int considered = 0;
            for (int i = 0; i < board.Count && considered < 18; i++)
            {
                Prospect p = board[i];
                if (taken.Contains(p.Id)) continue;
                considered++;
                // board is sorted by estimated grade; reconstruct a score: rank-based grade with need
                float score = -i * 0.65f + NeedBonus(team, p.Player.Position) + rng.Gaussian(0f, 1.2f);
                if (PositionBuckets.Of(p.Player.Position) == PositionBucket.K || PositionBuckets.Of(p.Player.Position) == PositionBucket.P)
                    score -= round < 6 ? 35f : 4f;
                if (PositionBuckets.Of(p.Player.Position) == PositionBucket.FB) score -= 15f;
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }
    }
}
