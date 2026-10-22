using System;
using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 LOCOMOTION (LOCO-002): the speed-banded cycle library. Every entry is one mocap gait cycle (a window of a looping clip that starts on the left foot's plant) together with what was MEASURED on it
    /// by <c>LocoCycleBuilder</c>: the ground speed that makes its planted foot stand still (native speed, NOT the root-motion speed that several takes overstate by 10-50 percent), the direction of that travel,
    /// the stride, and the foot-contact markers (normalised gait phase of the left plant = 0, left lift-off, right plant, right lift-off) used to keep blended cycles in step.
    /// Lives in Resources (licensed art, git-ignored); gameplay falls back to the animator blend tree when it is missing.
    /// </summary>
    public sealed class LocoCycleLibrary : ScriptableObject
    {
        public const string ResourcesName = "PixLocoCycles";
        public const string EditorPath = "Assets/_Licensed/Resources/PixLocoCycles.asset";

        [Serializable]
        public sealed class Cycle
        {
            public string name;
            public AnimationClip clip;
            /// <summary>Window of the clip in seconds: the cycle starts on the left foot plant and lasts <see cref="length"/>; times past the clip end wrap (all entries are seamless loops).</summary>
            public float tStart, length;
            /// <summary>Native ground speed (m/s, reference leg length) and its direction in the body frame (x right, z forward; unit vector).</summary>
            public float speed;
            public Vector2 dir = Vector2.up;
            /// <summary>Contact markers in cycle phase: left plant is 0.</summary>
            public float lLift, rPlant, rLift;
            public float stride;
            public bool carry;
            /// <summary>Name of the free-arm cycle whose arms replace this take's ball-carrier arms (same gait phase): the jog and the back-pedal exist only as ball-carrier takes.</summary>
            public string donor;
            [NonSerialized] public Cycle donorRef;
            /// <summary>Hips heading of the take relative to the world frame the cycle was measured in (degrees): the body must be turned by -this to face along travel.</summary>
            public float heading;
            public float soleMinL, soleMinR, hipsMean;
            /// <summary>Angle of the native travel direction (degrees, + = to the right).</summary>
            public float Angle { get { return Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg; } }
        }

        public List<Cycle> cycles = new List<Cycle>();
        /// <summary>Hip-to-ankle length of the rig the cycles were measured on (m): native speeds scale with the leg length of the body that plays them.</summary>
        public float refLegLength = 0.8535f;
        /// <summary>Native-speed scale of the Small / Medium / Large bodies relative to the Medium body the cycles were measured on (measured on the same takes), and their leg lengths.</summary>
        public float[] sizeScale = { 1f, 1f, 1f };
        public float[] legLength = { 0f, 0f, 0f };

        static LocoCycleLibrary _inst; static bool _tried;
        public static LocoCycleLibrary Load()
        {
            if (_inst != null) return _inst;
            if (_tried && !Application.isEditor) return null;
            _tried = true;
#if UNITY_EDITOR
            _inst = UnityEditor.AssetDatabase.LoadAssetAtPath<LocoCycleLibrary>(EditorPath);
#endif
            if (_inst == null) _inst = Resources.Load<LocoCycleLibrary>(ResourcesName);
            return _inst;
        }
        public static void ResetCache() { _inst = null; _tried = false; }
        public static void Set(LocoCycleLibrary lib) { _inst = lib; _tried = true; }
    }
}
