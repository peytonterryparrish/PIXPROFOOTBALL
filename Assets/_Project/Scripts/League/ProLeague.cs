using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    [Serializable]
    public class SeasonSummary
    {
        public int Year;
        public int ChampionTeamId;
        public string ChampionName;
        public int RunnerUpTeamId;
        public int MvpPlayerId;
        public string MvpName;
        public int OpoyPlayerId;
        public int DpoyPlayerId;
        public int RoyPlayerId;
        public string Notes = "";
    }

    /// <summary>The pro league: 32 teams, free agents, the draft class and the current season.</summary>
    [Serializable]
    public class ProLeague : IPlayerIdSource
    {
        public string Name = "PIX Football League";
        public int Seed;
        public int Year;
        public int NextId = 1;
        /// <summary>Persistent RNG state so the league continues deterministically across saves.</summary>
        public long RngS0, RngS1;
        public List<Conference> Conferences = new List<Conference>();
        public List<Team> Teams = new List<Team>();
        public List<Player> FreeAgents = new List<Player>();
        public List<Player> RetiredPlayers = new List<Player>();
        public List<Prospect> DraftClass = new List<Prospect>();
        public List<int> DraftOrder = new List<int>();
        public List<DraftPickRecord> LastDraft = new List<DraftPickRecord>();
        public List<ScoutingReport> ScoutingReports = new List<ScoutingReport>();
        public Season Season;
        public RecordBook Records = new RecordBook();
        public List<SeasonSummary> History = new List<SeasonSummary>();

        public int NextPlayerId() { return NextId++; }

        public Team GetTeam(int id)
        {
            return (id >= 0 && id < Teams.Count && Teams[id].Id == id) ? Teams[id] : FindTeamSlow(id);
        }

        private Team FindTeamSlow(int id)
        {
            for (int i = 0; i < Teams.Count; i++) if (Teams[i].Id == id) return Teams[i];
            return null;
        }

        public Conference GetConference(int id)
        {
            for (int i = 0; i < Conferences.Count; i++) if (Conferences[i].Id == id) return Conferences[i];
            return null;
        }

        public Division GetDivision(int id)
        {
            for (int c = 0; c < Conferences.Count; c++)
                for (int d = 0; d < Conferences[c].Divisions.Count; d++)
                    if (Conferences[c].Divisions[d].Id == id) return Conferences[c].Divisions[d];
            return null;
        }

        public Player FindPlayer(int id)
        {
            for (int i = 0; i < Teams.Count; i++)
            {
                Player p = Teams[i].GetPlayer(id);
                if (p != null) return p;
            }
            for (int i = 0; i < FreeAgents.Count; i++) if (FreeAgents[i].Id == id) return FreeAgents[i];
            for (int i = 0; i < DraftClass.Count; i++) if (DraftClass[i].Player.Id == id) return DraftClass[i].Player;
            for (int i = 0; i < RetiredPlayers.Count; i++) if (RetiredPlayers[i].Id == id) return RetiredPlayers[i];
            return null;
        }

        public IEnumerable<Player> AllRosterPlayers()
        {
            for (int t = 0; t < Teams.Count; t++)
                for (int i = 0; i < Teams[t].Roster.Count; i++) yield return Teams[t].Roster[i];
        }

        public Rng GetRng()
        {
            var r = new Rng(0UL);
            r.SetState(unchecked((ulong)RngS0), unchecked((ulong)RngS1));
            return r;
        }

        public void SaveRng(Rng r)
        {
            ulong a, b;
            r.GetState(out a, out b);
            RngS0 = unchecked((long)a);
            RngS1 = unchecked((long)b);
        }

        /// <summary>Re-links runtime caches after loading from disk.</summary>
        public void Bind()
        {
            for (int i = 0; i < Teams.Count; i++) Teams[i].RebuildIndex();
            if (Season != null && !Season.IsEmpty) Season.Attach(this);
        }
    }

    [Serializable]
    public class CollegeWorld : IPlayerIdSource
    {
        public int Seed;
        public int Year;
        public int NextId = 1000000;
        public long RngS0, RngS1;
        public List<Conference> Conferences = new List<Conference>();
        public List<School> Schools = new List<School>();
        /// <summary>All 41 bowl games from the data file (6 of them are playoff-tier).</summary>
        public List<ConfBowlDef> Bowls = new List<ConfBowlDef>();
        public string PlayoffName = "Gridiron Crown Playoff";
        public string ChampionshipName = "Gridiron Crown Championship";
        public Season Season;
        public RecordBook Records = new RecordBook();
        public List<SeasonSummary> History = new List<SeasonSummary>();

        public int NextPlayerId() { return NextId++; }

        public School GetSchool(int id)
        {
            return (id >= 0 && id < Schools.Count && Schools[id].Id == id) ? Schools[id] : null;
        }

        public Conference GetConference(int id)
        {
            for (int i = 0; i < Conferences.Count; i++) if (Conferences[i].Id == id) return Conferences[i];
            return null;
        }

        public List<Team> TeamList()
        {
            var l = new List<Team>(Schools.Count);
            for (int i = 0; i < Schools.Count; i++) l.Add(Schools[i].Team);
            return l;
        }

        public Rng GetRng()
        {
            var r = new Rng(0UL);
            r.SetState(unchecked((ulong)RngS0), unchecked((ulong)RngS1));
            return r;
        }

        public void SaveRng(Rng r)
        {
            ulong a, b;
            r.GetState(out a, out b);
            RngS0 = unchecked((long)a);
            RngS1 = unchecked((long)b);
        }

        public void Bind()
        {
            for (int i = 0; i < Schools.Count; i++) Schools[i].Team.RebuildIndex();
            if (Season != null && !Season.IsEmpty) Season.Attach(this);
        }
    }

    /// <summary>Root object for saving: serialize with JsonUtility (public fields only).</summary>
    [Serializable]
    public class LeagueSave
    {
        public int Version = 1;
        public ProLeague Pro;
        public CollegeWorld College;

        public void Bind()
        {
            if (Pro != null) Pro.Bind();
            if (College != null) College.Bind();
        }

        public static LeagueSave Create(int seed, IReadOnlyList<SchoolDef> schools = null, IReadOnlyList<ConfBowlDef> confBowls = null)
        {
            var s = new LeagueSave { Pro = LeagueFactory.CreatePro(seed) };
            if (schools != null && schools.Count > 0) s.College = CollegeFactory.CreateWorld(seed ^ 0x5A5A5A, schools, confBowls);
            return s;
        }
    }
}
