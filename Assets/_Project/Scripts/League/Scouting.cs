using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>A draft-eligible player. The true ratings live in <see cref="Player"/>; teams only see scouted estimates.</summary>
    [Serializable]
    public class Prospect
    {
        public Player Player;
        public int SchoolId = -1;
        public string SchoolName = "";
        public ClassYear ClassYear = ClassYear.Senior;
        public bool DeclaredEarly;
        /// <summary>Public mock-draft rank (1 = best) from the generic scouting service.</summary>
        public int ConsensusRank;
        public bool Drafted;
        public int DraftedByTeamId = -1;
        public int OverallPick;
        /// <summary>College honors of his last season (v0.9.0), e.g. "2026 Golden Helm Trophy".</summary>
        public List<string> Honors = new List<string>();
        /// <summary>Combine / pro day numbers (v0.9.0); <see cref="CombineResult.Done"/> is false until they were measured.</summary>
        public CombineResult Combine = new CombineResult();

        public int Id { get { return Player.Id; } }

        /// <summary>The player's true value as a blend of current overall and ceiling (hidden from teams).</summary>
        public float TrueGrade
        {
            get { return ScoutingReport.Grade(Player.Ratings.Overall(Player.Position), Player.Potential, Player.Bio.Age); }
        }
    }

    [Serializable]
    public class ScoutedProspect
    {
        public int ProspectId;
        /// <summary>Estimated ratings (precision-weighted mean of all observations).</summary>
        public Ratings Estimated;
        public float EstimatedPotential;
        /// <summary>Total precision (1 / sd^2) of the estimate; grows with scouting points.</summary>
        public float Precision;
        public int PointsSpent;
        public int Observations;

        public float Sd { get { return Precision <= 0f ? 99f : (float)(1.0 / Math.Sqrt(Precision)); } }
    }

    /// <summary>
    /// A team's view of the draft class. Every estimate starts as a blurry glance (sd ~9 rating points) and sharpens
    /// as the team spends scouting points via <see cref="Reveal"/>: estimate sd = 9 / sqrt(1 + points / 2).
    /// </summary>
    [Serializable]
    public class ScoutingReport
    {
        public const float BaseSd = 9f;
        public const float PointsPerPrecisionUnit = 2f;

        public int TeamId;
        public int Year;
        public long Seed;
        public int PointsBudget;
        public int PointsSpent;
        public List<ScoutedProspect> Entries = new List<ScoutedProspect>();

        [NonSerialized] private Dictionary<int, ScoutedProspect> _lookup;

        public ScoutingReport() { }

        public ScoutingReport(int teamId, int year, long seed, int budget)
        {
            TeamId = teamId; Year = year; Seed = seed; PointsBudget = budget;
        }

        public int PointsRemaining { get { return Math.Max(0, PointsBudget - PointsSpent); } }

        /// <summary>Standard deviation of observation noise for a given amount of total points.</summary>
        public static float SdForPoints(int points)
        {
            return (float)(BaseSd / Math.Sqrt(1.0 + points / PointsPerPrecisionUnit));
        }

        /// <summary>Draft grade blend used everywhere (true and estimated): 55% current overall, 45% ceiling, small youth bonus.</summary>
        public static float Grade(float overall, float potential, int age)
        {
            return 0.55f * overall + 0.45f * potential + Math.Max(0, 23 - age) * 0.8f;
        }

        private ScoutedProspect Find(int prospectId)
        {
            if (_lookup == null || _lookup.Count != Entries.Count)
            {
                _lookup = new Dictionary<int, ScoutedProspect>(Entries.Count);
                for (int i = 0; i < Entries.Count; i++) _lookup[Entries[i].ProspectId] = Entries[i];
            }
            ScoutedProspect e;
            return _lookup.TryGetValue(prospectId, out e) ? e : null;
        }

        private Rng ObsRng(int prospectId, int obsIndex)
        {
            ulong h = unchecked((ulong)Seed) ^ 0x9E3779B97F4A7C15UL;
            h = unchecked(h * 0xBF58476D1CE4E5B9UL + (ulong)(uint)prospectId * 0x94D049BB133111EBUL);
            h = unchecked(h ^ (h >> 29) ^ ((ulong)(uint)obsIndex * 0xD6E8FEB86659FD93UL));
            return new Rng(h);
        }

        private void Observe(ScoutedProspect e, Prospect p, float sdObs, bool first)
        {
            Rng r = ObsRng(p.Id, e.Observations);
            float common = r.Gaussian(0f, sdObs * 0.8f);
            float precObs = 1f / (sdObs * sdObs);
            Ratings truth = p.Player.Ratings;
            Ratings est = e.Estimated;
            for (int k = 0; k < Ratings.KindCount; k++)
            {
                var kind = (RatingKind)k;
                float obs = truth.Get(kind) + common + r.Gaussian(0f, sdObs * 0.6f);
                float value = first ? obs : (e.Estimated.Get(kind) * e.Precision + obs * precObs) / (e.Precision + precObs);
                est.Set(kind, (int)Math.Round(value));
            }
            float obsPot = p.Player.Potential + r.Gaussian(0f, sdObs * 1.3f);
            e.EstimatedPotential = first ? obsPot : (e.EstimatedPotential * e.Precision + obsPot * precObs) / (e.Precision + precObs);
            e.Estimated = est;
            e.Precision = first ? precObs : e.Precision + precObs;
            e.Observations++;
        }

        /// <summary>The team's current estimate; creates the baseline glance on first use (free).</summary>
        public ScoutedProspect Get(Prospect p)
        {
            ScoutedProspect e = Find(p.Id);
            if (e != null) return e;
            e = new ScoutedProspect { ProspectId = p.Id };
            Observe(e, p, BaseSd, true);
            Entries.Add(e);
            if (_lookup != null) _lookup[e.ProspectId] = e;
            return e;
        }

        /// <summary>
        /// Spends up to <paramref name="points"/> scouting points on a prospect (limited by the remaining budget)
        /// and sharpens the estimate. Returns the updated entry.
        /// </summary>
        public ScoutedProspect Reveal(Prospect p, int points)
        {
            ScoutedProspect e = Get(p);
            points = Math.Min(points, PointsRemaining);
            if (points <= 0) return e;
            PointsSpent += points;
            e.PointsSpent += points;
            float sdObs = (float)(BaseSd / Math.Sqrt(points / PointsPerPrecisionUnit));
            Observe(e, p, sdObs, false);
            return e;
        }

        public float EstimatedOverall(Prospect p)
        {
            ScoutedProspect e = Get(p);
            return e.Estimated.Overall(p.Player.Position);
        }

        public float EstimatedGrade(Prospect p)
        {
            ScoutedProspect e = Get(p);
            return Grade(e.Estimated.Overall(p.Player.Position), e.EstimatedPotential, p.Player.Bio.Age);
        }

        /// <summary>Prospects ordered best-first by this team's estimated grade.</summary>
        public List<Prospect> BigBoard(IList<Prospect> pool)
        {
            var list = new List<Prospect>(pool);
            var grade = new Dictionary<int, float>(list.Count);
            for (int i = 0; i < list.Count; i++) grade[list[i].Id] = EstimatedGrade(list[i]);
            list.Sort((a, b) => grade[b.Id].CompareTo(grade[a.Id]));
            return list;
        }
    }
}
