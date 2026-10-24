using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>Kind of direction change a ball carrier has decided on (v0.11.0 RB).</summary>
    public enum CarryCutKind
    {
        None,
        /// <summary>Plain plant-and-go: a lane change of 36-70 degrees made off one foot with a hip sink.</summary>
        Plant,
        /// <summary>Jump cut: a sharp (70+ degrees) cut made right in front of a defender, a short hop-step off the outside foot.</summary>
        Jump,
        /// <summary>Cut-back: against the grain of the designed run or the pursuit flow (the back side of the play).</summary>
        CutBack,
        /// <summary>Bounce: the designed hole is shut, the runner presses outside toward the sideline.</summary>
        Bounce
    }

    public enum CarryCutPhase { None, Approach, Plant, Drive }

    /// <summary>Master switch of the v0.11.0 ball-carrier style (QA A/B: the view sets it from the command line).</summary>
    public static class CarryStyleConfig
    {
        public static bool Enabled = System.Environment.GetEnvironmentVariable("PIX_CARRY_STYLE_OFF") != "1";      // QA A/B: set PIX_CARRY_STYLE_OFF=1 for the v0.10 carrier
        /// <summary>Planned moves: the move is decided this long before it is executed (the view plays its take across the lead).</summary>
        public static float MoveLead = 0.42f;
    }

    /// <summary>
    /// v0.11.0 RB track (owner note 7): what the simulation has decided about HOW the ball carrier runs, before it happens. The view reads these fields and plays the matching style
    /// (arm that carries the ball, press / plant / drive of a cut, move take with its lead). Pure data; <see cref="CarrierAI"/> (CarrierAI.Style.cs) fills it every tick.
    /// </summary>
    public sealed partial class SimAgent
    {
        // ---- carry arm ("away from the nearest defender"; switches on cuts across the field)
        /// <summary>+1 = right arm, -1 = left arm, relative to his body (0 = not carrying). The view animates the ball across the chest when it changes.</summary>
        public int CarryArm;
        public float CarryArmSince = -9f;
        public int CarryArmPrev;
        internal int CarryArmWant; internal float CarryArmWantSince;

        // ---- posture
        /// <summary>0..1: pressing a hole / defender (shorter, choppier steps, lower, ball covered).</summary>
        public float CarryPress;
        /// <summary>0..1: lowered centre of gravity (traffic, plant, contact coming).</summary>
        public float CarryCrouch;
        /// <summary>Both hands on the ball (a defender is about to hit him).</summary>
        public bool CarryTwoHands;
        /// <summary>Running the sideline (within two yards of the line).</summary>
        public bool CarryTight;
        /// <summary>The defender nearest to the carrier (id, -1 none) and his lateral side relative to the carrier's heading (+1 right).</summary>
        public int CarryThreat = -1; public int CarryThreatSide;

        // ---- cut: Approach (brake, shorten, sink) -> Plant (outside foot down) -> Drive (re-accelerate)
        public CarryCutPhase CutPhase;
        public CarryCutKind CutKind;
        /// <summary>Signed turn of the cut in degrees (+ = to the right).</summary>
        public float CutAngle;
        /// <summary>Foot that plants (+1 right, -1 left): the outside foot, opposite to the direction of the cut.</summary>
        public int CutPlantFoot;
        /// <summary>Seconds until the plant (Approach), seconds since it (Plant / Drive).</summary>
        public float CutEta, CutT;
        internal Vec2 CutDir, CutHold; internal float CutCooldownUntil, CutEtaTotal;

        // ---- planned move (decided early so the view can play the entry)
        public CarrierMove PlanMove;
        public float PlanEta;
        public int PlanThreat = -1;
        internal float PlanTtc;
        /// <summary>Number of moves this carry (budget).</summary>
        public int CarryMoveCount;
    }
}
