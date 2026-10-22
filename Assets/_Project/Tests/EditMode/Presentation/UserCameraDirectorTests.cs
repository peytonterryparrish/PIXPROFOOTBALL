using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Presentation;

namespace PixFootball.Tests
{
    /// <summary>v0.6: with a human playing, the camera director never fights the user camera.</summary>
    public class UserCameraDirectorTests
    {
        [Test]
        public void NoCinematicCutBeforeTheSnapWhileAHumanPlaysExceptTheIntro()
        {
            var ctx = new DirectorContext { Cinematics = 0, FirstPlayOfDrive = true, AfterScore = true, PreSnapSeconds = 20f, UserPlaying = true };
            Assert.AreEqual(0, ShotRules.PreSnap(ctx).Count, "no establishing shot while the user reads the defense / picks a hot route");
            ctx.GameStart = true;
            Assert.AreEqual(1, ShotRules.PreSnap(ctx).Count, "the game intro still plays");
            ctx.UserPlaying = false; ctx.GameStart = false;
            Assert.Greater(ShotRules.PreSnap(ctx).Count, 0, "a CPU-vs-CPU game keeps its director");
        }

        [Test]
        public void PostWhistleCutsAreBriefWhileAHumanPlays()
        {
            var ctx = new DirectorContext { Cinematics = 0, Headline = Headline.Touchdown, Touchdown = true, Score = true, HomeBenefits = true, CarrierKnown = true, PlaysSinceCrowd = 9, UserPlaying = true };
            var user = ShotRules.PostPlay(ctx);
            Assert.LessOrEqual(ShotRules.Total(user), 3.01f);
            ctx.UserPlaying = false;
            var cpu = ShotRules.PostPlay(ctx);
            Assert.Greater(ShotRules.Total(cpu), ShotRules.Total(user));
            Assert.IsFalse(user.Any(s => s.Duration > 1.61f));
        }
    }
}
