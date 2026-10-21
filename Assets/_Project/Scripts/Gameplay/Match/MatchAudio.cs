using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>Stadium audio for a match: crowd bed that swells with the action, roars and boos on big plays, whistles, hits, catches and kicks.</summary>
    public sealed class MatchAudio : MonoBehaviour
    {
        AudioSource _bed, _roar, _sfx, _whistle, _crack;
        PixRuntimeAssets _rt;
        float _bedTarget = 0.4f, _roarLeft, _boo;

        public void Init()
        {
            _rt = PixRuntimeAssets.Load();
            _bed = Make(true); _roar = Make(false); _sfx = Make(false); _whistle = Make(false); _crack = Make(false);
            AudioClip bed = _rt != null && _rt.sfxBeds.Count > 0 ? _rt.sfxBeds[0] : (_rt != null ? _rt.crowdAmbience : null);
            _bed.clip = bed; _bed.volume = 0.4f * PixSettings.CrowdVolume;
            if (bed != null) _bed.Play();
        }

        AudioSource Make(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = loop; s.spatialBlend = 0f;
            return s;
        }

        static AudioClip Pick(List<AudioClip> l) { return l != null && l.Count > 0 ? l[Random.Range(0, l.Count)] : null; }

        public void Whistle()
        {
            var c = _rt != null ? (Pick(_rt.sfxWhistle) ?? _rt.whistle) : null;
            if (c != null) { _whistle.pitch = Random.Range(0.97f, 1.03f); _whistle.PlayOneShot(c, 0.55f); }
        }

        /// <summary>
        /// Hit sound by impact energy (0..1): a pad crack always, a body thud layered under it from a medium hit up, a second heavy thud and a lower pitch on the big ones.
        /// The pitch drops and the volume rises with the energy; the crack is scheduled a few milliseconds ahead of the thud like the real sound.
        /// </summary>
        public void Hit(float intensity)
        {
            intensity = Mathf.Clamp01(intensity);
            if (_rt == null) return;
            var crack = Pick(_rt.sfxHit) ?? Pick(_rt.sfxTackle);
            var thud = intensity > 0.3f ? Pick(_rt.sfxTackle) : null;
            if (crack != null)
            {
                _crack.clip = crack; _crack.pitch = Random.Range(0.96f, 1.08f) - 0.12f * intensity; _crack.volume = Mathf.Lerp(0.35f, 0.95f, intensity);
                _crack.Play();
            }
            if (thud != null)
            {
                _sfx.pitch = Random.Range(0.92f, 1.04f) - 0.16f * intensity;
                _sfx.PlayOneShot(thud, Mathf.Lerp(0.4f, 1f, intensity));
                if (intensity > 0.72f) { var t2 = Pick(_rt.sfxTackle); if (t2 != null && t2 != thud) _sfx.PlayOneShot(t2, 0.55f * intensity); }
            }
        }

        public void Catch() { var c = _rt != null ? Pick(_rt.sfxCatch) : null; if (c != null) { _sfx.pitch = Random.Range(0.95f, 1.05f); _sfx.PlayOneShot(c, 0.7f); } }
        public void Kick() { var c = _rt != null ? Pick(_rt.sfxKick) : null; if (c != null) { _sfx.pitch = Random.Range(0.95f, 1.0f); _sfx.PlayOneShot(c, 0.9f); } }
        public void BlockHit() { var c = _rt != null ? Pick(_rt.sfxHit) : null; if (c != null) { _sfx.pitch = Random.Range(0.9f, 1.1f); _sfx.PlayOneShot(c, 0.35f); } }

        /// <summary>Crowd roar; strength 0..1.</summary>
        public void Cheer(float strength)
        {
            var c = _rt != null ? (Pick(_rt.sfxCheers) ?? _rt.crowdCheer) : null;
            if (c == null) return;
            _roar.pitch = 1f; _roar.volume = Mathf.Clamp01(0.35f + 0.5f * strength) * PixSettings.CrowdVolume;
            _roar.clip = c; _roar.Play(); _roarLeft = Mathf.Min(c.length, 6f);
            _bedTarget = 0.6f;
        }

        /// <summary>Boos: the crowd bed dropped in pitch and pushed up for a few seconds.</summary>
        public void Boo()
        {
            _boo = 3.2f;
        }

        public void Quiet() { _bedTarget = 0.3f; }
        public void Stir(float level) { _bedTarget = Mathf.Max(_bedTarget, 0.3f + 0.3f * level); }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_roarLeft > 0f) { _roarLeft -= dt; if (_roarLeft < 1.2f) _roar.volume = Mathf.MoveTowards(_roar.volume, 0f, dt * 0.5f); }
            _bedTarget = Mathf.MoveTowards(_bedTarget, 0.38f, dt * 0.08f);
            float boo = _boo > 0f ? 1f : 0f; if (_boo > 0f) _boo -= dt;
            if (_bed != null)
            {
                _bed.pitch = Mathf.MoveTowards(_bed.pitch, boo > 0f ? 0.72f : 1f, dt * 0.6f);
                float target = (_bedTarget + (boo > 0f ? 0.3f : 0f)) * PixSettings.CrowdVolume;
                _bed.volume = Mathf.MoveTowards(_bed.volume, target, dt * 0.35f);
            }
        }

        /// <summary>Reacts to a simulation event (hits, catches, kicks, blocks).</summary>
        public void OnEvent(in PlayEvent e)
        {
            switch (e.Type)
            {
                case SimEventType.Tackle: case SimEventType.Sack: Hit(Mathf.Clamp01(e.Value / 8f + (e.Tackle == TackleType.BigHit ? 0.5f : 0.2f))); break;
                case SimEventType.Catch: case SimEventType.Interception: case SimEventType.Recovery: Catch(); break;
                case SimEventType.Kick: Kick(); break;
                case SimEventType.Pancake: BlockHit(); Stir(0.3f); break;
                case SimEventType.Throw: Stir(0.2f); break;
                case SimEventType.Whistle: Whistle(); break;
                case SimEventType.Fumble: Stir(0.8f); break;
            }
        }
    }
}
