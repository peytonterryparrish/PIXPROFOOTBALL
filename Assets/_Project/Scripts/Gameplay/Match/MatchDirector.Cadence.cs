using System.Collections;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 QBSTANCE (E-054, E-085): the quarterback's cadence between the snap request and the snap. The button (or the CPU's timer) used to snap the ball on the same frame; now the quarterback calls it:
    /// his head swivels, he bounces on his feet, and on the "go" (the last 0.12 s) his hips drop about 6 in so the hands stay under the centre, then the ball is snapped. A user press and a CPU snap take the same path.
    /// v0.12.0 (RULES): the count belongs to the simulation (<see cref="PlaySimulation.BeginCount"/>): it decides how long the quarterback really takes (a motion, a shift and the one-second set after it, a slow snap
    /// that runs into the play clock) and who jumps it. The window lasts as long as the simulation says and ends at once when the sim blows the play dead (false start, offside, delay of game...).
    /// </summary>
    public sealed partial class MatchDirector
    {
        /// <summary>QA: skip the cadence window (the "before" state snaps on the press).</summary>
        public static bool DebugNoCadence;

        /// <summary>Length of the cadence (seconds): 0.6 to 1.0, seeded by the play so a replay of the same play looks the same.</summary>
        public static float CadenceSeconds(int seed) { return 0.6f + 0.1f * (((seed % 5) + 5) % 5); }

        IEnumerator CadenceWindow()
        {
            if (DebugNoCadence || Sim == null || Sim.Kind != PlayKind.Scrimmage || Sim.Phase != PlayPhase.PreSnap || Stage == null) yield break;
            var qb = Sim.Agents[0];
            if (qb == null || qb.Side != SimSide.Offense) yield break;
            float len = CadenceSeconds(Sim.OffPlay != null ? Sim.OffPlay.Name.Length * 7 + Sim.BallX0.GetHashCode() : 0);
            bool cpuOffense = !_userIsOffense || Autopilot;
            len = Sim.BeginCount(len, _preClock, cpuOffense, Flow == null || !Flow.CpuHurryUp);      // v0.12.0 (RULES): the simulation plans the count (motion, shift, slow snap, who jumps it)
            Stage.BeginCadence(len);
            float t = 0f;
            while (t < len && !_quit && Sim.Phase == PlayPhase.PreSnap)
            {
                float dt = Time.deltaTime > 0f ? Mathf.Min(Time.deltaTime, 0.05f) : 0.0167f;
                Sim.Tick(dt, default(ControlInput), KickUserPreSnap());
                Stage.Present(dt);
                t += dt; _preClock -= dt;
                PresentFrame(dt, 0f, _preT + t);
                if (Ui != null) Ui.SetScore(MakeScore(false, _preClock, _preT + t));
                yield return null;
            }
            // S1 fix: the count can start a shift or a motion; the ball is not snapped while a body of the stage is still walking or turning to it (up to 2.5 s more, then 0.4 s of stillness)
            float settle = 0f, extra = 0f;
            while (!_quit && Sim.Phase == PlayPhase.PreSnap && extra < 2.5f && Stage != null && Stage.CarryActive && settle < 0.4f)
            {
                float dt = Time.deltaTime > 0f ? Mathf.Min(Time.deltaTime, 0.05f) : 0.0167f;
                if (Stage.BreakInProgress) settle = 0f; else settle += dt;
                Sim.Tick(dt, default(ControlInput), KickUserPreSnap());
                Stage.Present(dt);
                extra += dt; _preClock -= dt;
                PresentFrame(dt, 0f, _preT + t + extra);
                if (Ui != null) Ui.SetScore(MakeScore(false, _preClock, _preT + t + extra));
                yield return null;
            }
            Stage.EndCadence();
        }
    }
}
