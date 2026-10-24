using System;
using System.Collections;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Presentation
{
    public sealed partial class BroadcastDirector
    {
        // ---- shot queue
        readonly List<ShotPlan> _queue = new List<ShotPlan>();
        ShotPlan? _activeShot; float _shotAge; float _queueDelay;
        ShotContext _ctx = new ShotContext();
        bool _preShotsScheduled, _nextPlayShown;
        int _focusAgent, _secondAgent = -1;

        public bool ShotActive { get { return _activeShot.HasValue; } }
        public ShotKind ActiveShot { get { return _activeShot.HasValue ? _activeShot.Value.Kind : ShotKind.None; } }
        public int QueuedShots { get { return _queue.Count; } }

        /// <summary>Cuts straight back to the live camera and drops every shot that has not played (a snap, a rebuilt play, a replay taking over).</summary>
        public void AbortShots()
        {
            _queue.Clear(); _activeShot = null; _queueDelay = 0f;
            if (_cam != null && _cam.Active && (_rc == null || !_rc.Active) && !_introActive && !_showActive) _cam.End();
        }

        /// <summary>Queues shots from the autotest or a tool (the camera director plays them over the live scene).</summary>
        public void QueueShots(IList<ShotPlan> shots, float delay = 0f)
        {
            _queue.AddRange(shots); _queueDelay = delay;
        }

        void TickShots(float dt)
        {
            if (_cam == null || (_rc != null && _rc.Active) || _introActive || _showActive) return;
            if (!_activeShot.HasValue)
            {
                if (_queue.Count == 0) return;
                if (_queueDelay > 0f) { _queueDelay -= dt; return; }
                StartShot(_queue[0]); _queue.RemoveAt(0);
            }
            if (!_activeShot.HasValue) return;
            var plan = _activeShot.Value;
            _shotAge += dt;
            float u = Mathf.Clamp01(_shotAge / Mathf.Max(0.1f, plan.Duration));
            _ctx.Dt = dt;   // v0.9.0 CONNECT E-394: the tight shots smooth their subject / facing with it
            var pose = ShotLibrary.Evaluate(plan.Kind, u, _ctx);
            _cam.Set(pose.Pos, pose.Look, pose.Fov, pose.Focus, pose.Aperture > 0f ? pose.Aperture : 2f);
            _dir.Ui?.SetHint(""); if (_dir.Markers != null) _dir.Markers.SetRing(null);   // the cinematic shots carry no controller prompts or player ring
            if (u >= 1f)
            {
                _activeShot = null;
                if (_queue.Count == 0) { _cam.End(); }
            }
        }

        void StartShot(ShotPlan plan)
        {
            _activeShot = plan; _shotAge = 0f; ShotsPlayed++; LastShot = plan.Kind.ToString();
            var stage = _dir.Stage;
            ReplayView.SideOverride = _dir.Cams != null && _dir.Cams.FixedSide ? 1f : 0f;   // v0.11: the replay sideline angle is on the live camera side
            _ctx = new ShotContext { LiveSide = _dir.Cams != null ? _dir.Cams.CameraSide(_frame) : (_frame.TowardsPlusZ ? 1f : -1f), Downfield = _frame.TowardsPlusZ ? 1f : -1f, Variant = ShotsPlayed };
            if (_sim != null) _ctx.Los = _frame.ToUnity(new Vec2(_sim.BallX0, _sim.LosY));
            int focus = _focusAgent, second = _secondAgent;
            switch (plan.Kind)
            {
                case ShotKind.SidelineQb:
                    _ctx.Subject = () => stage.Views[0] != null ? stage.Views[0].transform.position : _ctx.Los;
                    break;
                case ShotKind.TightCarrier:
                case ShotKind.Celebration:
                    _ctx.Subject = () => ViewPos(focus);
                    _ctx.Second = () => second >= 0 ? ViewPos(second) : Vector3.zero;
                    _ctx.Facing = () => ViewForward(focus);
                    break;
                case ShotKind.CoachReaction:
                {
                    int team = _coachTeam;
                    var tr = _dir.Officials != null ? _dir.Officials.CoachTransform(team, 0) : null;
                    _ctx.Subject = () => tr != null ? tr.position : new Vector3(team == 0 ? -27.6f : 27.6f, 0f, _ctx.Los.z);
                    break;
                }
                case ShotKind.CrowdShot:
                    _ctx.Subject = () => _ctx.Los;
                    break;
            }
            if (!_cam.Active) _cam.Begin();
            var pose = ShotLibrary.Evaluate(plan.Kind, 0f, _ctx);
            _cam.Set(pose.Pos, pose.Look, pose.Fov, pose.Focus, pose.Aperture > 0f ? pose.Aperture : 2f, true);
        }
        int _coachTeam;

        Vector3 ViewPos(int agent)
        {
            var v = _dir.Stage != null && agent >= 0 && agent < 22 ? _dir.Stage.Views[agent] : null;
            return v != null ? v.transform.position : _ctx.Los;
        }

        Vector3 ViewForward(int agent)
        {
            var v = _dir.Stage != null && agent >= 0 && agent < 22 ? _dir.Stage.Views[agent] : null;
            return v != null ? v.Forward : Vector3.forward;
        }

        // ============================================================================================ pre-snap hook
        public void OnPreSnapFrame(float dt, float t, float playClock, bool snapping)
        {
            if (!_attached) return;
            if (snapping) { AbortShots(); return; }
            if (_rc.Active || _introActive || _showActive) return;
            if (!_preShotsScheduled && t >= 0.35f && _sim != null && _sim.Phase == PlayPhase.PreSnap) { _preShotsScheduled = true; SchedulePreSnap(playClock, t); }
            if (!_nextPlayShown && t >= 0.8f) { _nextPlayShown = true; MaybeShowNextPlay(); }
            TickShots(dt);
        }

        void SchedulePreSnap(float playClock, float t)
        {
            bool userOff = !_dir.Config.CpuVsCpu && State.Possession == _dir.Config.UserTeam;
            float room;
            if (_dir.AutoRun) room = MatchDirector.AutopilotSnapDelay - t;
            else room = userOff ? playClock : 3.1f;
            var ctx = new DirectorContext
            {
                Cinematics = PixSettings.Cinematics, UserPlaying = !_dir.Config.CpuVsCpu && !_dir.AutoRun, GameStart = _firstPlay, FirstPlayOfDrive = _firstPlayOfDrive, AfterScore = _lastWasScore,
                FirstPlayOfHalf = _firstPlayOfHalf, Kickoff = _sim.Kind == PlayKind.Kickoff, DriveIndex = _driveIndex, PreSnapSeconds = room
            };
            var plan = ShotRules.PreSnap(ctx);
            _lastWasScore = false;
            if (plan.Count == 0) return;
            _focusAgent = 0; _secondAgent = -1;
            _queue.Clear(); _queue.AddRange(plan); _queueDelay = 0f;
        }

        void MaybeShowNextPlay()
        {
            if (!PixSettings.TvOverlays || _sim == null || _sim.Kind != PlayKind.Scrimmage || State.Phase != GamePhase.Scrimmage) return;
            if (PixSettings.Scorebug == ScorebugStyle.Bottom) return;   // v0.11 B-088: the bottom bar already shows down, distance, spot and clock
            var s = State;
            bool twoMin = (s.Quarter == 2 || s.Quarter == 4) && s.ClockSeconds <= 120f;
            bool notable = s.Down >= 3 || s.IsGoalToGo || (_firstPlayOfDrive && _driveIndex > 1) || twoMin;
            if (_playsSinceCard < 2) notable = notable && s.Down == 4;
            if (notable)
            {
                int spot = s.BallSpot; var own = s.Possession; var opp = own.Opponent();
                _ov.ShowNextPlay(OverlayText.DownAndDistance(s.Down, s.Distance, s.IsGoalToGo), SpotText(spot, own, opp), s.ClockDisplay);
                _playsSinceCard = 0;
            }
            else if (_playsSinceCompare >= 9 && _playsSinceCard >= 3 && _trk != null && _trk.Plays >= 8)
            {
                _ov.ShowComparison(OverlayText.CompareShort(_trk.HomeBox, _trk.AwayBox), VisOf(CoreTeam.Away), VisOf(CoreTeam.Home), "TEAM STATS");
                _playsSinceCompare = 0; _playsSinceCard = 0;
            }
        }

        // ============================================================================================ post-play shots, reactions
        int FocusAgentOf(PlaySimulation sim, out int second)
        {
            second = -1;
            int focus = sim.Carrier != null ? sim.Carrier.Id : -1;
            var ev = sim.Log.Events;
            for (int i = ev.Count - 1; i >= 0; i--) if (ev[i].Type == SimEventType.Touchdown && ev[i].AgentA >= 0) { focus = ev[i].AgentA; break; }   // the scorer, whoever holds the ball now
            for (int i = ev.Count - 1; i >= 0; i--)
            {
                var e = ev[i];
                if (e.Type == SimEventType.Tackle && e.AgentA >= 0 && e.AgentB >= 0) { if (focus < 0) focus = e.AgentB; second = e.AgentA; break; }
                if (e.Type == SimEventType.Sack && e.AgentB >= 0) { if (focus < 0) focus = e.AgentB; second = e.AgentA; break; }
            }
            if (focus < 0)
                for (int i = ev.Count - 1; i >= 0; i--) { var e = ev[i]; if ((e.Type == SimEventType.Catch || e.Type == SimEventType.Interception) && e.AgentA >= 0) { focus = e.AgentA; break; } }
            return focus >= 0 ? focus : 0;
        }

        void QueuePostPlayShots(PlayOutcome o, PlaySimulation sim, float score)
        {
            _lastWasScore = o.ScoringTeam.HasValue;
            if (_rc.Active) return;
            int second; _focusAgent = FocusAgentOf(sim, out second); _secondAgent = second;
            var r = o.Result;
            bool turnover = r != null && r.Turnover || o.Headline == Headline.TurnoverOnDowns || o.Headline == Headline.Interception || o.Headline == Headline.FumbleLost;
            CoreTeam benefit = o.Offense;
            if (o.ScoringTeam.HasValue) benefit = o.ScoringTeam.Value;
            else if (turnover || o.Headline == Headline.Sack) benefit = o.Offense.Opponent();
            var ctx = new DirectorContext
            {
                Cinematics = PixSettings.Cinematics, UserPlaying = !_dir.Config.CpuVsCpu && !_dir.AutoRun, Headline = o.Headline, Touchdown = o.Headline == Headline.Touchdown, Turnover = turnover,
                Score = o.ScoringTeam.HasValue, BigPlay = score >= HighlightScorer.FullThreshold, HomeBenefits = benefit == CoreTeam.Home,
                CarrierKnown = o.Headline != Headline.Incomplete && o.Headline != Headline.Kickoff && o.Headline != Headline.Punt && o.Headline != Headline.Penalty && o.Headline != Headline.Kneel && o.Headline != Headline.Spike && sim.Kind == PlayKind.Scrimmage,
                PlaysSinceCrowd = _playsSinceCrowd,
                // v0.9.0 CONNECT E-380/E-381: fit the plan into the match director's wind-down (same numbers as MatchDirector's `wind`) so no shot outlives it
                WindSeconds = ShotRules.WindFor(o.Headline == Headline.Touchdown, sim.Kind == PlayKind.Scrimmage && sim.Result != null && sim.Result.Type == PlayType.Penalty),
                FirstShotDelay = o.Headline == Headline.Touchdown ? 0.75f : 0.5f
            };
            var plan = ShotRules.PostPlay(ctx);
            _coachTeam = TeamIdxOf(benefit);
            if (plan.Count == 0) return;
            _queue.Clear(); _queue.AddRange(plan);
            _queueDelay = o.Headline == Headline.Touchdown ? 0.75f : 0.5f;
            if (plan.Exists(p => p.Kind == ShotKind.CrowdShot)) _playsSinceCrowd = 0;
        }

        void CrowdOnTouchdown(CoreTeam scoring)
        {
            if (_crowd == null) return;
            if (scoring == CoreTeam.Home) { _crowd.Excite(1f); _playsSinceCrowd = 0; } else _crowd.Calm();
        }

        void ReactToOutcome(PlayOutcome o, PlaySimulation sim, float score)
        {
            var r = o.Result;
            bool turnover = r != null && r.Turnover || o.Headline == Headline.TurnoverOnDowns || o.Headline == Headline.Interception || o.Headline == Headline.FumbleLost;
            CoreTeam benefit = o.Offense; float strength = 0f;
            switch (o.Headline)
            {
                case Headline.Touchdown: benefit = o.ScoringTeam ?? o.Offense; strength = 1f; break;
                case Headline.Safety: benefit = o.ScoringTeam ?? o.Offense.Opponent(); strength = 0.8f; break;
                case Headline.Interception: case Headline.FumbleLost: case Headline.TurnoverOnDowns: benefit = o.Offense.Opponent(); strength = 0.9f; break;
                case Headline.Sack: benefit = o.Offense.Opponent(); strength = 0.55f; break;
                case Headline.FieldGoal: benefit = o.Offense; strength = 0.7f; break;
                case Headline.TwoPointGood: benefit = o.Offense; strength = 0.6f; break;
                default:
                    if (r != null && (r.Type == PlayType.Run || r.Type == PlayType.Pass) && r.Yards >= 20) { benefit = o.Offense; strength = 0.65f; }
                    else if (_bigHit) { benefit = o.Offense.Opponent(); strength = 0.4f; }
                    break;
            }
            if (strength > 0f && _crowd != null)
            {
                if (benefit == CoreTeam.Home) { _crowd.Excite(strength); _playsSinceCrowd = 0; }
                else if (o.Headline == Headline.Touchdown || turnover) _crowd.Calm();
            }
            // coaches clap for the team that benefits (touchdowns already do through the official's signal)
            if (strength >= 0.55f && _dir.Officials != null && o.Headline != Headline.Touchdown) _dir.Officials.CoachCheer(TeamIdxOf(benefit));
            if (o.Headline == Headline.Touchdown) StartCoroutine(CelebrationRoutine(sim, o));      // v0.11.0 SIM-FLOW (B-070): a kickoff-return touchdown gets its celebration too (the shot rule queued it already)
        }

        // ---- touchdown celebration: teammates run to the scorer and cheer, the beaten defenders hang their heads
        IEnumerator CelebrationRoutine(PlaySimulation sim, PlayOutcome o)
        {
            var stage = _dir.Stage;
            if (stage == null || sim == null) yield break;
            int scorer = sim.Carrier != null ? sim.Carrier.Id : -1;
            if (scorer < 0) { var ev = sim.Log.Events; for (int i = ev.Count - 1; i >= 0; i--) if (ev[i].Type == SimEventType.Touchdown) { scorer = ev[i].AgentA; break; } }
            if (scorer < 0 || scorer > 21) yield break;
            yield return new WaitForSeconds(0.35f);
            if (stage.Sim != sim) yield break;
            var side = sim.Agents[scorer].Side;
            Vector3 sp = stage.WorldPos(scorer);
            var mates = new List<KeyValuePair<float, int>>(); var foes = new List<KeyValuePair<float, int>>();
            for (int i = 0; i < 22; i++)
            {
                if (i == scorer || stage.Views[i] == null) continue;
                var a = sim.Agents[i]; if (a.IsDown) continue;
                float d = (stage.WorldPos(i) - sp).magnitude;
                if (a.Side == side) { if (d < 28f) mates.Add(new KeyValuePair<float, int>(d, i)); } else if (d < 20f) foes.Add(new KeyValuePair<float, int>(d, i));
            }
            mates.Sort((x, y) => x.Key.CompareTo(y.Key)); foes.Sort((x, y) => x.Key.CompareTo(y.Key));
            int nm = Mathf.Min(7, mates.Count);
            for (int k = 0; k < nm; k++)
            {
                int ag = mates[k].Value;
                float ang = (k * 360f / Mathf.Max(1, nm) + 25f) * Mathf.Deg2Rad;
                float rad = 1.5f + 0.55f * (k % 2);
                Vector3 target = sp + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * rad;
                stage.WalkTo(ag, target, 6.3f, sp);
            }
            for (int k = 0; k < Mathf.Min(5, foes.Count); k++) stage.PlayReaction(foes[k].Value, "frustration", 11 + k * 3, false);
            var done = new HashSet<int>();
            float t = 0f;
            while (t < 3.4f && stage.Sim == sim)
            {
                t += Time.deltaTime;
                for (int k = 0; k < nm; k++)
                {
                    int ag = mates[k].Value;
                    if (done.Contains(ag) || !stage.WalkArrived(ag)) continue;
                    done.Add(ag);
                    stage.PlayReaction(ag, (k & 1) == 0 ? "cheer" : "celebrate_td_3", 5 + k * 7, true);
                }
                yield return null;
            }
        }
    }
}
