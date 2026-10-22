using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.5 Settings > Upscaler (Off / DLSS / FSR), v0.9: Auto (the default). HDRP 17.6 ships both as advanced upscalers on top of dynamic resolution: DLSS needs an NVIDIA RTX GPU and the NVIDIA module
    /// (com.unity.modules.nvidia), FSR 2 the AMD module (com.unity.modules.amd) and D3D12 / Vulkan. The option sets the pipeline upscaler priority list and the camera permissions;
    /// when the chosen upscaler is not supported on this machine the option silently stays on the engine's own upscaler (Status says what happened). Applied on every scene load.
    /// Auto picks DLSS Quality when the GPU reports DLSS, FSR 2 Quality when it reports FSR 2 and the native image with the tuned TAA otherwise (GFX-417). The editor applies the setup too
    /// when the command line has -pixUpscaler, and puts the pipeline asset back on exit (GFX-448), so lookdev renders and the shipped look agree.
    /// </summary>
    public static class UpscalerControl
    {
        public static string Status = "off";
        static int _override = -1;
        static List<string> _originalNames;
        static bool _saved;

        public static bool DlssAvailable { get { try { return HDDynamicResolutionPlatformCapabilities.DLSSDetected; } catch (Exception) { return false; } } }
        public static bool Fsr2Available { get { try { return HDDynamicResolutionPlatformCapabilities.FSR2Detected; } catch (Exception) { return false; } } }
        /// <summary>The stored choice: 0 Off, 1 DLSS, 2 FSR, 3 Auto.</summary>
        public static int Mode { get { return _override >= 0 ? _override : PixSettings.Upscaler; } }
        /// <summary>The choice after Auto is resolved against this machine: 0 Off, 1 DLSS, 2 FSR.</summary>
        public static int ResolvedMode
        {
            get
            {
                int m = Mode;
                if (m != 3) return m;
                return DlssAvailable ? 1 : (Fsr2Available ? 2 : 0);
            }
        }

        /// <summary>Command line / test override (-pixUpscaler 0|1|2|3), not stored in the settings.</summary>
        public static void Override(int mode) { _override = Mathf.Clamp(mode, 0, 3); Apply(); }

        static bool EditorAllowed()
        {
#if UNITY_EDITOR
            foreach (string a in Environment.GetCommandLineArgs()) if (a == "-pixUpscaler") return true;
            return false;
#else
            return true;
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded += (s, m) => Apply();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.playModeStateChanged += st => { if (st == UnityEditor.PlayModeStateChange.ExitingPlayMode) RestoreEditorAsset(); };
#endif
            Apply();
        }

#if UNITY_EDITOR
        static HDRenderPipelineAsset _editorAsset; static GlobalDynamicResolutionSettings _editorDr; static bool _editorDrSaved;
        static void RestoreEditorAsset()
        {
            if (_editorDrSaved && _editorAsset != null)
            {
                var rps = _editorAsset.currentPlatformRenderPipelineSettings; rps.dynamicResolutionSettings = _editorDr; _editorAsset.currentPlatformRenderPipelineSettings = rps;
            }
            _editorDrSaved = false;
        }
#endif

        public static void Apply()
        {
            try
            {
                int mode = ResolvedMode;
                var asset = GraphicsSettings.currentRenderPipeline as HDRenderPipelineAsset;
                if (asset == null) { Status = "no HDRP asset"; return; }
                bool wantDlss = mode == 1 && DlssAvailable, wantFsr2 = mode == 2 && Fsr2Available;
                bool wantFsr1 = mode == 2 && !wantFsr2;
                var rps = asset.currentPlatformRenderPipelineSettings;
                var dr = rps.dynamicResolutionSettings;
#if UNITY_EDITOR
                if (EditorAllowed() && !_editorDrSaved) { _editorAsset = asset; _editorDr = dr; _editorDrSaved = true; }
#endif
                if (!_saved) { _saved = true; _originalNames = dr.advancedUpscalerNames != null ? new List<string>(dr.advancedUpscalerNames) : new List<string>(); }
                var names = new List<string>();
                if (wantDlss) names.Add("DLSS");
                else if (wantFsr2) names.Add("FSR2");
                else names.AddRange(_originalNames);
                bool changed = false;
                if (dr.advancedUpscalerNames == null || !SameList(dr.advancedUpscalerNames, names)) { dr.advancedUpscalerNames = names; changed = true; }
                var wantFilter = wantFsr1 ? DynamicResUpscaleFilter.EdgeAdaptiveScalingUpres : DynamicResUpscaleFilter.ContrastAdaptiveSharpen;
                if (mode == 0 || mode == 1 || wantFsr1) { if (dr.upsampleFilter != wantFilter) { dr.upsampleFilter = wantFilter; changed = true; } }
                if (wantDlss) { if (dr.DLSSPerfQualitySetting != 2u || !dr.DLSSUseOptimalSettings) { dr.DLSSPerfQualitySetting = 2u; dr.DLSSUseOptimalSettings = true; changed = true; } }       // 2 = Quality (about 67 percent per axis)
                if (wantFsr2) { if (dr.FSR2QualitySetting != 2u || !dr.FSR2UseOptimalSettings) { dr.FSR2QualitySetting = 2u; dr.FSR2UseOptimalSettings = true; changed = true; } }       // Quality, resolution picked by the upscaler
                if (wantDlss || wantFsr2) { if (!dr.enabled) { dr.enabled = true; changed = true; } if (!dr.useMipBias) { dr.useMipBias = true; changed = true; } }   // textures stay as sharp as the display resolution under an upscaler
                if (changed && EditorAllowed()) { rps.dynamicResolutionSettings = dr; asset.currentPlatformRenderPipelineSettings = rps; }
                foreach (var cam in UnityEngine.Object.FindObjectsByType<HDAdditionalCameraData>(FindObjectsSortMode.None))
                {
                    cam.allowDeepLearningSuperSampling = wantDlss;
                    cam.allowFidelityFX2SuperResolution = wantFsr2;
                    cam.allowDynamicResolution = wantDlss || wantFsr2 || wantFsr1 || PixSettings.RenderScale < 0.999f;
                }
                TuneCameras(wantDlss || wantFsr2);
                string auto = Mode == 3 ? "auto: " : "";
                Status = mode == 0 ? (Mode == 3 ? "auto: native image, TAA (no DLSS / FSR 2 on this GPU)" : "off") : (wantDlss ? auto + "DLSS active (quality)" : wantFsr2 ? auto + "FSR 2 active (quality)" : wantFsr1 ? "FSR 1 (edge adaptive scaling) - " + (mode == 2 ? "FSR 2 not supported here" : "") : "DLSS not supported on this GPU - using the engine upscaler");
                if (!EditorAllowed() && (wantDlss || wantFsr2)) Status += " (editor: not applied, start with -pixUpscaler)";
            }
            catch (Exception e) { Status = "upscaler setup failed: " + e.Message; Debug.LogWarning("[PIX] " + Status); }
        }

        /// <summary>v0.9 (GFX-417): sharper, steadier edges for every gameplay camera: TAA at high quality with a little more sharpening and anti-flicker (painted lines and the turf stripes shimmered under the old medium setting). Called on every scene load and by the weather system.</summary>
        public static void TuneCameras(bool upscaled = false)
        {
            foreach (var cam in UnityEngine.Object.FindObjectsByType<HDAdditionalCameraData>(FindObjectsSortMode.None))
            {
                if (cam.antialiasing != HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing) continue;
                cam.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
                cam.taaSharpenMode = HDAdditionalCameraData.TAASharpenMode.ContrastAdaptiveSharpening;
                cam.taaSharpenStrength = upscaled ? 0.35f : 0.6f;
                cam.taaAntiFlicker = 0.7f;
                cam.taaHistorySharpening = 0.5f;
                cam.taaAntiHistoryRinging = true;
                cam.taaBaseBlendFactor = 0.88f;
            }
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
            return "upscaler mode " + Mode + " (" + PixSettings.UpscalerNames[Mathf.Clamp(Mode, 0, 3)] + "), resolved " + ResolvedMode + ", dlss detected=" + DlssAvailable + ", fsr2 detected=" + Fsr2Available + ", status: " + Status;
        }
    }
}
