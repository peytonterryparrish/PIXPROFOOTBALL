using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Presentation;

namespace PixFootball.Tests
{
    public class StatTableTests
    {
        [Test]
        public void PasserGetsCmpAttYdsTdInt()
        {
            var t = StatTable.For(new StatLine { PassAtt = 24, PassCmp = 18, PassYds = 231, PassTd = 2, PassInt = 1 }, Position.QB);
            Assert.AreEqual(StatTableKind.Passer, t.Kind);
            CollectionAssert.AreEqual(new[] { "CMP", "ATT", "YDS", "TD", "INT" }, t.Headers);
            CollectionAssert.AreEqual(new[] { "18", "24", "231", "2", "1" }, t.Values);
        }

        [Test]
        public void RusherGetsAverageAndLong()
        {
            var t = StatTable.For(new StatLine { RushAtt = 14, RushYds = 88, RushTd = 1, RushLong = 27 }, Position.RB);
            Assert.AreEqual(StatTableKind.Rusher, t.Kind);
            CollectionAssert.AreEqual(new[] { "ATT", "YDS", "AVG", "LNG", "TD" }, t.Headers);
            CollectionAssert.AreEqual(new[] { "14", "88", "6.3", "27", "1" }, t.Values);
        }

        [Test]
        public void ReceiverGetsRecYdsAvgLngTd()
        {
            var t = StatTable.For(new StatLine { Rec = 5, RecYds = 92, RecLong = 41 }, Position.WR);
            Assert.AreEqual(StatTableKind.Receiver, t.Kind);
            CollectionAssert.AreEqual(new[] { "REC", "YDS", "AVG", "LNG", "TD" }, t.Headers);
            CollectionAssert.AreEqual(new[] { "5", "92", "18.4", "41", "0" }, t.Values);
        }

        [Test]
        public void DefenderGetsTklSackInt()
        {
            var t = StatTable.For(new StatLine { Tackles = 6, Sacks = 2 }, Position.LE);
            Assert.AreEqual(StatTableKind.Defender, t.Kind);
            CollectionAssert.AreEqual(new[] { "TKL", "SACK", "INT" }, t.Headers);
            CollectionAssert.AreEqual(new[] { "6", "2", "0" }, t.Values);
        }

        [Test]
        public void EmptyLineYieldsNoTableAndHeadersMatchValues()
        {
            Assert.IsNull(StatTable.For(new StatLine(), Position.WR));
            Assert.IsNull(StatTable.For(null, Position.QB));
            var all = new[]
            {
                StatTable.For(new StatLine { PassAtt = 3, PassCmp = 2 }, Position.QB), StatTable.For(new StatLine { RushAtt = 3 }, Position.RB), StatTable.For(new StatLine { Rec = 1 }, Position.WR),
                StatTable.For(new StatLine { Tackles = 1 }, Position.MLB), StatTable.For(new StatLine { FgAtt = 2, FgMade = 1, FgLong = 38 }, Position.K), StatTable.For(new StatLine { KrAtt = 2, KrYds = 41 }, Position.WR)
            };
            foreach (var t in all) { Assert.IsNotNull(t); Assert.AreEqual(t.Headers.Length, t.Values.Length); }
        }

        [Test]
        public void ColumnChoiceAgreesWithTheStatLineText()
        {
            // the table picks the same category as the one-line text: a line that has text has a table and vice versa
            var lines = new[] { new StatLine(), new StatLine { PassAtt = 5 }, new StatLine { RushAtt = 2 }, new StatLine { Rec = 3 }, new StatLine { Sacks = 1 }, new StatLine { FgAtt = 1 }, new StatLine { KrAtt = 1 } };
            foreach (var l in lines) Assert.AreEqual(OverlayText.StatLine(l, Position.WR).Length > 0, StatTable.For(l, Position.WR) != null);
        }
    }
}
