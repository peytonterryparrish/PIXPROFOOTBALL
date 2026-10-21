using PixFootball.Gameplay;
using PixFootball.UI;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.Presentation
{
    /// <summary>
    /// First-person helmet overlay (optional in Settings): a procedural, subtle facemask - the dark rim of the helmet at the top, two curved bars of the mask across the lower part of the
    /// view, side posts and a soft vignette - drawn once into a texture and shown only while the first-person camera is the live camera.
    /// </summary>
    public sealed class FirstPersonFx : MonoBehaviour
    {
        MatchDirector _dir; Canvas _canvas; RawImage _img; Texture2D _tex; CanvasGroup _group;
        public bool Showing { get { return _canvas != null && _canvas.enabled; } }

        public static FirstPersonFx Create(Transform parent, MatchDirector dir)
        {
            var go = new GameObject("FirstPersonFx");
            go.transform.SetParent(parent, false);
            var fx = go.AddComponent<FirstPersonFx>();
            fx._dir = dir;
            fx.Build();
            return fx;
        }

        void Build()
        {
            _canvas = UiKit.CreateCanvas("FirstPersonCanvas", 12, transform);
            _canvas.GetComponent<GraphicRaycaster>().enabled = false;
            var root = (RectTransform)_canvas.transform;
            _tex = MakeMask(1280, 720);
            var go = UiKit.Rect(root, "Mask");
            UiKit.Stretch((RectTransform)go);
            _img = go.gameObject.AddComponent<RawImage>(); _img.texture = _tex; _img.raycastTarget = false;
            _group = go.gameObject.AddComponent<CanvasGroup>();
            _canvas.enabled = false;
        }

        void Update()
        {
            bool want = _dir != null && _dir.Cams != null && _dir.Cams.Mode == CameraMode.FirstPerson && !_dir.Cams.Overridden && PixSettings.FirstPersonMask && !_dir.Config.CpuVsCpu
                        && (_dir.Phase == MatchPhase.PreSnap || _dir.Phase == MatchPhase.Live || _dir.Phase == MatchPhase.PostPlay) && !_dir.Paused;
            if (want != _canvas.enabled) _canvas.enabled = want;
            if (want) _group.alpha = 0.85f;
        }

        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>Distance-field drawing of a few curves: bars of the mask are anti-aliased strokes with a thin highlight, the rim and vignette are gradients.</summary>
        static Texture2D MakeMask(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "PIX_FpMask" };
            var px = new Color32[w * h];
            Vector3 steel = new Vector3(0.20f, 0.21f, 0.24f), hi = new Vector3(0.55f, 0.57f, 0.62f);
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);          // 0 bottom .. 1 top
                for (int x = 0; x < w; x++)
                {
                    float u = x / (float)(w - 1), cx = u * 2f - 1f;
                    float a = 0f; Vector3 c = steel;
                    // vignette
                    float r = Mathf.Sqrt(cx * cx + (v * 2f - 1f) * (v * 2f - 1f) * 0.8f);
                    a = Mathf.Max(a, Smooth(0.78f, 1.35f, r) * 0.7f);
                    // helmet rim: a dark band at the top whose lower edge dips toward the middle
                    float rimEdge = 0.93f - 0.035f * (1f - cx * cx);
                    float rim = Smooth(rimEdge, rimEdge + 0.07f, v);
                    a = Mathf.Max(a, rim * 0.95f);
                    // mask bars: two curved strokes across the lower view (they bow toward the middle) and two side posts
                    float bar1 = 0.115f + 0.045f * (1f - cx * cx), bar2 = 0.05f + 0.03f * (1f - cx * cx);
                    float d1 = Mathf.Abs(v - bar1) * h, d2 = Mathf.Abs(v - bar2) * h;
                    float th = 8.5f;
                    float s1 = 1f - Smooth(th - 1.2f, th + 1.2f, d1), s2 = 1f - Smooth(th - 1.2f, th + 1.2f, d2);
                    float postX = 0.055f + 0.035f * (1f - v) * (1f - v);
                    float d3 = Mathf.Min(Mathf.Abs(u - postX), Mathf.Abs(u - (1f - postX))) * w;
                    float s3 = 1f - Smooth(th * 0.9f - 1.2f, th * 0.9f + 1.2f, d3);
                    float stroke = Mathf.Max(Mathf.Max(s1, s2), s3 * Smooth(0.0f, 0.5f, 1f - v * 0.6f));
                    if (stroke > 0f)
                    {
                        float edge = Mathf.Max(d1 < th ? 1f - Mathf.Abs(d1 - th * 0.35f) / (th * 0.6f) : 0f, d2 < th ? 1f - Mathf.Abs(d2 - th * 0.35f) / (th * 0.6f) : 0f);
                        c = Vector3.Lerp(steel, hi, Mathf.Clamp01(edge) * 0.55f);
                        a = Mathf.Max(a, stroke * 0.9f);
                    }
                    // the bottom of the view darkens a little under the mask
                    a = Mathf.Max(a, Smooth(0.2f, 0.0f, v) * 0.4f);
                    px[y * w + x] = new Color32((byte)(c.x * 255f), (byte)(c.y * 255f), (byte)(c.z * 255f), (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            tex.SetPixels32(px); tex.Apply(false, true);
            return tex;
        }
    }
}
