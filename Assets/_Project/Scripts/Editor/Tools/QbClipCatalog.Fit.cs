using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using PixFootball.Art;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.13.0 QB-2: the FITTED families of the quarterback catalog. A fitted row is a take (or a window of one) that the view plays time-warped and distance-fitted to the simulated path of the same moment (the drop, the roll-out, the hand-off ride, the
    /// side-step), so it keeps ALL its travel (<see cref="QbClipTable.Entry.path"/> is the whole hips path of the window) and its planted feet are measured against the clip's own world (slide at keep = 1). The families are found BY RULE (the clip name), never by a list,
    /// so a take that is imported later (the day-2 / day-3 raw pairs: <c>QB_falling_throw_00N_AB</c>, <c>QB_Sneak_00N_AB</c>, <c>QB_cadence_00N_*</c>) lands in its family at the next <c>QbClipCatalog.BuildBatch</c> without a code change.
    /// </summary>
    public static partial class QbClipCatalog
    {
        static readonly Regex ReDropWalk = new Regex(@"\dstepdropback_walk|backpedal_?walk|dropback_walk", RegexOptions.IgnoreCase);
        static readonly Regex ReDropSettle = new Regex(@"\dstepdropback_settle|dropback_settle", RegexOptions.IgnoreCase);
        static readonly Regex ReDropStop = new Regex(@"dropback_into_stop|dropback_stop", RegexOptions.IgnoreCase);
        static readonly Regex ReSidestep = new Regex(@"^qbsidestep|sidestep(back|fwd|left|right)", RegexOptions.IgnoreCase);
        static readonly Regex ReFake = new Regex(@"fakehandoff|fake_handoff|handofffake|play_?action", RegexOptions.IgnoreCase);
        static readonly Regex ReRoll = new Regex(@"ready_to_run|run_(left|right|forward)_pass|runfoward|scramble|transitionrun|roll(out)?\b|bootleg", RegexOptions.IgnoreCase);
        static readonly Regex ReKneel = new Regex(@"kneel|take_?a_?knee|victory", RegexOptions.IgnoreCase);
        static readonly Regex ReSpike = new Regex(@"spike", RegexOptions.IgnoreCase);
        static readonly Regex ReSlide = new Regex(@"qb_?slide|slide", RegexOptions.IgnoreCase);
        static readonly Regex ReSneak = new Regex(@"sneak", RegexOptions.IgnoreCase);

        /// <summary>The fitted rows of a clip, by name. True when the clip belongs to a fitted family (the stable / snap / throw rules do not look at it again).</summary>
        static bool FitWindows(Feat f, AnimationClip c, float score, List<QbClipTable.Entry> outList, StringBuilder rej)
        {
            string n = c.name, low = n.ToLowerInvariant();
            int N = f.N, last = N - 1;
            if (ReUcDrop.IsMatch(low)) { FitDrops(f, score, outList, rej, n, false); return false; }      // the catch-and-drop takes are also snap windows (the rules below add them)
            if (ReSgGather.IsMatch(low)) { FitDrops(f, score, outList, rej, n, true); return false; }
            if (ReDropWalk.IsMatch(low)) { FitWhole(f, QbFam.DropWalk, "walk", score, outList, rej, n, 0.35f); return true; }
            if (ReDropSettle.IsMatch(low)) { FitWhole(f, QbFam.DropSettle, "settle", score, outList, rej, n, 0.35f); return true; }
            if (ReDropStop.IsMatch(low)) { FitWhole(f, QbFam.DropStop, "stop", score, outList, rej, n, 0.35f); return true; }
            if (ReSidestep.IsMatch(low) && f.N / Rate <= 3.2f) { FitWhole(f, QbFam.Sidestep, "sidestep", score, outList, rej, n, 0.45f); return true; }
            if (ReFake.IsMatch(low)) { FitFakes(f, score, outList, rej, n); return true; }
            if (ReKneel.IsMatch(low) || ReSlide.IsMatch(low) || ReSpike.IsMatch(low) || ReSneak.IsMatch(low)) { FitGround(f, low, score, outList, rej, n); return true; }
            if (ReRoll.IsMatch(low)) { FitRuns(f, score, outList, rej, n); return false; }      // the run windows are added; the throw windows of the same take are still looked for
            return false;
        }

        static void FitWhole(Feat f, string fam, string kind, float score, List<QbClipTable.Entry> outList, StringBuilder rej, string n, float slideMax)
        {
            int b = f.N - 1;
            if (f.N < 6) { rej.AppendLine(n + "," + fam + ",too short"); return; }
            float sl = Slide(f, 0, b, 1f);
            if (sl > slideMax) { rej.AppendLine(n + "," + fam + ",slide " + sl.ToString("0.00") + " (the clip's own world)"); return; }
            if (!PoseRange(f, 0, b, 0.62f, 48f)) { rej.AppendLine(n + "," + fam + ",pose out of range (hips / lean)"); return; }
            var e = Make(f, fam, kind, 0, b, 0, '?', score, false, true);
            e.yawSync = f.Chest[b]; outList.Add(e);
        }

        /// <summary>The fake hand-off: the hands (ball) reach out to the side and come back; the window is around the farthest reach (sync), the side of it in <see cref="QbClipTable.Entry.side"/>.</summary>
        static void FitFakes(Feat f, float score, List<QbClipTable.Entry> outList, StringBuilder rej, string n)
        {
            int N = f.N; var lat = new float[N]; var hd = new float[N];
            for (int i = 0; i < N; i++)
            {
                var p = f.P[i]; Vector3 hm = (p[AnimRig.LH] + p[AnimRig.RH]) * 0.5f - p[AnimRig.Hips];
                Vector3 fwd = new Vector3(Mathf.Sin(f.Hip[i] * Mathf.Deg2Rad), 0f, Mathf.Cos(f.Hip[i] * Mathf.Deg2Rad)); Vector3 right = Vector3.Cross(Vector3.up, fwd);
                lat[i] = Vector3.Dot(hm, right); hd[i] = Vector3.Dot(hm, fwd);
            }
            int gap = Mathf.RoundToInt(2.0f * Rate), last = -gap;
            for (int i = (int)(0.6f * Rate); i < N - (int)(0.5f * Rate); i++)
            {
                float m = Mathf.Abs(lat[i]); if (m < 0.34f || i - last < gap) continue;
                bool peak = true; for (int k = Mathf.Max(0, i - 8); k <= Mathf.Min(N - 1, i + 8); k++) if (Mathf.Abs(lat[k]) > m + 1e-4f) { peak = false; break; }
                if (!peak) continue;
                int a = Mathf.Max(0, i - Mathf.RoundToInt(0.7f * Rate)), b = Mathf.Min(N - 1, i + Mathf.RoundToInt(0.6f * Rate));
                while (a < i - 8 && !(f.HandsY[a] >= 0.85f && f.HandsY[a] <= 1.6f)) a++;
                float drift = Drift(f, a, b), sl = Slide(f, a, b, 1f);
                if (f.HipsY[a] < 0.70f || !PoseRange(f, a, b, 0.66f, 40f)) { rej.AppendLine(n + "@" + (i / Rate).ToString("0.00") + ",fake,pose"); continue; }
                if (sl > 0.45f || drift > 3.0f) { rej.AppendLine(n + "@" + (i / Rate).ToString("0.00") + ",fake,slide " + sl.ToString("0.00") + " drift " + drift.ToString("0.00")); continue; }
                var e = Make(f, QbFam.Fake, "fake", a, b, i, '?', score, false, true); e.side = lat[i]; outList.Add(e); last = i;
            }
        }

        /// <summary>A take of the ground families (kneel, slide, spike, sneak): the window around the low point of the hips (kneel, slide) or the downward swing of the hands (spike) or the push (sneak). A take whose actor never goes down is bad data and is left out.</summary>
        static void FitGround(Feat f, string low, float score, List<QbClipTable.Entry> outList, StringBuilder rej, string n)
        {
            int N = f.N;
            bool kneel = ReKneel.IsMatch(low), slide = ReSlide.IsMatch(low) && !kneel, spike = ReSpike.IsMatch(low), sneak = ReSneak.IsMatch(low);
            string fam = kneel ? QbFam.Kneel : slide ? QbFam.Slide : spike ? QbFam.Spike : QbFam.Sneak;
            float minH = float.MaxValue; int iMin = 0; for (int i = 0; i < N; i++) if (f.HipsY[i] < minH) { minH = f.HipsY[i]; iMin = i; }
            float maxH = 0f; for (int i = 0; i < N; i++) maxH = Mathf.Max(maxH, f.HipsY[i]);
            float motion = 0f; for (int i = 1; i < N; i++) motion += Mathf.Abs(f.HipsY[i] - f.HipsY[i - 1]) + (f.H[i] - f.H[i - 1]).magnitude;
            if (motion < 0.4f) { rej.AppendLine(n + "," + fam + ",no motion in the take (static retarget)"); return; }
            if (kneel || slide)
            {
                // the actor must really go down: hips under 0.58 m (a knee) / 0.45 m (a slide) after having stood
                float need = kneel ? 0.58f : 0.45f;
                if (minH > need || maxH - minH < 0.25f) { rej.AppendLine(n + "," + fam + ",the actor never goes down (hips min " + minH.ToString("0.00") + " max " + maxH.ToString("0.00") + ")"); return; }
                int a = iMin, b = iMin;
                while (a > 0 && f.HipsY[a] < maxH - 0.06f) a--;
                a = Mathf.Max(0, a - Mathf.RoundToInt(0.3f * Rate));
                while (b < N - 1 && f.HipsY[b] < minH + 0.5f * (maxH - minH) && b - iMin < Mathf.RoundToInt(2.5f * Rate)) b++;
                b = Mathf.Min(N - 1, b + Mathf.RoundToInt(0.4f * Rate));
                var e = Make(f, fam, kneel ? "kneel" : "slide", a, b, iMin, '?', score, false, true); outList.Add(e);
                return;
            }
            if (spike)
            {
                // the ball is swung down: the hands' height over the hips falls through 0.12 m after being above 0.30 m
                for (int i = (int)(0.5f * Rate); i < N - 4; i++)
                {
                    float hy = (f.P[i][AnimRig.LH].y + f.P[i][AnimRig.RH].y) * 0.5f - f.P[i][AnimRig.Hips].y;
                    if (hy > 0.12f) continue;
                    int up = -1; for (int k = i; k >= Mathf.Max(0, i - 18); k--) { float hk = (f.P[k][AnimRig.LH].y + f.P[k][AnimRig.RH].y) * 0.5f - f.P[k][AnimRig.Hips].y; if (hk > 0.32f) { up = k; break; } }
                    if (up < 0) continue;
                    int a = Mathf.Max(0, up - Mathf.RoundToInt(0.35f * Rate)), b = Mathf.Min(N - 1, i + Mathf.RoundToInt(0.6f * Rate));
                    if (Slide(f, a, b, 1f) > 0.5f || Drift(f, a, b) > 2.0f || !PoseRange(f, a, b, 0.66f, 45f)) { i += 20; continue; }
                    outList.Add(Make(f, fam, "spike", a, b, i, '?', score, false, true)); i += (int)(2.0f * Rate);
                }
                return;
            }
            // sneak: the whole usable take (the view plays the window from the snap to the push)
            FitWhole(f, fam, "sneak", score, outList, rej, n, 0.6f);
        }

        /// <summary>Run windows of the scramble / roll-out takes: a stretch of at least 0.9 s with the hips moving at a steady 2.2 to 6.5 m/s on a steady heading (the view loops the stride and warps it to the simulated speed). Kind = the heading of the run against the chest (fwd / left / right / back).</summary>
        static void FitRuns(Feat f, float score, List<QbClipTable.Entry> outList, StringBuilder rej, string n)
        {
            int N = f.N; var sp = new float[N];
            for (int i = 2; i < N - 2; i++) sp[i] = (f.H[i + 2] - f.H[i - 2]).magnitude * (Rate / 4f);
            int i0 = 2;
            while (i0 < N - 20)
            {
                if (sp[i0] < 2.2f || sp[i0] > 6.5f) { i0++; continue; }
                int j = i0; float hd0 = HeadingOf(f, i0);
                while (j + 1 < N - 2 && sp[j + 1] >= 2.0f && sp[j + 1] <= 7.0f && Mathf.Abs(Wrap(HeadingOf(f, j + 1) - hd0)) < 28f) j++;
                if (j - i0 >= Mathf.RoundToInt(0.9f * Rate))
                {
                    int a = i0, b = j; float sl = Slide(f, a, b, 1f); float dir; float dist = Travel(f, a, b, out dir);
                    if (sl <= 0.45f && f.HipsY[a] >= 0.62f && PoseRange(f, a, b, 0.62f, 60f))
                    {
                        string kind = Mathf.Abs(dir) < 40f ? "fwd" : (Mathf.Abs(dir) > 140f ? "back" : (dir > 0f ? "right" : "left"));
                        var e = Make(f, QbFam.Roll, kind, a, b, 0, '?', score, false, true); e.travelDir = dir; e.dist = dist; outList.Add(e);
                    }
                    else rej.AppendLine(n + "@" + (a / Rate).ToString("0.00") + ",roll,slide " + sl.ToString("0.00"));
                    i0 = j + 1;
                }
                else i0 = j + 1;
            }
        }

        static float HeadingOf(Feat f, int i) { Vector2 d = f.H[Mathf.Min(f.N - 1, i + 2)] - f.H[Mathf.Max(0, i - 2)]; return YawOf(new Vector3(d.x, 0f, d.y)); }
    }
}

namespace PixFootball.EditorTools
{
    public static partial class QbClipCatalog
    {
        static readonly Regex ReUcDrop = new Regex(@"undercen\w*_catch_to_\s*\d_step|undercen\w*_\d_step_dropback", RegexOptions.IgnoreCase);
        static readonly Regex ReSgGather = new Regex(@"snap_1stepdropback|snaptodropback_transition", RegexOptions.IgnoreCase);

        /// <summary>The under-centre catch-and-drop takes (and the shotgun gather): the catch (the first hand peak: sync), the first step after it and the plant (the travel segment mvA .. mvB), the window runs to half a second after the plant. The row is the SNAP window of the play AND its drop: the view starts it so the catch lands on the simulated snap and fits the segment to the simulated drop.</summary>
        static bool FitDrops(Feat f, float score, List<QbClipTable.Entry> outList, StringBuilder rej, string n, bool sg)
        {
            int N = f.N; var sp = new float[N];
            for (int i = 2; i < N - 2; i++) sp[i] = (f.Hs[Mathf.Min(N - 1, i + 2)] - f.Hs[Mathf.Max(0, i - 2)]).magnitude * (Rate / 4f);
            var raw = new float[N]; for (int i = 2; i < N - 2; i++) raw[i] = (f.H[i + 2] - f.H[i - 2]).magnitude * (Rate / 4f);
            int s = -1;
            if (sg) { bool right; s = HandsClose(f, out right); }
            else { int lim = Mathf.Min(N - 4, Mathf.RoundToInt(1.8f * Rate)); for (int i = 6; i < lim; i++) { float spd = Mathf.Max(f.RelR[i], f.RelL[i]); if (spd >= 1.5f && spd >= Mathf.Max(f.RelR[i - 1], f.RelL[i - 1]) && spd >= Mathf.Max(f.RelR[i + 1], f.RelL[i + 1])) { s = i; break; } } }
            if (s < 0) { bool rh2; int hc = HandsClose(f, out rh2); if (hc > 0 && hc < N / 2) s = hc; }      // the hands close on the ball (a catch with a soft hand peak)
            if (s < 0) { rej.AppendLine(n + ",drop_uc,no catch"); return true; }
            int mA = -1; for (int i = s; i < N - 4; i++) if (raw[i] >= 1.0f) { mA = i; break; }
            if (mA < 0) { rej.AppendLine(n + ",drop_uc,no step after the catch"); return true; }
            int mB = -1; for (int i = mA + Mathf.RoundToInt(0.4f * Rate); i < N - 6; i++) { bool still = true; for (int k = i; k < i + 5; k++) if (raw[k] > 0.7f) { still = false; break; } if (still) { mB = i; break; } }
            if (mB < 0) { rej.AppendLine(n + ",drop_uc,no plant"); return true; }
            int b = Mathf.Min(N - 1, mB + Mathf.RoundToInt(0.5f * Rate));
            float dist = (f.H[mB] - f.H[mA]).magnitude;
            float sl = Slide(f, 0, b, 1f);
            if (dist < (sg ? 0.25f : 0.9f)) { rej.AppendLine(n + ",drop_uc,travel " + dist.ToString("0.00") + " m"); return true; }
            if (sl > 0.50f) { rej.AppendLine(n + ",drop_uc,slide " + sl.ToString("0.00") + " (the clip's own world)"); return true; }
            if (f.HipsY[0] < 0.62f) { rej.AppendLine(n + ",drop_uc,start pose"); return true; }
            var e = Make(f, sg ? QbFam.DropSg : QbFam.DropUc, "drop", 0, b, s, '?', score, false, true);
            e.mvA = mA / Rate; e.mvB = mB / Rate; e.yawSync = f.Chest[s]; e.yaw0 = f.Chest[0];
            outList.Add(e);
            return true;
        }
    }
}
