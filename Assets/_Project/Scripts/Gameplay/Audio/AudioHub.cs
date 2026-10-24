using System;
using System.Collections.Generic;
using System.IO;
using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    public enum AudioBus { Music, Crowd, Sfx }

    /// <summary>
    /// v0.11.0 AUDIO+CROWD: the one place sound starts. Owns the clip bank, the three buses (the settings sliders; no AudioMixer asset, the mix is code), the master limiter and meter on the
    /// listener, and the 3D one-shot voice pool (spatialised for the camera, per-group concurrency / spacing limits, priority stealing) every on-field effect goes through.
    /// QA flags: <c>-pixAudioSolo music|crowd|sfx</c> mutes the other buses (per-bus loudness), <c>-pixAudioLog file</c> writes every started effect.
    /// </summary>
    [ExecuteAlways]
    public sealed class AudioHub : MonoBehaviour
    {
        public static AudioHub Instance { get; private set; }

        public static AudioHub Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("PixAudioHub");
            if (Application.isPlaying) DontDestroyOnLoad(go);
            return go.AddComponent<AudioHub>();
        }

        public PixAudioBank Bank { get; private set; }
        public MasterLimiter Limiter { get; private set; }

        // ---------------------------------------------------------------- buses
        static string _solo; static bool _soloRead;
        static string Solo
        {
            get
            {
                if (_soloRead) return _solo;
                _soloRead = true; _solo = null;
                var a = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-pixAudioSolo") _solo = a[i + 1].ToLowerInvariant();
                return _solo;
            }
        }

        /// <summary>Linear gain of a bus: its settings slider (0..1), 0 when another bus is soloed for a measurement.</summary>
        public static float BusGain(AudioBus b)
        {
            string s = Solo;
            if (s != null && s != b.ToString().ToLowerInvariant()) return 0f;
            switch (b)
            {
                case AudioBus.Music: return PixSettings.MusicVolume;
                case AudioBus.Crowd: return PixSettings.CrowdVolume;
                default: return PixSettings.SfxVolume;
            }
        }

        // ---------------------------------------------------------------- voices
        sealed class Voice { public AudioSource S; public string Key; public float Vol, EndsAt, StartedAt; public int Prio; }
        const int PoolSize = 28;
        const float MaxHearDistance = 150f;
        Voice[] _voices;
        readonly Dictionary<string, float> _lastStart = new Dictionary<string, float>();
        Transform _listener; float _listenerCheck;
        static AnimationCurve _rolloff;

        struct Limit { public int Max; public float Gap; public Limit(int m, float g) { Max = m; Gap = g; } }
        static readonly Dictionary<string, Limit> Limits = new Dictionary<string, Limit>
        {
            { "step_run_dry", new Limit(6, 0.05f) }, { "step_run_wet", new Limit(6, 0.05f) }, { "step_run_snow", new Limit(6, 0.05f) },
            { "step_walk_dry", new Limit(4, 0.08f) }, { "step_walk_wet", new Limit(4, 0.08f) }, { "step_walk_snow", new Limit(4, 0.08f) },
            { "plant_cut", new Limit(3, 0.1f) }, { "slide_turf", new Limit(2, 0.2f) },
            { "block_thud", new Limit(4, 0.1f) }, { "helmet_clack", new Limit(3, 0.14f) }, { "shed_slap", new Limit(3, 0.12f) },
            { "grunt_hit", new Limit(2, 0.3f) }, { "grunt_effort", new Limit(2, 0.35f) },
            { "hit_light", new Limit(4, 0.05f) }, { "hit_medium", new Limit(4, 0.05f) }, { "hit_heavy", new Limit(3, 0.07f) }, { "hit_big", new Limit(2, 0.12f) },
            { "pile_scrum", new Limit(1, 0.4f) }, { "ground_thump", new Limit(3, 0.1f) },
            { "whistle_short", new Limit(1, 0.4f) }, { "whistle_long", new Limit(1, 0.4f) },
            { "throw_whoosh", new Limit(2, 0.15f) }, { "snap_slap", new Limit(1, 0.3f) }, { "chain_clank", new Limit(1, 0.4f) },
        };

        // ---------------------------------------------------------------- stats / QA
        public readonly Dictionary<string, int> Started = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Missing = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Dropped = new Dictionary<string, int>();
        public int TotalStarted, TotalDropped, MaxSimultaneous;
        StreamWriter _log; float _logT0;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Bank = PixAudioBank.Load();
            if (Bank == null) Debug.LogWarning("[PIX] PixAudioBank missing (PIX Football > Audio > Build Audio Bank): the game has no music, crowd or effect sounds.");
            if (_rolloff == null)
            {
                // the broadcast camera sits 30-60 m from the action: keep full level out to 20 m and fall off gently so a tackle at the far hash is still clearly heard
                _rolloff = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.13f, 0.85f), new Keyframe(0.33f, 0.5f), new Keyframe(0.66f, 0.2f), new Keyframe(1f, 0.08f));
                for (int i = 0; i < _rolloff.length; i++) _rolloff.SmoothTangents(i, 0f);
            }
            _voices = new Voice[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("Sfx" + i); go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false; s.loop = false; s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Custom; s.SetCustomCurve(AudioSourceCurveType.CustomRolloff, _rolloff);
                s.minDistance = 1f; s.maxDistance = MaxHearDistance; s.dopplerLevel = 0f; s.spread = 40f; s.priority = 100;
                _voices[i] = new Voice { S = s };
            }
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++)
                if (a[i] == "-pixAudioLog")
                {
                    try { _log = new StreamWriter(a[i + 1], false) { AutoFlush = true }; _logT0 = Time.realtimeSinceStartup; _log.WriteLine("t,key,vol,pitch,x,y,z"); }
                    catch (Exception e) { Debug.LogWarning("[PIX] audio log: " + e.Message); }
                }
        }

        void OnDestroy() { if (Instance == this) Instance = null; if (_log != null) { _log.Dispose(); _log = null; } }

        void Update()
        {
            if (Time.unscaledTime >= _listenerCheck)
            {
                _listenerCheck = Time.unscaledTime + 1f;
                var l = FindAnyObjectByType<AudioListener>();
                if (l != null)
                {
                    _listener = l.transform;
                    if (Limiter == null || Limiter.gameObject != l.gameObject)
                    {
                        Limiter = l.GetComponent<MasterLimiter>();
                        if (Limiter == null) Limiter = l.gameObject.AddComponent<MasterLimiter>();
                    }
                }
            }
            if (_voices != null)
            {
                int n = 0; float t = Time.time;
                for (int i = 0; i < PoolSize; i++) if (_voices[i].S.isPlaying) n++;
                if (n > MaxSimultaneous) MaxSimultaneous = n;
            }
        }

        public Vector3 ListenerPosition
        {
            get
            {
                if (_listener != null) return _listener.position;
                var c = Camera.main; return c != null ? c.transform.position : Vector3.zero;
            }
        }

        /// <summary>Forgets when each group last started (tests that fire events back to back).</summary>
        public void ResetLimits() { _lastStart.Clear(); if (_voices != null) foreach (var v in _voices) { v.S.Stop(); v.Key = null; } }

        public void ResetStats()
        {
            Started.Clear(); Missing.Clear(); Dropped.Clear(); TotalStarted = TotalDropped = 0; MaxSimultaneous = 0;
            if (Limiter != null) Limiter.ResetStats();
        }

        void Count(Dictionary<string, int> d, string k) { int n; d.TryGetValue(k, out n); d[k] = n + 1; }

        /// <summary>
        /// Plays one effect of the group at a world position. vol is the event strength 0..1+ (times the group trim and the SFX slider), prio 0..9 decides who is stolen when the pool is full
        /// (a hit beats a footstep), spatial 0 plays it flat (UI-like), 1 fully 3D. Returns false when the effect was limited, too far, silent or the bank has no such group.
        /// </summary>
        public bool Play(string key, Vector3 pos, float vol = 1f, float pitch = 1f, int prio = 5, float spatial = 1f)
        {
            if (Bank == null || _voices == null) return false;
            var clip = Bank.Pick(key);
            if (clip == null) { Count(Missing, key); return false; }
            float now = Time.unscaledTime;
            Limit lim;
            bool hasLim = Limits.TryGetValue(key, out lim);
            float gain = vol * Bank.Gain(key) * BusGain(AudioBus.Sfx);
            if (gain < 0.0008f) return false;
            if (spatial > 0.01f)
            {
                float d = Vector3.Distance(ListenerPosition, pos);
                if (d > MaxHearDistance) { Count(Dropped, key); TotalDropped++; return false; }
                float att = _rolloff.Evaluate(Mathf.Clamp01(d / MaxHearDistance));
                if (gain * att < 0.0015f) { Count(Dropped, key); TotalDropped++; return false; }
            }
            if (hasLim)
            {
                float last; if (_lastStart.TryGetValue(key, out last) && now - last < lim.Gap) { Count(Dropped, key); TotalDropped++; return false; }
                int playing = 0; Voice oldest = null;
                for (int i = 0; i < PoolSize; i++) { var v = _voices[i]; if (v.Key == key && v.S.isPlaying) { playing++; if (oldest == null || v.StartedAt < oldest.StartedAt) oldest = v; } }
                if (playing >= lim.Max)
                {
                    if (prio < 6 || oldest == null) { Count(Dropped, key); TotalDropped++; return false; }
                    oldest.S.Stop();
                }
            }
            Voice voice = null;
            for (int i = 0; i < PoolSize; i++) if (!_voices[i].S.isPlaying) { voice = _voices[i]; break; }
            if (voice == null)
            {
                // steal the weakest running voice that is not more important than the new one (lowest priority, then quietest, then oldest)
                float best = float.MaxValue;
                for (int i = 0; i < PoolSize; i++)
                {
                    var v = _voices[i]; if (v.Prio > prio) continue;
                    float score = v.Prio * 100f + v.Vol * 10f - (now - v.StartedAt) * 0.01f;
                    if (score < best) { best = score; voice = v; }
                }
                if (voice == null) { Count(Dropped, key); TotalDropped++; return false; }
                voice.S.Stop();
            }
            var s = voice.S;
            s.transform.position = pos;
            s.spatialBlend = Mathf.Clamp01(spatial);
            s.clip = clip; s.pitch = pitch; s.volume = Mathf.Clamp01(gain); s.panStereo = 0f;
            s.Play();
            voice.Key = key; voice.Vol = gain; voice.Prio = prio; voice.StartedAt = now; voice.EndsAt = now + clip.length / Mathf.Max(0.1f, pitch);
            _lastStart[key] = now;
            Count(Started, key); TotalStarted++;
            if (_log != null) _log.WriteLine((Time.realtimeSinceStartup - _logT0).ToString("0.000") + "," + key + "," + gain.ToString("0.000") + "," + pitch.ToString("0.00") + "," + pos.x.ToString("0.0") + "," + pos.y.ToString("0.0") + "," + pos.z.ToString("0.0"));
            return true;
        }

        /// <summary>Plays a clip flat (2D) on a pooled voice, for one-shots that are not in the world (ambient reaction clips go through <c>CrowdMix</c> instead).</summary>
        public bool Play2D(string key, float vol = 1f, float pitch = 1f, int prio = 5) { return Play(key, ListenerPosition, vol, pitch, prio, 0f); }
    }
}
