using System.Collections.Generic;
using PixFootball.Sim;
using UnityEngine;
using PixFootball.Core;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 AUDIO+CROWD: sim event / contact / outcome to sound. Target list (every one makes sound): snap, handoff, pitch, throw, catch (soft / hard / contested), drop, incompletion,
    /// deflection, interception, tackle, sack, qb hit, hurry, block win / loss, shed, pancake, broken tackle, jam, pile-on, fumble, recovery, kick / punt / kickoff, kick landing (bounce),
    /// fair catch, blocked kick, out of bounds, touchdown, spike, kneel, penalty flag, whistle, cuts, foot plants, big-hit contacts; plus crowd reactions and music stingers.
    /// </summary>
    public sealed partial class MatchAudio
    {
        readonly HashSet<CrowdCue> _cuedThisPlay = new HashSet<CrowdCue>();
        sealed class PendingHit { public float Due, Made; public int A, B; public Vector3 Pos; public float K; public bool Big, Sack; }
        readonly List<PendingHit> _pendingHits = new List<PendingHit>();
        sealed class Sched { public float Due; public string Key; public Vector3 Pos; public float Vol, Pitch; public int Prio; }
        readonly List<Sched> _sched = new List<Sched>();
        readonly float[] _contactSeen = new float[22];
        string _surf = "dry"; float _surfCheck;
        public int EventsSeen, ContactsSeen;

        PlaySimulation Sim { get { return _md != null ? _md.Sim : null; } }

        void Later(float delay, string key, Vector3 pos, float vol, float pitch, int prio)
        {
            _sched.Add(new Sched { Due = Time.unscaledTime + delay, Key = key, Pos = pos, Vol = vol, Pitch = pitch, Prio = prio });
        }

        void TickPending()
        {
            float now = Time.unscaledTime;
            for (int i = _sched.Count - 1; i >= 0; i--)
            {
                var s = _sched[i];
                if (now < s.Due) continue;
                _sched.RemoveAt(i);
                if (_hub != null) _hub.Play(s.Key, s.Pos, s.Vol, s.Pitch, s.Prio);
            }
            for (int i = _pendingHits.Count - 1; i >= 0; i--)
            {
                var p = _pendingHits[i];
                if (now < p.Due) continue;
                _pendingHits.RemoveAt(i);
                bool seen = (p.A >= 0 && p.A < 22 && _contactSeen[p.A] >= p.Made) || (p.B >= 0 && p.B < 22 && _contactSeen[p.B] >= p.Made);
                if (!seen) PlayHit(p.K, p.Pos, p.Big, p.Sack);
            }
            if (now >= _surfCheck) { _surfCheck = now + 1f; _surf = Surface(); }
        }

        static string Surface()
        {
            var ws = WeatherSystem.Instance;
            if (ws != null && ws.Applied)
            {
                if (ws.Climate != null && ws.Climate.Snow > 0.25f) return "snow";
                if (ws.Preset.Wetness > 0.35f || ws.Preset.RainIntensity > 0.2f) return "wet";
            }
            return "dry";
        }

        static float Sat(float x) { return Mathf.Clamp01(x); }
        static CoreTeam Home { get { return CoreTeam.Home; } }
        static int Bene(CoreTeam t) { return t == CoreTeam.Home ? 1 : -1; }

        // ---------------------------------------------------------------- foot plants (LookDirector.FootSteps calls this for every planted foot of a moving body)
        public static void FootPlant(int agent, Vector3 pos, float speedYd)
        {
            var m = _instance; if (m == null || m._hub == null) return;
            m.Plant(pos, speedYd);
        }

        void Plant(Vector3 pos, float speed)
        {
            // footfalls are only worth starting near the camera (22 bodies at broadcast distance turn into mush and cost voices)
            float dist = Vector3.Distance(_hub.ListenerPosition, pos);
            if (dist > (speed > 4.2f ? 55f : 30f)) return;
            string g = (speed > 4.2f ? "step_run_" : "step_walk_") + _surf;
            float v = Mathf.Lerp(0.3f, 1f, Sat((speed - 2f) / 8f));
            _hub.Play(g, pos, v, Random.Range(0.92f, 1.08f), 1, 1f);
        }

        // ---------------------------------------------------------------- contacts (fire at the contact moment of the choreography)
        public void OnContact(in ContactImpact c)
        {
            ContactsSeen++;
            float now = Time.unscaledTime;
            if (c.AgentA >= 0 && c.AgentA < 22) _contactSeen[c.AgentA] = now;
            if (c.AgentB >= 0 && c.AgentB < 22) _contactSeen[c.AgentB] = now;
            PlayHit(Mathf.Clamp01(c.Intensity), c.Position, c.Big, c.Sack);
        }

        /// <summary>Pad crack by weight: light / medium / heavy / big, helmet clack and a grunt from the medium hits up, a ground thump on the big ones, and the crowd's "ooh".</summary>
        void PlayHit(float k, Vector3 p, bool big, bool sack)
        {
            if (_hub == null) return;
            float vol = 0.6f + 0.4f * k, pitch = Random.Range(0.96f, 1.06f) - 0.14f * k;
            if (big) { _hub.Play("hit_big", p, vol, pitch, 9); _hub.Play("helmet_clack", p, 0.45f + 0.3f * k, Random.Range(0.95f, 1.05f), 6); Later(0.07f, "ground_thump", p, 0.7f + 0.3f * k, Random.Range(0.9f, 1f), 6); }
            else if (k > 0.55f) { _hub.Play("hit_heavy", p, vol, pitch, 8); if (k > 0.7f) _hub.Play("helmet_clack", p, 0.4f, Random.Range(0.95f, 1.05f), 5); }
            else if (k > 0.3f) _hub.Play("hit_medium", p, vol, pitch, 7);
            else _hub.Play("hit_light", p, vol, pitch, 6);
            if (k > 0.35f || sack) Later(0.03f, "grunt_hit", p, 0.45f + 0.4f * k, Random.Range(0.9f, 1.1f), 5);
            if (sack) { _hub.Play("hit_heavy", p, 0.9f, 0.9f, 8); Later(0.09f, "ground_thump", p, 0.9f, 0.9f, 6); }
            if (big || k > 0.75f) { _crowd.Cue(CrowdCue.BigHit, k, 0); _music.Duck(0.35f, 0.6f); }
        }

        // ---------------------------------------------------------------- sim events
        /// <summary>Reacts to a simulation event.</summary>
        public void OnEvent(in PlayEvent e, PlayFrame frame)
        {
            if (_hub == null) return;
            EventsSeen++;
            Vector3 p = frame.ToUnity(e.Pos);
            var sim = Sim;
            switch (e.Type)
            {
                case SimEventType.Snap:
                    _hub.Play("snap_slap", p, 0.9f, Random.Range(0.95f, 1.05f), 7, 1f);
                    _crowd.Cue(CrowdCue.Snap, 0f, 0);
                    break;
                case SimEventType.Handoff: _hub.Play("handoff_slap", p, 0.8f, Random.Range(0.95f, 1.05f), 6); break;
                case SimEventType.Pitch: _hub.Play("throw_whoosh", p, 0.5f, 1.1f, 5); Later(0.12f, "handoff_slap", p, 0.6f, 1f, 5); break;
                case SimEventType.Throw:
                    _hub.Play("throw_whoosh", p, Mathf.Clamp(0.55f + e.Value / 60f, 0.5f, 1f), Random.Range(0.95f, 1.08f), 6);
                    if (e.Note != "away" && e.Value > 18f) _hub.Play("grunt_effort", p, 0.35f, Random.Range(0.95f, 1.1f), 4);
                    _crowd.Stir(0.3f);
                    break;
                case SimEventType.Catch: case SimEventType.Interception: PlayCatch(e, p, sim); break;
                case SimEventType.FairCatch: _hub.Play("catch_soft", p, 0.8f, 1f, 6); break;
                case SimEventType.Drop:
                    _hub.Play("drop_thud", p, 0.8f, Random.Range(0.95f, 1.05f), 6); _crowd.Cue(CrowdCue.NearMiss, 0.6f, 0); break;
                case SimEventType.Incomplete: _hub.Play(_surf == "wet" ? "bounce_wet" : (_surf == "snow" ? "bounce_snow" : "bounce_dry"), p, 0.7f, Random.Range(0.95f, 1.05f), 5); break;
                case SimEventType.Deflection: _hub.Play("shed_slap", p, 0.85f, Random.Range(0.95f, 1.1f), 6); _crowd.Cue(CrowdCue.NearMiss, 0.5f, 0); break;
                case SimEventType.Tackle: case SimEventType.Sack:
                    {
                        float k = Mathf.Clamp01(e.Value / 8f + (e.Tackle == TackleType.BigHit ? 0.5f : 0.2f));
                        _pendingHits.Add(new PendingHit { Due = Time.unscaledTime + 0.45f, Made = Time.unscaledTime - 0.3f, A = e.AgentA, B = e.AgentB, Pos = p, K = k, Big = e.Tackle == TackleType.BigHit, Sack = e.Type == SimEventType.Sack });
                        if (e.Type == SimEventType.Sack) CueOnce(CrowdCue.Sack, 0.8f, SackBenefit(sim));
                        break;
                    }
                case SimEventType.QbHit: _hub.Play("hit_medium", p, 0.8f, Random.Range(0.9f, 1f), 7); Later(0.04f, "grunt_hit", p, 0.55f, Random.Range(0.85f, 1f), 5); break;
                case SimEventType.QbHurry: _hub.Play("grunt_effort", p, 0.45f, Random.Range(0.95f, 1.1f), 4); break;
                case SimEventType.BlockWin: case SimEventType.BlockLoss:
                    _hub.Play("block_thud", p, 0.4f + 0.2f * Random.value, Random.Range(0.92f, 1.08f), 3, 1f);
                    if (Random.value < 0.25f) _hub.Play("helmet_clack", p, 0.3f, Random.Range(0.95f, 1.05f), 3);
                    break;
                case SimEventType.Shed: _hub.Play("shed_slap", p, 0.6f, Random.Range(0.95f, 1.1f), 4); break;
                case SimEventType.Jam: _hub.Play("shed_slap", p, 0.55f, Random.Range(0.9f, 1.05f), 4); _hub.Play("hit_light", p, 0.45f, 1f, 4); break;
                case SimEventType.Pancake: PlayHit(0.55f, p, false, false); _hub.Play("block_thud", p, 0.9f, 0.9f, 6); Later(0.08f, "ground_thump", p, 0.8f, 1f, 5); break;
                case SimEventType.BrokenTackle: _hub.Play("grunt_effort", p, 0.6f, Random.Range(0.9f, 1.05f), 5); _hub.Play("hit_light", p, 0.6f, 1f, 5); _hub.Play("plant_cut", p, 0.7f, 1f, 4); break;
                case SimEventType.PileOn: _hub.Play("pile_scrum", p, 0.7f, Random.Range(0.95f, 1.05f), 6); _hub.Play("grunt_effort", p, 0.4f, 1f, 3); break;
                case SimEventType.Fumble:
                    _hub.Play("fumble_pop", p, 0.9f, Random.Range(0.95f, 1.05f), 8);
                    if (e.Note != "snap") _crowd.Cue(CrowdCue.NearMiss, 0.8f, 0); else _crowd.Cue(CrowdCue.NearMiss, 0.4f, 0);
                    break;
                case SimEventType.Recovery: _hub.Play("pile_scrum", p, 0.6f, 1f, 6); Later(0.1f, "catch_soft", p, 0.5f, 1f, 4); break;
                case SimEventType.Kick:
                    {
                        var kind = sim != null ? sim.Kind : PlayKind.Kickoff;
                        _hub.Play(kind == PlayKind.Punt ? "kick_punt" : "kick_thump", p, kind == PlayKind.FieldGoal ? 0.85f : 1f, Random.Range(0.96f, 1.03f), 9, 0.7f);
                        if (kind == PlayKind.Kickoff) _crowd.Cue(CrowdCue.Kickoff, 0.7f, 0);
                        break;
                    }
                case SimEventType.KickLanded:
                    _hub.Play(_surf == "wet" ? "bounce_wet" : (_surf == "snow" ? "bounce_snow" : "bounce_dry"), p, 0.8f, Random.Range(0.9f, 1f), 5); break;
                case SimEventType.KickBlocked: _hub.Play("hit_heavy", p, 0.9f, 1f, 8); _crowd.Cue(CrowdCue.NearMiss, 0.9f, 0); break;
                case SimEventType.OutOfBounds: _hub.Play("ground_thump", p, 0.45f, 1.1f, 3); break;
                case SimEventType.Touchdown:
                    {
                        int b = ScoreBenefit(sim, e.AgentA);
                        CueOnce(CrowdCue.Touchdown, 1f, b);
                        _music.Stinger("st_touchdown", b > 0 ? 0f : -2f, 0.6f);
                        break;
                    }
                case SimEventType.Safety: CueOnce(CrowdCue.Safety, 0.8f, ScoreBenefit(sim, e.AgentA)); break;
                case SimEventType.Penalty: _hub.Play("flag_toss", p, 0.8f, 1f, 5); break;
                case SimEventType.CarrierMove: _hub.Play("plant_cut", p, 0.75f, Random.Range(0.9f, 1.1f), 5); break;
                case SimEventType.Throwaway: _hub.Play("throw_whoosh", p, 0.7f, 1f, 5); break;
                case SimEventType.Kneel: _hub.Play("ground_thump", p, 0.6f, 1f, 4); break;
                case SimEventType.Spike: _hub.Play("drop_thud", p, 1f, 1f, 6); _hub.Play("ground_thump", p, 0.5f, 1f, 4); break;
                case SimEventType.Whistle: Whistle(false); break;
            }
        }

        void PlayCatch(in PlayEvent e, Vector3 p, PlaySimulation sim)
        {
            string g = "catch_soft"; float v = 0.8f;
            if (sim != null && e.AgentA >= 0 && e.AgentA < 22 && sim.Agents[e.AgentA] != null)
            {
                var r = sim.Agents[e.AgentA]; float best = 99f;
                for (int i = 0; i < 22; i++)
                {
                    var a = sim.Agents[i]; if (a == null || a.Side == r.Side) continue;
                    float dx = a.Pos.X - r.Pos.X, dy = a.Pos.Y - r.Pos.Y; float d = Mathf.Sqrt(dx * dx + dy * dy); if (d < best) best = d;
                }
                if (e.Type == SimEventType.Interception) g = "catch_hard";
                else if (best < 1.7f) { g = "catch_contested"; v = 0.95f; }
                else if (e.Value > 9f || (sim.Ball != null && sim.Ball.Vel.Length > 14f)) { g = "catch_hard"; v = 0.9f; }
            }
            _hub.Play(g, p, v, Random.Range(0.96f, 1.05f), 8, 1f);
            if (g == "catch_contested") Later(0.04f, "grunt_effort", p, 0.4f, Random.Range(0.95f, 1.1f), 4);
        }

        int ScoreBenefit(PlaySimulation sim, int agent)
        {
            if (_md == null || _md.Flow == null) return 0;
            var poss = _md.Flow.State.Possession;
            if (sim == null || agent < 0 || agent > 21 || sim.Agents[agent] == null) return Bene(poss);
            return Bene(sim.Agents[agent].Side == SimSide.Offense ? poss : poss.Opponent());
        }

        int SackBenefit(PlaySimulation sim)
        {
            if (_md == null || _md.Flow == null) return 0;
            return Bene(_md.Flow.State.Possession.Opponent());     // the defence benefits
        }

        void CueOnce(CrowdCue cue, float strength, int benefit)
        {
            if (!_cuedThisPlay.Add(cue)) return;
            _crowd.Cue(cue, strength, benefit);
        }

        // ---------------------------------------------------------------- play outcome (the whistle: the crowd reads the result)
        public void OnOutcome(PlayOutcome o)
        {
            if (_crowd == null || o == null) return;
            CoreTeam off = o.Offense, def = off.Opponent();
            int yards = o.Result != null ? o.Result.Yards : 0;
            bool scrim = o.Kind == PlayKind.Scrimmage;
            switch (o.Headline)
            {
                case Headline.Touchdown: CueOnce(CrowdCue.Touchdown, 1f, Bene(o.ScoringTeam ?? off)); break;
                case Headline.Safety: CueOnce(CrowdCue.Safety, 0.8f, Bene(o.ScoringTeam ?? def)); break;
                case Headline.FieldGoal: _crowd.Cue(CrowdCue.FieldGoal, 0.9f, Bene(off)); _music.Stinger("st_fieldgoal", Bene(off) > 0 ? 0f : -2f, 0.4f); break;
                case Headline.ExtraPoint: case Headline.TwoPointGood: _crowd.Cue(CrowdCue.FieldGoal, 0.5f, Bene(off)); break;
                case Headline.FieldGoalMissed: case Headline.TwoPointFailed: _crowd.Cue(CrowdCue.FieldGoalMiss, 0.8f, Bene(def)); break;
                case Headline.Interception: case Headline.FumbleLost: case Headline.TurnoverOnDowns:
                    _crowd.Cue(CrowdCue.Turnover, 1f, Bene(def)); _music.Stinger("st_turnover", Bene(def) > 0 ? 0f : -1f, 0.5f); break;
                case Headline.Sack: CueOnce(CrowdCue.Sack, 0.8f, Bene(def)); break;
                case Headline.Incomplete: _crowd.Cue(CrowdCue.Incomplete, 0.5f, Bene(def)); break;
                case Headline.Penalty:
                    if (o.PenaltyAccepted && o.PenaltyCall != null) _crowd.Cue(CrowdCue.Penalty, 0.8f, o.PenaltyCall.CommittedBy == CoreTeam.Home ? -1 : 1);
                    else _crowd.Cue(CrowdCue.Penalty, 0.3f, 0);
                    break;
                case Headline.FirstDown: _crowd.Cue(CrowdCue.FirstDown, 0.7f, Bene(off)); break;
                default:
                    if (scrim && o.Result != null && (o.Result.Type == PlayType.Run || o.Result.Type == PlayType.Pass))
                    {
                        if (yards >= 15) _crowd.Cue(CrowdCue.BigGain, Mathf.Clamp01(yards / 40f), Bene(off));
                        else if (o.Result.Type == PlayType.Pass && yards >= 5) _crowd.Cue(CrowdCue.Catch, Mathf.Clamp01(yards / 20f), Bene(off));
                    }
                    break;
            }
            if (o.FirstDownGained && o.Headline != Headline.FirstDown && o.Headline != Headline.Touchdown) _crowd.Cue(CrowdCue.FirstDown, 0.7f, Bene(off));
        }
    }
}
