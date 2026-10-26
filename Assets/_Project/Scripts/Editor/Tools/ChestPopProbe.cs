using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.13.0 NATURAL-3: where do the chest-rotation pops (POP_CHEST_ROT_OVER_1400DEGS) come from? Self-contained QA probe (flag <c>-pixChestProbe</c>, works inside MatchWatchAudit and EngineAudit without a hook):
    /// every active body is watched in LateUpdate (last in the frame); a frame in which the chest turns faster than 1400 deg/s in the world is an event, written with its context: sim action, clip, stance kind,
    /// matcher layer, frames since the matcher's last take switch / park / resume, the size of the jump the inertializer hid, how much of the turn is the pelvis (whole-body yaw), how much is the chest against the pelvis,
    /// and the spine-rate limiter's state. Output <c>chest_pops.csv</c> + <c>chest_pops.md</c> (categories) in <c>-pixChestOut</c> (default: the audit's output folder).
    /// </summary>
    public static class ChestPopProbe
    {
        public static bool On { get { return Array.IndexOf(Environment.GetCommandLineArgs(), "-pixChestProbe") >= 0; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!On) return;
            var go = new GameObject("PixChestPopProbe"); UnityEngine.Object.DontDestroyOnLoad(go); go.AddComponent<ChestPopRunner>();
        }
    }

    [DefaultExecutionOrder(32000)]
    public sealed class ChestPopRunner : MonoBehaviour
    {
        sealed class W
        {
            public SimAgentView V; public Transform Hips, Chest, Spine; public Quaternion PrevChest, PrevHips, PrevChestLocal; public bool Have;
            public int PrevSwitches; public int SinceSwitch = 99; public float PrevLayer; public int SinceLayer = 99; public string PrevClip = ""; public int SinceClip = 99; public AgentAction PrevAct = (AgentAction)(-1); public int SinceAct = 99;
        }
        readonly Dictionary<SimAgentView, W> _w = new Dictionary<SimAgentView, W>();
        readonly List<SimAgentView> _views = new List<SimAgentView>();
        readonly StringBuilder _csv = new StringBuilder("t,play,agent,role,act,clip,stance,mm,sinceSwitch,sinceLayer,sinceClip,sinceAct,chestDegS,hipsDegS,chestLocalDegS,mmJump,mmResid,cat\n");
        readonly Dictionary<string, int[]> _cat = new Dictionary<string, int[]>();
        int _frame, _events; float _clock, _lastFlush; string _out;
        static readonly FieldInfo ActF = typeof(SimAgentView).GetField("_lastAction", BindingFlags.Instance | BindingFlags.NonPublic);

        void Start()
        {
            _out = Arg("-pixChestOut", Arg("-pixEngineAuditOut", Arg("-pixMwOut", "D:/PIXFootball/Renders/chest_probe")));
            Directory.CreateDirectory(_out);
            Application.quitting += () => Flush();
        }
        static string Arg(string n, string d) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length ? a[i + 1] : d; }

        void LateUpdate()
        {
            _frame++; float dt = Time.deltaTime; _clock += dt;
            if (dt < 1e-4f) return;
            if (_frame % 90 == 1) { _views.Clear(); _views.AddRange(FindObjectsByType<SimAgentView>(FindObjectsSortMode.None)); }
            foreach (var v in _views)
            {
                if (v == null || !v.gameObject.activeInHierarchy || v.Avatar == null || v.Avatar.Animator == null || !v.Avatar.Animator.isHuman) continue;
                W w; if (!_w.TryGetValue(v, out w)) { _w[v] = w = new W { V = v }; var an = v.Avatar.Animator; w.Hips = an.GetBoneTransform(HumanBodyBones.Hips); w.Chest = an.GetBoneTransform(HumanBodyBones.Chest) ?? an.GetBoneTransform(HumanBodyBones.UpperChest); w.Spine = an.GetBoneTransform(HumanBodyBones.Spine); }
                if (w.Hips == null || w.Chest == null) continue;
                var mm = v.Mm; float layer = mm != null ? mm.Layer : -1f; int sw = mm != null ? mm.Switches : 0;
                string clip = v.Rig != null ? v.Rig.CurrentClipName : ""; AgentAction act = ActF != null ? (AgentAction)ActF.GetValue(v) : (AgentAction)(-1);
                w.SinceSwitch++; w.SinceLayer++; w.SinceClip++; w.SinceAct++;
                if (sw != w.PrevSwitches) { w.SinceSwitch = 0; w.PrevSwitches = sw; }
                if (Mathf.Abs(layer - w.PrevLayer) > 0.3f) { w.SinceLayer = 0; w.PrevLayer = layer; }
                if (clip != w.PrevClip) { w.SinceClip = 0; w.PrevClip = clip; }
                if (act != w.PrevAct) { w.SinceAct = 0; w.PrevAct = act; }
                Quaternion cr = w.Chest.rotation, hr = w.Hips.rotation, cl = Quaternion.Inverse(hr) * cr;
                if (w.Have && !v.ContactDriven && (v.Ragdoll == null || !v.Ragdoll.Active))
                {
                    float cd = Quaternion.Angle(cr, w.PrevChest) / dt, hd = Quaternion.Angle(hr, w.PrevHips) / dt, ld = Quaternion.Angle(cl, w.PrevChestLocal) / dt;
                    if (cd > 1400f)
                    {
                        _events++;
                        string stance = v.Stance.Kind.ToString(); bool mmOn = mm != null && mm.Layer > 0.5f;
                        string cat = Group(v.Role) + " | " + act + " | " + (clip.Length == 0 ? "(tree)" : "clip") + " | " + (mmOn ? "mm" : "nomm")
                            + " | " + (w.SinceSwitch <= 2 ? "SWITCH" : w.SinceLayer <= 2 ? "LAYER" : w.SinceClip <= 2 ? "CLIPCHG" : w.SinceAct <= 2 ? "ACTCHG" : "steady")
                            + " | " + (ld > 0.6f * cd ? "torso" : hd > 0.6f * cd ? "body" : "mixed");
                        int[] c; if (!_cat.TryGetValue(cat, out c)) _cat[cat] = c = new int[1]; c[0]++;
                        _csv.Append(_clock.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(0).Append(',').Append(v.AgentId).Append(',').Append(v.Role).Append(',').Append(act).Append(',').Append(clip.Replace(',', ';')).Append(',').Append(stance).Append(',').Append(mmOn ? 1 : 0).Append(',')
                            .Append(w.SinceSwitch).Append(',').Append(w.SinceLayer).Append(',').Append(w.SinceClip).Append(',').Append(w.SinceAct).Append(',').Append(cd.ToString("0")).Append(',').Append(hd.ToString("0")).Append(',').Append(ld.ToString("0")).Append(',')
                            .Append(mm != null && mm.Inertial != null ? mm.Inertial.LastJump.ToString("0.0", CultureInfo.InvariantCulture) : "-").Append(',').Append(mm != null && mm.Inertial != null ? mm.Inertial.Residual.ToString("0.0", CultureInfo.InvariantCulture) : "-").Append(',').Append(cat.Replace(',', ';')).Append('\n');
                    }
                }
                w.PrevChest = cr; w.PrevHips = hr; w.PrevChestLocal = cl; w.Have = true;
            }
            if (_clock - _lastFlush > 30f) { Flush(); _lastFlush = _clock; }
        }

        static string Group(PlayerRole r)
        {
            string s = r.ToString();
            if (s == "LT" || s == "LG" || s == "C" || s == "RG" || s == "RT") return "OL";
            if (s == "DT" || s == "LE" || s == "RE" || s == "DE" || s == "NT") return "DL";
            if (s.EndsWith("LB")) return "LB";
            if (s == "CB" || s == "S" || s == "FS" || s == "SS") return "DB";
            return s;
        }

        void Flush()
        {
            if (_out == null) return;
            try
            {
                var sb = new StringBuilder("# chest pops > 1400 deg/s (world rotation of the chest bone, one event per frame)\n\nevents " + _events + "\n\n| role group | action | clip | layer | trigger | which part turns | events |\n|---|---|---|---|---|---|---|\n");
                foreach (var kv in _cat.OrderByDescending(k => k.Value[0])) sb.AppendLine("| " + kv.Key + " | " + kv.Value[0] + " |");
                File.WriteAllText(_out + "/chest_pops.md", sb.ToString());
                File.WriteAllText(_out + "/chest_pops.csv", _csv.ToString());
            }
            catch (Exception) { }
        }
    }
}
