using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>v0.4: turns HDRP dynamic resolution on in the project's HDRP asset (hardware dynamic resolution, 50 to 100 percent) so the Render scale option in Settings works. Cameras only opt in when the scale is below 100 percent.</summary>
    public static class HdrpTuning
    {
        const string AssetPath = "Assets/_Project/Settings/PixHDRP.asset";

        [MenuItem("PIX Football/Settings/Enable HDRP Dynamic Resolution")]
        public static void EnableDynamicResolution()
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(AssetPath);
            var so = new SerializedObject(asset);
            const string root = "m_RenderPipelineSettings.dynamicResolutionSettings.";
            Set(so, root + "enabled", true); Set(so, root + "minPercentage", 50f); Set(so, root + "maxPercentage", 100f);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log("[Hdrp] dynamic resolution enabled in " + AssetPath);
        }

        static void Set(SerializedObject so, string path, object v)
        {
            var p = so.FindProperty(path);
            if (p == null) { Debug.LogWarning("[Hdrp] missing property " + path); return; }
            if (v is bool) p.boolValue = (bool)v; else if (v is float) p.floatValue = (float)v;
        }
    }
}
