using System.Collections;
using PixFootball.Art;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>Main menu (UI-3 style: dark navy, blue + gold accents): Free Roam, Settings, Quit; menu music; 3D player stage.</summary>
    public class MainMenuController : MonoBehaviour
    {
        public Transform StageAnchor;
        public Camera StageCamera;
        GameObject _menuRoot, _settingsRoot, _teamRoot;
        TeamSelectPanel _teamSelect;
        SettingsPanel _settings;
        Button _firstButton;
        AudioSource _music;
        int _track = -1;

        void Awake()
        {
            GameSession.SetPaused(false);
            UiKit.EnsureEventSystem();
            BuildUi();
            BuildStage();
            StartMusic();
        }

        void Start() { StartCoroutine(SelectFirst(_firstButton != null ? _firstButton.gameObject : null)); }

        IEnumerator SelectFirst(GameObject g)
        {
            yield return null;
            if (g != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(g);
        }

        void BuildUi()
        {
            var canvas = UiKit.CreateCanvas("MenuCanvas", 10, transform);
            var root = (RectTransform)canvas.transform;

            // background wash: fog art tinted navy, darker on the left where the menu sits
            var left = UiKit.Panel(root, "LeftShade", new Color(0.03f, 0.04f, 0.08f, 0.92f));
            UiKit.Anchor(left.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(1000, 0)); left.rectTransform.anchorMax = new Vector2(0, 1); left.rectTransform.sizeDelta = new Vector2(1000, 0);
            var edge = UiKit.Panel(root, "LeftEdge", UiColors.AccentBlue);
            UiKit.Anchor(edge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(1000, 0), new Vector2(4, 0)); edge.rectTransform.anchorMax = new Vector2(0, 1); edge.rectTransform.sizeDelta = new Vector2(4, 0);

            // ---- main menu column ----
            var menu = UiKit.Rect(root, "MainMenu"); UiKit.Stretch(menu); _menuRoot = menu.gameObject;
            var logoSprite = UiKit.Sprite("logo_league");
            if (logoSprite != null)
            {
                var logo = UiKit.Panel(menu, "Logo", Color.white, logoSprite); logo.preserveAspect = true;
                UiKit.Anchor(logo.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(110, -44), new Vector2(430, 442));
                var tag = UiKit.Label(menu, "PLAY NOW", 52, UiColors.Gold, true, TextAlignmentOptions.TopLeft, "Mode");
                UiKit.Anchor(tag.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(580, -150), new Vector2(400, 70)); tag.characterSpacing = 6f;
                var rule = UiKit.Panel(menu, "TagRule", UiColors.Gold); UiKit.Anchor(rule.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(586, -222), new Vector2(200, 6));
                var verTag = UiKit.Label(menu, "v" + GameplayInfo.Version + "  /  KICKOFF", 24, UiColors.TextSecondary, true, TextAlignmentOptions.TopLeft, "Build");
                UiKit.Anchor(verTag.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(586, -238), new Vector2(400, 40)); verTag.characterSpacing = 3f;
            }
            else
            {
                var title = UiKit.Label(menu, GameplayInfo.Title, 168, UiColors.TextPrimary, true, TextAlignmentOptions.BottomLeft, "Title");
                UiKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(110, -150), new Vector2(880, 190));
                title.characterSpacing = 4f;
                var rule = UiKit.Panel(menu, "TitleRule", UiColors.Gold); UiKit.Anchor(rule.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(118, -352), new Vector2(260, 9));
            }

            var col = UiKit.Rect(menu, "Buttons");
            UiKit.Anchor(col, new Vector2(0, 1f), new Vector2(0, 1f), new Vector2(110, -520), new Vector2(640, 480));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 14; v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            _firstButton = UiKit.MenuButton(col, "PLAY NOW", OpenTeamSelect, 640, 90, 40);
            UiKit.MenuButton(col, "FREE ROAM", () => GameSession.StartFreeRoam(), 640, 90, 40);
            var settingsBtn = UiKit.MenuButton(col, "SETTINGS", OpenSettings, 640, 90, 40);
            UiKit.MenuButton(col, "QUIT", GameSession.Quit, 640, 90, 40);
            _settingsBtn = settingsBtn;

            // ---- settings ----
            _settings = SettingsPanel.Build(root, CloseSettings);
            _settingsRoot = _settings.gameObject; _settingsRoot.SetActive(false);
            _teamSelect = TeamSelectPanel.Build(root, CloseTeamSelect);
            _teamRoot = _teamSelect.gameObject; _teamRoot.SetActive(false);

            // ---- footer prompts ----
            var bar = UiKit.Panel(root, "Footer", UiColors.BgPanelDark); UiKit.Anchor(bar.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 76)); bar.rectTransform.anchorMax = new Vector2(1, 0); bar.rectTransform.sizeDelta = new Vector2(0, 76);
            var row = UiKit.Rect(bar.rectTransform, "Prompts"); UiKit.Anchor(row, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(110, 0), new Vector2(1100, 56));
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 48; h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
            UiKit.FooterPrompt(row, "glyph_a", "SELECT", 48); UiKit.FooterPrompt(row, "glyph_b", "BACK", 48); UiKit.FooterPrompt(row, "glyph_analog_all", "NAVIGATE", 56);
            var ver = UiKit.Label(bar.rectTransform, "PIX FOOTBALL  v" + GameplayInfo.Version, 24, UiColors.TextMuted, false, TextAlignmentOptions.MidlineRight, "Version");
            UiKit.Anchor(ver.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-60, 0), new Vector2(600, 40));
        }

        Button _settingsBtn;

        void OpenSettings()
        {
            _menuRoot.SetActive(false); _settingsRoot.SetActive(true);
            StartCoroutine(SelectFirst(_settings.First != null ? _settings.First.gameObject : null));
        }

        void OpenTeamSelect()
        {
            _menuRoot.SetActive(false); _teamRoot.SetActive(true);
            StartCoroutine(SelectFirst(_teamSelect.First != null ? _teamSelect.First.gameObject : null));
        }

        void CloseTeamSelect()
        {
            _teamRoot.SetActive(false); _menuRoot.SetActive(true);
            StartCoroutine(SelectFirst(_firstButton != null ? _firstButton.gameObject : null));
        }

        void CloseSettings()
        {
            PixSettings.Save(); PixSettings.Apply();
            _settingsRoot.SetActive(false); _menuRoot.SetActive(true);
            StartCoroutine(SelectFirst(_settingsBtn != null ? _settingsBtn.gameObject : null));
        }

        void BuildStage()
        {
            if (StageAnchor == null) return;
            var view = SimAgentView.Create(StageAnchor, PlayerSize.Medium, TeamColors.Home, "MenuPlayer");
            view.transform.localPosition = Vector3.zero; view.transform.localRotation = Quaternion.identity;
            view.SnapYaw(StageAnchor.eulerAngles.y);
        }

        void StartMusic()
        {
            var rt = PixRuntimeAssets.Load();
            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = false; _music.playOnAwake = false; _music.spatialBlend = 0f;
            if (rt != null && rt.menuMusic.Count > 0) NextTrack();
        }

        void NextTrack()
        {
            var rt = PixRuntimeAssets.Load();
            if (rt == null || rt.menuMusic.Count == 0) return;
            _track = (_track + 1 + (_track < 0 ? Random.Range(0, rt.menuMusic.Count) : 0)) % rt.menuMusic.Count;
            _music.clip = rt.menuMusic[_track]; _music.volume = PixSettings.MusicVolume; _music.Play();
        }

        void Update()
        {
            if (_music != null)
            {
                _music.volume = PixSettings.MusicVolume;
                if (_music.clip != null && !_music.isPlaying) NextTrack();
            }
        }
    }
}
