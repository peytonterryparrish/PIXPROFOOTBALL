using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.13.0 QB-MOCAP: builds <c>Assets/_Licensed/Resources/PixQbClips.asset</c> (the quarterback's catalog, <see cref="QbClipTable"/>) from the measured clips.
    /// The BoNES quarterback takes are 10 s pieces of one continuous recording (he walks, turns, throws and resets inside one clip), so a clip is not a unit: the builder finds the WINDOWS that are
    /// (a) stable (stance / pocket loops: the body stays put, the chest within a bounded turn, the hands up, the loop closes on itself), (b) around an event (the release of a throw = the peak of the faster hand, the catch of a snap = the hands closing) with a
    /// lead-in that starts from the ready position, (c) whole short clips (cadence, pump fake, kneel ...). Every window is measured as the match shows it: root motion is discarded in the match, so the travel of the hips is a loss
    /// and the slide of a planted foot is measured in the frame of the (smoothed) hips. Rows that fail their family's limits are left out, the reason is in <c>Logs/qb/catalog_rejects.csv</c>.
    /// Batch: <c>-executeMethod PixFootball.EditorTools.QbClipCatalog.BuildBatch</c> (after the ArtLibrary and the tags are built).
    /// </summary>
    public static partial class QbClipCatalog
    {
        const float Rate = 30f;

        /// <summary>Per-frame features of one sampled clip.</summary>
        sealed class Feat
        {
            public int N; public string Name;
            public Vector2[] H, Hs;                    // hips xz, hips xz low-passed (the part of the travel the match extracts as root motion)
            public float[] ChestRaw; public float[] HipsY, Chest, Hip, Lean, HandsY, HandsD, RelR, RelL;   // per frame; hand speeds relative to the hips (m/s)
            public Vector2[] FR, FL, TWR, TWL; public float[] FRy, FLy;       // ankle xz relative to the smoothed hips; sole height of the right / left foot (ClipQa.Sole)
            public float Ground;
            public Vector3[][] P;                      // raw positions (for the seam distance)
        }

        static float Wrap(float a) { return Mathf.DeltaAngle(0f, a); }
        static float YawOf(Vector3 v) { return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; }

        static Feat Extract(AnimRig rig, AnimationClip clip)
        {
            var fr = rig.Sample(clip, 0f, Mathf.Min(clip.length, 30f), Rate);
            int N = fr.Count;
            var f = new Feat { N = N, Name = clip.name };
            f.H = new Vector2[N]; f.Hs = new Vector2[N]; f.HipsY = new float[N]; f.ChestRaw = new float[N]; f.Chest = new float[N]; f.Hip = new float[N]; f.Lean = new float[N]; f.HandsY = new float[N]; f.HandsD = new float[N];
            f.RelR = new float[N]; f.RelL = new float[N]; f.FR = new Vector2[N]; f.FL = new Vector2[N]; f.TWR = new Vector2[N]; f.TWL = new Vector2[N]; f.FRy = new float[N]; f.FLy = new float[N]; f.P = new Vector3[N][];
            var lowToe = new List<float>(N);
            for (int i = 0; i < N; i++) lowToe.Add(Mathf.Min(fr[i].P[AnimRig.LT].y, fr[i].P[AnimRig.RT].y));
            lowToe.Sort(); f.Ground = lowToe[Mathf.Clamp((int)(N * 0.05f), 0, N - 1)];
            for (int i = 0; i < N; i++)
            {
                var p = fr[i].P; f.P[i] = p;
                f.H[i] = new Vector2(p[AnimRig.Hips].x, p[AnimRig.Hips].z); f.HipsY[i] = p[AnimRig.Hips].y - f.Ground;
                f.Hip[i] = YawOf(AnimRig.Forward(fr[i]));
                // the shoulder line flips when the arms are overhead / the clavicles rise (a throw): the chest is never more than 70 degrees from the hips in these windows, so the line is clamped to the hips +- 70
                { float cy = YawOf(AnimRig.ShoulderForward(fr[i])), dh = Mathf.DeltaAngle(f.Hip[i], cy); f.Chest[i] = f.Hip[i] + Mathf.Clamp(dh, -70f, 70f); f.ChestRaw[i] = Mathf.Abs(dh); }
                f.Lean[i] = Vector3.Angle(p[AnimRig.Head] - p[AnimRig.Hips], Vector3.up);
                f.HandsY[i] = (p[AnimRig.LH].y + p[AnimRig.RH].y) * 0.5f - f.Ground; f.HandsD[i] = Vector3.Distance(p[AnimRig.LH], p[AnimRig.RH]);
                f.FRy[i] = p[AnimRig.RT].y - 0.023f; f.FLy[i] = p[AnimRig.LT].y - 0.023f;      // the toe bone: a foot that pivots on the ball of the foot is planted (the ankle swings round it)
            }
            // smoothed hips (±0.4 s): what the match extracts as root motion; the feet are measured against it
            int w = 8;
            for (int i = 0; i < N; i++)
            {
                Vector2 s = Vector2.zero; int c = 0;
                for (int k = Mathf.Max(0, i - w); k <= Mathf.Min(N - 1, i + w); k++) { s += f.H[k]; c++; }
                f.Hs[i] = s / c;
                f.TWR[i] = new Vector2(fr[i].P[AnimRig.RT].x, fr[i].P[AnimRig.RT].z); f.TWL[i] = new Vector2(fr[i].P[AnimRig.LT].x, fr[i].P[AnimRig.LT].z);
                f.FR[i] = new Vector2(fr[i].P[AnimRig.RT].x, fr[i].P[AnimRig.RT].z) - f.Hs[i];
                f.FL[i] = new Vector2(fr[i].P[AnimRig.LT].x, fr[i].P[AnimRig.LT].z) - f.Hs[i];
            }
            for (int i = 1; i < N - 1; i++)
            {
                f.RelR[i] = ((fr[i + 1].P[AnimRig.RH] - fr[i + 1].P[AnimRig.Hips]) - (fr[i - 1].P[AnimRig.RH] - fr[i - 1].P[AnimRig.Hips])).magnitude * (Rate / 2f);
                f.RelL[i] = ((fr[i + 1].P[AnimRig.LH] - fr[i + 1].P[AnimRig.Hips]) - (fr[i - 1].P[AnimRig.LH] - fr[i - 1].P[AnimRig.Hips])).magnitude * (Rate / 2f);
            }
            return f;
        }

        // ------------------------------------------------------------------------------------------ window measures
        /// <summary>Hips travel over the window (m, start to end) and the heading of it relative to the chest at the start.</summary>
        static float Travel(Feat f, int a, int b, out float dir)
        {
            Vector2 d = f.H[b] - f.H[a]; dir = d.magnitude > 0.05f ? Wrap(YawOf(new Vector3(d.x, 0f, d.y)) - f.Chest[a]) : 0f;
            return d.magnitude;
        }

        /// <summary>Largest hips distance from the start position over the window.</summary>
        static float Drift(Feat f, int a, int b) { float m = 0f; for (int k = a; k <= b; k++) m = Mathf.Max(m, (f.H[k] - f.H[a]).magnitude); return m; }

        /// <summary>Mean speed of the planted feet against the smoothed hips (what the match shows as a slide), m/s. <paramref name="keep"/> = the share of the clip's own travel the view re-applies as a root offset (0 = none: the feet skate by the whole travel, 1 = all: they stay planted).</summary>
        static float Slide(Feat f, int a, int b, float keep = 0f)
        {
            float sum = 0f; int n = 0;
            for (int k = a + 1; k <= b; k++)
            {
                if (f.FRy[k] < 0.035f && f.FRy[k - 1] < 0.035f && (f.TWR[k] - f.TWR[k - 1]).magnitude * Rate < 0.45f) { float s = ((f.FR[k] + keep * f.Hs[k]) - (f.FR[k - 1] + keep * f.Hs[k - 1])).magnitude * Rate; if (s < 3f) { sum += s; n++; } }
                if (f.FLy[k] < 0.035f && f.FLy[k - 1] < 0.035f && (f.TWL[k] - f.TWL[k - 1]).magnitude * Rate < 0.45f) { float s = ((f.FL[k] + keep * f.Hs[k]) - (f.FL[k - 1] + keep * f.Hs[k - 1])).magnitude * Rate; if (s < 3f) { sum += s; n++; } }
            }
            return n < 4 ? 0.15f : sum / n;      // no true plant in the window (a hop, a swing): nothing to slide
        }

        static float MaxChest(Feat f, int a, int b) { float m = 0f; for (int k = a; k <= b; k++) m = Mathf.Max(m, Mathf.Abs(Wrap(f.Chest[k] - f.Chest[a]))); return m; }

        /// <summary>Mean distance (m) between two frames' joint positions relative to the hips: 0 = identical pose.</summary>
        static float Seam(Feat f, int a, int b)
        {
            float s = 0f; int n = AnimRig.NB;
            for (int j = 0; j < n; j++) s += ((f.P[a][j] - f.P[a][AnimRig.Hips]) - (f.P[b][j] - f.P[b][AnimRig.Hips])).magnitude;
            return s / n;
        }

        // ------------------------------------------------------------------------------------------ stable windows (stance, pocket)
        sealed class Lim { public float HipsLo, HipsHi, LeanLo, LeanHi, HandsLo, HandsHi, ChestMax, DriftMax, MinLen, MaxLen, SlideMax, SeamMax; }

        /// <summary>Every frame of the window stands at least <paramref name="hipsMin"/> m tall and leans at most <paramref name="leanMax"/> degrees (the stance audit fails a quarterback under 0.62 m or over 38 degrees at the snap).</summary>
        static bool PoseRange(Feat f, int a, int b, float hipsMin, float leanMax) { for (int k = a; k <= b; k++) if (f.HipsY[k] < hipsMin || f.Lean[k] > leanMax) return false; return true; }

        static bool PoseOk(Feat f, int k, Lim l)
        {
            return f.HipsY[k] >= l.HipsLo && f.HipsY[k] <= l.HipsHi && f.Lean[k] >= l.LeanLo && f.Lean[k] <= l.LeanHi && f.HandsY[k] >= l.HandsLo && f.HandsY[k] <= l.HandsHi;
        }

        /// <summary>Longest stable windows of the clip (non-overlapping, earliest first): every frame in the pose limits, the chest within ChestMax of the start, the hips within DriftMax, and a loop point (the pose at the end equals the start within SeamMax).</summary>
        static List<int[]> StableWindows(Feat f, Lim l)
        {
            var res = new List<int[]>();
            int minN = Mathf.RoundToInt(l.MinLen * Rate), maxN = Mathf.RoundToInt(l.MaxLen * Rate);
            int i = 0;
            while (i < f.N - minN)
            {
                if (!PoseOk(f, i, l)) { i++; continue; }
                int j = i; float chestMax = 0f, drift = 0f;
                while (j + 1 < f.N && j + 1 - i < maxN)
                {
                    int k = j + 1;
                    if (!PoseOk(f, k, l)) break;
                    chestMax = Mathf.Max(chestMax, Mathf.Abs(Wrap(f.Chest[k] - f.Chest[i]))); drift = Mathf.Max(drift, (f.H[k] - f.H[i]).magnitude);
                    if (chestMax > l.ChestMax || drift > l.DriftMax) break;
                    j = k;
                }
                if (j - i >= minN)
                {
                    // the loop point: the frame in [i + minN, j] whose pose is closest to the start
                    int best = -1; float bs = float.MaxValue;
                    for (int k = i + minN; k <= j; k++) { float s = Seam(f, i, k); if (s < bs) { bs = s; best = k; } }
                    if (best > 0 && bs <= l.SeamMax && Slide(f, i, best) <= l.SlideMax) { res.Add(new[] { i, best }); i = best + 1; continue; }
                }
                i += 4;
            }
            return res;
        }

        // ------------------------------------------------------------------------------------------ events
        static List<int> HandPeaks(Feat f, float minSpeed, float minGapS, out bool[] rightHand)
        {
            var peaks = new List<int>(); var rh = new List<bool>();
            int gap = Mathf.RoundToInt(minGapS * Rate);
            for (int i = 4; i < f.N - 4; i++)
            {
                float sr = f.RelR[i], sl = f.RelL[i]; float s = Mathf.Max(sr, sl);
                if (s < minSpeed) continue;
                bool isMax = true;
                for (int k = Mathf.Max(1, i - 6); k <= Mathf.Min(f.N - 2, i + 6); k++) if (Mathf.Max(f.RelR[k], f.RelL[k]) > s + 1e-4f) { isMax = false; break; }
                if (!isMax) continue;
                if (peaks.Count > 0 && i - peaks[peaks.Count - 1] < gap) continue;
                peaks.Add(i); rh.Add(sr >= sl);
            }
            rightHand = rh.ToArray(); return peaks;
        }

        static string ThrowKind(string n)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("hail")) return "hail";
            if (n.Contains("falling")) return "fall";
            if (n.Contains("throwaway")) return "away";
            if (n.Contains("pumpfake")) return "pump";
            if (n.Contains("offbalan") || n.Contains("tackle")) return "off";
            if (n.Contains("sidearm") || n.Contains("siderman")) return "sidearm";
            if (n.Contains("shovel") || n.Contains("underhand") || n.Contains("toss")) return "shovel";
            if (n.Contains("jump")) return "jump";
            if (n.Contains("lob")) return "lob";
            if (n.Contains("touch")) return "touch";
            if (n.Contains("bullet")) return "bullet";
            if (n.Contains("run_") || n.Contains("scramble") || n.Contains("fthrow") || n.Contains("ready_to_run") || n.Contains("backpedal")) return "run";
            return "pass";
        }

        // ------------------------------------------------------------------------------------------ the build
        static readonly Regex ReNoise = new Regex(@"_co$|_dude$|^mediumplayer", RegexOptions.IgnoreCase);

        [MenuItem("PIX Football/Assets/Build QB Clip Catalog")]
        public static void BuildMenu() { Build(); }

        /// <summary>The library (QB rules) and then the catalog, one editor session.</summary>
        public static void RebuildBatch() { BatchWatchdog.Arm(90); try { ArtLibraryBuilder.Build(); AssetDatabase.Refresh(); Build(); } catch (Exception ex) { Debug.LogError("[QbClipCatalog] " + ex); } if (Application.isBatchMode) EditorApplication.Exit(0); }

        public static void BuildBatch() { BatchWatchdog.Arm(60); try { Build(); } catch (Exception ex) { Debug.LogError("[QbClipCatalog] " + ex); } if (Application.isBatchMode) EditorApplication.Exit(0); }

        public static int Build()
        {
            Directory.CreateDirectory(QbClipAudit.OutDir);
            ArtLibrary.ResetCache(); ClipTagTable.ResetCache();
            var lib = ArtLibrary.Load(); if (lib == null) { Debug.LogError("[QbClipCatalog] no ArtLibrary"); return 0; }
            var tags = ClipTagTable.Load();
            var rig = AnimRig.CreateMedium(); if (rig == null) return 0;
            var table = AssetDatabase.LoadAssetAtPath<QbClipTable>(QbClipTable.EditorPath);
            if (table == null) { table = ScriptableObject.CreateInstance<QbClipTable>(); Directory.CreateDirectory("Assets/_Licensed/Resources"); AssetDatabase.CreateAsset(table, QbClipTable.EditorPath); }
            table.entries.Clear();
            var rej = new StringBuilder("clip,family,why\n"); var csv = new StringBuilder("clip,family,kind,a,b,sync,hand,yaw0,yawSync,handYaw,dist,travelDir,slide,hips,lean,handsY,score,loop,mirror,quality\n");
            int nClips = 0, notInLib = 0;
            foreach (var c in QbClipAudit.QbClips())
            {
                string n = c.name;
                if (ReNoise.IsMatch(n)) continue;
                if (lib.ClipByName(n) == null) { notInLib++; rej.AppendLine(n + ",,not in the ArtLibrary"); continue; }
                float score = 70f; ClipTagTable.Entry te;
                if (tags != null && tags.TryGet(n, out te)) { if (te.reject) { rej.AppendLine(n + ",,tag reject"); continue; } score = te.score; }
                if (c.length < 0.3f || c.length > 40f) { rej.AppendLine(n + ",,length " + c.length.ToString("0.0")); continue; }
                Feat f;
                try { f = Extract(rig, c); } catch (Exception ex) { rej.AppendLine(n + ",,sample failed " + ex.Message); continue; }
                nClips++;
                var found = new List<QbClipTable.Entry>();
                try { Windows(f, c, score, found, rej); } catch (Exception ex) { rej.AppendLine(n + ",,windows failed " + ex.Message); }
                foreach (var e in found) { table.entries.Add(e); csv.AppendLine(Row(e)); }
                if (nClips % 50 == 0) Debug.Log("[QbClipCatalog] " + nClips + " clips, " + table.entries.Count + " rows");
            }
            rig.Dispose();
            AddMirrorRows(table, lib);
            EditorUtility.SetDirty(table); AssetDatabase.SaveAssets(); QbClipTable.ResetCache();
            File.WriteAllText(QbClipAudit.OutDir + "/catalog.csv", csv.ToString());
            File.WriteAllText(QbClipAudit.OutDir + "/catalog_rejects.csv", rej.ToString());
            var fam = table.entries.GroupBy(e => e.family).OrderBy(g => g.Key).Select(g => g.Key + "=" + g.Count() + "/" + g.Select(e => e.clip).Distinct().Count());
            Debug.Log("[QbClipCatalog] " + nClips + " clips measured (" + notInLib + " not in the library), " + table.entries.Count + " rows (family=rows/clips): " + string.Join(" ", fam.ToArray()));
            return table.entries.Count;
        }

        static string Row(QbClipTable.Entry e)
        {
            Func<float, string> F = v => v.ToString("0.###", CultureInfo.InvariantCulture);
            return string.Join(",", new[] { e.clip, e.family, e.kind, F(e.a), F(e.b), F(e.sync), e.hand.ToString(), F(e.yaw0), F(e.yawSync), F(e.handYaw), F(e.dist), F(e.travelDir), F(e.slide), F(e.hips), F(e.lean), F(e.handsY), F(e.score), e.loop ? "1" : "0", e.mirror ?? "", F(e.quality) });
        }

        /// <summary>Every window also exists as its mirror image (the Humanoid Mirror clip setting, twin assets in <c>Resources/MirroredClips</c>): left and right stance / pocket / snap variants, the throwing hand swaps (a right-handed passer gets the left-handed BGS takes, a left-hander the BoNES ones), the yaws and the hips path flip. A mirror row is a clip of its own for the bag and for the recorder.</summary>
        static void AddMirrorRows(QbClipTable table, ArtLibrary lib)
        {
            var src = new List<QbClipTable.Entry>(table.entries);
            MaterialFactory.EnsureFolder(ClipFacingAudit.MirrorDir);
            var twins = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (var e in src)
            {
                e.mirror = ""; e.mirrorOf = "";
                var c = lib.ClipByName(e.clip); if (c == null || e.clip.EndsWith("_M")) continue;
                AnimationClip twin;
                if (!twins.TryGetValue(e.clip, out twin))
                {
                    string path = ClipFacingAudit.MirrorDir + "/Qb_" + ClipCleaner.SafeName(e.clip) + "_M.anim";
                    twin = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                    if (twin == null) { twin = UnityEngine.Object.Instantiate(c); twin.name = "Qb_" + e.clip + "_M"; AssetDatabase.CreateAsset(twin, path); }
                    var set = AnimationUtility.GetAnimationClipSettings(twin); set.mirror = true; set.loopTime = false; AnimationUtility.SetAnimationClipSettings(twin, set);
                    EditorUtility.SetDirty(twin); twins[e.clip] = twin;
                }
                var m = new QbClipTable.Entry
                {
                    clip = twin.name, clipRef = twin, mirrorOf = e.clip, family = e.family, kind = e.kind, a = e.a, b = e.b, sync = e.sync, hand = e.hand == 'R' ? 'L' : (e.hand == 'L' ? 'R' : e.hand),
                    yaw0 = -e.yaw0, yawSync = -e.yawSync, handYaw = -e.handYaw, dist = e.dist, travelDir = -e.travelDir, slide = e.slide, hips = e.hips, lean = e.lean, handsY = e.handsY, score = e.score, loop = e.loop, mirror = "", quality = e.quality, side = -e.side, mvA = e.mvA, mvB = e.mvB
                };
                if (e.path != null) { m.path = (float[])e.path.Clone(); for (int i = 0; i + 1 < m.path.Length; i += 2) m.path[i] = -m.path[i]; }
                table.entries.Add(m);
            }
            AssetDatabase.SaveAssets();
        }

        static void AttachMirrors(QbClipTable table, ArtLibrary lib)
        {
            foreach (var e in table.entries)
            {
                var c = lib.ClipByName(e.clip); var m = c != null ? ClipFacingTable.MirrorOf(c) : null;
                if (m == null) { m = lib.ClipByName(e.clip + "_M"); }
                e.mirror = m != null ? m.name : "";
            }
        }

        static QbClipTable.Entry Make(Feat f, string fam, string kind, int a, int b, int sync, char hand, float score, bool loop, bool full = false)
        {
            float dir; float dist = Travel(f, a, b, out dir);
            float maxDisp = Drift(f, a, b), keep = full ? 1f : ((!loop && maxDisp > 0.10f) ? Mathf.Min(1f, QbClipTable.PathCap / maxDisp) : 0f);      // v0.13.0 QB-2: a fitted take gives ALL its travel back (the view scales it to the simulated path)      // travel the view gives back as a root offset (capped)
            var e = new QbClipTable.Entry
            {
                clip = f.Name, family = fam, kind = kind, a = a / Rate, b = b / Rate, sync = sync > 0 ? sync / Rate : 0f, hand = hand,
                yaw0 = f.Chest[a], yawSync = sync > 0 ? f.Chest[sync] : f.Chest[b], dist = dist, travelDir = dir, slide = Slide(f, a, b, keep), hips = f.HipsY[a], lean = f.Lean[a], handsY = f.HandsY[a], score = score, loop = loop
            };
            if (keep > 0f)
            {
                int n = Mathf.Max(2, Mathf.CeilToInt((b - a) / Rate * QbClipTable.PathRate) + 1); e.path = new float[n * 2];
                for (int i = 0; i < n; i++) { int k = Mathf.Min(f.N - 1, a + Mathf.RoundToInt(i / QbClipTable.PathRate * Rate)); Vector2 d = f.H[k] - f.H[a]; e.path[2 * i] = d.x; e.path[2 * i + 1] = d.y; }
            }
            if (loop) { float sx = 0f, sz = 0f; for (int k = a; k <= b; k++) { sx += Mathf.Sin(f.Hip[k] * Mathf.Deg2Rad); sz += Mathf.Cos(f.Hip[k] * Mathf.Deg2Rad); } e.yaw0 = Mathf.Atan2(sx, sz) * Mathf.Rad2Deg; e.yawSync = e.yaw0; }      // a loop plays with the mean HIP yaw of its window (the match measures and corrects the hips heading)
            {
                float q = 100f;
                q -= 55f * Mathf.Clamp01(e.slide / 0.45f);
                if (!full) q -= 22f * Mathf.Clamp01(e.dist / 0.60f);
                q -= 13f * Mathf.Clamp01((100f - score) / 55f);
                if (sync > 0) q -= 15f * Mathf.Clamp01(Mathf.Abs(Wrap(e.yawSync - e.yaw0)) / 60f);
                e.quality = Mathf.Clamp(q, 0f, 100f);
            }
            if (sync > 0 && (hand == 'R' || hand == 'L'))
            {
                int hb = hand == 'R' ? AnimRig.RH : AnimRig.LH; int s = Mathf.Clamp(sync, 1, f.N - 2);
                Vector3 hv = f.P[s + 1][hb] - f.P[s - 1][hb]; hv.y = 0f; e.handYaw = hv.sqrMagnitude > 1e-6f ? YawOf(hv) : e.yawSync;
            }
            return e;
        }

        // limits per stable family
        static readonly Lim LimStanceSg = new Lim { HipsLo = 0.74f, HipsHi = 0.97f, LeanLo = 0f, LeanHi = 24f, HandsLo = 0.90f, HandsHi = 1.60f, ChestMax = 14f, DriftMax = 0.10f, MinLen = 1.4f, MaxLen = 6f, SlideMax = 0.22f, SeamMax = 0.045f };
        static readonly Lim LimStanceUc = new Lim { HipsLo = 0.66f, HipsHi = 0.92f, LeanLo = 8f, LeanHi = 32f, HandsLo = 0.50f, HandsHi = 1.15f, ChestMax = 18f, DriftMax = 0.12f, MinLen = 1.0f, MaxLen = 6f, SlideMax = 0.30f, SeamMax = 0.07f };
        static readonly Lim LimPocket = new Lim { HipsLo = 0.74f, HipsHi = 0.97f, LeanLo = 0f, LeanHi = 26f, HandsLo = 0.98f, HandsHi = 1.46f, ChestMax = 30f, DriftMax = 0.16f, MinLen = 1.6f, MaxLen = 5f, SlideMax = 0.28f, SeamMax = 0.07f };

        static void Windows(Feat f, AnimationClip c, float score, List<QbClipTable.Entry> outList, StringBuilder rej)
        {
            string n = c.name; string low = n.ToLowerInvariant();
            if (FitWindows(f, c, score, outList, rej)) return;      // v0.13.0 QB-2: the fitted families (drop, side-step, fake, ground takes, runs), found by name
            bool uc = low.Contains("undercenter") || low.Contains("undrctr") || low.Contains("undercener") || low.Contains("under_c");
            bool cadence = low.Contains("cadence");
            bool snapLike = low.Contains("shotgun_to_catch") || low.Contains("shotgun_snap_catch") || low.Contains("undercenter_to_catch_bad") || low.Contains("undercenter_snap");
            bool throwLike = Regex.IsMatch(low, @"pass|throw|lob|bullet|shovel|toss|underhand|sidearm|siderman|hail|jump|offbalan|offbalanace|pumpfake|fthrow");
            bool dropLike = Regex.IsMatch(low, @"dropback");

            // ---- cadence: the whole clip, in place
            if (cadence && f.N / Rate <= 4.2f)
            {
                int b = f.N - 1; float dir; float dist = Travel(f, 0, b, out dir);
                if (dist <= 0.35f && MaxChest(f, 0, b) <= 30f && Slide(f, 0, b) <= 0.4f && PoseRange(f, 0, b, uc ? 0.64f : 0.72f, 33f)) outList.Add(Make(f, uc ? QbFam.CadenceUc : QbFam.CadenceSg, "cadence", 0, b, 0, '?', score, false));
                else rej.AppendLine(n + ",cadence,dist " + dist.ToString("0.00") + " chest " + MaxChest(f, 0, b).ToString("0") + " slide " + Slide(f, 0, b).ToString("0.00"));
                return;
            }

            // ---- stable windows: stance (shotgun / under centre) and the pocket
            if (!Regex.IsMatch(low, @"celebrat|cheer|frustrat|dive|slide|kneel|spike|sneak|falling|hail|toss|fakehandoff|freestyle") && (!throwLike || low.Contains("loop") || low.Contains("ready_to") || low.Contains("pocket") || low.Contains("neutral") || low.Contains("stance") || low.Contains("shotgun")))
            {
                foreach (var w in StableWindows(f, uc ? LimStanceUc : LimStanceSg)) outList.Add(Make(f, uc ? QbFam.StanceUc : QbFam.StanceSg, "stance", w[0], w[1], 0, '?', score, true));
                if (!uc) foreach (var w in StableWindows(f, LimPocket))
                    {
                        // a pocket window that is also a (narrower) stance window is the same loop twice: keep the one with the larger turn (scanning) only
                        outList.Add(Make(f, QbFam.Pocket, "pocket", w[0], w[1], 0, '?', score, true));
                    }
            }

            // ---- the snap: the hands closing on the ball
            if (snapLike)
            {
                bool right;
                int t = HandsClose(f, out right);
                if (t > 0)
                {
                    int a = Mathf.Max(0, t - Mathf.RoundToInt(0.8f * Rate)), b = Mathf.Min(f.N - 1, t + Mathf.RoundToInt(0.8f * Rate));
                    float dir; float dist = Drift(f, a, b); float skp = dist > 0.10f ? Mathf.Min(1f, QbClipTable.PathCap / dist) : 0f; float sl = Slide(f, a, b, skp);
                    if (dist <= 1.0f && MaxChest(f, a, b) <= 30f && sl <= 0.30f && f.HipsY[a] >= 0.72f && PoseRange(f, a, b, uc ? 0.66f : 0.70f, uc ? 33f : 36f)) outList.Add(Make(f, uc ? QbFam.SnapUc : QbFam.SnapSg, "snap", a, b, t, '?', score, false));
                    else rej.AppendLine(n + ",snap,dist " + dist.ToString("0.00") + " chest " + MaxChest(f, a, b).ToString("0") + " slide " + sl.ToString("0.00"));
                }
            }

            // ---- the under-centre snap: the catch-and-drop takes start in the stance, the hands take the ball at the first hand peak, the drop starts about a quarter second later
            if (Regex.IsMatch(low, @"undercen\w*_catch_to_|undercener_catch|undercenter_snap"))
            {
                int s = -1; int lim = Mathf.Min(f.N - 4, Mathf.RoundToInt(1.6f * Rate));
                for (int i = 6; i < lim; i++) { float sp = Mathf.Max(f.RelR[i], f.RelL[i]); if (sp >= 1.5f && sp >= Mathf.Max(f.RelR[i - 1], f.RelL[i - 1]) && sp >= Mathf.Max(f.RelR[i + 1], f.RelL[i + 1])) { s = i; break; } }
                if (s > 0)
                {
                    int a = 0, b = Mathf.Min(f.N - 1, s + Mathf.RoundToInt(0.35f * Rate));
                    float drift = Drift(f, a, b), kp = drift > 0.10f ? Mathf.Min(1f, QbClipTable.PathCap / drift) : 0f, sl = Slide(f, a, b, kp);
                    if (drift <= 0.7f && MaxChest(f, a, b) <= 30f && sl <= 0.35f && PoseRange(f, a, b, 0.66f, 33f)) outList.Add(Make(f, QbFam.SnapUc, "snap", a, b, s, '?', score, false));
                    else rej.AppendLine(n + ",snap_uc,drift " + drift.ToString("0.00") + " chest " + MaxChest(f, a, b).ToString("0") + " slide " + sl.ToString("0.00"));
                }
                else rej.AppendLine(n + ",snap_uc,no hand peak");
            }

            // ---- throws
            if (throwLike)
            {
                bool[] rh; var peaks = HandPeaks(f, 4.5f, 1.6f, out rh);
                for (int p = 0; p < peaks.Count; p++)
                {
                    int s = peaks[p]; char hand = rh[p] ? 'R' : 'L';
                    int hb = hand == 'R' ? AnimRig.RH : AnimRig.LH;
                    string kind = ThrowKind(n);
                    float hy = f.P[s][hb].y - f.Ground;
                    if (hy < 1.05f) { rej.AppendLine(n + "@" + (s / Rate).ToString("0.00") + ",throw,hand low " + hy.ToString("0.00")); continue; }
                    int b = Mathf.Min(f.N - 1, s + Mathf.RoundToInt(0.9f * Rate));
                    bool onRun = kind == "run";
                    string fam = kind == "fall" ? QbFam.ThrowFall : kind == "off" ? QbFam.ThrowOff : kind == "shovel" ? QbFam.ThrowShovel : kind == "pump" ? QbFam.Pump : onRun ? QbFam.ThrowRun : QbFam.ThrowSet;
                    float driftMax = onRun ? 9f : (kind == "off" || kind == "fall" ? 1.6f : 1.2f), slideMax = onRun ? 9f : (kind == "off" || kind == "fall" ? 0.60f : 0.38f);
                    int chosen = -1; string why = "";
                    foreach (float lead in new[] { 1.0f, 0.8f, 0.65f, 0.5f, 0.4f })
                    {
                        // the lead-in starts from the ready position (hands up near the chest, nearly still) and the window stays put: little travel, little foot slide
                        int a = Mathf.Max(0, s - Mathf.RoundToInt(lead * Rate));
                        while (a < s - 6 && !(f.HandsY[a] >= 0.90f && f.HandsY[a] <= 1.85f && f.HandsD[a] <= 0.75f && f.RelR[a] < 3.0f && f.RelL[a] < 3.0f && f.HipsY[a] >= 0.72f)) a++;
                        if (s - a < Mathf.RoundToInt(0.28f * Rate)) { why = "lead-in " + ((s - a) / Rate).ToString("0.00") + " s"; continue; }
                        float drift = Drift(f, a, b), kp = drift > 0.10f ? Mathf.Min(1f, QbClipTable.PathCap / drift) : 0f, sl = Slide(f, a, b, kp);
                        if (drift > driftMax || sl > slideMax || f.HipsY[a] < 0.70f) { why = "drift " + drift.ToString("0.00") + " slide " + sl.ToString("0.00") + " hips " + f.HipsY[a].ToString("0.00"); continue; }
                        float turn = Mathf.Abs(Wrap(f.Chest[s] - f.Chest[a]));
                        if (!onRun && f.ChestRaw[s] > 50f) { why = "shoulder line unreliable at the release (" + f.ChestRaw[s].ToString("0") + " deg off the hips)"; continue; }
                        if (!onRun && kind != "off" && kind != "fall" && turn > 70f) { why = "turn " + turn.ToString("0"); continue; }
                        chosen = a; break;
                    }
                    if (chosen < 0) { rej.AppendLine(n + "@" + (s / Rate).ToString("0.00") + "," + fam + "," + why); continue; }
                    outList.Add(Make(f, fam, kind, chosen, b, s, hand, score, false));
                }
            }
        }

        /// <summary>The frame the hands close on the ball: the minimum hand distance after the first fifth of the clip, with the hands in front of the body and nearly still before it.</summary>
        static int HandsClose(Feat f, out bool right)
        {
            right = true; int best = -1; float bd = float.MaxValue;
            for (int i = Mathf.RoundToInt(f.N * 0.08f); i < f.N - 6; i++)
            {
                if (f.HandsD[i] < bd && f.HandsY[i] > 0.7f) { bd = f.HandsD[i]; best = i; }
            }
            return bd < 0.32f ? best : -1;
        }
    }
}
