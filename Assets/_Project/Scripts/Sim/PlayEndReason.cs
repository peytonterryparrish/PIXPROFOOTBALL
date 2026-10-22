using System;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.7: WHY the play ended, in terms a viewer can verify on screen (playtest note 3, "play ended with no tackle / no obvious reason").
    /// Every play that ends publishes exactly one of these through <see cref="PlaySimulation.EndInfo"/>; the HUD shows <see cref="Text"/>.
    /// </summary>
    public enum PlayEndKind
    {
        None,
        /// <summary>A defender made the tackle (<see cref="PlayEndReason.Tackler"/>).</summary>
        Tackle,
        Sack,
        Safety,
        OutOfBounds,
        /// <summary>A defender shoved the carrier over the sideline.</summary>
        PushedOutOfBounds,
        /// <summary>Pass hit the ground / nobody caught it.</summary>
        Incomplete,
        /// <summary>The receiver had it and dropped it.</summary>
        Dropped,
        /// <summary>A defender batted the pass away.</summary>
        BrokenUp,
        Throwaway,
        Interception,
        Touchdown,
        /// <summary>The carrier dove / went to the ground on his own, or the whistle came with no one in reach (forward progress stops).</summary>
        ForwardProgress,
        FumbleRecovered,
        FumbleTurnover,
        Kneel,
        Spike,
        Touchback,
        FairCatch,
        FieldGoalGood,
        FieldGoalMissed,
        KickBlocked,
        PuntDowned,
        KickOutOfBounds,
        /// <summary>Dead-ball foul before the snap or a foul that stopped the play.</summary>
        Penalty,
    }

    /// <summary>One play-end explanation. <see cref="Text"/> is the headline ("TACKLED BY #52"), <see cref="Detail"/> an optional second line.</summary>
    public sealed class PlayEndReason
    {
        public PlayEndKind Kind;
        public string Text = "";
        public string Detail = "";
        /// <summary>Simulation agent ids (0-21) of the main actor (tackler, sacker, receiver, kicker) and the other party (carrier, passer); -1 = none.</summary>
        public int Primary = -1, Secondary = -1;
        /// <summary>Jersey numbers for the same two agents (0 = unknown).</summary>
        public int PrimaryNumber, SecondaryNumber;
        public TackleType Tackle;
        /// <summary>Simulation time of the end and the ball spot (y) it was ruled at.</summary>
        public float Time, SpotY;
        /// <summary>True when the reason has a visible cause on the field (a tackler in contact, a ball on the ground, a sideline, a kick result...).</summary>
        public bool HasVisibleCause = true;

        public override string ToString() { return Kind + ": " + Text + (Detail.Length > 0 ? " (" + Detail + ")" : ""); }
    }

    public sealed partial class PlaySimulation
    {
        /// <summary>Why the play ended (null while it is live). Set by <see cref="EndPlay"/>.</summary>
        public PlayEndReason EndInfo { get; private set; }

        private int Jersey(int agentId)
        {
            if (agentId < 0 || agentId >= 22) return 0;
            Player p = Agents[agentId].Player;
            return p != null && p.Bio != null ? p.Bio.JerseyNumber : 0;
        }

        /// <summary>"#52" for a known jersey number; an unnumbered player (test rosters, free agents) is named by position instead.</summary>
        private static string Num(int n) { return n > 0 ? "#" + n : (n < 0 ? "THE " + ((Position)(-n - 1)).ToString() : "A DEFENDER"); }

        private int JerseyOrPos(int agentId)
        {
            int j = Jersey(agentId);
            if (j > 0 || agentId < 0 || agentId >= 22) return j;
            return -(1 + (int)Agents[agentId].PlayPos);
        }

        private PlayEndReason BuildEndInfo(EndReason reason)
        {
            var r = new PlayEndReason { Time = Time, SpotY = EndY };
            SimAgent c = Carrier;
            int carrierId = c != null ? c.Id : (LastOffenseCarrier >= 0 ? LastOffenseCarrier : -1);
            Action<PlayEndKind, string, int, int> set = (k, t, a, b) =>
            {
                r.Kind = k; r.Text = t; r.Primary = a; r.Secondary = b;
                r.PrimaryNumber = Jersey(a); r.SecondaryNumber = Jersey(b);
            };

            if (Kind == PlayKind.Kneel) { set(PlayEndKind.Kneel, "KNEEL DOWN", 0, -1); return r; }
            if (Kind == PlayKind.Spike) { set(PlayEndKind.Spike, "SPIKED TO STOP THE CLOCK", 0, -1); return r; }

            if (Kind == PlayKind.Kickoff || Kind == PlayKind.Punt || Kind == PlayKind.FieldGoal) return BuildKickEndInfo(r, reason, set);

            if (reason == EndReason.Touchdown)
            {
                set(PlayEndKind.Touchdown, "TOUCHDOWN", carrierId, -1);
                if (r.PrimaryNumber > 0) r.Text = "TOUCHDOWN  #" + r.PrimaryNumber;
                return r;
            }
            if (reason == EndReason.Safety || SafetyScored)
            {
                if (QbSack) set(PlayEndKind.Safety, "SAFETY - SACKED BY " + Num(JerseyOrPos(SackerId)), SackerId, 0);
                else set(PlayEndKind.Safety, "SAFETY", TacklerId, carrierId);
                if (QbSack && Jersey(SackerId) > 0) r.Text = "SAFETY - SACKED BY #" + Jersey(SackerId);
                return r;
            }
            if (reason == EndReason.Sack || QbSack)
            {
                set(PlayEndKind.Sack, "SACKED BY " + Num(JerseyOrPos(SackerId >= 0 ? SackerId : TacklerId)), SackerId >= 0 ? SackerId : TacklerId, 0);
                r.Tackle = LastTackleType;
                return r;
            }
            if (reason == EndReason.Incomplete)
            {
                // the closest cause on the log: a throwaway, a batted ball or a drop, otherwise the ball hit the grass
                for (int i = Log.Events.Count - 1; i >= 0; i--)
                {
                    PlayEvent e = Log.Events[i];
                    if (e.Type == SimEventType.Deflection) { set(PlayEndKind.BrokenUp, "PASS BROKEN UP BY " + Num(JerseyOrPos(e.AgentA)), e.AgentA, TargetId); return r; }
                    if (e.Type == SimEventType.Drop) { set(PlayEndKind.Dropped, "DROPPED BY " + Num(JerseyOrPos(e.AgentA)), e.AgentA, PasserId); return r; }
                    if (e.Type == SimEventType.Throwaway) { set(PlayEndKind.Throwaway, "THROWN AWAY", PasserId, -1); return r; }
                    if (e.Type == SimEventType.Throw) { if (e.Note == "away") { set(PlayEndKind.Throwaway, "THROWN AWAY", PasserId, -1); return r; } break; }
                }
                set(PlayEndKind.Incomplete, "INCOMPLETE", PasserId, TargetId);
                if (TargetId >= 0 && Jersey(TargetId) > 0) r.Detail = "intended for #" + Jersey(TargetId);
                return r;
            }
            if (reason == EndReason.OutOfBounds)
            {
                if (LastTackleType == TackleType.PushOutOfBounds) set(PlayEndKind.PushedOutOfBounds, "PUSHED OUT BY " + Num(JerseyOrPos(TacklerId)), TacklerId, carrierId);
                else set(PlayEndKind.OutOfBounds, "OUT OF BOUNDS", carrierId, -1);
                return r;
            }
            if (reason == EndReason.TimeLimit)
            {
                set(PlayEndKind.ForwardProgress, "FORWARD PROGRESS", carrierId, -1);
                r.HasVisibleCause = false;
                return r;
            }
            if (reason == EndReason.FumbleLost || (Turnover && TurnoverFumble))
            {
                int rec = LastRecoveryAgent();
                set(PlayEndKind.FumbleTurnover, "FUMBLE - RECOVERED BY " + Num(JerseyOrPos(rec)), rec, -1);
                return r;
            }
            if (reason == EndReason.Interception || TurnoverInterception)
            {
                set(PlayEndKind.Interception, "INTERCEPTED BY " + Num(JerseyOrPos(CatcherId)), CatcherId, PasserId);
                return r;
            }
            if (reason == EndReason.Tackle)
            {
                if (TacklerId >= 0 && LastTackleType != TackleType.None)
                {
                    r.Tackle = LastTackleType;
                    set(PlayEndKind.Tackle, (LastTackleType == TackleType.GangTackle ? "GANG TACKLE BY " : "TACKLED BY ") + Num(JerseyOrPos(TacklerId)), TacklerId, carrierId);
                    return r;
                }
                int rec = LastRecoveryAgent();
                if (rec >= 0)
                {
                    set(PlayEndKind.FumbleRecovered, "FUMBLE - RECOVERED BY " + Num(JerseyOrPos(rec)), rec, -1);
                    return r;
                }
                set(PlayEndKind.ForwardProgress, "RUNNER DOWN", carrierId, -1);
                return r;
            }
            set(PlayEndKind.ForwardProgress, "FORWARD PROGRESS", carrierId, -1);
            r.HasVisibleCause = false;
            return r;
        }

        private int LastRecoveryAgent()
        {
            for (int i = Log.Events.Count - 1; i >= 0; i--) if (Log.Events[i].Type == SimEventType.Recovery) return Log.Events[i].AgentA;
            return -1;
        }

        private PlayEndReason BuildKickEndInfo(PlayEndReason r, EndReason reason, Action<PlayEndKind, string, int, int> set)
        {
            int kicker = KickerId >= 0 ? KickerId : (Specials != null && Specials.KickerAgent != null ? Specials.KickerAgent.Id : -1);
            if (Kind == PlayKind.FieldGoal)
            {
                string what = IsExtraPoint ? "EXTRA POINT" : "FIELD GOAL";
                if (KickBlocked) { set(PlayEndKind.KickBlocked, what + " BLOCKED", LastBlockerId(), kicker); return r; }
                if (KickGood) { set(PlayEndKind.FieldGoalGood, what + " IS GOOD", kicker, -1); return r; }
                if (Log.Has(SimEventType.FieldGoalMissed)) { set(PlayEndKind.FieldGoalMissed, what + " NO GOOD", kicker, -1); return r; }
                if (reason == EndReason.Touchdown) { set(PlayEndKind.Touchdown, "TOUCHDOWN", Carrier != null ? Carrier.Id : -1, -1); return r; }
            }
            if (reason == EndReason.Touchdown) { set(PlayEndKind.Touchdown, "TOUCHDOWN", Carrier != null ? Carrier.Id : -1, -1); return r; }
            if (KickBlocked) { set(PlayEndKind.KickBlocked, "KICK BLOCKED", LastBlockerId(), kicker); return r; }
            if (KickTouchback) { set(PlayEndKind.Touchback, "TOUCHBACK", Carrier != null ? Carrier.Id : -1, -1); return r; }
            if (KickFairCatch) { set(PlayEndKind.FairCatch, "FAIR CATCH", ReturnerFromLog(), -1); if (r.PrimaryNumber > 0) r.Text = "FAIR CATCH  #" + r.PrimaryNumber; return r; }
            if (KickOob) { set(PlayEndKind.KickOutOfBounds, "KICK OUT OF BOUNDS", kicker, -1); return r; }
            if (KickRecoveredByKicker) { int rec = LastRecoveryAgent(); set(PlayEndKind.FumbleRecovered, "RECOVERED BY " + Num(JerseyOrPos(rec)) + " (KICKING TEAM)", rec, -1); return r; }
            if (TacklerId >= 0 && reason == EndReason.Tackle)
            {
                r.Tackle = LastTackleType;
                set(PlayEndKind.Tackle, "TACKLED BY " + Num(JerseyOrPos(TacklerId)), TacklerId, Carrier != null ? Carrier.Id : -1);
                return r;
            }
            if (reason == EndReason.OutOfBounds) { set(PlayEndKind.OutOfBounds, "OUT OF BOUNDS", Carrier != null ? Carrier.Id : -1, -1); return r; }
            for (int i = Log.Events.Count - 1; i >= 0; i--)
                if (Log.Events[i].Type == SimEventType.Whistle) { set(PlayEndKind.PuntDowned, "BALL DOWNED BY " + Num(JerseyOrPos(Log.Events[i].AgentA)), Log.Events[i].AgentA, -1); return r; }
            set(PlayEndKind.ForwardProgress, Carrier != null ? "RUNNER DOWN" : "BALL DEAD", Carrier != null ? Carrier.Id : -1, -1);
            return r;
        }

        private int LastBlockerId()
        {
            for (int i = Log.Events.Count - 1; i >= 0; i--) if (Log.Events[i].Type == SimEventType.KickBlocked) return Log.Events[i].AgentA;
            return -1;
        }

        private int ReturnerFromLog()
        {
            for (int i = Log.Events.Count - 1; i >= 0; i--) if (Log.Events[i].Type == SimEventType.FairCatch) return Log.Events[i].AgentA;
            return -1;
        }
    }
}
