using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace PixFootball.EditorTools
{
    public static class SceneTextureDump
    {
        public static void Run()
        {
            EditorSceneManager.OpenScene(SceneBuilder.StadiumScenePath);
            var sb = new StringBuilder();
            var seen = new HashSet<string>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.name.StartsWith("Crowd_")) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    string mp = AssetDatabase.GetAssetPath(m);
                    if (!seen.Add(mp + "|" + r.name)) continue;
                    var sh = m.shader; int pc = sh.GetPropertyCount();
                    var tex = new StringBuilder();
                    for (int i = 0; i < pc; i++)
                        if (sh.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                        {
                            var t = m.GetTexture(sh.GetPropertyName(i));
                            if (t != null) tex.Append(" [" + sh.GetPropertyName(i) + "=" + Path.GetFileName(AssetDatabase.GetAssetPath(t)) + "]");
                        }
                    sb.AppendLine(r.name + " :: " + Path.GetFileName(mp) + " :: " + sh.name + tex);
                }
            }
            File.WriteAllText("Logs/scene_textures.txt", sb.ToString());
        }
    }
}
