using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 CRITICAL: per-frame body health probe for the built player and the editor (QA only, behind <c>-pixBodyWatch &lt;outdir&gt;</c>). While a match runs it measures every one of the 22 views each frame with <see cref="BodyCheck"/>:
    /// view active, every visible part's LOD renderer active with a mesh, skinned-mesh bounds against the real skeleton (culled while the skeleton is on screen), the body in the camera frustum and drawn,
    /// feet height against the turf (floating / sunk), T / bind pose, a twisted trunk, a body far from where the simulation put him, NaN bones. Every problem is counted twice: on any body, and on a body the broadcast camera sees
    /// ("vis"); only those matter to the viewer. A new kind <c>popOnEntry</c> counts pose problems in the first 5 frames after a body enters the picture (stale pose of a culled animator).
    /// It also counts, per team, the frames in which none of its 11 bodies is on screen (the team "disappears").
    /// It writes <c>watch_frames.csv</c> (one line per frame), <c>watch_events.txt</c> (one line per episode of 4+ frames, the 22-body table at every kickoff / special-teams set), <c>watch_summary.txt</c> and <c>watch_stanceclips.txt</c>.
    /// With <c>-pixBodyWatchShots N</c> it saves a picture at the start of each new visible problem (at most N per kind).
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class BodyWatch : MonoBehaviour
    {
        public static bool Requested { get { return Arg("-pixBodyWatch") != null; } }
        static string Arg(string name) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1]; return null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Requested) return;
            var go = new GameObject("PixBodyWatch");
            DontDestroyOnLoad(go);
            go.AddComponent<BodyWatch>();
        }

        // ---- problem kinds
        const int KInactive = 0, KMissingPart = 1, KCulledWrong = 2, KNotDrawn = 3, KFloating = 4, KTPose = 5, KTwisted = 6, KOffPlace = 7, KBadScale = 8, KSunk = 9, KPopOnEntry = 10, KCount = 11;
        static readonly string[] KName = { "viewInactive", "missingPart", "culledWhileOnScreen", "notDrawnInFrustum", "floatingFeet", "tOrBindPose", "twistedTrunk", "offPlace", "badScaleOrNaN", "sunkBody", "popOnEntry" };

        string _out;
        StreamWriter _frames, _events;
        readonly int[,] _issueFrames = new int[22, KCount];     // any body
        readonly int[,] _issueVis = new int[22, KCount];        // body on screen
        readonly int[,] _runLen = new int[22, KCount];
        readonly int[,] _runStart = new int[22, KCount];
        readonly Dictionary<string, int> _byPosKind = new Dictionary<string, int>();
        readonly Dictionary<string, int> _byActionKind = new Dictionary<string, int>();
        readonly Dictionary<string, int> _posFrames = new Dictionary<string, int>();
        readonly Dictionary<string, int> _actionFrames = new Dictionary<string, int>();
        readonly Dictionary<string, int> _shotsPerKind = new Dictionary<string, int>();
        readonly int[] _onScreenFor = new int[22];
        readonly Plane[] _planes = new Plane[6];
        int _frame, _framesWithView;
        int _lastPlayId = -1, _tableDumped = -1;
        int _maxShots;
        readonly Dictionary<string, int[]> _ankleHist = new Dictionary<string, int[]>();
        readonly int[] _minInView = { 99, 99 };
        readonly int[] _teamGoneFrames = new int[2];
        readonly Dictionary<string, int[]> _teamGoneByPhase = new Dictionary<string, int[]>();   // phase|kind -> frames, offense gone, defense gone

        void Awake()
        {
            _out = Arg("-pixBodyWatch");
            if (string.IsNullOrEmpty(_out)) _out = Path.Combine(Application.dataPath, "..", "Logs", "bodywatch");
            Directory.CreateDirectory(_out);
            _frames = new StreamWriter(Path.Combine(_out, "watch_frames.csv"), false, new UTF8Encoding(false));
            _events = new StreamWriter(Path.Combine(_out, "watch_events.txt"), false, new UTF8Encoding(false));
            _frames.WriteLine("frame,t,play,phase,simPhase,kind,offIn,defIn,offDrawn,defDrawn,offActive,defActive,missingParts,culledWrong,notDrawn,floating,tpose,twisted,offPlace,sunk,badScale,lodMin,lodMax,dtMs");
            int.TryParse(Arg("-pixBodyWatchShots") ?? "0", out _maxShots);
            Debug.Log("[BodyWatch] recording to " + _out);
            try { DumpStanceClips(); } catch (Exception e) { Debug.LogWarning("[BodyWatch] stance clip dump: " + e.Message); }
        }

        /// <summary>Which stance clips exist in THIS player (the slim ArtLibrary of a build drops keys / long takes): per stance kind and role, the keys, their variant counts and the clip the plan resolves to.</summary>
        void DumpStanceClips()
        {
            var lib = PixFootball.Art.ArtLibrary.Load();
            var sb = new StringBuilder();
            string[] photoKeys = { "photo_player_small", "photo_player_medium", "photo_player_large", "photo_player_small_athletic", "photo_player_small_runner", "photo_player_small_stocky", "photo_player_small_tall", "photo_player_medium_athletic", "photo_player_medium_runner", "photo_player_medium_stocky", "photo_player_medium_tall", "photo_player_large_athletic", "photo_player_large_runner", "photo_player_large_stocky", "photo_player_large_tall" };
            sb.AppendLine("ArtLibrary: " + (lib == null ? "NULL" : "loaded") + "  photo prefabs: " + (lib == null ? "-" : string.Join(",", Array.FindAll(photoKeys, k => lib.Prefab(k) != null))));
            if (lib != null)
            {
                int nKeys = 0, nClips = 0; foreach (var k in lib.ClipKeys) { nKeys++; nClips += lib.ClipVariantCount(k); }
                sb.AppendLine("clip keys " + nKeys + ", clips " + nClips);
                foreach (StanceKind kind in Enum.GetValues(typeof(StanceKind)))
                    foreach (PlayerRole role in new[] { PlayerRole.QB, PlayerRole.RB, PlayerRole.WR, PlayerRole.TE, PlayerRole.OL, PlayerRole.DL, PlayerRole.LB, PlayerRole.DB, PlayerRole.K })
                    {
                        var keys = StanceRules.ClipKeys(kind, role); if (keys.Length == 0) continue;
                        var spec = new StanceSpec { Kind = kind };
                        var plan = StanceClips.Plan(spec, 3, role);
                        var ks = new StringBuilder();
                        foreach (var k in keys) ks.Append(k + "(" + lib.ClipVariantCount(k) + ") ");
                        sb.AppendLine(kind.ToString().PadRight(20) + role.ToString().PadRight(4) + (plan.Clip != null ? "OK  " + plan.Clip.name + " len=" + plan.Clip.length.ToString("0.0") : "NO CLIP") + "   keys: " + ks);
                    }
            }
            File.WriteAllText(Path.Combine(_out, "watch_stanceclips.txt"), sb.ToString());
        }

        void LateUpdate()
        {
            var d = MatchDirector.Instance;
            if (d == null || d.Stage == null || d.Sim == null) return;
            var stage = d.Stage; var sim = d.Sim;
            var cam = PlayerLod.ViewCamera != null ? PlayerLod.ViewCamera : Camera.main;
            if (cam == null) return;
            _frame++;
            GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            int[] inView = new int[2], drawn = new int[2], active = new int[2];
            var tot = new int[KCount];
            int lodMin = 9, lodMax = -1;
            int playId = d.PlayNumber;
            bool viewsSeen = false;
            string kindName = sim.Kind.ToString();
            var views = stage.Views;
            for (int i = 0; i < 22; i++)
            {
                var v = views[i]; var a = sim.Agents[i];
                if (v == null) continue;
                viewsSeen = true;
                int side = a.Side == SimSide.Offense ? 0 : 1;
                string posKey = a.PlayPos.ToString();
                string actKey = a.Action.ToString();
                Inc(_posFrames, posKey); Inc(_actionFrames, actKey);
                var h = BodyCheck.Measure(v, cam, _planes, (_frame + i) % 4 == 0);
                var bits = new bool[KCount];
                bits[KInactive] = h.Inactive; bits[KMissingPart] = h.MissingPart; bits[KCulledWrong] = h.CulledWrong; bits[KNotDrawn] = h.NotDrawn; bits[KTPose] = h.TPose; bits[KTwisted] = h.Twisted; bits[KOffPlace] = h.OffPlace; bits[KBadScale] = h.BadScale;
                // floating / sunk count only while the simulation says the feet are on the turf
                bool grounded = BodyCheck.FeetOnTurfAction(a.Action) && a.JumpTime <= 0f && !a.IsDown && !(v.Ragdoll != null && v.Ragdoll.Active) && !v.ContactDriven;
                bits[KFloating] = h.Floating && grounded;
                bits[KSunk] = h.Sunk && grounded;
                // a body that just came into the picture: the first frames show the stale pose of a culled animator
                bool on = h.OnScreen && !h.Inactive;
                if (on) _onScreenFor[i]++; else _onScreenFor[i] = 0;
                bool entering = on && _onScreenFor[i] <= 5 && _framesWithView > 5;
                bits[KPopOnEntry] = entering && (bits[KTPose] || bits[KTwisted] || bits[KOffPlace] || bits[KBadScale] || bits[KSunk] || bits[KFloating]);
                if (!h.Inactive)
                {
                    active[side]++;
                    if (h.OnScreen) inView[side]++;
                    if (h.Drawn) drawn[side]++;
                    if (h.Lod >= 0) { if (h.Lod < lodMin) lodMin = h.Lod; if (h.Lod > lodMax) lodMax = h.Lod; }
                    if (h.SoleGap < 8f && BodyCheck.FeetOnTurfAction(a.Action) && on)
                    {
                        int[] hist; if (!_ankleHist.TryGetValue(actKey, out hist)) _ankleHist[actKey] = hist = new int[14];
                        hist[Mathf.Clamp((int)((h.SoleGap + 0.1f) / 0.05f), 0, 13)]++;
                    }
                }
                for (int k = 0; k < KCount; k++)
                {
                    if (bits[k])
                    {
                        tot[k]++;
                        _issueFrames[i, k]++; if (on) _issueVis[i, k]++;
                        if (on) { Inc(_byPosKind, posKey + "|" + KName[k]); Inc(_byActionKind, actKey + "|" + KName[k]); }
                        if (_runLen[i, k] == 0) _runStart[i, k] = _frame;
                        _runLen[i, k]++;
                        if (_runLen[i, k] == 4 || (k == KPopOnEntry && _runLen[i, k] == 1)) Episode(KName[k], i, v, a, playId, kindName, _runStart[i, k], h, cam);
                    }
                    else _runLen[i, k] = 0;
                }
            }
            if (!viewsSeen) return;
            _framesWithView++;
            for (int s = 0; s < 2; s++) if (inView[s] < _minInView[s]) _minInView[s] = inView[s];
            // a team that has none of its bodies in the picture (the whole team "disappears")
            string pk = d.Phase + "|" + kindName; int[] tg; if (!_teamGoneByPhase.TryGetValue(pk, out tg)) _teamGoneByPhase[pk] = tg = new int[3];
            tg[0]++; if (inView[0] == 0) { tg[1]++; _teamGoneFrames[0]++; } if (inView[1] == 0) { tg[2]++; _teamGoneFrames[1]++; }
            _frames.WriteLine(_frame + "," + Time.unscaledTime.ToString("0.000", CultureInfo.InvariantCulture) + "," + playId + "," + d.Phase + "," + sim.Phase + "," + kindName + "," + inView[0] + "," + inView[1] + "," + drawn[0] + "," + drawn[1] + "," + active[0] + "," + active[1]
                + "," + tot[KMissingPart] + "," + tot[KCulledWrong] + "," + tot[KNotDrawn] + "," + tot[KFloating] + "," + tot[KTPose] + "," + tot[KTwisted] + "," + tot[KOffPlace] + "," + tot[KSunk] + "," + tot[KBadScale] + "," + lodMin + "," + lodMax + "," + (Time.unscaledDeltaTime * 1000f).ToString("0.0", CultureInfo.InvariantCulture));
            // the 22-body table at the first frame of every kickoff / punt / field goal set and of every fifth play
            if (playId != _lastPlayId) { _lastPlayId = playId; _tableDumped = -1; }
            if (_tableDumped != playId && sim.Phase == PlayPhase.PreSnap && _framesWithView > 3 && (sim.Kind != PlayKind.Scrimmage || playId % 5 == 0))
            {
                _tableDumped = playId;
                DumpTable(d, "play " + playId + " " + sim.Kind + " " + sim.Phase + " t=" + sim.Time.ToString("0.00"), cam);
            }
        }

        void DumpTable(MatchDirector d, string title, Camera cam)
        {
            _events.WriteLine("---- " + title + "  camera=" + cam.transform.position.ToString("0.0") + " fov=" + cam.fieldOfView.ToString("0"));
            var sim = d.Sim; var views = d.Stage.Views;
            for (int i = 0; i < 22; i++)
            {
                var v = views[i]; var a = sim.Agents[i];
                if (v == null) { _events.WriteLine("  " + i + " " + a.PlayPos + " view=NULL"); continue; }
                var photo = v.Avatar != null ? v.Avatar.Photo : null;
                int act = 0, drawn = 0, tot = 0;
                if (photo != null) foreach (var p in photo.parts) { if (p == null || p.lods == null) continue; foreach (var r in p.lods) { if (r == null) continue; tot++; if (r.gameObject.activeInHierarchy && r.enabled) { act++; if (r.isVisible) drawn++; } } }
                Vector3 vp = cam.WorldToViewportPoint(v.transform.position);
                _events.WriteLine("  " + i.ToString("00") + " " + a.Side.ToString().Substring(0, 3) + " " + a.PlayPos.ToString().PadRight(5) + " pos=" + v.transform.position.ToString("0.0") + " vp=" + vp.ToString("0.00") + " active=" + v.gameObject.activeInHierarchy + " act/drawn/tot=" + act + "/" + drawn + "/" + tot + " lvl=" + (photo != null ? photo.Level : -1) + " action=" + a.Action + " stance=" + v.Stance.Kind);
            }
        }

        void Episode(string kind, int agent, SimAgentView v, SimAgent a, int play, string playKind, int startFrame, in BodyHealth h, Camera cam)
        {
            Vector3 vp = cam.WorldToViewportPoint(v.transform.position);
            _events.WriteLine("f" + startFrame + " play" + play + " " + playKind + " " + kind + " agent" + agent + " " + a.PlayPos + " " + a.Side + " action=" + a.Action + " stance=" + v.Stance.Kind + " pos=" + v.transform.position.ToString("0.0") + " vis=" + (h.OnScreen ? 1 : 0) + " vp=" + vp.ToString("0.00") + h.Detail);
            if (_maxShots > 0 && h.OnScreen)
            {
                int n; _shotsPerKind.TryGetValue(kind, out n);
                if (n < _maxShots) { _shotsPerKind[kind] = n + 1; try { Shot(Path.Combine(_out, "shot_" + kind + "_" + (n + 1) + "_p" + play + "_" + a.PlayPos + ".png"), cam); } catch { } }
            }
        }

        /// <summary>A picture of what the viewer sees now: the screen in a player, a render of the camera in a batch-mode editor (WaitForEndOfFrame never fires there).</summary>
        public static void Shot(string path, Camera cam)
        {
            if (!Application.isBatchMode) { ScreenCapture.CaptureScreenshot(path); return; }
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var old = cam.targetTexture; cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = old; rt.Release(); Destroy(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG()); Destroy(tex);
        }

        static void Inc(Dictionary<string, int> m, string k) { int n; m.TryGetValue(k, out n); m[k] = n + 1; }

        void OnDestroy() { Finish(); }
        void OnApplicationQuit() { Finish(); }

        bool _done;
        public void Finish()
        {
            if (_done) return; _done = true;
            try
            {
                _frames.Flush(); _frames.Close(); _events.Flush(); _events.Close();
                var sb = new StringBuilder();
                sb.AppendLine("frames with views: " + _framesWithView + "  (22 bodies each)");
                sb.AppendLine("min bodies of a team on screen (a bone in the viewport): offense " + _minInView[0] + ", defense " + _minInView[1]);
                sb.AppendLine("frames in which a whole team is off screen: offense " + _teamGoneFrames[0] + ", defense " + _teamGoneFrames[1] + "  (" + (100.0 * _teamGoneFrames[0] / Math.Max(1, _framesWithView)).ToString("0.0") + " % / " + (100.0 * _teamGoneFrames[1] / Math.Max(1, _framesWithView)).ToString("0.0") + " %)");
                sb.AppendLine("\nwhole team off screen by phase|play kind (frames, offense gone, defense gone):");
                foreach (var kv in _teamGoneByPhase) sb.AppendLine("  " + kv.Key.PadRight(24) + kv.Value[0].ToString().PadLeft(6) + kv.Value[1].ToString().PadLeft(6) + kv.Value[2].ToString().PadLeft(6));
                sb.AppendLine("\nproblem                 body-frames any body        body-frames on screen   bodies affected (vis)");
                for (int k = 0; k < KCount; k++)
                {
                    int any = 0, vis = 0, bodies = 0; for (int i = 0; i < 22; i++) { any += _issueFrames[i, k]; vis += _issueVis[i, k]; if (_issueVis[i, k] > 0) bodies++; }
                    sb.AppendLine(KName[k].PadRight(22) + any.ToString().PadLeft(8) + " (" + (100.0 * any / Math.Max(1L, (long)_framesWithView * 22)).ToString("0.000").PadLeft(7) + " %)   " + vis.ToString().PadLeft(8) + " (" + (100.0 * vis / Math.Max(1L, (long)_framesWithView * 22)).ToString("0.000").PadLeft(7) + " %)   " + bodies);
                }
                sb.AppendLine("\nby position (issue|count of on-screen body-frames, share of that position's frames):");
                foreach (var kv in Sorted(_byPosKind)) { string pos = kv.Key.Substring(0, kv.Key.IndexOf('|')); sb.AppendLine("  " + kv.Key.PadRight(32) + kv.Value + "  " + (100.0 * kv.Value / Math.Max(1, _posFrames[pos])).ToString("0.00") + " %"); }
                sb.AppendLine("\nby action:");
                foreach (var kv in Sorted(_byActionKind)) { string act = kv.Key.Substring(0, kv.Key.IndexOf('|')); sb.AppendLine("  " + kv.Key.PadRight(32) + kv.Value + "  " + (100.0 * kv.Value / Math.Max(1, _actionFrames[act])).ToString("0.00") + " %"); }
                sb.AppendLine("\nlowest-sole height histogram, on-screen bodies on the turf (5 cm buckets starting at -10 cm; last = 55 cm and up):");
                foreach (var kv in _ankleHist) sb.AppendLine("  " + kv.Key.PadRight(14) + string.Join(" ", Array.ConvertAll(kv.Value, x => x.ToString())));
                File.WriteAllText(Path.Combine(_out, "watch_summary.txt"), sb.ToString());
            }
            catch (Exception e) { Debug.LogWarning("[BodyWatch] finish: " + e.Message); }
        }

        static List<KeyValuePair<string, int>> Sorted(Dictionary<string, int> m)
        {
            var l = new List<KeyValuePair<string, int>>(m); l.Sort((x, y) => y.Value.CompareTo(x.Value)); return l;
        }
    }
}
