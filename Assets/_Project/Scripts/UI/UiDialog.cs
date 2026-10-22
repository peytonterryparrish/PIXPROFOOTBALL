using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// A small modal confirmation ("QUIT TO DESKTOP?", "KEEP THESE DISPLAY SETTINGS?"). The safe button is selected first, B / Esc counts as "no", and an optional countdown runs the "no"
    /// action when it reaches zero (used for resolution changes: if the screen is unreadable the old mode comes back by itself).
    /// </summary>
    public sealed class UiDialog : MonoBehaviour, ICancelHandler
    {
        Action _yes, _no;
        float _left; bool _counting;
        string _message;
        TextMeshProUGUI _msg;
        GameObject _returnTo;
        bool _closed;

        public static bool AnyOpen { get; private set; }

        public static UiDialog Show(Transform parent, string title, string message, string yesLabel, string noLabel, Action onYes, Action onNo, float countdownSeconds = 0f, bool defaultYes = false)
        {
            var root = UiKit.Rect(parent, "Dialog"); UiKit.Stretch(root);
            var d = root.gameObject.AddComponent<UiDialog>();
            d._yes = onYes; d._no = onNo; d._message = message; d._left = countdownSeconds; d._counting = countdownSeconds > 0f;
            d._returnTo = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            Button yes, no;
            if (UiTheme.IsGlass)
            {
                var gp = PixFootball.UI.Glass.GlassWidgets.BuildDialog(root, title, message, yesLabel, noLabel, () => d.Close(true), () => d.Close(false));
                d._msg = gp.Message; yes = gp.Yes; no = gp.No;
            }
            else
            {
                var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.03f, 0.06f, 0.86f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
                var box = UiKit.Panel(root, "Box", UiColors.BgPanel); UiKit.Anchor(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(920, 460));
                var bar = UiKit.Panel(box.rectTransform, "Bar", UiColors.Gold); UiKit.Anchor(bar.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(0, 8)); bar.rectTransform.anchorMax = new Vector2(1, 1); bar.rectTransform.sizeDelta = new Vector2(0, 8);
                var t = UiKit.Label(box.rectTransform, title.ToUpperInvariant(), 54, UiColors.TextPrimary, true, TextAlignmentOptions.Center, "Title");
                UiKit.Anchor(t.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(860, 80)); t.characterSpacing = 4f; UiKit.Fit(t, 30);
                d._msg = UiKit.Label(box.rectTransform, message, 30, UiColors.TextSecondary, false, TextAlignmentOptions.Top, "Message");
                d._msg.textWrappingMode = TextWrappingModes.Normal; UiKit.Anchor(d._msg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -130), new Vector2(820, 130));
                var row = UiKit.Rect(box.rectTransform, "Buttons"); UiKit.Anchor(row, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 54), new Vector2(840, 84));
                var h = row.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 24; h.childAlignment = TextAnchor.MiddleCenter; h.childControlWidth = false; h.childControlHeight = false; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
                yes = UiKit.MenuButton(row, yesLabel, () => d.Close(true), 408, 84, 34);
                no = UiKit.MenuButton(row, noLabel, () => d.Close(false), 408, 84, 34);
            }
            // explicit left / right navigation so the pad never wanders onto a row behind the dialog
            var ny = yes.navigation; ny.mode = Navigation.Mode.Explicit; ny.selectOnRight = no; ny.selectOnLeft = no; yes.navigation = ny;
            var nn = no.navigation; nn.mode = Navigation.Mode.Explicit; nn.selectOnRight = yes; nn.selectOnLeft = yes; no.navigation = nn;
            root.transform.SetAsLastSibling();
            AnyOpen = true;
            d.StartCoroutine(d.SelectLater((defaultYes ? yes : no).gameObject));
            d.Refresh();
            return d;
        }

        IEnumerator SelectLater(GameObject g)
        {
            yield return null;
            if (g != null && EventSystem.current != null) { UiAudio.Mute(0.2f); EventSystem.current.SetSelectedGameObject(g); }
        }

        void Refresh()
        {
            if (_msg == null) return;
            _msg.text = _counting ? _message + "\n\nREVERTING IN " + Mathf.CeilToInt(Mathf.Max(0f, _left)) + " S" : _message;
        }

        void Update()
        {
            if (_closed || !_counting) return;
            int before = Mathf.CeilToInt(_left);
            _left -= Time.unscaledDeltaTime;
            if (_left <= 0f) { Close(false); return; }
            if (Mathf.CeilToInt(_left) != before) Refresh();
        }

        void Close(bool yes)
        {
            if (_closed) return;
            _closed = true; AnyOpen = false;
            var back = _returnTo;
            var cb = yes ? _yes : _no;
            Destroy(gameObject);
            if (back != null && back.activeInHierarchy && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(back);
            if (cb != null) cb();
        }

        void OnDestroy() { if (!_closed) AnyOpen = false; }

        public void OnCancel(BaseEventData e) { Close(false); }
    }
}
