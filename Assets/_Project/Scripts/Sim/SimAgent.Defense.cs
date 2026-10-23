using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>v0.10.0 defensive state of an agent (kept out of the main SimAgent file so the defense track owns it).</summary>
    public sealed partial class SimAgent
    {
        /// <summary>Alignment technique number of a down lineman at the snap (0 nose, 1, 2, 3, 4, 5, 7, 9); -1 for everybody else.</summary>
        public int Technique = -1;
        /// <summary>The gap his technique gives him (a 3-technique has the B gap, a 1-technique the A gap, a 5-technique the C gap...). Valid when <see cref="HasTechGap"/>.</summary>
        public RunGap TechGap;
        public bool HasTechGap;

        /// <summary>v0.10.0: the man defender whose receiver stayed in to block has no man: he is a free defender (rat / blitzer) from <see cref="FreeSince"/> on.</summary>
        public bool IsFree;
        public float FreeSince;
        /// <summary>The man this defender covered at the snap (kept after he is freed, for the film room and the tests).</summary>
        public int OriginalCoverTarget = -1;
        /// <summary>Counters of rule decisions made this play (the coverage probe and the tests read them): man-to-free releases, pass-offs, matches.</summary>
        public int RuleReleases, RulePassOffs, RuleMatches;
    }
}
