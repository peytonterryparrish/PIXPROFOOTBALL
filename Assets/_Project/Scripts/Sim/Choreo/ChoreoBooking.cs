using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    public enum ChoreoKind { None, Tackle, Catch, Handoff, GetUp }

    /// <summary>The lifecycle of a booking: planned (a forecast the steering works on) -> playing (the take owns the actors' sim positions) -> done.</summary>
    public enum ChoreoPhase { Forecast, Playing, Done }

    /// <summary>
    /// An interaction the choreographer has decided on: who, which take (or pair of takes), where and when the sync frame happens, how the take is placed on the field, and when each actor is handed
    /// back to the simulation. While a booking is <see cref="ChoreoPhase.Playing"/> the simulated position and facing of an owned actor FOLLOW the take's root motion, so nothing glides.
    /// The Unity layer reads a booking to start the same clips at the same moment (<see cref="ClipTime"/>) and does not move the actors itself.
    /// </summary>
    public sealed class ChoreoBooking
    {
        public int Id;
        public ChoreoKind Kind;
        public PairKind PairKind;
        public ChoreoPhase Phase;
        /// <summary>Actor A (the carrier, receiver, quarterback: the offense) and actor B (the tackler, defender). -1 when absent.</summary>
        public int A = -1, B = -1;
        public ChoreoPair Pair;
        public ChoreoClip ClipA, ClipB;
        /// <summary>Placement of the take(s) on the field.</summary>
        public ChoreoRigid Align;
        /// <summary>Clip seconds per simulation second (the pace of the game).</summary>
        public float Rate = 1f;
        /// <summary>Simulation time (the choreographer's clock) the clips start, and the clip time they start at.</summary>
        public float StartSim, StartT;
        /// <summary>Simulation time the warp of the placement residual and the carried momentum started (never re-anchored when the clip is re-timed).</summary>
        public float WarpStart;
        /// <summary>Simulation time of the sync frame (the pair's contact moment, the catch frame).</summary>
        public float SyncSim;
        /// <summary>Clip time at which each actor is handed back to the simulation.</summary>
        public float ExitTA, ExitTB;
        /// <summary>The actor's sim position follows the take.</summary>
        public bool OwnsA, OwnsB;
        /// <summary>Residual between the simulated position at the start and the take's root there (yards), decayed over <see cref="WarpOut"/> seconds, and the momentum carried through (yards per second).</summary>
        public Vec2 ResidA, ResidB, SlideA, SlideB;
        public float WarpOut = 0.25f;
        /// <summary>Planned approach geometry (forecast): where the pair will meet and when.</summary>
        public Vec2 SyncPos;
        public float Eta;
        /// <summary>Statistics: largest correction applied in one tick (yards), simulated time spent following.</summary>
        public float MaxStepErr;
        public int FramesFollowed;
        /// <summary>Where each owned actor was at the end of the previous follow tick (the audit reads the largest visible displacement of one tick, <see cref="MaxStepErr"/>).</summary>
        public Vec2 LastA, LastB; public bool HaveA, HaveB;
        public Vec2 RootLastA, RootLastB;
        /// <summary>Set by the view when it has started the clips of this booking.</summary>
        public bool ViewStarted;
        /// <summary>Catch bookings: the defender plays the ball (interception / swat) instead of a receiver catching it; the world point where the hands meet the ball and the clip time of the hand-close moment.</summary>
        public bool Defender;
        /// <summary>A late tackler who piles on: only actor B (the joiner) follows the take, aligned to the carrier who is already down.</summary>
        public bool Joiner;
        public Vec3 HandPoint;
        public float HandCloseT;

        /// <summary>Clip time of the take at a choreographer time.</summary>
        /// <summary>v0.10.0 lead-in warp: until the contact the take plays at <see cref="RateIn"/> (the speed that makes the clip's travel match the bodies'), after it at <see cref="Rate"/>; 0 = one rate throughout.</summary>
        public float RateIn, SyncClipT; public bool WarpDone;
        public float ClipTime(float simTime) { return RateIn > 0f ? (simTime < SyncSim ? SyncClipT - (SyncSim - simTime) * RateIn : SyncClipT + (simTime - SyncSim) * Rate) : StartT + (simTime - StartSim) * Rate; }
        /// <summary>Simulation time at which the actor is released.</summary>
        public float ReleaseSimA { get { return RateIn > 0f ? SyncSim + (ExitTA - SyncClipT) / Rate : StartSim + (ExitTA - StartT) / Rate; } }
        public float ReleaseSimB { get { return RateIn > 0f ? SyncSim + (ExitTB - SyncClipT) / Rate : StartSim + (ExitTB - StartT) / Rate; } }
        public bool Involves(int agent) { return agent >= 0 && (A == agent || B == agent); }
        public override string ToString() { return Kind + "/" + PairKind + " a=" + A + " b=" + B + " " + (Pair != null ? Pair.A.Name : (ClipA != null ? ClipA.Name : "")); }
    }

    /// <summary>Counters of one play's choreography (the audit and the tests read them).</summary>
    public sealed class ChoreoStats
    {
        public int TurnBookings, TurnTicks, DuelRolls, DuelBookings, DuelCatches; public int Forecasts, Bookings, TackleBookings, CatchBookings, HandoffBookings, GetUpBookings, PileBookings;
        /// <summary>Forecast quality: absolute error of the predicted contact time, measured at the moment the contact really happened using the forecast made about 0.5 s earlier (seconds).</summary>
        public readonly List<float> EtaError = new List<float>();
        /// <summary>Largest per-tick correction the steering applied to any agent's velocity (yards per second).</summary>
        public float MaxSteerRate;
        /// <summary>Largest difference between the sim position and the take's root plus the intended residual after a tick (yards): ~0 when the sim follows the take.</summary>
        public float MaxFollowErr;
        public int SteeredTicks;
        public int FollowTicks;
        /// <summary>Ticks an actor was owned by a take, and the ticks (by seconds since the booking: under 0.3, 0.3 to 0.6, over 0.6) where his visible step differed from the take's own root step by more than 5 cm (the warp of the placement and the carried momentum are the only reasons).</summary>
        public int OwnedTicks; public readonly int[] ExcessTicks = new int[3], OwnedByAge = new int[3];
    }
}
