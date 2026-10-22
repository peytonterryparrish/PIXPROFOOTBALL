using UnityEngine;
using UnityEngine.InputSystem;

namespace PixFootball.Gameplay
{
    /// <summary>One collision as the presentation sees it (a tackle, a sack, a truck, a pancake, a catch in traffic).</summary>
    public struct ContactImpact
    {
        public Vector3 Position;
        /// <summary>0..1 impact energy (closing speed, hit type).</summary>
        public float Intensity;
        public bool Big;
        public bool Sack;
        public int AgentA, AgentB;
        /// <summary>v0.9.0 CONNECT E-348: the same contact with explicit roles (AgentA / AgentB mean different things per contact kind): who delivered the hit and who took it. Valid when <see cref="HasRoles"/>.</summary>
        public int Hitter, Victim;
        public bool HasRoles;
    }

    /// <summary>
    /// v0.4 collision feel. Trauma based camera shake (subtle: centimetres and a fraction of a degree, squared so only big hits are felt) applied after the camera brain, and Xbox controller
    /// rumble through <see cref="Gamepad.SetMotorSpeeds"/> with a decaying envelope. Both can be turned off in Settings.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public sealed class CollisionFeel : MonoBehaviour
    {
        public static CollisionFeel Instance { get; private set; }
        public float Trauma { get; private set; }
        float _lowRumble, _highRumble, _rumbleLeft;
        Camera _cam;
        float _seed;
        /// <summary>v0.9 (GFX-403): was 0.06 m / 0.45 degrees, squared on top, so a normal tackle moved the camera by about 2 mm. Now a tackle is felt, a big hit is not missed.</summary>
        public const float MaxShiftMetres = 0.14f, MaxShakeDegrees = 1.1f;
        float _punch, _hitStopUntil;   // camera kick (degrees, decays in about 0.25 s) and the end of a running hit-stop (unscaled time)
        const float HitStopScale = 0.08f;

        public static CollisionFeel Create(Camera cam)
        {
            var go = cam.gameObject;
            var f = go.GetComponent<CollisionFeel>();
            if (f == null) f = go.AddComponent<CollisionFeel>();
            f._cam = cam; Instance = f;
            return f;
        }

        void Awake() { Instance = this; _seed = Random.value * 100f; }

        /// <summary>Adds camera trauma (0..1).</summary>
        public void Shake(float amount)
        {
            if (!PixSettings.CameraShake) return;
            Trauma = Mathf.Min(1f, Trauma + Mathf.Clamp01(amount));
        }

        /// <summary>Freezes the action for a moment (time scale to 0.08 for that many real seconds, then back); a pause or another time-scale user in the meantime wins.</summary>
        public void HitStop(float seconds)
        {
            if (Time.timeScale < 0.5f && _hitStopUntil <= Time.unscaledTime) return;   // paused or slow motion already: leave it
            if (_hitStopUntil > Time.unscaledTime) { _hitStopUntil = Mathf.Max(_hitStopUntil, Time.unscaledTime + seconds); return; }
            _hitStopUntil = Time.unscaledTime + seconds;
            _stopSaved = Time.timeScale; Time.timeScale = HitStopScale;
        }
        float _stopSaved = 1f;

        /// <summary>Starts / refreshes a rumble envelope (motor strengths 0..1, seconds).</summary>
        public void Rumble(float low, float high, float seconds)
        {
            if (!PixSettings.Rumble) return;
            if (Gamepad.current == null) return;
            _lowRumble = Mathf.Max(low, _rumbleLeft > 0f ? _lowRumble * 0.6f : 0f);
            _highRumble = Mathf.Max(high, _rumbleLeft > 0f ? _highRumble * 0.6f : 0f);
            _rumbleLeft = Mathf.Max(_rumbleLeft, seconds);
            _rumbleTotal = Mathf.Max(seconds, 0.05f);
        }
        float _rumbleTotal = 0.2f;

        /// <summary>Maps a collision onto shake and rumble. involvesUser: the player's own man was hit or hit somebody.</summary>
        public void OnImpact(in ContactImpact c, bool involvesUser)
        {
            float k = Mathf.Clamp01(c.Intensity);
            Shake(0.30f + 0.65f * k * (c.Big ? 1.2f : 0.8f) * (c.Sack ? 1.1f : 1f));
            if ((c.Big || c.Sack || k > 0.8f) && PixSettings.CameraShake)
            {
                _punch = Mathf.Max(_punch, 1.5f);   // GFX-403: a 1.5 degree camera kick and a 40-60 ms hit-stop on a big hit, a sack or a truck
                HitStop(Mathf.Lerp(0.04f, 0.06f, k));
            }
            if (involvesUser)
            {
                float low = Mathf.Lerp(0.25f, 0.9f, k), high = c.Big ? 0.75f : Mathf.Lerp(0.1f, 0.45f, k);
                Rumble(low, high, Mathf.Lerp(0.12f, c.Big ? 0.42f : 0.26f, k));
            }
            else if (c.Big) Rumble(0.18f, 0.12f, 0.12f);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (_hitStopUntil > 0f && Time.unscaledTime >= _hitStopUntil)
            {
                _hitStopUntil = 0f;
                if (Mathf.Abs(Time.timeScale - HitStopScale) < 0.001f) Time.timeScale = _stopSaved;   // only when nobody else changed it
            }
            if (_rumbleLeft > 0f)
            {
                _rumbleLeft -= dt;
                var pad = Gamepad.current;
                if (pad != null)
                {
                    float f = Mathf.Clamp01(_rumbleLeft / _rumbleTotal);
                    if (_rumbleLeft > 0f) pad.SetMotorSpeeds(_lowRumble * f, _highRumble * f);
                }
                if (_rumbleLeft <= 0f) StopRumble();
            }
            if (Time.timeScale <= 0f) StopRumbleIfIdle();
        }

        void StopRumbleIfIdle() { if (_rumbleLeft <= 0f) return; StopRumble(); }

        void StopRumble()
        {
            _rumbleLeft = 0f; _lowRumble = _highRumble = 0f;
            var pad = Gamepad.current;
            if (pad != null) pad.SetMotorSpeeds(0f, 0f);
        }

        void LateUpdate()
        {
            if (_cam == null) return;
            float dt = Time.unscaledDeltaTime;
            if (_punch > 0.01f) { _cam.transform.localRotation *= Quaternion.Euler(-_punch, 0f, 0f); _punch = Mathf.MoveTowards(_punch, 0f, dt * 6f); }   // the kick decays in about 0.25 s
            if (Trauma <= 0.001f) { Trauma = 0f; return; }
            float s = Trauma * Trauma;
            float t = Time.unscaledTime * 38f + _seed;
            float nx = Mathf.PerlinNoise(t, 0.1f) * 2f - 1f, ny = Mathf.PerlinNoise(0.3f, t) * 2f - 1f, nr = Mathf.PerlinNoise(t, 7.7f) * 2f - 1f;
            var tr = _cam.transform;
            tr.localPosition += tr.right * (nx * MaxShiftMetres * s) + tr.up * (ny * MaxShiftMetres * s);
            tr.localRotation *= Quaternion.Euler(ny * MaxShakeDegrees * s, nx * MaxShakeDegrees * s, nr * MaxShakeDegrees * s * 1.5f);
            Trauma = Mathf.Max(0f, Trauma - dt * 1.6f);
        }

        void OnDisable() { StopRumble(); }
        void OnApplicationQuit() { StopRumble(); }
    }
}
