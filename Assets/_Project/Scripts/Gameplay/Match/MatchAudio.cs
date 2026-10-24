using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;
using PixFootball.Core;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 AUDIO+CROWD: the sound director of a match. Every sim event, contact and play outcome becomes sound here (see MatchAudio.Events.cs): on-field effects through the 3D voice pool of
    /// <see cref="AudioHub"/>, the stadium through <see cref="CrowdMix"/> (layered beds plus home-biased reactions) and the score through <see cref="MusicDirector"/> (pre-game cue, quiet underscore,
    /// two-minute tension bed, halftime, final, stingers). This part follows the match state (phase, down and distance, clock, weather) and steers the beds and the music; everything pauses with the game.
    /// </summary>
    public sealed partial class MatchAudio : MonoBehaviour
    {
        AudioHub _hub;
        CrowdMix _crowd;
        MusicDirector _music;
        MatchDirector _md;
        PixAudioBank _bank;
        MatchPhase _lastPhase = MatchPhase.Booting;
        float _tension, _noise, _clap;
        bool _tenseMusic, _halftimeDone, _finalDone;
        int _lastQuarter = 1;
        PlaySimulation _lastSim;

        public CrowdMix Crowd { get { return _crowd; } }
        public MusicDirector Music { get { return _music; } }
        public AudioHub Hub { get { return _hub; } }

        public void Init()
        {
            _hub = AudioHub.Ensure();
            _bank = _hub.Bank;
            _crowd = gameObject.AddComponent<CrowdMix>(); _crowd.Init();
            _music = MusicDirector.Ensure();
            _md = GetComponent<MatchDirector>();
            _crowd.Pregame = true;
            _crowd.Fullness = 0.82f + 0.14f * Hash01((_md != null && _md.Config != null ? _md.Config.Seed : 7) * 31 + 5);
            _hub.ResetStats();
            _instance = this;
            _music.PreGame();
        }

        static MatchAudio _instance;
        /// <summary>The running match's sound director (null in menus / free roam).</summary>
        public static MatchAudio Current { get { return _instance; } }
        void OnDestroy() { if (_instance == this) _instance = null; }

        static float Hash01(int n) { unchecked { uint h = (uint)n * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; return (h & 0xFFFF) / 65535f; } }

        // ---------------------------------------------------------------- legacy entry points (kept for the old call sites)
        /// <summary>Referee whistle; `longBlast` for the end of a quarter / half / game.</summary>
        public void Whistle() { Whistle(false); }
        float _lastWhistleAt = -99f;
        const float WhistleDebounce = 1.5f;
        public void Whistle(bool longBlast)
        {
            float now = Time.realtimeSinceStartup;
            if (now - _lastWhistleAt < WhistleDebounce) return;
            _lastWhistleAt = now;
            if (_hub == null) return;
            Vector3 p = Focus();
            _hub.Play(longBlast ? "whistle_long" : "whistle_short", p, 0.9f, Random.Range(0.97f, 1.03f), 8, 0.6f);
        }

        /// <summary>The pre-snap "call" sound (audible, flip, defender switch): a UI tick, NOT the referee whistle.</summary>
        public void CallCue()
        {
            if (_hub == null) return;
            _hub.Play("snap_slap", Focus(), 0.25f, Random.Range(1.2f, 1.35f), 3, 0.3f);
            _crowd.Stir(0.2f);
        }

        /// <summary>Old table calls from MatchDirector.React: the crowd now reacts from the real outcome (OnOutcome) and the sim events, so these are no-ops.</summary>
        public void Cheer(float strength) { }
        public void Boo() { }
        public void Quiet() { _crowd.Cue(CrowdCue.Timeout, 0f, 0); }
        public void Stir(float level) { if (_crowd != null) _crowd.Stir(level); }
        public void Hit(float intensity, float pan = 0f) { PlayHit(Mathf.Clamp01(intensity), Focus(), false, false); }
        public void Catch(float pan = 0f) { if (_hub != null) _hub.Play("catch_soft", Focus(), 0.8f, 1f, 6, 0.7f); }
        public void Kick() { if (_hub != null) _hub.Play("kick_thump", Focus(), 1f, 1f, 7, 0.7f); }
        public void BlockHit(float pan = 0f) { if (_hub != null) _hub.Play("block_thud", Focus(), 0.6f, 1f, 4, 0.8f); }

        /// <summary>Where the camera is looking: the ball (or the carrier), for sounds that have no world position.</summary>
        Vector3 Focus()
        {
            if (_md != null && _md.Stage != null && _md.Stage.Ball != null) return _md.Stage.Ball.Position;
            var c = Camera.main; return c != null ? c.transform.position + c.transform.forward * 30f : Vector3.zero;
        }

        // ---------------------------------------------------------------- state follower
        void Update()
        {
            if (_md == null || _crowd == null) return;
            var flow = _md.Flow;
            var phase = _md.Phase;
            float dt = Time.unscaledDeltaTime;
            // weather beds
            var w = _md.Weather;
            _crowd.Rain = Mathf.Clamp01(w.RainIntensity * 1.1f) * 0.9f; _crowd.Wind = Mathf.Clamp01(w.WindStrength * 0.7f);
            if (phase != _lastPhase)
            {
                OnPhase(_lastPhase, phase);
                _lastPhase = phase;
            }
            if (_md.Sim != _lastSim) { _lastSim = _md.Sim; _cuedThisPlay.Clear(); _pendingHits.Clear(); }
            if (flow == null) return;
            var s = flow.State;
            float tT = 0f, tN = 0f, tC = 0f;
            bool pre = phase == MatchPhase.PlayCall || phase == MatchPhase.PreSnap;
            bool homeDef = s.Possession == CoreTeam.Away;
            bool twoMin = (s.Quarter == 2 || s.Quarter == 4) && s.NormClock < 120f && Mathf.Abs(s.HomeScore - s.AwayScore) <= 8;
            if (pre || phase == MatchPhase.Live)
            {
                bool late = s.Down >= 3 && s.Phase == GamePhase.Scrimmage;
                if (late && homeDef && pre) { tN = 0.85f; tC = 0.35f; }
                if (late && !homeDef && (s.Distance >= 4 || s.YardsToGoal <= 20)) tT = 0.7f;
                if (s.YardsToGoal <= 8 && !homeDef) tT = Mathf.Max(tT, 0.55f);
                if (twoMin && !homeDef) tT = Mathf.Max(tT, 0.6f);
                if (phase == MatchPhase.Live) { tN = 0f; tC = 0f; }
            }
            if (phase == MatchPhase.Break) tC = 0.55f;
            _tension = Mathf.MoveTowards(_tension, tT, dt * 0.4f);
            _noise = Mathf.MoveTowards(_noise, tN, dt * (tN > _noise ? 0.5f : 1.2f));
            _clap = Mathf.MoveTowards(_clap, tC, dt * 0.5f);
            _crowd.Tension = _tension; _crowd.Noise = _noise; _crowd.Clap = _clap;
            // music: tense bed in the two-minute drill, otherwise the quiet underscore; live play pushes it down
            bool wantTense = twoMin && phase != MatchPhase.Break && phase != MatchPhase.Final && phase != MatchPhase.CoinToss && phase != MatchPhase.Booting;
            if (wantTense != _tenseMusic && phase != MatchPhase.Break && phase != MatchPhase.Final && phase != MatchPhase.CoinToss && phase != MatchPhase.Booting)
            {
                _tenseMusic = wantTense;
                _music.MatchBed(wantTense);
                if (wantTense) _music.Stinger("st_twominute", 0f, 0.4f);
            }
            _music.SetLive(phase == MatchPhase.Live);
            if (s.Quarter != _lastQuarter) { _lastQuarter = s.Quarter; }
            TickPending();
        }

        void OnPhase(MatchPhase from, MatchPhase to)
        {
            switch (to)
            {
                case MatchPhase.CoinToss: _crowd.Pregame = true; break;
                case MatchPhase.PlayCall:
                    if (from == MatchPhase.CoinToss || from == MatchPhase.Booting) { _crowd.Pregame = false; _music.MatchBed(false); _crowd.Cue(CrowdCue.Kickoff, 0.6f, 0); }
                    else if (from == MatchPhase.Break && !_tenseMusic) _music.MatchBed(false);
                    break;
                case MatchPhase.Break:
                    if (_md.Flow != null && _md.Flow.State.Quarter == 3 && !_halftimeDone) { _halftimeDone = true; _music.Halftime(); _crowd.Cue(CrowdCue.Halftime, 0.6f, 0); Whistle(true); }
                    break;
                case MatchPhase.Final:
                    if (!_finalDone && _md.Flow != null)
                    {
                        _finalDone = true;
                        var s = _md.Flow.State; int benefit = s.HomeScore > s.AwayScore ? 1 : (s.HomeScore < s.AwayScore ? -1 : 0);
                        _music.Stinger(benefit >= 0 ? "st_final_win" : "st_final_loss", 0f, 0.5f);
                        _crowd.Cue(CrowdCue.Final, 1f, benefit);
                        Whistle(true);
                    }
                    break;
            }
        }
    }
}
