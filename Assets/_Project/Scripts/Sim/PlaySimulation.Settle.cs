using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    public sealed partial class PlaySimulation
    {
        /// <summary>
        /// v0.9.0 (E-257 / E-276): the pre-snap alignment is written straight into the bodies (formation slots, coverage alignment, technique shifts), so two men could stand on the same
        /// spot and pop 0.4 yd apart on the first live tick. This relaxes the 22 standing bodies apart (three passes, a minimum centre spacing of the radii plus 0.05 yd) BEFORE the snap
        /// and records the settled spots as their start positions, so nothing resolves a stacked alignment during the play.
        /// </summary>
        internal void SettlePreSnapBodies()
        {
            if (Phase != PlayPhase.PreSnap || Agents[0] == null || Agents[21] == null) return;
            for (int pass = 0; pass < 4; pass++)
            {
                bool any = false;
                for (int i = 0; i < 22; i++)
                {
                    SimAgent a = Agents[i];
                    for (int j = i + 1; j < 22; j++)
                    {
                        SimAgent b = Agents[j];
                        // the quarterback under centre, the snapper / holder / kicker group and the returner stack are placed by their own routines
                        if (a.Side == b.Side && Kind == PlayKind.Scrimmage && (a.Slot == 0 || b.Slot == 0)) continue;
                        if (a.Side == b.Side && Kind != PlayKind.Scrimmage && Specials != null && (Specials.IsKickerOrHolder(a) || Specials.IsKickerOrHolder(b))) continue;
                        Vec2 d = b.Body.Pos - a.Body.Pos;
                        float min = a.Body.Radius + b.Body.Radius + 0.05f;
                        float dsq = d.LenSq();
                        if (dsq >= min * min) continue;
                        float dist = (float)Math.Sqrt(dsq);
                        Vec2 n;
                        if (dist < 1e-3f) n = new Vec2(i < j ? 1f : -1f, 0f);   // stacked exactly: the lower slot goes to the left
                        else n = d * (1f / dist);
                        float push = (min - dist) * 0.5f;
                        float ma = a.Body.Mass, mb = b.Body.Mass, tot = ma + mb;
                        float pa = Math.Min(0.8f, (min - dist) * mb / tot), pb = Math.Min(0.8f, (min - dist) * ma / tot);
                        a.Body.Pos = ClampInField(a.Body.Pos - n * pa);
                        b.Body.Pos = ClampInField(b.Body.Pos + n * pb);
                        any = true;
                    }
                }
                if (!any) break;
            }
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Agents[i];
                a.StartPos = a.Body.Pos;
                a.ClearHistory(a.Body.Pos);
            }
        }
    }
}
