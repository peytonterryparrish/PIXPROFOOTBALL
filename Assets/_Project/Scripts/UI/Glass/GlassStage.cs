using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// The live 3D background of the glass menus: the menu's photoreal player in the team kit, facing the camera with a slow sway (a turntable of a few degrees, never a full turn), in front of the
    /// stadium at night. The stadium is a separate scene (MenuBackdrop_Glass, built by GlassSceneBuilder) loaded additively, so the Classic menu is untouched; if the scene is not in the build the
    /// player simply stands on the old stage floor.
    /// Framing is per aspect ratio: the canvas content ends at <see cref="ContentRight"/> (canvas units, set by the screen that is open) and the player is centred in the free space to its right,
    /// whatever the width of the canvas (16:9, 16:10, 21:9, 4:3). <see cref="Parallax"/> nudges the camera so the backdrop drifts a little when the focus moves across the hub tiles.
    /// </summary>
    public sealed class GlassStage : MonoBehaviour
    {
        public const string BackdropSceneName = "MenuBackdrop_Glass";

        public Transform Anchor;
        public Camera Cam;
        /// <summary>The menu canvas rect (its width gives the free space right of the content); null = a 16:9 / wider canvas of the screen aspect.</summary>
        public RectTransform CanvasRect;
        /// <summary>Right edge of the UI content in canvas units (1920 wide design): the hub ends at 1308, the team grid at 1824.</summary>
        public float ContentRight = 1308f;
        /// <summary>Sway amplitude (degrees) and period (seconds) of the slow turntable.</summary>
        public float SwayDegrees = 11f, SwayPeriod = 18f;
        /// <summary>Camera drift target in -1..1 (x, y), eased; the hub sets it from the focused tile.</summary>
        public Vector2 Parallax;
        float _t, _px, _py;
        Vector3 _camBase;
        float _aspect;

        public static readonly Vector3 CamPosition = new Vector3(0.2f, 1.30f, 4.7f);
        public static readonly Vector3 PlayerFocus = new Vector3(0f, 0.90f, 0f);
        /// <summary>Kept for the QA renders that aim the camera by a world point (the old layout).</summary>
        public static readonly Vector3 CamTarget = new Vector3(1.25f, 1.05f, 0f);
        public const float DefaultFov = 37f;

        public static GlassStage Attach(GameObject host, Transform anchor, Camera cam)
        {
            var s = host.AddComponent<GlassStage>();
            s.Anchor = anchor; s.Cam = cam;
            Frame(cam, anchor);
            if (cam != null) s._camBase = cam.transform.position;
            if (Application.isPlaying) s.StartCoroutine(s.LoadBackdrop());   // edit-mode QA renders load the backdrop scene themselves
            return s;
        }

        /// <summary>Where the player's centre sits on the screen, as a fraction of the screen width, for a canvas of <paramref name="canvasW"/> units whose content ends at <paramref name="contentRight"/>.</summary>
        public static float PlayerScreenFraction(float canvasW, float contentRight)
        {
            if (canvasW <= 1f) return 0.8f;
            float free = canvasW - contentRight;
            float cx = free >= 560f ? contentRight + free * 0.5f : canvasW - 300f;   // no room right of the content: tuck him against the right edge, behind the glass
            return Mathf.Clamp(cx / canvasW, 0.55f, 0.92f);
        }

        /// <summary>The point the camera looks at so that the player appears at screen fraction <paramref name="frac"/> (0..1 from the left) for the camera's field of view and <paramref name="aspect"/>.</summary>
        public static Vector3 AimPoint(Vector3 camPos, float fov, float aspect, float frac)
        {
            Vector3 d = PlayerFocus - camPos;
            float tanH = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * Mathf.Max(0.5f, aspect);
            float theta = Mathf.Atan((frac * 2f - 1f) * tanH) * Mathf.Rad2Deg;
            return camPos + Quaternion.Euler(0f, -theta, 0f) * d;
        }

        /// <summary>Frames the camera for the glass layout with the default content width (shared with the QA renders).</summary>
        public static void Frame(Camera cam, Transform anchor)
        {
            if (cam == null) return;
            Frame(cam, cam.aspect, PlayerScreenFraction(Mathf.Max(1920f, 1080f * cam.aspect), 1308f));
        }

        public static void Frame(Camera cam, float aspect, float frac)
        {
            if (cam == null) return;
            cam.transform.position = CamPosition;
            cam.fieldOfView = DefaultFov;
            cam.transform.LookAt(AimPoint(CamPosition, DefaultFov, aspect, frac));
        }

        /// <summary>The canvas width in units for the current screen (falls back to the expand rule of a 1920 x 1080 reference).</summary>
        float CanvasWidth(float aspect)
        {
            if (CanvasRect != null && CanvasRect.rect.width > 1f) return CanvasRect.rect.width;
            return Mathf.Max(1920f, 1080f * aspect);
        }

        /// <summary>Re-aims the camera for the given aspect (edit-mode QA renders call this per size; in play it runs every frame).</summary>
        public void Refresh(float aspect)
        {
            if (Cam == null) return;
            _aspect = aspect;
            float frac = PlayerScreenFraction(CanvasWidth(aspect), ContentRight);
            Vector3 pos = CamPosition + new Vector3(_px * 0.22f + Mathf.Sin(_t * 0.11f) * 0.10f, _py * 0.06f + Mathf.Sin(_t * 0.07f) * 0.03f, 0f);
            Cam.transform.position = pos;
            Cam.transform.LookAt(AimPoint(pos, Cam.fieldOfView, aspect, frac));
            if (Anchor != null)
            {
                // face the camera, then sway a few degrees either way (a slow, subtle turntable)
                Vector3 to = pos - Anchor.position;
                float face = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                float sway = Mathf.Sin(_t * Mathf.PI * 2f / Mathf.Max(2f, SwayPeriod)) * SwayDegrees;
                Anchor.rotation = Quaternion.Euler(0f, face + 4f + sway, 0f);
            }
        }

        /// <summary>Hides the old stage floor (the real field takes over) and removes nothing else.</summary>
        public static void HideStageFloor()
        {
            var floor = GameObject.Find("StageFloor");
            if (floor != null) floor.SetActive(false);
        }

        IEnumerator LoadBackdrop()
        {
            if (!Application.CanStreamedLevelBeLoaded(BackdropSceneName)) yield break;
            var op = SceneManager.LoadSceneAsync(BackdropSceneName, LoadSceneMode.Additive);
            if (op == null) yield break;
            while (!op.isDone) yield return null;
            HideStageFloor();
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-3.2f * Time.unscaledDeltaTime);
            _px = Mathf.Lerp(_px, Parallax.x, k); _py = Mathf.Lerp(_py, Parallax.y, k);
            float aspect = Cam != null ? (Cam.pixelHeight > 0 ? (float)Cam.pixelWidth / Cam.pixelHeight : Cam.aspect) : 1.78f;
            Refresh(aspect);
        }
    }
}
