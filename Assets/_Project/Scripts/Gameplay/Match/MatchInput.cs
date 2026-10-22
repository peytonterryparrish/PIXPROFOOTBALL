using UnityEngine;
using UnityEngine.InputSystem;

namespace PixFootball.Gameplay
{
    public enum Btn { A = 0, B = 1, X = 2, Y = 3, LB = 4, RB = 5 }

    /// <summary>One frame of match input from the pad (or keyboard): sticks, face buttons with press / hold / release tracking, D-pad and system buttons.</summary>
    public sealed class MatchInputFrame
    {
        public Vector2 Move, Look;
        public bool Sprint, LT;
        /// <summary>Edge flags (true only on the frame the trigger crossed the threshold) so no consumer needs its own previous-frame bookkeeping (QA-833).</summary>
        public bool LTDown, SprintDown;
        public readonly bool[] Down = new bool[6], Up = new bool[6], Held = new bool[6];
        public readonly float[] HeldTime = new float[6];
        public bool DPadUp, DPadDown, DPadLeft, DPadRight, View, Menu, Back, Confirm, Cancel;
        /// <summary>v0.5 replay scrubbing: the D-pad left / right (keyboard Q / E) held down.</summary>
        public bool DPadLeftHeld, DPadRightHeld;
        public bool LookFlickLeft, LookFlickRight, LookFlickUp, LookHeldDown;
        public bool UsingGamepad = true;
        /// <summary>v0.7: timeout (R3 on the pad, Backspace on the keyboard; it used to be LT, which now holds the route view and switches call tabs) and the keyboard quick-search key (slash).</summary>
        public bool Timeout, SearchKey;
        /// <summary>Navigation (menus / overlays): stick or D-pad, one step per press.</summary>
        public int NavX, NavY;

        public bool AnyDown { get { for (int i = 0; i < 6; i++) if (Down[i]) return true; return false; } }
        public void Clear()
        {
            Move = Look = Vector2.zero; Sprint = LT = false; LTDown = SprintDown = false;
            for (int i = 0; i < 6; i++) { Down[i] = Up[i] = Held[i] = false; HeldTime[i] = 0f; }
            DPadUp = DPadDown = DPadLeft = DPadRight = View = Menu = Back = Confirm = Cancel = false; DPadLeftHeld = DPadRightHeld = false; Timeout = SearchKey = false;
            LookFlickLeft = LookFlickRight = LookFlickUp = LookHeldDown = false; NavX = NavY = 0;
        }
    }

    /// <summary>Polls the gamepad and keyboard directly (no input-action asset needed) into a <see cref="MatchInputFrame"/>.</summary>
    public sealed class MatchInput
    {
        public static MatchInput Instance { get { return _inst ?? (_inst = new MatchInput()); } }
        static MatchInput _inst;
        /// <summary>The autotest replaces device input with this frame when non-null.</summary>
        public static MatchInputFrame Scripted;
        /// <summary>Autotest scripting: while on, device input is ignored and <see cref="Poll"/> returns the queued frames one per call (an empty frame when the queue is empty).</summary>
        public static bool ScriptMode;
        static readonly System.Collections.Generic.Queue<MatchInputFrame> _queue = new System.Collections.Generic.Queue<MatchInputFrame>();
        static readonly MatchInputFrame _empty = new MatchInputFrame();
        public static int QueuedFrames { get { return _queue.Count; } }
        public static void Enqueue(MatchInputFrame f) { _queue.Enqueue(f); }
        public static void ClearQueue() { _queue.Clear(); }

        public readonly MatchInputFrame Frame = new MatchInputFrame();

        /// <summary>Keyboard quick search: while <see cref="CaptureText"/> is on, typed characters collect here (the play-call sheet drains it each frame).</summary>
        public static readonly System.Text.StringBuilder TypedText = new System.Text.StringBuilder();
        public static bool CaptureText;
        static Keyboard _hookedKeyboard;
        static void HookText(Keyboard kb)
        {
            if (kb == null || ReferenceEquals(kb, _hookedKeyboard)) return;
            _hookedKeyboard = kb;
            kb.onTextInput += c => { if (CaptureText && TypedText.Length < 40 && !char.IsControl(c)) TypedText.Append(c); };
        }
        readonly float[] _heldFor = new float[6];
        float _prevRsMag, _flickCooldown;
        float _navRepeat; int _lastNavX, _lastNavY;
        int _lastPollFrame = -1; bool _prevLTHeld, _prevSprintHeld;
        int _swallow;

        /// <summary>Drops the device input for the next <paramref name="frames"/> polls (and clears the frame already read this frame): used when a menu closes so the button that closed it never also acts in the game (QA-755).</summary>
        public static void SwallowInput(int frames) { var i = Instance; i._swallow = Mathf.Max(i._swallow, frames); i.Frame.Clear(); }
        // reused per-poll scratch (QA-711 / QA-830: no garbage per poll)
        readonly bool[] _press = new bool[6], _rel = new bool[6], _held = new bool[6];

        public MatchInputFrame Poll(float unscaledDt)
        {
            if (Scripted != null) return Scripted;
            if (ScriptMode) { _empty.Clear(); return _queue.Count > 0 ? _queue.Dequeue() : _empty; }
            var f = Frame;
            // QA-831: several callers poll in the same frame (match loop, replay, shows); the timers below must advance once per frame, so later calls reuse the first result
            if (Application.isPlaying && _lastPollFrame == Time.frameCount) return f;
            _lastPollFrame = Time.frameCount;
            if (_swallow > 0) { _swallow--; f.Clear(); f.UsingGamepad = InputDeviceTracker.GamepadActive; return f; }
            f.Clear();
            var pad = Gamepad.current; var kb = Keyboard.current;
            HookText(kb);
            InputDeviceTracker.Update();
            f.UsingGamepad = InputDeviceTracker.GamepadActive;

            Vector2 mv = Vector2.zero, look = Vector2.zero;
            bool[] press = _press, rel = _rel, held = _held;
            for (int i = 0; i < 6; i++) press[i] = rel[i] = held[i] = false;
            if (pad != null)
            {
                mv = pad.leftStick.ReadValue(); look = pad.rightStick.ReadValue();
                f.Sprint |= pad.rightTrigger.ReadValue() > 0.3f; f.LT |= pad.leftTrigger.ReadValue() > 0.3f;
                Set(0, pad.buttonSouth, press, rel, held); Set(1, pad.buttonEast, press, rel, held); Set(2, pad.buttonWest, press, rel, held); Set(3, pad.buttonNorth, press, rel, held);
                Set(4, pad.leftShoulder, press, rel, held); Set(5, pad.rightShoulder, press, rel, held);
                f.DPadUp |= pad.dpad.up.wasPressedThisFrame; f.DPadDown |= pad.dpad.down.wasPressedThisFrame;
                f.DPadLeft |= pad.dpad.left.wasPressedThisFrame; f.DPadRight |= pad.dpad.right.wasPressedThisFrame;
                f.DPadLeftHeld |= pad.dpad.left.isPressed; f.DPadRightHeld |= pad.dpad.right.isPressed;
                f.View |= pad.selectButton.wasPressedThisFrame; f.Menu |= pad.startButton.wasPressedThisFrame;
                f.Confirm |= pad.buttonSouth.wasPressedThisFrame; f.Cancel |= pad.buttonEast.wasPressedThisFrame;
                f.Timeout |= pad.rightStickButton.wasPressedThisFrame;
            }
            if (kb != null)
            {
                Vector2 k = Vector2.zero;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) k.y += 1f; if (kb.sKey.isPressed || kb.downArrowKey.isPressed) k.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) k.x += 1f; if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) k.x -= 1f;
                if (k.sqrMagnitude > 0f) mv = k.normalized;
                Vector2 kl = Vector2.zero;
                if (kb.iKey.isPressed) kl.y += 1f; if (kb.kKey.isPressed) kl.y -= 1f; if (kb.lKey.isPressed) kl.x += 1f; if (kb.jKey.isPressed) kl.x -= 1f;
                if (kl.sqrMagnitude > 0f) look = kl.normalized;
                f.Sprint |= kb.leftShiftKey.isPressed; f.LT |= kb.vKey.isPressed;
                Set(0, kb.spaceKey, press, rel, held); Set(1, kb.fKey, press, rel, held); Set(2, kb.cKey, press, rel, held); Set(3, kb.rKey, press, rel, held);
                Set(4, kb.zKey, press, rel, held); Set(5, kb.xKey, press, rel, held);
                f.DPadDown |= kb.tKey.wasPressedThisFrame; f.DPadUp |= kb.gKey.wasPressedThisFrame; f.DPadLeft |= kb.qKey.wasPressedThisFrame; f.DPadRight |= kb.eKey.wasPressedThisFrame;
                f.DPadLeftHeld |= kb.qKey.isPressed; f.DPadRightHeld |= kb.eKey.isPressed;
                f.View |= kb.tabKey.wasPressedThisFrame; f.Menu |= kb.escapeKey.wasPressedThisFrame;
                f.Timeout |= kb.backspaceKey.wasPressedThisFrame; f.SearchKey |= kb.slashKey.wasPressedThisFrame;
                // QA-832 / QA-993: Confirm / Cancel are menu-only keys (Enter, Backspace); Space (A) and F (B) are read through Down[] so a game button never double-fires a menu action
                f.Confirm |= kb.enterKey.wasPressedThisFrame; f.Cancel |= kb.backspaceKey.wasPressedThisFrame;
            }
            f.Move = mv; f.Look = look;
            for (int i = 0; i < 6; i++)
            {
                f.Down[i] = press[i]; f.Up[i] = rel[i]; f.Held[i] = held[i];
                if (held[i]) _heldFor[i] += unscaledDt; else if (!rel[i]) _heldFor[i] = 0f;
                f.HeldTime[i] = _heldFor[i];
                if (rel[i]) f.HeldTime[i] = _heldFor[i];
            }
            for (int i = 0; i < 6; i++) if (rel[i]) _heldFor[i] = 0f;

            f.LTDown = f.LT && !_prevLTHeld; f.SprintDown = f.Sprint && !_prevSprintHeld; _prevLTHeld = f.LT; _prevSprintHeld = f.Sprint;

            // right-stick flicks (juke left / right, truck up) and a held "down" (protect the ball)
            float mag = look.magnitude;
            _flickCooldown -= unscaledDt;
            if (mag > 0.78f && _prevRsMag < 0.45f && _flickCooldown <= 0f)
            {
                if (Mathf.Abs(look.x) > Mathf.Abs(look.y)) { if (look.x < 0) f.LookFlickLeft = true; else f.LookFlickRight = true; }
                else if (look.y > 0f) f.LookFlickUp = true;
                _flickCooldown = 0.25f;
            }
            if (look.y < -0.6f && Mathf.Abs(look.x) < 0.7f) f.LookHeldDown = true;
            _prevRsMag = mag;

            // menu navigation with repeat
            int nx = f.DPadRight ? 1 : (f.DPadLeft ? -1 : 0), ny = f.DPadUp ? 1 : (f.DPadDown ? -1 : 0);
            if (nx == 0 && ny == 0)
            {
                int sx = mv.x > 0.6f ? 1 : (mv.x < -0.6f ? -1 : 0), sy = mv.y > 0.6f ? 1 : (mv.y < -0.6f ? -1 : 0);
                if (sx != 0 || sy != 0)
                {
                    _navRepeat -= unscaledDt;
                    if (sx != _lastNavX || sy != _lastNavY || _navRepeat <= 0f) { nx = sx; ny = sy; _navRepeat = (sx != _lastNavX || sy != _lastNavY) ? 0.4f : 0.14f; }
                }
                else _navRepeat = 0f;
                _lastNavX = sx; _lastNavY = sy;
            }
            f.NavX = nx; f.NavY = ny;
            return f;
        }

        static void Set(int i, UnityEngine.InputSystem.Controls.ButtonControl c, bool[] press, bool[] rel, bool[] held)
        {
            press[i] |= c.wasPressedThisFrame; rel[i] |= c.wasReleasedThisFrame; held[i] |= c.isPressed;
        }

        /// <summary>Whether the HUD should show controller glyphs or keycaps.</summary>
        public static bool GamepadActive { get { return InputDeviceTracker.GamepadActive; } }
    }
}

