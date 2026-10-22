using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace PixFootball.UI.Glass
{
    /// <summary>The dropdown control (see <see cref="GlassWidgets.Dropdown"/>): a Selectable that opens a popover of option buttons.</summary>
    public sealed class GlassDropdown : Selectable, ISubmitHandler, IPointerClickHandler
    {
        public TextMeshProUGUI ValueLabel;
        public string[] Options = new string[0];
        public int Index;
        public Action<int> Changed;
        GameObject _popup;

        public void SetOptions(string[] options, int index)
        {
            Options = options ?? new string[0]; Index = Mathf.Clamp(index, 0, Mathf.Max(0, Options.Length - 1)); RefreshLabel();
        }

        public void RefreshLabel() { if (ValueLabel != null) ValueLabel.text = Options.Length > 0 ? Options[Index] : ""; }

        public override void OnPointerEnter(PointerEventData e)
        {
            base.OnPointerEnter(e);
            if (_popup == null && EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject) EventSystem.current.SetSelectedGameObject(gameObject);
        }

        public void OnSubmit(BaseEventData e) { Open(); }
        public void OnPointerClick(PointerEventData e) { if (IsInteractable()) Open(); }
        public bool IsOpen { get { return _popup != null; } }

        public void Open()
        {
            if (_popup != null || Options.Length == 0) return;
            var t = GlassTheme.T;
            var host = (RectTransform)transform;
            var canvas = GetComponentInParent<Canvas>();
            _popup = new GameObject("DropdownPopup", typeof(RectTransform));
            var pr = (RectTransform)_popup.transform; pr.SetParent(host, false);
            var cv = _popup.AddComponent<Canvas>(); cv.overrideSorting = true; cv.sortingOrder = (canvas != null ? canvas.sortingOrder : 0) + 8;
            _popup.AddComponent<GraphicRaycaster>();
            GlassGraphic.Prepare(canvas != null ? canvas.rootCanvas : null);
            int visible = Mathf.Min(Options.Length, 7);
            float itemH = 58f, pad = 10f, w = host.rect.width * 0.6f, h = visible * itemH + pad * 2f;
            pr.anchorMin = pr.anchorMax = new Vector2(1f, 0f); pr.pivot = new Vector2(1f, 1f); pr.anchoredPosition = new Vector2(-12f, -6f); pr.sizeDelta = new Vector2(w, h);
            // open upwards when it would run off the bottom of the canvas
            var corners = new Vector3[4]; host.GetWorldCorners(corners);
            var root = canvas != null ? (RectTransform)canvas.rootCanvas.transform : null;
            if (root != null)
            {
                Vector3 local = root.InverseTransformPoint(corners[0]);
                if (local.y - h - 12f < root.rect.yMin) { pr.anchorMin = pr.anchorMax = new Vector2(1f, 1f); pr.pivot = new Vector2(1f, 0f); pr.anchoredPosition = new Vector2(-12f, 6f); }
            }
            var panel = GlassKit.Panel(pr, "Panel", 1.8f, true); UiKit.Stretch(panel.rectTransform);
            var list = UiKit.Rect(pr, "List"); UiKit.Stretch(list, pad, pad, pad, pad);
            var v = list.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 0f; v.childControlWidth = true; v.childControlHeight = false; v.childForceExpandWidth = true; v.childForceExpandHeight = false;
            var sc = list.gameObject.AddComponent<RectMask2D>();
            int first = Mathf.Clamp(Index - visible / 2, 0, Mathf.Max(0, Options.Length - visible));
            Button selectBtn = null; var btns = new List<Button>();
            for (int k = 0; k < visible; k++)
            {
                int idx = first + k;
                var b = GlassKit.Button(list, Options[idx], () => Choose(idx), GlassButtonStyle.Ghost, w - pad * 2f, itemH);
                var txt = b.GetComponentInChildren<TextMeshProUGUI>(); if (txt != null) { txt.alignment = TextAlignmentOptions.MidlineLeft; txt.fontSize = 24f; var rt = txt.rectTransform; rt.offsetMin = new Vector2(20f, 0f); }
                var lel = b.GetComponent<LayoutElement>(); lel.preferredWidth = -1f; lel.flexibleWidth = 1f; lel.preferredHeight = itemH;
                if (idx == Index) { selectBtn = b; var gf = b.GetComponent<GlassFocusable>(); if (gf != null) gf.Pinned = true; }
                btns.Add(b);
            }
            // wrap navigation inside the popup
            for (int k = 0; k < btns.Count; k++)
            {
                var nav = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = btns[(k + btns.Count - 1) % btns.Count], selectOnDown = btns[(k + 1) % btns.Count] };
                btns[k].navigation = nav;
            }
            var closer = _popup.AddComponent<PopupCancel>(); closer.Owner = this;
            if (EventSystem.current != null && selectBtn != null) EventSystem.current.SetSelectedGameObject(selectBtn.gameObject);
            UiAudio.Click();
        }

        void Choose(int i)
        {
            bool changed = i != Index;
            Index = i; RefreshLabel();
            Close();
            if (changed && Changed != null) Changed(Index);
        }

        public void Close()
        {
            if (_popup != null) { Destroy(_popup); _popup = null; }
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(gameObject);
        }

        sealed class PopupCancel : MonoBehaviour, ICancelHandler
        {
            public GlassDropdown Owner;
            public void OnCancel(BaseEventData e) { if (Owner != null) Owner.Close(); e.Use(); }
        }

        protected override void OnDisable() { base.OnDisable(); if (_popup != null) { Destroy(_popup); _popup = null; } }
    }
}

