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
            public bool Active, Loop, Hold, Ending, Frozen, Scrub;
            public float T, Start, End, Speed = 1f, FadeIn = 0.1f, FadeOut = 0.15f, W, EndingT, EndingFade, EndW;
            public int Input = 1; public AvatarMask Mask;
            // v0.9.0 CONNECT: facing offset of the clip (degrees), plan asked for an upper-body mask, mask currently upper-body, fade-in floor used
            public float Yaw; public bool UpperPlan, MaskUpper;
            // v0.10.0 CHOREO: speed (m/s) above which this plan hands the legs to locomotion (0 = the default <see cref="UpperBodySpeed"/>)
            public float MaskSpeed;
            // v0.10.0 CHOREO: seconds since the mask last changed (a mask flip is a pose pop: a slot keeps its mask for at least 0.4 s)
            public float MaskT;
            public float Eff { get { return Smooth(W); } }
        }

        /// <summary>Smoothstep ease used for every fade (unit tested).</summary>
        public static float Smooth(float w) { w = Mathf.Clamp01(w); return w * w * (3f - 2f * w); }

        /// <summary>Speed (m/s) above which an upper-body plan keeps the legs of the locomotion layer.</summary>
        public const float UpperBodySpeed = 2.0f;

        PlayableGraph _graph;
        AnimationLayerMixerPlayable _mixer;
        AnimatorControllerPlayable _loco;
        AnimationLayerMixerPlayable _locoMix;
        /// <summary>v0.9.0 LOCOMOTION: the gait synthesizer layered over the animator controller (null when the cycle library is missing).</summary>
        public GaitPlayer Gait { get; private set; }
        /// <summary>v0.10.0 MOTION: the locomotion motion matcher (replaces the gait player unless -pixLocoLegacy; null without the database).</summary>
        public MotionMatcher Mm { get; private set; }
        readonly Slot[] _s = { new Slot(), new Slot() };
        int _cur = -1;                // index of the newest slot
        bool _created;
        LocomotionAnimator _locoDriver;
        static AvatarMask _upperMask, _fullMask;

        public AnimatorControllerPlayable Loco { get { return _loco; } }
        /// <summary>QA: build the player without the gait synthesizer (the "before" state).</summary>
        public static bool DisableGait;
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
        /// <summary>v0.10.0 MOTION: the clip layer is fading out while the motion matcher plays the legs: the outgoing clips give the legs to the matcher at once (it inertializes the change), so the feet are not
        /// dragged between two leg poses for the length of the fade. Returns true when a mask was changed this call.</summary>
        public bool LegsToLoco()
        {
            bool any = false;
            for (int i = 0; i < _s.Length; i++)
            {
                var s = _s[i];
                if (s == null || !s.Active || !s.Ending || s.MaskUpper) continue;
                s.MaskUpper = true; s.Mask = UpperMask(); _mixer.SetLayerMaskFromAvatarMask((uint)s.Input, s.Mask); any = true;
            }
            return any;
        }
        public float Time01 { get { var s = Cur; return s != null && s.End > s.Start ? Mathf.Clamp01((s.T - s.Start) / (s.End - s.Start)) : 0f; } }
        public float ClipTime { get { var s = Cur; return s != null ? s.T : 0f; } }
        /// <summary>Pauses the take where it is (the pose is held, the weight is kept).</summary>
        public void SetFrozen(bool on) { foreach (var s in _s) s.Frozen = on && s.Active; }
        public string CurrentClipName { get { var s = Cur; return s != null && s.Clip != null ? s.Clip.name : ""; } }
        /// <summary>The newest clip (null when none plays).</summary>
        public AnimationClip CurrentClip { get { var s = Cur; return s != null ? s.Clip : null; } }
        /// <summary>True while the newest clip is still inside its window (not fading out, not held at the end).</summary>
        public bool InWindow { get { var s = Cur; return s != null && !s.Ending && (s.Hold ? true : s.T < s.End - 0.001f); } }
        /// <summary>Seconds of the newest clip's window that are left at its playback speed (0 for a looping / held / scrubbed clip).</summary>
        public float RemainingSeconds { get { var s = Cur; return s == null || s.Ending || s.Loop || s.Hold || s.Scrub ? 0f : Mathf.Max(0f, (s.End - s.T) / Mathf.Max(0.05f, s.Speed)); } }
        /// <summary>v0.7.0: the clip time is set by the caller (distance-matched drop-backs: the animation follows the simulated position instead of the clock). The clip never ends by itself while scrubbed.</summary>
        public void SetScrub(bool on) { if (_hasPend) { _pend.Scrub = on; return; } var s = Cur; if (s != null) s.Scrub = on; }
        public void Scrub(float clipTime) { if (_hasPend) { _pend.Scrub = true; _pend.ScrubT = clipTime; return; } var s = Cur; if (s != null) { s.Scrub = true; s.T = Mathf.Clamp(clipTime, s.Start, s.End); } }
        /// <summary>Changes the playback speed of the newest clip (stride matching for clips that travel).</summary>
        public void SetSpeed(float speed) { var s = Cur; if (s != null) s.Speed = Mathf.Max(0.05f, speed); }
        /// <summary>Clip time of the newest clip in seconds of the source clip.</summary>
        public float CurrentTime { get { var s = Cur; return s != null ? s.T : 0f; } }

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
            _locoMix = AnimationLayerMixerPlayable.Create(_graph, 3);
            _graph.Connect(_loco, 0, _locoMix, 0);
            _locoMix.SetInputWeight(0, 1f);
            _graph.Connect(_locoMix, 0, _mixer, 0);
            if (Mm == null && !DisableGait) Mm = MotionMatcher.TryCreate();      // v0.10.0 MOTION
            if (Mm == null && Gait == null) { var lib = LocoCycleLibrary.Load(); if (lib != null && lib.cycles.Count > 0 && !DisableGait) Gait = new GaitPlayer(lib, gameObject.GetHashCode()); }
            if (Mm != null) Mm.Attach(_graph, _locoMix, 1); else if (Gait != null) Gait.Attach(_graph, _locoMix, 1);
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

        public void Play(in ClipPlan p) { NextMaskSpeed = p.MaskSpeed; Play(p.Clip, p.Start, p.End, p.Speed, p.FadeIn, p.FadeOut, p.Loop, p.Hold, p.UpperBody, p.Phase, p.FadeFloor); NextMaskSpeed = 0f; }

        // v0.9.0 CONNECT E-266: a third request while the hidden slot is still visibly fading out is deferred (the older clip finishes a short fade first) instead of recycling the slot with a pop
        struct PendingPlay { public AnimationClip Clip; public float Start, End, Speed, FadeIn, FadeOut, Phase, Floor, ScrubT, MaskSpeed; public bool Loop, Hold, Upper, Scrub; }
        PendingPlay _pend; bool _hasPend;
        /// <summary>v0.10.0 CHOREO: mask speed of the next Play call (set by <see cref="Play(in ClipPlan)"/>, consumed by the slot it creates).</summary>
        public float NextMaskSpeed;
        /// <summary>Counts every clip start and stop (the pop filter and the audit read it: a new value means the pose source changed this frame).</summary>
        public int Transitions { get; private set; }
        /// <summary>Speed (m/s) used to decide whether an upper-body plan keeps the legs of the locomotion layer: set by the view (the animator's smoothed speed otherwise).</summary>
        public float MaskSpeedOverride = -1f;

        /// <summary>Plays a window [start, end] of the clip. end &lt;= 0 means the clip length. A clip that is already playing is cross-faded out underneath the new one.
        /// phase = initial offset into the window (a looping stance staggered between players) - the loop point stays at the start of the window.
        /// fadeFloor &gt; 0 replaces <see cref="MinFade"/> for this clip (impact frames: a tackle or catch must be fully in at the contact frame).</summary>
        public void Play(AnimationClip clip, float start, float end, float speed, float fadeIn, float fadeOut, bool loop, bool hold, bool upperBody = false, float phase = 0f, float fadeFloor = -1f)
        {
            if (!Valid || clip == null) return;
            float floor = fadeFloor > 0f ? Mathf.Min(MinFade, fadeFloor) : MinFade;
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
                if (n.Active && n.Eff > 0.12f)
                {
                    // defer: finish the hidden clip's fade quickly, start the new one when the slot is free
                    _pend = new PendingPlay { Clip = clip, Start = start, End = end, Speed = speed, FadeIn = fadeIn, FadeOut = fadeOut, Phase = phase, Floor = floor, Loop = loop, Hold = hold, Upper = upperBody, MaskSpeed = NextMaskSpeed }; NextMaskSpeed = 0f;
                    _hasPend = true;
                    if (!n.Ending) { n.Ending = true; n.EndingT = 0f; n.EndingFade = 0.1f; n.EndW = n.W; }
                    else if (n.EndingFade - n.EndingT > 0.1f) n.EndingFade = n.EndingT + 0.1f;
                    return;
                }
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
            n.Active = true; n.Ending = false; n.Frozen = false; n.Scrub = false;
            n.Start = Mathf.Clamp(start, 0f, Mathf.Max(0f, clip.length - 0.02f));
            n.End = end <= 0f || end > clip.length ? clip.length : Mathf.Max(end, n.Start + 0.05f);
            n.T = loop && phase > 0f ? n.Start + Mathf.Repeat(phase, Mathf.Max(0.05f, n.End - n.Start)) : n.Start;
            n.Speed = Mathf.Max(0.05f, speed);
            n.FadeIn = fadeIn > 0.001f ? Mathf.Max(floor, fadeIn) : 0f; n.FadeOut = Mathf.Max(floor, fadeOut);
            n.Loop = loop; n.Hold = hold;
            // a clip replacing a clip always gets a real cross-fade, however snappy the caller asked
            if (crossing && n.FadeIn < floor) n.FadeIn = floor;      // 0.22 s smoothstep = at most 0.2 weight per frame at 30 Hz (impact takes pass a lower floor)
            n.W = n.FadeIn <= 0.001f && !crossing ? 1f : 0f;
            n.Cp.SetTime(n.T);
            _cur = n == _s[0] ? 0 : 1;
            // upper-body plans keep the legs of the locomotion layer while the player is moving (re-decided every frame in Update: E-260)
            n.UpperPlan = upperBody;
            n.MaskSpeed = NextMaskSpeed; NextMaskSpeed = 0f; n.MaskT = 1f;      // v0.10.0 CHOREO
            bool mask = upperBody && MoveSpeed() > (n.MaskSpeed > 0f ? n.MaskSpeed : UpperBodySpeed);
            var m = mask ? UpperMask() : FullMask();
            _mixer.SetLayerMaskFromAvatarMask((uint)n.Input, m);
            n.Mask = m; n.MaskUpper = mask;
            n.Yaw = ClipFacingOffset(clip);
            Transitions++;
            _mixer.SetInputWeight(n.Input, n.Eff);
        }

        public void Stop(float fade = 0.15f, float fadeFloor = -1f)
        {
            _hasPend = false;
            float floor = fadeFloor > 0f ? Mathf.Min(MinFade, fadeFloor) : MinFade;
            bool any = false;
            foreach (var s in _s)
            {
                if (!s.Active || s.Ending) continue;
                s.Ending = true; s.EndingT = 0f; s.EndingFade = Mathf.Max(floor, fade); s.EndW = s.W; any = true;
            }
            if (any) Transitions++;
        }

        public void StopImmediate() { _hasPend = false; DropSlot(_s[0]); DropSlot(_s[1]); _cur = -1; Transitions++; }

        /// <summary>Speed the view feeds for the upper-body mask decision (m/s); falls back to the locomotion animator's smoothed speed.</summary>
        float MoveSpeed() { return MaskSpeedOverride >= 0f ? MaskSpeedOverride : (_locoDriver != null ? _locoDriver.SmoothedLocalVelocity.magnitude : 0f); }

        static float ClipFacingOffset(AnimationClip c) { return PixFootball.Art.ClipFacingTable.Offset(c); }

        /// <summary>v0.9.0 CONNECT E-264: the facing correction (degrees) of the clips that are playing, blended with the SAME weights as the poses (an outgoing clip's offset leaves while the incoming one's arrives), already multiplied by the clip layer weight.</summary>
        public float FacingOffset
        {
            get
            {
                float off = 0f;
                for (int k = 0; k < 2; k++)
                {
                    // older slot first, newest on top (the layer mixer order)
                    int i = _cur < 0 ? k : (k == 0 ? 1 - _cur : _cur);
                    var s = _s[i];
                    if (!s.Active) continue;
                    off = Mathf.LerpAngle(off, s.Yaw, s.Eff);
                }
                return off;
            }
        }
        /// <summary>Weight of the newest clip alone (0..1, smoothstepped).</summary>
        public float NewestWeight { get { var s = Cur; return s != null ? s.Eff : 0f; } }
        /// <summary>v0.10.0 CHOREO: the newest clip is masked to the upper body right now (the legs belong to locomotion).</summary>
        public bool UpperBodyActive { get { var s = Cur; return s != null && s.MaskUpper; } }
        /// <summary>v0.10.0 CHOREO: playback speed of the newest clip.</summary>
        public float PlaySpeed { get { var s = Cur; return s != null ? s.Speed : 1f; } }

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
                    if (!s.Frozen && !s.Scrub) s.T += dt * s.Speed;
                    if (s.Scrub) { /* the caller sets the time */ }
                    else if (s.T >= s.End)
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
                // v0.9.0 CONNECT E-260: an upper-body plan re-decides every frame whether the legs belong to the locomotion layer (hysteresis 2.0 / 1.2 m/s)
                if (s.UpperPlan)
                {
                    float sp = MoveSpeed();
                    float hiSp = s.MaskSpeed > 0f ? s.MaskSpeed : UpperBodySpeed, loSp = s.MaskSpeed > 0f ? s.MaskSpeed * 0.6f : 1.2f;
                    s.MaskT += dt;
                    bool wantUpper = s.MaskUpper ? sp > loSp : sp > hiSp;
                    if (wantUpper != s.MaskUpper && s.MaskT >= 0.4f) { s.MaskT = 0f; s.MaskUpper = wantUpper; s.Mask = wantUpper ? UpperMask() : FullMask(); _mixer.SetLayerMaskFromAvatarMask((uint)s.Input, s.Mask); }
                }
                float mw = s.Eff;
                // v0.9.0 CONNECT E-262: an outgoing full-body clip under an upper-body-masked newcomer gives the legs back to the locomotion layer over the same fade (no pop when it is dropped)
                if (!newest && _cur >= 0 && _s[_cur].Active && _s[_cur].MaskUpper && !s.MaskUpper) mw *= 1f - _s[_cur].Eff;
                _mixer.SetInputWeight(s.Input, mw);
            }
            // a deferred clip (E-266) starts as soon as the hidden slot is free
            if (_hasPend)
            {
                var hid = _cur >= 0 ? _s[1 - _cur] : null;
                if (hid == null || !hid.Active || hid.Eff <= 0.12f)
                {
                    var p = _pend; _hasPend = false;
                    NextMaskSpeed = p.MaskSpeed;
                    Play(p.Clip, p.Start, p.End, p.Speed, p.FadeIn, p.FadeOut, p.Loop, p.Hold, p.Upper, p.Phase, p.Floor);
                    NextMaskSpeed = 0f;
                    if (p.Scrub) { var ns = Cur; if (ns != null) { ns.Scrub = true; ns.T = Mathf.Clamp(p.ScrubT, ns.Start, ns.End); } }
                }
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
        void OnDisable() { if (Mm != null) Mm.Detach(); if (Gait != null) Gait.Detach(); if (_created && _graph.IsValid()) _graph.Destroy(); _created = false; foreach (var s in _s) { s.Active = false; s.W = 0f; } _cur = -1; }
        void OnDestroy() { if (_created && _graph.IsValid()) _graph.Destroy(); _created = false; }
    }
}
