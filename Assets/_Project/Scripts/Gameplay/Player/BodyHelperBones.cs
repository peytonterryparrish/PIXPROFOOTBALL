using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 BODIES: drives the four helper bones of a photoreal body (hip x2, shoulder x2; created by the model import step, see PhotoArmProportions). Each helper sits at the joint of the bone it follows, under that bone's parent, and takes
    /// half of that bone's rotation. The pants / jersey vertices in the blend between pelvis and thigh, and between chest and arm, are partly weighted to it, so at a deep hip flexion or with the arm overhead they turn by half of the joint instead of
    /// being pulled between two bones (the ballooning seat, the stretched armpit). Cost: four quaternion products per body per frame; skinning stays on the GPU. Runs after every other body layer (order 110) and before the ball is placed (150).
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class BodyHelperBones : MonoBehaviour
    {
        static readonly string[] Names = { "rig:LeftUpLegHelper", "rig:RightUpLegHelper", "rig:LeftArmHelper", "rig:RightArmHelper" };
        static readonly string[] Driven = { "rig:LeftUpLeg", "rig:RightUpLeg", "rig:LeftArm", "rig:RightArm" };
        /// <summary>Share of the driven bone's rotation the helper takes.</summary>
        public static float Share = 0.5f;
        /// <summary>Off since the weight relaxation (PhotoArmProportions.SmoothTransition) measured better than the helpers: no vertex is weighted to the helper bones now (HelperPeak 0); the bones and this driver stay so a corrective weighting can be switched on without a rebuild of the rig.</summary>
        public static bool Enabled = false;

        Transform[] _helper, _driven; Quaternion[] _helperBind, _drivenBind; int _n;

        void Awake()
        {
            var all = GetComponentsInChildren<Transform>(true);
            _helper = new Transform[Names.Length]; _driven = new Transform[Names.Length]; _helperBind = new Quaternion[Names.Length]; _drivenBind = new Quaternion[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                Transform h = null, d = null;
                foreach (var t in all) { if (t.name == Names[i]) h = t; else if (t.name == Driven[i]) d = t; }
                if (h == null || d == null) continue;
                _helper[_n] = h; _driven[_n] = d; _helperBind[_n] = h.localRotation; _drivenBind[_n] = d.localRotation; _n++;
            }
        }

        void LateUpdate()
        {
            if (!Enabled) return;
            for (int i = 0; i < _n; i++)
            {
                Quaternion delta = Quaternion.Inverse(_drivenBind[i]) * _driven[i].localRotation;
                _helper[i].localRotation = _helperBind[i] * Quaternion.Slerp(Quaternion.identity, delta, Share);
            }
        }

        /// <summary>QA: number of helpers found.</summary>
        public int Count { get { return _n; } }
    }
}
