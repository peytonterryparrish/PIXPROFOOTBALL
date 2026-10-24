using PixFootball.Gameplay;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// Live backdrop for the glass surfaces: a blurred, half-resolution copy of the finished 3D frame (after post-processing, before the overlay canvases) published to the UI shader as the global
    /// texture <c>_PixGlassBackdrop</c>. It is produced by an HDRP global Custom Pass (<see cref="GlassBlurPass"/>) that only runs while at least one glass surface is enabled and the graphics tier allows
    /// it (Low tier: flat frosted fallback in the shader, zero cost). Cost on a 1440p frame: one half-res downsample + 2 x (2 gaussian passes) on a 1280x720 target, a fraction of a millisecond (Docs/QA/glass_ui.md).
    /// </summary>
    public static class GlassBackdrop
    {
        public static readonly int TexId = Shader.PropertyToID("_PixGlassBackdrop");
        public static readonly int ScaleId = Shader.PropertyToID("_PixGlassBackdropScale");
        public static readonly int OnId = Shader.PropertyToID("_PixGlassBackdropOn");

        static int _users;
        /// <summary>QA / tests: never run the live blur (the shader draws the static frosted fallback).</summary>
        public static bool ForceFallback;
        /// <summary>Where the pass runs: AfterPostProcess for the real overlay canvases; the QA renders that draw the UI inside a camera use BeforePostProcess.</summary>
        public static CustomPassInjectionPoint InjectionPoint = CustomPassInjectionPoint.AfterPostProcess;
        /// <summary>A camera with this name is never blurred (the QA UI camera).</summary>
        public static string SkipCameraName = "";
        /// <summary>Frames in which the blur pass actually ran (QA proves the live path with it).</summary>
        public static int PassRuns;

        public static bool AnyUser { get { return _users > 0; } }

        /// <summary>The live blur is wanted: glass is on screen, the tier allows it and nothing forces the fallback.</summary>
        public static bool WantBlur { get { return !ForceFallback && _users > 0 && QualityTiers.Current > 0 && !UiTheme.IsTile; } }   // the Tile look draws solid surfaces: no blur pass, ever

        public static void AddUser() { _users++; GlassBackdropDriver.Ensure(); }
        public static void RemoveUser() { _users = Mathf.Max(0, _users - 1); }

        /// <summary>Pure rule for the tier gate (tested): Low (tier 0) uses the frosted fallback, Medium and above the live blur.</summary>
        public static bool TierAllowsBlur(int tier) { return tier > 0; }

        /// <summary>Pure helper (tested): the uv extent of a scaled blur texture that holds valid pixels.</summary>
        public static Vector2 ValidExtent(Vector2Int viewport, Vector2Int texture)
        {
            return new Vector2(texture.x > 0 ? Mathf.Clamp01((float)viewport.x / texture.x) : 1f, texture.y > 0 ? Mathf.Clamp01((float)viewport.y / texture.y) : 1f);
        }
    }


    /// <summary>Downsamples and blurs the camera colour into half-resolution handles and publishes the result to the UI shader.</summary>
    [System.Serializable]
    public sealed class GlassBlurPass : CustomPass
    {
        RTHandle _a, _b, _tmp;

        protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
        {
            _a = Alloc("PixGlassBlurA"); _b = Alloc("PixGlassBlurB"); _tmp = Alloc("PixGlassBlurTmp");
            name = "PIX glass backdrop blur";
        }

        static RTHandle Alloc(string n)
        {
            return RTHandles.Alloc(Vector2.one * 0.5f, TextureXR.slices, dimension: TextureXR.dimension, colorFormat: GraphicsFormat.R8G8B8A8_SRGB, useDynamicScale: true, name: n);
        }

        protected override void Execute(CustomPassContext ctx)
        {
            if (!GlassBackdrop.WantBlur) return;
            var cam = ctx.hdCamera.camera;
            if (cam.cameraType != CameraType.Game) return;
            if (!string.IsNullOrEmpty(GlassBackdrop.SkipCameraName) && cam.name == GlassBackdrop.SkipCameraName) return;

            var t = GlassTheme.T;
            float radius = Mathf.Max(1f, t.blurRadius);
            CustomPassUtils.GaussianBlur(ctx, ctx.cameraColorBuffer, _a, _tmp, 9, radius, 0, 0, true);
            RTHandle result = _a;
            for (int i = 1; i < Mathf.Clamp(t.blurPasses, 1, 4); i++)
            {
                RTHandle dst = result == _a ? _b : _a;
                CustomPassUtils.GaussianBlur(ctx, result, dst, _tmp, 9, radius, 0, 0, false);
                result = dst;
            }

            Vector2 size = result.GetScaledSize(result.rtHandleProperties.currentViewportSize);
            Vector2 ext = GlassBackdrop.ValidExtent(new Vector2Int(Mathf.RoundToInt(size.x), Mathf.RoundToInt(size.y)), new Vector2Int(result.rt.width, result.rt.height));
            ctx.cmd.SetGlobalTexture(GlassBackdrop.TexId, result);
            ctx.cmd.SetGlobalVector(GlassBackdrop.ScaleId, new Vector4(ext.x, ext.y, 0f, 0f));
            ctx.cmd.SetGlobalFloat(GlassBackdrop.OnId, 1f);
            GlassBackdrop.PassRuns++;
        }

        protected override void Cleanup()
        {
            if (_a != null) { _a.Release(); _a = null; }
            if (_b != null) { _b.Release(); _b = null; }
            if (_tmp != null) { _tmp.Release(); _tmp = null; }
        }
    }
}
