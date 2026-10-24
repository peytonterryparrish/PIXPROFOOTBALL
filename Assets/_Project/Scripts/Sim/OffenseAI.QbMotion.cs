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
            Vec2 down = new Vec2(0f, 1f);
            q.HasFace = true;
            if (_shotgun) { q.FaceDir = down; return; }
            // v0.10.0 QB: the reverse pivot opens the hips about 72 degrees to the side the drop travels (not all the way round: he never turns his back on the field),
            // holds the open stance through the first half of the drop and brings the hips back square to the line over the second half, so he SETS with his chest north
            float el = S.Time - _dropT0;           // seconds since the secure ended (the drop clock starts after it)
            if (el <= 0f) { q.FaceDir = down; return; }
            float open = QbSeqTiming.OpenDegrees * (float)Math.PI / 180f * (dir.X > 0.15f ? -1f : (dir.X < -0.15f ? 1f : -1f));
            float k = SimMath.Clamp01(el / QbSeqTiming.PivotSeconds); k = k * k * (3f - 2f * k);
            float tail = Math.Max(0.4f, _dropDur * 0.5f);
            float u = SimMath.Clamp01((el - (_dropDur - tail - 0.1f)) / tail); u = u * u * (3f - 2f * u);
            q.FaceDir = RotateDir(down, open * k * (1f - u));
        }

        /// <summary>The way the passer faces in the pocket: toward his current read, within 35 degrees of the line, so the shoulders track the progression.</summary>
        private Vec2 QbSetFacing(SimAgent q)
        {
            Vec2 down = new Vec2(0f, 1f);
            if (!_rolling) return down;      // v0.10.0 QB: the chest faces north (downfield) in the pocket; the HEAD scans the reads (the presentation turns it), the feet shuffle
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
                if (!QbV11.Legacy && S.PassThrown && S.TargetId >= 0) return S.TargetId;      // v0.11.0 QB: after the release his eyes follow the ball to the receiver he threw it to
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
                q.FaceDir = QbStanceMoveFacing(new Vec2(dir, 0f), 0.45f, 0.62f, 0.78f);      // v0.11.0 QB: square stance while he rolls: chest to the field, hips opened 26 degrees at most, eyes downfield
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

namespace PixFootball.Sim
{
    internal sealed partial class OffenseAI
    {
        private QbPhase _seqPrev = QbPhase.PreSnap;
        private float _seqScrambleAt = -1f;

        /// <summary>
        /// v0.10.0 QB: the simulation plans around the quarterback's choreographed segments (see <see cref="QbSeqTiming"/>). Called every tick from the quarterback update:
        /// the snap in his hands is SECURED first (the body holds still while the ball is pulled to the belly, the drop clock starts after it), and pocket-to-scramble is a transition with a tuck and a first drive step
        /// (the body is held to a fraction of his speed for 0.34 s) instead of a full-speed start from the plant.
        /// </summary>
        private void QbSeqWatch(SimAgent q)
        {
            QbPhase st = q.QbState;
            QbTakeoffWatch(q);      // v0.11.0 QB: stance until he decides to run
            if (st != _seqPrev)
            {
                if (st == QbPhase.Dropback && (_seqPrev == QbPhase.WaitSnap || _seqPrev == QbPhase.PreSnap) && _dropT0 < 0f)
                {
                    float sec = QbSeqTiming.Secure(_shotgun);
                    _dropT0 = S.Time + sec; _dropFrom = q.Pos;
                    q.CommitUntil = S.Time + sec; q.CommitSpeedFrac = 0f;
                }
                if (st == QbPhase.Scramble && (_seqPrev == QbPhase.Set || _seqPrev == QbPhase.Dropback || _seqPrev == QbPhase.Windup) && q.Speed < 1.6f) _seqScrambleAt = S.Time;      // the transition is for a quarterback who stands in the pocket; one who is still moving off his drop runs on (a brake to 30 percent was a hard stop)
                _seqPrev = st;
            }
            if (st == QbPhase.Mesh && _qa != null && _qa.Action != QbAction.Toss && !_shotgun && S.Time < QbSeqTiming.MeshOpenSeconds)
            {
                // the open step toward the runner (a hand-off from under centre): the body is held to 45 percent of his speed for 0.40 s, the exchange is the runner's to meet
                q.CommitUntil = S.Time + 0.03f; q.CommitSpeedFrac = QbSeqTiming.MeshOpenSpeed;
            }
            if (_rolling && st == QbPhase.Dropback && _qa != null)
            {
                // a rolling passer runs with his shoulders turned upfield (eyes on the field), not square to the way he runs
                q.HasFace = true; int rdir = _qa.RollDirection >= 0 ? 1 : -1; q.FaceDir = QbStanceMoveFacing(new Vec2(rdir, 0f), 0.45f, 0.55f, 0.84f);
            }
            if (st == QbPhase.Scramble && _seqScrambleAt >= 0f)
            {
                float t = S.Time - _seqScrambleAt;
                if (t < QbSeqTiming.ScrambleStartSeconds)
                {
                    q.CommitUntil = S.Time + 0.03f;
                    q.CommitSpeedFrac = t < QbSeqTiming.ScrambleFirstSeconds ? QbSeqTiming.ScrambleFirstSpeed : QbSeqTiming.ScrambleSecondSpeed;
                }
            }
        }
    }
}

namespace PixFootball.Sim
{
    internal sealed partial class OffenseAI
    {
        /// <summary>v0.10.0 QB: on a hand-off the quarterback pivots toward the runner but his body never turns more than 150 degrees from the line (a reverse pivot to the back behind him) (the runner behind him is reached with the arms and the open step, not by spinning his back to the field).</summary>
        private void QbSeqMeshFace(SimAgent q)
        {
            if (!q.HasFace || _qa == null || _qa.Action == QbAction.Toss) return;
            Vec2 down = new Vec2(0f, 1f);
            float ang = SignedAngle(down, q.FaceDir);
            const float lim = 150f * (float)Math.PI / 180f;
            if (Math.Abs(ang) > lim) q.FaceDir = RotateDir(down, ang > 0f ? lim : -lim);
        }
    }
}
