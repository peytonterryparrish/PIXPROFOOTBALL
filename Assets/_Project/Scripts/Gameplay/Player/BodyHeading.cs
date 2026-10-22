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
