using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// The live 3D background of the glass menus: the menu's photoreal player on a slow turntable in front of the stadium at night. The stadium is a separate scene (MenuBackdrop_Glass, built by
    /// GlassSceneBuilder) loaded additively, so the Classic menu is untouched; if the scene is not in the build the player simply stands on the old stage floor. The camera is framed so the
    /// player sits right of centre, leaving the left two thirds for the glass UI.
    /// </summary>
    public sealed class GlassStage : MonoBehaviour
    {
        public const string BackdropSceneName = "MenuBackdrop_Glass";

        public Transform Anchor;
        public Camera Cam;
        /// <summary>Turntable speed in degrees per second.</summary>
        public float TurnSpeed = 8f;
        float _yaw, _t;
        Vector3 _camBase;

        public static readonly Vector3 CamPosition = new Vector3(0.2f, 1.30f, 4.7f);
        public static readonly Vector3 CamTarget = new Vector3(1.25f, 1.05f, 0f);

        public static GlassStage Attach(GameObject host, Transform anchor, Camera cam)
        {
            var s = host.AddComponent<GlassStage>();
            s.Anchor = anchor; s.Cam = cam;
            Frame(cam, anchor);
            s._yaw = anchor != null ? anchor.eulerAngles.y : 0f;
            if (cam != null) s._camBase = cam.transform.position;
            if (Application.isPlaying) s.StartCoroutine(s.LoadBackdrop());   // edit-mode QA renders load the backdrop scene themselves
            return s;
        }

        /// <summary>Frames the camera for the glass layout (shared with the QA renders).</summary>
        public static void Frame(Camera cam, Transform anchor)
        {
            if (cam == null) return;
            cam.transform.position = CamPosition;
            cam.transform.LookAt(CamTarget);
            cam.fieldOfView = 32f;
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
            if (Anchor != null)
            {
                _yaw += TurnSpeed * Time.unscaledDeltaTime;
                Anchor.rotation = Quaternion.Euler(0f, _yaw, 0f);
            }
            if (Cam != null)
            {
                // a barely perceptible dolly so the bokeh behind the glass is never completely still
                _t += Time.unscaledDeltaTime;
                Cam.transform.position = _camBase + new Vector3(Mathf.Sin(_t * 0.11f) * 0.12f, Mathf.Sin(_t * 0.07f) * 0.03f, 0f);
                Cam.transform.LookAt(CamTarget);
            }
        }
    }
}
