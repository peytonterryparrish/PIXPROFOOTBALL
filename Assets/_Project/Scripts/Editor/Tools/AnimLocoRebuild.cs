using PixFootball.Art;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>v0.7.0: rebuilds only the player locomotion controller (blend trees) and re-links it in PixRuntimeAssets (the full RuntimeAssetsBuilder / AnimRebuild is not needed for a blend-tree change). Run: -executeMethod PixFootball.EditorTools.AnimLocoRebuild.Run</summary>
    public static class AnimLocoRebuild
    {
        public static void Run()
        {
            BatchWatchdog.Arm(15);
            var rt = AssetDatabase.LoadAssetAtPath<PixRuntimeAssets>(PixRuntimeAssets.EditorPath);
            if (rt == null) { Debug.LogError("[AnimLoco] no PixRuntimeAssets"); return; }
            var speeds = new float[5];
            var ctrl = AnimatorControllerBuilder.BuildAndReport(speeds);
            if (ctrl == null) return;
            rt.playerController = ctrl;
            rt.clipSpeeds = AnimatorControllerBuilder.LastSpeeds;
            EditorUtility.SetDirty(rt);
            AssetDatabase.SaveAssets();
            PixRuntimeAssets.ResetCache();
            Debug.Log("[AnimLoco] controller rebuilt, speeds " + string.Join(",", rt.clipSpeeds));
        }
    }
}
