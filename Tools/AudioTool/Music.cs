using System.Globalization;
using System.Numerics;
using System.Text;

namespace PixAudio;

/// <summary>
/// Music production verbs (v0.11.0 AUDIO+CROWD music track).
///   mgrid  bowl|house [src...]              beat-grid estimate (bpm, first beat) from onset flux
///   mrecon bowl|house                       stem reconstruction: LS gains of the stems vs the mix, residual energy overall / per band / per window
///   mbars  bowl|house bpm t0 [beatsPerBar]  per-bar RMS table of mix + every stem (section picking)
///   mfeat  file.wav|srcname [start end]     feature line (spectrum, syllabic modulation, harmonic peaks)
///   mrender spec.txt                        render every cue in a spec (loops + stingers) and print a table
///   mcheck dir                              loop-seam + loudness + peak check of every wav in the dir
/// </summary>
public static class MusicVerbs
{
    public static IDictionary<string, Func<string[], int>> Register() => new Dictionary<string, Func<string[], int>>
    {
        ["mgrid"] = Grid, ["mrecon"] = Recon, ["mbars"] = Bars, ["mfeat"] = Feat, ["mrender"] = MusicRender.Run, ["mseam"] = MusicRender.SeamScan, ["mcheck"] = MusicRender.Check,
    };

    static int Grid(string[] a)
    {
        string set = a[0]; var names = a.Length > 1 ? a.Skip(1).ToArray() : new[] { set + ".mix", set + ".Perc" };
        var clips = names.Select(MusicLib.Src).ToArray();
        var coarse = MusicLib.Grid(clips, 70, 180, 0.05);
        Console.WriteLine($"coarse bpm={coarse.bpm:F2} t0={coarse.t0:F3} score={coarse.score:F3}");
        var fine = MusicLib.Grid(clips, coarse.bpm - 0.3, coarse.bpm + 0.3, 0.002);
        Console.WriteLine($"fine   bpm={fine.bpm:F3} t0={fine.t0:F3} score={fine.score:F3}  (beat={60 / fine.bpm:F4}s bar4={240 / fine.bpm:F4}s)");
        foreach (var half in new[] { 0.5, 2.0 / 3, 1.5, 2.0 })
        {
            double lo = fine.bpm * half; var g = MusicLib.Grid(clips, lo - 0.3, lo + 0.3, 0.01);
            Console.WriteLine($"  alt x{half:F2}: bpm={g.bpm:F2} t0={g.t0:F3} score={g.score:F3}");
        }
        return 0;
    }

    static int Recon(string[] a)
    {
        string set = a[0]; double bLo = Program.Opt(a, "--lo", 0), bHi = Program.Opt(a, "--hi", 0); var mix = MusicLib.Src(set + ".mix"); var stems = MusicLib.StemNames(set);
        if (bLo > 0 || bHi > 0) { mix = mix.Copy(); if (bLo > 0) MusicLib.Filter(mix, true, bLo); if (bHi > 0) MusicLib.Filter(mix, false, bHi); }
        var sc = stems.Select(MusicLib.Src).ToArray(); int k = stems.Length; int n = mix.N;
        if (bLo > 0 || bHi > 0) { sc = sc.Select(c => { var d = c.Copy(); if (bLo > 0) MusicLib.Filter(d, true, bLo); if (bHi > 0) MusicLib.Filter(d, false, bHi); return d; }).ToArray(); Console.WriteLine($"[band-limited fit {bLo}-{bHi} Hz]"); }
        { double tf = Program.Opt(a, "--from", -1), tt = Program.Opt(a, "--to", -1); if (tf >= 0) { mix = Dsp.Slice(mix, tf, tt); sc = sc.Select(c => Dsp.Slice(c, tf, tt)).ToArray(); n = mix.N; Console.WriteLine($"[range {tf}-{tt} s]"); } }
        {
            var sum = new Clip(mix.Rate, 2, sc[0].N); foreach (var s in sc) for (int ch = 0; ch < 2; ch++) for (int t = 0; t < Math.Min(sum.N, s.N); t++) sum.D[ch][t] += s.D[ch][t];
            int lag = Program.Flag(a, "--nolag") ? 0 : MusicLag.Find(mix, sum, 6.0);
            Console.WriteLine($"stem-sum vs mix lag: stems[t+{lag}] ~ mix[t]  ({lag / 44100.0:F4} s)");
            for (int i = 0; i < k; i++) sc[i] = MusicLag.Shift(sc[i], lag, mix.N);
            n = mix.N;
        }
        double[,] G = new double[k, k]; double[] r = new double[k];
        for (int ch = 0; ch < 2; ch++)
        {
            for (int i = 0; i < k; i++)
            {
                var x = sc[i].D[ch]; double acc = 0; var m = mix.D[ch]; for (int t = 0; t < n; t++) acc += (double)x[t] * m[t]; r[i] = r[i] + acc;
                for (int j = i; j < k; j++) { var y = sc[j].D[ch]; double g = 0; for (int t = 0; t < n; t++) g += (double)x[t] * y[t]; G[i, j] += g; if (j != i) G[j, i] += g; }
            }
        }
        var gain = Solve(G, r); double me = 0; for (int ch = 0; ch < 2; ch++) for (int t = 0; t < n; t++) me += (double)mix.D[ch][t] * mix.D[ch][t];
        double re = 0; var res = new Clip(mix.Rate, 2, n);
        for (int ch = 0; ch < 2; ch++) for (int t = 0; t < n; t++) { double s = 0; for (int i = 0; i < k; i++) s += gain[i] * sc[i].D[ch][t]; double d = mix.D[ch][t] - s; res.D[ch][t] = (float)d; re += d * d; }
        Console.WriteLine($"{set}: stems={string.Join(",", stems.Select(s => s.Substring(set.Length + 1)))}");
        Console.WriteLine("global LS gains (dB): " + string.Join(" ", stems.Select((s, i) => $"{s.Substring(set.Length + 1)}={Dsp.Db(Math.Abs(gain[i])):F1}{(gain[i] < 0 ? "(neg)" : "")}")));
        Console.WriteLine($"GLOBAL residual: {10 * Math.Log10(re / me):F1} dB below mix  (explained {(1 - re / me) * 100:F1}% of energy)");
        {
            // bus-compressor model: mix ~ g(t) * sum_i w_i stem_i, g(t) a free scalar per block (stem weights = global LS gains)
            foreach (double blkSec in new[] { 0.1, 0.5 })
            {
                int blk = (int)(blkSec * mix.Rate); double e2 = 0;
                for (int st = 0; st + blk <= n; st += blk)
                {
                    double xy = 0, yy = 0;
                    for (int ch = 0; ch < 2; ch++) for (int t = st; t < st + blk; t++) { double y = 0; for (int i = 0; i < k; i++) y += gain[i] * sc[i].D[ch][t]; xy += y * mix.D[ch][t]; yy += y * y; }
                    double g = xy / (yy + 1e-30);
                    for (int ch = 0; ch < 2; ch++) for (int t = st; t < st + blk; t++) { double y = 0; for (int i = 0; i < k; i++) y += gain[i] * sc[i].D[ch][t]; double d = mix.D[ch][t] - g * y; e2 += d * d; }
                }
                Console.WriteLine($"BUS-DYNAMICS model (free scalar gain per {blkSec * 1000:F0} ms): residual {10 * Math.Log10(e2 / me):F1} dB below mix");
            }
        }
        int win = 4 * mix.Rate; var wr = new List<double>(); double reL = 0;
        for (int st = 0; st + win <= n; st += win)
        {
            var Gw = new double[k, k]; var rw = new double[k];
            for (int ch = 0; ch < 2; ch++)
                for (int i = 0; i < k; i++)
                {
                    var x = sc[i].D[ch]; double acc = 0; for (int t = st; t < st + win; t++) acc += (double)x[t] * mix.D[ch][t]; rw[i] += acc;
                    for (int j = i; j < k; j++) { var y = sc[j].D[ch]; double g = 0; for (int t = st; t < st + win; t++) g += (double)x[t] * y[t]; Gw[i, j] += g; if (j != i) Gw[j, i] += g; }
                }
            for (int i = 0; i < k; i++) Gw[i, i] += 1e-6 * (Gw[i, i] + 1e-12);
            var gw = Solve(Gw, rw); double e1 = 0, e2 = 0;
            for (int ch = 0; ch < 2; ch++) for (int t = st; t < st + win; t++) { double s = 0; for (int i = 0; i < k; i++) s += gw[i] * sc[i].D[ch][t]; double d = mix.D[ch][t] - s; e2 += d * d; e1 += (double)mix.D[ch][t] * mix.D[ch][t]; }
            wr.Add(10 * Math.Log10((e2 + 1e-12) / (e1 + 1e-12))); reL += e2;
        }
        wr.Sort();
        Console.WriteLine($"LOCAL(4 s windows, per-window gains) residual: median {wr[wr.Count / 2]:F1} dB, worst {wr[^1]:F1} dB, best {wr[0]:F1} dB over {wr.Count} windows; pooled {10 * Math.Log10(reL / me):F1} dB");
        BandReport("mix", mix, n); BandReport("residual(global)", res, n);
        var fm = Dsp.Analyze(mix, false); var fr = Dsp.Analyze(res, false);
        Console.WriteLine($"mix      : {fm}\nresidual : {fr}");
        return 0;
    }

    static void BandReport(string name, Clip c, int n)
    {
        const int N = 8192; double hz = c.Rate / (double)N; var win = new double[N]; for (int i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N);
        var buf = new Complex[N]; double[] be = new double[5]; double[] edges = { 0, 100, 300, 3400, 8000, 1e9 };
        var m = Dsp.Mono(c);
        for (int st = 0; st + N <= n; st += N * 4)
        {
            for (int i = 0; i < N; i++) buf[i] = m.D[0][st + i] * win[i]; Dsp.Fft(buf);
            for (int kk = 1; kk < N / 2; kk++) { double f = kk * hz, e = buf[kk].Real * buf[kk].Real + buf[kk].Imaginary * buf[kk].Imaginary; for (int b = 0; b < 5; b++) if (f >= edges[b] && f < edges[b + 1]) { be[b] += e; break; } }
        }
        double tot = be.Sum() + 1e-18;
        Console.WriteLine($"  bands {name,-17} <100: {Pc(be[0], tot)}  100-300: {Pc(be[1], tot)}  0.3-3.4k: {Pc(be[2], tot)}  3.4-8k: {Pc(be[3], tot)}  >8k: {Pc(be[4], tot)}   (abs total {10 * Math.Log10(tot / 1e6):F1} dB)");
    }
    static string Pc(double v, double t) => (100 * v / t).ToString("F1", CultureInfo.InvariantCulture) + "%";

    public static double[] Solve(double[,] A, double[] b)
    {
        int n = b.Length; var M = new double[n, n + 1];
        for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) M[i, j] = A[i, j]; M[i, n] = b[i]; }
        for (int c = 0; c < n; c++)
        {
            int p = c; for (int r = c + 1; r < n; r++) if (Math.Abs(M[r, c]) > Math.Abs(M[p, c])) p = r;
            for (int j = 0; j <= n; j++) (M[c, j], M[p, j]) = (M[p, j], M[c, j]);
            double d = M[c, c]; if (Math.Abs(d) < 1e-30) continue;
            for (int r = 0; r < n; r++) { if (r == c) continue; double f = M[r, c] / d; for (int j = c; j <= n; j++) M[r, j] -= f * M[c, j]; }
        }
        var x = new double[n]; for (int i = 0; i < n; i++) x[i] = Math.Abs(M[i, i]) < 1e-30 ? 0 : M[i, n] / M[i, i]; return x;
    }

    static int Bars(string[] a)
    {
        string set = a[0]; double bpm = double.Parse(a[1], CultureInfo.InvariantCulture), t0 = double.Parse(a[2], CultureInfo.InvariantCulture); int bpb = a.Length > 3 ? int.Parse(a[3]) : 4;
        var names = new[] { set + ".mix" }.Concat(MusicLib.StemNames(set)).ToArray(); var cl = names.Select(MusicLib.Src).ToArray();
        double bar = 60.0 / bpm * bpb;
        Console.WriteLine("bar  t(s)   " + string.Join(" ", names.Select(s => s.Substring(set.Length + 1).PadLeft(7))));
        for (int b = 0; ; b++)
        {
            double s = t0 + b * bar, e = s + bar; if (e > cl[0].Dur) break;
            var sb = new StringBuilder($"{b,3} {s,7:F2}");
            foreach (var c in cl)
            {
                int i0 = (int)(s * c.Rate), i1 = Math.Min(c.N, (int)(e * c.Rate)); double acc = 0; for (int ch = 0; ch < c.Ch; ch++) for (int i = i0; i < i1; i++) acc += (double)c.D[ch][i] * c.D[ch][i];
                sb.Append($" {10 * Math.Log10(acc / ((i1 - i0) * c.Ch) + 1e-12),7:F1}");
            }
            Console.WriteLine(sb);
        }
        return 0;
    }

    static int Feat(string[] a)
    {
        var c = MusicLib.Src(a[0]);
        if (a.Length >= 3) c = Dsp.Slice(c, double.Parse(a[1], CultureInfo.InvariantCulture), double.Parse(a[2], CultureInfo.InvariantCulture));
        Console.WriteLine(FeatLine(a[0], c));
        return 0;
    }

    public static string FeatLine(string name, Clip c)
    {
        var f = Dsp.Analyze(c, false);
        return $"{name,-30} dur={f.Dur,6:F1} lufs={f.Lufs,6:F1} cen={f.Centroid,5:F0} r85={f.Rolloff85,5:F0} hi(3.4-8k)={f.BandHigh * 100,4:F1}% air={f.BandAir * 100,4:F1}% syl={f.Syllabic:F2} harm={f.Harm:F2} flat={f.Flatness:F4}";
    }
}
public static class MusicLagVerbs
{
    public static IDictionary<string, Func<string[], int>> Register() => new Dictionary<string, Func<string[], int>>
    {
        ["mlag"] = a =>
        {
            string set = a[0]; var mix = MusicLib.Src(set + ".mix"); double lo = Program.Opt(a, "--lo", 0), hi = Program.Opt(a, "--hi", 0);
            Clip Prep(Clip c) { c = c.Copy(); if (lo > 0) MusicLib.Filter(c, true, lo); if (hi > 0) MusicLib.Filter(c, false, hi); return c; }
            var m = Prep(mix);
            foreach (var s in MusicLib.StemNames(set)) { var c = Prep(MusicLib.Src(s)); int lag = MusicLag.Find(m, c, 8.0); Console.WriteLine($"{s,-14} lag={lag,8} ({lag / 44100.0,8:F4}s) corr={MusicLag.LastCorr:F3}"); }
            return 0;
        },
    };
}
