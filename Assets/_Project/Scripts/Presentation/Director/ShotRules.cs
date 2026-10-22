using System.Collections.Generic;
using PixFootball.Gameplay;

namespace PixFootball.Presentation
{
    public enum ShotKind { None, SkySwoop, SidelineQb, EndZone, StadiumWide, TightCarrier, Celebration, CoachReaction, CrowdShot }

    public struct ShotPlan
    {
        public ShotKind Kind; public float Duration;
        public ShotPlan(ShotKind kind, float duration) { Kind = kind; Duration = duration; }
        public override string ToString() { return Kind + " " + Duration.ToString("0.0") + "s"; }
    }

    /// <summary>What the camera director knows when it picks shots.</summary>
    public struct DirectorContext
    {
        /// <summary>0 Full, 1 Minimal, 2 Off (PixSettings.Cinematics).</summary>
        public int Cinematics;
        // ---- before the snap
        public bool GameStart, FirstPlayOfDrive, AfterScore, FirstPlayOfHalf, Kickoff;
        public int DriveIndex;
        /// <summary>Seconds before the snap can happen (play clock left); the shots must leave live view before then.</summary>
        public float PreSnapSeconds;
        // ---- after the whistle
        public Headline Headline;
        public bool Touchdown, Turnover, Score, BigPlay, HomeBenefits, CarrierKnown;
        public int PlaysSinceCrowd;
        /// <summary>v0.6: a human plays this game; cinematic cuts must never fight his camera (no pre-snap cuts after the intro, short post-whistle cuts only).</summary>
        public bool UserPlaying;
    }

    /// <summary>
    /// 2K5-style camera director rules: which cinematic shots go before a snap and after a whistle. Pure and tested. Hard rules: nothing runs when Cinematics is Off, a shot never
    /// runs into the snap (the live camera is back at least <see cref="MinLiveBeforeSnap"/> seconds before the play can start), shots before a snap are short.
    /// </summary>
    public static class ShotRules
    {
        public const float MinLiveBeforeSnap = 0.6f, MaxPreSnapShot = 2.6f, MaxPostPlayTotal = 5.2f;

        public static List<ShotPlan> PreSnap(in DirectorContext c)
        {
            var l = new List<ShotPlan>();
            if (c.Cinematics >= 2) return l;
            if (c.UserPlaying && !c.GameStart) return l;   // v0.6: never a cut while the user picks plays / reads the defense
            float room = c.PreSnapSeconds - MinLiveBeforeSnap;
            if (room < 1.0f) return l;
            if (c.GameStart) { l.Add(new ShotPlan(ShotKind.SkySwoop, System.Math.Min(3.2f, room))); return l; }
            if (c.Kickoff && c.Cinematics == 0 && c.AfterScore) { l.Add(new ShotPlan(ShotKind.SkySwoop, System.Math.Min(2.4f, room))); return l; }
            if (c.Cinematics == 0 && (c.FirstPlayOfDrive || c.AfterScore) && !c.Kickoff)
            {
                ShotKind k = (c.DriveIndex % 3) == 0 ? ShotKind.SkySwoop : (c.DriveIndex % 3) == 1 ? ShotKind.SidelineQb : ShotKind.EndZone;
                l.Add(new ShotPlan(k, System.Math.Min(MaxPreSnapShot, room)));
                return l;
            }
            if (c.Cinematics == 1 && c.FirstPlayOfHalf) l.Add(new ShotPlan(ShotKind.SkySwoop, System.Math.Min(2.2f, room)));
            return l;
        }

        public static List<ShotPlan> PostPlay(in DirectorContext c)
        {
            var l = new List<ShotPlan>();
            if (c.Cinematics >= 2) return l;
            bool full = c.Cinematics == 0;
            if (c.Touchdown)
            {
                l.Add(new ShotPlan(ShotKind.Celebration, 2.4f));
                if (full) l.Add(new ShotPlan(c.HomeBenefits ? ShotKind.CrowdShot : ShotKind.CoachReaction, 1.7f));
            }
            else if (c.Turnover)
            {
                if (c.CarrierKnown) l.Add(new ShotPlan(ShotKind.TightCarrier, 1.2f));
                l.Add(new ShotPlan(ShotKind.CoachReaction, 1.8f));
            }
            else if (c.Score)
            {
                if (full && c.HomeBenefits) l.Add(new ShotPlan(ShotKind.CrowdShot, 1.6f));
            }
            else if (c.BigPlay && c.CarrierKnown)
            {
                l.Add(new ShotPlan(ShotKind.TightCarrier, 1.5f));
            }
            else if (full && c.CarrierKnown && (c.Headline == Headline.FirstDown || c.Headline == Headline.Sack || c.Headline == Headline.None))
            {
                l.Add(new ShotPlan(ShotKind.TightCarrier, 1.2f));
            }
            if (full && c.PlaysSinceCrowd >= 7 && l.Count > 0 && !Has(l, ShotKind.CrowdShot) && Total(l) < MaxPostPlayTotal - 1.4f) l.Add(new ShotPlan(ShotKind.CrowdShot, 1.4f));
            // v0.6: a human plays: keep the post-whistle cuts brief (no crowd / sideline cutaways)
            float budget = MaxPostPlayTotal;
            if (c.UserPlaying) { bool td = c.Touchdown; budget = 3.0f; l.RemoveAll(s => s.Kind == ShotKind.CrowdShot && !td); for (int k = 0; k < l.Count; k++) if (l[k].Duration > 1.6f) l[k] = new ShotPlan(l[k].Kind, 1.6f); }
            // never longer than the budget
            float total = 0f;
            for (int i = 0; i < l.Count; i++)
            {
                if (total + l[i].Duration > budget) { l.RemoveRange(i, l.Count - i); break; }
                total += l[i].Duration;
            }
            return l;
        }

        static bool Has(List<ShotPlan> l, ShotKind k) { foreach (var s in l) if (s.Kind == k) return true; return false; }
        public static float Total(List<ShotPlan> l) { float t = 0f; foreach (var s in l) t += s.Duration; return t; }
    }
}
