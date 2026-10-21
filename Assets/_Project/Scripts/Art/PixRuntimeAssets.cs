using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// Small runtime registry for assets produced by the PIX editor tools (cleaned materials, animator controller, TMP fonts, UI sprites).
    /// Lives at Assets/_Licensed/Resources/PixRuntimeAssets.asset (git-ignored with the rest of the licensed art). Load() returns null if absent.
    /// </summary>
    public class PixRuntimeAssets : ScriptableObject
    {
        public const string ResourcesName = "PixRuntimeAssets";
        public const string EditorPath = "Assets/_Licensed/Resources/PixRuntimeAssets.asset";

        [Serializable] public class SpriteEntry { public string key; public Sprite sprite; }
        [Serializable] public class ClipEntry { public string key; public AnimationClip clip; }

        public RuntimeAnimatorController playerController;
        public Material uniformMaterial;
        public Material crowdBillboardMaterial;
        public ScriptableObject fontTitle;   // TMP_FontAsset (Saira Condensed Bold)
        public ScriptableObject fontBody;    // TMP_FontAsset (Barlow Semi Condensed)
        public List<SpriteEntry> sprites = new List<SpriteEntry>();
        public List<AudioClip> menuMusic = new List<AudioClip>();
        public AudioClip crowdAmbience, crowdCheer, footstep, whistle, uiClick, uiMove;
        [Header("Match SFX (v0.2)")]
        public List<AudioClip> sfxTackle = new List<AudioClip>(), sfxHit = new List<AudioClip>(), sfxCatch = new List<AudioClip>(), sfxKick = new List<AudioClip>(), sfxWhistle = new List<AudioClip>(), sfxCheers = new List<AudioClip>(), sfxBeds = new List<AudioClip>(), sfxUi = new List<AudioClip>();
        [Header("Match materials (v0.2)")]
        public Material lineLos, lineFirstDown, ringMarker;
        [Header("College hash marks (v0.3)")] public Material hashMark;
        public float[] clipSpeeds = new float[0];
        public TextAsset teamsCsv;

        static PixRuntimeAssets _inst; static bool _tried;
        Dictionary<string, Sprite> _map;

        public static PixRuntimeAssets Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<PixRuntimeAssets>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<PixRuntimeAssets>(ResourcesName);
            return _inst;
        }

        public static void ResetCache() { _inst = null; _tried = false; }

        public Sprite Sprite(string key)
        {
            if (_map == null)
            {
                _map = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in sprites) if (e != null && e.sprite != null && !string.IsNullOrEmpty(e.key)) _map[e.key] = e.sprite;
            }
            Sprite s; return _map.TryGetValue(key, out s) ? s : null;
        }
    }
}

