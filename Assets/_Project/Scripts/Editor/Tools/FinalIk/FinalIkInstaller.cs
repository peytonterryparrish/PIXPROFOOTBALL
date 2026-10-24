using System.IO;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11.0 MOTION: puts the Final IK bridge (this repo's own code, Editor/Tools/FinalIk/Bridge~, a folder Unity ignores because of the tilde) next to the licensed Final IK asset:
    /// Assets/_Licensed/RootMotion/PixBridge/. Both folders are git-ignored (Asset Store EULA: the source must never be redistributed). Without Assets/_Licensed/RootMotion the bridge is not installed and the game runs on its own foot / arm code.
    /// Final IK itself is unpacked by Tools/FinalIk/install_finalik.sh (a variant unpacker that keeps the scripts). Run: -executeMethod PixFootball.EditorTools.FinalIkInstaller.Run  (or the menu PIX/Install Final IK bridge).
    /// </summary>
    public static class FinalIkInstaller
    {
        const string Src = "Assets/_Project/Scripts/Editor/Tools/FinalIk/Bridge~", DefaultDst = "Assets/_Licensed/RootMotion/PixBridge", Asset = "Assets/_Licensed/RootMotion/RootMotion.Runtime.asmdef";

        [MenuItem("PIX/Install Final IK bridge")]
        public static void Run()
        {
            if (!File.Exists(Asset)) { Debug.Log("[FinalIk] Assets/_Licensed/RootMotion is not installed: no bridge (the game uses its own IK)"); return; }
            string Dst = DefaultDst; { var a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a, "-pixIkBridgeDst"); if (i >= 0 && i + 1 < a.Length) Dst = a[i + 1]; }      // a private test copy outside the shared _Licensed folder (every track's project compiles whatever is in there)
            Directory.CreateDirectory(Dst);
            foreach (var f in Directory.GetFiles(Src)) { string to = Path.Combine(Dst, Path.GetFileName(f)); if (!File.Exists(to) || File.ReadAllText(to) != File.ReadAllText(f)) File.Copy(f, to, true); }
            AssetDatabase.Refresh();
            Debug.Log("[FinalIk] bridge installed in " + Dst);
        }
    }
}
