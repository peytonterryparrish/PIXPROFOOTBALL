using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// A Humanoid clip as dense arrays: root body transform (RootT / RootQ) and every muscle, sampled at a fixed rate. All cleanup operations work on this
    /// representation (trim, filter, loop, mirror, retime, layer) and ToClip writes it back as a Humanoid .anim.
    /// </summary>
    public sealed class PoseSeq
    {
        public float Rate;
        public int N;
        public float[][] M;          // [muscle][frame]
        public Vector3[] RT;         // body position
        public Quaternion[] RQ;      // body rotation
        public string Name;
        public bool InPlace;         // root XZ locked (writes constant XZ)
        public bool Loop;

        public static string[] MuscleNames { get { return HumanTrait.MuscleName; } }
        public static int MuscleCount { get { return HumanTrait.MuscleCount; } }
        static Dictionary<string, int> _idx;
        public static int Index(string name)
        {
            if (_idx == null) { _idx = new Dictionary<string, int>(); var n = MuscleNames; for (int i = 0; i < n.Length; i++) _idx[n[i]] = i; }
            int r; return _idx.TryGetValue(name, out r) ? r : -1;
        }

        public PoseSeq Clone()
        {
            var c = new PoseSeq { Rate = Rate, N = N, Name = Name, InPlace = InPlace, Loop = Loop };
            c.M = new float[M.Length][]; for (int i = 0; i < M.Length; i++) c.M[i] = (float[])M[i].Clone();
            c.RT = (Vector3[])RT.Clone(); c.RQ = (Quaternion[])RQ.Clone();
            return c;
        }

        public static PoseSeq Alloc(int n, float rate, string name)
        {
            var s = new PoseSeq { Rate = rate, N = n, Name = name, M = new float[MuscleCount][], RT = new Vector3[n], RQ = new Quaternion[n] };
            for (int i = 0; i < s.M.Length; i++) s.M[i] = new float[n];
            return s;
        }

        public float Duration { get { return N > 1 ? (N - 1) / Rate : 0f; } }

        // ------------------------------------------------------------------------------------------ read
        public static PoseSeq FromClip(AnimationClip clip, float t0, float t1, float rate)
        {
            if (t1 <= 0f || t1 > clip.length) t1 = clip.length;
            int n = Mathf.Max(2, Mathf.RoundToInt((t1 - t0) * rate) + 1);
            var s = Alloc(n, rate, clip.name);
            var names = MuscleNames;
            var curves = new Dictionary<string, AnimationCurve>();
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type != typeof(Animator)) continue;
                curves[b.propertyName] = AnimationUtility.GetEditorCurve(clip, b);
            }
            for (int m = 0; m < names.Length; m++)
            {
                AnimationCurve c;
                if (!curves.TryGetValue(names[m], out c)) continue;
                for (int i = 0; i < n; i++) s.M[m][i] = c.Evaluate(t0 + i / rate);
            }
            AnimationCurve tx = Get(curves, "RootT.x"), ty = Get(curves, "RootT.y"), tz = Get(curves, "RootT.z");
            AnimationCurve qx = Get(curves, "RootQ.x"), qy = Get(curves, "RootQ.y"), qz = Get(curves, "RootQ.z"), qw = Get(curves, "RootQ.w");
            for (int i = 0; i < n; i++)
            {
                float t = t0 + i / rate;
                s.RT[i] = new Vector3(tx != null ? tx.Evaluate(t) : 0f, ty != null ? ty.Evaluate(t) : 0f, tz != null ? tz.Evaluate(t) : 0f);
                var q = new Quaternion(qx != null ? qx.Evaluate(t) : 0f, qy != null ? qy.Evaluate(t) : 0f, qz != null ? qz.Evaluate(t) : 0f, qw != null ? qw.Evaluate(t) : 1f);
                float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                s.RQ[i] = mag < 1e-6f ? Quaternion.identity : new Quaternion(q.x / mag, q.y / mag, q.z / mag, q.w / mag);
                if (i > 0 && Quaternion.Dot(s.RQ[i], s.RQ[i - 1]) < 0f) s.RQ[i] = new Quaternion(-s.RQ[i].x, -s.RQ[i].y, -s.RQ[i].z, -s.RQ[i].w);
            }
            return s;
        }

        static AnimationCurve Get(Dictionary<string, AnimationCurve> d, string k) { AnimationCurve c; return d.TryGetValue(k, out c) ? c : null; }

        public static bool IsHumanoidClip(AnimationClip clip)
        {
            foreach (var b in AnimationUtility.GetCurveBindings(clip)) if (b.type == typeof(Animator) && b.propertyName == "RootT.x") return true;
            return false;
        }

        // ------------------------------------------------------------------------------------------ write
        public AnimationClip ToClip(string name)
        {
            var clip = new AnimationClip { name = name, frameRate = Rate };
            var names = MuscleNames;
            for (int m = 0; m < names.Length; m++) clip.SetCurve("", typeof(Animator), names[m], Curve(M[m]));
            var tx = new float[N]; var ty = new float[N]; var tz = new float[N];
            var qx = new float[N]; var qy = new float[N]; var qz = new float[N]; var qw = new float[N];
            for (int i = 0; i < N; i++)
            {
                tx[i] = InPlace ? RT[0].x : RT[i].x; ty[i] = RT[i].y; tz[i] = InPlace ? RT[0].z : RT[i].z;
                qx[i] = RQ[i].x; qy[i] = RQ[i].y; qz[i] = RQ[i].z; qw[i] = RQ[i].w;
            }
            clip.SetCurve("", typeof(Animator), "RootT.x", Curve(tx)); clip.SetCurve("", typeof(Animator), "RootT.y", Curve(ty)); clip.SetCurve("", typeof(Animator), "RootT.z", Curve(tz));
            clip.SetCurve("", typeof(Animator), "RootQ.x", Curve(qx)); clip.SetCurve("", typeof(Animator), "RootQ.y", Curve(qy));
            clip.SetCurve("", typeof(Animator), "RootQ.z", Curve(qz)); clip.SetCurve("", typeof(Animator), "RootQ.w", Curve(qw));
            var st = AnimationUtility.GetAnimationClipSettings(clip);
            st.loopTime = Loop; st.loopBlend = Loop;
            st.loopBlendOrientation = true; st.loopBlendPositionY = true; st.loopBlendPositionXZ = InPlace;
            st.keepOriginalOrientation = true; st.keepOriginalPositionY = true; st.keepOriginalPositionXZ = false;
            st.heightFromFeet = false; st.cycleOffset = 0f; st.mirror = false;
            st.startTime = 0f; st.stopTime = Duration;
            AnimationUtility.SetAnimationClipSettings(clip, st);
            clip.EnsureQuaternionContinuity();
            return clip;
        }

        AnimationCurve Curve(float[] v)
        {
            var keys = new Keyframe[N];
            float dt = 1f / Rate;
            for (int i = 0; i < N; i++)
            {
                float a = v[Mathf.Max(0, i - 1)], b = v[Mathf.Min(N - 1, i + 1)];
                float span = (Mathf.Min(N - 1, i + 1) - Mathf.Max(0, i - 1)) * dt;
                float slope = span > 0f ? (b - a) / span : 0f;
                keys[i] = new Keyframe(i * dt, v[i], slope, slope);
            }
            return new AnimationCurve(keys);
        }

        // ------------------------------------------------------------------------------------------ operations
        public PoseSeq Trim(float t0, float t1)
        {
            int a = Mathf.Clamp(Mathf.RoundToInt(t0 * Rate), 0, N - 2), b = Mathf.Clamp(Mathf.RoundToInt(t1 * Rate), a + 1, N - 1);
            return Slice(a, b);
        }

        public PoseSeq Slice(int a, int b)
        {
            int n = b - a + 1;
            var s = Alloc(n, Rate, Name); s.InPlace = InPlace; s.Loop = Loop;
            for (int m = 0; m < M.Length; m++) Array.Copy(M[m], a, s.M[m], 0, n);
            Array.Copy(RT, a, s.RT, 0, n); Array.Copy(RQ, a, s.RQ, 0, n);
            return s;
        }

        /// <summary>Re-bases the root so the clip starts at the origin facing +Z (position XZ zeroed, yaw removed). Y is kept.</summary>
        public void RebaseRoot(bool faceForward)
        {
            Vector3 p0 = RT[0];
            float yaw0 = faceForward ? Yaw(RQ[0]) : 0f;
            var undo = Quaternion.Euler(0f, -yaw0, 0f);
            for (int i = 0; i < N; i++)
            {
                Vector3 d = RT[i] - p0; d.y = 0f;
                d = undo * d;
                RT[i] = new Vector3(d.x, RT[i].y, d.z);
                RQ[i] = undo * RQ[i];
            }
        }

        public static float Yaw(Quaternion q)
        {
            // humanoid body frame: forward = +Z of the body rotation
            Vector3 f = q * Vector3.forward; f.y = 0f;
            return f.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        /// <summary>Rotates the whole clip about Y.</summary>
        public void RotateYaw(float deg)
        {
            var r = Quaternion.Euler(0f, deg, 0f);
            for (int i = 0; i < N; i++)
            {
                Vector3 p = RT[i]; Vector3 q = r * new Vector3(p.x, 0f, p.z); RT[i] = new Vector3(q.x, p.y, q.z);
                RQ[i] = r * RQ[i];
            }
        }

        public void Gaussian(float sigmaSeconds, string[] onlyContains = null, string[] skipContains = null)
        {
            if (sigmaSeconds <= 0f || N < 5) return;
            float sg = sigmaSeconds * Rate;
            int rad = Mathf.Max(1, Mathf.CeilToInt(sg * 3f));
            var w = new float[2 * rad + 1]; float sum = 0f;
            for (int k = -rad; k <= rad; k++) { w[k + rad] = Mathf.Exp(-0.5f * k * k / (sg * sg)); sum += w[k + rad]; }
            for (int k = 0; k < w.Length; k++) w[k] /= sum;
            var names = MuscleNames;
            var tmp = new float[N];
            for (int m = 0; m < M.Length; m++)
            {
                if (onlyContains != null && !ContainsAny(names[m], onlyContains)) continue;
                if (skipContains != null && ContainsAny(names[m], skipContains)) continue;
                Filter(M[m], tmp, w, rad);
            }
            // body rotation: smooth quaternion components (hemisphere continuous) and renormalise
            var qx = new float[N]; var qy = new float[N]; var qz = new float[N]; var qw = new float[N];
            for (int i = 0; i < N; i++) { qx[i] = RQ[i].x; qy[i] = RQ[i].y; qz[i] = RQ[i].z; qw[i] = RQ[i].w; }
            Filter(qx, tmp, w, rad); Filter(qy, tmp, w, rad); Filter(qz, tmp, w, rad); Filter(qw, tmp, w, rad);
            for (int i = 0; i < N; i++) { float m2 = Mathf.Sqrt(qx[i] * qx[i] + qy[i] * qy[i] + qz[i] * qz[i] + qw[i] * qw[i]); RQ[i] = new Quaternion(qx[i] / m2, qy[i] / m2, qz[i] / m2, qw[i] / m2); }
            var py = new float[N]; for (int i = 0; i < N; i++) py[i] = RT[i].y;
            Filter(py, tmp, w, rad); for (int i = 0; i < N; i++) RT[i].y = py[i];
        }

        static void Filter(float[] v, float[] tmp, float[] w, int rad)
        {
            int n = v.Length;
            for (int i = 0; i < n; i++)
            {
                float a = 0f;
                for (int k = -rad; k <= rad; k++) { int j = i + k; if (j < 0) j = 0; else if (j >= n) j = n - 1; a += v[j] * w[k + rad]; }
                tmp[i] = a;
            }
            Array.Copy(tmp, v, n);
        }

        static bool ContainsAny(string s, string[] parts) { foreach (var p in parts) if (s.Contains(p)) return true; return false; }

        /// <summary>Removes one-/two-frame spikes: a sample that deviates from the linear interpolation of its neighbours by more than the threshold (muscle units) is replaced. Returns the number of fixes.</summary>
        public int Despike(float threshold)
        {
            int fixes = 0;
            for (int m = 0; m < M.Length; m++)
            {
                var v = M[m];
                for (int i = 2; i < N - 2; i++)
                {
                    float a = v[i - 1], b = v[i + 1];
                    float lin = 0.5f * (a + b);
                    if (Mathf.Abs(v[i] - lin) > threshold && Mathf.Abs(a - b) < threshold * 0.6f) { v[i] = lin; fixes++; }
                    else if (i < N - 3)
                    {   // two-frame spike
                        float c = v[i - 1], d = v[i + 2];
                        if (Mathf.Abs(c - d) < threshold * 0.6f && Mathf.Abs(v[i] - c) > threshold && Mathf.Abs(v[i + 1] - c) > threshold && Mathf.Sign(v[i] - c) == Mathf.Sign(v[i + 1] - c))
                        { v[i] = Mathf.Lerp(c, d, 0.33f); v[i + 1] = Mathf.Lerp(c, d, 0.66f); fixes += 2; i++; }
                    }
                }
            }
            return fixes;
        }

        /// <summary>Clamps every muscle to +/-limit (the avatar's human limits); returns how many samples were outside.</summary>
        public int ClampMuscles(float limit, string[] skipContains = null)
        {
            int c = 0; var names = MuscleNames;
            for (int m = 0; m < M.Length; m++)
            {
                if (skipContains != null && ContainsAny(names[m], skipContains)) continue;
                var v = M[m];
                for (int i = 0; i < N; i++)
                {
                    float x = v[i];
                    if (x > limit) { v[i] = limit; c++; } else if (x < -limit) { v[i] = -limit; c++; }
                }
            }
            return c;
        }

        /// <summary>Human-limit handling that keeps mocap fidelity: knee / elbow stretch (hyperextension) are hard-limited to +/-1.05; every other body muscle only has its excess over +/-1 compressed (x0.4) so a pose slightly outside the avatar limits stays natural.</summary>
        public int LimitMuscles(string[] hardParts, string[] skipParts)
        {
            int c = 0; var names = MuscleNames;
            for (int m = 0; m < M.Length; m++)
            {
                if (ContainsAny(names[m], skipParts)) continue;
                bool hard = ContainsAny(names[m], hardParts);
                var v = M[m];
                for (int i = 0; i < N; i++)
                {
                    float x = v[i], a = Mathf.Abs(x);
                    if (a <= 1f) continue;
                    float y = hard ? Mathf.Min(a, 1.05f) : 1f + 0.4f * (a - 1f);
                    v[i] = Mathf.Sign(x) * y; c++;
                }
            }
            return c;
        }

        /// <summary>Linear blend of the first/last frames: fades the edges toward the pose at the other end so the seam disappears (hold-style loops).</summary>
        public void CloseSeam(int frames)
        {
            frames = Mathf.Min(frames, N / 3);
            if (frames < 2) return;
            // blend the last `frames` toward frame 0, half-way split: the end moves toward the start pose
            for (int u = 0; u < frames; u++)
            {
                float k = (u + 1f) / frames; k = k * k * (3f - 2f * k);
                int i = N - frames + u;
                for (int m = 0; m < M.Length; m++) M[m][i] = Mathf.Lerp(M[m][i], M[m][0], k);
                RQ[i] = Quaternion.Slerp(RQ[i], RQ[0], k);
                RT[i].y = Mathf.Lerp(RT[i].y, RT[0].y, k);
            }
        }

        /// <summary>Cross-fades the last w frames with the frames just before `loopStart` so that sample[N] == sample[loopStart-1+1]: seamless cycle for takes cut out of a longer recording.</summary>
        public static PoseSeq MakeLoop(PoseSeq src, int start, int len, int fade)
        {
            fade = Mathf.Min(fade, start);
            var s = Alloc(len, src.Rate, src.Name); s.Loop = true; s.InPlace = src.InPlace;
            for (int i = 0; i < len; i++)
            {
                for (int m = 0; m < s.M.Length; m++) s.M[m][i] = src.M[m][start + i];
                s.RT[i] = src.RT[start + i]; s.RQ[i] = src.RQ[start + i];
            }
            for (int u = 0; u < fade; u++)
            {
                float k = (u + 1f) / (fade + 1f); k = k * k * (3f - 2f * k);
                int i = len - fade + u;          // tail frame; the "other side" is the frame before the start
                int j = start - fade + u;        // matching frame before the loop start (continues into frame `start` after the last)
                if (j < 0) continue;
                for (int m = 0; m < s.M.Length; m++) s.M[m][i] = Mathf.Lerp(s.M[m][i], src.M[m][j], k);
                s.RQ[i] = Quaternion.Slerp(s.RQ[i], src.RQ[j], k);
                // root translation: keep the original path (velocity-consistent); only Y is blended with the pre-start height
                s.RT[i].y = Mathf.Lerp(s.RT[i].y, src.RT[j].y, k);
            }
            return s;
        }

        /// <summary>Mirrors left/right (swaps limb muscles, negates the lateral/twist muscles of the centre line and the root).</summary>
        public PoseSeq Mirror()
        {
            var r = Clone(); var names = MuscleNames;
            for (int m = 0; m < names.Length; m++)
            {
                string n = names[m]; string other = null;
                if (n.StartsWith("Left ")) other = "Right " + n.Substring(5);
                else if (n.StartsWith("Right ")) other = "Left " + n.Substring(6);
                if (other != null)
                {
                    int o = Index(other);
                    if (o >= 0) Array.Copy(M[o], r.M[m], N);
                }
                else
                {
                    bool neg = n.Contains("Left-Right") || n.Contains("Twist") || n.Contains("Turn");
                    if (neg) for (int i = 0; i < N; i++) r.M[m][i] = -M[m][i];
                }
            }
            for (int i = 0; i < N; i++)
            {
                r.RT[i] = new Vector3(-RT[i].x, RT[i].y, RT[i].z);
                r.RQ[i] = new Quaternion(RQ[i].x, -RQ[i].y, -RQ[i].z, RQ[i].w);
            }
            r.Name = Name;
            return r;
        }

        /// <summary>Mirror across the body's own sagittal plane (the take keeps facing the same way): turn the body to +Z in the clip frame, mirror, turn back.</summary>
        public PoseSeq MirrorAboutBody() { return MirrorAbout(Yaw(RQ[0])); }

        /// <summary>Mirror across the vertical plane through the clip-frame heading psi (degrees): the body keeps facing the same way. For a take that was turned to face +Z in the game frame psi = -PlayerAvatar.ModelYaw.</summary>
        public PoseSeq MirrorAbout(float psi)
        {
            var c = Clone(); c.RotateYaw(-psi);
            var m = c.Mirror(); m.RotateYaw(psi);
            return m;
        }

        public PoseSeq Resample(float newRate)
        {
            int n = Mathf.Max(2, Mathf.RoundToInt(Duration * newRate) + 1);
            var s = Alloc(n, newRate, Name); s.InPlace = InPlace; s.Loop = Loop;
            for (int i = 0; i < n; i++)
            {
                float f = Mathf.Min(N - 1f, i / newRate * Rate);
                int a = Mathf.Min(N - 2, (int)f); float k = f - a;
                for (int m = 0; m < M.Length; m++) s.M[m][i] = Mathf.Lerp(M[m][a], M[m][a + 1], k);
                s.RT[i] = Vector3.Lerp(RT[a], RT[a + 1], k); s.RQ[i] = Quaternion.Slerp(RQ[a], RQ[a + 1], k);
            }
            return s;
        }

        /// <summary>Speed multiplier applied by resampling (2 = twice as fast, clip half as long).</summary>
        public PoseSeq Retime(float speed)
        {
            var r = Clone();
            r.Rate = Rate * speed;
            return r.Resample(Rate);
        }

        /// <summary>Overrides muscles whose name contains any of the parts with those of another sequence (same frame count by resampling to this length).</summary>
        public void Layer(PoseSeq other, string[] parts, float weight = 1f)
        {
            var names = MuscleNames;
            for (int m = 0; m < names.Length; m++)
            {
                if (!ContainsAny(names[m], parts)) continue;
                for (int i = 0; i < N; i++)
                {
                    float f = N > 1 ? i * (other.N - 1f) / (N - 1f) : 0f;
                    int a = Mathf.Min(other.N - 2, (int)f); float k = f - a;
                    float ov = Mathf.Lerp(other.M[m][a], other.M[m][a + 1], k);
                    M[m][i] = Mathf.Lerp(M[m][i], ov, weight);
                }
            }
        }

        /// <summary>Mean muscle distance (arms/legs/spine; fingers ignored) between two frames, plus a velocity term.</summary>
        public float Dist(int i, PoseSeq o, int j, bool withVel)
        {
            float s = 0f; int c = 0;
            for (int m = 0; m < M.Length; m++)
            {
                if (m >= 55) break;   // body muscles only (first 55 are body; the rest are fingers)
                float d = M[m][i] - o.M[m][j];
                s += d * d; c++;
                if (withVel && i > 0 && j > 0 && i < N - 1 && j < o.N - 1)
                {
                    float va = (M[m][i + 1] - M[m][i - 1]) * Rate * 0.5f, vb = (o.M[m][j + 1] - o.M[m][j - 1]) * o.Rate * 0.5f;
                    s += 0.02f * (va - vb) * (va - vb);
                }
            }
            return Mathf.Sqrt(s / Mathf.Max(1, c));
        }
    }
}
