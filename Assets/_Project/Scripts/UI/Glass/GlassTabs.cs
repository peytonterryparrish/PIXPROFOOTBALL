using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace PixFootball.UI.Glass
{
    /// <summary>Underline tab strip: one selected tab (orange underline, white label), focus chips on the others. Optional LB / RB paging.</summary>
    public sealed class GlassTabs : MonoBehaviour
    {
        public Button[] Buttons; public TextMeshProUGUI[] Labels; public Image[] Underlines; public GlassGraphic[] Chips;
        public Action<int> Changed;
        public bool BumperPaging;
        public int Index { get; private set; }

        public void SetIndex(int i, bool notify)
        {
            if (Buttons == null || Buttons.Length == 0) return;
            i = Mathf.Clamp(i, 0, Buttons.Length - 1);
            Index = i;
            var t = GlassTheme.T;
            for (int k = 0; k < Buttons.Length; k++)
            {
                bool on = k == i;
                if (Labels[k] != null) Labels[k].color = on ? t.textPrimary : t.textSecondary;
                if (Underlines[k] != null) { var c = Underlines[k].color; c.a = on ? 1f : 0f; Underlines[k].color = c; }
            }
            if (notify && Changed != null) Changed(i);
        }

        void Update()
        {
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

