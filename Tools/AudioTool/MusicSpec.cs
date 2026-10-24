using System.Globalization;
using System.Numerics;
using System.Text;

namespace PixAudio;

/// <summary>
/// mspec set [--win 10] : phase-agnostic stem explanation. For every third-octave band and every 10 s window the mix band-energy trajectory
/// is fitted by a non-negative mix of the stems' band-energy trajectories (reverb / limiting / EQ on the mix bus do not break this the way
/// a sample-exact least squares does). Prints per band: share of the mix energy the stems explain and the share left over.
/// A voice present in the mix but in no stem would show as leftover energy concentrated in 200-3400 Hz.
/// </summary>
public static class MusicSpecVerbs
{
    public static IDictionary<string, Func<string[], int>> Register() => new Dictionary<string, Func<string[], int>> { ["mspec"] = Spec };

    public static double[][] BandTraj(Clip c, double[] edges, int N, int H)
    {
        var m = Dsp.Mono(c).D[0]; int frames = Math.Max(0, (m.Length - N) / H + 1); double hz = c.Rate / (double)N;
        var o = new double[edges.Length - 1][]; for (int b = 0; b < o.Length; b++) o[b] = new double[frames];
        var win = new double[N]; for (int i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N);
        var buf = new Complex[N];
        for (int f = 0; f < frames; f++)
        {
            for (int i = 0; i < N; i++) buf[i] = m[f * H + i] * win[i];
            Dsp.Fft(buf);
            for (int k = 1; k < N / 2; k++)
            {
                double fq = k * hz; if (fq < edges[0] || fq >= edges[^1]) continue;
                int b = 0; while (b + 1 < edges.Length - 1 && fq >= edges[b + 1]) b++;
                o[b][f] += buf[k].Real * buf[k].Real + buf[k].Imaginary * buf[k].Imaginary;
            }
        }
        return o;
    }

    public static double[] Nnls(double[,] G, double[] r, int iters = 300)
    {
        int k = r.Length; var g = new double[k];
        for (int it = 0; it < iters; it++)
            for (int i = 0; i < k; i++)
            {
                double s = r[i]; for (int j = 0; j < k; j++) if (j != i) s -= G[i, j] * g[j];
                g[i] = G[i, i] > 1e-30 ? Math.Max(0, s / G[i, i]) : 0;
            }
        return g;
    }

    static int Spec(string[] a)
    {
        string set = a[0]; double winSec = Program.Opt(a, "--win", 10);
        var names = MusicLib.StemNames(set); var mix = MusicLib.Src(set + ".mix");
        double shiftSec = Program.Opt(a, "--shift", 0), injDb = Program.Opt(a, "--injdb", -99); int ii = Array.IndexOf(a, "--inject");
        if (ii >= 0) { var song = Wav.Read(a[ii + 1]); if (song.Rate != mix.Rate) song = Dsp.Resample(song, mix.Rate); song = Dsp.Slice(Dsp.Stereo(song), 40, 40 + mix.Dur); double gdb = (Dsp.Lufs(mix) + injDb) - Dsp.Lufs(song); mix = mix.Copy(); float gg = (float)Dsp.FromDb(gdb); for (int c = 0; c < 2; c++) for (int t = 0; t < Math.Min(mix.N, song.N); t++) mix.D[c][t] += song.D[c][t] * gg; Console.WriteLine($"[injected {Path.GetFileName(a[ii + 1])} at {injDb} dB rel mix LUFS]"); }
        var edges = new List<double>(); for (double f = 40; f < 9000; f *= Math.Pow(2, 1.0 / 3)) edges.Add(f); var ed = edges.ToArray();
        const int N = 4096, H = 2048;
        var mt = BandTraj(mix, ed, N, H); var st = names.Select(n => BandTraj(MusicLib.Src(n), ed, N, H)).ToArray();
        if (shiftSec != 0) { int sf = (int)(shiftSec * mix.Rate / H); foreach (var s in st) for (int b = 0; b < s.Length; b++) { var o = new double[s[b].Length]; for (int t = 0; t < o.Length; t++) o[t] = s[b][(t + sf + o.Length) % o.Length]; s[b] = o; } Console.WriteLine($"[stems circularly shifted {shiftSec}s: negative control]"); }
        int nb = ed.Length - 1, frames = mt[0].Length, wf = (int)(winSec * mix.Rate / H);
        Console.WriteLine($"{set}: {names.Length} stems, {nb} third-octave bands, {frames} frames, window {winSec}s");
        Console.WriteLine("band(Hz)       mix_dB  explained%  leftover_dB_rel_mix");
        double totMix = 0, totLeft = 0, vMix = 0, vLeft = 0;
        for (int b = 0; b < nb; b++)
        {
            double em = 0, left = 0;
            for (int w0 = 0; w0 + wf <= frames; w0 += wf)
            {
                int k = names.Length; var G = new double[k, k]; var r = new double[k];
                for (int i = 0; i < k; i++)
                {
                    for (int t = w0; t < w0 + wf; t++) r[i] += st[i][b][t] * mt[b][t];
                    for (int j = i; j < k; j++) { double s = 0; for (int t = w0; t < w0 + wf; t++) s += st[i][b][t] * st[j][b][t]; G[i, j] = s; G[j, i] = s; }
                }
                var g = Nnls(G, r);
                for (int t = w0; t < w0 + wf; t++)
                {
                    double fit = 0; for (int i = 0; i < k; i++) fit += g[i] * st[i][b][t];
                    em += mt[b][t]; left += Math.Max(0, mt[b][t] - fit);
                }
            }
            double fc = Math.Sqrt(ed[b] * ed[b + 1]);
            Console.WriteLine($"{fc,8:F0}      {10 * Math.Log10(em / 1e6 + 1e-12),6:F1}   {100 * (1 - left / em),6:F1}      {10 * Math.Log10(left / em + 1e-12),6:F1}");
            totMix += em; totLeft += left; if (fc >= 250 && fc <= 3400) { vMix += em; vLeft += left; }
        }
        Console.WriteLine($"ALL bands: stems explain {100 * (1 - totLeft / totMix):F1}% of mix band-energy (leftover {10 * Math.Log10(totLeft / totMix):F1} dB)");
        Console.WriteLine($"250-3400 Hz (vocal band): explained {100 * (1 - vLeft / vMix):F1}% (leftover {10 * Math.Log10(vLeft / vMix):F1} dB)");
        return 0;
    }
}
