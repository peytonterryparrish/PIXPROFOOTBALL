using UnityEngine;
using UnityEngine.InputSystem;

namespace PixFootball.UI.Franchise
{
    /// <summary>The franchise screens' extra buttons (the A / B / d-pad come from the EventSystem). Gamepad button or the keyboard key of <see cref="UiPrompts.KeyOf"/>; polled once per frame by whoever owns the action.</summary>
    public enum PadBtn { X, Y, LB, RB, LT, RT }

    public static class FranchiseInput
    {
        public static bool Pressed(PadBtn b)
        {
            var pad = Gamepad.current;
            var kb = Keyboard.current;
            switch (b)
            {
                case PadBtn.X: return (pad != null && pad.buttonWest.wasPressedThisFrame) || (kb != null && kb.cKey.wasPressedThisFrame);
                case PadBtn.Y: return (pad != null && pad.buttonNorth.wasPressedThisFrame) || (kb != null && kb.rKey.wasPressedThisFrame);
                case PadBtn.LB: return (pad != null && pad.leftShoulder.wasPressedThisFrame) || (kb != null && (kb.zKey.wasPressedThisFrame || kb.pageUpKey.wasPressedThisFrame));
                case PadBtn.RB: return (pad != null && pad.rightShoulder.wasPressedThisFrame) || (kb != null && (kb.xKey.wasPressedThisFrame || kb.pageDownKey.wasPressedThisFrame));
                case PadBtn.LT: return (pad != null && pad.leftTrigger.wasPressedThisFrame) || (kb != null && kb.vKey.wasPressedThisFrame);
                case PadBtn.RT: return (pad != null && pad.rightTrigger.wasPressedThisFrame) || (kb != null && (kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame));
            }
            return false;
        }

        /// <summary>B / Escape pressed this frame (the EventSystem's Cancel is consumed by whichever control handles it; screens that need a plain "back" poll this).</summary>
        public static bool BackPressed()
        {
            var pad = Gamepad.current; var kb = Keyboard.current;
            return (pad != null && pad.buttonEast.wasPressedThisFrame) || (kb != null && (kb.escapeKey.wasPressedThisFrame || kb.fKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame));
        }

        /// <summary>Index after stepping by <paramref name="dir"/> inside 0..count-1 (wraps).</summary>
        public static int Wrap(int index, int dir, int count) { return count <= 0 ? 0 : ((index + dir) % count + count) % count; }
    }
}
