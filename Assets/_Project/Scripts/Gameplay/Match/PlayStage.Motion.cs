using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 MOTION: hands the simulation's planned path to the locomotion matcher of a view. The sim decides where every agent will be at +0.33 / +0.66 / +1.0 s (route stems and breaks for receivers,
    /// the desired velocity through the body's response for everybody else); the view only follows.
    /// </summary>
    public sealed partial class PlayStage
    {
        void MotionPlanHook(SimAgent a, SimAgentView v)
        {
            if (v == null || a == null || Sim == null) return;
            Vec2 p1, p2, p3;
            Sim.PlanPoints(a, MotionDb.Horizon[0], MotionDb.Horizon[1], MotionDb.Horizon[2], out p1, out p2, out p3);
            Vector3 c = Frame.ToUnity(a.Pos);
            v.SetMotionPlan(Frame.ToUnity(p1) - c, Frame.ToUnity(p2) - c, Frame.ToUnity(p3) - c);
        }
    }
}
