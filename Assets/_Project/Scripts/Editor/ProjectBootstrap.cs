using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.EditorTools
{
    /// <summary>One-time project configuration: HDRP asset, linear color, player settings.
    /// Run headless: Unity -batchmode -executeMethod PixFootball.EditorTools.ProjectBootstrap.Run -quit</summary>
    public static class ProjectBootstrap
    {
        const string SettingsDir = "Assets/_Project/Settings";
        const string HdrpAssetPath = SettingsDir + "/PixHDRP.asset";

        [MenuItem("PIX Football/Setup/Bootstrap Project")]
        public static void Run()
        {
            Directory.CreateDirectory(SettingsDir);

            var hdrp = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(HdrpAssetPath);
            if (hdrp == null)
            {
                hdrp = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
                AssetDatabase.CreateAsset(hdrp, HdrpAssetPath);
            }
            GraphicsSettings.defaultRenderPipeline = hdrp;
            QualitySettings.renderPipeline = hdrp;

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.companyName = "Peyton Parrish Games";
            PlayerSettings.productName = "PIX Football";
            if (string.IsNullOrEmpty(PlayerSettings.bundleVersion) || PlayerSettings.bundleVersion == "1.0")
                PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D12 });
            PlayerSettings.defaultScreenWidth = 2560;
            PlayerSettings.defaultScreenHeight = 1440;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;

            AssetDatabase.SaveAssets();
            Debug.Log("[PIX] Bootstrap complete: HDRP asset assigned, linear color, D3D12, version " + PlayerSettings.bundleVersion);
        }
    }
}
