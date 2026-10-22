using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Match-long state of the v0.6 look: the kit dirt of every player (grass stains, mud, helmet scuffs) driven by what happens on the field (tackles, dives, falls, ragdolls,
    /// line contact), the wear of the turf (divots at tackles and piles, footsteps, mud, prints in snow) and breath vapour in the cold. The football logic is untouched:
    /// this only observes the simulation events through <see cref="PlayStage"/>.
    /// </summary>
    public sealed class LookDirector : MonoBehaviour
    {
        public static LookDirector Instance { get; private set; }

        public readonly DirtBook Dirt = new DirtBook();
        PlayStage _stage;
        readonly bool[] _engaged = new bool[22];
        readonly AgentAction[] _lastAction = new AgentAction[22];
        readonly float[] _breathAt = new float[22];
        float _stepClock;
        public int DirtEvents { get; private set; }

        public static LookDirector Create(Transform parent, PlayStage stage)
        {
            var go = new GameObject("LookDirector");
            go.transform.SetParent(parent, false);
            var d = go.AddComponent<LookDirector>();
            d._stage = stage; stage.Look = d;
            if (stage.Contacts != null) stage.Contacts.Knocked += d.OnKnocked;
            return d;
        }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; if (_stage != null && _stage.Contacts != null) _stage.Contacts.Knocked -= OnKnocked; }

        public DirtContext Context()
        {
            var w = WeatherSystem.Instance;
            if (w == null || w.Climate == null) return DirtContext.Dry;
            return new DirtContext { Wetness = w.Climate.Wet, Snow = w.Climate.Snow, ArtificialTurf = w.ArtificialTurf };
        }

        int PlayerId(int agent)
        {
            if (_stage == null || _stage.Sim == null || agent < 0 || agent >= 22) return int.MinValue;
            var p = _stage.Sim.Agents[agent].Player;
            return p != null ? p.Id : int.MinValue;
        }

        // ------------------------------------------------------------------------------------------------ dressing
        public JerseyLook LookFor(SimAgent a, UniformSet kit)
        {
            int number = a.Player != null && a.Player.Bio != null ? a.Player.Bio.JerseyNumber : -1;
            DirtState dirt = a.Player != null ? Dirt.Get(a.Player.Id) : default(DirtState);
            var j = JerseyLook.For(number, kit, kit.Home, dirt);
            j.Name = a.Player != null ? PixLook.Surname(a.Player.Name) : "";
            return j;
        }

        /// <summary>Called when a pooled view is dressed for a new play: number, number colours and the dirt this player has collected so far.</summary>
        public void Dress(SimAgentView v, SimAgent a, UniformSet kit)
        {
            if (v == null || v.Avatar == null) return;
            v.Avatar.ApplyLookMode();
            v.Avatar.SetJersey(LookFor(a, kit));
        }

        // ---- v0.9 (GFX-433): wear is stamped under the foot that plants, when it lands (the foot bone stops going down), not under the body root every 0.16 s
        readonly float[] _footY = new float[44], _footVy = new float[44], _footCool = new float[44];
        void FootSteps(int i, SimAgent a, SimAgentView v, float dt, WeatherSystem weather, FieldWear wear, float wet, bool snowy)
        {
            var an = v.Avatar != null ? v.Avatar.Animator : null;
            if (an == null || dt < 1e-5f) return;
            float speed = a.Vel.Length;   // yards per second
            for (int f = 0; f < 2; f++)
            {
                var bone = an.GetBoneTransform(f == 0 ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                if (bone == null) continue;
                int k = i * 2 + f;
                float y = bone.position.y - v.transform.position.y;
                float vy = (y - _footY[k]) / dt;
                bool plant = _footVy[k] < -0.35f && vy > -0.2f && y < 0.17f && _footCool[k] <= 0f;
                _footY[k] = y; _footVy[k] = vy; _footCool[k] -= dt;
                if (!plant || speed <= 2.2f) continue;
                _footCool[k] = 0.14f;
                float s = Mathf.Clamp01((speed - 2f) / 7f);
                bool art = weather != null && weather.ArtificialTurf;
                wear.Footstep(bone.position, 0.2f + 0.08f * s, art ? 0.002f : 0.011f * (0.5f + s), art ? 0f : wet * 0.045f * s, snowy ? 0.55f : 0f);   // two feet at about 3 plants per second replace the one-per-0.16 s root stamp
            }
        }

        // ---- v0.9 (GFX-411): the soft foot shadow under every player on every tier
        readonly Transform[] _blobFor = new Transform[22], _blob = new Transform[22];
        float _blobStrength = -1f;
        void EnsureBlobs()
        {
            if (_stage == null) return;
            var w = WeatherSystem.Instance;
            float k = 1f;
            if (w != null && w.Applied) k = w.Preset.Time == TimeOfDay.Night ? 0.8f : (1.15f - 0.35f * w.Preset.CloudCover);   // a little lighter under flat light where the real shadows are weak anyway
            if (Mathf.Abs(k - _blobStrength) > 0.01f) { _blobStrength = k; FootShadow.SetStrength(k); }
            for (int i = 0; i < 22; i++)
            {
                var v = _stage.Views[i]; if (v == null) { _blobFor[i] = null; _blob[i] = null; continue; }
                if (_blobFor[i] != v.transform) { _blob[i] = FootShadow.Ensure(v.transform); if (_blob[i] != null) _blobFor[i] = v.transform; }
            }
        }

        public void BeginPlay()
        {
            EnsureBlobs();
            if (FieldLogo.Instance != null) { var md = MatchDirector.Instance; int q = md != null && md.Flow != null ? md.Flow.State.Quarter : 1; FieldLogo.Instance.FaceCamera(Camera.main, q <= 2 ? 0 : 1); }
            WipeAtHalftime();
            System.Array.Clear(_engaged, 0, 22);
            for (int i = 0; i < 22; i++) { _lastAction[i] = AgentAction.Idle; _breathAt[i] = Random.value * 1.5f; }
        }

        int _lastHalf = 0;
        /// <summary>v0.9 (GFX-436): at halftime the kits are wiped down by 40 percent (the second half starts a little cleaner).</summary>
        void WipeAtHalftime()
        {
            var md = MatchDirector.Instance; if (md == null || md.Flow == null) return;
            int half = md.Flow.State.Quarter <= 2 ? 1 : 2;
            if (_lastHalf == 1 && half == 2) { Dirt.Wipe(0.4f); for (int i = 0; i < 22; i++) Refresh(i); }
            _lastHalf = half;
        }

        UniformSet KitOf(SimAgent a) { return a.Side == SimSide.Offense ? _stage.OffenseKit : _stage.DefenseKit; }

        void Refresh(int agent)
        {
            if (_stage == null || _stage.Sim == null || agent < 0 || agent >= 22) return;
            var v = _stage.Views[agent]; if (v == null || v.Avatar == null) return;
            var a = _stage.Sim.Agents[agent];
            v.Avatar.SetJersey(LookFor(a, KitOf(a)));
        }

        // ------------------------------------------------------------------------------------------------ events
        void Soil(int agent, DirtEvent e, float intensity)
        {
            int id = PlayerId(agent);
            if (id == int.MinValue || e == DirtEvent.None) return;
            Dirt.Add(id, e, intensity, Context());
            DirtEvents++;
            Refresh(agent);
        }

        void Divot(int agent, float radius, float strength)
        {
            var w = FieldWear.Instance; if (w == null || _stage == null || _stage.Sim == null || agent < 0 || agent >= 22) return;
            var c = Context();
            w.Divot(_stage.WorldPos(agent), radius, strength * (c.ArtificialTurf ? 0.25f : 1f), DirtModel.MudFor(c));
        }

        /// <summary>A simulation event from the stage (called once per event).</summary>
        public void OnEvent(in PlayEvent e)
        {
            switch (e.Type)
            {
                case SimEventType.Tackle:
                    if (e.AgentB < 0) break;
                    switch (e.Tackle)
                    {
                        case TackleType.WrapUp: Soil(e.AgentB, DirtEvent.TackledWrap, 0.8f); Divot(e.AgentB, 0.55f, 0.3f); break;
                        case TackleType.BigHit: Soil(e.AgentB, DirtEvent.TackledHard, 1f); Divot(e.AgentB, 0.75f, 0.5f); break;
                        case TackleType.Shoestring: Soil(e.AgentB, DirtEvent.TackledLow, 0.8f); Divot(e.AgentB, 0.5f, 0.3f); break;
                        case TackleType.DiveTackle: Soil(e.AgentB, DirtEvent.TackledLow, 0.9f); if (e.AgentA >= 0) Soil(e.AgentA, DirtEvent.DiveTackle, 0.9f); Divot(e.AgentB, 0.7f, 0.45f); break;
                        case TackleType.GangTackle: Soil(e.AgentB, DirtEvent.Pile, 0.8f); if (e.AgentA >= 0) Soil(e.AgentA, DirtEvent.Pile, 0.6f); Divot(e.AgentB, 0.95f, 0.55f); break;
                        case TackleType.PushOutOfBounds: Soil(e.AgentB, DirtEvent.Fell, 0.4f); break;
                    }
                    break;
                case SimEventType.BrokenTackle:
                    if (e.AgentB >= 0) { Soil(e.AgentB, DirtEvent.Fell, 0.7f); Divot(e.AgentB, 0.45f, 0.25f); }
                    break;
                case SimEventType.Sack:
                    if (e.AgentB >= 0) { Soil(e.AgentB, DirtEvent.Sacked, 1f); Divot(e.AgentB, 0.85f, 0.55f); }
                    break;
                case SimEventType.Kick:
                    if (e.AgentA >= 0) { Soil(e.AgentA, DirtEvent.KickPlant, 1f); Divot(e.AgentA, 0.45f, 0.35f); }   // GFX-436 / 433: the kicker plants the standing foot at contact
                    break;
                case SimEventType.Pancake:
                    if (e.AgentB >= 0) Soil(e.AgentB, DirtEvent.Pancaked, 0.9f);
                    break;
                case SimEventType.CarrierMove:
                    if (e.AgentA >= 0 && (int)e.Value == (int)CarrierMove.Dive) { Soil(e.AgentA, DirtEvent.Dove, 1f); Divot(e.AgentA, 0.6f, 0.4f); }
                    break;
            }
        }

        void OnKnocked(int agent, float intensity)
        {
            Soil(agent, DirtEvent.Ragdoll, Mathf.Clamp01(0.4f + intensity * 0.6f));
            Divot(agent, 0.6f, 0.35f);
        }

        /// <summary>The play is over: everybody who was locked up at the line takes a little line dirt (once per play).</summary>
        public void EndPlay()
        {
            for (int i = 0; i < 22; i++)
            {
                if (!_engaged[i]) continue;
                _engaged[i] = false;
                Soil(i, DirtEvent.LineContact, 1f);
            }
        }

        // ------------------------------------------------------------------------------------------------ per frame
        public void Tick(float dt)
        {
            if (_stage == null || _stage.Sim == null) return;
            EnsureBlobs();
            var sim = _stage.Sim;
            var weather = WeatherSystem.Instance;
            var wear = FieldWear.Instance;
            bool snowy = weather != null && weather.Climate != null && weather.Climate.Snow > 0.15f;
            float wet = weather != null && weather.Climate != null ? weather.Climate.Wet : 0f;
            _stepClock += dt;
            bool step = _stepClock >= 0.16f;
            if (step) _stepClock = 0f;
            bool cold = weather != null && weather.Preset.BreathVapor && weather.Fx != null;
            for (int i = 0; i < 22; i++)
            {
                var a = sim.Agents[i]; var v = _stage.Views[i]; if (v == null) continue;
                if (a.Action == AgentAction.Engaged || a.Action == AgentAction.Block || a.Action == AgentAction.Shed) _engaged[i] = true;
                if (a.Action != _lastAction[i])
                {
                    if (a.Action == AgentAction.Fallen || a.Action == AgentAction.Stumble) { Soil(i, DirtEvent.Fell, a.Action == AgentAction.Fallen ? 0.6f : 0.25f); if (a.Action == AgentAction.Fallen) Divot(i, 0.45f, 0.2f); }
                    _lastAction[i] = a.Action;
                }
                if (wear != null && wear.Ready) FootSteps(i, a, v, dt, weather, wear, wet, snowy);
                if (cold && v.Avatar != null && v.Avatar.Head != null)
                {
                    _breathAt[i] -= dt;
                    if (_breathAt[i] <= 0f)
                    {
                        _breathAt[i] = 1.7f + Random.value * 1.1f;
                        Vector3 mouth = v.Avatar.Head.position + v.Forward * 0.13f - Vector3.up * 0.04f;
                        weather.Fx.Breath(mouth, v.Forward, a.Vel.Length);
                    }
                }
            }
        }
    }
}
