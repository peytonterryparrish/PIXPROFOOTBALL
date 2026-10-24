using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11.0 ANIMLIB: the finger mocap of the raw takes. Until v0.11 PoseSeq read and wrote the finger curves under the wrong property names ("Left Index 1 Stretched" instead of
    /// "LeftHand.Index.1 Stretched"), so every cleaned clip had its fingers dropped (bind-pose hands in the game) and the authored hand poses never reached the bones. The raw BoNES takes carry real
    /// finger motion (QB 15/15 files, studio 20/20, yan 43 percent, retargets 3/4, Mixamo 11/12; Motus has static fingers). This copies it into the cleaned take: the raw take is found by name
    /// (mirror and segment suffixes removed), the cleaned window is located in it by the body muscles, and the finger muscles of that window are written into the cleaned sequence.
    /// </summary>
    public static class FingerTransplant
    {
        static Dictionary<string, AnimationClip> _raw;
        static readonly Regex Seg = new Regex(@"_s\d\d$"), Dup = new Regex(@"\.\d{3}$");

        static void BuildIndex()
        {
            if (_raw != null) return;
            _raw = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Licensed/DraftFever/Animations" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                if (path.Contains("/Clean/") || path.EndsWith("Player_A_01.fbx")) continue;
                foreach (var c in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                {
                    if (c.name.StartsWith("__preview")) continue;
                    string k = ClipCleaner.SafeName(c.name);
                    if (!_raw.ContainsKey(k)) _raw[k] = c;
                }
            }
            Debug.Log("[FingerTransplant] raw index " + _raw.Count + " clips");
        }

        public static AnimationClip RawFor(string cleanName, out bool mirror)
        {
            BuildIndex();
            mirror = false; string n = cleanName;
            if (n.EndsWith("_M")) { mirror = true; n = n.Substring(0, n.Length - 2); }
            AnimationClip c;
            if (_raw.TryGetValue(n, out c)) return c;
            string n2 = Seg.Replace(n, ""); if (_raw.TryGetValue(n2, out c)) return c;
            string n3 = Dup.Replace(n2, ""); if (_raw.TryGetValue(n3, out c)) return c;
            if (n.EndsWith("_M")) { }
            return null;
        }

        static bool IsFinger(int m) { string s = PoseSeq.MuscleNames[m]; return s.Contains("Thumb") || s.Contains("Index") || s.Contains("Middle") || s.Contains("Ring") || s.Contains("Little"); }
        static int[] _body, _fingers;

        static void Init()
        {
            if (_body != null) return;
            var b = new List<int>(); var f = new List<int>();
            for (int m = 0; m < PoseSeq.MuscleCount; m++) { if (IsFinger(m)) f.Add(m); else if (!PoseSeq.MuscleNames[m].Contains("Hand")) b.Add(m); }
            _body = b.ToArray(); _fingers = f.ToArray();
        }

        /// <summary>Range of the finger muscles of a sequence (max over the finger "Stretched" muscles of max - min).</summary>
        public static float FingerRange(PoseSeq s)
        {
            Init(); float r = 0f;
            foreach (int m in _fingers) { if (!PoseSeq.MuscleNames[m].Contains("Stretched")) continue; float mn = 9f, mx = -9f; for (int i = 0; i < s.N; i++) { mn = Mathf.Min(mn, s.M[m][i]); mx = Mathf.Max(mx, s.M[m][i]); } r = Mathf.Max(r, mx - mn); }
            return r;
        }

        /// <summary>Copies the raw take's finger muscles into seq. Returns a note ("" = not done): the raw take must have finger motion and the window must be found with a mean body-muscle difference under 0.08.</summary>
        public static string Transplant(PoseSeq seq, string cleanName)
        {
            Init();
            bool mirror; var rc = RawFor(cleanName, out mirror); if (rc == null || rc.length > 130f) return "";
            var raw = PoseSeq.FromClip(rc, 0f, rc.length, 30f);
            if (FingerRange(raw) < 0.08f) return "";
            if (mirror) raw = raw.Mirror();
            int N = seq.N, R = raw.N; if (N > R + 3) return "";
            // locate the window: mean absolute difference of the body muscles, coarse (every 3rd frame and muscle subset) then fine
            int[] sub = _body.Where((m, i) => i % 2 == 0).ToArray();
            Func<int, int, float> cost = (o, step) => { double s = 0; int c = 0; for (int i = 0; i < N; i += step) { int j = Mathf.Clamp(o + i, 0, R - 1); foreach (int m in sub) { s += Mathf.Abs(seq.M[m][i] - raw.M[m][j]); c++; } } return (float)(s / Math.Max(1, c)); };
            int best = 0; float bc = 9f; int maxO = Mathf.Max(0, R - N);
            for (int o = 0; o <= maxO + 3; o += 2) { float c = cost(o, 4); if (c < bc) { bc = c; best = o; } }
            { int lo = Mathf.Max(0, best - 2), hi = Mathf.Min(maxO + 3, best + 2); float fb = 9f; int fo = best; for (int o = lo; o <= hi; o++) { float c = cost(o, 1); if (c < fb) { fb = c; fo = o; } } best = fo; bc = fb; }
            if (bc > 0.08f) return "fingers:nomatch(" + bc.ToString("0.00") + ")";
            foreach (int m in _fingers)
            {
                for (int i = 0; i < N; i++) { int j = Mathf.Clamp(best + i, 0, R - 1); seq.M[m][i] = Mathf.Clamp(raw.M[m][j], -1f, 1f); }
                Smooth(seq.M[m], 1.0f);
            }
            return "fingers:mocap@" + best + "(" + bc.ToString("0.000") + ")";
        }

        static void Smooth(float[] v, float sigma)
        {
            int n = v.Length, rad = Mathf.CeilToInt(sigma * 3f); var o = new float[n]; var w = new float[2 * rad + 1]; float sum = 0f;
            for (int k = -rad; k <= rad; k++) { w[k + rad] = Mathf.Exp(-0.5f * k * k / (sigma * sigma)); sum += w[k + rad]; }
            for (int i = 0; i < n; i++) { float a = 0f; for (int k = -rad; k <= rad; k++) a += v[Mathf.Clamp(i + k, 0, n - 1)] * w[k + rad]; o[i] = a / sum; }
            Array.Copy(o, v, n);
        }
    }
}
