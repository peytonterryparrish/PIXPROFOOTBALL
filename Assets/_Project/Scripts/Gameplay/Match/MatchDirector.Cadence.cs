using System.Collections;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 QBSTANCE (E-054, E-085): the quarterback's cadence between the snap request and the snap. The button (or the CPU's timer) used to snap the ball on the same frame; now the quarterback calls it:
    /// his head swivels, he bounces on his feet, and on the "go" (the last 0.12 s) his hips drop about 6 in so the hands stay under the centre, then the ball is snapped. A user press and a CPU snap take the same path.
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
            Stage.BeginCadence(len);
            float t = 0f;
            while (t < len && !_quit)
            {
                float dt = Time.deltaTime > 0f ? Mathf.Min(Time.deltaTime, 0.05f) : 0.0167f;
                Sim.Tick(dt, default(ControlInput), KickUserPreSnap());
                Stage.Present(dt);
                t += dt;
                yield return null;
            }
            Stage.EndCadence();
        }
    }
}
