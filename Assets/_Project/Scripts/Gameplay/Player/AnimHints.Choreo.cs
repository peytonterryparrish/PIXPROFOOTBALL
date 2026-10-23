namespace PixFootball.Gameplay
{
    /// <summary>v0.10.0 CHOREO: the booking the simulation's choreographer made for this player's action (catch, interception), and the clip time the booking is at right now.</summary>
    public partial struct AnimHints
    {
        public PixFootball.Sim.ChoreoBooking Booked;
        public float BookedClipTime;
        /// <summary>Name of the A take of the hand-off pair both actors play (chosen once from the speeds of the quarterback and the back); null = the old seeded pick.</summary>
        public string HandoffPick;
    }
}
