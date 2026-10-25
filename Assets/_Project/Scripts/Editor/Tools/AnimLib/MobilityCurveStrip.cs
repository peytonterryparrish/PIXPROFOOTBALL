using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.12.0 (integration): the MoCap Online Mobility clips carry an Animator float curve named "Speed" (classID 95). Played by the locomotion
    /// rig it takes over the controller's own Speed parameter, and every SetFloat logged "Parameter 'Speed' is controlled by a curve" (800 k a game).
    /// Removes only curves named after a locomotion controller parameter from the cleaned clips under Assets/_Licensed/MocapOnline: muscle,
    /// finger and root curves are bound to the Animator type too and stay (a name match is the only test). Run after every MotionDbBuilder
    /// rebuild of the Mobility set, which re-creates the cleaned clips from the takes.
    /// Run: Unity -batchmode -projectPath ... -executeMethod PixFootball.EditorTools.MobilityCurveStrip.Run -quit
    /// </summary>
    public static class MobilityCurveStrip
    {
        static readonly HashSet<string> Params = new HashSet<string> { "Speed", "VelX", "VelZ", "Celebrating", "Protect", "LocoSpeed", "CycleOffset" };

        [MenuItem("PIX Football/Animation/Strip Mobility Parameter Curves")]
        public static void Run()
        {
            int clips = 0, curves = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/_Licensed/MocapOnline" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".anim")) continue;     // imported FBX takes are read-only; the matcher plays the cleaned .anim copies
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) continue;
                int n = 0;
                foreach (var b in AnimationUtility.GetCurveBindings(clip))
                {
                    if (b.type != typeof(Animator) || !string.IsNullOrEmpty(b.path) || !Params.Contains(b.propertyName)) continue;
                    AnimationUtility.SetEditorCurve(clip, b, null);
                    n++;
                }
                if (n == 0) continue;
                EditorUtility.SetDirty(clip);
                clips++; curves += n;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[PIX] MobilityCurveStrip: removed {curves} parameter curves from {clips} clips");
        }
    }
}
