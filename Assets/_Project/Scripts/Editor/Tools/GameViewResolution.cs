using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Track D (v0.7): runs the autotest in the editor with the Game view at a fixed resolution (default 2560x1440), so screenshots and the FPS log are comparable with the built player.
    /// Launch WITHOUT -batchmode: Unity.exe -projectPath ... -executeMethod PixFootball.EditorTools.GameViewResolution.RunAutoTest -pixAutotest ... [-pixGvW 2560 -pixGvH 1440]
    /// </summary>
    public static class GameViewResolution
    {
        static int Arg(string name, int def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) { int v; if (int.TryParse(a[i + 1], out v)) return v; }
            return def;
        }

        /// <summary>Compile check entry for batch mode.</summary>
        public static void Nop() { }

        public static void RunAutoTest()
        {
            SetSize(Arg("-pixGvW", 2560), Arg("-pixGvH", 1440));
            EditorSceneManager.OpenScene(SceneBuilder.MenuScenePath);
            EditorApplication.isPlaying = true;
        }

        /// <summary>Adds (once) and selects a fixed-resolution Game view size.</summary>
        public static void SetSize(int w, int h)
        {
            try
            {
                var asm = typeof(Editor).Assembly;
                var sizesType = asm.GetType("UnityEditor.GameViewSizes");
                var singleton = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
                var instance = singleton.GetProperty("instance").GetValue(null, null);
                var group = sizesType.GetMethod("GetGroup").Invoke(instance, new object[] { 0 });   // GameViewSizeGroupType.Standalone
                var groupType = group.GetType();
                var getTotal = groupType.GetMethod("GetTotalCount");
                var getSize = groupType.GetMethod("GetGameViewSize");
                int count = (int)getTotal.Invoke(group, null);
                int index = -1;
                string label = "PIXQA " + w + "x" + h;
                for (int i = 0; i < count; i++)
                {
                    var s = getSize.Invoke(group, new object[] { i });
                    var st = s.GetType();
                    if ((int)st.GetProperty("width").GetValue(s, null) == w && (int)st.GetProperty("height").GetValue(s, null) == h && (int)st.GetProperty("sizeType").GetValue(s, null) == 1) { index = i; break; }
                }
                if (index < 0)
                {
                    var gvsType = asm.GetType("UnityEditor.GameViewSize");
                    var enumType = asm.GetType("UnityEditor.GameViewSizeType");
                    var ctor = gvsType.GetConstructor(new[] { enumType, typeof(int), typeof(int), typeof(string) });
                    var size = ctor.Invoke(new object[] { Enum.ToObject(enumType, 1), w, h, label });   // FixedResolution
                    groupType.GetMethod("AddCustomSize").Invoke(group, new object[] { size });
                    index = (int)getTotal.Invoke(group, null) - 1;
                }
                var gvType = asm.GetType("UnityEditor.GameView");
                var gv = EditorWindow.GetWindow(gvType);
                gvType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(gv, index, null);
                gv.Repaint();
                Debug.Log("[GameViewResolution] Game view set to " + w + "x" + h + " (index " + index + ")");
            }
            catch (Exception e) { Debug.LogWarning("[GameViewResolution] could not set the Game view size: " + e.Message); }
        }
    }
}
