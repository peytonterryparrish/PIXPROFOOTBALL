using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// One controllable ball carrier: momentum locomotion (ILocomotionSource), ball-carrier moves, animation and footsteps.
    /// The body is simulated at a fixed 60 Hz and the visual transform is interpolated.
    /// </summary>
    public class PlayerController : MonoBehaviour
    {
        public const float Step = 1f / 60f;
        public const float MetersPerSecondToMph = 2.23694f;
        // Pitch plus a safety margin (metres)
        public static readonly Vector2 FieldMin = new Vector2(-30f, -62f), FieldMax = new Vector2(30f, 62f);

        public SimAgentView View;
        public PlayerAvatar Avatar;
        public LocomotionAnimator Anim;
        public ILocomotionSource Loco;
        public CameraRig Cameras;
        public AudioSource StepAudio;

        float _acc;
        Vector3 _prevPos, _curPos, _prevVel, _curVel;
        float _visYaw;
        float _lookYaw;
        float _lookPitch;
        PlayerMove _move;
        float _moveElapsed, _moveLock;
        float _mulValue = 1f, _mulUntil;
        float _celebrateTime;
        bool _celebrating;
        float _stepDistance;
        float _time;
        bool _protect;
        float _spinSign = 1f;
        AudioClip _stepClip;

        public Vector3 Position { get { return transform.position; } }
        public Vector3 Velocity { get { return Vector3.Lerp(_prevVel, _curVel, _acc / Step); } }
        public Vector3 Facing { get { return Loco != null ? Loco.Facing : Vector3.forward; } }
        public float SpeedMph { get { return Velocity.magnitude * MetersPerSecondToMph; } }
        public float Energy01 { get { return Loco != null ? Loco.Energy01 : 1f; } }
        public PlayerMove CurrentMove { get { return _move; } }
        public float LookYaw { get { return _lookYaw; } }
        public float LookPitch { get { return _lookPitch; } }
        public bool Celebrating { get { return _celebrating; } }
        public bool FirstPersonLook { get; set; }
        public void SetLook(float yaw, float pitch) { _lookYaw = yaw; _lookPitch = pitch; }
        public Transform Head { get { return Avatar != null ? Avatar.Head : null; } }

        public static PlayerController Spawn(Transform parent, Vector3 position, float yaw, TeamColors colors)
        {
            var go = new GameObject("PlayerController");
            go.transform.SetParent(parent, false);
            var pc = go.AddComponent<PlayerController>();
            pc.View = SimAgentView.Create(go.transform, PlayerSize.Medium, colors, "AgentView");
            pc.Avatar = pc.View.Avatar; pc.Anim = pc.View.Anim;
            pc.Loco = new AgentBodyLocomotion(AgentBodyLocomotion.DefaultRatings());
            var lib = PixRuntimeAssets.Load();
            if (lib != null && lib.footstep != null)
            {
                pc.StepAudio = go.AddComponent<AudioSource>();
                pc.StepAudio.playOnAwake = false; pc.StepAudio.spatialBlend = 0f; pc.StepAudio.volume = 0.35f;
                pc._stepClip = lib.footstep;
            }
            pc.ResetTo(position, yaw);
            return pc;
        }

        public void ResetTo(Vector3 position, float yaw)
        {
            if (Loco == null) return;
            Loco.Teleport(position, yaw);
            _prevPos = _curPos = position; _prevVel = _curVel = Vector3.zero; _acc = 0f;
            _visYaw = yaw; if (View != null) View.SnapYaw(yaw); _lookYaw = yaw; _lookPitch = 0f;
            _move = PlayerMove.None; _moveLock = 0f; _celebrating = false; _mulValue = 1f;
            transform.position = position; transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (Anim != null) { Anim.SetCelebrating(false); }
        }

        float BasisYaw
        {
            get
            {
                if (FirstPersonLook) return _lookYaw;
                return Cameras != null ? Cameras.MoveBasisYaw : 0f;
            }
        }

        /// <summary>Advance the player by one rendered frame.</summary>
        public void Tick(InputFrame f, float dt)
        {
            if (Loco == null) return;
            _time += dt;

            // ---- look (first person) ----
            if (FirstPersonLook)
            {
                _lookYaw += f.LookDelta.x;
                _lookPitch = Mathf.Clamp(_lookPitch - f.LookDelta.y, -35f, 35f);
            }

            // ---- world move vector ----
            Vector2 stick = f.Move;
            Vector2 worldMove;
            if (f.MoveIsWorld) worldMove = stick;
            else
            {
                float yaw = BasisYaw * Mathf.Deg2Rad;
                float s = Mathf.Sin(yaw), c = Mathf.Cos(yaw);
                // stick (x right, y up) -> world XZ in camera frame (camera looks along (sin yaw, cos yaw))
                worldMove = new Vector2(stick.x * c + stick.y * s, -stick.x * s + stick.y * c);
            }

            // ---- celebrate toggle / cancel ----
            if (f.Celebrate && !_celebrating && !Loco.IsDown && Loco.Velocity.magnitude < 2.5f) { _celebrating = true; _celebrateTime = 0f; Anim.SetCelebrating(true); }
            if (_celebrating)
            {
                _celebrateTime += dt;
                if (worldMove.magnitude > 0.5f || f.Hurdle || f.Dive || f.Spin) { _celebrating = false; Anim.SetCelebrating(false); }
                else worldMove = Vector2.zero;
            }

            // ---- moves ----
            _moveLock -= dt;
            if (_move != PlayerMove.None) { _moveElapsed += dt; if (_moveElapsed > PlayerMoves.Get(_move).Lock) _move = PlayerMove.None; }
            if (_moveLock <= 0f && !Loco.IsDown && !_celebrating)
            {
                PlayerMove m = PlayerMove.None;
                if (f.Dive) m = PlayerMove.Dive;
                else if (f.Hurdle) m = PlayerMove.Hurdle;
                else if (f.Spin) m = PlayerMove.Spin;
                else if (f.Truck) m = PlayerMove.Truck;
                else if (f.JukeLeft) m = PlayerMove.JukeLeft;
                else if (f.JukeRight) m = PlayerMove.JukeRight;
                else if (f.StiffArmLeft) m = PlayerMove.StiffArmLeft;
                else if (f.StiffArmRight) m = PlayerMove.StiffArmRight;
                if (m != PlayerMove.None) StartMove(m, worldMove);
            }
            _protect = f.Protect && !Loco.IsDown;
            Anim.SetProtect(_protect);

            // ---- step the body ----
            var li = new LocomotionInput { Move = Loco.IsDown ? Vector2.zero : worldMove, Sprint = f.Sprint, Protect = _protect };
            if (FirstPersonLook)
            {
                float r = _lookYaw * Mathf.Deg2Rad;
                li.HasFace = true; li.FaceDir = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
            }
            _acc += dt;
            int guard = 0;
            while (_acc >= Step && guard++ < 8)
            {
                _acc -= Step;
                _prevPos = _curPos; _prevVel = _curVel;
                float mul = (_time < _mulUntil ? _mulValue : 1f) * (_protect ? 0.92f : 1f);
                Loco.SetSpeedMultiplier(mul);
                Loco.Step(Step, li);
                Loco.ClampPosition(FieldMin, FieldMax);
                _curPos = Loco.Position; _curVel = Loco.Velocity;
            }
            if (guard >= 8) _acc = 0f;

            float a = Mathf.Clamp01(_acc / Step);
            Vector3 pos = Vector3.Lerp(_prevPos, _curPos, a);
            transform.position = pos;
            Vector3 facing = Loco.Facing;
            View.Apply(pos, Velocity, facing, PixFootball.Sim.AgentAction.Run, 0f, dt);
            float spinOff = 0f;
            if (_move == PlayerMove.Spin) { float u = Mathf.Clamp01(_moveElapsed / 0.6f); spinOff = _spinSign * 360f * (u * u * (3f - 2f * u)); }
            if (Avatar != null) Avatar.SetVisualYaw(spinOff);
            _visYaw = View.transform.eulerAngles.y;
            Footsteps(Velocity.magnitude, dt);
        }

        void StartMove(PlayerMove m, Vector2 worldMove)
        {
            var spec = PlayerMoves.Get(m);
            Vector3 vel = Loco.Velocity;
            float speed = vel.magnitude;
            Vector3 fwd = speed > 1f ? vel.normalized : Loco.Facing;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float sp01 = Mathf.Clamp01(speed / Mathf.Max(1f, Loco.TopSpeed));
            switch (m)
            {
                case PlayerMove.JukeLeft:
                case PlayerMove.JukeRight:
                {
                    float sign = m == PlayerMove.JukeLeft ? -1f : 1f;
                    Loco.Kick(right * sign * (3.0f + 2.2f * sp01));
                    Mul(0.88f, 0.45f);
                    break;
                }
                case PlayerMove.Spin:
                {
                    float sign = Vector3.Dot(new Vector3(worldMove.x, 0f, worldMove.y), right) < -0.2f ? -1f : 1f;
                    _spinSign = sign;
                    Loco.Kick(right * sign * (2.2f + 1.8f * sp01));
                    Mul(0.84f, 0.75f);
                    break;
                }
                case PlayerMove.StiffArmLeft:
                case PlayerMove.StiffArmRight:
                    Mul(0.94f, 0.9f); break;
                case PlayerMove.Truck:
                    Loco.Kick(fwd * (1.0f + 0.8f * sp01)); Mul(0.9f, 0.9f); break;
                case PlayerMove.Hurdle:
                    Mul(0.97f, 0.8f); break;
                case PlayerMove.Dive:
                    Loco.Kick(fwd * (1.6f + 1.2f * sp01)); Loco.Fall(1.7f); break;
            }
            _move = m; _moveElapsed = 0f; _moveLock = Mathf.Max(0.2f, spec.Lock);
            Anim.Trigger(m);
        }

        void Mul(float value, float duration) { _mulValue = value; _mulUntil = _time + duration; }

        void Footsteps(float speed, float dt)
        {
            if (StepAudio == null || _stepClip == null || speed < 1.2f || Loco.IsDown) return;
            _stepDistance += speed * dt;
            float stride = Mathf.Lerp(1.3f, 2.5f, Mathf.Clamp01(speed / 9f));
            if (_stepDistance >= stride)
            {
                _stepDistance = 0f;
                StepAudio.pitch = Random.Range(0.92f, 1.08f);
                StepAudio.PlayOneShot(_stepClip, Mathf.Lerp(0.25f, 0.7f, Mathf.Clamp01(speed / 9f)));
            }
        }
    }
}
