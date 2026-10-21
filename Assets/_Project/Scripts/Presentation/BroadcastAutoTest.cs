using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Presentation
{
    /// <summary>
    /// v0.5 autotest segment (registered with <see cref="AutoTest.ExtraParts"/>, so it runs as part of -pixAutotest, or alone with -pixAutotestBroadcastOnly): one autopilot game with
    /// short quarters that goes through everything the broadcast layer does and takes a screenshot of each piece into Logs/autotest (bc_*.png): the game intro (wide shot, matchup card,
    /// stadium and weather card), the pre-snap establishing shots, the post-play cuts (touchdown celebration, crowd), the automatic replay at both angles with slow motion and depth of
    /// field, a manual replay with the free camera at every preset angle, every TV overlay, first-person on offense and defense, the halftime show and the postgame show; FPS is reported
    /// for the live play and for the replays / cinematics separately.
    /// </summary>
    public sealed class BroadcastTestRunner
    {
        readonly AutoTest _t;
        MatchDirector _dir; BroadcastDirector _bd;
        readonly HashSet<string> _done = new HashSet<string>();
        readonly List<float> _liveFrames = new List<float>(), _castFrames = new List<float>();
        string _lastShot = ""; float _shotSince; string _lastReplayPhase = ""; float _replaySince; string _lastCard = ""; int _passes;

        public BroadcastTestRunner(AutoTest t) { _t = t; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { AutoTest.ExtraParts.Add(t => new BroadcastTestRunner(t).Run()); }

        void Log(string s) { _t.LogLine(s); }

        static float Arg(string name, float def)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) { float v; if (float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return v; }
            return def;
        }

        IEnumerator Delayed(float seconds, string name)
        {
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < seconds) yield return null;
            if (_done.Add("shot:" + name)) yield return _t.Capture(name);
        }

        void ShotAfter(float seconds, string name) { if (!_done.Contains("shot:" + name) && !_done.Contains("queued:" + name)) { _done.Add("queued:" + name); _t.StartCoroutine(Delayed(seconds, name)); } }

        public IEnumerator Run()
        {
            Log("---- v0.5 broadcast autotest: " + UpscalerControl.Report());
            var cfg = new MatchConfig { HomeId = "buffalo_ironhorns", AwayId = "miami_reefrunners", QuarterMinutes = 2, RunoffScale = 0.4f, UserTeam = CoreTeam.Home, Seed = 20261030, CpuVsCpu = false, Difficulty = Difficulty.Pro };
            MatchDirector.ForceAutopilot = true; MatchDirector.AutopilotSnapDelay = 4.4f;
            var previous = MatchDirector.Instance;
            GameSession.StartMatch(cfg);
            float wait = 0f;
            while ((MatchDirector.Instance == null || MatchDirector.Instance == previous || MatchDirector.Instance.Flow == null) && wait < 120f) { wait += Time.unscaledDeltaTime; yield return null; }
            _dir = MatchDirector.Instance;
            if (_dir == null || _dir.Flow == null) { Log("FAIL: broadcast match did not start"); yield break; }
            _bd = BroadcastDirector.Instance;
            if (_bd == null) { Log("FAIL: no BroadcastDirector in the match scene"); yield break; }
            Log("broadcast match started in " + wait.ToString("0.0") + "s");
            Log("crowd flash material: " + _bd.Crowd.FlashInfo);
            var pages = new Queue<string>();
            _bd.PageShown += p => pages.Enqueue(p);
            var cards = new Queue<string>();
            _bd.Overlays.CardShown += c => cards.Enqueue(c);
            bool staged = false; int tds = 0, plays = 0;
            _dir.PlayFinished += o => { plays++; if (o.Headline == Headline.Touchdown) tds++; if (o.Result != null && o.Result.Type == PlayType.Pass) _passes++; };
            _dir.PrePlayHook = fl => { if (!staged && tds == 0 && plays >= 3 && fl.State.Phase == GamePhase.Scrimmage && fl.State.Possession == fl.UserTeam) { staged = true; fl.State.BallSpot = 96; fl.State.Down = 1; fl.State.Distance = 4; Log("  (staged a goal-line situation for the touchdown replay)"); } };
            Time.timeScale = Arg("-pixAutotestSpeed", 1.4f);
            float start = Time.unscaledTime, budget = 60f * 24f;
            bool galleryRun = false, manualRun = false, fpOffRun = false, fpDefRun = false;
            float introSeen = -1f; string lastPhaseLog = "";
            int maxPlays = (int)Arg("-pixAutotestPlays", 0);   // short runs (upscaler comparison): stop after this many plays
            while (!_dir.Finished && Time.unscaledTime - start < budget)
            {
                float dtu = Time.unscaledDeltaTime;
                bool busy = _bd.Busy || _bd.Cam.Active;
                bool afterCapture = Time.frameCount - _t.LastCaptureFrame < 10;   // the screenshot itself costs a few hundred ms: not part of the frame statistics
                if (dtu > 0.1f && !afterCapture) Log("  hitch " + (dtu * 1000f).ToString("0") + " ms (phase " + _dir.Phase + ", replay=" + _bd.Replay.PhaseName + ", show=" + _bd.CurrentShow + "/" + _bd.CurrentPage + ", shot=" + _bd.ActiveShot + ", cam=" + _bd.Cam.Active + ")");
                if (!afterCapture)
                {
                    if (_dir.Phase == MatchPhase.Live && !busy && Time.timeScale > 0f) _liveFrames.Add(dtu);
                    else if (busy || _dir.Phase == MatchPhase.PostPlay) _castFrames.Add(dtu);
                }
                // ---- pages and cards raise their screenshots
                while (pages.Count > 0) OnPage(pages.Dequeue());
                while (cards.Count > 0) OnCard(cards.Dequeue());
                // ---- intro
                if (_bd.CurrentShow == "intro") { if (introSeen < 0f) introSeen = Time.unscaledTime; if (Time.unscaledTime - introSeen > 0.5f) ShotAfter(0f, "bc_intro_wide"); }
                // ---- establishing / post-play shots
                string shot = _bd.ShotActive ? _bd.ActiveShot.ToString() : "";
                if (shot != _lastShot) { _lastShot = shot; _shotSince = Time.unscaledTime; }
                if (shot.Length > 0 && Time.unscaledTime - _shotSince > 1.1f)
                {
                    switch (_bd.ActiveShot)
                    {
                        case ShotKind.SkySwoop: ShotAfter(0f, "bc_shot_sky"); break;
                        case ShotKind.SidelineQb: ShotAfter(0f, "bc_shot_sideline_qb"); break;
                        case ShotKind.EndZone: ShotAfter(0f, "bc_shot_endzone"); break;
                        case ShotKind.TightCarrier: ShotAfter(0f, "bc_shot_tight_carrier"); break;
                        case ShotKind.Celebration: ShotAfter(0f, "bc_shot_celebration"); break;
                        case ShotKind.CoachReaction: ShotAfter(0f, "bc_shot_coach"); break;
                        case ShotKind.CrowdShot: ShotAfter(0.4f, "bc_shot_crowd"); break;
                    }
                }
                // ---- automatic replay
                if (_bd.Replay.Active && !_bd.Replay.Manual)
                {
                    if (_bd.Replay.PhaseName != _lastReplayPhase) { _lastReplayPhase = _bd.Replay.PhaseName; _replaySince = Time.unscaledTime; }
                    float rs = Time.unscaledTime - _replaySince;
                    if (_lastReplayPhase == "auto replay A") { if (rs > 1.0f) ShotAfter(0f, "bc_replay_a1"); if (rs > 2.6f) ShotAfter(0f, "bc_replay_a2"); }
                    else if (_lastReplayPhase == "auto replay B") { if (rs > 0.9f) ShotAfter(0f, "bc_replay_b1"); if (rs > 2.4f) ShotAfter(0f, "bc_replay_b2"); }
                    else if (_lastReplayPhase.StartsWith("reel")) { if (rs > 1.4f) ShotAfter(0f, "bc_reel_" + _bd.CurrentShow + "_" + _lastReplayPhase.Substring(5)); }
                }
                // ---- scripted probes (each once, at a quiet moment)
                var sim = _dir.Sim;
                if (maxPlays == 0 && !_probeBusy && !fpOffRun && _dir.Phase == MatchPhase.PreSnap && sim != null && sim.Kind == PlayKind.Scrimmage && !busy && !_bd.ShotActive && UserSide() == SimSide.Offense && plays >= 2 && _fpDone == 0) { fpOffRun = true; _t.StartCoroutine(FirstPerson("bc_fp_offense")); }
                if (maxPlays == 0 && !_probeBusy && !fpDefRun && _dir.Phase == MatchPhase.PreSnap && sim != null && sim.Kind == PlayKind.Scrimmage && !busy && !_bd.ShotActive && UserSide() == SimSide.Defense && plays >= 2 && _fpDone == 1) { fpDefRun = true; _t.StartCoroutine(FirstPerson("bc_fp_defense")); }
                if (maxPlays == 0 && !_probeBusy && !galleryRun && _dir.Phase == MatchPhase.PreSnap && sim != null && sim.Kind == PlayKind.Scrimmage && sim.Time < 0.8f && !busy && _bd.Library.Count >= 2 && plays >= 5)
                { galleryRun = true; yield return OverlayGallery(); }
                if (maxPlays == 0 && !_probeBusy && !manualRun && galleryRun && _dir.Phase == MatchPhase.PreSnap && sim != null && sim.Kind == PlayKind.Scrimmage && !busy && _bd.Library.Count >= 3)
                { manualRun = true; yield return ManualReplay(); }
                if (maxPlays > 0 && _dir.Phase == MatchPhase.Live && plays >= 2 && sim != null && sim.Time > 1.2f) ShotAfter(0f, "bc_upscaler_" + UpscalerControl.Mode);
                if (maxPlays > 0 && plays >= maxPlays) break;
                string pl = _dir.Phase + "/Q" + _dir.Flow.State.Quarter;
                if (pl != lastPhaseLog && (_dir.Phase == MatchPhase.Break)) { lastPhaseLog = pl; Log("  phase " + pl); }
                yield return null;
            }
            // the postgame show runs after Finished is set
            float w2 = 0f;
            while (!_bd.PostgameShown && w2 < 150f && _bd.CurrentShow != "") { w2 += Time.unscaledDeltaTime; while (pages.Count > 0) OnPage(pages.Dequeue()); yield return null; }
            float w3 = 0f; while (w3 < 1.5f) { w3 += Time.unscaledDeltaTime; yield return null; }
            Time.timeScale = 1f;
            Report(plays, tds);
        }

        SimSide UserSide() { return _dir.Flow.State.Possession == _dir.Config.UserTeam ? SimSide.Offense : SimSide.Defense; }

        void OnPage(string p)
        {
            Log("  page: " + p);
            string show = _bd.CurrentShow;
            switch (p)
            {
                case "matchup": ShotAfter(1.5f, "bc_intro_matchup"); break;
                case "stadium": ShotAfter(1.5f, "bc_intro_stadium"); break;
                case "halftime stats": ShotAfter(1.2f, "bc_halftime_stats"); break;
                case "halftime performers": ShotAfter(1.2f, "bc_halftime_performers"); break;
                case "final": ShotAfter(1.5f, "bc_postgame_final"); break;
                case "box score": ShotAfter(1.2f, "bc_postgame_stats"); break;
                case "leaders": ShotAfter(1.2f, "bc_postgame_performers"); break;
                case "return": ShotAfter(1.2f, "bc_postgame_return"); break;
                case "highlights": break;
            }
        }

        void OnCard(string id)
        {
            _lastCard = id;
            if (id.StartsWith("stat:")) ShotAfter(1.1f, "bc_overlay_stat_third");
            else if (id == "drive") ShotAfter(1.2f, "bc_overlay_drive_summary");
            else if (id == "score") ShotAfter(1.2f, "bc_overlay_score_update");
            else if (id == "penalty") ShotAfter(1.0f, "bc_overlay_penalty_third");
            else if (id == "twominute") ShotAfter(1.0f, "bc_overlay_two_minute");
            else if (id == "nextplay") ShotAfter(1.0f, "bc_overlay_next_play");
            else if (id == "compare") ShotAfter(1.2f, "bc_overlay_compare");
            else if (id == "ticker") ShotAfter(0.9f, "bc_overlay_ticker");
        }

        // ============================================================================================ probes
        bool _probeBusy;
        int _fpDone;
        IEnumerator FirstPerson(string name)
        {
            _probeBusy = true;
            int cine = PixSettings.Cinematics; PixSettings.Cinematics = 2;   // no director cuts while the first-person camera is being checked
            // first person on the line (pre-snap), then again when the snap has happened and the play is live
            var mode = _dir.Cams.Mode;
            _dir.Cams.SetMode(CameraMode.FirstPerson, false);
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.9f && _dir.Phase == MatchPhase.PreSnap) yield return null;
            Log("  first-person " + name + " (line): mode=" + _dir.Cams.Mode + " mask showing=" + _bd.FirstPerson.Showing + " user agent=" + _dir.UserAgent + " phase=" + _dir.Phase);
            yield return _t.Capture(name + "_presnap");
            float w = 0f;
            while (w < 9f && !(_dir.Phase == MatchPhase.Live && _dir.Sim != null && _dir.Sim.Time > 0.9f)) { w += Time.unscaledDeltaTime; yield return null; }
            Log("  first-person " + name + " (live): mask showing=" + _bd.FirstPerson.Showing + " user agent=" + _dir.UserAgent + " phase=" + _dir.Phase + " sim time " + (_dir.Sim != null ? _dir.Sim.Time.ToString("0.0") : "-"));
            yield return _t.Capture(name + "_live");
            t0 = Time.unscaledTime; while (Time.unscaledTime - t0 < 0.8f) yield return null;
            yield return _t.Capture(name + "_live2");
            PixSettings.Cinematics = cine;
            _dir.Cams.SetMode(mode, false);
            _fpDone++;
            _probeBusy = false;
        }

        IEnumerator OverlayGallery()
        {
            _probeBusy = true;
            Log("  overlay gallery (game paused)");
            GameSession.SetPaused(true);
            var ov = _bd.Overlays;
            Action[] shows = { _bd.Debug_DriveSummary, () => _bd.Debug_PlayerStat(), _bd.Debug_Comparison, _bd.Debug_TwoMinute, _bd.Debug_NextPlay, _bd.Debug_Penalty, _bd.Debug_Score, _bd.Debug_Ticker };
            string[] names = { "bc_gallery_drive", "bc_gallery_stat", "bc_gallery_compare", "bc_gallery_twominute", "bc_gallery_nextplay", "bc_gallery_penalty", "bc_gallery_score", "bc_gallery_ticker" };
            for (int i = 0; i < shows.Length; i++)
            {
                ov.ClearAll();
                float c0 = Time.unscaledTime; while (Time.unscaledTime - c0 < 0.35f) yield return null;
                shows[i]();
                float t0 = Time.unscaledTime; while (Time.unscaledTime - t0 < 1.15f) yield return null;
                if (_done.Add("shot:" + names[i])) yield return _t.Capture(names[i]);
            }
            // two cards at once: the stat third and the drive summary sit in different slots and never overlap
            ov.ClearAll();
            _bd.Debug_PlayerStat(); _bd.Debug_DriveSummary(); _bd.Debug_NextPlay();
            float t1 = Time.unscaledTime; while (Time.unscaledTime - t1 < 1.2f) yield return null;
            if (_done.Add("shot:bc_gallery_combined")) yield return _t.Capture("bc_gallery_combined");
            // crowd wave and camera flashes
            ov.ClearAll();
            _bd.Debug_Crowd(1f);
            _bd.Debug_Shot(ShotKind.CrowdShot, 3.2f);
            float t2 = Time.unscaledTime; while (Time.unscaledTime - t2 < 1.4f) { _bd.OnPlayFrame(Time.unscaledDeltaTime, false); yield return null; }
            if (_done.Add("shot:bc_gallery_crowd")) yield return _t.Capture("bc_gallery_crowd");
            float t3 = Time.unscaledTime; while (Time.unscaledTime - t3 < 1.3f) { _bd.OnPlayFrame(Time.unscaledDeltaTime, false); yield return null; }
            if (_done.Add("shot:bc_gallery_crowd2")) yield return _t.Capture("bc_gallery_crowd2");
            _bd.AbortShots();
            ov.ClearAll();
            GameSession.SetPaused(false);
            Time.timeScale = Arg("-pixAutotestSpeed", 1.4f);
            Log("  crowd: " + _bd.Crowd.ChunkCount + " chunks, flashes emitted " + _bd.Crowd.FlashCount);
            _probeBusy = false;
        }

        IEnumerator ManualReplay()
        {
            _probeBusy = true;
            Log("  manual replay with the free camera (game paused)");
            GameSession.SetPaused(true);
            _bd.RequestManualReplay();
            float w = 0f; while (!(_bd.Replay.Active && _bd.Replay.Manual) && w < 8f) { w += Time.unscaledDeltaTime; yield return null; }
            if (!_bd.Replay.Active) { Log("FAIL: manual replay did not start"); GameSession.SetPaused(false); yield break; }
            var rc = _bd.Replay; var clip = rc.CurrentClip;
            Log("  replaying '" + clip.Title + "' (" + clip.Frames.Count + " frames, " + clip.Duration.ToString("0.0") + " s, key agent " + clip.KeyAgent + " at " + clip.KeyTime.ToString("0.0") + " s)");
            rc.ScriptedInput = new MatchInputFrame();   // neutral scripted pad: we steer the camera through Script()
            yield return Hold(0.7f); yield return Cap("bc_manual_free_default");
            int key = clip.KeyAgent; int other = clip.SecondAgent >= 0 ? clip.SecondAgent : (key + 11) % 22;
            rc.Script(ReplayAngle.Free, key, clip.TowardsPlusZ ? 150f : 330f, 22f, 9f, true, false); yield return Hold(0.5f); yield return Cap("bc_manual_free_orbit_dof");
            rc.Script(ReplayAngle.Free, other, 60f, 5f, 7f, true, true); yield return Hold(0.5f); yield return Cap("bc_manual_ground_level_other_player");
            rc.Script(ReplayAngle.Sideline, key, 0f, 0f, 0f, true, false); yield return Hold(0.5f); yield return Cap("bc_manual_sideline");
            rc.Script(ReplayAngle.EndZone, key, 0f, 0f, 0f, true, false); yield return Hold(0.5f); yield return Cap("bc_manual_endzone");
            rc.Script(ReplayAngle.High, key, 0f, 0f, 0f, false, false); yield return Hold(0.5f); yield return Cap("bc_manual_high");
            rc.Script(ReplayAngle.BehindQb, 0, 0f, 0f, 0f, true, false); yield return Hold(0.5f); yield return Cap("bc_manual_behind_qb");
            rc.Script(ReplayAngle.FollowBall, -1, 0f, 0f, 0f, true, false); yield return Hold(0.5f); yield return Cap("bc_manual_follow_ball");
            // pause, slow motion and a scrub through the transport
            rc.Script(ReplayAngle.Free, key, clip.TowardsPlusZ ? 200f : 20f, 16f, 8f, true, false);
            yield return Press(rc, Btn.X); yield return Hold(0.4f); yield return Press(rc, Btn.A); yield return Hold(0.4f); yield return Cap("bc_manual_paused_slow_ui");
            var f = new MatchInputFrame(); f.DPadLeftHeld = true; rc.ScriptedInput = f; yield return Hold(0.6f); rc.ScriptedInput = new MatchInputFrame(); yield return Cap("bc_manual_scrubbed");
            rc.ScriptRequestExit();
            float w2 = 0f; while (rc.Active && w2 < 4f) { w2 += Time.unscaledDeltaTime; yield return null; }
            rc.ScriptedInput = null;
            _dir.Ui?.SetPaused(false);
            GameSession.SetPaused(false);
            Time.timeScale = Arg("-pixAutotestSpeed", 1.4f);
            Log("  manual replay finished (recorder mean sample cost " + (_bd.Recorder.Samples > 0 ? (_bd.Recorder.TotalSampleMs / _bd.Recorder.Samples).ToString("0.000") : "n/a") + " ms over " + _bd.Recorder.Samples + " samples)");
            _probeBusy = false;
        }

        IEnumerator Hold(float s) { float t0 = Time.unscaledTime; while (Time.unscaledTime - t0 < s) yield return null; }
        IEnumerator Cap(string n) { if (_done.Add("shot:" + n)) yield return _t.Capture(n); }
        IEnumerator Press(ReplayController rc, Btn b) { var f = new MatchInputFrame(); f.Down[(int)b] = true; rc.ScriptedInput = f; yield return null; rc.ScriptedInput = new MatchInputFrame(); yield return null; }

        // ============================================================================================ report
        static void Stats(List<float> frames, out float avg, out float low1, out float min)
        {
            avg = low1 = min = 0f; if (frames.Count < 10) return;
            var f = new List<float>(frames); float sum = 0f, max = 0f; foreach (float x in f) { sum += x; max = Mathf.Max(max, x); }
            f.Sort(); float p99 = f[Mathf.Min(f.Count - 1, (int)(f.Count * 0.99f))];
            avg = f.Count / sum; low1 = 1f / p99; min = 1f / max;
        }

        void Report(int plays, int tds)
        {
            float a, l, m;
            Stats(_liveFrames, out a, out l, out m);
            Log("FPS [v0.5 live play, broadcast layer on] frames=" + _liveFrames.Count + " avg=" + a.ToString("0.0") + " 1%low=" + l.ToString("0.0") + " min=" + m.ToString("0.0") + "  at " + Screen.width + "x" + Screen.height);
            Stats(_castFrames, out a, out l, out m);
            Log("FPS [v0.5 replays / cinematics / shows] frames=" + _castFrames.Count + " avg=" + a.ToString("0.0") + " 1%low=" + l.ToString("0.0") + " min=" + m.ToString("0.0"));
            var sb = new StringBuilder();
            sb.Append("broadcast summary: plays " + plays + ", touchdowns " + tds + ", replays recorded " + _bd.Library.Count + " (" + (_bd.Library.TotalBytes / 1024) + " KB), auto replays " + _bd.AutoReplaysPlayed + ", shots " + _bd.ShotsPlayed);
            sb.Append(", crowd waves " + _bd.Crowd.WaveCount + ", flashes " + _bd.Crowd.FlashCount);
            if (_bd.Recorder.Samples > 0) sb.Append(", recorder " + (_bd.Recorder.TotalSampleMs / _bd.Recorder.Samples).ToString("0.000") + " ms/sample (" + _bd.Recorder.Samples + " samples)");
            Log(sb.ToString());
            foreach (var c in _bd.Library.Clips) Log("  clip " + c.Id + ": Q" + c.Quarter + " " + c.Title + " score " + c.Excitement.ToString("0") + " " + (c.Bytes / 1024) + " KB " + c.Frames.Count + " frames");
            if (_bd.Tracker != null)
            {
                var b = _bd.Tracker;
                Log("tracker: " + b.Drives.Count + " drives, " + b.Lines.Count + " player lines, home " + b.HomeBox.TotalYards + " yds / away " + b.AwayBox.TotalYards + " yds, plays " + b.Plays);
                foreach (var d in b.Drives) Log("  drive team " + d.TeamId + " Q" + d.Quarter + ": " + OverlayText.DriveHeadline(d) + " -> " + d.Result);
            }
            Log("v0.5 broadcast autotest done");
        }
    }
}
