using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Gameplay
{
    public enum OfficialSignal { None, Touchdown, FirstDown, Incomplete, Penalty, Timeout, Safety, DeadBall }

    /// <summary>Cross-fading clip player for the officials and coaches (no controller: two clips in a mixer, time driven by hand).</summary>
    public sealed class MiniAnim
    {
        PlayableGraph _g; AnimationMixerPlayable _mix;
        readonly AnimationClipPlayable[] _p = new AnimationClipPlayable[2];
        readonly AnimationClip[] _clip = new AnimationClip[2];
        readonly float[] _t = new float[2];
        readonly bool[] _loop = new bool[2];
        int _cur = -1; float _fade = 0.2f, _w; bool _valid;
        public AnimationClip Current { get { return _cur >= 0 ? _clip[_cur] : null; } }
        public bool Finished { get { return _cur < 0 || (!_loop[_cur] && _clip[_cur] != null && _t[_cur] >= _clip[_cur].length - 0.01f); } }
        public float Time01 { get { return _cur >= 0 && _clip[_cur] != null ? Mathf.Clamp01(_t[_cur] / Mathf.Max(0.01f, _clip[_cur].length)) : 0f; } }

        public MiniAnim(Animator animator, string name)
        {
            if (animator == null) return;
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            _g = PlayableGraph.Create(name);
            _g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_g, "Anim", animator);
            _mix = AnimationMixerPlayable.Create(_g, 2);
            output.SetSourcePlayable(_mix);
            for (int i = 0; i < 2; i++) _mix.SetInputWeight(i, 0f);
            _valid = true;
        }

        public void Play(AnimationClip clip, bool loop, float fade = 0.2f, float startAt = 0f)
        {
            if (!_valid || clip == null) return;
            if (_cur >= 0 && _clip[_cur] == clip && _loop[_cur] == loop) return;
            int next = _cur < 0 ? 0 : 1 - _cur;
            if (_p[next].IsValid()) { _g.Disconnect(_mix, next); _g.DestroyPlayable(_p[next]); }
            _p[next] = AnimationClipPlayable.Create(_g, clip);
            _p[next].SetApplyFootIK(false); _p[next].SetSpeed(0.0);
            _g.Connect(_p[next], 0, _mix, next);
            _clip[next] = clip; _t[next] = startAt; _loop[next] = loop;
            _p[next].SetTime(startAt);
            _fade = Mathf.Max(0.01f, fade);
            if (_cur < 0) { _w = 1f; _mix.SetInputWeight(next, 1f); } else { _w = 0f; _mix.SetInputWeight(next, 0f); }
            _cur = next;
        }

        public void Update(float dt, float speed = 1f)
        {
            if (!_valid || _cur < 0) return;
            for (int i = 0; i < 2; i++)
            {
                if (!_p[i].IsValid() || _clip[i] == null) continue;
                _t[i] += dt * speed;
                float len = _clip[i].length;
                if (_loop[i]) _t[i] = Mathf.Repeat(_t[i], len); else _t[i] = Mathf.Min(_t[i], len);
                _p[i].SetTime(_t[i]);
            }
            int other = 1 - _cur;
            _w = Mathf.MoveTowards(_w, 1f, dt / _fade);
            _mix.SetInputWeight(_cur, _w);
            if (_p[other].IsValid()) _mix.SetInputWeight(other, 1f - _w);
            _g.Evaluate(0f);
        }

        public void Destroy() { if (_valid && _g.IsValid()) _g.Destroy(); _valid = false; }
    }

    /// <summary>
    /// The seven-man officiating crew (referee, umpire, head linesman, line judge, field judge, side judge, back judge) following the play with locomotion
    /// clips and signalling touchdowns, first downs, incomplete passes, penalties (flag), timeouts and safeties; plus two sideline coaches per team in team colours.
    /// Everything is optional: without the ArtLibrary prefabs the component builds nothing.
    /// </summary>
    public sealed class MatchOfficials : MonoBehaviour
    {
        sealed class Person
        {
            public Transform Tr; public MiniAnim Anim; public string Role;
            public Vector3 Target; public float Yaw; public float SignalT; public AnimationClip Signal;
            public float Speed;
            public bool Coach; public int Team; public float Bias;
        }

        readonly List<Person> _crew = new List<Person>(), _coaches = new List<Person>();
        ArtLibrary _lib;
        AnimationClip _idle, _walk, _run, _back, _clapC, _headset, _clipboard;
        PlayFrame _frame; bool _hasFrame;
        float _ballZ, _losZ; Vector3 _ballWorld; bool _live;
        public int Count { get { return _crew.Count; } }
        public int CoachCount { get { return _coaches.Count; } }

        public static MatchOfficials Create(Transform parent, TeamInfo home, TeamInfo away)
        {
            var go = new GameObject("MatchOfficials"); go.transform.SetParent(parent, false);
            var m = go.AddComponent<MatchOfficials>();
            m.Build(home, away);
            return m;
        }

        void Build(TeamInfo home, TeamInfo away)
        {
            _lib = ArtLibrary.Load();
            if (_lib == null) return;
            _idle = _lib.Clip("ref_idle"); _walk = _lib.Clip("ref_walk"); _run = _lib.Clip("ref_run"); _back = _lib.Clip("ref_backpedal");
            _clapC = _lib.Clip("coach_clap"); _headset = _lib.Clip("coach_headset_idle"); _clipboard = _lib.Clip("coach_clipboard_idle");
            GameObject rp = _lib.Prefab("referee");
            if (rp != null)
            {
                string[] roles = { "R", "U", "HL", "LJ", "FJ", "SJ", "BJ" };
                foreach (string r in roles) _crew.Add(Spawn(rp, "Official_" + r, r, false, 0));
            }
            string[] keys = { "coach_a", "coach_b", "coach_c" };
            for (int team = 0; team < 2; team++)
            {
                TeamInfo ti = team == 0 ? home : away;
                for (int i = 0; i < 3; i++)
                {
                    GameObject cp = _lib.Prefab(keys[(i + team) % 3]);
                    if (cp == null) continue;
                    var p = Spawn(cp, "Coach_" + (team == 0 ? "H" : "A") + i, "C" + i, true, team);
                    p.Bias = (i - 1) * 4.5f;
                    var tint = p.Tr.GetComponentInChildren<TeamTint>(true);
                    if (tint != null && ti != null) tint.SetTeamColors(ti.PrimaryUi, ti.SecondaryUi);
                    _coaches.Add(p);
                }
            }
        }

        Person Spawn(GameObject prefab, string name, string role, bool coach, int team)
        {
            var go = Instantiate(prefab, transform);
            go.name = name;
            var p = new Person { Tr = go.transform, Role = role, Coach = coach, Team = team };
            var anim = go.GetComponentInChildren<Animator>(true);
            p.Anim = new MiniAnim(anim, name);
            if (coach) p.Anim.Play(role == "C1" ? _clipboard ?? _headset : _headset ?? _idle, true, 0f);
            else p.Anim.Play(_idle, true, 0f);
            go.SetActive(false);
            return p;
        }

        void OnDestroy() { foreach (var p in _crew) p.Anim?.Destroy(); foreach (var p in _coaches) p.Anim?.Destroy(); }

        // ================================================================================================ per play
        /// <summary>Places the crew for a new snap (sim frame: X lateral, Y downfield from the offense's goal line).</summary>
        public void BeginPlay(PlayFrame frame, PlaySimulation sim)
        {
            _frame = frame; _hasFrame = true; _live = false;
            for (int i = 0; i < _crew.Count; i++)
            {
                Vec2 t = CrewTarget(_crew[i].Role, sim.BallX0, sim.LosY, sim.LosY);
                Vector3 w = frame.ToUnity(t);
                if (!_crew[i].Tr.gameObject.activeSelf || (_crew[i].Tr.position - w).magnitude > 40f) { _crew[i].Tr.position = w; _crew[i].Tr.gameObject.SetActive(true); }
                _crew[i].Target = w;
            }
            PlaceCoaches(frame, sim.LosY);
        }

        static Vec2 CrewTarget(string role, float bx, float los, float ballY)
        {
            float W = FieldGeometry.Width;
            switch (role)
            {
                case "R": return new Vec2(Mathf.Clamp(bx + 7f, 6f, W - 6f), Mathf.Max(-9f, ballY - 12f));
                case "U": return new Vec2(Mathf.Clamp(bx - 3f, 6f, W - 6f), Mathf.Min(108f, ballY + 6f));
                case "HL": return new Vec2(-1.6f, ballY);
                case "LJ": return new Vec2(W + 1.6f, ballY);
                case "FJ": return new Vec2(-1.8f, Mathf.Min(108f, ballY + 17f));
                case "SJ": return new Vec2(W + 1.8f, Mathf.Min(108f, ballY + 17f));
                default: return new Vec2(bx, Mathf.Min(112f, ballY + 24f));
            }
        }

        void PlaceCoaches(PlayFrame frame, float losY)
        {
            for (int i = 0; i < _coaches.Count; i++)
            {
                var c = _coaches[i];
                float sideX = c.Team == 0 ? -27.6f : 27.6f;
                Vector3 w = new Vector3(sideX, 0f, Mathf.Clamp(frame.ToUnity(new Vec2(26.6f, losY)).z + c.Bias, -38f, 38f));
                c.Target = w;
                if (!c.Tr.gameObject.activeSelf) { c.Tr.position = w; c.Tr.gameObject.SetActive(true); }
            }
        }

        public void Signal(OfficialSignal s, bool homeTeamInvolved = true)
        {
            string key; bool all = false;
            switch (s)
            {
                case OfficialSignal.Touchdown: key = "ref_signal_td"; all = true; break;
                case OfficialSignal.FirstDown: key = "ref_signal_first_down"; break;
                case OfficialSignal.Incomplete: key = "ref_signal_incomplete"; break;
                case OfficialSignal.Penalty: key = "ref_signal_penalty"; break;
                case OfficialSignal.Timeout: key = "ref_signal_timeout"; break;
                case OfficialSignal.Safety: key = "ref_signal_safety"; break;
                case OfficialSignal.DeadBall: key = "ref_signal_dead_ball"; break;
                default: return;
            }
            if (_lib == null) return;
            int n = _lib.ClipVariantCount(key);
            AnimationClip clip = n > 1 ? _lib.ClipVariant(key, Random.Range(0, n)) : _lib.Clip(key);
            if (clip == null) return;
            for (int i = 0; i < _crew.Count; i++)
            {
                var p = _crew[i];
                bool does = all || p.Role == "R" || (s == OfficialSignal.Incomplete && (p.Role == "U" || p.Role == "BJ") && false);
                if (!does) continue;
                p.Signal = clip; p.SignalT = clip.length; p.Anim.Play(clip, false, 0.15f);
            }
            if (s == OfficialSignal.Touchdown) CoachCheer(homeTeamInvolved ? 0 : 1);
        }

        public void CoachCheer(int team)
        {
            if (_clapC == null) return;
            foreach (var c in _coaches) if (c.Team == team) { c.Signal = _clapC; c.SignalT = _clapC.length * 2f; c.Anim.Play(_clapC, true, 0.2f); }
        }

        // ================================================================================================ per frame
        public void Tick(float dt, PlaySimulation sim, bool live)
        {
            if (!_hasFrame || sim == null) return;
            _live = live;
            float ballY = sim.Ball.Pos.Y;
            float follow = live ? Mathf.Max(sim.LosY - 3f, ballY) : sim.LosY;
            for (int i = 0; i < _crew.Count; i++)
            {
                var p = _crew[i];
                Vec2 t = CrewTarget(p.Role, sim.BallX0, sim.LosY, follow);
                if (p.Role == "R" && live) { float qy = sim.Agents[0].Pos.Y; t = new Vec2(Mathf.Clamp(sim.Agents[0].Pos.X + 7f, 6f, FieldGeometry.Width - 6f), qy - 8f); if (ballY > sim.LosY + 3f) t = new Vec2(t.X, ballY - 9f); }
                p.Target = _frame.ToUnity(t);
                Move(p, dt, 6.0f, _frame.ToUnity(new Vec2(sim.Ball.Pos.X, ballY)));
            }
            for (int i = 0; i < _coaches.Count; i++)
            {
                var c = _coaches[i];
                float sideX = c.Team == 0 ? -27.6f : 27.6f;
                float z = Mathf.Clamp(_frame.ToUnity(new Vec2(26.6f, follow)).z + c.Bias, -40f, 40f);
                c.Target = new Vector3(sideX, 0f, z);
                Move(c, dt, 2.2f, new Vector3(0f, 0f, c.Target.z));
            }
        }

        void Move(Person p, float dt, float maxSpeed, Vector3 look)
        {
            Vector3 pos = p.Tr.position;
            Vector3 to = p.Target - pos; to.y = 0f;
            float d = to.magnitude;
            float want = Mathf.Min(maxSpeed, d * 2.4f);
            if (d < 0.35f) want = 0f;
            p.Speed = Mathf.MoveTowards(p.Speed, want, dt * 9f);
            if (d > 0.001f) pos += to / d * Mathf.Min(d, p.Speed * dt);
            pos.y = 0f; p.Tr.position = pos;
            // facing: the ball when standing, the direction of travel when running
            Vector3 face = p.Speed > 1.2f ? to : look - pos; face.y = 0f;
            if (face.sqrMagnitude > 0.01f) p.Yaw = Mathf.MoveTowardsAngle(p.Yaw, Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg, 360f * dt);
            p.Tr.rotation = Quaternion.Euler(0f, p.Yaw, 0f);
            if (p.SignalT > 0f) { p.SignalT -= dt; if (p.SignalT <= 0f) { p.Signal = null; } }
            else if (p.Signal == null)
            {
                if (p.Coach) { p.Anim.Play(p.Role == "C1" ? (_clipboard ?? _headset) : (_headset ?? _idle), true, 0.25f); }
                else
                {
                    bool backwards = p.Speed > 1.5f && Vector3.Dot(to.normalized, p.Tr.forward) < -0.3f;
                    AnimationClip c = p.Speed > 3.4f ? _run : (p.Speed > 0.5f ? (backwards && _back != null ? _back : _walk) : _idle);
                    p.Anim.Play(c ?? _idle, true, 0.2f);
                }
            }
            else if (p.SignalT <= 0f) p.Signal = null;
            p.Anim.Update(dt, p.Coach || p.Signal != null ? 1f : Mathf.Clamp(0.6f + p.Speed * 0.12f, 0.6f, 1.4f));
        }
    }
}
