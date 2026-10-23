namespace PixFootball.Sim
{
    /// <summary>v0.10.0 CHOREO: the sequence planner of this play (null when the options carry no clip library).</summary>
    public sealed partial class PlaySimulation
    {
        /// <summary>The choreographer of this play: forecasts the interactions, steers the paths into them and moves the actors along the booked takes. Null when <see cref="PlaySimOptions.ChoreoLibrary"/> is not set.</summary>
        public Choreographer Choreo { get; private set; }

        void InitChoreo()
        {
            Choreo = Options != null && Options.ChoreoLibrary != null ? new Choreographer(this, Options.ChoreoLibrary) : null;
        }
    }
}
