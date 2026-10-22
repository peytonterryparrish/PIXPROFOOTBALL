using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>A draft-eligible college player in the scouting pool (a junior or senior who is not redshirting) with the public opinion of him.</summary>
    internal sealed class PoolEntry
    {
        public Player P;
        public School S;
        public float Public;
        public int Rank;
    }

    internal sealed class PoolCache
    {
        public int Year;
        public List<PoolEntry> List = new List<PoolEntry>();
        public Dictionary<int, PoolEntry> ById = new Dictionary<int, PoolEntry>();
    }

    /// <summary>One player the scouting screens can act on: a college junior/senior now, or a member of the draft class once it exists.</summary>
    public sealed class ScoutTarget
    {
        public Player Player;
        public School School;
        /// <summary>The real draft prospect when the class exists, otherwise a stand-in (same player, same id).</summary>
        public Prospect Prospect;
        public int PublicRank;
        public bool InClass;
    }

    /// <summary>
    /// Scouting for the College Network. Every pro team has 3-5 scouts (region and position skill); the user spends weekly scouting points himself and
    /// his assigned scouts quietly sharpen the ratings of the best players of their region or school every week. Knowledge is private to each team:
    /// the user's lives in a <see cref="ScoutingReport"/>, the CPU teams' reports are built from their own scouts when the draft class is made.
    /// </summary>
    public static class CollegeScouting
    {
        // ---- tiers: cumulative scouting points spent on one player
        public const int BandPoints = 6;
        public const int RangePoints = 20;
        public const int ExactPoints = 60;

        // ---- costs and budgets
        public const int GameCost = 15;
        public const int GameBonusPerPlayer = 6;
        public const int GameBonusPlayers = 8;
        public const int CombineCost = 30;
        public const int ProDayCost = 20;
        public const int ScoutingPhaseAllowance = 250;
        public const int WalletCap = 450;
        public const int ManualWeeklyBase = 10;
        public const int StartingPoints = 40;
        public const int MinScouts = 3;
        public const int MaxScouts = 5;

        // ================================================================ scouts
        public static void GenerateScouts(CareerSave s)
        {
            CollegeNetState n = s.Net;
            n.Scouts.Clear(); n.ScoutPool.Clear();
            var used = CareerFactory.UsedNames(s, false);
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                Rng rng = CollegeNet.NetRng(s, 9001, t, s.College != null ? s.College.Year : 0);
                int count = t == s.UserTeamId ? 4 : MinScouts + rng.Range(0, 3);
                for (int i = 0; i < count; i++) n.Scouts.Add(NewScout(s, rng, t, i == 0, used));
            }
            RefreshMarket(s);
        }

        private static Scout NewScout(CareerSave s, Rng rng, int teamId, bool director, HashSet<string> used)
        {
            string first, last;
            NameGenerator.PersonName(rng, used, out first, out last);
            int skill = (int)Math.Round(rng.Gaussian(director ? 70f : 63f, 9f));
            skill = Math.Max(38, Math.Min(95, skill));
            int age = rng.Range(31, 63);
            var sc = new Scout
            {
                Id = s.Net.NextScoutId++, Name = first + " " + last, TeamId = teamId, Age = age, Skill = skill, HomeRegion = rng.Range(0, 8),
                Experience = Math.Max(0, age - 27 - rng.Range(0, 8)), Director = director, FocusBucket = -1, Salary = 70000L + skill * 2800L
            };
            if (!director && rng.Chance(0.6f)) sc.FocusBucket = (int)new[] { PositionBucket.QB, PositionBucket.RB, PositionBucket.WR, PositionBucket.TE, PositionBucket.OL, PositionBucket.DL, PositionBucket.LB, PositionBucket.CB, PositionBucket.S }[rng.Range(0, 9)];
            sc.AssignRegion = director ? -1 : sc.HomeRegion;
            return sc;
        }

        private static void RefreshMarket(CareerSave s)
        {
            CollegeNetState n = s.Net;
            n.ScoutPool.Clear();
            var used = CareerFactory.UsedNames(s, false);
            for (int i = 0; i < n.Scouts.Count; i++) used.Add(n.Scouts[i].Name);
            Rng rng = CollegeNet.NetRng(s, 9002, s.College != null ? s.College.Year : 0);
            for (int i = 0; i < 8; i++) n.ScoutPool.Add(NewScout(s, rng, -1, false, used));
        }

        public static List<Scout> TeamScouts(CareerSave s, int teamId)
        {
            var l = new List<Scout>();
            for (int i = 0; i < s.Net.Scouts.Count; i++) if (s.Net.Scouts[i].TeamId == teamId) l.Add(s.Net.Scouts[i]);
            return l;
        }

        public static List<Scout> UserScouts(CareerSave s) { CollegeNet.Ensure(s); return TeamScouts(s, s.UserTeamId); }

        public static List<Scout> Market(CareerSave s) { CollegeNet.Ensure(s); return new List<Scout>(s.Net.ScoutPool); }

        public static Scout FindScout(CareerSave s, int id)
        {
            for (int i = 0; i < s.Net.Scouts.Count; i++) if (s.Net.Scouts[i].Id == id) return s.Net.Scouts[i];
            for (int i = 0; i < s.Net.ScoutPool.Count; i++) if (s.Net.ScoutPool[i].Id == id) return s.Net.ScoutPool[i];
            return null;
        }

        /// <summary>Sends one of the user's scouts to a school (<paramref name="schoolId"/> >= 0) or a region (<paramref name="region"/> >= 0) and optionally focuses him on a position group (a <see cref="PositionBucket"/> index, -1 for any).</summary>
        public static OpResult AssignScout(CareerSave s, int scoutId, int schoolId, int region, int focusBucket)
        {
            CollegeNet.Ensure(s);
            Scout sc = FindScout(s, scoutId);
            if (sc == null || sc.TeamId != s.UserTeamId) return OpResult.Fail("That is not one of your scouts.");
            if (sc.Director) return OpResult.Fail("The director runs the department and does not scout on his own.");
            if (schoolId >= 0 && (s.College == null || s.College.GetSchool(schoolId) == null)) return OpResult.Fail("Unknown school.");
            if (focusBucket >= PositionBuckets.All.Length) return OpResult.Fail("Unknown position group.");
            sc.AssignSchoolId = schoolId; sc.AssignRegion = schoolId >= 0 ? -1 : region; sc.FocusBucket = focusBucket;
            string where = schoolId >= 0 ? s.College.GetSchool(schoolId).Name : (region >= 0 ? ((Region)region).ToString() : ((Region)sc.HomeRegion).ToString());
            return OpResult.Success(sc.Name + " now covers " + where + (focusBucket >= 0 ? " (" + (PositionBucket)focusBucket + ")" : "") + ".");
        }

        public static OpResult HireScout(CareerSave s, int scoutId)
        {
            CollegeNet.Ensure(s);
            Scout sc = null; int idx = -1;
            for (int i = 0; i < s.Net.ScoutPool.Count; i++) if (s.Net.ScoutPool[i].Id == scoutId) { sc = s.Net.ScoutPool[i]; idx = i; }
            if (sc == null) return OpResult.Fail("That scout is not on the market.");
            if (TeamScouts(s, s.UserTeamId).Count >= MaxScouts) return OpResult.Fail("The department is full (" + MaxScouts + " scouts): let one go first.");
            s.Net.ScoutPool.RemoveAt(idx);
            sc.TeamId = s.UserTeamId; sc.AssignRegion = sc.HomeRegion; sc.AssignSchoolId = -1;
            s.Net.Scouts.Add(sc);
            return OpResult.Success(sc.Name + " joins the scouting department.");
        }

        public static OpResult FireScout(CareerSave s, int scoutId)
        {
            CollegeNet.Ensure(s);
            Scout sc = FindScout(s, scoutId);
            if (sc == null || sc.TeamId != s.UserTeamId) return OpResult.Fail("That is not one of your scouts.");
            if (sc.Director) return OpResult.Fail("The director of scouting cannot be let go.");
            if (TeamScouts(s, s.UserTeamId).Count <= MinScouts) return OpResult.Fail("A department needs at least " + MinScouts + " scouts.");
            s.Net.Scouts.Remove(sc);
            sc.TeamId = -1;
            s.Net.ScoutPool.Add(sc);
            return OpResult.Success(sc.Name + " leaves the department.");
        }

        /// <summary>Scouts age, the oldest retire, skill drifts, the departments are topped up to 3-5 and the market is refreshed (once a year after the draft).</summary>
        internal static void NewYear(CareerSave s)
        {
            CollegeNetState n = s.Net;
            Rng rng = CollegeNet.NetRng(s, 9003, s.College != null ? s.College.Year : 0);
            var used = CareerFactory.UsedNames(s, false);
            for (int i = 0; i < n.Scouts.Count; i++) used.Add(n.Scouts[i].Name);
            for (int i = n.Scouts.Count - 1; i >= 0; i--)
            {
                Scout sc = n.Scouts[i];
                sc.Age++; sc.Experience++; sc.PointsThisSeason = 0;
                sc.Skill = Math.Max(38, Math.Min(95, sc.Skill + rng.Range(-2, 4) + (sc.Experience < 5 ? 1 : 0) - (sc.Age > 58 ? 1 : 0)));
                bool retire = sc.Age >= 62 && rng.Chance(0.35f) || sc.Age >= 66;
                if (!retire) continue;
                if (sc.TeamId == s.UserTeamId) NewsWriter.Post(s, NewsKind.College, sc.Name + " retires from scouting", "Your scout " + sc.Name + (sc.Director ? ", the director of scouting," : "") + " is retiring after " + sc.Experience + " years in the game.", 2, s.UserTeamId);
                bool wasDirector = sc.Director;
                n.Scouts.RemoveAt(i);
                var rep = NewScout(s, rng, sc.TeamId, wasDirector, used);
                n.Scouts.Add(rep);
            }
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                var team = TeamScouts(s, t);
                if (team.Count == 0) n.Scouts.Add(NewScout(s, rng, t, true, used));
                else if (t != s.UserTeamId && team.Count < MaxScouts && rng.Chance(0.12f)) n.Scouts.Add(NewScout(s, rng, t, false, used));
            }
            RefreshMarket(s);
        }

        // ================================================================ the user's report and points
        /// <summary>Starts the user's scouting knowledge for the new college year.</summary>
        internal static void NewReport(CareerSave s)
        {
            CollegeNetState n = s.Net;
            int year = s.College != null ? s.College.Year : s.Year;
            Rng rng = CollegeNet.NetRng(s, 9004, year);
            n.UserReport = new ScoutingReport(s.UserTeamId, year, unchecked((long)rng.NextULong()), StartingPoints);
            n.HasUserReport = true;
        }

        /// <summary>The report the user is working with: his own college-season knowledge until the combine, then the one in the pro league's scouting reports.</summary>
        public static ScoutingReport UserReport(CareerSave s)
        {
            CollegeNet.Ensure(s);
            var reps = s.Pro.ScoutingReports;
            for (int i = 0; i < reps.Count; i++) if (reps[i].TeamId == s.UserTeamId) return reps[i];
            if (s.College == null) return null;
            if (!s.Net.HasUserReport) NewReport(s);
            return s.Net.UserReport;
        }

        public static int PointsRemaining(CareerSave s) { ScoutingReport r = UserReport(s); return r == null ? 0 : r.PointsRemaining; }

        /// <summary>Points the general manager gets to spend himself each week: a base, the director of scouting and the head coach's own eye.</summary>
        public static int PointsPerWeek(CareerSave s)
        {
            int pts = ManualWeeklyBase;
            var scouts = TeamScouts(s, s.UserTeamId);
            for (int i = 0; i < scouts.Count; i++) if (scouts[i].Director) pts += Math.Max(0, (scouts[i].Skill - 50) / 6);
            Coach hc = s.UserTeam.HeadCoach;
            if (hc != null) pts += (hc.Scouting - 60) / 10;
            return Math.Max(4, pts);
        }

        /// <summary>Points the user's scouts put into their assignments each week on their own.</summary>
        public static int AutoPointsPerWeek(CareerSave s)
        {
            float sum = 0f;
            var scouts = TeamScouts(s, s.UserTeamId);
            for (int i = 0; i < scouts.Count; i++) sum += scouts[i].WeeklyPoints;
            return (int)Math.Round(sum);
        }

        private static bool InSeasonPhase(CareerSave s)
        {
            return s.Phase == CareerPhase.RegularSeason || s.Phase == CareerPhase.Playoffs || s.Phase == CareerPhase.PixBowl;
        }

        // ================================================================ the pool
        internal static PoolCache Pool(CareerSave s)
        {
            CollegeNetState n = s.Net;
            var cache = n.PoolCacheObj as PoolCache;
            int year = s.College.Year;
            if (cache != null && cache.Year == year) return cache;
            cache = new PoolCache { Year = year };
            for (int si = 0; si < s.College.Schools.Count; si++)
            {
                School sc = s.College.Schools[si];
                var ro = sc.Team.Roster;
                for (int i = 0; i < ro.Count; i++)
                {
                    Player p = ro[i];
                    if (p.Retired || p.IsRedshirting || (p.Class != ClassYear.Junior && p.Class != ClassYear.Senior)) continue;
                    cache.List.Add(new PoolEntry { P = p, S = sc, Public = PublicGrade(s, p, year) });
                }
            }
            cache.List.Sort((a, b) => { int c = b.Public.CompareTo(a.Public); return c != 0 ? c : a.P.Id.CompareTo(b.P.Id); });
            for (int i = 0; i < cache.List.Count; i++) { cache.List[i].Rank = i + 1; cache.ById[cache.List[i].P.Id] = cache.List[i]; }
            n.PoolCacheObj = cache;
            return cache;
        }

        /// <summary>What the public (the mock-draft world) thinks of a player: his real grade plus public-opinion noise (sd 3.2 points) that is fixed for the year, so the public board is stable and nobody needs the truth to read it.</summary>
        public static float PublicGrade(CareerSave s, Player p, int year)
        {
            float g = ScoutingReport.Grade(p.Ratings.Overall(p.Position), p.Potential, p.Bio.Age);
            Rng r = CollegeNet.NetRng(s, 9005, year, p.Id);
            return g + r.Gaussian(0f, 3.2f);
        }

        /// <summary>Finds a player the scouting screens can act on, in the draft class when it exists, otherwise among the college juniors and seniors.</summary>
        public static ScoutTarget FindTarget(CareerSave s, int playerId)
        {
            if (s.College == null) return null;
            var cls = s.Pro.DraftClass;
            for (int i = 0; i < cls.Count; i++)
                if (cls[i].Id == playerId)
                    return new ScoutTarget { Player = cls[i].Player, Prospect = cls[i], School = cls[i].SchoolId >= 0 ? s.College.GetSchool(cls[i].SchoolId) : null, PublicRank = cls[i].ConsensusRank, InClass = true };
            if (cls.Count > 0) return null;   // once the class exists only its members can be scouted (juniors who stayed in school are out of this draft)
            PoolEntry e;
            if (Pool(s).ById.TryGetValue(playerId, out e))
                return new ScoutTarget { Player = e.P, School = e.S, Prospect = new Prospect { Player = e.P, SchoolId = e.S.Id, SchoolName = e.S.ShortName, ClassYear = e.P.Class, DeclaredEarly = e.P.Class == ClassYear.Junior }, PublicRank = e.Rank };
            return null;
        }

        public static bool ClassExists(CareerSave s) { return s.Pro.DraftClass.Count > 0; }

        // ================================================================ knowledge
        public static ScoutTier TierFor(ScoutedProspect e)
        {
            if (e == null) return ScoutTier.Unknown;
            int p = e.PointsSpent;
            if (p >= ExactPoints) return ScoutTier.Exact;
            if (p >= RangePoints) return ScoutTier.Range;
            if (p >= BandPoints) return ScoutTier.Band;
            return ScoutTier.Glance;
        }

        /// <summary>The user's entry for a player without creating one (null when he never looked at him).</summary>
        public static ScoutedProspect KnownEntry(ScoutingReport rep, int playerId)
        {
            if (rep == null) return null;
            var l = rep.Entries;
            for (int i = 0; i < l.Count; i++) if (l[i].ProspectId == playerId) return l[i];
            return null;
        }

        public static ScoutTier TierOf(CareerSave s, int playerId) { return TierFor(KnownEntry(UserReport(s), playerId)); }

        /// <summary>Spends points (charged or free) on one player and snaps his estimate to the truth once enough were spent.</summary>
        internal static void Apply(ScoutingReport rep, Prospect pr, int points, bool charge)
        {
            if (points <= 0) return;
            if (!charge) rep.PointsBudget += points;
            rep.Reveal(pr, points);
            SnapIfExact(rep, pr);
        }

        internal static void SnapIfExact(ScoutingReport rep, Prospect pr)
        {
            ScoutedProspect e = KnownEntry(rep, pr.Id);
            if (e == null || e.PointsSpent < ExactPoints) return;
            e.Estimated = pr.Player.Ratings; e.EstimatedPotential = pr.Player.Potential; e.Precision = 1000000f;
        }

        public static bool IsPhysical(RatingKind k)
        {
            return k == RatingKind.Speed || k == RatingKind.Acceleration || k == RatingKind.Agility || k == RatingKind.Strength || k == RatingKind.Jumping;
        }

        public static bool PhysicalsKnown(CareerSave s, int playerId) { return s.Net.PhysicalsKnown.Contains(playerId); }

        // ================================================================ user actions
        private static OpResult Gate(CareerSave s)
        {
            if (s.College == null) return OpResult.Fail("This career has no college world.");
            CollegeNet.Ensure(s);
            return null;
        }

        private static void Log(CareerSave s, string text)
        {
            s.Net.Log.Add(new ScoutLogItem { Year = s.Year, Week = s.Season != null ? s.Season.Week : 0, Text = text });
            if (s.Net.Log.Count > 80) s.Net.Log.RemoveRange(0, s.Net.Log.Count - 60);
        }

        /// <summary>Spends scouting points on one player: his ratings and ceiling get sharper (see the tier thresholds: 6 points a grade band, 20 a range, 60 the exact numbers).</summary>
        public static OpResult ScoutPlayer(CareerSave s, int playerId, int points)
        {
            OpResult gate = Gate(s);
            if (gate != null) return gate;
            if (s.Phase == CareerPhase.RookieSigning || s.Phase == CareerPhase.FreeAgency || s.Phase == CareerPhase.TrainingCamp || s.Phase == CareerPhase.Draft && s.Draft.Finished)
                return OpResult.Fail("Scouting is over for this year.");
            ScoutTarget t = FindTarget(s, playerId);
            if (t == null) return OpResult.Fail("That player is not draft eligible.");
            if (t.InClass && t.Prospect.Drafted) return OpResult.Fail("He was already drafted.");
            ScoutingReport rep = UserReport(s);
            if (rep == null) return OpResult.Fail("No scouting report.");
            if (points <= 0) return OpResult.Fail("Spend at least one point.");
            if (rep.PointsRemaining <= 0) return OpResult.Fail("No scouting points left.");
            ScoutTier before = TierFor(KnownEntry(rep, playerId));
            int spent = rep.PointsSpent;
            Apply(rep, t.Prospect, points, true);
            int used = rep.PointsSpent - spent;
            ScoutTier after = TierFor(KnownEntry(rep, playerId));
            Log(s, "Scouted " + t.Player.Name + " (" + used + " points)");
            return OpResult.Success("Spent " + used + " points on " + t.Player.Name + (after != before ? " - now " + after : "") + "; " + rep.PointsRemaining + " left.");
        }

        /// <summary>Points that raise a player to the next tier from where he is.</summary>
        public static int PointsToNextTier(CareerSave s, int playerId)
        {
            ScoutedProspect e = KnownEntry(UserReport(s), playerId);
            int p = e != null ? e.PointsSpent : 0;
            if (p >= ExactPoints) return 0;
            if (p >= RangePoints) return ExactPoints - p;
            if (p >= BandPoints) return RangePoints - p;
            return BandPoints - p;
        }

        public static OpResult ScoutNextTier(CareerSave s, int playerId)
        {
            int need = PointsToNextTier(s, playerId);
            if (need <= 0) return OpResult.Fail("His ratings are already exact.");
            return ScoutPlayer(s, playerId, need);
        }

        /// <summary>The user goes to a college game of this week: costs points, and both teams' best draft-eligible players get a free look (the bonus).</summary>
        public static OpResult AttendGame(CareerSave s, int gameId)
        {
            OpResult gate = Gate(s);
            if (gate != null) return gate;
            ScheduledGame g = CollegeNet.FindGame(s, gameId);
            Season cs = s.College.Season;
            if (g == null || g.Week != cs.Week || g.Played) return OpResult.Fail("That game is not on this week.");
            if (!InSeasonPhase(s) && s.Phase != CareerPhase.Preseason) return OpResult.Fail("There are no college games to attend right now.");
            if (s.Net.AttendYear == cs.Year && s.Net.AttendWeek == cs.Week) return OpResult.Fail("You already attended a game this week.");
            ScoutingReport rep = UserReport(s);
            if (rep == null || rep.PointsRemaining < GameCost) return OpResult.Fail("A game visit costs " + GameCost + " scouting points.");
            rep.PointsSpent += GameCost;
            s.Net.AttendYear = cs.Year; s.Net.AttendWeek = cs.Week;
            PoolCache pool = Pool(s);
            int looked = 0;
            var names = new List<string>();
            for (int side = 0; side < 2; side++)
            {
                int schoolId = side == 0 ? g.HomeId : g.AwayId;
                int taken = 0;
                for (int i = 0; i < pool.List.Count && taken < GameBonusPlayers; i++)
                {
                    PoolEntry e = pool.List[i];
                    if (e.S.Id != schoolId) continue;
                    var pr = new Prospect { Player = e.P, SchoolId = e.S.Id, SchoolName = e.S.ShortName, ClassYear = e.P.Class };
                    Apply(rep, pr, GameBonusPerPlayer, false);
                    taken++; looked++;
                    if (names.Count < 3) names.Add(e.P.Name);
                }
            }
            Log(s, "Attended " + s.College.GetSchool(g.AwayId).ShortName + " at " + s.College.GetSchool(g.HomeId).ShortName);
            return OpResult.Success("You watched " + looked + " draft prospects live (" + string.Join(", ", names.ToArray()) + "...); " + rep.PointsRemaining + " points left.");
        }

        /// <summary>The combine (Scouting phase, once): costs points and shows the user the physical measurements of everybody who worked out; their speed, strength and jumping ratings become exact.</summary>
        public static OpResult AttendCombine(CareerSave s)
        {
            OpResult gate = Gate(s);
            if (gate != null) return gate;
            if (s.Phase != CareerPhase.Scouting) return OpResult.Fail("The combine is held in the scouting phase of the offseason.");
            if (s.Net.CombineAttended) return OpResult.Fail("You already attended the combine.");
            ScoutingReport rep = UserReport(s);
            if (rep == null || rep.PointsRemaining < CombineCost) return OpResult.Fail("Attending the combine costs " + CombineCost + " scouting points.");
            rep.PointsSpent += CombineCost;
            s.Net.CombineAttended = true;
            int n = 0;
            var cls = s.Pro.DraftClass;
            for (int i = 0; i < cls.Count; i++)
            {
                if (!cls[i].Combine.Done || cls[i].Combine.ProDay) continue;
                RevealPhysicals(s, rep, cls[i]);
                n++;
            }
            Log(s, "Attended the combine");
            return OpResult.Success("Combine: " + n + " prospects measured; " + rep.PointsRemaining + " points left.");
        }

        /// <summary>A school's pro day (Scouting phase): measures the school's prospects who skipped or were not invited to the combine.</summary>
        public static OpResult AttendProDay(CareerSave s, int schoolId)
        {
            OpResult gate = Gate(s);
            if (gate != null) return gate;
            if (s.Phase != CareerPhase.Scouting) return OpResult.Fail("Pro days are held in the scouting phase of the offseason.");
            if (s.Net.ProDaySchools.Contains(schoolId)) return OpResult.Fail("You already went to that pro day.");
            School sc = s.College.GetSchool(schoolId);
            if (sc == null) return OpResult.Fail("Unknown school.");
            ScoutingReport rep = UserReport(s);
            if (rep == null || rep.PointsRemaining < ProDayCost) return OpResult.Fail("A pro day visit costs " + ProDayCost + " scouting points.");
            var cls = s.Pro.DraftClass;
            var targets = new List<Prospect>();
            for (int i = 0; i < cls.Count; i++) if (cls[i].SchoolId == schoolId) targets.Add(cls[i]);
            if (targets.Count == 0) return OpResult.Fail(sc.ShortName + " has nobody in this draft class.");
            rep.PointsSpent += ProDayCost;
            s.Net.ProDaySchools.Add(schoolId);
            Rng rng = CollegeNet.NetRng(s, 9006, s.Year, schoolId);
            int n = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                Prospect pr = targets[i];
                if (!pr.Combine.Done)
                {
                    pr.Combine = CollegeDraftClass.MeasureCombine(pr.Player, rng, true);
                }
                if (pr.Combine.Done && !s.Net.PhysicalsKnown.Contains(pr.Id)) { RevealPhysicals(s, rep, pr); n++; }
            }
            Log(s, "Went to the " + sc.ShortName + " pro day");
            return OpResult.Success(sc.ShortName + " pro day: " + n + " prospects measured; " + rep.PointsRemaining + " points left.");
        }

        private static void RevealPhysicals(CareerSave s, ScoutingReport rep, Prospect pr)
        {
            if (!s.Net.PhysicalsKnown.Contains(pr.Id)) s.Net.PhysicalsKnown.Add(pr.Id);
            ScoutedProspect e = rep.Get(pr);
            Ratings est = e.Estimated;
            Ratings truth = pr.Player.Ratings;
            est.Speed = truth.Speed; est.Acceleration = truth.Acceleration; est.Agility = truth.Agility; est.Strength = truth.Strength; est.Jumping = truth.Jumping;
            e.Estimated = est;
            Apply(rep, pr, 4, false);
        }

        // ================================================================ the weekly work of the scouts
        /// <summary>One week of scouting: the user's weekly points arrive and his assigned scouts study the best players of their area.</summary>
        internal static void WeeklyTick(CareerSave s)
        {
            if (!InSeasonPhase(s) || s.College == null) return;
            CollegeNetState n = s.Net;
            if (!n.HasUserReport) return;
            ScoutingReport rep = n.UserReport;
            int manual = PointsPerWeek(s);
            rep.PointsBudget += manual;
            n.PointsEarned += manual;
            int week = s.Season != null ? s.Season.Week : 0;
            PoolCache pool = Pool(s);
            var scouts = TeamScouts(s, s.UserTeamId);
            for (int i = 0; i < scouts.Count; i++) AutoScout(s, scouts[i], rep, pool, week);
            if (rep.PointsRemaining > WalletCap) rep.PointsBudget = rep.PointsSpent + WalletCap;
        }

        private static void AutoScout(CareerSave s, Scout sc, ScoutingReport rep, PoolCache pool, int week)
        {
            if (sc.Director) return;
            int region = sc.AssignSchoolId >= 0 ? -1 : (sc.AssignRegion >= 0 ? sc.AssignRegion : sc.HomeRegion);
            float output = sc.WeeklyPoints * (region == sc.HomeRegion && sc.AssignSchoolId < 0 ? 1.2f : 1f) * (sc.FocusBucket >= 0 ? 1.2f : 1f);
            var cands = new List<PoolEntry>();
            for (int i = 0; i < pool.List.Count && cands.Count < 30; i++)
            {
                PoolEntry e = pool.List[i];
                if (sc.AssignSchoolId >= 0 ? e.S.Id != sc.AssignSchoolId : (int)e.S.Region != region) continue;
                if (sc.FocusBucket >= 0 && (int)PositionBuckets.Of(e.P.Position) != sc.FocusBucket) continue;
                ScoutedProspect k = KnownEntry(rep, e.P.Id);
                if (k != null && k.PointsSpent >= ExactPoints) continue;
                cands.Add(e);
            }
            if (cands.Count == 0) return;
            Rng rng = CollegeNet.NetRng(s, 9007, s.College.Year, week, sc.Id);
            int picks = Math.Min(5, cands.Count);
            var chosen = new List<PoolEntry>();
            int tries = 0;
            while (chosen.Count < picks && tries++ < 200)
            {
                // better public ranks are likelier, every candidate has a chance
                int idx = (int)Math.Min(cands.Count - 1, Math.Floor(Math.Pow(rng.NextFloat(), 1.7) * cands.Count));
                PoolEntry e = cands[idx];
                if (!chosen.Contains(e)) chosen.Add(e);
            }
            for (int i = 0; i < cands.Count && chosen.Count < picks; i++) if (!chosen.Contains(cands[i])) chosen.Add(cands[i]);
            int total = (int)Math.Round(output);
            if (total < picks) total = picks;
            int each = total / picks, extra = total - each * picks;
            for (int i = 0; i < chosen.Count; i++)
            {
                PoolEntry e = chosen[i];
                var pr = new Prospect { Player = e.P, SchoolId = e.S.Id, SchoolName = e.S.ShortName, ClassYear = e.P.Class };
                Apply(rep, pr, each + (i < extra ? 1 : 0), false);
            }
            sc.PointsThisSeason += total;
        }

        // ================================================================ CPU teams
        /// <summary>The CPU teams' private scouting reports for a new draft class: each team's budget comes from its scouting points, head coach and scouts; the points go to the public top of the board, heavier on prospects from the regions its scouts know.</summary>
        internal static List<ScoutingReport> BuildCpuReports(CareerSave s, IList<Prospect> cls, Rng rng, int year)
        {
            var reports = new List<ScoutingReport>();
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                Team team = s.Pro.Teams[t];
                Coach hc = team.HeadCoach;
                int budget = team.ScoutingPoints + (hc != null ? (hc.Scouting - 60) * 2 : 0);
                var scouts = TeamScouts(s, t);
                float delta = 0f;
                for (int i = 0; i < scouts.Count; i++) delta += scouts[i].Director ? (scouts[i].Skill - 60) * 1.5f : (scouts[i].WeeklyPoints - 7f) * 19f;
                budget = Math.Max(80, budget + (int)Math.Round(delta));
                var rep = new ScoutingReport(team.Id, year, unchecked((long)rng.NextULong()), budget);
                int top = Math.Min(cls.Count, 120);
                var w = new float[top];
                float totalWeight = 0f;
                for (int i = 0; i < top; i++)
                {
                    float boost = 1f;
                    School sc = cls[i].SchoolId >= 0 && s.College != null ? s.College.GetSchool(cls[i].SchoolId) : null;
                    if (sc != null && scouts.Count > 0)
                    {
                        int match = 0;
                        for (int k = 0; k < scouts.Count; k++) if (!scouts[k].Director && scouts[k].HomeRegion == (int)sc.Region) match++;
                        boost += 0.3f * match / scouts.Count * 2f;
                    }
                    w[i] = (1.6f - 1.2f * i / top) * boost;
                    totalWeight += w[i];
                }
                for (int i = 0; i < top; i++) rep.Reveal(cls[i], Math.Max(1, (int)Math.Round(budget * w[i] / totalWeight)));
                reports.Add(rep);
            }
            return reports;
        }

        // ================================================================ the big board
        public static BoardEntry FindEntry(CareerSave s, int playerId)
        {
            var l = s.Net.Board;
            for (int i = 0; i < l.Count; i++) if (l[i].PlayerId == playerId) return l[i];
            return null;
        }

        private static BoardEntry EntryFor(CareerSave s, int playerId)
        {
            BoardEntry e = FindEntry(s, playerId);
            if (e == null) { e = new BoardEntry { PlayerId = playerId, AddedYear = s.Year }; s.Net.Board.Add(e); }
            return e;
        }

        private static void Tidy(CareerSave s, BoardEntry e)
        {
            if (!e.Starred && !e.OnBoard && string.IsNullOrEmpty(e.Note) && string.IsNullOrEmpty(e.Tag)) s.Net.Board.Remove(e);
        }

        private static void SyncDraftList(CareerSave s)
        {
            if (s.Draft == null) return;
            var ids = new List<int>();
            for (int i = 0; i < s.Net.Board.Count; i++) if (s.Net.Board[i].OnBoard) ids.Add(s.Net.Board[i].PlayerId);
            s.Draft.BigBoard = ids;
        }

        /// <summary>Highlights a player (the star in every list). Starring does not put him on the big board.</summary>
        public static OpResult Star(CareerSave s, int playerId, bool starred)
        {
            OpResult gate = Gate(s);
            if (gate != null) return gate;
            if (FindTarget(s, playerId) == null) return OpResult.Fail("That player is not draft eligible.");
            BoardEntry e = EntryFor(s, playerId);
            e.Starred = starred;
            Tidy(s, e);
            return OpResult.Success(starred ? "Starred." : "Star removed.");
        }

        /// <summary>Puts a player on the personal big board (at <paramref name="index"/> among the board entries, -1 = at the end) and stars him.</summary>
        public static OpResult AddToBoard(CareerSave s, int playerId, int index = -1)
        {
            OpResult gate = Gate(s);
            if (gate != null) return gate;
            if (FindTarget(s, playerId) == null) return OpResult.Fail("That player is not draft eligible.");
            BoardEntry e = EntryFor(s, playerId);
            var board = s.Net.Board;
            board.Remove(e);
            e.OnBoard = true; e.Starred = true;
            int pos = board.Count;
            if (index >= 0)
            {
                int seen = 0;
                for (int i = 0; i < board.Count; i++)
                {
                    if (!board[i].OnBoard) continue;
                    if (seen == index) { pos = i; break; }
                    seen++;
                }
            }
            board.Insert(Math.Min(pos, board.Count), e);
            SyncDraftList(s);
            return OpResult.Success("On your big board at #" + (BoardIds(s).IndexOf(playerId) + 1) + ".");
        }

        public static OpResult RemoveFromBoard(CareerSave s, int playerId)
        {
            BoardEntry e = FindEntry(s, playerId);
            if (e == null || !e.OnBoard) return OpResult.Fail("He is not on your board.");
            e.OnBoard = false;
            Tidy(s, e);
            SyncDraftList(s);
            return OpResult.Success("Removed from the board.");
        }

        /// <summary>Moves a player to a new place on the big board (0 = top).</summary>
        public static OpResult MoveOnBoard(CareerSave s, int playerId, int newIndex)
        {
            BoardEntry e = FindEntry(s, playerId);
            if (e == null || !e.OnBoard) return OpResult.Fail("He is not on your board.");
            return AddToBoard(s, playerId, Math.Max(0, newIndex));
        }

        public static OpResult SetNote(CareerSave s, int playerId, string note, string tag = null)
        {
            OpResult gate = Gate(s);
            if (gate != null) return gate;
            if (FindTarget(s, playerId) == null) return OpResult.Fail("That player is not draft eligible.");
            BoardEntry e = EntryFor(s, playerId);
            e.Note = note ?? "";
            if (tag != null) e.Tag = tag;
            if (e.Note.Length > 400) e.Note = e.Note.Substring(0, 400);
            Tidy(s, e);
            return OpResult.Success("Note saved.");
        }

        /// <summary>Player ids on the big board, first choice first.</summary>
        public static List<int> BoardIds(CareerSave s)
        {
            var ids = new List<int>();
            for (int i = 0; i < s.Net.Board.Count; i++) if (s.Net.Board[i].OnBoard) ids.Add(s.Net.Board[i].PlayerId);
            return ids;
        }

        /// <summary>After the class is made: board entries of players who are not in it (stayed in school, graduated unsigned) are dropped.</summary>
        internal static void PruneBoard(CareerSave s)
        {
            if (s.College == null) return;
            var cls = s.Pro.DraftClass;
            if (cls.Count > 0)
            {
                var ids = new HashSet<int>();
                for (int i = 0; i < cls.Count; i++) ids.Add(cls[i].Id);
                s.Net.Board.RemoveAll(e => !ids.Contains(e.PlayerId));
            }
            else
            {
                PoolCache pool = Pool(s);
                s.Net.Board.RemoveAll(e => !pool.ById.ContainsKey(e.PlayerId));
            }
            SyncDraftList(s);
        }
    }
}
