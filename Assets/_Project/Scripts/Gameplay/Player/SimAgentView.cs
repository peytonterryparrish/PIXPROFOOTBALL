using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>Coordinate conversion between the Sim (yards, X lateral 0..53.33, Y downfield 0..100) and Unity (metres, X lateral, Z downfield, centred on the pitch). Free roam only; the match uses <see cref="PlayFrame"/>.</summary>
    public static class SimSpace
    {
        public const float YardsToMeters = 0.9144f;
        public const float HalfWidthYd = 26.665f;
        public const float MidfieldYd = 50f;

        public static Vector3 ToUnity(Vec2 p) { return new Vector3((p.X - HalfWidthYd) * YardsToMeters, 0f, (p.Y - MidfieldYd) * YardsToMeters); }
        public static Vector3 ToUnity(Vec3 p) { return new Vector3((p.X - HalfWidthYd) * YardsToMeters, p.Z * YardsToMeters, (p.Y - MidfieldYd) * YardsToMeters); }
        public static Vec2 ToSim(Vector3 u) { return new Vec2(u.x / YardsToMeters + HalfWidthYd, u.z / YardsToMeters + MidfieldYd); }
        public static Vector3 VelToUnity(Vec2 v) { return new Vector3(v.X * YardsToMeters, 0f, v.Y * YardsToMeters); }
        public static Vector3 DirToUnity(Vec2 d) { return new Vector3(d.X, 0f, d.Y).normalized; }
    }

    /// <summary>
    /// Presentation of one simulated player: avatar + locomotion animator driven by position / velocity / facing / AgentAction.
    /// Free roam drives a single view directly; the match drives 22 of them (ApplyMatch) through a PlayableGraph so any mocap clip
    /// can be layered over the locomotion blend tree.
    /// </summary>
    public class SimAgentView : MonoBehaviour
    {
        public PlayerAvatar Avatar;
        public LocomotionAnimator Anim;
        public ActionAnimator Rig;
        public PlayerRole Role;
        public PlayerSize Size;
        public int AgentId = -1;
        AgentAction _lastAction = AgentAction.Idle;
        float _yaw;
        RushMove _lastRush;
        bool _ownsClip;
        float _kickT = -1f;
        /// <summary>The contact director owns this player right now (an authored pair take, a ragdoll or a get-up): ApplyMatch keeps its hands off the clip layer.</summary>
        public bool ContactDriven;
        public RagdollRig Ragdoll;
        /// <summary>Lean, foot lock and stiff-arm IK over the animation (v0.4).</summary>
        public BodyMotion Motion;
        /// <summary>v0.6: the pre-snap stance of this player for the current play (set by PlayStage.Begin from StanceRules).</summary>
        public StanceSpec Stance;
        /// <summary>v0.6: degrees the body is turned while the playing clip runs (ClipFacingTable): mocap clips are authored facing different ways, the simulation facing must win.</summary>
        float _clipYaw;
        public float ClipYaw { get { return _clipYaw; } }
        /// <summary>QA: switch the clip facing correction off (the gallery measures the "before" state with it).</summary>
        public static bool DebugNoClipFacing;

        public static SimAgentView Create(Transform parent, PlayerSize size, TeamColors colors, string name = "AgentView")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<SimAgentView>();
            v.Size = size;
            v.Avatar = PlayerAvatar.Spawn(go.transform, size, colors, "Avatar");
            v.Anim = go.AddComponent<LocomotionAnimator>();
            if (v.Avatar.Animator != null)
            {
                var rt = PixRuntimeAssets.Load();
                v.Anim.Init(v.Avatar.Animator, rt != null ? rt.playerController : null, rt != null ? rt.clipSpeeds : null);
            }
            return v;
        }

        /// <summary>Match view: locomotion controller inside an ActionAnimator graph.</summary>
        public static SimAgentView CreateForMatch(Transform parent, PlayerSize size, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<SimAgentView>();
            v.Size = size;
            v.Avatar = PlayerAvatar.Spawn(go.transform, size, TeamColors.Home, "Avatar");
            v.Anim = go.AddComponent<LocomotionAnimator>();
            v.Rig = go.AddComponent<ActionAnimator>();
            if (v.Avatar.Animator != null) v.Avatar.Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;   // players outside the camera are not posed
            v.Ragdoll = go.AddComponent<RagdollRig>(); v.Ragdoll.Setup(v.Avatar.Animator);
            v.Motion = new BodyMotion(v.Avatar);
            if (v.Avatar.Animator != null)
            {
                var rt = PixRuntimeAssets.Load();
                v.Anim.InitRig(v.Avatar.Animator, v.Rig, rt != null ? rt.playerController : null, rt != null ? rt.clipSpeeds : null);
            }
            return v;
        }

        /// <summary>v0.6 photoreal bodies: this pooled body becomes <paramref name="playerId"/> (face, skin, kit options are seeded by the id).</summary>
        /// <summary>Id of the player this pooled view last wore (int.MinValue = nobody yet).</summary>
        public int IdentityId = int.MinValue;

        public void SetIdentity(int playerId, PixFootball.Core.Position pos)
        {
            if (Avatar != null && playerId != IdentityId) Avatar.SetAppearance(playerId, RoleUtil.AppearanceRole(pos));
            IdentityId = playerId;
        }

        public void Dress(TeamColors uniform, TeamColors helmet)
        {
            if (Avatar == null) return;
            Avatar.SetColors(uniform); Avatar.SetHelmetColors(helmet);
        }

        /// <summary>Applies a simulation sample: world pose and animation state. lateralHint (-1 left, +1 right) disambiguates handed moves.</summary>
        public void Apply(Vector3 position, Vector3 velocity, Vector3 facing, AgentAction action, float lateralHint, float dt)
        {
            transform.position = position;
            float target = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            _yaw = Mathf.MoveTowardsAngle(_yaw, target, 900f * dt);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 f = new Vector3(Mathf.Sin(_yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(_yaw * Mathf.Deg2Rad));
            Anim.Drive(velocity, f, dt);
            if (action != _lastAction)
            {
                var move = MoveFor(action, lateralHint);
                if (move != PlayerMove.None) Anim.Trigger(move);
                Anim.SetCelebrating(action == AgentAction.Celebrate);
                _lastAction = action;
            }
        }


        /// <summary>v0.6 root cause fix of "OL / DL / CBs face backwards": the clip that is playing is authored facing some other way than the simulation facing, so the body is turned by the measured offset while the clip's weight is up.</summary>
        float _clipYawCur, _setback; bool _reaction;
        void ApplyClipFacing(float dt)
        {
            if (Avatar == null || Avatar.Root == null) return;
            // a new take changes the offset smoothly (no pop of the whole body while the poses cross-fade)
            _clipYawCur = Mathf.MoveTowardsAngle(_clipYawCur, _clipYaw, 900f * Mathf.Max(dt, 0.0001f));
            bool owned = !ContactDriven || _reaction;   // sideline reactions (cheer, frustration) are plain clips: they get their offset too
            float w = Rig != null && Rig.Valid && owned && !(Ragdoll != null && Ragdoll.Active) ? Mathf.Clamp01(Rig.Weight) : 0f;
            _tableYaw = DebugNoClipFacing ? 0f : _clipYawCur * w;
            Avatar.SetVisualYaw(_tableYaw + _loopYaw);
            // linemen in a 3-point stance lean over their simulated position: pull the body back so the helmets meet across the ball instead of overlapping it (QA-455)
            bool lineStance = _lastAction == AgentAction.Stance && (Stance.Kind == StanceKind.ThreePoint || Stance.Kind == StanceKind.CenterSnap || Stance.Kind == StanceKind.FourPoint);
            _setback = Mathf.MoveTowards(_setback, lineStance && !ContactDriven ? 0.36f : 0f, 1.6f * Mathf.Max(dt, 0.0001f));
            Avatar.transform.localPosition = new Vector3(0f, 0f, -_setback);
        }

        /// <summary>A clip started from outside the match animation logic (celebration reactions): turn the body by the clip's measured facing offset.</summary>
        public void NoteClip(AnimationClip clip) { _clipYaw = ClipFacingTable.Offset(clip); _reaction = true; }
        /// <summary>Contact director: places the view root and yaw for an authored take (after ApplyMatch has set the simulation pose).</summary>
        public void SetContactPose(Vector3 position, float yaw)
        {
            transform.position = position; _yaw = yaw; transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>Puts the view root under a ragdoll that could not be recovered with a get-up take.</summary>
        public void SnapToGround(Vector2 xz, float yaw)
        {
            transform.position = new Vector3(xz.x, transform.position.y, xz.y); _yaw = yaw; transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public void SnapYaw(float yaw) { _yaw = yaw; }
        /// <summary>The stance of this player changed (a flip, audible, shift or press moved him): play the new stance clip.</summary>
        public void ReplanStance(StanceSpec spec) { if (spec.Kind == Stance.Kind && spec.Mirror == Stance.Mirror && spec.IsLeftOfBall == Stance.IsLeftOfBall) return; Stance = spec; _lastAction = (AgentAction)(-1); }
        public float Yaw { get { return _yaw; } }
        public Vector3 Forward { get { return new Vector3(Mathf.Sin(_yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(_yaw * Mathf.Deg2Rad)); } }

        /// <summary>Resets animation state when the view is re-used for a new play.</summary>
        public void ResetForPlay(Vector3 position, float yaw)
        {
            transform.position = position; _yaw = yaw; transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            _lastAction = (AgentAction)(-1); _ownsClip = false; _kickT = -1f; _lastRush = RushMove.None; ContactDriven = false; _reaction = false; _clipYaw = 0f; _clipYawCur = 0f; _loopYaw = 0f; _tableYaw = 0f; _setback = 0f; if (Avatar != null) { Avatar.SetVisualYaw(0f); Avatar.transform.localPosition = Vector3.zero; }
            if (Ragdoll != null) Ragdoll.Cancel();
            if (Motion != null) Motion.Cancel();
            if (Rig != null) Rig.StopImmediate();
            if (Anim != null) Anim.Drive(Vector3.zero, Forward, 1f);
        }

        // ------------------------------------------------------------------------------------------ match
        public void ApplyMatch(Vector3 position, Vector3 velocity, Vector3 facing, AgentAction action, in AnimHints h, float lateralHint, bool specialTeams, bool shotgunQb, float dt)
        {
            transform.position = position;
            float target = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            float turnRate = (action == AgentAction.Tackled || action == AgentAction.Fallen) ? 120f : 720f;
            _yaw = Mathf.MoveTowardsAngle(_yaw, target, turnRate * dt);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 f = Forward;
            Anim.Drive(velocity, f, dt);
            Anim.SetProtect(h.Carrying);     // v0.8: ball carriers run with the ball tucked (Carry locomotion tree)
            if (Rig != null && !Rig.Valid) { Rig.EnsureBuilt(); if (!Rig.Valid) return; }   // the pooled player was just re-activated: wait for his graph
            _loopAllowed = LoopAction(action);
            ApplyClipFacing(dt);
            if (Motion != null)
            {
                Motion.Sample(velocity, f, dt);
                Motion.LocomotionWeight = (ContactDriven || (Ragdoll != null && Ragdoll.Active)) ? 0f : 1f - (Rig != null ? Mathf.Clamp01(Rig.Weight) : 0f);
            }

            if (ContactDriven) { _lastAction = action; _lastRush = h.Rush; return; }   // an authored take / ragdoll / get-up is running
            bool changed = action != _lastAction || (action == AgentAction.Engaged && h.Rush != _lastRush);
            if (!changed) return;
            var prevAction = _lastAction;
            _lastAction = action; _lastRush = h.Rush;

            ClipPlan plan = default(ClipPlan);
            bool owns = true;
            switch (action)
            {
                case AgentAction.Stance: plan = StanceClips.Plan(Stance, h.Seed, Role); break;
                case AgentAction.Block: plan = AnimKit.Block(h.Seed); break;
                case AgentAction.Engaged: plan = AnimKit.Engaged(h); break;
                case AgentAction.Shed: plan = AnimKit.Shed(h.Seed); break;
                case AgentAction.Catch: plan = AnimKit.Catch(h); break;
                case AgentAction.Throw: plan = AnimKit.Throw(h); break;
                case AgentAction.Handoff: plan = AnimKit.Handoff(h); break;
                case AgentAction.Jump: plan = AnimKit.Jump(h.Seed); break;
                case AgentAction.Spin: plan = AnimKit.Spin(h.Seed); break;
                case AgentAction.TackleWrapUp: case AgentAction.TackleBigHit: case AgentAction.TackleShoestring: case AgentAction.TackleDive: case AgentAction.TackleGang:
                    plan = AnimKit.Tackle(h, false); break;
                case AgentAction.Tackled: plan = h.Tackle != TackleType.None || h.Sack ? AnimKit.Tackle(h, true) : AnimKit.Fall(h.Seed); break;
                case AgentAction.Fallen: plan = AnimKit.Fall(h.Seed); break;
                case AgentAction.Celebrate: plan = AnimKit.Celebrate(h.Seed); break;
                case AgentAction.Kick: plan = AnimKit.Kick(h.Kind, h.Seed, false); if (!plan.Valid) { _kickT = 0f; owns = false; } break;     // v0.8: kicking mocap, the procedural swing stays as the fallback
                default: owns = false; break;
            }
            if (!plan.Valid && prevAction == AgentAction.Stance && action != AgentAction.Stance && action != AgentAction.Kick) plan = AnimKit.GetOff(Role, h.Seed);     // v0.8: explode out of the stance
            if (!plan.Valid && Role == PlayerRole.QB && (h.Kind == PixFootball.Sim.PlayKind.Kneel || h.Kind == PixFootball.Sim.PlayKind.Spike) && action != AgentAction.Stance) plan = AnimKit.QbSpecial(h.Kind, h.Seed);
            var mv = MoveFor(action, lateralHint);
            if (mv != PlayerMove.None && !(action == AgentAction.Spin && plan.Valid)) { Anim.Trigger(mv); owns = false; }
            if (plan.Valid && Rig != null) { Rig.Play(plan); _ownsClip = true; _clipYaw = ClipFacingTable.Offset(plan.Clip); }
            else if (_ownsClip && Rig != null) { Rig.Stop(0.18f); _ownsClip = false; }
        }

        /// <summary>
        /// Presentation-only contact correction applied after <see cref="ApplyMatch"/> for paired clips (tackles, engaged blocks): the engine owns the positions, but the
        /// paired takes were authored at a fixed distance, so the view steps toward its partner (offset, scaled by w) and optionally turns to face him.
        /// </summary>
        public void ApplyContact(Vector3 offset, float yawToward, float w, bool turn)
        {
            if (w <= 0f) return;
            transform.position += offset * w;
            if (turn) { _yaw = Mathf.LerpAngle(_yaw, yawToward, Mathf.Clamp01(w)); transform.rotation = Quaternion.Euler(0f, _yaw, 0f); }
        }

        /// <summary>
        /// v0.6 closed-loop facing: the pose of the playing animation (locomotion blends mix clips authored facing -60 to -135 degrees away, e.g. the quarterback shuffles used for back-pedal and strafe) is measured from the hips every frame
        /// and the body is turned so it faces the simulation heading. Only for plain locomotion / stance / block actions; takes that really turn the hips (throws, spins, tackles, falls) are left alone.
        /// </summary>
        float _loopYaw, _tableYaw; bool _loopAllowed;
        public float LoopYaw { get { return _loopYaw; } }
        public static bool DebugNoFacingLoop;

        static bool LoopAction(AgentAction a)
        {
            switch (a)
            {
                case AgentAction.Stance: case AgentAction.Idle: case AgentAction.Run: case AgentAction.Sprint: case AgentAction.Backpedal: case AgentAction.Shuffle:
                case AgentAction.Block: case AgentAction.Route: case AgentAction.Carry: return true;
                default: return false;
            }
        }

        void UpdateFacingLoop(float dt)
        {
            if (Avatar == null || Avatar.Animator == null || Avatar.Root == null || Rig == null || !Rig.Valid) return;
            bool allow = _loopAllowed && !ContactDriven && !DebugNoClipFacing && !DebugNoFacingLoop && !(Ragdoll != null && Ragdoll.Active);
            if (allow)
            {
                var pose = BodyHeading.Measure(Avatar.Animator);
                if (pose.Valid && pose.HipsHeight > 0.5f)
                {
                    float err = StanceRules.YawDelta(pose.Yaw, _yaw);
                    if (Mathf.Abs(err) > 8f) _loopYaw = Mathf.LerpAngle(_loopYaw, _loopYaw - err, 1f - Mathf.Exp(-dt / 0.09f));
                }
            }
            else _loopYaw = Mathf.MoveTowardsAngle(_loopYaw, 0f, 420f * dt);
            Avatar.SetVisualYaw(_tableYaw + _loopYaw);
        }

        /// <summary>Procedural leg swing for kicks and punts (no kicking mocap exists in the source set): hip and knee of the right leg are driven in LateUpdate over the running animation.</summary>
        int _lodTick;
        void LateUpdate()
        {
            if (Avatar != null && Avatar.Lod != null && ((++_lodTick + AgentId) & 3) == 0) Avatar.Lod.Update(transform.position);
            UpdateFacingLoop(Time.deltaTime);
            if (Motion != null) Motion.Late(Time.deltaTime);
            if (_kickT < 0f || Avatar == null || Avatar.RightUpperLeg == null || Avatar.RightLowerLeg == null) return;
            _kickT += Time.deltaTime;
            float t = _kickT;
            if (t > 0.75f) { _kickT = -1f; return; }
            // hip angle about the lateral axis (positive = leg forward), knee flexion (positive = foot back)
            float hip, knee, w = 1f;
            if (t < 0.12f) { float u = t / 0.12f; hip = Mathf.Lerp(-38f, 38f, u * u); knee = Mathf.Lerp(95f, 8f, u); }
            else if (t < 0.38f) { float u = (t - 0.12f) / 0.26f; hip = Mathf.Lerp(38f, 82f, 1f - (1f - u) * (1f - u)); knee = Mathf.Lerp(8f, 30f, u); }
            else { float u = (t - 0.38f) / 0.37f; w = 1f - u * u * (3f - 2f * u); hip = 82f; knee = 30f; }
            if (t < 0.06f) w = Mathf.Clamp01(t / 0.06f);
            Vector3 right = Vector3.Cross(Vector3.up, Forward);
            Avatar.RightUpperLeg.rotation = Quaternion.AngleAxis(-hip * w, right) * Avatar.RightUpperLeg.rotation;
            Avatar.RightLowerLeg.rotation = Quaternion.AngleAxis(knee * w, right) * Avatar.RightLowerLeg.rotation;
            if (Avatar.Chest != null) Avatar.Chest.rotation = Quaternion.AngleAxis(-10f * w, right) * Avatar.Chest.rotation;
        }

        /// <summary>Sim action to ball-carrier move (other actions are covered by locomotion).</summary>
        public static PlayerMove MoveFor(AgentAction a, float lateralHint)
        {
            switch (a)
            {
                case AgentAction.Juke: return lateralHint < 0f ? PlayerMove.JukeLeft : PlayerMove.JukeRight;
                case AgentAction.Spin: return PlayerMove.Spin;
                case AgentAction.StiffArm: return lateralHint < 0f ? PlayerMove.StiffArmLeft : PlayerMove.StiffArmRight;
                case AgentAction.Truck: return PlayerMove.Truck;
                case AgentAction.Hurdle: return PlayerMove.Hurdle;
                case AgentAction.Dive: return PlayerMove.Dive;
                default: return PlayerMove.None;
            }
        }
    }
}

