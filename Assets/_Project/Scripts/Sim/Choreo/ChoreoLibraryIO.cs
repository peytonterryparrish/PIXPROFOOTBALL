using System.Collections.Generic;
using System.IO;

namespace PixFootball.Sim
{
    /// <summary>A plain in-memory clip library (what <see cref="ChoreoLibraryIO.Read"/> returns): lets headless runs and tests use the measured Draft Fever data without the engine.</summary>
    public sealed class StaticChoreoLibrary : IChoreoLibrary
    {
        public readonly List<ChoreoPair> PairList = new List<ChoreoPair>();
        public readonly List<ChoreoClip> CatchList = new List<ChoreoClip>(), InterceptList = new List<ChoreoClip>();
        public IReadOnlyList<ChoreoPair> Pairs { get { return PairList; } }
        public IReadOnlyList<ChoreoClip> Catches { get { return CatchList; } }
        public IReadOnlyList<ChoreoClip> Intercepts { get { return InterceptList; } }
    }

    /// <summary>Binary dump / load of a clip library (the Unity layer writes it from the measured tables, headless calibration runs read it).</summary>
    public static class ChoreoLibraryIO
    {
        const int Magic = 0x43484f52;   // "CHOR"

        public static void Write(IChoreoLibrary lib, Stream s)
        {
            using (var w = new BinaryWriter(s, System.Text.Encoding.UTF8, true))
            {
                w.Write(Magic);
                w.Write(lib.Pairs.Count);
                foreach (var p in lib.Pairs)
                {
                    w.Write(p.Key ?? ""); w.Write((int)p.Kind); w.Write(p.ContactT); w.Write(p.Distance); w.Write(p.Bearing); w.Write(p.ClosingB); w.Write(p.SpeedA); w.Write(p.LowB);
                    WriteClip(w, p.A); WriteClip(w, p.B);
                }
                w.Write(lib.Catches.Count); foreach (var c in lib.Catches) WriteClip(w, c);
                w.Write(lib.Intercepts.Count); foreach (var c in lib.Intercepts) WriteClip(w, c);
            }
        }

        static void WriteClip(BinaryWriter w, ChoreoClip c)
        {
            w.Write(c.Name ?? ""); w.Write(c.Length); w.Write(c.Step); w.Write(c.ContactT); w.Write(c.SettleT);
            w.Write(c.HandX); w.Write(c.HandY); w.Write(c.HandZ); w.Write(c.MinHips); w.Write(c.SettleLead);
            w.Write(c.Path.Length); foreach (float f in c.Path) w.Write(f);
        }

        public static StaticChoreoLibrary Read(Stream s)
        {
            var lib = new StaticChoreoLibrary();
            using (var r = new BinaryReader(s, System.Text.Encoding.UTF8, true))
            {
                if (r.ReadInt32() != Magic) throw new InvalidDataException("not a choreo library");
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var p = new ChoreoPair { Key = r.ReadString(), Kind = (PairKind)r.ReadInt32(), ContactT = r.ReadSingle(), Distance = r.ReadSingle(), Bearing = r.ReadSingle(), ClosingB = r.ReadSingle(), SpeedA = r.ReadSingle(), LowB = r.ReadSingle() };
                    p.A = ReadClip(r); p.B = ReadClip(r);
                    lib.PairList.Add(p);
                }
                n = r.ReadInt32(); for (int i = 0; i < n; i++) lib.CatchList.Add(ReadClip(r));
                n = r.ReadInt32(); for (int i = 0; i < n; i++) lib.InterceptList.Add(ReadClip(r));
            }
            return lib;
        }

        static ChoreoClip ReadClip(BinaryReader r)
        {
            var c = new ChoreoClip { Name = r.ReadString(), Length = r.ReadSingle(), Step = r.ReadSingle(), ContactT = r.ReadSingle(), SettleT = r.ReadSingle(), HandX = r.ReadSingle(), HandY = r.ReadSingle(), HandZ = r.ReadSingle(), MinHips = r.ReadSingle(), SettleLead = r.ReadSingle() };
            int n = r.ReadInt32(); c.Path = new float[n]; for (int i = 0; i < n; i++) c.Path[i] = r.ReadSingle();
            return c;
        }
    }
}
