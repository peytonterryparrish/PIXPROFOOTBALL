using System;
using System.Collections.Generic;
using PixFootball.Core;
using Side = PixFootball.Core.Team;

namespace PixFootball.League
{
    // ------------------------------------------------------------------------------------------
    // Serialization design: everything in this file is [Serializable] with public fields, List<T>
    // and ids (no dictionaries, no cross references) so Unity's JsonUtility can save/load it.
    // Lookup caches are [NonSerialized] and rebuilt lazily.
    // ------------------------------------------------------------------------------------------

    public enum Handedness { Right, Left }

    /// <summary>Development trait: speeds up growth and (for Late) shifts the growth window.</summary>
    public enum DevTrait { Normal, Star, Superstar, Late }

    public enum ClassYear { Freshman, Sophomore, Junior, Senior }

    public enum Archetype
    {
        None,
        QbPocket, QbScrambler, QbGunslinger,
        RbPower, RbSpeed, RbAllPurpose,
        FbBlocker,
        WrDeepThreat, WrPossession, WrSlot,
        TeBlocker, TeReceiver,
        OlPassProtector, OlRunMauler, OlBalanced,
        DlRunStuffer, DlPassRusher, DlBalanced,
        LbRunStopper, LbCoverage, LbBlitzer,
        CbManLockdown, CbZone, CbSlot,
        SBox, SCenterfield,
        KPlacekicker, PPunter
    }

    public enum CoachRole { HeadCoach, OffensiveCoordinator, DefensiveCoordinator }
    public enum OffenseScheme { Balanced, AirRaid, WestCoast, PowerRun, SpreadOption }
    public enum DefenseScheme { Base43, Base34, Nickel, Aggressive, Zone }

    [Serializable]
    public class PlayerBio
    {
        public int Id;
        public string FirstName;
        public string LastName;
        public int Age;
        public int HeightIn;
        public int WeightLb;
        public string Hometown;
        public string College;
        public int JerseyNumber;
        public Handedness Hand;

        public string FullName { get { return FirstName + " " + LastName; } }

        public string HeightDisplay { get { return (HeightIn / 12) + "'" + (HeightIn % 12) + "\""; } }
    }

    [Serializable]
    public class InjuryState
    {
        public int WeeksOut;
        public string Type = "";
        /// <summary>Accumulated wear from past injuries (raises retirement odds, lowers durability).</summary>
        public int Wear;
        /// <summary>Weeks spent on injured reserve this stay (v0.8.0 career layer; -1 = not on IR).</summary>
        public int IrWeeks = -1;

        public bool IsInjured { get { return WeeksOut > 0; } }
        public void Heal(int weeks = 1) { WeeksOut = Math.Max(0, WeeksOut - weeks); if (WeeksOut == 0) Type = ""; }
    }

    [Serializable]
    public class Contract
    {
        public int Years;
        public int YearsLeft;
        /// <summary>Average annual value in dollars.</summary>
        public long Aav;
        public long SigningBonus;
        public long Guaranteed;
        /// <summary>Cap hit for each contract year, index 0 = first year.</summary>
        public List<long> CapHits = new List<long>();
        public bool Rookie;
        public int StartYear;

        public const long MinSalary = 850000L;

        public int YearIndex { get { return Math.Max(0, Math.Min(CapHits.Count - 1, Years - YearsLeft)); } }

        /// <summary>Cap hit for the current contract year (0 when no contract).</summary>
        public long CapHit { get { return (CapHits.Count == 0 || YearsLeft <= 0) ? 0L : CapHits[YearIndex]; } }

        public bool IsExpiring { get { return YearsLeft <= 1; } }

        /// <summary>Builds a contract: bonus is prorated over the deal, base salary makes up the rest.</summary>
        public static Contract Create(int years, long aav, float bonusFraction, float guaranteedFraction, int startYear, bool rookie = false)
        {
            years = Math.Max(1, years);
            if (aav < MinSalary) aav = MinSalary;
            long total = aav * years;
            long bonus = (long)(total * Math.Max(0f, Math.Min(0.6f, bonusFraction)));
            long prorated = bonus / years;
            long baseSalary = aav - prorated;
            var c = new Contract
            {
                Years = years, YearsLeft = years, Aav = aav, SigningBonus = bonus,
                Guaranteed = (long)(total * Math.Max(0f, Math.Min(1f, guaranteedFraction))),
                Rookie = rookie, StartYear = startYear
            };
            for (int i = 0; i < years; i++)
            {
                // Slight back-loading: later years carry a little more base, same total.
                float shape = years == 1 ? 1f : 0.92f + 0.16f * i / (years - 1);
                c.CapHits.Add(Math.Max(MinSalary, (long)(baseSalary * shape) + prorated));
            }
            return c;
        }

        public Contract Clone()
        {
            var c = (Contract)MemberwiseClone();
            c.CapHits = new List<long>(CapHits);
            return c;
        }

        public void AdvanceYear() { if (YearsLeft > 0) YearsLeft--; }
    }

    /// <summary>Counting stats (used for single games, seasons and career lines).</summary>
    [Serializable]
    public class StatLine
    {
        public int Games, GamesStarted;
        // passing
        public int PassAtt, PassCmp, PassYds, PassTd, PassInt, Sacked, SackYds, PassLong;
        // rushing
        public int RushAtt, RushYds, RushTd, RushLong, Fumbles, FumblesLost;
        // receiving
        public int Targets, Rec, RecYds, RecTd, RecLong;
        // defense
        public int Tackles, TacklesForLoss, Sacks, Interceptions, IntYds, IntTd, PassesDefended, ForcedFumbles, FumbleRecoveries;
        // kicking / punting
        public int FgAtt, FgMade, FgLong, XpAtt, XpMade, Punts, PuntYds, PuntIn20;
        // returns
        public int KrAtt, KrYds, PrAtt, PrYds, RetTd;
        // misc
        public int Penalties, PenaltyYds;
        /// <summary>Two-point conversions scored and safeties made (QA-611: they count toward <see cref="Points"/>).</summary>
        public int TwoPtMade, Safeties;

        public int Points { get { return (RushTd + RecTd + IntTd + RetTd) * 6 + FgMade * 3 + XpMade + TwoPtMade * 2 + Safeties * 2; } }
        public int TotalTd { get { return RushTd + RecTd + IntTd + RetTd; } }

        public bool HasActivity
        {
            get
            {
                return PassAtt + RushAtt + Targets + Tackles + Sacks + Interceptions + PassesDefended + FgAtt + XpAtt
                       + Punts + KrAtt + PrAtt + Fumbles + ForcedFumbles + FumbleRecoveries + Sacked + TacklesForLoss > 0;
            }
        }

        public float PasserRating
        {
            get
            {
                if (PassAtt <= 0) return 0f;
                float att = PassAtt;
                float a = Clamp01(((PassCmp / att) - 0.3f) * 5f);
                float b = Clamp01(((PassYds / att) - 3f) * 0.25f);
                float c = Clamp01((PassTd / att) * 20f);
                float d = Clamp01(2.375f - (PassInt / att * 25f));
                return (a + b + c + d) / 6f * 100f;
            }
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 2.375f ? 2.375f : v); }

        public void Add(StatLine o)
        {
            if (o == null) return;
            Games += o.Games; GamesStarted += o.GamesStarted;
            PassAtt += o.PassAtt; PassCmp += o.PassCmp; PassYds += o.PassYds; PassTd += o.PassTd; PassInt += o.PassInt;
            Sacked += o.Sacked; SackYds += o.SackYds; PassLong = Math.Max(PassLong, o.PassLong);
            RushAtt += o.RushAtt; RushYds += o.RushYds; RushTd += o.RushTd; RushLong = Math.Max(RushLong, o.RushLong);
            Fumbles += o.Fumbles; FumblesLost += o.FumblesLost;
            Targets += o.Targets; Rec += o.Rec; RecYds += o.RecYds; RecTd += o.RecTd; RecLong = Math.Max(RecLong, o.RecLong);
            Tackles += o.Tackles; TacklesForLoss += o.TacklesForLoss; Sacks += o.Sacks; Interceptions += o.Interceptions;
            IntYds += o.IntYds; IntTd += o.IntTd; PassesDefended += o.PassesDefended; ForcedFumbles += o.ForcedFumbles;
            FumbleRecoveries += o.FumbleRecoveries;
            FgAtt += o.FgAtt; FgMade += o.FgMade; FgLong = Math.Max(FgLong, o.FgLong); XpAtt += o.XpAtt; XpMade += o.XpMade;
            Punts += o.Punts; PuntYds += o.PuntYds; PuntIn20 += o.PuntIn20;
            KrAtt += o.KrAtt; KrYds += o.KrYds; PrAtt += o.PrAtt; PrYds += o.PrYds; RetTd += o.RetTd;
            Penalties += o.Penalties; PenaltyYds += o.PenaltyYds;
            TwoPtMade += o.TwoPtMade; Safeties += o.Safeties;
        }
    }

    [Serializable]
    public class SeasonStats : StatLine
    {
        public int Season;
        public int TeamId;
        public bool Postseason;
    }

    [Serializable]
    public class Player
    {
        public PlayerBio Bio = new PlayerBio();
        public Position Position;
        public Archetype Archetype;
        public Ratings Ratings;
        /// <summary>Hidden ceiling for the player's overall rating (40-99).</summary>
        public int Potential;
        public DevTrait Dev;
        public Contract Contract = new Contract();
        public InjuryState Injury = new InjuryState();
        /// <summary>0-100.</summary>
        public int Morale = 70;
        public int TeamId = -1;
        /// <summary>Seasons in the pro league (0 = rookie) or college seasons played.</summary>
        public int Experience;
        public ClassYear Class = ClassYear.Senior;
        public bool RedshirtUsed;
        public bool IsRedshirting;
        /// <summary>Recruiting star rating 1-5 (college).</summary>
        public int Stars;
        public int DraftYear;
        public int DraftRound;
        public int DraftPick;
        public bool Retired;
        public List<SeasonStats> Career = new List<SeasonStats>();
        /// <summary>College seasons of a player who turned pro (v0.9.0 College Network): moved out of <see cref="Career"/> when the draft class is made so pro totals, records and the Hall of Fame count only pro seasons. <see cref="SeasonStats.TeamId"/> is the school id.</summary>
        public List<SeasonStats> CollegeCareer = new List<SeasonStats>();
        /// <summary>Special abilities (v0.8.0 career layer, see <see cref="AbilityCatalog"/>); empty for players that never got any.</summary>
        public List<PlayerAbility> Abilities = new List<PlayerAbility>();

        public int Id { get { return Bio.Id; } }
        public string Name { get { return Bio.FullName; } }
        public int Age { get { return Bio.Age; } }
        public int Overall { get { return Ratings.Overall(Position); } }
        public bool IsAvailable { get { return !Injury.IsInjured && !Retired; } }
        public bool IsRookie { get { return Experience == 0 && DraftYear > 0; } }

        /// <summary>Stats row for the given season (created on demand).</summary>
        public SeasonStats StatsFor(int season, int teamId, bool postseason = false)
        {
            for (int i = Career.Count - 1; i >= 0; i--)
            {
                var s = Career[i];
                if (s.Season == season && s.TeamId == teamId && s.Postseason == postseason) return s;
            }
            var created = new SeasonStats { Season = season, TeamId = teamId, Postseason = postseason };
            Career.Add(created);
            return created;
        }

        public SeasonStats FindStats(int season)
        {
            for (int i = Career.Count - 1; i >= 0; i--)
                if (Career[i].Season == season && !Career[i].Postseason) return Career[i];
            return null;
        }

        public StatLine CareerTotals()
        {
            var t = new StatLine();
            for (int i = 0; i < Career.Count; i++) if (!Career[i].Postseason) t.Add(Career[i]);
            return t;
        }

        public override string ToString() { return Position + " " + Name + " (" + Overall + ")"; }
    }

    [Serializable]
    public class Coach
    {
        public int Id;
        public string Name;
        public CoachRole Role;
        public int Age;
        /// <summary>Quality of the play design/calling (40-99).</summary>
        public int OffenseRating;
        public int DefenseRating;
        /// <summary>Boosts youth progression (40-99).</summary>
        public int Development;
        /// <summary>Scouting / recruiting skill (40-99).</summary>
        public int Scouting;
        /// <summary>0-100: willingness to go for it on 4th down and gamble.</summary>
        public int Aggression;
        /// <summary>0-100: pass-heavy preference (50 = balanced).</summary>
        public int PassTendency;
        public OffenseScheme OffScheme;
        public DefenseScheme DefScheme;
        public int Prestige;
    }

    [Serializable]
    public class DepthSlot
    {
        public Position Pos;
        public List<int> PlayerIds = new List<int>();
    }

    [Serializable]
    public class DepthChart
    {
        public List<DepthSlot> Slots = new List<DepthSlot>();

        public List<int> Get(Position p)
        {
            for (int i = 0; i < Slots.Count; i++) if (Slots[i].Pos == p) return Slots[i].PlayerIds;
            var s = new DepthSlot { Pos = p };
            Slots.Add(s);
            return s.PlayerIds;
        }

        public bool TryGet(Position p, out List<int> ids)
        {
            for (int i = 0; i < Slots.Count; i++) if (Slots[i].Pos == p) { ids = Slots[i].PlayerIds; return true; }
            ids = null;
            return false;
        }

        public int StarterId(Position p)
        {
            List<int> ids;
            return TryGet(p, out ids) && ids.Count > 0 ? ids[0] : 0;
        }
    }

    [Serializable]
    public class Team
    {
        public int Id;
        public string City;
        public string Nickname;
        public string Abbreviation;
        /// <summary>Hex strings like "#12AB34".</summary>
        public string PrimaryColor = "#000000";
        public string SecondaryColor = "#FFFFFF";
        public string TertiaryColor = "#FFFFFF";
        /// <summary>Data-file id (e.g. "buffalo_ironhorns"); empty for generated teams.</summary>
        public string Key = "";
        /// <summary>Logo asset key (e.g. "ProV6/buffalo_ironhorns"); empty when none.</summary>
        public string Logo = "";
        public int ConferenceId = -1;
        public int DivisionId = -1;
        public bool IsCollege;
        public string StadiumName = "";
        /// <summary>True when the stadium is a dome (decided by the city climate, see LeagueFactory.IsDomeCity): the weather system should not rain or snow on a dome game.</summary>
        public bool IsDome { get { return StadiumName != null && StadiumName.EndsWith(" Dome"); } }
        public List<Player> Roster = new List<Player>();
        /// <summary>Practice squad (v0.8.0 career layer): under contract and on the cap, never plays in simulated games.</summary>
        public List<Player> PracticeSquad = new List<Player>();
        /// <summary>Injured reserve (v0.8.0 career layer): off the active roster, on the cap, heals every week.</summary>
        public List<Player> InjuredReserve = new List<Player>();
        public DepthChart Depth = new DepthChart();
        public List<Coach> Staff = new List<Coach>();
        public long SalaryCap = LeagueRules.SalaryCap;
        public long DeadCap;
        /// <summary>Dead money from this offseason's cuts that lands on next season's cap (QA-561).</summary>
        public long DeadCapNext;
        /// <summary>Annual scouting points to spend on draft prospects.</summary>
        public int ScoutingPoints = 400;
        public int Prestige = 3;

        [NonSerialized] private Dictionary<int, Player> _index;
        [NonSerialized] private int _indexCount = -1;

        public string FullName { get { return City + " " + Nickname; } }
        public override string ToString() { return FullName; }

        public long CapUsed
        {
            get
            {
                long t = DeadCap;
                for (int i = 0; i < Roster.Count; i++) t += Roster[i].Contract.CapHit;
                for (int i = 0; i < PracticeSquad.Count; i++) t += PracticeSquad[i].Contract.CapHit;
                for (int i = 0; i < InjuredReserve.Count; i++) t += InjuredReserve[i].Contract.CapHit;
                return t;
            }
        }

        public long CapSpace { get { return SalaryCap - CapUsed; } }

        public Coach HeadCoach { get { return FindCoach(CoachRole.HeadCoach); } }
        public Coach OffensiveCoordinator { get { return FindCoach(CoachRole.OffensiveCoordinator); } }
        public Coach DefensiveCoordinator { get { return FindCoach(CoachRole.DefensiveCoordinator); } }

        public Coach FindCoach(CoachRole role)
        {
            for (int i = 0; i < Staff.Count; i++) if (Staff[i].Role == role) return Staff[i];
            return null;
        }

        public Player GetPlayer(int id)
        {
            if (_index == null || _indexCount != Roster.Count) RebuildIndex();
            Player p;
            if (_index.TryGetValue(id, out p) && p != null)
            {
                return p;
            }
            // roster may have been mutated in place (swap); rebuild once and retry
            RebuildIndex();
            return _index.TryGetValue(id, out p) ? p : null;
        }

        /// <summary>Finds a player on the active roster, practice squad or injured reserve (null when he is not under contract here).</summary>
        public Player FindAnyPlayer(int id)
        {
            Player p = GetPlayer(id);
            if (p != null) return p;
            for (int i = 0; i < PracticeSquad.Count; i++) if (PracticeSquad[i].Id == id) return PracticeSquad[i];
            for (int i = 0; i < InjuredReserve.Count; i++) if (InjuredReserve[i].Id == id) return InjuredReserve[i];
            return null;
        }

        public void AddPlayer(Player p)
        {
            Roster.Add(p);
            p.TeamId = Id;
            _indexCount = -1;
        }

        public bool RemovePlayer(Player p)
        {
            bool ok = Roster.Remove(p);
            _indexCount = -1;
            return ok;
        }

        /// <summary>Call after mutating <see cref="Roster"/> directly.</summary>
        public void InvalidateIndex() { _indexCount = -1; }

        public void RebuildIndex()
        {
            if (_index == null) _index = new Dictionary<int, Player>(); else _index.Clear();
            for (int i = 0; i < Roster.Count; i++) _index[Roster[i].Id] = Roster[i];
            _indexCount = Roster.Count;
        }

        public int CountAt(Position p)
        {
            int n = 0;
            for (int i = 0; i < Roster.Count; i++) if (Roster[i].Position == p) n++;
            return n;
        }

        /// <summary>Weighted team rating (0-99) built from the depth chart starters.</summary>
        public int TeamOverall()
        {
            return TeamRating.Compute(this);
        }
    }

    [Serializable]
    public class Division
    {
        public int Id;
        public string Name;
        public int ConferenceId;
        public List<int> TeamIds = new List<int>();
    }

    [Serializable]
    public class Conference
    {
        public int Id;
        public string Name;
        public string Abbreviation;
        public bool IsCollege;
        public string Key = "";
        public string Logo = "";
        public List<Division> Divisions = new List<Division>();
        /// <summary>Every member team id (also listed in the divisions when divisions exist).</summary>
        public List<int> TeamIds = new List<int>();
    }

    public enum Region { Northeast, Southeast, Midwest, Southwest, West, Mountain, Pacific, Plains }

    [Serializable]
    public class School
    {
        public int Id;
        public string Name;
        public string ShortName;
        public string Key = "";
        public string Logo = "";
        public string Mascot;
        public string Abbreviation;
        public string PrimaryColor = "#000000";
        public string SecondaryColor = "#FFFFFF";
        /// <summary>Conference id or -1 for independents.</summary>
        public int ConferenceId = -1;
        /// <summary>Prestige in stars, 1-6.</summary>
        public int Prestige = 3;
        /// <summary>Continuous prestige used to drift the star rating over the years (0-100).</summary>
        public float PrestigePoints = 50f;
        /// <summary>Prestige on the shared 1-5 star scale (recruit stars and pro teams use 1-5): the sixth, elite tier shows as 5 stars; use <see cref="PrestigeLabel"/> to name it (QA-613).</summary>
        public int PrestigeStars { get { return Prestige > 5 ? 5 : (Prestige < 1 ? 1 : Prestige); } }
        /// <summary>Text for the prestige tier: "Elite" for the sixth tier, otherwise "n-star".</summary>
        public string PrestigeLabel { get { return Prestige >= 6 ? "Elite" : PrestigeStars + "-star"; } }
        public Region Region;
        public string City = "";
        public string State = "";
        public string StadiumName = "";
        public int StadiumCapacity = 50000;
        public Team Team = new Team();

        public bool Independent { get { return ConferenceId < 0; } }
        public override string ToString() { return Name; }
    }

    /// <summary>Global constants for the pro league economy.</summary>
    public static class LeagueRules
    {
        public const long SalaryCap = 255000000L;
        public const long MinimumSalary = Contract.MinSalary;
        public const int RosterSize = 53;
        public const int ProGames = 17;
        public const int CollegeGames = 12;
        public const int CollegeRosterSize = 85;
        public const int DraftRounds = 7;

        /// <summary>Roster composition (sums to 53).</summary>
        public static readonly Position[] RosterOrder =
        {
            Position.QB, Position.RB, Position.FB, Position.WR, Position.TE,
            Position.LT, Position.LG, Position.C, Position.RG, Position.RT,
            Position.LE, Position.DT, Position.RE, Position.LOLB, Position.MLB, Position.ROLB,
            Position.CB, Position.FS, Position.SS, Position.K, Position.P
        };

        public static int ProSlots(Position p)
        {
            switch (p)
            {
                case Position.QB: return 3;
                case Position.RB: return 4;
                case Position.FB: return 1;
                case Position.WR: return 6;
                case Position.TE: return 3;
                case Position.LT: return 2;
                case Position.LG: return 2;
                case Position.C: return 2;
                case Position.RG: return 2;   // QA-576: a like-for-like backup at every line spot (OL 2/2/2/2/2 = 10)
                case Position.RT: return 2;
                case Position.LE: return 2;
                case Position.DT: return 4;
                case Position.RE: return 2;
                case Position.LOLB: return 2;
                case Position.MLB: return 2;  // QA-576: one LB slot moves to the line so the roster still sums to 53
                case Position.ROLB: return 2;
                case Position.CB: return 6;
                case Position.FS: return 2;
                case Position.SS: return 2;
                case Position.K: return 1;
                case Position.P: return 1;
            }
            return 0;
        }

        public static int CollegeSlots(Position p)
        {
            switch (p)
            {
                case Position.QB: return 4;
                case Position.RB: return 6;
                case Position.FB: return 2;
                case Position.WR: return 10;
                case Position.TE: return 5;
                case Position.LT: return 3;
                case Position.LG: return 3;
                case Position.C: return 3;
                case Position.RG: return 3;
                case Position.RT: return 3;
                case Position.LE: return 4;
                case Position.DT: return 6;
                case Position.RE: return 4;
                case Position.LOLB: return 3;
                case Position.MLB: return 5;
                case Position.ROLB: return 3;
                case Position.CB: return 9;
                case Position.FS: return 3;
                case Position.SS: return 4;
                case Position.K: return 1;
                case Position.P: return 1;
            }
            return 0;
        }

        /// <summary>Number of starters the depth chart needs at each position.</summary>
        public static int Starters(Position p)
        {
            switch (p)
            {
                case Position.WR: return 4;   // QA-577: Empty fields four wide, Ace/Goal Line two tight ends, Dime four corners
                case Position.TE: return 2;
                case Position.DT: return 2;
                case Position.CB: return 4;
                default: return 1;
            }
        }
    }

    public enum PositionBucket { QB, RB, WR, TE, OL, DL, LB, CB, S, K, P, FB }

    public static class PositionBuckets
    {
        public static PositionBucket Of(Position p)
        {
            switch (p)
            {
                case Position.QB: return PositionBucket.QB;
                case Position.RB: return PositionBucket.RB;
                case Position.FB: return PositionBucket.FB;
                case Position.WR: return PositionBucket.WR;
                case Position.TE: return PositionBucket.TE;
                case Position.LT: case Position.LG: case Position.C: case Position.RG: case Position.RT: return PositionBucket.OL;
                case Position.LE: case Position.DT: case Position.RE: return PositionBucket.DL;
                case Position.LOLB: case Position.MLB: case Position.ROLB: return PositionBucket.LB;
                case Position.CB: return PositionBucket.CB;
                case Position.FS: case Position.SS: return PositionBucket.S;
                case Position.K: return PositionBucket.K;
                default: return PositionBucket.P;
            }
        }

        public static readonly PositionBucket[] All =
        {
            PositionBucket.QB, PositionBucket.RB, PositionBucket.FB, PositionBucket.WR, PositionBucket.TE, PositionBucket.OL,
            PositionBucket.DL, PositionBucket.LB, PositionBucket.CB, PositionBucket.S, PositionBucket.K, PositionBucket.P
        };

        /// <summary>Pro roster count for a bucket (sums to 53).</summary>
        public static int ProCount(PositionBucket b)
        {
            switch (b)
            {
                case PositionBucket.QB: return 3;
                case PositionBucket.RB: return 4;
                case PositionBucket.FB: return 1;
                case PositionBucket.WR: return 6;
                case PositionBucket.TE: return 3;
                case PositionBucket.OL: return 10;
                case PositionBucket.DL: return 8;
                case PositionBucket.LB: return 6;
                case PositionBucket.CB: return 6;
                case PositionBucket.S: return 4;
                case PositionBucket.K: return 1;
                default: return 1;
            }
        }
    }
}
