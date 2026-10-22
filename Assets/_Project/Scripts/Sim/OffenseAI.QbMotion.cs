using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.9.0 QBSTANCE (E-070, E-078, E-086, E-088, E-089, E-090, E-091, E-096, E-099, E-076): quarterback movement that the animation can follow.
    /// Under centre the body turns through a reverse pivot (about 0.25 s) and runs the drop facing the drop direction, a shotgun quarterback keeps facing the line and retreats; both ramp their speed down over the last yard so the feet plant
    /// (no coasting past the spot). In the pocket he faces the read (within 35 degrees), keeps live feet (a slow lateral slide), a rolling quarterback keeps going out to about 10 yd and throws on the run, the release waits for the body to face the target,
    /// and after a hand-off / a throw he carries through two or three steps instead of freezing. All additive: the state machine in OffenseAI.Qb.cs calls these helpers.
    /// </summary>
    internal sealed partial class OffenseAI
    {
        private float _qbDropClock0 = -1f, _qbDoneAt = -1f;

        private static Vec2 RotateDir(Vec2 d, float rad)
        {
            float c = (float)Math.Cos(rad), s = (float)Math.Sin(rad);
            return new Vec2(d.X * c - d.Y * s, d.X * s + d.Y * c);
        }

        private static float SignedAngle(Vec2 a, Vec2 b)
        {
            return (float)Math.Atan2(a.X * b.Y - a.Y * b.X, a.Dot(b));
        }

        /// <summary>The way the quarterback faces on the drop (called every tick while in <see cref="QbPhase.Dropback"/>; the speed profile is the drop planner's): a reverse pivot under centre, eyes on the line in the shotgun.</summary>
        private void QbDropFacing(SimAgent q, Vec2 dir)
        {
            if (_qbDropClock0 < 0f || q.QbClock < _qbDropClock0 - 0.01f) _qbDropClock0 = q.QbClock;
            float t = q.QbClock - _qbDropClock0;
            Vec2 down = new Vec2(0f, 1f);
            q.HasFace = true;
            if (_shotgun) { q.FaceDir = down; return; }
            // reverse pivot: the body turns from the line to the drop direction in about a quarter second (clockwise when he drops straight back)
            float ang = SignedAngle(down, dir);
            if (ang > 2.6f) ang -= 2f * (float)Math.PI;
            float k = SimMath.Clamp01(t / 0.24f); k = k * k * (3f - 2f * k);
            q.FaceDir = RotateDir(down, ang * k);
        }

        /// <summary>The way the passer faces in the pocket: toward his current read, within 35 degrees of the line, so the shoulders track the progression.</summary>
        private Vec2 QbSetFacing(SimAgent q)
        {
            Vec2 down = new Vec2(0f, 1f);
            if (_reads == null || _reads.Count == 0 || S.Options.QbHoldsBall) return down;
            SimAgent r = S.Agents[_reads[Math.Min(Math.Max(0, q.ReadIdx), _reads.Count - 1)]];
            Vec2 to = r.Pos - q.Pos;
            if (to.LenSq() < 1f) return down;
            float th = (float)Math.Atan2(to.X, to.Y);
            th = SimMath.Clamp(th, -0.61f, 0.61f) * 0.85f;
            return RotateDir(down, -th);
        }

        /// <summary>Id of the receiver the quarterback is looking at (his current read; the throw target during a throw), -1 = none. The presentation turns his head to him.</summary>
        /// <summary>Agent id of the runner the quarterback fakes the hand-off to (-1 = none).</summary>
        internal int QbFakeTargetId { get { return _fakeTarget != null ? _fakeTarget.Id : -1; } }

        internal int QbLookTargetId
        {
            get
            {
                if (_qb == null) return -1;
                if (_qb.QbState == QbPhase.Windup && _qb.ThrowTarget >= 0) return _qb.ThrowTarget;
                if (_reads == null || _reads.Count == 0) return -1;
                return _reads[Math.Min(Math.Max(0, _qb.ReadIdx), _reads.Count - 1)];
            }
        }

        /// <summary>Pocket motion: pressure drift wins; a rolling passer keeps running the arc out to about 10 yd; a clean pocket still has live feet (a slow lateral slide).</summary>
        private Vec2 QbSetMotion(SimAgent q, Vec2 drift)
        {
            if (_rolling)
            {
                int dir = _qa.RollDirection >= 0 ? 1 : -1;
                float x0 = S.BallX0 + S.OffForm.Slots[0].X;
                float ext = (q.Pos.X - x0) * dir;
                q.HasFace = true;
                q.FaceDir = new Vec2(dir * 0.62f, 0.78f).Norm();      // eyes downfield while he runs: shoulders between the run and the line
                if (ext < 10.0f && q.Pos.X > 3.5f && q.Pos.X < FieldGeometry.Width - 3.5f) return new Vec2(dir, 0f) * Math.Min(q.Body.EffectiveVMax * 0.8f, 5.2f);
                return Vec2.Zero;
            }
            if (drift.LenSq() > 0.01f) return drift;
            float t = S.Time - q.QbSetAt;
            if (t > 0.30f)
            {
                float sway = (float)Math.Sin(t * 3.9f + 1.3f * q.Id);
                return new Vec2(sway * 0.8f, 0f);
            }
            return Vec2.Zero;
        }

        /// <summary>After a hand-off, a fake or a throw he carries through a couple of steps (his speed decays over about 0.9 s) instead of freezing.</summary>
        private Vec2 QbFollowThrough(SimAgent q)
        {
            if (_qbDoneAt < 0f || S.Time < _qbDoneAt) _qbDoneAt = S.Time;
            float t = S.Time - _qbDoneAt;
            if (t > 0.9f) return Vec2.Zero;
            return q.Vel * Math.Max(0f, 0.8f - t * 0.9f);
        }

        /// <summary>Extra wind-up (seconds) so the body can turn to the target before the ball leaves: the turn takes angle / turn rate, of which 60 percent of the wind-up is available.</summary>
        private float QbWindupExtra(SimAgent q, SimAgent target, float windup)
        {
            Vec2 to = target.Pos - q.Pos;
            if (to.LenSq() < 0.04f) return 0f;
            float ang = SimMath.AngleBetween(q.Facing, to.Norm());
            float turn = ang / Math.Max(3.0f, q.Body.TurnRate);
            float have = 0.6f * windup;
            return turn > have ? Math.Min(0.45f, turn - have) : 0f;
        }

        /// <summary>True when the quarterback's body faces within 20 degrees of the throw target (the release is gated by the body, not only the clock).</summary>
        private bool QbAligned(SimAgent q)
        {
            if (q.ThrowTarget < 0 || q.ThrowTarget >= S.Agents.Length) return true;
            Vec2 to = S.Agents[q.ThrowTarget].Pos - q.Pos;
            if (to.LenSq() < 0.04f) return true;
            return SimMath.AngleBetween(q.Facing, to.Norm()) < 0.35f;
        }
    }
}
