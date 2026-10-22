using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    // ------------------------------------------------------------------------------------------
    // v0.8.0 "Franchise" (Career Mode) data model.
    // Same serialization rules as LeagueModel.cs: [Serializable], public fields, List<T> and integer ids only
    // (no dictionaries, no references between objects, no nullable value types) so Unity's JsonUtility can save and load it.
    // Runtime caches are [NonSerialized].
    // ------------------------------------------------------------------------------------------

    /// <summary>Where the franchise calendar stands. The order of the members is the order of the year.</summary>
    public enum CareerPhase
    {
        /// <summary>Training camp is over, final cuts are due: the new season exists but week 1 has not been played.</summary>
        Preseason,
        RegularSeason,
        Playoffs,
        PixBowl,
        /// <summary>Season over: awards, All-Pro teams, trophy case, legacy.</summary>
        Awards,
        Retirements,
        /// <summary>New league year: contracts tick, cap grows, expiring players can be re-signed or franchise tagged.</summary>
        ReSign,
        /// <summary>Combine and scouting: spend scouting points on the draft class.</summary>
        Scouting,
        Draft,
        RookieSigning,
        FreeAgency,
        /// <summary>Progression, aging and ability changes are applied; staff can be changed.</summary>
        TrainingCamp
    }

    public enum TransactionKind
    {
        Signing, ReSigning, FranchiseTag, Release, Waived, WaiverClaim, Trade, DraftPick, RookieSigned, PracticeSquad, Promoted,
        InjuredReserve, ActivatedFromIR, Retirement, CoachHired, CoachFired, Extension, Restructure
    }

    public enum NewsKind
    {
        Signing, ReSigning, Trade, Injury, Record, Upset, Award, Retirement, Draft, Release, Coach, Milestone, HallOfFame, Title, Rumor, Season, Owner
    }

    /// <summary>Result of a management action: <see cref="Ok"/> plus a message the UI can show verbatim.</summary>
    public sealed class OpResult
    {
        public bool Ok;
        public string Message = "";
        public OpResult(bool ok, string message) { Ok = ok; Message = message ?? ""; }
        public static OpResult Success(string message = "") { return new OpResult(true, message); }
        public static OpResult Fail(string message) { return new OpResult(false, message); }
        public override string ToString() { return (Ok ? "OK: " : "FAILED: ") + Message; }
    }

    [Serializable]
    public class CareerSettings
    {
        /// <summary>The user's depth chart is rebuilt after injuries and roster moves (positions edited by hand stay as they are).</summary>
        public bool AutoDepthChart = true;
        /// <summary>The user's long-term injured players go to injured reserve on their own, like a CPU team.</summary>
        public bool AutoInjuredReserve = false;
        /// <summary>Last regular-season week in which trades are allowed.</summary>
        public int TradeDeadlineWeek = 9;
        /// <summary>Length of the free agency bidding period in days.</summary>
        public int FreeAgencyDays = 14;
        /// <summary>Years a player must be retired before the Hall of Fame can vote on him.</summary>
        public int HofWaitYears = 2;
        public bool InjuriesEnabled = true;
        /// <summary>Simulate the college world alongside the pro season (needs a CollegeWorld in the league save).</summary>
        public bool SimCollege = true;
    }

    [Serializable]
    public class DraftPickAsset
    {
        public int Id;
        public int Year;
        public int Round;
        public int OriginalTeamId;
        public int OwnerTeamId;
        /// <summary>Overall pick number once the draft order is known (0 before).</summary>
        public int Overall;
        /// <summary>Player taken with the pick (0 until used).</summary>
        public int PlayerId;
        public bool Used;
    }

    [Serializable]
    public class RookieRight
    {
        public int PlayerId;
        public int TeamId;
        public int Overall;
        public int Round;
        public int Year;
        public bool Signed;
    }

    public enum NegotiationState { Open, Signed, Declined, Walked }

    [Serializable]
    public class Negotiation
    {
        public int PlayerId;
        public int TeamId;
        public int Year;
        public int Rounds;
        public long LastCounterAav;
        public int LastCounterYears;
        public NegotiationState State;
    }

    [Serializable]
    public class FaOffer
    {
        public int Id;
        public int PlayerId;
        public int TeamId;
        public long Aav;
        public int Years;
        /// <summary>Signing bonus as a fraction of the total contract value (0-0.5).</summary>
        public float BonusPct;
        public int DayPlaced;
    }

    [Serializable]
    public class FaSigning
    {
        public int Year;
        public int Day;
        public int PlayerId;
        public string PlayerName;
        public Position Pos;
        public int Ovr;
        public int Age;
        public int TeamId;
        public int FromTeamId;
        public long Aav;
        public int Years;
    }

    [Serializable]
    public class WaiverEntry
    {
        public Player Player;
        public int FromTeamId;
        public int Year;
        public int Week;
        /// <summary>Dead cap the release put on the former team (returned when somebody claims the player).</summary>
        public long DeadNow, DeadNext;
        /// <summary>Teams that put in a claim (the user's claims; CPU claims are decided when the waivers clear).</summary>
        public List<int> ClaimTeamIds = new List<int>();
        public int PlayerId { get { return Player != null ? Player.Id : 0; } }
    }

    [Serializable]
    public class TradeAsset
    {
        public bool IsPick;
        public int PlayerId;
        public int PickId;

        public static TradeAsset OfPlayer(int id) { return new TradeAsset { PlayerId = id }; }
        public static TradeAsset OfPick(int pickId) { return new TradeAsset { IsPick = true, PickId = pickId }; }
    }

    /// <summary>Team A sends <see cref="FromA"/> to team B and team B sends <see cref="FromB"/> to team A.</summary>
    [Serializable]
    public class TradeProposal
    {
        public int TeamA, TeamB;
        public List<TradeAsset> FromA = new List<TradeAsset>();
        public List<TradeAsset> FromB = new List<TradeAsset>();
    }

    public enum TradeOfferStatus { Pending, Accepted, Rejected, Expired }

    /// <summary>A CPU team's offer to the user (shown in the trade inbox).</summary>
    [Serializable]
    public class TradeOffer
    {
        public int Id;
        /// <summary>The CPU team that made the offer (it is team A of the proposal).</summary>
        public int FromTeamId;
        public int ToTeamId;
        public int Year;
        public int ExpiresWeek;
        public string Message = "";
        public TradeOfferStatus Status;
        public TradeProposal Proposal = new TradeProposal();
    }

    [Serializable]
    public class TransactionRecord
    {
        public int Id;
        public int Year;
        public int Week;
        public CareerPhase Phase;
        public TransactionKind Kind;
        public int TeamId;
        public int OtherTeamId = -1;
        public int PlayerId;
        public string PlayerName = "";
        public long Amount;
        public string Text = "";
    }

    [Serializable]
    public class NewsItem
    {
        public int Id;
        public int Year;
        public int Week;
        public CareerPhase Phase;
        public NewsKind Kind;
        public string Headline = "";
        public string Body = "";
        public List<int> TeamIds = new List<int>();
        public List<int> PlayerIds = new List<int>();
        /// <summary>1 (minor) to 5 (league-shaking); the ticker favours high priorities.</summary>
        public int Priority = 2;
        /// <summary>The item involves the user's team.</summary>
        public bool Mine;
    }

    [Serializable]
    public class InjuryEvent
    {
        public int Year;
        public int Week;
        public int PlayerId;
        public string Name = "";
        public int TeamId;
        public Position Pos;
        public string Type = "";
        public int Weeks;
        public bool Starter;
        public bool PlacedOnIr;
    }

    [Serializable]
    public class CoachCandidate
    {
        public Coach Coach;
        public long AskSalary;
        public int Years;
    }

    [Serializable]
    public class StaffContract
    {
        public int TeamId;
        public int CoachId;
        public long Salary;
        public int YearsLeft;
    }

    [Serializable]
    public class TeamMorale
    {
        public int TeamId;
        public int Value = 65;
        public int Trend;
    }

    [Serializable]
    public class ManualDepth
    {
        public int TeamId;
        public Position Pos;
    }

    [Serializable]
    public class TagUse
    {
        public int TeamId;
        public int Year;
        public int PlayerId;
    }

    [Serializable]
    public class PriorTeam
    {
        public int PlayerId;
        public int TeamId;
    }

    [Serializable]
    public class CampResult
    {
        public int PlayerId;
        public int TeamId;
        public string Name = "";
        public Position Pos;
        public int Age;
        public int OvrBefore, OvrAfter;
        public string AbilityChange = "";
    }

    // -------------------------------------------------------------- history
    public enum PlayoffResult { Missed, WildCard, Divisional, ConferenceFinal, RunnerUp, Champion }

    [Serializable]
    public class TeamSeasonRecord
    {
        public int Year;
        public int TeamId;
        public int Wins, Losses, Ties;
        public int PointsFor, PointsAgainst;
        public int ConferenceSeed;
        public bool DivisionTitle;
        public bool ConferenceTitle;
        public PlayoffResult Result;
        public int TeamOvr;
        public int CoachId;
        public string MvpName = "";
    }

    public enum TrophyKind { PixBowl, ConferenceTitle, DivisionTitle, PerfectRegularSeason }

    [Serializable]
    public class TrophyEntry
    {
        public int Year;
        public int TeamId;
        public TrophyKind Kind;
        public string Opponent = "";
        public string Score = "";
        public string BowlMvp = "";
        public int BowlMvpId;
        public string Coach = "";
    }

    [Serializable]
    public class AllProSelection
    {
        public Position Pos;
        public int PlayerId;
        public string Name = "";
        public int TeamId;
        public float Score;
    }

    [Serializable]
    public class AllProTeam
    {
        public int Year;
        /// <summary>1 = first team, 2 = second team.</summary>
        public int Team;
        public List<AllProSelection> Players = new List<AllProSelection>();
    }

    public enum AccoladeKind { AllPro1, AllPro2, Mvp, Opoy, Dpoy, Roy, Champion, PixBowlMvp }

    [Serializable]
    public class Accolade
    {
        public int PlayerId;
        public int Year;
        public int TeamId;
        public AccoladeKind Kind;
    }

    [Serializable]
    public class RetiredRecord
    {
        public int PlayerId;
        public int Year;
        public int TeamId;
        public bool Inducted;
        public bool Considered;
    }

    [Serializable]
    public class HofEntry
    {
        public int PlayerId;
        public string Name = "";
        public Position Pos;
        public int InductionYear;
        public int RetiredYear;
        public int DraftYear;
        public int Seasons;
        public float Score;
        public int TeamId;
        public string Teams = "";
        public string Summary = "";
        public string Accolades = "";
    }

    [Serializable]
    public class TeamRecordEntry
    {
        public string Category = "";
        public int TeamId;
        public string TeamName = "";
        public int Year;
        public int Value;
    }

    [Serializable]
    public class OwnerGoal
    {
        public int Year;
        public string Id = "";
        public string Text = "";
        public int Target;
        public int Actual;
        public bool Resolved;
        public bool Met;
        public int Weight = 1;
    }

    // -------------------------------------------------------------- draft
    [Serializable]
    public class DraftSlot
    {
        public int PickId;
        public int Overall;
        public int Round;
        public int PickInRound;
        public int OriginalTeamId;
        public int TeamId;
        public int PlayerId;
        public string PlayerName = "";
        public Position Pos;
        public int ConsensusRank;
        public string Grade = "";
        public bool Made;
    }

    [Serializable]
    public class DraftState
    {
        public int Year;
        public bool Built;
        public bool Finished;
        public int Current;
        public List<DraftSlot> Slots = new List<DraftSlot>();
        /// <summary>The user's starred prospects (their personal big board), best first.</summary>
        public List<int> BigBoard = new List<int>();
    }

    // -------------------------------------------------------------- the save root
    /// <summary>
    /// Root object of a Career (franchise) save: serialize with JsonUtility. Wraps the existing <see cref="LeagueSave"/> and adds
    /// everything the franchise layer needs. All services in the Career folder are static and take a <see cref="CareerSave"/>.
    /// </summary>
    [Serializable]
    public class CareerSave
    {
        public int Version = 1;
        public string Name = "PIX Football Career";
        public int Seed;
        public int StartYear;
        public LeagueSave League;
        public int UserTeamId;
        public CareerPhase Phase = CareerPhase.Preseason;
        public CareerSettings Settings = new CareerSettings();

        /// <summary>Salary cap of <see cref="StartYear"/>; it grows by <see cref="CapGrowth"/> every year.</summary>
        public long CapBase = LeagueRules.SalaryCap;
        public double CapGrowth = 0.05;

        public long RngS0, RngS1;
        public int NextEntityId = 1;

        // roster / contracts
        public List<DraftPickAsset> Picks = new List<DraftPickAsset>();
        public List<RookieRight> RookieRights = new List<RookieRight>();
        public List<Negotiation> Negotiations = new List<Negotiation>();
        public List<TagUse> TagUses = new List<TagUse>();
        public List<PriorTeam> PriorTeams = new List<PriorTeam>();
        public List<WaiverEntry> Waivers = new List<WaiverEntry>();
        public List<ManualDepth> ManualDepth = new List<ManualDepth>();

        // free agency
        public int FaDay;
        public bool FaOpen;
        public List<FaOffer> FaOffers = new List<FaOffer>();
        public List<FaSigning> FaLog = new List<FaSigning>();

        // trades
        public List<TradeOffer> Inbox = new List<TradeOffer>();

        // staff and morale
        public List<CoachCandidate> CoachPool = new List<CoachCandidate>();
        public List<StaffContract> StaffContracts = new List<StaffContract>();
        public List<TeamMorale> Morale = new List<TeamMorale>();

        // draft
        public DraftState Draft = new DraftState();

        // logs
        public List<TransactionRecord> Transactions = new List<TransactionRecord>();
        public List<NewsItem> News = new List<NewsItem>();
        public List<InjuryEvent> InjuryLog = new List<InjuryEvent>();
        public List<CampResult> LastCamp = new List<CampResult>();
        /// <summary>Box scores of the user's own games (newest last, at most <see cref="MaxStoredBoxes"/>), kept in the save so the schedule and the hub can show them after a reload.</summary>
        public List<StoredBox> UserBoxes = new List<StoredBox>();
        public const int MaxStoredBoxes = 40;


        // history
        public List<TeamSeasonRecord> TeamSeasons = new List<TeamSeasonRecord>();
        public List<TrophyEntry> Trophies = new List<TrophyEntry>();
        public List<AllProTeam> AllPro = new List<AllProTeam>();
        public List<Accolade> Accolades = new List<Accolade>();
        public List<RetiredRecord> Retired = new List<RetiredRecord>();
        public List<HofEntry> HallOfFame = new List<HofEntry>();
        public List<TeamRecordEntry> TeamRecords = new List<TeamRecordEntry>();
        public List<OwnerGoal> Goals = new List<OwnerGoal>();
        public int OwnerSatisfaction = 60;

        [NonSerialized] public List<List<Prospect>> DraftBoards;
        /// <summary>Results of games played or simulated one by one this week, before the week was advanced (reported in the week report, not saved).</summary>
        [NonSerialized] public List<GameResult> PendingResults = new List<GameResult>();
        [NonSerialized] public int DraftBoardsKey;

        /// <summary>PIX Bowl most valuable player of the season just played (set when the final is simulated).</summary>
        public int BowlMvpId;
        public string BowlMvpName = "";
        public int BowlMvpTeamId = -1;
        public string BowlScoreText = "";

        /// <summary>Records seen in the last record-book snapshot (category|scope|value), used to detect broken records.</summary>
        public List<string> RecordSnapshot = new List<string>();

        // ---------------------------------------------------------------- convenience
        public ProLeague Pro { get { return League != null ? League.Pro : null; } }
        /// <summary>The college world, or null when the career has none (JsonUtility loads a missing world as an empty one, so an empty world counts as none).</summary>
        public CollegeWorld College { get { return League != null && League.College != null && League.College.Schools.Count > 0 ? League.College : null; } }
        public Season Season { get { return Pro != null ? Pro.Season : null; } }
        public int Year { get { return Pro != null ? Pro.Year : StartYear; } }
        public Team UserTeam { get { return Pro.GetTeam(UserTeamId); } }
        public bool IsUser(int teamId) { return teamId == UserTeamId; }

        public int NextId() { return NextEntityId++; }

        /// <summary>
        /// Hands out an independent random stream and advances the persistent career generator, so every operation is deterministic
        /// for a given save and seed no matter how many other operations ran in between.
        /// </summary>
        public Rng NewRng()
        {
            var master = new Rng(0UL);
            master.SetState(unchecked((ulong)RngS0), unchecked((ulong)RngS1));
            Rng child = master.Fork();
            ulong a, b;
            master.GetState(out a, out b);
            RngS0 = unchecked((long)a); RngS1 = unchecked((long)b);
            return child;
        }

        public void SeedRng(int seed)
        {
            var r = new Rng(seed ^ 0x5F3759DF);
            ulong a, b;
            r.GetState(out a, out b);
            RngS0 = unchecked((long)a); RngS1 = unchecked((long)b);
        }

        /// <summary>Re-links the runtime caches after loading from disk.</summary>
        public void Bind()
        {
            if (League != null) League.Bind();
        }

        /// <summary>Finds a player anywhere: rosters, practice squads, injured reserve, free agents, waivers, draft class, retired.</summary>
        public Player FindPlayer(int id)
        {
            Player p = Pro != null ? Pro.FindPlayer(id) : null;
            if (p != null) return p;
            for (int i = 0; i < Waivers.Count; i++) if (Waivers[i].PlayerId == id) return Waivers[i].Player;
            return null;
        }

        /// <summary>The team that currently has the player under contract (active roster, practice squad or IR), or -1.</summary>
        public int TeamOf(int playerId)
        {
            for (int t = 0; t < Pro.Teams.Count; t++) if (Pro.Teams[t].FindAnyPlayer(playerId) != null) return t;
            return -1;
        }
    }
}

namespace PixFootball.League
{
    /// <summary>One stored box score of a user game (season year and week make the key; the play-by-play text is not kept to keep the save small).</summary>
    [System.Serializable]
    public class StoredBox
    {
        public int Year, Week;
        public GameResult Result;
    }
}
