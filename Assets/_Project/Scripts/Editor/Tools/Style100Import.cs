using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11.0 MOTION: reads the 100STYLE locomotion dataset (Ian Mason, CC BY 4.0 - credit "The 100STYLE Dataset - Ian Mason"; MOTION DATA ONLY, its retarget mesh is non-commercial and is not used).
    /// BVH (60 fps, cm, Y up, +Z forward, right handed) -> Unity (x mirrored, metres) -> a Humanoid avatar built from the BVH T-pose -> HumanPose per frame -> Humanoid muscle clips at 30 fps.
    /// Then cuts the long takes (3000-17000 frames of free walking / running / side-stepping / transitions) into usable pieces by their own root and foot trajectories:
    ///   loop   a steady gait cycle (speed and direction constant, seam closed), one per speed / travel-angle bin
    ///   start  stand still -> moving
    ///   stop   moving -> stand still
    ///   cut    a change of travel direction at speed
    /// Output: one .anim per piece (root normalised to the origin and heading +Z at its first frame) and a catalogue CSV with the measured speed, travel angle, cadence, flight fraction and seam error.
    /// Run (batch, any project): -executeMethod PixFootball.EditorTools.Style100Import.Run -s100In "file1.bvh,file2.bvh" -s100Out "Assets/Style100" -s100Log "D:/.../catalog.csv"
    /// Pure data conversion: no game types, safe to run in an empty project.
    /// </summary>
    public static class Style100Import
    {
        // ------------------------------------------------------------------------------------------------ BVH
        public sealed class Bvh
        {
            public string[] Name; public int[] Parent; public Vector3[] Offset; public string[][] Chan; public int[] ChanStart; public int NChan;
            public float[][] F; public float Dt; public int Frames;
        }

        sealed class Tok
        {
            readonly string _s; int _i;
            public Tok(string s) { _s = s; }
            public string Next()
            {
                while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
                int a = _i; while (_i < _s.Length && !char.IsWhiteSpace(_s[_i])) _i++;
                return a == _i ? null : _s.Substring(a, _i - a);
            }
            public float NextFloat() { return float.Parse(Next(), CultureInfo.InvariantCulture); }
        }

        public static Bvh Load(string path)
        {
            var t = new Tok(File.ReadAllText(path));
            var names = new List<string>(); var parent = new List<int>(); var off = new List<Vector3>(); var chan = new List<string[]>(); var cs = new List<int>();
            int nchan = 0;
            if (t.Next() != "HIERARCHY") throw new Exception("not a BVH: " + path);
            string tok = t.Next();      // ROOT
            Action<int> joint = null;
            joint = p =>
            {
                string nm = t.Next(); t.Next();          // name, {
                int me = names.Count; names.Add(nm); parent.Add(p); off.Add(Vector3.zero); chan.Add(new string[0]); cs.Add(nchan);
                while (true)
                {
                    string k = t.Next();
                    if (k == "OFFSET") { off[me] = new Vector3(t.NextFloat(), t.NextFloat(), t.NextFloat()); }
                    else if (k == "CHANNELS") { int n = (int)t.NextFloat(); var c = new string[n]; for (int i = 0; i < n; i++) c[i] = t.Next(); chan[me] = c; nchan += n; }
                    else if (k == "JOINT") joint(me);
                    else if (k == "End") { t.Next(); t.Next(); t.Next(); t.NextFloat(); t.NextFloat(); t.NextFloat(); t.Next(); }      // Site { OFFSET x y z }
                    else if (k == "}") return;
                    else throw new Exception("BVH parse error at '" + k + "'");
                }
            };
            joint(-1);
            if (t.Next() != "MOTION") throw new Exception("no MOTION");
            t.Next(); int frames = (int)t.NextFloat(); t.Next(); t.Next(); float dt = t.NextFloat();
            var data = new float[frames][];
            for (int f = 0; f < frames; f++) { var row = new float[nchan]; for (int c = 0; c < nchan; c++) row[c] = t.NextFloat(); data[f] = row; }
            return new Bvh { Name = names.ToArray(), Parent = parent.ToArray(), Offset = off.ToArray(), Chan = chan.ToArray(), ChanStart = cs.ToArray(), NChan = nchan, F = data, Dt = dt, Frames = frames };
        }

        static Quaternion AxisQ(char axis, float deg)
        {
            float h = deg * Mathf.Deg2Rad * 0.5f, s = Mathf.Sin(h), c = Mathf.Cos(h);
            return axis == 'X' ? new Quaternion(s, 0, 0, c) : axis == 'Y' ? new Quaternion(0, s, 0, c) : new Quaternion(0, 0, s, c);
        }

        /// <summary>Right-handed BVH rotation -> Unity (mirror in x).</summary>
        static Quaternion JointRot(Bvh b, int j, float[] row)
        {
            Quaternion q = Quaternion.identity; var ch = b.Chan[j]; int s = b.ChanStart[j];
            for (int i = 0; i < ch.Length; i++)
            {
                if (!ch[i].EndsWith("rotation")) continue;
                q = q * AxisQ(ch[i][0], row[s + i]);
            }
            return new Quaternion(q.x, -q.y, -q.z, q.w);
        }

        static Vector3 RootPos(Bvh b, float[] row)
        {
            var ch = b.Chan[0]; Vector3 p = Vector3.zero;
            for (int i = 0; i < ch.Length; i++) { if (ch[i] == "Xposition") p.x = row[i]; else if (ch[i] == "Yposition") p.y = row[i]; else if (ch[i] == "Zposition") p.z = row[i]; }
            return new Vector3(-p.x, p.y, p.z) * 0.01f;
        }

        // ------------------------------------------------------------------------------------------------ rig
        static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "Hips", "Hips" }, { "Chest", "Spine" }, { "Chest2", "Chest" }, { "Chest3", "UpperChest" }, { "Neck", "Neck" }, { "Head", "Head" },
            { "RightCollar", "RightShoulder" }, { "RightShoulder", "RightUpperArm" }, { "RightElbow", "RightLowerArm" }, { "RightWrist", "RightHand" },
            { "LeftCollar", "LeftShoulder" }, { "LeftShoulder", "LeftUpperArm" }, { "LeftElbow", "LeftLowerArm" }, { "LeftWrist", "LeftHand" },
            { "RightHip", "RightUpperLeg" }, { "RightKnee", "RightLowerLeg" }, { "RightAnkle", "RightFoot" }, { "RightToe", "RightToes" },
            { "LeftHip", "LeftUpperLeg" }, { "LeftKnee", "LeftLowerLeg" }, { "LeftAnkle", "LeftFoot" }, { "LeftToe", "LeftToes" },
        };

        sealed class Rig
        {
            public GameObject Root; public Transform[] T; public Avatar Avatar; public HumanPoseHandler H; public int[] Bone;      // BVH joint -> HumanBodyBones index or -1
            public void Dispose() { if (Root != null) UnityEngine.Object.DestroyImmediate(Root); }
        }

        static Rig BuildRig(Bvh b)
        {
            var r = new Rig { Root = new GameObject("S100Root") };
            r.T = new Transform[b.Name.Length];
            for (int j = 0; j < b.Name.Length; j++)
            {
                var g = new GameObject(b.Name[j]); r.T[j] = g.transform;
                g.transform.SetParent(b.Parent[j] < 0 ? r.Root.transform : r.T[b.Parent[j]], false);
                g.transform.localPosition = j == 0 ? Vector3.zero : new Vector3(-b.Offset[j].x, b.Offset[j].y, b.Offset[j].z) * 0.01f;
                g.transform.localRotation = Quaternion.identity;
            }
            var human = new List<HumanBone>(); var skel = new List<SkeletonBone>();
            skel.Add(new SkeletonBone { name = r.Root.name, position = Vector3.zero, rotation = Quaternion.identity, scale = Vector3.one });
            for (int j = 0; j < b.Name.Length; j++)
            {
                skel.Add(new SkeletonBone { name = b.Name[j], position = r.T[j].localPosition, rotation = Quaternion.identity, scale = Vector3.one });
                string hn; if (Map.TryGetValue(b.Name[j], out hn)) human.Add(new HumanBone { boneName = b.Name[j], humanName = hn, limit = new HumanLimit { useDefaultValues = true } });
            }
            var hd = new HumanDescription { human = human.ToArray(), skeleton = skel.ToArray(), upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f, armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false };
            r.Avatar = AvatarBuilder.BuildHumanAvatar(r.Root, hd);
            if (r.Avatar == null || !r.Avatar.isValid || !r.Avatar.isHuman) throw new Exception("avatar not valid / human");
            r.H = new HumanPoseHandler(r.Avatar, r.Root.transform);
            return r;
        }

        // ------------------------------------------------------------------------------------------------ analysis
        public sealed class Take
        {
            public string Name; public int N;            // frames at the source rate
            public float Dt;
            public float[][] Muscle;                     // per frame, HumanTrait.MuscleCount
            public Vector3[] BodyPos; public Quaternion[] BodyRot;
            public Vector3[] Hips, LA, RA, LT, RT, LW, RW, Head;      // Unity world, metres
            public float[] Heading;                      // deg
            public float[] Speed, SpeedS;                // m/s raw / smoothed 0.25 s
            public Vector2[] Vel;                        // smoothed horizontal velocity
            public float[] Travel;                       // travel direction relative to heading, deg (+ = right)
            public float FloorL, FloorR;
        }

        static float HeadingOf(Vector3 lh, Vector3 rh)
        {
            Vector3 right = rh - lh; right.y = 0f; if (right.sqrMagnitude < 1e-8f) return 0f;
            Vector3 f = Vector3.Cross(right.normalized, Vector3.up);
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        static Take Convert(Bvh b, Rig rig, string name)
        {
            int n = b.Frames, mc = HumanTrait.MuscleCount;
            var tk = new Take { Name = name, N = n, Dt = b.Dt, Muscle = new float[n][], BodyPos = new Vector3[n], BodyRot = new Quaternion[n], Hips = new Vector3[n], LA = new Vector3[n], RA = new Vector3[n], LT = new Vector3[n], RT = new Vector3[n], LW = new Vector3[n], RW = new Vector3[n], Head = new Vector3[n], Heading = new float[n], Speed = new float[n], SpeedS = new float[n], Vel = new Vector2[n], Travel = new float[n] };
            int jLH = Array.IndexOf(b.Name, "LeftHip"), jRH = Array.IndexOf(b.Name, "RightHip"), jLA = Array.IndexOf(b.Name, "LeftAnkle"), jRA = Array.IndexOf(b.Name, "RightAnkle"), jLT = Array.IndexOf(b.Name, "LeftToe"), jRT = Array.IndexOf(b.Name, "RightToe"), jLW = Array.IndexOf(b.Name, "LeftWrist"), jRW = Array.IndexOf(b.Name, "RightWrist"), jHd = Array.IndexOf(b.Name, "Head");
            var pose = new HumanPose();
            for (int f = 0; f < n; f++)
            {
                var row = b.F[f];
                for (int j = 0; j < b.Name.Length; j++)
                {
                    if (j == 0) rig.T[0].localPosition = RootPos(b, row);
                    rig.T[j].localRotation = JointRot(b, j, row);
                }
                rig.H.GetHumanPose(ref pose);
                tk.Muscle[f] = (float[])pose.muscles.Clone(); tk.BodyPos[f] = pose.bodyPosition; tk.BodyRot[f] = pose.bodyRotation;
                tk.Hips[f] = rig.T[0].position; tk.LA[f] = rig.T[jLA].position; tk.RA[f] = rig.T[jRA].position; tk.LT[f] = rig.T[jLT].position; tk.RT[f] = rig.T[jRT].position;
                tk.LW[f] = rig.T[jLW].position; tk.RW[f] = rig.T[jRW].position; tk.Head[f] = rig.T[jHd].position;
                tk.Heading[f] = HeadingOf(rig.T[jLH].position, rig.T[jRH].position);
            }
            // velocity: central difference over +-0.1 s, then a 0.25 s box
            int h = Mathf.Max(1, Mathf.RoundToInt(0.1f / b.Dt));
            var v = new Vector2[n];
            for (int f = 0; f < n; f++) { int a = Mathf.Max(0, f - h), c = Mathf.Min(n - 1, f + h); v[f] = new Vector2(tk.Hips[c].x - tk.Hips[a].x, tk.Hips[c].z - tk.Hips[a].z) / ((c - a) * b.Dt); tk.Speed[f] = v[f].magnitude; }
            int w = Mathf.Max(1, Mathf.RoundToInt(0.125f / b.Dt));
            for (int f = 0; f < n; f++)
            {
                Vector2 s = Vector2.zero; int cnt = 0;
                for (int k = Mathf.Max(0, f - w); k <= Mathf.Min(n - 1, f + w); k++) { s += v[k]; cnt++; }
                tk.Vel[f] = s / cnt; tk.SpeedS[f] = tk.Vel[f].magnitude;
                tk.Travel[f] = tk.SpeedS[f] > 0.4f ? Mathf.DeltaAngle(tk.Heading[f], Mathf.Atan2(tk.Vel[f].x, tk.Vel[f].y) * Mathf.Rad2Deg) : 0f;
            }
            Func<Vector3[], float> floor = a => { var l = a.Select(p => p.y).OrderBy(x => x).ToList(); return l[(int)(0.03f * (l.Count - 1))]; };
            tk.FloorL = Mathf.Min(floor(tk.LT), floor(tk.LA) - 0.0f); tk.FloorR = Mathf.Min(floor(tk.RT), floor(tk.RA));
            return tk;
        }

        // ------------------------------------------------------------------------------------------------ segments
        public sealed class Seg
        {
            public string Kind; public int A, B;           // source frames [A, B]
            public float Speed, Speed1, Angle, TurnDeg, Cadence, Flight, Seam, Dur, Score;
            public string Id;
        }

        static float Flight(Take t, int a, int b)
        {
            int c = 0, n = 0;
            for (int f = a; f <= b; f++) { n++; if (t.LT[f].y - t.FloorL > 0.10f && t.RT[f].y - t.FloorR > 0.10f) c++; }
            return n == 0 ? 0f : c / (float)n;
        }

        /// <summary>Steps per second from the sign changes of the forward position difference of the two ankles in the body frame.</summary>
        static float Cadence(Take t, int a, int b)
        {
            int steps = 0; float prev = 0f;
            for (int f = a; f <= b; f++)
            {
                Vector3 d = t.LA[f] - t.RA[f]; float hr = t.Heading[f] * Mathf.Deg2Rad;
                float x = d.x * Mathf.Sin(hr) + d.z * Mathf.Cos(hr), z = d.x * Mathf.Cos(hr) - d.z * Mathf.Sin(hr);
                // use the axis of travel when the body faces sideways to it
                float s = Mathf.Abs(t.Travel[f]) > 60f && Mathf.Abs(t.Travel[f]) < 120f ? z : x;
                if (f > a && Mathf.Sign(s) != Mathf.Sign(prev) && Mathf.Abs(s) > 0.01f && Mathf.Abs(prev) > 0.01f) steps++;
                if (Mathf.Abs(s) > 0.01f) prev = s;
            }
            return steps / Mathf.Max(0.1f, (b - a) * t.Dt);
        }

        static float PoseDist(Take t, int a, int b)
        {
            float d = 0f; var ma = t.Muscle[a]; var mb = t.Muscle[b];
            // body muscles only (the finger muscles are at their default in this data)
            for (int i = 0; i < 55 && i < ma.Length; i++) { float x = ma[i] - mb[i]; d += x * x; }
            d /= 55f;
            // feet relative to the hips in the heading frame: position
            Func<Vector3, int, Vector3> loc = (p, f) => Quaternion.Euler(0f, -t.Heading[f], 0f) * (p - t.Hips[f]);
            d += 6f * ((loc(t.LA[a], a) - loc(t.LA[b], b)).sqrMagnitude + (loc(t.RA[a], a) - loc(t.RA[b], b)).sqrMagnitude + (loc(t.LT[a], a) - loc(t.LT[b], b)).sqrMagnitude + (loc(t.RT[a], a) - loc(t.RT[b], b)).sqrMagnitude);
            return d;
        }

        /// <summary>Steady gait cycles: speed within 10 percent, travel angle within 12 degrees, almost no turning, a seam where the pose repeats.</summary>
        static List<Seg> Loops(Take t, string type)
        {
            var found = new List<Seg>();
            int step = Mathf.Max(1, Mathf.RoundToInt(0.05f / t.Dt)), minL = Mathf.RoundToInt(0.45f / t.Dt), maxL = Mathf.RoundToInt(1.5f / t.Dt), lstep = Mathf.Max(1, Mathf.RoundToInt(1f / 30f / t.Dt));
            for (int a = 8; a + maxL < t.N - 8; a += step)
            {
                if (t.SpeedS[a] < 0.5f) continue;
                for (int L = minL; L <= maxL; L += lstep)
                {
                    int b = a + L;
                    float mu = 0f, amin = 999f, amax = -999f; bool ok = true; float a0 = t.Travel[a];
                    for (int f = a; f <= b; f += 2) { mu += t.SpeedS[f]; }
                    mu /= ((b - a) / 2 + 1);
                    if (mu < 0.5f) break;
                    for (int f = a; f <= b; f += 2)
                    {
                        if (Mathf.Abs(t.SpeedS[f] - mu) > 0.10f * mu + 0.12f) { ok = false; break; }
                        float ang = Mathf.DeltaAngle(a0, t.Travel[f]); amin = Mathf.Min(amin, ang); amax = Mathf.Max(amax, ang);
                    }
                    if (!ok) break;           // a longer window will not become steadier
                    if (amax - amin > 14f && mu > 0.8f) continue;
                    if (Mathf.Abs(Mathf.DeltaAngle(t.Heading[a], t.Heading[b])) > 6f) continue;
                    float seam = PoseDist(t, a, b);
                    // the velocity must also repeat
                    float vd = (t.Vel[a] - t.Vel[b]).magnitude; if (vd > 0.12f * mu + 0.1f) continue;
                    float s0 = seam + vd;
                    found.Add(new Seg { Kind = "loop", A = a, B = b, Speed = Mathf.Sqrt((t.Hips[b].x - t.Hips[a].x) * (t.Hips[b].x - t.Hips[a].x) + (t.Hips[b].z - t.Hips[a].z) * (t.Hips[b].z - t.Hips[a].z)) / (L * t.Dt), Angle = Mathf.DeltaAngle(0f, a0 + (amin + amax) * 0.5f), Seam = s0, Dur = L * t.Dt });
                }
            }
            return found;
        }

        static List<Seg> Transitions(Take t, float mv)
        {
            var segs = new List<Seg>(); int n = t.N; float dt = t.Dt;
            int sec(float s) { return Mathf.RoundToInt(s / dt); }
            // still = smoothed speed under 0.35 m/s
            bool[] still = new bool[n]; for (int f = 0; f < n; f++) still[f] = t.SpeedS[f] < 0.35f;
            // starts: a still stretch of >= 0.3 s followed by speed > 2 within 2.5 s
            for (int f = sec(0.4f); f < n - sec(3f); f++)
            {
                if (!(still[f - 1] && !still[f])) continue;
                bool held = true; for (int k = f - sec(0.3f); k < f; k++) if (!still[k]) { held = false; break; }
                if (!held) continue;
                int g = -1; float peak = 0f;
                for (int k = f; k < Mathf.Min(n - 1, f + sec(2.5f)); k++) { if (t.SpeedS[k] > peak) { peak = t.SpeedS[k]; } if (t.SpeedS[k] > mv && g < 0) g = k; }
                if (g < 0) continue;
                // plateau: speed within 10 percent of the speed reached after 1 s, the end of the clip is 0.3 s after the speed settles
                int e = g; float vref = t.SpeedS[Mathf.Min(n - 1, g + sec(0.5f))];
                for (int k = g; k < Mathf.Min(n - 1, f + sec(3.2f)); k++) { e = k; if (Mathf.Abs(t.SpeedS[k] - vref) < 0.08f * vref && k > g + sec(0.2f)) break; }
                int a = Mathf.Max(0, f - sec(0.25f)), bb = Mathf.Min(n - 1, e + sec(0.3f));
                if (Mathf.Abs(Mathf.DeltaAngle(t.Heading[a], t.Heading[bb])) > 25f) continue;
                float ang = t.Travel[Mathf.Min(n - 1, g + sec(0.3f))];
                segs.Add(new Seg { Kind = "start", A = a, B = bb, Speed = 0f, Speed1 = t.SpeedS[bb], Angle = ang, Dur = (bb - a) * dt });
            }
            // stops: speed > 2 falling to still, still for >= 0.3 s afterwards
            for (int f = sec(1.5f); f < n - sec(0.8f); f++)
            {
                if (!(!still[f - 1] && still[f])) continue;
                bool held = true; for (int k = f; k < f + sec(0.3f); k++) if (!still[k]) { held = false; break; }
                if (!held) continue;
                int s = -1; float vmax = 0f;
                for (int k = f; k > Mathf.Max(1, f - sec(2.5f)); k--) { vmax = Mathf.Max(vmax, t.SpeedS[k]); if (t.SpeedS[k] > mv && s < 0) s = k; }
                if (s < 0) continue;
                // the speed before the stop: where it still equals 92 percent of its plateau
                int a = s; for (int k = s; k > Mathf.Max(1, f - sec(3f)); k--) { a = k; if (t.SpeedS[k] > vmax * 0.97f) break; }
                a = Mathf.Max(0, a - sec(0.35f)); int bb = Mathf.Min(n - 1, f + sec(0.35f));
                if (Mathf.Abs(Mathf.DeltaAngle(t.Heading[a], t.Heading[bb])) > 25f) continue;
                float ang = t.Travel[Mathf.Max(0, s - sec(0.2f))];
                segs.Add(new Seg { Kind = "stop", A = a, B = bb, Speed = t.SpeedS[a], Speed1 = 0f, Angle = ang, Dur = (bb - a) * dt });
            }
            // cuts: the travel direction in the world turns by > 50 degrees within 0.8 s while the speed stays above 1.8 m/s
            float[] dir = new float[n]; for (int f = 0; f < n; f++) dir[f] = Mathf.Atan2(t.Vel[f].x, t.Vel[f].y) * Mathf.Rad2Deg;
            int w = sec(0.8f); int last = -9999;
            for (int f = sec(0.5f); f < n - w - sec(0.6f); f++)
            {
                if (f - last < sec(1.2f)) continue;
                if (t.SpeedS[f] < mv * 1.1f || t.SpeedS[f + w] < mv * 0.9f) continue;
                float turn = Mathf.DeltaAngle(dir[f], dir[f + w]); if (Mathf.Abs(turn) < 50f) continue;
                bool fast = true; for (int k = f; k <= f + w; k += 3) if (t.SpeedS[k] < mv * 0.9f) { fast = false; break; }
                if (!fast) continue;
                // the turn must be one monotone swing (not a wobble)
                float prev = 0f; bool mono = true; for (int k = f; k <= f + w; k += 3) { float d = Mathf.DeltaAngle(dir[f], dir[k]); if (Mathf.Sign(turn) * (d - prev) < -8f) { mono = false; break; } prev = d; }
                if (!mono) continue;
                int a = Mathf.Max(0, f - sec(0.45f)), bb = Mathf.Min(n - 1, f + w + sec(0.5f));
                last = f + w;
                segs.Add(new Seg { Kind = "cut", A = a, B = bb, Speed = t.SpeedS[f], Speed1 = t.SpeedS[f + w], Angle = t.Travel[f], TurnDeg = turn, Dur = (bb - a) * dt });
            }
            return segs;
        }

        // ------------------------------------------------------------------------------------------------ clip writing
        static readonly string[] FingerRest = null;

        static string FingerProp(string muscle)
        {
            // "Left Thumb 1 Stretched" -> "LeftHand.Thumb.1 Stretched"
            string side = muscle.StartsWith("Left ") ? "Left" : "Right"; string rest = muscle.Substring(side.Length + 1);
            int sp = rest.IndexOf(' '); if (sp < 0) return null;
            return side + "Hand." + rest.Substring(0, sp) + "." + rest.Substring(sp + 1);
        }

        static bool IsFinger(string m) { return m.Contains("Thumb") || m.Contains("Index") || m.Contains("Middle") || m.Contains("Ring") || m.Contains("Little"); }

        /// <summary>Writes frames [a, b] (every <paramref name="skip"/>-th) as a 30 fps humanoid clip: root normalised to the origin and +Z at its first frame, optional closing of the seam over the last frames.</summary>
        static AnimationClip MakeClip(Take t, int a, int b, string name, bool loop)
        {
            int skip = Mathf.Max(1, Mathf.RoundToInt((1f / 30f) / t.Dt));
            var idx = new List<int>(); for (int f = a; f <= b; f += skip) idx.Add(f);
            if (idx[idx.Count - 1] != b && (b - idx[idx.Count - 1]) * 2 >= skip) idx.Add(b);
            int n = idx.Count; int mc = HumanTrait.MuscleCount;
            float h0 = Mathf.Atan2((t.BodyRot[a] * Vector3.forward).x, (t.BodyRot[a] * Vector3.forward).z) * Mathf.Rad2Deg;
            Quaternion un = Quaternion.Euler(0f, -h0, 0f);
            Vector3 p0 = t.BodyPos[a]; p0.y = 0f;
            var clip = new AnimationClip { name = name, frameRate = 30f };
            var rt = new AnimationCurve[3]; var rq = new AnimationCurve[4]; for (int i = 0; i < 3; i++) rt[i] = new AnimationCurve(); for (int i = 0; i < 4; i++) rq[i] = new AnimationCurve();
            var mu = new AnimationCurve[mc]; for (int i = 0; i < mc; i++) mu[i] = new AnimationCurve();
            Quaternion prev = Quaternion.identity; bool hp = false;
            int close = loop ? Mathf.Min(4, n / 4) : 0;
            for (int k = 0; k < n; k++)
            {
                int f = idx[k]; float time = k / 30f;
                Vector3 p = un * (t.BodyPos[f] - p0);
                Quaternion q = un * t.BodyRot[f]; if (hp && Quaternion.Dot(q, prev) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w); prev = q; hp = true;
                rt[0].AddKey(time, p.x); rt[1].AddKey(time, p.y); rt[2].AddKey(time, p.z);
                rq[0].AddKey(time, q.x); rq[1].AddKey(time, q.y); rq[2].AddKey(time, q.z); rq[3].AddKey(time, q.w);
                float w = close > 0 && k > n - 1 - close ? (k - (n - 1 - close)) / (float)close : 0f; w = w * w * (3f - 2f * w);
                for (int i = 0; i < mc; i++)
                {
                    float v = t.Muscle[f][i];
                    if (w > 0f) v = Mathf.Lerp(v, t.Muscle[idx[0]][i], w);
                    mu[i].AddKey(time, v);
                }
            }
            string[] pn = { "RootT.x", "RootT.y", "RootT.z" }, qn = { "RootQ.x", "RootQ.y", "RootQ.z", "RootQ.w" };
            for (int i = 0; i < 3; i++) clip.SetCurve("", typeof(Animator), pn[i], Smooth(rt[i]));
            for (int i = 0; i < 4; i++) clip.SetCurve("", typeof(Animator), qn[i], Smooth(rq[i]));
            for (int i = 0; i < mc; i++)
            {
                string m = HumanTrait.MuscleName[i];
                if (IsFinger(m)) { string prop = FingerProp(m); if (prop == null) continue; float rest = m.Contains("Stretched") ? -0.35f : 0f; var fc = new AnimationCurve(new Keyframe(0f, rest), new Keyframe((n - 1) / 30f, rest)); clip.SetCurve("", typeof(Animator), prop, fc); continue; }
                clip.SetCurve("", typeof(Animator), m, Smooth(mu[i]));
            }
            var set = AnimationUtility.GetAnimationClipSettings(clip); set.loopTime = false; AnimationUtility.SetAnimationClipSettings(clip, set);
            return clip;
        }

        static AnimationCurve Smooth(AnimationCurve c) { for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f); return c; }

        // ------------------------------------------------------------------------------------------------ self test
        /// <summary>Plays the written clip on the avatar and compares the ankle / wrist positions (relative to the hips) with the BVH forward kinematics.</summary>
        static string SelfTest(Take t, Rig rig, AnimationClip clip, int a, int b, Bvh bvh)
        {
            var host = new GameObject("S100Test"); var go = UnityEngine.Object.Instantiate(rig.Root, host.transform); go.name = "S100Root"; go.transform.localPosition = Vector3.zero;
            var an = go.GetComponent<Animator>(); if (an == null) an = go.AddComponent<Animator>(); an.avatar = rig.Avatar; an.applyRootMotion = true;
            Transform lf = an.GetBoneTransform(HumanBodyBones.LeftFoot), rf = an.GetBoneTransform(HumanBodyBones.RightFoot), hp = an.GetBoneTransform(HumanBodyBones.Hips), lw = an.GetBoneTransform(HumanBodyBones.LeftHand), lt = an.GetBoneTransform(HumanBodyBones.LeftToes);
            float maxAnk = 0f, maxWr = 0f, maxHipY = 0f; int tested = 0;
            int skip = Mathf.Max(1, Mathf.RoundToInt((1f / 30f) / t.Dt));
            for (int k = 0; k < clip.length * 30f - 1; k += 6)
            {
                clip.SampleAnimation(go, k / 30f);
                int f = a + k * skip;
                Quaternion un = Quaternion.Euler(0f, -Mathf.Atan2((t.BodyRot[a] * Vector3.forward).x, (t.BodyRot[a] * Vector3.forward).z) * Mathf.Rad2Deg, 0f);
                Vector3 hH = hp.position;
                Func<Vector3, Vector3> srcRel = p => un * (p - t.Hips[f]);
                Vector3 ra = lf.position - hH, sa = srcRel(t.LA[f]);
                maxAnk = Mathf.Max(maxAnk, (ra - sa).magnitude); maxWr = Mathf.Max(maxWr, ((lw.position - hH) - srcRel(t.LW[f])).magnitude);
                maxHipY = Mathf.Max(maxHipY, Mathf.Abs((hp.position.y - go.transform.position.y) - (t.Hips[f].y)));
                tested++;
            }
            UnityEngine.Object.DestroyImmediate(host);
            return "selftest frames " + tested + " max ankle err m " + maxAnk.ToString("0.000") + " max wrist err m " + maxWr.ToString("0.000") + " max hips height err m " + maxHipY.ToString("0.000");
        }

        // ------------------------------------------------------------------------------------------------ driver
        static string Arg(string name, string def)
        {
            var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
        }

        public static void Run()
        {
            string[] files = Arg("-s100In", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            string outDir = Arg("-s100Out", "Assets/Style100"); string log = Arg("-s100Log", "style100_catalog.csv");
            int maxLoops = int.Parse(Arg("-s100Loops", "10"), CultureInfo.InvariantCulture);
            Directory.CreateDirectory(outDir);
            var cat = new StringBuilder("id,file,kind,srcStart,srcEnd,dur_s,speed_mps,speed_end_mps,travel_deg,turn_deg,cadence,flight,seam\n");
            var rep = new StringBuilder();
            foreach (var path in files)
            {
                string nm = Path.GetFileNameWithoutExtension(path); string type = nm.Substring(nm.LastIndexOf('_') + 1);
                try
                {
                    var bvh = Load(path); var rig = BuildRig(bvh); var tk = Convert(bvh, rig, nm);
                    { var sp = tk.SpeedS.OrderBy(x => x).ToList(); Func<float, float> q = p => sp[Mathf.Clamp((int)(p * (sp.Count - 1)), 0, sp.Count - 1)]; rep.AppendLine(nm + ": frames " + bvh.Frames + " dt " + bvh.Dt.ToString("0.0000") + " avatar human " + rig.Avatar.isHuman + " speed p50 " + q(0.5f).ToString("0.00") + " p90 " + q(0.9f).ToString("0.00") + " p99 " + q(0.99f).ToString("0.00") + " max " + sp[sp.Count - 1].ToString("0.00") + " share>3 " + (sp.Count(x => x > 3f) / (float)sp.Count).ToString("0.00") + " share>4 " + (sp.Count(x => x > 4f) / (float)sp.Count).ToString("0.00")); }
                    var segs = new List<Seg>();
                    if (type == "FR" || type == "BR" || type == "SR" || type == "FW" || type == "BW" || type == "SW")
                    {
                        var loops = Loops(tk, type);
                        foreach (var s in loops) { s.Cadence = Cadence(tk, s.A, s.B); s.Flight = Flight(tk, s.A, s.B); }
                        // best seam per (speed bin 0.4 m/s, travel angle bin 25 degrees)
                        var best = loops.GroupBy(s => new { sb = Mathf.RoundToInt(s.Speed / 0.4f), ab = Mathf.RoundToInt(s.Angle / 25f) }).Select(g => g.OrderBy(s => s.Seam + 0.3f * s.Flight).First()).OrderBy(s => s.Seam).Take(maxLoops).ToList();
                        segs.AddRange(best);
                    }
                    if (type == "TR1" || type == "TR2" || type == "TR3") segs.AddRange(Transitions(tk, float.Parse(Arg("-s100Move", "1.0"), CultureInfo.InvariantCulture)));
                    int i = 0;
                    foreach (var s in segs)
                    {
                        s.Id = "S100_" + nm + "_" + s.Kind + (i++).ToString("00");
                        if (s.Kind != "loop") { s.Cadence = Cadence(tk, s.A, s.B); s.Flight = Flight(tk, s.A, s.B); }
                        var clip = MakeClip(tk, s.A, s.B, s.Id, s.Kind == "loop");
                        AssetDatabase.CreateAsset(clip, outDir + "/" + s.Id + ".anim");
                        cat.AppendLine(string.Join(",", new[] { s.Id, nm, s.Kind, s.A.ToString(), s.B.ToString(), s.Dur.ToString("0.00", CultureInfo.InvariantCulture), s.Speed.ToString("0.00", CultureInfo.InvariantCulture), s.Speed1.ToString("0.00", CultureInfo.InvariantCulture), s.Angle.ToString("0", CultureInfo.InvariantCulture), s.TurnDeg.ToString("0", CultureInfo.InvariantCulture), s.Cadence.ToString("0.0", CultureInfo.InvariantCulture), s.Flight.ToString("0.00", CultureInfo.InvariantCulture), s.Seam.ToString("0.0000", CultureInfo.InvariantCulture) }));
                        if (i == 1) rep.AppendLine("  " + SelfTest(tk, rig, clip, s.A, s.B, bvh));
                    }
                    rep.AppendLine("  segments " + segs.Count + " (" + string.Join(", ", segs.GroupBy(x => x.Kind).Select(g => g.Key + " " + g.Count())) + ")");
                    rig.Dispose();
                }
                catch (Exception e) { rep.AppendLine(nm + " FAILED " + e); }
                File.WriteAllText(log, cat.ToString()); File.WriteAllText(log + ".report.txt", rep.ToString());
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Style100] done\n" + rep);
            EditorApplication.Exit(0);
        }
    }
}
