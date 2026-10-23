using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 QB (playtest note 7, kickoffs out of sync with the ball): the kick as ONE choreographed event. The simulation fixes the contact time before the approach starts, the view plays the approach on a clip timed to it, and here the kicking foot is closed
    /// onto the ball at that moment: a short IK pulse (the instep of the right foot comes to the ball, centred on the contact time) so the foot is at the ball on the frame the ball leaves, whatever the clip's own root path was.
    /// The ball itself is placed by the stage (tee, holder's spot, the punter's drop) and launched by the simulation on the contact frame.
    /// </summary>
    public partial class SimAgentView
    {
        bool _kickOn; Vector3 _kickBall, _kickDir; float _kickAt; bool _kickHaveAt; float _kickW;
        /// <summary>QA: weight of the kick IK pulse (0..1).</summary>
        public float KickIkWeight { get { return _kickW; } }
        /// <summary>QA: switch the kick contact IK off (the "before" state).</summary>
        public static bool DebugNoKickIk;
        /// <summary>How far (m) under the ball centre the instep path passes (the instep meets the ball low and behind its centre: the ball radius 0.105 m plus the foot's thickness less the lift of the swing).</summary>
        float _kickStandoff = 0.13f;

        /// <summary>Stage input every frame of a kicker's / punter's action: where the ball will be at contact, which way it goes and how many seconds until the simulated contact (negative / zero once it was struck).</summary>
        public void SetKickStandoff(float m) { _kickStandoff = m; }

        public void SetKickTarget(bool on, Vector3 ballAtContact, Vector3 dir, float etaSeconds)
        {
            _kickOn = on; _kickBall = ballAtContact; _kickDir = dir;
            if (on && etaSeconds > 0f) { _kickAt = _clock + etaSeconds; _kickHaveAt = true; }
            if (!on) _kickHaveAt = false;
        }

        Vector3 _kickPrevRaw, _kickCorr; bool _kickHavePrev; Vector3 _kickVel;

        void KickIkLate(float dt)
        {
            if (DebugNoKickIk || Avatar == null || Avatar.RightUpperLeg == null || Avatar.RightLowerLeg == null || Avatar.RightFoot == null) return;
            var toes = Avatar.Animator != null ? Avatar.Animator.GetBoneTransform(HumanBodyBones.RightToes) : null;
            if (toes == null) { _kickW = 0f; return; }
            Transform ankle = Avatar.RightFoot;
            Vector3 instep = Vector3.Lerp(ankle.position, toes.position, 0.55f);       // the animation's own instep this frame (before this correction)
            // the swing's velocity from the animation's frames (the bones are re-evaluated by the animator every frame, so this is always the raw path)
            if (_kickHavePrev && dt > 1e-4f) _kickVel = Vector3.Lerp(_kickVel, (instep - _kickPrevRaw) / dt, 0.7f);
            _kickPrevRaw = instep; _kickHavePrev = true;
            Vector3 dir = _kickDir; dir.y = 0f; dir = dir.sqrMagnitude > 1e-4f ? dir.normalized : Forward;
            // the instep meets the ball just behind and under its centre (the ball's radius is 0.105 m)
            Vector3 goal = _kickBall + Vector3.down * _kickStandoff;
            float want = 0f;
            if (_kickOn && _kickHaveAt && _kickVel.magnitude > 4f)
            {
                float t = _clock - _kickAt;       // negative before contact
                float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t + 0.20f) / 0.12f));
                float fall = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.02f) / 0.10f));
                want = rise * fall;
            }
            _kickW = want;
            // the correction vector is a smoothed one: the foot's path is bent through the ball at no more than 4 m/s of extra speed (a drag from far away whips the leg)
            Vector3 miss = goal - instep;
            Vector3 vh = _kickVel.sqrMagnitude > 1e-4f ? _kickVel.normalized : dir;
            Vector3 across = miss - vh * Vector3.Dot(miss, vh);
            if (miss.magnitude < 0.45f) across = miss;      // close to the ball the along-swing part is closed too (a frame of timing is 0.3 m at 18 m/s): at no more than 4 m/s of extra speed
            if (across.magnitude > 0.60f) across = across.normalized * 0.60f;
            _kickCorr = Vector3.MoveTowards(_kickCorr, across * want, 6.0f * Mathf.Max(dt, 0.0001f));
            if (_kickCorr.sqrMagnitude < 1e-6f) return;
            Vector3 ankleTarget = ankle.position + _kickCorr;
            Vector3 pole = Avatar.RightLowerLeg.position - Avatar.RightUpperLeg.position;
            LimbIK.Apply(Avatar.RightUpperLeg, Avatar.RightLowerLeg, ankle, ankleTarget, pole + dir * 0.2f, 1f);
        }
    }
}
