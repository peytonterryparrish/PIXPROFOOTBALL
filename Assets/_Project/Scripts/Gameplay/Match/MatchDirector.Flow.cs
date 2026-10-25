using System.Collections;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 SIM-FLOW: the director side of the dead-ball flow. In a simulated (watch-mode) game the bodies of a finished play are carried over to the next one (see PlayStage.Between): no cut to black, the players jog back,
    /// huddle, break and set, substitutes jog on and off, and the snap waits for the break. The hooks in MatchDirector.cs are one-liners that call into this file.
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>The watch-mode flow (carried bodies, huddles, benches) is on for simulated games.</summary>
        bool FlowCarry { get { return PlayStage.ContinuityEnabled && (Config.CpuVsCpu || ForceAutopilot) && Stage != null; } }

        // ------------------------------------------------------------------------------------------ v0.12.0 (RULES): the dead-ball time
        // Whistle to snap is a setting (PixSettings.DeadBallSeconds, default 25 s; it was about 16). The snap waits for it, the huddle hold and the play clock are built from it.
        float _whistleAt = -1f, _preClock, _preT, _setT;
        /// <summary>Seconds on the clock since the last whistle (99 when there was none, e.g. after a break).</summary>
        float SinceWhistle { get { return _whistleAt < 0f ? 99f : Time.time - _whistleAt; } }

        /// <summary>The whistle-to-snap time the next play aims for: the setting for a scrimmage down, shorter for a hurry-up (no huddle) and for a kick or try; 0 outside a watched game.</summary>
        float DeadBallTarget()
        {
            if (!FlowCarry || Flow == null) return 0f;
            float d = PixSettings.DeadBallSeconds;
            if (Flow.State.Phase != PixFootball.Core.GamePhase.Scrimmage) return d * 0.6f;
            return Flow.CpuHurryUp ? d * 0.4f : d;
        }

        /// <summary>True while the dead-ball time has not run out: the CPU does not snap yet (the count, about one second, still has to be called).</summary>
        bool DeadBallHold() { float t = DeadBallTarget(); return t > 0f && SinceWhistle < t - 0.9f; }

        /// <summary>The play clock a watched game starts the pre-snap with: it runs from the whistle, five seconds longer than the dead-ball time so a slow quarterback can run into a delay of game.</summary>
        float DeadBallPlayClock() { float t = DeadBallTarget(); return t > 0f ? Mathf.Max(16f, t + 5f - Mathf.Min(SinceWhistle, t)) : -1f; }

        /// <summary>Seconds the stage has from the build of the next play to the count (what the huddle hold and the walk-in stretch to fill).</summary>
        float PreSnapBudget() { float t = DeadBallTarget(); return t > 0f ? Mathf.Max(0f, t - SinceWhistle - 1.0f) : 0f; }

        /// <summary>Called by BeginPlay just before the stage is built: tells the stage how the next play comes together.</summary>
        void FlowBeforeBegin()
        {
            if (!FlowCarry || Stage == null) return;
            bool first = !Stage.CarryPending;
            Stage.NextBeginFromBench = first;
            Stage.NextBeginHuddle = _huddleOn;
            Stage.NextBeginHurry = Flow.CpuHurryUp || Flow.State.Phase != PixFootball.Core.GamePhase.Scrimmage;
            Stage.NextBeginBudget = PreSnapBudget();      // v0.12.0 (RULES): the huddle hold and the walk-in stretch to fill the dead-ball time
        }

        /// <summary>A timeout: both teams to their sideline for a few seconds, the referee signals.</summary>
        IEnumerator FlowTimeout(PixFootball.Core.Team team)
        {
            TimeoutsShown++;
            if (Officials != null) Officials.Signal(OfficialSignal.Timeout);
            Stage.TimeoutBegin();
            float t = 0f, arrivedAt = -1f;
            while (t < 14f && (arrivedAt < 0f || t < arrivedAt + 3.2f) && !_quit)
            {
                float dt = Mathf.Min(Time.deltaTime, 0.05f);
                if (Paused) { PauseFrame(); yield return null; continue; }
                t += dt;
                if (arrivedAt < 0f && Stage.TimeoutArrived()) arrivedAt = t;
                Sim.Tick(dt, default(ControlInput), -1);
                Stage.Present(dt);
                PresentFrame(dt, 0f, 0f);
                yield return null;
            }
            Stage.TimeoutEnd();
        }

        float _measureWind;
        /// <summary>NFL: about two or three plays a game end within a ball length of the line to gain and are measured with the chains.</summary>
        void FlowMeasure(PlayOutcome o)
        {
            if (Sim == null || o == null || o.Result == null || !FlowCarry || Sim.Kind != PlayKind.Scrimmage || o.Headline == Headline.Touchdown) return;
            if (o.Result.Type != PixFootball.Core.PlayType.Run && o.Result.Type != PixFootball.Core.PlayType.Pass) return;
            float ey = Sim.Carrier != null ? Sim.Carrier.Pos.Y : Sim.Ball.Pos.Y;
            if (Sim.LineToGainY >= 100f || Mathf.Abs(ey - Sim.LineToGainY) > 0.42f) return;
            _measureWind = 2.6f; MeasuresShown++;
            Banner("MEASUREMENT", UiColorsLite.Yellow, 2.4f, "THE CHAINS COME OUT");
        }
        float FlowExtraWind() { float w = _measureWind; _measureWind = 0f; return w; }
        /// <summary>QA: measurements shown.</summary>
        public static int MeasuresShown;
        public static int TimeoutsShown;

        /// <summary>The end of a play in watch mode: keep the bodies on the field for the next play (halftime sends everybody to the benches).</summary>
        void FlowEndPlay(PlayOutcome outcome, bool halftime)
        {
            Stage.PrepareCarry();
            if (halftime) Stage.DismissAll();
            Sim = null;
        }
    }
}
