using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    public static partial class DefaultPlaybooks
    {
        public const string KickoffF = "Kickoff", OnsideF = "Onside Kick", KickReturnF = "Kickoff Return",
            PuntF = "Punt", PuntReturnF = "Punt Return", FieldGoalF = "Field Goal", FieldGoalBlockF = "Field Goal Block";

        /// <summary>Complete default playbook: offense, defense and special teams.</summary>
        public static Playbook Create(string name = "Standard")
        {
            var pb = new Playbook(name);
            AddOffense(pb);
            AddDefense(pb);
            AddSpecialTeams(pb);
            return pb;
        }

        public static Playbook Offense() { var pb = new Playbook("Standard Offense"); AddOffense(pb); return pb; }
        public static Playbook Defense() { var pb = new Playbook("Standard Defense"); AddDefense(pb); return pb; }
        public static Playbook SpecialTeams() { var pb = new Playbook("Standard Special Teams"); AddSpecialTeams(pb); return pb; }

        private static SlotSpec St(Position p, float x, float y, string label = "") { return new SlotSpec(p, x, y, label); }

        public static List<Formation> SpecialFormations()
        {
            var list = new List<Formation>();
            float[] kx = { -25f, -20f, -15f, -10f, -5f, 5f, 10f, 15f, 20f, 25f };
            Position[] kp = { Position.CB, Position.SS, Position.LOLB, Position.TE, Position.MLB, Position.FB, Position.ROLB, Position.RB, Position.FS, Position.CB };
            var kick = new List<SlotSpec> { St(Position.K, 0f, -5f, "K") };
            for (int i = 0; i < 10; i++) kick.Add(St(kp[i], kx[i], -1f, "L" + i));
            list.Add(new Formation(KickoffF, PlaySide.Special, "Kickoff", kick.ToArray()));

            float[] ox = { -12f, -9f, -6f, -3f, 3f, 6f, 9f, 12f, 15f, -15f };
            var onside = new List<SlotSpec> { St(Position.K, 0f, -5f, "K") };
            for (int i = 0; i < 10; i++) onside.Add(St(kp[i], ox[i], -1f, "L" + i));
            list.Add(new Formation(OnsideF, PlaySide.Special, "Onside", onside.ToArray()));

            list.Add(new Formation(KickReturnF, PlaySide.Special, "KickoffReturn", new[] {
                St(Position.RB, 0f, 58f, "R1"), St(Position.WR, 6f, 56f, "R2"),
                St(Position.FB, -20f, 40f), St(Position.TE, -10f, 40f), St(Position.TE, 0f, 40f), St(Position.LOLB, 10f, 40f), St(Position.MLB, 20f, 40f),
                St(Position.ROLB, -15f, 48f), St(Position.CB, -5f, 48f), St(Position.CB, 5f, 48f), St(Position.FS, 15f, 48f) }));

            list.Add(new Formation(PuntF, PlaySide.Special, "Punt", new[] {
                St(Position.P, -0.45f, -14f, "P"), St(Position.C, 0f, 0f, "C"), St(Position.LG, -1.5f, 0f), St(Position.RG, 1.5f, 0f),
                St(Position.LT, -3f, 0f), St(Position.RT, 3f, 0f), St(Position.TE, -4.5f, 0f), St(Position.TE, 4.5f, 0f),
                St(Position.WR, -20f, 0f, "G1"), St(Position.WR, 20f, 0f, "G2"), St(Position.FB, 0f, -4f, "PP") }));

            list.Add(new Formation(PuntReturnF, PlaySide.Special, "PuntReturn", new[] {
                St(Position.CB, 0f, 45f, "PR"), St(Position.LE, -4.5f, 1.2f), St(Position.DT, -1.5f, 1.2f), St(Position.RE, 4.5f, 1.2f),
                St(Position.LOLB, -6f, 4f), St(Position.MLB, 0f, 4f), St(Position.ROLB, 6f, 4f),
                St(Position.CB, -20f, 3f), St(Position.CB, 20f, 3f), St(Position.FS, -8f, 20f), St(Position.SS, 8f, 20f) }));

            list.Add(new Formation(FieldGoalF, PlaySide.Special, "FieldGoal", new[] {
                St(Position.K, -1.5f, -9.0f, "K"), St(Position.QB, 0f, -7f, "H"), St(Position.C, 0f, 0f, "C"), St(Position.LG, -1.5f, 0f), St(Position.RG, 1.5f, 0f),
                St(Position.LT, -3f, 0f), St(Position.RT, 3f, 0f), St(Position.TE, -4.5f, 0f), St(Position.TE, 4.5f, 0f),
                St(Position.WR, -6f, -1f), St(Position.WR, 6f, -1f) }));

            list.Add(new Formation(FieldGoalBlockF, PlaySide.Special, "FieldGoalBlock", new[] {
                St(Position.LE, -4.5f, 1.2f), St(Position.DT, -1.5f, 1.2f), St(Position.DT, 1.5f, 1.2f), St(Position.RE, 4.5f, 1.2f),
                St(Position.LOLB, -6f, 2f), St(Position.MLB, 0f, 3f), St(Position.ROLB, 6f, 2f),
                St(Position.CB, -9f, 2f), St(Position.CB, 9f, 2f), St(Position.FS, 0f, 12f, "PR"), St(Position.SS, 0f, 8f) }));
            return list;
        }

        private static Play SpecialPlay(Formation f, string name, PlayCategory cat, Assignment[] a, params string[] tags)
        {
            for (int i = 0; i < a.Length; i++) a[i].Slot = i;
            return new Play
            {
                Id = Slug(f.Name + "." + name), Name = name, FormationName = f.Name, Side = PlaySide.Special,
                Category = cat, Scheme = CoverageScheme.None, Assignments = a, Tags = tags
            };
        }

        private static LaneAssignment Lane(Formation f, int slot, int lane, float depth, bool gunner = false, bool fan = false)
        {
            var l = new LaneAssignment { Lane = lane, Gunner = gunner };
            SlotSpec s = f.Slots[slot];
            if (fan && depth > 25f)
            {
                // v0.9.0 (E-342): a kickoff coverage lane fans out (x scaled 1.15 toward the sideline by depth 20) instead of two vertical segments: the line spreads as it comes down the field
                float fx = Math.Max(-24f, Math.Min(24f, s.X * 1.15f));
                l.Path.Add(new Vec2(s.X, 10f));
                l.Path.Add(new Vec2(fx, 22f));
                l.Path.Add(new Vec2(fx, depth));
                return l;
            }
            l.Path.Add(new Vec2(s.X, depth * 0.5f));
            l.Path.Add(new Vec2(s.X, depth));
            return l;
        }

        private static BlockAssignment Blk(BlockKind k, BlockTarget t, RunGap gap = RunGap.None)
        {
            return new BlockAssignment { Block = k, Target = t, Gap = gap };
        }

        public static void AddSpecialTeams(Playbook pb)
        {
            foreach (Formation f in SpecialFormations()) if (pb.FindFormation(f.Name) == null) pb.Formations.Add(f);

            // Kickoffs
            Formation ko = pb.FindFormation(KickoffF);
            Action<string, KickKind, float> kickoff = (name, kind, aim) =>
            {
                var a = new Assignment[11];
                a[0] = new KickAssignment { KickType = kind, Role = KickRole.Kicker, AimX = aim };
                for (int i = 1; i < 11; i++) { var ln = Lane(ko, i, i, kind == KickKind.Squib ? 30f : 55f, false, true); ln.Contain = i == 1 || i == 10; a[i] = ln; }   // v0.9.0 (E-341): the widest two contain (they were gunners that converged on the ball spot)
                AddPlay(pb, SpecialPlay(ko, name, PlayCategory.Kickoff, a, "kickoff"));
            };
            kickoff("Kickoff Deep Middle", KickKind.Deep, 0f);
            kickoff("Kickoff Deep Left", KickKind.Deep, -15f);
            kickoff("Kickoff Deep Right", KickKind.Deep, 15f);
            kickoff("Kickoff Squib", KickKind.Squib, 0f);

            Formation on = pb.FindFormation(OnsideF);
            Action<string, float> onside = (name, aim) =>
            {
                var a = new Assignment[11];
                a[0] = new KickAssignment { KickType = KickKind.Onside, Role = KickRole.Kicker, AimX = aim };
                for (int i = 1; i < 11; i++) a[i] = Lane(on, i, i, 12f);
                AddPlay(pb, SpecialPlay(on, name, PlayCategory.Onside, a, "onside"));
            };
            onside("Onside Kick Short", 10f);
            onside("Onside Kick Surprise", -10f);

            // Kickoff returns
            Formation kr = pb.FindFormation(KickReturnF);
            Action<string, BlockKind, float> kickReturn = (name, kind, lane) =>
            {
                var a = new Assignment[11];
                var r1 = new ReturnerAssignment { FairCatchAllowed = false, ReturnLaneX = lane };
                r1.Path.Add(new Vec2(lane, 50f)); r1.Path.Add(new Vec2(lane, 20f));
                a[0] = r1;
                a[1] = new ReturnerAssignment { FairCatchAllowed = true, ReturnLaneX = 0f };
                // v0.9.0 (E-343): the blockers have different jobs. A wedge return builds a four-man wedge in front of the returner, three wall men and two trailers; a left / right return is a five-man wall,
                // two wedge men and two cross blocks (all nine used to get the same kind and target)
                bool wedge = kind == BlockKind.Wedge;
                for (int i = 2; i < 11; i++)
                {
                    if (wedge) a[i] = i <= 5 ? Blk(BlockKind.Wedge, BlockTarget.NearestDefender) : (i <= 8 ? Blk(BlockKind.Wall, BlockTarget.Playside) : Blk(BlockKind.Man, BlockTarget.NearestDefender));
                    else a[i] = i <= 6 ? Blk(BlockKind.Wall, BlockTarget.Playside) : (i <= 8 ? Blk(BlockKind.Wedge, BlockTarget.NearestDefender) : Blk(BlockKind.Man, BlockTarget.Linebacker));
                }
                AddPlay(pb, SpecialPlay(kr, name, PlayCategory.KickReturn, a, "return"));
            };
            kickReturn("Kick Return Middle Wedge", BlockKind.Wedge, 0f);
            kickReturn("Kick Return Left", BlockKind.Wall, -15f);
            kickReturn("Kick Return Right", BlockKind.Wall, 15f);

            // Punts
            Formation pf = pb.FindFormation(PuntF);
            Action<string, KickKind, float> punt = (name, kind, aim) =>
            {
                var a = new Assignment[11];
                a[0] = new KickAssignment { KickType = kind, Role = KickRole.Kicker, AimX = aim };
                a[1] = new KickAssignment { KickType = kind, Role = KickRole.Snapper };
                for (int i = 2; i < 8; i++) a[i] = Blk(BlockKind.PassPro, BlockTarget.NearestDefender);
                a[8] = Lane(pf, 8, 1, 45f, true);
                a[9] = Lane(pf, 9, 2, 45f, true);
                a[10] = Blk(BlockKind.PassPro, BlockTarget.BlitzPickup);
                AddPlay(pb, SpecialPlay(pf, name, PlayCategory.Punt, a, "punt"));
            };
            punt("Punt Standard", KickKind.Punt, 0f);
            punt("Punt Directional Left", KickKind.DirectionalLeft, -15f);
            punt("Punt Directional Right", KickKind.DirectionalRight, 15f);
            punt("Rugby Punt", KickKind.Rugby, 8f);
            punt("Pooch Punt", KickKind.Pooch, 0f);   // v0.9.0 (E-339)

            // Punt returns / blocks
            Formation pr = pb.FindFormation(PuntReturnF);
            Action<string, bool, bool> puntReturn = (name, rush, safe) =>
            {
                var a = new Assignment[11];
                var ret = new ReturnerAssignment { FairCatchAllowed = safe, ReturnLaneX = 0f };
                ret.Path.Add(new Vec2(0f, 40f)); ret.Path.Add(new Vec2(0f, 15f));
                a[0] = ret;
                RunGap[] gaps = { RunGap.None, RunGap.CLeft, RunGap.ALeft, RunGap.CRight };
                for (int i = 1; i < 4; i++)
                    a[i] = rush ? (Assignment)new RushAssignment { Gap = gaps[i] } : Blk(BlockKind.Man, BlockTarget.NearestDefender);
                for (int i = 4; i < 7; i++)
                    a[i] = rush ? (Assignment)new BlitzAssignment { Gap = i == 4 ? RunGap.DLeft : (i == 6 ? RunGap.DRight : RunGap.BRight), Edge = i != 5 }
                                : Blk(BlockKind.Wall, BlockTarget.Playside);
                a[7] = rush ? (Assignment)Lane(pr, 7, 1, 30f) : Blk(BlockKind.Stalk, BlockTarget.Cornerback);
                a[8] = rush ? (Assignment)Lane(pr, 8, 2, 30f) : Blk(BlockKind.Stalk, BlockTarget.Cornerback);
                a[9] = Blk(BlockKind.Wall, BlockTarget.Playside);
                a[10] = Blk(BlockKind.Wall, BlockTarget.Playside);
                AddPlay(pb, SpecialPlay(pr, name, PlayCategory.PuntReturn, a, "puntreturn"));
            };
            puntReturn("Punt Return Set", false, false);
            puntReturn("Punt Return Safe", false, true);
            puntReturn("Punt Block", true, false);

            // Field goals / extra points
            Formation fg = pb.FindFormation(FieldGoalF);
            Action<string, PlayCategory, KickKind> kickPlay = (name, cat, kind) =>
            {
                var a = new Assignment[11];
                a[0] = new KickAssignment { KickType = kind, Role = KickRole.Kicker };
                a[1] = new KickAssignment { KickType = kind, Role = KickRole.Holder };
                a[2] = new KickAssignment { KickType = kind, Role = KickRole.Snapper };
                for (int i = 3; i < 11; i++) a[i] = Blk(BlockKind.PassPro, BlockTarget.NearestDefender);
                AddPlay(pb, SpecialPlay(fg, name, cat, a, "kick"));
            };
            kickPlay("Field Goal", PlayCategory.FieldGoal, KickKind.FieldGoal);
            kickPlay("Extra Point", PlayCategory.PointAfter, KickKind.ExtraPoint);

            Formation fb = pb.FindFormation(FieldGoalBlockF);
            Action<string, bool, bool> fgBlock = (name, edge, safe) =>
            {
                var a = new Assignment[11];
                RunGap[] dl = { RunGap.CLeft, RunGap.BLeft, RunGap.BRight, RunGap.CRight };
                for (int i = 0; i < 4; i++) a[i] = new RushAssignment { Gap = dl[i], Contain = i == 0 || i == 3 };
                a[4] = edge ? (Assignment)new BlitzAssignment { Gap = RunGap.DLeft, Edge = true } : (safe ? (Assignment)Lane(fb, 4, 5, 8f) : new RushAssignment { Gap = RunGap.ALeft });
                a[5] = safe ? (Assignment)Lane(fb, 5, 6, 8f) : new RushAssignment { Gap = RunGap.ARight };   // QA-539: Safe Return rushes only the four linemen and peels the rest back as blockers for the return
                a[6] = edge ? (Assignment)new BlitzAssignment { Gap = RunGap.DRight, Edge = true } : (safe ? (Assignment)Lane(fb, 6, 4, 8f) : new RushAssignment { Gap = RunGap.None });   // QA-541: the middle rush no longer sends two men through the right B gap
                a[7] = edge && !safe ? (Assignment)new BlitzAssignment { Gap = RunGap.DLeft, Edge = true, Delay = 0.2f } : Lane(fb, 7, 1, 10f);
                a[8] = edge && !safe ? (Assignment)new BlitzAssignment { Gap = RunGap.DRight, Edge = true, Delay = 0.2f } : Lane(fb, 8, 2, 10f);
                var ret = new ReturnerAssignment { FairCatchAllowed = false };
                ret.Path.Add(new Vec2(0f, 15f));
                a[9] = ret;
                a[10] = Lane(fb, 10, 3, 6f);
                AddPlay(pb, SpecialPlay(fb, name, PlayCategory.FieldGoalBlock, a, "fgblock"));
            };
            fgBlock("Field Goal Block Middle", false, false);
            fgBlock("Field Goal Block Edge", true, false);
            fgBlock("Field Goal Safe Return", false, true);
        }
    }
}
