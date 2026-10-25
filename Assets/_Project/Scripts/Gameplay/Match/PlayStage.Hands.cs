using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.12.0 CONTACT: hands on the pads. While two linemen are engaged (a block, a pass rush, a double team) each man's hands are asked, through the Final IK door (<see cref="PixIk.Hand"/>), to be on the other man's
    /// shoulder pads: the blocker's palms on the defender's chest (the numbers), the defender's hands on the blocker's shoulders. The pair clips are authored with the partner at one fixed distance and the simulation pushes the pair
    /// around, so without this the hands hang in the air or go through the pads. The legs are NOT Final IK's: the leg chains belong to <see cref="EngageStepper"/> / <see cref="FootPlant"/> / <see cref="StanceIK"/>, which
    /// re-apply their ankle targets after Final IK has run (<see cref="SimAgentView.EngageLegsRestore"/>). Without the Final IK asset <see cref="PixIk.Available"/> is false and nothing here runs.
    /// </summary>
    public sealed partial class PlayStage
    {
        /// <summary>QA: switch the pad hands off (<c>-pixNoPadHands</c>).</summary>
        public static bool NoPadHands = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoPadHands") >= 0;
        /// <summary>A hand is only asked for the pad when the animated hand is within this distance (m) of it: further away the clip is doing something else (a swim over the top, a shed).</summary>
        public const float PadReach = 0.55f;
        /// <summary>QA: hand requests made this game.</summary>
        public static int PadHandRequests;

        /// <summary>World point on the front of <paramref name="opp"/>'s pads where the hand of <paramref name="self"/> goes. side: +1 = self's right hand, -1 = left.</summary>
        public static Vector3 PadTarget(Vector3 selfChest, Vector3 oppChest, int side, bool onChest)
        {
            Vector3 u = oppChest - selfChest; u.y = 0f;
            if (u.sqrMagnitude < 1e-4f) return oppChest;
            u.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, u);      // the blocking man's own right
            // blocker: palms on the numbers, a hand width either side of the sternum, a little under the chest bone; defender: hands on the shoulders (wider, higher)
            float lateral = onChest ? 0.15f : 0.21f, rise = onChest ? -0.02f : 0.07f, front = onChest ? 0.19f : 0.17f;
            return oppChest - u * front + right * (side * lateral) + Vector3.up * rise;
        }

        void ServicePadHands()
        {
            if (NoPadHands || EngageStepper.V011 || !PixIk.Available || PixIk.Off || Sim == null || Sim.Phase != PlayPhase.Live) return;
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Sim.Agents[i]; var e = a.Engagement;
                if (e == null || e.Defender == null || e.Primary == null) continue;
                if (a.Action != AgentAction.Engaged && a.Action != AgentAction.Block && a.Action != AgentAction.Shed) continue;
                bool isDef = e.Defender == a;
                if (!isDef && !e.Blockers.Contains(a)) continue;
                SimAgent opp = isDef ? e.Primary : e.Defender;
                var v = _views[i]; var ov = _views[opp.Id];
                if (v == null || ov == null || v.ContactDriven || v.Avatar == null || ov.Avatar == null) continue;
                if (v.Ragdoll != null && v.Ragdoll.Active) continue;
                Transform ch = v.Avatar.Chest, och = ov.Avatar.Chest, rh = v.Avatar.RightHand, lh = v.Avatar.LeftHand;
                if (ch == null || och == null || rh == null || lh == null) continue;
                bool onChest = !isDef;
                Vector3 tr = PadTarget(ch.position, och.position, +1, onChest), tl = PadTarget(ch.position, och.position, -1, onChest);
                // a hand is only pulled when the clip already has it near the pad
                if ((rh.position - tr).sqrMagnitude < PadReach * PadReach) { PixIk.Hand(v, false, tr, rh.rotation, 0.85f, 0f); PadHandRequests++; }
                if ((lh.position - tl).sqrMagnitude < PadReach * PadReach) { PixIk.Hand(v, true, tl, lh.rotation, 0.85f, 0f); PadHandRequests++; }
            }
        }
    }
}
