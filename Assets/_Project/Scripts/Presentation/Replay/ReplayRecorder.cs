using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Presentation
{
    /// <summary>The animated transforms of one player model (a skinned bone, or an ancestor of one), in a stable order shared by every player of the same body size.</summary>
    public sealed class BoneSet
    {
        public Transform[] Bones;
        public Transform[] PosBones;
        public int[] PosIndex;

        /// <summary>Finds the transforms a skinned player actually animates: every skinned bone plus the nodes between them and the model root (the root itself is posed by the view).</summary>
        public static BoneSet Build(Transform root, Animator animator)
        {
            var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var animated = new HashSet<Transform>();
            foreach (var smr in smrs)
                foreach (var b in smr.bones)
                {
                    for (var t = b; t != null && t != root; t = t.parent) if (!animated.Add(t)) break;
                }
            var hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips != null) for (var t = hips; t != null && t != root; t = t.parent) animated.Add(t);
            var list = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t != root && animated.Contains(t)) list.Add(t);
            var pos = new List<int>();
            for (int i = 0; i < list.Count; i++) if (list[i].parent == root || list[i] == hips) pos.Add(i);
            var set = new BoneSet { Bones = list.ToArray(), PosIndex = pos.ToArray() };
            set.PosBones = new Transform[pos.Count];
            for (int i = 0; i < pos.Count; i++) set.PosBones[i] = list[pos[i]];
            return set;
        }
    }

    /// <summary>
    /// Instant-replay recorder. Choice and why: a replay could be rebuilt by re-running the deterministic play simulation from its seed, but what the viewer sees is not a function of the
    /// simulation alone (the contact director warps players onto authored tackle takes, the active ragdoll is physics, the carry arm and clip variants depend on view state and the user's
    /// input timing), so a re-simulated play would look different from the one that was watched. The recorder therefore stores what was shown: every 1/30 s the root pose of the 22 players
    /// and the local rotation of each animated bone (4 bytes, smallest-three packing) plus the ball. About 6 KB per sample, 1.8 MB for a ten second play, one pass over about 2,200
    /// transforms per sample (a fraction of a millisecond). Playback poses plain avatars from the samples, so it is exact and independent of the animation graph.
    /// </summary>
    [DefaultExecutionOrder(600)]
    public sealed class ReplayRecorder : MonoBehaviour
    {
        public const float SampleStep = 1f / 30f;
        public const float MaxSeconds = 26f, PreSnapKeep = 1.1f;

        readonly ReplayLayout[] _layouts = new ReplayLayout[3];
        readonly Dictionary<SimAgentView, BoneSet> _sets = new Dictionary<SimAgentView, BoneSet>();
        readonly Stack<ReplayFrame> _pool = new Stack<ReplayFrame>();
        readonly BoneSet[] _agentSets = new BoneSet[22];
        MatchDirector _dir;
        ReplayClip _clip;
        bool _recording, _snapped;
        float _clock, _nextSample, _snapAt;

        public ReplayClip Current { get { return _clip; } }
        public bool Recording { get { return _recording; } }
        public double LastSampleMs { get; private set; }
        /// <summary>Seconds since the snap (negative before it) on the recorder clock.</summary>
        public float SnapRelativeTime { get { return _snapped ? _clock - _snapAt : -1f; } }
        public long Samples { get; private set; }
        public double TotalSampleMs { get; private set; }

        public void Attach(MatchDirector d) { _dir = d; }

        /// <summary>A play was built: start a fresh clip for it (the previous uncommitted clip is dropped).</summary>
        public ReplayClip Begin(PlaySimulation sim, PlayFrame frame)
        {
            if (_clip != null) Recycle(_clip);
            _clip = new ReplayClip { TowardsPlusZ = frame.TowardsPlusZ };
            _recording = true; _snapped = false; _clock = 0f; _nextSample = 0f; _snapAt = 0f;
            var stage = _dir.Stage;
            for (int i = 0; i < 22; i++)
            {
                var a = sim.Agents[i]; var v = stage.Views[i];
                int size = v != null ? (int)v.Size : 1;
                _clip.Agents[i] = new ReplayAgent
                {
                    Size = (byte)size, Side = (byte)(a.Side == SimSide.Offense ? 0 : 1), Slot = (byte)a.Slot, Position = a.PlayPos.ToString(),
                    Name = a.Player != null ? a.Player.Name : a.Label, Number = a.Player != null ? a.Player.Bio.JerseyNumber : 0, PlayerId = a.Player != null ? a.Player.Id : -1,
                    Dirt = a.Player != null && _dir.Look != null ? _dir.Look.Dirt.Get(a.Player.Id) : default(PixFootball.Core.DirtState)
                };
                _agentSets[i] = v != null ? SetFor(v) : null;
                if (v != null && _layouts[size] == null) _layouts[size] = new ReplayLayout { BoneCount = _agentSets[i].Bones.Length, PosBones = _agentSets[i].PosIndex };
            }
            for (int s = 0; s < 3; s++) _clip.Layouts[s] = _layouts[s];
            _clip.BuildOffsets();
            var ok = stage.OffenseKit; var dk = stage.DefenseKit;
            Kit(_clip.OffenseKit, ok); Kit(_clip.DefenseKit, dk); _clip.OffenseHome = ok.Home; _clip.OffenseWord = ok.Wordmark; _clip.DefenseWord = dk.Wordmark; _clip.OffenseCrest = ok.CrestKey; _clip.DefenseCrest = dk.CrestKey;
            _clip.LosPos = frame.ToUnity(new Vec2(sim.BallX0, sim.LosY));
            return _clip;
        }

        static void Kit(Color[] dst, UniformSet k)
        {
            dst[0] = k.Uniform.Primary; dst[1] = k.Uniform.Secondary; dst[2] = k.Uniform.Tertiary;
            dst[3] = k.Helmet.Primary; dst[4] = k.Helmet.Secondary; dst[5] = k.Helmet.Tertiary;
        }

        BoneSet SetFor(SimAgentView v)
        {
            BoneSet s;
            if (_sets.TryGetValue(v, out s)) return s;
            s = BoneSet.Build(v.Avatar.Root, v.Avatar.Animator);
            _sets[v] = s;
            return s;
        }

        /// <summary>The snap: pre-snap samples older than a second before it are dropped and the clip's time zero is the snap.</summary>
        public void MarkSnap()
        {
            if (_clip == null || _snapped) return;
            _snapped = true; _snapAt = _clock;
            TrimBefore(_clock - PreSnapKeep);
        }

        public void Stop() { _recording = false; }

        /// <summary>Ends the recording and returns the finished clip with frame times relative to the snap (negative before it).</summary>
        public ReplayClip Finish()
        {
            if (_clip == null) return null;
            _recording = false;
            var c = _clip; _clip = null;
            if (c.Frames.Count < 4) { Recycle(c); return null; }
            float t0 = _snapped ? _snapAt : c.Frames[c.Frames.Count - 1].T - 3f;
            foreach (var f in c.Frames) f.T -= t0;
            c.SnapTime = 0f;
            c.EndTime = c.Frames[c.Frames.Count - 1].T;
            return c;
        }

        public void Recycle(ReplayClip c) { foreach (var f in c.Frames) if (_pool.Count < 1200) _pool.Push(f); c.Frames.Clear(); }

        void TrimBefore(float t)
        {
            if (_clip == null) return;
            int n = 0;
            while (n < _clip.Frames.Count - 1 && _clip.Frames[n].T < t) n++;
            if (n <= 0) return;
            for (int i = 0; i < n; i++) if (_pool.Count < 1200) _pool.Push(_clip.Frames[i]);
            _clip.Frames.RemoveRange(0, n);
        }

        void LateUpdate()
        {
            if (!_recording || _clip == null || _dir == null || _dir.Stage == null || _dir.Stage.Sim == null) return;
            _clock += Time.deltaTime;
            if (_clock < _nextSample) return;
            _nextSample = Mathf.Max(_nextSample + SampleStep, _clock - SampleStep);
            if (_snapped && _clock - _snapAt > MaxSeconds) { _recording = false; return; }
            Sample();
            if (!_snapped) TrimBefore(_clock - PreSnapKeep);
        }

        void Sample()
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var stage = _dir.Stage;
            ReplayFrame f = _pool.Count > 0 ? _pool.Pop() : null;
            if (f == null || f.Rot.Length != _clip.RotCount || f.BonePos.Length != _clip.PosCount) f = new ReplayFrame(_clip.RotCount, _clip.PosCount);
            f.T = _clock;
            for (int i = 0; i < 22; i++)
            {
                var v = stage.Views[i]; var set = _agentSets[i];
                if (v == null || set == null) continue;
                var tr = v.transform;
                f.Pos[i] = tr.position; f.Yaw[i] = tr.eulerAngles.y;
                f.BodyYaw[i] = v.Avatar != null && v.Avatar.Root != null ? v.Avatar.Root.localEulerAngles.y : PlayerAvatar.ModelYaw; f.Setback[i] = v.Avatar != null ? v.Avatar.transform.localPosition.z : 0f;
                int ro = _clip.RotOffset[i], po = _clip.PosOffset[i];
                var bones = set.Bones;
                int n = Mathf.Min(bones.Length, _clip.RotOffset[i + 1] - ro);
                for (int b = 0; b < n; b++) f.Rot[ro + b] = QuatPack.Pack(bones[b].localRotation);
                var pb = set.PosBones;
                int m = Mathf.Min(pb.Length, _clip.PosOffset[i + 1] - po);
                for (int b = 0; b < m; b++) f.BonePos[po + b] = pb[b].localPosition;
            }
            var ball = stage.Ball;
            if (ball != null) { var bt = ball.transform; f.BallPos = bt.position; f.BallRot = QuatPack.Pack(bt.rotation); f.BallVisible = ball.gameObject.activeInHierarchy; }
            _clip.Frames.Add(f);
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            LastSampleMs = ms; TotalSampleMs += ms; Samples++;
        }
    }
}
