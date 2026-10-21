using PixFootball.Art;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>Palette from Docs/UIStyleGuide.md.</summary>
    public static class UiColors
    {
        public static readonly Color BgDeep = Hex("#11141C"), BgNav = Hex("#1A252C"), BgPanel = Hex("#1E1F28"), BgPanelDark = Hex("#15161E"),
            Row = Hex("#2A2B37"), RowHover = Hex("#34364A"), AccentBlue = Hex("#2D5ABC"), AccentBlueHot = Hex("#2D6AED"), AccentBlueDeep = Hex("#1D4190"),
            Gold = Hex("#FFC837"), Orange = Hex("#FF9000"), Danger = Hex("#C20000"), Disabled = Hex("#333333"), TextPrimary = Color.white,
            TextSecondary = Hex("#C5C5C5"), TextMuted = Hex("#6D6D6D"), Scrim = new Color(0.02f, 0.03f, 0.05f, 0.82f);

        public static Color Hex(string h) { Color c; ColorUtility.TryParseHtmlString(h, out c); return c; }
    }

    /// <summary>Code-built UI helpers: flat panels, TextMeshPro labels (Saira Condensed / Barlow), Xbox glyphs.</summary>
    public static class UiKit
    {
        public static TMP_FontAsset Title { get { var rt = PixRuntimeAssets.Load(); return rt != null ? rt.fontTitle as TMP_FontAsset : null; } }
        public static TMP_FontAsset Body { get { var rt = PixRuntimeAssets.Load(); return rt != null ? rt.fontBody as TMP_FontAsset : null; } }
        public static Sprite Sprite(string key) { var rt = PixRuntimeAssets.Load(); return rt != null ? rt.Sprite(key) : null; }

        public static Canvas CreateCanvas(string name, int sortOrder, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = sortOrder;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; s.referenceResolution = new Vector2(1920, 1080); s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; s.matchWidthOrHeight = 0.5f;
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
        }
    }

    /// <summary>Highlight visuals for MenuButton (selection by pointer or gamepad focus).</summary>
    public class MenuItemVisual : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Background; public GameObject Accent; public TextMeshProUGUI Text;
        bool _selected, _hover;
        public void OnSelect(BaseEventData e) { _selected = true; Refresh(); }
        public void OnDeselect(BaseEventData e) { _selected = false; Refresh(); }
        public void OnPointerEnter(PointerEventData e) { _hover = true; Refresh(); }
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
