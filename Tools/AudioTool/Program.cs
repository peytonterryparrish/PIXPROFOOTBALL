using System.Globalization;
using System.Text;

namespace PixAudio;

/// <summary>
/// PIX audio offline tool (v0.11.0 AUDIO+CROWD). Reads / writes WAV, measures loudness (BS.1770 LUFS), spectrum, vocal-ness and tempo, cuts, loops and mixes.
/// Verbs live in partial-free static classes registered below; add a new file with a static Run(string[] args) and register it in Verbs.
///   dotnet run -c Release --project Tools/AudioTool -- info file.wav [...]
///   ... cut in.wav out.wav start end [fadeIn fadeOut] [--lufs -23] [--peak -3] [--mono] [--rate 44100]
///   ... loop in.wav out.wav start end xfade [--lufs -23]
///   ... scan dir > report.csv   (features for every wav under dir)
/// </summary>
public static class Program
{
    public static readonly Dictionary<string, Func<string[], int>> Verbs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["info"] = Info, ["cut"] = Cut, ["loop"] = Loop, ["scan"] = Scan,
    };

    public static int Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        foreach (var t in typeof(Program).Assembly.GetTypes())
        {
            if (!t.Name.EndsWith("Verbs") || !t.IsAbstract) continue;
            var m = t.GetMethod("Register", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (m == null) continue;
            foreach (var kv in (IDictionary<string, Func<string[], int>>)m.Invoke(null, null)) Verbs[kv.Key] = kv.Value;
        }
        if (args.Length == 0 || !Verbs.TryGetValue(args[0], out var verb)) { Console.Error.WriteLine("verbs: " + string.Join(", ", Verbs.Keys)); return 2; }
        try { return verb(args.Skip(1).ToArray()); }
        catch (Exception e) { Console.Error.WriteLine("ERROR: " + e.Message); return 1; }
    }

    // ------------------------------------------------------------ helpers used by every verb file
    public static double Opt(string[] a, string name, double def) { int i = Array.IndexOf(a, name); return i >= 0 && i + 1 < a.Length ? double.Parse(a[i + 1], CultureInfo.InvariantCulture) : def; }
    public static bool Flag(string[] a, string name) => Array.IndexOf(a, name) >= 0;
    public static string[] Positional(string[] a)
    {
        var l = new List<string>();
        for (int i = 0; i < a.Length; i++) { if (a[i].StartsWith("--")) { if (a[i] != "--mono" && a[i] != "--stereo" && a[i] != "--csv") i++; continue; } l.Add(a[i]); }
        return l.ToArray();
    }
    public static Clip Prep(Clip c, string[] opts)
    {
        double rate = Opt(opts, "--rate", 0); if (rate > 0 && c.Rate != (int)rate) c = Dsp.Resample(c, (int)rate);
        if (Flag(opts, "--mono")) c = Dsp.Mono(c); else if (Flag(opts, "--stereo")) c = Dsp.Stereo(c);
        return c;
    }
    public static void Finish(Clip c, string[] opts)
    {
        if (opts.Contains("--lufs")) Dsp.NormalizeLufs(c, Opt(opts, "--lufs", -23), Opt(opts, "--ceiling", -1));
        else if (opts.Contains("--peak")) Dsp.NormalizePeak(c, Opt(opts, "--peak", -3));
    }

    static int Info(string[] a)
    {
        foreach (var f in Positional(a))
        {
            var c = Wav.Read(f); var ft = Dsp.Analyze(c);
            Console.WriteLine($"{Path.GetFileName(f)}  {c.Rate} Hz x{c.Ch}  {ft}");
        }
        return 0;
    }

    static int Cut(string[] a)
    {
        var p = Positional(a); if (p.Length < 4) { Console.Error.WriteLine("cut in out start end [fadeIn fadeOut]"); return 2; }
        var c = Prep(Wav.Read(p[0]), a);
        double s = double.Parse(p[2], CultureInfo.InvariantCulture), e = double.Parse(p[3], CultureInfo.InvariantCulture);
        var o = Dsp.Slice(c, s, e);
        double fi = p.Length > 4 ? double.Parse(p[4], CultureInfo.InvariantCulture) : 0.005, fo = p.Length > 5 ? double.Parse(p[5], CultureInfo.InvariantCulture) : 0.02;
        Dsp.Fade(o, fi, fo); Finish(o, a); Wav.Write(p[1], o, (int)Opt(a, "--bits", 16));
        Console.WriteLine($"{p[1]} {o.Dur:F2}s lufs={Dsp.Lufs(o):F1} peak={Dsp.Db(Dsp.Peak(o)):F1}dB");
        return 0;
    }

    static int Loop(string[] a)
    {
        var p = Positional(a); if (p.Length < 5) { Console.Error.WriteLine("loop in out start end xfade"); return 2; }
        var c = Prep(Wav.Read(p[0]), a);
        var o = Dsp.MakeLoop(c, double.Parse(p[2], CultureInfo.InvariantCulture), double.Parse(p[3], CultureInfo.InvariantCulture), double.Parse(p[4], CultureInfo.InvariantCulture));
        Finish(o, a); Wav.Write(p[1], o, (int)Opt(a, "--bits", 16));
        Console.WriteLine($"{p[1]} {o.Dur:F2}s lufs={Dsp.Lufs(o):F1} peak={Dsp.Db(Dsp.Peak(o)):F1}dB");
        return 0;
    }

    static int Scan(string[] a)
    {
        var p = Positional(a); var files = Directory.EnumerateFiles(p[0], "*.wav", SearchOption.AllDirectories).OrderBy(x => x).ToList();
        var sb = new StringBuilder("file,dur,peakdb,lufs,short,centroid,r85,flat,sub,low,mid,high,air,syl,harm,bpm,bpmconf,onsets\n");
        foreach (var f in files)
        {
            try
            {
                var c = Wav.Read(f); var t = Dsp.Analyze(c);
                sb.AppendLine($"\"{Path.GetRelativePath(p[0], f)}\",{t.Dur:F2},{Dsp.Db(t.Peak):F1},{t.Lufs:F1},{t.Short:F1},{t.Centroid:F0},{t.Rolloff85:F0},{t.Flatness:F3},{t.BandSub:F3},{t.BandLow:F3},{t.BandMid:F3},{t.BandHigh:F3},{t.BandAir:F3},{t.Syllabic:F3},{t.Harm:F3},{t.Bpm:F1},{t.BpmConf:F2},{t.OnsetRate:F2}");
            }
            catch (Exception e) { sb.AppendLine($"\"{Path.GetRelativePath(p[0], f)}\",ERROR {e.Message.Replace(',', ';')}"); }
        }
        Console.Write(sb.ToString());
        return 0;
    }
}
