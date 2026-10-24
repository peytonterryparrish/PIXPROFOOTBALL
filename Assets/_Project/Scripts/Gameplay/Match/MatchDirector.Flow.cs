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

        /// <summary>Called by BeginPlay just before the stage is built: tells the stage how the next play comes together.</summary>
        void FlowBeforeBegin()
        {
            if (!FlowCarry || Stage == null) return;
            bool first = !Stage.CarryPending;
            Stage.NextBeginFromBench = first;
            Stage.NextBeginHuddle = _huddleOn;
            Stage.NextBeginHurry = Flow.CpuHurryUp || Flow.State.Phase != PixFootball.Core.GamePhase.Scrimmage;
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
