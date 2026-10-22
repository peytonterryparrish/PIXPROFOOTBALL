using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace PixFootball.UI.Glass
{
    /// <summary>Fades / slides / scales a panel in when it becomes active (and can be staggered): 120-200 ms, ease-out, unscaled time.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class GlassReveal : MonoBehaviour
    {
        public float Delay;
        public Vector2 FromOffset = new Vector2(0f, -14f);
        public float FromScale = 0.985f;
        public float Duration = 0f;       // 0 = theme slow motion
        public bool PlayOnEnable = true;

        CanvasGroup _cg; RectTransform _rt; Vector2 _rest; float _t; bool _playing, _haveRest;

        void Awake() { _cg = GetComponent<CanvasGroup>(); _rt = transform as RectTransform; }

        void OnEnable() { if (PlayOnEnable) Play(); }

        public void Play()
        {
            if (_cg == null) _cg = GetComponent<CanvasGroup>();
            if (_rt == null) _rt = transform as RectTransform;
            if (!_haveRest) { _rest = _rt.anchoredPosition; _haveRest = true; }
            _t = -Delay; _playing = true; Set(0f);
        }

        void Update()
        {
            if (!_playing) return;
            _t += Time.unscaledDeltaTime;
            float d = Duration > 0f ? Duration : GlassTheme.T.motionSlow;
            float u = Mathf.Clamp01(_t / d);
            Set(1f - (1f - u) * (1f - u));
            if (u >= 1f) _playing = false;
        }

        void Set(float u)
        {
            _cg.alpha = u;
            if (_rt != null && _haveRest) _rt.anchoredPosition = _rest + FromOffset * (1f - u);
            float s = Mathf.Lerp(FromScale, 1f, u);
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}

