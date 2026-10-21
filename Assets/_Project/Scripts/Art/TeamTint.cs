using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// Team-colour tint for the sideline coach prefabs (PIX_Coach_A/B/C): the polo / jacket ("primary") and the cap / visor ("secondary")
    /// use HDRP/Lit materials whose _BaseColor is white-ish in the art and is multiplied by the team colour at runtime through a
    /// MaterialPropertyBlock (no material instances, so every coach of a team shares the batch).
    /// </summary>
    [DisallowMultipleComponent]
    public class TeamTint : MonoBehaviour
    {
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [Tooltip("Renderers tinted with the primary team colour (polo / jacket).")]
        public Renderer[] primaryRenderers;
        [Tooltip("Renderers tinted with the secondary team colour (cap / visor).")]
        public Renderer[] secondaryRenderers;
        public Color primary = Color.white;
        public Color secondary = Color.white;
        [Tooltip("False: keep the colours authored on the materials until SetTeamColors is called.")]
        public bool overrideMaterialColors;

        MaterialPropertyBlock _block;

        void OnEnable() { Apply(); }

        public void SetTeamColors(Color primaryColor, Color secondaryColor)
        {
            primary = primaryColor;
            secondary = secondaryColor;
            overrideMaterialColors = true;
            Apply();
        }

        public void Apply()
        {
            if (!overrideMaterialColors) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            Paint(primaryRenderers, primary);
            Paint(secondaryRenderers, secondary);
        }

        void Paint(Renderer[] renderers, Color c)
        {
            if (renderers == null) return;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_block);
                _block.SetColor(BaseColor, c);
                r.SetPropertyBlock(_block);
            }
        }
    }
}
