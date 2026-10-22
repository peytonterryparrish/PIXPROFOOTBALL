using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>
    /// Save / load. In the Unity editor the round trip goes through JsonUtility (the real save format); in the plain-.NET test harness a
    /// reflection serializer that keeps only public fields (the JsonUtility rules) stands in for it.
    /// </summary>
    public class CareerSaveTests
    {
        internal static CareerSave RoundTrip(CareerSave s, out int jsonLength)
        {
#if UNITY_2017_1_OR_NEWER
            string json = UnityEngine.JsonUtility.ToJson(s);
            jsonLength = json.Length;
            CareerSave back = UnityEngine.JsonUtility.FromJson<CareerSave>(json);
#else
            var opts = new System.Text.Json.JsonSerializerOptions { IncludeFields = true, MaxDepth = 64 };
            opts.TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    ti =>
                    {
                        // JsonUtility only sees public instance fields that are not [NonSerialized]
                        for (int i = ti.Properties.Count - 1; i >= 0; i--)
                        {
                            var attr = ti.Properties[i].AttributeProvider;
                            var f = attr as FieldInfo;
                            if (f == null || f.IsStatic || f.IsNotSerialized) ti.Properties.RemoveAt(i);
                        }
                    }
                }
            };
            string json = System.Text.Json.JsonSerializer.Serialize(s, opts);
            jsonLength = json.Length;
            CareerSave back = System.Text.Json.JsonSerializer.Deserialize<CareerSave>(json, opts);
            // JsonUtility never produces null for a [Serializable] class field: a missing college world loads as an empty one
            if (back.League.College == null) back.League.College = new CollegeWorld();
#endif
            back.Bind();
            return back;
        }

        private static CareerSave RoundTrip(CareerSave s) { int len; return RoundTrip(s, out len); }

        [Test]
        public void ASeasonInProgressSurvivesTheRoundTrip()
        {
            CareerSave s = CareerTestUtil.Fresh(141);
            CareerFlow.Advance(s);
            for (int w = 0; w < 6; w++) CareerFlow.Advance(s);
            // some state in every list
            Team u = s.UserTeam;
            RosterManager.Release(s, u, u.Roster.Where(p => !Offseason.IsStarter(u, p)).OrderBy(p => p.Overall).First().Id);
            DepthManager.Swap(s, u, Position.QB, 0, 1);
            TradeEngine.GenerateOffers(s, new Rng(1)); TradeEngine.GenerateOffers(s, new Rng(2));
            int len;
            CareerSave back = RoundTrip(s, out len);
            Assert.Greater(len, 1000000);
            Assert.AreEqual(s.Phase, back.Phase);
            Assert.AreEqual(s.Season.Week, back.Season.Week);
            Assert.AreEqual(s.UserTeamId, back.UserTeamId);
            Assert.AreEqual(CareerTestUtil.Fingerprint(s), CareerTestUtil.Fingerprint(back));
            Assert.AreEqual(s.Picks.Count, back.Picks.Count);
            Assert.AreEqual(s.News.Count, back.News.Count);
            Assert.AreEqual(s.Transactions.Count, back.Transactions.Count);
            Assert.AreEqual(s.Waivers.Count, back.Waivers.Count);
            Assert.AreEqual(s.Inbox.Count, back.Inbox.Count);
            Assert.AreEqual(s.ManualDepth.Count, back.ManualDepth.Count);
            Assert.AreEqual(s.CoachPool.Count, back.CoachPool.Count);
            Assert.AreEqual(s.StaffContracts.Count, back.StaffContracts.Count);
            Assert.AreEqual(s.Settings.TradeDeadlineWeek, back.Settings.TradeDeadlineWeek);
            Assert.AreEqual(s.CapBase, back.CapBase);
            Assert.AreEqual(s.UserTeam.PracticeSquad.Count, back.UserTeam.PracticeSquad.Count);
            Assert.AreEqual(s.UserTeam.InjuredReserve.Count, back.UserTeam.InjuredReserve.Count);
            Assert.AreEqual(s.UserTeam.CapUsed, back.UserTeam.CapUsed);
            Player a = s.Pro.AllRosterPlayers().First(p => p.Abilities.Count > 0), b = back.FindPlayer(a.Id);
            Assert.AreEqual(a.Abilities.Count, b.Abilities.Count);
            Assert.AreEqual(a.Abilities[0].Id, b.Abilities[0].Id);
            Assert.AreEqual(a.Overall, b.Overall);
            Assert.AreEqual(s.NewRng().NextULong(), back.NewRng().NextULong(), "the career random stream continues identically");
            CareerTestUtil.AssertClean(back, false);
        }

        [Test]
        public void ALoadedCareerKeepsSimulatingExactlyLikeTheOriginal()
        {
            CareerSave s = CareerTestUtil.Fresh(142);
            CareerFlow.Advance(s);
            for (int w = 0; w < 4; w++) CareerFlow.Advance(s);
            CareerSave back = RoundTrip(s);
            CareerFlow.AdvanceUntil(s, CareerPhase.Draft);
            CareerFlow.AdvanceUntil(back, CareerPhase.Draft);
            Assert.AreEqual(CareerTestUtil.Fingerprint(s), CareerTestUtil.Fingerprint(back));
            Assert.AreEqual(s.Season.ChampionTeamId, back.Season.ChampionTeamId);
            Assert.AreEqual(s.AllPro.Count, back.AllPro.Count);
            Assert.AreEqual(s.Trophies.Count, back.Trophies.Count);
            Assert.AreEqual(s.Draft.Slots.Count, back.Draft.Slots.Count);
            // save in the middle of the draft and continue
            CareerFlow.Advance(s); CareerFlow.Advance(back);
            CareerSave mid = RoundTrip(back);
            Assert.AreEqual(s.Draft.Current, mid.Draft.Current);
            CareerFlow.AdvanceUntil(s, CareerPhase.Preseason);
            CareerFlow.AdvanceUntil(mid, CareerPhase.Preseason);
            Assert.AreEqual(CareerTestUtil.Fingerprint(s), CareerTestUtil.Fingerprint(mid));
            Assert.AreEqual(s.HallOfFame.Count, mid.HallOfFame.Count);
            CareerTestUtil.AssertClean(mid, false);
        }

        [Test]
        public void EveryPhaseCanBeSavedAndLoaded()
        {
            CareerSave s = CareerTestUtil.Fresh(143);
            var seen = new HashSet<CareerPhase>();
            int guard = 0;
            while (guard++ < 200 && !(s.Year == 2027 && s.Phase == CareerPhase.Preseason))
            {
                if (seen.Add(s.Phase) || s.Phase == CareerPhase.RegularSeason && s.Season.Week == 9)
                {
                    CareerSave back = RoundTrip(s);
                    Assert.AreEqual(s.Phase, back.Phase);
                    Assert.AreEqual(CareerTestUtil.Fingerprint(s), CareerTestUtil.Fingerprint(back), s.Phase.ToString());
                    // the loaded career can take the next step
                    CareerFlow.AutoResolveUser(back);
                    Assert.IsTrue(CareerFlow.Advance(back).Ok, "advance after loading in " + s.Phase);
                }
                CareerFlow.AutoResolveUser(s);
                Assert.IsTrue(CareerFlow.Advance(s).Ok);
            }
            Assert.AreEqual(12, seen.Count, "all twelve phases were visited");
        }

        [Test]
        public void SavedDataFollowsTheJsonUtilityRules()
        {
            // every serializable type in the league assembly: public fields only (or [NonSerialized]), nothing JsonUtility cannot store
            var bad = new List<string>();
            Assembly asm = typeof(CareerSave).Assembly;
            foreach (Type t in asm.GetTypes())
            {
                if (!t.IsDefined(typeof(SerializableAttribute), false) || !t.IsClass && !t.IsValueType || t.IsEnum || t.Namespace != "PixFootball.League") continue;
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsNotSerialized) continue;
                    if (f.Name.Contains("<")) continue;   // compiler backing field
                    if (!f.IsPublic) { bad.Add(t.Name + "." + f.Name + " is not public"); continue; }
                    string why = Unsupported(f.FieldType);
                    if (why != null) bad.Add(t.Name + "." + f.Name + ": " + why);
                }
                if (t.IsAbstract) bad.Add(t.Name + " is abstract");
            }
            Assert.AreEqual(0, bad.Count, string.Join("\n", bad.ToArray()));
        }

        private static string Unsupported(Type ft)
        {
            if (ft.IsPrimitive || ft == typeof(string) || ft.IsEnum) return null;
            if (ft.IsGenericType)
            {
                Type def = ft.GetGenericTypeDefinition();
                if (def == typeof(List<>)) return Unsupported(ft.GetGenericArguments()[0]);
                return ft.Name + " is not supported (use List<T>)";
            }
            if (ft.IsArray) return "arrays of custom types are not supported, use List<T>";
            if (ft.IsInterface || ft.IsAbstract) return ft.Name + " is abstract";
            if (typeof(IDictionary).IsAssignableFrom(ft)) return "dictionary";
            if (ft == typeof(Rng)) return "Rng";
            if (!ft.IsDefined(typeof(SerializableAttribute), false)) return ft.Name + " is not [Serializable]";
            return null;
        }

        [Test]
        public void TheSaveStaysAReasonableSizeAfterTenYearsOfLogs()
        {
            CareerSave s = CareerTestUtil.Fresh(144);
            for (int y = 0; y < 3; y++) CareerFlow.SimToNextSeason(s);
            int len;
            CareerSave back = RoundTrip(s, out len);
            Assert.Less(len, 60000000, "json size " + len);
            Assert.LessOrEqual(back.News.Count, NewsWriter.MaxItems);
            Assert.LessOrEqual(back.Transactions.Count, Ledger.MaxTransactions);
            Assert.AreEqual(3, back.Pro.History.Count);
            Assert.AreEqual(3, back.AllPro.Count(a => a.Team == 1));
            TestContext.WriteLine("career save json after 3 years: " + len / 1024 + " KB");
        }
    }
}
