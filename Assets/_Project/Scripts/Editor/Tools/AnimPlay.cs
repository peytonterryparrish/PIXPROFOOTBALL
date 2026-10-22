using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Editor replacement for <c>AnimationPlayableUtilities.PlayClip</c> that switches Foot IK OFF.
    /// The cleaned clips (Animations/Clean/*.anim) are muscle-space clips without the LeftFootT/Q / RightFootT/Q goal curves that the FBX importer bakes
    /// into model clips: with the default Foot IK of PlayClip the goals read as zero and the feet are pulled to the hips (legs folded, hip-to-foot / leg 0.08).
    /// The game (ActionAnimator, MatchOfficials, the controller states) always plays without Foot IK; every offline tool must do the same.
    /// </summary>
    public static class AnimPlay
    {
        public static AnimationClipPlayable PlayClip(Animator animator, AnimationClip clip, out PlayableGraph graph)
        {
            graph = PlayableGraph.Create("AnimPlay");
            var output = AnimationPlayableOutput.Create(graph, "AnimPlay", animator);
            var cp = AnimationClipPlayable.Create(graph, clip);
            cp.SetApplyFootIK(false);
            output.SetSourcePlayable(cp);
            graph.Play();
            return cp;
        }
    }
}
