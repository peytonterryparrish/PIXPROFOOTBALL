using System.Text;

namespace PixAudio;

/// <summary>Planar float audio, -1..1.</summary>
public sealed class Clip
{
    public int Rate; public float[][] D;
    public int Ch => D.Length;
    public int N => D.Length == 0 ? 0 : D[0].Length;
    public double Dur => Rate == 0 ? 0 : N / (double)Rate;
    public Clip(int rate, int ch, int n) { Rate = rate; D = new float[ch][]; for (int c = 0; c < ch; c++) D[c] = new float[n]; }
    public Clip Copy() { var o = new Clip(Rate, Ch, N); for (int c = 0; c < Ch; c++) Array.Copy(D[c], o.D[c], N); return o; }
}

public static class Wav
{
    public static Clip Read(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        if (b.Length < 12 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF") throw new Exception("not RIFF: " + path);
        int pos = 12, fmtTag = 1, ch = 2, rate = 44100, bits = 16, dataPos = -1, dataLen = 0;
        while (pos + 8 <= b.Length)
        {
            string id = Encoding.ASCII.GetString(b, pos, 4);
            uint sz = BitConverter.ToUInt32(b, pos + 4);
            int body = pos + 8;
            if (id == "fmt ")
            {
                fmtTag = BitConverter.ToUInt16(b, body); ch = BitConverter.ToUInt16(b, body + 2);
                rate = (int)BitConverter.ToUInt32(b, body + 4); bits = BitConverter.ToUInt16(b, body + 14);
                if (fmtTag == 0xFFFE && sz >= 26) fmtTag = BitConverter.ToUInt16(b, body + 24);
            }
            else if (id == "data")
            {
                dataPos = body; long len = sz; if (len > b.Length - body || sz == 0xFFFFFFFF) len = b.Length - body; dataLen = (int)len;
                break;
            }
            long next = (long)body + sz + (sz & 1);
            if (next > b.Length) break;
            pos = (int)next;
        }
        if (dataPos < 0) throw new Exception("no data chunk: " + path);
        int bytes = bits / 8, frames = dataLen / (bytes * ch);
        var clip = new Clip(rate, ch, frames);
        int p = dataPos;
        for (int i = 0; i < frames; i++)
            for (int c = 0; c < ch; c++)
            {
                float v;
                if (fmtTag == 3) v = bits == 32 ? BitConverter.ToSingle(b, p) : (float)BitConverter.ToDouble(b, p);
                else if (bits == 16) v = BitConverter.ToInt16(b, p) / 32768f;
                else if (bits == 24) { int s = (b[p] | (b[p + 1] << 8) | (b[p + 2] << 16)); if ((s & 0x800000) != 0) s |= unchecked((int)0xFF000000); v = s / 8388608f; }
                else if (bits == 32) v = BitConverter.ToInt32(b, p) / 2147483648f;
                else if (bits == 8) v = (b[p] - 128) / 128f;
                else throw new Exception("unsupported bits " + bits);
                clip.D[c][i] = v; p += bytes;
            }
        return clip;
    }

    public static void Write(string path, Clip a, int bits = 16)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        int bytes = bits / 8, n = a.N, ch = a.Ch;
        using var fs = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(fs);
        int dataLen = n * ch * bytes;
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + dataLen); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)(bits == 32 ? 3 : 1)); w.Write((short)ch); w.Write(a.Rate); w.Write(a.Rate * ch * bytes); w.Write((short)(ch * bytes)); w.Write((short)bits);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(dataLen);
        var buf = new byte[dataLen]; int p = 0;
        var rnd = new Random(1);
        for (int i = 0; i < n; i++)
            for (int c = 0; c < ch; c++)
            {
                float v = a.D[c][i];
                if (bits == 32) { BitConverter.TryWriteBytes(new Span<byte>(buf, p, 4), v); p += 4; continue; }
                v = Math.Clamp(v, -1f, 1f);
                if (bits == 16)
                {
                    // TPDF dither at 16 bit
                    double d = (rnd.NextDouble() - rnd.NextDouble()) / 32768.0;
                    int s = (int)Math.Round(Math.Clamp(v + d, -1.0, 1.0) * 32767.0);
                    buf[p++] = (byte)(s & 255); buf[p++] = (byte)((s >> 8) & 255);
                }
                else
                {
                    int s = (int)Math.Round(v * 8388607.0);
                    buf[p++] = (byte)(s & 255); buf[p++] = (byte)((s >> 8) & 255); buf[p++] = (byte)((s >> 16) & 255);
                }
            }
        w.Write(buf);
    }
}
