using System;
using System.Collections.Generic;
using PixFootball.Core;
using Side = PixFootball.Core.Team;

namespace PixFootball.League
{
    internal sealed partial class GameSim
    {
        private void SetTempo(PlayResult r, bool hurry, bool milk)
        {
            if (hurry) r.RunoffSeconds = _rng.Range(7f, 14f);
            else if (milk) r.RunoffSeconds = _rng.Range(36f, 40f);
            else r.RunoffSeconds = _rng.Range(SimTuning.RunoffMin, SimTuning.RunoffMax);
        }

        // ============================================================ passing
        private PlayOutcome DoPass(SimUnit off, SimUnit def, bool hurry, bool milk)
        {
            var o = new PlayOutcome { Passer = off.QB };
            SimPlayer qb = off.QB;
            float eo = off.Edge + off.OffBonus, ed = def.Edge + def.DefBonus;
            int ytg = _gs.YardsToGoal;
            int spot = _gs.BallSpot;

            float press = (def.PassRush + ed) - (off.PassBlock + eo) + SimTuning.PressRef;
            float esc = 1f - Clamp((qb.R.Awareness - 72f) * 0.004f + (qb.QbMob - 55f) * 0.003f, -0.2f, 0.35f);
            float coverTime = ((def.CovCb + def.CovS) * 0.5f + ed - SimTuning.CoverRefAvg) * 0.006f;
            float pSack = SimTuning.SackBase * (float)Math.Exp(SimTuning.SackK * press) * esc * (1f + coverTime);
            if (hurry) pSack *= 0.9f;
            pSack = Clamp(pSack, 0.012f, 0.22f);
            float pPress = Clamp(SimTuning.PressureBase * (float)Math.Exp(SimTuning.PressureK * press), 0.05f, 0.65f);
            bool pressure = _rng.NextFloat() < pPress;
            o.Pressure = pressure;

            // ---- sack
            if (_rng.NextFloat() < pSack)
            {
                int lost = (int)Math.Round(3.2f + Exp(3.3f));
                lost = Clamp(lost, 1, 16);
                o.K = OutKind.Sack;
                o.Sacker = PickRusher(def, true);
                o.Pressure = true;
                o.Yards = -lost;
                bool fum = _rng.Chance(0.08f);
                if (fum)
                {
                    bool lostFum = _rng.Chance(0.55f);
                    o.Fumble = true; o.FumbleLost = lostFum; o.Forcer = o.Sacker; o.Tackler = o.Sacker;
                    if (lostFum)
                    {
                        int ret = (int)Math.Round(Exp(2.5f));
                        o.R = PlayResult.FumbleLost(-lost, ret, false, 5f);
                    }
                    else o.R = PlayResult.FumbleRecovered(-lost, 5f);
                }
                else o.R = PlayResult.Sack(lost, 5.5f, qb.P.Id, o.Sacker.P.Id);
                o.R.PasserId = qb.P.Id;
                SetTempo(o.R, hurry, milk);
                return o;
            }

            // ---- scramble
            float scrambleP = 0.045f + Math.Max(0f, qb.QbMob - 55f) * 0.0045f;
            if (pressure && _rng.NextFloat() < scrambleP * 2.2f)
            {
                int y = (int)Math.Round(_rng.Gaussian(5.2f, 3.5f) + Exp(1.5f) * (qb.QbMob > 70 ? 1.6f : 0.8f));
                y = Clamp(y, -2, 40);
                return BuildRun(off, def, qb, y, hurry, milk, true);
            }

            // ---- throw type
            float[] tp = { 0.19f, 0.44f, 0.27f, 0.10f };
            float wrOpen = (off.WR[0].Open + off.WR[1].Open + off.WR[2].Open) / 3f;
            float deepAdj = (wrOpen - 70f) * 0.002f + (qb.R.ThrowPower - 74f) * 0.0013f;
            tp[3] += deepAdj; tp[1] -= deepAdj * 0.5f; tp[2] -= deepAdj * 0.5f;
            if (_gs.Distance >= 10) { tp[3] += 0.05f; tp[2] += 0.02f; tp[1] -= 0.05f; tp[0] -= 0.02f; }
            else if (_gs.Distance <= 3) { tp[3] -= 0.07f; tp[2] -= 0.08f; tp[1] += 0.11f; tp[0] += 0.04f; }
            if (ytg < 25) { tp[3] = 0f; tp[2] *= 0.5f; }
            if (ytg < 12) { tp[2] = 0f; tp[0] *= 0.6f; }
            int type = Pick(tp, 4);

            // ---- target selection
            float[] w = new float[9];
            for (int i = 0; i < 9; i++)
            {
                float m;
                int grp = i <= 3 ? 0 : (i <= 5 ? 1 : 2);
                switch (type)
                {
                    case 0: m = grp == 0 ? 0.35f : (grp == 1 ? 1.1f : 3.0f); break;
                    case 1: m = 1f; break;
                    case 2: m = grp == 0 ? 1.1f : (grp == 1 ? 1.0f : 0.5f); break;
                    default: m = grp == 0 ? 1.5f : (grp == 1 ? 0.45f : 0.1f); break;
                }
                w[i] = off.TargetW[i] * m;
            }
            int ti = Pick(w, 9);
            SimPlayer target = off.Targets[ti];
            o.Target = target;

            SimPlayer covDef; float cover;
            if (ti <= 2) { covDef = def.CB[ti]; cover = covDef.CbCover; }
            else if (ti == 3) { covDef = def.S[1]; cover = covDef.SCover; }
            else if (ti <= 5) { if (_rng.Chance(0.5f)) { covDef = def.LB[_rng.Range(0, 3)]; cover = covDef.LbCover; } else { covDef = def.S[_rng.Range(0, 2)]; cover = covDef.SCover; } }
            else { covDef = def.LB[_rng.Range(0, 3)]; cover = covDef.LbCover; }
            if (type == 3) cover = cover * 0.8f + def.CovS * 0.2f;
            cover += ed;
            float m2 = (target.Open + eo) - cover - SimTuning.MatchRef;

            float basep = type == 0 ? SimTuning.CompBaseCheck : (type == 1 ? SimTuning.CompBaseShort : (type == 2 ? SimTuning.CompBaseMed : SimTuning.CompBaseDeep));
            float pc = basep + (qb.R.ThrowAccuracy - 74f) * SimTuning.AccK + (qb.R.Awareness - 72f) * 0.0015f
                       + m2 * SimTuning.MatchK + (target.Catch - 70f) * SimTuning.CatchK;
            if (type == 3) pc += (qb.R.ThrowPower - 74f) * 0.0025f;
            if (pressure) pc -= SimTuning.PressureCompPenalty * (type == 0 ? 0.6f : 1f);
            pc = Clamp(pc, 0.08f, 0.95f);

            o.Defender = covDef;
            int air;
            switch (type)
            {
                case 0: air = _rng.Range(-3, 4); break;
                case 1: air = _rng.Range(2, 10); break;
                case 2: air = _rng.Range(10, 18); break;
                default: air = 18 + (int)Exp(6.5f); if (air > 50) air = 50; break;
            }
            if (air > ytg) air = ytg;
            o.Air = air;

            if (_rng.NextFloat() >= pc)
            {
                // ---- incomplete or intercepted
                float pi = SimTuning.IntBase * (float)Math.Exp(-(qb.R.Awareness - 72f) * 0.012f - (qb.R.ThrowAccuracy - 74f) * 0.008f - m2 * 0.012f + (pressure ? 0.5f : 0f));
                pi *= type == 3 ? 1.4f : (type == 0 ? 0.45f : 1f);
                if (hurry) pi *= 1.15f;
                pi = Clamp(pi, 0.005f, 0.25f);
                if (_rng.NextFloat() < pi)
                {
                    SimPlayer picker = _rng.Chance(0.62f) ? covDef : (_rng.Chance(0.5f) ? def.S[_rng.Range(0, 2)] : def.CB[_rng.Range(0, 3)]);
                    int catchPoint = 100 - (spot + air);
                    o.K = OutKind.Int;
                    o.Defender = picker;
                    if (catchPoint <= 0)
                    {
                        o.R = PlayResult.Intercept(air, 0, false, 7f);
                        o.R.Touchback = true;
                    }
                    else
                    {
                        int ret = (int)Math.Round(Exp(11f));
                        ret = Clamp(ret, 0, 95);
                        bool td = catchPoint + ret >= 100 || _rng.Chance(0.01f);
                        if (td) ret = 100 - catchPoint;
                        o.RetYards = ret; o.Td = td;
                        o.R = PlayResult.Intercept(air, ret, td, 7f);
                    }
                    o.R.PasserId = qb.P.Id; o.R.ReceiverId = target.P.Id; o.R.DefenderId = picker.P.Id;
                    o.R.Turnover = true;
                    SetTempo(o.R, hurry, milk);
                    return o;
                }
                o.K = OutKind.PassInc;
                if (!_rng.Chance(0.28f)) o.Defender = null;
                o.R = PlayResult.Incomplete(4f, qb.P.Id, target.P.Id);
                return o;
            }

            // ---- completion
            float typeF = type == 0 ? 1.45f : (type == 1 ? 1.15f : (type == 2 ? 0.92f : 0.6f));
            float skillF = Clamp(target.Yac / 70f, 0.55f, 1.55f);
            float yacMean = SimTuning.YacMean * typeF * skillF * (1f + (m2 * 0.006f));
            int yac = (int)Math.Round(Exp(Math.Max(0.8f, yacMean)));
            if (yac > 60) yac = 60;
            bool oob = _rng.Chance(type >= 2 ? 0.2f : 0.11f) || (hurry && _rng.Chance(0.18f));
            if (oob) yac = Math.Min(yac, _rng.Range(0, 3));
            int yards = air + yac;
            if (yards < -3) yards = -3;
            if (yards > ytg) yards = ytg;
            bool tdPass = spot + yards >= 100;
            o.K = OutKind.PassComp;
            o.Yards = yards; o.Td = tdPass;
            float pFum = Clamp(0.0085f - (target.R.Carrying - 70f) * 0.0001f, 0.002f, 0.02f);
            float time = oob ? 4.5f : Clamp(5f + yards * 0.05f, 5f, 9f);
            if (!tdPass && _rng.NextFloat() < pFum)
            {
                bool lostFum = _rng.Chance(0.5f);
                o.Fumble = true; o.FumbleLost = lostFum;
                o.Forcer = covDef; o.Tackler = covDef;
                if (lostFum)
                {
                    int ret = (int)Math.Round(Exp(3f));
                    o.R = PlayResult.FumbleLost(yards, ret, false, time);
                }
                else o.R = PlayResult.FumbleRecovered(yards, time);
                o.R.PasserId = qb.P.Id; o.R.ReceiverId = target.P.Id;
                SetTempo(o.R, hurry, milk);
                return o;
            }
            o.R = PlayResult.Pass(yards, time, oob, qb.P.Id, target.P.Id);
            o.R.Touchdown = tdPass;
            if (!tdPass)
            {
                o.Tackler = _rng.Chance(0.45f) ? covDef : PickTackler(def, yards >= 10);
                o.R.TacklerId = o.Tackler.P.Id;
            }
            SetTempo(o.R, hurry, milk);
            return o;
        }

        // ============================================================ rushing
        private PlayOutcome DoRun(SimUnit off, SimUnit def, bool hurry, bool milk)
        {
            int ytg = _gs.YardsToGoal;
            int dist = _gs.Distance;
            bool shortYd = dist <= 2 || ytg <= 2;

            // ---- who carries
            SimPlayer rusher;
            float r = _rng.NextFloat();
            float qbRun = 0.012f + Math.Max(0f, off.QB.QbMob - 58f) * 0.0022f;
            bool sneak = shortYd && dist <= 1 && _rng.Chance(0.14f);
            if (sneak || r < qbRun) rusher = off.QB;
            else if (r < qbRun + (shortYd ? 0.08f : 0.025f)) rusher = off.FB;
            else
            {
                float share = 0.64f;
                if (_gs.Quarter >= 4 && _gs.PossessionScoreDiff > 0) share = 0.55f;
                rusher = _rng.NextFloat() < share ? off.RB1 : off.RB2;
            }

            float eo = off.Edge + off.OffBonus, ed = def.Edge + def.DefBonus;
            float blockEdge = (off.RunBlock + eo) - (def.RunStop + ed) + SimTuning.RunRef;
            float skill;
            if (rusher == off.QB) skill = (rusher.QbMob - 62f) * 0.8f;
            else if (rusher == off.FB) skill = rusher.RbRun - 76f;
            else skill = rusher.RbRun - 72f;

            float mu = SimTuning.RunMeanBase + SimTuning.RunBlockK * blockEdge + SimTuning.RunSkillK * skill;
            float pStuff = Clamp(SimTuning.StuffBase - SimTuning.StuffK * blockEdge - 0.0018f * skill, 0.05f, 0.38f);
            float pBig = Clamp(SimTuning.BigRunBase + 0.0014f * blockEdge + 0.0011f * skill + (rusher.R.Speed - 80f) * 0.0004f, 0.008f, 0.12f);
            if (shortYd) { pStuff += 0.045f; pBig *= 0.35f; mu *= 0.88f; }
            if (ytg <= 8) { pBig *= 0.3f; }
            if (hurry && _gs.Distance > 6) pStuff += 0.02f;

            int yards;
            float rr = _rng.NextFloat();
            if (sneak) yards = _rng.NextFloat() < 0.8f ? 1 + (int)Exp(0.8f) : 0;
            else if (rr < pStuff) yards = _rng.Range(-3, 2);
            else if (rr < pStuff + pBig) yards = 11 + (int)Exp(10f * (0.75f + (rusher.R.Speed - 70f) * 0.012f));
            else
            {
                float m = Math.Max(1.2f, mu * 1.12f);
                yards = (int)Math.Round(Exp(m * 0.5f) + Exp(m * 0.5f) - 0.4f);
            }
            if (yards > 80) yards = 80;
            return BuildRun(off, def, rusher, yards, hurry, milk, false);
        }

        private PlayOutcome BuildRun(SimUnit off, SimUnit def, SimPlayer rusher, int yards, bool hurry, bool milk, bool scramble)
        {
            int ytg = _gs.YardsToGoal;
            var o = new PlayOutcome { K = scramble ? OutKind.Scramble : OutKind.Run, Rusher = rusher };
            if (yards > ytg) yards = ytg;
            bool td = _gs.BallSpot + yards >= 100;
            o.Yards = yards; o.Td = td;
            bool oob = !td && (_rng.Chance(0.085f + (yards > 7 ? 0.08f : 0f)) || (hurry && _rng.Chance(0.15f)));
            float time = oob ? 4f : Clamp(5f + yards * 0.06f, 4.5f, 9f);
            float pFum = Clamp(0.0105f - (rusher.R.Carrying - 70f) * 0.00014f + (scramble ? 0.004f : 0f), 0.003f, 0.03f);
            SimPlayer tackler = td ? null : PickTackler(def, yards >= 9);
            o.Tackler = tackler;
            if (!td && _rng.NextFloat() < pFum)
            {
                bool lost = _rng.Chance(0.5f);
                o.Fumble = true; o.FumbleLost = lost; o.Forcer = tackler ?? def.LB[1];
                if (lost)
                {
                    int ret = (int)Math.Round(Exp(3f));
                    o.R = PlayResult.FumbleLost(yards, ret, false, time);
                }
                else o.R = PlayResult.FumbleRecovered(yards, time);
                o.R.RusherId = rusher.P.Id;
                SetTempo(o.R, hurry, milk);
                return o;
            }
            o.R = PlayResult.Run(yards, time, oob, rusher.P.Id, tackler != null ? tackler.P.Id : 0);
            o.R.Touchdown = td;
            SetTempo(o.R, hurry, milk);
            return o;
        }
    }
}
