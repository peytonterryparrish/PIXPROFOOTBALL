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
    [DefaultExecutionOrder(-200)]
    public sealed partial class MatchCameraRig : MonoBehaviour
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
        // v0.6 user camera (UserCameraMath): the director hands over what to look at, the rig smooths the pose
        UserCameraInput _view; bool _hasView, _poseInit; Vector3 _sPos, _sLook; float _sFov = 40f, _viewYaw, _viewYawS;   // _viewYawS: v0.9.0 CONNECT E-392 smoothed view yaw
        public CameraStyle Style { get { return PixSettings.UserCameraStyle; } }
        /// <summary>Latest target pose of the user camera (tests and the camera gallery read it).</summary>
        public UserCameraPose LastPose { get; private set; }
        float _prefsT; UserCameraPreset _preset; float _trimH, _trimD, _trimF, _zoomOut = 1f;

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
            // v0.9.0 CONNECT E-400: pressing View between the sideline broadcast camera (44 m out) and a player camera (Classic / Madden / Wide / first person) is a straight cut, not a 0.6 s
            // blend through the middle of the field; the player cameras blend among themselves as before.
            var cut = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            var blends = ScriptableObject.CreateInstance<CinemachineBlenderSettings>();
            blends.name = "PixViewCuts";
            blends.CustomBlends = new[]
            {
                new CinemachineBlenderSettings.CustomBlend { From = "CM_Broadcast", To = "CM_Behind", Blend = cut },
                new CinemachineBlenderSettings.CustomBlend { From = "CM_Behind", To = "CM_Broadcast", Blend = cut },
                new CinemachineBlenderSettings.CustomBlend { From = "CM_Broadcast", To = "CM_FirstPerson", Blend = cut },
                new CinemachineBlenderSettings.CustomBlend { From = "CM_FirstPerson", To = "CM_Broadcast", Blend = cut },
            };
            Brain.CustomBlends = blends;
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

            // v0.6: the user camera is driven directly (UserCameraMath): behind the offense / defense, no procedural components
            Behind = MakeCam("CM_Behind", 40f);

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

        /// <summary>View button: Classic -> Madden -> Wide -> first person -> broadcast (last, so a stray press never lands on the TV camera) -> Classic.</summary>
        public void Cycle()
        {
            if (Mode == CameraMode.Behind)
            {
                var s = PixSettings.UserCameraStyle;
                if (s != CameraStyle.Wide) { PixSettings.UserCameraStyle = (CameraStyle)((int)s + 1); _prefsT = 0f; }
                else SetMode(CameraMode.FirstPerson, false);
            }
            else if (Mode == CameraMode.FirstPerson) SetMode(CameraMode.Broadcast, false);
            else { PixSettings.UserCameraStyle = CameraStyle.Classic; _prefsT = 0f; SetMode(CameraMode.Behind, false); }
        }

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
            float side = CameraSide(frame);
            _bcFollow.FollowOffset = new Vector3(44f * side, 17f, 0f);
            if (cut && !SmoothBetweenPlays) { Broadcast.PreviousStateIsValid = false; }
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
        /// <summary>v0.6: what the behind-the-offense / behind-the-defense camera frames this frame (the director calls it every frame).</summary>
        public void SetUserView(in UserCameraInput c) { _view = c; _hasView = true; }


        /// <summary>Jumps every camera to the current focus (new play).</summary>
        public void Cut()
        {
            _smoothFocus = _focus; _poseInit = false;
            if (!SmoothBetweenPlays) Broadcast.PreviousStateIsValid = false; Behind.PreviousStateIsValid = false;
            UpdateProxies(0.0167f, !SmoothBetweenPlays);
        }

        void LateUpdate() { if (_inited) UpdateProxies(Time.deltaTime, false); }

        void UpdateProxies(float dt, bool snap)
        {
            // ---- broadcast
            // v0.9.0 CONNECT E-398: the broadcast camera stacked three smoothers (this lerp, the Cinemachine position damping and the composer's rotation damping); the lerp is gone, the
            // Cinemachine damping (0.9 s on z, 0.5 / 0.6 s rotation) does the smoothing. The z clamp moves from 47 to 52 m and the lens widens a little past 47 m so the end zones stay in shot.
            BroadcastStep(dt, snap);   // v0.11 WATCH: MatchCameraRig.Broadcast.cs

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
                // v0.9.0 CONNECT E-401: the first-person yaw is driven from the intended heading (targetYaw: the stick / downfield) instead of the slow _behindYaw (40 to 100 deg/s), so after a sharp
                // cut the view is no longer 0.5 to 1 s behind the body; a head-turn limit keeps it within 110 degrees of the body's own yaw, and the look assist below only leans toward the ball.
                float fpBase = targetYaw;
                float wantYaw = fpBase, wantPitch = 0f;
                _assistWeightS = Mathf.MoveTowards(_assistWeightS, _assistWeight, dt * 1.6f);
                if (_hasAssist && _assistWeightS > 0.01f)
                {
                    Vector3 to = _assistPoint - hp; Vector3 flat = new Vector3(to.x, 0f, to.z);
                    if (flat.sqrMagnitude > 2.25f)
                    {
                        float aYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                        float diff = Mathf.DeltaAngle(fpBase, aYaw);
                        float cone = Mathf.Clamp01(1.25f - Mathf.Abs(diff) / 95f);
                        float w = _assistWeightS * cone * (1f - 0.35f * Mathf.Clamp01(speed / 8f));
                        wantYaw = fpBase + diff * w;
                        wantPitch = Mathf.Clamp(-Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg * w, -26f, 14f);   // negative pitch looks up
                    }
                }
                wantYaw = _user.Yaw + Mathf.Clamp(Mathf.DeltaAngle(_user.Yaw, wantYaw), -110f, 110f);   // E-401: head-turn limit relative to the body
                _fpYaw = Mathf.MoveTowardsAngle(_fpYaw, wantYaw, 330f * dt);
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

            // ---- v0.6 user camera: behind the offense / defense (Classic, Madden, Wide), driven from UserCameraMath with smoothing
            if (_hasView)
            {
                // the settings change rarely: read the PlayerPrefs twice a second, not every frame (QA-682)
                _prefsT -= dt;
                if (_prefsT <= 0f || snap) { _prefsT = 0.5f; _preset = UserCameraPreset.Of(PixSettings.UserCameraStyle); _trimH = PixSettings.CameraHeightTrim; _trimD = PixSettings.CameraDistanceTrim; _trimF = PixSettings.CameraFovTrim; _zoomOut = PixSettings.CameraZoomOut; }
                var preset = _preset;
                // v0.9.0 CONNECT E-392: the view direction turns toward its target at a limited rate (a turnover return flips it 180 degrees) instead of jumping; a new play (Cut) snaps it
                var vin = _view;
                float targetViewYaw = Mathf.Atan2(_view.ViewDir.x, _view.ViewDir.z) * Mathf.Rad2Deg;
                if (snap || !_poseInit) _viewYawS = targetViewYaw;
                else _viewYawS = Mathf.MoveTowardsAngle(_viewYawS, targetViewYaw, 190f * dt);
                vin.ViewDir = Quaternion.Euler(0f, _viewYawS, 0f) * Vector3.forward;
                var pose = UserCameraMath.Evaluate(vin, preset, _trimH, _trimD, _trimF, _zoomOut);
                LastPose = pose;
                _viewYaw = _viewYawS;
                if (snap || !_poseInit) { _sPos = pose.Position; _sLook = pose.LookAt; _sFov = pose.Fov; _poseInit = true; }
                else
                {
                    float follow = _view.Follow ? 0.22f : 0.34f;
                    // v0.9.0 CONNECT E-390: the camera height and look height follow a rising ball quickly (a 0.55 s lag lost the ball off the top of the frame)
                    float vTau = _view.BallAirborne ? 0.28f : 0.55f;
                    // along the view direction the camera lags a little (smooth, weighty); sideways it tracks tighter
                    Vector3 d = new Vector3(vin.ViewDir.x, 0f, vin.ViewDir.z).normalized, side = Vector3.Cross(Vector3.up, d);
                    Vector3 dp = pose.Position - _sPos; dp.y = 0f;
                    float along = Vector3.Dot(dp, d), lat = Vector3.Dot(dp, side);
                    Vector3 step = d * (along * UserCameraMath.Alpha(dt, follow)) + side * (lat * UserCameraMath.Alpha(dt, follow * 0.7f));
                    _sPos += step; _sPos.y = Mathf.Lerp(_sPos.y, pose.Position.y, UserCameraMath.Alpha(dt, vTau));
                    Vector3 dl = pose.LookAt - _sLook; dl.y = 0f;
                    float la = Vector3.Dot(dl, d), ll = Vector3.Dot(dl, side);
                    _sLook += d * (la * UserCameraMath.Alpha(dt, follow)) + side * (ll * UserCameraMath.Alpha(dt, follow * 0.7f)); _sLook.y = Mathf.Lerp(_sLook.y, pose.LookAt.y, UserCameraMath.Alpha(dt, vTau));
                    _sFov = Mathf.Lerp(_sFov, pose.Fov, UserCameraMath.Alpha(dt, 0.6f));
                }
                Vector3 fwd = _sLook - _sPos;
                if (fwd.sqrMagnitude > 1e-4f) Behind.transform.SetPositionAndRotation(_sPos, Quaternion.LookRotation(fwd.normalized, Vector3.up));
                var lens = Behind.Lens; lens.FieldOfView = _sFov; Behind.Lens = lens;
            }

            switch (Mode)
            {
                case CameraMode.Broadcast: MoveBasisYaw = Frame.TowardsPlusZ ? 270f : 90f; break;
                case CameraMode.Behind: MoveBasisYaw = _hasView ? _viewYaw : _behindYaw; break;
                default: MoveBasisYaw = _behindYaw; break;
            }
        }
    }
}
