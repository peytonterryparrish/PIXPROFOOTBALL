using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 AUDIO+CROWD: last stage of the master output (lives on the AudioListener object). A sample-accurate brick-wall limiter at -1 dBFS (instant attack, 150 ms release) so a pile of
    /// hits, a roar and a stinger together can never clip, and a BS.1770 K-weighted loudness meter (momentary / short-term / integrated LUFS, true to the final mix) for the QA numbers.
    /// </summary>
    public sealed class MasterLimiter : MonoBehaviour
    {
        public static MasterLimiter Instance { get; private set; }
        public bool Enabled = true;
        public float CeilingDb = -1f;
        /// <summary>Make-up gain before the limiter. The buses are balanced against each other (crowd -29 LUFS, music -30, effects peaking -12 dBFS before it); +6 dB brings the whole mix to about -20 LUFS, peaks near the ceiling.</summary>
        public float MakeupDb = 6f;

        // ---- stats (written on the audio thread, read on the main thread; races are harmless for display)
        public volatile int LimitedSamples;
        public float PrePeakDb = -120f, PostPeakDb = -120f;
        public long Samples;

        float _ceil = 0.891f, _gain = 1f, _rel, _mk = 2f;
        int _rate = 48000;
        // K-weighting (two biquads per channel, up to 2 channels)
        double[] _a1 = new double[2], _a2 = new double[2];
        double[] _b0 = new double[2], _b1 = new double[2], _b2 = new double[2];
        readonly double[,] _z1 = new double[2, 2], _z2 = new double[2, 2];   // [stage, channel]
        double _blockSum; int _blockN, _blockLen;
        readonly object _lock = new object();
        readonly System.Collections.Generic.List<float> _hops = new System.Collections.Generic.List<float>(4096);   // 100 ms mean-square (summed over channels)
        float _prePeak, _postPeak;

        void Awake()
        {
            Instance = this;
            _rate = AudioSettings.outputSampleRate; if (_rate <= 0) _rate = 48000;
            _ceil = Mathf.Pow(10f, CeilingDb / 20f);
            _mk = Mathf.Pow(10f, MakeupDb / 20f);
            _rel = 1f - Mathf.Exp(-1f / (_rate * 0.15f));
            _blockLen = _rate / 10;
            // stage 1: high shelf, stage 2: RLB high-pass (ITU-R BS.1770-4)
            double f0 = 1681.974450955533, G = 3.999843853973347, Q = 0.7071752369554196;
            double K = System.Math.Tan(System.Math.PI * f0 / _rate), Vh = System.Math.Pow(10, G / 20), Vb = System.Math.Pow(Vh, 0.4996667741545416);
            double a0 = 1 + K / Q + K * K;
            _b0[0] = (Vh + Vb * K / Q + K * K) / a0; _b1[0] = 2 * (K * K - Vh) / a0; _b2[0] = (Vh - Vb * K / Q + K * K) / a0; _a1[0] = 2 * (K * K - 1) / a0; _a2[0] = (1 - K / Q + K * K) / a0;
            f0 = 38.13547087602444; Q = 0.5003270373238773; K = System.Math.Tan(System.Math.PI * f0 / _rate);
            double d = 1 + K / Q + K * K;
            _b0[1] = 1; _b1[1] = -2; _b2[1] = 1; _a1[1] = 2 * (K * K - 1) / d; _a2[1] = (1 - K / Q + K * K) / d;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnAudioFilterRead(float[] data, int channels) { Process(data, channels); }

        /// <summary>The limiter + meter on one interleaved buffer (the audio thread calls it; tests call it directly).</summary>
        public void Process(float[] data, int channels)
        {
            int frames = data.Length / channels;
            float gain = _gain, ceil = _ceil, rel = _rel, pre = _prePeak, post = _postPeak, mk = Enabled ? (float)System.Math.Pow(10.0, MakeupDb / 20.0) : 1f; int limited = 0;
            for (int i = 0; i < frames; i++)
            {
                int o = i * channels;
                for (int c = 0; c < channels; c++) data[o + c] *= mk;
                float pk = 0f;
                for (int c = 0; c < channels; c++) { float a = data[o + c]; if (a < 0f) a = -a; if (a > pk) pk = a; }
                if (pk > pre) pre = pk;
                if (Enabled)
                {
                    float need = pk * gain > ceil ? ceil / pk : 1f;
                    if (need < gain) { gain = need; limited++; } else gain += (1f - gain) * rel;
                    if (gain > 1f) gain = 1f;
                    for (int c = 0; c < channels; c++) data[o + c] *= gain;
                }
                // loudness: K-weight the post-limiter signal
                double sum = 0;
                for (int c = 0; c < channels && c < 2; c++)
                {
                    double x = data[o + c];
                    double y = _b0[0] * x + _z1[0, c]; _z1[0, c] = _b1[0] * x - _a1[0] * y + _z2[0, c]; _z2[0, c] = _b2[0] * x - _a2[0] * y;
                    double w = _b0[1] * y + _z1[1, c]; _z1[1, c] = _b1[1] * y - _a1[1] * w + _z2[1, c]; _z2[1, c] = _b2[1] * y - _a2[1] * w;
                    sum += w * w;
                    float ap = data[o + c]; if (ap < 0f) ap = -ap; if (ap > post) post = ap;
                }
                _blockSum += sum;
                if (++_blockN >= _blockLen)
                {
                    lock (_lock) { _hops.Add((float)(_blockSum / _blockN)); if (_hops.Count > 36000) _hops.RemoveRange(0, 6000); }
                    _blockSum = 0; _blockN = 0;
                }
            }
            _gain = gain; _prePeak = pre; _postPeak = post;
            Samples += frames;
            if (limited > 0) LimitedSamples += limited;
        }

        public void ResetStats()
        {
            lock (_lock) _hops.Clear();
            LimitedSamples = 0; _prePeak = _postPeak = 0f; Samples = 0;
        }

        static double Lufs(double ms) { return -0.691 + 10.0 * System.Math.Log10(System.Math.Max(ms, 1e-12)); }

        /// <summary>Peak levels (dBFS) of the mix before and after the limiter since the last reset.</summary>
        public void Peaks(out float preDb, out float postDb)
        {
            preDb = 20f * Mathf.Log10(Mathf.Max(_prePeak, 1e-6f)); postDb = 20f * Mathf.Log10(Mathf.Max(_postPeak, 1e-6f));
        }

        /// <summary>Momentary (last 400 ms), short-term (last 3 s) and gated integrated loudness of the final mix since the last reset.</summary>
        public void Loudness(out float momentary, out float shortTerm, out float integrated)
        {
            float[] h;
            lock (_lock) h = _hops.ToArray();
            momentary = shortTerm = integrated = -70f;
            if (h.Length < 4) return;
            momentary = (float)Lufs(Mean(h, h.Length - 4, 4));
            int n3 = System.Math.Min(30, h.Length); shortTerm = (float)Lufs(Mean(h, h.Length - n3, n3));
            // integrated: 400 ms blocks at 100 ms hop, absolute gate -70, relative gate -10
            int nb = h.Length - 3; var blocks = new double[nb];
            for (int i = 0; i < nb; i++) blocks[i] = Mean(h, i, 4);
            double s1 = 0; int c1 = 0;
            for (int i = 0; i < nb; i++) if (Lufs(blocks[i]) > -70) { s1 += blocks[i]; c1++; }
            if (c1 == 0) return;
            double rel = Lufs(s1 / c1) - 10, s2 = 0; int c2 = 0;
            for (int i = 0; i < nb; i++) if (Lufs(blocks[i]) > rel) { s2 += blocks[i]; c2++; }
            integrated = (float)Lufs(c2 > 0 ? s2 / c2 : s1 / c1);
        }

        static double Mean(float[] h, int from, int n) { double s = 0; for (int i = 0; i < n; i++) s += h[from + i]; return s / n; }
    }
}
