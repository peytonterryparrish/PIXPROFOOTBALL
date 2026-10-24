using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 WATCH+HUD: the sideline broadcast camera of a watched game (BUGS B-052, B-053, B-054, B-110, B-114).
    /// One sideline for the whole game (the camera used to jump 88 m to the other side whenever the ball changed hands), a tighter lens so a body is 75-90 px tall at 1080p instead of 50-60, a smooth
    /// glide to the next line of scrimmage instead of a teleport, a camera that leads and tracks a kick or a pass, and a higher, wider set-up for the kicking plays that must hold all 22 bodies
    /// (the exact fit lives in <see cref="MatchCameraRig.Fit"/>).
    /// </summary>
    public sealed partial class MatchCameraRig
    {
        /// <summary>Watch games: the broadcast camera stays on the +x sideline all game.</summary>
        public bool FixedSide;
        /// <summary>Watch games: the camera glides to the next line of scrimmage between plays instead of cutting.</summary>
        public bool SmoothBetweenPlays;
        /// <summary>Vertical field of view of the sideline shot (degrees). 27 = the v0.10 lens; a watched game uses a tighter one.</summary>
        public float BaseFov = 27f;
        /// <summary>A kick or a pass is in the air: the camera leads and follows the ball tightly.</summary>
        public bool BallInFlight;
        public bool KickInFlight;
        float _kickBlend, _leadK = 0.55f, _dbgZ, _bcZ, _bcZv; bool _zInit; public static bool DebugJumps;
        /// <summary>Seconds the sideline camera takes to settle on a new focus point along the field.</summary>
        public float ZTau = 0.55f;

        /// <summary>Which sideline the broadcast camera is on for a play: +1 = the +x side.</summary>
        public float CameraSide(PlayFrame f) { return FixedSide ? 1f : (f.TowardsPlusZ ? 1f : -1f); }
        public float LiveSide { get { return CameraSide(Frame); } }

        /// <summary>One frame of the broadcast camera: where it stands, what it leads, how wide the lens is, where it looks.</summary>
        void BroadcastStep(float dt, bool snap)
        {
            _smoothFocus = _focus;
            // a kickoff / punt that is framed for all 22 bodies is shot a little higher and further out (the field fills the picture, not the stands)
            float wantHigh = FitSpan > 1f ? 1f : 0f;
            _kickBlend = snap ? wantHigh : Mathf.MoveTowards(_kickBlend, wantHigh, dt / 0.8f);
            if (_bcFollow != null) _bcFollow.FollowOffset = new Vector3((44f + 6f * _kickBlend) * CameraSide(Frame), 17f + 9f * _kickBlend, 0f);
            _leadK = snap ? 0.55f : Mathf.MoveTowards(_leadK, BallInFlight ? 1.5f : 0.55f, dt * 2.5f);   // eased: a sudden change of the lead is a jump of the whole shot
            Vector3 lead = new Vector3(_focusVel.x, 0f, _focusVel.z) * _leadK;
            float zT = Mathf.Clamp(_smoothFocus.z + lead.z, -52f, 52f);
            if (snap || !_zInit) { _bcZ = zT; _bcZv = 0f; _zInit = true; }
            else _bcZ = Mathf.SmoothDamp(_bcZ, zT, ref _bcZv, ZTau, 90f, Mathf.Max(dt, 0.0001f));   // a new focus (a returner, the next line of scrimmage) is a glide, never a step
            float z = _bcZ; float zShift = _bcZ - zT;
            if (DebugJumps && Mathf.Abs(z - _dbgZ) > 12f) Debug.Log("[CamDbg] target z " + _dbgZ.ToString("0.0") + " -> " + z.ToString("0.0") + " focus " + _focus.ToString("0.0") + " vel " + _focusVel.ToString("0.0") + " fit " + FitSpan + " snap " + snap + " follow z " + (_bcFollow != null ? _bcFollow.transform.position.z.ToString("0.0") : "-"));
            _dbgZ = z;
            _bcTarget.position = new Vector3(0f, 0f, z);
            if (Broadcast != null)
            {
                float wantBcFov = BaseFov + 3f * Mathf.Clamp01((Mathf.Abs(z) - 47f) / 5f);
                FitFov(ref wantBcFov);      // v0.11.0 CRITICAL: a kickoff is framed wide enough for both teams
                var bl = Broadcast.Lens;
                bl.FieldOfView = snap ? wantBcFov : Mathf.Lerp(bl.FieldOfView, wantBcFov, UserCameraMath.Alpha(dt, Mathf.Lerp(0.6f, 0.25f, _kickBlend)));
                Broadcast.Lens = bl;
            }
            // the look point follows the ball up when a kick climbs (a punt peaks at 25 m)
            _bcLook.position = new Vector3(Mathf.Clamp(_smoothFocus.x * 0.55f, -14f, 14f), Mathf.Clamp(_smoothFocus.y * Mathf.Lerp(0.6f, 0.9f, _kickBlend), 1.0f, 20f), _smoothFocus.z + lead.z * 0.85f + zShift);
        }
    }
}
