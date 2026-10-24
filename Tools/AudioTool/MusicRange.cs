using System.Globalization;
namespace PixAudio;
public static class MusicRangeVerbs
{
    public static IDictionary<string, Func<string[], int>> Register() => new Dictionary<string, Func<string[], int>>
    {
        // mrange set t0 t1 : third-octave band level (dB) of the mix and every stem within [t0,t1]
        ["mrange"] = a =>
        {
            string set = a[0]; double t0 = double.Parse(a[1], CultureInfo.InvariantCulture), t1 = double.Parse(a[2], CultureInfo.InvariantCulture);
            var names = new[] { set + ".mix" }.Concat(MusicLib.StemNames(set)).ToArray();
            var ed = new List<double>(); for (double f = 31.5; f < 12000; f *= Math.Pow(2, 1.0 / 3)) ed.Add(f); var e = ed.ToArray();
            var tr = names.Select(n => MusicSpecVerbs.BandTraj(Dsp.Slice(MusicLib.Src(n), t0, t1), e, 4096, 2048)).ToArray();
            Console.WriteLine("band    " + string.Join(" ", names.Select(n => n.Substring(set.Length + 1).PadLeft(7))));
            for (int b = 0; b < e.Length - 1; b++)
            {
                var sb = new System.Text.StringBuilder($"{Math.Sqrt(e[b] * e[b + 1]),6:F0}  ");
                foreach (var t in tr) { double s = 0; foreach (var v in t[b]) s += v; s /= Math.Max(1, t[b].Length); sb.Append($" {10 * Math.Log10(s / 1e6 + 1e-12),7:F1}"); }
                Console.WriteLine(sb);
            }
            if (Program.Flag(a, "--ref"))
            {
                // smoothness of (mix - ref stem) across bands 70 Hz - 5.7 kHz after removing a cubic in log-frequency: a hidden extra source would leave humps
                string rn = a[Array.IndexOf(a, "--ref") + 1]; int ri = Array.FindIndex(names, n => n.EndsWith("." + rn, StringComparison.OrdinalIgnoreCase));
                var xs = new List<double>(); var ys = new List<double>();
                for (int b = 0; b < e.Length - 1; b++)
                {
                    double fc = Math.Sqrt(e[b] * e[b + 1]); if (fc < 70 || fc > 5700) continue;
                    double m = 0, s = 0; foreach (var v in tr[0][b]) m += v; foreach (var v in tr[ri][b]) s += v; m /= tr[0][b].Length; s /= tr[ri][b].Length;
                    xs.Add(Math.Log(fc / 1000.0)); ys.Add(10 * Math.Log10(m + 1e-12) - 10 * Math.Log10(s + 1e-12));
                }
                int nn = xs.Count, deg = 4; var G = new double[deg, deg]; var r = new double[deg];
                for (int i = 0; i < nn; i++) for (int p = 0; p < deg; p++) { r[p] += ys[i] * Math.Pow(xs[i], p); for (int q = 0; q < deg; q++) G[p, q] += Math.Pow(xs[i], p + q); }
                var c = MusicVerbs.Solve(G, r); double se = 0, mxd = 0; for (int i = 0; i < nn; i++) { double f = 0; for (int p = 0; p < deg; p++) f += c[p] * Math.Pow(xs[i], p); double d = ys[i] - f; se += d * d; mxd = Math.Max(mxd, Math.Abs(d)); }
                Console.WriteLine($"[ref {rn}] mix-minus-stem level difference over {nn} bands: mean {ys.Average():F1} dB, range {ys.Min():F1}..{ys.Max():F1} dB; RMS deviation from a smooth cubic EQ curve {Math.Sqrt(se / nn):F2} dB, max {mxd:F2} dB");
            }
            return 0;
        },
    };
}
