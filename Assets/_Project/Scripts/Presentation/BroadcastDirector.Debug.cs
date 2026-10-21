using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.League;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Presentation
{
    /// <summary>Tools and autotest entry points that put each overlay on screen on demand (with the live data when there is some, sample data otherwise).</summary>
    public sealed partial class BroadcastDirector
    {
        public bool Debug_PlayerStat()
        {
            EnsureTracker();
            foreach (var cat in new[] { "pass", "rush", "rec", "def" })
                for (int t = 0; t < 2; t++)
                {
                    var l = _trk.Leaders(t, cat, 1);
                    if (l.Count == 0) continue;
                    var p = _trk.PlayerById(l[0].PlayerId);
                    if (p == null) continue;
                    _ov.ShowPlayerStat(p, l[0].Stats, VisOfIdx(t), "BIG PLAY");
                    return true;
                }
            return false;
        }

        public void Debug_DriveSummary()
        {
            EnsureTracker();
            DriveRecord d = null;
            for (int i = _trk.Drives.Count - 1; i >= 0; i--) if (!string.IsNullOrEmpty(_trk.Drives[i].Result)) { d = _trk.Drives[i]; break; }
            if (d == null) d = new DriveRecord { TeamId = Flow.Home.Id, Quarter = 1, Plays = 8, Yards = 62, Seconds = 265, StartSpot = 25, Result = "Touchdown", Points = 7 };
            int idx = d.TeamId == Flow.Home.Id ? 0 : 1;
            _ov.ShowDriveSummary(d, VisOfIdx(idx), "STARTED AT " + SpotText(d.StartSpot, TeamOf(idx), TeamOf(1 - idx)) + "  -  Q" + d.Quarter);
        }

        public void Debug_Comparison()
        {
            EnsureTracker();
            _ov.ShowComparison(OverlayText.CompareShort(_trk.HomeBox, _trk.AwayBox), VisOf(CoreTeam.Away), VisOf(CoreTeam.Home), "TEAM STATS");
        }

        public void Debug_TwoMinute() { _ov.ShowTwoMinute("2ND QUARTER"); }

        public void Debug_NextPlay()
        {
            var s = State;
            _ov.ShowNextPlay(OverlayText.DownAndDistance(s.Down, s.Distance, s.IsGoalToGo), SpotText(s.BallSpot, s.Possession, s.Possession.Opponent()), s.ClockDisplay);
        }

        public void Debug_Penalty() { _ov.ShowPenalty("PENALTY - FALSE START", Flow.Abbr(CoreTeam.Away) + "   5 YARDS", VisOf(CoreTeam.Away)); }

        public void Debug_Score()
        {
            var s = State;
            _ov.ShowScoreUpdate(VisOf(CoreTeam.Away), s.AwayScore, VisOf(CoreTeam.Home), s.HomeScore, "TOUCHDOWN", s.ClockDisplay);
        }

        public void Debug_Ticker() { _ov.ShowTicker("DOE COMPLETES TO ROE FOR 18 YARDS - FIRST DOWN", VisOf(CoreTeam.Home), State.ClockDisplay); }

        public void Debug_Crowd(float strength) { _crowd.Excite(strength); }

        public void Debug_Shot(ShotKind kind, float seconds, int agent = 0)
        {
            _focusAgent = agent; _secondAgent = -1; _coachTeam = 0;
            if (_sim == null) return;
            _queue.Clear(); _queue.Add(new ShotPlan(kind, seconds)); _queueDelay = 0f;
        }
    }
}
