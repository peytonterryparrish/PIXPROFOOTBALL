using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.11.0 CRITICAL: broadcast framing of the kicking plays (see <see cref="MatchCameraRig.FitSpan"/>).</summary>
    public sealed partial class MatchDirector
    {
        /// <summary>QA: -pixNoKickFit switches the wide kickoff framing off (the v0.10 look).</summary>
        public static bool NoKickFit = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoKickFit") >= 0;

        /// <summary>
        /// While a kickoff or punt is set up and in the air (until a returner holds the ball) the camera looks at the middle of the 22 bodies and the lens holds all of them. Called from <see cref="ApplyCameraTarget"/> with the focus the director chose.
        /// </summary>
        Vector3[] _fitPts;

        void FitFraming(ref Vector3 focus, ref Vector3 fvel)
        {
            if (Cams == null) return;
            float span = 0f;
            if (!NoKickFit && Sim != null && (Sim.Kind == PlayKind.Kickoff || Sim.Kind == PlayKind.Punt) && Stage != null)
            {
                var b = Sim.Ball;
                bool carried = b.State == BallState.Held && b.Holder >= 0 && Sim.Agents[b.Holder].Side == SimSide.Defense;
                if (Sim.Phase == PlayPhase.PreSnap || (Sim.Phase == PlayPhase.Live && !carried && Sim.Time < 9f))
                {
                    float zMin = 1e9f, zMax = -1e9f;
                    if (_fitPts == null) _fitPts = new Vector3[23];
                    for (int i = 0; i < 22; i++) { Vector3 w = _frame.ToUnity(Sim.Agents[i].Pos); _fitPts[i] = w + Vector3.up * 0.9f; float z = w.z; if (z < zMin) zMin = z; if (z > zMax) zMax = z; }
                    if (Stage != null && Stage.Ball != null) { _fitPts[22] = Stage.Ball.Position; Cams.FitPointCount = 23; } else Cams.FitPointCount = 22;
                    Cams.FitPoints = _fitPts;
                    span = (zMax - zMin) + 8f;
                    focus = new Vector3(focus.x, focus.y, 0.5f * (zMin + zMax)); fvel = Vector3.zero;
                }
            }
            if (span <= 0f) Cams.FitPointCount = 0;
            Cams.FitSpan = span;
        }
    }
}
