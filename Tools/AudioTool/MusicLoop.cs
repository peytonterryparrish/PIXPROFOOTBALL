using System.Globalization;
using System.Numerics;
using System.Text;
namespace PixAudio;

/// <summary>
///   mloop set stemSpec minSec maxSec [--step 2] [--top 8] [--from s --to s]
///       stemSpec = "strings:0,WW:-3,PCH:-6" (stem name : gain dB). Scores bar-aligned loop candidates (a, b) by chroma + level similarity of the 8 beats before a and before b.
///   mhits set stemSpec [--top 25]  : strongest transients (time, rise dB, level, 12 dB decay time) of the layered stems
/// </summary>
public static class MusicLoopVerbs
{
    public static IDictionary<string, Func<string[], int>> Register() => new Dictionary<string, Func<string[], int>> { ["mloop"] = Loop, ["mhits"] = Hits };

    public static readonly Dictionary<string, (double bpm, double t0)> Grids = new() { ["bowl"] = (108.0, 0.290), ["house"] = (90.0, 0.650) };

    public static Clip Layered(string set, string spec)
    {
        Clip sum = null;
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':'); string nm = kv[0].Contains('.') ? kv[0] : set + "." + kv[0]; double g = kv.Length > 1 ? double.Parse(kv[1], CultureInfo.InvariantCulture) : 0;
            var c = MusicLib.Src(nm); if (sum == null) { sum = new Clip(c.Rate, 2, c.N); }
            float gl = (float)Dsp.FromDb(g); for (int ch = 0; ch < 2; ch++) for (int i = 0; i < Math.Min(sum.N, c.N); i++) sum.D[ch][i] += c.D[ch][i] * gl;
        }
        return sum;
    }

    static double[] Chroma(float[] m, int s, int len, int rate)
    {
        int N = 16384; var ch = new double[12]; var buf = new Complex[N]; int use = Math.Min(len, N);
        for (int i = 0; i < N; i++) buf[i] = (i < use && s + i < m.Length) ? m[s + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / use)) : 0;
        Dsp.Fft(buf); double hz = rate / (double)N;
        for (int k = 1; k < N / 2; k++)
        {
            double f = k * hz; if (f < 70 || f > 1600) continue; double p = 12 * Math.Log2(f / 440.0) + 69; int pc = ((int)Math.Round(p) % 12 + 12) % 12;
            ch[pc] += Math.Sqrt(buf[k].Real * buf[k].Real + buf[k].Imaginary * buf[k].Imaginary);
        }
        double n = Math.Sqrt(ch.Sum(x => x * x)) + 1e-18; for (int i = 0; i < 12; i++) ch[i] /= n; return ch;
    }

    static int Loop(string[] a)
    {
        string set = a[0]; var sig = Layered(set, a[1]); double minS = double.Parse(a[2], CultureInfo.InvariantCulture), maxS = double.Parse(a[3], CultureInfo.InvariantCulture);
        int step = (int)Program.Opt(a, "--step", 2), top = (int)Program.Opt(a, "--top", 8); double from = Program.Opt(a, "--from", 0), to = Program.Opt(a, "--to", 1e9);
        var (bpm, t0) = Grids[set]; double beat = 60 / bpm, bar = 4 * beat; int rate = sig.Rate; var m = Dsp.Mono(sig).D[0];
        int nBars = (int)((sig.Dur - t0) / bar); int nBeats = nBars * 4;
        var chroma = new double[nBeats][]; var lvl = new double[nBeats];
        for (int k = 0; k < nBeats; k++)
        {
            int s = (int)((t0 + k * beat) * rate), len = (int)(beat * rate); chroma[k] = Chroma(m, s, len, rate);
            double e = 0; for (int i = 0; i < len && s + i < m.Length; i++) e += (double)m[s + i] * m[s + i]; lvl[k] = 10 * Math.Log10(e / len + 1e-12);
        }
        double Sim(int ba, int bb, int n)
        {
            double cs = 0, ld = 0; for (int i = 1; i <= n; i++) { double d = 0; for (int p = 0; p < 12; p++) d += chroma[ba - i][p] * chroma[bb - i][p]; cs += d; ld += Math.Abs(lvl[ba - i] - lvl[bb - i]); }
            return cs / n - 0.02 * ld / n;
        }
        var res = new List<(double score, int a, int b, double cs8, double cs2)>();
        for (int ba = 2; ba < nBars; ba += step)
            for (int bl = 2; ba + bl <= nBars; bl += step)
            {
                double aS = t0 + ba * bar, bS = t0 + (ba + bl) * bar, dur = bS - aS; if (dur < minS || dur > maxS || aS < from || bS > to) continue;
                int A = ba * 4, B = (ba + bl) * 4; if (A < 8 || B > nBeats) continue;
                double s8 = Sim(A, B, 8), s2 = Sim(A, B, 2);
                // also the music right after the wrap: beats after a vs after b would continue past the end, so use beats just after a vs beats just after b where they exist (musical parallelism)
                double after = B + 4 <= nBeats ? Sim(A + 4, B + 4, 4) : s8;
                res.Add((0.5 * s8 + 0.3 * s2 + 0.2 * after, ba, ba + bl, s8, s2));
            }
        foreach (var r in res.OrderByDescending(x => x.score).Take(top))
            Console.WriteLine($"score={r.score:F3} a=bar{r.a} ({t0 + r.a * bar:F2}s) b=bar{r.b} ({t0 + r.b * bar:F2}s) dur={(r.b - r.a) * bar:F1}s chroma8={r.cs8:F3} chroma2={r.cs2:F3}");
        return 0;
    }

    static int Hits(string[] a)
    {
        string set = a[0]; var sig = Layered(set, a[1]); int top = (int)Program.Opt(a, "--top", 25); int rate = sig.Rate; var m = Dsp.Mono(sig);
        int hop = (int)(0.01 * rate); int n = m.N / hop; var db = new double[n];
        for (int i = 0; i < n; i++) { double s = 0; for (int k = 0; k < hop; k++) { double v = m.D[0][i * hop + k]; s += v * v; } db[i] = 10 * Math.Log10(s / hop + 1e-12); }
        var list = new List<(double rise, int i)>();
        for (int i = 12; i < n - 5; i++)
        {
            double prev = -200; for (int k = 3; k <= 12; k++) prev = Math.Max(prev, db[i - k]); double rise = db[i + 2] - prev;
            if (rise > Program.Opt(a, "--rise", 4) && db[i + 2] > -60 && db[i + 2] >= db[i + 1] && db[i + 2] >= db[i + 3]) list.Add((rise, i));
        }
        var chosen = new List<(double rise, int i)>();
        foreach (var h in list.OrderByDescending(x => x.rise)) { if (chosen.Any(c => Math.Abs(c.i - h.i) < 80)) continue; chosen.Add(h); if (chosen.Count >= top) break; }
        foreach (var h in chosen.OrderBy(x => x.i))
        {
            double lv = db[h.i + 2]; int j = h.i + 2; while (j < n - 1 && db[j] > lv - 12) j++;
            Console.WriteLine($"t={(h.i + 2) * 0.01,7:F2}s rise={h.rise,5:F1} dB level={lv,6:F1} dB decay12={(j - h.i - 2) * 0.01,5:F2}s");
        }
        return 0;
    }
}
