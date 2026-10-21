using System.Collections.Generic;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>In-game HUD: speed (mph) + stamina, team badge, camera/lighting readout and the controls legend (Xbox glyphs, keyboard keys when the keyboard is active).</summary>
    public class FreeRoamHud : MonoBehaviour
    {
        struct Row { public GameObject Glyph; public GameObject Key; }
        readonly List<Row> _rows = new List<Row>();
        TextMeshProUGUI _speed, _cam, _team;
        Image _stamina, _crest, _staminaBack;
        string _scheme = "";
        FreeRoamGame _game;
        Canvas _canvas;

        static readonly string[][] Controls =
        {
            // glyph key, keyboard text, label
            new[] { "glyph_ls", "WASD", "MOVE" },
            new[] { "glyph_rt", "SHIFT", "SPRINT" },
            new[] { "glyph_a", "SPACE", "HURDLE" },
            new[] { "glyph_b", "F", "SPIN" },
            new[] { "glyph_x", "C", "DIVE" },
            new[] { "glyph_y", "R", "TRUCK" },
            new[] { "glyph_rs", "Q / E", "JUKE  (FLICK RS)" },
            new[] { "glyph_lb", "Z", "STIFF-ARM LEFT" },
            new[] { "glyph_rb", "X", "STIFF-ARM RIGHT" },
            new[] { "glyph_view", "TAB", "CAMERA" },
            new[] { "glyph_menu", "ESC", "PAUSE" },
        };

        void Start()
        {
            _game = FreeRoamGame.Instance;
            _canvas = UiKit.CreateCanvas("HudCanvas", 5, transform);
            var root = (RectTransform)_canvas.transform;

            // ---- speed panel (top-left) ----
            var panel = UiKit.Panel(root, "SpeedPanel", new Color(0.06f, 0.07f, 0.1f, 0.78f));
            UiKit.Anchor(panel.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -48), new Vector2(520, 150));
            var edge = UiKit.Panel(panel.rectTransform, "Edge", UiColors.Gold); UiKit.Anchor(edge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(6, 150)); edge.rectTransform.anchorMax = new Vector2(0, 1); edge.rectTransform.sizeDelta = new Vector2(6, 0);
            _crest = UiKit.Panel(panel.rectTransform, "Crest", Color.white); _crest.preserveAspect = true; _crest.enabled = false;
            UiKit.Anchor(_crest.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(26, 0), new Vector2(110, 110));
            _speed = UiKit.Label(panel.rectTransform, "0.0", 84, UiColors.TextPrimary, true, TextAlignmentOptions.MidlineRight, "Speed");
            UiKit.Anchor(_speed.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-160, -4), new Vector2(210, 100));
            var unit = UiKit.Label(panel.rectTransform, "MPH", 34, UiColors.Gold, true, TextAlignmentOptions.MidlineLeft, "Unit");
            UiKit.Anchor(unit.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-18, -10), new Vector2(130, 50));
            _team = UiKit.Label(panel.rectTransform, "", 22, UiColors.TextSecondary, true, TextAlignmentOptions.MidlineLeft, "Team");
            UiKit.Anchor(_team.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(150, -6), new Vector2(300, 30));
            _staminaBack = UiKit.Panel(panel.rectTransform, "StaminaBack", new Color(1, 1, 1, 0.14f));
            UiKit.Anchor(_staminaBack.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(150, 14), new Vector2(350, 10));
            _stamina = UiKit.Panel(_staminaBack.rectTransform, "Stamina", UiColors.AccentBlueHot); _stamina.rectTransform.anchorMin = Vector2.zero; _stamina.rectTransform.anchorMax = new Vector2(1, 1); _stamina.rectTransform.offsetMin = _stamina.rectTransform.offsetMax = Vector2.zero;

            // ---- camera / lighting (top-right) ----
            _cam = UiKit.Label(root, "", 28, UiColors.TextSecondary, true, TextAlignmentOptions.TopRight, "Cam");
            UiKit.Anchor(_cam.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-60, -52), new Vector2(700, 80));

            // ---- controls legend (bottom-left) ----
            var legend = UiKit.Panel(root, "Controls", new Color(0.06f, 0.07f, 0.1f, 0.62f));
            float rowH = 44f;
            UiKit.Anchor(legend.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(60, 48), new Vector2(430, 36 + rowH * Controls.Length));
            var lay = UiKit.Rect(legend.rectTransform, "Rows"); UiKit.Stretch(lay, 18, 14, 14, 14);
            var v = lay.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 0; v.childControlWidth = true; v.childControlHeight = false; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            foreach (var c in Controls)
            {
                var row = UiKit.Rect(lay, "Row_" + c[2]); row.sizeDelta = new Vector2(0, rowH);
                var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = rowH;
                var g = UiKit.Glyph(row, c[0], 38f);
                var gr = g.rectTransform; UiKit.Anchor(gr, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), gr.sizeDelta);
                var kb = UiKit.Panel(row, "Key", new Color(0.9f, 0.9f, 0.92f, 1f)); UiKit.Anchor(kb.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(100, 34));
                var kt = UiKit.Label(kb.rectTransform, c[1], 22, new Color(0.08f, 0.09f, 0.12f), true, TextAlignmentOptions.Center, "KeyText"); UiKit.Stretch(kt.rectTransform);
                var lbl = UiKit.Label(row, c[2], 25, UiColors.TextPrimary, true, TextAlignmentOptions.MidlineLeft, "Text");
                UiKit.Anchor(lbl.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(120, 0), new Vector2(280, rowH));
                _rows.Add(new Row { Glyph = g.gameObject, Key = kb.gameObject });
            }
            RefreshScheme(true);
            if (_game != null) { _game.TeamChanged += OnTeam; OnTeam(_game.Team); }
        }

        void OnDestroy() { if (_game != null) _game.TeamChanged -= OnTeam; }

        void OnTeam(TeamInfo t)
        {
            if (t == null) return;
            if (_team != null) _team.text = t.Name.ToUpperInvariant();
            var s = UiKit.Sprite("crest_" + t.Id);
            if (_crest != null) { _crest.sprite = s; _crest.enabled = s != null; }
        }

        void RefreshScheme(bool force)
        {
            string s = PixInput.ActiveScheme;
            if (!force && s == _scheme) return;
            _scheme = s;
            bool pad = s == "Gamepad";
            foreach (var r in _rows) { r.Glyph.SetActive(pad); r.Key.SetActive(!pad); }
        }

        void Update()
        {
            if (_game == null || _game.Player == null) return;
            RefreshScheme(false);
            if (_team != null && string.IsNullOrEmpty(_team.text) && _game.Team != null) OnTeam(_game.Team);
            var p = _game.Player;
            _speed.text = p.SpeedMph.ToString("0.0");
            _stamina.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(p.Energy01), 1f);
            if (_game.Cameras != null && _game.Lighting != null)
                _cam.text = (_game.Cameras.Mode == CameraMode.Broadcast ? "BROADCAST CAM" : _game.Cameras.Mode == CameraMode.Behind ? "BEHIND PLAYER" : "FIRST PERSON") + "  /  " + (_game.Lighting.Current == LightingPreset.Night ? "NIGHT GAME" : "DAY GAME");
            _canvas.enabled = !_game.Paused;
        }
    }
}
