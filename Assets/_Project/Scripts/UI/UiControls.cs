using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>Left/right value selector (resolution, quality, camera, on/off): works with gamepad left/right, submit and pointer clicks.</summary>
    public class OptionSelector : Selectable, ISubmitHandler, IPointerClickHandler
    {
        public string[] Options = new string[0];
        public int Index;
        public TextMeshProUGUI NameLabel, ValueLabel;
        public GameObject Accent;
        public event Action<int> Changed;
        public bool Wrap = true;
        /// <summary>Glass theme: the value is shown as plain text (the chevrons are drawn as icons beside it).</summary>
        public bool PlainValue;

        public void SetOptions(string[] options, int index) { Options = options; Index = Mathf.Clamp(index, 0, Mathf.Max(0, options.Length - 1)); RefreshLabel(); }

        public void Step(int dir)
        {
            if (Options == null || Options.Length == 0) return;
            int n = Index + dir;
            if (Wrap) n = ((n % Options.Length) + Options.Length) % Options.Length; else n = Mathf.Clamp(n, 0, Options.Length - 1);
            if (n == Index) return;
            Index = n; RefreshLabel(); UiAudio.Move();
            if (Changed != null) Changed(Index);
        }

        string PlayerValue(string v) { return PlainValue ? v : "<   " + v + "   >"; }

        public void RefreshLabel() { if (ValueLabel != null) ValueLabel.text = Options != null && Options.Length > 0 ? (PlayerValue(Options[Mathf.Clamp(Index, 0, Options.Length - 1)])) : ""; }

        // hover moves the selection too (one highlighted row at a time), and on long lists (32 teams) LB / RB page by 8 so the player never needs 31 presses (QA-764 / QA-768)
        public override void OnPointerEnter(PointerEventData e)
        {
            base.OnPointerEnter(e);
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject) EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public override void OnSelect(BaseEventData e) { base.OnSelect(e); UiAudio.Move(); }

        void Update()
        {
            if (Options == null || Options.Length < 12 || EventSystem.current == null || EventSystem.current.currentSelectedGameObject != gameObject) return;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null)
            {
                if (pad.leftShoulder.wasPressedThisFrame) Step(-8);
                else if (pad.rightShoulder.wasPressedThisFrame) Step(8);
            }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.pageUpKey.wasPressedThisFrame) Step(-8);
                else if (kb.pageDownKey.wasPressedThisFrame) Step(8);
            }
        }

        public override void OnMove(AxisEventData e)
        {
            if (e.moveDir == MoveDirection.Left) { Step(-1); e.Use(); }
            else if (e.moveDir == MoveDirection.Right) { Step(1); e.Use(); }
            else base.OnMove(e);
        }

        public void OnSubmit(BaseEventData e) { Step(1); }

        public void OnPointerClick(PointerEventData e)
        {
            var rt = (RectTransform)transform;
            Vector2 local; RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out local);
            Step(local.x < 0f ? -1 : 1);
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            bool on = state == SelectionState.Selected || state == SelectionState.Highlighted || state == SelectionState.Pressed;
            var img = targetGraphic as Image;
            if (img != null) img.color = on ? UiColors.AccentBlue : UiColors.Row;
            if (Accent != null) Accent.SetActive(on);
        }
    }

    public static class UiControls
    {
        public static OptionSelector Selector(Transform parent, string name, string[] options, int index, Action<int> onChanged, float width = 760f, float height = 50f)
        {
            if (UiTheme.IsGlass) return PixFootball.UI.Glass.GlassKit.Stepper(parent, name, options, index, onChanged, width, Mathf.Max(height, 56f));
            var r = UiKit.Rect(parent, "Opt_" + name);
            r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var bg = r.gameObject.AddComponent<Image>(); bg.color = UiColors.Row;
            var sel = r.gameObject.AddComponent<OptionSelector>();
            sel.targetGraphic = bg; sel.transition = Selectable.Transition.None;
            var bar = UiKit.Panel(r, "Accent", UiColors.Gold); UiKit.Anchor(bar.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(6, height)); bar.gameObject.SetActive(false);
            var n = UiKit.Label(r, name.ToUpperInvariant(), 28, UiColors.TextSecondary, true, TextAlignmentOptions.MidlineLeft, "Name"); UiKit.Stretch(n.rectTransform, 28, 0, width * 0.5f, 0);
            var v = UiKit.Label(r, "", 30, UiColors.TextPrimary, true, TextAlignmentOptions.MidlineRight, "Value"); UiKit.Stretch(v.rectTransform, width * 0.4f, 0, 24, 0);
            sel.NameLabel = n; sel.ValueLabel = v; sel.Accent = bar.gameObject;
            sel.SetOptions(options, index);
            if (onChanged != null) sel.Changed += onChanged;
            return sel;
        }

        public static Slider SliderRow(Transform parent, string name, float value, Action<float> onChanged, float width = 760f, float height = 50f)
        {
            if (UiTheme.IsGlass) return PixFootball.UI.Glass.GlassKit.SliderRow(parent, name, value, onChanged, width, Mathf.Max(height, 56f));
            var r = UiKit.Rect(parent, "Slider_" + name);
            r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var bg = r.gameObject.AddComponent<Image>(); bg.color = UiColors.Row;
            var n = UiKit.Label(r, name.ToUpperInvariant(), 28, UiColors.TextSecondary, true, TextAlignmentOptions.MidlineLeft, "Name"); UiKit.Stretch(n.rectTransform, 28, 0, width * 0.55f, 0);
            var slider = r.gameObject.AddComponent<Slider>();
            // track
            var track = UiKit.Panel(r, "Track", new Color(0.42f, 0.42f, 0.46f)); UiKit.Anchor(track.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-90, 0), new Vector2(width * 0.4f, 8));
            var fillArea = UiKit.Rect(track.rectTransform, "FillArea"); UiKit.Stretch(fillArea);
            var fill = UiKit.Panel(fillArea, "Fill", UiColors.Gold); fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0, 1f); fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = Vector2.zero;
            var handleArea = UiKit.Rect(track.rectTransform, "HandleArea"); UiKit.Stretch(handleArea, 12, 0, 12, 0);
            var handle = UiKit.Panel(handleArea, "Handle", Color.white); handle.rectTransform.sizeDelta = new Vector2(24, 44);
            var val = UiKit.Label(r, Mathf.RoundToInt(value * 100) + "%", 28, UiColors.TextPrimary, true, TextAlignmentOptions.MidlineRight, "Value"); UiKit.Anchor(val.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-18, 0), new Vector2(90, height));
            slider.fillRect = fill.rectTransform; slider.handleRect = handle.rectTransform; slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight; slider.minValue = 0f; slider.maxValue = 1f; slider.wholeNumbers = false; slider.value = value;
            slider.transition = Selectable.Transition.None;
            slider.onValueChanged.AddListener(x => { val.text = Mathf.RoundToInt(x * 100) + "%"; if (onChanged != null) onChanged(x); });
            var vis = r.gameObject.AddComponent<RowHighlight>(); vis.Background = bg; vis.Target = slider;
            return slider;
        }
    }

    /// <summary>Highlights a row (background) while a child Selectable has focus or the pointer hovers.</summary>
    public class RowHighlight : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Image Background;
        /// <summary>The control this row belongs to; hovering the row selects it.</summary>
        public Selectable Target;
        bool _s, _h;
        public void OnSelect(BaseEventData e) { _s = true; R(); }
        public void OnDeselect(BaseEventData e) { _s = false; R(); }
        public void OnPointerEnter(PointerEventData e)
        {
            _h = true; R();
            if (Target != null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != Target.gameObject) EventSystem.current.SetSelectedGameObject(Target.gameObject);
        }
        public void OnPointerExit(PointerEventData e) { _h = false; R(); }
        void R() { if (Background != null) Background.color = (_s || _h) ? UiColors.AccentBlue : UiColors.Row; }
    }
}
