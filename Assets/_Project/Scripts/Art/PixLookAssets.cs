using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// v0.6 look assets: the PIX/Lit uniform / helmet / turf / marking materials, weather sprites and the jersey number placements.
    /// Lives at Assets/_Licensed/Resources/PixLookAssets.asset (built by <c>PIX Football/V6/Build All V6 Art</c>; git-ignored with the rest of the licensed art). Load() returns null if absent.
    /// </summary>
    public class PixLookAssets : ScriptableObject
    {
        public const string ResourcesName = "PixLookAssets";
        public const string EditorPath = "Assets/_Licensed/Resources/PixLookAssets.asset";

        [Header("PIX/Lit materials")]
        public Material uniformMaterial;
        public Material helmetMaterial;
        public Material turfMaterial;
        public Material markingsMaterial;

        [Header("Weather / effects")]
        public Shader fxShader;
        public Material rainMaterial, snowMaterial, splashMaterial, puffMaterial, wearStampMaterial;
        [Header("Field wear")] public ComputeShader wearCompute;
        public Texture2D rainStreak, snowflake, splash, puff, floodCookie, grassBlades, helmetScuff, turfMicroNormal;

        [Header("Jersey number placements on the uniform uv (cx, cy, height, rotation radians)")]
        public Vector4 numberBack, numberFront, numberShoulderL, numberShoulderR;
        [Header("Jersey text: player name (back) and team wordmark (chest): cx, cy, height, rotation; max widths in uv")]
        public Vector4 namePlacement, wordPlacement;
        public Vector2 textMaxWidth = new Vector2(0.24f, 0.13f);
        public Texture2D letterAtlas;
        [Header("Helmet crest decal: size, centre y, centre z, flip (shell half extent units)")]
        public Vector4 helmetDecal = new Vector4(0.4f, 0.04f, -0.02f, 1f);

        static PixLookAssets _inst; static bool _tried;

        public static PixLookAssets Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<PixLookAssets>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<PixLookAssets>(ResourcesName);
            return _inst;
        }

        public static void ResetCache() { _inst = null; _tried = false; }
    }
}
