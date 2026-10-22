using System;
using PixFootball.League;

namespace PixFootball.Gameplay
{
    /// <summary>v0.8 Franchise: a played franchise game hands its result (score, box score, player lines) back to the career.</summary>
    public sealed partial class MatchDirector
    {
        bool _careerPosted;

        /// <summary>The game is final: build the <see cref="GameResult"/> and post it once (before any postgame show, so quitting the show never loses the result).</summary>
        void PostCareerResult()
        {
            if (_careerPosted || !CareerSession.Pending || Flow == null) return;
            _careerPosted = true;
            try { CareerSession.CompleteMatch(BuildCareerResult()); }
            catch (Exception e) { CareerSession.LastMessage = "The result could not be posted: " + e.Message; UnityEngine.Debug.LogError("[PIX] career result: " + e); }
        }

        GameResult BuildCareerResult()
        {
            var s = Flow.State;
            var r = new GameResult
            {
                HomeTeamId = Flow.Home.Id, AwayTeamId = Flow.Away.Id, HomeScore = s.HomeScore, AwayScore = s.AwayScore, Overtime = s.IsOvertime, OvertimePeriods = Math.Max(0, s.Quarter - 4), TotalPlays = Flow.Snaps
            };
            r.Home.TeamId = Flow.Home.Id; r.Away.TeamId = Flow.Away.Id; r.Home.Score = s.HomeScore; r.Away.Score = s.AwayScore;
            r.Home.ByQuarter.Clear(); r.Away.ByQuarter.Clear();
            int homeOt = 0, awayOt = 0;
            for (int q = 1; q <= 7; q++)   // the flow counts quarters from 1 (5 to 7 = overtime periods)
            {
                if (q <= 4) { r.Home.ByQuarter.Add(Flow.HomeByQuarter[q]); r.Away.ByQuarter.Add(Flow.AwayByQuarter[q]); }
                else { homeOt += Flow.HomeByQuarter[q]; awayOt += Flow.AwayByQuarter[q]; }
            }
            r.Home.OvertimeScore = homeOt; r.Away.OvertimeScore = awayOt;
            var br = Broadcast as IBroadcastResult;
            if (br != null) br.FillResult(r);
            else
            {
                r.Home.Plays = Flow.HomeStats.Plays; r.Away.Plays = Flow.AwayStats.Plays;
            }
            return r;
        }
    }
}
