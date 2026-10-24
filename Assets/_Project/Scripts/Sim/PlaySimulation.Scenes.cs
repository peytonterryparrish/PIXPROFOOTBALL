namespace PixFootball.Sim
{
    /// <summary>
    /// v0.11.0 SIM-FLOW: small hooks for the situational scenes that are decided in the simulation before they are shown (gang tackles, sideline plays, strips ...). Pure state, no RNG of its own.
    /// </summary>
    public sealed partial class PlaySimulation
    {
        private bool _endExtended;

        /// <summary>
        /// The wrap window of a tackle can be stretched once (a second tackler is arriving: he piles on instead of the whistle cutting the play under him). Real forward-progress whistles come with
        /// two or more men on the carrier on about a third of tackles.
        /// </summary>
        internal void ExtendEnd(float seconds)
        {
            if (_endAt < 0f || _endExtended) return;
            _endAt += seconds; _endExtended = true;
        }

        /// <summary>A strip: the pending whistle is cancelled, the ball is loose and the play goes on.</summary>
        internal void CancelEnd() { _endAt = -1f; _ruledSpotY = null; }

        /// <summary>QA: the wrap window was stretched on this play.</summary>
        public bool EndWasExtended { get { return _endExtended; } }
    }
}
