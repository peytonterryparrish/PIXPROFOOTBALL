using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.UI;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>
    /// UI/FLOW audit fixes in the play-call screen, the pre-snap panel and the penalty panel: responsive layout maths (QA-775 / 776 / 777 / 778 / 786), prompt wrapping,
    /// tags (QA-780), card text (QA-781 / 67 / 64 / 65) and the small text rules (QA-63 / 165 / 795 / 166). Pure helpers only, no scene needed.
    /// </summary>
    public class UiFlowPlayCallUiTests
    {
        static readonly Playbook Book = DefaultPlaybooks.Create();

        // screens: 4:3, 16:10, 16:9, 21:9 and 32:9 style ultra wide at a typical resolution each
        static readonly float[][] Screens = { new[] { 1600f, 1200f }, new[] { 1920f, 1200f }, new[] { 1920f, 1080f }, new[] { 2560f, 1080f }, new[] { 3440f, 1440f } };

        // ------------------------------------------------------------------------------------------- canvas size maths
        [Test]
        public void CanvasSizeFollowsTheMatchHalfScaler()
        {
            Vector2 s43 = PlayCallLayout.CanvasSizeFor(1600f, 1200f);
            Assert.AreEqual(1663f, s43.x, 3f); Assert.AreEqual(1247f, s43.y, 3f);
            Vector2 s169 = PlayCallLayout.CanvasSizeFor(1920f, 1080f);
            Assert.AreEqual(1920f, s169.x, 0.5f); Assert.AreEqual(1080f, s169.y, 0.5f);
            Vector2 s219 = PlayCallLayout.CanvasSizeFor(2560f, 1080f);
            Assert.AreEqual(935f, s219.y, 6f);
            Assert.AreEqual(1920f, PlayCallLayout.CanvasSizeFor(3840f, 2160f).x, 0.5f, "resolution does not matter, aspect does");
        }

        [Test]
        public void SixteenByNineKeepsTheOriginalPlayCallLayout()
        {
            PlayCallRects r = PlayCallLayout.Compute(1920f, 1080f, 1);
            Assert.AreEqual(80f, r.ListX); Assert.AreEqual(520f, r.ListW); Assert.AreEqual(650f, r.CardsX);
            Assert.AreEqual(378f, r.CardW); Assert.AreEqual(1190f, r.CardsW, 1f);
            Assert.AreEqual(678f, r.ContentH, 1f);
            Assert.AreEqual(1046f, r.ScoreX, 1f);
        }

        [Test]
        public void EveryAspectKeepsCardsFooterAndTopBarInsideTheCanvas()
        {
            foreach (float[] sc in Screens)
                foreach (int rows in new[] { 1, 2 })
                {
                    Vector2 cs = PlayCallLayout.CanvasSizeFor(sc[0], sc[1]);
                    PlayCallRects r = PlayCallLayout.Compute(cs.x, cs.y, rows);
                    string tag = sc[0] + "x" + sc[1] + " rows " + rows;
                    // horizontal: list, three cards and the preview stay between the margins (QA-776)
                    Assert.GreaterOrEqual(r.ListX, PlayCallLayout.Margin - 0.01f, tag);
                    Assert.LessOrEqual(r.CardsX + r.CardsW, cs.x - PlayCallLayout.Margin + 0.5f, tag + ": right card is cut off");
                    Assert.GreaterOrEqual(r.CardsX, r.ListX + r.ListW, tag + ": list overlaps the cards");
                    Assert.GreaterOrEqual(r.CardW, PlayCallLayout.MinCardW, tag);
                    // vertical: the card block ends above the page counter and the footer (QA-777)
                    Assert.LessOrEqual(r.ContentTop + r.CardH + PlayCallLayout.PageTextBand + r.FooterH, cs.y + 0.5f, tag + ": cards run under the footer");
                    Assert.LessOrEqual(100f + r.DiagramH + 62f + r.InfoH + 58f, r.CardH + 0.5f, tag + ": card content taller than the card");
                    Assert.GreaterOrEqual(r.InfoH, 60f, tag);
                    Assert.LessOrEqual(r.ContentTop + r.PreviewH, cs.y - r.FooterH, tag + ": preview under the footer");
                    Assert.LessOrEqual(r.PreviewTextX + r.PreviewTextW, r.CardsW + 0.5f, tag + ": preview text outside its panel");
                    // top bar: team text, score column and the timeout button sit side by side (QA-778)
                    Assert.LessOrEqual(r.TeamX + r.TeamW + 24f, r.ScoreX + 0.5f, tag);
                    Assert.LessOrEqual(r.ScoreX + r.ScoreW + 24f, r.TimeoutLeft + 0.5f, tag + ": score overlaps the timeout button");
                    Assert.LessOrEqual(r.FooterAvail, cs.x - 2f * PlayCallLayout.Margin + 0.01f, tag);
                    Assert.Greater(r.FooterH, 80f);
                }
        }

        [Test]
        public void FooterGrowsWithItsRows()
        {
            Assert.Greater(PlayCallLayout.Compute(1920f, 1080f, 2).FooterH, PlayCallLayout.Compute(1920f, 1080f, 1).FooterH);
            Assert.Less(PlayCallLayout.Compute(1920f, 1080f, 2).ContentH, PlayCallLayout.Compute(1920f, 1080f, 1).ContentH);
        }

        [Test]
        public void FitScaleOnlyShrinksAndNeverBelowSeventyPercent()
        {
            Assert.AreEqual(1f, PlayCallLayout.FitScale(500f, 600f));
            Assert.AreEqual(0.8f, PlayCallLayout.FitScale(1000f, 800f), 1e-4f);
            Assert.AreEqual(0.7f, PlayCallLayout.FitScale(1000f, 100f), 1e-4f);
        }

        // ------------------------------------------------------------------------------------------- prompt wrapping
        [Test]
        public void PromptsWrapToAnotherRowInsteadOfRunningOffScreen()
        {
            CollectionAssert.AreEqual(new[] { 0 }, PromptWrap.Break(new float[] { 100, 100, 100 }, 10f, 1000f));
            CollectionAssert.AreEqual(new[] { 0, 2 }, PromptWrap.Break(new float[] { 100, 100, 100 }, 10f, 250f));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, PromptWrap.Break(new float[] { 200, 200, 200 }, 10f, 250f));
            CollectionAssert.AreEqual(new[] { 0, 1 }, PromptWrap.Break(new float[] { 100, 100, 100, 100 }, 10f, 150f, 2), "the last allowed row takes the rest");
            CollectionAssert.AreEqual(new[] { 0 }, PromptWrap.Break(new float[0], 10f, 100f));
            CollectionAssert.AreEqual(new[] { 0, 1 }, PromptWrap.Break(new float[] { 900, 50 }, 10f, 400f), "an item wider than a row stays alone");
            // every produced row fits (when no item is wider than the row)
            var w = new float[] { 160, 120, 210, 90, 300, 140, 180, 100, 220 };
            int[] starts = PromptWrap.Break(w, 40f, 600f, 10);
            for (int r = 0; r < starts.Length; r++)
            {
                int end = r + 1 < starts.Length ? starts[r + 1] : w.Length;
                float sum = 0f; for (int i = starts[r]; i < end; i++) sum += w[i] + (i > starts[r] ? 40f : 0f);
                Assert.LessOrEqual(sum, 600f, "row " + r);
            }
        }

        // ------------------------------------------------------------------------------------------- tags (QA-780)
        [Test]
        public void PlayActionGoalLineAndPreventHaveTheirOwnTags()
        {
            Func<PlayCategory, string> tag = c => PlayTags.For(new Play { Category = c });
            Assert.AreEqual("PLAY ACTION", tag(PlayCategory.PlayAction));
            Assert.AreEqual("GOAL LINE", tag(PlayCategory.GoalLine));
            Assert.AreEqual("PREVENT", tag(PlayCategory.Prevent));
            Assert.AreEqual("ZONE", tag(PlayCategory.ZoneCoverage));
            Assert.AreEqual("BLITZ", tag(PlayCategory.Blitz));
            Assert.AreEqual("RUN", tag(PlayCategory.Draw));
            Assert.AreEqual("PASS", tag(PlayCategory.Screen));
            Assert.AreEqual("ONSIDE", tag(PlayCategory.Onside));
            Assert.AreEqual("RETURN", tag(PlayCategory.PuntReturn));
            Assert.AreEqual("tag_play_action", PlayTags.SpriteKey("PLAY ACTION"));
            var unknown = new Color(1f, 0f, 1f);
            foreach (PlayCategory c in Enum.GetValues(typeof(PlayCategory)))
                Assert.AreNotEqual(unknown, PlayTags.ColorOf(PlayTags.For(new Play { Category = c }), unknown), c + " has a tag without a colour");
            Assert.AreNotEqual(PlayTags.ColorOf("GOAL LINE"), PlayTags.ColorOf("BLITZ"));
            Assert.AreNotEqual(PlayTags.ColorOf("PREVENT"), PlayTags.ColorOf("ZONE"));
        }

        // ------------------------------------------------------------------------------------------- card text
        [Test]
        public void DescribeNeverThrowsOnShortOrCustomAssignmentArrays()
        {
            foreach (PlaySide side in new[] { PlaySide.Offense, PlaySide.Defense, PlaySide.Special })
                foreach (PlayCategory cat in new[] { PlayCategory.InsideRun, PlayCategory.DeepPass, PlayCategory.ZoneCoverage, PlayCategory.Punt })
                    foreach (Assignment[] asg in new[] { null, new Assignment[0], new Assignment[1], new Assignment[3], new Assignment[Formation.SlotCount] })
                    {
                        var p = new Play { Id = "x", Name = "x", FormationName = "Singleback", Side = side, Category = cat, Assignments = asg };
                        string text = null;
                        Assert.DoesNotThrow(() => text = PlayCallScreen.Describe(Book, p), side + " " + cat + " " + (asg == null ? "null" : asg.Length.ToString()));
                        Assert.IsNotNull(text);
                    }
            Assert.DoesNotThrow(() => PlayCallScreen.Describe(Book, null));
        }

        [Test]
        public void EveryPlayDescribesInPlainWords()
        {
            foreach (Play p in Book.AllPlays)
            {
                string t = PlayCallScreen.Describe(Book, p);
                Assert.IsFalse(string.IsNullOrWhiteSpace(t), p.ToString());
                StringAssert.DoesNotContain(" GAP", t, p + ": gap letters are jargon");
                if (p.Side == PlaySide.Special) Assert.AreNotEqual(PlayTags.For(p), t.Trim(), p + ": the text only repeats the category chip");
            }
            Assert.AreEqual("OFF TACKLE RIGHT", PlayCallScreen.GapText(RunGap.CRight));
            Assert.AreEqual("UP THE MIDDLE", PlayCallScreen.GapText(RunGap.ALeft));
            Assert.AreEqual("TO THE EDGE LEFT", PlayCallScreen.GapText(RunGap.DLeft));
            Assert.AreEqual("", PlayCallScreen.GapText(RunGap.None));
        }

        [Test]
        public void SpecialTeamsCardsDescribeThePlan()
        {
            Play punt = Book.PlaysFor(DefaultPlaybooks.PuntF).First(p => p.Category == PlayCategory.Punt);
            string t = PlayCallScreen.Describe(Book, punt);
            Assert.AreNotEqual("PUNT", t.Trim());
            Play ret = Book.PlaysFor(DefaultPlaybooks.PuntReturnF).First();
            Assert.IsNotEmpty(PlayCallScreen.Describe(Book, ret));
        }

        // ------------------------------------------------------------------------------------------- pre-snap and penalty text
        [Test]
        public void PreSnapModeLineIsUpperCaseWithOneSeparator()
        {
            Assert.AreEqual("SHOTGUN  -  HOT ROUTE: X", PreSnapPanel.ModeLine("Shotgun", "hot route: x"));
            Assert.AreEqual("SHOTGUN", PreSnapPanel.ModeLine("shotgun", ""));
            Assert.AreEqual("MOTION", PreSnapPanel.ModeLine("", "Motion"));
            Assert.AreEqual("", PreSnapPanel.ModeLine(null, null));
            Assert.Greater(PreSnapPanel.PlayBlockY, 242f, "the play block clears the TV lower-thirds, which end at y 242");
        }

        [Test]
        public void RefereeTextReadsNaturallyAndMetaLineUsesOneSeparator()
        {
            Assert.AreEqual("Offside, number 77, on the Reefrunners defense. 5-yard penalty, repeat 1st down.",
                PenaltyPanel.NaturalReferee("Offside, number 77, defense (Reefrunners). 5-yard penalty, repeat 1st down."));
            Assert.AreEqual("Delay of Game, on the Ironhorns offense. 5-yard penalty.", PenaltyPanel.NaturalReferee("Delay of Game, offense (Ironhorns). 5-yard penalty."));
            Assert.AreEqual("Penalty declined. Holding on the Reefrunners offense.", PenaltyPanel.NaturalReferee("Penalty declined. Holding offense (Reefrunners)."));
            Assert.AreEqual("No team here.", PenaltyPanel.NaturalReferee("No team here."));
            Assert.AreEqual("", PenaltyPanel.NaturalReferee(null));

            Assert.AreEqual("REEFRUNNERS - #77 - 5 YARDS - LOSS OF DOWN", PenaltyPanel.MetaLine("REEFRUNNERS", "#77", "5 YARDS  -  LOSS OF DOWN"));
            Assert.AreEqual("REEFRUNNERS - 10 YARDS", PenaltyPanel.MetaLine("REEFRUNNERS", "", "10 YARDS"));
            Assert.AreEqual("DECIDES IN 20 SEC", PenaltyPanel.CountdownText(20f));
            Assert.AreEqual("DECIDES IN 3 SEC", PenaltyPanel.CountdownText(2.2f));
            Assert.AreEqual("DECIDES IN 0 SEC", PenaltyPanel.CountdownText(-1f));
            Assert.AreEqual("COACH SUGGESTS: ACCEPT", PenaltyPanel.RecommendText(true));
            StringAssert.DoesNotContain("  ", PenaltyPanel.RecommendText(false));
        }
    }
}
