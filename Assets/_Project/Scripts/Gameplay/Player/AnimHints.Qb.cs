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
        /// <summary>v0.11.0 QB: the quarterback is running with the ball on purpose (a takeoff, a keeper, a sprinting user passer); false = he stays in the square throwing stance, in the pocket or moving, and seconds since the takeoff began.</summary>
        public bool QbTakeoff; public float QbTakeoffSince;
    }
}

namespace PixFootball.Gameplay
{
    /// <summary>v0.13.0 QB-MOCAP: what the mocap sequence needs from the play (filled by <c>PlayStage.QbMocapHints</c>, see PlayStage.QbSeq.cs).</summary>
    public partial struct AnimHints
    {
        /// <summary>Counter of the scrimmage play (changes with every snap sequence): the mocap sequence resets and draws its clips per play.</summary>
        public int QbPlay;
        /// <summary>The cadence: its length and the time into it (seconds, -1 = no count is on).</summary>
        public float QbCadenceLen, QbCadenceT;
        /// <summary>Seconds until the snap is in his hands (the count that is left plus the flight of the ball; the ball in flight: its remaining time); -1 = unknown / none.</summary>
        public float QbSnapEta;
        /// <summary>Seconds left of the simulated wind-up (the ball leaves when it is 0 and the body is aligned); -1 = no throw.</summary>
        public float QbWindupLeft;
        /// <summary>Distance (m) from the passer to the throw target and the sim's pending style (0 auto, 1 bullet, 2 touch, 3 lob).</summary>
        public float QbTargetDist; public int QbStyle;
        /// <summary>v0.13.0 QB-2: seconds until the drop clock starts (the secure is over; <= 0 = running; -1 = no drop) and until the planned arrival at the drop point (-1 = none), the drop point (world) and the planned QB action (QbAction, -1 = none).</summary>
        public float QbDropWait, QbDropLeft; public Vector3 QbDropPt; public int QbAct;
        /// <summary>v0.13.0 QB-2: the hand-off runner: seconds to the planned exchange and the world position of the runner (zero vector = none).</summary>
        public Vector3 QbRunnerPos; public float QbRunnerGap;
    }
}
