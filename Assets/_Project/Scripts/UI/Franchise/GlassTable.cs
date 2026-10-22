using System;
using System.Collections.Generic;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI.Franchise
{
    /// <summary>One column of a <see cref="GlassTable"/>: a header, a weight (share of the width), alignment and the callbacks that give the text and the tint of a cell by row index.</summary>
    public sealed class TableColumn
    {
        public string Header = "";
        public float Weight = 1f;
        public TextAlignmentOptions Align = TextAlignmentOptions.MidlineLeft;
        public Func<int, string> Text;
        public Func<int, Color?> Tint;
        public bool Sortable = true;
        public TableColumn() { }
        public TableColumn(string header, float weight, Func<int, string> text, bool right = false, Func<int, Color?> tint = null, bool sortable = true)
        {
            Header = header; Weight = weight; Text = text; Tint = tint; Sortable = sortable;
            Align = right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
        }
    }

    /// <summary>
    /// The franchise list: one gamepad-focusable control that shows a window of a long list as glass rows. Up / down moves the cursor (and scrolls), A activates the row, X fires the alternate action,
    /// Y cycles the sort column; the mouse wheel scrolls, a click selects, a second click on the cursor row activates, a click on a header sorts. At the first or last row the move passes on to
    /// the neighbouring control (explicit navigation set by the page). Rows are pooled: a 250 row draft board costs the same as a 12 row one. Text comes from the column callbacks (row index = index in the data
    /// the page keeps, already sorted / filtered by the page).
    /// </summary>
    public sealed class GlassTable : Selectable, ISubmitHandler, IPointerClickHandler, IScrollHandler
    {
        public const float DefaultRowHeight = 50f, HeaderHeight = 40f;

        public List<TableColumn> Columns = new List<TableColumn>();
        public float RowHeight = DefaultRowHeight;
        public int Count { get; private set; }
        public int Cursor { get; private set; }
        public int Top { get; private set; }
        public int VisibleRows { get; private set; }
        public int SortColumn = -1; public bool SortDescending = true;

        public Action<int> CursorChanged, Activated, AltAction, HeaderClicked, SortRequested;
        /// <summary>Per-row tint of the whole row text (injured, expiring, user's own...), null = default colours.</summary>
        public Func<int, Color?> RowTint;
        /// <summary>Empty list text.</summary>
        public string EmptyText = "Nothing to show.";
        /// <summary>Pad-only: X / Y act on the cursor row while the table has focus.</summary>
        public bool HandleSortKey = true;

        sealed class Row { public RectTransform Rt; public Image Bg; public TextMeshProUGUI[] Cells; }
        readonly List<Row> _rows = new List<Row>();
        RectTransform _rt, _rowHost, _headerRt, _scrollTrack, _scrollThumb;
        GlassGraphic _cursorBar;
        TextMeshProUGUI[] _headers = new TextMeshProUGUI[0];
        Image[] _sortIcons = new Image[0];
        TextMeshProUGUI _empty;
        float[] _x = new float[0], _w = new float[0];
        bool _focused, _built; float _padX = 16f;
        bool _hasHeader = true;

        // ------------------------------------------------------------------ construction
        /// <summary>Creates the table inside <paramref name="parent"/> (the caller anchors the returned rect transform; the table is laid out for the size it has at <see cref="Build"/>).</summary>
        public static GlassTable Create(RectTransform parent, string name, float width, float height, IList<TableColumn> columns, float rowHeight = DefaultRowHeight, bool header = true)
        {
            var r = UiKit.Rect(parent, name);
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f); r.sizeDelta = new Vector2(width, height);
            var t = r.gameObject.AddComponent<GlassTable>();
            t.transition = Transition.None;
            t.Columns = new List<TableColumn>(columns);
            t.RowHeight = rowHeight; t._hasHeader = header;
            t.Build();
            return t;
        }

        void Build()
        {
            _rt = (RectTransform)transform;
            var t = GlassTheme.T;
            float w = _rt.rect.width > 1f ? _rt.rect.width : _rt.sizeDelta.x, h = _rt.rect.height > 1f ? _rt.rect.height : _rt.sizeDelta.y;
            float head = _hasHeader ? HeaderHeight : 0f;
            VisibleRows = Mathf.Max(1, Mathf.FloorToInt((h - head) / RowHeight));

            // column geometry
            float total = 0f; foreach (var c in Columns) total += c.Weight;
            float usable = w - _padX * 2f - 14f;
            _x = new float[Columns.Count]; _w = new float[Columns.Count];
            float x = _padX;
            for (int i = 0; i < Columns.Count; i++) { _w[i] = usable * Columns[i].Weight / total; _x[i] = x; x += _w[i]; }

            var backing = GlassKit.Panel(_rt, "Backing", 1.35f, true, t.cornerRadius); UiKit.Stretch(backing.rectTransform);
            // header
            if (_hasHeader)
            {
                _headerRt = UiKit.Rect(_rt, "Header"); UiKit.Anchor(_headerRt, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(w, HeaderHeight));
                var rule = GlassKit.Rule(_headerRt, "HeaderRule", 0.9f); var rr = rule.rectTransform; rr.anchorMin = new Vector2(0f, 0f); rr.anchorMax = new Vector2(1f, 0f); rr.pivot = new Vector2(0.5f, 0f); rr.anchoredPosition = new Vector2(0f, 1f); rr.sizeDelta = new Vector2(-24f, 1.4f);
                _headers = new TextMeshProUGUI[Columns.Count]; _sortIcons = new Image[Columns.Count];
                for (int i = 0; i < Columns.Count; i++)
                {
                    var hx = GlassKit.Eyebrow(_headerRt, Columns[i].Header, Columns[i].Align == TextAlignmentOptions.MidlineRight ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft, "H" + i);
                    GlassKit.Style(hx, 18f, t.textSecondary, true, 2f);
                    UiKit.Anchor(hx.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(_x[i], -2f), new Vector2(_w[i], HeaderHeight)); UiKit.Fit(hx, 12f);
                    _headers[i] = hx;
                    var ic = GlassKit.Icon(_headerRt, "ui_arrow_down", 18f, t.accent, "Sort" + i);
                    ic.gameObject.SetActive(false); _sortIcons[i] = ic;
                }
            }

            // rows
            var host = UiKit.Rect(_rt, "Rows"); host.anchorMin = new Vector2(0f, 1f); host.anchorMax = new Vector2(1f, 1f); host.pivot = new Vector2(0f, 1f);
            host.anchoredPosition = new Vector2(0f, -head); host.sizeDelta = new Vector2(0f, VisibleRows * RowHeight);
            host.gameObject.AddComponent<RectMask2D>();
            _rowHost = host;
            _cursorBar = GlassKit.Surface(host, "Cursor", GlassKind.Glass, t.cornerRadiusSmall, false, 1.25f);
            _cursorBar.rectTransform.anchorMin = new Vector2(0f, 1f); _cursorBar.rectTransform.anchorMax = new Vector2(1f, 1f); _cursorBar.rectTransform.pivot = new Vector2(0.5f, 1f);
            _cursorBar.rectTransform.sizeDelta = new Vector2(-8f, RowHeight - 4f);
            _cursorBar.SetFocus(0f);
            for (int i = 0; i < VisibleRows; i++)
            {
                var row = new Row { Rt = UiKit.Rect(host, "Row" + i), Cells = new TextMeshProUGUI[Columns.Count] };
                row.Rt.anchorMin = new Vector2(0f, 1f); row.Rt.anchorMax = new Vector2(1f, 1f); row.Rt.pivot = new Vector2(0.5f, 1f);
                row.Rt.anchoredPosition = new Vector2(0f, -i * RowHeight); row.Rt.sizeDelta = new Vector2(0f, RowHeight);
                row.Bg = row.Rt.gameObject.AddComponent<Image>(); row.Bg.raycastTarget = false; row.Bg.color = new Color(1f, 1f, 1f, 0f);
                for (int c = 0; c < Columns.Count; c++)
                {
                    var tx = GlassKit.Text(row.Rt, "", GlassText.Body, Columns[c].Align, "C" + c);
                    GlassKit.Style(tx, 24f, t.textPrimary, false, 0f);
                    UiKit.Anchor(tx.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(_x[c], 0f), new Vector2(_w[c], RowHeight)); UiKit.Fit(tx, 16f);
                    row.Cells[c] = tx;
                }
                _rows.Add(row);
            }
            // scroll indicator
            _scrollTrack = UiKit.Rect(_rt, "Scroll"); UiKit.Anchor(_scrollTrack, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-4f, -head), new Vector2(4f, VisibleRows * RowHeight));
            var tr = _scrollTrack.gameObject.AddComponent<Image>(); tr.color = new Color(1f, 1f, 1f, 0.07f); tr.raycastTarget = false;
            _scrollThumb = UiKit.Rect(_scrollTrack, "Thumb"); _scrollThumb.anchorMin = new Vector2(0f, 1f); _scrollThumb.anchorMax = new Vector2(1f, 1f); _scrollThumb.pivot = new Vector2(0.5f, 1f);
            var th = _scrollThumb.gameObject.AddComponent<Image>(); th.color = new Color(t.accent.r, t.accent.g, t.accent.b, 0.75f); th.raycastTarget = false;
            // empty text
            _empty = GlassKit.Text(_rt, "", GlassText.Body, TextAlignmentOptions.Center, "Empty"); GlassKit.Style(_empty, 24f, t.textMuted, false, 0f);
            UiKit.Anchor(_empty.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -head - 60f), new Vector2(w - 40f, 60f));
            _built = true;
            Refresh();
        }

        // ------------------------------------------------------------------ data
        public void SetCount(int n, bool keepCursor = true)
        {
            Count = Mathf.Max(0, n);
            if (!keepCursor) { Cursor = 0; Top = 0; }
            Cursor = Mathf.Clamp(Cursor, 0, Mathf.Max(0, Count - 1));
            Clamp();
            Refresh();
        }

        void Clamp()
        {
            int maxTop = Mathf.Max(0, Count - VisibleRows);
            if (Cursor < Top) Top = Cursor;
            if (Cursor >= Top + VisibleRows) Top = Cursor - VisibleRows + 1;
            Top = Mathf.Clamp(Top, 0, maxTop);
        }

        public void SetCursor(int i, bool notify = true)
        {
            i = Mathf.Clamp(i, 0, Mathf.Max(0, Count - 1));
            if (i == Cursor && !notify) { Refresh(); return; }
            bool changed = i != Cursor;
            Cursor = i; Clamp(); Refresh();
            if (notify && CursorChanged != null && Count > 0) CursorChanged(Cursor);
            if (changed) UiAudio.Move();
        }

        public void SetSort(int column, bool descending)
        {
            SortColumn = column; SortDescending = descending; RefreshHeader();
        }

        void RefreshHeader()
        {
            var t = GlassTheme.T;
            for (int i = 0; i < _headers.Length; i++)
            {
                bool on = i == SortColumn;
                _headers[i].color = on ? t.accent : t.textSecondary;
                var ic = _sortIcons[i];
                ic.gameObject.SetActive(on);
                if (on)
                {
                    var rt = ic.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
                    float tw = Mathf.Min(_w[i], _headers[i].GetPreferredValues(_headers[i].text).x);
                    bool right = Columns[i].Align == TextAlignmentOptions.MidlineRight;
                    float cx = right ? _x[i] + _w[i] - tw - 16f : _x[i] + tw + 16f;
                    rt.anchoredPosition = new Vector2(cx, -2f); rt.sizeDelta = new Vector2(16f, 16f);
                    rt.localEulerAngles = new Vector3(0f, 0f, SortDescending ? 0f : 180f);
                }
            }
        }

        public void Refresh()
        {
            if (!_built) return;
            var t = GlassTheme.T;
            RefreshHeader();
            for (int i = 0; i < _rows.Count; i++)
            {
                int idx = Top + i;
                var row = _rows[i];
                bool on = idx < Count;
                row.Rt.gameObject.SetActive(on);
                if (!on) continue;
                bool cur = idx == Cursor;
                row.Bg.color = !cur && (idx & 1) == 1 ? new Color(1f, 1f, 1f, 0.035f) : new Color(1f, 1f, 1f, 0f);
                Color? rowTint = RowTint != null ? RowTint(idx) : null;
                for (int c = 0; c < Columns.Count; c++)
                {
                    var col = Columns[c];
                    string s = col.Text != null ? col.Text(idx) : "";
                    var cell = row.Cells[c];
                    if (cell.text != s) cell.text = s;
                    Color? tint = col.Tint != null ? col.Tint(idx) : null;
                    cell.color = tint ?? rowTint ?? (cur ? Color.white : t.textPrimary);
                }
            }
            // cursor bar
            bool show = Count > 0 && Cursor >= Top && Cursor < Top + VisibleRows;
            _cursorBar.gameObject.SetActive(show);
            if (show) _cursorBar.rectTransform.anchoredPosition = new Vector2(0f, -(Cursor - Top) * RowHeight - 2f);
            _cursorBar.SetFocus(_focused ? 1f : 0.28f);
            // scroll thumb
            bool scroll = Count > VisibleRows;
            _scrollTrack.gameObject.SetActive(scroll);
            if (scroll)
            {
                float trackH = VisibleRows * RowHeight, thumbH = Mathf.Max(24f, trackH * VisibleRows / Count);
                float y = (trackH - thumbH) * (Count - VisibleRows == 0 ? 0f : (float)Top / (Count - VisibleRows));
                _scrollThumb.sizeDelta = new Vector2(0f, thumbH); _scrollThumb.anchoredPosition = new Vector2(0f, -y);
            }
            _empty.text = Count == 0 ? EmptyText : "";
        }

        /// <summary>Index of the row that shows text <paramref name="s"/> in column 0 (-1 when not found); handy for tests.</summary>
        public string CellText(int row, int col) { return Columns[col].Text != null ? Columns[col].Text(row) : ""; }

        // ------------------------------------------------------------------ input
        /// <summary>Edit-mode tools (no focus events): draw the cursor as focused.</summary>
        public void ShowFocused(bool on) { _focused = on; Refresh(); }

        public override void OnSelect(BaseEventData e) { base.OnSelect(e); _focused = true; Refresh(); UiAudio.Move(); }
        public override void OnDeselect(BaseEventData e) { base.OnDeselect(e); _focused = false; Refresh(); }

        /// <summary>A page can take the up / down moves first (the depth chart picks a row up and moves it): return true when handled.</summary>
        public Func<MoveDirection, bool> MoveHook;

        public override void OnMove(AxisEventData e)
        {
            if (MoveHook != null && MoveHook(e.moveDir)) { e.Use(); return; }
            if (e.moveDir == MoveDirection.Up && Cursor > 0) { SetCursor(Cursor - 1); e.Use(); return; }
            if (e.moveDir == MoveDirection.Down && Cursor < Count - 1) { SetCursor(Cursor + 1); e.Use(); return; }
            base.OnMove(e);
        }

        public void OnSubmit(BaseEventData e) { if (Count > 0 && Activated != null) { UiAudio.Click(); Activated(Cursor); } }

        public void OnScroll(PointerEventData e)
        {
            if (Count <= VisibleRows) return;
            int d = e.scrollDelta.y > 0f ? -3 : 3;
            Top = Mathf.Clamp(Top + d, 0, Count - VisibleRows);
            if (Cursor < Top) Cursor = Top; else if (Cursor >= Top + VisibleRows) Cursor = Top + VisibleRows - 1;
            Refresh();
            if (CursorChanged != null) CursorChanged(Cursor);
        }

        public void OnPointerClick(PointerEventData e)
        {
            Vector2 lp;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out lp)) return;
            float y = -lp.y;
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != gameObject) EventSystem.current.SetSelectedGameObject(gameObject);
            if (_hasHeader && y < HeaderHeight)
            {
                for (int i = 0; i < Columns.Count; i++)
                    if (lp.x >= _x[i] && lp.x <= _x[i] + _w[i] && Columns[i].Sortable) { UiAudio.Click(); if (HeaderClicked != null) HeaderClicked(i); return; }
                return;
            }
            int row = Top + Mathf.FloorToInt((y - (_hasHeader ? HeaderHeight : 0f)) / RowHeight);
            if (row < 0 || row >= Count) return;
            if (row == Cursor) { if (Activated != null) { UiAudio.Click(); Activated(Cursor); } }
            else SetCursor(row);
        }

        void Update()
        {
            if (!_focused || Count == 0 || UiDialog.AnyOpen) return;
            if (FranchiseInput.Pressed(PadBtn.X) && AltAction != null) { UiAudio.Click(); AltAction(Cursor); }
            if (HandleSortKey && FranchiseInput.Pressed(PadBtn.Y))
            {
                // cycle to the next sortable column (the page re-sorts and refreshes)
                int n = Columns.Count, c = SortColumn;
                for (int k = 0; k < n; k++) { c = (c + 1) % n; if (Columns[c].Sortable) break; }
                UiAudio.Click();
                if (SortRequested != null) SortRequested(c);
                else if (HeaderClicked != null) HeaderClicked(c);
            }
        }
    }
}
