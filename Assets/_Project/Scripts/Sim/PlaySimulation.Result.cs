using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    public sealed partial class PlaySimulation
    {
        internal int LastOffenseCarrier = -1;

        private static int RoundYards(float v) { return (int)Math.Round(v, MidpointRounding.AwayFromZero); }

        private int Pid(int agentId) { return agentId >= 0 && agentId < 22 ? Agents[agentId].PlayerId : 0; }

        /// <summary>Spot (ball y) where the play was whistled dead.</summary>
        private void DetermineEndY()
        {
            SimAgent c = Carrier;
            if (c != null)
            {
                float dirY = c.Side == SimSide.Offense ? 1f : -1f;
                EndY = c.Pos.Y + dirY * 0.2f;
                if (c.Move == CarrierMove.Dive && c.MoveTime > 0f) EndY += dirY * 0.7f;
                if (_ruledSpotY.HasValue) EndY = _ruledSpotY.Value;   // spotted where he was wrapped up, not where he slid to
            }
            else EndY = Ball.Pos.Y;
        }

        private PlayResult BuildResult(EndReason reason)
        {
            DetermineEndY();
            float time = Time + 1.0f;
            switch (Kind)
            {
                case PlayKind.Kneel: return PlayResult.KneelDown(Math.Max(2f, time));
                case PlayKind.Spike: return PlayResult.SpikeBall(1f);
                case PlayKind.Kickoff: case PlayKind.Punt: case PlayKind.FieldGoal:
                    return Specials.BuildKickResult(reason, time);
            }

            PlayResult r = BuildScrimmageResult(reason, time);
            // offenses snap quicker than the full play clock: the rulebook default is a conservative runoff
            if (r.RunoffSeconds < 0f) r.RunoffSeconds = Math.Max(12f, Rules.NormalRunoff - 5f);
            if (IsTwoPoint)
            {
                bool good = reason == EndReason.Touchdown && !DefensiveTd && BallSide == SimSide.Offense;
                var tp = PlayResult.TwoPointTry(good);
                tp.DefensiveScore = reason == EndReason.Touchdown && DefensiveTd;
                tp.TimeElapsed = 0f;
                return tp;
            }
            return r;
        }

        private PlayResult BuildScrimmageResult(EndReason reason, float time)
        {
            PlayResult r;
            int yds = RoundYards(EndY - LosY);
            int passerPid = Pid(PasserId);
            int targetPid = Pid(TargetId);
            int rusherPid = Pid(RusherId >= 0 ? RusherId : LastOffenseCarrier);
            int tacklerPid = Pid(TacklerId);

            if (reason == EndReason.Touchdown)
            {
                if (BallSide == SimSide.Offense)
                {
                    int toGo = Math.Max(1, (int)Math.Ceiling(100f - LosY));
                    r = PassCompleted ? PlayResult.Pass(toGo, time, false, passerPid, Pid(CatcherId)) : PlayResult.Run(toGo, time, false, rusherPid, 0);
                    r.Touchdown = true;
                    return r;
                }
                int catchYds = RoundYards(TurnoverSpotY - LosY);
                r = TurnoverInterception ? PlayResult.Intercept(catchYds, 0, true, time) : PlayResult.FumbleLost(catchYds, 0, true, time);
                r.Touchdown = true; r.Turnover = true;
                r.PasserId = passerPid; r.DefenderId = Pid(CatcherId >= 0 ? CatcherId : TacklerId);
                return r;
            }

            if (reason == EndReason.Safety)
            {
                if (QbSack) r = PlayResult.Sack(Math.Max(1, RoundYards(LosY - EndY)), time, passerPid, tacklerPid);
                else r = PlayResult.Run(yds, time, false, rusherPid, tacklerPid);
                r.Safety = true;
                return r;
            }

            if ((reason == EndReason.Sack || QbSack) && !Turnover)
            {
                r = PlayResult.Sack(Math.Max(0, RoundYards(LosY - EndY)), time, Pid(0), tacklerPid);
                if (EndY <= 0f) r.Safety = true;
                return r;
            }

            if (reason == EndReason.Incomplete)
                return PlayResult.Incomplete(Math.Max(3f, Time + 0.8f), passerPid, targetPid);

            if (Turnover)
            {
                float fromGoalLine = Math.Min(TurnoverSpotY, 100f);
                int ret = Math.Max(0, RoundYards(fromGoalLine - EndY));
                int catchYds = RoundYards(TurnoverSpotY - LosY);
                if (TurnoverInterception) r = PlayResult.Intercept(catchYds, ret, false, time);
                else r = PlayResult.FumbleLost(catchYds, ret, false, time);
                r.Turnover = true;
                r.OutOfBounds = OutOfBounds;
                r.PasserId = passerPid; r.DefenderId = Pid(CatcherId >= 0 ? CatcherId : TacklerId);
                r.TacklerId = tacklerPid;
                if (TurnoverSpotY >= 100f && ret <= 0) r.Touchback = true;
                return r;
            }

            if (FumbleCount > 0 && BallSide == SimSide.Offense && PassCompleted == false && !PassThrown)
            {
                r = PlayResult.FumbleRecovered(yds, time);
                r.RusherId = rusherPid; r.TacklerId = tacklerPid; r.OutOfBounds = OutOfBounds;
                return r;
            }

            if (PassCompleted)
            {
                r = PlayResult.Pass(RoundYards(EndY - LosY), time, OutOfBounds, passerPid, Pid(CatcherId));
                r.TacklerId = tacklerPid;
                return r;
            }

            r = PlayResult.Run(yds, time, OutOfBounds, rusherPid, tacklerPid);
            return r;
        }
    }
}
