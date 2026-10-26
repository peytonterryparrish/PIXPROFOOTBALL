using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.13.0 QB-2: the other fitted segments of the quarterback: the hand-off (QB side: the A take of the hand-off pair the back plays the B take of, started on the same exchange tick), the play fake (the window around the reach of a fake hand-off, on the side of the runner), the spike (the
    /// window around the downward swing, its low point on the tick the simulation throws the ball), and the roll-out (the lateral shuffle loop, time-warped to the simulated speed, its path turned to the simulated heading).
    /// </summary>
    public partial class SimAgentView
    {
        bool _qmMeshOn, _qmFakeTried, _qmSpikeTried, _qfLoop; int _qfLoops; float _qfLastT, _qrClipSpeed = 3.8f;

        // ------------------------------------------------------------------------------------------ the hand-off, quarterback side
        bool QmMesh(QbClipTable tab, in AnimHints h, AgentAction action, float dt)
        {
            _qmMeshOn = false;
            if (!h.QbHandoffPlan) { QmEnd(true); return false; }
            if (_qm == Qm.Mesh) { if (Rig.InWindow) { _qmMeshOn = true; _qbMeshEta = h.QbMeshEta; return true; } QmEnd(false); return false; }
            if (action != AgentAction.Handoff || h.EventEta < 0f)
            {
                // the catch window's tail holds the ball until the hand-off take starts (the stage starts both takes when the two bodies are about to meet)
                if (_qm == Qm.Snap && Rig.InWindow && _simVel.magnitude < 1.4f) return true;
                if (_qm != Qm.None) QmEnd(false);
                return false;
            }
            var plan = AnimKit.Handoff(h); if (!plan.Valid || plan.Clip == null) { QmEnd(true); return false; }
            plan = ChoreoMask(AgentAction.Handoff, PacedPlan(AgentAction.Handoff, plan));
            QmEnd(false);
            Rig.SetFrozen(false); Rig.Play(plan); _ownsClip = true; _clipYaw = ClipFacingTable.Offset(plan.Clip);
            _qm = Qm.Mesh; _qmE = null; _qmClipNow = plan.Clip; _qmSince = _clock; _qmPathE = null; _qmRot = 0f; _qfFit = false; _qmMeshOn = true; _qbMeshEta = h.QbMeshEta; _qmSpeed = plan.Speed;
            if (Motion != null) { Motion.SetCarriage(BodyMotion.Carriage.Chest); Motion.CarriageFloor = 0.7f; }
            return true;
        }

        // ------------------------------------------------------------------------------------------ the play fake
        bool QmFake(QbClipTable tab, in AnimHints h, bool shotgun)
        {
            if (_qm == Qm.Fake)
            {
                if (!Rig.InWindow) { QmEnd(false); return false; }
                float left = Mathf.Max(0.06f, (h.UnderCenter ? 0.45f : 0.32f) - h.QbClock);
                QmServoSync(Mathf.Max(0.05f, 0.5f * left), false);
                return true;
            }
            if (_qmFakeTried) return false;
            _qmFakeTried = true;
            float lat = 0f;
            if (h.QbRunnerGap >= 0f) { Vector3 rel = h.QbRunnerPos - (transform.position + _qmOff); rel.y = 0f; lat = Vector3.Dot(rel, Vector3.Cross(Vector3.up, Forward)); }
            var pool = QbMc.Pool(tab, e => e.sync > e.a + 0.3f && (Mathf.Abs(lat) < 0.35f || Mathf.Sign(e.side) == Mathf.Sign(lat)), QbFam.Fake);
            var pick = QbMc.Pick("fake", pool, QbMc.Seed(h.QbPlay, 71));
            if (pick == null) return false;
            float fakeLen = h.UnderCenter ? 0.45f : 0.32f, rem = Mathf.Max(0.18f, fakeLen - h.QbClock);
            float start = Mathf.Max(pick.a, pick.sync - 0.40f), speed = Mathf.Clamp((pick.sync - start) / (0.55f * rem), 0.7f, 2.2f);
            _qfYaw = _yaw;
            QmStart(Qm.Fake, pick, start, pick.b, false, speed, 0.16f, 0f, -pick.yaw0);
            if (_qm == Qm.Fake) { QmFitOn(pick, start, speed, 1f, 0f, 1.3f); _qfYawLock = true; }
            return _qm == Qm.Fake;
        }

        // ------------------------------------------------------------------------------------------ the spike
        bool QmSpike(QbClipTable tab, in AnimHints h)
        {
            if (_qm == Qm.Spike)
            {
                if (!Rig.InWindow) { QmEnd(false); return false; }
                QmServoSync(Mathf.Max(0f, 0.7f - h.QbClock), h.BallReleased);
                return true;
            }
            if (h.QbClock < 0.1f) _qmSpikeTried = false;      // a new spike play (the play counter is the scrimmage plays')
            if (_qmSpikeTried) return false;
            _qmSpikeTried = true;
            var pool = QbMc.Pool(tab, e => e.sync > e.a + 0.2f, QbFam.Spike);
            var pick = QbMc.Pick("spike", pool, QbMc.Seed(h.QbPlay, 73));
            if (pick == null) return false;
            float eta = Mathf.Max(0.1f, 0.7f - h.QbClock), start = Mathf.Clamp(pick.sync - eta, pick.a, pick.sync - 0.05f);
            QmStart(Qm.Spike, pick, start, pick.b, false, 1f, 0.14f, 0f, -pick.yaw0);
            return _qm == Qm.Spike;
        }

        // ------------------------------------------------------------------------------------------ the roll-out
        /// <summary>The lateral shuffle loop of a roll-out / boot / a drift across the pocket at speed. True = the loop owns him this frame.</summary>
        bool QmRoll(QbClipTable tab, in AnimHints h, bool shotgun)
        {
            float sp = _simVel.magnitude;
            if (_qm == Qm.Roll)
            {
                if (sp < 1.1f || !Rig.InWindow) { QmEnd(false); return false; }
                float want = Mathf.Clamp(sp / _qrClipSpeed, 0.55f, 1.6f);
                _qmSpeed = Mathf.Lerp(_qmSpeed, want, 0.25f); Rig.SetSpeed(_qmSpeed);
                return true;
            }
            if (sp < 2.2f) return false;
            Vector3 vd = new Vector3(_simVel.x, 0f, _simVel.z).normalized; float lat = Vector3.Dot(vd, Vector3.Cross(Vector3.up, Forward));
            var pool = QbMc.Pool(tab, e => QmHandOk(e) && e.b - e.a >= 1.2f && e.PathLen(e.a, e.b) > 3f && Mathf.Sign(e.PathAt(e.b).x - e.PathAt(e.a).x) == Mathf.Sign(lat), QbFam.DropWalk);
            var fit = pool.FindAll(e => Mathf.Abs(QfRot(e, _yaw, e.PathAt(e.b) - e.PathAt(e.a), vd)) <= 28f);
            var pick = QbMc.Pick("roll", fit, QbMc.Seed(h.QbPlay, 79));
            if (pick == null) return false;
            _qrClipSpeed = pick.PathLen(pick.a, pick.b) / (pick.b - pick.a);
            float rot = QfRot(pick, _yaw, pick.PathAt(pick.b) - pick.PathAt(pick.a), vd), sp0 = Mathf.Clamp(sp / _qrClipSpeed, 0.55f, 1.6f);
            _qfYaw = _yaw;
            QmStart(Qm.Roll, pick, pick.a, pick.b, true, sp0, 0.22f, 0f, -pick.yaw0);
            if (_qm != Qm.Roll) return false;
            QmFitOn(pick, pick.a, sp0, 1f, rot, 3.0f); _qfLoop = true; _qfLoops = 0; _qfLastT = pick.a; _qfYawLock = true;
            return true;
        }
    }
}
