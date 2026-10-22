using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace PixFootball.UI.Glass
{
    /// <summary>Chevron tint for a stepper row: grey, orange while the row has focus.</summary>
    public sealed class GlassChevronTint : MonoBehaviour
    {
        public Image Left, Right; public GlassFocusable Focus; public OptionSelector Selector;
        void Update()
        {
            if (Focus == null) return;
            var t = GlassTheme.T;
            var c = Color.Lerp(t.textMuted, t.accent, Focus.Amount);
            if (Left != null) Left.color = c;
            if (Right != null) Right.color = c;
        }
    }
}

