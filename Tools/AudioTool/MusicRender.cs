using System.Globalization;
using System.Text;

namespace PixAudio;

/// <summary>
/// mrender spec.txt [--only name,name] [--out musicDir stingerDir]
///   cue NAME loop|shot role="..." lufs=-22 [stmax=-16] [bpm=108] [notes="..."]
///   loop A B X                         source seconds A..B, equal-power crossfade tail X (loop cues; layers are cut from A-X..B)
///   layer SRC [gain=dB] [lp=Hz] [hp=Hz] [env=t:db;t:db] [from=s to=s at=s fi=s fo=s speed=r sweep=r0:r1]   (shot cues use from/to/at; env times are layer-local seconds)
///   rev wet=-12 tail=1.5 [room=0.82 damp=0.35]  (shot cues)   fade IN OUT (shot cues)   tailpad S (silence appended before reverb tail)
/// SRC is bowl.brass etc. (MusicLib.Sources) or ext:fullpath.wav (labelled by the caller in the log).
/// </summary>
public static class MusicRender
{
    sealed class Cue
    {
        public string Name, Kind; public Dictionary<string, string> Kv = new(); public double A, B, X; public List<Dictionary<string, string>> Layers = new();
        public Dictionary<string, string> Rev; public double FadeIn = 0.003, FadeOut = 0.05; public double TailPad;
        public bool IsLoop => Kind == "loop";
        public double N(string k, double d) => Kv.TryGetValue(k, out var v) ? double.Parse(v, CultureInfo.InvariantCulture) : d;
    }

    static Dictionary<string, string> Parse(IEnumerable<string> toks)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in toks) { int i = t.IndexOf('='); if (i > 0) d[t.Substring(0, i)] = t.Substring(i + 1).Trim('"'); }
        return d;
    }
    static List<string> Tokenize(string line)
    {
        var l = new List<string>(); var sb = new StringBuilder(); bool q = false;
        foreach (char c in line) { if (c == '"') { q = !q; sb.Append(c); } else if (char.IsWhiteSpace(c) && !q) { if (sb.Length > 0) { l.Add(sb.ToString()); sb.Clear(); } } else sb.Append(c); }
        if (sb.Length > 0) l.Add(sb.ToString()); return l;
    }
    static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);
    static double G(Dictionary<string, string> kv, string k, double d) => kv.TryGetValue(k, out var v) ? D(v) : d;

    // mseam spec cue [--range 8]: tries shifting the loop window by whole beats (keeps length) and prints the seam metrics of each
    public static int SeamScan(string[] a)
    {
        var p = Program.Positional(a); var cues = new List<Cue>(); Cue cur = null;
        foreach (var raw in File.ReadAllLines(p[0]))
        {
            var line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue; var t = Tokenize(line);
            switch (t[0])
            {
                case "cue": cur = new Cue { Name = t[1], Kind = t[2], Kv = Parse(t.Skip(3)) }; cues.Add(cur); break;
                case "loop": cur.A = D(t[1]); cur.B = D(t[2]); cur.X = D(t[3]); break;
                case "layer": { var kv = Parse(t.Skip(2)); kv["src"] = t[1]; cur.Layers.Add(kv); break; }
            }
        }
        var c = cues.First(x => x.Name == p[1]); double bpm = c.N("bpm", 108), beat = 60 / bpm; int range = (int)Program.Opt(a, "--range", 8);
        for (int k = -range; k <= range; k++)
        {
            var cc = new Cue { Name = c.Name, Kind = c.Kind, Kv = c.Kv, A = c.A + k * beat, B = c.B + k * beat, X = c.X, Layers = c.Layers };
            if (cc.A < cc.X + 0.1) continue; var o = RenderLoop(cc); Dsp.NormalizeLufs(o, -23, -1);
            int w = (int)(0.05 * o.Rate); double e1 = 0, e2 = 0, step = 0, jump = 0; int cnt = 0;
            for (int ch = 0; ch < 2; ch++) { for (int i = 0; i < w; i++) { e1 += (double)o.D[ch][i] * o.D[ch][i]; e2 += (double)o.D[ch][o.N - w + i] * o.D[ch][o.N - w + i]; } for (int i = 1; i < w; i++) { double d1 = o.D[ch][i] - o.D[ch][i - 1], d2 = o.D[ch][o.N - w + i] - o.D[ch][o.N - w + i - 1]; step += d1 * d1 + d2 * d2; cnt += 2; } jump = Math.Max(jump, Math.Abs(o.D[ch][0] - o.D[ch][o.N - 1])); }
            var dbl = Dsp.Concat(o, o); var fx = Dsp.Flux(dbl, 512); int sf = o.N / 512; double sm = 0; for (int q = sf - 3; q <= sf + 3; q++) sm = Math.Max(sm, fx[q]);
            var wins = new List<double>(); for (int q = 3; q < fx.Length - 3; q += 3) { double mm = 0; for (int r = q - 3; r <= q + 3; r++) mm = Math.Max(mm, fx[r]); wins.Add(mm); }
            // long-window (0.5 s) level step across the wrap
            int w5 = (int)(0.5 * o.Rate); double l1 = 0, l2 = 0; for (int ch = 0; ch < 2; ch++) for (int i = 0; i < w5; i++) { l1 += (double)o.D[ch][i] * o.D[ch][i]; l2 += (double)o.D[ch][o.N - w5 + i] * o.D[ch][o.N - w5 + i]; }
            Console.WriteLine($"shift {k,3} beats  A={cc.A:F2} B={cc.B:F2}  lvl50ms={10 * Math.Log10((e1 + 1e-12) / (e2 + 1e-12)),6:F2} dB  lvl500ms={10 * Math.Log10((l1 + 1e-12) / (l2 + 1e-12)),6:F2} dB  jump/step={jump / (Math.Sqrt(step / cnt) + 1e-9):F2}  seamFluxPct={100.0 * wins.Count(x => x <= sm) / wins.Count:F0}");
        }
        return 0;
    }

    public static int Run(string[] a)
    {
        var p = Program.Positional(a); string specPath = p[0];
        var only = a.Contains("--only") ? a[Array.IndexOf(a, "--only") + 1].Split(',') : null;
        string outRoot = @"C:\Users\peyto\Desktop\PeytonParrishGames\PIX PRO FOOTBALL\Assets\_Licensed\PIX\Audio\";
        string musicDir = outRoot + @"Music\", stingDir = outRoot + @"Stingers\";
        var cues = new List<Cue>(); Cue cur = null;
        foreach (var raw in File.ReadAllLines(specPath))
        {
            var line = raw.Trim(); if (line.Length == 0 || line.StartsWith("#")) continue; var t = Tokenize(line);
            switch (t[0])
            {
                case "out": musicDir = t[1].TrimEnd('\\') + "\\"; stingDir = t[2].TrimEnd('\\') + "\\"; break;
                case "cue": cur = new Cue { Name = t[1], Kind = t[2], Kv = Parse(t.Skip(3)) }; cues.Add(cur); break;
                case "loop": cur.A = D(t[1]); cur.B = D(t[2]); cur.X = D(t[3]); break;
                case "layer": { var kv = Parse(t.Skip(2)); kv["src"] = t[1]; cur.Layers.Add(kv); break; }
                case "rev": cur.Rev = Parse(t.Skip(1)); break;
                case "fade": cur.FadeIn = D(t[1]); cur.FadeOut = D(t[2]); break;
                case "tailpad": cur.TailPad = D(t[1]); break;
                default: throw new Exception("bad spec line: " + line);
            }
        }
        var manifest = new StringBuilder("file,role,loop,dur_s,lufs,peak_db,bpm,source_file,source_range_s,stems_used,notes\n");
        Console.WriteLine("name                  kind  dur_s   lufs  stmax  peak_dB  gain_dB");
        foreach (var c in cues)
        {
            if (only != null && !only.Contains(c.Name)) continue;
            Clip o = c.IsLoop ? RenderLoop(c) : RenderShot(c);
            double gainDb = Finish(c, o);
            string path = (c.IsLoop || c.Kv.GetValueOrDefault("dir") == "music" ? musicDir : stingDir) + c.Name + ".wav";
            Wav.Write(path, o, 16);
            var back = Wav.Read(path);
            double lufs = Dsp.Lufs(back), st = back.Dur > 3 ? Dsp.ShortTermMax(back) : lufs, pk = Dsp.Db(Dsp.Peak(back));
            Console.WriteLine($"{c.Name,-21} {c.Kind,-5} {back.Dur,6:F2} {lufs,6:F1} {st,6:F1} {pk,7:F2} {gainDb,7:F1}");
            var srcs = c.Layers.Select(l => l["src"]).Distinct().ToList();
            string srcFiles = string.Join("|", srcs.Select(s => MusicLib.Sources.TryGetValue(s, out var f) ? Path.GetFileName(f) : Path.GetFileName(s.Replace("ext:", ""))).Distinct());
            string range = c.IsLoop ? $"{c.A:F2}-{c.B:F2}" : string.Join("|", c.Layers.Select(l => $"{G(l, "from", 0):F2}-{G(l, "to", 0):F2}").Distinct());
            string nm(string s) => s.Contains('.') ? s.Substring(s.IndexOf('.') + 1) : s;
            string stems = string.Join("+", srcs.Select(s => (s.StartsWith("ext:") ? "EPIKTON:" : s.StartsWith("bowl") ? "bowl:" : s.StartsWith("house") ? "house:" : "") + nm(s)));
            manifest.AppendLine($"{c.Name}.wav,\"{c.Kv.GetValueOrDefault("role", "")}\",{(c.IsLoop ? 1 : 0)},{back.Dur:F2},{lufs:F1},{pk:F2},{c.N("bpm", 0):F0},\"{srcFiles}\",\"{range}\",\"{stems}\",\"{c.Kv.GetValueOrDefault("notes", "")}\"");
        }
        if (only == null) File.WriteAllText(musicDir + "music_manifest.csv", manifest.ToString());
        else File.WriteAllText(Path.Combine(Path.GetDirectoryName(specPath), "manifest_partial.csv"), manifest.ToString());
        return 0;
    }

    static double[] ParseEnvT(string env, out double[] db)
    {
        var parts = env.Split(';', StringSplitOptions.RemoveEmptyEntries); var t = new double[parts.Length]; db = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++) { var kv = parts[i].Split(':'); t[i] = D(kv[0]); db[i] = D(kv[1]); }
        return t;
    }

    /// <summary>Source layer cut [from,to] with speed / sweep, filters, dB envelope, gain and fades.</summary>
    static Clip Layer(Dictionary<string, string> l, double from, double to, bool shot)
    {
        var src = MusicLib.Src(l["src"]);
        to = Math.Min(to, src.Dur); var c = Dsp.Slice(src, from, to);
        if (shot && l.TryGetValue("sweep", out var sw))
        {
            var rr = sw.Split(':'); double r0 = D(rr[0]), r1 = D(rr[1]); double outDur = G(l, "dur", (to - from) / ((r0 + r1) / 2)); c = Sweep(c, r0, r1, outDur);
        }
        else if (shot && G(l, "speed", 1) != 1) c = MusicLib.Varispeed(c, G(l, "speed", 1));
        if (l.TryGetValue("hp", out var hp)) MusicLib.Filter(c, true, D(hp));
        if (l.TryGetValue("lp", out var lp)) MusicLib.Filter(c, false, D(lp));
        double g = G(l, "gain", 0); float gl = (float)Dsp.FromDb(g);
        float[] env = null;
        if (l.TryGetValue("env", out var es))
        {
            var et = ParseEnvT(es, out var edb); env = new float[c.N];
            for (int i = 0; i < c.N; i++)
            {
                double t = i / (double)c.Rate; double v;
                if (t <= et[0]) v = edb[0]; else if (t >= et[^1]) v = edb[^1]; else { int k = 0; while (et[k + 1] < t) k++; double f = (t - et[k]) / (et[k + 1] - et[k]); v = edb[k] + (edb[k + 1] - edb[k]) * f; }
                env[i] = (float)Dsp.FromDb(v);
            }
        }
        for (int ch = 0; ch < c.Ch; ch++) for (int i = 0; i < c.N; i++) c.D[ch][i] *= gl * (env == null ? 1f : env[i]);
        if (shot) Dsp.Fade(c, G(l, "fi", 0.003), G(l, "fo", 0.02));
        return c;
    }

    /// <summary>Variable-rate playback (pitch+speed sweep) with linear interpolation: rate ramps r0 -> r1 over outDur seconds (exponential in pitch).</summary>
    public static Clip Sweep(Clip c, double r0, double r1, double outDur)
    {
        int n = (int)(outDur * c.Rate); var o = new Clip(c.Rate, c.Ch, n); double pos = 0;
        for (int i = 0; i < n; i++)
        {
            double f = i / (double)n; double r = r0 * Math.Pow(r1 / r0, f); int k = (int)pos; double fr = pos - k;
            for (int ch = 0; ch < c.Ch; ch++) { float x0 = k < c.N ? c.D[ch][k] : 0f, x1 = k + 1 < c.N ? c.D[ch][k + 1] : 0f; o.D[ch][i] = (float)(x0 + (x1 - x0) * fr); }
            pos += r;
        }
        return o;
    }

    static Clip RenderLoop(Cue c)
    {
        int rate = 44100; double s0 = c.A - c.X; var parts = new List<Clip>();
        foreach (var l in c.Layers)
        {
            var seg = Layer(l, s0, c.B, false); parts.Add(seg);
        }
        int n = parts.Max(x => x.N); var sum = new Clip(rate, 2, n);
        foreach (var p in parts) for (int ch = 0; ch < 2; ch++) for (int i = 0; i < p.N; i++) sum.D[ch][i] += p.D[ch][i];
        // sum now starts at source time (A-X); MakeLoop wants source-timeline positions -> pad is unnecessary, shift the indices instead
        return Dsp.MakeLoop(sum, c.X, c.B - s0, c.X);
    }

    static Clip RenderShot(Cue c)
    {
        int rate = 44100; var placed = new List<(Clip clip, int at)>(); int end = 0;
        foreach (var l in c.Layers)
        {
            var lc = Layer(l, G(l, "from", 0), G(l, "to", 1e9), true); int at = (int)(G(l, "at", 0) * rate); placed.Add((lc, at)); end = Math.Max(end, at + lc.N);
        }
        end += (int)(c.TailPad * rate);
        var sum = new Clip(rate, 2, end);
        foreach (var (clip, at) in placed) for (int ch = 0; ch < 2; ch++) for (int i = 0; i < clip.N; i++) sum.D[ch][at + i] += clip.D[Math.Min(ch, clip.Ch - 1)][i];
        if (c.Rev != null)
        {
            double tail = G(c.Rev, "tail", 1.5); var wet = MusicLib.Reverb(sum, G(c.Rev, "room", 0.82), G(c.Rev, "damp", 0.35), tail);
            float wg = (float)Dsp.FromDb(G(c.Rev, "wet", -12) + 12);
            var o = new Clip(rate, 2, wet.N);
            for (int ch = 0; ch < 2; ch++) for (int i = 0; i < wet.N; i++) o.D[ch][i] = (i < sum.N ? sum.D[ch][i] : 0f) + wet.D[ch][i] * wg;
            sum = o;
        }
        Dsp.Fade(sum, c.FadeIn, c.FadeOut);
        return sum;
    }

    /// <summary>Loudness target (integrated and/or short-term max) then a look-ahead limiter at -1.3 dBFS so the 16-bit file peaks below -1 dBFS. Returns the applied gain.</summary>
    static double Finish(Cue c, Clip o)
    {
        double ceil = -1.3;
        double g = double.MaxValue;
        if (c.Kv.ContainsKey("lufs")) g = Math.Min(g, c.N("lufs", -23) - Dsp.Lufs(o));
        if (c.Kv.ContainsKey("stmax")) g = Math.Min(g, c.N("stmax", -16) - (o.Dur > 3 ? Dsp.ShortTermMax(o) : Dsp.Lufs(o)));
        if (g == double.MaxValue) g = 0;
        Dsp.Gain(o, g);
        if (Dsp.Db(Dsp.Peak(o)) > ceil)
        {
            if (c.IsLoop)
            {
                var tri = Dsp.Concat(o, o, o); Limit(tri, ceil);
                for (int ch = 0; ch < o.Ch; ch++) Array.Copy(tri.D[ch], o.N, o.D[ch], 0, o.N);
            }
            else Limit(o, ceil);
            // re-aim loudness after limiting (limiting lowers it slightly): small make-up only if the peak allows
        }
        if (c.Kv.ContainsKey("lufs") && !c.Kv.ContainsKey("stmax"))
        {
            for (int it = 0; it < 3; it++)
            {
                double err = c.N("lufs", -23) - Dsp.Lufs(o); if (err < 0.15) break;
                Dsp.Gain(o, err); g += err;
                if (c.IsLoop) { var tri = Dsp.Concat(o, o, o); Limit(tri, ceil); for (int ch = 0; ch < o.Ch; ch++) Array.Copy(tri.D[ch], o.N, o.D[ch], 0, o.N); } else Limit(o, ceil);
            }
        }
        return g;
    }

    public static void Limit(Clip c, double ceilDb, double lookMs = 4, double releaseMs = 150)
    {
        int n = c.N, la = Math.Max(1, (int)(lookMs * 0.001 * c.Rate)); float ceil = (float)Dsp.FromDb(ceilDb);
        var g0 = new float[n]; for (int i = 0; i < n; i++) { float pk = 0; for (int ch = 0; ch < c.Ch; ch++) pk = Math.Max(pk, Math.Abs(c.D[ch][i])); g0[i] = pk > ceil ? ceil / pk : 1f; }
        // sliding minimum over +-la
        var gm = new float[n]; var dq = new LinkedList<int>(); int w = 2 * la + 1;
        for (int i = 0; i < n + la; i++)
        {
            if (i < n) { while (dq.Count > 0 && g0[dq.Last.Value] >= g0[i]) dq.RemoveLast(); dq.AddLast(i); }
            int centre = i - la; if (centre < 0) continue;
            while (dq.Count > 0 && dq.First.Value < centre - la) dq.RemoveFirst();
            gm[centre] = g0[dq.First.Value];
        }
        // box smoothing over +-la (guaranteed <= g0 at every sample)
        var gs = new float[n]; double acc = 0; var pre = new double[n + 1]; for (int i = 0; i < n; i++) pre[i + 1] = pre[i] + gm[i];
        for (int i = 0; i < n; i++) { int lo = Math.Max(0, i - la), hi = Math.Min(n - 1, i + la); gs[i] = (float)((pre[hi + 1] - pre[lo]) / (hi - lo + 1)); }
        // release shaping (never above the box-smoothed gain)
        double rel = Math.Exp(-1.0 / (releaseMs * 0.001 * c.Rate)); double s;
        var gf = new float[n]; s = 1.0;
        for (int i = 0; i < n; i++) { s = gs[i] < s ? gs[i] : s * rel + gs[i] * (1 - rel); if (s > gs[i]) s = gs[i]; gf[i] = (float)s; }
        for (int ch = 0; ch < c.Ch; ch++) for (int i = 0; i < n; i++) c.D[ch][i] *= gf[i];
    }

    // ------------------------------------------------------------------------------------------------ mcheck
    static readonly string[] LoopNames = { "menu_calm", "menu_drive", "menu_house", "pregame_bed", "halftime_loop", "match_under_low", "match_under_tense" };

    public static int Check(string[] a)
    {
        string dir = Program.Positional(a)[0];
        Console.WriteLine("file                     dur_s    lufs  stmax  peak_dB  clipped  first/last50ms: lvl_diff_dB  jump/step  startAbs  endAbs");
        foreach (var f in Directory.EnumerateFiles(dir, "*.wav").OrderBy(x => x))
        {
            var c = Wav.Read(f); string name = Path.GetFileNameWithoutExtension(f); bool loop = LoopNames.Contains(name);
            double lufs = Dsp.Lufs(c), st = c.Dur > 3 ? Dsp.ShortTermMax(c) : lufs, pk = Dsp.Db(Dsp.Peak(c)); int clipped = 0;
            foreach (var ch in c.D) foreach (var v in ch) if (Math.Abs(v) >= 0.9995f) clipped++;
            int w = (int)(0.05 * c.Rate); string seam = "";
            double e1 = 0, e2 = 0, step = 0; int cnt = 0; double jump = 0;
            for (int ch = 0; ch < c.Ch; ch++)
            {
                for (int i = 0; i < w; i++) { e1 += (double)c.D[ch][i] * c.D[ch][i]; e2 += (double)c.D[ch][c.N - w + i] * c.D[ch][c.N - w + i]; }
                for (int i = 1; i < w; i++) { double d1 = c.D[ch][i] - c.D[ch][i - 1], d2 = c.D[ch][c.N - w + i] - c.D[ch][c.N - w + i - 1]; step += d1 * d1 + d2 * d2; cnt += 2; }
                jump = Math.Max(jump, Math.Abs(c.D[ch][0] - c.D[ch][c.N - 1]));
            }
            double stepRms = Math.Sqrt(step / Math.Max(1, cnt)), lvl = 10 * Math.Log10((e1 + 1e-12) / (e2 + 1e-12));
            if (loop) { var dbl = Dsp.Concat(c, c); var fx = Dsp.Flux(dbl, 512); int sf = c.N / 512; double sm = 0; for (int q = sf - 3; q <= sf + 3; q++) sm = Math.Max(sm, fx[q]); var wins = new List<double>(); for (int q = 3; q < fx.Length - 3; q += 3) { double mm = 0; for (int r = q - 3; r <= q + 3; r++) mm = Math.Max(mm, fx[r]); wins.Add(mm); } double pct = 100.0 * wins.Count(x => x <= sm) / wins.Count; seam = $"{lvl,8:F2}  {jump / (stepRms + 1e-9),8:F2}  seamFluxPct={pct,5:F0}"; }
            else seam = "       -         -";
            double s0 = 0, s1 = 0; for (int ch = 0; ch < c.Ch; ch++) { s0 = Math.Max(s0, Math.Abs(c.D[ch][0])); s1 = Math.Max(s1, Math.Abs(c.D[ch][c.N - 1])); }
            Console.WriteLine($"{name + (loop ? "*" : ""),-24} {c.Dur,6:F1} {lufs,7:F1} {st,6:F1} {pk,8:F2} {clipped,7}   {seam}   {s0:F5}  {s1:F5}");
        }
        return 0;
    }
}
