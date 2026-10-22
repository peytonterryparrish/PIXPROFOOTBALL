using PixFootball.Art;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// Palette from Docs/UIStyleGuide.md (Classic) and Docs/UIStyleGuide_Glass.md (Glass). The tokens are properties: a screen that still reads <c>UiColors.Gold</c> gets the glass accent orange while
    /// the glass theme is active, so the parts that were not rebuilt still sit inside the one palette.
    /// </summary>
    public static class UiColors
    {
        static readonly Color cBgDeep = Hex("#11141C"), cBgNav = Hex("#1A252C"), cBgPanel = Hex("#1E1F28"), cBgPanelDark = Hex("#15161E"),
            cRow = Hex("#2A2B37"), cRowHover = Hex("#34364A"), cAccentBlue = Hex("#2D5ABC"), cAccentBlueHot = Hex("#2D6AED"), cAccentBlueDeep = Hex("#1D4190"),
            cGold = Hex("#FFC837"), cOrange = Hex("#FF9000"), cDanger = Hex("#C20000"), cDisabled = Hex("#333333"), cTextPrimary = Color.white,
            cTextSecondary = Hex("#C5C5C5"), cTextMuted = Hex("#6D6D6D"), cScrim = new Color(0.02f, 0.03f, 0.05f, 0.82f);
        static readonly Color gBgDeep = Hex("#151618"), gBgNav = Hex("#1B1C1E"), gBgPanel = Hex("#232326"), gBgPanelDark = Hex("#19191B"), gRow = Hex("#2A2B2E"), gRowHover = Hex("#36373B"),
            gAccent = Hex("#F49A52"), gAccentHot = Hex("#FFB070"), gAccentDeep = Hex("#C9722F"), gDanger = Hex("#E2685C"), gDisabled = Hex("#3A3B3E"), gTextPrimary = Hex("#F5F5F5"),
            gTextSecondary = Hex("#BFC1C4"), gTextMuted = Hex("#8E9094"), gScrim = new Color(0.02f, 0.025f, 0.035f, 0.74f);

        static bool G { get { return UiTheme.IsGlass; } }
        public static Color BgDeep { get { return G ? gBgDeep : cBgDeep; } }
        public static Color BgNav { get { return G ? gBgNav : cBgNav; } }
        public static Color BgPanel { get { return G ? gBgPanel : cBgPanel; } }
        public static Color BgPanelDark { get { return G ? gBgPanelDark : cBgPanelDark; } }
        public static Color Row { get { return G ? gRow : cRow; } }
        public static Color RowHover { get { return G ? gRowHover : cRowHover; } }
        public static Color AccentBlue { get { return G ? gAccent : cAccentBlue; } }
        public static Color AccentBlueHot { get { return G ? gAccentHot : cAccentBlueHot; } }
        public static Color AccentBlueDeep { get { return G ? gAccentDeep : cAccentBlueDeep; } }
        public static Color Gold { get { return G ? gAccent : cGold; } }
        public static Color Orange { get { return G ? gAccent : cOrange; } }
        public static Color Danger { get { return G ? gDanger : cDanger; } }
        public static Color Disabled { get { return G ? gDisabled : cDisabled; } }
        public static Color TextPrimary { get { return G ? gTextPrimary : cTextPrimary; } }
        public static Color TextSecondary { get { return G ? gTextSecondary : cTextSecondary; } }
        public static Color TextMuted { get { return G ? gTextMuted : cTextMuted; } }
        public static Color Scrim { get { return G ? gScrim : cScrim; } }

        public static Color Hex(string h) { Color c; ColorUtility.TryParseHtmlString(h, out c); return c; }
    }

    /// <summary>Code-built UI helpers: flat panels, TextMeshPro labels (Saira Condensed / Barlow), Xbox glyphs.</summary>
    public static class UiKit
    {
        /// <summary>The Classic title font (Saira Condensed Bold); the glass theme draws with DejaVu Sans Bold instead (see <see cref="Title"/>).</summary>
        public static TMP_FontAsset ClassicTitle { get { var rt = PixRuntimeAssets.Load(); return rt != null ? rt.fontTitle as TMP_FontAsset : null; } }
        public static TMP_FontAsset ClassicBody { get { var rt = PixRuntimeAssets.Load(); return rt != null ? rt.fontBody as TMP_FontAsset : null; } }
        public static TMP_FontAsset Title { get { return UiTheme.IsGlass ? PixFootball.UI.Glass.GlassTheme.Current.Title : ClassicTitle; } }
        public static TMP_FontAsset Body { get { return UiTheme.IsGlass ? PixFootball.UI.Glass.GlassTheme.Current.Body : ClassicBody; } }
        public static Sprite Sprite(string key) { var rt = PixRuntimeAssets.Load(); return rt != null ? rt.Sprite(key) : null; }

        public static Canvas CreateCanvas(string name, int sortOrder, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = sortOrder;
            var s = go.AddComponent<CanvasScaler>();
            // v0.7 (note 2): "Expand" keeps the whole 1920 x 1080 reference rectangle on screen at every aspect (nothing cut off at the bottom of a 21:9 / 16:10 / windowed screen); the canvas grows past it on the other axis
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; s.referenceResolution = new Vector2(1920, 1080); s.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            PixFootball.UI.Glass.GlassGraphic.Prepare(c);   // the glass shader reads texcoord 1..3
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform r, float l = 0, float b = 0, float rt = 0, float t = 0)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = new Vector2(l, b); r.offsetMax = new Vector2(-rt, -t);
        }

        public static void Anchor(RectTransform r, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
        }

        /// <summary>GetComponent-or-AddComponent. (The Editor hands out a "fake null" for a missing component, which `??` does not treat as null: always go through TryGetComponent.)</summary>
        public static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T c;
            if (!go.TryGetComponent(out c)) c = go.AddComponent<T>();
            return c;
        }

        /// <summary>Destroys an object: deferred in play, immediate in edit-mode tools.</summary>
        public static void Kill(GameObject go) { if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go); }

        public static Image Panel(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var r = Rect(parent, name);
            var img = r.gameObject.AddComponent<Image>();
            img.color = color; img.sprite = sprite; img.raycastTarget = false;
            return img;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, bool title = false, TextAlignmentOptions align = TextAlignmentOptions.Left, string name = "Label")
        {
            var r = Rect(parent, name);
            var t = r.gameObject.AddComponent<TextMeshProUGUI>();
            var f = title ? Title : Body;
            if (f != null) t.font = f;
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
            return t;
        }

        /// <summary>Shrinks the text to fit its rect (down to <paramref name="minSize"/>), then truncates with an ellipsis (long team names, play names, ticker lines: QA-765).</summary>
        public static TextMeshProUGUI Fit(TextMeshProUGUI t, float minSize)
        {
            if (t == null) return null;
            float size = t.fontSize; t.fontSizeMax = size; t.fontSizeMin = Mathf.Min(minSize, size); t.enableAutoSizing = true; t.overflowMode = TextOverflowModes.Ellipsis;
            return t;
        }

        public static Image Glyph(Transform parent, string key, float height)
        {
            var s = Sprite(key);
            var r = Rect(parent, "Glyph_" + key);
            var img = r.gameObject.AddComponent<Image>();
            img.sprite = s; img.preserveAspect = true; img.raycastTarget = false; img.enabled = s != null;
            float aspect = s != null ? s.rect.width / s.rect.height : 1f;
            r.sizeDelta = new Vector2(height * aspect, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = height; le.preferredWidth = height * aspect;
            return img;
        }

        /// <summary>Flat menu button in the UI-3 style: dark row, blue highlight with gold accent bar.</summary>
        public static Button MenuButton(Transform parent, string label, System.Action onClick, float width = 560f, float height = 76f, float fontSize = 34f)
        {
            if (UiTheme.IsGlass) return PixFootball.UI.Glass.GlassKit.Button(parent, label, onClick, PixFootball.UI.Glass.GlassButtonStyle.Secondary, width, height, null, "Btn_" + label, Mathf.Clamp(fontSize * 0.78f, 22f, 34f));
            var r = Rect(parent, "Btn_" + label);
            r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var bg = r.gameObject.AddComponent<Image>(); bg.color = UiColors.Row;
            var btn = r.gameObject.AddComponent<Button>();
            btn.targetGraphic = bg;
            var cb = btn.colors;
            cb.normalColor = Color.white; cb.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f); cb.selectedColor = Color.white; cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f); cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            cb.fadeDuration = 0.08f;
            btn.colors = cb;
            var bar = Panel(r, "Accent", UiColors.Gold); Anchor(bar.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(6, height)); bar.gameObject.SetActive(false);
            var t = Label(r, label, fontSize, UiColors.TextPrimary, true, TextAlignmentOptions.MidlineLeft, "Text");
            Stretch(t.rectTransform, 32, 0, 12, 0);
            var sel = r.gameObject.AddComponent<MenuItemVisual>(); sel.Background = bg; sel.Accent = bar.gameObject; sel.Text = t;
            btn.onClick.AddListener(UiAudio.Click);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        public static void FooterPrompt(Transform parent, string glyphKey, string label, float height = 44f)
        {
            var row = Rect(parent, "Prompt_" + label);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 10; h.childAlignment = TextAnchor.MiddleLeft; h.childControlHeight = true; h.childControlWidth = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            Glyph(row, glyphKey, height);
            var t = Label(row, label, 26, UiColors.TextSecondary, false, TextAlignmentOptions.MidlineLeft);
            var le = t.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = height; le.minWidth = 20;
            t.enableAutoSizing = false;
            var fit = t.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var rf = row.gameObject.AddComponent<ContentSizeFitter>(); rf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; rf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var m = go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            m.AssignDefaultActions();
            go.AddComponent<UiNavKeeper>();
        }
    }

    /// <summary>Highlight visuals for MenuButton (selection by pointer or gamepad focus).</summary>
    public class MenuItemVisual : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Background; public GameObject Accent; public TextMeshProUGUI Text;
        bool _selected, _hover;
        public void OnSelect(BaseEventData e) { _selected = true; Refresh(); UiAudio.Move(); }
        public void OnDeselect(BaseEventData e) { _selected = false; Refresh(); }
        // hover also moves the selection, so the mouse and the controller never highlight two rows at once and A acts on the row under the cursor (QA-764)
        public void OnPointerEnter(PointerEventData e)
        {
            _hover = true; Refresh();
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject) EventSystem.current.SetSelectedGameObject(gameObject);
        }
        public void OnPointerExit(PointerEventData e) { _hover = false; Refresh(); }
        void OnEnable() { _selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject; _hover = false; Refresh(); }
        void Refresh()
        {
            bool on = _selected || _hover;
            if (Background != null) Background.color = on ? UiColors.AccentBlue : UiColors.Row;
            if (Accent != null) Accent.SetActive(on);
        }
    }
}

namespace PixFootball.UI
{
    /// <summary>
    /// Scales a fixed-size design block down (never up) so it fits its host rectangle: the host is stretched by anchors, the block (child 0) is laid out at its design size and shrunk when the
    /// canvas is shorter or narrower than the design (4:3, 21:9). Used by the screens that were laid out for 16:9 only.
    /// </summary>
    public sealed class FitScale : MonoBehaviour
    {
        public RectTransform Block;
        public Vector2 DesignSize = new Vector2(800, 700);
        public float MaxScale = 1f;

        /// <summary>The scale a block of <paramref name="design"/> size needs to fit <paramref name="host"/> (pure, tested).</summary>
        public static float ScaleFor(Vector2 host, Vector2 design, float max = 1f)
        {
            if (design.x <= 0f || design.y <= 0f) return max;
            return Mathf.Clamp(Mathf.Min(host.x / design.x, host.y / design.y), 0.3f, max);
        }

        void OnRectTransformDimensionsChange() { Apply(); }
        void OnEnable() { Apply(); }

        public void Apply()
        {
            if (Block == null) return;
            var host = (RectTransform)transform;
            float k = ScaleFor(host.rect.size, DesignSize, MaxScale);
            Block.localScale = new Vector3(k, k, 1f);
        }
    }
}
