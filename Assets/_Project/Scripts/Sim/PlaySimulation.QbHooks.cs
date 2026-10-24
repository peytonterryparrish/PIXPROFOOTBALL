namespace PixFootball.Sim
{
    /// <summary>v0.9.0 QBSTANCE: read-only hook for the presentation (the receiver the quarterback is looking at).</summary>
    public sealed partial class PlaySimulation
    {
        /// <summary>Agent id of the receiver the quarterback reads / throws to (-1 = none).</summary>
        public int QbLookTargetId { get { return OffAI != null ? OffAI.QbLookTargetId : -1; } }
        /// <summary>Agent id of the runner of a play-action fake (-1 = none).</summary>
        public int QbFakeTargetId { get { return OffAI != null ? OffAI.QbFakeTargetId : -1; } }
        /// <summary>v0.10.0 QB: agent id of the field-goal holder (-1 = none): the ball is spotted at his knee.</summary>
        public int KickHolderId { get { return Specials != null && Specials.HolderAgent != null && Kind == PlayKind.FieldGoal ? Specials.HolderAgent.Id : -1; } }
        /// <summary>v0.11.0 QB: the quarterback is running with the ball on purpose (takeoff, keeper, sprinting user passer); false = he is in the throwing stance, in the pocket or on the move.</summary>
        public bool QbTakeoff { get { return OffAI != null && OffAI.QbTakeoffNow; } }
        /// <summary>v0.11.0 QB: simulation time the takeoff began (-1 = none).</summary>
        public float QbTakeoffAt { get { return OffAI != null ? OffAI.QbTakeoffAt : -1f; } }
        /// <summary>QA: the quarterback has decided to run (the audit measures against it in the v0.10.0 A/B mode too).</summary>
        public bool QbTakeoffIntent { get { return OffAI != null && OffAI.QbTakeoffIntentNow; } }
    }
}
