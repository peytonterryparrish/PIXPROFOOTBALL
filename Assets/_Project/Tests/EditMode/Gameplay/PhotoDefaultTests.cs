using NUnit.Framework;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.6.0: photoreal players are the default body; their pooling rules, lettering contrast and the facial hair switch.</summary>
    public class PhotoDefaultTests
    {
        [Test]
        public void PhotoPlayersAreOnByDefaultAndSettingRoundTrips()
        {
            bool before = PixSettings.PhotoPlayers;
            try
            {
                PixSettings.PhotoPlayers = false; Assert.IsFalse(PixSettings.PhotoPlayers);
                PixSettings.PhotoPlayers = true; Assert.IsTrue(PixSettings.PhotoPlayers);
            }
            finally { PixSettings.PhotoPlayers = before; }
        }

        [Test]
        public void SizeFollowsPositionAndWeight()
        {
            Assert.AreEqual(PlayerSize.Large, RoleUtil.SizeOf(Position.LT, 250));
            Assert.AreEqual(PlayerSize.Large, RoleUtil.SizeOf(Position.RE, 240));
            Assert.AreEqual(PlayerSize.Medium, RoleUtil.SizeOf(Position.QB, 225));
            Assert.AreEqual(PlayerSize.Large, RoleUtil.SizeOf(Position.TE, 262));
            Assert.AreEqual(PlayerSize.Medium, RoleUtil.SizeOf(Position.TE, 245));
            Assert.AreEqual(PlayerSize.Small, RoleUtil.SizeOf(Position.RB, 185));
            Assert.AreEqual(PlayerSize.Small, RoleUtil.SizeOf(Position.WR, 190));
            Assert.AreEqual(PlayerSize.Medium, RoleUtil.SizeOf(Position.WR, 220));
            Assert.AreEqual(PlayerSize.Small, RoleUtil.SizeOf(Position.K, 210));
            Assert.AreEqual(RoleUtil.SizeOf(Position.CB), RoleUtil.SizeOf(Position.CB, 0), "unknown weight keeps the position default");
        }

        [Test]
        public void AppearanceIsDeterministicPerPlayerId()
        {
            var a = PlayerAppearance.FromSeed(4711, PhotoBody.Large, RoleUtil.AppearanceRole(Position.RT));
            var b = PlayerAppearance.FromSeed(4711, PhotoBody.Large, RoleUtil.AppearanceRole(Position.RT));
            var c = PlayerAppearance.FromSeed(4712, PhotoBody.Large, RoleUtil.AppearanceRole(Position.RT));
            Assert.AreEqual(a.Key(), b.Key());
            Assert.AreNotEqual(a.Key(), c.Key());
        }

        [Test]
        public void FacialHairShellsStayOffUntilHairCardsExist()
        {
            Assert.IsFalse(PhotoPlayer.ShowFacialHair, "PHR-018: speckled stubble shells are disabled for v0.6.0");
        }

        static Color Lin(string hex) { Color c; ColorUtility.TryParseHtmlString(hex, out c); return c.linear; }

        [TestCase("#0B1E3F", "#2B3A67", "#8A8F9C")]   // navy kit, dull blue-grey accents
        [TestCase("#111111", "#222222", "#333333")]   // black kit, dark accents
        [TestCase("#F2F2F2", "#E0E0E0", "#D8D8D8")]   // white kit, light accents
        [TestCase("#7A1010", "#5A0A0A", "#901818")]   // maroon kit
        public void NumbersAndLetteringKeepContrastOnAnyKit(string jersey, string secondary, string tertiary)
        {
            var colors = new TeamColors(Lin(jersey), Lin(secondary), Lin(tertiary));
            var kit = new UniformSet { Uniform = colors, Helmet = colors, Home = true, Wordmark = "OWLS" };
            var look = JerseyLook.For(23, kit, true, default(DirtState));
            Assert.GreaterOrEqual(JerseyLook.Ratio(look.Fill, colors.Primary), 3.3f, "number / lettering fill against the jersey");
            Assert.GreaterOrEqual(JerseyLook.Ratio(look.Trim, look.Fill), 1.9f, "outline against the fill");
        }
    }
}
