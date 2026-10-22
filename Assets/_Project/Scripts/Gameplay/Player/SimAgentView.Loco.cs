using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 LOCOMOTION (LOCO-005): the view side of the gait system. Feeds the simulated velocity (authoritative: no smoothing between the simulation and the feet) to the gait synthesizer,
    /// hands its contact / warp state to the planted-foot solver in BodyMotion and turns the body so the playing cycles travel along the simulated velocity.
    /// </summary>
    public partial class SimAgentView
    {
        float _legScale = -1f, _gaitBias;
        /// <summary>QA: switch the whole gait system off (the "before" state: animator blend tree only).</summary>
        public static bool DebugNoGait;

        /// <summary>QA: -pixLocoOff on the command line plays the match with the v0.8 locomotion (animator blend tree, v0.4 foot lock) for before / after measurements.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LocoSwitch()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (System.Array.IndexOf(args, "-pixLocoOff") >= 0) { DebugNoGait = true; FootPlant.Enabled = false; ActionAnimator.DisableGait = true; DebugNoActionGround = true; }
            if (System.Array.IndexOf(args, "-pixLocoNoActionGround") >= 0) DebugNoActionGround = true;
            if (System.Array.IndexOf(args, "-pixLocoNoPlant") >= 0) FootPlant.Enabled = false;
            if (System.Array.IndexOf(args, "-pixLocoNoPolish") >= 0) FootPlant.Polish = false;
            if (System.Array.IndexOf(args, "-pixLocoNoWarp") >= 0) FootPlant.DebugNoWarp = true;
            if (System.Array.IndexOf(args, "-pixLocoNoIK") >= 0) FootPlant.DebugNoIK = true;
            if (System.Array.IndexOf(args, "-pixLocoNoDonor") >= 0) GaitPlayer.DebugNoDonor = true;
        }
        public float GaitBias { get { return _gaitBias; } }
        public GaitPlayer Gait { get { return Rig != null ? Rig.Gait : null; } }
        /// <summary>QA: the speed scale of this body (library native speeds are multiplied by it).</summary>
        public float GaitLegScale { get { if (_legScale < 0f) _legScale = MeasureLegScale(); return _legScale; } }

        static bool GaitAction(AgentAction a)
        {
            switch (a)
            {
                case AgentAction.Run: case AgentAction.Sprint: case AgentAction.Carry: case AgentAction.Route: case AgentAction.Idle: case AgentAction.Backpedal: case AgentAction.Shuffle:
                case AgentAction.Juke: case AgentAction.StiffArm: case AgentAction.Truck: case AgentAction.Hurdle: case AgentAction.Dive: return true;
                default: return false;
            }
        }

        float MeasureLegScale()
        {
            var lib = LocoCycleLibrary.Load();
            var a = Avatar != null ? Avatar.Animator : null;
            if (lib == null || a == null || !a.isHuman) return 1f;
            Transform u = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg), l = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg), f = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            if (u == null || l == null || f == null) return 1f;
            float len = Vector3.Distance(u.position, l.position) + Vector3.Distance(l.position, f.position);
            int si = Mathf.Clamp((int)Size, 0, 2);
            // the speed scale of this body size was measured on the same takes (build time); a body variant with longer or shorter legs than the reference of its size scales with them
            float sz = lib.sizeScale != null && lib.sizeScale.Length == 3 ? lib.sizeScale[si] : 1f;
            float refLeg = lib.legLength != null && lib.legLength.Length == 3 && lib.legLength[si] > 0.2f ? lib.legLength[si] : lib.refLegLength;
            return Mathf.Clamp(sz * (len > 0.2f ? len / refLeg : 1f), 0.6f, 1.6f);
        }

        void DriveGait(Vector3 velocity, Vector3 facing, AgentAction action, bool carrying, float dt)
        {
            var g = Gait;
            var plant = Motion != null ? Motion.Plant : null;
            if (plant != null) plant.GroundY = transform.position.y;
            if (Motion != null) Motion.ArmSwingEnabled = !carrying && g != null && g.Ready && !DebugNoGait;
            if (g == null || !g.Ready || DebugNoGait)
            {
                if (plant != null) { plant.Warp = 1f; plant.HintLeft = plant.HintRight = -1; plant.Speed = new Vector2(velocity.x, velocity.z).magnitude; plant.TravelDir = new Vector2(velocity.x, velocity.z); }
                _gaitBias = Mathf.MoveTowards(_gaitBias, 0f, 120f * dt);
                return;
            }
            if (_legScale < 0f) _legScale = MeasureLegScale();
            Vector3 fwd = new Vector3(facing.x, 0f, facing.z); fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var local = new Vector2(Vector3.Dot(velocity, right), Vector3.Dot(velocity, fwd));
            bool owned = !ContactDriven && !(Ragdoll != null && Ragdoll.Active) && Rig.Weight < 0.9f && GaitAction(action);
            g.SizeKey = (int)Size;
            if (plant != null) g.Feedback(plant, new Vector2(velocity.x, velocity.z).magnitude, dt);
            g.Update(dt, local, carrying, _legScale, owned);
            if (plant != null) g.Learn(plant, new Vector2(velocity.x, velocity.z), new Vector2(velocity.x, velocity.z).magnitude, dt);
            _gaitBias = Mathf.Lerp(_gaitBias, g.Layer > 0.3f ? g.YawBias : 0f, 1f - Mathf.Exp(-dt / 0.08f));
            if (plant != null)
            {
                Vector2 vh = new Vector2(velocity.x, velocity.z);
                plant.Speed = vh.magnitude; plant.TravelDir = vh;
                bool hinted = g.Layer > 0.35f;
                plant.Warp = g.Layer > 0.05f ? g.Warp : 1f;
                plant.HintLeft = hinted ? (g.LeftContact ? 1 : 0) : -1;
                plant.HintRight = hinted ? (g.RightContact ? 1 : 0) : -1;
                plant.GaitLayer = g.Layer;
            }
        }

        void ResetGait()
        {
            _gaitBias = 0f; _actOff = 0f;
            var g = Gait; if (g != null && g.Ready) g.Reset();
        }
    }
}
