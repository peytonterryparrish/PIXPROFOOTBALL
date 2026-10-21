using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Analytic two-bone IK (arm / leg): given the root (shoulder / hip), joint (elbow / knee) and end (hand / foot) positions and a target for the end, returns where the joint and the end go.
    /// The pole direction keeps the elbow / knee bending the way the animation bends it. Pure math, unit tested. The Apply helpers rotate real bones to follow the solved positions.
    /// </summary>
    public static class LimbIK
    {
        /// <summary>Returns the new joint position and the reachable end position (the target clamped to the limb's reach).</summary>
        public static void Solve(Vector3 root, Vector3 joint, Vector3 end, Vector3 target, Vector3 pole, out Vector3 newJoint, out Vector3 newEnd)
        {
            float l1 = Vector3.Distance(root, joint), l2 = Vector3.Distance(joint, end);
            Vector3 toTarget = target - root;
            float d = toTarget.magnitude;
            float reachMax = (l1 + l2) * 0.999f, reachMin = Mathf.Abs(l1 - l2) * 1.001f + 1e-4f;
            if (d < 1e-5f) { newJoint = joint; newEnd = end; return; }
            float dc = Mathf.Clamp(d, reachMin, reachMax);
            Vector3 dir = toTarget / d;
            newEnd = root + dir * dc;
            // law of cosines: distance along the root->target axis of the joint's foot point, and its height off the axis
            float a = (l1 * l1 - l2 * l2 + dc * dc) / (2f * dc);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            Vector3 p = pole - Vector3.Dot(pole, dir) * dir;
            if (p.sqrMagnitude < 1e-8f) { p = joint - root; p -= Vector3.Dot(p, dir) * dir; }
            if (p.sqrMagnitude < 1e-8f) p = Vector3.up - Vector3.Dot(Vector3.up, dir) * dir;
            p.Normalize();
            newJoint = root + dir * a + p * h;
        }

        /// <summary>Rotates the upper and lower bones (parent first) so the chain follows the solved positions. lower's position must be its joint, endBone's the end.</summary>
        public static void Apply(Transform upper, Transform lower, Transform endBone, Vector3 target, Vector3 pole, float weight)
        {
            if (upper == null || lower == null || endBone == null || weight <= 0.001f) return;
            Vector3 root = upper.position, joint = lower.position, end = endBone.position;
            Vector3 nj, ne;
            Solve(root, joint, end, target, pole, out nj, out ne);
            if (weight < 1f) { nj = Vector3.Lerp(joint, nj, weight); ne = Vector3.Lerp(end, ne, weight); }
            // upper bone swings so the joint lands on nj
            Quaternion r1 = Quaternion.FromToRotation(joint - root, nj - root);
            upper.rotation = r1 * upper.rotation;
            // the lower bone follows (its world position moved with the parent); swing it so the end lands on ne
            joint = lower.position; end = endBone.position;
            Quaternion r2 = Quaternion.FromToRotation(end - joint, ne - joint);
            lower.rotation = r2 * lower.rotation;
        }

        /// <summary>Total reach of a limb.</summary>
        public static float Reach(Vector3 root, Vector3 joint, Vector3 end) { return Vector3.Distance(root, joint) + Vector3.Distance(joint, end); }
    }
}
