using System.Linq;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>Pause menu: Resume, Camera, Lighting (Day/Night), Team, Quit to Menu. Opened by the Start/Esc button through FreeRoamGame.</summary>
    public class PauseMenu : MonoBehaviour, ICancelHandler
    {
        GameObject _root;
        Button _resume;
        OptionSelector _cam, _light, _team;
        FreeRoamGame _game;

        void Start()
        {
            _game = FreeRoamGame.Instance;
            UiKit.EnsureEventSystem();
            var canvas = UiKit.CreateCanvas("PauseCanvas", 50, transform);
            var root = (RectTransform)canvas.transform;
            _root = canvas.gameObject;

            var scrim = UiKit.Panel(root, "Scrim", UiColors.Scrim); UiKit.Stretch(scrim.rectTransform);
            var sp = UiKit.Sprite("bg_pause");
            if (sp != null) { var o = UiKit.Panel(root, "Overlay", new Color(1, 1, 1, 0.5f), sp); UiKit.Stretch(o.rectTransform); }
            var rail = UiKit.Panel(root, "Rail", new Color(0.05f, 0.06f, 0.1f, 0.94f));
            UiKit.Anchor(rail.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(860, 0)); rail.rectTransform.anchorMax = new Vector2(0, 1); rail.rectTransform.sizeDelta = new Vector2(860, 0);
            var edge = UiKit.Panel(root, "RailEdge", UiColors.AccentBlue);
            UiKit.Anchor(edge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(860, 0), new Vector2(4, 0)); edge.rectTransform.anchorMax = new Vector2(0, 1); edge.rectTransform.sizeDelta = new Vector2(4, 0);

            var title = UiKit.Label(root, "PAUSED", 120, UiColors.TextPrimary, true, TextAlignmentOptions.BottomLeft, "Title");
            UiKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(100, -80), new Vector2(700, 140)); title.characterSpacing = 4f;
            var rule = UiKit.Panel(root, "Rule", UiColors.Gold); UiKit.Anchor(rule.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(108, -228), new Vector2(220, 8));

            var col = UiKit.Rect(root, "Items");
            UiKit.Anchor(col, new Vector2(0, 1), new Vector2(0, 1), new Vector2(100, -270), new Vector2(720, 640));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 14; v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            _resume = UiKit.MenuButton(col, "RESUME", Resume, 720, 84, 38);
            _cam = UiControls.Selector(col, "Camera", new[] { "Broadcast", "Behind player", "First person" }, 0, i => { if (_game != null && _game.Cameras != null) { _game.Cameras.SetMode((CameraMode)i, false); PixSettings.DefaultCamera = (CameraMode)i; } }, 720, 84);
            _light = UiControls.Selector(col, "Lighting", new[] { "Night game", "Day game" }, 0, i => { if (_game != null && _game.Lighting != null) _game.Lighting.Apply((LightingPreset)i); }, 720, 84);
            var names = TeamCatalog.Teams.Select(t => t.Name).ToArray();
            _team = UiControls.Selector(col, "Uniform", names, TeamCatalog.IndexOf(TeamCatalog.SavedTeamId), i => { if (_game != null) _game.SetTeam(TeamCatalog.Teams[i]); }, 720, 84);
            UiKit.MenuButton(col, "QUIT TO MENU", GameSession.QuitToMenu, 720, 84, 38);

            var bar = UiKit.Panel(root, "Footer", UiColors.BgPanelDark); UiKit.Anchor(bar.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 76)); bar.rectTransform.anchorMax = new Vector2(1, 0); bar.rectTransform.sizeDelta = new Vector2(0, 76);
            var row = UiKit.Rect(bar.rectTransform, "Prompts"); UiKit.Anchor(row, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(100, 0), new Vector2(1100, 56));
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 48; h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
            UiKit.FooterPrompt(row, "glyph_a", "SELECT", 48); UiKit.FooterPrompt(row, "glyph_b", "RESUME", 48); UiKit.FooterPrompt(row, "glyph_analog_all", "NAVIGATE", 56);

            var cancel = root.gameObject.AddComponent<CancelRelay>(); cancel.Target = this;
            _root.SetActive(false);
            if (_game != null) _game.PauseChanged += OnPause;
        }

        void OnDestroy() { if (_game != null) _game.PauseChanged -= OnPause; }

        void OnPause(bool paused)
        {
            _root.SetActive(paused);
            if (!paused) return;
            if (_game.Cameras != null) _cam.SetOptions(_cam.Options, (int)_game.Cameras.Mode);
            if (_game.Lighting != null) _light.SetOptions(_light.Options, (int)_game.Lighting.Current);
            _team.SetOptions(_team.Options, TeamCatalog.IndexOf(TeamCatalog.SavedTeamId));
            StartCoroutine(SelectResume());
        }

        System.Collections.IEnumerator SelectResume()
        {
            yield return null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(_resume.gameObject);
        }

        void Resume() { if (_game != null) _game.SetPaused(false); }

        public void OnCancel(BaseEventData e) { Resume(); }

        sealed class CancelRelay : MonoBehaviour, ICancelHandler
        {
            public PauseMenu Target;
            public void OnCancel(BaseEventData e) { if (Target != null) Target.Resume(); }
        }
    }
}
