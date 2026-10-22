using System.Collections;
using PixFootball.Sim;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// What the MatchDirector asks of the broadcast layer (implemented by PixFootball.Presentation.BroadcastDirector; the Gameplay assembly cannot reference Presentation).
    /// The broadcast layer owns the replay recorder, the camera director, the TV overlays and the halftime / postgame shows. Every member is optional for the match to run:
    /// without an implementation the director plays exactly like v0.4.
    /// </summary>
    public interface IBroadcast
    {
        void Attach(MatchDirector director);
        /// <summary>True while a replay or a show owns the screen and the input (the director's loops idle and the pause controller stays out of the way).</summary>
        bool Busy { get; }
        /// <summary>The game intro (sky camera over the stadium, matchup card, stadium and weather card) before the opening kickoff.</summary>
        IEnumerator GameIntro();
        /// <summary>A play was built (formation on the field, before the snap). Called again if the play is rebuilt (audible, play changed).</summary>
        void OnPlayPrepared(PlaySimulation sim, PlayFrame frame);
        /// <summary>Every pre-snap frame. <paramref name="snapping"/> is true on the frame the snap happens: running shots must end that instant so a cut never hides the start of a play.</summary>
        void OnPreSnapFrame(float dt, float t, float playClock, bool snapping);
        void OnSnap(PlaySimulation sim);
        /// <summary>Every frame of the live play and of the wind-down after the whistle (after the stage presented the simulation).</summary>
        void OnPlayFrame(float dt, bool live);
        void OnPlayEvent(PlayEvent e);
        void OnImpact(ContactImpact impact);
        /// <summary>The play has been applied to the game state (banner shown): start the post-play shots, reactions and overlays.</summary>
        void BeginPostPlay(PlayOutcome outcome, PlaySimulation sim);
        /// <summary>After the wind-down: the rest of the post-play shots, the automatic replay, the drive summary.</summary>
        IEnumerator FinishPostPlay();
        IEnumerator Halftime();
        /// <summary>The postgame show; <see cref="PostgameShown"/> tells the director whether the show already ended with the return to the menu.</summary>
        IEnumerator Postgame();
        bool PostgameShown { get; }
        /// <summary>Pause menu entry: starts the manual instant replay (free camera) of the most recent plays.</summary>
        void RequestManualReplay();
        bool HasReplay { get; }
    }
}

namespace PixFootball.Gameplay
{
    /// <summary>v0.8 Franchise: the broadcast layer owns the live box score (player lines, team box scores, drives, scoring); the match asks it to fill the <see cref="PixFootball.League.GameResult"/> it hands to the career.</summary>
    public interface IBroadcastResult
    {
        void FillResult(PixFootball.League.GameResult r);
    }
}
