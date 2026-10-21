using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Per-player PlayableGraph: the locomotion AnimatorController (blend tree + ball-carrier move layer) sits at the bottom of a layer mixer and
    /// any ArtLibrary clip can be played over it (stances, blocks, catches, throws, tackles ...) with cross-fades. Time is driven manually so
    /// trimmed windows, speed and "hold the last pose" are exact.
    /// </summary>
    public sealed class ActionAnimator : MonoBehaviour
    {
        PlayableGraph _graph;
        AnimationLayerMixerPlayable _mixer;
        AnimatorControllerPlayable _loco;
        AnimationClipPlayable _clip;
        bool _hasClip, _created;

        AnimationClip _clipAsset;
        float _t, _start, _end, _speed = 1f, _fadeIn = 0.1f, _fadeOut = 0.15f, _w;
        bool _loop, _hold, _ending, _frozen;
        float _endingT, _endingFade;

        public AnimatorControllerPlayable Loco { get { return _loco; } }
        public bool Valid { get { return _created && _graph.IsValid(); } }
        public bool IsPlaying { get { return _hasClip && !_ending; } }
        public float Weight { get { return _w; } }
        public float Time01 { get { return _hasClip && _end > _start ? Mathf.Clamp01((_t - _start) / (_end - _start)) : 0f; } }
        public float ClipTime { get { return _t; } }
        /// <summary>Pauses the take where it is (the pose is held, the weight is kept).</summary>
        public void SetFrozen(bool on) { _frozen = on; }
        public string CurrentClipName { get { return _hasClip && _clipAsset != null ? _clipAsset.name : ""; } }

        Animator _animator; RuntimeAnimatorController _controller; bool _wantGraph;

        public bool Init(Animator animator, RuntimeAnimatorController controller)
        {
            if (animator == null || controller == null) return false;
            _animator = animator; _controller = controller; _wantGraph = true;
            return Build();
        }

        bool Build()
        {
            var animator = _animator; var controller = _controller;
            animator.runtimeAnimatorController = null;
            _hasClip = false; _ending = false; _w = 0f;
            _graph = PlayableGraph.Create("PlayerRig_" + gameObject.name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(_graph, "Anim", animator);
            _loco = AnimatorControllerPlayable.Create(_graph, controller);
            _mixer = AnimationLayerMixerPlayable.Create(_graph, 2);
            _graph.Connect(_loco, 0, _mixer, 0);
            _mixer.SetInputWeight(0, 1f);
            _mixer.SetInputWeight(1, 0f);
            output.SetSourcePlayable(_mixer);
            _graph.Play();
            _created = true;
            return true;
        }

        /// <summary>Plays a window [start, end] of the clip. end &lt;= 0 means the clip length.</summary>
        public void Play(AnimationClip clip, float start, float end, float speed, float fadeIn, float fadeOut, bool loop, bool hold)
        {
            if (!Valid || clip == null) return;
            float prevW = _hasClip && !_ending ? _w : 0f;
            DropClip();
            _w = prevW;
            _clipAsset = clip;
            _clip = AnimationClipPlayable.Create(_graph, clip);
            _clip.SetApplyFootIK(false);
            _clip.SetSpeed(0.0);
            _graph.Connect(_clip, 0, _mixer, 1);
            _hasClip = true; _ending = false; _frozen = false;
            _start = Mathf.Clamp(start, 0f, Mathf.Max(0f, clip.length - 0.02f));
            _end = end <= 0f || end > clip.length ? clip.length : Mathf.Max(end, _start + 0.05f);
            _t = _start; _speed = Mathf.Max(0.05f, speed);
            _fadeIn = Mathf.Max(0.0f, fadeIn); _fadeOut = Mathf.Max(0.02f, fadeOut);
            _loop = loop; _hold = hold;
            _clip.SetTime(_t);
            _w = _fadeIn <= 0.001f ? 1f : _w;
            _mixer.SetInputWeight(1, _w);
        }

        public void Stop(float fade = 0.15f)
        {
            if (!_hasClip || _ending) return;
            _ending = true; _endingT = 0f; _endingFade = Mathf.Max(0.02f, fade);
        }

        public void StopImmediate() { DropClip(); }

        void DropClip()
        {
            if (!_hasClip) return;
            _mixer.SetInputWeight(1, 0f);
            _graph.Disconnect(_mixer, 1);
            if (_clip.IsValid()) _graph.DestroyPlayable(_clip);
            _hasClip = false; _ending = false; _w = 0f; _clipAsset = null;
        }

        void Update()
        {
            if (_needRebuild && _animator != null && _animator.isActiveAndEnabled) { _needRebuild = false; if (!Valid) Build(); }
            if (!Valid || !_hasClip) return;
            float dt = UnityEngine.Time.deltaTime;
            if (dt <= 0f) return;
            if (!_ending)
            {
                if (!_frozen) _t += dt * _speed;
                if (_t >= _end)
                {
                    if (_loop) _t = _start + Mathf.Repeat(_t - _start, Mathf.Max(0.05f, _end - _start));
                    else if (_hold) _t = _end;
                    else { _t = _end; _ending = true; _endingT = 0f; _endingFade = _fadeOut; }
                }
                else if (!_loop && !_hold && _end - _t < _fadeOut) { _ending = true; _endingT = 0f; _endingFade = Mathf.Max(0.02f, _end - _t); }
                _w = _fadeIn <= 0.001f ? 1f : Mathf.MoveTowards(_w, 1f, dt / _fadeIn);
            }
            else
            {
                _endingT += dt;
                _t = Mathf.Min(_end, _t + dt * _speed);
                _w = Mathf.Clamp01(1f - _endingT / _endingFade);
                if (_w <= 0.001f) { DropClip(); return; }
            }
            _clip.SetTime(_t);
            _mixer.SetInputWeight(1, _w);
        }

        // a pooled player is deactivated between plays: Unity drops the animator binding, so the graph is rebuilt when he is switched back on
        bool _needRebuild;
        void OnEnable() { if (_wantGraph && !Valid) _needRebuild = true; }
        void OnDisable() { if (_created && _graph.IsValid()) _graph.Destroy(); _created = false; _hasClip = false; }
        void OnDestroy() { if (_created && _graph.IsValid()) _graph.Destroy(); _created = false; }
    }
}
