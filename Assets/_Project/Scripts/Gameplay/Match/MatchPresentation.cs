using System;
using System.Collections.Generic;
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
    }

    public struct PlayCallItem
    {
        public string Label, Tag;
        public Btn Button;
    }

    public sealed class PlayCallData
    {
        public string Title = "", Subtitle = "";
        public bool Offense = true;
        public List<PlayCallItem> Items = new List<PlayCallItem>();
        public float Seconds = 20f;
        public string TeamName = "";
    }

    public struct ReceiverIcon
    {
        public Vector3 World;
        public Btn Button;
        public string Label;
        public bool Highlight;
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

    /// <summary>
    /// What the MatchDirector needs from the UI layer (implemented by PixFootball.UI.MatchHud; the Gameplay assembly cannot reference UI).
    /// All interactive screens report back through callbacks; the director waits on them from its coroutine.
    /// </summary>
    public interface IMatchPresentation
    {
        void Build(MatchDirector director);
        void SetScore(ScoreSnapshot s);
        void ShowBanner(string text, Color color, float seconds, string sub);
        void ShowToast(string text, float seconds);
        void ShowCoinTossCall(string userTeam, string cpuTeam, Action<bool> onCallHeads);
        void ShowCoinTossResult(bool resultHeads, bool userWon, string winnerName, Action<bool> onReceive);
        void ShowPlayCall(PlayCallData data, Action<int> onPick);
        void HidePlayCall();
        void ShowFinal(FinalData data, Action onContinue);
        void SetReceiverIcons(List<ReceiverIcon> icons, Camera cam);
        void SetHint(string hint);
        void SetFade(float alpha);
        void SetPaused(bool paused);
        void SetCameraLabel(string label);
        bool PauseOpen { get; }
        /// <summary>Raised by the pause menu.</summary>
        event Action ResumeRequested, QuitRequested, CameraCycleRequested, LightingToggleRequested;
    }
}
