using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Gameplay
{
    /// <summary>The play-call screen's data, penalty choices, referee text and the CPU hurry-up (v0.3).</summary>
    public sealed partial class MatchFlow
    {
        public const int RecentLimit = 12;
        public readonly List<Play> RecentOffense = new List<Play>(), RecentDefense = new List<Play>();
        string _lastOffFormation = "", _lastDefFormation = "";
        public AudibleBook Audibles { get { return _audibles ?? (_audibles = new AudibleBook(Book)); } }
        AudibleBook _audibles;

        /// <summary>The user team's coach (tendencies for Ask Coach).</summary>
        public CoachProfile UserCoach { get { return PhysicalGameSim.ProfileOf(TeamOf(UserTeam)); } }

        /// <summary>True when the CPU offense is in a hurry-up (no huddle, sideline routes, spike).</summary>
        public bool CpuHurryUp { get { return State.Phase == GamePhase.Scrimmage && PlayCaller.IsHurryUp(State) && !IsUser(State.Possession); } }

        // ------------------------------------------------------------------------------------------------ remembering what the user called
        public void RememberUserCall(bool offense, Play p)
        {
            if (p == null) return;
            var list = offense ? RecentOffense : RecentDefense;
            list.RemoveAll(x => x.Name == p.Name && x.FormationName == p.FormationName);
            list.Add(p);
            while (list.Count > RecentLimit) list.RemoveAt(0);
            if (offense) _lastOffFormation = p.Side == PlaySide.Offense ? Book.FindFormation(p.FormationName).Family : _lastOffFormation;
            else _lastDefFormation = p.Side == PlaySide.Defense ? Book.FindFormation(p.FormationName).Family : _lastDefFormation;
        }

        // ------------------------------------------------------------------------------------------------ special teams options
        Play FindPlay(string name, string formation)
        {
            foreach (Play p in Book.AllPlays) if (p.Name == name && (formation == null || p.FormationName == formation)) return p;
            return null;
        }

        /// <summary>Special-teams plays the user can call right now (kicks when kicking, returns / blocks when the CPU kicks).</summary>
        public List<Play> SpecialOptionsFor(bool userOffense, Play cpuOffenseCall)
        {
            var l = new List<Play>();
            if (userOffense)
            {
                if (State.Phase == GamePhase.Kickoff)
                {
                    foreach (string n in new[] { "Kickoff Deep Middle", "Kickoff Deep Left", "Kickoff Deep Right", "Kickoff Squib", "Onside Kick Short", "Onside Kick Surprise" })
                    { Play p = FindPlay(n, null); if (p != null) l.Add(p); }
                }
                else if (State.Phase == GamePhase.PointAfter)
                {
                    if (!State.RequiresTwoPoint) { Play p = FindPlay("Extra Point", DefaultPlaybooks.FieldGoalF); if (p != null) l.Add(p); }
                }
                else
                {
                    foreach (Play p in Book.PlaysFor(DefaultPlaybooks.PuntF)) l.Add(p);
                    foreach (Play p in Book.PlaysFor(DefaultPlaybooks.FieldGoalF)) if (p.Category == PlayCategory.FieldGoal) l.Add(p);
                }
            }
            else
            {
                if (State.Phase == GamePhase.Kickoff) l.AddRange(Book.PlaysFor(DefaultPlaybooks.KickReturnF));
                else if (cpuOffenseCall != null && cpuOffenseCall.Category == PlayCategory.Punt) l.AddRange(Book.PlaysFor(DefaultPlaybooks.PuntReturnF));
                else if (cpuOffenseCall != null && (cpuOffenseCall.Category == PlayCategory.FieldGoal || cpuOffenseCall.Category == PlayCategory.PointAfter)) l.AddRange(Book.PlaysFor(DefaultPlaybooks.FieldGoalBlockF));
                else if (State.Phase == GamePhase.PointAfter) l.AddRange(Book.PlaysFor(DefaultPlaybooks.FieldGoalBlockF));
            }
            return l;
        }

        // ------------------------------------------------------------------------------------------------ the call screen model
        string Ordinal(int d) { return d == 1 ? "1ST" : d == 2 ? "2ND" : d == 3 ? "3RD" : "4TH"; }

        public string SpotText()
        {
            int spot = State.BallSpot;
            string own = Abbr(State.Possession), opp = Abbr(State.Possession.Opponent());
            if (spot == 50) return "50";
            return spot < 50 ? own + " " + spot : opp + " " + (100 - spot);
        }

        public string SituationText()
        {
            if (State.Phase == GamePhase.Kickoff) return "Q" + State.Quarter + "  " + State.ClockDisplay;
            if (State.Phase == GamePhase.PointAfter) return State.RequiresTwoPoint ? "TWO-POINT TRY REQUIRED" : "EXTRA POINT OR TWO-POINT TRY";
            return Ordinal(State.Down) + " & " + (State.IsGoalToGo ? "GOAL" : State.Distance.ToString()) + "   " + SpotText();
        }

        /// <summary>Builds the full play-call screen model for the user (null when the user has nothing to choose: for example the CPU is kicking off and there is no return to call).</summary>
        public PlayCallModel CreateCallModel(bool userOffense, Play cpuOffenseCall = null)
        {
            GamePhase ph = State.Phase;
            PlaySide side = userOffense ? PlaySide.Offense : PlaySide.Defense;
            List<Play> special = SpecialOptionsFor(userOffense, cpuOffenseCall);
            bool playbookTab;
            if (userOffense) playbookTab = ph == GamePhase.Scrimmage || ph == GamePhase.PointAfter;
            else
            {
                bool cpuKicks = cpuOffenseCall != null && (cpuOffenseCall.Category == PlayCategory.Punt || cpuOffenseCall.Category == PlayCategory.FieldGoal
                                                           || cpuOffenseCall.Category == PlayCategory.PointAfter || cpuOffenseCall.Category == PlayCategory.Kneel || cpuOffenseCall.Category == PlayCategory.Spike);
                playbookTab = (ph == GamePhase.Scrimmage || ph == GamePhase.PointAfter) && !cpuKicks;
            }            if (!playbookTab && special.Count == 0) return null;
            var m = new PlayCallModel(Book, side, playbookTab, special.Count > 0);
            var s = m.Situation;
            s.Title = userOffense ? (ph == GamePhase.Kickoff ? "KICKOFF" : (ph == GamePhase.PointAfter ? "AFTER THE TOUCHDOWN" : "OFFENSE")) : (ph == GamePhase.Kickoff ? "KICK RETURN" : "DEFENSE");
            s.TeamName = TeamOf(UserTeam).Nickname.ToUpperInvariant();
            s.DownText = ph == GamePhase.Scrimmage ? Ordinal(State.Down) + " & " + (State.IsGoalToGo ? "GOAL" : State.Distance.ToString()) : (ph == GamePhase.Kickoff ? "KICKOFF" : (State.RequiresTwoPoint ? "2-PT TRY" : "PAT"));
            s.SpotText = ph == GamePhase.Scrimmage ? "BALL ON " + SpotText() : "";
            s.ScoreText = Abbr(CoreTeam.Home) + " " + State.HomeScore + "  -  " + Abbr(CoreTeam.Away) + " " + State.AwayScore;
            s.ClockText = "Q" + State.Quarter + " " + (State.IsCollegeOvertime ? "OT" : State.ClockDisplay);
            s.Quarter = State.Quarter;
            s.Timeouts = State.TimeoutsOf(UserTeam);
            s.PlayClockSeconds = Rules.PlayClockDead;
            s.CoachStyle = UserCoach.Label;
            m.SpecialPlays = special;
            if (playbookTab)
            {
                m.CoachPicks = userOffense ? Cpu.Caller.SuggestOffense(State, UserCoach, RecentOffense, 6) : Cpu.Caller.SuggestDefense(State, UserCoach, 5);
                var rec = userOffense ? RecentOffense : RecentDefense;
                m.RecentPlays = new List<Play>(rec); m.RecentPlays.Reverse();
                m.LastFormation = userOffense ? _lastOffFormation : _lastDefFormation;
            }
            if (playbookTab && userOffense && ph == GamePhase.Scrimmage && State.Down == 4 && special.Count > 0)
            {
                // fourth down: the coach starts with the kicking decision
                var fourth = Cpu.Caller.FourthDown(State, UserCoach);
                Play kick = null;
                foreach (Play sp in special) if ((fourth == PlayCaller.FourthCall.Punt && sp.Category == PlayCategory.Punt) || (fourth == PlayCaller.FourthCall.FieldGoal && sp.Category == PlayCategory.FieldGoal)) { kick = sp; break; }
                if (kick != null) m.CoachPicks.Insert(0, new CoachPick { Play = kick, Reason = fourth == PlayCaller.FourthCall.Punt ? "4TH DOWN: PUNT IT AWAY" : "4TH DOWN: KICK THE FIELD GOAL", Score = 2f });
            }
            m.Open(ph == GamePhase.PointAfter && special.Count > 0 ? CallTab.Special : (playbookTab ? CallTab.Playbook : CallTab.Special));
            return m;
        }

        // ------------------------------------------------------------------------------------------------ penalties
        /// <summary>The accept / decline choice for the flag on a finished play, or null when the play has no flag.</summary>
        public PenaltyChoice PenaltyChoiceFor(PlaySimulation sim)
        {
            if (sim == null || sim.Result == null || sim.Result.Penalty == null) return null;
            return PenaltyEnforcer.Describe(State, sim.Result);
        }

        public bool UserDecides(PenaltyChoice c) { return c != null && !Config.CpuVsCpu && IsUser(c.OfferedTo); }

        /// <summary>Jersey number of the player the flag is on (0 when unknown).</summary>
        public int PenaltyNumber(PenaltyCall call)
        {
            Player p = TeamOf(call.CommittedBy).GetPlayer(call.PlayerId);
            return p != null && p.Bio != null ? p.Bio.JerseyNumber : 0;
        }

        public string OutcomeText(PenaltyOutcome o)
        {
            if (o.Phase == GamePhase.Final) return "FINAL";
            if (o.Phase == GamePhase.Kickoff) return "KICKOFF";
            if (o.Phase == GamePhase.PointAfter) return o.Points >= 6 ? "TOUCHDOWN" : "TRY";
            string spot = o.Spot == 50 ? "50" : (o.Spot < 50 ? Abbr(o.Possession) + " " + o.Spot : Abbr(o.Possession.Opponent()) + " " + (100 - o.Spot));
            string dd = Ordinal(o.Down) + " & " + (o.Spot + o.Distance >= 100 ? "GOAL" : o.Distance.ToString());
            string head = o.PossessionChanged ? "TURNOVER: " + Abbr(o.Possession) + " BALL  " : "";
            return head + dd + "  AT " + spot + (o.FirstDown ? "  (FIRST DOWN)" : "");
        }

        public string ChoiceHeadline(PenaltyChoice c)
        {
            int n = PenaltyNumber(c.Call);
            return "FLAG: " + c.Def.Name.ToUpperInvariant() + "  -  " + Abbr(c.CommittedBy) + (n > 0 ? "  #" + n : "");
        }

        /// <summary>What the referee says over the stadium (no audio yet: shown as text).</summary>
        public string RefereeText(PenaltyChoice c, bool accepted)
        {
            int n = PenaltyNumber(c.Call);
            string who = (c.CommittedBy == State.Possession ? "offense" : "defense");
            if (c.Call.Def.PreSnap && c.Call.Type != PenaltyType.DelayOfGame) who = (c.CommittedBy == State.Possession ? "offense" : "defense");
            string number = n > 0 ? ", number " + n + "," : "";
            string text = c.Def.Name + number + " " + who + " (" + TeamOf(c.CommittedBy).Nickname + ")";
            if (!accepted) return "Penalty declined. " + text + ".";
            int y = Math.Abs(c.Yards);
            text += ". " + y + "-yard penalty";
            if (c.LossOfDown) text += ", loss of down";
            if (c.Accept != null)
            {
                if (c.Accept.FirstDown && c.AutomaticFirstDown) text += ", automatic first down";
                else if (c.Accept.ReplayDown) text += ", repeat " + Ordinal(c.Accept.Down).ToLowerInvariant() + " down";
            }
            return text + ".";
        }

        public string DeclineSummary(PenaltyChoice c)
        {
            if (c.DeadBall) return "NO PLAY: REPLAY THE DOWN";
            return OutcomeText(c.Decline);
        }
    }
}



