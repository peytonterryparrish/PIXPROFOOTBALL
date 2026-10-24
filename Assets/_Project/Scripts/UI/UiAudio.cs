using PixFootball.Art;
using PixFootball.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// Menu sounds: a tick when the selection moves and a click when something is chosen or backed out of (QA-767: uiClick / uiMove used to be imported and never played).
    /// One shared AudioSource that ignores the listener pause, so the pause menu is not silent, volume = SFX slider.
    /// </summary>
    public static class UiAudio
    {
        static AudioSource _src;
        static float _lastMove = -1f, _muteUntil;

        static AudioSource Source()
        {
            if (_src != null) return _src;
            var go = new GameObject("UiAudio") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            _src = go.AddComponent<AudioSource>();
            _src.playOnAwake = false; _src.spatialBlend = 0f; _src.ignoreListenerPause = true;
            return _src;
        }

        /// <summary>Silences move ticks for a moment (a menu opening and selecting its first row is not a user action).</summary>
        public static void Mute(float seconds) { _muteUntil = Mathf.Max(_muteUntil, Time.unscaledTime + seconds); }

        static void Play(AudioClip c, float volume, float pitch)
        {
            if (c == null || !Application.isPlaying) return;
            var s = Source();
            s.pitch = pitch;
            s.PlayOneShot(c, Mathf.Clamp01(volume * PixSettings.SfxVolume));
        }

        public static void Move()
        {
            if (Time.unscaledTime < _muteUntil || Time.unscaledTime - _lastMove < 0.05f) return;
            _lastMove = Time.unscaledTime;
            var rt = PixRuntimeAssets.Load();
            Play(rt != null ? (rt.uiMove != null ? rt.uiMove : rt.uiClick) : null, 0.35f, 1.15f);
        }

        public static void Click()
        {
            var rt = PixRuntimeAssets.Load();
            Play(rt != null ? rt.uiClick : null, 0.6f, 1f);
            if (MusicDirector.Instance != null) MusicDirector.Instance.Duck(0.22f, 0.3f);      // v0.11.0 AUDIO: the music steps back under a UI selection
        }
    }

    /// <summary>
    /// Keeps menus drivable by controller / keyboard after a mouse click on empty space cleared the selection: the first navigation press re-selects the last row (or the first one on screen) (QA-763).
    /// Lives on the EventSystem object.
    /// </summary>
    public sealed class UiNavKeeper : MonoBehaviour
    {
        GameObject _last;

        void Update()
        {
            var es = EventSystem.current;
            if (es == null) return;
            var cur = es.currentSelectedGameObject;
            if (cur != null && cur.activeInHierarchy) { _last = cur; return; }
            if (!NavPressed()) return;
            GameObject t = _last != null && _last.activeInHierarchy ? _last : FirstSelectable();
            if (t != null) es.SetSelectedGameObject(t);
        }

        static bool NavPressed()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame
                || kb.wKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)) return true;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null && (pad.dpad.up.wasPressedThisFrame || pad.dpad.down.wasPressedThisFrame || pad.dpad.left.wasPressedThisFrame || pad.dpad.right.wasPressedThisFrame
                || pad.buttonSouth.wasPressedThisFrame || pad.leftStick.ReadValue().sqrMagnitude > 0.36f)) return true;
            return false;
        }

        static GameObject FirstSelectable()
        {
            Selectable best = null; float bestY = float.NegativeInfinity;
            foreach (var s in Object.FindObjectsByType<Selectable>(FindObjectsSortMode.None))
            {
                if (s == null || !s.IsInteractable() || !s.gameObject.activeInHierarchy || s.navigation.mode == Navigation.Mode.None) continue;
                float y = s.transform.position.y;
                if (y > bestY) { bestY = y; best = s; }
            }
            return best != null ? best.gameObject : null;
        }
    }
}
