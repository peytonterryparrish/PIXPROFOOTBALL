using System.Collections.Generic;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Active-ragdoll for one player: Humanoid-bone rigid bodies, capsule / box colliders and character joints, built lazily from the avatar's bind pose the first time the player takes
    /// a hit big enough to need one. The animation hands the pose to the physics over 0.12 s (the animated pose is blended in LateUpdate and the bodies are pulled to it), the bodies
    /// simulate for 0.4 to 1.2 s with capped velocities on the ground collider, then the settled pose is blended into a get-up take (the director chooses it by the face up / down state).
    /// Rigid bodies sit on their own layer that only collides with the ground plane, so ragdolls never explode into each other or the stadium.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class RagdollRig : MonoBehaviour
    {
        public const int RagdollLayer = 20, GroundLayer = 21;
        public const float MaxBodySpeed = 9f, MaxAngularSpeed = 18f;
        static bool _layersReady; static GameObject _ground; static PhysicsMaterial _mat;

        struct Part { public HumanBodyBones Id; public Transform T; public Rigidbody Rb; public Collider Col; public int Layer; public float Mass; }

        readonly List<Part> _parts = new List<Part>();
        Transform[] _all; Vector3[] _bindPos; Quaternion[] _bindRot;
        Quaternion[] _snapRot; Vector3 _snapHipsPos; Quaternion _snapHipsRot;
        Animator _anim; Transform _hips, _head, _chest;
        bool _built, _snapValid;
        public readonly RagdollStateMachine Machine = new RagdollStateMachine();
        /// <summary>Raised when the body has settled and a get-up take is needed; the argument is the face up state.</summary>
        public System.Action<bool> RecoverNeeded;

        public bool Built { get { return _built; } }
        public bool Active { get { return Machine.Active; } }
        public Transform Hips { get { return _hips; } }
        public bool Available { get { return _anim != null && _anim.isHuman; } }

        /// <summary>Called once when the avatar is spawned: remembers the bind pose (the joints are built in it so their limits are measured from the rest pose).</summary>
        public void Setup(Animator animator)
        {
            _anim = animator;
            if (animator == null || !animator.isHuman) return;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips); _head = animator.GetBoneTransform(HumanBodyBones.Head);
            _chest = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
            _all = animator.transform.GetComponentsInChildren<Transform>(true);
            _bindPos = new Vector3[_all.Length]; _bindRot = new Quaternion[_all.Length];
            for (int i = 0; i < _all.Length; i++) { _bindPos[i] = _all[i].localPosition; _bindRot[i] = _all[i].localRotation; }
        }

        static void EnsureWorld()
        {
            if (_layersReady) return;
            _layersReady = true;
            for (int l = 0; l < 32; l++) if (l != GroundLayer) Physics.IgnoreLayerCollision(RagdollLayer, l, true);
            Physics.IgnoreLayerCollision(RagdollLayer, GroundLayer, false);
            _mat = new PhysicsMaterial("PixRagdoll") { dynamicFriction = 0.7f, staticFriction = 0.8f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Maximum, bounceCombine = PhysicsMaterialCombine.Minimum };
        }

        static void EnsureGround()
        {
            if (_ground != null) return;
            _ground = new GameObject("RagdollGround") { layer = GroundLayer };
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            var b = _ground.AddComponent<BoxCollider>(); b.size = new Vector3(260f, 1f, 260f); b.sharedMaterial = _mat;
            Object.DontDestroyOnLoad(_ground);
        }

        // ------------------------------------------------------------------------------------------------------------ build
        void Add(HumanBodyBones id, HumanBodyBones child, float radius, float mass, bool box, Vector3 boxSizeWorld, Part? parent, HumanBodyBones? hinge, bool sphere = false)
        {
            var t = _anim.GetBoneTransform(id); if (t == null) return;
            var p = new Part { Id = id, T = t, Mass = mass, Layer = t.gameObject.layer };
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.mass = mass; rb.linearDamping = 0.2f; rb.angularDamping = 0.9f; rb.maxAngularVelocity = MaxAngularSpeed; rb.maxDepenetrationVelocity = 1.5f;
            rb.solverIterations = 12; rb.solverVelocityIterations = 4; rb.interpolation = RigidbodyInterpolation.None; rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.isKinematic = true;
            Collider col;
            if (sphere) { var s = t.gameObject.AddComponent<SphereCollider>(); s.radius = radius; s.center = t.InverseTransformVector(Vector3.up * radius * 0.7f); col = s; }
            else if (box)
            {
                var b = t.gameObject.AddComponent<BoxCollider>();
                Vector3 r = _right, u = Vector3.up, f = _fwd;
                Vector3 size = Vector3.zero;
                for (int i = 0; i < 3; i++)
                {
                    Vector3 axisW = t.TransformDirection(i == 0 ? Vector3.right : (i == 1 ? Vector3.up : Vector3.forward));
                    float dr = Mathf.Abs(Vector3.Dot(axisW, r)), du = Mathf.Abs(Vector3.Dot(axisW, u)), df = Mathf.Abs(Vector3.Dot(axisW, f));
                    float s = dr >= du && dr >= df ? boxSizeWorld.x : (du >= df ? boxSizeWorld.y : boxSizeWorld.z);
                    size[i] = s / Mathf.Max(0.0001f, t.lossyScale[i]);
                }
                b.size = size; b.center = t.InverseTransformVector(Vector3.up * boxSizeWorld.y * 0.15f);
                col = b;
            }
            else
            {
                var childT = _anim.GetBoneTransform(child);
                var c = t.gameObject.AddComponent<CapsuleCollider>();
                Vector3 off = childT != null ? t.InverseTransformPoint(childT.position) : t.InverseTransformVector(Vector3.down * 0.3f * _scale);
                int dir = 0; float m = Mathf.Abs(off.x); if (Mathf.Abs(off.y) > m) { dir = 1; m = Mathf.Abs(off.y); } if (Mathf.Abs(off.z) > m) { dir = 2; m = Mathf.Abs(off.z); }
                c.direction = dir; c.center = off * 0.5f; c.height = Mathf.Max(radius * 2.2f, off.magnitude * 1.08f); c.radius = radius;
                col = c;
            }
            col.sharedMaterial = _mat; col.enabled = false;
            p.Rb = rb; p.Col = col;
            if (parent.HasValue)
            {
                var j = t.gameObject.AddComponent<CharacterJoint>();
                j.connectedBody = parent.Value.Rb; j.enablePreprocessing = false; j.enableProjection = true; j.enableCollision = false;
                j.autoConfigureConnectedAnchor = true;
                ConfigureJoint(j, t, childOf(id), hinge);
            }
            _parts.Add(p);
        }

        HumanBodyBones? childOf(HumanBodyBones id)
        {
            switch (id)
            {
                case HumanBodyBones.LeftUpperArm: return HumanBodyBones.LeftLowerArm; case HumanBodyBones.RightUpperArm: return HumanBodyBones.RightLowerArm;
                case HumanBodyBones.LeftLowerArm: return HumanBodyBones.LeftHand; case HumanBodyBones.RightLowerArm: return HumanBodyBones.RightHand;
                case HumanBodyBones.LeftUpperLeg: return HumanBodyBones.LeftLowerLeg; case HumanBodyBones.RightUpperLeg: return HumanBodyBones.RightLowerLeg;
                case HumanBodyBones.LeftLowerLeg: return HumanBodyBones.LeftFoot; case HumanBodyBones.RightLowerLeg: return HumanBodyBones.RightFoot;
            }
            return null;
        }

        Vector3 _right = Vector3.right, _fwd = Vector3.forward; float _scale = 1f;

        void ConfigureJoint(CharacterJoint j, Transform t, HumanBodyBones? child, HumanBodyBones? hinge)
        {
            SoftJointLimit lim = new SoftJointLimit();
            Vector3 boneDir = Vector3.down;
            if (child.HasValue) { var c = _anim.GetBoneTransform(child.Value); if (c != null) boneDir = (c.position - t.position).normalized; }
            if (hinge.HasValue)
            {
                // elbow / knee: a hinge. Its axis is perpendicular to the limb and to the direction the joint folds toward (arms fold forward, shins fold back)
                bool knee = hinge.Value == HumanBodyBones.LeftLowerLeg || hinge.Value == HumanBodyBones.RightLowerLeg;
                Vector3 fold = knee ? -_fwd : _fwd;
                Vector3 axisW = Vector3.Cross(boneDir, fold).normalized;
                // joint rotation by a NEGATIVE angle about the axis must fold the limb: check numerically
                Vector3 test = Quaternion.AngleAxis(-30f, axisW) * boneDir;
                if (Vector3.Dot(test, fold) < Vector3.Dot(boneDir, fold)) axisW = -axisW;
                j.axis = t.InverseTransformDirection(axisW);
                Vector3 sw = Vector3.Cross(axisW, boneDir); if (sw.sqrMagnitude < 1e-4f) sw = Vector3.up;
                j.swingAxis = t.InverseTransformDirection(sw.normalized);
                lim.limit = -150f; j.lowTwistLimit = lim; lim.limit = 6f; j.highTwistLimit = lim;
                lim.limit = 6f; j.swing1Limit = lim; lim.limit = 6f; j.swing2Limit = lim;
            }
            else
            {
                // ball joints: twist about the limb, swing about the body side axis / the other perpendicular
                j.axis = t.InverseTransformDirection(boneDir.sqrMagnitude > 0.1f ? boneDir : Vector3.down);
                Vector3 sw = Vector3.Cross(boneDir, _right); if (sw.sqrMagnitude < 1e-4f) sw = _fwd;
                j.swingAxis = t.InverseTransformDirection(sw.normalized);
                bool torso = t == _chest || t == _head;
                lim.limit = torso ? -35f : -50f; j.lowTwistLimit = lim; lim.limit = torso ? 35f : 50f; j.highTwistLimit = lim;
                lim.limit = torso ? 35f : 100f; j.swing1Limit = lim; lim.limit = torso ? 30f : 70f; j.swing2Limit = lim;
            }
        }

        void Build()
        {
            if (_built || !Available) return;
            EnsureWorld(); EnsureGround();
            // build in the bind pose: the joints take their zero position (and limits) from the pose they are created in
            var cur = new Quaternion[_all.Length]; var curP = new Vector3[_all.Length];
            for (int i = 0; i < _all.Length; i++) { cur[i] = _all[i].localRotation; curP[i] = _all[i].localPosition; _all[i].localRotation = _bindRot[i]; _all[i].localPosition = _bindPos[i]; }
            var lu = _anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var ru = _anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (lu != null && ru != null)
            {
                Vector3 r = ru.position - lu.position; r.y = 0f;
                if (r.sqrMagnitude > 1e-4f) { _right = r.normalized; _fwd = Vector3.Cross(_right, Vector3.up).normalized; }
            }
            _scale = _head != null && _hips != null ? Mathf.Clamp(Vector3.Distance(_head.position, _hips.position) / 0.62f, 0.7f, 1.4f) : 1f;
            float s = _scale;
            Part hipsP; Part? chestP = null;
            // pelvis
            Add(HumanBodyBones.Hips, HumanBodyBones.Spine, 0f, 14f, true, new Vector3(0.34f, 0.22f, 0.22f) * s, null, null);
            hipsP = _parts[0];
            var chestId = _anim.GetBoneTransform(HumanBodyBones.Chest) != null ? HumanBodyBones.Chest : HumanBodyBones.Spine;
            Add(chestId, HumanBodyBones.Head, 0f, 26f, true, new Vector3(0.36f, 0.34f, 0.24f) * s, hipsP, null);
            if (_parts.Count > 1) chestP = _parts[1];
            Part anchor = chestP.HasValue ? chestP.Value : hipsP;
            Add(HumanBodyBones.Head, HumanBodyBones.Head, 0.115f * s, 5f, false, Vector3.zero, anchor, null, true);
            Add(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, 0.06f * s, 3.2f, false, Vector3.zero, anchor, null);
            Add(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, 0.05f * s, 2f, false, Vector3.zero, Find(HumanBodyBones.LeftUpperArm), HumanBodyBones.LeftLowerArm);
            Add(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, 0.06f * s, 3.2f, false, Vector3.zero, anchor, null);
            Add(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, 0.05f * s, 2f, false, Vector3.zero, Find(HumanBodyBones.RightUpperArm), HumanBodyBones.RightLowerArm);
            Add(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.09f * s, 9f, false, Vector3.zero, hipsP, null);
            Add(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.065f * s, 5f, false, Vector3.zero, Find(HumanBodyBones.LeftUpperLeg), HumanBodyBones.LeftLowerLeg);
            Add(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.09f * s, 9f, false, Vector3.zero, hipsP, null);
            Add(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.065f * s, 5f, false, Vector3.zero, Find(HumanBodyBones.RightUpperLeg), HumanBodyBones.RightLowerLeg);
            for (int i = 0; i < _all.Length; i++) { _all[i].localRotation = cur[i]; _all[i].localPosition = curP[i]; }
            _snapRot = new Quaternion[_all.Length];
            _built = true;
        }

        Part? Find(HumanBodyBones id) { foreach (var p in _parts) if (p.Id == id) return p; return null; }

        // ------------------------------------------------------------------------------------------------------------ control
        /// <summary>
        /// Hands the body to the physics. velocity: the body's ground velocity (m/s); impulse: an extra velocity change (m/s) applied at the chest (the hit); intensity 0..1 decides how
        /// long the body is left to the physics. Returns false when the rig is unavailable or already active.
        /// </summary>
        public bool Begin(Vector3 velocity, Vector3 impulse, float intensity)
        {
            if (!Available || Machine.Active) return false;
            if (!_built) Build();
            if (!_built) return false;
            Machine.Trigger(intensity);
            _snapValid = false;
            foreach (var p in _parts)
            {
                p.T.gameObject.layer = RagdollLayer;
                p.Col.enabled = true;
                p.Rb.isKinematic = false;
                p.Rb.position = p.T.position; p.Rb.rotation = p.T.rotation;
                Vector3 v = velocity;
                if (p.T == _chest) v += impulse;
                else v += impulse * 0.35f;
                p.Rb.linearVelocity = RagdollStateMachine.CapVelocity(v, MaxBodySpeed);
                p.Rb.angularVelocity = Vector3.zero;
            }
            return true;
        }

        /// <summary>Back to the animation immediately.</summary>
        public void Cancel()
        {
            if (!_built) return;
            Release();
            Machine.Cancel();
        }

        void Release()
        {
            foreach (var p in _parts)
            {
                if (p.Rb == null) continue;
                p.Rb.isKinematic = true; p.Col.enabled = false; p.T.gameObject.layer = p.Layer;
            }
        }

        void FixedUpdate()
        {
            if (!Machine.Active || Machine.Phase == RagdollPhase.Recover) return;
            foreach (var p in _parts)
            {
                if (p.Rb == null || p.Rb.isKinematic) continue;
                p.Rb.linearVelocity = RagdollStateMachine.CapVelocity(p.Rb.linearVelocity, MaxBodySpeed);
                // never let a body sink under the grass
                var pos = p.Rb.position;
                if (pos.y < 0.03f) { pos.y = 0.03f; p.Rb.position = pos; var v = p.Rb.linearVelocity; if (v.y < 0f) { v.y = 0f; p.Rb.linearVelocity = v; } }
            }
        }

        float MaxSpeed()
        {
            float m = 0f;
            foreach (var p in _parts) if (p.Rb != null && !p.Rb.isKinematic) m = Mathf.Max(m, p.Rb.linearVelocity.magnitude);
            return m;
        }

        /// <summary>+1 lying face up, -1 face down (from the chest's front vector).</summary>
        public float FrontY()
        {
            if (_hips == null || _head == null) return 0f;
            var lu = _anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var ru = _anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (lu == null || ru == null) return 0f;
            Vector3 right = ru.position - lu.position, up = _head.position - _hips.position;
            return Vector3.Cross(right, up).normalized.y;
        }

        /// <summary>World pose of the pelvis on the ground: position (x, z) and the heading the body faces (degrees).</summary>
        public void PelvisPose(out Vector2 xz, out float yaw)
        {
            xz = _hips != null ? new Vector2(_hips.position.x, _hips.position.z) : Vector2.zero; yaw = 0f;
            var lu = _anim.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var ru = _anim.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (lu == null || ru == null) return;
            Vector3 r = ru.position - lu.position;
            if (new Vector2(r.x, r.z).magnitude > 0.1f) yaw = Mathf.Atan2(-r.z, r.x) * Mathf.Rad2Deg;
        }

        void Snapshot()
        {
            for (int i = 0; i < _all.Length; i++) _snapRot[i] = _all[i].localRotation;
            _snapHipsPos = _hips.position; _snapHipsRot = _hips.rotation; _snapValid = true;
        }

        // ------------------------------------------------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (!_built || !Machine.Active) return;
            float dt = Time.deltaTime;
            bool grounded = _hips != null && _hips.position.y < 0.42f;
            var before = Machine.Phase;
            Machine.Update(dt, MaxSpeed(), grounded);
            float w = Machine.Weight;
            switch (Machine.Phase)
            {
                case RagdollPhase.BlendIn:
                case RagdollPhase.Active:
                    foreach (var p in _parts)
                    {
                        Vector3 sp = p.Rb.position; Quaternion sr = p.Rb.rotation;
                        if (w >= 0.999f) p.T.SetPositionAndRotation(sp, sr);
                        else p.T.SetPositionAndRotation(Vector3.Lerp(p.T.position, sp, w), Quaternion.Slerp(p.T.rotation, sr, w));
                    }
                    break;
                case RagdollPhase.Recover:
                    if (before != RagdollPhase.Recover)
                    {
                        // the bodies stop; the settled pose is what the get-up take is blended from
                        foreach (var p in _parts) p.T.SetPositionAndRotation(p.Rb.position, p.Rb.rotation);
                        Snapshot();
                        bool faceUp = RagdollStateMachine.IsFaceUp(FrontY());
                        Release();
                        Machine.ConsumeRecoverRequest();
                        if (RecoverNeeded != null) RecoverNeeded(faceUp);
                    }
                    else if (_snapValid)
                    {
                        // the animator (now playing the get-up take) is blended over the settled pose
                        for (int i = 0; i < _all.Length; i++)
                        {
                            if (_all[i] == _hips) continue;
                            _all[i].localRotation = Quaternion.Slerp(_all[i].localRotation, _snapRot[i], w);
                        }
                        _hips.localRotation = Quaternion.Slerp(_hips.localRotation, _hips.parent != null ? Quaternion.Inverse(_hips.parent.rotation) * _snapHipsRot : _snapHipsRot, w);
                        _hips.position = Vector3.Lerp(_hips.position, _snapHipsPos, w);
                    }
                    break;
            }
            if (Machine.Phase == RagdollPhase.Animated) Release();
        }

        void OnDisable() { if (_built) { Release(); Machine.Cancel(); } }
    }
}
