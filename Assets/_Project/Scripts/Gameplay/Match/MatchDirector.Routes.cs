using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.7 field aids of the user's player: the route view (hold LT before the snap, and as the quarterback during the play), the always-visible marker over the controlled player and the whistle banner.</summary>
    public sealed partial class MatchDirector
    {
        /// <summary>True while the user holds LT in a state that shows the routes (set by the input handlers, read once per frame by <see cref="UpdateRouteView"/>).</summary>
        bool _routeHeld;
        /// <summary>QA probes: shows the route view without a pad (also under autopilot).</summary>
        public static bool DebugRouteView;
        int _routesBuiltFor = -1;

        /// <summary>The route view is on screen right now (tests and the autotest read it).</summary>
        public bool RouteViewShown { get { return Routes != null && Routes.Shown; } }

        void UpdateRouteView()
        {
            if (Routes == null) return;
            bool qbLive = Sim != null && Sim.Phase == PlayPhase.Live && UserAgent == 0 && Sim.Agents[0].HasBall && !Sim.PassIsThrown;
            bool allowed = Sim != null && !Config.CpuVsCpu && (!Autopilot || DebugRouteView) && _userIsOffense && Sim.Kind == PlayKind.Scrimmage && (Sim.Phase == PlayPhase.PreSnap || qbLive) && !PreSnapMenuOpen;
            bool want = allowed && (_routeHeld || DebugRouteView);
            _routeHeld = false;   // the input handlers set it again next frame while LT stays down
            if (!want)
            {
                if (Routes.Shown) { Routes.SetShown(false); if (Ui != null) Ui.SetRouteBadges(null, MainCamera); }
                return;
            }
            bool keep = Sim.Phase == PlayPhase.Live && _routesBuiltFor == PlayNumber && Routes.Signature != int.MinValue;
            if (!keep && (!Routes.Shown || Routes.Signature != RouteOverlay.SignatureOf(Sim) || _routesBuiltFor != PlayNumber))
            {
                Routes.Rebuild(Sim, _frame, Stage != null ? Stage.OffenseKit.Uniform.Primary : Color.white);
                _routesBuiltFor = PlayNumber;
            }
            Routes.SetShown(true);
            if (Ui != null) Ui.SetRouteBadges(Routes.Badges, MainCamera);
        }

        /// <summary>The audible / hot-route / motion menus own the screen while they are open: no route picture behind them.</summary>
        bool PreSnapMenuOpen { get { return _ps == PsMode.Audible || _ps == PsMode.CoverageMenu; } }

        /// <summary>The throw buttons of the quarterback's targets as the HUD shows them (backs take LB / RB, receivers left to right take X, Y, B, A, RB).</summary>
        void ThrowMap(List<KeyValuePair<Btn, int>> map)
        {
            map.Clear();
            var sim = Sim;
            var backs = new List<int>(); var wides = new List<int>();
            for (int i = 1; i < 11; i++)
            {
                var a = sim.Agents[i]; if (!a.IsReceiverEligible) continue;
                if (a.PlayPos == Position.RB || a.PlayPos == Position.FB) backs.Add(i); else wides.Add(i);
            }
            wides.Sort((p, q) => sim.Agents[p].Pos.X.CompareTo(sim.Agents[q].Pos.X));
            Btn[] wideBtns = { Btn.X, Btn.Y, Btn.B, Btn.A, Btn.RB };
            for (int k = 0; k < wides.Count && k < wideBtns.Length; k++) map.Add(new KeyValuePair<Btn, int>(wideBtns[k], wides[k]));
            for (int k = 0; k < backs.Count; k++)
            {
                Btn b = k == 0 ? Btn.LB : Btn.RB;
                bool taken = false; foreach (var e in map) if (e.Key == b) { taken = true; break; }
                if (!taken) map.Add(new KeyValuePair<Btn, int>(b, backs[k]));
            }
        }

        readonly List<KeyValuePair<Btn, int>> _throwMap = new List<KeyValuePair<Btn, int>>();

        /// <summary>The button icons over the receivers while the route view is up before the snap (the same buttons the pass will use).</summary>
        void AddRouteIcons(List<ReceiverIcon> into)
        {
            if (Sim == null) return;
            ThrowMap(_throwMap);
            foreach (var e in _throwMap) into.Add(IconFor(e.Value, e.Key, false, false));
        }

        // ============================================================================================ marker over the controlled player
        void UpdateUserMarker(bool show)
        {
            if (Ui == null) return;
            if (!show || UserAgent < 0 || Stage.Views[UserAgent] == null) { Ui.SetUserMarker(false, Vector3.zero, MainCamera, ""); return; }
            var a = Sim.Agents[UserAgent];
            int num = a.Player != null && a.Player.Bio != null ? a.Player.Bio.JerseyNumber : 0;
            string label = (num > 0 ? "#" + num + " " : "") + a.PlayPos;
            Ui.SetUserMarker(true, Stage.Views[UserAgent].transform.position + Vector3.up * 2.45f, MainCamera, label);
        }

        // ============================================================================================ whistle
        /// <summary>The small banner that says why the play ended ("TACKLED BY #52", "OUT OF BOUNDS", "INCOMPLETE") the moment the whistle blows (the big result banner follows with the outcome).</summary>
        void ShowWhistle(PlaySimulation sim, PlayOutcome outcome)
        {
            if (Ui == null || sim == null || sim.EndInfo == null) return;
            var e = sim.EndInfo;
            if (string.IsNullOrEmpty(e.Text) || e.Kind == PlayEndKind.None || e.Kind == PlayEndKind.Touchdown) return;
            if (outcome != null && !string.IsNullOrEmpty(outcome.Text) && outcome.Text.IndexOf(e.Text, StringComparison.OrdinalIgnoreCase) >= 0) return;   // the big banner says the same
            string t = e.Text + (string.IsNullOrEmpty(e.Detail) ? "" : "  -  " + e.Detail);
            Ui.ShowWhistle(t, 2.4f);
        }
    }
}
