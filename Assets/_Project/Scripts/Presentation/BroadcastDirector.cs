using System;
using System.Collections;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;
using CoreTeam = PixFootball.Core.Team;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.Presentation
{
    /// <summary>
    /// The v0.5 "Broadcast" layer for a match: records every play for instant replay, directs the cameras 2K5 style (establishing shots, post-play cuts, celebrations, reactions), draws the TV
    /// overlays, runs the automatic and manual replays, the halftime and postgame shows, makes the crowd and the sidelines react, and feeds the play-by-play text to the ticker
    /// (<see cref="ICommentaryHook"/>). The <see cref="MatchDirector"/> only talks to it through <see cref="IBroadcast"/>.
    /// </summary>
    public sealed partial class BroadcastDirector : MonoBehaviour, IBroadcast
    {
        public static BroadcastDirector Instance { get; private set; }

        MatchDirector _dir;
        ReplayRecorder _rec; ReplayPlayer _player; ReplayHud _hud; BroadcastCamera _cam; TvOverlays _ov; ReplayController _rc;
        ReplayLibrary _lib = new ReplayLibrary(); GameTracker _trk; CrowdReaction _crowd; FirstPersonFx _fp;
        readonly CommentaryBus _bus = new CommentaryBus();
        bool _attached;

        public ReplayLibrary Library { get { return _lib; } }
        public GameTracker Tracker { get { return _trk; } }
        public TvOverlays Overlays { get { return _ov; } }
        public ReplayController Replay { get { return _rc; } }
        public ReplayPlayer ReplayActors { get { return _player; } }
        public ReplayRecorder Recorder { get { return _rec; } }
        public CrowdReaction Crowd { get { return _crowd; } }
        public FirstPersonFx FirstPerson { get { return _fp; } }
        public BroadcastCamera Cam { get { return _cam; } }
        public CommentaryBus Commentary { get { return _bus; } }
        public MatchDirector Director { get { return _dir; } }
        public int AutoReplaysPlayed { get; private set; }
        public int ShotsPlayed { get; private set; }
        public string LastShot { get; private set; } = "";
        public static int MaxAutoReplaysWhenAutopilot = 5;

        bool _introActive, _showActive;
        public bool Busy { get { return (_rc != null && _rc.Active) || _introActive || _showActive; } }
        public bool HasReplay { get { return _lib.Count > 0; } }
        public bool PostgameShown { get; private set; }

        // ---- per play state
        PlaySimulation _sim; PlayFrame _frame;
        string _situation = ""; int _quarterBefore; string _clockBefore = "";
        float _impactMax; bool _bigHit; float _bigHitTime = -1f; int _bigHitAgent = -1;
        float _postTime; bool _pendingClip; ReplayClip _lastClip; bool _wantAuto;
        PlayOutcome _lastOutcome; PlayFacts _lastFacts; PlayDigest _lastDigest;
        int _driveIndex; int _lastOffense = -1; bool _lastWasScore; int _lastQuarterSeen = 1; bool _firstPlay = true;
        int _playsSinceCrowd, _playsSinceCompare, _playsSinceCard;
        bool _twoMinHalf1, _twoMinHalf2;

        // ============================================================================================ bootstrap
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            // a named handler, removed before it is added: with Enter Play Mode options (no domain reload) a session no longer stacks one more handler per run (QA-874)
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var d = FindAnyObjectByType<MatchDirector>();
            if (d != null && d.GetComponent<BroadcastDirector>() == null && FindAnyObjectByType<BroadcastDirector>() == null) d.gameObject.AddComponent<BroadcastDirector>();
        }

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Attach(MatchDirector d)
        {
            if (_attached) return;
            _attached = true;
            _dir = d;
            _rec = gameObject.AddComponent<ReplayRecorder>(); _rec.Attach(d);
            _lib.Dropped = _rec.Recycle;   // trimmed clips give their frames back to the recorder pool (QA-858)
            _player = new GameObject("ReplayPlayer").AddComponent<ReplayPlayer>(); _player.transform.SetParent(transform, false);
            _player.Warm(8, 8, 9);
            _cam = BroadcastCamera.Create(transform, d.MainCamera, d.Cams);
            _hud = ReplayHud.Create(transform);
            _ov = TvOverlays.Create(transform, d);
            _crowd = gameObject.AddComponent<CrowdReaction>(); _crowd.Build();
            _fp = FirstPersonFx.Create(transform, d);
            _rc = gameObject.AddComponent<ReplayController>();
            _rc.Attach(d, _player, _hud, _cam, _ov, _lib);
            _rc.TitleOf = c => c.Title;
            _bus.Register(new TickerHook(this));
            if (d.Ui != null) d.Ui.SetReplayAvailable(false);
        }

        // ============================================================================================ helpers
        MatchFlow Flow { get { return _dir.Flow; } }
        GameState State { get { return _dir.Flow.State; } }

        TeamVis VisOf(CoreTeam t)
        {
            var info = t == CoreTeam.Home ? _dir.HomeInfo : _dir.AwayInfo;
            return new TeamVis(Flow.Abbr(t), info.Name, "crest_" + info.Id, info.PrimaryUi);
        }

        TeamVis VisOfIdx(int idx) { return VisOf(idx == 0 ? CoreTeam.Home : CoreTeam.Away); }

        void EnsureTracker()
        {
            if (_trk != null || _dir.Flow == null) return;
            _trk = new GameTracker(Flow.Home, Flow.Away, Flow.Abbr(CoreTeam.Home), Flow.Abbr(CoreTeam.Away), _dir.Config.QuarterMinutes);
            if (Flow.State != null && Flow.State.Rules != null && Flow.State.Rules.OvertimeMinutes > 0) _trk.OvertimeSeconds = Flow.State.Rules.OvertimeMinutes * 60;   // time of possession in overtime uses the real period length (QA-841)
        }

        Func<int, Player> Lookup(PlaySimulation sim)
        {
            var map = new Dictionary<int, Player>();
            for (int i = 0; i < 22; i++) { var p = sim.Agents[i].Player; if (p != null) map[p.Id] = p; }
            return id => { Player p; return map.TryGetValue(id, out p) ? p : null; };
        }

        public int TeamIdxOf(CoreTeam t) { return t == CoreTeam.Home ? 0 : 1; }

        string SituationText()
        {
            var s = State;
            string q = s.Quarter <= 4 ? "Q" + s.Quarter : "OT";
            return q + "  " + s.ClockDisplay + "  -  " + OverlayText.DownAndDistance(s.Down, s.Distance, s.IsGoalToGo);
        }

        // ============================================================================================ IBroadcast: per play
        public void OnPlayPrepared(PlaySimulation sim, PlayFrame frame)
        {
            if (!_attached) return;
            EnsureTracker();
            AbortShots();
            _sim = sim; _frame = frame;
            _ov.ClearStale();   // the last play's stat strip / ticker / score card do not follow the teams into the next snap (QA-096 / 228 / 236 / 240 / 264)
            _rec.Begin(sim, frame);
            _trk.BeginPlay(State);
            _preShotsScheduled = false; _nextPlayShown = false;
            _impactMax = 0f; _bigHit = false; _bigHitTime = -1f;
            _situation = State.Phase == GamePhase.Scrimmage ? SituationText() : (State.Quarter <= 4 ? "Q" + State.Quarter : "OT") + "  " + State.ClockDisplay;
            _quarterBefore = State.Quarter; _clockBefore = State.ClockDisplay;
            _downBefore = State.Phase == GamePhase.Scrimmage ? State.Down : 0;
            // drive bookkeeping for the camera director
            if (sim.Kind == PlayKind.Scrimmage)
            {
                int off = TeamIdxOf(State.Possession);
                _firstPlayOfDrive = off != _lastOffense;
                if (_firstPlayOfDrive) { _driveIndex++; _lastOffense = off; }
            }
            else _firstPlayOfDrive = false;
            _firstPlayOfHalf = State.Quarter != _lastQuarterSeen && (State.Quarter == 3 || State.Quarter == 1);
            _lastQuarterSeen = State.Quarter;
        }
        bool _firstPlayOfDrive, _firstPlayOfHalf;
        int _downBefore;

        public void OnSnap(PlaySimulation sim)
        {
            if (!_attached) return;
            _rec.MarkSnap();
            AbortShots();
            _ov.ClearStale();
            _ov.Clear(OverlaySlot.Side); _ov.Clear(OverlaySlot.BottomRight); _ov.Clear(OverlaySlot.Center);
            _impactMax = 0f; _bigHit = false; _bigHitTime = -1f;
            _firstPlay = false;
        }

        public void OnPlayEvent(PlayEvent e)
        {
            if (!_attached) return;
            if (e.Type == SimEventType.Touchdown && _sim != null)
            {
                var a = e.AgentA >= 0 && e.AgentA < 22 ? _sim.Agents[e.AgentA] : null;
                if (a != null) CrowdOnTouchdown(a.Side == SimSide.Offense ? State.Possession : State.Possession.Opponent());
            }
        }

        public void OnImpact(ContactImpact c)
        {
            if (!_attached) return;
            _impactMax = Mathf.Max(_impactMax, c.Intensity);
            if (c.Big && c.Intensity >= 0.55f) { _bigHit = true; _bigHitTime = _rec.SnapRelativeTime; _bigHitAgent = c.HasRoles ? c.Victim : (c.AgentB >= 0 ? c.AgentB : c.AgentA); }      // v0.9.0 CONNECT E-348: the replay focuses on the victim of a big hit, whatever the contact kind
        }

        public void OnPlayFrame(float dt, bool live)
        {
            if (!_attached) return;
            if (!live)
            {
                _postTime += dt;
                if (_pendingClip && _postTime >= 1.7f) CommitClip();
            }
            TickShots(dt);
        }

        // ============================================================================================ post play
        public void BeginPostPlay(PlayOutcome o, PlaySimulation sim)
        {
            if (!_attached || o == null) return;
            EnsureTracker();
            _lastOutcome = o; _postTime = 0f; _pendingClip = true; _wantAuto = false;
            var state = State;
            var lookup = Lookup(sim);
            _lastDigest = _trk.RecordPlay(o, sim.Kind, lookup, state);
            _playsSinceCrowd++; _playsSinceCompare++; _playsSinceCard++;

            // ---- commentary (ticker)
            var line = CommentaryWriter.Summarize(o, lookup, _quarterBefore, _clockBefore, _dir.PlayNumber);
            if (!string.IsNullOrEmpty(line.Text)) _bus.Emit(line);

            // ---- facts for the replay score and the director
            var r = o.Result;
            _lastFacts = new PlayFacts
            {
                Headline = o.Headline, Type = r != null ? r.Type : PlayType.Run, Yards = r != null ? r.Yards : 0, ReturnYards = r != null ? r.ReturnYards : 0,
                Touchdown = r != null && r.Touchdown, Turnover = r != null && r.Turnover, Safety = r != null && r.Safety,
                OffenseIsHome = o.Offense == CoreTeam.Home, Quarter = _quarterBefore, ClockSeconds = state.ClockSeconds,
                ScoreMargin = o.Offense == CoreTeam.Home ? state.HomeScore - state.AwayScore : state.AwayScore - state.HomeScore,
                Impact = _impactMax, BigHit = _bigHit, ThirdOrFourthDownConversion = _downBefore >= 3 && o.Headline == Headline.FirstDown, Overtime = state.Quarter > 4
            };
            float score = HighlightScorer.Score(_lastFacts);
            _lastFacts.Impact = _impactMax;
            _lastScore = score;
            bool autoCap = _dir.Autopilot && AutoReplaysPlayed >= MaxAutoReplaysWhenAutopilot;
            _wantAuto = PixSettings.AutoReplay && HighlightScorer.WantsAutoReplay(score, PixSettings.Cinematics) && !autoCap && sim.Kind != PlayKind.Kickoff
                        || (PixSettings.AutoReplay && PixSettings.Cinematics < 2 && !autoCap && o.Headline == Headline.Touchdown);

            // ---- crowd, sidelines, players
            ReactToOutcome(o, sim, score);

            // ---- overlays
            ShowPostPlayOverlays(o, sim, state);

            // ---- camera shots (during the wind-down)
            QueuePostPlayShots(o, sim, score);
        }

        float _lastScore;

        void CommitClip()
        {
            if (!_pendingClip) return;
            _pendingClip = false;
            var clip = _rec.Finish();
            if (clip == null) { _lastClip = null; return; }
            clip.Title = HighlightScorer.Title(_lastFacts);
            clip.Situation = _situation;
            clip.Quarter = _quarterBefore; clip.Clock = _clockBefore;
            clip.Excitement = _lastScore;
            clip.Possession = _lastFacts.OffenseIsHome ? 0 : 1; clip.OffenseIsHome = _lastFacts.OffenseIsHome;
            DetermineKey(clip);
            _lib.Add(clip);
            _lastClip = clip;
            if (_dir.Ui != null) _dir.Ui.SetReplayAvailable(true);
        }

        /// <summary>Where the moment of interest is in the clip: the touchdown, the interception, the sack, the big hit or the last tackle, and who to look at.</summary>
        void DetermineKey(ReplayClip clip)
        {
            clip.KeyAgent = 0; clip.KeyTime = clip.EndTime - 0.8f;
            if (_sim == null) return;
            var ev = _sim.Log.Events;
            PlayEvent? td = null, inter = null, sack = null, tackle = null, catchE = null, fum = null;
            for (int i = 0; i < ev.Count; i++)
            {
                var e = ev[i];
                switch (e.Type)
                {
                    case SimEventType.Touchdown: td = e; break;
                    case SimEventType.Interception: inter = e; break;
                    case SimEventType.Sack: sack = e; break;
                    case SimEventType.Tackle: if (e.AgentA >= 0 && e.AgentB >= 0) tackle = e; break;
                    case SimEventType.Catch: catchE = e; break;
                    case SimEventType.Fumble: fum = e; break;
                }
            }
            if (td.HasValue) { clip.KeyTime = td.Value.Time; clip.KeyAgent = Mathf.Max(0, td.Value.AgentA); clip.Tags.Add("td"); }
            else if (inter.HasValue) { clip.KeyTime = inter.Value.Time; clip.KeyAgent = Mathf.Max(0, inter.Value.AgentA); clip.Tags.Add("turnover"); }
            else if (sack.HasValue) { clip.KeyTime = sack.Value.Time; clip.KeyAgent = Mathf.Max(0, sack.Value.AgentB >= 0 ? sack.Value.AgentB : sack.Value.AgentA); clip.SecondAgent = sack.Value.AgentA; clip.Tags.Add("sack"); }
            else if (fum.HasValue) { clip.KeyTime = fum.Value.Time; clip.KeyAgent = Mathf.Max(0, fum.Value.AgentA); clip.Tags.Add("turnover"); }
            else if (_bigHit && _bigHitTime > 0f) { clip.KeyTime = _bigHitTime; clip.KeyAgent = Mathf.Max(0, _bigHitAgent); clip.Tags.Add("bighit"); }
            else if (tackle.HasValue) { clip.KeyTime = tackle.Value.Time; clip.KeyAgent = tackle.Value.AgentB; clip.SecondAgent = tackle.Value.AgentA; }
            else if (catchE.HasValue) { clip.KeyTime = catchE.Value.Time; clip.KeyAgent = Mathf.Max(0, catchE.Value.AgentA); }
            clip.KeyTime = Mathf.Clamp(clip.KeyTime, clip.StartTime + 0.5f, clip.EndTime);
            if (clip.KeyAgent < 0 || clip.KeyAgent > 21) clip.KeyAgent = 0;
        }

        public IEnumerator FinishPostPlay()
        {
            if (!_attached) yield break;
            // let the post-play shots finish (the wind-down is already over)
            float w = 0f;
            // v0.9.0 CONNECT E-380: the plan is trimmed to the wind now, so this only mops up a straggler (was up to 3.2 s of dead wait)
            while ((_activeShot != null || _queue.Count > 0) && w < 0.8f) { w += Time.unscaledDeltaTime; TickShots(Time.unscaledDeltaTime); yield return null; }
            AbortShots();
            CommitClip();
            if (_lastClip != null && _wantAuto && !_rc.Active)
            {
                AutoReplaysPlayed++;
                yield return _rc.PlayAuto(_lastClip, _lastClip.Title, true);
            }
            _wantAuto = false;
            // drive summary after the replay (shown on the next pre-snap when the play-call screen closes)
            if (_lastDigest != null && _lastDigest.EndedDrive != null && OverlayText.WorthSummary(_lastDigest.EndedDrive) && PixSettings.TvOverlays)
            {
                var d = _lastDigest.EndedDrive; int idx = _lastDigest.EndedDriveTeamIdx;
                string start = "STARTED AT " + SpotText(d.StartSpot, TeamOf(idx), TeamOf(1 - idx)) + "  -  Q" + d.Quarter;
                _ov.ShowDriveSummary(d, VisOfIdx(idx), start);
                _playsSinceCard = 0;
            }
            _lastDigest = null;
        }

        CoreTeam TeamOf(int idx) { return idx == 0 ? CoreTeam.Home : CoreTeam.Away; }

        string SpotText(int spot, CoreTeam own, CoreTeam opp)
        {
            if (spot == 50) return "MIDFIELD";
            return spot < 50 ? Flow.Abbr(own) + " " + spot : Flow.Abbr(opp) + " " + (100 - spot);
        }

        // ============================================================================================ overlays after a play
        void ShowPostPlayOverlays(PlayOutcome o, PlaySimulation sim, GameState state)
        {
            if (!PixSettings.TvOverlays) return;
            var r = o.Result;
            // player stat lower-third for a big play
            if (_lastDigest != null && _lastDigest.FeaturedPlayerId > 0 && _lastScore >= HighlightScorer.FullThreshold)
            {
                var p = _trk.PlayerById(_lastDigest.FeaturedPlayerId); var line = _trk.Line(_lastDigest.FeaturedPlayerId);
                if (p != null && line != null) _ov.ShowPlayerStat(p, line.Stats, VisOfIdx(_trk.TeamIdxOfPlayer(p)), TagFor(o));
                // a quarterback who threw the long touchdown gets his line too
                if (r != null && r.Type == PlayType.Pass && r.Touchdown) { var q = _trk.PlayerById(r.PasserId); var ql = q != null ? _trk.Line(q.Id) : null; if (q != null && ql != null) _ov.ShowPlayerStat(q, ql.Stats, VisOfIdx(_trk.TeamIdxOfPlayer(q)), "TD PASS"); }
            }
            // penalties
            if (o.Headline == Headline.Penalty && o.PenaltyCall != null)
            {
                var pc = o.PenaltyCall;
                _ov.ShowPenalty("PENALTY - " + pc.Def.Name.ToUpperInvariant(), Flow.Abbr(pc.CommittedBy) + (Flow.PenaltyNumber(pc) > 0 ? " #" + Flow.PenaltyNumber(pc) : "") + "   " + (o.PenaltyYards >= 0 ? o.PenaltyYards : Math.Abs(pc.Def.ProYards)) + ((o.PenaltyYards >= 0 ? o.PenaltyYards : Math.Abs(pc.Def.ProYards)) == 1 ? " YARD" : " YARDS"), VisOf(pc.CommittedBy));
            }
            // scoring cards
            if (o.ScoringTeam.HasValue && o.Headline != Headline.Penalty)
                _ov.ShowScoreUpdate(VisOf(CoreTeam.Away), state.AwayScore, VisOf(CoreTeam.Home), state.HomeScore, o.Text, state.ClockDisplay, 5.5f, o.ScoringTeam.Value == CoreTeam.Away ? 0 : 1);
            // two-minute warning
            bool twoMin = (state.Quarter == 2 || state.Quarter == 4) && state.ClockSeconds <= 120f && state.ClockSeconds > 0f && _dir.Config.QuarterMinutes > 2;   // a 2-minute quarter is two minutes long: no warning card
            if (twoMin && !(state.Quarter == 2 ? _twoMinHalf1 : _twoMinHalf2))
            {
                if (state.Quarter == 2) _twoMinHalf1 = true; else _twoMinHalf2 = true;
                _ov.ShowTwoMinute(state.Quarter == 2 ? "2ND QUARTER" : "4TH QUARTER");
            }
        }

        string TagFor(PlayOutcome o)
        {
            switch (o.Headline)
            {
                case Headline.Touchdown: return "TOUCHDOWN";
                case Headline.Interception: return "INTERCEPTION";
                case Headline.Sack: return "SACK";
                case Headline.FumbleLost: return "TAKEAWAY";
                default: return o.Result != null && o.Headline != Headline.Incomplete && o.Result.Yards >= 20 ? "BIG PLAY" : "";   // an incompletion is never a big play (QA-141)
            }
        }

        /// <summary>The ticker: one summary line of play-by-play per play (a lower-priority card than the stat lines).</summary>
        sealed class TickerHook : ICommentaryHook
        {
            readonly BroadcastDirector _b;
            public TickerHook(BroadcastDirector b) { _b = b; }
            public void OnCommentary(CommentaryLine line)
            {
                if (!line.PlaySummary || line.Priority < 0.2f || !PixSettings.TvOverlays) return;
                var o = _b._lastOutcome;
                var vis = o != null ? _b.VisOf(o.Offense) : _b.VisOf(CoreTeam.Home);
                _b._ov.ShowTicker(line.Text, vis, line.Clock, 3.4f);
            }
        }

        // ============================================================================================ manual replay
        public void RequestManualReplay()
        {
            if (!_attached || Busy) return;
            StartCoroutine(ManualRoutine());
        }

        IEnumerator ManualRoutine()
        {
            CommitClip();
            var clip = _lib.Latest;
            if (clip == null) { _dir.Ui?.ShowToast("NO REPLAY YET", 2f); yield break; }
            _dir.Ui?.SetPaused(false);   // hide the pause menu; the game stays paused (time scale 0), the replay runs on unscaled time
            yield return _rc.PlayManual(clip, clip.Title);
            _dir.Ui?.SetPaused(true);
        }
    }
}
