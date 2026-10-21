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
        public Transform Head, RightHand, LeftFoot, RightFoot;
        public Material UniformInstance;
        readonly List<Renderer> _headRenderers = new List<Renderer>();
        bool _headHidden;

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
            string key = size == PlayerSize.Small ? "player_small" : size == PlayerSize.Large ? "player_large" : "player_medium";
            var prefab = lib != null ? lib.Prefab(key) : null;
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

            var rt = PixRuntimeAssets.Load();
            Material baseUniform = rt != null ? rt.uniformMaterial : null;
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                if (n.StartsWith("Torso") || n.StartsWith("Legs"))
                {
                    if (UniformInstance == null)
                    {
                        var src = baseUniform != null ? baseUniform : r.sharedMaterial;
                        UniformInstance = new Material(src) { name = "PIX_Uniform_Team" };
                        SetColors(colors);
                    }
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = UniformInstance;
                    r.sharedMaterials = mats;
                }
                if (n.StartsWith("Head") || n.StartsWith("Eyes")) _headRenderers.Add(r);
            }
            Head = Animator.GetBoneTransform(HumanBodyBones.Head);
            RightHand = Animator.GetBoneTransform(HumanBodyBones.RightHand);
            LeftFoot = Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            RightFoot = Animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (Head != null) foreach (var r in Head.GetComponentsInChildren<Renderer>(true)) if (!_headRenderers.Contains(r)) _headRenderers.Add(r);
        }

        public void SetVisualYaw(float offsetDegrees)
        {
            if (Root != null) Root.localRotation = Quaternion.Euler(0f, ModelYaw + offsetDegrees, 0f);
        }

        public void SetColors(TeamColors c)
        {
            if (UniformInstance == null) return;
            UniformInstance.SetColor(Primary, c.Primary);
            UniformInstance.SetColor(Secondary, c.Secondary);
            UniformInstance.SetColor(Tertiary, c.Tertiary);
        }

        /// <summary>Hides the head/helmet from the camera (first-person) but keeps their shadow.</summary>
        public void HideHead(bool hide)
        {
            if (_headHidden == hide) return;
            _headHidden = hide;
            foreach (var r in _headRenderers)
                if (r != null) r.shadowCastingMode = hide ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }
    }
}
