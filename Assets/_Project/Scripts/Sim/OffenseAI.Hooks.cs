using System;
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
        /// <summary>v0.9.0 CONNECT E-274: the receiver the quarterback is reading right now.</summary>
        internal int ReadTargetId { get { return _reads.Count > 0 && _qb != null ? _reads[Math.Min(Math.Max(0, _qb.ReadIdx), _reads.Count - 1)] : -1; } }
        /// <summary>v0.9.0 CONNECT E-274: where a hand-off exchange happens (the runner's position a moment ahead).</summary>
        internal Vec2 MeshPointNow { get { return _handoffTarget != null ? _handoffTarget.Pos + _handoffTarget.Vel * 0.18f : Vec2.Zero; } }
    }
}
