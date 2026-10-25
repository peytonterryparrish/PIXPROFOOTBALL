using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.12 baseline: the raw evidence behind the toe-bone float detectors.
    /// float_probe.csv = per-agent bone heights (ankle, toe, toe end) next to the lowest VISIBLE sole vertex of the baked skinned mesh, so a toe-bone metric can be re-scored against what the player sees.
    /// sidestance/*.png = orthographic side views of single bodies (the other 21 hidden) with a ruler pole (green 0, yellow 5 cm, red 10 cm, white every 10 cm up to 40 cm) so a floating stance shows against the scale.
    /// Recording only: it never changes a body, a camera setting of the broadcast rig or the simulation.
    /// </summary>
    public sealed partial class MatchWatchRunner
    {
        readonly StringBuilder _probeCsv = new StringBuilder();
        bool _probeHeader;
        Mesh _bakeMesh; readonly List<Vector3> _bakeVerts = new List<Vector3>();
        readonly Dictionary<string, int> _sideCount = new Dictionary<string, int>();
        int _sideTotal;

        /// <summary>Lowest world-space y of the drawn skinned-mesh vertices within 0.28 m (horizontal) of each ankle bone, relative to gy (the visible sole). NaN when nothing was drawn.</summary>
        void SoleMin(VT t, float gy, out float ls, out float rs)
        {
            if (_bakeMesh == null) _bakeMesh = new Mesh();
            float bl = float.PositiveInfinity, br = float.PositiveInfinity; Vector3 cl = t.LF.position, cr = t.RF.position; const float R2 = 0.28f * 0.28f;
            foreach (var smr in t.Smrs)
            {
                if (!Drawn(smr)) continue;
                smr.BakeMesh(_bakeMesh, true);
                _bakeMesh.GetVertices(_bakeVerts);
                var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                for (int k = 0; k < _bakeVerts.Count; k++)
                {
                    Vector3 w = m.MultiplyPoint3x4(_bakeVerts[k]);
                    float dx = w.x - cl.x, dz = w.z - cl.z; if (dx * dx + dz * dz <= R2 && w.y < bl) bl = w.y;
                    dx = w.x - cr.x; dz = w.z - cr.z; if (dx * dx + dz * dz <= R2 && w.y < br) br = w.y;
                }
            }
            ls = float.IsPositiveInfinity(bl) ? float.NaN : bl - gy; rs = float.IsPositiveInfinity(br) ? float.NaN : br - gy;
        }

        static float Pitch(Transform from, Transform to)
        {
            if (from == null || to == null) return float.NaN;
            Vector3 d = to.position - from.position; float h = Mathf.Sqrt(d.x * d.x + d.z * d.z);
            return Mathf.Atan2(d.y, h) * Mathf.Rad2Deg;
        }

        static string P(float x) { return float.IsNaN(x) ? "" : x.ToString("0.000", CI); }

        void ProbeRow(string tag, int i, SimAgentView v, VT t, SimAgent a, PlaySimulation sim, float gy, float speedMs, float hipsY)
        {
            if (!_probeHeader) { _probeCsv.AppendLine("tag,play,frame,agent,pos,body,act,stance,clip,speed,viewSpeed,hipsY,lankleY,rankleY,ltoeY,rtoeY,ltoeEndY,rtoeEndY,lsoleY,rsoleY,lfootPitch,rfootPitch,lToeAnkleDist,groundY"); _probeHeader = true; }
            float la = t.LF.position.y - gy, ra = t.RF.position.y - gy;
            float lt = t.LT != null ? t.LT.position.y - gy : float.NaN, rt = t.RT != null ? t.RT.position.y - gy : float.NaN;
            float lte = t.LTE != null ? t.LTE.position.y - gy : float.NaN, rte = t.RTE != null ? t.RTE.position.y - gy : float.NaN;
            float ls, rs; SoleMin(t, gy, out ls, out rs);
            float dist = t.LT != null ? Vector3.Distance(t.LT.position, t.LF.position) : float.NaN;
            string body = t.An.avatar != null ? t.An.avatar.name : t.An.gameObject.name;
            _probeCsv.Append(tag).Append(',').Append(_dir.PlayNumber).Append(',').Append(_frame).Append(',').Append(i).Append(',').Append(a.PlayPos).Append(',').Append(body.Replace(',', '_')).Append(',').Append(a.Action).Append(',').Append(v.Stance.Kind).Append(',')
                    .Append(v.Rig != null ? v.Rig.CurrentClipName : "").Append(',').Append(P(speedMs)).Append(',').Append(P(t.ViewSpeed)).Append(',').Append(P(hipsY)).Append(',').Append(P(la)).Append(',').Append(P(ra)).Append(',').Append(P(lt)).Append(',').Append(P(rt)).Append(',')
                    .Append(P(lte)).Append(',').Append(P(rte)).Append(',').Append(P(ls)).Append(',').Append(P(rs)).Append(',').Append(P(Pitch(t.LF, t.LT))).Append(',').Append(P(Pitch(t.RF, t.RT))).Append(',').Append(P(dist)).Append(',').Append(P(gy)).AppendLine();
            // distributions of the metric gap, per body
            float soleLow = Mathf.Min(float.IsNaN(ls) ? 9f : ls, float.IsNaN(rs) ? 9f : rs), toeLow = Mathf.Min(float.IsNaN(lt) ? 9f : lt, float.IsNaN(rt) ? 9f : rt);
            if (soleLow < 8f) { S("probe " + tag + ": lowest visible sole above turf m " + Grp(a.PlayPos), soleLow); if (toeLow < 8f) S("probe " + tag + ": toe bone minus sole m " + body, toeLow - soleLow); }
        }

        void ProbeFlush()
        {
            if (_probeCsv.Length == 0) return;
            File.AppendAllText(_out + "/float_probe.csv", _probeCsv.ToString()); _probeCsv.Length = 0;
        }

        // ---------------------------------------------------------------------------------------------------------- side views
        Material _unlitMat; readonly Dictionary<Color, Material> _poleMats = new Dictionary<Color, Material>();
        Material PoleMat(Color c)
        {
            Material m;
            if (_poleMats.TryGetValue(c, out m)) return m;
            var sh = Shader.Find("HDRP/Unlit"); if (sh == null) sh = Shader.Find("Unlit/Color");
            m = new Material(sh); m.SetColor("_UnlitColor", c); m.SetColor("_Color", c); m.color = c; _poleMats[c] = m; return m;
        }

        GameObject BuildRuler(Vector3 basePos, Vector3 along)
        {
            var root = new GameObject("MwRuler");
            Action<float, float, float, Color> add = (y0, y1, w, col) =>
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube); var cl = c.GetComponent<Collider>(); if (cl != null) Destroy(cl);
                c.transform.SetParent(root.transform, false);
                // thin plane-facing slab: 1.5 cm deep, w wide along the view plane
                c.transform.position = basePos + Vector3.up * ((y0 + y1) * 0.5f); c.transform.localScale = new Vector3(0.015f, y1 - y0, w); c.transform.rotation = Quaternion.LookRotation(along, Vector3.up);
                c.GetComponent<Renderer>().sharedMaterial = PoleMat(col); c.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            };
            add(0f, 0.005f, 0.30f, new Color(0f, 0.9f, 0f));      // 0 cm
            add(0.05f, 0.055f, 0.30f, new Color(1f, 0.9f, 0f));   // 5 cm
            add(0.10f, 0.105f, 0.30f, new Color(1f, 0.1f, 0.1f)); // 10 cm
            for (int k = 2; k <= 4; k++) add(0.1f * k, 0.1f * k + 0.005f, 0.20f, Color.white);
            add(0f, 0.45f, 0.012f, new Color(1f, 1f, 1f));        // the pole (z = along the facing, x = thin across the view)
            return root;
        }

        /// <summary>One orthographic side view of one body (all other bodies and the ball hidden), ruler pole beside him at 0 / 5 / 10 / 20 / 30 / 40 cm. Saves sidestance/p{play}_{tag}_{pos}.png.</summary>
        void SideShot(string tag, int agent, int cap)
        {
            if (_sideTotal >= 160) return;
            string group = tag + "_" + (_dir.Stage != null && _dir.Sim != null ? Grp(_dir.Sim.Agents[agent].PlayPos) : "?");
            int n; _sideCount.TryGetValue(group, out n); if (n >= cap) return;
            var st = _dir.Stage; var sim = st != null ? st.Sim : null; if (sim == null) return;
            var v = st.Views[agent]; if (v == null) return;
            var t = GetVT(v); if (t == null) return;
            _sideCount[group] = n + 1; _sideTotal++;
            _capturing = true;
            var hidden = new List<Renderer>();
            GameObject ruler = null;
            try
            {
                var a = sim.Agents[agent];
                Vector3 fd = st.Frame.DirToUnity(a.Facing); Vector3 fwd = new Vector3(fd.x, 0f, fd.z); if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward; fwd.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                float gy = v.transform.position.y;
                Vector3 mid = (t.LF.position + t.RF.position) * 0.5f; mid.y = gy;
                for (int k = 0; k < 22; k++) if (k != agent && st.Views[k] != null) foreach (var r in st.Views[k].GetComponentsInChildren<Renderer>(false)) if (r.enabled) { r.enabled = false; hidden.Add(r); }
                if (st.Ball != null) foreach (var r in st.Ball.GetComponentsInChildren<Renderer>(false)) if (r.enabled) { r.enabled = false; hidden.Add(r); }
                ruler = BuildRuler(mid + fwd * 0.75f, fwd);
                var cam = _closeCam; var main = _dir.MainCamera;
                cam.cullingMask = main != null ? main.cullingMask : ~0;
                cam.orthographic = true; cam.orthographicSize = 0.40f; cam.nearClipPlane = 3.45f; cam.farClipPlane = 4.55f;      // a 1.1 m slab around the body: his neighbours in the line are out of it
                cam.transform.position = mid + fwd * 0.25f + right * 4f + Vector3.up * 0.34f; cam.transform.LookAt(new Vector3(mid.x, gy + 0.34f, mid.z) + fwd * 0.25f);
                // lower the turf horizon: look straight across so the turf is a line at the bottom of the frame
                var tex = Grab(cam, 960, 540);
                // second view from the other side so a one-sided occluder cannot hide a foot
                cam.transform.position = mid + fwd * 0.25f - right * 4f + Vector3.up * 0.34f; cam.transform.LookAt(new Vector3(mid.x, gy + 0.34f, mid.z) + fwd * 0.25f);
                var tex2 = Grab(cam, 960, 540);
                var big = new Texture2D(1920, 540, TextureFormat.RGB24, false);
                big.SetPixels(0, 0, 960, 540, tex.GetPixels()); big.SetPixels(960, 0, 960, 540, tex2.GetPixels()); big.Apply();
                Directory.CreateDirectory(_out + "/sidestance");
                string name = "sidestance/p" + _dir.PlayNumber + "_" + tag + "_" + a.PlayPos + "_a" + agent + ".png";
                File.WriteAllBytes(_out + "/" + name, big.EncodeToPNG());
                Destroy(tex); Destroy(tex2); Destroy(big);
                _stripIndex.AppendLine(name + " | side stance | " + Ctx() + " | act " + a.Action + " stance " + v.Stance.Kind + " clip " + (v.Rig != null ? v.Rig.CurrentClipName : "") + " | left tile: camera on the right of the player, right tile: opposite side");
            }
            catch (Exception e) { Issue("AUDIT_TOOL_EXCEPTION_SIDE", e.GetType().Name + " " + e.Message); }
            finally
            {
                foreach (var r in hidden) if (r != null) r.enabled = true;
                if (ruler != null) Destroy(ruler);
                _closeCam.orthographic = false; _closeCam.nearClipPlane = 0.1f; _closeCam.farClipPlane = 600f;
                _capturing = false;
            }
        }

        /// <summary>One representative per group (OL, DL, WR, QB) of the line / formation in this play.</summary>
        void SideShotsForGroups(string tag, PlaySimulation sim, int cap)
        {
            var want = new HashSet<string> { "OL", "DL", "WR", "QB" }; var done = new HashSet<string>();
            for (int i = 0; i < 22; i++)
            {
                string g = Grp(sim.Agents[i].PlayPos);
                if (!want.Contains(g) || done.Contains(g)) continue;
                done.Add(g); SideShot(tag, i, cap);
            }
        }
    }
}
