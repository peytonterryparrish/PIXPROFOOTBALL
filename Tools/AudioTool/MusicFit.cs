using System.Numerics;
namespace PixAudio;

/// <summary>
/// mfit set [--win 10] [--n 8192] [--shift s] [--inject file --injdb d] [--drop stemname]
/// Frequency-dependent complex least squares: per 1/6-octave band and 10 s window, mix STFT bins ~ sum_i c_i * stem_i STFT bins (c_i complex).
/// Handles EQ, delay and phase of the mix bus; not reverb tails or nonlinear limiting. Reports explained energy per band (and the leftover).
/// </summary>
public static class MusicFitVerbs
{
    public static IDictionary<string, Func<string[], int>> Register() => new Dictionary<string, Func<string[], int>> { ["mfit"] = Fit };

    static Complex[] SolveC(Complex[,] A, Complex[] b)
    {
        int n = b.Length; var M = new Complex[n, n + 1];
        for (int i = 0; i < n; i++) { for (int j = 0; j < n; j++) M[i, j] = A[i, j]; M[i, n] = b[i]; }
        for (int c = 0; c < n; c++)
        {
            int p = c; for (int r = c + 1; r < n; r++) if (M[r, c].Magnitude > M[p, c].Magnitude) p = r;
            for (int j = 0; j <= n; j++) (M[c, j], M[p, j]) = (M[p, j], M[c, j]);
            var d = M[c, c]; if (d.Magnitude < 1e-30) continue;
            for (int r = 0; r < n; r++) { if (r == c) continue; var f = M[r, c] / d; for (int j = c; j <= n; j++) M[r, j] -= f * M[c, j]; }
        }
        var x = new Complex[n]; for (int i = 0; i < n; i++) x[i] = M[i, i].Magnitude < 1e-30 ? 0 : M[i, n] / M[i, i]; return x;
    }

    static Complex[][] Stft(float[] m, int start, int frames, int N, int H, double[] win)
    {
        var o = new Complex[frames][]; var buf = new Complex[N];
        for (int f = 0; f < frames; f++)
        {
            int s = start + f * H; for (int i = 0; i < N; i++) buf[i] = (s + i < m.Length ? m[s + i] : 0f) * win[i];
            Dsp.Fft(buf); var row = new Complex[N / 2]; Array.Copy(buf, row, N / 2); o[f] = row;
        }
        return o;
    }

    static int Fit(string[] a)
    {
        string set = a[0]; double winSec = Program.Opt(a, "--win", 10); int N = (int)Program.Opt(a, "--n", 8192), H = N / 4;
        var names = MusicLib.StemNames(set).ToList(); var mix = MusicLib.Src(set + ".mix");
        int di = Array.IndexOf(a, "--drop"); if (di >= 0) names.RemoveAll(s => s.EndsWith("." + a[di + 1], StringComparison.OrdinalIgnoreCase));
        int ii = Array.IndexOf(a, "--inject"); double injDb = Program.Opt(a, "--injdb", -99), shiftSec = Program.Opt(a, "--shift", 0);
        if (ii >= 0)
        {
            var song = Wav.Read(a[ii + 1]); if (song.Rate != mix.Rate) song = Dsp.Resample(song, mix.Rate); song = Dsp.Stereo(Dsp.Slice(song, 40, 40 + mix.Dur));
            double gdb = (Dsp.Lufs(mix) + injDb) - Dsp.Lufs(song); mix = mix.Copy(); float gg = (float)Dsp.FromDb(gdb);
            for (int c = 0; c < 2; c++) for (int t = 0; t < Math.Min(mix.N, song.N); t++) mix.D[c][t] += song.D[c][t] * gg;
            Console.WriteLine($"[injected {Path.GetFileName(a[ii + 1])} at {injDb} dB rel mix LUFS]");
        }
        var mm = Dsp.Mono(mix).D[0]; var ss = names.Select(n => Dsp.Mono(MusicLib.Src(n)).D[0]).ToArray(); int k = ss.Length;
        if (shiftSec != 0) { int sh = (int)(shiftSec * mix.Rate); ss = ss.Select(x => { var o = new float[x.Length]; for (int i = 0; i < o.Length; i++) o[i] = x[(i + sh) % x.Length]; return o; }).ToArray(); Console.WriteLine($"[stems shifted {shiftSec}s: negative control]"); }
        var edges = new List<double>(); for (double f = 40; f < 9000; f *= Math.Pow(2, 1.0 / 6)) edges.Add(f); var ed = edges.ToArray(); int nb = ed.Length - 1;
        double hz = mix.Rate / (double)N; var win = new double[N]; for (int i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N);
        var em = new double[nb]; var er = new double[nb];
        int wframes = (int)(winSec * mix.Rate / H), nWin = (mm.Length - N) / H / wframes;
        for (int w = 0; w < nWin; w++)
        {
            int start = w * wframes * H; var M = Stft(mm, start, wframes, N, H, win); var S = ss.Select(x => Stft(x, start, wframes, N, H, win)).ToArray();
            for (int b = 0; b < nb; b++)
            {
                int k0 = (int)Math.Ceiling(ed[b] / hz), k1 = (int)Math.Ceiling(ed[b + 1] / hz) - 1; if (k1 < k0) continue;
                var G = new Complex[k, k]; var r = new Complex[k]; double e = 0;
                for (int f = 0; f < wframes; f++)
                    for (int kk = k0; kk <= k1; kk++)
                    {
                        var m = M[f][kk]; e += m.Real * m.Real + m.Imaginary * m.Imaginary;
                        for (int i = 0; i < k; i++)
                        {
                            var ci = Complex.Conjugate(S[i][f][kk]); r[i] += ci * m;
                            for (int j = 0; j < k; j++) G[i, j] += ci * S[j][f][kk];
                        }
                    }
                for (int i = 0; i < k; i++) G[i, i] *= 1.000001;
                var c = SolveC(G, r); double res = 0;
                for (int f = 0; f < wframes; f++)
                    for (int kk = k0; kk <= k1; kk++)
                    {
                        Complex fit = 0; for (int i = 0; i < k; i++) fit += c[i] * S[i][f][kk];
                        var d = M[f][kk] - fit; res += d.Real * d.Real + d.Imaginary * d.Imaginary;
                    }
                em[b] += e; er[b] += res;
            }
        }
        Console.WriteLine($"{set}: stems={string.Join(",", names.Select(n => n.Substring(set.Length + 1)))} N={N} windows={nWin}x{winSec}s");
        Console.WriteLine("band(Hz)   explained%  residual_dB");
        double tm = 0, tr = 0, vm = 0, vr = 0, lm = 0, lr = 0;
        for (int b = 0; b < nb; b++)
        {
            double fc = Math.Sqrt(ed[b] * ed[b + 1]); tm += em[b]; tr += er[b]; if (fc >= 250 && fc <= 3400) { vm += em[b]; vr += er[b]; } if (fc < 250) { lm += em[b]; lr += er[b]; }
            if (b % 2 == 0) Console.WriteLine($"{fc,8:F0}    {100 * (1 - er[b] / (em[b] + 1e-30)),6:F1}     {10 * Math.Log10(er[b] / (em[b] + 1e-30) + 1e-12),6:F1}");
        }
        Console.WriteLine($"ALL 40-9000 Hz: residual {10 * Math.Log10(tr / tm):F1} dB below mix ({100 * (1 - tr / tm):F1}% explained)");
        Console.WriteLine($"<250 Hz       : residual {10 * Math.Log10(lr / lm):F1} dB ({100 * (1 - lr / lm):F1}%)   250-3400 Hz (vocal band): residual {10 * Math.Log10(vr / vm):F1} dB ({100 * (1 - vr / vm):F1}%)");
        return 0;
    }
}
