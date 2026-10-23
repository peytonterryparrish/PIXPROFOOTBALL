using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    /// v0.10.0 CHOREO frame strips (QA only, <c>-pixChoreoShots</c>). Finds real plays that contain the interactions the choreography is about (a tackle from the front, the side and from behind, a catch
    /// in stride, a catch in traffic, a pass-rush engagement, a hand-off mesh, a pile-on with the get-up and the walk back) with dry runs, then renders each one through the real PlayStage as a
    /// 12-frame contact sheet from a side and a front / behind camera. Args: -pixChoreoShotsOut dir, -pixChoreoShotsChoreo 0|1, -pixChoreoShotsOnly name,name.
    /// Run: Unity -batchmode -projectPath ... -executeMethod PixFootball.EditorTools.ChoreoShots.Run -pixChoreoShots   (WITHOUT -nographics).
    /// </summary>
    public static class ChoreoShots
    {
        public const string Flag = "-pixChoreoShots";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }
        public static string Arg(string name, string def)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }
        public static void Run()
        {
            BatchWatchdog.Arm(60);
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath);
            EditorApplication.isPlaying = true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has) return;
            BatchWatchdog.Arm(60);
            new GameObject("PixChoreoShots").AddComponent<ChoreoShotsRunner>();
        }
    }

    public sealed class ChoreoShotsRunner : MonoBehaviour
    {
        const float Dt = 1f / 60f;
        string _out; bool _choreo;
        Camera _cam; PlayStage _stage; Playbook _book; RuleSet _rules;
        readonly StringBuilder _report = new StringBuilder();
        static readonly UniformSet KitA = Kit(TeamColors.Home, true), KitB = Kit(TeamColors.Away, false);
        static UniformSet Kit(TeamColors c, bool home) { var u = new UniformSet(); u.Uniform = c; u.Helmet = c; u.Home = home; return u; }
        int _playIndex;

        struct Find { public bool Ok; public Play Off, Def; public int Seed; public float T; public int A, B; public string Note; }

        IEnumerator Start()
        {
            Time.captureFramerate = 60;
            _out = ChoreoShots.Arg("-pixChoreoShotsOut", "D:/PIXFootball/anim_choreo/v0100/strips");
            _choreo = ChoreoShots.Arg("-pixChoreoShotsChoreo", "1") == "1";
            Directory.CreateDirectory(_out);
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) Destroy(pc.gameObject);
            foreach (var fr in FindObjectsByType<FreeRoamGame>(FindObjectsSortMode.None)) fr.enabled = false;
            foreach (var br in FindObjectsByType<Unity.Cinemachine.CinemachineBrain>(FindObjectsSortMode.None)) br.enabled = false;
            var camGo = new GameObject("ChoreoShotsCam");
            _cam = camGo.AddComponent<Camera>(); var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            hdc.customRenderingSettings = true;
            { var fs = hdc.renderingPathCustomFrameSettings; var mk = hdc.renderingPathCustomFrameSettingsOverrideMask; foreach (var ff in new[] { FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField }) { mk.mask[(uint)ff] = true; fs.SetEnabled(ff, false); } hdc.renderingPathCustomFrameSettings = fs; hdc.renderingPathCustomFrameSettingsOverrideMask = mk; }
            _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 1500f; _cam.allowHDR = true; _cam.tag = "MainCamera";
            _book = DefaultPlaybooks.Create(); _rules = RuleSet.Pro();
            _stage = new GameObject("ChoreoShotsStage").AddComponent<PlayStage>();
            _stage.Init();
            yield return null;
            string only = ChoreoShots.Arg("-pixChoreoShotsOnly", "");
            string[] onlies = only.Length > 0 ? only.Split(',') : null;
            var scenarios = new[] { "tackle_front", "tackle_side", "tackle_behind", "catch_stride", "catch_traffic", "pass_rush", "handoff", "pile_getup" };
            foreach (string scn in scenarios)
            {
                if (onlies != null && Array.IndexOf(onlies, scn) < 0) continue;
                Find f = Search(scn);
                if (!f.Ok) { _report.AppendLine(scn + ": no matching play found"); continue; }
                _report.AppendLine(scn + ": " + f.Off.Name + " vs " + f.Def.Name + " seed " + f.Seed + " event at " + f.T.ToString("0.00") + " s, agents " + f.A + "/" + f.B + " " + f.Note);
                yield return Render(scn, f, "side");
                yield return Render(scn, f, scn.StartsWith("tackle") || scn == "catch_stride" ? "behind" : "front");
            }
            File.WriteAllText(_out + "/shots_report.txt", _report.ToString());
            Debug.Log("[ChoreoShots] done");
            yield return null;
            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------------------------------ sim helpers
        PlaySimulation NewSim(Play off, Play def, int seed)
        {
            var sim = new PlaySimulation();
            var opt = new PlaySimOptions { Playbook = _book, Penalties = false, AutoSnap = false, AutoSnapDelay = 99f, QbWindupSeconds = 0.42f };
            ChoreoHooks.Configure(opt, _choreo);
            var gs = new GameState(_rules); gs.Phase = GamePhase.Scrimmage; gs.BallSpot = 35; gs.Down = 1; gs.Distance = 10; gs.Possession = Team.Home;
            sim.Setup(gs, off, def, null, null, _rules, new Rng(seed), opt);
            return sim;
        }

        Play DefFor(Play off, int salt)
        {
            var defs = _book.AllPlays.Where(p => p.Side == PlaySide.Defense).ToList();
            var zone = defs.Where(p => p.Category == PlayCategory.ZoneCoverage).ToList();
            var man = defs.Where(p => p.Category == PlayCategory.ManCoverage).ToList();
            int h = Mathf.Abs(off.Name.GetHashCode() + off.FormationName.GetHashCode() + salt);
            if (h % 3 == 0 && man.Count > 0) return man[h % man.Count];
            return zone[h % zone.Count];
        }

        Find Search(string scn)
        {
            var offense = _book.AllPlays.Where(p => p.Side == PlaySide.Offense && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike && p.Name != "Hail Mary").ToList();
            bool wantRun = scn.StartsWith("tackle") || scn == "handoff" || scn == "pile_getup";
            var pool = offense.Where(p => wantRun ? !p.IsPass : p.IsPass).ToList();
            if (pool.Count == 0) pool = offense;
            for (int k = 0; k < 220; k++)
            {
                var off = pool[k % pool.Count]; var def = DefFor(off, k / pool.Count);
                int seed = 1200 + k;
                var sim = NewSim(off, def, seed);
                for (int i = 0; i < 90; i++) sim.Tick(Dt, default(ControlInput), -1);
                sim.Tick(Dt, new ControlInput { Snap = true }, -1);
                int seen = 0; float engStart = -1f; int engA = -1, engB = -1; float engBest = 0f;
                int guard = 0;
                while (sim.Phase != PlayPhase.Dead && guard++ < 1500)
                {
                    sim.Tick(Dt, default(ControlInput), -1);
                    var ev = sim.Log.Events;
                    for (; seen < ev.Count; seen++)
                    {
                        var e = ev[seen];
                        if (scn.StartsWith("tackle") && e.Type == SimEventType.Tackle && e.Tackle != TackleType.Drag && e.AgentA >= 0 && e.AgentB >= 0)
                        {
                            var tk = sim.Agents[e.AgentA]; var cr = sim.Agents[e.AgentB];
                            if (cr.Slot == 0 && cr.Side == SimSide.Offense) continue;
                            if (e.Vel.Length < 2.5f) continue;
                            float b = Math.Abs(ChoreoMath.Bearing(tk.Pos, cr.Pos, ChoreoMath.YawOf(e.Vel)));
                            bool ok = scn == "tackle_front" ? b < 40f : (scn == "tackle_side" ? (b > 60f && b < 120f) : b > 140f);
                            if (ok) return new Find { Ok = true, Off = off, Def = def, Seed = seed, T = e.Time, A = e.AgentB, B = e.AgentA, Note = "bearing " + b.ToString("0") + " speed " + (e.Vel.Length * 0.9144f).ToString("0.0") + " m/s" };
                        }
                        if (scn == "catch_stride" && e.Type == SimEventType.Catch && e.AgentA >= 0 && e.AgentA < 11)
                        {
                            var r = sim.Agents[e.AgentA];
                            if (r.Vel.Length * 0.9144f > 3.2f) return new Find { Ok = true, Off = off, Def = def, Seed = seed, T = e.Time, A = e.AgentA, B = -1, Note = "receiver " + (r.Vel.Length * 0.9144f).ToString("0.0") + " m/s" };
                        }
                        if (scn == "catch_traffic" && e.Type == SimEventType.Catch && e.AgentA >= 0 && e.AgentA < 11)
                        {
                            var r = sim.Agents[e.AgentA]; int near = -1; float nd = 1.5f;
                            for (int j = 11; j < 22; j++) { float d = (sim.Agents[j].Pos - r.Pos).Length; if (d < nd) { nd = d; near = j; } }
                            if (near >= 0) return new Find { Ok = true, Off = off, Def = def, Seed = seed, T = e.Time, A = e.AgentA, B = near, Note = "defender " + nd.ToString("0.0") + " yd" };
                        }
                        if (scn == "handoff" && e.Type == SimEventType.Handoff && e.AgentA >= 0 && e.AgentB >= 0)
                            return new Find { Ok = true, Off = off, Def = def, Seed = seed, T = e.Time, A = e.AgentA, B = e.AgentB, Note = "" };
                        if (scn == "pile_getup" && e.Type == SimEventType.PileOn && e.AgentA >= 0 && e.AgentB >= 0)
                            return new Find { Ok = true, Off = off, Def = def, Seed = seed, T = e.Time, A = e.AgentB, B = e.AgentA, Note = "pile-on" };
                    }
                    if (scn == "pass_rush")
                    {
                        // the longest OL / DL engagement of the play
                        for (int i = 11; i < 22; i++)
                        {
                            var d = sim.Agents[i]; if (!d.IsDefensiveLineman || d.State != AgentState.Engaged || d.Engagement == null || d.Engagement.Primary == null) continue;
                            if (engStart < 0f || engB != d.Id) { engStart = sim.Time; engB = d.Id; engA = d.Engagement.Primary.Id; }
                            else if (sim.Time - engStart > engBest) { engBest = sim.Time - engStart; }
                        }
                        if (engBest > 1.6f && engB >= 0) return new Find { Ok = true, Off = off, Def = def, Seed = seed, T = engStart + engBest * 0.5f, A = engA, B = engB, Note = "engaged " + engBest.ToString("0.0") + " s" };
                    }
                }
            }
            return default(Find);
        }

        // ------------------------------------------------------------------------------------------ render
        static readonly float[] TackleMarks = { -0.9f, -0.55f, -0.3f, -0.12f, 0f, 0.15f, 0.35f, 0.65f, 1.0f, 1.6f, 2.4f, 3.2f };
        static readonly float[] CatchMarks = { -0.8f, -0.55f, -0.38f, -0.25f, -0.12f, 0f, 0.1f, 0.22f, 0.4f, 0.7f, 1.0f, 1.5f };
        static readonly float[] HandoffMarks = { -0.7f, -0.45f, -0.25f, -0.1f, 0f, 0.12f, 0.3f, 0.55f, 0.9f, 1.3f, 1.8f, 2.4f };
        static readonly float[] RushMarks = { -0.9f, -0.6f, -0.3f, 0f, 0.3f, 0.6f, 0.9f, 1.2f, 1.5f, 1.8f, 2.1f, 2.4f };
        static readonly float[] PileMarks = { -0.5f, -0.2f, 0f, 0.3f, 0.7f, 1.2f, 1.8f, 2.4f, 3.2f, 4.0f, 5.0f, 6.0f };

        IEnumerator Render(string scn, Find f, string camName)
        {
            var sim = NewSim(f.Off, f.Def, f.Seed);
            var frame = new PlayFrame(true);
            _stage.Begin(sim, frame, KitA, KitB, ++_playIndex);
            foreach (var v in _stage.Views) { if (v == null || v.Avatar == null || v.Avatar.Animator == null) continue; v.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; foreach (var smr in v.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true; }
            for (int i = 0; i < 90; i++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
            sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
            float[] marks = scn.StartsWith("tackle") ? TackleMarks : (scn.StartsWith("catch") ? CatchMarks : (scn == "handoff" ? HandoffMarks : (scn == "pass_rush" ? RushMarks : PileMarks)));
            var tiles = new List<Texture2D>(); int mi = 0; var idx = new StringBuilder(scn + " " + camName + ": ");
            float t = 0f; int guard = 0;
            while (mi < marks.Length && guard++ < 2400)
            {
                sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; t += Dt;
                while (mi < marks.Length && t >= f.T + marks[mi])
                {
                    tiles.Add(Shot(frame, f, camName, sim));
                    idx.Append((t - f.T).ToString("0.00") + "[" + ClipOf(f.A) + "|" + ClipOf(f.B) + "] ");
                    mi++;
                }
            }
            Sheet(tiles, 4, _out + "/" + scn + "_" + camName + ".png");
            _report.AppendLine("   " + idx);
            _stage.End();
            yield return null;
        }

        string ClipOf(int agent) { if (agent < 0) return "-"; var v = _stage.Views[agent]; return v != null && v.Rig != null ? (v.Rig.CurrentClipName.Length > 22 ? v.Rig.CurrentClipName.Substring(0, 22) : v.Rig.CurrentClipName) + (v.ContactDriven ? "*" : "") : ""; }

        Texture2D Shot(PlayFrame frame, Find f, string camName, PlaySimulation sim)
        {
            var va = _stage.Views[f.A]; var vb = f.B >= 0 ? _stage.Views[f.B] : null;
            Vector3 pa = va.transform.position, pb = vb != null ? vb.transform.position : pa;
            Vector3 mid = (pa + pb) * 0.5f;
            Vector3 head = frame.DirToUnity(sim.Agents[f.A].Vel.Length > 1.0f ? sim.Agents[f.A].Vel.Norm() : sim.Agents[f.A].Facing);
            if (scn_isEngage(f)) head = frame.DirToUnity(new Vec2(0f, 1f));
            Vector3 right = Vector3.Cross(Vector3.up, head);
            float dist = vb != null ? Mathf.Clamp((pa - pb).magnitude * 0.5f + 3.0f, 3.6f, 6.0f) : 3.8f;
            if (camName == "side") { _cam.transform.position = mid + right * dist + Vector3.up * 1.4f; _cam.transform.rotation = Quaternion.LookRotation(mid + Vector3.up * 0.9f - _cam.transform.position); _cam.fieldOfView = 44f; }
            else if (camName == "behind") { _cam.transform.position = mid - head * (dist + 0.5f) + right * 0.9f + Vector3.up * 1.9f; _cam.transform.rotation = Quaternion.LookRotation(mid + head * 1.2f + Vector3.up * 0.9f - _cam.transform.position); _cam.fieldOfView = 46f; }
            else { _cam.transform.position = mid + head * (dist + 0.3f) - right * 1.6f + Vector3.up * 1.7f; _cam.transform.rotation = Quaternion.LookRotation(mid + Vector3.up * 0.9f - _cam.transform.position); _cam.fieldOfView = 44f; }
            // only the two actors are in the frame (the other twenty players stood in front of the camera in the first strips)
            var hidden = new List<Renderer>();
            for (int j = 0; j < 22; j++) { var ov = _stage.Views[j]; if (ov == null || ov == va || ov == vb) continue; foreach (var rr in ov.GetComponentsInChildren<Renderer>()) if (rr.enabled) { rr.enabled = false; hidden.Add(rr); } }
            var shot = ShotCam(540, 400);
            foreach (var rr in hidden) rr.enabled = true;
            return shot;
        }
        static bool scn_isEngage(Find f) { return f.Note.StartsWith("engaged"); }

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

        void Sheet(List<Texture2D> tiles, int cols, string path)
        {
            if (tiles.Count == 0) return;
            int w = tiles[0].width, h = tiles[0].height, rows = (tiles.Count + cols - 1) / cols;
            var tex = new Texture2D(w * cols, h * rows, TextureFormat.RGB24, false);
            for (int i = 0; i < tiles.Count; i++) { int cx = i % cols, cy = rows - 1 - i / cols; tex.SetPixels(cx * w, cy * h, w, h, tiles[i].GetPixels()); Destroy(tiles[i]); }
            File.WriteAllBytes(path, tex.EncodeToPNG()); Destroy(tex);
        }
    }
}
