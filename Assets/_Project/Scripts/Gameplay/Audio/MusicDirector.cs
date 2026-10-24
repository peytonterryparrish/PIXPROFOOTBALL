using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 AUDIO+CROWD: all music of the game. Instrumental cinematic cues made from the Draft Fever OST stems (the owner's sung songs are no longer used anywhere).
    /// Two crossfading loop voices plus a stinger voice; menu playlist (calm / drive / house), pre-game cue then bed, halftime, quiet between-play underscore, tense two-minute bed,
    /// event stingers; a ducking envelope (UI sounds, roars, hits push the music down and it recovers by itself). Volume = Music slider.
    /// </summary>
    [ExecuteAlways]
    public sealed class MusicDirector : MonoBehaviour
    {
        public static MusicDirector Instance { get; private set; }

        public static MusicDirector Ensure()
        {
            if (Instance != null) return Instance;
            var hub = AudioHub.Ensure();
            return hub.gameObject.AddComponent<MusicDirector>();
        }

        sealed class Voice { public AudioSource S; public float Env, Target, Gain, Fade = 2.5f; public string Cue; }
        Voice[] _v;
        AudioSource _st;
        int _cur = -1;
        PixAudioBank _bank;
        float _duck = 1f, _duckTarget = 1f, _duckHold;
        float _sceneGain = 1f, _sceneGainTarget = 1f;     // in-match bed level (live play pushes the music down)
        string[] _playlist; int _plIndex; bool _playlistMode; float _plXfade = 3.5f;
        public string CurrentCue { get { return _cur >= 0 ? _v[_cur].Cue : ""; } }
        public string LastStinger { get; private set; } = "";
        public int StingerCount { get; private set; }
        public readonly System.Collections.Generic.List<string> History = new System.Collections.Generic.List<string>();

        // per-cue trim in dB on top of the file loudness (files are mastered per role: menu -22 LUFS, beds -24..-30, stingers about -17)
        static float CueTrim(string cue)
        {
            switch (cue)
            {
                case "match_under_low": return 3f;      // already -30 LUFS: bring it to a readable -27 under the crowd
                case "match_under_tense": return 0f;
                case "st_touchdown": return 0f;
                default: return 0f;
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _bank = PixAudioBank.Load();
            _v = new Voice[2];
            for (int i = 0; i < 2; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false; s.loop = true; s.spatialBlend = 0f; s.ignoreListenerPause = true; s.priority = 20;
                _v[i] = new Voice { S = s };
            }
            _st = gameObject.AddComponent<AudioSource>();
            _st.playOnAwake = false; _st.loop = false; _st.spatialBlend = 0f; _st.ignoreListenerPause = true; _st.priority = 20;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        AudioClip Clip(string cue) { return _bank != null ? _bank.Named(cue) : null; }

        // ---------------------------------------------------------------- loops
        /// <summary>Crossfades to a looping cue (no-op if it is already the current one).</summary>
        public void PlayLoop(string cue, float fade = 2.5f, float trimDb = 0f)
        {
            _playlistMode = false;
            Switch(cue, fade, trimDb);
        }

        void Switch(string cue, float fade, float trimDb)
        {
            if (_cur >= 0 && _v[_cur].Cue == cue && _v[_cur].Target > 0.5f) return;
            var clip = Clip(cue);
            if (clip == null) { Debug.LogWarning("[PIX] music cue missing: " + cue); return; }
            int next = _cur < 0 ? 0 : 1 - _cur;
            var n = _v[next];
            n.S.Stop(); n.S.clip = clip; n.S.loop = true; n.S.time = 0f; n.Cue = cue; n.Gain = Mathf.Pow(10f, (CueTrim(cue) + trimDb) / 20f);
            n.Env = 0f; n.Target = 1f; n.Fade = Mathf.Max(0.05f, fade);
            n.S.volume = 0f; n.S.Play();
            if (_cur >= 0) { _v[_cur].Target = 0f; _v[_cur].Fade = Mathf.Max(0.05f, fade); }
            _cur = next;
            History.Add(cue);
        }

        /// <summary>Fades the current loop out and stops everything after the fade.</summary>
        public void Stop(float fade = 1.5f)
        {
            _playlistMode = false;
            foreach (var v in _v) { v.Target = 0f; v.Fade = Mathf.Max(0.05f, fade); }
        }

        // ---------------------------------------------------------------- scenes
        /// <summary>Main menu: the three menu loops one after the other with equal-power crossfades, starting at a random one.</summary>
        public void PlayMenu()
        {
            _sceneGainTarget = 1f; _duckTarget = 1f;
            if (_playlistMode && _cur >= 0 && _v[_cur].Target > 0.5f) return;
            _playlist = new[] { "menu_calm", "menu_drive", "menu_house" };
            _plIndex = Random.Range(0, _playlist.Length);
            _playlistMode = true;
            Switch(_playlist[_plIndex], 2.0f, 0f);
        }

        /// <summary>Menu fade-out before the scene changes.</summary>
        public void FadeOutMenu(float seconds) { Stop(seconds); }

        /// <summary>Pre-game: the intro cue (build to a hit), then the pregame bed under the coin toss and line-ups.</summary>
        public void PreGame()
        {
            _playlistMode = false; _sceneGainTarget = 1f;
            var intro = Clip("pregame_intro");
            foreach (var v in _v) { v.Target = 0f; v.Fade = 0.8f; }
            if (intro != null) { Stinger("pregame_intro", 0f, 0f); Invoke(nameof(StartPregameBed), Mathf.Max(0.5f, intro.length - 6f)); }
            else StartPregameBed();
        }
        void StartPregameBed() { Switch("pregame_bed", 4f, 0f); }

        /// <summary>Match: the quiet underscore under the crowd.</summary>
        public void MatchBed(bool tense)
        {
            _playlistMode = false;
            if (IsInvoking(nameof(StartPregameBed))) CancelInvoke(nameof(StartPregameBed));
            Switch(tense ? "match_under_tense" : "match_under_low", tense ? 2.5f : 4f, 0f);
        }

        public void Halftime()
        {
            _playlistMode = false; _sceneGainTarget = 1f;
            Stinger("st_halftime", 0f, 0.25f);
            Invoke(nameof(StartHalftimeLoop), 5.5f);
        }
        void StartHalftimeLoop() { Switch("halftime_loop", 3f, 0f); }

        /// <summary>Live play: music sits lower (the crowd and the hits carry the picture); between plays it comes back.</summary>
        public void SetLive(bool live) { _sceneGainTarget = live ? 0.45f : 1f; }

        // ---------------------------------------------------------------- stingers and ducking
        /// <summary>Plays a stinger over the loops and ducks the loops while it rings. Returns its length.</summary>
        public float Stinger(string cue, float trimDb = 0f, float duckLoops = 0.5f)
        {
            var clip = Clip(cue);
            if (clip == null) return 0f;
            _st.Stop(); _st.clip = clip; _st.volume = Mathf.Clamp01(Mathf.Pow(10f, (CueTrim(cue) + trimDb) / 20f) * AudioHub.BusGain(AudioBus.Music));
            _st.Play();
            LastStinger = cue; StingerCount++; History.Add(cue);
            if (duckLoops > 0f) Duck(duckLoops, Mathf.Min(clip.length * 0.8f, 6f));
            return clip.length;
        }

        /// <summary>Pushes the loops down by `amount` (0..1) for `hold` seconds; they come back over about 1.5 s.</summary>
        public void Duck(float amount, float hold)
        {
            amount = Mathf.Clamp01(amount);
            float target = 1f - amount;
            if (target < _duckTarget || _duckHold <= 0f) _duckTarget = target;
            _duckHold = Mathf.Max(_duckHold, hold);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_duckHold > 0f) _duckHold -= dt; else _duckTarget = 1f;
            _duck = Mathf.MoveTowards(_duck, _duckTarget, dt * (_duckTarget < _duck ? 6f : 0.7f));
            _sceneGain = Mathf.MoveTowards(_sceneGain, _sceneGainTarget, dt * 0.35f);
            float bus = AudioHub.BusGain(AudioBus.Music);
            for (int i = 0; i < 2; i++)
            {
                var v = _v[i];
                if (v.S.clip == null) continue;
                v.Env = Mathf.MoveTowards(v.Env, v.Target, dt / v.Fade);
                // equal-power law so the crossfade does not dip
                float e = Mathf.Sin(v.Env * Mathf.PI * 0.5f);
                v.S.volume = Mathf.Clamp01(e * v.Gain * _duck * _sceneGain * bus);
                if (v.Target <= 0f && v.Env <= 0f && v.S.isPlaying) { v.S.Stop(); v.Cue = null; }
            }
            // menu playlist: when the current loop is near its end, fade to the next one
            if (_playlistMode && _cur >= 0)
            {
                var v = _v[_cur];
                if (v.S.clip != null && v.S.isPlaying && v.Target > 0.5f && v.S.time > v.S.clip.length - _plXfade - 0.2f && v.S.time > 5f)
                {
                    _plIndex = (_plIndex + 1) % _playlist.Length;
                    Switch(_playlist[_plIndex], _plXfade, 0f);
                }
            }
        }
    }
}
