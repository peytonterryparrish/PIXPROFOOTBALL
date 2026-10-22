using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.7.0 Track A verification: real PlayStage plays (the match views, graphs, plans, QB flow, ball follower) on the stadium scene.
    ///  1. QB film strips: a side camera that follows the quarterback, one contact sheet per play (stance, snap, drop, pocket, wind-up, release, scramble, hand-off ...) + four behind-the-QB frames.
    ///  2. Locomotion numbers over every running player of those plays: foot slide (horizontal speed of the planted foot, m/s), toe-out of both feet, hips bob.
    /// Run: Unity -projectPath ... -executeMethod PixFootball.EditorTools.AnimQbShots.Run   (batch mode WITHOUT -nographics).  Output: D:/PIXFootball/anim_qb/shots/*.png + anim_qb_metrics.txt
    /// Args: -pixAnimQbOnly text (play name filter), -pixAnimQbMetricsOnly 1
    /// </summary>
    public static class AnimQbShots
    {
        public const string Flag = "-pixAnimQb";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }
        public static string Arg(string name, string def)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }

        public static void Run()
        {
            BatchWatchdog.Arm(25);
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath);
            EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has) return;
            BatchWatchdog.Arm(25);
            new GameObject("PixAnimQb").AddComponent<AnimQbRunner>();
        }
    }

    public sealed class AnimQbRunner : MonoBehaviour
    {
        const string Out = "D:/PIXFootball/anim_qb/shots";
        Camera _cam; PlayStage _stage; Playbook _book; RuleSet _rules;
        readonly StringBuilder _report = new StringBuilder();
        int _playIndex;
        static readonly UniformSet KitA = Kit(TeamColors.Home, true), KitB = Kit(TeamColors.Away, false);
        static UniformSet Kit(TeamColors c, bool home) { var u = new UniformSet(); u.Uniform = c; u.Helmet = c; u.Home = home; return u; }

        // ---- locomotion metrics (all players of every play)
        sealed class Bin { public List<float[]> Frames = new List<float[]>(); public double LowSum; public int LowN; public double LowMin = 9; public int N, Fast; public double Slide; public double ToeL, ToeR; public int NL, NR; public double BobMin = 9, BobMax = -9; }
        readonly Dictionary<string, Bin> _bins = new Dictionary<string, Bin>();
        readonly Dictionary<int, Vector3[]> _prevFeet = new Dictionary<int, Vector3[]>();
        readonly Dictionary<int, Vector3[]> _prevToes = new Dictionary<int, Vector3[]>();

        IEnumerator Start()
        {
            Time.captureFramerate = 60;
            Directory.CreateDirectory(Out);
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception || t == LogType.Error) Debug.Log("[AnimQb] logged " + t + ": " + c); };
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) Destroy(pc.gameObject);
            foreach (var fr in FindObjectsByType<FreeRoamGame>(FindObjectsSortMode.None)) fr.enabled = false;
            foreach (var br in FindObjectsByType<Unity.Cinemachine.CinemachineBrain>(FindObjectsSortMode.None)) br.enabled = false;
            var camGo = new GameObject("AnimQbCam");
            _cam = camGo.AddComponent<Camera>(); var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            hdc.customRenderingSettings = true;
            { var fs = hdc.renderingPathCustomFrameSettings; var mk = hdc.renderingPathCustomFrameSettingsOverrideMask; foreach (var ff in new[] { FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField }) { mk.mask[(uint)ff] = true; fs.SetEnabled(ff, false); } hdc.renderingPathCustomFrameSettings = fs; hdc.renderingPathCustomFrameSettingsOverrideMask = mk; }
            _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 1500f; _cam.allowHDR = true; _cam.tag = "MainCamera";
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoStride") >= 0) BodyMotion.StrideMatchEnabled = false;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoToeClamp") >= 0) BodyMotion.ToeClampEnabled = false;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoFlow") >= 0) SimAgentView.DebugNoFlow = true;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoGround") >= 0) SimAgentView.DebugNoGroundFit = true;
            _book = DefaultPlaybooks.Create(); _rules = RuleSet.Pro();
            _stage = new GameObject("AnimQbStage").AddComponent<PlayStage>();
            _stage.Init();
            yield return null;
            string only = AnimQbShots.Arg("-pixAnimQbOnly", "");
            bool metricsOnly = AnimQbShots.Arg("-pixAnimQbMetricsOnly", "0") == "1";
            if (AnimQbShots.Arg("-pixAnimQbStances", "1") == "1") yield return StanceTest();
            if (AnimQbShots.Arg("-pixAnimQbTreadmill", "1") == "1") yield return Treadmill();
            var plays = AnimQbShots.Arg("-pixAnimQbPlays", "1") == "1" ? Plays() : new List<KeyValuePair<string, Play>>();
            foreach (var pl in plays)
            {
                if (only.Length > 0 && pl.Key.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) continue;
                yield return RunPlay(pl.Key, pl.Value, metricsOnly);
            }
            if (AnimQbShots.Arg("-pixAnimQbCatch", "1") == "1")
            {
                var qa = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Offense && (p.Assignments[0] as QbAssignment) != null && (p.Assignments[0] as QbAssignment).Shotgun && (p.Assignments[0] as QbAssignment).Action == QbAction.QuickPass);
                if (qa != null) yield return RunCatch("catch_quick", qa);
            }
            if (AnimQbShots.Arg("-pixAnimQbKicks", "1") == "1")
            {
                Func<string, Play> sp = n => _book.AllPlays.First(p => p.Side == PlaySide.Special && p.Name == n);
                yield return RunKick("kickoff", sp("Kickoff Deep Middle"), sp("Kick Return Middle Wedge"), new GameState(_rules, Team.Away));
                yield return RunKick("fieldgoal", sp("Field Goal"), sp("Field Goal Block Middle"), Scrim(80, 4, 5));
            }
            WriteReport();
            Debug.Log("[AnimQb] done");
            yield return null;
            EditorApplication.Exit(0);
        }

        List<KeyValuePair<string, Play>> Plays()
        {
            var list = new List<KeyValuePair<string, Play>>();
            Func<Play, QbAssignment> qa = p => p.Assignments[0] as QbAssignment;
            var offense = _book.AllPlays.Where(p => p.Side == PlaySide.Offense && qa(p) != null).ToList();
            Action<string, Func<Play, bool>> add = (name, f) => { var p = offense.FirstOrDefault(f); if (p != null) list.Add(new KeyValuePair<string, Play>(name, p)); };
            add("drop5_undercenter", p => qa(p).Action == QbAction.DropbackPass && !qa(p).Shotgun && qa(p).DropbackSteps == 5 && p.Category != PlayCategory.Kneel);
            add("drop3_undercenter", p => qa(p).Action == QbAction.DropbackPass && !qa(p).Shotgun && qa(p).DropbackSteps == 3);
            add("drop7_undercenter", p => qa(p).Action == QbAction.DropbackPass && !qa(p).Shotgun && qa(p).DropbackSteps >= 7);
            add("drop_shotgun", p => (qa(p).Action == QbAction.DropbackPass || qa(p).Action == QbAction.QuickPass) && qa(p).Shotgun);
            add("handoff", p => qa(p).Action == QbAction.Handoff && !qa(p).Shotgun);
            add("handoff_shotgun", p => qa(p).Action == QbAction.Handoff && qa(p).Shotgun);
            add("playaction", p => qa(p).Action == QbAction.PlayActionFake);
            add("rollout", p => qa(p).Action == QbAction.Rollout || qa(p).Action == QbAction.Boot);
            return list;
        }

        GameState Scrim(int spot, int down, int dist)
        {
            var gs = new GameState(_rules); gs.Phase = GamePhase.Scrimmage; gs.BallSpot = spot; gs.Down = down; gs.Distance = dist; gs.Possession = Team.Home;
            return gs;
        }

        IEnumerator RunPlay(string name, Play off, bool metricsOnly)
        {
            var def = _book.AllPlays.First(p => p.Side == PlaySide.Defense && p.FormationName == _book.Formations.First(f => f.Side == PlaySide.Defense).Name);
            var sim = new PlaySimulation();
            sim.Setup(Scrim(35, 1, 10), off, def, null, null, _rules, new Rng(77), new PlaySimOptions { Playbook = _book, Penalties = false, AutoSnap = false, AutoSnapDelay = 99f, QbWindupSeconds = 0.42f });
            var frame = new PlayFrame(true);
            _stage.Begin(sim, frame, KitA, KitB, ++_playIndex);
            foreach (var v in _stage.Views)
            {
                if (v == null || v.Avatar == null || v.Avatar.Animator == null) continue;
                v.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var smr in v.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            }
            const float dt = 1f / 60f;
            for (int f = 0; f < 90; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }
            float[] marks = { -0.3f, 0.0f, 0.2f, 0.4f, 0.6f, 0.8f, 1.0f, 1.3f, 1.6f, 2.0f, 2.4f, 2.8f };
            var tiles = new List<Texture2D>(); var back = new List<Texture2D>();
            float[] backMarks = { -0.3f, 0.6f, 1.4f, 2.2f };
            int mi = 0, bi = 0; float t = -0.3f; bool snapped = false; int frames = 0;
            // pre-snap -0.3 s is the settled stance
            if (!metricsOnly) { yield return null; tiles.Add(Shot(sim, frame, 0, 640, 480, true)); mi = 1; back.Add(Shot(sim, frame, 0, 960, 540, false)); bi = 1; }
            sim.Tick(dt, new ControlInput { Snap = true }, -1); _stage.Present(dt); snapped = true; t = 0f;
            while (sim.Phase != PlayPhase.Dead && t < 4.2f)
            {
                sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); t += dt; frames++;
                yield return null;
                Measure(sim);
                if (frames % 12 == 0) { var qa = sim.Agents[0]; _report.AppendLine("   [" + name + " t=" + t.ToString("0.00") + "] qb " + qa.Action + " " + qa.QbState + " v=" + qa.Speed.ToString("0.0") + " : " + (_stage.Views[0] != null ? _stage.Views[0].DebugFlow : "")); }
                if (!metricsOnly)
                {
                    if (mi < marks.Length && t >= marks[mi]) { tiles.Add(Shot(sim, frame, 0, 640, 480, true)); mi++; }
                    if (bi < backMarks.Length && t >= backMarks[bi]) { back.Add(Shot(sim, frame, 0, 960, 540, false)); bi++; }
                }
            }
            if (!metricsOnly)
            {
                Sheet(tiles, 4, Out + "/qb_" + name + "_side.png");
                Sheet(back, 2, Out + "/qb_" + name + "_behind.png");
            }
            if (sim.Phase == PlayPhase.Dead) for (int f = 0; f < 150; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }      // the broadcast keeps showing the players for a couple of seconds after the whistle
            // tackle scan: every player the simulation has down when the play ends - is the view lying, still contact driven, or standing up while he is down?
            for (int i = 0; i < 22; i++)
            {
                var a = sim.Agents[i]; var v = _stage.Views[i]; if (v == null || !(a.IsDown || a.Action == AgentAction.Tackled || a.Action == AgentAction.Fallen)) continue;
                var hp = v.Avatar != null && v.Avatar.Animator != null ? v.Avatar.Animator.GetBoneTransform(HumanBodyBones.Hips) : null;
                float hy = hp != null ? hp.position.y - v.transform.position.y : -1f;
                _report.AppendLine("   [tackle scan " + name + "] agent " + i + " " + a.PlayPos + " sim " + a.Action + " down=" + a.IsDown + " view hipsY=" + hy.ToString("0.00") + " contactDriven=" + v.ContactDriven + " clip=" + (v.Rig != null ? v.Rig.CurrentClipName : "") + (hy > 0.75f ? "   <-- STANDING WHILE DOWN" : ""));
            }
            _report.AppendLine("play " + name + ": " + off.Name + " ended " + sim.EndReason + " at " + t.ToString("0.0") + " s");
            string log = string.Join(" | ", sim.Log.Events.Take(14).Select(e => e.ToString()));
            _report.AppendLine("   events: " + log);
            _stage.End();
            yield return null;
        }

        /// <summary>Follows the intended receiver from the release to the end of the catch (a tile every 0.1 s): hands together on the catch frame, the ball in a hand.</summary>
        IEnumerator RunCatch(string name, Play off)
        {
            var def = _book.AllPlays.First(p => p.Side == PlaySide.Defense && p.FormationName == _book.Formations.First(f => f.Side == PlaySide.Defense).Name);
            var sim = new PlaySimulation();
            sim.Setup(Scrim(35, 1, 10), off, def, null, null, _rules, new Rng(78), new PlaySimOptions { Playbook = _book, Penalties = false, AutoSnap = false, AutoSnapDelay = 99f, QbWindupSeconds = 0.42f });
            var frame = new PlayFrame(true);
            _stage.Begin(sim, frame, KitA, KitB, ++_playIndex);
            foreach (var v in _stage.Views)
            {
                if (v == null || v.Avatar == null || v.Avatar.Animator == null) continue;
                v.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var smr in v.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            }
            const float dt = 1f / 60f;
            for (int f = 0; f < 90; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }
            sim.Tick(dt, new ControlInput { Snap = true }, -1); _stage.Present(dt);
            var tiles = new List<Texture2D>(); int target = -1; float tRelease = -1f, tCatch = -1f, nextShot = 0f;
            while (sim.Phase != PlayPhase.Dead && sim.Time < 5f && tiles.Count < 12)
            {
                sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt);
                yield return null;
                if (target < 0 && sim.Log.Has(SimEventType.Throw)) { var e = sim.Log.First(SimEventType.Throw).Value; target = e.AgentB; tRelease = sim.Time; nextShot = sim.Time + 0.15f; }
                if (tCatch < 0f && sim.Log.Has(SimEventType.Catch)) tCatch = sim.Time;
                if (target >= 0 && sim.Time >= nextShot) { tiles.Add(Shot(sim, frame, target, 640, 480, true)); nextShot += 0.1f; _report.AppendLine("   [" + name + " t=" + (sim.Time - tRelease).ToString("0.00") + " after release] receiver " + sim.Agents[target].Action + " : " + _stage.Views[target].DebugFlow); }
                if (tCatch >= 0f && sim.Time > tCatch + 0.5f) break;
            }
            Sheet(tiles, 4, Out + "/" + name + "_side.png");
            _report.AppendLine("catch film " + name + ": release at " + tRelease.ToString("0.00") + ", catch event at " + tCatch.ToString("0.00") + ", ended " + sim.EndReason);
            _stage.End();
            yield return null;
        }

        IEnumerator RunKick(string name, Play off, Play def, GameState gs)
        {
            var sim = new PlaySimulation();
            sim.Setup(gs, off, def, null, null, _rules, new Rng(77), new PlaySimOptions { Playbook = _book, Penalties = false, AutoSnap = false, AutoSnapDelay = 99f });
            var frame = new PlayFrame(true);
            _stage.Begin(sim, frame, KitA, KitB, ++_playIndex);
            foreach (var v in _stage.Views)
            {
                if (v == null || v.Avatar == null || v.Avatar.Animator == null) continue;
                v.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var smr in v.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            }
            int kicker = 0; for (int i = 0; i < 22; i++) if (sim.Agents[i].PlayPos == Position.K) { kicker = i; break; }
            const float dt = 1f / 60f;
            for (int f = 0; f < 90; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }
            var tiles = new List<Texture2D>(); float t = -0.3f; int idx = 0; float nextShot = 0f; int frames = 0; float contactSeen = -1f;
            tiles.Add(Shot(sim, frame, kicker, 640, 480, true));
            sim.Tick(dt, new ControlInput { Snap = true }, -1); _stage.Present(dt); t = 0f;
            _report.AppendLine("kick " + name + ": contact planned at sim time " + sim.KickContactTime.ToString("0.00"));
            while (sim.Phase != PlayPhase.Dead && t < 4.2f && tiles.Count < 12)
            {
                sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); t += dt; frames++;
                yield return null;
                if (contactSeen < 0f && sim.Log.Has(SimEventType.Kick)) { contactSeen = sim.Time; _report.AppendLine("   Kick event at sim time " + sim.Time.ToString("0.00") + " ; kicker view: " + _stage.Views[kicker].DebugFlow); tiles.Add(Shot(sim, frame, kicker, 640, 480, true)); }
                if (t >= nextShot) { tiles.Add(Shot(sim, frame, kicker, 640, 480, true)); nextShot += 0.3f; }
            }
            Sheet(tiles, 4, Out + "/kick_" + name + "_side.png");
            _report.AppendLine("kick " + name + " ended " + sim.EndReason);
            _stage.End();
            yield return null;
        }

        // ------------------------------------------------------------------------------------------ camera
        Texture2D Shot(PlaySimulation sim, PlayFrame frame, int agent, int w, int h, bool side)
        {
            var v = _stage.Views[agent];
            Vector3 q = v.transform.position;
            Vector3 d = frame.DirToUnity(new Vec2(0f, 1f)), r = Vector3.Cross(Vector3.up, d);
            if (side)
            {
                _cam.transform.position = q + d * 2.4f - r * 2.6f + Vector3.up * 1.45f; _cam.transform.rotation = Quaternion.LookRotation(q + Vector3.up * 1.15f - _cam.transform.position); _cam.fieldOfView = 36f;
            }
            else
            {
                var c = new UserCameraInput { ViewDir = d, Los = frame.ToUnity(new Vec2(sim.BallX0, sim.LosY)), PreSnap = sim.Phase == PlayPhase.PreSnap };
                var pose = UserCameraMath.Evaluate(c, UserCameraPreset.Classic(), 0f, 0f, 0f, 1f);
                _cam.transform.SetPositionAndRotation(pose.Position, pose.Rotation); _cam.fieldOfView = pose.Fov;
                // a tighter look at the quarterback: the camera slides to just behind him
                _cam.transform.position = q - d * 7.5f + Vector3.up * 3.2f + r * 0.6f; _cam.transform.rotation = Quaternion.LookRotation(q + d * 3f + Vector3.up * 0.9f - _cam.transform.position); _cam.fieldOfView = 38f;
            }
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _cam.targetTexture = rt; _cam.Render(); _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; _cam.targetTexture = null; rt.Release(); Destroy(rt);
            return tex;
        }

        Texture2D ShotCam(int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _cam.targetTexture = rt; _cam.Render(); _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; _cam.targetTexture = null; rt.Release(); Destroy(rt);
            return tex;
        }

        static void Sheet(List<Texture2D> tiles, int cols, string path)
        {
            if (tiles.Count == 0) return;
            int w = tiles[0].width, h = tiles[0].height, rows = (tiles.Count + cols - 1) / cols;
            var tex = new Texture2D(w * cols, h * rows, TextureFormat.RGB24, false);
            for (int i = 0; i < tiles.Count; i++) { int cx = i % cols, cy = rows - 1 - i / cols; tex.SetPixels(cx * w, cy * h, w, h, tiles[i].GetPixels()); Destroy(tiles[i]); }
            File.WriteAllBytes(path, tex.EncodeToPNG()); Destroy(tex);
        }

        // ------------------------------------------------------------------------------------------ metrics
        void Measure(PlaySimulation sim)
        {
            for (int i = 0; i < 22; i++)
            {
                var a = sim.Agents[i]; var v = _stage.Views[i];
                if (v == null || v.ContactDriven || a.IsDown) continue;
                float speed = a.Speed * 0.9144f;
                Bin b = null;
                if (speed > 2.5f && !(v.Rig != null && v.Rig.Weight > 0.3f))
                {
                    string bin = speed < 4.5f ? "jog 2.5-4.5" : (speed < 6.5f ? "run 4.5-6.5" : "sprint >6.5");
                    bin = (a.HasBall ? "carry " : "free ") + a.Action + " " + bin;
                    if (!_bins.TryGetValue(bin, out b)) _bins[bin] = b = new Bin();
                }
                Sample(v, b, i);
            }
        }

        /// <summary>One frame of foot metrics for a view (null bin = only keeps the previous feet).</summary>
        void Sample(SimAgentView v, Bin b, int key)
        {
            if (v == null || v.Avatar == null || v.Avatar.Animator == null) return;
            var an = v.Avatar.Animator;
            Transform lf = an.GetBoneTransform(HumanBodyBones.LeftFoot), rf = an.GetBoneTransform(HumanBodyBones.RightFoot), lt = an.GetBoneTransform(HumanBodyBones.LeftToes), rt = an.GetBoneTransform(HumanBodyBones.RightToes), hips = an.GetBoneTransform(HumanBodyBones.Hips);
            if (lf == null || rf == null) return;
            Vector3[] prev;
            var cur = new[] { lf.position, rf.position };
            if (b != null && _prevFeet.TryGetValue(key, out prev))
            {
                float gy = v.transform.position.y;
                float ll = lf.position.y - gy, rr = rf.position.y - gy;
                int low = ll <= rr ? 0 : 1;
                float lowY = Mathf.Min(ll, rr);
                b.LowSum += lowY; b.LowN++; b.LowMin = Math.Min(b.LowMin, lowY);
                {
                    float la = float.NaN, ra = float.NaN; Vector3 fw = v.Forward;
                    if (lt != null) { Vector3 t = lt.position - lf.position; t.y = 0f; if (t.magnitude > 0.05f) la = -Vector3.SignedAngle(fw, t, Vector3.up); }
                    if (rt != null) { Vector3 t = rt.position - rf.position; t.y = 0f; if (t.magnitude > 0.05f) ra = Vector3.SignedAngle(fw, t, Vector3.up); }
                    float lty = lt != null ? lt.position.y - gy : 9f, rty = rt != null ? rt.position.y - gy : 9f;
                    Vector3 ltp = lt != null ? lt.position : cur[0], rtp = rt != null ? rt.position : cur[1];
                    float ltv = 0f, rtv = 0f;
                    if (_prevToes.TryGetValue(key, out var pt)) { Vector3 dl = ltp - pt[0]; dl.y = 0f; Vector3 dr = rtp - pt[1]; dr.y = 0f; ltv = dl.magnitude * 60f; rtv = dr.magnitude * 60f; }
                    b.Frames.Add(new[] { ll, rr, (cur[0] - prev[0]).magnitude * 60f, (cur[1] - prev[1]).magnitude * 60f, la, ra, lty, rty, ltv, rtv });
                }
                if (_trace != null) _trace.AppendLine(_traceFrame++ + "," + (hips != null ? (hips.position.y - gy).ToString("0.000") : "") + "," + ll.ToString("0.000") + "," + rr.ToString("0.000") + "," + ((cur[0] - prev[0]).magnitude * 60f).ToString("0.00") + "," + ((cur[1] - prev[1]).magnitude * 60f).ToString("0.00") + "," + (v.Rig != null ? v.Rig.Weight.ToString("0.00") : "") + "," + (v.Rig != null ? v.Rig.CurrentClipName : "") + "," + (v.Motion != null ? v.Motion.LocoTrim.ToString("0.00") + "," + v.Motion.SlideError.ToString("0.00") + "," + v.Motion.ToeCorrectionLeft.ToString("0.0") + "," + v.Motion.ToeCorrectionRight.ToString("0.0") + "," + v.Motion.ToeOutAfterLeft.ToString("0") + "," + v.Motion.ToeOutAfterRight.ToString("0") : ""));
                if (lowY < 0.13f)
                {
                    Vector3 d = cur[low] - prev[low]; d.y = 0f;
                    float s = d.magnitude * 60f;
                    b.Slide += s; b.N++; if (s > 1.2f) b.Fast++;
                }
                Vector3 fwd = v.Forward;
                if (lt != null && ll < 0.14f) { Vector3 t = lt.position - lf.position; t.y = 0f; if (t.magnitude > 0.05f) { b.ToeL += -Vector3.SignedAngle(fwd, t, Vector3.up); b.NL++; } }
                if (rt != null && rr < 0.14f) { Vector3 t = rt.position - rf.position; t.y = 0f; if (t.magnitude > 0.05f) { b.ToeR += Vector3.SignedAngle(fwd, t, Vector3.up); b.NR++; } }
                if (hips != null) { b.BobMin = Math.Min(b.BobMin, hips.position.y - gy); b.BobMax = Math.Max(b.BobMax, hips.position.y - gy); }
            }
            _prevFeet[key] = cur;
            _prevToes[key] = new[] { lt != null ? lt.position : cur[0], rt != null ? rt.position : cur[1] };
        }

        // ------------------------------------------------------------------------------------------ treadmill
        // ------------------------------------------------------------------------------------------ stances (pose numbers)
        string _stances = "";

        IEnumerator StanceTest()
        {
            var root = new GameObject("Stances").transform;
            var cases = new[]
            {
                new { K = StanceKind.CenterSnap, R = PlayerRole.OL, S = PlayerSize.Large, M = false }, new { K = StanceKind.ThreePoint, R = PlayerRole.OL, S = PlayerSize.Large, M = false }, new { K = StanceKind.ThreePoint, R = PlayerRole.OL, S = PlayerSize.Large, M = true },
                new { K = StanceKind.ThreePoint, R = PlayerRole.DL, S = PlayerSize.Large, M = false }, new { K = StanceKind.FourPoint, R = PlayerRole.DL, S = PlayerSize.Large, M = false }, new { K = StanceKind.TwoPointLine, R = PlayerRole.DL, S = PlayerSize.Large, M = false },
                new { K = StanceKind.ThreePoint, R = PlayerRole.TE, S = PlayerSize.Medium, M = false }, new { K = StanceKind.QbUnderCenter, R = PlayerRole.QB, S = PlayerSize.Medium, M = false }, new { K = StanceKind.QbShotgun, R = PlayerRole.QB, S = PlayerSize.Medium, M = false },
                new { K = StanceKind.ReceiverSplit, R = PlayerRole.WR, S = PlayerSize.Small, M = false }, new { K = StanceKind.ReceiverSplit, R = PlayerRole.WR, S = PlayerSize.Small, M = true }, new { K = StanceKind.ReceiverSlot, R = PlayerRole.WR, S = PlayerSize.Small, M = false },
                new { K = StanceKind.CornerPress, R = PlayerRole.DB, S = PlayerSize.Small, M = false }, new { K = StanceKind.CornerOff, R = PlayerRole.DB, S = PlayerSize.Small, M = false }, new { K = StanceKind.SafetyDeep, R = PlayerRole.DB, S = PlayerSize.Small, M = false },
                new { K = StanceKind.LinebackerTwoPoint, R = PlayerRole.LB, S = PlayerSize.Medium, M = false }, new { K = StanceKind.Holder, R = PlayerRole.QB, S = PlayerSize.Medium, M = false }, new { K = StanceKind.BackTwoPoint, R = PlayerRole.RB, S = PlayerSize.Medium, M = false },
            };
            var stanceTiles = new List<Texture2D>();
            var sb = new StringBuilder();
            sb.AppendLine("## stances in the match view (photoreal body of the role; ankle y of an idle stand = 0.117 m, so 'lowest ankle' above ~0.14 means the feet hover; hips y; chest lean = degrees of the hips-chest line from vertical)");
            sb.AppendLine("| stance | role | mirror | clip | lowest ankle y | lowest toe y | hips y | chest lean | head y | ground offset |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var c in cases)
            {
                var view = SimAgentView.CreateForMatch(root, c.S, "St");
                view.Role = c.R; view.AgentId = 5; view.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                view.ResetForPlay(Vector3.zero, 0f);
                view.Stance = new StanceSpec { Kind = c.K, Mirror = c.M, IsLeftOfBall = c.M };
                yield return null; yield return null;
                var h = new AnimHints { Seed = 5, Side = 'C' };
                float lowA = 9f, lowT = 9f, hips = 0f, lean = 0f, head = 0f; int n = 0;
                for (int f = 0; f < 80; f++)
                {
                    view.ApplyMatch(Vector3.zero, Vector3.zero, Vector3.forward, AgentAction.Stance, h, 0f, false, false, 1f / 60f);
                    yield return null;
                    if (f < 30) continue;
                    var an = view.Avatar.Animator;
                    Transform lf = an.GetBoneTransform(HumanBodyBones.LeftFoot), rf = an.GetBoneTransform(HumanBodyBones.RightFoot), lt = an.GetBoneTransform(HumanBodyBones.LeftToes), rt = an.GetBoneTransform(HumanBodyBones.RightToes), hp = an.GetBoneTransform(HumanBodyBones.Hips), ch = an.GetBoneTransform(HumanBodyBones.Chest), hd = an.GetBoneTransform(HumanBodyBones.Head);
                    lowA = Mathf.Min(lowA, Mathf.Min(lf.position.y, rf.position.y)); if (lt != null && rt != null) lowT = Mathf.Min(lowT, Mathf.Min(lt.position.y, rt.position.y));
                    hips += hp.position.y; lean += Vector3.Angle(Vector3.up, ch.position - hp.position); head += hd.position.y; n++;
                }
                { var cp = new Vector3(2.6f, 1.0f, 1.2f); _cam.transform.position = cp; _cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0.8f, 0f) - cp); _cam.fieldOfView = 34f; stanceTiles.Add(ShotCam(480, 560)); }
                sb.AppendLine("| " + c.K + " | " + c.R + " | " + (c.M ? "yes" : "no") + " | " + (view.Rig != null ? view.Rig.CurrentClipName : "") + " | " + lowA.ToString("0.000") + " | " + lowT.ToString("0.000") + " | " + (hips / n).ToString("0.00") + " | " + (lean / n).ToString("0") + " | " + (head / n).ToString("0.00") + " | " + view.GroundOffset.ToString("0.000") + " |");
                Destroy(view.gameObject); yield return null;
            }
            Sheet(stanceTiles, 6, Out + "/stances_side.png");
            _stances = sb.ToString();
            Destroy(root.gameObject);
        }

        struct Gait { public string Name; public Vector3 Dir; public float Speed; public bool Carry; public AgentAction Action; }

        IEnumerator Treadmill()
        {
            var root = new GameObject("Treadmill").transform;
            var cases = new List<Gait>();
            cases.Add(new Gait { Name = "idle", Dir = Vector3.forward, Speed = 0f, Action = AgentAction.Idle });
            foreach (float s in new[] { 1.5f, 2.5f, 3.5f, 4.5f, 5.5f, 6.5f, 7.5f }) cases.Add(new Gait { Name = "free forward", Dir = Vector3.forward, Speed = s, Action = s > 6.5f ? AgentAction.Sprint : AgentAction.Run });
            foreach (float s in new[] { 2.5f, 4.5f, 6.5f, 8f }) cases.Add(new Gait { Name = "carry forward", Dir = Vector3.forward, Speed = s, Carry = true, Action = AgentAction.Carry });
            foreach (float s in new[] { 1.5f, 2.5f, 3.5f, 4.5f }) cases.Add(new Gait { Name = "free backpedal", Dir = Vector3.back, Speed = s, Action = AgentAction.Backpedal });
            foreach (float s in new[] { 2f, 3f }) cases.Add(new Gait { Name = "free shuffle", Dir = Vector3.right, Speed = s, Action = AgentAction.Shuffle });
            var sb = new StringBuilder();
            sb.AppendLine("## treadmill (a real match view driven at constant speed after a 7 m/s^2 ramp; metrics over the last 2.3 s)");
            sb.AppendLine("| gait | m/s | ankle speed at contact | ball-of-foot (toe) speed at contact | ankle contacts > 1.2 | toe-out L | toe-out R | hips y min..max | lowest ankle y mean (min) |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            int k = 100;
            bool strideWas = BodyMotion.StrideMatchEnabled;
            string tmFilter = AnimQbShots.Arg("-pixAnimQbTreadmillOnly", "");
            foreach (float mult in new[] { 1f, 0.8f, 1.25f, 1.6f })
            foreach (var g in cases)
            {
                LocomotionAnimator.DebugTrim = mult;
                if (tmFilter.Length > 0 && g.Name.IndexOf(tmFilter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var view = SimAgentView.CreateForMatch(root, PlayerSize.Medium, "Tm");
                view.Role = PlayerRole.WR; view.AgentId = 3; view.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                view.ResetForPlay(Vector3.zero, 0f);
                yield return null; yield return null;
                Vector3 pos = Vector3.zero; float sp = 0f; const float dt = 1f / 60f;
                var h = new AnimHints { Seed = 5, Side = 'C', Carrying = g.Carry };
                var bin = new Bin();
                bool trace = g.Name == "idle" || (g.Name == "free forward" && g.Speed == 5.5f) || (g.Name == "free backpedal" && g.Speed == 2.5f) || (g.Name == "carry forward" && g.Speed == 4.5f);
                _trace = trace ? new StringBuilder("frame,hipsY,lAnkleY,rAnkleY,lFootSpeed,rFootSpeed,rigW,clip,trim,slideErr,toeCorrL,toeCorrR,toeAfterL,toeAfterR\n") : null; _traceFrame = 0;
                for (int f = 0; f < 390; f++)
                {
                    sp = Mathf.MoveTowards(sp, g.Speed, 7f * dt);
                    Vector3 vel = g.Dir * sp; pos += vel * dt;
                    view.ApplyMatch(pos, vel, Vector3.forward, g.Action, h, 0f, false, false, dt);
                    yield return null;
                    Sample(view, f > 150 ? bin : null, k);
                }
                sb.AppendLine(TreadmillRow(g.Name + " [x" + mult.ToString("0.00") + "]", g.Speed, bin));
                if (_trace != null) { File.WriteAllText("D:/PIXFootball/anim_qb/trace_" + g.Name.Replace(" ", "_") + "_x" + mult.ToString("0.00") + "_" + g.Speed.ToString("0.0") + ".csv", _trace.ToString()); _trace = null; }
                Destroy(view.gameObject); yield return null;
            }
            LocomotionAnimator.DebugTrim = 1f; BodyMotion.StrideMatchEnabled = strideWas;
            _treadmill = sb.ToString();
            Destroy(root.gameObject);
        }
        static string TreadmillRow(string name, float speed, Bin b)
        {
            float minL = 9f, minR = 9f; foreach (var f in b.Frames) { minL = Mathf.Min(minL, f[0]); minR = Mathf.Min(minR, f[1]); }
            // one sample per ground contact: the slowest the foot gets while it is within 3 cm of its lowest point (0 = planted on the turf)
            double sum = 0; int n = 0, fast = 0; double tl = 0, tr = 0; int ntl = 0, ntr = 0;
            for (int foot = 0; foot < 2; foot++)
            {
                float min = foot == 0 ? minL : minR; bool inContact = false; float best = 99f; float ang = float.NaN;
                for (int i = 0; i <= b.Frames.Count; i++)
                {
                    bool low = i < b.Frames.Count && b.Frames[i][foot] < min + 0.03f;
                    if (low) { var f = b.Frames[i]; if (f[2 + foot] < best) { best = f[2 + foot]; ang = f[4 + foot]; } inContact = true; }
                    else if (inContact) { sum += best; n++; if (best > 1.2f) fast++; if (!float.IsNaN(ang)) { if (foot == 0) { tl += ang; ntl++; } else { tr += ang; ntr++; } } inContact = false; best = 99f; ang = float.NaN; }
                }
            }
            double tsum = 0; int tn = 0, tfast = 0;
            for (int foot = 0; foot < 2; foot++)
            {
                float tmin = 9f; foreach (var f in b.Frames) tmin = Mathf.Min(tmin, f[6 + foot]);
                bool inC = false; float best = 99f;
                for (int i = 0; i <= b.Frames.Count; i++)
                {
                    bool low = i < b.Frames.Count && b.Frames[i][6 + foot] < tmin + 0.025f;
                    if (low) { best = Mathf.Min(best, b.Frames[i][8 + foot]); inC = true; }
                    else if (inC) { tsum += best; tn++; if (best > 1.2f) tfast++; inC = false; best = 99f; }
                }
            }
            string toeCol = tn > 0 ? (tsum / tn).ToString("0.00") + " (" + (100.0 * tfast / tn).ToString("0") + "% > 1.2)" : "-";
            return "| " + name + " | " + speed.ToString("0.0") + " | " + (n > 0 ? (sum / n).ToString("0.00") : "-") + " | " + toeCol + " | " + (n > 0 ? (100.0 * fast / n).ToString("0") + "%" : "-") + " | " + (ntl > 0 ? (tl / ntl).ToString("0") : "-") + " | " + (ntr > 0 ? (tr / ntr).ToString("0") : "-") + " | " + b.BobMin.ToString("0.00") + ".." + b.BobMax.ToString("0.00") + " | " + (b.LowN > 0 ? (b.LowSum / b.LowN).ToString("0.000") + " (" + b.LowMin.ToString("0.000") + ")" : "-") + " |";
        }
        string _treadmill = "";
        StringBuilder _trace; int _traceFrame;

        void WriteReport()
        {
            var sb = new StringBuilder("# Animation QB / locomotion metrics (AnimQbShots)\n\n");
            sb.AppendLine("planted-foot slide = horizontal speed (m/s) of the lower foot while it is within 13 cm of the ground (0 = planted); toe-out in degrees (+ = away from the body line); hips bob = vertical range of the hips.");
            sb.AppendLine();
            sb.AppendLine("| gait | frames | mean planted-foot slide | frames with slide > 1.2 m/s | toe-out left | toe-out right | hips y min..max |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var kv in _bins.OrderBy(k => k.Key))
            {
                var b = kv.Value;
                sb.AppendLine("| " + kv.Key + " | " + b.N + " | " + (b.N > 0 ? (b.Slide / b.N).ToString("0.00") : "-") + " | " + (b.N > 0 ? (100.0 * b.Fast / b.N).ToString("0") + "%" : "-") + " | " + (b.NL > 0 ? (b.ToeL / b.NL).ToString("0") : "-") + " | " + (b.NR > 0 ? (b.ToeR / b.NR).ToString("0") : "-") + " | " + b.BobMin.ToString("0.00") + ".." + b.BobMax.ToString("0.00") + " |");
            }
            sb.AppendLine();
            sb.AppendLine(_stances);
            sb.AppendLine(_treadmill);
            sb.Append(_report);
            File.WriteAllText("D:/PIXFootball/anim_qb/anim_qb_metrics.txt", sb.ToString());
        }
    }
}
