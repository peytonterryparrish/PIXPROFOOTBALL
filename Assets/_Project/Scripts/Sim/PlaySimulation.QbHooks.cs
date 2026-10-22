namespace PixFootball.Sim
{
    /// <summary>v0.9.0 QBSTANCE: read-only hook for the presentation (the receiver the quarterback is looking at).</summary>
    public sealed partial class PlaySimulation
    {
        /// <summary>Agent id of the receiver the quarterback reads / throws to (-1 = none).</summary>
        public int QbLookTargetId { get { return OffAI != null ? OffAI.QbLookTargetId : -1; } }
        /// <summary>Agent id of the runner of a play-action fake (-1 = none).</summary>
        public int QbFakeTargetId { get { return OffAI != null ? OffAI.QbFakeTargetId : -1; } }
    }
}
