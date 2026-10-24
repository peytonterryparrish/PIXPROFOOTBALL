using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.11.0 RB track (owner note 7: "mirror how running backs run with the football in real life"). The simulation decides HOW the carrier runs before the view draws it:
    ///  - which arm carries the ball: the arm away from the defenders' leverage (a back switches hands when he cuts across the field, never mid-stride without a reason);
    ///  - press and cut: a lane change of 36 degrees or more is announced (Approach: brake, shorten the step, sink), made off the OUTSIDE foot (Plant), then driven out of (Drive);
    ///  - the posture signals the view needs (press, crouch, two hands in traffic, running the sideline).
    /// Real-life basis (what each rule is built on): running-back technique coaching (ball in the arm nearest the sideline / away from the pursuit, five points of pressure, switch hands
    /// only when the threat changes sides), the "press the hole, plant, cut, burst" sequence of inside-zone and outside-zone teaching (a back slows 10-20 percent on the press, plants the
    /// outside foot, loses roughly a quarter of his speed in a 90 degree cut and is back to speed within about a second) and the jump cut (hop-step off the outside foot in front of a defender).
    /// </summary>
    internal sealed partial class CarrierAI
    {
        static Vec2 RightOf(Vec2 h) { return new Vec2(h.Y, -h.X); }

        bool StyleOn(SimAgent c)
        {
            return CarryStyleConfig.Enabled && S.Kind == PlayKind.Scrimmage && !(c.Slot == 0 && c.Side == SimSide.Offense);
        }

        /// <summary>Hook A (CarrierAI.Update, after the lane is chosen): turns a sharp lane change into Approach -> Plant -> Drive and returns the direction to steer along this tick.</summary>
        Vec2 StyleCut(SimAgent c, Vec2 dir, Vec2 guide)
        {
            if (!StyleOn(c) || c.ControlledByUser || c.Move != CarrierMove.None) { if (c.CutPhase != CarryCutPhase.None && c.Move != CarrierMove.None) c.CutPhase = CarryCutPhase.None; return dir; }
            float speed = c.Speed;
            Vec2 heading = speed > 1.5f ? c.Vel.Norm() : c.Facing;
            float now = S.Time;
            switch (c.CutPhase)
            {
                case CarryCutPhase.Approach:
                {
                    // the lane moved again by a lot, or he is slow now: the cut is called off
                    if (SimMath.AngleBetween(dir, c.CutDir) > 0.50f || speed < 2.2f) { c.CutPhase = CarryCutPhase.None; c.CutCooldownUntil = now + 0.2f; return dir; }
                    c.CutEta -= PlaySimulation.Dt;
                    c.CutDir = dir;
                    if (c.CutEta <= 0f) { c.CutPhase = CarryCutPhase.Plant; c.CutT = 0f; return c.CutDir; }
                    return c.CutHold;
                }
                case CarryCutPhase.Plant:
                {
                    c.CutT += PlaySimulation.Dt;
                    if (c.CutT >= 0.17f) c.CutPhase = CarryCutPhase.Drive;
                    return c.CutDir;
                }
                case CarryCutPhase.Drive:
                {
                    c.CutT += PlaySimulation.Dt;
                    if (c.CutT >= 0.75f) { c.CutPhase = CarryCutPhase.None; c.CutKind = CarryCutKind.None; c.CutCooldownUntil = now + 0.35f; }
                    return dir;
                }
            }
            if (speed < 3.2f || now < c.CutCooldownUntil) return dir;
            float ang = SimMath.AngleBetween(heading, dir);
            if (ang < 0.62f || ang > 3.0f) return dir;
            // ---- a cut is decided: announce it
            Vec2 right = RightOf(heading);
            float side = dir.Dot(right) >= 0f ? 1f : -1f;
            c.CutDir = dir; c.CutHold = heading;
            c.CutAngle = side * ang * SimMath.Rad2Deg;
            c.CutPlantFoot = side > 0f ? -1 : 1;
            float agi = c.Body.Agility01;
            c.CutEta = c.CutEtaTotal = SimMath.Lerp(0.18f, 0.10f, agi) * SimMath.Clamp(0.65f + speed / 9f, 0.75f, 1.15f) * (ang > 1.4f ? 1.1f : 1f);
            c.CutT = 0f;
            c.CutKind = ClassifyCut(c, heading, dir, guide, ang, side);
            c.CutPhase = CarryCutPhase.Approach;
            return heading;
        }

        CarryCutKind ClassifyCut(SimAgent c, Vec2 heading, Vec2 dir, Vec2 guide, float ang, float side)
        {
            // nearest defender ahead
            float ahead = 99f;
            for (int i = 0; i < 22; i++)
            {
                SimAgent o = S.Agents[i]; if (o.Side == c.Side || o.IsDown) continue;
                Vec2 rel = o.Pos - c.Pos; float al = rel.Dot(heading); if (al < 0.2f) continue;
                float d = rel.Length; if (d < ahead) ahead = d;
            }
            if (ang >= 1.22f && ahead < 3.6f) return CarryCutKind.Jump;
            // against the grain: the new lane goes the other way than the designed path / the flow of the pursuit
            float flow = guide.LenSq() > 0.25f ? guide.Norm().Dot(RightOf(heading)) : 0f;
            if (ang >= 1.0f && flow * side < -0.15f) return CarryCutKind.CutBack;
            // outward: toward the sideline he is nearer to
            float toLine = c.Pos.X < FieldGeometry.CenterX ? -1f : 1f;
            if (dir.X * toLine > 0.35f && Math.Abs(heading.X) < 0.5f) return CarryCutKind.Bounce;
            return CarryCutKind.Plant;
        }

        /// <summary>Hook B (CarrierAI.Update, after the speed cap): the press before a hole or a defender (and the brake of an announced cut). Returns the cap to use.</summary>
        float StyleCap(SimAgent c, float cap, bool pastLos)
        {
            if (!StyleOn(c)) { c.CarryPress = 0f; return cap; }
            Vec2 heading = c.Speed > 1.5f ? c.Vel.Norm() : c.Facing;
            float ahead = 99f;
            for (int i = 0; i < 22; i++)
            {
                SimAgent o = S.Agents[i]; if (o.Side == c.Side || o.IsDown || o.Body.FallenTime > 0.35f) continue;
                Vec2 rel = o.Pos - c.Pos; float al = rel.Dot(heading);
                if (al < 0.3f || al > 4.8f || Math.Abs(rel.Dot(RightOf(heading))) > 2.1f) continue;
                if (al < ahead) ahead = al;
            }
            float target = ahead < 4.8f ? SimMath.Clamp01((4.8f - ahead) / 2.8f) : 0f;
            if (ahead < 2.4f) target *= SimMath.Clamp01((ahead - 1.4f) / 1.0f);      // the read happens before the man is on him: inside 1.4 yd he is committed and no longer slows (the tackle curve and the ability tests are calibrated on the contact itself)
            if (!pastLos && c.Pos.Y > S.LosY - 1.0f && c.Pos.Y < S.LosY + 1.2f) target = Math.Max(target, 0.10f);      // at the line, reading the blocks
            if (c.CutPhase == CarryCutPhase.Approach) target = Math.Max(target, 0.25f + 0.75f * (1f - SimMath.Clamp01(c.CutEta / Math.Max(0.05f, c.CutEtaTotal))));
            if (c.CutPhase == CarryCutPhase.Plant) target = 1f;
            // pressing slows a back by up to about 16 percent; fast backs read the hole at speed, slow ones stack up
            float k = 1f - (float)Math.Exp(-PlaySimulation.Dt / (target > c.CarryPress ? 0.10f : 0.22f));
            c.CarryPress += (target - c.CarryPress) * k;
            return cap * (1f - 0.075f * c.CarryPress);
        }

        /// <summary>Hook C (CarrierAI.Update): the carry arm, the crouch, two hands in traffic, the sideline tightrope.</summary>
        void StyleArm(SimAgent c, float nearDef)
        {
            if (!CarryStyleConfig.Enabled || (c.Slot == 0 && c.Side == SimSide.Offense && S.Kind == PlayKind.Scrimmage)) return;
            float now = S.Time;
            Vec2 heading = c.Speed > 1.5f ? c.Vel.Norm() : c.Facing;
            if ((c.CutPhase == CarryCutPhase.Approach && c.CutEta < 0.32f) || c.CutPhase == CarryCutPhase.Plant) heading = c.CutDir;      // the ball changes arms for the lane he is about to take
            Vec2 right = RightOf(heading);
            // leverage of the defenders around him: sum of (1 / d^2) with the sign of the side they are on
            float bal = 0f; int nid = -1; float nb = 99f; int nside = 0;
            for (int i = 0; i < 22; i++)
            {
                SimAgent o = S.Agents[i]; if (o.Side == c.Side || o.IsDown || o.Body.FallenTime > 0.35f) continue;
                Vec2 rel = o.Pos - c.Pos; float dist = rel.Length; if (dist > 7.5f) continue;
                float al = rel.Dot(heading); if (al < -1.2f) continue;
                float lat = rel.Dot(right);
                float w = 1f / ((0.6f + dist) * (0.6f + dist) * (0.6f + dist)) * (al > 0f ? 1f : 0.6f) * SimMath.Clamp01(Math.Abs(lat) / 0.55f);
                bal += w * (lat >= 0f ? 1f : -1f);
                if (dist < nb) { nb = dist; nid = i; nside = lat >= 0f ? 1 : -1; }
            }
            c.CarryThreat = nb < 7.5f ? nid : -1; c.CarryThreatSide = nside;
            int pref = bal > 0.004f ? -1 : bal < -0.004f ? 1 : 0;
            bool strong = Math.Abs(bal) > 0.035f;
            // the nearest man decides when he is clearly nearer than the rest and not straight ahead (the ball is covered from the man who is about to reach it)
            if (nid >= 0 && nb < 6.5f) { float nl = (S.Agents[nid].Pos - c.Pos).Dot(right); if (Math.Abs(nl) > 0.55f) { pref = nl > 0f ? -1 : 1; strong = nb < 3.2f; } }
            if (c.PlanMove == CarrierMove.StiffArm || c.Move == CarrierMove.StiffArm)
            {
                // the stiff arm is the free arm: the defender is on its side, the ball is in the other arm
                int sid = c.PlanThreat >= 0 ? c.PlanThreat : c.MoveThreatId;
                if (sid >= 0 && sid < 22) { float l = (S.Agents[sid].Pos - c.Pos).Dot(right); if (Math.Abs(l) > 0.25f) { pref = l > 0f ? -1 : 1; strong = true; } }
            }
            if (pref == 0)
            {
                // no one near: the arm toward the nearer sideline, with hysteresis
                float lineDir = c.Pos.X < FieldGeometry.CenterX ? -1f : 1f;      // the sideline he is nearer to is at -X / +X
                float s = new Vec2(lineDir, 0f).Dot(right);
                if (Math.Abs(s) > 0.45f) pref = s > 0f ? 1 : -1;
            }
            if (c.CarryArm == 0)
            {
                c.CarryArm = pref != 0 ? pref : (c.Pos.X < FieldGeometry.CenterX ? -1 : 1) * (RightOf(heading).X >= 0f ? 1 : -1);
                c.CarryArmPrev = c.CarryArm; c.CarryArmSince = now - 5f; c.CarryArmWant = 0;
            }
            else if (pref != 0 && pref != c.CarryArm)
            {
                if (c.CarryArmWant != pref) { c.CarryArmWant = pref; c.CarryArmWantSince = now; }
                float dwell = strong ? 0.05f : 0.18f;
                if (now - c.CarryArmWantSince >= dwell && now - c.CarryArmSince >= (strong ? 0.24f : 0.55f))
                {
                    c.CarryArmPrev = c.CarryArm; c.CarryArm = pref; c.CarryArmSince = now; c.CarryArmWant = 0;
                }
            }
            else c.CarryArmWant = 0;
            c.BallSide = -c.CarryArm;      // the sim's own BallSide is measured along Perp (to the LEFT): +1 = left arm
            // crouch: traffic ahead and the plant
            float ahead = 99f;
            for (int i = 0; i < 22; i++)
            {
                SimAgent o = S.Agents[i]; if (o.Side == c.Side || o.IsDown || o.Body.FallenTime > 0.35f) continue;
                Vec2 rel = o.Pos - c.Pos; float al = rel.Dot(heading); if (al < -0.5f || al > 4f) continue;
                if (Math.Abs(rel.Dot(right)) > 1.8f) continue;
                if (al < ahead) ahead = al;
            }
            float ct = ahead < 4f ? SimMath.Clamp01((3.6f - ahead) / 2.6f) : 0f;
            if (c.CutPhase == CarryCutPhase.Plant) ct = Math.Max(ct, 0.85f); else if (c.CutPhase == CarryCutPhase.Approach) ct = Math.Max(ct, 0.30f + 0.4f * (1f - SimMath.Clamp01(c.CutEta / Math.Max(0.05f, c.CutEtaTotal))));
            else if (c.CutPhase == CarryCutPhase.Drive) ct = Math.Max(ct, 0.45f);
            c.CarryCrouch += (ct - c.CarryCrouch) * (1f - (float)Math.Exp(-PlaySimulation.Dt / (ct > c.CarryCrouch ? 0.09f : 0.25f)));
            c.CarryTwoHands = nearDef < 1.9f && c.Move == CarrierMove.None && (c.Protecting || nb < 1.9f);
            c.CarryTight = c.Speed > 3f && FieldGeometry.DistanceToSideline(c.Pos.X) < 1.7f;
        }
    }
}
