using System.Collections.Generic;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.UI.Franchise;
using PixFootball.UI.Glass;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PixFootball.UI
{
    /// <summary>v0.8 Franchise: the CAREER tile of the hub opens the career start flow (continue, new, load) and then the franchise shell; a game played from the franchise returns straight to it.</summary>
    public partial class MainMenuController
    {
        CareerStartPanel _careerStart;
        FranchiseUi _franchise;

        /// <summary>The line under the CAREER tile: the latest save, or what the mode is.</summary>
        string CareerTileSubtitle()
        {
            try
            {
                var m = CareerSession.Latest();
                if (m != null) return m.TeamName + " - " + m.Phase;
            }
            catch (System.Exception e) { Debug.LogWarning("[PIX] career tile: " + e.Message); }
            return "Build a dynasty: 32 teams, endless seasons";
        }

        void BuildCareerUi(RectTransform root)
        {
            _careerStart = CareerStartPanel.Build(root);
            _careerStart.Started = OpenFranchise;
            _careerStart.Back = CloseCareerStart;
            _careerStart.Section = GlassSection;
            _careerStart.Footer = items => { if (_footer != null) _footer.SetItems(items); };
            _careerStart.gameObject.SetActive(false);
        }

        /// <summary>The hub's CAREER tile.</summary>
        void OpenCareerStart()
        {
            RememberOpener();
            _menuRoot.SetActive(false); _careerStart.gameObject.SetActive(true);
            _careerStart.Show(0);
            StartCoroutine(SelectFirst(_careerStart.First != null ? _careerStart.First.gameObject : null));
        }

        void CloseCareerStart()
        {
            GlassSection("MAIN MENU");
            _careerStart.gameObject.SetActive(false); _menuRoot.SetActive(true); SetFooter(false);
            StartCoroutine(SelectFirst(ReturnTarget(_firstButton != null ? _firstButton.gameObject : null)));
        }

        /// <summary>Autotest (-pixAutotestCareer): the franchise shell that is open, and a way to open one for a session that was started from code.</summary>
        public FranchiseUi AutotestFranchise { get { return _franchise; } }
        public void AutotestOpenFranchise(FranchiseController ctl) { OpenFranchise(ctl); }

        void OpenFranchise(FranchiseController ctl)
        {
            if (ctl == null) return;
            if (_franchise != null) { _franchise.Close(); Destroy(_franchise.gameObject); _franchise = null; }
            _menuRoot.SetActive(false); _careerStart.gameObject.SetActive(false);
            var host = new FranchiseHost
            {
                Exit = CloseFranchise,
                Section = GlassSection,
                Footer = items => { if (_footer != null) _footer.SetItems(items); },
                Leave = StartWithFade
            };
            _franchise = FranchiseUi.Build((RectTransform)_canvasRoot, ctl, host);
            if (_stageView != null) _stageView.gameObject.SetActive(false);   // the dense screens need no menu player behind them
            _franchise.Open();
        }

        void CloseFranchise()
        {
            if (_franchise != null) { _franchise.Close(); Destroy(_franchise.gameObject); _franchise = null; }
            CareerSession.Close();
            if (_stageView != null) _stageView.gameObject.SetActive(StageInMenu);
            GlassSection("MAIN MENU");
            _menuRoot.SetActive(true); SetFooter(false);
            RefreshCareerTile();
            StartCoroutine(SelectFirst(_firstButton != null ? _firstButton.gameObject : null));
        }

        void RefreshCareerTile()
        {
            // rebuilds the hub so the CAREER tile shows the newest save
            if (_hub == null) return;
            int page = _hub.Page;
            Destroy(_hub.Root.gameObject);
            BuildHub((RectTransform)_canvasRoot, page);
        }

        /// <summary>After a franchise game the menu opens the franchise again (the save is still in the session).</summary>
        void ReopenFranchiseIfReturning()
        {
            if (!CareerSession.ReturnToFranchise || !CareerSession.Active) { CareerSession.ReturnToFranchise = false; return; }
            OpenFranchise(FranchiseController.FromSession());
        }
    }
}
