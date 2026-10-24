using System.Globalization;
namespace PixAudio;
public static class MusicSimVerbs
{
    public static IDictionary<string, Func<string[], int>> Register() => new Dictionary<string, Func<string[], int>>
    {
        // msim set stemSpec [--minbars 2] : for every bar, the later bar (>= minbars away) with the highest waveform correlation at exact bar offset (copy-paste detector)
        ["msim"] = a =>
        {
            string set = a[0]; var sig = MusicLoopVerbs.Layered(set, a[1]); var (bpm, t0) = MusicLoopVerbs.Grids[set]; int minb = (int)Program.Opt(a, "--minbars", 2);
            double barS = 240.0 / bpm; var m = Dsp.Mono(sig).D[0]; int rate = sig.Rate; int nb = (int)((sig.Dur - t0) / barS);
            int B = (int)Math.Round(barS * rate);
            for (int i = 0; i < nb; i++)
            {
                int s0 = (int)Math.Round((t0 + i * barS) * rate); double e0 = 0; for (int k = 0; k < B; k++) e0 += (double)m[s0 + k] * m[s0 + k];
                if (10 * Math.Log10(e0 / B + 1e-12) < -50) continue;
                double best = -1; int bj = -1;
                for (int j = i + minb; j < nb; j++)
                {
                    int s1 = (int)Math.Round((t0 + j * barS) * rate); if (s1 + B > m.Length) break; double xy = 0, e1 = 0; for (int k = 0; k < B; k++) { xy += (double)m[s0 + k] * m[s1 + k]; e1 += (double)m[s1 + k] * m[s1 + k]; }
                    double r = xy / Math.Sqrt(e0 * e1 + 1e-30); if (r > best) { best = r; bj = j; }
                }
                Console.WriteLine($"bar {i,3} ({t0 + i * barS,6:F1}s) best match bar {bj,3} (+{bj - i}) corr={best:F3}");
            }
            return 0;
        },
    };
}
