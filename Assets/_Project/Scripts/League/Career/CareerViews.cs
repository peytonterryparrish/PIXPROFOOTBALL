using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    // View models for the franchise screens: plain data the UI binds to (nothing here changes the save).

    public sealed class TeamRatingsView
    {
        public int TeamId;
        public string Name = "";
        public int Ovr, Off, Def, SpecialTeams;
    }

    public sealed class NextGameView
    {
        public ScheduledGame Game;
        public int Week;
        public string Label = "";
        public bool Home;
        public int OpponentId;
        public string OpponentName = "";
        public string OpponentRecord = "";
        public TeamRatingsView You, Them;
        public bool Bye;
    }

    public sealed class HubHome
    {
        public string PhaseLabel = "";
        public CareerPhase Phase;
        public int Year;
        public string Record = "";
        public NextGameView Next;
        public List<StandingsRow> Division = new List<StandingsRow>();
        public List<NewsItem> Ticker = new List<NewsItem>();
        public List<OwnerGoal> Goals = new List<OwnerGoal>();
        public int OwnerSatisfaction;
        public string JobSecurity = "";
        public long CapSpace;
        public int PendingTradeOffers;
        public int InjuredStarters;
        public int TeamMorale;
        public int LegacyScore;
        public string LegacyTier = "";
        public List<string> Blockers = new List<string>();
        public string NextStepLabel = "";
    }

    public sealed class ScheduleRow
    {
        public int Week;
        public string Label = "";
        public bool Home, Played, Bye, Won, Tie;
        public int OpponentId;
        public string Opponent = "";
        public int TeamScore, OpponentScore;
    }

    public sealed class PowerRankRow
    {
        public int Rank, TeamId;
        public string Name = "";
        public string Record = "";
        public int Ovr;
    }

    public sealed class RosterRow
    {
        public int PlayerId;
        public string Name = "";
        public Position Pos;
        public Archetype Archetype;
        public int Jersey, Age, Ovr, Potential;
        public RosterStatus Status;
        public long CapHit;
        public int YearsLeft;
        public bool Injured;
        public int InjuryWeeks;
        public int Morale;
        public int AbilityCount;
        public bool Starter;
    }

    public sealed class RatingView
    {
        public RatingKind Kind;
        public string Name = "";
        public int Value;
    }

    public sealed class AbilityView
    {
        public string Id = "", Name = "", Stars = "", Description = "", Effects = "";
        public int Tier;
    }

    public sealed class StatSeasonView
    {
        public int Year, TeamId, Games;
        public string Team = "";
        public string Line = "";
        public bool Postseason;
    }

    public sealed class PlayerCard
    {
        public int Id;
        public string Name = "";
        public Position Pos;
        public string Archetype = "";
        public int Age, HeightIn, WeightLb, Jersey, Overall, Potential;
        public string Height = "", College = "", Hand = "", DevTrait = "";
        public string TeamName = "";
        public RosterStatus Status;
        public List<RatingView> Ratings = new List<RatingView>();
        public List<AbilityView> Abilities = new List<AbilityView>();
        public int YearsLeft;
        public long Aav, CapHit, Guaranteed, DeadIfReleased, MarketValue;
        public bool Expiring;
        public string Draft = "";
        public bool Injured;
        public string InjuryType = "";
        public int InjuryWeeks;
        public PlayerMoraleInfo Morale;
        public float TradeValue;
        public List<StatSeasonView> Seasons = new List<StatSeasonView>();
        public List<string> Honors = new List<string>();
        /// <summary>v0.9.0: the college seasons of a player who came through the College Network (team = the school).</summary>
        public List<StatSeasonView> CollegeSeasons = new List<StatSeasonView>();
    }

    public sealed class NeedRow
    {
        public Position Pos;
        public int Have, Target, StarterOvr;
        public float Need;
    }

    /// <summary>The data each franchise screen needs, assembled from the save. See Docs/Career.md for the screen to call map.</summary>
    public static class CareerViews
    {
        // ---------------------------------------------------------------- team strength
        public static TeamRatingsView Ratings(Team t)
        {
            var v = new TeamRatingsView { TeamId = t.Id, Name = t.FullName, Ovr = t.TeamOverall() };
            double off = 0, def = 0, st = 0; int no = 0, nd = 0, ns = 0;
            for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
            {
                Position pos = LeagueRules.RosterOrder[i];
                List<int> ids;
                if (!t.Depth.TryGet(pos, out ids)) continue;
                for (int k = 0; k < LeagueRules.Starters(pos) && k < ids.Count; k++)
                {
                    Player p = t.GetPlayer(ids[k]);
                    if (p == null) continue;
                    int o = p.Ratings.Overall(pos);
                    if (PositionGroup.IsOffense(pos)) { off += o; no++; } else if (PositionGroup.IsDefense(pos)) { def += o; nd++; } else { st += o; ns++; }
                }
            }
            v.Off = no == 0 ? 0 : (int)Math.Round(off / no);
            v.Def = nd == 0 ? 0 : (int)Math.Round(def / nd);
            v.SpecialTeams = ns == 0 ? 0 : (int)Math.Round(st / ns);
            return v;
        }

        private static string RecordOf(CareerSave s, int teamId)
        {
            if (s.Season == null || s.Season.IsEmpty) return "0-0";
            int w = s.Season.WinsOf(teamId), l = s.Season.LossesOf(teamId);
            int t = 0;
            for (int i = 0; i < s.Season.Games.Count; i++) { ScheduledGame g = s.Season.Games[i]; if (g.Kind == GameKind.Regular && g.Played && g.Involves(teamId) && g.Tie) t++; }
            return w + "-" + l + (t > 0 ? "-" + t : "");
        }

        // ---------------------------------------------------------------- hub
        public static HubHome Home(CareerSave s)
        {
            Team u = s.UserTeam;
            var h = new HubHome { Phase = s.Phase, Year = s.Year, PhaseLabel = CareerText.PhaseLabel(s), Record = RecordOf(s, u.Id) };
            h.Blockers = CareerFlow.Blockers(s);
            h.NextStepLabel = NextStepLabel(s);
            ScheduledGame g = CareerFlow.UserGame(s);
            if (CareerFlow.InSeason(s) || s.Phase == CareerPhase.Preseason)
            {
                var n = new NextGameView { You = Ratings(u) };
                if (s.Phase == CareerPhase.Preseason && s.Season != null)
                {
                    var first = s.Season.GamesOfTeam(u.Id);
                    g = first.Count > 0 ? first[0] : null;
                }
                if (g == null) n.Bye = CareerFlow.InSeason(s);
                else
                {
                    n.Game = g; n.Week = g.Week; n.Label = string.IsNullOrEmpty(g.Label) ? "Week " + g.Week : g.Label;
                    n.Home = g.HomeId == u.Id; n.OpponentId = g.OpponentOf(u.Id);
                    n.OpponentName = CareerText.TeamName(s, n.OpponentId); n.OpponentRecord = RecordOf(s, n.OpponentId);
                    n.Them = Ratings(s.Pro.GetTeam(n.OpponentId));
                }
                h.Next = n;
            }
            if (s.Season != null && !s.Season.IsEmpty) h.Division = s.Season.DivisionStandings(u.DivisionId);
            h.Ticker = NewsWriter.Ticker(s, 12);
            for (int i = 0; i < s.Goals.Count; i++) if (s.Goals[i].Year == s.Year) h.Goals.Add(s.Goals[i]);
            h.OwnerSatisfaction = s.OwnerSatisfaction; h.JobSecurity = OwnerEngine.JobSecurity(s);
            h.CapSpace = u.SalaryCap - u.CapUsed;
            h.PendingTradeOffers = TradeEngine.PendingOffers(s).Count;
            h.TeamMorale = MoraleEngine.TeamMorale(s, u.Id);
            for (int i = 0; i < u.Roster.Count; i++) if (u.Roster[i].Injury.IsInjured && Offseason.IsStarter(u, u.Roster[i])) h.InjuredStarters++;
            LegacyBreakdown lb = HistoryEngine.Legacy(s, u.Id);
            h.LegacyScore = lb.Score; h.LegacyTier = lb.Tier;
            return h;
        }

        /// <summary>The label of the big "continue" button.</summary>
        public static string NextStepLabel(CareerSave s)
        {
            switch (s.Phase)
            {
                case CareerPhase.Preseason: return "Start the season";
                case CareerPhase.RegularSeason: return CareerFlow.UserGame(s) != null ? "Play or sim week " + s.Season.Week : "Sim week " + s.Season.Week + " (bye)";
                case CareerPhase.Playoffs: return CareerFlow.UserGame(s) != null ? "Play or sim the " + CareerText.PlayoffRoundName(s.Season.PostseasonStage) : "Sim the " + CareerText.PlayoffRoundName(s.Season.PostseasonStage);
                case CareerPhase.PixBowl: return "Sim the PIX Bowl";
                case CareerPhase.Awards: return "Continue to retirements";
                case CareerPhase.Retirements: return "Open the re-sign window";
                case CareerPhase.ReSign: return "Close the re-sign window";
                case CareerPhase.Scouting: return "Start the draft";
                case CareerPhase.Draft: return CareerDraft.UserOnClock(s) ? "You are on the clock" : "Sim to your next pick";
                case CareerPhase.RookieSigning: return "Open free agency";
                case CareerPhase.FreeAgency: return FreeAgency.IsDone(s) ? "Go to training camp" : "Advance one day";
                case CareerPhase.TrainingCamp: return "Go to the preseason";
            }
            return "Continue";
        }

        // ---------------------------------------------------------------- schedule and standings
        public static List<ScheduleRow> Schedule(CareerSave s, int teamId)
        {
            var rows = new List<ScheduleRow>();
            if (s.Season == null || s.Season.IsEmpty) return rows;
            var games = s.Season.GamesOfTeam(teamId);
            int last = s.Season.RegularWeeks;
            int gi = 0;
            for (int w = 1; w <= last; w++)
            {
                if (gi < games.Count && games[gi].Week == w)
                {
                    ScheduledGame g = games[gi++];
                    bool home = g.HomeId == teamId;
                    var r = new ScheduleRow { Week = w, Label = "Week " + w, Home = home, Played = g.Played, OpponentId = g.OpponentOf(teamId), Opponent = CareerText.TeamName(s, g.OpponentOf(teamId)) };
                    if (g.Played) { r.TeamScore = home ? g.HomeScore : g.AwayScore; r.OpponentScore = home ? g.AwayScore : g.HomeScore; r.Won = g.WinnerId == teamId; r.Tie = g.Tie; }
                    rows.Add(r);
                }
                else rows.Add(new ScheduleRow { Week = w, Label = "Week " + w, Bye = true });
            }
            for (; gi < games.Count; gi++)
            {
                ScheduledGame g = games[gi];
                bool home = g.HomeId == teamId;
                var r = new ScheduleRow { Week = g.Week, Label = g.Label, Home = home, Played = g.Played, OpponentId = g.OpponentOf(teamId), Opponent = CareerText.TeamName(s, g.OpponentOf(teamId)) };
                if (g.Played) { r.TeamScore = home ? g.HomeScore : g.AwayScore; r.OpponentScore = home ? g.AwayScore : g.HomeScore; r.Won = g.WinnerId == teamId; }
                rows.Add(r);
            }
            return rows;
        }

        public static List<PowerRankRow> PowerRankings(CareerSave s)
        {
            var rows = new List<PowerRankRow>();
            var standings = s.Season != null && !s.Season.IsEmpty ? s.Season.Standings() : new List<StandingsRow>();
            var pct = new Dictionary<int, StandingsRow>();
            for (int i = 0; i < standings.Count; i++) pct[standings[i].TeamId] = standings[i];
            var score = new Dictionary<int, float>();
            for (int i = 0; i < s.Pro.Teams.Count; i++)
            {
                Team t = s.Pro.Teams[i];
                StandingsRow r;
                float sc = t.TeamOverall();
                if (pct.TryGetValue(t.Id, out r) && r.Games > 0) sc += (r.Pct - 0.5f) * 30f + (r.PF - r.PA) / (float)r.Games * 0.25f;
                score[t.Id] = sc;
                rows.Add(new PowerRankRow { TeamId = t.Id, Name = t.FullName, Record = RecordOf(s, t.Id), Ovr = t.TeamOverall() });
            }
            rows.Sort((a, b) => { int c = score[b.TeamId].CompareTo(score[a.TeamId]); return c != 0 ? c : a.TeamId.CompareTo(b.TeamId); });
            for (int i = 0; i < rows.Count; i++) rows[i].Rank = i + 1;
            return rows;
        }

        // ---------------------------------------------------------------- roster
        public static List<RosterRow> RosterTable(CareerSave s, Team t, bool includeSquadAndReserve = true)
        {
            var rows = new List<RosterRow>();
            foreach (Player p in CapManager.AllUnderContract(t))
            {
                RosterStatus st = RosterManager.StatusOf(s, t, p);
                if (!includeSquadAndReserve && st != RosterStatus.Active) continue;
                rows.Add(new RosterRow
                {
                    PlayerId = p.Id, Name = p.Name, Pos = p.Position, Archetype = p.Archetype, Jersey = p.Bio.JerseyNumber, Age = p.Age, Ovr = p.Overall, Potential = p.Potential,
                    Status = st, CapHit = p.Contract.CapHit, YearsLeft = p.Contract.YearsLeft, Injured = p.Injury.IsInjured, InjuryWeeks = p.Injury.WeeksOut, Morale = p.Morale,
                    AbilityCount = p.Abilities.Count, Starter = Offseason.IsStarter(t, p)
                });
            }
            rows.Sort((a, b) =>
            {
                int c = a.Status.CompareTo(b.Status);
                if (c != 0) return c;
                c = Array.IndexOf(LeagueRules.RosterOrder, a.Pos).CompareTo(Array.IndexOf(LeagueRules.RosterOrder, b.Pos));
                return c != 0 ? c : b.Ovr.CompareTo(a.Ovr);
            });
            return rows;
        }

        /// <summary>Positional needs, biggest first: slots short of the target and thin starters (the draft, free agency and trade screens show it).</summary>
        public static List<NeedRow> Needs(CareerSave s, Team t)
        {
            var rows = new List<NeedRow>();
            for (int i = 0; i < LeagueRules.RosterOrder.Length; i++)
            {
                Position pos = LeagueRules.RosterOrder[i];
                int have = t.CountAt(pos), target = LeagueRules.ProSlots(pos);
                float starter = Offseason.BestOvr(t, pos);
                float need = Math.Max(0, target - have) * 10f + (starter < 72f ? (72f - starter) * 0.4f : 0f) + (have < LeagueRules.Starters(pos) ? 25f : 0f);
                rows.Add(new NeedRow { Pos = pos, Have = have, Target = target, StarterOvr = (int)starter, Need = need });
            }
            rows.Sort((a, b) => b.Need.CompareTo(a.Need));
            return rows;
        }

        public static List<InjuryEvent> InjuryReport(CareerSave s, int teamId)
        {
            var l = new List<InjuryEvent>();
            Team t = s.Pro.GetTeam(teamId);
            foreach (Player p in CapManager.AllUnderContract(t))
                if (p.Injury.IsInjured)
                    l.Add(new InjuryEvent { Year = s.Year, PlayerId = p.Id, Name = p.Name, TeamId = teamId, Pos = p.Position, Type = p.Injury.Type, Weeks = p.Injury.WeeksOut, Starter = Offseason.IsStarter(t, p), PlacedOnIr = t.InjuredReserve.Contains(p) });
            l.Sort((a, b) => b.Weeks.CompareTo(a.Weeks));
            return l;
        }

        // ---------------------------------------------------------------- player card
        public static PlayerCard Card(CareerSave s, int playerId)
        {
            Player p = s.FindPlayer(playerId);
            if (p == null) return null;
            int teamId = s.TeamOf(playerId);
            Team t = teamId >= 0 ? s.Pro.GetTeam(teamId) : null;
            var c = new PlayerCard
            {
                Id = p.Id, Name = p.Name, Pos = p.Position, Archetype = p.Archetype.ToString(), Age = p.Age, HeightIn = p.Bio.HeightIn, WeightLb = p.Bio.WeightLb, Jersey = p.Bio.JerseyNumber,
                Overall = p.Overall, Potential = p.Potential, Height = p.Bio.HeightDisplay, College = p.Bio.College ?? "", Hand = p.Bio.Hand.ToString(), DevTrait = p.Dev.ToString(),
                TeamName = t != null ? t.FullName : (p.Retired ? "Retired" : "Free agent"), Status = t != null ? RosterManager.StatusOf(s, t, p) : (RosterManager.FindWaiver(s, p.Id) != null ? RosterStatus.Waivers : RosterStatus.FreeAgent),
                YearsLeft = p.Contract.YearsLeft, Aav = p.Contract.Aav, CapHit = p.Contract.CapHit, Guaranteed = p.Contract.Guaranteed, DeadIfReleased = Offseason.DeadMoneyOf(p.Contract),
                MarketValue = CapMath.Market(s, p, Negotiator.StartYearNow(s)), Expiring = p.Contract.YearsLeft <= 1, Injured = p.Injury.IsInjured, InjuryType = p.Injury.Type ?? "", InjuryWeeks = p.Injury.WeeksOut,
                TradeValue = TradeEngine.PlayerValue(s, p)
            };
            if (p.DraftRound > 0) c.Draft = p.DraftYear + " round " + p.DraftRound + ", pick " + p.DraftPick; else if (p.DraftYear > 0) c.Draft = p.DraftYear + " undrafted";
            RatingWeight[] weights = OverallWeights.For(p.Position);
            var w = new List<RatingWeight>(weights);
            w.Sort((a, b) => b.Weight.CompareTo(a.Weight));
            for (int i = 0; i < w.Count; i++) c.Ratings.Add(new RatingView { Kind = w[i].Kind, Name = w[i].Kind.ToString(), Value = p.Ratings.Get(w[i].Kind) });
            for (int i = 0; i < p.Abilities.Count; i++)
            {
                AbilityDef d = AbilityCatalog.Get(p.Abilities[i].Id);
                if (d == null) continue;
                c.Abilities.Add(new AbilityView { Id = d.Id, Name = d.Name, Tier = p.Abilities[i].Tier, Stars = AbilityCatalog.Stars(p.Abilities[i].Tier), Description = d.Description, Effects = AbilityCatalog.Describe(p.Abilities[i]) });
            }
            if (t != null) c.Morale = MoraleEngine.Breakdown(s, t, p);
            for (int i = 0; i < p.Career.Count; i++)
            {
                SeasonStats st = p.Career[i];
                c.Seasons.Add(new StatSeasonView { Year = st.Season, TeamId = st.TeamId, Team = CareerText.TeamNick(s, st.TeamId), Games = st.Games, Postseason = st.Postseason, Line = HistoryEngine.CareerLine(p.Position, st, 1) });
            }
            for (int i = 0; i < s.Accolades.Count; i++)
                if (s.Accolades[i].PlayerId == p.Id) c.Honors.Add(s.Accolades[i].Year + " " + s.Accolades[i].Kind);
            for (int i = 0; i < p.CollegeCareer.Count; i++)
            {
                SeasonStats st = p.CollegeCareer[i];
                if (st.Postseason) continue;
                School sc = s.College != null ? s.College.GetSchool(st.TeamId) : null;
                c.CollegeSeasons.Add(new StatSeasonView { Year = st.Season, TeamId = st.TeamId, Team = sc != null ? sc.ShortName : (p.Bio.College ?? ""), Games = st.Games, Line = CollegeViews.SeasonLine(p.Position, st) });
            }
            return c;
        }
    }
}
