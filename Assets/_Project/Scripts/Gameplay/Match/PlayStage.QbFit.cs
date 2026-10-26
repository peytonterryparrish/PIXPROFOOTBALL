using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    public sealed partial class PlayStage
    {
        /// <summary>
        /// v0.13.0 QB-2: before the snap the quarterback's view draws the catch-and-drop take for this drop (<see cref="SimAgentView.QbDropFitPlan"/>) and the simulation moves its drop point along its line to the take's distance (at most 40 percent) and, when the take
        /// would have to be played at less than 0.6 or more than 2 times its speed, stretches the drop time to the take's: the take then plays at its own scale and its planted feet stay planted. Only the plain drop-back passes (a roll-out, a hand-off and a play fake have their own takes).
        /// </summary>
        void QbDropRefit(PlaySimulation sim, PlayFrame frame)
        {
            var v = _views[0];
            if (v == null || sim == null || sim.Kind != PlayKind.Scrimmage || DebugNoQbSequence || sim.Phase != PlayPhase.PreSnap || sim.OffForm == null || sim.OffForm.Slots.Length == 0) return;
            int steps = sim.QbDropSteps; if (steps <= 0 || sim.QbRolling) return;
            var act = (QbAction)sim.QbPlanActionId;
            if (act != QbAction.DropbackPass && act != QbAction.QuickPass && act != QbAction.ScreenPass && act != QbAction.HailMary) return;
            bool sg = sim.OffForm.Slots[0].Y < -2.5f;
            Vec2 start = new Vec2(sim.BallX0 + sim.OffForm.Slots[0].X, sim.LosY + sim.OffForm.Slots[0].Y);
            Vec2 d = sim.QbDropPoint - start; float D = d.Length * PlayFrame.YardsToMeters; if (D < 0.3f) return;
            Vector3 dirSim = frame.DirToUnity(d.Norm()), down = frame.DirToUnity(new Vec2(0f, 1f));
            float yawBase = Mathf.Atan2(down.x, down.z) * Mathf.Rad2Deg, k, dur;
            if (v.QbDropFitPlan(_qbPlayNo, steps, sg, yawBase, dirSim, D, sim.QbDropDuration, out k, out dur)) sim.QbRefitDrop(k, dur);
        }
    }
}
