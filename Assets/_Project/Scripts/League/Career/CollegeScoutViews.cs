using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>One attribute cell of the scouting table: locked until the user spent enough points, then a grade band, then a range, then the exact number.</summary>
    public sealed class AttrCell
    {
        public RatingKind Kind;
        public string Name = "", Code = "";
        public bool Locked = true;
        /// <summary>What to print: "?" (locked), "B+" (grade band), "72-78" (range) or "75" (exact).</summary>
        public string Text = "?";
        public string Grade = "";
        /// <summary>The best estimate (for bars); -1 while locked.</summary>
        public int Value = -1;
        public int Lo = -1, Hi = -1;
        public bool Exact;
        /// <summary>The number comes from the combine / pro day measurements.</summary>
        public bool Measured;
    }

    /// <summary>One row of the scouting table (Madden-style reference 43): rank, name, school, position, height/weight, projected round, attribute grades locked or unlocked with scouting points, needs.</summary>
    public sealed class ScoutingRow
    {
        public int Rank, PlayerId, SchoolId = -1;
        public string Name = "", School = "", SchoolCrest = "", Height = "", ClassLabel = "", Stars = "";
        public Position Pos;
        public int WeightLb, Age;
        public bool DeclaredEarly, InClass, Drafted;
        public int PublicRank;
        public string PublicRound = "", UserRound = "";
        public int UserRank;
        public ScoutTier Tier;
        public int PointsSpent, NextTierCost;
        public float Uncertainty;
        public string OverallText = "?", PotentialText = "?", GradeText = "?";
        public List<AttrCell> Attributes = new List<AttrCell>();
        public bool Starred, OnBoard;
        public string Note = "", Tag = "";
        /// <summary>"High", "Medium" or "" - how badly the user's team needs the position.</summary>
        public string Need = "";
        public string Combine = "-";
        public bool PhysicalsKnown;
        public string Honors = "", SeasonLine = "";
    }

    public sealed class ScoutingFilter
    {
        /// <summary>"user" (the user's own grade), "public" (consensus rank), "position", "school", "points".</summary>
        public string Sort = "user";
        /// <summary>A <see cref="PositionBucket"/> index or -1.</summary>
        public int Bucket = -1;
        public int SchoolId = -1;
        public int Region = -1;
        public bool OnlyStarred, OnlyBoard, OnlyUnscouted;
        public string Search = "";
        public int Skip, Take = 100;
    }

    public sealed class ScoutingTableView
    {
        public List<ScoutingRow> Rows = new List<ScoutingRow>();
        public int Total;
        public int PointsRemaining, PointsPerWeek, AutoPointsPerWeek, PointsSpent;
        public bool ClassExists;
        public string Phase = "";
        public List<NeedRow> Needs = new List<NeedRow>();
        public int BoardCount, StarCount;
        public bool CombineAvailable, CombineAttended, CanAttendGame;
        public int BandCost = CollegeScouting.BandPoints, RangeCost = CollegeScouting.RangePoints, ExactCost = CollegeScouting.ExactPoints;
    }

    public sealed class ProspectCardView
    {
        public int PlayerId, SchoolId = -1;
        public string Name = "", School = "", SchoolCrest = "", Hometown = "", Height = "", ClassLabel = "", Stars = "", Archetype = "";
        public Position Pos;
        public int Age, WeightLb;
        public bool DeclaredEarly, InClass, Drafted;
        public int PublicRank, UserRank, DraftedPick;
        public string PublicRound = "", UserRound = "";
        public ScoutTier Tier;
        public int PointsSpent, NextTierCost;
        public float Uncertainty;
        public string OverallText = "?", PotentialText = "?", GradeText = "?";
        public List<AttrCell> Attributes = new List<AttrCell>();
        public CombineResult Combine = new CombineResult();
        public bool PhysicalsKnown;
        public List<AbilityView> Abilities = new List<AbilityView>();
        public List<string> Honors = new List<string>();
        public List<StatSeasonView> CollegeSeasons = new List<StatSeasonView>();
        public List<string> ScoutNotes = new List<string>();
        public bool Starred, OnBoard;
        public int BoardIndex = -1;
        public string Note = "", Tag = "";
        public string Need = "";
        public string DevTrait = "";
    }

    public sealed class BigBoardRow
    {
        public int Order, PlayerId, PublicRank, UserRank;
        public string Name = "", School = "", UserRound = "", PublicRound = "", Note = "", Tag = "", GradeText = "?";
        public Position Pos;
        public ScoutTier Tier;
        public bool Starred, OnBoard, Drafted;
        /// <summary>Public rank minus the user's rank: positive = the user likes him more than the public does.</summary>
        public int Delta;
        public string Need = "";
    }

    public sealed class ScoutRowView
    {
        public Scout Scout;
        public string RegionName = "", Focus = "", Assignment = "", Rating = "";
        public float WeeklyPoints;
    }

    public sealed class ScoutDepartmentView
    {
        public List<ScoutRowView> Scouts = new List<ScoutRowView>();
        public List<ScoutRowView> Market = new List<ScoutRowView>();
        public int ManualPointsPerWeek, AutoPointsPerWeek, PointsRemaining;
        public bool CanHire, CanFire;
        public List<ScoutLogItem> Log = new List<ScoutLogItem>();
    }

    /// <summary>Scouting table, prospect card, personal big board and scouting department for the College Network screens.</summary>
    public static class CollegeScoutViews
    {
        // ================================================================ text helpers
        public static string Letter(float v)
        {
            if (v >= 90) return "A+";
            if (v >= 85) return "A";
            if (v >= 80) return "A-";
            if (v >= 75) return "B+";
            if (v >= 70) return "B";
            if (v >= 65) return "B-";
            if (v >= 60) return "C+";
            if (v >= 55) return "C";
            if (v >= 50) return "C-";
            if (v >= 45) return "D";
            return "F";
        }

        public static string RoundText(int rank)
        {
            if (rank <= 0) return "";
            if (rank <= 10) return "Round 1 (top 10)";
            if (rank <= 32) return "Round 1";
            if (rank <= 224) return "Round " + ((rank - 1) / 32 + 1);
            return "Undrafted";
        }

        public static string Code(RatingKind k)
        {
            switch (k)
            {
                case RatingKind.Speed: return "SPD";
                case RatingKind.Acceleration: return "ACC";
                case RatingKind.Agility: return "AGI";
                case RatingKind.Strength: return "STR";
                case RatingKind.Awareness: return "AWR";
                case RatingKind.Catching: return "CTH";
                case RatingKind.Carrying: return "CAR";
                case RatingKind.ThrowPower: return "THP";
                case RatingKind.ThrowAccuracy: return "THA";
                case RatingKind.Tackling: return "TAK";
                case RatingKind.BlockShedding: return "BSH";
                case RatingKind.RunBlocking: return "RBK";
                case RatingKind.PassBlocking: return "PBK";
                case RatingKind.ManCoverage: return "MCV";
                case RatingKind.ZoneCoverage: return "ZCV";
                case RatingKind.Jumping: return "JMP";
                case RatingKind.Stamina: return "STA";
                case RatingKind.Injury: return "INJ";
                case RatingKind.KickPower: return "KPW";
                case RatingKind.KickAccuracy: return "KAC";
                case RatingKind.BreakTackle: return "BTK";
                case RatingKind.Elusiveness: return "ELU";
            }
            return "";
        }

        public static string KindName(RatingKind k)
        {
            string n = k.ToString();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n.Length; i++) { if (i > 0 && char.IsUpper(n[i])) sb.Append(' '); sb.Append(n[i]); }
            return sb.ToString();
        }

        /// <summary>The text for an overall (or ceiling) estimate at a tier: "?" (locked), a grade band, a range, or the exact number (<paramref name="truth"/> is only read at the exact tier).</summary>
        public static string RatingText(ScoutedProspect e, ScoutTier tier, float estimate, float sdFactor, int truth)
        {
            switch (tier)
            {
                case ScoutTier.Exact: return truth.ToString();
                case ScoutTier.Range:
                {
                    float half = Math.Max(1f, 1.65f * e.Sd * sdFactor);
                    int lo = (int)Math.Round(estimate - half), hi = (int)Math.Round(estimate + half);
                    return Math.Max(0, lo) + "-" + Math.Min(99, hi);
                }
                case ScoutTier.Band: return Letter(estimate);
            }
            return "?";
        }

        private static string StarText(int stars) { return stars <= 0 ? "" : new string('*', Math.Min(5, stars)); }

        private static RatingKind[] KeyKinds(Position pos, int count)
        {
            var w = new List<RatingWeight>(OverallWeights.For(pos));
            w.RemoveAll(x => x.Kind == RatingKind.Stamina || x.Kind == RatingKind.Injury);
            w.Sort((a, b) => b.Weight.CompareTo(a.Weight));
            var l = new List<RatingKind>();
            for (int i = 0; i < w.Count && l.Count < count; i++) l.Add(w[i].Kind);
            return l.ToArray();
        }

        private static AttrCell Cell(CareerSave s, ScoutedProspect e, ScoutTier tier, Player p, RatingKind k, bool physicalsKnown)
        {
            var c = new AttrCell { Kind = k, Name = KindName(k), Code = Code(k) };
            int truth = p.Ratings.Get(k);
            if (physicalsKnown && CollegeScouting.IsPhysical(k) && e != null)
            {
                c.Locked = false; c.Exact = true; c.Measured = true; c.Value = truth; c.Lo = truth; c.Hi = truth; c.Text = truth.ToString(); c.Grade = Letter(truth);
                return c;
            }
            if (e == null || tier == ScoutTier.Unknown || tier == ScoutTier.Glance) return c;
            float est = e.Estimated.Get(k);
            c.Locked = false; c.Value = (int)Math.Round(est); c.Grade = Letter(est);
            if (tier == ScoutTier.Exact) { c.Exact = true; c.Value = truth; c.Lo = truth; c.Hi = truth; c.Text = truth.ToString(); c.Grade = Letter(truth); }
            else if (tier == ScoutTier.Range)
            {
                float half = Math.Max(1f, 1.65f * e.Sd);
                c.Lo = Math.Max(0, (int)Math.Round(est - half)); c.Hi = Math.Min(99, (int)Math.Round(est + half)); c.Text = c.Lo + "-" + c.Hi;
            }
            else { c.Text = c.Grade; c.Lo = Math.Max(0, (int)Math.Round(est - e.Sd * 1.65f)); c.Hi = Math.Min(99, (int)Math.Round(est + e.Sd * 1.65f)); }
            return c;
        }

        // ================================================================ candidates
        private sealed class Cand
        {
            public Player P;
            public School S;
            public Prospect Pr;   // set when the class exists
            public int PublicRank;
            public float PublicGrade;
        }

        private static List<Cand> Candidates(CareerSave s)
        {
            var l = new List<Cand>();
            if (s.College == null) return l;
            if (s.Pro.DraftClass.Count > 0)
            {
                var cls = s.Pro.DraftClass;
                for (int i = 0; i < cls.Count; i++)
                    l.Add(new Cand { P = cls[i].Player, S = cls[i].SchoolId >= 0 ? s.College.GetSchool(cls[i].SchoolId) : null, Pr = cls[i], PublicRank = cls[i].ConsensusRank, PublicGrade = 1000f - cls[i].ConsensusRank });
                return l;
            }
            PoolCache pool = CollegeScouting.Pool(s);
            for (int i = 0; i < pool.List.Count; i++) l.Add(new Cand { P = pool.List[i].P, S = pool.List[i].S, PublicRank = pool.List[i].Rank, PublicGrade = pool.List[i].Public });
            return l;
        }

        private static Dictionary<int, ScoutedProspect> EntryMap(ScoutingReport rep)
        {
            var d = new Dictionary<int, ScoutedProspect>();
            if (rep == null) return d;
            for (int i = 0; i < rep.Entries.Count; i++) d[rep.Entries[i].ProspectId] = rep.Entries[i];
            return d;
        }

        private static float UserGrade(Cand c, Dictionary<int, ScoutedProspect> map)
        {
            ScoutedProspect e;
            if (map.TryGetValue(c.P.Id, out e)) return ScoutingReport.Grade(e.Estimated.Overall(c.P.Position), e.EstimatedPotential, c.P.Bio.Age);
            return c.Pr != null ? 1000f - c.PublicRank : c.PublicGrade;
        }

        /// <summary>Rank of every candidate on the user's own board (his estimate, the public opinion where he knows nothing).</summary>
        private static Dictionary<int, int> UserRanks(List<Cand> cands, Dictionary<int, ScoutedProspect> map)
        {
            var order = new List<Cand>(cands);
            var grade = new Dictionary<int, float>(order.Count);
            for (int i = 0; i < order.Count; i++) grade[order[i].P.Id] = UserGrade(order[i], map);
            order.Sort((a, b) => { int c = grade[b.P.Id].CompareTo(grade[a.P.Id]); return c != 0 ? c : a.P.Id.CompareTo(b.P.Id); });
            var ranks = new Dictionary<int, int>(order.Count);
            for (int i = 0; i < order.Count; i++) ranks[order[i].P.Id] = i + 1;
            return ranks;
        }

        private static string NeedOf(Dictionary<Position, float> needs, Position pos)
        {
            float v;
            if (!needs.TryGetValue(pos, out v)) return "";
            return v >= 25f ? "High" : (v >= 10f ? "Medium" : "");
        }

        private static Dictionary<Position, float> NeedMap(CareerSave s)
        {
            var d = new Dictionary<Position, float>();
            var rows = CareerViews.Needs(s, s.UserTeam);
            for (int i = 0; i < rows.Count; i++) d[rows[i].Pos] = rows[i].Need;
            return d;
        }

        // ================================================================ the scouting table
        public static ScoutingTableView ScoutingTable(CareerSave s, ScoutingFilter f = null)
        {
            var v = new ScoutingTableView();
            if (s.College == null) return v;
            CollegeNet.Ensure(s);
            if (f == null) f = new ScoutingFilter();
            ScoutingReport rep = CollegeScouting.UserReport(s);
            var map = EntryMap(rep);
            v.ClassExists = s.Pro.DraftClass.Count > 0;
            v.Phase = CareerText.PhaseLabel(s);
            v.PointsRemaining = rep != null ? rep.PointsRemaining : 0; v.PointsSpent = rep != null ? rep.PointsSpent : 0;
            v.PointsPerWeek = CollegeScouting.PointsPerWeek(s); v.AutoPointsPerWeek = CollegeScouting.AutoPointsPerWeek(s);
            var needsRows = CareerViews.Needs(s, s.UserTeam);
            for (int i = 0; i < needsRows.Count && i < 8; i++) v.Needs.Add(needsRows[i]);
            for (int i = 0; i < s.Net.Board.Count; i++) { if (s.Net.Board[i].OnBoard) v.BoardCount++; if (s.Net.Board[i].Starred) v.StarCount++; }
            v.CombineAvailable = s.Phase == CareerPhase.Scouting; v.CombineAttended = s.Net.CombineAttended;
            Season cs = s.College.Season;
            v.CanAttendGame = !v.ClassExists && !cs.IsComplete && !(s.Net.AttendYear == cs.Year && s.Net.AttendWeek == cs.Week) && v.PointsRemaining >= CollegeScouting.GameCost;

            var cands = Candidates(s);
            var ranks = UserRanks(cands, map);
            // filter
            var list = new List<Cand>();
            string search = (f.Search ?? "").Trim().ToLowerInvariant();
            for (int i = 0; i < cands.Count; i++)
            {
                Cand c = cands[i];
                if (f.Bucket >= 0 && (int)PositionBuckets.Of(c.P.Position) != f.Bucket) continue;
                if (f.SchoolId >= 0 && (c.S == null || c.S.Id != f.SchoolId)) continue;
                if (f.Region >= 0 && (c.S == null || (int)c.S.Region != f.Region)) continue;
                if (f.OnlyStarred || f.OnlyBoard)
                {
                    BoardEntry be = CollegeScouting.FindEntry(s, c.P.Id);
                    if (be == null || f.OnlyStarred && !be.Starred || f.OnlyBoard && !be.OnBoard) continue;
                }
                if (f.OnlyUnscouted) { ScoutedProspect k; if (map.TryGetValue(c.P.Id, out k) && k.PointsSpent > 0) continue; }
                if (search.Length > 0 && c.P.Name.ToLowerInvariant().IndexOf(search, StringComparison.Ordinal) < 0 && (c.S == null || c.S.ShortName.ToLowerInvariant().IndexOf(search, StringComparison.Ordinal) < 0)) continue;
                list.Add(c);
            }
            v.Total = list.Count;
            switch ((f.Sort ?? "user").ToLowerInvariant())
            {
                case "public": list.Sort((a, b) => a.PublicRank.CompareTo(b.PublicRank)); break;
                case "position": list.Sort((a, b) => { int x = Array.IndexOf(LeagueRules.RosterOrder, a.P.Position).CompareTo(Array.IndexOf(LeagueRules.RosterOrder, b.P.Position)); return x != 0 ? x : ranks[a.P.Id].CompareTo(ranks[b.P.Id]); }); break;
                case "school": list.Sort((a, b) => { int x = string.CompareOrdinal(a.S != null ? a.S.ShortName : "", b.S != null ? b.S.ShortName : ""); return x != 0 ? x : ranks[a.P.Id].CompareTo(ranks[b.P.Id]); }); break;
                case "points":
                    list.Sort((a, b) =>
                    {
                        ScoutedProspect ea, eb; map.TryGetValue(a.P.Id, out ea); map.TryGetValue(b.P.Id, out eb);
                        int pa = ea != null ? ea.PointsSpent : 0, pb = eb != null ? eb.PointsSpent : 0;
                        int x = pb.CompareTo(pa);
                        return x != 0 ? x : ranks[a.P.Id].CompareTo(ranks[b.P.Id]);
                    });
                    break;
                default: list.Sort((a, b) => ranks[a.P.Id].CompareTo(ranks[b.P.Id])); break;
            }
            var needs = NeedMap(s);
            for (int i = f.Skip; i < list.Count && i < f.Skip + Math.Max(1, f.Take); i++)
            {
                ScoutingRow row = MakeRow(s, list[i], map, ranks, needs, 6);
                row.Rank = i + 1;
                v.Rows.Add(row);
            }
            return v;
        }

        private static ScoutingRow MakeRow(CareerSave s, Cand c, Dictionary<int, ScoutedProspect> map, Dictionary<int, int> ranks, Dictionary<Position, float> needs, int attrCount)
        {
            Player p = c.P;
            ScoutedProspect e;
            map.TryGetValue(p.Id, out e);
            ScoutTier tier = CollegeScouting.TierFor(e);
            var r = new ScoutingRow
            {
                PlayerId = p.Id, Name = p.Name, Pos = p.Position, Age = p.Bio.Age, Height = p.Bio.HeightDisplay, WeightLb = p.Bio.WeightLb, SchoolId = c.S != null ? c.S.Id : -1, School = c.S != null ? c.S.ShortName : (c.Pr != null ? c.Pr.SchoolName : ""),
                SchoolCrest = c.S != null ? c.S.Logo : "", ClassLabel = p.Class.ToString(), Stars = StarText(p.Stars), DeclaredEarly = c.Pr != null ? c.Pr.DeclaredEarly : p.Class == ClassYear.Junior, InClass = c.Pr != null, Drafted = c.Pr != null && c.Pr.Drafted,
                PublicRank = c.PublicRank, PublicRound = RoundText(c.PublicRank), UserRank = ranks.ContainsKey(p.Id) ? ranks[p.Id] : 0, Tier = tier, PointsSpent = e != null ? e.PointsSpent : 0,
                Uncertainty = e != null ? e.Sd : 99f, Need = NeedOf(needs, p.Position)
            };
            r.UserRound = RoundText(r.UserRank);
            r.NextTierCost = PointsToNext(r.PointsSpent);
            BoardEntry be = CollegeScouting.FindEntry(s, p.Id);
            if (be != null) { r.Starred = be.Starred; r.OnBoard = be.OnBoard; r.Note = be.Note; r.Tag = be.Tag; }
            r.PhysicalsKnown = CollegeScouting.PhysicalsKnown(s, p.Id);
            if (e != null)
            {
                r.OverallText = RatingText(e, tier, e.Estimated.Overall(p.Position), 1f, p.Overall);
                r.PotentialText = RatingText(e, tier, e.EstimatedPotential, 1.3f, p.Potential);
                r.GradeText = tier >= ScoutTier.Band ? Letter(ScoutingReport.Grade(e.Estimated.Overall(p.Position), e.EstimatedPotential, p.Bio.Age) - 0f) : "?";
            }
            var kinds = KeyKinds(p.Position, attrCount);
            for (int i = 0; i < kinds.Length; i++) r.Attributes.Add(Cell(s, e, tier, p, kinds[i], r.PhysicalsKnown));
            if (c.Pr != null)
            {
                if (r.PhysicalsKnown) r.Combine = c.Pr.Combine.Summary; else r.Combine = c.Pr.Combine.Done ? "Measured" : (c.Pr.Combine.Skipped ? "Skipped" : "-");
                r.Honors = string.Join("; ", c.Pr.Honors.ToArray());
            }
            else
            {
                var hs = CollegeHonors.HonorsOf(s, p.Id);
                var parts = new List<string>();
                for (int i = 0; i < hs.Count && i < 3; i++) parts.Add(CollegeHonors.HonorText(s, hs[i]));
                r.Honors = string.Join("; ", parts.ToArray());
            }
            SeasonStats st = p.FindStats(s.College.Year) ?? LastCollege(p);
            r.SeasonLine = st != null ? CollegeViews.SeasonLine(p.Position, st) : "";
            return r;
        }

        private static SeasonStats LastCollege(Player p)
        {
            return p.CollegeCareer.Count > 0 ? p.CollegeCareer[p.CollegeCareer.Count - 1] : null;
        }

        private static int PointsToNext(int p)
        {
            if (p >= CollegeScouting.ExactPoints) return 0;
            if (p >= CollegeScouting.RangePoints) return CollegeScouting.ExactPoints - p;
            if (p >= CollegeScouting.BandPoints) return CollegeScouting.RangePoints - p;
            return CollegeScouting.BandPoints - p;
        }

        // ================================================================ the prospect card
        public static ProspectCardView Card(CareerSave s, int playerId)
        {
            if (s.College == null) return null;
            CollegeNet.Ensure(s);
            ScoutTarget t = CollegeScouting.FindTarget(s, playerId);
            if (t == null) return null;
            Player p = t.Player;
            ScoutingReport rep = CollegeScouting.UserReport(s);
            ScoutedProspect e = CollegeScouting.KnownEntry(rep, playerId);
            ScoutTier tier = CollegeScouting.TierFor(e);
            var map = EntryMap(rep);
            var cands = Candidates(s);
            var ranks = UserRanks(cands, map);
            var v = new ProspectCardView
            {
                PlayerId = p.Id, Name = p.Name, Pos = p.Position, Age = p.Bio.Age, Height = p.Bio.HeightDisplay, WeightLb = p.Bio.WeightLb, Hometown = p.Bio.Hometown ?? "", School = t.School != null ? t.School.Name : p.Bio.College ?? "",
                SchoolId = t.School != null ? t.School.Id : -1, SchoolCrest = t.School != null ? t.School.Logo : "", ClassLabel = p.Class.ToString(), Stars = StarText(p.Stars), DeclaredEarly = t.Prospect.DeclaredEarly, InClass = t.InClass,
                Drafted = t.InClass && t.Prospect.Drafted, DraftedPick = t.InClass && t.Prospect.Drafted ? t.Prospect.OverallPick : 0, PublicRank = t.PublicRank, PublicRound = RoundText(t.PublicRank),
                UserRank = ranks.ContainsKey(p.Id) ? ranks[p.Id] : 0, Tier = tier, PointsSpent = e != null ? e.PointsSpent : 0, Uncertainty = e != null ? e.Sd : 99f
            };
            v.UserRound = RoundText(v.UserRank);
            v.NextTierCost = PointsToNext(v.PointsSpent);
            v.PhysicalsKnown = CollegeScouting.PhysicalsKnown(s, p.Id);
            v.Need = NeedOf(NeedMap(s), p.Position);
            if (e != null)
            {
                v.OverallText = RatingText(e, tier, e.Estimated.Overall(p.Position), 1f, p.Overall);
                v.PotentialText = RatingText(e, tier, e.EstimatedPotential, 1.3f, p.Potential);
                v.GradeText = tier >= ScoutTier.Band ? Letter(ScoutingReport.Grade(e.Estimated.Overall(p.Position), e.EstimatedPotential, p.Bio.Age)) : "?";
            }
            var kinds = KeyKinds(p.Position, 10);
            for (int i = 0; i < kinds.Length; i++) v.Attributes.Add(Cell(s, e, tier, p, kinds[i], v.PhysicalsKnown));
            if (t.InClass)
            {
                v.Combine = t.Prospect.Combine;
                v.Honors.AddRange(t.Prospect.Honors);
                if (tier >= ScoutTier.Range)
                {
                    var hints = AbilityCatalog.ScoutedHints(p, e != null ? e.PointsSpent : 0);
                    for (int i = 0; i < hints.Count; i++)
                    {
                        AbilityDef d = AbilityCatalog.Get(hints[i].Id);
                        if (d == null) continue;
                        v.Abilities.Add(new AbilityView { Id = d.Id, Name = d.Name, Tier = hints[i].Tier, Stars = AbilityCatalog.Stars(hints[i].Tier), Description = d.Description, Effects = AbilityCatalog.Describe(hints[i]) });
                    }
                }
            }
            else
            {
                var hs = CollegeHonors.HonorsOf(s, p.Id);
                for (int i = 0; i < hs.Count; i++) v.Honors.Add(CollegeHonors.HonorText(s, hs[i]));
            }
            var seasons = t.InClass || p.CollegeCareer.Count > 0 ? p.CollegeCareer : p.Career;
            for (int i = 0; i < seasons.Count; i++)
            {
                SeasonStats st = seasons[i];
                if (st.Postseason) continue;
                School sc = s.College.GetSchool(st.TeamId);
                v.CollegeSeasons.Add(new StatSeasonView { Year = st.Season, TeamId = st.TeamId, Team = sc != null ? sc.ShortName : "", Games = st.Games, Line = CollegeViews.SeasonLine(p.Position, st) });
            }
            BoardEntry be = CollegeScouting.FindEntry(s, p.Id);
            if (be != null) { v.Starred = be.Starred; v.OnBoard = be.OnBoard; v.Note = be.Note; v.Tag = be.Tag; v.BoardIndex = be.OnBoard ? CollegeScouting.BoardIds(s).IndexOf(p.Id) : -1; }
            v.ScoutNotes = Notes(v);
            return v;
        }

        private static List<string> Notes(ProspectCardView v)
        {
            var notes = new List<string>();
            if (v.Tier < ScoutTier.Band) { notes.Add("Not scouted yet: spend " + v.NextTierCost + " points to unlock his grade bands."); return notes; }
            AttrCell best = null, worst = null, second = null;
            for (int i = 0; i < v.Attributes.Count; i++)
            {
                AttrCell c = v.Attributes[i];
                if (c.Locked) continue;
                if (best == null || c.Value > best.Value) { second = best; best = c; }
                else if (second == null || c.Value > second.Value) second = c;
                if (worst == null || c.Value < worst.Value) worst = c;
            }
            if (best != null) notes.Add("Strength: " + best.Name + " (" + best.Grade + ")");
            if (second != null && second != best) notes.Add("Strength: " + second.Name + " (" + second.Grade + ")");
            if (worst != null && worst != best) notes.Add("Concern: " + worst.Name + " (" + worst.Grade + ")");
            if (v.Tier < ScoutTier.Exact) notes.Add("Still fuzzy: ratings are +/- " + Math.Round(v.Uncertainty, 1) + "; " + v.NextTierCost + " more points sharpen them.");
            return notes;
        }

        // ================================================================ the personal big board
        public static List<BigBoardRow> BigBoard(CareerSave s)
        {
            var rows = new List<BigBoardRow>();
            if (s.College == null) return rows;
            CollegeNet.Ensure(s);
            ScoutingReport rep = CollegeScouting.UserReport(s);
            var map = EntryMap(rep);
            var cands = Candidates(s);
            var byId = new Dictionary<int, Cand>();
            for (int i = 0; i < cands.Count; i++) byId[cands[i].P.Id] = cands[i];
            var ranks = UserRanks(cands, map);
            var needs = NeedMap(s);
            int order = 0;
            var list = s.Net.Board;
            for (int i = 0; i < list.Count; i++)
            {
                BoardEntry be = list[i];
                Cand c;
                if (!be.OnBoard || !byId.TryGetValue(be.PlayerId, out c)) continue;
                rows.Add(MakeBoardRow(s, c, be, map, ranks, needs, ++order));
            }
            return rows;
        }

        /// <summary>Highlighted (starred) players who are not on the ordered board yet.</summary>
        public static List<BigBoardRow> Starred(CareerSave s)
        {
            var rows = new List<BigBoardRow>();
            if (s.College == null) return rows;
            CollegeNet.Ensure(s);
            var map = EntryMap(CollegeScouting.UserReport(s));
            var cands = Candidates(s);
            var byId = new Dictionary<int, Cand>();
            for (int i = 0; i < cands.Count; i++) byId[cands[i].P.Id] = cands[i];
            var ranks = UserRanks(cands, map);
            var needs = NeedMap(s);
            var list = s.Net.Board;
            for (int i = 0; i < list.Count; i++)
            {
                Cand c;
                if (!list[i].Starred || list[i].OnBoard || !byId.TryGetValue(list[i].PlayerId, out c)) continue;
                rows.Add(MakeBoardRow(s, c, list[i], map, ranks, needs, 0));
            }
            return rows;
        }

        private static BigBoardRow MakeBoardRow(CareerSave s, Cand c, BoardEntry be, Dictionary<int, ScoutedProspect> map, Dictionary<int, int> ranks, Dictionary<Position, float> needs, int order)
        {
            ScoutedProspect e;
            map.TryGetValue(c.P.Id, out e);
            ScoutTier tier = CollegeScouting.TierFor(e);
            var r = new BigBoardRow
            {
                Order = order, PlayerId = c.P.Id, Name = c.P.Name, Pos = c.P.Position, School = c.S != null ? c.S.ShortName : "", PublicRank = c.PublicRank, PublicRound = RoundText(c.PublicRank), UserRank = ranks.ContainsKey(c.P.Id) ? ranks[c.P.Id] : 0,
                Note = be.Note, Tag = be.Tag, Tier = tier, Starred = be.Starred, OnBoard = be.OnBoard, Drafted = c.Pr != null && c.Pr.Drafted, Need = NeedOf(needs, c.P.Position)
            };
            r.UserRound = RoundText(r.UserRank);
            r.Delta = r.PublicRank - r.UserRank;
            if (e != null && tier >= ScoutTier.Band) r.GradeText = Letter(ScoutingReport.Grade(e.Estimated.Overall(c.P.Position), e.EstimatedPotential, c.P.Bio.Age));
            return r;
        }

        // ================================================================ the scouting department
        public static ScoutDepartmentView Department(CareerSave s)
        {
            var v = new ScoutDepartmentView();
            if (s.College == null) return v;
            CollegeNet.Ensure(s);
            var mine = CollegeScouting.UserScouts(s);
            for (int i = 0; i < mine.Count; i++) v.Scouts.Add(RowOf(s, mine[i]));
            var market = CollegeScouting.Market(s);
            for (int i = 0; i < market.Count; i++) v.Market.Add(RowOf(s, market[i]));
            v.ManualPointsPerWeek = CollegeScouting.PointsPerWeek(s); v.AutoPointsPerWeek = CollegeScouting.AutoPointsPerWeek(s); v.PointsRemaining = CollegeScouting.PointsRemaining(s);
            v.CanHire = mine.Count < CollegeScouting.MaxScouts; v.CanFire = mine.Count > CollegeScouting.MinScouts;
            v.Log.AddRange(s.Net.Log);
            v.Log.Reverse();
            return v;
        }

        private static ScoutRowView RowOf(CareerSave s, Scout sc)
        {
            string assign;
            if (sc.Director) assign = "Runs the department";
            else if (sc.TeamId < 0) assign = "Available";
            else if (sc.AssignSchoolId >= 0) { School school = s.College.GetSchool(sc.AssignSchoolId); assign = school != null ? school.Name : "School"; }
            else assign = ((Region)(sc.AssignRegion >= 0 ? sc.AssignRegion : sc.HomeRegion)).ToString();
            return new ScoutRowView
            {
                Scout = sc, RegionName = ((Region)sc.HomeRegion).ToString(), Focus = sc.FocusLabel, Assignment = assign, WeeklyPoints = sc.WeeklyPoints,
                Rating = sc.Skill >= 85 ? "Elite" : (sc.Skill >= 72 ? "Very good" : (sc.Skill >= 58 ? "Solid" : "Raw"))
            };
        }
    }
}
