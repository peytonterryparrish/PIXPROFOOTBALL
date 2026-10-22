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
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.6 QA: the Formation Gallery. Plays the real <see cref="PlayStage"/> (the same 22 pooled views, animation graphs and stance logic as a match) on the stadium scene for every
    /// formation in both attack directions, checks facing / stance / pose of every player numerically and renders top-down, behind-the-QB, sideline and line close-up pictures with
    /// facing arrows (green = the simulation's facing, cyan / red = the body as posed) and position labels. Run: <c>Unity -projectPath ... -executeMethod PixFootball.EditorTools.StanceGallery.Run</c>
    /// (no -batchmode needed, -batchmode works without -nographics). Args: <c>-pixGalleryOut dir</c> <c>-pixGalleryMode formations|camera|all</c> <c>-pixGalleryShots 0|1</c> <c>-pixGalleryOnly text</c>.
    /// </summary>
    public static class StanceGallery
    {
        public const string Flag = "-pixGallery";
        public static string Arg(string name, string def)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return def;
        }
        public static bool Has(string name) { return Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0; }

        public static void Run()
        {
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath);
            EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has(Flag)) return;
            var go = new GameObject("PixStanceGallery");
            go.AddComponent<StanceGalleryRunner>();
        }
    }

    /// <summary>One numeric check result.</summary>
    public struct GalleryRow
    {
        public string Scenario, Direction, Phase, Position, Label, Check, Expected, Actual;
        public int Slot; public bool Pass;
    }

    public sealed class StanceGalleryRunner : MonoBehaviour
    {
        string _out;
        bool _shots;
        string _only;
        Camera _cam;
        PlayStage _stage;
        Playbook _book;
        RuleSet _rules;
        readonly List<GalleryRow> _rows = new List<GalleryRow>();
        readonly List<GameObject> _gizmos = new List<GameObject>();
        Material _unlit;
        int _playIndex;
        int _imgCount;

        public static readonly UniformSet KitA = Kit(TeamColors.Home, true), KitB = Kit(TeamColors.Away, false);
        static UniformSet Kit(TeamColors c, bool home) { var u = new UniformSet(); u.Uniform = c; u.Helmet = c; u.Home = home; return u; }

        sealed class Scn { public string Name, Group; public Play Off, Def; public GameState Gs; public bool Snap = true; }

        IEnumerator Start()
        {
            Time.captureFramerate = 60;
            _out = Path.GetFullPath(StanceGallery.Arg("-pixGalleryOut", "Docs/QA/stance_gallery"));
            Directory.CreateDirectory(_out);
            _shots = StanceGallery.Arg("-pixGalleryShots", "1") == "1";
            _only = StanceGallery.Arg("-pixGalleryOnly", "");
            string mode = StanceGallery.Arg("-pixGalleryMode", "formations");
            Application.logMessageReceived += (c, st, t) => { if (t == LogType.Exception || t == LogType.Error) Debug.Log("[Gallery] logged " + t + ": " + c); };
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            // the stadium scene also hosts the free-roam game (a player, its camera rig with a Cinemachine brain): out of the way
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) Destroy(pc.gameObject);
            foreach (var fr in FindObjectsByType<FreeRoamGame>(FindObjectsSortMode.None)) fr.enabled = false;
            foreach (var br in FindObjectsByType<Unity.Cinemachine.CinemachineBrain>(FindObjectsSortMode.None)) br.enabled = false;
            var camGo = new GameObject("GalleryCam");
            _cam = camGo.AddComponent<Camera>(); var hdc = camGo.AddComponent<HDAdditionalCameraData>();
            // no motion blur / depth of field in the gallery pictures (every shot is a cut)
            hdc.customRenderingSettings = true;
            { var fs = hdc.renderingPathCustomFrameSettings; var mk = hdc.renderingPathCustomFrameSettingsOverrideMask; foreach (var ff in new[] { FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField }) { mk.mask[(uint)ff] = true; fs.SetEnabled(ff, false); } hdc.renderingPathCustomFrameSettings = fs; hdc.renderingPathCustomFrameSettingsOverrideMask = mk; }
            _cam.nearClipPlane = 0.2f; _cam.farClipPlane = 1500f; _cam.allowHDR = true; _cam.tag = "MainCamera";
            _unlit = new Material(Shader.Find("HDRP/Unlit")); _unlit.SetColor("_UnlitColor", Color.white);
            _book = DefaultPlaybooks.Create(); _rules = RuleSet.Pro();
            SimAgentView.DebugNoClipFacing = StanceGallery.Has("-pixNoClipFacing");
            _stage = new GameObject("GalleryStage").AddComponent<PlayStage>();
            _stage.Init();
            yield return null;
            int exit = 0;


            if (mode == "formations" || mode == "all") { yield return Formations(); yield return Celebrations(); }
            if (mode == "camera" || mode == "all") yield return CameraSequences();
            if (mode == "hud" || mode == "all") yield return HudShots();
            WriteResults();
            Debug.Log("[Gallery] done: " + _rows.Count + " checks, images " + _imgCount);
            yield return null;
            EditorApplication.Exit(exit);
        }

        /// <summary>The match views are posed only while a camera sees them (CullUpdateTransforms); the gallery measures bones, so everybody animates.</summary>
        void AlwaysAnimate()
        {
            foreach (var v in _stage.Views)
            {
                if (v == null || v.Avatar == null || v.Avatar.Animator == null) continue;
                v.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var smr in v.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;   // skinned meshes of unseen players are not updated otherwise
            }
        }

        // ============================================================================================ scenarios
        List<Scn> BuildScenarios()
        {
            var list = new List<Scn>();
            var offForms = _book.Formations.Where(f => f.Side == PlaySide.Offense).ToList();
            var defForms = _book.Formations.Where(f => f.Side == PlaySide.Defense).ToList();
            Func<string, Play> offPlay = fn => _book.AllPlays.First(p => p.Side == PlaySide.Offense && p.FormationName == fn && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike);
            Func<string, Play> defPlay = fn => _book.AllPlays.First(p => p.Side == PlaySide.Defense && p.FormationName == fn);
            for (int i = 0; i < offForms.Count; i++)
            {
                var f = offForms[i]; var d = defForms[i % defForms.Count];
                bool gl = f.Name == "Goal Line";
                list.Add(new Scn { Name = "OFF " + f.Name + " vs " + d.Name, Group = "offense", Off = offPlay(f.Name), Def = defPlay(d.Name), Gs = Scrim(gl ? 96 : 35, 1, gl ? 4 : 10) });
            }
            for (int j = 0; j < defForms.Count; j++)
            {
                var d = defForms[j]; var f = offForms[(j * 3 + 1) % offForms.Count];
                bool gl = d.Name.StartsWith("Goal Line");
                list.Add(new Scn { Name = "DEF " + d.Name + " vs " + f.Name, Group = "defense", Off = offPlay(f.Name), Def = defPlay(d.Name), Gs = Scrim(gl ? 96 : 35, 1, gl ? 4 : 10) });
            }
            Func<string, Play> sp = n => _book.AllPlays.First(p => p.Side == PlaySide.Special && p.Name == n);
            list.Add(new Scn { Name = "ST Kickoff Deep Middle", Group = "special", Off = sp("Kickoff Deep Middle"), Def = sp("Kick Return Middle Wedge"), Gs = new GameState(_rules, Team.Away) });
            list.Add(new Scn { Name = "ST Kickoff Squib / Return Left", Group = "special", Off = sp("Kickoff Squib"), Def = sp("Kick Return Left"), Gs = new GameState(_rules, Team.Away) });
            list.Add(new Scn { Name = "ST Onside Kick Short", Group = "special", Off = sp("Onside Kick Short"), Def = sp("Kick Return Right"), Gs = new GameState(_rules, Team.Away) });
            list.Add(new Scn { Name = "ST Punt Standard vs Return Set", Group = "special", Off = sp("Punt Standard"), Def = sp("Punt Return Set"), Gs = Scrim(30, 4, 8) });
            list.Add(new Scn { Name = "ST Punt Directional Left vs Return Safe", Group = "special", Off = sp("Punt Directional Left"), Def = sp("Punt Return Safe"), Gs = Scrim(30, 4, 8) });
            list.Add(new Scn { Name = "ST Field Goal vs Block Middle", Group = "special", Off = sp("Field Goal"), Def = sp("Field Goal Block Middle"), Gs = Scrim(80, 4, 5) });
            list.Add(new Scn { Name = "ST Field Goal vs Block Edge", Group = "special", Off = sp("Field Goal"), Def = sp("Field Goal Block Edge"), Gs = Scrim(70, 4, 5) });
            var pat = new GameState(_rules); pat.Phase = GamePhase.PointAfter; pat.Possession = Team.Home; pat.BallSpot = 98;
            list.Add(new Scn { Name = "ST Extra Point vs Block Middle", Group = "special", Off = _book.AllPlays.First(p => p.Side == PlaySide.Special && p.Category == PlayCategory.PointAfter), Def = sp("Field Goal Block Middle"), Gs = pat });
            var two = new GameState(_rules); two.Phase = GamePhase.PointAfter; two.Possession = Team.Home; two.BallSpot = 98;
            list.Add(new Scn { Name = "ST Two-point try (Shotgun vs Goal Line)", Group = "special", Off = offPlay("Shotgun"), Def = defPlay(defForms.First(f => f.Name.StartsWith("Goal Line")).Name), Gs = two });
            var kn = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Offense && p.Category == PlayCategory.Kneel);
            if (kn != null) list.Add(new Scn { Name = "OFF Kneel", Group = "special", Off = kn, Def = defPlay(defForms[0].Name), Gs = Scrim(40, 1, 10) });
            var spk = _book.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Offense && p.Category == PlayCategory.Spike);
            if (spk != null) list.Add(new Scn { Name = "OFF Spike", Group = "special", Off = spk, Def = defPlay(defForms[0].Name), Gs = Scrim(40, 1, 10) });
            if (!string.IsNullOrEmpty(_only)) list = list.Where(s => s.Name.IndexOf(_only, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            return list;
        }

        GameState Scrim(int spot, int down, int dist)
        {
            var gs = new GameState(_rules); gs.Phase = GamePhase.Scrimmage; gs.BallSpot = spot; gs.Down = down; gs.Distance = dist; gs.Possession = Team.Home;
            return gs;
        }

        PlaySimulation MakeSim(Scn s, int seed, bool autoSnap)
        {
            var sim = new PlaySimulation();
            sim.Setup(s.Gs.Clone(), s.Off, s.Def, null, null, _rules, new Rng(seed), new PlaySimOptions { Playbook = _book, Penalties = false, AutoSnap = autoSnap, AutoSnapDelay = 99f });
            return sim;
        }

        // ============================================================================================ formations
        IEnumerator Formations()
        {
            var scns = BuildScenarios();
            Debug.Log("[Gallery] " + scns.Count + " scenarios x 2 directions");
            int n = 0;
            foreach (var sc in scns)
            {
                foreach (bool plus in new[] { true, false })
                {
                    yield return RunScenario(sc, plus, 100 + (n++));
                }
            }
        }

        IEnumerator RunScenario(Scn sc, bool plus, int seed)
        {
            var frame = new PlayFrame(plus);
            var sim = MakeSim(sc, seed, false);
            _stage.Begin(sim, frame, KitA, KitB, ++_playIndex);
            AlwaysAnimate();
            string dir = plus ? "+Z" : "-Z";
            const float dt = 1f / 60f;
            // pre-snap settle: the formation is on the field, the stance clips fade in
            for (int f = 0; f < 80; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }
            yield return null;
            CheckPreSnap(sc, dir, sim, frame);
            if (_shots) yield return Shots(sc, dir, sim, frame, "presnap");
            ClearGizmos();
            if (sc.Snap && (sim.Kind != PlayKind.Kneel))
            {
                sim.Tick(dt, new ControlInput { Snap = true }, -1); _stage.Present(dt);
                for (int f = 0; f < 36; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }
                yield return null;
                CheckLive(sc, dir, sim, frame, "snap+0.6s");
                if (_shots) yield return TopShot(sc, dir, sim, frame, "snap06");
                ClearGizmos();
                for (int f = 0; f < 54 && sim.Phase != PlayPhase.Dead; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }
                yield return null;
                CheckLive(sc, dir, sim, frame, "snap+1.5s");
                if (_shots && !sc.Name.StartsWith("DEF")) yield return TopShot(sc, dir, sim, frame, "snap15");
                ClearGizmos();
            }
            _stage.End();
            yield return null;
        }

        // ============================================================================================ checks
        void Add(Scn sc, string dir, string phase, SimAgent a, string check, string expected, string actual, bool pass)
        {
            _rows.Add(new GalleryRow { Scenario = sc.Name, Direction = dir, Phase = phase, Slot = a.Id, Position = a.PlayPos.ToString(), Label = a.Label, Check = check, Expected = expected, Actual = actual, Pass = pass });
        }

        public static StanceSpec SpecOf(PlaySimulation sim, SimAgent a, bool shotgun)
        {
            float relX = a.StartPos.X - sim.BallX0, depth = a.StartPos.Y - sim.LosY;
            string unit = a.Side == SimSide.Offense ? sim.OffForm.Unit : sim.DefForm.Unit;
            return StanceRules.For(a.PlayPos, a.Side, sim.Kind, unit, relX, depth, shotgun, a.ReturnerRole);
        }

        void CheckPreSnap(Scn sc, string dir, PlaySimulation sim, PlayFrame frame)
        {
            var qbForm = sim.OffForm != null && sim.OffForm.Slots.Length > 0 ? sim.OffForm.Slots[0] : null;
            bool shotgun = qbForm != null && qbForm.Y < -2.5f;
            var lib = ArtLibrary.Load();
            for (int i = 0; i < 22; i++)
            {
                var a = sim.Agents[i]; var v = _stage.Views[i];
                if (v == null) { Add(sc, dir, "presnap", a, "VIEW", "view", "missing", false); continue; }
                Vec2 exp = StanceRules.ExpectedPreSnapFacing(a.Side);
                if (sim.Kind != PlayKind.Scrimmage && sim.Kind != PlayKind.Kneel && sim.Kind != PlayKind.Spike && a.Side == SimSide.Defense) exp = new Vec2(0f, -1f);
                float simAng = StanceRules.AngleDeg(a.Facing, exp);
                Add(sc, dir, "presnap", a, "SIM_FACING", "<=" + StanceRules.FacingToleranceDeg + "deg of " + (a.Side == SimSide.Offense ? "+Y" : "-Y"), simAng.ToString("0") + "deg", simAng <= StanceRules.FacingToleranceDeg);
                Vector3 ew = frame.DirToUnity(exp);
                float expYaw = StanceRules.YawOf(ew.x, ew.z);
                var pose = BodyHeading.Measure(v.Avatar != null ? v.Avatar.Animator : null);
                if (pose.Valid)
                {
                    float d = Mathf.Abs(StanceRules.YawDelta(pose.Yaw, expYaw));
                    Add(sc, dir, "presnap", a, "VISUAL_FACING", "<=" + (StanceRules.FacingToleranceDeg + 5f) + "deg of yaw " + expYaw.ToString("0"), "body yaw " + pose.Yaw.ToString("0") + " (hips " + pose.HipsYaw.ToString("0") + ", shoulders " + pose.ShouldersYaw.ToString("0") + ", root " + v.Yaw.ToString("0") + ")", d <= StanceRules.FacingToleranceDeg + 5f);
                    Add(sc, dir, "presnap", a, "NOT_TPOSE", "hand span < 1.5m", pose.HandSpan.ToString("0.00") + "m", pose.HandSpan < 1.5f);
                    var spec = SpecOf(sim, a, shotgun);
                    StanceCheck(sc, dir, a, v, spec, pose, lib);
                }
                else Add(sc, dir, "presnap", a, "VISUAL_FACING", "valid humanoid pose", "no pose", false);
            }
        }

        void StanceCheck(Scn sc, string dir, SimAgent a, SimAgentView v, StanceSpec spec, BodyPose pose, ArtLibrary lib)
        {
            string[] keys = StanceRules.ClipKeys(spec.Kind, v.Role);
            string playing = v.Rig != null ? v.Rig.CurrentClipName : "";
            string playingBase = playing.EndsWith("_M") ? playing.Substring(0, playing.Length - 2) : playing;   // the mirror-image twin of a clip
            bool clipOk;
            if (keys.Length == 0) clipOk = true;
            else
            {
                clipOk = false;
                foreach (string k in keys) if (lib != null) foreach (var c in lib.ClipVariants(k)) if (c != null && c.name == playingBase) clipOk = true;
            }
            Add(sc, dir, "presnap", a, "STANCE_CLIP", spec.Kind + " " + string.Join("/", keys), string.IsNullOrEmpty(playing) ? "(locomotion idle)" : playing, clipOk);
            switch (spec.Kind)
            {
                case StanceKind.CenterSnap: case StanceKind.ThreePoint: case StanceKind.FourPoint:
                    Add(sc, dir, "presnap", a, "STANCE_LOW", "head <= 1.45m (3-point)", pose.HeadHeight.ToString("0.00") + "m", pose.HeadHeight <= 1.45f);
                    break;
                case StanceKind.LinebackerTwoPoint: case StanceKind.CornerPress: case StanceKind.CornerOff: case StanceKind.ReceiverSplit: case StanceKind.ReceiverSlot: case StanceKind.SafetyDeep: case StanceKind.BackTwoPoint:
                    Add(sc, dir, "presnap", a, "STANCE_LOW", "head <= 1.8m (2-point crouch)", pose.HeadHeight.ToString("0.00") + "m", pose.HeadHeight <= 1.8f);
                    break;
            }
            if (spec.Kind == StanceKind.ReceiverSplit || spec.Kind == StanceKind.ReceiverSlot || spec.Kind == StanceKind.Gunner)
            {
                // outside foot back: the left-of-ball receiver has his left foot back (from the offense's view), the right-of-ball receiver his right foot back
                float lr = pose.LeftFootFwd - pose.RightFootFwd;   // < 0: left foot behind
                bool wantLeftBack = spec.IsLeftOfBall;
                // sim-side mirroring: offense faces +Y; the body yaw already takes the frame into account because the measurement is along the body heading
                // a stance take with both feet level has no "outside foot back": nothing to check
                var ftab = ClipFacingTable.Load(); ClipFacingTable.Entry fe;
                bool neutral = ftab != null && ftab.TryGet(playing, out fe) && fe.footBack == 0;
                bool ok = neutral || Mathf.Abs(lr) < 0.06f || (wantLeftBack ? lr < 0f : lr > 0f);
                Add(sc, dir, "presnap", a, "FOOT_SIDE", "outside foot back (" + (wantLeftBack ? "left" : "right") + ")", "left-right foot offset " + lr.ToString("0.00") + "m", ok);
            }
        }

        void CheckLive(Scn sc, string dir, PlaySimulation sim, PlayFrame frame, string phase)
        {
            for (int i = 0; i < 22; i++)
            {
                var a = sim.Agents[i]; var v = _stage.Views[i];
                if (v == null || v.Avatar == null) continue;
                if (v.ContactDriven || a.IsDown) continue;
                switch (a.Action)
                {
                    case AgentAction.Tackled: case AgentAction.Fallen: case AgentAction.Stumble: case AgentAction.Dive: case AgentAction.Engaged: case AgentAction.Shed:
                    case AgentAction.TackleBigHit: case AgentAction.TackleDive: case AgentAction.TackleGang: case AgentAction.TackleShoestring: case AgentAction.TackleWrapUp:
                    case AgentAction.Jump: case AgentAction.Catch: case AgentAction.Throw: case AgentAction.Spin: case AgentAction.Juke: case AgentAction.Hurdle: case AgentAction.Handoff: case AgentAction.Kick: case AgentAction.Celebrate:
                        continue;
                }
                var pose = BodyHeading.Measure(v.Avatar.Animator);
                if (!pose.Valid) continue;
                Vector3 f = frame.DirToUnity(a.Facing);
                float simYaw = StanceRules.YawOf(f.x, f.z);
                float d = Mathf.Abs(StanceRules.YawDelta(pose.Yaw, v.Yaw));
                string clip = v.Rig != null ? v.Rig.CurrentClipName : "";
                Add(sc, dir, phase, a, "LIVE_BODY_VS_ROOT", "<=35deg between body and root (" + a.Action + ")", d.ToString("0") + "deg" + (clip.Length > 0 ? " clip " + clip : ""), d <= 35f);
                float d2 = Mathf.Abs(StanceRules.YawDelta(v.Yaw, simYaw));
                if (a.Speed < 6f) Add(sc, dir, phase, a, "LIVE_ROOT_VS_SIM", "<=40deg between root yaw and sim facing", d2.ToString("0") + "deg", d2 <= 40f);
                Add(sc, dir, phase, a, "NOT_TPOSE", "hand span < 1.6m", pose.HandSpan.ToString("0.00") + "m", pose.HandSpan < 1.6f);
            }
        }

        // ============================================================================================ rendering
        void ClearGizmos() { foreach (var g in _gizmos) if (g != null) Destroy(g); _gizmos.Clear(); }

        GameObject Bar(Vector3 a, Vector3 b, float w, Color c)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            Vector3 d = b - a; float len = d.magnitude;
            g.transform.position = (a + b) * 0.5f; g.transform.rotation = len > 1e-4f ? Quaternion.LookRotation(d / len) : Quaternion.identity; g.transform.localScale = new Vector3(w, w, len);
            var r = g.GetComponent<Renderer>(); r.sharedMaterial = _unlit; var mpb = new MaterialPropertyBlock(); mpb.SetColor("_UnlitColor", c); r.SetPropertyBlock(mpb);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _gizmos.Add(g); return g;
        }

        void Arrow(Vector3 from, Vector3 dir, float len, float w, Color c)
        {
            dir.y = 0f; if (dir.sqrMagnitude < 1e-6f) return; dir.Normalize();
            Vector3 tip = from + dir * len;
            Bar(from, tip, w, c);
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            Bar(tip, tip - dir * 0.45f + side * 0.28f, w * 1.3f, c); Bar(tip, tip - dir * 0.45f - side * 0.28f, w * 1.3f, c);
        }

        void Label(Vector3 pos, string text, Color c)
        {
            var go = new GameObject("lbl"); var t = go.AddComponent<TextMeshPro>();
            t.text = text; t.fontSize = 3.4f; t.alignment = TextAlignmentOptions.Center; t.color = c; t.textWrappingMode = TextWrappingModes.NoWrap;
            go.transform.position = pos; go.transform.rotation = _cam.transform.rotation;
            _gizmos.Add(go);
        }

        static readonly HumanBodyBones[][] BoneLinks =
        {
            new[] { HumanBodyBones.Hips, HumanBodyBones.Spine }, new[] { HumanBodyBones.Spine, HumanBodyBones.Chest }, new[] { HumanBodyBones.Chest, HumanBodyBones.Neck }, new[] { HumanBodyBones.Neck, HumanBodyBones.Head },
            new[] { HumanBodyBones.Chest, HumanBodyBones.LeftUpperArm }, new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm }, new[] { HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand },
            new[] { HumanBodyBones.Chest, HumanBodyBones.RightUpperArm }, new[] { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm }, new[] { HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand },
            new[] { HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg }, new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg }, new[] { HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot },
            new[] { HumanBodyBones.Hips, HumanBodyBones.RightUpperLeg }, new[] { HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg }, new[] { HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot },
        };

        /// <summary>The animated skeleton of every player as bright bars (the picture of the pose does not depend on the skinned mesh): lines yellow, left hand / foot magenta.</summary>
        void Skeletons()
        {
            for (int i = 0; i < 22; i++)
            {
                var v = _stage.Views[i]; if (v == null || v.Avatar == null || v.Avatar.Animator == null) continue;
                var an = v.Avatar.Animator;
                Color col = i < 11 ? new Color(1f, 0.9f, 0.1f) : new Color(1f, 0.45f, 0.1f);
                foreach (var l in BoneLinks)
                {
                    var a = an.GetBoneTransform(l[0]); var b = an.GetBoneTransform(l[1]);
                    if (a == null || b == null) continue;
                    bool left = l[1].ToString().StartsWith("Left");
                    Bar(a.position, b.position, 0.045f, left && (l[1] == HumanBodyBones.LeftHand || l[1] == HumanBodyBones.LeftFoot) ? new Color(1f, 0.1f, 0.9f) : col);
                }
            }
        }

        void Annotate(PlaySimulation sim, PlayFrame frame, bool labels)
        {
            var qbForm = sim.OffForm.Slots[0]; bool shotgun = qbForm != null && qbForm.Y < -2.5f;
            for (int i = 0; i < 22; i++)
            {
                var a = sim.Agents[i]; var v = _stage.Views[i]; if (v == null) continue;
                Vector3 p = v.transform.position;
                Vector3 sf = frame.DirToUnity(a.Facing);
                Arrow(p + Vector3.up * 2.45f, sf, 1.8f, 0.07f, new Color(0.2f, 1f, 0.2f));
                var pose = BodyHeading.Measure(v.Avatar != null ? v.Avatar.Animator : null);
                bool ok = true;
                if (pose.Valid)
                {
                    Vector3 exp = frame.DirToUnity(StanceRules.ExpectedPreSnapFacing(a.Side));
                    ok = Mathf.Abs(StanceRules.YawDelta(pose.Yaw, StanceRules.YawOf(exp.x, exp.z))) <= StanceRules.FacingToleranceDeg + 5f;
                    float yr = pose.Yaw * Mathf.Deg2Rad;
                    Arrow(p + Vector3.up * 2.2f, new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr)), 1.8f, 0.07f, ok ? new Color(0.1f, 0.9f, 1f) : new Color(1f, 0.15f, 0.15f));
                }
                if (labels)
                {
                    var spec = SpecOf(sim, a, shotgun);
                    Label(p + Vector3.up * 3.05f, (a.Label.Length > 0 ? a.Label : a.PlayPos.ToString()) + "\n" + spec.Kind, ok ? Color.white : new Color(1f, 0.4f, 0.4f));
                }
            }
        }

        void Bounds2(PlaySimulation sim, PlayFrame frame, out Vector3 center, out Vector2 half)
        {
            float x0 = 1e9f, x1 = -1e9f, z0 = 1e9f, z1 = -1e9f;
            for (int i = 0; i < 22; i++) { Vector3 p = frame.ToUnity(sim.Agents[i].Pos); x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); }
            center = new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f); half = new Vector2((x1 - x0) * 0.5f + 3f, (z1 - z0) * 0.5f + 3f);
        }

        IEnumerator Shots(Scn sc, string dir, PlaySimulation sim, PlayFrame frame, string tag)
        {
            yield return TopShot(sc, dir, sim, frame, tag + "_top");
            // behind the QB with the Classic user camera
            {
                var c = new UserCameraInput { ViewDir = frame.DirToUnity(new Vec2(0f, 1f)), Los = frame.ToUnity(new Vec2(sim.BallX0, sim.LosY)), PreSnap = true };
                if (sim.Kind != PlayKind.Scrimmage && sim.Kind != PlayKind.Kneel && sim.Kind != PlayKind.Spike) c.Los = frame.ToUnity(sim.Agents[0].Pos);
                var pose = UserCameraMath.Evaluate(c, UserCameraPreset.Classic(), 0f, 0f, 0f, 1f);
                _cam.transform.SetPositionAndRotation(pose.Position, pose.Rotation); _cam.fieldOfView = pose.Fov;
                yield return Capture(sc, dir, tag + "_qb", true, sim, frame);
            }
            // sideline
            {
                Vector3 ctr; Vector2 half; Bounds2(sim, frame, out ctr, out half);
                Vector3 side = Vector3.Cross(Vector3.up, frame.DirToUnity(new Vec2(0f, 1f)));
                float dist = Mathf.Max(28f, half.y / Mathf.Tan(20f * Mathf.Deg2Rad) * 0.75f + 6f);
                _cam.transform.position = ctr - side * dist + Vector3.up * 3.2f; _cam.transform.rotation = Quaternion.LookRotation(ctr + Vector3.up * 1f - _cam.transform.position); _cam.fieldOfView = 40f;
                yield return Capture(sc, dir, tag + "_side", false, sim, frame);
            }
            // line close-up (scrimmage plays): behind the left guard, low, like the 2K8 stance reference
            if (sim.Kind == PlayKind.Scrimmage)
            {
                Vector3 los = frame.ToUnity(new Vec2(sim.BallX0, sim.LosY));
                Vector3 d = frame.DirToUnity(new Vec2(0f, 1f)), side = Vector3.Cross(Vector3.up, d);
                _cam.transform.position = los - d * 4.2f - side * 2.2f + Vector3.up * 1.5f; _cam.transform.rotation = Quaternion.LookRotation(los + d * 1.5f + side * 3f + Vector3.up * 0.6f - _cam.transform.position); _cam.fieldOfView = 52f;
                yield return Capture(sc, dir, tag + "_line", false, sim, frame);
            }
        }

        IEnumerator TopShot(Scn sc, string dir, PlaySimulation sim, PlayFrame frame, string tag)
        {
            Vector3 ctr; Vector2 half; Bounds2(sim, frame, out ctr, out half);
            float aspect = 16f / 9f, tan = Mathf.Tan(20f * Mathf.Deg2Rad);
            float h = Mathf.Max(half.y / tan, half.x / (tan * aspect)) + 2f;
            Vector3 d = frame.DirToUnity(new Vec2(0f, 1f));
            _cam.transform.position = ctr + Vector3.up * h; _cam.transform.rotation = Quaternion.LookRotation(Vector3.down, d); _cam.fieldOfView = 40f;
            yield return Capture(sc, dir, tag.EndsWith("_top") ? tag : tag + "_top", true, sim, frame);
        }

        IEnumerator Capture(Scn sc, string dir, string tag, bool labels, PlaySimulation sim, PlayFrame frame)
        {
            ClearGizmos();
            Annotate(sim, frame, labels);
            if (tag.EndsWith("_line") || tag.EndsWith("_side") || tag.EndsWith("_qb")) Skeletons();   // the pose as an animated skeleton, independent of the skinned mesh
            yield return null;
            const int W = 1600, H = 900;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _cam.targetTexture = rt;
            _cam.Render(); _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = null; _cam.targetTexture = null; rt.Release(); Destroy(rt);
            string name = Safe(sc.Name) + "_" + (dir == "+Z" ? "plusZ" : "minusZ") + "_" + tag + ".png";
            File.WriteAllBytes(Path.Combine(_out, name), tex.EncodeToPNG()); Destroy(tex); _imgCount++;
            ClearGizmos();
        }

        static string Safe(string s) { var sb = new StringBuilder(); foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_'); return sb.ToString(); }

        // ============================================================================================ post-whistle / celebration facing
        IEnumerator Celebrations()
        {
            var sc = new Scn { Name = "Celebration (run play, teammates jog to the scorer)", Off = _book.AllPlays.First(p => p.Side == PlaySide.Offense && p.Category == PlayCategory.InsideRun && p.FormationName == "Singleback"), Def = _book.AllPlays.First(p => p.Side == PlaySide.Defense && p.FormationName == "4-3 Base"), Gs = Scrim(35, 1, 10) };
            foreach (bool plus in new[] { true, false })
            {
                var frame = new PlayFrame(plus);
                var sim = MakeSim(sc, 4242 + (plus ? 0 : 1), false);
                _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); AlwaysAnimate();
                const float dt = 1f / 60f;
                string dir = plus ? "+Z" : "-Z";
                for (int f = 0; f < 70; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }
                sim.Tick(dt, new ControlInput { Snap = true }, -1);
                int guard = 0; while (sim.Phase != PlayPhase.Dead && guard++ < 60 * 9) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }
                // the whistle: offense jogs to the ball carrier and cheers, defenders hang their heads (what BroadcastDirector.CelebrationRoutine does)
                int scorer = sim.Carrier != null ? sim.Carrier.Id : 1;
                Vector3 sp = _stage.WorldPos(scorer);
                var mates = new List<int>();
                for (int i = 0; i < 11 && mates.Count < 7; i++) if (i != scorer && !sim.Agents[i].IsDown) mates.Add(i);
                for (int k = 0; k < mates.Count; k++)
                {
                    float ang = (k * 360f / mates.Count + 25f) * Mathf.Deg2Rad;
                    _stage.WalkTo(mates[k], sp + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * (1.5f + 0.55f * (k % 2)), 6.3f, sp);
                }
                for (int i = 11; i < 16; i++) if (!sim.Agents[i].IsDown) _stage.PlayReaction(i, "frustration", 11 + i, false);
                var cheered = new HashSet<int>();
                for (int f = 0; f < 60 * 5; f++)
                {
                    sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt);
                    for (int k = 0; k < mates.Count; k++) if (!cheered.Contains(mates[k]) && _stage.WalkArrived(mates[k])) { cheered.Add(mates[k]); _stage.PlayReaction(mates[k], (k & 1) == 0 ? "cheer" : "celebrate_td_3", 5 + k * 7, true); }
                    yield return null;
                }
                yield return null;
                foreach (int m in mates)
                {
                    var v = _stage.Views[m]; var a = sim.Agents[m];
                    var pose = BodyHeading.Measure(v.Avatar.Animator);
                    Vector3 to = sp - v.transform.position; to.y = 0f;
                    if (!pose.Valid || to.sqrMagnitude < 0.04f) continue;
                    float want = StanceRules.YawOf(to.x, to.z);
                    float d = Mathf.Abs(StanceRules.YawDelta(pose.Yaw, want));
                    Add(sc, dir, "celebration", a, "CELEBRATION_FACING", "<=45deg toward the scorer (yaw " + want.ToString("0") + ")", "body yaw " + pose.Yaw.ToString("0") + (v.Rig != null ? " clip " + v.Rig.CurrentClipName : ""), d <= 45f);
                    Add(sc, dir, "celebration", a, "NOT_TPOSE", "hand span < 1.8m", pose.HandSpan.ToString("0.00") + "m", pose.HandSpan < 1.8f);
                }
                _stage.ClearWalks();
                _stage.End();
                yield return null;
            }
        }

        // ============================================================================================ HUD / markers / coin toss pictures
        IEnumerator HudShots()
        {
            string dirOut = Path.GetFullPath(StanceGallery.Arg("-pixHudOut", "Docs/QA/hud"));
            Directory.CreateDirectory(dirOut);
            var home = TeamCatalog.ById("buffalo_ironhorns"); var away = TeamCatalog.Teams.Count > 3 ? TeamCatalog.Teams[3] : TeamCatalog.Teams[0];
            var sc = new Scn { Name = "hud", Off = _book.AllPlays.First(p => p.Side == PlaySide.Offense && p.FormationName == "Shotgun"), Def = _book.AllPlays.First(p => p.Side == PlaySide.Defense && p.FormationName == "Nickel"), Gs = Scrim(35, 2, 7) };
            var frame = new PlayFrame(true);
            var sim = MakeSim(sc, 77, false);
            _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); AlwaysAnimate();
            const float dt = 1f / 60f;
            for (int f = 0; f < 80; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); yield return null; }
            var markers = FieldMarkers.Create(transform);
            markers.SetLines(frame, sim.LosY, sim.LosY + 7f, true, true);
            markers.SetRing(_stage.Views[0].transform);
            var stars = new List<Transform>(); for (int i = 1; i < 11; i++) if (sim.Agents[i].IsReceiverEligible && _stage.Views[i] != null) stars.Add(_stage.Views[i].transform);
            markers.SetStars(stars);
            var c = UserCameraView.Build(sim, frame, SimSide.Offense);
            var pose = UserCameraMath.Evaluate(c, UserCameraPreset.Classic(), 0f, 0f, 0f, 1f);
            _cam.transform.SetPositionAndRotation(pose.Position, pose.Rotation); _cam.fieldOfView = pose.Fov;
            var gb = _cam.GetComponent<Unity.Cinemachine.CinemachineBrain>(); if (gb != null) gb.enabled = false;
            var hud = new GameObject("GalleryHud").AddComponent<PixFootball.UI.MatchHud>();
            hud.Build(null);
            var cv = hud.HudRoot.GetComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = _cam; cv.planeDistance = 1f;
            var snap = new ScoreSnapshot { HomeAbbr = "BUF", AwayAbbr = "MIA", HomeCrest = "crest_" + home.Id, AwayCrest = "crest_" + away.Id, HomeColor = home.PrimaryUi, AwayColor = away.PrimaryUi, HomeScore = 14, AwayScore = 10, Quarter = "2ND", Clock = "4:58", PlayClock = 18, DownDistance = "2ND & 7", Spot = "BALL ON BUF 35", PossessionHome = true, HomeTimeouts = 3, AwayTimeouts = 2 };
            hud.SetScore(snap); hud.SetWind(35f, 9f); hud.SetHint("A  SNAP      Y  AUDIBLE      VIEW  CAMERA");
            Canvas.ForceUpdateCanvases();
            yield return HudCapture(dirOut, "bottom_bar");
            PixSettings.Scorebug = ScorebugStyle.Top; hud.SetScore(snap); Canvas.ForceUpdateCanvases();
            yield return HudCapture(dirOut, "top_block");
            PixSettings.Scorebug = ScorebugStyle.Bottom; hud.SetScore(snap);
            hud.SetKickMeter(true, 0.46f, "Perfect", 0.55f, "Centered"); hud.ShowBanner("FIRST DOWN", new Color(0.176f, 0.353f, 0.737f), 3f, "2ND & 7");
            Canvas.ForceUpdateCanvases();
            yield return HudCapture(dirOut, "kick_meter_banner");
            hud.SetKickMeter(false, 0.5f, "", 0.5f, "");
            // coin toss on the field
            markers.HideAll(); _stage.End();
            var offs = MatchOfficials.Create(transform, home, away);
            _stage.BeginToss(KitA, KitB, Vector3.zero); AlwaysAnimate2();
            offs.PlaceForToss(Vector3.zero, 180f);
            hud.SetTossBackdrop(true); hud.SetTossCaption("Rollers choose tails.");
            hud.ShowCoinTossCall("BUFFALO", "MIAMI", h => { });
            for (int f = 0; f < 60; f++) { offs.TossTick(dt); yield return null; }
            _cam.transform.SetPositionAndRotation(new Vector3(1.4f, 1.55f, -8.2f), Quaternion.LookRotation(new Vector3(0f, 1f, 0f) - new Vector3(1.4f, 1.55f, -8.2f))); _cam.fieldOfView = 32f;
            Canvas.ForceUpdateCanvases();
            yield return HudCapture(dirOut, "coin_toss");
            _stage.EndToss();
        }

        void AlwaysAnimate2() { foreach (var v in FindObjectsByType<SimAgentView>(FindObjectsSortMode.None)) { if (v.Avatar == null || v.Avatar.Animator == null) continue; v.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; foreach (var smr in v.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true; } }

        IEnumerator HudCapture(string dirOut, string name)
        {
            yield return null; yield return null;
            const int W = 1920, H = 1080;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _cam.targetTexture = rt; _cam.Render(); _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = null; _cam.targetTexture = null; rt.Release(); Destroy(rt);
            File.WriteAllBytes(Path.Combine(dirOut, name + ".png"), tex.EncodeToPNG()); Destroy(tex); _imgCount++;
        }

        // ============================================================================================ camera sequences
        sealed class Seq { public string Name; public Play Off, Def; public GameState Gs; public SimSide User; public float[] Times; public bool OnCatch; }

        IEnumerator CameraSequences()
        {
            string dirOut = Path.GetFullPath(StanceGallery.Arg("-pixCameraOut", "Docs/QA/camera"));
            Directory.CreateDirectory(dirOut);
            var rig = MatchCameraRig.Create(transform, _cam);
            if (rig.Brain != null) rig.Brain.enabled = true;
            rig.SetMode(CameraMode.Behind, true);
            Func<string, Play> sp = n => _book.AllPlays.First(p => p.Side == PlaySide.Special && p.Name == n);
            Func<PlayCategory, string, Play> offBy = (cat, form) => _book.AllPlays.First(p => p.Side == PlaySide.Offense && p.Category == cat && (form == null || p.FormationName == form));
            var seqs = new List<Seq>
            {
                new Seq { Name = "pass_offense", Off = offBy(PlayCategory.ShortPass, "Shotgun"), Def = _book.AllPlays.First(p => p.Side == PlaySide.Defense && p.FormationName == "4-3 Base" && p.Name == "Cover 3 Sky"), Gs = Scrim(35, 1, 10), User = SimSide.Offense, Times = new[] { -0.4f, 0.5f, 1.5f, 2.6f }, OnCatch = true },
                new Seq { Name = "run_offense", Off = offBy(PlayCategory.InsideRun, "Singleback"), Def = _book.AllPlays.First(p => p.Side == PlaySide.Defense && p.FormationName == "4-3 Base" && p.Name == "Cover 3 Sky"), Gs = Scrim(35, 1, 10), User = SimSide.Offense, Times = new[] { -0.4f, 0.5f, 1.5f, 2.6f, 3.6f } },
                new Seq { Name = "pass_defense", Off = offBy(PlayCategory.DeepPass, "Shotgun"), Def = _book.AllPlays.First(p => p.Side == PlaySide.Defense && p.FormationName == "Nickel" && p.Name == "Cover 3 Sky"), Gs = Scrim(35, 1, 10), User = SimSide.Defense, Times = new[] { -0.4f, 0.5f, 1.5f, 2.6f }, OnCatch = true },
                new Seq { Name = "run_defense", Off = offBy(PlayCategory.InsideRun, "I-Form"), Def = _book.AllPlays.First(p => p.Side == PlaySide.Defense && p.FormationName == "4-3 Base" && p.Name == "Cover 3 Sky"), Gs = Scrim(35, 1, 10), User = SimSide.Defense, Times = new[] { -0.4f, 0.5f, 1.5f, 2.6f, 3.6f } },
                new Seq { Name = "kickoff_kicking", Off = sp("Kickoff Deep Middle"), Def = sp("Kick Return Middle Wedge"), Gs = new GameState(_rules, Team.Away), User = SimSide.Offense, Times = new[] { -0.4f, 1.0f, 2.5f, 4.5f } },
                new Seq { Name = "kickoff_returning", Off = sp("Kickoff Deep Middle"), Def = sp("Kick Return Middle Wedge"), Gs = new GameState(_rules, Team.Away), User = SimSide.Defense, Times = new[] { -0.4f, 1.0f, 2.5f, 4.5f, 7f } },
                new Seq { Name = "punt_kicking", Off = sp("Punt Standard"), Def = sp("Punt Return Set"), Gs = Scrim(30, 4, 8), User = SimSide.Offense, Times = new[] { -0.4f, 0.8f, 2.0f, 3.5f } },
                new Seq { Name = "fieldgoal", Off = sp("Field Goal"), Def = sp("Field Goal Block Middle"), Gs = Scrim(80, 4, 5), User = SimSide.Offense, Times = new[] { -0.4f, 0.8f, 2.0f } },
            };
            if (!string.IsNullOrEmpty(_only)) seqs = seqs.Where(s => s.Name.IndexOf(_only, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            int seed = 900;
            foreach (var sq in seqs)
                foreach (bool plus in new[] { true, false })
                {
                    var frame = new PlayFrame(plus);
                    var sc = new Scn { Name = sq.Name, Off = sq.Off, Def = sq.Def, Gs = sq.Gs };
                    var sim = MakeSim(sc, seed++, false);
                    _stage.Begin(sim, frame, KitA, KitB, ++_playIndex);
                    AlwaysAnimate();
                    rig.SetFrame(frame, true);
                    const float dt = 1f / 60f;
                    string dir = plus ? "plusZ" : "minusZ";
                    // pre-snap: let the camera settle (cut first)
                    rig.SetUserView(UserCameraView.Build(sim, frame, sq.User)); rig.Cut();
                    for (int f = 0; f < 70; f++) { sim.Tick(dt, default(ControlInput), -1); _stage.Present(dt); rig.SetUserView(UserCameraView.Build(sim, frame, sq.User)); yield return null; }
                    int ti = 0; float t = 0f; bool snapped = false; bool catchShot = false; float catchAt = -1f;
                    // shot at the pre-snap time
                    yield return Cam(rig, sq.Name, dir, "t_pre", sim);
                    ti = 1;
                    int guard = 0;
                    while (guard++ < 60 * 14 && (ti < sq.Times.Length || (sq.OnCatch && !catchShot)))
                    {
                        if (!snapped) { sim.Tick(dt, new ControlInput { Snap = true }, -1); snapped = true; }
                        else sim.Tick(dt, default(ControlInput), -1);
                        _stage.Present(dt); t += dt;
                        rig.SetUserView(UserCameraView.Build(sim, frame, sq.User));
                        if (sq.OnCatch && catchAt < 0f) foreach (var e in sim.Log.Events) if (e.Type == SimEventType.Catch) { catchAt = t; break; }
                        yield return null;
                        if (ti < sq.Times.Length && t >= sq.Times[ti]) { yield return Cam(rig, sq.Name, dir, "t_" + sq.Times[ti].ToString("0.0"), sim); ti++; }
                        if (sq.OnCatch && !catchShot && catchAt > 0f && t >= catchAt + 0.9f) { catchShot = true; yield return Cam(rig, sq.Name, dir, "after_catch", sim); }
                        if (sim.Phase == PlayPhase.Dead && ti >= sq.Times.Length) break;
                    }
                    _stage.End();
                    yield return null;
                }
            Destroy(rig.gameObject);
        }

        IEnumerator Cam(MatchCameraRig rig, string name, string dir, string tag, PlaySimulation sim)
        {
            yield return null;
            if (StanceGallery.Arg("-pixSkeleton", "0") == "1") Skeletons();
            const int W = 1600, H = 900;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _cam.targetTexture = rt; _cam.Render(); _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = null; _cam.targetTexture = null; rt.Release(); Destroy(rt);
            string outDir = Path.GetFullPath(StanceGallery.Arg("-pixCameraOut", "Docs/QA/camera"));
            File.WriteAllBytes(Path.Combine(outDir, name + "_" + dir + "_" + tag + ".png"), tex.EncodeToPNG()); Destroy(tex); _imgCount++;
            ClearGizmos();
            var p = rig.LastPose;
            Debug.Log("[Gallery] camera " + name + " " + dir + " " + tag + " pos=" + _cam.transform.position.ToString("0.0") + " fov=" + _cam.fieldOfView.ToString("0.0") + " pitch=" + p.PitchDegrees.ToString("0"));
        }

        // ============================================================================================ results
        void WriteResults()
        {
            var sb = new StringBuilder();
            sb.AppendLine("scenario,direction,phase,slot,position,label,check,pass,expected,actual");
            foreach (var r in _rows) sb.AppendLine(Q(r.Scenario) + "," + r.Direction + "," + r.Phase + "," + r.Slot + "," + r.Position + "," + Q(r.Label) + "," + r.Check + "," + (r.Pass ? 1 : 0) + "," + Q(r.Expected) + "," + Q(r.Actual));
            File.WriteAllText(Path.Combine(_out, "results.csv"), sb.ToString());
            int pass = _rows.Count(r => r.Pass), total = _rows.Count;
            var sum = new StringBuilder();
            sum.AppendLine("checks " + total + " pass " + pass + " fail " + (total - pass) + " rate " + (total > 0 ? (100.0 * pass / total).ToString("0.0") : "0") + "%");
            foreach (var g in _rows.GroupBy(r => r.Check)) sum.AppendLine(g.Key + ": " + g.Count(r => r.Pass) + "/" + g.Count());
            File.WriteAllText(Path.Combine(_out, "summary.txt"), sum.ToString());
            Debug.Log("[Gallery] " + sum.ToString());
        }

        static string Q(string s) { return "\"" + (s ?? "").Replace("\"", "'") + "\""; }
    }
}
