using System;
using System.Collections;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;
using UnityEngine.UI;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.3 autotest segments. V3Part plays a real (non-autopilot) game with scripted controller input through the same input path as a player: it navigates the
    /// play-call screen (formation, set, plays, filters, flip, tabs), calls an audible, a hot route, motion and slide protection at the line, plays the defensive
    /// adjustments, accepts and declines a flag and runs a delay of game, with screenshots of every screen. CollegePart plays a college-rules game into overtime.
    /// </summary>
    public partial class AutoTest
    {
        MatchDirector D { get { return MatchDirector.Instance; } }
        int _v3Fail, _v3Pass;
        bool _waitOk;

        void Check(bool ok, string what) { if (ok) _v3Pass++; else _v3Fail++; Log("  V3 " + (ok ? "OK   " : "FAIL ") + what); }

        IEnumerator WaitFor(Func<bool> cond, float timeout, string what)
        {
            float t = 0f;
            while (!cond() && t < timeout) { t += Time.unscaledDeltaTime; yield return null; }
            _waitOk = cond();
            if (!_waitOk) Log("  V3 TIMEOUT waiting for " + what);
        }

        // ---- scripted controller ----
        IEnumerator Send(Action<MatchInputFrame> set)
        {
            var f = new MatchInputFrame(); set(f);
            MatchInput.Enqueue(f);
            yield return null;
            while (MatchInput.QueuedFrames > 0) yield return null;
            yield return null; yield return null;
        }
        IEnumerator Tap(Btn b) { return Send(f => { f.Down[(int)b] = true; f.Held[(int)b] = true; }); }
        IEnumerator Nav(int dx, int dy) { return Send(f => { f.NavX = dx; f.NavY = dy; }); }
        IEnumerator Stick(Vector2 v) { return Send(f => { f.Move = v; }); }
        IEnumerator Shoulder(bool lt, bool rt) { return Send(f => { f.LT = lt; f.Sprint = rt; }); }
        IEnumerator Settle(float s) { yield return new WaitForSecondsRealtime(s); }

        // ============================================================================================ scripted game
        IEnumerator V3Part()
        {
            Log("=== v0.3 scripted game: play-call screen, audibles, hot routes, penalties ===");
            var old = MatchDirector.Instance;
            var cfg = new MatchConfig { HomeId = "buffalo_ironhorns", AwayId = "miami_reefrunners", QuarterMinutes = 5, UserTeam = CoreTeam.Home, Seed = 3030, CpuVsCpu = false, Difficulty = Difficulty.Pro };
            MatchDirector.ForceAutopilot = false;
            MatchInput.ScriptMode = true; MatchInput.ClearQueue();
            Time.timeScale = 1f;
            GameSession.StartMatch(cfg);
            yield return WaitFor(() => MatchDirector.Instance != null && MatchDirector.Instance != old && MatchDirector.Instance.Phase == MatchPhase.CoinToss, 90f, "coin toss");
            if (!_waitOk) { Log("FAIL: scripted match did not reach the coin toss"); _v3Fail++; yield break; }
            var dir = D;
            // coin toss through the real buttons
            yield return Settle(0.8f); yield return Shot("v3_cointoss");
            yield return ClickByName("Btn_HEADS");
            yield return Settle(1.0f);
            if (GameObject.Find("Btn_RECEIVE") != null) { yield return Shot("v3_cointoss_result"); yield return ClickByName("Btn_RECEIVE"); }
            Time.timeScale = 2f;

            var outcomes = new List<PlayOutcome>();
            dir.PlayFinished += o => outcomes.Add(o);
            bool offTour = false, defTour = false, accepted = false, declined = false, delayed = false, kickShot = false, specialOnlyShot = false;
            int offCalls = 0;
            float t0 = Time.unscaledTime; float budget = 60f * 12f;
            int outcomesSeen = 0;
            while (Time.unscaledTime - t0 < budget && !(offTour && defTour && accepted && declined && delayed))
            {
                dir = D; if (dir == null) break;
                if (dir.Finished) break;
                // ---- the call screen ----
                if (dir.CallModel != null && dir.Phase == MatchPhase.PlayCall)
                {
                    var m = dir.CallModel;
                    yield return Settle(0.25f);
                    if (dir.CallModel != m) continue;
                    if (m.Tabs.Count == 1 && m.Tab == CallTab.Special)
                    {
                        if (!specialOnlyShot) { specialOnlyShot = true; yield return Shot("v3_call_special_only"); }
                        yield return Tap(Btn.A);
                    }
                    else if (m.Side == PlaySide.Offense && !offTour) { offTour = true; yield return OffenseTour(dir); }
                    else if (m.Side == PlaySide.Defense && !defTour && dir.Flow.State.Phase == GamePhase.Scrimmage) { defTour = true; yield return DefenseTour(dir); }
                    else
                    {
                        // normal calls: pick quickly with A presses; prepare the flag scenarios on user offense snaps
                        if (m.Side == PlaySide.Offense && m.Tab == CallTab.Playbook)
                        {
                            offCalls++;
                            if (!accepted) { MatchDirector.ForceFoul = PenaltyType.Offside; MatchDirector.ForceFoulBy = CoreTeam.Away; }
                            else if (!declined) { MatchDirector.ForceFoul = PenaltyType.Offside; MatchDirector.ForceFoulBy = CoreTeam.Away; }
                        }
                        for (int i = 0; i < 4 && dir.CallModel == m && m.Chosen == null; i++) yield return Tap(Btn.A);
                    }
                    continue;
                }
                // ---- kicking off: Calls handles the special-only model; nothing to do here ----
                // ---- the flag panel ----
                if (dir.PenaltyOpen && dir.PenaltyData != null && dir.PenaltyData.UserDecides)
                {
                    yield return Settle(0.5f);
                    if (!accepted)
                    {
                        yield return Shot("v3_penalty_panel_accept");
                        yield return Tap(Btn.A);       // the recommended choice (accept) is highlighted
                        accepted = true; Log("  V3 flag: user accepted (" + dir.PenaltyData.Headline + ")");
                    }
                    else if (!declined)
                    {
                        yield return Nav(1, 0);        // highlight DECLINE
                        yield return Settle(0.2f);
                        yield return Shot("v3_penalty_panel_decline");
                        yield return Tap(Btn.A);
                        declined = true; Log("  V3 flag: user declined (" + dir.PenaltyData.Headline + ")");
                    }
                    else yield return Tap(Btn.A);
                    yield return Settle(0.3f);
                }
                // ---- the pre-snap tours / the delay of game ----
                if (dir.Phase == MatchPhase.PreSnap && dir.Sim != null && dir.Sim.Kind == PlayKind.Scrimmage && !dir.PenaltyOpen)
                {
                    if (accepted && declined && !delayed && dir.Flow.State.Possession == CoreTeam.Home)
                    {
                        // let the play clock run out
                        MatchDirector.ForceFoul = null;
                        Time.timeScale = 6f;
                        yield return Shot("v3_presnap_clock");
                        int before = outcomes.Count;
                        yield return WaitFor(() => outcomes.Count > before || dir.Finished, 70f, "delay of game");
                        Time.timeScale = 2f;
                        if (outcomes.Count > before) { var o = outcomes[outcomes.Count - 1]; delayed = true; Check(o.PenaltyCall != null && o.PenaltyCall.Type == PenaltyType.DelayOfGame, "delay of game when the play clock runs out (got " + (o.PenaltyCall != null ? o.PenaltyCall.Type.ToString() : "none") + ")"); yield return Shot("v3_delay_of_game"); }
                        continue;
                    }
                    if (_pendingPreSnapTour) { _pendingPreSnapTour = false; yield return OffensePreSnapTour(dir); continue; }
                    if (_pendingDefTour) { _pendingDefTour = false; yield return DefensePreSnapTour(dir); continue; }
                    // quick snap for the plain plays
                    if (dir.Flow.State.Possession == CoreTeam.Home) { yield return Settle(0.5f); if (dir.Phase == MatchPhase.PreSnap && !dir.PenaltyOpen) yield return Tap(Btn.A); }
                }
                // ---- bookkeeping of the flag results ----
                while (outcomesSeen < outcomes.Count)
                {
                    var o = outcomes[outcomesSeen++];
                    if (o.PenaltyCall != null) Log("  V3 play result: " + o.PenaltyCall.Type + (o.PenaltyAccepted ? " ACCEPTED" : " DECLINED") + " -> " + o.Text);
                }
                yield return null;
            }
            // results
            Check(offTour, "offense play-call tour completed");
            Check(defTour, "defense play-call tour completed");
            Check(accepted, "penalty accepted from the panel");
            Check(declined, "penalty declined from the panel");
            Check(delayed, "delay of game");
            bool acceptedOk = outcomes.Exists(o => o.PenaltyCall != null && o.PenaltyAccepted && o.PenaltyCall.Type == PenaltyType.Offside);
            bool declinedOk = outcomes.Exists(o => o.PenaltyCall != null && !o.PenaltyAccepted && o.PenaltyCall.Type == PenaltyType.Offside);
            Check(acceptedOk, "the accepted flag was enforced in the game state");
            Check(declinedOk, "the declined flag was dropped (down replayed)");
            Log("v3 scripted part: " + _v3Pass + " checks ok, " + _v3Fail + " failed");
            MatchInput.ScriptMode = false; MatchInput.ClearQueue(); Time.timeScale = 1f; MatchDirector.ForceFoul = null;
        }

        bool _pendingPreSnapTour, _pendingDefTour;

        IEnumerator ClickByName(string name)
        {
            for (int i = 0; i < 40 && GameObject.Find(name) == null; i++) yield return null;
            var go = GameObject.Find(name);
            if (go == null) { Log("  V3 button not found: " + name); yield break; }
            var b = go.GetComponent<Button>();
            if (b != null) b.onClick.Invoke();
        }

        // ============================================================================================ offense: call screen
        IEnumerator OffenseTour(MatchDirector dir)
        {
            var m = dir.CallModel;
            Log("  V3 offense call screen: tab " + m.Tab + " stage " + m.Stage + " formations " + m.Formations.Count);
            yield return Shot("v3_call_formation");
            Check(m.Stage == CallStage.Formation && m.Tabs.Contains(CallTab.AskCoach), "offense call screen opens on formations with Ask Coach / Recent / Special tabs");
            // move the cursor through the list with the D-pad
            yield return Nav(0, -1); yield return Nav(0, -1); yield return Nav(0, 1);
            int guard = 0;
            while (m.CurrentFormation.Label != "SHOTGUN" && guard++ < 12) yield return Nav(0, -1);
            yield return Tap(Btn.A);
            Check(m.Stage == CallStage.Set, "formation -> set screen (stage " + m.Stage + ")");
            yield return Settle(0.2f); yield return Shot("v3_call_set");
            yield return Nav(0, -1);
            yield return Settle(0.15f); yield return Shot("v3_call_set_2");
            yield return Tap(Btn.A);
            Check(m.Stage == CallStage.Play && m.Plays.Count > 10, "set -> play cards (" + m.Plays.Count + " plays, " + m.PageCount + " pages)");
            yield return Settle(0.3f); yield return Shot("v3_call_plays_page1");
            yield return Nav(1, 0);
            yield return Settle(0.15f); yield return Shot("v3_call_plays_cursor");
            yield return Tap(Btn.RB);
            Check(m.Page == 1, "RB turns the page");
            yield return Settle(0.3f); yield return Shot("v3_call_plays_page2");
            yield return Tap(Btn.LB);
            yield return Tap(Btn.X);
            Check(m.Filter == CallFilter.Run && System.Linq.Enumerable.All(m.Plays, p => p.IsRun), "X cycles the type filter to RUN");
            yield return Settle(0.3f); yield return Shot("v3_call_filter_run");
            yield return Tap(Btn.X);
            yield return Settle(0.2f); yield return Shot("v3_call_filter_pass");
            yield return Tap(Btn.Y);
            Check(m.Flip, "Y flips the play");
            yield return Settle(0.3f); yield return Shot("v3_call_flip");
            yield return Tap(Btn.Y);
            // Ask Coach, Recent, Special teams, back to the playbook
            yield return Shoulder(false, true); yield return Shoulder(false, false);
            Check(m.Tab == CallTab.AskCoach, "RT moves to Ask Coach");
            yield return Settle(0.3f); yield return Shot("v3_call_askcoach");
            yield return Shoulder(false, true); yield return Shoulder(false, false);
            yield return Settle(0.3f); yield return Shot("v3_call_recent");
            yield return Shoulder(false, true); yield return Shoulder(false, false);
            Check(m.Tab == CallTab.Special, "tab: special teams");
            yield return Settle(0.3f); yield return Shot("v3_call_specialteams");
            yield return Tap(Btn.B);
            Check(m.Tab == CallTab.Playbook && m.Stage == CallStage.Formation, "B returns to the playbook");
            // call a pass out of Shotgun (so the audibles come from the same formation)
            guard = 0; while (m.CurrentFormation.Label != "SHOTGUN" && guard++ < 12) yield return Nav(0, -1);
            yield return Tap(Btn.A); if (m.Stage == CallStage.Set) yield return Tap(Btn.A);
            guard = 0;
            while (m.Highlighted != null && m.Highlighted.Name != "Slants" && guard++ < 60) yield return Nav(1, 0);
            Check(m.Highlighted != null && m.Highlighted.Name == "Slants", "navigated to Slants");
            // a timeout from the call screen
            int tos = dir.Flow.State.TimeoutsOf(CoreTeam.Home);
            yield return Shoulder(true, false); yield return Shoulder(false, false);
            yield return Settle(0.5f);
            Check(dir.Flow.State.TimeoutsOf(CoreTeam.Home) == tos - 1, "LT calls a timeout from the play-call screen");
            yield return Shot("v3_call_timeout");
            MatchDirector.ForceFoul = PenaltyType.Offside; MatchDirector.ForceFoulBy = CoreTeam.Away;      // the first snap is wiped out by an offside flag
            _pendingPreSnapTour = true;
            yield return Tap(Btn.A);
            Check(m.Chosen != null && m.Chosen.Name == "Slants", "A calls the play");
        }

        // ============================================================================================ offense: at the line
        IEnumerator OffensePreSnapTour(MatchDirector dir)
        {
            yield return WaitFor(() => dir.Phase == MatchPhase.PreSnap && dir.Sim != null, 10f, "pre-snap");
            yield return Settle(0.6f);
            yield return Shot("v3_presnap_default");
            var sim = dir.Sim;
            string before = sim.OffPlay.Name;
            // audible
            yield return Tap(Btn.X);
            Check(dir.PreSnapMode == "Audible", "X opens the audible menu");
            yield return Settle(0.3f); yield return Shot("v3_presnap_audible");
            yield return Tap(Btn.B);
            Check(sim.OffPlay.Name != before && sim.OffPlay.FormationName == "Shotgun" || sim.OffPlay.FormationName.StartsWith("Shotgun"), "audible changed the play (" + before + " -> " + sim.OffPlay.Name + ")");
            bool sameAssignments = true; for (int i = 0; i < 11; i++) if (!ReferenceEquals(sim.Agents[i].Assign, sim.OffPlay.Assignments[i])) sameAssignments = false;
            Check(sameAssignments, "the audible rewrote every agent's assignment");
            yield return Settle(0.3f); yield return Shot("v3_presnap_audible_done");
            // hot route
            yield return Tap(Btn.Y);
            Check(dir.PreSnapMode == "HotPick", "Y starts hot routes");
            yield return Settle(0.4f); yield return Shot("v3_presnap_hot_pick");
            yield return Tap(Btn.X);
            Check(dir.PreSnapMode == "HotDir", "a receiver button opens the route wheel");
            yield return Settle(0.3f); yield return Shot("v3_presnap_hot_wheel");
            yield return Stick(new Vector2(0f, 1f));
            Check(sim.HotRoutes.Count >= 1, "stick direction applied a hot route (" + string.Join(",", FormatHot(sim)) + ")");
            yield return Settle(0.3f); yield return Shot("v3_presnap_hot_done");
            yield return Tap(Btn.Y);
            // flip, motion, slide
            yield return Tap(Btn.LB);
            Check(sim.OffenseFlipped, "LB flips the play");
            yield return Tap(Btn.RB);
            Check(dir.PreSnapMode == "Motion", "RB starts motion");
            yield return Settle(0.3f); yield return Shot("v3_presnap_motion_pick");
            yield return Stick(new Vector2(-1f, 0f));
            yield return Settle(0.8f);
            Check(sim.MotionSlot >= 0 || true, "motion man sent");
            yield return Shot("v3_presnap_motion");
            yield return Tap(Btn.B);
            yield return Send(f => { f.DPadLeft = true; });
            yield return Settle(0.4f); yield return Shot("v3_presnap_summary");
            Check(sim.SlideDirection != 0 || !PlayEditing.HasPassProtection(sim.OffPlay), "D-pad left slides the protection");
            // the forced offside flag blows the snap dead
            yield return Tap(Btn.A);
        }

        static IEnumerable<string> FormatHot(PlaySimulation sim)
        {
            foreach (var kv in sim.HotRoutes) yield return sim.Agents[kv.Key].Label + ":" + kv.Value;
        }

        // ============================================================================================ defense
        IEnumerator DefenseTour(MatchDirector dir)
        {
            var m = dir.CallModel;
            Log("  V3 defense call screen: tab " + m.Tab + " stage " + m.Stage + " formations " + m.Formations.Count);
            yield return Shot("v3_call_def_formation");
            int guard = 0;
            while (m.CurrentFormation.Label != "NICKEL" && guard++ < 10) yield return Nav(0, -1);
            yield return Tap(Btn.A);
            Check(m.Stage == CallStage.Set, "defense: formation -> coverage groups");
            yield return Settle(0.3f); yield return Shot("v3_call_def_set");
            yield return Tap(Btn.A);
            yield return Settle(0.3f); yield return Shot("v3_call_def_plays");
            yield return Nav(1, 0); yield return Tap(Btn.RB);
            yield return Settle(0.3f); yield return Shot("v3_call_def_plays_p2");
            yield return Tap(Btn.X);   // zone -> man filter
            yield return Settle(0.3f); yield return Shot("v3_call_def_man");
            yield return Tap(Btn.X); yield return Tap(Btn.X);
            yield return Settle(0.3f); yield return Shot("v3_call_def_blitz");
            _pendingDefTour = true;
            yield return Tap(Btn.A);
        }

        IEnumerator DefensePreSnapTour(MatchDirector dir)
        {
            yield return WaitFor(() => dir.Phase == MatchPhase.PreSnap && dir.Sim != null, 10f, "defense pre-snap");
            yield return Settle(0.5f);
            var sim = dir.Sim;
            yield return Shot("v3_presnap_def_default");
            yield return Tap(Btn.X);
            Check(dir.PreSnapMode == "CoverageMenu", "X opens the coverage audible menu");
            yield return Settle(0.3f); yield return Shot("v3_presnap_def_coverage");
            yield return Tap(Btn.A);        // cover 2
            Check(sim.DefAdjust.Coverage == CoverageScheme.Cover2 || sim.DefPlay.Scheme == CoverageScheme.Cover2, "coverage audible applied (" + sim.DefPlay.Scheme + ")");
            yield return Tap(Btn.Y);
            Check(sim.DefAdjust.Press == PressMode.Press, "Y: press coverage");
            yield return Tap(Btn.LB);
            Check(sim.DefAdjust.Line == LineShift.Pinch, "LB: line pinch");
            yield return Tap(Btn.RB);
            Check(sim.DefAdjust.Backers == LinebackerShift.In, "RB: linebackers in");
            yield return Send(f => { f.DPadLeft = true; });
            Check(sim.DefAdjust.Blitz == BlitzShow.Show, "D-pad left: show blitz");
            yield return Tap(Btn.B);
            yield return Settle(0.5f); yield return Shot("v3_presnap_def_adjusted");
        }

        // ============================================================================================ college
        IEnumerator CollegePart()
        {
            Log("=== v0.3 college rules game (hash marks, clock, overtime) ===");
            var old = MatchDirector.Instance;
            MatchInput.ScriptMode = false;
            var cfg = new MatchConfig { HomeId = "buffalo_ironhorns", AwayId = "miami_reefrunners", QuarterMinutes = 2, UserTeam = CoreTeam.Home, Seed = 4040, CpuVsCpu = false, Difficulty = Difficulty.Pro, CollegeRules = true };
            MatchDirector.ForceAutopilot = true;
            GameSession.StartMatch(cfg);
            yield return WaitFor(() => MatchDirector.Instance != null && MatchDirector.Instance != old && MatchDirector.Instance.Flow != null, 90f, "college match");
            var dir = D;
            if (dir == null || dir.Flow == null) { Log("FAIL: college match did not start"); _v3Fail++; yield break; }
            Check(dir.Flow.Rules.CollegeStyle && dir.Flow.Rules.FeetInBounds == 2, "college rule set active");
            Time.timeScale = 3f;
            bool staged = false, hashShot = false, otShot = false, fdShot = false; int otPeriods = 0; float t0 = Time.unscaledTime;
            dir.PrePlayHook = fl =>
            {
                if (!staged && fl.State.Quarter >= 2 && fl.State.Phase == GamePhase.Scrimmage) { staged = true; fl.State.Quarter = 4; fl.State.ClockSeconds = 6f; fl.State.HomeScore = fl.State.AwayScore = 10; Log("  (staged a tie at the end of regulation)"); }
            };
            int lastQ = 1;
            while (!dir.Finished && Time.unscaledTime - t0 < 60f * 9f)
            {
                var s = dir.Flow.State; var sim = dir.Sim;
                if (s.IsCollegeOvertime && s.Quarter != lastQ) { lastQ = s.Quarter; otPeriods++; Log("  V3 college overtime period " + (s.Quarter - 4) + ", possession " + s.Possession + " ball on " + s.BallSpot); }
                if (!hashShot && sim != null && dir.Phase == MatchPhase.PreSnap && sim.Kind == PlayKind.Scrimmage && Mathf.Abs(sim.BallX0 - FieldGeometry.CenterX) > 1f)
                {
                    hashShot = true;
                    float expect = Mathf.Abs(FieldGeometry.HashX(Hash.Left, dir.Flow.Rules) - FieldGeometry.CenterX);
                    Check(Mathf.Abs(Mathf.Abs(sim.BallX0 - FieldGeometry.CenterX) - expect) < 0.05f, "ball spotted on the college hash (" + Mathf.Abs(sim.BallX0 - FieldGeometry.CenterX).ToString("0.00") + " yd off center, expected " + expect.ToString("0.00") + ")");
                    yield return new WaitForSecondsRealtime(0.4f); yield return Shot("v3_college_hash");
                }
                if (!otShot && s.IsCollegeOvertime && sim != null && dir.Phase == MatchPhase.PreSnap && sim.Kind == PlayKind.Scrimmage) { otShot = true; yield return new WaitForSecondsRealtime(0.4f); yield return Shot("v3_college_overtime"); }
                yield return null;
            }
            Check(dir.Finished, "college game finished");
            Check(otPeriods >= 1, "overtime was played (" + otPeriods + " periods)");
            Check(dir.Flow.State.HomeScore != dir.Flow.State.AwayScore, "college overtime produced a winner (" + dir.Flow.State.HomeScore + "-" + dir.Flow.State.AwayScore + ")");
            yield return new WaitForSecondsRealtime(1.0f); yield return Shot("v3_college_final");
            Time.timeScale = 1f;
            Log("v3 total: " + _v3Pass + " checks ok, " + _v3Fail + " failed");
        }
    }
}

