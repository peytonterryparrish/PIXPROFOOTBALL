using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace PixFootball.UI.Glass
{
    /// <summary>
    /// Remembers which registry icon an Image shows and fills the sprite in when it is enabled. Prefabs store only the key (the licensed sprite itself is never referenced from committed assets);
    /// the sprite comes from <see cref="GlassIcons"/> at runtime.
    /// </summary>
    [UnityEngine.ExecuteAlways]
    public sealed class GlassIconKey : UnityEngine.MonoBehaviour
    {
        public string Key;

        void OnEnable() { Apply(); }

        public void Apply()
        {
            var img = GetComponent<UnityEngine.UI.Image>();
            if (img == null || string.IsNullOrEmpty(Key)) return;
            if (img.sprite == null)
            {
                var s = GlassIcons.Get(Key);
                if (s != null) { img.sprite = s; img.enabled = true; }
            }
        }
    }
}

