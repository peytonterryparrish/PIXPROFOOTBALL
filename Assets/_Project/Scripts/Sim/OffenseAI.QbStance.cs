using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.11.0 QB (playtest note 8): the quarterback is in his square throwing stance (ball high at the chest in both hands, shoulders to the read, eyes scanning in progression order) the whole time he has the ball, in the pocket AND while he slides,
    /// climbs, escapes or rolls out, until he DECIDES to run. This file is the decision: <see cref="QbTakeoffNow"/> is true from the tick a computer quarterback ends his escape work and commits to the run (or a user quarterback holds sprint, RT),
    /// and for a quarterback who keeps the ball as a runner. The presentation poses everything else as the stance and, on this flag, plays the choreographed tuck-and-run transition. Nothing here changes a simulated outcome: it only publishes the intent.
    /// </summary>
    internal sealed partial class OffenseAI
    {
        private bool _qbTakeoff, _qbTakeoffIntent, _scrambleHolding, _scrambleHoldKnown;
        /// <summary>QA: the takeoff decision whatever the A/B switch says.</summary>
        internal bool QbTakeoffIntentNow { get { return _qbTakeoffIntent; } }
        private float _qbTakeoffAt = -1f, _userSprintT;

        /// <summary>True while the quarterback is running with the ball on purpose (a takeoff, a keeper, a sprinting user passer).</summary>
        internal bool QbTakeoffNow { get { return _qbTakeoff; } }
        /// <summary>Simulation time the current takeoff began (-1 = none).</summary>
        internal float QbTakeoffAt { get { return _qbTakeoff ? _qbTakeoffAt : -1f; } }

        /// <summary>Called every tick from the quarterback update (<see cref="QbSeqWatch"/>).</summary>
        private void QbTakeoffWatch(SimAgent q)
        {
            bool now;

            switch (q.QbState)
            {
                case QbPhase.Scramble:
                    if (q.ControlledByUser)
                    {
                        // RT (sprint) is the only thing that takes a user quarterback out of the stance; a short debounce so a flick of the button is not a takeoff
                        _userSprintT = q.Exertion > 0.5f ? Math.Min(0.3f, _userSprintT + PlaySimulation.Dt) : Math.Max(0f, _userSprintT - 2f * PlaySimulation.Dt);
                        now = _userSprintT >= 0.12f || (_qbTakeoff && _userSprintT > 0.02f);
                    }
                    else now = _scrambleHoldKnown && !_scrambleHolding;      // the computer quarterback's lateral escape (hold) is the stance; the commitment to run is the takeoff
                    break;
                case QbPhase.Done:
                    now = q.HasBall && !S.PassThrown && !_awayPending && !(_qa != null && _qa.Action == QbAction.Keep && _qa.DropbackSteps > 0 && S.Time < 1.05f);
                    break;
                default: now = false; _userSprintT = 0f; break;
            }
            _qbTakeoffIntent = now;
            if (QbV11.Legacy) now = true;      // v0.10.0: a quarterback who left the pocket ran the cycle with the ball at the ear
            if (now && !_qbTakeoff) _qbTakeoffAt = S.Time;
            _qbTakeoff = now;
        }

        /// <summary>The way a quarterback in the stance faces while he moves with the ball: chest to the field, opened toward the way he travels by at most 26 degrees (the shoulders stay square to the read; the hips and the feet shuffle).</summary>
        private static Vec2 QbStanceMoveFacing(Vec2 moveDir, float openRad, float legacyX = 0.55f, float legacyY = 0.84f)
        {
            Vec2 down = new Vec2(0f, 1f);
            float side = moveDir.X >= 0f ? 1f : -1f;
            if (QbV11.Legacy) return new Vec2(side * legacyX, legacyY).Norm();      // the v0.10.0 facing
            return RotateDir(down, -side * openRad);
        }
    }
}

namespace PixFootball.Sim
{
    /// <summary>v0.11.0 QB: switches for the A/B of the audit (QA only). <see cref="Legacy"/> = the v0.10.0 quarterback (run cycle with the ball at the ear when he leaves the pocket, the old facing, the old receiver tracking).</summary>
    public static class QbV11
    {
        public static bool Legacy = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixQbV10") >= 0;
    }
}
