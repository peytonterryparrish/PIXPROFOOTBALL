#if UNITY_2017_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>The league state is plain [Serializable] data so Unity's JsonUtility can save and load it.</summary>
    public class LeagueSerializationTests
    {
        [Test]
        public void ProLeagueRoundTripsThroughJsonUtilityAndKeepsSimulatingIdentically()
        {
            ProLeague lg = LeagueFactory.CreatePro(5);
            for (int i = 0; i < 3; i++) lg.Season.SimWeek();
            var save = new LeagueSave { Pro = lg };
            string json = UnityEngine.JsonUtility.ToJson(save);
            Assert.Greater(json.Length, 1000000);
            LeagueSave back = UnityEngine.JsonUtility.FromJson<LeagueSave>(json);
            back.Bind();

            Assert.AreEqual(32, back.Pro.Teams.Count);
            Assert.AreEqual(lg.Year, back.Pro.Year);
            Assert.AreEqual(LeagueTestUtil.Fingerprint(lg), LeagueTestUtil.Fingerprint(back.Pro));
            Assert.AreEqual(LeagueTestUtil.Fingerprint(lg.Season), LeagueTestUtil.Fingerprint(back.Pro.Season));
            Assert.AreEqual(lg.Season.Week, back.Pro.Season.Week);
            Assert.AreEqual(lg.Teams[3].CapUsed, back.Pro.Teams[3].CapUsed);
            Assert.AreEqual(lg.Teams[3].Staff[0].Name, back.Pro.Teams[3].Staff[0].Name);
            Assert.AreEqual(lg.Teams[3].Depth.StarterId(Position.QB), back.Pro.Teams[3].Depth.StarterId(Position.QB));
            Player p0 = lg.Teams[7].Roster[10], p1 = back.Pro.Teams[7].Roster[10];
            Assert.AreEqual(p0.Name, p1.Name);
            Assert.AreEqual(p0.Overall, p1.Overall);
            Assert.AreEqual(p0.Contract.CapHits.Count, p1.Contract.CapHits.Count);
            Assert.AreEqual(p0.Career.Count, p1.Career.Count);

            // determinism survives the save: both leagues play the same next weeks
            lg.Season.SimWeek(); back.Pro.Season.SimWeek();
            lg.Season.SimToEnd(); back.Pro.Season.SimToEnd();
            Assert.AreEqual(LeagueTestUtil.Fingerprint(lg.Season), LeagueTestUtil.Fingerprint(back.Pro.Season));
            Assert.AreEqual(lg.Season.MvpId, back.Pro.Season.MvpId);
            Assert.AreEqual(lg.Season.ChampionTeamId, back.Pro.Season.ChampionTeamId);
        }

        [Test]
        public void SavedOffseasonStateRoundTrips()
        {
            ProLeague lg = LeagueFactory.CreatePro(6);
            lg.Season.SimToEnd();
            Offseason.Run(lg, null, new Rng(1));
            string json = UnityEngine.JsonUtility.ToJson(new LeagueSave { Pro = lg });
            LeagueSave back = UnityEngine.JsonUtility.FromJson<LeagueSave>(json);
            back.Bind();
            Assert.AreEqual(LeagueTestUtil.Fingerprint(lg), LeagueTestUtil.Fingerprint(back.Pro));
            Assert.AreEqual(lg.History.Count, back.Pro.History.Count);
            Assert.AreEqual(lg.LastDraft.Count, back.Pro.LastDraft.Count);
            Assert.AreEqual(lg.Records.Entries.Count, back.Pro.Records.Entries.Count);
            Assert.AreEqual(lg.RetiredPlayers.Count, back.Pro.RetiredPlayers.Count);
            Assert.AreEqual(lg.GetRng().NextULong(), back.Pro.GetRng().NextULong());
        }

        [Test]
        public void CollegeWorldRoundTripsWithPollsAndPostseason()
        {
            CollegeWorld w = LeagueTestUtil.LoadCollege(9);
            w.Season.SimToEnd();
            var save = new LeagueSave { College = w };
            string json = UnityEngine.JsonUtility.ToJson(save);
            LeagueSave back = UnityEngine.JsonUtility.FromJson<LeagueSave>(json);
            back.Bind();
            Assert.AreEqual(138, back.College.Schools.Count);
            Assert.AreEqual(41, back.College.Bowls.Count);
            Assert.AreEqual(LeagueTestUtil.Fingerprint(w.Season), LeagueTestUtil.Fingerprint(back.College.Season));
            Assert.AreEqual(w.Season.Polls.Count, back.College.Season.Polls.Count);
            Assert.AreEqual(w.Season.GoldenHelmId, back.College.Season.GoldenHelmId);
            Assert.AreEqual(w.Schools[5].Name, back.College.Schools[5].Name);
            Assert.AreEqual(LeagueTestUtil.Fingerprint(w.Schools[5].Team), LeagueTestUtil.Fingerprint(back.College.Schools[5].Team));
            Assert.AreEqual(w.Season.Seeds.Count, back.College.Season.Seeds.Count);
        }

        [Test]
        public void SerializedTypesAvoidDictionariesAndReferences()
        {
            // every serialized root field must be a primitive, enum, string, list of serializable classes or a serializable class
            var roots = new[] { typeof(LeagueSave), typeof(ProLeague), typeof(CollegeWorld), typeof(Team), typeof(Player), typeof(Season), typeof(School) };
            foreach (Type t in roots) CheckType(t, new HashSet<Type>());
        }

        private static void CheckType(Type t, HashSet<Type> seen)
        {
            if (!seen.Add(t)) return;
            Assert.IsTrue(t.IsDefined(typeof(SerializableAttribute), false), t.Name + " must be [Serializable]");
            foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (f.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                Type ft = f.FieldType;
                if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>)) ft = ft.GetGenericArguments()[0];
                Assert.IsFalse(ft.IsGenericType, t.Name + "." + f.Name + " uses generic " + ft);
                if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string)) continue;
                if (ft == typeof(Ratings)) continue;
                CheckType(ft, seen);
            }
        }
    }
}
#endif
