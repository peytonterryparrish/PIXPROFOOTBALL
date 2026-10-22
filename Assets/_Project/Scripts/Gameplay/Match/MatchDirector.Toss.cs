using UnityEngine;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Gameplay
{
    /// <summary>v0.6: the coin toss happens on the field (All-Pro 2K8 style): the referee at midfield, two captains per team, a close camera and a caption strip; the call / receive buttons are drawn over it.</summary>
    public sealed partial class MatchDirector
    {
        bool _tossScene;
        Vector3 _tossCenter = new Vector3(0f, 0f, 0f);

        void BeginTossScene()
        {
            if (Ui == null || Config.CpuVsCpu || Autopilot || Stage == null) return;
            _tossScene = true;
            Stage.BeginToss(_homeKit, _awayKit, _tossCenter);
            if (Officials != null) Officials.PlaceForToss(_tossCenter, 180f);
            // the Close camera frames the referee and the four captains from the sideline, low and wide
            Cams.CloseUp(_tossCenter, new Vector3(1.4f, 1.55f, -8.2f));
            Ui.SetTossBackdrop(true);
            TossCaption("COIN TOSS");
            StartCoroutine(TossTicker());
        }

        System.Collections.IEnumerator TossTicker()
        {
            while (_tossScene) { if (Officials != null) Officials.TossTick(Time.unscaledDeltaTime); yield return null; }
        }

        void TossCaption(string text) { if (_tossScene && Ui != null) Ui.SetTossCaption(text); }

        void EndTossScene()
        {
            if (!_tossScene) return;
            _tossScene = false;
            if (Ui != null) { Ui.SetTossCaption(""); Ui.SetTossBackdrop(false); }
            if (Officials != null) Officials.EndToss();
            if (Stage != null) Stage.EndToss();
            Cams.CloseUp(null, Vector3.zero);
            Cams.Cut();
        }
    }
}
