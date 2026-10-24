using System.Numerics;

namespace PixAudio;

public static class Dsp
{
    // ---------------------------------------------------------------- basics
    public static Clip Mono(Clip a)
    {
        if (a.Ch == 1) return a.Copy();
        var o = new Clip(a.Rate, 1, a.N);
        for (int i = 0; i < a.N; i++) { float s = 0; for (int c = 0; c < a.Ch; c++) s += a.D[c][i]; o.D[0][i] = s / a.Ch; }
        return o;
    }
    public static Clip Stereo(Clip a)
    {
        if (a.Ch == 2) return a.Copy();
        var o = new Clip(a.Rate, 2, a.N);
        Array.Copy(a.D[0], o.D[0], a.N); Array.Copy(a.D[a.Ch > 1 ? 1 : 0], o.D[1], a.N);
        return o;
    }
    public static float Peak(Clip a) { float p = 0; foreach (var ch in a.D) foreach (var v in ch) { float x = Math.Abs(v); if (x > p) p = x; } return p; }
    public static double Db(double lin) => 20.0 * Math.Log10(Math.Max(lin, 1e-9));
    public static double FromDb(double db) => Math.Pow(10.0, db / 20.0);
    public static void Gain(Clip a, double db) { float g = (float)FromDb(db); foreach (var ch in a.D) for (int i = 0; i < ch.Length; i++) ch[i] *= g; }

    public static Clip Slice(Clip a, double startSec, double endSec)
    {
        int s = Math.Clamp((int)Math.Round(startSec * a.Rate), 0, a.N), e = Math.Clamp((int)Math.Round(endSec * a.Rate), s, a.N);
        var o = new Clip(a.Rate, a.Ch, e - s);
        for (int c = 0; c < a.Ch; c++) Array.Copy(a.D[c], s, o.D[c], 0, e - s);
        return o;
    }
    public static void Fade(Clip a, double inSec, double outSec)
    {
        int fi = (int)(inSec * a.Rate), fo = (int)(outSec * a.Rate), n = a.N;
        for (int c = 0; c < a.Ch; c++)
        {
            for (int i = 0; i < fi && i < n; i++) a.D[c][i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / fi));
            for (int i = 0; i < fo && i < n; i++) a.D[c][n - 1 - i] *= (float)(0.5 - 0.5 * Math.Cos(Math.PI * i / fo));
        }
    }
    /// <summary>Trim leading/trailing samples under thresholdDb (peak), keeping a little padding.</summary>
    public static Clip TrimSilence(Clip a, double thresholdDb = -50, double padSec = 0.005, double tailPadSec = 0.05)
    {
        float th = (float)FromDb(thresholdDb); int s = 0, e = a.N - 1;
        bool Loud(int i) { for (int c = 0; c < a.Ch; c++) if (Math.Abs(a.D[c][i]) > th) return true; return false; }
        while (s < a.N && !Loud(s)) s++;
        while (e > s && !Loud(e)) e--;
        s = Math.Max(0, s - (int)(padSec * a.Rate)); e = Math.Min(a.N, e + 1 + (int)(tailPadSec * a.Rate));
        var o = new Clip(a.Rate, a.Ch, Math.Max(0, e - s));
        for (int c = 0; c < a.Ch; c++) Array.Copy(a.D[c], s, o.D[c], 0, o.N);
        return o;
    }
    public static Clip Concat(params Clip[] parts)
    {
        int n = parts.Sum(p => p.N); var o = new Clip(parts[0].Rate, parts[0].Ch, n); int at = 0;
        foreach (var p in parts) { for (int c = 0; c < o.Ch; c++) Array.Copy(p.D[Math.Min(c, p.Ch - 1)], 0, o.D[c], at, p.N); at += p.N; }
        return o;
    }
    /// <summary>Adds b into a starting at sample offset (the result grows if needed).</summary>
    public static Clip MixInto(Clip a, Clip b, int offset, double gainDb = 0)
    {
        float g = (float)FromDb(gainDb);
        int n = Math.Max(a.N, offset + b.N); var o = new Clip(a.Rate, a.Ch, n);
        for (int c = 0; c < a.Ch; c++) { Array.Copy(a.D[c], o.D[c], a.N); for (int i = 0; i < b.N; i++) o.D[c][offset + i] += b.D[Math.Min(c, b.Ch - 1)][i] * g; }
        return o;
    }

    // ---------------------------------------------------------------- resampling (windowed sinc)
    public static Clip Resample(Clip a, int newRate)
    {
        if (a.Rate == newRate) return a.Copy();
        double ratio = (double)newRate / a.Rate;
        int outN = (int)Math.Floor(a.N * ratio);
        var o = new Clip(newRate, a.Ch, outN);
        double scale = Math.Min(1.0, ratio) * 0.97; int half = 16;
        int w = (int)Math.Ceiling(half / scale);
        for (int c = 0; c < a.Ch; c++)
        {
            var src = a.D[c]; var dst = o.D[c];
            Parallel.For(0, outN, i =>
            {
                double t = i / ratio; int ti = (int)Math.Floor(t); double acc = 0;
                for (int k = ti - w + 1; k <= ti + w; k++)
                {
                    if (k < 0 || k >= src.Length) continue;
                    double d = t - k, x = d * scale;
                    double sinc = Math.Abs(x) < 1e-9 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
                    double u = d / w; if (Math.Abs(u) >= 1) continue;
                    double win = 0.42 + 0.5 * Math.Cos(Math.PI * u) + 0.08 * Math.Cos(2 * Math.PI * u);
                    acc += src[k] * sinc * win * scale;
                }
                dst[i] = (float)acc;
            });
        }
        return o;
    }

    // ---------------------------------------------------------------- loudness (ITU-R BS.1770-4, K-weighted, gated)
    sealed class Biquad
    {
        double b0, b1, b2, a1, a2, z1, z2;
        public Biquad(double b0, double b1, double b2, double a1, double a2) { this.b0 = b0; this.b1 = b1; this.b2 = b2; this.a1 = a1; this.a2 = a2; }
        public double Step(double x) { double y = b0 * x + z1; z1 = b1 * x - a1 * y + z2; z2 = b2 * x - a2 * y; return y; }
    }
    static Biquad[] KWeight(int fs)
    {
        double f0 = 1681.974450955533, G = 3.999843853973347, Q = 0.7071752369554196;
        double K = Math.Tan(Math.PI * f0 / fs), Vh = Math.Pow(10, G / 20), Vb = Math.Pow(Vh, 0.4996667741545416);
        double a0 = 1 + K / Q + K * K;
        var s1 = new Biquad((Vh + Vb * K / Q + K * K) / a0, 2 * (K * K - Vh) / a0, (Vh - Vb * K / Q + K * K) / a0, 2 * (K * K - 1) / a0, (1 - K / Q + K * K) / a0);
        f0 = 38.13547087602444; Q = 0.5003270373238773; K = Math.Tan(Math.PI * f0 / fs);
        double d = 1 + K / Q + K * K;
        var s2 = new Biquad(1, -2, 1, 2 * (K * K - 1) / d, (1 - K / Q + K * K) / d);
        return new[] { s1, s2 };
    }
    /// <summary>400 ms block mean-square energies at a 100 ms hop (summed over channels).</summary>
    public static double[] BlockEnergies(Clip a)
    {
        int blk = (int)(0.4 * a.Rate), hop = (int)(0.1 * a.Rate);
        var ks = new float[a.Ch][];
        for (int c = 0; c < a.Ch; c++)
        {
            var f = KWeight(a.Rate); var o = new float[a.N];
            for (int i = 0; i < a.N; i++) o[i] = (float)f[1].Step(f[0].Step(a.D[c][i]));
            ks[c] = o;
        }
        int count = a.N >= blk ? (a.N - blk) / hop + 1 : 0;
        var e = new double[count];
        for (int b = 0; b < count; b++)
        {
            double sum = 0;
            for (int c = 0; c < a.Ch; c++) { double s = 0; int st = b * hop; for (int i = 0; i < blk; i++) { double v = ks[c][st + i]; s += v * v; } sum += s / blk; }
            e[b] = sum;
        }
        return e;
    }
    public static double LufsOf(double ms) => -0.691 + 10 * Math.Log10(Math.Max(ms, 1e-12));
    public static double Lufs(Clip a)
    {
        int minN = (int)(0.4 * a.Rate);
        if (a.N < minN) { var p = new Clip(a.Rate, a.Ch, minN); for (int c = 0; c < a.Ch; c++) Array.Copy(a.D[c], p.D[c], a.N); a = p; return LufsOf(BlockEnergies(a).Average()); }
        var e = BlockEnergies(a);
        var g1 = e.Where(x => LufsOf(x) > -70).ToArray();
        if (g1.Length == 0) return -70;
        double rel = LufsOf(g1.Average()) - 10;
        var g2 = g1.Where(x => LufsOf(x) > rel).ToArray();
        return g2.Length == 0 ? LufsOf(g1.Average()) : LufsOf(g2.Average());
    }
    /// <summary>Loudest 3-second window (short-term LUFS max).</summary>
    public static double ShortTermMax(Clip a)
    {
        int win = Math.Min(a.N, 3 * a.Rate); double best = -120;
        for (int st = 0; ; st += a.Rate / 2)
        {
            int len = Math.Min(win, a.N - st); var tmp = new Clip(a.Rate, a.Ch, len);
            for (int c = 0; c < a.Ch; c++) Array.Copy(a.D[c], st, tmp.D[c], 0, len);
            best = Math.Max(best, Lufs(tmp));
            if (st + win >= a.N) break;
        }
        return best;
    }
    /// <summary>Applies the gain that puts integrated LUFS on target, then lowers it if the sample peak would pass ceilingDb. Returns the gain in dB.</summary>
    public static double NormalizeLufs(Clip a, double targetLufs, double ceilingDb = -1.0)
    {
        double cur = Lufs(a), g = targetLufs - cur; double pk = Db(Peak(a)) + g;
        if (pk > ceilingDb) g -= pk - ceilingDb;
        Gain(a, g); return g;
    }
    public static double NormalizePeak(Clip a, double peakDb) { double g = peakDb - Db(Peak(a)); Gain(a, g); return g; }

    // ---------------------------------------------------------------- FFT + spectral features
    public static void Fft(Complex[] x)
    {
        int n = x.Length;
        for (int i = 1, j = 0; i < n; i++) { int bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit; if (i < j) (x[i], x[j]) = (x[j], x[i]); }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len; var wl = new Complex(Math.Cos(ang), Math.Sin(ang));
            for (int i = 0; i < n; i += len) { Complex w = 1; for (int k = 0; k < len / 2; k++) { var u = x[i + k]; var v = x[i + k + len / 2] * w; x[i + k] = u + v; x[i + k + len / 2] = u - v; w *= wl; } }
        }
    }
    public sealed class Features
    {
        public double Dur, Peak, Rms, Lufs, Short, Centroid, Rolloff85, Flatness;
        public double BandSub, BandLow, BandMid, BandHigh, BandAir; // fraction of energy
        /// <summary>0..1: share of the envelope modulation spectrum (0.4-20 Hz) that sits at syllable rate (2.5-7 Hz) in the 250-3400 Hz band. Sung / spoken vocals score high.</summary>
        public double Syllabic;
        /// <summary>0..1 mean tonal-peak ratio in 100-1000 Hz.</summary>
        public double Harm;
        public double OnsetRate, Bpm, BpmConf;
        public override string ToString() => $"dur={Dur:F1}s peak={Db(Peak):F1}dB lufs={Lufs:F1} st={Short:F1} cen={Centroid:F0}Hz r85={Rolloff85:F0} flat={Flatness:F2} bands(sub/low/mid/hi/air)={BandSub:P0}/{BandLow:P0}/{BandMid:P0}/{BandHigh:P0}/{BandAir:P0} syl={Syllabic:F2} harm={Harm:F2} bpm={Bpm:F1}({BpmConf:F2}) onsets/s={OnsetRate:F2}";
    }

    public static Features Analyze(Clip a, bool tempo = true)
    {
        var f = new Features { Dur = a.Dur, Peak = Peak(a), Lufs = Lufs(a) };
        var m = Mono(a); int fs = m.Rate; double sum2 = 0; foreach (var v in m.D[0]) sum2 += v * v; f.Rms = Math.Sqrt(sum2 / Math.Max(1, m.N));
        f.Short = a.Dur > 3 ? ShortTermMax(a) : f.Lufs;
        const int N = 2048, H = 1024; var win = new double[N]; for (int i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N);
        int frames = Math.Max(0, (m.N - N) / H + 1); var avg = new double[N / 2];
        var specEnvSpeech = new double[frames]; var flux = new double[frames]; double[] prev = null;
        double harmAcc = 0; int harmN = 0; double hz = (double)fs / N;
        var buf = new Complex[N];
        int step = Math.Max(1, frames / 800);
        for (int fr = 0; fr < frames; fr++)
        {
            for (int i = 0; i < N; i++) buf[i] = m.D[0][fr * H + i] * win[i];
            Fft(buf);
            var mag = new double[N / 2]; double speech = 0;
            for (int k = 0; k < N / 2; k++) { mag[k] = buf[k].Magnitude; double fq = k * hz; if (fq >= 250 && fq <= 3400) speech += mag[k] * mag[k]; }
            specEnvSpeech[fr] = speech;
            if (fr % step == 0) for (int k = 0; k < N / 2; k++) avg[k] += mag[k] * mag[k];
            if (prev != null) { double fl = 0; for (int k = 0; k < N / 2; k++) { double d = mag[k] - prev[k]; if (d > 0) fl += d; } flux[fr] = fl; }
            prev = mag;
            if (fr % step == 0)
            {
                int k0 = (int)(100 / hz), k1 = (int)(1000 / hz); double mx = 0, mean = 0; for (int k = k0; k <= k1; k++) { mean += mag[k]; mx = Math.Max(mx, mag[k]); }
                mean /= (k1 - k0 + 1); if (mean > 1e-9) { harmAcc += Math.Min(1.0, (mx / mean) / 25.0); harmN++; }
            }
        }
        f.Harm = harmN > 0 ? harmAcc / harmN : 0;
        double etot = avg.Sum() + 1e-18, cen = 0, acc = 0, lg = 0; int roll = 0; bool rolled = false;
        double sub = 0, low = 0, mid = 0, hi = 0, air = 0;
        for (int k = 1; k < avg.Length; k++)
        {
            double fq = k * hz; cen += fq * avg[k]; acc += avg[k]; if (!rolled && acc >= 0.85 * etot) { roll = k; rolled = true; }
            lg += Math.Log(avg[k] + 1e-18);
            if (fq < 100) sub += avg[k]; else if (fq < 300) low += avg[k]; else if (fq < 3400) mid += avg[k]; else if (fq < 8000) hi += avg[k]; else air += avg[k];
        }
        f.Centroid = cen / etot; f.Rolloff85 = roll * hz; f.Flatness = Math.Exp(lg / (avg.Length - 1)) / (etot / (avg.Length - 1) + 1e-18);
        f.BandSub = sub / etot; f.BandLow = low / etot; f.BandMid = mid / etot; f.BandHigh = hi / etot; f.BandAir = air / etot;
        double frRate = (double)fs / H;
        f.Syllabic = ModShare(specEnvSpeech, frRate, 2.5, 7.0, 0.4, 20.0);
        if (frames > 4)
        {
            double thr = flux.Average() * 1.6; int on = 0; for (int i = 1; i < frames - 1; i++) if (flux[i] > thr && flux[i] >= flux[i - 1] && flux[i] > flux[i + 1]) on++;
            f.OnsetRate = on / Math.Max(1.0, a.Dur);
            if (tempo) { var t = Tempo(flux, frRate); f.Bpm = t.bpm; f.BpmConf = t.conf; }
        }
        return f;
    }

    static double ModShare(double[] env, double rate, double lo, double hi, double totLo, double totHi)
    {
        int n = env.Length; if (n < 64) return 0;
        int P = 1; while (P < n) P <<= 1;
        var x = new Complex[P]; double mean = 0; var l = new double[n]; for (int i = 0; i < n; i++) { l[i] = Math.Log(env[i] + 1e-12); mean += l[i]; } mean /= n;
        for (int i = 0; i < n; i++) x[i] = (l[i] - mean) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n));
        Fft(x); double a = 0, t = 0; double df = rate / P;
        for (int k = 1; k < P / 2; k++) { double fq = k * df, e = x[k].Magnitude; e *= e; if (fq >= totLo && fq <= totHi) t += e; if (fq >= lo && fq <= hi) a += e; }
        return t > 0 ? a / t : 0;
    }

    public static (double bpm, double conf) Tempo(double[] flux, double rate)
    {
        int n = flux.Length; if (n < 200) return (0, 0);
        double mean = flux.Average(); var x = flux.Select(v => v - mean).ToArray();
        int lagMin = Math.Max(1, (int)(rate * 60 / 200)), lagMax = (int)(rate * 60 / 60);
        var ac = new double[lagMax * 2 + 4];
        double zero = 0; for (int i = 0; i < n; i++) zero += x[i] * x[i];
        for (int lag = lagMin / 2; lag <= lagMax * 2 && lag < n / 2; lag++)
        {
            double s = 0; for (int i = 0; i + lag < n; i++) s += x[i] * x[i + lag]; ac[lag] = s / (n - lag);
        }
        double best = -1, bestLag = lagMin;
        for (int lag = lagMin; lag <= lagMax && lag < n / 2; lag++)
        {
            double s = ac[lag] + 0.5 * ac[lag * 2] + (lag % 2 == 0 ? 0.5 * ac[lag / 2] : 0);
            if (s > best) { best = s; bestLag = lag; }
        }
        double bpm = 60.0 * rate / bestLag;
        while (bpm < 70) bpm *= 2;
        while (bpm > 180) bpm /= 2;
        return (bpm, best / (zero / n + 1e-12));
    }

    /// <summary>Onset (spectral flux) strength, one value per hop of `hop` samples; for beat grids.</summary>
    public static double[] Flux(Clip a, int hop = 512)
    {
        var m = Mono(a); const int N = 1024; int frames = Math.Max(0, (m.N - N) / hop + 1); var o = new double[frames]; double[] prev = null; var buf = new Complex[N];
        for (int fr = 0; fr < frames; fr++)
        {
            for (int i = 0; i < N; i++) buf[i] = m.D[0][fr * hop + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N));
            Fft(buf); var mag = new double[N / 2]; for (int k = 0; k < N / 2; k++) mag[k] = Math.Log(1 + 100 * buf[k].Magnitude);
            if (prev != null) { double fl = 0; for (int k = 0; k < N / 2; k++) { double d = mag[k] - prev[k]; if (d > 0) fl += d; } o[fr] = fl; }
            prev = mag;
        }
        return o;
    }

    /// <summary>Short-time RMS in dB, one value per hopSec.</summary>
    public static double[] RmsDb(Clip a, double hopSec = 0.05)
    {
        int h = Math.Max(1, (int)(hopSec * a.Rate)); int n = a.N / h; var o = new double[n];
        for (int i = 0; i < n; i++) { double s = 0; for (int c = 0; c < a.Ch; c++) for (int k = 0; k < h; k++) { double v = a.D[c][i * h + k]; s += v * v; } o[i] = 10 * Math.Log10(s / (h * a.Ch) + 1e-12); }
        return o;
    }

    // ---------------------------------------------------------------- loops
    /// <summary>Seamless loop of [a,b) seconds: the last `xfade` seconds are blended with the audio that precedes `a`, so wrapping b to a is continuous. Needs a >= xfade.</summary>
    public static Clip MakeLoop(Clip src, double aSec, double bSec, double xfadeSec)
    {
        int a = (int)Math.Round(aSec * src.Rate), b = (int)Math.Round(bSec * src.Rate), x = (int)Math.Round(xfadeSec * src.Rate);
        if (a < x) throw new Exception("loop start must be >= crossfade length");
        int L = b - a; var o = new Clip(src.Rate, src.Ch, L);
        for (int c = 0; c < src.Ch; c++)
        {
            Array.Copy(src.D[c], a, o.D[c], 0, L);
            for (int i = 0; i < x; i++)
            {
                double w = (i + 1.0) / (x + 1.0);
                double fin = Math.Sin(w * Math.PI / 2), fout = Math.Cos(w * Math.PI / 2);
                o.D[c][L - x + i] = (float)(src.D[c][b - x + i] * fout + src.D[c][a - x + i] * fin);
            }
        }
        return o;
    }
    /// <summary>Best loop end near bSec (+-searchSec): maximises the normalised correlation of the 80 ms windows at the two points.</summary>
    public static double BestLoopEnd(Clip src, double aSec, double bSec, double searchSec)
    {
        var m = Mono(src); int a = (int)(aSec * m.Rate), b0 = (int)(bSec * m.Rate), w = (int)(0.08 * m.Rate), s = (int)(searchSec * m.Rate);
        double best = -2; int bestB = b0;
        for (int b = b0 - s; b <= b0 + s; b += 8)
        {
            if (b + w >= m.N || a + w >= m.N) continue;
            double xy = 0, xx = 1e-9, yy = 1e-9;
            for (int i = 0; i < w; i++) { double p = m.D[0][a + i], q = m.D[0][b + i]; xy += p * q; xx += p * p; yy += q * q; }
            double r = xy / Math.Sqrt(xx * yy); if (r > best) { best = r; bestB = b; }
        }
        return bestB / (double)m.Rate;
    }
}
