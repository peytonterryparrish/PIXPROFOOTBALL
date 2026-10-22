using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>v0.7.0 read-only hooks for the animation layer (additive; nothing here changes the simulation).</summary>
    internal sealed partial class OffenseAI
    {
        internal Vec2 DropPoint { get { return _dropPoint; } }
        internal int DropSteps { get { return _qa != null ? _qa.DropbackSteps : 0; } }
        internal bool Rolling { get { return _rolling; } }
        internal int HandoffTargetId { get { return _handoffTarget != null ? _handoffTarget.Id : -1; } }
        internal QbAction QbPlanAction { get { return _qa != null ? _qa.Action : QbAction.DropbackPass; } }
    }
}
