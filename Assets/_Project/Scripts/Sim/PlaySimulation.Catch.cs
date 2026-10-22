using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    public sealed partial class PlaySimulation
    {
        /// <summary>Offense completes a catch: the receiver becomes the ball carrier.</summary>
        internal void CompleteCatch(SimAgent r)
        {
            GiveBall(r);
            PassCompleted = true;
            CatcherId = r.Id;
            PassCatchY = r.Pos.Y;
            PassTipped = false;
            r.PathIdx = 0;
            PlayAction(r, AgentAction.Catch, GamePace.CatchSeconds);
            r.CommitUntil = Time + GamePace.CatchSeconds; r.CommitSpeedFrac = GamePace.CatchSpeedFrac;   // catch -> run is not an instant flip
            LogEvent(SimEventType.Catch, r.Id, PasserId, new Vec3(r.Pos, Ball.Pos.Z), TackleType.None, r.Pos.Y - LosY);
        }

        /// <summary>Defense takes the ball away in the air.</summary>
        internal void CompleteInterception(SimAgent d)
        {
            GiveBall(d);
            BallSide = d.Side;
            Turnover = true;
            TurnoverInterception = true;
            TurnoverSpotY = d.Pos.Y;
            CatcherId = d.Id;
            PassTipped = false;
            PlayAction(d, AgentAction.Catch, GamePace.CatchSeconds);
            d.CommitUntil = Time + GamePace.CatchSeconds; d.CommitSpeedFrac = GamePace.CatchSpeedFrac;   // v0.9.0 (E-138): an interceptor secures the ball before he turns upfield, like a receiver
            LogEvent(SimEventType.Interception, d.Id, PasserId, new Vec3(d.Pos, Ball.Pos.Z), TackleType.None, d.Pos.Y - LosY);
            Blocks.ReleaseAll();
        }

        /// <summary>How open the quarterback judged the receiver on his last pass (0 = covered, 1 = wide open; -1 if none thrown). For tuning and UI.</summary>
        public float LastThrowOpenness { get { return PassThrown ? Qb.PendingOpen : -1f; } }
    }
}
