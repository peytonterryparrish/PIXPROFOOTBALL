using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.12.0 NATURAL: the view side of <see cref="NaturalBody"/> (his personal style, the layer calls).</summary>
    public partial class SimAgentView
    {
        /// <summary>The natural-movement layer of this body (null until <see cref="CaptureRest"/> ran).</summary>
        public NaturalBody Nat;
        public NaturalStyle NatStyle { get { return Nat != null ? Nat.Style : NaturalStyle.Neutral; } }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void NaturalSwitch()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-pixNatOff") >= 0) NaturalBody.Enabled = false;      // v0.12.0 NATURAL: the GAIT state (A/B on the same tree)
            if (System.Array.IndexOf(args, "-pixNatNoSecondary") >= 0) NaturalBody.NoSecondary = true;
            if (System.Array.IndexOf(args, "-pixNatNoIdle") >= 0) NaturalBody.NoIdle = true;
            if (System.Array.IndexOf(args, "-pixNatNoLook") >= 0) NaturalBody.NoLook = true;
            if (System.Array.IndexOf(args, "-pixNatNoTempo") >= 0) NaturalBody.NoTempo = true;
            if (System.Array.IndexOf(args, "-pixNatNoFacing") >= 0) NaturalBody.NoFacing = true;
        }


        void NaturalInit()
        {
            if (Avatar == null || Avatar.Animator == null) return;
            Nat = new NaturalBody(Avatar.Animator);
            Nat.SetStyle(NaturalStyle.For(AgentId + 77, Size, Role));
        }

        void NaturalIdentity(int playerId, Position pos)
        {
            if (Nat == null) return;
            Nat.SetStyle(NaturalStyle.For(playerId > 0 ? playerId : 90000 + AgentId, Size, RoleUtil.Of(pos)));      // a roster player has his own id; the stub players of the audits (id 0) are told apart by their slot
        }

        bool _natCarry;
        /// <summary>Playback-rate factor of his own running cadence for the matcher (1 with the layer off). The matcher's stride warp takes the difference back, so his speed stays the simulation's.</summary>
        float NaturalTempo() { return Nat != null && Nat.Ok && NaturalBody.Enabled && !NaturalBody.NoTempo ? Nat.Style.TempoAt(_clock) : 1f; }

        /// <summary>LateUpdate, after the animation and before the foot planter: the running body's secondary motion and the standing body's micro-motion.</summary>
        void NaturalSecondaryLate(float dt)
        {
            if (Nat == null || !Nat.Ok || Motion == null) return;
            var m = Mm;
            bool free = !ContactDriven && !(Ragdoll != null && Ragdoll.Active) && !DebugNoGait;
            bool owned = free && m != null && m.Ready && m.Layer > 0.5f && (Rig == null || Rig.Weight < 0.5f);
            Nat.Secondary(dt, Motion.SpeedNow, Motion.TravelDir, owned, _natCarry || Role == PlayerRole.QB, Role == PlayerRole.QB);
            bool plainStand = _lastAction == AgentAction.Idle && !StanceLayerOn && !(m != null && m.Ready && m.Layer > 0.5f) && (Rig == null || Rig.Weight < 0.3f);
            bool standSet = _lastAction == AgentAction.Stance && !StanceLayerOn && LooseStance(Stance.Kind);      // a stance set that is a plain stand (receivers, corners, safeties, backers, backs): breathing and weight shifts on top of its clip; never a hand on the turf
            bool standing = free && (plainStand || standSet) && Motion.SpeedNow < 0.35f;
            Nat.Idle(dt, standing, Forward, standSet ? 0.4f : 1f);
        }

        static bool LooseStance(StanceKind k)
        {
            switch (k)
            {
                case StanceKind.Upright: case StanceKind.BackTwoPoint: case StanceKind.ReceiverSplit: case StanceKind.ReceiverSlot: case StanceKind.LinebackerTwoPoint: case StanceKind.CornerPress: case StanceKind.CornerOff: case StanceKind.SafetyDeep:
                case StanceKind.Gunner: case StanceKind.KickoffRunner: case StanceKind.Returner: case StanceKind.Kicker: case StanceKind.Punter: case StanceKind.LinebackerBlitz: case StanceKind.KickoffReceive: return true;
                default: return false;
            }
        }

        /// <summary>LateUpdate, on the final pose of a free body: the look stays inside the limits and rates of <see cref="NaturalLook"/>.</summary>
        void NaturalLookLate(float dt)
        {
            if (Nat == null || !Nat.Ok) return;
            bool free = !ContactDriven && !(Ragdoll != null && Ragdoll.Active);
            bool chest = false, neck = false;
            if (free)
            {
                switch (_lastAction)
                {
                    case AgentAction.Idle: case AgentAction.Run: case AgentAction.Sprint: case AgentAction.Route: case AgentAction.Carry: case AgentAction.Backpedal: case AgentAction.Shuffle:
                        neck = true; chest = !StanceLayerOn; break;
                    case AgentAction.Stance:
                        neck = true; chest = !StanceLayerOn && Role != PlayerRole.OL && Role != PlayerRole.DL && Role != PlayerRole.QB; break;
                    case AgentAction.Block: case AgentAction.Engaged: case AgentAction.Shed: case AgentAction.Catch: case AgentAction.Jump:
                        neck = true; break;      // the chest twist is the move; the head still stays within a neck's range
                    default: break;
                }
            }
            Nat.LookLimit(dt, chest, neck);
        }

        // ------------------------------------------------------------------------------------------ eased facing
        float _turnVel;
        /// <summary>Largest turn rate (deg/s) of a standing / walking body and the angular acceleration the turn may build up with (a person starts and ends a turn over a few frames; he does not snap).</summary>
        public static float NatTurnStand = 400f, NatTurnAccel = 4200f;
        static bool FreeTurn(AgentAction a)
        {
            switch (a) { case AgentAction.Idle: case AgentAction.Run: case AgentAction.Sprint: case AgentAction.Route: case AgentAction.Carry: case AgentAction.Backpedal: case AgentAction.Shuffle: return true; default: return false; }
        }

        /// <summary>The visible body heading follows the simulated facing at a bounded rate that grows with the speed (400 deg/s standing, the old rate at a run) with a bounded angular acceleration, so a turn builds up and settles instead of starting and stopping at full rate.</summary>
        float TurnYaw(float cur, float target, float turnRate, AgentAction action, Vector3 velocity, float dt)
        {
            if (!NaturalBody.Enabled || NaturalBody.NoFacing || ContactDriven || Huddle || !FreeTurn(action) || (Ragdoll != null && Ragdoll.Active) || dt <= 1e-5f) { _turnVel = 0f; return Mathf.MoveTowardsAngle(cur, target, turnRate * dt); }
            float sp = new Vector2(velocity.x, velocity.z).magnitude;
            float maxRate = Mathf.Lerp(Mathf.Min(NatTurnStand, turnRate), turnRate, Mathf.InverseLerp(1.5f, 5f, sp));
            return NaturalLook.TurnStep(cur, target, ref _turnVel, maxRate, NatTurnAccel, dt);
        }
    }
}
