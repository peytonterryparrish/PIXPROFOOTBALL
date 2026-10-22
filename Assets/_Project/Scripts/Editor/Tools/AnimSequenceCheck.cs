using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.8 in-game animation sequence check: real SimAgentViews (animation graph, AnimKit plans, BodyMotion) are driven through scripted sequences (stance, get-off, route, catch,
    /// carry, juke, stop, tackle / fall; OL block / engage; QB throw; kicker; DB backpedal / break) at a fixed 30 Hz and the pose of 14 bones is measured every frame:
    /// the per-step maximum rotation change (pops show up as spikes), the clip that played, the lowest foot point. Run: Unity -projectPath ... -executeMethod PixFootball.EditorTools.AnimSequenceCheck.Run
    /// (batch mode works). Output: Docs/QA/anim_sequence.md + Logs/anim_seq.txt.
    /// </summary>
    public static class AnimSequenceCheck
    {
        public const string Flag = "-pixAnimSeq";
        public static bool Has { get { return Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0; } }

        public static void Run()
        {
            BatchWatchdog.Arm(12);
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath);
            EditorApplication.isPlaying = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Has) return;
            BatchWatchdog.Arm(12);      // entering play mode reloaded the domain: the timer must be re-armed
            new GameObject("PixAnimSeq").AddComponent<AnimSequenceRunner>();
        }
    }

    public sealed class AnimSequenceRunner : MonoBehaviour
    {
        struct Step
        {
            public string Name; public float Dur; public AgentAction Action; public float Speed; public float Lateral; public bool Backward;
            public StanceSpec Stance; public bool HasStance; public AnimHints Hints; public string Note;
        }

        sealed class Result { public string Scenario, Step, Clip; public float MaxDelta, MeanDelta, MinFoot, MaxWeightJump, MinLeg = 9f; public int Frames; }

        static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
        };

        readonly List<Result> _results = new List<Result>();
        readonly StringBuilder _trace = new StringBuilder();

        IEnumerator Start()
        {
            Time.captureFramerate = 30;
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;
            foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None)) Destroy(pc.gameObject);
            foreach (var fr in FindObjectsByType<FreeRoamGame>(FindObjectsSortMode.None)) fr.enabled = false;
            foreach (var br in FindObjectsByType<Unity.Cinemachine.CinemachineBrain>(FindObjectsSortMode.None)) br.enabled = false;
            yield return null;
            var root = new GameObject("SeqRoot").transform;
            int exit = 0;
            foreach (var sc in Scenarios())
            {
                var view = SimAgentView.CreateForMatch(root, sc.Size, "Seq_" + sc.Name);
                view.Role = sc.Role; view.Size = sc.Size; view.AgentId = 3;
                view.Avatar.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                view.ResetForPlay(Vector3.zero, 0f);
                yield return null; yield return null;
                yield return RunScenario(sc.Name, view, sc.Steps);
                Destroy(view.gameObject);
                yield return null;
            }
            WriteReport();
            Debug.Log("[AnimSeq] done: " + _results.Count + " steps");
            yield return null;
            EditorApplication.Exit(exit);
        }

        struct Scenario { public string Name; public PlayerRole Role; public PlayerSize Size; public List<Step> Steps; }

        static AnimHints H(int seed = 5) { return new AnimHints { Seed = seed, Side = 'C' }; }

        static List<Scenario> Scenarios()
        {
            var list = new List<Scenario>();
            // --- WR: stance -> get-off -> route -> catch -> carry -> juke -> stop -> tackled -> fall
            var catchH = H(); catchH.BallLocalDir = new Vector3(0f, 0f, 1f); catchH.BallHeight = 1.9f;
            var carryH = H(); carryH.Carrying = true;
            var tackleH = H(); tackleH.Tackle = TackleType.WrapUp; tackleH.Side = 'C';
            list.Add(new Scenario
            {
                Name = "WR", Role = PlayerRole.WR, Size = PlayerSize.Small, Steps = new List<Step>
                {
                    new Step { Name = "stance", Dur = 1.6f, Action = AgentAction.Stance, Speed = 0f, Stance = new StanceSpec { Kind = StanceKind.ReceiverSplit }, HasStance = true, Hints = H() },
                    new Step { Name = "snap + get-off", Dur = 1.2f, Action = AgentAction.Route, Speed = 4.5f, Hints = H() },
                    new Step { Name = "route sprint", Dur = 1.6f, Action = AgentAction.Route, Speed = 7.0f, Hints = H() },
                    new Step { Name = "catch on the run", Dur = 1.4f, Action = AgentAction.Catch, Speed = 5.5f, Hints = catchH },
                    new Step { Name = "carry sprint", Dur = 1.8f, Action = AgentAction.Carry, Speed = 8.0f, Hints = carryH },
                    new Step { Name = "juke left", Dur = 0.9f, Action = AgentAction.Juke, Speed = 7.5f, Lateral = -1f, Hints = carryH },
                    new Step { Name = "carry", Dur = 1.0f, Action = AgentAction.Carry, Speed = 7.5f, Hints = carryH },
                    new Step { Name = "stop", Dur = 1.6f, Action = AgentAction.Idle, Speed = 0f, Hints = H() },
                    new Step { Name = "tackled (fall)", Dur = 2.2f, Action = AgentAction.Fallen, Speed = 0f, Hints = tackleH },
                    new Step { Name = "idle", Dur = 1.0f, Action = AgentAction.Idle, Speed = 0f, Hints = H() },
                }
            });
            // --- OL: stance -> block (pocket) -> engaged -> idle
            var engH = H(); engH.Rush = RushMove.Swim;
            list.Add(new Scenario
            {
                Name = "OL", Role = PlayerRole.OL, Size = PlayerSize.Large, Steps = new List<Step>
                {
                    new Step { Name = "3-point stance", Dur = 1.6f, Action = AgentAction.Stance, Stance = new StanceSpec { Kind = StanceKind.ThreePoint }, HasStance = true, Hints = H() },
                    new Step { Name = "pass set (block)", Dur = 2.0f, Action = AgentAction.Block, Speed = 0.8f, Backward = true, Hints = H() },
                    new Step { Name = "engaged", Dur = 2.0f, Action = AgentAction.Engaged, Speed = 0f, Hints = engH },
                    new Step { Name = "idle", Dur = 1.0f, Action = AgentAction.Idle, Hints = H() },
                }
            });
            // --- DL: 4-point/3-point -> rush
            list.Add(new Scenario
            {
                Name = "DL", Role = PlayerRole.DL, Size = PlayerSize.Large, Steps = new List<Step>
                {
                    new Step { Name = "3-point stance", Dur = 1.6f, Action = AgentAction.Stance, Stance = new StanceSpec { Kind = StanceKind.ThreePoint }, HasStance = true, Hints = H() },
                    new Step { Name = "get-off rush", Dur = 1.6f, Action = AgentAction.Run, Speed = 5.0f, Hints = H() },
                    new Step { Name = "tackle wrap", Dur = 2.0f, Action = AgentAction.TackleWrapUp, Speed = 3f, Hints = tackleH },
                    new Step { Name = "idle", Dur = 1.0f, Action = AgentAction.Idle, Hints = H() },
                }
            });
            // --- QB: shotgun stance -> throw -> run -> idle
            var throwH = H(); throwH.ThrowDistance = 18f; throwH.ThrowAngle = 10f;
            list.Add(new Scenario
            {
                Name = "QB", Role = PlayerRole.QB, Size = PlayerSize.Medium, Steps = new List<Step>
                {
                    new Step { Name = "shotgun stance", Dur = 1.6f, Action = AgentAction.Stance, Stance = new StanceSpec { Kind = StanceKind.QbShotgun }, HasStance = true, Hints = H() },
                    new Step { Name = "drop back", Dur = 1.2f, Action = AgentAction.Backpedal, Speed = 3.0f, Backward = true, Hints = H() },
                    new Step { Name = "throw", Dur = 1.5f, Action = AgentAction.Throw, Speed = 0f, Hints = throwH },
                    new Step { Name = "scramble", Dur = 1.5f, Action = AgentAction.Run, Speed = 5.5f, Hints = H() },
                    new Step { Name = "idle", Dur = 1.0f, Action = AgentAction.Idle, Hints = H() },
                }
            });
            // --- DB: press stance -> backpedal -> run -> interception catch
            var intH = H(); intH.Defender = true; intH.BallLocalDir = new Vector3(0f, 0f, -1f); intH.BallHeight = 1.5f;
            list.Add(new Scenario
            {
                Name = "DB", Role = PlayerRole.DB, Size = PlayerSize.Small, Steps = new List<Step>
                {
                    new Step { Name = "press stance", Dur = 1.6f, Action = AgentAction.Stance, Stance = new StanceSpec { Kind = StanceKind.CornerPress }, HasStance = true, Hints = H() },
                    new Step { Name = "backpedal", Dur = 1.6f, Action = AgentAction.Backpedal, Speed = 3.5f, Backward = true, Hints = H() },
                    new Step { Name = "break and run", Dur = 1.6f, Action = AgentAction.Run, Speed = 7.0f, Hints = H() },
                    new Step { Name = "interception", Dur = 1.4f, Action = AgentAction.Catch, Speed = 5f, Hints = intH },
                    new Step { Name = "idle", Dur = 1.0f, Action = AgentAction.Idle, Hints = H() },
                }
            });
            // --- K: kickoff
            var kickH = H(); kickH.Kind = PlayKind.Kickoff;
            list.Add(new Scenario
            {
                Name = "K", Role = PlayerRole.K, Size = PlayerSize.Small, Steps = new List<Step>
                {
                    new Step { Name = "stand at the spot", Dur = 1.2f, Action = AgentAction.Stance, Stance = new StanceSpec { Kind = StanceKind.Kicker }, HasStance = true, Hints = kickH },
                    new Step { Name = "kick", Dur = 2.4f, Action = AgentAction.Kick, Hints = kickH },
                    new Step { Name = "idle", Dur = 1.0f, Action = AgentAction.Idle, Hints = kickH },
                }
            });
            return list;
        }

        IEnumerator RunScenario(string name, SimAgentView view, List<Step> steps)
        {
            Vector3 pos = Vector3.zero; float dt = 1f / 30f;
            var anim = view.Avatar.Animator;
            var bones = Bones.Select(b => anim.GetBoneTransform(b)).ToArray();
            var prev = new Quaternion[bones.Length]; bool havePrev = false;
            float prevW = 0f; float speedNow = 0f;
            foreach (var st in steps)
            {
                if (st.HasStance) view.ReplanStance(st.Stance);
                var res = new Result { Scenario = name, Step = st.Name, MinFoot = 9f, Clip = "" };
                int frames = Mathf.RoundToInt(st.Dur * 30f);
                float sum = 0f; var h = st.Hints;
                for (int i = 0; i < frames; i++)
                {
                    // the simulation's velocity ramps toward the step speed (acceleration limited like the engine)
                    speedNow = Mathf.MoveTowards(speedNow, st.Speed, 7f * dt);
                    Vector3 vel = new Vector3(0f, 0f, st.Backward ? -speedNow : speedNow);
                    pos += vel * dt;
                    view.ApplyMatch(pos, vel, Vector3.forward, st.Action, h, st.Lateral, view.Role == PlayerRole.K, false, dt);
                    yield return null;      // WaitForEndOfFrame never fires in -batchmode (that was the hang): the bones are read after the next animation pass instead
                    float maxD = 0f;
                    for (int b = 0; b < bones.Length; b++)
                    {
                        if (bones[b] == null) continue;
                        var q = bones[b].localRotation;
                        if (havePrev) maxD = Mathf.Max(maxD, Quaternion.Angle(prev[b], q));
                        prev[b] = q;
                        if (b >= 12) res.MinFoot = Mathf.Min(res.MinFoot, bones[b].position.y);
                    }
                    havePrev = true;
                    if (bones[8] != null && bones[10] != null && bones[12] != null && bones[9] != null && bones[11] != null && bones[13] != null)
                    {
                        float l1 = Vector3.Distance(bones[8].position, bones[10].position) + Vector3.Distance(bones[10].position, bones[12].position);
                        float l2 = Vector3.Distance(bones[9].position, bones[11].position) + Vector3.Distance(bones[11].position, bones[13].position);
                        res.MinLeg = Mathf.Min(res.MinLeg, Vector3.Distance(bones[8].position, bones[12].position) / Mathf.Max(0.01f, l1), Vector3.Distance(bones[9].position, bones[13].position) / Mathf.Max(0.01f, l2));
                    }
                    float w = view.Rig != null ? view.Rig.Weight : 0f;
                    if (i > 0) res.MaxWeightJump = Mathf.Max(res.MaxWeightJump, Mathf.Abs(w - prevW));
                    prevW = w;
                    if (i > 0) { res.MaxDelta = Mathf.Max(res.MaxDelta, maxD); sum += maxD; }
                    string cn = view.Rig != null ? view.Rig.CurrentClipName : "";
                    if (cn.Length > 0) res.Clip = cn;
                    if (i % 3 == 0) _trace.AppendLine(name + "," + st.Name + "," + i + "," + maxD.ToString("0.0") + "," + w.ToString("0.00") + "," + cn);
                }
                res.Frames = frames; res.MeanDelta = frames > 1 ? sum / (frames - 1) : 0f;
                _results.Add(res);
            }
        }

        void WriteReport()
        {
            Directory.CreateDirectory("Logs"); Directory.CreateDirectory("Docs/QA");
            File.WriteAllText("Logs/anim_seq.txt", _trace.ToString());
            var sb = new StringBuilder();
            sb.AppendLine("# Animation sequence check (v0.8)");
            sb.AppendLine();
            sb.AppendLine("Real match views (graph + AnimKit plans + BodyMotion) driven at 30 Hz through scripted sequences by `AnimSequenceCheck`. Per step: the largest single-frame rotation change of 14 bones (degrees per frame; a pop shows as a spike far above the mean), the mean change, the largest jump of the clip layer weight (a smooth cross-fade moves it by at most 0.23 per frame (0.22 s minimum fade); the verdict threshold is 0.24), the lowest foot point (m) and the clip that played. Raw trace: `Logs/anim_seq.txt`.");
            sb.AppendLine();
            sb.AppendLine("| Scenario | Step | Clip | max deg/frame | mean deg/frame | max weight jump | lowest foot y | min leg extension | verdict |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            int bad = 0;
            foreach (var r in _results)
            {
                string verdict = "ok";
                if (r.MaxDelta > 38f && r.MaxDelta > r.MeanDelta * 6f) verdict = "POP";
                if (r.MaxWeightJump > 0.24f) verdict = verdict == "ok" ? "weight jump" : verdict + " + weight jump";
                if (r.MinFoot < -0.06f) verdict = verdict == "ok" ? "foot in turf" : verdict + " + foot in turf";
                if (r.MinLeg < 0.3f) verdict = verdict == "ok" ? "folded legs" : verdict + " + folded legs";
                if (verdict != "ok") bad++;
                sb.AppendLine("| " + r.Scenario + " | " + r.Step + " | " + (string.IsNullOrEmpty(r.Clip) ? "(locomotion)" : r.Clip) + " | " + r.MaxDelta.ToString("0.0") + " | " + r.MeanDelta.ToString("0.0") + " | " + r.MaxWeightJump.ToString("0.00") + " | " + r.MinFoot.ToString("0.00") + " | " + r.MinLeg.ToString("0.00") + " | " + verdict + " |");
            }
            sb.AppendLine();
            sb.AppendLine("Steps flagged: " + bad + " of " + _results.Count + ".");
            File.WriteAllText("Docs/QA/anim_sequence.md", sb.ToString());
        }
    }

}
