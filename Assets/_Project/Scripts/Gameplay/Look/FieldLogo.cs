using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// The home team's crest painted at midfield (v0.6, All-Pro 2K8 style): a flat quad just above the turf using the weather-aware PIX/Lit marking material
    /// (so it chips, scuffs, wets and snows over like the rest of the paint). The painted league shield of the field texture is cleared by V6ArtBuilder.BuildFieldMarkings.
    /// Up on the crest points to +X, so it reads upright from the main broadcast sideline.
    /// </summary>
    public sealed class FieldLogo : MonoBehaviour
    {
        public static FieldLogo Instance { get; private set; }
        public const float Height = 0.018f;

        public static FieldLogo Create(Transform parent, string crestKey, float width = 12f)
        {
            if (Instance != null) Destroy(Instance.gameObject);
            var tex = PixLook.CrestTexture(crestKey);
            var assets = PixLook.Assets;
            if (tex == null || assets == null || assets.markingsMaterial == null) return null;
            ClearPaintedShield(assets.markingsMaterial);
            var go = new GameObject("FieldLogo");
            if (parent != null) go.transform.SetParent(parent, false);
            var logo = go.AddComponent<FieldLogo>();
            Instance = logo;
            float h = width * tex.height / Mathf.Max(1f, tex.width);
            var mesh = new Mesh { name = "FieldLogoQuad" };
            mesh.vertices = new[] { new Vector3(-width / 2, 0, -h / 2), new Vector3(-width / 2, 0, h / 2), new Vector3(width / 2, 0, h / 2), new Vector3(width / 2, 0, -h / 2) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.tangents = new[] { new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1), new Vector4(1, 0, 0, 1) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mat = new Material(assets.markingsMaterial) { name = "PIX_FieldLogo(runtime)" };
            mat.SetTexture("_BaseColorMap", tex); mat.SetTexture("_MainTex", tex);
            mat.SetColor("_BaseColor", new Color(0.96f, 0.96f, 0.96f, 1f));
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = true;
            go.transform.SetPositionAndRotation(new Vector3(0f, Height, 0f), Quaternion.Euler(0f, 90f, 0f));
            return logo;
        }

        /// <summary>
        /// C-007 (v0.7 note 21): exactly one logo at midfield. The weather clones (WX_*) and the original stadium material of the field markings use the texture that still has the painted
        /// league shield; every renderer that uses one of them is moved onto the cleaned markings material (the home crest is drawn by this class).
        /// </summary>
        static void ClearPaintedShield(Material cleaned)
        {
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            for (int k = 0; k < renderers.Length; k++)
            {
                var r = renderers[k];
                if (r == null || r.GetComponent<FieldLogo>() != null) continue;
                var mats = r.sharedMaterials; bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m == cleaned || m.name.IndexOf("FieldLogo", System.StringComparison.Ordinal) >= 0) continue;
                    if (m.name.IndexOf("FieldMarkings", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    mats[i] = cleaned; dirty = true;
                }
                if (dirty) r.sharedMaterials = mats;
            }
        }

        /// <summary>QA-706: turns the crest so it reads upright from the camera that frames the play (the user camera looks along the field, the broadcast camera across it).</summary>
        int _half = -1;
        /// <summary>v0.9 (GFX-435): the crest is oriented ONCE per half (it used to re-rotate to the camera axis at every snap, so it spun between plays); half = 0 for the 1st / 2nd quarter, 1 for the 2nd half.</summary>
        public void FaceCamera(Camera cam, int half)
        {
            if (half == _half) return;
            if (cam == null) return;
            _half = half;
            FaceCamera(cam);
        }

        public void FaceCamera(Camera cam)
        {
            if (cam == null) return;
            Vector3 f = cam.transform.forward; f.y = 0f;
            if (f.sqrMagnitude < 0.01f) return;
            float yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            // snap to the nearest of the four field axes: the logo stays aligned with the yard lines
            yaw = Mathf.Round(yaw / 90f) * 90f;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }
    }
}
