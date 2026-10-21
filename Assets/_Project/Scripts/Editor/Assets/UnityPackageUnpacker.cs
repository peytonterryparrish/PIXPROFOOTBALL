// Pure System.IO unpacker for .unitypackage (tar.gz) files. No UnityEngine dependency so it can also be
// loaded from PowerShell (Add-Type -Path ...) outside the editor. C# 5 syntax only.
// Preserves GUIDs/.meta, skips scripts, and de-duplicates by GUID across packages.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PixFootball.EditorTools
{
    public static class UnityPackageUnpacker
    {
        static readonly string[] SkipExt = { ".cs", ".asmdef", ".asmref", ".dll", ".pdb", ".mdb", ".rsp", ".unity", ".lighting", ".renderTexture" };

        static string ReadStr(byte[] b, int off, int len)
        {
            int end = off;
            while (end < off + len && b[end] != 0) end++;
            return Encoding.UTF8.GetString(b, off, end - off);
        }

        static long ReadOct(byte[] b, int off, int len)
        {
            if ((b[off] & 0x80) != 0)
            {
                long v = b[off] & 0x7f;
                for (int i = 1; i < len; i++) v = (v << 8) | b[off + i];
                return v;
            }
            string s = ReadStr(b, off, len).Trim();
            if (s.Length == 0) return 0;
            return Convert.ToInt64(s, 8);
        }

        delegate bool EntryHandler(string name, long size, Stream data);

        static void Walk(string pkg, EntryHandler handler)
        {
            using (FileStream fs = File.OpenRead(pkg))
            using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
            {
                byte[] hdr = new byte[512];
                string longName = null;
                while (true)
                {
                    if (!ReadFull(gz, hdr, 512)) break;
                    bool allZero = true;
                    for (int i = 0; i < 512; i++) if (hdr[i] != 0) { allZero = false; break; }
                    if (allZero) break;
                    string name = ReadStr(hdr, 0, 100);
                    string prefix = ReadStr(hdr, 345, 155);
                    if (prefix.Length > 0 && hdr[257] == (byte)'u') name = prefix + "/" + name;
                    long size = ReadOct(hdr, 124, 12);
                    char type = (char)hdr[156];
                    if (type == 'L')
                    {
                        byte[] nb = new byte[size];
                        ReadFull(gz, nb, (int)size);
                        longName = ReadStr(nb, 0, (int)size);
                        Skip(gz, Pad(size));
                        continue;
                    }
                    if (longName != null) { name = longName; longName = null; }
                    if (type == 'x' || type == 'g') { Skip(gz, size + Pad(size)); continue; }
                    long pad = Pad(size);
                    BoundedStream bs = new BoundedStream(gz, size);
                    if (type == '0' || type == (char)0)
                    {
                        handler(name, size, bs);
                    }
                    bs.Drain();
                    Skip(gz, pad);
                }
            }
        }

        static long Pad(long size) { return (512 - (size % 512)) % 512; }

        static bool ReadFull(Stream s, byte[] buf, int count)
        {
            int got = 0;
            while (got < count)
            {
                int n = s.Read(buf, got, count - got);
                if (n <= 0) return false;
                got += n;
            }
            return true;
        }

        static void Skip(Stream s, long n)
        {
            byte[] buf = new byte[81920];
            while (n > 0)
            {
                int r = s.Read(buf, 0, (int)Math.Min(buf.Length, n));
                if (r <= 0) break;
                n -= r;
            }
        }

        class BoundedStream : Stream
        {
            readonly Stream inner; long remaining;
            public BoundedStream(Stream s, long len) { inner = s; remaining = len; }
            public void Drain() { Skip(inner, remaining); remaining = 0; }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (remaining <= 0) return 0;
                int n = inner.Read(buffer, offset, (int)Math.Min(count, remaining));
                remaining -= n; return n;
            }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin so) { throw new NotSupportedException(); }
            public override void SetLength(long v) { throw new NotSupportedException(); }
            public override void Write(byte[] b, int o, int c) { throw new NotSupportedException(); }
        }

        /// <summary>Maps guid -> original asset path (pass 1).</summary>
        public static Dictionary<string, string> ReadManifest(string pkg)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            Walk(pkg, delegate(string name, long size, Stream data)
            {
                if (name.EndsWith("/pathname"))
                {
                    string guid = Norm(name);
                    guid = guid.Substring(0, guid.IndexOf('/'));
                    using (StreamReader r = new StreamReader(data, Encoding.UTF8))
                    {
                        string p = r.ReadLine();
                        if (p != null) map[guid] = p.Trim();
                    }
                }
                return true;
            });
            return map;
        }

        static string Norm(string name)
        {
            if (name.StartsWith("./")) name = name.Substring(2);
            return name;
        }

        /// <summary>
        /// Extracts into projectAssetsRoot ("&lt;proj&gt;/Assets") under destSubFolder.
        /// Original "Assets/" prefix is stripped. seenGuids is shared across calls to de-duplicate.
        /// Returns number of assets written.
        /// </summary>
        public static int Extract(string pkg, string projectAssetsRoot, string destSubFolder, HashSet<string> seenGuids, List<string> log)
        {
            Dictionary<string, string> manifest = ReadManifest(pkg);
            Dictionary<string, string> dest = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> kv in manifest)
            {
                string p = kv.Value.Replace('\\', '/');
                string lower = p.ToLowerInvariant();
                bool skip = false;
                for (int i = 0; i < SkipExt.Length; i++) if (lower.EndsWith(SkipExt[i])) { skip = true; break; }
                if (skip) { if (log != null) log.Add("SKIP-SCRIPT " + p); continue; }
                if (seenGuids.Contains(kv.Key)) { if (log != null) log.Add("SKIP-DUP " + p); continue; }
                string rel = p;
                if (rel.StartsWith("Assets/")) rel = rel.Substring(7);
                else if (rel.StartsWith("Packages/")) { if (log != null) log.Add("SKIP-PACKAGES " + p); continue; }
                dest[kv.Key] = destSubFolder + "/" + rel;
            }
            int written = 0;
            Walk(pkg, delegate(string rawName, long size, Stream data)
            {
                string name = Norm(rawName);
                int slash = name.IndexOf('/');
                if (slash < 0) return true;
                string guid = name.Substring(0, slash);
                string kind = name.Substring(slash + 1);
                string d;
                if (!dest.TryGetValue(guid, out d)) return true;
                string full;
                if (kind == "asset") full = Path.Combine(projectAssetsRoot, d.Replace('/', Path.DirectorySeparatorChar));
                else if (kind == "asset.meta") full = Path.Combine(projectAssetsRoot, (d + ".meta").Replace('/', Path.DirectorySeparatorChar));
                else return true;
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                using (FileStream o = File.Create(full))
                {
                    byte[] buf = new byte[1 << 20];
                    int n;
                    while ((n = data.Read(buf, 0, buf.Length)) > 0) o.Write(buf, 0, n);
                }
                if (kind == "asset") written++;
                return true;
            });
            foreach (KeyValuePair<string, string> kv in dest)
            {
                string full = Path.Combine(projectAssetsRoot, kv.Value.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) Directory.CreateDirectory(full);
                seenGuids.Add(kv.Key);
            }
            return written;
        }
    }
}
