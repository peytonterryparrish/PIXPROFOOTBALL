using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>The user's pre-snap controls at the line: audibles, hot routes, flip, motion, slide protection; on defense coverage audibles, shifts, show blitz / back off, press / off.</summary>
    public sealed partial class MatchDirector
    {
        enum PsMode { None, Audible, HotPick, HotDir, Motion, CoverageMenu }
        PsMode _ps;
        int _psSlot = -1, _motionCursor = -1;
        readonly int[] _psSlots = new int[5];
        DefenseAdjust _adj = new DefenseAdjust();
        bool _prevLT, _prevRT;
        float _wheelLock;
        readonly PreSnapHud _hud = new PreSnapHud();
        string _psToast = ""; float _psToastT;

        /// <summary>Button order used for receivers in the hot-route and motion modes (Y is the mode key, so it is skipped).</summary>
        static readonly Btn[] ReceiverButtons = { Btn.X, Btn.B, Btn.A, Btn.LB, Btn.RB };

        public bool PreSnapBusy { get { return _ps != PsMode.None; } }
        public string PreSnapMode { get { return _ps.ToString(); } }

        void ResetPreSnap()
        {
            _ps = PsMode.None; _psSlot = -1; _motionCursor = -1; _adj = new DefenseAdjust(); _wheelLock = 0f; _psToast = "";
            if (Ui != null) Ui.SetPreSnap(null);
        }

        Vec2 StickToSim(Vector2 st)
        {
            float yaw = Cams.MoveBasisYaw * Mathf.Deg2Rad, s = Mathf.Sin(yaw), c = Mathf.Cos(yaw);
            Vector2 world = new Vector2(st.x * c + st.y * s, -st.x * s + st.y * c);
            return _frame.DirToSim(world);
        }

        // ================================================================================================ offense
        List<int> ReceiverSlots(Func<int, bool> pred)
        {
            var l = new List<int>();
            for (int i = 1; i < 11; i++) if (pred(i)) l.Add(i);
            l.Sort((a, b) => Sim.Agents[a].Pos.X.CompareTo(Sim.Agents[b].Pos.X));
            return l;
        }

        void PsToast(string s) { _psToast = s; _psToastT = 2.2f; }

        /// <summary>Handles a frame of pre-snap input on offense. Returns true when a snap was requested.</summary>
        bool OffensePreSnapInput(MatchInputFrame inp, ref bool recall)
        {
            bool snap = false;
            // v0.7: LT is held to show the routes on the turf (timeout moved to D-pad down / R3 on the offense)
            _prevLT = inp.LT;
            if (inp.LT) _routeHeld = true;
            if (inp.Timeout && _ps == PsMode.None) TryUserTimeout();
            if (_psToastT > 0f) _psToastT -= Time.unscaledDeltaTime;
            switch (_ps)
            {
                case PsMode.None:
                    if (inp.Down[(int)Btn.A]) snap = true;
                    else if (inp.Down[(int)Btn.X]) { _ps = PsMode.Audible; }
                    else if (inp.Down[(int)Btn.Y])
                    {
                        if (ReceiverSlots(Sim.CanHotRoute).Count > 0) _ps = PsMode.HotPick; else PsToast("NO HOT ROUTES FROM THIS FORMATION");
                    }
                    else if (inp.Down[(int)Btn.LB]) { if (Sim.FlipOffense()) { _offCall = Sim.OffPlay; Audio.CallCue(); /* v0.9.0 CONNECT E-379: not the referee whistle */ PsToast("PLAY FLIPPED"); PlayRebuilt(); } }
                    else if (inp.Down[(int)Btn.RB]) { if (ReceiverSlots(Sim.CanMotion).Count > 0) { _ps = PsMode.Motion; _motionCursor = -1; } else PsToast("NO MOTION FROM THIS FORMATION"); }
                    else if (inp.DPadLeft) SlideProtect(-1);
                    else if (inp.DPadRight) SlideProtect(1);
                    else if (inp.DPadUp && Ui != null && !Autopilot) recall = true;
                    else if (inp.DPadDown) TryUserTimeout();
                    break;
                case PsMode.Audible:
                    if (inp.Down[(int)Btn.X]) { _ps = PsMode.None; break; }
                    for (int k = 0; k < 5; k++)
                    {
                        Btn b = AudibleButtons[k];
                        if (!inp.Down[(int)b]) continue;
                        Play p = Flow.Audibles.Get(Sim.OffPlay, k);
                        if (p != null && Sim.ChangeOffensePlay(p)) { _offCall = Sim.OffPlay; PsToast("AUDIBLE: " + p.Name.ToUpperInvariant()); Audio.CallCue(); /* v0.9.0 CONNECT E-379 */ _ps = PsMode.None; PlayRebuilt(); }
                    }
                    break;
                case PsMode.HotPick:
                    if (inp.Down[(int)Btn.Y] || inp.Menu) { _ps = PsMode.None; break; }
                    {
                        var cands = ReceiverSlots(Sim.CanHotRoute);
                        for (int k = 0; k < cands.Count && k < ReceiverButtons.Length; k++)
                            if (inp.Down[(int)ReceiverButtons[k]]) { _psSlot = cands[k]; _ps = PsMode.HotDir; _wheelLock = 0.25f; }
                    }
                    break;
                case PsMode.HotDir:
                    if (inp.Down[(int)Btn.Y]) { _ps = PsMode.None; break; }
                    if (inp.Down[(int)Btn.B]) { _ps = PsMode.HotPick; break; }
                    _wheelLock -= Time.unscaledDeltaTime;
                    if (_wheelLock <= 0f && inp.Move.magnitude > 0.7f)
                    {
                        HotRoute hr = HotFromStick(_psSlot, inp.Move);
                        if (Sim.SetHotRoute(_psSlot, hr)) { _offCall = Sim.OffPlay; PsToast("HOT ROUTE: " + Sim.Agents[_psSlot].Label + " " + PlayEditing.HotRouteName(hr)); }
                        _ps = PsMode.HotPick;
                    }
                    break;
                case PsMode.Motion:
                    if (inp.Down[(int)Btn.A]) { snap = true; break; }
                    if (inp.Down[(int)Btn.Y] || inp.Down[(int)Btn.B]) { Sim.StopMotion(); _ps = PsMode.None; break; }
                    {
                        var cands = ReceiverSlots(Sim.CanMotion);
                        if (inp.Down[(int)Btn.RB] || _motionCursor < 0)
                        {
                            if (_motionCursor < 0 && !inp.Down[(int)Btn.RB]) _motionCursor = 0;
                            else { _motionCursor++; if (_motionCursor >= cands.Count) { _motionCursor = -1; Sim.StopMotion(); _ps = PsMode.None; break; } }
                        }
                        if (_motionCursor >= 0 && _motionCursor < cands.Count)
                        {
                            int slot = cands[_motionCursor];
                            if (inp.Move.magnitude > 0.7f && Mathf.Abs(inp.Move.x) > Mathf.Abs(inp.Move.y))
                            {
                                Vec2 d = StickToSim(new Vector2(Mathf.Sign(inp.Move.x), 0f));
                                if (Sim.SetMotion(slot, d.X >= 0f ? 1 : -1)) { PsToast("MOTION: " + Sim.Agents[slot].Label); PlayRebuilt(); }
                            }
                        }
                    }
                    break;
            }
            return snap;
        }

        static readonly Btn[] AudibleButtons = { Btn.A, Btn.B, Btn.Y, Btn.LB, Btn.RB };

        void SlideProtect(int dir)
        {
            if (!PlayEditing.HasPassProtection(Sim.OffPlay)) { PsToast("NO PASS PROTECTION ON A RUN"); return; }
            int cur = Sim.SlideDirection;
            int want = cur == dir ? 0 : dir;
            if (Sim.SetSlideProtection(want)) { _offCall = Sim.OffPlay; PsToast(want == 0 ? "PROTECTION: STANDARD" : (want < 0 ? "SLIDE PROTECTION LEFT" : "SLIDE PROTECTION RIGHT")); }
        }

        /// <summary>The hot route a stick direction picks for the receiver: up = streak, up and out = fade, out = out, down = curl, up and in = slant, in = in, down diagonals = block.</summary>
        HotRoute HotFromStick(int slot, Vector2 stick)
        {
            Vec2 d = StickToSim(stick.normalized);
            return HotFromSim(slot, d);
        }

        HotRoute HotFromSim(int slot, Vec2 d)
        {
            float side = Sim.OffForm.Slots[slot].X < -0.5f ? -1f : 1f;
            float lat = d.X * side;      // outward positive
            float ang = Mathf.Atan2(lat, d.Y) * Mathf.Rad2Deg;   // 0 = downfield, 90 = outward
            float a = Mathf.Abs(ang);
            if (a < 22.5f) return HotRoute.Streak;
            if (a < 67.5f) return ang > 0f ? HotRoute.Fade : HotRoute.Slant;
            if (a < 112.5f) return ang > 0f ? HotRoute.Out : HotRoute.In;
            if (a < 157.5f) return ang > 0f ? HotRoute.Curl : HotRoute.Block;   // the two back diagonals are different routes now (outward back = curl, inward back = stay in and block) (QA-060)
            return HotRoute.Curl;
        }

        // ================================================================================================ defense
        bool DefensePreSnapInput(MatchInputFrame inp, ref bool recall)
        {
            bool lt = inp.LT, ltEdge = lt && !_prevLT; _prevLT = lt;
            if (ltEdge) TryUserTimeout();
            if (_psToastT > 0f) _psToastT -= Time.unscaledDeltaTime;
            bool changed = false;
            if (_ps == PsMode.CoverageMenu)
            {
                if (inp.Down[(int)Btn.X]) { _ps = PsMode.None; return false; }
                for (int k = 0; k < 5; k++)
                {
                    Btn b = AudibleButtons[k];
                    if (!inp.Down[(int)b]) continue;
                    CoverageScheme cs = DefenseAdjust.CoverageChoices[k];
                    if (DefenseAdjust.FindCoverage(Flow.Book, Sim.DefPlay, cs) == null && cs != Sim.DefPlay.Scheme) { PsToast(DefenseAdjust.CoverageName(cs) + " NOT AVAILABLE IN THIS FORMATION"); continue; }
                    _adj.Coverage = cs; changed = true; _ps = PsMode.None; PsToast("COVERAGE: " + DefenseAdjust.CoverageName(cs));
                }
            }
            else
            {
                if (inp.Down[(int)Btn.X]) _ps = PsMode.CoverageMenu;
                else if (inp.Down[(int)Btn.Y]) { _adj.Press = _adj.Press == PressMode.Auto ? PressMode.Press : (_adj.Press == PressMode.Press ? PressMode.Off : PressMode.Auto); changed = true; PsToast(_adj.Press == PressMode.Press ? "PRESS COVERAGE" : (_adj.Press == PressMode.Off ? "OFF COVERAGE" : "COVERAGE: AS CALLED")); }
                else if (inp.Down[(int)Btn.LB]) { _adj.Line = _adj.Line == LineShift.Normal ? LineShift.Pinch : (_adj.Line == LineShift.Pinch ? LineShift.Spread : LineShift.Normal); changed = true; PsToast("LINE: " + _adj.Line.ToString().ToUpperInvariant()); }
                else if (inp.Down[(int)Btn.RB]) { _adj.Backers = _adj.Backers == LinebackerShift.Normal ? LinebackerShift.In : (_adj.Backers == LinebackerShift.In ? LinebackerShift.Out : LinebackerShift.Normal); changed = true; PsToast("LINEBACKERS: " + _adj.Backers.ToString().ToUpperInvariant()); }
                else if (inp.DPadLeft) { _adj.Blitz = _adj.Blitz == BlitzShow.Show ? BlitzShow.None : BlitzShow.Show; changed = true; PsToast(_adj.Blitz == BlitzShow.Show ? "SHOW BLITZ" : "BLITZ SHOW OFF"); }
                else if (inp.DPadRight) { _adj.Blitz = _adj.Blitz == BlitzShow.BackOff ? BlitzShow.None : BlitzShow.BackOff; changed = true; PsToast(_adj.Blitz == BlitzShow.BackOff ? "BACK OFF" : "BACK OFF CANCELLED"); }
                else if (inp.DPadUp && Ui != null && !Autopilot) recall = true;
                else if (inp.DPadDown) TryUserTimeout();
                else if (inp.Down[(int)Btn.B]) { _defUser = CycleDefender(_defUser); Audio.CallCue(); /* v0.9.0 CONNECT E-379 */ }
            }
            if (changed && Sim.SetDefenseAdjust(_adj)) { _defCall = Sim.DefPlay; }
            return false;
        }

        /// <summary>B on defense: the next defender (left to right as the offense sees them).</summary>
        int CycleDefender(int current)
        {
            var ids = new List<int>();
            for (int i = 11; i < 22; i++) ids.Add(i);
            ids.Sort((a, b) => Sim.Agents[a].Pos.X.CompareTo(Sim.Agents[b].Pos.X));
            int idx = ids.IndexOf(current);
            return ids[(idx + 1 + ids.Count) % ids.Count];
        }

        // ================================================================================================ HUD
        void UpdatePreSnapHud(float playClock)
        {
            if (Ui == null || Config.CpuVsCpu || Autopilot) return;
            var h = _hud;
            h.Hints.Clear(); h.Adjustments.Clear(); h.Menu.Clear(); h.Wheel.Clear(); h.Icons.Clear(); h.Book = Flow.Book;
            bool off = _userIsOffense;
            h.OffenseSide = off;
            h.PlayName = Sim.OffPlay != null && off ? Sim.OffPlay.Name : (Sim.DefPlay != null ? Sim.DefPlay.Name : "");
            h.Formation = off ? (Sim.OffForm != null ? Sim.OffForm.Name : "") : (Sim.DefForm != null ? Sim.DefForm.Name : "");
            h.Seconds = Mathf.CeilToInt(playClock);
            h.Adjustments.AddRange(Sim.PreSnapSummary());
            h.Mode = _psToastT > 0f ? _psToast : "";
            if (Sim.Kind != PlayKind.Scrimmage) { Ui.SetPreSnap(null); return; }
            if (off)
            {
                switch (_ps)
                {
                    case PsMode.None:
                        if (Routes != null && Routes.Shown) AddRouteIcons(h.Icons);   // the pass buttons over the receivers, like the route screen of a modern football game
                        h.Hints.Add(new HintPair("glyph_a", "SNAP")); h.Hints.Add(new HintPair("glyph_x", "AUDIBLE")); h.Hints.Add(new HintPair("glyph_y", "HOT ROUTE")); h.Hints.Add(new HintPair("glyph_lb", "FLIP"));
                        h.Hints.Add(new HintPair("glyph_rb", "MOTION")); h.Hints.Add(new HintPair("glyph_dpad_left", "SLIDE")); h.Hints.Add(new HintPair("glyph_dpad_up", "NEW PLAY")); h.Hints.Add(new HintPair("glyph_lt", "HOLD: ROUTES")); h.Hints.Add(new HintPair("glyph_dpad_down", "TIMEOUT"));
                        break;
                    case PsMode.Audible:
                        h.MenuTitle = "AUDIBLE";
                        for (int k = 0; k < 5; k++)
                        {
                            Play p = Flow.Audibles.Get(Sim.OffPlay, k);
                            h.Menu.Add(new MenuRow { Button = AudibleButtons[k], Label = p != null ? p.Name : "-", Tag = p != null ? TagOf(p) : "", Dim = p == null, Play = p });
                        }
                        h.Hints.Add(new HintPair("", "PICK A BUTTON")); h.Hints.Add(new HintPair("glyph_x", "CANCEL"));
                        break;
                    case PsMode.HotPick:
                        {
                            h.Mode = "HOT ROUTE: CHOOSE A RECEIVER";
                            var c = ReceiverSlots(Sim.CanHotRoute);
                            for (int k = 0; k < c.Count && k < ReceiverButtons.Length; k++) h.Icons.Add(IconFor(c[k], ReceiverButtons[k], false, false));
                            h.Hints.Add(new HintPair("", "PRESS A RECEIVER'S BUTTON")); h.Hints.Add(new HintPair("glyph_y", "DONE"));
                            break;
                        }
                    case PsMode.HotDir:
                        {
                            h.Mode = "HOT ROUTE: " + Sim.Agents[_psSlot].Label;
                            h.WheelTitle = Sim.Agents[_psSlot].Label + "\nROUTE";
                            for (int k = 0; k < 8; k++)
                            {
                                float a = k * 45f * Mathf.Deg2Rad;
                                Vec2 d = StickToSim(new Vector2(Mathf.Sin(a), Mathf.Cos(a)));
                                HotRoute hr = HotFromSim(_psSlot, d);
                                h.Wheel.Add(new WheelOption { Slot = k, Label = PlayEditing.HotRouteName(hr), Selected = false });
                            }
                            h.Icons.Add(IconFor(_psSlot, Btn.A, true, true));
                            h.Hints.Add(new HintPair("glyph_ls", "PICK A DIRECTION")); h.Hints.Add(new HintPair("glyph_b", "BACK")); h.Hints.Add(new HintPair("glyph_y", "DONE"));
                            break;
                        }
                    case PsMode.Motion:
                        {
                            h.Mode = "MOTION";
                            var c = ReceiverSlots(Sim.CanMotion);
                            for (int k = 0; k < c.Count; k++) h.Icons.Add(IconFor(c[k], Btn.A, true, k == _motionCursor));
                            h.Hints.Add(new HintPair("glyph_rb", "NEXT PLAYER")); h.Hints.Add(new HintPair("glyph_ls", "SEND LEFT / RIGHT")); h.Hints.Add(new HintPair("glyph_a", "SNAP")); h.Hints.Add(new HintPair("glyph_b", "CANCEL"));
                            break;
                        }
                }
            }
            else
            {
                if (_ps == PsMode.CoverageMenu)
                {
                    h.MenuTitle = "COVERAGE";
                    for (int k = 0; k < 5; k++)
                    {
                        CoverageScheme cs = DefenseAdjust.CoverageChoices[k];
                        bool ok = cs == Sim.DefPlay.Scheme || DefenseAdjust.FindCoverage(Flow.Book, Sim.DefPlay, cs) != null;
                        h.Menu.Add(new MenuRow { Button = AudibleButtons[k], Label = DefenseAdjust.CoverageName(cs), Tag = cs == CoverageScheme.Cover0 || cs == CoverageScheme.Cover1 ? "MAN" : "ZONE", Selected = _adj.Coverage == cs, Dim = !ok });
                    }
                    h.Hints.Add(new HintPair("", "PICK A COVERAGE")); h.Hints.Add(new HintPair("glyph_x", "CANCEL"));
                }
                else
                {
                    h.Hints.Add(new HintPair("glyph_x", "COVERAGE")); h.Hints.Add(new HintPair("glyph_y", "PRESS / OFF")); h.Hints.Add(new HintPair("glyph_lb", "LINE SHIFT")); h.Hints.Add(new HintPair("glyph_rb", "LB SHIFT"));
                    h.Hints.Add(new HintPair("glyph_dpad_left", "SHOW BLITZ")); h.Hints.Add(new HintPair("glyph_dpad_right", "BACK OFF")); h.Hints.Add(new HintPair("glyph_b", "SWITCH PLAYER")); h.Hints.Add(new HintPair("glyph_lt", "TIMEOUT"));
                }
            }
            Ui.SetPreSnap(h);
            // receiver / motion icons ride the same icon layer as the throw icons
            Ui.SetReceiverIcons(h.Icons.Count > 0 ? h.Icons : null, MainCamera);
        }

        ReceiverIcon IconFor(int slot, Btn b, bool arrow, bool highlight)
        {
            return new ReceiverIcon { World = Stage.WorldPos(slot) + Vector3.up * 2.45f, Button = b, Label = Sim.Agents[slot].Label, Arrow = arrow, Highlight = highlight };
        }

        static string TagOf(Play p)
        {
            if (p.IsRun) return "RUN";
            if (p.Category == PlayCategory.PlayAction) return "RPO";
            return "PASS";
        }
    }
}

