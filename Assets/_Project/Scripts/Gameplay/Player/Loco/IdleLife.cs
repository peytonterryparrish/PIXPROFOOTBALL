using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.13.0 NATURAL-2 (owner: players stand like statues): a floor of life for any body that stands without moving. <see cref="NaturalBody"/> already breathes and shifts the weight of a plain stand, but a held mocap
    /// pose (a kickoff runner's idle, a hand-down stance, a frozen action clip, an idle clip that is two frames long) can still sit unchanged for many seconds. This layer WATCHES the pose it is given (the final pose
    /// before its own offsets: the animator rewrites every bone each frame, so a pose that does not change is a pose that is held) and, while chest, head and upper arms have not turned for a moment, fades in small slow
    /// offsets above the pelvis: breathing, a weight shift in the trunk, shoulders, a head that drifts, arms and hands that are never quite still. Pelvis and legs are never touched, so planted feet stay planted;
    /// the offsets are 3 degrees at most (head 2), well inside the look limits of <see cref="NaturalLook"/>. It fades out when the pose moves by itself. Rhythms come from the player's own <see cref="NaturalStyle"/>.
    /// <c>-pixIdleLifeOff</c> switches it off (the "before" state of the idle probe).
    /// </summary>
    public sealed class IdleLife
    {
        public static bool Enabled = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixIdleLifeOff") < 0;
        /// <summary>Pose speed (summed over chest, head and the two upper arms, deg/s, low-passed 0.5 s) under which a body counts as still, and the speed above which the layer is fully out.</summary>
        public const float StillBelow = 4f, MovingAbove = 14f;
        /// <summary>Fade time (s) of the layer in and out.</summary>
        public const float FadeIn = 0.9f, FadeOut = 0.35f;

        const int Spine = 0, Chest = 1, Head = 2, LSh = 3, RSh = 4, LUA = 5, RUA = 6, LLA = 7, RLA = 8, LHand = 9, RHand = 10, N = 11;
        readonly Transform[] _t = new Transform[N];
        readonly Quaternion[] _lastOut = new Quaternion[N], _lastBase = new Quaternion[N], _base = new Quaternion[N];
        readonly bool[] _have = new bool[N];
        readonly Quaternion[] _prevPose = new Quaternion[4];
        bool _prevHave;
        float _activity, _w, _clock;
        public NaturalStyle Style = NaturalStyle.Neutral;
        public bool Ok { get; private set; }
        /// <summary>QA: the weight applied (0..1) and the smoothed pose speed.</summary>
        public float Weight { get { return _w; } }
        public float Activity { get { return _activity; } }

        public IdleLife(Animator a)
        {
            if (a == null || !a.isHuman) return;
            _t[Spine] = a.GetBoneTransform(HumanBodyBones.Spine); _t[Chest] = a.GetBoneTransform(HumanBodyBones.Chest); _t[Head] = a.GetBoneTransform(HumanBodyBones.Head);
            _t[LSh] = a.GetBoneTransform(HumanBodyBones.LeftShoulder); _t[RSh] = a.GetBoneTransform(HumanBodyBones.RightShoulder);
            _t[LUA] = a.GetBoneTransform(HumanBodyBones.LeftUpperArm); _t[RUA] = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _t[LLA] = a.GetBoneTransform(HumanBodyBones.LeftLowerArm); _t[RLA] = a.GetBoneTransform(HumanBodyBones.RightLowerArm);
            _t[LHand] = a.GetBoneTransform(HumanBodyBones.LeftHand); _t[RHand] = a.GetBoneTransform(HumanBodyBones.RightHand);
            Ok = _t[Spine] != null && _t[Chest] != null && _t[Head] != null && _t[LUA] != null && _t[RUA] != null;
        }

        public void SetStyle(NaturalStyle s) { Style = s; }

        /// <summary>Pure: weight (0..1) the layer wants for a pose speed (deg/s).</summary>
        public static float WantFor(float activity) { return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(StillBelow, MovingAbove, activity)); }

        /// <summary>Pure: a smooth, never-repeating-looking signal in -1..1 (two incommensurate sines) at about <paramref name="hz"/>.</summary>
        public static float Wave(float t, float hz, float phase) { return 0.62f * Mathf.Sin(2f * Mathf.PI * (hz * t + phase)) + 0.38f * Mathf.Sin(2f * Mathf.PI * (hz * 1.0507f * t + phase * 2.3f + 0.31f)); }

        static bool Same(Quaternion a, Quaternion b) { return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w; }

        /// <summary>Call in LateUpdate, last. <paramref name="active"/> = the body may be given life (not a contact take, not a ragdoll); <paramref name="arms"/> = the arms may move (not a quarterback, a carrier, a hand-down stance).</summary>
        public void Late(float dt, bool active, bool arms)
        {
            if (!Ok || dt <= 1e-5f) return;
            _clock += dt;
            // 1. the pose as it was given to us: a bone we wrote last frame and the animator did not rewrite (a culled animator) still holds our offset, so its base is the one we stored
            for (int i = 0; i < N; i++)
            {
                var t = _t[i]; if (t == null) continue;
                Quaternion cur = t.localRotation;
                _base[i] = _have[i] && Same(cur, _lastOut[i]) ? _lastBase[i] : cur;
            }
            // 2. how much the pose moves by itself: chest, head and both upper arms (local turns are enough for a still / moving test)
            float speed = 0f;
            for (int k = 0; k < 4; k++)
            {
                int bi = k == 0 ? Chest : k == 1 ? Head : k == 2 ? LUA : RUA;
                if (_prevHave) speed += Quaternion.Angle(_prevPose[k], _base[bi]) / dt;
                _prevPose[k] = _base[bi];
            }
            _prevHave = true;
            _activity = Mathf.Lerp(_activity, Mathf.Min(speed, 400f), 1f - Mathf.Exp(-dt / 0.5f));
            float want = active ? WantFor(_activity) : 0f;
            _w = Mathf.MoveTowards(_w, want, dt / (want > _w ? FadeIn : FadeOut));
            if (_w <= 0.002f) { for (int i = 0; i < N; i++) _have[i] = false; return; }
            // 3a. the micro-motion of real standing mocap (IdleLifeData, baked from the Mobility Pro idle and fidget takes): his own take, speed and amplitude
            var data = UseMocap ? IdleLifeData.Load() : null;
            if (data != null && data.takes != null && data.takes.Length > 0) { MocapOffsets(data, arms); return; }
            // 3. offsets (degrees): his own rhythms
            float t0 = _clock + Style.Phase0 * 37f, ph = Style.Phase0;
            float breathHz = Mathf.Clamp(Style.BreathHz, 0.15f, 0.4f), breath = Mathf.Sin(2f * Mathf.PI * (breathHz * t0 + ph));
            float shift = Wave(t0, 1f / Mathf.Max(3f, Style.ShiftPeriod), Style.ShiftPhase);
            float amp = Mathf.Clamp(Style.BreathAmp, 0.4f, 1.3f);
            // trunk: breath (pitch), weight shift (roll and a little yaw)
            Put(Spine, Quaternion.Euler(amp * 0.45f * breath, 0.6f * shift, 1.1f * shift));
            Put(Chest, Quaternion.Euler(amp * 0.65f * breath, 0.9f * Wave(t0 + 3f, 0.09f, ph * 3f), 1.3f * shift));
            // shoulders: a breath lifts them, the weight shift drops one
            Put(LSh, Quaternion.Euler(0f, 0f, 0.9f * breath + 0.8f * shift)); Put(RSh, Quaternion.Euler(0f, 0f, -0.9f * breath - 0.8f * shift));
            // head: drifts about 2 degrees, slowly; the eyes never freeze
            float ha = Mathf.Lerp(0.8f, 1.15f, Style.Phase0);
            Put(Head, Quaternion.Euler(1.1f * ha * Wave(t0 + 7f, 0.21f, ph * 5f), 1.8f * ha * Wave(t0 + 11f, 0.13f, ph * 7f), 0.6f * ha * Wave(t0 + 17f, 0.17f, ph * 11f)));
            if (arms)
            {
                float a = Mathf.Lerp(0.85f, 1.2f, Style.Phase0);
                Put(LUA, Quaternion.Euler(1.4f * a * Wave(t0 + 2f, 0.19f, ph * 13f), 0.8f * a * Wave(t0 + 5f, 0.15f, ph * 17f), 1.1f * a * Wave(t0 + 9f, 0.24f, ph * 19f)));
                Put(RUA, Quaternion.Euler(1.4f * a * Wave(t0 + 4f, 0.17f, ph * 23f), 0.8f * a * Wave(t0 + 8f, 0.14f, ph * 29f), -1.1f * a * Wave(t0 + 13f, 0.22f, ph * 31f)));
                Put(LLA, Quaternion.Euler(0f, 1.8f * a * Wave(t0 + 6f, 0.26f, ph * 37f), 2.2f * a * Wave(t0 + 1f, 0.31f, ph * 41f)));
                Put(RLA, Quaternion.Euler(0f, 1.8f * a * Wave(t0 + 10f, 0.23f, ph * 43f), 2.2f * a * Wave(t0 + 15f, 0.29f, ph * 47f)));
                Put(LHand, Quaternion.Euler(2.2f * a * Wave(t0 + 3f, 0.34f, ph * 53f), 2.6f * a * Wave(t0 + 12f, 0.27f, ph * 59f), 1.8f * a * Wave(t0 + 19f, 0.36f, ph * 61f)));
                Put(RHand, Quaternion.Euler(2.2f * a * Wave(t0 + 14f, 0.32f, ph * 67f), 2.6f * a * Wave(t0 + 21f, 0.25f, ph * 71f), 1.8f * a * Wave(t0 + 23f, 0.38f, ph * 73f)));
            }
            else for (int i = LUA; i <= RHand; i++) _have[i] = false;
        }


        /// <summary>QA: -pixIdleLifeProcedural plays the procedural rhythms even when the baked mocap is present.</summary>
        public static bool UseMocap = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixIdleLifeProcedural") < 0;
        /// <summary>Largest turn (degrees) of every bone from the mocap (spine, chest, head, shoulders, upper arms, forearms, hands): a fidget take that reaches for the face is cut down to a twitch.</summary>
        static readonly float[] MaxDeg = { 3f, 4f, 8f, 4f, 4f, 9f, 9f, 12f, 12f, 16f, 16f };
        int _takeIdx = -1; float _speed = 1f, _amp = 1f, _tOff;

        /// <summary>Which take, how fast and how strong this player's micro-motion is (drawn once from his seed; the idle takes twice as likely as the fidgets).</summary>
        void PickTake(IdleLifeData d)
        {
            int seed = Style.Seed; int n = d.takes.Length;
            float tot = 0f; for (int i = 0; i < n; i++) tot += TakeWeight(d.takes[i]);
            float u = NaturalStyle.U(seed, 2001) * tot, acc = 0f; _takeIdx = n - 1;
            for (int i = 0; i < n; i++) { acc += TakeWeight(d.takes[i]); if (u <= acc) { _takeIdx = i; break; } }
            _amp = Mathf.Lerp(0.75f, 1.25f, NaturalStyle.U(seed, 2002));
            _speed = Mathf.Lerp(0.88f, 1.14f, NaturalStyle.U(seed, 2003));
            _tOff = NaturalStyle.U(seed, 2004) * 40f;
            _pickedSeed = seed;
        }
        int _pickedSeed = int.MinValue;
        static float TakeWeight(IdleLifeData.Take t) { return t.name != null && t.name.Contains("Fgt") ? 1f : 2f; }

        /// <summary>RMS turn (degrees, over all bones above the pelvis) every take is scaled to: a fidget that looks around by 45 degrees becomes a sway of a few, the idle take that moves a degree gets livelier.</summary>
        public const float TargetRms = 1.6f;
        static float TakeScale(IdleLifeData.Take t) { return t.rms > 0.01f ? Mathf.Clamp(TargetRms / t.rms, 0.04f, 4f) : 1f; }

        void MocapOffsets(IdleLifeData d, bool arms)
        {
            if (_takeIdx < 0 || _takeIdx >= d.takes.Length || _pickedSeed != Style.Seed) PickTake(d);
            var tk = d.takes[_takeIdx]; if (tk.frames < 2) return;
            float pos = IdleLifeData.PingPong((_clock + _tOff) * _speed, tk.fps, tk.frames);
            for (int b = 0; b < IdleLifeData.Bones; b++)
            {
                if (!arms && b >= LUA) { _have[b] = false; continue; }
                Vector3 v = d.Sample(tk, pos, b) * (_amp * TakeScale(tk)); float m = v.magnitude, cap = MaxDeg[b];
                if (m > cap) { v *= cap / m; m = cap; }
                Put(b, m > 1e-4f ? Quaternion.AngleAxis(m, v / m) : Quaternion.identity);
            }
        }
        /// <summary>The offset (scaled by the layer's weight) on top of the base pose of one bone.</summary>
        void Put(int i, Quaternion offset)
        {
            var t = _t[i]; if (t == null) return;
            Quaternion res = _base[i] * Quaternion.Slerp(Quaternion.identity, offset, _w);
            t.localRotation = res; _lastOut[i] = res; _lastBase[i] = _base[i]; _have[i] = true;
        }
    }
}
