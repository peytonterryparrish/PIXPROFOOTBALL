using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 BODIES (playtest note 9): what the ball does once the play is dead. The tucked carry (five points of pressure) belongs to live play only. After the whistle:
    /// the carrier lets the tuck go and holds the ball loosely in one hand while he gets up and walks (<see cref="BallFollower.SetRelaxed"/>); a scorer holds it up on the palm for a moment and spikes it
    /// down when his hand swings down (<see cref="BallFollower.SetSpike"/>, at the latest 2.4 s into the celebration); an official comes to him and takes it (<see cref="BallFollower.SetOfficial"/>).
    /// </summary>
    public sealed partial class PlayStage
    {
        /// <summary>QA / probes: officials that do not come from a match director.</summary>
        public MatchOfficials CrewOverride;
        MatchOfficials Crew { get { if (CrewOverride != null) return CrewOverride; var md = MatchDirector.Instance; return md != null ? md.Officials : null; } }

        float _bfDeadAt = -1f; int _bfState; bool _bfSent; float _bfSentAt;
        /// <summary>QA: the ball flow state of the dead play (0 normal, 1 relaxed, 2 spiked, 3 with an official) and seconds since the whistle.</summary>
        public int BallFlowState { get { return _bfState; } }
        public float SecondsSinceWhistle { get { return _bfDeadAt < 0f ? -1f : Time.time - _bfDeadAt; } }
        public const float SpikeNotBefore = 0.9f, SpikeLatest = 2.4f, RelaxAfter = 0.55f, OfficialCallAfter = 0.6f;

        /// <summary>True while the whistle has gone but a carrier still holds the ball and an official has not taken it (the match director keeps the play on screen for it, at most about 7 s after the whistle, so the ball is never left stuck in a hand).</summary>
        public bool BallFlowBusy
        {
            get
            {
                if (Sim == null || Ball == null || Sim.Phase != PlayPhase.Dead || _bfDeadAt < 0f || _bfState >= 2 || Crew == null) return false;
                var b = Sim.Ball; if (b.State != BallState.Held || b.Holder < 0 || b.Holder >= Sim.Agents.Length) return false;
                if (Sim.Agents[b.Holder].Action == AgentAction.Celebrate) return false;
                return Time.time - _bfDeadAt < 7f;
            }
        }

        /// <summary>Called from <c>UpdateBall</c> for the player who holds the ball. Returns true when the ball was placed here.</summary>
        bool BallFlowHeld(SimAgent h, SimAgentView v)
        {
            if (SimAgentView.DebugNoFlow || Sim == null || Ball == null) return false;
            if (Sim.Phase != PlayPhase.Dead) { _bfDeadAt = -1f; _bfState = 0; _bfSent = false; return false; }
            if (_bfDeadAt < 0f) { _bfDeadAt = Time.time; _bfState = 0; _bfSent = false; }
            float since = Time.time - _bfDeadAt;
            if (_bfState >= 2) return true;      // the ball follower owns it now (spiked, or in the official's hand)
            int side = _carrySide[h.Id] >= 0 ? 1 : -1;
            if (h.Action == AgentAction.Celebrate)
            {
                side = CelebrateHand(h, v);
                float t = h.ActionTime;
                bool swing = t > SpikeNotBefore && Ball.BallVelocity.y < -1.5f;
                if (swing || t > SpikeLatest) { Ball.SetSpike(v, side, v.transform.position.y); _bfState = 2; return true; }
                Ball.SetHand(v, side);
                return true;
            }
            // on the ground = the hips are low: the simulation keeps the Tackled action long after the get-up, the body is the truth
            bool onTheGround = false;
            { var an = v.Avatar != null ? v.Avatar.Animator : null; var hb = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Hips) : null; if (hb != null) onTheGround = hb.position.y - v.transform.position.y < 0.62f; else onTheGround = h.Action == AgentAction.Tackled || h.Action == AgentAction.Fallen; }
            // the official sets off as soon as the whistle has blown and stops an arm's length from the carrier, on the side he comes from
            var crew = Crew; bool near = false;
            if (crew != null && since > OfficialCallAfter)
            {
                Vector3 cp = v.transform.position;
                Vector3 tp0; Vector3 toward = crew.TakerPosition(out tp0) ? tp0 - cp : -v.Forward; toward.y = 0f;
                toward = toward.sqrMagnitude > 1e-3f ? toward.normalized : -v.Forward;
                Vector3 spot = cp + toward * 0.95f;
                if (!_bfSent) { _bfSent = crew.SendForBall(spot); _bfSentAt = Time.time; }
                else crew.SendForBall(spot);
                Vector3 tp;
                if (_bfSent && crew.TakerPosition(out tp)) { Vector3 d = tp - cp; d.y = 0f; near = d.magnitude < 1.5f; }
            }
            if (crew != null && since > 5.2f) near = true;      // the official is late (a blocked path, a slow get-up): the ball is handed over anyway, never left in the carrier's hand
            if (since < RelaxAfter || (onTheGround && since < 1.6f)) return false;
            // loose in one hand, then into the official's hand once he is there and the carrier is on his feet
            Ball.SetRelaxed(v, side); _bfState = 1;
            v.SetTuck(0f, side);
            if (v.Motion != null) v.Motion.SetProtect(false, side);
            if (near && (!onTheGround || since > 5.2f) && crew != null)
            {
                Vector3 fwd; var hand = crew.TakerHand(out fwd);
                if (hand != null) { Ball.SetOfficial(hand, fwd); _bfState = 3; }
            }
            return true;
        }
    }
}
