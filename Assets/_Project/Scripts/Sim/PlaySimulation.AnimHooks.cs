using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.7.0 read-only hooks for the animation layer (Track A): where the quarterback's drop-back ends, how many steps it is, whether it is a roll-out, and who takes the hand-off.
    /// The presentation uses them to scrub the drop-back take by the distance he has left to travel and to start a hand-off take so its exchange frame lands on the simulated exchange.
    /// </summary>
    public sealed partial class PlaySimulation
    {
        /// <summary>Where the quarterback's drop-back ends (field frame, yards).</summary>
        public Vec2 QbDropPoint { get { return OffAI != null ? OffAI.DropPoint : Vec2.Zero; } }
        /// <summary>0 (no drop), 3, 5 or 7 steps of the called play.</summary>
        public int QbDropSteps { get { return OffAI != null ? OffAI.DropSteps : 0; } }
        /// <summary>The drop-back is a roll-out / boot to the side.</summary>
        public bool QbRolling { get { return OffAI != null && OffAI.Rolling; } }
        /// <summary>Agent id of the runner the quarterback hands the ball to (-1 = none).</summary>
        public int QbHandoffTargetId { get { return OffAI != null ? OffAI.HandoffTargetId : -1; } }
        /// <summary>True for a called hand-off (not a toss / option read, whose exchange is not predictable).</summary>
        public bool QbPlanIsHandoff { get { return OffAI != null && OffAI.QbPlanAction == QbAction.Handoff; } }
    }
}
