using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace PixFootball.UI.Glass
{
    /// <summary>
    /// Focus / hover / press visuals for a glass control (gamepad focus and the mouse look the same): the surface eases to the orange edge + glow state, the label brightens, the control grows a hair
    /// and an optional accent rail fades in. Timings come from the theme (120-200 ms, unscaled time so it works while the game is paused). Hovering also moves the selection so the pad and the
    /// mouse never highlight two controls (QA-764).
    /// </summary>
    public sealed class GlassFocusable : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        public GlassGraphic Surface;
        public TextMeshProUGUI Label;
        public Graphic Icon;
        public Graphic Rail;
        public Color LabelNormal = new Color(0.75f, 0.76f, 0.77f), LabelFocus = Color.white;
        public Color IconNormal = new Color(0.88f, 0.89f, 0.9f), IconFocus = Color.white;
        public float FocusScale = 1.02f;
        public bool PointerSelects = true;
        public bool PlaySound = true;
        /// <summary>The label is dark on a silver (action) surface: it keeps its colour.</summary>
        public bool KeepLabelColor;
        /// <summary>Extra brightness the surface gets when focused.</summary>
        public float FocusBrighten = 0.035f;

        float _f, _press; bool _alphaEnabled = true;
        bool _selected, _hover, _down, _enabledLook = true;
        Selectable _sel;

        /// <summary>Edit-mode tools and shots (no Update loop): draws the control at a focus amount (0..1) and enabled or disabled look now.</summary>
        public void ShowState(float focus, bool enabled) { _f = Mathf.Clamp01(focus); _enabledLook = enabled; Apply(); }

        public bool IsOn { get { return _selected || _hover; } }
        public float Amount { get { return _f; } }

        void Awake() { _sel = GetComponent<Selectable>(); }

        void OnEnable()
        {
            _selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
            _hover = false; _down = false;
            _f = _selected ? 1f : 0f; Apply();
        }

        /// <summary>Tile look: the object that moves to the front (last sibling) while this control is focused, so its scaled glow is never covered by neighbours (the mock raises the focused tile).</summary>
        public Transform RaiseTarget;
        void Raise() { if (RaiseTarget != null && RaiseTarget.parent != null && RaiseTarget.GetSiblingIndex() != RaiseTarget.parent.childCount - 1) RaiseTarget.SetAsLastSibling(); }

        public void OnSelect(BaseEventData e) { _selected = true; Raise(); if (PlaySound) UiAudio.Move(); }
        public void OnDeselect(BaseEventData e) { _selected = false; }
        public void OnPointerEnter(PointerEventData e)
        {
            if (_sel != null && !_sel.IsInteractable()) return;
            _hover = true; Raise();
            if (PointerSelects && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject) EventSystem.current.SetSelectedGameObject(gameObject);
        }
        public void OnPointerExit(PointerEventData e) { _hover = false; _down = false; }
        public void OnPointerDown(PointerEventData e) { _down = true; }
        public void OnPointerUp(PointerEventData e) { _down = false; }

        /// <summary>Forces the look (used by tabs: the selected tab keeps its accent whether or not it has focus).</summary>
        public bool Pinned;
        /// <summary>The surface is invisible until focused (ghost buttons, tabs): its opacity follows the focus amount.</summary>
        public bool GhostSurface;

        void Update() { Step(Time.unscaledDeltaTime); }

        /// <summary>One frame of focus / press easing (also called by the measurement tools): no allocation.</summary>
        public void Step(float dt)
        {
            bool interactable = _sel == null || _sel.IsInteractable();
            float target = (interactable && (IsOn || Pinned)) ? 1f : 0f;
            float speed = 1f / Mathf.Max(0.02f, GlassTheme.T.motionBase);
            float nf = Mathf.MoveTowards(_f, target, dt * speed);
            float np = Mathf.MoveTowards(_press, (_down && interactable) ? 1f : 0f, dt * speed * 2f);
            if (Mathf.Approximately(nf, _f) && Mathf.Approximately(np, _press) && interactable == _enabledLook) return;
            _f = nf; _press = np; _enabledLook = interactable;
            Apply();
        }

        void Apply()
        {
            float e = _f * _f * (3f - 2f * _f);   // smoothstep
            if (Surface != null)
            {
                Surface.SetFocus(e);
                if (GhostSurface) { var sc = Surface.color; sc.a = e; Surface.color = sc; }
                else if (GlassTheme.T.flat && _enabledLook != _alphaEnabled) { _alphaEnabled = _enabledLook; var sc = Surface.color; sc.a = _enabledLook ? 1f : 0.4f; Surface.color = sc; }   // Tile: a disabled control is drawn at 40%
                Surface.SetBrighten(e * FocusBrighten - _press * 0.05f - (_enabledLook ? 0f : 0.04f));
            }
            var tk = GlassTheme.T;
            float fs = FocusScale;
            if (tk.flat && Surface != null && (Surface.Kind == GlassKind.Row || Surface.Kind == GlassKind.Tab)) fs = 1f;   // rows and tabs do not grow: their focus is the gold bar / plate
            float s = Mathf.Lerp(1f, fs, e) * (1f - _press * (tk.flat ? 1f - tk.pressScale : 0.012f));
            transform.localScale = new Vector3(s, s, 1f);
            if (Label != null && !KeepLabelColor)
            {
                var c = Color.Lerp(LabelNormal, LabelFocus, e);
                if (!_enabledLook) c.a = 0.45f;
                Label.color = c;
            }
            else if (Label != null && !_enabledLook) { var c = Label.color; c.a = 0.45f; Label.color = c; }
            if (Icon != null) { var c = Color.Lerp(IconNormal, IconFocus, e); if (!_enabledLook) c.a = 0.45f; Icon.color = c; }
            if (Rail != null) { var c = Rail.color; c.a = e; Rail.color = c; }
        }
    }
}

