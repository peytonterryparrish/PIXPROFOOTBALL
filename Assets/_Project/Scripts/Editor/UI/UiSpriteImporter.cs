using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Import rules for the curated Draft Fever Bowl UI art under <c>Assets/_Licensed/UI/</c> (DFB components, Screens, Logos).
    /// Every texture becomes a single-mode Sprite (no mipmaps, bilinear, clamped). Small sprites stay uncompressed (crisp
    /// glyphs/icons); larger ones use BC7 on Standalone. 9-slice borders are read from a <c>slicing.json</c> that sits next to
    /// the PNGs (see <see cref="SlicingFile"/>): <c>{"defaultPpu":200,"borders":[{"file":"btn_x.png","border":[left,bottom,right,top],"ppu":400}]}</c>
    /// with borders in source pixels (Unity's Vector4 order) and pixels-per-unit = 100 x source scale (so 2x art has 200 and
    /// "Set Native Size" gives the 1920x1080 design size). Editing a slicing.json re-imports its sibling textures.
    /// Editor-only; lives in the PixFootball.Editor assembly.
    /// </summary>
    public class UiSpriteImporter : AssetPostprocessor
    {
        public const string Root = "Assets/_Licensed/UI";
        public const string SlicingFileName = "slicing.json";

        /// <summary>Longest side (px) up to which a sprite stays uncompressed.</summary>
        public const int UncompressedMaxSide = 512;
        public const int DefaultMaxSize = 4096;
        public const int ScreenRefMaxSize = 2048;

        // ---- slicing.json model (JsonUtility friendly) ----
        [Serializable]
        public class BorderEntry
        {
            public string file;
            /// <summary>[left, bottom, right, top] in source pixels (optional; omit or leave empty for no 9-slice).</summary>
            public int[] border;
            /// <summary>Optional per-file pixels-per-unit override (0 = use the folder default).</summary>
            public float ppu;
        }

        [Serializable]
        public class SlicingFile
        {
            public int version = 1;
            public string note;
            /// <summary>Folder default pixels-per-unit (100 x source scale: 2x art => 200). 0 = 100.</summary>
            public float defaultPpu;
            public List<BorderEntry> borders = new List<BorderEntry>();
        }

        struct SpriteMeta
        {
            public bool HasBorder;
            public Vector4 Border;   // x=left, y=bottom, z=right, w=top
            public float Ppu;        // pixels per unit (resolved: per-file, else folder default, else 100)
        }

        sealed class CacheEntry
        {
            public DateTime StampUtc;
            public Dictionary<string, SpriteMeta> Map;
            public float DefaultPpu;
        }

        static readonly Dictionary<string, CacheEntry> s_Cache = new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        // Bump when the rules below change so Unity re-imports existing assets.
        public override uint GetVersion() { return 2; }

        static bool InScope(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            if (!assetPath.StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase)) return false;
            string ext = Path.GetExtension(assetPath).ToLowerInvariant();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga";
        }

        // ------------------------------------------------------------------ texture import
        void OnPreprocessTexture()
        {
            string path = assetPath.Replace('\\', '/');
            if (!InScope(path)) return;

            var ti = (TextureImporter)assetImporter;

            int w = 0, h = 0;
            TryReadImageSize(path, out w, out h);
            int longest = Math.Max(w, h);

            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            // College/conference/bowl/pro team logos are pixel art: keep them crisp.
            bool pixelArt = path.Contains("/Logos/CollegeV2/") || path.Contains("/Logos/ConferenceBowl/") || path.Contains("/Logos/ProV6/");
            ti.filterMode = pixelArt ? FilterMode.Point : FilterMode.Bilinear;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.sRGBTexture = true;
            ti.isReadable = false;
            ti.npotScale = TextureImporterNPOTScale.None;

            bool isScreenRef = path.IndexOf("/Screens/", StringComparison.OrdinalIgnoreCase) >= 0;
            int maxSize = isScreenRef ? ScreenRefMaxSize : DefaultMaxSize;
            ti.maxTextureSize = maxSize;

            // Full-rect meshes so 9-sliced sprites never get clipped by a tight mesh.
            var settings = new TextureImporterSettings();
            ti.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.Center;
            settings.spritePivot = new Vector2(0.5f, 0.5f);
            ti.SetTextureSettings(settings);

            // Compression: crisp & uncompressed for small sprites, BC7 (HQ) for big art.
            var standalone = ti.GetPlatformTextureSettings("Standalone");
            if (longest > 0 && longest <= UncompressedMaxSide)
            {
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                standalone.overridden = false;
            }
            else
            {
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                standalone.overridden = true;
                standalone.maxTextureSize = maxSize;
                standalone.format = TextureImporterFormat.BC7;
                standalone.textureCompression = TextureImporterCompression.CompressedHQ;
            }
            ti.SetPlatformTextureSettings(standalone);

            // pixels-per-unit and 9-slice border from the sibling slicing.json
            SpriteMeta meta;
            if (TryGetMeta(path, out meta))
            {
                ti.spritePixelsPerUnit = meta.Ppu > 0f ? meta.Ppu : 100f;
                if (meta.HasBorder) ti.spriteBorder = meta.Border;
            }
            else
            {
                ti.spritePixelsPerUnit = 100f;
            }
        }

        // ------------------------------------------------------------------ slicing.json
        static bool TryGetMeta(string assetPath, out SpriteMeta meta)
        {
            meta = default(SpriteMeta);
            string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string jsonPath = dir + "/" + SlicingFileName;
            Dictionary<string, SpriteMeta> map = LoadSlicing(jsonPath);
            if (map != null && map.TryGetValue(Path.GetFileName(assetPath), out meta)) return true;
            // no explicit entry: still honour the folder default ppu
            float def = LoadDefaultPpu(jsonPath);
            if (def > 0f) { meta = new SpriteMeta { Ppu = def }; return true; }
            return false;
        }

        static Dictionary<string, SpriteMeta> LoadSlicing(string jsonAssetPath)
        {
            string full = Path.GetFullPath(jsonAssetPath);
            if (!File.Exists(full)) { s_Cache.Remove(jsonAssetPath); return null; }

            DateTime stamp = File.GetLastWriteTimeUtc(full);
            CacheEntry entry;
            if (s_Cache.TryGetValue(jsonAssetPath, out entry) && entry.StampUtc == stamp)
                return entry.Map;

            var map = new Dictionary<string, SpriteMeta>(StringComparer.OrdinalIgnoreCase);
            float defPpu = 0f;
            try
            {
                var file = JsonUtility.FromJson<SlicingFile>(File.ReadAllText(full));
                if (file != null)
                {
                    defPpu = file.defaultPpu;
                    if (file.borders != null)
                    {
                        foreach (var b in file.borders)
                        {
                            if (b == null || string.IsNullOrEmpty(b.file)) continue;
                            var m = new SpriteMeta { Ppu = b.ppu > 0f ? b.ppu : file.defaultPpu };
                            if (b.border != null && b.border.Length >= 4)
                            {
                                // Unity Sprite border order: x = left, y = bottom, z = right, w = top
                                m.HasBorder = true;
                                m.Border = new Vector4(b.border[0], b.border[1], b.border[2], b.border[3]);
                            }
                            map[b.file] = m;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[UiSpriteImporter] Could not parse " + jsonAssetPath + ": " + e.Message);
            }
            s_Cache[jsonAssetPath] = new CacheEntry { StampUtc = stamp, Map = map, DefaultPpu = defPpu };
            return map;
        }

        static float LoadDefaultPpu(string jsonAssetPath)
        {
            LoadSlicing(jsonAssetPath);
            CacheEntry entry;
            return s_Cache.TryGetValue(jsonAssetPath, out entry) ? entry.DefaultPpu : 0f;
        }

        // Re-import sibling textures when a slicing.json is added or edited.
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // Folders whose textures were imported in this same batch already read slicing.json straight from disk,
            // so only folders where the json alone changed need a follow-up re-import.
            var importedPngDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string p in imported)
            {
                string n0 = p.Replace('\\', '/');
                if (n0.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && n0.StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase))
                    importedPngDirs.Add(Path.GetDirectoryName(n0).Replace('\\', '/'));
            }

            List<string> dirs = null;
            foreach (string p in imported)
            {
                string norm = p.Replace('\\', '/');
                if (!norm.StartsWith(Root + "/", StringComparison.OrdinalIgnoreCase)) continue;
                if (!norm.EndsWith("/" + SlicingFileName, StringComparison.OrdinalIgnoreCase)) continue;
                s_Cache.Remove(norm);
                string jsonDir = Path.GetDirectoryName(norm).Replace('\\', '/');
                if (importedPngDirs.Contains(jsonDir)) continue;
                if (dirs == null) dirs = new List<string>();
                dirs.Add(jsonDir);
            }
            if (dirs == null || dirs.Count == 0) return;

            EditorApplication.delayCall += () =>
            {
                foreach (string dir in dirs)
                {
                    if (!Directory.Exists(Path.GetFullPath(dir))) continue;
                    foreach (string png in Directory.GetFiles(Path.GetFullPath(dir), "*.png", SearchOption.TopDirectoryOnly))
                    {
                        string rel = ToAssetPath(png);
                        if (rel != null) AssetDatabase.ImportAsset(rel, ImportAssetOptions.ForceUpdate);
                    }
                }
            };
        }

        static string ToAssetPath(string fullPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .Replace('\\', '/').TrimEnd('/') + "/";
            string norm = fullPath.Replace('\\', '/');
            if (!norm.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)) return null;
            return norm.Substring(projectRoot.Length);
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Reads width/height from a PNG IHDR chunk without decoding (works inside OnPreprocessTexture).</summary>
        static bool TryReadImageSize(string assetPath, out int width, out int height)
        {
            width = height = 0;
            try
            {
                string full = Path.GetFullPath(assetPath);
                if (!File.Exists(full)) return false;
                using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var head = new byte[24];
                    if (fs.Read(head, 0, 24) < 24) return false;
                    // PNG signature 89 50 4E 47, IHDR width/height are big-endian at offsets 16 and 20.
                    if (head[0] != 0x89 || head[1] != 0x50 || head[2] != 0x4E || head[3] != 0x47) return false;
                    width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
                    height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
                    return width > 0 && height > 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        [MenuItem("PIX Football/UI/Reimport UI Sprites")]
        static void ReimportAll()
        {
            s_Cache.Clear();
            AssetDatabase.ImportAsset(Root, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("[UiSpriteImporter] Re-imported " + Root);
        }
    }
}
