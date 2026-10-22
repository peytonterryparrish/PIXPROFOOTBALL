using System;
using System.Collections;
using System.Collections.Generic;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI.Franchise
{
    /// <summary>What the franchise shell needs from the main menu.</summary>
    public sealed class FranchiseHost
    {
        /// <summary>Leave the franchise (back to the tile hub).</summary>
        public Action Exit;
        /// <summary>The section caption of the top bar.</summary>
        public Action<string> Section;
        /// <summary>The footer prompts of the active page.</summary>
        public Action<IList<PromptItem>> Footer;
        /// <summary>Starts the Unity match (the main menu fades its music out first).</summary>
        public Action<Action> Leave;
    }

    /// <summary>A screen of the franchise. Pages are built once into the shell's design block and shown / hidden; they never own the canvas.</summary>
    public abstract class FranchisePage
    {
        public RectTransform Root;
        public FranchiseUi Shell;
        protected FranchiseController Ctl { get { return Shell.Ctl; } }
        protected CareerSave Save { get { return Shell.Ctl.Save; } }
        public bool Built { get; protected set; }

        /// <summary>Builds the page into <paramref name="parent"/> (size = <see cref="FranchiseUi.PageSize"/>).</summary>
        public void EnsureBuilt(RectTransform parent)
        {
            if (Built) return;
            Root = UiKit.Rect(parent, GetType().Name); UiKit.Stretch(Root);
            Root.gameObject.AddComponent<CanvasGroup>();
            Build(Root);
            Built = true;
        }

        protected abstract void Build(RectTransform root);
        /// <summary>The page became visible: bind data.</summary>
        public virtual void OnShow() { Refresh(); }
        public virtual void OnHide() { }
        /// <summary>Called every frame while the page is shown (the draft clock).</summary>
        public virtual void Tick(float dt) { }
        public abstract void Refresh();
        /// <summary>Where the pad lands when the page opens.</summary>
        public virtual Selectable Entry { get { return null; } }
        /// <summary>B pressed: return true when the page used it (closing a detail, ...).</summary>
        public virtual bool HandleBack() { return false; }
        public virtual PromptItem[] Prompts() { return new PromptItem[0]; }
    }

    /// <summary>
    /// The franchise shell (reference: the franchise hub of the console football sims): a tab strip HOME / TEAM / LEAGUE / OFFICE in the top bar (LB / RB), a design block with a sub-tab strip and
    /// the page below (LT / RT), the league wire ticker along the bottom, toasts, a busy overlay for long simulations and an overlay slot for full screen flows (the draft). Everything is glass.
    /// </summary>
    public sealed class FranchiseUi : MonoBehaviour
    {
        public static readonly Vector2 PageSize = new Vector2(1728f, 732f);
        public const float SubTabsHeight = 56f;
        public static Vector2 ContentSize { get { return new Vector2(PageSize.x, PageSize.y - SubTabsHeight - 10f); } }

        public FranchiseController Ctl { get; private set; }
        public FranchiseHost Host;
        public RectTransform Root, Block, ContentHost, OverlayHost;
        public GlassTabs Tabs, SubTabs;
        public GlassTicker Ticker;
        public GlassToast ToastView;
        public int TabIndex { get; private set; }
        public int SubIndex { get; private set; }

        sealed class TabSlot { public List<FranchisePage> Pages = new List<FranchisePage>(); public Func<FranchisePage>[] Factories; public int Sub; }
        readonly List<TabSlot> _slots = new List<TabSlot>();
        FranchisePage _homePage, _overlay;
        GameObject _busy; TextMeshProUGUI _busyText;
        RectTransform _subTabsHolder;
        bool _open; int _modalFrame = -10;

        // ------------------------------------------------------------------ construction
        public static FranchiseUi Build(RectTransform canvas, FranchiseController ctl, FranchiseHost host)
        {
            var t = GlassTheme.T;
            var go = UiKit.Rect(canvas, "Franchise"); UiKit.Stretch(go);
            var ui = go.gameObject.AddComponent<FranchiseUi>();
            ui.Root = go; ui.Ctl = ctl; ui.Host = host;
            ctl.Changed += ui.OnChanged;

            // ---- tab strip in the top bar (same place as the menu hub tabs)
            var bar = UiKit.Rect(go, "TabBar");
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f); bar.pivot = new Vector2(0.5f, 1f); bar.anchoredPosition = new Vector2(0f, -t.safeMarginY); bar.sizeDelta = new Vector2(-t.safeMarginX * 2f, 76f);
            ui.Tabs = GlassKit.Tabs(bar, FranchiseNav.Tabs, 0, i => ui.ShowTab(i), 56f, 24f, "MainTabs");
            ui.Tabs.BumperPaging = false;
            var tr = (RectTransform)ui.Tabs.transform; tr.anchorMin = tr.anchorMax = new Vector2(1f, 0.5f); tr.pivot = new Vector2(1f, 0.5f); tr.anchoredPosition = Vector2.zero;
            var fit = tr.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // ---- design block (shrinks on short canvases like the hub)
            var hostRt = UiKit.Rect(go, "FitHost");
            hostRt.anchorMin = Vector2.zero; hostRt.anchorMax = Vector2.one; hostRt.offsetMin = new Vector2(t.safeMarginX, 196f); hostRt.offsetMax = new Vector2(-t.safeMarginX, -152f);
            var block = UiKit.Rect(hostRt, "Block");
            block.anchorMin = block.anchorMax = new Vector2(0f, 1f); block.pivot = new Vector2(0f, 1f); block.anchoredPosition = Vector2.zero; block.sizeDelta = PageSize;
            var fs = hostRt.gameObject.AddComponent<FitScale>(); fs.Block = block; fs.DesignSize = PageSize;
            ui.Block = block;

            ui._subTabsHolder = Lay.Group(block, "SubTabs", 0f, 0f, PageSize.x, SubTabsHeight);
            ui.ContentHost = Lay.Group(block, "Content", 0f, SubTabsHeight + 10f, ContentSize.x, ContentSize.y);
            ui.OverlayHost = Lay.Group(block, "Overlay", 0f, 0f, PageSize.x, PageSize.y); ui.OverlayHost.gameObject.SetActive(false);

            // ---- ticker
            var strip = UiKit.Rect(go, "TickerHost");
            strip.anchorMin = new Vector2(0f, 0f); strip.anchorMax = new Vector2(1f, 0f); strip.pivot = new Vector2(0.5f, 0f);
            strip.offsetMin = new Vector2(t.safeMarginX, 124f); strip.offsetMax = new Vector2(-t.safeMarginX, 124f + 52f);
            ui.Ticker = GlassTicker.Build(strip, "LEAGUE WIRE", 52f, 24f);

            ui.ToastView = GlassToast.Create(go, 190f);
            ui.BuildBusy(go);

            // ---- pages
            ui._homePage = new HomePage { Shell = ui };
            ui._slots.Add(new TabSlot());
            ui._slots.Add(new TabSlot { Factories = new Func<FranchisePage>[] { () => new RosterPage(), () => new DepthPage(), () => new SquadPage() } });
            ui._slots.Add(new TabSlot { Factories = new Func<FranchisePage>[] { () => new SchedulePage(), () => new StandingsPage(), () => new LeadersPage(), () => new AwardsPage(), () => new RecordsPage(), () => new TrophyPage(), () => new NewsPage() } });
            ui._slots.Add(new TabSlot { Factories = new Func<FranchisePage>[] { () => new CapPage(), () => new ReSignPage(), () => new FreeAgencyPage(), () => new TradesPage(), () => new StaffPage(), () => new OwnerPage() } });
            for (int i = 1; i < ui._slots.Count; i++) foreach (var f in ui._slots[i].Factories) ui._slots[i].Pages.Add(null);
            ui.SubTabs = null;
            return ui;
        }

        void BuildBusy(RectTransform parent)
        {
            var t = GlassTheme.T;
            var root = UiKit.Rect(parent, "Busy"); UiKit.Stretch(root);
            var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.025f, 0.035f, 0.78f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            var box = GlassKit.Panel(root, "Box", 1.7f, true, t.cornerRadiusLarge); UiKit.Anchor(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 200f));
            _busyText = Lay.Text(box.rectTransform, "SIMULATING", 40f, t.textPrimary, 0f, 40f, 760f, 60f, true, TextAlignmentOptions.Center, 5f, "Title");
            var sub = Lay.Text(box.rectTransform, "Please wait", 24f, t.textSecondary, 0f, 112f, 760f, 40f, false, TextAlignmentOptions.Center, 0f, "Sub");
            _busy = root.gameObject; _busy.SetActive(false);
        }

        // ------------------------------------------------------------------ lifecycle
        public void Open(int tab = 0, int sub = -1)
        {
            _open = true; Root.gameObject.SetActive(true);
            if (Host != null && Host.Section != null) Host.Section("FRANCHISE");
            RefreshTicker();
            ShowTab(tab, sub);
        }

        public void Close() { _open = false; Root.gameObject.SetActive(false); }

        void OnDestroy() { if (Ctl != null) Ctl.Changed -= OnChanged; }

        void OnChanged() { if (_open) { RefreshTicker(); CurrentPage().Refresh(); } }

        public void RefreshTicker()
        {
            var items = NewsWriter.Ticker(Ctl.Save, 12);
            var l = new List<string>();
            foreach (var n in items) l.Add(n.Headline);
            if (l.Count == 0) l.Add("The " + Ctl.Save.Year + " season is about to begin");
            Ticker.SetHeadlines(l);
        }

        // ------------------------------------------------------------------ tabs and pages
        FranchisePage CurrentPage()
        {
            if (_overlay != null) return _overlay;
            if (TabIndex == 0) return _homePage;
            return PageAt(TabIndex, SubIndex);
        }

        FranchisePage PageAt(int tab, int sub)
        {
            var slot = _slots[tab];
            if (slot.Pages[sub] == null) { var p = slot.Factories[sub](); p.Shell = this; slot.Pages[sub] = p; }
            return slot.Pages[sub];
        }

        public FranchisePage PageOf(int tab, int sub) { return tab == 0 ? _homePage : PageAt(tab, sub); }

        public void ShowTab(int tab, int sub = -1)
        {
            tab = Mathf.Clamp(tab, 0, FranchiseNav.Tabs.Length - 1);
            CloseOverlay(false);
            HidePage(CurrentPage());
            TabIndex = tab;
            Tabs.SetIndex(tab, false);
            if (_subTabsHolder.childCount > 0) for (int i = _subTabsHolder.childCount - 1; i >= 0; i--) UiKit.Kill(_subTabsHolder.GetChild(i).gameObject);
            SubTabs = null;
            if (tab == 0) { SubIndex = 0; _subTabsHolder.gameObject.SetActive(false); ContentHost.anchoredPosition = new Vector2(0f, 0f); ContentHost.sizeDelta = PageSize; }
            else
            {
                var slot = _slots[tab];
                SubIndex = Mathf.Clamp(sub >= 0 ? sub : slot.Sub, 0, slot.Pages.Count - 1);
                _subTabsHolder.gameObject.SetActive(true);
                ContentHost.anchoredPosition = new Vector2(0f, -(SubTabsHeight + 10f)); ContentHost.sizeDelta = ContentSize;
                SubTabs = GlassKit.Tabs(_subTabsHolder, FranchiseNav.SubTabs[tab], SubIndex, i => ShowSub(i), 52f, 22f, "SubTabs");
                SubTabs.BumperPaging = false;
                var rt = (RectTransform)SubTabs.transform; rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f); rt.anchoredPosition = Vector2.zero;
                var fit = rt.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                if (Application.isPlaying) GlassSkin.Apply(_subTabsHolder);
            }
            ShowPage(CurrentPage(), true);
        }

        public void ShowSub(int sub)
        {
            if (TabIndex == 0) return;
            var slot = _slots[TabIndex];
            sub = Mathf.Clamp(sub, 0, slot.Pages.Count - 1);
            HidePage(CurrentPage());
            SubIndex = sub; slot.Sub = sub;
            if (SubTabs != null) SubTabs.SetIndex(sub, false);
            ShowPage(CurrentPage(), false);
        }

        void HidePage(FranchisePage p) { if (p != null && p.Built) { p.OnHide(); p.Root.gameObject.SetActive(false); } }

        void ShowPage(FranchisePage p, bool focusEntry)
        {
            p.Shell = this;
            p.EnsureBuilt(_overlay != null && ReferenceEquals(p, _overlay) ? OverlayHost : ContentHost);
            p.Root.gameObject.SetActive(true);
            p.OnShow();
            UpdateFooter();
            if (focusEntry || true) StartCoroutine(FocusLater(p));
        }

        IEnumerator FocusLater(FranchisePage p)
        {
            yield return null;
            if (!_open || p == null || !p.Built) yield break;
            var e = p.Entry;
            if (e == null || !e.gameObject.activeInHierarchy) e = TabIndex > 0 && SubTabs != null && SubTabs.Buttons.Length > 0 ? SubTabs.Buttons[Mathf.Clamp(SubIndex, 0, SubTabs.Buttons.Length - 1)] : Tabs.Buttons[TabIndex];
            if (e != null && EventSystem.current != null) { UiAudio.Mute(0.15f); EventSystem.current.SetSelectedGameObject(e.gameObject); }
        }

        public void Focus(Selectable s) { if (s != null && EventSystem.current != null && s.gameObject.activeInHierarchy) EventSystem.current.SetSelectedGameObject(s.gameObject); }

        public void FocusNextFrame(Selectable s) { StartCoroutine(FocusSel(s)); }
        IEnumerator FocusSel(Selectable s) { yield return null; Focus(s); }

        // ------------------------------------------------------------------ overlays (the draft)
        public void ShowOverlay(FranchisePage p)
        {
            if (_overlay != null) CloseOverlay(false);
            HidePage(CurrentPage());
            _overlay = p; p.Shell = this;
            OverlayHost.gameObject.SetActive(true); ContentHost.gameObject.SetActive(false); _subTabsHolder.gameObject.SetActive(false);
            p.EnsureBuilt(OverlayHost); p.Root.gameObject.SetActive(true); p.OnShow();
            UpdateFooter(); StartCoroutine(FocusLater(p));
        }

        public void CloseOverlay(bool restore = true)
        {
            if (_overlay == null) return;
            _overlay.OnHide(); if (_overlay.Built) _overlay.Root.gameObject.SetActive(false);
            _overlay = null;
            OverlayHost.gameObject.SetActive(false); ContentHost.gameObject.SetActive(true);
            if (restore) { _subTabsHolder.gameObject.SetActive(TabIndex > 0); ShowPage(CurrentPage(), true); }
        }

        public bool HasOverlay { get { return _overlay != null; } }

        // ------------------------------------------------------------------ messages and busy
        public void Toast(string text, string icon = "ui_info")
        {
            if (string.IsNullOrEmpty(text)) return;
            if (Application.isPlaying) ToastView.Show(text, 3.2f, icon); else ToastView.ShowNow(text, icon);
        }

        public void Confirm(string title, string message, string yes, Action onYes, string no = "CANCEL")
        {
            UiDialog.Show(Root, title, message, yes, no, onYes, null);
        }

        /// <summary>Shows the busy overlay for two frames, runs <paramref name="work"/> and hides it (the long sims freeze the frame for a second or two).</summary>
        public void Busy(string label, Action work)
        {
            if (!Application.isPlaying) { work(); return; }
            StartCoroutine(BusyRoutine(label, work));
        }

        IEnumerator BusyRoutine(string label, Action work)
        {
            _busyText.text = label.ToUpperInvariant(); _busy.SetActive(true); _busy.transform.SetAsLastSibling();
            yield return null; yield return null;
            try { work(); }
            catch (Exception e) { Debug.LogError("[PIX] franchise: " + e); Toast("Something went wrong: " + e.Message, "ui_info"); }
            _busy.SetActive(false);
            Ctl.Touch();
        }

        public bool IsBusy { get { return _busy != null && _busy.activeSelf; } }

        // ------------------------------------------------------------------ footer
        public void UpdateFooter()
        {
            if (Host == null || Host.Footer == null) return;
            var l = new List<PromptItem> { PromptFooter.Item("glyph_a", "SELECT"), PromptFooter.Item("glyph_b", "BACK") };
            l.AddRange(CurrentPage().Prompts());
            if (_overlay == null)
            {
                l.Add(new PromptItem(new[] { "glyph_lb", "glyph_rb" }, "TABS"));
                if (TabIndex > 0) l.Add(new PromptItem(new[] { "glyph_lt", "glyph_rt" }, "SECTIONS"));
            }
            Host.Footer(l);
        }

        // ------------------------------------------------------------------ input
        void Update()
        {
            if (!_open) return;
            if (!IsBusy && Application.isPlaying) CurrentPage().Tick(Time.unscaledDeltaTime);
            if (IsBusy || UiDialog.AnyOpen || ActionSheet.AnyOpen) { _modalFrame = Time.frameCount; return; }
            if (Time.frameCount - _modalFrame <= 1) return;   // the B that just closed a dialog or a sheet must not also go back a screen
            if (_overlay == null)
            {
                int d = FranchiseInput.Pressed(PadBtn.LB) ? -1 : FranchiseInput.Pressed(PadBtn.RB) ? 1 : 0;
                if (d != 0) { ShowTab(FranchiseInput.Wrap(TabIndex, d, FranchiseNav.Tabs.Length)); UiAudio.Click(); return; }
                int s = FranchiseInput.Pressed(PadBtn.LT) ? -1 : FranchiseInput.Pressed(PadBtn.RT) ? 1 : 0;
                if (s != 0 && TabIndex > 0) { ShowSub(FranchiseInput.Wrap(SubIndex, s, _slots[TabIndex].Pages.Count)); UiAudio.Click(); return; }
            }
            if (FranchiseInput.BackPressed()) HandleBack();
        }

        public void HandleBack()
        {
            if (CurrentPage().HandleBack()) return;
            if (_overlay != null) { CloseOverlay(true); return; }
            if (TabIndex != 0) { ShowTab(0); return; }
            Confirm("Leave the franchise?", "Your progress is saved after every week and phase. You can continue from the career tile of the main menu.", "LEAVE", () => { Ctl.Autosave(); if (Host != null && Host.Exit != null) Host.Exit(); });
        }
    }
}
