using System;
using PixFootball.Sim;

namespace PixFootball.Gameplay
{
    /// <summary>v0.7 (playtest note 3): every play end publishes a <see cref="PlayEndReason"/>; this is the minimal HUD hook. Track C owns the styling of the banner.</summary>
    public sealed partial class MatchDirector
    {
        /// <summary>Raised once per finished play with the simulation's explanation of why it ended (tackler's number, sideline, incomplete, kick result...).</summary>
        public event Action<PlayEndReason, PlayOutcome> PlayEndReasonShown;

        /// <summary>The one-line reason for a finished play, "" when the headline already says everything.</summary>
        string EndReasonLine(PlayOutcome o, string headline)
        {
            PlayEndReason r = o != null ? o.EndReason : null;
            if (r == null || string.IsNullOrEmpty(r.Text)) return "";
            if (string.Equals(r.Text, headline, StringComparison.OrdinalIgnoreCase)) return "";
            return r.Text;
        }
    }
}
