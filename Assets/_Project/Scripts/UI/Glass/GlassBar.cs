using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace PixFootball.UI.Glass
{
    /// <summary>A progress / rating bar with a settable value (the fill is an anchored child).</summary>
    public sealed class GlassBar : MonoBehaviour
    {
        public RectTransform Fill; public float Value;
        public void Set(float v)
        {
            Value = Mathf.Clamp01(v);
            if (Fill != null) Fill.anchorMax = new Vector2(Value, 1f);
        }
    }
}

