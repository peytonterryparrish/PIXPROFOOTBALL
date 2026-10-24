using NUnit.Framework;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.11.0 CRITICAL: culling mode of the match animators, the wide kickoff lens and the body health helper.</summary>
    public class CriticalTests
    {
        [Test]
        public void MatchAnimatorsAreNeverCulled()
        {
            // an off-screen body keeps being posed (CullUpdateTransforms left the layers working on a stale pose); -pixCullUpdate is the QA switch back
            bool flag = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixCullUpdate") >= 0;
            Assert.AreEqual(flag ? AnimatorCullingMode.CullUpdateTransforms : AnimatorCullingMode.AlwaysAnimate, PlayerLod.MatchCulling);
        }

        [Test]
        public void TheKickoffLensWidensWithTheSpanAndStaysInRange()
        {
            float aspect = 16f / 9f;
            float f30 = MatchCameraRig.FovForSpan(30f, 47f, aspect), f60 = MatchCameraRig.FovForSpan(60f, 47f, aspect), f200 = MatchCameraRig.FovForSpan(200f, 47f, aspect);
            Assert.GreaterOrEqual(f30, 27f);
            Assert.Greater(f60, f30);
            Assert.Greater(f60, 36f, "the 57 m of a kickoff do not fit a 27 degree lens at 47 m");
            Assert.LessOrEqual(f200, 62f);
            // a narrower window needs a wider lens for the same span
            Assert.Greater(MatchCameraRig.FovForSpan(60f, 47f, 1f), f60);
        }

        [Test]
        public void FeetStayOnTheTurfOnlyForGroundedActions()
        {
            Assert.IsTrue(BodyCheck.FeetOnTurfAction(AgentAction.Stance));
            Assert.IsTrue(BodyCheck.FeetOnTurfAction(AgentAction.Run));
            Assert.IsFalse(BodyCheck.FeetOnTurfAction(AgentAction.Jump));
            Assert.IsFalse(BodyCheck.FeetOnTurfAction(AgentAction.TackleDive));
            Assert.IsFalse(BodyCheck.FeetOnTurfAction(AgentAction.Fallen));
        }
    }
}
