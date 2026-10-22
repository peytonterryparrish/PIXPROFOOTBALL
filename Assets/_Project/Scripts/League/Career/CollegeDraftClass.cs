using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>
    /// The draft class from the college world: every senior plus the juniors who declare (the better the public thinks of a junior, the likelier he comes out),
    /// the best ~260 by true grade with a share for every position group, with their college stats, honors and combine numbers attached.
    /// </summary>
    public static class CollegeDraftClass
    {
        public const int ClassSize = 260;
        /// <summary>Prospects up to this public rank are invited to the combine; the rest rely on pro days.</summary>
        public const int CombineInvites = 210;

        /// <summary>Chance that a junior with this public rank among all draft-eligible players declares for the draft.</summary>
        public static float DeclareChance(int publicRank)
        {
            if (publicRank <= 32) return 0.96f;
            if (publicRank <= 64) return 0.90f;
            if (publicRank <= 120) return 0.78f;
            if (publicRank <= 200) return 0.55f;
            if (publicRank <= 320) return 0.28f;
            return 0.04f;
        }

        /// <summary>
        /// Builds the class and the scouting reports of all 32 teams (CPU teams from their own scouts, the user from his college-season knowledge plus the offseason allowance).
        /// The players are the same objects as on the college rosters; <see cref="CollegeFactory.AdvanceYear"/> removes them after the draft.
        /// </summary>
        public static void Prepare(CareerSave s, Rng rng)
        {
            CollegeNet.Ensure(s);
            CollegeWorld w = s.College;
            int year = s.Year;
            PoolCache pool = CollegeScouting.Pool(s);
            Rng dr = CollegeNet.NetRng(s, 9010, year);
            var picked = new List<Prospect>();
            for (int i = 0; i < pool.List.Count; i++)
            {
                PoolEntry e = pool.List[i];
                bool senior = e.P.Class == ClassYear.Senior;
                if (!senior && !dr.Chance(DeclareChance(e.Rank))) continue;
                picked.Add(new Prospect { Player = e.P, SchoolId = e.S.Id, SchoolName = e.S.ShortName, ClassYear = e.P.Class, DeclaredEarly = !senior });
            }
            List<Prospect> cls = ProspectGenerator.Finish(picked, rng.Fork(), ClassSize);

            // the public board is the same opinion that ranked them all season
            var pub = new Dictionary<int, float>();
            for (int i = 0; i < cls.Count; i++) pub[cls[i].Id] = pool.ById[cls[i].Id].Public;
            cls.Sort((a, b) => { int c = pub[b.Id].CompareTo(pub[a.Id]); return c != 0 ? c : a.Id.CompareTo(b.Id); });
            for (int i = 0; i < cls.Count; i++) cls[i].ConsensusRank = i + 1;

            // combine numbers, honors, college stats
            Rng cr = CollegeNet.NetRng(s, 9011, year);
            for (int i = 0; i < cls.Count; i++)
            {
                Prospect pr = cls[i];
                Player p = pr.Player;
                pr.Honors = HonorStrings(s, p.Id);
                p.Injury.Heal(30);   // the months since the last game: a college season-ending injury is mostly rehabbed by the time he turns pro
                float skip = i < 20 ? 0.25f : 0.04f;
                if (i + 1 > CombineInvites) pr.Combine = new CombineResult();
                else if (cr.Chance(skip)) pr.Combine = new CombineResult { Skipped = true };
                else pr.Combine = MeasureCombine(p, cr, false);
                // the college seasons stay with the player but out of his pro career
                if (p.Career.Count > 0) { p.CollegeCareer.AddRange(p.Career); p.Career.Clear(); }
            }
            s.Pro.DraftClass = cls;

            // reports
            var reports = CollegeScouting.BuildCpuReports(s, cls, rng.Fork(), year);
            CollegeNetState n = s.Net;
            ScoutingReport user = n.HasUserReport ? n.UserReport : new ScoutingReport(s.UserTeamId, year, unchecked((long)rng.NextULong()), 0);
            user.TeamId = s.UserTeamId; user.Year = year;
            int allowance = CollegeScouting.ScoutingPhaseAllowance;
            var scouts = CollegeScouting.TeamScouts(s, s.UserTeamId);
            for (int i = 0; i < scouts.Count; i++) if (scouts[i].Director) allowance += Math.Max(0, scouts[i].Skill - 55);
            user.PointsBudget += allowance;
            var ids = new HashSet<int>();
            for (int i = 0; i < cls.Count; i++) ids.Add(cls[i].Id);
            user.Entries.RemoveAll(e => !ids.Contains(e.ProspectId));
            reports[s.UserTeamId] = user;
            s.Pro.ScoutingReports = reports;
            n.HasUserReport = false;
            n.UserReport = new ScoutingReport();
            n.PhysicalsKnown.Clear();
            n.CombineAttended = false;
            n.ProDaySchools.Clear();
            n.PoolCacheObj = null;
            CollegeScouting.PruneBoard(s);
        }

        private static List<string> HonorStrings(CareerSave s, int playerId)
        {
            var l = new List<string>();
            var hs = CollegeHonors.HonorsOf(s, playerId);
            for (int i = 0; i < hs.Count; i++) l.Add(CollegeHonors.HonorText(s, hs[i]));
            return l;
        }

        // ================================================================ combine numbers
        /// <summary>The 40, bench, vertical ... of a player: his physical ratings plus a little noise. A pro day is a touch kinder than the combine's electronic timing.</summary>
        public static CombineResult MeasureCombine(Player p, Rng rng, bool proDay)
        {
            Ratings r = p.Ratings;
            float weightAdj = (p.Bio.WeightLb - 230) * 0.0009f;
            var c = new CombineResult { Done = true, ProDay = proDay };
            float kind = proDay ? -0.02f : 0f;
            c.Forty = Round2(5.45f - (r.Speed - 40) * 0.0215f + weightAdj + rng.Gaussian(0f, 0.035f) + kind);
            c.TenYard = Round2(1.72f - (r.Acceleration - 40) * 0.0045f + weightAdj * 0.3f + rng.Gaussian(0f, 0.015f) + kind * 0.3f);
            c.ThreeCone = Round2(7.6f - (r.Agility - 40) * 0.019f + weightAdj * 0.5f + rng.Gaussian(0f, 0.05f) + kind);
            c.Shuttle = Round2(4.75f - (r.Agility - 40) * 0.0105f + weightAdj * 0.3f + rng.Gaussian(0f, 0.03f) + kind);
            c.BenchReps = Math.Max(2, (int)Math.Round(6f + (r.Strength - 40) * 0.5f + (p.Bio.WeightLb - 230) * 0.03f + rng.Gaussian(0f, 2f)));
            c.VerticalIn = Math.Max(20, (int)Math.Round(24f + (r.Jumping - 40) * 0.28f - (p.Bio.WeightLb - 230) * 0.015f + rng.Gaussian(0f, 1.2f)));
            c.BroadJumpIn = Math.Max(80, (int)Math.Round(90f + (r.Jumping - 40) * 0.55f + (r.Speed - 40) * 0.15f + rng.Gaussian(0f, 3f)));
            return c;
        }

        private static float Round2(float v) { return (float)Math.Round(v, 2); }
    }
}
