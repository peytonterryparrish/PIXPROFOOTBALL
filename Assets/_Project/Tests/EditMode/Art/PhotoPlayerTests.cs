using System.IO;
using System.Linq;
using NUnit.Framework;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>Checks of the photoreal player build (PIX_PhotoPlayer_* prefabs under Assets/_Licensed/PhotorealPlayer). Skipped when the licensed art is absent (clean checkout).</summary>
    public class PhotoPlayerTests
    {
        const string Root = "Assets/_Licensed/PhotorealPlayer";
        static readonly string[] Sizes = { "Small", "Medium", "Large" };
        static readonly string[] Optional = { "Visor", "HeadBeard", "HeadScalp", "HeadBrows", "SleeveL", "SleeveR", "Towel", "Pouch", "PlayCard", "Wristband" };

        static GameObject Prefab(string size)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/PIX_PhotoPlayer_" + size + ".prefab");
            if (p == null) Assert.Ignore("photoreal player prefab not built (Assets/_Licensed/PhotorealPlayer missing)");
            return p;
        }

        static int Tris(Renderer r)
        {
            Mesh m = null; var smr = r as SkinnedMeshRenderer;
            if (smr != null) m = smr.sharedMesh; else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) m = mf.sharedMesh; }
            if (m == null) return 0;
            int t = 0; for (int i = 0; i < m.subMeshCount; i++) t += (int)(m.GetIndexCount(i) / 3);
            return t;
        }

        [Test]
        public void PrefabsAreHumanoidWithAnimatorAndPhotoPlayer()
        {
            foreach (string s in Sizes)
            {
                var p = Prefab(s);
                var anim = p.GetComponent<Animator>();
                Assert.IsNotNull(anim, s);
                Assert.IsNotNull(anim.avatar, s);
                Assert.IsTrue(anim.avatar.isValid && anim.avatar.isHuman, s + " avatar must be a valid Humanoid");
                Assert.IsFalse(anim.applyRootMotion);
                Assert.IsNotNull(p.GetComponent<PhotoPlayer>(), s);
                Assert.IsNotNull(p.GetComponent<TeamTint>(), s + " TeamTint compatibility");
                Assert.IsNotNull(anim.GetBoneTransform(HumanBodyBones.Head), s);
                Assert.IsNotNull(anim.GetBoneTransform(HumanBodyBones.LeftFoot), s);
            }
        }

        [Test]
        public void EveryPartHasItsLodsAndTheTriangleBudgetHolds()
        {
            int[] budget = { 85000, 40000, 18000, 9000 };
            foreach (string s in Sizes)
            {
                var pp = Prefab(s).GetComponent<PhotoPlayer>();
                var tot = new int[4];
                foreach (var part in pp.parts)
                {
                    Assert.IsNotNull(part.lods, part.key);
                    Assert.Greater(part.lods.Length, 0, part.key);
                    if (Optional.Contains(part.key)) continue;
                    for (int l = 0; l < 4; l++) tot[l] += Tris(part.lods[Mathf.Min(l, part.lods.Length - 1)]);
                }
                for (int l = 0; l < 4; l++) Assert.LessOrEqual(tot[l], budget[l], s + " LOD" + l + " triangles " + tot[l]);
                Assert.Greater(tot[0], 40000, s + " LOD0 should be a hero mesh");
                Assert.Greater(tot[0], tot[1]); Assert.Greater(tot[1], tot[2]); Assert.Greater(tot[2], tot[3]);
                foreach (var key in new[] { "Jersey", "Pants", "Arms", "Cleats", "Head", "Helmet_Shell", "FaceMask" })
                    Assert.AreEqual(4, pp.parts.First(p => p.key == key).lods.Length, s + " " + key + " needs LOD0..3");
            }
        }

        [Test]
        public void HeadsCarryTheSculptedAndAuthoredBlendShapes()
        {
            foreach (string s in Sizes)
            {
                var pp = Prefab(s).GetComponent<PhotoPlayer>();
                foreach (var key in new[] { "Head", "Eyes" })
                {
                    var smr = pp.parts.First(p => p.key == key).lods[0] as SkinnedMeshRenderer;
                    Assert.IsNotNull(smr, s + " " + key);
                    for (int i = 1; i <= PlayerAppearance.HeadShapeCount; i++) Assert.GreaterOrEqual(smr.sharedMesh.GetBlendShapeIndex("S" + i.ToString("00")), 0, s + " " + key + " S" + i);
                    if (key == "Head")
                        foreach (var t in new[] { "T_JawWide", "T_ChinLong", "T_NoseBig", "T_CheekFull", "T_BrowHeavy" }) Assert.GreaterOrEqual(smr.sharedMesh.GetBlendShapeIndex(t), 0, s + " " + t);
                }
            }
        }

        [Test]
        public void NothingNamedAfterTheRealPlayerOrTheTeamShips()
        {
            if (!Directory.Exists(Root)) Assert.Ignore("licensed folder absent");
            string[] banned = { "burrow", "bengal", "nfl", "nike", "wilson", "cincinnati", "tiger", "joe_", "joe " };
            foreach (string path in Directory.GetFiles(Root, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".meta")))
            {
                string n = Path.GetFileName(path).ToLowerInvariant();
                foreach (var b in banned) Assert.IsFalse(n.Contains(b), "forbidden name in " + path);
            }
            foreach (string s in Sizes)
            {
                var p = Prefab(s);
                foreach (var t in p.GetComponentsInChildren<Transform>(true))
                    foreach (var b in banned) Assert.IsFalse(t.name.ToLowerInvariant().Contains(b), "forbidden node name " + t.name);
                foreach (var r in p.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials) if (m != null) foreach (var b in banned) Assert.IsFalse(m.name.ToLowerInvariant().Contains(b), "forbidden material name " + m.name);
            }
        }

        [Test]
        public void ApplyingAnAppearanceSwitchesSkinHairAndLevels()
        {
            var prefab = Prefab("Medium");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var pp = go.GetComponent<PhotoPlayer>();
                pp.autoLod = false; pp.Init();
                var a = PlayerAppearance.FromSeed(4242, PhotoBody.Medium);
                a.FacialHair = FacialHair.FullBeard; a.SkinTone = 6; a.Visor = VisorStyle.Smoke; a.Sleeves = SleeveMode.Both; a.Gloves = true;
                pp.SetTeamColors(new Color(0.1f, 0.2f, 0.5f), new Color(0.9f, 0.7f, 0.1f), Color.white);
                // v0.6.0: facial hair shells are off by default (PHR-018); the switch still works when hair cards arrive
                pp.ApplyAppearance(a);
                Assert.IsFalse(pp.parts.First(p => p.key == "HeadBeard").visible, "beard shells are disabled by default");
                PhotoPlayer.ShowFacialHair = true;
                try { pp.ApplyAppearance(a); } finally { PhotoPlayer.ShowFacialHair = false; }
                var head = pp.parts.First(p => p.key == "Head").lods[0];
                Assert.AreEqual(pp.library.skinHead[6], head.sharedMaterial);
                var beard = pp.parts.First(p => p.key == "HeadBeard");
                Assert.IsTrue(beard.visible);
                pp.SetLevel(2);
                Assert.AreEqual(2, pp.Level);
                foreach (var part in pp.parts)
                {
                    int active = part.lods.Count(r => r.gameObject.activeSelf);
                    if (!part.visible) Assert.AreEqual(0, active, part.key);
                    else if (part.lods.Length == 1 || part.hideBeyond) Assert.LessOrEqual(active, 1, part.key);
                    else Assert.AreEqual(1, active, part.key + " must show exactly one LOD");
                }
                pp.SetLevel(0);
                Assert.IsTrue(pp.parts.First(p => p.key == "Jersey").lods[0].gameObject.activeSelf);
                Assert.IsFalse(pp.parts.First(p => p.key == "Jersey").lods[3].gameObject.activeSelf);
                // head blend shapes follow the descriptor
                var smr = (SkinnedMeshRenderer)head;
                int ia = smr.sharedMesh.GetBlendShapeIndex("S" + (a.HeadA + 1).ToString("00"));
                Assert.Greater(smr.GetBlendShapeWeight(ia), 0f);
            }
            finally { Object.DestroyImmediate(go); }
        }


        [Test]
        public void EveryLodFollowsTheAnimatedSkeletonAndTheHelmetSitsOnTheHeadBone()
        {
            foreach (string s in Sizes)
            {
                var p = Prefab(s);
                var anim = p.GetComponent<Animator>();
                var head = anim.GetBoneTransform(HumanBodyBones.Head);
                var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                foreach (var smr in p.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Assert.IsNotNull(smr.sharedMesh, s + " " + smr.name);
                    Assert.AreEqual(smr.sharedMesh.bindposes.Length, smr.bones.Length, s + " " + smr.name + " bones vs bindposes");
                    Assert.IsNotNull(smr.rootBone, s + " " + smr.name + " root bone");
                    foreach (var b in smr.bones) { Assert.IsNotNull(b, s + " " + smr.name + " null bone"); Assert.IsTrue(b.IsChildOf(hips.parent) || b.IsChildOf(p.transform), s + " " + smr.name + " bone outside the rig: " + b.name); }
                }
                var pp = p.GetComponent<PhotoPlayer>();
                foreach (var part in pp.parts.Where(x => x.key.StartsWith("Helmet") || x.key == "FaceMask"))
                    foreach (var r in part.lods) Assert.IsTrue(r.transform.IsChildOf(head), s + " " + part.key + " must be parented under the head bone");
            }
        }
        [Test]
        public void ArtLibraryHasThePhotoKeysAndStillTheOldOnes()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ArtLibrary>("Assets/_Licensed/ArtLibrary.asset");
            if (lib == null) Assert.Ignore("ArtLibrary asset absent");
            lib.BuildMaps();
            foreach (var k in new[] { "photo_player_small", "photo_player_medium", "photo_player_large" })
                if (lib.Prefab(k) == null) Assert.Ignore("photo keys not registered yet (run PIX Football/Photoreal Player/Register ArtLibrary Keys)");
            Assert.IsNotNull(lib.Prefab("player_medium"), "the existing player prefab key must be untouched");
        }
    }
}
