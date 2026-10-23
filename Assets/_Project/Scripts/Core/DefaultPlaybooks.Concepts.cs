using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    /// <summary>
    /// The passing concepts. A concept is written in ROLES, not labels, so it builds correctly out of every formation: S1..S4 are the receivers of the concept's strong side outside in, W1..W3 the weak side,
    /// SI / WI the innermost strong / weak receiver (the crossers), TE the inline tight end, HB / F the backs; "Role=Route@depth*read" gives a route (and the quarterback's read), "@PassPro" / "@Chip" / "@Block" a block.
    /// Receivers the concept does not name run a clearing vertical (wide) or a hitch (slot).
    /// </summary>
    internal sealed class PassConcept
    {
        public string Name;
        public PlayCategory Cat;
        public string[] Forms;
        public string Spec;
        public QbAction Action = QbAction.QuickPass;
        public int Steps = 3;
        /// <summary>Roll-out direction relative to the concept's strong side: +1 toward it, -1 away, 0 a pocket play.</summary>
        public int Roll;
        public bool Screen, FakeRun;
        public SideMode Side = SideMode.Strong;
        /// <summary>One line a coach would say about the concept (play-call card text).</summary>
        public string Note = "";
    }

    public static partial class DefaultPlaybooks
    {
        private static PassConcept C(string name, PlayCategory cat, string[] forms, string spec, QbAction action = QbAction.QuickPass, int steps = 3, int roll = 0, SideMode side = SideMode.Strong,
            bool screen = false, bool fakeRun = false, string note = "")
        {
            return new PassConcept { Name = name, Cat = cat, Forms = forms, Spec = spec, Action = action, Steps = steps, Roll = roll, Side = side, Screen = screen, FakeRun = fakeRun, Note = note };
        }

        /// <summary>The passing concepts of the playbook (the data the call screen, the diagram and the tests read).</summary>
        internal static List<PassConcept> PassConcepts()
        {
            string[] sg = { ShotgunF, Singleback };
            string[] spread = { ShotgunF, Singleback, Pistol, Empty };
            var l = new List<PassConcept>();
            var SP = PlayCategory.ShortPass; var MP = PlayCategory.MediumPass; var DP = PlayCategory.DeepPass; var PA = PlayCategory.PlayAction;
            // ---- quick game (three-step, one read) ----
            l.Add(C("Slants", SP, new[] { ShotgunF, Singleback, Trips, Pistol }, "TE=Flat S1=Slant*1 S2=Slant*2 S3=Slant*3 W1=Slant*4 W2=Slant HB=@Chip", note: "Every wide receiver breaks in at 45 degrees after a three-step stem"));
            l.Add(C("Quick Hitches", SP, new[] { ShotgunF, Pistol, Trips }, "S1=Hitch@6*1 W1=Hitch@6*2 S2=Hitch@6*3 W2=Hitch@6*3 TE=Hitch@6 HB=Swing", note: "Stop at six yards and turn to the passer"));
            l.Add(C("Stick", SP, new[] { ShotgunF, Trips, Singleback, Pistol }, "TE+=Stick*1 S1=Go S2=Flat*2 S3=Flat*2 W1=Hitch@6*3 W2=Flat HB=Swing", side: SideMode.Te, note: "The tight end sticks at five, the slot runs the flat, the outside man clears"));
            l.Add(C("Curl-Flat", SP, new[] { Singleback, ShotgunF, IForm }, "S1=Curl@12*1 S2=Flat*2 W1=Curl@12*3 W2=Flat HB=@Chip F=@PassPro", QbAction.DropbackPass, 5, note: "Curl at twelve, flat underneath, on both sides"));
            l.Add(C("Mesh", SP, new[] { ShotgunF, Empty, Pistol }, "WI=Drag@5*1 SI=Shallow@6.5*2 S1=Go W1=Curl@12*3 TE=Seam HB=Flat", note: "Two shallow crossers pass each other at the middle (the rub)"));
            l.Add(C("Snag", SP, new[] { Trips }, "TE=@Chip S1=Corner@12*3 S2=Curl@6*1 S3=Flat*2 W1=Go HB=@PassPro", note: "Corner behind a curl behind a flat: the spot throw to the middle receiver"));
            l.Add(C("Spacing", SP, new[] { ShotgunF, Empty }, "S1=Hitch@6*3 W1=Hitch@6*4 S2=Curl@8*1 W2=Curl@8*2 S3=Flat*5 TE=Flat HB=Swing", note: "Five receivers at four spots across the field"));
            l.Add(C("Double Outs", SP, sg, "TE=Hitch@6 S1=Out@10*1 W1=Out@10*2 S2=Flat*3 HB=Swing", note: "Both outside receivers break to the sidelines at ten yards"));
            l.Add(C("Texas Route", SP, new[] { ShotgunF, Singleback, Pistol }, "HB=Texas*1 S1=Go*2 W1=Hitch@6*3 S2=Slant TE=Out@8", note: "The back runs the angle behind the linebackers"));
            l.Add(C("Shallow Cross", SP, new[] { ShotgunF, Empty, Pistol, Singleback }, "SI=Shallow@4*1 WI=Drag@6*2 S1=Go W1=Post@12*3 TE=Seam HB=@Chip", note: "One crosser high-low with a drag, the post clears the safety"));
            // ---- intermediate ----
            l.Add(C("Levels", MP, new[] { ShotgunF, Empty }, "S1=Dig@12*1 S2=In@6*2 W1=Dig@12*3 W2=In@6 TE=Seam HB=Swing", QbAction.DropbackPass, 5, note: "A dig at twelve over an in-route at six on each side"));
            l.Add(C("Smash", MP, new[] { ShotgunF, Singleback, Trips, Pistol }, "S1=Hitch@6*2 S2=Corner@12*1 S3=Flat W1=Go W2=Hitch@6 HB=Swing", QbAction.DropbackPass, 5, side: SideMode.Wr, note: "A hitch under a corner on the same side: high-low on the corner"));
            l.Add(C("Flood", MP, new[] { ShotgunF, Singleback, IForm, Pistol }, "S1=Corner@14*1 S2=Out@10*2 S3=Flat*3 HB=Flat*3 W1=Go W2=Dig@12", QbAction.Rollout, 5, roll: 1, note: "Three levels to one side: deep corner, out at ten, flat"));
            l.Add(C("Y-Cross", MP, sg, "TE+=Cross@14*1 S1=Post@12*2 S2=Curl@10*3 W1=Go W2=Hitch@6 HB=Swing", QbAction.DropbackPass, 5, side: SideMode.Te, note: "The tight end crosses the field behind the linebackers"));
            l.Add(C("Drive", MP, sg, "W1=Dig@12*1 S2=Drag@5*2 S1=Go TE=Seam HB=Swing", QbAction.DropbackPass, 5, note: "A shallow drag underneath a dig from the other side"));
            l.Add(C("Deep Comebacks", MP, sg, "S1=Comeback@16*1 W1=Comeback@16*2 S2=Go*3 TE=Out@8 HB=Flat", QbAction.DropbackPass, 5, note: "Both outside receivers run sixteen and come back to the sideline"));
            l.Add(C("TE Seam", MP, new[] { Singleback, IForm, ShotgunF }, "TE+=Seam*1 S1=Hitch@6*2 W1=Out@10*3 HB=Flat F=@PassPro", QbAction.DropbackPass, 5, side: SideMode.Te, note: "The tight end splits the safeties"));
            l.Add(C("Dagger", MP, new[] { ShotgunF, Singleback, Pistol }, "S2=Seam*2 S1=Dig@15*1 W1=Go W2=Curl@10 HB=Swing", QbAction.DropbackPass, 5, note: "The slot clears the seam, the outside receiver digs behind him"));
            l.Add(C("Mills", MP, new[] { ShotgunF, Singleback, IForm }, "S2=Post@12*1 S1=Dig@12*2 W1=Go TE=@Chip HB=Swing", QbAction.DropbackPass, 5, note: "A post and a dig from the same side"));
            // ---- deep ----
            l.Add(C("Four Verticals", DP, new[] { ShotgunF, Empty, Trips }, "W1=Go*1 S1=Go*2 S2=Seam*3 W2=Seam S3=Seam TE=Seam HB=Swing*4", QbAction.DropbackPass, 5, note: "Everybody runs straight up the field"));
            l.Add(C("Hail Mary", DP, new[] { ShotgunF, Empty }, "S1=Go*1 W1=Go*2 S2=Go*3 W2=Go S3=Go TE=Go HB=@PassPro", QbAction.HailMary, 5, note: "Heave it into the end zone"));
            l.Add(C("Post-Wheel", DP, sg, "S1=Post@12*1 HB=Wheel*2 W1=Go S2=Out@10 TE=Flat", QbAction.DropbackPass, 5, note: "A post to hold the safety, the back wheels up the sideline"));
            l.Add(C("Corner-Post", DP, sg, "S2=Post@12*1 S1=Corner@12*2 W1=Dig@12*3 TE=Seam HB=@PassPro", QbAction.DropbackPass, 7, note: "A post and a corner from the same side (scissors)"));
            l.Add(C("Out-and-Up Shot", DP, new[] { Singleback, IForm }, "S1=OutAndUp*1 W1=Go*2 TE=Drag@5*3 HB=@PassPro F=Flat*4", QbAction.DropbackPass, 7, note: "Sell the out, then go"));
            l.Add(C("Wheel Route", DP, sg, "HB=Wheel*1 S1=Go*2 W1=Hitch@6 S2=Out@10 TE=Drag@5", QbAction.DropbackPass, 5, note: "The back runs the wheel down the sideline"));
            l.Add(C("Fade", DP, new[] { ShotgunF, Empty, GoalLine }, "S1=Fade*1 W1=Fade*2 S2=Slant*3 TE=Flat*4 Y2=Flat HB=@PassPro F=@PassPro", note: "Throw it up to the corner"));
            // ---- play action ----
            l.Add(C("PA Boot", PA, new[] { Singleback, IForm }, "W1=Cross@14*1 S1=Corner@14*2 TE+=Flat*3 S2=Flat F=Flat", QbAction.Boot, 5, roll: 1, side: SideMode.Te, fakeRun: true, note: "Fake the handoff and roll out away from the pressure"));
            l.Add(C("PA Crossers", PA, new[] { IForm, Singleback }, "TE+=Cross@14*1 S1=Post@12*2 W1=Dig@12*3", QbAction.PlayActionFake, 5, side: SideMode.Te, fakeRun: true, note: "The linebackers bite on the fake, the tight end crosses behind them"));
            l.Add(C("PA Deep Shot", PA, new[] { Singleback, IForm }, "S1=Post@12*1 W1=Go*2 TE=Out@8*3 S2=Hitch@6*4", QbAction.PlayActionFake, 5, side: SideMode.Te, fakeRun: true, note: "Sell the run, take the post over the top"));
            // ---- screens ----
            l.Add(C("HB Screen", PlayCategory.Screen, sg, "HB=ScreenRelease*1 S1=Go W1=Go S2=Go TE=@Block", QbAction.ScreenPass, 3, screen: true, note: "Let the rush through, throw back to the running back behind the blockers"));
            l.Add(C("WR Screen", PlayCategory.Screen, new[] { Trips, Empty }, "S2=Bubble*1 S3=Bubble*2 S1=@Block W1=Go TE=@Block", QbAction.ScreenPass, 3, screen: true, note: "A quick bubble behind the wide receivers' blocks"));
            // inside the five: quick throws and a bootleg so the goal-line calls are not just four runs and a fade
            l.Add(C("Goal Line Slant", SP, new[] { GoalLine }, "S1=Slant*1 TE=Flat*2 F=Flat*3 Y2=@Chip HB=@PassPro", QbAction.DropbackPass, 3, note: "A quick slant at the goal line"));
            l.Add(C("Goal Line Boot", PA, new[] { GoalLine }, "Y2=Drag@3*1 S1=Out@6*2 F=Flat*3 TE=@Chip", QbAction.Boot, 3, roll: 1, side: SideMode.Te, fakeRun: true, note: "Fake the dive and boot"));
            l.Add(C("Slip Screen", PlayCategory.Screen, sg, "TE+=ScreenRelease*1 S1=Go W1=Go HB=Flat", QbAction.ScreenPass, 3, side: SideMode.Te, screen: true, note: "The tight end slips out behind the rush"));
            return l;
        }

        private static void AddPasses(Playbook pb)
        {
            foreach (PassConcept c in PassConcepts())
            {
                foreach (string fn in Expand(c.Forms))
                {
                    Formation f = pb.FindFormation(fn);
                    var b = new OffBuilder(f, true, c.Side);
                    int dropSteps = b.Shotgun ? c.Steps : (c.Steps <= 3 ? 3 : (c.Steps <= 5 ? 5 : 7));
                    int rollDir = c.Roll * b.StrongSign;
                    b.SetQb(c.Action, dropSteps, rollDir);
                    if (c.FakeRun)
                    {
                        int fd = rollDir >= 0 ? 1 : -1;
                        b.FakeCarry("HB", CarryStyle.InsideZone, fd > 0 ? RunGap.BRight : RunGap.BLeft, P(fd * 1.2f, Math.Min(-2.6f, b.F.Slots[OffBuilder.HB].Y + 3f)), P(fd * 1.8f, -1.2f));
                    }
                    b.Spec(c.Spec);
                    if (b.RouteCount < 2) continue;
                    int slide = rollDir != 0 ? rollDir : b.StrongSign;
                    if (c.Screen) b.OlScreen(); else b.OlPassPro(slide);
                    Play made = b.Finish(c.Name, c.Cat, "pass");
                    made.Note = c.Note;
                    AddPlay(pb, made);
                }
            }
        }
    }
}
