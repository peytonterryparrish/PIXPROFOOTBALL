using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace PixFootball.UI.Glass
{
    /// <summary>
    /// Tab strip. Glass look: underline tabs (selected = orange underline, white label). Tile look (Direction 16): skewed parallelogram tabs, the gold plate slides to the active tab (0.16 s ease-out) and the label
    /// turns dark on gold. Optional LB / RB paging, and an optional <see cref="Content"/> panel that crossfades (0.20 s, 24 unit drift in the paging direction) when the tab changes.
    /// </summary>
    public sealed class GlassTabs : MonoBehaviour
    {
        public Button[] Buttons; public TextMeshProUGUI[] Labels; public Image[] Underlines; public GlassGraphic[] Chips;
        public Action<int> Changed;
        public bool BumperPaging;
        /// <summary>Underline tabs with their own colours (the play-call strip tabs: light gold when active).</summary>
        public bool UseCustomColors; public Color ActiveColor = Color.white, IdleColor = Color.grey;
        public int Index { get; private set; }

        // ---- Tile look
        public bool Tile;
        /// <summary>The strip of tabs (without the caps) and the whole bar (with LB / RB caps; null for a bare strip).</summary>
        public RectTransform Root, BarRoot;
        public GlassGraphic[] Bgs; public GlassGraphic Plate;
        public float[] SlotX, SlotW;
        /// <summary>Panel that crossfades when the tab changes (set by the screen; optional).</summary>
        public RectTransform Content;
        float _px, _pw, _fromX, _fromW, _toX, _toW, _anim = 1f;
        bool _plateReady;

        /// <summary>The whole bar to anchor (caps included when the bar has them).</summary>
        public RectTransform Bar { get { return BarRoot != null ? BarRoot : (RectTransform)transform; } }

        public void SetIndex(int i, bool notify)
        {
            if (Buttons == null || Buttons.Length == 0) return;
            i = Mathf.Clamp(i, 0, Buttons.Length - 1);
            int prev = Index;
            Index = i;
            var t = GlassTheme.T;
            if (Tile && Bgs != null)
            {
                for (int k = 0; k < Buttons.Length; k++) if (Labels[k] != null) { var lc = k == i ? t.onGold : t.textTab; if (!Buttons[k].interactable) lc.a = 0.4f; Labels[k].color = lc; }
                _fromX = _px; _fromW = _pw; _toX = SlotX[i]; _toW = SlotW[i];
                if (!_plateReady) SnapPlate();
                else if (prev != i) _anim = 0f;
            }
            else
            {
                for (int k = 0; k < Buttons.Length; k++)
                {
                    bool on = k == i;
                    if (Labels[k] != null) Labels[k].color = UseCustomColors ? (on ? ActiveColor : IdleColor) : (on ? t.textPrimary : t.textSecondary);
                    if (Underlines != null && Underlines[k] != null) { var c = Underlines[k].color; c.a = on ? 1f : 0f; Underlines[k].color = c; }
                }
            }
            if (notify)
            {
                if (Content != null && prev != i) TileCrossfade.Play(Content, i > prev ? 1 : -1);
                if (Changed != null) Changed(i);
            }
        }

        /// <summary>Enables or disables one tab (disabled = 40% and not selectable; the Tile look dims the plate, the cap text and the label).</summary>
        public void SetEnabled(int i, bool on)
        {
            if (Buttons == null || i < 0 || i >= Buttons.Length) return;
            Buttons[i].interactable = on;
            float a = on ? 1f : 0.4f;
            if (Labels[i] != null) { var c = Labels[i].color; c.a = a; Labels[i].color = c; }
            if (Bgs != null && Bgs[i] != null) { var c = Bgs[i].color; c.a = a; Bgs[i].color = c; }
            if (Underlines != null && Underlines[i] != null && !on) { var c = Underlines[i].color; c.a = 0f; Underlines[i].color = c; }
        }

        /// <summary>Puts the gold plate on the active tab without animating (build time, edit-mode shots).</summary>
        public void SnapPlate()
        {
            if (!Tile || Plate == null || SlotX == null) return;
            _plateReady = true; _px = SlotX[Index]; _pw = SlotW[Index]; _toX = _px; _toW = _pw; _fromX = _px; _fromW = _pw; _anim = 1f;
            PlaceMarker();
        }

        void PlaceMarker()
        {
            var r = Plate.rectTransform;
            r.anchoredPosition = new Vector2(_px, 0f);
            r.sizeDelta = new Vector2(_pw, GlassTheme.T.tabHeight);
        }

        void Update()
        {
            if (Tile && _anim < 1f)
            {
                _anim = Mathf.Min(1f, _anim + Time.unscaledDeltaTime / Mathf.Max(0.02f, GlassTheme.T.tabSlide));
                float e = 1f - (1f - _anim) * (1f - _anim) * (1f - _anim);   // ease-out
                _px = Mathf.Lerp(_fromX, _toX, e); _pw = Mathf.Lerp(_fromW, _toW, e);
                PlaceMarker();
            }
            if (!BumperPaging || Buttons == null || UiDialog.AnyOpen) return;
            int dir = 0;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null) { if (pad.leftShoulder.wasPressedThisFrame) dir = -1; else if (pad.rightShoulder.wasPressedThisFrame) dir = 1; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null) { if (kb.pageUpKey.wasPressedThisFrame) dir = -1; else if (kb.pageDownKey.wasPressedThisFrame) dir = 1; }
            if (dir == 0) return;
            SetIndex(GlassNav.Step(Index, dir, Buttons.Length, true), true);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(Buttons[Index].gameObject);
        }
    }
}
