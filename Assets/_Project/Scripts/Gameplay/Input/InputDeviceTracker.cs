using UnityEngine;
using UnityEngine.InputSystem;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Which device the player used last (controller or keyboard / mouse). Drives the glyph / keycap prompts everywhere. A device wins only when it produced REAL input this frame
    /// (a button press, a stick pushed past the dead zone, a key press, mouse movement beyond jitter): pads that stream reports every frame no longer pin the scheme to Gamepad.
    /// At startup the scheme follows the connected devices (controller if one is plugged in, otherwise keyboard).
    /// </summary>
    public static class InputDeviceTracker
    {
        static bool _init;
        static int _frame = -1;
        static bool _pad = true;

        /// <summary>True while the controller is the active device.</summary>
        public static bool GamepadActive { get { EnsureInit(); return _pad; } }

        /// <summary>v0.11: the unscaled time of the last real input (a button, a key, the stick); the HUD helper bar shows itself again after it.</summary>
        public static float LastActivityTime = -100f;

        /// <summary>Test / autotest hook: force a scheme.</summary>
        public static void Force(bool gamepad) { _init = true; _pad = gamepad; }

        static void EnsureInit() { if (_init) return; _init = true; _pad = Gamepad.current != null; }

        /// <summary>Call once per frame from anything that polls input (idempotent within a frame).</summary>
        public static void Update()
        {
            EnsureInit();
            int f = Time.frameCount;
            if (f == _frame) return;
            _frame = f;
            var pad = Gamepad.current;
            if (pad != null && PadActivity(pad)) { _pad = true; LastActivityTime = Time.unscaledTime; }
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) { _pad = false; LastActivityTime = Time.unscaledTime; }
            var m = Mouse.current;
            if (m != null && (m.delta.ReadValue().sqrMagnitude > 9f || m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame)) _pad = false;
        }

        /// <summary>Real (not jitter) activity on a pad this frame.</summary>
        public static bool PadActivity(Gamepad pad)
        {
            if (pad.leftStick.ReadValue().sqrMagnitude > 0.09f || pad.rightStick.ReadValue().sqrMagnitude > 0.09f) return true;
            if (pad.leftTrigger.ReadValue() > 0.3f || pad.rightTrigger.ReadValue() > 0.3f) return true;
            return pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.buttonWest.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame
                || pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame || pad.startButton.wasPressedThisFrame || pad.selectButton.wasPressedThisFrame
                || pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame;
        }
    }
}
