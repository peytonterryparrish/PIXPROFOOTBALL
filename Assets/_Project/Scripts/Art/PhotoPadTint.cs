using UnityEngine;

namespace PixFootball.Art
{
    /// <summary>
    /// Team tint of the shoulder pads (v0.7 photoreal roster): the shell material is a PIX/Lit uniform-mode material, so it needs its own per-player instance and the kit colours.
    /// Self-contained on purpose (PhotoPlayer is not touched): it follows the colours that PhotoPlayer.SetTeamColors writes to the jersey instance, a few times per second, and
    /// gives the pads the kit's secondary colour as shell (the collar / neck roll is the only part that shows under the jersey).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhotoPadTint : MonoBehaviour
    {
        static readonly int Primary = Shader.PropertyToID("_PixPrimary"), Secondary = Shader.PropertyToID("_PixSecondary"), Tertiary = Shader.PropertyToID("_PixTertiary");

        [Tooltip("All LOD renderers of the Pads part; sharedMaterials = [shell, trim, metal].")]
        public Renderer[] pads = new Renderer[0];
        public PhotoPlayer photo;
        public int interval = 20;

        Material _shell;
        Color _p, _s, _t;
        bool _built;

        void Awake() { Build(); }

        void Start() { Sync(true); }

        void Build()
        {
            if (_built) return;
            _built = true;
            if (photo == null) photo = GetComponent<PhotoPlayer>();
            foreach (var r in pads)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (mats == null || mats.Length == 0 || mats[0] == null) continue;
                if (_shell == null) _shell = new Material(mats[0]) { name = mats[0].name + "_" + gameObject.name };
                mats[0] = _shell;
                r.sharedMaterials = mats;
            }
        }

        void LateUpdate()
        {
            if (interval > 0 && (Time.frameCount + GetHashCode()) % interval == 0) Sync(false);
        }

        /// <summary>Copies the kit colours from the jersey instance (or call SetColors directly).</summary>
        public void Sync(bool force)
        {
            if (photo == null || _shell == null) return;
            var jm = photo.JerseyMaterial;
            if (jm == null || !jm.HasProperty(Primary)) return;
            Color p = jm.GetColor(Primary), s = jm.GetColor(Secondary), t = jm.GetColor(Tertiary);
            if (!force && p == _p && s == _s && t == _t) return;
            SetColors(p, s, t);
        }

        public void SetColors(Color primary, Color secondary, Color tertiary)
        {
            _p = primary; _s = secondary; _t = tertiary;
            if (_shell == null) return;
            _shell.SetColor(Primary, secondary); _shell.SetColor(Secondary, primary); _shell.SetColor(Tertiary, tertiary);
        }
    }
}
