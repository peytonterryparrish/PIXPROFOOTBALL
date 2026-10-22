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
            if (a.PlayPos == Position.QB && a.Player != null && a.Player.Bio != null && a.Player.Bio.Hand == PixFootball.League.Handedness.Left) h.LeftHanded = true;
            if (a.Side == SimSide.Offense && a.Slot == 0 && Sim.Kind == PlayKind.Scrimmage)
            {
                int tid = a.QbState == QbPhase.Fake ? Sim.QbFakeTargetId : Sim.QbLookTargetId;
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
                    if (b.State == BallState.Snap) anchor = new Vector3(ballW.x, ballW.y, ballW.z);
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
                _qbDrop = Mathf.MoveTowards(_qbDrop, go && kind == StanceKind.QbUnderCenter ? 0.15f : 0f, dt / 0.12f);
                d.HipsDrop = _qbDrop;
                if (cad && !go) d.HipsDrop += 0.025f * Mathf.Max(0f, Mathf.Sin(_cadenceT * 9.5f));      // the bounce of the cadence
                if (!go) { d.HeadYaw = 26f * Mathf.Sin(_qbDropT * 1.15f) * (0.6f + 0.4f * Mathf.Sin(_qbDropT * 0.37f)); d.HeadPitch = 2f * Mathf.Sin(_qbDropT * 0.8f); }
            }
            else if (!qbStand && i == 0) { _qbDrop = 0f; }
            // motion facts the pose layer uses for the quarterback (and the footfall planner of every body in the layer)
            Vector3 down = Frame.DirToUnity(new Vec2(0f, 1f));
            Vector3 look = Vector3.zero; bool hasLook = false;
            if (a.Side == SimSide.Offense && a.Slot == 0 && Sim.Kind == PlayKind.Scrimmage)
            {
                int tid = a.QbState == QbPhase.Fake ? Sim.QbFakeTargetId : Sim.QbLookTargetId;
                if (tid >= 0 && tid < 22) { look = WorldPos(tid) + Vector3.up * 1.75f; hasLook = true; }
            }
            bool lefty = a.Player != null && a.Player.Bio != null && a.Player.Bio.Hand == PixFootball.League.Handedness.Left && a.PlayPos == Position.QB;
            v.SetMotionInputs(Frame.VelToUnity(a.Vel), down, look, hasLook, lefty);
            if (a.Side == SimSide.Offense && a.Slot == 0 && Sim.Kind == PlayKind.Scrimmage && (a.QbState == QbPhase.Windup || Sim.Ball.State != BallState.Pass)) BallSpinHint.PassSpin = BallSpinHint.For(a.PendingStyle);
            v.StanceInput = d;
        }
    }
}
