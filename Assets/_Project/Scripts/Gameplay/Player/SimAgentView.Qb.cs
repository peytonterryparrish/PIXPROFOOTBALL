using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 QBSTANCE: the quarterback's drop-back and pocket on the pose layer (E-069, E-073, E-074, E-077, E-078, E-079). The mocap drop takes hopped (51 to 63 percent of the frames with both feet in the air) and were scrubbed by the distance travelled;
    /// the quarterback now drops and stands on the <see cref="StanceKind.QbDrop"/> / <see cref="StanceKind.QbPocket"/> poses: the simulation moves and turns the body (reverse pivot under centre), the <see cref="ProcGait"/> places the feet so one is always on the turf,
    /// the shoulders and the head look back to the field, the ball rides at the chest. Called first by <see cref="QbFlow"/>; false = the old flow handles the state (hand-off, play fake, scramble, roll-out).
    /// </summary>
    public partial class SimAgentView
    {
        bool QbPoseFlow(AgentAction action, in AnimHints h, bool shotgunQb, float dt)
        {
            _qbPoseOn = false;
            _qbSpecial = 0;
            if (DebugNoFlow || DebugNoStancePose || (!h.IsQb && h.QbSpecial == 0) || Rig == null || !Rig.Valid || StanceLayer == null || !StanceLayer.Ok) return false;
            if (h.QbSpecial != 0 && Role == PlayerRole.QB && !DebugNoFlow && !DebugNoStancePose && Rig != null && Rig.Valid && StanceLayer != null && StanceLayer.Ok && action != AgentAction.Stance)
            {
                // victory formation / clock play: the kneel-down and the spike are driven by the pose layer (the mocap takes never kneel)
                if (h.Qb == QbPhase.PreSnap || h.Qb == QbPhase.WaitSnap) { _qbStanceHold = true; return true; }      // waiting for the snap: the under-centre / shotgun stance pose stays
                if (h.QbSpecial == 1 && (h.Qb == QbPhase.Kneel || action == AgentAction.Fallen))
                {
                    _qbSpecial = 1;
                    if (action == AgentAction.Fallen) { if (!_kneeling) { _kneeling = true; _kneelSince = _clock; } _specialU = Mathf.Clamp01((_clock - _kneelSince) / 0.55f); } else { _kneeling = false; _specialU = 0f; }
                    if (_qbMode != QbMode.Pocket) EnterPocket(h);
                    return true;
                }
                if (h.QbSpecial == 2 && h.Qb == QbPhase.Spike)
                {
                    _qbSpecial = 2; _specialU = Mathf.Clamp((h.QbClock - 0.30f) / 0.40f, 0f, 1.5f);
                    if (_qbMode != QbMode.Pocket) EnterPocket(h);
                    return true;
                }
            }
            switch (action)
            {
                case AgentAction.Idle: case AgentAction.Run: case AgentAction.Sprint: case AgentAction.Backpedal: case AgentAction.Shuffle: case AgentAction.Carry: case AgentAction.Block: case AgentAction.Route: break;
                case AgentAction.Handoff: if (h.Qb != QbPhase.Fake) return false; break;      // the play fake: no solo hand-off take, the ball is carried out toward the runner
                default: return false;
            }
            if (!h.IsQb) return false;
            var q = h.Qb;
            bool fake = q == QbPhase.Fake;
            bool dropping = q == QbPhase.Dropback && !h.Rolling && h.DropSteps > 0;
            bool pocket = q == QbPhase.Set || (q == QbPhase.Dropback && !h.Rolling && !dropping);
            if (!dropping && !pocket && !fake) return false;
            if (fake) { if (!_wasFake) _fakeSince = _clock; _wasFake = true; } else _wasFake = false;
            _qbPoseOn = true; _qbPoseKind = dropping ? StanceKind.QbDrop : StanceKind.QbPocket;
            if (Motion != null) { Motion.SetCarriage(_lefty ? BodyMotion.Carriage.Chest : BodyMotion.Carriage.Chest); Motion.CarriageFloor = 0.7f; }
            if (_qbMode != QbMode.Pocket) EnterPocket(h); else PocketLife(h);
            return true;
        }
    }
}
