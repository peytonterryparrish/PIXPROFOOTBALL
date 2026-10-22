using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>v0.9.0 QBSTANCE: batch helpers for the stance / QB audit runs (QA only).</summary>
    public static class QbStanceTools
    {
        /// <summary>Re-imports the scripts of the Art assembly and the licensed ScriptableObjects (a working copy made from another project's Library keeps stale script bindings), then runs the engine audit.</summary>
        public static void RunAudit()
        {
            ReimportBindings();
            EngineAudit.Run();
        }

        public static void ReimportBindings()
        {
            foreach (var g in AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/_Project/Scripts/Art" }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
            foreach (var g in AssetDatabase.FindAssets("t:ScriptableObject", new[] { "Assets/_Licensed/Resources" }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(g), ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset("Assets/_Licensed/ArtLibrary.asset", ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
            Debug.Log("[QbStance] bindings re-imported");
        }
    }
}
