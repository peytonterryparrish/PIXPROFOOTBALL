using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    /// <summary>
    /// Five audible slots per offensive formation (A, B, Y, LB, RB at the line). Defaults pick a balanced mix from the formation's plays;
    /// slots are editable (<see cref="Set"/>) so a later "edit audibles" screen only has to call that.
    /// </summary>
    public sealed class AudibleBook
    {
        public const int SlotCount = 5;
        public static readonly string[] SlotNames = { "A", "B", "Y", "LB", "RB" };

        readonly Playbook _book;
        readonly Dictionary<string, string[]> _custom = new Dictionary<string, string[]>();

        public AudibleBook(Playbook book) { _book = book; }

        /// <summary>The play a slot calls (null when none), for the formation of the current play. Never the play already called.</summary>
        public Play Get(Play current, int slot)
        {
            IList<Play> all = For(current);
            return slot >= 0 && slot < all.Count ? all[slot] : null;
        }

        public void Set(string formationName, int slot, string playId)
        {
            // QA-537: ignore a bad slot, and a play that belongs to another formation (it would change the formation mid pre-snap)
            if (slot < 0 || slot >= SlotCount) return;
            if (playId != null)
            {
                Play target = _book.FindPlay(playId);
                if (target == null || target.FormationName != formationName) return;
            }
            string[] ids;
            if (!_custom.TryGetValue(formationName, out ids)) { ids = new string[SlotCount]; _custom[formationName] = ids; }
            ids[slot] = playId;
        }

        /// <summary>The five audible plays for the formation of <paramref name="current"/> (entries may be null in a tiny formation).</summary>
        public IList<Play> For(Play current)
        {
            string fn = current.FormationName;
            var result = new Play[SlotCount];
            string[] custom; _custom.TryGetValue(fn, out custom);
            List<Play> plays = _book.PlaysFor(fn);
            plays.RemoveAll(p => p.Category == PlayCategory.Kneel || p.Category == PlayCategory.Spike || p.Name == "Hail Mary");
            // QA-536: the defaults keep the direction of the call (a Left run never audibles to a Right one)
            if (current.Name.EndsWith(" Left")) plays.RemoveAll(p => p.Name.EndsWith(" Right"));
            else if (current.Name.EndsWith(" Right")) plays.RemoveAll(p => p.Name.EndsWith(" Left"));
            for (int i = 0; i < SlotCount; i++)
                if (custom != null && custom[i] != null) result[i] = _book.FindPlay(custom[i]);
            // defaults: inside run, outside run, quick pass, intermediate pass, deep shot
            PlayCategory[][] prefs =
            {
                new[] { PlayCategory.InsideRun, PlayCategory.Draw },
                new[] { PlayCategory.OutsideRun, PlayCategory.PlayAction },
                new[] { PlayCategory.ShortPass, PlayCategory.Screen },
                new[] { PlayCategory.MediumPass, PlayCategory.PlayAction },
                new[] { PlayCategory.DeepPass, PlayCategory.MediumPass }
            };
            var used = new HashSet<string> { current.Id };
            foreach (Play r in result) if (r != null) used.Add(r.Id);
            string[][] favs = { new[] { "Inside Zone Right", "Dive Right" }, new[] { "Outside Zone Right", "Toss Sweep Right", "Draw Right" }, new[] { "Slants", "Quick Hitches", "Stick" }, new[] { "Y-Cross", "Curl-Flat", "Smash", "Flood" }, new[] { "Four Verticals", "Post-Wheel", "Fade" } };
            for (int i = 0; i < SlotCount; i++)
            {
                if (result[i] != null) continue;
                Play pick = null;
                foreach (string favName in favs[i])
                {
                    // QA-536: a Left run audibles to the Left variants (the favourites are written Right)
                    string name = current.Name.EndsWith(" Left") && favName.EndsWith(" Right") ? favName.Substring(0, favName.Length - 6) + " Left" : favName;
                    foreach (Play p in plays) if (p.Name == name && !used.Contains(p.Id)) { pick = p; break; }
                    if (pick != null) break;
                }
                if (pick == null)
                    foreach (PlayCategory cat in prefs[i])
                    {
                        foreach (Play p in plays) if (p.Category == cat && !used.Contains(p.Id)) { pick = p; break; }
                        if (pick != null) break;
                    }
                if (pick == null) foreach (Play p in plays) if (!used.Contains(p.Id)) { pick = p; break; }
                result[i] = pick;
                if (pick != null) used.Add(pick.Id);
            }
            // keep a flipped play flipped: the audible runs to the same side the play was set to
            if (current.Mirrored) for (int i = 0; i < SlotCount; i++) if (result[i] != null && !result[i].Mirrored) result[i] = PlayEditing.Mirror(result[i]);
            return result;
        }
    }

    /// <summary>Coverage and alignment adjustments a defensive player can make at the line (v0.3).</summary>
    public enum LineShift { Normal, Pinch, Spread }
    public enum LinebackerShift { Normal, In, Out }
    public enum BlitzShow { None, Show, BackOff }
    public enum PressMode { Auto, Press, Off }

    public sealed class DefenseAdjust
    {
        public CoverageScheme Coverage = CoverageScheme.None;   // None = keep the call
        public PressMode Press = PressMode.Auto;
        public LineShift Line = LineShift.Normal;
        public LinebackerShift Backers = LinebackerShift.Normal;
        public BlitzShow Blitz = BlitzShow.None;

        public bool IsDefault { get { return Coverage == CoverageScheme.None && Press == PressMode.Auto && Line == LineShift.Normal && Backers == LinebackerShift.Normal && Blitz == BlitzShow.None; } }
        public DefenseAdjust Clone() { return (DefenseAdjust)MemberwiseClone(); }

        public static readonly CoverageScheme[] CoverageChoices = { CoverageScheme.Cover2, CoverageScheme.Cover3, CoverageScheme.Cover1, CoverageScheme.Cover0, CoverageScheme.Cover4 };

        public static string CoverageName(CoverageScheme s)
        {
            switch (s)
            {
                case CoverageScheme.Cover0: return "COVER 0"; case CoverageScheme.Cover1: return "COVER 1"; case CoverageScheme.Cover2: return "COVER 2";
                case CoverageScheme.Cover3: return "COVER 3"; case CoverageScheme.Cover4: return "COVER 4"; default: return s.ToString().ToUpperInvariant();
            }
        }

        /// <summary>The playbook play of the same formation that runs the requested shell (zone / man / blitz variants preferred in that order).</summary>
        public static Play FindCoverage(Playbook book, Play current, CoverageScheme scheme)
        {
            Play best = null; int bestRank = int.MaxValue;
            foreach (Play p in book.PlaysFor(current.FormationName))
            {
                if (p.Scheme != scheme || p.Side != PlaySide.Defense) continue;
                int rank = p.Category == PlayCategory.ZoneCoverage || p.Category == PlayCategory.ManCoverage ? 0 : (p.Category == PlayCategory.Blitz ? 1 : 2);
                if (scheme == CoverageScheme.Cover0) rank = p.Category == PlayCategory.Blitz ? 0 : 1;
                if (rank < bestRank) { best = p; bestRank = rank; }
            }
            return best;
        }
    }
}
