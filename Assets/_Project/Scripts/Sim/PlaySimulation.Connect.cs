using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.9.0 CONNECT: additive, read-only hooks for the presentation layer (nothing here changes the simulation).
    /// E-006: a callback after every fixed step so the view can keep the last two states and render between them.
    /// E-274: the quarterback hooks the animation layer was missing (set time, drop progress, read target, release ETA, throwing hand, mesh point).
    /// </summary>
    public sealed partial class PlaySimulation
    {
        /// <summary>Raised after every fixed 1/60 s step of <see cref="Tick"/> (the view snapshots the agents here for render interpolation).</summary>
        public event Action<PlaySimulation> AfterStep;
        /// <summary>Number of fixed steps taken through <see cref="Tick"/> since Setup.</summary>
        public int StepCounter { get; private set; }
        /// <summary>How far the wall clock is into the next fixed step, 0..1 (the render interpolation factor between the last two states).</summary>
        public float StepAlpha { get { return Math.Max(0f, Math.Min(1f, _accum / Dt)); } }

        /// <summary>Simulation time the quarterback reached his set point (-1 before).</summary>
        public float QbSetTime { get { return Agents != null && Agents[0].QbSetAt > 0f ? Agents[0].QbSetAt : -1f; } }

        /// <summary>0..1 how far the quarterback is along his drop from the snap spot to the drop point.</summary>
        public float QbDropProgress01
        {
            get
            {
                if (Agents == null || OffForm == null || OffForm.Slots.Length == 0 || OffAI == null) return 0f;
                Vec2 start = new Vec2(BallX0 + OffForm.Slots[0].X, LosY + OffForm.Slots[0].Y);
                float total = (OffAI.DropPoint - start).Length;
                if (total < 0.3f) return 1f;
                float left = (OffAI.DropPoint - Agents[0].Pos).Length;
                return Math.Max(0f, Math.Min(1f, 1f - left / total));
            }
        }

        /// <summary>Seconds until the release while the quarterback winds up (-1 when he is not winding up).</summary>
        public float QbReleaseEta { get { return Agents != null && Agents[0].QbState == QbPhase.Windup ? Math.Max(0f, Agents[0].WindupLeft) : -1f; } }
        /// <summary>The returner signalled for a fair catch on this kick play.</summary>
        public bool KickWasFairCatch { get { return KickFairCatch; } }
        /// <summary>The kick ended in a touchback.</summary>
        public bool KickWasTouchback { get { return KickTouchback; } }
        /// <summary>Every quarterback is right-handed in this game (user note 1 is about the kickers; the throwing hand is a constant until handedness is data).</summary>
        public bool QbThrowsLeft { get { return false; } }
        /// <summary>Where the quarterback and the back meet for the hand-off (field frame, yards); zero when there is no hand-off.</summary>
        public Vec2 MeshPoint { get { return OffAI != null ? OffAI.MeshPointNow : Vec2.Zero; } }
    }
}
