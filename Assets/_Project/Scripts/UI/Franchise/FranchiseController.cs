using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.League;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.UI.Franchise
{
    public enum HeroAction { PlayGame, Advance, OpenRoster, OpenResign, OpenFreeAgency, OpenDraft, OpenStaff, OpenAwards }

    /// <summary>What the big tile of the HOME tab shows and does for the current phase of the calendar (pure, EditMode tested).</summary>
    public sealed class HeroModel
    {
        public string Eyebrow = "", Title = "", Subtitle = "", Primary = "", Secondary = "";
        public HeroAction PrimaryAction = HeroAction.Advance;
        /// <summary>The user has a game to play (Play + Sim buttons, matchup card).</summary>
        public bool IsGame;
        public bool IsBye;
        /// <summary>1-based step of the offseason (0 during the season) and the number of steps (the calendar from the awards to the camp).</summary>
        public int Step, Steps;
        public List<string> Blockers = new List<string>();
        public NextGameView Next;
        public bool CanAdvance { get { return Blockers.Count == 0; } }

        public static readonly CareerPhase[] OffseasonOrder =
        {
            CareerPhase.Awards, CareerPhase.Retirements, CareerPhase.ReSign, CareerPhase.Scouting, CareerPhase.Draft, CareerPhase.RookieSigning, CareerPhase.FreeAgency, CareerPhase.TrainingCamp
        };

        public static HeroModel For(CareerSave s, HubHome home)
        {
            var h = new HeroModel { Blockers = home.Blockers, Next = home.Next };
            int idx = Array.IndexOf(OffseasonOrder, s.Phase);
            if (idx >= 0) { h.Step = idx + 1; h.Steps = OffseasonOrder.Length; }
            switch (s.Phase)
            {
                case CareerPhase.Preseason:
                    h.Eyebrow = "PRESEASON " + s.Year; h.Title = "Final cuts"; h.Subtitle = "Get to " + RosterRules.ActiveLimit + " players, set the depth chart, then start the season.";
                    h.Primary = "START THE SEASON"; h.Secondary = "ROSTER"; h.PrimaryAction = HeroAction.Advance; h.IsGame = false; break;
                case CareerPhase.RegularSeason:
                case CareerPhase.Playoffs:
                case CareerPhase.PixBowl:
                    {
                        bool game = home.Next != null && home.Next.Game != null && !home.Next.Game.Played;
                        h.IsGame = game; h.IsBye = home.Next != null && home.Next.Bye;
                        h.Eyebrow = home.PhaseLabel.ToUpperInvariant();
                        if (game) { h.Title = home.Next.Label; h.Subtitle = (home.Next.Home ? "Home against " : "At ") + home.Next.OpponentName; h.Primary = "PLAY GAME"; h.Secondary = "SIM GAME"; h.PrimaryAction = HeroAction.PlayGame; }
                        else if (home.Next != null && home.Next.Game != null) { h.Title = "Game played"; h.Subtitle = "Finish the week to see the league results."; h.Primary = "FINISH WEEK " + s.Season.Week; h.PrimaryAction = HeroAction.Advance; }
                        else
                        {
                            h.Title = s.Phase == CareerPhase.RegularSeason ? "Bye week" : "Your season is over";
                            h.Subtitle = s.Phase == CareerPhase.RegularSeason ? "No game for your team. Rest, manage the roster and sim the week." : "Sim the rest of the postseason to see who wins the PIX Bowl.";
                            h.Primary = home.NextStepLabel.ToUpperInvariant(); h.PrimaryAction = HeroAction.Advance;
                        }
                        break;
                    }
                case CareerPhase.Awards: h.Eyebrow = "OFFSEASON"; h.Title = "Season awards"; h.Subtitle = "All-Pro teams, the MVP and your legacy score. Look at the League tab, then continue."; h.Primary = "CONTINUE"; h.Secondary = "AWARDS"; h.PrimaryAction = HeroAction.Advance; break;
                case CareerPhase.Retirements: h.Eyebrow = "OFFSEASON"; h.Title = "Retirements"; h.Subtitle = "Veterans hang up the cleats and the Hall of Fame votes. The new league year opens next."; h.Primary = "OPEN THE RE-SIGN WINDOW"; h.PrimaryAction = HeroAction.Advance; break;
                case CareerPhase.ReSign: h.Eyebrow = "OFFSEASON"; h.Title = "Re-sign players"; h.Subtitle = Fmt.Plural(Negotiator.ExpiringPlayers(s, s.UserTeam).Count, "contract", "contracts") + " expire. Extend them, tag one, or let them walk."; h.Primary = "NEGOTIATE"; h.Secondary = "CLOSE THE WINDOW"; h.PrimaryAction = HeroAction.OpenResign; break;
                case CareerPhase.Scouting: h.Eyebrow = "OFFSEASON"; h.Title = "Combine and scouting"; h.Subtitle = "Spend " + CareerDraft.PointsRemaining(s) + " scouting points to sharpen your board before the draft."; h.Primary = "OPEN THE BOARD"; h.Secondary = "START THE DRAFT"; h.PrimaryAction = HeroAction.OpenDraft; break;
                case CareerPhase.Draft: h.Eyebrow = "OFFSEASON"; h.Title = "The Draft"; h.Subtitle = CareerDraft.UserOnClock(s) ? "You are on the clock." : "Sim to your next pick or open the board."; h.Primary = "OPEN THE DRAFT"; h.Secondary = "SIM TO MY PICK"; h.PrimaryAction = HeroAction.OpenDraft; break;
                case CareerPhase.RookieSigning: h.Eyebrow = "OFFSEASON"; h.Title = "Sign your rookies"; h.Subtitle = "Your picks are waiting for a contract. Free agency opens next."; h.Primary = "SIGN ROOKIES"; h.Secondary = "OPEN FREE AGENCY"; h.PrimaryAction = HeroAction.OpenDraft; break;
                case CareerPhase.FreeAgency:
                    h.Eyebrow = "OFFSEASON"; h.Title = "Free agency, day " + s.FaDay + " of " + s.Settings.FreeAgencyDays;
                    h.Subtitle = FreeAgency.IsDone(s) ? "The bidding period is over. Get under the cap and the roster limit, then go to camp." : "Place offers, then advance the day. The best players sign first.";
                    h.Primary = "FREE AGENCY"; h.Secondary = FreeAgency.IsDone(s) ? "GO TO CAMP" : "ADVANCE DAY"; h.PrimaryAction = HeroAction.OpenFreeAgency; break;
                case CareerPhase.TrainingCamp: h.Eyebrow = "OFFSEASON"; h.Title = "Training camp"; h.Subtitle = "Players progress and age. Check the staff, then go to the preseason."; h.Primary = "GO TO THE PRESEASON"; h.Secondary = "STAFF"; h.PrimaryAction = HeroAction.Advance; break;
            }
            return h;
        }
    }

    /// <summary>Navigation targets of the franchise shell (tab and sub page) used by the hub tiles and by tests.</summary>
    public static class FranchiseNav
    {
        public static readonly string[] Tabs = { "HOME", "TEAM", "LEAGUE", "OFFICE" };
        public static readonly string[][] SubTabs =
        {
            new string[0],
            new[] { "ROSTER", "DEPTH CHART", "INJURIES & SQUAD" },
            new[] { "SCHEDULE", "STANDINGS", "LEADERS", "AWARDS", "RECORDS", "TROPHIES & HOF", "NEWS" },
            new[] { "CAP & CONTRACTS", "RE-SIGN", "FREE AGENCY", "TRADES", "STAFF", "OWNER" }
        };
        public const int Home = 0, Team = 1, League = 2, Office = 3;
        public const int Roster = 0, Depth = 1, Squad = 2;
        public const int Schedule = 0, Standings = 1, Leaders = 2, Awards = 3, Records = 4, Trophies = 5, News = 6;
        public const int Cap = 0, ReSign = 1, FreeAgency = 2, Trades = 3, Staff = 4, Owner = 5;
    }

    /// <summary>One entry of the sim menu (what it does and whether it is available now).</summary>
    public sealed class SimOption
    {
        public string Label, Hint; public bool Enabled = true; public Func<string> Run;
    }

    /// <summary>
    /// The franchise controller: owns the open <see cref="CareerSave"/> for the UI, performs the calendar steps (advance, sim a week / to a week / to the playoffs / to the offseason / a whole season,
    /// auto-resolving what blocks the user) with an autosave after every week and phase, launches the Unity match and builds the hero model of the HOME tab. Pure C# except for the
    /// save slots, so the whole flow runs headless in EditMode tests ("through the UI controllers").
    /// </summary>
    public sealed class FranchiseController
    {
        public readonly CareerSave Save;
        public readonly CareerPrefs Prefs;
        public int Slot;
        /// <summary>Raised after anything changed the save (the shell refreshes the visible page).</summary>
        public event Action Changed;
        /// <summary>The last message for the toast / status line.</summary>
        public string Message = "";
        public bool AutosaveEnabled = true;
        public int Autosaves { get; private set; }

        public FranchiseController(CareerSave save, CareerPrefs prefs, int slot)
        {
            Save = save; Prefs = prefs ?? new CareerPrefs(); Slot = slot;
            if (save.UserBoxes != null) foreach (var sb in save.UserBoxes) if (sb != null && sb.Result != null) _box[BoxKey(sb.Year, sb.Week, sb.Result.HomeTeamId, sb.Result.AwayTeamId)] = sb.Result;   // box scores of the user's games survive a reload
        }

        public static FranchiseController FromSession()
        {
            return CareerSession.Active ? new FranchiseController(CareerSession.Save, CareerSession.Prefs, CareerSession.Slot) : null;
        }

        public LeagueTeam User { get { return Save.UserTeam; } }
        public CareerPhase Phase { get { return Save.Phase; } }

        public void Touch() { if (Changed != null) Changed(); }

        public void Say(string m) { Message = m ?? ""; }

        // ------------------------------------------------------------------ box scores of this session (the save keeps scores only)
        readonly Dictionary<string, GameResult> _box = new Dictionary<string, GameResult>();
        static string BoxKey(int year, int week, int home, int away) { return year + ":" + week + ":" + home + ":" + away; }

        public void Remember(IEnumerable<GameResult> results, int week)
        {
            if (results == null) return;
            foreach (var r in results) if (r != null) { _box[BoxKey(Save.Year, week, r.HomeTeamId, r.AwayTeamId)] = r; StoreUserBox(r, week); }
        }

        /// <summary>The user's own games go into the save (newest last, capped) so their box scores persist.</summary>
        void StoreUserBox(GameResult r, int week)
        {
            if (Save.UserBoxes == null || (r.HomeTeamId != Save.UserTeamId && r.AwayTeamId != Save.UserTeamId)) return;
            for (int i = Save.UserBoxes.Count - 1; i >= 0; i--)
            {
                var b = Save.UserBoxes[i];
                if (b.Year == Save.Year && b.Week == week && b.Result != null && b.Result.HomeTeamId == r.HomeTeamId && b.Result.AwayTeamId == r.AwayTeamId) Save.UserBoxes.RemoveAt(i);
            }
            Save.UserBoxes.Add(new StoredBox { Year = Save.Year, Week = week, Result = r });
            while (Save.UserBoxes.Count > CareerSave.MaxStoredBoxes) Save.UserBoxes.RemoveAt(0);
        }

        /// <summary>Picks up the results the match (or a one-off sim) posted this week.</summary>
        public void AbsorbPending() { Remember(Save.PendingResults, Week); }

        public GameResult BoxOf(ScheduledGame g)
        {
            GameResult r; return g != null && _box.TryGetValue(BoxKey(Save.Year, g.Week, g.HomeId, g.AwayId), out r) ? r : null;
        }

        public int BoxCount { get { return _box.Count; } }

        // ------------------------------------------------------------------ queries
        public HubHome Home() { return CareerViews.Home(Save); }
        public HeroModel Hero() { return HeroModel.For(Save, Home()); }
        public List<string> Blockers() { return CareerFlow.Blockers(Save); }
        public bool UserHasGame { get { var g = CareerFlow.UserGame(Save); return g != null && !g.Played && CareerFlow.InSeason(Save); } }
        public bool InSeason { get { return CareerFlow.InSeason(Save); } }
        public bool InRegularSeason { get { return Save.Phase == CareerPhase.RegularSeason; } }
        public int Week { get { return Save.Season != null ? Save.Season.Week : 0; } }
        public int RegularWeeks { get { return Save.Season != null ? Save.Season.RegularWeeks : 18; } }

        // ------------------------------------------------------------------ autosave
        public bool Autosave()
        {
            if (!AutosaveEnabled || Slot <= 0) return false;
            bool ok = CareerSession.Active && ReferenceEquals(CareerSession.Save, Save) ? CareerSession.SaveNow() : false;
            if (ok) Autosaves++;
            return ok;
        }

        // ------------------------------------------------------------------ calendar
        /// <summary>One calendar step. A blocked step returns the blockers (and says so). Autosaves after a step that changed the week or the phase.</summary>
        public StepResult Advance()
        {
            var r = CareerFlow.Advance(Save);
            if (!r.Ok) { Message = r.Blockers.Count > 0 ? r.Blockers[0] : "Cannot advance."; Touch(); return r; }
            if (r.Messages.Count > 0) Message = r.Messages[r.Messages.Count - 1];
            if (r.Week != null) { Remember(r.Week.Results, r.Week.Week); if (r.Week.UserResult != null) { Remember(new[] { r.Week.UserResult }, r.Week.Week); Message = ResultLine(r.Week.UserResult); } }
            Autosave();
            Touch();
            return r;
        }

        public string ResultLine(GameResult g)
        {
            return CareerText.TeamNick(Save, g.AwayTeamId) + " " + g.AwayScore + ", " + CareerText.TeamNick(Save, g.HomeTeamId) + " " + g.HomeScore + (g.Overtime ? " (OT)" : "");
        }

        /// <summary>Plays the user's game of the week with the quick simulation (the box score is read before the week ends) and finishes the week.</summary>
        public StepResult SimNext() { return Advance(); }

        /// <summary>Simulates only the user's game now and returns its result (the rest of the week waits for Advance), so the box score can be shown first.</summary>
        public GameResult SimUserGameNow()
        {
            var g = CareerFlow.UserGame(Save);
            if (g == null || g.Played) return null;
            var r = CareerFlow.SimGame(Save, g.Id);
            if (r != null) { Message = ResultLine(r); Remember(new[] { r }, g.Week); }
            Touch();
            return r;
        }

        /// <summary>Resolves what blocks the user the way a CPU team would. Returns the blockers that remain.</summary>
        public List<string> AutoResolve()
        {
            var b = CareerFlow.AutoResolveUser(Save);
            int tries = 0; while (b.Count > 0 && tries++ < 5) b = CareerFlow.AutoResolveUser(Save);
            Touch();
            return b;
        }

        /// <summary>Advances (auto-resolving blockers) until <paramref name="target"/>; stops at the user's own pending decision when <paramref name="stopAtDecision"/>. Returns the steps taken.</summary>
        public int AdvanceUntil(CareerPhase target, int maxSteps = 400)
        {
            int steps = 0;
            try
            {
                while (Save.Phase != target && steps < maxSteps)
                {
                    var b = AutoResolve();
                    if (b.Count > 0) { Message = b[0]; break; }
                    var r = CareerFlow.Advance(Save);
                    if (!r.Ok) { Message = r.Blockers.Count > 0 ? r.Blockers[0] : "Blocked."; break; }
                    steps++;
                    if (r.Week != null) Autosave();
                }
            }
            catch (Exception e) { Message = e.Message; }
            Autosave(); Touch();
            return steps;
        }

        /// <summary>Sims regular-season weeks (user games included) until the calendar reaches <paramref name="week"/> (the next week to play) or the postseason starts.</summary>
        public int SimToWeek(int week)
        {
            int steps = 0;
            while (Save.Phase == CareerPhase.RegularSeason && Save.Season.Week < week && steps < 40)
            {
                var r = CareerFlow.Advance(Save);
                if (!r.Ok) { Message = r.Blockers.Count > 0 ? r.Blockers[0] : "Blocked."; break; }
                steps++;
            }
            if (steps > 0) Message = "Simulated " + Fmt.Plural(steps, "week", "weeks") + ".";
            Autosave(); Touch();
            return steps;
        }

        public int SimWeeks(int n) { return SimToWeek(Week + Math.Max(1, n)); }

        public int SimToPlayoffs()
        {
            int s = 0;
            if (Save.Phase == CareerPhase.Preseason) s += AdvanceUntil(CareerPhase.RegularSeason);
            s += SimToWeek(RegularWeeks + 1);
            if (Save.Phase == CareerPhase.RegularSeason) { var r = CareerFlow.Advance(Save); if (r.Ok) s++; }
            Touch();
            return s;
        }

        /// <summary>Plays out the season (to the awards, the start of the offseason).</summary>
        public int SimToOffseason()
        {
            int steps = 0;
            if (Save.Phase == CareerPhase.Preseason) steps += AdvanceUntil(CareerPhase.RegularSeason);
            if (Save.Phase == CareerPhase.RegularSeason || Save.Phase == CareerPhase.Playoffs || Save.Phase == CareerPhase.PixBowl) steps += AdvanceUntil(CareerPhase.Awards);
            Message = "The " + Save.Year + " season is complete.";
            Touch();
            return steps;
        }

        /// <summary>Plays one whole year with the auto pilot: from here to the next preseason.</summary>
        public int SimSeason()
        {
            int steps = 0;
            try
            {
                if (Save.Phase == CareerPhase.Preseason) steps += AdvanceUntil(CareerPhase.RegularSeason);
                steps += AdvanceUntil(CareerPhase.Preseason);
            }
            catch (Exception e) { Message = e.Message; }
            Message = "Welcome to the " + Save.Year + " preseason.";
            Autosave(); Touch();
            return steps;
        }

        /// <summary>The draft with auto-picks for the user (CPU teams always auto-pick): runs until the draft is over and the rookies are waiting.</summary>
        public int AutoDraft()
        {
            int guard = 0;
            if (Save.Phase == CareerPhase.Scouting) { var r = CareerFlow.Advance(Save); if (!r.Ok) return 0; }
            var rng = Save.NewRng();
            while (Save.Phase == CareerPhase.Draft && guard++ < 600)
            {
                int n = 0; while (CareerDraft.UserOnClock(Save) && n++ < 300) CareerDraft.AutoPick(Save, rng);
                var r = CareerFlow.Advance(Save);
                if (!r.Ok) break;
            }
            Autosave(); Touch();
            return guard;
        }

        public List<SimOption> SimOptions()
        {
            var l = new List<SimOption>();
            bool reg = Save.Phase == CareerPhase.RegularSeason, pre = Save.Phase == CareerPhase.Preseason, season = InSeason;
            if (reg || season)
            {
                l.Add(new SimOption { Label = "Sim this week", Hint = "play none, sim all", Run = () => { var r = Advance(); return Message; } });
            }
            if (reg)
            {
                for (int k = 1; k <= 3; k++)
                {
                    int target = Week + k * 3;
                    if (target <= RegularWeeks) l.Add(new SimOption { Label = "Sim to week " + target, Hint = "keeps your roster as it is", Run = () => { SimToWeek(target); return Message; } });
                }
            }
            if (pre || reg) l.Add(new SimOption { Label = "Sim to the playoffs", Hint = "all remaining regular season games", Run = () => { SimToPlayoffs(); return Message; } });
            if (pre || season) l.Add(new SimOption { Label = "Sim to the offseason", Hint = "through the PIX Bowl", Run = () => { SimToOffseason(); return Message; } });
            l.Add(new SimOption { Label = "Sim the whole season", Hint = "auto pilot to next preseason", Run = () => { SimSeason(); return Message; } });
            return l;
        }

        // ------------------------------------------------------------------ the match
        public bool PlayNext(out string error)
        {
            error = "";
            if (!UserHasGame) { error = "There is no game to play."; return false; }
            if (!CareerSession.Active || !ReferenceEquals(CareerSession.Save, Save)) { error = "This franchise is not open in the session."; return false; }
            Autosave();
            return CareerSession.LaunchUserGame(out error);
        }
    }
}
