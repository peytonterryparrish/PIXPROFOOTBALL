using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Gameplay;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.13.0 COVERAGE: clip-usage report of the watched game. <see cref="ClipUsage"/> records every clip any system starts; this writes <c>clips_played.csv</c>
    /// (clip name, asset path, system, plays, seconds, first play number) and the "distinct clips played" lines of summary.txt.
    /// </summary>
    public sealed partial class MatchWatchRunner
    {
        void ClipsHook()
        {
            ClipUsage.Reset(); ClipUsage.Enabled = true;
            _dir.PlayStarted += sim => ClipUsage.Marker = _dir.PlayNumber;
        }

        string ClipsReport()
        {
            var rows = ClipUsage.Rows.Where(e => e.Clip != null).ToList();
            var sb = new StringBuilder("clip,asset_path,system,plays,seconds,first_play\n");
            var path = new Dictionary<AnimationClip, string>();
            foreach (var e in rows.OrderBy(r => r.Clip.name).ThenBy(r => r.System))
            {
                string p; var id = e.Clip;
                if (!path.TryGetValue(id, out p)) path[id] = p = AssetDatabase.GetAssetPath(e.Clip);
                sb.Append(Csv(e.Clip.name)).Append(',').Append(Csv(string.IsNullOrEmpty(p) ? "(runtime)" : p)).Append(',').Append(Csv(e.System)).Append(',')
                  .Append(e.Plays).Append(',').Append(e.Seconds.ToString("0.00", CI)).Append(',').Append(e.FirstPlay).Append('\n');
            }
            File.WriteAllText(_out + "/clips_played.csv", sb.ToString());
            var distinct = new HashSet<string>(rows.Select(e => (path[e.Clip]) + "|" + e.Clip.name));
            var played = new HashSet<string>(rows.Where(e => e.Plays > 0 || e.Seconds >= 0.5f).Select(e => (path[e.Clip]) + "|" + e.Clip.name));
            var rep = new StringBuilder();
            rep.AppendLine("== clips played (ClipUsage; clips_played.csv) ==");
            rep.AppendLine("distinct clips played: " + distinct.Count + " (" + played.Count + " with a play or >= 0.5 s on screen), recorded rows " + rows.Count + ", plays " + rows.Sum(e => e.Plays) + ", seconds " + rows.Sum(e => e.Seconds).ToString("0", CI));
            foreach (var g in rows.GroupBy(e => e.System).OrderByDescending(g => g.Select(e => e.Clip.name).Distinct().Count()))
                rep.AppendLine("  " + g.Key + " : " + g.Select(e => e.Clip.name).Distinct().Count() + " clips, " + g.Sum(e => e.Plays) + " plays, " + g.Sum(e => e.Seconds).ToString("0", CI) + " s");
            return rep.ToString();
        }


        static string Csv(string s) { return s != null && (s.Contains(",") || s.Contains("\"")) ? "\"" + s.Replace("\"", "\"\"") + "\"" : (s ?? ""); }
    }
}
