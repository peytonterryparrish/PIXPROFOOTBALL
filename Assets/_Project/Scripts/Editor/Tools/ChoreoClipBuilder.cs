using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.10.0 CHOREO: measures the root motion and key moments of every action clip of the ArtLibrary (clips of 8 s or less) into PixChoreo.asset (<see cref="ChoreoTable"/>).
    /// The match plays the clips in place (Center-of-Mass import, root motion off); the root path stored here is the travel each take wants, so the simulation can move the agent along it.
    /// Run: Unity -batchmode -projectPath ... -executeMethod PixFootball.EditorTools.ChoreoClipBuilder.Run   (menu: PIX Football/Assets/Build Choreo Table). Log: Logs/choreo_table.txt
    /// </summary>
    public static class ChoreoClipBuilder
    {
        public const float ModelYaw = -90f;
        const float Step = 0.05f, MaxLength = 8.0f;

        struct Sample { public Vector3 root, hips; public float yaw; public Vector3 handMid; public float lf, rf; public Vector3 la, ra, lt, rt; }

        /// <summary>Compiles the project and exits (a build check for the choreography files).</summary>
        public static void Noop() { Debug.Log("[Choreo] compiled"); }

        /// <summary>Writes the runtime clip library of the sequence planner to a file (headless calibration runs read it). Args: -pixChoreoLibOut path.</summary>
        public static void Export()
        {
            string path = ChoreoAudit.Arg("-pixChoreoLibOut", "D:/PIXFootball/Work/Choreo/choreo_lib.bin");
            PixFootball.Gameplay.GameChoreoLibrary.ResetCache(); ContactTable.ResetCache(); ChoreoTable.ResetCache(); ArtLibrary.ResetCache();
            var lib = PixFootball.Gameplay.GameChoreoLibrary.Shared;
            if (lib == null) { Debug.LogError("[Choreo] no library to export"); EditorApplication.Exit(1); return; }
            using (var f = File.Create(path)) PixFootball.Sim.ChoreoLibraryIO.Write(lib, f);
            Debug.Log("[Choreo] exported " + lib.Pairs.Count + " pairs, " + lib.Catches.Count + " catches, " + lib.Intercepts.Count + " interceptions to " + path);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        [MenuItem("PIX Football/Assets/Build Choreo Table")]
        public static void Run()
        {
            BatchWatchdog.Arm(25);
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            if (lib == null) { Debug.LogError("[Choreo] no ArtLibrary"); return; }
            var prefab = lib.Prefab("player_medium");
            var go = Object.Instantiate(prefab); go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.Euler(0f, ModelYaw, 0f);
            var anim = go.GetComponent<Animator>(); anim.applyRootMotion = true;
            var table = AssetDatabase.LoadAssetAtPath<ChoreoTable>(ChoreoTable.EditorPath);
            if (table == null) { table = ScriptableObject.CreateInstance<ChoreoTable>(); MaterialFactory.EnsureFolder("Assets/_Licensed/Resources"); AssetDatabase.CreateAsset(table, ChoreoTable.EditorPath); }
            table.clips.Clear();
            var timing = ClipTimingTable.Load();
            var log = new StringBuilder();
            var seen = new HashSet<string>();
            int skipped = 0;
            foreach (string key in lib.ClipKeys.OrderBy(k => k, System.StringComparer.Ordinal))
            {
                foreach (var clip in lib.ClipVariants(key))
                {
                    if (clip == null || !seen.Add(clip.name)) continue;
                    if (clip.length > MaxLength || clip.length < 0.1f || clip.isLooping) { skipped++; continue; }
                    var e = MeasureClip(go, anim, clip, timing);
                    table.clips.Add(e);
                    log.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-18} {1,-52} len={2:0.00} contact={3:0.00} settle={4:0.00} minHips={5:0.00} lead={6:+0;-0;0} root0to1=({7:0.00},{8:0.00}) hand=({9:0.00},{10:0.00},{11:0.00}) kmean={12:0.00} kmin={13:0.00}",
                        key, clip.name, e.length, e.contactT, e.settleT, e.minHips, e.settleLead, e.s[(e.Count - 1) * ChoreoTable.Stride], e.s[(e.Count - 1) * ChoreoTable.Stride + 1], e.hand.x, e.hand.y, e.hand.z, e.k != null && e.k.Length > 0 ? e.k.Average() : 1f, e.k != null && e.k.Length > 0 ? e.k.Min() : 1f));
                }
            }
            Object.DestroyImmediate(go);
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            ChoreoTable.ResetCache();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/choreo_table.txt", log.ToString());
            Debug.Log("[Choreo] " + table.clips.Count + " clips measured, " + skipped + " skipped (loops / long takes) -> Logs/choreo_table.txt");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static ChoreoTable.Entry MeasureClip(GameObject go, Animator anim, AnimationClip clip, ClipTimingTable timing)
        {
            int n = Mathf.Max(3, Mathf.FloorToInt(clip.length / Step) + 1);
            var s = Measure(go, anim, clip, n);
            var e = new ChoreoTable.Entry { clip = clip.name, length = clip.length, step = Step, s = new float[n * ChoreoTable.Stride] };
            float minH = 9f, h0 = 0f;
            for (int i = 0; i < n; i++)
            {
                int o = i * ChoreoTable.Stride;
                e.s[o] = s[i].root.x; e.s[o + 1] = s[i].root.z; e.s[o + 2] = s[i].hips.x; e.s[o + 3] = s[i].hips.z; e.s[o + 4] = s[i].yaw; e.s[o + 5] = s[i].hips.y;
                if (i < 4) h0 = Mathf.Max(h0, s[i].hips.y);
                minH = Mathf.Min(minH, s[i].hips.y);
            }
            e.minHips = minH;
            e.k = ScaleFactors(s, n);
            float t = 0f, t2;
            if (timing != null && timing.TryGet(clip.name, out t, out t2) && t > 0f) e.contactT = Mathf.Min(t, clip.length);
            // hand mid-point at the key moment, in the pelvis frame
            if (e.contactT > 0f)
            {
                int ic = Mathf.Clamp(Mathf.RoundToInt(e.contactT / Step), 0, n - 1);
                Vector3 d = s[ic].handMid - s[ic].hips;
                float yaw = s[ic].yaw * Mathf.Deg2Rad;
                // pelvis frame: forward = (sin yaw, cos yaw), right = (cos yaw, -sin yaw)
                Vector3 fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)), right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));
                e.hand = new Vector3(Vector3.Dot(d, right), d.y + s[ic].hips.y, Vector3.Dot(d, fwd));
            }
            // settle: after a fall the hips are back above 80 percent of their starting height; otherwise a short beat after the key moment
            bool lies = minH < 0.55f * Mathf.Max(0.5f, h0);
            int lie = -1;
            if (lies) for (int i = 0; i < n; i++) if (s[i].hips.y < 0.55f * h0) { lie = i; break; }
            float settle;
            if (lies && lie >= 0)
            {
                settle = clip.length;
                for (int i = lie + 2; i < n; i++) if (s[i].hips.y > 0.8f * h0) { settle = i * Step; break; }
            }
            else settle = e.contactT > 0f ? Mathf.Min(clip.length, e.contactT + 0.30f) : clip.length * 0.8f;
            e.settleT = settle;
            int isx = Mathf.Clamp(Mathf.RoundToInt(settle / Step), 0, n - 1);
            e.settleLead = Mathf.Abs(s[isx].lf - s[isx].rf) < 0.04f ? 0f : (s[isx].lf > s[isx].rf ? 1f : -1f);
            return e;
        }

        /// <summary>
        /// Per sample, the factor by which the take's travel must be scaled so the body moves at the speed its planted foot allows: the anchor foot (the planted one that moves least over the turf) has the world
        /// velocity vf while the root moves at vr, so the body that keeps that foot still moves at vr - vf; k = vr.(vr - vf) / vr.vr along the line of travel, held to 0.35 .. 1.25 and smoothed over 5 samples.
        /// 1 where the root is slow (under 0.6 m/s), where no foot is on the turf (flight, a dive) or where nothing needs scaling.
        /// </summary>
        static float[] ScaleFactors(Sample[] s, int n)
        {
            var k = new float[n]; for (int i = 0; i < n; i++) k[i] = 1f;
            if (n < 5) return k;
            float floor = 9f;
            for (int i = 0; i < n; i++) { floor = Mathf.Min(floor, Mathf.Min(s[i].lt.y, s[i].la.y - 0.085f)); floor = Mathf.Min(floor, Mathf.Min(s[i].rt.y, s[i].ra.y - 0.085f)); }
            var raw = new float[n]; for (int i = 0; i < n; i++) raw[i] = 1f;
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 vr = (s[i + 1].root - s[i - 1].root) / (2f * Step); vr.y = 0f;
                float sp = vr.magnitude; if (sp < 0.6f) continue;
                bool lp = Mathf.Min(s[i].lt.y, s[i].la.y - 0.085f) - floor < 0.045f, rp = Mathf.Min(s[i].rt.y, s[i].ra.y - 0.085f) - floor < 0.045f;
                if (!lp && !rp) continue;
                Vector3 vl = ((s[i + 1].la + s[i + 1].lt) - (s[i - 1].la + s[i - 1].lt)) * (0.5f / (2f * Step)); vl.y = 0f;
                Vector3 vrr = ((s[i + 1].ra + s[i + 1].rt) - (s[i - 1].ra + s[i - 1].rt)) * (0.5f / (2f * Step)); vrr.y = 0f;
                Vector3 vf = lp && rp ? (vl.sqrMagnitude <= vrr.sqrMagnitude ? vl : vrr) : (lp ? vl : vrr);
                float kk = Vector3.Dot(vr, vr - vf) / (sp * sp);
                raw[i] = Mathf.Clamp(kk, 0.35f, 1.25f);
            }
            for (int i = 0; i < n; i++)
            {
                float sum = 0f; int c = 0;
                for (int j = Mathf.Max(0, i - 2); j <= Mathf.Min(n - 1, i + 2); j++) { sum += raw[j]; c++; }
                k[i] = sum / c;
            }
            return k;
        }

        static Sample[] Measure(GameObject go, Animator anim, AnimationClip clip, int n)
        {
            go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.Euler(0f, ModelYaw, 0f);
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var l = anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var r = anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand); var rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var lt = anim.GetBoneTransform(HumanBodyBones.LeftToes) ?? anim.GetBoneTransform(HumanBodyBones.LeftFoot);
            var rt = anim.GetBoneTransform(HumanBodyBones.RightToes) ?? anim.GetBoneTransform(HumanBodyBones.RightFoot);
            var lf2 = anim.GetBoneTransform(HumanBodyBones.LeftFoot); var rf2 = anim.GetBoneTransform(HumanBodyBones.RightFoot);
            var res = new Sample[n];
            PlayableGraph g;
            AnimPlay.PlayClip(anim, clip, out g);
            g.Evaluate(0f);
            float prevYaw = 90f;
            for (int i = 0; i < n; i++)
            {
                if (i > 0) g.Evaluate(Step);
                Vector3 rv = r.position - l.position;
                float yaw = prevYaw;
                if (new Vector2(rv.x, rv.z).magnitude > 0.12f) yaw = Mathf.Atan2(-rv.z, rv.x) * Mathf.Rad2Deg;
                prevYaw = yaw;
                float yr = yaw * Mathf.Deg2Rad;
                Vector3 fwd = new Vector3(Mathf.Sin(yr), 0f, Mathf.Cos(yr));
                res[i] = new Sample
                {
                    root = go.transform.position, hips = hips.position, yaw = yaw,
                    handMid = lh != null && rh != null ? (lh.position + rh.position) * 0.5f : hips.position,
                    lf = lt != null ? Vector3.Dot(lt.position - hips.position, fwd) : 0f, rf = rt != null ? Vector3.Dot(rt.position - hips.position, fwd) : 0f,
                    la = lf2 != null ? lf2.position : hips.position, ra = rf2 != null ? rf2.position : hips.position, lt = lt != null ? lt.position : hips.position, rt = rt != null ? rt.position : hips.position
                };
            }
            g.Destroy();
            return res;
        }
    }
}
