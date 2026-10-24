using System;
using System.Collections.Generic;
using PixFootball.Core;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>Everything the scorebug shows.</summary>
    public sealed class ScoreSnapshot
    {
        public string HomeAbbr = "HOM", AwayAbbr = "AWY", HomeCrest = "", AwayCrest = "";
        public Color HomeColor = Color.blue, AwayColor = Color.red;
        public int HomeScore, AwayScore;
        public string Quarter = "1ST", Clock = "5:00";
        public int PlayClock = -1;
        public string DownDistance = "", Spot = "";
        public bool PossessionHome;
        public int HomeTimeouts = 3, AwayTimeouts = 3;
        public bool ClockRunning;
        public bool TwoMinute;
        public bool InPlay;
        /// <summary>Flag text shown in the yellow strip under the scorebug (penalty announcements).</summary>
        public string FlagText = "";
    }

    public struct ReceiverIcon
    {
        public Vector3 World;
        public Btn Button;
        public string Label;
        public bool Highlight;
        /// <summary>Show an arrow instead of a button glyph (motion candidates).</summary>
        public bool Arrow;
    }

    public sealed class FinalData
    {
        public string HomeName = "", AwayName = "", HomeAbbr = "", AwayAbbr = "", HomeCrest = "", AwayCrest = "";
        public Color HomeColor, AwayColor;
        public int HomeScore, AwayScore;
        public int[] HomeByQuarter = new int[8], AwayByQuarter = new int[8];
        public TeamStats HomeStats, AwayStats;
        public string Headline = "FINAL";
        public bool UserWon, Tie;
    }

    /// <summary>One "glyph + label" pair in the bottom prompt bar.</summary>
    public struct HintPair
    {
        public string Glyph, Text;
        public HintPair(string glyph, string text) { Glyph = glyph; Text = text; }
    }

    public struct MenuRow
    {
        public Btn Button;
        public string Label, Tag;
        public bool Selected, Dim;
        /// <summary>v0.7: the play behind the row (the audible list shows its play art, personnel and concept like the call sheet).</summary>
        public Play Play;
    }

    public struct WheelOption
    {
        /// <summary>Compass slot 0-7: 0 up, 1 up-right, 2 right, 3 down-right, 4 down, 5 down-left, 6 left, 7 up-left (screen frame).</summary>
        public int Slot;
        public string Label;
        public bool Selected;
    }

    /// <summary>What the pre-snap overlay shows: prompt bar, active adjustments, the audible menu, the hot-route wheel and the receiver icons.</summary>
    public sealed class PreSnapHud
    {
        public string Mode = "";
        public string PlayName = "", Formation = "";
        public List<HintPair> Hints = new List<HintPair>();
        public List<string> Adjustments = new List<string>();
        public string MenuTitle = "";
        public List<MenuRow> Menu = new List<MenuRow>();
        public string WheelTitle = "";
        public List<WheelOption> Wheel = new List<WheelOption>();
        public List<ReceiverIcon> Icons = new List<ReceiverIcon>();
        public bool OffenseSide = true;
        public int Seconds = -1;
        /// <summary>v0.7: the playbook the menu rows' plays are drawn from.</summary>
        public Playbook Book;
    }

    /// <summary>The penalty accept / decline panel.</summary>
    public sealed class PenaltyPanelData
    {
        public string Headline = "", Referee = "";
        public string Team = "", Player = "", Yards = "", Rule = "";
        public string AcceptTitle = "ACCEPT", AcceptText = "", DeclineTitle = "DECLINE", DeclineText = "";
        public bool UserDecides = true, Recommended = true;
        /// <summary>0 = accept highlighted, 1 = decline highlighted.</summary>
        public int Selected;
        public bool DeadBall;
        public Color Accent = new Color(1f, 0.918f, 0f);
        public float Seconds;
    }

    /// <summary>
    /// What the MatchDirector needs from the UI layer (implemented by PixFootball.UI.MatchHud; the Gameplay assembly cannot reference UI).
    /// The director polls the input and owns every model; the UI only draws what it is handed.
    /// </summary>
    public interface IMatchPresentation
    {
        void Build(MatchDirector director);
        void SetScore(ScoreSnapshot s);
        void ShowBanner(string text, Color color, float seconds, string sub);
        void ShowToast(string text, float seconds);
        void ShowCoinTossCall(string userTeam, string cpuTeam, Action<bool> onCallHeads);
        void ShowCoinTossResult(bool resultHeads, bool userWon, string winnerName, Action<bool> onReceive);
        /// <summary>On-field coin toss: true makes the toss modal transparent (small title on top, buttons in the lower third) so the field scene shows; false (default) is the full-screen modal.</summary>
        void SetTossBackdrop(bool onField);
        /// <summary>The caption strip along the bottom of the screen during the toss ("Rollers choose tails."); empty hides it.</summary>
        void SetTossCaption(string text);
        /// <summary>Shows (or refreshes) the full play-call screen for the model; <paramref name="playClock"/> is the seconds left.</summary>
        void ShowPlayCall(PlayCallModel model, float playClock);
        void HidePlayCall();
        void SetPreSnap(PreSnapHud hud);
        void ShowPenalty(PenaltyPanelData data, Action<bool> onDecision);
        void RefreshPenalty(PenaltyPanelData data);
        void HidePenalty();
        void ShowFinal(FinalData data, Action onContinue);
        void SetReceiverIcons(List<ReceiverIcon> icons, Camera cam);
        /// <summary>v0.7: read numbers / RUN tags at the ends of the routes drawn on the turf while LT is held (null hides them).</summary>
        void SetRouteBadges(IList<RouteOverlay.Badge> badges, Camera cam);
        /// <summary>v0.7: the marker above the player the user controls (always visible, on offense and defense); <paramref name="label"/> is "#54 LB" style text.</summary>
        void SetUserMarker(bool visible, Vector3 world, Camera cam, string label);
        /// <summary>v0.7: the small whistle banner after a play: the reason it ended ("DOWN BY CONTACT  -  #54", "OUT OF BOUNDS", "INCOMPLETE").</summary>
        void ShowWhistle(string text, float seconds);
        void SetHint(string hint);
        void SetFade(float alpha);
        void SetPaused(bool paused);
        void SetCameraLabel(string label);
        bool PauseOpen { get; }
        /// <summary>Raised by the pause menu.</summary>
        event Action ResumeRequested, QuitRequested, CameraCycleRequested, LightingToggleRequested;
        /// <summary>v0.5: the pause menu's "Instant Replay" entry.</summary>
        event Action ReplayRequested;
        /// <summary>v0.5: a replay owns the screen: the hint bar, receiver icons and flag strip step aside.</summary>
        void SetReplayMode(bool on);
        /// <summary>v0.5: first-person camera: the scorebug shrinks and the hint bar goes quiet.</summary>
        void SetHudMinimal(bool minimal);
        /// <summary>v0.5: hides (or shows) the whole in-game HUD canvas (the halftime and postgame shows draw their own score strip).</summary>
        void SetHudVisible(bool visible);
        /// <summary>v0.11 watch mode: the user hid the whole HUD (the Y button); the broadcast layer's own SetHudVisible calls cannot bring it back.</summary>
        void SetHudUserHidden(bool hidden);
        /// <summary>v0.11 watch mode: a CPU versus CPU game is on, the helper bar shows the watch controls instead of controller hints.</summary>
        void SetWatchMode(bool on);
        /// <summary>v0.5: whether the pause menu offers Instant Replay (a play has been recorded).</summary>
        void SetReplayAvailable(bool available);
        /// <summary>v0.7: wind indicator (top right). <paramref name="degreesFromNorth"/> is the heading the wind blows toward, clockwise from the compass N; 0 mph = calm.</summary>
        void SetWind(float degreesFromNorth, float mph);
        /// <summary>v0.7: kick meter (bottom centre). timing01 and aim01 are marker positions along the gauges (0.5 = centre / perfect); the labels are the verdict text ("Perfect", "Good", "Early", "Late", "Centered", "Left", "Right").</summary>
        void SetKickMeter(bool visible, float timing01, string timingLabel, float aim01, string aimLabel);
        /// <summary>The root RectTransform of the match HUD canvas (the TV overlays are built next to it).</summary>
        UnityEngine.Transform HudRoot { get; }
    }
}
