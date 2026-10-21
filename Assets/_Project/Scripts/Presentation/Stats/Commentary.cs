using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.Sim;

namespace PixFootball.Presentation
{
    public enum CommentaryKind { Info, Snap, Pass, Catch, Run, Tackle, Sack, Interception, Fumble, Touchdown, FieldGoal, Penalty, FirstDown, BigHit, Punt, Kickoff, Incomplete, Timeout, Period }

    /// <summary>One line of play-by-play text. v0.5 has no commentary audio: the lines feed the lower-third ticker, and a voice layer can subscribe to the same bus later.</summary>
    public struct CommentaryLine
    {
        public CommentaryKind Kind;
        public string Text;
        /// <summary>0..1: how much the line matters (a touchdown is 1, a routine run 0.2).</summary>
        public float Priority;
        public int Quarter;
        public string Clock;
        public int PlayNumber;
        /// <summary>True for the line that sums up a finished play (the ticker shows these), false for live events during the play.</summary>
        public bool PlaySummary;
    }

    /// <summary>Implemented by anything that wants the play-by-play text (the ticker now; a voice-over later).</summary>
    public interface ICommentaryHook
    {
        void OnCommentary(CommentaryLine line);
    }

    /// <summary>Fan-out of commentary lines to the registered hooks plus a short history.</summary>
    public sealed class CommentaryBus
    {
        readonly List<ICommentaryHook> _hooks = new List<ICommentaryHook>();
        readonly List<CommentaryLine> _history = new List<CommentaryLine>();
        public IReadOnlyList<CommentaryLine> History { get { return _history; } }
        public int HistoryLimit = 40;

        public void Register(ICommentaryHook h) { if (h != null && !_hooks.Contains(h)) _hooks.Add(h); }
        public void Unregister(ICommentaryHook h) { _hooks.Remove(h); }

        public void Emit(CommentaryLine line)
        {
            _history.Add(line);
            if (_history.Count > HistoryLimit) _history.RemoveAt(0);
            for (int i = 0; i < _hooks.Count; i++) _hooks[i].OnCommentary(line);
        }
    }

    /// <summary>Turns finished plays and sim events into plain play-by-play text.</summary>
    public static class CommentaryWriter
    {
        static string Last(Player p) { return p == null ? "" : p.Bio.LastName.ToUpperInvariant(); }
        static string Yds(int y) { y = Math.Abs(y); return y + (y == 1 ? " YARD" : " YARDS"); }

        /// <summary>The one-line recap of a finished play: "DOE PASS COMPLETE TO ROE FOR 18 YARDS".</summary>
        public static CommentaryLine Summarize(PlayOutcome o, Func<int, Player> lookup, int quarter, string clock, int playNumber)
        {
            var line = new CommentaryLine { Quarter = quarter, Clock = clock, PlayNumber = playNumber, PlaySummary = true, Kind = CommentaryKind.Info, Priority = 0.2f };
            if (o == null || o.Result == null) return line;
            PlayResult r = o.Result;
            Func<int, Player> get = id => id > 0 && lookup != null ? lookup(id) : null;
            Player passer = get(r.PasserId), rec = get(r.ReceiverId), rusher = get(r.RusherId), tackler = get(r.TacklerId), defender = get(r.DefenderId), kicker = get(r.KickerId);
            string t;
            switch (o.Headline)
            {
                case Headline.Touchdown:
                    line.Kind = CommentaryKind.Touchdown; line.Priority = 1f;
                    if (r.Type == PlayType.Pass) t = Last(passer) + " TO " + Last(rec) + " - " + Yds(r.Yards) + " - TOUCHDOWN";
                    else if (r.Type == PlayType.Run) t = Last(rusher) + " RUNS IT IN FROM " + Yds(r.Yards) + " - TOUCHDOWN";
                    else if (r.Type == PlayType.Interception) t = Last(defender) + " RETURNS THE INTERCEPTION FOR A TOUCHDOWN";
                    else t = "TOUCHDOWN";
                    break;
                case Headline.FieldGoal: line.Kind = CommentaryKind.FieldGoal; line.Priority = 0.7f; t = Last(kicker) + " FIELD GOAL IS GOOD"; break;
                case Headline.FieldGoalMissed: line.Kind = CommentaryKind.FieldGoal; line.Priority = 0.5f; t = Last(kicker) + " FIELD GOAL NO GOOD"; break;
                case Headline.ExtraPoint: line.Kind = CommentaryKind.FieldGoal; line.Priority = 0.4f; t = "EXTRA POINT GOOD"; break;
                case Headline.TwoPointGood: line.Kind = CommentaryKind.Touchdown; line.Priority = 0.6f; t = "TWO-POINT CONVERSION GOOD"; break;
                case Headline.Interception: line.Kind = CommentaryKind.Interception; line.Priority = 0.9f; t = Last(passer) + " INTERCEPTED BY " + Last(defender); break;
                case Headline.FumbleLost: line.Kind = CommentaryKind.Fumble; line.Priority = 0.9f; t = "FUMBLE - " + Last(defender) + " RECOVERS FOR THE DEFENSE"; break;
                case Headline.Sack: line.Kind = CommentaryKind.Sack; line.Priority = 0.7f; t = Last(tackler) + " SACKS " + Last(passer) + " FOR A LOSS OF " + Yds(r.Yards); break;
                case Headline.Incomplete: line.Kind = CommentaryKind.Incomplete; line.Priority = 0.25f; t = Last(passer) + " PASS INCOMPLETE" + (rec != null ? " INTENDED FOR " + Last(rec) : ""); break;
                case Headline.Punt: line.Kind = CommentaryKind.Punt; line.Priority = 0.2f; t = Last(kicker) + " PUNTS " + Yds(r.Yards); break;
                case Headline.Kickoff: line.Kind = CommentaryKind.Kickoff; line.Priority = 0.2f; t = r.Touchback ? "KICKOFF - TOUCHBACK" : "KICKOFF RETURNED " + Yds(r.ReturnYards); break;
                case Headline.Penalty: line.Kind = CommentaryKind.Penalty; line.Priority = 0.5f; t = o.PenaltyName.Length > 0 ? "FLAG - " + Humanize(o.PenaltyName) : "FLAG ON THE PLAY"; break;
                case Headline.Safety: line.Kind = CommentaryKind.Touchdown; line.Priority = 0.9f; t = "SAFETY"; break;
                case Headline.TurnoverOnDowns: line.Kind = CommentaryKind.Info; line.Priority = 0.7f; t = "TURNOVER ON DOWNS"; break;
                default:
                    if (r.Type == PlayType.Pass) { line.Kind = CommentaryKind.Catch; t = Last(passer) + " COMPLETES TO " + Last(rec) + " FOR " + Yds(r.Yards); line.Priority = r.Yards >= 15 ? 0.5f : 0.3f; }
                    else if (r.Type == PlayType.Run) { line.Kind = CommentaryKind.Run; t = Last(rusher) + (r.Yards < 0 ? " LOSES " : " RUNS FOR ") + Yds(r.Yards); line.Priority = r.Yards >= 12 ? 0.5f : 0.25f; }
                    else if (r.Type == PlayType.Kneel) t = "QUARTERBACK KNEELS";
                    else if (r.Type == PlayType.Spike) t = "QUARTERBACK SPIKES THE BALL";
                    else t = "";
                    break;
            }
            if (o.Headline == Headline.FirstDown && t.Length > 0) t += " - FIRST DOWN";
            line.Text = t;
            return line;
        }

        static string Humanize(string pascal)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < pascal.Length; i++) { if (i > 0 && char.IsUpper(pascal[i])) sb.Append(' '); sb.Append(char.ToUpperInvariant(pascal[i])); }
            return sb.ToString();
        }

        /// <summary>A live event inside a play (throw, catch, big hit); null when the event is not worth a line.</summary>
        public static CommentaryLine? Event(PlayEvent e, Func<int, Player> agentPlayer, int quarter, string clock, int playNumber)
        {
            var line = new CommentaryLine { Quarter = quarter, Clock = clock, PlayNumber = playNumber, Priority = 0.3f };
            Player a = e.AgentA >= 0 && agentPlayer != null ? agentPlayer(e.AgentA) : null, b = e.AgentB >= 0 && agentPlayer != null ? agentPlayer(e.AgentB) : null;
            switch (e.Type)
            {
                case SimEventType.Snap: line.Kind = CommentaryKind.Snap; line.Text = "SNAP"; line.Priority = 0.1f; return line;
                case SimEventType.Throw: line.Kind = CommentaryKind.Pass; line.Text = Last(a) + " THROWS"; return line;
                case SimEventType.Catch: line.Kind = CommentaryKind.Catch; line.Text = Last(a) + " MAKES THE CATCH"; return line;
                case SimEventType.Sack: line.Kind = CommentaryKind.Sack; line.Text = "SACK"; line.Priority = 0.6f; return line;
                case SimEventType.Interception: line.Kind = CommentaryKind.Interception; line.Text = "INTERCEPTED BY " + Last(a); line.Priority = 0.8f; return line;
                case SimEventType.Fumble: line.Kind = CommentaryKind.Fumble; line.Text = "FUMBLE"; line.Priority = 0.8f; return line;
                case SimEventType.Touchdown: line.Kind = CommentaryKind.Touchdown; line.Text = "TOUCHDOWN"; line.Priority = 1f; return line;
            }
            return null;
        }
    }
}
