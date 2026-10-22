using System;
using System.Collections.Generic;
using System.Text;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>What an agent is doing right now. The Unity layer maps this to animation states.</summary>
    public enum AgentAction
    {
        Stance, Idle, Run, Sprint, Backpedal, Shuffle,
        Block, Engaged, Shed,
        Route, Catch, Throw, Handoff, Carry,
        Juke, Spin, StiffArm, Truck, Hurdle, Dive,
        TackleWrapUp, TackleBigHit, TackleShoestring, TackleDive, TackleGang,
        Tackled, Fallen, Stumble, Kick, Jump, Celebrate
    }

    /// <summary>Tackle outcomes (descriptive, for animation and the event log).</summary>
    public enum TackleType
    {
        None, WrapUp, BigHit, Shoestring, DiveTackle, GangTackle, BrokenTackle, Drag, PushOutOfBounds
    }

    public enum SimEventType
    {
        Snap, Handoff, Pitch, Throw, Catch, Drop, Interception, Deflection,
        Tackle, BlockWin, BlockLoss, Pancake, Shed, Sack, QbHit, Fumble, Recovery,
        OutOfBounds, Touchdown, Safety, FirstDownCrossed, Penalty, Jam,
        Kick, KickLanded, FairCatch, Touchback, FieldGoalGood, FieldGoalMissed, KickBlocked,
        CarrierMove, BrokenTackle, Incomplete, Whistle, Scramble, Throwaway, Kneel, Spike, PlayEnd,
        /// <summary>v0.7: the kicker starts his approach (and the kick clip); Value = seconds until the ball is struck.</summary>
        KickApproach
    }

    /// <summary>
    /// Which pre-snap body position a player should hold (engine-side truth; <see cref="AgentAction.Stance"/> stays the single action while the ball is dead).
    /// Derived from position, alignment and formation by <see cref="PreSnapStances"/> and kept current through shifts, motion and audibles.
    /// </summary>
    public enum PreSnapStance
    {
        None, ThreePoint, FourPoint, TwoPoint, Upright,
        /// <summary>Center over the ball, hand on it.</summary>
        Snapper,
        QbUnderCenter, QbShotgun,
        /// <summary>Back standing behind the line (hands on thighs).</summary>
        BackSet,
        /// <summary>Corner within a yard or two of the receiver, hands ready to jam.</summary>
        PressCorner,
        /// <summary>Defensive back off the ball, backpedal ready.</summary>
        OffCoverage,
        Linebacker, Safety,
        /// <summary>Kicker or punter at the spot, kickoff coverage runner.</summary>
        Kicker, KickoffRunner,
        /// <summary>Holder kneeling to receive the snap.</summary>
        HolderKneel,
        Returner, Gunner
    }

    public enum PlayPhase { PreSnap, Live, Dead }

    public enum EndReason
    {
        None, Tackle, OutOfBounds, Touchdown, Incomplete, Interception, FumbleLost, Safety, Sack,
        KickResult, Kneel, Spike, Penalty, TimeLimit, DeadBall
    }

    public enum ThrowStyle { Auto, Bullet, Touch, Lob }

    public enum SimSide { Offense = 0, Defense = 1 }

    /// <summary>
    /// Per-tick user input. Move is in field frame (X lateral, Y downfield). Pulse fields (Juke..Snap) are "pressed this tick";
    /// <see cref="PlaySimulation.Tick"/> consumes them on the first fixed step of the tick. ThrowToSlot 0 means none.
    /// </summary>
    public struct ControlInput
    {
        public Vec2 Move;
        public bool Sprint, Protect;
        public bool JukeLeft, JukeRight, Spin, StiffArm, Truck, Hurdle, Dive, Jump, Tackle, SwitchPlayer, Snap, Throwaway;
        /// <summary>Offense slot (1, 7-10) to throw to; 0 = no throw this tick.</summary>
        public int ThrowToSlot;
        public ThrowStyle ThrowStyle;
        /// <summary>Lead nudge in yards (field frame) added to the computed catch point.</summary>
        public Vec2 LeadNudge;
        /// <summary>Kick meter (user kickers): pulse when the kick is struck. The three readings below travel with the pulse.</summary>
        public bool KickPress;
        /// <summary>Power fill chosen by the player, 0..1 (1 = full leg).</summary>
        public float KickPower;
        /// <summary>Timing reading, 0..1 (1 = perfect; see <see cref="KickMeter"/>).</summary>
        public float KickTiming;
        /// <summary>Aim reading, -1 (left) .. +1 (right); 0 = centered.</summary>
        public float KickAim;

        public static ControlInput None { get { return new ControlInput(); } }
    }

    public struct PlayEvent
    {
        public float Time;
        public SimEventType Type;
        /// <summary>Primary agent id (0-10 offense slots, 11-21 defense slots), -1 if none.</summary>
        public int AgentA;
        /// <summary>Secondary agent id, -1 if none.</summary>
        public int AgentB;
        public TackleType Tackle;
        public Vec3 Pos;
        public float Value;
        public string Note;

        public override string ToString()
        {
            return Time.ToString("0.00") + "s " + Type + (AgentA >= 0 ? " a=" + AgentA : "") + (AgentB >= 0 ? " b=" + AgentB : "")
                   + (Tackle != TackleType.None ? " [" + Tackle + "]" : "") + " @" + Pos + (Value != 0f ? " v=" + Value.ToString("0.##") : "")
                   + (string.IsNullOrEmpty(Note) ? "" : " " + Note);
        }
    }

    public sealed class PlayEventLog
    {
        public readonly List<PlayEvent> Events = new List<PlayEvent>();

        public void Add(float time, SimEventType type, int a, int b, Vec3 pos, TackleType tackle = TackleType.None, float value = 0f, string note = null)
        {
            Events.Add(new PlayEvent { Time = time, Type = type, AgentA = a, AgentB = b, Pos = pos, Tackle = tackle, Value = value, Note = note });
        }

        public int Count { get { return Events.Count; } }

        public bool Has(SimEventType t)
        {
            for (int i = 0; i < Events.Count; i++) if (Events[i].Type == t) return true;
            return false;
        }

        public int CountOf(SimEventType t)
        {
            int n = 0;
            for (int i = 0; i < Events.Count; i++) if (Events[i].Type == t) n++;
            return n;
        }

        public PlayEvent? First(SimEventType t)
        {
            for (int i = 0; i < Events.Count; i++) if (Events[i].Type == t) return Events[i];
            return null;
        }

        /// <summary>Order-sensitive FNV-1a digest of the whole log (used by determinism tests).</summary>
        public ulong Digest()
        {
            ulong h = 14695981039346656037UL;
            unchecked
            {
                for (int i = 0; i < Events.Count; i++)
                {
                    PlayEvent e = Events[i];
                    h = Mix(h, (ulong)(uint)BitConverter.SingleToInt32Bits(e.Time));
                    h = Mix(h, (ulong)(int)e.Type);
                    h = Mix(h, (ulong)(uint)e.AgentA);
                    h = Mix(h, (ulong)(uint)e.AgentB);
                    h = Mix(h, (ulong)(int)e.Tackle);
                    h = Mix(h, (ulong)(uint)BitConverter.SingleToInt32Bits(e.Pos.X));
                    h = Mix(h, (ulong)(uint)BitConverter.SingleToInt32Bits(e.Pos.Y));
                    h = Mix(h, (ulong)(uint)BitConverter.SingleToInt32Bits(e.Value));
                }
            }
            return h;
        }

        private static ulong Mix(ulong h, ulong v)
        {
            unchecked { return (h ^ v) * 1099511628211UL; }
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Events.Count; i++) sb.AppendLine(Events[i].ToString());
            return sb.ToString();
        }
    }
}
