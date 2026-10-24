using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.11.0 SIM-FLOW: the runner who takes it to the sideline. Decided once, from the situation, when he is within a few yards of the boundary (never random frame to frame):
    ///  - the clock matters (two-minute drill, trailing late in a half): he runs out of bounds to stop it, unless the lane downfield is wide open;
    ///  - he is squeezed (a tackler inside 3.4 yd closing, no open lane): about one runner in three steps out instead of taking the hit (NFL: roughly 7 plays per game end out of bounds).
    /// The decision bends his heading toward the line; the existing boundary check ends the play and the choreographer / view play the out-of-bounds finish.
    /// </summary>
    internal sealed partial class CarrierAI
    {
        public static bool SidelineScenes = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoSidelineScenes") < 0;
        private readonly Dictionary<int, int> _outState = new Dictionary<int, int>();      // 0 undecided, 1 going out, -1 stays in
        private readonly Dictionary<int, float> _outCheck = new Dictionary<int, float>();

        internal Vec2 SceneSideline(SimAgent c, Vec2 dir)
        {
            if (!SidelineScenes || S.Kind != PlayKind.Scrimmage || c.Side != SimSide.Offense || S.BallSide != SimSide.Offense || c.Move != CarrierMove.None || S.EndPending) return dir;
            if (c.Slot == 0 && !S.QbHasCrossedLos) return dir;
            float toLine = Math.Min(c.Pos.X, FieldGeometry.Width - c.Pos.X);
            if (toLine > 11.5f) return dir;
            float sgn = c.Pos.X < FieldGeometry.CenterX ? -1f : 1f;
            int st; _outState.TryGetValue(c.Id, out st);
            if (st == 0 || st == -1)
            {
                float nextAt; _outCheck.TryGetValue(c.Id, out nextAt);
                if (S.Time < nextAt) return dir;
                _outCheck[c.Id] = S.Time + 0.25f;
                bool hurry = PlayCaller.IsHurryUp(S.State);
                float near = 99f;
                for (int i = 11; i < 22; i++) { SimAgent d = S.Agents[i]; if (d.State == AgentState.Down || d.Body.FallenTime > 0.35f) continue; float dd = (d.Pos - c.Pos).Length; if (dd < near) near = dd; }
                bool squeezed = near < 3.4f && c.PathIdxOpen < 4.5f;
                bool gained = c.Pos.Y > S.LosY + 1.0f;
                uint h = (uint)(((c.Id * 73) ^ ((int)(S.BallX0 * 11f)) ^ ((int)(S.LosY * 5f)) ^ (S.State.Quarter * 131)) & 0x7fffffff);
                bool go = false;
                if (hurry && gained && toLine < 11f && c.PathIdxOpen < 12f) go = h % 100 < 90;
                else if (squeezed && gained && toLine < 4.2f) go = h % 100 < 38;
                if (go) { _outState[c.Id] = 1; st = 1; }
                else if (st == 0) _outState[c.Id] = -1;
            }
            if (st != 1) return dir;
            float dirY = c.Side == SimSide.Offense ? 1f : -1f;
            Vec2 want = new Vec2(sgn * 0.92f, dirY * 0.38f);
            Vec2 mixed = dir * 0.2f + want.Norm() * 0.8f;
            return mixed.Norm();
        }
    }
}
