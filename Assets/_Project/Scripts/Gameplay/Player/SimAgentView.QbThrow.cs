using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 QB (playtest note 10): the quarterback's throw on the pose layer, keyed to the simulated wind-up and release (no mocap throw take: the v0.10 derived takes hopped and turned his back to the field).
    /// The progress <c>u</c> runs 0..1 over the simulated wind-up (the ball leaves on the simulation's release tick), then 1..1.5 over the follow-through. Set feet: the stance turns side-on, the FRONT foot (left for a right-hander) steps to the target and is planted before the release,
    /// the back foot pivots on the ball of the foot, the hips open before the shoulders, the release is high over the front foot, the hand finishes across the body. On the run the feet are the run's (the planner), the upper body throws.
    /// </summary>
    public partial class SimAgentView
    {
        bool _qtOn, _qtRun, _qtFeet, _qtAway; float _qtT0, _qtTotal, _qtRel = -1f, _qtU;
        Vector3 _qtDir, _qtRoot0, _qtFrontFrom, _qtFrontTo, _qtBackPos; float _qtFrontYaw0, _qtBackYaw0, _qtYawT; bool _qtFrontIsLeft;
        Vector3 _qtLastR, _qtLastL; float _qtLastRY, _qtLastLY; bool _qtHaveFeet;
        /// <summary>Seconds the follow-through lasts after the release.</summary>
        public const float QtFollowSeconds = 0.46f;
        /// <summary>QA: the throw progress (0..1.5) of the pose layer throw, -1 when none.</summary>
        public float QbThrowProgress { get { return _qtOn ? _qtU : -1f; } }

        static Vector3 FwdOfYaw(float yawDeg) { float a = yawDeg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); }

        /// <summary>Claims the quarterback for the throw while the simulation winds up and for the follow-through after the release. Returns true when it owns him this frame.</summary>
        bool QbThrowFlow(in AnimHints h, float dt)
        {
            bool can = !QbV11.Legacy && h.IsQb && !h.Pitch && Role == PlayerRole.QB && StanceLayer != null && StanceLayer.Ok && Rig != null && Rig.Valid && !DebugNoStancePose;
            if (!can) { QbThrowEnd(); return false; }
            if (h.Qb == QbPhase.Windup)
            {
                if (!_qtOn || _qtRel >= 0f) QbThrowBegin(h);
                float el = _clock - _qtT0;
                _qtU = Mathf.Clamp01((el + 0.03f) / Mathf.Max(0.12f, _qtTotal));
                return true;
            }
            if (_qtOn && h.BallReleased && (h.Qb == QbPhase.Done || h.Qb == QbPhase.Windup))
            {
                if (_qtRel < 0f) _qtRel = _clock;
                float t = _clock - _qtRel;
                if (t < QtFollowSeconds) { _qtU = 1f + 0.5f * Mathf.Clamp01(t / 0.30f); return true; }
            }
            QbThrowEnd();
            return false;
        }

        void QbThrowBegin(in AnimHints h)
        {
            _qtOn = true; _qtRel = -1f; _qtT0 = _clock; _qtTotal = Mathf.Max(0.12f, h.WindupSeconds);
            _qtRun = _simVel.magnitude > 2.4f;
            Vector3 d = h.TargetDir; d.y = 0f; if (d.sqrMagnitude < 1e-3f) d = Forward; _qtDir = d.normalized;
            _qtYawT = Mathf.Atan2(_qtDir.x, _qtDir.z) * Mathf.Rad2Deg;
            _qtFrontIsLeft = !_lefty;
            if (_gait.Valid)
            {
                // a target far to the side: the foot that is already ahead along the throw is the stride foot (stepping the other foot across the body scissored the legs)
                float S0 = StanceLayer.Scale; Vector3 r0 = transform.position;
                float aL = Vector3.Dot(_gait.L.Pos - r0, _qtDir), aR = Vector3.Dot(_gait.R.Pos - r0, _qtDir);
                float aFront = _qtFrontIsLeft ? aL : aR, aBack = _qtFrontIsLeft ? aR : aL;
                if (aBack - aFront > 0.15f * S0 && Vector3.Angle(_qbDown, _qtDir) > 55f) _qtFrontIsLeft = !_qtFrontIsLeft;
            }
            _qtRoot0 = transform.position; _qtFeet = false; _qtHaveFeet = false;
            if (!_qtRun && _gait.Valid)
            {
                var front = _qtFrontIsLeft ? _gait.L : _gait.R; var back = _qtFrontIsLeft ? _gait.R : _gait.L;
                float S = StanceLayer.Scale;
                _qtFrontFrom = front.Pos; _qtFrontYaw0 = front.Yaw; _qtBackPos = back.Pos; _qtBackYaw0 = back.Yaw;
                Vector3 perp = Vector3.Cross(Vector3.up, _qtDir);
                Vector3 to = _qtRoot0 + _qtDir * (0.62f * S) + perp * ((_qtFrontIsLeft ? -1f : 1f) * 0.19f * S);
                to.y = _qtFrontFrom.y; _qtFrontTo = to; _qtFeet = true;
            }
        }

        void QbThrowEnd()
        {
            if (!_qtOn) return;
            _qtOn = false; _qtRel = -1f;
            // hand the planner the feet where the throw left them: no pop into the stance that follows
            if (_qtHaveFeet) _gait.Seed(_qtLastR, _qtLastL);
            _gait.Cancel();
            _qtHaveFeet = false;
        }

        /// <summary>The pose layer: the keyed throw pose of this frame.</summary>
        bool QbThrowPose(ref StancePose target, ref float tau, ref bool gait)
        {
            if (!_qtOn) return false;
            if (_qtRel >= 0f && _clock - _qtRel > QtFollowSeconds + 0.05f) { QbThrowEnd(); return false; }      // a hit after the release (a contact take owns the body): the pose does not wait for the flow
            target = StancePoses.QbThrow11(_qtU, _qtRun); if (_lefty) target = target.Mirrored();
            tau = 0.03f; gait = true; _throwPose = true;
            return target.Valid;
        }

        /// <summary>The scripted feet of a set-feet throw (called from <see cref="QbSeqDyn"/> after the planner): front foot lifts at 8 percent of the wind-up, lands at 70, planted until the end; the back foot pivots on the ball of the foot to point across the target line and its heel comes up at the release.</summary>
        void QbThrowDyn(ref StanceDyn d)
        {
            if (!_qtOn) return;
            Vector3 head = Avatar != null && Avatar.Head != null ? Avatar.Head.position : transform.position + Vector3.up * 1.6f;
            if (_qbHasLook) { d.Look = _qbLook; d.LookW = 0.95f; } else { d.Look = head + _qtDir * 10f; d.LookW = 0.9f; }
            if (!_qtFeet || !_gait.Valid) return;
            float S = StanceLayer.Scale, u = Mathf.Min(_qtU, 1f);
            float swing = Mathf.Clamp01((u - 0.05f) / 0.55f), e = swing * swing * (3f - 2f * swing);
            Vector3 fp = Vector3.Lerp(_qtFrontFrom, _qtFrontTo, e);
            float arc = 0.13f * Mathf.Sin(Mathf.PI * swing);
            { Vector3 perpT = Vector3.Cross(Vector3.up, _qtDir); fp += perpT * ((_qtFrontIsLeft ? -1f : 1f) * 0.10f * S * Mathf.Sin(Mathf.PI * swing)); }      // the swing bows outward: the stride foot never passes through the back foot
            fp.y = _qtFrontFrom.y + arc;
            float fyaw = Mathf.LerpAngle(_qtFrontYaw0, _qtYawT + (_qtFrontIsLeft ? -4f : 4f), e);
            // the back foot: toe fixed, the heel swings round to point across the line (65 degrees), the heel lifts as the weight goes forward
            float byawT = _qtYawT + (_qtFrontIsLeft ? 65f : -65f);
            float pv = Mathf.Clamp01((u - 0.05f) / 0.5f); float byaw = Mathf.LerpAngle(_qtBackYaw0, byawT, pv * pv * (3f - 2f * pv));
            Vector3 toe = _qtBackPos + FwdOfYaw(_qtBackYaw0) * _gait.BallOfFoot;
            Vector3 bp = toe - FwdOfYaw(byaw) * _gait.BallOfFoot; bp.y = _qtBackPos.y;
            float blift = 0.06f * Mathf.Clamp01((u - 0.75f) / 0.25f); bp.y += blift;
            // the legs reach: a foot never further than the leg can follow
            Vector3 root = transform.position; Vector3 hh = fp - root; hh.y = 0f; float lim = 0.60f * S; if (hh.magnitude > lim) { Vector3 c = root + hh.normalized * lim; fp = new Vector3(c.x, fp.y, c.z); }
            Vector3 hb = bp - root; hb.y = 0f; if (hb.magnitude > lim) { Vector3 c = root + hb.normalized * lim; bp = new Vector3(c.x, bp.y, c.z); }
            if (_qtFrontIsLeft) { d.LFoot = fp; d.LFootYaw = fyaw; d.LLift = arc; d.RFoot = bp; d.RFootYaw = byaw; d.RLift = 1.75f * blift / 0.8f; }
            else { d.RFoot = fp; d.RFootYaw = fyaw; d.RLift = arc; d.LFoot = bp; d.LFootYaw = byaw; d.LLift = 1.75f * blift / 0.8f; }
            d.GaitOn = true;
            _qtLastR = d.RFoot; _qtLastL = d.LFoot; _qtLastRY = d.RFootYaw; _qtLastLY = d.LFootYaw; _qtHaveFeet = true;
        }
    }
}
