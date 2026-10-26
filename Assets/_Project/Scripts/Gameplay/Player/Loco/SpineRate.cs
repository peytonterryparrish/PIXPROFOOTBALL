using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.13.0 NATURAL-3: angular speed cap of the trunk. A take switch (or a pose source that changes: matcher to stance layer, stance layer to a block clip) with a different torso puts the chest 30-60 degrees
    /// away from where it was one frame earlier; the inertializer hides the jump of the matcher's own switches, but the other pose sources do not run through it, and the chest turned at 2,000-10,000 deg/s for one
    /// frame (POP_CHEST_ROT_OVER_1400DEGS, 2.19 per play after the grown take database). The chest of the final pose (the last thing in LateUpdate) is allowed to turn at most <see cref="CapDegS"/> against the chest
    /// it showed the frame before: the excess is taken off the spine (the whole trunk above the pelvis turns about the lower spine, the chest keeps its pose against the spine, arms and head follow), so a pop
    /// becomes a two- or three-frame swing. No variety is cut, no pose changes except in those frames; nothing happens below the cap.
    /// </summary>
    public sealed class SpineRate
    {
        public static bool Enabled = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixSpineRateOff") < 0;
        /// <summary>Largest turn of the chest per second (degrees; the audit's pop line is 1400).</summary>
        public static float CapDegS = ArgF("-pixSpineRate", 1150f);
        static float ArgF(string name, float def) { var a = System.Environment.GetCommandLineArgs(); int i = System.Array.IndexOf(a, name); float v; return i >= 0 && i + 1 < a.Length && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : def; }

        readonly Transform _spine, _chest, _root; Quaternion _prev; Vector3 _prevRootPos; Quaternion _prevRootRot; bool _have;
        /// <summary>QA: frames in which the cap acted, and the largest turn (degrees in one frame) it had to take off.</summary>
        public int Capped; public float LargestCut;

        public SpineRate(Animator a, Transform root)
        {
            if (a == null || !a.isHuman) return;
            _spine = a.GetBoneTransform(HumanBodyBones.Spine);
            _chest = a.GetBoneTransform(HumanBodyBones.Chest) ?? a.GetBoneTransform(HumanBodyBones.UpperChest);
            _root = root;
        }

        /// <summary>The chest rotation after the cap: <paramref name="now"/> turned toward from <paramref name="prev"/> by at most <paramref name="maxDeg"/> degrees.</summary>
        public static Quaternion Limit(Quaternion prev, Quaternion now, float maxDeg) { return Quaternion.RotateTowards(prev, now, maxDeg); }

        public bool Ok { get { return _spine != null && _chest != null && _root != null; } }
        public void Reset() { _have = false; }

        /// <summary>LateUpdate, last. <paramref name="active"/> false (ragdoll, contact-driven physics, a body that was just placed): no cap, the state restarts.</summary>
        public void Late(float dt, bool active)
        {
            if (!Ok || !Enabled || dt <= 1e-5f) return;
            Quaternion now = _chest.rotation;
            Vector3 rp = _root.position; Quaternion rr = _root.rotation;
            // a body that was moved or turned as a whole between two frames (a reset, a placement, the facing loop snapping) is not a pop of the trunk
            if (!active || !_have || (rp - _prevRootPos).sqrMagnitude > 1f || Quaternion.Angle(rr, _prevRootRot) > 25f) { _prev = now; _prevRootPos = rp; _prevRootRot = rr; _have = active; return; }
            float max = CapDegS * dt, ang = Quaternion.Angle(_prev, now);
            if (ang > max)
            {
                Quaternion limited = Limit(_prev, now, max);
                Quaternion delta = limited * Quaternion.Inverse(now);
                _spine.rotation = delta * _spine.rotation;
                Capped++; if (ang - max > LargestCut) LargestCut = ang - max;
                now = _chest.rotation;
            }
            _prev = now; _prevRootPos = rp; _prevRootRot = rr;
        }
    }
}
