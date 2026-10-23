using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    public sealed partial class Choreographer
    {
        /// <summary>
        /// A late tackler piles on (the simulation logs <see cref="SimEventType.PileOn"/>): he plays the B take of a wrap pair other than the first tackler's, placed around the carrier who is already
        /// down at the pair's contact layout, and his body follows that take (he steps in, wraps, falls, gets up) instead of sliding in with the locomotion velocity.
        /// </summary>
        public ChoreoBooking OnPileOn(SimAgent joiner, SimAgent carrier)
        {
            if (_lib == null || joiner == null || carrier == null || _byAgent[joiner.Id] != null) return null;
            float cyaw = ChoreoMath.YawOf(carrier.Facing);
            float bearing = ChoreoMath.Bearing(joiner.Pos, carrier.Pos, cyaw);
            var pair = ChoosePair(PairKind.Wrap, bearing, 0.5f, 2f, carrier, joiner, null);
            if (pair == null) return null;
            float rate = GamePace.AnimationRate;
            float tc = pair.ContactT, t0 = Math.Max(0f, tc - 0.3f);
            Vec2 away = joiner.Pos - carrier.Pos;
            Vec2 dir = away.LenSq() > 1e-4f ? away.Norm() : ChoreoMath.DirOfYaw(cyaw + 90f);
            float distYd = Math.Max(0.6f, pair.Distance * ChoreoClip.MetersToYards);
            Vec2 target = carrier.Pos + dir * distYd;
            float faceYaw = ChoreoMath.YawOf(carrier.Pos - target);
            var align = ChoreoMath.Align(pair.B.Hips(tc), pair.B.YawAt(tc), target, faceYaw);
            var b = new ChoreoBooking
            {
                Kind = ChoreoKind.Tackle, PairKind = PairKind.Wrap, Phase = ChoreoPhase.Playing, A = carrier.Id, B = joiner.Id, Pair = pair, ClipA = pair.A, ClipB = pair.B,
                Align = align, Rate = rate, StartSim = _t, WarpStart = _t, StartT = t0, SyncSim = _t + (tc - t0) / rate, OwnsA = false, OwnsB = true, Joiner = true
            };
            Vec2 r0 = align.Apply(pair.B.Root(t0));
            b.ResidB = joiner.Pos - r0;
            b.SlideB = (joiner.Vel - align.RotateOnly(pair.B.RootVelocity(t0)) * rate).ClampLen(SlideMaxV);
            b.WarpOut = Math.Max(0.25f, b.ResidB.Length / 3.0f);
            b.ExitTB = Math.Max(t0 + 0.2f, Math.Min(Math.Min(pair.B.Length, MaxPlay), pair.B.SettleT > 0f ? pair.B.SettleT : pair.B.Length));
            Add(b); Own(b);
            Remember(pair.A.Name);
            Stats.PileBookings++;
            return b;
        }
    }
}
