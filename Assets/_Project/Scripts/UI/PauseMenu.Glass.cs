using System.Linq;
using PixFootball.Gameplay;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>Glass pause menu of free roam: a light scrim over the (blurred) stadium, the nav bar, and one dense glass card with the same rows as Classic.</summary>
    public partial class PauseMenu
    {
        void BuildGlass(RectTransform root)
        {
            var t = GlassTheme.T;
            var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.025f, 0.035f, 0.36f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            GlassWidgets.SideShade(root, true, 1100f, 0.55f);
            GlassWidgets.TopBar(root, "PAUSED");

            var card = GlassKit.Panel(root, "PauseCard", 1.2f, true);
            var cr = card.rectTransform; UiKit.Anchor(cr, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(t.safeMarginX, -150f), new Vector2(780f, 690f));
            var v = cr.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(28, 28, 22, 24); v.spacing = 12f; v.childAlignment = TextAnchor.UpperLeft; v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = false; v.childForceExpandHeight = false;

            var title = GlassKit.Text(cr, "PAUSED", GlassText.Title, TextAlignmentOptions.MidlineLeft, "Title"); GlassKit.Style(title, 52f, t.textPrimary, true, t.trackingTitle);
            GlassKit.Size(title.gameObject, 724f, 70f);
            var rule = GlassKit.Rule(cr); rule.gameObject.GetComponent<LayoutElement>().preferredWidth = 724f;
            _resume = GlassKit.Button(cr, "RESUME", Resume, GlassButtonStyle.Primary, 724f, 68f, "ui_arrow_right");
            const float w = 724f, h = 62f;
            _cam = UiControls.Selector(cr, "Camera", new[] { "Broadcast", "Behind player", "First person" }, 0, i => { if (Game != null && Game.Cameras != null) Game.Cameras.SetMode((CameraMode)i, false); }, w, h);
            _light = UiControls.Selector(cr, "Lighting", new[] { "Night game", "Day game" }, 0, i => { if (_game != null && _game.Lighting != null) _game.Lighting.Apply((LightingPreset)i); }, w, h);
            var names = TeamCatalog.Teams.Select(tm => tm.Name).ToArray();
            _team = UiControls.Selector(cr, "Uniform", names, TeamCatalog.IndexOf(TeamCatalog.SavedTeamId), i => { if (_game != null) _game.SetTeam(TeamCatalog.Teams[i]); }, w, h);
            UiControls.SliderRow(cr, "Master volume", PixSettings.MasterVolume, x => { PixSettings.MasterVolume = x; AudioListener.volume = x; }, w, h);
            UiControls.SliderRow(cr, "Crowd volume", PixSettings.CrowdVolume, x => { PixSettings.CrowdVolume = x; }, w, h);
            GlassKit.Button(cr, "QUIT TO MENU", () => UiDialog.Show(root, "Quit to menu?", "Leave free roam and return to the main menu.", "QUIT", "STAY", GameSession.QuitToMenu, null), GlassButtonStyle.Secondary, 724f, 64f, "ui_close");

            var foot = GlassWidgets.BottomBar(root, null);
            foot.Prompts.Set(PromptFooter.Item("glyph_a", "SELECT"), PromptFooter.Item("glyph_b", "RESUME"), PromptFooter.Item("glyph_analog_all", "NAVIGATE"));
        }
    }
}
