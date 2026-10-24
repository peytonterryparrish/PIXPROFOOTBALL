using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace PixFootball.EditorTools
{
    public sealed partial class MatchWatchRunner
    {
        bool _capturing;
        const int RW = 640, RH = 360, RN = 60, REvery = 6;
        RenderTexture[] _ring; float[] _ringT; int _ringHead, _ringFilled;
        RenderTexture _idleRt;
        Camera _closeCam;

        sealed class Pend { public string Tag; public float T0; public float[] Offs; public float Due; public string Ctx; public string Name; }
        readonly List<Pend> _pend = new List<Pend>();
        readonly Dictionary<string, int> _stripCount = new Dictionary<string, int>();
        readonly StringBuilder _stripIndex = new StringBuilder();

        void SetupCapture()
        {
            _ring = new RenderTexture[RN]; _ringT = new float[RN];
            for (int i = 0; i < RN; i++) { _ring[i] = new RenderTexture(RW, RH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); _ring[i].Create(); _ringT[i] = -99f; }
            var cam = _dir.MainCamera;
            // keep the broadcast camera enabled (Camera.main users) but make its per-frame render tiny
            _idleRt = new RenderTexture(96, 54, 24, RenderTextureFormat.ARGB32); _idleRt.Create();
            if (cam != null) cam.targetTexture = _idleRt;
            var go = new GameObject("MatchWatchCloseCam");
            _closeCam = go.AddComponent<Camera>(); var chd = go.AddComponent<HDAdditionalCameraData>(); _closeCam.enabled = false; _closeCam.nearClipPlane = 0.1f; _closeCam.farClipPlane = 600f; _closeCam.allowHDR = true;
            chd.customRenderingSettings = true;
            { var fs = chd.renderingPathCustomFrameSettings; var mk = chd.renderingPathCustomFrameSettingsOverrideMask; foreach (var ff in new[] { FrameSettingsField.MotionBlur, FrameSettingsField.DepthOfField }) { mk.mask[(uint)ff] = true; fs.SetEnabled(ff, false); } chd.renderingPathCustomFrameSettings = fs; chd.renderingPathCustomFrameSettingsOverrideMask = mk; }
            chd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            _mainHd = cam != null ? cam.GetComponent<HDAdditionalCameraData>() : null;
            if (_mainHd != null)
            {
                Ev("main camera aa=" + _mainHd.antialiasing + " dynRes=" + _mainHd.allowDynamicResolution + " dlss=" + _mainHd.allowDeepLearningSuperSampling + " customSettings=" + _mainHd.customRenderingSettings);
                // the shipped player shows no tilt-shift blur on the broadcast camera (exe screenshots); the editor run applies the broadcast depth of field at a fixed focus: leave it out of the evidence
                _mainHd.customRenderingSettings = true;
                var fs2 = _mainHd.renderingPathCustomFrameSettings; var mk2 = _mainHd.renderingPathCustomFrameSettingsOverrideMask;
                foreach (var ff in new[] { FrameSettingsField.DepthOfField }) { mk2.mask[(uint)ff] = true; fs2.SetEnabled(ff, false); }
                _mainHd.renderingPathCustomFrameSettings = fs2; _mainHd.renderingPathCustomFrameSettingsOverrideMask = mk2;
            }
            SteadyMainCam();
            Ev("capture ring ready; broadcast camera " + (cam != null ? cam.name + " fov " + cam.fieldOfView : "NULL"));
        }

        HDAdditionalCameraData _mainHd;
        /// <summary>Capture renders are one-off renders into render textures of changing sizes: temporal anti-aliasing / upscalers have no history and smear the players. SMAA, no dynamic resolution.</summary>
        void SteadyMainCam()
        {
            if (_mainHd == null) return;
            if (_mainHd.antialiasing != HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing) _mainHd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            _mainHd.allowDynamicResolution = false; _mainHd.allowDeepLearningSuperSampling = false;
        }

        Texture2D ReadRt(RenderTexture rt, int w, int h)
        {
            var old = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = old; return tex;
        }

        Texture2D Grab(Camera c, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = c.targetTexture; bool en = c.enabled; c.targetTexture = rt; c.Render(); c.targetTexture = prev; c.enabled = en;
            var tex = ReadRt(rt, w, h); RenderTexture.ReleaseTemporary(rt); return tex;
        }

        void RingTick()
        {
            if (_capturing || _ring == null) return;
            var cam = _dir.MainCamera; if (cam == null) return;
            if (_frame % REvery == 0 && _dir.Stage != null && _dir.Stage.Sim != null || _frame % (REvery * 3) == 0)
            {
                SteadyMainCam();
                var prev = cam.targetTexture; cam.targetTexture = _ring[_ringHead]; cam.Render(); cam.targetTexture = prev;
                _ringT[_ringHead] = Time.time; _ringHead = (_ringHead + 1) % RN; _ringFilled = Mathf.Min(RN, _ringFilled + 1);
            }
            for (int i = _pend.Count - 1; i >= 0; i--)
            {
                if (Time.time < _pend[i].Due) continue;
                var p = _pend[i]; _pend.RemoveAt(i);
                try { BuildStrip(p); } catch (Exception e) { Issue("AUDIT_TOOL_EXCEPTION_STRIP", e.Message); }
            }
        }

        void BuildStrip(Pend p)
        {
            int n = p.Offs.Length, cols = 3, rows = (n + cols - 1) / cols;
            var big = new Texture2D(cols * RW, rows * RH, TextureFormat.RGB24, false);
            var black = new Color32[RW * RH]; for (int k = 0; k < black.Length; k++) black[k] = new Color32(0, 0, 0, 255);
            var times = new StringBuilder();
            for (int k = 0; k < n; k++)
            {
                float want = p.T0 + p.Offs[k]; int best = -1; float bd = 9f;
                for (int i = 0; i < RN; i++) { float d = Mathf.Abs(_ringT[i] - want); if (d < bd) { bd = d; best = i; } }
                int cx = (k % cols) * RW, cy = (rows - 1 - k / cols) * RH;
                if (best >= 0 && bd < 0.2f) { var tile = ReadRt(_ring[best], RW, RH); big.SetPixels32(cx, cy, RW, RH, tile.GetPixels32()); Destroy(tile); times.Append(p.Offs[k].ToString("+0.0;-0.0", CI)).Append("s "); }
                else { big.SetPixels32(cx, cy, RW, RH, black); times.Append("(missing) "); }
            }
            big.Apply();
            string file = "strips/" + p.Name + ".png";
            File.WriteAllBytes(_out + "/" + file, big.EncodeToPNG());
            Destroy(big);
            _stripIndex.AppendLine(file + " | " + p.Tag + " | " + p.Ctx + " | tiles (left-right, top-bottom): " + times);
            File.WriteAllText(_out + "/strips_index.txt", _stripIndex.ToString());
        }

        void RequestStrip(string tag, float[] offs, int cap)
        {
            int c; _stripCount.TryGetValue(tag, out c);
            if (c >= cap) return;
            _stripCount[tag] = c + 1;
            var s = _dir.Flow != null ? _dir.Flow.State : null;
            string ctx = s != null ? "Q" + s.Quarter + " " + s.ClockDisplay + " " + s.Down + "&" + s.Distance + " spot " + s.BallSpot + " play#" + _dir.PlayNumber : "";
            string name = tag + "_" + (c + 1) + "_p" + _dir.PlayNumber;
            _pend.Add(new Pend { Tag = tag, T0 = Time.time, Offs = offs, Due = Time.time + offs.Max() + 0.15f, Ctx = ctx, Name = name });
        }

        static readonly float[] OffEvent = { -0.4f, -0.2f, 0f, 0.2f, 0.4f, 0.8f };
        static readonly float[] OffSnap = { -0.6f, -0.3f, 0f, 0.3f, 0.6f, 1.0f };
        static readonly float[] OffKick = { -0.9f, -0.5f, -0.1f, 0.3f, 0.8f, 1.5f };
        static readonly float[] OffSetup = { 0.3f, 1.2f, 2.2f, 3.3f, 4.5f, 5.5f };
        static readonly float[] OffAfter = { 0.3f, 1.0f, 2.0f, 3.0f, 4.0f, 5.0f };

        void RequestStripsOnPlayStart(PlaySimulation sim)
        {
            if (sim.Kind == PlayKind.Kickoff) RequestStrip("kickoff_setup", OffSetup, 5);
            else if (sim.Kind == PlayKind.Scrimmage) RequestStrip("huddle_break_and_set", OffSetup, 5);
            else RequestStrip("setup_" + sim.Kind, OffSetup, 3);
            if (sim.Kind == PlayKind.Scrimmage) { HudAfter("presnap", 2.0f, 2); HudAfter("live", 4.5f, 2); }
            if (sim.Kind == PlayKind.Kickoff) HudAfter("kickoff", 2.0f, 2);
        }

        void RequestStripsOnPhase(MatchPhase p)
        {
            if (p == MatchPhase.Break) { RequestStrip("break_quarter_or_half", OffAfter, 4); HudAfter("break", 1.2f, 2); }
            if (p == MatchPhase.Final) { RequestStrip("final", OffAfter, 1); HudAfter("final", 1.0f, 1); }
            if (p == MatchPhase.PlayCall) HudAfter("playcall", 1.0f, 1);
            if (p == MatchPhase.PreSnap) RequestStrip("presnap_phase_from_start", new[] { 0.2f, 0.5f, 0.8f, 1.1f, 1.4f, 1.8f }, 8);
            if (p == MatchPhase.CoinToss) { RequestStrip("cointoss", OffAfter, 1); HudAfter("cointoss", 2.0f, 1); }
        }

        void OnPlayFinishedCapture(PlayOutcome o)
        {
            RequestStrip("postplay_" + (o.Headline == Headline.Touchdown ? "touchdown" : o.Headline == Headline.None ? "none" : o.Headline.ToString()), OffAfter, 2);
            HudAfter("post_" + o.Headline, 0.7f, 2);
            if (o.PenaltyAccepted) HudAfter("penalty", 0.6f, 2);
        }

        void OnSimEvent(PlayEvent e)
        {
            NoteEvent(e); H("event " + e.Type);
            var sim = _dir.Sim; string kind = sim != null ? sim.Kind.ToString() : "";
            switch (e.Type)
            {
                case SimEventType.Snap: RequestStrip("snap", OffSnap, 4); break;
                case SimEventType.Handoff: RequestStrip("handoff", OffEvent, 3); break;
                case SimEventType.Pitch: RequestStrip("pitch", OffEvent, 2); break;
                case SimEventType.Throw: RequestStrip("throw", OffEvent, 5); break;
                case SimEventType.Catch: RequestStrip("catch", OffEvent, 5); break;
                case SimEventType.Drop: RequestStrip("drop", OffEvent, 2); break;
                case SimEventType.Interception: RequestStrip("interception", OffEvent, 2); break;
                case SimEventType.Tackle: RequestStrip("tackle", OffEvent, 6); break;
                case SimEventType.Sack: RequestStrip("sack", OffEvent, 3); break;
                case SimEventType.QbHit: RequestStrip("qbhit", OffEvent, 2); break;
                case SimEventType.Fumble: RequestStrip("fumble", OffEvent, 2); break;
                case SimEventType.Pancake: RequestStrip("pancake", OffEvent, 2); break;
                case SimEventType.BrokenTackle: RequestStrip("brokentackle", OffEvent, 2); break;
                case SimEventType.Touchdown: RequestStrip("touchdown", OffAfter, 3); break;
                case SimEventType.Kick: RequestStrip("kick_" + kind, OffKick, 3); break;
                case SimEventType.KickApproach: RequestStrip("kickapproach_" + kind, new[] { 0.0f, 0.3f, 0.6f, 0.9f, 1.2f, 1.5f }, 3); break;
                case SimEventType.FairCatch: RequestStrip("faircatch", OffEvent, 2); break;
                case SimEventType.Touchback: RequestStrip("touchback", OffAfter, 1); break;
                case SimEventType.FieldGoalGood: RequestStrip("fg_good", OffAfter, 2); break;
                case SimEventType.FieldGoalMissed: RequestStrip("fg_missed", OffAfter, 1); break;
                case SimEventType.KickBlocked: RequestStrip("kick_blocked", OffEvent, 1); break;
                case SimEventType.Whistle: RequestStrip("whistle", OffEvent, 4); break;
                case SimEventType.Penalty: RequestStrip("penalty_event", OffEvent, 2); break;
                case SimEventType.Incomplete: RequestStrip("incomplete", OffEvent, 3); break;
                case SimEventType.OutOfBounds: RequestStrip("out_of_bounds", OffEvent, 2); break;
                case SimEventType.Scramble: RequestStrip("scramble", OffEvent, 2); break;
                case SimEventType.Jam: RequestStrip("jam", OffEvent, 2); break;
                case SimEventType.Deflection: RequestStrip("deflection", OffEvent, 2); break;
                case SimEventType.Kneel: RequestStrip("kneel", OffEvent, 1); break;
                case SimEventType.Spike: RequestStrip("spike", OffEvent, 1); break;
            }
        }

        // ---------------------------------------------------------------------------------------------------------- anomaly shots (the evidence for flagged issues)
        void AnomalyShot(string name, int agent)
        {
            var cam = _dir.MainCamera; if (cam == null) return;
            _capturing = true;
            try
            {
                SteadyMainCam();
                var a = Grab(cam, 960, 540);
                Texture2D b = null;
                var st = _dir.Stage;
                if (agent >= 0 && agent < 22 && st != null && st.Views[agent] != null)
                {
                    var t = GetVT(st.Views[agent]);
                    Vector3 target = t != null ? t.Hips.position : st.Views[agent].transform.position + Vector3.up;
                    _closeCam.transform.position = target + new Vector3(2.6f, 0.7f, -2.6f); _closeCam.transform.LookAt(target); _closeCam.fieldOfView = 36f; _closeCam.cullingMask = cam.cullingMask;
                    b = Grab(_closeCam, 540, 540);
                }
                int w = 960 + (b != null ? 540 : 0);
                var big = new Texture2D(w, 540, TextureFormat.RGB24, false);
                big.SetPixels(0, 0, 960, 540, a.GetPixels()); if (b != null) big.SetPixels(960, 0, 540, 540, b.GetPixels());
                big.Apply();
                File.WriteAllBytes(_out + "/anomaly/" + name + "_p" + _dir.PlayNumber + ".png", big.EncodeToPNG());
                Destroy(a); if (b != null) Destroy(b); Destroy(big);
                _stripIndex.AppendLine("anomaly/" + name + "_p" + _dir.PlayNumber + ".png | anomaly | " + Ctx());
            }
            finally { _capturing = false; }
        }

        // ---------------------------------------------------------------------------------------------------------- HUD probes
        readonly Dictionary<string, int> _hudCount = new Dictionary<string, int>();
        readonly Queue<string> _hudJobs = new Queue<string>();
        void HudAfter(string tag, float seconds, int cap)
        {
            int c; _hudCount.TryGetValue(tag, out c); if (c >= cap) return; _hudCount[tag] = c + 1;
            StartCoroutine(HudDelay(tag + "_" + (c + 1), seconds));
        }
        IEnumerator HudDelay(string tag, float seconds) { yield return new WaitForSeconds(seconds); _hudJobs.Enqueue(tag); }

        IEnumerator CaptureWorker()
        {
            while (!_finishing)
            {
                if (_hudJobs.Count > 0 && _dir != null && !_dir.Finished || _hudJobs.Count > 0 && _dir != null && _dir.Phase == MatchPhase.Final)
                {
                    string tag = _hudJobs.Dequeue();
                    foreach (var res in new[] { new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) })
                    {
                        _capturing = true;
                        yield return HudProbe(tag, res.x, res.y);
                        _capturing = false;
                    }
                }
                yield return null;
            }
        }

        sealed class HRect { public string Path, Group, Text; public Rect R; public float Alpha; }

        IEnumerator HudProbe(string tag, int w, int h)
        {
            var cam = _dir.MainCamera; if (cam == null) yield break;
            var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.gameObject.activeInHierarchy).ToList();
            var modes = canvases.Select(c => new { c, c.renderMode, c.worldCamera, c.planeDistance, c.gameObject.layer }).ToList();
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); rt.Create();
            var prevRt = cam.targetTexture;
            foreach (var c in canvases) { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = cam; c.planeDistance = 1f; }
            SteadyMainCam();
            cam.targetTexture = rt;
            yield return null; yield return null;
            Canvas.ForceUpdateCanvases();
            foreach (var s in FindObjectsByType<CanvasScaler>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) s.SendMessage("Handle", SendMessageOptions.DontRequireReceiver);
            Canvas.ForceUpdateCanvases();
            List<HRect> rects = null;
            try { rects = MeasureHud(cam, canvases, w, h); } catch (Exception e) { Issue("AUDIT_TOOL_EXCEPTION_HUD", e.Message); }
            Texture2D shot = null;
            try { cam.Render(); shot = ReadRt(rt, w, h); } catch (Exception e) { Issue("AUDIT_TOOL_EXCEPTION_HUD_RENDER", e.Message); }
            cam.targetTexture = prevRt;
            foreach (var m in modes) { if (m.c == null) continue; m.c.renderMode = m.renderMode; m.c.worldCamera = m.worldCamera; m.c.planeDistance = m.planeDistance; }
            string stem = "hud/" + tag + "_" + w + "x" + h;
            if (shot != null) { File.WriteAllBytes(_out + "/" + stem + ".png", shot.EncodeToPNG()); Destroy(shot); }
            if (rects != null) AnalyseHud(tag, w, h, rects, stem);
            rt.Release(); Destroy(rt);
        }

        List<HRect> MeasureHud(Camera cam, List<Canvas> canvases, int w, int h)
        {
            var list = new List<HRect>();
            var corners = new Vector3[4];
            foreach (var cv in canvases)
            {
                foreach (var g in cv.GetComponentsInChildren<Graphic>(false))
                {
                    if (g == null || !g.gameObject.activeInHierarchy || !g.enabled || g.canvasRenderer.cull) continue;
                    float alpha = g.color.a * g.canvasRenderer.GetAlpha();
                    foreach (var grp in g.GetComponentsInParent<CanvasGroup>(false)) { alpha *= grp.alpha; if (grp.ignoreParentGroups) break; }
                    if (alpha < 0.05f) continue;
                    string text = null;
                    Rect rr;
                    var tmp = g as TMP_Text;
                    if (tmp != null)
                    {
                        if (string.IsNullOrEmpty(tmp.text)) continue;
                        text = tmp.text.Length > 60 ? tmp.text.Substring(0, 60) : tmp.text;
                        tmp.ForceMeshUpdate();
                        var b = tmp.textBounds; if (b.size.sqrMagnitude < 1e-8f) continue;
                        var tr = tmp.transform;
                        Vector3 a0 = cam.WorldToViewportPoint(tr.TransformPoint(b.min)), a1 = cam.WorldToViewportPoint(tr.TransformPoint(b.max)), a2 = cam.WorldToViewportPoint(tr.TransformPoint(new Vector3(b.min.x, b.max.y, 0))), a3 = cam.WorldToViewportPoint(tr.TransformPoint(new Vector3(b.max.x, b.min.y, 0)));
                        float x0 = Mathf.Min(Mathf.Min(a0.x, a1.x), Mathf.Min(a2.x, a3.x)), x1 = Mathf.Max(Mathf.Max(a0.x, a1.x), Mathf.Max(a2.x, a3.x)), y0 = Mathf.Min(Mathf.Min(a0.y, a1.y), Mathf.Min(a2.y, a3.y)), y1 = Mathf.Max(Mathf.Max(a0.y, a1.y), Mathf.Max(a2.y, a3.y));
                        rr = Rect.MinMaxRect(x0, y0, x1, y1);
                    }
                    else
                    {
                        var t = g as Text; if (t != null) { if (string.IsNullOrEmpty(t.text)) continue; text = t.text.Length > 60 ? t.text.Substring(0, 60) : t.text; }
                        g.rectTransform.GetWorldCorners(corners);
                        Vector3 c0 = cam.WorldToViewportPoint(corners[0]), c2 = cam.WorldToViewportPoint(corners[2]);
                        rr = Rect.MinMaxRect(Mathf.Min(c0.x, c2.x), Mathf.Min(c0.y, c2.y), Mathf.Max(c0.x, c2.x), Mathf.Max(c0.y, c2.y));
                    }
                    if (rr.width * w < 3f || rr.height * h < 3f) continue;
                    // paths: drop the canvas name, group by the first two segments
                    string path = NodePath(g.transform, cv.transform);
                    var segs = path.Split('/'); string grpName = segs.Length >= 2 ? segs[0] + "/" + segs[1] : segs[0];
                    if (segs.Length >= 3 && (segs[0].StartsWith("MatchHud") || segs[0] == "Hud")) grpName = segs[0] + "/" + segs[1];
                    list.Add(new HRect { Path = path, Group = grpName, R = rr, Text = text, Alpha = alpha });
                }
            }
            return list;
        }

        static string NodePath(Transform t, Transform root)
        {
            var sb = new List<string>(); while (t != null && t != root) { sb.Add(t.name); t = t.parent; }
            sb.Reverse(); return string.Join("/", sb);
        }

        void AnalyseHud(string tag, int w, int h, List<HRect> rects, string stem)
        {
            var sb = new StringBuilder();
            sb.AppendLine("HUD probe " + tag + " " + w + "x" + h + " " + Ctx() + "; elements=" + rects.Count);
            var groups = rects.GroupBy(r => r.Group).ToList();
            var gRects = new List<KeyValuePair<string, Rect>>();
            foreach (var g in groups)
            {
                float x0 = g.Min(r => r.R.xMin), x1 = g.Max(r => r.R.xMax), y0 = g.Min(r => r.R.yMin), y1 = g.Max(r => r.R.yMax);
                var u = Rect.MinMaxRect(x0, y0, x1, y1);
                gRects.Add(new KeyValuePair<string, Rect>(g.Key, u));
                string sample = string.Join(" | ", g.Where(r => r.Text != null).Select(r => r.Text.Replace("\n", " ")).Take(4));
                sb.AppendLine(g.Key + " : x " + x0.ToString("0.000", CI) + ".." + x1.ToString("0.000", CI) + " y " + y0.ToString("0.000", CI) + ".." + y1.ToString("0.000", CI) + "  size " + ((x1 - x0) * 100).ToString("0.0", CI) + "% x " + ((y1 - y0) * 100).ToString("0.0", CI) + "%  centre (" + ((x0 + x1) / 2).ToString("0.00", CI) + "," + ((y0 + y1) / 2).ToString("0.00", CI) + ")  text: " + sample);
                if (x0 < -0.01f || x1 > 1.01f || y0 < -0.01f || y1 > 1.01f) Issue("HUD_ELEMENT_OFF_SCREEN_" + g.Key.Replace(" ", "_"), tag + " " + w + "x" + h + " x " + x0.ToString("0.00", CI) + ".." + x1.ToString("0.00", CI) + " y " + y0.ToString("0.00", CI) + ".." + y1.ToString("0.00", CI), Mathf.Max(-x0, x1 - 1f, -y0, y1 - 1f));
            }
            // coverage of the play area (centre 60% x 50%)
            int cells = 0, covered = 0;
            for (int ix = 0; ix < 60; ix++)
                for (int iy = 0; iy < 50; iy++)
                {
                    float px = 0.2f + 0.6f * (ix + 0.5f) / 60f, py = 0.25f + 0.5f * (iy + 0.5f) / 50f; cells++;
                    foreach (var r in rects) if (r.R.Contains(new Vector2(px, py)) && r.Alpha > 0.15f) { covered++; break; }
                }
            float cov = covered / (float)cells;
            sb.AppendLine("coverage of the centre play area (x 0.2-0.8, y 0.25-0.75) by visible HUD graphics: " + (cov * 100).ToString("0.0", CI) + "%");
            S("hud centre-area coverage % (" + tag.Split('_')[0] + ")", cov * 100f);
            // bottom strip: elements whose centre is below 22% of the screen height
            var bottom = rects.Where(r => r.R.center.y < 0.22f).ToList();
            if (bottom.Count > 0)
            {
                float by1 = bottom.Max(r => r.R.yMax);
                sb.AppendLine("lowest 22%: " + bottom.Count + " graphics reaching up to y=" + by1.ToString("0.000", CI) + " (" + (by1 * 100).ToString("0.0", CI) + "% of the screen height)");
                S("hud bottom elements top edge % of height (" + tag.Split('_')[0] + ")", by1 * 100f);
            }
            // overlaps between groups
            for (int i = 0; i < gRects.Count; i++)
                for (int j = i + 1; j < gRects.Count; j++)
                {
                    var a = gRects[i].Value; var b = gRects[j].Value;
                    float ix0 = Mathf.Max(a.xMin, b.xMin), ix1 = Mathf.Min(a.xMax, b.xMax), iy0 = Mathf.Max(a.yMin, b.yMin), iy1 = Mathf.Min(a.yMax, b.yMax);
                    if (ix1 > ix0 && iy1 > iy0)
                    {
                        float inter = (ix1 - ix0) * (iy1 - iy0), small = Mathf.Min(a.width * a.height, b.width * b.height);
                        if (small > 1e-6f && inter / small > 0.25f && !gRects[i].Key.Contains(gRects[j].Key) && !gRects[j].Key.Contains(gRects[i].Key)) sb.AppendLine("OVERLAP groups " + gRects[i].Key + " x " + gRects[j].Key + " : " + (inter / small * 100).ToString("0") + "% of the smaller");
                    }
                }
            sb.AppendLine("--- every element ---");
            foreach (var r in rects.OrderBy(r => r.Group)) sb.AppendLine(r.Path + " : " + r.R.xMin.ToString("0.000", CI) + "," + r.R.yMin.ToString("0.000", CI) + " - " + r.R.xMax.ToString("0.000", CI) + "," + r.R.yMax.ToString("0.000", CI) + (r.Text != null ? " \"" + r.Text.Replace("\n", " ") + "\"" : ""));
            File.WriteAllText(_out + "/" + stem + ".txt", sb.ToString());
        }
    }
}
