using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.13.0 QB-2 (owner, 2026-10-25: "drops, play action, handoffs, scrambles, slides and kneels are still hand-keyed poses, not mocap"): the quarterback's FITTED takes. A drop take travels a few metres on its own schedule and the simulation drops another distance on another schedule,
    /// so a fitted take is chosen by the distance the simulation is about to cover (and turned by a few degrees to its heading), its speed is servoed every frame to the simulated arrival (the time-warp), and its root travel is given back as a view offset that follows the CLIP's path
    /// instead of the simulated one: the planted feet of the clip stay planted (the slide is the scale error times the speed), the offset budget is metres for the quarterback during these segments only, and the offset decays back into the simulation at the end of the play.
    /// The stage tells the simulation the distance of the take it drew (<see cref="QbDropFitPlan"/>, before the snap: the drop point moves along its line by the ratio of the two distances, at most 40 percent), so the scale stays at one and the feet do not slide.
    /// The drawn yaw is held while the drop plays (the clip turns the hips itself; the closed facing loop and the simulated reverse pivot stay off).
    /// </summary>
    public partial class SimAgentView
    {
        bool _qfFit, _qfYawLock; float _qfYaw;
        QbClipTable.Entry _qfPick; int _qfPickPlay = -1; float _qfPickScale = 1f, _qfPickRot;
        QbClipTable.Entry _qfSettle; float _qfWalkEnd, _qfSettleMove, _qfSettleScale = 1f, _qfRotNow; bool _qfDropDone;

        /// <summary>QA: the segment on screen is a fitted take (time-warped and distance-fitted to the simulated path).</summary>
        public bool QbFitActive { get { return _qfFit && _qm != Qm.None; } }
        /// <summary>QA: the scale of the playing fitted take's path against the clip's own travel (1 = the clip's travel as it is).</summary>
        public float QbFitScale { get { return _qfFit ? _qmPathScale : 1f; } }

        void QfReset() { _qfFit = false; _qfYawLock = false; _qfDropDone = false; _qfSettle = null; }

        void QmStartFit(Qm seg, QbClipTable.Entry e, float start, float end, float speed, float fadeIn, float scale, float rot, float cap)
        {
            bool lockWas = _qfYawLock; float yawWas = _qfYaw;
            QmStart(seg, e, start, Mathf.Min(e.b, end), false, speed, fadeIn, 0f, -e.yaw0);
            if (_qm != seg) return;      // the clip is missing
            QmFitOn(e, start, speed, scale, rot, cap);
            if (seg == Qm.Drop || seg == Qm.Settle) { _qfYaw = lockWas ? yawWas : _yaw; _qfYawLock = true; }
        }

        /// <summary>The window that just started is a fitted take: its path is scaled / turned / given a metres-wide offset budget.</summary>
        void QmFitOn(QbClipTable.Entry e, float start, float speed, float scale, float rot, float cap)
        {
            _qfFit = true; _qmPathScale = scale; _qmRot = rot; _qmCap = cap; _qmSpeed = speed;
            if (_qmPathE == null && e.path != null && e.path.Length >= 4) { _qmPathE = e; _qmT0 = start; }
        }

        /// <summary>The signed angle (degrees) that turns the clip's net travel into the simulated heading, for a take drawn with the yaw <paramref name="yawBase"/>.</summary>
        static float QfRot(QbClipTable.Entry e, float yawBase, Vector2 net, Vector3 simDir)
        {
            if (net.sqrMagnitude < 1e-4f || simDir.sqrMagnitude < 1e-4f) return 0f;
            Vector3 cw = Quaternion.Euler(0f, yawBase - e.yaw0, 0f) * new Vector3(net.x, 0f, net.y);
            return Vector3.SignedAngle(cw, simDir, Vector3.up);
        }

        // ------------------------------------------------------------------------------------------ the under-centre / shotgun drop
        /// <summary>The stage asks before the snap: which catch-and-drop take plays this drop, and by how much the simulated drop (distance <paramref name="D"/> m along <paramref name="dirSim"/>, <paramref name="simDur"/> seconds) must change to be that take's.
        /// True = a take was drawn (the stage refits the simulation); false = no take fits (the drop stays on the pose layer).</summary>
        public bool QbDropFitPlan(int play, int steps, bool shotgun, float yawBase, Vector3 dirSim, float D, float simDur, out float distScale, out float newDur)
        {
            distScale = 1f; newDur = 0f; _qfPick = null; _qfPickPlay = play;
            var tab = QbClipTable.Load(); if (QbMc.Off || !QbMc.DropMocap || tab == null || D < 0.3f || steps <= 0) return false;
            var pool = QbMc.Pool(tab, e => e.mvB > e.mvA + 0.3f && (shotgun ? steps <= 3 && e.Steps <= 1 : e.Steps == steps), shotgun ? QbFam.DropSg : QbFam.DropUc);
            var fit = new List<QbClipTable.Entry>(); var cost = new List<float>(); float best = 9f;
            float kLo = shotgun ? 0.5f : 0.72f, kHi = shotgun ? 1.8f : 1.40f;
            foreach (var e in pool)
            {
                float dClip = e.PathLen(e.mvA, e.mvB); if (dClip < 0.2f) continue;
                float k = dClip / D; if (k < kLo || k > kHi) continue;
                float rot = QfRot(e, yawBase, e.PathAt(e.mvB) - e.PathAt(e.mvA), dirSim); if (Mathf.Abs(rot) > 25f) continue;
                float c = Mathf.Abs(Mathf.Log(k)) * 2f + Mathf.Abs(rot) / 60f;
                fit.Add(e); cost.Add(c); best = Mathf.Min(best, c);
            }
            if (fit.Count == 0) return false;
            var near = new List<QbClipTable.Entry>(); for (int i = 0; i < fit.Count; i++) if (cost[i] <= best + 0.18f) near.Add(fit[i]);
            var pick = QbMc.Pick("dropFit" + (shotgun ? "S" : "U") + steps, near, QbMc.Seed(play, 61));
            if (pick == null) return false;
            float dc = pick.PathLen(pick.mvA, pick.mvB), dur = pick.mvB - pick.mvA;
            distScale = dc / D; _qfPickScale = 1f; _qfPickRot = QfRot(pick, yawBase, pick.PathAt(pick.mvB) - pick.PathAt(pick.mvA), dirSim);
            float r0 = dur / Mathf.Max(0.2f, simDur);
            newDur = r0 > 2.0f ? dur / 2.0f : (r0 < 0.6f ? dur / 0.6f : 0f);
            _qfPick = pick;
            return true;
        }

        /// <summary>The catch-and-drop take plays on through the drop: its speed is servoed so the plant (mvB) lands when the simulation arrives. False = the take is over (the pocket loops take the body).</summary>
        bool QmUcDropRun(in AnimHints h)
        {
            if (Rig.CurrentClip != _qmClipNow) return true;      // the window has not started yet (the clip before it is still fading out)
            float t = Rig.ClipTime; var e = _qmE;
            if (t >= e.mvB + 0.30f || !Rig.InWindow) return false;
            if (h.Qb == QbPhase.Set && t >= e.mvB - 0.02f) return false;      // planted, and the simulation is set: the pocket
            float left = Mathf.Max(0f, h.QbDropWait) + (h.QbDropLeft >= 0f ? h.QbDropLeft : 0.3f), rem = e.mvB - t;
            float want = rem > 0.02f ? Mathf.Clamp(rem / Mathf.Max(0.12f, left), 0.7f, 2.2f) : 1f;
            _qmSpeed = Mathf.Lerp(_qmSpeed, want, 0.35f); Rig.SetSpeed(_qmSpeed);
            return true;
        }
    }
}
