using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Broadcast overlays drawn on the grass (flat strips under the players, never over them): the blue line of scrimmage, the yellow
    /// first-down line, and the violet ring under the player the user controls.
    /// </summary>
    public sealed class FieldMarkers : MonoBehaviour
    {
        const float LineY = 0.022f, WidthM = 48.76f;
        Transform _los, _fd, _ring;
        Renderer _losR, _fdR, _ringR;
        Transform _follow;
        float _pulse;

        public static FieldMarkers Create(Transform parent)
        {
            var go = new GameObject("FieldMarkers");
            go.transform.SetParent(parent, false);
            var m = go.AddComponent<FieldMarkers>();
            var rt = PixRuntimeAssets.Load();
            m._los = Strip(go.transform, "LineOfScrimmage", rt != null ? rt.lineLos : null, WidthM, 0.28f, out m._losR);
            m._fd = Strip(go.transform, "FirstDownLine", rt != null ? rt.lineFirstDown : null, WidthM, 0.30f, out m._fdR);
            m._ring = Strip(go.transform, "UserRing", rt != null ? rt.ringMarker : null, 1.9f, 1.9f, out m._ringR);
            m._ring.localPosition = new Vector3(0f, LineY + 0.01f, 0f);
            m.HideAll();
            return m;
        }

        static Transform Strip(Transform parent, string name, Material mat, float w, float d, out Renderer r)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name; q.transform.SetParent(parent, false);
            Destroy(q.GetComponent<Collider>());
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            q.transform.localScale = new Vector3(w, d, 1f);
            r = q.GetComponent<Renderer>();
            if (mat != null) r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return q.transform;
        }

        public void HideAll() { _los.gameObject.SetActive(false); _fd.gameObject.SetActive(false); _ring.gameObject.SetActive(false); }

        /// <summary>Spots are 0..100 from the offense's own goal line.</summary>
        public void SetLines(PlayFrame frame, float losSpot, float lineToGainSpot, bool showLos, bool showFirstDown)
        {
            _los.gameObject.SetActive(showLos);
            _los.position = new Vector3(0f, LineY, frame.SpotToZ(losSpot));
            bool fd = showFirstDown && lineToGainSpot < 99.5f && lineToGainSpot > 0.5f;
            _fd.gameObject.SetActive(fd);
            _fd.position = new Vector3(0f, LineY + 0.004f, frame.SpotToZ(lineToGainSpot));
        }

        public void SetRing(Transform follow)
        {
            _follow = follow;
            _ring.gameObject.SetActive(follow != null);
        }

        void LateUpdate()
        {
            if (_follow == null || !_ring.gameObject.activeSelf) return;
            _pulse += Time.deltaTime;
            Vector3 p = _follow.position; p.y = LineY + 0.012f;
            _ring.position = p;
            float s = 1.9f + 0.08f * Mathf.Sin(_pulse * 4f);
            _ring.localScale = new Vector3(s, s, 1f);
            _ring.rotation = Quaternion.Euler(90f, _pulse * 40f, 0f);
        }
    }
}
