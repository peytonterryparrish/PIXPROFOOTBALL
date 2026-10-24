using System.Globalization;
using System.Numerics;
namespace PixAudio;
public static class MusicModVerbs
{
    public static IDictionary<string, Func<string[], int>> Register() => new Dictionary<string, Func<string[], int>>
    {
        // mmod bpm item... : top envelope-modulation peaks (0.4-16 Hz) of the 100-3400 Hz band with their ratio to the beat rate. Tempo-locked peaks (ratio ~ k/4) = rhythmic LFO / arpeggio, not speech.
        ["mmod"] = a =>
        {
            var p = Program.Positional(a); double bpm = double.Parse(p[0], CultureInfo.InvariantCulture), beatHz = bpm / 60;
            foreach (var it in p.Skip(1))
            {
                Clip c = MusicLib.Sources.ContainsKey(it) ? MusicLib.Src(it) : Wav.Read(it); c = c.Copy(); MusicLib.Filter(c, true, 100); MusicLib.Filter(c, false, 3400);
                var m = Dsp.Mono(c).D[0]; int hop = 441; int n = m.Length / hop; var env = new double[n];
                for (int i = 0; i < n; i++) { double s = 0; for (int k = 0; k < hop; k++) s += (double)m[i * hop + k] * m[i * hop + k]; env[i] = Math.Log(s / hop + 1e-12); }
                int P = 1; while (P < n) P <<= 1; var x = new Complex[P]; double mean = env.Average(); for (int i = 0; i < n; i++) x[i] = (env[i] - mean) * (0.5 - 0.5 * Math.Cos(2 * Math.PI * i / n)); Dsp.Fft(x);
                double df = 100.0 / P; var pk = new List<(double f, double e)>(); double tot = 0;
                for (int k = 2; k < P / 2; k++) { double f = k * df, e = x[k].Magnitude; if (f < 0.4 || f > 16) continue; e *= e; tot += e; if (e > x[k - 1].Magnitude * x[k - 1].Magnitude && e >= x[k + 1].Magnitude * x[k + 1].Magnitude) pk.Add((f, e)); }
                var top = pk.OrderByDescending(q => q.e).Take(4).ToList(); double top4 = top.Sum(q => q.e);
                Console.WriteLine($"{(it.Length > 30 ? Path.GetFileNameWithoutExtension(it) : it),-30} top peaks Hz (ratio to beat): " + string.Join("  ", top.Select(q => $"{q.f:F2} ({q.f / beatHz:F2}) {100 * q.e / tot:F0}%")) + $"   top4 share {100 * top4 / tot:F0}%");
            }
            return 0;
        },
    };
}
