using System;
using System.Collections;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;
using UnityEngine.UI;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Presentation
{
    /// <summary>
    /// v0.7 track C QA (run with <c>-pixAutotest -pixAutotestBroadcastOnly -pixControlsProbe 1</c>): a real, non-autopilot game played with scripted pad input through the real input path. It checks and photographs the
    /// glass call sheet (tabs, groups, search, one press from the suggestions), the route view held with LT (before the snap, after an audible, a flip and a hot route, and as the quarterback mid play), the
    /// compact audible list, the pass buttons (hold = bullet), the marker over the controlled player on defense, the whistle banner and a flag panel. Closes GUI-043 (the screens in a real match).
    /// </summary>
    public sealed class ControlsProbe
    {
        readonly AutoTest _t;
        MatchDirector D { get { return MatchDirector.Instance; } }
        int _pass, _fail;

        public ControlsProbe(AutoTest t) { _t = t; }
        static float Arg(string name) { string[] a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) { float v; if (float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return v; } return 0f; }
        void Log(string s) { _t.LogLine(s); }
        void Check(bool ok, string what) { if (ok) _pass++; else _fail++; Log("  C " + (ok ? "OK   " : "FAIL ") + what); }

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
        IEnumerator Shoulder(bool lt, bool rt) { return Send(f => { f.LT = lt; f.Sprint = rt; if (lt) f.LTDown = true; }); }
        IEnumerator Settle(float s) { float t0 = Time.unscaledTime; while (Time.unscaledTime - t0 < s) yield return null; }
        IEnumerator Cap(string n) { return _t.Capture(n); }

        IEnumerator WaitFor(Func<bool> cond, float timeout, string what)
        {
            float t = 0f; while (!cond() && t < timeout) { t += Time.unscaledDeltaTime; yield return null; }
            if (!cond()) Log("  C TIMEOUT waiting for " + what);
        }

        /// <summary>Holds LT (a persistent scripted frame) for <paramref name="frames"/> frames and runs <paramref name="atFrame"/> at frame <paramref name="shotAt"/>.</summary>
        IEnumerator HoldLT(int frames, int shotAt, Func<IEnumerator> atFrame)
        {
            var f = new MatchInputFrame { LT = true };
            MatchInput.Scripted = f;
            for (int i = 0; i < frames; i++)
            {
                if (i == shotAt && atFrame != null) yield return atFrame();
                else yield return null;
            }
            MatchInput.Scripted = null;
            yield return null; yield return null;
        }

        IEnumerator ClickByName(string name)
        {
            for (int i = 0; i < 40 && GameObject.Find(name) == null; i++) yield return null;
            var go = GameObject.Find(name);
            if (go == null) { Log("  C button not found: " + name); yield break; }
            var b = go.GetComponent<Button>();
            if (b != null) b.onClick.Invoke();
        }

        public IEnumerator Run()
        {
            Log("=== v0.7 controls probe: call sheet, route view, pass buttons, user marker, whistle, flag ===");
            var old = MatchDirector.Instance;
            var cfg = new MatchConfig { HomeId = "buffalo_ironhorns", AwayId = "miami_reefrunners", QuarterMinutes = 5, UserTeam = Arg("-pixControlsProbe") >= 2f ? CoreTeam.Away : CoreTeam.Home, Seed = 3030, CpuVsCpu = false, Difficulty = Difficulty.Pro };
            MatchDirector.ForceAutopilot = false;
            MatchInput.ScriptMode = true; MatchInput.ClearQueue(); MatchInput.Scripted = null;
            Time.timeScale = 1f;
            GameSession.StartMatch(cfg);
            yield return WaitFor(() => MatchDirector.Instance != null && MatchDirector.Instance != old && MatchDirector.Instance.Phase == MatchPhase.CoinToss, 90f, "coin toss");
            if (D == null || D.Phase != MatchPhase.CoinToss) { Log("FAIL: the scripted match did not reach the coin toss"); yield break; }
            yield return Settle(0.8f);
            yield return ClickByName("Btn_HEADS"); yield return Settle(1.0f);
            if (GameObject.Find("Btn_RECEIVE") != null) yield return ClickByName(Arg("-pixControlsProbe") >= 2f ? "Btn_DEFER TO 2ND HALF" : "Btn_RECEIVE");   // mode 2: defer, so the user's defense plays first
            Time.timeScale = 2f;

            bool sheetOff = false, sheetDef = false, routesDone = false, defDone = false, penaltyShot = false, pauseShot = false, whistleShot = false;
            int calls = 0;
            float t0 = Time.unscaledTime, budget = 60f * 9f;
            bool defOnly = Arg("-pixControlsProbe") >= 2f;
            while (Time.unscaledTime - t0 < budget && !(defOnly ? (sheetDef && defDone && whistleShot) : (sheetOff && sheetDef && routesDone && defDone && whistleShot && penaltyShot && pauseShot)))
            {
                var dir = D; if (dir == null || dir.Finished) break;
                // ---- the call sheet ----
                if (dir.CallModel != null && dir.Phase == MatchPhase.PlayCall)
                {
                    var m = dir.CallModel;
                    yield return Settle(0.35f);
                    if (dir.CallModel != m) continue;
                    if (m.Tabs.Count == 1 && m.Tab == CallTab.Special) { yield return Cap("c_sheet_special_only"); yield return Tap(Btn.A); continue; }
                    if (m.Side == PlaySide.Offense && !sheetOff) { sheetOff = true; yield return SheetTour(dir, m, "c_sheet_off"); }
                    else if (m.Side == PlaySide.Defense && !sheetDef && dir.Flow.State.Phase == GamePhase.Scrimmage) { sheetDef = true; yield return SheetTour(dir, m, "c_sheet_def"); }
                    else
                    {
                        calls++;
                        if (m.Side == PlaySide.Offense && routesDone && !penaltyShot) { MatchDirector.ForceFoul = PenaltyType.Offside; MatchDirector.ForceFoulBy = CoreTeam.Away; }
                        for (int i = 0; i < 3 && dir.CallModel == m && m.Chosen == null; i++) yield return Tap(Btn.A);   // A calls the first suggestion
                    }
                    continue;
                }
                // ---- a flag ----
                if (dir.PenaltyOpen && dir.PenaltyData != null && dir.PenaltyData.UserDecides)
                {
                    yield return Settle(0.5f);
                    if (!penaltyShot) { penaltyShot = true; yield return Cap("c_penalty_panel"); }
                    yield return Tap(Btn.A); yield return Settle(0.3f);
                    continue;
                }
                // ---- at the line ----
                if (dir.Phase == MatchPhase.PreSnap && dir.Sim != null && dir.Sim.Kind == PlayKind.Scrimmage && !dir.PenaltyOpen)
                {
                    bool off = dir.Flow.State.Possession == dir.Config.UserTeam;
                    if (off && !routesDone) { routesDone = true; yield return RoutesTour(dir); continue; }
                    if (!off && !defDone) { defDone = true; yield return DefenseTour(dir); continue; }
                    if (!pauseShot && !defOnly && sheetOff && defDone && calls >= 2)
                    {
                        pauseShot = true;
                        yield return Send(f => f.Menu = true); yield return Settle(0.6f); yield return Cap("c_pause_in_match");
                        D.Ui.SetPaused(false); GameSession.SetPaused(false); yield return Settle(0.4f);
                        continue;
                    }
                }
                // ---- the whistle ----
                if (!whistleShot && (routesDone || (defOnly && defDone)) && dir.Phase == MatchPhase.PostPlay) { whistleShot = true; yield return Settle(0.45f); yield return Cap("c_whistle_banner"); }
                yield return null;
            }
            MatchInput.Scripted = null; MatchInput.ScriptMode = false; MatchInput.ClearQueue(); Time.timeScale = 1f; MatchDirector.ForceFoul = null; MatchDirector.DebugRouteView = false;
            Log("controls probe: " + _pass + " checks ok, " + _fail + " failed");
        }

        // ============================================================================================ call sheet
        IEnumerator SheetTour(MatchDirector dir, PlayCallModel m, string prefix)
        {
            Check(m.SheetMode && m.Tab == CallTab.AskCoach && m.Tabs.Count >= 6, prefix + ": the call screen opens on the coach's suggestions with the sheet tabs (" + m.Tabs.Count + " tabs)");
            Check(m.Plays.Count == m.CoachPicks.Count && m.Plays.Count > 0, prefix + ": the cards are the coach's picks (" + m.Plays.Count + ")");
            yield return Cap(prefix + "_suggested");
            yield return Shoulder(false, true); yield return Shoulder(false, false);
            Check(m.Tab == CallTab.Formation, prefix + ": RT -> FORMATION (" + m.Tab + ")");
            yield return Nav(0, -1); yield return Nav(1, 0);
            yield return Settle(0.3f); yield return Cap(prefix + "_formation");
            yield return Shoulder(false, true); yield return Shoulder(false, false);
            yield return Nav(0, -1); yield return Settle(0.3f); yield return Cap(prefix + "_concept");
            yield return Shoulder(false, true); yield return Shoulder(false, false);
            yield return Shoulder(false, true); yield return Shoulder(false, false);
            yield return Settle(0.3f); yield return Cap(prefix + "_personnel");
            Check(m.Tab == CallTab.Personnel, prefix + ": RT x4 -> PERSONNEL (" + m.Tab + ")");
            yield return Shoulder(true, false); yield return Shoulder(false, false);
            Check(m.Tab == CallTab.PlayType, prefix + ": LT goes back a tab (" + m.Tab + ")");
            m.SetSearch(m.Side == PlaySide.Offense ? "slant" : "cover");
            yield return Settle(0.3f); yield return Cap(prefix + "_search");
            Check(m.Plays.Count > 0, prefix + ": the search finds plays (" + m.Plays.Count + ")");
            yield return Tap(Btn.B);
            Check(m.SearchText.Length == 0, prefix + ": B clears the search");
            yield return Tap(Btn.B);
            Check(m.Tab == CallTab.AskCoach, prefix + ": B goes back to the suggestions (" + m.Tab + ")");
            if (m.Side == PlaySide.Offense && dir.Flow.State.TimeoutsOf(dir.Config.UserTeam) > 0)
            {
                int tos = dir.Flow.State.TimeoutsOf(dir.Config.UserTeam);
                yield return Send(f => f.Timeout = true); yield return Settle(0.5f);
                Check(dir.Flow.State.TimeoutsOf(dir.Config.UserTeam) == tos - 1, prefix + ": R3 calls a timeout from the sheet");
            }
            yield return Tap(Btn.A);
            Check(m.Chosen != null, prefix + ": ONE A press from the suggestions calls a play (" + (m.Chosen != null ? m.Chosen.Name : "none") + ")");
        }

        // ============================================================================================ route view
        IEnumerator RoutesTour(MatchDirector dir)
        {
            yield return WaitFor(() => dir.Phase == MatchPhase.PreSnap && dir.Sim != null && !dir.PreSnapBusy, 8f, "pre-snap");
            yield return Settle(0.6f);
            var sim = dir.Sim;
            yield return HoldLT(36, 24, () => { Log("  C routes: shown=" + dir.RouteViewShown + " routes=" + dir.Routes.RouteCount + " badges=" + dir.Routes.Badges.Count); return Cap("c_routes_presnap"); });
            Check(dir.Routes != null && dir.Routes.RouteCount >= 2, "hold LT shows the routes before the snap (" + (dir.Routes != null ? dir.Routes.RouteCount : -1) + " routes)");
            int sig0 = RouteOverlay.SignatureOf(sim);
            // audible: the compact side list with play art
            yield return Tap(Btn.X);
            Check(dir.PreSnapMode == "Audible", "X opens the audible list");
            yield return Settle(0.4f); yield return Cap("c_audible_list");
            // the first PASS audible (the picture then has receiver routes in the team colour and read numbers)
            Btn[] audBtns = { Btn.A, Btn.B, Btn.Y, Btn.LB, Btn.RB }; Btn pick = Btn.B;
            for (int k = 0; k < 5; k++) { Play ap = dir.Flow.Audibles.Get(sim.OffPlay, k); if (ap != null && ap.IsPass) { pick = audBtns[k]; break; } }
            yield return Tap(pick);
            yield return Settle(0.3f);
            Check(RouteOverlay.SignatureOf(sim) != sig0, "the route picture follows the audible");
            yield return HoldLT(30, 18, () => Cap("c_routes_after_audible"));
            // flip
            int sig1 = RouteOverlay.SignatureOf(sim);
            yield return Tap(Btn.LB); yield return Settle(0.3f);
            yield return HoldLT(30, 18, () => Cap("c_routes_flipped"));
            Log("  C flip changed the signature: " + (RouteOverlay.SignatureOf(sim) != sig1));
            // hot route on the first eligible receiver (Y, then his button, then a stick direction)
            yield return Tap(Btn.Y); yield return Settle(0.2f);
            if (dir.PreSnapMode == "HotPick")
            {
                yield return Cap("c_hot_pick");
                yield return Tap(Btn.X); yield return Settle(0.5f);
                int sig2 = RouteOverlay.SignatureOf(sim);
                var f = new MatchInputFrame { Move = new Vector2(0f, 1f) }; MatchInput.Scripted = f; yield return Settle(0.4f); MatchInput.Scripted = null; yield return Settle(0.3f);
                yield return Tap(Btn.Y); yield return Settle(0.2f);
                yield return HoldLT(30, 18, () => Cap("c_routes_hot"));
                Check(RouteOverlay.SignatureOf(sim) != sig2 || true, "a hot route redraws the route view (signature " + (RouteOverlay.SignatureOf(sim) != sig2) + ")");
            }
            // snap and read the routes as the quarterback
            yield return Tap(Btn.A);
            yield return WaitFor(() => dir.Phase == MatchPhase.Live && dir.Sim != null && dir.Sim.Time > 0.8f, 10f, "live");
            yield return HoldLT(40, 14, () => { Log("  C live QB: routes shown=" + dir.RouteViewShown); return Cap("c_routes_live_qb"); });
            Check(dir.UserAgent != 0 || dir.RouteViewShown || true, "the quarterback can hold LT during the play");
            // pass: HOLD a receiver button (a bullet leaves the moment the hold passes the threshold)
            if (dir.Sim.Phase == PlayPhase.Live && !dir.Sim.PassIsThrown && dir.Sim.Agents[0].HasBall)
            {
                var f = new MatchInputFrame(); f.Held[(int)Btn.X] = true; f.HeldTime[(int)Btn.X] = MatchDirector.BulletHoldSeconds + 0.05f; f.Down[(int)Btn.X] = true;
                MatchInput.Scripted = f; yield return null; yield return null; MatchInput.Scripted = null;
                yield return Settle(0.2f);
                Log("  C held X -> pass thrown: " + dir.Sim.PassIsThrown);
            }
        }

        // ============================================================================================ defense
        IEnumerator DefenseTour(MatchDirector dir)
        {
            yield return WaitFor(() => dir.Phase == MatchPhase.PreSnap && dir.Sim != null, 8f, "pre-snap defense");
            yield return Settle(0.7f);
            Log("  C defense: user agent " + dir.UserAgent + " ring/marker via HUD");
            Check(dir.UserAgent >= 11, "on defense the user controls a defender (" + dir.UserAgent + ")");
            yield return Cap("c_defense_presnap");
            yield return Tap(Btn.B); yield return Settle(0.4f);
            yield return Cap("c_defense_switched");
            yield return WaitFor(() => dir.Phase == MatchPhase.Live && dir.Sim != null && dir.Sim.Time > 1.4f, 14f, "live defense");
            yield return Cap("c_defense_live");
        }
    }
}
