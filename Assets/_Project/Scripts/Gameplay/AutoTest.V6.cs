using System;
using System.Collections;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.6 autotest segment "look gallery" (<c>-pixAutotestLookOnly</c>): one autopilot game paused twice - at a pre-snap and at the first tackle - and photographed in the six
    /// graphics framings (broadcast pre-snap, tackle close-up, player portrait, helmet close-up, field macro, crowd wide) under the six weather / time presets, and in the v0.5 look
    /// (<see cref="PixLook.Legacy"/>) against the v0.6 look at the very same moment for the clear day and night presets. Files: <c>look_&lt;preset&gt;_&lt;framing&gt;[_v05].png</c>.
    /// </summary>
    public partial class AutoTest
    {
        /// <summary>Snow cover of the look gallery (default 0.8); -pixSnowCover and -pixSnowEv override the cover and the full-cover exposure offset for tuning.</summary>
        static float SnowArg()
        {
            var a = System.Environment.GetCommandLineArgs(); float v = 0.8f;
            for (int i = 0; i < a.Length - 1; i++)
            {
                if (a[i] == "-pixSnowCover") float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v);
                if (a[i] == "-pixSnowEv") { float e; if (float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out e)) WeatherSystem.SnowFullCoverEv = e; }
            }
            return v;
        }

        struct LookCase { public string Name; public WeatherKind Kind; public TimeOfDay Time; public bool Compare; }

        static LookCase[] LookCases()
        {
            var all = AllLookCases();
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-pixLookCase") { var only = new List<LookCase>(); foreach (var lc in all) if (args[i + 1].Contains(lc.Name)) only.Add(lc); if (only.Count > 0) return only.ToArray(); }   // e.g. -pixLookCase day_snow
            return all;
        }

        static LookCase[] AllLookCases()
        {
            return new[]
            {
                new LookCase { Name = "day_clear", Kind = WeatherKind.Clear, Time = TimeOfDay.Day, Compare = true },
                new LookCase { Name = "dusk_clear", Kind = WeatherKind.Clear, Time = TimeOfDay.Dusk },
                new LookCase { Name = "night_clear", Kind = WeatherKind.Clear, Time = TimeOfDay.Night, Compare = true },
                new LookCase { Name = "day_overcast", Kind = WeatherKind.Overcast, Time = TimeOfDay.Day },
                new LookCase { Name = "night_rain", Kind = WeatherKind.Rain, Time = TimeOfDay.Night },
                new LookCase { Name = "day_snow", Kind = WeatherKind.Snow, Time = TimeOfDay.Day },
                new LookCase { Name = "night_fog", Kind = WeatherKind.Fog, Time = TimeOfDay.Night },
            };
        }

        readonly List<Canvas> _uiHidden = new List<Canvas>();

        void LookUi(bool show)
        {
            if (!show)
            {
                _uiHidden.Clear();
                foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) if (c.enabled) { c.enabled = false; _uiHidden.Add(c); }
            }
            else { foreach (var c in _uiHidden) if (c != null) c.enabled = true; _uiHidden.Clear(); }
        }

        IEnumerator LookSettle(int frames = 6) { for (int i = 0; i < frames; i++) yield return null; yield return new WaitForSecondsRealtime(0.25f); }

        IEnumerator LookShoot(string preset, string framing, bool compare)
        {
            yield return LookSettle();
            yield return Shot("look_" + preset + "_" + framing);
            if (!compare) yield break;
            var ws = WeatherSystem.Instance;
            if (ws != null) ws.SetLegacy(true);
            yield return LookSettle();
            yield return Shot("look_" + preset + "_" + framing + "_v05");
            if (ws != null) ws.SetLegacy(false);
            yield return LookSettle(3);
        }

        IEnumerator LookPart()
        {
            var cfg = new MatchConfig
            {
                HomeId = "buffalo_ironhorns", AwayId = "miami_reefrunners", QuarterMinutes = 5, UserTeam = CoreTeam.Home, Seed = 20261023, CpuVsCpu = false, Difficulty = Difficulty.Pro,
                Weather = WeatherChoice.Clear, Time = TimeChoice.Night
            };
            MatchDirector.ForceAutopilot = true; MatchDirector.AutopilotSnapDelay = 0.9f;
            var previous = MatchDirector.Instance;
            GameSession.StartMatch(cfg);
            float wait = 0f;
            while ((MatchDirector.Instance == null || MatchDirector.Instance == previous || MatchDirector.Instance.Flow == null) && wait < 120f) { wait += Time.unscaledDeltaTime; yield return null; }
            var dir = MatchDirector.Instance;
            if (dir == null || dir.Flow == null) { Log("FAIL: match did not start"); yield break; }
            Log("look gallery: match started in " + wait.ToString("0.0") + "s, weather " + dir.Weather.Label);
            var ws = WeatherSystem.Instance;
            if (ws == null) { Log("FAIL: no WeatherSystem"); yield break; }

            // ---- 1. hold a scrimmage pre-snap
            float t0 = Time.unscaledTime;
            while (!(dir.Phase == MatchPhase.PreSnap && dir.Sim != null && dir.Sim.Kind == PlayKind.Scrimmage && dir.PlayNumber > 2) && Time.unscaledTime - t0 < 240f) yield return null;
            MatchDirector.AutopilotSnapDelay = 600f;
            yield return new WaitForSecondsRealtime(1.2f);
            LookUi(false);
            var cases = LookCases();
            foreach (var c in cases)
            {
                ws.Apply(c.Kind, c.Time, ws.ArtificialTurf);
                if (c.Kind == WeatherKind.Snow) ws.SetSnowCover(SnowArg());
                yield return LookShoot(c.Name, "1_broadcast_presnap", c.Compare);
            }
            Log("look gallery: pre-snap shots done");

            // ---- 2. snap and freeze at the first tackle
            LookUi(true);
            int tackler = -1, carrier = -1;
            dir.SimEvent += e => { if (carrier < 0 && e.Type == SimEventType.Tackle && e.AgentA >= 0 && e.AgentB >= 0) { tackler = e.AgentA; carrier = e.AgentB; } };
            ws.Apply(WeatherKind.Clear, TimeOfDay.Night, ws.ArtificialTurf);
            MatchDirector.AutopilotSnapDelay = 0.9f;
            t0 = Time.unscaledTime;
            while (carrier < 0 && Time.unscaledTime - t0 < 90f) yield return null;
            if (carrier < 0) { Log("look gallery: no tackle happened (skipping close-ups)"); yield break; }
            yield return new WaitForSecondsRealtime(0.4f);
            Time.timeScale = 0f;
            Log("look gallery: frozen at the tackle of agent " + carrier + " by " + tackler);
            LookUi(false);
            dir.Cams.SetOverride(true);
            var cam = dir.MainCamera;
            var vc = dir.Stage.Views[carrier]; var vt = dir.Stage.Views[tackler];
            Vector3 pc = vc.transform.position, pt = vt.transform.position;
            Vector3 mid = (pc + pt) * 0.5f;
            Vector3 axis = pt - pc; axis.y = 0f; if (axis.sqrMagnitude < 0.01f) axis = Vector3.forward; axis.Normalize();
            Vector3 side = new Vector3(axis.z, 0f, -axis.x);
            if (Vector3.Dot(side, Vector3.right) < 0f) side = -side;   // the side nearest the stands the broadcast camera sits in front of
            Vector3 fwd = vc.Forward;
            Transform head = vc.Avatar != null ? vc.Avatar.Head : null;
            Vector3 headPos = head != null ? head.position : pc + Vector3.up * 1.6f;
            Log("look gallery: carrier at " + pc.ToString("F1") + " number " + (dir.Sim.Agents[carrier].Player != null ? dir.Sim.Agents[carrier].Player.Bio.JerseyNumber : -1));

            foreach (var c in cases)
            {
                ws.Apply(c.Kind, c.Time, ws.ArtificialTurf);
                if (c.Kind == WeatherKind.Snow) ws.SetSnowCover(SnowArg());
                Aim(cam, mid + side * 4.6f + Vector3.up * 1.5f, mid + Vector3.up * 0.8f, 38f); yield return LookShoot(c.Name, "2_tackle_closeup", c.Compare);
                Aim(cam, pc + (fwd + side * 0.35f).normalized * 2.7f + Vector3.up * 1.5f, pc + Vector3.up * 1.25f, 28f); yield return LookShoot(c.Name, "3_player_portrait", c.Compare);
                Aim(cam, headPos + (fwd * 0.9f + side * 0.45f) + Vector3.up * 0.08f, headPos, 21f); yield return LookShoot(c.Name, "4_helmet_closeup", c.Compare);
                Aim(cam, pc + side * 3.4f - fwd * 0.5f + Vector3.up * 0.38f, pc + Vector3.up * 0.04f, 42f); yield return LookShoot(c.Name, "5_field_macro", c.Compare);
                Aim(cam, new Vector3(-30f, 3.2f, pc.z), new Vector3(60f, 12f, pc.z + 6f), 52f); yield return LookShoot(c.Name, "6_crowd_wide", c.Compare);
            }
            dir.Cams.SetOverride(false);
            Time.timeScale = 1f;
            LookUi(true);
            Log("look gallery done");
        }

        static void Aim(Camera cam, Vector3 pos, Vector3 target, float fov)
        {
            cam.transform.position = pos; cam.transform.LookAt(target); cam.fieldOfView = fov;
        }
    }
}
