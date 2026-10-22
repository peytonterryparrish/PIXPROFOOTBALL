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
    public partial class MainMenuController : MonoBehaviour
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
            GameSession.ResetTime();
            CareerSession.OnMenuLoaded();   // v0.8: the exhibition uses the standalone league again
            UiKit.EnsureEventSystem();
            BuildUi();
            BuildStage();
            StartMusic();
        }

        void Start()
        {
            if (_franchise != null) { GlassSection("FRANCHISE"); if (_stageView != null) _stageView.gameObject.SetActive(false); }   // the stage framing follows the open screen
            if (_menuRoot != null && _menuRoot.activeInHierarchy) StartCoroutine(SelectFirst(_firstButton != null ? _firstButton.gameObject : null));
        }

        IEnumerator SelectFirst(GameObject g)
        {
            yield return null;
            if (g != null && EventSystem.current != null) { UiAudio.Mute(0.25f); EventSystem.current.SetSelectedGameObject(g); }
        }

        void BuildUi()
        {
            if (UiTheme.IsGlass) { BuildGlassUi(); return; }
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
                var rule = UiKit.Panel(menu, "TagRule", UiColors.Gold); UiKit.Anchor(rule.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(586, -190), new Vector2(200, 6));
                var verTag = UiKit.Label(menu, "VERSION " + GameplayInfo.Version, 24, UiColors.TextSecondary, true, TextAlignmentOptions.TopLeft, "Build");
                UiKit.Anchor(verTag.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(586, -206), new Vector2(400, 40)); verTag.characterSpacing = 3f;
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
            var roam = UiKit.MenuButton(col, "FREE ROAM", () => StartWithFade(GameSession.StartFreeRoam), 640, 90, 40);
            var settingsBtn = UiKit.MenuButton(col, "SETTINGS", OpenSettings, 640, 90, 40);
            var quit = UiKit.MenuButton(col, "QUIT", ConfirmQuit, 640, 90, 40);
            _settingsBtn = settingsBtn;

            // one-line description of the highlighted entry (what each mode is)
            _desc = UiKit.Label(menu, "", 28, UiColors.TextSecondary, false, TextAlignmentOptions.TopLeft, "Desc");
            UiKit.Anchor(_desc.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(116, -950), new Vector2(640, 90));
            _desc.textWrappingMode = TextWrappingModes.Normal;
            Describe(_firstButton, "Pick two teams, the rules and the weather, then play a full game.");
            Describe(roam, "Run around the empty stadium: test the moves, the cameras and the lighting.");
            Describe(settingsBtn, "Video, gameplay, camera, broadcast and audio options.");
            Describe(quit, "Close the game.");

            // ---- settings ----
            _canvasRoot = root;
            _settings = SettingsPanel.Build(root, CloseSettings); _settings.OnReset = RebuildSettings;
            _settingsRoot = _settings.gameObject; _settingsRoot.SetActive(false);
            _teamSelect = TeamSelectPanel.Build(root, CloseTeamSelect, StartMatchWithFade);
            _teamRoot = _teamSelect.gameObject; _teamRoot.SetActive(false);

            // ---- footer prompts (controller glyphs or keycaps, whichever device is in use; BACK only where B does something) ----
            var bar = UiKit.Panel(root, "Footer", UiColors.BgPanelDark); UiKit.Anchor(bar.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 76)); bar.rectTransform.anchorMax = new Vector2(1, 0); bar.rectTransform.sizeDelta = new Vector2(0, 76);
            var row = UiKit.Rect(bar.rectTransform, "Prompts"); UiKit.Anchor(row, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(110, 0), new Vector2(1100, 56));
            _footer = PromptFooter.Attach(row, 48, 26, 48);
            SetFooter(false);
            var ver = UiKit.Label(bar.rectTransform, "PIX FOOTBALL  v" + GameplayInfo.Version, 24, UiColors.TextSecondary, false, TextAlignmentOptions.MidlineRight, "Version");
            UiKit.Anchor(ver.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-60, 0), new Vector2(600, 40));
        }

        Button _settingsBtn;
        Transform _canvasRoot;
        PromptFooter _footer;
        TextMeshProUGUI _desc;

        void SetFooter(bool withBack)
        {
            if (_footer == null) return;
            if (!withBack && UiTheme.IsGlass) { SetHubFooter(); return; }
            if (withBack) _footer.Set(PromptFooter.Item("glyph_a", "SELECT"), PromptFooter.Item("glyph_b", "BACK"), PromptFooter.Item("glyph_analog_all", "NAVIGATE"));
            else _footer.Set(PromptFooter.Item("glyph_a", "SELECT"), PromptFooter.Item("glyph_analog_all", "NAVIGATE"));
        }

        void Describe(Button b, string text)
        {
            var d = b.gameObject.AddComponent<MenuDescription>(); d.Target = _desc; d.Text = text;
        }

        /// <summary>"Reset all options" changed every stored value: build the settings pages again so they show the defaults.</summary>
        void RebuildSettings()
        {
            int idx = _settingsRoot.transform.GetSiblingIndex();
            Destroy(_settingsRoot);
            _settings = SettingsPanel.Build(_canvasRoot, CloseSettings); _settings.OnReset = RebuildSettings;
            _settingsRoot = _settings.gameObject; _settingsRoot.transform.SetSiblingIndex(idx);
            StartCoroutine(SelectFirst(_settings.First != null ? _settings.First.gameObject : null));
        }

        void OpenSettings() { OpenSettingsAt(0); }

        /// <summary>Opens the settings screen on page <paramref name="page"/> (the glass hub has one tile per page).</summary>
        void OpenSettingsAt(int page)
        {
            RememberOpener();
            GlassSection("SETTINGS");
            _settings.SetPage(page);
            _menuRoot.SetActive(false); _settingsRoot.SetActive(true); SetFooter(true);
            var first = _settings.TabFor(page) != null ? _settings.TabFor(page) : _settings.First;
            StartCoroutine(SelectFirst(first != null ? first.gameObject : null));
        }

        GameObject _opener;
        /// <summary>The control that opened a sub screen gets the focus back when it closes (the hub tile, not always the first one).</summary>
        void RememberOpener() { _opener = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null; }
        GameObject ReturnTarget(GameObject fallback) { return _opener != null && _opener.activeInHierarchy ? _opener : fallback; }

        // also driven by the autotest through SendMessage("OpenTeamSelect")
        void OpenTeamSelect()
        {
            RememberOpener();
            GlassSection("TEAM SELECT");
            _menuRoot.SetActive(false); _teamRoot.SetActive(true); SetFooter(true);
            StartCoroutine(SelectFirst(_teamSelect.First != null ? _teamSelect.First.gameObject : null));
        }

        void CloseTeamSelect()
        {
            GlassSection("MAIN MENU");
            _teamRoot.SetActive(false); _menuRoot.SetActive(true); SetFooter(false);
            StartCoroutine(SelectFirst(ReturnTarget(_firstButton != null ? _firstButton.gameObject : null)));
        }

        void CloseSettings()
        {
            GlassSection("MAIN MENU");
            PixSettings.Save(); PixSettings.Apply();
            _settingsRoot.SetActive(false); _menuRoot.SetActive(true); SetFooter(false);
            StartCoroutine(SelectFirst(ReturnTarget(_settingsBtn != null ? _settingsBtn.gameObject : null)));
        }

        /// <summary>QUIT asks first: one slip of A on the first column no longer closes the game (QA-802).</summary>
        void ConfirmQuit()
        {
            UiDialog.Show(_menuRoot.transform.parent, "Quit the game?", "Return to the desktop. Settings are already saved.", "QUIT", "STAY", GameSession.Quit, null);
        }

        void StartMatchWithFade(MatchConfig cfg) { StartWithFade(() => GameSession.StartMatch(cfg)); }

        /// <summary>The menu music fades out before the scene changes instead of cutting off (QA-801).</summary>
        void StartWithFade(System.Action load)
        {
            if (_leaving) return;
            _leaving = true;
            StartCoroutine(FadeAndLoad(load));
        }

        bool _leaving;

        IEnumerator FadeAndLoad(System.Action load)
        {
            float t = 0f, v0 = _music != null ? _music.volume : 0f;
            while (t < 0.45f)
            {
                t += Time.unscaledDeltaTime;
                if (_music != null) _music.volume = Mathf.Lerp(v0, 0f, t / 0.45f);
                yield return null;
            }
            load();
        }

        void BuildStage()
        {
            if (StageAnchor == null) return;
            var view = SimAgentView.Create(StageAnchor, PlayerSize.Medium, TeamColors.Home, "MenuPlayer");
            view.SetIdentity(12, PixFootball.Core.Position.QB);
            view.transform.localPosition = Vector3.zero; view.transform.localRotation = Quaternion.identity;
            view.SnapYaw(StageAnchor.eulerAngles.y);
            _stageView = view;
            if (UiTheme.IsGlass)
            {
                var st = PixFootball.UI.Glass.GlassStage.Attach(gameObject, StageAnchor, StageCamera != null ? StageCamera : Camera.main);
                st.CanvasRect = _canvasRoot as RectTransform;
                DressStage(_hubHomeId);   // the menu player wears the kit of the last home team
            }
        }

        SimAgentView _stageView;
        string _hubHomeId;

        /// <summary>The menu player puts on the kit (uniform and helmet in the team colours) and the face of the quarterback of the given team. Public APIs of the player only.</summary>
        void DressStage(string teamId)
        {
            if (_stageView == null || string.IsNullOrEmpty(teamId)) return;
            var ti = TeamCatalog.ById(teamId);
            if (ti == null) return;
            var c = ti.Colors;
            _stageView.Dress(c, c);
            try
            {
                var lt = MatchData.TeamByKey(teamId);
                int qb = lt != null ? lt.Depth.StarterId(PixFootball.Core.Position.QB) : 0;
                _stageView.SetIdentity(qb > 0 ? qb : 12, PixFootball.Core.Position.QB);
            }
            catch (System.Exception e) { Debug.LogWarning("[PIX] menu stage identity: " + e.Message); }
        }

        void StartMusic()
        {
            var rt = PixRuntimeAssets.Load();
            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = false; _music.playOnAwake = false; _music.spatialBlend = 0f;
            if (rt != null && rt.menuMusic.Count > 0) NextTrack();
            else Debug.LogWarning("[PIX] No menu music in PixRuntimeAssets (build the runtime assets): the main menu is silent.");
        }

        float _trackEndsAt, _trackStart;

        void NextTrack()
        {
            var rt = PixRuntimeAssets.Load();
            if (rt == null || rt.menuMusic.Count == 0) return;
            _track = (_track + 1 + (_track < 0 ? Random.Range(0, rt.menuMusic.Count) : 0)) % rt.menuMusic.Count;
            _music.clip = rt.menuMusic[_track]; _music.volume = 0f; _music.Play();
            _trackStart = Time.unscaledTime; _trackEndsAt = Time.unscaledTime + rt.menuMusic[_track].length;
        }

        void Update()
        {
            if (_music == null || _leaving) return;
            // the next track starts when this one has run its time, not whenever isPlaying reads false (a focus loss or an audio device reset must not skip a song)
            if (_music.clip != null && Time.unscaledTime >= _trackEndsAt + 0.25f) NextTrack();
            float fadeIn = Mathf.Clamp01((Time.unscaledTime - _trackStart) / 1.5f);
            _music.volume = PixSettings.MusicVolume * fadeIn;
        }
    }

    /// <summary>Shows a one-line description for a menu row while it is highlighted.</summary>
    public sealed class MenuDescription : MonoBehaviour, UnityEngine.EventSystems.ISelectHandler, UnityEngine.EventSystems.IPointerEnterHandler
    {
        public TextMeshProUGUI Target; public string Text;
        public void OnSelect(UnityEngine.EventSystems.BaseEventData e) { if (Target != null) Target.text = Text; }
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { if (Target != null) Target.text = Text; }
    }
}
