using System.Collections.Generic;
using PixFootball.Art;
using UnityEngine;
using UnityEngine.Rendering;

namespace PixFootball.Gameplay
{
    public enum PlayerSize { Small, Medium, Large }

    /// <summary>Spawns a PIX_Player_* prefab from the ArtLibrary and applies a team-coloured uniform material instance.</summary>
    public class PlayerAvatar : MonoBehaviour
    {
        static readonly int Primary = Shader.PropertyToID("_Primary_Color");
        static readonly int Secondary = Shader.PropertyToID("_Secondary_Color");
        static readonly int Tertiary = Shader.PropertyToID("_Terciary_Color");

        /// <summary>Motus clips are authored facing +X; rotate the body so the clip forward is world +Z.</summary>
        public const float ModelYaw = -90f;
        public Animator Animator;
        public Transform Root;
        public Transform Head, RightHand, LeftHand, RightForearm, LeftForearm, Chest, RightUpperLeg, RightLowerLeg, LeftFoot, RightFoot;
        public Material HelmetInstance { get { if (_photo != null) return _photo.HelmetMaterial; return _useLegacy || _v6Helmet == null ? _legacyHelmet : _v6Helmet; } }
        /// <summary>Distance based helmet / face mask level of detail (v0.4 performance).</summary>
        public PlayerLod Lod;
        readonly List<Renderer> _helmetRenderers = new List<Renderer>();
        /// <summary>The uniform material instance in use (V6 PIX/Lit or the legacy v0.5 shader graph).</summary>
        public Material UniformInstance { get { if (_photo != null) return _photo.JerseyMaterial; return _useLegacy || _v6Uniform == null ? _legacyUniform : _v6Uniform; } }
        Material _legacyUniform, _v6Uniform, _legacyHelmet, _v6Helmet;
        readonly List<Renderer> _uniformRenderers = new List<Renderer>();
        bool _useLegacy = true;
        PixFootball.Art.PixLookAssets _look;
        readonly List<Renderer> _headRenderers = new List<Renderer>();
        bool _headHidden;

        /// <summary>The photoreal body of this avatar (v0.6 default), null for the legacy Draft Fever bodies.</summary>
        public PhotoPlayer Photo { get { return _photo; } }
        public bool IsPhoto { get { return _photo != null; } }
        PhotoPlayer _photo;
        PlayerSize _size;
        PlayerAppearance _lastAppearance; bool _hasAppearance;

        public static PlayerAvatar Spawn(Transform parent, PlayerSize size, TeamColors colors, string name = "Player")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var av = go.AddComponent<PlayerAvatar>();
            av.Build(size, colors);
            return av;
        }

        void Build(PlayerSize size, TeamColors colors)
        {
            var lib = ArtLibrary.Load();
            _size = size;
            string key = size == PlayerSize.Small ? "player_small" : size == PlayerSize.Large ? "player_large" : "player_medium";
            var prefab = lib != null ? lib.Prefab(key) : null;
            if (lib != null && PixSettings.PhotoPlayers)
            {
                string pk = size == PlayerSize.Small ? "photo_player_small" : size == PlayerSize.Large ? "photo_player_large" : "photo_player_medium";
                var pp = lib.Prefab(pk);
                if (pp != null && pp.GetComponent<PhotoPlayer>() != null) { prefab = pp; key = pk; }
                else Debug.LogWarning("[PlayerAvatar] photoreal prefab '" + pk + "' not available: falling back to the legacy body");
            }
            if (prefab == null)
            {
                Debug.LogWarning("[PlayerAvatar] ArtLibrary/prefab '" + key + "' not available");
                return;
            }
            var inst = Instantiate(prefab, transform);
            inst.name = "Body";
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
            Root = inst.transform;
            Animator = inst.GetComponent<Animator>();
            if (Animator == null) Animator = inst.AddComponent<Animator>();
            Animator.applyRootMotion = false;
            Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _photo = inst.GetComponent<PhotoPlayer>();
            if (_photo != null) { BuildPhoto(inst); return; }

            var rt = PixRuntimeAssets.Load();
            Material baseUniform = rt != null ? rt.uniformMaterial : null;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                if (n.StartsWith("Torso") || n.StartsWith("Legs"))
                {
                    if (_legacyUniform == null)
                    {
                        var src = baseUniform != null ? baseUniform : r.sharedMaterial;
                        _legacyUniform = new Material(src) { name = "PIX_Uniform_Team" };
                        _look = PixFootball.Art.PixLookAssets.Load();
                        if (_look != null && _look.uniformMaterial != null)
                        {
                            _v6Uniform = new Material(_look.uniformMaterial) { name = "PIX_Uniform_V6_Team" };
                            PixLook.SetNumberPlacements(_v6Uniform, _look);
                        }
                        SetColors(colors);
                    }
                    _uniformRenderers.Add(r);
                }
                // small parts do not need to cast shadows (22 players x several shadow maps is the biggest GPU cost of a match)
                if (n.StartsWith("Eyes") || n.StartsWith("FM_") || n.StartsWith("visor") || n.StartsWith("Hands")) r.shadowCastingMode = ShadowCastingMode.Off;
                if (n.StartsWith("Head") || n.StartsWith("Eyes")) _headRenderers.Add(r);
                if (r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name.Contains("Helmet_Shader"))
                {
                    if (_legacyHelmet == null)
                    {
                        _legacyHelmet = new Material(r.sharedMaterial) { name = "PIX_Helmet_Team" };
                        var lk = PixFootball.Art.PixLookAssets.Load();
                        if (lk != null && lk.helmetMaterial != null) _v6Helmet = new Material(lk.helmetMaterial) { name = "PIX_Helmet_V6_Team" };
                    }
                    _helmetRenderers.Add(r);
                }
            }
            ApplyLookMode(true);
            Head = Animator.GetBoneTransform(HumanBodyBones.Head);
            RightHand = Animator.GetBoneTransform(HumanBodyBones.RightHand);
            LeftHand = Animator.GetBoneTransform(HumanBodyBones.LeftHand);
            RightForearm = Animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            LeftForearm = Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Chest = Animator.GetBoneTransform(HumanBodyBones.Chest);
            RightUpperLeg = Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            RightLowerLeg = Animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            LeftFoot = Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            RightFoot = Animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (Head != null) foreach (var r in Head.GetComponentsInChildren<Renderer>(true)) if (!_headRenderers.Contains(r)) _headRenderers.Add(r);
            Lod = new PlayerLod(Head);
            SetupHelmetFrame();   // after Head: the frame is measured against the Helmet holder under the head bone
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (smr.name.StartsWith("Torso") || smr.name.StartsWith("Legs") || smr.name.StartsWith("Arms") || smr.name.StartsWith("Hands") || smr.name.StartsWith("Shoes") || smr.name.StartsWith("Head")) Lod.AddSkinned(smr); else if (smr.name.StartsWith("Eyes")) Lod.AddFarHidden(smr);
            foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true)) { smr.quality = SkinQuality.Bone4; smr.updateWhenOffscreen = false; }
        }

        // ---- helmet frame (v0.6): the shader places the stripe and the crest in "helmet space" (x right, y up, z forward, 1 = half extent of the shell) relative to the
        // Helmet holder (a child of the head bone) so every part of the helmet that shares the material (shell, liners, bumpers) agrees, whatever its own mesh origin.
        Transform _helmetHolder;
        Matrix4x4 _helmetBasis;      // holder-local -> helmet space (rotation, then centre / extent scale)
        Matrix4x4 _helmetNormalBasis;
        static readonly int[] HW = { Shader.PropertyToID("_PixHW0"), Shader.PropertyToID("_PixHW1"), Shader.PropertyToID("_PixHW2") };
        static readonly int[] HN = { Shader.PropertyToID("_PixHN0"), Shader.PropertyToID("_PixHN1"), Shader.PropertyToID("_PixHN2") };

        void SetupHelmetFrame()
        {
            _helmetHolder = null;
            if (_v6Helmet == null || Head == null) return;
            _helmetHolder = Head.Find("Helmet");
            if (_helmetHolder == null) return;
            // bounds of the shell in holder space at bind pose
            bool any = false; Bounds b = new Bounds();
            foreach (var r in _helmetHolder.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                if (r.name.StartsWith("FM_") || r.name.StartsWith("visor") || r.transform.parent.name == "FaceMask" || r.transform.parent.name == "Visor") continue;
                var mb = mf.sharedMesh.bounds; var toHolder = _helmetHolder.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = toHolder.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (!any) { _helmetHolder = null; return; }
            // the avatar's own axes (right / up / forward) expressed in holder space
            Matrix4x4 w2h = _helmetHolder.worldToLocalMatrix;
            Vector3 right = w2h.MultiplyVector(transform.right).normalized, up = w2h.MultiplyVector(transform.up).normalized, fwd = w2h.MultiplyVector(transform.forward).normalized;
            Matrix4x4 rot = Matrix4x4.identity;
            rot.SetRow(0, new Vector4(right.x, right.y, right.z, 0)); rot.SetRow(1, new Vector4(up.x, up.y, up.z, 0)); rot.SetRow(2, new Vector4(fwd.x, fwd.y, fwd.z, 0));
            _helmetNormalBasis = rot;
            // extents along the avatar axes from the eight corners
            Vector3 cen = rot.MultiplyPoint3x4(b.center);
            Vector3 mn = new Vector3(1e9f, 1e9f, 1e9f), mx = -mn;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 q = rot.MultiplyPoint3x4(c); mn = Vector3.Min(mn, q); mx = Vector3.Max(mx, q);
            }
            Vector3 mid = (mn + mx) * 0.5f, half = Vector3.Max((mx - mn) * 0.5f, new Vector3(0.01f, 0.01f, 0.01f));
            Matrix4x4 scale = Matrix4x4.Scale(new Vector3(1f / half.x, 1f / half.y, 1f / half.z)) * Matrix4x4.Translate(-mid);
            _helmetBasis = scale * rot;
            if (_look != null) _v6Helmet.SetVector(PixLook.PixHelmetDecal, _look.helmetDecal);
            UpdateHelmetFrame();
        }

        public void UpdateHelmetFrame()
        {
            if (_v6Helmet == null || _helmetHolder == null) return;
            Matrix4x4 m = _helmetBasis * _helmetHolder.worldToLocalMatrix;
            Matrix4x4 n = _helmetNormalBasis * _helmetHolder.worldToLocalMatrix;
            for (int i = 0; i < 3; i++) { _v6Helmet.SetVector(HW[i], m.GetRow(i)); _v6Helmet.SetVector(HN[i], n.GetRow(i)); }
        }

        void LateUpdate() { if (_photo == null && !_useLegacy) UpdateHelmetFrame(); }


        // ---------------------------------------------------------------------------------------------------- photoreal body (v0.6 default)
        /// <summary>Photoreal prefab: no name-prefix material pass (PHR-022), the PhotoPlayer owns its materials, parts and level of detail.</summary>
        void BuildPhoto(GameObject inst)
        {
            _photo.autoLod = false;        // PlayerLod drives the level from the view tick (hysteresis, ViewCamera, quality bias)
            _photo.manageHelmetLod = true; // helmet shell / mask / pads are parts of the photo prefab
            _photo.Init();
            Head = Animator.GetBoneTransform(HumanBodyBones.Head);
            RightHand = Animator.GetBoneTransform(HumanBodyBones.RightHand);
            LeftHand = Animator.GetBoneTransform(HumanBodyBones.LeftHand);
            RightForearm = Animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            LeftForearm = Animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Chest = Animator.GetBoneTransform(HumanBodyBones.Chest);
            RightUpperLeg = Animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            RightLowerLeg = Animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            LeftFoot = Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            RightFoot = Animator.GetBoneTransform(HumanBodyBones.RightFoot);
            Lod = new PlayerLod(null);
            Lod.AttachPhoto(_photo);
            _look = PixFootball.Art.PixLookAssets.Load();
            _photo.SetLevel(0);
        }

        /// <summary>Body type that goes with the pool this avatar came from (small = receivers / backs, large = linemen).</summary>
        PhotoBody BodyType { get { return _size == PlayerSize.Small ? PhotoBody.Small : _size == PlayerSize.Large ? PhotoBody.Large : PhotoBody.Medium; } }

        /// <summary>Gives a pooled photoreal body the face, skin and kit options of one player. Deterministic from the player id (the same man always looks the same); legacy bodies ignore it.</summary>
        public void SetAppearance(int playerId, int roleHint)
        {
            if (_photo == null) return;
            var a = PlayerAppearance.FromSeed(playerId, BodyType, roleHint);
            if (_hasAppearance && _lastAppearance.Key() == a.Key()) return;
            _lastAppearance = a; _hasAppearance = true;
            _photo.ApplyAppearance(a);
        }

        void SetPhotoJersey(in JerseyLook look)
        {
            var jm = _photo.JerseyMaterial;
            if (jm != null) { PixLook.ApplyJersey(jm, look); _photo.ApplyLayout(jm); }
            var pm = _photo.PantsMaterial;
            if (pm != null)
            {
                var bare = look; bare.HasNumber = false; bare.Name = ""; bare.Wordmark = "";   // dirt on the pants, no lettering
                PixLook.ApplyJersey(pm, bare);
            }
            var hm = _photo.HelmetMaterial;
            if (hm != null) { hm.SetVector(PixLook.PixScuff, new Vector4(look.Dirt.HelmetScuff, 3f, 0f, 0f)); PixLook.ApplyHelmet(hm, look); }
        }

        public void SetVisualYaw(float offsetDegrees)
        {
            if (Root != null) Root.localRotation = Quaternion.Euler(0f, ModelYaw + offsetDegrees, 0f);
        }

        static readonly int HelmetPrimary = Shader.PropertyToID("_Primary_Color"), HelmetSecondary = Shader.PropertyToID("_Secondary_Color"), HelmetTertiary = Shader.PropertyToID("_Terciary_Color");

        /// <summary>Helmet shell in the team colours (primary shell, secondary and tertiary on the detail masks).</summary>
        public void SetHelmetColors(TeamColors c)
        {
            if (_photo != null) { _photo.SetHelmetColors(c.Primary, c.Secondary, c.Tertiary); return; }
            if (_legacyHelmet != null) { _legacyHelmet.SetColor(HelmetPrimary, c.Primary); _legacyHelmet.SetColor(HelmetSecondary, c.Secondary); _legacyHelmet.SetColor(HelmetTertiary, c.Tertiary); }
            if (_v6Helmet != null) { _v6Helmet.SetColor(PixLook.PixPrimary, c.Primary); _v6Helmet.SetColor(PixLook.PixSecondary, c.Secondary); _v6Helmet.SetColor(PixLook.PixTertiary, c.Tertiary); }
        }

        public void SetColors(TeamColors c)
        {
            if (_photo != null) { _photo.SetTeamColors(c.Primary, c.Secondary, c.Tertiary); return; }
            if (_legacyUniform != null)
            {
                _legacyUniform.SetColor(Primary, c.Primary);
                _legacyUniform.SetColor(Secondary, c.Secondary);
                _legacyUniform.SetColor(Tertiary, c.Tertiary);
            }
            if (_v6Uniform != null)
            {
                _v6Uniform.SetColor(PixLook.PixPrimary, c.Primary);
                _v6Uniform.SetColor(PixLook.PixSecondary, c.Secondary);
                _v6Uniform.SetColor(PixLook.PixTertiary, c.Tertiary);
            }
        }

        /// <summary>Jersey number, number colours and dirt of the player wearing this body now (v0.6; the legacy look ignores it).</summary>
        public void SetJersey(in JerseyLook look)
        {
            if (_photo != null) { SetPhotoJersey(look); return; }
            if (_v6Uniform != null) PixLook.ApplyJersey(_v6Uniform, look);
            if (_v6Helmet != null) { _v6Helmet.SetVector(PixLook.PixScuff, new Vector4(look.Dirt.HelmetScuff, 3f, 0f, 0f)); PixLook.ApplyHelmet(_v6Helmet, look); }
        }

        /// <summary>Switches between the V6 PIX/Lit materials and the v0.5 shader graphs (before / after gallery).</summary>
        public void ApplyLookMode(bool force = false)
        {
            if (_photo != null) return;   // the photoreal body only has the PIX/Lit look
            bool legacy = PixLook.Legacy || _v6Uniform == null;
            if (!force && legacy == _useLegacy) return;
            _useLegacy = legacy;
            foreach (var r in _uniformRenderers)
            {
                if (r == null) continue;
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = UniformInstance;
                r.sharedMaterials = mats;
            }
            foreach (var r in _helmetRenderers)
            {
                if (r == null) continue;
                if (r.sharedMaterial == null || r.sharedMaterial.shader == null) continue;
                bool shell = r.sharedMaterial == _legacyHelmet || r.sharedMaterial == _v6Helmet || r.sharedMaterial.shader.name.Contains("Helmet_Shader");
                if (!shell) continue;
                var hm = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < hm.Length; i++) hm[i] = HelmetInstance;
                r.sharedMaterials = hm;
            }
        }

        /// <summary>Hides the head/helmet from the camera (first-person) but keeps their shadow.</summary>
        List<Renderer> _fpArms; List<ShadowCastingMode> _fpArmShadow;
        public void HideHead(bool hide)
        {
            if (_photo != null) { _headHidden = hide; _photo.HideHead(hide); return; }
            if (_headHidden == hide) return;
            _headHidden = hide;
            // v0.6 first person: the helmet camera sat inside the player's own forearms and hands (a skin-coloured blob filling the screen, QA-127 / QA-202): hide them from the camera too
            if (_fpArms == null)
            {
                _fpArms = new List<Renderer>(); _fpArmShadow = new List<ShadowCastingMode>();
                foreach (var ar in GetComponentsInChildren<Renderer>(true)) if (ar.name.StartsWith("Arms") || ar.name.StartsWith("Hands")) { _fpArms.Add(ar); _fpArmShadow.Add(ar.shadowCastingMode); }
            }
            for (int i = 0; i < _fpArms.Count; i++) if (_fpArms[i] != null) _fpArms[i].shadowCastingMode = hide ? ShadowCastingMode.ShadowsOnly : _fpArmShadow[i];
            if (_headShadow == null) { _headShadow = new Dictionary<Renderer, ShadowCastingMode>(); foreach (var hr in _headRenderers) if (hr != null) _headShadow[hr] = hr.shadowCastingMode; }   // remember Eyes / FM_ / visor = Off (QA-704)
            foreach (var r in _headRenderers)
            {
                if (r == null) continue;
                ShadowCastingMode orig; if (!_headShadow.TryGetValue(r, out orig)) orig = ShadowCastingMode.On;
                r.shadowCastingMode = hide ? ShadowCastingMode.ShadowsOnly : orig;
            }
        }

        Dictionary<Renderer, ShadowCastingMode> _headShadow;

        /// <summary>The four per-avatar material instances (legacy / V6 uniform and helmet) belong to this avatar: destroy them with it (QA-703).</summary>
        void OnDestroy()
        {
            foreach (var m in new[] { _legacyUniform, _v6Uniform, _legacyHelmet, _v6Helmet }) { if (m == null) continue; if (Application.isPlaying) Destroy(m); else DestroyImmediate(m); }
        }
    }
}
