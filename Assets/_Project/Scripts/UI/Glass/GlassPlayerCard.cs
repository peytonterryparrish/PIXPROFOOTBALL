using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{

    /// <summary>Player card (the pack's "player spotlight portrait"): a portrait slot (live render, or the silhouette until a render is assigned), name in two lines, position, OVERALL box and three attribute stats.</summary>
    public sealed class GlassPlayerCard
    {
        public RectTransform Root;
        public RawImage Portrait;
        public TextMeshProUGUI First, Last, Position, Overall, OverallLabel;
        public GameObject SilhouetteRoot;
        public GlassPortraitStage Stage;
        public TextMeshProUGUI[] StatValue = new TextMeshProUGUI[3], StatLabel = new TextMeshProUGUI[3];

        public static GlassPlayerCard Create(Transform parent, string first, string last, string position, int overall, string[] statLabels, int[] statValues, float width = 780f, float height = 520f, Color? teamColor = null)
        {
            var t = GlassTheme.T;
            var pc = new GlassPlayerCard();
            var card = GlassWidgets.Card(parent, "PLAYER SPOTLIGHT", width, height, "PlayerCard");
            pc.Root = card.Root;
            float pad = t.panelPadding;
            float slotW = height * 0.55f, slotH = height - 140f;
            // portrait well + live render
            var slot = GlassKit.Well(card.Root, "PortraitSlot", t.cornerRadius);
            UiKit.Anchor(slot.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(pad, pad), new Vector2(slotW, slotH));
            slot.Sheen = 1f;
            var silHolder = UiKit.Rect(slot.rectTransform, "Silhouette"); UiKit.Anchor(silHolder, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 24f), new Vector2(slotW * 0.62f, slotW * 0.8f));
            var head = GlassKit.Surface(silHolder, "Head", GlassKind.Glass, slotW * 0.14f, false, 0.6f); UiKit.Anchor(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(slotW * 0.28f, slotW * 0.28f)); head.Brighten = 0.03f;
            var body = GlassKit.Surface(silHolder, "Shoulders", GlassKind.Glass, slotW * 0.2f, false, 0.6f); UiKit.Anchor(body.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(slotW * 0.62f, slotW * 0.44f)); body.Brighten = 0.03f;
            pc.SilhouetteRoot = silHolder.gameObject;
            var rawGo = UiKit.Rect(slot.rectTransform, "Portrait"); UiKit.Stretch(rawGo, 4f, 4f, 4f, 4f);
            pc.Portrait = rawGo.gameObject.AddComponent<RawImage>(); pc.Portrait.raycastTarget = false; pc.Portrait.enabled = false;
            var cap = GlassKit.Eyebrow(slot.rectTransform, "PLAYER PORTRAIT", TextAlignmentOptions.MidlineLeft, "Caption"); GlassKit.Style(cap, 15f, t.textMuted, true, 2f);
            UiKit.Anchor(cap.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 12f), new Vector2(slotW - 32f, 24f));

            float x = pad + slotW + 34f, w = width - x - pad;
            pc.First = GlassKit.Text(card.Root, first.ToUpperInvariant(), GlassText.Caption, TextAlignmentOptions.BottomLeft, "First"); GlassKit.Style(pc.First, 24f, t.textSecondary, true, 5f);
            UiKit.Anchor(pc.First.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -92f), new Vector2(w, 34f));
            pc.Last = GlassKit.Text(card.Root, last.ToUpperInvariant(), GlassText.Title, TextAlignmentOptions.TopLeft, "Last"); GlassKit.Style(pc.Last, 60f, t.textPrimary, true, 3f);
            UiKit.Anchor(pc.Last.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -124f), new Vector2(w, 74f)); UiKit.Fit(pc.Last, 34f);
            pc.Position = GlassKit.Text(card.Root, position, GlassText.Label, TextAlignmentOptions.MidlineLeft, "Position"); GlassKit.Style(pc.Position, 24f, t.textSecondary, false, 2f);
            UiKit.Anchor(pc.Position.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -204f), new Vector2(w, 34f));
            // OVERALL box (orange outline like the pack)
            var ovr = GlassKit.Surface(card.Root, "OverallBox", GlassKind.Glass, t.cornerRadiusSmall, false, 1f); ovr.SetFocus(0.9f); ovr.GlowScale = 0.22f;
            UiKit.Anchor(ovr.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -252f), new Vector2(170f, 104f));
            pc.Overall = GlassKit.Text(ovr.rectTransform, overall.ToString(), GlassText.Title, TextAlignmentOptions.Center, "Value"); GlassKit.Style(pc.Overall, 56f, t.accent, true);
            UiKit.Anchor(pc.Overall.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(160f, 66f));
            pc.OverallLabel = GlassKit.Eyebrow(ovr.rectTransform, "OVERALL", TextAlignmentOptions.Center, "Label"); GlassKit.Style(pc.OverallLabel, 18f, t.textSecondary, false, 2f);
            UiKit.Anchor(pc.OverallLabel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(160f, 26f));
            // three attribute stats
            var rule = GlassKit.Rule(card.Root); var rr = rule.rectTransform; rr.anchorMin = new Vector2(0f, 0f); rr.anchorMax = new Vector2(0f, 0f); rr.pivot = new Vector2(0f, 0f); rr.anchoredPosition = new Vector2(x, pad + 98f); rr.sizeDelta = new Vector2(w, 1.4f);
            for (int i = 0; i < 3; i++)
            {
                string lab = statLabels != null && i < statLabels.Length ? statLabels[i] : "";
                int val = statValues != null && i < statValues.Length ? statValues[i] : 0;
                float cw = w / 3f;
                var sl = GlassKit.Eyebrow(card.Root, lab, TextAlignmentOptions.MidlineLeft, "StatLabel" + i); GlassKit.Style(sl, 20f, t.textSecondary, false, 2f);
                UiKit.Anchor(sl.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x + i * cw, pad + 62f), new Vector2(cw, 30f));
                var sv = GlassKit.Text(card.Root, val.ToString(), GlassText.Heading, TextAlignmentOptions.MidlineLeft, "StatValue" + i); GlassKit.Style(sv, 46f, t.textPrimary, true);
                UiKit.Anchor(sv.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x + i * cw, pad), new Vector2(cw, 56f));
                pc.StatLabel[i] = sl; pc.StatValue[i] = sv;
            }
            if (teamColor.HasValue)
            {
                var c = teamColor.Value;
                var bar = GlassKit.Solid(card.Root, "TeamBar", new Color(c.r, c.g, c.b, 0.9f), 2f);
                UiKit.Anchor(bar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -86f), new Vector2(46f, 4f));
            }
            return pc;
        }

        /// <summary>Starts a live portrait of the photoreal player in the slot.</summary>
        public void StartLivePortrait(TeamColors colors, int w = 480, int h = 640)
        {
            if (Stage != null) Stage.Dispose();
            Stage = GlassPortraitStage.Create(colors, w, h);
            Portrait.texture = Stage.Target; Portrait.enabled = true;
            if (SilhouetteRoot != null) SilhouetteRoot.SetActive(false);
        }

        public void Dispose() { if (Stage != null) { Stage.Dispose(); Stage = null; } }
    }
}
