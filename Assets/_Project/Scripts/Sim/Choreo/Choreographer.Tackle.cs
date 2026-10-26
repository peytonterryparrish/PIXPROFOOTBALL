using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    public sealed partial class Choreographer
    {
        // ---- forecast of the tackle: one slot per defender
        struct TackleForecast
        {
            public bool Active;
            public int Carrier;
            public float Eta, MadeAt;
            /// <summary>Contact time predicted when the ETA first dropped under 0.55 s (the forecast the audit scores against the real contact).</summary>
            public float Pred05At;
            public float Bearing, Closing;
            public Vec2 SyncPos;
            public PairKind Kind;
            public ChoreoPair Plan;
        }
        readonly TackleForecast[] _tf = new TackleForecast[22];

        /// <summary>Public view of a defender's forecast: seconds until he reaches the carrier, 0.. when none (-1).</summary>
        public float TackleEta(int defender) { return defender >= 0 && defender < 22 && _tf[defender].Active ? _tf[defender].Eta : -1f; }
        public Vec2 TackleSyncPos(int defender) { return _tf[defender].SyncPos; }
        public ChoreoPair TacklePlan(int defender) { return defender >= 0 && defender < 22 && _tf[defender].Active ? _tf[defender].Plan : null; }

        void ForecastTackles()
        {
            SimAgent c = S.Carrier;
            for (int i = 0; i < 22; i++) _tf[i].Active = false;
            if (c == null || c.State == AgentState.Down || c.Body.FallenTime > 0f || _byAgent[c.Id] != null || S.EndPending) return;
            float cr = c.Body.Radius;
            for (int i = 0; i < 22; i++)
            {
                SimAgent d = S.Agents[i];
                if (d.Side == c.Side || _byAgent[i] != null) continue;
                if (d.State == AgentState.Down || d.Body.FallenTime > 0f || d.Body.GetUpTime > 0f) continue;
                if (d.State == AgentState.Engaged || d.State == AgentState.Tackling) continue;
                Vec2 rel = c.Pos - d.Pos;
                float reach = d.Body.Radius + cr + 0.42f + 0.18f * Rating01(d.R.Tackling);
                // the pursuer is accelerating toward his intent: use the average of his velocity and what he wants
                Vec2 vd = d.Want.Length > 0.5f ? (d.Vel + d.Want) * 0.5f : d.Vel;
                float eta = ChoreoMath.TimeToReach(rel, c.Vel - vd, reach);
                if (eta > Horizon) continue;
                var f = _tf[i];
                if (f.Carrier != c.Id || f.MadeAt < _t - 0.25f) f = default(TackleForecast);       // a stale or different forecast starts over
                f.Active = true; f.Carrier = c.Id; f.Eta = eta;
                f.MadeAt = _t;
                f.SyncPos = c.Pos + c.Vel * eta;
                if (eta <= 0.55f && f.Pred05At <= 0f) f.Pred05At = _t + eta;
                // what the contact will probably be: the tackler's bearing seen from the carrier at the contact, the closing speed, the kind
                Vec2 cAt = c.Pos + c.Vel * eta, dAt = d.Pos + vd * eta;
                Vec2 cdir = c.Speed > 1.75f ? c.Vel.Norm() : c.Facing;
                f.Bearing = ChoreoMath.Bearing(dAt, cAt, ChoreoMath.YawOf(cdir));
                Vec2 toC = (cAt - dAt).Norm();
                f.Closing = Math.Max(0f, (vd - c.Vel).Dot(toC));
                bool qbSack = c.Slot == 0 && c.Side == SimSide.Offense && S.OffAI != null && S.OffAI.IsPassPlay && !S.PassThrown;
                f.Kind = qbSack ? PairKind.Sack : (d.Move == CarrierMove.Dive ? PairKind.Dive : PairKind.Wrap);
                if (eta <= 0.8f && _lib != null)
                {
                    f.Plan = ChoosePair(f.Kind, f.Bearing, c.Speed * 0.9144f, f.Closing * 0.9144f, c, d, f.Plan);
                    SteerPursuer(d, c, ref f);
                }
                _tf[i] = f;
                Stats.Forecasts++;
            }
        }

        // The pursuer's last 0.8 s are bent so he arrives on the carrier's far side of the planned approach (bearing of the planned take), never more than a few tenths of a yard and a bounded velocity change.
        void SteerPursuer(SimAgent d, SimAgent c, ref TackleForecast f)
        {
            if (DebugNoSteer || DebugNoSteerTackle) return;
            if (f.Kind == PairKind.Sack && !SteerSacks) return;      // a pass rusher is not bent toward the quarterback: bending a rush shortens the time to throw (it cost 0.3 yd per pass, 3 sigma)
            if (f.Plan == null || f.Eta < 0.06f || d.ControlledByUser) return;
            if (d.Move != CarrierMove.None) return;
            Vec2 cdir = c.Speed > 1.75f ? c.Vel.Norm() : c.Facing;
            // where the tackler wants to be relative to the carrier: on the bearing of the planned take, at the contact distance of the take
            float wantYaw = ChoreoMath.YawOf(cdir) + f.Plan.Bearing;
            float distYd = Math.Max(0.45f, f.Plan.Distance * ChoreoClip.MetersToYards);
            Vec2 cAt = c.Pos + c.Vel * f.Eta;
            Vec2 want = cAt + ChoreoMath.DirOfYaw(wantYaw) * distYd;
            Vec2 dAt = d.Pos + (d.Want.Length > 0.5f ? (d.Vel + d.Want) * 0.5f : d.Vel) * f.Eta;
            Vec2 err = want - dAt;
            // only correct the part that is a small bend of a path that already works (a defender running at the wrong angle is not turned around)
            float el = err.Length;
            if (el < 0.05f || el > 1.6f) return;
            float gain = ChoreoMath.Smooth01(1f - f.Eta / 0.8f);
            Vec2 dv = err * (1f / Math.Max(0.12f, f.Eta)) * gain * 0.55f * PursuerSteerScale;
            float cap = 1.8f * PursuerSteerScale;                                   // yards per second of bend, at most
            if (dv.Length > cap) dv = dv.Norm() * cap;
            d.Want = d.Want + dv;
            if (dv.Length > Stats.MaxSteerRate) Stats.MaxSteerRate = dv.Length;
            Stats.SteeredTicks++;
        }

        /// <summary>
        /// The reach a defender needs to attempt the tackle: when the choreographer has him booked for this carrier the contact happens at the take's layout (the hips of the pair about 0.55 m apart)
        /// instead of the generic arm's length, so the clip's contact frame and the simulated contact are the same moment.
        /// </summary>
        public float TackleReach(SimAgent d, SimAgent c, float reach)
        {
            // (v0.10.0: the reach stays the simulation's: shortening it by 0.28 yd delayed every tackle from the side and from behind by up to 0.3 s and added 0.45 yd to the average run. The take starts at the clip moment whose hips are as far apart as the bodies are, instead.)
            return reach;
        }

        // ===================================================================================================
        // Choosing the take
        // ===================================================================================================
        float ScorePair(ChoreoPair p, float bearing, float carrierSpeedM, float closingM, float wantLow, float distM, int seed)
        {
            float ang = Math.Abs(ChoreoMath.DeltaAngle(p.Bearing, bearing));
            float score = -ang / 25f;
            float dv = carrierSpeedM - p.SpeedA;
            score -= dv > 0f ? dv * 0.55f : -dv * 0.2f;
            score -= Math.Abs(p.ClosingB - closingM) * 0.12f;
            if (wantLow > 0f) score -= Math.Abs(p.LowB - wantLow) * 1.2f;
            if (distM > 0f && p.Distance > 0f) score -= Math.Abs(p.Distance - distM) * 0.8f;
            int used = 0; for (int i = 0; i < _recent.Count; i++) if (_recent[i] == p.A.Name) used++;      // v0.13 (ANIMLIB-BULK): a take is worth less for every time it was booked lately (no-repeat bag; was a flat 0.9 for the last 6)
            score -= Math.Min(used, 3) * 0.7f;
            score += ChoreoMath.Hash01(seed, p.A.Name.Length, ChoreoMath.HashString(p.A.Name)) * 0.9f;      // v0.13 wave 2 (BULK-2): 0.45 -> 0.9, more of the booked pool is drawn (24 of 70 tackle pairs in the wave-2 game)
            return score;
        }

        /// <summary>Best take for the situation. <paramref name="keep"/> (the take already planned) stays unless another one is clearly better (no flip-flopping while the forecast is refined).</summary>
        ChoreoPair ChoosePair(PairKind kind, float bearing, float carrierSpeedM, float closingM, SimAgent c, SimAgent d, ChoreoPair keep)
        {
            if (_lib == null) return null;
            float wantLow = kind == PairKind.Dive ? 0.9f : (kind == PairKind.Wrap || kind == PairKind.Sack ? 0.15f : 0f);
            float distM = (RadiusM(c) + RadiusM(d)) * 1.15f;
            int seed = (c.Id * 131 + d.Id) * 7 + 1000;
            ChoreoPair best = null; float bs = float.MinValue, keepScore = float.MinValue;
            var pairs = _lib.Pairs;
            for (int i = 0; i < pairs.Count; i++)
            {
                var p = pairs[i];
                if (p.Kind != kind || p.A == null || p.B == null || p.A.Count < 2 || p.B.Count < 2) continue;
                float sc = ScorePair(p, bearing, carrierSpeedM, closingM, wantLow, distM, seed);
                if (p == keep) keepScore = sc;
                if (sc > bs) { bs = sc; best = p; }
            }
            if (keep != null && keep.Kind == kind && keepScore > bs - 0.8f) return keep;
            return best;
        }

        static PairKind KindFor(TackleType t, bool sack)
        {
            if (sack) return PairKind.Sack;
            switch (t)
            {
                case TackleType.BigHit: return PairKind.BigHit;
                case TackleType.Shoestring: case TackleType.DiveTackle: return PairKind.Dive;
                case TackleType.BrokenTackle: return PairKind.Broken;
                default: return PairKind.Wrap;
            }
        }

        // ===================================================================================================
        // The contact: book the take
        // ===================================================================================================
        /// <summary>
        /// Called by the tackle resolver the moment the contact is decided (the Tackle / BrokenTackle event is logged). Picks the take from what the simulation decided, places the pair on the field
        /// at the contact and hands the two bodies to the take. Returns the booking, or null when no suitable take exists or either body is already owned.
        /// </summary>
        public ChoreoBooking OnContact(SimAgent tackler, SimAgent carrier, TackleType type, bool sack, bool broken, bool diving, float closingYd, Vec2 carrierVel0)
        {
            if (_lib == null || tackler == null || carrier == null) return null;
            if (_byAgent[tackler.Id] != null || _byAgent[carrier.Id] != null) return null;
            PairKind kind;
            bool qbPre = carrier.Slot == 0 && carrier.Side == SimSide.Offense && !S.PassIsThrown && S.Kind == PlayKind.Scrimmage;
            if (broken) kind = type == TackleType.Shoestring ? PairKind.Broken : (qbPre ? PairKind.SackBroken : PairKind.Broken);
            else kind = KindFor(type, sack);
            Vec2 cdir = carrierVel0.Length > 1.75f ? carrierVel0.Norm() : carrier.Facing;
            float cyaw = ChoreoMath.YawOf(cdir);
            float bearing = ChoreoMath.Bearing(tackler.Pos, carrier.Pos, cyaw);
            float spM = carrierVel0.Length * 0.9144f, clM = Math.Max(0f, closingYd * 0.9144f);
            var tf = _tf[tackler.Id];
            ChoreoPair pair = null;
            ChoreoPair plan = tf.Active && tf.Plan != null && tf.Plan.Kind == kind ? tf.Plan : null;      // the planned take gets a bonus: the approach was bent toward it
            pair = ChoosePairFit(kind, bearing, spM, clM, carrier, tackler, carrierVel0, cyaw, plan);
            if (pair == null && kind != PairKind.Wrap) { kind = PairKind.Wrap; pair = ChoosePairFit(kind, bearing, spM, clM, carrier, tackler, carrierVel0, cyaw, null); }
            if (pair == null) return null;
            if (tf.Active && tf.Pred05At > 0f) Stats.EtaError.Add(Math.Abs(tf.Pred05At - _t));
            var b = BuildPairBooking(pair, kind, carrier, tackler, carrierVel0, cyaw);
            Remember(pair.A.Name);
            Stats.TackleBookings++;
            return b;
        }

        static readonly float[] AxisBlends = { 0.6f, 0.9f, 0.3f };
        struct Placement { public ChoreoRigid Align; public float T0, LeadSim, Cost, RateIn; public Vec2 ResidA, ResidB, SlideA, SlideB; }
        /// <summary>QA: no lead-in time warp (-pixNoLeadWarp).</summary>
        public static bool DebugNoLeadWarp = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoLeadWarp") >= 0;

        /// <summary>Places a pair on the field at the contact: the clip starts where its hips are as far apart as the bodies are now, the pair is aligned with the carrier anchored (the alignment tries three blends of "his heading" and "the axis between the two" and keeps the one that moves the bodies least), and what is left is the residual that is warped out and the momentum that is slid through.</summary>
        Placement Place(ChoreoPair p, SimAgent carrier, SimAgent tackler, Vec2 carrierVel, float cyaw, float etaSim = -1f)
        {
            float rate = GamePace.AnimationRate;
            float tc = p.ContactT;
            float simDistM = (tackler.Pos - carrier.Pos).Length * 0.9144f;
            float t0 = tc, bestErr = float.MaxValue;
            for (float t = Math.Max(0f, tc - 0.5f); t <= tc + 1e-4f; t += 0.025f)
            {
                float dd = (p.A.Hips(t) - p.B.Hips(t)).Length;
                float err = Math.Abs(dd - simDistM);
                if (err < bestErr) { bestErr = err; t0 = t; }
            }
            float lead = Math.Min(0.36f, Math.Max(0.06f, tc - t0));
            t0 = Math.Max(0f, tc - lead);
            float leadSim = lead / rate;
            if (etaSim > 0f) { lead = Math.Min(tc, Math.Max(0.05f, etaSim * rate)); t0 = Math.Max(0f, tc - lead); leadSim = etaSim; }      // a duel: the contact is the ball's arrival, a forecast time away
            // the lead-in warp: the bodies arrive at the sim speed, the take's lead-in is authored at its own (a runner at 6.3 m/s against a take at 4.2 m/s slid 2 m/s under his feet): play the lead-in at the ratio
            float rateIn = rate;
            if (etaSim <= 0f && !DebugNoLeadWarp && tc - t0 > 0.04f)
            {
                float cA = (p.A.Root(tc) - p.A.Root(t0)).Length / (tc - t0) * rate, cB = (p.B.Root(tc) - p.B.Root(t0)).Length / (tc - t0) * rate;
                float sA = carrierVel.Length * 0.9144f, sB = tackler.Vel.Length * 0.9144f, num = 0f; int cnt = 0;
                if (cA > 2.0f && sA > 2.0f) { num += sA / cA; cnt++; }
                if (cB > 2.0f && sB > 2.0f) { num += sB / cB; cnt++; }
                if (cnt > 0) { float k = SimMath.Clamp(num / cnt, 0.85f, 1.4f); if (Math.Abs(k - 1f) > 0.03f) { rateIn = rate * k; leadSim = lead / rateIn; } }
            }
            // aligned at the START of the take: both bodies are where the take puts them at t0 (the layout of the pair at the contact is then the authored one, whatever the speeds)
            Vec2 predA = carrier.Pos, predB = tackler.Pos;
            Vec2 hA = p.A.Hips(t0), hB = p.B.Hips(t0);
            Placement best = default(Placement); best.Cost = float.MaxValue;
            foreach (float w in AxisBlends)
            {
                var align = ChoreoMath.AlignPair(hB, hA, p.A.YawAt(t0), predB, predA, cyaw, w);
                Vec2 rA = align.Apply(p.A.Root(t0)), rB = align.Apply(p.B.Root(t0));
                Vec2 residA = carrier.Pos - rA, residB = tackler.Pos - rB;
                float cost = Math.Max(residA.Length, residB.Length);
                if (cost < best.Cost)
                {
                    Vec2 vA = align.RotateOnly(p.A.RootVelocity(t0)) * rateIn, vB = align.RotateOnly(p.B.RootVelocity(t0)) * rateIn;
                    best = new Placement { Align = align, T0 = t0, LeadSim = leadSim, RateIn = rateIn != rate ? rateIn : 0f, Cost = cost, ResidA = residA, ResidB = residB, SlideA = (carrierVel - vA).ClampLen(SlideMaxV), SlideB = (tackler.Vel - vB).ClampLen(SlideMaxV) };
                }
            }
            return best;
        }

        /// <summary>The best take for the situation, judged by how well it fits the bodies as they are (the placement residual counts like a bearing mismatch of 40 degrees per yard).</summary>
        ChoreoPair ChoosePairFit(PairKind kind, float bearing, float carrierSpeedM, float closingM, SimAgent c, SimAgent d, Vec2 carrierVel, float cyaw, ChoreoPair prefer)
        {
            if (_lib == null) return null;
            float wantLow = kind == PairKind.Dive ? 0.9f : (kind == PairKind.Wrap || kind == PairKind.Sack ? 0.15f : 0f);
            float distM = (RadiusM(c) + RadiusM(d)) * 1.15f;
            int seed = (c.Id * 131 + d.Id) * 7 + 1000;
            ChoreoPair best = null; float bs = float.MinValue;
            var pairs = _lib.Pairs;
            for (int i = 0; i < pairs.Count; i++)
            {
                var p = pairs[i];
                if (p.Kind != kind || p.A == null || p.B == null || p.A.Count < 2 || p.B.Count < 2) continue;
                float sc = ScorePair(p, bearing, carrierSpeedM, closingM, wantLow, distM, seed);
                if (sc < bs - 6f) continue;      // cannot win even with a perfect fit
                sc -= 1.6f * Place(p, c, d, carrierVel, cyaw).Cost;
                if (p == prefer) sc += 0.8f;
                if (sc > bs) { bs = sc; best = p; }
            }
            return best;
        }

        ChoreoBooking BuildPairBooking(ChoreoPair p, PairKind kind, SimAgent carrier, SimAgent tackler, Vec2 carrierVel, float cyaw, float etaSim = -1f)
        {
            var pl = Place(p, carrier, tackler, carrierVel, cyaw, etaSim);
            float rate = GamePace.AnimationRate, tc = p.ContactT, t0 = pl.T0;
            var b = new ChoreoBooking
            {
                Kind = ChoreoKind.Tackle, PairKind = kind, Phase = ChoreoPhase.Playing, A = carrier.Id, B = tackler.Id, Pair = p, ClipA = p.A, ClipB = p.B,
                Align = pl.Align, Rate = rate, RateIn = pl.RateIn, SyncClipT = tc, StartSim = _t, WarpStart = _t, StartT = t0, SyncSim = _t + pl.LeadSim, OwnsA = true, OwnsB = true,
                ResidA = pl.ResidA, ResidB = pl.ResidB, SlideA = pl.SlideA, SlideB = pl.SlideB
            };
            float worst = Math.Max(b.ResidA.Length, b.ResidB.Length);
            b.WarpOut = Math.Max(0.25f, worst / 3.0f);
            // exits: the take's settle moment (upright again), the carrier who runs on after a broken tackle is let go earlier
            float cap = MaxPlay;
            b.ExitTA = Math.Min(Math.Min(p.A.Length, cap), p.A.SettleT > 0f ? p.A.SettleT : p.A.Length);
            b.ExitTB = Math.Min(Math.Min(p.B.Length, cap), p.B.SettleT > 0f ? p.B.SettleT : p.B.Length);
            if (kind == PairKind.Broken || kind == PairKind.SackBroken || kind == PairKind.Truck || kind == PairKind.Contested || kind == PairKind.Collision || kind == PairKind.BigHitShrug)
                b.ExitTA = Math.Min(b.ExitTA, tc + (kind == PairKind.Contested ? 0.35f : 0.5f) * rate);
            if (kind == PairKind.Collision) b.ExitTB = Math.Min(b.ExitTB, tc + 0.9f * rate);
            b.ExitTA = Math.Max(b.ExitTA, t0 + 0.2f); b.ExitTB = Math.Max(b.ExitTB, t0 + 0.2f);
            Add(b); Own(b);
            return b;
        }
    }
}
