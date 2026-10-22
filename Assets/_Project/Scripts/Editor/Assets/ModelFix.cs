using System.IO;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// One-shot batch pass of the MODELS track (a single Unity launch): verify that the Blender exporter reproduces the original body meshes (no-bulk export in
    /// Assets/_Licensed/PIX/Meshes/Bodies), copy the bulky export from Logs/model_qa/pixmesh over it, import it (prefabs + LOD table) and render the "after" QA sheets.
    /// Batch: ModelFix.All  (-qaTag after).
    /// </summary>
    public static class ModelFix
    {
        public static void All()
        {
            bool ok = BodyMeshImporter.VerifyAll();
            Debug.Log("[MdlFix] exporter verification ok=" + ok);
            if (!ok) { Debug.LogError("[MdlFix] aborted: exporter does not reproduce the original meshes"); return; }
            foreach (string size in new[] { "Small", "Medium", "Large" })
            {
                string src = "Logs/model_qa/pixmesh/" + size + ".pixmesh", dst = BodyMeshImporter.Dir + "/" + size + ".pixmesh";
                if (File.Exists(src)) File.Copy(src, dst, true);
            }
            AssetDatabase.Refresh();
            BodyMeshImporter.Run();
            Debug.Log("[MdlFix] imported");
            ModelQaRenderer.RunAll();
        }

        /// <summary>Coach SMRs ship with an (almost) empty localBounds (0.01 m box at the root bone, prefab_dump.txt): with updateWhenOffscreen off Unity culls the whole coach whenever that point leaves the screen. Bounds follow the bones instead (3 coaches: negligible CPU).</summary>
        public static void FixNpcBounds()
        {
            int n = 0;
            foreach (string name in new[] { "PIX_Coach_A", "PIX_Coach_B", "PIX_Coach_C" })
            {
                string path = CharacterSetup.Root + "/Prefabs/" + name + ".prefab";
                if (!File.Exists(path)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                bool dirty = false;
                foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    if (smr.localBounds.size.magnitude < 0.2f && !smr.updateWhenOffscreen) { smr.updateWhenOffscreen = true; dirty = true; n++; }
                if (dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
            }
            Debug.Log("[MdlFix] coach renderers switched to bone-driven bounds: " + n);
        }

        /// <summary>Import only (the .pixmesh files are already in place) and render.</summary>
        public static void ImportAndShoot()
        {
            BodyMeshImporter.Run();
            FixNpcBounds();
            ModelQaRenderer.RunAll();
        }
    }
}
