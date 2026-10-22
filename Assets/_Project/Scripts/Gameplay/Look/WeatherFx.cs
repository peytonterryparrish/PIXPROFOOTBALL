using PixFootball.Art;
using PixFootball.Core;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Particle effects of the weather: rain streaks, splashes on the grass, falling snow and breath vapour. Everything is a plain ParticleSystem following the camera
    /// (a box of weather around the view), drawn with the unlit PIX/FxUnlit shader; amounts scale with the graphics tier (see <see cref="QualityTiers.FxScale"/>).
    /// </summary>
    public sealed class WeatherFx
    {
        readonly Transform _root;
        ParticleSystem _rain, _splash, _snow, _breath;
        ParticleSystem.EmissionModule _rainEm, _splashEm, _snowEm;
        ParticleSystem.VelocityOverLifetimeModule _rainVel, _snowVel;
        float _breathClock, _boxSize = 40f;
        WeatherPreset _preset;
        Material _rainMat, _snowMat, _splashMat, _puffMat;

        public bool RainOn { get { return _rain != null && _rain.isPlaying; } }
        public bool SnowOn { get { return _snow != null && _snow.isPlaying; } }
        public ParticleSystem Rain { get { return _rain; } }
        public ParticleSystem Snow { get { return _snow; } }

        public WeatherFx(Transform parent)
        {
            var go = new GameObject("WeatherFx");
            go.transform.SetParent(parent, false);
            _root = go.transform;
            var a = PixLook.Assets;
            if (a == null || a.fxShader == null) { Debug.LogWarning("[PIX] weather particles unavailable (PixLookAssets / FxUnlit missing)"); return; }
            _rainMat = a.rainMaterial; _snowMat = a.snowMaterial; _splashMat = a.splashMaterial; _puffMat = a.puffMaterial;
            Build();
        }

        ParticleSystem Make(string name, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = true; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = max; main.prewarm = false; main.useUnscaledTime = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return ps;
        }

        void Build()
        {
            // ---- rain: stretched streaks falling fast, a box around the camera
            _rain = Make("Rain", _rainMat, 26000);
            {
                var m = _rain.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.25f); m.startSpeed = 0f; m.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.034f);
                m.startColor = new Color(0.78f, 0.84f, 0.95f, 0.42f);
                var sh = _rain.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(40f, 0.5f, 40f);
                _rainVel = _rain.velocityOverLifetime; _rainVel.enabled = true; _rainVel.space = ParticleSystemSimulationSpace.World;
                _rainVel.x = 1.5f; _rainVel.y = -19f; _rainVel.z = 0.5f;
                _rainEm = _rain.emission; _rainEm.rateOverTime = 0f;
                var r = _rain.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.lengthScale = 22f; r.velocityScale = 0f; r.cameraVelocityScale = 0f; r.normalDirection = 1f;
            }
            // ---- rain splashes on the ground
            _splash = Make("Splash", _splashMat, 6000);
            {
                var m = _splash.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.3f); m.startSpeed = 0f; m.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.4f); m.startColor = new Color(0.85f, 0.9f, 1f, 0.35f);
                var sh = _splash.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(26f, 0.01f, 26f);
                _splashEm = _splash.emission; _splashEm.rateOverTime = 0f;
                var sz = _splash.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(1f, 1f)));
                var col = _splash.colorOverLifetime; col.enabled = true;
                var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var r = _splash.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            }
            // ---- snow: slow drifting flakes
            _snow = Make("Snow", _snowMat, 36000);
            {
                var m = _snow.main; m.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f); m.startSpeed = 0f; m.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.065f); m.startColor = new Color(0.96f, 0.97f, 1f, 0.9f);
                var sh = _snow.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(44f, 0.5f, 44f);
                _snowVel = _snow.velocityOverLifetime; _snowVel.enabled = true; _snowVel.space = ParticleSystemSimulationSpace.World;
                _snowVel.x = new ParticleSystem.MinMaxCurve(0.4f, 1.6f); _snowVel.y = new ParticleSystem.MinMaxCurve(-1.9f, -1.0f); _snowVel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                var noise = _snow.noise; noise.enabled = true; noise.strength = 0.55f; noise.frequency = 0.35f; noise.scrollSpeed = 0.4f;
                _snowEm = _snow.emission; _snowEm.rateOverTime = 0f;
                var r = _snow.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Billboard;
            }
            // ---- breath vapour: emitted by hand at the players' mouths
            _breath = Make("Breath", _puffMat, 600);
            {
                var m = _breath.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f); m.startSpeed = 0f; m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f); m.startColor = new Color(0.9f, 0.93f, 1f, 0.22f);
                var em = _breath.emission; em.enabled = false;
                var sz = _breath.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 2.6f)));
                var col = _breath.colorOverLifetime; col.enabled = true;
                var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var vel = _breath.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.y = 0.25f;
                var r = _breath.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Billboard;
            }
        }

        /// <summary>Brightness of the unlit particle colours for the lighting of the preset (streaks and flakes must read in the day and not glow at night).</summary>
        static float Brightness(WeatherPreset p)
        {
            switch (p.Time) { case TimeOfDay.Day: return 1.0f; case TimeOfDay.Dusk: return 0.85f; default: return 0.8f; }
        }

        public void Configure(WeatherPreset p)
        {
            _preset = p;
            if (_rain == null) return;
            float fx = QualityTiers.FxScale;
            float b = Brightness(p);
            SetPlaying(_rain, p.RainIntensity > 0.01f && fx > 0f);
            SetPlaying(_splash, p.RainIntensity > 0.01f && QualityTiers.WeatherSplashes);
            SetPlaying(_snow, p.SnowfallIntensity > 0.01f && fx > 0f);
            var rm = _rain.main; rm.startColor = new Color(0.78f * b, 0.84f * b, 0.95f * b, 0.55f);
            var sm = _snow.main; sm.startColor = new Color(0.95f * b, 0.96f * b, 1f * b, 0.9f);
            var sp = _splash.main; sp.startColor = new Color(0.8f * b, 0.86f * b, 0.95f * b, 0.35f);
            float wind = p.WindStrength;
            _rainVel.x = 1.0f + 5f * wind; _rainVel.z = 0.4f * wind;
            _snowVel.x = new ParticleSystem.MinMaxCurve(0.3f + 1.2f * wind, 1.2f + 2.6f * wind);
            _rainEm.rateOverTime = 5200f * p.RainIntensity * fx;
            _splashEm.rateOverTime = 420f * p.RainIntensity * fx;
            _snowEm.rateOverTime = 2600f * p.SnowfallIntensity * fx;
            // pre-fill so the first frame already shows weather
            if (p.RainIntensity > 0.01f) { _rain.Simulate(1.3f, true, true); _rain.Play(); }
            if (p.SnowfallIntensity > 0.01f) { _snow.Simulate(9f, true, true); _snow.Play(); }
        }

        static void SetPlaying(ParticleSystem ps, bool on)
        {
            if (ps == null) return;
            if (on) { if (!ps.isPlaying) ps.Play(); }
            else if (ps.isPlaying || ps.particleCount > 0) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void Update(float dt, WeatherPreset p, Camera cam)
        {
            if (_rain == null || cam == null) return;
            // QA-714: the broadcast camera is 40+ m off the field: centre the weather where the camera looks at the ground and widen the volumes with its height, so the play is
            // inside the rain / snow instead of only the near sideline. Emission scales with the area so the density stays the same.
            Transform ct = cam.transform;
            Vector3 c = ct.position, fw = ct.forward;
            Vector3 center;
            if (fw.y < -0.05f && c.y > 4f) { float tt = Mathf.Min(-c.y / fw.y, 120f); center = c + fw * tt; center.y = 0f; }
            else { Vector3 f = fw; f.y = 0f; center = c + f.normalized * (f.sqrMagnitude > 0.01f ? 14f : 0f); }
            float size = Mathf.Clamp(40f + Mathf.Max(0f, c.y - 3f) * 1.4f, 40f, 80f);
            float k = (size * size) / 1600f;
            if (Mathf.Abs(size - _boxSize) > 2f)
            {
                _boxSize = size;
                var rs = _rain.shape; rs.scale = new Vector3(size, 0.5f, size);
                var ss = _snow.shape; ss.scale = new Vector3(size * 1.1f, 0.5f, size * 1.1f);
                var ps = _splash.shape; ps.scale = new Vector3(size * 0.65f, 0.01f, size * 0.65f);
                float fx = QualityTiers.FxScale;
                _rainEm.rateOverTime = 5200f * p.RainIntensity * fx * k; _snowEm.rateOverTime = 2600f * p.SnowfallIntensity * fx * Mathf.Sqrt(k); _splashEm.rateOverTime = 420f * p.RainIntensity * fx * k;
            }
            _rain.transform.position = new Vector3(center.x, 24f, center.z);
            _snow.transform.position = new Vector3(center.x, 16f, center.z);
            _splash.transform.position = new Vector3(center.x, 0.03f, center.z);
        }

        /// <summary>A puff of breath at a mouth, travelling with the player's heading.</summary>
        public void Breath(Vector3 mouth, Vector3 forward, float speed)
        {
            if (_breath == null || !_preset.BreathVapor || !QualityTiers.BreathVapor) return;
            var ep = new ParticleSystem.EmitParams
            {
                position = mouth, velocity = forward.normalized * 0.45f + Vector3.up * 0.15f, startSize = Random.Range(0.05f, 0.09f), startLifetime = Random.Range(0.9f, 1.4f), applyShapeToPosition = false
            };
            _breath.Emit(ep, 1);
        }

        public void SetVisible(bool on) { if (_root != null) _root.gameObject.SetActive(on); }

        public void Dispose() { if (_root != null) Object.Destroy(_root.gameObject); }
    }
}
