using System;
using System.Collections.Generic;
using System.Linq;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.EditorTools
{
    public sealed partial class MatchWatchRunner
    {
        // ---------------------------------------------------------------------------------------------------------- audio probe (what plays, when)
        AudioSource[] _audio = new AudioSource[0]; int _audioRefresh;
        readonly Dictionary<AudioSource, string> _audioWas = new Dictionary<AudioSource, string>();
        readonly Dictionary<string, int> _audioStarts = new Dictionary<string, int>();
        readonly Dictionary<string, int> _audioEventStarts = new Dictionary<string, int>();
        string _lastEventType = ""; int _lastEventFrame = -999;

        void NoteEvent(PlayEvent e) { _lastEventType = e.Type.ToString(); _lastEventFrame = _frame; }

        void AudioProbe()
        {
            if (_frame % 2 != 0) return;
            if (_audioRefresh-- <= 0) { _audio = FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None); _audioRefresh = 120; }
            int playing = 0;
            foreach (var a in _audio)
            {
                if (a == null) continue;
                string cur = a.isPlaying && a.clip != null ? a.clip.name : null;
                if (cur != null) playing++;
                string was; _audioWas.TryGetValue(a, out was);
                if (cur != null && cur != was)
                {
                    int n; _audioStarts.TryGetValue(cur, out n); _audioStarts[cur] = n + 1;
                    if (_frame - _lastEventFrame < 20) { string k = _lastEventType + " -> " + cur; int m; _audioEventStarts.TryGetValue(k, out m); _audioEventStarts[k] = m + 1; }
                }
                _audioWas[a] = cur;
            }
            S("audio sources playing", playing);
        }

        string AudioReport()
        {
            var sb = new System.Text.StringBuilder("== audio: clip starts ==\n");
            foreach (var kv in _audioStarts.OrderByDescending(k => k.Value)) sb.AppendLine(kv.Key + " : " + kv.Value);
            sb.AppendLine("== audio: clip starts within 0.33 s after a sim event ==");
            foreach (var kv in _audioEventStarts.OrderBy(k => k.Key)) sb.AppendLine(kv.Key + " : " + kv.Value);
            return sb.ToString();
        }

        // ---------------------------------------------------------------------------------------------------------- per-play numbers
        void LogResult(PlayOutcome o)
        {
            var r = o.Result; if (r == null) return;
            string k = o.Kind.ToString() + "/" + r.Type;
            H("result " + k);
            if (o.Kind == PlayKind.Scrimmage && !o.PenaltyAccepted)
            {
                S("yards per scrimmage play " + r.Type, r.Yards);
                S("play seconds (TimeElapsed) " + r.Type, r.TimeElapsed);
                if (r.RunoffSeconds >= 0f) S("runoff seconds after the play " + r.Type, r.RunoffSeconds);
            }
            if (r.ReturnYards != 0) S("return yards " + o.Kind, r.ReturnYards);
            _plays.AppendLine("    result: type " + r.Type + " yards " + r.Yards + " time " + r.TimeElapsed.ToString("0.0", CI) + " runoff " + r.RunoffSeconds.ToString("0.0", CI) + " oob " + r.OutOfBounds + " td " + r.Touchdown + " turnover " + r.Turnover + " ret " + r.ReturnYards);
        }
    }
}
