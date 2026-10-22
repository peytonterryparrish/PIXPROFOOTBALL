using System.Collections.Generic;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Offline sampler used by the animation audit / cleaner / contact-sheet tools: a Medium player instance with a manual PlayableGraph.
    /// Samples a clip at a fixed rate and returns per-frame bone world positions and rotations (root motion applied to the instance).
    /// </summary>
    public sealed class AnimRig
    {
        public static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
            HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
        };
        public const int Hips = 0, Spine = 1, Chest = 2, Neck = 3, Head = 4, LUA = 5, LLA = 6, LH = 7, RUA = 8, RLA = 9, RH = 10,
            LUL = 11, LLL = 12, LF = 13, LT = 14, RUL = 15, RLL = 16, RF = 17, RT = 18, LSh = 19, RSh = 20;
        public const int NB = 21;

        /// <summary>The match avatar carries PlayerAvatar.ModelYaw on its model, so headings measured here are in the game frame (0 = +Z).</summary>
        public static readonly Quaternion StartRot = Quaternion.Euler(0f, PixFootball.Gameplay.PlayerAvatar.ModelYaw, 0f);
        public GameObject Go;
        public Animator Anim;
        public Transform[] T = new Transform[NB];
        PlayableGraph _graph;
        AnimationClipPlayable _cp;
        bool _hasGraph;

        public struct Frame
        {
            public Vector3[] P;     // world positions
            public Quaternion[] R;  // world rotations
            public float t;
        }

        public static AnimRig Create(GameObject prefab)
        {
            var r = new AnimRig();
            r.Go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            r.Go.transform.position = Vector3.zero; r.Go.transform.rotation = StartRot;
            r.Anim = r.Go.GetComponentInChildren<Animator>();
            r.Anim.applyRootMotion = true;
            r.Anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            for (int i = 0; i < NB; i++) r.T[i] = r.Anim.GetBoneTransform(Bones[i]);
            return r;
        }

        public static AnimRig CreateMedium()
        {
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            var prefab = lib != null ? lib.Prefab("player_medium") : null;
            if (prefab == null) { Debug.LogError("[AnimRig] no player_medium prefab"); return null; }
            return Create(prefab);
        }

        void Begin(AnimationClip clip)
        {
            End();
            Go.transform.position = Vector3.zero; Go.transform.rotation = StartRot;
            _graph = PlayableGraph.Create("AnimRig");
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "o", Anim);
            _cp = AnimationClipPlayable.Create(_graph, clip);
            _cp.SetApplyFootIK(false);
            _cp.SetSpeed(1.0);
            output.SetSourcePlayable(_cp);
            _hasGraph = true;
        }

        public void End() { if (_hasGraph) { _graph.Destroy(); _hasGraph = false; } }

        Frame Capture(float t)
        {
            var f = new Frame { P = new Vector3[NB], R = new Quaternion[NB], t = t };
            for (int i = 0; i < NB; i++)
            {
                if (T[i] == null) { f.P[i] = Vector3.zero; f.R[i] = Quaternion.identity; continue; }
                f.P[i] = T[i].position; f.R[i] = T[i].rotation;
            }
            return f;
        }

        /// <summary>Samples [t0, t1] of the clip at the given rate with root motion integrated from the clip start (not from t0).</summary>
        public List<Frame> Sample(AnimationClip clip, float t0, float t1, float rate)
        {
            var list = new List<Frame>();
            Begin(clip);
            float dt = 1f / rate;
            _cp.SetTime(0); _graph.Evaluate(0f);
            float t = 0f;
            // advance (root motion accumulates) to t0
            while (t + dt <= t0 + 1e-4f) { _graph.Evaluate(dt); t += dt; }
            int n = Mathf.Max(1, Mathf.RoundToInt((t1 - t0) * rate));
            list.Add(Capture(t));
            for (int i = 0; i < n; i++)
            {
                _graph.Evaluate(dt); t += dt;
                list.Add(Capture(t));
            }
            End();
            return list;
        }

        /// <summary>Static pose at time t (no root motion).</summary>
        public Frame PoseAt(AnimationClip clip, float t)
        {
            Begin(clip);
            _cp.SetTime(t); _graph.Evaluate(0f);
            var f = Capture(t);
            End();
            return f;
        }

        public void Dispose() { End(); if (Go != null) Object.DestroyImmediate(Go); }

        // ---- geometry helpers ------------------------------------------------------------------------------
        public static Vector3 Forward(Frame f)
        {
            // from the hip line: left->right x up gives forward
            Vector3 r = f.P[RUL] - f.P[LUL]; r.y = 0f;
            if (r.sqrMagnitude < 1e-6f) return Vector3.forward;
            return Vector3.Cross(r.normalized, Vector3.up);
        }

        public static Vector3 ShoulderForward(Frame f)
        {
            Vector3 r = f.P[RUA] - f.P[LUA]; r.y = 0f;
            if (r.sqrMagnitude < 1e-6f) return Vector3.forward;
            return Vector3.Cross(r.normalized, Vector3.up);
        }

        public static float YawOf(Vector3 fwd) { return Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg; }

        /// <summary>Knee flexion in degrees (0 = straight, positive = normal bend, negative = hyperextension).</summary>
        public static float KneeFlex(Frame f, bool left)
        {
            int u = left ? LUL : RUL, l = left ? LLL : RLL, ft = left ? LF : RF;
            Vector3 thigh = (f.P[l] - f.P[u]).normalized, shin = (f.P[ft] - f.P[l]).normalized;
            float ang = Vector3.Angle(thigh, shin);
            // bend direction: the knee bends so that the shin goes backwards relative to the body forward
            Vector3 fwd = Forward(f);
            Vector3 axis = Vector3.Cross(Vector3.up, fwd);              // body right
            Vector3 n = Vector3.Cross(thigh, shin);
            float sign = Vector3.Dot(n, axis) >= 0f ? 1f : -1f;         // thigh->shin turning toward +right-axis means shin swings backward
            return ang * sign * (Mathf.Abs(ang) < 1.5f ? 0f : 1f);
        }

        public static float ElbowFlex(Frame f, bool left)
        {
            int u = left ? LUA : RUA, l = left ? LLA : RLA, h = left ? LH : RH;
            return Vector3.Angle((f.P[l] - f.P[u]), (f.P[h] - f.P[l]));
        }
    }
}
