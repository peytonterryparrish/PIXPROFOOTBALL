using CoreTeam = PixFootball.Core.Team;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 watch mode (WATCH+HUD track): every game is CPU versus CPU on the broadcast camera. The director keeps playing the user's side with the AI (the <see cref="Autopilot"/> path) but, unlike
    /// the autotest autopilot, a person is watching: the huddle, the natural snap tempo, the full replays and the postgame show all stay on, and the pad works for the system buttons
    /// (<see cref="WatchControls"/>). The user-control game is unchanged behind <see cref="WatchMode.UserControl"/>.
    /// </summary>
    public sealed partial class MatchDirector
    {
        bool _watch;

        /// <summary>True for a watch-mode game (CPU versus CPU, a person watching). Autotests (ForceAutopilot) are never watch games.</summary>
        public bool WatchActive { get { return _watch; } }

        /// <summary>The director plays the user's side AND nobody watches the pad: the autotest autopilot. Everything that used to test <c>Autopilot</c> for "skip the human parts" tests this.</summary>
        public bool AutoRun { get { return Autopilot && !_watch; } }

        /// <summary>Called once after the config is cloned: turns an ordinary game into a watch game.</summary>
        void ApplyWatchMode()
        {
            _watch = false;
            if (ForceAutopilot) return;
            if (Config.CpuVsCpu) { _watch = true; return; }           // a game that was already CPU versus CPU (tools) is watched as well
            if (!WatchMode.Enabled) return;
            Config.CpuVsCpu = true;
            _watch = true;
        }

        /// <summary>Starts the pad reader of a watch game (after the HUD exists).</summary>
        void StartWatchControls()
        {
            if (!_watch) return;
            WatchMode.SpeedIndex = 0;
            WatchControls.Attach(this);
            if (Cams != null) { Cams.FixedSide = true; Cams.SmoothBetweenPlays = true; Cams.BaseFov = 22f; Cams.SetFrame(Cams.Frame, false); }   // one sideline, glide between plays, tighter lens
            if (Ui != null) Ui.SetWatchMode(true);
        }

        /// <summary>The side the user camera presets look from: in a watch game always behind the team with the ball.</summary>
        SimSide ViewSide() { return _watch ? SimSide.Offense : UserSide(); }

        // ------------------------------------------------------------------------------------------ system buttons
        /// <summary>Pause / resume.</summary>
        public void WatchTogglePause()
        {
            if (Phase == MatchPhase.Final) return;
            SetPaused(!Paused);
        }

        /// <summary>The View button in a watch game: sideline broadcast, then the behind-the-ball presets (Classic, Madden, Wide), then back to the broadcast camera. No first person (nobody is "on" a player).</summary>
        public void WatchCycleCamera()
        {
            if (Cams == null || BroadcastBusy) return;
            if (Cams.Mode == CameraMode.Broadcast) { PixSettings.UserCameraStyle = CameraStyle.Classic; Cams.SetMode(CameraMode.Behind, false); return; }
            var s = PixSettings.UserCameraStyle;
            if (Cams.Mode == CameraMode.Behind && s != CameraStyle.Wide) { PixSettings.UserCameraStyle = (CameraStyle)((int)s + 1); return; }
            Cams.SetMode(CameraMode.Broadcast, false);
        }

        /// <summary>Applies the watch game speed to the time scale (not while paused, in a hit-stop or in a replay).</summary>
        public void WatchApplySpeed()
        {
            if (Paused || BroadcastBusy) return;
            float want = WatchMode.Speed;
            float ts = Time.timeScale;
            if (ts < 0.5f) return;                    // a hit-stop or slow motion owns the clock right now
            if (Mathf.Abs(ts - want) > 0.001f) Time.timeScale = want;
        }

        internal bool WatchPadFree { get { return !InputLocked && !BroadcastBusy && Phase != MatchPhase.Final; } }   // B-101: a replay, the intro and the shows own Start / Esc (they skip) - it must not also pause

        float _runoffWindow = 5f;

        int _periodHold;
        /// <summary>B-078 / B-096: while the period break banner shows, the bar still reads the old quarter at 0:00 (it used to jump to "2ND 3:00 / 4TH & 1" before the result).</summary>
        void ApplyPeriodHold(ScoreSnapshot sc)
        {
            if (_periodHold <= 0) return;
            sc.Quarter = _periodHold == 1 ? "1ST" : _periodHold == 2 ? "2ND" : _periodHold == 3 ? "3RD" : _periodHold == 4 ? "4TH" : "OT";
            sc.Clock = "0:00"; sc.DownDistance = ""; sc.Spot = ""; sc.TwoMinute = false; sc.PlayClock = -1;
        }

        /// <summary>A kickoff, punt or field goal / point-after is the current play (the HUD shows the wind dial only then, B-077).</summary>
        public bool KickingPlay { get { return Sim != null && (Sim.Kind == PlayKind.Kickoff || Sim.Kind == PlayKind.Punt || Sim.Kind == PlayKind.FieldGoal); } }

        /// <summary>The final screen headline of a watched game (it used to say FINAL twice, B-089): who won.</summary>
        string WatchFinalHeadline(CoreTeam? w) { return w.HasValue ? TeamName(w.Value).ToUpperInvariant() + " WIN" : "FINAL"; }

        /// <summary>Tells the camera rig whether a kick or a pass is in the air (the broadcast camera then leads the ball).</summary>
        static readonly bool NoBallCam = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoBallCam") >= 0;   // QA: the pre-v0.11 broadcast camera
        void CameraBallHint() { if (Cams == null || Sim == null) return; var st = Sim.Ball.State; Cams.BallInFlight = !NoBallCam && (st == BallState.Kick || st == BallState.Pass); Cams.KickInFlight = !NoBallCam && st == BallState.Kick; }
    }

    /// <summary>
    /// The pad (or keyboard) in a watch game. Start / Esc pause, View / Tab cycles the camera, Y / R hides the HUD, X / C turns the helper bar off and on, RB / X key switches 1x and 2x speed.
    /// Polled in Update so every phase (pre-snap, live play, the whistle window, breaks) hears it, and while paused (the time scale is 0 but Update still runs).
    /// </summary>
    public sealed class WatchControls : MonoBehaviour
    {
        MatchDirector _dir;
        bool _prevPaused;
        public bool HudHidden { get; private set; }

        public static WatchControls Attach(MatchDirector d)
        {
            var w = d.gameObject.AddComponent<WatchControls>();
            w._dir = d;
            return w;
        }

        void Update()
        {
            if (_dir == null || !_dir.WatchActive) return;
            _dir.WatchApplySpeed();
            if (MatchInput.Instance == null || !_dir.WatchPadFree) { _prevPaused = _dir.Paused; return; }
            var inp = MatchInput.Instance.Poll(Time.unscaledDeltaTime);
            if (inp == null) return;
            bool paused = _dir.Paused, was = _prevPaused;
            _prevPaused = paused;
            if (inp.Menu)
            {
                if (was && !paused) return;      // the pause menu closed itself on this very press (B / Esc): it must not open again
                _dir.WatchTogglePause();
                _prevPaused = _dir.Paused;
                return;
            }
            if (paused) return;
            if (inp.View) _dir.WatchCycleCamera();
            if (inp.Down[(int)Btn.Y]) { HudHidden = !HudHidden; if (_dir.Ui != null) _dir.Ui.SetHudUserHidden(HudHidden); }
            if (inp.Down[(int)Btn.X])
            {
                WatchMode.SetHelper(WatchMode.ToggleHelper(WatchMode.Helper));
                if (_dir.Ui != null) _dir.Ui.ShowToast(WatchMode.Helper == HelperBarMode.Off ? "HELPER BAR OFF" : "HELPER BAR ON", 1.4f);
            }
            if (inp.Down[(int)Btn.RB]) { WatchMode.NextSpeed(); _dir.WatchApplySpeed(); if (_dir.Ui != null) _dir.Ui.ShowToast("SPEED " + WatchMode.SpeedLabel, 1.2f); }
            if (_dir.Cams != null && _dir.Cams.Mode == CameraMode.FirstPerson) _dir.Cams.SetMode(CameraMode.Broadcast, false);
        }
    }
}
