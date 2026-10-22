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
    public sealed partial class MatchDirector : MonoBehaviour
    {
        public static MatchDirector Instance { get; private set; }
        /// <summary>Set by the autotest before the match scene loads: the director then plays the user side itself.</summary>
        public static bool ForceAutopilot;
        /// <summary>Autotest hook: the next snap is blown dead by this pre-snap foul (committed by the given team).</summary>
        public static PenaltyType? ForceFoul; public static CoreTeam ForceFoulBy = CoreTeam.Away;
        /// <summary>Autotest hook: dictates the offensive and defensive call of the next snaps (skips the play-call screen); return null for the normal flow.</summary>
        public static Func<MatchFlow, KeyValuePair<Play, Play>?> ForcedCall;
        /// <summary>Autotest hook: the autopilot ball carrier uses this move (Juke, Spin, StiffArm, Truck, Hurdle) whenever a defender is close ahead.</summary>
        public static string AutoMove;
        /// <summary>Autotest hook: seconds the autopilot waits at the line before it snaps (the broadcast shots need a few seconds of pre-snap to be seen).</summary>
        public static float AutopilotSnapDelay = 0.9f;

        public Camera MainCamera;
        public LightingController Lighting;
        [Tooltip("A MonoBehaviour implementing IMatchPresentation (PixFootball.UI.MatchHud).")]
        public MonoBehaviour UiBehaviour;

        public MatchConfig Config { get; private set; }
        public MatchFlow Flow { get; private set; }
        public PlayStage Stage { get; private set; }
        public MatchCameraRig Cams { get; private set; }
        public FieldMarkers Markers { get; private set; }
        /// <summary>v0.7: the routes drawn on the turf while the user holds LT (see <see cref="RouteOverlay"/>).</summary>
        public RouteOverlay Routes { get; private set; }
        public MatchAudio Audio { get; private set; }
        public CollisionFeel Feel { get; private set; }
        public MatchOfficials Officials { get; private set; }
        /// <summary>v0.6 look layer: kit dirt, field wear, weather (null when the look assets are missing).</summary>
        public LookDirector Look { get; private set; }
        public WeatherPreset Weather { get; private set; }
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
        /// <summary>The play-call screen model while the screen is open (autotest and tools read it).</summary>
        public PlayCallModel CallModel { get; private set; }
        public bool PenaltyOpen { get; private set; }
        public PenaltyPanelData PenaltyData { get; private set; }
        public string StatusText { get; private set; } = "";

        /// <summary>v0.5 broadcast layer (replays, camera director, TV overlays, shows); null in tests or when the Presentation assembly is not in the scene.</summary>
        public IBroadcast Broadcast { get; private set; }
        /// <summary>Set by the broadcast layer while a replay owns the pad: the pause controller does not poll.</summary>
        public bool InputLocked;
        bool BroadcastBusy { get { return Broadcast != null && Broadcast.Busy; } }

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
        void Awake() { Instance = this; GameSession.ResetTime(); }
        Action _hResume, _hQuit, _hCam, _hLight, _hReplay;
        void OnDestroy()
        {
            if (Instance == this) Instance = null; GameSession.ResetTime(); MatchInput.Scripted = null;
            // QA-733: the HUD outlives nothing, but the handlers close over this director: unsubscribe
            if (Ui != null) { if (_hResume != null) Ui.ResumeRequested -= _hResume; if (_hQuit != null) Ui.QuitRequested -= _hQuit; if (_hCam != null) Ui.CameraCycleRequested -= _hCam; if (_hLight != null) Ui.LightingToggleRequested -= _hLight; if (_hReplay != null) Ui.ReplayRequested -= _hReplay; }
            if (Stage != null && Stage.Contacts != null) Stage.Contacts.Impact -= OnContactImpact;
        }

        IEnumerator Start()
        {
            Ui = UiBehaviour as IMatchPresentation;
            if (MainCamera == null) MainCamera = Camera.main;
            PlayerLod.ViewCamera = MainCamera;
            Config = MatchData.Current.Clone();
            if (Config.CpuVsCpu || ForceAutopilot) Autopilot = true;
            var league = MatchData.League;
            LeagueTeam home = MatchData.TeamByKey(Config.HomeId), away = MatchData.TeamByKey(Config.AwayId);
            HomeInfo = TeamCatalog.ById(Config.HomeId); AwayInfo = TeamCatalog.ById(Config.AwayId);
            _homeKit = UniformSet.For(HomeInfo, true); _awayKit = UniformSet.For(AwayInfo, false);
            _rng = new Rng(Config.Seed != 0 ? Config.Seed : Environment.TickCount);

            SetupLook(Config);
            Stage = new GameObject("PlayStage").AddComponent<PlayStage>();
            Stage.transform.SetParent(transform, false);
            Stage.Init();
            Look = LookDirector.Create(transform, Stage);
            Stage.Warm(8, 8, 9);
            if (PixSettings.PhotoPlayers) Stage.WarmAllBodies(2);      // v0.9.0 CONNECT E-227 / E-269: every roster body (size x base) with its ragdoll is built behind the loading screen
            Cams = MatchCameraRig.Create(transform, MainCamera);
            if (Config.CpuVsCpu) Cams.SetMode(CameraMode.Broadcast, true);   // v0.6: the broadcast camera is for simulated games; the user plays from behind the offense / defense
            Feel = CollisionFeel.Create(MainCamera);
            if (Stage.Contacts != null) Stage.Contacts.Impact += OnContactImpact;
            Markers = FieldMarkers.Create(transform);
            Routes = RouteOverlay.Create(transform);
            Audio = gameObject.AddComponent<MatchAudio>(); Audio.Init();
            try { Officials = MatchOfficials.Create(transform, HomeInfo, AwayInfo); } catch (Exception e) { Debug.LogWarning("[PIX] officials unavailable: " + e.Message); }
            if (Ui != null)
            {
                Ui.Build(this);
                _hResume = () => SetPaused(false); _hQuit = () => { _quit = true; GameSession.QuitToMenu(); };
                _hCam = () => { Cams.Cycle(); if (!Config.CpuVsCpu) PixSettings.DefaultCamera = Cams.Mode; };
                _hLight = () => { if (Lighting != null) Lighting.Toggle(false); };
                Ui.ResumeRequested += _hResume; Ui.QuitRequested += _hQuit; Ui.CameraCycleRequested += _hCam; Ui.LightingToggleRequested += _hLight;
            }
            if (Ui != null) Ui.SetScore(InitialScore(league));
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)) { var b = mb as IBroadcast; if (b != null) { Broadcast = b; break; } }
            if (Broadcast != null)
            {
                Broadcast.Attach(this);
                if (Ui != null) { _hReplay = () => { if (Broadcast != null) Broadcast.RequestManualReplay(); }; Ui.ReplayRequested += _hReplay; }
            }
            yield return null;
            yield return CoinToss();
            if (_quit) yield break;
            if (Broadcast != null) yield return Broadcast.GameIntro();
            if (_quit) yield break;
            yield return GameLoop();
        }

        // ============================================================================================ v0.6 look
        static string ArgValue(string name) { string[] a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return null; }

        /// <summary>Natural grass or artificial turf for a home stadium: a fixed third of the league plays on turf.</summary>
        public static bool ArtificialTurfFor(string homeId)
        {
            string arg = ArgValue("-pixTurf");
            if (arg != null) return arg.StartsWith("art", StringComparison.OrdinalIgnoreCase);
            uint h = 2166136261u; foreach (char c in homeId ?? "") h = (h ^ c) * 16777619u;
            return h % 100u < 34u;
        }

        void SetupLook(MatchConfig cfg)
        {
            PixLook.ReadArgs();
            var wc = cfg.Weather; var tc = cfg.Time;
            string w = ArgValue("-pixWeather"), t = ArgValue("-pixTime");
            int n;
            if (w != null && int.TryParse(w, out n)) wc = (WeatherChoice)Mathf.Clamp(n, 0, 5);
            if (t != null && int.TryParse(t, out n)) tc = (TimeChoice)Mathf.Clamp(n, 0, 3);
            var ws = WeatherSystem.Ensure(Lighting);
            int seed = cfg.Seed != 0 ? cfg.Seed : Environment.TickCount;
            Weather = ws.Choose(wc, tc, seed, ArtificialTurfFor(cfg.HomeId));
            cfg.ResolvedWeather = Weather.Kind; cfg.ResolvedTime = Weather.Time; cfg.BallSlickness = Weather.BallSlickness;
            FieldWear.Create(transform);
            FieldLogo.Create(transform, "crest_" + HomeInfo.Id);
            Note("weather: " + Weather.Label + (ws.ArtificialTurf ? " (artificial turf)" : " (natural grass)"));
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
            BeginTossScene();
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
                TossCaption(TeamName(user) + " call it in the air.");
                Ui.ShowCoinTossCall(TeamName(user), TeamName(cpu), h => { userCallsHeads = h; called = true; });
                if (Autopilot) { yield return new WaitForSecondsRealtime(1.6f); userCallsHeads = _autoRng.Next(2) == 0; called = true; }
                while (!called) yield return null;
                userWon = resultHeads == userCallsHeads;
                TossCaption("It's " + (resultHeads ? "heads" : "tails") + ". " + (userWon ? TeamName(user) : TeamName(cpu)) + " won the toss.");
                bool userReceives = false, answered = false;
                CoreTeam winner = userWon ? user : cpu;
                Ui.ShowCoinTossResult(resultHeads, userWon, TeamName(winner), r => { userReceives = r; answered = true; });
                if (Autopilot) { yield return new WaitForSecondsRealtime(1.2f); }
                if (Autopilot && userWon) { userReceives = true; answered = true; }
                while (!answered) yield return null;
                bool winnerReceives = userWon ? userReceives : !_rng.Chance(0.25f);
                receiver = winnerReceives ? winner : winner.Opponent();
            }
            TossCaption(TeamName(receiver) + " will receive.");
            CoreTeam kicker = receiver.Opponent();
            if (Ui != null) Ui.HidePlayCall();
            Flow = new MatchFlow(Config, MatchData.TeamByKey(Config.HomeId), MatchData.TeamByKey(Config.AwayId), kicker, _rng.Fork());
            Markers.SetHashMarks(Flow.Rules);
            Note("toss: " + TeamName(receiver) + " receive");
            if (_tossScene) { float tw = 0f; while (tw < 1.5f && !_quit) { tw += Time.unscaledDeltaTime; yield return null; } EndTossScene(); }
            yield return null;
        }

        // ============================================================================================ game loop
        // ---- QA-732: one exception inside a stage of the game loop no longer kills the whole match coroutine chain
        int _loopFailures;
        IEnumerator Safe(IEnumerator inner, string what)
        {
            while (true)
            {
                object cur = null; bool finished = false, failed = false;
                try
                {
                    if (!inner.MoveNext()) finished = true; else cur = inner.Current;
                }
                catch (Exception e)
                {
                    Debug.LogError("[PIX] " + what + " failed: " + e);
                    failed = true;
                }
                if (failed)
                {
                    _loopFailures++;
                    // v0.9.0 CONNECT E-291: the 22 views stay on the field for a frame (a developer sees the state), the fade covers the rebuild
                    yield return Fade(1f, 0.15f);
                    if (Stage != null) Stage.End();
                    Sim = null; _recall = false;
                    if (_loopFailures >= 5) { Debug.LogError("[PIX] too many failures in the game loop: back to the menu"); _quit = true; GameSession.QuitToMenu(); }
                    yield break;
                }
                if (finished) yield break;
                var nested = cur as IEnumerator;
                if (nested != null) yield return Safe(nested, what); else yield return cur;
            }
        }

        IEnumerator GameLoop()
        {
            var state = Flow.State;
            while (!state.IsFinal && !_quit)
            {
                CoreTeam offTeam = state.Possession;
                if (PrePlayHook != null) PrePlayHook(Flow);
                offTeam = state.Possession;
                _userIsOffense = !Config.CpuVsCpu && offTeam == Config.UserTeam;
                _frame = PlayFrame.For(state);
                if (state.Quarter != _lastQuarter) _lastQuarter = state.Quarter;
                if (WeatherSystem.Instance != null) WeatherSystem.Instance.SetGameProgress(state.Quarter);   // v0.9 (GFX-414): the sun sinks over the game

                // ---- calls ----
                _playClockCarry = Mathf.Max(8f, state.PlayClock);
                yield return Safe(Calls(), "play calling");
                if (_quit) yield break;

                // ---- build the play and wait for the snap ----
                while (true)
                {
                    _recall = false;
                    // v0.9.0 CONNECT E-268: the preview built for the call screen is kept when the call did not change (the field was built twice per snap)
                    if (_previewSim != null && ReferenceEquals(_previewSim, Sim) && ReferenceEquals(_previewOff, _offCall) && ReferenceEquals(_previewDef, _defCall) && Sim.Phase == PlayPhase.PreSnap) { _previewSim = null; _previewKeep = true; }
                    else { _previewSim = null; if (Stage != null) Stage.NextBeginHuddle = _huddleOn && !Autopilot; BeginPlay(); }
                    yield return Safe(PreSnap(), "pre-snap");
                    if (_quit) yield break;
                    if (!_recall) break;
                    yield return Safe(Calls(), "play calling");
                }
                if (_periodInterrupt)
                {
                    _periodInterrupt = false;
                    yield return PeriodBreak(_pendingPeriod, false);
                    continue;
                }
                yield return Safe(LivePlay(), "live play");
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
            if (ForcedCall != null) { var fc = ForcedCall(Flow); if (fc.HasValue) { _offCall = fc.Value.Key; _defCall = fc.Value.Value; yield return null; yield break; } }
            _offCall = cpuOff; _defCall = cpuDef;
            if (Config.CpuVsCpu) yield break;
            bool userOff = state.Possession == Config.UserTeam;
            if (!userOff)
            {
                // the CPU offense calls first; a kneel or spike needs no answer from the user
                if (cpuOff.Category == PlayCategory.Kneel || cpuOff.Category == PlayCategory.Spike) { _defCall = Flow.DefenseAnswer(cpuOff); yield break; }
            }
            var model = Flow.CreateCallModel(userOff, userOff ? null : cpuOff);
            if (model == null) { if (!userOff) _defCall = Flow.DefenseAnswer(cpuOff); yield break; }
            // ---- the full play-call screen over a preview of the field; the play clock keeps ticking while the user chooses ----
            PreviewField();
            if (_fade > 0.01f) StartCoroutine(Fade(0f, 0.12f));      // v0.9.0 CONNECT E-270: a quick dip, the huddle and the walk-in carry the cut
            float startClock = Mathf.Max(8f, _playClockCarry > 0f ? _playClockCarry : Flow.Rules.PlayClockDead);
            _playClock = startClock;
            CallModel = model;
            Ui.ShowPlayCall(model, _playClock);
            float t = 0f;
            _prevLT = _prevRT = true;
            while (model.Chosen == null && !_quit)
            {
                if (BroadcastBusy) { yield return null; continue; }   // a replay owns the screen and the pad
                float dtU = Time.unscaledDeltaTime;
                if (Autopilot)
                {
                    t += Time.deltaTime;
                    if (t > 1.0f) AutoChoose(model);
                }
                else if (!Paused)
                {
                    _playClock -= dtU; t += dtU;
                    var inp = MatchInput.Instance.Poll(dtU);
                    DriveCallScreen(model, inp);
                    if (model.TimeoutRequested)
                    {
                        model.ClearTimeoutRequest();
                        if (TryUserTimeout()) { _playClock = Flow.Rules.PlayClockDead; model.Situation.Timeouts = Flow.State.TimeoutsOf(Config.UserTeam); }
                    }
                    if (_playClock <= 0f && model.Chosen == null) model.Expire();
                }
                Ui.ShowPlayCall(model, _playClock);
                // v0.9.0 CONNECT E-293: the field behind the call screen lives (idles breathe, the huddle breaks to the line, the crew walks)
                if (Sim != null && Stage != null && !Paused)
                {
                    float dtp = Mathf.Min(Time.deltaTime, 0.05f);
                    Sim.Tick(dtp, default(ControlInput), -1);
                    Stage.Present(dtp);
                    if (Officials != null) Officials.Tick(dtp, Sim, false);
                }
                yield return null;
            }
            Ui.HidePlayCall(); CallModel = null;
            if (_callSearchTyping) EndCallSearch();
            if (_quit) yield break;
            _playClockCarry = Mathf.Max(_playClock, 7f);
            Play choice = model.Chosen;
            if (choice == null) yield break;
            Flow.RememberUserCall(userOff, choice);
            if (userOff) { _offCall = choice; _defCall = Flow.DefenseAnswer(_offCall); }
            else _defCall = choice;
            yield return null;
        }

        void AutoChoose(PlayCallModel m)
        {
            // autopilot: prefer the coach's pick, otherwise whatever the screen offers first
            if (m.CoachPicks.Count > 0 && m.Tabs.Contains(CallTab.AskCoach)) { m.SetTab(CallTab.AskCoach); m.Confirm(); if (m.Chosen != null) return; }
            m.Expire();
        }

        /// <summary>
        /// Feeds one frame of input to the play-call model. Call sheet (glass): D-pad left / right move along the play cards, up / down change the group (formation, concept...), A calls the play,
        /// B goes back to the suggestions (or clears a search), X cycles the type filter, Y flips the play, LB / RB page the cards, LT / RT switch tab, RS left / right quick-find by initial letter,
        /// R3 (keyboard Backspace) calls a timeout, "/" types a search on the keyboard. Classic: the old three-stage flow with RT for tabs and LT for the timeout.
        /// </summary>
        void DriveCallScreen(PlayCallModel m, MatchInputFrame inp)
        {
            if (m.SheetMode && _callSearchTyping) { DriveCallSearch(m, inp); return; }
            if (inp.View) { Cams.Cycle(); if (!Config.CpuVsCpu) PixSettings.DefaultCamera = Cams.Mode; }
            if (inp.Menu) { SetPaused(true); return; }
            bool rt = inp.Sprint, rtEdge = rt && !_prevRT; _prevRT = rt;
            bool lt = inp.LT, ltEdge = lt && !_prevLT; _prevLT = lt;
            if (inp.NavX != 0 || inp.NavY != 0) m.Navigate(inp.NavX, inp.NavY);
            if (inp.Down[(int)Btn.A]) m.Confirm();
            else if (inp.Down[(int)Btn.B]) m.Back();
            else if (inp.Down[(int)Btn.LB]) m.PageStep(-1);
            else if (inp.Down[(int)Btn.RB]) m.PageStep(1);
            else if (inp.Down[(int)Btn.X]) m.CycleFilter(1);
            else if (inp.Down[(int)Btn.Y]) m.ToggleFlip();
            if (rtEdge) m.CycleTab(1);
            if (m.SheetMode)
            {
                if (ltEdge) m.CycleTab(-1);
                if (inp.LookFlickLeft) m.CycleLetter(-1); else if (inp.LookFlickRight) m.CycleLetter(1);
                if (inp.Timeout) m.RequestTimeout();
                if (inp.SearchKey && !MatchInput.CaptureText) { _callSearchTyping = true; MatchInput.CaptureText = true; MatchInput.TypedText.Clear(); }
            }
            else if (ltEdge) m.RequestTimeout();
        }

        bool _callSearchTyping;

        /// <summary>Keyboard search mode of the call sheet: typed text filters the plays by name, Enter / Esc / "/" leave it (Backspace deletes, an empty Backspace clears and leaves).</summary>
        void DriveCallSearch(PlayCallModel m, MatchInputFrame inp)
        {
            var typed = MatchInput.TypedText;
            for (int i = 0; i < typed.Length; i++) { char c = typed[i]; if (c == '/' && i == 0 && m.SearchText.Length == 0) continue; m.AppendSearch(c); }
            typed.Clear();
            if (inp.Cancel) { if (m.SearchText.Length == 0) EndCallSearch(); else m.BackspaceSearch(); }
            if (inp.Confirm || inp.Menu || inp.SearchKey) EndCallSearch();
        }

        void EndCallSearch() { _callSearchTyping = false; MatchInput.CaptureText = false; MatchInput.TypedText.Clear(); MatchInput.SwallowInput(2); }

        float _playClock, _playClockCarry;
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
        Play _previewOff, _previewDef; PlaySimulation _previewSim; bool _previewKeep;
        /// <summary>v0.9.0 CONNECT E-252: the offense huddles and breaks to the line, the defense jogs up (QA: off with PixSettings / a test).</summary>
        public static bool HuddleBreak = true;
        bool _huddleOn { get { return HuddleBreak && Flow != null && Flow.State.Phase == GamePhase.Scrimmage; } }

        void PreviewField()
        {
            var state = Flow.State;
            // show the spot with a throwaway sim of the called formation (so the field is not empty behind the menu)
            if (Stage != null) Stage.NextBeginHuddle = _huddleOn && !Autopilot;
            BeginPlay(_offCall, _defCall);
            _previewSim = Sim; _previewOff = _offCall; _previewDef = _defCall;
        }

        // ---------------------------------------------------------------------------------------- build the play
        void BeginPlay() { BeginPlay(_offCall, _defCall); }

        void BeginPlay(Play off, Play def)
        {
            var state = Flow.State;
            Sim = Flow.CreateSim(off, def, false);
            PlayNumber++;
            _frame = PlayFrame.For(state);
            var offKit = state.Possession == CoreTeam.Home ? _homeKit : _awayKit;
            var defKit = state.Possession == CoreTeam.Home ? _awayKit : _homeKit;
            Stage.Begin(Sim, _frame, offKit, defKit, PlayNumber);
            Cams.SetFrame(_frame, true);
            ResetPreSnap();
            if (Officials != null) Officials.BeginPlay(_frame, Sim);
            bool scrim = state.Phase == GamePhase.Scrimmage;
            Markers.SetLines(_frame, state.BallSpot, state.BallSpot + state.Distance, scrim, scrim && !state.IsGoalToGo);
            _userIsOffense = !Config.CpuVsCpu && state.Possession == Config.UserTeam;
            // the defender the user controls pre-snap
            _defUser = ChooseDefaultDefender();
            UserAgent = SelectUser(MatchInputIdle, true);
            ApplyCameraTarget(true);
            Cams.Cut();
            if (Ui != null) Ui.SetScore(MakeScore(false));
            if (Broadcast != null) Broadcast.OnPlayPrepared(Sim, _frame);
        }

        bool _starsOn; int _starPlay = -1; readonly List<Transform> _starList = new List<Transform>();
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
            ResetKickMeter();
            var state = Flow.State;
            _fadeTarget = 0f;
            float t = 0f;
            _runoff0 = state.ClockRunning ? state.PendingRunoff : 0f;
            bool hurry = Flow.CpuHurryUp;
            float cpuDelay = hurry ? 0.9f + (float)_autoRng.NextDouble() * 0.8f : 2.6f + (float)_autoRng.NextDouble() * 1.6f;
            bool snap = false, cpuTimeoutChecked = false, delayFoul = false;
            if (_fade > 0.01f) StartCoroutine(Fade(0f, 0.14f));
            float playClock = _playClockCarry > 0f ? _playClockCarry : Flow.Rules.PlayClockDead;
            _playClockCarry = 0f;
            _prevLT = true;
            Note("presnap " + SituationText());
            while (!snap && !_recall && !_quit)
            {
                float dt = Time.deltaTime;
                if (Paused) { PauseFrame(); yield return null; continue; }
                t += dt; playClock -= dt;
                MatchInputFrame inp = ReadInput();
                if (inp.Menu) { SetPaused(true); yield return null; continue; }
                if (inp.View) { Cams.Cycle(); if (!Config.CpuVsCpu) PixSettings.DefaultCamera = Cams.Mode; }
                bool kickoff = Sim.Kind == PlayKind.Kickoff;
                if (_userIsOffense && !kickoff)
                {
                    if (Sim.Kind == PlayKind.Scrimmage)
                    {
                        bool rc = false;
                        if (!Autopilot) { if (OffensePreSnapInput(inp, ref rc)) snap = true; }
                        else if (inp.Down[(int)Btn.A]) snap = true;
                        if (rc) { _recall = true; _playClockCarry = Mathf.Max(5f, playClock); break; }
                    }
                    else
                    {
                        if (inp.Down[(int)Btn.A]) snap = true;
                        if (inp.DPadDown || inp.LTDown) TryUserTimeout();   // LT is the timeout button everywhere (D-pad down still works here)
                        SetHint("[A] SNAP, THEN [A] AGAIN TO KICK ON THE MARKER   [LS] AIM   [LT] TIMEOUT");
                    }
                }
                else if (_userIsOffense && kickoff)
                {
                    if (inp.Down[(int)Btn.A]) { snap = true; KickPrePress(); } else if (t > 8f) snap = true;   // v0.7: the press starts the run-up with the meter reading; no press for 8 s and the kick goes on a default reading
                    SetHint("[A] RUN UP AND KICK ON THE MARKER   [LS] AIM");
                }
                else
                {
                    if (kickoff) { SetHint("GET READY TO RETURN THE KICK   [LS] MOVE   [RT] SPRINT"); }
                    else if (Sim.Kind == PlayKind.Scrimmage)
                    {
                        bool rc = false;
                        if (!Autopilot) DefensePreSnapInput(inp, ref rc);
                        if (rc) { _recall = true; _playClockCarry = Mathf.Max(5f, playClock); break; }
                    }
                    else
                    {
                        if (inp.DPadDown || inp.LTDown) TryUserTimeout();   // LT is the timeout button everywhere (D-pad down still works here)
                        SetHint("[LT] TIMEOUT   [VIEW] CAMERA");
                    }
                    if (t > cpuDelay && _ps == PsMode.None) snap = true;
                }
                if (Autopilot && t > AutopilotSnapDelay) snap = true;
                if (snap && Stage != null && Stage.BreakInProgress && Sim.Kind == PlayKind.Scrimmage && playClock > 2f) snap = false;      // v0.9.0 CONNECT E-252: nobody snaps the ball while the team is still breaking to the line
                if (playClock <= 0f && !snap)
                {
                    // delay of game: the user's offense let the clock run out
                    if (_userIsOffense && !Autopilot && Sim.Kind == PlayKind.Scrimmage && Sim.CallDelayOfGame()) delayFoul = true;
                    snap = true;
                }
                if (state.Phase == GamePhase.Scrimmage && t > 1.2f && !snap && !cpuTimeoutChecked)
                {
                    cpuTimeoutChecked = true; CoreTeam cto;
                    if (!Config.CpuVsCpu && Flow.TryCpuTimeout(out cto)) { Banner(TeamName(cto).ToUpperInvariant() + " TIMEOUT", UiColorsLite.Blue, 1.8f, ""); _runoff0 = 0f; }
                }
                UserAgent = SelectUser(inp, true);
                KickInputSample(inp);
                Sim.Tick(dt, default(ControlInput), KickUserPreSnap());
                Stage.Present(dt);
                PresentFrame(dt, 0f, t);
                if (Broadcast != null) Broadcast.OnPreSnapFrame(dt, t, playClock, snap);
                UpdatePreSnapHud(playClock);
                if (Ui != null) Ui.SetScore(MakeScore(false, playClock, t));
                yield return null;
            }
            if (Ui != null) { Ui.SetPreSnap(null); Ui.SetReceiverIcons(null, MainCamera); }
            if (_quit || _recall) yield break;

            // snap: burn the runoff; the period may end first
            yield return CadenceWindow();      // v0.9.0 QBSTANCE: the quarterback calls the cadence before the ball is snapped
            var pc = Flow.PrepareSnap();
            if (pc.Changed)
            {
                _pendingPeriod = pc; _periodInterrupt = true;
                Stage.End(); Sim = null;
                yield break;
            }
            Phase = MatchPhase.Live;
            if (Sim.Kind == PlayKind.Kickoff) Audio.Whistle();
            if (ForceFoul.HasValue && Sim.Kind == PlayKind.Scrimmage && state.Possession == Config.UserTeam) { if (Sim.CallPreSnapFoul(ForceFoul.Value, ForceFoulBy, ForceFoulBy == state.Possession ? 3 : 11)) delayFoul = true; ForceFoul = null; }
            if (!delayFoul) Sim.Tick(0.0167f, new ControlInput { Snap = true }, KickUserPreSnap());
            Note("snap " + Sim.OffPlay.Name + " vs " + Sim.DefPlay.Name + (delayFoul ? " (delay of game)" : ""));
            if (PlayStarted != null) PlayStarted(Sim);
            if (Broadcast != null) Broadcast.OnSnap(Sim);
            yield return null;
        }

        bool TryUserTimeout()
        {
            var s = Flow.State;
            if (Autopilot) return false;
            if (s.TimeoutsOf(Config.UserTeam) > 0 && (s.Phase == GamePhase.Scrimmage) && s.CallTimeout(Config.UserTeam))
            {
                _runoff0 = 0f;
                Banner(TeamName(Config.UserTeam).ToUpperInvariant() + " TIMEOUT", UiColorsLite.Blue, 1.8f, s.TimeoutsOf(Config.UserTeam) + " REMAINING");
                if (Officials != null) Officials.Signal(OfficialSignal.Timeout);
                return true;
            }
            return false;
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
                if (inp.View) { Cams.Cycle(); if (!Config.CpuVsCpu) PixSettings.DefaultCamera = Cams.Mode; }
                int user; ControlInput ci = new ControlInput();
                if (!KickControl(inp, ref ci, out user)) ci = BuildControl(inp, dt, out user);   // v0.7: the user's kick meter drives the kick
                sim.Tick(dt, ci, user);
                Stage.Present(dt);
                HandleEvents();
                PresentFrame(dt, t, 0f);
                if (Broadcast != null) Broadcast.OnPlayFrame(dt, true);
                if (Ui != null) Ui.SetScore(MakeScore(true));
                yield return null;
            }
            if (_quit) yield break;

            // ---- result ----
            Phase = MatchPhase.PostPlay;
            SetHint("");   // the controls line goes at the whistle, not 30 seconds later (QA-044 / 256)
            // ---- flags: the offended team accepts or declines (the user decides when it is his; the CPU uses the expected-points rule) ----
            PenaltyDecision decision = PenaltyDecision.Auto;
            var choice = Flow.PenaltyChoiceFor(sim);
            if (choice != null)
            {
                PenaltyDecision d = PenaltyDecision.Auto;
                yield return PenaltyFlow(sim, choice, x => d = x);
                decision = d;
                if (_quit) yield break;
            }
            var outcome = Flow.Finish(sim, decision);
            Note("result " + outcome.Text + " " + Flow.Summary());
            Markers.SetRing(null); Ui?.SetReceiverIcons(null, MainCamera); UpdateUserMarker(false); if (Routes != null) Routes.SetShown(false); Ui?.SetRouteBadges(null, MainCamera);
            React(outcome);
            ShowWhistle(sim, outcome);   // v0.7: why the play ended, in a small banner (skipped when the result banner already says it)
            if (PlayFinished != null) PlayFinished(outcome);
            if (Broadcast != null) Broadcast.BeginPostPlay(outcome, sim);
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
                if (Broadcast != null) Broadcast.OnPlayFrame(dt, false);
                if (Ui != null) Ui.SetScore(MakeScore(false));
                yield return null;
            }
            if (Broadcast != null && !_quit) yield return Broadcast.FinishPostPlay();
            // ---- period / game transitions ----
            if (outcome.PeriodChanged || outcome.GameOver)
            {
                var pc = new PeriodChange { Changed = true, From = outcome.QuarterBefore, To = outcome.QuarterAfter, Final = outcome.GameOver, Halftime = outcome.QuarterBefore == 2 && outcome.QuarterAfter == 3 };
                yield return PeriodBreak(pc, true);
            }
            else CheckTwoMinute();
            // fade out, clear, fade in is handled by the next BeginPlay (cut hidden by the banner / fade)
            // v0.9.0 CONNECT E-216: the last tackled player gets up before the cut (budget 3.1 s over the whistle window)
            if (!_quit && Stage != null && Sim != null)
            {
                float extra = 0f;
                while (!_quit && extra < 3.1f && Stage.PostPlayBusy)
                {
                    if (Paused) { PauseFrame(); yield return null; continue; }
                    float dtx = Mathf.Min(Time.deltaTime, 0.05f); extra += dtx;
                    sim.Tick(dtx, default(ControlInput), -1); Stage.Present(dtx); HandleEvents(); PresentFrame(dtx, 0f, 0f);
                    if (Broadcast != null) Broadcast.OnPlayFrame(dtx, false);
                    yield return null;
                }
            }
            yield return Fade(1f, 0.1f);      // v0.9.0 CONNECT E-270 (was 0.22 s)
            Stage.End(); Sim = null;
            yield return null;
            _fadeTarget = 0f;
        }

        // ---------------------------------------------------------------------------------------- penalties
        IEnumerator PenaltyFlow(PlaySimulation sim, PenaltyChoice c, Action<PenaltyDecision> done)
        {
            Audio.Whistle(); _penaltyWhistled = true;   // v0.9.0 CONNECT E-383: this is the play's whistle; React() must not blow a second one
            bool userDecides = Flow.UserDecides(c) && !Autopilot && Ui != null;
            int yards = Math.Abs(c.Yards);
            var data = new PenaltyPanelData
            {
                Headline = c.Def.Name,
                Team = Flow.TeamOf(c.CommittedBy).Nickname.ToUpperInvariant(),
                Player = Flow.PenaltyNumber(c.Call) > 0 ? "#" + Flow.PenaltyNumber(c.Call) : "",
                Yards = yards + (yards == 1 ? " YARD" : " YARDS") + (c.LossOfDown ? "  -  LOSS OF DOWN" : (c.AutomaticFirstDown ? "  -  AUTOMATIC FIRST DOWN" : "")),
                Referee = Flow.RefereeText(c, true),
                AcceptTitle = "ACCEPT", DeclineTitle = "DECLINE",
                AcceptText = Flow.OutcomeText(c.Accept) + (yards > 0 ? "\n" + (c.Yards < 0 ? "BALL MOVES BACK " : "BALL MOVES UP ") + yards : ""),
                DeclineText = Flow.DeclineSummary(c),
                UserDecides = userDecides, Recommended = c.RecommendAccept, Selected = c.RecommendAccept ? 0 : 1, DeadBall = c.DeadBall, Seconds = 20f
            };
            if (Ui != null) Ui.SetScore(MakeScore(false, -1f, 0f, Flow.ChoiceHeadline(c)));
            Officials?.Signal(OfficialSignal.Penalty, c.Call.CommittedBy == CoreTeam.Home);
            bool accept = c.RecommendAccept;
            if (Ui != null && !Autopilot)
            {
                bool decided = false;
                PenaltyOpen = true; PenaltyData = data;
                Ui.ShowPenalty(data, a => { accept = a; decided = true; });
                float t = 0f;
                _prevLT = _prevRT = true;
                while (!decided && !_quit)
                {
                    float dt = Time.unscaledDeltaTime;
                    if (Paused) { PauseFrame(); yield return null; continue; }
                    t += dt;
                    if (userDecides)
                    {
                        var inp = MatchInput.Instance.Poll(dt);
                        if (inp.Menu) { SetPaused(true); yield return null; continue; }
                        if (inp.NavX != 0) data.Selected = inp.NavX < 0 ? 0 : 1;
                        // X / B only move the highlight (accept / decline); A confirms, so a reflex B at a destructive prompt decides nothing (QA-796)
                        if (inp.Down[(int)Btn.X]) data.Selected = 0;
                        else if (inp.Down[(int)Btn.B]) data.Selected = 1;
                        else if (inp.Down[(int)Btn.A]) { accept = data.Selected == 0; decided = true; }
                        data.Seconds = Mathf.Max(0f, 20f - t);
                        if (data.Seconds <= 0f) { accept = c.RecommendAccept; decided = true; }
                    }
                    else if (t > 3.0f) decided = true;
                    Ui.RefreshPenalty(data);
                    sim.Tick(dt, default(ControlInput), -1); Stage.Present(dt); PresentFrame(dt, 0f, 0f);
                    yield return null;
                }
                Ui.HidePenalty(); PenaltyOpen = false;
            }
            else if (Autopilot) yield return new WaitForSeconds(0.4f);
            if (userDecides && !Autopilot) { }
            Color col = UiColorsLite.Yellow;
            string refText = Flow.RefereeText(c, accept);
            Banner(accept ? "PENALTY ACCEPTED" : "PENALTY DECLINED", col, 2.6f, refText);   // the banner shrinks long announcements to fit instead of cutting them mid-word
            if (Ui != null) Ui.SetScore(MakeScore(false, -1f, 0f, (accept ? "ACCEPTED: " : "DECLINED: ") + c.Def.Name.ToUpperInvariant()));
            Note("penalty " + c.Def.Name + (accept ? " accepted" : " declined") + (userDecides ? " (user)" : " (cpu)"));
            done(accept ? PenaltyDecision.Accept : PenaltyDecision.Decline);
            float w = 0f;
            while (w < 1.6f && !_quit) { if (!Paused) w += Time.deltaTime; sim.Tick(Time.deltaTime, default(ControlInput), -1); Stage.Present(Time.deltaTime); PresentFrame(Time.deltaTime, 0f, 0f); yield return null; }
        }
        int _fadeToken;
        IEnumerator Fade(float to, float seconds)
        {
            // QA-734: a newer fade supersedes an older one still running (two coroutines no longer fight over the black cut)
            int tok = ++_fadeToken;
            float from = _fade; float t = 0f;
            while (t < seconds && tok == _fadeToken) { t += Time.unscaledDeltaTime; _fade = Mathf.Lerp(from, to, t / seconds); if (Ui != null) Ui.SetFade(_fade); yield return null; }
            if (tok != _fadeToken) yield break;
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
            string text; string sub = Flow.Abbr(CoreTeam.Away) + " " + s.AwayScore + "  -  " + Flow.Abbr(CoreTeam.Home) + " " + s.HomeScore;   // away first, like the scorebug
            if (pc.Halftime) text = "HALFTIME";
            else if (pc.From == 4) text = "OVERTIME";
            else text = "END OF " + Ordinal2(pc.From) + " QUARTER";
            Audio.Whistle();
            Banner(text, UiColorsLite.Blue, pc.Halftime ? 3.8f : 2.6f, sub);
            float w = 0f; float dur = pc.Halftime ? 3.8f : 2.6f;
            if (Sim != null) { }
            while (w < dur && !_quit) { if (!Paused) w += Time.deltaTime; if (Ui != null) Ui.SetScore(MakeScore(false)); yield return null; }
            if (pc.Halftime) { Note("halftime"); if (Broadcast != null && !_quit) yield return Broadcast.Halftime(); }
            Note("period change to Q" + s.Quarter);
            if (!afterPlay) { }
        }

        static string Ordinal2(int q) { return q == 1 ? "1ST" : q == 2 ? "2ND" : q == 3 ? "3RD" : "4TH"; }

        IEnumerator FinalScreen()
        {
            Phase = MatchPhase.Final;
            Finished = true;
            PostCareerResult();   // v0.8: a franchise game posts its result before any postgame show
            if (_fade > 0.01f) yield return Fade(0f, 0.3f);
            var s = Flow.State;
            Note("final " + Flow.Summary());
            Audio.Whistle(); Audio.Cheer(1f);
            if (Broadcast != null)
            {
                yield return Broadcast.Postgame();
                if (Broadcast.PostgameShown)
                {
                    // the postgame show ends with the return to the menu: nothing else to present
                    if (!_quit) { _quit = true; if (!Autopilot) GameSession.QuitToMenu(); }
                    yield break;
                }
            }
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
            if (!p) MatchInput.SwallowInput(2);
            if (Ui != null) Ui.SetPaused(p);
        }

        void PauseFrame()
        {
            if (Autopilot || InputLocked) return;
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
            // QA-180: an autopilot game is a CPU-vs-CPU simulation, not a blind stick: unless a scripted test move is forced, the simulation AI plays the user's side too
            if (Autopilot && AutoMove == null) return ci;
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
            bool qbHasBall = scrim && side == SimSide.Offense && UserAgent == 0 && a.HasBall && !sim.PassIsThrown;
            // LT: protect the ball for a carrier; for the quarterback it holds the route view instead (no slower tuck)
            ci.Move = move; ci.Sprint = inp.Sprint; ci.Protect = (inp.LT && !qbHasBall) || inp.LookHeldDown;
            if (qbHasBall && inp.LT) _routeHeld = true;
            bool stick = st.magnitude > 0.12f;

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

        /// <summary>How long a receiver button has to be held before the pass goes as a bullet (seconds, unscaled): a tap is shorter.</summary>
        public const float BulletHoldSeconds = 0.17f;
        /// <summary>Distance (yards) from the quarterback beyond which a tapped pass is a lob instead of a touch pass.</summary>
        public const float TapLobDistance = 19f;

        /// <summary>The pass style of a receiver button: held past the threshold = bullet; a tap = touch (soft, accurate) to a close receiver, lob (high arc over the defense) to a deep one.</summary>
        public static ThrowStyle ThrowStyleFor(float heldSeconds, float distanceYards)
        {
            return heldSeconds >= BulletHoldSeconds ? ThrowStyle.Bullet : (distanceYards >= TapLobDistance ? ThrowStyle.Lob : ThrowStyle.Touch);
        }

        float ReceiverDistance(int receiverAgent)
        {
            if (Sim == null || receiverAgent < 0 || receiverAgent >= 22) return 0f;
            return (Sim.Agents[receiverAgent].Pos - Sim.Agents[0].Pos).Length;
        }

        void BuildThrow(MatchInputFrame inp, ref ControlInput ci)
        {
            for (int i = 0; i < 6; i++)
            {
                int slot = _iconSlot[i];
                if (slot <= 0) continue;
                // v0.7 (note 16): HOLD the receiver's button = bullet (it leaves the moment the hold passes a tiny threshold), TAP = lob / touch (a short toss to a close man, a high ball to a deep one)
                if (inp.Up[i] || (inp.Held[i] && inp.HeldTime[i] >= BulletHoldSeconds))
                {
                    if (_fired[i]) continue;
                    _fired[i] = true;
                    float held = inp.HeldTime[i];
                    ci.ThrowToSlot = slot;
                    ci.ThrowStyle = ThrowStyleFor(held, ReceiverDistance(slot));
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
        /// <summary>A collision from the contact director: camera shake for everybody, rumble when the user's own player is in it.</summary>
        void OnContactImpact(ContactImpact c)
        {
            bool user = !Config.CpuVsCpu && UserAgent >= 0 && (c.AgentA == UserAgent || c.AgentB == UserAgent);
            if (Feel != null) Feel.OnImpact(c, user);
            if (Broadcast != null) Broadcast.OnImpact(c);
        }

        void HandleEvents()
        {
            foreach (var e in Stage.Fresh)
            {
                Audio.OnEvent(e, _frame);
                if (Broadcast != null) Broadcast.OnPlayEvent(e);
                if (Feel != null && e.Type == SimEventType.Catch && UserAgent >= 0 && e.AgentA == UserAgent) Feel.Rumble(0.15f, 0.35f, 0.12f);   // the user hauls one in
                if (SimEvent != null) SimEvent(e);
            }
        }

        bool _penaltyWhistled;   // v0.9.0 CONNECT E-383
        void React(PlayOutcome o)
        {
            if (!_penaltyWhistled) Audio.Whistle();   // v0.9.0 CONNECT E-383: one whistle per play (the penalty flag already blew it)
            _penaltyWhistled = false;
            if (PlayEndReasonShown != null && o.EndReason != null) PlayEndReasonShown(o.EndReason, o);
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
            if (o.Text.Length > 0 && o.Headline != Headline.Kickoff && o.Headline != Headline.Punt && o.Headline != Headline.Penalty)
            {
                string sub = "";
                if (o.Headline == Headline.FirstDown) sub = Ordinal(Flow.State.Down) + " & " + (Flow.State.IsGoalToGo ? "GOAL" : Flow.State.Distance.ToString());
                else if (o.ScoringTeam.HasValue) sub = Flow.Abbr(CoreTeam.Away) + " " + Flow.State.AwayScore + "  -  " + Flow.Abbr(CoreTeam.Home) + " " + Flow.State.HomeScore;   // away first, like the scorebug
                if (sub.Length == 0) sub = EndReasonLine(o, o.Text);   // v0.7: the reason is always on screen (sack: who, incomplete: why, turnover: who recovered)
                Banner(o.Text, col, secs, sub);
            }
            else if (o.Text.Length == 0 && !(o.PenaltyCall != null && o.PenaltyAccepted))
            {
                // an ordinary run or catch has no headline: say how it ended (note 3, "play ended with no tackle / no obvious reason")
                string why = EndReasonLine(o, "");
                if (why.Length > 0) Banner(why, UiColorsLite.Blue, 1.7f, o.LogText);
            }
            if (Officials != null)
            {
                if (o.Headline == Headline.Touchdown) Officials.Signal(OfficialSignal.Touchdown, o.ScoringTeam == CoreTeam.Home);
                else if (o.Headline == Headline.FirstDown) Officials.Signal(OfficialSignal.FirstDown);
                else if (o.Headline == Headline.Incomplete) Officials.Signal(OfficialSignal.Incomplete);
                else if (o.Headline == Headline.Safety) Officials.Signal(OfficialSignal.Safety);
                else if (o.Headline == Headline.FieldGoal || o.Headline == Headline.ExtraPoint) Officials.CoachCheer(o.ScoringTeam == CoreTeam.Home ? 0 : 1);
                else if (o.Headline != Headline.Kickoff && o.Headline != Headline.Penalty) Officials.Signal(OfficialSignal.DeadBall);   // v0.9 (GFX-427): every other whistle gets the dead-ball signal and the whistle arm
            }
            if (o.PenaltyCall != null && !o.PenaltyAccepted) Banner("NO PENALTY", UiColorsLite.Blue, 1.4f, "");
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
            if (!Config.CpuVsCpu) Cams.SetUserView(BuildUserView());
        }

        /// <summary>v0.6 user camera input: behind the user's offense (looking downfield) or behind the user's defense (looking at the offense); see <see cref="UserCameraMath"/>.</summary>
        UserCameraInput BuildUserView() { return UserCameraView.Build(Sim, _frame, UserSide()); }

        void PresentFrame(float dt, float liveT, float preT)
        {
            ApplyCameraTarget(false);
            // ring marker under the controlled player
            bool showRing = !Config.CpuVsCpu && UserAgent >= 0 && Stage.Views[UserAgent] != null;
            Markers.SetRing(showRing ? Stage.Views[UserAgent].transform : null);
            UpdateUserMarker(showRing && Cams.Mode != CameraMode.FirstPerson);   // v0.7 note 1: a marker over the controlled player that never hides behind bodies (offense and defense)
            UpdateRouteView();
            // v0.6: gold stars under the eligible receivers while the user's offense lines up (All-Pro 2K8); they disappear at the snap
            bool wantStars = !Config.CpuVsCpu && _userIsOffense && Sim.Kind == PlayKind.Scrimmage && Sim.Phase == PlayPhase.PreSnap && PixSettings.PreSnapStars;
            if (wantStars != _starsOn || (wantStars && _starPlay != PlayNumber))
            {
                _starsOn = wantStars; _starPlay = PlayNumber; _starList.Clear();
                if (wantStars) for (int i = 1; i < 11; i++) if (Sim.Agents[i].IsReceiverEligible && Stage.Views[i] != null) _starList.Add(Stage.Views[i].transform);
                Markers.SetStars(wantStars ? _starList : null);
            }
            // lines: the line of scrimmage disappears once the ball is snapped (the first-down line stays until the play ends)
            var st = Flow.State;
            bool scrim = st.Phase == GamePhase.Scrimmage;
            if (Phase != MatchPhase.PostPlay) Markers.SetLines(_frame, st.BallSpot, st.BallSpot + st.Distance, scrim && Sim.Phase == PlayPhase.PreSnap, scrim && !st.IsGoalToGo);
            UpdateIcons();
            UpdateKickHud(dt);
            if (Officials != null) Officials.Tick(dt, Sim, Sim.Phase == PlayPhase.Live);
            if (Ui != null) Ui.SetCameraLabel(Cams.Mode == CameraMode.Broadcast ? "BROADCAST" : Cams.Mode == CameraMode.Behind ? PixSettings.CameraStyleNames[(int)PixSettings.UserCameraStyle].ToUpperInvariant() + " CAM" : "FIRST PERSON");
            if (!Config.CpuVsCpu && Sim.Phase == PlayPhase.Live) SetLiveHint();
            if (Ui != null && Broadcast != null) Ui.SetReplayAvailable(Broadcast.HasReplay);
            UpdateFirstPerson();
        }

        /// <summary>First-person polish: minimal HUD and the look-to-ball assist (ball in the air or the carrier in view; offense and defense).</summary>
        void UpdateFirstPerson()
        {
            bool fp = Cams.Mode == CameraMode.FirstPerson && !Cams.Overridden && !Config.CpuVsCpu;
            if (Ui != null) Ui.SetHudMinimal(fp);
            if (!fp || Sim == null || UserAgent < 0) { Cams.SetLookAssist(false, Vector3.zero, 0f); return; }
            var ball = Sim.Ball;
            bool userHasBall = Sim.Agents[UserAgent].HasBall;
            if (userHasBall && !(UserAgent == 0 && Sim.Kind == PlayKind.Scrimmage && !Sim.PassIsThrown)) { Cams.SetLookAssist(false, Vector3.zero, 0f); return; }   // the carrier looks where he runs
            if (UserAgent == 0 && Sim.Kind == PlayKind.Scrimmage && !Sim.PassIsThrown && Sim.Phase == PlayPhase.Live) { Cams.SetLookAssist(false, Vector3.zero, 0f); return; }   // the quarterback reads downfield
            Vector3 p; float weight;
            if (ball.State == BallState.Pass || ball.State == BallState.Kick || ball.State == BallState.Pitch) { p = Stage.Ball.Position; weight = 0.9f; }
            else if (ball.Holder >= 0 && ball.Holder != UserAgent) { p = Stage.WorldPos(ball.Holder) + Vector3.up * 1.2f; weight = Sim.Phase == PlayPhase.Live ? 0.55f : 0.3f; }
            else { p = Stage.Ball.Position; weight = 0.4f; }
            Cams.SetLookAssist(true, p, weight);
        }

        /// <summary>The play was rebuilt in the pre-snap (flip, audible, motion): tell the broadcast layer so the replay recorder does not keep the old formation (QA-735).</summary>
        void PlayRebuilt() { if (Broadcast != null) Broadcast.OnPlayPrepared(Sim, _frame); }

        void SetLiveHint()
        {
            if (UserAgent < 0) { SetHint(""); return; }
            var a = Sim.Agents[UserAgent];
            SimSide side = UserSide();
            if (UserAgent == 0 && a.HasBall && !Sim.PassIsThrown && Sim.Kind == PlayKind.Scrimmage) SetHint("[A][B][X][Y][LB][RB] THROW (HOLD = BULLET, TAP = LOB)   [RS] LEAD   [LT] ROUTES   [LS] SCRAMBLE");
            else if (a.HasBall) SetHint("[RT] SPRINT   [A] HURDLE   [B] SPIN   [X] DIVE   [Y] TRUCK   [LB][RB] STIFF-ARM   [RS] JUKE   [LT] PROTECT");
            else if (side == SimSide.Defense) SetHint("[A] TACKLE   [Y] JUMP / SWAT   [X] DIVE   [B] SWITCH PLAYER   [RT] SPRINT");
            else if (Sim.Kind == PlayKind.Kickoff && side == SimSide.Offense) SetHint("[LS] MOVE   [RT] SPRINT   [A] JUMP");
            else SetHint("[A] JUMP   [RT] SPRINT");
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
        ScoreSnapshot MakeScore(bool live, float playClock = -1f, float preT = 0f, string flag = "")
        {
            var s = Flow.State;
            var sc = new ScoreSnapshot
            {
                HomeAbbr = Flow.Abbr(CoreTeam.Home), AwayAbbr = Flow.Abbr(CoreTeam.Away),
                HomeCrest = "crest_" + HomeInfo.Id, AwayCrest = "crest_" + AwayInfo.Id,
                HomeColor = HomeInfo.PrimaryUi, AwayColor = AwayInfo.PrimaryUi,
                HomeScore = s.HomeScore, AwayScore = s.AwayScore,
                Quarter = s.Quarter == 1 ? "1ST" : s.Quarter == 2 ? "2ND" : s.Quarter == 3 ? "3RD" : s.Quarter == 4 ? "4TH" : (s.Quarter == 5 ? "OT" : (s.Quarter - 4) + "OT"),
                PossessionHome = s.Possession == CoreTeam.Home,
                HomeTimeouts = s.HomeTimeouts, AwayTimeouts = s.AwayTimeouts, InPlay = live, FlagText = flag
            };
            float clock = s.ClockSeconds;
            if (live && Sim != null && Sim.Phase == PlayPhase.Live && Sim.Kind != PlayKind.Kickoff) clock = Mathf.Max(0f, clock - Sim.Time);
            else if (!live && Phase == MatchPhase.PreSnap && _runoff0 > 0f) clock = Mathf.Max(0f, clock - _runoff0 * Mathf.Clamp01(preT / 5f));
            int ci = Mathf.CeilToInt(clock);
            sc.Clock = s.IsCollegeOvertime ? "" : (ci / 60) + ":" + (ci % 60).ToString("00");
            sc.TwoMinute = (s.Quarter == 2 || s.Quarter == 4) && clock <= 120f && !s.IsCollegeOvertime && Config.QuarterMinutes > 2;   // orange = the last two minutes; in a 2-minute quarter that would be the whole quarter (QA-272)
            sc.PlayClock = playClock >= 0f ? Mathf.CeilToInt(playClock) : -1;
            if (s.Phase == GamePhase.Kickoff) { sc.DownDistance = "KICKOFF"; sc.Spot = ""; }
            else if (s.Phase == GamePhase.PointAfter) { sc.DownDistance = s.RequiresTwoPoint ? "2-PT TRY" : "PAT"; sc.Spot = ""; }
            else { sc.DownDistance = Ordinal(s.Down) + " & " + (s.IsGoalToGo ? "GOAL" : s.Distance.ToString()); sc.Spot = SpotText(); }   // standard notation ("BUF 30"), the label says what it is
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
            if (a.HasBall && AutoMove != null && a.Pos.Y > sim.LosY + 3f)
            {
                if (Time.time > _autoNextMove)
                {
                    float nd = float.MaxValue;
                    for (int i = 0; i < 22; i++) { var o = sim.Agents[i]; if (o.Side == a.Side || o.IsDown) continue; float dd = (o.Pos - a.Pos).Length; if (dd < nd) nd = dd; }
                    float need = 2.8f;
                    if (nd < need)
                    {
                        _autoNextMove = Time.time + 1.6f;
                        switch (AutoMove)
                        {
                            case "Truck": f.Down[(int)Btn.Y] = true; break;
                            case "Spin": f.Down[(int)Btn.B] = true; break;
                            case "StiffArm": f.Down[(int)Btn.RB] = true; break;
                            case "Hurdle": f.Down[(int)Btn.A] = true; break;
                            case "JukeLeft": f.LookFlickLeft = true; f.Look = new Vector2(-1f, 0f); break;
                            case "JukeRight": f.LookFlickRight = true; f.Look = new Vector2(1f, 0f); break;
                        }
                    }
                }
            }
            else
            if (AutoMove == null && a.HasBall && Time.time > _autoNextMove) { _autoNextMove = Time.time + 1.4f; if (_autoRng.Next(2) == 0) { f.LookFlickLeft = true; f.Look = new Vector2(-1f, 0f); } else { f.LookFlickRight = true; f.Look = new Vector2(1f, 0f); } }
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









