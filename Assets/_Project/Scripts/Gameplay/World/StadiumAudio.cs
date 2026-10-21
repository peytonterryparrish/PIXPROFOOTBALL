using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>Stadium crowd bed plus reactive cheers.</summary>
    public class StadiumAudio : MonoBehaviour
    {
        AudioSource _bed, _fx;
        PlayerController _player;
        PlayerMove _lastMove;
        float _cheerLeft, _cheerCooldown;

        public void Init(PlayerController p)
        {
            _player = p;
            var rt = PixRuntimeAssets.Load();
            var lib = ArtLibrary.Load();
            AudioClip bed = rt != null && rt.crowdAmbience != null ? rt.crowdAmbience : (lib != null ? lib.Audio("crowd_ambience", 0) : null);
            AudioClip cheer = rt != null && rt.crowdCheer != null ? rt.crowdCheer : (lib != null ? lib.Audio("crowd_cheer", 0) : null);
            _bed = gameObject.AddComponent<AudioSource>();
            _bed.clip = bed; _bed.loop = true; _bed.spatialBlend = 0f; _bed.playOnAwake = false; _bed.volume = 0.5f * PixSettings.CrowdVolume;
            if (bed != null) _bed.Play();
            _fx = gameObject.AddComponent<AudioSource>();
            _fx.clip = cheer; _fx.loop = false; _fx.spatialBlend = 0f; _fx.playOnAwake = false;
            _cheerClip = cheer;
        }

        AudioClip _cheerClip;

        public void OnPlayerMove(PlayerMove m)
        {
            if (m != _lastMove && m != PlayerMove.None && _cheerClip != null && _cheerCooldown <= 0f && m != PlayerMove.StiffArmLeft && m != PlayerMove.StiffArmRight)
            {
                _fx.volume = 0.55f * PixSettings.CrowdVolume;
                _fx.Play(); _cheerCooldown = 6f;
            }
            _lastMove = m;
        }

        void Update()
        {
            _cheerCooldown -= Time.unscaledDeltaTime;
            if (_bed != null)
            {
                float speed = _player != null ? _player.SpeedMph : 0f;
                float target = (0.42f + 0.18f * Mathf.Clamp01(speed / 20f)) * PixSettings.CrowdVolume;
                _bed.volume = Mathf.MoveTowards(_bed.volume, target, Time.unscaledDeltaTime * 0.2f);
            }
        }
    }
}
