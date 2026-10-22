using System;
using System.Collections;
using System.Collections.Generic;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Presentation
{
    /// <summary>
    /// Runs replays: the automatic broadcast replay after a big play (two angles, slow motion around the key moment, depth of field on the key player, skippable), the manual instant replay
    /// with the free camera (play / pause / slow motion / scrub / rewind, preset angles, target switching, ground level) and the highlight reels of the halftime and postgame shows.
    /// All timing is unscaled: the replay is independent of the game's time scale.
    /// </summary>
    public sealed class ReplayController : MonoBehaviour
    {
        MatchDirector _dir;
        ReplayPlayer _player; ReplayHud _hud; BroadcastCamera _cam; TvOverlays _overlays; ReplayLibrary _lib;
        readonly ReplayView _view = new ReplayView();
        bool _stageHidden;

        public bool Active { get; private set; }
        public bool Manual { get; private set; }
        public ReplayView View { get { return _view; } }
        public ReplayClip CurrentClip { get; private set; }
        public float Time01 { get { return CurrentClip != null && CurrentClip.Duration > 0f ? Mathf.Clamp01((_player.Time - CurrentClip.StartTime) / CurrentClip.Duration) : 0f; } }
        public string PhaseName { get; private set; } = "";
        public Func<ReplayClip, string> TitleOf;
        /// <summary>Autotest / tools: scripted input for the manual replay (read instead of the pad while set).</summary>
        [System.NonSerialized] public MatchInputFrame ScriptedInput;
        public event Action<string> Milestone;
        public float Speed { get; private set; } = 1f;
        public bool Paused { get; private set; }

        public void Attach(MatchDirector dir, ReplayPlayer player, ReplayHud hud, BroadcastCamera cam, TvOverlays overlays, ReplayLibrary lib)
        {
            _dir = dir; _player = player; _hud = hud; _cam = cam; _overlays = overlays; _lib = lib;
        }

        void Note(string s) { PhaseName = s; if (Milestone != null) Milestone(s); }

        // ============================================================================================ stage hand-over
        void Begin(ReplayClip clip, bool manual, string title, bool reel = false)
        {
            Active = true; Manual = manual; CurrentClip = clip;
            _dir.Ui?.SetReplayMode(true);
            if (_dir.Stage != null) { _dir.Stage.gameObject.SetActive(false); _stageHidden = true; }
            if (_dir.Officials != null) _dir.Officials.SetCrewVisible(false);
            if (_dir.Markers != null) _dir.Markers.HideAll();
            _player.Load(clip);
            _cam.Begin();
            _hud.Show(manual, title, clip.Situation, reel);
            if (_overlays != null) _overlays.Enabled = !manual;
        }

        void SwapClip(ReplayClip clip, string title)
        {
            CurrentClip = clip;
            _player.Load(clip);
            _hud.SetTitle(title);
        }

        void End()
        {
            _hud.Hide();
            _cam.End();
            _player.Unload();
            if (_stageHidden && _dir.Stage != null) _dir.Stage.gameObject.SetActive(true);
            _stageHidden = false;
            if (_dir.Officials != null) _dir.Officials.SetCrewVisible(true);
            _dir.Ui?.SetReplayMode(false);
            if (_overlays != null) _overlays.Enabled = true;
            Active = false; Manual = false; CurrentClip = null; PhaseName = "";
        }

        MatchInputFrame ReadInput()
        {
            if (ScriptedInput != null) return ScriptedInput;
            return MatchInput.Instance.Poll(UnityEngine.Time.unscaledDeltaTime);
        }

        static bool SkipPressed(MatchInputFrame f) { return f.Down[(int)Btn.A] || f.Menu || f.Cancel || f.Down[(int)Btn.B]; }

        // ============================================================================================ automatic broadcast replay
        /// <summary>Two angles with slow motion around the key moment and the key player in focus. <paramref name="skippable"/> lets A cut it short.</summary>
        public IEnumerator PlayAuto(ReplayClip clip, string title, bool skippable, float speedScale = 1f)
        {
            // an exception inside the replay must never leave the stage hidden and the replay HUD up (QA-864)
            try { yield return PlayAutoInner(clip, title, skippable, speedScale); }
            finally { if (Active) End(); }
        }

        IEnumerator PlayAutoInner(ReplayClip clip, string title, bool skippable, float speedScale)
        {
            if (clip == null || clip.Frames.Count < 4) yield break;
            Begin(clip, false, title);
            Note("auto replay A");
            float key = Mathf.Clamp(clip.KeyTime, clip.StartTime + 0.5f, clip.EndTime);
            int target = clip.KeyAgent;
            bool done = false;
            // ---- angle A: the sideline long lens, real time then slowing into the key moment
            float a0 = Mathf.Max(clip.StartTime + 0.1f, key - 3.3f), a1 = Mathf.Max(a0 + 1.5f, key - 0.45f);
            // ---- angle B: low orbit around the key player, slow motion through the moment
            // v0.9.0 CONNECT E-381: B used to run its whole 2 s window at 0.36x (about 5.7 s real). Now the deep slow motion is only the beat around the key moment and each angle has a hard real-time cap
            // (A 3.0 s, B 3.4 s), so a touchdown replay costs about 6.4 s instead of 8.5 s+.
            float b0 = Mathf.Max(clip.StartTime + 0.1f, key - 0.9f), b1 = Mathf.Min(clip.EndTime, key + 0.85f);
            if (b1 <= b0 + 0.6f) b1 = Mathf.Min(clip.EndTime, b0 + 1.6f);

            _view.Reset(clip, target, ReplayAngle.Sideline); _view.Dof = true;
            yield return RunSegment(a0, a1, t => 1f, skippable, v => done = v, speedScale, null, 3.0f);
            if (!done)
            {
                Note("auto replay B");
                _hud.Flash();
                _view.Reset(clip, target, ReplayAngle.Orbit, 205f); _view.Pitch = 11f; _view.Dist = 8.6f; _view.AutoOrbitRate = 16f; _view.Dof = true;
                _cam.Cut();
                yield return RunSegment(b0, b1, t => Mathf.Abs(t - key) < 0.4f ? 0.36f : 0.8f, skippable, v => done = v, speedScale, null, 3.4f);
            }
            yield return new WaitForSecondsRealtime(0.12f);
            End();
        }

        IEnumerator RunSegment(float t0, float t1, Func<float, float> speedAt, bool skippable, Action<bool> skipped, float speedScale, Func<bool> extraSkip = null, float maxReal = 0f)
        {
            float t = t0, real = 0f;   // v0.9.0 CONNECT E-381: maxReal > 0 caps the real (unscaled) duration of the segment
            _player.Time = t; Speed = 1f;
            ReplayClip clip = CurrentClip;
            ApplyCamera(clip, t, 0.016f);
            _cam.Cut();
            yield return null;
            while (t < t1)
            {
                float dt = Mathf.Min(UnityEngine.Time.unscaledDeltaTime, 0.05f);
                real += dt; if (maxReal > 0f && real > maxReal) yield break;
                float sp = speedAt(t) * speedScale;
                // ease into and out of the slow-motion window instead of a hard switch
                Speed = Mathf.MoveTowards(Speed, sp, dt * 4f);
                t += dt * Speed;
                _player.Time = t;
                ApplyCamera(clip, t, dt);
                _hud.SetSlow(Speed, Speed < 0.95f);
                if (skippable && !_dir.Autopilot && SkipPressed(ReadInput())) { skipped(true); yield break; }
                if (extraSkip != null && extraSkip()) { skipped(true); yield break; }   // a highlight reel polls its skip every frame, not only between clips (QA-863)
                yield return null;
            }
        }

        void ApplyCamera(ReplayClip clip, float t, float dt)
        {
            var s = _view.Compute(clip, t, dt);
            _cam.Set(s.Pos, s.Look, s.Fov, s.Focus, s.Aperture);
        }

        // ============================================================================================ highlight reel (halftime / postgame shows)
        /// <summary>Plays each clip once (one angle, slow motion at the key moment) with a title strip; <paramref name="skip"/> is polled to cut the reel short.</summary>
        public IEnumerator PlayReel(List<ReplayClip> clips, Func<bool> skip, Action<ReplayClip, int> onClip)
        {
            try { yield return PlayReelInner(clips, skip, onClip); }
            finally { if (Active) End(); }
        }

        IEnumerator PlayReelInner(List<ReplayClip> clips, Func<bool> skip, Action<ReplayClip, int> onClip)
        {
            if (clips == null || clips.Count == 0) yield break;
            bool first = true; int n = 0;
            foreach (var clip in clips)
            {
                if (clip.Frames.Count < 4) continue;
                string title = TitleOf != null ? TitleOf(clip) : clip.Title;
                if (first) { Begin(clip, false, title, true); first = false; } else { SwapClip(clip, title); _hud.Flash(); }
                _hud.SetTitle(title);
                if (onClip != null) onClip(clip, n);
                Note("reel " + n);
                n++;
                float key = Mathf.Clamp(clip.KeyTime, clip.StartTime + 0.5f, clip.EndTime);
                int target = clip.KeyAgent;
                ReplayAngle[] angles = { ReplayAngle.Sideline, ReplayAngle.High, ReplayAngle.FollowBall };
                _view.Reset(clip, target, angles[(n - 1) % angles.Length]); _view.Dof = angles[(n - 1) % angles.Length] != ReplayAngle.High;
                float t0 = Mathf.Max(clip.StartTime + 0.1f, key - 2.6f), t1 = Mathf.Min(clip.EndTime, key + 1.3f);
                bool skipped = false;
                yield return RunSegment(t0, t1, t => (t > key - 0.8f && t < key + 0.8f) ? 0.4f : 1f, false, v => skipped = v, 1f, skip);
                if (skipped) break;
            }
            if (!first) End();
        }

        // ============================================================================================ manual instant replay with the free camera
        /// <summary>The interactive replay: scrub, pause, slow motion, rewind, preset angles, free orbit / pan / zoom, target switching, depth of field and ground level.</summary>
        public IEnumerator PlayManual(ReplayClip clip, string title)
        {
            try { yield return PlayManualInner(clip, title); }
            finally { if (Active) { _dir.InputLocked = false; End(); } }
        }

        IEnumerator PlayManualInner(ReplayClip clip, string title)
        {
            if (clip == null || clip.Frames.Count < 4) yield break;
            Begin(clip, true, title);
            Note("manual replay");
            _dir.InputLocked = true;
            Paused = false; Speed = 1f;
            float[] speeds = { 1f, 0.5f, 0.25f };
            int speedIdx = 0;
            int clipIdx = _lib != null ? IndexOf(clip) : -1;
            float t = clip.StartTime;
            int target = clip.KeyAgent;
            _view.Reset(clip, target, ReplayAngle.Free, clip.TowardsPlusZ ? 200f : 20f);
            ReplayAngle[] cycle = { ReplayAngle.Free, ReplayAngle.Sideline, ReplayAngle.EndZone, ReplayAngle.High, ReplayAngle.BehindQb, ReplayAngle.FollowBall };
            int angleIdx = 0;
            float holdEnd = 0f;
            bool exit = false;
            yield return null;
            while (!exit)
            {
                float dt = Mathf.Min(UnityEngine.Time.unscaledDeltaTime, 0.05f);
                var inp = ReadInput();
                if (inp.AnyDown || inp.DPadLeftHeld || inp.DPadRightHeld || inp.DPadUp || inp.DPadDown || inp.Move.sqrMagnitude > 0.04f || inp.Look.sqrMagnitude > 0.04f || inp.Sprint || inp.LT) _hud.NoteActivity();
                if (inp.Down[(int)Btn.B] || inp.Menu || inp.Cancel) { exit = true; break; }
                if (inp.Down[(int)Btn.A]) Paused = !Paused;
                if (inp.Down[(int)Btn.X]) { speedIdx = (speedIdx + 1) % speeds.Length; Speed = speeds[speedIdx]; Note("speed " + Speed); }
                if (inp.Down[(int)Btn.Y]) { angleIdx = (angleIdx + 1) % cycle.Length; _view.Angle = cycle[angleIdx]; if (_view.Angle == ReplayAngle.Free) _view.Reset(clip, _view.Target, ReplayAngle.Free, clip.TowardsPlusZ ? 200f : 20f); Note("angle " + ReplayView.AngleNames[(int)_view.Angle]); }
                if (inp.Down[(int)Btn.LB]) _view.Target = StepTarget(_view.Target, -1);
                if (inp.Down[(int)Btn.RB]) _view.Target = StepTarget(_view.Target, 1);
                if (inp.DPadUp) _view.Dof = !_view.Dof;
                if (inp.DPadDown) _view.Ground = !_view.Ground;
                if (inp.View && _lib != null && _lib.Count > 1)
                {
                    clipIdx = ((clipIdx < 0 ? _lib.Count - 1 : clipIdx) - 1 + _lib.Count) % _lib.Count;
                    var c2 = _lib.Clips[clipIdx];
                    clip = c2; SwapClip(c2, TitleOf != null ? TitleOf(c2) : c2.Title); _hud.SetTitle(TitleOf != null ? TitleOf(c2) : c2.Title); _hud.Flash();
                    t = c2.StartTime; _view.Reset(c2, c2.KeyAgent, ReplayAngle.Free, c2.TowardsPlusZ ? 200f : 20f); angleIdx = 0;
                }
                // ---- free camera input (any stick movement drops a preset into the free camera without a jump)
                Vector2 mv = inp.Move, lk = inp.Look; float zoom = (inp.Sprint ? 1f : 0f) - (inp.LT ? 1f : 0f);
                if (mv.sqrMagnitude > 0.04f || lk.sqrMagnitude > 0.04f || Mathf.Abs(zoom) > 0.1f) _view.ToFree();
                if (_view.Angle == ReplayAngle.Free)
                {
                    float sens = PixSettings.LookSensitivity, inv = PixSettings.InvertLookY ? -1f : 1f;   // the same sensitivity and invert-Y options as free roam (QA-824)
                    _view.Yaw += lk.x * 120f * dt * sens; _view.Pitch = Mathf.Clamp(_view.Pitch - lk.y * inv * 75f * dt * sens, 2f, 82f);
                    _view.Dist = Mathf.Clamp(_view.Dist - zoom * 11f * dt, 3.5f, 55f);
                    float yr = _view.Yaw * Mathf.Deg2Rad;
                    Vector3 fwd = new Vector3(-Mathf.Sin(yr), 0f, -Mathf.Cos(yr)), right = new Vector3(Mathf.Cos(yr), 0f, -Mathf.Sin(yr));
                    _view.Pan += (right * mv.x + fwd * mv.y) * 7f * dt;
                    _view.Pan = Vector3.ClampMagnitude(_view.Pan, 40f);
                }
                // ---- time: D-pad held = rewind / fast forward, otherwise the play speed (paused: single steps)
                float rate = Paused ? 0f : Speed;
                if (inp.DPadLeftHeld) rate = Paused ? -0.5f : -Mathf.Max(Speed, 1f) * 1.6f; else if (inp.DPadRightHeld) rate = Paused ? 0.5f : Mathf.Max(Speed, 1f) * 2.2f;
                if (Paused && inp.DPadLeft) t -= 0.0333f; else if (Paused && inp.DPadRight) t += 0.0333f;
                t += dt * rate;
                float end = clip.EndTime;
                if (t >= end) { if (holdEnd <= 0f) holdEnd = 0.9f; holdEnd -= dt; t = end; if (holdEnd <= 0f) { t = clip.StartTime; holdEnd = 0f; } }
                else holdEnd = 0f;
                t = Mathf.Clamp(t, clip.StartTime, end);
                _player.Time = t;
                ApplyCamera(clip, t, dt);
                _hud.SetSlow(Speed, false);
                string state = Paused ? "PAUSED" : (rate < 0f ? "REWIND" : (rate > Speed + 0.01f ? "FAST FORWARD" : (Speed < 0.99f ? "SLOW MOTION  " + Speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x" : "PLAYING  1x")));
                float key01 = clip.Duration > 0f ? Mathf.Clamp01((clip.KeyTime - clip.StartTime) / clip.Duration) : 0f;
                _hud.SetTransport(clip.Duration > 0f ? (t - clip.StartTime) / clip.Duration : 0f, key01, t - clip.StartTime, clip.Duration, state, ReplayView.AngleNames[(int)_view.Angle] + (_view.Ground && _view.Angle == ReplayAngle.Free ? " (GROUND LEVEL)" : ""), TargetName(clip, _view.Target), _view.Dof);
                yield return null;
            }
            _dir.InputLocked = false;
            End();
        }

        int IndexOf(ReplayClip c) { for (int i = 0; i < _lib.Count; i++) if (_lib.Clips[i] == c) return i; return -1; }

        int StepTarget(int cur, int dir)
        {
            // ball (-1), then agents 0..21
            int n = cur + dir;
            if (n < -1) n = 21; if (n > 21) n = -1;
            return n;
        }

        string TargetName(ReplayClip c, int target)
        {
            if (target < 0) return "TARGET  BALL";
            var a = c.Agents[target];
            return "TARGET  #" + a.Number + " " + a.Position + " " + OverlayText.ShortName(a.Name);
        }

        // ============================================================================================ autotest drive
        /// <summary>Scripted camera for the autotest: set the angle, target, orbit and zoom of the running manual replay.</summary>
        public void Script(ReplayAngle angle, int target, float yaw, float pitch, float dist, bool dof, bool ground)
        {
            _view.Angle = angle; _view.Target = target; _view.Yaw = yaw; _view.Pitch = pitch; _view.Dist = dist; _view.Dof = dof; _view.Ground = ground; _view.Pan = Vector3.zero;
        }

        public void ScriptRequestExit() { ScriptedInput = new MatchInputFrame(); ScriptedInput.Down[(int)Btn.B] = true; }
    }
}
