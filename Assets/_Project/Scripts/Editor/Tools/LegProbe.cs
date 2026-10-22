using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PixFootball.EditorTools
{
   /// <summary>
   /// v0.9.0 LOCOMOTION round 3 (LOCO-016): leg quality of a running body, measured on the FINAL pose (after the animation, the stance IK, FootPlant, the action ground pass), every frame.
   ///  - knee deviation from the hip-ankle plane (the plane through hip, ankle and the direction of travel): lateral offset of the knee in cm, + = inward (valgus), and as an angle at the thigh;
   ///  - knee hinge yaw: the horizontal direction of the knee's hinge axis against the body's lateral axis (a square runner's knees bend in the line of travel; 0 = square) while the knee is bent more than 15 degrees;
   ///  - pole flips: the hinge axis turning more than 50 degrees in one frame;
   ///  - foot yaw against the direction of travel at ground contact (toe-out + / toe-in -), and its frame-to-frame change while planted;
    ///  - ankle roll (tilt of the sole about the foot long axis, degrees) and its frame-to-frame jitter; thigh / shin / foot twist muscles in degrees (the foot twist muscle turns the foot toe-in / toe-out against the shin).
   /// Pure observation: nothing here changes the pose.
   /// </summary>
   public sealed class LegProbe
   {
       public sealed class Acc
       {
           public readonly Dictionary<string, List<float>> V = new Dictionary<string, List<float>>();
           public int Frames, Flips, RollPops, YawPops;
           public void Add(string k, float v) { List<float> l; if (!V.TryGetValue(k, out l)) V[k] = l = new List<float>(); l.Add(v); }
       }

       sealed class Leg
       {
           public Transform U, L, F, T; public bool Left;
           public Vector3 PrevHinge; public bool HaveHinge; public float PrevRoll, PrevYaw; public bool PrevRollOk; public bool HavePrev, PrevStance;
           public int[] M = new int[7]; public Vector3 LocalLat = Vector3.right, LocalFwd = Vector3.forward; public bool HaveAxes;
       }

       readonly Animator _an; readonly Leg _l = new Leg { Left = true }, _r = new Leg();
       HumanPoseHandler _h; HumanPose _pose; bool _ok, _poseOk;
       static readonly string[] MuscleNames = { "Upper Leg Front-Back", "Upper Leg In-Out", "Upper Leg Twist In-Out", "Lower Leg Stretch", "Lower Leg Twist In-Out", "Foot Up-Down", "Foot Twist In-Out" };

       public LegProbe(Animator an, PixFootball.Gameplay.FootPlant plant = null)
       {
           _an = an;
           if (an == null || !an.isHuman) return;
           Bind(_l, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, "Left ");
           Bind(_r, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes, "Right ");
           _ok = _l.U != null && _l.L != null && _l.F != null && _r.U != null && _r.L != null && _r.F != null;
           try { _h = new HumanPoseHandler(an.avatar, an.transform); _poseOk = true; } catch { _poseOk = false; }
           if (plant != null && plant.Ok) { _l.HaveAxes = plant.SoleAxes(true, out _l.LocalLat, out _l.LocalFwd); _r.HaveAxes = plant.SoleAxes(false, out _r.LocalLat, out _r.LocalFwd); }
       }

       void Bind(Leg l, HumanBodyBones u, HumanBodyBones lo, HumanBodyBones f, HumanBodyBones t, string side)
       {
           l.U = _an.GetBoneTransform(u); l.L = _an.GetBoneTransform(lo); l.F = _an.GetBoneTransform(f); l.T = _an.GetBoneTransform(t);
           for (int i = 0; i < MuscleNames.Length; i++) l.M[i] = Array.IndexOf(HumanTrait.MuscleName, side + MuscleNames[i]);
       }

       static float Deg(int idx, float v)
       {
           if (idx < 0) return 0f;
           return v >= 0f ? v * HumanTrait.GetMuscleDefaultMax(idx) : -v * HumanTrait.GetMuscleDefaultMin(idx);
       }

       /// <summary>One frame. <paramref name="facing"/> = the body's forward (horizontal), <paramref name="travel"/> = the velocity direction (horizontal, may be zero), gy = turf height under the player.</summary>
       public void Sample(Acc acc, Vector3 facing, Vector3 travel, float speed, float gy)
       {
           if (!_ok || acc == null) return;
           facing.y = 0f; travel.y = 0f;
           if (facing.sqrMagnitude < 1e-4f) return;
           facing.Normalize();
           Vector3 refDir = facing;
           if (speed > 1f && travel.sqrMagnitude > 1e-4f)
           {
               float d = Vector3.Dot(travel.normalized, facing);
               if (d > 0.5f) refDir = travel.normalized; else if (d < -0.5f) refDir = -travel.normalized;     // backpedal: the body line is the reverse of the travel
           }
           Vector3 right = Vector3.Cross(Vector3.up, refDir);       // the lateral axis of the line of travel
           acc.Frames++;
           if (_poseOk) { try { _h.GetHumanPose(ref _pose); } catch { _poseOk = false; } }
           Sample(acc, _l, refDir, facing, right, gy);
           Sample(acc, _r, refDir, facing, right, gy);
            Vector3 pf = Vector3.Cross(_r.U.position - _l.U.position, Vector3.up); pf.y = 0f;
            if (pf.sqrMagnitude > 1e-4f) acc.Add("pelvis yaw off line deg", Mathf.Abs(Vector3.SignedAngle(refDir, pf, Vector3.up)));
       }

       void Sample(Acc acc, Leg g, Vector3 refDir, Vector3 facing, Vector3 right, float gy)
       {
           Vector3 H = g.U.position, K = g.L.position, A = g.F.position, T = g.T != null ? g.T.position : A + facing * 0.15f;
           float sole = Mathf.Min(T.y - gy, A.y - gy - 0.09f);
           bool stance = sole < 0.03f;
           // knee deviation from the hip-ankle plane
           Vector3 a = A - H;
           Vector3 n = Vector3.Cross(a, refDir);
           if (n.sqrMagnitude > 1e-6f)
           {
               n.Normalize(); if (Vector3.Dot(n, right) < 0f) n = -n;
               float dev = Vector3.Dot(K - H, n);              // + = toward the right of travel
               float inward = g.Left ? dev : -dev;             // + = toward the midline (knock-kneed), - = bowed out
               float thigh = Mathf.Max(0.1f, (K - H).magnitude);
               acc.Add(stance ? "knee dev cm stance" : "knee dev cm swing", Mathf.Abs(inward) * 100f);
               if (stance) { acc.Add("knee inward cm stance (signed)", inward * 100f); acc.Add("knee dev deg stance", Mathf.Atan2(Mathf.Abs(inward), thigh) * Mathf.Rad2Deg); }
           }
           // knee hinge axis
           Vector3 hinge = Vector3.Cross(K - H, A - K);
           float bend = hinge.magnitude / Mathf.Max(1e-5f, (K - H).magnitude * (A - K).magnitude);
           bool bent = bend > 0.26f;       // sin 15 degrees
           if (bent)
           {
               Vector3 hh = hinge; hh.y = 0f;
               if (hh.sqrMagnitude > 1e-6f)
               {
                   float ang = Vector3.SignedAngle(right, hh, Vector3.up);
                   if (ang > 90f) ang -= 180f; else if (ang < -90f) ang += 180f;
                   acc.Add(stance ? "knee hinge yaw deg stance" : "knee hinge yaw deg swing", Mathf.Abs(ang));
                   // knee direction (the way the knee points) against the line of travel: signed, + = knee points outward
                   Vector3 kd = K - (H + A) * 0.5f; kd.y = 0f;
                   if (kd.sqrMagnitude > 1e-6f)
                   {
                       float k = Vector3.SignedAngle(refDir, kd, Vector3.up);       // + = knee points to the right of travel
                       acc.Add(stance ? "knee pointing off the line of travel deg stance" : "knee pointing off the line of travel deg swing", Mathf.Abs(k));
                       acc.Add("knee pointing out deg (signed, + = out, all)", g.Left ? -k : k);
                   }
               }
               if (g.HaveHinge && Vector3.Angle(hinge, g.PrevHinge) > 50f) acc.Flips++;
               g.PrevHinge = hinge; g.HaveHinge = true;
           }
           else g.HaveHinge = false;
           // foot yaw against the direction of travel
           float yaw = float.NaN;
           { float yy; if (g.HaveAxes) { if (PixFootball.Gameplay.FootPlant.YawOf(g.F.rotation, g.LocalLat, refDir, out yy)) yaw = yy; } else { Vector3 td = T - A; td.y = 0f; if (td.magnitude > 0.06f) yaw = Vector3.SignedAngle(refDir, td, Vector3.up); } }
           if (!float.IsNaN(yaw))
           {
               // + = toes to the right
               float outDeg = g.Left ? -yaw : yaw;
               acc.Add("foot yaw deg all", Mathf.Abs(yaw));
               if (stance)
               {
                   acc.Add("foot toe-out deg contact (abs)", Mathf.Abs(outDeg)); acc.Add("foot toe-out deg contact (signed, + = out)", outDeg);
                   if (g.HavePrev && g.PrevStance) { float dy = Mathf.Abs(Mathf.DeltaAngle(g.PrevYaw, yaw)); acc.Add("foot yaw change deg/frame in contact", dy); if (dy > 8f) acc.YawPops++; }
               }
           }
           // ankle roll = tilt of the sole about the foot's long axis (0 = flat); foot twist = the humanoid foot twist muscle (toe-in / toe-out of the foot against the shin)
           float roll = 0f;
           if (g.HaveAxes)
           {
                float rr; Vector3 fw; bool okr = PixFootball.Gameplay.FootPlant.RollOf(g.F.rotation, g.LocalLat, g.LocalFwd, out rr, out fw);
                if (okr)
                {
                    roll = rr;
                    acc.Add("ankle roll deg (abs, all)", Mathf.Abs(roll));
                    if (stance) acc.Add("ankle roll deg stance (abs)", Mathf.Abs(roll));
                    if (g.HavePrev && g.PrevRollOk) { float dr = Mathf.Abs(Mathf.DeltaAngle(g.PrevRoll, roll)); acc.Add("ankle roll jitter deg/frame", dr); if (dr > 6f) acc.RollPops++; }
                }
                g.PrevRollOk = okr;
           }
           if (_poseOk)
           {
               var m = _pose.muscles;
               float ft = Deg(g.M[6], g.M[6] >= 0 ? m[g.M[6]] : 0f), ut = Deg(g.M[2], g.M[2] >= 0 ? m[g.M[2]] : 0f), lt = Deg(g.M[4], g.M[4] >= 0 ? m[g.M[4]] : 0f);
               acc.Add("thigh twist deg (abs)", Mathf.Abs(ut)); acc.Add("shin twist deg (abs)", Mathf.Abs(lt)); acc.Add("foot twist deg (abs)", Mathf.Abs(ft));
           }
           g.PrevRoll = roll; g.PrevYaw = float.IsNaN(yaw) ? g.PrevYaw : yaw; g.HavePrev = true; g.PrevStance = stance && !float.IsNaN(yaw);
       }

       // ------------------------------------------------------------------------------------------ report
       static float Mean(List<float> l) { return l == null || l.Count == 0 ? float.NaN : l.Average(); }
       static float P(List<float> l, float p) { if (l == null || l.Count == 0) return float.NaN; var s = l.OrderBy(x => x).ToList(); return s[Mathf.Clamp((int)(p * (s.Count - 1) + 0.5f), 0, s.Count - 1)]; }
       static string F(float v) { return float.IsNaN(v) ? "-" : v.ToString("0.0", CultureInfo.InvariantCulture); }
       static string MP(Acc a, string k) { List<float> l; a.V.TryGetValue(k, out l); return F(Mean(l)) + " / " + F(P(l, 0.95f)); }
       static string M1(Acc a, string k) { List<float> l; a.V.TryGetValue(k, out l); return F(Mean(l)); }

       public static string Table(IDictionary<string, Acc> rows)
       {
           var sb = new StringBuilder();
           sb.AppendLine("| case | frames | knee dev stance cm (mean / p95) | knee inward cm (signed mean) | knee hinge yaw stance deg | knee points off line deg stance | pole flips | toe-out at contact deg (abs mean / p95) | toe-out signed | yaw change in contact deg/frame (p95) | ankle roll stance deg (mean / p95) | roll jitter deg/frame (mean / p95) | roll pops >6 | yaw pops >8 | thigh / shin / foot twist muscle deg | pelvis off line deg (mean / p95) |");
           sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
           foreach (var kv in rows.OrderBy(x => x.Key))
           {
               var a = kv.Value; List<float> yc; a.V.TryGetValue("foot yaw change deg/frame in contact", out yc);
               sb.AppendLine("| " + kv.Key + " | " + a.Frames + " | " + MP(a, "knee dev cm stance") + " | " + M1(a, "knee inward cm stance (signed)") + " | " + MP(a, "knee hinge yaw deg stance") + " | " + MP(a, "knee pointing off the line of travel deg stance") + " | " + a.Flips + " | " + MP(a, "foot toe-out deg contact (abs)") + " | " + M1(a, "foot toe-out deg contact (signed, + = out)") + " | " + F(P(yc, 0.95f)) + " | " + MP(a, "ankle roll deg stance (abs)") + " | " + MP(a, "ankle roll jitter deg/frame") + " | " + a.RollPops + " | " + a.YawPops + " | " + M1(a, "thigh twist deg (abs)") + " / " + M1(a, "shin twist deg (abs)") + " / " + M1(a, "foot twist deg (abs)") + " | " + MP(a, "pelvis yaw off line deg") + " |");
           }
           return sb.ToString();
       }
   }
}
