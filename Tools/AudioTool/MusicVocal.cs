using System.Numerics;
namespace PixAudio;
public static class MusicVocalVerbs
{
    public static IDictionary<string, Func<string[], int>> Register() => new Dictionary<string, Func<string[], int>>
    {
        // mvocal item... : feature row per item. item = source name (bowl.brass), or a wav path; "path@t0-t1" cuts a range. Prints energy shares above 600 Hz / 1 kHz (where words live), syllabic modulation, tonal peaks.
        ["mvocal"] = a =>
        {
            Console.WriteLine("item                                   dur   lufs  cen  r85  >600Hz  >1kHz  3.4-8k  syl   harm");
            foreach (var it in Program.Positional(a))
            {
                string path = it; double t0 = -1, t1 = -1; int at = it.IndexOf('@'); if (at > 0) { path = it.Substring(0, at); var r = it.Substring(at + 1).Split('-'); t0 = double.Parse(r[0], System.Globalization.CultureInfo.InvariantCulture); t1 = double.Parse(r[1], System.Globalization.CultureInfo.InvariantCulture); }
                Clip c = MusicLib.Sources.ContainsKey(path) ? MusicLib.Src(path) : Wav.Read(path); if (c.Rate != 44100) c = Dsp.Resample(c, 44100);
                if (t0 >= 0) c = Dsp.Slice(c, t0, t1);
                var f = Dsp.Analyze(c, false); var m = Dsp.Mono(c).D[0]; const int N = 4096; var buf = new Complex[N]; double hz = 44100.0 / N; double tot = 0, e600 = 0, e1k = 0;
                for (int st = 0; st + N <= m.Length; st += N * 3)
                {
                    for (int i = 0; i < N; i++) buf[i] = m[st + i] * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / N)); Dsp.Fft(buf);
                    for (int k = 1; k < N / 2; k++) { double e = buf[k].Real * buf[k].Real + buf[k].Imaginary * buf[k].Imaginary, fq = k * hz; tot += e; if (fq > 600) e600 += e; if (fq > 1000) e1k += e; }
                }
                string nm = Path.GetFileNameWithoutExtension(path); if (MusicLib.Sources.ContainsKey(path)) nm = path; if (t0 >= 0) nm += $"@{t0:F0}-{t1:F0}";
                Console.WriteLine($"{(nm.Length > 36 ? nm.Substring(0, 36) : nm),-36} {f.Dur,6:F0} {f.Lufs,6:F1} {f.Centroid,4:F0} {f.Rolloff85,4:F0}  {100 * e600 / tot,5:F1}%  {100 * e1k / tot,5:F1}%  {100 * f.BandHigh,5:F1}%  {f.Syllabic:F2}  {f.Harm:F2}");
            }
            return 0;
        },
    };
}
