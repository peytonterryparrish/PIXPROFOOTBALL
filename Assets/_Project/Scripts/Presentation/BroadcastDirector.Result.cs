using PixFootball.Gameplay;
using PixFootball.League;

namespace PixFootball.Presentation
{
    /// <summary>v0.8 Franchise: copies the live box score into the result of a franchise game (see <see cref="IBroadcastResult"/>).</summary>
    public sealed partial class BroadcastDirector : IBroadcastResult
    {
        public void FillResult(GameResult r)
        {
            EnsureTracker();
            if (_trk == null || r == null) return;
            // the match supplies ids, scores and the points by quarter; the tracker supplies the rest of the box score, the player lines, the drives and the scoring plays
            var hb = _trk.HomeBox; var ab = _trk.AwayBox;
            hb.TeamId = r.HomeTeamId; hb.Score = r.Home.Score; hb.ByQuarter = r.Home.ByQuarter; hb.OvertimeScore = r.Home.OvertimeScore;
            ab.TeamId = r.AwayTeamId; ab.Score = r.Away.Score; ab.ByQuarter = r.Away.ByQuarter; ab.OvertimeScore = r.Away.OvertimeScore;
            r.Home = hb; r.Away = ab;
            r.PlayerLines.Clear();
            foreach (var kv in _trk.Lines)
            {
                var l = kv.Value;
                l.TeamId = _trk.Home.GetPlayer(l.PlayerId) != null ? _trk.Home.Id : _trk.Away.Id;   // the roster decides (a traded player may still carry his old team id)
                l.Stats.Games = 1;
                l.Stats.GamesStarted = l.Started ? 1 : 0;
                r.PlayerLines.Add(l);
            }
            r.Drives.AddRange(_trk.Drives);
            r.Scoring.AddRange(_trk.Scoring);
        }
    }
}
