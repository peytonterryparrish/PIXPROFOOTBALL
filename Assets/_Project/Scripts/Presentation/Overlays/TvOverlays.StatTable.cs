using PixFootball.League;
using PixFootball.UI;
using TMPro;
using UnityEngine;

namespace PixFootball.Presentation
{
    public sealed partial class TvOverlays
    {
        const float TableHeaderH = 30f, TableRowH = 58f, TableColW = 98f;

        /// <summary>
        /// The 2K8 player stat strip: a thin dark header row of small muted caps ("#  NAME  TEAM  ATT  YDS  AVG  LNG  TD"), one bigger data row under it, a team-colour accent at the left.
        /// Lives in the lower-third slot (above a bottom scorebug bar), so it replaces and queues exactly like the old lower-third card.
        /// </summary>
        Card MakeStatTable(Player p, StatTable table, TeamVis team, string tag)
        {
            Vector2 hidden;
            const float numX = 28f, nameX = 92f, teamX = 388f, colsX = 494f;   // v0.11 B-085: a long surname no longer touches the team column
            int n = table.Headers.Length;
            float w = colsX + n * TableColW + 20f, h = TableHeaderH + TableRowH;
            var root = NewCard(_root, "StatTable", new Vector2(0, 0), new Vector2(0, 0), new Vector2(80, 160), new Vector2(w, h), new Vector2(-(w + 160f), 0), out hidden);

            Color headerBg = new Color(0f, 0f, 0f, 0.9f), rowBg = TvUi.Navy;
            TvUi.Bar(root, "RowBg", rowBg, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(w, TableRowH));
            TvUi.Bar(root, "HeaderBg", headerBg, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, TableRowH), new Vector2(w, TableHeaderH));
            TvUi.Bar(root, "Accent", team.Color, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(12, h));
            TvUi.Bar(root, "Rule", TvUi.Gold, new Vector2(0, 0), new Vector2(0, 0), new Vector2(12, 0), new Vector2(w - 12f, 3));

            Color muted = new Color(0.62f, 0.64f, 0.7f, 1f);
            Vector2 z = new Vector2(0, 0);
            // header row
            TvUi.Text(root, "#", 20, muted, true, TextAlignmentOptions.MidlineLeft, z, z, new Vector2(numX, TableRowH), new Vector2(60, TableHeaderH), 2f);
            TvUi.Text(root, "NAME", 20, muted, true, TextAlignmentOptions.MidlineLeft, z, z, new Vector2(nameX, TableRowH), new Vector2(270, TableHeaderH), 2f);
            TvUi.Text(root, "TEAM", 20, muted, true, TextAlignmentOptions.MidlineLeft, z, z, new Vector2(teamX, TableRowH), new Vector2(100, TableHeaderH), 2f);
            for (int i = 0; i < n; i++)
                TvUi.Text(root, table.Headers[i], 20, muted, true, TextAlignmentOptions.Center, z, z, new Vector2(colsX + i * TableColW, TableRowH), new Vector2(TableColW, TableHeaderH), 2f);
            // data row
            TvUi.Text(root, p.Bio.JerseyNumber.ToString(), 36, TvUi.Gold, true, TextAlignmentOptions.MidlineLeft, z, z, new Vector2(numX, 2), new Vector2(60, TableRowH - 4f));
            TvUi.Text(root, OverlayText.ShortName(p.Name), 38, Color.white, true, TextAlignmentOptions.MidlineLeft, z, z, new Vector2(nameX, 2), new Vector2(284, TableRowH - 4f), 1f);
            TvUi.Text(root, team.Abbr, 32, Color.white, true, TextAlignmentOptions.MidlineLeft, z, z, new Vector2(teamX, 2), new Vector2(100, TableRowH - 4f), 1.5f);
            for (int i = 0; i < n; i++)
                TvUi.Text(root, table.Values[i], 38, Color.white, true, TextAlignmentOptions.Center, z, z, new Vector2(colsX + i * TableColW, 2), new Vector2(TableColW, TableRowH - 4f));

            // optional tag chip ("BIG PLAY", "TD PASS") sitting on the strip's top edge
            if (!string.IsNullOrEmpty(tag))
            {
                float cw = 36f + tag.Length * 13f;
                var chip = TvUi.Bar(root, "Tag", TvUi.Gold, z, z, new Vector2(12, h), new Vector2(cw, 28));
                TvUi.Text(chip.transform, tag.ToUpperInvariant(), 20, new Color(0.06f, 0.07f, 0.1f), true, TextAlignmentOptions.Center, z, z, Vector2.zero, new Vector2(cw, 28), 2f);
            }
            return new Card { Root = root, Rest = root.anchoredPosition, Hidden = hidden };
        }
    }
}
