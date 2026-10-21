using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Cinemachine cameras for a match: a high sideline broadcast camera that always sits on the side that makes the offense drive left to
    /// right, follows the ball with a lead and frames the whole 11v11 formation; a behind-the-player chase camera (behind the quarterback
    /// looking downfield before the throw); and a helmet camera locked to the user-controlled player.
    /// </summary>
    public sealed class MatchCameraRig : MonoBehaviour
    {
        public Camera Main;
        public CinemachineBrain Brain;
        public CinemachineCamera Broadcast, Behind, FirstPerson, Close;
        Transform _closeAnchor;
        public CameraMode Mode { get; private set; } = CameraMode.Broadcast;
        public float MoveBasisYaw { get; private set; } = 270f;
        public PlayFrame Frame { get; private set; } = new PlayFrame(true);

        CinemachineFollow _bcFollow;
        Transform _bcTarget, _bcLook, _behindFollow, _behindLook, _fpAnchor;
        Vector3 _focus, _focusVel, _smoothFocus;
        SimAgentView _user;
        Vector3 _userVel;
        bool _qbBehind;
        float _behindYaw, _fpYaw;
        bool _inited;
        float _zoom = 1f;

        public static MatchCameraRig Create(Transform parent, Camera main)
        {
            var go = new GameObject("MatchCameraRig");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<MatchCameraRig>();
            rig.Init(main);
            return rig;
        }

        void Init(Camera main)
        {
            Main = main;
            Brain = Main.GetComponent<CinemachineBrain>();
            if (Brain == null) Brain = Main.gameObject.AddComponent<CinemachineBrain>();
            Brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.6f);
            Brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            Brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;

            _bcTarget = Proxy("BroadcastFollow"); _bcLook = Proxy("BroadcastLook");
            _behindFollow = Proxy("BehindFollow"); _behindLook = Proxy("BehindLook");
            _fpAnchor = Proxy("FirstPersonAnchor");

            Broadcast = MakeCam("CM_Broadcast", 27f);
            _bcFollow = Broadcast.gameObject.AddComponent<CinemachineFollow>();
            _bcFollow.FollowOffset = new Vector3(44f, 17f, 0f);
            _bcFollow.TrackerSettings = TrackerSettings.Default;
            _bcFollow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            _bcFollow.TrackerSettings.PositionDamping = new Vector3(0f, 0f, 0.9f);
            var brc = Broadcast.gameObject.AddComponent<CinemachineRotationComposer>();
            brc.Damping = new Vector2(0.5f, 0.6f);
            brc.Composition.ScreenPosition = new Vector2(0f, -0.02f);
            brc.Composition.DeadZone.Enabled = true; brc.Composition.DeadZone.Size = new Vector2(0.12f, 0.08f);
            Broadcast.Target.TrackingTarget = _bcTarget; Broadcast.Target.LookAtTarget = _bcLook;

            Behind = MakeCam("CM_Behind", 58f);
            var f2 = Behind.gameObject.AddComponent<CinemachineFollow>();
            f2.FollowOffset = new Vector3(0f, 3.0f, -7.2f);
            f2.TrackerSettings = TrackerSettings.Default;
            f2.TrackerSettings.BindingMode = BindingMode.LockToTargetWithWorldUp;
            f2.TrackerSettings.PositionDamping = new Vector3(0.25f, 0.25f, 0.18f);
            f2.TrackerSettings.RotationDamping = Vector3.zero;
            var rc2 = Behind.gameObject.AddComponent<CinemachineRotationComposer>();
            rc2.Damping = new Vector2(0.15f, 0.25f);
            Behind.Target.TrackingTarget = _behindFollow; Behind.Target.LookAtTarget = _behindLook;
            // keep the chase camera out of the goal posts (they carry simple colliders in the match scene)
            var deocc = Behind.gameObject.AddComponent<CinemachineDeoccluder>();
            deocc.CollideAgainst = 1; deocc.AvoidObstacles.Enabled = true; deocc.AvoidObstacles.CameraRadius = 0.25f; deocc.AvoidObstacles.DistanceLimit = 0f;

            FirstPerson = MakeCam("CM_FirstPerson", 76f);
            FirstPerson.gameObject.AddComponent<CinemachineHardLockToTarget>();
            FirstPerson.gameObject.AddComponent<CinemachineRotateWithFollowTarget>();
            FirstPerson.Target.TrackingTarget = _fpAnchor;
            FirstPerson.Lens.NearClipPlane = 0.08f;
            _closeAnchor = Proxy("CloseAnchor");
            Close = MakeCam("CM_Close", 32f);
            Close.gameObject.AddComponent<CinemachineHardLockToTarget>();
            Close.gameObject.AddComponent<CinemachineRotateWithFollowTarget>();
            Close.Target.TrackingTarget = _closeAnchor;
            Close.Priority.Value = 5;
            _inited = true;
            SetMode(PixSettings.DefaultCamera, true);
        }

        Transform Proxy(string n) { var t = new GameObject(n).transform; t.SetParent(transform, false); return t; }

        CinemachineCamera MakeCam(string n, float fov)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var cam = go.AddComponent<CinemachineCamera>();
            cam.Lens.FieldOfView = fov; cam.Lens.NearClipPlane = 0.2f; cam.Lens.FarClipPlane = 1200f;
            cam.Priority.Value = 10;
            return cam;
        }

        public void Cycle() { SetMode((CameraMode)(((int)Mode + 1) % 3), false); }

        public void SetMode(CameraMode m, bool instant)
        {
            Mode = m;
            Broadcast.Priority.Value = m == CameraMode.Broadcast ? 30 : 10;
            Behind.Priority.Value = m == CameraMode.Behind ? 30 : 10;
            FirstPerson.Priority.Value = m == CameraMode.FirstPerson ? 30 : 10;
            if (_user != null && _user.Avatar != null) _user.Avatar.HideHead(m == CameraMode.FirstPerson);
            if (instant) UpdateProxies(0.0167f, true);
        }

        /// <summary>Frame changes when the offense changes direction: the broadcast camera moves to the other sideline.</summary>
        public void SetFrame(PlayFrame frame, bool cut)
        {
            Frame = frame;
            float side = frame.TowardsPlusZ ? 1f : -1f;
            _bcFollow.FollowOffset = new Vector3(44f * side, 17f, 0f);
            if (cut) { Broadcast.PreviousStateIsValid = false; }
        }

        public void SetUser(SimAgentView user, bool qbBehind)
        {
            if (_user != null && _user != user && _user.Avatar != null) _user.Avatar.HideHead(false);
            _user = user; _qbBehind = qbBehind;
            if (_user != null && _user.Avatar != null) _user.Avatar.HideHead(Mode == CameraMode.FirstPerson);
        }

        /// <summary>Debug / replay helper (autotest close-ups): a tight camera looking at a point from a world-space offset. Pass null to go back to the normal camera.</summary>
        public void CloseUp(Vector3? target, Vector3 offset)
        {
            if (!target.HasValue) { Close.Priority.Value = 5; Brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.6f); return; }
            Brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            Vector3 t = target.Value;
            _closeAnchor.SetPositionAndRotation(t + offset, Quaternion.LookRotation((t + Vector3.up * 1.0f) - (t + offset), Vector3.up));
            Close.PreviousStateIsValid = false;
            Close.Priority.Value = 50;
        }

        // ------------------------------------------------------------------------------------------ v0.5 broadcast hand-over
        /// <summary>True while the broadcast layer drives the main camera itself (replays, establishing shots, post-play cuts): the Cinemachine brain is off and the live cameras keep tracking underneath.</summary>
        public bool Overridden { get; private set; }

        /// <summary>Hands the main camera to the broadcast layer (on) or takes it back (off): the live cameras jump to the current focus so the cut back never shows a swing.</summary>
        public void SetOverride(bool on)
        {
            if (Overridden == on) return;
            Overridden = on;
            if (Brain != null) Brain.enabled = !on;
            if (!on)
            {
                Broadcast.PreviousStateIsValid = false; Behind.PreviousStateIsValid = false; FirstPerson.PreviousStateIsValid = false; Close.PreviousStateIsValid = false;
                UpdateProxies(0.0167f, true);
                if (Brain != null) Brain.ManualUpdate();
            }
        }

        // ---- first-person polish: look-to-ball assist and stride head bob
        bool _hasAssist; Vector3 _assistPoint; float _assistWeight, _assistWeightS, _fpPitch, _bobPhase;
        /// <summary>The first-person camera leans toward a point of interest (the ball in the air, the carrier) inside a wide cone; weight 0..1.</summary>
        public void SetLookAssist(bool on, Vector3 point, float weight) { _hasAssist = on; _assistPoint = point; _assistWeight = on ? Mathf.Clamp01(weight) : 0f; }

        public void SetFocus(Vector3 point, Vector3 velocity) { _focus = point; _focusVel = velocity; }
        public void SetUserVelocity(Vector3 v) { _userVel = v; }

        /// <summary>Jumps every camera to the current focus (new play).</summary>
        public void Cut()
        {
            _smoothFocus = _focus;
            Broadcast.PreviousStateIsValid = false; Behind.PreviousStateIsValid = false;
            UpdateProxies(0.0167f, true);
        }

        void LateUpdate() { if (_inited) UpdateProxies(Time.deltaTime, false); }

        void UpdateProxies(float dt, bool snap)
        {
            // ---- broadcast
            _smoothFocus = snap ? _focus : Vector3.Lerp(_smoothFocus, _focus, 1f - Mathf.Exp(-dt * 7f));
            Vector3 lead = new Vector3(_focusVel.x, 0f, _focusVel.z) * 0.55f;
            float z = Mathf.Clamp(_smoothFocus.z + lead.z, -47f, 47f);
            _bcTarget.position = new Vector3(0f, 0f, z);
            _bcLook.position = new Vector3(Mathf.Clamp(_smoothFocus.x * 0.55f, -14f, 14f), 1.0f, _smoothFocus.z + lead.z * 0.85f);

            // ---- behind / first person
            if (_user != null)
            {
                Vector3 p = _user.transform.position;
                float speed = _userVel.magnitude;
                float targetYaw;
                if (_qbBehind) targetYaw = Frame.DownfieldYaw;
                else targetYaw = speed > 1.5f ? Mathf.Atan2(_userVel.x, _userVel.z) * Mathf.Rad2Deg : _user.Yaw;
                if (snap) _behindYaw = targetYaw;
                _behindYaw = Mathf.MoveTowardsAngle(_behindYaw, targetYaw, (40f + 60f * Mathf.Clamp01(speed / 8f)) * dt);
                _behindFollow.SetPositionAndRotation(p, Quaternion.Euler(0f, _behindYaw, 0f));
                Vector3 bf = Quaternion.Euler(0f, _behindYaw, 0f) * Vector3.forward;
                _behindLook.position = p + new Vector3(0f, 1.35f, 0f) + bf * 3f;

                Transform head = _user.Avatar != null ? _user.Avatar.Head : null;
                Vector3 hp = head != null ? head.position : p + Vector3.up * 1.7f;
                if (snap) _fpYaw = _behindYaw;
                // look-to-ball assist: inside a wide cone the view leans toward the ball / carrier (more when the player is not sprinting), and tilts up for a pass in the air
                float wantYaw = _behindYaw, wantPitch = 0f;
                _assistWeightS = Mathf.MoveTowards(_assistWeightS, _assistWeight, dt * 1.6f);
                if (_hasAssist && _assistWeightS > 0.01f)
                {
                    Vector3 to = _assistPoint - hp; Vector3 flat = new Vector3(to.x, 0f, to.z);
                    if (flat.sqrMagnitude > 2.25f)
                    {
                        float aYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                        float diff = Mathf.DeltaAngle(_behindYaw, aYaw);
                        float cone = Mathf.Clamp01(1.25f - Mathf.Abs(diff) / 95f);
                        float w = _assistWeightS * cone * (1f - 0.35f * Mathf.Clamp01(speed / 8f));
                        wantYaw = _behindYaw + diff * w;
                        wantPitch = Mathf.Clamp(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg * w, -26f, 14f);   // negative pitch looks up
                    }
                }
                _fpYaw = Mathf.MoveTowardsAngle(_fpYaw, wantYaw, 220f * dt);
                _fpPitch = snap ? wantPitch : Mathf.Lerp(_fpPitch, wantPitch, 1f - Mathf.Exp(-dt * 6f));
                // stride head bob: the phase advances with the distance run (about 1.9 m per stride), tiny amplitude that grows with speed
                float bobAmp = Mathf.Clamp01(speed / 6.5f);
                if (speed > 0.6f) _bobPhase += speed * dt / 1.9f * Mathf.PI * 2f;
                Quaternion fyaw = Quaternion.Euler(0f, _fpYaw, 0f);
                Vector3 fl = fyaw * Vector3.forward, fr = fyaw * Vector3.right;
                Vector3 bob = Vector3.up * (Mathf.Sin(_bobPhase * 2f) * 0.016f * bobAmp) + fr * (Mathf.Sin(_bobPhase) * 0.011f * bobAmp);
                float roll = Mathf.Sin(_bobPhase) * 0.55f * bobAmp;
                _fpAnchor.SetPositionAndRotation(new Vector3(hp.x, hp.y + 0.03f, hp.z) + fl * 0.12f + bob, Quaternion.Euler(_fpPitch, _fpYaw, roll));
            }

            switch (Mode)
            {
                case CameraMode.Broadcast: MoveBasisYaw = Frame.TowardsPlusZ ? 270f : 90f; break;
                default: MoveBasisYaw = _behindYaw; break;
            }
        }
    }
}
