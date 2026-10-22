using System;
using System.Collections.Generic;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>Hidden runtime object: owns the Custom Pass volume and keeps the shader globals sane when the blur is off.</summary>
    [ExecuteAlways]
    public sealed class GlassBackdropDriver : MonoBehaviour
    {
        static GlassBackdropDriver _inst;
        CustomPassVolume _volume;
        Texture2D _black;

        public static void Ensure()
        {
            if (_inst != null) return;
            var go = new GameObject("PixGlassBackdrop") { hideFlags = HideFlags.HideAndDontSave };
            if (Application.isPlaying) DontDestroyOnLoad(go);
            _inst = go.AddComponent<GlassBackdropDriver>();
        }

        void OnEnable()
        {
            _black = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            _black.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.11f, 1f)); _black.Apply();
            Shader.SetGlobalTexture(GlassBackdrop.TexId, _black);
            Shader.SetGlobalVector(GlassBackdrop.ScaleId, new Vector4(1f, 1f, 0f, 0f));
            Shader.SetGlobalFloat(GlassBackdrop.OnId, 0f);
            _volume = gameObject.AddComponent<CustomPassVolume>();
            _volume.isGlobal = true;
            _volume.injectionPoint = GlassBackdrop.InjectionPoint;
            _volume.priority = 100f;
            _volume.AddPassOfType(typeof(GlassBlurPass));
        }

        void OnDisable()
        {
            if (_volume != null) { if (Application.isPlaying) Destroy(_volume); else DestroyImmediate(_volume); }
            if (_black != null) DestroyImmediate(_black);
            Shader.SetGlobalFloat(GlassBackdrop.OnId, 0f);
        }

        void Update()
        {
            if (!GlassBackdrop.WantBlur) Shader.SetGlobalFloat(GlassBackdrop.OnId, 0f);
            // the injection point may be changed by the QA tool before the first frame
            if (_volume != null && _volume.injectionPoint != GlassBackdrop.InjectionPoint) _volume.injectionPoint = GlassBackdrop.InjectionPoint;
        }
    }
}

