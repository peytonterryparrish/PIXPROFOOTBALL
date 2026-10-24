using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 CRITICAL (playtest note 2, "the whole team disappears on kickoffs"): the broadcast lens was a fixed 27 degrees that only followed the ball along the field. A kickoff spreads the two teams over 55-60 m (the kicking line at its 35, the front of the
    /// receiving team 10 yards off, the returners 60 m away), so one whole team stood outside the picture for the whole set and for the first seconds of the kick. The match director now sets <see cref="FitSpan"/> (metres of field along the
    /// length that must be in shot) and the lens widens to hold it; 0 = normal framing.
    /// </summary>
    public sealed partial class MatchCameraRig
    {
        /// <summary>Length of field (m) the broadcast shot must hold; 0 = off.</summary>
        public float FitSpan;
        float _fitSmooth;

        /// <summary>Vertical field of view (degrees) at which a camera <paramref name="distance"/> metres from the focus sees <paramref name="span"/> metres across its width (with a 12 % margin for the lens and the damping), clamped to 27..62.</summary>
        public static float FovForSpan(float span, float distance, float aspect)
        {
            float tanV = span * 1.12f / (2f * Mathf.Max(25f, distance) * Mathf.Max(0.5f, aspect));
            return Mathf.Clamp(2f * Mathf.Atan(tanV) * Mathf.Rad2Deg, 27f, 62f);
        }

        /// <summary>v0.11 WATCH: the world positions of the bodies that must be in shot (set by the director with <see cref="FitSpan"/>); null = use the span estimate.</summary>
        public Vector3[] FitPoints; public int FitPointCount;

        /// <summary>
        /// Exact field of view for the pose the camera has right now: the smallest vertical field of view that puts every fit point inside the picture, with a margin and the bottom strip the score bar covers kept clear.
        /// (The span estimate assumed every body is as far away as the middle of the field; the near-sideline bodies at the two ends of a kickoff left the frame.)
        /// </summary>
        public static float FovForPoints(Transform cam, Vector3[] pts, int n, float aspect, float margin = 1.12f, float bottomClear = 0.10f)
        {
            float need = 0.01f;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = cam.InverseTransformPoint(pts[i]);
                if (d.z < 1f) continue;
                float tx = Mathf.Abs(d.x) / d.z / Mathf.Max(0.5f, aspect);
                float ty = d.y >= 0f ? d.y / d.z : -d.y / d.z / (1f - 2f * bottomClear);
                need = Mathf.Max(need, Mathf.Max(tx, ty));
            }
            return Mathf.Clamp(2f * Mathf.Atan(need * margin) * Mathf.Rad2Deg, 27f, 70f);
        }
        void FitFov(ref float fov)
        {
            _fitSmooth = Mathf.MoveTowards(_fitSmooth, FitSpan, 60f * Time.unscaledDeltaTime);
            if (_fitSmooth <= 1f || Broadcast == null) return;
            Camera cam = PlayerLod.ViewCamera != null ? PlayerLod.ViewCamera : Camera.main;
            float aspect = cam != null && cam.pixelHeight > 0 ? (float)cam.pixelWidth / cam.pixelHeight : 16f / 9f;
            Vector3 look = _bcLook != null ? _bcLook.position : Vector3.zero;
            if (FitPoints != null && FitPointCount > 0) fov = Mathf.Max(fov, FovForPoints(Broadcast.transform, FitPoints, FitPointCount, aspect));   // v0.11 WATCH: exact fit for the pose the camera has
            else fov = Mathf.Max(fov, FovForSpan(_fitSmooth, Vector3.Distance(Broadcast.transform.position, look), aspect));
        }
    }
}
