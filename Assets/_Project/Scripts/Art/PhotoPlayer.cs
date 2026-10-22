using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PixFootball.Art
{
    /// <summary>
    /// Runtime face of the photoreal player prefab (PIX_PhotoPlayer_{Small,Medium,Large}): per-player material instances (team colours), appearance (head shapes, skin tone,
    /// facial hair, brows, eyes, sleeves, gloves, wristbands, visor) and the distance based level of detail of every part.
    /// Self-contained and independent of Gameplay: the lead wires it in PlayerAvatar (see Docs/QA/photoreal.md "Integration hooks").
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PhotoPlayer : MonoBehaviour
    {
        static readonly int Primary = Shader.PropertyToID("_PixPrimary"), Secondary = Shader.PropertyToID("_PixSecondary"), Tertiary = Shader.PropertyToID("_PixTertiary");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), BaseColorMap = Shader.PropertyToID("_BaseColorMap");
        static readonly int Hue = Shader.PropertyToID("_Hue"), Saturation = Shader.PropertyToID("_Saturation");
        static readonly int NumFront = Shader.PropertyToID("_PixNumFront"), NumBack = Shader.PropertyToID("_PixNumBack"), NumShoulderL = Shader.PropertyToID("_PixNumShoulderL"), NumShoulderR = Shader.PropertyToID("_PixNumShoulderR");
        static readonly int NamePlace = Shader.PropertyToID("_PixNamePlace"), WordPlace = Shader.PropertyToID("_PixWordPlace"), TextInfo = Shader.PropertyToID("_PixTextInfo"), Letters = Shader.PropertyToID("_PixLetters");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness"), Metallic = Shader.PropertyToID("_Metallic");

        /// <summary>One body part and its level-of-detail renderers, most detailed first.</summary>
        [Serializable]
        public class Part
        {
            public string key;
            public Renderer[] lods;
            [Tooltip("true: not drawn beyond the last LOD it has (eyes, hair shells, visor); false: the last LOD is used at every distance.")]
            public bool hideBeyond;
            [NonSerialized] public bool visible = true;
        }

        public PhotoPlayerLibrary library;
        public Animator animator;
        public Transform headBone;
        public Part[] parts = new Part[0];
        [Tooltip("Distance thresholds (metres) of the four levels: below [0] LOD0, below [1] LOD1, below [2] LOD2, beyond LOD3. Same defaults as PlayerLod.")]
        public float near = 11f, mid = 26f, far = 60f;
        public bool autoLod = true;
        [Tooltip("false when PlayerLod already switches the Helmet gear groups (the prefab keeps the Helmet / Helmet_Shell / FaceMask / Visor layout PlayerLod expects).")]
        public bool manageHelmetLod = true;

        /// <summary>Camera used for the automatic level of detail (falls back to Camera.main).</summary>
        public static Camera ViewCamera;
        public static float Bias = 1f;
        /// <summary>v0.6: facial hair shells (alpha-clip strands) read as speckle at close range (PHR-018): off until hand-authored hair cards exist.</summary>
        public static bool ShowFacialHair = false;

        readonly Dictionary<string, Part> _map = new Dictionary<string, Part>();
        readonly Dictionary<string, Material> _mat = new Dictionary<string, Material>();
        int _level = -1;
        bool _headHidden;
        Color _primary = Color.white, _secondary = Color.white, _tertiary = Color.white;
        PlayerAppearance _appearance;
        bool _hasAppearance;

        public int Level { get { return _level; } }
        public PlayerAppearance Appearance { get { return _appearance; } }

        // ---- materials the lead needs for the jersey number system (PixLook.ApplyJersey(photo.JerseyMaterial, look); photo.ApplyLayout(photo.JerseyMaterial))
        public Material JerseyMaterial { get { return Mat("Jersey"); } }
        public Material PantsMaterial { get { return Mat("Pants"); } }
        public Material SocksMaterial { get { return Mat("Socks"); } }
        public Material HelmetMaterial { get { return Mat("Helmet_Shell"); } }

        public IEnumerable<Material> UniformMaterials
        {
            get { foreach (var k in new[] { "Jersey", "Pants", "Socks", "Cleats" }) { var m = Mat(k); if (m != null) yield return m; } }
        }

        void Awake() { Init(); }

        bool _inited;

        public void Init()
        {
            if (_inited) return;
            _inited = true;
            foreach (var p in parts) if (p != null && !string.IsNullOrEmpty(p.key)) _map[p.key] = p;
            if (library == null) library = PhotoPlayerLibrary.Load();
            if (library == null) return;
            // team dependent parts get their own instance (one per player, shared by every LOD of the part)
            Inst("Jersey", library.jersey); Inst("Pants", library.pants); Inst("Socks", library.socks); Inst("Cleats", library.cleats);
            Inst("SleeveR", library.sleeve); Inst("SleeveL", library.sleeve, "SleeveR");
            Inst("Wristband", library.band); Inst("PlayCard", library.band, "Wristband"); Inst("Towel", library.towel); Inst("Pouch", library.pouch);
            Inst("Helmet_Shell", library.helmet); Inst("FaceMask", library.faceMask); Inst("Chinstrap", library.padding); Inst("HelmetPad", library.padding, "Chinstrap");
            Inst("Visor", library.visor);
            Inst("Gloves", library.gloves);
            Inst("HeadBrows", library.hair); Inst("HeadBeard", library.hair); Inst("HeadScalp", library.hair);
            if (_map.ContainsKey("Eyes")) Inst("Eyes", library.eyes);
            // part -> material assignment
            foreach (var k in new[] { "Jersey", "Pants", "Socks", "Undersock", "Cleats", "SleeveR", "SleeveL", "Wristband", "PlayCard", "Towel", "Pouch", "Helmet_Shell", "FaceMask", "Chinstrap", "HelmetPad", "Visor", "HeadBrows", "HeadBeard", "HeadScalp", "Eyes" })
            {
                Part p; if (!_map.TryGetValue(k, out p)) continue;
                string mk = k == "Undersock" ? "Socks" : k;
                Material m; if (!_mat.TryGetValue(mk, out m)) continue;
                foreach (var r in p.lods) if (r != null) r.sharedMaterial = m;
            }
            // shadow casting: small parts do not need to cast shadows (22 players x several cascades)
            foreach (var k in new[] { "Eyes", "FaceMask", "Visor", "HeadBrows", "HeadBeard", "HeadScalp", "PlayCard", "Wristband", "Head", "Chinstrap", "HelmetPad", "Cleats", "Pouch", "Towel", "SleeveL", "SleeveR" })   // v0.6.0: 22 players x every part x 4 cascades was the biggest shadow cost; the shell, jersey, pants, socks and arms carry the silhouette
            {
                Part p; if (!_map.TryGetValue(k, out p)) continue;
                foreach (var r in p.lods) if (r != null) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true)) { smr.quality = SkinQuality.Bone4; smr.updateWhenOffscreen = false; }
            ApplyDefaultVisibility();
            SetupHelmetFrame();
            SetLevel(0);
        }

        // ---- helmet frame: PIX/Lit helmet mode places the stripe and the crest in "helmet space" (x right, y up, z forward, 1 = half extent of the shell) relative to the Helmet
        // holder under the head bone (same convention as PlayerAvatar.SetupHelmetFrame), so every helmet part that shares the material agrees.
        static readonly int[] HW = { Shader.PropertyToID("_PixHW0"), Shader.PropertyToID("_PixHW1"), Shader.PropertyToID("_PixHW2") };
        static readonly int[] HN = { Shader.PropertyToID("_PixHN0"), Shader.PropertyToID("_PixHN1"), Shader.PropertyToID("_PixHN2") };
        static readonly int HelmetDecalId = Shader.PropertyToID("_PixHelmetDecal"), DecalTexId = Shader.PropertyToID("_PixDecalTex");
        Transform _helmetHolder;
        Matrix4x4 _helmetBasis, _helmetNormalBasis;

        void SetupHelmetFrame()
        {
            _helmetHolder = null;
            var hm = Mat("Helmet_Shell");
            if (hm == null || headBone == null) return;
            _helmetHolder = headBone.Find("Helmet");
            var shell = P("Helmet_Shell");
            if (_helmetHolder == null || shell == null || shell.lods.Length == 0) { _helmetHolder = null; return; }
            var mf = shell.lods[0].GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) { _helmetHolder = null; return; }
            Matrix4x4 toHolder = _helmetHolder.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var mb = mf.sharedMesh.bounds;
            // the avatar axes (right / up / forward) expressed in holder space: forward from the pelvis (works for any facing of the model)
            Vector3 fwd = Vector3.forward, up = Vector3.up;
            if (animator != null && animator.isHuman)
            {
                var l = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var r = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
                if (l != null && r != null) { fwd = Vector3.Cross(r.position - l.position, Vector3.up); fwd.y = 0f; if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward; fwd.Normalize(); }
            }
            Vector3 right = Vector3.Cross(up, fwd);
            Matrix4x4 w2h = _helmetHolder.worldToLocalMatrix;
            Vector3 rh = w2h.MultiplyVector(right).normalized, uh = w2h.MultiplyVector(up).normalized, fh = w2h.MultiplyVector(fwd).normalized;
            Matrix4x4 rot = Matrix4x4.identity;
            rot.SetRow(0, new Vector4(rh.x, rh.y, rh.z, 0)); rot.SetRow(1, new Vector4(uh.x, uh.y, uh.z, 0)); rot.SetRow(2, new Vector4(fh.x, fh.y, fh.z, 0));
            _helmetNormalBasis = rot;
            Vector3 mn = new Vector3(1e9f, 1e9f, 1e9f), mx = -mn;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 q = rot.MultiplyPoint3x4(toHolder.MultiplyPoint3x4(c)); mn = Vector3.Min(mn, q); mx = Vector3.Max(mx, q);
            }
            Vector3 mid = (mn + mx) * 0.5f, half = Vector3.Max((mx - mn) * 0.5f, new Vector3(1e-6f, 1e-6f, 1e-6f));   // holder space can be tiny (the FBX bones carry a x100 unit scale): no absolute clamp
            _helmetBasis = Matrix4x4.Scale(new Vector3(1f / half.x, 1f / half.y, 1f / half.z)) * Matrix4x4.Translate(-mid) * rot;
            var look = PixLookAssets.Load();
            if (look != null) hm.SetVector(HelmetDecalId, look.helmetDecal);
            UpdateHelmetFrame();
        }

        void UpdateHelmetFrame()
        {
            var hm = Mat("Helmet_Shell");
            if (hm == null || _helmetHolder == null) return;
            Matrix4x4 m = _helmetBasis * _helmetHolder.worldToLocalMatrix;
            Matrix4x4 n = _helmetNormalBasis * _helmetHolder.worldToLocalMatrix;
            for (int i = 0; i < 3; i++) { hm.SetVector(HW[i], m.GetRow(i)); hm.SetVector(HN[i], n.GetRow(i)); }
        }

        void LateUpdate() { UpdateHelmetFrame(); }

        /// <summary>Team crest on both sides of the helmet (PixLook.CrestTexture(key) gives the texture).</summary>
        public void SetHelmetDecal(Texture crest) { var hm = Mat("Helmet_Shell"); if (hm != null && crest != null) hm.SetTexture(DecalTexId, crest); }

        void Inst(string key, Material template, string shareWith = null)
        {
            if (template == null) return;
            Material m;
            if (shareWith != null && _mat.TryGetValue(shareWith, out m)) { _mat[key] = m; return; }
            m = new Material(template) { name = template.name + "_" + gameObject.name };
            _mat[key] = m;
        }

        Material Mat(string key) { Init(); Material m; return _mat.TryGetValue(key, out m) ? m : null; }
        Part P(string key) { Part p; return _map.TryGetValue(key, out p) ? p : null; }

        void ApplyDefaultVisibility()
        {
            foreach (var k in new[] { "Visor", "HeadBeard", "HeadScalp", "SleeveL", "SleeveR", "Towel", "Pouch", "PlayCard", "Wristband" }) SetVisible(k, false);
        }

        // ------------------------------------------------------------------------------------------------ team colours
        /// <summary>Kit colours (LINEAR, exactly like PixFootball.Gameplay.TeamColors, so PlayerAvatar.SetColors(TeamColors) values can be passed straight through):
        /// jersey primary/secondary/tertiary, pants base = tertiary with primary stripe, socks like the jersey, helmet shell = primary.</summary>
        public void SetTeamColors(Color primary, Color secondary, Color tertiary)
        {
            Init();
            _primary = primary; _secondary = secondary; _tertiary = tertiary;
            SetPix(Mat("Jersey"), primary, secondary, tertiary);
            SetPix(Mat("Pants"), tertiary, primary, secondary);
            SetPix(Mat("Socks"), primary, secondary, tertiary);
            SetPix(Mat("Cleats"), Luma(tertiary) > 0.45f ? tertiary : new Color(0.9f, 0.9f, 0.9f).linear, secondary, new Color(0.04f, 0.04f, 0.04f).linear);
            SetPix(Mat("Helmet_Shell"), primary, secondary, tertiary);
            if (_hasAppearance) ApplyAccents();
        }

        /// <summary>Helmet shell colours when they differ from the jersey (primary shell, secondary stripe, tertiary outline).</summary>
        public void SetHelmetColors(Color primary, Color secondary, Color tertiary) { SetPix(Mat("Helmet_Shell"), primary, secondary, tertiary); }

        static void SetPix(Material m, Color p, Color s, Color t)
        {
            if (m == null) return;
            m.SetColor(Primary, p); m.SetColor(Secondary, s); m.SetColor(Tertiary, t);
        }

        static float Luma(Color linear) { return 0.2126f * linear.r + 0.7152f * linear.g + 0.0722f * linear.b; }

        /// <summary>Copies the jersey layout (number / name / wordmark placement on the PHR jersey UV) into a uniform material instance. Call after PixLook.ApplyJersey.</summary>
        public void ApplyLayout(Material m)
        {
            if (m == null || library == null) return;
            m.SetVector(NumFront, library.numberFront); m.SetVector(NumBack, library.numberBack);
            m.SetVector(NumShoulderL, library.numberShoulderL); m.SetVector(NumShoulderR, library.numberShoulderR);
            m.SetVector(NamePlace, library.namePlacement); m.SetVector(WordPlace, library.wordPlacement);
            var look = PixLookAssets.Load();
            if (look != null && look.letterAtlas != null) m.SetTexture(Letters, look.letterAtlas);
            Vector4 ti = m.GetVector(TextInfo);
            ti.y = library.textMaxWidth.x; ti.w = library.textMaxWidth.y;
            m.SetVector(TextInfo, ti);
        }

        // ------------------------------------------------------------------------------------------------ appearance
        static readonly Color[] HairColors =
        {
            new Color32(26, 22, 20, 255), new Color32(58, 40, 28, 255), new Color32(86, 58, 36, 255), new Color32(126, 92, 56, 255),
            new Color32(170, 136, 84, 255), new Color32(140, 62, 30, 255), new Color32(120, 120, 122, 255), new Color32(205, 205, 205, 255),
        };

        public void ApplyAppearance(PlayerAppearance a)
        {
            Init();
            if (library == null) return;
            a = a.Sanitized();
            _appearance = a; _hasAppearance = true;
            // head shape: convex mix of two of the twelve sculpted faces + the authored tweaks
            float t = a.HeadBlend / 100f;
            foreach (var key in new[] { "Head", "Eyes", "HeadBrows", "HeadBeard", "HeadScalp" })
            {
                var p = P(key); if (p == null) continue;
                foreach (var r in p.lods)
                {
                    var smr = r as SkinnedMeshRenderer; if (smr == null || smr.sharedMesh == null) continue;
                    var mesh = smr.sharedMesh;
                    int n = mesh.blendShapeCount;
                    if (n == 0) continue;
                    for (int i = 0; i < n; i++) smr.SetBlendShapeWeight(i, 0f);
                    SetShape(smr, "S" + (a.HeadA + 1).ToString("00"), 100f * (1f - t));
                    SetShape(smr, "S" + (a.HeadB + 1).ToString("00"), 100f * t);
                    SetShape(smr, "T_JawWide", a.JawWide); SetShape(smr, "T_ChinLong", a.ChinLong); SetShape(smr, "T_NoseBig", a.NoseBig);
                    SetShape(smr, "T_CheekFull", a.CheekFull); SetShape(smr, "T_BrowHeavy", a.BrowHeavy);
                }
            }
            // skin
            int tone = Mathf.Clamp(a.SkinTone, 0, PlayerAppearance.SkinToneCount - 1);
            Assign("Head", library.skinHead[tone]);
            var arms = P("Arms");
            if (arms != null)
            {
                var skinArm = library.skinArms[tone];
                var hands = a.Gloves ? Mat("Gloves") : skinArm;
                foreach (var r in arms.lods) if (r != null) r.sharedMaterials = new[] { skinArm, hands };
            }
            // eyes
            var eyes = Mat("Eyes");
            if (eyes != null && library.eyeHueSaturation != null && library.eyeHueSaturation.Length > a.EyeColor)
            {
                var hs = library.eyeHueSaturation[a.EyeColor];
                eyes.SetFloat(Hue, hs.x); eyes.SetFloat(Saturation, hs.y);
            }
            // hair
            Color hair = HairColors[Mathf.Clamp(a.HairColor, 0, HairColors.Length - 1)];
            SetHair("HeadBrows", library.brow, (int)a.Brows, hair, true);
            SetHair("HeadBeard", library.beard, !ShowFacialHair || a.FacialHair == FacialHair.None ? -1 : a.FacialHair == FacialHair.Stubble ? 0 : a.FacialHair == FacialHair.Goatee ? 1 : a.FacialHair == FacialHair.FullBeard ? 2 : 3, hair, ShowFacialHair && a.FacialHair != FacialHair.None);
            SetHair("HeadScalp", library.scalp, a.Scalp == ScalpStyle.Shaved ? -1 : (int)a.Scalp - 1, hair * 0.92f, a.Scalp != ScalpStyle.Shaved);
            // visor
            ApplyVisor(a.Visor);
            // kit accessories
            SetVisible("SleeveL", a.Sleeves == SleeveMode.Left || a.Sleeves == SleeveMode.Both);
            SetVisible("SleeveR", a.Sleeves == SleeveMode.Right || a.Sleeves == SleeveMode.Both);
            SetVisible("Wristband", a.Wristbands);
            SetVisible("PlayCard", a.PlayCard);
            SetVisible("Towel", a.Towel);
            SetVisible("Pouch", a.Pouch);
            ApplyAccents();
            ApplyVisibility();
        }

        static void SetShape(SkinnedMeshRenderer smr, string name, float weight)
        {
            if (Mathf.Approximately(weight, 0f)) return;
            int i = smr.sharedMesh.GetBlendShapeIndex(name);
            if (i >= 0) smr.SetBlendShapeWeight(i, weight);
        }

        void Assign(string key, Material m)
        {
            var p = P(key); if (p == null || m == null) return;
            foreach (var r in p.lods) if (r != null) r.sharedMaterial = m;
        }

        void SetHair(string key, Texture2D[] set, int index, Color tint, bool on)
        {
            var m = Mat(key); var p = P(key);
            if (p == null || m == null) return;
            bool show = on && index >= 0 && set != null && index < set.Length && set[index] != null;
            if (show) { m.SetTexture(BaseColorMap, set[index]); m.SetColor(BaseColor, tint.linear); }
            SetVisible(key, show);
        }

        Color Accent(AccentColor c)
        {
            switch (c)
            {
                case AccentColor.Black: return new Color(0.05f, 0.05f, 0.055f).linear;
                case AccentColor.Grey: return new Color(0.45f, 0.46f, 0.48f).linear;
                case AccentColor.TeamPrimary: return _primary;
                case AccentColor.TeamSecondary: return _secondary;
                case AccentColor.TeamTertiary: return _tertiary;
                default: return new Color(0.93f, 0.93f, 0.93f).linear;
            }
        }

        void ApplyAccents()
        {
            var a = _appearance;
            var sleeve = Mat("SleeveR"); if (sleeve != null) { var c = Accent(a.SleeveColor); SetPix(sleeve, c, c, c); }
            var band = Mat("Wristband"); if (band != null) { var c = Accent(a.WristColor); SetPix(band, c, c, c); }
            var towel = Mat("Towel"); if (towel != null) SetPix(towel, new Color(0.9f, 0.9f, 0.9f).linear, Color.white, Color.white);
            var pouch = Mat("Pouch"); if (pouch != null) SetPix(pouch, new Color(0.06f, 0.06f, 0.065f).linear, Color.white, Color.white);
            var gloves = Mat("Gloves");
            if (gloves != null)
            {
                var c = Accent(a.GloveColor);
                // back of the hand = glove colour, palm = a contrasting grip colour, cuff = team tertiary
                Color palm = Luma(c) > 0.3f ? new Color(0.12f, 0.12f, 0.13f).linear : new Color(0.85f, 0.85f, 0.85f).linear;
                SetPix(gloves, c, palm, a.GloveColor == AccentColor.TeamTertiary ? _primary : _tertiary);
            }
        }

        void ApplyVisor(VisorStyle v)
        {
            var m = Mat("Visor");
            SetVisible("Visor", v != VisorStyle.None && m != null);
            if (m == null) return;
            switch (v)
            {
                case VisorStyle.Smoke: m.SetColor(BaseColor, new Color(0.04f, 0.045f, 0.05f, 0.55f).linear); m.SetFloat(Smoothness, 0.95f); m.SetFloat(Metallic, 0f); break;
                case VisorStyle.Mirror: m.SetColor(BaseColor, new Color(0.55f, 0.62f, 0.72f, 0.78f).linear); m.SetFloat(Smoothness, 1f); m.SetFloat(Metallic, 1f); break;
                default: m.SetColor(BaseColor, new Color(0.82f, 0.9f, 1f, 0.16f).linear); m.SetFloat(Smoothness, 0.97f); m.SetFloat(Metallic, 0f); break;
            }
        }

        // ------------------------------------------------------------------------------------------------ level of detail
        void SetVisible(string key, bool on) { var p = P(key); if (p != null) p.visible = on; }

        public void SetPartVisible(string key, bool on) { Init(); SetVisible(key, on); ApplyVisibility(); }

        void ApplyVisibility()
        {
            int level = Mathf.Max(_level, 0);
            _level = -1;
            SetLevel(level);
        }

        public static int LevelFor(float distance, float near, float mid, float far)
        {
            float b = Mathf.Max(0.1f, Bias);
            if (distance < near * b) return 0;
            if (distance < mid * b) return 1;
            if (distance < far * b) return 2;
            return 3;
        }

        /// <summary>Activates the renderer of every visible part for the level (0 = hero ... 3 = far).</summary>
        public void SetLevel(int level)
        {
            Init();
            level = Mathf.Clamp(level, 0, 3);
            if (level == _level) return;
            _level = level;
            foreach (var p in parts)
            {
                if (p == null || p.lods == null || p.lods.Length == 0) continue;
                bool helmetPart = p.key == "Helmet_Shell" || p.key == "FaceMask" || p.key == "Chinstrap" || p.key == "HelmetPad" || p.key == "Visor";
                if (helmetPart && !manageHelmetLod && p.key != "Visor") { continue; }
                int pick = level < p.lods.Length ? level : (p.hideBeyond ? -1 : p.lods.Length - 1);
                for (int i = 0; i < p.lods.Length; i++)
                {
                    var r = p.lods[i]; if (r == null) continue;
                    bool on = p.visible && i == pick;
                    if (r.gameObject.activeSelf != on) r.gameObject.SetActive(on);
                }
            }
        }

        void Update()
        {
            if (!autoLod) return;
            var cam = ViewCamera != null ? ViewCamera : Camera.main;
            if (cam == null) return;
            SetLevel(LevelFor(Vector3.Distance(cam.transform.position, transform.position), near, mid, far));
        }

        /// <summary>First person / replay: hides the head, helmet and hair from the camera but keeps their shadow.</summary>
        readonly Dictionary<Renderer, ShadowCastingMode> _shadowMemo = new Dictionary<Renderer, ShadowCastingMode>();

        public void HideHead(bool hide)
        {
            if (_headHidden == hide) return;
            _headHidden = hide;
            foreach (var p in parts)
            {
                if (p == null || p.lods == null) continue;
                // the helmet camera sits inside the head: head, helmet and (first person) the player's own arms are hidden from the camera but keep their shadow
                bool headPart = p.key == "Head" || p.key == "Eyes" || p.key.StartsWith("Head") || p.key == "Helmet_Shell" || p.key == "FaceMask" || p.key == "Chinstrap" || p.key == "HelmetPad" || p.key == "Visor"
                               || p.key == "Arms" || p.key == "SleeveL" || p.key == "SleeveR" || p.key == "Wristband" || p.key == "PlayCard";
                if (!headPart) continue;
                foreach (var r in p.lods)
                {
                    if (r == null) continue;
                    ShadowCastingMode orig;
                    if (!_shadowMemo.TryGetValue(r, out orig)) { orig = r.shadowCastingMode; _shadowMemo[r] = orig; }
                    r.shadowCastingMode = hide ? ShadowCastingMode.ShadowsOnly : orig;
                }
            }
        }

        void OnDestroy()
        {
            var seen = new HashSet<Material>();
            foreach (var kv in _mat) if (kv.Value != null && seen.Add(kv.Value)) Destroy(kv.Value);
            _mat.Clear();
        }
    }
}
