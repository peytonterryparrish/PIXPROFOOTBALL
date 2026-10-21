using UnityEngine;

namespace PixFootball.Presentation
{
    public enum ReplayAngle { Free = 0, Sideline = 1, EndZone = 2, High = 3, BehindQb = 4, FollowBall = 5, Orbit = 6 }

    /// <summary>
    /// The replay camera: preset angles (sideline, end zone, high, behind the quarterback, follow the ball), the free camera (orbit / pan / zoom around a target player, ground level)
    /// and the depth of field that keeps the target in focus. Pure math over the recorded clip: given a clip and a time it returns where the camera is.
    /// </summary>
    public sealed class ReplayView
    {
        public static readonly string[] AngleNames = { "FREE CAMERA", "SIDELINE", "END ZONE", "HIGH", "BEHIND QB", "FOLLOW BALL", "ORBIT" };

        public ReplayAngle Angle = ReplayAngle.Free;
        /// <summary>Agent index (0-10 offense, 11-21 defense) the camera is about, or -1 for the ball.</summary>
        public int Target;
        public float Yaw = 215f, Pitch = 17f, Dist = 13f;
        public Vector3 Pan;
        public bool Ground, Dof = true;
        public float AutoOrbitRate;
        Vector3 _fp; bool _init;
        Vector3 _lastPos, _lastFocus;

        public Vector3 LastPos { get { return _lastPos; } }
        public Vector3 LastFocus { get { return _lastFocus; } }

        public void Reset(ReplayClip clip, int target, ReplayAngle angle, float yaw = 215f)
        {
            Target = target; Angle = angle; Yaw = yaw; Pitch = 17f; Dist = 13f; Pan = Vector3.zero; Ground = false; _init = false; AutoOrbitRate = 0f;
        }

        public Vector3 Subject(ReplayClip clip, float t)
        {
            return Target < 0 ? clip.BallPosAt(t) : clip.AgentPos(Target, t) + Vector3.up * 1.05f;
        }

        /// <summary>Switches to the free camera keeping the current view (no jump).</summary>
        public void ToFree()
        {
            if (Angle == ReplayAngle.Free) return;
            Vector3 d = _lastPos - _lastFocus;
            Dist = Mathf.Clamp(d.magnitude, 3f, 60f);
            Yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            Pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(d.y / Mathf.Max(0.01f, d.magnitude), -1f, 1f)) * Mathf.Rad2Deg, 2f, 80f);
            Pan = Vector3.zero;
            Angle = ReplayAngle.Free;
        }

        public ShotPose Compute(ReplayClip clip, float t, float dt)
        {
            float down = clip.TowardsPlusZ ? 1f : -1f;
            Vector3 subj = Subject(clip, t);
            _fp = _init ? Vector3.Lerp(_fp, subj, 1f - Mathf.Exp(-Mathf.Max(dt, 0.0001f) * 9f)) : subj;
            _init = true;
            Vector3 fp = _fp;
            Vector3 pos, look = fp; float fov = 30f, ap = 2.2f; bool dof = Dof;
            switch (Angle)
            {
                case ReplayAngle.Sideline:
                    pos = new Vector3(down * 28.5f, 2.3f, fp.z - down * 3f); fov = 22f; ap = 3.2f;
                    break;
                case ReplayAngle.EndZone:
                    pos = new Vector3(fp.x * 0.35f, 8.5f, -down * 60f); fov = 15f; ap = 3.5f;
                    break;
                case ReplayAngle.High:
                    pos = new Vector3(fp.x + 4f, 27f, fp.z - down * 17f); fov = 32f; ap = 5f; look = fp + Vector3.down * 0.5f;
                    break;
                case ReplayAngle.BehindQb:
                {
                    Vector3 qb = clip.AgentPos(0, t);
                    pos = qb + new Vector3(0f, 2.15f, -down * 5.4f);
                    look = qb + new Vector3(0f, 1.2f, down * 14f); fov = 42f; ap = 4f;
                    break;
                }
                case ReplayAngle.FollowBall:
                {
                    Vector3 b0 = clip.BallPosAt(t - 0.35f), b1 = clip.BallPosAt(t + 0.35f);
                    Vector3 trav = b1 - b0; trav.y = 0f; if (trav.sqrMagnitude < 0.04f) trav = new Vector3(0f, 0f, down); trav.Normalize();
                    Vector3 right = Vector3.Cross(Vector3.up, trav);
                    pos = clip.BallPosAt(t) - trav * 8.5f + right * 3.2f + Vector3.up * 3.2f;
                    look = clip.BallPosAt(t) + trav * 3f; fp = look; fov = 36f; ap = 2.8f;
                    break;
                }
                case ReplayAngle.Orbit:
                    Yaw += AutoOrbitRate * Mathf.Max(dt, 0f);
                    goto default;
                default:
                {
                    float pitch = Ground ? Mathf.Min(Pitch, 3.5f) : Pitch;
                    float dist = Ground ? Mathf.Min(Dist, 9.5f) : Dist;
                    float yr = Yaw * Mathf.Deg2Rad, pr = pitch * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Sin(yr) * Mathf.Cos(pr), Mathf.Sin(pr), Mathf.Cos(yr) * Mathf.Cos(pr));
                    look = fp + Pan;
                    pos = look + dir * dist;
                    if (Ground) { pos.y = Mathf.Max(0.55f, pos.y); }
                    pos.y = Mathf.Max(0.35f, pos.y);
                    fov = Ground ? 36f : 32f; ap = 1.9f;
                    break;
                }
            }
            _lastPos = pos; _lastFocus = look;
            float focus = dof ? Vector3.Distance(pos, Angle == ReplayAngle.BehindQb ? clip.AgentPos(0, t) + Vector3.up * 1.4f : (Angle == ReplayAngle.FollowBall ? clip.BallPosAt(t) : subj + Pan)) : -1f;
            return new ShotPose { Pos = pos, Look = look, Fov = fov, Focus = focus, Aperture = ap };
        }
    }
}
