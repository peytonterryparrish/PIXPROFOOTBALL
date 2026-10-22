using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.6: the wind indicator and the kick meter of the HUD (All-Pro 2K8 style). Both are DISPLAY-ONLY: the simulation has no wind and no kick timing / aim input yet (logged as SIM rows).</summary>
    public sealed partial class MatchDirector
    {
        float _kickT, _kickFreeze, _kickTiming = 0.5f, _kickAim = 0.5f, _windWorldDeg = -1f, _windMph, _lastWindShown = float.NaN;
        bool _kickShown;

        /// <summary>Wind of the game: the weather's strength scaled to mph and a direction seeded from the match seed (a gentle breeze on a clear day).</summary>
        void SetupWind()
        {
            float strength = Weather.WindStrength;
            _windMph = Mathf.Round(Mathf.Clamp(2f + strength * 26f, 0f, 30f));
            if (Weather.Kind == PixFootball.Core.WeatherKind.Clear && strength < 0.05f) _windMph = Mathf.Round(3f + (Config.Seed & 7));
            _windWorldDeg = ((Config.Seed != 0 ? Config.Seed : 1234) * 47 % 360 + 360) % 360;
        }

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

            bool kick = Sim.Kind == PlayKind.Kickoff || Sim.Kind == PlayKind.Punt || Sim.Kind == PlayKind.FieldGoal;
            bool userKick = kick && !Config.CpuVsCpu && _userIsOffense;
            if (!userKick) { if (_kickShown) { Ui.SetKickMeter(false, 0.5f, "", 0.5f, ""); _kickShown = false; } _kickT = 0f; _kickFreeze = 0f; return; }
            if (Sim.Phase == PlayPhase.PreSnap)
            {
                _kickFreeze = 0f; _kickT += dt;
                _kickTiming = 0.5f + 0.5f * Mathf.Sin(_kickT * 2.6f);
                // the wind's sideways push drifts the aim marker; display only
                float side = Mathf.Sin((_windWorldDeg - view) * Mathf.Deg2Rad);
                _kickAim = Mathf.Clamp01(0.5f + side * Mathf.Clamp01(_windMph / 30f) * 0.45f);
            }
            else _kickFreeze += dt;
            if (_kickFreeze > 1.8f) { if (_kickShown) { Ui.SetKickMeter(false, 0.5f, "", 0.5f, ""); _kickShown = false; } return; }
            float dev = Mathf.Abs(_kickTiming - 0.5f);
            string tl = dev < 0.07f ? "Perfect" : dev < 0.18f ? "Good" : (_kickTiming < 0.5f ? "Early" : "Late");
            float ad = Mathf.Abs(_kickAim - 0.5f);
            string al = ad < 0.06f ? "Centered" : (_kickAim < 0.5f ? "Left" : "Right");
            Ui.SetKickMeter(true, _kickTiming, tl, _kickAim, al); _kickShown = true;
        }
    }
}
