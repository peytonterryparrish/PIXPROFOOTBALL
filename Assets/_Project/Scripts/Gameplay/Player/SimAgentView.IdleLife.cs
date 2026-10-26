using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.13.0 NATURAL-2: the view side of <see cref="IdleLife"/> (a floor of life for bodies that stand without moving). One-line hook in <c>SimAgentView.LateUpdate</c>, last.</summary>
    public partial class SimAgentView
    {
        IdleLife _idleLife;

        /// <summary>LateUpdate, last: breathing, a weight shift in the trunk, a drifting head and arms that are never quite still, while the pose given to us has not moved for a moment (see <see cref="IdleLife"/>).</summary>
        void IdleLifeLate(float dt)
        {
            IdleLifeBody(dt);
            SpineRateLate(dt);      // v0.13.0 NATURAL-3: the trunk turns at most CapDegS against the frame before, on the very last pose
        }

        SpineRate _spineRate;
        void SpineRateLate(float dt)
        {
            if (!SpineRate.Enabled || Avatar == null || Avatar.Animator == null || !Avatar.Animator.isHuman) return;
            if (_spineRate == null) _spineRate = new SpineRate(Avatar.Animator, transform);
            bool active = !ContactDriven && !(Ragdoll != null && Ragdoll.Active) && _lastAction != AgentAction.Tackled && _lastAction != AgentAction.Fallen && _lastAction != AgentAction.GetUp;
            _spineRate.Late(dt, active);
        }

        void IdleLifeBody(float dt)
        {
            if (!IdleLife.Enabled || Avatar == null || Avatar.Animator == null || !Avatar.Animator.isHuman) return;
            if (_idleLife == null) _idleLife = new IdleLife(Avatar.Animator);
            if (!_idleLife.Ok) return;
            _idleLife.SetStyle(NatStyle);
            bool active = !ContactDriven && !(Ragdoll != null && Ragdoll.Active) && _lastAction != AgentAction.Tackled && _lastAction != AgentAction.Fallen && _lastAction != AgentAction.GetUp && !_qmOwn;
            // arms stay as the clip / the pose layer / the ball arm IK left them for a quarterback, a carrier, a hand-down stance, a block, a catch, a throw
            bool hold = Role == PlayerRole.QB || _natCarry || StanceLayerOn || _lastAction == AgentAction.Carry || _lastAction == AgentAction.Block || _lastAction == AgentAction.Engaged || _lastAction == AgentAction.Shed
                || _lastAction == AgentAction.Catch || _lastAction == AgentAction.Throw || _lastAction == AgentAction.Handoff;
            if (_lastAction == AgentAction.Stance) { var k = Stance.Kind; if (k == StanceKind.ThreePoint || k == StanceKind.FourPoint || k == StanceKind.CenterSnap) hold = true; }
            _idleLife.Late(dt, active, !hold);
        }
    }
}
