using UnityEngine;
using UnityEngine.InputSystem;

namespace PixFootball.Gameplay
{
    /// <summary>One frame of player intent (from devices, or scripted by the autotest).</summary>
    public struct InputFrame
    {
        /// <summary>Raw left stick / WASD (x right, y up on the stick).</summary>
        public Vector2 Move;
        /// <summary>Look in degrees this frame (yaw, pitch): mouse delta, or right stick rate * dt.</summary>
        public Vector2 LookDelta;
        public bool Sprint, Protect;
        public bool Hurdle, Spin, Dive, Truck, JukeLeft, JukeRight, StiffArmLeft, StiffArmRight, Celebrate;
        public bool CycleCamera, Pause, ToggleLighting, ResetPlayer;
        /// <summary>When set the movement vector is already a world-space XZ direction (autotest).</summary>
        public bool MoveIsWorld;
        public bool UsingGamepad;
    }

    /// <summary>Wraps the generated PixControls asset: polls actions and turns right-stick flicks into juke / truck / protect.</summary>
    public sealed class PixInput
    {
        static PixInput _inst;
        public static PixInput Instance { get { return _inst ?? (_inst = new PixInput()); } }

        readonly PixControls _controls;
        float _prevRsMag;
        float _flickCooldown;

        PixInput()
        {
            _controls = new PixControls();
            _controls.Enable();
            Application.quitting += () => { if (_controls != null) _controls.Dispose(); _inst = null; };
        }

        public PixControls Controls { get { return _controls; } }

        public static string ActiveScheme { get; private set; } = "Gamepad";

        /// <param name="flicks">convert right stick flicks into moves (off in first person where the stick looks)</param>
        public InputFrame Poll(float dt, bool flicks)
        {
            var g = _controls.Gameplay;
            var f = new InputFrame();
            f.Move = g.Move.ReadValue<Vector2>();
            f.Sprint = g.Sprint.IsPressed();
            f.Protect = g.Protect.IsPressed();
            f.Hurdle = g.Hurdle.WasPressedThisFrame();
            f.Spin = g.Spin.WasPressedThisFrame();
            f.Dive = g.Dive.WasPressedThisFrame();
            f.Truck = g.Truck.WasPressedThisFrame();
            f.JukeLeft = g.JukeLeft.WasPressedThisFrame();
            f.JukeRight = g.JukeRight.WasPressedThisFrame();
            f.StiffArmLeft = g.StiffArmLeft.WasPressedThisFrame();
            f.StiffArmRight = g.StiffArmRight.WasPressedThisFrame();
            f.Celebrate = g.Celebrate.WasPressedThisFrame();
            f.CycleCamera = g.CycleCamera.WasPressedThisFrame();
            f.Pause = g.Pause.WasPressedThisFrame();
            f.ToggleLighting = g.ToggleLighting.WasPressedThisFrame();
            f.ResetPlayer = g.ResetPlayer.WasPressedThisFrame();

            // last used device decides glyphs and look handling
            var look = g.Look.ReadValue<Vector2>();
            var ctrl = g.Look.activeControl;
            bool mouseLook = ctrl != null && ctrl.device is Mouse;
            var pad = Gamepad.current;
            if (pad != null && (pad.wasUpdatedThisFrame)) ActiveScheme = "Gamepad";
            if (Keyboard.current != null && Keyboard.current.wasUpdatedThisFrame) ActiveScheme = "KeyboardMouse";
            f.UsingGamepad = ActiveScheme == "Gamepad";

            if (mouseLook) f.LookDelta = new Vector2(look.x * 2f, look.y * 2f);
            else f.LookDelta = new Vector2(look.x * 150f * dt, look.y * 90f * dt);
            f.LookDelta *= PixSettings.LookSensitivity;
            if (PixSettings.InvertLookY) f.LookDelta.y = -f.LookDelta.y;

            // right-stick flicks (NCAA style): left/right = juke, up = truck, down (held) = protect the ball
            if (flicks && !mouseLook)
            {
                float mag = look.magnitude;
                _flickCooldown -= dt;
                if (mag > 0.78f && _prevRsMag < 0.45f && _flickCooldown <= 0f)
                {
                    if (Mathf.Abs(look.x) > Mathf.Abs(look.y)) { if (look.x < 0) f.JukeLeft = true; else f.JukeRight = true; }
                    else if (look.y > 0f) f.Truck = true;
                    _flickCooldown = 0.25f;
                }
                if (look.y < -0.6f && Mathf.Abs(look.x) < 0.7f) f.Protect = true;
                _prevRsMag = mag;
            }
            return f;
        }
    }
}
