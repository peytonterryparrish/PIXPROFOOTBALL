using System.Collections.Generic;
using System.Text;
using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>One body's health this frame (see <see cref="BodyCheck.Measure"/>).</summary>
    public struct BodyHealth
    {
        public bool Inactive, MissingPart, CulledWrong, NotDrawn, Floating, Sunk, TPose, Twisted, OffPlace, BadScale;
        public bool OnScreen, Drawn;
        /// <summary>Lowest sole above the turf (m, the bind pose = 0; the ankle or the ball of the foot, whichever is lower): floating when above +0.02, sunk when below -0.03. 9 = not measured.</summary>
        public float SoleGap;
        public int Lod;
        public float TwistDeg, ZeroMuscle;
        public string Detail;
        public bool Any { get { return Inactive || MissingPart || CulledWrong || NotDrawn || Floating || Sunk || TPose || Twisted || OffPlace || BadScale; } }
    }

    /// <summary>
    /// v0.11.0 CRITICAL: the body health checks of <see cref="BodyWatch"/> (the player / editor match probe) and of the editor stance audit: a view that is switched off, a visible part with no live renderer or mesh, a skinned mesh whose bounds are
    /// off screen while its skeleton is on screen (culled although seen), floating or sunken feet, a T / bind pose (arms out in a line, every muscle at zero), a twisted trunk, a body away from its simulated place.
    /// </summary>
    public static class BodyCheck
    {
        /// <summary>Height of the lowest sole above the turf, the way FootPlant measures it (ankle and toes against their bind-pose heights).</summary>
        public static float SoleGap(SimAgentView v)
        {
            var an = v.Avatar != null ? v.Avatar.Animator : null;
            if (an == null || !an.isHuman || v.Motion == null || v.Motion.Plant == null || !v.Motion.Plant.Ok) return 9f;
            var plant = v.Motion.Plant;
            Transform lf = an.GetBoneTransform(HumanBodyBones.LeftFoot), rf = an.GetBoneTransform(HumanBodyBones.RightFoot), lt = an.GetBoneTransform(HumanBodyBones.LeftToes), rt = an.GetBoneTransform(HumanBodyBones.RightToes);
            if (lf == null || rf == null) return 9f;
            float gy = v.transform.position.y;
            float low = Mathf.Min(lf.position.y - gy - plant.RestAnkleL, rf.position.y - gy - plant.RestAnkleR);
            if (lt != null && rt != null) low = Mathf.Min(low, Mathf.Min(lt.position.y - gy - plant.RestToeL, rt.position.y - gy - plant.RestToeR));
            return low;
        }

        static HumanPose _pose;
        static readonly Dictionary<Animator, HumanPoseHandler> Handlers = new Dictionary<Animator, HumanPoseHandler>();

        /// <param name="planes">frustum planes of <paramref name="cam"/> (GeometryUtility.CalculateFrustumPlanes)</param>
        /// <param name="checkMuscles">also test the muscle space for a bind pose (a few microseconds per body)</param>
        public static BodyHealth Measure(SimAgentView v, Camera cam, Plane[] planes, bool checkMuscles)
        {
            var r = new BodyHealth { SoleGap = 9f, Lod = -1 };
            var sb = new StringBuilder();
            r.Inactive = !v.gameObject.activeInHierarchy || v.Avatar == null || v.Avatar.Root == null || !v.Avatar.Root.gameObject.activeInHierarchy;
            if (r.Inactive) { r.Detail = " inactive"; return r; }
            var av = v.Avatar; var photo = av.Photo; var an = av.Animator;
            Vector3 rootPos = v.transform.position;
            Vector3 ls = av.Root.lossyScale;
            if (float.IsNaN(rootPos.x) || float.IsNaN(rootPos.y) || ls.x < 0.5f || ls.y < 0.5f || ls.z < 0.5f || ls.x > 2f || ls.y > 2f) { r.BadScale = true; sb.Append(" scale=" + ls.ToString("0.00")); }
            SkinnedMeshRenderer bodyBounds = null;
            if (photo != null)
            {
                int lvl = Mathf.Max(0, photo.Level); r.Lod = lvl;
                foreach (var p in photo.parts)
                {
                    if (p == null || p.lods == null || p.lods.Length == 0 || !p.visible) continue;
                    int pick = lvl < p.lods.Length ? lvl : (p.hideBeyond ? -1 : p.lods.Length - 1);
                    if (pick < 0) continue;
                    var rr = p.lods[pick];
                    bool ok = rr != null && rr.gameObject.activeInHierarchy && rr.enabled;
                    if (ok) { var smr = rr as SkinnedMeshRenderer; if (smr != null && smr.sharedMesh == null) ok = false; if (smr != null && p.key == "Jersey") bodyBounds = smr; }
                    if (!ok) { r.MissingPart = true; sb.Append(" part:" + p.key + "@L" + lvl); }
                }
            }
            Transform hips = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Hips) : null;
            Transform lf = av.LeftFoot, rf = av.RightFoot, head = av.Head, lh = av.LeftHand, rh = av.RightHand;
            if (hips == null || lf == null || rf == null || head == null || lh == null || rh == null) { r.Detail = sb.ToString(); return r; }
            Vector3 hp = hips.position;
            Vector3 flat = hp - rootPos; flat.y = 0f;
            Transform[] ts = { hips, lf, rf, head, lh, rh };
            foreach (var bt in ts)
            {
                Vector3 bp = bt.position; float s = bp.x + bp.y + bp.z;
                if (float.IsNaN(s) || float.IsInfinity(s) || (bp - rootPos).sqrMagnitude > 25f) { r.BadScale = true; sb.Append(" bone " + bt.name + " at " + bp.ToString("0.0")); break; }
            }
            // (written so that a NaN fails the test: every comparison with NaN is false)
            if (!(flat.magnitude <= 3.2f) || !(hp.y >= 0.12f) || !(hp.y <= 3.2f) || !(Mathf.Abs(rootPos.y) <= 1.5f)) { r.OffPlace = true; sb.Append(" hips=" + (hp - rootPos).ToString("0.00")); }
            if (cam != null)
                for (int k = 0; k < ts.Length; k++) { Vector3 vp = cam.WorldToViewportPoint(ts[k].position); if (vp.z > 0f && vp.x > 0.01f && vp.x < 0.99f && vp.y > 0.01f && vp.y < 0.99f) { r.OnScreen = true; break; } }
            bool boundsIn = true;
            if (bodyBounds != null && planes != null)
            {
                Bounds wb = bodyBounds.bounds;
                boundsIn = GeometryUtility.TestPlanesAABB(planes, wb);
                if (r.OnScreen && !boundsIn) { r.CulledWrong = true; sb.Append(" bounds=" + wb.center.ToString("0.0") + "/" + wb.size.ToString("0.0")); }
            }
            if (photo != null)
            {
                foreach (var p in photo.parts)
                {
                    if (p == null || p.lods == null || (p.key != "Jersey" && p.key != "Pants" && p.key != "Helmet_Shell" && p.key != "Head")) continue;
                    foreach (var rr in p.lods) if (rr != null && rr.gameObject.activeInHierarchy && rr.isVisible) { r.Drawn = true; break; }
                    if (r.Drawn) break;
                }
                if (!r.Drawn && r.OnScreen && boundsIn) r.NotDrawn = true;
            }
            r.SoleGap = SoleGap(v);
            if (r.SoleGap < 8f) { if (r.SoleGap > 0.04f) r.Floating = true; if (r.SoleGap < -0.05f) r.Sunk = true; if (r.Floating || r.Sunk) sb.Append(" sole=" + r.SoleGap.ToString("0.000")); }
            Transform lua = an.GetBoneTransform(HumanBodyBones.LeftUpperArm), rua = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
            if (lua != null && rua != null)
            {
                Vector3 across = rua.position - lua.position; float shoulderW = across.magnitude;
                Vector3 wl = lh.position - lua.position, wr = rh.position - rua.position;
                float armL = wl.magnitude, armR = wr.magnitude;
                Vector3 axis = across / Mathf.Max(0.01f, shoulderW);
                float hl = Mathf.Abs(Vector3.Dot(wl, axis)) / Mathf.Max(0.01f, armL), hr = Mathf.Abs(Vector3.Dot(wr, axis)) / Mathf.Max(0.01f, armR);
                bool outL = Vector3.Dot(wl, -axis) > 0f, outR = Vector3.Dot(wr, axis) > 0f;
                if (hl > 0.86f && hr > 0.86f && outL && outR && Mathf.Abs(wl.y) < 0.22f && Mathf.Abs(wr.y) < 0.22f && armL > 0.62f && armR > 0.62f) { r.TPose = true; sb.Append(" T:" + armL.ToString("0.00") + "/" + armR.ToString("0.00")); }
                if (!r.TPose && checkMuscles && an.isHuman && an.avatar != null && an.avatar.isValid)
                {
                    HumanPoseHandler h; if (!Handlers.TryGetValue(an, out h)) Handlers[an] = h = new HumanPoseHandler(an.avatar, an.transform);
                    h.GetHumanPose(ref _pose);
                    float sum = 0f; int n = Mathf.Min(_pose.muscles.Length, 55);
                    for (int m = 0; m < n; m++) sum += Mathf.Abs(_pose.muscles[m]);
                    r.ZeroMuscle = sum / n;
                    if (r.ZeroMuscle < 0.02f) { r.TPose = true; sb.Append(" zeroMuscles=" + r.ZeroMuscle.ToString("0.000")); }
                }
            }
            Transform lul = an.GetBoneTransform(HumanBodyBones.LeftUpperLeg), rul = an.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (lua != null && rua != null && lul != null && rul != null)
            {
                Vector3 sh = rua.position - lua.position; sh.y = 0f; Vector3 hh = rul.position - lul.position; hh.y = 0f;
                if (sh.sqrMagnitude > 1e-4f && hh.sqrMagnitude > 1e-4f) { r.TwistDeg = Vector3.SignedAngle(hh, sh, Vector3.up); if (Mathf.Abs(r.TwistDeg) > 62f) { r.Twisted = true; sb.Append(" twist=" + r.TwistDeg.ToString("0")); } }
            }
            r.Detail = sb.ToString();
            return r;
        }

        /// <summary>The simulated action keeps the feet on the turf (a jump, dive, catch in the air, tackle or fall does not).</summary>
        public static bool FeetOnTurfAction(Sim.AgentAction a)
        {
            switch (a)
            {
                case Sim.AgentAction.Dive: case Sim.AgentAction.Hurdle: case Sim.AgentAction.Jump: case Sim.AgentAction.Catch: case Sim.AgentAction.TackleDive: case Sim.AgentAction.TackleBigHit: case Sim.AgentAction.TackleWrapUp: case Sim.AgentAction.TackleShoestring: case Sim.AgentAction.TackleGang:
                case Sim.AgentAction.Tackled: case Sim.AgentAction.Fallen: case Sim.AgentAction.Stumble: case Sim.AgentAction.Celebrate: case Sim.AgentAction.Kick: case Sim.AgentAction.Throw: case Sim.AgentAction.Spin: case Sim.AgentAction.Truck: case Sim.AgentAction.Juke:
                    return false;
            }
            return true;
        }
    }
}
