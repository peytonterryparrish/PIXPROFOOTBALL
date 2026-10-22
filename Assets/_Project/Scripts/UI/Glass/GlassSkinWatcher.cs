using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace PixFootball.UI.Glass
{
    /// <summary>Keeps reskinning a screen whose panels are created lazily (pre-snap rows and chips): re-applies <see cref="GlassSkin"/> a few times a second, only while the Glass theme is active.</summary>
    public sealed class GlassSkinWatcher : MonoBehaviour
    {
        public float Interval = 0.2f;
        float _next;

        public static void Watch(GameObject root)
        {
            if (root == null || !UiTheme.IsGlass) return;
            if (root.GetComponent<GlassSkinWatcher>() == null) root.AddComponent<GlassSkinWatcher>();
            GlassSkin.Apply(root.transform);
        }

        void LateUpdate()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + Interval;
            GlassSkin.Apply(transform);
        }
    }
}

