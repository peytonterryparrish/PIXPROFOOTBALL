using System.Globalization;
namespace PixAudio;
public static class MusicEnvVerbs
{
    public static IDictionary<string, Func<string[], int>> Register() => new Dictionary<string, Func<string[], int>>
    {
        // menv set stemSpec t0 t1 [--hop 0.25] : RMS dB envelope of the layered stems (or a wav path with set="file")
        ["menv"] = a =>
        {
            Clip c = a[0] == "file" ? Wav.Read(a[1]) : MusicLoopVerbs.Layered(a[0], a[1]); double t0 = double.Parse(a[2], CultureInfo.InvariantCulture), t1 = double.Parse(a[3], CultureInfo.InvariantCulture), hop = Program.Opt(a, "--hop", 0.25);
            var sb = new System.Text.StringBuilder();
            for (double t = t0; t < t1; t += hop)
            {
                int i0 = (int)(t * c.Rate), i1 = Math.Min(c.N, (int)((t + hop) * c.Rate)); if (i1 <= i0) break; double s = 0, pk = 0; for (int ch = 0; ch < c.Ch; ch++) for (int i = i0; i < i1; i++) { double v = c.D[ch][i]; s += v * v; pk = Math.Max(pk, Math.Abs(v)); }
                sb.Append($"{t,7:F2}:{10 * Math.Log10(s / ((i1 - i0) * c.Ch) + 1e-12),6:F1} ");
            }
            Console.WriteLine(sb); return 0;
        },
    };
}
