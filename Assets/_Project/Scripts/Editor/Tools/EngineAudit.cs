using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    /// v0.9 match-engine audit tool (QA only, additive, behind the flag <c>-pixEngineAudit</c>). Runs REAL plays through PlayStage (22 SimAgentViews + ball follower + contact director) on the stadium scene and measures
    /// the animated bodies frame by frame (60 Hz): planted-foot slide, flight phase, step rate and stride length per ground speed, hips height, bone pops, clip switches, body yaw vs simulated facing, ball to hand distance,
    /// the pre-snap stance pose of every position (joint angles), the kicker's side and kicking foot, and film strips of key moments (QB drop and throw, hand-off and run, route and catch, pass set, kicks, tackle).
    /// Run: Unity -projectPath ... -executeMethod PixFootball.EditorTools.EngineAudit.Run -pixEngineAudit  (batch mode WITHOUT -nographics).
    /// Args: -pixEngineAuditOut dir (default D:/PIXFootball/Renders/engine_audit), -pixEngineAuditPlays n (default 36), -pixEngineAuditStrips 0|1, -pixEngineAuditStances 0|1, -pixEngineAuditPhase metrics|strips|all.
    /// Outputs: motion_frames.csv (per agent per frame), motion_summary.txt (distributions), stance_poses.csv, strips/*.png, kick_report.txt.
    /// </summary>
    public static class EngineAudit
    {
        public const string Flag = "-pixEngineAudit";
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
            new GameObject("PixEngineAudit").AddComponent<EngineAuditRunner>();
        }
    }

    public sealed class EngineAuditRunner : MonoBehaviour
    {
        string _out;
        Camera _cam; PlayStage _stage; Playbook _book; RuleSet _rules;
        readonly StringBuilder _report = new StringBuilder();
        readonly StringBuilder _csv = new StringBuilder();
        readonly Dictionary<string, List<double>> _acc = new Dictionary<string, List<double>>();
        readonly Dictionary<string, int> _hist = new Dictionary<string, int>();
        readonly Dictionary<string, double[]> _gait = new Dictionary<string, double[]>();   // key -> time, steps, distance
        readonly List<string> _pops = new List<string>();
        readonly Dictionary<SimAgentView, LegProbe> _probes = new Dictionary<SimAgentView, LegProbe>();
        readonly SortedDictionary<string, LegProbe.Acc> _legAcc = new SortedDictionary<string, LegProbe.Acc>();
        int _playIndex;
        static readonly UniformSet KitA = Kit(TeamColors.Home, true), KitB = Kit(TeamColors.Away, false);
        static UniformSet Kit(TeamColors c, bool home) { var u = new UniformSet(); u.Uniform = c; u.Helmet = c; u.Home = home; return u; }
        const float Dt = 1f / 60f;

        sealed class Bones
        {
            public Transform Hips, Spine, Chest, Head, LF, RF, LT, RT, LH, RH, LUL, RUL, LLL, RLL, LUA, RUA, LLA, RLA;
            public Vector3 PrevLT, PrevRT, PrevLF, PrevRF, PrevHips, PrevHipsLocal;
            public Quaternion PrevHipsRot = Quaternion.identity, PrevChestRot = Quaternion.identity, PrevHeadRot = Quaternion.identity;
            public int CntL, CntR; public float MinL = 99f, MinR = 99f;
            public bool PlantL, PlantR, HavePrev; public string PrevClip = ""; public AgentAction PrevAct = (AgentAction)(-1);
            public int ClipSwitches; public float TimeAlive; public float LastSwitchT = -9f; public int ShortSwitches;
            public Vector3 PrevHandL, PrevHandR;
        }
        readonly Dictionary<SimAgentView, Bones> _bones = new Dictionary<SimAgentView, Bones>(); bool _warned;

        void Add(string k, double v) { List<double> l; if (!_acc.TryGetValue(k, out l)) _acc[k] = l = new List<double>(); l.Add(v); }
        void Hist(string k) { int n; _hist.TryGetValue(k, out n); _hist[k] = n + 1; }

        static string Grp(Position p)
        {
            if (PositionGroup.IsOffensiveLine(p)) return "OL";
            if (PositionGroup.IsDefensiveLine(p)) return "DL";
            if (PositionGroup.IsLinebacker(p)) return "LB";
            if (p == Position.CB) return "CB";
            if (p == Position.FS || p == Position.SS) return "S";
            return p.ToString();
        }

        IEnumerator Start()
        {
            Time.captureFramerate = 60;
            _out = EngineAudit.Arg("-pixEngineAuditOut", "D:/PIXFootball/Renders/engine_audit");
            Directory.CreateDirectory(_out); Directory.CreateDirectory(_out + "/strips");
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception || t == LogType.Error) { Debug.Log("[EngineAudit] logged " + t + ": " + c + " :: " + (st.Length > 300 ? st.Substring(0, 300) : st)); if (t == LogType.Exception && st.Contains("EngineAudit")) { try { File.WriteAllText(_out + "/engine_audit_report_PARTIAL.txt", _report.ToString()); } catch { } EditorApplication.Exit(3); } } };
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) Destroy(pc.gameObject);
            foreach (var fr in FindObjectsByType<FreeRoamGame>(FindObjectsSortMode.None)) fr.enabled = false;
            foreach (var br in FindObjectsByType<Unity.Cinemachine.CinemachineBrain>(FindObjectsSortMode.None)) br.enabled = false;
            var camGo = new GameObject("EngineAuditCam");
            _cam = camGo.AddComponent<Camera>(); var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            hdc.customRenderingSettings = true;
            { var fs = hdc.renderingPathCustomFrameSettings; var mk = hdc.renderingPathCustomFrameSettingsOverrideMask; foreach (var ff in new[] { FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField }) { mk.mask[(uint)ff] = true; fs.SetEnabled(ff, false); } hdc.renderingPathCustomFrameSettings = fs; hdc.renderingPathCustomFrameSettingsOverrideMask = mk; }
            _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 1500f; _cam.allowHDR = true; _cam.tag = "MainCamera";
            _book = DefaultPlaybooks.Create(); _rules = RuleSet.Pro();
            _stage = new GameObject("EngineAuditStage").AddComponent<PlayStage>();
            _stage.Init();
            yield return null;
            string phase = EngineAudit.Arg("-pixEngineAuditPhase", "all");
            int nPlays = int.Parse(EngineAudit.Arg("-pixEngineAuditPlays", "36"));
            if (phase == "all" || phase == "metrics")
            {
                if (EngineAudit.Arg("-pixEngineAuditStances", "1") == "1") yield return StanceAudit();
                yield return MetricPlays(nPlays);
                yield return KickAudit();
                WriteSummary();
            }
            if ((phase == "all" || phase == "strips") && EngineAudit.Arg("-pixEngineAuditStrips", "1") == "1") yield return Strips();
            File.WriteAllText(_out + "/engine_audit_report.txt", _report.ToString());
            Debug.Log("[EngineAudit] done");
            yield return null;
            EditorApplication.Exit(0);
        }

        // ------------------------------------------------------------------------------------------ helpers
        PlaySimulation NewSim(Play off, Play def, GameState gs, int seed, PlaySimOptions opt = null)
        {
            var sim = new PlaySimulation();
            opt = opt ?? new PlaySimOptions { Playbook = _book, Penalties = false, AutoSnap = false, AutoSnapDelay = 99f, QbWindupSeconds = 0.42f };
            sim.Setup(gs, off, def, null, null, _rules, new Rng(seed), opt);
            return sim;
        }

        GameState Scrim(int spot, int down, int dist)
        {
            var gs = new GameState(_rules); gs.Phase = GamePhase.Scrimmage; gs.BallSpot = spot; gs.Down = down; gs.Distance = dist; gs.Possession = Team.Home;
            return gs;
        }

        Play DefFor(Play off)
        {
            var defs = _book.AllPlays.Where(p => p.Side == PlaySide.Defense).ToList();
            var zone = defs.Where(p => p.Category == PlayCategory.ZoneCoverage).ToList();
            var man = defs.Where(p => p.Category == PlayCategory.ManCoverage).ToList();
            var bl = defs.Where(p => p.Category == PlayCategory.Blitz).ToList();
            int h = Mathf.Abs(off.Name.GetHashCode() + off.FormationName.GetHashCode());
            if (off.IsPass && h % 5 == 0 && bl.Count > 0) return bl[h % bl.Count];
            if (h % 3 == 0 && man.Count > 0) return man[h % man.Count];
            return zone[h % zone.Count];
        }

        void Prep()
        {
            foreach (var v in _stage.Views)
            {
                if (v == null || v.Avatar == null || v.Avatar.Animator == null) continue;
                v.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var smr in v.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            }
        }

        Bones GetBones(SimAgentView v)
        {
            Bones b;
            if (_bones.TryGetValue(v, out b) && b.Hips != null) return b;
            var an = v.Avatar.Animator; if (an == null || !an.isHuman) { if (!_warned) { _warned = true; Debug.Log("[EngineAudit] view " + v.name + " animator " + (an == null ? "null" : "isHuman=" + an.isHuman + " avatar=" + (an.avatar == null ? "null" : an.avatar.name + " valid=" + an.avatar.isValid + " human=" + an.avatar.isHuman) + " active=" + an.gameObject.activeInHierarchy + " enabled=" + an.enabled)); } return null; }
            b = new Bones();
            b.Hips = an.GetBoneTransform(HumanBodyBones.Hips); b.Spine = an.GetBoneTransform(HumanBodyBones.Spine); b.Chest = an.GetBoneTransform(HumanBodyBones.Chest) ?? an.GetBoneTransform(HumanBodyBones.UpperChest);
            b.Head = an.GetBoneTransform(HumanBodyBones.Head);
            b.LF = an.GetBoneTransform(HumanBodyBones.LeftFoot); b.RF = an.GetBoneTransform(HumanBodyBones.RightFoot);
            b.LT = an.GetBoneTransform(HumanBodyBones.LeftToes); b.RT = an.GetBoneTransform(HumanBodyBones.RightToes);
            b.LH = an.GetBoneTransform(HumanBodyBones.LeftHand); b.RH = an.GetBoneTransform(HumanBodyBones.RightHand);
            b.LUL = an.GetBoneTransform(HumanBodyBones.LeftUpperLeg); b.RUL = an.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            b.LLL = an.GetBoneTransform(HumanBodyBones.LeftLowerLeg); b.RLL = an.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            b.LUA = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); b.RUA = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
            b.LLA = an.GetBoneTransform(HumanBodyBones.LeftLowerArm); b.RLA = an.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _bones[v] = b;
            return b;
        }

        // ------------------------------------------------------------------------------------------ stance audit
        IEnumerator StanceAudit()
        {
            var sbCsv = new StringBuilder("formation,side,agent,pos,simStance,viewKind,mirror,leftOfBall,clip,hipsY,headY,trunkLeanDeg,thighToTrunkDeg,kneeL,kneeR,feetStaggerM,feetWidthM,handL_y,handR_y,handsDown,lowestToeY,headPitchDeg,simX,simY,bodyYawErrDeg\n");
            var forms = _book.AllPlays.Where(p => p.Side == PlaySide.Offense).GroupBy(p => p.FormationName).Select(g => g.First()).ToList();
            var defForms = _book.AllPlays.Where(p => p.Side == PlaySide.Defense).GroupBy(p => p.FormationName).Select(g => g.First()).ToList();
            var specials = new[] { "Kickoff Deep Middle", "Kick Return Middle Wedge", "Field Goal", "Field Goal Block Middle" };
            var tiles = new Dictionary<string, Texture2D>();
            int fi = 0;
            var jobs = new List<KeyValuePair<Play, Play>>();
            foreach (var f in forms) { var dfm = defForms[fi++ % defForms.Count]; jobs.Add(new KeyValuePair<Play, Play>(f, dfm)); }
            foreach (var d in defForms) if (!jobs.Any(j => j.Value != null && j.Value.FormationName == d.FormationName)) jobs.Add(new KeyValuePair<Play, Play>(forms[0], d));
            foreach (var sp in specials) { var p = _book.AllPlays.FirstOrDefault(x => x.Side == PlaySide.Special && x.Name == sp); if (p != null) jobs.Add(new KeyValuePair<Play, Play>(p, null)); }
            foreach (var job in jobs)
            {
                var off = job.Key; var def = job.Value;
                if (off.Side == PlaySide.Special)
                {
                    // pair the special with its opponent unit
                    def = off.Name.StartsWith("Kickoff") ? _book.AllPlays.First(p => p.Side == PlaySide.Special && p.Name == "Kick Return Middle Wedge") : (off.Name == "Field Goal" ? _book.AllPlays.First(p => p.Side == PlaySide.Special && p.Name == "Field Goal Block Middle") : _book.AllPlays.First(p => p.Side == PlaySide.Special && p.Name == "Kickoff Deep Middle"));
                }
                GameState gs = off.Name.StartsWith("Kickoff") ? new GameState(_rules, Team.Away) : (off.Name == "Field Goal" ? Scrim(80, 4, 5) : Scrim(35, 1, 10));
                if (off.Side == PlaySide.Special && off.Name.StartsWith("Kick Return")) gs = new GameState(_rules, Team.Away);
                PlaySimulation sim;
                try { sim = NewSim(off, def ?? DefFor(off), gs, 91); } catch (Exception e) { _report.AppendLine("stance audit: could not set up " + off.Name + ": " + e.Message); continue; }
                var frame = new PlayFrame(true);
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex);
                Prep();
                for (int f = 0; f < 100; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                for (int i = 0; i < 22; i++)
                {
                    var a = sim.Agents[i]; var v = _stage.Views[i]; var b = v != null ? GetBones(v) : null;
                    if (b == null || b.Hips == null) continue;
                    float gy = v.transform.position.y;
                    var pose = BodyHeading.Measure(v.Avatar.Animator);
                    Vector3 fwd = v.Forward; Vector3 rightV = Vector3.Cross(Vector3.up, fwd);
                    Vector3 hipsToHead = b.Head.position - b.Hips.position;
                    float lean = Vector3.Angle(Vector3.up, b.Chest != null ? b.Chest.position - b.Hips.position : hipsToHead);
                    float headPitch = Vector3.Angle(Vector3.up, hipsToHead);
                    float kneeL = 180f - Vector3.Angle(b.LUL.position - b.LLL.position, b.LF.position - b.LLL.position);
                    float kneeR = 180f - Vector3.Angle(b.RUL.position - b.RLL.position, b.RF.position - b.RLL.position);
                    kneeL = 180f - kneeL; kneeR = 180f - kneeR;   // knee flexion: 0 = straight leg
                    float thigh = Vector3.Angle(Vector3.up, b.Hips.position - b.LUL.position);   // unused detail
                    float stagger = Vector3.Dot(b.RF.position - b.LF.position, fwd);
                    float width = Mathf.Abs(Vector3.Dot(b.RF.position - b.LF.position, rightV));
                    float lhY = b.LH.position.y - gy, rhY = b.RH.position.y - gy;
                    int down = (lhY < 0.2f ? 1 : 0) + (rhY < 0.2f ? 1 : 0);
                    float lowToe = Mathf.Min(b.LT != null ? b.LT.position.y : 9f, b.RT != null ? b.RT.position.y : 9f) - gy;
                    float yawErr = StanceRules.YawDelta(pose.Yaw, v.Yaw);
                    sbCsv.AppendLine(string.Join(",", new[] { off.FormationName + "|" + (def != null ? def.FormationName : ""), a.Side.ToString(), i.ToString(), a.PlayPos.ToString(), a.StanceKind.ToString(), v.Stance.Kind.ToString(), v.Stance.Mirror.ToString(), v.Stance.IsLeftOfBall.ToString(), v.Rig != null ? v.Rig.CurrentClipName : "", F(b.Hips.position.y - gy), F(b.Head.position.y - gy), F(lean), F(headPitch), F(kneeL), F(kneeR), F(stagger), F(width), F(lhY), F(rhY), down.ToString(), F(lowToe), F(headPitch), F(a.Pos.X), F(a.Pos.Y), F(yawErr) }));
                    string key = a.PlayPos + "|" + v.Stance.Kind + (v.Stance.Mirror ? "|m" : "");
                    Add("stance hipsY " + key, b.Hips.position.y - gy); Add("stance trunk lean deg " + key, lean); Add("stance knee flex L " + key, kneeL); Add("stance knee flex R " + key, kneeR);
                    Add("stance stagger m (R foot ahead +) " + key, stagger); Add("stance feet width m " + key, width); Add("stance hands down " + key, down); Add("stance body yaw err deg " + key, Mathf.Abs(yawErr)); Add("stance lowest toe y " + key, lowToe);
                    Add("stance hand heights (min) " + key, Mathf.Min(lhY, rhY));
                    if (!tiles.ContainsKey(key) && a.PlayPos != Position.K || (a.PlayPos == Position.K && !tiles.ContainsKey(key)))
                    {
                        // 3/4 front-left tile of this kind
                        Vector3 q = v.transform.position;
                        if (EngineAudit.Arg("-pixEngineAuditTileCam", "front") == "side") { _cam.transform.position = q + rightV * 3.4f + Vector3.up * 0.9f - fwd * 0.2f; _cam.transform.rotation = Quaternion.LookRotation(q + Vector3.up * 0.75f - _cam.transform.position); _cam.fieldOfView = 30f; }   // v0.9.0 QBSTANCE: profile view (lean, hand on the turf)
                        else { _cam.transform.position = q + fwd * 3.0f - rightV * 2.0f + Vector3.up * 1.3f; _cam.transform.rotation = Quaternion.LookRotation(q + Vector3.up * 0.8f - _cam.transform.position); _cam.fieldOfView = 34f; }
                        // v0.9.0 QBSTANCE: the other 21 players are hidden for the tile (a side camera would otherwise look through the next lineman)
                        var hid = new List<Renderer>();
                        if (EngineAudit.Arg("-pixEngineAuditTileCam", "front") == "side") for (int j = 0; j < 22; j++) { var ov = _stage.Views[j]; if (ov == null || ov == v) continue; foreach (var rr in ov.GetComponentsInChildren<Renderer>()) if (rr.enabled) { rr.enabled = false; hid.Add(rr); } }
                        tiles[key] = ShotCam(360, 440);
                        foreach (var rr in hid) rr.enabled = true;
                        _report.AppendLine("stance tile: " + key + " (" + off.FormationName + ")");
                    }
                }
                _stage.End();
                yield return null;
            }
            File.WriteAllText(_out + "/stance_poses.csv", sbCsv.ToString());
            Sheet(tiles.OrderBy(k => k.Key).Select(k => k.Value).ToList(), 8, _out + "/strips/stance_tiles.png");
            File.WriteAllText(_out + "/strips/stance_tiles_index.txt", string.Join("\n", tiles.OrderBy(k => k.Key).Select((k, i) => i + ": " + k.Key)));
        }

        static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        // ------------------------------------------------------------------------------------------ metric plays
        IEnumerator MetricPlays(int n)
        {
            NaturalProbe.Reset();
            _csv.AppendLine("play,t,agent,pos,simAction,clip,rigW,speedMs,hipsY,lowToeY,footLslide,footRslide,plantedL,plantedR,hipsRotDegS,chestRotDegS,headRotDegS,bodyYawErrFace,bodyYawErrVel,ballDistHand,mmLayer,mmClip,mmRow,fpL,fpR,hintL,hintR,mmWarp,mmRate,hipsOff,soleLow");
            var offense = _book.AllPlays.Where(p => p.Side == PlaySide.Offense && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike && p.Name != "Hail Mary").ToList();
            var rnd = new System.Random(4242);
            var chosen = new List<Play>();
            // coverage of every category first, then random
            foreach (var cat in offense.Select(p => p.Category).Distinct()) { var l = offense.Where(p => p.Category == cat).ToList(); chosen.Add(l[rnd.Next(l.Count)]); }
            while (chosen.Count < n) chosen.Add(offense[rnd.Next(offense.Count)]);
            for (int pi = 0; pi < n; pi++)
            {
                var off = chosen[pi]; var def = DefFor(off);
                var sim = NewSim(off, def, Scrim(35, 1, 10), 500 + pi);
                var frame = new PlayFrame(true);
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex);
                Prep();
                for (int f = 0; f < 90; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                foreach (var b in _bones.Values) { b.HavePrev = false; b.ClipSwitches = 0; b.TimeAlive = 0f; b.ShortSwitches = 0; }
                sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
                float t = 0f; int frames = 0;
                while (sim.Phase != PlayPhase.Dead && t < 12f)
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); t += Dt; frames++;
                    yield return null;
                    Sample(sim, pi, t);
                }
                // 2.5 s after the whistle (falls, get-ups)
                for (int f = 0; f < 150 && sim.Phase == PlayPhase.Dead; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; t += Dt; Sample(sim, pi, t, true); }
                CutProbe.EndPlay();      // v0.12.0 GAIT
                NaturalProbe.EndPlay();      // v0.12.0 NATURAL
                for (int i = 0; i < 22; i++)
                {
                    var v = _stage.Views[i]; Bones b; if (v == null || !_bones.TryGetValue(v, out b)) continue;
                    if (b.TimeAlive > 0.5f) { Add("clip switches per second " + Grp(sim.Agents[i].PlayPos), b.ClipSwitches / b.TimeAlive); Add("clip switches <0.25s apart per play " + Grp(sim.Agents[i].PlayPos), b.ShortSwitches); }
                }
                _report.AppendLine("play " + pi + " " + off.Name + " [" + off.FormationName + "] vs " + def.Name + " ended " + sim.EndReason + " at " + t.ToString("0.0"));
                _stage.End();
                yield return null;
            }
            File.WriteAllText(_out + "/motion_frames.csv", _csv.ToString());
            File.WriteAllText(_out + "/natural.md", NaturalProbe.Report());      // v0.12.0 NATURAL
        }

        void Sample(PlaySimulation sim, int play, float t, bool afterWhistle = false)
        {
            var frame = _stage.Frame;
            for (int i = 0; i < 22; i++)
            {
                var a = sim.Agents[i]; var v = _stage.Views[i]; if (v == null || v.Avatar == null) continue;
                var b = GetBones(v); if (b == null) continue;
                float gy = v.transform.position.y;
                string g = Grp(a.PlayPos);
                string clip = v.Rig != null ? v.Rig.CurrentClipName : "";
                float rigW = v.Rig != null ? v.Rig.Weight : 0f;
                float speedMs = a.Speed * 0.9144f;
                string act = a.Action.ToString();
                bool contact = v.ContactDriven;
                string akey = g + " " + act + (contact ? " [contact]" : "");
                float hipsY = b.Hips.position.y - gy;
                Vector3 ltP = b.LT != null ? b.LT.position : b.LF.position, rtP = b.RT != null ? b.RT.position : b.RF.position;
                float lth = ltP.y - gy, rth = rtP.y - gy;
                float lowToe = Mathf.Min(lth, rth);
                float lah = b.LF.position.y - gy, rah = b.RF.position.y - gy;
                float lc = Mathf.Min(lth, lah - 0.09f), rc = Mathf.Min(rth, rah - 0.09f);   // contact height: ball of the foot or the ankle above its resting height
                if (speedMs <= 1.5f) FormAuditHook.NoteSlow(v, speedMs);      // v0.12.0 GAIT: keeps the 0.2 s acceleration window of FormProbe continuous across a start from rest
                { var cp = v.Motion != null ? v.Motion.Plant : null; bool cok = cp != null && cp.Ok; CutProbe.Feed(i, a.PlayPos.ToString(), a.Action == AgentAction.Carry || a.HasBall, new CutProbe.Fr { Vel = new Vector2(frame.DirToUnity(a.Vel).x, frame.DirToUnity(a.Vel).z), Speed = speedMs, Valid = !contact && !afterWhistle && !a.IsDown, AL = b.LF.position, AR = b.RF.position, TL = ltP, TR = rtP, SoleL = cok ? Mathf.Min(lth - cp.RestToeL, lah - cp.RestAnkleL) : lc, SoleR = cok ? Mathf.Min(rth - cp.RestToeR, rah - cp.RestAnkleR) : rc, HipsY = hipsY, FlagL = cok && cp.PlantedLeft, FlagR = cok && cp.PlantedRight, RigW = rigW, MmLayer = v.Mm != null ? v.Mm.Layer : -1f, Clip = clip, Act = a.Action }); }      // v0.12.0 GAIT: cut audit series
                bool plantL = b.PlantL ? lc < 0.07f : lc < 0.04f, plantR = b.PlantR ? rc < 0.07f : rc < 0.04f;
                float slideL = -1f, slideR = -1f, hipsRot = 0f, chestRot = 0f, headRot = 0f;
                if (b.HavePrev)
                {
                    Vector3 dl = ltP - b.PrevLT; dl.y = 0f; Vector3 dr = rtP - b.PrevRT; dr.y = 0f;
                    Vector3 dla = b.LF.position - b.PrevLF; dla.y = 0f; Vector3 dra = b.RF.position - b.PrevRF; dra.y = 0f;
                    if (plantL && b.PlantL) slideL = Mathf.Min(dl.magnitude, dla.magnitude) / Dt;
                    if (plantR && b.PlantR) slideR = Mathf.Min(dr.magnitude, dra.magnitude) / Dt;
                    hipsRot = Quaternion.Angle(b.Hips.rotation, b.PrevHipsRot) / Dt;
                    chestRot = b.Chest != null ? Quaternion.Angle(b.Chest.rotation, b.PrevChestRot) / Dt : 0f;
                    headRot = b.Head != null ? Quaternion.Angle(b.Head.rotation, b.PrevHeadRot) / Dt : 0f;
                    bool movingBody = !contact && !afterWhistle && !(a.IsDown);
                    if (movingBody)
                    {
                        if (slideL >= 0f) Add("planted foot slide m/s " + akey, slideL);
                        if (slideR >= 0f) Add("planted foot slide m/s " + akey, slideR);
                        bool loco = (a.Action == AgentAction.Run || a.Action == AgentAction.Sprint || a.Action == AgentAction.Carry || a.Action == AgentAction.Route || a.Action == AgentAction.Backpedal || a.Action == AgentAction.Shuffle);
                        if (loco && speedMs > 1.0f)
                        {
                            // gait bins: step rate and stride length per speed bin
                            string sk = akey + " v" + Mathf.Clamp((int)(speedMs / 1.5f) * 1.5f, 0f, 9f).ToString("0.0");
                            double[] gs; if (!_gait.TryGetValue(sk, out gs)) _gait[sk] = gs = new double[4];
                            gs[0] += Dt; gs[2] += speedMs * Dt;
                            if (plantL && !b.PlantL) gs[1]++;
                            if (plantR && !b.PlantR) gs[1]++;
                            gs[3] += (lth > 0.10f && rth > 0.10f) ? Dt : 0.0;
                            Add("hips height m " + akey, hipsY);
                            Add("lowest toe above ground m " + akey, lowToe);
                            if (lth > 0.10f && rth > 0.10f) Hist("flight-phase frames " + akey);
                            Hist("loco frames " + akey);
                            if (speedMs > 1.5f) { FormAuditHook.Feed(v, a.Action.ToString(), a.Action == AgentAction.Carry || a.HasBall, a.PlayPos.ToString(), speedMs, frame.DirToUnity(a.Vel), gy, (float)Dt); LegProbe lp; if (!_probes.TryGetValue(v, out lp)) _probes[v] = lp = new LegProbe(v.Avatar.Animator, v.Motion != null ? v.Motion.Plant : null); string lk = (a.Action == AgentAction.Carry || a.HasBall ? "Carry" : a.Action.ToString()) + (speedMs < 3f ? " <3 m/s" : speedMs < 5.5f ? " 3-5.5 m/s" : " >=5.5 m/s"); LegProbe.Acc la; if (!_legAcc.TryGetValue(lk, out la)) _legAcc[lk] = la = new LegProbe.Acc(); lp.Sample(la, v.Forward, frame.DirToUnity(a.Vel), speedMs, gy); }
                        }
                        // idle/stance feet hover
                        if (!loco && speedMs < 0.3f && (a.Action == AgentAction.Idle || a.Action == AgentAction.Stance || a.Action == AgentAction.Block)) Add("standing lowest toe above ground m " + akey, lowToe);
                        // standing animation but the sim moves: the foot slides / body glides
                        if ((a.Action == AgentAction.Idle || a.Action == AgentAction.Stance) && speedMs > 1.0f) Hist("STAND clip while sim speed>1m/s " + g + " " + act);
                        if ((a.Action == AgentAction.Run || a.Action == AgentAction.Sprint) && speedMs < 0.5f) Hist("RUN clip while sim speed<0.5m/s " + g);
                        // pops
                        if (hipsRot > 900f) { Hist("hips rot pop >900deg/s " + g); if (_pops.Count < 400) _pops.Add(play + "," + t.ToString("0.00") + "," + i + "," + a.PlayPos + "," + act + "," + clip + ",hips," + hipsRot.ToString("0")); }
                        if (chestRot > 1400f) { Hist("chest rot pop >1400deg/s " + g); if (_pops.Count < 400) _pops.Add(play + "," + t.ToString("0.00") + "," + i + "," + a.PlayPos + "," + act + "," + clip + ",chest," + chestRot.ToString("0")); }
                        if (headRot > 1600f) Hist("head rot pop >1600deg/s " + g);
                        Add("hips rot deg/s " + g, hipsRot);
                        Vector3 hl = v.transform.InverseTransformPoint(b.Hips.position);
                        float hjump = (hl - b.PrevHipsLocal).magnitude;
                        if (hjump > 0.10f) { Hist("hips local jump >10cm in one frame " + g); if (_pops.Count < 400) _pops.Add(play + "," + t.ToString("0.00") + "," + i + "," + a.PlayPos + "," + act + "," + clip + ",hipsLocalJump," + hjump.ToString("0.00")); }
                    }
                }
                // clip switches
                if (clip != b.PrevClip && b.HavePrev)
                {
                    if (!afterWhistle) { b.ClipSwitches++; if (t - b.LastSwitchT < 0.25f) b.ShortSwitches++; b.LastSwitchT = t; }
                    Hist("clip: " + g + " " + act + " -> " + (clip.Length == 0 ? "(locomotion tree)" : clip));
                }
                if (!afterWhistle) b.TimeAlive += Dt;
                if (b.HavePrev || true) { if (clip.Length > 0 || rigW > 0.05f) Hist("clip-in-use: " + g + " " + act + " :: " + (clip.Length == 0 ? "(none)" : clip)); else Hist("clip-in-use: " + g + " " + act + " :: (locomotion tree)"); }
                // body yaw vs sim facing / velocity
                var pose = BodyHeading.Measure(v.Avatar.Animator);
                float errFace = 0f, errVel = 0f;
                if (pose.Valid)
                {
                    Vector3 fd = frame.DirToUnity(a.Facing); float simYaw = Mathf.Atan2(fd.x, fd.z) * Mathf.Rad2Deg;
                    errFace = Mathf.Abs(StanceRules.YawDelta(pose.Yaw, simYaw));
                    if (speedMs > 2f) { Vector3 vd = frame.DirToUnity(a.Vel); float vy = Mathf.Atan2(vd.x, vd.z) * Mathf.Rad2Deg; errVel = Mathf.Abs(StanceRules.YawDelta(pose.Yaw, vy)); }
                    if (!contact && !afterWhistle && !a.IsDown) { Add("body yaw vs sim facing deg " + akey, errFace); if (speedMs > 2f) Add("body yaw vs sim velocity deg " + akey, errVel); }
                }
                // v0.9.0 QBSTANCE: the quarterback's drop-back (any sim action): flight phase, speed, how far the body faces from the way he travels
                if (a.PlayPos == Position.QB && a.Side == SimSide.Offense && (a.QbState == QbPhase.Dropback) && speedMs > 0.8f && !contact && !afterWhistle)
                {
                    Hist("QBDROP frames"); if (lth > 0.10f && rth > 0.10f) Hist("QBDROP flight frames (both toes above 10 cm)");
                    Add("QBDROP speed m/s", speedMs); Add("QBDROP body yaw vs sim velocity deg", errVel); Add("QBDROP hips height m", hipsY);
                    Hist("QBDROP sim action " + act); Hist("QBDROP clip " + (clip.Length == 0 ? "(locomotion tree)" : clip));
                }
                // ball vs hand
                float ballD = -1f;
                if (sim.Ball.Holder == i && _stage.Ball != null)
                {
                    Vector3 bp = _stage.Ball.Position; ballD = Mathf.Min(Vector3.Distance(bp, b.LH.position), Vector3.Distance(bp, b.RH.position));
                    Add("ball to nearest hand m, holder " + g + " " + act, ballD);
                    if (ballD > 0.45f) Hist("ball >45cm from both hands while held " + g + " " + act);
                }
                NaturalProbe.Feed(play, i, v, g, a.Side.ToString(), act, a.Action == AgentAction.Carry || a.HasBall, speedMs, frame.DirToUnity(a.Vel), frame.DirToUnity(a.Facing), t, !contact && !a.IsDown, Mathf.RoundToInt(t / Dt));      // v0.12.0 NATURAL
                _csv.Append(play).Append(',').Append(t.ToString("0.000")).Append(',').Append(i).Append(',').Append(a.PlayPos).Append(',').Append(act).Append(',').Append(clip).Append(',').Append(F(rigW)).Append(',').Append(F(speedMs)).Append(',').Append(F(hipsY)).Append(',').Append(F(lowToe)).Append(',').Append(F(slideL)).Append(',').Append(F(slideR)).Append(',').Append(plantL ? 1 : 0).Append(',').Append(plantR ? 1 : 0).Append(',').Append(F(hipsRot)).Append(',').Append(F(chestRot)).Append(',').Append(F(headRot)).Append(',').Append(F(errFace)).Append(',').Append(F(errVel)).Append(',').Append(F(ballD)).Append(',').Append(v.Mm != null ? F(v.Mm.Layer) : "-").Append(',').Append(v.Mm != null ? v.Mm.ClipName : "-").Append(',').Append(v.Mm != null ? v.Mm.Row : -1).Append(',').Append(v.Motion != null && v.Motion.Plant.PlantedLeft ? 1 : 0).Append(',').Append(v.Motion != null && v.Motion.Plant.PlantedRight ? 1 : 0).Append(',').Append(v.Motion != null ? v.Motion.Plant.HintLeft : -1).Append(',').Append(v.Motion != null ? v.Motion.Plant.HintRight : -1).Append(',').Append(v.Mm != null ? F(v.Mm.Warp) : "-").Append(',').Append(v.Mm != null ? F(v.Mm.Rate) : "-").Append(',').Append(F(new Vector2(b.Hips.position.x - v.transform.position.x, b.Hips.position.z - v.transform.position.z).magnitude)).Append(',').Append(F(v.Motion != null && v.Motion.Plant != null && v.Motion.Plant.Ok ? Mathf.Min(Mathf.Min(ltP.y - gy - v.Motion.Plant.RestToeL, b.LF.position.y - gy - v.Motion.Plant.RestAnkleL), Mathf.Min(rtP.y - gy - v.Motion.Plant.RestToeR, b.RF.position.y - gy - v.Motion.Plant.RestAnkleR)) : lowToe)).AppendLine();
                b.PrevLT = ltP; b.PrevRT = rtP; b.PrevHipsRot = b.Hips.rotation; b.PrevChestRot = b.Chest != null ? b.Chest.rotation : Quaternion.identity; b.PrevHeadRot = b.Head != null ? b.Head.rotation : Quaternion.identity;
                b.PrevHipsLocal = v.transform.InverseTransformPoint(b.Hips.position);
                b.PrevLF = b.LF.position; b.PrevRF = b.RF.position;
                TrackContact(b, 0, plantL, slideL, akey, speedMs, !contact && !afterWhistle && !a.IsDown); TrackContact(b, 1, plantR, slideR, akey, speedMs, !contact && !afterWhistle && !a.IsDown);
                b.PlantL = plantL; b.PlantR = plantR; b.PrevClip = clip; b.PrevAct = a.Action; b.HavePrev = true;
            }
        }

        void TrackContact(Bones b, int foot, bool plant, float slide, string akey, float speedMs, bool valid)
        {
            ref int n = ref (foot == 0 ? ref b.CntL : ref b.CntR); ref float mn = ref (foot == 0 ? ref b.MinL : ref b.MinR); bool was = foot == 0 ? b.PlantL : b.PlantR;
            if (plant) { if (!was) { n = 0; mn = 99f; } n++; if (slide >= 0f && slide < mn) mn = slide; }
            else if (was)
            {
                if (valid && n >= 3 && mn < 98f) { Add("contact min slide m/s " + akey, mn); Add("contact frames " + akey, n); if (speedMs > 1f) Add("contact min slide m/s by v " + akey + " v" + Mathf.Clamp((int)(speedMs / 1.5f) * 1.5f, 0f, 9f).ToString("0.0"), mn); }
            }
        }

        // ------------------------------------------------------------------------------------------ kicks: kicker side and foot
        IEnumerator KickAudit()
        {
            var kicks = new List<KeyValuePair<string, KeyValuePair<Play, Play>>>();
            Func<string, Play> sp = n => _book.AllPlays.First(p => p.Side == PlaySide.Special && p.Name == n);
            kicks.Add(new KeyValuePair<string, KeyValuePair<Play, Play>>("kickoff", new KeyValuePair<Play, Play>(sp("Kickoff Deep Middle"), sp("Kick Return Middle Wedge"))));
            kicks.Add(new KeyValuePair<string, KeyValuePair<Play, Play>>("fieldgoal", new KeyValuePair<Play, Play>(sp("Field Goal"), sp("Field Goal Block Middle"))));
            var punt = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Special && p.Category == PlayCategory.Punt); var pret = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Special && p.Category == PlayCategory.PuntReturn);
            if (punt != null && pret != null) kicks.Add(new KeyValuePair<string, KeyValuePair<Play, Play>>("punt", new KeyValuePair<Play, Play>(punt, pret)));
            foreach (var kv in kicks)
            {
                GameState gs = kv.Key == "kickoff" ? new GameState(_rules, Team.Away) : (kv.Key == "fieldgoal" ? Scrim(80, 4, 5) : Scrim(30, 4, 8));
                var sim = NewSim(kv.Value.Key, kv.Value.Value, gs, 77);
                var frame = new PlayFrame(true);
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep();
                int kicker = 0; for (int i = 0; i < 22; i++) if (sim.Agents[i].PlayPos == Position.K || (kv.Key == "punt" && sim.Agents[i].PlayPos == Position.P)) { kicker = i; break; }
                for (int f = 0; f < 90; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                var kv0 = _stage.Views[kicker]; var b0 = GetBones(kv0);
                Vector3 ball0 = _stage.Ball.Position; Vector3 kfwd = kv0.Forward; Vector3 kright = Vector3.Cross(Vector3.up, kfwd);
                Vector3 dirToBall = ball0 - kv0.transform.position; dirToBall.y = 0f;
                // direction the kick goes: downfield of the kicker (toward the opponent goal) in the frame
                Vector3 downfield = frame.DirToUnity(new Vec2(0f, 1f));
                Vector3 downRight = Vector3.Cross(Vector3.up, downfield);
                float latPre = Vector3.Dot(kv0.transform.position - ball0, downRight);
                float backPre = Vector3.Dot(ball0 - kv0.transform.position, downfield);
                _report.AppendLine("KICK " + kv.Key + ": pre-snap kicker lateral offset from ball = " + latPre.ToString("0.00") + " m (negative = kicker stands to the LEFT of the ball seen from behind the kicker looking downfield), distance behind ball " + backPre.ToString("0.00") + " m, facing yaw " + kv0.Yaw.ToString("0") + ", stance " + kv0.Stance.Kind + ", clip " + (kv0.Rig != null ? kv0.Rig.CurrentClipName : ""));
                Add("kicker pre-snap lateral offset m (neg=left) " + kv.Key, latPre);
                sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
                float t = 0f; float peakL = 0, peakR = 0; float tPeakL = 0, tPeakR = 0; Vector3 pl = b0.LF.position, pr = b0.RF.position; float contactT = -1f; float latAtContact = 0; bool kickSeen = false;
                float maxLatSwing = 0; float firstApproach = -1f; float latApproachStart = 0;
                while (sim.Phase != PlayPhase.Dead && t < 6f)
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); t += Dt; yield return null;
                    if (b0.LF == null) break;
                    float sl = ((b0.LF.position - pl).magnitude) / Dt, sr = ((b0.RF.position - pr).magnitude) / Dt; pl = b0.LF.position; pr = b0.RF.position;
                    if (sl > peakL) { peakL = sl; tPeakL = t; }
                    if (sr > peakR) { peakR = sr; tPeakR = t; }
                    if (!kickSeen && sim.Log.Has(SimEventType.Kick)) { kickSeen = true; contactT = t; Vector3 bp = _stage.Ball.Position; latAtContact = Vector3.Dot(kv0.transform.position - bp, downRight); _report.AppendLine("   at contact: kicker lateral offset " + latAtContact.ToString("0.00") + " m from ball, speed L foot " + sl.ToString("0.0") + " R foot " + sr.ToString("0.0") + " m/s, right foot Y " + (b0.RF.position.y).ToString("0.00") + " left foot Y " + b0.LF.position.y.ToString("0.00")); }
                    if (firstApproach < 0 && sim.Agents[kicker].Action == AgentAction.Kick) { firstApproach = t; Vector3 bp = _stage.Ball.Position; latApproachStart = Vector3.Dot(kv0.transform.position - bp, downRight); _report.AppendLine("   approach (Action=Kick) starts at t=" + t.ToString("0.00") + " lateral offset " + latApproachStart.ToString("0.00") + " m, distance behind ball " + Vector3.Dot(bp - kv0.transform.position, downfield).ToString("0.00") + " m, clip " + (kv0.Rig != null ? kv0.Rig.CurrentClipName : "") + " DebugFlow " + kv0.DebugFlow); }
                    if (kickSeen && t > contactT + 0.6f) break;
                }
                _report.AppendLine("   peak foot speed: LEFT " + peakL.ToString("0.0") + " m/s at t=" + tPeakL.ToString("0.00") + ", RIGHT " + peakR.ToString("0.0") + " m/s at t=" + tPeakR.ToString("0.00") + " ; kick event at t=" + contactT.ToString("0.00") + " => kicking foot appears to be " + (peakR >= peakL ? "RIGHT" : "LEFT"));
                _stage.End();
                yield return null;
            }
        }

        // ------------------------------------------------------------------------------------------ summary
        void WriteSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Engine audit: animated-body metrics (Unity, PlayStage, 60 Hz)\n");
            foreach (var k in _acc.Keys.OrderBy(x => x))
            {
                var l = _acc[k]; l.Sort();
                Func<double, double> q = p => l[Math.Min(l.Count - 1, (int)(p * l.Count))];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-70} n={1,6} mean {2,8:F3} p05 {3,8:F3} p50 {4,8:F3} p95 {5,8:F3} p99 {6,8:F3} max {7,8:F3}", k, l.Count, l.Average(), q(.05), q(.5), q(.95), q(.99), l[l.Count - 1]));
            }
            sb.AppendLine("\n## gait bins: key | seconds | steps/s | stride length m (distance per step) | flight-phase fraction");
            foreach (var kv in _gait.OrderBy(x => x.Key))
            {
                var g = kv.Value; if (g[0] < 0.5) continue;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-50} t={1,6:F1}s steps/s {2,5:F2} stride {3,5:F2} m flight {4,5:F2}", kv.Key, g[0], g[1] / g[0], g[1] > 0 ? g[2] / g[1] : 0.0, g[3] / g[0]));
            }
            sb.AppendLine("\n## counters");
            foreach (var kv in _hist.OrderBy(x => x.Key)) if (!kv.Key.StartsWith("clip-in-use") && !kv.Key.StartsWith("clip: ")) sb.AppendLine(kv.Key + " = " + kv.Value);
            File.WriteAllText(_out + "/motion_summary.txt", sb.ToString());
            FormAuditHook.Write(_out); CutProbe.Write(_out); File.WriteAllText(_out + "/leg_quality.md", "# Leg quality in real plays (final pose, every running player each frame)" + System.Environment.NewLine + System.Environment.NewLine + LegProbe.Table(_legAcc));
            var sb2 = new StringBuilder("# clip usage per sim action: group action :: clip -> frames\n");
            foreach (var kv in _hist.OrderBy(x => x.Key)) if (kv.Key.StartsWith("clip-in-use")) sb2.AppendLine(kv.Key + " = " + kv.Value);
            File.WriteAllText(_out + "/clip_usage.txt", sb2.ToString());
            var sb3 = new StringBuilder("# clip switches: group action -> clip : count\n");
            foreach (var kv in _hist.OrderBy(x => x.Key)) if (kv.Key.StartsWith("clip: ")) sb3.AppendLine(kv.Key + " = " + kv.Value);
            File.WriteAllText(_out + "/clip_switches.txt", sb3.ToString());
            File.WriteAllLines(_out + "/pops.csv", new[] { "play,t,agent,pos,action,clip,kind,value" }.Concat(_pops));
        }

        // ------------------------------------------------------------------------------------------ strips
        sealed class Timeline { public float QbThrow = -1, Catch = -1, Handoff = -1, Tackle = -1, Kick = -1, End = 0; public int Receiver = -1, Runner = -1, Tackler = -1, Carrier = -1; public List<PlayEvent> Events = new List<PlayEvent>(); public bool Sack; public float SnapSimTime; }

        Timeline Dry(Play off, Play def, GameState gs, int seed)
        {
            var sim = NewSim(off, def, gs, seed);
            for (int f = 0; f < 90; f++) sim.Tick(Dt, default(ControlInput), -1);
            sim.Tick(Dt, new ControlInput { Snap = true }, -1);
            var tl = new Timeline { SnapSimTime = sim.Time };
            int guard = 0;
            while (sim.Phase != PlayPhase.Dead && guard++ < 1500) sim.Tick(Dt, default(ControlInput), -1);
            tl.End = sim.Time - tl.SnapSimTime;
            tl.Events = sim.Log.Events.ToList();
            foreach (var e in sim.Log.Events)
            {
                float rt = e.Time - tl.SnapSimTime;
                if (e.Type == SimEventType.Throw && tl.QbThrow < 0) { tl.QbThrow = rt; tl.Receiver = e.AgentB; }
                if (e.Type == SimEventType.Catch && tl.Catch < 0) { tl.Catch = rt; tl.Receiver = e.AgentA >= 0 ? e.AgentA : tl.Receiver; }
                if (e.Type == SimEventType.Handoff && tl.Handoff < 0) { tl.Handoff = rt; tl.Runner = e.AgentB >= 0 ? e.AgentB : e.AgentA; }
                if ((e.Type == SimEventType.Tackle || e.Type == SimEventType.Sack) && tl.Tackle < 0) { tl.Tackle = rt; tl.Tackler = e.AgentA; tl.Carrier = e.AgentB; tl.Sack = e.Type == SimEventType.Sack; }
                if (e.Type == SimEventType.Kick && tl.Kick < 0) tl.Kick = rt;
            }
            return tl;
        }

        enum Cam { Side, FrontLeft, Behind, Top }

        IEnumerator Strips()
        {
            Func<Func<Play, bool>, Play> pick = pr => _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Offense && pr(p));
            Func<Play, QbAssignment> qa = p => p.Assignments[0] as QbAssignment;
            var uc5 = pick(p => qa(p) != null && qa(p).Action == QbAction.DropbackPass && !qa(p).Shotgun && qa(p).DropbackSteps == 5 && p.Category != PlayCategory.Kneel);
            var uc3 = pick(p => qa(p) != null && qa(p).Action == QbAction.DropbackPass && !qa(p).Shotgun && qa(p).DropbackSteps == 3);
            var uc7 = pick(p => qa(p) != null && qa(p).Action == QbAction.DropbackPass && !qa(p).Shotgun && qa(p).DropbackSteps >= 7);
            var sgQ = pick(p => qa(p) != null && (qa(p).Action == QbAction.QuickPass || qa(p).Action == QbAction.DropbackPass) && qa(p).Shotgun);
            var hoUc = pick(p => qa(p) != null && qa(p).Action == QbAction.Handoff && !qa(p).Shotgun);
            var hoSg = pick(p => qa(p) != null && qa(p).Action == QbAction.Handoff && qa(p).Shotgun);
            var roll = pick(p => qa(p) != null && (qa(p).Action == QbAction.Rollout || qa(p).Action == QbAction.Boot));
            var pa = pick(p => qa(p) != null && qa(p).Action == QbAction.PlayActionFake);
            var outside = pick(p => p.Category == PlayCategory.OutsideRun);
            var screen = pick(p => p.Category == PlayCategory.Screen);
            var deep = pick(p => p.Category == PlayCategory.DeepPass);
            var toss = pick(p => qa(p) != null && qa(p).Action == QbAction.Toss);
            var kneel = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Offense && p.Category == PlayCategory.Kneel); var spike = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Offense && p.Category == PlayCategory.Spike);
            var jobs = new List<Action>();
            // (name, play, who, camera set, marker builder)
            var list = new List<object[]>
            {
                new object[] { "qb_uc5", uc5, "qb", new[] { Cam.Side, Cam.FrontLeft } },
                new object[] { "qb_uc3", uc3, "qb", new[] { Cam.Side, Cam.FrontLeft } },
                new object[] { "qb_uc7", uc7, "qb", new[] { Cam.Side } },
                new object[] { "qb_shotgun", sgQ, "qb", new[] { Cam.Side, Cam.FrontLeft } },
                new object[] { "qb_rollout", roll, "qb", new[] { Cam.Side, Cam.FrontLeft } },
                new object[] { "qb_playaction", pa, "qb", new[] { Cam.Side } },
                new object[] { "rb_handoff_uc", hoUc, "runner", new[] { Cam.Side, Cam.Behind } },
                new object[] { "rb_handoff_sg", hoSg, "runner", new[] { Cam.Side } },
                new object[] { "rb_outside_run", outside, "runner", new[] { Cam.Side, Cam.Behind } },
                new object[] { "wr_route_catch", sgQ, "receiver", new[] { Cam.Side, Cam.FrontLeft } },
                new object[] { "wr_deep_catch", deep, "receiver", new[] { Cam.Side } },
                new object[] { "ol_pass_set", uc5, "lg", new[] { Cam.Side } },
                new object[] { "dl_rush", uc5, "dl", new[] { Cam.Side } },
                new object[] { "cb_cover", sgQ, "cb", new[] { Cam.Side, Cam.Behind } },
                new object[] { "tackle_carrier", outside, "carrier_tackle", new[] { Cam.Side, Cam.FrontLeft } },
                new object[] { "tackle_tackler", outside, "tackler", new[] { Cam.Side } },
                new object[] { "screen", screen, "runner", new[] { Cam.Side } },
                new object[] { "qb_toss", toss, "qb", new[] { Cam.Side } },
                new object[] { "qb_kneel", kneel, "qb", new[] { Cam.Side } },
                new object[] { "qb_spike", spike, "qb", new[] { Cam.Side } },
            };
            int seedBase = 900;
            foreach (var item in list)
            {
                string name = (string)item[0]; var off = (Play)item[1]; string who = (string)item[2]; var cams = (Cam[])item[3];
                { string only = EngineAudit.Arg("-pixEngineAuditStripOnly", ""); if (only.Length > 0 && Array.IndexOf(only.Split(','), name) < 0) { seedBase++; continue; } }      // v0.9.0 QBSTANCE: render only the named strips
                if (off == null) { _report.AppendLine("strip " + name + ": no matching play"); continue; }
                var def = DefFor(off);
                int seed = seedBase++;
                Timeline tl = Dry(off, def, Scrim(35, 1, 10), seed);
                _report.AppendLine("strip " + name + ": " + off.Name + " [" + off.FormationName + "] vs " + def.Name + " throw " + tl.QbThrow.ToString("0.00") + " catch " + tl.Catch.ToString("0.00") + " handoff " + tl.Handoff.ToString("0.00") + " tackle " + tl.Tackle.ToString("0.00") + " end " + tl.End.ToString("0.00"));
                foreach (var cam in cams)
                    yield return OneStrip(name, off, def, seed, tl, who, cam);
            }
            // kicks
            Func<string, Play> sp = n => _book.AllPlays.First(p => p.Side == PlaySide.Special && p.Name == n);
            string onlyK = EngineAudit.Arg("-pixEngineAuditStripOnly", ""); string[] onlyKs = onlyK.Length > 0 ? onlyK.Split(',') : null;
            if (onlyKs == null || Array.IndexOf(onlyKs, "kick_fieldgoal") >= 0) yield return KickStrip("kick_fieldgoal", sp("Field Goal"), sp("Field Goal Block Middle"), Scrim(80, 4, 5));
            if (onlyKs == null || Array.IndexOf(onlyKs, "kick_kickoff") >= 0) yield return KickStrip("kick_kickoff", sp("Kickoff Deep Middle"), sp("Kick Return Middle Wedge"), new GameState(_rules, Team.Away));
            var pu = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Special && p.Category == PlayCategory.Punt); var pr2 = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Special && p.Category == PlayCategory.PuntReturn);
            if (pu != null && pr2 != null && (onlyKs == null || Array.IndexOf(onlyKs, "kick_punt") >= 0)) yield return KickStrip("kick_punt", pu, pr2, Scrim(30, 4, 8));
        }

        int FocalIndex(PlaySimulation sim, Timeline tl, string who)
        {
            switch (who)
            {
                case "qb": return 0;
                case "runner": return tl.Runner >= 0 ? tl.Runner : 1;
                case "receiver": return tl.Receiver >= 0 ? tl.Receiver : 8;
                case "carrier_tackle": return tl.Carrier >= 0 ? tl.Carrier : 1;
                case "tackler": return tl.Tackler >= 0 ? tl.Tackler : 15;
                case "lg": for (int i = 0; i < 11; i++) if (sim.Agents[i].PlayPos == Position.LG) return i; return 3;
                case "dl": for (int i = 11; i < 22; i++) if (PositionGroup.IsDefensiveLine(sim.Agents[i].PlayPos)) return i; return 11;
                case "cb": for (int i = 11; i < 22; i++) if (sim.Agents[i].PlayPos == Position.CB) return i; return 17;
            }
            return 0;
        }

        float[] Marks(Timeline tl, string who)
        {
            var m = new List<float> { -0.3f, 0f, 0.2f, 0.4f };
            switch (who)
            {
                case "qb":
                    if (tl.QbThrow > 0) { m.AddRange(new[] { tl.QbThrow * 0.35f, tl.QbThrow * 0.6f, tl.QbThrow - 0.45f, tl.QbThrow - 0.25f, tl.QbThrow - 0.1f, tl.QbThrow, tl.QbThrow + 0.12f, tl.QbThrow + 0.4f }); }
                    else m.AddRange(new[] { 0.7f, 1.0f, 1.4f, 1.8f, 2.2f, 2.6f, 3.0f, 3.4f });
                    break;
                case "runner":
                    if (tl.Handoff > 0) m.AddRange(new[] { tl.Handoff - 0.3f, tl.Handoff - 0.1f, tl.Handoff, tl.Handoff + 0.2f, tl.Handoff + 0.5f, tl.Handoff + 0.9f, tl.Handoff + 1.4f, Mathf.Max(tl.Handoff + 1.9f, tl.Tackle - 0.3f) });
                    else m.AddRange(new[] { 0.7f, 1.0f, 1.4f, 1.8f, 2.2f, 2.6f, 3.0f, 3.4f });
                    break;
                case "receiver":
                    if (tl.QbThrow > 0 && tl.Catch > 0) m.AddRange(new[] { tl.QbThrow * 0.5f, tl.QbThrow, (tl.QbThrow + tl.Catch) * 0.5f, tl.Catch - 0.3f, tl.Catch - 0.15f, tl.Catch, tl.Catch + 0.15f, tl.Catch + 0.4f, tl.Catch + 0.8f });
                    else m.AddRange(new[] { 0.7f, 1.0f, 1.4f, 1.8f, 2.2f, 2.6f, 3.0f, 3.4f });
                    break;
                case "carrier_tackle": case "tackler":
                    if (tl.Tackle > 0) m.AddRange(new[] { tl.Tackle - 1.0f, tl.Tackle - 0.6f, tl.Tackle - 0.35f, tl.Tackle - 0.15f, tl.Tackle, tl.Tackle + 0.2f, tl.Tackle + 0.5f, tl.Tackle + 1.0f });
                    else m.AddRange(new[] { 0.7f, 1.0f, 1.4f, 1.8f, 2.2f, 2.6f, 3.0f, 3.4f });
                    break;
                default: m.AddRange(new[] { 0.6f, 0.9f, 1.2f, 1.5f, 1.8f, 2.1f, 2.5f, 3.0f }); break;
            }
            var l = m.Where(x => x >= -0.3f && x <= tl.End + 0.8f).Distinct().OrderBy(x => x).ToList();
            while (l.Count > 12) l.RemoveAt(l.Count / 2);
            return l.ToArray();
        }

        IEnumerator OneStrip(string name, Play off, Play def, int seed, Timeline tl, string who, Cam cam)
        {
            var sim = NewSim(off, def, Scrim(35, 1, 10), seed);
            var frame = new PlayFrame(true);
            _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep();
            for (int f = 0; f < 90; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
            int focal = FocalIndex(sim, tl, who);
            var marks = Marks(tl, who);
            var tiles = new List<Texture2D>(); int mi = 0;
            var idx = new StringBuilder(name + " " + cam + " focal agent " + focal + " (" + sim.Agents[focal].PlayPos + "): marks (s after snap) ");
            if (marks.Length > 0 && marks[0] < 0f) { yield return null; tiles.Add(StripShot(frame, focal, cam, sim)); idx.Append(marks[0].ToString("0.00") + " "); mi = 1; }
            sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
            float t = 0f;
            while (sim.Phase != PlayPhase.Dead && mi < marks.Length && t < tl.End + 1.2f)
            {
                sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); t += Dt; yield return null;
                if (t >= marks[mi]) { tiles.Add(StripShot(frame, focal, cam, sim)); idx.Append(t.ToString("0.00") + (_stage.Views[focal].Rig != null ? "[" + _stage.Views[focal].Rig.CurrentClipName + "|" + sim.Agents[focal].Action + "|" + _stage.Views[focal].PoseDebug + "] " : " ")); mi++; }
            }
            // after the whistle for tackles
            while (mi < marks.Length && marks[mi] > t && t < tl.End + 1.5f && (who.Contains("tackle") || name == "qb_kneel" || name == "qb_spike"))
            {
                sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); t += Dt; yield return null;
                if (t >= marks[mi]) { tiles.Add(StripShot(frame, focal, cam, sim)); idx.Append(t.ToString("0.00") + " "); mi++; }
            }
            Sheet(tiles, 4, _out + "/strips/" + name + "_" + cam + ".png");
            _report.AppendLine("   " + idx.ToString());
            _stage.End();
            yield return null;
        }

        IEnumerator KickStrip(string name, Play off, Play def, GameState gs)
        {
            var sim = NewSim(off, def, gs, 77);
            var frame = new PlayFrame(true);
            _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep();
            int kicker = 0; for (int i = 0; i < 22; i++) if (sim.Agents[i].PlayPos == Position.K || sim.Agents[i].PlayPos == Position.P) { kicker = i; break; }
            for (int f = 0; f < 90; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
            foreach (var cam in new[] { Cam.Side, Cam.Behind, Cam.Top })
            {
                // each camera needs its own run: re-run the play
                _stage.End(); yield return null;
                sim = NewSim(off, def, gs, 77);
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep();
                for (int f = 0; f < 90; f++) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; }
                var tiles = new List<Texture2D>(); float t = 0f, next = -0.3f;
                tiles.Add(StripShot(frame, kicker, cam, sim));
                sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt);
                bool kicked = false; float afterKick = 0f;
                while (sim.Phase != PlayPhase.Dead && tiles.Count < 12 && t < 6f)
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); t += Dt; yield return null;
                    if (!kicked && sim.Log.Has(SimEventType.Kick)) kicked = true;
                    if (kicked) afterKick += Dt;
                    if (t >= next + (kicked ? 0.12f : 0.3f)) { tiles.Add(StripShot(frame, kicker, cam, sim)); next = t; }
                    if (kicked && afterKick > 0.8f) break;
                }
                Sheet(tiles, 4, _out + "/strips/" + name + "_" + cam + ".png");
            }
            _stage.End();
            yield return null;
        }

        Texture2D StripShot(PlayFrame frame, int agent, Cam cam, PlaySimulation sim)
        {
            var v = _stage.Views[agent]; Vector3 q = v.transform.position; Vector3 fwd = v.Forward; Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 down = frame.DirToUnity(new Vec2(0f, 1f)); Vector3 dr = Vector3.Cross(Vector3.up, down);
            bool defender = agent >= 11;
            Vector3 face = fwd;
            switch (cam)
            {
                case Cam.Side: _cam.transform.position = q + dr * 3.4f + Vector3.up * 1.4f; _cam.transform.rotation = Quaternion.LookRotation(q + Vector3.up * 1.0f + down * 0.4f - _cam.transform.position); _cam.fieldOfView = 40f; break;
                case Cam.FrontLeft: _cam.transform.position = q + face * 3.6f - right * 2.0f + Vector3.up * 1.5f; _cam.transform.rotation = Quaternion.LookRotation(q + Vector3.up * 1.0f - _cam.transform.position); _cam.fieldOfView = 40f; break;
                case Cam.Behind: _cam.transform.position = q - face * 4.2f + right * 0.6f + Vector3.up * 2.0f; _cam.transform.rotation = Quaternion.LookRotation(q + face * 2f + Vector3.up * 0.9f - _cam.transform.position); _cam.fieldOfView = 42f; break;
                default: _cam.transform.position = q + Vector3.up * 9f - down * 0.5f; _cam.transform.rotation = Quaternion.LookRotation(q - _cam.transform.position, down); _cam.fieldOfView = 40f; break;
            }
            var hidden = new List<Renderer>();
            if (EngineAudit.Arg("-pixEngineAuditStripHide", "0") == "1") for (int j = 0; j < 22; j++) { var ov = _stage.Views[j]; if (ov == null || ov == v) continue; foreach (var rr in ov.GetComponentsInChildren<Renderer>()) if (rr.enabled) { rr.enabled = false; hidden.Add(rr); } }      // v0.9.0 QBSTANCE: only the focal player in the frame
            var shot = ShotCam(540, 400);
            foreach (var rr in hidden) rr.enabled = true;
            return shot;
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
