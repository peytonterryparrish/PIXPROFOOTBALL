using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Presentation
{
    /// <summary>A stand-in player used during replays: a plain avatar (no animation graph, no ragdoll, no locomotion) whose bones are written from the recording.</summary>
    public sealed class ReplayActor
    {
        public PlayerAvatar Avatar;
        public GameObject Go;
        public PlayerSize Size;
        /// <summary>v0.7: <c>size + 3 * (int)BodyBase</c>, the pool key and the recorded body kind.</summary>
        public int Key;
        public BoneSet Set;
        public int AgentIndex = -1;
    }

    /// <summary>
    /// Plays a <see cref="ReplayClip"/> back on a pool of replay actors and a replay ball: the clip time is advanced by the controller (any speed, either direction, paused),
    /// and every LateUpdate the samples around that time are interpolated onto the actors (root position and yaw, bone rotations, hip and root-bone positions, the ball).
    /// </summary>
    [DefaultExecutionOrder(700)]
    public sealed class ReplayPlayer : MonoBehaviour
    {
        readonly Dictionary<int, Stack<ReplayActor>> _free = new Dictionary<int, Stack<ReplayActor>>();
        readonly ReplayActor[] _active = new ReplayActor[22];
        Transform _ball; GameObject _ballGo;
        int _lodTick;

        public ReplayClip Clip { get; private set; }
        public float Time;
        public bool Loaded { get { return Clip != null; } }
        public bool PosesBall = true;

        /// <summary>Creates the replay actors ahead of time (during the loading screen) so the first replay does not hitch.</summary>
        public void Warm(int small, int medium, int large)
        {
            var made = new List<ReplayActor>();
            for (int i = 0; i < small; i++) made.Add(Acquire(PlayerSize.Small));
            for (int i = 0; i < medium; i++) made.Add(Acquire(PlayerSize.Medium));
            for (int i = 0; i < large; i++) made.Add(Acquire(PlayerSize.Large));
            foreach (var a in made) Release(a);
            EnsureBall();
        }

        ReplayActor Acquire(PlayerSize size) { return Acquire((int)size); }

        /// <summary>Acquires an actor of one body kind: <paramref name="key"/> = <c>size + 3 * (int)BodyBase</c> (the 15 bodies of roster v2).</summary>
        ReplayActor Acquire(int key)
        {
            Stack<ReplayActor> st;
            if (_free.TryGetValue(key, out st) && st.Count > 0) { var a = st.Pop(); a.Go.SetActive(true); return a; }
            var size = (PlayerSize)(key % 3);
            PlayerAvatar.PendingBase = (BodyBase)(key / 3);
            var av = PlayerAvatar.Spawn(transform, size, TeamColors.Home, "Replay_" + size);
            var actor = new ReplayActor { Avatar = av, Go = av.gameObject, Size = size, Key = key };
            if (av.Root != null) actor.Set = BoneSet.Build(av.Root, av.Animator);
            if (av.Animator != null) { av.Animator.runtimeAnimatorController = null; av.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
            return actor;
        }

        void Release(ReplayActor a)
        {
            a.Go.SetActive(false); a.AgentIndex = -1;
            Stack<ReplayActor> st;
            if (!_free.TryGetValue(a.Key, out st)) _free[a.Key] = st = new Stack<ReplayActor>();
            st.Push(a);
        }

        void EnsureBall()
        {
            if (_ballGo != null) return;
            var lib = ArtLibrary.Load();
            var prefab = lib != null ? lib.Prefab("prop_footballball") : null;
            if (prefab == null) Debug.LogWarning("[Replay] prop_footballball missing: the replay ball is a plain sphere");
            _ballGo = prefab != null ? Instantiate(prefab, transform) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (prefab == null) { var col = _ballGo.GetComponent<Collider>(); if (col != null) Destroy(col); _ballGo.transform.localScale = new Vector3(0.17f, 0.17f, 0.28f); }
            _ballGo.name = "ReplayBall"; _ball = _ballGo.transform;
            foreach (var r in _ballGo.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _ballGo.SetActive(false);
        }

        /// <summary>Puts a clip on the field: acquires the 22 actors, dresses them in the play's kits, hides nothing else (the caller hides the live stage).</summary>
        public void Load(ReplayClip clip)
        {
            Unload();
            Clip = clip; Time = clip.StartTime;
            EnsureBall();
            for (int i = 0; i < 22; i++)
            {
                var ag = clip.Agents[i];
                var a = Acquire((int)ag.Size);
                a.AgentIndex = i;
                var kit = ag.Side == 0 ? clip.OffenseKit : clip.DefenseKit;
                a.Avatar.SetColors(new TeamColors(kit[0], kit[1], kit[2]));
                a.Avatar.SetHelmetColors(new TeamColors(kit[3], kit[4], kit[5]));
                a.Avatar.ApplyLookMode();
                { var uk = new UniformSet { Uniform = new TeamColors(kit[0], kit[1], kit[2]), Helmet = new TeamColors(kit[3], kit[4], kit[5]), Home = (ag.Side == 0) == clip.OffenseHome, Wordmark = ag.Side == 0 ? clip.OffenseWord : clip.DefenseWord, CrestKey = ag.Side == 0 ? clip.OffenseCrest : clip.DefenseCrest }; var jl = JerseyLook.For(ag.Number, uk, uk.Home, ag.Dirt); jl.Name = PixLook.Surname(ag.Name); a.Avatar.SetJersey(jl); }
                if (ag.PlayerId != -1) { PixFootball.Core.Position rp; if (System.Enum.TryParse(ag.Position, out rp)) a.Avatar.SetAppearance(ag.PlayerId, RoleUtil.AppearanceRole(rp)); }
                a.Go.name = "Replay_P" + i + "_" + ag.Position;
                _active[i] = a;
            }
            _ballGo.SetActive(PosesBall);
            Pose(Time);
        }

        public void Unload()
        {
            for (int i = 0; i < 22; i++) if (_active[i] != null) { Release(_active[i]); _active[i] = null; }
            if (_ballGo != null) _ballGo.SetActive(false);
            Clip = null;
        }

        public Transform BoneOf(int agent, HumanBodyBones bone)
        {
            var a = agent >= 0 && agent < 22 ? _active[agent] : null;
            return a != null && a.Avatar != null && a.Avatar.Animator != null ? a.Avatar.Animator.GetBoneTransform(bone) : null;
        }

        public Transform ActorRoot(int agent) { return agent >= 0 && agent < 22 && _active[agent] != null ? _active[agent].Go.transform : null; }

        /// <summary>Debug: logs where actor <paramref name="agent"/> really is and what its renderers do (the replay QA probe).</summary>
        public string DebugDump(int agent)
        {
            var a = agent >= 0 && agent < 22 ? _active[agent] : null;
            if (a == null) return "actor " + agent + " null";
            var sb = new System.Text.StringBuilder();
            sb.Append("actor " + agent + " go=" + a.Go.name + " active=" + a.Go.activeInHierarchy + " pos=" + a.Go.transform.position.ToString("0.00") + " rec=" + (Clip != null ? Clip.AgentPos(agent, Time).ToString("0.00") : "-") + " parent=" + (a.Go.transform.parent != null ? a.Go.transform.parent.name + "@" + a.Go.transform.parent.position.ToString("0.0") + " scale " + a.Go.transform.lossyScale.ToString("0.00") : "-"));
            if (a.Avatar != null && a.Avatar.Root != null) sb.Append(" root.local=" + a.Avatar.Root.localPosition.ToString("0.00") + " rootworld=" + a.Avatar.Root.position.ToString("0.00"));
            int on = 0, tot = 0, vis = 0;
            foreach (var r in a.Go.GetComponentsInChildren<Renderer>(true)) { tot++; if (r.enabled && r.gameObject.activeInHierarchy) { on++; if (r.isVisible) vis++; } }
            sb.Append(" renderers on/total=" + on + "/" + tot + " visible=" + vis);
            int n = 0;
            foreach (var smr in a.Go.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (n++ >= 3) break;
                sb.Append(" | SMR " + smr.name + " bounds c=" + smr.bounds.center.ToString("0.0") + " s=" + smr.bounds.size.ToString("0.0") + " rootBone=" + (smr.rootBone != null ? smr.rootBone.name + "@" + smr.rootBone.position.ToString("0.0") : "null") + " mesh=" + (smr.sharedMesh != null ? smr.sharedMesh.name : "null") + " bones=" + smr.bones.Length);
            }
            return sb.ToString();
        }

        void LateUpdate() { if (Clip != null) Pose(Time); }

        /// <summary>Interpolates the recording at time t onto the actors.</summary>
        public void Pose(float t)
        {
            var clip = Clip;
            if (clip == null || clip.Frames.Count == 0) return;
            int i; float a; clip.Locate(t, out i, out a);
            var f0 = clip.Frames[i]; var f1 = clip.Frames[Mathf.Min(i + 1, clip.Frames.Count - 1)];
            bool lerp = a > 0.001f && f1 != f0;
            _lodTick++;
            for (int k = 0; k < 22; k++)
            {
                var act = _active[k]; if (act == null || act.Set == null) continue;
                var tr = act.Go.transform;
                Vector3 pos = lerp ? Vector3.Lerp(f0.Pos[k], f1.Pos[k], a) : f0.Pos[k];
                float yaw = lerp ? Mathf.LerpAngle(f0.Yaw[k], f1.Yaw[k], a) : f0.Yaw[k];
                // C-008 (v0.7 note 27): the replay actor IS the avatar object (the live view has a view root above its avatar), so the recorded line-stance setback must be applied to the world position along the facing; writing it to the local position moved all 22 actors to the origin
                float setback = lerp ? Mathf.Lerp(f0.Setback[k], f1.Setback[k], a) : f0.Setback[k];
                Quaternion faceRot = Quaternion.Euler(0f, yaw, 0f);
                tr.SetPositionAndRotation(pos + faceRot * new Vector3(0f, 0f, setback), faceRot);
                // the facing correction of the live view (clip offset / hips loop) and the line stance setback live on the avatar root, not in the recorded bones
                if (act.Avatar != null && act.Avatar.Root != null) { float by = lerp ? Mathf.LerpAngle(f0.BodyYaw[k], f1.BodyYaw[k], a) : f0.BodyYaw[k]; act.Avatar.Root.localRotation = Quaternion.Euler(0f, by, 0f); }
                var bones = act.Set.Bones;
                int ro = clip.RotOffset[k];
                int n = Mathf.Min(bones.Length, clip.RotOffset[k + 1] - ro);
                if (lerp)
                    for (int b = 0; b < n; b++) bones[b].localRotation = Quaternion.Slerp(QuatPack.Unpack(f0.Rot[ro + b]), QuatPack.Unpack(f1.Rot[ro + b]), a);
                else
                    for (int b = 0; b < n; b++) bones[b].localRotation = QuatPack.Unpack(f0.Rot[ro + b]);
                var pb = act.Set.PosBones; int po = clip.PosOffset[k];
                int m = Mathf.Min(pb.Length, clip.PosOffset[k + 1] - po);
                for (int b = 0; b < m; b++) pb[b].localPosition = lerp ? Vector3.Lerp(f0.BonePos[po + b], f1.BonePos[po + b], a) : f0.BonePos[po + b];
                if (act.Avatar.Lod != null && ((_lodTick + k) & 3) == 0) act.Avatar.Lod.Update(pos);
            }
            if (_ball != null && PosesBall)
            {
                bool vis = f0.BallVisible;
                if (_ballGo.activeSelf != vis) _ballGo.SetActive(vis);
                if (vis)
                {
                    Vector3 bp = lerp ? Vector3.Lerp(f0.BallPos, f1.BallPos, a) : f0.BallPos;
                    Quaternion br = lerp ? Quaternion.Slerp(QuatPack.Unpack(f0.BallRot), QuatPack.Unpack(f1.BallRot), a) : QuatPack.Unpack(f0.BallRot);
                    _ball.SetPositionAndRotation(bp, br);
                }
            }
        }
    }
}
