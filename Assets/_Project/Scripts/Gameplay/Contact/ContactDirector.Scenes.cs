using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 SIM-FLOW: the blow-up hit. The simulation marks the hardest big hits (10.8+ yd/s closing, a tackler not much lighter than the carrier, front or side, about four a game) with the event note "BlowUp" and books no pair take:
    /// the carrier is thrown back by the active ragdoll (blended from the animated pose, capped velocities, then the classified get-up), the tackler follows through with his own big-hit clip and the camera shakes.
    /// </summary>
    public sealed partial class ContactDirector
    {
        public static bool NoBlowUps = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoBlowUps") >= 0;
        /// <summary>QA: blow-up hits shown as ragdoll knocks.</summary>
        public static int BlowUpsShown;

        bool TryBlowUp(in PlayEvent e)
        {
            if (!RagdollEnabled || NoBlowUps) return false;
            int tackler = e.AgentA, carrier = e.AgentB;
            var vc = View(carrier); var vt = View(tackler);
            if (vc == null || vt == null || vc.Ragdoll == null || !vc.Ragdoll.Available || vc.Ragdoll.Active) return false;
            if (InSession(carrier) || InSession(tackler)) return false;
            Vector3 dir = vc.transform.position - vt.transform.position; dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = vt.Forward;
            dir.Normalize();
            float closing = Mathf.Max(0f, e.Value * PlayFrame.YardsToMeters);
            float mag = Mathf.Clamp(2.6f + 0.32f * closing, 3.2f, 7.0f);
            FireNow(vc.transform.position, Mathf.Clamp01(0.6f + closing / 22f), true, carrier, tackler, tackler, carrier);
            _knocks.Add(new PendingKnock { Agent = carrier, Impulse = dir * mag + Vector3.up * 1.5f, Intensity = 0.92f, At = 0.04f });
            BlowUpsShown++;
            return true;
        }
    }
}
