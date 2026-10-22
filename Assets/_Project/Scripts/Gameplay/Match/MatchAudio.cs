using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Stadium audio for a match: crowd bed that swells with the action, roars and boos on big plays, whistles, hits, catches and kicks.
    /// One-shots (hits, catches, kicks, blocks, whistles) play on a small round-robin pool of sources so a second tackle never cuts the first and a new pitch never re-pitches a
    /// sound that is still playing; they follow the SFX slider, the crowd follows the Crowd slider, and everything pauses with the game (AudioListener.pause).
    /// </summary>
    public sealed class MatchAudio : MonoBehaviour
    {
        const int PoolSize = 8;
        AudioSource _bed, _roar;
        AudioSource[] _pool;
        int _poolNext;
        PixRuntimeAssets _rt;
        float _bedTarget = 0.4f, _roarLeft, _boo, _quietHold;

        public void Init()
        {
            _rt = PixRuntimeAssets.Load();
            _bed = Make(true); _roar = Make(false);
            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++) _pool[i] = Make(false);
            AudioClip bed = _rt != null && _rt.sfxBeds.Count > 0 ? _rt.sfxBeds[0] : (_rt != null ? _rt.crowdAmbience : null);
            _bed.clip = bed; _bed.volume = 0.4f * PixSettings.CrowdVolume;
            if (bed != null) _bed.Play();
            else if (_rt == null) Debug.LogWarning("[PIX] PixRuntimeAssets missing: the match has no crowd or effect sounds.");
        }

        AudioSource Make(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = loop; s.spatialBlend = 0f;
            return s;
        }

        static AudioClip Pick(List<AudioClip> l) { return l != null && l.Count > 0 ? l[Random.Range(0, l.Count)] : null; }

        /// <summary>Plays a clip on the next free source of the pool at the given pitch / volume (times the SFX slider).</summary>
        void Shot(AudioClip c, float volume, float pitch, float pan = 0f)
        {
            if (c == null || _pool == null) return;
            // prefer a source that is not playing; otherwise recycle the oldest one
            AudioSource s = null;
            for (int i = 0; i < PoolSize; i++)
            {
                var cand = _pool[(_poolNext + i) % PoolSize];
                if (!cand.isPlaying) { s = cand; _poolNext = (_poolNext + i + 1) % PoolSize; break; }
            }
            if (s == null) { s = _pool[_poolNext]; _poolNext = (_poolNext + 1) % PoolSize; }
            s.clip = c; s.pitch = pitch; s.volume = Mathf.Clamp01(volume * PixSettings.SfxVolume); s.panStereo = Mathf.Clamp(pan, -1f, 1f);
            s.Play();
        }

        public void Whistle()
        {
            var c = _rt != null ? (Pick(_rt.sfxWhistle) ?? _rt.whistle) : null;
            Shot(c, 0.55f, Random.Range(0.97f, 1.03f));
        }

        /// <summary>
        /// Hit sound by impact energy (0..1): a pad crack always, a body thud layered under it from a medium hit up, a second heavy thud and a lower pitch on the big ones.
        /// The pitch drops and the volume rises with the energy. <paramref name="pan"/> (-1 left .. 1 right) places the hit across the stereo field.
        /// </summary>
        public void Hit(float intensity, float pan = 0f)
        {
            intensity = Mathf.Clamp01(intensity);
            if (_rt == null) return;
            var crack = Pick(_rt.sfxHit) ?? Pick(_rt.sfxTackle);
            var thud = intensity > 0.3f ? Pick(_rt.sfxTackle) : null;
            Shot(crack, Mathf.Lerp(0.35f, 0.95f, intensity), Random.Range(0.96f, 1.08f) - 0.12f * intensity, pan);
            if (thud != null)
            {
                Shot(thud, Mathf.Lerp(0.4f, 1f, intensity), Random.Range(0.92f, 1.04f) - 0.16f * intensity, pan);
                if (intensity > 0.72f) { var t2 = Pick(_rt.sfxTackle); if (t2 != null && t2 != thud) Shot(t2, 0.55f * intensity, Random.Range(0.9f, 0.98f), pan); }
            }
        }

        public void Catch(float pan = 0f) { Shot(_rt != null ? Pick(_rt.sfxCatch) : null, 0.7f, Random.Range(0.95f, 1.05f), pan); }
        public void Kick() { Shot(_rt != null ? Pick(_rt.sfxKick) : null, 0.9f, Random.Range(0.95f, 1.0f)); }
        public void BlockHit(float pan = 0f) { Shot(_rt != null ? Pick(_rt.sfxHit) : null, 0.35f, Random.Range(0.9f, 1.1f), pan); }

        /// <summary>Crowd roar; strength 0..1.</summary>
        public void Cheer(float strength)
        {
            var c = _rt != null ? (Pick(_rt.sfxCheers) ?? _rt.crowdCheer) : null;
            if (c == null) return;
            _roar.pitch = 1f; _roar.volume = Mathf.Clamp01(0.35f + 0.5f * strength) * PixSettings.CrowdVolume;
            _roar.clip = c; _roar.Play(); _roarLeft = Mathf.Min(c.length, 6f);
            _bedTarget = 0.6f; _quietHold = 0f;
        }

        /// <summary>Boos: the crowd bed dropped in pitch and pushed up for a few seconds.</summary>
        public void Boo()
        {
            _boo = 3.2f;
        }

        /// <summary>The crowd hushes (turnover, injury): held for about five seconds, then it drifts back to the normal bed.</summary>
        public void Quiet() { _bedTarget = 0.3f; _quietHold = 5f; }
        public void Stir(float level) { _bedTarget = Mathf.Max(_bedTarget, 0.3f + 0.3f * level); }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_roarLeft > 0f) { _roarLeft -= dt; if (_roarLeft < 1.2f) _roar.volume = Mathf.MoveTowards(_roar.volume, 0f, dt * 0.5f); }
            if (_quietHold > 0f) _quietHold -= dt;
            else _bedTarget = Mathf.MoveTowards(_bedTarget, 0.38f, dt * 0.08f);
            float boo = _boo > 0f ? 1f : 0f; if (_boo > 0f) _boo -= dt;
            if (_bed != null)
            {
                _bed.pitch = Mathf.MoveTowards(_bed.pitch, boo > 0f ? 0.72f : 1f, dt * 0.6f);
                float target = (_bedTarget + (boo > 0f ? 0.3f : 0f)) * PixSettings.CrowdVolume;
                _bed.volume = Mathf.MoveTowards(_bed.volume, target, dt * 0.35f);
            }
        }

        /// <summary>Stereo pan of a sim position relative to the screen centre line, using the main camera (right of the screen = +1).</summary>
        static float PanOf(in PlayEvent e, PlayFrame frame)
        {
            var cam = Camera.main;
            if (cam == null) return 0f;
            Vector3 vp = cam.WorldToViewportPoint(frame.ToUnity(e.Pos));
            if (vp.z <= 0f) return 0f;
            return Mathf.Clamp((vp.x - 0.5f) * 1.4f, -0.8f, 0.8f);
        }

        /// <summary>Reacts to a simulation event (hits, catches, kicks, blocks).</summary>
        public void OnEvent(in PlayEvent e, PlayFrame frame)
        {
            switch (e.Type)
            {
                case SimEventType.Tackle: case SimEventType.Sack: Hit(Mathf.Clamp01(e.Value / 8f + (e.Tackle == TackleType.BigHit ? 0.5f : 0.2f)), PanOf(e, frame)); break;
                case SimEventType.Catch: case SimEventType.Interception: case SimEventType.Recovery: Catch(PanOf(e, frame)); break;
                case SimEventType.Kick: Kick(); break;
                case SimEventType.Pancake: BlockHit(PanOf(e, frame)); Stir(0.3f); break;
                case SimEventType.Throw: Stir(0.2f); break;
                case SimEventType.Whistle: Whistle(); break;
                case SimEventType.Fumble: Stir(0.8f); break;
            }
        }
    }
}
