using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>What a posed humanoid looks like in world space (v0.6 QA and the clip facing table): heading, height, feet and hands.</summary>
    public struct BodyPose
    {
        public bool Valid;
        /// <summary>Heading (degrees about +Y, 0 = +Z, + toward +X) from the hips and from the shoulders; the body faces the average.</summary>
        public float HipsYaw, ShouldersYaw;
        public float HeadHeight, HipsHeight;
        /// <summary>Foot and hand offsets along the body heading (m, + = forward) and hand heights (m).</summary>
        public float LeftFootFwd, RightFootFwd, LeftHandY, RightHandY, HandSpan;

        public float Yaw
        {
            get
            {
                // the average of two angles: go through vectors so 359 and 1 average to 0
                float a = HipsYaw * Mathf.Deg2Rad, b = ShouldersYaw * Mathf.Deg2Rad;
                return Mathf.Atan2(0.7f * Mathf.Sin(a) + 0.3f * Mathf.Sin(b), 0.7f * Mathf.Cos(a) + 0.3f * Mathf.Cos(b)) * Mathf.Rad2Deg;
            }
        }
    }

    /// <summary>Measures the heading of a posed Humanoid avatar from its bones (left-to-right vector of the hips and the shoulders crossed with up).</summary>
    public static class BodyHeading
    {
        /// <summary>Facing yaw (degrees) of a body whose left-to-right vector is <paramref name="right"/>: forward = right x up.</summary>
        public static float YawOfAcross(Vector3 right)
        {
            right.y = 0f;
            if (right.sqrMagnitude < 1e-6f) return 0f;
            Vector3 fwd = Vector3.Cross(right.normalized, Vector3.up);
            return Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        }

        // ---- v0.9 (GFX-419): the facing loop measured 12 bones for 22 players every frame; the result is cached per animator (bone transforms resolved once) and refreshed at most every 1/30 s
        sealed class Cache { public Transform Lul, Rul, Lua, Rua, Hips, Head, Lf, Rf, Lh, Rh; public float At = -1f; public BodyPose Pose; public Animator A; }
        static readonly System.Collections.Generic.Dictionary<Animator, Cache> _cache = new System.Collections.Generic.Dictionary<Animator, Cache>();
        public const float RefreshSeconds = 1f / 30f;

        public static BodyPose MeasureThrottled(Animator a)
        {
            if (a == null || !a.isHuman) return new BodyPose();
            Cache c;
            if (!_cache.TryGetValue(a, out c))
            {
                c = new Cache { A = a, Lul = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg), Rul = a.GetBoneTransform(HumanBodyBones.RightUpperLeg), Lua = a.GetBoneTransform(HumanBodyBones.LeftUpperArm), Rua = a.GetBoneTransform(HumanBodyBones.RightUpperArm),
                    Hips = a.GetBoneTransform(HumanBodyBones.Hips), Head = a.GetBoneTransform(HumanBodyBones.Head), Lf = a.GetBoneTransform(HumanBodyBones.LeftFoot), Rf = a.GetBoneTransform(HumanBodyBones.RightFoot), Lh = a.GetBoneTransform(HumanBodyBones.LeftHand), Rh = a.GetBoneTransform(HumanBodyBones.RightHand) };
                _cache[a] = c;
            }
            float now = Time.unscaledTime;
            if (c.At >= 0f && now - c.At < RefreshSeconds && now >= c.At) return c.Pose;
            c.At = now;
            var p = new BodyPose();
            if (c.Lul == null || c.Rul == null || c.Lua == null || c.Rua == null) { c.Pose = p; return p; }
            p.HipsYaw = YawOfAcross(c.Rul.position - c.Lul.position);
            p.ShouldersYaw = YawOfAcross(c.Rua.position - c.Lua.position);
            float yaw = p.Yaw * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            float gy = a.transform.position.y;
            if (c.Hips != null) p.HipsHeight = c.Hips.position.y - gy;
            if (c.Head != null) p.HeadHeight = c.Head.position.y - gy;
            if (c.Lf != null) p.LeftFootFwd = Vector3.Dot(c.Lf.position, fwd);
            if (c.Rf != null) p.RightFootFwd = Vector3.Dot(c.Rf.position, fwd);
            if (c.Lh != null) p.LeftHandY = c.Lh.position.y - gy;
            if (c.Rh != null) p.RightHandY = c.Rh.position.y - gy;
            if (c.Lh != null && c.Rh != null) { Vector3 d = c.Lh.position - c.Rh.position; d.y = 0f; p.HandSpan = d.magnitude; }
            p.Valid = true; c.Pose = p;
            return p;
        }

        public static BodyPose Measure(Animator a)
        {
            var p = new BodyPose();
            if (a == null || !a.isHuman) return p;
            Transform lul = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg), rul = a.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            Transform lua = a.GetBoneTransform(HumanBodyBones.LeftUpperArm), rua = a.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (lul == null || rul == null || lua == null || rua == null) return p;
            p.HipsYaw = YawOfAcross(rul.position - lul.position);
            p.ShouldersYaw = YawOfAcross(rua.position - lua.position);
            Transform hips = a.GetBoneTransform(HumanBodyBones.Hips), head = a.GetBoneTransform(HumanBodyBones.Head);
            Transform lf = a.GetBoneTransform(HumanBodyBones.LeftFoot), rf = a.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform lh = a.GetBoneTransform(HumanBodyBones.LeftHand), rh = a.GetBoneTransform(HumanBodyBones.RightHand);
            float yaw = p.Yaw * Mathf.Deg2Rad;
            Vector3 fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            float gy = a.transform.position.y;
            if (hips != null) p.HipsHeight = hips.position.y - gy;
            if (head != null) p.HeadHeight = head.position.y - gy;
            if (lf != null) p.LeftFootFwd = Vector3.Dot(lf.position, fwd);
            if (rf != null) p.RightFootFwd = Vector3.Dot(rf.position, fwd);
            if (lh != null) p.LeftHandY = lh.position.y - gy;
            if (rh != null) p.RightHandY = rh.position.y - gy;
            if (lh != null && rh != null) { Vector3 d = lh.position - rh.position; d.y = 0f; p.HandSpan = d.magnitude; }
            p.Valid = true;
            return p;
        }
    }
}
