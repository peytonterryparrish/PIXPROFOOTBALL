using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>What a flat panel of the Classic kit becomes in the glass theme.</summary>
    public enum GlassSkinRole { Auto, Skip, Glass, Well, Solid, Selected }


    /// <summary>Applies <see cref="GlassImageSkin"/> to every eligible Image under a root (idempotent; call again after a screen created more children).</summary>
    public static class GlassSkin
    {
        /// <summary>Refreshes every skin under a root (QA renders).</summary>
        public static void RefreshAll(Transform root)
        {
            if (root == null) return;
            foreach (var s in root.GetComponentsInChildren<GlassImageSkin>(true)) s.Refresh();
        }

        public static int Apply(Transform root)
        {
            if (root == null || !UiTheme.IsGlass) return 0;
            int n = 0;
            var list = new List<Image>();
            root.GetComponentsInChildren(true, list);
            foreach (var img in list)
            {
                if (img.GetComponent<GlassImageSkin>() != null) continue;
                if (img.GetComponentInParent<GlassGraphic>() != null && img.transform.parent != null && img.transform.parent.GetComponent<GlassGraphic>() != null) { /* a child of a glass panel is still a normal panel */ }
                if (!GlassImageSkin.Eligible(img)) continue;
                img.gameObject.AddComponent<GlassImageSkin>();
                n++;
            }
            return n;
        }
    }
}

namespace PixFootball.UI.Glass
{
}
