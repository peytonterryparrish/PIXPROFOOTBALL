using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Cinemachine 3 camera set for free roam: high sideline broadcast camera (tracks the carrier with lead and damping),
    /// behind-the-player chase camera and a head-attached, stabilised first-person camera. Cycled with the View button.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public PlayerController Player;
        public Camera Main;
        public CinemachineBrain Brain;
        public CameraMode Mode { get; private set; } = CameraMode.Broadcast;

        /// <summary>Camera yaw (degrees) that the stick "up" direction maps to.</summary>
        public float MoveBasisYaw { get; private set; } = 90f;

        public CinemachineCamera Broadcast, Behind, FirstPerson;
        Transform _bcFollow, _bcLook, _behindFollow, _behindLook, _fpAnchor;
        float _behindYaw;
        Vector3 _fpPos;
        bool _inited;

        public static CameraRig Create(Transform parent, Camera main, PlayerController player)
        {
            var go = new GameObject("CameraRig");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<CameraRig>();
            rig.Init(main, player);
            return rig;
        }

        public void Init(Camera main, PlayerController player)
        {
            Main = main; Player = player;
            if (Player != null) Player.Cameras = this;
            Brain = Main.GetComponent<CinemachineBrain>();
            if (Brain == null) Brain = Main.gameObject.AddComponent<CinemachineBrain>();
            Brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 0.7f);
            Brain.UpdateMethod = CinemachineBrain.UpdateMethods.SmartUpdate;
            Brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;

            _bcFollow = Proxy("BroadcastFollow"); _bcLook = Proxy("BroadcastLook");
            _behindFollow = Proxy("BehindFollow"); _behindLook = Proxy("BehindLook");
            _fpAnchor = Proxy("FirstPersonAnchor");

            Broadcast = MakeCam("CM_Broadcast", 21f);
            var bf = Broadcast.gameObject.AddComponent<CinemachineFollow>();
            bf.FollowOffset = new Vector3(-42f, 15.5f, 0f);
            bf.TrackerSettings = TrackerSettings.Default;
            bf.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            bf.TrackerSettings.PositionDamping = new Vector3(0f, 0f, 1.1f);
            var brc = Broadcast.gameObject.AddComponent<CinemachineRotationComposer>();
            brc.Damping = new Vector2(0.55f, 0.65f);
            brc.Composition.ScreenPosition = new Vector2(0f, 0.05f);
            brc.Composition.DeadZone.Enabled = true; brc.Composition.DeadZone.Size = new Vector2(0.16f, 0.1f);
            Broadcast.Target.TrackingTarget = _bcFollow; Broadcast.Target.LookAtTarget = _bcLook;

            Behind = MakeCam("CM_Behind", 58f);
            var f2 = Behind.gameObject.AddComponent<CinemachineFollow>();
            f2.FollowOffset = new Vector3(0f, 2.7f, -6.4f);
            f2.TrackerSettings = TrackerSettings.Default;
            f2.TrackerSettings.BindingMode = BindingMode.LockToTargetWithWorldUp;
            f2.TrackerSettings.PositionDamping = new Vector3(0.25f, 0.25f, 0.18f);
            f2.TrackerSettings.RotationDamping = Vector3.zero;
            var rc2 = Behind.gameObject.AddComponent<CinemachineRotationComposer>();
            rc2.Damping = new Vector2(0.15f, 0.25f);
            Behind.Target.TrackingTarget = _behindFollow; Behind.Target.LookAtTarget = _behindLook;

            FirstPerson = MakeCam("CM_FirstPerson", 76f);
            FirstPerson.gameObject.AddComponent<CinemachineHardLockToTarget>();
            FirstPerson.gameObject.AddComponent<CinemachineRotateWithFollowTarget>();
            FirstPerson.Target.TrackingTarget = _fpAnchor;
            FirstPerson.Lens.NearClipPlane = 0.08f;

            _inited = true;
            SetMode(PixSettings.DefaultCamera, true);
        }

        Transform Proxy(string n) { var t = new GameObject(n).transform; t.SetParent(transform, false); return t; }

        CinemachineCamera MakeCam(string n, float fov)
        {
            var go = new GameObject(n);
            go.transform.SetParent(transform, false);
            var cam = go.AddComponent<CinemachineCamera>();
            cam.Lens.FieldOfView = fov;
            cam.Lens.NearClipPlane = 0.2f;
            cam.Lens.FarClipPlane = 1200f;
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
            if (Player != null)
            {
                bool fp = m == CameraMode.FirstPerson;
                if (fp && !Player.FirstPersonLook)
                {
                    var fwd = Player.Facing;
                    Player.SetLook(Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, 0f);
                    SnapProxies();
                }
                Player.FirstPersonLook = fp;
                if (Player.Avatar != null) Player.Avatar.HideHead(fp);
            }
            if (instant && Brain != null)
            {
                SnapProxies();
                Brain.ManualUpdate();
            }
        }

        void SnapProxies()
        {
            if (Player == null) return;
            UpdateProxies(0.0167f, true);
        }

        void LateUpdate() { if (_inited && Player != null) UpdateProxies(Time.deltaTime, false); }

        void UpdateProxies(float dt, bool snap)
        {
            Vector3 p = Player.Position;
            Vector3 v = Player.Velocity;

            // ---- broadcast: rides the sideline, leads the action along the field
            Vector3 lead = v * 0.65f;
            float z = Mathf.Clamp(p.z + lead.z, -46f, 46f);
            _bcFollow.position = new Vector3(0f, 0f, z);
            _bcLook.position = p + new Vector3(0f, 1.1f, 0f) + lead * 0.75f;

            // ---- behind: heading follows travel direction slowly
            float speed = v.magnitude;
            Vector3 f = Player.Facing;
            float targetYaw = speed > 1.5f ? Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg : Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            if (snap) _behindYaw = targetYaw;
            _behindYaw = Mathf.MoveTowardsAngle(_behindYaw, targetYaw, (40f + 60f * Mathf.Clamp01(speed / 8f)) * dt);
            _behindFollow.SetPositionAndRotation(p, Quaternion.Euler(0f, _behindYaw, 0f));
            Vector3 bf = Quaternion.Euler(0f, _behindYaw, 0f) * Vector3.forward;
            _behindLook.position = p + new Vector3(0f, 1.35f, 0f) + bf * 3f;

            // ---- first person: stabilised head position, yaw/pitch from look
            Vector3 head = Player.Head != null ? Player.Head.position : p + Vector3.up * 1.7f;
            float lookYaw = Player.LookYaw;
            Vector3 fwdLook = Quaternion.Euler(0f, lookYaw, 0f) * Vector3.forward;
            Vector3 targetPos = new Vector3(head.x, head.y + 0.03f, head.z) + fwdLook * 0.12f;
            if (snap) _fpPos = targetPos;
            else _fpPos = new Vector3(targetPos.x, Mathf.Lerp(_fpPos.y, targetPos.y, 1f - Mathf.Exp(-dt * 10f)), targetPos.z);
            _fpAnchor.SetPositionAndRotation(_fpPos, Quaternion.Euler(Player.LookPitch, lookYaw, 0f));

            // ---- stick basis
            switch (Mode)
            {
                case CameraMode.Broadcast: MoveBasisYaw = 90f; break;
                case CameraMode.Behind: MoveBasisYaw = _behindYaw; break;
                default: MoveBasisYaw = lookYaw; break;
            }
        }
    }
}
