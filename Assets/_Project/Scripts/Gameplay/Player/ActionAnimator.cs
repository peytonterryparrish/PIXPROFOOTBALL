using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Per-player PlayableGraph: the locomotion AnimatorController (blend tree + ball-carrier move layer) sits at the bottom of a layer mixer and
    /// any ArtLibrary clip can be played over it (stances, blocks, catches, throws, tackles ...). Time is driven manually so trimmed windows, speed and
    /// "hold the last pose" are exact.
    /// v0.8 (smoothness): two clip slots so a new clip cross-fades FROM the old one (the layer mixer draws higher inputs on top, so the newest clip is always
    /// moved to the upper input and the outgoing one to the lower input), smoothstep fade curves, a loop phase that does not move the loop point, and an
    /// optional upper-body mask so a throw / catch / hand-off made on the move keeps the legs of the locomotion layer instead of freezing them.
    /// </summary>
    public sealed class ActionAnimator : MonoBehaviour
    {
        /// <summary>No cross-fade is shorter than this (2K8 smoothness: a smoothstep over 0.22 s moves the weight by at most 0.2 per frame at 30 Hz).</summary>
        public const float MinFade = 0.22f;
        sealed class Slot
        {
            public AnimationClipPlayable Cp;
            public AnimationClip Clip;
            public bool Active, Loop, Hold, Ending, Frozen;
            public float T, Start, End, Speed = 1f, FadeIn = 0.1f, FadeOut = 0.15f, W, EndingT, EndingFade, EndW;
            public int Input = 1; public AvatarMask Mask;
            public float Eff { get { return Smooth(W); } }
        }

        /// <summary>Smoothstep ease used for every fade (unit tested).</summary>
        public static float Smooth(float w) { w = Mathf.Clamp01(w); return w * w * (3f - 2f * w); }

        /// <summary>Speed (m/s) above which an upper-body plan keeps the legs of the locomotion layer.</summary>
        public const float UpperBodySpeed = 2.0f;

        PlayableGraph _graph;
        AnimationLayerMixerPlayable _mixer;
        AnimatorControllerPlayable _loco;
        readonly Slot[] _s = { new Slot(), new Slot() };
        int _cur = -1;                // index of the newest slot
        bool _created;
        LocomotionAnimator _locoDriver;
        static AvatarMask _upperMask, _fullMask;

        public AnimatorControllerPlayable Loco { get { return _loco; } }
        public bool Valid { get { return _created && _graph.IsValid(); } }
        public bool IsPlaying { get { return _cur >= 0 && _s[_cur].Active && !_s[_cur].Ending; } }
        /// <summary>Combined weight of the clip layer (0 = pure locomotion).</summary>
        public float Weight
        {
            get
            {
                float a = _s[0].Active ? _s[0].Eff : 0f, b = _s[1].Active ? _s[1].Eff : 0f;
                return 1f - (1f - a) * (1f - b);
            }
        }
        public float Time01 { get { var s = Cur; return s != null && s.End > s.Start ? Mathf.Clamp01((s.T - s.Start) / (s.End - s.Start)) : 0f; } }
        public float ClipTime { get { var s = Cur; return s != null ? s.T : 0f; } }
        /// <summary>Pauses the take where it is (the pose is held, the weight is kept).</summary>
        public void SetFrozen(bool on) { foreach (var s in _s) s.Frozen = on && s.Active; }
        public string CurrentClipName { get { var s = Cur; return s != null && s.Clip != null ? s.Clip.name : ""; } }

        Slot Cur { get { return _cur >= 0 && _s[_cur].Active ? _s[_cur] : null; } }

        Animator _animator; RuntimeAnimatorController _controller; bool _wantGraph;

        public bool Init(Animator animator, RuntimeAnimatorController controller)
        {
            if (animator == null || controller == null) return false;
            _animator = animator; _controller = controller; _wantGraph = true;
            _locoDriver = GetComponent<LocomotionAnimator>();
            return Build();
        }

        bool Build()
        {
            var animator = _animator; var controller = _controller;
            animator.runtimeAnimatorController = null;
            foreach (var s in _s) { s.Active = false; s.W = 0f; s.Ending = false; }
            _cur = -1;
            _graph = PlayableGraph.Create("PlayerRig_" + gameObject.name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "Anim", animator);
            _loco = AnimatorControllerPlayable.Create(_graph, controller);
            _mixer = AnimationLayerMixerPlayable.Create(_graph, 3);
            _graph.Connect(_loco, 0, _mixer, 0);
            _mixer.SetInputWeight(0, 1f);
            _mixer.SetInputWeight(1, 0f);
            _mixer.SetInputWeight(2, 0f);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
            _created = true;
            return true;
        }

        static AvatarMask UpperMask()
        {
            if (_upperMask != null) return _upperMask;
            _upperMask = new AvatarMask { name = "PIX_UpperBody" };
            foreach (AvatarMaskBodyPart p in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
            {
                if (p == AvatarMaskBodyPart.LastBodyPart) continue;
                bool on = p == AvatarMaskBodyPart.Body || p == AvatarMaskBodyPart.Head || p == AvatarMaskBodyPart.LeftArm || p == AvatarMaskBodyPart.RightArm
                          || p == AvatarMaskBodyPart.LeftFingers || p == AvatarMaskBodyPart.RightFingers;
                _upperMask.SetHumanoidBodyPartActive(p, on);
            }
            return _upperMask;
        }

        static AvatarMask FullMask()
        {
            if (_fullMask != null) return _fullMask;
            _fullMask = new AvatarMask { name = "PIX_FullBody" };
            foreach (AvatarMaskBodyPart p in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
                if (p != AvatarMaskBodyPart.LastBodyPart) _fullMask.SetHumanoidBodyPartActive(p, true);
            return _fullMask;
        }

        public void Play(in ClipPlan p) { Play(p.Clip, p.Start, p.End, p.Speed, p.FadeIn, p.FadeOut, p.Loop, p.Hold, p.UpperBody, p.Phase); }

        /// <summary>Plays a window [start, end] of the clip. end &lt;= 0 means the clip length. A clip that is already playing is cross-faded out underneath the new one.
        /// phase = initial offset into the window (a looping stance staggered between players) - the loop point stays at the start of the window.</summary>
        public void Play(AnimationClip clip, float start, float end, float speed, float fadeIn, float fadeOut, bool loop, bool hold, bool upperBody = false, float phase = 0f)
        {
            if (!Valid || clip == null) return;
            var old = Cur;
            Slot n;
            if (old == null)
            {
                n = _s[0].Active ? _s[1] : _s[0];
                if (n.Active) DropSlot(n);
                n.Input = 1;
            }
            else
            {
                int ni = 1 - _cur;
                n = _s[ni];
                if (n.Active) DropSlot(n);          // an older, hidden slot is recycled
                // the layer mixer draws higher inputs on top: the outgoing clip must sit on the lower input, the new one on the upper
                if (old.Input != 1)
                {
                    _mixer.SetInputWeight(old.Input, 0f);
                    _graph.Disconnect(_mixer, old.Input);
                    _graph.Connect(old.Cp, 0, _mixer, 1);
                    old.Input = 1;
                    _mixer.SetInputWeight(1, old.Eff);
                    _mixer.SetLayerMaskFromAvatarMask(1u, old.Mask != null ? old.Mask : FullMask());
                }
                n.Input = 2;
            }
            bool crossing = old != null && !old.Ending;
            n.Clip = clip;
            n.Cp = AnimationClipPlayable.Create(_graph, clip);
            n.Cp.SetApplyFootIK(false);
            n.Cp.SetSpeed(0.0);
            _graph.Connect(n.Cp, 0, _mixer, n.Input);
            n.Active = true; n.Ending = false; n.Frozen = false;
            n.Start = Mathf.Clamp(start, 0f, Mathf.Max(0f, clip.length - 0.02f));
            n.End = end <= 0f || end > clip.length ? clip.length : Mathf.Max(end, n.Start + 0.05f);
            n.T = loop && phase > 0f ? n.Start + Mathf.Repeat(phase, Mathf.Max(0.05f, n.End - n.Start)) : n.Start;
            n.Speed = Mathf.Max(0.05f, speed);
            n.FadeIn = fadeIn > 0.001f ? Mathf.Max(MinFade, fadeIn) : 0f; n.FadeOut = Mathf.Max(MinFade, fadeOut);
            n.Loop = loop; n.Hold = hold;
            // a clip replacing a clip always gets a real cross-fade, however snappy the caller asked
            if (crossing && n.FadeIn < MinFade) n.FadeIn = MinFade;      // 0.22 s smoothstep = at most 0.2 weight per frame at 30 Hz
            n.W = n.FadeIn <= 0.001f && !crossing ? 1f : 0f;
            n.Cp.SetTime(n.T);
            _cur = n == _s[0] ? 0 : 1;
            // upper-body plans keep the legs of the locomotion layer while the player is moving
            bool mask = upperBody && _locoDriver != null && _locoDriver.SmoothedLocalVelocity.magnitude > UpperBodySpeed;
            var m = mask ? UpperMask() : FullMask();
            _mixer.SetLayerMaskFromAvatarMask((uint)n.Input, m);
            n.Mask = m;
            _mixer.SetInputWeight(n.Input, n.Eff);
        }

        public void Stop(float fade = 0.15f)
        {
            foreach (var s in _s)
            {
                if (!s.Active || s.Ending) continue;
                s.Ending = true; s.EndingT = 0f; s.EndingFade = Mathf.Max(MinFade, fade); s.EndW = s.W;
            }
        }

        public void StopImmediate() { DropSlot(_s[0]); DropSlot(_s[1]); _cur = -1; }

        void DropSlot(Slot s)
        {
            if (!s.Active) return;
            _mixer.SetInputWeight(s.Input, 0f);
            _graph.Disconnect(_mixer, s.Input);
            if (s.Cp.IsValid()) _graph.DestroyPlayable(s.Cp);
            s.Active = false; s.Ending = false; s.W = 0f; s.Clip = null;
        }

        /// <summary>Rebuilds the graph of a pooled player that was just re-activated (called by the view before its first pose, so no frame is drawn in the bind pose; QA-677).</summary>
        public void EnsureBuilt()
        {
            if (_needRebuild && _animator != null && _animator.isActiveAndEnabled) { _needRebuild = false; if (!Valid) Build(); }
        }

        void Update()
        {
            EnsureBuilt();
            if (!Valid) return;
            float dt = UnityEngine.Time.deltaTime;
            if (dt <= 0f) return;
            bool any = false;
            for (int i = 0; i < 2; i++)
            {
                var s = _s[i];
                if (!s.Active) continue;
                any = true;
                bool newest = i == _cur;
                if (!s.Ending)
                {
                    if (!s.Frozen) s.T += dt * s.Speed;
                    if (s.T >= s.End)
                    {
                        if (s.Loop) s.T = s.Start + Mathf.Repeat(s.T - s.Start, Mathf.Max(0.05f, s.End - s.Start));
                        else if (s.Hold) s.T = s.End;
                        else { s.T = s.End; s.Ending = true; s.EndingT = 0f; s.EndingFade = s.FadeOut; s.EndW = s.W; }
                    }
                    else if (!s.Loop && !s.Hold && s.End - s.T < s.FadeOut) { s.Ending = true; s.EndingT = 0f; s.EndingFade = Mathf.Max(0.02f, s.End - s.T); s.EndW = s.W; }
                    // the newest slot fades in; an outgoing slot keeps the weight it had (no jump), the new clip covers it
                    if (!s.Ending && newest) s.W = s.FadeIn <= 0.001f ? 1f : Mathf.MoveTowards(s.W, 1f, dt / s.FadeIn);
                }
                else
                {
                    s.EndingT += dt;
                    s.T = Mathf.Min(s.End, s.T + dt * s.Speed);
                    s.W = Mathf.Clamp01(s.EndW * (1f - s.EndingT / s.EndingFade));
                    if (s.W <= 0.001f) { DropSlot(s); if (_cur == i) _cur = _s[1 - i].Active ? 1 - i : -1; continue; }
                }
                s.Cp.SetTime(s.T);
                _mixer.SetInputWeight(s.Input, s.Eff);
            }
            // the old slot is released once the newest one is fully in
            if (_cur >= 0 && _s[_cur].Active && !_s[_cur].Ending && _s[_cur].W >= 0.999f)
            {
                var o = _s[1 - _cur];
                if (o.Active) DropSlot(o);
            }
            if (!any) _cur = -1;
        }

        // a pooled player is deactivated between plays: Unity drops the animator binding, so the graph is rebuilt when he is switched back on
        bool _needRebuild;
        void OnEnable() { if (_wantGraph && !Valid) _needRebuild = true; }
        void OnDisable() { if (_created && _graph.IsValid()) _graph.Destroy(); _created = false; foreach (var s in _s) { s.Active = false; s.W = 0f; } _cur = -1; }
        void OnDestroy() { if (_created && _graph.IsValid()) _graph.Destroy(); _created = false; }
    }
}
