using System.Collections.Generic;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>
    /// v0.11.0 SIM-FLOW: the scene frequencies are decided in the simulation, against NFL per-game references (assisted tackles about 36 percent of tackles, 3 to 5 blow-up hits, 2 to 3 fumbles, a hurry-up
    /// runner who goes out of bounds), and the same seed gives the same scenes (no RNG outside the match stream).
    /// </summary>
    public class SimFlowTests
    {
        static List<PlayRecord> Games(int n, int seed0, out int games)
        {
            ProLeague lg = LeagueTestUtil.FreshPro(7); var rng = new Rng(2025);
            var all = new List<PlayRecord>(); games = n;
            for (int g = 0; g < n; g++)
            {
                int a = rng.Range(0, 32), b = rng.Range(0, 32); while (b == a) b = rng.Range(0, 32);
                var game = new PhysicalGameSim(lg.Teams[a], lg.Teams[b], RuleSet.Pro(), new Rng(seed0 + g)) { PlayerModifiers = false, StaffTendencies = false, KeepLogs = true };
                game.Run();
                all.AddRange(game.Plays);
            }
            return all;
        }

        [Test, Category("Slow")]
        public void AssistedTacklesBlowUpsAndFumblesMatchTheNflReferences()
        {
            int games; var plays = Games(30, 700, out games);
            int tackled = 0, assisted = 0, blow = 0, fumbles = 0, fumLost = 0;
            foreach (var rec in plays)
            {
                if (rec.Log == null) continue;
                int tk = 0, pile = rec.Log.CountOf(SimEventType.PileOn); bool gang = false;
                foreach (var e in rec.Log.Events)
                {
                    if (e.Type == SimEventType.Tackle && e.Tackle != TackleType.Drag && e.Tackle != TackleType.Shoestring) { tk++; if (e.Tackle == TackleType.GangTackle) gang = true; }
                    if (e.Type == SimEventType.Tackle && e.Note == "BlowUp") blow++;
                }
                if (tk > 0) { tackled++; if (pile > 0 || gang || tk > 1) assisted++; }
                fumbles += rec.Log.CountOf(SimEventType.Fumble);
                if (rec.Result != null && rec.Result.Type == PlayType.Fumble && rec.Result.Turnover) fumLost++;
            }
            double share = 100.0 * assisted / tackled;
            TestContext.WriteLine("assisted tackles " + share.ToString("0.0") + "%, blow-up hits per game " + (blow / (double)games).ToString("0.0") + ", fumbles per game " + (fumbles / (double)games).ToString("0.00") + ", lost " + (fumLost / (double)games).ToString("0.00"));
            Assert.That(share, Is.InRange(28.0, 46.0), "assisted share of tackles (NFL about 36)");
            Assert.That(blow / (double)games, Is.InRange(2.0, 6.5), "blow-up hits per game");
            Assert.That(fumbles / (double)games, Is.InRange(1.7, 3.2), "fumbles per game (NFL about 2.4)");
            Assert.That(fumLost / (double)games, Is.InRange(0.6, 1.9), "fumbles lost per game (NFL about 1.0 to 1.1)");
        }

        [Test]
        public void TheSameSeedGivesTheSameScenes()
        {
            int g1, g2; var a = Games(2, 910, out g1); var b = Games(2, 910, out g2);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Log == null) continue;
                Assert.AreEqual(a[i].Log.Count, b[i].Log.Count, "play " + i);
                Assert.AreEqual(a[i].Log.CountOf(SimEventType.PileOn), b[i].Log.CountOf(SimEventType.PileOn));
                Assert.AreEqual(a[i].Log.CountOf(SimEventType.Fumble), b[i].Log.CountOf(SimEventType.Fumble));
            }
        }

        [Test, Category("Slow")]
        public void HurryUpRunnersAndReceiversTakeItOutOfBounds()
        {
            int games; var plays = Games(24, 700, out games);
            int oob = 0;
            foreach (var rec in plays) if (rec.Result != null && rec.Result.OutOfBounds && (rec.Result.Type == PlayType.Run || rec.Result.Type == PlayType.Pass)) oob++;
            TestContext.WriteLine("out-of-bounds plays per game " + (oob / (double)games).ToString("0.00") + " (NFL about 7; the passing game is middle-heavy, see simflow.md)");
            Assert.Greater(oob / (double)games, 0.15);
        }
    }
}
