using System.Globalization;
using System.Numerics;
using System.Text;

namespace PixAudio;

/// <summary>Shared helpers for the music-production verbs (source registry, filters, reverb, varispeed, bar grid).</summary>
public static class MusicLib
{
    public const string Root = @"C:\Users\peyto\Desktop\PeytonParrishGames\PIX PRO FOOTBALL\_AssetSource\DraftFever\Sound\";
    public const double BowlStemLead = 65334 / 44100.0;
    public static readonly Dictionary<string, string> Sources = BuildSources();
    static Dictionary<string, string> BuildSources()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string b = Root + @"Draft Fever Bowl OST\", h = Root + @"Draft House OST\";
        d["bowl.mix"] = b + "Draft Fever Bowl 8-24-24.wav";
        foreach (var s in new[] { "brass", "PCH", "Perc", "strings", "WW" }) d["bowl." + s] = b + @"Stems\" + s + "_03.wav";
        d["house.mix"] = h + "Draft House 8-22-24.wav";
        foreach (var s in new[] { "PCH", "Perc", "WW", "brass", "strings" }) d["house." + s] = h + @"Stems\" + s + "_04.wav";
        d["house.fx"] = h + @"Stems\fx.cm_01.wav"; d["house.guitar"] = h + @"Stems\guitar.cm_02.wav"; d["house.synth"] = h + @"Stems\synth.cm_01.wav";
        return d;
    }
    public static string[] StemNames(string set) => Sources.Keys.Where(k => k.StartsWith(set + ".", StringComparison.OrdinalIgnoreCase) && !k.EndsWith(".mix", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x).ToArray();

    static readonly Dictionary<string, Clip> cache = new(StringComparer.OrdinalIgnoreCase);
    public static Clip Src(string name)
    {
        if (cache.TryGetValue(name, out var c)) return c;
        string path = Sources.TryGetValue(name, out var p) ? p : name;
        if (!File.Exists(path)) throw new Exception("unknown source " + name);
        c = Wav.Read(path); if (c.Rate != 44100) c = Dsp.Resample(c, 44100); c = Dsp.Stereo(c);
        if (name.StartsWith("bowl.", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".mix", StringComparison.OrdinalIgnoreCase)) c = Dsp.Slice(c, BowlStemLead, c.Dur); // bowl stems carry 1.4815 s of lead-in the mix lacks (measured by mlag)
        cache[name] = c; return c;
    }

    // ---------------------------------------------------------------- RBJ biquads (applied in cascade for steeper slopes)
    public static void Filter(Clip a, bool highpass, double fc, int order2 = 2, double q = 0.7071)
    {
        double w0 = 2 * Math.PI * fc / a.Rate, cs = Math.Cos(w0), al = Math.Sin(w0) / (2 * q);
        double b0, b1, b2, a0 = 1 + al, a1 = -2 * cs, a2 = 1 - al;
        if (highpass) { b0 = (1 + cs) / 2; b1 = -(1 + cs); b2 = (1 + cs) / 2; } else { b0 = (1 - cs) / 2; b1 = 1 - cs; b2 = (1 - cs) / 2; }
        b0 /= a0; b1 /= a0; b2 /= a0; a1 /= a0; a2 /= a0;
        for (int ch = 0; ch < a.Ch; ch++)
            for (int pass = 0; pass < order2; pass++)
            {
                double z1 = 0, z2 = 0; var d = a.D[ch];
                for (int i = 0; i < d.Length; i++) { double x = d[i], y = b0 * x + z1; z1 = b1 * x - a1 * y + z2; z2 = b2 * x - a2 * y; d[i] = (float)y; }
            }
    }

    // ---------------------------------------------------------------- varispeed (pitch + tempo together): factor 0.5 = octave down, twice as long
    public static Clip Varispeed(Clip a, double factor)
    {
        int tmp = (int)Math.Round(a.Rate / factor);
        var r = Dsp.Resample(a, tmp); r.Rate = a.Rate; return r;
    }

    // ---------------------------------------------------------------- simple Freeverb-style stereo reverb; returns wet only
    public static Clip Reverb(Clip a, double room = 0.84, double damp = 0.3, double tailSec = 0)
    {
        int extra = (int)(tailSec * a.Rate); var inp = Dsp.Mono(a);
        int n = a.N + extra; var o = new Clip(a.Rate, 2, n);
        int[] ct = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 }; int[] at = { 556, 441, 341, 225 };
        double sc = a.Rate / 44100.0;
        for (int side = 0; side < 2; side++)
        {
            int spread = side * 23;
            var outBuf = o.D[side];
            var combs = ct.Select(t => new float[(int)((t + spread) * sc)]).ToArray(); var cpos = new int[8]; var cstore = new float[8];
            var aps = at.Select(t => new float[(int)((t + spread) * sc)]).ToArray(); var apos = new int[4];
            for (int i = 0; i < n; i++)
            {
                float x = i < inp.N ? inp.D[0][i] * 0.015f : 0f; float sum = 0;
                for (int k = 0; k < 8; k++)
                {
                    var buf = combs[k]; float y = buf[cpos[k]];
                    cstore[k] = (float)(y * (1 - damp) + cstore[k] * damp);
                    buf[cpos[k]] = x + cstore[k] * (float)room; if (++cpos[k] >= buf.Length) cpos[k] = 0; sum += y;
                }
                for (int k = 0; k < 4; k++)
                {
                    var buf = aps[k]; float bo = buf[apos[k]]; float y = -sum + bo; buf[apos[k]] = sum + bo * 0.5f; if (++apos[k] >= buf.Length) apos[k] = 0; sum = y;
                }
                outBuf[i] = sum * 3f;
            }
        }
        return o;
    }

    // ---------------------------------------------------------------- misc
    public static double[] Bandpass2(Clip a) => null;
    public static float[] MonoArr(Clip a) => Dsp.Mono(a).D[0];

    /// <summary>Stable zero crossing near sample index i (searching +-range samples), rising direction on the first channel.</summary>
    public static int NearestZero(Clip a, int i, int range = 400)
    {
        var d = a.D[0]; int best = Math.Clamp(i, 0, a.N - 1); double bv = double.MaxValue;
        for (int k = Math.Max(0, i - range); k < Math.Min(a.N - 1, i + range); k++) { double v = Math.Abs(d[k]) + 0.001 * Math.Abs(k - i) / range; if (v < bv) { bv = v; best = k; } }
        return best;
    }

    /// <summary>Beat grid estimate from onset flux of the given clips. Returns (bpm, firstBeatSec, score, secondBestScoreRatio).</summary>
    public static (double bpm, double t0, double score) Grid(Clip[] clips, double bpmLo, double bpmHi, double step, int hop = 256)
    {
        var flux = new double[0];
        foreach (var c in clips)
        {
            var f = Dsp.Flux(c, hop); if (flux.Length == 0) flux = new double[f.Length];
            double mean = f.Average(); for (int i = 0; i < Math.Min(f.Length, flux.Length); i++) flux[i] += Math.Max(0, f[i] - mean * 1.0);
        }
        double fr = clips[0].Rate / (double)hop; int n = flux.Length;
        double best = -1, bb = 0, bt = 0;
        for (double bpm = bpmLo; bpm <= bpmHi; bpm += step)
        {
            double per = 60.0 / bpm * fr; // frames per beat
            int phases = (int)Math.Ceiling(per);
            for (int ph = 0; ph < phases; ph++)
            {
                double s = 0; int cnt = 0;
                for (double t = ph; t < n - 1; t += per) { int i = (int)t; double fx = t - i; s += flux[i] * (1 - fx) + flux[i + 1] * fx; cnt++; }
                s /= cnt; if (s > best) { best = s; bb = bpm; bt = ph / fr; }
            }
        }
        return (bb, bt, best);
    }

    public static string F(double v, int d = 1) => v.ToString("F" + d, CultureInfo.InvariantCulture);
}

public static class MusicLag
{
    /// <summary>Lag L (samples) such that y[t+L] best matches x[t]; searched on a 4x-decimated mono copy then refined at full rate.</summary>
    public static double LastCorr; public static int Find(Clip x, Clip y, double maxSec)
    {
        const int D = 4;
        float[] dx = Dec(Dsp.Mono(x).D[0], D), dy = Dec(Dsp.Mono(y).D[0], D);
        int n = Math.Max(dx.Length, dy.Length), P = 1; while (P < 2 * n) P <<= 1;
        var A = new System.Numerics.Complex[P]; var B = new System.Numerics.Complex[P];
        for (int i = 0; i < dx.Length; i++) A[i] = dx[i]; for (int i = 0; i < dy.Length; i++) B[i] = dy[i];
        Dsp.Fft(A); Dsp.Fft(B);
        for (int i = 0; i < P; i++) A[i] = System.Numerics.Complex.Conjugate(A[i]) * B[i] ;
        // inverse fft via conj trick
        for (int i = 0; i < P; i++) A[i] = System.Numerics.Complex.Conjugate(A[i]);
        Dsp.Fft(A);
        int maxL = (int)(maxSec * x.Rate / D); double best = double.MinValue; int bl = 0;
        for (int l = -maxL; l <= maxL; l++) { double v = A[(l + P) % P].Real; if (v > best) { best = v; bl = l; } }
        // refine at full rate around bl*D with direct dot products on a 20 s excerpt
        var mx = Dsp.Mono(x).D[0]; var my = Dsp.Mono(y).D[0]; int c0 = Math.Min(mx.Length / 3, 44100 * 30), len = Math.Min(44100 * 20, mx.Length / 3);
        double bv = double.MinValue; int bL = bl * D;
        for (int l = bl * D - 6; l <= bl * D + 6; l++)
        {
            double s = 0; for (int i = 0; i < len; i++) { int j = c0 + i + l; if (j < 0 || j >= my.Length) continue; s += (double)mx[c0 + i] * my[j]; }
            if (s > bv) { bv = s; bL = l; }
        }
        { double ex = 0, ey = 0; for (int i = 0; i < len; i++) { int j = c0 + i + bL; if (j < 0 || j >= my.Length) continue; ex += (double)mx[c0 + i] * mx[c0 + i]; ey += (double)my[j] * my[j]; } LastCorr = bv / Math.Sqrt(ex * ey + 1e-30);
        }
        return bL;
    }
    static float[] Dec(float[] a, int d) { var o = new float[a.Length / d]; for (int i = 0; i < o.Length; i++) { float s = 0; for (int k = 0; k < d; k++) s += a[i * d + k]; o[i] = s / d; } return o; }

    /// <summary>Clip y shifted so that out[t] = y[t+lag] (zero padded), length n.</summary>
    public static Clip Shift(Clip y, int lag, int n)
    {
        var o = new Clip(y.Rate, y.Ch, n);
        for (int c = 0; c < y.Ch; c++) for (int t = 0; t < n; t++) { int j = t + lag; o.D[c][t] = (j >= 0 && j < y.N) ? y.D[c][j] : 0f; }
        return o;
    }
}
