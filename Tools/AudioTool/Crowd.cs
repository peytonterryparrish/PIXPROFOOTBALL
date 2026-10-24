using System.Globalization;
using System.Numerics;
using System.Text;

namespace PixAudio;

/// <summary>
/// Crowd audio verbs (v0.11.0 AUDIO+CROWD crowd track).
///   cscan  dir|file... [--out f.csv]       whole-file features (+ chant screen: syllabic, harm, envper = envelope periodicity 0.25-2 s) for every wav
///   cwin   file [win=10] [hop=5]           the same measures per window, to pick ranges inside long recordings
///   cenv   file [hop=0.5] [a b]            short-term RMS timeline (dBFS) as text (finds swells / peaks / silences)
///   cprobe file a b                        feature line of a [a,b) range
///   crender spec.txt [--only group]        render every entry of a spec (loops + one-shots) to the Crowd folder, write the manifest csv
///   ccheck dir [--spec spec.txt]           verification: format (44.1k stereo 16-bit), LUFS vs target, peak, loop seams
/// Spec lines (key=value, # comments):
///   out folder / manifest file / src key path pack
///   loop group nn lufs=-26 src=key a=10 b=40 xf=2.5 [lp= hp= gain= pitch= note= derived=1 add=key:a:b:gainDb:offset ...]
///   shot group nn lufs=-20 src=key a=1 b=6 fi=0.02 fo=0.8 [lp= hp= gain= pitch= ramp=dB rampt=sec note= derived=1 add=...]
/// </summary>
public static class CrowdVerbs
{
    public static IDictionary<string, Func<string[], int>> Register() => new Dictionary<string, Func<string[], int>>
    {
        ["cscan"] = Scan, ["cselftest"] = SelfTest, ["cwin"] = Win, ["cenv"] = Env, ["cprobe"] = Probe, ["crender"] = Render, ["ccheck"] = Check,
    };

    static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ envelope-based screens
    /// <summary>Log speech-band (250-3400 Hz) frame energy at 100 frames/s from a mono clip.</summary>
    public static double[] SpeechEnv(Clip mono, out double frameRate)
    {
        int fs = mono.Rate, hop = Math.Max(1, fs / 100); frameRate = (double)fs / hop;
        const int N = 1024; var win = new double[N]; for (int i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N);
        int frames = Math.Max(0, (mono.N - N) / hop + 1); var env = new double[frames]; var buf = new Complex[N];
        int k0 = (int)(250.0 * N / fs), k1 = (int)(3400.0 * N / fs);
        for (int f = 0; f < frames; f++)
        {
            for (int i = 0; i < N; i++) buf[i] = mono.D[0][f * hop + i] * win[i];
            Dsp.Fft(buf); double e = 0; for (int k = k0; k <= k1; k++) e += buf[k].Real * buf[k].Real + buf[k].Imaginary * buf[k].Imaginary;
            env[f] = Math.Log(e + 1e-12);
        }
        return env;
    }
    /// <summary>Peak normalised autocorrelation (0.25-2.0 s lag) of the slow-detrended speech-band envelope: rhythmic chanting / clapping / drums score high, diffuse roar low.</summary>
    public static double EnvPeriod(double[] env, double rate, int from, int to)
    {
        to = Math.Min(to, env.Length); int n = to - from; if (n < (int)(rate * 4)) return 0;
        var x = new double[n]; for (int i = 0; i < n; i++) x[i] = env[from + i];
        int w = (int)(1.5 * rate); var y = new double[n];
        var cs = new double[n + 1]; for (int i = 0; i < n; i++) cs[i + 1] = cs[i] + x[i];
        for (int i = 0; i < n; i++) { int lo = Math.Max(0, i - w / 2), hi = Math.Min(n, i + w / 2 + 1); y[i] = x[i] - (cs[hi] - cs[lo]) / (hi - lo); }
        double zero = 0; foreach (var v in y) zero += v * v; if (zero < 1e-9) return 0;
        int l0 = (int)(0.25 * rate), l1 = Math.Min((int)(2.0 * rate), n / 2); double best = 0;
        for (int lag = l0; lag <= l1; lag++)
        {
            double s = 0; for (int i = 0; i + lag < n; i++) s += y[i] * y[i + lag];
            s = s / (n - lag) / (zero / n); if (s > best) best = s;
        }
        return best;
    }

    // ------------------------------------------------------------------ cscan
    static int Scan(string[] a)
    {
        var p = Program.Positional(a); string outf = null; int oi = Array.IndexOf(a, "--out"); if (oi >= 0) outf = a[oi + 1];
        p = p.Where(x => x != outf).ToArray();
        var files = new List<string>();
        foreach (var x in p) { if (Directory.Exists(x)) files.AddRange(Directory.EnumerateFiles(x, "*.wav", SearchOption.AllDirectories).OrderBy(z => z)); else files.Add(x); }
        var sb = new StringBuilder("file,rate,ch,dur,peakdb,lufs,stmax,centroid,r85,flat,sub,low,mid,high,air,syl,harm,envper,vclar,vfrac,onsets,rmsvar\n");
        var rows = new string[files.Count];
        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            var f = files[i];
            try
            {
                var c = Wav.Read(f); var t = Dsp.Analyze(c, false); var m = Dsp.Mono(c);
                var env = SpeechEnv(m, out double fr); double ep = EnvPeriod(env, fr, 0, env.Length); var vo = Voiced(m, 0, Math.Min(c.Dur, 60));
                var r = Dsp.RmsDb(m, 1.0); double mu = r.Length > 0 ? r.Average() : 0, sd = r.Length > 1 ? Math.Sqrt(r.Select(v => (v - mu) * (v - mu)).Average()) : 0;
                rows[i] = $"\"{Path.GetFileName(f)}\",{c.Rate},{c.Ch},{t.Dur:F1},{Dsp.Db(t.Peak):F1},{t.Lufs:F1},{t.Short:F1},{t.Centroid:F0},{t.Rolloff85:F0},{t.Flatness:F3},{t.BandSub:F2},{t.BandLow:F2},{t.BandMid:F2},{t.BandHigh:F2},{t.BandAir:F2},{t.Syllabic:F3},{t.Harm:F3},{ep:F3},{vo.mean:F3},{vo.frac:F3},{t.OnsetRate:F1},{sd:F1}";
            }
            catch (Exception e) { rows[i] = $"\"{Path.GetFileName(f)}\",ERROR {e.Message.Replace(',', ';')}"; }
            Console.Error.WriteLine("scanned " + Path.GetFileName(f));
        });
        foreach (var r in rows) sb.AppendLine(r);
        if (outf != null) File.WriteAllText(outf, sb.ToString()); else Console.Write(sb.ToString());
        return 0;
    }

    // ------------------------------------------------------------------ voicedness (pitched / sung / chanted content)
    /// <summary>Pitch-clarity screen on a spectrally whitened frame (so low-passed rumble does not read as periodic): decimates to ~11 kHz, 93 ms frames (50% overlap), magnitude spectrum divided by its smoothed envelope over 150-3000 Hz,
    /// squared, mean removed and inverse-FFT'd (= whitened autocorrelation), peak at lags of 80-500 Hz. Returns (mean clarity, fraction of frames above 0.25).
    /// Diffuse roar / murmur sits near the noise floor (~0.05-0.12), unison singing / chanting and sustained vowels rise. Relative screen only.</summary>
    public static (double mean, double frac) Voiced(Clip mono, double t0, double t1)
    {
        int dec = Math.Max(1, mono.Rate / 11025), fs = mono.Rate / dec; int s0 = (int)(t0 * mono.Rate), s1 = Math.Min(mono.N, (int)(t1 * mono.Rate));
        int n = (s1 - s0) / dec; const int N = 1024; if (n < N) return (0, 0);
        var x = new float[n]; for (int i = 0; i < n; i++) { double s = 0; for (int k = 0; k < dec; k++) s += mono.D[0][s0 + i * dec + k]; x[i] = (float)(s / dec); }
        int kLo = Math.Max(1, (int)(150.0 * N / fs)), kHi = Math.Min(N / 2 - 1, (int)(3000.0 * N / fs)), lmin = fs / 500, lmax = fs / 80;
        var win = new double[N]; for (int i = 0; i < N; i++) win[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N);
        var buf = new Complex[N]; var mag = new double[N / 2 + 1]; double acc = 0; int cnt = 0, voiced = 0; const int S = 8;
        for (int st = 0; st + N <= n; st += N / 2)
        {
            double e0 = 0; for (int i = 0; i < N; i++) { buf[i] = x[st + i] * win[i]; e0 += buf[i].Real * buf[i].Real; }
            if (e0 < 1e-9) continue;
            Dsp.Fft(buf); for (int k = 0; k <= N / 2; k++) mag[k] = buf[k].Magnitude + 1e-9;
            var w = new double[N / 2 + 1]; double mean = 0; int nb = 0;
            for (int k = kLo; k <= kHi; k++)
            {
                double sm = 0; int c = 0; for (int j = Math.Max(kLo, k - S); j <= Math.Min(kHi, k + S); j++) { sm += mag[j]; c++; }
                double r = mag[k] / (sm / c); w[k] = r * r; mean += w[k]; nb++;
            }
            mean /= nb; for (int k = 0; k < N; k++) buf[k] = 0;
            for (int k = kLo; k <= kHi; k++) { double v = w[k] - mean; buf[k] = v; buf[N - k] = v; }
            Dsp.Fft(buf);   // real symmetric input -> real output ~ autocorrelation (up to scale)
            var absC = new List<double>(); for (int lag = 8; lag < 400 && lag < N / 2; lag++) absC.Add(Math.Abs(buf[lag].Real)); absC.Sort(); double sd = absC[absC.Count / 2] / 0.6745 + 1e-12;
            double best = 0; for (int lag = lmin; lag <= lmax; lag++) best = Math.Max(best, buf[lag].Real / sd);
            acc += best; cnt++; if (best > 5.0) voiced++;
        }
        return cnt == 0 ? (0, 0) : (acc / cnt, voiced / (double)cnt);
    }

    // ------------------------------------------------------------------ cwin
    static int Win(string[] a)
    {
        string outf = null; int oi = Array.IndexOf(a, "--out"); if (oi >= 0) outf = a[oi + 1];
        double win = Program.Opt(a, "--win", 10), hop = Program.Opt(a, "--hop", 5);
        var p = Program.Positional(a).Where(x => x != outf).ToArray();
        var files = new List<string>(); foreach (var x in p) { if (Directory.Exists(x)) files.AddRange(Directory.EnumerateFiles(x, "*.wav").OrderBy(z => z)); else files.Add(x); }
        var res = new List<string>[files.Count];
        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, fi =>
        {
            var rows = new List<string>(); var c = Wav.Read(files[fi]); var m = Dsp.Mono(c); var env = SpeechEnv(m, out double fr); string nm = Path.GetFileName(files[fi]);
            for (double t = 0; t + win <= c.Dur + 0.01; t += hop)
            {
                var s = Dsp.Slice(c, t, t + win); var an = Dsp.Analyze(s, false);
                int f0 = (int)(t * fr), f1 = Math.Min(env.Length, (int)((t + win) * fr));
                var r = Dsp.RmsDb(Dsp.Mono(s), 0.25); double mu = r.Average(), sd = Math.Sqrt(r.Select(v => (v - mu) * (v - mu)).Average());
                var vo = Voiced(m, t, t + win);
                rows.Add($"{nm},{t:F1},{t + win:F1},{an.Lufs:F1},{an.Centroid:F0},{sd:F1},{an.Syllabic:F3},{an.Harm:F3},{EnvPeriod(env, fr, f0, f1):F3},{vo.mean:F3},{vo.frac:F3},{an.OnsetRate:F1}");
            }
            res[fi] = rows; Console.Error.WriteLine("windowed " + nm);
        });
        var sb = new StringBuilder("file,t0,t1,lufs,centroid,rmssd,syl,harm,envper,vclar,vfrac,onsets\n");
        foreach (var r in res) foreach (var l in r) sb.AppendLine(l);
        if (outf != null) File.WriteAllText(outf, sb.ToString()); else Console.Write(sb.ToString());
        return 0;
    }

    /// <summary>Positive control for the voicedness / syllabic / periodicity screens: synthetic pink-ish noise with an added harmonic 'sung' tone (180 Hz, vibrato, syllable-rate 4 Hz gating) at several SNRs.</summary>
    static int SelfTest(string[] a)
    {
        var rnd = new Random(7); int fs = 44100, n = fs * 12;
        Console.WriteLine("snr_dB  syl   harm  envper vclar vfrac   (noise only first)");
        foreach (double snr in new[] { -100.0, 60, 25, 10, 3, -3, -10 })
        {
            var c = new Clip(fs, 1, n); double lp = 0; var noise = new float[n];
            for (int i = 0; i < n; i++) { lp = 0.97 * lp + (rnd.NextDouble() - 0.5) * 0.3; noise[i] = (float)lp; }
            double nr = 0; foreach (var v in noise) nr += v * v; nr = Math.Sqrt(nr / n);
            double amp = nr * Math.Pow(10, snr / 20) * Math.Sqrt(2) / 2.2;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)fs, f0 = 180 * (1 + 0.01 * Math.Sin(2 * Math.PI * 5.5 * t)), ph = 2 * Math.PI * f0 * t, s = 0;
                for (int h = 1; h <= 8; h++) s += Math.Sin(h * ph) / h;
                double gate = 0.5 + 0.5 * Math.Sin(2 * Math.PI * 4 * t);
                c.D[0][i] = noise[i] + (snr < -50 ? 0 : (float)(amp * s * gate));
            }
            var an = Dsp.Analyze(c, false); var env = SpeechEnv(c, out double fr); var vo = Voiced(c, 0, c.Dur);
            Console.WriteLine($"{snr,6:F0}  {an.Syllabic:F2}  {an.Harm:F2}  {EnvPeriod(env, fr, 0, env.Length):F2}   {vo.mean:F2}  {vo.frac:F2}");
        }
        return 0;
    }

    // ------------------------------------------------------------------ cenv
    static int Env(string[] a)
    {
        var p = Program.Positional(a); var c = Wav.Read(p[0]); double hop = p.Length > 1 ? D(p[1]) : 0.5;
        double t0 = p.Length > 3 ? D(p[2]) : 0, t1 = p.Length > 3 ? D(p[3]) : c.Dur;
        var s = Dsp.Slice(c, t0, t1); var r = Dsp.RmsDb(s, hop);
        Console.WriteLine($"{Path.GetFileName(p[0])} {c.Rate}Hz x{c.Ch} {c.Dur:F1}s; rms dBFS per {hop}s from {t0}s");
        var line = new StringBuilder(); int per = (int)Math.Max(1, Math.Round(5.0 / hop));
        for (int i = 0; i < r.Length; i++)
        {
            if (i % per == 0) { if (line.Length > 0) Console.WriteLine(line); line.Clear(); line.Append($"{t0 + i * hop,6:F1}s: "); }
            line.Append($"{r[i],4:F0} ");
        }
        if (line.Length > 0) Console.WriteLine(line);
        return 0;
    }

    static int Probe(string[] a)
    {
        var p = Program.Positional(a); var c = Wav.Read(p[0]); var s = Dsp.Slice(c, D(p[1]), D(p[2]));
        var an = Dsp.Analyze(s, false); var env = SpeechEnv(Dsp.Mono(s), out double fr);
        var vo = Voiced(Dsp.Mono(s), 0, s.Dur); Console.WriteLine($"{an}  envper={EnvPeriod(env, fr, 0, env.Length):F2} vclar={vo.mean:F2} vfrac={vo.frac:F2}");
        return 0;
    }

    // ------------------------------------------------------------------ filters
    sealed class Bq
    {
        double b0, b1, b2, a1, a2, z1, z2;
        public Bq(bool high, double fc, double fs, double q = 0.7071)
        {
            double w = 2 * Math.PI * fc / fs, cs = Math.Cos(w), al = Math.Sin(w) / (2 * q), a0 = 1 + al;
            if (!high) { b0 = (1 - cs) / 2 / a0; b1 = (1 - cs) / a0; b2 = b0; } else { b0 = (1 + cs) / 2 / a0; b1 = -(1 + cs) / a0; b2 = b0; }
            a1 = -2 * cs / a0; a2 = (1 - al) / a0;
        }
        public double Step(double x) { double y = b0 * x + z1; z1 = b1 * x - a1 * y + z2; z2 = b2 * x - a2 * y; return y; }
    }
    static void Filter(Clip c, bool high, double fc)
    {
        for (int ch = 0; ch < c.Ch; ch++) { var f1 = new Bq(high, fc, c.Rate); var f2 = new Bq(high, fc, c.Rate); var d = c.D[ch]; for (int i = 0; i < d.Length; i++) d[i] = (float)f2.Step(f1.Step(d[i])); }
    }
    /// <summary>Speed/pitch change by semitones through the windowed-sinc resampler (negative = slower and lower).</summary>
    static Clip Pitch(Clip c, double semis)
    {
        double f = Math.Pow(2, semis / 12); int rate = c.Rate; var r = Dsp.Resample(c, (int)Math.Round(rate / f)); r.Rate = rate; return r;
    }
    /// <summary>Slow dynamic leveller: divides by the RMS smoothed over `sec` seconds (centred) and restores the overall RMS, with the correction limited to +-maxDb. Evens out a bed so the loop wrap has no level step.</summary>
    static void Flatten(Clip c, double sec, double maxDb)
    {
        int n = c.N, w = Math.Max(1, (int)(sec * c.Rate)); var pw = new double[n + 1];
        for (int i = 0; i < n; i++) { double s = 0; for (int ch = 0; ch < c.Ch; ch++) s += c.D[ch][i] * c.D[ch][i]; pw[i + 1] = pw[i] + s / c.Ch; }
        double tot = pw[n] / n; var g = new float[n]; double lim = Dsp.FromDb(maxDb);
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - w / 2), hi = Math.Min(n, i + w / 2); double sm = (pw[hi] - pw[lo]) / (hi - lo);
            g[i] = (float)Math.Clamp(Math.Sqrt(tot / (sm + 1e-12)), 1 / lim, lim);
        }
        // smooth the gain curve itself (200 ms) so the leveller never modulates audibly
        int sw = (int)(0.2 * c.Rate); var cs = new double[n + 1]; for (int i = 0; i < n; i++) cs[i + 1] = cs[i] + g[i];
        for (int ch = 0; ch < c.Ch; ch++) for (int i = 0; i < n; i++) { int lo = Math.Max(0, i - sw / 2), hi = Math.Min(n, i + sw / 2); c.D[ch][i] *= (float)((cs[hi] - cs[lo]) / (hi - lo)); }
    }

    /// <summary>Slice [a,b); for mono-sourced material the right channel is taken from the same recording `wide` seconds later (or earlier near the end), which decorrelates a diffuse crowd into a believable stereo field without synthesising anything.</summary>
    static Clip SliceWide(Clip st, bool mono, double a, double b, double wide)
    {
        var l = Dsp.Slice(st, a, b); if (!mono) return l;
        double shift = (b + wide <= st.Dur) ? wide : -wide; if (a + shift < 0) return l;
        var r = Dsp.Slice(st, a + shift, b + shift); for (int i = 0; i < l.N; i++) l.D[1][i] = r.D[0][i];
        return l;
    }

    /// <summary>Loop-seam numbers: step = sample jump across the wrap relative to the typical sample-to-sample change (ratio ~1 is seamless); edge = level change dB between the last and first 50 ms; dip = level of the 0.5 s either side of the wrap vs the whole loop (dB).</summary>
    public static (double step, double ratio, double edge, double r0, double r1, double dip) SeamMetrics(Clip c)
    {
        int n = c.N, w = (int)(0.05 * c.Rate); double step = 0, typ = 0, e0 = 0, e1 = 0;
        for (int ch = 0; ch < c.Ch; ch++)
        {
            var d = c.D[ch]; step += Math.Abs(d[0] - d[n - 1]); double dd = 0; int m = Math.Min(c.Rate, n - 1); for (int i = 1; i <= m; i++) dd += Math.Abs(d[i] - d[i - 1]); typ += dd / m;
            for (int i = 0; i < w; i++) { e0 += d[i] * d[i]; e1 += d[n - w + i] * d[n - w + i]; }
        }
        step /= c.Ch; typ /= c.Ch; double r0 = 10 * Math.Log10(e0 / (w * c.Ch) + 1e-12), r1 = 10 * Math.Log10(e1 / (w * c.Ch) + 1e-12);
        int hw = (int)(0.5 * c.Rate); double ej = 0, ea = 0;
        for (int ch = 0; ch < c.Ch; ch++) { var d = c.D[ch]; for (int i = 0; i < hw; i++) ej += d[i] * d[i] + d[n - 1 - i] * d[n - 1 - i]; for (int i = 0; i < n; i++) ea += d[i] * d[i]; }
        double dip = 10 * Math.Log10(ej / (2.0 * hw * c.Ch) + 1e-12) - 10 * Math.Log10(ea / ((double)n * c.Ch) + 1e-12);
        return (step, step / (typ + 1e-9), r1 - r0, r0, r1, dip);
    }

    static void SoftLimit(Clip c, double ceilDb)
    {
        float ceil = (float)Dsp.FromDb(ceilDb), knee = ceil * 0.7f;
        foreach (var ch in c.D) for (int i = 0; i < ch.Length; i++)
            {
                float v = ch[i], av = Math.Abs(v); if (av <= knee) continue;
                float ex = (av - knee) / (ceil - knee); float o = knee + (ceil - knee) * (float)Math.Tanh(ex); ch[i] = Math.Sign(v) * o;
            }
    }

    // ------------------------------------------------------------------ render
    sealed class SrcInfo { public string Key, Path, Pack; public Clip Clip; public bool Mono; }
    sealed class Entry { public string Kind, Group, Nn; public Dictionary<string, string> O = new(); public List<string> Adds = new(); }

    static Clip Stereo44(Clip c)
    {
        if (c.Rate != 44100) c = Dsp.Resample(c, 44100);
        return Dsp.Stereo(c);
    }

    static int Render(string[] a)
    {
        var p = Program.Positional(a); string only = null; int oi = Array.IndexOf(a, "--only"); if (oi >= 0) { only = a[oi + 1]; p = p.Where(x => x != only).ToArray(); }
        string outDir = null, manifest = null; var srcs = new Dictionary<string, SrcInfo>(); var entries = new List<Entry>();
        foreach (var raw in File.ReadAllLines(p[0]))
        {
            var line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue;
            var tok = Tokenize(line); string v = tok[0];
            if (v == "out") outDir = tok[1];
            else if (v == "manifest") manifest = tok[1];
            else if (v == "src") srcs[tok[1]] = new SrcInfo { Key = tok[1], Path = tok[2], Pack = tok.Count > 3 ? tok[3] : "" };
            else if (v == "loop" || v == "shot")
            {
                var e = new Entry { Kind = v, Group = tok[1], Nn = tok[2] };
                for (int i = 3; i < tok.Count; i++) { int eq = tok[i].IndexOf('='); if (eq < 0) continue; string k = tok[i][..eq], val = tok[i][(eq + 1)..]; if (k == "add") e.Adds.Add(val); else e.O[k] = val; }
                entries.Add(e);
            }
        }
        Clip Load(string key)
        {
            var s = srcs[key]; if (s.Clip == null) { var raw = Wav.Read(s.Path); s.Mono = raw.Ch == 1; s.Clip = Stereo44(raw); } return s.Clip;
        }
        foreach (var e in entries)
        {
            if (only != null && e.Group != only) continue;
            string src = e.O["src"]; var sc = Load(src);
            double A = D(e.O["a"]), B = D(e.O["b"]); double target = D(e.O["lufs"]);
            double xf = e.O.ContainsKey("xf") ? D(e.O["xf"]) : 2.5;
            Clip o;
            if (e.Kind == "loop")
            {
                if (e.O.ContainsKey("pitch")) throw new Exception("pitch on loops not supported");
                double wide = e.O.ContainsKey("wide") ? D(e.O["wide"]) : 7.3, snap = e.O.ContainsKey("snap") ? D(e.O["snap"]) : 1.5, step = 0.2;
                // one working segment that covers every candidate start/end; layers, filters and the leveller are applied once, before looping (a filter after the wrap would leave a start-up step at the seam)
                double lo = Math.Max(0, A - snap - xf), hi = Math.Min(sc.Dur, B + snap);
                var big = SliceWide(sc, srcs[src].Mono, lo, hi, wide);
                foreach (var ad in e.Adds) { var q = ad.Split(':'); var lay = Dsp.Slice(Load(q[0]), D(q[1]), D(q[2])); big = Dsp.MixInto(big, lay, (int)((D(q[4]) + (A - xf - lo)) * 44100), D(q[3])); }
                big = Dsp.Slice(big, 0, hi - lo);
                if (e.O.ContainsKey("flat")) Flatten(big, D(e.O["flat"]), e.O.ContainsKey("flatmax") ? D(e.O["flatmax"]) : 9);
                if (e.O.ContainsKey("hp")) Filter(big, true, D(e.O["hp"]));
                if (e.O.ContainsKey("lp")) Filter(big, false, D(e.O["lp"]));
                if (e.O.ContainsKey("gain")) Dsp.Gain(big, D(e.O["gain"]));
                double bestScore = double.MaxValue, bA = A, bB = B; o = null;
                for (double a2 = Math.Max(lo + xf, A - snap); a2 <= A + snap + 1e-6; a2 += step)
                    for (double b2 = Math.Max(a2 + 1.0, B - snap); b2 <= Math.Min(hi, B + snap) + 1e-6; b2 += step)
                    {
                        if (Math.Abs((b2 - a2) - (B - A)) > snap) continue;
                        var seg = Dsp.Slice(big, a2 - lo - xf, b2 - lo); var cand = Dsp.MakeLoop(seg, xf, seg.Dur, xf);
                        var m = SeamMetrics(cand); double score = Math.Abs(m.edge) + Math.Abs(m.dip) + 0.5 * Math.Max(0, m.ratio - 2);
                        if (score < bestScore) { bestScore = score; bA = a2; bB = b2; o = cand; }
                    }
                e.O["a"] = bA.ToString("F2", CultureInfo.InvariantCulture); e.O["b"] = bB.ToString("F2", CultureInfo.InvariantCulture); e.O["seamscore"] = bestScore.ToString("F2", CultureInfo.InvariantCulture);
            }
            else
            {
                var work = sc; double pr = 1.0;
                if (e.O.ContainsKey("pitch")) { work = Pitch(sc, D(e.O["pitch"])); pr = Math.Pow(2, -D(e.O["pitch"]) / 12); }
                o = Dsp.Slice(work, A * pr, B * pr);
                foreach (var ad in e.Adds) { var q = ad.Split(':'); var lay = Dsp.Slice(Load(q[0]), D(q[1]), D(q[2])); o = Dsp.MixInto(o, lay, (int)(D(q[4]) * 44100), D(q[3])); }
            }
            if (e.Kind == "shot" && e.O.ContainsKey("hp")) Filter(o, true, D(e.O["hp"]));
            if (e.Kind == "shot" && e.O.ContainsKey("lp")) Filter(o, false, D(e.O["lp"]));
            if (e.Kind == "shot" && e.O.ContainsKey("gain")) Dsp.Gain(o, D(e.O["gain"]));
            if (e.Kind == "shot")
            {
                if (e.O.ContainsKey("ramp"))
                {   // building swell: gain rises from -ramp dB to 0 dB over `rampt` seconds (default first 40% of the clip), smoothstep
                    double rd = D(e.O["ramp"]), rt = e.O.ContainsKey("rampt") ? D(e.O["rampt"]) : o.Dur * 0.4;
                    for (int i = 0; i < o.N; i++) { double t = i / (double)o.Rate / rt; double s = t >= 1 ? 1 : t * t * (3 - 2 * t); float g = (float)Dsp.FromDb(-rd * (1 - s)); for (int c = 0; c < o.Ch; c++) o.D[c][i] *= g; }
                }
                double fi = e.O.ContainsKey("fi") ? D(e.O["fi"]) : 0.01, fo = e.O.ContainsKey("fo") ? D(e.O["fo"]) : 0.3;
                Dsp.Fade(o, fi, fo);
            }
            double g0 = Dsp.NormalizeLufs(o, target, 12);   // gain to target; peak guarded by the soft limiter below
            if (Dsp.Db(Dsp.Peak(o)) > -1.3)
            {
                for (int it = 0; it < 6 && Dsp.Db(Dsp.Peak(o)) > -1.2; it++)
                {
                    SoftLimit(o, -1.4);
                    double cur = Dsp.Lufs(o); Dsp.Gain(o, target - cur);   // restore loudness; the limiter again if needed
                }
                SoftLimit(o, -1.4);
            }
            if (outDir == null) throw new Exception("spec needs an 'out' line");
            string file = Path.Combine(outDir, e.Group, $"{e.Group}_{e.Nn}.wav");
            Wav.Write(file, o, 16);
            Console.WriteLine($"{e.Group}_{e.Nn}  {o.Dur,6:F2}s  lufs={Dsp.Lufs(o),6:F1} (target {target})  peak={Dsp.Db(Dsp.Peak(o)),5:F1}  g={g0:F1}dB");
        }
        if (manifest != null && only == null) WriteManifest(manifest, entries, srcs, outDir);
        return 0;
    }

    static List<string> Tokenize(string s)
    {
        var r = new List<string>(); var sb = new StringBuilder(); bool q = false;
        foreach (char ch in s)
        {
            if (ch == '"') { q = !q; continue; }
            if (char.IsWhiteSpace(ch) && !q) { if (sb.Length > 0) { r.Add(sb.ToString()); sb.Clear(); } continue; }
            sb.Append(ch);
        }
        if (sb.Length > 0) r.Add(sb.ToString());
        return r;
    }

    static void WriteManifest(string path, List<Entry> entries, Dictionary<string, SrcInfo> srcs, string outDir)
    {
        var sb = new StringBuilder("group,file,loop,dur_s,lufs,peak_db,centroid_hz,syllabic,source_pack,source_file,source_range_s,derived,notes\n");
        foreach (var e in entries)
        {
            string file = Path.Combine(outDir, e.Group, $"{e.Group}_{e.Nn}.wav"); if (!File.Exists(file)) continue;
            var c = Wav.Read(file); var an = Dsp.Analyze(c, false); var s = srcs[e.O["src"]];
            string range = $"{e.O["a"]}-{e.O["b"]}"; foreach (var ad in e.Adds) { var q = ad.Split(':'); range += $" +{q[0]}@{q[1]}-{q[2]}"; }
            string note = e.O.TryGetValue("note", out var nt) ? nt.Replace('"', '\'').Replace(',', ';') : "";
            string der = e.O.TryGetValue("derived", out var dv) ? dv : "0";
            sb.AppendLine($"{e.Group},{Path.GetFileName(file)},{(e.Kind == "loop" ? 1 : 0)},{an.Dur:F2},{an.Lufs:F1},{Dsp.Db(an.Peak):F1},{an.Centroid:F0},{an.Syllabic:F3},{s.Pack},{Path.GetFileName(s.Path)},{range},{der},\"{note}\"");
        }
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine("manifest -> " + path);
    }

    // ------------------------------------------------------------------ ccheck
    static (int rate, int ch, int bits) Header(string f)
    {
        using var fs = File.OpenRead(f); var b = new byte[64]; fs.Read(b, 0, 64);
        return (BitConverter.ToInt32(b, 24), BitConverter.ToInt16(b, 22), BitConverter.ToInt16(b, 34));
    }
    static int Check(string[] a)
    {
        string specPath = null; int si = Array.IndexOf(a, "--spec"); if (si >= 0) specPath = a[si + 1];
        var p = Program.Positional(a).Where(x => x != specPath).ToArray(); string dir = p[0];
        var targets = new Dictionary<string, double>(); var loops = new HashSet<string>();
        if (specPath != null)
            foreach (var raw in File.ReadAllLines(specPath))
            {
                var t = Tokenize(raw.Trim()); if (t.Count < 3 || (t[0] != "loop" && t[0] != "shot")) continue;
                foreach (var kv in t.Skip(3)) if (kv.StartsWith("lufs=")) targets[$"{t[1]}_{t[2]}.wav"] = D(kv[5..]);
                if (t[0] == "loop") loops.Add($"{t[1]}_{t[2]}.wav");
            }
        int bad = 0; var sb = new StringBuilder();
        sb.AppendLine("file,fmt,dur,lufs,target,dLufs,peak,loop,seamStep,stepVsTypical,edge50msDiffDb,edge50msFirst/Last,wrapDipDb,verdict");
        foreach (var f in Directory.EnumerateFiles(dir, "*.wav", SearchOption.AllDirectories).OrderBy(x => x))
        {
            var h = Header(f); var c = Wav.Read(f); string name = Path.GetFileName(f); var an = Dsp.Analyze(c, false);
            bool fmtOk = h.rate == 44100 && h.ch == 2 && h.bits == 16; double pk = Dsp.Db(an.Peak);
            bool hasT = targets.TryGetValue(name, out double tg); bool isLoop = loops.Contains(name) || name.StartsWith("bed_");
            double dl = hasT ? an.Lufs - tg : double.NaN; bool ok = fmtOk && pk <= -0.95 && (!hasT || Math.Abs(dl) <= 1.0);
            string seam = ",,,,,";
            if (isLoop)
            {
                var sm = SeamMetrics(c);
                bool seamOk = sm.ratio <= 6 && Math.Abs(sm.edge) <= 3.0 && Math.Abs(sm.dip) <= 2.0;
                if (!seamOk) ok = false;
                seam = $",{sm.step:F5},{sm.ratio:F2},{sm.edge:F1},{sm.r0:F1}/{sm.r1:F1},{sm.dip:F1}";
            }
            sb.AppendLine($"{name},{(fmtOk ? "ok" : $"{h.rate}/{h.ch}/{h.bits}")},{an.Dur:F1},{an.Lufs:F1},{(hasT ? tg.ToString() : "")},{(hasT ? dl.ToString("F1") : "")},{pk:F1},{(isLoop ? 1 : 0)}{seam},{(ok ? "PASS" : "FAIL")}");
            if (!ok) bad++;
        }
        Console.Write(sb.ToString()); Console.WriteLine($"# {bad} file(s) failed");
        return bad == 0 ? 0 : 1;
    }
}
