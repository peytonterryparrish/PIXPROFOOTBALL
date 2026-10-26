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
            if (_qbPre == QbPre.Walk && !DebugNoFlow && Rig != null && Rig.Valid)
            {
                // v0.10.0 QB: walking up to the line / in behind the centre: the real locomotion walk plays (no pose layer, no stance hold); he is grounded by the walk cycle, the stage moves him at its speed
                _qbStanceHold = false; if (_ownsClip) { Rig.Stop(0.25f); _ownsClip = false; }
                _qbMode = QbMode.None; if (Motion != null) Motion.SetCarriage(BodyMotion.Carriage.None);
                return true;
            }
            _qbSecure = false; _qbScrStart = false;
            if (h.IsQb) QbSeqTrack(h);
            if (QbMocapFlow(action, h, shotgunQb, dt)) return true;      // v0.13.0 QB-MOCAP: the quarterback's mocap windows (stance, count, snap, pocket, throw); false = the pose layer / the old flow
            if (QbThrowFlow(h, dt)) { _qbPoseOn = false; _qbStanceHold = false; if (Motion != null) { Motion.SetCarriage(BodyMotion.Carriage.None); Motion.CarriageFloor = 0f; } if (_ownsClip && Rig != null) { Rig.Stop(0.15f); _ownsClip = false; } return true; }      // v0.11.0 QB: the throw is the pose layer's
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
                case AgentAction.Handoff: if (h.Qb != QbPhase.Fake && h.Qb != QbPhase.Dropback && !(h.QbHandoffPlan && (h.Qb == QbPhase.Mesh || h.Qb == QbPhase.Done))) return false; break;      // the Handoff action outlives the fake by 0.2 s: the drop that follows it is the pose layer too, never the mocap hand-off take      // v0.10.0 QB: the hand-off's quarterback side is posed here (the back keeps his pair take)      // the play fake: no solo hand-off take, the ball is carried out toward the runner
                default: return false;
            }
            if (!h.IsQb) return false;
            var q = h.Qb;
            bool fake = q == QbPhase.Fake;
            bool dropping = q == QbPhase.Dropback && !h.Rolling && h.DropSteps > 0;
            bool stanceMove = !h.QbTakeoff && (q == QbPhase.Scramble || (q == QbPhase.Dropback && h.Rolling));      // v0.11.0 QB (note 8): escaping the pocket, working across the line and rolling out are the throwing stance in motion (shuffle / slide steps, ball high at the chest), never the run cycle with the ball at the ear
            bool watching = !QbV11.Legacy && q == QbPhase.Done && h.BallReleased && !h.QbHandoffPlan && action != AgentAction.Handoff;      // v0.11.0 QB: after the throw he settles in the stance and watches the ball
            bool pocket = q == QbPhase.Set || (q == QbPhase.Dropback && !h.Rolling && !dropping) || stanceMove || watching;
            _gait.StepScale = 1f;
            _gait.ReachH = (_qbPoseOn || dropping || stanceMove || watching || q == QbPhase.Set || q == QbPhase.Mesh || q == QbPhase.Done) && StanceLayer != null ? (dropping && !shotgunQb ? 0.44f : 0.58f) * StanceLayer.Scale : 0f;
            _gait.ReachOnly = dropping && !shotgunQb; _gait.SwingOverride = _gait.ReachOnly ? 0.34f : 0f;      // v0.10.0 QB: an N-step drop is N landings (long unhurried swings, a step only when the planted foot trails by its reach)
            _gait.PivotSlack = _gait.ReachOnly ? 25f : 0f;
            _qbMeshEta = h.QbMeshEta; _qbUcNow = h.UnderCenter; _qbEmpty = !QbV11.Legacy && h.Qb == QbPhase.Done && !h.Carrying;      // v0.11.0 QB: the ball is gone (thrown / handed off / pitched): relaxed open hands, not a ball held at the chest
            bool mesh = q == QbPhase.Mesh && h.QbHandoffPlan;
            bool carryOut = h.QbHandoffPlan && q == QbPhase.Done && _seqHadMesh && _clock - _seqEnter < QbSeqTiming.MeshFollowSeconds && !h.BallReleased;
            if (mesh || carryOut)
            {
                // v0.10.0 QB: the hand-off, quarterback side: secure, the open step, both hands reach into the runner's pocket for the 0.30 s before the exchange tick, the empty arms finish the fake and the eyes go to the field
                _qbPoseOn = true; _qbPoseKind = StanceKind.QbPocket; _qbMeshOn = mesh; _qbCarryOut = carryOut;
                if (mesh && _seqFromSnap && _clock - _seqEnter < QbSeqTiming.Secure(shotgunQb)) { _qbSecure = true; _qbSecureU = (_clock - _seqEnter) / QbSeqTiming.Secure(shotgunQb); }
                if (carryOut) _qbCarryU = (_clock - _seqEnter) / QbSeqTiming.MeshFollowSeconds;
                if (Motion != null) { Motion.SetCarriage(carryOut ? BodyMotion.Carriage.None : BodyMotion.Carriage.Chest); Motion.CarriageFloor = carryOut ? 0f : 0.7f; }
                if (_qbMode != QbMode.Pocket) EnterPocket(h);
                return true;
            }
            _qbMeshOn = false; _qbCarryOut = false;
            if (dropping && _seqFromSnap && _clock - _seqEnter < QbSeqTiming.Secure(shotgunQb))
            {
                // v0.10.0 QB: the SECURE: the snap is in his hands, the ball is pulled to the belly and nothing else moves (the simulation holds the body for the same window)
                _qbSecure = true; _qbSecureU = (_clock - _seqEnter) / QbSeqTiming.Secure(shotgunQb);
                _qbPoseOn = true; _qbPoseKind = StanceKind.QbDrop;
                if (Motion != null) { Motion.SetCarriage(BodyMotion.Carriage.Chest); Motion.CarriageFloor = 0.7f; }
                if (_qbMode != QbMode.Pocket) EnterPocket(h);
                return true;
            }
            if (ScrambleStartPose && q == QbPhase.Scramble && _seqFromPocket && _clock - _seqEnter < QbSeqTiming.ScrambleStartSeconds && !h.Rolling)
            {
                // v0.10.0 QB: pocket to scramble is a transition (the ball up to the ear, the trunk into the run, the first drive step), the simulation holds his speed down for the same 0.34 s; the locomotion takes over at its exit window
                _qbScrStart = true; _qbScrU = (_clock - _seqEnter) / QbSeqTiming.ScrambleStartSeconds;
                _qbPoseOn = true; _qbPoseKind = StanceKind.QbDrop;
                if (Motion != null) { Motion.SetCarriage(BodyMotion.Carriage.Ear); Motion.CarriageFloor = 0f; }
                return true;
            }
            if (!dropping && !pocket && !fake) return false;
            if (fake) { if (!_wasFake) _fakeSince = _clock; _wasFake = true; } else _wasFake = false;
            _qbPoseOn = true; _qbPoseKind = dropping ? StanceKind.QbDrop : StanceKind.QbPocket;
            if (Motion != null) { Motion.SetCarriage(_lefty ? BodyMotion.Carriage.Chest : BodyMotion.Carriage.Chest); Motion.CarriageFloor = 0.7f; }
            if (_qbMode != QbMode.Pocket) EnterPocket(h); else if (q == QbPhase.Set && (QbV11.Legacy || _simVel.magnitude < 0.8f)) PocketLife(h);      // v0.11.0 QB: the pump fake is for a quarterback standing in the pocket
            return true;
        }
    }
}
