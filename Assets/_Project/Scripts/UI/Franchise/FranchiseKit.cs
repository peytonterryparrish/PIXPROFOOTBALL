using System;
using System.Collections;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI.Franchise
{
    /// <summary>Pure text and colour helpers of the franchise screens (EditMode tested).</summary>
    public static class Fmt
    {
        public static string Money(long dollars) { return CareerText.Money(dollars); }

        /// <summary>"$12.5M" style for amounts of a million or more, "$850K" below.</summary>
        public static string MoneyShort(long d)
        {
            long a = Math.Abs(d);
            string s = a >= 1000000L ? "$" + (a / 1000000.0).ToString(a >= 100000000L ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture) + "M" : "$" + (a / 1000L) + "K";
            return d < 0 ? "-" + s : s;
        }

        public static string Years(int n) { return n + (n == 1 ? " yr" : " yrs"); }

        public static string Height(int inches) { return (inches / 12) + "'" + (inches % 12) + "\""; }

        public static string Record(int w, int l, int t) { return w + "-" + l + (t > 0 ? "-" + t : ""); }

        public static string Ordinal(int n) { return CareerText.Ordinal(n); }

        public static string Pos(Position p) { return p.ToString(); }

        /// <summary>The status of a roster row as a short caps tag.</summary>
        public static string Status(RosterStatus s)
        {
            switch (s)
            {
                case RosterStatus.Active: return "ACTIVE";
                case RosterStatus.PracticeSquad: return "SQUAD";
                case RosterStatus.InjuredReserve: return "IR";
                case RosterStatus.FreeAgent: return "FA";
                case RosterStatus.Waivers: return "WAIVERS";
                default: return "UNSIGNED";
            }
        }

        public static string Stars(int tier) { return AbilityCatalog.Stars(tier); }

        /// <summary>Rating tint: green for elite, white for solid, amber for thin, nothing special in between (the accent is reserved for focus).</summary>
        public static Color OvrColor(int ovr)
        {
            var t = GlassTheme.T;
            if (ovr >= 85) return t.success;
            if (ovr >= 72) return t.textPrimary;
            if (ovr >= 62) return t.textSecondary;
            return t.warning;
        }

        public static string Trim(string s, int max) { return s != null && s.Length > max ? s.Substring(0, max - 1) + "." : s ?? ""; }

        public static string Plural(int n, string one, string many) { return n + " " + (n == 1 ? one : many); }
    }

    /// <summary>An absolute-position layout helper: top-left anchored placement inside a design block (<see cref="FranchiseUi.PageSize"/> reference units for the franchise pages).</summary>
    public static class Lay
    {
        /// <summary>Nothing below 20 canvas units (SPEC section 1: the mock's 16 to 19 px labels are raised): every helper clamps to it.</summary>
        public const float MinText = 20f;
        /// <summary>Width and height of the content area under the sub tabs (what a page lays out in).</summary>
        public static float W { get { return FranchiseUi.ContentSize.x; } }
        public static float H { get { return FranchiseUi.ContentSize.y; } }

        public static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f); rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Group(RectTransform parent, string name, float x, float y, float w, float h)
        {
            return Place(UiKit.Rect(parent, name), x, y, w, h);
        }

        /// <summary>Removes the LayoutElement a library widget adds (the franchise pages place everything by hand). Safe in edit mode.</summary>
        public static void Unlayout(GameObject go)
        {
            var le = go.GetComponent<LayoutElement>();
            if (le == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(le); else UnityEngine.Object.DestroyImmediate(le);
        }

        /// <summary>A glass pill behind a text that sits straight on the 3D scene (keeps it readable over a bright stadium).</summary>
        public static TextMeshProUGUI Under(TextMeshProUGUI t, float padX = 16f, float padY = 4f)
        {
            if (GlassTheme.T.cornerRadius <= 0.5f) return t;   // the Tile theme has a flat charcoal backdrop: the text is readable without a pill
            var g = GlassKit.Panel(t.transform.parent, "TextBack", 1.2f, false, 14f);
            var rt = g.rectTransform; var tr = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = tr.anchoredPosition + new Vector2(-padX, padY); rt.sizeDelta = tr.sizeDelta + new Vector2(padX * 2f, padY * 2f);
            g.transform.SetSiblingIndex(t.transform.GetSiblingIndex());
            return t;
        }

        public static GlassCard Card(RectTransform parent, string title, float x, float y, float w, float h, float density = 1f, bool shadow = true)
        {
            var c = GlassWidgets.Card(parent, title, w, h, "Card_" + (string.IsNullOrEmpty(title) ? "x" : title), shadow, density);
            Place(c.Root, x, y, w, h);
            Unlayout(c.Root.gameObject);
            return c;
        }

        public static TextMeshProUGUI Text(RectTransform parent, string text, float size, Color color, float x, float y, float w, float h, bool bold = false, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, float tracking = 0f, string name = "Text", bool fit = true)
        {
            size = Mathf.Max(MinText, size);
            var tx = GlassKit.Text(parent, text, bold ? GlassText.Heading : GlassText.Body, align, name);
            GlassKit.Style(tx, size, color, bold, GlassTokens.Em(tracking, size));   // tracking = CSS letter-spacing in px
            Place(tx.rectTransform, x, y, w, h);
            if (fit) UiKit.Fit(tx, Mathf.Max(MinText, size * 0.62f));
            return tx;
        }

        public static TextMeshProUGUI Caps(RectTransform parent, string text, float x, float y, float w, float h = 28f, Color? color = null, float size = 20f, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            var tx = GlassKit.Eyebrow(parent, text, align, "Caps");
            GlassKit.Style(tx, Mathf.Max(MinText, size), color ?? GlassTheme.T.textSecondary, true, GlassTokens.Em(3f, Mathf.Max(MinText, size)));
            Place(tx.rectTransform, x, y, w, h); UiKit.Fit(tx, MinText);
            return tx;
        }

        /// <summary>A panel head: a darker strip with the title in tracked caps and a gold hairline under it; <paramref name="right"/> is an optional muted caption at the right end. Returns the strip.</summary>
        public static RectTransform Head(RectTransform parent, string title, float x, float y, float w, float h = 56f, string right = null, float size = 26f)
        {
            var t = GlassTheme.T;
            var strip = GlassKit.Solid(parent, "Head", t.flat ? t.panelHead : new Color(0f, 0f, 0f, 0.30f), 0f);
            Place(strip.rectTransform, x, y, w, h);
            Text(strip.rectTransform, title.ToUpperInvariant(), size, t.textPrimary, 20f, 0f, w * (right != null ? 0.62f : 1f) - 40f, h, true, TextAlignmentOptions.MidlineLeft, 4f, "Title");
            if (!string.IsNullOrEmpty(right)) Text(strip.rectTransform, right, 20f, t.textMuted, w * 0.55f, 0f, w * 0.45f - 20f, h, false, TextAlignmentOptions.MidlineRight, 1.5f, "Right");
            var line = GlassKit.Solid(strip.rectTransform, "Gold", t.accent, 0f); Place(line.rectTransform, 0f, h - 2f, w, 2f);
            return strip.rectTransform;
        }
    }

    /// <summary>A vertical stack of heading / label-value / bar / paragraph lines inside a rectangle (rebuilt by <see cref="Clear"/> + calls). Used by every detail pane.</summary>
    public sealed class StackPanel
    {
        public RectTransform Root;
        public float Y { get; private set; }
        public float Width, Height;

        public static StackPanel Create(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var s = new StackPanel { Root = Lay.Group(parent, name, x, y, w, h), Width = w, Height = h };
            s.Root.gameObject.AddComponent<RectMask2D>();
            return s;
        }

        public void Clear()
        {
            for (int i = Root.childCount - 1; i >= 0; i--) UiKit.Kill(Root.GetChild(i).gameObject);
            Y = 0f;
        }

        public void Gap(float h) { Y += h; }

        public void Rule() { var r = GlassKit.Rule(Root, "Rule", 0.8f); Lay.Place(r.rectTransform, 0f, Y + 4f, Width, 1.4f); Y += 12f; }

        public TextMeshProUGUI Heading(string text, Color? c = null, float size = 30f)
        {
            var t = Lay.Text(Root, text, size, c ?? GlassTheme.T.textPrimary, 0f, Y, Width, size + 14f, true, TextAlignmentOptions.MidlineLeft, 1.5f, "Heading");
            Y += size + 14f; return t;
        }

        public TextMeshProUGUI Caps(string text, Color? c = null)
        {
            var t = Lay.Caps(Root, text, 0f, Y, Width, 28f, c); Y += 32f; return t;
        }

        public void Line(string label, string value, Color? valueColor = null, float height = 34f, float size = 22f)
        {
            var t = GlassTheme.T;
            Lay.Text(Root, label, size - 2f, t.textSecondary, 0f, Y, Width * 0.46f, height, false, TextAlignmentOptions.MidlineLeft, 0f, "Label");
            Lay.Text(Root, value, size, valueColor ?? t.textPrimary, Width * 0.46f, Y, Width * 0.54f, height, true, TextAlignmentOptions.MidlineRight, 0f, "Value");
            Y += height;
        }

        public void Bar(string label, float v01, string valueText, bool accent = false, float height = 34f)
        {
            var t = GlassTheme.T;
            Lay.Text(Root, label.ToUpperInvariant(), 20f, t.textSecondary, 0f, Y, Width * 0.34f, height, true, TextAlignmentOptions.MidlineLeft, 1.5f, "BarLabel");
            var bar = GlassKit.ProgressBar(Root, Mathf.Clamp01(v01), Width * 0.46f, 8f, accent, "Bar");
            Lay.Unlayout(bar.gameObject);
            Lay.Place((RectTransform)bar.transform, Width * 0.36f, Y + (height - 8f) * 0.5f, Width * 0.46f, 8f);
            Lay.Text(Root, valueText, 22f, t.textPrimary, Width * 0.84f, Y, Width * 0.16f, height, true, TextAlignmentOptions.MidlineRight, 0f, "BarValue");
            Y += height;
        }

        public TextMeshProUGUI Para(string text, float size = 22f, Color? c = null, float maxHeight = 0f)
        {
            var tx = GlassKit.Text(Root, text ?? "", GlassText.Body, TextAlignmentOptions.TopLeft, "Para");
            size = Mathf.Max(Lay.MinText, size);
            GlassKit.Style(tx, size, c ?? GlassTheme.T.textSecondary, false, 0f); tx.textWrappingMode = TextWrappingModes.Normal; tx.overflowMode = TextOverflowModes.Ellipsis;
            float h = tx.GetPreferredValues(text ?? "", Width, 0f).y + 4f;
            if (maxHeight > 0f) h = Mathf.Min(h, maxHeight);
            Lay.Place(tx.rectTransform, 0f, Y, Width, h);
            Y += h + 4f; return tx;
        }

        public bool Fits(float need) { return Y + need <= Height; }
    }

    public sealed class SheetItem
    {
        public string Label, Hint;
        public Action Do;
        public bool Enabled = true, Danger, KeepOpen;
        public SheetItem(string label, Action act, string hint = null, bool enabled = true, bool danger = false) { Label = label; Do = act; Hint = hint; Enabled = enabled; Danger = danger; }
    }

    /// <summary>
    /// The context menu of every list: a centred glass card with a title, a line of context and a vertical list of actions (A picks, B closes). Disabled actions stay visible with the reason as their hint,
    /// so the player learns why a move is not possible.
    /// </summary>
    public sealed class ActionSheet : MonoBehaviour, ICancelHandler
    {
        public static bool AnyOpen { get; set; }
        GameObject _returnTo; Action _closed; bool _done;
        public Button First;

        public static ActionSheet Show(Transform parent, string title, string subtitle, IList<SheetItem> items, Action onClosed = null)
        {
            var t = GlassTheme.T;
            var root = UiKit.Rect(parent, "ActionSheet"); UiKit.Stretch(root);
            var sheet = root.gameObject.AddComponent<ActionSheet>();
            sheet._closed = onClosed;
            sheet._returnTo = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.025f, 0.035f, 0.74f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;

            float w = 820f, rowH = 62f, gap = 10f, head = 118f;
            float h = head + items.Count * (rowH + gap) + 34f;
            h = Mathf.Min(h, 900f);
            var box = GlassKit.Panel(root, "Box", 1.7f, true, t.cornerRadiusLarge);
            UiKit.Anchor(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h)); box.SetFocus(0f);
            Lay.Head(box.rectTransform, title, 0f, 0f, w, 64f, null, 28f);
            if (!string.IsNullOrEmpty(subtitle)) Lay.Text(box.rectTransform, subtitle, 22f, t.textSecondary, 40f, 74f, w - 80f, 30f, false, TextAlignmentOptions.MidlineLeft, 0f, "Sub");
            var rule = GlassKit.Rule(box.rectTransform); Lay.Place(rule.rectTransform, 40f, 108f, w - 80f, 1.4f);
            var btns = new List<Button>();
            float y = head;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                var b = GlassKit.Button(box.rectTransform, it.Label, () => sheet.Pick(it), GlassButtonStyle.Secondary, w - 80f, rowH, null, "Item" + i, 26f);
                Lay.Place((RectTransform)b.transform, 40f, y, w - 80f, rowH);
                var lbl = b.GetComponentInChildren<TextMeshProUGUI>(); if (lbl != null) { lbl.alignment = TextAlignmentOptions.MidlineLeft; lbl.rectTransform.offsetMin = new Vector2(28f, 0f); lbl.rectTransform.offsetMax = new Vector2(-340f, 0f); }
                if (!it.Enabled) b.interactable = false;
                if (it.Danger) { var tx = b.GetComponentInChildren<TextMeshProUGUI>(); if (tx != null) { var f = b.GetComponent<GlassFocusable>(); if (f != null) f.LabelNormal = t.danger; tx.color = t.danger; } }
                if (!string.IsNullOrEmpty(it.Hint))
                {
                    var hint = Lay.Text((RectTransform)b.transform, it.Hint, 20f, t.textMuted, 0f, 0f, 330f, rowH, false, TextAlignmentOptions.MidlineRight, 0f, "Hint");
                    var hr = hint.rectTransform; hr.anchorMin = hr.anchorMax = new Vector2(1f, 0.5f); hr.pivot = new Vector2(1f, 0.5f); hr.anchoredPosition = new Vector2(-24f, 0f);
                }
                btns.Add(b); y += rowH + gap;
            }
            var live = btns.FindAll(b => b.interactable);   // disabled rows stay visible but the pad skips them (the cycle runs over the enabled ones only)
            for (int i = 0; i < live.Count; i++)
            {
                var nv = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = live[(i + live.Count - 1) % live.Count], selectOnDown = live[(i + 1) % live.Count] };
                live[i].navigation = nv;
            }
            foreach (var b in btns) if (!b.interactable) b.navigation = new Navigation { mode = Navigation.Mode.None };
            sheet.First = null;
            foreach (var b in btns) if (b.interactable) { sheet.First = b; break; }
            root.transform.SetAsLastSibling();
            AnyOpen = true;
            sheet.StartCoroutine(sheet.SelectLater(sheet.First != null ? sheet.First.gameObject : null));
            return sheet;
        }

        IEnumerator SelectLater(GameObject g)
        {
            yield return null;
            if (g != null && EventSystem.current != null) { UiAudio.Mute(0.2f); EventSystem.current.SetSelectedGameObject(g); }
        }

        void Pick(SheetItem it)
        {
            if (!it.KeepOpen) Close();
            if (it.Do != null) it.Do();
        }

        public void OnCancel(BaseEventData e) { Close(); }

        public void Close()
        {
            if (_done) return; _done = true;
            AnyOpen = false;
            var back = _returnTo;
            var cl = _closed;
            Destroy(gameObject);
            if (back != null && EventSystem.current != null && back.activeInHierarchy) EventSystem.current.SetSelectedGameObject(back);
            if (cl != null) cl();
        }

        void OnDestroy() { if (!_done) AnyOpen = false; }
    }

    /// <summary>Contract offer editor: stepper rows for money, years and signing bonus around an ask. Used by re-signing, free agency and extensions.</summary>
    public sealed class OfferEditor
    {
        public RectTransform Root;
        public OptionSelector AavSel, YearsSel, BonusSel;
        long[] _aav = new long[0];
        public Action Changed;
        static readonly float[] BonusChoices = { 0f, 0.10f, 0.20f, 0.30f, 0.40f, 0.50f };

        public long Aav { get { return _aav.Length == 0 ? 0 : _aav[Mathf.Clamp(AavSel.Index, 0, _aav.Length - 1)]; } }
        public int Years { get { return YearsSel.Index + 1; } }
        public float BonusPct { get { return BonusChoices[Mathf.Clamp(BonusSel.Index, 0, BonusChoices.Length - 1)]; } }

        /// <summary>Money options for an ask: 70 percent to 150 percent in 2.5 percent steps (rounded to 10K), minimum salary floor.</summary>
        public static long[] AavOptions(long ask, long minSalary)
        {
            var l = new List<long>();
            for (int p = 700; p <= 1500; p += 25)
            {
                long v = Math.Max(minSalary, ((long)(ask * (p / 1000.0)) / 10000L) * 10000L);
                if (l.Count == 0 || v > l[l.Count - 1]) l.Add(v);
            }
            return l.ToArray();
        }

        public static int IndexOfClosest(long[] options, long target)
        {
            int best = 0; long bd = long.MaxValue;
            for (int i = 0; i < options.Length; i++) { long d = Math.Abs(options[i] - target); if (d < bd) { bd = d; best = i; } }
            return best;
        }

        public static OfferEditor Build(RectTransform parent, float x, float y, float w, float rowH = 56f)
        {
            var e = new OfferEditor { Root = Lay.Group(parent, "OfferEditor", x, y, w, rowH * 3f + 20f) };
            e.AavSel = GlassKit.Stepper(e.Root, "Per year", new[] { "-" }, 0, i => { if (e.Changed != null) e.Changed(); }, w, rowH); Lay.Place((RectTransform)e.AavSel.transform, 0f, 0f, w, rowH);
            e.YearsSel = GlassKit.Stepper(e.Root, "Length", new[] { "1 year", "2 years", "3 years", "4 years", "5 years" }, 2, i => { if (e.Changed != null) e.Changed(); }, w, rowH); Lay.Place((RectTransform)e.YearsSel.transform, 0f, rowH + 10f, w, rowH);
            e.BonusSel = GlassKit.Stepper(e.Root, "Signing bonus", new[] { "0%", "10%", "20%", "30%", "40%", "50%" }, 2, i => { if (e.Changed != null) e.Changed(); }, w, rowH); Lay.Place((RectTransform)e.BonusSel.transform, 0f, (rowH + 10f) * 2f, w, rowH);
            e.AavSel.Wrap = false; e.YearsSel.Wrap = false; e.BonusSel.Wrap = false;
            return e;
        }

        public void Set(long ask, int years, float bonusPct, long minSalary)
        {
            _aav = AavOptions(ask, minSalary);
            var names = new string[_aav.Length];
            for (int i = 0; i < _aav.Length; i++) names[i] = Fmt.MoneyShort(_aav[i]);
            AavSel.SetOptions(names, IndexOfClosest(_aav, ask));
            YearsSel.SetOptions(YearsSel.Options, Mathf.Clamp(years, 1, 5) - 1);
            int bi = 0; float bd = 9f; for (int i = 0; i < BonusChoices.Length; i++) { float d = Mathf.Abs(BonusChoices[i] - bonusPct); if (d < bd) { bd = d; bi = i; } }
            BonusSel.SetOptions(BonusSel.Options, bi);
        }

        public IEnumerable<Selectable> Selectables() { yield return AavSel; yield return YearsSel; yield return BonusSel; }
    }

    /// <summary>Small helpers to wire explicit navigation between controls.</summary>
    public static class NavWire
    {
        public static void Set(Selectable s, Selectable up = null, Selectable down = null, Selectable left = null, Selectable right = null, bool keepAuto = false)
        {
            if (s == null) return;
            var n = s.navigation;
            if (!keepAuto) n.mode = Navigation.Mode.Explicit;
            else n.mode = Navigation.Mode.Explicit;
            n.selectOnUp = up; n.selectOnDown = down; n.selectOnLeft = left; n.selectOnRight = right;
            s.navigation = n;
        }

        /// <summary>Chains a vertical list: up / down between neighbours (the first goes up to <paramref name="top"/>, the last down to <paramref name="bottom"/>).</summary>
        public static void Column(IList<Selectable> items, Selectable top = null, Selectable bottom = null, Selectable left = null, Selectable right = null)
        {
            for (int i = 0; i < items.Count; i++)
                Set(items[i], i > 0 ? items[i - 1] : top, i + 1 < items.Count ? items[i + 1] : bottom, left, right);
        }
    }
}

namespace PixFootball.UI.Franchise
{
    /// <summary>A modal "please wait" box for the work that freezes the frame for a moment (building a league, simulating a season). Two frames are rendered before the work starts so the box is visible.</summary>
    public sealed class BusyOverlay
    {
        public GameObject Root; TextMeshProUGUI _title, _sub;

        public static BusyOverlay Create(RectTransform parent)
        {
            var t = GlassTheme.T;
            var b = new BusyOverlay();
            var root = UiKit.Rect(parent, "Busy"); UiKit.Stretch(root);
            var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.025f, 0.035f, 0.78f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            var box = GlassKit.Panel(root, "Box", 1.7f, true, t.cornerRadiusLarge); UiKit.Anchor(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 200f));
            b._title = Lay.Text(box.rectTransform, "WORKING", 40f, t.textPrimary, 0f, 40f, 760f, 60f, true, TextAlignmentOptions.Center, 5f, "Title");
            b._sub = Lay.Text(box.rectTransform, "Please wait", 24f, t.textSecondary, 0f, 112f, 760f, 40f, false, TextAlignmentOptions.Center, 0f, "Sub");
            b.Root = root.gameObject; b.Root.SetActive(false);
            return b;
        }

        public bool Visible { get { return Root != null && Root.activeSelf; } }

        public void Run(MonoBehaviour host, string label, string sub, Action work, Action<Exception> failed = null)
        {
            _title.text = label.ToUpperInvariant(); _sub.text = sub ?? "Please wait";
            if (!Application.isPlaying) { work(); return; }
            host.StartCoroutine(Routine(work, failed));
        }

        IEnumerator Routine(Action work, Action<Exception> failed)
        {
            Root.SetActive(true); Root.transform.SetAsLastSibling();
            yield return null; yield return null;
            try { work(); }
            catch (Exception e) { Debug.LogError("[PIX] franchise: " + e); if (failed != null) failed(e); }
            Root.SetActive(false);
        }
    }
}

namespace PixFootball.UI.Franchise
{
    /// <summary>A full-screen modal frame (scrim, fitted design block, dense glass box). B closes it; the focus returns to what had it. Pages fill <see cref="Box"/>.</summary>
    public sealed class ModalView : MonoBehaviour, ICancelHandler
    {
        public RectTransform Box;
        public Button Primary;
        Action _closed; bool _done; GameObject _returnTo;

        public static ModalView Create(FranchiseUi shell, Vector2 size, Action onClosed)
        {
            var t = GlassTheme.T;
            var root = UiKit.Rect(shell.Root, "Modal"); UiKit.Stretch(root);
            var m = root.gameObject.AddComponent<ModalView>(); m._closed = onClosed;
            m._returnTo = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.025f, 0.035f, 0.8f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            var host = UiKit.Rect(root, "Host"); host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = new Vector2(t.safeMarginX, 110f); host.offsetMax = new Vector2(-t.safeMarginX, -70f);
            var block = UiKit.Rect(host, "Block"); block.anchorMin = block.anchorMax = new Vector2(0.5f, 0.5f); block.pivot = new Vector2(0.5f, 0.5f); block.anchoredPosition = Vector2.zero; block.sizeDelta = size;
            var fs = host.gameObject.AddComponent<FitScale>(); fs.Block = block; fs.DesignSize = size;
            var box = GlassKit.Panel(block, "Box", 1.7f, true, t.cornerRadiusLarge); Lay.Place(box.rectTransform, 0f, 0f, size.x, size.y); box.SetFocus(0f);
            m.Box = box.rectTransform;
            root.transform.SetAsLastSibling();
            ActionSheet.AnyOpen = true;
            return m;
        }

        public void SelectFirst(Selectable s) { if (s != null && isActiveAndEnabled) StartCoroutine(SelectLater(s.gameObject)); }

        IEnumerator SelectLater(GameObject g)
        {
            yield return null;
            if (g != null && EventSystem.current != null) { UiAudio.Mute(0.2f); EventSystem.current.SetSelectedGameObject(g); }
        }

        public void OnCancel(BaseEventData e) { Close(); }

        public void Close()
        {
            if (_done) return; _done = true;
            ActionSheet.AnyOpen = false;
            var back = _returnTo; var cl = _closed;
            Destroy(gameObject);
            if (back != null && EventSystem.current != null && back.activeInHierarchy) EventSystem.current.SetSelectedGameObject(back);
            if (cl != null) cl();
        }

        void OnDestroy() { if (!_done) ActionSheet.AnyOpen = false; }
    }

    /// <summary>A modal with a title and a list of text lines (lines that start with # are small headings): free agency results, camp results, the draft recap.</summary>
    public static class InfoModal
    {
        public static ModalView Show(FranchiseUi shell, string title, string kicker, IList<string> lines, string closeLabel = "CONTINUE", Action onClosed = null)
        {
            var t = GlassTheme.T;
            int rows = Mathf.Min(lines.Count, 15);
            float h = 200f + rows * 38f + 100f;
            var size = new Vector2(1100f, Mathf.Clamp(h, 420f, 860f));
            var m = ModalView.Create(shell, size, onClosed);
            Lay.Head(m.Box, kicker, 0f, 0f, size.x, 56f, null, 24f);
            Lay.Text(m.Box, title.ToUpperInvariant(), 42f, t.textPrimary, 44f, 62f, size.x - 88f, 56f, true, TextAlignmentOptions.MidlineLeft, 2f, "Title");
            var rule = GlassKit.Rule(m.Box); Lay.Place(rule.rectTransform, 44f, 122f, size.x - 88f, 1.4f);
            float y = 138f;
            for (int i = 0; i < rows; i++)
            {
                string l = lines[i];
                if (l.StartsWith("#")) Lay.Caps(m.Box, l.Substring(1).Trim(), 44f, y + 4f, size.x - 88f, 28f, t.textSecondary, 20f);
                else Lay.Text(m.Box, l, 22f, t.textPrimary, 44f, y, size.x - 88f, 36f, false, TextAlignmentOptions.MidlineLeft, 0f, "Line" + i);
                y += 38f;
            }
            if (lines.Count > rows) Lay.Text(m.Box, "+ " + (lines.Count - rows) + " more", 20f, t.textMuted, 44f, y, 400f, 30f, false);
            var b = GlassKit.Button(m.Box, closeLabel, () => m.Close(), GlassButtonStyle.Primary, 380f, 66f, null, "Close");
            Lay.Place((RectTransform)b.transform, size.x * 0.5f - 190f, size.y - 92f, 380f, 66f);
            m.Primary = b; m.SelectFirst(b);
            return m;
        }
    }
}
