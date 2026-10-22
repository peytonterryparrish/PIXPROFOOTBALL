using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.7: the wind indicator and the kick meter of the HUD (All-Pro 2K8 style), and the user's kick control. The meter DRIVES the kick now:
    /// the timing cursor ping-pongs along the bar, the left stick steers the aim, and the kick button (A) stops the cursor and starts the kicker's
    /// approach; the timing reading scales power and accuracy in the simulation (<see cref="KickMeter"/>), the aim reading steers the ball.
    /// Kickoff: one press (it also blows the whistle). Field goal / extra point / punt: A snaps, then A again kicks while the meter runs.
    /// </summary>
    public sealed partial class MatchDirector
    {
        const float KickCursorPeriod = KickMeter.CursorPeriodSeconds;   // v0.9.0 (E-312): 1.45 s, eased near the sweet spot
        float _kickT, _kickFreeze, _kickTiming = 0.5f, _kickAim = 0.5f, _windWorldDeg = -1f, _windMph, _lastWindShown = float.NaN;
        float _kickStickX;
        bool _kickShown, _kickLocked, _kickPrePressed;
        float _kickLockedTiming, _kickLockedAim;

        /// <summary>Wind of the game: the weather's strength scaled to mph and a direction seeded from the match seed (a gentle breeze on a clear day).</summary>
        void SetupWind()
        {
            float strength = Weather.WindStrength;
            _windMph = Mathf.Round(Mathf.Clamp(2f + strength * 26f, 0f, 30f));
            if (Weather.Kind == PixFootball.Core.WeatherKind.Clear && strength < 0.05f) _windMph = Mathf.Round(3f + (Config.Seed & 7));
            _windWorldDeg = ((Config.Seed != 0 ? Config.Seed : 1234) * 47 % 360 + 360) % 360;
        }

        /// <summary>The stick in the simulation frame (X lateral), through the camera the way the rest of the match reads it.</summary>
        Vec2 KickStickToSim(MatchInputFrame inp)
        {
            float yaw = Cams != null ? Cams.MoveBasisYaw * Mathf.Deg2Rad : 0f, s = Mathf.Sin(yaw), c = Mathf.Cos(yaw);
            Vector2 st = inp.Move;
            Vector2 world = new Vector2(st.x * c + st.y * s, -st.x * s + st.y * c);
            return _frame.DirToSim(world);
        }

        /// <summary>True when this play is a kick of the user's team that the user is kicking (the meter applies).</summary>
        bool UserKickPlay()
        {
            return Sim != null && !Config.CpuVsCpu && _userIsOffense && !(Autopilot && AutoMove == null)
                   && (Sim.Kind == PlayKind.Kickoff || Sim.Kind == PlayKind.Punt || Sim.Kind == PlayKind.FieldGoal);
        }

        /// <summary>The agent the simulation should treat as user-controlled before and at the snap: the user's kicker (so the kick waits for the meter), otherwise nobody.</summary>
        int KickUserPreSnap()
        {
            return UserKickPlay() ? Sim.KickerSlot : -1;
        }

        /// <summary>Called every frame (pre-snap and live) with the pad: remembers the aim stick for the meter.</summary>
        void KickInputSample(MatchInputFrame inp)
        {
            if (!UserKickPlay() || inp == null) return;
            float x = KickStickToSim(inp).X;
            _kickStickX = Mathf.Abs(x) < 0.12f ? 0f : Mathf.Clamp(x, -1f, 1f);
        }

        /// <summary>Kickoff: the pre-snap press both blows the whistle and starts the run-up; its meter reading is sent as soon as the kicker is ready.</summary>
        void KickPrePress()
        {
            _kickPrePressed = true;
            _kickLocked = true;
            _kickLockedTiming = KickMeter.TimingFromCursor(_kickTiming);
            _kickLockedAim = _kickStickX;
            _kickFreeze = 0f;
        }

        /// <summary>
        /// The user's kick control during a live kick play. Returns true when it handled the frame (the user controls the kicker until the ball is struck).
        /// </summary>
        bool KickControl(MatchInputFrame inp, ref ControlInput ci, out int simUser)
        {
            simUser = -1;
            if (!UserKickPlay() || Sim.Phase != PlayPhase.Live || Sim.KickStruck) return false;
            int kid = Sim.KickerSlot;
            if (kid < 0) return false;
            UserAgent = kid; simUser = kid;
            KickInputSample(inp);
            ci.Move = Vec2.Zero;
            if (Sim.AwaitingUserKick)
            {
                if (_kickPrePressed)
                {
                    ci.KickPress = true; ci.KickPower = 1f; ci.KickTiming = _kickLockedTiming; ci.KickAim = _kickLockedAim;
                    _kickPrePressed = false;
                }
                else if (!_kickLocked && inp.Down[(int)Btn.A])
                {
                    float timing = KickMeter.TimingFromCursor(_kickTiming);
                    ci.KickPress = true; ci.KickPower = 1f; ci.KickTiming = timing; ci.KickAim = _kickStickX;
                    _kickLocked = true; _kickLockedTiming = timing; _kickLockedAim = _kickStickX; _kickFreeze = 0f;
                }
            }
            return true;
        }

        void ResetKickMeter() { _kickLocked = false; _kickPrePressed = false; _kickFreeze = 0f; _kickT = 0f; _kickStickX = 0f; }

        void UpdateKickHud(float dt)
        {
            if (Ui == null || Sim == null) return;
            if (_windWorldDeg < 0f) SetupWind();
            // the wind the simulation really uses (engine SIM-021): speed and compass direction of the current quarter
            if (Flow != null && Flow.State != null && Flow.State.Conditions != null)
            {
                PixFootball.Core.MatchConditions mc = Flow.State.Conditions.ForQuarter(Flow.State.Quarter);
                _windMph = Mathf.Round(mc.WindMph); _windWorldDeg = mc.WindToDegrees;
            }
            // the arrow is drawn relative to the camera: up = along the view direction, so "tailwind" always points up the screen for the user's drive
            float view = Cams != null ? Cams.MoveBasisYaw : 0f;
            float rel = Mathf.Repeat(_windWorldDeg - view, 360f);
            if (Mathf.Abs(rel - _lastWindShown) > 0.5f || float.IsNaN(_lastWindShown)) { Ui.SetWind(rel, _windMph); _lastWindShown = rel; }

            bool userKick = UserKickPlay();
            if (!userKick) { if (_kickShown) { Ui.SetKickMeter(false, 0.5f, "", 0.5f, ""); _kickShown = false; } ResetKickMeter(); return; }

            bool running = !_kickLocked && !Sim.KickStruck && (Sim.Phase == PlayPhase.PreSnap || Sim.AwaitingUserKick);
            if (running)
            {
                _kickFreeze = 0f; _kickT += dt;
                _kickTiming = KickMeter.PingPongEased(_kickT, KickCursorPeriod);   // 0.5 = the sweet spot (v0.9.0 E-312: the cursor slows there)
            }
            else _kickFreeze += dt;
            if (_kickFreeze > 1.8f) { if (_kickShown) { Ui.SetKickMeter(false, 0.5f, "", 0.5f, ""); _kickShown = false; } return; }
            float timing = _kickLocked ? _kickLockedTiming : KickMeter.TimingFromCursor(_kickTiming);
            float aim = _kickLocked ? _kickLockedAim : _kickStickX;
            _kickAim = Mathf.Clamp01(0.5f + 0.5f * aim);
            Ui.SetKickMeter(true, _kickTiming, KickMeter.TimingLabel(timing), _kickAim, KickMeter.AimLabel(aim)); _kickShown = true;
        }
    }
}
