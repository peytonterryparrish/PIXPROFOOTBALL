using PixFootball.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.Presentation
{
    /// <summary>
    /// The broadcast layer's own camera control: while it is active the Cinemachine brain is off (the live cameras keep tracking underneath, so handing the camera back is an instant cut
    /// to exactly where the live camera is) and the main camera is driven directly: position, rotation, field of view and an optional depth of field focused on a point of interest
    /// (HDRP physical camera mode: the aperture and focus distance of the camera control the blur, the lens is switched to a physical 36 x 24 mm sensor).
    /// </summary>
    [DefaultExecutionOrder(31000)]
    public sealed class BroadcastCamera : MonoBehaviour
    {
        public const float SensorHeight = 24f, SensorWidth = 36f;

        Camera _cam; HDAdditionalCameraData _hd; MatchCameraRig _rig;
        Volume _vol; DepthOfField _dof;
        Vector3 _pos; Quaternion _rot = Quaternion.identity; float _fov = 30f, _focus = -1f, _aperture = 2.4f;
        bool _pendingCut;
        float _dofWeight, _dofTarget;
        bool _savedPhysical; float _savedFov;
        public bool Active { get; private set; }
        public Vector3 Position { get { return _pos; } }
        public Quaternion Rotation { get { return _rot; } }
        public float Fov { get { return _fov; } }
        public bool DofOn { get { return _dofTarget > 0.5f; } }

        public static BroadcastCamera Create(Transform parent, Camera cam, MatchCameraRig rig)
        {
            var go = new GameObject("BroadcastCamera");
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<BroadcastCamera>();
            b._cam = cam; b._rig = rig; b._hd = cam.GetComponent<HDAdditionalCameraData>();
            b.BuildVolume();
            return b;
        }

        void BuildVolume()
        {
            var go = new GameObject("BroadcastDepthOfField");
            go.transform.SetParent(transform, false);
            _vol = go.AddComponent<Volume>();
            _vol.isGlobal = true; _vol.priority = 500f; _vol.weight = 0f;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            _dof = prof.Add<DepthOfField>(true);
            _dof.focusMode.Override(DepthOfFieldMode.UsePhysicalCamera);
            _dof.focusDistanceMode.Override(FocusDistanceMode.Volume);
            _dof.focusDistance.Override(10f);
            _vol.sharedProfile = prof;
            _vol.profile = prof;
        }

        /// <summary>Takes the camera from the live rig.</summary>
        public void Begin()
        {
            if (Active) return;
            Active = true;
            _savedPhysical = _cam.usePhysicalProperties; _savedFov = _cam.fieldOfView;
            _pos = _cam.transform.position; _rot = _cam.transform.rotation; _fov = _cam.fieldOfView;
            _rig.SetOverride(true);
            _pendingCut = true;
        }

        /// <summary>Gives the camera back (a cut to the live camera).</summary>
        public void End()
        {
            if (!Active) return;
            Active = false;
            _dofTarget = 0f; _dofWeight = 0f; if (_vol != null) _vol.weight = 0f;
            _cam.usePhysicalProperties = false; _cam.fieldOfView = _savedFov;   // QA-867: hand the lens back as we found it
            _rig.SetOverride(false);
            ResetHistory();
        }

        // v0.9 (GFX-442): a cut resets the TAA history instead of switching to FXAA for 3 frames (the visible AA change after every broadcast cut)
        void ResetHistory()
        {
            if (_cam == null) return;
            var hc = UnityEngine.Rendering.HighDefinition.HDCamera.GetOrCreate(_cam);
            if (hc != null) hc.Reset();
        }

        /// <summary>Sets where the camera is for this frame. focusDistance &lt;= 0 turns the depth of field off.</summary>
        public void Set(Vector3 pos, Quaternion rot, float fov, float focusDistance = -1f, float aperture = 2.2f, bool cut = false)
        {
            _pos = pos; _rot = rot; _fov = Mathf.Clamp(fov, 8f, 100f);
            if (focusDistance > 0f) { _focus = focusDistance; _aperture = aperture; _dofTarget = 1f; }
            else _dofTarget = 0f;
            if (cut) _pendingCut = true;
        }

        public void Set(Vector3 pos, Vector3 lookAt, float fov, float focusDistance = -1f, float aperture = 2.2f, bool cut = false)
        {
            Vector3 d = lookAt - pos; if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
            Set(pos, Quaternion.LookRotation(d.normalized, Vector3.up), fov, focusDistance, aperture, cut);
        }

        public void Cut() { _pendingCut = true; }

        void LateUpdate()
        {
            if (!Active || _cam == null) return;
            var t = _cam.transform;
            t.SetPositionAndRotation(_pos, _rot);
            // physical camera: the field of view comes from the focal length on a 36 x 24 mm sensor, the aperture and the focus distance make the depth of field
            float dt = UnityEngine.Time.unscaledDeltaTime;
            _dofWeight = Mathf.MoveTowards(_dofWeight, _dofTarget, dt / 0.35f);
            if (_dofWeight > 0.001f)
            {
                _cam.usePhysicalProperties = true;
                _cam.sensorSize = new Vector2(SensorWidth, SensorHeight);
                _cam.gateFit = Camera.GateFitMode.Vertical;
                _cam.focalLength = (SensorHeight * 0.5f) / Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad);
                _cam.aperture = _aperture;
                _cam.focusDistance = Mathf.Max(0.3f, _focus);
                if (_dof != null) _dof.focusDistance.value = Mathf.Max(0.3f, _focus);
                if (_vol != null) _vol.weight = _dofWeight;
            }
            else
            {
                _cam.usePhysicalProperties = false;
                _cam.fieldOfView = _fov;
                if (_vol != null) _vol.weight = 0f;
            }
            if (_pendingCut) { _pendingCut = false; ResetHistory(); }
        }
    }
}
