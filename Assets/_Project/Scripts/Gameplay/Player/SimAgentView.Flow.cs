using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.7.0 Track A (playtest notes 7, 9, 24, 25, 30): the presentation flow of one player.
    ///  - Commit windows: a throw / catch / hand-off / jump / get-off / celebration is not cut by a lower-priority change of the simulated action (the Run that follows a Catch, the Idle after a Throw); it plays out and then blends away.
    ///  - The quarterback follows his simulation state instead of one generic locomotion: stance, snap, distance-matched drop-back (the clip is scrubbed by the metres he has left to travel, so the feet cannot slide),
    ///    pocket idle with the ball high at the chest, occasional pump fake, scramble with the ball cocked by the ear, throws cut around their measured release.
    /// </summary>
    public partial class SimAgentView
    {
        float _clock, _commitUntil; int _commitPri;
        enum QbMode { None, Drop, Pocket, Loco }
        QbMode _qbMode;
        float _dropD0, _dropTa, _dropTb, _modeSince;
        bool _pumped, _pumping;

        /// <summary>QA: one line of the flow state (QB mode, playing clip, clip-layer weight, carriage).</summary>
        public string DebugFlow { get { return _qbMode + " / " + (Rig != null ? Rig.CurrentClipName + " w" + Rig.Weight.ToString("0.00") : "-") + " / carriage " + (Motion != null ? Motion.CurrentCarriage.ToString() + "(" + Motion.ShownCarriage + ")" : "-") + " / commit " + (_clock < _commitUntil ? _commitPri.ToString() : "-"); } }

        /// <summary>QA: switch the commit windows and the quarterback flow off (the "before" state).</summary>
        public static bool DebugNoFlow;

        void ResetFlow()
        {
            _clock = 0f; _commitUntil = 0f; _commitPri = 0; _qbMode = QbMode.None; _dropD0 = 0f; _pumped = _pumping = false;
            if (Motion != null) Motion.SetCarriage(BodyMotion.Carriage.None);
        }

        // ------------------------------------------------------------------------------------------ commit windows
        /// <summary>How hard a simulated action is to interrupt: a lower-priority action never cuts a committed higher-priority one that is still playing.</summary>
        public static int Priority(AgentAction a)
        {
            switch (a)
            {
                case AgentAction.Tackled: case AgentAction.Fallen: case AgentAction.TackleWrapUp: case AgentAction.TackleBigHit: case AgentAction.TackleShoestring: case AgentAction.TackleDive: case AgentAction.TackleGang: return 9;
                case AgentAction.Celebrate: return 8;
                case AgentAction.Kick: return 7;
                case AgentAction.Throw: case AgentAction.Handoff: case AgentAction.Catch: return 6;
                case AgentAction.Jump: case AgentAction.Spin: case AgentAction.Juke: case AgentAction.StiffArm: case AgentAction.Truck: case AgentAction.Hurdle: case AgentAction.Dive: return 5;
                case AgentAction.Engaged: case AgentAction.Shed: case AgentAction.Stumble: return 3;
                case AgentAction.Block: return 2;
                case AgentAction.Stance: return 0;
                default: return 1;
            }
        }

        AgentAction CommitFilter(AgentAction action)
        {
            if (DebugNoFlow) return action;
            if (_clock < _commitUntil && action != _lastAction && Priority(action) < _commitPri) return _lastAction;
            return action;
        }

        void NoteCommit(AgentAction action, in ClipPlan plan, bool getOff)
        {
            _commitUntil = 0f; _commitPri = 0;
            if (plan.Clip == null) return;
            float len = (plan.End > plan.Start ? plan.End - plan.Start : plan.Clip.length - plan.Start) / Mathf.Max(0.1f, plan.Speed);
            if (plan.Loop) return;
            switch (action)
            {
                case AgentAction.Catch:
                    // v0.9.0 CONNECT E-144: the catch commit ends at the hands-secure frame (about 0.45 s) and ranks below the carrier moves, so a juke / spin / stiff arm after the catch is not suppressed
                    _commitPri = 4; _commitUntil = _clock + Mathf.Clamp(Mathf.Min(len * 0.85f, 0.45f), 0.25f, 0.6f); break;
                case AgentAction.Throw: case AgentAction.Handoff: case AgentAction.Jump:
                    _commitPri = Priority(action); _commitUntil = _clock + Mathf.Clamp(len * 0.85f, 0.3f, 1.4f); break;
                case AgentAction.Celebrate:
                    _commitPri = 8; _commitUntil = _clock + Mathf.Min(len * 0.95f, 7f); break;
                case AgentAction.Kick:
                    _commitPri = 7; _commitUntil = _clock + Mathf.Clamp(len * 0.8f, 0.3f, 2f); break;
                default:
                    if (getOff) { _commitPri = 2; _commitUntil = _clock + Mathf.Clamp(len * 0.8f, 0.3f, 1.2f); }
                    break;
            }
        }

        /// <summary>Clips that are not locked to a simulation event play at the pace of the game (heavier bodies, the same tempo as the motion): GamePace.AnimationRate.</summary>
        static ClipPlan PacedPlan(AgentAction action, ClipPlan plan)
        {
            switch (action)
            {
                case AgentAction.Juke: case AgentAction.Spin: case AgentAction.StiffArm: case AgentAction.Truck: case AgentAction.Stumble: case AgentAction.Engaged:      // v0.9.0 CONNECT E-363
                case AgentAction.Fallen: case AgentAction.Tackled: case AgentAction.Celebrate: case AgentAction.Shed: case AgentAction.Block: case AgentAction.Jump:
                case AgentAction.TackleWrapUp: case AgentAction.TackleBigHit: case AgentAction.TackleShoestring: case AgentAction.TackleDive: case AgentAction.TackleGang:
                    plan.Speed *= GamePace.AnimationRate; break;
            }
            return plan;
        }

        // ------------------------------------------------------------------------------------------ the quarterback
        /// <summary>Returns true when the quarterback flow owns the player this frame (the generic action to clip mapping must not run).</summary>
        bool QbFlow(AgentAction action, in AnimHints h, bool shotgunQb, float dt)
        {
            _qbStanceHold = false;
            if (QbPoseFlow(action, h, shotgunQb, dt)) return true;      // v0.9.0 QBSTANCE: drop-back and pocket on the pose layer
            if (DebugNoFlow || !h.IsQb || Rig == null || !Rig.Valid) return false;
            switch (action)
            {
                case AgentAction.Idle: case AgentAction.Run: case AgentAction.Sprint: case AgentAction.Backpedal: case AgentAction.Shuffle: case AgentAction.Carry: case AgentAction.Block: case AgentAction.Route: break;
                case AgentAction.Stance: return false;
                default: _qbMode = QbMode.None; if (Motion != null) Motion.SetCarriage(BodyMotion.Carriage.None); return false;      // throw, hand-off, tackled ... : the generic path plays the take
            }
            var q = h.Qb;
            if (q == QbPhase.PreSnap || q == QbPhase.WaitSnap) { _qbStanceHold = true; return true; }      // v0.9.0: the stance pose layer stays up until the ball is in his hands; the snap is on its way: keep the stance until the ball is in his hands
            if (q == QbPhase.Done || q == QbPhase.Kneel || q == QbPhase.Spike || q == QbPhase.Windup)
            {
                if (Motion != null) Motion.SetCarriage(BodyMotion.Carriage.None);
                _qbMode = QbMode.None;
                return false;
            }
            var carriage = BodyMotion.Carriage.Chest;
            if (q == QbPhase.Scramble || (q == QbPhase.Dropback && h.Rolling)) carriage = BodyMotion.Carriage.Ear;
            if (Motion != null) Motion.SetCarriage(carriage);
            if (Motion != null) Motion.CarriageFloor = (_qbMode == QbMode.Drop || _qbMode == QbMode.Pocket) ? 0.7f : 0f;

            bool drop = q == QbPhase.Dropback && !h.Rolling && h.DropSteps > 0 && h.DropRemaining > 0.9f;
            if (drop)
            {
                if (_qbMode == QbMode.None) StartDrop(h, shotgunQb);
                if (_qbMode == QbMode.Drop)
                {
                    float p = Mathf.Clamp01(1f - h.DropRemaining / Mathf.Max(0.5f, _dropD0));
                    float t = p < 0.06f ? Mathf.Lerp(0f, _dropTa, p / 0.06f) : Mathf.Lerp(_dropTa, _dropTb, (p - 0.06f) / 0.94f);
                    Rig.Scrub(t);
                    if (p >= 0.985f) EnterPocket(h);
                    return true;
                }
            }
            switch (q)
            {
                case QbPhase.Set: case QbPhase.Dropback:
                    if (!(q == QbPhase.Dropback && h.Rolling))
                    {
                        if (_qbMode != QbMode.Pocket) EnterPocket(h);
                        else PocketLife(h);
                        return true;
                    }
                    break;
            }
            // mesh, play-action fake, scramble, roll-out: locomotion under the carriage
            if (_qbMode != QbMode.Loco)
            {
                if (_ownsClip) { Rig.Stop(DebugNoConnect ? 0.22f : 0.38f); _ownsClip = false; }      // v0.9.0 CONNECT E-288: the crouch is let go over a longer blend (the pop filter absorbs the rest) instead of a plain 0.22 s fade
                _qbMode = QbMode.Loco;
            }
            return true;
        }

        void StartDrop(in AnimHints h, bool shotgunQb)
        {
            ClipPlan plan; float ta, tb, dist;
            if (!AnimKit.QbDrop(h, shotgunQb, out plan, out ta, out tb, out dist)) { EnterPocket(h); return; }
            Rig.Play(plan); _ownsClip = true; _clipYaw = ClipFacingTable.Offset(plan.Clip);
            Rig.SetScrub(true);
            _qbMode = QbMode.Drop; _dropD0 = h.DropRemaining; _dropTa = ta; _dropTb = tb; _modeSince = _clock;
        }

        void EnterPocket(in AnimHints h)
        {
            var plan = AnimKit.QbPocket(h.Seed);
            _qbMode = QbMode.Pocket; _modeSince = _clock; _pumped = _pumping = false;
            if (plan.Valid && !DebugNoConnect && Rig.CurrentClip == plan.Clip && Rig.InWindow) { _ownsClip = true; }      // v0.9.0 CONNECT E-287: the stance loop that is playing IS the pocket loop: it continues (no restart at another phase)
            else if (plan.Valid) { Rig.Play(plan); _ownsClip = true; _clipYaw = ClipFacingTable.Offset(plan.Clip); }
            else if (_ownsClip) { Rig.Stop(0.22f); _ownsClip = false; }
        }

        void PocketLife(in AnimHints h)
        {
            if (_pumping)
            {
                if (!Rig.InWindow) { _pumping = false; var back = AnimKit.QbPocket(h.Seed); if (back.Valid) { Rig.Play(back); _clipYaw = ClipFacingTable.Offset(back.Clip); } }
                return;
            }
            // a pump fake now and then while he holds the ball (seeded per player and play, so it is the same in a replay of the same play)
            if (!_pumped && !h.Pressure && _clock - _modeSince > 0.85f && (h.Seed & 3) == 0)
            {
                _pumped = true;
                var pump = AnimKit.QbPump(h.Seed);
                if (pump.Valid) { Rig.Play(pump); _clipYaw = ClipFacingTable.Offset(pump.Clip); _pumping = true; }
            }
        }
    }
}
