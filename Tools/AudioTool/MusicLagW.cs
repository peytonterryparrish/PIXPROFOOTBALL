namespace PixAudio;
public static class MusicLagWVerbs
{
    public static IDictionary<string, Func<string[], int>> Register() => new Dictionary<string, Func<string[], int>>
    {
        // mlagw set [--lo 100 --hi 2000] [--range 60] [--win 10]: per-window best lag (full rate) between the mix and the equal-gain stem sum
        ["mlagw"] = a =>
        {
            string set = a[0]; double lo = Program.Opt(a, "--lo", 100), hi = Program.Opt(a, "--hi", 2000); int range = (int)Program.Opt(a, "--range", 60); double win = Program.Opt(a, "--win", 10);
            var mix = MusicLib.Src(set + ".mix").Copy(); MusicLib.Filter(mix, true, lo); MusicLib.Filter(mix, false, hi); var mx = Dsp.Mono(mix).D[0];
            Clip sum = null; foreach (var s in MusicLib.StemNames(set)) { var c = MusicLib.Src(s).Copy(); MusicLib.Filter(c, true, lo); MusicLib.Filter(c, false, hi); if (sum == null) sum = c; else for (int ch = 0; ch < 2; ch++) for (int i = 0; i < Math.Min(sum.N, c.N); i++) sum.D[ch][i] += c.D[ch][i]; }
            var sx = Dsp.Mono(sum).D[0]; int W = (int)(win * 44100);
            for (int st = range; st + W + range < Math.Min(mx.Length, sx.Length); st += W)
            {
                double best = -2; int bl = 0; double ex = 0; for (int i = 0; i < W; i++) ex += (double)mx[st + i] * mx[st + i];
                for (int l = -range; l <= range; l++)
                {
                    double xy = 0, ey = 0; for (int i = 0; i < W; i++) { double y = sx[st + i + l]; xy += mx[st + i] * y; ey += y * y; }
                    double r = xy / Math.Sqrt(ex * ey + 1e-30); if (r > best) { best = r; bl = l; }
                }
                Console.WriteLine($"t={st / 44100.0,6:F1}s  lag={bl,4}  corr={best:F3}");
            }
            return 0;
        },
    };
}
