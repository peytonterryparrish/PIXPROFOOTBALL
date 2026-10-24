using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PixAudio;

/// <summary>
/// PIX on-field SFX curation verbs (v0.11.0 AUDIO+CROWD, SFX sub-track).
///   sfxprobe  file|dir ...            measured features per file: dur, peak(+position), centroid, rolloff, decay time, clip flag, onset list
///   sfxbuild  spec.csv [outDir]       render the whole curated library from one reproducible spec (+ sfx_manifest.csv)
///   sfxverify [outDir]                final audit: mono / 44.1k / 16-bit / peak -3 / no leading silence / max duration / counts
/// Spec: CSV  group,layers,maxdur,fadeout_ms,notes   ('#' = comment; no commas inside fields)
///   layers = layer ('|' layer)*   ; layer = pack:relpath[#start-end][@offset_s][*gain_db][~k]
///   pack: df (DraftFever SFX folder) ss ad gs wow gm (extracted to sfx_tmp\pack\dir\file.wav)
///   #a-b = source cut in seconds (either side may be empty) ; @o = place layer o seconds into the clip ; *g = layer gain in dB (each layer is peak-normalised to 0 dB first)
///   ~k = use the k-th detected transient of the (cut) source: window [onset_k - 4 ms, next onset - 8 ms] (splits multi-step files)
/// </summary>
public static class SfxVerbs
{
    const string RepoRoot = @"C:\Users\peyto\Desktop\PeytonParrishGames\PIX PRO FOOTBALL";
    const string Tmp = @"D:\PIXFootball\Work\Audio\sfx_tmp";
    static string OutDefault => Path.Combine(RepoRoot, @"Assets\_Licensed\PIX\Audio\Sfx");
    static readonly Dictionary<string, double> MaxDur = new()
    {
        ["hit_light"] = .6, ["hit_medium"] = .8, ["hit_heavy"] = 1.2, ["hit_big"] = 1.8, ["block_thud"] = .5, ["helmet_clack"] = .4, ["shed_slap"] = .4, ["grunt_hit"] = .8, ["grunt_effort"] = .7, ["pile_scrum"] = 2.5, ["ground_thump"] = .8,
        ["snap_slap"] = .4, ["handoff_slap"] = .4, ["throw_whoosh"] = .7, ["ball_whirr"] = 2, ["catch_soft"] = .4, ["catch_hard"] = .5, ["catch_contested"] = .7, ["drop_thud"] = .6, ["bounce_dry"] = .6, ["bounce_wet"] = .6, ["bounce_snow"] = .6, ["fumble_pop"] = .5,
        ["kick_thump"] = 1.2, ["kick_punt"] = 1.2, ["kick_short"] = .8,
        ["step_run_dry"] = .3, ["step_run_wet"] = .3, ["step_run_snow"] = .3, ["step_walk_dry"] = .35, ["step_walk_wet"] = .35, ["step_walk_snow"] = .35, ["plant_cut"] = .35, ["slide_turf"] = 1.2,
        ["whistle_short"] = .9, ["whistle_long"] = 2.5, ["chain_clank"] = 1.2, ["flag_toss"] = .6, ["cadence"] = 1.5,
    };

        /// <summary>Groups whose first strong transient (within 20 dB of the peak) is aligned to t=0 so gameplay can trigger them on contact; other groups start at -38 dB.</summary>
    static readonly HashSet<string> Align = new() { "hit_light", "hit_medium", "hit_heavy", "hit_big", "block_thud", "helmet_clack", "shed_slap", "ground_thump", "snap_slap", "handoff_slap", "catch_soft", "catch_hard", "catch_contested", "drop_thud", "fumble_pop", "bounce_dry", "bounce_wet", "bounce_snow", "kick_thump", "kick_punt", "kick_short", "step_run_dry", "step_run_wet", "step_run_snow", "step_walk_dry", "step_walk_wet", "step_walk_snow", "plant_cut" };
    static readonly Dictionary<string, double> MinDur = new() { ["pile_scrum"] = 1.2, ["ball_whirr"] = 1.0, ["hit_big"] = 0.4 };

    public static IDictionary<string, Func<string[], int>> Register() => new Dictionary<string, Func<string[], int>>
    {
        ["sfxprobe"] = Probe, ["sfxbuild"] = Build, ["sfxverify"] = Verify, ["sfxenv"] = EnvPrint, ["sfxdups"] = Dups,
    };

    // ------------------------------------------------------------------ source resolution
    static string Root(string pack) => pack switch
    {
        "df" => Path.Combine(RepoRoot, @"Assets\_Licensed\DraftFever\Audio\SFX"),
        _ => Path.Combine(Tmp, pack),
    };
    static string Resolve(string pack, string rel) => Path.Combine(Root(pack), rel.Replace('/', Path.DirectorySeparatorChar));
    static readonly Dictionary<string, Clip> Cache = new();
    static Clip Load(string path)
    {
        if (Cache.TryGetValue(path, out var c)) return c;
        var w = Wav.Read(path);
        if (w.Ch == 2) { double sm = 0, sl = 0, sr = 0; for (int i = 0; i < w.N; i++) { double l = w.D[0][i], r = w.D[1][i]; sm += (l + r) * (l + r) / 4; sl += l * l; sr += r * r; } double d = 10 * Math.Log10((sm + 1e-12) / (Math.Max(sl, sr) + 1e-12)); if (d < -4) Console.WriteLine($"  WARN phasey stereo ({d:F1} dB mono loss): {Path.GetFileName(path)}"); }
        w = Dsp.Mono(w); if (w.Rate != 44100) w = Dsp.Resample(w, 44100);
        Cache[path] = w; return w;
    }

    // ------------------------------------------------------------------ envelope / onset helpers
    /// <summary>RMS envelope in dBFS, window 4 ms, hop 1 ms. env[i] describes [i ms, i ms + 4 ms].</summary>
    public static double[] Env(Clip a)
    {
        int hop = a.Rate / 1000, win = 4 * hop; int n = Math.Max(0, (a.N - win) / hop + 1); var e = new double[n]; var x = a.D[0];
        for (int i = 0; i < n; i++) { double s = 0; int o = i * hop; for (int k = 0; k < win; k++) { double v = x[o + k]; s += v * v; } e[i] = 10 * Math.Log10(s / win + 1e-14); }
        return e;
    }

    /// <summary>Transient onsets in seconds (start of the rise). Threshold relative to the global envelope peak.</summary>
    public static List<double> Onsets(Clip a, double relDb = -34, double riseDb = 9, double refracMs = 110)
    {
        var e = Env(a); var res = new List<double>(); if (e.Length < 20) return res;
        double gmax = e.Max(); int last = -100000;
        for (int i = 12; i < e.Length - 2; i++)
        {
            if (i - last < refracMs) continue;
            if (e[i] < gmax + relDb) continue;
            double mn = 1e9; for (int k = Math.Max(0, i - 14); k < i - 1; k++) mn = Math.Min(mn, e[k]);
            if (e[i] - mn < riseDb) continue;
            int pk = i; for (int k = i; k < Math.Min(e.Length, i + 30); k++) if (e[k] > e[pk]) pk = k;
            int st = i; for (int k = i; k >= Math.Max(0, i - 14); k--) { if (e[k] > e[pk] - 30) st = k; else break; }
            res.Add(st / 1000.0); last = pk;
        }
        return res;
    }

    static double PeakAt(Clip a, out double peak)
    {
        var x = a.D[0]; int pi = 0; float p = 0; for (int i = 0; i < x.Length; i++) { float v = Math.Abs(x[i]); if (v > p) { p = v; pi = i; } }
        peak = p; return pi / (double)a.Rate;
    }

    static int ClipRun(Clip a, float th = 0.995f)
    {
        var x = a.D[0]; int run = 0, best = 0; for (int i = 0; i < x.Length; i++) { if (Math.Abs(x[i]) >= th) { run++; if (run > best) best = run; } else run = 0; }
        return best;
    }

    /// <summary>Time from the envelope peak until the 4 ms RMS falls dB below the peak (default 30).</summary>
    static double Decay(double[] e, double dB = 30)
    {
        if (e.Length == 0) return 0; int pk = 0; for (int i = 0; i < e.Length; i++) if (e[i] > e[pk]) pk = i;
        for (int i = pk; i < e.Length; i++) if (e[i] < e[pk] - dB) return (i - pk) / 1000.0;
        return (e.Length - pk) / 1000.0;
    }

    /// <summary>sfxenv file [hopMs=25] [start end]: coarse RMS envelope (dB re peak) as a text strip, for choosing cut points without listening.</summary>
    static int EnvPrint(string[] a)
    {
        var p = Program.Positional(a); var c = Dsp.Mono(Wav.Read(p[0])); if (c.Rate != 44100) c = Dsp.Resample(c, 44100);
        int hop = (int)(44.1 * (p.Length > 1 ? double.Parse(p[1], CultureInfo.InvariantCulture) : 25)); double t0 = p.Length > 3 ? double.Parse(p[2], CultureInfo.InvariantCulture) : 0, t1 = p.Length > 3 ? double.Parse(p[3], CultureInfo.InvariantCulture) : c.Dur;
        var x = c.D[0]; double mu = x.Average(); int s0 = (int)(t0 * 44100), s1 = Math.Min(x.Length, (int)(t1 * 44100));
        double pk = 1e-9; for (int i = s0; i < s1; i++) pk = Math.Max(pk, Math.Abs(x[i] - mu));
        var sb = new StringBuilder(); int n = 0;
        for (int i = s0; i + hop <= s1; i += hop)
        {
            double s = 0; for (int k = 0; k < hop; k++) { double v = x[i + k] - mu; s += v * v; }
            double db = 10 * Math.Log10(s / hop + 1e-14) - 20 * Math.Log10(pk); int lv = (int)Math.Clamp((db + 60) / 6, 0, 10);
            if (n % 10 == 0) sb.Append($"\n{i / 44100.0,6:F2}s ");
            sb.Append("_.:-=+*#%@@"[lv]); n++;
        }
        Console.WriteLine(Path.GetFileName(p[0]) + " peak=" + Dsp.Db(pk).ToString("F1") + sb);
        return 0;
    }

    /// <summary>sfxdups [outDir]: flags near-identical clips within a group (peak-aligned normalised cross-correlation over 120 ms &gt; 0.9) so one recording is not used twice under two names.</summary>
    static int Dups(string[] a)
    {
        string dir = Program.Positional(a).FirstOrDefault() ?? OutDefault; int n = 0;
        foreach (var gd in Directory.GetDirectories(dir).OrderBy(x => x))
        {
            var fs = Directory.GetFiles(gd, "*.wav").OrderBy(x => x).ToList(); var cl = fs.Select(Wav.Read).ToList();
            for (int i = 0; i < fs.Count; i++) for (int j = i + 1; j < fs.Count; j++)
            {
                double best = 0; int w = 5292; var x = cl[i].D[0]; var y = cl[j].D[0];
                for (int lag = -441; lag <= 441; lag += 4)
                {
                    double xy = 0, xx = 1e-12, yy = 1e-12; for (int k = 0; k < w; k++) { int p = k, q = k + lag; if (p >= x.Length || q < 0 || q >= y.Length) continue; xy += x[p] * y[q]; xx += x[p] * x[p]; yy += y[q] * y[q]; }
                    best = Math.Max(best, xy / Math.Sqrt(xx * yy));
                }
                if (best > 0.9) { Console.WriteLine($"DUP {Path.GetFileName(fs[i])} ~ {Path.GetFileName(fs[j])}  r={best:F3}"); n++; }
            }
        }
        Console.WriteLine($"{n} near-duplicate pairs"); return n == 0 ? 0 : 3;
    }

    // ------------------------------------------------------------------ probe
    static int Probe(string[] a)
    {
        var files = new List<string>();
        foreach (var p in a.Where(s => !s.StartsWith("--")))
        {
            if (Directory.Exists(p)) files.AddRange(Directory.EnumerateFiles(p, "*.wav", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            else if (File.Exists(p)) files.Add(p);
            else foreach (var f in Directory.EnumerateFiles(Path.GetDirectoryName(Path.GetFullPath(p)) ?? ".", Path.GetFileName(p))) files.Add(f);
        }
        bool onsets = !Program.Flag(a, "--noonsets");
        string baseDir = a.FirstOrDefault(s => Directory.Exists(s)) ?? "";
        if (baseDir.Length > 0) baseDir = Path.GetFullPath(baseDir);
        foreach (var f in files)
        {
            try
            {
                var raw = Wav.Read(f); var c = Dsp.Mono(raw); if (c.Rate != 44100) c = Dsp.Resample(c, 44100); { var xx = c.D[0]; double mu = 0; foreach (var v in xx) mu += v; mu /= Math.Max(1, xx.Length); for (int i = 0; i < xx.Length; i++) xx[i] -= (float)mu; }
                double pkAt = PeakAt(c, out double pk); var env = Env(c); var ft = c.N > 4096 ? Dsp.Analyze(c, false) : null;
                var on = onsets ? Onsets(c) : new List<double>();
                string name = baseDir.Length > 0 && Path.GetFullPath(f).StartsWith(baseDir) ? Path.GetRelativePath(baseDir, f) : Path.GetFileName(f);
                Console.WriteLine($"{name}  {raw.Rate}x{raw.Ch} {c.Dur:F2}s pk={Dsp.Db(pk):F1}@{pkAt:F3} cen={(ft?.Centroid ?? 0):F0} r85={(ft?.Rolloff85 ?? 0):F0} t30={Decay(env):F2} clip={ClipRun(c)}" + (onsets ? $" on[{on.Count}]=" + string.Join(",", on.Take(14).Select(t => t.ToString("F2"))) : ""));
            }
            catch (Exception ex) { Console.WriteLine($"{f}  ERROR {ex.Message}"); }
        }
        return 0;
    }

    // ------------------------------------------------------------------ build
    sealed class Layer { public string Pack, File; public double A = -1, B = -1, Off, Gain, Ext; public int K; public string Raw; }

    /// <summary>Sustain extension (whistles, spins): finds the steady part of the clip (35%..70% of the active length), then repeats it with correlated, equal-power crossfaded splices until the clip is `target` seconds long.</summary>
    static Clip Extend(Clip src, double target)
    {
        var x = src.D[0]; int n = x.Length; if (n / 44100.0 >= target) return src;
        float pk = Dsp.Peak(src); double lim = pk * Dsp.FromDb(-24); int act = n; for (int i = n - 1; i > 0; i--) if (Math.Abs(x[i]) > lim) { act = i; break; }
        double aS = 0.35 * act / 44100.0, bS = 0.70 * act / 44100.0; int xf = (int)(0.05 * 44100);
        bS = Dsp.BestLoopEnd(src, aS, bS, 0.08); int a = (int)(aS * 44100), b = (int)(bS * 44100); if (a < xf) a = xf;
        var outL = new List<float>(); for (int i = 0; i < b; i++) outL.Add(x[i]);
        int need = (int)(target * 44100) - (n - b);
        void Splice(int from)
        {
            int baseI = outL.Count - xf;
            for (int i = 0; i < xf; i++) { double w = (i + 1.0) / (xf + 1.0); outL[baseI + i] = (float)(outL[baseI + i] * Math.Cos(w * Math.PI / 2) + x[a - xf + i] * Math.Sin(w * Math.PI / 2)); }
            for (int i = a; i < from; i++) outL.Add(x[i]);
        }
        while (outL.Count < need) Splice(b);
        Splice(n);
        var o = new Clip(44100, 1, outL.Count); outL.CopyTo(o.D[0]); return o;
    }
    static readonly Regex LayerRx = new(@"^(?<p>[a-z]+):(?<f>[^#@&*~]+?)(#(?<a>[\d.]*)-(?<b>[\d.]*))?(@(?<o>[\d.]+))?(&(?<e>[\d.]+))?(\*(?<g>-?[\d.]+))?(~(?<k>\d+))?$");
    static Layer ParseLayer(string s)
    {
        var m = LayerRx.Match(s.Trim()); if (!m.Success) throw new Exception("bad layer: " + s);
        var l = new Layer { Pack = m.Groups["p"].Value, File = m.Groups["f"].Value, Raw = s.Trim() };
        if (m.Groups["a"].Success && m.Groups["a"].Value != "") l.A = double.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture);
        if (m.Groups["b"].Success && m.Groups["b"].Value != "") l.B = double.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture);
        if (m.Groups["o"].Success) l.Off = double.Parse(m.Groups["o"].Value, CultureInfo.InvariantCulture);
        if (m.Groups["g"].Success) l.Gain = double.Parse(m.Groups["g"].Value, CultureInfo.InvariantCulture);
        if (m.Groups["k"].Success) l.K = int.Parse(m.Groups["k"].Value);
        if (m.Groups["e"].Success) l.Ext = double.Parse(m.Groups["e"].Value, CultureInfo.InvariantCulture);
        return l;
    }

    sealed class Row { public string Group, Layers, Notes; public double MaxDur; public double FadeMs; }
    sealed class Result { public string Group, File, Source, Pack, Range, Notes; public double Dur, PeakDb, RmsDb, Centroid; }

    /// <summary>Renders one layer to a normalised (peak 0 dB) mono clip, returns it with the source range actually used.</summary>
    static Clip RenderLayer(Layer l, out string range, List<string> warn)
    {
        string path = Resolve(l.Pack, l.File); if (!File.Exists(path)) throw new Exception("missing source " + path);
        var src = Load(path); double a = l.A < 0 ? 0 : l.A, b = l.B < 0 ? src.Dur : Math.Min(l.B, src.Dur);
        var cut = Dsp.Slice(src, a, b);
        if (l.K > 0)
        {
            var on = Onsets(cut); if (on.Count < l.K) throw new Exception($"{l.File}: only {on.Count} onsets, wanted #{l.K}");
            double s = Math.Max(0, on[l.K - 1] - 0.004), e = l.K < on.Count ? on[l.K] - 0.008 : cut.Dur;
            a += s; b = a + (e - s); cut = Dsp.Slice(src, a, b);
        }
        range = $"{a:F3}-{b:F3}";
        if (ClipRun(cut) >= 4) warn.Add("clipped-source");
        cut = cut.Copy();
        if (l.Ext > 0) { cut = Extend(cut, l.Ext); range += $"&{l.Ext:F1}s"; }
        var x = cut.D[0];
        double mean = 0; for (int i = 0; i < x.Length; i++) mean += x[i]; mean /= Math.Max(1, x.Length); for (int i = 0; i < x.Length; i++) x[i] -= (float)mean;
        Dsp.NormalizePeak(cut, 0);
        return cut;
    }

    static int Build(string[] a)
    {
        var p = Program.Positional(a); if (p.Length < 1) { Console.Error.WriteLine("sfxbuild spec.csv [outDir]"); return 2; }
        string outDir = p.Length > 1 ? p[1] : OutDefault;
        var rows = new List<Row>();
        foreach (var line in File.ReadAllLines(p[0]))
        {
            var t = line.Trim(); if (t.Length == 0 || t.StartsWith("#") || t.StartsWith("group,")) continue;
            var c = t.Split(','); if (c.Length < 2) throw new Exception("bad row: " + line);
            double md = c.Length > 2 && c[2].Trim() != "" ? double.Parse(c[2], CultureInfo.InvariantCulture) : MaxDur.GetValueOrDefault(c[0].Trim(), 1.0);
            rows.Add(new Row { Group = c[0].Trim(), Layers = c[1].Trim(), MaxDur = md, FadeMs = c.Length > 3 && c[3].Trim() != "" ? double.Parse(c[3], CultureInfo.InvariantCulture) : -1, Notes = c.Length > 4 ? c[4].Trim() : "" });
        }
        foreach (var g in rows.Select(r => r.Group).Distinct())
        {
            string gd = Path.Combine(outDir, g); if (Directory.Exists(gd)) foreach (var f in Directory.GetFiles(gd, "*.wav")) File.Delete(f);
        }
        var counts = new Dictionary<string, int>(); var results = new List<Result>(); int fails = 0;
        foreach (var r in rows)
        {
            try
            {
                var warn = new List<string>(); var layers = r.Layers.Split('|').Select(ParseLayer).ToList();
                var ranges = new List<string>(); Clip mix = null;
                foreach (var l in layers)
                {
                    var c = RenderLayer(l, out var rg, warn); ranges.Add(rg); Dsp.Gain(c, l.Gain);
                    mix = mix == null ? Dsp.MixInto(new Clip(44100, 1, 0), c, (int)Math.Round(l.Off * 44100)) : Dsp.MixInto(mix, c, (int)Math.Round(l.Off * 44100));
                }
                var o = Finish(mix, r, warn, out string info);
                if (warn.Contains("clipped-source")) { Console.WriteLine($"DROP {r.Group} {r.Layers}: clipped source"); fails++; continue; }
                counts[r.Group] = counts.GetValueOrDefault(r.Group) + 1; string fn = $"{r.Group}_{counts[r.Group]:00}.wav";
                Wav.Write(Path.Combine(outDir, r.Group, fn), o, 16);
                var ft = Dsp.Analyze(o, false); double sum2 = 0; foreach (var v in o.D[0]) sum2 += v * v;
                results.Add(new Result
                {
                    Group = r.Group, File = fn, Dur = o.Dur, PeakDb = Dsp.Db(Dsp.Peak(o)), RmsDb = Dsp.Db(Math.Sqrt(sum2 / o.N)), Centroid = ft.Centroid,
                    Pack = string.Join("+", layers.Select(l => l.Pack).Distinct()), Source = string.Join(" + ", layers.Select(l => l.Pack + ":" + l.File)), Range = string.Join(" + ", ranges),
                    Notes = (r.Notes + (info.Length > 0 ? " [" + info + "]" : "")).Trim(),
                });
                Console.WriteLine($"{fn,-22} {o.Dur:F2}s pk={Dsp.Db(Dsp.Peak(o)):F1} rms={Dsp.Db(Math.Sqrt(sum2 / o.N)):F1} cen={ft.Centroid:F0} {info}");
            }
            catch (Exception ex) { Console.WriteLine($"FAIL {r.Group} {r.Layers}: {ex.Message}"); fails++; }
        }
        string man = Path.Combine(outDir, "sfx_manifest.csv"); var keep = new List<string>();
        if (File.Exists(man)) foreach (var l in File.ReadAllLines(man).Skip(1)) { var g = l.Split(',')[0]; if (!counts.ContainsKey(g)) keep.Add(l); }
        var sb = new StringBuilder("group,file,dur_s,peak_db,rms_db,centroid_hz,source_pack,source_file,source_range_s,notes\n");
        foreach (var l in keep) sb.AppendLine(l);
        foreach (var r in results) sb.AppendLine($"{r.Group},{r.File},{r.Dur:F3},{r.PeakDb:F1},{r.RmsDb:F1},{r.Centroid:F0},{r.Pack},{Q(r.Source)},{Q(r.Range)},{Q(r.Notes)}");
        File.WriteAllText(man, sb.ToString());
        Console.WriteLine($"built {results.Count} clips, {fails} failed/dropped, {counts.Count} groups -> {outDir}");
        return fails > 0 ? 3 : 0;
    }
    static string Q(string s) => "\"" + s.Replace("\"", "'").Replace("\n", " ") + "\"";

    /// <summary>Trim leading silence, cap length, fade tail, remove DC, normalise sample peak to -3 dBFS.</summary>
    static Clip Finish(Clip m, Row r, List<string> warn, out string info)
    {
        var x = m.D[0]; int n = x.Length; double mean = 0; for (int i = 0; i < n; i++) mean += x[i]; mean /= Math.Max(1, n); for (int i = 0; i < n; i++) x[i] -= (float)mean;
        float pk = Dsp.Peak(m); float th = pk * (float)Dsp.FromDb(Align.Contains(r.Group) ? -20 : -38);
        int s = 0; while (s < n && Math.Abs(x[s]) < th) s++;
        s = Math.Max(0, s - (int)(0.003 * 44100));
        int max = (int)(r.MaxDur * 44100); int e = Math.Min(n, s + max);
        int blk = 441; int last = s; double lim = Dsp.FromDb(Dsp.Db(pk) - 52);
        for (int i = e - blk; i >= s; i -= blk / 2) { double q = 0; for (int k = 0; k < blk; k++) q += x[i + k] * x[i + k]; if (Math.Sqrt(q / blk) > lim) { last = i + blk; break; } }
        bool capped = false;
        if (last < e) e = Math.Min(e, last + (int)(0.04 * 44100)); else capped = true;
        var o = new Clip(44100, 1, Math.Max(1, e - s)); Array.Copy(x, s, o.D[0], 0, o.N);
        bool stepCut = false;
        if (r.Group.StartsWith("step_") || r.Group == "plant_cut")
        {
            var on = Onsets(o, -30, 8, 80); double t2 = on.FirstOrDefault(t => t >= 0.17);
            if (t2 > 0) { int n2 = (int)((t2 - 0.012) * 44100); if (n2 > 0.12 * 44100 && n2 < o.N) { var o2 = new Clip(44100, 1, n2); Array.Copy(o.D[0], 0, o2.D[0], 0, n2); o = o2; stepCut = true; } }
        }
        double tailDb = Dsp.Db(Rms(o.D[0], Math.Max(0, o.N - 441), o.N)) - Dsp.Db(Dsp.Peak(o));
        double fo = r.FadeMs > 0 ? r.FadeMs / 1000.0 : (capped && tailDb > -40 ? 0.08 : tailDb > -50 ? 0.045 : 0.025);
        fo = Math.Min(fo, o.Dur * 0.4);
        double mm = 0; var y = o.D[0]; for (int i = 0; i < y.Length; i++) mm += y[i]; mm /= y.Length; for (int i = 0; i < y.Length; i++) y[i] -= (float)mm;
        Dsp.Fade(o, 0.0015, fo);
        Dsp.NormalizePeak(o, -3.0);
        double pkAt = PeakAt(o, out _); info = (stepCut ? "stepcut " : "") + (capped ? "cut@max " : "") + $"fo={fo * 1000:F0}ms tail={tailDb:F0}dB peak@{pkAt:F3}s";
        return o;
    }
    static double Rms(float[] x, int a, int b) { double s = 0; for (int i = a; i < b; i++) s += x[i] * x[i]; return Math.Sqrt(s / Math.Max(1, b - a)); }

    // ------------------------------------------------------------------ verify
    static int Verify(string[] a)
    {
        string dir = Program.Positional(a).FirstOrDefault() ?? OutDefault; int bad = 0, total = 0;
        foreach (var gd in Directory.GetDirectories(dir).OrderBy(x => x))
        {
            string g = Path.GetFileName(gd); var files = Directory.GetFiles(gd, "*.wav").OrderBy(x => x).ToList(); var probs = new List<string>(); double lo = 1e9, hi = -1e9, dmax = 0;
            foreach (var f in files)
            {
                var b = File.ReadAllBytes(f); int ch = BitConverter.ToUInt16(b, 22), rate = BitConverter.ToInt32(b, 24), bits = BitConverter.ToUInt16(b, 34);
                var c = Wav.Read(f); double pk = Dsp.Db(Dsp.Peak(c)); lo = Math.Min(lo, pk); hi = Math.Max(hi, pk); dmax = Math.Max(dmax, c.Dur);
                string nm = Path.GetFileName(f);
                if (ch != 1 || rate != 44100 || bits != 16) probs.Add($"{nm}: fmt {ch}ch/{rate}/{bits}");
                if (Math.Abs(pk + 3) > 0.3) probs.Add($"{nm}: peak {pk:F2}");
                float th = Dsp.Peak(c) * (float)Dsp.FromDb(-40); int s = 0; var x = c.D[0]; while (s < x.Length && Math.Abs(x[s]) < th) s++;
                if (s > 0.006 * 44100) probs.Add($"{nm}: lead {s / 44.1:F1}ms");
                if (MaxDur.TryGetValue(g, out var md) && c.Dur > md + 0.002) probs.Add($"{nm}: dur {c.Dur:F2}>{md}");
                if (MinDur.TryGetValue(g, out var mn) && c.Dur < mn - 0.002) probs.Add($"{nm}: dur {c.Dur:F2}<min {mn}");
                if (ClipRun(c, 0.9999f) >= 3) probs.Add($"{nm}: clip");
                if (Math.Abs(x[x.Length - 1]) > 0.003) probs.Add($"{nm}: tail not faded ({x[x.Length - 1]:F4})");
            }
            total += files.Count; bad += probs.Count;
            Console.WriteLine($"{g,-16} n={files.Count,2} peak[{lo:F2},{hi:F2}] maxdur={dmax:F2}  " + (probs.Count == 0 ? "OK" : "PROBLEMS " + string.Join("; ", probs)));
        }
        Console.WriteLine($"total {total} files, {bad} problems");
        return bad == 0 ? 0 : 3;
    }
}
