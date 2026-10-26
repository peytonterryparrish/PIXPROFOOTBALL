using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11.0 QB audit (flag <c>-pixQbChoreoPhase flow</c>): CPU-vs-CPU passing plays through the real PlayStage, no user input, logging the quarterback frame by frame for the owner's note 8 / 10 / 9 measures:
    /// stance compliance (ball at the chest in both hands, shoulders square within 20 degrees of the read, not the run cycle), pocket foot slide, crossed feet, the release frame (ball to throw hand), the stride foot and front-foot direction,
    /// hip-then-shoulder order, follow-through, the stance after the throw. Output: flow_summary.txt, flow_frames_[name].csv, strips/flow_[name]_[cam].png.
    /// </summary>
    public sealed partial class QbChoreoRunner
    {
        bool _flowUc;
        sealed class FScn { public string Name; public Play Off; public Play Def; public int Seed; public bool Strip; }

        struct FFr
        {
            public float T, SimT, Speed, ChestYaw, HipsYaw, ReadErr, BallChestH, BallChestD, BallHeadD, OffHandD, ThrowHandD, CrossGap, SlideL, SlideR, YawRFoot, YawLFoot, TgtFaceErr, HandToOppHip, TorsoLean, ReadRel;
            public string Mc; public float McT, McSync;      // v0.13.0 QB-MOCAP: the mocap window on screen, its clip time and the clip time of its key moment
            public bool UcDrop; public Vector3 HipsPos, RootPos, HandPos; public int Frame; public QbPhase State; public AgentAction Act; public bool Takeoff, HasBall, PassOut, PlantL, PlantR; public string Seq; public Vector3 ToeL, ToeR;
        }

        sealed class FRes { public FScn S; public List<FFr> F = new List<FFr>(); public int SnapIdx = -1, RelIdx = -1, TakeoffIdx = -1; public int Target = -1; public string Outcome = ""; public float End; public float TargetDirYaw; public bool Lefty; }

        List<FScn> FlowScenarios()
        {
            var plays = _book.AllPlays.Where(p => p.Side == PlaySide.Offense).ToList();
            Func<Play, QbAssignment> qa = p => p.Assignments != null && p.Assignments.Length > 0 ? p.Assignments[0] as QbAssignment : null;
            Func<QbAction, int, int, List<Play>> sel = (act, sg, steps) => plays.Where(p => qa(p) != null && qa(p).Action == act && (sg < 0 || qa(p).Shotgun == (sg == 1)) && (steps < 0 || qa(p).DropbackSteps == steps) && p.Category != PlayCategory.Kneel).ToList();
            var defs = _book.AllPlays.Where(p => p.Side == PlaySide.Defense).ToList();
            var blitz = defs.Where(p => p.Category == PlayCategory.Blitz && p.FormationName == "4-3 Base").ToList(); if (blitz.Count == 0) blitz = defs.Where(p => p.Category == PlayCategory.Blitz).ToList();
            var zone = defs.Where(p => p.Category == PlayCategory.ZoneCoverage && p.FormationName == "4-3 Base").ToList(); if (zone.Count == 0) zone = defs.Where(p => p.Category == PlayCategory.ZoneCoverage).ToList();
            var man = defs.Where(p => p.Category == PlayCategory.ManCoverage && p.FormationName == "4-3 Base").ToList(); if (man.Count == 0) man = defs.Where(p => p.Category == PlayCategory.ManCoverage).ToList();
            var l = new List<FScn>();
            Action<string, List<Play>, int, List<Play>, int, int, bool> add = (n, ps, pi, ds, di, seed, strip) => { if (ps.Count == 0 || ds.Count == 0) { _report.AppendLine("flow " + n + ": no play"); return; } l.Add(new FScn { Name = n, Off = ps[pi % ps.Count], Def = ds[di % ds.Count], Seed = seed, Strip = strip }); };
            var uc5 = sel(QbAction.DropbackPass, 0, 5); var uc7 = sel(QbAction.DropbackPass, 0, 7); var uc3 = sel(QbAction.DropbackPass, 0, 3);
            var sgD = sel(QbAction.DropbackPass, 1, -1); var sgQ = sel(QbAction.QuickPass, 1, -1); var ro = sel(QbAction.Rollout, 0, -1); var bo = sel(QbAction.Boot, 0, -1); var pa = sel(QbAction.PlayActionFake, 0, -1);
            add("clean_uc5_a", uc5, 0, zone, 0, 4101, true);
            add("clean_uc5_b", uc5, 3, zone, 2, 4102, false);
            add("clean_uc3", uc3, 0, man, 1, 4103, false);
            add("clean_sg_a", sgD, 0, zone, 1, 4104, true);
            add("clean_sg_b", sgD, 2, man, 0, 4105, false);
            add("quick_sg", sgQ, 0, man, 2, 4106, false);
            add("pa_uc", pa, 0, zone, 3, 4107, true);
            add("rollout", ro, 0, zone, 0, 4108, true);
            add("boot", bo, 0, zone, 1, 4109, false);
            add("blitz_uc7_a", uc7, 0, blitz, 0, 4110, true);
            add("blitz_uc7_b", uc7, 1, blitz, 1, 4111, false);
            add("blitz_sg_a", sgD, 1, blitz, 2, 4112, true);
            add("blitz_sg_b", sgD, 3, blitz, 3, 4113, false);
            add("blitz_uc5", uc5, 1, blitz, 4, 4114, false);
            add("blitz_sg_c", sgD, 4, blitz, 5, 4115, false);
            add("blitz_sg_d", sgQ, 1, blitz, 6, 4116, false);
            add("blitz_uc5_b", uc5, 2, blitz, 7, 4117, false);
            add("blitz_sg_e", sgD, 5, blitz, 8, 4118, false);
            { var hoU = sel(QbAction.Handoff, 0, -1); var hoS = sel(QbAction.Handoff, 1, -1); var kn = plays.Where(p => p.Category == PlayCategory.Kneel).ToList(); var sp = plays.Where(p => p.Category == PlayCategory.Spike).ToList();
              add("pa_b", pa, 1, zone, 5, 4301, true); add("pa_c", pa, 2, man, 0, 4302, true); add("pa_d", pa, 3, zone, 7, 4303, false);
              add("handoff_uc", hoU, 0, zone, 0, 4201, true); add("handoff_sg", hoS, 0, zone, 1, 4202, true); add("kneel", kn, 0, zone, 0, 4203, true); add("spike", sp, 0, zone, 0, 4204, true); }
            return l;
        }

        IEnumerator FlowAudit(bool strips)
        {
            var scs = FlowScenarios(); string only = QbChoreoAudit.Arg("-pixQbChoreoOnly", ""); if (only.Length > 0) { var fl = only.Split(','); scs = scs.Where(s => Array.IndexOf(fl, s.Name) >= 0).ToList(); }
            var rs = new List<FRes>();
            foreach (var s in scs)
            {
                FRes r = null;
                yield return RunFlow(s, null, null, x => r = x);
                rs.Add(r); WriteFlowCsv(r);
                if (strips && s.Strip) foreach (var cam in new[] { "Side", "Behind" }) yield return RunFlow(s, r, cam, null);
            }
            WriteFlowSummary(rs);
        }

        FFr FlowSample(PlaySimulation sim, float t, int fi)
        {
            var a = sim.Agents[0]; var v = _stage.Views[0];
            var f = new FFr { T = t, SimT = sim.Time, Frame = fi, State = a.QbState, Act = a.Action, Speed = a.Speed * 0.9144f, HasBall = sim.Ball.Holder == 0 && sim.Ball.State == BallState.Held, Takeoff = sim.QbTakeoffIntent, PassOut = sim.Ball.State == BallState.Pass, UcDrop = _flowUc && a.QbState == QbPhase.Dropback, SlideL = -1f, SlideR = -1f, CrossGap = 9f, ReadErr = -1f };
            var b = GetBones(v); if (b == null) return f;
            var frame = _stage.Frame;
            Vector3 north = frame.DirToUnity(new Vec2(0f, 1f)); float northYaw = Mathf.Atan2(north.x, north.z) * Mathf.Rad2Deg;
            float gy = v.transform.position.y;
            Vector3 acrossH = b.RUL.position - b.LUL.position, acrossS = b.RUA.position - b.LUA.position;
            f.HipsYaw = Mathf.DeltaAngle(northYaw, BodyHeading.YawOfAcross(acrossH)); f.ChestYaw = Mathf.DeltaAngle(northYaw, BodyHeading.YawOfAcross(acrossS));
            // feet
            Vector3 ltP = b.LT != null ? b.LT.position : b.LF.position, rtP = b.RT != null ? b.RT.position : b.RF.position;
            float lth = ltP.y - gy, rth = rtP.y - gy;
            float lc = Mathf.Min(lth, b.LF.position.y - gy - 0.09f), rc = Mathf.Min(rth, b.RF.position.y - gy - 0.09f);
            bool plantL = b.PlantL ? lc < 0.07f : lc < 0.04f, plantR = b.PlantR ? rc < 0.07f : rc < 0.04f;
            f.PlantL = plantL; f.PlantR = plantR; f.ToeL = ltP; f.ToeR = rtP;
            if (b.HavePrev)
            {
                Vector3 dl = ltP - b.PrevLT; dl.y = 0f; Vector3 dr = rtP - b.PrevRT; dr.y = 0f; Vector3 dla = b.LF.position - b.PrevLF; dla.y = 0f; Vector3 dra = b.RF.position - b.PrevRF; dra.y = 0f;
                if (plantL && b.PlantL) f.SlideL = Mathf.Min(dl.magnitude, dla.magnitude) / Dt;
                if (plantR && b.PlantR) f.SlideR = Mathf.Min(dr.magnitude, dra.magnitude) / Dt;
            }
            b.PrevLT = ltP; b.PrevRT = rtP; b.PrevLF = b.LF.position; b.PrevRF = b.RF.position; b.PlantL = plantL; b.PlantR = plantR; b.HavePrev = true;
            // crossed feet: the right ankle should be to the right of the left ankle in the hips' frame (positive = fine)
            { Vector3 across = (b.RUL.position - b.LUL.position); across.y = 0f; if (across.sqrMagnitude > 1e-4f) { across.Normalize(); Vector3 rl = b.RF.position - b.LF.position; rl.y = 0f; f.CrossGap = Vector3.Dot(rl, across);
                    // travelling: the feet must not cross the line of travel (an open-hipped drop moves along a diagonal of the hips: the order across the hips is not the test)
                    if (a.Speed > 0.9f) { Vector3 vd = frame.DirToUnity(a.Vel.Norm()); vd.y = 0f; Vector3 perp = Vector3.Cross(Vector3.up, vd.normalized); float sgn = Vector3.Dot(across, perp); if (Mathf.Abs(sgn) > 0.3f) f.CrossGap = Vector3.Dot(rl, perp) * Mathf.Sign(sgn); } } }
            f.HipsPos = b.Hips.position; f.RootPos = v.transform.position;
            f.YawRFoot = FootYaw(b.RF, b.RT, northYaw); f.YawLFoot = FootYaw(b.LF, b.LT, northYaw);
            // ball against the body
            Vector3 bp = _stage.Ball != null ? _stage.Ball.Position : Vector3.zero;
            var th = v.ThrowHandBone; var oh = v.OffHandBone;
            if (th != null) { f.ThrowHandD = Vector3.Distance(bp, th.position); f.HandPos = th.position; }
            if (oh != null) f.OffHandD = Vector3.Distance(bp, oh.position);
            if (b.Chest != null) { f.BallChestH = bp.y - b.Chest.position.y; Vector3 dd = bp - b.Chest.position; f.BallChestD = dd.magnitude; }
            if (b.Head != null) f.BallHeadD = Vector3.Distance(bp, b.Head.position);
            // the read
            int tid = sim.QbLookTargetId;
            if (tid >= 0 && tid < 22)
            {
                Vector3 to = _stage.WorldPos(tid) - v.transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.25f) { float rel = Mathf.DeltaAngle(northYaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg); f.ReadRel = rel; f.ReadErr = Mathf.Abs(Mathf.DeltaAngle(f.ChestYaw, Mathf.Clamp(rel, -20f, 20f))); }
            }
            // throwing hand against the opposite hip (the follow-through)
            { var oppHip = v.Avatar.Animator.GetBoneTransform(v.IsLefty ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg); if (th != null && oppHip != null) f.HandToOppHip = Vector3.Distance(th.position, oppHip.position); }
            // the targeted receiver turns to the ball
            if (sim.Ball.State == BallState.Pass && sim.Ball.Intended > 0 && sim.Ball.Intended < 22)
            {
                var tv = _stage.Views[sim.Ball.Intended]; if (tv != null) { Vector3 toB = bp - tv.transform.position; toB.y = 0f; if (toB.sqrMagnitude > 0.04f) f.TgtFaceErr = Vector3.Angle(tv.Forward, toB); }
            }
            f.Seq = v.QbSeqName;
            f.Mc = v.QbMocapName; f.McT = v.QbMocapClipTime; f.McSync = v.QbMocapSyncTime;
            return f;
        }

        static float FootYaw(Transform ankle, Transform toe, float northYaw)
        {
            if (ankle == null || toe == null) return 0f;
            Vector3 d = toe.position - ankle.position; d.y = 0f; if (d.sqrMagnitude < 1e-5f) return 0f;
            return Mathf.DeltaAngle(northYaw, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
        }

        IEnumerator RunFlow(FScn s, FRes dry, string cam, Action<FRes> done)
        {
            var sim = NewSim(s.Off, s.Def, Scrim(35, 1, 10), s.Seed);
            var frame = new PlayFrame(true);
            { var qa0 = s.Off.Assignments != null && s.Off.Assignments.Length > 0 ? s.Off.Assignments[0] as QbAssignment : null; _flowUc = qa0 != null && !qa0.Shotgun && qa0.Action != QbAction.Rollout && qa0.Action != QbAction.Boot; }
            _stage.NextBeginHuddle = false;
            _stage.Begin(sim, frame, KitA, KitB, ++_playIndex); Prep();
            var res = new FRes { S = s };
            var tiles = new List<Texture2D>();
            List<float> marks = null; int mi = 0;
            if (dry != null) marks = FlowMarks(dry);
            float t = 0f; int fi = 0; float ready = -1f;
            while (t < 16f)
            {
                sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; t += Dt; fi++;
                res.F.Add(FlowSample(sim, t, fi));
                if (!_stage.BreakInProgress && t > 0.5f) { if (ready < 0f) ready = t; if (t > ready + 0.35f) break; }
            }
            float cadLen = MatchDirector.CadenceSeconds(s.Seed);
            _stage.BeginCadence(cadLen); float ct = 0f;
            while (ct < cadLen) { sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; t += Dt; ct += Dt; fi++; res.F.Add(FlowSample(sim, t, fi)); }
            _stage.EndCadence();
            sim.Tick(Dt, new ControlInput { Snap = true }, -1); _stage.Present(Dt); yield return null; t += Dt; fi++;
            res.SnapIdx = res.F.Count; res.F.Add(FlowSample(sim, t, fi)); float tSnap = t;
            float after = 0f; bool relSeen = false; float tAfterRel = 0f;
            while (sim.Phase != PlayPhase.Dead && after < 10f)
            {
                sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; t += Dt; after += Dt; fi++;
                var f = FlowSample(sim, t, fi); res.F.Add(f);
                if (res.RelIdx < 0 && sim.Ball.State == BallState.Pass && sim.Ball.Thrower == 0) { res.RelIdx = res.F.Count - 1; res.Target = sim.Ball.Intended; relSeen = true; }
                if (res.TakeoffIdx < 0 && f.Takeoff) res.TakeoffIdx = res.F.Count - 1;
                if (relSeen) tAfterRel += Dt;
                if (cam != null && marks != null && mi < marks.Count && t - tSnap >= marks[mi]) { tiles.Add(StripShotAt(frame, 0, cam, sim)); mi++; }
                if (cam != null && marks != null && mi >= marks.Count) break;
                if (relSeen && tAfterRel > 1.6f && cam == null) break;
            }
            res.End = t; res.Lefty = _stage.Views[0] != null && _stage.Views[0].IsLefty;
            { var r = sim.Result; res.Outcome = r != null ? r.Type.ToString() + (r.Penalty != null ? "+flag" : "") : "?"; }
            if (cam != null) Sheet(tiles, 4, _out + "/strips/flow_" + s.Name + "_" + cam + ".png");
            _stage.End(); yield return null;
            if (done != null) done(res);
        }

        List<float> FlowMarks(FRes r)
        {
            var l = new List<float>(); float snapT = r.F[r.SnapIdx].T;
            if (r.RelIdx > 0)
            {
                float tr = r.F[r.RelIdx].T - snapT;
                foreach (float d in new[] { -1.4f, -0.9f, -0.55f, -0.38f, -0.26f, -0.14f, -0.05f, 0f, 0.1f, 0.22f, 0.4f, 0.7f }) l.Add(tr + d);
            }
            else
            {
                float last = r.F[r.F.Count - 1].T - snapT;
                for (int i = 0; i < 12; i++) l.Add(0.4f + (last - 0.4f) * i / 11f);
            }
            return l.Where(x => x >= 0f).Distinct().OrderBy(x => x).ToList();
        }

        void WriteFlowCsv(FRes r)
        {
            var sb = new StringBuilder("hipsDy,frame,t,state,action,seq,takeoff,hasBall,passOut,speedMs,hipsYaw,chestYaw,readRel,readErr,ballChestH,ballChestD,ballHeadD,offHandD,throwHandD,crossGap,slideL,slideR,plantL,plantR,yawRFoot,yawLFoot,handToOppHip,tgtFaceErr\n");
            for (int fi0 = 0; fi0 < r.F.Count; fi0++)
            { var f = r.F[fi0]; float hdy = fi0 > 0 ? ((f.HipsPos - r.F[fi0 - 1].HipsPos) - (f.RootPos - r.F[fi0 - 1].RootPos)).magnitude : 0f; sb.Append(F(hdy)).Append(',');
                sb.Append(f.Frame).Append(',').Append(F(f.T)).Append(',').Append(f.State).Append(',').Append(f.Act).Append(',').Append(f.Seq).Append(',').Append(f.Takeoff ? 1 : 0).Append(',').Append(f.HasBall ? 1 : 0).Append(',').Append(f.PassOut ? 1 : 0).Append(',').Append(F(f.Speed)).Append(',').Append(F(f.HipsYaw)).Append(',').Append(F(f.ChestYaw)).Append(',').Append(F(f.ReadRel)).Append(',').Append(F(f.ReadErr)).Append(',').Append(F(f.BallChestH)).Append(',').Append(F(f.BallChestD)).Append(',').Append(F(f.BallHeadD)).Append(',').Append(F(f.OffHandD)).Append(',').Append(F(f.ThrowHandD)).Append(',').Append(F(f.CrossGap)).Append(',').Append(F(f.SlideL)).Append(',').Append(F(f.SlideR)).Append(',').Append(f.PlantL ? 1 : 0).Append(',').Append(f.PlantR ? 1 : 0).Append(',').Append(F(f.YawRFoot)).Append(',').Append(F(f.YawLFoot)).Append(',').Append(F(f.HandToOppHip)).Append(',').Append(F(f.TgtFaceErr)).Append('\n'); }
            File.WriteAllText(_out + "/flow_frames_" + r.S.Name + ".csv", sb.ToString());
        }

        /// <summary>Stance compliance: the quarterback holds the ball (sim), is in the pocket-type phase, has not taken off, the ball is at the chest (not above it), both hands on it, the chest within 20 degrees of the read, not the run cycle.</summary>
        static bool StanceEligible(FFr f) { return f.HasBall && !f.Takeoff && (f.State == QbPhase.Set || f.State == QbPhase.Scramble || (f.State == QbPhase.Dropback && !f.UcDrop && f.Seq != "Secure")) && f.Seq != "Secure"; }
        static bool Compliant(FFr f)
        {
            if (f.Seq == "Loco" || f.Seq == "None") return false;
            if (f.BallChestH > 0.16f || f.BallChestD > 0.45f || f.OffHandD > 0.38f || f.ThrowHandD > 0.38f) return false;
            if (f.ReadErr >= 0f && f.ReadErr > 20f) return false;
            if (f.ReadErr < 0f && Mathf.Abs(f.ChestYaw) > 20f) return false;
            return true;
        }

        void WriteFlowSummary(List<FRes> rs)
        {
            var sb = new StringBuilder("# QB flow audit v0.11.0 (60 Hz, real PlayStage, CPU vs CPU, huddle off)\n\n");
            var faceErr = new List<float>(); var mcSync = new List<float>(); int mcThrows = 0, poseThrows = 0; var mcClips = new HashSet<string>(); var mcFrames = 0; var allFrames = 0;      // v0.13.0 QB-MOCAP
            var all = new List<FFr>(); var pocket = new List<FFr>(); var move = new List<FFr>();
            int hipJumps = 0, hipJumpsEarly = 0, crossEvents = 0, releases = 0; var relGaps = new List<float>(); var relGaps2 = new List<float>(); var strideOk = 0; var strideN = 0; var frontToe = new List<float>(); var backToe = new List<float>(); var handSync = new List<float>(); var frontYaw = new List<float>(); var sepList = new List<float>(); var followList = new List<float>(); var postStance = new List<float>(); var slides = new List<float>(); var dropSlides = new List<float>();
            foreach (var r in rs)
            {
                sb.AppendLine("## " + r.S.Name + ": " + r.S.Off.Name + " (" + r.S.Off.FormationName + ") vs " + r.S.Def.Name + "  outcome " + r.Outcome);
                var af = r.F.Skip(Math.Max(0, r.SnapIdx)).ToList();
                var el = af.Where(StanceEligible).ToList();
                int ok = el.Count(Compliant);
                var mv = el.Where(x => x.Speed > 1.0f).ToList(); int okm = mv.Count(Compliant);
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "STANCE: eligible frames {0}, compliant {1} ({2:F1}%); while moving >1 m/s: {3} frames, compliant {4} ({5:F1}%); locomotion-cycle frames {6}; ball-at-ear frames {7}", el.Count, ok, 100.0 * ok / Math.Max(1, el.Count), mv.Count, okm, 100.0 * okm / Math.Max(1, mv.Count), el.Count(x => x.Seq == "Loco"), el.Count(x => x.BallChestH > 0.16f)));
                { var why = new Dictionary<string, int>(); foreach (var x in el) { if (Compliant(x)) continue; string k = x.Seq == "Loco" || x.Seq == "None" ? "locomotion" : (x.BallChestH > 0.16f ? "ball high" : (x.OffHandD > 0.38f || x.ThrowHandD > 0.38f ? "hand off ball" : (x.BallChestD > 0.45f ? "ball far" : "chest yaw vs read"))); int c; why.TryGetValue(k, out c); why[k] = c + 1; } if (why.Count > 0) sb.AppendLine("   non-compliant by: " + string.Join(", ", why.Select(kv => kv.Key + " " + kv.Value).ToArray())); }
                all.AddRange(el); if (el.Count > 0) { pocket.AddRange(el.Where(x => x.Speed <= 1.0f)); move.AddRange(mv); }
                var tk = af.Where(x => x.Takeoff).ToList(); if (tk.Count > 0) sb.AppendLine("TAKEOFF: " + tk.Count + " frames (first at " + (r.TakeoffIdx >= 0 ? (r.F[r.TakeoffIdx].T - r.F[r.SnapIdx].T).ToString("0.00") : "?") + " s after the snap), seq during takeoff " + string.Join("/", tk.Select(x => x.Seq).Distinct().ToArray()));
                // phases
                var segs = new List<string>(); string last = null; int n = 0; foreach (var x in af) { string k = x.State + "/" + x.Seq; if (k != last) { if (last != null) segs.Add(last + "(" + (n * Dt).ToString("0.00") + "s)"); last = k; n = 0; } n++; } if (last != null) segs.Add(last + "(" + (n * Dt).ToString("0.00") + "s)");
                sb.AppendLine("SEGMENTS: " + string.Join(" > ", segs.ToArray()));
                { var mseg = new List<string>(); string lm = null; foreach (var x in af) { allFrames++; string m = string.IsNullOrEmpty(x.Mc) ? "-" : x.Mc; if (m != "-") { mcFrames++; mcClips.Add(m.Substring(m.IndexOf(':') + 1)); } if (m != lm) { mseg.Add(m); lm = m; } } sb.AppendLine("MOCAP: " + string.Join(" > ", mseg.ToArray())); }
                { int hj = 0, hjEarly = 0; var hjs = new List<string>(); for (int i = 1; i < r.F.Count; i++) { Vector3 dh = (r.F[i].HipsPos - r.F[i - 1].HipsPos) - (r.F[i].RootPos - r.F[i - 1].RootPos); if (dh.magnitude > 0.10f) { if (r.F[i].Frame < 10) { hjEarly++; continue; } hj++; if (hjs.Count < 4) hjs.Add("f" + r.F[i].Frame + " " + r.F[i].State + "/" + r.F[i].Seq + " " + dh.magnitude.ToString("0.00") + " m"); } } hipJumps += hj; hipJumpsEarly += hjEarly; sb.AppendLine("HIPS JUMPS over 10 cm in one frame (the root motion removed, frames 10 on; " + hjEarly + " more in the first 9 frames of the play, the pooled view settling): " + hj + (hjs.Count > 0 ? "  [" + string.Join("; ", hjs.ToArray()) + "]" : "")); }
                // feet: pocket slide, crossed feet
                var cs = ContactSlidesF(af, x => x.HasBall && (x.State == QbPhase.Set || x.State == QbPhase.Scramble || x.State == QbPhase.Dropback)); slides.AddRange(cs);
                sb.AppendLine("PLANTED-FOOT slide m/s (6+ frame contacts, core frames, ball in hand): " + Stat(cs));
                { var dsl = ContactSlidesF(af, x => x.HasBall && x.State == QbPhase.Dropback); dropSlides.AddRange(dsl); sb.AppendLine("DROP planted-foot slide m/s (Dropback frames, ball in hand): " + Stat(dsl)); }      // v0.13.0 QB-2
                int cross = 0; bool inC = false; int cf = 0; foreach (var x in af) { bool cr = x.HasBall && x.CrossGap < 0.04f && x.Speed > 0.3f; if (cr) { cf++; if (!inC && cf >= 3) { cross++; inC = true; } } else { cf = 0; inC = false; } }
                crossEvents += cross; sb.AppendLine("CROSSED-FEET events (ankles within 4 cm of crossing the hips' line for 3+ frames, moving): " + cross + "; min lateral ankle gap " + (af.Where(x => x.HasBall).Select(x => x.CrossGap).DefaultIfEmpty(9f).Min()).ToString("0.000") + " m");
                // the release
                if (r.RelIdx > 0)
                {
                    releases++;
                    var rf = r.F[r.RelIdx];
                    {
                        faceErr.Add(Mathf.Abs(Mathf.DeltaAngle(rf.ChestYaw, rf.ReadRel)));
                        int thr0 = -1; for (int i = Math.Max(0, r.RelIdx - 120); i <= r.RelIdx; i++) if (r.F[i].Mc != null && r.F[i].Mc.StartsWith("Throw:")) { thr0 = i; break; }
                        if (thr0 >= 0) { int pass = -1; for (int i = thr0; i < r.F.Count; i++) if (r.F[i].Mc != null && r.F[i].Mc.StartsWith("Throw:") && r.F[i].McSync > 0f && r.F[i].McT > r.F[i].McSync + 0.004f) { pass = i; break; } mcThrows++; if (pass > 0) { mcSync.Add(pass - r.RelIdx); sb.AppendLine("   MOCAP throw: " + r.F[thr0].Mc + ", clip passes its release moment " + (pass - r.RelIdx) + " frames after the sim's release"); } }
                        else poseThrows++;
                    }
                    float g0 = rf.ThrowHandD, g1 = r.RelIdx + 1 < r.F.Count ? r.F[r.RelIdx + 1].ThrowHandD : -1f, g3 = r.RelIdx + 3 < r.F.Count ? r.F[r.RelIdx + 3].ThrowHandD : -1f;
                    relGaps.Add(g0); relGaps2.Add(g3);
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "RELEASE at {0:F2} s after the snap ({1}, target {2}): ball-to-throwing-hand gap {3:F3} m on the release frame, {4:F3} next frame, {5:F3} three frames on; speed {6:F1} m/s; state before {7}", r.F[r.RelIdx].T - r.F[r.SnapIdx].T, r.F[r.RelIdx].State, r.Target, g0, g1, g3, rf.Speed, r.F[Math.Max(0, r.RelIdx - 2)].State));
                    // the arm against the release: the frame of the peak throwing-hand speed within 0.25 s before to 0.1 s after the sim's release (0 = the ball leaves exactly when the hand is fastest)
                    { int pk = -1; float ps = 0f; for (int i = Math.Max(1, r.RelIdx - 15); i <= Math.Min(r.F.Count - 1, r.RelIdx + 6); i++) { float sp = (r.F[i].HandPos - r.F[i - 1].HandPos).magnitude / Dt; if (sp > ps) { ps = sp; pk = i; } } if (pk >= 0) { handSync.Add(pk - r.RelIdx); sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "   hand speed peaks {0:F1} m/s at {1} frames from the release (negative = the arm was fastest before the ball left)", ps, pk - r.RelIdx)); } }
                    { float gnd = 9f; for (int i = Math.Max(0, r.RelIdx - 60); i <= r.RelIdx; i++) gnd = Mathf.Min(gnd, Mathf.Min(r.F[i].ToeL.y, r.F[i].ToeR.y)); bool lf0 = r.Lefty; float ftH = (lf0 ? rf.ToeR.y : rf.ToeL.y) - gnd, btH = (lf0 ? rf.ToeL.y : rf.ToeR.y) - gnd; frontToe.Add(ftH); backToe.Add(btH); sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "   front toe {0:F3} m / back toe {1:F3} m above the turf at the release", ftH, btH)); }
                    // stride foot: the foot that landed last in the 0.6 s before the release
                    int landL = -1, landR = -1; for (int i = Math.Max(1, r.RelIdx - 36); i <= r.RelIdx; i++) { if (r.F[i].PlantL && !r.F[i - 1].PlantL) landL = i; if (r.F[i].PlantR && !r.F[i - 1].PlantR) landR = i; }
                    string strideFoot = landL < 0 && landR < 0 ? "none (feet set)" : (landL > landR ? "LEFT" : "RIGHT");
                    bool lefty = r.Lefty; string expect = lefty ? "RIGHT" : "LEFT"; // the stride foot of a right-hander is the left
                    if (landL >= 0 || landR >= 0) { strideN++; if (strideFoot == expect) strideOk++; }
                    // front foot direction vs the throw
                    float aimYaw = r.F[r.RelIdx].ReadRel; Vector3 ff = Vector3.zero;
                    float frontFootYaw = lefty ? rf.YawRFoot : rf.YawLFoot; float fe = Mathf.Abs(Mathf.DeltaAngle(frontFootYaw, aimYaw)); frontYaw.Add(fe);
                    // hips before shoulders: separation (hips minus chest toward the target) at -0.12 s and at the release
                    float sepPre = SignedToward(r.F[Math.Max(0, r.RelIdx - 7)], aimYaw), sepRel = SignedToward(rf, aimYaw);
                    sepList.Add(sepPre);
                    // follow-through: throwing hand to the opposite hip 0.25 s after
                    float ftd = r.RelIdx + 15 < r.F.Count ? r.F[r.RelIdx + 15].HandToOppHip : -1f; if (ftd > 0f) followList.Add(ftd);
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "   stride foot {0} (expected {1}); front-foot yaw error to the target {2:F1} deg; hips-minus-chest toward the target {3:F1} deg 0.12 s before, {4:F1} at the release; throwing hand to the opposite hip {5:F2} m 0.25 s after", strideFoot, expect, fe, sepPre, sepRel, ftd));
                    // stance after the throw: 0.5 to 1.3 s after the release
                    var post = r.F.Skip(r.RelIdx + 30).Take(48).ToList(); if (post.Count > 10)
                    {
                        int run = post.Count(x => x.Seq == "Loco" || x.Seq == "None"); postStance.Add(100f * (post.Count - run) / post.Count);
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "   AFTER the throw (0.5-1.3 s): chest yaw abs mean {0:F1} deg, locomotion-cycle frames {1} of {2}, sim speed mean {3:F1} m/s, seq {4}", post.Average(x => Mathf.Abs(x.ChestYaw)), run, post.Count, post.Average(x => x.Speed), string.Join("/", post.Select(x => x.Seq).Distinct().ToArray())));
                    }
                    var tf = r.F.Skip(r.RelIdx).Take(60).Where(x => x.TgtFaceErr > 0f).ToList(); if (tf.Count > 0) sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "   TARGET facing error to the ball in flight: mean {0:F1} deg, last 0.3 s mean {1:F1} deg", tf.Average(x => x.TgtFaceErr), tf.Skip(Math.Max(0, tf.Count - 18)).Average(x => x.TgtFaceErr)));
                }
                sb.AppendLine();
            }
            var hdr = new StringBuilder("# TOTALS\n");
            hdr.AppendLine(string.Format(CultureInfo.InvariantCulture, "STANCE COMPLIANCE (all plays, QB holding the ball in Set / non-takeoff Scramble / Dropback after the secure): {0} eligible frames, compliant {1:F1}%; standing still (<=1 m/s) {2:F1}% of {3}; moving (>1 m/s) {4:F1}% of {5}", all.Count, 100.0 * all.Count(Compliant) / Math.Max(1, all.Count), 100.0 * pocket.Count(Compliant) / Math.Max(1, pocket.Count), pocket.Count, 100.0 * move.Count(Compliant) / Math.Max(1, move.Count), move.Count));
            hdr.AppendLine("MOCAP: " + mcClips.Count + " distinct windows on screen; frames on mocap " + mcFrames + " of " + allFrames + " after the snap; throws on mocap " + mcThrows + " vs pose layer " + poseThrows);
            hdr.AppendLine("MOCAP release sync (frames the clip passes its release moment after the sim's release; 0 / +1 = on it) " + Stat(mcSync) + "; chest to target at the release deg " + Stat(faceErr));
            hdr.AppendLine("PLANTED-FOOT slide m/s (all): " + Stat(slides));
            hdr.AppendLine("DROP planted-foot slide m/s (Dropback only): " + Stat(dropSlides));
            hdr.AppendLine("HIPS JUMPS over 10 cm (frames 10 on, all plays): " + hipJumps + "; first 9 frames of a play (pooled view settling): " + hipJumpsEarly);
            hdr.AppendLine("CROSSED-FEET events: " + crossEvents + " over " + rs.Count + " plays");
            hdr.AppendLine("RELEASES: " + releases + "; ball-to-hand gap on the release frame m " + Stat(relGaps) + "; three frames later " + Stat(relGaps2));
            hdr.AppendLine("RELEASE vs the arm: frames from the release to the peak throwing-hand speed " + Stat(handSync));
            hdr.AppendLine("FRONT (stride) toe height at the release m " + Stat(frontToe) + "; back toe " + Stat(backToe));
            hdr.AppendLine("STRIDE FOOT correct: " + strideOk + " of " + strideN + "; front-foot yaw error deg " + Stat(frontYaw) + "; hips lead the chest toward the target 0.12 s before the release deg " + Stat(sepList) + "; follow-through hand to opposite hip m " + Stat(followList) + "; after-throw non-locomotion share % " + Stat(postStance));
            File.WriteAllText(_out + "/flow_summary.txt", hdr.ToString() + "\n" + sb.ToString());
            _report.AppendLine(hdr.ToString());
        }

        static float SignedToward(FFr f, float aimYaw)
        {
            // positive when the hips have turned further toward the target than the chest has
            float h = Mathf.DeltaAngle(f.HipsYaw, aimYaw), c = Mathf.DeltaAngle(f.ChestYaw, aimYaw);
            return Mathf.Abs(c) - Mathf.Abs(h);
        }

        static List<float> ContactSlidesF(List<FFr> fr, Func<FFr, bool> pick)
        {
            var res = new List<float>();
            for (int foot = 0; foot < 2; foot++)
            {
                int i = 0;
                while (i < fr.Count)
                {
                    bool pl = foot == 0 ? fr[i].PlantL : fr[i].PlantR;
                    if (!pl) { i++; continue; }
                    int j = i; while (j + 1 < fr.Count && (foot == 0 ? fr[j + 1].PlantL : fr[j + 1].PlantR)) j++;
                    if (j - i + 1 >= 6)
                    {
                        int a = i + 2, b = j - 2; bool all = true; for (int k = a; k <= b; k++) if (!pick(fr[k])) { all = false; break; }
                        if (all && b > a) { Vector3 pa = foot == 0 ? fr[a].ToeL : fr[a].ToeR, pb = foot == 0 ? fr[b].ToeL : fr[b].ToeR; pa.y = 0f; pb.y = 0f; res.Add((pb - pa).magnitude / ((b - a) * Dt)); }
                    }
                    i = j + 1;
                }
            }
            return res;
        }
    }
}
