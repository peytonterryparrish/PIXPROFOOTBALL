using PixFootball.League;
using PixFootball.Gameplay;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11.0: only the test career builder (<see cref="MakeCareer"/>) is left. The v0.8 career screen renders had stale tab indexes and were replaced by <c>FranchiseShots</c> / <c>UiWp2Shots</c>.
    /// </summary>
    public static class CareerUiShots
    {
        /// <summary>A career advanced to week 5 of the regular season (some games simulated so the screens have standings, stats and news).</summary>
        public static CareerSave MakeCareer(int seed = 11, int team = 5, int weeks = 4)
        {
            var o = new CareerOptions { Seed = seed, UserTeamId = team };
            var defs = MatchData.Defs; if (defs != null && defs.Count == 32) o.Teams = defs;
            var s = CareerFlow.NewCareer(o);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            for (int i = 0; i < weeks; i++) CareerFlow.Advance(s);
            return s;
        }
    }
}
