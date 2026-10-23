using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.10.0 BODIES: after the whistle one official comes to collect the ball from the carrier (the ball travels from the carrier's hand to the official's hand, see <see cref="BallFollower.SetOfficial"/>).</summary>
    public sealed partial class MatchOfficials
    {
        Person _taker; Vector3 _takerSpot;

        /// <summary>True while an official is on his way to (or standing with) the ball.</summary>
        public bool HasTaker { get { return _taker != null; } }

        /// <summary>Sends the quickest free official (distance plus the rest of the signal he is making) to <paramref name="spot"/>. Returns false when nobody is within reach.</summary>
        public bool SendForBall(Vector3 spot)
        {
            _takerSpot = new Vector3(spot.x, 0f, spot.z);
            if (_taker != null) return true;
            Person best = null; float bestEta = 1e9f;
            foreach (var p in _crew)
            {
                if (p == null || p.Tr == null || !p.Tr.gameObject.activeSelf) continue;
                Vector3 d = p.Tr.position - spot; d.y = 0f;
                float eta = d.magnitude / 5.5f + Mathf.Max(0f, p.SignalT) * 0.6f;
                if (eta < bestEta) { bestEta = eta; best = p; }
            }
            if (best == null || bestEta > 6f) return false;
            _taker = best;
            if (_taker.SignalT > 0.4f) { _taker.SignalT = 0f; _taker.Signal = null; }      // the signal is cut short, he goes for the ball
            return true;
        }

        /// <summary>Where the collecting official stands (world), or false when none was sent.</summary>
        public bool TakerPosition(out Vector3 pos)
        {
            pos = Vector3.zero; if (_taker == null || _taker.Tr == null) return false;
            pos = _taker.Tr.position; return true;
        }

        /// <summary>The collecting official's right hand and the way he faces (the ball goes there).</summary>
        public Transform TakerHand(out Vector3 forward)
        {
            forward = Vector3.forward;
            if (_taker == null || _taker.Tr == null) return null;
            forward = _taker.Tr.forward;
            var an = _taker.Tr.GetComponentInChildren<Animator>(true);
            if (an != null && an.isHuman) { var h = an.GetBoneTransform(HumanBodyBones.RightHand); if (h != null) return h; }
            return _taker.Tr;
        }

        /// <summary>Stand down (the next play re-spots the crew).</summary>
        public void ReleaseTaker() { _taker = null; }
    }
}
