using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// Material templates, skin-tone sets, hair-shell textures and the jersey layout (number / name / wordmark placements on the PHR jersey UV) of the photoreal player
    /// (PIX_PhotoPlayer_*). Built by <c>PIX Football/Photoreal Player/Build</c> (PhotoPlayerBuilder) into Assets/_Licensed/PhotorealPlayer/PhotoPlayerLibrary.asset and referenced by the prefabs.
    /// Everything it points to lives under Assets/_Licensed/PhotorealPlayer (git-ignored, swappable if the model licence changes).
    /// </summary>
    public class PhotoPlayerLibrary : ScriptableObject
    {
        public const string EditorPath = "Assets/_Licensed/PhotorealPlayer/PhotoPlayerLibrary.asset";

        [Header("PIX/Lit uniform-mode templates (cloned per player)")]
        public Material jersey, pants, socks, cleats, sleeve, band, pouch, towel, gloves, padding;
        [Header("PIX/Lit helmet-mode template, solid face mask, visor, alpha-clip hair shell")]
        public Material helmet, faceMask, visor, hair;
        [Header("Skin: one material per tone (Arm_Skin graph + SSS diffusion profile)")]
        public Material[] skinHead = new Material[PlayerAppearance.SkinToneCount];
        public Material[] skinArms = new Material[PlayerAppearance.SkinToneCount];
        [Header("Eyes (Eyes_Shader graph: _Hue / _Saturation recolour the iris)")]
        public Material eyes;
        public Vector2[] eyeHueSaturation = new Vector2[PlayerAppearance.EyeColorCount];
        [Header("Hair shell textures in the head UV (RGB tone, A cutout)")]
        public Texture2D[] beard = new Texture2D[4];      // stubble, goatee, full, moustache
        public Texture2D[] brow = new Texture2D[3];       // thin, normal, thick
        public Texture2D[] scalp = new Texture2D[2];      // buzz, short

        [Header("Jersey layout on the PHR jersey UV: (cx, cy, height, rotation radians), same convention as PixLookAssets")]
        public Vector4 numberFront, numberBack, numberShoulderL, numberShoulderR, namePlacement, wordPlacement;
        [Tooltip("Maximum width of the player name (back) and team wordmark (chest) in PHR jersey uv units, replaces PixLookAssets.textMaxWidth for this uv layout.")]
        public Vector2 textMaxWidth = new Vector2(0.2f, 0.1f);

        static PhotoPlayerLibrary _inst; static bool _tried;

        public static PhotoPlayerLibrary Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<PhotoPlayerLibrary>(EditorPath);
#endif
            return _inst;
        }

        public static void ResetCache() { _inst = null; _tried = false; }
    }
}
