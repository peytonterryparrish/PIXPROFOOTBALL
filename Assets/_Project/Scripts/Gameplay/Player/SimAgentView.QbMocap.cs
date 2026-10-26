using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.13.0 QB-MOCAP (owner, v0.12 playtest: "he gave many QB mocap clips and only a couple seem to be used"): the quarterback's sequence on his mocap. The pre-snap stance, the count, the snap in his hands, the pocket and the throw are windows of the
    /// BoNES / Yan quarterback takes (<see cref="QbClipTable"/>, drawn through the no-repeat bag <see cref="QbMc"/>); the pose layer (QbSeq / QbThrow, v0.9 to v0.11) is the fallback for everything the catalog has no clean window for (and for the drop, the scramble,
    /// the hand-off and the fake, which stay as they were). The owner's rule holds for every window: square stance, the ball at the chest, scanning, also out of the pocket unless he takes off.
    /// <para>Time: the sim decides the moments, the clip serves them. The snap clip is started so its catch frame lands on the tick the sim completes the snap; the throw clip is servoed every frame so its release frame lands on the tick the sim releases the ball
    /// (the clip is slowed or held at the release pose, never the ball moved).</para>
    /// <para>Facing: root motion is discarded in the match, so every window plays with its own yaw (<see cref="ActionAnimator.YawOverride"/>): the loops with the mean chest yaw of the window, the throw with the chest yaw of the release, so the chest at the release faces where the simulation faces him
    /// (and the simulation does not release before he faces the target within 20 degrees).</para>
    /// </summary>
    public partial class SimAgentView
    {
        enum Qm { None, Stance, Cadence, Snap, Pocket, Pump, Throw, Drop, Settle, Fake, Mesh, Roll, Side, Ground, Spike }
        Qm _qm; QbClipTable.Entry _qmE; int _qmPlay = -1; float _qmSince, _qmUntil, _qmSpeed = 1f;
        bool _qmOwn, _qmRelSeen, _qmCadenceTried, _qmSnapTried, _qmThrowTried, _qmPre; QbClipTable.Entry _qmSnapE;
        int _qmDraw; AnimationClip _qmClipNow; QbClipTable.Entry _qmLast; bool _qmPumped, _qmStillOn; Vector3 _qmBase, _qmOff;
        QbClipTable.Entry _qmThrowE, _qmPathE; float _qmT0, _qmPathScale; bool _qmMir; Vector3 _qmSim0;
        float _qmRot, _qmCap = QbClipTable.PathCap + 0.15f;      // v0.13.0 QB-2: a fitted take turns its path by a few degrees and has its own offset budget (SimAgentView.QbFit.cs)

        /// <summary>QA: the quarterback mocap owns the body this frame.</summary>
        public bool QbMocapActive { get { return _qmOwn; } }
        /// <summary>QA: the segment name and the window on screen ("Stance:BONES_QB_x@2.30" ...), empty when the pose layer / the old flow owns him.</summary>
        public string QbMocapName { get { return _qm == Qm.None || _qmE == null ? "" : _qm + ":" + _qmE.clip + "@" + _qmE.a.ToString("0.00"); } }
        /// <summary>QA: the row on screen (null when none).</summary>
        public QbClipTable.Entry QbMocapEntry { get { return _qm == Qm.None ? null : _qmE; } }
        /// <summary>QA: the clip time of the key moment the playing window is synced to (-1 when none) and the clip time now.</summary>
        public float QbMocapSyncTime { get { return _qm == Qm.None || _qmE == null || _qmE.sync <= 0f ? -1f : _qmE.sync; } }
        public float QbMocapClipTime { get { return Rig != null && Rig.Valid && Rig.CurrentClip == _qmClipNow ? Rig.ClipTime : -1f; } }

        void QmEnd(bool stopClip)
        {
            if (_qm == Qm.None) return;
            if (!_gait.Valid && StanceLayer != null && StanceLayer.Ok) SeedGait();      // the pose layer takes the feet over where the mocap left them (not at the stance's rest spots)
            if (stopClip && Rig != null && Rig.Valid) { Rig.SetFrozen(false); Rig.SetSpeed(1f); Rig.Stop(0.30f); _ownsClip = false; }
            _qm = Qm.None; _qmE = null; _qfFit = false; _qfYawLock = false;
        }

        void QmReset()
        {
            QfReset(); _qmFakeTried = _qmSpikeTried = false;
            _qmOff = Vector3.zero; _qmLast = null;
            _qmCadenceTried = _qmSnapTried = _qmThrowTried = false; _qmSnapE = null; _qmThrowE = null; _qmPumped = false; _qmRelSeen = false; _qmDraw = 0; _qmSpeed = 1f;
        }

        ClipPlan QmPlan(QbClipTable.Entry e, AnimationClip c, float start, float end, bool loop, float speed, float fadeIn, float phase)
        {
            return new ClipPlan { Clip = c, Start = start, End = end, Speed = speed, FadeIn = fadeIn, FadeOut = 0.30f, Loop = loop, Hold = false, Phase = phase };
        }

        /// <summary>The clip of a row for this passer (the mirror image for a left-hander when the row's throwing hand is the other one), and whether it is the mirror (the yaws flip).</summary>
        AnimationClip QmClip(QbClipTable.Entry e, out bool mirrored)
        {
            mirrored = false;
            var lib = ArtLibrary.Load(); if (lib == null) return null;
            if (e.clipRef != null) return e.clipRef;      // a mirror-image row carries its twin (its yaws, hand and path are already mirrored)
            bool needMirror = e.hand != '?' && ((e.hand == 'L') != _lefty);
            if (needMirror)
            {
                if (string.IsNullOrEmpty(e.mirror)) return null;
                mirrored = true; return lib.ClipByName(e.mirror);
            }
            return lib.ClipByName(e.clip);
        }

        bool QmHandOk(QbClipTable.Entry e) { return e.hand == '?' || (e.hand == 'L') == _lefty || !string.IsNullOrEmpty(e.mirror); }

        void QmStart(Qm seg, QbClipTable.Entry e, float start, float end, bool loop, float speed, float fadeIn, float phase, float yaw)
        {
            bool mir; var c = QmClip(e, out mir); if (c == null) { QmEnd(true); return; }
            if (mir) yaw = -yaw;
            var plan = QmPlan(e, c, start, end, loop, speed, fadeIn, phase);
            Rig.YawOverride = yaw;
            Rig.SetFrozen(false);
            Rig.Play(plan); Rig.YawOverride = float.NaN;
            _qmClipNow = c;
            _ownsClip = true; _clipYaw = yaw;
            _qm = seg; _qmE = e; _qmLast = e; _qmSince = _clock; _qmSpeed = speed;
            _qmMir = mir; _qmPathE = null;
            _qmRot = 0f; _qmCap = QbClipTable.PathCap + 0.15f; _qfFit = false; _qfLoop = false;
            if (seg != Qm.Drop && seg != Qm.Settle) _qfYawLock = false;
            { Vector3 sp0 = transform.position - _viewOff; sp0.y = 0f; _qmSim0 = sp0; _qmBase = _qmOff; }      // the display carries over from the window before: no pop at a change of window
            if (e.path != null && e.path.Length >= 4)
            {
                _qmPathE = e; _qmT0 = start; Vector2 p0 = QmPathAt(e, start); float mx = 0.001f;
                for (int i = 0; i * 2 + 1 < e.path.Length; i++) { float t = e.a + i / QbClipTable.PathRate; if (t >= start) mx = Mathf.Max(mx, (new Vector2(e.path[2 * i], e.path[2 * i + 1]) - p0).magnitude); }
                _qmPathScale = Mathf.Min(1f, QbClipTable.PathCap / mx);
            }
            _qmDraw++;
            if (Motion != null) { Motion.SetCarriage(BodyMotion.Carriage.None); Motion.CarriageFloor = 0f; }
        }

        /// <summary>Called first by <see cref="QbPoseFlow"/>. True = the mocap owns the quarterback this frame (the pose layer and the old flow stay off).</summary>
        bool QbMocapFlow(AgentAction action, in AnimHints h, bool shotgunQb, float dt)
        {
            _qmOwn = false; _qmMeshOn = false;
            if (QbMc.Off || (!h.IsQb && h.QbSpecial != 2) || Role != PlayerRole.QB || DebugNoFlow || DebugNoStancePose || Rig == null || !Rig.Valid) { QmEnd(true); return false; }
            var tab = QbClipTable.Load();
            if (tab == null || tab.entries.Count == 0) { QmEnd(true); return false; }
            if (h.QbPlay != _qmPlay) { QmEnd(true); _qmPlay = h.QbPlay; QmReset(); _qmStillOn = false; }
            bool spikeNow = h.QbSpecial == 2 && h.Qb == QbPhase.Spike;      // v0.13.0 QB-2: the spike swing is a mocap window
            if ((h.QbSpecial != 0 && !spikeNow) || h.Pitch || (h.Kind != PixFootball.Sim.PlayKind.Scrimmage && !spikeNow)) { QmEnd(true); return false; }
            if (_qbPre != QbPre.None) { QmEnd(true); return false; }      // the walk, the survey and the crouch are the stage's / the pose layer's
            float spd = _simVel.magnitude;
            if (_qmStillOn) { if (spd > 1.6f || h.QbTakeoff) _qmStillOn = false; } else if (spd < 0.9f && !h.QbTakeoff) _qmStillOn = true;      // the pocket's live feet sway at up to 0.8 m/s: the window stays on over it (the view cancels the sway)
            bool still = _qmStillOn;
            bool res;
            switch (h.Qb)
            {
                case QbPhase.PreSnap: case QbPhase.WaitSnap:
                    res = QmPreSnap(tab, h, shotgunQb, dt); break;
                case QbPhase.Dropback: case QbPhase.Set:
                    res = QmPostSnap(tab, h, shotgunQb, still, dt); break;
                case QbPhase.Windup:
                    res = QmThrow(tab, h, shotgunQb, dt); break;
                case QbPhase.Mesh:
                    res = QmMesh(tab, h, action, dt); break;
                case QbPhase.Fake:
                    res = QmFake(tab, h, shotgunQb); break;
                case QbPhase.Spike:
                    res = QmSpike(tab, h); break;
                case QbPhase.Done:
                    res = QmFollow(h, dt); break;
                default:
                    QmEnd(true); res = false; break;
            }
            if (res && _qm == Qm.Mesh) QmOffsetIdle(dt);      // the hand-off take stands where the simulation has him (the stage steps him to the runner)
            else if (res) QmApplyPath(); else { _qmPathE = null; QmOffsetIdle(dt); }
            _qmOwn = res;
            if (res && _qfFit) _loopAllowed = false;      // v0.13.0 QB-2: a fitted take turns the hips itself (the closed facing loop stays off)
            if (res && _qfYawLock) { _yaw = _qfYaw; transform.rotation = Quaternion.Euler(0f, _yaw, 0f); }      // the drawn yaw is held through the drop (the simulated reverse pivot is the clip's)
            if (res) { _qbPoseOn = false; _qbSpecial = 0; _qbStanceHold = false; _throwPose = false; if (_qtOn) QbThrowEnd(); }
            return res;
        }

        // ------------------------------------------------------------------------------------------ pre-snap: stance, count, snap
        bool QmPreSnap(QbClipTable tab, in AnimHints h, bool shotgun, float dt)
        {
            // the snap: the clip starts so its catch frame is the tick the sim completes the snap
            if (h.QbSnapEta >= 0f && !_qmSnapTried)
            {
                if (_qmSnapE == null)
                {
                    var pool = QbMc.Pool(tab, e => e.sync > e.a + 0.25f && (e.b - e.sync) >= 0.15f, shotgun ? QbFam.SnapSg : QbFam.SnapUc);
                    _qmSnapE = (_qfPick != null && _qfPickPlay == h.QbPlay) ? _qfPick : QbMc.Pick("snap" + (shotgun ? "S" : "U"), pool, QbMc.Seed(h.QbPlay, 11));      // v0.13.0 QB-2: the drop take the stage drew (it is the snap window and the drop)
                    if (_qmSnapE == null) _qmSnapTried = true;
                }
                if (_qmSnapE != null)
                {
                    float lead = _qmSnapE.sync - _qmSnapE.a;
                    if (h.QbSnapEta <= lead + 0.02f)
                    {
                        float start = Mathf.Max(_qmSnapE.a, _qmSnapE.sync - h.QbSnapEta);
                        QmStart(Qm.Snap, _qmSnapE, start, _qmSnapE.b, false, 1f, 0.18f, 0f, -_qmSnapE.yaw0);
                        if (_qmSnapE.mvB > 0f && _qmSnapE == _qfPick) { QmFitOn(_qmSnapE, start, 1f, _qfPickScale, _qfPickRot, 3.0f); _qfYaw = _yaw; _qfYawLock = true; }      // v0.13.0 QB-2: the catch-and-drop take: its path is the drop, the drawn yaw is held
                        _qmSnapTried = true;
                    }
                }
            }
            if (_qm == Qm.Snap) { QmServoSync(h.QbSnapEta, false); return true; }

            // the count: a short cadence clip that fits between the start of the count and the start of the snap clip
            if (h.QbCadenceLen > 0f && !_qmCadenceTried && _qm != Qm.Snap)
            {
                _qmCadenceTried = true;
                if (_qmSnapE == null)
                {
                    var spool = QbMc.Pool(tab, e => e.sync > e.a + 0.25f && (e.b - e.sync) >= 0.15f, shotgun ? QbFam.SnapSg : QbFam.SnapUc);
                    _qmSnapE = (_qfPick != null && _qfPickPlay == h.QbPlay) ? _qfPick : QbMc.Pick("snap" + (shotgun ? "S" : "U"), spool, QbMc.Seed(h.QbPlay, 11));
                }
                float avail = (h.QbSnapEta >= 0f ? h.QbSnapEta : h.QbCadenceLen - h.QbCadenceT + 0.35f) - (_qmSnapE != null ? _qmSnapE.sync - _qmSnapE.a : 0.1f);
                var pool = QbMc.Pool(tab, e => (e.b - e.a) >= 0.55f, shotgun ? QbFam.CadenceSg : QbFam.CadenceUc);
                var ce = avail > 0.45f ? QbMc.Pick("cad" + (shotgun ? "S" : "U"), pool, QbMc.Seed(h.QbPlay, 13)) : null;
                if (ce != null)
                {
                    float full = ce.b - ce.a, len = Mathf.Min(full, Mathf.Max(0.5f, avail * 1.2f)), sp = Mathf.Clamp(len / Mathf.Max(0.3f, avail), 0.75f, 1.35f);      // a long count is played by its last seconds (the count ends on the call)
                    QmStart(Qm.Cadence, ce, ce.b - len, ce.b, false, sp, 0.20f, 0f, -ce.yaw0);
                    _qmUntil = _clock + len / sp;
                }
            }
            if (_qm == Qm.Cadence)
            {
                if (_clock < _qmUntil && Rig.InWindow) return true;
                QmEnd(false);      // the cadence is over: its window faded out by itself, the stance loop below takes the body
            }

            // the stance: loops, a new window every few seconds (no repeat)
            if (_qm == Qm.Stance && _clock < _qmUntil && Rig.InWindow) return true;
            List<QbClipTable.Entry> sp2 = shotgun ? QbMc.Pool(tab, null, QbFam.StanceSg, QbFam.Pocket) : QbMc.Pool(tab, null, QbFam.StanceUc);
            if (!shotgun && sp2.Count == 0) { QmEnd(true); return false; }
            var e2 = QbMc.Pick("stance" + (shotgun ? "S" : "U"), QbMc.Near(sp2, _qmLast, 50f), QbMc.Seed(h.QbPlay, 17 + _qmDraw));
            if (e2 == null) { QmEnd(true); return false; }
            float phase = Mathf.Repeat(QbMc.Seed(h.QbPlay, 19 + _qmDraw) * 0.001f, Mathf.Max(0.1f, e2.b - e2.a));
            QmStart(Qm.Stance, e2, e2.a, e2.b, true, 1f, 0.35f, phase, -e2.yaw0);
            _qmUntil = _clock + 3.0f + 0.001f * (QbMc.Seed(h.QbPlay, 23 + _qmDraw) % 3000);
            return true;
        }

        /// <summary>Re-times the playing window every frame so its key moment lands exactly <paramref name="eta"/> seconds from now (the sim's tick); with no time left it holds the pose of the moment until <paramref name="released"/>.</summary>
        void QmServoSync(float eta, bool released)
        {
            if (_qmE == null || _qmE.sync <= 0f || Rig.CurrentClip != _qmClipNow) return;      // the window has not started yet (the clip before it is still fading out: the play is deferred)
            float t = Rig.ClipTime, rem = _qmE.sync - t, step = Mathf.Max(Time.deltaTime, 0.004f);
            if (released || eta < 0f) { Rig.SetFrozen(false); _qmSpeed = Mathf.MoveTowards(_qmSpeed, 1f, 6f * Time.deltaTime); Rig.SetSpeed(_qmSpeed); return; }
            if (rem <= 0.004f) { Rig.SetFrozen(true); return; }      // at the moment and the sim has not released yet: hold the pose
            Rig.SetFrozen(false);
            float want = Mathf.Clamp(rem / Mathf.Max(eta, step), 0.35f, 2.4f);
            _qmSpeed = Mathf.Lerp(_qmSpeed, want, 0.55f);
            Rig.SetSpeed(Mathf.Min(_qmSpeed, rem / step));      // never past the moment inside one frame
        }

        // ------------------------------------------------------------------------------------------ after the snap: the secure, the pocket
        bool QmPostSnap(QbClipTable tab, in AnimHints h, bool shotgun, bool still, float dt)
        {
            // v0.13.0 QB-2: the catch-and-drop take plays on through the drop (SimAgentView.QbFit.cs); the pocket loops take the body at the plant
            if (_qm == Qm.Fake) { if (Rig.InWindow && h.Qb == QbPhase.Dropback) return true; QmEnd(false); }
            if (h.Rolling && QmRoll(tab, h, shotgun)) return true;
            if (_qm == Qm.Roll) QmEnd(false);
            bool ucDrop = _qm == Qm.Snap && _qmE != null && _qmE.mvB > 0f && _qfFit;
            if (ucDrop && (h.Qb == QbPhase.Dropback || h.Qb == QbPhase.Set) && QmUcDropRun(h)) return true;
            if (_qm == Qm.Snap && !ucDrop)
            {
                // the catch clip plays out its tail (the ball pulled in) while he stands; the moment he moves the pose layer's drop takes over
                if (still && Rig.InWindow && h.Qb != QbPhase.Dropback) return true;
                if (h.Qb == QbPhase.Dropback && _clock - _qmSince < _qmE.b - _qmE.a && _simVel.magnitude < 0.35f && Rig.InWindow) return true;
                QmEnd(false);
                return false;
            }
            if ((h.Qb == QbPhase.Dropback && !ucDrop) || !still || h.Rolling) { QmEnd(true); return false; }
            return QmPocket(tab, h, shotgun);
        }

        bool QmPocket(QbClipTable tab, in AnimHints h, bool shotgun)
        {
            if (_qm == Qm.Pump) { if (Rig.InWindow) return true; QmEnd(false); }
            // a pump fake now and then while he holds the ball and nobody is on him (seeded per play)
            if (_qm == Qm.Pocket && !_qmPumped && !h.Pressure && _clock - _qmSince > 0.7f && (QbMc.Seed(h.QbPlay, 53) & 3) == 0)
            {
                var ppool = QbMc.Pool(tab, e0 => QmHandOk(e0) && e0.sync > e0.a + 0.25f, QbFam.Pump);
                var pe = QbMc.Pick("pump", ppool, QbMc.Seed(h.QbPlay, 59));
                _qmPumped = true;
                if (pe != null) { QmStart(Qm.Pump, pe, pe.a, pe.b, false, 1f, 0.25f, 0f, -pe.yaw0); return true; }
            }
            if (_qm == Qm.Pocket && _clock < _qmUntil && Rig.InWindow) return true;
            var pool = QbMc.Pool(tab, null, QbFam.Pocket, QbFam.StanceSg);

            var e = QbMc.Pick("pocket", QbMc.Near(pool, _qmLast, 50f), QbMc.Seed(h.QbPlay, 29 + _qmDraw));
            if (e == null) { QmEnd(true); return false; }
            float phase = Mathf.Repeat(QbMc.Seed(h.QbPlay, 31 + _qmDraw) * 0.001f, Mathf.Max(0.1f, e.b - e.a));
            QmStart(Qm.Pocket, e, e.a, e.b, true, 1f, 0.35f, phase, -e.yaw0);
            _qmUntil = _clock + 2.4f + 0.001f * (QbMc.Seed(h.QbPlay, 37 + _qmDraw) % 2200);
            return true;
        }

        // ------------------------------------------------------------------------------------------ the throw
        bool QmThrow(QbClipTable tab, in AnimHints h, bool shotgun, float dt)
        {
            bool released = h.BallReleased;
            if (_qm != Qm.Throw)
            {
                if (_qmThrowTried || released) { QmEnd(true); return false; }
                float run = _simVel.magnitude;
                if (run > 2.4f || h.PrevScramble || h.Rolling) { _qmThrowTried = true; QmEnd(true); return false; }      // on the move: the pose layer's throw (the upper-body mocap for the run is a follow-up)
                float eta = h.QbWindupLeft >= 0f ? h.QbWindupLeft : h.WindupSeconds;
                string kind = h.QbStyle == (int)ThrowStyle.Lob || h.Lob ? "lob" : (h.QbTargetDist > 28f ? "lob" : (h.QbTargetDist > 17f ? "touch" : "bullet"));
                Vector3 fwd = Forward; float rel = Vector3.SignedAngle(fwd, h.TargetDir.sqrMagnitude > 0.01f ? h.TargetDir : fwd, Vector3.up);
                bool lefty = _lefty;
                var pool = QbMc.Pool(tab, e => QmHandOk(e) && e.sync > e.a + 0.28f && (e.b - e.sync) >= 0.35f && Mathf.Abs(Mathf.DeltaAngle(e.yaw0, e.yawSync)) <= 38f, QbFam.ThrowSet);
                // the family of the throw by distance and pressure: the matching kind first, anything set otherwise
                var kinded = pool.FindAll(e => e.kind == kind || (kind == "bullet" && e.kind == "pass") || (kind == "touch" && (e.kind == "pass" || e.kind == "bullet")));
                if (h.Pressure) { var off = pool.FindAll(e => e.kind == "sidearm" || e.kind == "jump"); if (off.Count > 0 && (QbMc.Seed(h.QbPlay, 41) & 3) == 0) kinded = off; }
                var pick = _qmThrowE;
                if (pick == null) { bool wide = (QbMc.Seed(h.QbPlay, 47) & 1) == 0; pick = QbMc.Pick(wide ? "throwAny" : "throw" + kind, !wide && kinded.Count > 0 ? kinded : pool, QbMc.Seed(h.QbPlay, 43)); _qmThrowE = pick; }
                if (pick == null) { _qmThrowTried = true; QmEnd(true); return false; }
                float lead = pick.sync - pick.a;
                if (eta > lead / 0.45f && _qm != Qm.None) return true;      // the wind-up is long: the pocket window plays on, the throw starts when the lead-in fits
                float start = Mathf.Clamp(pick.sync - eta, pick.a, pick.sync - 0.05f);
                QmStart(Qm.Throw, pick, start, pick.b, false, 1f, 0.16f, 0f, -pick.yawSync);
                _qmThrowTried = true; _qmRelSeen = false;
            }
            QmServoSync(h.QbWindupLeft >= 0f ? h.QbWindupLeft : h.WindupSeconds, released);
            if (released) _qmRelSeen = true;
            return true;
        }

        bool QmFollow(in AnimHints h, float dt)
        {
            if (_qm == Qm.Mesh) { if (Rig.InWindow) { _qmMeshOn = true; return true; } QmEnd(false); return false; }
            if (_qm == Qm.Throw)
            {
                QmServoSync(-1f, true);
                if (Rig.InWindow && Rig.CurrentClip != null) return true;
                QmEnd(false);
            }
            else QmEnd(true);
            return false;
        }
    }
}

namespace PixFootball.Gameplay
{
    public partial class SimAgentView
    {
        static Vector2 QmPathAt(QbClipTable.Entry e, float t)
        {
            float u = Mathf.Clamp((t - e.a) * QbClipTable.PathRate, 0f, (e.path.Length / 2 - 1));
            int i = Mathf.Min((int)u, e.path.Length / 2 - 2); if (i < 0) return Vector2.zero;
            float f = u - i;
            return new Vector2(Mathf.Lerp(e.path[2 * i], e.path[2 * i + 2], f), Mathf.Lerp(e.path[2 * i + 1], e.path[2 * i + 3], f));
        }

        /// <summary>The display offset of the last window unwinds at a gentle pace when no window owns him (the pose layer'''s footfall planner follows a root that moves at most this fast).</summary>
        void QmOffsetIdle(float dt)
        {
            if (_qmOff.sqrMagnitude < 1e-6f) { _qmOff = Vector3.zero; return; }
            _qmOff = Vector3.MoveTowards(_qmOff, Vector3.zero, Mathf.Max(0.22f, _qmOff.magnitude * 0.9f) * Mathf.Max(dt, 0.0001f));
            transform.position += _qmOff;
        }

        /// <summary>The match discards the root motion of every clip; a window of a take that steps (the throw's stride, the snap's shuffle) gets its travel back as a view offset, capped at <see cref="QbClipTable.PathCap"/>: the feet stay where they were planted.
        /// The simulation's own motion over the window (the wind-up step) is replaced by the clip's, so the two are not added. When the window ends the offset decays at the catch-up pace of <see cref="ApplyViewOffset"/>.</summary>
        void QmApplyPath()
        {
            if (Rig == null || !Rig.Valid || _qm == Qm.None) return;
            var e = _qmPathE; Vector2 p = Vector2.zero;
            if (e != null && Rig.CurrentClip == _qmClipNow) { float t = Mathf.Clamp(Rig.ClipTime, e.a, e.b); if (_qfLoop) { if (t < _qfLastT - 0.4f) _qfLoops++; _qfLastT = t; } p = QmPathAt(e, t) - QmPathAt(e, _qmT0); if (_qfLoop) p += (QmPathAt(e, e.b) - QmPathAt(e, e.a)) * _qfLoops; if (_qmMir) p.x = -p.x; }
            float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Rig.NewestWeight));
            Vector3 sp = transform.position - _viewOff; sp.y = 0f;
            Vector3 simD = sp - _qmSim0; simD.y = 0f;
            Vector3 clip = Quaternion.Euler(0f, _yaw + _tableYaw + _loopYaw + _qmRot, 0f) * new Vector3(p.x, 0f, p.y) * _qmPathScale;
            Vector3 want = _qmBase + (clip - simD) * w; want.y = 0f;
            float cap = Mathf.Max(_qmCap, _qmBase.magnitude);
            if (want.magnitude > cap) want = want.normalized * cap;
            transform.position += want; _qmOff = want;
            if (simD.magnitude > 0.5f && !_qfFit) _qmStillOn = false;      // the simulation walked off: the pose layer's footfall planner takes over
        }
    }
}
