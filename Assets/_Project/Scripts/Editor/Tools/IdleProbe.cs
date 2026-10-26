using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.13.0 NATURAL-2: "does a body that stands still ever stop moving?" The owner sees players frozen like statues. Self-contained QA probe (flag <c>-pixIdleProbe</c>, works inside EngineAudit and MatchWatchAudit
    /// without a hook): every active SimAgentView is watched in LateUpdate (last in the frame, after Final IK). A body is "standing" while its root has moved less than 0.25 m since the window began; over every
    /// 2 s of standing the probe takes the largest rotation of 14 key bones (hips, spine, chest, head, shoulders, arms, thighs, shins) against the first frame of the window and the range of the hips position. A window
    /// where no bone turned 1.5 degrees and the hips moved less than 1.5 cm is FROZEN: no pose change. Bodies in contact takes (statues by design), ragdolls, bodies outside the view with a culled animator and
    /// windows that contained a clip change are left out. Output: <c>idle_probe.md</c> + <c>idle_windows.csv</c> in <c>-pixIdleOut</c> (default: the audit's own output folder), flushed every 20 s of game time.
    /// </summary>
    public static class IdleProbe
    {
        public static bool On { get { return Array.IndexOf(Environment.GetCommandLineArgs(), "-pixIdleProbe") >= 0; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!On) return;
            var go = new GameObject("PixIdleProbe"); UnityEngine.Object.DontDestroyOnLoad(go); go.AddComponent<IdleProbeRunner>();
        }
    }

    [DefaultExecutionOrder(32000)]
    public sealed class IdleProbeRunner : MonoBehaviour
    {
        const int Window = 120; const float MoveLimit = 0.25f, RotFrozen = 1.5f, HipsFrozen = 0.015f;
        static readonly HumanBodyBones[] Bs = { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftHand, HumanBodyBones.RightHand };

        sealed class W
        {
            public SimAgentView V; public Transform[] T; public Quaternion[] Q0; public Vector3 Root0, Hips0; public int N; public float MaxRot, HipsRange; public string Clip0; public bool Dirty; public int Id;
            public float MaxHead, MaxChest, MaxHand;
        }
        readonly Dictionary<SimAgentView, W> _w = new Dictionary<SimAgentView, W>();
        readonly List<SimAgentView> _views = new List<SimAgentView>();
        readonly StringBuilder _csv = new StringBuilder("t,agent,role,clip,stance,mm,frozen,maxRotDeg,hipsRangeCm,headDeg,chestDeg,handDeg\n");
        readonly Dictionary<string, int[]> _cat = new Dictionary<string, int[]>();      // category -> [windows, frozen, frozen at 2 deg]
        int _frame; float _lastFlush; string _out; float _clock;
        int _all, _frozen, _frozen1, _frozen3, _skipCull, _skipContact, _skipClip;
        Camera _cam;

        void Start()
        {
            _out = Arg("-pixIdleOut", Arg("-pixEngineAuditOut", Arg("-pixMwOut", "D:/PIXFootball/Renders/idle_probe")));
            Directory.CreateDirectory(_out);
            Application.quitting += () => Flush();
        }

        static string Arg(string n, string d) { var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, n); return i >= 0 && i + 1 < a.Length ? a[i + 1] : d; }

        void LateUpdate()
        {
            _frame++; _clock += Time.deltaTime;
            if (_frame % 90 == 1) { _views.Clear(); _views.AddRange(FindObjectsByType<SimAgentView>(FindObjectsSortMode.None)); _cam = Camera.main; }
            Plane[] planes = null;
            if (_cam == null) { foreach (var c in Camera.allCameras) if (c.enabled) { _cam = c; break; } }
            if (_cam != null) planes = GeometryUtility.CalculateFrustumPlanes(_cam);
            foreach (var v in _views)
            {
                if (v == null || !v.gameObject.activeInHierarchy || v.Avatar == null || v.Avatar.Animator == null || !v.Avatar.Animator.isHuman) continue;
                W w; if (!_w.TryGetValue(v, out w)) { _w[v] = w = new W { V = v, Id = v.AgentId }; Bind(w); }
                if (w.T == null) { Bind(w); if (w.T == null) continue; }
                var an = v.Avatar.Animator; Vector3 root = v.transform.position; Transform rt = an.transform; Quaternion inv = Quaternion.Inverse(rt.rotation);
                bool contact = v.ContactDriven || (v.Ragdoll != null && v.Ragdoll.Active);
                bool culled = an.cullingMode != AnimatorCullingMode.AlwaysAnimate && planes != null && !GeometryUtility.TestPlanesAABB(planes, new Bounds(root + Vector3.up * 0.95f, new Vector3(3.5f, 3.5f, 3.5f)));
                string clip = v.Rig != null ? v.Rig.CurrentClipName : "";
                if (contact || culled) { if (w.N > 0) { if (contact) _skipContact++; else _skipCull++; } w.N = 0; continue; }
                Vector2 d = new Vector2(root.x - w.Root0.x, root.z - w.Root0.z);
                if (w.N == 0 || d.magnitude > MoveLimit) { Begin(w, root, clip, rt); continue; }
                if (clip != w.Clip0) w.Dirty = true;
                // largest turn of every key bone against the first frame of the window
                for (int i = 0; i < Bs.Length; i++)
                {
                    float a = Quaternion.Angle(w.Q0[i], inv * w.T[i].rotation);
                    if (a > w.MaxRot) w.MaxRot = a;
                    if (i == 3 && a > w.MaxHead) w.MaxHead = a;
                    if (i == 2 && a > w.MaxChest) w.MaxChest = a;
                    if ((i == 12 || i == 13) && a > w.MaxHand) w.MaxHand = a;
                }
                float hr = (rt.InverseTransformPoint(w.T[0].position) - w.Hips0).magnitude; if (hr > w.HipsRange) w.HipsRange = hr;
                if (++w.N >= Window) { Close(w); Begin(w, root, clip, rt); }
            }
            if (_clock - _lastFlush > 20f) { Flush(); _lastFlush = _clock; }
        }

        void Bind(W w)
        {
            var an = w.V.Avatar.Animator; w.T = new Transform[Bs.Length]; w.Q0 = new Quaternion[Bs.Length];
            for (int i = 0; i < Bs.Length; i++) { w.T[i] = an.GetBoneTransform(Bs[i]); if (w.T[i] == null) { w.T = null; return; } }
        }

        void Begin(W w, Vector3 root, string clip, Transform rt)
        {
            w.Root0 = root; w.Hips0 = rt.InverseTransformPoint(w.T[0].position); { var iv = Quaternion.Inverse(rt.rotation); for (int i = 0; i < Bs.Length; i++) w.Q0[i] = iv * w.T[i].rotation; }
            w.N = 1; w.MaxRot = 0f; w.HipsRange = 0f; w.Clip0 = clip; w.Dirty = false; w.MaxHead = w.MaxChest = w.MaxHand = 0f;
        }

        void Close(W w)
        {
            if (w.Dirty) { _skipClip++; return; }
            bool fz = w.MaxRot < RotFrozen && w.HipsRange < HipsFrozen, fz1 = w.MaxRot < 1f && w.HipsRange < 0.01f, fz3 = w.MaxRot < 3f && w.HipsRange < 0.03f;
            _all++; if (fz) _frozen++; if (fz1) _frozen1++; if (fz3) _frozen3++;
            var v = w.V; string stance = v.Stance.Kind.ToString(); var mm = v.Mm; bool mmOn = mm != null && mm.Layer > 0.5f;
            string cat = (v.Role) + " | " + (w.Clip0.Length == 0 ? "(tree)" : Short(w.Clip0)) + (mmOn ? " | matcher" : "") + " | " + stance;
            int[] c; if (!_cat.TryGetValue(cat, out c)) _cat[cat] = c = new int[3];
            c[0]++; if (fz) c[1]++; if (fz3) c[2]++;
            _csv.Append(_clock.ToString("0.0", CultureInfo.InvariantCulture)).Append(',').Append(w.Id).Append(',').Append(v.Role).Append(',').Append(w.Clip0.Replace(',', ';')).Append(',').Append(stance).Append(',').Append(mmOn ? 1 : 0).Append(',').Append(fz ? 1 : 0).Append(',')
                .Append(w.MaxRot.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append((w.HipsRange * 100f).ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(w.MaxHead.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(w.MaxChest.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(w.MaxHand.ToString("0.00", CultureInfo.InvariantCulture)).Append('\n');
        }

        static string Short(string c) { return c.Length > 34 ? c.Substring(0, 34) : c; }

        void Flush()
        {
            if (_out == null) return;
            try
            {
                var sb = new StringBuilder("# idle probe (2 s windows of standing bodies, 14 key bones)\n\n");
                sb.AppendLine("windows " + _all + "; FROZEN (no bone >= 1.5 deg, hips < 1.5 cm): " + _frozen + " = " + (_all > 0 ? (100f * _frozen / _all).ToString("0.0", CultureInfo.InvariantCulture) : "-") + " %; at 1 deg / 1 cm: " + _frozen1 + " = " + (_all > 0 ? (100f * _frozen1 / _all).ToString("0.0", CultureInfo.InvariantCulture) : "-") + " %; at 3 deg / 3 cm: " + _frozen3 + " = " + (_all > 0 ? (100f * _frozen3 / _all).ToString("0.0", CultureInfo.InvariantCulture) : "-") + " %");
                sb.AppendLine("left out: " + _skipContact + " contact / ragdoll, " + _skipCull + " culled off-screen, " + _skipClip + " with a clip change inside the window\n");
                sb.AppendLine("| role / clip / layer / stance | windows | frozen 1.5 deg | share | frozen 3 deg |\n|---|---|---|---|---|");
                foreach (var kv in _cat.OrderByDescending(k => k.Value[1]).ThenByDescending(k => k.Value[0])) sb.AppendLine("| " + kv.Key + " | " + kv.Value[0] + " | " + kv.Value[1] + " | " + (100f * kv.Value[1] / Mathf.Max(1, kv.Value[0])).ToString("0", CultureInfo.InvariantCulture) + " % | " + kv.Value[2] + " |");
                File.WriteAllText(_out + "/idle_probe.md", sb.ToString());
                File.WriteAllText(_out + "/idle_windows.csv", _csv.ToString());
            }
            catch (Exception) { }
        }
    }
}
