using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>One user-camera preset (metres / degrees). The camera sits behind the focus along the view direction, raised, and looks at a point ahead of the focus.</summary>
    public struct UserCameraPreset
    {
        public string Name;
        /// <summary>Height above the turf before the snap, and how far behind the line of scrimmage (pre-snap) / the focus (live) the camera stands.</summary>
        public float Height, Back;
        /// <summary>The look-at point: metres downfield of the focus, and its height.</summary>
        public float LookAhead, LookHeight;
        /// <summary>Vertical field of view in degrees.</summary>
        public float Fov;
        /// <summary>Pull-back at the snap: extra distance, height and field of view (scaled by the "zoom out" setting) over ZoomTime seconds.</summary>
        public float SnapBack, SnapHeight, SnapFov, ZoomTime;
        /// <summary>Live following of a ball carrier: distance behind him, height, look-ahead and the lead (seconds of his velocity added to the focus).</summary>
        public float CarrierBack, CarrierHeight, CarrierLookAhead, CarrierLead, CarrierFov;

        /// <summary>All-Pro 2K8 framing (the default): high and behind the offense, the whole formation and ~15-20 yd of the field in view, pitched down about 28 degrees.</summary>
        public static UserCameraPreset Classic()
        {
            return new UserCameraPreset { Name = "Classic", Height = 9.0f, Back = 17.0f, LookAhead = 0.5f, LookHeight = 0.5f, Fov = 46f,
                SnapBack = 3.2f, SnapHeight = 1.8f, SnapFov = 3f, ZoomTime = 0.9f, CarrierBack = 10.5f, CarrierHeight = 6.4f, CarrierLookAhead = 6.5f, CarrierLead = 0.5f, CarrierFov = 42f };
        }

        /// <summary>Madden style: lower and closer, over the quarterback's shoulders.</summary>
        public static UserCameraPreset Madden()
        {
            return new UserCameraPreset { Name = "Madden", Height = 5.4f, Back = 10.5f, LookAhead = 10.0f, LookHeight = 1.0f, Fov = 48f,
                SnapBack = 2.4f, SnapHeight = 1.4f, SnapFov = 4f, ZoomTime = 0.8f, CarrierBack = 7.5f, CarrierHeight = 4.4f, CarrierLookAhead = 8f, CarrierLead = 0.45f, CarrierFov = 50f };
        }

        /// <summary>Wide: high and far, the whole width of the field and the secondary.</summary>
        public static UserCameraPreset Wide()
        {
            return new UserCameraPreset { Name = "Wide", Height = 14.5f, Back = 21f, LookAhead = 9.0f, LookHeight = 0f, Fov = 42f,
                SnapBack = 3.5f, SnapHeight = 2.5f, SnapFov = 3f, ZoomTime = 1.0f, CarrierBack = 15f, CarrierHeight = 10.5f, CarrierLookAhead = 7f, CarrierLead = 0.5f, CarrierFov = 42f };
        }

        public static UserCameraPreset Of(CameraStyle style)
        {
            switch (style) { case CameraStyle.Madden: return Madden(); case CameraStyle.Wide: return Wide(); default: return Classic(); }
        }
    }

    /// <summary>What the camera is looking at this frame.</summary>
    public struct UserCameraInput
    {
        /// <summary>Unit vector on the XZ plane: the direction the user's side attacks (offense and the kicking team: the end zone they drive at; defense: toward the offense's backfield, i.e. away from the defense's goal).</summary>
        public Vector3 ViewDir;
        /// <summary>The ball on the line of scrimmage (pre-snap) in world space.</summary>
        public Vector3 Los;
        /// <summary>The live focus (quarterback in the pocket, the ball in the air, the ball carrier) and its velocity (m/s).</summary>
        public Vector3 Focus, FocusVelocity;
        /// <summary>True while the focus is a ball carrier / the ball in the open (the follow camera); false for the pocket / pre-snap framing.</summary>
        public bool Follow;
        public bool PreSnap;
        /// <summary>Seconds since the snap (drives the pull-back).</summary>
        public float SnapTime;
        /// <summary>When the user plays defense: the camera sits behind the defense, so the focus is the offense's backfield and the "back" side is the defense's.</summary>
        public bool Defense;
        /// <summary>v0.9 (GFX-395): 0 none, 1 kickoff, 2 field goal, 3 punt (the user kicks): the dedicated kick framings of <see cref="UserCameraMath.KickFraming"/> replace the scrimmage preset.</summary>
        public int KickKind;
        /// <summary>v0.9.0 CONNECT E-390: the focus is a ball in the air (pass, punt, kick); <c>Focus.y</c> is then its height in metres and the follow camera frames it in 3D instead of aiming at the turf under it.</summary>
        public bool BallAirborne;
    }

    public struct UserCameraPose
    {
        public Vector3 Position, LookAt;
        public float Fov;
        public Quaternion Rotation { get { Vector3 f = LookAt - Position; return f.sqrMagnitude < 1e-6f ? Quaternion.identity : Quaternion.LookRotation(f.normalized, Vector3.up); } }
        public float PitchDegrees { get { Vector3 f = (LookAt - Position).normalized; return Mathf.Asin(Mathf.Clamp(-f.y, -1f, 1f)) * Mathf.Rad2Deg; } }
    }

    /// <summary>Pure camera math for the behind-the-offense user camera (EditMode tested): target pose for a focus, preset and trims.</summary>
    public static class UserCameraMath
    {
        // v0.9.0 CONNECT E-393: MaxLateral 15.5 -> 20 m (the sideline is 24.4 m from the centre). E-396: MaxCamZ 63 -> end line (54.9 m) + 4 m so the camera never sits in the goalposts / stadium behind the end zone.
        public const float MaxLateral = 20f, MinZ = -62f, MaxZ = 62f, MaxCamZ = 58.9f, EndLineZ = 54.9f;

        /// <summary>Smooth 0..1 ramp used for the snap pull-back.</summary>
        public static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        public static UserCameraPose Evaluate(in UserCameraInput c, in UserCameraPreset p, float heightTrim, float distanceTrim, float fovTrim, float zoomOut)
        {
            Vector3 d = c.ViewDir; d.y = 0f;
            d = d.sqrMagnitude < 1e-6f ? Vector3.forward : d.normalized;
            float zoom = c.PreSnap ? 0f : Ease(c.SnapTime / Mathf.Max(0.05f, p.ZoomTime)) * Mathf.Clamp01(zoomOut);
            Vector3 focus; float back, height, ahead, fov, airH = 0f;
            if (c.Follow && !c.PreSnap)
            {
                float speed = new Vector3(c.FocusVelocity.x, 0f, c.FocusVelocity.z).magnitude;
                float run = Mathf.Clamp01(speed / 8f);
                focus = c.Focus + new Vector3(c.FocusVelocity.x, 0f, c.FocusVelocity.z) * p.CarrierLead;
                back = p.CarrierBack + 1.4f * run + distanceTrim * 0.75f;
                height = p.CarrierHeight + 0.5f * run + heightTrim * 0.75f;
                ahead = p.CarrierLookAhead; fov = p.CarrierFov + 2f * run + fovTrim;
                // v0.9.0 CONNECT E-390: a ball in the air (deep pass, punt, kick) climbs 10 to 20 m, far above the old fixed 0.5 m look height. Raise and pull back with its height and aim
                // at its 3D position so it stays inside the frame (the ground under it drops out of the bottom edge on the highest balls; the ball is the story).
                if (c.BallAirborne)
                {
                    airH = Mathf.Clamp(c.Focus.y, 0f, 30f);
                    height += Mathf.Min(airH * 0.5f, 7f); back += Mathf.Min(airH * 0.4f, 6f); fov += Mathf.Min(airH * 0.3f, 4f);
                }
            }
            else if (c.PreSnap)
            {
                focus = c.Los;
                back = p.Back + distanceTrim; height = p.Height + heightTrim; ahead = p.LookAhead; fov = p.Fov + fovTrim;
            }
            else
            {
                // the pocket: the focus is the quarterback / the play's centre of gravity, pulled back a little at the snap and kept loose behind the ball
                focus = Vector3.Lerp(c.Los, c.Focus, 0.72f);
                back = p.Back + distanceTrim + p.SnapBack * zoom; height = p.Height + heightTrim + p.SnapHeight * zoom; ahead = p.LookAhead; fov = p.Fov + fovTrim + p.SnapFov * zoom;
            }
            // the camera line: the focus slides along the view direction, the lateral position follows the focus (clamped to the width of the pitch)
            // v0.9.0 CONNECT E-393: the lens widens by up to 6 degrees as the focus nears the sideline (|x| 12 -> 24 m) so the carrier stays inside the middle of the frame
            fov += 6f * Ease((Mathf.Abs(focus.x) - 12f) / 12f);
            focus.x = Mathf.Clamp(focus.x, -MaxLateral, MaxLateral);
            if (c.KickKind != 0 && !(c.Follow && !c.PreSnap)) KickFraming(c.KickKind, ref back, ref height, ref ahead, ref fov);
            if (c.Defense) { back *= 1.10f; height *= 1.08f; ahead += 3f; }   // behind the defense: a little deeper and higher so the line and the backfield read
            Vector3 pos = focus - d * back; pos.y = height;
            Vector3 look = focus + d * ahead; look.y = c.BallAirborne && c.Follow && !c.PreSnap ? Mathf.Max(p.LookHeight, airH * 0.62f) : p.LookHeight;   // E-390: aim at the ball's height
            // v0.9.0 CONNECT E-396: the camera stays 4 m behind the end line at most; the metres lost behind the end zone come back as height (steeper, shorter shot, same framing), and
            // behind the end line it slides off the goalposts' uprights (x = +/-2.8 m) so the sticks never cut the frame.
            float zRaw = pos.z;
            pos.z = Mathf.Clamp(pos.z, -MaxCamZ, MaxCamZ);
            float lost = Mathf.Abs(zRaw) - Mathf.Abs(pos.z);
            if (lost > 0f) pos.y += Mathf.Min(lost * 0.5f, 3f);
            float behindEnd = Ease((Mathf.Abs(pos.z) - (EndLineZ - 2f)) / 3f);
            if (behindEnd > 0f && Mathf.Abs(pos.x) < 4.8f) pos.x = Mathf.Lerp(pos.x, (pos.x < 0f ? -4.8f : 4.8f), behindEnd);
            return new UserCameraPose { Position = pos, LookAt = look, Fov = fov };
        }

        /// <summary>v0.9 (GFX-395): kick framings behind the kicker: kickoff low and wide behind the tee, field goal low behind the kicker framing the posts, punt high behind the punter (metres / degrees).</summary>
        public static void KickFraming(int kind, ref float back, ref float height, ref float ahead, ref float fov)
        {
            switch (kind)
            {
                case 1: back = 11f; height = 3.5f; ahead = 16f; fov = 48f; break;
                case 2: back = 8f; height = 2.4f; ahead = 24f; fov = 38f; break;
                case 3: back = 13f; height = 6.0f; ahead = 14f; fov = 44f; break;
            }
        }

        /// <summary>Exponential smoothing factor for a time constant.</summary>
        public static float Alpha(float dt, float tau) { return 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, tau)); }
    }
}
