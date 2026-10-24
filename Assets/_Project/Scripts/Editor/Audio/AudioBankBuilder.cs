using System.Collections.Generic;
using System.IO;
using System.Linq;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11.0 AUDIO+CROWD: scans Assets/_Licensed/PIX/Audio (Music, Stingers, Sfx/*, Crowd/*) plus the Draft Fever rain / wind loops, sets sane import settings per kind
    /// and writes Assets/_Licensed/Resources/PixAudioBank.asset (groups and their dB trims). Menu: PIX Football/Audio/Build Audio Bank. Batch: PixFootball.EditorTools.AudioBankBuilder.Build.
    /// The clips themselves are made offline by Tools/AudioTool (see Docs/QA/v0110/audio.md).
    /// </summary>
    public static class AudioBankBuilder
    {
        public const string Root = "Assets/_Licensed/PIX/Audio";
        const string Ambience = "Assets/_Licensed/DraftFever/Audio/Ambience";

        /// <summary>Per-group trim in dB (clips are peak -3 dBFS for SFX and at their LUFS target for crowd / music). Broadcast mix: crowd bed -26 LUFS, hits well over it.</summary>
        static readonly Dictionary<string, float> Trim = new Dictionary<string, float>
        {
            // contact
            { "hit_light", -7 }, { "hit_medium", -4 }, { "hit_heavy", -2 }, { "hit_big", 0 }, { "block_thud", -4 }, { "helmet_clack", -6 }, { "shed_slap", -8 },
            { "grunt_hit", -7 }, { "grunt_effort", -10 }, { "pile_scrum", -3 }, { "ground_thump", -5 },
            // ball
            { "snap_slap", -5 }, { "handoff_slap", -8 }, { "throw_whoosh", -12 }, { "ball_whirr", -16 }, { "catch_soft", -10 }, { "catch_hard", -6 }, { "catch_contested", -3 },
            { "drop_thud", -8 }, { "bounce_dry", -12 }, { "bounce_wet", -12 }, { "bounce_snow", -12 }, { "fumble_pop", -6 },
            // kicks
            { "kick_thump", -1 }, { "kick_punt", -1 }, { "kick_short", -5 },
            // feet (a touch hotter than the sub-track suggested: 22 bodies at broadcast distance still have to be heard near the camera)
            { "step_run_dry", -17 }, { "step_run_wet", -17 }, { "step_run_snow", -17 }, { "step_walk_dry", -23 }, { "step_walk_wet", -23 }, { "step_walk_snow", -23 },
            { "plant_cut", -14 }, { "slide_turf", -14 },
            // ref / field
            { "whistle_short", -8 }, { "whistle_long", -9 }, { "chain_clank", -14 }, { "flag_toss", -16 },
            // weather loops (Draft Fever): sit under the crowd
            { "amb_rain", -14 }, { "amb_wind", -16 },
        };

        [MenuItem("PIX Football/Audio/Build Audio Bank")]
        public static void Build()
        {
            var bank = AssetDatabase.LoadAssetAtPath<PixAudioBank>(PixAudioBank.EditorPath);
            if (bank == null)
            {
                Directory.CreateDirectory(Path.GetFullPath("Assets/_Licensed/Resources"));
                bank = ScriptableObject.CreateInstance<PixAudioBank>();
                AssetDatabase.CreateAsset(bank, PixAudioBank.EditorPath);
            }
            bank.groups.Clear();
            int clips = 0, imported = 0;
            if (Directory.Exists(Path.GetFullPath(Root + "/Music"))) clips += AddFolder(bank, "music", Root + "/Music", Kind.Music, ref imported);
            if (Directory.Exists(Path.GetFullPath(Root + "/Stingers"))) clips += AddFolder(bank, "stinger", Root + "/Stingers", Kind.Stinger, ref imported);
            foreach (string top in new[] { "Sfx", "Crowd" })
            {
                string dir = Root + "/" + top;
                if (!Directory.Exists(Path.GetFullPath(dir))) continue;
                foreach (string sub in Directory.GetDirectories(Path.GetFullPath(dir)).OrderBy(x => x))
                {
                    string key = Path.GetFileName(sub);
                    clips += AddFolder(bank, key, dir + "/" + key, top == "Sfx" ? Kind.Sfx : Kind.Crowd, ref imported);
                }
            }
            clips += AddFolder(bank, "amb_rain", Ambience + "/Rain", Kind.Weather, ref imported);
            clips += AddFolder(bank, "amb_wind", Ambience + "/Winds", Kind.Weather, ref imported);
            CrowdBuilder.BakeFanData();      // the GPU crowd animation builds its meshes from this baked per-fan data
            EditorUtility.SetDirty(bank);
            AssetDatabase.SaveAssets();
            PixAudioBank.ResetCache();
            Debug.Log("[PIX] audio bank: " + bank.groups.Count + " groups, " + clips + " clips (" + imported + " re-imported) at " + PixAudioBank.EditorPath);
        }

        enum Kind { Music, Stinger, Sfx, Crowd, Weather }

        static int AddFolder(PixAudioBank bank, string key, string folder, Kind kind, ref int imported)
        {
            if (!Directory.Exists(Path.GetFullPath(folder))) return 0;
            var g = new PixAudioBank.Group { key = key };
            float trim; if (Trim.TryGetValue(key, out trim)) g.gainDb = trim;
            var files = Directory.GetFiles(Path.GetFullPath(folder), "*.wav").OrderBy(x => x).ToArray();
            foreach (string f in files)
            {
                string path = folder + "/" + Path.GetFileName(f);
                if (Configure(path, kind)) imported++;
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (c != null) g.clips.Add(c); else Debug.LogWarning("[PIX] audio bank: " + path + " did not import");
            }
            if (g.clips.Count > 0) bank.groups.Add(g);
            return g.clips.Count;
        }

        /// <summary>Applies the per-kind import settings; true when the file was re-imported.</summary>
        static bool Configure(string path, Kind kind)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.Default);
            var ai = AssetImporter.GetAtPath(path) as AudioImporter;
            if (ai == null) return false;
            var s = ai.defaultSampleSettings;
            var before = s;
            bool mono = false;
            switch (kind)
            {
                case Kind.Music: s.loadType = AudioClipLoadType.Streaming; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.62f; s.preloadAudioData = false; break;
                case Kind.Stinger: s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.75f; s.preloadAudioData = true; break;
                case Kind.Sfx: s.loadType = AudioClipLoadType.CompressedInMemory; s.compressionFormat = AudioCompressionFormat.ADPCM; s.preloadAudioData = true; mono = true; break;
                case Kind.Crowd: s.loadType = AudioClipLoadType.CompressedInMemory; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.55f; s.preloadAudioData = true; break;
                case Kind.Weather: s.loadType = AudioClipLoadType.CompressedInMemory; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.45f; s.preloadAudioData = true; break;
            }
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            bool same = before.loadType == s.loadType && before.compressionFormat == s.compressionFormat && Mathf.Approximately(before.quality, s.quality) && before.preloadAudioData == s.preloadAudioData && ai.forceToMono == mono && before.sampleRateSetting == s.sampleRateSetting;
            if (same) return false;
            ai.defaultSampleSettings = s; ai.forceToMono = mono; ai.loadInBackground = kind == Kind.Music;
            ai.SaveAndReimport();
            return true;
        }
    }
}
