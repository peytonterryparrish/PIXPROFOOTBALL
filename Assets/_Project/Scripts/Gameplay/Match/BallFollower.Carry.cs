using UnityEngine;

namespace PixFootball.Gameplay
{
    public sealed partial class BallFollower
    {
        /// <summary>
        /// v0.11.0 RB (B-065): the ball rests in the carry-side palm (the catch pose, held at its start every frame) while a full-body take owns the carrier's arms (the hand-off take after the exchange, a spin, a truck):
        /// the arms of those takes are not the tucked forearm the carry pose reads, which left the ball 0.4-0.57 m from both hands for about a second after a hand-off.
        /// </summary>
        public void SetPalmHold(SimAgentView holder, int side)
        {
            if (_mode != Mode.Catch || _holder != holder) MarkAttach(0.1f, 0f);
            _mode = Mode.Catch; _holder = holder; _side = side; _modeT = 0f;
        }
    }
}
