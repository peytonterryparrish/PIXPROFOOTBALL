using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.5 Settings > Upscaler (Off / DLSS / FSR). HDRP 17.6 ships both as advanced upscalers on top of dynamic resolution: DLSS needs an NVIDIA RTX GPU and the NVIDIA module
    /// (com.unity.modules.nvidia), FSR 2 the AMD module (com.unity.modules.amd) and D3D12 / Vulkan. The option sets the pipeline's upscaler priority list and the camera permissions;
    /// when the chosen upscaler is not supported on this machine the option silently stays on the engine's own upscaler (Status says what happened). Applied on every scene load.
    /// </summary>
    public static class UpscalerControl
    {
        public static string Status = "off";
        static int _override = -1;
        static List<string> _originalNames;
        static bool _saved;

        public static bool DlssAvailable { get { try { return HDDynamicResolutionPlatformCapabilities.DLSSDetected; } catch (Exception) { return false; } } }
        public static bool Fsr2Available { get { try { return HDDynamicResolutionPlatformCapabilities.FSR2Detected; } catch (Exception) { return false; } } }
        public static int Mode { get { return _override >= 0 ? _override : PixSettings.Upscaler; } }

        /// <summary>Command line / test override (-pixUpscaler 0|1|2), not stored in the settings.</summary>
        public static void Override(int mode) { _override = Mathf.Clamp(mode, 0, 2); Apply(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded += (s, m) => Apply();
            Apply();
        }

        public static void Apply()
        {
            try
            {
                int mode = Mode;
                var asset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
                if (asset == null) { Status = "no HDRP asset"; return; }
                bool wantDlss = mode == 1 && DlssAvailable, wantFsr2 = mode == 2 && Fsr2Available;
                bool wantFsr1 = mode == 2 && !wantFsr2;
                var rps = asset.currentPlatformRenderPipelineSettings;
                var dr = rps.dynamicResolutionSettings;
                if (!_saved) { _saved = true; _originalNames = dr.advancedUpscalerNames != null ? new List<string>(dr.advancedUpscalerNames) : new List<string>(); }
                var names = new List<string>();
                if (wantDlss) names.Add("DLSS");
                else if (wantFsr2) names.Add("FSR2");
                else names.AddRange(_originalNames);
                bool changed = false;
                if (dr.advancedUpscalerNames == null || !SameList(dr.advancedUpscalerNames, names)) { dr.advancedUpscalerNames = names; changed = true; }
                var wantFilter = wantFsr1 ? DynamicResUpscaleFilter.EdgeAdaptiveScalingUpres : DynamicResUpscaleFilter.ContrastAdaptiveSharpen;
                if (mode == 0 || mode == 1 || wantFsr1) { if (dr.upsampleFilter != wantFilter) { dr.upsampleFilter = wantFilter; changed = true; } }
                if (wantDlss) { if (dr.DLSSPerfQualitySetting != 1u || !dr.DLSSUseOptimalSettings) { dr.DLSSPerfQualitySetting = 1u; dr.DLSSUseOptimalSettings = true; changed = true; } }
                if (wantFsr2) { if (dr.FSR2QualitySetting != 1u || !dr.FSR2UseOptimalSettings) { dr.FSR2QualitySetting = 1u; dr.FSR2UseOptimalSettings = true; changed = true; } }   // balanced, resolution picked by the upscaler
                if (wantDlss || wantFsr2) { if (!dr.enabled) { dr.enabled = true; changed = true; } }
                if (changed && !Application.isEditor) { rps.dynamicResolutionSettings = dr; asset.currentPlatformRenderPipelineSettings = rps; }
                foreach (var cam in UnityEngine.Object.FindObjectsByType<HDAdditionalCameraData>(FindObjectsSortMode.None))
                {
                    cam.allowDeepLearningSuperSampling = wantDlss;
                    cam.allowFidelityFX2SuperResolution = wantFsr2;
                    cam.allowDynamicResolution = wantDlss || wantFsr2 || wantFsr1 || PixSettings.RenderScale < 0.999f;
                }
                Status = mode == 0 ? "off" : (wantDlss ? "DLSS active (balanced)" : wantFsr2 ? "FSR 2 active" : wantFsr1 ? "FSR 1 (edge adaptive scaling) - " + (mode == 2 ? "FSR 2 not supported here" : "") : "DLSS not supported on this GPU - using the engine upscaler");
            }
            catch (Exception e) { Status = "upscaler setup failed: " + e.Message; Debug.LogWarning("[PIX] " + Status); }
        }

        static bool SameList(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>One line for the logs: what the machine supports and what is active.</summary>
        public static string Report()
        {
            return "upscaler mode " + Mode + " (" + PixSettings.UpscalerNames[Mathf.Clamp(Mode, 0, 2)] + "), dlss detected=" + DlssAvailable + ", fsr2 detected=" + Fsr2Available + ", status: " + Status;
        }
    }
}
