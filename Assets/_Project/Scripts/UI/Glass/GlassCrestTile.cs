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
public sealed class GlassCrestTile : MonoBehaviour
{
    public Button Button; public Image Crest; public TextMeshProUGUI Name, Tag; public GlassGraphic Surface, Wash; public GlassGraphic TagChip;
    public int Index;
    public void SetRole(string role, bool accent)
    {
        if (TagChip == null) return;
        TagChip.gameObject.SetActive(!string.IsNullOrEmpty(role));
        if (Tag != null) { Tag.text = role ?? ""; Tag.color = accent ? GlassTheme.T.accent : GlassTheme.T.textPrimary; }
        TagChip.GlowScale = 0.25f; TagChip.SetFocus(accent ? 0.9f : 0f);
    }
}
}

