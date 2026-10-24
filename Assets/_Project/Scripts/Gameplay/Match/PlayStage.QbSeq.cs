using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 QB (playtest note 4): the quarterback's pre-snap sequence, decided by the stage before it happens (not physics): after the huddle break he WALKS up to a survey spot behind the line, stops and reads the defence (head sweep, a point at the middle of it),
    /// WALKS in behind the centre, gets low (upright to the under-centre crouch, the hands rising to the centre), and only then is the team "set": the snap waits for him (<see cref="BreakInProgress"/>).
    /// A shotgun quarterback walks to his spot, surveys and settles into the shotgun stance. The presented position of the quarterback is moved here (a walking speed profile on the real locomotion walk), the pose of each segment comes from <see cref="SimAgentView.SetQbPre"/>.
    /// </summary>
    public sealed partial class PlayStage
    {
        /// <summary>QA: switch the quarterback's pre-snap sequence off (the "before" state walks straight to the stance).</summary>
        public static bool DebugNoQbSequence;

        QbPreStage _qpStage; float _qpT, _qpSurveyLen, _qpCrouchLen;
        Vector3 _qpSurvey, _qpLook; bool _qpShotgun;
        float _qpSpeed; bool _qpHurry;

        /// <summary>The player asked for the snap while the quarterback is still on his way: the rest of the sequence is hurried (a faster walk, a short survey, a quick crouch), never skipped, so the snap is not ignored for long.</summary>
        public void QbSeqHurry() { if (QbSeqBusy) _qpHurry = true; }

        /// <summary>The quarterback sequence segments (QA reads the stage through the view's <see cref="SimAgentView.QbSeqName"/>).</summary>
        public enum QbPreStage { None, ToSurvey, Survey, StepIn, Crouch, Done }

        /// <summary>True while the quarterback is still doing his pre-snap sequence.</summary>
        public bool QbSeqBusy { get { return _qpStage != QbPreStage.None && _qpStage != QbPreStage.Done; } }

        /// <summary>Called once per play from <see cref="ConnectBegin"/> after the huddle was placed.</summary>
        void QbSeqBegin(PlaySimulation sim, PlayFrame frame, bool huddle)
        {
            _qpStage = QbPreStage.None; _qpT = 0f; _qpSpeed = 0f; _qpHurry = false;
            for (int k = 0; k < 22; k++) if (_views[k] != null) _views[k].SetQbPre(SimAgentView.QbPre.None, 0f, Vector3.zero, false, -1f);
            var v = _views[0];
            if (!huddle || DebugNoQbSequence || v == null || sim.Kind != PlayKind.Scrimmage || sim.OffForm == null || sim.OffForm.Slots.Length == 0) { if (v != null) v.SetQbPre(SimAgentView.QbPre.None, 0f, Vector3.zero, false, 0f); return; }
            var qa = sim.OffPlay != null && sim.OffPlay.Assignments != null && sim.OffPlay.Assignments.Length > 0 ? sim.OffPlay.Assignments[0] as QbAssignment : null;
            if (sim.OffPlay != null && (sim.OffPlay.Category == PlayCategory.Kneel || sim.OffPlay.Category == PlayCategory.Spike)) { v.SetQbPre(SimAgentView.QbPre.None, 0f, Vector3.zero, false, 0f); return; }
            _qpShotgun = sim.OffForm.Slots[0].Y < -2.5f;
            Vec2 fin = sim.Agents[0].Pos;
            // the survey spot: 3.2 yd behind the final spot under centre (a walk of about 3 yd back in to the centre), the final spot itself in the shotgun
            Vec2 sv = _qpShotgun ? fin : new Vec2(fin.X, fin.Y - 2.7f);
            _qpSurvey = frame.ToUnity(sv);
            _qpLook = frame.ToUnity(new Vec2(sim.BallX0, sim.LosY + 4.5f)) + Vector3.up * 1.5f;
            _qpSurveyLen = _qpShotgun ? 1.7f : 1.9f; _qpCrouchLen = _qpShotgun ? 0.55f : 0.9f;
            _qpStage = QbPreStage.ToSurvey; _g[0].Active = true; _g[0].Speed = 0f;
        }

        /// <summary>The quarterback's slice of <see cref="GlideStep"/>: moves his presented position through the sequence. Returns true when it handled him this frame (<paramref name="walking"/> = he is on his feet walking).</summary>
        bool QbSeqStep(int i, ref Glide g, Vector3 simTarget, ref Vector3 pos, ref Vector3 vel, ref Vector3 face, float dt, SimAgentView v, out bool walking)
        {
            walking = false;
            if (i != 0 || !QbSeqBusy) return false;
            dt = Mathf.Max(dt, 0.0001f);
            Vector3 down = Frame.DirToUnity(new Vec2(0f, 1f));
            Vector3 target = _qpStage == QbPreStage.ToSurvey || _qpStage == QbPreStage.Survey ? _qpSurvey : simTarget;
            switch (_qpStage)
            {
                case QbPreStage.ToSurvey: case QbPreStage.StepIn:
                {
                    Vector3 to = target - g.Shown; to.y = 0f; float d = to.magnitude;
                    float vmax = (_qpStage == QbPreStage.ToSurvey ? 2.5f : 1.5f) * (_qpHurry ? 1.8f : 1f);
                    float want = Mathf.Min(vmax, Mathf.Sqrt(2f * 2.2f * Mathf.Max(0f, d)) + 0.25f);      // a walking profile: up to speed, a constant deceleration over the last metres, never a crawl
                    _qpSpeed = Mathf.MoveTowards(_qpSpeed, want, 3.0f * dt);
                    float step = Mathf.Min(d, _qpSpeed * dt);
                    Vector3 dir = d > 1e-4f ? to / d : down;
                    g.Shown += dir * step;
                    bool arrived = d - step < 0.04f;
                    // he walks facing where he goes, and squares to the line over the last 1.5 m (he never walks backwards)
                    Vector3 f = _qpStage == QbPreStage.StepIn ? down : (d > 1.6f ? dir : Vector3.Slerp(down, dir, Mathf.Clamp01((d - 0.4f) / 1.2f)));
                    pos = new Vector3(g.Shown.x, pos.y, g.Shown.z); vel = arrived ? Vector3.zero : dir * _qpSpeed; face = f.sqrMagnitude > 1e-4f ? f.normalized : down;
                    walking = !arrived; v.SetQbPre(SimAgentView.QbPre.Walk, _qpT, _qpLook, _qpShotgun, 0f);
                    if (arrived) { g.Shown = target; _qpSpeed = 0f; _qpT = 0f; _qpStage = _qpStage == QbPreStage.ToSurvey ? QbPreStage.Survey : QbPreStage.Crouch; }
                    else _qpT += dt;
                    break;
                }
                case QbPreStage.Survey:
                    _qpT += dt; pos = new Vector3(_qpSurvey.x, pos.y, _qpSurvey.z); g.Shown = _qpSurvey; vel = Vector3.zero; face = down;
                    v.SetQbPre(SimAgentView.QbPre.Survey, _qpT, _qpLook, _qpShotgun, _qpSurveyLen);
                    if (_qpT >= (_qpHurry ? 0.25f : _qpSurveyLen)) { _qpT = 0f; _qpStage = _qpShotgun ? QbPreStage.Crouch : QbPreStage.StepIn; }
                    break;
                case QbPreStage.Crouch:
                    _qpT += dt * (_qpHurry ? 2.4f : 1f); g.Shown = simTarget; pos = new Vector3(simTarget.x, pos.y, simTarget.z); vel = Vector3.zero; face = down;
                    v.SetQbPre(SimAgentView.QbPre.Crouch, _qpT / _qpCrouchLen, _qpLook, _qpShotgun, 0f);
                    if (_qpT >= _qpCrouchLen) { _qpStage = QbPreStage.Done; g.Active = false; g.Speed = 0f; v.SetQbPre(SimAgentView.QbPre.None, 0f, _qpLook, _qpShotgun, 0f); }
                    break;
            }
            return true;
        }
    }
}

namespace PixFootball.Gameplay
{
    public sealed partial class PlayStage
    {
        /// <summary>
        /// v0.10.0 QB: the snap arrives in the quarterback's hands on exactly the frame the simulation completes the catch. The simulation hands him the ball when it is 0.91 m from his body; the displayed ball is drawn from its flight into
        /// the midpoint of his hands over the last 1.2 m of that flight (and over the first 0.1 s for the short under-centre snap), so at the catch frame it is in his hands (and the hold takes over from there without a glide).
        /// </summary>
        Vector3 QbSnapBall(Vector3 wp, BallSim b)
        {
            if (DebugNoQbSequence || b.State != BallState.Snap || Sim.Kind != PlayKind.Scrimmage) return wp;
            var v = _views[0]; if (v == null || v.Avatar == null || v.Avatar.RightHand == null || v.Avatar.LeftHand == null) return wp;
            Vector3 hm = (v.Avatar.RightHand.position + v.Avatar.LeftHand.position) * 0.5f;
            float dm = (Sim.Agents[0].Pos - b.Pos.XY).Length * PlayFrame.YardsToMeters;
            float distW = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.9144f + 1.5f - dm) / 1.2f));      // fully in the hands one fixed step (0.3 m) before the catch tick
            float timeW = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(b.FlightTime / 0.10f));
            return Vector3.Lerp(wp, hm, Mathf.Min(distW, timeW));
        }
    }
}

namespace PixFootball.Gameplay
{
    public sealed partial class PlayStage
    {
        /// <summary>
        /// v0.10.0 QB: the simulation's Handoff action outlives a play-action fake by 0.2 s: the quarterback who has already dropped back (or boots out) must not play the mocap hand-off take over it (a mocap pair take on a man running 2-3 m/s was a metre of foot slide);
        /// the view sees a run (the motion matcher's) instead. A real hand-off keeps its action.
        /// </summary>
        void QbViewAction(int i, SimAgent a, ref AgentAction viewAction)
        {
            if (i != 0 || viewAction != AgentAction.Handoff || a.Side != SimSide.Offense || Sim.Kind != PlayKind.Scrimmage) return;
            bool realHandoff = Sim.QbPlanIsHandoff && (a.QbState == QbPhase.Mesh || a.QbState == QbPhase.Done);
            if (realHandoff || a.QbState == QbPhase.Fake || a.QbState == QbPhase.Mesh) return;
            viewAction = a.Speed > 0.4f ? AgentAction.Run : AgentAction.Idle;
        }

        /// <summary>
        /// v0.10.0 QB: the reach of the hand-off. The simulation exchanges the ball with the runner 1.3 yd from the quarterback, further than his arms reach; over the 0.45 s before the exchange the quarterback's drawn position steps toward the runner (up to 0.55 m)
        /// and gives it back over 0.25 s after it, so his hands meet the runner's pocket on the exchange frame. Presentation only; the runner's side is CHOREO's.
        /// </summary>
        void QbMeshPull()
        {
            if (Sim == null || Sim.Kind != PlayKind.Scrimmage || !Sim.QbPlanIsHandoff || DebugNoQbSequence) return;
            var v = _views[0]; var q = Sim.Agents[0]; int rid = Sim.QbHandoffTargetId;
            if (v == null || rid <= 0 || rid >= 22 || _views[rid] == null) return;
            float w = v.QbMeshPullWeight;
            if (w <= 0.001f) return;
            Vector3 to = _views[rid].transform.position - v.transform.position; to.y = 0f;
            float d = to.magnitude; if (d < 0.05f) return;
            v.transform.position += to / d * Mathf.Min(0.80f * w, Mathf.Max(0f, d - MeshMinGap));      // v0.10.0 CHOREO: the hips never closer than a body width at the mesh (was 0.42: the bodies interpenetrated, median hips distance 0.44 m)
        }
    }
}

namespace PixFootball.Gameplay
{
    public sealed partial class PlayStage
    {
        /// <summary>v0.11.0 QB (playtest note 8): the quarterback's takeoff flag for the pose layer (stance until the simulation says he runs).</summary>
        void QbStanceHints(ref AnimHints h, SimAgent a)
        {
            if (a.Side != SimSide.Offense || a.Slot != 0 || Sim.Kind != PlayKind.Scrimmage) return;
            h.QbTakeoff = Sim.QbTakeoff;
            h.QbTakeoffSince = Sim.QbTakeoff ? Mathf.Max(0f, Sim.Time - Sim.QbTakeoffAt) : 0f;
        }
    }
}

namespace PixFootball.Gameplay
{
    public sealed partial class PlayStage
    {
        /// <summary>
        /// v0.11.0 QB (playtest note 9): while a pass is in the air the receiver it is thrown to, and the defender on him, turn their head to the ball (<see cref="SimAgentView.SetEyesOnBall"/>), from 0.1 s after the release until the catch is made.
        /// </summary>
        void ReceiverEyes(int i, SimAgent a, SimAgentView v)
        {
            if (v == null) return;
            var b = Sim.Ball; float w = 0f;
            if (!QbV11.Legacy && Sim.Kind == PlayKind.Scrimmage && b.State == BallState.Pass && b.FlightTime > 0.10f && b.Intended >= 0 && b.Intended < 22 && a.Action != AgentAction.Catch && a.Action != AgentAction.Fallen && a.Action != AgentAction.Tackled && !a.IsDown)
            {
                if (i == b.Intended) w = 1f;
                else if (a.Side != Sim.Agents[b.Intended].Side && a.CoverTarget == b.Intended && (a.Pos - Sim.Agents[b.Intended].Pos).Length < 9f) w = 0.85f;
            }
            v.SetEyesOnBall(Frame.ToUnity(b.Pos), w);
        }
    }
}

namespace PixFootball.Gameplay
{
    public sealed partial class PlayStage
    {
        /// <summary>v0.11.0 QB: once the ball is out, the passer watches the ball itself (not the receiver's head) all the way in.</summary>
        void QbEyesOverride(ref Vector3 look, ref bool hasLook, SimAgent a)
        {
            if (QbV11.Legacy || a.Side != SimSide.Offense || a.Slot != 0 || Sim.Kind != PlayKind.Scrimmage) return;
            if (Sim.PassIsThrown && Sim.Ball.State == BallState.Pass && Sim.Ball.Thrower == a.Id) { look = Frame.ToUnity(Sim.Ball.Pos); hasLook = true; }
        }
    }
}

namespace PixFootball.Gameplay
{
    public sealed partial class PlayStage
    {
        /// <summary>v0.11.0 QB: the flight of the pass: spin by style (from <see cref="BallSpinHint"/>) and the wobble from how the throw was made (pressure, on the run, a hurried or a hit arm; a lob is a little looser).</summary>
        void QbFlightHints(SimAgent a)
        {
            if (a.Side != SimSide.Offense || a.Slot != 0 || Sim.Kind != PlayKind.Scrimmage) return;
            float press = Mathf.Clamp01(Sim.LastThrowPressure);
            float run = Mathf.Clamp01(a.Speed * PlayFrame.YardsToMeters / 4.5f); if (Sim.Ball.State == BallState.Pass && Sim.Ball.FlightTime > 0.25f) return;      // the throw sets it for the flight
            BallSpinHint.Wobble = QbV11.Legacy ? 1.5f : 0.7f + 4.5f * press + 1.6f * run + (a.PendingStyle == ThrowStyle.Lob ? 0.6f : 0f);
        }
    }
}
