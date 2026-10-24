using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.11.0 ANIMLIB (B-012): helpers for the ground fit of the engaged pair takes.</summary>
    public partial class SimAgentView
    {
        Transform _alHips;

        /// <summary>True while the hips of the playing take are higher than <paramref name="m"/> above the view root: a man on his feet (a pancaked / lying blocker is left alone by the ground fit).</summary>
        bool HipsAbove(float m)
        {
            if (Avatar == null || Avatar.Animator == null || !Avatar.Animator.isHuman) return false;
            if (_alHips == null) _alHips = Avatar.Animator.GetBoneTransform(HumanBodyBones.Hips);
            return _alHips != null && _alHips.position.y - transform.position.y > m;
        }
    }
}
