using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.12.0 CONTACT: the pre-snap walk-in probe of <see cref="ChoreoAuditRunner"/> (<c>-pixChoreoAuditWalkIn n</c>: n plays whose bodies come from the last play's spots through the real
    /// huddle / break / <c>GlideStep</c> flow, each sampled until the break is over). A body is "walking in" when the simulation has him standing (Stance / Idle / Block, under 1.2 m/s) and the view moves him
    /// at 0.35 m/s or more. Reported (all keys start with "walk-in"): the view speed, the planted-foot slide per frame and per contact (slowest frame), the planted-interval drift, the share of frames with neither foot planted, the walk duration.
    /// </summary>
    public sealed partial class ChoreoAuditRunner
    {
        sealed class Wk
        {
            public readonly Vector3[] Root = new Vector3[5]; public int N;
            public Vector3 PrevLT, PrevRT, AL, AR; public bool PlantL, PlantR, Have;
            public float MinL = 99f, MinR = 99f, DrL, DrR; public int CntL, CntR; public bool WalkL, WalkR;
        }
        readonly Dictionary<SimAgentView, Wk> _wk = new Dictionary<SimAgentView, Wk>();

        IEnumerator WalkIn(int n)
        {
            var offense = _book.AllPlays.Where(p => p.Side == PlaySide.Offense && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike && p.Name != "Hail Mary").ToList();
            var rnd = new System.Random(777);
            int idx = 0;
            for (int pi = 0; pi < n + 1; pi++)
            {
                var off = offense[rnd.Next(offense.Count)]; var def = DefFor(off);
                var sim = NewSim(off, def, Scrim(20 + rnd.Next(60), 1 + rnd.Next(3), 4 + rnd.Next(8)), 900 + pi);
                _stage.NextBeginHuddle = true;
                _stage.Begin(sim, new PlayFrame(true), KitA, KitB, ++idx);
                Prep();
                bool measure = pi > 0 && _stage.CarryActive;      // the first play has no bodies to carry: it only sets the bodies down
                _wk.Clear();
                int f = 0;
                while (f < 60 * 40 && (f < 30 || _stage.BreakInProgress))
                {
                    sim.Tick(Dt, default(ControlInput), -1); _stage.Present(Dt); yield return null; f++;
                    if (measure) SampleWalk(sim);
                }
                if (measure) { Add("walk-in duration s (huddle spot to the last body on his line)", f * Dt); Cnt("walk-in plays"); }
                _stage.PrepareCarry();
                yield return null;
            }
            _stage.End();
            yield return null;
        }

        void SampleWalk(PlaySimulation sim)
        {
            for (int i = 0; i < 22; i++)
            {
                var a = sim.Agents[i]; var v = _stage.Views[i]; if (v == null || v.Avatar == null) continue;
                var b = Bones(v); if (b == null) continue;
                Wk w; if (!_wk.TryGetValue(v, out w)) { w = new Wk(); _wk[v] = w; }
                Vector3 root = v.transform.position;
                if (w.N < 5) { w.Root[w.N++] = root; }
                else { for (int k = 0; k < 4; k++) w.Root[k] = w.Root[k + 1]; w.Root[4] = root; }
                float vs = w.N >= 5 ? Vector3.ProjectOnPlane(w.Root[4] - w.Root[0], Vector3.up).magnitude / (4f * Dt) : 0f;
                bool standing = (a.Action == AgentAction.Stance || a.Action == AgentAction.Idle || a.Action == AgentAction.Block) && a.Speed * 0.9144f < 1.2f;
                bool walking = standing && vs >= 0.35f;
                Vector3 ltP = b.LT != null ? b.LT.position : b.LF.position, rtP = b.RT != null ? b.RT.position : b.RF.position;
                float gy = root.y;
                float lc = Mathf.Min(ltP.y - gy, b.LF.position.y - gy - 0.09f), rc = Mathf.Min(rtP.y - gy, b.RF.position.y - gy - 0.09f);
                bool plantL = w.PlantL ? lc < 0.07f : lc < 0.04f, plantR = w.PlantR ? rc < 0.07f : rc < 0.04f;
                if (w.Have)
                {
                    float slL = Vector3.ProjectOnPlane(ltP - w.PrevLT, Vector3.up).magnitude / Dt, slR = Vector3.ProjectOnPlane(rtP - w.PrevRT, Vector3.up).magnitude / Dt;
                    if (walking)
                    {
                        Add("walk-in view speed m/s", vs); Cnt("walk-in frames");
                        if (!plantL && !plantR) Cnt("walk-in frames with neither foot planted");
                        if (plantL && w.PlantL) Add("walk-in planted-foot slide m/s per frame", slL);
                        if (plantR && w.PlantR) Add("walk-in planted-foot slide m/s per frame", slR);
                    }
                    _wkVs = vs; WalkFoot(w, 0, plantL, slL, ltP, walking); WalkFoot(w, 1, plantR, slR, rtP, walking);
                }
                if (!w.Have) { w.AL = ltP; w.AR = rtP; w.DrL = w.DrR = 0f; w.CntL = w.CntR = 0; w.MinL = w.MinR = 99f; w.WalkL = w.WalkR = false; }
                w.PrevLT = ltP; w.PrevRT = rtP; w.PlantL = plantL; w.PlantR = plantR; w.Have = true;
            }
        }

        float _wkVs;
        void WalkFoot(Wk w, int foot, bool plant, float slide, Vector3 toe, bool walking)
        {
            bool was = foot == 0 ? w.PlantL : w.PlantR;
            if (plant)
            {
                if (!was) { if (foot == 0) { w.CntL = 0; w.MinL = 99f; w.AL = toe; w.DrL = 0f; w.WalkL = true; } else { w.CntR = 0; w.MinR = 99f; w.AR = toe; w.DrR = 0f; w.WalkR = true; } }
                if (foot == 0) { w.CntL++; if (was && slide < w.MinL) w.MinL = slide; w.WalkL &= walking; float d = Vector3.ProjectOnPlane(toe - w.AL, Vector3.up).magnitude; if (d > w.DrL) w.DrL = d; }
                else { w.CntR++; if (was && slide < w.MinR) w.MinR = slide; w.WalkR &= walking; float d = Vector3.ProjectOnPlane(toe - w.AR, Vector3.up).magnitude; if (d > w.DrR) w.DrR = d; }
            }
            else if (was)
            {
                int cnt = foot == 0 ? w.CntL : w.CntR; float mn = foot == 0 ? w.MinL : w.MinR, dr = foot == 0 ? w.DrL : w.DrR; bool wk = foot == 0 ? w.WalkL : w.WalkR;
                if (wk && cnt >= 3 && mn < 98f) { Add("walk-in planted-foot slide m/s per contact (slowest frame)", mn); Add("walk-in planted-interval drift m", dr); Cnt("walk-in contacts"); string bin = _wkVs < 1.2f ? "0.35-1.2" : (_wkVs < 2f ? "1.2-2" : (_wkVs < 3f ? "2-3" : (_wkVs < 4f ? "3-4" : "4+"))); Add("walk-in planted-foot slide per contact by view speed " + bin + " m/s", mn); Add("walk-in planted-interval drift by view speed " + bin + " m/s", dr); }
            }
        }
    }
}

namespace PixFootball.EditorTools
{
    public sealed partial class ChoreoAuditRunner
    {
        int _soleTick; Mesh _soleMesh; readonly List<Vector3> _soleVerts = new List<Vector3>();

        /// <summary>v0.12.0 CONTACT: lowest world y (relative to gy) of the drawn skinned-mesh vertices within 0.28 m (horizontal) of each ankle: the visible sole, as MatchWatchAudit's float probe measures it.</summary>
        bool SoleMeshMin(SimAgentView v, Transform lf, Transform rf, float gy, out float ls, out float rs)
        {
            if (_soleMesh == null) _soleMesh = new Mesh();
            float bl = float.PositiveInfinity, br = float.PositiveInfinity; Vector3 cl = lf.position, cr = rf.position; const float R2 = 0.28f * 0.28f;
            foreach (var smr in v.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy) continue;
                smr.BakeMesh(_soleMesh, true); _soleMesh.GetVertices(_soleVerts);
                var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                for (int k = 0; k < _soleVerts.Count; k++)
                {
                    Vector3 w = m.MultiplyPoint3x4(_soleVerts[k]);
                    float dx = w.x - cl.x, dz = w.z - cl.z; if (dx * dx + dz * dz <= R2 && w.y < bl) bl = w.y;
                    dx = w.x - cr.x; dz = w.z - cr.z; if (dx * dx + dz * dz <= R2 && w.y < br) br = w.y;
                }
            }
            ls = float.IsPositiveInfinity(bl) ? float.NaN : bl - gy; rs = float.IsPositiveInfinity(br) ? float.NaN : br - gy;
            return !float.IsNaN(ls) && !float.IsNaN(rs);
        }
    }
}
