using System;
using System.Collections;
using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;
using UnityEngine;
using CoreTeam = PixFootball.Core.Team;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.Gameplay
{
    public enum MatchPhase { Booting, CoinToss, PlayCall, PreSnap, Live, PostPlay, Break, Final }

    /// <summary>
    /// Runs a whole game in the stadium: coin toss, kickoffs, drives, play calling, scoring, tries, quarters and halftime, final score.
    /// The football rules live in <see cref="MatchFlow"/> (engine-free); this class presents them: 22 players, the ball, cameras, clock,
    /// banners and the user's controls (2K-style: quarterback pre-snap, then whoever has the ball; on defense the nearest defender).
    /// </summary>
    public sealed class MatchDirector : MonoBehaviour
    {
        public static MatchDirector Instance { get; private set; }
        /// <summary>Set by the autotest before the match scene loads: the director then plays the user side itself.</summary>
        public static bool ForceAutopilot;

        public Camera MainCamera;
        public LightingController Lighting;
        [Tooltip("A MonoBehaviour implementing IMatchPresentation (PixFootball.UI.MatchHud).")]
        public MonoBehaviour UiBehaviour;

        public MatchConfig Config { get; private set; }
        public MatchFlow Flow { get; private set; }
        public PlayStage Stage { get; private set; }
        public MatchCameraRig Cams { get; private set; }
        public FieldMarkers Markers { get; private set; }
        public MatchAudio Audio { get; private set; }
        public IMatchPresentation Ui { get; private set; }
        public MatchPhase Phase { get; private set; } = MatchPhase.Booting;
        public PlaySimulation Sim { get; private set; }
        public int PlayNumber { get; private set; }
        public TeamInfo HomeInfo { get; private set; }
        public TeamInfo AwayInfo { get; private set; }
        public int UserAgent { get; private set; } = -1;
        public bool Paused { get { return GameSession.Paused; } }
        /// <summary>When true the director plays the user's side itself (autotest / demo): picks plays, snaps, throws, runs, tackles.</summary>
        public bool Autopilot;
        public bool Finished { get; private set; }
        public string StatusText { get; private set; } = "";

        public event Action<PlaySimulation> PlayStarted;
        public event Action<PlayOutcome> PlayFinished;
        public event Action<PlayEvent> SimEvent;
        public event Action<string> Milestone;
        /// <summary>Called before the calls of every snap (the autotest uses it to stage a goal-line situation).</summary>
        public Action<MatchFlow> PrePlayHook;

        UniformSet _homeKit, _awayKit;
        TeamColors _homeCol;
        System.Random _autoRng = new System.Random(5);
        Rng _rng;
        PlayFrame _frame;
        bool _userIsOffense, _recall, _periodInterrupt, _quit;
        PeriodChange _pendingPeriod;
        Play _offCall, _defCall;
        int _defUser = -1;
        readonly int[] _iconSlot = new int[6];
        readonly bool[] _fired = new bool[6];
        float _runoff0;
        bool _fdLine;
        float _fade, _fadeTarget;
        readonly List<ReceiverIcon> _icons = new List<ReceiverIcon>();
        MatchInputFrame _input = new MatchInputFrame();
        float _autoT; int _autoThrowSlot; float _autoNextMove;
        int _lastQuarter = 1;
        bool _twoMinHome, _twoMinAway;

        // ============================================================================================ boot
        void Awake() { Instance = this; GameSession.SetPaused(false); }
        void OnDestroy() { if (Instance == this) Instance = null; GameSession.SetPaused(false); MatchInput.Scripted = null; }

        IEnumerator Start()
        {
            Ui = UiBehaviour as IMatchPresentation;
            if (MainCamera == null) MainCamera = Camera.main;
            Config = MatchData.Current.Clone();
            if (Config.CpuVsCpu || ForceAutopilot) Autopilot = true;
            var league = MatchData.League;
            LeagueTeam home = MatchData.TeamByKey(Config.HomeId), away = MatchData.TeamByKey(Config.AwayId);
            HomeInfo = TeamCatalog.ById(Config.HomeId); AwayInfo = TeamCatalog.ById(Config.AwayId);
            _homeKit = UniformSet.For(HomeInfo, true); _awayKit = UniformSet.For(AwayInfo, false);
            _rng = new Rng(Config.Seed != 0 ? Config.Seed : Environment.TickCount);

            if (Lighting != null) Lighting.Apply(PixSettings.Lighting);
            Stage = new GameObject("PlayStage").AddComponent<PlayStage>();
            Stage.transform.SetParent(transform, false);
            Stage.Init();
            Stage.Warm(8, 8, 9);
            Cams = MatchCameraRig.Create(transform, MainCamera);
            Markers = FieldMarkers.Create(transform);
            Audio = gameObject.AddComponent<MatchAudio>(); Audio.Init();
            if (Ui != null)
            {
                Ui.Build(this);
                Ui.ResumeRequested += () => SetPaused(false);
                Ui.QuitRequested += () => { _quit = true; GameSession.QuitToMenu(); };
                Ui.CameraCycleRequested += () => { Cams.Cycle(); PixSettings.DefaultCamera = Cams.Mode; };
                Ui.LightingToggleRequested += () => { if (Lighting != null) Lighting.Toggle(); };
            }
            if (Ui != null) Ui.SetScore(InitialScore(league));
            yield return null;
            yield return CoinToss();
            if (_quit) yield break;
            yield return GameLoop();
        }

        ScoreSnapshot InitialScore(ProLeague league)
        {
            var h = MatchData.TeamByKey(Config.HomeId); var a = MatchData.TeamByKey(Config.AwayId);
            return new ScoreSnapshot
            {
                HomeAbbr = string.IsNullOrEmpty(h.Abbreviation) ? h.Nickname : h.Abbreviation, AwayAbbr = string.IsNullOrEmpty(a.Abbreviation) ? a.Nickname : a.Abbreviation, HomeCrest = "crest_" + HomeInfo.Id, AwayCrest = "crest_" + AwayInfo.Id,
                HomeColor = HomeInfo.PrimaryUi, AwayColor = AwayInfo.PrimaryUi, Quarter = "1ST", Clock = Config.QuarterMinutes + ":00", DownDistance = "KICKOFF", Spot = "", PossessionHome = false
            };
        }

        string TeamName(CoreTeam t) { return t == CoreTeam.Home ? HomeInfo.Name : AwayInfo.Name; }
        TeamInfo InfoOf(CoreTeam t) { return t == CoreTeam.Home ? HomeInfo : AwayInfo; }

        void Note(string s) { StatusText = s; if (Milestone != null) Milestone(s); }

        // ============================================================================================ coin toss
        IEnumerator CoinToss()
        {
            Phase = MatchPhase.CoinToss;
            Note("coin toss");
            CoreTeam user = Config.UserTeam, cpu = user.Opponent();
            bool userCallsHeads = true, resultHeads = _rng.Chance(0.5f);
            bool userWon;
            CoreTeam receiver;
            if (Config.CpuVsCpu || Autopilot && Ui == null)
            {
                userWon = _rng.Chance(0.5f);
                receiver = userWon ? user : cpu;
            }
            else
            {
                bool called = false;
                Ui.ShowCoinTossCall(TeamName(user), TeamName(cpu), h => { userCallsHeads = h; called = true; });
                if (Autopilot) { yield return new WaitForSecondsRealtime(1.6f); userCallsHeads = _autoRng.Next(2) == 0; called = true; }
                while (!called) yield return null;
                userWon = resultHeads == userCallsHeads;
                bool userReceives = false, answered = false;
                CoreTeam winner = userWon ? user : cpu;
                Ui.ShowCoinTossResult(resultHeads, userWon, TeamName(winner), r => { userReceives = r; answered = true; });
                if (Autopilot) { yield return new WaitForSecondsRealtime(1.2f); }
                if (Autopilot && userWon) { userReceives = true; answered = true; }
                while (!answered) yield return null;
                bool winnerReceives = userWon ? userReceives : !_rng.Chance(0.25f);
                receiver = winnerReceives ? winner : winner.Opponent();
            }
            CoreTeam kicker = receiver.Opponent();
            if (Ui != null) Ui.HidePlayCall();
            Flow = new MatchFlow(Config, MatchData.TeamByKey(Config.HomeId), MatchData.TeamByKey(Config.AwayId), kicker, _rng.Fork());
            Note("toss: " + TeamName(receiver) + " receive");
            yield return null;
        }

        // ============================================================================================ game loop
        IEnumerator GameLoop()
        {
            var state = Flow.State;
            while (!state.IsFinal && !_quit)
            {
                CoreTeam offTeam = state.Possession;
                if (PrePlayHook != null) PrePlayHook(Flow);
                offTeam = state.Possession;
                _userIsOffense = !Config.CpuVsCpu && offTeam == Config.UserTeam;
                _frame = PlayFrame.For(offTeam, state.Quarter);
                if (state.Quarter != _lastQuarter) _lastQuarter = state.Quarter;

                // ---- calls ----
                yield return Calls();
                if (_quit) yield break;

                // ---- build the play and wait for the snap ----
                while (true)
                {
                    _recall = false;
                    BeginPlay();
                    yield return PreSnap();
                    if (_quit) yield break;
                    if (!_recall) break;
                    yield return Calls();
                }
                if (_periodInterrupt)
                {
                    _periodInterrupt = false;
                    yield return PeriodBreak(_pendingPeriod, false);
                    continue;
                }
                yield return LivePlay();
                if (_quit) yield break;
            }
            yield return FinalScreen();
        }

        // ---------------------------------------------------------------------------------------- calls
        IEnumerator Calls()
        {
            Phase = MatchPhase.PlayCall;
            var state = Flow.State;
            Play cpuOff, cpuDef;
            Flow.CpuCalls(out cpuOff, out cpuDef);
            _offCall = cpuOff; _defCall = cpuDef;
            if (Config.CpuVsCpu) yield break;
            bool userOff = state.Possession == Config.UserTeam;
            List<PlayOption> opts;
            if (userOff)
            {
                opts = Flow.OffenseOptions();
                if (opts.Count == 0) yield break;
            }
            else
            {
                if (state.Phase != GamePhase.Scrimmage || cpuOff.Category == PlayCategory.Punt || cpuOff.Category == PlayCategory.FieldGoal || cpuOff.Category == PlayCategory.Kneel)
                {
                    _defCall = Flow.DefenseAnswer(cpuOff);
                    yield break;
                }
                opts = Flow.DefenseOptions();
                if (opts.Count == 0) yield break;
            }
            // ---- show the menu over a preview of the field ----
            PreviewField();
            if (_fade > 0.01f) StartCoroutine(Fade(0f, 0.25f));
            var data = new PlayCallData { Offense = userOff, TeamName = TeamName(Config.UserTeam), Seconds = 25f };
            data.Title = userOff ? (state.Phase == GamePhase.Kickoff ? "KICKOFF" : (state.Phase == GamePhase.PointAfter ? "AFTER THE TOUCHDOWN" : "OFFENSE")) : "DEFENSE";
            data.Subtitle = SituationText();
            Btn[] order = { Btn.A, Btn.B, Btn.X, Btn.Y, Btn.LB, Btn.RB };
            for (int i = 0; i < opts.Count && i < 6; i++) data.Items.Add(new PlayCallItem { Label = opts[i].Label, Tag = opts[i].Tag, Button = order[i] });
            int picked = -1;
            Ui.ShowPlayCall(data, i => picked = i);
            float t = 0f;
            while (picked < 0 && !_quit)
            {
                if (Autopilot) { t += Time.deltaTime; if (t > 1.2f) picked = _autoRng.Next(Mathf.Min(opts.Count, data.Items.Count)); }
                else if (!Paused)
                {
                    t += Time.unscaledDeltaTime;
                    var inp = MatchInput.Instance.Poll(Time.unscaledDeltaTime);
                    if (inp.View) { Cams.Cycle(); PixSettings.DefaultCamera = Cams.Mode; }
                    if (inp.Menu) SetPaused(true);
                    for (int k = 0; k < data.Items.Count; k++) if (inp.Down[(int)data.Items[k].Button] && picked < 0) { picked = k; }
                }
                if (t > data.Seconds && picked < 0) picked = 0;
                yield return null;
            }
            Ui.HidePlayCall();
            if (picked < 0) yield break;
            var choice = opts[Mathf.Clamp(picked, 0, opts.Count - 1)];
            if (userOff)
            {
                _offCall = choice.Play;
                _defCall = Flow.DefenseAnswer(_offCall);
            }
            else _defCall = choice.Play;
            yield return null;
        }

        string SituationText()
        {
            var s = Flow.State;
            if (s.Phase == GamePhase.Kickoff) return "Q" + s.Quarter + "  " + s.ClockDisplay;
            if (s.Phase == GamePhase.PointAfter) return s.RequiresTwoPoint ? "TWO-POINT TRY REQUIRED" : "EXTRA POINT OR TWO-POINT TRY";
            return Ordinal(s.Down) + " & " + (s.IsGoalToGo ? "GOAL" : s.Distance.ToString()) + "   " + SpotText() + "   Q" + s.Quarter + " " + s.ClockDisplay;
        }

        static string Ordinal(int d) { return d == 1 ? "1ST" : d == 2 ? "2ND" : d == 3 ? "3RD" : "4TH"; }

        string SpotText()
        {
            var s = Flow.State;
            int spot = s.BallSpot;
            string own = Flow.Abbr(s.Possession), opp = Flow.Abbr(s.Possession.Opponent());
            if (spot == 50) return "50";
            return spot < 50 ? own + " " + spot : opp + " " + (100 - spot);
        }

        // ---------------------------------------------------------------------------------------- field preview while calling
        void PreviewField()
        {
            var state = Flow.State;
            // show the spot with a throwaway sim of the called formation (so the field is not empty behind the menu)
            BeginPlay(_offCall, _defCall);
        }

        // ---------------------------------------------------------------------------------------- build the play
        void BeginPlay() { BeginPlay(_offCall, _defCall); }

        void BeginPlay(Play off, Play def)
        {
            var state = Flow.State;
            Sim = Flow.CreateSim(off, def, false);
            PlayNumber++;
            _frame = PlayFrame.For(state.Possession, state.Quarter);
            var offKit = state.Possession == CoreTeam.Home ? _homeKit : _awayKit;
            var defKit = state.Possession == CoreTeam.Home ? _awayKit : _homeKit;
            Stage.Begin(Sim, _frame, offKit, defKit, PlayNumber);
            Cams.SetFrame(_frame, true);
            bool scrim = state.Phase == GamePhase.Scrimmage;
            Markers.SetLines(_frame, state.BallSpot, state.BallSpot + state.Distance, scrim, scrim && !state.IsGoalToGo);
            _userIsOffense = !Config.CpuVsCpu && state.Possession == Config.UserTeam;
            // the defender the user controls pre-snap
            _defUser = ChooseDefaultDefender();
            UserAgent = SelectUser(MatchInputIdle, true);
            ApplyCameraTarget(true);
            Cams.Cut();
            if (Ui != null) Ui.SetScore(MakeScore(false));
        }

        MatchInputFrame MatchInputIdle { get { return _idle; } }
        readonly MatchInputFrame _idle = new MatchInputFrame();

        int ChooseDefaultDefender()
        {
            if (Sim == null || Config.CpuVsCpu) return -1;
            SimSide side = UserSide();
            if (side == SimSide.Offense) return 0;
            int best = -1; float bd = float.MaxValue;
            Vec2 spot = new Vec2(Sim.BallX0, Sim.LosY);
            for (int i = 11; i < 22; i++)
            {
                var a = Sim.Agents[i];
                float d = (a.Pos - spot).Length + (a.PlayPos == Position.MLB ? -6f : 0f);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        SimSide UserSide() { return Flow.State.Possession == Config.UserTeam ? SimSide.Offense : SimSide.Defense; }

        // ---------------------------------------------------------------------------------------- pre-snap
        IEnumerator PreSnap()
        {
            Phase = MatchPhase.PreSnap;
            var state = Flow.State;
            _fadeTarget = 0f;
            float t = 0f;
            _runoff0 = state.ClockRunning ? state.PendingRunoff : 0f;
            float cpuDelay = 2.6f + (float)_autoRng.NextDouble() * 1.6f;
            bool snap = false, cpuTimeoutChecked = false;
            if (_fade > 0.01f) StartCoroutine(Fade(0f, 0.3f));
            float playClock = Flow.Rules.PlayClockDead;
            Note("presnap Q" + state.Quarter + " " + SituationText());
            while (!snap && !_recall && !_quit)
            {
                float dt = Time.deltaTime;
                if (Paused) { PauseFrame(); yield return null; continue; }
                t += dt; playClock -= dt;
                MatchInputFrame inp = ReadInput();
                if (inp.Menu) { SetPaused(true); yield return null; continue; }
                if (inp.View) { Cams.Cycle(); PixSettings.DefaultCamera = Cams.Mode; }
                bool kickoff = Sim.Kind == PlayKind.Kickoff;
                if (_userIsOffense && !kickoff)
                {
                    if (inp.Down[(int)Btn.A]) snap = true;
                    if (inp.Down[(int)Btn.Y] && Ui != null && !Autopilot) { _recall = true; break; }
                    if (inp.DPadDown) TryUserTimeout();
                    SetHint(Sim.Kind == PlayKind.Scrimmage ? "A  SNAP      Y  CHANGE PLAY      D-PAD DOWN  TIMEOUT      VIEW  CAMERA" : "A  KICK      VIEW  CAMERA");
                }
                else if (_userIsOffense && kickoff)
                {
                    if (t > 1.6f || inp.Down[(int)Btn.A]) snap = true;
                    SetHint("A  KICK OFF");
                }
                else
                {
                    if (inp.DPadDown) TryUserTimeout();
                    if (inp.Down[(int)Btn.B]) _defUser = NearestUserAgentToBall(_defUser);
                    SetHint(kickoff ? "GET READY TO RETURN" : "B  SWITCH PLAYER      D-PAD DOWN  TIMEOUT      VIEW  CAMERA");
                    if (t > cpuDelay) snap = true;
                }
                if (Autopilot && t > 0.9f) snap = true;
                if (playClock <= 0f) snap = true;
                if (state.Phase == GamePhase.Scrimmage && t > 1.2f && !snap && !cpuTimeoutChecked)
                {
                    cpuTimeoutChecked = true; CoreTeam cto;
                    if (!Config.CpuVsCpu && Flow.TryCpuTimeout(out cto)) { Banner(TeamName(cto) + " TIMEOUT", UiColorsLite.Blue, 1.8f, ""); _runoff0 = 0f; }
                }
                UserAgent = SelectUser(inp, true);
                Sim.Tick(dt, default(ControlInput), -1);
                Stage.Present(dt);
                PresentFrame(dt, 0f, t);
                if (Ui != null) Ui.SetScore(MakeScore(false, playClock, t));
                yield return null;
            }
            if (_quit || _recall) yield break;

            // snap: burn the runoff; the period may end first
            var pc = Flow.PrepareSnap();
            if (pc.Changed)
            {
                _pendingPeriod = pc; _periodInterrupt = true;
                Stage.End(); Sim = null;
                yield break;
            }
            Phase = MatchPhase.Live;
            if (Sim.Kind == PlayKind.Kickoff) Audio.Whistle();
            Sim.Tick(0.0167f, new ControlInput { Snap = true }, -1);
            Note("snap " + Sim.OffPlay.Name + " vs " + Sim.DefPlay.Name);
            if (PlayStarted != null) PlayStarted(Sim);
            yield return null;
        }

        void TryUserTimeout()
        {
            var s = Flow.State;
            if (Autopilot) return;
            if (s.TimeoutsOf(Config.UserTeam) > 0 && s.Phase == GamePhase.Scrimmage && s.CallTimeout(Config.UserTeam))
            {
                _runoff0 = 0f;
                Banner(TeamName(Config.UserTeam) + " TIMEOUT", UiColorsLite.Blue, 1.8f, s.TimeoutsOf(Config.UserTeam) + " REMAINING");
            }
        }

        // ---------------------------------------------------------------------------------------- the live play
        IEnumerator LivePlay()
        {
            Phase = MatchPhase.Live;
            var sim = Sim;
            float t = 0f;
            _autoT = 0f; _autoThrowSlot = 0; _autoNextMove = 0.8f;
            Array.Clear(_fired, 0, 6);
            while (sim.Phase != PlayPhase.Dead && !_quit)
            {
                if (Paused) { PauseFrame(); yield return null; continue; }
                float dt = Mathf.Min(Time.deltaTime, 0.05f);
                t += dt;
                MatchInputFrame inp = ReadInput();
                if (inp.Menu) { SetPaused(true); yield return null; continue; }
                if (inp.View) { Cams.Cycle(); PixSettings.DefaultCamera = Cams.Mode; }
                int user; ControlInput ci = BuildControl(inp, dt, out user);
                sim.Tick(dt, ci, user);
                Stage.Present(dt);
                HandleEvents();
                PresentFrame(dt, t, 0f);
                if (Ui != null) Ui.SetScore(MakeScore(true));
                yield return null;
            }
            if (_quit) yield break;

            // ---- result ----
            Phase = MatchPhase.PostPlay;
            var outcome = Flow.Finish(sim);
            Note("result " + outcome.Text + " " + Flow.Summary());
            Markers.SetRing(null); Ui?.SetReceiverIcons(null, MainCamera);
            React(outcome);
            if (PlayFinished != null) PlayFinished(outcome);
            float wind = outcome.Headline == Headline.Touchdown ? 4.2f : (sim.Kind == PlayKind.Scrimmage && sim.Result != null && sim.Result.Type == PlayType.Penalty ? 1.6f : 2.9f);
            float w = 0f;
            while (w < wind && !_quit)
            {
                if (Paused) { PauseFrame(); yield return null; continue; }
                float dt = Mathf.Min(Time.deltaTime, 0.05f);
                w += dt;
                sim.Tick(dt, default(ControlInput), -1);
                Stage.Present(dt);
                HandleEvents();
                PresentFrame(dt, 0f, 0f);
                if (Ui != null) Ui.SetScore(MakeScore(false));
                yield return null;
            }
            // ---- period / game transitions ----
            if (outcome.PeriodChanged || outcome.GameOver)
            {
                var pc = new PeriodChange { Changed = true, From = outcome.QuarterBefore, To = outcome.QuarterAfter, Final = outcome.GameOver, Halftime = outcome.QuarterBefore == 2 && outcome.QuarterAfter == 3 };
                yield return PeriodBreak(pc, true);
            }
            else CheckTwoMinute();
            // fade out, clear, fade in is handled by the next BeginPlay (cut hidden by the banner / fade)
            yield return Fade(1f, 0.22f);
            Stage.End(); Sim = null;
            yield return null;
            _fadeTarget = 0f;
        }

        IEnumerator Fade(float to, float seconds)
        {
            float from = _fade; float t = 0f;
            while (t < seconds) { t += Time.unscaledDeltaTime; _fade = Mathf.Lerp(from, to, t / seconds); if (Ui != null) Ui.SetFade(_fade); yield return null; }
            _fade = to; if (Ui != null) Ui.SetFade(_fade);
        }

        void CheckTwoMinute()
        {
            var s = Flow.State;
            bool now = s.TwoMinuteWarningFirstHalf || s.TwoMinuteWarningSecondHalf;
            if (now && !(_twoMinHome))
            {
                _twoMinHome = true;
            }
        }

        IEnumerator PeriodBreak(PeriodChange pc, bool afterPlay)
        {
            Phase = MatchPhase.Break;
            var s = Flow.State;
            if (pc.Final || s.IsFinal) yield break;
            string text; string sub = Flow.Abbr(CoreTeam.Home) + " " + s.HomeScore + "  -  " + Flow.Abbr(CoreTeam.Away) + " " + s.AwayScore;
            if (pc.Halftime) text = "HALFTIME";
            else if (pc.From == 4) text = "OVERTIME";
            else text = "END OF " + Ordinal2(pc.From) + " QUARTER";
            Audio.Whistle();
            Banner(text, UiColorsLite.Blue, pc.Halftime ? 3.8f : 2.6f, sub);
            float w = 0f; float dur = pc.Halftime ? 3.8f : 2.6f;
            if (Sim != null) { }
            while (w < dur && !_quit) { if (!Paused) w += Time.deltaTime; if (Ui != null) Ui.SetScore(MakeScore(false)); yield return null; }
            if (pc.Halftime) { Note("halftime"); }
            Note("period change to Q" + s.Quarter);
            if (!afterPlay) { }
        }

        static string Ordinal2(int q) { return q == 1 ? "1ST" : q == 2 ? "2ND" : q == 3 ? "3RD" : "4TH"; }

        IEnumerator FinalScreen()
        {
            Phase = MatchPhase.Final;
            Finished = true;
            if (_fade > 0.01f) yield return Fade(0f, 0.3f);
            var s = Flow.State;
            Note("final " + Flow.Summary());
            Audio.Whistle(); Audio.Cheer(1f);
            var d = new FinalData
            {
                HomeName = HomeInfo.Name, AwayName = AwayInfo.Name, HomeAbbr = Flow.Abbr(CoreTeam.Home), AwayAbbr = Flow.Abbr(CoreTeam.Away),
                HomeCrest = "crest_" + HomeInfo.Id, AwayCrest = "crest_" + AwayInfo.Id, HomeColor = HomeInfo.PrimaryUi, AwayColor = AwayInfo.PrimaryUi,
                HomeScore = s.HomeScore, AwayScore = s.AwayScore, HomeStats = Flow.HomeStats, AwayStats = Flow.AwayStats,
                Tie = s.HomeScore == s.AwayScore
            };
            Array.Copy(Flow.HomeByQuarter, d.HomeByQuarter, 8); Array.Copy(Flow.AwayByQuarter, d.AwayByQuarter, 8);
            CoreTeam? w = s.Winner;
            d.UserWon = w.HasValue && w.Value == Config.UserTeam;
            d.Headline = d.Tie ? "FINAL - TIE GAME" : (Config.CpuVsCpu ? "FINAL" : (d.UserWon ? "VICTORY" : "DEFEAT"));
            bool go = false;
            if (Ui != null) Ui.ShowFinal(d, () => go = true);
            float t = 0f;
            while (!go && !_quit) { t += Time.unscaledDeltaTime; if (Autopilot && t > 3f) go = true; yield return null; }
            if (!_quit) { _quit = true; if (!Autopilot) GameSession.QuitToMenu(); }
        }

        // ============================================================================================ input and control
        MatchInputFrame ReadInput()
        {
            if (Autopilot) return AutoInput();
            _input = MatchInput.Instance.Poll(Time.unscaledDeltaTime);
            return _input;
        }

        void SetPaused(bool p)
        {
            if (GameSession.Paused == p) return;
            GameSession.SetPaused(p);
            if (Ui != null) Ui.SetPaused(p);
        }

        void PauseFrame()
        {
            if (Autopilot) return;
            var inp = MatchInput.Instance.Poll(Time.unscaledDeltaTime);
            if (inp.Menu && Ui != null && !Ui.PauseOpen) { }
            else if (inp.Menu) SetPaused(false);
        }

        void SetHint(string s) { if (Ui != null) Ui.SetHint(s); }
        void Banner(string text, Color c, float secs, string sub) { if (Ui != null) Ui.ShowBanner(text, c, secs, sub); }

        int NearestUserAgentToBall(int current)
        {
            if (Sim == null) return current;
            SimSide side = UserSide();
            Vec2 focus = Sim.Ball.Holder >= 0 ? Sim.Agents[Sim.Ball.Holder].Pos : Sim.Ball.Pos.XY;
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < 22; i++)
            {
                var a = Sim.Agents[i];
                if (a.Side != side || i == current || a.IsDown) continue;
                float d = (a.Pos - focus).Length;
                if (d < bd) { bd = d; best = i; }
            }
            return best >= 0 ? best : current;
        }

        /// <summary>The agent the user is "on" (marker, camera, controls); the simulation only treats him as human-controlled when the user is actually steering.</summary>
        int SelectUser(MatchInputFrame inp, bool preSnap)
        {
            if (Sim == null || Config.CpuVsCpu) return -1;
            SimSide side = UserSide();
            var ball = Sim.Ball;
            int holder = ball.Holder;
            bool scrim = Sim.Kind == PlayKind.Scrimmage || Sim.Kind == PlayKind.Kneel || Sim.Kind == PlayKind.Spike;
            if (preSnap || Sim.Phase == PlayPhase.PreSnap)
            {
                if (side == SimSide.Offense) return scrim ? 0 : -1;
                if (!scrim) { for (int i = 11; i < 22; i++) if (Sim.Agents[i].ReturnerRole) return i; return -1; }
                return _defUser;
            }
            if (holder >= 0 && Sim.Agents[holder].Side == side) return holder;
            if (scrim && side == SimSide.Offense && !Sim.PassIsThrown && Sim.Agents[0].HasBall) return 0;
            if (ball.State == BallState.Pass && Sim.BallSide == SimSide.Offense)
            {
                int tgt = ball.Intended;
                if (side == SimSide.Offense && tgt >= 0) return tgt;
                if (side == SimSide.Defense && tgt >= 0)
                {
                    // the defender in coverage of the target
                    int best = -1; float bd = float.MaxValue;
                    for (int i = 11; i < 22; i++) { float d = (Sim.Agents[i].Pos - Sim.Agents[tgt].Pos).Length; if (d < bd) { bd = d; best = i; } }
                    _defUser = best;
                    return best;
                }
            }
            if (side == SimSide.Defense && Sim.BallSide == SimSide.Offense && scrim && _defUser >= 0 && Sim.Agents[_defUser].IsDown) _defUser = NearestUserAgentToBall(_defUser);
            if (side == SimSide.Defense && scrim && Sim.BallSide == SimSide.Offense) return _defUser;
            if (!scrim && holder < 0) return NearestUserAgentToBall(-1);
            if (holder >= 0 && Sim.Agents[holder].Side != side) return NearestUserAgentToBall(-1);
            return NearestUserAgentToBall(-1);
        }

        ControlInput BuildControl(MatchInputFrame inp, float dt, out int simUser)
        {
            var ci = new ControlInput();
            simUser = -1;
            UserAgent = SelectUser(inp, false);
            if (UserAgent < 0 || Config.CpuVsCpu) return ci;
            var sim = Sim; var a = sim.Agents[UserAgent];
            SimSide side = UserSide();
            bool scrim = sim.Kind == PlayKind.Scrimmage;

            // defense: B switches to the defender nearest the ball
            if (side == SimSide.Defense && inp.Down[(int)Btn.B] && sim.BallSide != SimSide.Defense) { _defUser = NearestUserAgentToBall(_defUser); UserAgent = _defUser; a = sim.Agents[UserAgent]; }

            // stick -> field frame through the camera
            float yaw = Cams.MoveBasisYaw * Mathf.Deg2Rad, s = Mathf.Sin(yaw), c = Mathf.Cos(yaw);
            Vector2 st = inp.Move;
            Vector2 world = new Vector2(st.x * c + st.y * s, -st.x * s + st.y * c);
            Vec2 move = _frame.DirToSim(world);
            ci.Move = move; ci.Sprint = inp.Sprint; ci.Protect = inp.LT || inp.LookHeldDown;
            bool stick = st.magnitude > 0.12f;

            bool qbHasBall = scrim && side == SimSide.Offense && UserAgent == 0 && a.HasBall && !sim.PassIsThrown;
            bool carrier = a.HasBall && !qbHasBall;
            bool anyButton = false;
            for (int i = 0; i < 6; i++) anyButton |= inp.Down[i];

            if (qbHasBall)
            {
                BuildThrow(inp, ref ci);
            }
            else if (carrier)
            {
                Vector3 fwd = Stage.Views[UserAgent] != null ? Stage.Views[UserAgent].Forward : Vector3.forward;
                if (inp.Down[(int)Btn.A]) ci.Hurdle = true;
                if (inp.Down[(int)Btn.B]) ci.Spin = true;
                if (inp.Down[(int)Btn.X]) ci.Dive = true;
                if (inp.Down[(int)Btn.Y]) ci.Truck = true;
                if (inp.Down[(int)Btn.LB] || inp.Down[(int)Btn.RB]) ci.StiffArm = true;
                if (inp.LookFlickLeft || inp.LookFlickRight || inp.LookFlickUp)
                {
                    Vector2 look = inp.Look;
                    Vector2 lw = new Vector2(look.x * c + look.y * s, -look.x * s + look.y * c);
                    Vector3 lv = new Vector3(lw.x, 0f, lw.y);
                    float rt = Vector3.Dot(lv, Vector3.Cross(Vector3.up, fwd)), fw = Vector3.Dot(lv, fwd);
                    if (Mathf.Abs(fw) > Mathf.Abs(rt) && fw > 0f) ci.Truck = true;
                    else if (rt < 0f) ci.JukeLeft = true; else ci.JukeRight = true;
                }
            }
            else if (side == SimSide.Defense && sim.BallSide == SimSide.Offense || !scrim)
            {
                if (inp.Down[(int)Btn.A]) ci.Tackle = true;
                if (inp.Down[(int)Btn.X]) ci.Dive = true;
                if (inp.Down[(int)Btn.Y]) ci.Jump = true;
            }
            else
            {
                if (inp.Down[(int)Btn.A] || inp.Down[(int)Btn.Y]) { ci.Jump = true; }
            }

            // assist: only hand the player to the human when he is steering or acting (the carrier and a quarterback holding the ball always are)
            bool active = qbHasBall || carrier || stick || anyButton || ci.ThrowToSlot > 0;
            simUser = active ? UserAgent : -1;
            return ci;
        }

        void BuildThrow(MatchInputFrame inp, ref ControlInput ci)
        {
            for (int i = 0; i < 6; i++)
            {
                int slot = _iconSlot[i];
                if (slot <= 0) continue;
                if (inp.Up[i] || (inp.Held[i] && inp.HeldTime[i] > 0.55f))
                {
                    if (_fired[i]) continue;
                    _fired[i] = true;
                    float held = inp.HeldTime[i];
                    ci.ThrowToSlot = slot;
                    ci.ThrowStyle = held < 0.18f ? ThrowStyle.Bullet : (held < 0.5f ? ThrowStyle.Touch : ThrowStyle.Lob);
                    Stage.LobThrow = ci.ThrowStyle == ThrowStyle.Lob;
                    Vector2 look = inp.Look;
                    if (look.magnitude > 0.3f)
                    {
                        float yaw = Cams.MoveBasisYaw * Mathf.Deg2Rad, sn = Mathf.Sin(yaw), cs = Mathf.Cos(yaw);
                        Vector2 lw = new Vector2(look.x * cs + look.y * sn, -look.x * sn + look.y * cs);
                        ci.LeadNudge = _frame.DirToSim(lw) * 4f;
                    }
                }
                if (!inp.Held[i] && !inp.Up[i]) _fired[i] = false;
            }
            if (inp.DPadUp) ci.Throwaway = true;
        }

        // ============================================================================================ events, camera, hud
        void HandleEvents()
        {
            foreach (var e in Stage.Fresh) { Audio.OnEvent(e); if (SimEvent != null) SimEvent(e); }
        }

        void React(PlayOutcome o)
        {
            Audio.Whistle();
            Color col = UiColorsLite.Blue; float secs = 2.4f;
            CoreTeam user = Config.UserTeam;
            bool bigGood = false, bad = false;
            switch (o.Headline)
            {
                case Headline.Touchdown: col = UiColorsLite.Gold; secs = 3.4f; bigGood = true; break;
                case Headline.FieldGoal: col = UiColorsLite.Green; secs = 2.8f; bigGood = true; break;
                case Headline.ExtraPoint: case Headline.TwoPointGood: col = UiColorsLite.Green; secs = 2.2f; bigGood = true; break;
                case Headline.Safety: col = UiColorsLite.Orange; secs = 3f; bigGood = true; break;
                case Headline.Interception: case Headline.FumbleLost: case Headline.TurnoverOnDowns: col = UiColorsLite.Red; secs = 3f; bad = true; break;
                case Headline.Penalty: col = UiColorsLite.Yellow; secs = 2.6f; break;
                case Headline.FirstDown: col = UiColorsLite.Blue; secs = 1.8f; break;
                case Headline.Sack: col = UiColorsLite.Red; secs = 2.0f; break;
                case Headline.FieldGoalMissed: case Headline.TwoPointFailed: col = UiColorsLite.Red; secs = 2.4f; bad = true; break;
            }
            if (o.Text.Length > 0 && o.Headline != Headline.Kickoff && o.Headline != Headline.Punt)
            {
                string sub = "";
                if (o.Headline == Headline.FirstDown) sub = Ordinal(Flow.State.Down) + " & " + (Flow.State.IsGoalToGo ? "GOAL" : Flow.State.Distance.ToString());
                else if (o.ScoringTeam.HasValue) sub = Flow.Abbr(CoreTeam.Home) + " " + Flow.State.HomeScore + "  -  " + Flow.Abbr(CoreTeam.Away) + " " + Flow.State.AwayScore;
                else if (o.Headline == Headline.Penalty) sub = o.Result.Penalty.CommittedBy == CoreTeam.Home ? Flow.Abbr(CoreTeam.Home) : Flow.Abbr(CoreTeam.Away);
                Banner(o.Text, col, secs, sub);
            }
            if (o.Headline == Headline.Kickoff && o.Result != null && o.Result.Touchback) Banner("TOUCHBACK", UiColorsLite.Blue, 1.8f, "");
            if (o.Headline == Headline.Punt && o.Result != null && o.Result.Touchback) Banner("TOUCHBACK", UiColorsLite.Blue, 1.8f, "");
            // crowd: cheer when the home team scores or makes a big play, boo home-team turnovers
            if (bigGood) Audio.Cheer(o.ScoringTeam == CoreTeam.Home ? 1f : 0.6f);
            else if (bad && o.Offense == CoreTeam.Home && o.Headline != Headline.TwoPointFailed) Audio.Boo();
            else if (bad) Audio.Cheer(0.5f);
            else if (o.Result != null && o.Result.Yards >= 20 && (o.Result.Type == PlayType.Run || o.Result.Type == PlayType.Pass)) Audio.Cheer(o.Offense == CoreTeam.Home ? 0.8f : 0.4f);
            else if (o.Headline == Headline.FirstDown && o.Offense == CoreTeam.Home) Audio.Cheer(0.35f);
        }

        void ApplyCameraTarget(bool snap)
        {
            if (Sim == null) return;
            Vector3 focus, fvel = Vector3.zero;
            var b = Sim.Ball;
            if (Sim.Phase == PlayPhase.PreSnap) focus = _frame.ToUnity(new Vec2(Sim.BallX0, Sim.LosY + 1f));
            else if (b.State == BallState.Held && b.Holder >= 0) { focus = _frame.ToUnity(Sim.Agents[b.Holder].Pos); fvel = _frame.VelToUnity(Sim.Agents[b.Holder].Vel); }
            else { focus = Stage.Ball != null ? Stage.Ball.Position : Vector3.zero; fvel = _frame.VelToUnity(b.Vel.XY) * 0.45f; }
            Cams.SetFocus(focus, fvel);
            Cams.SetUser(UserAgent >= 0 ? Stage.Views[UserAgent] : null, UserAgent == 0 && Sim.Agents[0].HasBall && !Sim.PassIsThrown && Sim.Kind == PlayKind.Scrimmage);
            if (UserAgent >= 0 && Stage.Views[UserAgent] != null) Cams.SetUserVelocity(_frame.VelToUnity(Sim.Agents[UserAgent].Vel));
        }

        void PresentFrame(float dt, float liveT, float preT)
        {
            ApplyCameraTarget(false);
            // ring marker under the controlled player
            bool showRing = !Config.CpuVsCpu && UserAgent >= 0 && Stage.Views[UserAgent] != null;
            Markers.SetRing(showRing ? Stage.Views[UserAgent].transform : null);
            // lines: the line of scrimmage disappears once the ball is snapped (the first-down line stays until the play ends)
            var st = Flow.State;
            bool scrim = st.Phase == GamePhase.Scrimmage;
            Markers.SetLines(_frame, st.BallSpot, st.BallSpot + st.Distance, scrim && Sim.Phase == PlayPhase.PreSnap, scrim && !st.IsGoalToGo);
            UpdateIcons();
            if (Ui != null) Ui.SetCameraLabel(Cams.Mode == CameraMode.Broadcast ? "BROADCAST" : Cams.Mode == CameraMode.Behind ? "BEHIND PLAYER" : "FIRST PERSON");
            if (!Config.CpuVsCpu && Sim.Phase == PlayPhase.Live) SetLiveHint();
        }

        void SetLiveHint()
        {
            if (UserAgent < 0) { SetHint(""); return; }
            var a = Sim.Agents[UserAgent];
            SimSide side = UserSide();
            if (UserAgent == 0 && a.HasBall && !Sim.PassIsThrown && Sim.Kind == PlayKind.Scrimmage) SetHint("A B X Y LB RB  THROW  (TAP = BULLET, HOLD = TOUCH / LOB)      RS  LEAD      LS  SCRAMBLE");
            else if (a.HasBall) SetHint("RT SPRINT   A HURDLE   B SPIN   X DIVE   Y TRUCK   LB RB STIFF-ARM   FLICK RS JUKE   LT PROTECT");
            else if (side == SimSide.Defense) SetHint("A TACKLE   Y JUMP / SWAT   X DIVE   B SWITCH PLAYER   RT SPRINT");
            else SetHint("A JUMP   RT SPRINT");
        }

        void UpdateIcons()
        {
            for (int i = 0; i < 6; i++) _iconSlot[i] = 0;
            _icons.Clear();
            if (Ui == null || Config.CpuVsCpu) return;
            var sim = Sim;
            bool show = sim.Phase == PlayPhase.Live && sim.Kind == PlayKind.Scrimmage && UserSide() == SimSide.Offense && UserAgent == 0 && sim.Agents[0].HasBall && !sim.PassIsThrown;
            if (!show) { Ui.SetReceiverIcons(null, MainCamera); return; }
            // backs take LB, receivers left-to-right (as the camera sees them) take X, Y, B, A, RB
            var recs = new List<int>();
            for (int i = 1; i < 11; i++) if (sim.Agents[i].IsReceiverEligible) recs.Add(i);
            var backs = new List<int>(); var wides = new List<int>();
            foreach (int i in recs)
            {
                var a = sim.Agents[i];
                bool back = a.PlayPos == Position.RB || a.PlayPos == Position.FB;
                if (back) backs.Add(i); else wides.Add(i);
            }
            // screen-left = smaller "right" component along the camera's right vector
            // broadcast camera: offense moves left->right, so the offense's left (small X) is the camera-far side; sort by lateral for "left to right" of the offense
            wides.Sort((p, q) => sim.Agents[p].Pos.X.CompareTo(sim.Agents[q].Pos.X));
            Btn[] wideBtns = { Btn.X, Btn.Y, Btn.B, Btn.A, Btn.RB };
            int wi = 0;
            foreach (int i in wides) { if (wi >= wideBtns.Length) break; Assign(wideBtns[wi++], i); }
            int bi = 0;
            foreach (int i in backs) { Assign(bi == 0 ? Btn.LB : Btn.RB, i); bi++; }
            Ui.SetReceiverIcons(_icons, MainCamera);
        }

        void Assign(Btn b, int agent)
        {
            if (_iconSlot[(int)b] != 0) return;
            _iconSlot[(int)b] = agent;
            Vector3 p = Stage.WorldPos(agent) + Vector3.up * 2.45f;
            _icons.Add(new ReceiverIcon { World = p, Button = b, Label = Sim.Agents[agent].Label });
        }

        // ============================================================================================ scoreboard
        ScoreSnapshot MakeScore(bool live, float playClock = -1f, float preT = 0f)
        {
            var s = Flow.State;
            var sc = new ScoreSnapshot
            {
                HomeAbbr = Flow.Abbr(CoreTeam.Home), AwayAbbr = Flow.Abbr(CoreTeam.Away),
                HomeCrest = "crest_" + HomeInfo.Id, AwayCrest = "crest_" + AwayInfo.Id,
                HomeColor = HomeInfo.PrimaryUi, AwayColor = AwayInfo.PrimaryUi,
                HomeScore = s.HomeScore, AwayScore = s.AwayScore,
                Quarter = s.Quarter == 1 ? "1ST" : s.Quarter == 2 ? "2ND" : s.Quarter == 3 ? "3RD" : s.Quarter == 4 ? "4TH" : "OT",
                PossessionHome = s.Possession == CoreTeam.Home,
                HomeTimeouts = s.HomeTimeouts, AwayTimeouts = s.AwayTimeouts, InPlay = live
            };
            float clock = s.ClockSeconds;
            if (live && Sim != null && Sim.Phase == PlayPhase.Live && Sim.Kind != PlayKind.Kickoff) clock = Mathf.Max(0f, clock - Sim.Time);
            else if (!live && Phase == MatchPhase.PreSnap && _runoff0 > 0f) clock = Mathf.Max(0f, clock - _runoff0 * Mathf.Clamp01(preT / 5f));
            int ci = Mathf.CeilToInt(clock);
            sc.Clock = (ci / 60) + ":" + (ci % 60).ToString("00");
            sc.TwoMinute = (s.Quarter == 2 || s.Quarter == 4) && clock <= 120f;
            sc.PlayClock = playClock >= 0f ? Mathf.CeilToInt(playClock) : -1;
            if (s.Phase == GamePhase.Kickoff) { sc.DownDistance = "KICKOFF"; sc.Spot = ""; }
            else if (s.Phase == GamePhase.PointAfter) { sc.DownDistance = s.RequiresTwoPoint ? "2-PT TRY" : "PAT"; sc.Spot = ""; }
            else { sc.DownDistance = Ordinal(s.Down) + " & " + (s.IsGoalToGo ? "GOAL" : s.Distance.ToString()); sc.Spot = "BALL ON " + SpotText(); }
            return sc;
        }

        // ============================================================================================ autopilot
        MatchInputFrame AutoInput()
        {
            var f = _input; f.Clear();
            var sim = Sim;
            if (sim == null || sim.Phase != PlayPhase.Live) return f;
            _autoT += Time.deltaTime;
            int u = UserAgent;
            if (u < 0) return f;
            var a = sim.Agents[u];
            // field direction toward the end zone the user attacks, as a stick in camera space
            float dirY = UserSide() == SimSide.Offense ? 1f : -1f;
            Vector3 goalDir = _frame.DirToUnity(new Vec2(0f, dirY));
            if (UserSide() == SimSide.Offense && a.HasBall && u == 0 && !sim.PassIsThrown)
            {
                // quarterback: throw to a receiver after a beat
                if (_autoT > 1.7f)
                {
                    int best = 0;
                    foreach (int i in new[] { 7, 8, 9, 10, 1 }) { if (sim.Agents[i].IsReceiverEligible && (best == 0 || sim.Agents[i].Pos.Y > sim.Agents[best].Pos.Y)) best = i; }
                    if (best > 0) for (int b = 0; b < 6; b++) if (_iconSlot[b] == best) { f.Down[b] = true; f.Up[b] = true; f.HeldTime[b] = 0.1f; break; }
                }
                return f;
            }
            // ball carrier / defender: move toward the goal or the ball with the occasional juke
            Vector3 want;
            if (a.HasBall) want = goalDir;
            else
            {
                Vector3 target = sim.Ball.Holder >= 0 ? _frame.ToUnity(sim.Agents[sim.Ball.Holder].Pos) : Stage.Ball.Position;
                want = (target - _frame.ToUnity(a.Pos)); want.y = 0f; if (want.sqrMagnitude < 0.01f) want = goalDir;
                want.Normalize();
            }
            float yaw = Cams.MoveBasisYaw * Mathf.Deg2Rad;
            // inverse of the camera mapping: world -> stick
            Vector2 w2 = new Vector2(want.x, want.z);
            float s = Mathf.Sin(yaw), c = Mathf.Cos(yaw);
            f.Move = new Vector2(w2.x * c - w2.y * s, w2.x * s + w2.y * c);
            f.Sprint = true;
            if (a.HasBall && Time.time > _autoNextMove) { _autoNextMove = Time.time + 1.4f; if (_autoRng.Next(2) == 0) { f.LookFlickLeft = true; f.Look = new Vector2(-1f, 0f); } else { f.LookFlickRight = true; f.Look = new Vector2(1f, 0f); } }
            return f;
        }
    }

    /// <summary>Palette used by banners (matches Docs/UIStyleGuide.md).</summary>
    public static class UiColorsLite
    {
        public static readonly Color Blue = new Color(0.176f, 0.353f, 0.737f), Gold = new Color(1f, 0.784f, 0.216f), Green = new Color(0.067f, 0.78f, 0.31f),
            Red = new Color(0.76f, 0.1f, 0.1f), Orange = new Color(1f, 0.565f, 0f), Yellow = new Color(1f, 0.918f, 0f);
    }
}
