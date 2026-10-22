using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.7.0 Track A clip builder (additive: never touches the clips ArtLibraryBuilder made, only adds clips under Animations/Clean, timing entries to PixClipTiming and keys to the ArtLibrary).
    ///  1. QB throws: the BoNES studio takes are 10 s multi-action recordings that start in a T-pose; the old runtime picked a window around 0.45 x length, which is a random moment. Here the right-hand
    ///     release (peak forward hand speed with the hand cocked above the chest) is detected, the take is cut around it (wind-up from the ball at the chest, release, follow-through) and registered as
    ///     pixqb_throw_set / _run / _off / _away with the release time in the timing table.
    ///  2. Handoff exchange moments and QB drop travel windows (start, end, metres) for distance-matched dropbacks.
    ///  3. Stance holds (WR split, QB under centre, press / off corners, OL, RB): a static pose cut from the best take with a faint breathing so nobody sways, derived by blending poses in muscle space.
    /// Run: Unity -batchmode -executeMethod PixFootball.EditorTools.AnimQbBuilder.BuildAll   (log: D:/PIXFootball/anim_qb/qb_build.txt)
    /// </summary>
    public static class AnimQbBuilder
    {
        const string OutDir = "D:/PIXFootball/anim_qb";
        static readonly StringBuilder Log = new StringBuilder();
        static readonly List<KeyValuePair<string, AnimationClip>> Made = new List<KeyValuePair<string, AnimationClip>>();
        static ClipTimingTable _timing;

        struct ThrowEvent { public float ts, tr, speed; }

        public static void BuildAll()
        {
            BatchWatchdog.Arm(30);
            Directory.CreateDirectory(OutDir);
            Log.Length = 0; Made.Clear();
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            if (lib == null) { Debug.LogError("[AnimQb] no ArtLibrary"); return; }
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            _timing = AssetDatabase.LoadAssetAtPath<ClipTimingTable>(ClipTimingTable.EditorPath);
            if (_timing == null) { Debug.LogError("[AnimQb] no timing table (run Build Clip Timing first)"); rig.Dispose(); return; }

            BuildThrows(lib, rig);
            MeasureHandoffs(lib, rig);
            MeasureDrops(lib, rig);
            BuildStances(lib);
            rig.Dispose();

            EditorUtility.SetDirty(_timing);
            Register();
            AssetDatabase.SaveAssets();
            ClipTimingTable.ResetCache(); ArtLibrary.ResetCache();
            File.WriteAllText(OutDir + "/qb_build.txt", Log.ToString());
            Debug.Log("[AnimQb] built " + Made.Count + " clips\n" + Log);
        }

        // ------------------------------------------------------------------------------------------------ timing helpers
        static void SetTiming(string clip, float t, float t2, float d)
        {
            _timing.entries.RemoveAll(e => e != null && e.clip == clip);
            _timing.entries.Add(new ClipTimingTable.Entry { clip = clip, t = t, t2 = t2, d = d });
        }

        static void Add(string key, AnimationClip c) { Made.Add(new KeyValuePair<string, AnimationClip>(key, c)); }

        // ------------------------------------------------------------------------------------------------ throws
        static readonly string[][] ThrowGroups =
        {
            new[] { ArtKeys.PixQbThrowSet, "qb_throw_bullet", "qb_throw_touch", "qb_throw_lob_short", "qb_throw_lob_medium", "qb_throw_lob_long", "qb_throw" },
            new[] { ArtKeys.PixQbThrowRun, "qb_run_left", "qb_run_right", "qb_scramble" },
            new[] { ArtKeys.PixQbThrowOff, "qb_throw_offbalance", "qb_throw_sidearm", "qb_throw_jump" },
            new[] { ArtKeys.PixQbThrowAway, "qb_throw_away" },
        };

        static void BuildThrows(ArtLibrary lib, AnimRig rig)
        {
            foreach (var g in ThrowGroups)
            {
                string outKey = g[0]; int n = 0;
                var seen = new HashSet<string>();
                for (int gi = 1; gi < g.Length; gi++)
                    for (int v = 0; v < lib.ClipVariantCount(g[gi]); v++)
                    {
                        var src = lib.ClipVariant(g[gi], v);
                        if (src == null || src.name.EndsWith("_M") || !seen.Add(src.name) || src.length < 1.2f) continue;
                        var fr = rig.Sample(src, 0f, Mathf.Min(src.length, 12f), 30f);
                        var evs = FindThrows(fr, 0.85f);
                        for (int k = 0; k < evs.Count && k < 2; k++)
                        {
                            var e = evs[k];
                            float t0 = Mathf.Max(0.8f, e.ts - 0.22f), t1 = Mathf.Min(src.length, e.tr + 0.95f);
                            if (e.tr - t0 < 0.25f || t1 - e.tr < 0.5f) continue;
                            var s = PoseSeq.FromClip(src, t0, t1, 30f); s.Loop = false; s.InPlace = true; s.RebaseRoot(true);
                            s.Name = "pixqb_" + outKey.Substring(6) + "_" + Short(src.name) + "_" + k;
                            ClipCleaner.WriteSeq(s);
                            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipCleaner.PathOf(s.Name));
                            SetTiming(s.Name, e.tr - t0, e.ts - t0, 0f);
                            Add(outKey, clip); n++;
                            Log.AppendLine(s.Name + " <- " + src.name + " window " + t0.ToString("0.00") + ".." + t1.ToString("0.00") + " release@" + (e.tr - t0).ToString("0.00") + " (src " + e.tr.ToString("0.00") + ", hand " + e.speed.ToString("0.0") + " m/s) windup from " + (e.ts - t0).ToString("0.00"));
                        }
                    }
                Log.AppendLine("== " + outKey + ": " + n + " clips");
            }
        }

        static string Short(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name) if (char.IsLetterOrDigit(c)) sb.Append(c);
            string s = sb.ToString().Replace("BONES", "").Replace("QB", "");
            return s.Length > 26 ? s.Substring(s.Length - 26) : s;
        }

        /// <summary>Throw releases: peak forward speed of the right hand relative to the chest while the hand is cocked above the chest; wind-up start = the last still moment with the ball at the chest.</summary>
        static List<ThrowEvent> FindThrows(List<AnimRig.Frame> fr, float minT)
        {
            int n = fr.Count; var loc = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 f = AnimRig.ShoulderForward(fr[i]); Vector3 r = Vector3.Cross(Vector3.up, f);
                Vector3 d = fr[i].P[AnimRig.RH] - fr[i].P[AnimRig.Chest];
                loc[i] = new Vector3(Vector3.Dot(d, r), d.y, Vector3.Dot(d, f));
            }
            var sp = new float[n]; var fs = new float[n];
            for (int i = 1; i < n - 1; i++) { Vector3 v = (loc[i + 1] - loc[i - 1]) * 15f; sp[i] = v.magnitude; fs[i] = v.z; }
            var cand = new List<ThrowEvent>();
            int w = 5;
            for (int i = Mathf.Max(w, Mathf.RoundToInt(minT * 30f)); i < n - w; i++)
            {
                if (sp[i] < 4.2f || fs[i] < 0.5f * sp[i]) continue;
                bool peak = true; for (int j = i - w; j <= i + w; j++) if (sp[j] > sp[i]) { peak = false; break; }
                if (!peak) continue;
                float maxUp = -9f; for (int j = Mathf.Max(0, i - 15); j <= i; j++) maxUp = Mathf.Max(maxUp, loc[j].y);
                if (maxUp < 0.12f) continue;
                int ts = -1;
                for (int j = i - 8; j >= Mathf.Max(1, i - 40); j--) if (sp[j] < 1.4f && loc[j].y < 0.10f) { ts = j; break; }
                float tsT = ts >= 0 ? ts / 30f : Mathf.Max(0f, i / 30f - 0.6f);
                cand.Add(new ThrowEvent { ts = tsT, tr = i / 30f, speed = sp[i] });
            }
            cand.Sort((a, b) => b.speed.CompareTo(a.speed));
            var res = new List<ThrowEvent>();
            foreach (var c in cand) { bool near = false; foreach (var r in res) if (Mathf.Abs(r.tr - c.tr) < 1.1f) near = true; if (!near) res.Add(c); }
            res.Sort((a, b) => a.tr.CompareTo(b.tr));
            return res;
        }

        // ------------------------------------------------------------------------------------------------ handoffs and drops
        static void MeasureHandoffs(ArtLibrary lib, AnimRig rig)
        {
            for (int v = 0; v < lib.ClipVariantCount("handoff"); v++)
            {
                var a = lib.ClipVariant("handoff", v); if (a == null || a.length > 8f) continue;
                var fr = rig.Sample(a, 0f, a.length, 30f);
                float best = -1f, bt = a.length * 0.4f;
                for (int i = (int)(fr.Count * 0.15f); i < fr.Count * 0.85f; i++)
                {
                    var f = fr[i]; Vector3 fw = AnimRig.ShoulderForward(f);
                    float ext = Mathf.Max(Vector3.Dot(f.P[AnimRig.RH] - f.P[AnimRig.Chest], fw), Vector3.Dot(f.P[AnimRig.LH] - f.P[AnimRig.Chest], fw));
                    if (ext > best) { best = ext; bt = i / 30f; }
                }
                SetTiming(a.name, bt, 0f, 0f);
                var b = PixFootball.Gameplay.AnimKit.TwinOf("handoff_b", a);
                if (b != null) SetTiming(b.name, bt, 0f, 0f);
                Log.AppendLine("handoff " + a.name + " exchange@" + bt.ToString("0.00") + " ext " + best.ToString("0.00"));
            }
        }

        static void MeasureDrops(ArtLibrary lib, AnimRig rig)
        {
            foreach (string key in new[] { "qb_drop_3", "qb_drop_5", "qb_drop_7" })
                for (int v = 0; v < lib.ClipVariantCount(key); v++)
                {
                    var c = lib.ClipVariant(key, v); if (c == null) continue;
                    var fr = rig.Sample(c, 0f, c.length, 30f);
                    int n = fr.Count; var sp = new float[n];
                    for (int i = 1; i < n - 1; i++) { Vector3 d = fr[i + 1].P[AnimRig.Hips] - fr[i - 1].P[AnimRig.Hips]; d.y = 0f; sp[i] = d.magnitude * 15f; }
                    int a = -1, b = -1;
                    for (int i = 1; i < n - 1; i++) if (sp[i] > 0.9f) { if (a < 0) a = i; b = i; }
                    if (a < 0) { Log.AppendLine(key + " " + c.name + " does not travel"); continue; }
                    Vector3 dd = fr[b].P[AnimRig.Hips] - fr[a].P[AnimRig.Hips]; dd.y = 0f;
                    SetTiming(c.name, a / 30f, b / 30f, dd.magnitude);
                    Log.AppendLine(key + " " + c.name + " travel " + (a / 30f).ToString("0.00") + ".." + (b / 30f).ToString("0.00") + " = " + dd.magnitude.ToString("0.00") + " m");
                }
        }

        // ------------------------------------------------------------------------------------------------ stance holds
        static PoseSeq Hold(AnimationClip src, float t, float dur, string name, float breath)
        {
            var one = PoseSeq.FromClip(src, Mathf.Max(0f, t - 0.0333f), Mathf.Min(src.length, t + 0.0333f), 30f);
            int n = Mathf.RoundToInt(dur * 30f) + 1;
            var s = PoseSeq.Alloc(n, 30f, name); s.Loop = true; s.InPlace = true;
            for (int i = 0; i < n; i++)
            {
                float ph = Mathf.Sin(i / 30f / dur * Mathf.PI * 2f);
                for (int m = 0; m < s.M.Length; m++) s.M[m][i] = one.M[m][1];
                s.RT[i] = one.RT[1]; s.RQ[i] = one.RQ[1];
                AddBreath(s, i, ph, breath);
            }
            return s;
        }

        static void AddBreath(PoseSeq s, int i, float ph, float amp)
        {
            string[] chest = { "Chest Front-Back", "UpperChest Front-Back", "Spine Front-Back" };
            foreach (var nm in chest) { int m = PoseSeq.Index(nm); if (m >= 0) s.M[m][i] += ph * amp; }
        }

        static PoseSeq Blend(PoseSeq a, PoseSeq b, float w, string name)
        {
            var s = a.Clone(); s.Name = name;
            for (int m = 0; m < s.M.Length; m++) for (int i = 0; i < s.N; i++) s.M[m][i] = Mathf.Lerp(a.M[m][i], b.M[m][Mathf.Min(i, b.N - 1)], w);
            return s;
        }

        static AnimationClip Src(ArtLibrary lib, string name) { var c = lib.ClipByName(name); return c ?? StickSheet.FindClip(name); }

        static void WriteStance(string key, PoseSeq s, StringBuilder note)
        {
            ClipCleaner.WriteSeq(s);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipCleaner.PathOf(s.Name));
            Add(key, clip);
            var m = s.MirrorAboutBody(); m.Name = s.Name + "_M"; m.Loop = true; m.InPlace = true;
            ClipCleaner.WriteSeq(m);
            Add(key, AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipCleaner.PathOf(m.Name)));
            Log.AppendLine("stance " + key + " " + s.Name + " (+ _M)  " + note);
        }

        static void BuildStances(ArtLibrary lib)
        {
            // QB under centre: the crouch of the BoNES loop (hands out in front, knees bent), held
            var uc = Src(lib, "BONES_QB_undercenter_to_catch_loop_001");
            if (uc != null)
            {
                var s = Hold(uc, 4.30f, 2.4f, "pixstance_qb_under_00", 0.018f);
                // pigeon-toed feet, shoulder width: turn the lower legs / feet in a little
                NudgeFeetIn(s, 0.12f);
                WriteStance(ArtKeys.PixStanceQbUnder, s, new StringBuilder("from loop_001 @4.30"));
            }
            // WR split: half way between the halfback two-point (square, hands on thighs) and the safety two-point (staggered, upright)
            var hb = Src(lib, "2pstance_halfback_001"); var sf = Src(lib, "2pstance_safety_001");
            if (hb != null && sf != null)
            {
                var a = Hold(hb, 1.0f, 2.4f, "pixstance_wr_a", 0.014f); var b = Hold(sf, 1.0f, 2.4f, "pixstance_wr_b", 0.014f);
                var s = Blend(a, b, 0.55f, "pixstance_wr_00");
                WriteStance(ArtKeys.PixStanceWr, s, new StringBuilder("0.45 halfback two-point + 0.55 safety two-point"));
                var r = Blend(a, b, 0.40f, "pixstance_rb_00");
                WriteStance(ArtKeys.PixStanceRb, r, new StringBuilder("0.6 halfback + 0.4 safety (hands on thighs, flat back)"));
            }
            // CB off ("in the chair") = the BGS man-to-man idle held; press = the same legs with the arms of the press-jam take at its ready frame
            var cb = Src(lib, "BGS__BGS_CB_Idle_M2M");
            if (cb != null)
            {
                var off = Hold(cb, 1.2f, 2.4f, "pixstance_cb_off_00", 0.012f);
                WriteStance(ArtKeys.PixStanceCbOff, off, new StringBuilder("BGS CB idle M2M held"));
                var jam = lib.ClipVariantCount("press_jam") > 0 ? lib.ClipVariant("press_jam", 0) : null;
                if (jam != null)
                {
                    var j = Hold(jam, 0.05f, 2.4f, "pixstance_cb_press_arm", 0.0f);
                    var p = off.Clone(); p.Name = "pixstance_cb_press_00";
                    p.Layer(j, new[] { "Left Shoulder", "Left Arm", "Left Forearm", "Left Hand", "Right Shoulder", "Right Arm", "Right Forearm", "Right Hand" }, 0.85f);
                    WriteStance(ArtKeys.PixStanceCbPress, p, new StringBuilder("CB idle legs + press_jam ready arms"));
                }
            }
        }

        static void NudgeFeetIn(PoseSeq s, float amount)
        {
            int l = PoseSeq.Index("Left Foot Twist In-Out"), r = PoseSeq.Index("Right Foot Twist In-Out");
            for (int i = 0; i < s.N; i++) { if (l >= 0) s.M[l][i] += amount; if (r >= 0) s.M[r][i] -= amount; }
        }

        // ------------------------------------------------------------------------------------------------ registration
        static void Register()
        {
            foreach (string path in new[] { "Assets/_Licensed/ArtLibrary.asset", "Assets/_Licensed/Resources/ArtLibrary.asset" })
            {
                var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>(path);
                if (lib == null) continue;
                var keys = new HashSet<string>(Made.Select(m => m.Key));
                lib.clips.RemoveAll(e => e != null && keys.Contains(e.key) && e.key.StartsWith("pix"));
                foreach (var m in Made)
                    if (m.Value != null) lib.clips.Add(new ArtLibrary.ClipEntry { key = m.Key, clip = m.Value, source = AssetDatabase.GetAssetPath(m.Value), length = m.Value.length, loop = m.Value.isLooping });
                EditorUtility.SetDirty(lib);
            }
        }
    }
}
