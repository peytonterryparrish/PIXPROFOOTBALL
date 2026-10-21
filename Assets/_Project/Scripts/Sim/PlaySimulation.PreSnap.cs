using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// Pre-snap changes to a live simulation (v0.3): audibles, flipping the play, hot routes, slide protection, motion on the offense;
    /// coverage audibles, line / linebacker shifts, show blitz / back off and press / off on the defense. Every change rewrites the
    /// agents' actual assignments and alignments (on clones: the playbook is never mutated) and re-prepares the AI, so the play that is
    /// snapped is the play the player set up. All calls are legal only while <see cref="Phase"/> is PreSnap on a scrimmage down.
    /// </summary>
    public sealed partial class PlaySimulation
    {
        readonly Dictionary<int, HotRoute> _hotRoutes = new Dictionary<int, HotRoute>();
        Play _calledDefense;
        float _motionLimitLeft = -16f, _motionLimitRight = 16f;
        bool _slideSet;

        /// <summary>Slot of the receiver in motion (-1 none) and his direction (-1 left, 1 right in the offense's frame).</summary>
        public int MotionSlot { get; private set; } = -1;
        public int MotionDir { get; private set; }
        public DefenseAdjust DefAdjust { get; private set; } = new DefenseAdjust();
        public IReadOnlyDictionary<int, HotRoute> HotRoutes { get { return _hotRoutes; } }
        public int SlideDirection { get { return OffPlay != null && PlayEditing.HasPassProtection(OffPlay) ? PlayEditing.SlideOf(OffPlay) : 0; } }
        public bool OffenseFlipped { get { return OffPlay != null && OffPlay.Mirrored; } }

        public bool CanAdjustPreSnap { get { return Phase == PlayPhase.PreSnap && Kind == PlayKind.Scrimmage; } }

        // ================================================================================================ offense
        void RefreshOffense(bool relayout)
        {
            for (int i = 0; i < 11; i++) Agents[i].Assign = OffPlay.Assignments[i];
            if (relayout) LayoutOffense();
            OffAI.Prepare(); DefAI.Prepare(); Specials.Prepare();
        }

        void LayoutOffense()
        {
            MotionSlot = -1; MotionDir = 0;
            for (int i = 0; i < 11; i++)
            {
                SlotSpec so = OffForm.Slots[i];
                Vec2 p = ClampInField(new Vec2(BallX0 + so.X, LosY + so.Y));
                SimAgent a = Agents[i];
                a.Body.Pos = p; a.StartPos = p; a.Body.Vel = Vec2.Zero;
                a.ClearHistory(p);
            }
        }

        /// <summary>Audible: switches the offensive call to another play of the same formation (flip and mirror state follow the new play).</summary>
        public bool ChangeOffensePlay(Play play)
        {
            if (!CanAdjustPreSnap || play == null || play.Side != PlaySide.Offense || play.FormationName != OffPlay.FormationName) return false;
            _hotRoutes.Clear(); _slideSet = false;
            OffPlay = play;
            OffForm = PlayEditing.FormationFor(Options.Playbook, play);
            RefreshOffense(true);
            return true;
        }

        /// <summary>Flips the play (left to right): the whole alignment, routes, run paths and the line's roles mirror.</summary>
        public bool FlipOffense()
        {
            if (!CanAdjustPreSnap) return false;
            Play m = PlayEditing.Mirror(OffPlay);
            // hot routes mirror with the play (the slots of the skill players do not change)
            OffPlay = m;
            OffForm = PlayEditing.FormationFor(Options.Playbook, m);
            RefreshOffense(true);
            return true;
        }

        public bool CanHotRoute(int slot) { return CanAdjustPreSnap && PlayEditing.CanHotRoute(OffPlay, OffForm, slot); }

        /// <summary>Gives the receiver in <paramref name="slot"/> a hot route (the quarterback reads him first) or a block.</summary>
        public bool SetHotRoute(int slot, HotRoute route)
        {
            if (!CanHotRoute(slot)) return false;
            OffPlay = PlayEditing.WithHotRoute(OffPlay, OffForm, slot, route);
            _hotRoutes[slot] = route;
            if (Math.Abs(OffForm.Slots[slot].Y) < 0.01f && slot == MotionSlot) { }
            RefreshOffense(false);
            return true;
        }

        /// <summary>Slide protection: the pass-blocking line slides toward -1 (left) or +1 (right); 0 restores the default.</summary>
        public bool SetSlideProtection(int dir)
        {
            if (!CanAdjustPreSnap || !PlayEditing.HasPassProtection(OffPlay)) return false;
            OffPlay = PlayEditing.WithSlide(OffPlay, dir);
            _slideSet = dir != 0;
            RefreshOffense(false);
            return true;
        }

        /// <summary>A back or receiver who starts off the line may go in motion (the offense's left = -1, right = +1).</summary>
        public bool CanMotion(int slot)
        {
            if (!CanAdjustPreSnap || slot < 1 || slot > 10) return false;
            SlotSpec s = OffForm.Slots[slot];
            if (!PositionGroup.IsEligibleReceiver(s.Position) || s.Y > -0.75f) return false;
            return true;
        }

        public bool SetMotion(int slot, int dir)
        {
            if (dir == 0) { StopMotion(); return true; }
            if (!CanMotion(slot)) return false;
            if (MotionSlot != slot) StopMotion();
            MotionSlot = slot; MotionDir = dir > 0 ? 1 : -1;
            return true;
        }

        public void StopMotion() { MotionSlot = -1; MotionDir = 0; }

        /// <summary>Called by the pre-snap step: the desired velocity of an agent while the play is set up (zero except for the man in motion).</summary>
        Vec2 PreSnapWant(int agent)
        {
            if (agent != MotionSlot || MotionDir == 0) return Vec2.Zero;
            SimAgent a = Agents[agent];
            float x = a.Pos.X - BallX0;
            if (MotionDir > 0 && x >= _motionLimitRight || MotionDir < 0 && x <= _motionLimitLeft) return Vec2.Zero;
            return new Vec2(MotionDir * 3.4f, 0f);
        }

        // ================================================================================================ defense
        /// <summary>Defensive audible to another play of the same formation.</summary>
        public bool ChangeDefensePlay(Play play)
        {
            if (!CanAdjustPreSnap || play == null || play.Side != PlaySide.Defense || play.FormationName != DefPlay.FormationName) return false;
            _calledDefense = play;
            ApplyDefense();
            return true;
        }

        /// <summary>Applies coverage audible, press / off, line and linebacker shifts, show blitz / back off.</summary>
        public bool SetDefenseAdjust(DefenseAdjust adj)
        {
            if (!CanAdjustPreSnap || adj == null) return false;
            if (_calledDefense == null) _calledDefense = DefPlay;
            DefAdjust = adj.Clone();
            ApplyDefense();
            return true;
        }

        void ApplyDefense()
        {
            Play baseCall = _calledDefense ?? DefPlay;
            Play call = baseCall;
            if (DefAdjust.Coverage != CoverageScheme.None && DefAdjust.Coverage != baseCall.Scheme)
            {
                Play alt = DefenseAdjust.FindCoverage(Options.Playbook, baseCall, DefAdjust.Coverage);
                if (alt != null) call = alt;
            }
            if (DefAdjust.Press != PressMode.Auto)
            {
                call = PlayEditing.CloneShallow(call);
                for (int i = 0; i < 11; i++)
                {
                    var cv = call.Assignments[i] as CoverageAssignment;
                    if (cv == null || DefForm.Slots[i].Position != Position.CB) continue;
                    var n = (CoverageAssignment)PlayEditing.Clone(cv);
                    n.Press = DefAdjust.Press == PressMode.Press;
                    call.Assignments[i] = n;
                }
            }
            DefPlay = call;
            for (int i = 0; i < 11; i++) Agents[11 + i].Assign = call.Assignments[i];
            LayoutDefense();
            OffAI.Prepare(); DefAI.Prepare(); Specials.Prepare();
        }

        void LayoutDefense()
        {
            bool anyBlitz = false;
            for (int i = 0; i < 11; i++) if (DefPlay.Assignments[i] is BlitzAssignment) anyBlitz = true;
            int mike = -1;
            for (int i = 0; i < 11; i++) if (DefForm.Slots[i].Position == Position.MLB) { mike = i; break; }
            for (int i = 0; i < 11; i++)
            {
                SlotSpec s = DefForm.Slots[i];
                float x = s.X, y = s.Y;
                Position pos = s.Position;
                if (PositionGroup.IsDefensiveLine(pos))
                {
                    if (DefAdjust.Line == LineShift.Pinch) x *= 0.62f; else if (DefAdjust.Line == LineShift.Spread) x *= 1.32f;
                }
                else if (PositionGroup.IsLinebacker(pos))
                {
                    if (DefAdjust.Backers == LinebackerShift.In) { x *= 0.75f; y = Math.Max(1.4f, y - 1.3f); }
                    else if (DefAdjust.Backers == LinebackerShift.Out) { x *= 1.25f; y += 1.1f; }
                }
                if (pos == Position.CB)
                {
                    if (DefAdjust.Press == PressMode.Press) y = 1.0f;
                    else if (DefAdjust.Press == PressMode.Off) y = Math.Max(y, 7.0f);
                }
                if (DefAdjust.Blitz == BlitzShow.Show)
                {
                    bool blitzer = DefPlay.Assignments[i] is BlitzAssignment;
                    bool decoy = !anyBlitz && (PositionGroup.IsLinebacker(pos) && (i == mike || Math.Abs(s.X) > 4f));
                    if (blitzer || decoy) y = Math.Min(y, pos == Position.CB || PositionGroup.IsSafety(pos) ? 1.6f : 1.2f);
                }
                else if (DefAdjust.Blitz == BlitzShow.BackOff)
                {
                    if (PositionGroup.IsLinebacker(pos)) y += 1.7f;
                    else if (PositionGroup.IsDefensiveBack(pos)) y += 2.0f;
                }
                Vec2 p = ClampInField(new Vec2(BallX0 + x, LosY + y));
                SimAgent a = Agents[11 + i];
                a.Body.Pos = p; a.StartPos = p; a.Body.Vel = Vec2.Zero;
                a.ClearHistory(p);
            }
        }

        // ================================================================================================ fouls
        /// <summary>The offense let the play clock expire: a dead-ball delay of game ends the play before the snap.</summary>
        public bool CallDelayOfGame()
        {
            if (Phase != PlayPhase.PreSnap || Kind != PlayKind.Scrimmage) return false;
            var call = new PenaltyCall(PenaltyType.DelayOfGame, State.Possession, 0f, Agents[0].PlayerId);
            LogEvent(SimEventType.Penalty, 0, -1, TackleType.None, (float)PenaltyType.DelayOfGame, "DelayOfGame");
            FinishPreSnap(PlayResult.PenaltyOnly(call));
            return true;
        }

        /// <summary>Test / scripting hook: blows the play dead before the snap with the given foul.</summary>
        public bool CallPreSnapFoul(PenaltyType type, Team by, int agentId = 0)
        {
            if (Phase != PlayPhase.PreSnap) return false;
            var call = new PenaltyCall(type, by, 0f, Agents[Math.Max(0, Math.Min(21, agentId))].PlayerId);
            LogEvent(SimEventType.Penalty, agentId, -1, TackleType.None, (float)type, type.ToString());
            FinishPreSnap(PlayResult.PenaltyOnly(call));
            return true;
        }

        /// <summary>Short text lines describing the pre-snap changes in effect (for the HUD).</summary>
        public List<string> PreSnapSummary()
        {
            var l = new List<string>();
            if (OffPlay != null && OffPlay.Mirrored) l.Add("FLIPPED");
            foreach (var kv in _hotRoutes) l.Add("HOT " + Agents[kv.Key].Label + " " + PlayEditing.HotRouteName(kv.Value));
            int slide = _slideSet ? SlideDirection : 0; if (slide != 0) l.Add(slide < 0 ? "SLIDE LEFT" : "SLIDE RIGHT");
            if (MotionSlot >= 0) l.Add("MOTION " + Agents[MotionSlot].Label);
            if (DefAdjust != null && !DefAdjust.IsDefault)
            {
                if (DefAdjust.Coverage != CoverageScheme.None) l.Add(DefenseAdjust.CoverageName(DefAdjust.Coverage));
                if (DefAdjust.Press != PressMode.Auto) l.Add(DefAdjust.Press == PressMode.Press ? "PRESS" : "OFF COVERAGE");
                if (DefAdjust.Line != LineShift.Normal) l.Add(DefAdjust.Line == LineShift.Pinch ? "LINE PINCH" : "LINE SPREAD");
                if (DefAdjust.Backers != LinebackerShift.Normal) l.Add(DefAdjust.Backers == LinebackerShift.In ? "LB IN" : "LB OUT");
                if (DefAdjust.Blitz != BlitzShow.None) l.Add(DefAdjust.Blitz == BlitzShow.Show ? "SHOW BLITZ" : "BACK OFF");
            }
            return l;
        }
    }
}

