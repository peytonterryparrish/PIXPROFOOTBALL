using System;
using System.Collections;
using System.IO;
using System.Text;
using PixFootball.Gameplay;
using PixFootball.League;
using UnityEngine;

namespace PixFootball.UI.Franchise
{
    /// <summary>
    /// v0.8 autotest segment <c>-pixAutotestCareer [-pixAutotestOut dir] [-pixAutotestQuarter 1] [-pixAutotestSpeed 2] [-pixAutotestCareerTeam n]</c>: the whole career path in the built player.
    /// New career (random team, saves go to a private folder under the output directory, never to the player's slots) -> franchise hub -> roster and depth chart -> start the season (week 1) ->
    /// PLAY the user's game through the real match scene (autopilot, short quarters) -> the result posts to <see cref="CareerSession.CompleteMatch"/> -> the menu reopens the hub with the box
    /// score -> sim the rest of the week -> save -> load -> compare -> sim to the draft -> draft board screenshot -> auto draft. Screenshots: career_*.png in the output directory.
    /// </summary>
    public sealed class CareerAutoTestRunner
    {
        readonly AutoTest _t;
        int _fails;
        public CareerAutoTestRunner(AutoTest t) { _t = t; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() { AutoTest.CareerPart = t => new CareerAutoTestRunner(t).Run(); }

        void Log(string s) { _t.LogLine("[career] " + s); }
        void Check(bool ok, string what) { if (ok) Log("ok: " + what); else { _fails++; Debug.LogError("[AutoTest] FAIL: " + what); } }

        MainMenuController Menu { get { return UnityEngine.Object.FindFirstObjectByType<MainMenuController>(); } }

        IEnumerator WaitForFranchise(float timeout, string what)
        {
            float w = 0f;
            while ((Menu == null || Menu.AutotestFranchise == null) && w < timeout) { w += Time.unscaledDeltaTime; yield return null; }
            Log(what + ": franchise shell " + (Menu != null && Menu.AutotestFranchise != null ? "open after " + w.ToString("0.0") + " s" : "NOT open"));
        }

        static string Snapshot(CareerSave s)
        {
            var t = s.UserTeam; long sum = 0; foreach (var p in t.Roster) sum += p.Id * 31L + p.Overall;
            int abil = 0; foreach (var p in t.Roster) abil += p.Abilities != null ? p.Abilities.Count : 0;
            return s.Year + "|" + s.Phase + "|wk" + (s.Season != null ? s.Season.Week : 0) + "|" + (s.Season != null ? s.Season.WinsOf(t.Id) + "-" + s.Season.LossesOf(t.Id) : "-") + "|roster" + t.Roster.Count + "|sum" + sum + "|abilities" + abil
                + "|boxes" + s.UserBoxes.Count + "|news" + s.News.Count + "|tx" + s.Transactions.Count;
        }

        public IEnumerator Run()
        {
            Log("career autotest starting, scene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
            float w = 0f;
            while ((Menu == null || Time.timeSinceLevelLoad < 3f) && w < 90f) { w += Time.unscaledDeltaTime; yield return null; }
            if (Menu == null) { Check(false, "main menu did not load"); yield break; }

            // ---- 1. new career, random team, private save folder
            CareerSession.DirOverride = Path.Combine(_t.OutputDirectory, "career_saves");
            if (Directory.Exists(CareerSession.DirOverride)) Directory.Delete(CareerSession.DirOverride, true);
            int quarter = 1; string qv = AutoTest.ArgOf("-pixAutotestQuarter"); int qi; if (qv != null && int.TryParse(qv, out qi) && qi > 0) quarter = qi;
            var rnd = new System.Random(Environment.TickCount);
            int team = rnd.Next(32); string tv = AutoTest.ArgOf("-pixAutotestCareerTeam"); int ti; if (tv != null && int.TryParse(tv, out ti)) team = ti;
            int seed = rnd.Next(1, 1000000);
            var prefs = new CareerPrefs { Difficulty = (int)Difficulty.Pro, QuarterMinutes = quarter };
            var save = CareerSession.StartNew(team, seed, prefs, 1, "Autotest Career");
            Check(save != null && save.UserTeam != null, "career created");
            Log("user team #" + team + " " + save.UserTeam.FullName + ", seed " + seed + ", league " + save.Pro.Teams.Count + " teams, " + quarter + "-minute quarters, phase " + save.Phase);
            Check(save.Pro.Teams.Count == 32, "32 teams");
            var ctl = new FranchiseController(save, prefs, 1);
            Menu.AutotestOpenFranchise(ctl);
            yield return new WaitForSecondsRealtime(1.0f);

            // ---- 2. preseason -> week 1
            Check(save.Phase == CareerPhase.Preseason, "starts in the preseason");
            ctl.AutoResolve();
            var step = ctl.Advance();
            Check(step.Ok && save.Phase == CareerPhase.RegularSeason, "advance to the regular season (" + save.Phase + ")");
            int guard = 0;
            while (!ctl.UserHasGame && save.Phase == CareerPhase.RegularSeason && guard++ < 5) { Log("bye in week " + ctl.Week + ", simming it"); ctl.Advance(); }
            var game = CareerFlow.UserGame(save);
            Check(game != null && !game.Played && ctl.UserHasGame, "the user has a game to play in week " + ctl.Week);
            var ui = Menu.AutotestFranchise;
            ui.ShowTab(0);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return _t.Capture("career_hub");
            ui.ShowTab(FranchiseNav.Team, FranchiseNav.Roster);
            yield return new WaitForSecondsRealtime(1.5f);
            yield return _t.Capture("career_roster");
            ui.ShowTab(FranchiseNav.Team, FranchiseNav.Depth);
            yield return new WaitForSecondsRealtime(1.2f);
            yield return _t.Capture("career_depth");
            ui.ShowTab(0);
            yield return new WaitForSecondsRealtime(0.5f);

            // ---- 3. play the game in the real match scene (autopilot)
            string before = Snapshot(save);
            int gameId = game.Id;
            Log("PLAY week " + ctl.Week + ": " + (game.HomeId == save.UserTeamId ? "home vs " + save.Pro.GetTeam(game.AwayId).FullName : "at " + save.Pro.GetTeam(game.HomeId).FullName));
            MatchDirector.ForceAutopilot = true;
            var previous = MatchDirector.Instance;
            string err;
            bool launched = ctl.PlayNext(out err);
            Check(launched, "PlayNext launched the match (" + err + ")");
            if (!launched) yield break;
            Check(CareerSession.Pending, "the session has a pending career game");
            w = 0f;
            while ((MatchDirector.Instance == null || MatchDirector.Instance == previous || MatchDirector.Instance.Flow == null) && w < 120f) { w += Time.unscaledDeltaTime; yield return null; }
            var dir = MatchDirector.Instance;
            if (dir == null || dir.Flow == null) { Check(false, "the match scene did not start"); yield break; }
            Log("match started after " + w.ToString("0.0") + " s: " + dir.AwayInfo.Name + " at " + dir.HomeInfo.Name + ", autopilot plays " + dir.Config.UserTeam);
            float speed = 2f; string sv = AutoTest.ArgOf("-pixAutotestSpeed"); float sf; if (sv != null && float.TryParse(sv, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sf)) speed = sf;
            Time.timeScale = speed;
            float start = Time.unscaledTime, lastProgress = start; int lastSnaps = -1; bool callShot = false, midShot = false; int skip = 0;
            while (!dir.Finished && Time.unscaledTime - start < 60f * 12f)
            {
                if (dir.Flow.Snaps != lastSnaps) { lastSnaps = dir.Flow.Snaps; lastProgress = Time.unscaledTime; }
                if (Time.unscaledTime - lastProgress > 120f) { Check(false, "the match stalled for 120 s in phase " + dir.Phase); break; }
                if (skip > 0) skip--; else if (dir.Phase == MatchPhase.Live || dir.Phase == MatchPhase.PostPlay) _t.AddFrameSample(Time.unscaledDeltaTime);
                if (!callShot && dir.Phase == MatchPhase.PlayCall && dir.Flow.Snaps >= 2) { callShot = true; yield return new WaitForSecondsRealtime(0.5f); yield return _t.Capture("career_playcall"); skip = 6; }
                if (!midShot && dir.Phase == MatchPhase.Live && dir.Flow.Snaps >= 8) { midShot = true; yield return new WaitForSecondsRealtime(1.2f); yield return _t.Capture("career_match_live"); skip = 6; }
                yield return null;
            }
            Time.timeScale = 1f;
            Check(dir.Finished, "the match finished (Q" + dir.Flow.State.Quarter + ", " + dir.Flow.Snaps + " snaps)");
            Check(callShot, "a play-call screen was captured");
            yield return new WaitForSecondsRealtime(1.0f);
            Check(!CareerSession.Pending, "the result was posted (no pending game); " + CareerSession.LastMessage);
            Log("final: " + dir.AwayInfo.Name + " " + dir.Flow.State.AwayScore + ", " + dir.HomeInfo.Name + " " + dir.Flow.State.HomeScore);
            var g2 = CareerFlow.FindGame(save, gameId);
            Check(g2 != null && g2.Played, "the scheduled game is marked played in the career");
            Check(save.UserBoxes.Count == 1, "the box score is stored in the save (" + save.UserBoxes.Count + ")");
            if (save.UserBoxes.Count > 0) { var r = save.UserBoxes[0].Result; Log("box: " + r.AwayScore + "-" + r.HomeScore + ", player lines " + r.PlayerLines.Count + ", plays " + r.TotalPlays + ", away yards " + r.Away.TotalYards + ", home yards " + r.Home.TotalYards); Check(r.PlayerLines.Count > 10, "the box score carries player lines"); }

            // ---- 4. back to the menu: the franchise hub reopens with the box score
            GameSession.QuitToMenu();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return WaitForFranchise(120f, "after the game");
            ui = Menu != null ? Menu.AutotestFranchise : null;
            Check(ui != null, "the menu reopened the franchise hub after the game");
            if (ui == null) yield break;
            yield return new WaitForSecondsRealtime(2.2f);
            var bsv = UnityEngine.Object.FindFirstObjectByType<BoxScoreView>();
            Check(bsv != null, "the box score of the played game is on screen");
            yield return _t.Capture("career_boxscore");
            if (bsv != null) bsv.Close();
            yield return new WaitForSecondsRealtime(0.8f);
            ctl = ui.Ctl;
            var g3 = CareerFlow.UserGame(CareerSession.Save);
            Check(g3 != null && g3.Played && ctl.BoxOf(g3) != null, "the controller finds the box score of the played game");
            yield return _t.Capture("career_hub_after_game");
            Log("record after the game: " + CareerSession.Save.Season.WinsOf(CareerSession.Save.UserTeamId) + "-" + CareerSession.Save.Season.LossesOf(CareerSession.Save.UserTeamId));

            // ---- 5. sim the rest of week 1, save, load, compare
            int weekBefore = ctl.Week;
            var adv = ctl.Advance();
            Check(adv.Ok && ctl.Week == weekBefore + 1, "the rest of week " + weekBefore + " simmed, now week " + ctl.Week + " (" + ctl.Message + ")");
            Check(ctl.Autosaves > 0, "autosave ran (" + ctl.Autosaves + ")");
            string sBefore = Snapshot(CareerSession.Save);
            Check(CareerSession.SaveNow(), "explicit save");
            string file = Path.Combine(CareerSession.Dir, "slot1.json");
            Log("save file " + (File.Exists(file) ? new FileInfo(file).Length / 1024 + " KB" : "MISSING"));
            string lerr;
            Check(CareerSession.Load(1, out lerr), "load the slot (" + lerr + ")");
            string sAfter = Snapshot(CareerSession.Save);
            Log("before: " + sBefore); Log("after : " + sAfter);
            Check(sBefore == sAfter, "the loaded career equals the saved one");
            var ctl2 = new FranchiseController(CareerSession.Save, CareerSession.Prefs, 1);
            Check(ctl2.BoxCount >= 1, "the box score survived the reload (" + ctl2.BoxCount + ")");
            var vr = CareerValidator.Check(CareerSession.Save, false);
            Check(vr == null || vr.Count == 0, "validator after load" + (vr != null && vr.Count > 0 ? ": " + vr[0] : ""));

            // ---- 6. to the draft
            Menu.AutotestOpenFranchise(ctl2);
            yield return new WaitForSecondsRealtime(0.5f);
            var sv2 = CareerSession.Save;
            int steps = ctl2.AdvanceUntil(CareerPhase.Scouting);
            Log("simmed " + steps + " steps to " + sv2.Phase + " " + sv2.Year + "; user record " + sv2.Season.WinsOf(sv2.UserTeamId) + "-" + sv2.Season.LossesOf(sv2.UserTeamId) + "; " + ctl2.Message);
            Check(sv2.Phase == CareerPhase.Scouting, "reached the scouting phase");
            var board = CareerDraft.Board(sv2);
            Check(board != null && board.Count >= 200, "draft class has " + (board != null ? board.Count : 0) + " prospects");
            var ui2 = Menu.AutotestFranchise;
            ui2.ShowTab(0);
            ui2.ShowOverlay(new DraftPage());
            yield return new WaitForSecondsRealtime(1.8f);
            yield return _t.Capture("career_draft");
            ui2.CloseOverlay(false);
            ctl2.AutoDraft();
            Log("after the auto draft: phase " + sv2.Phase + ", unsigned rights " + CareerDraft.UnsignedRights(sv2, sv2.UserTeamId).Count);
            Check(sv2.Phase == CareerPhase.RookieSigning || sv2.Phase == CareerPhase.FreeAgency, "the draft finished (" + sv2.Phase + ")");
            var vr2 = CareerValidator.Check(sv2, false);
            Log("validator problems: " + (vr2 == null ? 0 : vr2.Count));
            Log("career autotest finished, " + _fails + " failed checks");
        }
    }
}
