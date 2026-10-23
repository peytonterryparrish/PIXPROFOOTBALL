using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.9.0 QBSTANCE: the extra facts the quarterback / kicker presentation needs.</summary>
    public partial struct AnimHints
    {
        /// <summary>Unity-space direction of the opponent's goal (the quarterback looks back over his shoulder toward it during the drop).</summary>
        public Vector3 Downfield;
        /// <summary>World point of the receiver he is reading / throwing to (his head), and whether there is one.</summary>
        public Vector3 LookPoint; public bool HasLook;
        /// <summary>Left-handed passer (9 percent): the throw clips are the mirror images and the ball is carried on the other side.</summary>
        public bool LeftHanded;
        /// <summary>Unity-space direction from the passer to the throw target while a throw is on.</summary>
        public Vector3 TargetDir;
        /// <summary>The play is a snap read by the quarterback under centre (true) or in the shotgun / pistol (false).</summary>
        public bool UnderCenter;
        /// <summary>Kickoff kind (Deep / Onside / Squib / Rugby) for the kick clip choice.</summary>
        public int KickKind;
        /// <summary>The long snapper of a punt / field goal (his snap is the underhand long-snap take, not a quarterback throw).</summary>
        public bool Snapper;
        /// <summary>1 = kneel-down play, 2 = spike (the quarterback of a victory-formation / clock play), 0 = none; and the quarterback clock (seconds since the snap).</summary>
        public int QbSpecial; public float QbClock;
        /// <summary>v0.10.0 QB: the play is a hand-off (not a toss): the quarterback poses the open step, the reach into the runner's pocket and the carry-out.</summary>
        public bool QbHandoffPlan;
        /// <summary>v0.10.0 QB: seconds until the simulated exchange (the stage predicts it from the closing speed; -1 = not in a hand-off). The sync frame of the quarterback's reach.</summary>
        public float QbMeshEta;
    }
}
