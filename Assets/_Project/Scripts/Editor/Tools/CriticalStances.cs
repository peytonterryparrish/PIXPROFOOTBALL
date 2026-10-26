using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    /// v0.11.0 CRITICAL: stance audit of every position in every situation (QA only, behind <c>-pixCriticalStances</c>). Runs each offensive and defensive formation of the playbook (against rotating opponents), every special-teams set
    /// (kickoff and return, onside, punt and return and block, field goal and block, extra point), the huddle break and the first second after the snap through PlayStage, and judges each of the 22 bodies against the stance
    /// reference (<c>Docs/QA/REFERENCE_Stances.md</c>): feet on the turf (+-2 cm, the lowest sole against the bind pose), no T / bind pose, no twisted trunk, no missing part, facing the right way, hands where the stance puts them,
    /// knees bent for the two-point stances. Writes <c>stance_check.csv</c> (one row per body and sample), <c>stance_summary.txt</c> (fails per stance kind and position) and a contact sheet of one tile per stance kind.
    /// Run: Unity -projectPath ... -executeMethod PixFootball.EditorTools.CriticalStances.Run -pixCriticalStances -pixCriticalOut dir   (batch mode WITHOUT -nographics)
    /// </summary>
    public static class CriticalStances
    {
        public const string Flag = "-pixCriticalStances";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }
        public static string Arg(string name, string def) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return def; }

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
            new GameObject("PixCriticalStances").AddComponent<CriticalStancesRunner>();
        }
    }

    public sealed class CriticalStancesRunner : MonoBehaviour
    {
        string _out; Camera _cam; PlayStage _stage; Playbook _book; RuleSet _rules;
        readonly StringBuilder _csv = new StringBuilder(), _rep = new StringBuilder();
        readonly Dictionary<string, int[]> _fails = new Dictionary<string, int[]>();   // kind|pos -> samples, then one counter per check
        readonly Dictionary<string, Texture2D> _tiles = new Dictionary<string, Texture2D>();
        static readonly string[] Checks = { "floating", "sunk", "tpose", "twisted", "missingPart", "facing", "hands", "straightLegs", "badScale", "offPlace" };
        static readonly UniformSet KitA = Kit(TeamColors.Home, true), KitB = Kit(TeamColors.Away, false);
        static UniformSet Kit(TeamColors c, bool home) { var u = new UniformSet(); u.Uniform = c; u.Helmet = c; u.Home = home; return u; }
        const float Dt = 1f / 60f;
        int _playIndex;
        Plane[] _planes = new Plane[6];
        int _samples, _bad, _curBody = -1;
        readonly Dictionary<int, int[]> _byBody = new Dictionary<int, int[]>();

        PlaySimulation NewSim(Play off, Play def, GameState gs, int seed)
        {
            var sim = new PlaySimulation();
            var opt = new PlaySimOptions { Playbook = _book, Penalties = false, AutoSnap = false, AutoSnapDelay = 99f, QbWindupSeconds = 0.42f };
            sim.Setup(gs, off, def, null, null, _rules, new Rng(seed), opt);
            return sim;
        }

        GameState Scrim(int spot, int down, int dist)
        {
            var gs = new GameState(_rules); gs.Phase = GamePhase.Scrimmage; gs.BallSpot = spot; gs.Down = down; gs.Distance = dist; gs.Possession = Team.Home;
            return gs;
        }

        void Prep()
        {
            if (CriticalStances.Arg("-pixCriticalNoPrep", "0") == "1") return;      // keep the match culling mode: bodies outside the picture are not re-posed
            foreach (var v in _stage.Views)
            {
                if (v == null || v.Avatar == null || v.Avatar.Animator == null) continue;
                v.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        IEnumerator Start()
        {
            Time.captureFramerate = 60;
            _out = CriticalStances.Arg("-pixCriticalOut", "D:/PIXFootball/Renders/critical_v0110/stances");
            Directory.CreateDirectory(_out);
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception) Debug.Log("[CriticalStances] exception: " + c + " :: " + (st.Length > 300 ? st.Substring(0, 300) : st)); };
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) Destroy(pc.gameObject);
            foreach (var fr in FindObjectsByType<FreeRoamGame>(FindObjectsSortMode.None)) fr.enabled = false;
            foreach (var br in FindObjectsByType<Unity.Cinemachine.CinemachineBrain>(FindObjectsSortMode.None)) br.enabled = false;
            var camGo = new GameObject("CriticalCam");
            _cam = camGo.AddComponent<Camera>(); var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 1500f; _cam.allowHDR = true; _cam.tag = "MainCamera";
            PlayerLod.ViewCamera = _cam;
            _book = DefaultPlaybooks.Create(); _rules = RuleSet.Pro();
            _stage = new GameObject("CriticalStage").AddComponent<PlayStage>();
            _stage.Init();
            yield return null;
            _csv.AppendLine("situation,sample,agent,side,pos,stanceKind,mirror,clip,hipsY,trunkLean,kneeFlexMin,handsDown,soleGap,yawErr,twist,lod,fails");
            var jobs = BuildJobs();
            _rep.AppendLine("jobs: " + jobs.Count);
            foreach (var j in jobs)
            {
                PlaySimulation sim = null;
                try { sim = NewSim(j.Off, j.Def, j.State, 91); } catch (Exception e) { _rep.AppendLine("could not set up " + j.Name + ": " + e.Message); continue; }
                BodySelect.ForceBody = j.Body; _curBody = j.Body;
                _stage.NextBeginHuddle = j.Huddle;
                _stage.Begin(sim, new PlayFrame(true), KitA, KitB, ++_playIndex);
                Prep();
                // huddle: sample while they walk, then when settled
                if (j.Huddle)
                {
                    for (int f = 0; f < 40; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                    Sample(j.Name + " huddle", sim, false);
                }
                int guard = 0;
                for (int f = 0; f < 100; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                while (_stage.BreakInProgress && guard++ < 480) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                for (int f = 0; f < 20; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                Sample(j.Name + " set", sim, true);
                if (j.Snap)
                {
                    sim.Tick(Dt, new ControlInput { Snap = true }, -1);
                    for (int f = 0; f < 150; f++)
                    {
                        sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null;
                        if (f == 8 || f == 20 || f == 35 || f == 60 || f == 100 || f == 149) Sample(j.Name + " snap+" + f, sim, false);
                    }
                }
                _stage.End();
                yield return null;
            }
            WriteReport();
            Debug.Log("[CriticalStances] done: samples=" + _samples + " bodiesWithAFail=" + _bad);
            yield return null;
            EditorApplication.Exit(0);
        }

        sealed class Job { public string Name; public Play Off, Def; public GameState State; public bool Huddle, Snap; public int Body = -1; }

        List<Job> BuildJobs()
        {
            var jobs = new List<Job>();
            var offs = _book.AllPlays.Where(p => p.Side == PlaySide.Offense).GroupBy(p => p.FormationName).Select(g => g.First()).ToList();
            var defs = _book.AllPlays.Where(p => p.Side == PlaySide.Defense).GroupBy(p => p.FormationName).Select(g => g.First()).ToList();
            int di = 0;
            foreach (var o in offs) { var d = defs[di++ % defs.Count]; jobs.Add(new Job { Name = o.FormationName + " v " + d.FormationName, Off = o, Def = d, State = Scrim(35, 1, 10), Huddle = jobs.Count % 3 == 0, Snap = jobs.Count % 2 == 0 }); }
            foreach (var d in defs) if (!jobs.Any(j => j.Def.FormationName == d.FormationName)) jobs.Add(new Job { Name = offs[0].FormationName + " v " + d.FormationName, Off = offs[0], Def = d, State = Scrim(35, 1, 10), Snap = true });
            // goal line and backed up situations: the short-yardage fronts
            foreach (var o in offs.Take(3)) jobs.Add(new Job { Name = o.FormationName + " goal line", Off = o, Def = defs[defs.Count - 1], State = Scrim(98, 1, 2), Snap = true });
            Func<string, Play> sp = n => _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Special && p.Name == n);
            Action<string, string, GameState, bool> special = (a, b, gs, snap) => { var po = sp(a); var pd = sp(b); if (po != null && pd != null) jobs.Add(new Job { Name = a + " v " + b, Off = po, Def = pd, State = gs, Snap = snap }); };
            special("Kickoff Deep Middle", "Kick Return Middle Wedge", new GameState(_rules, Team.Away), true);
            special("Kickoff Squib", "Kick Return Left", new GameState(_rules, Team.Away), true);
            special("Onside Kick Short", "Kick Return Right", new GameState(_rules, Team.Away), true);
            special("Punt Standard", "Punt Return Set", Scrim(35, 4, 8), true);
            special("Punt Directional Left", "Punt Return Safe", Scrim(40, 4, 8), true);
            special("Punt Standard", "Punt Block", Scrim(35, 4, 8), true);
            special("Field Goal", "Field Goal Block Middle", Scrim(80, 4, 5), true);
            special("Extra Point", "Field Goal Block Middle", Scrim(98, 1, 2), true);
            if (CriticalStances.Arg("-pixCriticalBodies", "0") == "1")
            {
                // every one of the 15 bodies in every position of a few key sets (the default run gives each position one body)
                var o0 = offs[0]; var d0 = defs[0]; var gl = offs.FirstOrDefault(p => p.FormationName == "Goal Line") ?? offs[0];
                for (int b = 0; b < 15; b++)
                {
                    jobs.Add(new Job { Name = "body" + b + " " + o0.FormationName + " v " + d0.FormationName, Off = o0, Def = d0, State = Scrim(35, 1, 10), Snap = true, Body = b });
                    jobs.Add(new Job { Name = "body" + b + " " + gl.FormationName + " goal line", Off = gl, Def = defs[defs.Count - 1], State = Scrim(98, 1, 2), Snap = true, Body = b });
                    special("Kickoff Deep Middle", "Kick Return Middle Wedge", new GameState(_rules, Team.Away), true); jobs[jobs.Count - 1].Body = b; jobs[jobs.Count - 1].Name = "body" + b + " " + jobs[jobs.Count - 1].Name;
                    special("Punt Standard", "Punt Return Set", Scrim(35, 4, 8), true); jobs[jobs.Count - 1].Body = b; jobs[jobs.Count - 1].Name = "body" + b + " " + jobs[jobs.Count - 1].Name;
                    special("Field Goal", "Field Goal Block Middle", Scrim(80, 4, 5), true); jobs[jobs.Count - 1].Body = b; jobs[jobs.Count - 1].Name = "body" + b + " " + jobs[jobs.Count - 1].Name;
                }
                jobs.RemoveRange(0, jobs.Count - 75);
            }
            return jobs;
        }

        void Sample(string situation, PlaySimulation sim, bool tiles)
        {
            if (CriticalStances.Arg("-pixCriticalCam", "") == "broadcast") { _cam.transform.position = new Vector3(44f, 17f, _stage.Views[0] != null ? _stage.Views[0].transform.position.z : 0f); _cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 1f, _cam.transform.position.z) - _cam.transform.position); _cam.fieldOfView = 27f; }
            GeometryUtility.CalculateFrustumPlanes(_cam, _planes);
            for (int i = 0; i < 22; i++)
            {
                var v = _stage.Views[i]; var a = sim.Agents[i];
                if (v == null || v.Avatar == null || v.Avatar.Animator == null || !v.Avatar.Animator.isHuman) continue;
                var an = v.Avatar.Animator;
                // the checks read the bones: pose is updated for every body (Prep), the camera only decides what "on screen" means
                var h = BodyCheck.Measure(v, _cam, _planes, true);
                Transform hips = an.GetBoneTransform(HumanBodyBones.Hips), chest = an.GetBoneTransform(HumanBodyBones.Chest) ?? an.GetBoneTransform(HumanBodyBones.UpperChest);
                Transform lh = an.GetBoneTransform(HumanBodyBones.LeftHand), rh = an.GetBoneTransform(HumanBodyBones.RightHand);
                Transform lul = an.GetBoneTransform(HumanBodyBones.LeftUpperLeg), rul = an.GetBoneTransform(HumanBodyBones.RightUpperLeg), lll = an.GetBoneTransform(HumanBodyBones.LeftLowerLeg), rll = an.GetBoneTransform(HumanBodyBones.RightLowerLeg), lf = an.GetBoneTransform(HumanBodyBones.LeftFoot), rf = an.GetBoneTransform(HumanBodyBones.RightFoot);
                float gy = v.transform.position.y;
                float hipsY = hips.position.y - gy;
                float lean = Vector3.Angle(Vector3.up, chest != null ? chest.position - hips.position : Vector3.up);
                float kneeL = Vector3.Angle(lul.position - lll.position, lf.position - lll.position), kneeR = Vector3.Angle(rul.position - rll.position, rf.position - rll.position);
                float kneeFlexMin = 180f - Mathf.Max(kneeL, kneeR);
                int down = (lh.position.y - gy < 0.2f ? 1 : 0) + (rh.position.y - gy < 0.2f ? 1 : 0);
                var pose = BodyHeading.Measure(an);
                float yawErr = StanceRules.YawDelta(pose.Yaw, v.Yaw);
                var kind = v.Stance.Kind;
                var f = new bool[Checks.Length];
                bool standingOrMoving = !situation.EndsWith("huddle");
                bool standing = sim.Phase == PlayPhase.PreSnap && a.Action == AgentAction.Stance && !v.Huddle;
                f[0] = h.SoleGap < 8f && h.SoleGap > 0.02f && standing;
                f[1] = h.SoleGap < 8f && h.SoleGap < -0.03f && standing;
                f[2] = h.TPose; f[3] = h.Twisted; f[4] = h.MissingPart; f[8] = h.BadScale; f[9] = h.OffPlace;
                f[5] = standing && Mathf.Abs(yawErr) > StanceRules.FacingToleranceDeg && kind != StanceKind.Kicker && kind != StanceKind.Punter && kind != StanceKind.Holder && kind != StanceKind.LongSnapper;
                int wantDown = kind == StanceKind.ThreePoint && (v.Role == PlayerRole.DL || v.Role == PlayerRole.TE || v.Role == PlayerRole.RB || v.Role == PlayerRole.OL) ? 1 : (kind == StanceKind.FourPoint ? 2 : -1);      // the tackles and guards (StanceSpec.Ol 1 / 2) rest their forearms on the thighs by design (BODIES v0.10)
                f[6] = standing && wantDown >= 0 && down < wantDown;
                bool twoPoint = kind == StanceKind.ReceiverSplit || kind == StanceKind.ReceiverSlot || kind == StanceKind.CornerPress || kind == StanceKind.CornerOff || kind == StanceKind.LinebackerTwoPoint || kind == StanceKind.SafetyDeep || kind == StanceKind.BackTwoPoint || kind == StanceKind.Gunner;
                f[7] = standing && twoPoint && kneeFlexMin < 12f;
                string key = kind + "|" + a.PlayPos + (situation.EndsWith("huddle") ? "|huddle" : situation.Contains("snap+") ? "|snap" : "|set");
                if (CriticalStances.Arg("-pixCriticalNoPrep", "0") == "1") key += h.OnScreen ? "|ON" : "|off";
                if (CriticalStances.Arg("-pixCriticalNoPrep", "0") == "1") key += h.OnScreen ? "|ON" : "|off";
                int[] c; if (!_fails.TryGetValue(key, out c)) _fails[key] = c = new int[Checks.Length + 1];
                c[0]++; _samples++;
                bool any = false; for (int k = 0; k < f.Length; k++) if (f[k]) { c[k + 1]++; any = true; }
                if (any) _bad++;
                if (_curBody >= 0 && standingOrMoving) { int[] bb; if (!_byBody.TryGetValue(_curBody, out bb)) _byBody[_curBody] = bb = new int[2]; bb[0]++; if (any) bb[1]++; if (any) _rep.AppendLine("BODYFAIL body" + _curBody + " " + a.PlayPos + " " + kind + " " + situation + " : " + string.Join("+", Checks.Where((n, k) => f[k]).ToArray()) + " sole=" + F(h.SoleGap) + " twist=" + F(h.TwistDeg)); }
                _csv.AppendLine(string.Join(",", new[] { situation.Replace(",", ";"), situation.Contains("snap+") ? situation.Substring(situation.IndexOf("snap+")) : (situation.EndsWith("huddle") ? "huddle" : "set"), i.ToString(), a.Side.ToString(), a.PlayPos.ToString(), kind.ToString(), v.Stance.Mirror.ToString(),
                    v.Rig != null ? v.Rig.CurrentClipName : "", F(hipsY), F(lean), F(kneeFlexMin), down.ToString(), F(h.SoleGap), F(yawErr), F(h.TwistDeg), h.Lod.ToString(), string.Join("+", Checks.Where((n, k) => f[k]).ToArray()) }));
                if (tiles && !_tiles.ContainsKey(kind + "|" + (v.Stance.Mirror ? "m" : "")) && a.PlayPos != Position.LOLB)
                {
                    Vector3 q = v.transform.position; Vector3 fwd = v.Forward, rightV = Vector3.Cross(Vector3.up, fwd);
                    _cam.transform.position = q + fwd * 3.0f - rightV * 2.0f + Vector3.up * 1.3f; _cam.transform.rotation = Quaternion.LookRotation(q + Vector3.up * 0.8f - _cam.transform.position); _cam.fieldOfView = 34f;
                    var hid = new List<Renderer>();
                    for (int j = 0; j < 22; j++) { var ov = _stage.Views[j]; if (ov == null || ov == v) continue; foreach (var rr in ov.GetComponentsInChildren<Renderer>()) if (rr.enabled) { rr.enabled = false; hid.Add(rr); } }
                    _tiles[kind + "|" + (v.Stance.Mirror ? "m" : "")] = ShotCam(360, 440);
                    foreach (var rr in hid) rr.enabled = true;
                    _rep.AppendLine("tile: " + kind + (v.Stance.Mirror ? " mirror" : "") + " = " + a.PlayPos + " in " + situation);
                }
            }
        }

        static string F(float x) { return x.ToString("0.000", CultureInfo.InvariantCulture); }

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

        void WriteReport()
        {
            File.WriteAllText(_out + "/stance_check.csv", _csv.ToString());
            var sb = new StringBuilder();
            sb.AppendLine("samples (body x sample): " + _samples + ", with at least one fail: " + _bad + " (" + (100.0 * _bad / Math.Max(1, _samples)).ToString("0.0") + " %)");
            sb.AppendLine("checks: " + string.Join(", ", Checks));
            sb.AppendLine("\nkind|pos|phase".PadRight(44) + "n".PadLeft(6) + string.Join("", Checks.Select(c => c.Substring(0, Math.Min(7, c.Length)).PadLeft(9)).ToArray()));
            foreach (var kv in _fails.OrderBy(k => k.Key))
                sb.AppendLine(kv.Key.PadRight(44) + kv.Value[0].ToString().PadLeft(6) + string.Join("", kv.Value.Skip(1).Select(x => (x == 0 ? "." : x.ToString()).PadLeft(9)).ToArray()));
            foreach (var kv in _byBody.OrderBy(k => k.Key)) sb.AppendLine("body " + kv.Key + " (size " + (kv.Key % 3) + " base " + (kv.Key / 3) + "): samples " + kv.Value[0] + ", with a fail " + kv.Value[1]);
            sb.AppendLine("\n" + _rep);
            File.WriteAllText(_out + "/stance_summary.txt", sb.ToString());
            var tl = _tiles.OrderBy(k => k.Key).ToList();
            File.WriteAllText(_out + "/stance_tiles_index.txt", string.Join("\n", tl.Select((k, i) => i + ": " + k.Key).ToArray()));
            if (tl.Count > 0)
            {
                int w = 360, h = 440, cols = 7, rows = (tl.Count + cols - 1) / cols;
                var tex = new Texture2D(w * cols, h * rows, TextureFormat.RGB24, false);
                for (int i = 0; i < tl.Count; i++) { int cx = i % cols, cy = rows - 1 - i / cols; tex.SetPixels(cx * w, cy * h, w, h, tl[i].Value.GetPixels()); }
                File.WriteAllBytes(_out + "/stance_tiles.png", tex.EncodeToPNG());
            }
        }
    }
}
