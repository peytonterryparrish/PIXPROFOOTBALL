using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 QBSTANCE: what the stance pose layer needs from the play: the play context of a stance (pass / run set, press call, edge, goal line) and the per-frame inputs of the pose
    /// (the centre's rear for the quarterback's hands, the incoming snap for the shotgun catch, the ball for the kicker's eyes, the quarterback's cadence).
    /// </summary>
    public sealed partial class PlayStage
    {
        StanceContext StanceCtx(SimAgent a)
        {
            var c = new StanceContext();
            var op = Sim.OffPlay;
            if (Sim.Kind == PlayKind.Scrimmage && op != null) { c.PassPlay = op.IsPass; c.RunPlay = !op.IsPass; }
            if (a.Side == SimSide.Defense)
            {
                var ca = a.Assign as CoverageAssignment;
                if (ca != null) { c.PressKnown = true; c.Press = ca.Press; }
                var ru = a.Assign as RushAssignment; var bl = a.Assign as BlitzAssignment;
                if ((ru != null && ru.Contain) || (bl != null && bl.Edge)) c.Edge = true;
                c.GoalLine = Sim.DefForm != null && Sim.DefForm.Unit == "4-4-3";
            }
            return c;
        }

        /// <summary>v0.9.0 QBSTANCE hints: the opponent goal direction, the passer's hand, the receiver he is reading, the kick kind.</summary>
        void AddQbHints(ref AnimHints h, SimAgent a, SimAgentView v)
        {
            h.Downfield = Frame.DirToUnity(new Vec2(0f, 1f));
            h.UnderCenter = !_shotgun;
            h.QbHandoffPlan = Sim.Kind == PlayKind.Scrimmage && Sim.QbPlanIsHandoff;
            h.QbMeshEta = -1f;
            QbStanceHints(ref h, a);      // v0.11.0 QB
            if (h.QbHandoffPlan && a.Side == SimSide.Offense && a.Slot == 0 && a.QbState == QbPhase.Mesh && Sim.QbHandoffTargetId > 0)
            {
                // the exchange happens when the runner is 1.3 yd away (OffenseAI.QbMesh): time to it from the closing speed
                var tg = Sim.Agents[Sim.QbHandoffTargetId]; Vec2 dd = tg.Pos - a.Pos; float dist = dd.Length;
                float closeSpeed = Mathf.Max(1.2f, (a.Vel - tg.Vel).Dot(dd.Norm()));
                h.QbMeshEta = Mathf.Max(0f, (dist - 1.3f) / closeSpeed);
            }
            if (a.PlayPos == Position.QB && a.Player != null && a.Player.Bio != null && a.Player.Bio.Hand == PixFootball.League.Handedness.Left) h.LeftHanded = true;
            if (a.Side == SimSide.Offense && a.Slot == 0 && Sim.Kind == PlayKind.Scrimmage)
            {
                int tid = a.QbState == QbPhase.Fake ? Sim.QbFakeTargetId : (a.QbState == QbPhase.Mesh && Sim.QbPlanIsHandoff ? Sim.QbHandoffTargetId : Sim.QbLookTargetId);
                if (tid >= 0 && tid < 22) { h.LookPoint = WorldPos(tid) + Vector3.up * 1.75f; h.HasLook = true; Vector3 d = WorldPos(tid) - WorldPos(a.Id); d.y = 0f; h.TargetDir = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.zero; }
            }
            if (a.Side == SimSide.Offense && a.Slot == 0 && (Sim.Kind == PlayKind.Kneel || Sim.Kind == PlayKind.Spike)) { h.QbSpecial = Sim.Kind == PlayKind.Kneel ? 1 : 2; h.Qb = a.QbState; h.QbClock = a.QbClock; }
            if (h.IsQb && (a.QbState == QbPhase.Dropback || a.QbState == QbPhase.Set || a.QbState == QbPhase.Scramble || a.QbState == QbPhase.Windup)) h.Carrying = false;      // v0.9.0 (E-080): a passer on the move runs on the plain locomotion; the ear / chest carriage owns the ball, the tucked-ball carry tree does not fight it
            var ka = Sim.OffPlay != null && Sim.OffPlay.Assignments != null && Sim.OffPlay.Assignments.Length > 0 ? Sim.OffPlay.Assignments[0] as KickAssignment : null;
            if (ka != null && (a.PlayPos == Position.K || a.PlayPos == Position.P)) h.KickKind = (int)ka.KickType;
            h.Snapper = a.Side == SimSide.Offense && a.PlayPos == Position.C && (Sim.Kind == PlayKind.Punt || Sim.Kind == PlayKind.FieldGoal);
        }

        float _qbDrop, _qbDropT;
        float _cadenceLen = -1f, _cadenceT;
        /// <summary>The quarterback starts his cadence (called by the match director between the snap request and the snap).</summary>
        public void BeginCadence(float seconds) { _cadenceLen = seconds; _cadenceT = 0f; }
        public void EndCadence() { _cadenceLen = -1f; _cadenceT = 0f; }

        /// <summary>Hands the stance pose layer of <paramref name="v"/> its per-frame inputs. Called for every player before his view is applied.</summary>
        void UpdateStanceInput(int i, SimAgent a, SimAgentView v, float dt)
        {
            if (v == null) return;
            var d = default(StanceDyn);
            var kind = v.Stance.Kind;
            bool qbStand = a.Side == SimSide.Offense && a.Slot == 0 && Sim.Kind == PlayKind.Scrimmage && (a.Action == AgentAction.Stance || a.QbState == QbPhase.WaitSnap || a.QbState == QbPhase.PreSnap);
            var b = Sim.Ball;
            Vector3 ballW = Frame.ToUnity(b.Pos);
            switch (kind)
            {
                case StanceKind.CenterSnap: case StanceKind.LongSnapper:
                    d.Anchor = ballW; d.HasAnchor = true; break;
                case StanceKind.QbUnderCenter:
                {
                    // hands under the centre: the back of his hips (the ball, once the snap is on its way)
                    int ci = -1; for (int j = 1; j < 11; j++) if (Sim.Agents[j].PlayPos == Position.C) { ci = j; break; }
                    Vector3 anchor = ballW + v.Forward * -0.40f;
                    if (ci >= 0 && _views[ci] != null && _views[ci].Avatar != null && _views[ci].Avatar.Animator != null)
                    {
                        var hb = _views[ci].Avatar.Animator.GetBoneTransform(HumanBodyBones.Hips);
                        if (hb != null) anchor = hb.position - v.Forward * 0.14f;
                    }
                    // v0.10.0 QB: the hands STAY under the centre while the snap comes up into them (they used to chase the ball, which is why it was "in his hands" two frames before the catch)
                    d.Anchor = anchor; d.HasAnchor = true;
                    break;
                }
                case StanceKind.QbShotgun:
                {
                    // the snap reaches him: both hands go to the ball as it comes in
                    if (b.State == BallState.Snap)
                    {
                        Vector3 chest = v.transform.position + Vector3.up * 1.2f;
                        float dist = (ballW - chest).magnitude;
                        d.Reach = ballW; d.ReachW = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4.5f, 1.1f, dist));
                    }
                    else if (b.State == BallState.Held && b.Holder == a.Id && Ball != null)
                    {
                        // v0.10.0 QB: the caught snap rides in the hands as they bring it in (the hold socket and the extended hands used to be 26 cm apart for the first 8 frames)
                        float since = Time.time - _holderSince;
                        if (since < 0.28f) { d.Reach = Ball.Position; d.ReachW = 1f - Mathf.SmoothStep(0f, 1f, since / 0.28f); }
                    }
                    break;
                }
                case StanceKind.Kicker: case StanceKind.Punter: case StanceKind.Holder:
                    d.Look = ballW; d.LookW = 0.85f; break;
            }
            if (qbStand && (kind == StanceKind.QbUnderCenter || kind == StanceKind.QbShotgun))
            {
                // the cadence: a head on a swivel while he reads the defence, the hips drop about 6 in on the snap call so the hands stay under the centre
                _qbDropT += dt;
                bool cad = _cadenceLen > 0f; if (cad) _cadenceT += dt;
                bool go = a.QbState == QbPhase.WaitSnap || (cad && _cadenceT > _cadenceLen - 0.14f);
                _qbDrop = Mathf.MoveTowards(_qbDrop, go && kind == StanceKind.QbUnderCenter ? 0.15f : 0f, (QbV11.Legacy ? 1f : 0.15f) * dt / 0.12f);      // v0.11.0 QB (B-021): the step was dt / 0.12 = 0.139 m a frame (the whole 0.15 m drop in ONE frame: the hips pop at the call); now 0.15 m over 0.12 s
                d.HipsDrop = _qbDrop;
                if (cad && !go) d.HipsDrop += 0.025f * Mathf.Max(0f, Mathf.Sin(_cadenceT * 9.5f));      // the bounce of the cadence
                if (!go) { d.HeadYaw = 26f * Mathf.Sin(_qbDropT * 1.15f) * (0.6f + 0.4f * Mathf.Sin(_qbDropT * 0.37f)); d.HeadPitch = 2f * Mathf.Sin(_qbDropT * 0.8f); }
            }
            else if (!qbStand && i == 0) { if (QbV11.Legacy) _qbDrop = 0f; else { _qbDrop = Mathf.MoveTowards(_qbDrop, 0f, 0.15f * dt / 0.14f); d.HipsDrop = _qbDrop; } }      // v0.11.0 QB (B-021): the hips come back up over 0.14 s after the snap (the 0.15 m drop of the call used to vanish in one frame)
            // motion facts the pose layer uses for the quarterback (and the footfall planner of every body in the layer)
            Vector3 down = Frame.DirToUnity(new Vec2(0f, 1f));
            Vector3 look = Vector3.zero; bool hasLook = false;
            if (a.Side == SimSide.Offense && a.Slot == 0 && Sim.Kind == PlayKind.Scrimmage)
            {
                int tid = a.QbState == QbPhase.Fake ? Sim.QbFakeTargetId : (a.QbState == QbPhase.Mesh && Sim.QbPlanIsHandoff ? Sim.QbHandoffTargetId : Sim.QbLookTargetId);
                if (tid >= 0 && tid < 22) { look = WorldPos(tid) + Vector3.up * 1.75f; hasLook = true; }
            }
            QbEyesOverride(ref look, ref hasLook, a);      // v0.11.0 QB: after the release his eyes follow the ball
            bool lefty = a.Player != null && a.Player.Bio != null && a.Player.Bio.Hand == PixFootball.League.Handedness.Left && a.PlayPos == Position.QB;
            v.SetMotionInputs(Frame.VelToUnity(a.Vel), down, look, hasLook, lefty);
            if (a.Side == SimSide.Offense && a.Slot == 0 && Sim.Kind == PlayKind.Scrimmage && (a.QbState == QbPhase.Windup || Sim.Ball.State != BallState.Pass)) BallSpinHint.PassSpin = BallSpinHint.For(a.PendingStyle);
            QbFlightHints(a);      // v0.11.0 QB
            if (i == 0 && a.Side == SimSide.Offense && Sim.Kind == PlayKind.Scrimmage && (a.QbState == QbPhase.Mesh || a.QbState == QbPhase.Fake || a.QbState == QbPhase.Done))
            {
                // v0.10.0 QB: where the runner's pocket is (his belly, a hand's breadth toward the quarterback), handed to the quarterback's pose with a marker weight: his arms reach for it at the sync frame
                int rid = a.QbState == QbPhase.Fake ? Sim.QbFakeTargetId : Sim.QbHandoffTargetId;
                if (rid > 0 && rid < 22 && _views[rid] != null && _views[rid].Avatar != null && _views[rid].Avatar.Animator != null)
                {
                    var hb = _views[rid].Avatar.Animator.GetBoneTransform(HumanBodyBones.Hips);
                    if (hb != null) { Vector3 toQ = v.transform.position - hb.position; toQ.y = 0f; toQ = toQ.sqrMagnitude > 1e-4f ? toQ.normalized : Vector3.back; d.Reach = hb.position + toQ * 0.12f + Vector3.up * 0.10f + Frame.VelToUnity(Sim.Agents[rid].Vel) * 0.02f; d.ReachW = 0.005f; }
                }
            }
            v.StanceInput = d;
            KickTarget(i, a, v);      // v0.10.0 QB: the kick event (note 7)
            ReceiverEyes(i, a, v);      // v0.11.0 QB: eyes on the ball
        }
    }
}
