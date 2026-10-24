using System;
using System.Collections.Generic;
using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>What the crowd reacts to. Shared by the crowd sound (CrowdMix) and the crowd animation (Presentation/Crowd).</summary>
    public enum CrowdCue { Kickoff, Snap, Catch, BigGain, FirstDown, Incomplete, NearMiss, Sack, BigHit, Turnover, Touchdown, FieldGoal, FieldGoalMiss, Safety, Penalty, Punt, Timeout, Final, Halftime }

    /// <summary>
    /// v0.11.0 AUDIO+CROWD: the layered stadium sound. Looping beds (murmur low / mid / high, tension, "noise", claps, cheer, boo, rain, wind) crossfade from the situation (attendance, weather,
    /// down and distance, two-minute drill, who has the ball) and reaction one-shots (cheers, roar, groan, boo, gasp, ooh, claps) fire from sim events with a home bias; the beds duck under each
    /// reaction so a roar reads. Everything is 2D and follows the Crowd slider.
    /// </summary>
    public sealed class CrowdMix : MonoBehaviour
    {
        /// <summary>Raised for every crowd cue (cue, strength 0..1, benefit +1 home / -1 away / 0 neutral): the crowd animation listens.</summary>
        public static event Action<CrowdCue, float, int> Reacted;

        sealed class Layer
        {
            public string Group; public AudioSource A, B; public float Level, Target, Attack = 1.2f, Release = 0.8f, Mix, Age, Rot = 0f;
            public bool Swapping; public float Trim = 1f;
        }

        readonly Dictionary<string, Layer> _layers = new Dictionary<string, Layer>();
        AudioSource[] _shots; int _shotNext;
        PixAudioBank _bank;
        float _bedDuck = 1f, _bedDuckTarget = 1f, _bedDuckHold;

        // ---- situation (set by MatchAudio every frame)
        public float Fullness = 0.85f;      // attendance 0..1
        public float Rain, Wind;
        public bool Pregame;
        public float Tension;               // 0..1 hushed anticipation
        public float Noise;                 // 0..1 home defence "make some noise"
        public float Clap;                  // 0..1 rhythmic claps (timeouts, halftime)
        // ---- internal envelopes
        float _intensity, _cheerBed, _booBed, _hush;
        public float Intensity { get { return _intensity; } }
        public int Reactions;
        public readonly Dictionary<string, int> ReactionCounts = new Dictionary<string, int>();

        static readonly string[] Beds = { "bed_murmur_low", "bed_murmur_mid", "bed_murmur_high", "bed_tension", "bed_noise", "bed_clap", "bed_cheer", "bed_boo", "amb_rain", "amb_wind" };

        public void Init()
        {
            _bank = PixAudioBank.Load();
            foreach (var g in Beds)
            {
                var l = new Layer { Group = g };
                l.A = Make(true); l.B = Make(true);
                if (g == "bed_noise" || g == "bed_cheer" || g == "bed_boo" || g == "bed_clap") { l.Attack = 0.7f; l.Release = 1.2f; }
                if (g == "bed_tension") { l.Attack = 1.5f; l.Release = 1.5f; }
                _layers[g] = l;
            }
            _shots = new AudioSource[6];
            for (int i = 0; i < _shots.Length; i++) _shots[i] = Make(false);
        }

        AudioSource Make(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = loop; s.spatialBlend = 0f; s.priority = 60; s.volume = 0f;
            return s;
        }

        // ---------------------------------------------------------------- reactions
        void Shot(string group, float vol)
        {
            if (_bank == null || _shots == null) return;
            var clip = _bank.Pick(group);
            if (clip == null) return;
            AudioSource s = null;
            for (int i = 0; i < _shots.Length; i++) { var c = _shots[(_shotNext + i) % _shots.Length]; if (!c.isPlaying) { s = c; _shotNext = (_shotNext + i + 1) % _shots.Length; break; } }
            if (s == null) { s = _shots[_shotNext]; _shotNext = (_shotNext + 1) % _shots.Length; }
            s.clip = clip; s.pitch = UnityEngine.Random.Range(0.97f, 1.03f);
            s.volume = Mathf.Clamp01(vol * _bank.Gain(group) * AudioHub.BusGain(AudioBus.Crowd));
            s.Play();
            Reactions++; int n; ReactionCounts.TryGetValue(group, out n); ReactionCounts[group] = n + 1;
            // the beds step back for the reaction, longer for the long ones
            DuckBeds(Mathf.Clamp01(0.25f + 0.5f * vol), Mathf.Min(clip.length * 0.55f, 5f));
        }

        void DuckBeds(float amount, float hold)
        {
            float t = 1f - Mathf.Clamp01(amount);
            if (t < _bedDuckTarget || _bedDuckHold <= 0f) _bedDuckTarget = t;
            _bedDuckHold = Mathf.Max(_bedDuckHold, hold);
        }

        /// <summary>One crowd cue. benefit: +1 good for the home side, -1 good for the visitors, 0 neutral. strength 0..1.</summary>
        /// <summary>A passing swell of attention (a throw in the air, a snap on a big down).</summary>
        public void Stir(float level) { _intensity = Mathf.Max(_intensity, 0.2f + 0.35f * Mathf.Clamp01(level)); }

        public void Cue(CrowdCue cue, float strength, int benefit)
        {
            strength = Mathf.Clamp01(strength);
            if (Reacted != null) Reacted(cue, strength, benefit);
            bool good = benefit > 0, bad = benefit < 0;
            switch (cue)
            {
                case CrowdCue.Touchdown:
                case CrowdCue.Safety:
                    if (good) { Shot("roar_td", 1f); _cheerBed = 9f; _intensity = 1f; Shot("whoop_short", 0.6f); }
                    else if (bad) { Shot("groan", 0.9f); Shot("boo_short", 0.5f); _hush = 4f; _intensity = Mathf.Max(_intensity, 0.5f); }
                    else { Shot("cheer_l", 0.8f); _cheerBed = 4f; _intensity = 0.8f; }
                    break;
                case CrowdCue.FieldGoal:
                    if (good) { Shot("cheer_m", 0.9f); _intensity = Mathf.Max(_intensity, 0.65f); Shot("clap_burst", 0.5f); }
                    else if (bad) { Shot("sigh_relief", 0.5f); _hush = 2f; }
                    else Shot("cheer_s", 0.6f);
                    break;
                case CrowdCue.FieldGoalMiss:
                    if (good) { Shot("cheer_s", 0.8f); Shot("whoop_short", 0.5f); }
                    else if (bad) { Shot("groan", 0.8f); _hush = 2.5f; }
                    else Shot("gasp", 0.6f);
                    break;
                case CrowdCue.Turnover:
                    if (good) { Shot("cheer_l", 1f); Shot("whoop_short", 0.7f); _cheerBed = 5f; _intensity = 0.95f; }
                    else { Shot("groan", 1f); Shot("boo_short", 0.7f); _booBed = 3.5f; _hush = 3.5f; _intensity = Mathf.Max(_intensity, 0.4f); }
                    break;
                case CrowdCue.Sack:
                    Shot("ooh_hit", 0.8f);
                    if (good) { Shot("cheer_m", 0.8f); _intensity = Mathf.Max(_intensity, 0.65f); }
                    else if (bad) { Shot("groan", 0.7f); _hush = 2f; }
                    break;
                case CrowdCue.BigHit:
                    Shot("ooh_hit", 0.45f + 0.5f * strength); _intensity = Mathf.Max(_intensity, 0.35f + 0.3f * strength);
                    break;
                case CrowdCue.BigGain:
                    if (good) { Shot(strength > 0.7f ? "cheer_l" : "cheer_m", 0.6f + 0.4f * strength); _intensity = Mathf.Max(_intensity, 0.5f + 0.4f * strength); }
                    else if (bad) { Shot("groan", 0.5f); _hush = 1.5f; }
                    else { Shot("ooh_hit", 0.5f); }
                    break;
                case CrowdCue.Catch:
                    if (good) { Shot("cheer_s", 0.5f + 0.4f * strength); _intensity = Mathf.Max(_intensity, 0.3f + 0.3f * strength); }
                    else if (strength > 0.4f) Shot("gasp", 0.4f);
                    break;
                case CrowdCue.FirstDown:
                    if (good) { Shot("cheer_s", 0.8f); Shot("clap_burst", 0.5f); _intensity = Mathf.Max(_intensity, 0.45f); }
                    else if (bad) { Shot("sigh_relief", 0.3f); }
                    break;
                case CrowdCue.Incomplete:
                    if (good) { Shot("cheer_s", 0.55f); }          // home defence broke it up
                    else if (bad) { Shot("groan", 0.4f); }          // home offence missed
                    else Shot("gasp", 0.3f);
                    break;
                case CrowdCue.NearMiss: Shot("gasp", 0.55f); break;
                case CrowdCue.Penalty:
                    if (bad) { Shot("boo_short", 0.8f); _booBed = 2.5f; }       // flag on the home team
                    else if (good) { Shot("cheer_s", 0.6f); Shot("clap_burst", 0.3f); }
                    else Shot("gasp", 0.35f);
                    break;
                case CrowdCue.Punt: if (!bad) Shot("sigh_relief", 0.25f); break;
                case CrowdCue.Kickoff: Shot("cheer_s", 0.7f); Shot("whoop_short", 0.4f); _intensity = Mathf.Max(_intensity, 0.4f); break;
                case CrowdCue.Timeout: _booBed = 0f; break;
                case CrowdCue.Halftime: Shot("clap_burst", 0.6f); break;
                case CrowdCue.Final:
                    if (good) { Shot("roar_td", 1f); _cheerBed = 8f; }
                    else if (bad) { Shot("groan", 0.8f); Shot("sigh_relief", 0.5f); }
                    else Shot("cheer_l", 0.7f);
                    break;
                case CrowdCue.Snap: break;
            }
        }

        // ---------------------------------------------------------------- beds
        static float Sat(float x) { return Mathf.Clamp01(x); }

        void Update()
        {
            if (_bank == null) return;
            float dt = Time.unscaledDeltaTime;
            if (_bedDuckHold > 0f) _bedDuckHold -= dt; else _bedDuckTarget = 1f;
            _bedDuck = Mathf.MoveTowards(_bedDuck, _bedDuckTarget, dt * (_bedDuckTarget < _bedDuck ? 5f : 0.8f));
            // slow decay of the excitement so one big play keeps the stadium buzzing for ~20 s
            _intensity = Mathf.MoveTowards(_intensity, 0.18f, dt * 0.05f);
            if (_cheerBed > 0f) _cheerBed -= dt;
            if (_booBed > 0f) _booBed -= dt;
            if (_hush > 0f) _hush -= dt;
            float hush = _hush > 0f ? 0.55f : 1f;
            float cheer = _cheerBed > 0f ? Mathf.Min(1f, _cheerBed / 2f) : 0f;
            float boo = _booBed > 0f ? Mathf.Min(1f, _booBed / 1.5f) : 0f;
            float I = _intensity, F = Fullness, T = Tension;
            float hi = Sat((I - 0.2f) * 1.6f) * F;
            float mid = (1f - 0.75f * hi) * (1f - 0.55f * T) * hush * (0.6f + 0.4f * F);
            Set("bed_murmur_low", Mathf.Max(Pregame ? 0.9f : 0f, Sat(0.75f - F) * 1.2f) * hush);
            Set("bed_murmur_mid", Pregame ? 0.45f : mid);
            Set("bed_murmur_high", hi * (1f - 0.4f * T) * hush);
            Set("bed_tension", T * 0.9f);
            Set("bed_noise", Noise * 0.85f * (1f - 0.5f * cheer));
            Set("bed_clap", Clap * 0.7f);
            Set("bed_cheer", cheer * 0.9f);
            Set("bed_boo", boo * 0.75f);
            Set("amb_rain", Rain); Set("amb_wind", Wind * 0.8f);
            float bus = AudioHub.BusGain(AudioBus.Crowd);
            foreach (var kv in _layers) UpdateLayer(kv.Value, dt, bus);
        }

        void Set(string g, float target) { Layer l; if (_layers.TryGetValue(g, out l)) l.Target = Mathf.Clamp01(target); }

        void UpdateLayer(Layer l, float dt, float bus)
        {
            float rate = l.Target > l.Level ? 1f / l.Attack : 1f / l.Release;
            l.Level = Mathf.MoveTowards(l.Level, l.Target, dt * rate);
            if (l.Level <= 0.002f && l.Target <= 0.002f)
            {
                if (l.A.isPlaying) l.A.Stop();
                if (l.B.isPlaying) l.B.Stop();
                l.Age = 0f; l.Swapping = false;
                return;
            }
            var group = _bank.Get(l.Group);
            if (group == null || group.clips.Count == 0) return;
            if (!l.A.isPlaying && !l.Swapping)
            {
                l.A.clip = _bank.Pick(l.Group); l.A.loop = true; l.A.time = UnityEngine.Random.value * l.A.clip.length * 0.9f; l.A.Play(); l.Age = 0f;
            }
            // rotate to another variant every ~2 loops (ambient layers only) with a 3 s crossfade
            l.Age += dt;
            if (!l.Swapping && group.clips.Count > 1 && l.A.clip != null && l.Age > l.A.clip.length * 2.2f + 8f && l.Level > 0.05f)
            {
                var next = _bank.Pick(l.Group);
                if (next != l.A.clip) { l.B.clip = next; l.B.loop = true; l.B.time = UnityEngine.Random.value * next.length * 0.9f; l.B.volume = 0f; l.B.Play(); l.Swapping = true; l.Mix = 0f; }
                else l.Age = 0f;
            }
            float gain = l.Level * _bank.Gain(l.Group) * bus * _bedDuck;
            if (l.Swapping)
            {
                l.Mix = Mathf.MoveTowards(l.Mix, 1f, dt / 3f);
                l.A.volume = Mathf.Clamp01(gain * Mathf.Cos(l.Mix * Mathf.PI * 0.5f));
                l.B.volume = Mathf.Clamp01(gain * Mathf.Sin(l.Mix * Mathf.PI * 0.5f));
                if (l.Mix >= 1f) { l.A.Stop(); var t = l.A; l.A = l.B; l.B = t; l.Swapping = false; l.Age = 0f; }
            }
            else l.A.volume = Mathf.Clamp01(gain);
        }

        /// <summary>Names and levels of the beds that are audible right now (QA log).</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _layers) if (kv.Value.Level > 0.02f) sb.Append(kv.Key).Append('=').Append(kv.Value.Level.ToString("0.00")).Append(' ');
            return sb.ToString();
        }
    }
}
