using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace PixFootball.UI.Glass
{
    /// <summary>Pill switch row: A / Submit toggles, Right turns it on, Left off. The knob slides and the orange fill fades (motion from the theme).</summary>
    public sealed class GlassToggle : Selectable, ISubmitHandler, IPointerClickHandler
    {
        public bool Value;
        public Action<bool> Changed;
        public GlassGraphic Track, OnFill;
        public RectTransform Knob;
        public TextMeshProUGUI Label;
        float _t;
        const float Off = 17f, On = 55f;

        public void Set(bool v, bool animate)
        {
            Value = v;
            if (!animate) { _t = v ? 1f : 0f; Apply(); }
        }

        public void Flip() { Set(!Value, true); UiAudio.Move(); if (Changed != null) Changed(Value); }

        public void OnSubmit(BaseEventData e) { Flip(); }
        public void OnPointerClick(PointerEventData e) { if (IsInteractable()) Flip(); }

        public override void OnMove(AxisEventData e)
        {
            if (e.moveDir == MoveDirection.Right) { if (!Value) { Set(true, true); UiAudio.Move(); if (Changed != null) Changed(true); } e.Use(); }
            else if (e.moveDir == MoveDirection.Left) { if (Value) { Set(false, true); UiAudio.Move(); if (Changed != null) Changed(false); } e.Use(); }
            else base.OnMove(e);
        }

        public override void OnPointerEnter(PointerEventData e)
        {
            base.OnPointerEnter(e);
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject) EventSystem.current.SetSelectedGameObject(gameObject);
        }

        void Update()
        {
            float target = Value ? 1f : 0f;
            if (Mathf.Approximately(_t, target)) return;
            _t = Mathf.MoveTowards(_t, target, Time.unscaledDeltaTime / Mathf.Max(0.02f, GlassTheme.T.motionFast));
            Apply();
        }

        void Apply()
        {
            float e = _t * _t * (3f - 2f * _t);
            if (Knob != null) Knob.anchoredPosition = new Vector2(Mathf.Lerp(Off, On, e), 0f);
            if (OnFill != null) { var c = OnFill.color; c.a = e; OnFill.color = c; }
        }
    }
}

