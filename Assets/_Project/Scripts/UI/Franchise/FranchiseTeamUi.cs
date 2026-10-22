using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.UI.Franchise
{
    /// <summary>Team identity helpers: the crest sprite and the colour of a league team, and a crest + name chip builder.</summary>
    public static class TeamUi
    {
        public static Sprite Crest(LeagueTeam t) { return t == null || string.IsNullOrEmpty(t.Key) ? null : UiKit.Sprite("crest_" + t.Key); }

        public static Color Tint(LeagueTeam t, float alpha = 1f)
        {
            Color c;
            if (t == null || !ColorUtility.TryParseHtmlString(t.PrimaryColor, out c)) c = new Color(0.4f, 0.4f, 0.45f);
            c.a = alpha; return c;
        }

        public static Color Secondary(LeagueTeam t)
        {
            Color c;
            if (t == null || !ColorUtility.TryParseHtmlString(t.SecondaryColor, out c)) c = Color.white;
            return c;
        }

        public static Image CrestImage(RectTransform parent, LeagueTeam t, float size, float x, float y, string name = "Crest")
        {
            var img = UiKit.Panel(parent, name, Color.white, Crest(t)); img.preserveAspect = true; img.enabled = img.sprite != null;
            Lay.Place(img.rectTransform, x, y, size, size);
            return img;
        }

        public static string Abbr(LeagueTeam t) { return t == null ? "" : (string.IsNullOrEmpty(t.Abbreviation) ? t.Nickname.Substring(0, Math.Min(3, t.Nickname.Length)).ToUpperInvariant() : t.Abbreviation); }

        /// <summary>The conference and division a team plays in, "East" style (for the standings card title).</summary>
        public static string DivisionName(ProLeague lg, LeagueTeam t)
        {
            foreach (var c in lg.Conferences)
                foreach (var d in c.Divisions)
                    if (d.Id == t.DivisionId) { string pre = string.IsNullOrEmpty(c.Abbreviation) ? c.Name : c.Abbreviation; return d.Name.StartsWith(pre) ? d.Name : pre + " " + d.Name; }
            return "Division";
        }
    }

    /// <summary>A clickable glass tile (surface + button + focus look) that hosts whatever the page puts in <see cref="Body"/>. Used by the HOME status tiles.</summary>
    public static class TileKit
    {
        public static Button Tile(RectTransform parent, string name, float x, float y, float w, float h, Action onClick, out RectTransform body, float density = 1.1f)
        {
            var g = GlassKit.Panel(parent, name, density, true, GlassTheme.T.cornerRadiusLarge);
            Lay.Place(g.rectTransform, x, y, w, h);
            g.raycastTarget = true;
            var b = g.gameObject.AddComponent<Button>(); b.targetGraphic = g; b.transition = Selectable.Transition.None;
            b.onClick.AddListener(UiAudio.Click);
            if (onClick != null) b.onClick.AddListener(() => onClick());
            var f = g.gameObject.AddComponent<GlassFocusable>(); f.Surface = g; f.FocusScale = 1.02f; f.FocusBrighten = 0.05f; f.PlaySound = true;
            body = g.rectTransform;
            return b;
        }
    }
}
