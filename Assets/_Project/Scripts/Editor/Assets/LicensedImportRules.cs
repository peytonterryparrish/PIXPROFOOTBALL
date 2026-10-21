using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Import rules for everything under Assets/_Licensed/DraftFever (raw copied files only: package-extracted assets
    /// keep their own .meta and are only size-capped). Textures: normal/mask/sRGB + max size; audio: SFX vs music;
    /// models: strip cameras/lights, no animation on static meshes.
    /// </summary>
    public class LicensedImportRules : AssetPostprocessor
    {
        public const string Root = "Assets/_Licensed/DraftFever";

        static bool InScope(string path) { return path.StartsWith(Root, StringComparison.OrdinalIgnoreCase); }

        // ---------------------------------------------------------------- textures
        void OnPreprocessTexture()
        {
            if (!InScope(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            string p = assetPath.Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();

            int cap = 2048;
            if (p.Contains("/Characters/Bodies/") || p.Contains("/Characters/MainPlayerDelivery/") || p.Contains("/Characters/SkinTextures/")
                || p.Contains("/Characters/Heads/") || p.Contains("/Characters/FacialRig/") || p.Contains("/Helmets/")
                || p.Contains("/Packages/MainPlayer/") || p.Contains("/Packages/PlayerMaterials/") || p.Contains("/Packages/HelmetShader/")
                || p.Contains("/Packages/Stadium_") || p.Contains("/Props/Field/") || p.Contains("/UI/Atlases/"))
                cap = 4096;

            if (!ti.importSettingsMissing)
            {   // package-extracted asset with its own meta: only clamp the size, never change type
                if (ti.maxTextureSize > cap) ti.maxTextureSize = cap;
                return;
            }

            ti.maxTextureSize = cap;
            ti.isReadable = false;
            ti.textureCompression = TextureImporterCompression.Compressed;

            if (p.Contains("/UI/") || p.Contains("/Logos/"))
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.alphaIsTransparency = true;
                ti.mipmapEnabled = false;
                ti.spritePixelsPerUnit = 100;
                ti.filterMode = FilterMode.Bilinear;
                return;
            }

            bool isNormal = name.Contains("normal") || name.EndsWith("_n") || name.Contains("_nrm") || name.Contains("normalmap");
            bool isMask = name.Contains("maskmap") || name.EndsWith("_mask") || name.EndsWith("_m") || name.Contains("metallic")
                          || name.Contains("roughness") || name.Contains("mixed_ao") || name.Contains("_ao") || name.Contains("rmt")
                          || name.Contains("height") || name.Contains("opacitymask") || name.Contains("colormask") || name.Contains("idmap")
                          || name.Contains("metalaodetailrough") || name.EndsWith("_r") || name.Contains("displacement");
            if (isNormal)
            {
                ti.textureType = TextureImporterType.NormalMap;
            }
            else if (isMask)
            {
                ti.sRGBTexture = false;
            }
            if (p.Contains("/Props/") && cap > 2048) cap = 2048;
        }

        // ---------------------------------------------------------------- audio
        void OnPreprocessAudio()
        {
            if (!InScope(assetPath)) return;
            var ai = (AudioImporter)assetImporter;
            if (!ai.importSettingsMissing) return;
            string p = assetPath.Replace('\\', '/');
            var s = ai.defaultSampleSettings;
            if (p.Contains("/Audio/Music/"))
            {
                s.loadType = AudioClipLoadType.Streaming;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
                s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                ai.forceToMono = false;
            }
            else if (p.Contains("/Audio/Ambience/"))
            {
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.5f;
            }
            else
            {   // SFX
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.ADPCM;
                s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            }
            s.preloadAudioData = !p.Contains("/Audio/Music/");
            ai.defaultSampleSettings = s;
        }

        // ---------------------------------------------------------------- models
        void OnPreprocessModel()
        {
            if (!InScope(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            string p = assetPath.Replace('\\', '/');

            if (p.Contains("/Animations/"))
            {
                if (mi.importSettingsMissing) AnimationImportSettings.ConfigureHumanoid(mi);
                return;
            }
            if (!mi.importSettingsMissing) return;
            mi.importCameras = false;
            mi.importLights = false;
            mi.isReadable = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importVisibility = true;
            bool animated = p.Contains("/Characters/Crowd/") || p.Contains("/Characters/Referee/") || p.Contains("/Characters/Coaches/");
            if (!animated) mi.importAnimation = false;
        }
    }
}
