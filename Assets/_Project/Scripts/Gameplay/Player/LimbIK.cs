using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Analytic two-bone IK (arm / leg): given the root (shoulder / hip), joint (elbow / knee) and end (hand / foot) positions and a target for the end, returns where the joint and the end go.
    /// The pole direction keeps the elbow / knee bending the way the animation bends it. Pure math, unit tested. The Apply helpers rotate real bones to follow the solved positions.
    /// v0.10.0 BODIES: <see cref="Apply"/> also rolls the upper bone about its own axis so the joint's hinge lies across the bending plane (without it the forearm picked up a quarter turn of roll
    /// whenever the arm was moved far from the animation: candy-wrapper wrists), and a hand follows the forearm for the larger part of its roll (<see cref="HandKeepWorld"/>) instead of holding its world orientation.
    /// </summary>
    public static class LimbIK
    {
        static readonly Dictionary<Transform, Vector3> Hinges = new Dictionary<Transform, Vector3>();
        /// <summary>Roll the upper bone so the registered hinge lies across the bending plane.</summary>
        public static bool AlignHinges = true;
        /// <summary>How much of its world orientation an arm's hand keeps when the arm moves (1 = all, the old behaviour; a foot always keeps all of it).</summary>
        public static float HandKeepWorld = 0.4f;

        /// <summary>Registers the flexion hinge of a joint (an elbow): <paramref name="hingeLocal"/> is the axis in the lower bone's own space, positive = the way the joint folds.</summary>
        public static void RegisterHinge(Transform lower, Vector3 hingeLocal) { if (lower != null) Hinges[lower] = hingeLocal; }
        public static void UnregisterHinge(Transform lower) { if (lower != null) Hinges.Remove(lower); }

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
            Quaternion endRot = endBone.rotation;     // QA-723: the foot / hand keeps its world orientation (flat foot, palm direction)
            Vector3 root = upper.position, joint = lower.position, end = endBone.position;
            Vector3 nj, ne;
            Solve(root, joint, end, target, pole, out nj, out ne);
            if (weight < 1f) { nj = Vector3.Lerp(joint, nj, weight); ne = Vector3.Lerp(end, ne, weight); }
            // upper bone swings so the joint lands on nj
            Quaternion r1 = Quaternion.FromToRotation(joint - root, nj - root);
            upper.rotation = r1 * upper.rotation;
            // v0.10.0 BODIES: roll the upper bone about its own axis so the hinge of the joint lies across the bending plane (the joint stays where it is, the lower bone is swung next)
            Vector3 hingeLocal; bool arm = false;
            if (AlignHinges && Hinges.TryGetValue(lower, out hingeLocal))
            {
                arm = true;
                Vector3 axis = nj - root, e = ne - nj;
                Vector3 want = Vector3.Cross(axis, e);
                float bend = want.magnitude / Mathf.Max(1e-5f, axis.magnitude * e.magnitude);
                if (bend > 0.06f && axis.sqrMagnitude > 1e-8f)
                {
                    axis.Normalize(); want.Normalize();
                    Vector3 cur = lower.rotation * hingeLocal;
                    Vector3 cp = Vector3.ProjectOnPlane(cur, axis), wp = Vector3.ProjectOnPlane(want, axis);
                    if (cp.sqrMagnitude > 1e-6f && wp.sqrMagnitude > 1e-6f)
                    {
                        float a = Vector3.SignedAngle(cp, wp, axis);
                        if (Mathf.Abs(a) > 90f) a = Mathf.Sign(a) * 90f;      // never a half turn: a bent arm that would need one is a pole the animation never meant
                        upper.rotation = Quaternion.AngleAxis(a * Mathf.Clamp01(bend * 6f), axis) * upper.rotation;
                    }
                }
            }
            // the lower bone follows (its world position moved with the parent); swing it so the end lands on ne
            joint = lower.position; end = endBone.position;
            Quaternion r2 = Quaternion.FromToRotation(end - joint, ne - joint);
            lower.rotation = r2 * lower.rotation;
            endBone.rotation = Quaternion.Slerp(endBone.rotation, endRot, weight * (arm ? HandKeepWorld : 1f));
        }

        /// <summary>Total reach of a limb.</summary>
        public static float Reach(Vector3 root, Vector3 joint, Vector3 end) { return Vector3.Distance(root, joint) + Vector3.Distance(joint, end); }
    }
}
